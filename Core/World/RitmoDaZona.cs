namespace Jandirus.Core.World;

/// <summary>
/// ============================ AS ZONAS QUE MUDAM O RITMO DO TREINO ============================
/// Hoje sao duas, e elas puxam pra lados opostos: a Sala do Tempo multiplica o ganho de BP por 280
/// (um dia la = um ano aqui) e a dimensao mental o divide por 4 (`MIND_GAIN_MULT = 0.25`,
/// `MindMeditate.dm:18`). O original trata as duas pelo mesmo mecanismo -- `htc_gain_mult()` e
/// `mind_gain_mult()` sao o mesmo proc com outro numero, e os dois entram nos mesmos `*_Gain`.
///
/// **POR QUE UMA CHAMADA SO, E NAO UMA LINHA EM CADA LUGAR.** O modo de falha deste tipo de
/// multiplicador ja esta registrado no cabecalho do <see cref="SalaDoTempo.AplicarRitmo"/>: **ligar
/// e esquecer de desligar**. Um jogador que saisse da mente com `zoneGainMult = 0,25` no bolso
/// treinaria a um quarto na Terra pra sempre, e nada na tela diria de onde veio -- pior que o caso
/// da Sala, porque este e um castigo silencioso em vez de um premio silencioso.
///
/// Aqui a pergunta e feita UMA vez por troca de zona e as duas respostas saem juntas. A ORDEM E O
/// CONTRATO: `AplicarRitmo` escreve o valor **neutro** quando a zona nao e a Sala, e e sobre esse
/// neutro que a mente escreve o dela. Nao ha caminho em que os dois valham ao mesmo tempo (uma
/// mente nao e a Sala do Tempo), e nao ha caminho em que nenhum dos dois seja escrito.
///
/// O SERVIDOR CHAMA ISTO NO `AplicarGravidade`, que e o funil por onde toda troca de zona passa:
/// login, passagem, porta, nave, admin, planeta gerado, mente e leme. Ver `GameServer.cs`.
/// ==========================================================================================
/// </summary>
public static class RitmoDaZona
{
	/// <param name="sessaoDaSalaValendo">
	/// A sessao da Sala do Tempo deste corpo ainda rende? Ver <see cref="SalaDoTempo.SessaoValendo"/>
	/// -- estar dentro nao basta. Irrelevante fora da Sala.
	/// </param>
	public static void Aplicar(Stats.Fighter f, ZoneKey zona, bool sessaoDaSalaValendo = true)
	{
		// PRIMEIRO, e ele e quem escreve o NEUTRO: fora da Sala, `zoneGainMult` volta a 1 e
		// `zoneMasteryMult` tambem. Sem esta chamada em primeiro lugar, sair da mente nunca limparia
		// o 0,25.
		SalaDoTempo.AplicarRitmo(f, zona, sessaoDaSalaValendo);

		// E A MENTE E A EXCECAO QUE ESCREVE POR CIMA: o ganho de BP a um quarto e (2026-10-16) a MAESTRIA DE FORMA PARADA -- o
		// dono: *"faça q n é possivel masterizar forma na meditaçao profunda"*. DIVERGENCIA DECLARADA: o original nao mexe na
		// maestria la dentro (o `mind_gain_mult` entra nos `*_Gain` e em nada mais). Zero aqui quer dizer "nesta zona nao se
		// masteriza": o funil da maestria por tempo na forma (`GameServer.SubirMaestriaDaZona`) multiplica por ele, e a
		// maestria do ki divino (`GameServer.TickDaMaestriaDivina`) pergunta por ele.
		if (DimensaoMental.EhAMente(zona))
		{
			f.zoneGainMult = Stats.GainKnobs.MindDimensionMult;
			if (!MenteMasterizaFormaDeTeste) f.zoneMasteryMult = 0;
		}
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--menteteste`, familia 1): a mente volta a deixar masterizar forma -- a regra de antes de
	/// 2026-10-16. Lido pelo <see cref="Aplicar"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MenteMasterizaFormaDeTeste;
}
