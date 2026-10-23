using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O MAPA DO LUGAR, DESENHADO -- o quadro da aba World com o mundo inteiro em miniatura e o marcador de
/// onde voce esta.
///
/// ============================ ELE SE ATUALIZA SOZINHO, E A PAGINA NAO REMONTA ============================
/// A pagina da aba so e refeita quando a assinatura dela muda (`MenuJogo.Assinatura`), e a posicao do corpo
/// nao pode entrar la: andar com o menu aberto remontaria a aba a cada tile. Entao quem acompanha o corpo e
/// ESTE node, no `_Process`: move o marcador, reescreve a linha das coordenadas quando a celula muda e
/// troca a frase de estado -- sem tocar em mais nada da pagina.
///
/// A IMAGEM NAO E DELE: vem do `MapasDosLugares`, que a compoe uma vez por zona, fora do quadro, e guarda.
/// A pagina pode ser destruida e refeita a vontade (ela e, a cada decimo de hora do ceu): o node novo
/// pergunta de novo e recebe a mesma textura.
///
/// O NORTE E EM CIMA, e o mapa e o RETANGULO INTEIRO do lugar. Num planeta, sair por uma borda e chegar
/// pela oposta (a volta do mundo); o marcador so pula de um lado pro outro junto com o corpo.
///
/// UM CLIQUE NO QUADRO ALTERNA ENTRE O MUNDO INTEIRO E A VISTA DE PERTO (`MapaDoLugar.VistaDePerto`): a
/// mesma imagem, recortada em volta do corpo e ampliada. E o que faz o mapa servir num mundo gerado
/// grande, onde o relevo e miudo e o mundo inteiro encolhido vira um chuvisco.
/// ==========================================================================================================
/// </summary>
public partial class MapaDoLugarNaTela : Control
{
	/// <summary>O lado do quadro, em px de tela. Um planeta de 500 tiles fica a meio pixel por tile.</summary>
	public const float Lado = 264f;

	/// <summary>A altura do quadro quando o lugar nao tem mapa: so cabe o aviso.</summary>
	private const float AlturaSemMapa = 92f;

	private static readonly Color Fundo = new("0e1017");

	/// <summary>A vista de perto esta ligada? Do PROCESSO, e nao do node: a pagina remonta sozinha, e isso nao pode devolver a vista ao mundo inteiro.</summary>
	private static bool _dePerto;

	private Label? _tamanho, _coordenadas, _estado, _dica;
	private Font? _fonte;

	// o que o ultimo quadro calculou -- e o que o `_Draw` pinta
	private MapasDosLugares.Mapa? _mapa;
	private Rect2 _retangulo;
	private Rect2? _recorte;      // o pedaco da textura que a vista de perto mostra, em pixels dela
	private Vector2? _marcador;
	private bool _fora;
	private Facing _olhar;
	private (int X, int Y)? _celula;
	private string _frase = "";

	// ---------------------------------------------------------------- as portas de bancada
	/// <summary>O estado do mapa neste quadro (nulo = a cena ainda esta trocando de zona).</summary>
	public MapasDosLugares.Estado? EstadoDeTeste => _mapa?.Estado;

	/// <summary>Onde o mapa esta desenhado, em pixels deste node.</summary>
	public Rect2 RetanguloDeTeste => _retangulo;

	/// <summary>Onde o marcador esta desenhado, em pixels deste node. Nulo sem mapa ou sem corpo.</summary>
	public Vector2? MarcadorDeTeste => _marcador;

	/// <summary>O marcador esta preso na borda porque o corpo passou do mapa?</summary>
	public bool ForaDeTeste => _fora;

	/// <summary>O mapa que este quadro mostra (os custos de compor e de subir, pra bancada).</summary>
	public MapasDosLugares.Mapa? MapaDeTeste => _mapa;

	/// <summary>A vista de perto esta ligada? E o recorte da textura que ela mostra, em pixels dela (nulo no mundo inteiro).</summary>
	public static bool DePertoDeTeste => _dePerto;
	public Rect2? RecorteDeTeste => _recorte;

	/// <summary>Alterna entre o mundo inteiro e a vista de perto. E o clique no quadro; publico pra bancada.</summary>
	public void AlternarVista()
	{
		_dePerto = !_dePerto;
		Atualizar();
		QueueRedraw();
	}

	public override void _GuiInput(InputEvent e)
	{
		if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
		AlternarVista();
		AcceptEvent();
	}

