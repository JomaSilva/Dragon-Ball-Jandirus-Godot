using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// QUEM SO A GRAVIDADE SEGURA SE ARRASTA (dono, 2026-10-23): *"personagem ao cair na gravidade, faça com q personagem ai cair por
/// gravidade ainda consiga se mover lentamente se arrastando"*. Perguntado, ele escolheu: 1/4 da velocidade a pe, nos DOIS estados --
///   * quem DESMAIOU pelo esmagamento (a gravidade, ou o peso, alem do que o corpo aguenta) se arrasta DEITADO pra sair do campo -- e
///     continua se arrastando fora dele, ate levantar (`CombatState.DesmaioDaGravidade`, decisao minha: o desmaio e o mesmo);
///   * quem esta PRESO DE PE pela razao >= 4 (`Esmagamento.Prende`, o `gravParalysis` do DM) anda, com o andar de sempre.
/// Nos dois, SO O PASSO: nao corre, nao voa, e nao ganha golpe, agarrao, cavar nem investida -- esses continuam recusados pelo
/// `PodeMexerOCorpo` de sempre. O desmaiado por OUTRA coisa (soco, ki, fome) continua parado: so a gravidade (a regra literal).
///
/// ONDE MORA: a recusa do passo e o `PodeMexerOCorpo` (`GameServer.Ia.cs`), e o rastejo e uma licenca DO PASSO DO JOGADOR, dada no
/// `AplicarInput` (`GameServer.cs`) -- o funil continua igual pro resto, e pra IA (decisao minha: o NPC esmagado continua preso, como no
/// DM; o pedido fala do personagem). O passo e um quarto do que o corpo daria sem a gravidade (`Esmagamento.PassoDoRastejo`), posto no
/// `SpeedStat` pelo `RecalcularVelocidade` -- o MESMO numero que o cliente obedece pela ficha e que o `ValidateStep` confere --, e o bit
/// `SheetState.SoRasteja` diz ao cliente que ele pode tentar o passo. O corpo deitado DESLIZA, como caiu: sem quadro novo, sem pose nova
/// e sem virar. NADA MAIS MUDA: quando se cai, quando se levanta e o dano do esmagamento sao os de antes. Prova: `--rastejoteste` e o robo
/// `--diagrastejo`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// SO A GRAVIDADE SEGURA ESTE CORPO? -- a pergunta unica do rastejo. Quem a faz: o `AplicarInput` (a licenca do passo e a corrida
	/// recusada), o `RecalcularVelocidade` (o passo de um quarto e o bit da ficha).
	///   * DESMAIADO: so se o desmaio veio do esmagamento (`DesmaioDaGravidade`) -- dentro ou fora do campo;
	///   * DE PE: so se a razao prende (`PrensadoPelaGravidade`, o mesmo `!dead || KeepsBody` do castigo).
	/// E SO A PE: quem voa continua preso no ar como antes (o pedido e se arrastar). E DE MAOS VAZIAS: quem ja segurava alguem quando a
	/// gravidade o prendeu continua preso ate soltar (soltar sempre pode) -- o passo de quem segura e o arremesso, e o de quem carrega leva
	/// o outro junto, e o rastejo nao da golpe nem agarrao. O Majin desfeito em pedacos nao se arrasta -- o corpo nem esta la.
	/// </summary>
	private bool SoAGravidadeSegura(ServerPlayer pl) =>
		!RastejoDesligadoDeTeste && !pl.Voando && pl.AgarrandoId == 0 && pl.Combate is { Desfeito: false } c
		&& (pl.Ficha.KO ? c.DesmaioDaGravidade : PrensadoPelaGravidade(pl));

	/// <summary>
	/// O ESMAGAMENTO ACABOU DE DERRUBAR ESTE CORPO -- chamado pelo `TickDoEsmagamento`, logo depois do dano que o fez cair. Marca o desmaio
	/// como da gravidade, refaz o passo e manda a ficha NA HORA (sem isto o cliente so saberia que pode se arrastar no tique de ficha
	/// seguinte, ate 0,2 s depois, e o passo dele e o do servidor discordariam nesse meio-tempo), e diz ao dono do corpo que ele ainda se
	/// arrasta: caido sempre foi parado, e sem a frase ninguem tentaria (decisao minha).
	/// </summary>
	private void MarcarODesmaioDaGravidade(ServerPlayer pl)
	{
		if (pl.Combate is not { Desfeito: false } c) return;
		c.DesmaioDaGravidade = true;
		RecalcularVelocidade(pl);
		MandarFicha(pl);
		Avisar(pl, Esmagamento.PorPeso(pl.Ficha)
			? "Você desmaiou esmagado pelo peso, mas ainda consegue se arrastar devagar."
			: "Você desmaiou esmagado pela gravidade, mas ainda consegue se arrastar devagar: saia daqui.");
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--rastejoteste`): ninguem se arrasta -- o jogo de antes de 2026-10-23, em que a gravidade que derruba ou
	/// prende segura o corpo parado. Lido pelo <see cref="SoAGravidadeSegura"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool RastejoDesligadoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--rastejoteste`): quem se arrasta CORRE -- o SHIFT do prensado de pe volta a valer. Lido pela linha da
	/// corrida no `AplicarInput`. Falso em jogo, sempre.
	/// </summary>
	internal bool RastejoCorreDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--rastejoteste`): o desmaiado que se arrasta VIRA pro lado do passo -- a cabeca deitada (`DirecaoDeitado`)
	/// e o olhar seguem a tecla, uma pose que ninguem pediu. Lido pela linha do olhar no `AplicarInput`. Falso em jogo, sempre.
	/// </summary>
	internal bool DeitadoViraNoRastejoDeTeste;
}
