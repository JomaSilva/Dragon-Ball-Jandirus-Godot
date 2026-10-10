using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O CAMPO DA MAQUINA DE GRAVIDADE, NA TELA -- um FILTRO VERMELHO por cima de tudo o que esta nele.
///
/// ============================ E O `Gravity Field.dmi` DO ORIGINAL ============================
/// O dono (2026-10-09): *"a area afetada pela gravidade da maquina fica vermelha, um filtro de cor em cima
/// de tudo q ta sobre a grav artificial deixando tudo vermelho"*. E o que o DM faz: ao ligar, a maquina
/// ganha um overlay `image('Gravity Field.dmi')` esticado pelo alcance (`Gravity.dm:316-318`). O icone e
/// um quadrado liso de (255, 51, 51) com 59/255 de opacidade, e ele herda o plano da maquina
/// (`plane=MOB_LAYER+5`, `:167`) -- ACIMA de todo corpo. Chao, mobilia e gente ficam avermelhados.
///
/// O port nao desenhava nada: o campo era uma coisa que so se descobria pisando. A primeira tentativa
/// daqui foi uma tinta roxa e rala no CHAO, por baixo dos corpos -- e o dono corrigiu na hora.
///
/// A COR E A OPACIDADE SAO AS DO ICONE. O que nao e igual e a FORMA: la e um quadrado esticado, e aqui o
/// campo deixou de ser um quadrado -- ele se molda ao lugar, *"igual um liquido"* (a outra regra do dono,
/// ver `CampoDeGravidade`). O filtro e pintado tile a tile sobre o que o campo alcancou: numa sala ele tem
/// a forma da sala, e a forma muda quando uma parede sobe ou cai.
/// ============================================================================================
///
/// QUEM DECIDE QUEM PESA E O SERVIDOR. Aqui so se pinta o que ele mandou (`S2C.CamposDeGravidade`): cada
/// tile que o liquido alcancou, aparado pela caixa do alcance -- as duas metades de
/// `CampoDeGravidade.Alcanca`, a mesma conta que la decide. Num alcance impar a fileira de fora sai
/// pintada pela metade, e e isso mesmo: o campo acaba no meio do tile.
///
/// UM NODE SO, que le a lista do cliente: nao ha o que plantar por zona nem o que esquecer de limpar --
/// o servidor manda a lista da zona em que se entra, vazia quando nao ha maquina ligada. E SO REDESENHA
/// QUANDO A LISTA MUDA: o filtro do original e parado.
/// </summary>
public partial class CamposDeGravidadeNaTela : Node2D
{
	/// <summary>A cor do `Gravity Field.dmi`: (255, 51, 51), com 59 de 255 de opacidade.</summary>
	public static readonly Color Filtro = new(1f, 0.2f, 0.2f, 59f / 255f);

	/// <summary>
	/// O PLANO DO FILTRO: acima dos corpos (0), da mobilia e das copas (1) -- o `plane=MOB_LAYER+5` de la --,
	/// e abaixo do que e AVISO e nao coisa do lugar: o balao de fala (21), as faiscas de golpe (28 a 31), a
	/// explosao (40), o fantasma de construir (50) e o veu do que nao se ve (90), que esconde o campo junto
	/// com o resto.
	/// </summary>
	public const int Plano = 20;

	private readonly List<Rect2> _ladrilhos = [];
	private int _versao = -1;

	/// <summary>Quantos ladrilhos estao pintados agora. So pra bancada (`--diagmaquinas`).</summary>
	public int LadrilhosDeTeste => _ladrilhos.Count;

	public override void _Process(double delta)
	{
		if (GameClient.Instance is not { } cli || _versao == cli.VersaoDosCampos) return;

		_versao = cli.VersaoDosCampos;
		Remontar(cli.CamposDeGravidade);
		QueueRedraw();
	}

	/// <summary>A lista vira ladrilhos uma vez, quando chega: cada tile do campo, aparado pela caixa do alcance.</summary>
	private void Remontar(List<GameClient.CampoDeGravidadeVisto> campos)
	{
		_ladrilhos.Clear();
		const int t = ZoneCollision.TileSize;

		foreach (GameClient.CampoDeGravidadeVisto campo in campos)
		{
			float meio = CampoDeGravidade.MeioLado(campo.Alcance);
			Vector2 centro = new((campo.Maquina.X + 0.5f) * t, (campo.Maquina.Y + 0.5f) * t);
			Rect2 caixa = new(centro - new Vector2(meio, meio), new Vector2(meio * 2, meio * 2));

			foreach (Vector2I tile in campo.Tiles)
			{
				Rect2 area = new Rect2(tile.X * t, tile.Y * t, t, t).Intersection(caixa);
				if (area.Size.X > 0 && area.Size.Y > 0) _ladrilhos.Add(area);
			}
		}
	}

	/// <summary>
	/// DOIS CAMPOS SOBREPOSTOS PINTAM DUAS VEZES, e o trecho em comum fica mais vermelho -- como dois overlays
	/// do original, e como a propria gravidade, que la e aqui se SOMA.
	/// </summary>
	public override void _Draw()
	{
		foreach (Rect2 ladrilho in _ladrilhos) DrawRect(ladrilho, Filtro);
	}
}
