using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ O MAJIN DESFEITO EM PEDACOS, NA TELA (dono, 2026-10-15) ============================
/// *"majin "morto" por soco n morre de verdade ele so fica em um KO mais longo e começa uma animaçao q tinha no DM
/// representando ele regenerando e voltando a vida"*.
///
/// E o ramo `DeathRegen >= 6` do `TestDeathRegen` (`DeathRegen.dm:152-173`): o corpo sai do mapa, cinco pedacos de
/// `Majin1.dmi` ficam espalhados em volta de onde ele caiu (`x+rand(-5,5)`), e no fim eles andam de volta pro corpo
/// (`walk_towards(A,src,2)`) e somem. O DM so desenhava o `chunk1`; a folha tem tres, e aqui os tres se revezam.
///
/// ============================ (2026-10-18) A VOLTA LENTA, E O LACO DOS QUATRO ULTIMOS QUADROS ============================
/// O dono: *"os pedaços por mais q separados se eu bater onde estava o corpo do majin eles viajam juntos, pq vc colocou eles como
/// 1 entidade so, eles tinham q ser separados e imunes a golpes etc e irem se movendo lentamente ate se encontrarem, e ao se
/// encontrarem deveriam começar a animaçao a qual circulei no print e ao chegarem nos naqueles 4 sprites finais da imagem ficaria em
/// um loop desses 4 sprites por uns segundos (uns 5 segundos)"*. Tres coisas mudaram:
///   * PRESOS AO CHAO, e nao ao corpo. Os pedacos eram filhos de um node que ia aonde o corpo fosse, e o soco no lugar arremessava
///     o corpo (invisivel) e os cinco com ele. Agora o node ancora no ponto do mundo em que o corpo se desfez (<see cref="_ancora"/>)
///     e cada pedaco anda o PROPRIO caminho dali. O golpe tambem deixou de mover o corpo no servidor (o `Arremessar` e o arrasto do
///     feixe recusam o desfeito -- `GameServer.Empurrao.cs`), mas a tela nao depende disso: se o corpo andasse, os pedacos ficariam;
///   * A VOLTA E O DESFEITO INTEIRO, e nao 0,6 s no fim. Espalhados num estouro curto, os cinco se arrastam devagar ate o centro e se
///     encontram no instante certo pra que...
///   * ...ao se encontrarem toque a `regenerate` da folha do corpo ATE os quatro ultimos quadros, e esses quatro fiquem em laco
///     <see cref="SegundosDoLaco"/>. A conta fecha com os 30 s do servidor (`CombatState.SegundosDesfeito`): e quando o corpo levanta e
///     esta tela sai.
/// O RELOGIO E O DE PAREDE (`Time.GetTicksUsec`), e nao a soma dos `delta`: com a janela sem foco o jogo roda a 4 quadros por segundo,
/// e a soma dos quadros fica pra tras do servidor -- a licao do `CorpoSecando`.
///
/// QUEM NAO VIU O COMECO (o corpo entrou na tela no meio do desfeito) tem o relogio contado de quando o viu, e o corpo levanta antes de
/// o laco chegar. Pro corpo que levanta antes da hora a volta e APRESSADA: os pedacos se juntam em <see cref="SegundosDaVoltaApressada"/>,
/// a reconstrucao toca ate o fim, e o corpo reaparece -- sem o laco, que e espera e ja nao ha o que esperar.
/// ===========================================================================================================================
///
/// A RECONSTRUCAO toca a `regenerate` da PROPRIA folha do corpo; a folha que nao a tem (a "Majin - Form 3") toca a da `Majin1` -- o
/// dono: *"n sei se o sprite de todos os majins tem essa animaçao de reconstruir o corpo, se nao tiverem é so trocar o sprite pra
/// outro quando for reconstruir o corpo"*. Ver <see cref="FolhaDaReconstrucao"/>.
///
/// QUEM A CRIA E A RECOMPOE e o <see cref="World.MarcarDesfeito"/>, pelo `Ocupacao.Desfeito` do snapshot. FILHA DO CORPO, como a
/// <see cref="NaveDesenhada"/>: morre com ele. Os irmaos que ela esconde (o boneco, a aura, a nebulosa...) voltam quando ela sai da
/// arvore -- por QUALQUER caminho, inclusive o corpo saindo de vista. A camera do corpo local fica de fora: ela nao desenha nada, e e
/// ela que segue o jogador.
/// </summary>
public partial class PedacosDoMajin : Node2D
{
	/// <summary>O nome do node. Uma casa so: quem cria e quem recompoe procuram por ele.</summary>
	public const string NomeDoNode = "PedacosDoMajin";

