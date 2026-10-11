using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// AS SUPER ESFERAS NA TELA DO MUNDO -- a lista que o servidor manda (`GameClient.Supers`) vira nodes no espaco.
/// O porque, a arte e o tamanho estao em <see cref="SuperEsferaDesenhada"/>.
///
/// ============================ SO O QUE O SERVIDOR MANDOU ============================
/// A posicao das sete e segredo (e funcao da semente e do CICLO, e o ciclo nao viaja -- ver `Protocol.S2C.
/// SuperEsferas`). O cliente desenha as que vieram no pacote, que sao as do recorte de chunk em que o corpo esta,
/// e mais nenhuma: nao ha seta, nem marca fora da tela, nem nada que faca o papel do Dragon Radar. Quem acha a
/// celula e o radar; a bola na tela e o que deixa chegar os ultimos passos olhando pra ela.
/// ====================================================================================
///
/// MESMO JEITO DOS PLANETAS E DAS ESFERAS DO CHAO (`DesenharPlanetas`, `DesenharEsferas`): a lista inteira se
/// refaz quando o pacote chega (`SupersMudaram`) e quando o mundo troca de zona -- nunca por quadro.
/// </summary>
public partial class World
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagdesejos`): as Super Esferas nao sao desenhadas -- o que este cliente fez ate
	/// 2026-10-11 (a lista chegava e ninguem a lia). Falso em jogo, sempre.
	/// </summary>
	public static bool SuperEsferasSemDesenhoDeTeste;

	/// <summary>
	/// O pai das Super Esferas desenhadas. Proprio, e nao o dos planetas: o `DesenharPlanetas` esvazia o node dele
	/// a cada troca de chunk, e levaria as esferas junto (a vizinhanca e as Super chegam em pacotes separados).
	/// </summary>
	private Node2D? _superEsferas;

	/// <summary>
	/// REFAZ AS SUPER ESFERAS DESENHADAS a partir da lista do servidor. Fora do espaco nao sobra nenhuma: elas so
	/// existem la, e a lista de quem acabou de pousar e a do lugar de onde veio.
	/// </summary>
	private void DesenharSupers()
	{
		if (_superEsferas == null)
		{
			_superEsferas = new Node2D { Name = "SuperEsferas" };
			AddChild(_superEsferas);
		}

		// TIRAR DO PAI ANTES DE SOLTAR: um node na fila de saida ainda ocupa o nome, e o irmao novo nasceria como
		// "@Super1@2" -- a bancada (e quem depurar) acha a esfera pelo nome.
		foreach (Node n in _superEsferas.GetChildren())
		{
			_superEsferas.RemoveChild(n);
			n.QueueFree();
		}

		if (SuperEsferasSemDesenhoDeTeste) return;
		if (GameClient.Instance is not { } cli || !Espaco.EhEspaco(cli.Zone)) return;

		foreach (GameClient.SuperInfo s in cli.Supers)
			_superEsferas.AddChild(new SuperEsferaDesenhada
			{
				Name = "Super" + s.Numero,
				Position = s.Pos,
				Numero = s.Numero,
				Dono = s.Dono,
				Minha = s.Minha,
			});
	}

	// =====================================================================
	// O QUE A BANCADA LE -- os nodes desenhados, e nao a lista que chegou
	// =====================================================================
	/// <summary>
	/// As Super Esferas como estao DESENHADAS agora: o numero, onde no mundo, onde na TELA (pixels da janela, ja
	/// com a camera), o diametro em pixels de mundo, quantas estrelas tem o quadro desenhado (0 = sem arte), e a
	/// legenda escrita.
	/// </summary>
	public List<(int Numero, Vector2 NoMundo, Vector2 NaTela, float Diametro, int Estrelas, string Legenda)> SuperEsferasDesenhadasDeTeste() =>
		_superEsferas == null ? []
		: [.. _superEsferas.GetChildren().OfType<SuperEsferaDesenhada>().Where(e => !e.IsQueuedForDeletion())
			.Select(e => (e.Numero, e.GlobalPosition, e.GetGlobalTransformWithCanvas().Origin, e.DiametroDesenhado, e.EstrelasDesenhadas, e.LegendaDesenhada))];

	/// <summary>
	/// QUEM sao os nodes desenhados agora (a identidade de cada um, na ordem). E a regua do custo: entre dois
	/// pacotes eles tem que ser os MESMOS -- um desenho que se remonta por quadro troca todos a cada leitura.
	/// </summary>
	public ulong[] NodesDasSupersDeTeste() =>
		_superEsferas == null ? []
		: [.. _superEsferas.GetChildren().OfType<SuperEsferaDesenhada>().Where(e => !e.IsQueuedForDeletion()).Select(e => e.GetInstanceId())];

	/// <summary>
	/// REFAZ O DESENHO AGORA, com a lista que o cliente ja tem. So pra bancada, depois de mexer no botao de
	/// defeito: em jogo quem chama e o pacote.
	/// </summary>
	public void RedesenharSupersDeTeste() => DesenharSupers();
}
