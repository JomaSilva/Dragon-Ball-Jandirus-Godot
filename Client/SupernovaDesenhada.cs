using Godot;
using Jandirus.Core.Npc;

namespace Jandirus.Client;

/// <summary>
/// A SUPERNOVA NA TELA (v0.3.0, item 22): o sol que nasce sobre o chefe, cresce, cai uns tiles e explode -- e e ai que a
/// destruicao do planeta comeca. As regras e os prazos estao em <see cref="Supernova"/> (Core); aqui e so o desenho.
///
/// O SOL E O `MegaSupernova2018.dmi` DO DM (oito quadros de um sol com o anel laranja e os raios, ja convertido em
/// `Assets/Sprites/Blasts`) -- e a "imagem que parece um sol" do pedido do dono.
///
/// ============================ O RELOGIO E DE QUEM OLHA ============================
/// O servidor diz SO que o corpo esta concentrando (`Ocupacao.Supernova`, no snapshot de sempre). O desenho anda pelos
/// prazos do Core a partir do quadro em que ESTA tela o viu: cresce sobre a cabeca durante a carga, sai da mao, cai
/// <see cref="Supernova.TilesDaQueda"/> pro sul (o `walk(A, SOUTH, 3)` do DM) e explode no chao. O que manda a explosao e o
/// FIM DA QUEDA; o chefe saindo do mundo (o servidor o tira no instante em que a destruicao comeca) solta o sol da mao se
/// ele ainda estava la -- quem chegou no meio da carga ve a historia inteira, so que mais curta.
/// =================================================================================
///
/// NAO E FILHO DO CORPO de proposito: ele sobrevive ao chefe (o chefe sai do mundo e o sol ainda esta caindo), e por isso
/// mora nos atores do mundo (`World.MarcarSupernova`) e segue o corpo pela pergunta <see cref="Onde"/>.
/// </summary>
public partial class SupernovaDesenhada : Node2D
{
	/// <summary>A folha do sol: `MegaSupernova2018.dmi`, oito quadros de 348 x 370.</summary>
	public const string FolhaDoSol = "res://Assets/Sprites/Blasts/MegaSupernova2018.tres";

	/// <summary>
	/// (2026-10-17) A folha da Death Ball do Freeza de Namek: a da tecnica (`deathball2017purple2`, quatro quadros de 98 x 89). Ela
	/// cresce, cai e explode pelo MESMO relogio do sol -- *"seguindo a mesma logica da super nova"*, o dono.
	/// </summary>
	public const string FolhaDaDeathBall = "res://Assets/Sprites/Blasts/deathball2017purple2.tres";

	/// <summary>Qual esfera esta e -- o servidor diz pela ocupacao do corpo (`Ocupacao.Supernova` / `DeathBallDoUltimato`).</summary>
	public AtaqueDoUltimato Ataque = AtaqueDoUltimato.Supernova;

	/// <summary>A folha DESTA esfera.</summary>
	public string Folha => Ataque == AtaqueDoUltimato.DeathBall ? FolhaDaDeathBall : FolhaDoSol;

	/// <summary>A largura de um quadro da folha -- e por ela que o diametro vira escala.</summary>
	private float LadoDaFolha => Ataque == AtaqueDoUltimato.DeathBall ? 98f : 348f;

	/// <summary>O sol no fim da carga: seis tiles de diametro -- maior que qualquer corpo, e ainda sobre a cabeca.</summary>
	public const float DiametroNoFimDaCarga = 6 * 32f;

	/// <summary>...e encostando no chao, no fim da queda: ele cresce enquanto desce, como quem se aproxima.</summary>
	public const float DiametroNoChao = 9 * 32f;

	/// <summary>Quanto acima do ponto desenhado do corpo o pe do sol fica (a cabeca e o braco erguido).</summary>
	private const float AlturaSobreACabeca = 40f;

	/// <summary>Quanto o estouro do impacto dura e cresce -- o `ms` do `World.EstouroNoMundo` (1,5 s, ~650 px de raio).</summary>
	public const long MsDoEstouro = 1500;

	/// <summary>Onde o corpo de quem conjura esta DESENHADO agora; nulo quando ele saiu do mundo.</summary>
	public Func<Vector2?> Onde = () => null;

