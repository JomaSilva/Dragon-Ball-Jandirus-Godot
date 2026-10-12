using Godot;
using Jandirus.Core.Magic;

namespace Jandirus.Client;

/// <summary>
/// UMA SUPER ESFERA DO DRAGAO NO ESPACO -- o planetoide que se reivindica, e ao lado do qual se chama o Super Shenron.
///
/// ============================ O PEDIDO DO DONO (2026-10-11) ============================
/// *"faça as super esferas aparecerem"*. O servidor sempre mandou as que estao por perto (`S2C.SuperEsferas`: o
/// numero, a posicao, o nome do dono e se e minha -- o recorte por chunk do resto do espaco) e o cliente guardava
/// a lista sem desenhar nada: so o placar e a frase do radar apareciam, na aba World. Desde que o Super Shenron
/// so atende AO LADO de uma delas (`GameServer.DianteDeUmaSuper`), isso virou "va ate uma coisa que nao existe na
/// tela".
/// ======================================================================================
///
/// ============================ A ARTE E A DO ORIGINAL ============================
/// `Icons/SuperDragonball.dmi` do DM (`obj/items/SuperDragonBall`, `ProceduralSpace.dm:1495-1497`): sete estados,
/// "1".."7", de 64x64, um por numero de estrelas. Ja estava convertida no port (`SuperDragonball.png` + `.tres`,
/// sete animacoes de um quadro) e nunca tinha sido usada.
/// ================================================================================
///
/// ============================ O TAMANHO: O DISCO E O ALCANCE ============================
/// No DM o icone tem 64 px (dois tiles) e a descricao diz "do tamanho de um PLANETOIDE". Aqui ele e desenhado com
/// o raio do proprio alcance (<see cref="SuperEsferas.AlcanceDeReivindicar"/>, que e tambem o de invocar: 2 tiles),
/// ou seja 128 px de diametro -- a arte ampliada 2x, exata. Duas razoes:
///
///   * e um PLANETOIDE: menor que o menor planeta (220 px de diametro) e grande o bastante pra se ver assim que
///     entra no quadro. No zoom padrao (3) a tela mostra 427x240 px de mundo: o disco ocupa pouco mais da metade
///     da ALTURA (o menor planeta, 92%) e um oitavo da area -- o corpo, o ceu em volta e o HUD continuam a vista
///     (as fotos `desejos-07` e `desejos-08` da `--diagdesejos`);
///   * o que se ve e o que vale: o corpo esta "ao lado" (pode reivindicar, pode chamar o dragao) exatamente
///     quando esta encostado no disco. Um disco menor que o alcance deixaria o jogador adivinhando a distancia;
///     um maior o poria dentro da bola sem saber se ja chegou.
///
/// O raio SAI da constante do Core, e nao de um numero daqui: se o alcance mudar, o desenho muda junto.
/// ========================================================================================
///
/// ============================ LUZ PROPRIA: ELA NAO ESCURECE COM O ESPACO ============================
/// O espaco nao tem ceu (`Ceu.SemCeu`: "crepusculo parado"), e o ambiente dele -- o `CanvasModulate` da
/// `Iluminacao` -- multiplica a cena por um malva de mais ou menos 0,51 / 0,33 / 0,39. Por ele o laranja da folha
/// (`e08020`) chegaria a tela como um marrom de 0,45 / 0,17 / 0,05 sobre o ceu preto, com a legenda apagada
/// junto: desenhada, e ainda assim apagada (a foto `desejos-07-sem-luz-propria` da `--diagdesejos` e esse
/// estado, pelo botao <see cref="SobOAmbienteDeTeste"/>). O pedido e que elas APARECAM, e os ultimos passos ate
/// uma se dao olhando pra tela: ela sai na cor em que foi pintada -- o material do node e `Unshaded`, e o disco e
/// a legenda o herdam. Os planetas e os corpos continuam sob o crepusculo: nao sao assunto daqui.
/// ====================================================================================================
///
/// O QUE MAIS ELA DIZ: quantas estrelas (a arte ja mostra; a legenda repete por extenso) e DE QUEM E -- "SUA",
/// "de Fulano" ou "sem dono" --, com o que o pacote ja traz. Sem isso so o placar da aba World respondia, e ele
/// nao diz qual das sete esta na sua frente.
///
/// O CUSTO: nasce quando o pacote chega (`World.DesenharSupers`) e nao faz nada por quadro -- nem `_Process`, nem
/// redesenho. Sao no maximo sete, e quase sempre nenhuma.
/// </summary>
public partial class SuperEsferaDesenhada : Node2D
{
	private const string Folha = "res://Assets/Sprites/SuperDragonball.tres";

	/// <summary>
	/// O raio do disco, em pixels de mundo: o alcance de reivindicar (e de invocar). Ver o cabecalho.
	/// </summary>
	public const float Raio = SuperEsferas.AlcanceDeReivindicar;

	/// <summary>O tamanho da letra da legenda, em pixels de mundo (a camera a amplia junto com o resto).</summary>
	private const int LetraDaLegenda = 10;

	/// <summary>A altura da caixa da legenda, em pixels de mundo: uma linha da letra de cima, com o contorno.</summary>
	private const float AlturaDaLegenda = 16f;

	/// <summary>Quanto a legenda fica acima do anel do alcance, em pixels de mundo (o halo passa 6 px do raio).</summary>
	private const float FolgaDaLegenda = 8f;

	/// <summary>1 a 7: o numero de estrelas. Escolhe o quadro da folha.</summary>
	public int Numero = 1;

	/// <summary>O nome de quem a reivindicou, ou vazio (sem dono). Vem do pacote.</summary>
	public string Dono = "";

