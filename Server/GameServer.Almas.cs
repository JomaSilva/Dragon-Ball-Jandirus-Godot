using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS ALMAS DO OUTRO MUNDO (dono, 2026-10-16) ============================
/// *"alem de colocar npcs no outro mundo, npcs mortos com a aureula etc, no inferno tb"*. Sao HABITANTES (molde do tipo cidadao, com
/// `"alma": true` -- ver `MoldeDeNpc.Alma`), nascidos pelo povoamento de sempre (`npcs.json`: 40 no Outro Mundo, 20 no Ceu, 30 no
/// Inferno), com tres diferencas, todas daqui:
///
///   1. NASCEM MORTOS, DE AUREOLA E DE PE (<see cref="FazerMortoDoAlem"/>). Nada novo na tela: e o estado de todo jogador que ja chegou
///      ao Outro Mundo (`MorteJaViajou`), e a aureola vai pelo mesmo `TickDasAureolas` (por diferenca). De pe porque o lugar e de morto
///      (`Alem.MortoDePe`) -- a mesma pergunta que deixa o jogador morto andar la.
///   2. O RELOGIO DA MORTE TRANCADO. Sem isso o tique da morte (`VenceuOPrazoDaMorte`) acharia um NPC do mundo morto com o prazo vencido
///      (o campo nasce 0) e o tiraria do mundo no segundo seguinte -- e o povoamento poria outro no lugar, pra sempre.
///   3. A SEGUNDA MORTE NAO E A DO JOGADOR (<see cref="AlmaSeRecompoe"/>). O fantasma do jogador que cai de vez no Outro Mundo e curado e
///      DEVOLVIDO A MESA DO ENMA (`MorrerDeNovoNoAlem`, `Death.dm:113-118`); a alma se recompoe ali mesmo, com o mesmo coma curto. Sem
///      isso cada alma do Inferno derrubada iria parar no Outro Mundo -- o Inferno esvaziando e a mesa do Enma lotando.
///
/// E O RESTO E O DE TODO HABITANTE: passeia, conversa (com as frases de morto -- `rotina.json`), treina e chama pra spar. O que
/// precisou mudar pra isso foi o "caido" da vida diaria, que lia o `dead` cru (`GameServer.Rotina.cs`): um morto de pe nao esta caido.
/// ===============================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// FAZ DE UM CORPO SEM DONO UM MORTO DO OUTRO MUNDO: morto, com a viagem feita (a aureola), de pe (o lugar e que decide) e com o
	/// relogio da morte trancado. As almas e o vilao que volta no Inferno (<see cref="NascerVilaoDoInferno"/>) passam por aqui.
	/// </summary>
	private static void FazerMortoDoAlem(ServerPlayer npc)
	{
		npc.Ficha.dead = true;
		npc.Ficha.KO = false;
		npc.MorteJaViajou = !AlmaSemAureolaDeTeste;
		npc.RelogioDaMorte = AlmaComRelogioDeTeste ? 0 : long.MaxValue;
	}

	/// <summary>A ALMA: um morto do Outro Mundo cuja segunda morte e recompor-se ali mesmo. Chamada pelo `NascerNpc`.</summary>
	private void FazerAlma(ServerPlayer npc)
	{
		FazerMortoDoAlem(npc);
		if (!AlmaVaiAoEnmaDeTeste && npc.Combate is { } c) c.AoMorrerDeNovo = _ => AlmaSeRecompoe(npc);
	}

	/// <summary>
	/// A ALMA QUE "MORRE DE NOVO" SE RECOMPOE NO LUGAR: curada e em coma pelo mesmo prazo do fantasma do jogador
	/// (`Alem.SegundosDeComaDoMortoDeNovo`). Nao e uma morte (nao conta, nao paga Zenkai, nao mexe em karma nem em reputacao) -- e o
	/// mesmo "um morto nao morre duas vezes" do jogador, sem a viagem a mesa do Enma.
	/// </summary>
	private void AlmaSeRecompoe(ServerPlayer alma)
	{
		if (alma.Combate is not { } c) return;
		c.Corpo.Restaurar();
		c.SincronizarVida();
		MandarFeridas(alma);
		c.Nocautear(Alem.SegundosDeComaDoMortoDeNovo);
	}

	/// <summary>DEFEITO INJETADO (bancada `--infernoteste`): a alma nasce sem a aureola -- um morto qualquer. Falso em jogo, sempre.</summary>
	internal static bool AlmaSemAureolaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--infernoteste`): o relogio da morte da alma fica como nasceu (zero) -- e o tique da morte a tira do mundo.
	/// Falso em jogo, sempre.
	/// </summary>
	internal static bool AlmaComRelogioDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--infernoteste`): a segunda morte da alma e a do jogador -- ela vai parar na mesa do Enma. Falso em jogo,
	/// sempre.
	/// </summary>
	internal static bool AlmaVaiAoEnmaDeTeste;
}