	/// <summary>A folha dos pedacos -- `A.icon='Majin1.dmi'` (`DeathRegen.dm:159`): a unica de Majin que os tem.</summary>
	public const string FolhaDosPedacos = "res://Assets/Sprites/Character Icons/Majins/Majin1.tres";

	/// <summary>`var/amount=5` (`DeathRegen.dm:154`).</summary>
	public const int Quantos = 5;

	/// <summary>Ate onde os pedacos caem, em tiles. O DM espalha cinco (`rand(-5,5)`); tres deixam a cena ao lado do corpo.</summary>
	public const int AlcanceEmTiles = 3;

	/// <summary>O estouro do comeco: os pedacos saem do centro e caem onde vao ficar.</summary>
	public const double SegundosDoEstouro = 0.35;

	/// <summary>Os quatro ultimos quadros da reconstrucao em laco, antes do corpo levantar -- o dono: *"uns 5 segundos"*.</summary>
	public const double SegundosDoLaco = 5;

	/// <summary>Quantos quadros do FIM da `regenerate` ficam em laco -- *"naqueles 4 sprites finais"*.</summary>
	public const int QuadrosDoLaco = 4;

	/// <summary>A volta de quem levantou antes da hora: os pedacos se juntam nisto. Ver o cabecalho.</summary>
	public const double SegundosDaVoltaApressada = 0.5;