	/// <summary>E deste jogador (uma das "minhas" do placar)? Vem do pacote.</summary>
	public bool Minha;

	private Sprite2D? _disco;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagdesejos`): a esfera SEM luz propria -- desenhada sob o crepusculo do
	/// espaco, como os planetas. Falso em jogo, sempre.
	/// </summary>
	public static bool SobOAmbienteDeTeste;

	/// <summary>
	/// A LEGENDA DE UMA SUPER ESFERA: as estrelas e o dono. Pura, pra bancada cobrar o texto sem tela.
	/// </summary>
	public static string Legenda(int numero, string dono, bool minha) =>
		(numero == 1 ? "1 estrela" : $"{numero} estrelas")
		+ " · " + (minha ? "SUA" : dono.Length > 0 ? $"de {dono}" : "sem dono");

	public override void _Ready()
	{
		// NA FRENTE DOS PLANETAS (-60) E DAS ESTRELAS (-80), ATRAS DOS CORPOS (0): quem reivindica fica EM CIMA
		// do disco, e tem que continuar aparecendo.
		ZIndex = -40;

		// LUZ PROPRIA (ver o cabecalho): fora do ambiente do espaco. O material e do NODE -- vale pro anel do
		// `_Draw` -- e os dois filhos o herdam (`UseParentMaterial`). E o gerado do motor, o mesmo do halo do
		// ar divino (`PoeiraDivina`): nao ha shader do projeto aqui.
		if (!SobOAmbienteDeTeste)
			Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };

		Texture2D? quadro = FolhasPresas.Carregar(Folha) is { } folha && folha.HasAnimation(Numero.ToString())
			? folha.GetFrameTexture(Numero.ToString(), 0)
			: null;

		if (quadro != null)
		{
			_disco = new Sprite2D
			{
				Name = "Disco",
				Texture = quadro,
				// PELO DIAMETRO, como o planeta: o quadro tem 64 px e o disco tem dois raios.
				Scale = Vector2.One * (Raio * 2f / quadro.GetWidth()),
				TextureFilter = TextureFilterEnum.Nearest,
				UseParentMaterial = true,
			};
			AddChild(_disco);
		}
		else if (_jaReclamou.Add(Numero))
		{
			// SEM ARTE, UM DISCO DOURADO -- e nao nada (ver `_Draw`). Uma Super Esfera invisivel e o defeito
			// que este arquivo veio consertar.
			GD.PushError($"[super] sem desenho pra Super Esfera #{Numero} ('{Folha}') -- vai aparecer como disco dourado.");
		}

		Label legenda = Tema.Legenda(Legenda(Numero, Dono, Minha),
									 Minha ? Tema.Destaque : Dono.Length > 0 ? Tema.Texto : Tema.TextoFraco, LetraDaLegenda);
		legenda.Name = "Legenda";
		// EM CIMA DA BOLA, como o nome da estrela (`EstrelaDesenhada`), e nao embaixo: quem esta ao lado dela tem a
		// bola no meio da tela, e embaixo do meio da tela mora a caixa de fala -- a legenda ficava atras dela (as
		// duas primeiras fotos da `--diagdesejos` sairam assim).
		legenda.Position = new Vector2(-120, -Raio - AlturaDaLegenda - FolgaDaLegenda);
		legenda.Size = new Vector2(240, AlturaDaLegenda);
		legenda.VerticalAlignment = VerticalAlignment.Bottom;
		legenda.HorizontalAlignment = HorizontalAlignment.Center;
		legenda.UseParentMaterial = true;
		AddChild(legenda);
	}

	/// <summary>Os numeros que ja reclamaram da arte nesta sessao: um erro por numero, e nao um por pacote.</summary>
	private static readonly HashSet<int> _jaReclamou = [];

	/// <summary>
	/// O HALO -- e, sem a arte, o proprio disco. Desenhado UMA vez (nada aqui pede redesenho): um anel dourado
	/// fino na borda do alcance, pra a bola nao se perder no ceu estrelado e pra a borda dela -- que e onde o
	/// corpo tem que encostar -- ficar nitida mesmo sobre o brilho de uma estrela ao fundo.
	/// </summary>
	public override void _Draw()
	{
		var ouro = new Color(1f, 0.82f, 0.30f);
		if (_disco == null) DrawCircle(Vector2.Zero, Raio, ouro with { A = 0.90f });
		DrawCircle(Vector2.Zero, Raio + 6f, ouro with { A = 0.10f });
		DrawArc(Vector2.Zero, Raio + 1f, 0, Mathf.Tau, 96, ouro with { A = 0.45f }, 1.5f);
	}

	// =====================================================================
	// O QUE A BANCADA LE -- o que esta DESENHADO, e nao o que o pacote disse
	// =====================================================================
	/// <summary>
	/// Quantas estrelas tem o QUADRO que esta desenhado (1 a 7), lido do recorte da folha -- e nao do campo
	/// <see cref="Numero"/>, que e o que se pediu. Zero sem arte.
	/// </summary>
	public int EstrelasDesenhadas =>
		_disco?.Texture is AtlasTexture { Region: var r } && r.Size.X > 0 ? (int)(r.Position.X / r.Size.X) + 1 : 0;

	/// <summary>O diametro do disco como esta desenhado, em pixels de mundo.</summary>
	public float DiametroDesenhado => _disco is { Texture: { } t } ? t.GetWidth() * _disco.Scale.X : Raio * 2f;

	/// <summary>A legenda como esta escrita.</summary>
	public string LegendaDesenhada => GetNodeOrNull<Label>("Legenda")?.Text ?? "";
}
