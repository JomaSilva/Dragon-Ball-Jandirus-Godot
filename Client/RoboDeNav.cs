using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA CARTA ESTELAR (`--diagnav`).
///
/// ============================ O QUE SO UM TESTE RESPONDE ============================
/// Um mapa desenhado nao devolve nada: o `_Draw` pinta e acaba. Sem janela nao ha o que olhar, e
/// "o mapa mostra os planetas" continuaria sendo uma afirmacao minha ate alguem abrir o jogo.
///
/// As perguntas que importam e que so um numero responde:
///   * a aba Nav existe FORA do espaco? (ela passou a existir sempre, e isso e uma mudanca de regra)
///   * o cliente consegue enumerar planetas SOZINHO, sem pacote nenhum?
///   * os procedurais aparecem quando se aproxima -- e somem quando se afasta?
///   * quanto custa a varredura, de verdade, em milissegundos?
///   * clicar seleciona? viajar liga o piloto?
/// ====================================================================================
///
/// COMO RODAR:
///     Godot --headless --path . --host --diagnav --nome Piloto --conta piloto
/// </summary>
public partial class RoboDeNav : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private MapaEstelar? _mapa;
	private int _longe, _perto;
	private double _msDeVarredura;

	/// <summary>O que a camada de sistemas trouxe: a tela nova, e os numeros que ela produz.</summary>
	private TelaDoSistema? _tela;
	private SistemaSolar? _daTerra, _cheio;
	private int _sistemasLonge, _folhas;
	private double _msDeSistemas;

	/// <summary>A camera antes de o tempo passar -- o teste de que a aba nao se remonta sozinha.</summary>
	private float _escalaAntes;
	private Vector2 _centroAntes;
	private MapaEstelar? _mapaAntes;

	private static GameClient? C => GameClient.Instance;

	// =====================================================================
	// AS FOTOS DA CARTA (`--fotorotulo <nome>`)
	// =====================================================================
	/// <summary>
	/// AS QUATRO ESCALAS EM QUE A CARTA E FOTOGRAFADA, em pixels de tela por CELULA de sistema.
	///
	/// ============================ A GRADE SO APARECE NUMA FAIXA DE ZOOM ============================
	/// A reclamacao do dono e visual (*"senti q os sistemas estao mt certinhos"*), e uma reticula so se
	/// enxerga quando cabem muitas celulas na tela E cada uma ainda tem tamanho: com 200 px por celula
	/// ha tres pontos na tela e nao ha padrao pra ver; com 3 px por celula a magnitude limite corta 89%
	/// das estrelas e o que sobra e ralo demais pra formar linha. Por isso quatro escalas e nao uma --
	/// a foto que responde e a do meio, e as das pontas sao o controle que mostra que ela nao foi
	/// escolhida a dedo.
	///
	/// 48 -> ~28x17 celulas, e o zoom em que se escolhe destino vizinho.
	/// 20 -> ~69x41 celulas: e AQUI que linha e coluna apareciam. E a foto principal.
	/// 10 -> ~138x82 celulas, ainda com TODAS as estrelas desenhadas.
	///  7 -> ~197x117 celulas: a magnitude limite ja corta, e o que se julga e o VAZIO em grande escala.
	///
	/// O 7 e o mais aberto que cabe: abaixo dele o enquadramento passa de <see cref="MapaEstelar"/>
	/// `CelulasMax` (30.000) e a carta volta ao esqueleto de sete pontos -- que e um teto legitimo do
	/// widget, mas fotografa-lo nao diria nada sobre a grade.
	/// ==========================================================================================
	/// </summary>
	private static readonly float[] Escalas = [48f, 20f, 10f, 7f];

	/// <summary>
	/// ONDE A CAMERA PARA PRA FOTOGRAFAR -- longe da Terra, e isso importa.
	///
	/// Na origem os sete ANCORADOS mandam no quadro: eles nao sorteiam posicao (nascem onde o pre-feito
	/// esta) e passam por cima da magnitude limite, entao um enquadramento centrado ali mostraria
	/// justamente a parte do universo que esta mudanca NAO tocou. O ponto abaixo e a celula (60,40), a
	/// 4,7 milhoes de px da Terra: la so ha sistema gerado, que e o que o sorteio decide.
	///
	/// E ele e FIXO pras duas rodadas (antes e depois): a mesma seed, o mesmo centro e a mesma escala
	/// sao o que faz duas fotos serem comparaveis.
	/// </summary>
	private static readonly Vector2 PontoDaFoto = new(60 * (float)Sistemas.CelulaPx, 40 * (float)Sistemas.CelulaPx);

	/// <summary>
	/// O ROTULO DO ARQUIVO -- `--fotorotulo antes` / `--fotorotulo depois`.
	///
	/// A rodada "antes" e a MESMA build com as duas constantes de <see cref="Sistemas"/> nos valores
	/// velhos (margem = `RaioSistemaTeto`, `VaziosPor256` = 0). Nao ha caminho paralelo de desenho:
	/// as duas fotos saem do `_Draw` de producao, e e por isso que elas podem ser postas lado a lado.
	/// </summary>
	private string Rotulo
	{
		get
		{
			string[] a = OS.GetCmdlineArgs();
			int i = Array.IndexOf(a, "--fotorotulo");
			return i >= 0 && i + 1 < a.Length ? a[i + 1] : "carta";
		}
	}

	/// <summary>Quantas fotos ja sairam. E o indice em <see cref="Escalas"/>.</summary>
	private int _fotos;

	/// <summary>A camera ja foi posta na escala desta foto? Ver o par enquadra/fotografa nos passos.</summary>
	private bool _enquadrado;

	// =====================================================================
	// O ROTEIRO VISUAL DA RODA (`--diagnav` com janela)
	// =====================================================================
	/// <summary>
	/// Qual entrada do <see cref="RoteiroVisual"/> vem agora. Ver o `case 21` dos passos.
	///
	/// ============================ POR QUE UM ROTEIRO E NAO NUMEROS ============================
	/// Os passos 18 e 20 ja MEDEM a roda, e uma medida verde nao responde a reclamacao que o dono
	/// fez: ela e sobre a mao dele no mouse. "A escala caiu de 0,0012 pra 0,00096" nao diz se o
	/// mapa continuou legivel, se a estrela que ele estava mirando fugiu pro canto, nem se o mapa
	/// sumiu no fim do afastamento -- e as tres so se respondem OLHANDO.
	///
	/// Por isso o roteiro nunca chama `Zoom()`: cada entrada empurra roda de VERDADE pelo viewport
	/// (ver <see cref="RodaEm"/>) e a entrada seguinte fotografa. Sao duas entradas por foto porque
	/// `GetTexture().GetImage()` devolve o quadro JA DESENHADO -- fotografar no mesmo passo que
	/// mexeu na camera fotografa o enquadramento ANTERIOR.
	/// ========================================================================================
	/// </summary>
	private int _visuais;

	/// <summary>O ponto de tela (global) onde a cruz da foto fica, e onde a roda e empurrada.</summary>
	private Vector2 _ancoraDaBorda;

	/// <summary>Onde, no mundo, esta a estrela da borda -- a que tem que ficar DEBAIXO da cruz.</summary>
	private Vector2 _estrelaDaBorda;

	/// <summary>A escala e a rolagem de antes do gesto, pra legenda da foto seguinte.</summary>
	private float _escalaDoGesto;
	private int _rolagemDoGesto;

	/// <summary>
	/// A ESCALA EM QUE O ROTEIRO VISUAL COMECA, em px de tela por celula de sistema.
	///
	/// 48 e o zoom "de escolher vizinho" (ver <see cref="Escalas"/>): as estrelas tem tamanho de
	/// sobra pra alguem reconhecer NA FOTO que a mesma continua debaixo da cruz. Nas escalas abertas
	/// elas viram ponto de 2 px e a foto da ancora nao provaria nada.
	/// </summary>
	private const float PxDaFotoDaRoda = 48f;

	/// <summary>
	/// O ScrollContainer que RECORTA a carta. E ele, e nao a janela, que decide o que aparece.
	///
	/// A aba mora num `ScrollContainer`: o mapa pode ter 800 px de altura e so 400 estarem desenhados.
	/// Uma foto tirada pelo retangulo do mapa pegaria, abaixo do corte, o MUNDO que esta atras do menu
	/// -- foi exatamente o que a primeira tentativa produziu (meia carta e meio planeta roxo).
	/// </summary>
	private ScrollContainer? Rolagem
	{
		get
		{
			for (Node? p = _mapa?.GetParent(); p != null; p = p.GetParent())
				if (p is ScrollContainer sc) return sc;
			return null;
		}
	}

	/// <summary>
	/// ============================ TRES PASSOS PRA CARTA FICAR GRANDE, E POR QUE ELES SAO TRES ============================
	/// A carta em jogo mede 728x330 -- 36 por 16 celulas no zoom da foto principal. Da pra ver o campo,
	/// mas nao da pra JULGAR um padrao bidimensional: dezesseis fileiras e pouco pra o olho decidir se
	/// existe coluna ou se ele esta inventando uma.
	///
	/// So que o tamanho nao esta no mapa: ele vem de tres coisas encadeadas, e cada uma so sabe o
	/// tamanho novo no quadro DEPOIS da anterior -- a janela, o painel do menu (uma caixa de 760x580
	/// ancorada no meio da tela, que nao cresce com a janela) e enfim a altura minima do widget. Fazer
	/// as tres no mesmo passo deixa o layout resolvido so no fim e a primeira foto sai no tamanho velho.
	///
	/// NADA DISTO TOCA NO DESENHO: o `_Draw` da carta e o mesmo do jogo, e a escala esta cravada em
	/// pixels de tela por CELULA (ver <see cref="Escalas"/>), entao a carta maior mostra MAIS celulas
	/// e nao celulas diferentes. Aumentar so muda quanto do universo cabe na foto.
	/// ================================================================================================================
	/// </summary>
	private void JanelaGrande() => DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);

	/// <summary>O painel do menu passa a ocupar a janela inteira. Ver <see cref="JanelaGrande"/>.</summary>
	private void PainelGrande()
	{
		for (Node? p = _mapa?.GetParent(); p != null; p = p.GetParent())
			if (p is PanelContainer pc)
			{
				Vector2I j = DisplayServer.WindowGetSize();
				// O painel e ancorado no CENTRO (offsets simetricos), entao o que se ajusta e o
				// meio-lado. 30 px de respiro pra a moldura nao encostar na borda da tela.
				float mx = j.X / 2f - 30, my = j.Y / 2f - 30;
				pc.OffsetLeft = -mx; pc.OffsetRight = mx;
				pc.OffsetTop = -my; pc.OffsetBottom = my;
				return;
			}
	}

	/// <summary>A carta ocupa toda a area visivel da rolagem. Ver <see cref="JanelaGrande"/>.</summary>
	private void CartaGrande()
	{
		if (_mapa == null || Rolagem is not { } sc) return;
		// 120 px pra barra de zoom em cima e pro painel do destino embaixo -- sem isso a rolagem
		// aparece e a carta e recortada de novo, so que por outro motivo.
		_mapa.CustomMinimumSize = new Vector2(0, Math.Max(330f, sc.Size.Y - 120f));
	}

	/// <summary>
	/// Salva SO O RETANGULO DA CARTA em `user://carta-&lt;rotulo&gt;-&lt;n&gt;celula.png`.
	///
	/// Recorta em vez de salvar a tela inteira porque o resto da janela (barra de abas, avisos, painel
	/// do destino) e ruido que muda de tamanho entre uma rodada e outra -- e duas fotos com molduras
	/// diferentes se comparam pior que duas fotos do mesmo recorte.
	///
	/// Silenciosa no headless de proposito: la o `GetImage` devolve nada e isso e ESPERADO, nao um
	/// defeito. Falhar o teste por causa disso seria falhar por causa do modo de execucao.
	/// </summary>
	private void Fotografar(float pxPorCelula)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza)"); return; }

			string caminho = ProjectSettings.GlobalizePath(
				$"user://carta-{Rotulo}-{pxPorCelula:0}px.png");

			if (_mapa != null)
			{
				// O RETANGULO DA FOTO E A INTERSECAO de tres coisas, e as tres ja erraram sozinhas:
				// o mapa (o que se quer), a rolagem (o que esta DESENHADO dele) e a imagem (o que
				// existe). Sem a segunda a foto pega o mundo atras do menu; sem a terceira o
				// `GetRegion` estoura quando a janela e menor que o layout pede.
				Rect2 r = _mapa.GetGlobalRect();
				if (Rolagem is { } sc) r = r.Intersection(sc.GetGlobalRect());
				r = r.Intersection(new Rect2(0, 0, img.GetWidth(), img.GetHeight()));

				if (r.Size.X < 8 || r.Size.Y < 8)
				{ _passos.Add($"  --     sem foto: a carta ficou fora da tela ({r})"); return; }

				img = img.GetRegion(new Rect2I((int)r.Position.X, (int)r.Position.Y,
											   (int)r.Size.X, (int)r.Size.Y));
			}

			img.SavePng(caminho);
			_passos.Add($"  ok     foto {pxPorCelula:0} px/celula -> {caminho}");
		}
		catch (Exception e) { _passos.Add("  --     sem foto: " + e.Message); }
	}

	/// <summary>
	/// O QUE A FOTO MOSTRA, EM NUMERO -- a legenda dela.
	///
	/// A foto responde "parece grade?" e nao responde "quanto". Estes tres numeros sao os mesmos que a
	/// bancada `sistemas` mede no universo inteiro, so que restritos AO QUADRO que foi fotografado: sem
	/// eles, uma foto que parece boa pode estar boa por acaso do enquadramento.
	/// </summary>
	private void MedirOQuadro(float pxPorCelula)
	{
		if (_mapa is not { } m) return;

		List<SistemaSolar> vis = m.SistemasDeTeste();
		long celulas = m.CelulasDoEnquadramentoDeTeste;

		// VIZINHO MAIS PROXIMO dentro do quadro: media, desvio e o CV. CV baixo = espacamento igual =
		// reticula. Quadratico de proposito -- sao centenas de pontos, nao milhares.
		double soma = 0, soma2 = 0;
		int n = 0, noEixo = 0;
		for (int i = 0; i < vis.Count; i++)
		{
			double melhor = double.MaxValue, bx = 0, by = 0;
			for (int j = 0; j < vis.Count; j++)
			{
				if (i == j) continue;
				double dx = vis[j].Estrela.Pos.X - (double)vis[i].Estrela.Pos.X;
				double dy = vis[j].Estrela.Pos.Y - (double)vis[i].Estrela.Pos.Y;
				double d2 = dx * dx + dy * dy;
				if (d2 >= melhor) continue;
				melhor = d2; bx = dx; by = dy;
			}
			if (melhor == double.MaxValue) continue;

			double d = Math.Sqrt(melhor);
			soma += d; soma2 += d * d; n++;

			// VIES DE EIXO: o vizinho mais proximo cai a menos de 15 graus de um dos eixos? Num campo
			// isotropico isso da 33,3%; na reticula deu 96,6%.
			double ax = Math.Abs(bx), ay = Math.Abs(by);
			double menor = Math.Min(ax, ay), maior = Math.Max(ax, ay);
			if (maior > 0 && menor / maior <= 0.2679) noEixo++;   // tan(15 graus)
		}

		if (n == 0) return;
		double media = soma / n;
		double cv = Math.Sqrt(Math.Max(0, soma2 / n - media * media)) / media;

		_passos.Add($"  --     {pxPorCelula:0} px/celula: {vis.Count} estrelas em {celulas} celulas "
				  + $"({100.0 * vis.Count / Math.Max(1, celulas):0.0}% habitadas) | vizinho medio "
				  + $"{media:0} px, CV {cv:0.000} | no eixo {100.0 * noEixo / n:0.0}%");
	}

	// =====================================================================
	// A RODA DO MOUSE (`--diagnav`)
	// =====================================================================
	/// <summary>
	/// EMPURRA UMA RODA DE MOUSE DE VERDADE, pelo viewport, no ponto pedido.
	///
	/// ============================ POR QUE UM EVENTO E NAO UMA CHAMADA ============================
	/// Chamar `_mapa.Zoom()` provaria que o zoom funciona -- e o zoom SEMPRE funcionou. O defeito que
	/// o dono reclamou nao esta no zoom: esta em quem MAIS recebe o mesmo evento. Isso e uma pergunta
	/// sobre a SUBIDA do evento pela arvore de Controls, e a unica coisa que exercita a subida e um
	/// evento de verdade entrando pelo viewport.
	///
	/// Vai o aperto E a soltura porque o Godot mantem a mascara de botao apertado entre os dois: so o
	/// aperto deixaria a roda "presa" pro proximo passo da bancada.
	/// ==========================================================================================
	/// </summary>
	private void RodaEm(Vector2 global, bool paraCima)
	{
		foreach (bool apertado in new[] { true, false })
			GetViewport().PushInput(new InputEventMouseButton
			{
				ButtonIndex = paraCima ? MouseButton.WheelUp : MouseButton.WheelDown,
				Pressed = apertado,
				Position = global,
				GlobalPosition = global,
			});
	}

	/// <summary>
	/// UM PONTO DA ABA QUE NAO E MAPA -- e onde a rolagem tem que continuar viva.
	///
	/// O conserto da roda e o tipo de coisa que passa a valer pra tela inteira sem ninguem perceber, e
	/// a metade cara do pedido e justamente a que NAO se ve: fora do mapa a pagina rola como sempre
	/// rolou. Este ponto e o topo da area visivel da rolagem (o bloco de dominio, acima da carta), e a
	/// bancada afirma que ele esta fora dos dois mapas antes de usa-lo -- um ponto que por acaso caisse
	/// dentro da carta deixaria o teste verde sem testar nada.
	/// </summary>
	private Vector2? PontoForaDoMapa()
	{
		if (Rolagem is not { } sc) return null;
		Vector2 p = sc.GetGlobalRect().Position + new Vector2(8, 6);
		if (_mapa != null && _mapa.Visible && _mapa.GetGlobalRect().HasPoint(p)) return null;
		if (_tela != null && _tela.Visible && _tela.GetGlobalRect().HasPoint(p)) return null;
		return p;
	}

	/// <summary>
	/// O DEGRAU DE ZOOM MEDIDO EM CADA TELA -- e a checagem de que as duas sao a MESMA mao.
	///
	/// As duas telas do nav sao irmas na arvore e o dono passa de uma pra outra com duplo clique. Se
	/// uma andasse 1,25x por clique e a outra 1,6x, as duas passariam sozinhas em tudo que esta medido
	/// abaixo -- "afastou", "nao rolou", "parou no limite" -- e o manuseio ainda assim seria outro. So
	/// comparar os dois numeros pega isso.
	/// </summary>
	private readonly Dictionary<string, float> _degrau = [];

	/// <summary>
	/// A BANCADA DA RODA DE UMA TELA -- as cinco familias, na mesma ordem em que alguem perguntaria.
	///
	/// ============================ POR QUE UMA SO PRA AS DUAS TELAS ============================
	/// A carta e a tela do sistema nao compartilham classe nenhuma (uma desenha galaxia, a outra
	/// desenha orbitas), e a tentacao e medir a roda so onde o dono reclamou. Mas o defeito nao era da
	/// carta: era da regra do Godot que vale pra todo `Control` (`mouse_force_pass_scroll_events`).
	/// Uma bancada escrita duas vezes envelhece torta -- a segunda copia ganha menos familias que a
	/// primeira e ninguem percebe. Por isso as telas entram como delegados e a bateria e uma so.
	///
	/// AS CINCO FAMILIAS, E O QUE CADA UMA PEGA SOZINHA:
	///   A. a roda muda o zoom, nos dois sentidos, e um clique pra cada lado volta ao mesmo lugar.
	///   B. a roda sobre o mapa NAO rola a pagina -- a queixa literal do dono.
	///   C. um pixel FORA do mapa a pagina rola como sempre rolou -- sem isto o conserto vira prisao.
	///   D. o ponto sob o cursor CONTINUA sob o cursor -- A fica verde com o zoom no centro.
	///   E. os limites seguram, e no limite o mapa ainda serve pra alguma coisa.
	/// =========================================================================================
	/// </summary>
	/// <param name="mapa">O widget, pra achar o retangulo na tela e empurrar a roda dentro dele.</param>
	/// <param name="quem">O nome que sai no relatorio.</param>
	/// <param name="escala">A escala de zoom da tela, lida do proprio widget.</param>
	/// <param name="limites">Os dois fins da escada, lidos da PRODUCAO (ver `LimitesDeTeste`).</param>
	/// <param name="telaDe">Onde, na tela, este ponto do mundo esta desenhado. Conta da producao.</param>
	/// <param name="mundoDe">A volta: que ponto do mundo esta debaixo deste pixel.</param>
	/// <param name="alvo">O ponto do mundo que fica no meio do enquadramento de trabalho.</param>
	/// <param name="usavel">
	/// O QUE "O MAPA CONTINUA UTILIZAVEL" QUER DIZER, em numero -- e ele tem que falar do DESENHO.
	///
	/// A primeira versao perguntava so "o alvo responde ao clique?", e isso nao podia ficar vermelho
	/// nunca: os dois acertadores tem piso proprio (12 px na tela do sistema) e o alvo fica no ponto
	/// ancorado, a distancia ZERO do cursor -- ele responderia mesmo desenhado com meio pixel. Uma
	/// checagem que nao tem como reprovar e um enfeite. Por isso a pergunta virou "quanto disto ainda
	/// esta DESENHADO", que e o que decide se o jogador consegue fazer alguma coisa naquele zoom.
	/// </param>
	/// <param name="reenquadrar">Devolve a tela ao enquadramento de trabalho, entre uma familia e outra.</param>
	private void BancadaDaRoda(Control mapa, string quem, Func<float> escala, (float Min, float Max) limites,
							   Func<Vector2, Vector2> telaDe, Func<Vector2, Vector2> mundoDe,
							   Vector2 alvo, Func<(bool Ok, string Oque)> usavel, Action reenquadrar)
	{
		if (Rolagem is not { } sc) { Conferir(false, $"{quem}: sem ScrollContainer pra medir"); return; }
		if (!mapa.IsVisibleInTree() || mapa.GetGlobalRect().Size.X < 8)
		{ Conferir(false, $"{quem}: o mapa nao esta na tela ({mapa.GetGlobalRect()})"); return; }

		Rect2 r = mapa.GetGlobalRect();
		Vector2 meio = r.GetCenter();

		// ---------------- A e B: o zoom muda, a pagina nao ----------------
		// Elas andam JUNTAS de proposito: sao dois numeros do MESMO gesto, e o defeito do dono era
		// exatamente os dois ao mesmo tempo -- a escala mudava certo (o zoom sempre funcionou) E a
		// pagina descia 52 px no mesmo quadro. Medir so o primeiro teria ficado verde a reclamacao toda.
		reenquadrar();
		float e0 = escala();
		int r0 = sc.ScrollVertical;

		RodaEm(meio, false);
		float e1 = escala();
		Conferir(e1 < e0, $"{quem}: a roda pra baixo AFASTA ({e0:0.000000} -> {e1:0.000000})");
		Conferir(sc.ScrollVertical == r0,
			$"{quem}: e a pagina NAO desce junto (rolagem {r0} -> {sc.ScrollVertical}, teto {sc.GetVScrollBar().MaxValue:0})");

		// A ROLAGEM DA VOLTA SE COMPARA COM A DE AGORA, E NAO COM A DO COMECO -- e isso ja mordeu.
		// Com o vazamento injetado de proposito, a roda pra baixo levava a pagina de 0 pra 52 e a roda
		// pra cima trazia de volta pra 0: comparar com o comeco (0) dava VERDE justamente no gesto em
		// que a pagina andou. O que se pergunta e se ESTE gesto mexeu na pagina.
		int rAntesDaVolta = sc.ScrollVertical;
		RodaEm(meio, true);
		float e2 = escala();
		Conferir(e2 > e1, $"{quem}: a roda pra cima APROXIMA ({e1:0.000000} -> {e2:0.000000})");
		Conferir(sc.ScrollVertical == rAntesDaVolta,
			$"{quem}: e na volta a pagina tambem fica parada (rolagem {rAntesDaVolta} -> {sc.ScrollVertical})");

		// UM CLIQUE PRA CADA LADO VOLTA PRO MESMO ZOOM. Sem isto, "sobe" e "desce" com degraus
		// diferentes (1,25 contra 1,20, por exemplo) passariam nos dois primeiros: o dono e que
		// descobriria, empurrando a roda pra frente e pra tras e vendo o mapa derivar.
		Conferir(Mathf.Abs(e2 - e0) <= e0 * 0.001f,
			$"{quem}: um clique pra cada lado volta pro mesmo zoom ({e0:0.00000000} -> {e2:0.00000000})");
		_degrau[quem] = e2 / e1;

		// ---------------- C: FORA do mapa a pagina ainda rola ----------------
		// ============================ O JEITO MAIS PROVAVEL DE O CONSERTO QUEBRAR TUDO ============================
		// O `ScrollContainer` que o mapa parou de empurrar e o UNICO rolador vertical do menu P -- ele
		// rola Stats, Learning, Tech e todas as outras abas. Se o freio da roda tivesse sido posto NELE
		// (ou no `_pilha`, ou com um `MouseFilter` mais acima, ou desligando o `VerticalScrollMode`),
		// tudo continuaria dando zoom certinho e o menu inteiro teria ficado preso, calado. Esta e a
		// familia que separa "consertei a roda" de "prendi a pagina".
		// ======================================================================================================
		if (PontoForaDoMapa() is { } fora)
		{
			int rf = sc.ScrollVertical;
			float ef = escala();
			RodaEm(fora, false);
			Conferir(sc.ScrollVertical > rf,
				$"{quem}: FORA do mapa a pagina rola como sempre rolou ({rf} -> {sc.ScrollVertical}, "
			  + $"teto {sc.GetVScrollBar().MaxValue:0})");
			Conferir(escala() == ef,
				$"{quem}: e a roda fora do mapa nao mexe no zoom dele ({ef:0.00000000})");
			sc.ScrollVertical = rf;
		}
		else Conferir(false, $"{quem}: nao achei ponto da aba fora dos dois mapas pra testar a rolagem");

		// ---------------- D: o ponto sob o cursor continua sob o cursor ----------------
		// ============================ A FAMILIA QUE A FAMILIA A NAO PEGA ============================
		// Trocar `b.Position` por `Size / 2f` no braco da roda deixa A, B e C verdes e destroi o
		// manuseio: o jogador aponta um mundo, aproxima, e o mundo foge pra fora da tela. Por isso a
		// ancora e tomada LONGE do meio -- num ponto a 22% da largura -- e por isso a bancada AFIRMA
		// essa distancia antes de medir: uma ancora que calhasse no centro deixaria o teste verde com
		// o zoom centrado, que e exatamente o defeito que ele existe pra pegar.
		// =========================================================================================
		reenquadrar();
		var ancora = new Vector2(r.Position.X + r.Size.X * 0.22f, r.Position.Y + r.Size.Y * 0.62f);
		Conferir((ancora - meio).Length() > 60f,
			$"{quem}: a ancora do teste esta LONGE do meio ({(ancora - meio).Length():0} px -- perto do meio o teste nao separaria os dois zooms)");

		Vector2 sobOCursor = mundoDe(ancora);
		for (int i = 0; i < 3; i++) RodaEm(ancora, true);
		float desvio = (telaDe(sobOCursor) - ancora).Length();
		Conferir(desvio < 2f,
			$"{quem}: aproximando 3x, o ponto sob o cursor continua sob o cursor ({desvio:0.00} px de desvio)");

		for (int i = 0; i < 6; i++) RodaEm(ancora, false);
		float desvioLonge = (telaDe(sobOCursor) - ancora).Length();
		Conferir(desvioLonge < 2f,
			$"{quem}: e afastando 6x tambem ({desvioLonge:0.00} px de desvio)");

		// ---------------- E: os limites seguram, e o mapa sobrevive a eles ----------------
		// A roda e o unico zoom da tela do sistema, entao segurar a roda apertada ate o fim e um gesto
		// NORMAL e nao um caso de borda. As tres perguntas: parou no limite ESCRITO (e nao num numero
		// qualquer -- ver `LimitesDeTeste`), insistir nao passa dele, e la o mapa ainda serve.
		reenquadrar();
		Vector2 noAlvo = telaDe(alvo);   // a ancora mantem o alvo debaixo deste pixel a rodada inteira
		if (!r.HasPoint(noAlvo)) noAlvo = meio;

		for (int i = 0; i < 50; i++) RodaEm(noAlvo, false);
		float min = escala();
		Conferir(Mathf.Abs(min - limites.Min) <= limites.Min * 0.001f,
			$"{quem}: 50 cliques pra baixo param no limite de longe ({min:0.00000000} x {limites.Min:0.00000000})");
		for (int i = 0; i < 10; i++) RodaEm(noAlvo, false);
		Conferir(escala() == min, $"{quem}: e dez cliques a mais nao passam dele ({escala():0.00000000})");
		(bool okLonge, string oqueLonge) = usavel();
		Conferir(okLonge, $"{quem}: no limite de longe o mapa continua utilizavel -- {oqueLonge}");

		for (int i = 0; i < 50; i++) RodaEm(noAlvo, true);
		float max = escala();
		Conferir(Mathf.Abs(max - limites.Max) <= limites.Max * 0.001f,
			$"{quem}: 50 cliques pra cima param no limite de perto ({max:0.00000000} x {limites.Max:0.00000000})");
		for (int i = 0; i < 10; i++) RodaEm(noAlvo, true);
		Conferir(escala() == max, $"{quem}: e dez cliques a mais nao passam dele ({escala():0.00000000})");
		(bool okPerto, string oquePerto) = usavel();
		Conferir(okPerto, $"{quem}: no limite de perto o mapa continua utilizavel -- {oquePerto}");

		reenquadrar();
	}

	/// <summary>
	/// "A CARTA CONTINUA UTILIZAVEL": os sete mundos com mapa proprio ainda saem, e a Terra responde.
	///
	/// Os pre-feitos sao o ESQUELETO -- eles entram por fora das duas varreduras (a de planetas e a de
	/// estrelas), que desligam nos extremos de zoom. Se um dia alguem "otimizar" o zoom aberto e passar
	/// a gate-a-los tambem, o limite de longe vira uma tela preta: a escala segura, o mapa nao serve, e
	/// nenhuma checagem de zoom repara.
	/// </summary>
	private static Func<(bool, string)> UsavelNaCarta(MapaEstelar carta, PlanetaNoEspaco terra) => () =>
	{
		int mundos = carta.PlanetasDeTeste().Count;
		bool clique = carta.ClicarEm(terra);
		return (mundos >= 7 && clique,
				$"{mundos} mundos desenhados e o clique na Terra {(clique ? "responde" : "NAO responde")}");
	};

	/// <summary>
	/// "A TELA DO SISTEMA CONTINUA UTILIZAVEL": a estrela ainda tem disco na tela, e responde.
	///
	/// O numero e o RAIO DESENHADO, e nao o clique sozinho: o acertador tem piso proprio de 12 px (ver
	/// `CorpoEm`) e o alvo esta a distancia zero do cursor, entao o clique responderia com a estrela
	/// desenhada com meio pixel. E o piso de 9 px do desenho que faz o zoom mais aberto ainda mostrar
	/// alguma coisa -- se ele cair, o limite de longe vira um campo vazio.
	/// </summary>
	private static Func<(bool, string)> UsavelNoSistema(TelaDoSistema tela) => () =>
	{
		(float raio, _, _) = tela.DesenhoDaEstrelaDeTeste();
		bool clique = tela.ClicarNaEstrela();
		return (raio >= 2f && clique,
				$"a estrela desenha {raio:0.0} px de raio e {(clique ? "responde" : "NAO responde")} ao clique");
	};

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	// =====================================================================
	// AS FOTOS DA RODA
	// =====================================================================
	/// <summary>
	/// O PAINEL DO MENU P -- a moldura inteira, com abas, carta e o painel do destino.
	///
	/// O recorte das fotos da roda e ELE, e nao a carta (como no <see cref="Fotografar"/>): metade do
	/// que estas fotos precisam mostrar acontece FORA do mapa. "A pagina desceu junto?" so se ve se o
	/// texto em volta estiver na foto -- um recorte colado no mapa sairia parecido com a pagina parada
	/// e com a pagina rolada, porque o mapa desce junto com ela.
	/// </summary>
	private PanelContainer? Painel
	{
		get
		{
			for (Node? p = _mapa?.GetParent(); p != null; p = p.GetParent())
				if (p is PanelContainer pc) return pc;
			return null;
		}
	}

	/// <summary>
	/// Salva o painel do menu em `user://roda-&lt;nome&gt;.png`, com uma cruz opcional no ponto do cursor.
	///
	/// Silenciosa no headless pelo mesmo motivo do <see cref="Fotografar"/>: la o `GetImage` devolve
	/// nada, e isso e o modo de execucao e nao um defeito.
	/// </summary>
	private void FotoDaAba(string nome, Vector2? cruz = null)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add($"  --     sem foto ({nome}): headless nao renderiza"); return; }

			var r = new Rect2(0, 0, img.GetWidth(), img.GetHeight());
			if (Painel is { } pc) r = r.Intersection(pc.GetGlobalRect());
			if (r.Size.X < 8 || r.Size.Y < 8)
			{ _passos.Add($"  --     sem foto ({nome}): o painel ficou fora da tela ({r})"); return; }

			var corte = new Rect2I((int)r.Position.X, (int)r.Position.Y, (int)r.Size.X, (int)r.Size.Y);
			img = img.GetRegion(corte);
			if (cruz is { } c) Cruz(img, c - (Vector2)corte.Position);

			string caminho = ProjectSettings.GlobalizePath($"user://roda-{nome}.png");
			img.SavePng(caminho);
			_passos.Add($"  ok     foto {nome} -> {caminho}");
		}
		catch (Exception e) { _passos.Add($"  --     sem foto ({nome}): " + e.Message); }
	}

	/// <summary>
	/// A MIRA DO CURSOR, desenhada NA FOTO -- o mouse de verdade nao aparece no `GetImage`.
	///
	/// Ela tem BURACO NO MEIO de proposito: o que a foto precisa mostrar e o que esta DEBAIXO do
	/// cursor, e uma cruz cheia taparia justamente a estrela que se quer conferir.
	/// </summary>
	private static void Cruz(Image img, Vector2 p)
	{
		if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
		int cx = (int)p.X, cy = (int)p.Y, w = img.GetWidth(), h = img.GetHeight();

		void Ponto(int x, int y)
		{
			// 2x2 por ponto: a 1 px a linha some no meio das estrelas quando alguem olha a foto
			// inteira de uma vez, que e como ela vai ser olhada.
			for (int dx = 0; dx < 2; dx++)
				for (int dy = 0; dy < 2; dy++)
					if (x + dx >= 0 && y + dy >= 0 && x + dx < w && y + dy < h)
						img.SetPixel(x + dx, y + dy, Colors.Magenta);
		}

		for (int d = -20; d <= 20; d++)
		{
			if (Math.Abs(d) < 6) continue;   // o buraco
			Ponto(cx + d, cy);
			Ponto(cx, cy + d);
		}
	}

	/// <summary>A legenda de cada foto: a escala e a rolagem valem mais que a foto sozinha.</summary>
	private void Legenda(string oque, float escala) =>
		_passos.Add($"  --     {oque}: escala {escala:0.00000000} | rolagem {Rolagem?.ScrollVertical ?? -1}"
				  + $" (teto {Rolagem?.GetVScrollBar().MaxValue ?? -1:0})");

	/// <summary>
	/// O ROTEIRO VISUAL. Devolve TRUE quando acabou. Uma entrada por meio segundo -- ver <see cref="_visuais"/>.
	///
	/// A ordem responde, nesta ordem, as perguntas que alguem faria com o mouse na mao: aproximou?
	/// afastou? a pagina ficou parada? a estrela da BORDA continua debaixo do cursor? o mapa sobrevive
	/// aos dois limites de zoom? fora do mapa a pagina ainda rola? e na tela do sistema, tudo de novo?
	/// </summary>
	private bool RoteiroVisual()
	{
		if (_mapa is not { } carta) { _passos.Add("  --     sem carta: o roteiro visual nao roda"); return true; }
		Vector2 meio = carta.GetGlobalRect().GetCenter();

		switch (_visuais++)
		{
			// ---------- a roda na carta, no layout de jogo ----------
			case 0:
				// O PASSO 19 pos a carta na frente trocando `Visible` na mao, e aquilo NAO devolve a
				// barra de "+/- / ver tudo" (quem a esconde e a moldura, no evento). Fotografar assim
				// mostraria uma aba que o jogador nunca ve. Este e o mesmo botao "Voltar", e ele e
				// inocuo quando a carta ja esta na frente.
				_tela?.VoltarDeTeste();
				carta.EnquadrarDeTeste(PontoDaFoto, PxDaFotoDaRoda);
				return false;

			case 1:
				Legenda("01 carta, antes de tocar na roda", carta.EscalaDeTeste);
				FotoDaAba("01-carta-antes");
				return false;

			case 2:
				for (int i = 0; i < 3; i++) RodaEm(meio, true);
				return false;

			case 3:
				Legenda("02 tres cliques de roda PRA CIMA no meio da carta", carta.EscalaDeTeste);
				FotoDaAba("02-carta-aproximada");
				return false;

			case 4:
				for (int i = 0; i < 6; i++) RodaEm(meio, false);
				return false;

			case 5:
				Legenda("03 seis cliques PRA BAIXO", carta.EscalaDeTeste);
				FotoDaAba("03-carta-afastada");
				return false;

			// ---------- a estrela da BORDA: zoom no cursor x zoom no centro ----------
			case 6:
			{
				carta.EnquadrarDeTeste(PontoDaFoto, PxDaFotoDaRoda);

				// A ESTRELA MAIS A ESQUERDA que ainda esta inteira no quadro. E ela que separa os dois
				// zooms: com ancora no cursor ela fica parada; com ancora no centro ela caminha pra
				// fora da tela, porque o centro esta longe dela.
				Vector2 tam = carta.Size;
				SistemaSolar? melhor = null;
				float menorX = float.MaxValue;
				foreach (SistemaSolar s in carta.SistemasDeTeste())
				{
					Vector2 t = carta.TelaDeTeste(new Vector2(s.Estrela.Pos.X, s.Estrela.Pos.Y))
							  - carta.GetGlobalRect().Position;
					if (t.X < 30 || t.X > tam.X * 0.28f) continue;
					if (t.Y < 40 || t.Y > tam.Y - 40) continue;
					if (t.X >= menorX) continue;
					menorX = t.X; melhor = s;
				}

				if (melhor is not { } estrela)
				{ Conferir(false, "nao achei estrela perto da borda esquerda pra ancorar o zoom"); return false; }

				_estrelaDaBorda = new Vector2(estrela.Estrela.Pos.X, estrela.Estrela.Pos.Y);
				_ancoraDaBorda = carta.TelaDeTeste(_estrelaDaBorda);
				_passos.Add($"  --     04 a estrela da borda e '{estrela.NomeDaEstrela}', desenhada a "
						  + $"{menorX:0} px da borda esquerda (a carta tem {tam.X:0} px de largura)");
				return false;
			}

			case 7:
				FotoDaAba("04-borda-antes", _ancoraDaBorda);
				return false;

			case 8:
				for (int i = 0; i < 3; i++) RodaEm(_ancoraDaBorda, true);
				return false;

			case 9:
			{
				// A CRUZ VAI NO MESMO PIXEL DA FOTO ANTERIOR -- e esse o teste. Com zoom no centro, a
				// estrela teria caminhado pra fora e a cruz estaria apontando pro vazio.
				FotoDaAba("05-borda-depois", _ancoraDaBorda);

				Vector2 agora = carta.TelaDeTeste(_estrelaDaBorda);
				float desvio = (agora - _ancoraDaBorda).Length();
				Conferir(desvio < 2f,
					$"depois de aproximar, a estrela da borda continua debaixo do cursor ({desvio:0.00} px de desvio)");
				Legenda("05 aproximado NO CURSOR", carta.EscalaDeTeste);
				return false;
			}

			// ---------- os dois limites ----------
			case 10:
				for (int i = 0; i < 40; i++) RodaEm(meio, false);
				return false;

			case 11:
				Legenda("06 afastado ATE O LIMITE (40 cliques pra baixo)", carta.EscalaDeTeste);
				_passos.Add($"  --     06 no limite a carta mostra {carta.PlanetasDeTeste().Count} pre-feitos "
						  + $"e {carta.SistemasDeTeste().Count} sistemas");
				FotoDaAba("06-limite-afastado");
				return false;

			case 12:
				// APROXIMAR CENTRADO NA ESTRELA: sem isso o limite de perto cairia num pedaco vazio do
				// espaco e a foto sairia preta -- o que pareceria defeito sem ser.
				carta.IrPara(_estrelaDaBorda == Vector2.Zero ? PontoDaFoto : _estrelaDaBorda);
				for (int i = 0; i < 60; i++) RodaEm(meio, true);
				return false;

			case 13:
				Legenda("07 aproximado ATE O LIMITE (60 cliques pra cima)", carta.EscalaDeTeste);
				FotoDaAba("07-limite-aproximado");
				return false;

			// ---------- fora do mapa a pagina tem que rolar ----------
			case 14:
				carta.EnquadrarDeTeste(PontoDaFoto, PxDaFotoDaRoda);
				_rolagemDoGesto = Rolagem?.ScrollVertical ?? 0;
				return false;

			case 15:
				Legenda("08 a pagina antes de rolar (roda FORA do mapa)", carta.EscalaDeTeste);
				_escalaDoGesto = carta.EscalaDeTeste;
				FotoDaAba("08-pagina-antes", PontoForaDoMapa());
				return false;

			case 16:
				if (PontoForaDoMapa() is { } fora) for (int i = 0; i < 3; i++) RodaEm(fora, false);
				return false;

			case 17:
			{
				FotoDaAba("09-pagina-depois", PontoForaDoMapa());
				Legenda("09 depois de tres cliques FORA do mapa", carta.EscalaDeTeste);
				Conferir(carta.EscalaDeTeste == _escalaDoGesto,
					$"roda fora do mapa NAO mexe no zoom dele ({_escalaDoGesto:0.00000000} -> {carta.EscalaDeTeste:0.00000000})");
				if (Rolagem is { } sc) sc.ScrollVertical = _rolagemDoGesto;
				return false;
			}

			// ---------- e tudo de novo na tela do sistema ----------
			case 18:
				// PELO CAMINHO DE VERDADE (`AbrirSistema` dispara o mesmo evento do duplo clique), pra a
				// foto sair com a moldura que o jogador ve -- com a barra de "+/-" da carta escondida,
				// que e justamente o que deixa a roda como unico zoom desta tela.
				carta.EnquadrarDeTeste(Vector2.Zero, PxDaFotoDaRoda);
				return false;

			case 19:
				if (_daTerra is { } sol) Conferir(carta.AbrirSistema(sol), "abri a tela do sistema pelo duplo clique");
				else Conferir(false, "sem o sistema da Terra: a tela do sistema nao abre");
				return false;

			case 20:
				if (_tela is { } t0) Legenda("10 tela do sistema, antes da roda", t0.EscalaDeTeste);
				FotoDaAba("10-sistema-antes");
				return false;

			case 21:
				if (_tela is { } t1) for (int i = 0; i < 3; i++) RodaEm(t1.GetGlobalRect().GetCenter(), true);
				return false;

			case 22:
				if (_tela is { } t2) Legenda("11 tres cliques PRA CIMA na tela do sistema", t2.EscalaDeTeste);
				FotoDaAba("11-sistema-aproximado");
				return false;

			case 23:
				if (_tela is { } t3) for (int i = 0; i < 6; i++) RodaEm(t3.GetGlobalRect().GetCenter(), false);
				return false;

			case 24:
				if (_tela is { } t4) Legenda("12 seis cliques PRA BAIXO na tela do sistema", t4.EscalaDeTeste);
				FotoDaAba("12-sistema-afastado");
				return false;

			default:
				// DESFAZ: as quatro fotos de escala que vem depois sao da CARTA, e ela esta escondida
				// atras da tela do sistema desde a entrada 19.
				_tela?.VoltarDeTeste();
				return true;
		}
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || MenuJogo.Instancia is not { } menu) return;

		_t += delta;
		if (_t < 0.5) return;
		_t = 0;

		switch (_passo++)
		{
			case 0:
				if (cli.Atributos.Raca is not { Length: > 0 }) { _passo = 0; return; }
				// A SEED CHEGA NO LOGIN, e nao so ao decolar -- e ela que faz a carta funcionar em
				// terra firme. Se voltar a chegar so no espaco, este passo cai.
				Conferir(cli.SeedDoUniverso != 0, $"a seed do universo chega no login ({cli.SeedDoUniverso})");
				Conferir(!Espaco.EhEspaco(cli.Zone), "estou em TERRA FIRME (e onde a aba nao existia antes)");
				break;

			case 1:
			{
				Conferir(Array.IndexOf(menu.AbasDeTeste, "Nav") >= 0,
					"a aba Nav existe fora do espaco (a carta se consulta antes de decolar)");

				// EM TERRA, A POSICAO DE GALAXIA E A DO PLANETA -- e nao a do corpo. O spawn da
				// Terra e (7984, 8016): usar isso no mapa punha o jogador a 11 mil px da origem,
				// fora do raio 220 da propria Terra, com "voce esta aqui" apontando pro vazio.
				Vector2? g = MapaEstelar.MinhaPosicaoNaGalaxia();
				Vector2? corpo = World.Instancia?.PosicaoLocal;
				Conferir(g is { } gg && gg.Length() < 1f,
					$"em terra, a posicao de galaxia e a do planeta ({g}), e nao a do corpo ({corpo})");
				break;
			}

			case 2:
				menu.Abrir();
				menu.IrPara("Nav");
				_mapa = menu.MapaDeTeste;
				Conferir(_mapa != null, "a aba Nav monta o mapa sem quebrar");
				break;

			case 3:
			{
				if (_mapa == null) { Conferir(false, "sem mapa: o resto nao da pra medir"); _passo = 99; break; }
				// LONGE: so o esqueleto (os sete com mapa proprio).
				_mapa.VerTudo();
				_longe = _mapa.PlanetasDeTeste().Count;
				Conferir(_longe >= 7, $"no zoom aberto aparecem os mundos pre-feitos ({_longe})");
				Conferir(!_mapa.VendoProcedurais, "no zoom aberto a varredura procedural fica DESLIGADA");
				break;
			}

			case 4:
			{
				if (_mapa == null) break;
				// PERTO: aproxima da Terra e conta de novo. O cliente faz isso SOZINHO -- nenhum
				// pacote de rede foi pedido entre um passo e outro.
				_mapa.IrPara(Vector2.Zero);
				for (int i = 0; i < 40 && !_mapa.VendoProcedurais; i++) _mapa.Zoom(1.25f);

				// A PRIMEIRA PASSADA ESQUENTA O CACHE e nao entra na conta: ela paga a varredura
				// inteira, e isso acontece uma vez. O que roda a 60 Hz e a SEGUNDA -- e e ela que
				// precisa caber num quadro. Medir as duas juntas seria reportar o pior caso como
				// se fosse o normal.
				_mapa.PlanetasDeTeste();
				ulong t0 = Time.GetTicksUsec();
				_perto = _mapa.PlanetasDeTeste().Count;
				_msDeVarredura = (Time.GetTicksUsec() - t0) / 1000.0;

				Conferir(_mapa.VendoProcedurais, $"aproximando, a varredura liga (escala {_mapa.EscalaDeTeste:0.0000})");
				Conferir(_perto > _longe,
					$"o cliente enumera planetas sozinho: {_longe} longe -> {_perto} perto, sem um byte de rede");
				Conferir(_msDeVarredura < 8, $"a varredura cabe num quadro ({_msDeVarredura:0.00} ms)");
				break;
			}

			case 5:
			{
				if (_mapa == null) break;
				// CLICAR: pega um planeta que esta na tela e clica onde ele esta desenhado.
				List<PlanetaNoEspaco> lista = _mapa.PlanetasDeTeste();
				PlanetaNoEspaco alvo = lista.Find(p => p.Premade && p.Nome == "Earth");
				Conferir(_mapa.ClicarEm(alvo), $"clicar no desenho de {alvo.Nome} seleciona ele");
				Conferir(_mapa.Selecionado?.Nome == alvo.Nome, "a selecao e a que foi clicada");
				break;
			}

			case 6:
			{
				// VIAJAR EM TERRA NAO LIGA O PILOTO. E a regra que o botao apagado promete.
				Conferir(World.Instancia?.DestinoDoPiloto == null,
					"em terra firme o piloto NAO liga (viajar e coisa do espaco)");

				// A CAMERA SOBREVIVE A ANDAR. A assinatura da aba carregava a direcao do corpo, que
				// e reescrita a cada pacote de input -- virar pra esquerda com o menu aberto
				// remontava a pagina e criava um mapa novo, jogando fora o zoom e o arrasto.
				_mapa!.IrPara(new Vector2(12345, -6789));
				_mapa.Zoom(1.6f);
				_escalaAntes = _mapa.EscalaDeTeste;
				_centroAntes = _mapa.CentroDeTeste;
				_mapaAntes = _mapa;
				break;
			}

			case 7:
				// Um quadro depois (varios pacotes de ficha ja chegaram, e o robo "andou"): a camera
				// tem que estar onde eu deixei, e tem que ser o MESMO no.
				Conferir(ReferenceEquals(menu.MapaDeTeste, _mapaAntes),
					"o mapa nao foi recriado por um pacote de ficha");
				Conferir(_mapa!.EscalaDeTeste == _escalaAntes && _mapa.CentroDeTeste == _centroAntes,
					"o zoom e o arrasto sobrevivem aos pacotes de ficha");

				// Sobe pro espaco pra provar o outro lado.
				C?.SendHabilidade("decolar");   // decolar e HABILIDADE, nao verb (GameServer.Raciais.cs:157)
				break;

			case 8:
				Conferir(Espaco.EhEspaco(cli.Zone), "decolei: estou no espaco");
				break;

			case 9:
			{
				if (_mapa == null) break;
				// O mapa foi remontado junto com a aba (a zona mudou, e a assinatura leva a zona).
				menu.IrPara("Nav");
				_mapa = menu.MapaDeTeste;
				if (_mapa == null) { Conferir(false, "o mapa sumiu depois de decolar"); break; }
				_mapa.VerMim();
				List<PlanetaNoEspaco> lista = _mapa.PlanetasDeTeste();
				PlanetaNoEspaco alvo = lista.Find(p => p.Nome == "Namek");
				_mapa.ClicarEm(alvo);
				World.Instancia?.Pilotar(new Vector2(alvo.Pos.X, alvo.Pos.Y));
				break;
			}

			case 10:
				Conferir(World.Instancia?.DestinoDoPiloto != null,
					"no espaco, viajar LIGA o piloto automatico");
				World.Instancia?.SoltarPiloto();
				Conferir(World.Instancia?.DestinoDoPiloto == null, "o botao Parar desliga o piloto");
				break;

			case 11:
			{
				// ============================ O PONTINHO E UM SISTEMA ============================
				// No zoom aberto a carta mostrava SO os sete pre-feitos e o resto era preto. Agora
				// ela mostra a galaxia inteira em estrelas, e este e o passo que prova a diferenca:
				// o MESMO enquadramento em que a varredura de PLANETAS esta desligada.
				// ==============================================================================
				if (_mapa == null) break;
				_mapa.VerTudo();

				// A VARREDURA DE SISTEMAS RODA A CADA VEZ QUE A CAMERA ANDA -- ou seja, a cada quadro
				// de um arrasto. Medir o custo NAO e opcional aqui: sao dezenas de milhares de
				// celulas, e a diferenca entre 1 ms e 10 ms e a diferenca entre arrastar o mapa e ver
				// o mapa engasgar.
				ulong t0 = Time.GetTicksUsec();
				_sistemasLonge = _mapa.SistemasDeTeste().Count;
				_msDeSistemas = (Time.GetTicksUsec() - t0) / 1000.0;

				Conferir(!_mapa.VendoProcedurais, "no zoom aberto os PLANETAS continuam desligados");
				Conferir(_mapa.VendoSistemas, "no MESMO zoom os SISTEMAS estao ligados");
				Conferir(_sistemasLonge > 100,
					$"a carta desenha a galaxia em estrelas ({_sistemasLonge} sistemas), e nao 7 pontos");
				Conferir(_msDeSistemas < 8,
					$"a varredura de sistemas cabe num quadro ({_msDeSistemas:0.00} ms)");

				// ============================ A MAGNITUDE LIMITE TEM QUE DISPARAR ============================
				// Ela e o que impede a carta de tentar desenhar vinte mil discos por quadro. Se um
				// dia ela parar de cortar, nada quebra: o mapa so fica lento -- e lentidao nao falha
				// bancada nenhuma. Por isso o corte e AFIRMADO, e nao suposto.
				// =========================================================================================
				Conferir(_mapa.MagnitudeDeTeste > 360f,
					$"no zoom aberto so as estrelas grandes entram (>= {_mapa.MagnitudeDeTeste:0} px de raio)");
				Conferir(_sistemasLonge < 4000,
					$"e por isso a contagem fica manejavel ({_sistemasLonge}, alvo ~1500)");
				Conferir(_mapa.SistemasDeTeste().Exists(s => s.Ancorado),
					"o sistema da Terra passa por cima da magnitude (ele e o esqueleto do mapa)");

				// O TETO TEM QUE DISPARAR (regra 0.7): teto que nunca dispara e teto nenhum. E ele e
				// o de LEGIBILIDADE (px por celula), que nao depende do tamanho da janela.
				for (int i = 0; i < 60 && _mapa.VendoSistemas; i++) _mapa.Zoom(1f / 1.25f);
				Conferir(!_mapa.VendoSistemas,
					$"o teto dispara ao afastar: {Sistemas.CelulaPx * _mapa.EscalaDeTeste:0.0} px por celula");
				Conferir(_mapa.SistemasDeTeste().Count == 0, "e ai a carta volta ao esqueleto");
				break;
			}

			case 12:
			{
				if (_mapa == null) break;
				// A ARTE. Sem ela a estrela vira um disco chapado -- e some CALADA se alguem
				// esquecer de rodar o `estrelas` do AssetPipeline.
				_folhas = ArteDeEstrela.FolhasLidas();
				Conferir(_folhas == 32, $"as 32 folhas de estrela estao na tabela ({_folhas})");

				// O SISTEMA DA TERRA, pelo caminho do mouse. Ele e ANCORADO: a estrela nasceu a
				// partir do planeta, e nao o contrario.
				_mapa.VerTudo();
				_mapa.IrPara(Vector2.Zero);
				for (int i = 0; i < 40 && !_mapa.VendoProcedurais; i++) _mapa.Zoom(1.25f);

				if (Sistemas.Em(cli.SeedDoUniverso, new Vec2(0, 0)) is not { } sol)
				{ Conferir(false, "a celula da Terra devia ter sistema"); break; }

				_daTerra = sol;
				Conferir(sol.Ancorado, "o sistema da Terra e ANCORADO (a estrela e que se mudou)");
				Conferir(sol.Estrela.Classe == ClasseDeEstrela.Amarela,
					$"a Terra orbita uma estrela amarela ({sol.Estrela.Classe})");
				Conferir(_mapa.ClicarNoSistema(sol), "clicar no desenho do sistema da Terra seleciona ele");
				break;
			}

			case 13:
			{
				// DUPLO CLIQUE ABRE A TELA. E o pedido do dono, e o unico passo que prova que o
				// evento chega na moldura e que a moldura troca as duas telas.
				if (_mapa == null || _daTerra is not { } sol) break;
				Conferir(_mapa.AbrirSistema(sol), "duplo clique no sistema pede a tela dele");

				_tela = menu.SistemaDeTeste;
				Conferir(_tela is { Visible: true }, "a tela do sistema aparece");
				Conferir(!_mapa.Visible, "e a carta da galaxia some (elas nao se sobrepoem)");
				break;
			}

			case 14:
			{
				if (_tela == null) { Conferir(false, "sem tela do sistema: o resto nao da pra medir"); break; }
				if (_tela.SistemaDeTeste is not { } s) { Conferir(false, "a tela abriu vazia"); break; }

				Conferir(s.Id == _daTerra?.Id, "a tela abriu no sistema que foi clicado");

				// A TERRA TEM QUE ESTAR LA DENTRO, e no lugar EXATO onde ela sempre esteve. Se um dia
				// a orbita passar a ser RECALCULADA em vez de devolvida literal, este passo cai antes
				// de o universo inteiro andar debaixo do jogo.
				bool achouTerra = false;
				for (int k = 0; k < s.Orbitas; k++)
				{
					PlanetaNoEspaco p = s.Planeta(k);
					if (p.Nome == "Earth") achouTerra = p.Pos.X == 0 && p.Pos.Y == 0;
				}
				Conferir(achouTerra, "a Terra esta na tela do sistema, em (0,0), bit a bit");

				// ============================ A PROVA QUE A ASSINATURA NAO DA ============================
				// `SistemasBench` avisa: a assinatura do Core fica verde com a estrela matando 37%
				// fora de onde parece, porque nao ha pixel nenhum dentro dela. Aqui o numero existe:
				// o nucleo da folha desenhada tem que dar exatamente o raio letal.
				// =====================================================================================
				(float raio, float folha, float nucleo) = _tela.DesenhoDaEstrelaDeTeste();
				float nucleoDesenhado = folha * nucleo / 2f;
				Conferir(nucleo > 0.5f && nucleo < 0.75f,
					$"o nucleo da folha veio MEDIDO do arquivo ({nucleo:0.000} do meio-lado)");
				Conferir(Mathf.Abs(nucleoDesenhado - raio) < 0.01f,
					$"o nucleo desenhado casa com o raio letal ({nucleoDesenhado:0.00} px x {raio:0.00} px)");
				break;
			}

			case 15:
			{
				if (_tela?.SistemaDeTeste is not { } s) break;

				// CLICAR DENTRO DA TELA: cada orbita responde onde ela esta desenhada.
				int responderam = 0;
				for (int k = 0; k < s.Orbitas; k++) if (_tela.ClicarNaOrbita(k)) responderam++;
				Conferir(responderam == s.Orbitas,
					$"todas as {s.Orbitas} orbitas respondem ao clique ({responderam})");

				Conferir(_tela.ClicarNaEstrela(), "a estrela tambem responde ao clique");
				Conferir(_tela.Selecionado == null, "com a estrela selecionada nao ha PLANETA de destino");

				_tela.ClicarNaOrbita(0);
				Conferir(_tela.Selecionado != null, "clicar num mundo devolve destino pro painel de baixo");
				break;
			}

			case 16:
			{
				// UM SISTEMA CHEIO. O maximo e 10 orbitas, e uma tela vista so com as 4 do ancorado
				// da Terra nao prova que dez cabem nem que dez respondem.
				if (_tela == null) break;
				for (int cx = 1; cx < 600 && _cheio == null; cx++)
					if (Sistemas.Do(cli.SeedDoUniverso, cx, 0) is { Orbitas: 10 } s) _cheio = s;

				if (_cheio is not { } gordo)
				{ Conferir(false, "nao achei sistema de 10 mundos em 600 celulas"); break; }

				_tela.Mostrar(gordo);
				int responderam = 0;
				for (int k = 0; k < gordo.Orbitas; k++) if (_tela.ClicarNaOrbita(k)) responderam++;
				Conferir(responderam == 10, $"um sistema de 10 mundos cabe e responde inteiro ({responderam})");

				// E A VIAGEM SAI DE DENTRO DA TELA -- o mesmo caminho do duplo clique.
				PlanetaNoEspaco alvo = gordo.Planeta(0);
				World.Instancia?.Pilotar(new Vector2(alvo.Pos.X, alvo.Pos.Y));
				break;
			}

			case 17:
				Conferir(World.Instancia?.DestinoDoPiloto != null,
					"viajar de dentro da tela do sistema liga o piloto");
				World.Instancia?.SoltarPiloto();
				break;

			case 18:
				// A RODA NA TELA DO SISTEMA -- e ela vem ANTES da carta de proposito: os passos 13 a 16
				// deixaram esta tela visivel, e um Control escondido nao recebe evento nenhum. Trocar a
				// visibilidade e medir no mesmo quadro tambem nao serve: o VBox so redistribui o espaco
				// no proximo, e o retangulo do irmao que acabou de reaparecer ainda e o antigo (zero).
				//
				// Aqui a roda nao e conforto: quando esta tela abre, `MenuJogo` esconde a barra de "+/-"
				// (ela e da carta) e a barra propria desta tela nao tem botao de zoom -- sem a roda
				// limpa nao ha como aproximar de um planeta.
				// O local existe pro lambda nao capturar o CAMPO anulavel (o compilador nao leva a
				// checagem de nulo pra dentro da closure).
				//
				// O ALVO E A ESTRELA, em local (0,0): ela e o unico corpo que existe em TODA escala
				// desta tela (os mundos saem do quadro no zoom fechado), entao e ela que responde a
				// pergunta "no limite o mapa ainda serve pra alguma coisa?" nas duas pontas.
				if (_tela is { } tela)
					BancadaDaRoda(tela, "tela do sistema", () => tela.EscalaDeTeste,
								  TelaDoSistema.LimitesDeTeste, tela.TelaDeTeste, tela.LocalDeTeste,
								  Vector2.Zero, UsavelNoSistema(tela), tela.Reenquadrar);
				else Conferir(false, "sem tela do sistema: a roda dela nao da pra medir");
				break;

			case 19:
				// AS FOTOS DA CARTA, quando ha renderizador de verdade.
				//
				// `--headless` usa o renderizador de mentira: `GetImage()` volta vazio ou nulo, e
				// por isso a foto e um EXTRA e nao um passo que pode falhar. Rodando com janela
				// (`--diagnav` sem `--headless`), ela e a unica prova visual que um teste consegue
				// produzir de um widget que so existe enquanto desenha.
				//
				// A CARTA DA GALAXIA TEM QUE ESTAR NA FRENTE: os passos 13 a 16 deixaram a TELA DO
				// SISTEMA visivel por cima dela. Fotografar sem desfazer isso renderia quatro fotos
				// de um sistema so -- verdes, salvas, e sobre outro assunto.
				if (_tela != null) _tela.Visible = false;
				if (_mapa != null) _mapa.Visible = true;
				JanelaGrande();
				break;

			case 20:
			{
				// A RODA NA CARTA, no layout NORMAL da aba -- antes de `PainelGrande`/`CartaGrande`.
				//
				// Aqueles dois esticam a carta ate quase encher a area visivel, e e justamente ai que a
				// pagina quase para de ter o que rolar: medir a rolagem num enquadramento sem sobra
				// deixaria "a pagina nao desceu" verde por falta de pagina, e nao por causa do conserto.
				// O layout de jogo tem 736 px de sobra medidos, e e nele que o dono reclamou.
				//
				// O ALVO E A TERRA, o pre-feito em (0,0): os sete pre-feitos sao os unicos corpos que a
				// carta desenha em QUALQUER escala (a varredura procedural e a de estrelas desligam nos
				// extremos), entao e ele que responde "no limite o mapa ainda serve" nas duas pontas.
				if (_mapa is { } carta)
				{
					PlanetaNoEspaco terra = carta.PlanetasDeTeste().Find(p => p.Premade && p.Nome == "Earth");
					BancadaDaRoda(carta, "carta estelar", () => carta.EscalaDeTeste,
								  MapaEstelar.LimitesDeTeste, carta.TelaDeTeste, carta.MundoDeTeste,
								  new Vector2(terra.Pos.X, terra.Pos.Y), UsavelNaCarta(carta, terra),
								  () => carta.EnquadrarDeTeste(Vector2.Zero, PxDaFotoDaRoda));
				}

				// ============================ E AS DUAS TELAS TEM QUE SER A MESMA MAO ============================
				// Agora que os dois degraus foram MEDIDOS (um em cada bateria), da pra perguntar a unica
				// coisa que nenhuma das duas responde sozinha: se o mouse do dono se comporta igual nas
				// duas telas. Ver `_degrau`.
				// ============================================================================================
				if (_degrau.TryGetValue("carta estelar", out float dCarta)
					&& _degrau.TryGetValue("tela do sistema", out float dTela))
					Conferir(Mathf.Abs(dCarta - dTela) < 0.001f,
						$"as duas telas do nav dao o MESMO degrau por clique ({dCarta:0.000}x x {dTela:0.000}x)");
				else Conferir(false, "faltou medir o degrau de uma das duas telas");
				break;
			}

			// ============================ AS FOTOS DA RODA VEM ANTES DE ESTICAR A ABA ============================
			// Aqui a aba ainda esta no layout de JOGO (a carta com 728x330 dentro do painel de 760x580),
			// que e onde o dono poe a mao no mouse. Os passos a seguir esticam painel e carta pra
			// fotografar a GRADE de estrelas, e naquele enquadramento a rolagem quase nao tem sobra --
			// a foto "a pagina ficou parada" sairia verde por falta de pagina.
			//
			// O roteiro re-arma o proprio passo: cada entrada dele custa os mesmos 0,5 s dos outros
			// passos, porque a foto e o quadro JA desenhado e ela precisa de um quadro depois do gesto.
			// ==================================================================================================
			case 21:
				if (!RoteiroVisual()) _passo = 21;
				break;

			case 22:
				PainelGrande();
				break;

			case 23:
				CartaGrande();
				_passos.Add($"  --     janela {DisplayServer.WindowGetSize()} | carta "
						  + $"{_mapa?.GetGlobalRect()} | rolagem {Rolagem?.GetGlobalRect()}");
				break;

			// ============================ UM PASSO ENQUADRA, O SEGUINTE FOTOGRAFA ============================
			// `GetTexture().GetImage()` devolve o quadro JA DESENHADO: pedir a foto no mesmo passo que
			// mexeu na camera fotografa o enquadramento ANTERIOR. Meio segundo entre um passo e outro e
			// muito mais do que o necessario, e o custo e um segundo por foto numa bancada que ja leva dez.
			// ============================================================================================
			case >= 24 when _fotos < Escalas.Length:
			{
				float px = Escalas[_fotos];
				if (!_enquadrado)
				{
					_mapa?.EnquadrarDeTeste(PontoDaFoto, px);
					_enquadrado = true;
					break;
				}
				MedirOQuadro(px);
				Fotografar(px);
				_fotos++;
				_enquadrado = false;
				break;
			}

			default:
				_acabou = true;
				menu.Fechar();
				GD.Print("\n[nav] ===== BANCADA DA CARTA ESTELAR =====");
				foreach (string l in _passos) GD.Print("[nav] " + l);
				GD.Print($"[nav]   planetas: {_longe} no zoom aberto, {_perto} aproximado "
						 + $"| varredura {_msDeVarredura:0.00} ms");
				GD.Print($"[nav]   sistemas: {_sistemasLonge} no zoom aberto em {_msDeSistemas:0.00} ms | {_folhas} folhas de estrela"
						 + $" | Terra em '{_daTerra?.NomeDaEstrela ?? "?"}' ({_daTerra?.Orbitas ?? 0} mundos)"
						 + $" | maior aberto: {_cheio?.Orbitas ?? 0} mundos");
				GD.Print(_falhas.Count == 0
					? "[nav] ===== TUDO OK ====="
					: $"[nav] ===== {_falhas.Count} FALHA(S) =====\n[nav]   " + string.Join("\n[nav]   ", _falhas));
				break;
		}
	}
}
