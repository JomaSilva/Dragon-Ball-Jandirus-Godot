using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// O BILLS E O WHIS COMO ALVO DA TECLA E -- "eles ficam parados la como um objeto q da pra interagir" (dono, 2026-10-11).
///
/// Eles sao CORPOS, e nao construcoes: nao estao em `cli.Obras`, que e onde o menu procura alvo. Entram pela porta
/// da rocha de minerio (`RochasNaTela.Disputar`): uma `ObraInfo` so com tipo, nome e posicao, e dali pra frente o
/// caminho e o de qualquer objeto -- a dica "[E] Whis", o titulo e os botoes saem do `Interacoes`.
///
/// QUEM DIZ QUAL CORPO TEM MENU E O SERVIDOR (`S2C.DivinoPerto`), como diz qual cadaver esta ao alcance: o cliente
/// nao sabe se aquele corpo e o Whis parado em casa, o Whis no meio do treino de outra pessoa ou o Bills cochilando
/// na Terra (que nao tem menu nenhum). A POSICAO sai do mundo desenhado, e nao do pacote, pelo motivo do cadaver:
/// o pacote diz QUAL corpo, e o corpo ja esta na tela. Sem ele na tela, nao ha alvo.
///
/// NO MEIO DE UMA LUTA COM UM DELES O MENU E A SAIDA, e ela ganha de tudo, a qualquer distancia -- pelo motivo do
/// veiculo montado (`MenuDeInteracao.Abrir`): nao e prioridade inventada, e que a luta esta EM CIMA de voce. O
/// mestre tambem luta de longe, e quem quer dizer "chega" nao pode depender de encostar nele (nem de nao haver
/// uma macieira mais perto). Ver `GameServer.DivinoAoAlcance`.
///
/// E O BOTAO SO PEDE: lugar, distancia e estado sao conferidos de novo no servidor, na hora do verbo
/// (`GameServer.PorQueNaoPede`).
/// </summary>
public static class DivinosNaTela
{
	/// <summary>O corpo divino ao alcance disputa a tecla E com a mobilia por perto, pela regra de sempre: o mais perto ganha.</summary>
	public static void Disputar(Vector2 eu, ref GameClient.ObraInfo? melhor, ref float melhorDist)
	{
		if (GameClient.Instance is not { DivinoPerto: not 0 } cli) return;
		if (!Interacoes.Interativo(cli.TipoDoDivinoPerto)) return;

		// A SAIDA DA LUTA: distancia zero (o alvo e posto em mim), e por isso ela ganha de qualquer coisa por perto.
		if (cli.TipoDoDivinoPerto is BillsEWhis.TipoEmTreino or BillsEWhis.TipoEmDesafio)
		{
			melhorDist = 0;
			melhor = new GameClient.ObraInfo(0, cli.TipoDoDivinoPerto, cli.NomeDoDivinoPerto, eu,
											 Aparafusada: true, Lab: 0, Dono: "", Arte: "", Estado: "",
											 Pixel: Vector2.Zero, Densa: true);
			return;
		}

		if (World.Instancia?.PosicaoDesenhadaDe(cli.DivinoPerto) is not { } corpo) return;

		// O MESMO CORTE POR EIXO do servidor (`AoAlcanceDaTeclaE`): com raio, o menu abriria onde ele recusaria.
		if (Math.Abs(corpo.X - eu.X) > Interacoes.Alcance || Math.Abs(corpo.Y - eu.Y) > Interacoes.Alcance) return;

		float d = eu.DistanceSquaredTo(corpo);
		if (d >= melhorDist) return;
		melhorDist = d;
		melhor = new GameClient.ObraInfo(0, cli.TipoDoDivinoPerto, cli.NomeDoDivinoPerto, corpo,
										 Aparafusada: true, Lab: 0, Dono: "", Arte: "", Estado: "",
										 Pixel: Vector2.Zero, Densa: true);
	}
}
