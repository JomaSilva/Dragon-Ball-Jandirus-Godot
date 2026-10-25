using Jandirus.Core.Races;

namespace Jandirus.Server;

/// <summary>
/// ============================ O ESCRITOR DA ASCENSAO (dono, 2026-10-23) ============================
/// A regra e a conta moram no Core (`Core/Races/Ascensao.cs`); aqui mora QUEM a escreve no corpo, e e o UNICO escritor de producao do
/// `Fighter.BPBoost` alem do zero neutro do bio (a `formas` [21] da FormasBench varre os fontes e cobra isso).
///
/// DUAS PORTAS CHAMAM, e as duas sao necessarias:
///   * o topo do `Treinar` (`GameServer.cs`), que roda pra TODO jogador a 5 Hz -- o `Stats()` deste port, onde o DM chama o
///     `Auto_Gain()` (`Stats.dm:257`) --, treinando ou nao: o BP sobe e o bonus acompanha no mesmo tique, e o marco de ganho logo
///     abaixo dele ja le o valor novo;
///   * o `AplicarForma` (`GameServer.Formas.cs`), o funil de toda troca de forma, ANTES do `RepercutirPoder`: sem esta, quem entra
///     numa forma absoluta expressaria por ate 200 ms a Ascensao SOMADA a ela (e um soco nessa janela bateria com 16 x 16).
///
/// SO JOGADOR (o dono: "NPC fica como esta"): corpo sem dono na tela nao e tocado -- nem o NPC, nem o reflexo da mente, nem o boneco
/// do corpo largado (que divide a ficha com o dono, e o dono ja a escreve). Quem nao ascende recebe 1: a raca que mudou (a
/// reencarnacao, o androide) nao carrega o bonus da raca de antes.
/// ===================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>DEFEITO INJETADO (bancada `--ascensaoteste`): o NPC tambem ascende -- o jogo sem o "so jogadores" do dono. Falso em jogo, sempre.</summary>
	internal static bool AscensaoProNpcDeTeste;

	/// <summary>O `BPBoost` deste corpo, pela regra do Core: a raca e a classe do perfil de formas, o BP BASE e a forma de agora.</summary>
	private static void AfirmarAAscensao(ServerPlayer pl)
	{
		if (!EhJogador(pl) && !AscensaoProNpcDeTeste) return;
		bool absoluta = Jandirus.Core.Forms.Catalogo.Def(pl.Forma?.Atual)?.Absoluta == true;
		pl.Ficha.BPBoost = Ascensao.Fator(pl.Race, pl.Ficha.Class, pl.Ficha.BP, absoluta);
	}
}
