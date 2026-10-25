using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// O SUPER MAJIN NO SERVIDOR (dono, 2026-10-23: *"dar a forma super majin melhorada pros majins q nao sao corrupted"*). A forma e uma
/// entrada do catalogo como outra qualquer -- a tecla C, a tecla de forma, a cena, o dreno e o multiplicador ja passam pelos funis de
/// sempre. O que e SO dela e a maestria: dez vezes mais lenta e travada pelo BP base de agora. A regra e os numeros moram no Core
/// (`Core/Forms/SuperMajin.cs`); aqui so se poe o corpo (o BP base e as portas dele) na pergunta, dentro do funil unico da maestria por
/// tempo (`SubirMaestriaDaZona`, em `GameServer.Formas.cs`).
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// ============================ O `Maestria.Subir` DO FUNIL, COM A TRAVA DO SUPER MAJIN ============================
	/// Pra toda outra forma e o `Subir` de sempre, com o mesmo ganho. No Super Majin o ganho sai mais lento e nao passa do teto que o BP
	/// base segura agora (`SuperMajin.TetoDaMaestria`). No tique em que a barra ENCOSTA num teto abaixo de 100% ela e CRAVADA nele (ver
	/// `SuperMajin.Ganho`) e o funil devolve um marco -- o `TickDaForma` o diz ao jogador do mesmo jeito que diz "forma DOMINADA" ("Super
	/// Majin: a maestria parou em 30%, ..."). Encostada, ela nao sobe mais (ganho zero, sem marco): o aviso sai uma vez por teto.
	///
	/// A CONTA DO TETO SO RODA NESTA FORMA: o funil e chamado a cada tique de cada corpo transformado, e as portas de referencia sao duas
	/// buscas no catalogo.
	/// </summary>
	private static bool SubirPeloPoder(ServerPlayer pl, string id, double ganho, out string marco)
	{
		if (id != SuperMajin.Id) return pl.Forma.Maestria.Subir(id, ganho, out marco);

		double teto = SuperMajin.TetoDaMaestria(pl.Ficha.BP, pl.Forma);
		(double lento, bool noTeto) = SuperMajin.Ganho(id, pl.Forma.Maestria.De(id), ganho, teto);
		if (!noTeto) return pl.Forma.Maestria.Subir(id, lento, out marco);

		pl.Forma.Maestria.Por(id, teto);
		marco = SuperMajin.MarcoDoTeto(teto);
		return true;
	}
}
