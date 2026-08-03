using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A SOMBRA DO QUE ESTA NA FRENTE. Nao ha alcance de visao: de dia voce enxerga a tela
/// inteira, e a unica coisa que esconde alguma coisa e o que uma parede (ou uma arvore) tapa.
///
/// POR QUE ISTO NAO E UMA LUZ. A primeira versao era um <see cref="PointLight2D"/> preso no
/// personagem, e o dono cortou: "essa luz de visao nao deveria ser uma luz em si". Ele estava
/// certo por um motivo mecanico -- uma luz SOMA brilho em tudo que alcanca, inclusive no
/// sprite de quem a carrega, e o personagem ficava mais claro que o mundo em volta.
///
/// POR QUE NAO E MAIS UM CIRCULO. A segunda versao tinha alcance (12 tiles) e um leque de 128
/// raios em angulos uniformes. Dois problemas, e o dono viu os dois: o circulo nao devia
/// existir de dia, e a borda saia SERRILHADA. O serrilhado tem causa exata: 128 raios dao um
/// passo de 2,81 graus, e um tile de 32 px visto a 384 px ocupa ~4,8 graus -- uma parede
/// isolada era amostrada por um ou dois raios e a sombra dela PULAVA um tile inteiro a cada
/// passo do jogador. Aumentar o numero de raios nao conserta isso; so muda a frequencia.
///
/// O QUE CONSERTA e mirar nas QUINAS. Sombra de geometria reta e delimitada por retas que
/// passam pelos CANTOS do obstaculo -- entao os raios sao lancados exatamente ali, um de cada
/// lado de cada quina. Entre duas quinas vizinhas nao ha nada pra amostrar, e a aresta que
/// liga os dois pontos e a propria face da parede. Borda reta por construcao.
///
/// O LIMITE E A TELA, e isso e o corte de desempenho: so entram no calculo as celulas dentro
/// do retangulo da camera. Uma parede do outro lado do planeta nao projeta nada que alguem
/// possa ver, entao nao custa nada.
/// </summary>
public partial class Visao : Node2D
{
	/// <summary>Quem enxerga.</summary>
	public Node2D? Alvo;

	/// <summary>
	/// O QUE CEGA: parede, porta e arvore -- tudo que e denso (ver MapConverter). E um bitset
	/// proprio (`.vis`), separado do de colisao, porque os dois divergem: a porta cega e nao
	/// bloqueia, a borda do mundo bloqueia e nao interessa cegar.
	/// </summary>
	public ZoneCollision? Mapa;

	/// <summary>
	/// ATE QUANDO ESTOU CEGO (o `blindT` do Solar Flare), em ms do relogio do cliente.
	///
	/// A CEGUEIRA MORA AQUI e nao numa cortina por cima da tela porque cegar E o campo de
	/// visao indo a zero -- e o mesmo sistema, com o leque vazio. Uma cortina separada seria
	/// um segundo jeito de escurecer a tela, e os dois teriam que combinar pra sempre.
	/// </summary>
	public ulong CegoAte;

	public bool Cego => Time.GetTicksMsec() < CegoAte;

	/// <summary>
	/// A COR DA SOMBRA. O alfa e a unica coisa que se costuma querer mexer aqui: mais alto
	/// esconde melhor o interior das casas, mais baixo faz a sombra da arvore pesar menos no
	/// campo aberto.
	/// </summary>
	private static readonly Color Sombra = new(0.02f, 0.025f, 0.05f, 0.85f);

	/// <summary>
	/// Quanto o raio passa AO LADO da quina, em pixels.
	///
	/// Sao dois raios por quina: um que raspa por dentro (bate na parede) e um que raspa por
	/// fora (segue adiante). E o par deles que produz a descontinuidade -- a reta da sombra.
	///
	/// O desvio e em PIXELS, nao em angulo, e essa e a diferenca que importa: um epsilon
	/// angular fixo vira um desvio grande numa quina distante e some abaixo da precisao do
	/// float numa quina perto (a 20 px, 1e-4 rad da 0,002 px, e o vertice comeca a piscar
	/// entre uma face e outra conforme o jogador anda). Em pixels, a folga e sempre a mesma.
	/// </summary>
	private const float Folga = 0.3f;

	/// <summary>
	/// Raios soltos em angulos regulares, alem dos das quinas.
	///
	/// Nao servem pra desenhar sombra -- servem de PISO: garantem que nenhum setor entre dois
	/// vertices vizinhos chegue perto de 180 graus, que e a condicao pro leque de triangulos
	/// nao se dobrar sobre si mesmo. Custam nada e tiram uma classe inteira de caso-limite.
	/// </summary>
	private const int RaiosSoltos = 16;

