using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA GOTA NA TELA ============================
/// O pedido foi *"faca um SHADER legal q deixa a TELA ONDULANDO IGUAL UMA GOTA quando cai na
/// agua"*, e ele so esta atendido se CINCO coisas forem verdade ao mesmo tempo:
///
///   1. o shader COMPILA e o `ColorRect` entra na composicao;
///   2. o MUNDO se move de verdade -- pixel deslocado, e nao uniform escrito;
///   3. a INTERFACE **nao** se move (a queixa registrada do dono na nevoa: *"a gui ta ficando toda
///      com efeito, deveria ser tudo menos a GUI"*);
///   4. a onda MORRE SOZINHA e a tela volta EXATAMENTE ao que era -- se sobrasse um resto, ele
///      apareceria bem na hora da troca de zona;
///   5. `Parar()` corta no meio (o *"interrompeu, a onda morre"*), e o quadro seguinte ja esta limpo.
///
/// ============================ POR QUE ELA PRECISA DE JANELA ============================
/// Porque as cinco perguntas sao sobre PIXEL. Este projeto tem registro de quatro defeitos visuais
/// que passaram por quatro mil checagens verdes porque a bancada media INTENCAO -- e um shader e o
/// caso extremo disso: `SetShaderParameter` devolve void, nunca falha, e continua devolvendo void
/// com o shader inteiro sem compilar. No headless o `GetImage` volta vazio, entao ali nao ha nada
/// a medir e a bancada diz isso em vez de fingir que passou.
///
/// O QUADRO CALMO E O CONTROLE de tudo: cada veredito e uma comparacao contra ele. Nao ha como o
/// item 2 ficar verde com o shader morto (as duas fotos sairiam byte a byte iguais), e nao ha como
/// o item 3 ficar verde por acidente (a faixa de interface e opaca e desenhada DEPOIS da gota --
/// se ela mudasse, seria porque a camada esta errada).
/// ======================================================================================
///
///     &lt;godot&gt; --path . --diaggota --position 1920,0 --resolution 1280x720
///
/// SEM REDE, SEM MUNDO E SEM LOGIN: a gota nao depende de zona nem de servidor. O que ela toca e um
/// `CanvasLayer`, um relogio e o quadro desenhado.
/// </summary>
public partial class RoboDaGota : Node2D
{
	private readonly List<string> _linhas = [];
	private int _falhas;

	private void Ok(string oque, bool passou)
	{
		_linhas.Add((passou ? "  OK   " : "  FALHA") + "  " + oque);
		if (!passou) _falhas++;
	}

	private void Nota(string t) => _linhas.Add("   --    " + t);

	/// <summary>Onde a faixa de INTERFACE comeca, em fracao da altura. Abaixo disto e GUI.</summary>
	private const float FaixaDaGui = 0.85f;

	/// <summary>
	/// Quantos quadros cada metade da familia 6 mede.
	///
	/// ERAM 60, E 60 NAO MEDIA NADA: a diferenca saiu NEGATIVA (0,66 ms com a onda contra 0,68 sem
	/// ela) -- o passe de tela cheia e menor que o tremor do relogio numa amostra curta. Um numero
	/// negativo num relatorio de custo nao e "de graca", e "a bancada nao mediu", e a diferenca entre
	/// as duas frases e a unica coisa que essa familia tem pra entregar.
	/// </summary>
	private const int Blocos = 24;

	/// <summary>Quantos quadros MEDIDOS por bloco (fora os tres de folga do cronometro).</summary>
	private const int PorBloco = 40;

	private GotaNaTela _gota = null!;

	private Image? _calma;
	private double _maiorNoMundo, _maiorNaGui;
	private int _quadrosDeOnda;
	private double _msComOnda, _msSemOnda;

	public override void _Ready()
	{
		// VSYNC DESLIGADO: com ele, medir custo daria 16,6 ms nos dois casos e a familia de custo
		// seria uma mentira bem formatada.
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = 0;

		// O CRONOMETRO DA PLACA. Ver <see cref="SomarGpu"/> -- sem ele a familia 6 mede a CPU e nao
		// enxerga o passe de tela cheia.
		RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);

