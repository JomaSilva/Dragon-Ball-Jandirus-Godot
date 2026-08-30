using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O CORPO QUE FICA ============================
/// *"por mais q sua CAMERA (ou personagem, no caso da meditacao) va pra outro local, o CORPO
/// ORIGINAL E VERDADEIRO CONTINUA NO MAPA ANTERIOR, PARADO, fazendo exatamente o q ele tava fazendo
/// antes. entao enquanto vc ta lutando na mente, pessoas do lado de fora vao ver vc MEDITANDO
/// normalmente, e se elas TE BATEREM vc ACORDA da meditacao pro corpo real. e na nave, ao pilotar ou
/// observar, teu corpo vai ficar la parado enquanto vc controla a nave, e caso alguem te bata vc
/// volta pro corpo principal."* -- o dono, sobre os DOIS sistemas na mesma frase.
///
/// E UM MECANISMO SO, e este arquivo e ele. A mente (`GameServer.Clone.cs`) e o leme da nave grande
/// (`GameServer.NaveGrande.cs`) sao dois CHAMADORES; nao ha uma segunda copia disto em lugar nenhum.
/// O DM tambem escreveu uma vez so -- `mob/npc/mind_dummy` (`MindMeditate.dm:127-167`) nasce na
/// meditacao E no leme (`ShipVessel.dm:329-346`), e o que muda entre os dois e so o `wake_mode`.
/// Aqui nem o `wake_mode` existe: ver <see cref="VoltarDeOndeEstiver"/>.
/// ==========================================================================
///
/// ============================ O BONECO E O CORPO, NAO UMA COPIA DELE ============================
/// Ele compartilha a MESMA `Fighter` e o MESMO `CombatState` do dono -- as instancias, nao copias.
/// E a diferenca mais importante deste arquivo, e ela e o que faz o pedido funcionar de graca:
///
///   * bater nele **fere voce de verdade** ("apanhar traz de volta, com o dano valendo no corpo
///     real"): o `MeleeResolver` ja escreveu no seu corpo antes de o servidor sequer perceber que
///     era um boneco. Nao ha transferencia de dano pra escrever, e portanto nao ha transferencia
///     pra errar;
///   * o Sense le o seu BP expresso, a HUD mostra a sua vida, as feridas desenhadas no boneco sao as
///     suas, os membros quebrados sao os seus, a forma desenhada e a sua -- tudo isso porque nao ha
///     um segundo numero em lugar nenhum;
///   * a POSE sai sozinha. `ServerPlayer.Pose()` deriva `Meditando` de `Ficha.med`, que e a MESMA
///     ficha: quem largou o corpo meditando deixa um corpo que medita, e quem largou de pe no leme
///     deixa um corpo de pe. **Nao ha campo de pose no boneco, e nao pode haver.**
///
/// O CLONE DA MENTE E O OPOSTO E ISSO NAO E CONTRADICAO: la a ficha e copiada por VALOR justamente
/// pra bater no clone **nao** tirar a sua vida (ver `ClonarFicha`). Sao dois objetos com propositos
/// opostos -- o clone e OUTRA pessoa com os seus numeros; o boneco e VOCE.
///
/// CONSEQUENCIA QUE PRECISA DE GUARDA: com a ficha compartilhada, deixar o boneco entrar no laco de
/// jogadores regeneraria, digeriria e cronometraria o seu corpo DUAS vezes por tique. **A guarda nao
/// e um `if` no `TickCombate`** -- e o boneco nao estar no `_players`, que e a lista que aquele laco
/// (e outros cinco) percorre. Ver o bloco "ELE ENTRA NA ZONA E **NAO** NO `_players`", oitenta linhas
/// abaixo, que e onde a decisao mora de verdade.
/// ==========================================================================================
///
/// ============================ ELE NAO E UM CORPO POSSUIDO ============================
/// O Oozaru sem controle e a Furia Lendaria usam `Cerebro != null` -- "o SERVIDOR esta dirigindo
/// este corpo". O boneco e o contrario exato: `Cerebro == null` **e** `Peer == null`, ou seja
/// **ninguem** o dirige. Ele nao anda, nao bate, nao decide; ele so ocupa o lugar e apanha.
///
/// Por isso ele nao entra no `TickDosCorposSemDono` (que filtra por `Cerebro != null`) e por isso o
/// bit `EntityState.SemRedeas` continua FALSO nele: aquele bit quer dizer "a IA tomou este corpo", e
/// e ele que pinta o olho da fera. Um corpo meditando com olho de berserk seria a mentira que os
/// dois sistemas ficariam contando um sobre o outro.
///
/// E na outra ponta: **nao da pra largar o corpo enquanto se esta possuido** (ver
/// <see cref="LargarOCorpo"/>). A fera nao medita e nao pilota.
/// ================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// LARGA O CORPO AQUI E LEVA A ATENCAO PRA <paramref name="destino"/>.
	///
	/// Devolve FALSO quando o pedido e recusado (ja esta fora do corpo, o corpo nao tem dono na tela,
	/// ou ele esta POSSUIDO) -- e ai nada aconteceu: nem boneco, nem viagem. Quem chama tem que
	/// respeitar o falso ANTES de escrever o proprio estado (o leme de uma nave, por exemplo), senao
	/// fica com um piloto registrado que nunca saiu da ponte e um leme trancado pra todo mundo.
	/// </summary>
	private bool LargarOCorpo(ServerPlayer pl, ZoneKey destino, Vec2 pos)
	{
		if (pl.Peer == null) return false;              // corpo sem dono nao tem atencao pra mandar embora
		if (pl.BonecoLargado != null) return false;     // ja esta fora do corpo

		// POSSUIDO NAO LARGA O CORPO. O corpo ja nao e dele -- quem o dirige e o
		// `TickDosCorposSemDono` --, e deixar a fera "sair" produziria um Oozaru dirigido pela IA no
		// mapa E um boneco parado ao lado, os dois com a mesma ficha. Ver o cabecalho deste arquivo.
		if (SemAsRedeas(pl)) { Avisar(pl, "voce nao esta no controle do proprio corpo."); return false; }

		var boneco = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,        // ninguem olha por esses olhos...
			Cerebro = null,     // ...e ninguem os dirige. As duas metades de "corpo inerte".
			DonoDoCorpoLargado = pl.Id,

			// O NOME E O SEU. Quem passa do lado tem que ver VOCE, e nao "Fulano (corpo)": o pedido
			// e literalmente *"pessoas do lado de fora vao ver vc MEDITANDO normalmente"*.
			Name = pl.Name,
			Zone = pl.Zone,
			Pos = pl.Pos,
			Facing = pl.Facing,
			Race = pl.Race,
			Class = pl.Class,
			Genero = pl.Genero,
			Planeta = pl.Planeta,
			Idade = pl.Idade,
			SpeedStat = pl.SpeedStat,
			LastInputMs = NowMs(),

			// AS INSTANCIAS COMPARTILHADAS -- ver o cabecalho. `Ficha` da a vida, o BP expresso e a
			// pose; `Combate` da os membros e RECEBE O DANO; `Forma` faz o boneco ser desenhado
			// transformado (o `SincronizarFormas` la embaixo le exatamente este objeto).
			Ficha = pl.Ficha,
			Combate = pl.Combate,
			Forma = pl.Forma,

			// O LIVRO E OS NIVEIS TAMBEM, e aqui e por SEGURANCA e nao por regra: quem resolve um
			// golpe pergunta coisas ao corpo que apanhou (*"ele sabe Afterimage?"*, no vulto da
			// esquiva e no Zanzo Clash). Deixar nulos faria o boneco ser o unico corpo do jogo em que
			// essas perguntas teriam que ser feitas com `?.` -- e a primeira que esquecesse derrubaria
			// o tique. Como sao as SUAS skills, a resposta certa e a sua. Ninguem escreve neles a
			// partir do boneco: ele nao treina, nao aprende e nao e tiqueado.
			Livro = pl.Livro,
			Niveis = pl.Niveis,

			// A APARENCIA E COPIA, e e a unica coisa que e. Ela nao descreve estado de jogo -- e a
			// lista de roupa/cabelo --, e o `Sanear` do catalogo reescreve a lista que recebe: dois
			// corpos apontando pra mesma instancia foi um bug ja registrado no clone.
			Visual = pl.Visual.Copiar(),
		};

		// ============================ ELE ENTRA NA ZONA E **NAO** NO `_players` ============================
		// Esta e a linha mais importante do arquivo, e ela e o preco de compartilhar a ficha.
		//
		// `_players` quer dizer "um corpo que o servidor SIMULA": e sobre ele que correm `TickFichas`
		// (o `Treinar`, ou seja o GANHO DE BP), `TickDaForma` (o dreno de Ki da transformacao),
		// `TickDosNiveis`, o estomago, a regeneracao e o `Persistir`. O boneco aponta pra MESMA ficha
		// do dono, e o dono ja esta nessa lista -- bastava acrescentar o boneco pra tudo isso passar a
		// rodar **duas vezes no mesmo corpo**: BP dobrado por tique meditando, Ki da forma drenando no
		// dobro, fome no dobro. Nada disso apareceria como erro; apareceria como numero errado.
		//
		// Guardas espalhadas nesses seis lacos seriam a "linha nova em cinco dos seis lugares" que
		// este port ja registra como o erro que mais se repetiu nele -- e o setimo laco de amanha
		// nasceria quebrado. Ficar de fora do `_players` responde a todos de uma vez, hoje e depois.
		//
		// A `ZoneList` e outra coisa: ela e "o que existe NESTE lugar". E dela que saem o snapshot,
		// o `AlvoNaFrente`, o alcance das rajadas e a busca dos NPCs -- exatamente o que o corpo
		// parado precisa pra ser VISTO e APANHAR. Ver `Mirar`, que e o unico ponto que precisou
		// aprender a achar um corpo por id fora do `_players`.
		// ==========================================================================================
		ZoneList(boneco.Zone.Hash).Add(boneco);
		pl.BonecoLargado = boneco;

		// PELO MESMO MOTIVO ELE NAO PASSA PELO `PorNoMundo`: alem do `_players`, dois dos cinco passos
		// de la estariam ERRADOS aqui, e os dois calados:
		//   * `PrepararCombate` CRIA um `CombatState` novo (com membros novos, e um gancho de negar
		//     morte fechado sobre o boneco). Seria exatamente o segundo corpo que este arquivo existe
		//     pra nao ter;
		//   * `AplicarGravidade(nascendo: true)` ACLIMATA ao chao como se o corpo tivesse nascido ali.
		//     Este corpo nao nasceu: ele esta parado no mesmo lugar, com a mesma gravidade, ha um
		//     instante. Reaclimatar seria mexer na ficha do dono por causa de um passo de fabrica.
		// Os outros (`Statify`, `SpeedStat`) ou ja estao feitos na ficha do dono ou estao acima.

		// QUEM ESTA NA ZONA PRECISA VER O CORPO. Mesmo funil do NPC: manda o `PeerLook` pra todo
		// mundo da zona e, dentro dele, o `SincronizarFormas` -- sem o segundo, quem largou o corpo
		// em SSJ3 deixaria pra tras um lutador comum de cabelo curto. Os envios de VOLTA (pro
		// boneco) sao no-op, porque `Peer` e nulo.
		TrocarAparencias(boneco);

		MoveToZone(pl.Id, destino, pos);
		return true;
	}

	/// <summary>
	/// VOLTA PRO CORPO. Tira o boneco do mundo e devolve a atencao do dono pra ONDE O BONECO ESTA --
	/// e nao pra onde ele foi deixado.
	///
	/// A DIFERENCA E A RESPOSTA A "ALGUEM O ARREMESSA": o boneco e um corpo comum, entao um soco o
	/// empurra, uma explosao o joga longe e alguem pode carrega-lo pra outro canto do mapa. Voltar
	/// pra coordenada de partida devolveria o dono pra um lugar onde o corpo dele **nao esta mais**
	/// -- e ele apareceria teleportado, do lado de fora do proprio corpo.
	///
	/// <paramref name="seSumiu"/> e pra onde ir quando o boneco NAO EXISTE MAIS: a zona foi
	/// descarregada, a nave em que ele estava explodiu, um admin o apagou. Nulo = a Terra. **Nunca
	/// ha o caso de ficar preso**: a saida existe mesmo sem corpo pra voltar.
	/// </summary>
	private void VoltarProCorpo(ServerPlayer pl, string motivo, ZoneKey? seSumiu = null, Vec2 posSeSumiu = default)
	{
		ServerPlayer? boneco = pl.BonecoLargado;

		// OS DOIS LADOS DO ESPELHO ZERAM AQUI, e este e o unico lugar que zera qualquer um dos dois.
		pl.BonecoLargado = null;
		if (boneco != null) boneco.DonoDoCorpoLargado = 0;

		// E SE ELE JA TIVER SAIDO DO MUNDO, nao ha pra onde voltar: a zona foi descarregada, a nave
		// explodiu, um admin o apagou. O teste e a `ZoneList` porque e nela que o boneco vive.
		if (boneco != null && !ZoneList(boneco.Zone.Hash).Contains(boneco)) boneco = null;

		ZoneKey destino;
		Vec2 pos;
		if (boneco != null)
		{
			destino = boneco.Zone;
			pos = boneco.Pos;
			pl.Facing = boneco.Facing;

			// O AVESSO EXATO DO NASCIMENTO: `RemoverNpc` tira das duas listas e avisa a ZONA INTEIRA
			// com `PeerLeft`. Tem que ser a zona e nao so o dono -- quem estava vendo o corpo parado
			// ficaria com um boneco congelado na tela pra sempre.
			//
			// E ele NAO desfaz nada da ficha nem do corpo, o que aqui e obrigatorio: aquelas
			// instancias sao as do dono, que continua vivo usando as duas.
			RemoverNpc(boneco);
		}
		else
		{
			destino = seSumiu ?? SpawnZone;
			pos = seSumiu != null ? posSeSumiu : SpawnPos;
			GD.Print($"[server] {pl.Name} voltou pro corpo e o corpo nao estava mais la -- caiu em {destino.Name}");
		}

		MoveToZone(pl.Id, destino, pos);
		if (motivo.Length > 0) Avisar(pl, motivo);
	}

	/// <summary>
	/// ============================ APANHAR TRAZ DE VOLTA ============================
	/// Chamado de UM lugar: o <see cref="MarcarAgressao"/>, que e o funil unico de "fulano agrediu
	/// sicrano" deste port -- as vinte e tantas chamadas dele sao o soco, o contra-ataque, o punho
	/// espiritual, as rajadas, as tecnicas, o Zanzo Clash e o dano espalhado.
	///
	/// E o mesmo encaixe do DM: la o gatilho e o `refresh_combat_tag()` do `mind_dummy`
	/// (`MindMeditate.dm:147-149`), e o comentario do autor explica por que e nesse proc e nao no
	/// dano -- *"so this fires no matter the outcome"*. **Golpe APARADO ou ESQUIVADO tambem acorda.**
	/// Quem tocou no seu corpo te trouxe de volta, tenha machucado ou nao.
	///
	/// O DANO NAO PRECISA SER TRANSFERIDO porque ele ja caiu no lugar certo: o `MeleeResolver`
	/// escreveu no `CombatState` compartilhado antes desta funcao ser chamada.
	/// ==============================================================================
	///
	/// ============================ POR QUE ADIADO E NAO NA MESMA LINHA ============================
	/// Voltar pro corpo chama `MoveToZone`, que MEXE nas listas de duas zonas. `MarcarAgressao` roda
	/// no meio da resolucao de um golpe, e o `AlvoNaFrente`/`PresaDaFera` que levaram ate ele estao
	/// percorrendo a lista da zona: remover o boneco dali derrubaria o tique com "Collection was
	/// modified" -- o mesmo defeito que fez o NPC morto virar `_npcsPraTirar` em vez de sumir na
	/// hora. A fila e drenada no fim do proprio tique: "na hora" continua sendo na hora (33 ms).
	/// ======================================================================================
	/// </summary>
	private void AcordarNoCorpo(ServerPlayer boneco)
	{
		if (boneco.DonoDoCorpoLargado != 0) PorNaFilaDeVolta(boneco.DonoDoCorpoLargado);
	}

	/// <summary>
	/// Enfileira o DONO. Os dois gatilhos entram por aqui (o golpe, que so tem o boneco na mao; e as
	/// bordas do tique, que so tem o dono) -- e a fila e de ids e nao de corpos porque tanto o dono
	/// quanto o boneco podem sair do mundo entre o pedido e a drenagem.
	/// </summary>
	private void PorNaFilaDeVolta(int donoId)
	{
		if (!_acordar.Contains(donoId)) _acordar.Add(donoId);
	}

	/// <summary>
	/// Quem volta pro proprio corpo no fim deste tique. Lista de INSTANCIA e reusada, pelo mesmo
	/// argumento do <see cref="_npcsPraTirar"/>: ela sai vazia quase sempre e alocar por tique num
	/// laco de 30 Hz e lixo constante pro coletor.
	/// </summary>
	private readonly List<int> _acordar = [];

	/// <summary>
	/// DRENA A FILA -- chamado uma vez por tique, do <see cref="TickCombate"/>, com as listas de zona
	/// ja livres.
	///
	/// ============================ POR INDICE E NAO `foreach`, E O MOTIVO E O MESMO DO TIQUE ============================
	/// Numa `List&lt;T&gt;` **o `Add` invalida o enumerador** (ao contrario do `Dictionary.Remove`, que
	/// nao invalida nada) -- e o corpo deste laco alcanca o `_acordar.Add` da <see cref="PorNaFilaDeVolta"/>
	/// cinco chamadas abaixo:
	///
	///     VoltarDeOndeEstiver -> VoltarProCorpo/SairDaMente/PararDePilotar -> MoveToZone
	///       -> SoltarDoEmbateDeKi -> Resolver -> Devolver -> MarcarAgressao -> AcordarNoCorpo -> PorNaFilaDeVolta
	///
	/// Conferi elo por elo e HOJE ele nao estoura, mas por coincidencia e nao por desenho: o unico alvo
	/// possivel de um `Devolver` disparado daqui e o proprio corpo que esta voltando (ou o boneco dele),
	/// entao o id enfileirado e sempre o que ja esta na lista -- e o `if (!_acordar.Contains(...))` da
	/// `PorNaFilaDeVolta` engole o `Add`. Basta um caminho novo alcancar um boneco de OUTRO dono pra que
	/// a fila que existe justamente pra evitar "Collection was modified" passe a lanca-lo.
	///
	/// O indice fecha isso de graca e sem copia: quem for enfileirado no meio da drenagem e atendido
	/// NESTE mesmo tique (que e o que a fila promete -- *"na hora"* = 33 ms), e a `Contains` la garante
	/// que ninguem entra duas vezes, entao o laco termina. Ver o comentario grande do `TickCombate`.
	/// ==============================================================================================================
	/// </summary>
	private void TickDeQuemVolta()
	{
		if (_acordar.Count == 0) return;
		for (int i = 0; i < _acordar.Count; i++)
			if (_players.TryGetValue(_acordar[i], out ServerPlayer? pl) && pl.BonecoLargado != null)
				VoltarDeOndeEstiver(pl, "algo atinge o seu corpo -- voce volta pra ele na hora.");
		_acordar.Clear();
	}

	/// <summary>
	/// VOLTA, SEJA LA DE ONDE VOCE ESTIVER. E o `wake_mode` do DM (`MindMeditate.dm:142`) -- so que
	/// **derivado em vez de guardado**.
	///
	/// La o boneco carrega um numero dizendo se ele foi deixado por meditacao (1) ou pelo leme (2), e
	/// o `wake_owner()` liga na saida certa. Aqui esse numero seria a segunda verdade sobre uma coisa
	/// que o servidor **ja sabe**: quem esta na mente **esta numa zona que e uma mente**, quem esta no
	/// leme e o `PilotoId` de uma nave grande. Guardar o modo seria criar a chance de ele discordar do
	/// estado real -- e o dia em que discordasse, a pessoa voltaria pelo caminho errado.
	///
	/// ============================ E ESSE DIA CHEGOU, COM O VISITANTE ============================
	/// Ate a Camada 2 a pergunta da mente aqui era `pl.CloneId != 0` -- "tenho um reflexo?" --, e ela
	/// era certa enquanto entrar na mente e erguer um reflexo fossem a mesma coisa. Nao sao mais: quem
	/// visita a mente do outro nunca tem clone, e o anfitriao perde o dele no instante em que a visita
	/// chega (e o `del(clone)` do `add_member`). Os dois teriam caido no ramo do LEME, e dali no
	/// terceiro ramo -- voltando pro corpo com a mente ainda aberta atras deles.
	///
	/// A pergunta certa e do LUGAR, e o lugar nao depende de quantos corpos ha nele. Ver
	/// `Core/World/DimensaoMental.cs`.
	/// =====================================================================================
	///
	/// Os dois caminhos terminam no <see cref="VoltarProCorpo"/>; o que eles fazem a mais e desfazer
	/// o que era SEU (o clone, o registro de piloto). O terceiro ramo e a rede de seguranca: se um
	/// dia alguem largar o corpo por um motivo novo e esquecer de tratar aqui, o corpo volta assim
	/// mesmo -- nunca preso.
	/// </summary>
	private void VoltarDeOndeEstiver(ServerPlayer pl, string motivo)
	{
		if (NaMente(pl)) { SairDaMente(pl, motivo); return; }
		if (NaveDoPiloto(pl) is { } nave && Jandirus.Core.Tech.NaveGrande.EhNaveGrande(nave.Tipo))
		{ PararDePilotar(pl, nave, motivo); return; }
		VoltarProCorpo(pl, motivo);
	}

	/// <summary>
	/// ============================ AS BORDAS QUE NAO SAO UM GOLPE ============================
	/// Chamado por jogador, no laco do <see cref="TickCombate"/>. Sao as tres maneiras de a viagem
	/// deixar de fazer sentido sem ninguem ter batido em nada, e a resposta as tres e a mesma --
	/// **acorda**, nunca "fica preso":
	///
	///   1. **O CORPO CAIU** (nocaute ou morte). A ficha e compartilhada, entao o corpo la fora
	///      desmaiar E voce desmaiar; continuar pilotando uma nave de dentro de um corpo caido no
	///      chao de outro planeta e o absurdo que o DM fecha com o `mind_monitor` (`:436-462`:
	///      morreu, sai; caiu, e ejetado). Voltando na hora, o nocaute e a morte seguem o caminho
	///      normal -- inclusive o renascimento, que agora acontece no lugar certo (o berco), e nao
	///      no bolso da mente nem no vazio do espaco;
	///   2. **O CORPO SUMIU DO MUNDO** -- a zona foi descarregada, a nave onde ele estava explodiu,
	///      um admin o apagou. `VoltarProCorpo` cai no destino de emergencia (a Terra) e diz no
	///      console o que houve;
	///   3. **A ZONA DO CORPO NAO E MAIS A MESMA**: nao ha caso hoje, e por isso nao ha codigo --
	///      o boneco nao anda sozinho, e se ele for levado embora, o dono volta ONDE ELE ESTIVER
	///      (ver <see cref="VoltarProCorpo"/>).
	///
	/// DESLOGAR NAO ESTA NESTA LISTA porque ele nao e uma borda deste laco: quem cai e tratado no
	/// `Drop`, ANTES de o progresso ir pro disco -- senao o save gravaria a zona da mente ou o vazio
	/// do espaco como o lugar do personagem, e o dono relogaria dentro de um bolso sem saida.
	/// ====================================================================================
	/// </summary>
	private void BordasDeQuemEstaFora(ServerPlayer pl)
	{
		if (pl.BonecoLargado is not { } boneco) return;
		if (!ZoneList(boneco.Zone.Hash).Contains(boneco) || pl.Ficha.dead || pl.Ficha.KO)
			PorNaFilaDeVolta(pl.Id);
	}
}
