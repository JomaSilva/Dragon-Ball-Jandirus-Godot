using Godot;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// **O VIGOR DO TREINO, DITO AO JOGADOR** -- o relato dos jogadores de 2026-10-23: *"bp congela do nada e os ganhos ficam mt baixos"*.
///
/// ============================ A CAUSA (investigacao com a DLL da v0.4.4) ============================
/// Nao havia teto. O T gasta 0,5% do vigor por segundo (`Cansaco.VigorDoTreinoPorSegundo`) e a digestao devolve 0,5% a cada 3 s
/// (`Nutricao.PingoDeVigorPorPassada`): em ~4,6 min o servidor cortava o treino ("você está exausto"), e o vigor so voltava comendo --
/// ~9,5 min parado pra encher de novo, meditando ou nao. SEM COMIDA o `Digerir` sai na entrada e o vigor nunca mais volta, com UM aviso
/// de fome so (a bandeira `Hungry`). E quem apertava T exausto era ACEITO e derrubado 200 ms depois, sem saber por que nem quanto
/// faltava. Por cima, o peso cobrava o vigor pelo QUADRADO (o ganho, so linear) e a janela do tiro de ki nunca fechava -- os dois
/// consertados no Core (`Cansaco.VigorDoTreino`, `Fighter.JanelaDoTiro.cs`).
/// =====================================================================================================
///
/// O DONO (2026-10-24): *"treino para sem vigor"* (continua parando) e *"Avisar e mostrar no HUD"*. Aqui moram as duas portas do pedido;
/// as frases sao do `Cansaco` (o servidor e as bancadas leem as mesmas), e o HUD tira "EXAUSTO"/"SEM COMIDA" da propria ficha pela mesma
/// conta (`Client/Hud.Avisos.cs`). A bancada e a `--vigortreinoteste` (`GameServer.VigorTreinoTeste.cs`).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--vigortreinoteste`): o pedido de treinar exausto volta a PASSAR -- o treino liga e cai no primeiro passe,
	/// 200 ms depois, o jogo de antes de 2026-10-24. Lido por <see cref="PedidoDeTreinoAguenta"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool TExaustoPassaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--vigortreinoteste`): a tentativa de treinar com o estomago vazio volta a nao dizer nada -- o jogo de
	/// antes de 2026-10-24, so com o "seu estomago ronca" de uma vez. Lido por <see cref="PedidoDeTreinoAguenta"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool EstomagoVazioCaladoDeTeste;

	/// <summary>
	/// O PEDIDO DE TREINAR (o T) PASSA PELO VIGOR? Chamado pelo `AceitarTreinoOuMeditacao`, DEPOIS das recusas de sempre (no ar, caido,
	/// na briga...): elas dizem o que fazer agora, e esta diz quanto esperar.
	///
	///   * EXAUSTO (o vigor nao paga nem um passe -- a mesma pergunta do corte, `Cansaco.AguentaTreinar`): recusado JA NO PEDIDO, com
	///     quanto falta de descanso e que e a comida que recupera (`Cansaco.FraseDoTreinoRecusado`); o cliente larga a pose, como em toda
	///     recusa. Antes o pedido passava e o treino caia no primeiro passe.
	///   * COM O ESTOMAGO VAZIO: o aviso de que sem comida o vigor nao volta, A CADA TENTATIVA -- na recusa ele vem dentro da frase; aceito,
	///     numa linha propria. Quem tem o vigor travado no cheio (o Majin, o androide de energia infinita, o morto) nao ouve: pra ele comida
	///     nao conta.
	/// </summary>
	private bool PedidoDeTreinoAguenta(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		bool aguenta = Cansaco.TreinoNaoCansaDeTeste || Cansaco.AguentaTreinar(f, SegundosPorFicha);
		if (!aguenta && !TExaustoPassaDeTeste)
		{
			double falta = Cansaco.SegundosAteAguentarTreinar(f, SegundosPorFicha);
			GD.Print($"[server] {pl.Name}: o pedido de treinar foi recusado (exausto: vigor {f.stamina / Math.Max(f.maxstamina, 1e-9):P1}, "
					 + $"nutricao {f.CurrentNutrition:0.##}, falta {(double.IsFinite(falta) ? $"{falta:0.#} s" : "sem fim")})");
			Avisar(pl, Cansaco.FraseDoTreinoRecusado(falta));
			MandarEfeito(pl, Jandirus.Core.Stats.TreinoEMeditacao.EfeitoDeQueCaiu, 0);
			return false;
		}
		if (f.CurrentNutrition <= 0 && !Nutricao.VigorSempreCheio(f) && !EstomagoVazioCaladoDeTeste)
			Avisar(pl, Cansaco.AvisoDoEstomagoVazio);
		return true;
	}
}