	/// <summary>Margem alem da tela, em pixels. Cobre a folga de um quadro de camera.</summary>
	private const float Margem = 96f;

	private readonly List<Raio> _raios = [];
	private Vector2[] _pontos = [];
	private Color[] _cores = [];
	private int[] _indices = [];

	private Vector2 _ultimoOlho = new(float.NaN, float.NaN);
	private Rect2 _ultimaTela;

	/// <summary>De onde o leque atual foi lancado. Guardado porque <see cref="Ve"/> precisa.</summary>
	private Vector2 _olho;

	/// <summary>Um raio resolvido: pra onde apontou, ate onde foi, e em que angulo saiu.</summary>
	private readonly struct Raio(Vector2 dir, float dist, float angulo, int ordem)
	{
		public readonly Vector2 Dir = dir;
		public readonly float Dist = dist;
		public readonly float Angulo = angulo;

		/// <summary>Ordem de criacao. E o desempate FINAL da ordenacao -- ver Ordenar().</summary>
		public readonly int Ordem = ordem;
	}

	public override void _Ready()
	{
		// POR CIMA DE TUDO que e mundo. z_index vence a ordenacao por Y, entao a sombra cobre
		// tiles e personagens. O HUD nao entra nisso: e CanvasLayer, camada de tela.
		ZIndex = 90;

		// desenha em coordenadas de MUNDO: a malha e montada com posicoes globais
		TopLevel = true;

		// NENHUMA LUZ TOCA A SOMBRA. Sem isto, a fogueira do cenario iluminaria a propria
		// sombra -- uma PointLight2D aditiva bate em todo CanvasItem que compartilhe a
		// mascara, e sairia um clarao laranja no meio do escuro.
		LightMask = 0;
	}

	// =====================================================================
	// QUANDO REFAZER
	// =====================================================================
	public override void _Process(double delta)
	{
		if (Alvo == null || Mapa == null) { Visible = false; return; }
		Visible = true;

		// cego: nao ha leque pra recalcular, e a cada quadro a cortina tem que continuar de pe
		if (Cego) { QueueRedraw(); return; }

		Vector2 olho = Olho();
		Rect2 tela = Tela(olho);
		if (olho.IsEqualApprox(_ultimoOlho) && tela.Position.IsEqualApprox(_ultimaTela.Position)
			&& tela.Size.IsEqualApprox(_ultimaTela.Size)) return;

		_ultimoOlho = olho;
		_ultimaTela = tela;
		QueueRedraw();
	}

	/// <summary>
	/// DE ONDE SE OLHA -- e nao e o centro do sprite.
	///
	/// A caixa de colisao do personagem sao os PES (<see cref="MoveRules.FeetOffsetY"/> abaixo
	/// do centro), entao encostar de frente numa parede horizontal e um estado NORMAL em que o
	/// centro do corpo esta DENTRO da celula bloqueante. Olhando dali, todo raio lateral bate
	/// na parede vizinha no primeiro passo e a tela inteira apaga -- e isso aconteceria toda
	/// vez que alguem encostasse num muro.
	///
	/// O centro da caixa dos pes, ao contrario, e o unico ponto que a regra de movimento
	/// GARANTE estar fora de parede (uma celula tem 32 px e a caixa 16x10: nao ha como o
	/// centro cair dentro de um bloco com os quatro cantos livres).
	/// </summary>
	private Vector2 Olho() => Alvo!.GlobalPosition + new Vector2(0, MoveRules.FeetOffsetY);

	private Rect2 Tela(Vector2 olho)
	{
		Camera2D? cam = GetViewport()?.GetCamera2D();
		Vector2 tam = GetViewportRect().Size;
		if (cam != null) tam /= cam.Zoom;
		Vector2 centro = cam?.GetScreenCenterPosition() ?? olho;
		return new Rect2(centro - tam * 0.5f - new Vector2(Margem, Margem),
						 tam + new Vector2(Margem, Margem) * 2f);
	}

