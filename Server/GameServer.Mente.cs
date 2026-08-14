using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O CORPO COMO ELE ENTROU NA MENTE. Ver <see cref="GameServer.FotografarParaAMente"/>.
///
/// So o que **nao** e derivavel entra aqui. O `HP`, por exemplo, NAO esta na lista: ele e a media
/// dos membros (`CombatState.SincronizarVida`), entao devolver os membros ja devolve o HP -- e
/// guardar os dois criaria a chance de eles voltarem discordando. E o `mind_snap_hp` do DM
/// (`MindMeditate.dm:332-339`) existe la porque **la** o HP e um numero proprio.
/// </summary>
public sealed class FotoDaMente
{
	/// <summary>Vida e "estava decepado" por membro, pelo nome -- a mesma foto do savefile.</summary>
	public readonly Dictionary<string, (double Vida, bool Decepado)> Membros = [];

	public double Ki, Vigor;
	public bool KO, Morto;
}

/// <summary>
/// ============================ A MENTE: A PORTA, O VISITANTE, OS CHEFES E AS TRES REGRAS ============================
/// *"a mecanica da MEDITACAO, onde vc podia meditar profundamente e enfrentar um CLONE seu na sua
/// mente, e se um player meditasse AO SEU LADO ele entraria na sua mente e poderia lutar com vc. la
/// dentro os FERIMENTOS NAO SAO TRANSFERIDOS pro corpo real, NAO TEM COMO TER ZENKAI e o GANHO DE BP
/// E REDUZIDO. e tb tem a opcao de enfrentar NPCS BOSS Q VC JA VIU ANTES na sua mente, pra fazer uma
/// luta simulada."*
///
/// Porte de `Code/Modules/Stats/Training/MindMeditate.dm`. O que era de la e o que e novo:
///
///   | pedido                        | DM                                   | aqui                          |
///   |-------------------------------|--------------------------------------|-------------------------------|
///   | o corpo fica pra tras         | `mob/npc/mind_dummy`                 | `LargarOCorpo` (Camada 1)     |
///   | o reflexo                     | `mob/npc/MindClone`                  | `CriarClone`                  |
///   | ele se reergue derrotado      | `clone_watch()` + `MIND_CLONE_REVIVE`| `DerrubadoNaMente`/`ReerguerOOponente` |
///   | ele tem o SEU poder           | `mind_seed_bp` = `expressedBP`       | `EspelharODono` + `ServerPlayer.BpDaMente` |
///   | e o poder dele nao infla      | `ai_no_powerup` + `NPCTicker()`      | o re-pino do `TicarUmCorpo`   |
///   | o visitante ao lado           | `oview(1)` + `datum/mind_session`    | <see cref="GameServer.MenteAoLado"/> |
///   | ferida nao volta              | `mind_snapshot`/`mind_restore`       | <see cref="FotoDaMente"/>     |
///   | ganho reduzido                | `mind_gain_mult()`                   | `RitmoDaZona` (0,25 no funil) |
///   | sem Zenkai                    | -- (nao existe la)                    | o gate do `AoPerderALuta`     |
///   | chefe ja visto                | -- (nao existe la)                    | <see cref="GameServer.ChamarChefe"/> |
///
/// ============================ A SESSAO DO DM NAO FOI PORTADA -- ELA E A ZONA ============================
/// O original tem um `datum/mind_session` com `members`, `cell`, `zlevel`, `cx/cy` e um alocador de
/// celulas de 100x100 num z-level construido a mao, mais `mind_alloc_cell`/`mind_free_cell` e um teto
/// de oito mentes simultaneas. Nada disso existe aqui, e o motivo e a Camada 1: a mente e uma
/// `ZoneKey.Interior("Interdimension", id)`, e **o hash da chave ja e o corte de interesse**. Duas
/// pessoas na mesma mente sao duas pessoas com a mesma chave; oito mentes sao oito chaves; a nona nao
/// precisa esperar celula nenhuma.
///
/// E os "membros" tambem sao derivados -- sao os corpos daquela `ZoneList`. Um campo `members` seria
/// uma segunda lista que precisa concordar com a primeira, e o dia em que discordasse alguem ficaria
/// preso num bolso que "nao tem ninguem".
/// ==================================================================================================
///
/// ============================ NAO SE ZERA O `Ficha.med` NA ENTRADA ============================
/// O DM zera (`med = 0`, `:398`) porque la o corpo que fica e um mob **separado** com o `icon_state`
/// escrito a mao. Aqui o corpo que fica **e o seu**, com a mesma ficha, e a pose sai de
/// `ServerPlayer.Pose()` -- que le `Ficha.med`. Zerar aqui poria um corpo de pe no lugar do corpo
/// meditando, que e exatamente o que o dono pediu pra ver.
///
/// E o `med` ligado nao vira mais ganho de graca: e o proprio `Treinar` que agora rende 0,25x dentro
/// da mente, pelo funil de ritmo de zona. O portao ficou onde o ganho e cobrado, e nao num campo que
/// tambem desenha o corpo.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Onde o corpo nasce dentro da dimensao mental (centro do mapa Interdimension).</summary>
	private static readonly Vec2 PosMental = new(250 * 32 + 16, 250 * 32 + 16);

	/// <summary>
	/// A que distancia o OPONENTE aparece. Perto o bastante pra a luta comecar sozinha.
	///
	/// Vale pro reflexo, pro chefe convocado E pro visitante -- e o visitante usar o mesmo ponto nao
	/// e economia: ele **toma o lugar** do corpo que a mente tinha erguido (ver
	/// <see cref="EntrarNaMente"/>).
	/// </summary>
	private const float DistanciaDoOponente = 96f;

	/// <summary>
	/// QUAO PERTO PRECISA ESTAR O CORPO DE QUEM JA ESTA EM TRANSE. E o `oview(1, src)` do
	/// `mind_enter` (`MindMeditate.dm:361`) -- "o tile do lado".
	///
	/// UM TILE E MEIO e nao um, e a razao e a mesma que o Cell ja tinha pra encostar num companheiro
	/// caido (`GameServer.Sagas.AbsorverCompanheiroCaido`): a posicao aqui e CONTINUA e nao uma grade.
	/// Dois corpos em tiles adjacentes na diagonal estao a ~45 px de centro a centro, e exigir 32
	/// faria "sentar do lado" depender de meio pixel.
	/// </summary>
	private const float PertoParaEntrarJunto = 48f;

	/// <summary>
	/// O contador de LUGARES dos chefes convocados na mente -- o terceiro argumento da semente de
	/// NPC, como no povoamento e nas sagas. Comeca acima da faixa das sagas (5.000.000) pra as
	/// fichas nao colidirem, e **nao persiste**: um chefe de treino nao atravessa reinicio.
	/// </summary>
	private ulong _lugarDaMente = 6_000_000;

	/// <summary>
	/// ESTE CORPO ESTA DENTRO DE UMA MENTE? A pergunta e do LUGAR, e nao de um campo -- ver o
	/// cabecalho de `Core/World/DimensaoMental.cs`.
	///
	/// Ela substituiu `pl.CloneId != 0` em quatro pontos (a saida da mente, a volta pro corpo, a
	/// decolagem e o rasgo do G4). Aquela pergunta era certa enquanto "estar na mente" e "ter um
	/// reflexo" eram a mesma coisa; com o visitante -- que entra na mente do outro e por isso nao tem
	/// reflexo nenhum -- ela passa a responder NAO pra quem esta la dentro.
	/// </summary>
	private static bool NaMente(ServerPlayer pl) => DimensaoMental.EhAMente(pl.Zone);

	// =====================================================================
	// A PORTA
	// =====================================================================
	/// <summary>
	/// ENTRA NA MENTE -- na propria, ou na de quem esta meditando do seu lado.
	///
	/// So meditando: e a condicao do original, e ela faz sentido -- e um lugar pra onde se vai por
	/// dentro, nao um mapa que se atravessa.
	/// </summary>
	private void EntrarNaMente(ServerPlayer pl)
	{
		if (pl.Peer == null) return;                       // clone nao medita

		// NAO HA GUARDA DE POSSESSAO AQUI, e e de proposito. Meditar ficou fora do `ComandoDeCorpo`
		// (e a saida da fera), mas a PORTA da mente entra pelo canal `C2S.Habilidade`, que esta na
		// lista -- entao o possuido ja e recusado no despacho e nunca chega nesta funcao. Um
		// `if (SemAsRedeas(pl))` aqui seria a mesma verdade escrita duas vezes, e a segunda copia e
		// sempre a que discorda um dia. (O `LargarOCorpo` recusa de novo, e ai por escrito.)

		if (NaMente(pl)) { Avisar(pl, "voce ja esta em transe."); return; }
		if (!pl.Ficha.med) { Avisar(pl, "e preciso estar MEDITANDO (tecla M) pra mergulhar na mente."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "agora nao."); return; }

		// ============================ O VISITANTE: A MENTE E A DE QUEM ESTA AO LADO ============================
		// *"se um player meditasse AO SEU LADO ele entraria na sua mente e poderia lutar com vc."*
		//
		// E o `for(var/mob/npc/mind_dummy/D in oview(1, src)) if(D.session && D.session.active)` do
		// `mind_enter` (`MindMeditate.dm:361-364`), e o encaixe aqui e o mesmo: o que se procura no chao
		// e o CORPO LARGADO de alguem -- que so existe porque a Camada 1 deixou de fazer o meditador
		// sumir do mapa. Sem aquele boneco nao ha em que reparar, e o visitante seria impossivel.
		// ==================================================================================================
		ServerPlayer? vizinho = MenteAoLado(pl);

		// ============================ A MENTE E A ZONA DELE, E NAO `De(id dele)` ============================
		// Parece a mesma coisa e nao e: quem esta ao seu lado pode ser um VISITANTE -- alguem que ja
		// entrou na mente de um terceiro. `De(vizinho.Id)` daria o bolso VAZIO do vizinho, e o segundo
		// visitante cairia sozinho num quarto branco achando que tinha entrado na roda.
		//
		// A zona onde ele esta e a resposta certa em todos os casos, inclusive no dele proprio.
		// ================================================================================================
		bool visitando = vizinho != null;
		ZoneKey mente = vizinho?.Zone ?? DimensaoMental.De(pl.Id);

		// O CORPO NAO VEM JUNTO -- o mecanismo unico da Camada 1 (`GameServer.CorpoLargado.cs`), o
		// mesmo que o leme da nave grande usa. Ele deixa o corpo aqui, com a MESMA ficha (entao a pose
		// de meditar sai sozinha), e so entao viaja. Falso = recusado, e ai nada aconteceu.
		if (!LargarOCorpo(pl, mente, visitando ? PosMental + new Vec2(DistanciaDoOponente, 0) : PosMental))
			return;

		// ============================ A FOTO DO CORPO, E ELA E A REGRA 1 ============================
		// *"la dentro os FERIMENTOS NAO SAO TRANSFERIDOS pro corpo real"*. Ver `FotografarParaAMente`.
		//
		// DEPOIS de largar o corpo e nao antes, e nao e detalhe: `LargarOCorpo` pode RECUSAR (possuido,
		// ja fora do corpo), e uma foto tirada antes ficaria pendurada pra sempre num corpo que nunca
		// entrou -- pronta pra "restaurar" a vida de dez minutos atras na proxima saida de qualquer
		// coisa.
		// =====================================================================================
		FotografarParaAMente(pl);

		if (visitando)
		{
			// ============================ DOIS NA MENTE: SEM NPC ============================
			// *"DOIS jogadores meditando lado a lado entram na MESMA dimensao -- sem NPC: so eles,
			// podendo lutar entre si"* -- o cabecalho do proprio `MindMeditate.dm` (linhas 10-11), e o
			// `add_member` executa isso deletando o clone quando o segundo entra (`:201-204`).
			//
			// QUEM DESFAZ E O DONO DA MENTE e nao o vizinho de quem se veio: so ele pode ter erguido
			// alguma coisa ali (ver `ChamarChefe`), e num bolso com tres pessoas o vizinho pode nao ser
			// ele. O dono sai da propria chave da zona -- nao ha campo pra consultar.
			//
			// O visitante nasce EXATAMENTE onde o reflexo estava, e isso e o desenho e nao economia: ele
			// toma o lugar dele. Quem estava treinando contra si mesmo continua olhando pro mesmo ponto
			// da tela, e quem aparece la e uma pessoa.
			// =========================================================================
			ServerPlayer? dono = _players.GetValueOrDefault(DimensaoMental.Anfitriao(mente));
			if (dono != null)
				DesfazerOOponente(dono, "o seu reflexo se desfaz -- outra consciencia ocupa o lugar dele.");

			// AVISA QUEM JA ESTAVA LA -- todos, e nao so o vizinho: com tres pessoas no bolso, contar
			// so pra uma seria a mesma "linha nova em dois dos tres lugares" de sempre.
			foreach (ServerPlayer o in ZoneList(mente.Hash))
				if (o != pl && o.Peer != null) Avisar(o, $"outra consciencia adentra a sua mente... e {pl.Name}.");

			string deQuem = dono != null ? $"na mente de {dono.Name}" : "numa mente que nao e a sua";
			Avisar(pl, $"voce mergulha {deQuem}. Aqui nada e real -- ferimentos nao voltam com voce.");
			GD.Print($"[server] {pl.Name} entrou {deQuem}");
			return;
		}

		ServerPlayer clone = CriarClone(pl, mente);
		pl.CloneId = clone.Id;

		Avisar(pl, "voce fecha os olhos. Diante de voce, VOCE -- e ele conhece cada golpe seu.");
		GD.Print($"[server] {pl.Name} entrou na propria mente (clone id {clone.Id}, BP {clone.Ficha.BP:N0})");
	}

	/// <summary>
	/// QUEM ESTA EM TRANSE DO MEU LADO -- devolve a PESSOA (que esta la dentro), e nao o corpo largado
	/// dela que esta aqui no chao. Quem chama usa a ZONA dessa pessoa como destino, e nao o id dela --
	/// ver o bloco do visitante em <see cref="EntrarNaMente"/>.
	///
	/// TRES PERGUNTAS, e a terceira e a que separa este sistema do outro que usa o mesmo boneco: o
	/// corpo largado ao seu lado pode ser de alguem que esta **pilotando uma nave**, e nesse caso nao
	/// ha mente nenhuma pra visitar. `NaMente(dono)` responde isso pelo LUGAR onde o dono esta -- e e
	/// exatamente o `D.session && D.session.active` do original, que tambem so aceita o dummy de
	/// meditacao (o do leme tem `wake_mode = 2` e `session` nulo).
	/// </summary>
	private ServerPlayer? MenteAoLado(ServerPlayer pl)
	{
		float perto2 = PertoParaEntrarJunto * PertoParaEntrarJunto;

		foreach (ServerPlayer b in ZoneList(pl.Zone.Hash))
		{
			if (b == pl || b.DonoDoCorpoLargado == 0) continue;
			if ((b.Pos - pl.Pos).LengthSquared > perto2) continue;
			if (!_players.TryGetValue(b.DonoDoCorpoLargado, out ServerPlayer? dono)) continue;
			if (!NaMente(dono)) continue;   // ele largou o corpo pro LEME, e nao pra mente
			return dono;
		}
		return null;
	}

	/// <summary>
	/// SAI DA MENTE. Chamado pelo jogador, pela MORTE do oponente (e nao pela queda dele -- caido, ele
	/// se reergue: ver `DerrubadoNaMente`), por um golpe no corpo la fora
	/// (<see cref="VoltarDeOndeEstiver"/>), pela morte, pelo nocaute e pelo logout.
	///
	/// A PERGUNTA DE ENTRADA E O LUGAR e nao o `CloneId`: o visitante nunca teve clone, e o anfitriao
	/// perde o dele no instante em que o visitante chega. Com a pergunta antiga, os dois teriam saido
	/// por aqui sem que nada acontecesse -- e o `VoltarDeOndeEstiver` os teria mandado pro ramo do
	/// leme de nave.
	/// </summary>
	private void SairDaMente(ServerPlayer pl, string motivo)
	{
		if (!NaMente(pl)) return;

		DesfazerOOponente(pl, "");
		ZoneKey mente = pl.Zone;

		// A REGRA 1, COBRADA AQUI: o corpo volta EXATAMENTE como entrou. Antes do `VoltarProCorpo`
		// porque ele troca de zona -- e o `TrocarFeridas`/`MandarCorpo` que saem de la tem que
		// descrever o corpo ja restaurado, senao a zona de fora ve o sangue de uma luta que nao
		// aconteceu ate o proximo tique consertar.
		RestaurarDaMente(pl);

		// VOLTA PRO CORPO -- pra ONDE ELE ESTIVER, e nao pra onde ele foi deixado (ele pode ter sido
		// arremessado no meio da meditacao). Sem boneco nenhum, o mecanismo cai na Terra em vez de
		// deixar alguem preso no bolso. Ver `GameServer.CorpoLargado.cs`.
		VoltarProCorpo(pl, motivo);

		// QUEM FICOU NA MENTE PRECISA SABER -- o `remove_member` (`MindMeditate.dm:211-215`). Depois da
		// viagem de proposito: `MoveToZone` ja tirou este corpo da lista da mente, entao o laco fala
		// exatamente com quem sobrou.
		foreach (ServerPlayer o in ZoneList(mente.Hash))
			if (o.Peer != null) Avisar(o, $"a presenca de {pl.Name} se desfaz da sua mente...");
	}

	/// <summary>
	/// DESFAZ O CORPO QUE ESTA MENTE ERGUEU -- o reflexo ou o chefe convocado, o que houver.
	///
	/// UMA CASA SO porque sao tres os motivos de ele sumir (a saida, a chegada de um visitante, a
	/// convocacao de outro chefe) e porque o campo e um so (<see cref="ServerPlayer.CloneId"/>): tres
	/// lugares zerando o mesmo campo seria o terceiro esquecendo, e o efeito seria um corpo dirigido
	/// parado num bolso pra sempre.
	/// </summary>
	private void DesfazerOOponente(ServerPlayer dono, string aviso)
	{
		if (dono.CloneId == 0) return;
		if (_players.TryGetValue(dono.CloneId, out ServerPlayer? corpo)) RemoverNpc(corpo);
		dono.CloneId = 0;
		if (aviso.Length > 0) Avisar(dono, aviso);
	}

	// =====================================================================
	// REGRA 1 -- A FERIDA NAO VOLTA PRO CORPO REAL
	// =====================================================================
	/// <summary>
	/// FOTOGRAFA O CORPO NA ENTRADA -- `mind_snapshot()` (`MindMeditate.dm:332-339`).
	///
	/// ============================ POR QUE PRECISA DE FOTO, SE O DANO E "DE MENTIRA" ============================
	/// Porque nesta arquitetura ele **e de verdade enquanto dura**. A Camada 1 fez o corpo largado
	/// compartilhar a `Fighter` e o `CombatState` do dono -- e daquela decisao saiu de graca que bater
	/// no corpo meditando fere a pessoa. O preco simetrico e este: apanhar DENTRO da mente tambem fere
	/// o corpo la fora, porque nao ha dois corpos.
	///
	/// A alternativa seria dar ao jogador um segundo `CombatState` ao entrar -- e ai a mente teria uma
	/// fisica propria, membros proprios e um segundo `NegarMorte`: exatamente o "atalho paralelo" que
	/// este port recusa. Com a foto, a luta na mente roda pelo MESMO resolvedor de golpe do mundo (e
	/// portanto com as mesmas regras de aparo, esquiva, quebra e Zanzo Clash) e o que a desfaz e uma
	/// funcao de restauro na saida.
	///
	/// E ELA TEM UM EFEITO VISIVEL QUE E DESEJADO: enquanto voce apanha la dentro, quem esta do lado do
	/// seu corpo VE o estrago. O corpo em transe e vulneravel de verdade -- e e por isso que levar um
	/// nocaute na mente EXPULSA -- o `MIND_KO_EJECT` do DM, coberto pelo `BordasDeQuemEstaFora`. **Do
	/// define so o GATILHO foi portado**: la ha 5 s de tolerancia (50 tiques caido dentro da mente antes
	/// da expulsao, `MindMeditate.dm:453-462`) e aqui a volta e imediata. O atraso e uma decisao do dono
	/// que ainda nao foi tomada, e nao um esquecimento.
	/// ======================================================================================================
	/// </summary>
	private static void FotografarParaAMente(ServerPlayer pl)
	{
		var f = new FotoDaMente { Ki = pl.Ficha.Ki, Vigor = pl.Ficha.stamina, KO = pl.Ficha.KO, Morto = pl.Ficha.dead };
		foreach (BodyPart p in pl.Combate.Corpo.Partes) f.Membros[p.Nome] = (p.Vida, p.Decepado);
		pl.FotoDaMente = f;
	}

	/// <summary>
	/// DEVOLVE O CORPO EXATAMENTE COMO ENTROU -- `mind_restore()` (`:341-351`).
	///
	/// O QUE **NAO** VOLTA, e cada ausencia e uma decisao:
	///   * o BP ganho la dentro (e real, so que a um quarto -- a regra 3);
	///   * a FORMA em que a pessoa esta (o DM tambem nao mexe: transformar-se e um ato, nao um
	///     ferimento);
	///   * o HP, que nao e guardado porque e derivado dos membros (ver <see cref="FotoDaMente"/>).
	///
	/// A ORDEM importa em um ponto: `SincronizarVida` roda DEPOIS dos membros, senao o HP seria a
	/// media do corpo ferido. E o KO/morte sao escritos direto em vez de por `Levantar()`/`Reviver()`:
	/// aqueles dois **curam** (o `Levantar` empurra todo nucleo pra 30% da vida, o `Reviver` restaura o
	/// corpo inteiro), e curar aqui apagaria a ferida com que a pessoa ENTROU.
	/// </summary>
	private void RestaurarDaMente(ServerPlayer pl)
	{
		if (pl.FotoDaMente is not { } f) return;
		pl.FotoDaMente = null;

		foreach (BodyPart p in pl.Combate.Corpo.Partes)
		{
			if (!f.Membros.TryGetValue(p.Nome, out (double Vida, bool Decepado) antes)) continue;
			p.Decepado = antes.Decepado;
			p.Vida = Math.Clamp(antes.Vida, 0, p.VidaMax);
		}

		pl.Ficha.dead = f.Morto;
		pl.Ficha.KO = f.KO;
		if (!f.KO) pl.Combate.NocauteRestante = 0;   // senao o cronometro do KO desfeito ainda correria
		pl.Ficha.Ki = Math.Min(f.Ki, pl.Ficha.MaxKi);
		pl.Ficha.stamina = Math.Min(f.Vigor, pl.Ficha.maxstamina);

		pl.Combate.SincronizarVida();
		AjustarGanhoDoRabo(pl);
		MandarFicha(pl);   // 5 Hz seria tarde: a barra de vida ficaria vermelha por dois quadros
	}

	// =====================================================================
	// OS CHEFES QUE VOCE JA VIU
	// =====================================================================
	/// <summary>
	/// "EU VI ESTE CHEFE" -- chamado do laco do <see cref="TickDoRoteiro"/>, uma vez por segundo, por
	/// corpo com ficha de chefe.
	///
	/// ============================ POR QUE ALI, E NAO NO MONITOR DA SAGA ============================
	/// Porque o monitor de saga so conhece os chefes que **a cadeia** poe no mundo: um chefe nascido
	/// pelo verb de admin, ou pela bancada, ou por um evento futuro, nao passaria por la -- e o jogador
	/// que enfrentou o Cell num teste do dono nao teria como enfrenta-lo na mente depois. O
	/// `TickDoRoteiro` ja varre `p.Papel is { EhChefe: true }` no `_players` inteiro: e o funil de
	/// "todo corpo com ficha pronta que existe agora", independente de quem o pos ali.
	///
	/// De quebra ele resolve a pergunta na direcao certa: "ja viu" e uma propriedade do CORPO que
	/// esta na sua frente, nao do estado de um evento.
	/// ==========================================================================================
	///
	/// O RAIO E O DA VISTA (22 tiles, o mesmo do chat e do luto), e ele e a metade que importa: sem
	/// ele, "eu vi Freeza" viraria "eu estava no mesmo planeta que Freeza" e o portao nao gatearia
	/// nada -- o "teto que nunca dispara" que este port ja nomeou.
	///
	/// **CHEFE DENTRO DE UMA MENTE NAO CONTA.** Ele e uma lembranca, e uma lembranca nao vira
	/// lembranca de si mesma; sem esta linha o visitante da sua mente sairia de la achando que
	/// enfrentou o chefe de verdade.
	/// </summary>
	private void AnotarChefesVistos(ServerPlayer chefe)
	{
		if (chefe.Papel is not { Molde.Id.Length: > 0 } papel) return;
		if (NaMente(chefe)) return;

		float raio2 = RaioDaVista * RaioDaVista;
		foreach (ServerPlayer o in ZoneList(chefe.Zone.Hash))
		{
			// SO PESSOA ANOTA, e a pergunta e `EhPessoa` (tem assinatura de conta) e nao `Peer != null`.
			// As duas quase sempre concordam, e a diferenca importa nas duas pontas: um NPC nao tem save
			// pra onde levar a lembranca, e um corpo de bancada (que tem conta e nao tem tela) precisa
			// poder ser medido pelo caminho de producao -- ver `--menteteste`.
			if (!EhPessoa(o)) continue;
			if ((o.Pos - chefe.Pos).LengthSquared > raio2) continue;
			if (!o.ChefesVistos.Add(papel.Molde.Id)) continue;

			Avisar(o, $"voce grava {chefe.Name} na memoria -- na sua mente, podera enfrenta-lo de novo.");
			MandarChefesDaMente(o);
			GD.Print($"[server] {o.Name} viu o chefe '{papel.Molde.Id}' ({chefe.Name})");
		}
	}

	/// <summary>
	/// CONVOCA UM CHEFE JA VISTO PRA UMA LUTA SIMULADA. E o `mente_chefe:&lt;molde&gt;` do canal de
	/// habilidade.
	///
	/// ============================ E O MESMO NASCIMENTO DAS SAGAS, DE PROPOSITO ============================
	/// `NascerNpc` + `BpsPinados` + `EntrarNoDegrau` sao literalmente os quatro passos do
	/// <see cref="NascerChefeDaSaga"/>, e nao uma fabrica de "chefe de treino". A ficha PRONTA do chefe
	/// (os degraus, o gatilho por membro, a forma, o sprite, o BP de cada degrau) e o que se instancia
	/// -- entao o Freeza da sua mente transforma pelas mesmas regras, com o mesmo roteiro
	/// (`TickDoRoteiro`), e a luta simulada mede a luta de verdade.
	///
	/// O QUE ELE **NAO** GANHA e o que e da CADEIA e nao do corpo: ele nao entra em `EstadoDoChefe`
	/// nenhum, entao nao ha saga pra ele encerrar, ultimato pra correr, recompensa pra pagar nem
	/// planeta pra condenar. O monitor de sagas so conhece corpos por `ec.Corpo`, e este id nao esta
	/// em lista nenhuma.
	/// ==================================================================================================
	/// </summary>
	private void ChamarChefe(ServerPlayer pl, string moldeId)
	{
		if (!NaMente(pl)) { Avisar(pl, "isso so acontece dentro da sua mente."); return; }

		// **SO NA PROPRIA MENTE.** As lembrancas sao de quem sonha: um visitante nao convoca chefe na
		// cabeca dos outros. Derivado do anfitriao da zona -- nao ha campo dizendo "esta mente e minha".
		if (DimensaoMental.Anfitriao(pl.Zone) != pl.Id)
		{
			Avisar(pl, "esta mente nao e a sua -- aqui quem chama as lembrancas e o dono dela.");
			return;
		}

		// ============================ DOIS NA MENTE: SEM NPC (a mesma regra da entrada) ============================
		// `add_member` deleta o clone quando o segundo entra e nunca mais o recria enquanto houver dois.
		// Deixar convocar um chefe com visita seria a mesma cena que aquela linha existe pra impedir --
		// so que com o chefe do lado de um dos dois.
		//
		// "OUTRA PESSOA" e `EhPessoa` e nao `Peer != null`: o que nao pode estar na mente e alguem com
		// ficha propria -- e o corpo que a mente ergueu (reflexo ou lembranca) nao tem conta nenhuma,
		// entao ele nunca se conta a si mesmo como visita.
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl && EhPessoa(o))
			{
				Avisar(pl, $"{o.Name} esta na sua mente -- as lembrancas nao se erguem com visita.");
				return;
			}

		if (!pl.ChefesVistos.Contains(moldeId)) { Avisar(pl, "voce nunca viu esse inimigo."); return; }

		MoldeDeNpc? molde = _moldes?.Get(moldeId);
		if (molde is not { EhChefe: true }) { Avisar(pl, "essa lembranca ja nao existe."); return; }

		// O QUE ESTIVER DE PE SAI ANTES: o reflexo, ou o chefe anterior. Um por vez -- ver
		// `ServerPlayer.CloneId`.
		DesfazerOOponente(pl, "");

		ulong lugar = ++_lugarDaMente;
		ServerPlayer? chefe = NascerNpc(moldeId, pl.Zone, pl.Pos + new Vec2(DistanciaDoOponente, 0), lugar);
		if (chefe?.Papel == null) { Avisar(pl, "a lembranca nao se formou."); return; }

		// O BP PINADO E O DEGRAU, os dois antes de qualquer outra coisa tocar a ficha -- a mesma ordem
		// do nascimento de saga. `Pinar` com a media de agora: a lembranca e do inimigo, mas a
		// dificuldade e do servidor de hoje, que e o que o proprio `bpRelativo` do molde pede.
		chefe.Papel.BpsPinados = Sagas.Pinar(molde, MediaDoServidor());
		chefe.Papel.Estagio = 0;
		EntrarNoDegrau(chefe, cura: 0, anunciar: false);

		// ============================ AS DUAS LINHAS QUE FAZEM DELE UMA LEMBRANCA ============================
		//   * `DonoDoClone` o poe no ramo do REFLEXO no `TicarUmCorpo`: ele vem atras de VOCE, some se
		//     voce sair do bolso, **se reergue 15 s depois de cair** (o `clone_watch`) e so a MORTE
		//     dele encerra o transe. Sem isso ele cairia no ramo de chefe comum (`PresaDaFera`), que
		//     caca o corpo mais proximo -- e o corpo mais proximo do mundo, pra ele, seria voce de
		//     qualquer jeito, mas nem a queda nem a morte dele significariam nada;
		//   * `Letal = false` e a mesma linha do clone: a mente nao decepa membro nem mata. E treino.
		// ================================================================================================
		chefe.DonoDoClone = pl.Id;
		chefe.Combate.Letal = false;
		pl.CloneId = chefe.Id;

		Avisar(pl, $"a lembranca toma forma: {chefe.Name} esta diante de voce.");
		GD.Print($"[server] {pl.Name} convocou '{moldeId}' na mente (id {chefe.Id}, BP {chefe.Ficha.BP:N0})");
	}

	/// <summary>
	/// A LISTA DE CHEFES VISTOS, pro menu. Um botao por lembranca -- ver `Client/Habilidades.cs`.
	///
	/// O NOME VIAJA JUNTO porque o cliente nao le o `npcs.json`: ele conhece o que pode apertar, e
	/// conhece pelo pacote. Mesma decisao (e mesma frase) do catalogo de obras.
	///
	/// Molde que sumiu do arquivo NAO e enviado -- e tambem nao e apagado do `HashSet`: o dono pode
	/// estar so renomeando uma saga, e "voce viu o Cell" nao deixa de ser verdade porque a linha do
	/// JSON mudou de lugar. Quem recusa de verdade e o <see cref="ChamarChefe"/>.
	/// </summary>
	private void MandarChefesDaMente(ServerPlayer pl)
	{
		if (pl.Peer is not { } peer || _moldes == null) return;

		var vistos = new List<MoldeDeNpc>();
		foreach (string id in pl.ChefesVistos)
			if (_moldes.Get(id) is { EhChefe: true } m) vistos.Add(m);
		vistos.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

		var w = Protocol.Begin(Protocol.S2C.MenteChefes);
		w.Put((byte)Math.Min(vistos.Count, byte.MaxValue));
		foreach (MoldeDeNpc m in vistos.Take(byte.MaxValue))
		{
			w.Put(m.Id);
			w.Put(m.Nome.Length > 0 ? m.Nome : m.Id);
		}
		peer.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}
}
