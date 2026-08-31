using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// AS PEDRAS QUE SOBEM DE UM PLANETA MORRENDO -- *"pedras levitando pelo mapa todo de forma
/// 'aleatoria' por 5 minutos"*, pedido literal do dono.
///
/// ============================ "PELO MAPA TODO" AO PE DA LETRA E PROIBITIVO, E A CONTA E CURTA ============================
/// A Terra tem **266 mil celulas** (numero medido, ver `Client/PintorDePedacos`). A 15% -- que e o
/// `prob(15)` do proprio DM pra chao solto (`SSJCinematic.dm:31`) -- seriam ~40.000
/// `AnimatedSprite2D`. Pra comparar, os tetos que este projeto ja aceitou: 3.000 decalques
/// permanentes, 120 decalques com prazo, 24 nodes de poeira, 12 luzes de ki. Quarenta mil sprites
/// e **duas ordens de grandeza** fora de qualquer um deles.
///
/// ============================ ENTAO E DENSIDADE PERTO DO JOGADOR, E ELE NAO PERDE NADA ============================
/// Da cadeira do jogador, *"pelo mapa todo, de forma aleatoria"* e **indistinguivel** de *"em toda a
/// tela, sorteado, e se renovando enquanto ele anda"* -- ele nunca ve o mapa todo, ele ve a tela. O
/// sorteio entao cobre exatamente o retangulo que a camera toca, e a populacao sai da AREA VISIVEL
/// vezes a densidade. Medido com os numeros reais deste projeto (viewport 1280x720, zoom 2..6):
/// 41 pedras no zoom mais aberto, 20 no padrao, 7 no mais fechado -- e **isso nao cresce nunca**,
/// nem com o tamanho do mapa nem com os cinco minutos passando.
///
/// A mesma escolha ja esta feita duas vezes neste jogo, e pela mesma razao: o chao solto da
/// cinematica de transformacao (`Transformacao.MontarOChaoSolto`) e o proprio ESPACO, que e maior
/// que qualquer planeta e entra instantaneo porque desenha so o que cabe na tela.
/// ==========================================================================================================
///
/// ============================ POR QUE ISTO **NAO** E UMA SEGUNDA COPIA DO CHAO SOLTO DA CINEMATICA ============================
/// A pergunta e legitima -- `Transformacao` ja faz pedra levitando, e a regra da casa e nao ter dois
/// motores pra a mesma coisa. As duas respondem perguntas DIFERENTES, e a diferenca esta no sorteio:
///
///   * na CINEMATICA o sorteio e `GD.Randi()`, local, e o proprio comentario de la diz que isso e
///     deliberado: *"a cena e do cliente e nao viaja pela rede, entao duas maquinas assistindo a
///     mesma transformacao veem pedras em tiles diferentes, e isso nao e defeito"*;
///   * na AGONIA isso **seria** defeito. E um acontecimento do MUNDO que dura cinco minutos e que
///     todo mundo naquele planeta esta olhando junto; dois amigos lado a lado tem que ver a MESMA
///     pedra na mesma pedra de chao, ou o efeito vira protetor de tela rodando em paralelo -- que e
///     exatamente o diagnostico que ja tirou o sorteio do RAIO das maos do cliente.
///
/// Entao aqui *"tem pedra no tile (x,y) agora?"* e **funcao pura de (seed do mundo, x, y, ciclo)**:
/// sem estado, sem memoria, sem pacote. Mesma disciplina do terreno, do ceu de estrelas e da lua.
/// Nao da pra derivar isso do sorteio da cinematica sem trocar o sorteio dela -- e trocar o dela
/// seria mudar uma cena que o dono ja aprovou olhando.
///
/// **O que NAO foi duplicado**: os numeros. A fracao de chao solto e a vida da pedra sao os do DM e
/// continuam morando em `Transformacao` (`FracaoDoChaoSolto`, `VidaMinima`/`VidaMaxima`,
/// `CaminhoDasPedras`), lidos daqui. Se alguem afinar a cena, a agonia acompanha.
/// ==========================================================================================================================
///
/// ============================ E O RESTO DO A4 JA EXISTIA ============================
/// O clima `Destruicao` ja e, na tabela de desenho do `ClimaNaTela`, a UNICA entrada em que o que
/// cai vai **PARA CIMA** (velocidade -900): *"sao pedacos do chao sendo arrancados pela explosao que
/// vem de baixo"*, cor de rocha quente, a massa mais densa da tabela, paralaxe 0,9. Sao 900
/// particulas de um pool de tamanho FIXO, um draw call, e a densidade delas ja segue a forca do ceu
/// -- que agora e a rampa. Estas pedras sao a camada de perto; aquelas sao a de longe.
/// ====================================================================================
/// </summary>
public partial class PedrasDaAgonia : Node2D
{
	/// <summary>
	/// A AGONIA DESTE CHAO, 0 a 1 -- escrita pelo <see cref="World"/> a cada quadro.
	///
	/// **Nao e calculada aqui.** E a mesma `MortePlanetaria.Intensidade` que aperta o ceu, encurta o
	/// tremor e engrossa a crosta do planeta visto do espaco. Ver o cabecalho daquela funcao.
	/// </summary>
	public double Agonia;