	// =====================================================================
	// O DESENHO
	// =====================================================================
	public override void _Draw()
	{
		if (Alvo == null || Mapa == null) return;

		// CEGO: preto puro, sem furo nenhum. Nem o proprio corpo aparece -- e o que separa
		// "estou no escuro" (da pra se orientar pelo que se lembra) de "nao enxergo".
		if (Cego)
		{
			Rect2 t = Tela(Olho());
			DrawRect(new Rect2(t.Position - t.Size, t.Size * 3f), Colors.Black);
			return;
		}

		Vector2 p = Olho();
		Rect2 tela = Tela(p);

		// Nao deveria acontecer (ver Olho()), mas se acontecer -- teleporte pra dentro de uma
		// parede, spawn em cima de uma casa -- nao pinte NADA. Uma tela preta e o pior modo de
		// falhar possivel; sem sombra o jogador ao menos continua jogando.
		if (Mapa.BlockedAt(new Vec2(p.X, p.Y))) return;

		Recalcular(p, tela);
		if (_raios.Count < 3) return;
		Montar(p, tela);
	}

	// =====================================================================
	// O LEQUE, DISPONIVEL PRA QUEM PRECISAR SABER "DA PRA VER?"
	// =====================================================================
	/// <summary>
	/// Refaz o leque de um ponto de vista. Publico porque o diagnostico (`--diagvisao`) precisa
	/// rodar isto SEM cena, sem camera e sem janela.
	/// </summary>
	public void Recalcular(Vector2 olho, Rect2 tela)
	{
		_olho = olho;
		Mirar(olho, tela);
		Ordenar();
	}

	/// <summary>Quantos raios o ultimo leque usou. Diagnostico.</summary>
	public int QuantosRaios => _raios.Count;

	/// <summary>
	/// ESTE PONTO E VISIVEL do ultimo ponto de vista calculado?
	///
	/// O leque ja e a resposta: acha-se o par de raios que abraca o angulo do ponto e
	/// pergunta-se de que lado da aresta entre eles o ponto caiu. Do mesmo lado que o
	/// observador, ve; do outro, esta na sombra.
	///
	/// E O(log n) e nao lanca raio nenhum -- da pra perguntar por dezenas de alvos no mesmo
	/// quadro sem custo.
	/// </summary>
	public bool Ve(Vector2 ponto)
	{
		int n = _raios.Count;
		if (n < 3) return true;

		Vector2 v = ponto - _olho;
		if (v.LengthSquared() < 1f) return true;
		float a = Angulo(v);

		// primeiro raio com angulo > a; o par e (esse-1, esse), com volta no fim
		int lo = 0, hi = n;
		while (lo < hi)
		{
			int meio = (lo + hi) >> 1;
			if (_raios[meio].Angulo <= a) lo = meio + 1; else hi = meio;
		}
		int j = lo % n;
		int i = (j - 1 + n) % n;

		Vector2 A = _olho + _raios[i].Dir * _raios[i].Dist;
		Vector2 B = _olho + _raios[j].Dir * _raios[j].Dist;
		Vector2 e = B - A;
		if (e.LengthSquared() < 1e-6f) return v.Length() <= _raios[i].Dist;

		float ladoPonto = e.X * (ponto.Y - A.Y) - e.Y * (ponto.X - A.X);
		float ladoOlho = e.X * (_olho.Y - A.Y) - e.Y * (_olho.X - A.X);
		return ladoPonto * ladoOlho >= 0f;
	}

	/// <summary>
	/// Monta a lista de raios: dois por quina de parede, quatro nos cantos da tela e alguns
	/// soltos de reserva. Cada um ja sai resolvido (marchado ate bater ou sair da tela).
	/// </summary>
	private void Mirar(Vector2 p, Rect2 tela)
	{
		_raios.Clear();
		const int T = ZoneCollision.TileSize;

		int cx0 = (int)MathF.Floor(tela.Position.X / T);
		int cy0 = (int)MathF.Floor(tela.Position.Y / T);
		int cx1 = (int)MathF.Floor(tela.End.X / T);
		int cy1 = (int)MathF.Floor(tela.End.Y / T);

		// QUINAS. Um ponto da grade e quina quando as quatro celulas em volta dele NAO sao
		// todas iguais -- ou seja, ali a parede comeca, acaba ou dobra. Onde as quatro sao
		// iguais nao ha nada que projete borda, e lancar raio seria trabalho jogado fora.
		for (int gy = cy0; gy <= cy1 + 1; gy++)
			for (int gx = cx0; gx <= cx1 + 1; gx++)
			{
				bool a = Cega(gx - 1, gy - 1), b = Cega(gx, gy - 1);
				bool c = Cega(gx - 1, gy), d = Cega(gx, gy);
				if (a == b && b == c && c == d) continue;
				Quina(p, tela, new Vector2(gx * T, gy * T));
			}

		// OS CANTOS DA TELA fecham o leque. Sem eles, dois raios vizinhos podem terminar em
		// bordas DIFERENTES do retangulo e a aresta entre os dois cortaria o canto da tela --
		// apareceria um triangulo escuro na quina do monitor, do nada.
		foreach (Vector2 canto in new[] { tela.Position, new Vector2(tela.End.X, tela.Position.Y), tela.End, new Vector2(tela.Position.X, tela.End.Y) })
			Lancar(p, tela, canto - p);

		for (int i = 0; i < RaiosSoltos; i++)
		{
			float ang = Mathf.Tau * i / RaiosSoltos;
			Lancar(p, tela, new Vector2(MathF.Cos(ang), MathF.Sin(ang)));
		}
	}

