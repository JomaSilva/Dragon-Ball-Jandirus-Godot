using Godot;

namespace Jandirus.Client;

/// <summary>
/// **OS PONTINHOS BRANCOS DO AR DE UM MUNDO DIVINO** -- o sinal de que a atmosfera daqui e outra.
///
/// ============================ O PEDIDO, E O QUE ELE TROCOU ============================
/// O dono (2026-10-12): *"tire o circulo/brilho q fica em volta do personagem em planetas com atmosfera divina,
/// so coloca um efeito (como acontece com os climas) q fica constante no planeta de uns pontinhos brancos q fica
/// voando pela atmosfera (mas nao sao muitos, so alguns pontinhos pra representar a divindade)"*.
///
/// O unico brilho ligado a atmosfera divina era o HALO DOURADO do disco do planeta no espaco (um clarao de 1,55
/// raio em volta dele, `PlanetaDesenhado.Divino.cs`), e ele saiu por este pedido -- por algumas horas: o dono o quis
/// de volta no mesmo dia (*"o halo no planeta de god ki em si vc pode deixar"*). Ficaram OS DOIS sinais: o halo no
/// espaco e, na SUPERFICIE, uns poucos pontos de luz derivando pelo ar enquanto se esta num mundo divino. A carta
/// estelar continua marcando esses mundos de dourado -- e por ela que se acha um antes de pousar.
///
/// (Um circulo em volta do PERSONAGEM, preso ao lugar, nunca existiu neste port: o que ha no corpo sao os aneis de
/// luz do DESPERTAR do ki divino, uma vez so -- `World.KiDivino.cs`.)
/// ======================================================================================
///
/// ============================ OS PONTOS ESTAO NO MUNDO, E NAO NA LENTE ============================
/// A mesma licao do `ClimaNaTela`: preso a tela, o ponto anda junto com o jogador e le como sujeira no vidro. Cada
/// ponto tem um lugar no MUNDO e uma deriva lenta; a tela e um ladrilho que se repete, entao o ponto que sai por
/// um lado volta pelo outro e a contagem na tela nunca muda. Quem anda ve os pontos ficarem pra tras.
///
/// SEM PARTICULA E SEM SHADER DO PROJETO: sao <see cref="Pontos"/> retangulos com a radial que as luzes ja usam
/// (`Fogo.Radial`), no material aditivo e sem luz das faiscas do estouro de ki -- a mesma chave de
/// `CanvasItemMaterial`, ou seja o shader que o motor ja gerou. De noite eles continuam brancos (o ambiente nao os
/// escurece), que e quando mais aparecem.
/// ================================================================================================
///
/// QUEM DECIDE SE O LUGAR E DIVINO e a pergunta pura que o cartao Ceu ja faz (`MenuJogo.LugarDivino`): a seed do
/// universo e a zona. Nenhum pacote novo.
/// </summary>
public partial class PoeiraDivina : Node2D
{
	/// <summary>O nome do node. Uma casa so: as bancadas (`--diaggodki`, `--diagv020`) procuram por ele.</summary>
	public const string NomeDoNode = "PoeiraDivina";

	/// <summary>Quantos pontos na tela. Poucos de proposito: *"nao sao muitos, so alguns pontinhos"*.</summary>
	public const int Pontos = 14;

	/// <summary>O lado de cada ponto, em px de mundo (a radial some na borda: o miolo aceso e um terco disso).</summary>
	private const float Lado = 7f;

	/// <summary>A deriva, em px de mundo por segundo: devagar, e mais pra cima que pros lados.</summary>
	private const float DerivaMinima = 5f, DerivaMaxima = 14f;

