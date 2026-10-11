namespace Jandirus.Core.Stats;

/// <summary>
/// O QUE A HISTORIA DO BILLS DEIXA NA FICHA de um personagem (dono, 2026-10-11; ver `Npc.BillsEWhis`). Dois
/// "ja aconteceu", e mais nada: campo publico da ficha vai pro save sozinho, e ficha antiga le `false`.
/// </summary>
public partial class Fighter
{
	/// <summary>
	/// ESTE PERSONAGEM JA RECEBEU O TRAJE do Deus da Destruicao. O dono: "players q virarem deus da destruicao tb vao
	/// ganhar ela de graca" -- UMA vez por personagem, na primeira vez em que o titulo e dele (seja qual for o
	/// caminho: duelo, admin, vencer o Bills, ou ja ser o Deus no dia em que isto entrou no jogo). A peca fica com
	/// ele depois que o titulo passa: e roupa, nao poder. Ver `GameServer.ReconciliarOTrajeDoDeus`.
	/// </summary>
	public bool TrajeDoDeusRecebido;

	/// <summary>
	/// A semente do planeta que este personagem ja ouviu ser a casa do Bills (0 = nunca ouviu). E o que faz a
	/// noticia das coordenadas ser dita UMA vez a quem nao estava la -- e de novo se a casa mudar de lugar.
	/// </summary>
	public ulong CasaDeBillsOuvida;
}