	/// <summary>Os dois raios de uma quina: um raspando de cada lado.</summary>
	private void Quina(Vector2 p, Rect2 tela, Vector2 quina)
	{
		Vector2 v = quina - p;
		float d = v.Length();
		if (d < 1f) return;                       // quina embaixo do pe: nao ha lado de ca nem de la
		Vector2 perp = new Vector2(-v.Y, v.X) / d;
		Lancar(p, tela, v + perp * Folga);
		Lancar(p, tela, v - perp * Folga);
	}

	private void Lancar(Vector2 p, Rect2 tela, Vector2 rumo)
	{
		float n = rumo.Length();
		if (n < 1e-6f) return;
		Vector2 d = rumo / n;

		float limite = ateABorda(p, d, tela);
		if (limite <= 0.5f) return;

		float dist = Marchar(p, d, limite);
		_raios.Add(new Raio(d, dist, Angulo(d), _raios.Count));
	}

	/// <summary>Onde o raio sai do retangulo da tela. E o "infinito" util deste sistema.</summary>
	private static float ateABorda(Vector2 p, Vector2 d, Rect2 r)
	{
		float t = float.MaxValue;
		if (MathF.Abs(d.X) > 1e-6f)
			t = MathF.Min(t, ((d.X > 0 ? r.End.X : r.Position.X) - p.X) / d.X);
		if (MathF.Abs(d.Y) > 1e-6f)
			t = MathF.Min(t, ((d.Y > 0 ? r.End.Y : r.Position.Y) - p.Y) / d.Y);
		return t == float.MaxValue ? 0f : t;
	}

	/// <summary>
	/// ATE ONDE O RAIO VAI. Caminha CELULA A CELULA pela grade (Amanatides-Woo) em vez de
	/// amostrar de N em N pixels.
	///
	/// A diferenca nao e so de desempenho: o passo fixo ERRA por construcao (ele para no
	/// primeiro ponto amostrado que ja esta dentro da parede, nunca na face dela), e e esse
	/// erro que fazia a borda tremer. Aqui a parada e exatamente a face da celula, entao dois
	/// raios que batem na MESMA parede devolvem pontos exatamente colineares -- a borda sai
	/// reta sozinha, sem precisar juntar arestas nem fundir nada.
	/// </summary>
	public float Marchar(Vector2 p, Vector2 d, float limite)
	{
		const int T = ZoneCollision.TileSize;
		int cx = (int)MathF.Floor(p.X / T);
		int cy = (int)MathF.Floor(p.Y / T);

		int sx = d.X > 0 ? 1 : d.X < 0 ? -1 : 0;
		int sy = d.Y > 0 ? 1 : d.Y < 0 ? -1 : 0;

		float tdx = sx != 0 ? MathF.Abs(T / d.X) : float.MaxValue;
		float tdy = sy != 0 ? MathF.Abs(T / d.Y) : float.MaxValue;

		float tmx = sx > 0 ? ((cx + 1) * T - p.X) / d.X
				  : sx < 0 ? (cx * T - p.X) / d.X
				  : float.MaxValue;
		float tmy = sy > 0 ? ((cy + 1) * T - p.Y) / d.Y
				  : sy < 0 ? (cy * T - p.Y) / d.Y
				  : float.MaxValue;

		// teto de passos: a diagonal da tela tem ~40 celulas, e o `limite` ja para o laco --
		// isto e so cinto de seguranca contra um raio degenerado
		for (int passo = 0; passo < 256; passo++)
		{
			bool porX = tmx < tmy;
			float t = porX ? tmx : tmy;
			if (t >= limite) return limite;

			// CANTO EXATO: o raio cruza um vertice da grade, os dois eixos avancam juntos.
			// Duas paredes que so se tocam pela diagonal nao tem fresta de verdade -- se as
			// duas laterais bloqueiam, o raio para ali. Sem esta regra nasce uma agulha de luz
			// de 1 px que pisca a cada passo do jogador.
			if (MathF.Abs(tmx - tmy) < 0.01f)
			{
				if (Cega(cx + sx, cy) && Cega(cx, cy + sy)) return t;
				cx += sx; cy += sy;
				tmx += tdx; tmy += tdy;
			}
			else if (porX) { cx += sx; tmx += tdx; }
			else { cy += sy; tmy += tdy; }

			if (Cega(cx, cy)) return t;
		}
		return limite;
	}

