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
	/// <summary>
	/// ONDE O CORPO NASCE DENTRO DA DIMENSAO MENTAL -- `locate(cx-3, cy, mz)`
	/// (`MindMeditate.dm:196-198`), tres tiles a oeste do centro do quarto branco.
	///
	/// ELE ERA (250,250) EM PIXEL CRU, e isso era o defeito que o dono relatou: aquele numero era o
	/// meio do **z24 do BYOND**, o mapa que o catalogo devolvia pelo nome "Interdimension" -- mosaico
	/// azul e nebulosa roxa, *"um LUGAR NADA A VER"*. Agora a mente tem planta propria
	/// (`DimensaoMental.Planta`) e a coordenada sai DELA, e nao de um numero escrito a mao: mudar o
	/// tamanho do quarto move o nascimento junto.
	/// </summary>
	private static readonly Vec2 PosDaPlanta = DimensaoMental.PixelDe(DimensaoMental.CelDeQuemMedita);

	/// <summary>
	/// O MEIO DO z24 DO BYOND, em pixel cru -- o endereco que o dono viu. So vale com
	/// <see cref="MenteAntiga"/>.
	/// </summary>
	private static readonly Vec2 PosDoZ24 = new(250 * 32 + 16, 250 * 32 + 16);

	private static Vec2 PosMental => MenteAntiga ? PosDoZ24 : PosDaPlanta;

	/// <summary>
	/// `--menteantiga`: **O MUNDO DE ANTES DO CONSERTO, DE PROPOSITO.**
	///
	/// ============================ ISTO E O `--semduro`, E PELO MESMO ARGUMENTO ============================
	/// A unica prova honesta de que este conserto conserta e mostrar o lugar SEM ele -- e a foto do
	/// "antes" nao da pra tirar com a bancada: ela precisa de tela, e o que se compara sao dois
	/// PIXELS, nao dois numeros. Fazer essa foto apagando o `z24_Interdimension.scn` do disco seria
	/// destruir o dado pra medir.
	///
	/// A chave desliga as DUAS linhas do conserto de uma vez, que e como o defeito existia:
	///   * a planta sai do funil (`MapaDaMente` devolve nulo), e o catalogo volta a casar
	///     "Interdimension" com o z24 do BYOND, pelo NOME;
	///   * o nascimento volta ao literal (250,250) em pixel cru -- o meio daquele mapa, bem na emenda
	///     entre o mosaico azul-petroleo e a nebulosa roxa.
	///
	/// **O CLIENTE PRECISA DA MESMA CHAVE** (`Client/World.cs` tem o ramo gemeo): so no servidor, o
	/// corpo andaria contra a colisao do z24 e a tela desenharia o quarto branco. A foto sairia
	/// mentindo, que e pior que nao ter foto.
	///
	/// Ela e de BANCADA e GRITA: uma partida de verdade rodando assim tem que aparecer no log.
	/// ================================================================================================
	/// </summary>
	public static bool MenteAntiga;

	/// <summary>
	/// A COLISAO DA MENTE -- a mesma pra toda mente, como a da nave.
	///
	/// Chamada pelo <see cref="MapaDaZonaOuCatalogo"/>, o funil unico de "onde ha parede nesta zona".
	/// Sem esta linha a mente cairia no `_catalogo?.Get(zona)?.Mapa`, que resolve pelo NOME e
	/// devolveria a colisao do z24 -- 500x500 de um mapa que ninguem mais desenha. O corpo esbarraria
	/// em paredes invisiveis e passaria pelas de verdade.
	/// </summary>
	private static ZoneCollision? MapaDaMente(ZoneKey zona) =>
		!MenteAntiga && DimensaoMental.EhAMente(zona) ? DimensaoMental.Planta().Colisao : null;

	/// <summary>
	/// A que distancia o OPONENTE aparece. Perto o bastante pra a luta comecar sozinha.
	///
	/// Vale pro reflexo, pro chefe convocado E pro visitante -- e o visitante usar o mesmo ponto nao
	/// e economia: ele **toma o lugar** do corpo que a mente tinha erguido (ver
	/// <see cref="EntrarNaMente"/>).
	/// </summary>
	private const float DistanciaDoOponente = 96f;

	/// <summary>De quanto em quanto tempo a coleira pode FALAR. Ver `ServerPlayer.ColeiraCaladaAte`.</summary>
	private const long MsEntreAvisosDaColeira = 20_000;

	/// <summary>
	/// O REFLEXO REAPARECE NA FRENTE DE QUEM FUGIU -- o que substituiu a parede da mente.
	///
	/// ============================ ELE APARECE NA FRENTE, E NAO ATRAS ============================
	/// A `Facing` do dono e o rumo da FUGA (ela e escrita pelo proprio movimento), entao por o corpo a
	/// <see cref="DistanciaDoOponente"/> nessa direcao o poe **cortando o caminho** -- que e a unica
	/// colocacao que fecha o combate. Reaparecer ATRAS seria a perseguicao recomecando do zero: o
	/// jogador voltaria a abrir distancia no mesmo instante e a coleira dispararia de novo daqui a
	/// quarenta tiles, pra sempre, com o reflexo virando um efeito sonoro nas costas dele.
	///
	/// E sao os MESMOS 96 px da entrada na mente (e da chegada do visitante, e da convocacao do
	/// chefe): "o oponente esta a tres tiles" e uma frase so deste lugar, e reaparecer a uma distancia
	/// PROPRIA seria a segunda regra sobre onde um oponente da mente fica.
	///
	/// ============================ NAO HA O QUE CONSULTAR ANTES DE POR ============================
	/// Em toda outra zona por um corpo num ponto exige perguntar se ali cabe (`PontoLivrePerto`). Aqui
	/// **nao ha uma celula densa em lugar nenhum** (ver `DimensaoMental.Planta`), e essa e a metade boa
	/// de nao ter parede: qualquer coordenada serve.
	/// ======================================================================================
	/// </summary>
	private void ReaparecerNaFrente(ServerPlayer npc, ServerPlayer dono)
	{
		npc.Pos = dono.Pos + MeleeArea.Frente(dono.Facing) * DistanciaDoOponente;
		npc.Moving = false;   // o passo que ele estava dando era pra um lugar que nao existe mais

		// O CLIENTE JA CRAVA ESTE SALTO sem interpolar (`RemotePlayer.LimiteDeSalto`, 3 tiles): a tela
		// mostra um reflexo que APARECE, e nao um corpo atravessando a distancia deslizando.
		long agora = NowMs();
		if (agora < npc.ColeiraCaladaAte) return;
		npc.ColeiraCaladaAte = agora + MsEntreAvisosDaColeira;

		AvisarNaMente(npc.Zone, npc.Papel is { EhChefe: true }
			? $"{npc.Name} ja esta a sua frente. Aqui dentro nao ha pra onde correr."
			: "o seu reflexo ja esta a sua frente. Nao se corre mais rapido que a propria mente.");
	}

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
	/// POR QUE ESTA PESSOA NAO MERGULHA AGORA -- a frase pronta, ou "" quando pode. **UMA LISTA SO**,
	/// e agora ela tem TRES leitores em vez de dois:
	///
	///   * a TELINHA do meditar, que apaga o botao antes do clique (pela metade que mora no Core);
	///   * o comeco da onda (<see cref="ComecarOMergulho"/>), que recusa NA HORA -- ninguem deve
	///     esperar 1,8 s de ondulacao pra ouvir "um morto nao mergulha";
	///   * o fim da onda (<see cref="EntrarNaMente"/>), que e a autoridade de verdade.
	///
	/// E o terceiro leitor e o que faz o pedido *"interrompeu, a onda morre"* funcionar sem bit
	/// nenhum: o <see cref="TickDaOndaDaMente"/> reavalia esta funcao TODO TIQUE enquanto a onda
	/// corre. Quem e nocauteado, morre, para de meditar ou anda no meio da ondulacao ja e recusado
	/// aqui -- nao ha estado "estou viajando" pra alguem lembrar de apagar em cinco lugares.
	///
	/// AS TRES PRIMEIRAS SAO DO CORE (`DimensaoMental.PorQueNaoMergulhar`) porque a telinha precisa
	/// delas sem falar com o servidor; a quarta e so daqui, e a razao esta escrita nela.
	/// </summary>
	private string RecusaDoMergulho(ServerPlayer pl)
	{
		if (DimensaoMental.PorQueNaoMergulhar(NaMente(pl), pl.Ficha.KO, pl.Ficha.dead) is { Length: > 0 } m)
			return m;

		// ESTA NAO TEM PAR NO CLIENTE, e de proposito: ela nao e sobre o ESTADO do corpo, e sim sobre
		// a ORDEM dos pacotes -- a telinha manda a atividade (`C2S.Activity`) e a habilidade
		// (`C2S.Habilidade`) no MESMO canal confiavel e ordenado, entao quando o `case` roda o `med`
		// ja esta escrito. Ela e a rede pra quem chegar por outro caminho (um verb velho, uma
		// bancada, um cliente remendado).
		//
		// E ELA GANHOU UM SEGUNDO EMPREGO COM A ONDA: quem ANDA durante a ondulacao deixa de meditar
		// (o cliente manda `Parado`, o servidor zera o `med`), e o mergulho se cancela sozinho. Nao
		// foi preciso escrever "andar cancela": ja estava dito.
		if (!pl.Ficha.med) return "e preciso estar MEDITANDO (tecla M) pra mergulhar na mente.";

		return "";
	}

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

		// A LISTA INTEIRA DE RECUSAS ESTA NO <see cref="RecusaDoMergulho"/>, e ela e cobrada DUAS
		// vezes: uma quando a onda comeca e outra aqui, quando ela acaba. Ver o cabecalho de la.
		if (RecusaDoMergulho(pl) is { Length: > 0 } motivo) { Avisar(pl, motivo); return; }

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
	// A GOTA -- A TELA ONDULA, E SO ENTAO SE VIAJA
	// =====================================================================
	/// <summary>
	/// UMA VIAGEM DA MENTE ESPERANDO A ONDA ACABAR.
	///
	/// **NAO E UM BIT DE "ESTOU EM TRANSICAO"**, e a diferenca e o pedido: *"Interrompeu, a onda
	/// morre: nocaute, morte, logout, o outro sumindo. **Derive** de 'estou em transicao' em vez de
	/// guardar um bit que alguem tenha que apagar."*
	///
	/// Estar aqui e a UNICA definicao de "estou viajando", e ninguem precisa apagar nada: o
	/// <see cref="TickDaOndaDaMente"/> reavalia a validade de cada entrada todo tique e a joga fora
	/// sozinho. Um `bool` no <c>ServerPlayer</c> exigiria uma linha nova no nocaute, na morte, no
	/// logout, na saida voluntaria e no golpe que arranca do transe -- e o quinto lugar seria o que
	/// esquecesse, deixando alguem preso numa ondulacao eterna.
	/// </summary>
	private readonly record struct OndaDaMente(int Id, long Quando, bool Entrando, string Motivo);

	/// <summary>
	/// Quem esta esperando a onda. Lista de INSTANCIA e ids (nao corpos), pelo mesmo argumento do
	/// <c>_acordar</c>: entre o pedido e o fim da onda o jogador pode sair do mundo.
	/// </summary>
	private readonly List<OndaDaMente> _ondaDaMente = [];

	/// <summary>Esta pessoa ja tem uma onda correndo? Derivado da fila -- nao ha campo.</summary>
	private bool NaOnda(int id)
	{
		foreach (OndaDaMente o in _ondaDaMente) if (o.Id == id) return true;
		return false;
	}

	/// <summary>
	/// COMECA A ONDA: manda a tela ondular e marca a viagem pro fim dela.
	///
	/// A duracao viaja NO PACOTE (<see cref="DimensaoMental.MsDaOnda"/>) exatamente pra o cliente
	/// nunca ter uma constante propria -- ver o comentario daquela constante. Aqui o mesmo numero
	/// vira o prazo da viagem, e e isso que faz o *"a viagem so acontece no FIM da onda"* ser
	/// verdade e nao coincidencia.
	/// </summary>
	private void ComecarAOnda(ServerPlayer pl, bool entrando, string motivo)
	{
		_ondaDaMente.Add(new OndaDaMente(pl.Id, NowMs() + DimensaoMental.MsDaOnda, entrando, motivo));
		MandarEfeito(pl, "ondulacao", DimensaoMental.MsDaOnda);
	}

	/// <summary>CORTA A ONDA na tela de quem nao vai mais viajar. `ms = 0` = "passou".</summary>
	private static void PararAOnda(ServerPlayer pl) => MandarEfeito(pl, "ondulacao", 0);

	/// <summary>
	/// ============================ A PORTA, COM A GOTA NA FRENTE ============================
	/// *"ao clicar em meditar profundamente a tela vai ter esse efeito por uns segundos e ai o
	/// jogador vai pra dimensao da mente dele"*.
	///
	/// Este e o unico chamador do <see cref="EntrarNaMente"/> em producao (o canal `mente`); as
	/// bancadas continuam entrando pela porta direta, sem onda, porque o que elas medem e a porta e
	/// nao a espera -- e uma bancada que precisasse tiquear 1,8 s pra ver um clone nascer estaria
	/// medindo o relogio.
	///
	/// AS RECUSAS SAO COBRADAS AQUI TAMBEM, e nao so no fim: quem esta morto tem que ouvir "nao"
	/// agora. A cobranca do fim continua existindo porque o mundo muda durante a onda.
	/// ======================================================================================
	/// </summary>
	private void ComecarOMergulho(ServerPlayer pl)
	{
		if (pl.Peer == null) return;   // corpo sem dono nao medita

		// PEDIR DE NOVO NAO EMPILHA ONDA. Sem isto, apertar o botao tres vezes marcaria tres viagens
		// -- e as duas ultimas cairiam no `EntrarNaMente` ja em transe, gastando duas recusas na cara
		// de quem so clicou depressa.
		if (NaOnda(pl.Id)) return;

		if (RecusaDoMergulho(pl) is { Length: > 0 } motivo) { Avisar(pl, motivo); return; }

		ComecarAOnda(pl, entrando: true, "");
	}

	/// <summary>
	/// ============================ A VOLTA POR VITORIA, COM A GOTA ============================
	/// *"quando ele DERROTAR O CLONE dele tb faca isso mas pra VOLTAR pro mundo real, pq atualmente
	/// a transicao ta MT RAPIDA E MT SECA sem efeito nenhum."*
	///
	/// Chamado de UM lugar: a morte do corpo que a mente ergueu (`GameServer.Clone.cs`). E a unica
	/// saida por VITORIA, e por isso a unica saida com onda.
	///
	/// ============================ **A PORRADA NO CORPO REAL NAO PASSA POR AQUI** ============================
	/// *"(SO N VAI TER EFEITO SE ALGUEM BATER NO CORPO REAL enquanto ta meditando)"* -- e nao passa
	/// mesmo: aquele caminho e outro do primeiro metro ao ultimo. `MarcarAgressao` -> `AcordarNoCorpo`
	/// -> `PorNaFilaDeVolta` -> `TickDeQuemVolta` -> `VoltarDeOndeEstiver` -> `SairDaMente`, tudo
	/// dentro do mesmo tique e sem tocar nesta fila. Ser arrancado tem que ser SECO: e a diferenca
	/// entre fechar os olhos e levar um soco na cara.
	///
	/// E o mesmo vale pras outras saidas seleorias, pelo mesmo motivo -- o nocaute e a morte do corpo
	/// la fora (`BordasDeQuemEstaFora`), a saida voluntaria (`sairdamente`) e o logout.
	/// ====================================================================================================
	///
	/// O CORPO DERROTADO SAI AGORA, e essa linha nao e enfeite: sem ela este mesmo ramo do
	/// `TicarUmCorpo` reencontraria o reflexo morto no tique seguinte e pediria a onda de novo, uma
	/// vez a cada 33 ms. E de quebra fica bonito -- o reflexo se desfaz, a tela ondula, o mundo volta.
	/// </summary>
	private void ComecarAVoltaDaMente(ServerPlayer dono, string motivo)
	{
		DesfazerOOponente(dono, "");
		if (NaOnda(dono.Id)) return;
		ComecarAOnda(dono, entrando: false, motivo);
	}

	/// <summary>
	/// DRENA AS ONDAS -- uma vez por tique, do <see cref="TickCombate"/>, logo DEPOIS do
	/// <see cref="TickDeQuemVolta"/> e pelas mesmas duas razoes: as listas de zona ja estao livres
	/// (a viagem mexe em duas delas), e a saida SECA tem que ganhar da onda quando as duas caem no
	/// mesmo tique -- quem levou o soco ja voltou, e o que sobra aqui e so apagar a ondulacao.
	///
	/// ============================ ELE OLHA TODA A FILA, E NAO SO O QUE VENCEU ============================
	/// Esta e a linha que cumpre o *"interrompeu, a onda morre"*. Se so os prazos vencidos fossem
	/// examinados, quem fosse nocauteado no primeiro decimo continuaria com a tela ondulando pelos
	/// 1,7 s restantes -- e so entao descobriria que nao ia a lugar nenhum.
	///
	/// A validade e uma PERGUNTA ao mundo, nao um bit: indo, e o <see cref="RecusaDoMergulho"/>
	/// (morto, caido, ja em transe, parou de meditar); voltando, e `NaMente` -- se ele ja saiu por
	/// qualquer outra porta, nao ha o que fazer. **Logout nao precisa de linha**: o id some do
	/// `_players` e a entrada e descartada.
	/// ==================================================================================================
	/// </summary>
	private void TickDaOndaDaMente()
	{
		if (_ondaDaMente.Count == 0) return;

		long agora = NowMs();
		for (int i = _ondaDaMente.Count - 1; i >= 0; i--)
		{
			OndaDaMente o = _ondaDaMente[i];

			if (!_players.TryGetValue(o.Id, out ServerPlayer? pl)) { _ondaDaMente.RemoveAt(i); continue; }

			// DEIXOU DE FAZER SENTIDO: a onda morre AGORA, no meio.
			string recusa = o.Entrando ? RecusaDoMergulho(pl) : (NaMente(pl) ? "" : "saiu");
			if (recusa.Length > 0)
			{
				_ondaDaMente.RemoveAt(i);
				PararAOnda(pl);
				// SO A IDA EXPLICA. Na volta o jogador ja recebeu a frase de quem o arrancou ("algo
				// atinge o seu corpo", o nocaute, a morte) -- um segundo aviso seria o servidor
				// contando duas vezes a mesma coisa.
				if (o.Entrando) Avisar(pl, recusa);
				continue;
			}

			if (agora < o.Quando) continue;

			_ondaDaMente.RemoveAt(i);
			if (o.Entrando) EntrarNaMente(pl);
			else SairDaMente(pl, o.Motivo);
		}
	}

	/// <summary>
	/// ADIANTA AS ONDAS PENDENTES -- **SO PRAS BANCADAS**, e ela nao pula caminho nenhum: ela vence o
	/// PRAZO e chama o <see cref="TickDaOndaDaMente"/> de producao, que e quem revalida e viaja.
	///
	/// ============================ POR QUE ELA PRECISOU EXISTIR ============================
	/// A `--menteviva` mede a PORTA pelo caminho de producao -- dezessete `UsarHabilidade(x, "mente")`
	/// seguidos de "e agora ele esta na mente". Com a onda no meio, os dezessete passariam a olhar pra um
	/// jogador que ainda esta em pe no planeta, e a bancada inteira ficaria vermelha por um motivo que
	/// nao e defeito nenhum.
	///
	/// O CONSERTO NAO PODIA SER AFROUXAR A ONDA em modo de teste: a segunda copia do caminho e o que
	/// este port ja registrou como o erro que mais se repete nele. Aqui a bancada atravessa a fila de
	/// verdade -- ela so nao espera o relogio, porque o relogio ja tem bancada propria (`--diaggota`,
	/// que fotografa a tela). Quem mede a mente mede a mente; quem mede a onda mede a onda.
	/// ==================================================================================
	/// </summary>
	internal void AdiantarAOndaDaMenteNoTeste()
	{
		for (int i = 0; i < _ondaDaMente.Count; i++) _ondaDaMente[i] = _ondaDaMente[i] with { Quando = 0 };
		TickDaOndaDaMente();
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
