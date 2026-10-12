using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O HALO DOURADO DE UM MUNDO DE ATMOSFERA DIVINA, visto do espaco -- o toque visual do pedido do dono
/// (2026-10-10): quem voa ate um planeta raro o reconhece antes de pousar, pelo mesmo dourado que o marca
/// na carta estelar (`MapaEstelar.DesenharPlaneta`).
///
/// E UM `Sprite2D` COM A RADIAL QUE AS LUZES JA USAM (`Fogo.Radial`), tingido e posto atras do disco: um
/// clarao macio que sobra em volta do planeta. Um planeta comum nao ganha node nenhum.
///
/// ============================ ELE E LUZ: SOMA NA CENA E NAO PEGA O AMBIENTE ============================
/// A primeira versao era um sprite comum, sem material. Fotografada em orbita (2026-10-11) ela saiu
/// VERMELHO-BRASA: o espaco tem ambiente (o crepusculo parado, forca da noite 0,66) e ele multiplica tudo o
/// que nao e `unshaded` -- e um clarao avermelhado em volta de um planeta, nesta casa, quer dizer planeta
/// MORRENDO (o magma do `PlanetaMorrendo.gdshader`). Dourado tem de chegar dourado na tela.
///
/// O MATERIAL E O DAS FAISCAS DO ESTOURO DE KI (`EstouroDeKi`: aditivo e sem luz) -- a mesma chave de
/// `CanvasItemMaterial`, entao o mesmo shader gerado pelo motor, e nao um `.gdshader` novo do projeto. O que
/// NAO foi medido: o quadro em que o primeiro mundo divino do processo entra na tela (a `--diagestouro` e
/// quem mede isso, e este halo nao tem ato no ensaio do lobby).
/// ====================================================================================================
///
/// QUEM DECIDE E QUEM CRIA O NODE (o `World`, ao montar a vizinhanca), e nao este arquivo pela seed: os
/// planetas de palco das bancadas (`RoboDaAgonia`) nascem com a seed que quiserem e continuam sem halo.
///
/// (2026-10-12: ele saiu por algumas horas -- o pedido do dono de tirar *"o circulo/brilho q fica em volta do
/// personagem em planetas com atmosfera divina"* foi lido como sendo este clarao -- e voltou no mesmo dia, pela
/// palavra dele: *"o halo no planeta de god ki em si vc pode deixar"*. O que entrou por aquele pedido e ficou sao os
/// pontinhos brancos da superficie, `PoeiraDivina`.)
/// </summary>
public partial class PlanetaDesenhado
{
	/// <summary>Este mundo tem atmosfera divina. Escrito por quem cria o node, antes de ele entrar na arvore.</summary>
	public bool Divino;

	/// <summary>O nome do node do halo. Uma casa so: a bancada (`--diaggodki`) procura por ele.</summary>
	public const string NomeDoHalo = "HaloDivino";

	/// <summary>Ate onde o clarao alcanca, em raios do planeta. Na borda do disco ele ainda tem ~1/3 da forca.</summary>
	private const float AlcanceDoHalo = 1.55f;

	/// <summary>A forca do clarao no centro (ele cai em linha reta ate zero no <see cref="AlcanceDoHalo"/>). Discreto: e um sinal, nao um sol.</summary>
	private const float ForcaDoHalo = 0.8f;

	/// <summary>O nome do quad do estouro (`NovoEstouro`, no arquivo do disco). E por ele entrar na arvore que o halo sabe que o planeta estourou.</summary>
	private const string NomeDoEstouro = "Estouro";

	/// <summary>Em que fracao do tempo do estouro o DISCO se apaga (o `0.55` do `Estourar`). O halo usa a mesma, pra sumirem juntos.</summary>
	private const double FracaoEmQueODiscoSome = 0.55;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diaggodki): o halo nao acompanha o disco no estouro -- o planeta some e o
	/// clarao dourado fica aceso em volta de nada ate o node ser liberado. Falso em jogo, sempre.
	/// </summary>
	public static bool HaloFicaNoEstouroDeTeste;

	/// <summary>
	/// A PERGUNTA, pra quem monta a vizinhanca: funcao pura da seed do mundo e da do universo, a mesma do
	/// servidor (`Core.World.AtmosferaDivina`). Mundo com mapa proprio nunca e (o Reino dos Deuses nao tem disco).
	/// </summary>
	public static bool TemAtmosferaDivina(bool premade, ulong seedDoMundo) =>
		!premade && GameClient.Instance is { SeedDoUniverso: not 0UL } cli
		&& AtmosferaDivina.DoMundo(cli.SeedDoUniverso, seedDoMundo, RegraDaAtmosfera.Hoje);

	/// <summary>O halo entra ANTES do icone (que nasce no `_Ready`), e um degrau de Z abaixo dele.</summary>
	public override void _EnterTree()
	{
		if (!Divino || HasNode(NomeDoHalo)) return;
		AddChild(new Sprite2D
		{
			Name = NomeDoHalo,
			Texture = Fogo.Radial(LuzDeKi.RaioDaTextura),
			Scale = Vector2.One * (Raio * AlcanceDoHalo / LuzDeKi.RaioDaTextura),
			Modulate = new Color(MenuJogo.OuroDivino, ForcaDoHalo),
			ZIndex = -1,
			Material = new CanvasItemMaterial
			{
				LightMode = CanvasItemMaterial.LightModeEnum.Unshaded,
				BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
			},
		});
		ChildEnteredTree += AoNascerUmFilho;   // uma vez so: a linha de cima barra a segunda entrada na arvore
	}

	/// <summary>
	/// O HALO SOME COM O DISCO. Quando um planeta estoura, o disco se apaga por baixo do clarao e o node so se
	/// libera no fim do efeito (`Estourar` e `AplicarAgonia`): sem isto o halo ficaria aceso em volta de
	/// planeta nenhum pelo ultimo segundo -- um mundo divino pode ser destruido como qualquer outro.
	///
	/// O ESTOURO E RECONHECIDO PELO QUAD DELE ENTRANDO NA ARVORE, pra a regra morar aqui: o `Estourar` nao
	/// precisa saber que halo existe. Se alguem renomear o quad, quem avisa e a familia F6 da `--diaggodki`.
	/// </summary>
	private void AoNascerUmFilho(Node filho)
	{
		if (HaloFicaNoEstouroDeTeste || filho.Name.ToString() != NomeDoEstouro) return;
		if (GetNodeOrNull<Sprite2D>(NomeDoHalo) is not { } halo) return;
		halo.CreateTween().TweenProperty(halo, "modulate:a", 0f, MortePlanetaria.SegundosDoEstouro * FracaoEmQueODiscoSome);
	}
}