	/// <summary>Sempre por aqui: fora do mapa CEGA (e a borda do mundo), e isso sai de graca.</summary>
	private bool Cega(int cx, int cy) => Mapa!.BlockedCell(cx, cy);

	private static float Angulo(Vector2 d)
	{
		float a = MathF.Atan2(d.Y, d.X);
		if (a < 0f) a += Mathf.Tau;
		// a soma acima pode devolver Tau exato por arredondamento, e ai o raio sairia do
		// intervalo [0, Tau) e iria parar no fim do array em vez do comeco -- invertendo o par
		if (a >= Mathf.Tau) a -= Mathf.Tau;
		return a;
	}

	/// <summary>
	/// Ordena por angulo. O comparador NUNCA devolve 0, e isso e proposital.
	///
	/// Numa grade alinhada aos eixos, duas quinas na mesma horizontal do jogador dao angulos
	/// exatamente iguais o tempo todo. O Array.Sort do .NET e INSTAVEL: com empate, a ordem
	/// entre elas mudaria de quadro pra quadro sem o jogador andar um pixel, e o par de
	/// triangulos correspondente inverteria -- cintilacao. Desempatar por distancia e, por
	/// fim, por ordem de criacao torna a saida deterministica.
	/// </summary>
	private void Ordenar() => _raios.Sort((x, y) =>
	{
		int c = x.Angulo.CompareTo(y.Angulo);
		if (c != 0) return c;
		c = x.Dist.CompareTo(y.Dist);
		return c != 0 ? c : x.Ordem.CompareTo(y.Ordem);
	});

	/// <summary>
	/// Monta e entrega a malha da sombra.
	///
	/// O que se desenha e o COMPLEMENTO do que se ve: pra cada par de raios vizinhos, o
	/// quadrilatero que vai dos dois pontos de parada pra fora, ate passar da tela. Onde os
	/// dois raios morreram na borda do retangulo, esse quadrilatero cai todo fora da tela e
	/// nao pinta nada -- que e o certo, ali nao ha sombra.
	///
	/// Uma chamada so de RenderingServer com os arrays prontos, e nao um vertice por vez: o
	/// caminho por vertice do ImmediateMesh custa duas passagens C#->motor CADA, e no pior
	/// caso (uma cidade cheia de quinas) isso sozinho passaria de mil chamadas por quadro.
	/// Os buffers sao reaproveitados entre quadros pela mesma razao.
	/// </summary>
	private void Montar(Vector2 p, Rect2 tela)
	{
		int n = _raios.Count;

		// alem do canto mais distante da tela: o que passar disso o monitor corta
		float longe = 0f;
		foreach (Vector2 c in new[] { tela.Position, new Vector2(tela.End.X, tela.Position.Y), tela.End, new Vector2(tela.Position.X, tela.End.Y) })
			longe = MathF.Max(longe, p.DistanceTo(c));
		longe += 64f;

		if (_pontos.Length != n * 2)
		{
			_pontos = new Vector2[n * 2];
			_cores = new Color[n * 2];
			for (int i = 0; i < _cores.Length; i++) _cores[i] = Sombra;
			_indices = new int[n * 6];
		}

		for (int i = 0; i < n; i++)
		{
			Raio r = _raios[i];
			_pontos[i * 2] = p + r.Dir * r.Dist;      // onde a vista termina
			_pontos[i * 2 + 1] = p + r.Dir * longe;   // e o mesmo rumo, fora da tela
		}

		for (int i = 0; i < n; i++)
		{
			int j = (i + 1) % n;
			int k = i * 6;
			_indices[k] = i * 2; _indices[k + 1] = j * 2; _indices[k + 2] = j * 2 + 1;
			_indices[k + 3] = i * 2; _indices[k + 4] = j * 2 + 1; _indices[k + 5] = i * 2 + 1;
		}

		RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), _indices, _pontos, _cores);
	}
}