	/// <summary>A seed do MUNDO -- o que faz duas telas concordarem. Ver o cabecalho.</summary>
	public ulong Seed;

	/// <summary>
	/// TETO DURO DE PEDRAS VIVAS. A conta da camera ja da 7..41, entao ele nao morde no uso normal --
	/// ele existe pro caso anormal (uma janela enorme, um zoom que alguem afrouxe depois) nao virar
	/// o unico teto que este jogo nao tem. E a mesma disciplina dos 120 decalques e dos 24 nodes de
	/// poeira: o pior caso e escrito, e nao descoberto.
	/// </summary>
	private const int MaxPedras = 64;

	/// <summary>
	/// DE QUANTO EM QUANTO TEMPO O SORTEIO E REFEITO, em segundos.
	///
	/// **Nada pesado dentro do tique**: varrer o retangulo visivel sao 135 celulas no zoom padrao e
	/// 273 no mais aberto, com um hash cada. Isso e barato uma vez por quarto de segundo e e
	/// desperdicio 60 vezes por segundo, pelos 310 s inteiros, sem que nada mude entre um quadro e
	/// o seguinte -- a pedra vive dezenas de segundos.
	/// </summary>
	private const double SegundosEntreSorteios = 0.25;

	/// <summary>
	/// QUANTO DURA O CICLO DE UMA CELULA, em segundos -- o meio da faixa do DM
	/// (`spawn(rand(100,400))`, `dusts.dm:207`, que sao 10 a 40 s).
	///
	/// E o periodo em que a celula RESSORTEIA se tem pedra. Ele e o mesmo pra todas de proposito: o
	/// que espalha as trocas no tempo (pra o chao nao piscar inteiro de uma vez) e o deslocamento por
	/// celula la embaixo, e nao um periodo diferente por celula -- com periodos diferentes as celulas
	/// entrariam em fase de novo mais tarde.
	/// </summary>
	private const double SegundosDoCiclo = 25.0;

	private readonly System.Collections.Generic.Dictionary<Vector2I, AnimatedSprite2D> _vivas = [];
	private SpriteFrames? _folha;
	private double _proximoSorteio;
	private bool _avisou;

	public override void _Ready()
	{
		// TOPLEVEL: as pedras sao do CHAO e nao de quem esta olhando. Sem isto elas herdariam a
		// posicao do pai e "perseguiriam" o dono -- o mesmo motivo pelo qual o chao solto da
		// cinematica e `TopLevel`.
		TopLevel = true;
		ZIndex = -1;
		ZAsRelative = false;

		// EXISTIR NA PASTA NAO E ESTAR IMPORTADO -- mesma guarda da cinematica: um `.tres` sem o
		// `.png` importado carrega NULO e o efeito some calado.
		if (ResourceLoader.Exists(Transformacao.CaminhoDasPedras))
			_folha = ResourceLoader.Load<SpriteFrames>(Transformacao.CaminhoDasPedras);
		else if (!_avisou)
		{
			_avisou = true;
			GD.PushWarning($"[agonia] `{Transformacao.CaminhoDasPedras}` nao resolve -- "
						 + "o planeta vai agonizar sem pedra no chao.");
		}
	}

	public override void _Process(double delta)
	{
		// A SAIDA BARATA VEM PRIMEIRO, e ela e o caso de 99,99% dos quadros do jogo: nenhum planeta
		// esta morrendo. Uma comparacao de `double` e um `Count` -- e nada mais roda.
		if (Agonia <= 0.001)
		{
			if (_vivas.Count > 0) Limpar();
			return;
		}
		if (_folha == null) return;

		_proximoSorteio -= delta;
		if (_proximoSorteio > 0) return;
		_proximoSorteio = SegundosEntreSorteios;

		Sortear();
	}

