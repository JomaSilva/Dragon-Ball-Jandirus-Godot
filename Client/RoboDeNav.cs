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

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
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

			case 19:
				PainelGrande();
				break;

			case 20:
				CartaGrande();
				_passos.Add($"  --     janela {DisplayServer.WindowGetSize()} | carta "
						  + $"{_mapa?.GetGlobalRect()} | rolagem {Rolagem?.GetGlobalRect()}");
				break;

			// ============================ UM PASSO ENQUADRA, O SEGUINTE FOTOGRAFA ============================
			// `GetTexture().GetImage()` devolve o quadro JA DESENHADO: pedir a foto no mesmo passo que
			// mexeu na camera fotografa o enquadramento ANTERIOR. Meio segundo entre um passo e outro e
			// muito mais do que o necessario, e o custo e um segundo por foto numa bancada que ja leva dez.
			// ============================================================================================
			case >= 21 when _fotos < Escalas.Length:
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
