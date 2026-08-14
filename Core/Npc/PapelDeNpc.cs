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

	/// <summary>
	/// ============================ O BP PINADO PELA SAGA, DEGRAU A DEGRAU ============================
	/// Vazio no corpo comum (e no chefe nascido fora de uma saga -- admin, bancada): ai o BP e o da
	/// ficha do `npcs.json`, lido direto. Preenchido pela cadeia de sagas com o vetor que ela resolveu
	/// **no disparo do marco** (ver <see cref="Sagas.Pinar"/>), dias in-game antes deste corpo existir.
	///
	/// Mora aqui, e nao no estado da saga sozinho, porque quem PRECISA do numero e o corpo: o
	/// `TickDoRoteiro` reancora o BP a cada segundo e o `AvancarEstagio` pina o degrau novo, e nenhum
	/// dos dois tem por que conhecer a cadeia. E o `boss_seed_bp`, que no original tambem e campo do
	/// mob e nao do controlador (BossEvents.dm:186).
	/// ==========================================================================================
	/// </summary>
	public double[] BpsPinados = [];

	/// <summary>
	/// O BP DESTE DEGRAU: o pinado quando ha um, senao o da ficha. Uma casa so -- as duas fontes
	/// respondidas por lugares diferentes divergiriam no primeiro degrau que alguem esquecesse.
	/// </summary>
	public double BpDoDegrau(int degrau) =>
		degrau >= 0 && degrau < BpsPinados.Length ? BpsPinados[degrau]
		: degrau >= 0 && degrau < Molde.Estagios.Length ? Molde.Estagios[degrau].Bp
		: 0;

	/// <summary>Atalho: o BP do degrau em que ele esta agora.</summary>
	public double BpAtual => BpDoDegrau(Estagio);

	/// <summary>
	/// ============================ O ALVO QUE O ROTEIRO IMPOE -- o `bev_prey` ============================
	/// Id de um corpo que este NPC persegue **por ordem do evento**, acima de qualquer decisao da IA.
	/// Zero = ele caca pelo criterio normal.
	///
	/// E o `mob/npc/var/tmp/mob/bev_prey` (BossEvents.dm:264), e a razao de existir e literal: o Cell
	/// atras de um androide NOCAUTEADO. A caca normal (`PresaDaFera`) pula quem esta no chao -- e tem
	/// que pular, senao a fera fica parada em cima de um corpo caido ate o prazo vencer --, entao sem
	/// este canal o Cell simplesmente nunca chegaria perto da vitima. O comentario do original diz o
	/// desenho inteiro: *"absorcao e prioridade ABSOLUTA: larga a luta atual"*.
	///
	/// Nao persiste: e um alvo de agora, e o corpo do outro nao atravessa reinicio.
	/// ================================================================================================
	/// </summary>
	public int PresaDoRoteiro;

	public bool EhChefe => Molde.EhChefe;

	/// <summary>De que tipo este corpo e. Ver <see cref="TipoDeNpc"/> -- e por ele que o recorte corta.</summary>
	public TipoDeNpc Tipo => Molde.Tipo;

	/// <summary>
	/// ============================ ELE ATACA POR CONTA PROPRIA? ============================
	/// Cidadao: **nao**. E o `AIAlwaysActive = 0` do `mob/npc/Citizen` (PlanetPopulation.dm:66), e o
	/// comentario do autor diz a regra inteira numa linha: *"a fightable being -> base foundTarget
	/// ENGAGES when provoked ... but never aggros on sight -> peaceful until hit"*.
	///
	/// A distincao e entre `monster = 1` (da pra bater nele, e ele revida) e `AIAlwaysActive = 1`
	/// (ele procura alvo sozinho): o cidadao tem a primeira e nao a segunda. Sem isso o povoamento
	/// entregaria quarenta habitantes que caem em cima do primeiro jogador que pousar no planeta --
	/// o `PresaDaFera` do servidor devolve *o corpo em pe mais proximo, seja quem for*, que e a
	/// receita do Oozaru selvagem e nao a de um vilarejo.
	/// ==============================================================================
	/// </summary>
	public bool Pacifico => Tipo == TipoDeNpc.Cidadao;

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
