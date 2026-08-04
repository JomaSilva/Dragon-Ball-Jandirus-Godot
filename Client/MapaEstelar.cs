using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O MAPA DA GALAXIA -- desenhado, com zoom, arrasto e clique.
///
/// ============================ POR QUE UM MAPA, E NAO UMA LISTA ============================
/// A aba Nav mostrava uma lista dos corpos celestes das tres chunks em volta: no maximo um punhado
/// de nomes, sem nocao nenhuma de ONDE eles estao. Isso responde "o que ha por perto" e nao
/// responde a pergunta que um mapa estelar existe pra responder -- "pra onde eu vou".
///
/// O pedido do dono foi direto: "faca um mapa do espaco com todos os planetas que o servidor sabe
/// onde estao, e esse minimapa e interativo... voce clica nos planetas e seleciona viajar... e voce
/// pode dar zoom e zoom out". E o `Nav System` do original, que o `Chronology.dm` descreve como
/// "o mapa da galaxia".
/// ==========================================================================================
///
/// ============================ DE ONDE VEM CADA PLANETA ============================
/// Nao ha pacote de rede nenhum aqui, e nao precisa haver: o universo e uma FUNCAO PURA da seed do
/// mundo (<see cref="Espaco.PlanetaDaChunk"/>), entao o cliente chega exatamente a mesma resposta
/// que o servidor sem trocar um byte. E o mesmo principio que ja desenha o ceu.
///
///   * OS SETE PRE-FEITOS (`Espaco.PreFeitos`) aparecem SEMPRE, em qualquer zoom. Sao os mundos
///     com mapa proprio -- os que "o servidor sabe onde estao" no sentido forte.
///   * OS PROCEDURAIS aparecem quando o zoom chega perto o bastante pra varredura valer a pena.
///     Nao e economia de preguica: a galaxia inteira tem CENTENAS DE MILHARES de planetas (um a
///     cada 40 chunks, num quadrado de ~2000 chunks so na regiao dos pre-feitos), e desenhar tudo
///     de uma vez seria um borrao cinza que nao ajuda ninguem a escolher destino.
/// ==================================================================================
///
/// VIAJAR E O PILOTO AUTOMATICO QUE JA EXISTE (`LocalPlayer.Destino`): ele preenche a direcao que
/// as teclas preencheriam, no passo normal, e o servidor valida cada passo como valida qualquer
/// outro. Nao ha teleporte -- Terra a Namek continua custando sete dias in-game.
/// </summary>
public partial class MapaEstelar : Control
{
	// =====================================================================
	// ESTADO DA CAMERA
	// =====================================================================
	/// <summary>O ponto do MUNDO que esta no centro do widget.</summary>
	private Vector2 _centro;

	/// <summary>Pixels de TELA por pixel de MUNDO. Cresce ao aproximar.</summary>
	private float _escala = 1f / 4000f;

	/// <summary>
	/// Os limites do zoom.
	///
	/// O de longe cabe os sete pre-feitos com folga (eles se espalham por ~3,7 milhoes de px); o de
	/// perto poe uma chunk inteira (2048 px) em ~300 px de tela, que e onde da pra ver a diferenca
	/// entre dois planetas vizinhos.
	/// </summary>
	private const float EscalaMin = 1f / 12000f;
	private const float EscalaMax = 0.15f;

	/// <summary>Quanto cada passo de roda multiplica o zoom.</summary>
	private const float PassoDeZoom = 1.25f;

	private bool _arrastando;
	private Vector2 _ultimoMouse;

	/// <summary>O planeta clicado. E ele que o botao "Viajar" usa.</summary>
	public PlanetaNoEspaco? Selecionado { get; private set; }

	/// <summary>Avisa a aba que a selecao mudou -- e ela quem desenha o painel do destino.</summary>
	public event Action? SelecaoMudou;

	/// <summary>Duplo clique num planeta: viajar direto, sem passar pelo botao.</summary>
	public event Action<PlanetaNoEspaco>? PediuViagem;

	// =====================================================================
	// O CACHE DA VARREDURA
	// =====================================================================
	/// <summary>
	/// Um BLOCO de 32x32 chunks. A varredura acontece por bloco, e nao por chunk, por uma razao
	/// simples: mover o mapa um pixel muda o conjunto de chunks visiveis, mas quase nunca muda o
	/// conjunto de BLOCOS -- entao o cache acerta em vez de revarrer a cada quadro.
	/// </summary>
	private const int ChunksPorBloco = 32;

