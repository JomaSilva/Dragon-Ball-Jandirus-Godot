namespace Jandirus.Core.Npc;

/// <summary>
/// O QUE UM CORPO SEM DONO CARREGA ALEM DA FICHA -- a certidao de nascimento dele, e o unico
/// estado VIVO do roteiro.
///
/// ============================ ELE NAO GUARDA COPIA DE NADA ============================
/// A tentacao era copiar pra ca os campos do molde que interessam (temperamento, "ascende
/// sozinho?", a lista de estagios). Seriam sete campos que ja existem em outro lugar, e a copia
/// e onde as duas verdades divergem: editar o `npcs.json` e reiniciar daria um servidor com metade
/// dos bichos obedecendo ao arquivo novo e metade a uma copia velha guardada em memoria.
///
/// Entao ele guarda a REFERENCIA ao <see cref="MoldeDeNpc"/> (que e dado imutavel lido do disco) e
/// deriva o resto. O unico campo de verdade e o <see cref="Estagio"/>, porque so ele MUDA em jogo.
/// ==================================================================================
///
/// ============================ E ELE NAO E A FICHA ============================
/// A ficha do NPC e a mesma de um jogador: <see cref="Stats.Fighter"/>,
/// <see cref="Skills.SkillBook"/>, <see cref="Skills.NiveisDeSkill"/>,
/// <see cref="Forms.EstadoDeForma"/>. Nada disso mora aqui. O que mora aqui e o que um jogador nao
/// tem: de que receita ele saiu, com que semente, e em que degrau do roteiro esta.
///
/// Ter cerebro (`ServerPlayer.Cerebro != null`) continua sendo "quem dirige este corpo"; ter papel
/// e "de onde este corpo veio". Sao perguntas diferentes: um jogador em furia lendaria tem cerebro
/// e nao tem papel; um NPC parado numa cidade tem papel e nao tem cerebro.
/// ==========================================================================
/// </summary>
public sealed class PapelDeNpc(MoldeDeNpc molde, ulong semente)
{
	/// <summary>A receita de onde este corpo saiu. Dado do disco -- nao se escreve nele.</summary>
	public readonly MoldeDeNpc Molde = molde;

	/// <summary>
	/// A SEMENTE deste corpo. Guardada porque o sorteio pode precisar ser refeito (um campo novo
	/// que nasca depois, uma bancada que queira reproduzir o mesmo bicho) e porque ela e a prova
	/// de determinismo: dois servidores com a mesma semente produzem o mesmo NPC.
	///
	/// Mesmo raciocinio do <see cref="Forms.LimiaresPessoais.Semente"/>, que ja existia por isso.
	/// </summary>
	public readonly ulong Semente = semente;

	/// <summary>
	/// EM QUE DEGRAU DO ROTEIRO ELE ESTA (0 = o primeiro). O `s2_form` do controlador de evento
	/// (BossEvents.dm:588), que la e 1-based.
	///
	/// UNICO CAMPO MUTAVEL DESTE TIPO, e de proposito: tudo o mais e derivavel do molde.
	/// </summary>
	public int Estagio;

	public bool EhChefe => Molde.EhChefe;

	/// <summary>Ver <see cref="MoldeDeNpc.AscendePorDecisao"/> -- "tem a forma e nao a usa".</summary>
	public bool AscendePorDecisao => Molde.AscendePorDecisao;

	public EstagioDeChefe? Degrau =>
		Estagio >= 0 && Estagio < Molde.Estagios.Length ? Molde.Estagios[Estagio] : null;

	/// <summary>O degrau seguinte, ou nulo quando este e o ultimo (a lista de um item so do Freeza de Vegeta).</summary>
	public EstagioDeChefe? Proximo =>
		Estagio + 1 < Molde.Estagios.Length ? Molde.Estagios[Estagio + 1] : null;

	/// <summary>
	/// O GATILHO QUE ENCERRA O DEGRAU ATUAL -- a fracao de vida do pior membro. Negativo = nada o
	/// encerra (ultimo degrau, ou ficha de um degrau so).
	/// </summary>
	public double GatilhoAtual => Proximo == null ? -1 : Degrau?.GatilhoMembro ?? -1;
}
