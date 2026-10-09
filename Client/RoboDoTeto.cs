using Godot;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO TETO (`--diagteto`). Roda com `--host` e JANELA.
///
/// A queixa era uma FOTO: nas fotos da `--diagcarga` nevava e chovia DENTRO do Banco da Terra, a
/// sala onde todo Human nasce, e a neblina fechava a sala como fechava a rua. No DM aquilo e uma
/// area `Inside`, e sobre ela nada do clima e desenhado (ver `Core/World/CelulaInterna.cs`).
///
/// ============================ O QUE ELE MEDE, E COMO ============================
/// O que cada camada do clima POE NA TELA, pixel a pixel. Com a arvore pausada ele tira QUATRO
/// fotos: a cena com a camada, sem ela, sem ela de novo, e com ela de volta. A diferenca entre a
/// primeira e a segunda e a camada; as outras duas sao a PENEIRA -- pixel em que as duas fotos "sem"
/// discordam, ou as duas "com", se mexeu sozinho apesar da pausa (tile animado anda pelo relogio do
/// renderizador) e fica fora da conta. E o metodo da prova do corpo da `--diagcarga`, com uma foto
/// a mais: so com tres, o que mudasse sozinho entre a primeira e a segunda passava por camada.
///
/// Cada pixel mudado cai numa celula do mapa, e a celula e "sob teto" ou "ao ar livre". **QUEM DIZ
/// QUAL E O SERVIDOR** (`GameServer.CelulaSobTetoDeTeste`), e nao o cliente: o cliente desenha com a
/// copia dele do plano, e julgar o desenho com essa mesma copia ficaria verde com as duas erradas
/// do mesmo jeito.
///
/// SAO DUAS CAMADAS (o que cai e a massa) EM DUAS POSICOES (dentro e fora do predio):
///   * DENTRO e a queixa: a sala inteira na tela, e nenhum pixel dela pode mudar;
///   * FORA e o contra-exemplo no mesmo quadro: tem que nevar no chao aberto (senao "zero pixel
///     sobre o predio" ficaria verde com o clima desligado) e nao pode nevar sobre o telhado.
///
/// O PIXEL NA DIVISA DE DUAS CELULAS NAO E JULGADO: a conta do robo e a do shader arredondam a
/// mesma divisa por caminhos diferentes, e um pixel de diferenca ali nao e defeito de ninguem.
/// ==================================================================================
///
/// ============================ E AS PORTAS DE ENTRADA ============================
/// As quatro medidas de cima NASCEM na situacao (o corpo e posto dentro, ou fora). O que elas nao
/// exercitam sao as transicoes, e ha tres:
///
///   * ANDAR. A celula de um pixel sai da camera, e a camera anda com o corpo. O robo sai do berco
///     pela porta com a tecla, e a cada quadro DESENHADO compara o canto de tela que os shaders
///     receberam com o canto que a camera tem naquele instante: tem que ser o mesmo. Com o defeito
///     injetado (`ClimaNaTela.TetoAtrasadoDeTeste`: a posicao escrita um quadro antes) a diferenca
///     aparece -- e a mascara escorregando sobre o predio.
///   * QUEBRAR. A celula interna que cai volta pro lado de fora (`NewTurfs.dm:13-17`). O robo abre
///     um buraco no piso do Banco pelo funil de producao do servidor e mede que passa a nevar NELE
///     e so nele.
///   * CONSERTAR. O verb de admin que refaz o cenario fecha o buraco -- e o teto tem que fechar
///     junto, nas duas pontas. A bancada do admin so conferia que a LISTA do estrago zerava.
///
/// E os dois defeitos do Core sao ligados no meio da rodada (`CelulaInterna.SemPlanoDeTeste`,
/// `CaidaContinuaInternaDeTeste`): com cada um a medida de producao tem que dar o resultado errado.
/// Nessas duas pernas o juiz do pixel e a resposta que o servidor deu ANTES de o defeito ligar --
/// o Core e um so pros dois lados deste processo, e o defeito mudaria a resposta do juiz junto.
/// ===============================================================================
///
/// ============================ E A LUZ DE DENTRO ============================
/// Sob teto nao escurece -- pra quem esta sob teto (`Iluminacao.ALuzDeDentro`; o dono escolheu por
/// foto entre tres desenhos, 2026-10-08). Aqui a medida e outra: a COR MEDIA de um bloco de piso
/// liso, comparada com a mesma sala travada no tom de dia. De madrugada e na tempestade as duas tem
/// que bater; da rua, a fileira da porta do Banco tem que ficar no escuro; e na caverna o chao tem
/// que clarear. Os dois defeitos da `Iluminacao` (`SemLuzDeDentroDeTeste`, que e o jogo de antes, e
/// `LuzDeDentroAcesaDaRuaDeTeste`, que e o desenho que o dono recusou) sao ligados no meio.
/// As fotos saem como `teto-luz-...png`.
/// ===========================================================================
///
/// Cada medida deixa duas fotos em `user://`: a cena (`teto-N-...png`) e o RETRATO do que foi
/// contado (`...-retrato.png`: branco = camada sobre ar livre, vermelho = camada sobre celula sob
/// teto, amarelo = mexeu sozinho, azulado = as celulas sob teto).
///
/// COMO RODAR (janela no segundo monitor, muda, APPDATA desviado):
///     Godot --path . --position 1920,0 --audio-driver Dummy --resolution 1280x720 --host --rede 7932
///           --diagteto --semfoco --raca Human --conta &lt;NOVA&gt; --nome RoboDoTeto
/// </summary>
public partial class RoboDoTeto : Node
{
	private static readonly ZoneKey Terra = ZoneKey.Premade("Earth");

	// O BANCO DA TERRA, em celula do port -- as mesmas do `GameServer.TetoTeste.cs`.
	private const int BercoCx = 73, BercoCy = 260;   // o `/obj/SpawnPoint`: piso, sob teto
	private const int ForaCx = 73, ForaCy = 264;     // dois passos ao sul da porta: ar livre

	private const int SoleiraCx = 73, SoleiraCy = 263;   // o degrau da porta: ar livre, colado na parede

	/// <summary>
	/// O bloco de piso que a perna do buraco derruba: 4x2 celulas de chao LIVRE a oeste do berco. A
	/// fileira de cima (y=259) e a do balcao e das estantes -- derruba-la sumiria com a mobilia, e a
	/// foto do buraco passaria a ser a foto de outra coisa.
	/// </summary>
	private const int BuracoX0 = 68, BuracoY0 = 260, BuracoLargura = 4, BuracoAltura = 2;

	private const int T = ZoneCollision.TileSize;