	/// <summary>
	/// QUANTOS BLOCOS DA PRA VARRER antes de a coisa deixar de fazer sentido.
	///
	/// Cada bloco custa 1024 chamadas de `PlanetaDaChunk` (um hash e uma volta nos sete pre-feitos).
	/// Sessenta blocos sao ~61 mil chamadas -- alguns milissegundos, e uma vez so, porque o
	/// resultado fica no cache. Acima disso a varredura ficaria cara E inutil ao mesmo tempo: o
	/// resultado seriam milhares de pontos a menos de um pixel um do outro.
	/// </summary>
	private const int BlocosVisiveisMax = 60;

	private static readonly Dictionary<(int X, int Y), List<PlanetaNoEspaco>> _cache = [];
	private static ulong _seedDoCache;

	/// <summary>
	/// TETO DO CACHE. Passar o mapa de ponta a ponta num zoom fechado visita milhares de blocos, e
	/// nenhum deles e jogado fora sozinho -- um cache sem teto e um vazamento com outro nome. Ao
	/// estourar, esvazia inteiro: revarrer o enquadramento de agora custa alguns quadros e nada
	/// mais, e um LRU aqui seria mais codigo do que o problema merece.
	/// </summary>
	private const int BlocosNoCacheMax = 4000;

	/// <summary>Quantos blocos varrer POR QUADRO. O resto espera o proximo -- o mapa nao trava.</summary>
	private const int BlocosPorQuadro = 6;

	// =====================================================================
	// CICLO
	// =====================================================================
	public override void _Ready()
	{
		// PARA O EVENTO AQUI. Este widget mora dentro do ScrollContainer do menu: sem `Stop`, a roda
		// do mouse rolaria a pagina em vez de dar zoom, e o arrasto arrastaria a rolagem.
		MouseFilter = MouseFilterEnum.Stop;
		// ALTO O BASTANTE PRA SER CARTA, baixo o bastante pra o painel do destino caber embaixo
		// sem rolagem. Medido na tela: com 380 o botao "Viajar" ficava fora da area visivel.
		CustomMinimumSize = new Vector2(0, 330);
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
		FocusMode = FocusModeEnum.Click;

		// O ENQUADRAMENTO DEPENDE DO TAMANHO, e no `_Ready` o tamanho ainda e zero -- o container
		// so o resolve no proximo quadro. Enquadrar agora usaria a medida de reserva e o mapa
		// nasceria com o zoom errado; por isso a primeira vez acontece no `Resized`.
		Resized += () => { if (!_enquadrou) { _enquadrou = true; VerTudo(); } };
		VerTudo();
	}

	private bool _enquadrou;

	/// <summary>Redesenha todo quadro: o corpo do jogador se move, e o mapa mostra onde ele esta.</summary>
	public override void _Process(double delta) => QueueRedraw();