	/// <summary>A animacao da reconstrucao, nas folhas de Majin.</summary>
	public const string AnimDaReconstrucao = "regenerate";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv030`): a tela ignora o `Ocupacao.Desfeito` -- o corpo fica desenhado, caido, sem
	/// pedaco nenhum. Lido pelo <see cref="World.MarcarDesfeito"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CorpoInteiroNaTelaDeTeste;

	/// <summary>
	/// (2026-10-18) DEFEITO INJETADO (robo `--diagpedacos`): os pedacos voltam a andar com o corpo -- o node segue o pai, como antes. Lido
	/// pelo <see cref="_Process"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool PresosAoCorpoDeTeste;

	/// <summary>A semente do espalhamento: o id do corpo -- a mesma cena em toda tela.</summary>
	public int Semente;

	/// <summary>A folha do corpo (a da reconstrucao, se ela a tiver) -- ver `CharacterVisual.CorpoParaOsPedacos`.</summary>
	public SpriteFrames? FolhaDoCorpo;

	/// <summary>A tinta e o brilho da pele do corpo: os pedacos tem a cor dele.</summary>
	public Material? Tinta;
	public Color Pele = Colors.White;

	private readonly List<AnimatedSprite2D> _pedacos = [];
	private readonly List<Vector2> _deOnde = [];
	private readonly List<CanvasItem> _escondidos = [];
	private AnimatedSprite2D? _refazendo;

	/// <summary>A folha da reconstrucao e quanto dura cada quadro dela, em segundos.</summary>
	private SpriteFrames? _folhaDoRefazer;
	private double[] _duracoes = [];

	/// <summary>O ponto do mundo em que o corpo se desfez. Ver o cabecalho: e aqui que os pedacos moram.</summary>
	private Vector2 _ancora;

	/// <summary>O nascimento desta cena no relogio de parede.</summary>
	private ulong _nasceuUsec;

	/// <summary>Quando os pedacos se encontram (segundos desde o nascimento). A volta apressada o traz pra perto.</summary>
	private double _encontro;

	/// <summary>A volta apressada: de quando ela parte (-1 = nao houve) e de onde cada pedaco partiu.</summary>
	private double _apressouEm = -1;
	private readonly List<Vector2> _deOndeApressou = [];

	/// <summary>Quando a reconstrucao comecou (segundos desde o nascimento); -1 = os pedacos ainda andam.</summary>
	private double _refazeuEm = -1;

	/// <summary>O corpo levantou no servidor: a cena termina assim que puder.</summary>
	public bool Recompondo { get; private set; }

	/// <summary>
	/// A FOLHA DA RECONSTRUCAO: a do corpo, se ela tem a `regenerate`; senao a da `Majin1`. Nula sem nenhuma das duas -- e
	/// ai o corpo so reaparece.
	/// </summary>
	public static SpriteFrames? FolhaDaReconstrucao(SpriteFrames? doCorpo) =>
		doCorpo != null && doCorpo.HasAnimation(AnimDaReconstrucao) ? doCorpo
		: FolhasPresas.Carregar(FolhaDosPedacos) is { } majin1 && majin1.HasAnimation(AnimDaReconstrucao) ? majin1
		: null;

	/// <summary>Os segundos desde o nascimento desta cena, no relogio de parede.</summary>
	private double Idade => (Time.GetTicksUsec() - _nasceuUsec) / 1e6;

	/// <summary>Quantos dos quadros da reconstrucao tocam UMA vez antes do laco (todos menos os quatro ultimos).</summary>
	private int QuadrosDaEntrada => Math.Max(0, _duracoes.Length - QuadrosDoLaco);

	/// <summary>Quanto dura a entrada da reconstrucao (os quadros antes do laco), em segundos.</summary>
	private double SegundosDaEntrada => _duracoes.Take(QuadrosDaEntrada).Sum();

	// ---- o que a bancada le ----

	/// <summary>As animacoes dos pedacos no chao agora, pra bancada.</summary>
	public List<string> PedacosDeTeste() => [.. _pedacos.Where(IsInstanceValid).Select(p => p.Animation.ToString())];

	/// <summary>Onde cada pedaco esta no MUNDO agora, pra bancada.</summary>
	public List<Vector2> PosicoesDeTeste() => [.. _pedacos.Where(IsInstanceValid).Select(p => p.GlobalPosition)];

	/// <summary>O ponto em que os pedacos se encontram (a ancora), pra bancada.</summary>
	public Vector2 AncoraDeTeste => _ancora;

	/// <summary>A folha e a animacao da reconstrucao em curso (nula e vazia fora dela), pra bancada.</summary>
	public (SpriteFrames? Folha, string Anim) RefazendoDeTeste =>
		_refazendo is { } r && IsInstanceValid(r) ? (r.SpriteFrames, r.Animation.ToString()) : (null, "");

	/// <summary>O quadro da reconstrucao na tela e quantos ela tem (-1 fora dela), pra bancada.</summary>
	public (int Quadro, int Total) QuadroDeTeste => _refazendo is { } r && IsInstanceValid(r) ? (r.Frame, _duracoes.Length) : (-1, 0);

	/// <summary>Os segundos desta cena e o instante marcado pro encontro, pra bancada.</summary>
	public (double Idade, double Encontro) RelogioDeTeste => (Idade, _encontro);

	public override void _Ready()
	{
		foreach (Node n in GetParent().GetChildren())
			if (n != this && n is not Camera2D && n is CanvasItem { Visible: true } irmao)
			{
				irmao.Visible = false;
				_escondidos.Add(irmao);
			}

		_ancora = GlobalPosition;
		_nasceuUsec = Time.GetTicksUsec();

		// A RECONSTRUCAO E CONHECIDA DESDE O COMECO, e e ela que marca a hora do encontro: os pedacos chegam pra que a entrada dela
		// acabe exatamente quando o laco tem os seus segundos ate os 30 do servidor.
		_folhaDoRefazer = FolhaDaReconstrucao(FolhaDoCorpo);
		if (_folhaDoRefazer != null)
		{
			double fps = Math.Max(_folhaDoRefazer.GetAnimationSpeed(AnimDaReconstrucao), 1);
			_duracoes = [.. Enumerable.Range(0, _folhaDoRefazer.GetFrameCount(AnimDaReconstrucao))
				.Select(i => _folhaDoRefazer.GetFrameDuration(AnimDaReconstrucao, i) / fps)];
		}
		_encontro = Math.Max(SegundosDoEstouro + 1,
			Jandirus.Core.Combat.CombatState.SegundosDesfeito - SegundosDoLaco - SegundosDaEntrada);

		if (FolhasPresas.Carregar(FolhaDosPedacos) is not { } folha) return;
		var sorte = new RandomNumberGenerator { Seed = (ulong)Math.Max(Semente, 1) };
		float tile = Jandirus.Core.World.ZoneCollision.TileSize;
		for (int i = 0; i < Quantos; i++)
		{
			var onde = new Vector2(sorte.RandiRange(-AlcanceEmTiles, AlcanceEmTiles), sorte.RandiRange(-AlcanceEmTiles, AlcanceEmTiles)) * tile;
			// NINGUEM CAI EM CIMA DO CENTRO: o pedaco sorteado no meio iria pra lugar nenhum, e o "se arrastam ate se encontrar" dele nao
			// existiria. Ele cai um tile pro lado.
			if (onde == Vector2.Zero) onde = new Vector2(i % 2 == 0 ? tile : -tile, 0);
			var pedaco = new AnimatedSprite2D
			{
				SpriteFrames = folha,
				Animation = $"chunk{1 + i % 3}",
				Centered = true,
				Position = Vector2.Zero,
				Material = Tinta,
				SelfModulate = Pele,
			};
			AddChild(pedaco);
			pedaco.Play();
			_pedacos.Add(pedaco);
			_deOnde.Add(onde);
		}
	}

	/// <summary>
	/// O CORPO LEVANTOU NO SERVIDOR (o fim do desfeito). No laco, a cena sai agora; na entrada da reconstrucao, ela termina e sai; com os
	/// pedacos ainda andando (quem nao viu o comeco, ou o corpo que levantou antes da hora), a volta e APRESSADA -- ver o cabecalho.
	/// </summary>
	public void Recompor()
	{
		if (Recompondo) return;
		Recompondo = true;
		if (_refazendo != null)
		{
			if (Idade - _refazeuEm >= SegundosDaEntrada) QueueFree();
			return;
		}
		double agora = Idade;
		if (agora >= _encontro - SegundosDaVoltaApressada) return;   // ja estao chegando: a volta normal e a apressada
		_apressouEm = agora;
		_deOndeApressou.Clear();
		foreach (AnimatedSprite2D p in _pedacos) _deOndeApressou.Add(p.Position);
		_encontro = agora + SegundosDaVoltaApressada;
	}

	public override void _Process(double delta)
	{
		// PRESOS AO CHAO: o corpo pode ir aonde for (um arremesso que o servidor deixasse passar, a correcao de posicao), e os pedacos
		// ficam onde ele se desfez.
		if (!PresosAoCorpoDeTeste) GlobalPosition = _ancora;
		double t = Idade;

		if (_refazendo == null)
		{
			if (t < _encontro) { AndarOsPedacos(t); return; }
			foreach (AnimatedSprite2D p in _pedacos) p.QueueFree();
			_pedacos.Clear();
			ComecarAReconstrucao(t);
			if (_refazendo == null) return;
		}

		double e = t - _refazeuEm;
		if (e < SegundosDaEntrada) { _refazendo.Frame = QuadroNo(e, 0, QuadrosDaEntrada); return; }
		// A ENTRADA ACABOU: quem ja levantou sai; quem nao levantou fica no laco dos quatro ultimos ate o servidor dizer.
		if (Recompondo) { QueueFree(); return; }
		int deLaco = _duracoes.Length - QuadrosDaEntrada;
		double volta = _duracoes.Skip(QuadrosDaEntrada).Sum();
		_refazendo.Frame = deLaco <= 0 || volta <= 0 ? 0 : QuadroNo((e - SegundosDaEntrada) % volta, QuadrosDaEntrada, deLaco);
	}

	/// <summary>O quadro (dentre `quantos` a partir de `primeiro`) que esta na tela `e` segundos depois do primeiro deles.</summary>
	private int QuadroNo(double e, int primeiro, int quantos)
	{
		double soma = 0;
		for (int i = primeiro; i < primeiro + quantos; i++)
		{
			soma += _duracoes[i];
			if (e < soma) return i;
		}
		return primeiro + quantos - 1;
	}

	/// <summary>
	/// OS CINCO NO CHAO: o estouro curto que os espalha, e o arrasto lento ate o centro. Cada um anda o PROPRIO caminho, com um balanco de
	/// lado e um respirar de corpo mole -- cinco pedacos vivos, e nao um bloco escorregando.
	/// </summary>
	private void AndarOsPedacos(double t)
	{
		for (int i = 0; i < _pedacos.Count; i++)
		{
			AnimatedSprite2D p = _pedacos[i];
			Vector2 alvo = _deOnde[i];
			Vector2 pos;
			if (_apressouEm >= 0)
			{
				float k = (float)Math.Clamp((t - _apressouEm) / Math.Max(_encontro - _apressouEm, 1e-3), 0, 1);
				pos = _deOndeApressou[i].Lerp(Vector2.Zero, k);
			}
			else if (t < SegundosDoEstouro)
			{
				float k = (float)(t / SegundosDoEstouro);
				pos = Vector2.Zero.Lerp(alvo, 1 - (1 - k) * (1 - k));   // sai rapido e assenta
			}
			else
			{
				float k = (float)Math.Clamp((t - SegundosDoEstouro) / Math.Max(_encontro - SegundosDoEstouro, 1e-3), 0, 1);
				float suave = k * k * (3 - 2 * k);
				Vector2 lado = alvo.Length() > 0 ? new Vector2(-alvo.Y, alvo.X).Normalized() : Vector2.Zero;
				pos = alvo.Lerp(Vector2.Zero, suave) + lado * (float)(Math.Sin(t * 2.1 + i * 1.3) * 2.5 * (1 - suave));
			}
			p.Position = pos;
			float respiro = (float)(Math.Sin(t * 5 + i * 0.9) * 0.07);
			p.Scale = new Vector2(1 + respiro, 1 - respiro);
		}
	}

	/// <summary>
	/// OS PEDACOS SE ENCONTRARAM: a `regenerate` toca no lugar do corpo (ver <see cref="FolhaDaReconstrucao"/>), quadro a quadro pelo
	/// relogio desta cena -- a entrada uma vez, os quatro ultimos em laco (ver <see cref="_Process"/>).
	/// </summary>
	private void ComecarAReconstrucao(double t)
	{
		if (_folhaDoRefazer is not { } folha || _duracoes.Length == 0) { QueueFree(); return; }

		_refazendo = new AnimatedSprite2D
		{
			SpriteFrames = folha,
			Animation = AnimDaReconstrucao,
			Centered = true,
			Material = Tinta,
			SelfModulate = Pele,
		};
		// ANCORADO COMO O BONECO: folha maior que a celula encosta pela base e pela esquerda (`CharacterVisual.Ancorar`).
		CharacterVisual.Ancorar(_refazendo, folha);
		AddChild(_refazendo);
		// QUEM ANDA OS QUADROS E O RELOGIO DESTA CENA, e nao o `Play()`: o laco e so dos quatro ultimos, e o `AnimatedSprite2D` so sabe
		// rodar a animacao inteira.
		_refazendo.Stop();
		_refazendo.Frame = 0;
		_refazeuEm = t;
	}

	public override void _ExitTree()
	{
		foreach (CanvasItem c in _escondidos)
			if (IsInstanceValid(c)) c.Visible = true;
		_escondidos.Clear();
	}
}
