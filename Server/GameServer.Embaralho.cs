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

		// CONSUMIDA ANTES DE QUALQUER TRABALHO: mesmo que nao haja um corpo pra mexer, o prazo foi
		// cumprido e a proxima chegada nao tem o que embaralhar de novo.
		_vazioDesde.Remove(zona.Hash);

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return;   // zona sem mapa: nao ha como perguntar onde da pra pisar

		ulong volta = _embaralhosDaZona.TryGetValue(zona.Hash, out ulong v) ? v + 1 : 1;
		_embaralhosDaZona[zona.Hash] = volta;

		int quantos = 0;
		foreach (ServerPlayer npc in ZoneList(zona.Hash))
		{
			// SO CORPO DO MUNDO. O crivo e o de `Core/Npc/Gente.cs` e nao um `if` novo: o boneco do
			// corpo largado e o reflexo da mente nao sao NPC do mundo, e teleportar o boneco de quem
			// esta meditando moveria o CORPO DE UM JOGADOR pra longe de onde ele se deitou.
			if (!EhNpcDoMundo(npc)) continue;

			// QUEM ESTA NO CHAO NAO ANDOU. Nocauteado e morto ficam onde cairam -- um corpo caido que
			// muda de lugar nao parece que passeou, parece que o jogo se enganou. (Morto tambem sai
			// do mundo no proximo tique de combate; mexer nele seria trabalho pra um cadaver.)
			if (npc.Ficha.dead || npc.Ficha.KO) continue;

			// A SEMENTE E (universo, zona, ESTE corpo, esta volta). O `Papel.Semente` e o que ja
			// distingue um habitante do outro desde o nascimento -- reusar o id do corpo faria o
			// sorteio depender da ordem em que os corpos foram criados no processo.
			Random r = SorteioDeNpc.Sorteador(
				Espaco.Misturar(SeedDoUniverso, zona.Hash,
								Espaco.Misturar(npc.Papel!.Semente, volta, 0)), "embaralho");

			const int t = ZoneCollision.TileSize;
			int dx = r.Next(Povoamento.TilesMinDoEmbaralho, Povoamento.TilesMaxDoEmbaralho + 1)
				   * (r.Next(2) == 0 ? -1 : 1);
			int dy = r.Next(Povoamento.TilesMinDoEmbaralho, Povoamento.TilesMaxDoEmbaralho + 1)
				   * (r.Next(2) == 0 ? -1 : 1);

			// ============================ "CUIDADO PRA N COLOCAR DENTRO DE PAREDES" ============================
			// O dono pediu isso em letras maiusculas, e a resposta nao e uma checagem nova: e o MESMO
			// funil que ja poe todo corpo no mundo (`PontoDeHabitante`, o berco, o pouso, o defensor
			// da bandeira). `PontoLivrePerto` responde as tres recusas de uma vez, anel por anel --
			// parede (inclusive construcao levantada e porta fechada, que entram no bitset em
			// runtime), beirada do mapa (de onde nao se pode dar um passo) e AGUA (livre pela colisao,
			// parada pela regra de personagem).
			//
			// Uma checagem propria aqui seria a quarta copia da pergunta, e seria a copia que nao
			// soube da agua no dia em que a agua entrou.
			// ==============================================================================================
			npc.Pos = mapa.PontoLivrePerto(new Vec2(npc.Pos.X + dx * t, npc.Pos.Y + dy * t));

			// O PASSO INTERROMPIDO NAO SOBREVIVE AO TELEPORTE: um corpo que congelou no meio de uma
			// caminhada acordaria "andando" pra um destino que ficou pra tras.
			npc.Moving = false;
			quantos++;
		}

		if (quantos > 0)
			GD.Print($"[povoamento] {zona.Name} ficou {(NowMs() - desde) / 60000} min sem ninguem: "
				   + $"{quantos} habitantes mudaram de lugar (volta {volta})");
	}
}
