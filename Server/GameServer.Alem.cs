using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ A MORTE LEVA PRO OUTRO MUNDO ============================
/// *"falta fazer o personagem quando MORRER ir pro OUTRO MUNDO e a AUREOLA aparecer sobre a
/// cabeca"* -- o pedido do dono, e este arquivo e a metade que conhece o servidor. A outra metade
/// (o LUGAR e os PRAZOS) esta em <see cref="Alem"/>, no Core.
///
/// Porte de `mob/proc/Death()` -- `Code/Modules/Death/Death.dm:6-111`. La sao doze passos; oito
/// deles ja estavam neste port espalhados por outros arquivos (a negacao da morte pela Aura of
/// Destruction, o Zenkai do algoz, a raiva de quem assistiu, a sucessao do trono, a saida da luta).
/// O que faltava eram os TRES ULTIMOS, e eles sao o pedido inteiro:
///
///     overlayList += 'Halo.dmi'      // :106-108 -- a aureola
///     loc = locate(187, 104, 6)      // :110    -- a viagem
///     SpreadHeal(100, 1, 1)          // :111    -- chega curado
/// ================================================================================
///
/// ============================ AS DUAS ETAPAS, E POR QUE SAO DUAS ============================
/// A morte deixou de ser um instante e passou a ser um percurso com dois prazos:
///
///     morre --[ Alem.MsNoChao ]--> Outro Mundo --[ Alem.MsNoAlem ]--> vivo, no berco
///       ^                             ^
///       |                             |
///   corpo CAIDO, na pose de       de PE, com aureola, andando
///   nocaute, com aureola          (`Un_KO()` do DM, `Death.dm:89`)
///
/// A PRIMEIRA ETAPA E DO DM: `KO(-1)` + `sleep(20)` ANTES do `loc =` (`:71-110`). Ela existe pra
/// quem viu a morte ver a morte -- sem ela, matar alguem seria "o inimigo piscou e nao estava mais
/// la", que le PIOR do que o jogo lia antes desta mudanca.
///
/// A SEGUNDA E DO PORT, e e um andaime declarado -- ver <see cref="Alem.MsNoAlem"/>. Nenhum dos
/// cinco caminhos de volta do original existe aqui ainda; sem uma volta, a viagem prenderia todo
/// mundo, e a instrucao do dono e *"prefira sempre o que nao prende ninguem"*.
/// ========================================================================================
///
/// ============================ UM RELOGIO SO, E ELE NAO E MAIS ESCRITO A MAO ============================
/// `pl.RenasceEm = NowMs() + MsAteRenascer` estava em NOVE lugares. Agora e o gancho
/// <see cref="Jandirus.Core.Combat.CombatState.AoMorrer"/>, instalado uma vez no `PrepararCombate`
/// ao lado do `NegarMorte` -- pelo mesmo argumento que criou aquele.
/// ====================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 1. O GANCHO -- "este corpo acabou de morrer"
	// =====================================================================
	/// <summary>
	/// O UNICO LUGAR QUE MARCA UMA MORTE. Pendurado em `CombatState.AoMorrer` pelo
	/// `PrepararCombate`, entao toda morte de todo corpo passa por aqui: soco, Kamehameha,
	/// explosao do planeta, calor da estrela, Final Explosion, Kaio-ken que estoura, fome,
	/// gestacao de bio-androide e o verb de admin.
	///
	/// ============================ ELE SO ESCREVE UM NUMERO, DE PROPOSITO ============================
	/// `Morrer()` e chamado de dentro do `MeleeResolver` e dos lacos de dano em area, que estao
	/// percorrendo a lista de uma zona. Viajar daqui mexeria nas listas de DUAS zonas no meio dessa
	/// varredura -- o "Collection was modified" que o `_npcsPraTirar` e o `TickDeQuemVolta` ja
	/// existem pra evitar. Quem viaja e o <see cref="PassoDaMorte"/>, no fim do prazo.
	/// ============================================================================================
	/// </summary>
	private void AMorteAconteceu(ServerPlayer pl)
	{
		pl.RelogioDaMorte = NowMs() + Alem.MsNoChao;

		// ============================ QUEM MORRE DENTRO DA MENTE NAO MORREU ============================
		// **NAO HA CORTE ESCRITO AQUI, E ISSO E UMA DECISAO.** O mecanismo que desfaz esta morte ja
		// existe e ja e chamado no MESMO tique: `BordasDeQuemEstaFora` (`GameServer.CorpoLargado.cs`)
		// enfileira o dono assim que ve `pl.Ficha.dead`, o `TickDeQuemVolta` chama `SairDaMente`, e o
		// `RestaurarDaMente` reescreve `dead = f.Morto` -- o estado com que a pessoa ENTROU, que e
		// sempre "viva" (`EntrarNaMente` recusa quem esta caido ou morto).
		//
		// Ou seja: quando o prazo dos 15 s vencer, `pl.Ficha.dead` ja e falso ha 15 s e o
		// `PassoDaMorte` nem e alcancado. E o `MindMeditate.dm:448` -- *"morte MENTAL nao e real"* --
		// funcionando pelo caminho que o port ja tinha, e nao por um `if (NaMente(pl))` novo aqui.
		//
		// Um segundo corte neste ponto seria a copia que um dia discorda: bastaria alguem mexer no
		// prazo de uma das duas pontas pra as duas regras contarem historias diferentes sobre a mesma
		// morte. Fica ESCRITO porque a ausencia precisa ser lida como escolha.
		// ==========================================================================================

		if (!EhJogador(pl)) return;   // corpo sem dono nao le mensagem nenhuma

		Avisar(pl, "você morre. O seu corpo esfria no chão -- e uma auréola se acende sobre a sua cabeça.");
	}

	// =====================================================================
	// 2. O PERCURSO -- chamado do TickCombate quando o prazo vence
	// =====================================================================
	/// <summary>
	/// VENCEU O PRAZO: ou sobe pro Outro Mundo, ou volta a vida. Qual dos dois se responde pelo
	/// LUGAR onde o corpo esta -- ver <see cref="Alem.MortoDePe"/> pro argumento de por que a etapa
	/// nao e um campo.
	/// </summary>
	private void PassoDaMorte(ServerPlayer pl)
	{
		if (Alem.EhOAlem(pl.Zone)) { Renascer(pl); return; }
		IrProAlem(pl);
	}

	/// <summary>
	/// ============================ A VIAGEM -- `loc = locate(187,104,6)` ============================
	/// `Death.dm:110-111`, os dois ultimos passos do original: o corpo aparece na mesa do Enma e
	/// **chega curado inteiro** (`SpreadHeal(100,1,1)`).
	///
	/// ============================ E ELE CHEGA DE PE, COM OS MEMBROS DE VOLTA ============================
	/// O DM levanta o morto ANTES de mover -- `RegrowLimb` em cada membro decepado e `spawn Un_KO()`
	/// (`:86-89`). E `Corpo.Restaurar()` faz exatamente as duas coisas numa chamada (o mesmo metodo
	/// que o `Reviver` usa), sem tocar em `dead`: a pessoa continua morta, so nao esta mais em
	/// pedacos. Sem isto o jogador chegaria ao Outro Mundo sem os bracos que perdeu na luta, e a
	/// primeira coisa que ele veria do alem seria um defeito.
	///
	/// A POSE se resolve sozinha: <see cref="ServerPlayer.Pose"/> pergunta ao `Alem.MortoDePe`, e a
	/// resposta muda no instante em que a zona muda. Nao ha nada a escrever.
	/// ================================================================================================
	///
	/// ============================ O QUE O `MoveToZone` JA FAZ, E NAO PRECISOU DE LINHA ============================
	/// Soltar embate de Ki, soltar o raio, limpar os projeteis do dono, avisar quem ficou
	/// (`PeerLeft` -- sem ele o corpo fantasma ficaria de pe no lugar da morte), zerar o orcamento de
	/// passo, derrubar o voo e a altitude, remandar cenario, obras, portas, aparencia e feridas.
	/// Tudo isso e o caminho de troca de planeta que ja existia; a morte so o usa.
	/// ========================================================================================================
	/// </summary>
	private void IrProAlem(ServerPlayer pl)
	{
		// ============================ A TRANCA DA SALA DO TEMPO ABRE AQUI, E NAO NO RENASCER ============================
		// **Decisao do dono**: depois de preso, *"so morrendo pra sair"*. Isto estava no `Renascer` --
		// certo enquanto morrer e renascer eram o mesmo instante. Nao sao mais: com a viagem no meio,
		// deixar a limpeza pro fim manteria o preso MARCADO COMO PRESO durante o minuto inteiro no
		// Outro Mundo, e a `SalaSessao` continuaria contando o tempo dele numa sala de outro planeta.
		//
		// O bit vai pro disco, entao ele tem que morrer no primeiro passo que tira o corpo da sala.
		// Ver `GameServer.SalaDoTempo.AMorteSaiDaSala`.
		// ==========================================================================================================
		AMorteSaiDaSala(pl);

		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		MoveToZone(pl.Id, alem, MesaDoEnma(alem));

		// `SpreadHeal(100,1,1)` + `RegrowLimb` -- ver o cabecalho. NAO e `Reviver`: `dead` fica.
		pl.Combate.Corpo.Restaurar();
		pl.Combate.SincronizarVida();
		AjustarGanhoDoRabo(pl);   // o rabo voltou: o ritmo de treino do Saiyajin volta com ele

		Avisar(pl, "o chão some sob você. Você abre os olhos no Outro Mundo, inteiro -- e morto.");
		GD.Print($"[server] {pl.Name} MORREU e foi pro Outro Mundo"
				 + $" ({Alem.MsNoAlem / 1000:0}s ate voltar a vida)");
	}

	/// <summary>
	/// A MESA DO ENMA, ja conferida contra a colisao da zona.
	///
	/// A coordenada vem do DM (<see cref="Alem.MesaDoEnma"/>); o `PontoLivrePerto` e a mesma defesa
	/// que o <see cref="PontoDeNascimento"/> usa -- um mapa convertido tem parede onde o `.dmm`
	/// tinha, e uma construcao levantada em cima do ponto o bloqueia em runtime. Cair dentro de uma
	/// parede no Outro Mundo seria a segunda maneira de prender alguem.
	///
	/// A ALTURA SAI DO MAPA e nao de um 500 cravado: e ela que inverte o eixo Y do BYOND, e um
	/// numero cravado aqui mentiria calado no dia em que o z6 fosse reconvertido com outro tamanho.
	/// </summary>
	private Vec2 MesaDoEnma(ZoneKey alem)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(alem);
		Vec2 mesa = Alem.MesaDoEnma(mapa?.Height ?? 500);
		return mapa?.PontoLivrePerto(mesa) ?? mesa;
	}

	// =====================================================================
	// 3. A AUREOLA NO FIO
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE A AUREOLA NAO CABE NO SNAPSHOT ============================
	/// Os dois bytes de flags do <see cref="Protocol.EntityState"/> estao CHEIOS -- o primeiro com
	/// direcao (2 bits), pose (3) e mais tres bits, o segundo com os oito de carga/deitado/voo/redeas/
	/// nave. Um terceiro byte custaria um byte por corpo por tique (com 151 habitantes, ~4,5 KB/s)
	/// pra carregar UM bit que muda duas vezes por vida.
	///
	/// Entao ela vai pelo canal do estado LENTO, que este port ja tem em duas cores: `S2C.Feridas` e
	/// `S2C.Forma`. Reliable, so quando MUDA, e reenviado a quem entra na zona. Custo por tique: zero.
	///
	/// ============================ E ELA E DETECTADA POR DIFERENCA, E NAO POR CHAMADA ============================
	/// A tentacao era mandar o pacote de dentro do <see cref="AMorteAconteceu"/> e do
	/// <see cref="Renascer"/>. Seriam dois lugares -- e `Ficha.dead` e escrito DIRETO em pelo menos
	/// quatro outros que nao passam por nenhum dos dois: o restauro da mente (`RestaurarDaMente`), a
	/// volta no tempo (`GameServer.Tecnicas.G4`), o reflexo que nasce vivo (`GameServer.Clone.cs:143`)
	/// e a gestacao do bio-androide (`GameServer.Tech.cs:868`).
	///
	/// Comparar contra o ultimo valor enviado cobre os seis de uma vez, e cobre o setimo que nascer
	/// amanha. E o argumento inteiro do `TickDasFeridas`, do lado do qual isto roda.
	/// ========================================================================================================
	/// </summary>
	private void TickDasAureolas()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			bool agora = Alem.TemAureola(pl.Ficha.dead);
			if (agora == pl.EnvAureola) continue;
			pl.EnvAureola = agora;
			MandarAureola(pl);
		}
	}

	/// <summary>Manda a aureola de um corpo pra todo mundo que esta na zona dele (o dono incluso).</summary>
	private void MandarAureola(ServerPlayer de)
	{
		NetDataWriter w = PacoteDeAureola(de);
		foreach (ServerPlayer o in ZoneList(de.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private static NetDataWriter PacoteDeAureola(ServerPlayer de)
	{
		var w = Protocol.Begin(Protocol.S2C.Aureola);
		w.Put(de.Id);
		w.Put(Alem.TemAureola(de.Ficha.dead));
		return w;
	}

	/// <summary>
	/// APRESENTA AS AUREOLAS a quem chega numa zona, e as de quem ja estava a ele.
	///
	/// Mesma familia de defeito das construcoes, das portas, das feridas e das formas: o pacote
	/// existe, sai UMA vez, e quem nao estava presente naquele instante nunca soube. Sem isto, quem
	/// entrasse no Outro Mundo veria os mortos de la sem aureola nenhuma -- e o Outro Mundo e
	/// justamente onde todo mundo tem uma.
	/// </summary>
	private void TrocarAureolas(ServerPlayer novo)
	{
		novo.EnvAureola = Alem.TemAureola(novo.Ficha.dead);

		List<ServerPlayer> zona = ZoneList(novo.Zone.Hash);
		NetDataWriter minha = PacoteDeAureola(novo);

		foreach (ServerPlayer outro in zona)
		{
			outro.Peer?.Send(minha, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			if (outro != novo)
				novo.Peer?.Send(PacoteDeAureola(outro), Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}
}
