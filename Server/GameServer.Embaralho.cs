using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O EMBARALHO: o passeio que ninguem viu acontecer.
///
/// ============================ O PROBLEMA QUE ELE RESOLVE E DO DONO, E E DE PERCEPCAO ============================
/// O congelamento anti-lag (`MenteDormindo`) faz o planeta vazio custar quase nada, e cobra um preco
/// que o dono viu antes de a coisa existir: *"pra n parecer q os npcs quase n se moveram pela IA
/// deles ter menos chamadas quando n tem ninguem"*. Um habitante congelado por meia hora esta,
/// quando voce volta, **no pixel exato** em que voce o deixou. O planeta nao parece parado enquanto
/// voce esteve fora -- ele parece parado PRA SEMPRE, que e pior.
///
/// O pedido: *"ao SAIR de um planeta e passar UNS 5 MINUTOS, todos os npcs do mapa sao colocados em
/// POSICOES ALEATORIAS (cuidado pra n colocar dentro de paredes) PROXIMA DA ONDE ELES ESTAVAM, pra
/// parecer q eles andaram. essa mudanca de posicao SO BASTA ACONTECER 1 VEZ, apos os 5 minutos q o
/// ULTIMO JOGADOR SAIU do planeta"*.
/// ==========================================================================================================
///
/// ============================ E ELE E PREGUICOSO -- A EQUIVALENCIA FOI MEDIDA, NAO SUPOSTA ============================
/// A leitura literal ("aos 5 minutos, embaralha") pede um relogio por zona correndo num planeta em
/// que, por definicao, nao ha ninguem. Aqui o embaralho acontece **no instante em que alguem chega**,
/// se o planeta ja tiver esfriado. Do ponto de vista de qualquer observador o resultado e IDENTICO --
/// e custa ZERO enquanto o planeta esta vazio, que e literalmente o objetivo do dono (*"poupar
/// recursos do server ao maximo"*).
///
/// **Isso so vale se ninguem LER a posicao de um NPC com a zona vazia**, e essa pergunta foi
/// respondida varrendo os leitores um por um:
///
///   * CONQUISTA   -- `TemPovo(planeta)` le o PLANO do `npcs.json`, nunca uma posicao;
///   * SAGA        -- `MonitorarChefe` le ficha, estagio e rancor. Zero posicao;
///   * CONVIVIO    -- `EhPessoa` exige assinatura, e NPC nasce sem: nem entra no laco;
///   * SENSE       -- e um bit que libera ler o BP de quem JA esta no snapshot da propria zona;
///   * SAVE        -- `Persistir` sai na primeira linha com `Peer == null`. **Posicao de NPC nunca
///                    vai pro disco**; num reinicio eles renascem em `PontoDeHabitante`;
///   * SNAPSHOT    -- so vai pra quem esta na zona, ou seja, ninguem.
///
/// Sobra UM leitor: o verbo de admin que lista corpos com coordenada (`GameServer.Admin.cs`). Um
/// admin em outro planeta veria a posicao velha ate alguem pousar la. E leitura de diagnostico, nao
/// regra de jogo -- e o proprio admin, ao ir ver, dispara o embaralho.
///
/// De quebra o preguicoso mata o problema do RELOGIO de graca: nada precisa sobreviver a reinicio
/// (no reinicio os corpos renascem em posicao deterministica de qualquer jeito), entao a marca pode
/// morar na memoria e ser `NowMs()` -- "5 minutos" tem que ser 5 minutos de parede, e aqui nao ha
/// como acelerar o relogio pra fugir de nada.
/// ================================================================================================================
///
/// ============================ O SAVE E O REINICIO -- A DECISAO, E POR QUE ELA E ESTA ============================
/// Sao duas perguntas ("o embaralho sobrevive ao save?" e "a marca de JA embaralhou sobrevive?"), e
/// as duas ja estao respondidas pelo desenho do port. Escritas aqui pra a resposta ser uma DECISAO e
/// nao uma coincidencia que alguem desfaz sem perceber:
///
/// **1. O EMBARALHO SOBREVIVE AO SAVE -- porque o save nao tem opiniao sobre onde um NPC esta.**
/// `Persistir` sai na primeira linha com `pl.Slot &lt; 0`/`Peer == null` (`GameServer.cs`), e o
/// `mundo.json` guarda obra, tecnologia e gravidade: **posicao de NPC nao vai pro disco por nenhuma
/// das duas portas**. Entao nao existe o defeito que a pergunta teme -- o embaralho acontecer e um
/// save posterior desfaze-lo --, porque nao ha nada gravado pra sobrescrever a memoria. A posicao de
/// um habitante vale o que vale a vida do processo, e o embaralho dura exatamente isso.
///
/// **2. E ELE NAO SE REPETE NO BOOT -- pela AUSENCIA de marca, que ja e uma resposta.** `_vazioDesde`
/// nasce vazio, e zona sem marca nao embaralha (ver o campo). Ou seja: no primeiro login depois de um
/// reinicio, NENHUM planeta embaralha, porque desde o boot ninguem saiu de lugar nenhum. A marca nao
/// precisa ser persistida -- persistir ela e que seria o defeito, porque ela ficaria falando de um
/// mundo que nao existe mais.
///
/// **3. REINICIAR ANTES OU DEPOIS DOS 5 MINUTOS DA NO MESMO, e de proposito.** Os corpos renascem em
/// `PontoDeHabitante(zona, lugar)`, que e funcao pura de (semente do universo, zona, lugar): o
/// vilarejo volta EXATAMENTE como no primeiro boot, tenha o prazo vencido ou nao. Isto e o oposto de
/// um esquecimento -- e a mesma promessa que o port faz pro mundo inteiro (o planeta gerado, a lua, o
/// ceu): **a mesma semente da o mesmo mundo**. Um embaralho persistido faria um servidor reiniciado
/// divergir de um servidor virgem com a mesma semente, e trocaria uma promessa forte por um enfeite.
///
/// O que o jogador ve, entao: sem reinicio, "eles andaram enquanto eu nao estava"; com reinicio, "o
/// mundo voltou ao comeco" -- que ja e o que ele ve em tudo o mais que reinicia junto.
/// ==================================================================================================================
///
/// ============================ DUAS PORTAS DE SAIDA, DUAS DE ENTRADA -- E NENHUMA LINHA ESPALHADA ============================
/// "Sair de um planeta" tem CINCO caminhos (nave, logout, entrar na mente, morrer, e o KO que NAO
/// conta como sair), e escrever a marca nos cinco seria o defeito que mais se repete neste port. A
/// marca e DERIVADA: a zona que estava no `_zonasComGente` no tique anterior e nao esta mais acabou
/// de esvaziar, e o conjunto ja e montado uma vez por tique. Um lugar so, e ele nao pode ficar pra
/// tras quando aparecer o sexto caminho.
///
/// A chegada nao da pra derivar do mesmo jeito, e a razao e o RELOGIO DO TIQUE: o pouso vindo do
/// espaco acontece no `TickDoEspaco`, DEPOIS de o conjunto ter sido montado -- embaralhar so no
/// tique seguinte deixaria o cliente receber um snapshot com as posicoes velhas e ver a cidade
/// inteira dar um pulo. Entao a chegada e perguntada nas duas portas de entrada no mundo, que sao
/// as mesmas duas em que um jogador entra num `ZoneList`: o login e o <c>MoveToZone</c>.
/// ====================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// QUANDO CADA ZONA FICOU VAZIA (`NowMs()`). Sem entrada = ninguem nunca saiu dela.
	///
	/// A AUSENCIA DE MARCA E UMA RESPOSTA, e nao um caso esquecido: um planeta que nenhum jogador
	/// visitou desde o boot nao embaralha ao ser visitado pela primeira vez. O dono pediu o embaralho
	/// *"apos os 5 minutos q o ULTIMO JOGADOR SAIU"* -- sem jogador nenhum nunca, nao ha de que sair,
	/// e os corpos ficam onde o povoamento deterministico os pos.
	/// </summary>
	private readonly Dictionary<ulong, long> _vazioDesde = [];

	/// <summary>
	/// O `_zonasComGente` DO TIQUE PASSADO. Existe pra a diferenca entre os dois -- e so pra isso.
	/// Guardar o conjunto inteiro (e nao um bool por zona) e o que faz a deteccao custar o tamanho
	/// da lista de zonas OCUPADAS, que e um numero de um digito, e nao o das zonas EXISTENTES.
	/// </summary>
	private readonly HashSet<ulong> _zonasComGenteAntes = [];

	/// <summary>
	/// QUANTAS VEZES CADA ZONA JA EMBARALHOU. Entra na semente pra duas ausencias seguidas nao
	/// darem o MESMO deslocamento -- sem ele, um planeta visitado e abandonado duas vezes veria os
	/// habitantes andarem duas vezes na mesma direcao, que e um passeio em linha reta e nao um
	/// passeio. Continua deterministico: a mesma semente de universo com a mesma sequencia de idas e
	/// vindas da o mesmo mundo.
	///
	/// OS DOIS DICIONARIOS SAO LIMITADOS PELO QUE FOI JOGADO, e nao pelo tamanho do universo: so
	/// entra zona em que um jogador ESTEVE. Explorar mil planetas gerados deixa mil pares de 16
	/// bytes; um planeta gerado que seja podado e regerado depois so recomeca a contagem, que muda a
	/// semente do proximo embaralho e mais nada.
	/// </summary>
	private readonly Dictionary<ulong, ulong> _embaralhosDaZona = [];

	/// <summary>
	/// A DIFERENCA ENTRE O TIQUE PASSADO E ESTE. Chamado UMA vez, de dentro do proprio laco que monta
	/// o `_zonasComGente` (`TickDosCorposSemDono`), pelo motivo do cabecalho: quem sai tem cinco
	/// portas e o conjunto tem uma.
	/// </summary>
	private void MarcarZonasQueEsvaziaram()
	{
		if (_zonasComGenteAntes.Count > 0)
		{
			long agora = NowMs();
			foreach (ulong h in _zonasComGenteAntes)
				if (!_zonasComGente.Contains(h)) _vazioDesde[h] = agora;
		}

		// A MARCA E SEMPRE REESCRITA, e e isso que faz o prazo ser do ULTIMO que saiu: quem entra e
		// sai de novo deixa uma marca NOVA por cima da velha, e o relogio recomeca do zero.
		_zonasComGenteAntes.Clear();
		foreach (ulong h in _zonasComGente) _zonasComGenteAntes.Add(h);
	}

	/// <summary>
	/// CHEGOU ALGUEM: se o planeta esfriou, os habitantes "andaram" enquanto voce nao estava.
	///
	/// Chamado ANTES de o recem-chegado receber qualquer snapshot daquela zona -- e por isso ele
	/// nunca ve o pulo, so o resultado. E acontece **uma vez so** (a marca e consumida), como o dono
	/// pediu: quem fica dez minutos no planeta nao ve ninguem se teleportar.
	/// </summary>
	private void EmbaralharSeEsfriou(ZoneKey zona)
	{
		if (!_vazioDesde.TryGetValue(zona.Hash, out long desde)) return;
		if (NowMs() - desde < (long)(Povoamento.SegundosAteOEmbaralho * 1000)) return;

		// O MAPA PRIMEIRO, E A ORDEM AQUI E UM DEFEITO CONSERTADO. A marca era consumida ANTES desta
		// pergunta, com a razao "mesmo que nao haja um corpo pra mexer, o prazo foi cumprido" -- e
		// ela vale pra "nao havia corpo" (o embaralho ACONTECEU e nao mexeu em ninguem) e **nao vale
		// pra "nao havia mapa"**, que e o embaralho nao ter acontecido. Do jeito antigo uma zona sem
		// mapa residente queimava o prazo calada: a primeira chegada nao embaralhava por falta de
		// mapa, apagava a marca, e a segunda nao tinha mais o que cumprir.
		//
		// Hoje isso e inerte (planeta gerado nao recebe povoamento, entao nao ha habitante nessas
		// zonas) e e latente: no dia em que tiver, o sintoma seria "o embaralho as vezes nao
		// acontece" -- do tipo que nao deixa rastro nenhum pra procurar.
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return;   // zona sem mapa: nao ha como perguntar onde da pra pisar

		// AGORA SIM. Consumida antes de mexer nos corpos: mesmo que nenhum deles sirva (todos
		// nocauteados, ou nenhum e do mundo), o embaralho aconteceu e a proxima chegada nao repete.
		_vazioDesde.Remove(zona.Hash);

		ulong volta = _embaralhosDaZona.TryGetValue(zona.Hash, out ulong v) ? v + 1 : 1;
		_embaralhosDaZona[zona.Hash] = volta;

		// A CAMPANHA DESTA SUPERFICIE, UMA VEZ SO -- e nao uma consulta por corpo. Ver
		// <see cref="PasseiaSozinho"/>: ela e a unica pergunta do crivo que depende da zona.
		Invasao? campanha = InvasaoAqui(zona);

		int quantos = 0, desistiu = 0, parados = 0;
		foreach (ServerPlayer npc in ZoneList(zona.Hash))
		{
			if (!PasseiaSozinho(npc, campanha)) { parados++; continue; }

			// A SEMENTE E (universo, zona, ESTE corpo, esta volta). O `Papel.Semente` e o que ja
			// distingue um habitante do outro desde o nascimento -- reusar o id do corpo faria o
			// sorteio depender da ordem em que os corpos foram criados no processo.
			Random r = SorteioDeNpc.Sorteador(
				Espaco.Misturar(SeedDoUniverso, zona.Hash,
								Espaco.Misturar(npc.Papel!.Semente, volta, 0)), "embaralho");

			const int t = ZoneCollision.TileSize;
			int cx = (int)MathF.Floor(npc.Pos.X / t);
			int cy = (int)MathF.Floor(npc.Pos.Y / t);

			// ============================ "CUIDADO PRA N COLOCAR DENTRO DE PAREDES" ============================
			// a segunda com o que fazer quando
			// nao da: *"Se o sorteio nao achar lugar livre em N tentativas, o NPC fica onde estava --
			// e melhor nao ter andado que ter afundado."*
			//
			// A PERGUNTA NAO E UMA CHECAGEM NOVA. `ServeDeChao` e literalmente a que o
			// `PontoLivrePerto` faz anel por anel pra por todo corpo no mundo (o berco, o pouso, o
			// habitante, o defensor da bandeira) -- ela so deixou de ser funcao local pra poder ter um
			// segundo consumidor. Responde as tres recusas de uma vez: parede (inclusive obra
			// levantada e porta fechada, que entram nas camadas de runtime), beirada do mapa (de onde
			// nao se pode dar um passo) e AGUA (livre pela colisao, parada pela regra de personagem).
			// Uma checagem escrita aqui seria a copia que nao soube da agua no dia em que a agua entrou.
			//
			// O QUE MUDOU FOI QUEM ESCOLHE O PONTO, e a razao inteira esta em `TentativasDoEmbaralho`:
			// o `PontoLivrePerto` nunca desiste (varre 64 aneis e devolve um ponto a TRINTA tiles, que
			// e o teleporte que o dono nao pediu) e a busca por aneis e DIRECIONAL (os habitantes de um
			// bolsao de pedra sairiam todos pro mesmo lado). Aqui os N candidatos ja nascem dentro do
			// raio, e quem nao acha lugar nenhum simplesmente nao andou.
			// ==============================================================================================
			Vec2? achou = null;
			for (int n = 0; n < Povoamento.TentativasDoEmbaralho && achou == null; n++)
			{
				int dx = r.Next(Povoamento.TilesMinDoEmbaralho, Povoamento.TilesMaxDoEmbaralho + 1)
					   * (r.Next(2) == 0 ? -1 : 1);
				int dy = r.Next(Povoamento.TilesMinDoEmbaralho, Povoamento.TilesMaxDoEmbaralho + 1)
					   * (r.Next(2) == 0 ? -1 : 1);

				// O CENTRO DA CELULA, e nao o ponto cru: um ponto na quina de uma celula livre
				// encostada em parede ja nasce em violacao pro `MoveRules`. Mesma nota do funil.
				if (mapa.ServeDeChao(cx + dx, cy + dy)) achou = mapa.CentroDaCelula(cx + dx, cy + dy);
			}

			// AS N TENTATIVAS ERRARAM: ELE FICA ONDE ESTAVA. Nao ha meio-termo a inventar aqui --
			// afundar na pedra e pior do que nao ter andado, e "empurra pro chao livre mais proximo"
			// (a tentacao) e o teleporte de volta.
			if (achou == null) { desistiu++; continue; }

			npc.Pos = achou.Value;

			// O PASSO INTERROMPIDO NAO SOBREVIVE AO TELEPORTE: um corpo que congelou no meio de uma
			// caminhada acordaria "andando" pra um destino que ficou pra tras.
			npc.Moving = false;
			quantos++;
		}

		if (quantos > 0 || desistiu > 0)
			GD.Print($"[povoamento] {zona.Name} ficou {(NowMs() - desde) / 60000} min sem ninguem: "
				   + $"{quantos} habitantes mudaram de lugar (volta {volta}"
				   + (desistiu > 0 ? $", {desistiu} sem lugar livre perto" : "")
				   + (parados > 0 ? $", {parados} de posto ou fora do crivo" : "") + ")");
	}

	/// <summary>
	/// ============================ QUEM ANDA SOZINHO -- E POR ISSO EMBARALHA ============================
	/// O embaralho e **a simulacao do passeio que ninguem viu**. Entao o crivo nao e "quem e NPC": e
	/// quem, com um jogador na zona, TERIA andado por conta propria naqueles cinco minutos. Quem nao
	/// anda por conta propria nao tem passeio a simular, e move-lo nao e vida -- e o jogo se enganando.
	///
	/// O dono listou os casos e deu a razao numa frase so: *"qualquer um cujo lugar seja o papel dele.
	/// Um NPC de conversa que sai andando quebra a missao de quem vai falar com ele."*
	///
	/// ============================ OS QUATRO QUE FICAM PARADOS ============================
	///   1. **O QUE NAO E CORPO DO MUNDO** -- o crivo e o de `Core/Npc/Gente.cs` e nao um `if` novo. O
	///      boneco do corpo largado e o reflexo da mente nao sao NPC do mundo, e teleportar o boneco de
	///      quem esta meditando moveria o CORPO DE UM JOGADOR pra longe de onde ele se deitou. (O
	///      proprio recem-chegado cai aqui tambem: ele e jogador.);
	///
	///   2. **O CAIDO** -- nocauteado e morto ficam onde cairam. Um corpo no chao que muda de lugar
	///      nao parece que passeou, parece que o jogo se enganou. (O morto ainda sai do mundo no
	///      proximo tique de combate; mexer nele seria trabalho pra um cadaver.);
	///
	///   3. **O CHEFE DE SAGA** (<see cref="TipoDeNpc.Chefe"/>) -- quem o move e o ROTEIRO
	///      (`TickDoRoteiro`), nao a decisao dele. Ele e um DESTINO: a saga anuncia ao mundo que Freeza
	///      chegou a Namek e manda gente ate ele; o BP dele e promessa e o lugar dele e parte do papel.
	///      Repare que hoje ele CAI no `PasseioDeHabitante` quando esta sem presa -- o ramo do
	///      habitante e compartilhado --, e mesmo assim nao entra aqui: o criterio e o PAPEL, e nao o
	///      ramo de codigo que por acaso o dirige;
	///
	///   4. **O DEFENSOR DA BANDEIRA** -- ele nasce `cidadao` (`NascerNpc("cidadao", ...)`, na
	///      `GameServer.Invasao.cs`), entao o TIPO nao o distingue: quem distingue e estar na lista de
	///      <see cref="Invasao.Defensores"/>. O lugar dele e o `PontoPertoDaBandeira`, e o caso e real
	///      e nao teorico -- um invasor pode largar a campanha no meio e deslogar, e a guarnicao fica
	///      sozinha num planeta vazio ate o prazo vencer. Sem esta linha, quem voltasse encontraria a
	///      defesa espalhada pelo mapa em vez de em volta da bandeira.
	///
	/// ============================ E OS QUE O DONO CITOU E ESTE PORT NAO TEM ============================
	/// Ficam escritos pra a ausencia ser PENDENCIA e nao esquecimento -- no dia em que nascerem, o
	/// lugar de ensina-los ao embaralho e este metodo, e nao um `if` no laco:
	///
	///   * **REI / PRINCIPE no trono** -- neste port sao CARGOS DE JOGADOR (`GameServer.CargoPortas.cs`),
	///     nao corpos. Nao ha rei de NPC pra sentar em trono nenhum;
	///   * **NPC DE CONVERSA (King Kai, Enma)** -- nao existem como corpo. A mesa do Enma e uma
	///     COORDENADA pra onde o morto e mandado (`GameServer.Alem.cs`), sem ninguem sentado nela;
	///   * **DONO DE LOJA** -- as lojas deste port sao verbo e aba de HTML, nao balconista.
	/// ==============================================================================================
	/// </summary>
	/// <param name="campanha">A invasao desta superficie, resolvida UMA vez pelo chamador (ou nula).</param>
	private bool PasseiaSozinho(ServerPlayer npc, Invasao? campanha)
	{
		if (!EhNpcDoMundo(npc)) return false;
		if (npc.Ficha.dead || npc.Ficha.KO) return false;
		if (npc.Papel!.Molde.Tipo == TipoDeNpc.Chefe) return false;
		if (campanha != null && campanha.Defensores.ContainsKey(npc.Id)) return false;
		return true;
	}
}