	/// <summary>O estouro no mundo, no ponto em que o sol bateu -- o mesmo do fim de um planeta (`World.EstouroNoMundo`).</summary>
	public Action<Vector2, long>? Estourar;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv030`): a tela ignora a Supernova -- o chefe congela imune sem sol nenhum, e o
	/// planeta explode do nada (o jogo de antes). Lido pelo `World.MarcarSupernova`.
	/// </summary>
	public static bool SolIgnoradoDeTeste;

	/// <summary>Quantos sois ja bateram no chao nesta sessao (bancada: o node se apaga logo depois do estouro).</summary>
	internal static int EstourosDeTeste { get; private set; }

	private AnimatedSprite2D _sol = null!;
	private double _t, _tDoSolto, _apagando;
	private Vector2 _cabeca, _soltoEm;
	private bool _solto, _estourou, _cancelada;

	/// <summary>O diametro desenhado agora, em pixels (bancada).</summary>
	internal float DiametroDeTeste => _sol.Scale.X * LadoDaFolha;

	/// <summary>O centro do sol agora (bancada).</summary>
	internal Vector2 CentroDeTeste => _sol.Position;

	/// <summary>O sol ja saiu da mao? (bancada)</summary>
	internal bool SoltoDeTeste => _solto;

	public override void _Ready()
	{
		// NA FRENTE DOS CORPOS E DO ESTOURO COMUM (40): e o maior objeto da tela, e ele tapa quem esta embaixo dele.
		ZIndex = 41;
		_sol = new AnimatedSprite2D { Name = "Sol", SpriteFrames = ResourceLoader.Load<SpriteFrames>(Folha) };
		AddChild(_sol);
		_sol.Play("default");
		if (Onde() is { } c) _cabeca = c;
		Desenhar();
	}

	/// <summary>
	/// O SERVIDOR DESISTIU com o sol ainda na mao (o servidor esvaziou, ou o chefe caiu por um caminho que a imunidade nao
	/// cobre): ele se apaga, sem estouro. Solto, ele termina a queda -- quem decide o fim e o servidor, e ele ja decidiu.
	/// </summary>
	public void Cancelar()
	{
		if (!_solto && !_estourou) _cancelada = true;
	}

	public override void _Process(double delta)
	{
		_t += delta;

		if (_cancelada || _estourou)
		{
			// APAGA EM MEIO SEGUNDO -- o estouro ja tomou a tela, ou o servidor desistiu.
			_apagando += delta;
			Modulate = new Color(1, 1, 1, (float)Math.Max(0, 1 - _apagando / 0.5));
			if (_apagando >= 0.5) QueueFree();
			return;
		}

		Vector2? corpo = Onde();
		if (!_solto)
		{
			if (corpo is { } c) _cabeca = c;
			if (_t >= Supernova.SegundosDeCarga || corpo == null) Soltar();
		}
		Desenhar();
		if (_solto && Supernova.ProgressoDaQueda(_t - _tDoSolto) >= 1) Explodir();
	}

	/// <summary>O sol deixa a mao: a queda parte do ponto em que ele esta agora.</summary>
	private void Soltar()
	{
		_solto = true;
		_tDoSolto = _t;
		_soltoEm = _sol.Position;
	}

	/// <summary>Bateu no chao: o estouro no mundo (o do fim de um planeta), e o sol se apaga dentro dele.</summary>
	private void Explodir()
	{
		_estourou = true;
		EstourosDeTeste++;
		Estourar?.Invoke(_sol.Position, MsDoEstouro);
	}

	private void Desenhar()
	{
		float diametro;
		Vector2 centro;
		if (!_solto)
		{
			diametro = DiametroNoFimDaCarga * Supernova.Crescimento(_t);
			centro = _cabeca + new Vector2(0, -AlturaSobreACabeca - diametro / 2f);
		}
		else
		{
			float queda = Supernova.ProgressoDaQueda(_t - _tDoSolto);
			diametro = Mathf.Lerp(DiametroNoFimDaCarga, DiametroNoChao, queda);
			centro = _soltoEm + new Vector2(0, Supernova.TilesDaQueda * 32f * queda);
		}
		_sol.Scale = Vector2.One * (diametro / LadoDaFolha);
		_sol.Position = centro;
	}
}
