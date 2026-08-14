using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ O CORTE DA VOZ -- E ELE E O SISTEMA INTEIRO ============================
/// *"cria um sistema de chat de VOZ LOCAL (voip) q ao apertar V vc consegue falar com pessoas PERTO de
/// vc, e com EFEITO DE DISTANCIA caso se afaste, e tb se tiver ATRAS DE PAREDE da uma ABAFADA"*.
///
/// A frase tem tres pedacos e eles moram em tres lugares:
///
///   | pedaco             | onde mora            | quem decide                                  |
///   |--------------------|----------------------|----------------------------------------------|
///   | apertar V, capturar| `Client/Microfone.cs`| o dono do microfone                          |
///   | **QUEM RECEBE**    | **este arquivo**     | **o servidor, e mais ninguem**               |
///   | como aquilo soa    | `Client/VozOuvida.cs`| o alto-falante de quem ouve                  |
///
/// ============================ "MANDA PRA TODOS E ABAIXA O VOLUME" E OUTRA COISA ============================
/// O caminho preguicoso -- relatar cada quadro pra zona inteira e deixar o cliente atenuar -- SOA igual
/// e **nao e a mesma coisa**: quem le os pacotes ouve a sala toda, e nao ha volume que desfaca um byte
/// entregue. Este port ja pagou esse erro duas vezes: o **sigilo do BP** (a API do corte foi escrita e
/// ficou orfa; o numero vazava por sete lugares) e a **barra de vida**, que so parou de mentir quando o
/// numero deixou de sair do servidor. Com voz e pior, porque nao e um numero -- e a voz de uma pessoa.
///
/// Entao: **quem esta fora nao recebe byte nenhum**. A distancia e a parede so modelam o que chega em
/// quem ja tinha direito de ouvir.
/// ==========================================================================================================
///
/// ============================ O ALCANCE E O DA FALA, E NAO UM NUMERO NOVO ============================
/// <see cref="RaioDaVista"/> -- os mesmos 22 tiles do `sayType()` do BYOND que o chat local ja usa
/// (`GameServer.Chat.cs`). Duas nocoes de "perto" divergem no dia em que alguem mexer numa: o jogador
/// diria "eu te ouvi falar mas o texto nao chegou", e nada na tela explicaria.
///
/// O GRITO NAO ENTRA. Texto tem `RaioDoGrito` porque no original um "!" transforma a fala; a voz nao
/// tem como saber que alguem gritou -- a amplitude do microfone e o quanto a pessoa esta perto dele, e
/// nao o quanto ela gritou. Um alcance maior "quando fala alto" seria dar 37 tiles a quem encostasse a
/// boca no microfone.
/// ====================================================================================================
///
/// ============================ DE ONDE A VOZ SAI: `pl.Pos`, E ESSA E A DECISAO ============================
/// **O `ServerPlayer` e a PESSOA, e nao o corpo.** Quem medita fundo, quem entra na ponte da nave e quem
/// larga o corpo tem o proprio `ServerPlayer` MOVIDO pra outra zona -- o que fica pra tras e um
/// *boneco* (`GameServer.CorpoLargado.cs`), que nao esta no `_players` e nao tem `Peer`.
///
/// Consequencia, escrita de proposito porque ela e uma REGRA e nao um efeito colateral:
///
///   * quem esta **dentro da propria mente** fala com quem esta la dentro -- e o visitante que meditou
///     ao lado ouve. Quem esta em pe ao lado do corpo que medita **nao ouve nada**, e ve um corpo
///     meditando em silencio. E o certo: a pessoa nao esta ali;
///   * quem esta na **ponte da nave** fala com quem esta na ponte. O corpo largado no chao do interior
///     nao emite som -- ele nao esta falando, ele esta parado;
///   * quem esta **nocauteado ou morto** continua com o `ServerPlayer` no proprio corpo, entao continua
///     falando dali. Paridade exata com o texto, que nunca calou quem esta no chao.
///
/// A alternativa -- fazer a voz sair do BONECO -- foi recusada: ela poria a voz de alguem saindo de um
/// corpo que a propria pessoa nao esta ocupando, ouvida por gente que ela nao ve e que nao a ve falar.
/// Um jogador na sala escutaria um monologo sem interlocutor vindo de um corpo em transe.
/// ========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A TORNEIRA DE CADA FALANTE -- o teto que impede um cliente modificado de virar amplificador.
	///
	/// Por ID DE CORPO e nao por conta: e o corpo que tem posicao e zona, e e a posicao que decide o
	/// leque. Uma conta com dois corpos vivos nao existe (o login e exclusivo, ver `Login`).
	///
	/// LIMPA NA SAIDA (<see cref="EsquecerVoz"/>). Sem isso o dicionario cresceria com o id de todo
	/// mundo que ja passou pelo servidor, e o credito de quem voltasse seria o que ele deixou.
	/// </summary>
	private readonly Dictionary<int, VozLocal.Torneira> _vozes = [];

	/// <summary>
	/// A ULTIMA SEQUENCIA que cada falante mandou -- o que vai NO PACOTE pra o decodificador saber
	/// que perdeu quadro.
	///
	/// ============================ POR QUE O SERVIDOR NUMERA E NAO REPASSA ============================
	/// O cliente manda a sequencia DELE, e ela e util pra nada aqui: o que o ouvinte precisa saber e
	/// quantos quadros faltaram **no caminho ate ele**, e esse caminho e o do servidor pra ele. Repassar
	/// o numero do falante misturaria as duas perdas -- e um cliente modificado que mandasse sequencias
	/// aleatorias faria o decodificador alheio inventar silencio a esmo.
	///
	/// O contador e do SERVIDOR, cresce um por quadro ACEITO (depois da torneira), e por isso um
	/// buraco nele quer dizer exatamente uma coisa: perda de rede entre nos dois.
	/// ============================================================================================
	/// </summary>
	private readonly Dictionary<int, ushort> _seqDeVoz = [];

	/// <summary>Buffer de leitura reusado -- um quadro por vez, e alocar 50x por segundo e lixo.</summary>
	private readonly byte[] _quadroDeVoz = new byte[VozLocal.MaxBytesDeQuadro];

	// =====================================================================
	// A PAREDE
	// =====================================================================
	/// <summary>
	/// A RESPOSTA DE "HA PAREDE ENTRE ESTES DOIS", GUARDADA POR UM INSTANTE.
	///
	/// ============================ NAO E CARA -- E RAPIDA DEMAIS PRA SER PAGA POR PACOTE ============================
	/// **MEDIDO** (`PathBlocked` sobre os `.vis` de verdade, Release):
	///
	///   | mapa                        | 12 tiles | 22 tiles |
	///   |-----------------------------|----------|----------|
	///   | z01_Earth (aberto)          | 0,611 us | 1,040 us |
	///   | z12_Lookout (9,3% cego)     | 0,622 us | 1,059 us |
	///   | z31_God_Realm (16,1%)       | 0,630 us | 1,066 us |
	///   | z23_Earth_Cave (84,7% cego) | 0,558 us | 0,897 us |
	///
	/// Teto honesto: **2,145 us por par** (campo aberto e distancia sempre no limite -- o raio percorre
	/// tudo sem achar nada; mapa cheio de parede e mais BARATO, sai no primeiro passo). Quatro falantes
	/// x vinte ouvintes = 172 us, ou 0,52% de um tique de 30 Hz. Ou seja: **da pra rodar por par, por
	/// quadro, e ainda sobra tique.**
	///
	/// Entao por que guardar? Porque **este e o unico custo do servidor cuja frequencia quem dita e o
	/// PACOTE DE FORA, e nao o relogio dele**. Tudo o mais aqui roda a 30 Hz porque o tique roda a
	/// 30 Hz; a voz roda a 50 Hz por falante, e um cliente na borda da torneira mantem esse ritmo o
	/// dia inteiro. O cache troca "custo por pacote recebido" por "custo por tempo de parede", que e a
	/// unidade em que se pode prometer alguma coisa.
	///
	/// **100 ms**, e o numero sai do mundo: a caminhada e 160 px/s, ou seja 16 px em 100 ms -- **meio
	/// tile**. Ninguem atravessa um vao de porta dentro da validade, e a transicao da abafada e
	/// suavizada no cliente de qualquer forma (ver `VozOuvida`), entao 100 ms de atraso na mudanca de
	/// parede nao tem como ser ouvido. Um segundo ja seria audivel: daria pra sair de tras do muro e
	/// continuar abafado por vinte quadros.
	/// ==========================================================================================================
	/// </summary>
	private const long MsDeValidadeDaParede = 100;

	private readonly Dictionary<long, (long Ate, bool Parede)> _paredeDaVoz = [];

	/// <summary>
	/// O MAPA DO QUE **CEGA** nesta zona -- e nao o que bloqueia.
	///
	/// ============================ A DIFERENCA E O SISTEMA INTEIRO ============================
	/// O `.col` e o `.vis` divergem DE PROPOSITO (ver `Client/Visao.cs`): uma porta CEGA e nao bloqueia;
	/// a beirada do mapa BLOQUEIA e nao cega. Perguntar ao `.col` daria a resposta errada nos dois casos
	/// mais comuns do jogo -- e faria a voz e a vista discordarem sobre o que e parede, que e o defeito
	/// que este projeto mais evita: duas verdades sobre a mesma coisa.
	///
	/// NULO E UMA RESPOSTA LEGITIMA: zona sem mapa de vista (o espaco, a dimensao mental) nao esconde
	/// nada -- e ali o olho do cliente tambem nao esconde nada (`World`: `_veu.Mapa = null`). O sistema
	/// degrada pro MESMO lugar em que a vista degrada, e nao pra um lugar proprio.
	/// ======================================================================================
	/// </summary>
	private ZoneCollision? MapaDaVista(ZoneKey zona) =>
		zona.Kind switch
		{
			// gerado: a montanha cega, a arvore nao. Assado na thread de geracao (ver `ZonaGerada.Vista`)
			ZoneKey.KindProcedural =>
				_zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? v) ? v.Vista : null,
			// o interior da nave: o casco e as paredes da sala, pela MESMA planta que o cliente desenha
			ZoneKey.KindInterior =>
				Jandirus.Core.Tech.NaveGrande.EhInterior(zona, out _)
					? Jandirus.Core.Tech.NaveGrande.Planta().QueEsconde
					: _catalogo?.Get(zona)?.Vista,
			_ => _catalogo?.Get(zona)?.Vista,
		};

	/// <summary>
	/// HA PAREDE ENTRE ESTES DOIS? Consulta o bitset do que cega, com a validade de
	/// <see cref="MsDeValidadeDaParede"/>.
	///
	/// A CHAVE E ORDENADA (menor id primeiro) porque a pergunta e simetrica: uma parede entre A e B e a
	/// mesma parede entre B e A. Sem ordenar, duas pessoas conversando manteriam DUAS entradas dizendo
	/// a mesma coisa -- e o dia em que elas discordassem (por um passo entre as duas consultas) um dos
	/// dois ouviria abafado e o outro nao, na mesma parede.
	/// </summary>
	private bool ParedeEntre(ServerPlayer a, ServerPlayer b, ZoneCollision? mapa, long agora)
	{
		if (mapa == null) return false;

		int lo = Math.Min(a.Id, b.Id), hi = Math.Max(a.Id, b.Id);
		long chave = ((long)lo << 32) | (uint)hi;

		if (_paredeDaVoz.TryGetValue(chave, out (long Ate, bool Parede) v) && agora < v.Ate)
			return v.Parede;

		// O OLHO FICA NOS PES, como no campo de visao do cliente: `pl.Pos` ja e o ponto do corpo no
		// chao (a altura de voo e desenho, ver `GameServer.Voo.cs`). Duas convencoes de "de onde o raio
		// sai" seriam a terceira chance de a voz e a vista discordarem.
		bool parede = mapa.PathBlocked(a.Pos, b.Pos);
		_paredeDaVoz[chave] = (agora + MsDeValidadeDaParede, parede);
		return parede;
	}

	/// <summary>
	/// Tira do cache o que ja venceu. Chamado no tique, e so quando o dicionario passa do tamanho de
	/// uma conversa de verdade -- varrer um dicionario de tres entradas 30x por segundo seria pagar
	/// pela limpeza mais do que pelo que se limpa.
	/// </summary>
	private void TickDaVoz()
	{
		if (_paredeDaVoz.Count < 64) return;

		long agora = NowMs();
		List<long>? mortas = null;
		foreach ((long chave, (long ate, bool _)) in _paredeDaVoz)
			if (agora >= ate) (mortas ??= []).Add(chave);
		if (mortas == null) return;
		foreach (long k in mortas) _paredeDaVoz.Remove(k);
	}

	/// <summary>Este corpo saiu: some com a torneira, a sequencia e o que sobrou do cache dele.</summary>
	private void EsquecerVoz(int id)
	{
		_vozes.Remove(id);
		_seqDeVoz.Remove(id);

		// O CACHE E POR PAR, entao a saida de um id deixa entradas mortas com o id do outro. Elas
		// vencem sozinhas em 100 ms -- mas um id REUSADO dentro dessa janela herdaria a resposta de
		// parede de outra pessoa. Ids sao sequenciais e nao se reciclam tao rapido; a varredura e
		// barata e fecha o buraco sem depender disso.
		List<long>? mortas = null;
		foreach (long chave in _paredeDaVoz.Keys)
			if ((int)(chave >> 32) == id || (int)(chave & 0xFFFFFFFF) == id) (mortas ??= []).Add(chave);
		if (mortas == null) return;
		foreach (long k in mortas) _paredeDaVoz.Remove(k);
	}

	// =====================================================================
	// O QUADRO CHEGOU
	// =====================================================================
	/// <summary>
	/// UM QUADRO DE VOZ CHEGOU. Este metodo e o corte inteiro.
	///
	/// A ordem das recusas nao e arbitraria -- e da mais barata pra mais cara, e da mais geral pra mais
	/// especifica: tamanho (uma comparacao), torneira (uma conta), zona (uma busca), distancia (um
	/// quadrado), vaga entre os quatro (um laco curto), parede (um raio, com cache).
	/// </summary>
	/// <param name="calado">
	/// A pessoa esta silenciada por admin. **Vem de fora, do mesmo lugar que o texto pergunta** --
	/// ver o `case Protocol.C2S.Voz` no dispatch, colado no `case Protocol.C2S.Chat`.
	/// </param>
	private void RecebiVoz(ServerPlayer a, byte[] dados, int tam, bool calado)
	{
		long agora = NowMs();

		if (!_vozes.TryGetValue(a.Id, out VozLocal.Torneira? torneira))
		{
			torneira = new VozLocal.Torneira();
			_vozes[a.Id] = torneira;
		}

		// A TORNEIRA CORRE MESMO PRA QUEM ESTA CALADO, e isso e de proposito: ela e o teto de TRAFEGO,
		// nao a punicao. Se o mute pulasse a torneira, calar alguem tiraria dele exatamente a unica
		// coisa que o impedia de inundar o servidor.
		if (!torneira.Cabe(agora, tam)) return;

		// ============================ O MUTE E O MESMO DO TEXTO, E ELE E MUDO ============================
		// `EstaCalado` e por CONTA e vive no disco (`AccountSave.Calada`) -- trocar de personagem nao
		// desfaz a punicao, e nem reiniciar o servidor. A voz respeita a mesma marca pelo mesmo funil.
		//
		// **SEM `Avisar` AQUI**: um aviso por quadro recusado sao 50 linhas de chat por segundo, e o
		// canal do aviso e CONFIAVEL -- a punicao viraria uma inundacao pior que a fala. Quem aperta a
		// tecla e avisado UMA vez, no cliente, quando o servidor recusa o inicio da fala (ver
		// `C2S.Voz` no dispatch).
		// ==============================================================================================
		if (calado) return;

		ushort seq = _seqDeVoz.TryGetValue(a.Id, out ushort s) ? (ushort)(s + 1) : (ushort)0;
		_seqDeVoz[a.Id] = seq;

		_ouvintes.Clear();
		// ============================ O CORTE, OU O DEFEITO QUE A BANCADA POE NO LUGAR DELE ============================
		// Em jogo isto e uma chamada so: <see cref="QuemOuveAVoz"/>. Ver <see cref="CorteQuebradoDeTeste"/>
		// pro que o `--vozdupla` troca aqui, e por que a troca precisa ser NESTE ponto.
		//
		// O `_vozDuplaLigada` vem PRIMEIRO de proposito: ele e falso em jogo, entao o `&&` corta antes de
		// tocar no delegate. Um processo que nao subiu como bancada nao consegue executar o defeito nem
		// que alguem escreva nele -- a chave nao esta no campo, esta na linha de comando.
		// ==========================================================================================================
		if (_vozDuplaLigada && CorteQuebradoDeTeste is { } defeito) defeito(a, agora, _ouvintes);
		else QuemOuveAVoz(a, agora, _ouvintes);

		foreach ((ServerPlayer o, byte dist, bool parede) in _ouvintes)
		{
			// ============================ TER CONEXAO NAO E TER DIREITO DE OUVIR ============================
			// NPC, clone e boneco largado tem `Peer` nulo. Eles passam pelo corte inteiro acima -- e tem
			// que passar, porque o corte responde *"quem tem direito de ouvir isto"*, e a resposta pra um
			// corpo que esta ali e a mesma tenha ele dono ou nao. O que muda e que **nao ha pra quem
			// entregar**, e isso e um fato de rede.
			//
			// A separacao nao e purismo: com o `Peer` dentro do corte, o corte deixaria de ser mensuravel
			// (uma bancada forja corpos, e corpo forjado nao tem conexao). Custa uma consulta de parede
			// por NPC no alcance -- 2 us, e o cache a cobra uma vez a cada 100 ms.
			// ==========================================================================================
			if (o.Peer == null) continue;

			var w = Protocol.Begin(Protocol.S2C.Voz);
			w.Put(a.Id);
			w.Put(seq);
			w.Put(dist);
			w.Put((byte)(parede ? 1 : 0));
			w.Put((byte)tam);
			w.Put(dados, 0, tam);

			// NAO CONFIAVEL, SEMPRE. Quadro de voz perdido se joga fora: retransmitir chega DEPOIS do
			// proximo e trava a fila atras dele -- a fala sairia com atraso crescente, que e pior do que
			// um estalo de 20 ms. E o exato oposto do chat de TEXTO, que e confiavel porque uma frase
			// faltando vira "ele me ignorou" (ver `GameServer.Chat.Mandar`).
			o.Peer.Send(w, Protocol.ChannelVoz, DeliveryMethod.Unreliable);

			// ============================ O QUE O SERVIDOR ENTREGOU -- SO BANCADA ============================
			// Nula em jogo. (quem falou, pra quem foi, quantos bytes de audio).
			//
			// Ela existe por causa da frase que abre este arquivo: *"quem esta fora nao recebe byte
			// nenhum"*. Essa afirmacao e sobre o que **nao** sai, e o `--vozteste` so consegue prova-la
			// pelo LEQUE (corpo forjado nao tem `Peer`, entao ali "nao entregou" e verdade sempre). Com
			// dois clientes de verdade, o unico jeito de a AUTORIDADE dizer quantos bytes sairam pra
			// alguem e contar na linha em que eles saem -- esta.
			// ==========================================================================================
			EspiaoDaVoz?.Invoke(a.Id, o.Id, tam);
		}
	}

	/// <summary>Ver o bloco em <see cref="RecebiVoz"/>. Nula em jogo.</summary>
	public static Action<int, int, int>? EspiaoDaVoz;

	/// <summary>
	/// ============================ O DEFEITO NO LUGAR DO CORTE -- SO `--vozdupla` ============================
	/// Nula em jogo, e **inalcancavel** fora da bancada: o `RecebiVoz` so olha pra ela quando o processo
	/// subiu com `--vozdupla` (ver a linha, e o `_vozDuplaLigada` na frente do `&&`).
	///
	/// ============================ POR QUE UMA BANCADA PRECISA PODER QUEBRAR ISTO ============================
	/// A afirmacao central deste sistema -- *"quem esta fora do alcance nao recebe byte nenhum"* -- e sobre
	/// o que **nao** sai. Uma checagem que conte zero bytes fica verde de dois jeitos: porque o corte
	/// funciona, ou porque **nada** estava sendo mandado (o falante mudo, a fase errada, o espiao
	/// desligado, a conta trocada). Os dois desfechos sao o mesmo zero, e o segundo passa despercebido
	/// pra sempre.
	///
	/// O unico jeito de separar os dois e por o defeito na frente da checagem e exigir que ela fique
	/// VERMELHA. E o defeito tem que ser o de verdade: **o corte trocado por "manda pra zona inteira"**,
	/// que e literalmente o caminho preguicoso que este arquivo recusa no cabecalho. Trocado AQUI, tudo o
	/// mais continua sendo producao -- a torneira, o mute, a sequencia, o `Peer.Send`, o espiao e o audio
	/// Opus de verdade que veio do outro processo. So a lista de quem ouve muda.
	///
	/// A alternativa seria a bancada FORJAR pacotes de voz e manda-los ela mesma pra provar que o contador
	/// do outro lado acorda. Isso mede o contador e nao mede o corte: seria uma imitacao do vazamento, e
	/// uma imitacao nao passa por `RecebiVoz`.
	/// ====================================================================================================
	/// </summary>
	internal Action<ServerPlayer, long, List<(ServerPlayer Quem, byte Dist, bool Parede)>>? CorteQuebradoDeTeste;

	/// <summary>Lista reusada do leque -- alocar uma por quadro de audio seria lixo por lixo.</summary>
	private readonly List<(ServerPlayer Quem, byte Dist, bool Parede)> _ouvintes = [];

	/// <summary>
	/// **O CORTE.** Quem tem direito de ouvir esta voz, a que distancia, e com parede ou sem.
	///
	/// ============================ ELE E UMA FUNCAO PORQUE ELE PRECISA SER MEDIDO ============================
	/// Este metodo E o sistema. Deixa-lo dissolvido no laco de envio o tornaria imensuravel: uma bancada
	/// de servidor forja corpos, corpo forjado nao tem conexao, e um corte escrito dentro do `Send`
	/// so poderia ser testado com gente de verdade conectada -- ou seja, nunca.
	///
	/// Escrito assim, `--vozteste` pergunta a ELE (e nao a uma copia da regra) quem ouviria o que, com
	/// mapas de verdade e distancias de verdade. Ver `GameServer.VozTeste.cs`.
	/// ======================================================================================================
	///
	/// A ORDEM DAS RECUSAS E DA MAIS BARATA PRA MAIS CARA: zona (a lista ja veio cortada), distancia (um
	/// quadrado), vaga entre as quatro (um laco curto), parede (um raio, com cache de 100 ms).
	/// </summary>
	public void QuemOuveAVoz(ServerPlayer a, long agora, List<(ServerPlayer Quem, byte Dist, bool Parede)> saida)
	{
		ZoneCollision? vista = MapaDaVista(a.Zone);
		float alcance = RaioDaVista;
		float alcance2 = alcance * alcance;

		// A LISTA DA ZONA E O PRIMEIRO CORTE, e o mais forte deles: `ZoneList` e o corte de interesse
		// que ja separa dois planetas, duas mentes e dois interiores de nave (o hash da `ZoneKey` E a
		// separacao -- ver `GameServer.NaveGrande.cs`). Nada disso precisou de codigo aqui, e e por isso
		// que a voz de quem esta dentro da propria mente nao vaza pra quem esta ao lado do corpo dele.
		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o.Id == a.Id) continue;

			Vec2 d = o.Pos - a.Pos;
			float dist2 = d.X * d.X + d.Y * d.Y;
			if (dist2 > alcance2) continue;

			float dist = MathF.Sqrt(dist2);
			if (!CabeNasVozesDe(o, a, dist, alcance2, agora)) continue;

			saida.Add((o, VozLocal.DistanciaEmByte(dist, alcance), ParedeEntre(a, o, vista, agora)));
		}
	}

	/// <summary>
	/// <paramref name="falante"/> CABE ENTRE AS QUATRO VOZES DE <paramref name="ouvinte"/>?
	///
	/// ============================ O TETO E DE BANDA, E ELE MUDA A ORDEM DA CONTA ============================
	/// Sem ele o leque e M x N. Com ele e 4 x N: vinte pessoas todas falando saem de 1,54 MB/s pra
	/// 308 KB/s de subida. Ver <see cref="VozLocal.MaxFalantesPorOuvinte"/> pra tabela.
	///
	/// QUEM CAI E O MAIS LONGE, sempre -- e o que o ouvinte menos entenderia de qualquer jeito. O
	/// desempate e pelo ID e nao pela ordem da lista: sem ele, duas pessoas exatamente a mesma distancia
	/// trocariam de vaga a cada quadro e as duas sairiam picotadas.
	///
	/// O LACO E CURTO POR CONSTRUCAO: ele percorre `_vozes`, que so tem quem falou nos ultimos 250 ms.
	/// Com aperta-pra-falar isso e uma a tres pessoas, e o dicionario e limpo na saida.
	/// ====================================================================================================
	/// </summary>
	private bool CabeNasVozesDe(ServerPlayer ouvinte, ServerPlayer falante, float dist, float alcance2,
								long agora)
	{
		int naFrente = 0;
		foreach ((int id, VozLocal.Torneira t) in _vozes)
		{
			if (id == falante.Id || id == ouvinte.Id) continue;
			if (!t.Falando(agora)) continue;
			if (!_players.TryGetValue(id, out ServerPlayer? outro)) continue;
			if (outro.Zone.Hash != ouvinte.Zone.Hash) continue;

			Vec2 d = outro.Pos - ouvinte.Pos;
			float d2 = d.X * d.X + d.Y * d.Y;
			if (d2 > alcance2) continue;

			float dOutro = MathF.Sqrt(d2);
			// mais perto vence; empate vai pro menor id (estavel entre quadros)
			if (dOutro < dist || (dOutro == dist && id < falante.Id)) naFrente++;
			if (naFrente >= VozLocal.MaxFalantesPorOuvinte) return false;
		}
		return true;
	}

	// =====================================================================
	// A BANCADA LE DAQUI
	// =====================================================================
	/// <summary>
	/// QUANTOS QUADROS ESTE FALANTE JA TEVE RECUSADOS pela torneira. So a bancada e o admin leem.
	///
	/// Existe porque o teto e uma afirmacao sobre o que **nao** acontece: "um cliente modificado nao
	/// consegue mandar mais que 50 quadros por segundo". Nao ha foto disso, e o sintoma de o teto estar
	/// quebrado (banda subindo) so aparece com gente de verdade no servidor de verdade.
	/// </summary>
	public int QuadrosDeVozRecusadosDeTeste(int id) =>
		_vozes.TryGetValue(id, out VozLocal.Torneira? t) ? t.Recusados : 0;

	/// <summary>Esta pessoa esta falando AGORA? Ver <see cref="VozLocal.MsAteCalar"/>.</summary>
	public bool FalandoPorVozDeTeste(int id) =>
		_vozes.TryGetValue(id, out VozLocal.Torneira? t) && t.Falando(NowMs());

	/// <summary>
	/// ESTE FALANTE JA TEVE ALGUM QUADRO ACEITO? -- ou seja, a sequencia dele existe.
	///
	/// ============================ COMO SE MEDE UM PACOTE QUE NAO SAIU ============================
	/// Uma bancada de servidor forja corpos, e corpo forjado nao tem conexao: "nao entregou" seria
	/// verdade sempre e nao provaria portao nenhum. Este contador so avanca DEPOIS do portao do mute
	/// -- entao "a sequencia nao existe" e a evidencia de que o quadro morreu antes do leque, e nao
	/// de que faltava alguem pra receber. Ver `GameServer.VozTeste.cs`.
	/// ========================================================================================
	/// </summary>
	public bool TemSequenciaDeVozDeTeste(int id) => _seqDeVoz.ContainsKey(id);
}