	private static Vector2 Corpo(int cx, int cy) =>
		new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];
	private bool _acabou;
	private int _passo;
	private double _t, _espera;
	private const double EsperaMaxima = 40.0;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string s) => _passos.Add("  --     " + s);
	private void Passar() { _passo++; _t = 0; }

	public override void _Ready()
	{
		// O robo anda com a arvore PAUSADA: e ele quem a pausa pra fotografar.
		ProcessMode = ProcessModeEnum.Always;
		RenderingServer.FramePostDraw += DepoisDeDesenhar;
	}

	public override void _ExitTree()
	{
		RenderingServer.FramePostDraw -= DepoisDeDesenhar;
		Soltar();
	}

	/// <summary>Devolve tudo o que a rodada pode ter deixado ligado -- inclusive se ela abortar no meio.</summary>
	private void Soltar()
	{
		Input.ActionRelease(Teclas.NomeNoMotor("move_down"));
		CelulaInterna.SemPlanoDeTeste = false;
		CelulaInterna.CaidaContinuaInternaDeTeste = false;
		ClimaNaTela.TetoAtrasadoDeTeste = false;
		Iluminacao.MeioDiaDeTeste = false;
		Iluminacao.SemLuzDeDentroDeTeste = false;
		Iluminacao.LuzDeDentroAcesaDaRuaDeTeste = false;
	}

	// =====================================================================
	// A MEDIDA: o que uma camada poe na tela
	// =====================================================================
	private enum Fase { Nao, Pausar, LerCom, LerSem, LerSemDeNovo, LerComDeNovo }

	/// <summary>O que a camada mudou na tela, separado pela celula em que cada pixel cai.</summary>
	private readonly record struct Medida(
		int MudouSobTeto, int MudouAoArLivre, int MudouNasMarcadas, int MexeuSozinho, int NaDivisa,
		int PixelsSobTeto, int PixelsAoArLivre);

	private Fase _fase = Fase.Nao;
	private CanvasItem? _camada;
	private int _minNiveis;
	private string _nomeDaMedida = "";
	private Image? _com, _sem, _semDeNovo, _comDeNovo;
	private Vector2 _centroDaMedida, _mundoDaMedida;
	private Medida? _medida;

	/// <summary>
	/// O JUIZ GUARDADO: as celulas que o servidor disse estarem sob teto ANTES de um defeito ligar.
	/// Nulo = perguntar ao servidor na hora (o caso de todas as medidas sem defeito).
	/// </summary>
	private HashSet<(int, int)>? _tetoGuardado;

	/// <summary>Celulas contadas a parte (o buraco no piso). Vazio = nenhuma.</summary>
	private readonly HashSet<(int, int)> _marcadas = [];

	/// <summary>
	/// Comeca a medir <paramref name="camada"/>. O resultado aparece em <see cref="_medida"/> quatro
	/// quadros depois; ate la o `_Process` so anda a maquina de fotos.
	/// </summary>
	/// <param name="minNiveis">
	/// Quantos niveis (de 255) um canal precisa mudar pro pixel contar. Floco e risco de chuva mudam
	/// dezenas; a massa mistura a cena com um quase-preto a ~10% e muda poucos. Com a arvore pausada
	/// as duas fotos "sem" saem identicas byte a byte, entao o piso de ruido e zero.
	/// </param>
	private void ComecarMedida(CanvasItem camada, int minNiveis, string nome)
	{
		_camada = camada;
		_minNiveis = minNiveis;
		_nomeDaMedida = nome;
		_medida = null;
		_com = _sem = _semDeNovo = _comDeNovo = null;
		_fase = Fase.Pausar;
	}

	private void AndarNaMedida(World mundo, GameServer servidor)
	{
		switch (_fase)
		{
			case Fase.Pausar:
				// A PAUSA POSTA AQUI VALE JA PRO QUADRO QUE VAI SER DESENHADO (medido na `--diagcarga`):
				// para script, sprite e `GpuParticles2D`. A camera e lida agora porque e com ESTA que
				// os tres quadros vao sair.
				GetTree().Paused = true;
				_centroDaMedida = mundo.CentroDaCameraDeTeste;
				_mundoDaMedida = GetViewport().GetVisibleRect().Size / Math.Max(mundo.ZoomDaCameraDeTeste, 0.01f);
				_fase = Fase.LerCom;
				return;

			case Fase.LerCom:
				_com = GetViewport()?.GetTexture()?.GetImage();        // o quadro pausado, com a camada
				if (_camada != null) _camada.Visible = false;
				_fase = Fase.LerSem;
				return;

			case Fase.LerSem:
				_sem = GetViewport()?.GetTexture()?.GetImage();        // sem a camada
				_fase = Fase.LerSemDeNovo;
				return;

			case Fase.LerSemDeNovo:
				_semDeNovo = GetViewport()?.GetTexture()?.GetImage();  // sem a camada de novo: meia peneira
				if (_camada != null) _camada.Visible = true;
				_fase = Fase.LerComDeNovo;
				return;

			case Fase.LerComDeNovo:
				// COM A CAMADA DE VOLTA: a outra metade da peneira. Com a arvore pausada a camada volta
				// identica (particula parada, uniforme do veu parado), entao o que diferir da primeira
				// foto se mexeu sozinho -- ver o `Julgar`.
				_comDeNovo = GetViewport()?.GetTexture()?.GetImage();
				GetTree().Paused = false;
				_fase = Fase.Nao;
				_medida = Julgar(servidor);
				return;
		}
	}

	/// <summary>As celulas sob teto num retangulo generoso em volta do Banco, pela resposta do servidor de AGORA.</summary>
	private static HashSet<(int, int)> GuardarOTeto(GameServer servidor)
	{
		var sob = new HashSet<(int, int)>();
		for (int cy = BercoCy - 20; cy <= BercoCy + 20; cy++)
			for (int cx = BercoCx - 30; cx <= BercoCx + 30; cx++)
				if (servidor.CelulaSobTetoDeTeste(Terra, cx, cy)) sob.Add((cx, cy));
		return sob;
	}

	private Medida? Julgar(GameServer servidor)
	{
		if (_com is not { } com || _sem is not { } sem || _semDeNovo is not { } semD || _comDeNovo is not { } comD
			|| com.IsEmpty() || sem.IsEmpty() || semD.IsEmpty() || comD.IsEmpty())
		{
			Nota($"{_nomeDaMedida}: sem foto (headless nao renderiza) -- NAO foi medido");
			return null;
		}

		int w = com.GetWidth(), h = com.GetHeight();
		if (sem.GetWidth() != w || sem.GetHeight() != h || semD.GetWidth() != w || semD.GetHeight() != h
			|| comD.GetWidth() != w || comD.GetHeight() != h)
		{
			Nota($"{_nomeDaMedida}: as quatro fotos nao tem o mesmo tamanho -- NAO foi medido");
			return null;
		}

		// OS BYTES, e nao `GetPixel`: sao 921 mil pixels em quatro fotos.
		com.Convert(Image.Format.Rgb8); sem.Convert(Image.Format.Rgb8);
		semD.Convert(Image.Format.Rgb8); comD.Convert(Image.Format.Rgb8);
		byte[] a = com.GetData(), b = sem.GetData(), d = semD.GetData(), e = comD.GetData();

		// ONDE CADA PIXEL ESTA NO MUNDO. A mesma conta do `ClimaNaTela`: a janela cobre `tela / zoom`
		// de mundo em volta do centro da camera.
		Vector2 origem = _centroDaMedida - _mundoDaMedida * 0.5f;
		float pxPorMundoX = w / _mundoDaMedida.X, pxPorMundoY = h / _mundoDaMedida.Y;

		// O QUE O JUIZ DIZ DE CADA CELULA DA TELA, uma vez por celula e nao por pixel.
		int cx0 = (int)MathF.Floor(origem.X / T), cy0 = (int)MathF.Floor(origem.Y / T);
		int nx = (int)MathF.Floor((origem.X + _mundoDaMedida.X) / T) - cx0 + 1;
		int ny = (int)MathF.Floor((origem.Y + _mundoDaMedida.Y) / T) - cy0 + 1;
		var teto = new bool[nx * ny];
		var marcada = new bool[nx * ny];
		for (int j = 0; j < ny; j++)
			for (int i = 0; i < nx; i++)
			{
				teto[j * nx + i] = _tetoGuardado?.Contains((cx0 + i, cy0 + j))
								   ?? servidor.CelulaSobTetoDeTeste(Terra, cx0 + i, cy0 + j);
				marcada[j * nx + i] = _marcadas.Contains((cx0 + i, cy0 + j));
			}

		var retrato = new byte[w * h * 3];
		int mudouTeto = 0, mudouLivre = 0, mudouMarcadas = 0, mexeu = 0, divisa = 0, pxTeto = 0, pxLivre = 0;
		for (int y = 0; y < h; y++)
		{
			float my = origem.Y + (y + 0.5f) / pxPorMundoY;
			int cy = (int)MathF.Floor(my / T);
			float fy = my / T - cy;
			bool divisaY = Math.Min(fy, 1 - fy) * T * pxPorMundoY < 1.5f;
			for (int x = 0; x < w; x++)
			{
				int k = (y * w + x) * 3;
				float mx = origem.X + (x + 0.5f) / pxPorMundoX;
				int cx = (int)MathF.Floor(mx / T);
				float fx = mx / T - cx;
				int ci = cx - cx0, cj = cy - cy0;
				bool naGrade = ci >= 0 && cj >= 0 && ci < nx && cj < ny;
				bool sobTeto = naGrade && teto[cj * nx + ci];

				// O FUNDO DO RETRATO: a cena sem a camada, escurecida, com as celulas sob teto azuladas.
				retrato[k] = (byte)(b[k] * 35 / 100);
				retrato[k + 1] = (byte)(b[k + 1] * 35 / 100);
				retrato[k + 2] = (byte)Math.Min(255, b[k + 2] * 35 / 100 + (sobTeto ? 70 : 0));

				if (divisaY || Math.Min(fx, 1 - fx) * T * pxPorMundoX < 1.5f) { divisa++; continue; }
				if (sobTeto) pxTeto++; else pxLivre++;

				// A PENEIRA TEM DUAS METADES: "sem" contra "sem de novo", e "com" contra "com de novo". So
				// com a primeira, o que mudasse sozinho exatamente entre a foto COM e a foto SEM passava
				// por obra da camada -- e o berco do Banco tem um tile animado debaixo do corpo: numa
				// rodada ele trocou de quadro nesse intervalo e pos 360 px de "neblina" dentro da sala.
				int ruido = Math.Max(Math.Abs(b[k] - d[k]), Math.Max(Math.Abs(b[k + 1] - d[k + 1]), Math.Abs(b[k + 2] - d[k + 2])));
				ruido = Math.Max(ruido, Math.Max(Math.Abs(a[k] - e[k]), Math.Max(Math.Abs(a[k + 1] - e[k + 1]), Math.Abs(a[k + 2] - e[k + 2]))));
				if (ruido >= 2)
				{
					mexeu++;
					retrato[k] = 255; retrato[k + 1] = 220; retrato[k + 2] = 0;
					continue;
				}

				int mudou = Math.Max(Math.Abs(a[k] - b[k]), Math.Max(Math.Abs(a[k + 1] - b[k + 1]), Math.Abs(a[k + 2] - b[k + 2])));
				if (mudou < _minNiveis) continue;

				if (naGrade && marcada[cj * nx + ci]) mudouMarcadas++;
				if (sobTeto) { mudouTeto++; retrato[k] = 255; retrato[k + 1] = 40; retrato[k + 2] = 40; }
				else { mudouLivre++; retrato[k] = retrato[k + 1] = retrato[k + 2] = 255; }
			}
		}

		Guardar(com, $"teto-{_nomeDaMedida}.png");
		Guardar(Image.CreateFromData(w, h, false, Image.Format.Rgb8, retrato), $"teto-{_nomeDaMedida}-retrato.png");

		var m = new Medida(mudouTeto, mudouLivre, mudouMarcadas, mexeu, divisa, pxTeto, pxLivre);
		Nota($"{_nomeDaMedida}: na tela ha {pxTeto} px de celula sob teto e {pxLivre} px de ar livre; "
			 + $"a camada mudou {mudouTeto} e {mudouLivre}"
			 + (_marcadas.Count > 0 ? $" ({mudouMarcadas} nas {_marcadas.Count} celulas marcadas)" : "")
			 + $"; {mexeu} px mexeram sozinhos, {divisa} na divisa");
		return m;
	}

	private void Guardar(Image img, string nome)
	{
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}");
		Error e = img.SavePng(caminho);
		Nota(e == Error.Ok ? $"foto {caminho}" : $"NAO consegui salvar {caminho} ({e})");
	}

	// =====================================================================
	// ANDANDO: o canto de tela que os shaders tem contra o da camera, a cada quadro desenhado
	// =====================================================================
	private ClimaNaTela? _clima;
	private bool _andando;
	private int _quadrosAndando;
	private float _maiorAtraso;
	private Vector2 _cantoAnterior;

	/// <summary>
	/// O QUADRO ACABOU DE SER DESENHADO. Entre o `FramePreDraw` (onde o `ClimaNaTela` escreve) e
	/// aqui ninguem processou nada, entao a camera de agora E a camera com que o quadro saiu.
	/// So conta o quadro em que a camera andou: parado, qualquer atraso da zero.
	/// </summary>
	private void DepoisDeDesenhar()
	{
		if (!_andando || _clima == null || World.Instancia is not { } mundo) return;
		Vector2 tamanho = GetViewport().GetVisibleRect().Size / Math.Max(mundo.ZoomDaCameraDeTeste, 0.01f);
		Vector2 canto = mundo.CentroDaCameraDeTeste - tamanho * 0.5f;
		if (canto.DistanceTo(_cantoAnterior) > 0.05f)
		{
			_quadrosAndando++;
			_maiorAtraso = Math.Max(_maiorAtraso, canto.DistanceTo(_clima.OrigemDoTetoDeTeste));
		}
		_cantoAnterior = canto;
	}

	private void ComecarAAndar(World mundo)
	{
		Vector2 tamanho = GetViewport().GetVisibleRect().Size / Math.Max(mundo.ZoomDaCameraDeTeste, 0.01f);
		_cantoAnterior = mundo.CentroDaCameraDeTeste - tamanho * 0.5f;
		_quadrosAndando = 0;
		_maiorAtraso = 0;
		_andando = true;
		Input.ActionPress(Teclas.NomeNoMotor("move_down"));
	}

	private void PararDeAndar()
	{
		Input.ActionRelease(Teclas.NomeNoMotor("move_down"));
		_andando = false;
	}

	/// <summary>Todas as celulas marcadas respondem <paramref name="sobTeto"/>? Uma resposta por ponta.</summary>
	private (bool Servidor, bool Cliente) AsMarcadasEstao(bool sobTeto, GameServer servidor, World mundo)
	{
		bool s = true, c = true;
		foreach ((int cx, int cy) in _marcadas)
		{
			s &= servidor.CelulaSobTetoDeTeste(Terra, cx, cy) == sobTeto;
			c &= mundo.CelulaSobTeto(cx, cy) == sobTeto;
		}
		return (s, c);
	}

	// =====================================================================
	// A LUZ DE DENTRO: a cor que um pedaco da tela tem
	// =====================================================================
	/// <summary>
	/// O PISO LIVRE do lado leste do Banco, 4x2 celulas: sem mobilia, sem corpo e longe dos paineis
	/// da tela (o chat cobre o canto de baixo do lado oeste). E dele que sai "a cor da sala".
	/// </summary>
	private const int PisoX0 = 74, PisoY0 = 260, PisoLargura = 4, PisoAltura = 2;

	/// <summary>
	/// A FILEIRA DA PORTA do Banco -- a porta e as duas paredes vizinhas. Sao as unicas celulas sob
	/// teto que quem esta na RUA enxerga (o resto do predio o veu de visao cobre de preto), e por isso
	/// e nelas que se mede se a luz de dentro vaza pra fora.
	/// </summary>
	private const int PortaX0 = 72, PortaY0 = 262, PortaLargura = 3, PortaAltura = 1;

	/// <summary>A caverna da Terra: no DM o chao dela e todo `Inside`. So pra foto e uma conferencia.</summary>
	private static readonly ZoneKey Caverna = ZoneKey.Premade("Earth_Cave");

	private static readonly Vector3 SemMedida = new(-1, -1, -1);
	private Vector3 _pisoDeNoite, _pisoDeDia, _portaDaRua, _chaoDaCaverna;
	private float _forcaDaNoiteLaDentro, _forcaDaNoiteNaRua;
	private (int Cx, int Cy) _salao;
	private int _etapaDaLuz;

	/// <summary>
	/// A COR MEDIA (de 0 a 255 por canal) de um bloco de celulas no quadro que esta na tela.
	///
	/// POR QUE A MEDIA DE UM BLOCO, e nao pixel contra pixel como nas medidas do clima: a luz so muda
	/// de um quadro pro outro com a arvore ANDANDO (quem a acerta e o `_Process` da `Iluminacao`), e
	/// duas fotos tiradas em instantes diferentes nao sao comparaveis pixel a pixel. Um bloco de piso
	/// liso e: nele nada se mexe, e a media so muda se a LUZ mudar.
	///
	/// Uma borda de 3 pixels fica de fora -- e a mesma divisa de celula que as outras medidas nao julgam.
	/// </summary>
	private Vector3 MediaDoBloco(Image img, World mundo, int cx0, int cy0, int largura, int altura)
	{
		int w = img.GetWidth(), h = img.GetHeight();
		Vector2 mundoNaTela = GetViewport().GetVisibleRect().Size / Math.Max(mundo.ZoomDaCameraDeTeste, 0.01f);
		Vector2 origem = mundo.CentroDaCameraDeTeste - mundoNaTela * 0.5f;
		float sx = w / mundoNaTela.X, sy = h / mundoNaTela.Y;

		int x0 = Math.Max(0, (int)MathF.Ceiling((cx0 * T - origem.X) * sx) + 3);
		int x1 = Math.Min(w, (int)MathF.Floor(((cx0 + largura) * T - origem.X) * sx) - 3);
		int y0 = Math.Max(0, (int)MathF.Ceiling((cy0 * T - origem.Y) * sy) + 3);
		int y1 = Math.Min(h, (int)MathF.Floor(((cy0 + altura) * T - origem.Y) * sy) - 3);
		if (x1 <= x0 || y1 <= y0) return SemMedida;

		byte[] px = img.GetData();
		long r = 0, g = 0, b = 0;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				int k = (y * w + x) * 3;
				r += px[k]; g += px[k + 1]; b += px[k + 2];
			}
		float n = (x1 - x0) * (y1 - y0);
		return new Vector3(r / n, g / n, b / n);
	}

	/// <summary>Fotografa o quadro de agora, guarda a foto e devolve os bytes RGB dela (nulo sem janela).</summary>
	private Image? Fotografar(string nome, bool medir)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{nome}: sem foto (headless nao renderiza) -- NAO foi medido"); return null; }
		Guardar(img, $"teto-{nome}.png");
		if (medir) img.Convert(Image.Format.Rgb8);
		return img;
	}

	/// <summary>A cor media do piso livre do Banco no quadro de agora (e a foto dele).</summary>
	private Vector3 MedirOPiso(World mundo, string nome) =>
		Fotografar(nome, medir: true) is { } img ? MediaDoBloco(img, mundo, PisoX0, PisoY0, PisoLargura, PisoAltura) : SemMedida;

	/// <summary>A cor media da fileira da porta do Banco no quadro de agora (e a foto dele).</summary>
	private Vector3 MedirAPorta(World mundo, string nome) =>
		Fotografar(nome, medir: true) is { } img ? MediaDoBloco(img, mundo, PortaX0, PortaY0, PortaLargura, PortaAltura) : SemMedida;

	/// <summary>A cor media do chao a leste do corpo, na caverna (e a foto dele).</summary>
	private Vector3 MedirOChaoDaCaverna(World mundo, string nome) =>
		Fotografar(nome, medir: true) is { } img ? MediaDoBloco(img, mundo, _salao.Cx + 1, _salao.Cy, 1, 1) : SemMedida;

	private static float Brilho(Vector3 c) => (c.X + c.Y + c.Z) / 3f;

	/// <summary>A maior diferenca entre duas cores, canal a canal, em niveis de 255.</summary>
	private static float Diferenca(Vector3 a, Vector3 b) =>
		Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));

	private static string Tinta(Vector3 c) => c.X < 0 ? "(sem medida)" : $"({c.X:0},{c.Y:0},{c.Z:0})";

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } cli
		 || Hud.Instancia is null
		 || World.Instancia is not { } mundo
		 || GameServer.Instance is not { } servidor
		 || mundo.GetNodeOrNull("Iluminacao/Clima") is not ClimaNaTela clima)
		{
			_espera += delta;
			if (_espera > EsperaMaxima)
			{
				Conferir(false, $"em {EsperaMaxima:0}s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
				Fechar();
			}
			return;
		}
		_espera = 0;
		_clima = clima;

		if (_fase != Fase.Nao) { AndarNaMedida(mundo, servidor); return; }
		_t += delta;

		Vector2 dentro = Corpo(BercoCx, BercoCy), fora = Corpo(ForaCx, ForaCy);
		Vector2 eu = mundo.PosicaoLocalDeTeste ?? Vector2.Zero;
		EstadoDoClima tempo = mundo.TempoQueFaz ?? default;

		switch (_passo)
		{
			// ------------------------------------------------------------------ 1. dentro / o que cai
			case 0:
				GD.Print("[diagteto] ===== O CLIMA NAO ENTRA NO BANCO =====");
				// ZOOM 2: de fora, o predio e o chao aberto cabem no mesmo quadro.
				mundo.AplicarZoom(2);
				// MEIO-DIA: a foto tem que ser legivel, e a prova do clima nao depende da hora.
				servidor.CeuDaTerraDeTeste(0.5);
				servidor.MoveToZone(cli.LocalId, Terra, new Vec2(dentro.X, dentro.Y));
				servidor.ForcarClima(Terra, TipoDeClima.Nevasca, 1200, 1, "bancada do teto");
				Passar();
				break;

			case 1:
				// A NEVASCA CHEIA E O CORPO NO BERCO. A entrada do clima forcado leva 1,2 s e o floco
				// leva ~1 s pra atravessar a tela; quatro segundos cobrem os dois com folga.
				if ((eu.DistanceTo(dentro) > 24f || tempo.Tipo != TipoDeClima.Nevasca || tempo.Forca < 0.95 || _t < 4.0) && _t < 20) return;
				Conferir(eu.DistanceTo(dentro) <= 24f, $"PRECONDICAO: o corpo esta no berco do Banco ({eu})");
				Conferir(servidor.CelulaSobTetoDeTeste(Terra, BercoCx, BercoCy) && !servidor.CelulaSobTetoDeTeste(Terra, ForaCx, ForaCy),
						 "PRECONDICAO: pro servidor o berco esta sob teto e dois passos ao sul da porta e ar livre");
				Conferir(mundo.CelulaSobTeto(BercoCx, BercoCy) && !mundo.CelulaSobTeto(ForaCx, ForaCy) && mundo.EuEstouSobTeto,
						 "...e o cliente responde o mesmo das duas celulas, e sabe que o corpo local esta sob teto");
				Conferir(tempo.Tipo == TipoDeClima.Nevasca && tempo.Forca >= 0.95,
						 $"PRECONDICAO: nevasca cheia na Terra ({Clima.Nome(tempo.Tipo)}, forca {tempo.Forca:0.00})");
				Nota($"o palco: {Ceu.NomeDaHora(mundo.Ceu?.Hora ?? -1)}, {clima.PingosVivos} flocos vivos");
				ComecarMedida(clima.QuedaDeTeste, 6, "1-dentro-o-que-cai");
				Passar();
				break;

			case 2:
				if (_medida is { } m1)
				{
					Conferir(m1.PixelsSobTeto > 100_000, $"PRECONDICAO: a sala esta na tela ({m1.PixelsSobTeto} px sob teto)");
					Conferir(m1.MudouSobTeto == 0,
							 $"DENTRO do Banco, nenhum floco e desenhado sobre celula sob teto ({m1.MudouSobTeto} px)");
				}
				else Conferir(false, "a medida 'dentro / o que cai' nao saiu");
				servidor.MoveToZone(cli.LocalId, Terra, new Vec2(fora.X, fora.Y));
				Passar();
				break;

			// ------------------------------------------------------------------ 2. fora / o que cai
			case 3:
				// Os flocos soltos ficaram em volta da camera ANTIGA (eles vivem no mundo): a tela
				// nova precisa de uma vida de floco pra encher de novo.
				if ((eu.DistanceTo(fora) > 24f || _t < 4.0) && _t < 20) return;
				Conferir(eu.DistanceTo(fora) <= 24f, $"PRECONDICAO: o corpo esta dois passos ao sul da porta ({eu})");
				Conferir(!mundo.EuEstouSobTeto, "...e o cliente sabe que o corpo local esta ao ar livre");
				ComecarMedida(clima.QuedaDeTeste, 6, "2-fora-o-que-cai");
				Passar();
				break;

			case 4:
				if (_medida is { } m2)
				{
					Conferir(m2.PixelsSobTeto > 20_000, $"PRECONDICAO: daqui o predio esta na tela ({m2.PixelsSobTeto} px sob teto)");
					Conferir(m2.MudouAoArLivre > 800,
							 $"contra-exemplo: FORA do Banco neva no chao aberto ({m2.MudouAoArLivre} px de floco)");
					Conferir(m2.MudouSobTeto == 0,
							 $"...e nenhum floco e desenhado sobre o predio ({m2.MudouSobTeto} px)");
				}
				else Conferir(false, "a medida 'fora / o que cai' nao saiu");

				// A MASSA: neblina, que nao tem nada caindo -- o que muda na tela e so o veu.
				servidor.ForcarClima(Terra, TipoDeClima.Neblina, 1200, 1, "bancada do teto");
				Passar();
				break;

			// ------------------------------------------------------------------ 3. fora / a massa
			case 5:
				if ((tempo.Tipo != TipoDeClima.Neblina || tempo.Forca < 0.95 || _t < 3.0) && _t < 20) return;
				Conferir(tempo.Tipo == TipoDeClima.Neblina && tempo.Forca >= 0.95,
						 $"PRECONDICAO: neblina cheia na Terra ({Clima.Nome(tempo.Tipo)}, forca {tempo.Forca:0.00})");
				ComecarMedida(clima.MassaDeTeste, 2, "3-fora-a-massa");
				Passar();
				break;

			case 6:
				if (_medida is { } m3)
				{
					Conferir(m3.MudouAoArLivre > 100_000,
							 $"contra-exemplo: FORA do Banco a massa da neblina cobre o chao aberto ({m3.MudouAoArLivre} px)");
					Conferir(m3.MudouSobTeto == 0,
							 $"...e nenhum pixel do predio e tingido por ela ({m3.MudouSobTeto} px)");
				}
				else Conferir(false, "a medida 'fora / a massa' nao saiu");
				servidor.MoveToZone(cli.LocalId, Terra, new Vec2(dentro.X, dentro.Y));
				Passar();
				break;

			// ------------------------------------------------------------------ 4. dentro / a massa
			case 7:
				if ((eu.DistanceTo(dentro) > 24f || _t < 3.0) && _t < 20) return;
				Conferir(eu.DistanceTo(dentro) <= 24f, $"PRECONDICAO: o corpo voltou pro berco ({eu})");
				ComecarMedida(clima.MassaDeTeste, 2, "4-dentro-a-massa");
				Passar();
				break;

			case 8:
				if (_medida is { } m4)
					Conferir(m4.MudouSobTeto == 0,
							 $"DENTRO do Banco, a massa da neblina nao tinge nenhum pixel da sala ({m4.MudouSobTeto} px)");
				else Conferir(false, "a medida 'dentro / a massa' nao saiu");

				// ------------------------------------------------------------------ 5. andando pela porta
				GD.Print("[diagteto] ===== ANDANDO: a mascara nao escorrega =====");
				servidor.ForcarClima(Terra, TipoDeClima.Nevasca, 1200, 1, "bancada do teto");
				ComecarAAndar(mundo);
				Passar();
				break;

			case 9:
				if (eu.Y < fora.Y - 4f && _t < 8) return;
				PararDeAndar();
				Conferir(eu.Y >= fora.Y - 4f && !mundo.EuEstouSobTeto,
						 $"o corpo saiu do berco PELA PORTA, andando, e esta ao ar livre (em {_t:0.0}s, {eu})");
				Conferir(_quadrosAndando >= 20, $"PRECONDICAO: a camera andou em {_quadrosAndando} quadros desenhados");
				Conferir(_maiorAtraso <= 0.01f,
						 $"andando, o canto de tela dos shaders e o da camera do MESMO quadro (maior diferenca {_maiorAtraso:0.000} px de mundo)");

				// O CONTRA-EXEMPLO: a posicao escrita no `_Process`, um quadro atras.
				ClimaNaTela.TetoAtrasadoDeTeste = true;
				servidor.MoveToZone(cli.LocalId, Terra, new Vec2(dentro.X, dentro.Y));
				Passar();
				break;

			case 10:
				if ((eu.DistanceTo(dentro) > 24f || _t < 2.0) && _t < 20) return;
				ComecarAAndar(mundo);
				Passar();
				break;

			case 11:
				if (eu.Y < fora.Y - 4f && _t < 8) return;
				PararDeAndar();
				ClimaNaTela.TetoAtrasadoDeTeste = false;
				Conferir(_quadrosAndando >= 20 && _maiorAtraso > 0.2f,
						 $"(defeito injetado: a posicao escrita um quadro antes) a mascara fica {_maiorAtraso:0.00} px de mundo "
						 + $"atras da camera em {_quadrosAndando} quadros -- a linha de cima reprovaria");

				// ------------------------------------------------------------------ 6. o plano nao vale
				GD.Print("[diagteto] ===== OS DEFEITOS DO CORE, NO DESENHO =====");
				servidor.MoveToZone(cli.LocalId, Terra, new Vec2(dentro.X, dentro.Y));
				Passar();
				break;

			case 12:
				if ((eu.DistanceTo(dentro) > 24f || tempo.Tipo != TipoDeClima.Nevasca || tempo.Forca < 0.95 || _t < 3.0) && _t < 20) return;
				// O JUIZ E GUARDADO ANTES: o Core e um so pros dois lados deste processo, e com o defeito
				// ligado o servidor tambem passaria a dizer "ar livre" da sala inteira.
				_tetoGuardado = GuardarOTeto(servidor);
				CelulaInterna.SemPlanoDeTeste = true;
				mundo.RemontarOTetoDeTeste();
				Passar();
				break;

			case 13:
				if (_t < 2.5) return;   // os flocos precisam de uma vida pra voltar a cair na sala
				ComecarMedida(clima.QuedaDeTeste, 6, "5-defeito-o-plano-nao-vale");
				Passar();
				break;

			case 14:
				CelulaInterna.SemPlanoDeTeste = false;
				// O CUSTO DA MONTAGEM, medido onde ela roda: e o que cada carga de zona com interior
				// paga a mais por causa do teto (o plano inteiro + a textura que sobe pra GPU).
				long relogioDoTeto = System.Diagnostics.Stopwatch.GetTimestamp();
				mundo.RemontarOTetoDeTeste();
				Nota($"montar o teto da Terra ({mundo.Teto.Largura}x{mundo.Teto.Altura} celulas, com a textura) levou "
					 + $"{System.Diagnostics.Stopwatch.GetElapsedTime(relogioDoTeto).TotalMilliseconds:0.00} ms");
				_tetoGuardado = null;
				if (_medida is { } m5)
					Conferir(m5.MudouSobTeto > 800,
							 $"(defeito injetado: o plano nao vale) volta a nevar DENTRO do Banco ({m5.MudouSobTeto} px) -- a medida 1 reprovaria");
				else Conferir(false, "a medida do defeito 'o plano nao vale' nao saiu");

				// ------------------------------------------------------------------ 7. a luz de dentro
				// ANTES DO BURACO, e nao depois: assim ela nao depende de o piso ter voltado.
				GD.Print("[diagteto] ===== A LUZ DE DENTRO: sob teto nao escurece, pra quem esta sob teto =====");
				// MADRUGADA DE LUA NOVA: a noite mais escura que o jogo tem. `Limpo` com prazo FORCA ceu
				// aberto (ver `GameServer.ForcarClima`).
				servidor.CeuDaTerraDeTeste(0.10, (int)FaseDaLua.Nova);
				servidor.ForcarClima(Terra, TipoDeClima.Limpo, 1200, 1, "bancada do teto");
				_etapaDaLuz = 0;
				Passar();
				break;

			case 15:
				switch (_etapaDaLuz)
				{
					// ---- de madrugada, dentro do Banco
					case 0:
						if (_t < 5.0) return;   // a hora vira, o clima sai, e a luz assenta
						Conferir(eu.DistanceTo(dentro) <= 24f && mundo.EuEstouSobTeto
								 && Ceu.NomeDaHora(mundo.Ceu?.Hora ?? 0.5) == "madrugada" && tempo.Tipo == TipoDeClima.Limpo,
								 $"PRECONDICAO: no berco, de madrugada, com o ceu limpo ({Ceu.NomeDaHora(mundo.Ceu?.Hora ?? -1)}, {Clima.Nome(tempo.Tipo)})");
						_pisoDeNoite = MedirOPiso(mundo, "luz-1-dentro-de-madrugada");
						_forcaDaNoiteLaDentro = Iluminacao.ForcaDaNoite();
						// A REFERENCIA: a mesma sala, o mesmo quadro, com o ambiente TRAVADO no tom de dia.
						Iluminacao.MeioDiaDeTeste = true;
						break;

					// ---- a referencia, e o veredito da madrugada
					case 1:
						if (_t < 1.0) return;
						_pisoDeDia = MedirOPiso(mundo, "luz-0-referencia-tom-de-dia");
						Iluminacao.MeioDiaDeTeste = false;
						Conferir(Brilho(_pisoDeDia) > 20,
								 $"PRECONDICAO: o piso de referencia e legivel (cor media {Tinta(_pisoDeDia)})");
						Conferir(_pisoDeNoite.X >= 0 && Diferenca(_pisoDeNoite, _pisoDeDia) <= 3f,
								 $"DENTRO do Banco, de madrugada, o piso tem a cor do tom de dia "
								 + $"(madrugada {Tinta(_pisoDeNoite)}, referencia {Tinta(_pisoDeDia)})");
						Conferir(_forcaDaNoiteLaDentro <= 0.001f,
								 $"...e la dentro a aura e a luz de ki nao brilham como se fosse noite (forca da noite {_forcaDaNoiteLaDentro:0.00})");

						// O CONTRA-EXEMPLO: o jogo de antes, no mesmo binario.
						Iluminacao.SemLuzDeDentroDeTeste = true;
						break;

					// ---- (defeito) sem a luz de dentro
					case 2:
					{
						if (_t < 1.0) return;
						Vector3 semALuz = MedirOPiso(mundo, "luz-1-defeito-sem-a-luz-de-dentro");
						float forca = Iluminacao.ForcaDaNoite();
						Iluminacao.SemLuzDeDentroDeTeste = false;
						Conferir(semALuz.X >= 0 && Brilho(semALuz) < Brilho(_pisoDeDia) * 0.5f && forca > 0.5f,
								 $"(defeito injetado: sem a luz de dentro) a sala escurece com a rua (piso {Tinta(semALuz)}, "
								 + $"forca da noite {forca:0.00}) -- as duas linhas de cima reprovariam");

						// A TEMPESTADE, DE DIA: o escuro dela nao e o da noite, e tambem nao entra.
						servidor.CeuDaTerraDeTeste(0.5);
						servidor.ForcarClima(Terra, TipoDeClima.Tempestade, 1200, 1, "bancada do teto");
						break;
					}

					// ---- tempestade ao meio-dia, dentro do Banco
					case 3:
					{
						if ((tempo.Tipo != TipoDeClima.Tempestade || tempo.Forca < 0.95 || _t < 5.0) && _t < 20) return;
						Conferir(tempo.Tipo == TipoDeClima.Tempestade && tempo.Forca >= 0.95,
								 $"PRECONDICAO: tempestade cheia ao meio-dia ({Clima.Nome(tempo.Tipo)}, forca {tempo.Forca:0.00})");
						Vector3 naTempestade = MedirOPiso(mundo, "luz-2-dentro-na-tempestade");
						Conferir(naTempestade.X >= 0 && Diferenca(naTempestade, _pisoDeDia) <= 3f,
								 $"DENTRO do Banco, na tempestade, o piso tem a cor do tom de dia "
								 + $"(tempestade {Tinta(naTempestade)}, referencia {Tinta(_pisoDeDia)})");

						// DA RUA, de madrugada: dois passos ao sul da porta.
						servidor.CeuDaTerraDeTeste(0.10, (int)FaseDaLua.Nova);
						servidor.ForcarClima(Terra, TipoDeClima.Limpo, 1200, 1, "bancada do teto");
						servidor.MoveToZone(cli.LocalId, Terra, new Vec2(fora.X, fora.Y));
						break;
					}

					// ---- da rua, de madrugada
					case 4:
						if ((eu.DistanceTo(fora) > 24f || tempo.Tipo != TipoDeClima.Limpo || _t < 5.0) && _t < 20) return;
						Conferir(eu.DistanceTo(fora) <= 24f && !mundo.EuEstouSobTeto
								 && Ceu.NomeDaHora(mundo.Ceu?.Hora ?? 0.5) == "madrugada",
								 $"PRECONDICAO: na rua, dois passos ao sul da porta, de madrugada ({eu}, {Ceu.NomeDaHora(mundo.Ceu?.Hora ?? -1)})");
						_portaDaRua = MedirAPorta(mundo, "luz-3-da-rua-de-madrugada");
						_forcaDaNoiteNaRua = Iluminacao.ForcaDaNoite();
						// O CONTRA-EXEMPLO: a luz de dentro acesa pra quem esta na rua -- o desenho do DM.
						Iluminacao.LuzDeDentroAcesaDaRuaDeTeste = true;
						break;

					// ---- (defeito) a luz de dentro acesa da rua, e a ida pra caverna
					case 5:
					{
						if (_t < 1.0) return;
						Vector3 acesa = MedirAPorta(mundo, "luz-3-defeito-acesa-da-rua");
						Iluminacao.LuzDeDentroAcesaDaRuaDeTeste = false;
						Conferir(Brilho(acesa) > 12,
								 $"PRECONDICAO: da rua a fileira da porta do Banco aparece (com a luz forcada, {Tinta(acesa)})");
						Conferir(_portaDaRua.X >= 0 && Brilho(_portaDaRua) < Brilho(acesa) * 0.5f,
								 $"DA RUA, de madrugada, a porta do Banco fica no escuro da rua {Tinta(_portaDaRua)}; "
								 + $"(defeito injetado: a luz acesa da rua) ela acende no breu {Tinta(acesa)} -- e o que o dono viu e recusou");
						Conferir(_forcaDaNoiteNaRua > 0.5f,
								 $"...e na rua continua noite pra aura e pra luz de ki (forca da noite {_forcaDaNoiteNaRua:0.00})");

						// A CAVERNA: no DM o chao dela e todo `Inside`. Quem acha um salao e o servidor.
						if (servidor.CelulaLivreSobTetoDeTeste(Caverna) is not { } salao)
						{
							Conferir(false, "PRECONDICAO: a caverna da Terra tem um salao livre sob teto pra fotografar");
							Passar();
							return;
						}
						_salao = salao;
						Vector2 la = Corpo(salao.Cx, salao.Cy);
						servidor.MoveToZone(cli.LocalId, Caverna, new Vec2(la.X, la.Y));
						break;
					}

					// ---- na caverna, de madrugada
					case 6:
					{
						Vector2 la = Corpo(_salao.Cx, _salao.Cy);
						if ((eu.DistanceTo(la) > 24f || !mundo.EuEstouSobTeto || _t < 6.0) && _t < 25) return;
						Conferir(eu.DistanceTo(la) <= 24f && mundo.EuEstouSobTeto,
								 $"PRECONDICAO: o corpo esta num salao da caverna da Terra, sob teto "
								 + $"(celula {_salao.Cx},{_salao.Cy}; hora local: {Ceu.NomeDaHora(mundo.Ceu?.Hora ?? -1)})");
						_chaoDaCaverna = MedirOChaoDaCaverna(mundo, "luz-4-caverna-de-madrugada");
						Iluminacao.SemLuzDeDentroDeTeste = true;
						break;
					}

					// ---- (defeito) a caverna sem a luz de dentro: como era
					case 7:
					{
						if (_t < 1.0) return;
						Vector3 escura = MedirOChaoDaCaverna(mundo, "luz-4-caverna-defeito-sem-a-luz-de-dentro");
						Iluminacao.SemLuzDeDentroDeTeste = false;
						Conferir(_chaoDaCaverna.X >= 0 && escura.X >= 0 && Brilho(_chaoDaCaverna) > Brilho(escura) * 2f + 4f,
								 $"na CAVERNA, de madrugada, o chao fica no tom de dia {Tinta(_chaoDaCaverna)}; "
								 + $"(defeito injetado: sem a luz de dentro) ele escurece {Tinta(escura)}");
						Passar();
						return;
					}
				}
				_etapaDaLuz++;
				_t = 0;
				break;

			case 16:
				// ------------------------------------------------------------------ 8. o buraco no piso
				GD.Print("[diagteto] ===== QUEBRAR: o buraco no piso deixa a neve entrar =====");
				servidor.CeuDaTerraDeTeste(0.5);
				servidor.ForcarClima(Terra, TipoDeClima.Nevasca, 1200, 1, "bancada do teto");
				servidor.MoveToZone(cli.LocalId, Terra, new Vec2(dentro.X, dentro.Y));
				Passar();
				break;

			case 17:
				if ((eu.DistanceTo(dentro) > 24f || tempo.Tipo != TipoDeClima.Nevasca || tempo.Forca < 0.95 || _t < 4.0) && _t < 20) return;
				Conferir(eu.DistanceTo(dentro) <= 24f && tempo.Tipo == TipoDeClima.Nevasca && tempo.Forca >= 0.95,
						 $"PRECONDICAO: de volta ao berco, com nevasca cheia ({eu}, {Clima.Nome(tempo.Tipo)} {tempo.Forca:0.00})");
				_marcadas.Clear();
				for (int dy = 0; dy < BuracoAltura; dy++)
					for (int dx = 0; dx < BuracoLargura; dx++)
						if (servidor.DerrubarCelulaDeTeste(Terra, BuracoX0 + dx, BuracoY0 + dy))
							_marcadas.Add((BuracoX0 + dx, BuracoY0 + dy));
				Passar();
				break;

			case 18:
			{
				if (_t < 3.0) return;   // o pacote chega, o estrago e aplicado, e a neve desce no buraco
				Conferir(_marcadas.Count >= 6,
						 $"PRECONDICAO: o piso do Banco caiu pelo funil de producao ({_marcadas.Count} de {BuracoLargura * BuracoAltura} celulas)");
				(bool noServidor, bool noCliente) = AsMarcadasEstao(sobTeto: false, servidor, mundo);
				Conferir(noServidor && noCliente,
						 $"as celulas caidas deixaram de estar sob teto nas DUAS pontas (servidor {noServidor}, cliente {noCliente})");
				ComecarMedida(clima.QuedaDeTeste, 6, "6-o-buraco-no-piso");
				Passar();
				break;
			}

			case 19:
				if (_medida is { } m6)
				{
					Conferir(m6.MudouNasMarcadas > 150, $"passa a nevar NO BURACO ({m6.MudouNasMarcadas} px de floco nas celulas caidas)");
					Conferir(m6.MudouSobTeto == 0, $"...e o resto da sala continua seco ({m6.MudouSobTeto} px)");
				}
				else Conferir(false, "a medida do buraco nao saiu");

				// O CONTRA-EXEMPLO: a caida continua interna. O juiz e o de agora (com o buraco).
				_tetoGuardado = GuardarOTeto(servidor);
				CelulaInterna.CaidaContinuaInternaDeTeste = true;
				mundo.RemontarOTetoDeTeste();
				Passar();
				break;

			case 20:
				if (_t < 2.5) return;
				ComecarMedida(clima.QuedaDeTeste, 6, "7-defeito-a-caida-continua-interna");
				Passar();
				break;

			case 21:
				CelulaInterna.CaidaContinuaInternaDeTeste = false;
				mundo.RemontarOTetoDeTeste();
				_tetoGuardado = null;
				if (_medida is { } m7)
					Conferir(m7.MudouNasMarcadas == 0,
							 $"(defeito injetado: a caida continua interna) o buraco fica SECO ({m7.MudouNasMarcadas} px) -- a medida 6 reprovaria");
				else Conferir(false, "a medida do defeito 'a caida continua interna' nao saiu");

				// ------------------------------------------------------------------ 9. o piso volta
				// O `Restaurar_Planeta` do admin, pelo verb de producao. O servidor esvazia o estrago da
				// zona e manda o cliente recarregar o cenario: o teto tem que FECHAR de novo nas duas pontas.
				GD.Print("[diagteto] ===== CONSERTAR: o piso volta, e o teto com ele =====");
				servidor.ConsertarCenarioDeTeste(cli.LocalId);
				Passar();
				break;

			case 22:
			{
				if (_t < 4.0) return;   // o pacote chega e a cena e reinstanciada do disco
				(bool noServidor, bool noCliente) = AsMarcadasEstao(sobTeto: true, servidor, mundo);
				Conferir(noServidor, "cenario refeito: pro SERVIDOR o piso do Banco voltou a estar sob teto");
				Conferir(noCliente, "...e pro CLIENTE tambem (o teto dele se remonta na recarga da zona)");
				ComecarMedida(clima.QuedaDeTeste, 6, "8-depois-do-conserto");
				Passar();
				break;
			}

			case 23:
				if (_medida is { } m8)
					Conferir(m8.MudouSobTeto == 0,
							 $"...e para de nevar onde era o buraco ({m8.MudouNasMarcadas} px nas celulas refeitas, {m8.MudouSobTeto} na sala inteira)");
				else Conferir(false, "a medida 'depois do conserto' nao saiu");
				_marcadas.Clear();
				Fechar();
				break;
		}
	}

	private void Fechar()
	{
		_acabou = true;
		GetTree().Paused = false;
		Soltar();
		foreach (string p in _passos) GD.Print("[diagteto] " + p);
		GD.Print(_falhas.Count == 0 ? "[diagteto] ===== TUDO OK =====" : $"[diagteto] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagteto]   - " + f);
		GD.Print($"[diagteto] fim: {_passos.Count(p => p.StartsWith("  ok"))} ok, {_falhas.Count} falha(s)");
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
