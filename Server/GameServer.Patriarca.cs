using Jandirus.Core.Npc;

namespace Jandirus.Server;

/// <summary>
/// O GRANDE PATRIARCA DE NAMEK NO SERVIDOR (dono, 2026-10-19: *"adicione o patriarca em namek assim como tinha no Finale-master"*) --
/// o menu E dele e a conversa do `Guru/Click()` do DM (`PlanetPopulation.dm:220-234`). Quem ele e, de onde vem e por que cada pedaco
/// mora onde mora: `Core/Npc/Patriarca.cs`. O nascimento perto do berco e o `PontoPertoDoBerco` (`GameServer.Povoamento.cs`), a folha
/// dele e a `AparenciaDeNpc` (`GameServer.Npc.cs`), e o "nao sai vagando" e a `Rotina.Parado` e o `PasseiaSozinho`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>DEFEITO INJETADO (`--patriarcateste`, robo `--diagpatriarca`): o apontador da tecla E nao enxerga o Guru -- ninguem fala com ele. Falso em jogo, sempre.</summary>
	internal static bool PatriarcaSemMenuDeTeste;

	/// <summary>DEFEITO INJETADO (`--patriarcateste`): o Guru nao pergunta a reputacao -- desperta quem o povo de Namek ve com maus olhos. Falso em jogo, sempre.</summary>
	internal static bool PatriarcaIgnoraAReputacaoDeTeste;

	/// <summary>DEFEITO INJETADO (`--patriarcateste`): o Guru nasce com o corpo sorteado do povo, e nao com a folha dele. Lido pela `AparenciaDeNpc`. Falso em jogo, sempre.</summary>
	internal static bool PatriarcaComOCorpoDoPovoDeTeste;

	/// <summary>Este corpo e um Grande Patriarca (o do povoamento, ou um que o admin pos no mundo)?</summary>
	private static bool EhOPatriarca(ServerPlayer p) => p.Peer == null && p.Papel?.Molde.Id == Patriarca.Molde;

	/// <summary>O Guru mais perto deste jogador na zona dele -- so os de pe, se pedido -- ou nulo.</summary>
	private ServerPlayer? PatriarcaMaisPerto(ServerPlayer pl, bool soDePe)
	{
		ServerPlayer? melhor = null;
		float perto = float.MaxValue;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			if (!EhOPatriarca(o) || (soDePe && (o.Ficha.dead || o.Ficha.KO))) continue;
			float d = (o.Pos - pl.Pos).LengthSquared;
			if (d >= perto) continue;
			perto = d;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// O GURU QUE A TECLA E DESTE JOGADOR ALCANCA, e o menu dele -- ou (0, "", "") quando nenhum. E o terceiro corpo que o
	/// `S2C.DivinoPerto` aponta, depois do Whis e do Bills (`ApontarODivinoPerto`): o de pe mais perto, dentro da caixa de 2 tiles por
	/// eixo do menu (`AoAlcanceDaTeclaE`, o `get_dist(usr, src) > 2` do DM). Caido ou morto nao tem menu (`if(dead || KO) return`,
	/// `:222`). O pacote leva o corpo, a chave do menu e o nome: nada de poder.
	/// </summary>
	private (int Corpo, string Tipo, string Nome) PatriarcaAoAlcance(ServerPlayer pl)
	{
		if (PatriarcaSemMenuDeTeste) return (0, "", "");
		ServerPlayer? guru = PatriarcaMaisPerto(pl, soDePe: true);
		return guru != null && AoAlcanceDaTeclaE(pl, guru) ? (guru.Id, Patriarca.Tipo, guru.Name) : (0, "", "");
	}

	/// <summary>
	/// FALAR COM O GRANDE PATRIARCA -- o `Guru/Click()` do DM (`PlanetPopulation.dm:220-234`), na ordem dele: longe, ele pede pra chegar
	/// perto; caido, nao ouve; o malvisto do povo dele e mandado embora; quem ja despertou ouve que o resto do caminho e dele; e o resto
	/// desperta -- pelo mesmo `DespertarPotencialG11` do verbo `Unlock_Potential` e do desejo ao dragao (`UnlockPotential.dm`), uma vez na
	/// vida. As falas sao as do DM, com o nome do corpo no lugar do "Guru" cravado.
	///
	/// DIVERGENCIA DECLARADA: diante do Guru caido o DM fica calado (`if(dead || KO) return`); aqui o servidor responde -- o menu so
	/// PEDE, e um pedido sem resposta parece botao quebrado.
	/// </summary>
	private void FalarComOPatriarca(ServerPlayer pl)
	{
		ServerPlayer? guru = PatriarcaMaisPerto(pl, soDePe: false);
		if (guru == null || !AoAlcanceDaTeclaE(pl, guru))
		{
			Avisar(pl, "Chegue mais perto para falar com o Grande Patriarca.");
			return;
		}
		if (guru.Ficha.dead || guru.Ficha.KO)
		{
			Avisar(pl, $"{guru.Name} está caído -- o Grande Patriarca não pode te ouvir agora.");
			return;
		}

		// O POVO DELE E O DO PLANETA EM QUE ELE NASCEU -- o "Namek" cravado no DM (`planet_rep_get("Namek", usr)`), que e a chave com
		// que o livro-caixa anota quem matou habitante de la (`MorreuUmCorpoSemDono`).
		string povo = guru.Berco.Natal.Length > 0 ? guru.Berco.Natal : guru.Zone.Name;
		if (!PatriarcaIgnoraAReputacaoDeTeste && Patriarca.Recusa(ReputacaoDe(povo, pl.Conta)))
		{
			Avisar(pl, $"{guru.Name}: \"Sinto o sofrimento do meu povo em suas mãos... Saia da minha frente.\"");
			return;
		}
		if (pl.Ficha.unlockPotential >= 1)
		{
			Avisar(pl, $"{guru.Name}: \"Seu potencial já foi despertado, jovem guerreiro. O resto do caminho é seu.\"");
			return;
		}

		AvisarPertoG3(guru, RaioDaVista, $"{guru.Name} põe a mão sobre a cabeça de {pl.Name}...");
		DespertarPotencialG11(pl, pl, autor: guru.Name);
		Avisar(pl, $"{guru.Name}: \"Desperte... AGORA!\" Uma torrente de poder oculto explode de dentro de você!");
	}
}