	/// <summary>Acima dos corpos e da chuva (70, `ClimaNaTela`), abaixo da cinematica de transformacao (80) e do veu de visao (90).</summary>
	private const int Camada = 72;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--diaggodki`, `--diagv020`): o ar divino nao desenha nada -- o mundo de antes de
	/// 2026-10-12 visto da superficie. Falso em jogo, sempre.
	/// </summary>
	public static bool ApagadaDeTeste;

	private double _t;
	private Texture2D _ponto = null!;

	/// <summary>Os pontos estao sendo desenhados neste quadro? So pra bancada.</summary>
	public bool AcesaDeTeste => Visible;

	/// <summary>Onde os pontos cairam no ultimo desenho, em px de mundo. So pra bancada: quantos sao, e se estao na tela.</summary>
	public List<Vector2> PontosDesenhadosDeTeste { get; } = [];

	public override void _Ready()
	{
		ZAsRelative = false;
		ZIndex = Camada;
		Visible = false;
		_ponto = Fogo.Radial(LuzDeKi.RaioDaTextura);
		Material = new CanvasItemMaterial
		{
			LightMode = CanvasItemMaterial.LightModeEnum.Unshaded,
			BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
		};
	}

	public override void _Process(double delta)
	{
		bool divino = !ApagadaDeTeste && GameClient.Instance is { } cli && MenuJogo.LugarDivino(cli);
		if (divino != Visible)
		{
			Visible = divino;
			if (!divino) PontosDesenhadosDeTeste.Clear();
		}
		if (!divino) return;
		_t += delta;
		QueueRedraw();
	}

	/// <summary>Um numero de 0 a 1, fixo pra cada (ponto, canal): o lugar, a deriva e a fase do brilho saem daqui.</summary>
	private static float Sorte(int ponto, int canal)
	{
		uint h = unchecked((uint)(ponto * 374761393 + canal * 668265263));
		h = (h ^ (h >> 13)) * 1274126177u;
		return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0x1000000;
	}

	public override void _Draw()
	{
		PontosDesenhadosDeTeste.Clear();
		if (GetViewport() is not { } tela) return;

		// O LADRILHO E A TELA, EM PX DE MUNDO (com uma margem, pra o ponto entrar ja inteiro).
		Transform2D paraOMundo = tela.CanvasTransform.AffineInverse();
		Vector2 canto = paraOMundo * Vector2.Zero;
		Vector2 tamanho = paraOMundo.BasisXform(tela.GetVisibleRect().Size) + new Vector2(Lado * 2, Lado * 2);
		canto -= new Vector2(Lado, Lado);
		if (tamanho.X <= 1 || tamanho.Y <= 1) return;

		for (int i = 0; i < Pontos; i++)
		{
			// O LUGAR NO MUNDO: a base do ponto mais a deriva dele ate agora.
			float rumo = Mathf.Lerp(-2.4f, -0.75f, Sorte(i, 2));                       // pra cima, abrindo pros lados
			float passo = Mathf.Lerp(DerivaMinima, DerivaMaxima, Sorte(i, 3));
			var noMundo = new Vector2(
				Sorte(i, 0) * tamanho.X + Mathf.Cos(rumo) * passo * (float)_t,
				Sorte(i, 1) * tamanho.Y + Mathf.Sin(rumo) * passo * (float)_t);

			// ...DOBRADO PRA DENTRO DO LADRILHO DA TELA: o que sai por um lado volta pelo outro.
			var naTela = new Vector2(
				canto.X + Mathf.PosMod(noMundo.X - canto.X, tamanho.X),
				canto.Y + Mathf.PosMod(noMundo.Y - canto.Y, tamanho.Y));

			// O BRILHO RESPIRA, cada ponto na propria fase: nenhum pisca junto com outro.
			float fase = (float)_t * Mathf.Lerp(0.5f, 1.1f, Sorte(i, 4)) + Sorte(i, 5) * Mathf.Tau;
			float brilho = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(fase));
			float lado = Lado * Mathf.Lerp(0.7f, 1.15f, Sorte(i, 6));

			Vector2 local = ToLocal(naTela);
			DrawTextureRect(_ponto, new Rect2(local - new Vector2(lado, lado) * 0.5f, new Vector2(lado, lado)),
							tile: false, new Color(1f, 1f, 1f, brilho));
			PontosDesenhadosDeTeste.Add(naTela);
		}
	}
}