		MontarCenario();

		_gota = new GotaNaTela { Name = "GotaNaTela" };
		AddChild(_gota);

		MontarRoteiro();
	}

	/// <summary>
	/// O ALVO: um XADREZ de alto contraste no MUNDO e uma faixa opaca de INTERFACE por cima.
	///
	/// Xadrez e nao um fundo liso porque o que se mede e DESLOCAMENTO: um pano de uma cor so
	/// continuaria da mesma cor depois de empurrado 40 px, e a bancada acharia que nada aconteceu
	/// com o shader funcionando perfeitamente.
	/// </summary>
	private void MontarCenario()
	{
		Vector2 tela = GetViewport().GetVisibleRect().Size;
		const int lado = 24;

		for (int y = 0; y < tela.Y; y += lado)
			for (int x = 0; x < tela.X; x += lado)
			{
				bool claro = ((x / lado) + (y / lado)) % 2 == 0;
				AddChild(new ColorRect
				{
					Color = claro ? new Color(0.92f, 0.92f, 0.95f) : new Color(0.10f, 0.11f, 0.16f),
					Position = new Vector2(x, y),
					Size = new Vector2(lado, lado),
					MouseFilter = Control.MouseFilterEnum.Ignore,
				});
			}

		// A INTERFACE. Camada 1 -- a do HUD --, opaca e com o mesmo xadrez, pra que qualquer
		// deslocamento nela seja tao visivel quanto no mundo. Se a gota estivesse na camada errada,
		// esta faixa mexeria.
		var gui = new CanvasLayer { Layer = 1, Name = "GuiDeTeste" };
		AddChild(gui);
		float topo = tela.Y * FaixaDaGui;
		for (int x = 0; x < tela.X; x += lado)
			gui.AddChild(new ColorRect
			{
				Color = (x / lado) % 2 == 0 ? new Color(1f, 0.55f, 0.10f) : new Color(0.05f, 0.05f, 0.05f),
				Position = new Vector2(x, topo),
				Size = new Vector2(lado, tela.Y - topo),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			});
	}

	// =====================================================================
	// O ROTEIRO -- um passo por quadro
	// =====================================================================
	private readonly List<Action> _passos = [];
	private int _passo;

	private double _somaSem, _somaCom;
	private int _nSem, _nCom;

	/// <summary>
	/// ============================ O CUSTO E DA GPU, E O RELOGIO DE PAREDE NAO O VE ============================
	/// A primeira versao desta familia cronometrava `Time.GetTicksUsec()` em volta de N quadros
	/// vazios. Ela devolveu 0,66 ms com a onda contra 0,68 ms **sem** ela -- e devolveu os MESMOS
	/// numeros a 720p e a 1080p, com 60 e com 400 amostras.
	///
	/// Nao era ruido de sorte: com vsync desligado e nenhum gargalo de desenho, o laco principal
	/// corre na CPU e o trabalho da placa fica ENFILEIRADO atras dele. O relogio de parede media a
	/// CPU; a onda custa GPU. Duas coisas diferentes concordando por acaso e exatamente o cego que
	/// este projeto ja registrou -- "as duas telas concordam" fica verde com as duas erradas igual.
	///
	/// `ViewportSetMeasureRenderTime` liga o cronometro DA PLACA (o motor poe marcadores em volta da
	/// passada de desenho), e `ViewportGetMeasuredRenderTimeGpu` devolve o milissegundo real daquele
	/// quadro. E o unico numero desta bancada que responde "quanto isto custa".
	/// ======================================================================================================
	/// </summary>
	private void SomarGpu(bool comOnda)
	{
		double ms = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
		if (ms <= 0) return;   // o primeiro quadro depois de ligar ainda nao tem medida
		if (comOnda) { _somaCom += ms; _nCom++; }
		else { _somaSem += ms; _nSem++; }
	}

	private void MontarRoteiro()
	{
		// Dois quadros pra o cenario existir de fato antes da foto de controle.
		_passos.Add(() => { });
		_passos.Add(() => { });

		_passos.Add(() =>
		{
			_calma = Foto("1-calma");
			if (_calma == null) { SemQuadro(); return; }
			Ok("1. o shader carregou e a gota existe na arvore", _gota.GetNodeOrNull<ColorRect>("Gota") != null);
			Ok("1. em repouso ela esta FORA da composicao (nao ha copia de framebuffer pra pagar)",
			   !_gota.Ondulando);
		});

		// ---- 2 e 3: a onda corre, e cada quadro dela e comparado com o controle ----
		_passos.Add(() => _gota.Cair(0.60));
		for (int i = 0; i < 40; i++)
			_passos.Add(MedirQuadroDaOnda);

		_passos.Add(() =>
		{
			if (_calma == null) return;
			Nota($"a onda durou {_quadrosDeOnda} quadros medidos");
			Ok("2. O MUNDO ONDULOU: o xadrez do mundo mudou no meio da onda "
			   + $"(maior diferenca de luminancia {_maiorNoMundo:0.000})", _maiorNoMundo > 0.20);
			Ok("3. A INTERFACE NAO ONDULOU: a faixa da camada 1 ficou intacta o tempo todo "
			   + $"(maior diferenca {_maiorNaGui:0.000})", _maiorNaGui < 0.02);
		});

		// ---- 4: ela morre sozinha e a tela volta ao que era ----
		_passos.Add(() => { });
		_passos.Add(() =>
		{
			if (_calma == null) return;
			Ok("4. a onda MORREU SOZINHA -- ninguem a desligou", !_gota.Ondulando);
			Image? depois = Foto("2-depois");
			Ok("4. a tela voltou EXATAMENTE ao que era (nenhum resto ondulando na troca de zona)",
			   depois != null && Diferenca(_calma, depois, mundo: true) < 0.02);
		});

		// ---- 5: o corte no meio ----
		_passos.Add(() => _gota.Cair(30.0));   // uma onda longa, pra o corte ter o que cortar
		_passos.Add(() => { });
		_passos.Add(() =>
		{
			Ok("5. a onda longa esta correndo", _gota.Ondulando);
			_gota.Parar();
		});
		_passos.Add(() => { });
		_passos.Add(() =>
		{
			if (_calma == null) return;
			Ok("5. `Parar()` matou a onda no meio", !_gota.Ondulando && _gota.FaseNaTela == 0f);
			Image? cortada = Foto("3-cortada");
			Ok("5. e o quadro seguinte ja esta limpo",
			   cortada != null && Diferenca(_calma, cortada, mundo: true) < 0.02);
		});

		// ---- 6: o custo, medido NA GPU e em blocos ALTERNADOS ----
		//
		// ============================ POR QUE ALTERNADO, E NAO UM BLOCO DE CADA ============================
		// A segunda versao desta familia mediu a GPU de verdade e AINDA devolveu negativo: 0,148 ms
		// sem a onda contra 0,121 ms com ela. Nao ha como um passe a mais custar menos -- o que havia
		// era DERIVA: um quadro de 0,13 ms deixa a placa quase parada, ela baixa o relogio sozinha, e
		// meio segundo separando as duas amostras basta pra elas caírem em estados de relogio
		// diferentes. A amostra maior nao conserta isso; ela so mede a deriva com mais casas.
		//
		// Alternando de 40 em 40 quadros, as duas metades atravessam os MESMOS estados de relogio, e
		// o que sobra na diferenca e a onda. E a mesma ideia do quadro calmo das familias 2 e 3: o
		// controle tem que ser tirado ao lado da medida, e nao antes dela.
		// ================================================================================================
		for (int bloco = 0; bloco < Blocos; bloco++)
		{
			bool comOnda = bloco % 2 == 1;
			_passos.Add(() => { if (comOnda) _gota.Cair(30.0); else _gota.Parar(); });

			// TRES QUADROS DE FOLGA: a medida de uma passada so fica pronta um ou dois quadros depois
			// dela, entao as primeiras leituras de cada bloco ainda descrevem o bloco ANTERIOR.
			_passos.Add(() => { });
			_passos.Add(() => { });
			_passos.Add(() => { });
			for (int i = 0; i < PorBloco; i++) _passos.Add(() => SomarGpu(comOnda));
		}

		_passos.Add(() =>
		{
			_gota.Parar();
			_msSemOnda = _nSem > 0 ? _somaSem / _nSem : 0;
			_msComOnda = _nCom > 0 ? _somaCom / _nCom : 0;
			Nota($"6. CUSTO NA GPU ({Blocos} blocos alternados de {PorBloco} quadros): "
				 + $"{_msSemOnda:0.000} ms/quadro sem a onda, {_msComOnda:0.000} ms/quadro com ela "
				 + $"(+{_msComOnda - _msSemOnda:0.000} ms enquanto ela dura)");
			Ok("6. a onda custa alguma coisa medivel na GPU (senao a familia nao esta medindo NADA)",
			   _msComOnda > _msSemOnda);
			// SEM TETO EM MILISSEGUNDO, e de proposito: o numero depende da placa e da resolucao de
			// quem roda. O veredito aqui e so que ele seja POSITIVO -- e essa e a checagem que a
			// primeira versao desta familia nao tinha e por isso passou verde devolvendo -0,02 ms.
		});

		_passos.Add(Terminar);
	}

	/// <summary>Um quadro da onda: mede o mundo e a interface contra o controle.</summary>
	private void MedirQuadroDaOnda()
	{
		if (_calma == null || !_gota.Ondulando) return;
		_quadrosDeOnda++;

		Image? agora = GetViewport()?.GetTexture()?.GetImage();
		if (agora == null || agora.IsEmpty()) return;

		_maiorNoMundo = Math.Max(_maiorNoMundo, Diferenca(_calma, agora, mundo: true));
		_maiorNaGui = Math.Max(_maiorNaGui, Diferenca(_calma, agora, mundo: false));

		// UMA FOTO DO MEIO, pra o dono OLHAR. O veredito e o numero; a foto e o que deixa alguem
		// discordar dele.
		if (_quadrosDeOnda == 8) Foto("4-no-meio-da-onda");
	}

	/// <summary>
	/// A MAIOR diferenca de luminancia entre duas fotos, na metade que interessa.
	///
	/// MAIOR e nao media: a onda e um anel fino: ela move uma faixa da tela e deixa o resto quieto,
	/// e uma media diluiria o anel no quadro inteiro ate ele sumir na tolerancia.
	/// </summary>
	private double Diferenca(Image a, Image b, bool mundo)
	{
		int w = Math.Min(a.GetWidth(), b.GetWidth());
		int h = Math.Min(a.GetHeight(), b.GetHeight());
		int corte = (int)(h * FaixaDaGui);
		int y0 = mundo ? 0 : corte, y1 = mundo ? corte : h;

		double maior = 0;
		for (int y = y0; y < y1; y += 3)
			for (int x = 0; x < w; x += 3)
				maior = Math.Max(maior, Math.Abs(a.GetPixel(x, y).Luminance - b.GetPixel(x, y).Luminance));
		return maior;
	}

	private Image? Foto(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;

		string caminho = $"user://gota-{nome}.png";
		img.SavePng(caminho);
		Nota($"foto: {ProjectSettings.GlobalizePath(caminho)}");
		return img;
	}

	private void SemQuadro()
	{
		Nota("SEM QUADRO (headless nao renderiza): a bancada inteira e sobre PIXEL, entao ela nao roda.");
		Nota("Rode com janela: --path . --diaggota --position 1920,0 --resolution 1280x720");
		_passo = _passos.Count - 1;   // pula direto pro relatorio
	}

	public override void _Process(double delta)
	{
		if (_passo >= _passos.Count) return;
		_passos[_passo++]();
	}

	private void Terminar()
	{
		GD.Print("");
		GD.Print("========== BANCADA DA GOTA NA TELA ==========");
		foreach (string l in _linhas) GD.Print(l);
		int ok = _linhas.Count(x => x.StartsWith("  OK", StringComparison.Ordinal));
		GD.Print($"===== FIM: {ok} OK, {_falhas} FALHA(S) =====");
		GetTree().Quit(_falhas == 0 ? 0 : 1);
	}
}
