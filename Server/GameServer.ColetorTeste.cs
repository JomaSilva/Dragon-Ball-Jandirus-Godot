namespace Jandirus.Server;

/// <summary>
/// O QUE A BANCADA DO COLETOR (`--diagcoletor`, `Client/RoboDoColetor.cs`) PRECISA LER DO SERVIDOR.
///
/// Ela mede os bytes que o `_Process` do servidor aloca por quadro, de fora, com uma marca antes e outra depois
/// do node. Pra dizer isso POR TIQUE e POR CORPO -- as duas unidades em que o custo cresce -- faltavam dois
/// numeros que so o servidor tem: quantos tiques chegaram ao fim e quantos corpos cada tique varre. Dividir por
/// trinta tiques por segundo seria supor que o acumulador nunca atrasa, e e justamente num quadro travado pelo
/// coletor que ele atrasa.
/// </summary>
public partial class GameServer
{
	/// <summary>Quantos corpos o tique varre (`_players`, gente e NPC) e quantos tiques chegaram ao fim. So bancada.</summary>
	internal (int Corpos, long Tiques) ContagemDoColetorDeTeste => (_players.Count, _quadrosInteiros);
}