	/// <summary>
	/// Liga as linhas do cartao que este node mantem em dia: o tamanho, as coordenadas, a frase de estado, e a
	/// dica do clique -- que so aparece onde ha mapa pra clicar.
	/// </summary>
	public void Ligar(Label tamanho, Label coordenadas, Label estado, Label dica)
	{
		_tamanho = tamanho;
		_coordenadas = coordenadas;
		_estado = estado;
		_dica = dica;
		Atualizar();
	}

	public MapaDoLugarNaTela()
	{
		// NO CONSTRUTOR, e nao no `_Ready`: o cartao liga as linhas (`Ligar`) logo depois de criar o node, e a
		// primeira conta ja precisa do tamanho do quadro.
		CustomMinimumSize = new Vector2(Lado, Lado);
		SizeFlagsVertical = SizeFlags.ShrinkBegin;
		TextureFilter = TextureFilterEnum.Linear;   // a imagem encolhe pro quadro: o filtro do pixel art a serrilharia
		MouseFilter = MouseFilterEnum.Stop;   // o clique e do mapa (alterna a vista), e nao do mundo atras do menu
		TooltipText = "O mundo em que você está, com o norte em cima. O marcador é você. "
					+ "Clique para alternar entre o mundo inteiro e a vista de perto.";
	}

	public override void _Ready() => _fonte = ThemeDB.FallbackFont;

	public override void _Process(double delta)
	{
		if (!IsVisibleInTree()) return;
		Atualizar();
		QueueRedraw();
	}

	/// <summary>Le o mundo e poe as tres linhas e o marcador em dia. So escreve texto quando ele muda.</summary>
	private void Atualizar()
	{
		World? mundo = World.Instancia;
		GameClient? cli = GameClient.Instance;
		_mapa = mundo != null && cli != null ? MapasDosLugares.Do(mundo, cli) : null;
		Vector2? eu = mundo?.PosicaoLocal;

		// AS COORDENADAS SEMPRE, com mapa ou sem: sao do corpo, e nao da imagem.
		if (eu is { } p)
		{
			(int X, int Y) celula = MapaDoLugar.Celula(new Vec2(p.X, p.Y));
			if (celula != _celula)
			{
				_celula = celula;
				if (_coordenadas != null) _coordenadas.Text = MapaDoLugar.Coordenadas(new Vec2(p.X, p.Y));
			}
		}
		else if (_celula != null || _coordenadas is { Text.Length: 0 })
		{
			_celula = null;
			if (_coordenadas != null) _coordenadas.Text = "—";
		}

		float altura = _mapa is { Estado: MapasDosLugares.Estado.SemMapa } ? AlturaSemMapa : Lado;
		if (CustomMinimumSize.Y != altura) CustomMinimumSize = new Vector2(Lado, altura);

		_marcador = null;
		_recorte = null;
		_fora = false;
		string frase, tamanho;
		if (_mapa == null)
		{
			frase = "Chegando…";
			tamanho = "—";
		}
		else
		{
			int w = _mapa.LarguraEmTiles, h = _mapa.AlturaEmTiles;
			tamanho = w > 0 && h > 0 ? $"{w} × {h} tiles" : "sem fim";
			_retangulo = Encaixar(w, h);

			bool dePerto = false;
			if (eu is { } q && w > 0 && h > 0 && _mapa.Estado != MapasDosLugares.Estado.SemMapa)
			{
				(float u, float v, bool fora) = MapaDoLugar.Marcador(new Vec2(q.X, q.Y), w, h);
				_fora = fora;
				_olhar = mundo!.OlharLocal;

				if (_dePerto && _mapa.Estado == MapasDosLugares.Estado.Pronto)
				{
					// DE PERTO: a janela em volta do corpo (em tiles) vira o recorte da textura (em pixels dela), e
					// o marcador vai pra onde o corpo esta DENTRO da janela -- o meio, menos junto das bordas.
					dePerto = true;
					(float x0, float y0, float lado, u, v) = MapaDoLugar.VistaDePerto(new Vec2(q.X, q.Y), w, h);
					_retangulo = Encaixar(1, 1);
					_recorte = new Rect2(x0 / _mapa.Bloco, y0 / _mapa.Bloco, lado / _mapa.Bloco, lado / _mapa.Bloco);
				}
				_marcador = _retangulo.Position + new Vector2(u, v) * _retangulo.Size;
			}

			// O FILTRO ACOMPANHA A VISTA: encolhida, a imagem quer o linear; ampliada, o do pixel art (cada tile um quadradinho).
			TextureFilterEnum filtro = dePerto ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
			if (TextureFilter != filtro) TextureFilter = filtro;

			frase = _mapa.Estado switch
			{
				MapasDosLugares.Estado.SemMapa => _mapa.Aviso,
				MapasDosLugares.Estado.Montando => "Desenhando o mapa…",
				_ when _fora => "Você está além da borda do mapa: o marcador para na beirada.",
				_ when dePerto => $"Vista de perto: os {_recorte!.Value.Size.X * _mapa.Bloco:0} tiles em volta de você.",
				_ when cli != null && Espaco.EhPlaneta(cli.Zone) => "Este mundo dá a volta: quem sai por uma borda chega pela oposta.",
				_ => "",
			};
		}

		if (_frase != frase)
		{
			_frase = frase;
			if (_estado != null) { _estado.Text = frase; _estado.Visible = frase.Length > 0; }
		}
		if (_tamanho != null && _tamanho.Text != tamanho) _tamanho.Text = tamanho;

		bool haOQueClicar = _mapa is { Estado: not MapasDosLugares.Estado.SemMapa };
		if (_dica != null && _dica.Visible != haOQueClicar) _dica.Visible = haOQueClicar;
	}

