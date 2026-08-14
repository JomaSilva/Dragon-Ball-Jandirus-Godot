using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A TELA DE UM SISTEMA SOLAR: a estrela no meio, os mundos nos aneis de orbita.
///
/// ============================ O PEDIDO, E O QUE ELE MUDA ============================
/// "no nav system atualmente cada pontinho e um planeta, a ideia era q cada pontinho fosse um
/// SISTEMA com 1 a 10 planetas em cada, e ao clicar DUAS VEZES no sistema vc vai abrir o MAPA DO
/// SISTEMA com a estrela no centro e os planetas orbitando em volta".
///
/// A carta estelar (<see cref="MapaEstelar"/>) responde "pra onde eu vou" na escala da galaxia.
/// Esta tela responde a pergunta seguinte, que a carta nao consegue responder em pixel nenhum: uma
/// celula de sistema mede 65.536 px e o sistema inteiro cabe em 23.000 -- no zoom em que a galaxia
/// e legivel, um sistema inteiro tem meio pixel. Sem uma tela propria, "10 planetas em orbita" e
/// uma frase no codigo que o jogador nunca ve.
/// ==================================================================================
///
/// ============================ NADA AQUI E DECIDIDO AQUI ============================
/// Toda posicao, todo raio, toda distancia sai de `Sistemas.Do` -- a mesma funcao pura que o
/// servidor chama. O cliente DESENHA E PEDE (regra 0.3): esta tela nao guarda estado de mundo, nao
/// recebe pacote de mapa e nao inventa corpo nenhum. Fechar e reabrir com a mesma seed devolve o
/// mesmo sistema, ate o ultimo pixel.
///
/// E O CORTE DO QUE SE MOSTRA E O MESMO: a referencia visual que o dono mandou tem excentricidade,
/// massa, densidade, velocidade de escape, diametro em km e composicao atmosferica. O gerador NAO
/// produz nenhum desses. Numero inventado numa tela de dados e pior que campo ausente, porque o
/// jogador acredita -- entao esses campos simplesmente nao existem aqui. O que sobrou e tudo
/// derivado de verdade: semieixo (que E a distancia, porque a orbita e circular), gravidade, lado
/// do mundo em tiles, bioma, clima possivel, hora local e tempo de viagem.
/// ==================================================================================
/// </summary>
public partial class TelaDoSistema : Control
{
	// =====================================================================
	// O QUE ESTA TELA MOSTRA
	// =====================================================================
	private SistemaSolar _sis;
	private bool _tem;

	/// <summary>Qual corpo esta detalhado: -1 = a estrela, 0.. = a orbita. Nulo = nada.</summary>
	private int? _alvo;

	/// <summary>Devolve quem esta selecionado como PLANETA, ou nulo se e a estrela (ou nada).</summary>
	public PlanetaNoEspaco? Selecionado =>
		_tem && _alvo is { } k && k >= 0 && k < _sis.Orbitas ? _sis.Planeta(k) : null;

	public event Action? SelecaoMudou;
	public event Action<PlanetaNoEspaco>? PediuViagem;

	/// <summary>Viajar ate o PORTO do sistema -- o ponto onde o piloto larga quem veio "pro sistema".</summary>
	public event Action<Vector2>? PediuPorto;

	public event Action? PediuVoltar;

	// =====================================================================
	// CAMERA -- em coordenadas LOCAIS, com a estrela na origem
	// =====================================================================
	/// <summary>
	/// POR QUE LOCAL E NAO MUNDO: um sistema pode estar a 10 milhoes de px da origem, e la um
	/// `float` so representa multiplos de ~1 px. Desenhar em coordenada de mundo faria a orbita
	/// interna tremer conforme a camera anda, longe da Terra e nao perto dela -- o tipo de defeito
	/// que so aparece em metade do universo. Subtraindo a estrela, tudo cabe em +-23.000.
	/// </summary>
	private Vector2 _centro;
	private float _escala = 0.01f;

	private const float EscalaMin = 0.0015f;
	private const float EscalaMax = 1.2f;
	private const float PassoDeZoom = 1.25f;

	private bool _arrastando;
	private Vector2 _ultimoMouse;

	// =====================================================================
	// AS ALTERNANCIAS -- o que se liga e desliga
	// =====================================================================
	private bool _aneis = true, _rotulos = true, _habitavel = true, _tamanhoReal;

	/// <summary>Altura da barra de controles. O mapa comeca abaixo dela.</summary>
	private const float BarraAlta = 36f;

	/// <summary>Largura do painel de detalhe, encostado a direita.</summary>
	private const float PainelLargo = 246f;

	/// <summary>
	/// A LARGURA QUE O PAINEL REALMENTE TOMA -- nunca mais que 45% do widget.
	///
	/// ============================ A BANCADA ACHOU ESTE ============================
	/// Sem o teto, num widget estreito o painel de 246 px comia a largura inteira e o MEIO DO MAPA
	/// caia DENTRO da area do painel. Consequencia medida: `CorpoEm` recusava a estrela, porque a
	/// primeira coisa que ele faz e "clique dentro do painel nao muda a selecao" -- e o centro do
	/// sistema estava, literalmente, debaixo do painel. Clicar no sol nao selecionava o sol, e nada
	/// no desenho explicava por que.
	///
	/// A largura do widget depende do menu, do tema e da janela do jogador; um numero fixo que
	/// funciona na minha tela e um bug na dele.
	/// ============================================================================
	/// </summary>
	private float PainelUsado => Mathf.Min(PainelLargo, Size.X * 0.45f);

