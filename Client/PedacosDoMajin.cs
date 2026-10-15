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
/// A RECONSTRUCAO: depois da volta, o corpo toca uma vez a `regenerate` da PROPRIA folha; a folha que nao a tem (a
/// "Majin - Form 3") toca a da `Majin1` -- o dono: *"n sei se o sprite de todos os majins tem essa animaçao de
/// reconstruir o corpo, se nao tiverem é so trocar o sprite pra outro quando for reconstruir o corpo"*. Ver
/// <see cref="FolhaDaReconstrucao"/>.
///
/// QUEM A CRIA E A RECOMPOE e o <see cref="World.MarcarDesfeito"/>, pelo `Ocupacao.Desfeito` do snapshot. FILHA DO
/// CORPO, como a <see cref="NaveDesenhada"/>: morre com ele. Os irmaos que ela esconde (o boneco, a aura, a nebulosa...)
/// voltam quando ela sai da arvore -- por QUALQUER caminho, inclusive o corpo saindo de vista. A camera do corpo local
/// fica de fora: ela nao desenha nada, e e ela que segue o jogador.
/// ===============================================================================================================
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

	/// <summary>A volta dos pedacos, em segundos -- o `walk_towards(A,src,2)` do DM, com o `spawn(10) del(A)` de teto.</summary>
	public const double SegundosDaVolta = 0.6;

	/// <summary>A animacao da reconstrucao, nas folhas de Majin.</summary>
	public const string AnimDaReconstrucao = "regenerate";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv030`): a tela ignora o `Ocupacao.Desfeito` -- o corpo fica desenhado, caido, sem
	/// pedaco nenhum. Lido pelo <see cref="World.MarcarDesfeito"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CorpoInteiroNaTelaDeTeste;

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
	private double _relogio, _duracaoDoRefazer;

	/// <summary>A virada pro refeito ja chegou: os pedacos estao voltando (ou o corpo se reconstruindo).</summary>
	public bool Recompondo { get; private set; }

	/// <summary>
	/// A FOLHA DA RECONSTRUCAO: a do corpo, se ela tem a `regenerate`; senao a da `Majin1`. Nula sem nenhuma das duas -- e
	/// ai o corpo so reaparece.
	/// </summary>
	public static SpriteFrames? FolhaDaReconstrucao(SpriteFrames? doCorpo) =>
		doCorpo != null && doCorpo.HasAnimation(AnimDaReconstrucao) ? doCorpo
		: FolhasPresas.Carregar(FolhaDosPedacos) is { } majin1 && majin1.HasAnimation(AnimDaReconstrucao) ? majin1
		: null;

	/// <summary>As animacoes dos pedacos no chao agora, pra bancada.</summary>
	public List<string> PedacosDeTeste() => [.. _pedacos.Where(IsInstanceValid).Select(p => p.Animation.ToString())];

	/// <summary>A folha e a animacao da reconstrucao em curso (nula e vazia fora dela), pra bancada.</summary>
	public (SpriteFrames? Folha, string Anim) RefazendoDeTeste =>
		_refazendo is { } r && IsInstanceValid(r) ? (r.SpriteFrames, r.Animation.ToString()) : (null, "");

	public override void _Ready()
	{
		foreach (Node n in GetParent().GetChildren())
			if (n != this && n is not Camera2D && n is CanvasItem { Visible: true } irmao)
			{
				irmao.Visible = false;
				_escondidos.Add(irmao);
			}

		if (FolhasPresas.Carregar(FolhaDosPedacos) is not { } folha) return;
		var sorte = new RandomNumberGenerator { Seed = (ulong)Math.Max(Semente, 1) };
		float tile = Jandirus.Core.World.ZoneCollision.TileSize;
		for (int i = 0; i < Quantos; i++)
		{
			var onde = new Vector2(sorte.RandiRange(-AlcanceEmTiles, AlcanceEmTiles), sorte.RandiRange(-AlcanceEmTiles, AlcanceEmTiles)) * tile;
			var pedaco = new AnimatedSprite2D
			{
				SpriteFrames = folha,
				Animation = $"chunk{1 + i % 3}",
				Centered = true,
				Position = onde,
				Material = Tinta,
				SelfModulate = Pele,
			};
			AddChild(pedaco);
			pedaco.Play();
			_pedacos.Add(pedaco);
			_deOnde.Add(onde);
		}
	}

	/// <summary>A virada pro refeito: os pedacos andam de volta, e depois o corpo se reconstroi.</summary>
	public void Recompor()
	{
		if (Recompondo) return;
		Recompondo = true;
		_relogio = 0;
	}

	public override void _Process(double delta)
	{
		if (!Recompondo) return;
		_relogio += delta;

		if (_refazendo == null)
		{
			float k = (float)Math.Min(_relogio / SegundosDaVolta, 1);
			for (int i = 0; i < _pedacos.Count; i++) _pedacos[i].Position = _deOnde[i].Lerp(Vector2.Zero, k);
			if (k < 1) return;

			foreach (AnimatedSprite2D p in _pedacos) p.QueueFree();
			_pedacos.Clear();
			ComecarAReconstrucao();
			_relogio = 0;
			return;
		}

		if (_relogio >= _duracaoDoRefazer) QueueFree();
	}

	/// <summary>Os pedacos chegaram: a `regenerate` toca UMA vez no lugar do corpo (ver <see cref="FolhaDaReconstrucao"/>).</summary>
	private void ComecarAReconstrucao()
	{
		if (FolhaDaReconstrucao(FolhaDoCorpo) is not { } folha) { QueueFree(); return; }

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
		_refazendo.Play();
		_duracaoDoRefazer = folha.GetFrameCount(AnimDaReconstrucao) / Math.Max(folha.GetAnimationSpeed(AnimDaReconstrucao), 1);
	}

	public override void _ExitTree()
	{
		foreach (CanvasItem c in _escondidos)
			if (IsInstanceValid(c)) c.Visible = true;
		_escondidos.Clear();
	}
}