	/// <summary>
	/// ============================ O SORTEIO: A CAMERA DECIDE ONDE, A SEED DECIDE QUAIS ============================
	/// O retangulo vem da CAMERA e nao de um numero escrito, pelo mesmo motivo que a cinematica
	/// aprendeu na pele (*"ta mt perto do personagem"*, e o retangulo cravado era METADE do que a
	/// tela mostra): a pergunta certa e feita a camera, e ai o efeito se resolve sozinho em qualquer
	/// zoom e em qualquer janela.
	///
	/// A DENSIDADE E A RAMPA: `FracaoDoChaoSolto x agonia`. No comeco dos cinco minutos ha uma pedra
	/// ou duas na tela; no ultimo minuto, o chao inteiro esta se soltando. E o `prob(15)` do DM
	/// continua sendo o TETO, e nao um numero novo.
	/// ==========================================================================================================
	/// </summary>
	private void Sortear()
	{
		const int T = ZoneCollision.TileSize;

		Camera2D? cam = GetViewport()?.GetCamera2D();
		if (cam == null) return;

		float zoom = cam.Zoom.X > 0.01f ? cam.Zoom.X : 1f;
		Vector2 meia = GetViewportRect().Size / (2f * zoom);
		Vector2 centro = cam.GetScreenCenterPosition();

		int x0 = Mathf.FloorToInt((centro.X - meia.X) / T);
		int x1 = Mathf.FloorToInt((centro.X + meia.X) / T);
		int y0 = Mathf.FloorToInt((centro.Y - meia.Y) / T);
		int y1 = Mathf.FloorToInt((centro.Y + meia.Y) / T);

		double densidade = Transformacao.FracaoDoChaoSolto * System.Math.Clamp(Agonia, 0, 1);
		double agora = World.Instancia?.TempoDoMundo ?? 0;

		ZoneCollision? mapa = World.Instancia?.Colisao;

		var querem = new System.Collections.Generic.HashSet<Vector2I>();

		for (int y = y0; y <= y1; y++)
			for (int x = x0; x <= x1; x++)
			{
				if (querem.Count >= MaxPedras) break;

				// ============================ TRES HASHES DA MESMA MISTURA, E ELA E A DO MUNDO ============================
				// `Espaco.Misturar` e a mesma funcao que gera o universo, as estrelas e o terreno --
				// estavel entre execucoes e entre maquinas (o `GetHashCode()` do .NET nao serve: ele e
				// randomizado por processo, e duas telas discordariam).
				//
				//   `h`  -> o DESLOCAMENTO do relogio desta celula, pra o chao nao trocar inteiro de
				//           uma vez. Sem ele, a cada 25 s todas as pedras da tela piscariam juntas.
				//   `q`  -> o sorteio DESTE ciclo: tem pedra ou nao.
				// ==================================================================================================
				ulong h = Espaco.Misturar(Seed ^ 0x9E37_79B9_7F4A_7C15UL, (ulong)(uint)x, (ulong)(uint)y);
				double desloc = h % 1000 / 1000.0 * SegundosDoCiclo;
				long ciclo = (long)System.Math.Floor((agora + desloc) / SegundosDoCiclo);

				ulong q = Espaco.Misturar(h, (ulong)ciclo, 0x5A17UL);
				if (q % 10000 / 10000.0 >= densidade) continue;

				// PEDRA NAO SAI DE DENTRO DE PAREDE -- `if(T && !T.density)` (`lssjbuff.dm:471`),
				// a mesma guarda da cinematica. Sem mapa (zona ainda carregando) nao ha o que checar.
				if (x < 0 || y < 0) continue;
				if (mapa != null && mapa.BlockedCell(x, y)) continue;

				querem.Add(new Vector2I(x, y));
			}

		// NASCE O QUE FALTA...
		foreach (Vector2I c in querem)
		{
			if (_vivas.ContainsKey(c)) continue;

			string anim = _folha!.GetAnimationNames()[0];
			var p = new AnimatedSprite2D
			{
				SpriteFrames = _folha,
				Animation = anim,
				// CENTRO DO SPRITE NO CENTRO DA CELULA (o `+ 0.5`): o quadro tem exatamente um tile de
				// lado, entao a pedra COBRE a celula -- que e o que o `new/obj/meff/Rising(T)` do BYOND
				// faz de graca, porque la o obj herda a posicao do turf.
				Position = new Vector2((c.X + 0.5f) * T, (c.Y + 0.5f) * T),
				ZIndex = -1,
				ZAsRelative = false,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			AddChild(p);
			p.Play();
			_vivas[c] = p;
		}

		// ...E MORRE O QUE SAIU DO SORTEIO OU DA TELA.
		foreach (Vector2I c in _vivas.Keys.ToList())
		{
			if (querem.Contains(c)) continue;
			if (IsInstanceValid(_vivas[c])) _vivas[c].QueueFree();
			_vivas.Remove(c);
		}
	}

	private void Limpar()
	{
		foreach (AnimatedSprite2D p in _vivas.Values)
			if (IsInstanceValid(p)) p.QueueFree();
		_vivas.Clear();
	}

	/// <summary>Quantas pedras estao vivas agora -- pra bancada medir o RESULTADO e nao o pedido.</summary>
	public int PedrasVivasDeTeste => _vivas.Count;

    /// <summary>Onde elas estao, no mundo. Pra bancada conferir alinhamento a grade e determinismo.</summary>
    public Vector2[] PedrasDeTeste
    {
        get
        {
            var l = new System.Collections.Generic.List<Vector2>(_vivas.Count);
            foreach (AnimatedSprite2D p in _vivas.Values)
                if (IsInstanceValid(p)) l.Add(p.GlobalPosition);
            l.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            return [.. l];
        }
    }
}