	/// <summary>O maior retangulo com a proporcao do lugar que cabe no quadro, centrado.</summary>
	private Rect2 Encaixar(int larguraEmTiles, int alturaEmTiles)
	{
		var quadro = new Vector2(Lado, CustomMinimumSize.Y);
		if (larguraEmTiles <= 0 || alturaEmTiles <= 0) return new Rect2(Vector2.Zero, quadro);
		float escala = Math.Min(quadro.X / larguraEmTiles, quadro.Y / alturaEmTiles);
		var tamanho = new Vector2(larguraEmTiles, alturaEmTiles) * escala;
		return new Rect2((quadro - tamanho) / 2f, tamanho);
	}

	public override void _Draw()
	{
		var quadro = new Rect2(Vector2.Zero, new Vector2(Lado, CustomMinimumSize.Y));
		DrawRect(quadro, Fundo);

		if (_mapa is { Estado: MapasDosLugares.Estado.Pronto, Textura: { } textura })
		{
			if (_recorte is { } recorte) DrawTextureRectRegion(textura, _retangulo, recorte);
			else DrawTextureRect(textura, _retangulo, false);
			DrawRect(_retangulo, Tema.Borda, false, 1f);
		}
		else
		{
			DrawRect(quadro, Tema.Borda, false, 1f);
			// A FRASE NO MEIO DO QUADRO VAZIO: quem olha pro quadro le por que ele esta vazio sem procurar ao lado.
			// (Desenhada a mao, nao passa pela traducao do Godot: a lingua vem do `Idioma`.)
			if (_fonte != null && _mapa is not { Estado: MapasDosLugares.Estado.SemMapa })
				DrawMultilineString(_fonte, new Vector2(12f, quadro.Size.Y / 2f), Idioma.T(_frase), HorizontalAlignment.Center,
									quadro.Size.X - 24f, 13, -1, Tema.TextoFraco);
			else if (_fonte != null)
				DrawMultilineString(_fonte, new Vector2(12f, quadro.Size.Y / 2f + 5f), Idioma.T("SEM MAPA AQUI"), HorizontalAlignment.Center,
									quadro.Size.X - 24f, 13, -1, Tema.TextoFraco);
		}

		if (_marcador is not { } m) return;

		// O MARCADOR: um anel que pulsa (pra achar de relance num mapa cheio de cor), o ponto, e a seta do olhar.
		// O RELOGIO E O DO PROCESSO, e nao um contador deste node: a pagina remonta sozinha a cada poucos segundos
		// (o texto do ceu muda), e com um contador proprio o anel recomecaria do zero a cada remontagem.
		float t = (float)(Time.GetTicksMsec() % 1400UL / 1400.0);
		Color miolo = _fora ? Tema.Perigo : Tema.Destaque;
		DrawArc(m, 6f + 11f * t, 0, Mathf.Tau, 28, new Color(1, 1, 1, 0.85f * (1f - t)), 1.6f, true);
		DrawCircle(m, 6.2f, new Color(0, 0, 0, 0.75f));
		DrawCircle(m, 4.6f, miolo);
		DrawArc(m, 5.4f, 0, Mathf.Tau, 24, Colors.White, 1.4f, true);

		Vector2 d = _olhar switch
		{
			Facing.North => Vector2.Up,
			Facing.East => Vector2.Right,
			Facing.West => Vector2.Left,
			_ => Vector2.Down,
		};
		var lado = new Vector2(-d.Y, d.X);
		Vector2 ponta = m + d * 13.5f;
		DrawLine(m + d * 8f + lado * 4.2f, ponta, Colors.White, 2f, true);
		DrawLine(m + d * 8f - lado * 4.2f, ponta, Colors.White, 2f, true);
	}
}