	/// <summary>
	/// ONDE EU ESTOU NA GALAXIA -- que NAO e onde o meu corpo esta.
	///
	/// A coordenada do corpo so vale como coordenada de galaxia NO ESPACO. Pousado, ela vira
	/// coordenada de superficie (o spawn da Terra e (7984, 8016), que na galaxia fica a 11 mil px
	/// da origem -- fora da propria Terra). Entao:
	///
	///   * no espaco  -> a posicao do corpo, que e a da nave;
	///   * num dos sete -> a posicao do PLANETA, que e exata e nao depende de memoria nenhuma;
	///   * num gerado -> de onde eu desci (`World.UltimaNoEspaco`), que e a unica pista que sobra:
	///     a posicao de um mundo sorteado sai da CHUNK dele, e o nome nao diz qual chunk e.
	/// </summary>
	public static Vector2? MinhaPosicaoNaGalaxia()
	{
		if (GameClient.Instance is not { } cli) return null;
		if (Espaco.EhEspaco(cli.Zone)) return World.Instancia?.PosicaoLocal;

		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, cli.Zone.Name, StringComparison.OrdinalIgnoreCase))
				return new Vector2(p.Pos.X, p.Pos.Y);

		return World.Instancia?.UltimaNoEspaco;
	}

	// =====================================================================
	// CONVERSAO
	// =====================================================================
	private Vector2 ParaTela(Vector2 mundo) => (mundo - _centro) * _escala + Size / 2f;
	private Vector2 ParaMundo(Vector2 tela) => (tela - Size / 2f) / _escala + _centro;

	// =====================================================================
	// ENTRADA
	// =====================================================================
	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton b)
		{
			switch (b.ButtonIndex)
			{
				case MouseButton.WheelUp when b.Pressed: Aproximar(PassoDeZoom, b.Position); return;
				case MouseButton.WheelDown when b.Pressed: Aproximar(1f / PassoDeZoom, b.Position); return;

				case MouseButton.Left when b.Pressed:
					GrabFocus();
					PlanetaNoEspaco? clicado = PlanetaEm(b.Position);
					if (clicado != null)
					{
						// DUPLO CLIQUE VIAJA. E o mesmo gesto que ja marca alvo no mundo -- quem
						// aprendeu um aprendeu o outro.
						if (b.DoubleClick && Selecionado is { } s && s.Nome == clicado.Value.Nome)
						{
							PediuViagem?.Invoke(clicado.Value);
							return;
						}
						Selecionado = clicado;
						SelecaoMudou?.Invoke();
						return;
					}
					// clicou no vazio: comeca a arrastar
					_arrastando = true;
					_ultimoMouse = b.Position;
					return;

				case MouseButton.Left: _arrastando = false; return;
			}
		}

		if (e is InputEventMouseMotion m && _arrastando)
		{
			// ARRASTA O MAPA, e nao a camera: o mundo acompanha o dedo. Por isso a subtracao.
			_centro -= (m.Position - _ultimoMouse) / _escala;
			_ultimoMouse = m.Position;
			QueueRedraw();
		}
	}

	/// <summary>
	/// Zoom ANCORADO NO CURSOR: o ponto do mundo sob o mouse continua sob o mouse.
	///
	/// Sem a ancora, aproximar sempre puxa pro centro e o jogador perde de vista o que estava
	/// olhando -- ele aproxima pra ver um planeta e o planeta foge pra fora da tela.
	/// </summary>
	private void Aproximar(float fator, Vector2 ancoraNaTela)
	{
		Vector2 antes = ParaMundo(ancoraNaTela);
		_escala = Mathf.Clamp(_escala * fator, EscalaMin, EscalaMax);
		Vector2 depois = ParaMundo(ancoraNaTela);
		_centro += antes - depois;
		QueueRedraw();
	}

	public void Zoom(float fator) => Aproximar(fator, Size / 2f);

	/// <summary>Enquadra os sete mundos com mapa proprio. E o "de onde tudo se ve".</summary>
	public void VerTudo()
	{
		Vector2 min = Vector2.Inf, max = -Vector2.Inf;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
		{
			var v = new Vector2(p.Pos.X, p.Pos.Y);
			min = min.Min(v);
			max = max.Max(v);
		}
		_centro = (min + max) / 2f;

		Vector2 tam = Size.X > 1 ? Size : new Vector2(640, 380);
		// A MARGEM E MAIOR NA VERTICAL porque o NOME e desenhado ABAIXO do disco: com margem igual
		// nos dois eixos, o planeta do extremo de baixo (Icer) cabia e o rotulo dele saia da moldura.
		Vector2 span = (max - min) * new Vector2(1.25f, 1.5f);
		_escala = Mathf.Clamp(Mathf.Min(tam.X / Mathf.Max(span.X, 1), tam.Y / Mathf.Max(span.Y, 1)),
							  EscalaMin, EscalaMax);
		QueueRedraw();
	}

	/// <summary>Centraliza em mim, num zoom em que da pra escolher vizinho.</summary>
	public void VerMim()
	{
		if (MinhaPosicaoNaGalaxia() is not { } eu) return;
		_centro = eu;
		_escala = Mathf.Clamp(0.02f, EscalaMin, EscalaMax);
		QueueRedraw();
	}

	// =====================================================================
	// QUAIS PLANETAS ESTAO NA TELA
	// =====================================================================
	/// <summary>
	/// A VARREDURA PROCEDURAL ESTA LIGADA NESTE ZOOM?
	///
	/// E a pergunta que a interface faz pra explicar por que so ha sete pontos na tela. Sem ela, um
	/// zoom aberto pareceria um universo quase vazio -- e o jogador nao teria como saber que basta
	/// aproximar.
	/// </summary>
	public bool VendoProcedurais => BlocosVisiveis(out _, out _, out _, out _) <= BlocosVisiveisMax;

	/// <summary>
	/// Quantos blocos o enquadramento cobre, e quais.
	///
	/// Devolve a CONTAGEM antes de montar lista nenhuma: num zoom bem aberto sao milhoes de pares,
	/// e alocar milhoes de tuplas pra depois descobrir que sao demais e o jeito caro de descobrir.
	/// </summary>
	private long BlocosVisiveis(out int bx0, out int by0, out int bx1, out int by1)
	{
		const float LadoDoBloco = ChunksPorBloco * (float)Espaco.ChunkPx;
		Vector2 canto0 = ParaMundo(Vector2.Zero);
		Vector2 canto1 = ParaMundo(Size);

		bx0 = Mathf.FloorToInt(canto0.X / LadoDoBloco); bx1 = Mathf.FloorToInt(canto1.X / LadoDoBloco);
		by0 = Mathf.FloorToInt(canto0.Y / LadoDoBloco); by1 = Mathf.FloorToInt(canto1.Y / LadoDoBloco);
		return (long)(bx1 - bx0 + 1) * (by1 - by0 + 1);
	}

	/// <summary>
	/// Os planetas de um bloco, varridos uma vez e guardados.
	///
	/// O cache e ESTATICO e sobrevive a fechar o menu: a resposta nao muda enquanto a seed for a
	/// mesma, e revarrer ao reabrir a aba seria pagar de novo por nada. Troca de servidor (seed
	/// diferente) joga tudo fora.
	/// </summary>
	private static List<PlanetaNoEspaco> DoBloco(ulong seed, int bx, int by)
	{
		if (seed != _seedDoCache) { _cache.Clear(); _seedDoCache = seed; }
		if (_cache.TryGetValue((bx, by), out List<PlanetaNoEspaco>? pronto)) return pronto;
		if (_cache.Count > BlocosNoCacheMax) _cache.Clear();

		var achados = new List<PlanetaNoEspaco>();
		for (int cy = 0; cy < ChunksPorBloco; cy++)
			for (int cx = 0; cx < ChunksPorBloco; cx++)
				if (Espaco.PlanetaDaChunk(seed, new ChunkId(bx * ChunksPorBloco + cx, by * ChunksPorBloco + cy))
					is { } p) achados.Add(p);

		_cache[(bx, by)] = achados;
		return achados;
	}

	/// <summary>
	/// O ULTIMO RESULTADO de <see cref="NaTela"/>, e o enquadramento que o produziu.
	///
	/// `_Process` pede um redesenho TODO QUADRO (o corpo se move), e a versao anterior montava uma
	/// lista nova a cada um -- centenas de planetas copiados 60 vezes por segundo, dezenas de KB de
	/// lixo por segundo enquanto a aba estivesse aberta, pra um resultado que so muda quando a
	/// camera muda. Agora ela so e refeita quando o enquadramento anda, ou quando a varredura
	/// trouxe bloco novo.
	/// </summary>
	private List<PlanetaNoEspaco> _naTela = [];
	private Vector2 _enquadradoEm;
	private float _enquadradoNa;
	private int _blocosQuandoEnquadrei = -1;

	private List<PlanetaNoEspaco> NaTela()
	{
		if (_blocosQuandoEnquadrei == _cache.Count
			&& _enquadradoNa == _escala && _enquadradoEm == _centro) return _naTela;

		_naTela = Varrer();
		_enquadradoEm = _centro;
		_enquadradoNa = _escala;
		_blocosQuandoEnquadrei = _cache.Count;
		return _naTela;
	}

	private List<PlanetaNoEspaco> Varrer()
	{
		// OS PRE-FEITOS SEMPRE. Eles sao sete e sao o esqueleto do mapa: sem eles, um zoom aberto
		// mostraria um retangulo vazio e o jogador nao teria como se situar.
		var l = new List<PlanetaNoEspaco>(Espaco.PreFeitos());

		ulong seed = GameClient.Instance?.SeedDoUniverso ?? 0;
		if (seed == 0) return l;

		if (BlocosVisiveis(out int bx0, out int by0, out int bx1, out int by1) > BlocosVisiveisMax)
			return l;   // longe demais: so o esqueleto

		int varridosAgora = 0;
		for (int by = by0; by <= by1; by++)
			for (int bx = bx0; bx <= bx1; bx++)
			{
				// ORCAMENTO POR QUADRO: um bloco novo custa 1024 hashes. Varrer sessenta de uma vez
				// no quadro em que o jogador soltou a roda daria um engasgo visivel; seis por quadro
				// enchem a tela em dez quadros e ninguem percebe.
				if (!_cache.ContainsKey((bx, by)) && ++varridosAgora > BlocosPorQuadro) continue;
				l.AddRange(DoBloco(seed, bx, by));
			}
		return l;
	}

	/// <summary>Raio do disco na TELA. Preso num minimo pra um mundo distante nao sumir.</summary>
	private float RaioNaTela(PlanetaNoEspaco p) => Mathf.Clamp(p.Raio * _escala, 2.5f, 46f);

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagnav`)
	// =====================================================================
	/// <summary>
	/// O que este enquadramento mostraria. PUBLICO SO PRA BANCADA.
	///
	/// Sem isto, "o mapa mostra os planetas" e uma afirmacao: sem janela nao ha o que olhar, e o
	/// `_Draw` nao devolve nada. E o mesmo motivo do `Portas` publico do World.
	/// </summary>
	public List<PlanetaNoEspaco> PlanetasDeTeste() => NaTela();

	public float EscalaDeTeste => _escala;
	public Vector2 CentroDeTeste => _centro;

	/// <summary>Move a camera como o arrasto faria. So pra bancada.</summary>
	public void IrPara(Vector2 mundo) { _centro = mundo; QueueRedraw(); }

	/// <summary>Clica onde este planeta esta. E o caminho do mouse, sem mouse.</summary>
	public bool ClicarEm(PlanetaNoEspaco p)
	{
		PlanetaNoEspaco? achado = PlanetaEm(ParaTela(new Vector2(p.Pos.X, p.Pos.Y)));
		if (achado == null) return false;
		Selecionado = achado;
		SelecaoMudou?.Invoke();
		return true;
	}

	private PlanetaNoEspaco? PlanetaEm(Vector2 tela)
	{
		PlanetaNoEspaco? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (PlanetaNoEspaco p in NaTela())
		{
			Vector2 c = ParaTela(new Vector2(p.Pos.X, p.Pos.Y));
			float d = c.DistanceTo(tela);
			// ALVO GENEROSO: o disco pode ter tres pixels, e ninguem acerta tres pixels com o mouse.
			// Dez de folga e o que faz o mapa parecer que responde.
			if (d > Mathf.Max(RaioNaTela(p) + 10f, 12f) || d >= melhorDist) continue;
			melhor = p;
			melhorDist = d;
		}
		return melhor;
	}

	// =====================================================================
	// DESENHO
	// =====================================================================
	public override void _Draw()
	{
		DrawRect(new Rect2(Vector2.Zero, Size), new Color("070912"));
		DesenharGrade();

		Vector2? eu = MinhaPosicaoNaGalaxia();
		foreach (PlanetaNoEspaco p in NaTela()) DesenharPlaneta(p, eu);

		DesenharRota(eu);
		DesenharEu(eu);
		DrawRect(new Rect2(Vector2.Zero, Size), Tema.Borda, false, 1);
	}

	/// <summary>
	/// A GRADE DE CHUNKS -- so quando cada uma cabe em pelo menos 24 px de tela.
	///
	/// Ela e a escala do mapa: sem nada de referencia, um espaco preto com pontos nao diz se dois
	/// planetas estao a uma hora ou a uma semana um do outro.
	/// </summary>
	private void DesenharGrade()
	{
		float passo = Espaco.ChunkPx * _escala;
		if (passo < 24f) return;

		var cor = new Color(Tema.Borda, 0.25f);
		Vector2 canto = ParaMundo(Vector2.Zero);
		float x0 = Mathf.Floor(canto.X / Espaco.ChunkPx) * Espaco.ChunkPx;
		float y0 = Mathf.Floor(canto.Y / Espaco.ChunkPx) * Espaco.ChunkPx;

		for (float x = x0; ParaTela(new Vector2(x, 0)).X <= Size.X; x += Espaco.ChunkPx)
		{
			float sx = ParaTela(new Vector2(x, 0)).X;
			DrawLine(new Vector2(sx, 0), new Vector2(sx, Size.Y), cor);
		}
		for (float y = y0; ParaTela(new Vector2(0, y)).Y <= Size.Y; y += Espaco.ChunkPx)
		{
			float sy = ParaTela(new Vector2(0, y)).Y;
			DrawLine(new Vector2(0, sy), new Vector2(Size.X, sy), cor);
		}
	}

	private void DesenharPlaneta(PlanetaNoEspaco p, Vector2? eu)
	{
		Vector2 c = ParaTela(new Vector2(p.Pos.X, p.Pos.Y));
		float r = RaioNaTela(p);
		if (c.X < -r - 60 || c.Y < -r - 60 || c.X > Size.X + r + 60 || c.Y > Size.Y + r + 60) return;

		// PRE-FEITO x GERADO se distinguem pela COR, e a diferenca importa: so o pre-feito tem
		// superficie pra pousar hoje. Um mapa que os desenha igual promete pouso onde nao ha.
		bool ehSelecionado = Selecionado is { } s && s.Nome == p.Nome;
		Color cor = p.Premade ? Tema.Destaque : new Color("6d7ba8");

		DrawCircle(c, r, new Color(cor, 0.22f));            // a atmosfera
		DrawCircle(c, r * 0.72f, cor);                       // o corpo
		if (p.Premade) DrawArc(c, r + 2, 0, Mathf.Tau, 24, new Color(cor, 0.55f), 1.5f);

		if (ehSelecionado)
		{
			DrawArc(c, r + 7, 0, Mathf.Tau, 32, Tema.Bom, 2f);
			DrawArc(c, r + 11, 0, Mathf.Tau, 32, new Color(Tema.Bom, 0.35f), 1f);
		}

		// O NOME so quando ha espaco pra ele: num zoom aberto, trezentos rotulos empilhados viram
		// uma mancha branca que esconde justamente os planetas.
		if (!ehSelecionado && r < 5f && !p.Premade) return;

		Font fonte = ThemeDB.FallbackFont;
		int tam = p.Premade ? 13 : 11;
		Vector2 medida = fonte.GetStringSize(p.Nome, HorizontalAlignment.Left, -1, tam);
		DrawString(fonte, c + new Vector2(-medida.X / 2f, r + tam + 2), p.Nome,
				   HorizontalAlignment.Left, -1, tam, p.Premade ? Tema.Texto : Tema.TextoFraco);

		// "VOCE ESTA AQUI": estou dentro da area de pouso deste planeta.
		if (eu is { } meu && meu.DistanceTo(new Vector2(p.Pos.X, p.Pos.Y)) <= p.Raio)
			DrawArc(c, r + 4, 0, Mathf.Tau, 28, Tema.Ki, 1.5f);
	}

	/// <summary>A linha do piloto automatico, e a do destino que esta so selecionado.</summary>
	private void DesenharRota(Vector2? eu)
	{
		if (eu is not { } meu) return;
		Vector2 de = ParaTela(meu);

		if (World.Instancia?.DestinoDoPiloto is { } indo)
		{
			Vector2 ate = ParaTela(new Vector2(indo.X, indo.Y));
			DrawLine(de, ate, new Color(Tema.Bom, 0.75f), 2f);
			DrawArc(ate, 6, 0, Mathf.Tau, 16, Tema.Bom, 2f);
		}
		else if (Selecionado is { } s)
		{
			// TRACEJADA enquanto e so uma intencao. Cheia quando o piloto esta ligado -- a diferenca
			// entre "pensei nisso" e "estou indo" tem que ser visivel sem ler texto.
			Tracejada(de, ParaTela(new Vector2(s.Pos.X, s.Pos.Y)), new Color(Tema.Texto, 0.35f));
		}
	}

	private void Tracejada(Vector2 a, Vector2 b, Color cor)
	{
		float total = a.DistanceTo(b);
		if (total < 1f) return;
		Vector2 dir = (b - a) / total;
		for (float t = 0; t < total; t += 12f)
			DrawLine(a + dir * t, a + dir * Mathf.Min(t + 6f, total), cor, 1.5f);
	}

	private void DesenharEu(Vector2? eu)
	{
		if (eu is not { } meu) return;
		Vector2 c = ParaTela(meu);
		DrawArc(c, 7, 0, Mathf.Tau, 20, Tema.Ki, 2f);
		DrawLine(c + new Vector2(-11, 0), c + new Vector2(-4, 0), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(4, 0), c + new Vector2(11, 0), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(0, -11), c + new Vector2(0, -4), Tema.Ki, 1.5f);
		DrawLine(c + new Vector2(0, 4), c + new Vector2(0, 11), Tema.Ki, 1.5f);
	}
}
