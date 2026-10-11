namespace Jandirus.Core.Stats;

/// <summary>
/// AS MELHORIAS DAS DRILLS QUE ESTE PERSONAGEM RECOLHEU -- ver <see cref="Jandirus.Core.Tech.Drill"/> e
/// `GameServer.Drill.cs`.
/// </summary>
public partial class Fighter
{
	/// <summary>
	/// OS NIVEIS DAS DRILLS MELHORADAS QUE ELE GUARDOU NA MOCHILA, um por Drill recolhida (so as acima do
	/// nivel 1). A proxima Drill que ele assentar volta no maior deles, e a entrada sai da lista.
	///
	/// ============================ POR QUE ISTO MORA NA FICHA ============================
	/// Uma obra no chao tem identidade (`Obra.Id`, gravada no `mundo.json`); o item na mochila nao tem -- e
	/// uma pilha de "Power_Drill", igual a qualquer outra. Sem esta lista, recolher uma Drill de nivel 10
	/// (mais de um milhao de zeni investidos) pra leva-la a outro planeta a devolveria de fabrica, e o jogo
	/// nao teria como avisar ANTES do clique: o menu da tecla E nao tem confirmacao.
	///
	/// Entao a melhoria fica com QUEM A PAGOU. Campo novo em `Fighter`, serializado inteiro no save: quem
	/// nunca recolheu uma Drill carrega a lista vazia.
	/// ====================================================================================
	/// </summary>
	public List<int> DrillsRecolhidas = [];
}
