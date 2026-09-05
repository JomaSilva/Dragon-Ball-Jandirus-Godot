using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A SALA DO TEMPO -- A SESSAO, A COMIDA E A PRISAO. Porte do `htc_session()`
/// (`Code/Modules/Globals/TimeChamber.dm:74-118`) com as regras do dono (13.6) por cima.
///
/// ============================ O QUE ESTE ARQUIVO DECIDE ============================
/// A porta (`GameServer.SalaDoTempo.cs`) decide QUEM PASSA. Este decide o que acontece com quem
/// ja passou, e sao quatro coisas que andam no mesmo relogio:
///
///   1. a SESSAO -- 2 dias in-game de bonus, contados por TIQUE;
///   2. o ENVELHECIMENTO -- um ano de idade por dia in-game fechado;
///   3. a COMIDA -- duas porcoes perto da porta, repostas por CONSUMO, que somem com a sessao;
///   4. a PRISAO -- 2 minutos de janela depois do fim, e entao a porta tranca.
/// ==================================================================================
///
/// ============================ POR QUE A CONTA E EM DIAS IN-GAME ============================
/// "48 minutos" nao e um numero solto: o relogio deste mundo anda um dia a cada 24 minutos
/// (`Ceu.SegundosPorDia = 1440`), entao 48 min sao **exatamente 2 dias in-game**, que e a mesma
/// sessao de "2 anos de treino" que o original concede (`HTC_MAX_YEARS = 2`, um ano por dia).
///
/// Contar os minutos reais direto daria o mesmo resultado hoje e mentiria amanha -- e por isso o
/// numero que persiste (`ServerPlayer.SalaDiasDaSessao`) e em DIAS, e os minutos aparecem so nas
/// frases. Ver <see cref="Jandirus.Core.World.SalaDoTempo.SessaoEmDias"/>.
///
/// **POR TIQUE, E NAO POR RELOGIO DE PAREDE.** O passo e `Protocol.TickSeconds` (fixo), que e a
/// mesma promessa que o DM escreve em voz alta: *"a conta anda por ticks, entao a sessao entrega os
/// HTC_MAX_YEARS anos CRAVADOS mesmo com lag (antes: relogio de parede de 40 min x passos de tick
/// lentos = jogador expulso com ~1.2 anos)"*. Um servidor lento demora mais em minutos de parede e
/// entrega a sessao inteira.
///
/// A TRAVA DE PAREDE E A REDE DE SEGURANCA, e nao a regra: a contagem por tique **para** quando o
/// corpo nao esta em jogo (deslogar la dentro nao treina nem envelhece -- e o `if(!client) continue`
/// do DM), entao sem uma segunda conta bastaria deslogar pra congelar os 280x pra sempre. Passado o
/// dobro do tempo nominal desde a entrada, a sessao acabou, tenha o tique contado o que tiver.
/// ===========================================================================================
///
/// ============================ A PRISAO EXIGE QUE A PORTA TENHA SIDO OFERECIDA ============================
/// A regra do dono (13.6c) da 2 minutos entre o fim dos bonus e a tranca. A pergunta que ela nao
/// responde e: **e quem estava deslogado quando os 2 minutos passaram?**
///
/// A resposta deste port e que a janela e ARMADA PELO TIQUE, na primeira vez que ele ve a sessao
/// acabada num corpo EM JOGO -- e nao calculada do instante da entrada. As consequencias sao as
/// duas que se quer:
///
///   * jogando normalmente, a janela abre aos 48 min e a tranca cai aos 50, que e a regra ao pe
///     da letra;
///   * quem caiu (ou fechou o jogo) volta com a sessao acabada mas **solto**, e ganha os mesmos 2
///     minutos contados do login pra andar ate a porta.
///
/// Isso e uma divergencia deliberada da leitura literal, e ela existe porque a alternativa e
/// injusta de um jeito que nao tem graca: **prender alguem por uma porta que ele nunca teve a
/// chance de atravessar**. A prisao e o preco de teimar, nao o preco de cair a internet.
/// ======================================================================================================
///
/// ============================ A COMIDA REPOE POR CONSUMO, E ISSO E LITERAL ============================
/// O teto e duas porcoes simultaneas, e **nao ha temporizador nenhum**: uma porcao nasce quando a
/// sessao comeca e outra quando alguem come uma. A macieira (`GameServer.Interacao.cs`) e o
/// precedente do formato -- coisa parada no chao, com estoque, que se usa de perto --, e a
/// diferenca esta exatamente aqui: la o estoque volta por sorteio no relogio, aqui ele volta pelo
/// GESTO. Um relogio faria a dupla aprender a voltar de X em X minutos; o consumo faz a comida
/// existir enquanto alguem estiver comendo.
///
/// O EFEITO COLATERAL ESTA ACEITO E ESCRITO: se alguem DESTRUIR uma porcao (ela e obra, e obra
/// cai no soco), ela nao volta ate a proxima sessao -- porque repor uma porcao destruida seria
/// repor por evento que nao e consumo, e o primeiro passo de volta pro temporizador.
/// ==================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O TIPO DA OBRA QUE E UMA PORCAO. Sai do catalogo (`construcoes.json`), extraido do DM
	/// (`/obj/items/food/Cooked_Meat`, `Modules/Stamina/Food.dm:102-106`) -- nao ha arte nem nome
	/// digitado a mao aqui, como manda a PARTE 0.1.
	/// </summary>
	private const string TipoDaComida = "Cooked_Meat";

	/// <summary>
	/// QUANTAS PORCOES CABEM AO MESMO TEMPO. **Duas, uma por vaga** -- a lotacao e 2 (13.6a), e a
	/// comida foi pedida "pra dois jogadores". Um teto que acompanha a lotacao e o que impede a
	/// dupla de estocar comida no chao pra levar embora.
	/// </summary>
	private const int PorcoesDaSala = 2;

	/// <summary>
	/// A NUTRICAO DE UMA PORCAO -- `nutrition = 30` do `Cooked_Meat` (`Food.dm:105`), contra 6 da
	/// maca. Uma refeicao de verdade: o tanque cheio e 50 (`Nutricao.TanqueBase`).
	/// </summary>
	private const double NutricaoDaPorcao = 30;

	/// <summary>
	/// ONDE AS PORCOES NASCEM, em celulas do z13 -- **ladeando quem acabou de entrar**.
	///
	/// A saida e o vao de (144..147, 338) na parede da linha 337 (conferido no `.col`), e o corpo
	/// chega em (145,340). Estas duas celulas ficam encostadas no vao, uma de cada lado do ponto de
	/// chegada, na linha 339.
	///
	/// **AS DUAS PRECISAM CABER NO `AlcanceDeUso` (64 px = 2 tiles) MEDIDO DA CHEGADA**, e a bancada
	/// cobra isso: a primeira versao poe uma delas em (148,339), tres tiles a direita -- ela nascia,
	/// desenhava, e **nao havia como comer**. Comida visivel e inalcancavel e pior que comida
	/// nenhuma, porque parece defeito do jogador.
	/// </summary>
	private static readonly (int X, int Y)[] CelulasDaComida = [(143, 339), (147, 339)];

	/// <summary>
	/// O ULTIMO AVISO DE TEMPO RESTANTE JA DADO, em minutos -- o `htc_warned_left` do DM
	/// (`TimeChamber.dm:36`). Vivo tambem: e ruido de sessao, nao estado de personagem.
	/// </summary>
	private readonly Dictionary<int, int> _avisoDaSala = [];

	/// <summary>Os minutos em que a sessao avisa quanto falta -- os mesmos 10/5/1 do DM.</summary>
	private static readonly int[] AvisosEmMinutos = [10, 5, 1];

	// =====================================================================
	// A PERGUNTA QUE O RESTO DO SERVIDOR FAZ
	// =====================================================================
	/// <summary>Este corpo esta dentro da Sala do Tempo?</summary>
	private static bool NaSala(ServerPlayer pl) =>
		string.Equals(pl.Zone.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// ESTE CORPO ESTA NA SALA **COM SESSAO ABERTA**?
	///
	/// ============================ ESTAR NO z13 NAO BASTA ============================
	/// `_naSala` e a lista de quem ATRAVESSOU A PORTA (por assinatura de personagem), e ela e a
	/// unica coisa que distingue um jogador em sessao de um corpo que foi parar la de outro jeito --
	/// um NPC posto por admin, um clone da mente, uma fera solta. Sem esta pergunta, um corpo sem
	/// dono manteria "alguem rendendo" pra sempre e a comida nunca sumiria; pior, ele envelheceria
	/// um ano por dia in-game sem nunca ter pedido nada.
	/// ==============================================================================
	/// </summary>
	private bool TemSessaoNaSala(ServerPlayer pl) =>
		NaSala(pl) && pl.Assinatura.Length > 0 && _naSala.ContainsKey(pl.Assinatura);

	/// <summary>
	/// A SESSAO DESTE CORPO AINDA PAGA? E a pergunta que o funil de gravidade
	/// (<see cref="AplicarGravidade"/>) faz pra decidir se escreve os 280x ou o 1x neutro.
	///
	/// FORA DA SALA A RESPOSTA E `true` E ELA NAO SIGNIFICA NADA: quem le isto ja perguntou se a
	/// zona e a Sala (o `AplicarRitmo` faz as duas perguntas). Devolver `false` aqui fora seria
	/// dizer "a sessao dele acabou" de todo mundo que nunca entrou.
	/// </summary>
	private static bool SessaoDaSalaValendo(ServerPlayer pl) =>
		!NaSala(pl) || Jandirus.Core.World.SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao);

	// =====================================================================
	// O QUE A TELA MOSTRA (ver `Protocol.AtributosState.SalaFase`)
	// =====================================================================
	/// <summary>
	/// EM QUE FASE DA SESSAO ESTE CORPO ESTA: 0 fora, 1 rendendo, 2 janela de saida, 3 preso.
	///
	/// ============================ POR QUE ISTO PRECISA ESTAR NA TELA ============================
	/// A licao da camada anterior foi que um multiplicador que ninguem ve e indistinguivel de nao
	/// existir. Aqui e pior: a sessao tem um PRAZO com castigo no fim, e o unico sinal dele seriam
	/// tres frases no chat -- que rolam pra cima. Um jogador que voltou do banheiro no minuto 47
	/// merece poder olhar em algum lugar e ver quanto falta.
	///
	/// SEM RELOGIO NO CLIENTE: quem conta o tempo e o servidor (dias in-game por tique), e ele manda
	/// o resultado. Ver o comentario do campo no `Protocol`.
	/// =========================================================================================
	/// </summary>
	private static byte FaseDaSalaPraTela(ServerPlayer pl)
	{
		if (!NaSala(pl)) return 0;
		if (pl.SalaPreso) return 3;
		return Jandirus.Core.World.SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao) ? (byte)1 : (byte)2;
	}

	/// <summary>
	/// QUANTOS MINUTOS REAIS FALTAM pra a fase virar -- o fim da sessao, ou a tranca.
	///
	/// NA FASE DA JANELA O NUMERO E O RELOGIO DE VERDADE (`SalaJanelaAte`), e nao o teto de 2
	/// minutos: mostrar "2 min" parado enquanto o tempo corre seria uma tela que mente devagar --
	/// e esta e a unica tela do jogo em que ficar olhando o numero e a coisa certa a fazer.
	/// </summary>
	private static double MinutosDaSalaPraTela(ServerPlayer pl)
	{
		if (!NaSala(pl) || pl.SalaPreso) return 0;

		double faltam = Jandirus.Core.World.SalaDoTempo.MinutosQueFaltam(pl.SalaDiasDaSessao);
		if (faltam > 0) return faltam;

		// A JANELA AINDA NAO ARMADA (o tique acorda no quadro seguinte) mostra o teto -- e o unico
		// instante em que o numero e uma promessa, e ela dura um tique.
		if (pl.SalaJanelaAte <= 0) return Jandirus.Core.World.SalaDoTempo.MinutosDaJanela;
		return Math.Max(pl.SalaJanelaAte - NowMs(), 0) / 60_000.0;
	}

	// =====================================================================
	// O TIQUE DA SESSAO
	// =====================================================================
	/// <summary>
	/// O `htc_session()` do DM, virado tique: conta os dias, envelhece, avisa, tira os bonus, abre
	/// a janela e tranca a porta.
	///
	/// O CUSTO E O NUMERO DE PESSOAS DENTRO, e ele desiste na primeira linha quando a sala esta
	/// vazia -- que e o caso em 99,9% dos tiques da vida do servidor.
	/// </summary>
	private void TickDaSessaoDaSala(double dt)
	{
		// A COMIDA ENTRA NA CONDICAO DE PARADA, e nao so a lotacao: a ultima pessoa some de
		// `_naSala` no mesmo tique em que sai da zona (`TickDaSalaDoTempo`), e um `return` que
		// olhasse so a lotacao deixaria as porcoes postas na sala vazia pra sempre.
		if (_naSala.Count == 0 && !_comidaPosta) return;

		long agora = NowMs();
		bool alguemRendendo = false;

		foreach (ServerPlayer pl in _players.Values)
		{
			if (!NaSala(pl)) continue;

			if (!TemSessaoNaSala(pl)) continue;

			bool valia = Jandirus.Core.World.SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao);
			if (valia)
			{
				CorrerASessao(pl, dt, agora);
				valia = Jandirus.Core.World.SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao);
			}

			if (valia) { alguemRendendo = true; continue; }

			// SESSAO ACABADA: abre a janela (ou tranca, se ela ja fechou).
			JanelaOuTranca(pl, agora);
		}

		// A COMIDA SOME COM A ULTIMA SESSAO VIVA. Ela e do LUGAR e nao da pessoa: enquanto um dos
		// dois ainda estiver rendendo, a mesa continua posta -- e quem ja acabou pode comer, porque
		// o que empurra pra porta e o fim do BONUS, nao a fome.
		if (!alguemRendendo && _comidaPosta) RecolherComidaDaSala();
	}

	/// <summary>
	/// UM PASSO DA SESSAO de quem ainda rende: dias, idade, avisos e a trava de parede.
	/// </summary>
	private void CorrerASessao(ServerPlayer pl, double dt, long agora)
	{
		double antes = pl.SalaDiasDaSessao;
		pl.SalaDiasDaSessao += dt * Jandirus.Core.World.SalaDoTempo.DiasPorSegundo;

		// ============================ A TRAVA DE PAREDE ============================
		// A rede de seguranca, e nao a regra (ver o cabecalho). Ela EMPURRA a conta ate o fim em
		// vez de expulsar: aqui ninguem e expulso, entao "acabou" e a unica coisa que ela pode
		// dizer. Assim a janela de 2 minutos abre logo abaixo pelo caminho normal.
		if (pl.SalaUltimaEntrada > 0
			&& agora - pl.SalaUltimaEntrada >= Jandirus.Core.World.SalaDoTempo.TravaDeParedeEmMs
			&& Jandirus.Core.World.SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao))
		{
			pl.SalaDiasDaSessao = Jandirus.Core.World.SalaDoTempo.SessaoEmDias;
			Avisar(pl, "a distorção da Sala do Tempo chega ao limite absoluto: sua sessão acaba aqui.");
			GD.Print($"[server] sala do tempo: a trava de PAREDE fechou a sessao de {pl.Name}");
		}

		// ============================ UM ANO POR DIA IN-GAME FECHADO ============================
		// `HTC_AGE_PER_GAME_DAY = 1`. A idade do port e INTEIRA (`ServerPlayer.Idade`), entao o ano
		// entra quando o dia FECHA -- e o gatilho e a travessia do piso do numero que ja persiste,
		// e nao um segundo acumulador que pudesse discordar dele depois de um relog.
		int diasFechados = (int)Math.Floor(pl.SalaDiasDaSessao) - (int)Math.Floor(antes);
		for (int i = 0; i < diasFechados; i++) EnvelhecerNaSala(pl);

		// ============================ OS AVISOS 10/5/1 ============================
		// Os mesmos do DM (`TimeChamber.dm:110-116`). Sem eles a sessao acaba do nada, e a janela
		// de 2 minutos vira uma pegadinha pra quem estiver longe da porta.
		double faltam = Jandirus.Core.World.SalaDoTempo.MinutosQueFaltam(pl.SalaDiasDaSessao);
		int ultimo = _avisoDaSala.GetValueOrDefault(pl.Id, 0);
		foreach (int w in AvisosEmMinutos)
		{
			if (faltam > w || ultimo == w || (ultimo != 0 && ultimo < w)) continue;
			_avisoDaSala[pl.Id] = w;
			Avisar(pl, $"Sala do Tempo: resta{(w == 1 ? "" : "m")} ~{w} minuto{(w == 1 ? "" : "s")} de treino.");
			break;
		}

		if (Jandirus.Core.World.SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao)) return;

		// ============================ O FIM DOS BONUS ============================
		// `AplicarGravidade` e o funil que escreve os dois ritmos, e ele ja pergunta pela sessao --
		// chamar o funil (em vez de escrever `zoneGainMult = 1` aqui) e o que garante que o fim da
		// sessao e a saida pela porta apagam o bonus pelo MESMO caminho.
		AplicarGravidade(pl);
		MandarAtributos(pl);
		Avisar(pl, "seus dois dias na Sala do Tempo se completam. Os ganhos voltam ao normal e a "
				   + "comida some.");
		GD.Print($"[server] sala do tempo: a sessao de {pl.Name} acabou ({pl.SalaDiasDaSessao:0.##} dias)");
	}

	/// <summary>
	/// A JANELA DE 2 MINUTOS, E A TRANCA. Regra do dono 13.6c.
	///
	/// A JANELA E ARMADA AQUI, na primeira vez que o tique ve a sessao acabada num corpo em jogo --
	/// ver o bloco "A PRISAO EXIGE QUE A PORTA TENHA SIDO OFERECIDA" no cabecalho.
	/// </summary>
	private void JanelaOuTranca(ServerPlayer pl, long agora)
	{
		if (pl.SalaPreso) return;

		if (pl.SalaJanelaAte <= 0)
		{
			pl.SalaJanelaAte = agora + (long)(Jandirus.Core.World.SalaDoTempo.MinutosDaJanela * 60_000);

			// A ficha lenta carrega o estado da sessao pra tela (ver `MandarAtributos`): sem esta
			// linha o painel continuaria dizendo "sessao valendo" ate o proximo pacote por outro
			// motivo, e a janela -- que dura dois minutos -- poderia acabar antes disso.
			MandarAtributos(pl);
			Avisar(pl, $"VOCÊ TEM {Jandirus.Core.World.SalaDoTempo.MinutosDaJanela:0} MINUTOS PRA SAIR "
					   + "PELA PORTA. Depois disso ela deixa de aceitar você.");
			return;
		}

		if (agora < pl.SalaJanelaAte) return;

		pl.SalaPreso = true;
		pl.SalaJanelaAte = 0;
		MandarAtributos(pl);

		string dono = DonoDoCargo("guardian");
		Avisar(pl, "a porta se fecha e não responde mais a você. Você está PRESO na Sala do Tempo.");
		Avisar(pl, dono.Length > 0
			? $"só o Guardião da Terra ({dono}) -- ou um administrador -- pode soltar você."
			: "não há Guardião da Terra no momento: só um administrador pode soltar você.");
		GD.Print($"[server] sala do tempo: {pl.Name} FICOU PRESO (a janela de "
				 + $"{Jandirus.Core.World.SalaDoTempo.MinutosDaJanela:0} min fechou)");
	}

	/// <summary>
	/// UM ANO A MAIS, e ele precisa chegar em TRES lugares.
	///
	/// ============================ TRES, E NAO UM ============================
	/// `ServerPlayer.Idade` e o que vai pro disco; `Fighter.Idade` e o que a conta de poder le (o
	/// `AgeDiv` do `netBuff` sai da curva de `Envelhecimento`); e a assinatura da ficha lenta e o
	/// que faz o numero novo chegar na tela. Escrever so o primeiro deixaria o personagem mais
	/// velho no save e igualzinho em jogo ate o proximo login -- que e o tipo de meio-estado que
	/// so aparece semanas depois.
	/// ======================================================================
	/// </summary>
	private void EnvelhecerNaSala(ServerPlayer pl)
	{
		pl.Idade++;
		pl.Ficha.Idade = pl.Idade;
		pl.Ficha.Statify();
		pl.SigAtributos = "";

		// O ANO VIROU PRA ELE -- e no DM e exatamente isso que chama o `AgeCheck` (`WorldClock.dm:92`,
		// o `proc/Years()`). A Sala do Tempo e o unico calendario que este port tem, entao ela e o
		// lugar certo pra pergunta "o corpo aguentou mais um ano?". Ver `ConferirMorteDeVelhice`.
		ConferirMorteDeVelhice(pl);
	}

	// =====================================================================
	// A COMIDA (13.6b)
	// =====================================================================
	/// <summary>
	/// A MESA ESTA POSTA? So isto e guardado -- **as porcoes em si sao contadas no `_noChao`**.
	///
	/// ============================ POR QUE NAO HA LISTA DE IDS AQUI ============================
	/// A primeira versao guardava uma `List&lt;int&gt;` com os ids das porcoes, e ela tinha o defeito que
	/// o proprio `Obra.Gravidade` documenta: **uma lista paralela envelhece quando a obra morre por
	/// fora**. Porcao e obra, e obra cai no soco (`GameServer.Estrago.cs` a tira do `_noChao` sem
	/// perguntar a ninguem) -- o id destruido continuaria na lista, o teto de duas ficaria cheio
	/// pra sempre e a comida **nunca mais nasceria**, sem erro e sem log.
	///
	/// Contar do `_noChao` nao pode divergir: e a mesma lista que desenha, que colide e que o
	/// `ObraQueAceita` procura. Este `bool` fica so pra o tique poder desistir na primeira linha
	/// quando a sala esta vazia -- ele diz "ha o que recolher", nunca "quantas ha".
	/// =======================================================================================
	/// </summary>
	private bool _comidaPosta;

	/// <summary>As porcoes de pe AGORA, lidas de onde elas moram de verdade.</summary>
	private List<Obra> PorcoesNaSala() =>
		[.. _noChao.Where(o => o.Tipo == TipoDaComida && o.Zona.Equals(ZoneKey.Premade(ZonaDaSala)))];

	/// <summary>
	/// ENCHE A MESA ate o teto. Chamada em DOIS lugares, e os dois sao gestos e nao relogios: a
	/// entrada de alguem (a sessao comecou) e o ato de comer (reposicao por consumo).
	/// </summary>
	private void AbastecerComidaDaSala()
	{
		if (_obras?.Get(TipoDaComida) == null)
		{
			// FALHAR ALTO (PARTE 0, "silencio no lugar de erro"): sem a ficha no catalogo a porcao
			// nasceria invisivel -- o cliente desenha obra pela arte do catalogo, e uma obra sem
			// entrada e recusada na carga do mapa sem uma palavra.
			GD.PushWarning($"[server] sala do tempo: '{TipoDaComida}' nao esta no catalogo de "
						   + "construcoes -- a comida da sala nao vai nascer (rode o AssetPipeline 'tech')");
			return;
		}

		bool mudou = false;
		int postas = PorcoesNaSala().Count;
		const int t = ZoneCollision.TileSize;

		foreach ((int cx, int cy) in CelulasDaComida)
		{
			if (postas >= PorcoesDaSala) break;

			float x = cx * t + t / 2f;
			float y = cy * t + t / 2f - Jandirus.Core.World.MoveRules.FeetOffsetY;

			// UMA POR CELULA: sem isto, comer duas vezes empilharia duas porcoes no mesmo pixel e
			// as duas responderiam ao mesmo botao.
			if (_noChao.Any(o => o.Zona.Equals(ZoneKey.Premade(ZonaDaSala))
								 && Math.Abs(o.X - x) < 8 && Math.Abs(o.Y - y) < 8))
				continue;

			var porcao = new Obra
			{
				Id = _proximaObraId++,
				Tipo = TipoDaComida,
				X = x,
				Y = y,
				DonoNome = "",
				Aparafusada = true,

				// `DoMapa` FAZ DUAS COISAS de uma vez, e as duas sao o que se quer: a porcao NAO vai
				// pro `mundo.json` (ela e da sessao, nao do mundo -- ver `Obra.DoMapa`), e ninguem
				// consegue guarda-la na mochila pra levar embora ("isto faz parte do lugar").
				DoMapa = true,
				ErguidaEm = NowMs(),
			};

			// A SALA E UM MAPA FEITO A MAO -- zona pre-feita, como toda zona de arquivo.
			porcao.PorZona(ZoneKey.Premade(ZonaDaSala));
			_noChao.Add(porcao);
			postas++;
			_comidaPosta = true;
			mudou = true;
		}

		if (mudou) MandarObras(ZoneKey.Premade(ZonaDaSala));
	}

	/// <summary>
	/// COMER UMA PORCAO -- e a reposicao acontece AQUI, no gesto, e nao num tique.
	/// </summary>
	private void ComerNaSala(ServerPlayer pl)
	{
		Obra? porcao = ObraQueAceita(pl, "sala_comer");
		if (porcao == null) { Avisar(pl, "não há comida por perto."); return; }

		// A OBRA SAI ANTES DA NUTRICAO ENTRAR: se a ordem fosse a inversa e algo abaixo recusasse,
		// o jogador teria comido uma porcao que continua no chao.
		// CHEIO NAO COME -- e a porcao continua na mesa.
		Jandirus.Core.Stats.Nutricao.Refeicao r = Jandirus.Core.Stats.Nutricao.Comer(pl.Ficha, NutricaoDaPorcao);
		if (!r.Comeu) { Avisar(pl, r.Aviso); return; }

		_noChao.Remove(porcao);

		Avisar(pl, "você come uma refeição da Sala do Tempo.");
		if (r.Aviso.Length > 0) Avisar(pl, r.Aviso);

		// A REPOSICAO POR CONSUMO. Ela so acontece se ainda ha sessao rendendo -- comer a ultima
		// porcao depois do fim nao faz nascer outra, e e isso que "aos 48 min a comida some" quer
		// dizer na pratica.
		if (AlguemRendendoNaSala()) AbastecerComidaDaSala();
		else MandarObras(ZoneKey.Premade(ZonaDaSala));
	}

	/// <summary>Tira as porcoes do chao -- o fim da sessao (13.6b: "aos 48 min a comida some").</summary>
	private void RecolherComidaDaSala()
	{
		_noChao.RemoveAll(o => o.Tipo == TipoDaComida && o.Zona.Equals(ZoneKey.Premade(ZonaDaSala)));
		_comidaPosta = false;
		MandarObras(ZoneKey.Premade(ZonaDaSala));
	}

	/// <summary>Ha alguem DENTRO, com sessao aberta, e ainda rendendo?</summary>
	private bool AlguemRendendoNaSala()
	{
		foreach (ServerPlayer p in _players.Values)
			if (TemSessaoNaSala(p) && Jandirus.Core.World.SalaDoTempo.SessaoValendo(p.SalaDiasDaSessao))
				return true;
		return false;
	}

	// =====================================================================
	// A PORTA, VISTA DE DENTRO
	// =====================================================================
	/// <summary>
	/// A SAIDA RECUSA QUEM ESTA PRESO. Chamada do tique das passagens, que e por onde se sai daqui
	/// (as celulas `fromhbtc`, 144..147 x 338 do z13).
	///
	/// ============================ POR QUE A GUARDA MORA NA PASSAGEM ============================
	/// A entrada e uma PORTA (objeto que responde) e a saida e uma PASSAGEM (chao que leva) -- foi
	/// assim que o DM as desenhou, e o port copiou os dois gestos. A tranca, entao, precisa existir
	/// nos dois lugares: `EntrarNaSala` ja recusava quem esta preso do lado de fora (pra ele nao
	/// entrar de novo depois de solto por engano), e esta e a metade que importa -- a de dentro.
	///
	/// SEM ISTO A PRISAO SERIA DECORATIVA: o bit estaria escrito, o Guardiao teria um verbo pra
	/// tirar, e o jogador sairia andando por cima da passagem como se nada fosse. E exatamente a
	/// armadilha 1 da PARTE 3 do plano ("regra escrita nao e regra ligada").
	/// =========================================================================================
	/// </summary>
	/// <returns>`true` quando a passagem foi RECUSADA.</returns>
	private bool APrisaoRecusaASaida(ServerPlayer pl, Passagem p)
	{
		if (!pl.SalaPreso || !NaSala(pl)) return false;
		if (string.Equals(p.Zona, ZonaDaSala, StringComparison.OrdinalIgnoreCase)) return false;

		// UMA VEZ A CADA POUCOS SEGUNDOS: o tique roda a 30 Hz e o corpo fica EM CIMA da celula --
		// sem a carencia, a recusa viraria trinta linhas de chat por segundo.
		long agora = NowMs();
		if (_avisoDePrisao.TryGetValue(pl.Id, out long mudo) && agora < mudo) return true;
		_avisoDePrisao[pl.Id] = agora + 5_000;

		string dono = DonoDoCargo("guardian");
		Avisar(pl, "a porta não se abre: você passou do limite e está preso na Sala do Tempo. "
				   + (dono.Length > 0
						? $"Só o Guardião da Terra ({dono}) -- ou um administrador -- solta."
						: "Não há Guardião da Terra no momento: só um administrador solta."));
		return true;
	}

	/// <summary>Quando a recusa da saida pode falar de novo, por corpo. Vivo, e so anti-spam.</summary>
	private readonly Dictionary<int, long> _avisoDePrisao = [];

	/// <summary>
	/// O QUE A SESSAO ESQUECE QUANDO O CORPO SAI DA SALA (ou do mundo).
	///
	/// SO O QUE E RUIDO DE SESSAO: o aviso de tempo e o silencio da recusa -- os dois sao
	/// dicionarios por id, e id se reusa (a mesma armadilha do resto do `Drop`). Os dias gastos e a
	/// prisao NAO se apagam aqui: sair da zona nao e o mesmo que acabar a sessao, e quem sai no
	/// minuto 30 e volta amanha ja gastou meia sessao (o proximo `sala_entrar` e que zera a conta).
	///
	/// A JANELA nao aparece aqui porque ela mora no proprio `ServerPlayer`, que morre com o login
	/// -- e e exatamente esse o comportamento pedido: quem cai recebe a janela inteira de novo.
	/// </summary>
	private void EsquecerSessaoDaSala(int id)
	{
		_avisoDaSala.Remove(id);
		_avisoDePrisao.Remove(id);
	}
}