	private HBoxContainer _barra = null!;

	// =====================================================================
	// CICLO
	// =====================================================================
	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
		// MAIS ALTA QUE A CARTA (330): aqui o painel de detalhe divide a largura com o mapa, e com
		// 330 os tres blocos do painel nao cabiam sem rolagem dentro do proprio painel.
		CustomMinimumSize = new Vector2(0, 460);
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		FocusMode = FocusModeEnum.Click;

		MontarBarra();
		Resized += () =>
		{
			PosicionarBarra();
			// ENQUADRAR DE NOVO QUANDO O TAMANHO CHEGAR.
			//
			// Esta tela nasce escondida e so e mostrada no duplo clique -- e no instante do
			// `Mostrar` o container ainda nao mediu nada, entao `Size` e zero e a moldura sai no
			// zoom minimo. Sintoma medido na bancada: o sistema inteiro cabia em 9 px e a orbita
			// interna caia a 2,4 px do centro do sol. E a mesma armadilha que o `MapaEstelar` ja
			// documenta no `_Ready` dele.
			if (_tem && !_enquadrouComTamanho) Reenquadrar();
		};
		PosicionarBarra();
	}

	/// <summary>Ja houve um enquadramento com tamanho de verdade? Ver o `Resized` acima.</summary>
	private bool _enquadrouComTamanho;

	public override void _Process(double delta) => QueueRedraw();

	/// <summary>
	/// Aponta a tela pra um sistema. E o unico jeito de ela ganhar conteudo -- ela nao busca nada
	/// sozinha, porque quem escolheu o sistema foi o clique na carta.
	/// </summary>
	public void Mostrar(SistemaSolar s)
	{
		_sis = s;
		_tem = true;
		_alvo = null;
		Reenquadrar();
		SelecaoMudou?.Invoke();
	}

	// =====================================================================
	// A BARRA
	// =====================================================================
	/// <summary>
	/// OS CONTROLES SAO WIDGETS DE VERDADE, e nao retangulos desenhados.
	///
	/// Um botao pintado no `_Draw` nao tem foco, nao tem teclado, nao tem dica e nao herda o tema --
	/// e esta tela mora dentro do mesmo menu que ja e todo feito de `Button`. O mapa desenhado
	/// comeca ABAIXO da barra por isso.
	/// </summary>
	private void MontarBarra()
	{
		_barra = new HBoxContainer { Name = "Controles" };
		AddChild(_barra);

		var voltar = new Button { Text = "◀ voltar", TooltipText = "volta pra carta da galaxia" };
		voltar.Pressed += () => PediuVoltar?.Invoke();
		_barra.AddChild(voltar);

		var reenq = new Button { Text = "reenquadrar", TooltipText = "poe o sistema inteiro na tela" };
		reenq.Pressed += Reenquadrar;
		_barra.AddChild(reenq);

		Alternar("orbitas", "desenha o anel de cada orbita", () => _aneis, v => _aneis = v);
		Alternar("rotulos", "o nome de cada mundo", () => _rotulos, v => _rotulos = v);
		Alternar("zona habitavel", "a faixa entre 3 e 7 raios da estrela", () => _habitavel, v => _habitavel = v);

		// ============================ POR QUE ESTE BOTAO EXISTE ============================
		// Um mundo mede 110 a 220 px de raio e as orbitas distam 1.400 a 2.200 px: no tamanho REAL
		// o maior planeta e 1/10 do vao entre dois aneis, ou seja um ponto de 2 px na tela cheia.
		// Isso e a verdade da escala e tem que estar disponivel -- mas nao pode ser o padrao, porque
		// uma tela em que nada da pra clicar nao e um mapa.
		//
		// A referencia tinha TAMBEM um botao de "escala por orbita x escala real" pras distancias.
		// Aqui ele seria mentira de interface: `Sistemas` poe as orbitas em progressao LINEAR de
		// proposito (`a_k = a0 + k*passo`, comentario "linear e nao geometrica"), entao a razao
		// entre a orbita externa e a interna nunca passa de 7,8x -- o sistema inteiro ja e legivel
		// em escala real, e o botao so trocaria um desenho honesto por outro igual.
		// =================================================================================
		Alternar("tamanho real", "os mundos no tamanho que eles tem de verdade (minusculos)",
				 () => _tamanhoReal, v => _tamanhoReal = v);
	}

	private void Alternar(string rotulo, string dica, Func<bool> ler, Action<bool> escrever)
	{
		var c = new CheckBox { Text = rotulo, TooltipText = dica, ButtonPressed = ler() };
		c.Toggled += v => { escrever(v); QueueRedraw(); };
		_barra.AddChild(c);
	}

	private void PosicionarBarra()
	{
		_barra.Position = Vector2.Zero;
		_barra.Size = new Vector2(Size.X, BarraAlta);
	}

	// =====================================================================
	// CONVERSAO
	// =====================================================================
	/// <summary>A area do desenho: tudo menos a barra.</summary>
	private Rect2 Area => new(0, BarraAlta, Size.X, Mathf.Max(Size.Y - BarraAlta, 1));

	/// <summary>
	/// O meio do DESENHO, que nao e o meio do widget.
	///
	/// O painel de detalhe come a direita, entao o mapa e centrado no que sobra. E ele desconta o
	/// painel SEMPRE, mesmo sem nada selecionado: descontar so quando ha selecao fazia o sistema
	/// inteiro PULAR pro lado no instante do clique -- o corpo que voce acabou de mirar saia de
	/// debaixo do cursor.
	/// </summary>
	private Vector2 MeioDoMapa
	{
		get
		{
			Rect2 a = Area;
			float util = Mathf.Max(a.Size.X - PainelUsado, 60);
			return new Vector2(util / 2f, a.Position.Y + a.Size.Y / 2f);
		}
	}

	private Vector2 ParaTela(Vector2 local) => (local - _centro) * _escala + MeioDoMapa;

	private Vector2 ParaLocal(Vector2 tela) => (tela - MeioDoMapa) / _escala + _centro;

	/// <summary>A posicao LOCAL de um corpo -- a de mundo menos a da estrela. Ver `_centro`.</summary>
	private Vector2 Local(Vec2 mundo) =>
		new(mundo.X - _sis.Estrela.Pos.X, mundo.Y - _sis.Estrela.Pos.Y);

	// =====================================================================
	// ENTRADA
	// =====================================================================
	public override void _GuiInput(InputEvent e)
	{
		if (!_tem) return;

		if (e is InputEventMouseButton b)
		{
			switch (b.ButtonIndex)
			{
				// A RODA DA ZOOM E NAO ROLA A PAGINA -- mesmo freio da carta, pelo mesmo motivo, e o
				// bloco comprido que explica o `AcceptEvent` mora la (`MapaEstelar._GuiInput`): o
				// `MouseFilter = Stop` nao segura roda porque `mouse_force_pass_scroll_events` nasce
				// ligado, e o freio tem que ser no braco que TRATOU o evento.
				//
				// Aqui era pior que desconforto: quando esta tela abre, `MenuJogo` esconde a barra de
				// "+/-" da carta (ela e da carta) e a barra propria desta tela nao tem botao de zoom
				// nenhum -- a roda e o UNICO jeito de aproximar de um planeta. As duas telas sao irmas
				// na arvore e o manuseio delas tem que ser o mesmo.
				case MouseButton.WheelUp when b.Pressed: Aproximar(PassoDeZoom, b.Position); AcceptEvent(); return;
				case MouseButton.WheelDown when b.Pressed: Aproximar(1f / PassoDeZoom, b.Position); AcceptEvent(); return;

				case MouseButton.Left when b.Pressed:
					GrabFocus();
					int? achado = CorpoEm(b.Position);
					if (achado != null)
					{
						// DUPLO CLIQUE VIAJA -- o mesmo gesto da carta, e o mesmo do alvo no mundo.
						if (b.DoubleClick && _alvo == achado)
						{
							if (achado.Value < 0)
							{
								Vec2 porto = _sis.PortoDeEntrada;
								PediuPorto?.Invoke(new Vector2(porto.X, porto.Y));
							}
							else PediuViagem?.Invoke(_sis.Planeta(achado.Value));
							return;
						}
						_alvo = achado;
						SelecaoMudou?.Invoke();
						return;
					}
					_arrastando = true;
					_ultimoMouse = b.Position;
					return;

				case MouseButton.Left: _arrastando = false; return;
			}
		}

		if (e is InputEventMouseMotion m && _arrastando)
		{
			// O MUNDO ACOMPANHA O DEDO -- por isso a subtracao. Igual a carta.
			_centro -= (m.Position - _ultimoMouse) / _escala;
			_ultimoMouse = m.Position;
			QueueRedraw();
		}
	}

	/// <summary>Zoom ancorado no cursor: o ponto sob o mouse continua sob o mouse.</summary>
	private void Aproximar(float fator, Vector2 ancora)
	{
		Vector2 antes = ParaLocal(ancora);
		_escala = Mathf.Clamp(_escala * fator, EscalaMin, EscalaMax);
		_centro += antes - ParaLocal(ancora);
		QueueRedraw();
	}

	/// <summary>
	/// Poe o sistema inteiro na tela.
	///
	/// O vao usa o RAIO DO SISTEMA e nao o corpo mais externo: sao a mesma coisa por construcao
	/// (`RaioDoSistema` alcanca o corpo mais externo) e usar o campo do Core garante que a moldura
	/// nao precise ser corrigida se um dia o gerador mudar o numero de orbitas.
	/// </summary>
	public void Reenquadrar()
	{
		if (!_tem) return;
		_centro = Vector2.Zero;
		_enquadrouComTamanho = Size.X > 1 && Size.Y > 1;

		Rect2 a = Area;
		float util = Mathf.Max(a.Size.X - PainelUsado, 120);
		// 2,3 e nao 2,0: o rotulo do corpo externo e desenhado ABAIXO do disco, e com a moldura
		// justa ele saia da area. Mesma correcao que a carta ja carrega no `VerTudo`.
		float vao = (float)_sis.RaioDoSistema * 2.3f;
		_escala = Mathf.Clamp(Mathf.Min(util, a.Size.Y) / Mathf.Max(vao, 1), EscalaMin, EscalaMax);
		QueueRedraw();
	}

	// =====================================================================
	// QUEM ESTA ONDE
	// =====================================================================
	/// <summary>O raio do corpo na TELA. No modo legivel ele tem um piso -- ver o botao.</summary>
	private float RaioNaTela(float raioDeMundo) =>
		_tamanhoReal ? Mathf.Max(raioDeMundo * _escala, 1f)
					 : Mathf.Clamp(raioDeMundo * _escala, 5f, 60f);

	private float RaioDaEstrelaNaTela() =>
		_tamanhoReal ? Mathf.Max(_sis.Estrela.Raio * _escala, 2f)
					 : Mathf.Clamp(_sis.Estrela.Raio * _escala, 9f, 150f);

	/// <summary>
	/// O CORPO SOB O PONTO: -1 pra estrela, 0.. pra orbita, nulo pro vazio.
	///
	/// ============================ QUEM GANHA O EMPATE, E POR QUE NAO E "O PLANETA SEMPRE" ============================
	/// A primeira versao dava a vitoria ao planeta em qualquer empate, com a justificativa de que uma
	/// gigante com 150 px de disco engole a orbita interna e o clique no mundo iria pro sol. A
	/// bancada mostrou o outro lado: com o sistema todo enquadrado, a orbita interna cai a 2,4 px do
	/// CENTRO da estrela -- entao clicar exatamente no sol selecionava o primeiro planeta, e a
	/// estrela ficava impossivel de selecionar. Nao era caso de borda: era o enquadramento padrao.
	///
	/// A regra certa e a simetrica: ganha quem tem o CENTRO mais perto do cursor, cada um ainda
	/// preso ao proprio raio de acerto. No centro do sol o sol ganha (distancia 0), em cima do
	/// planeta o planeta ganha (distancia 0), e no meio ganha o mais proximo.
	/// ============================================================================================================
	/// </summary>
	private int? CorpoEm(Vector2 tela)
	{
		if (_alvo != null && tela.X > Area.Size.X - PainelUsado) return _alvo;   // clique no painel nao muda nada

		int? melhor = null;
		float melhorDist = float.MaxValue;

		for (int k = 0; k < _sis.Orbitas; k++)
		{
			PlanetaNoEspaco p = _sis.Planeta(k);
			float d = ParaTela(Local(p.Pos)).DistanceTo(tela);
			// ALVO GENEROSO: o disco pode ter 2 px no tamanho real, e ninguem acerta 2 px.
			if (d > Mathf.Max(RaioNaTela(p.Raio) + 9f, 12f) || d >= melhorDist) continue;
			melhor = k; melhorDist = d;
		}

		float dEstrela = ParaTela(Vector2.Zero).DistanceTo(tela);
		if (dEstrela <= Mathf.Max(RaioDaEstrelaNaTela(), 12f) && dEstrela < melhorDist) return -1;

		return melhor;
	}

	// =====================================================================
	// DESENHO
	// =====================================================================
	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color("070912"));
		if (!_tem)
		{
			DrawString(ThemeDB.FallbackFont, new Vector2(14, BarraAlta + 24),
					   "nenhum sistema aberto", HorizontalAlignment.Left, -1, 13, Tema.TextoFraco);
			return;
		}

		DesenharZonaHabitavel();
		DesenharAneis();
		DesenharEstrela();
		for (int k = 0; k < _sis.Orbitas; k++) DesenharPlaneta(k);
		DesenharEu();
		DesenharTitulo();
		DesenharLegenda();
		DesenharPainel();
		DrawRect(new Rect2(Vector2.Zero, Size), Tema.Borda, false, 1);
	}

	/// <summary>
	/// A FAIXA VERDE: [3R, 7R] a partir da estrela.
	///
	/// Desenhada como um ARCO GROSSO e nao como dois circulos: e a faixa que e a informacao, e dois
	/// aneis finos leem como mais duas orbitas -- exatamente a confusao que ela deveria evitar.
	/// </summary>
	private void DesenharZonaHabitavel()
	{
		if (!_habitavel) return;
		(double dentro, double fora) = Sistemas.ZonaHabitavelDe(_sis.Estrela.Raio);

		float ri = (float)dentro * _escala, ro = (float)fora * _escala;
		if (ro - ri < 0.6f) return;   // fina demais pra ler: melhor nada do que uma linha ambigua

		DrawArc(ParaTela(Vector2.Zero), (ri + ro) / 2f, 0, Mathf.Tau, 96,
				new Color(Tema.Bom, 0.13f), ro - ri);
	}

	private void DesenharAneis()
	{
		if (!_aneis) return;
		Vector2 c = ParaTela(Vector2.Zero);
		for (int k = 0; k < _sis.Orbitas; k++)
		{
			float r = (float)_sis.Semieixo(k) * _escala;
			if (r < 2f) continue;
			bool marcado = _alvo == k;
			DrawArc(c, r, 0, Mathf.Tau, 96,
					new Color(Tema.Borda, marcado ? 0.95f : 0.5f), marcado ? 1.8f : 1f);
		}
	}

	/// <summary>
	/// A ESTRELA -- a folha de arte, escalada pelo NUCLEO MEDIDO, e o anel do raio que MATA.
	///
	/// ============================ O ANEL NAO E ENFEITE ============================
	/// `SistemasBench` avisa que a assinatura fica verde com a estrela matando 37% fora de onde
	/// parece, porque nao ha pixel nenhum dentro dela -- "a prova daquilo e uma FOTO com o anel da
	/// coroa desenhado sobre o raio". Este anel E essa prova, e ela fica ligada no jogo: se um dia
	/// a folha e o raio letal se desencontrarem, da pra VER, sem bancada nenhuma.
	///
	/// E AGORA ELE QUEIMA. A Fase 3 fechou com o rotulo dizendo "raio da estrela" e a nota dizendo
	/// "a letalidade nao esta ligada", porque naquele dia nao havia uma linha de codigo que ferisse
	/// ninguem -- prometer na tela o que a regra nao faz e a armadilha 1 da Parte 3, e quem
	/// acreditaria era o jogador. A Fase 4 ligou (`GameServer.Sol.cs` + `Core.World.CalorDaEstrela`),
	/// entao a palavra volta: **raio letal**, com o numero que este corpo especifico aguenta.
	///
	/// E o numero NAO e "morte": e dano por segundo, e por isso a tela mostra SEGUNDOS. A diferenca
	/// e do dono e e o coracao do desenho -- da pra entrar e sair.
	/// ============================================================================
	/// </summary>
	private void DesenharEstrela()
	{
		Vector2 c = ParaTela(Vector2.Zero);
		float r = RaioDaEstrelaNaTela();

		Texture2D? t = ArteDeEstrela.Textura(_sis.Estrela);
		float lado = ArteDeEstrela.LadoDaFolha(_sis.Estrela, r);
		if (t != null && lado > 1)
			DrawTextureRect(t, new Rect2(c - new Vector2(lado, lado) / 2f, lado, lado), false);
		else
			// SEM ARTE, UM DISCO -- e nao um buraco. Faltando o `estrelas.json` (ou o import), a
			// tela continua legivel em vez de mostrar um sistema sem sol.
			DrawCircle(c, r, ArteDeEstrela.Halo(_sis.Estrela));

		DrawArc(c, r, 0, Mathf.Tau, 64, new Color(Tema.Perigo, 0.85f), 1.5f);
		if (_alvo == -1) DrawArc(c, r + 6, 0, Mathf.Tau, 64, Tema.Bom, 2f);
	}

	private void DesenharPlaneta(int k)
	{
		PlanetaNoEspaco p = _sis.Planeta(k);
		Vector2 c = ParaTela(Local(p.Pos));
		float r = RaioNaTela(p.Raio);
		if (c.X < -40 || c.Y < -40 || c.X > Size.X + 40 || c.Y > Size.Y + 40) return;

		Color cor = CorDoCorpo(p);
		DrawCircle(c, r + 2f, new Color(cor, 0.22f));
		DrawCircle(c, r, cor);

		// O PRE-FEITO GANHA UM ANEL. Ele nao e "melhor": e o que tem mapa desenhado a mao em vez de
		// terreno sorteado, e essa e a diferenca que muda o que o jogador encontra ao pousar.
		if (p.Premade) DrawArc(c, r + 3.5f, 0, Mathf.Tau, 28, new Color(Tema.Destaque, 0.9f), 1.5f);

		if (_alvo == k)
		{
			DrawArc(c, r + 7, 0, Mathf.Tau, 32, Tema.Bom, 2f);
			DrawArc(c, r + 11, 0, Mathf.Tau, 32, new Color(Tema.Bom, 0.35f), 1f);
		}

		if (!_rotulos) return;
		Font f = ThemeDB.FallbackFont;
		const int tam = 11;
		Vector2 medida = f.GetStringSize(p.Nome, HorizontalAlignment.Left, -1, tam);
		DrawString(f, c + new Vector2(-medida.X / 2f, r + tam + 3), p.Nome,
				   HorizontalAlignment.Left, -1, tam, p.Premade ? Tema.Texto : Tema.TextoFraco);
	}

	/// <summary>
	/// VOCE ESTA AQUI -- so se eu estiver DENTRO deste sistema.
	///
	/// A cruz e a mesma da carta. Ela some quando estou noutro sistema em vez de ser desenhada na
	/// borda: uma marca encostada na moldura leria como "estou na beirada daqui", que e falso.
	/// </summary>
	private void DesenharEu()
	{
		if (MapaEstelar.MinhaPosicaoNaGalaxia() is not { } eu) return;
		Vector2 l = eu - new Vector2(_sis.Estrela.Pos.X, _sis.Estrela.Pos.Y);
		if (l.Length() > _sis.RaioDoSistema * 1.15) return;

		Vector2 c = ParaTela(l);
		DrawArc(c, 7, 0, Mathf.Tau, 20, Tema.Ki, 2f);
		DrawLine(c + new Vector2(-11, 0), c + new Vector2(-4, 0), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(4, 0), c + new Vector2(11, 0), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(0, -11), c + new Vector2(0, -4), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(0, 4), c + new Vector2(0, 11), Tema.Ki, 1.5f);
	}

	private void DesenharTitulo()
	{
		Font f = ThemeDB.FallbackFont;
		string t = $"{_sis.NomeDaEstrela}  ·  estrela {ArteDeEstrela.NomeDaClasse(_sis.Estrela.Classe)}"
				 + $"  ·  {_sis.Orbitas} mundo{(_sis.Orbitas == 1 ? "" : "s")}";
		DrawString(f, new Vector2(10, BarraAlta + 15), t, HorizontalAlignment.Left, -1, 13, Tema.Destaque);
	}

	/// <summary>
	/// A LEGENDA -- e SO com o que esta na tela.
	///
	/// Listar os seis biomas sempre encheria a legenda de entradas que nao correspondem a nenhum
	/// ponto desenhado, e uma legenda que descreve o que nao esta ali ensina a nao olhar pra ela.
	/// </summary>
	private void DesenharLegenda()
	{
		Font f = ThemeDB.FallbackFont;
		const int tam = 10;
		float y = Size.Y - 8;

		var itens = new List<(Color cor, string texto)>();
		if (_habitavel) itens.Add((new Color(Tema.Bom, 0.45f), "zona habitavel"));

		var vistos = new HashSet<string>();
		for (int k = _sis.Orbitas - 1; k >= 0; k--)
		{
			PlanetaNoEspaco p = _sis.Planeta(k);
			string nome = p.Premade ? "mundo com mapa proprio" : NomeDoBioma(p);
			if (vistos.Add(nome)) itens.Add((CorDoCorpo(p), nome));
		}
		// O ANEL VERMELHO E O RAIO LETAL -- e agora ele merece o nome. Ate a Fase 3 dizia "raio da
		// estrela" porque nenhuma linha de codigo feria ninguem; ver `DesenharEstrela`.
		itens.Add((new Color(Tema.Perigo, 0.85f), "raio letal"));

		foreach ((Color cor, string texto) in itens)
		{
			DrawRect(new Rect2(10, y - 8, 9, 9), cor);
			DrawString(f, new Vector2(24, y), texto, HorizontalAlignment.Left, -1, tam, Tema.TextoFraco);
			y -= 14;
		}
	}

	// =====================================================================
	// O PAINEL DE DETALHE
	// =====================================================================
	/// <summary>
	/// A FICHA DO CORPO SELECIONADO.
	///
	/// TRES BLOCOS, como a referencia: orbita, fisico e ceu. O terceiro nao se chama "atmosfera"
	/// porque nao ha atmosfera modelada -- ha um ciclo de dia/noite por planeta e uma lista de
	/// climas que podem cair ali, e e isso que ele diz.
	///
	/// Desenhado e nao montado com `Label`: sao ate quinze linhas que mudam a cada clique, e
	/// remontar quinze nodes por clique dentro de um `ScrollContainer` faz a pagina inteira
	/// re-medir e o mapa piscar. O texto aqui e conteudo de desenho, como os rotulos dos planetas.
	/// </summary>
	private void DesenharPainel()
	{
		if (_alvo is not { } k) return;

		Rect2 a = Area;
		float largo = PainelUsado;
		var caixa = new Rect2(Size.X - largo, a.Position.Y, largo, a.Size.Y);
		DrawRect(caixa, new Color("0d1018ee"));
		DrawLine(caixa.Position, caixa.Position + new Vector2(0, caixa.Size.Y), Tema.Borda, 1);

		Font f = ThemeDB.FallbackFont;
		float x = caixa.Position.X + 10, y = caixa.Position.Y + 18;
		float larguraUtil = largo - 20;

		void Titulo(string s)
		{
			DrawString(f, new Vector2(x, y), s, HorizontalAlignment.Left, larguraUtil, 13, Tema.Destaque);
			y += 20;
		}
		void Bloco(string s)
		{
			y += 4;
			DrawString(f, new Vector2(x, y), s, HorizontalAlignment.Left, larguraUtil, 10, Tema.BordaViva);
			y += 14;
		}
		void Linha(string rotulo, string valor)
		{
			DrawString(f, new Vector2(x, y), rotulo, HorizontalAlignment.Left, 92, 11, Tema.TextoFraco);
			DrawString(f, new Vector2(x + 96, y), valor, HorizontalAlignment.Left, larguraUtil - 96, 11, Tema.Texto);
			y += 15;
		}
		void Nota(string s)
		{
			y += 2;
			DrawString(f, new Vector2(x, y), s, HorizontalAlignment.Left, larguraUtil, 10, Tema.TextoFraco);
			y += 13;
		}

		if (k < 0) { FichaDaEstrela(Titulo, Bloco, Linha, Nota); return; }
		FichaDoMundo(k, Titulo, Bloco, Linha, Nota);
	}

	private void FichaDaEstrela(Action<string> Titulo, Action<string> Bloco,
								Action<string, string> Linha, Action<string> Nota)
	{
		Estrela e = _sis.Estrela;
		Titulo(_sis.NomeDaEstrela);

		Bloco("ESTRELA");
		Linha("classe", ArteDeEstrela.NomeDaClasse(e.Classe));
		Linha("raio letal", $"{e.Raio:N0} px");
		// ============================ O PODER DA ESTRELA PODE APARECER; O SEU, NAO ============================
		// `CalorDaEstrela.PoderDe` e um numero absoluto no mesmo eixo do BP, e mesmo assim ele sai
		// aqui: o sigilo esconde o poder de PESSOAS (`GameServer.Sigilo`), e a razao dele e que ler o
		// numero do outro mata a leitura de luta. Uma estrela nao e um lutador -- e o terreno.
		//
		// E E POR ISSO QUE ESTA TELA NAO DIZ "voce aguenta N segundos": pra calcular isso ela
		// precisaria do PROPRIO `expressedBP`, que chega NaN sem scouter. Quem tem o numero e o
		// servidor, e e ele que fala -- pelo chat, quando o corpo entra na coroa (`GameServer.Sol`).
		// Uma linha aqui que so funcionasse com scouter seria uma linha que hoje nunca aparece.
		// ==================================================================================================
		Linha("poder", $"{CalorDaEstrela.PoderDe(e.Classe):N0}");
		// TRAVESSIA: quanto tempo o corpo passaria DENTRO, de ponta a ponta, na velocidade de voo
		// base. E o outro lado da conta que o aviso do servidor da (quanto o corpo aguenta) -- com
		// os dois numeros na mao, "da pra atravessar?" vira uma pergunta que se responde.
		Linha("travessia", $"{Espaco.SegundosDeViagem(e.Raio * 2):0} s de voo base");
		Linha("mundos", _sis.Orbitas.ToString());

		(double dentro, double fora) = Sistemas.ZonaHabitavelDe(e.Raio);
		int naFaixa = 0;
		for (int i = 0; i < _sis.Orbitas; i++) if (_sis.Habitavel(i)) naFaixa++;
		Bloco("ZONA HABITAVEL");
		Linha("faixa", $"{dentro:N0} a {fora:N0} px");
		Linha("mundos nela", naFaixa == 0 ? "nenhum" : naFaixa.ToString());

		Bloco("CHEGADA");
		Vec2 porto = _sis.PortoDeEntrada;
		Linha("porto", $"{_sis.A0:N0} px da estrela");
		if (MapaEstelar.MinhaPosicaoNaGalaxia() is { } eu)
		{
			double d = (new Vector2(porto.X, porto.Y) - eu).Length();
			Linha("daqui", Espaco.DiasInGame(d) >= 1
				? $"{Espaco.DiasInGame(d):0.0} dias in-game"
				: $"{Espaco.DiasInGame(d) * 24:0.0} h in-game");
		}

		// O QUE A TELA PODE DIZER E O QUE O JOGO FAZ HOJE -- e hoje ele queima. A nota diz DANO POR
		// SEGUNDO e nao "mata": a diferenca e do dono (*"se vc cair la comeca a tomar MUITO dano"*) e
		// e ela que faz o jogador tentar sair em vez de aceitar a tela de morte.
		Nota("Dentro do raio o corpo QUEIMA:");
		Nota("dano por segundo, ate ceder.");
		Nota("Quanto maior o seu poder, mais");
		Nota("tempo -- o servidor avisa na coroa.");
		Nota("Duplo clique: viajar ate o porto.");
	}

	private void FichaDoMundo(int k, Action<string> Titulo, Action<string> Bloco,
							  Action<string, string> Linha, Action<string> Nota)
	{
		PlanetaNoEspaco p = _sis.Planeta(k);
		Titulo(p.Nome);

		Bloco("ORBITA");
		Linha("estrela", ArteDeEstrela.NomeDaClasse(_sis.Estrela.Classe));
		Linha("orbita", $"#{k} de {_sis.Orbitas}");
		Linha("semieixo", $"{_sis.Semieixo(k):N0} px");
		// EXCENTRICIDADE ZERO E UM FATO, e nao um enchimento: as orbitas sao circulares por
		// construcao (`SistemaSolar.Semieixo` e um raio, nao um eixo de elipse). Por isso perielio
		// e afelio nao aparecem -- os dois seriam o mesmo numero ja mostrado acima.
		Linha("excentricidade", "0,00 (circular)");
		Linha("zona habitavel", _sis.Habitavel(k) ? "sim" : "nao");

		Bloco("FISICO");
		Linha("raio do disco", $"{p.Raio:N0} px");
		if (p.Premade)
		{
			double g = Planetas.Catalogo?.De(p.Nome).Gravidade ?? 1;
			Linha("gravidade", $"{g:0.##}x");
			Linha("superficie", "mapa proprio");
		}
		else
		{
			MundoProcedural m = MundoProcedural.DaSeed(p.Seed, p.Nome);
			Linha("gravidade", $"{m.Gravidade:0.##}x");
			Linha("bioma", m.Bioma.ToString());
			Linha("mundo", $"{m.Lado}x{m.Lado} tiles");
		}

		Bloco("CEU");
		ZoneKey zona = p.Premade ? ZoneKey.Premade(p.Nome) : ZoneKey.Procedural(p.Nome, p.Seed);
		Linha("agora", HoraLa(zona));
		ClimaDoPlaneta c = Planetas.Clima(zona);
		Linha("clima", !c.Existe || c.Permitidos.Length == 0
			? "sempre limpo"
			: string.Join(", ", c.Permitidos.Select(Clima.Nome)));

		if (MapaEstelar.MinhaPosicaoNaGalaxia() is { } eu)
		{
			double d = (new Vector2(p.Pos.X, p.Pos.Y) - eu).Length();
			Bloco("VIAGEM");
			Linha("distancia", $"{d:N0} px");
			Linha("tempo", Espaco.DiasInGame(d) >= 1
				? $"{Espaco.DiasInGame(d):0.0} dias in-game"
				: $"{Espaco.DiasInGame(d) * 24:0.0} h in-game");
			Linha("real", $"{Espaco.SegundosDeViagem(d) / 60:0} min");
		}

		Nota("A orbita e PARADA: a posicao");
		Nota("nao muda com o tempo do mundo.");
	}

	/// <summary>Que horas sao naquele mundo -- funcao pura do relogio sincronizado, sem pedir nada.</summary>
	private static string HoraLa(ZoneKey zona)
	{
		RelogioDoPlaneta r = Planetas.Relogio(zona);
		if (GameClient.Instance is not { TempoChegou: true } cli) return Ceu.NomeDoCiclo(r);

		EstadoDoCeu e = Ceu.De(r, cli.TempoDoMundo);
		string hora = Ceu.NomeDaHora(e.Hora);
		return e.LuaNoCeu ? $"{hora}, {Ceu.NomeDaFase(e.Fase)}" : hora;
	}

	// =====================================================================
	// CORES
	// =====================================================================
	/// <summary>
	/// A COR DE UM MUNDO SAI DO BIOMA DELE -- que sai da seed, pela mesma conta do terreno.
	///
	/// Nao e decoracao: o bioma decide o chao que vai aparecer ao pousar e os climas que caem la.
	/// Pintar todo mundo gerado da mesma cor jogaria fora a unica informacao que o mapa consegue
	/// dar de relance.
	/// </summary>
	private static Color CorDoCorpo(PlanetaNoEspaco p)
	{
		if (p.Premade) return Tema.Destaque;
		return GeradorDeTerreno.BiomaDaSeed(p.Seed) switch
		{
			BiomaDeTerreno.Jardim => new Color("5fc46a"),
			BiomaDeTerreno.Deserto => new Color("d9b45e"),
			BiomaDeTerreno.Gelado => new Color("8fd4ea"),
			BiomaDeTerreno.Vulcanico => new Color("d8563f"),
			BiomaDeTerreno.Morto => new Color("8a7fa8"),
			_ => new Color("9aa3b8"),
		};
	}

	private static string NomeDoBioma(PlanetaNoEspaco p) =>
		GeradorDeTerreno.BiomaDaSeed(p.Seed) switch
		{
			BiomaDeTerreno.Jardim => "mundo jardim",
			BiomaDeTerreno.Deserto => "deserto",
			BiomaDeTerreno.Gelado => "gelado",
			BiomaDeTerreno.Vulcanico => "vulcanico",
			BiomaDeTerreno.Morto => "morto",
			_ => "rochoso",
		};

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagnav`)
	// =====================================================================
	/// <summary>O sistema aberto, ou nulo. So pra bancada -- o `_Draw` nao devolve nada.</summary>
	public SistemaSolar? SistemaDeTeste => _tem ? _sis : null;

	/// <summary>
	/// O BOTAO "VOLTAR", sem botao. SO PRA BANCADA.
	///
	/// Ela precisa entrar em DOIS sistemas na mesma rodada -- entrar so num nao distingue "a tela
	/// abre no sistema clicado" de "a tela abre sempre no mesmo" --, e pra clicar no segundo a
	/// carta tem que estar visivel de novo. Dispara o mesmo evento do botao, entao a moldura faz o
	/// mesmo caminho que faria com o dedo do jogador.
	/// </summary>
	public void VoltarDeTeste() => PediuVoltar?.Invoke();

	/// <summary>Clica onde este corpo esta desenhado. E o caminho do mouse, sem mouse.</summary>
	public bool ClicarNaOrbita(int k)
	{
		if (!_tem || k < 0 || k >= _sis.Orbitas) return false;
		int? achado = CorpoEm(ParaTela(Local(_sis.Planeta(k).Pos)));
		if (achado != k) return false;
		_alvo = k;
		SelecaoMudou?.Invoke();
		return true;
	}

	/// <summary>Clica na estrela. Devolve falso se o desenho dela nao responde onde ela esta.</summary>
	public bool ClicarNaEstrela()
	{
		if (!_tem || CorpoEm(ParaTela(Vector2.Zero)) != -1) return false;
		_alvo = -1;
		SelecaoMudou?.Invoke();
		return true;
	}

	/// <summary>
	/// O RAIO DESENHADO DA ESTRELA E O LADO DA FOLHA, em pixels de tela. SO PRA BANCADA.
	///
	/// E o par de numeros que prova que o desenho casa com o raio que mata: o nucleo da folha tem
	/// que dar exatamente `2 x raio`. Sem isto, a unica prova seria olhar uma foto.
	/// </summary>
	public (float raio, float folha, float nucleo) DesenhoDaEstrelaDeTeste() =>
		(RaioDaEstrelaNaTela(),
		 ArteDeEstrela.LadoDaFolha(_sis.Estrela, RaioDaEstrelaNaTela()),
		 ArteDeEstrela.NucleoDeTeste(_sis.Estrela));

	public float EscalaDeTeste => _escala;

	/// <summary>
	/// OS DOIS FINS DA ESCADA DE ZOOM. SO PRA BANCADA -- ver o irmao em <see cref="MapaEstelar"/>.
	///
	/// "A escala parou de mudar" fica verde tambem com o zoom morto; o que prova que o LIMITE segurou
	/// e o ponto de parada bater com o numero escrito aqui.
	/// </summary>
	public static (float Min, float Max) LimitesDeTeste => (EscalaMin, EscalaMax);

	/// <summary>
	/// A CONVERSAO LOCAL &lt;-&gt; TELA, em coordenadas GLOBais. SO PRA BANCADA.
	///
	/// Aqui a copia da formula seria pior que na carta: o meio do desenho NAO e o meio do widget (ver
	/// <see cref="MeioDoMapa"/> -- ele desconta a barra de cima e o painel da direita). Uma bancada com
	/// a conta propria mediria a ancora a partir de um centro que o desenho nao usa.
	/// </summary>
	public Vector2 TelaDeTeste(Vector2 local) => GetGlobalRect().Position + ParaTela(local);

	/// <summary>A volta do <see cref="TelaDeTeste"/>: que ponto do sistema esta debaixo deste pixel.</summary>
	public Vector2 LocalDeTeste(Vector2 telaGlobal) => ParaLocal(telaGlobal - GetGlobalRect().Position);
}
