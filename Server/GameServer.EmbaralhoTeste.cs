using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DO "SEM PLATEIA, SEM MENTE" (`--embaralhoteste`) ============================
/// Mede as duas metades do pedido do dono, e elas sao opostas de proposito: a mente **para** quando
/// nao ha ninguem no planeta, e o mundo **nao para**.
///
///   Godot --headless --path . --host --rede 7972 --embaralhoteste --conta bancada_embaralho --nome Embaralho
///
/// ============================ O QUE ESTA BANCADA APRENDEU COM AS OUTRAS ============================
/// Tres cegos que este port ja pagou, e que esta bancada tenta nao repetir:
///
///   1. **"O NUMERO NAO MUDOU" NAO E MEDIDA.** Toda afirmacao de congelamento vem com o CONTROLE ao
///      lado -- o mesmo corpo, o mesmo numero de tiques, com um jogador na zona. Sem o par, "o NPC
///      nao andou" fica verde num codigo que nunca anda.
///   2. **RELOGIO DE PAREDE NAO GIRA NUM LACO.** O prazo do embaralho e `NowMs()`, e 3600 tiques de
///      bancada correm em muito menos de 5 minutos: esperar seria medir o nada. A bancada EMPURRA a
///      marca pra tras (ela e a unica dona de `_vazioDesde` aqui) e diz que empurrou.
///   3. **"NAO CAIU NA PAREDE" TEM QUE PODER FALHAR.** Medir isso no berco da TERRA nao prova nada:
///      o (249,250) foi escrito PRA ela, e e campo aberto. A familia 4 refaz a medida em **ICER**,
///      onde 309 das 441 celulas em volta do mesmo ponto sao rocha -- e conta as celulas densas do
///      raio ANTES, pra a afirmacao ter como ficar vermelha. Um teto que nunca dispara e
///      indistinguivel de teto nenhum.
/// ==============================================================================================
/// </summary>
public partial class GameServer
{
	private bool _embaralhoDeTeste;

	/// <summary>Faixa de lugares propria -- longe da dos habitantes, das sagas e da `--genteteste`.</summary>
	private ulong _lugarDaBancadaDeEmbaralho = 8_300_000;

	private void RodarBancadaDoEmbaralho(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA: SEM PLATEIA, SEM MENTE (congelamento + embaralho) =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		var forjados = new List<ServerPlayer>();

		try
		{
			// A zona da medida e a do jogador, e a de estacionamento e OUTRA: o congelamento so
			// existe quando nao ha ninguem, entao a bancada precisa de um lugar pra por o host.
			var palco = ZoneKey.Premade("Earth");

			// ============================ ONDE O HOST ESTACIONA -- E ISSO E UMA MEDIDA ============================
			// Namek seria o obvio, e seria ERRADO: ela tem 32 habitantes no plano, que ficariam
			// ACORDADOS junto do host. A familia 2 mede "quanto custa um servidor com todo mundo
			// congelado" -- com o host num planeta povoado ela mediria uma mistura, e o contraste entre
			// dormir e agir sairia menor do que e por um motivo que nao e o codigo.
			//
			// O Lookout nao esta no plano de povoamento (`npcs.json`: Earth, Vegeta, Namek, Icer,
			// Arlia, Makyo_Star), entao o host la deixa o mundo inteiro sem plateia.
			// ================================================================================================
			var longe = ZoneKey.Premade("Lookout");

			ZoneCollision? mapa = MapaDaZonaOuCatalogo(palco);
			if (mapa == null || _moldes == null)
			{
				Checa("PRECONDICAO: a Earth tem mapa de colisao e os moldes carregaram", false);
				return;
			}

			// ---------------------------------------------------------------
			// FAMILIA 1 -- A MENTE PARA SEM PLATEIA, E SO SEM PLATEIA
			// ---------------------------------------------------------------
			Vec2 berco = PontoDeHabitante(palco, ++_lugarDaBancadaDeEmbaralho);
			var vila = new List<ServerPlayer>();
			for (int i = 0; i < 8; i++)
			{
				ServerPlayer? c = NascerNpc("cidadao", palco,
					mapa.PontoLivrePerto(berco + new Vec2(i * 64, 0)), ++_lugarDaBancadaDeEmbaralho);
				if (c != null) { vila.Add(c); forjados.Add(c); }
			}
			Checa("PRECONDICAO: nasceram 8 habitantes na Earth", vila.Count == 8, $"{vila.Count}");
			if (vila.Count != 8) return;

			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));

			// O RANCOR TEM QUE ESTAR FRIO: um cidadao com agressor recente tem presa, e presa e a
			// unica coisa que atravessa o congelamento. Sem isto a medida diria "nao congelou" por um
			// motivo que nao e o defeito.
			foreach (ServerPlayer c in vila) { c.UltimoAgressor = 0; c.RancorAte = 0; }

			var antes = vila.Select(c => c.Pos).ToList();
			for (int t = 0; t < 900; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double andouVazio = vila.Select((c, i) => (c.Pos - antes[i]).Length).Max();

			Checa("planeta SEM ninguem: 900 tiques (30 s) e nenhum habitante deu um pixel",
				  andouVazio < 0.001, $"o que mais andou: {andouVazio:0.00} px");
			Checa("...e a zona nao conta como habitada (o `_zonasComGente` e quem decide)",
				  !_zonasComGente.Contains(palco.Hash));

			// ---- O CONTROLE: o mesmo laco, com o host na zona ----
			MoveToZone(pl.Id, palco, berco + new Vec2(4 * ZoneCollision.TileSize, 0));
			antes = vila.Select(c => c.Pos).ToList();
			for (int t = 0; t < 900; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double andouComGente = vila.Select((c, i) => (c.Pos - antes[i]).Length).Max();

			Checa("CONTROLE: com o HOST na zona, os mesmos 8 andam nos mesmos 900 tiques",
				  andouComGente > 0.001, "a linha de cima e decoracao -- ela nao sabe ficar vermelha");
			Checa("...e o host conta como plateia igual a um cliente (o dono pediu os dois)",
				  _zonasComGente.Contains(palco.Hash));

			// ---- MORRER NAO E SAIR DO PLANETA (o defeito de 15 s) ----
			// O corpo fica na zona ate a mesa do Enma, e a tela do dono continua mostrando a cidade.
			bool mortoGuardado = pl.Ficha.dead;
			pl.Ficha.dead = true;
			TickDosCorposSemDono(Protocol.TickSeconds);
			bool plateiaMorta = _zonasComGente.Contains(palco.Hash);
			pl.Ficha.dead = mortoGuardado;
			TickDosCorposSemDono(Protocol.TickSeconds);

			Checa("um jogador MORTO na zona ainda e plateia (senao a cidade vira estatua na frente dele)",
				  plateiaMorta);

			// ---------------------------------------------------------------
			// FAMILIA 7 -- A CONTA DAS DECISOES, E NAO O RELOGIO
			// ---------------------------------------------------------------
			// ============================ POR QUE CONTAR, SE A FAMILIA 1 JA MEDIU ============================
			// A familia 1 mede DESLOCAMENTO ("ninguem deu um pixel") e a 2 mede TEMPO ("dormir e mais
			// barato"). As duas ficam VERDES numa mente que pensa **de vez em quando** -- que e
			// exatamente a outra opcao que o dono deixou em aberto (*"seria DESLIGADA ou teria seus
			// calculos chamados A CADA UM GRANDE INTERVALO"*):
			//
			//   * um corpo que decide uma vez a cada 900 tiques anda uma fracao de pixel, e o
			//     `andouVazio < 0.001` da familia 1 nao sabe distinguir isso de zero;
			//   * e 1/900 do custo some dentro do ruido do relogio da familia 2.
			//
			// Esta familia nao pergunta "andou?" nem "custou?": pergunta **quantas vezes a mente
			// rodou**. A resposta e um inteiro, e inteiro nao arredonda. Ver `_decisoesDaMente`.
			// ==========================================================================================
			long DecisoesEm(int tiques)
			{
				long a = _decisoesDaMente;
				for (int t = 0; t < tiques; t++) TickDosCorposSemDono(Protocol.TickSeconds);
				return _decisoesDaMente - a;
			}

			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			const int voltasDaConta = 600;
			long semPlateia = DecisoesEm(voltasDaConta);
			Checa($"zona sem jogador: {voltasDaConta} tiques e **ZERO** decisoes de mente no servidor "
				+ "inteiro (nao 'poucas' -- zero)",
				  semPlateia == 0, $"{semPlateia} decisoes");

			// ---- O CONTRA-EXEMPLO, E ELE E A LINHA MAIS IMPORTANTE DO PAR ----
			// Sem ele, "a mente nao roda" ficaria verde numa IA MORTA -- e uma IA morta e o pior
			// resultado possivel desta tarefa, porque ela parece otimizacao e nao acusa nada.
			MoveToZone(pl.Id, palco, berco + new Vec2(4 * ZoneCollision.TileSize, 0));
			TickDosCorposSemDono(Protocol.TickSeconds);

			int dirigidosNoPalco = ZoneList(palco.Hash).Count(c => c.Cerebro != null && EhNpcDoMundo(c));
			long comPlateia = DecisoesEm(voltasDaConta);
			long esperado = (long)dirigidosNoPalco * voltasDaConta;

			Checa("CONTRA-EXEMPLO: com o HOST na zona a mente roda em TEMPO REAL -- uma decisao por "
				+ "corpo por tique, exatamente, e nao uma amostragem",
				  comPlateia == esperado,
				  $"{comPlateia} decisoes; {dirigidosNoPalco} corpos x {voltasDaConta} tiques = {esperado}");

			Checa("...e o HOST SOZINHO ja e plateia -- ele e o unico jogador do servidor nesta medida",
				  Jogadores.Count() == 1 && comPlateia > 0, $"{Jogadores.Count()} jogador(es)");

			// ---- O DEFEITO INJETADO: E SE NPC CONTASSE COMO GENTE? ----
			// A tentacao obvia ao escrever `_zonasComGente` e "ha corpo nesta zona?". Com ela os oito
			// cidadaos manteriam UNS AOS OUTROS acordados pra sempre, e o anti-lag nao existiria --
			// num servidor sem ninguem online, que e o caso que o dono abriu a tarefa pra pagar menos.
			// A injecao e o proprio mundo: a zona abaixo tem OITO corpos e nenhum jogador.
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);
			long soComNpcs = DecisoesEm(120);
			Checa("PRECONDICAO (o defeito injetado): a zona tem 8 corpos e ZERO jogadores -- se NPC "
				+ "contasse como plateia, eles se acordariam em circulo pra sempre",
				  dirigidosNoPalco >= 8 && soComNpcs == 0,
				  $"{dirigidosNoPalco} corpos, {soComNpcs} decisoes");

			// ---------------------------------------------------------------
			// FAMILIA 8 -- QUEM PARECE PLATEIA E NAO E
			// ---------------------------------------------------------------
			// ============================ O CORPO NAO E A PESSOA ============================
			// Dois corpos deste port passam perto de "ha alguem aqui" e nao sao ninguem:
			//
			//   * o BONECO DO CORPO LARGADO -- ele carrega o nome, a ficha e a aparencia do dono DE
			//     PROPOSITO (quem passa do lado ve voce meditando). Se ele acordasse a zona, meditar
			//     numa cidade deserta manteria a cidade inteira pensando com o dono de olhos fechados
			//     em outra dimensao;
			//   * o REFLEXO DA MENTE -- ele nasce com o `expressedBP` do dono e briga como ele.
			//
			// Os dois sao cortados pelo `Gente.EhJogador`, e nao por um `if` escrito aqui. As linhas
			// abaixo forjam os dois DENTRO do `_players` (que e onde o `_zonasComGente` olha) porque
			// e la que o defeito moraria -- perguntar direto ao `Core` provaria a regra e nao a fiacao.
			// ==============================================================================
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			// O BONECO: `Peer` de verdade (o do host -- e o que faz dele o caso dificil) e
			// `DonoDoCorpoLargado` apontando pro dono. FICHA PROPRIA e nao a do host de proposito: o
			// boneco de verdade compartilha a do dono, e uma bancada que mexesse na ficha do jogador
			// pra provar uma regra de plateia estragaria as medidas de todas as outras familias.
			var boneco = new ServerPlayer
			{
				Id = _nextId++,
				Peer = pl.Peer,                 // ...ha um canal aberto...
				DonoDoCorpoLargado = pl.Id,     // ...e mesmo assim nao ha ninguem olhando por estes olhos
				Cerebro = null,
				Name = pl.Name,
				Zone = palco,
				Pos = berco,
				Race = pl.Race,
				Ficha = new Jandirus.Core.Stats.Fighter { Name = pl.Name, Race = pl.Race, BP = 1 },

				// O LIVRO E OS NIVEIS SAO OS DO DONO -- exatamente como no boneco de verdade
				// (`GameServer.CorpoLargado.cs`), e por seguranca e nao por regra: um corpo com `Peer`
				// dentro do `_players` passa pelo `MandarSkills`, que le `pl.Livro` sem `?.`. Sem esta
				// linha a bancada morria com um `NullReferenceException` no meio da familia 9 -- e a
				// licao e do proprio boneco: quem entra na lista paga o que a lista cobra.
				Livro = pl.Livro,
				Niveis = pl.Niveis,
				LastInputMs = NowMs(),
			};
			PorNoMundo(boneco);
			forjados.Add(boneco);

			long comBoneco = DecisoesEm(120);
			Checa("o BONECO do corpo largado nao e plateia: com ele (e so ele) na zona, a mente "
				+ "continua desligada",
				  comBoneco == 0 && !_zonasComGente.Contains(palco.Hash), $"{comBoneco} decisoes");

			// ---- O DEFEITO INJETADO: apaga o marcador que o distingue ----
			// UM campo, no MESMO corpo, no MESMO lugar. Se a linha acima estivesse verde por o corpo
			// nao estar na zona (ou por a bancada estar contando errado), esta aqui continuaria verde
			// junto -- e e por isso que ela existe.
			boneco.DonoDoCorpoLargado = 0;
			long bonecoVirouGente = DecisoesEm(120);
			Checa("PRECONDICAO (o defeito injetado): zerado o `DonoDoCorpoLargado`, o MESMO corpo no "
				+ "MESMO lugar passa a acordar a zona",
				  bonecoVirouGente == (long)dirigidosNoPalco * 120,
				  $"{bonecoVirouGente} decisoes");
			boneco.DonoDoCorpoLargado = pl.Id;

			// O REFLEXO: sem `Peer`, com `DonoDoClone`. `Cerebro` nulo de proposito -- o reflexo de
			// verdade e removido no primeiro tique em que o dono nao esta na mesma zona, e o que se
			// mede aqui e se ele ACORDA A ZONA, nao o que ele faz.
			var reflexo = new ServerPlayer
			{
				Id = _nextId++,
				Peer = null,
				DonoDoClone = pl.Id,
				Cerebro = null,
				Name = pl.Name + " (reflexo)",
				Zone = palco,
				Pos = berco,
				Race = pl.Race,
				Ficha = new Jandirus.Core.Stats.Fighter { Name = pl.Name, Race = pl.Race, BP = 1 },
				Livro = new Jandirus.Core.Skills.SkillBook(),   // o reflexo de verdade nasce com o dele
				LastInputMs = NowMs(),
			};
			PorNoMundo(reflexo);
			forjados.Add(reflexo);

			long comReflexo = DecisoesEm(120);
			Checa("o REFLEXO da mente nao e plateia: ele tem a ficha do dono e nao tem dono na tela",
				  comReflexo == 0 && !_zonasComGente.Contains(palco.Hash), $"{comReflexo} decisoes");

			// ---- O DEFEITO INJETADO NO REFLEXO, E ELE ENSINA UMA COISA ----
			// Quem corta o reflexo e o `Peer == null`, e SO ele: `Gente.EhJogador` e
			// `temDono && papel == null && donoDoCorpoLargado == 0`, e o `DonoDoClone` **nao entra na
			// conjuncao**. Dar um `Peer` ao reflexo o faz contar como jogador -- e a linha abaixo
			// existe pra isso estar MEDIDO e nao suposto. Hoje e inalcancavel (nenhum caminho da
			// `Peer` a um clone), e no dia em que alguem der, e esta linha que muda de cor.
			reflexo.Peer = pl.Peer;
			long reflexoComPeer = DecisoesEm(120);
			reflexo.Peer = null;
			Checa("PRECONDICAO (o defeito injetado): um reflexo com `Peer` acordaria a zona -- quem o "
				+ "corta hoje e o `Peer == null`, e o `DonoDoClone` nao entra na conta do `EhJogador`",
				  reflexoComPeer == (long)dirigidosNoPalco * 120, $"{reflexoComPeer} decisoes");

			// OS DOIS SAEM DO MUNDO AGORA, e nao no `finally`. Um corpo forjado que sobrevive a sua
			// familia vira ruido em todas as seguintes: o boneco tem `Peer`, entao ele passa pelos
			// laços que so existem pra quem tem tela (o `MandarSkills`, o `MandarCorpo`) e entra nas
			// medidas de custo das familias 5 e 13 como se fosse gente.
			boneco.Peer = null;
			RemoverNpc(boneco);
			RemoverNpc(reflexo);

			// ---------------------------------------------------------------
			// FAMILIA 2 -- O CUSTO: QUANTO CUSTA UM CORPO CONGELADO
			// ---------------------------------------------------------------
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			ulong t0 = Time.GetTicksUsec();
			for (int t = 0; t < 3000; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double usDormindo = (Time.GetTicksUsec() - t0) / 3000.0;

			MoveToZone(pl.Id, palco, berco + new Vec2(4 * ZoneCollision.TileSize, 0));
			TickDosCorposSemDono(Protocol.TickSeconds);
			t0 = Time.GetTicksUsec();
			for (int t = 0; t < 3000; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double usAcordado = (Time.GetTicksUsec() - t0) / 3000.0;

			GD.Print($"  MEDIDA  `TickDosCorposSemDono` com {CorposSemDonoNoServidor()} corpos: "
				   + $"planeta vazio {usDormindo:0.00} us/tique, com plateia {usAcordado:0.00} us/tique "
				   + $"({usDormindo / Protocol.TickSeconds / 10000:0.000}% x "
				   + $"{usAcordado / Protocol.TickSeconds / 10000:0.000}% do orcamento)");
			Checa("dormir e mais barato do que agir (senao o congelamento nao paga o proprio codigo)",
				  usDormindo < usAcordado, $"{usDormindo:0.00} x {usAcordado:0.00} us");

			// ---------------------------------------------------------------
			// FAMILIA 3 -- O EMBARALHO
			// ---------------------------------------------------------------
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			Checa("PRECONDICAO: a Earth ficou marcada como vazia no tique em que o host saiu",
				  _vazioDesde.ContainsKey(palco.Hash));

			// ---- CHEGAR CEDO NAO EMBARALHA ----
			antes = vila.Select(c => c.Pos).ToList();
			MoveToZone(pl.Id, palco, berco);
			double andouCedo = vila.Select((c, i) => (c.Pos - antes[i]).Length).Max();
			Checa("voltar ANTES dos 5 minutos nao mexe ninguem de lugar",
				  andouCedo < 0.001, $"{andouCedo:0.00} px");

			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			// ---- E AGORA O RELOGIO E EMPURRADO PRA TRAS ----
			// Ver o cego 2 do cabecalho: `NowMs()` nao anda dentro de um laco de bancada.
			GD.Print($"  (a bancada empurra `_vazioDesde` da Earth {Povoamento.SegundosAteOEmbaralho + 60:0} s "
				   + "pra tras -- o prazo e de PAREDE e nao gira num laco)");
			_vazioDesde[palco.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);

			antes = vila.Select(c => c.Pos).ToList();
			MoveToZone(pl.Id, palco, berco);

			int mudaram = 0, naParede = 0, longeDemais = 0;
			const int t2 = ZoneCollision.TileSize;
			double tetoEsperado = (Povoamento.TilesMaxDoEmbaralho + 8) * t2 * 1.5;
			for (int i = 0; i < vila.Count; i++)
			{
				double d = (vila[i].Pos - antes[i]).Length;
				if (d > 0.001) mudaram++;
				if (d > tetoEsperado) longeDemais++;
				if (mapa.BlockedAt(vila[i].Pos)) naParede++;
			}

			Checa("passados os 5 minutos, TODOS os habitantes mudaram de lugar na chegada do jogador",
				  mudaram == vila.Count, $"{mudaram}/{vila.Count}");
			Checa("...e NENHUM foi parar dentro de parede (o `PontoLivrePerto`, que o dono pediu em maiusculas)",
				  naParede == 0, $"{naParede} na pedra");
			Checa("...e nenhum atravessou o mapa: o passeio e PERTO de onde ele estava",
				  longeDemais == 0, $"{longeDemais} alem de {tetoEsperado:0} px");

			// QUANTO A LINHA ACIMA VALE NESTE MAPA. O berco da Terra e campo aberto de proposito
			// (o (249,250) do BYOND foi escrito PRA ela), entao "ninguem caiu na pedra" aqui pode ser
			// verdade por nao haver pedra. **Quem prova o funil e a familia 4, em Icer** -- este numero
			// so evita que a leitura desta secao seja mais confiante do que a medida.
			int cairiaNaPedra = 0;
			foreach (ServerPlayer c in vila)
				for (int dx = -Povoamento.TilesMaxDoEmbaralho; dx <= Povoamento.TilesMaxDoEmbaralho; dx++)
					for (int dy = -Povoamento.TilesMaxDoEmbaralho; dy <= Povoamento.TilesMaxDoEmbaralho; dy++)
						if (mapa.BlockedAt(c.Pos + new Vec2(dx * t2, dy * t2))) cairiaNaPedra++;
			GD.Print($"  (contexto: o raio do sorteio em volta dos 8 corpos da Terra tem {cairiaNaPedra} "
				   + "celulas densas -- a prova do funil e a familia 4, em Icer)");

			// ---- UMA VEZ SO ----
			antes = vila.Select(c => c.Pos).ToList();
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			MoveToZone(pl.Id, palco, berco);
			double andouDeNovo = vila.Select((c, i) => (c.Pos - antes[i]).Length).Max();
			Checa("sair e voltar na hora nao embaralha de novo (`SO BASTA ACONTECER 1 VEZ`)",
				  andouDeNovo < 0.001, $"{andouDeNovo:0.00} px");

			// ---- O CORPO DE UM JOGADOR NAO E EMBARALHADO ----
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);
			_vazioDesde[palco.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);

			Vec2 ondeOHostChega = berco;
			MoveToZone(pl.Id, palco, ondeOHostChega);
			Checa("o corpo de quem CHEGA nao e embaralhado junto (o crivo e o `EhNpcDoMundo`)",
				  (pl.Pos - ondeOHostChega).Length < 0.001, $"{(pl.Pos - ondeOHostChega).Length:0.00} px");

			// ---------------------------------------------------------------
			// FAMILIA 4 -- A PAREDE, NO MAPA QUE TEM PAREDE
			// ---------------------------------------------------------------
			// ============================ POR QUE ICER E NAO A TERRA ============================
			// "Ninguem caiu na pedra" e uma frase que fica verde num campo aberto sem provar nada. O
			// ponto de chegada pre-feito (249,250) e o campo do meio da TERRA, e em **Icer ele e
			// PAREDE**: 309 das 441 celulas do quadrado 21x21 em volta sao densas (medido no proprio
			// `z04_Icer.col` quando o berco foi escrito). E o mapa onde o funil TEM que trabalhar.
			// ==============================================================================
			var pedra = ZoneKey.Premade("Icer");
			ZoneCollision? mapaPedra = MapaDaZonaOuCatalogo(pedra);
			if (mapaPedra == null) Checa("PRECONDICAO: Icer tem mapa de colisao", false);
			else
			{
				var presos = new List<ServerPlayer>();
				for (int i = 0; i < 8; i++)
				{
					ServerPlayer? c = NascerNpc("cidadao", pedra,
						PontoDeHabitante(pedra, ++_lugarDaBancadaDeEmbaralho),
						++_lugarDaBancadaDeEmbaralho);
					if (c != null) { presos.Add(c); forjados.Add(c); }
				}

				int densasEmVolta = 0;
				foreach (ServerPlayer c in presos)
					for (int dx = -Povoamento.TilesMaxDoEmbaralho; dx <= Povoamento.TilesMaxDoEmbaralho; dx++)
						for (int dy = -Povoamento.TilesMaxDoEmbaralho; dy <= Povoamento.TilesMaxDoEmbaralho; dy++)
							if (mapaPedra.BlockedAt(c.Pos + new Vec2(dx * t2, dy * t2))) densasEmVolta++;

				MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
				TickDosCorposSemDono(Protocol.TickSeconds);
				_vazioDesde[pedra.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);
				MoveToZone(pl.Id, pedra, PontoDeHabitante(pedra, ++_lugarDaBancadaDeEmbaralho));

				int naPedra = presos.Count(c => mapaPedra.BlockedAt(c.Pos));
				Checa("PRECONDICAO (o defeito injetado, e ele e do MAPA): o raio do sorteio em volta dos "
					+ "8 corpos de Icer esta cheio de celula densa -- aqui um sorteio cru cairia na rocha",
					  densasEmVolta > 0, $"{densasEmVolta} celulas densas");
				Checa("...e mesmo assim NENHUM dos 8 embaralhou pra dentro da pedra em Icer",
					  naPedra == 0, $"{naPedra} na rocha");
			}

			// ---- PLANETA NUNCA VISITADO NAO EMBARALHA ----
			var virgem = ZoneKey.Premade("Arlia");
			ServerPlayer? arliano = NascerNpc("cidadao", virgem,
				PontoDeHabitante(virgem, ++_lugarDaBancadaDeEmbaralho), ++_lugarDaBancadaDeEmbaralho);
			if (arliano != null)
			{
				forjados.Add(arliano);
				Vec2 nasceuEm = arliano.Pos;
				_vazioDesde.Remove(virgem.Hash);   // ninguem nunca saiu de la
				MoveToZone(pl.Id, virgem, PontoDeHabitante(virgem, ++_lugarDaBancadaDeEmbaralho));
				Checa("planeta que jogador NENHUM visitou nao embaralha na primeira visita "
					+ "(o prazo e do ULTIMO que saiu, e ninguem saiu)",
					  (arliano.Pos - nasceuEm).Length < 0.001);
			}

			// ---------------------------------------------------------------
			// FAMILIA 6 -- QUEM FICA PARADO, E QUEM DESISTE DE ANDAR
			// ---------------------------------------------------------------
			// ============================ AS DUAS METADES QUE FALTAVAM ============================
			// A familia 3 prova que TODO MUNDO se move. Isso e metade da regra -- e ate esta sessao era
			// a regra inteira, o que estava ERRADO: o dono pediu explicitamente que *"chefe de saga no
			// posto dele, ... defensor de invasao ... e qualquer um cujo lugar seja o papel dele"* nao
			// embaralhassem. Cada afirmacao aqui vem com o CONTROLE ao lado (um cidadao comum, nascido
			// no mesmo ponto, na mesma chegada): sem ele "o chefe nao andou" ficaria verde num
			// embaralho que nao andou com ninguem.
			// ==============================================================================
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			Vec2 posto = mapa.PontoLivrePerto(berco + new Vec2(0, 3 * t2));
			ServerPlayer? chefe = NascerNpc("freeza_namek", palco, posto, ++_lugarDaBancadaDeEmbaralho);
			ServerPlayer? controle = NascerNpc("cidadao", palco, posto, ++_lugarDaBancadaDeEmbaralho);
			ServerPlayer? guarda = NascerNpc("cidadao", palco, posto, ++_lugarDaBancadaDeEmbaralho);
			if (chefe == null || controle == null || guarda == null)
				Checa("PRECONDICAO: nasceram o chefe, o defensor e o cidadao de controle", false);
			else
			{
				forjados.Add(chefe); forjados.Add(controle); forjados.Add(guarda);

				// A CAMPANHA FORJADA: o crivo do defensor nao e o TIPO (ele nasce `cidadao`, e o
				// `guarda` acima e literalmente o mesmo molde do `controle`) -- e estar na lista de
				// `Defensores` da invasao daquela superficie. Entao a bancada monta uma.
				ChaveDePlaneta? chave = ChaveDePlaneta.Da(palco);
				if (chave == null) Checa("PRECONDICAO: a Earth tem chave de planeta", false);
				else
				{
					var campanha = new Invasao
					{
						Chave = chave.Value, Zona = palco, Planeta = palco.Name,
						Bandeira = posto, Invasor = pl.Id,
					};
					campanha.Defensores[guarda.Id] = true;
					_invasoes[chave.Value] = campanha;

					Vec2 chefeAntes = chefe.Pos, controleAntes = controle.Pos, guardaAntes = guarda.Pos;
					_vazioDesde[palco.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);
					MoveToZone(pl.Id, palco, berco);

					Checa("CONTROLE: o cidadao comum nascido no mesmo ponto ANDOU (senao as tres linhas "
						+ "abaixo nao saberiam ficar vermelhas)",
						  (controle.Pos - controleAntes).Length > 0.001,
						  $"{(controle.Pos - controleAntes).Length:0.00} px");
					Checa("o CHEFE DE SAGA fica no posto dele (`Tipo == Chefe`) -- ele e um destino, "
						+ "e quem o move e o roteiro",
						  (chefe.Pos - chefeAntes).Length < 0.001,
						  $"o chefe andou {(chefe.Pos - chefeAntes).Length:0.00} px");
					Checa("o DEFENSOR DA BANDEIRA fica onde nasceu -- e ele e do MESMO molde do controle, "
						+ "entao quem o salvou foi a lista de `Defensores` e nao o tipo",
						  (guarda.Pos - guardaAntes).Length < 0.001,
						  $"o defensor andou {(guarda.Pos - guardaAntes).Length:0.00} px");

					_invasoes.Remove(chave.Value);

					// ---- DETERMINISMO: A MESMA SEMENTE DA O MESMO EMBARALHO ----
					// Rebobina o corpo E a volta (que entra na semente de proposito, pra duas ausencias
					// seguidas nao darem o mesmo deslocamento) e refaz a chegada. Sem rebobinar a volta
					// isto mediria o contrario do que diz.
					Vec2 primeiraVez = controle.Pos;
					controle.Pos = controleAntes;
					_embaralhosDaZona[palco.Hash] = _embaralhosDaZona[palco.Hash] - 1;
					MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
					TickDosCorposSemDono(Protocol.TickSeconds);
					_vazioDesde[palco.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);
					MoveToZone(pl.Id, palco, berco);

					Checa("DETERMINISMO: a mesma semente e a mesma volta poem o corpo no MESMO pixel "
						+ "(nada de relogio de parede nem `Random` sem semente no sorteio)",
						  (controle.Pos - primeiraVez).Length < 0.001,
						  $"{primeiraVez} x {controle.Pos}");
				}

				// ---- N TENTATIVAS E ENTAO ELE FICA ONDE ESTAVA ----
				// ============================ O DEFEITO INJETADO, E ELE E REVERSIVEL ============================
				// O dono: *"Se o sorteio nao achar lugar livre em N tentativas, o NPC fica onde estava --
				// e melhor nao ter andado que ter afundado."* Emparedar de verdade e a unica forma de a
				// linha poder ficar vermelha: em campo aberto ela seria verde por nunca disparar.
				//
				// A camada de obras e a REVERSIVEL de proposito (o bitset do arquivo nao se toca), e
				// quem desfaz e `AplicarColisaoDasObras`, que REFAZ a zona a partir do `_noChao` -- ou
				// seja, as construcoes de verdade do servidor voltam exatamente como estavam.
				// ==========================================================================================
				MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
				TickDosCorposSemDono(Protocol.TickSeconds);

				int mx = (int)MathF.Floor(controle.Pos.X / t2);
				int my = (int)MathF.Floor(controle.Pos.Y / t2);

				// UMA TESTEMUNHA: uma celula do anel que servia de chao ANTES. E ela que prova as duas
				// pontas -- que o defeito injetado pegou, e que o desfazer o desfez. Uma celula
				// escolhida no chute podia ser rocha do proprio arquivo, e ai as duas linhas
				// mentiriam juntas (a de precondicao verde por acaso, a de restauracao vermelha).
				int tx = mx + Povoamento.TilesMinDoEmbaralho, ty = my;
				bool servia = mapa.ServeDeChao(tx, ty);

				for (int dx = -Povoamento.TilesMaxDoEmbaralho; dx <= Povoamento.TilesMaxDoEmbaralho; dx++)
					for (int dy = -Povoamento.TilesMaxDoEmbaralho; dy <= Povoamento.TilesMaxDoEmbaralho; dy++)
						if (dx != 0 || dy != 0) mapa.Bloquear(mx + dx, my + dy);

				// A PRECONDICAO E AFIRMADA AGORA, E NAO DEPOIS DA CHEGADA -- e isto e uma medida, nao
				// estilo: entrar numa zona REAPLICA a colisao das obras (`MoveToZone` -> a zona e
				// refeita a partir do `_noChao`), o que apaga a camada injetada. Afirmada depois, esta
				// linha media a limpeza e nao o defeito, e ficava vermelha com o embaralho CERTO.
				Checa("PRECONDICAO (o defeito injetado): uma celula do anel que SERVIA de chao deixou de servir",
					  servia && !mapa.ServeDeChao(tx, ty), $"servia={servia}");

				Vec2 emparedadoEm = controle.Pos;
				_vazioDesde[palco.Hash] = NowMs() - (long)((Povoamento.SegundosAteOEmbaralho + 60) * 1000);
				MoveToZone(pl.Id, palco, berco);

				Checa("emparedado, o habitante FICA ONDE ESTAVA -- e nao e empurrado pro chao livre mais "
					+ "proximo, que seria o teleporte que o dono nao pediu",
					  (controle.Pos - emparedadoEm).Length < 0.001,
					  $"andou {(controle.Pos - emparedadoEm).Length:0.00} px");

				AplicarColisaoDasObras(palco);   // devolve a zona ao que o `_noChao` manda
				Checa("...e a colisao da Earth voltou ao que o mundo manda depois do defeito injetado",
					  mapa.ServeDeChao(tx, ty) == servia);
			}

			// ---------------------------------------------------------------
			// FAMILIA 9 -- O MUNDO NAO PARA (uma linha por sistema)
			// ---------------------------------------------------------------
			// ============================ A DISTINCAO QUE A TAREFA INTEIRA DEPENDE ============================
			// O dono pediu pra desligar **a mente (IA)**. Ele NAO pediu pra congelar o mundo, e a
			// diferenca entre as duas coisas e a diferenca entre otimizacao e bug: um prazo de saga que
			// para de contar num planeta vazio e uma saga que nunca acontece; uma gestacao que nao
			// vence e um jogador esperando pra sempre por uma criatura que o servidor esqueceu.
			//
			// **DECIDIR** (a mente, que so importa com plateia) x **ACONTECER** (o mundo, que segue).
			// A familia 7 provou a primeira metade; esta prova a segunda, com UMA linha por sistema --
			// porque uma familia que medisse "o mundo em geral" ficaria verde com um sistema congelado
			// dentro, e o sintoma de um sistema congelado e o silencio.
			//
			// O DEFEITO INJETADO desta familia e a otimizacao tentadora que NAO foi feita: pendurar
			// estes laços no mesmo `_zonasComGente` da mente. A ultima linha da familia a executa (nao
			// chamar o tique de uma zona vazia) e mostra o que cada uma destas medidas viraria.
			// ============================================================================================
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);
			Checa("PRECONDICAO: a Terra esta sem jogador nenhum -- tudo abaixo acontece sem plateia",
				  !_zonasComGente.Contains(palco.Hash) && ZoneList(palco.Hash).Count > 0);

			// ---- 1/9. O RELOGIO DO MUNDO (e quem move as fases de invasao e o prazo de arranque) ----
			double relogioAntes = _relogioDoMundo;
			for (int t = 0; t < 300; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			Checa("MUNDO 1/9 -- o RELOGIO DO MUNDO anda com todos os planetas vazios",
				  _relogioDoMundo - relogioAntes > 299 * Protocol.TickSeconds,
				  $"{_relogioDoMundo - relogioAntes:0.000} s");

			// ---- 2/9. OS RELOGIOS DO CORPO (forma, carga, voo, estomago...) ----
			// A cobaia e um habitante do planeta vazio. O estomago e o relogio mais facil de afirmar
			// (nutricao so anda pra baixo), e ele representa os dez: se o bloco inteiro fosse
			// pendurado na plateia, um chefe transformado num planeta vazio ficaria transformado pra
			// sempre e quem se deslogasse em furia lendaria acordaria possuido pra sempre.
			ServerPlayer cobaia = vila[0];
			cobaia.Ficha.IsInFight = false;
			cobaia.Ficha.stamina = 0;
			cobaia.Ficha.CurrentNutrition = 50;
			double nutricaoAntes = cobaia.Ficha.CurrentNutrition;
			for (int t = 0; t < 400; t++) TickCombate(Protocol.TickSeconds);
			Checa("MUNDO 2/9 -- os RELOGIOS DO CORPO seguem: a nutricao de um habitante cai num planeta "
				+ "onde nao ha ninguem pra ver",
				  cobaia.Ficha.CurrentNutrition < nutricaoAntes,
				  $"{nutricaoAntes:0.000} -> {cobaia.Ficha.CurrentNutrition:0.000}");

			// ---- 3/9. A SAGA CONTA OS DIAS ----
			if (_cadeia.Length == 0)
				Checa("MUNDO 3/9 -- PRECONDICAO: ha cadeia de sagas no `npcs.json`", false, "cadeia vazia");
			else
			{
				TickDasSagas();                                 // ancora `UltimoDiaVisto`
				long diaAntes = _sagas.UltimoDiaVisto;
				_adiantoDoCeu += Ceu.SegundosPorDia;            // a MESMA manivela do `--ceu`
				TickDasSagas();
				long diaDepois = _sagas.UltimoDiaVisto;
				_adiantoDoCeu -= Ceu.SegundosPorDia;
				TickDasSagas();                                 // reancora (recuar nao desvira nada)
				Checa("MUNDO 3/9 -- a SAGA conta os dias com o mundo inteiro vazio (prazo de chefe a "
					+ "caminho nao espera plateia)",
					  diaDepois == diaAntes + 1, $"dia {diaAntes} -> {diaDepois}");
			}

			// ---- 4/9. A INVASAO NAO CONGELA: ELA E RESOLVIDA ----
			// Este e o caso que so aparece medindo. Uma invasao cujo invasor deslogou nao pode ficar
			// PENDURADA: a guarnicao inteira ficaria de pe num planeta vazio, e o dono do planeta
			// nunca recuperaria o dominio. Congelar o tique da zona vazia produziria exatamente isso.
			ChaveDePlaneta? chaveDaTerra = ChaveDePlaneta.Da(palco);
			if (chaveDaTerra == null)
				Checa("MUNDO 4/9 -- PRECONDICAO: a Terra tem chave de planeta", false);
			else
			{
				_invasoes[chaveDaTerra.Value] = new Invasao
				{
					Chave = chaveDaTerra.Value, Zona = palco, Planeta = palco.Name,
					Bandeira = berco, Invasor = pl.Id,
					PrazoEm = _relogioDoMundo + 99999, PassoEm = _relogioDoMundo + 99999,
				};
				TickDasInvasoes();
				Checa("MUNDO 4/9 -- a INVASAO de um planeta vazio e RESOLVIDA e nao congelada "
					+ "(senao a guarnicao ficaria de pe pra sempre)",
					  !_invasoes.ContainsKey(chaveDaTerra.Value));
				_invasoes.Remove(chaveDaTerra.Value);
			}

			// ---- 5/9. A CONQUISTA COBRA LEALDADE DE QUEM NAO APARECE ----
			var dominioDeTeste = new Dominio
			{
				PreFeito = true, Planeta = palco.Name, Assinatura = "bancada-embaralho",
				Nome = "Bancada", Conta = "bancada", Lealdade = 100,
				Visto = TempoDoMundo - 3600 * 1000,   // ha muito tempo ninguem aparece
			};
			_dominios.Add(dominioDeTeste);
			TickDaConquista();                       // ancora a cobranca
			double lealdadeAntes = dominioDeTeste.Lealdade;
			_adiantoDoCeu += 10 * 3600;              // dez horas de mundo
			TickDaConquista();
			_adiantoDoCeu -= 10 * 3600;
			double lealdadeDepois = dominioDeTeste.Lealdade;
			_dominios.Remove(dominioDeTeste);
			TickDaConquista();                       // reancora depois de o relogio recuar
			Checa("MUNDO 5/9 -- a CONQUISTA cobra: a lealdade de um dominio sem visita cai com o "
				+ "planeta vazio (e e assim que se perde um planeta esquecido)",
				  lealdadeDepois < lealdadeAntes, $"{lealdadeAntes:0.0} -> {lealdadeDepois:0.0}");

			// ---- 6/9. O RESPAWN REPOE HABITANTE ----
			int povoAntes = CorposSemDonoNaZona(palco);
			_proximaManutencao = _relogioDoPovoamento;   // a manutencao de 5 min vence AGORA
			for (int t = 0; t < 400; t++) TickDoPovoamento(Protocol.TickSeconds);
			int povoDepois = CorposSemDonoNaZona(palco);
			Checa("MUNDO 6/9 -- o RESPAWN repoe habitantes num planeta onde nao ha ninguem "
				+ "(quem volta depois de meses encontra uma cidade, e nao um cemiterio)",
				  povoDepois > povoAntes, $"{povoAntes} -> {povoDepois} corpos");

			// ---- 7/9. O CRESCIMENTO (as macas brotam de volta) ----
			const int arvoreDeTeste = 987_654;
			_macas[arvoreDeTeste] = 0;
			int tentativasDeBrotar = 0;
			while (_macas.TryGetValue(arvoreDeTeste, out int quantas) && quantas == 0
				   && tentativasDeBrotar++ < 200)
			{
				_proximoBroto = 0;   // o relogio do broto e de PAREDE: um laco de bancada nao o gira
				TickDasArvores();
			}
			bool brotou = !_macas.ContainsKey(arvoreDeTeste) || _macas[arvoreDeTeste] > 0;
			_macas.Remove(arvoreDeTeste);
			Checa("MUNDO 7/9 -- o CRESCIMENTO segue: a arvore de um planeta vazio volta a dar fruto",
				  brotou, $"{tentativasDeBrotar} passadas sem brotar");

			// ---- 8/9. O CEU E O CLIMA ----
			double horaAntes = CeuDe(pl).HoraCrua;
			ForcarClima(palco, TipoDeClima.Chuva, 2, 1, "bancada");
			bool choveu = _climaForcado.ContainsKey(palco.Hash);
			_adiantoDoCeu += 600;                    // dez minutos de mundo
			TickDoCeu();                             // ele e quem chama o `TickDoClima`
			bool parouDeChover = !_climaForcado.ContainsKey(palco.Hash);
			double horaDepois = CeuDe(pl).HoraCrua;
			_adiantoDoCeu -= 600;
			Checa("MUNDO 8/9 -- o CEU e o CLIMA andam num planeta sem ninguem: a hora local avanca e "
				+ "a tempestade forcada vence o prazo dela",
				  choveu && parouDeChover && Math.Abs(horaDepois - horaAntes) > 1e-9,
				  $"chuva={choveu} passou={parouDeChover} hora {horaAntes:0.0000} -> {horaDepois:0.0000}");

			// ---- 9/9. A GESTACAO DE TECNOLOGIA E ALCANCADA NUM PLANETA VAZIO ----
			//
			// ============================ ESTA AFIRMACAO FOI INVERTIDA, E DE PROPOSITO ============================
			// Ela dizia *"o tanque nao espera o dono voltar pra abrir"* e media isso pelo lab SUMIR --
			// numa epoca em que o nascimento nao criava criatura nenhuma, entao "sumiu" era tudo o que
			// havia pra medir. Com o parto implementado (`NascerBioAndroide`), aquilo virou um exploit
			// com nome: **deslogar antes da hora e voltar com o laboratorio sumido e o personagem
			// intacto** -- meio milhao de zeni pra escapar da propria sentenca.
			//
			// O original espera o criador (`DNALabs.dm:355-367` so nasce com `creator.client`), e agora
			// o port tambem. O que esta linha mede continua sendo o que esta familia inteira mede -- que
			// o RELOGIO do mundo alcanca uma zona sem ninguem --, so que pelo lado certo: a fornada
			// vencida de um dono ausente **fica de pe, intacta, esperando**. Se alguem "otimizar" o
			// tique por zona vazia, o `Fornada` continuaria vencido e o lab continuaria la -- e a
			// diferenca aparece na linha de baixo, que exige o tique ter CHEGADO nele.
			// ==================================================================================================
			var labForjado = new Obra
			{
				Id = 9_870_001, Tipo = "Research_Station", Lab = 2,
				X = berco.X, Y = berco.Y, DonoConta = "conta-que-nao-existe",
				Fornada = new Gestacao
				{
					DonoConta = "conta-que-nao-existe", MaiorBp = 1000,
					Amostras = { new Amostra { Raca = "Human", Doador = "ninguem", Bp = 1000 } },
					PrometidaEm = NowMs() - 1,
				},
			};
			labForjado.PorZona(palco);
			_noChao.Add(labForjado);
			TickDaGestacao();
			bool esperou = _noChao.Contains(labForjado) && labForjado.Fornada is { PrometidaEm: > 0 };
			_noChao.Remove(labForjado);
			Checa("MUNDO 9/9 -- a GESTACAO de tecnologia e ALCANCADA num planeta vazio, e o tanque "
				+ "vencido ESPERA o criador voltar em vez de abrir sozinho (deslogar nao escapa da "
				+ "sentenca)",
				  esperou);

			// ---- O DEFEITO INJETADO DA FAMILIA INTEIRA ----
			// A "otimizacao" tentadora, executada: NAO chamar os relogios do mundo quando a zona esta
			// vazia. E literalmente isso que congelar o mundo junto com a mente faria, e a medida
			// abaixo e o que cada uma das nove linhas acima viraria -- todas verdes, e o jogo parado.
			double nutricaoCongelada = cobaia.Ficha.CurrentNutrition;
			double relogioCongelado = _relogioDoMundo;

			// O "TIQUE" DA ZONA CONGELADA E ESTE ESPACO EM BRANCO -- e isso nao e piada: congelar por
			// zona vazia e literalmente nao chamar nada, e o efeito de nao chamar nada e o que as duas
			// comparacoes abaixo medem. Elas sao o unico jeito de as nove linhas de cima terem como
			// ficar vermelhas: cada uma delas mede uma diferenca, e diferenca de nada e zero.
			for (int t = 0; t < 400; t++) TickDoTempoQueNaoPassa();

			Checa("PRECONDICAO (o defeito injetado): com o MUNDO congelado junto com a mente, 400 "
				+ "tiques nao movem nem o relogio nem o corpo -- e nenhuma das nove linhas acusaria",
				  Math.Abs(cobaia.Ficha.CurrentNutrition - nutricaoCongelada) < 1e-12
				  && Math.Abs(_relogioDoMundo - relogioCongelado) < 1e-12);

			// ---------------------------------------------------------------
			// FAMILIA 10 -- UMA VEZ SO, MEDIDA COM O DOBRO DO PRAZO
			// ---------------------------------------------------------------
			// ============================ POR QUE O DOBRO, E NAO "SAIR E VOLTAR" ============================
			// A familia 3 ja prova que sair e voltar NA HORA nao embaralha de novo. Ela nao prova o que
			// o dono escreveu -- *"essa mudanca de posicao SO BASTA ACONTECER 1 VEZ"* --, porque o modo
			// de falhar que ele teme e o outro: um planeta que fica VAZIO POR MUITO TEMPO e embaralha
			// duas, tres, dez vezes, ou que embaralha DE NOVO com o jogador ja dentro. O primeiro
			// deixaria os habitantes a quilometros de casa; o segundo faria a cidade dar um pulo na
			// frente de quem esta olhando -- que e a unica forma de o embaralho aparecer como bug.
			//
			// Entao aqui o planeta fica o DOBRO do prazo vazio, e depois o jogador fica o DOBRO do
			// prazo DENTRO. A afirmacao nao e "ninguem se moveu" (com plateia eles andam, e devem):
			// e que **nenhum corpo deu um salto de embaralho** num unico tique. Um passo de caminhada
			// e da ordem de um pixel por tique; o embaralho e no MINIMO tres tiles.
			// ==========================================================================================
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);

			var moradores = ZoneList(palco.Hash).Where(EhNpcDoMundo).ToList();
			_embaralhosDaZona.Remove(palco.Hash);
			_vazioDesde[palco.Hash] = NowMs() - (long)(Povoamento.SegundosAteOEmbaralho * 2000);
			GD.Print($"  (a bancada poe a Terra {Povoamento.SegundosAteOEmbaralho * 2:0} s vazia -- "
				   + $"o DOBRO do prazo -- com {moradores.Count} moradores)");

			var posDeAntes = moradores.Select(c => c.Pos).ToList();
			MoveToZone(pl.Id, palco, berco);
			int mexeramNaChegada = moradores.Count(c => (c.Pos - posDeAntes[moradores.IndexOf(c)]).Length > 0.001);

			Checa("com o DOBRO do prazo vencido, a chegada embaralha -- UMA vez, e a zona registra "
				+ "volta 1",
				  mexeramNaChegada > 0 && _embaralhosDaZona[palco.Hash] == 1,
				  $"{mexeramNaChegada} mexeram, volta {_embaralhosDaZona.GetValueOrDefault(palco.Hash)}");

			// ---- E AGORA O DOBRO DO PRAZO COM O JOGADOR DENTRO ----
			const double saltoDeEmbaralho = Povoamento.TilesMinDoEmbaralho * ZoneCollision.TileSize;
			int tiquesDoDobro = (int)(Povoamento.SegundosAteOEmbaralho * 2 / Protocol.TickSeconds);
			var ondeEstavam = moradores.ToDictionary(c => c.Id, c => c.Pos);
			double maiorSalto = 0;

			for (int t = 0; t < tiquesDoDobro; t++)
			{
				TickDosCorposSemDono(Protocol.TickSeconds);
				foreach (ServerPlayer c in moradores)
				{
					if (!_players.ContainsKey(c.Id)) continue;
					double salto = (c.Pos - ondeEstavam[c.Id]).Length;
					if (salto > maiorSalto) maiorSalto = salto;
					ondeEstavam[c.Id] = c.Pos;
				}
			}

			Checa($"...e {tiquesDoDobro} tiques depois ({Povoamento.SegundosAteOEmbaralho * 2:0} s, o dobro "
				+ "do prazo) NENHUM corpo deu um salto de embaralho na frente do jogador",
				  maiorSalto < saltoDeEmbaralho,
				  $"o maior passo de um tique foi {maiorSalto:0.00} px (um embaralho e >= {saltoDeEmbaralho:0})");
			Checa("...e a zona continua na volta 1: o embaralho aconteceu UMA vez, como o dono pediu",
				  _embaralhosDaZona.GetValueOrDefault(palco.Hash) == 1 && !_vazioDesde.ContainsKey(palco.Hash),
				  $"volta {_embaralhosDaZona.GetValueOrDefault(palco.Hash)}");

			// ---- O DEFEITO INJETADO: a marca que nao foi consumida ----
			// Se o `EmbaralharSeEsfriou` esquecesse de tirar a zona do `_vazioDesde`, TODA chegada
			// embaralharia. A injecao repoe a marca a mao e mede o mesmo salto -- e ele dispara.
			_vazioDesde[palco.Hash] = NowMs() - (long)(Povoamento.SegundosAteOEmbaralho * 2000);
			var antesDaRepeticao = moradores.ToDictionary(c => c.Id, c => c.Pos);
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			MoveToZone(pl.Id, palco, berco);
			double saltoRepetido = moradores.Where(c => _players.ContainsKey(c.Id))
										    .Select(c => (c.Pos - antesDaRepeticao[c.Id]).Length)
										    .DefaultIfEmpty(0).Max();
			Checa("PRECONDICAO (o defeito injetado): reposta a marca a mao, a chegada embaralha DE NOVO "
				+ "-- e o detector de salto acusa, entao as duas linhas acima sabem ficar vermelhas",
				  saltoRepetido >= saltoDeEmbaralho, $"maior salto {saltoRepetido:0.00} px");

			// ---------------------------------------------------------------
			// FAMILIA 11 -- SOBREVIVE AO SAVE
			// ---------------------------------------------------------------
			// ============================ A RESPOSTA E "O SAVE NAO TEM OPINIAO" ============================
			// A pergunta ("o embaralho sobrevive a um save?") supoe que o disco guarda onde um NPC
			// esta. Ele nao guarda -- e isso e uma DECISAO, nao um esquecimento (ver o cabecalho do
			// `GameServer.Embaralho.cs`). As duas portas do disco recusam um corpo sem dono:
			//
			//   * `Persistir` sai na primeira linha com `Slot < 0` / `Peer == null`;
			//   * o `mundo.json` grava OBRA, e nao corpo.
			//
			// Uma afirmacao dessas so vale com o CONTROLE ao lado -- se o save nao gravasse NADA, as
			// duas linhas abaixo ficariam verdes num servidor que perde tudo a cada reinicio. Entao a
			// primeira coisa medida e que o save FUNCIONA pra um jogador.
			// ==========================================================================================
			if (_store == null)
				Checa("PRECONDICAO: ha armazenamento de contas", false);
			else
			{
				// O CONTROLE: a posicao do JOGADOR vai pro disco, e da pra ler de volta.
				Vec2 ondeOHostEstava = pl.Pos;
				pl.Pos = berco + new Vec2(7 * ZoneCollision.TileSize, 3 * ZoneCollision.TileSize);
				Persistir(pl);
				CharacterSave? gravado = pl.Slot >= 0
					? _store.Carregar(pl.Conta)?.Slots[pl.Slot] : null;
				Checa("CONTROLE: a posicao de um JOGADOR vai pro disco e volta igual (senao 'o NPC nao "
					+ "vai pro disco' seria elogio a um save quebrado)",
					  gravado != null && Math.Abs(gravado.X - pl.Pos.X) < 0.5
								      && Math.Abs(gravado.Y - pl.Pos.Y) < 0.5,
					  gravado == null ? "nao li o save" : $"({gravado.X:0},{gravado.Y:0}) x {pl.Pos}");
				pl.Pos = ondeOHostEstava;

				// E AGORA O SAVE INTEIRO, o mesmo que o `Tick()` dispara de dois em dois minutos.
				var ondeOsNpcsEstavam = moradores.Where(c => _players.ContainsKey(c.Id))
												 .ToDictionary(c => c.Id, c => c.Pos);
				foreach (ServerPlayer p in _players.Values) Persistir(p);

				string caminhoDoTeste = System.IO.Path.Combine(_store.Pasta, "mundo-bancada-embaralho.json");
				GravarMundoEm(caminhoDoTeste);
				string mundoGravado = System.IO.File.ReadAllText(caminhoDoTeste);
				System.IO.File.Delete(caminhoDoTeste);

				int mudouNoSave = ondeOsNpcsEstavam.Count(
					kv => _players.ContainsKey(kv.Key) && (_players[kv.Key].Pos - kv.Value).Length > 0.001);

				Checa("o SAVE PERIODICO INTEIRO nao mexe em onde os habitantes embaralhados estao",
					  mudouNoSave == 0, $"{mudouNoSave} corpos mudaram de lugar por causa do save");

				bool nomeDeNpcNoMundo = moradores.Any(c => c.Name.Length > 2 && mundoGravado.Contains(c.Name));
				Checa("...e o `mundo.json` nao guarda corpo nenhum: nao ha o que sobrescrever a memoria "
					+ "no proximo boot (o vilarejo renasce em `PontoDeHabitante`, funcao pura da semente)",
					  !nomeDeNpcNoMundo);

				// A MARCA NAO E PERSISTIDA, E ISSO TAMBEM E DECISAO. Um `_vazioDesde` vindo do disco
				// falaria de um mundo que nao existe mais -- depois de um reinicio ninguem saiu de
				// lugar nenhum, entao a primeira visita a qualquer planeta NAO embaralha.
				Checa("...e a marca de 'esta vazio desde' mora so na memoria (depois de um boot nenhum "
					+ "planeta embaralha, porque desde o boot ninguem saiu de lugar nenhum)",
					  !mundoGravado.Contains("vazioDesde") && !mundoGravado.Contains("Embaralho"));
			}

			// ---------------------------------------------------------------
			// FAMILIA 5 -- O TIQUE INTEIRO, E NAO SO O PEDACO QUE DA ORGULHO
			// ---------------------------------------------------------------
			// ============================ O CEGO QUE ESTA FAMILIA EXISTE PRA FECHAR ============================
			// A familia 2 mede `TickDosCorposSemDono` ISOLADO, ve um numero minusculo e conclui que o
			// congelamento e barato. Verdade -- e irrelevante. A pergunta do dono foi *"poupar recursos
			// do server ao maximo"*, e pra responder isso e preciso saber o que o tique inteiro custa e
			// **que fatia dele a mente e**. Sem esta familia o congelamento podia parecer decisivo
			// sendo uma casa decimal do orcamento, e foi exatamente o que aconteceu por uma fase.
			//
			// Entao aqui se mede, com o mundo POVOADO de verdade e ninguem online, os tres blocos que
			// custam POR CORPO -- e eles sao a quase totalidade do tique de um servidor vazio:
			//
			//   * `TickCombate`             -- recarga, atordoamento e regeneracao passiva. MUNDO;
			//   * `TickDosRelogiosDoCorpo`  -- os dez relogios (forma, carga, voo, nado...). MUNDO;
			//   * `TickDosCorposSemDono`    -- a MENTE, e a unica das tres com porta de plateia.
			//
			// A leitura que interessa e a ULTIMA linha: quanto por cento do que sobra o congelamento
			// alcanca. Se for pouco, o proximo esforco de otimizacao nao e aqui -- e saber isso vale
			// mais do que qualquer micro-otimizacao que esta sessao pudesse ter feito no escuro.
			// ==============================================================================================
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));

			// O MUNDO CHEIO, E NAO O QUE DEU TEMPO DE NASCER. O povoamento nasce 1 corpo por tique
			// (`Povoamento.NascimentosPorTique`), entao no primeiro login so existe um punhado -- medir
			// ali diria "o servidor povoado custa quase nada" contando 18 corpos em vez de 148.
			for (int t = 0; t < 400; t++) TickDoPovoamento(Protocol.TickSeconds);
			int corpos = CorposSemDonoNoServidor();
			GD.Print($"  (a bancada encheu o mundo antes de medir: {corpos} corpos sem dono, "
				   + $"{_players.Count} no `_players`)");

			// Um tique de aquecimento por bloco: a primeira passada paga JIT e cache frio, e ela
			// entraria inteira na media do primeiro que fosse medido.
			TickCombate(Protocol.TickSeconds);
			TickDosRelogiosDoCorpo(Protocol.TickSeconds);
			TickDosCorposSemDono(Protocol.TickSeconds);

			const int voltas = 2000;

			ulong m0 = Time.GetTicksUsec();
			for (int t = 0; t < voltas; t++) TickCombate(Protocol.TickSeconds);
			double usCombate = (Time.GetTicksUsec() - m0) / (double)voltas;

			m0 = Time.GetTicksUsec();
			for (int t = 0; t < voltas; t++) TickDosRelogiosDoCorpo(Protocol.TickSeconds);
			double usRelogios = (Time.GetTicksUsec() - m0) / (double)voltas;

			m0 = Time.GetTicksUsec();
			for (int t = 0; t < voltas; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double usMente = (Time.GetTicksUsec() - m0) / (double)voltas;

			double orcamento = Protocol.TickSeconds * 1_000_000.0;   // us disponiveis por tique
			double porCorpo = usCombate + usRelogios + usMente;

			GD.Print($"  MEDIDA  tique de MUNDO VAZIO com {corpos} corpos (us/tique):");
			GD.Print($"            combate  {usCombate,7:0.00}   ({usCombate / orcamento * 100:0.00}% do orcamento)  MUNDO");
			GD.Print($"            relogios {usRelogios,7:0.00}   ({usRelogios / orcamento * 100:0.00}% do orcamento)  MUNDO");
			GD.Print($"            mente    {usMente,7:0.00}   ({usMente / orcamento * 100:0.00}% do orcamento)  <- a unica com porta de plateia");
			GD.Print($"            ------------------------------------------------------");
			GD.Print($"            por corpo{porCorpo,7:0.00}   ({porCorpo / orcamento * 100:0.00}% do orcamento de {orcamento:0} us)");
			GD.Print($"          => a MENTE e {usMente / porCorpo * 100:0.0}% do que o servidor vazio gasta por corpo; "
				   + $"o resto e mundo e NAO pode congelar.");

			Checa("o tique de um servidor povoado e vazio cabe folgado no orcamento (< 25%)",
				  porCorpo < orcamento * 0.25,
				  $"{porCorpo:0.00} us de {orcamento:0} us");

			// ---- O `.ToList()` DO ESPACO: a medida e de LIXO, nao de tempo ----
			// ============================ POR QUE MEDIR BYTES E NAO MICROSSEGUNDOS ============================
			// O defeito era `foreach (pl in _players.Values.ToList()) TickDoEspaco(pl)`: uma `List` de
			// TODO corpo do servidor, 30x por segundo, pra chamar um metodo que sai na primeira linha
			// pra quem nao esta no espaco. Em tempo isso quase nao aparece -- aparece no COLETOR, e um
			// microbenchmark de tempo diria "nao mudou nada" e mandaria devolver o conserto.
			//
			// O CONTROLE E O PROPRIO DEFEITO: a linha antiga, escrita aqui e so aqui, medida ao lado da
			// nova. Sem ele "alocou pouco" nao teria com o que ser comparado.
			// ============================================================================================
			long a0 = GC.GetAllocatedBytesForCurrentThread();
			for (int t = 0; t < 100; t++) { List<ServerPlayer> lixo = _players.Values.ToList(); _ = lixo.Count; }
			double bytesVelho = (GC.GetAllocatedBytesForCurrentThread() - a0) / 100.0;

			TickDoEspacoDeTodos();   // aquece o buffer ate a capacidade que ele vai usar
			a0 = GC.GetAllocatedBytesForCurrentThread();
			for (int t = 0; t < 100; t++) TickDoEspacoDeTodos();
			double bytesNovo = (GC.GetAllocatedBytesForCurrentThread() - a0) / 100.0;

			GD.Print($"  MEDIDA  a varredura do espaco com {_players.Count} corpos: "
				   + $"a linha ANTIGA alocava {bytesVelho:0} bytes/tique ({bytesVelho * 30 / 1024:0} KB/s de lixo a 30 Hz); "
				   + $"a nova aloca {bytesNovo:0}");
			Checa("PRECONDICAO (o defeito, medido): o `.ToList()` de `_players` alocava de verdade",
				  bytesVelho > 0, $"{bytesVelho:0} bytes");
			Checa("...e a varredura do espaco parou de alocar por tique (o buffer e reusado)",
				  bytesNovo < bytesVelho / 8, $"{bytesNovo:0} x {bytesVelho:0} bytes");

			// ---- E ELA AINDA CHEGA EM QUEM ESTA LA EM CIMA ----
			// ============================ "NAO ALOCOU" NAO E "FUNCIONOU" ============================
			// As duas linhas acima ficariam VERDES num `TickDoEspacoDeTodos` de corpo vazio: nao alocar
			// nada e exatamente o que um metodo que nao faz nada faz. O buffer novo filtra por
			// `Espaco.EhEspaco` -- se esse filtro estivesse invertido, ou se o `ContainsKey` recusasse
			// todo mundo, o servidor pararia de trocar de chunk e de pousar, calado, e nenhuma medida
			// de bytes acusaria.
			//
			// Entao aqui o host SOBE, e a afirmacao e sobre EFEITO: o tique do espaco escreve
			// `ChunkAtual`, e ele comeca zerado depois da subida.
			// ==================================================================================
			var ceu = Espaco.Zona(SeedDoUniverso);
			PlanetaNoEspaco terra = Espaco.PreFeitos().First(p => p.Nome == "Earth");
			MoveToZone(pl.Id, ceu, Espaco.PontoDeDecolagem(terra));
			pl.ChunkAtual = default;

			// E O ALVO NAO PODE SER O PROPRIO ZERO: se o ponto de decolagem caisse na chunk `default`,
			// a linha de baixo ficaria verde com o metodo comentado. Esta e a precondicao que da a ela
			// o direito de ser lida.
			Checa("PRECONDICAO: o ponto de decolagem nao fica na chunk zerada (senao a afirmacao "
				+ "seguinte nao saberia ficar vermelha)",
				  ChunkId.De(pl.Pos) != default, $"{ChunkId.De(pl.Pos)}");

			TickDoEspacoDeTodos();

			Checa("a varredura nova CHEGA em quem esta no espaco (senao 'nao alocou' seria elogio a um "
				+ "metodo vazio)",
				  pl.ChunkAtual == ChunkId.De(pl.Pos),
				  $"ChunkAtual={pl.ChunkAtual} esperado={ChunkId.De(pl.Pos)}");

			// ---------------------------------------------------------------
			// FAMILIA 12 -- A VARREDURA GERAL: NINGUEM NA PAREDE, NINGUEM NA AGUA
			// ---------------------------------------------------------------
			// ============================ AS FAMILIAS 3 E 4 OLHAM OITO CORPOS; ESTA OLHA TODOS ============================
			// O dono escreveu a recusa em maiusculas (*"cuidado pra n colocar dentro de paredes"*), e
			// oito corpos num berco escolhido nao respondem por um mundo inteiro. Aqui o embaralho e
			// disparado em **todo planeta povoado** -- um por um, com o host chegando em cada -- e
			// depois se varre **todo corpo sem dono do servidor**, em todas as zonas.
			//
			// A AGUA E O MOTIVO DE ESTA FAMILIA EXISTIR ALEM DA 4. Ela nao e parede: nao esta no
			// bitset, o `BlockedCell` diz que da pra passar, e um sorteio que so olhasse colisao
			// poria o vilarejo de Namek dentro do oceano. Quem responde e a MESMA `ServeDeChao` que
			// poe todo corpo no mundo -- e a precondicao abaixo conta a agua no alcance do sorteio,
			// porque uma afirmacao sobre agua num mapa sem agua e uma afirmacao sobre nada.
			// ========================================================================================================
			const int t3 = ZoneCollision.TileSize;
			var zonasPovoadas = _players.Values.Where(EhNpcDoMundo)
				.GroupBy(c => c.Zone.Hash).Select(g => g.First().Zone).ToList();

			foreach (ZoneKey z in zonasPovoadas)
			{
				MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
				TickDosCorposSemDono(Protocol.TickSeconds);
				_vazioDesde[z.Hash] = NowMs() - (long)(Povoamento.SegundosAteOEmbaralho * 2000);
				MoveToZone(pl.Id, z, PontoDeNascimento(z));
			}
			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));

			int varridos = 0, naPedraGeral = 0, naAguaGeral = 0, naBordaGeral = 0;
			int aguaNoAlcance = 0, pedraNoAlcance = 0;
			foreach (ServerPlayer c in _players.Values.Where(EhNpcDoMundo).ToList())
			{
				ZoneCollision? m = MapaDaZonaOuCatalogo(c.Zone);
				if (m == null) continue;

				varridos++;
				int cx = (int)MathF.Floor(c.Pos.X / t3), cy = (int)MathF.Floor(c.Pos.Y / t3);
				if (m.BlockedCell(cx, cy)) naPedraGeral++;
				if (m.EhAgua(cx, cy)) naAguaGeral++;
				if (m.NaBorda(cx, cy)) naBordaGeral++;

				// O QUE HAVIA AO ALCANCE DO SORTEIO. Sem este numero, "ninguem caiu na agua" nao
				// distingue um funil que funciona de um mapa que nao tem agua.
				for (int dx = -Povoamento.TilesMaxDoEmbaralho; dx <= Povoamento.TilesMaxDoEmbaralho; dx++)
					for (int dy = -Povoamento.TilesMaxDoEmbaralho; dy <= Povoamento.TilesMaxDoEmbaralho; dy++)
					{
						if (m.EhAgua(cx + dx, cy + dy)) aguaNoAlcance++;
						if (m.BlockedCell(cx + dx, cy + dy)) pedraNoAlcance++;
					}
			}

			GD.Print($"  MEDIDA  varredura geral: {varridos} corpos sem dono em {zonasPovoadas.Count} "
				   + $"zona(s), todas embaralhadas -- {pedraNoAlcance} celulas de PEDRA e {aguaNoAlcance} "
				   + "celulas de AGUA no alcance do sorteio em volta deles");

			Checa("PRECONDICAO: a varredura tem corpo pra varrer e pedra E agua ao alcance do sorteio "
				+ "(sem isso as tres linhas abaixo seriam verdades sobre um mundo vazio)",
				  varridos > 50 && pedraNoAlcance > 0 && aguaNoAlcance > 0,
				  $"{varridos} corpos, {pedraNoAlcance} pedra, {aguaNoAlcance} agua");

			Checa("VARREDURA: nenhum corpo sem dono do servidor inteiro ficou DENTRO DE PAREDE",
				  naPedraGeral == 0, $"{naPedraGeral} na pedra");
			Checa("VARREDURA: nenhum ficou DENTRO DA AGUA (ela nao e parede -- quem a recusa e a "
				+ "`ServeDeChao`, a mesma pergunta que poe todo corpo no mundo)",
				  naAguaGeral == 0, $"{naAguaGeral} na agua");
			Checa("VARREDURA: nenhum ficou na BEIRADA do mapa (celula de onde nao se da um passo)",
				  naBordaGeral == 0, $"{naBordaGeral} na borda");

			// ---------------------------------------------------------------
			// FAMILIA 13 -- O GANHO, COM O MUNDO CHEIO E NINGUEM ONLINE
			// ---------------------------------------------------------------
			// ============================ A PERGUNTA ERA OTIMIZACAO; SEM NUMERO NAO HA RESPOSTA ============================
			// A familia 2 mediu o par com OITO corpos acordados. Este mede com o mundo POVOADO, e a
			// conta que interessa nao e o par bruto -- e **quanto custa um corpo ACORDADO**, porque so
			// esse numero permite dizer o que o congelamento economiza no servidor inteiro.
			//
			// O host so pode estar num planeta de cada vez, entao nao da pra acordar o mundo todo de
			// uma vez pra medir: o par medido e (todo mundo dormindo) x (a zona mais cheia acordada), e
			// dele sai o custo POR CORPO ACORDADO, que e o que se extrapola. A extrapolacao vai dita
			// como extrapolacao -- o numero medido e o par, nao o total.
			// ========================================================================================================
			ZoneKey zonaMaisCheia = palco;
			int maisCheia = 0;
			foreach (ZoneKey z in zonasPovoadas)
			{
				int n = ZoneList(z.Hash).Count(c => c.Cerebro != null && EhNpcDoMundo(c));
				if (n > maisCheia) { maisCheia = n; zonaMaisCheia = z; }
			}

			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
			TickDosCorposSemDono(Protocol.TickSeconds);
			int corposTotais = CorposSemDonoNoServidor();

			const int voltasDoGanho = 4000;
			for (int t = 0; t < 200; t++) TickDosCorposSemDono(Protocol.TickSeconds);   // aquece

			long decisoesDormindo = _decisoesDaMente;
			ulong g0 = Time.GetTicksUsec();
			for (int t = 0; t < voltasDoGanho; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double usTudoDormindo = (Time.GetTicksUsec() - g0) / (double)voltasDoGanho;
			decisoesDormindo = _decisoesDaMente - decisoesDormindo;

			MoveToZone(pl.Id, zonaMaisCheia, PontoDeNascimento(zonaMaisCheia));
			for (int t = 0; t < 200; t++) TickDosCorposSemDono(Protocol.TickSeconds);   // aquece

			long decisoesAcordado = _decisoesDaMente;
			g0 = Time.GetTicksUsec();
			for (int t = 0; t < voltasDoGanho; t++) TickDosCorposSemDono(Protocol.TickSeconds);
			double usComUmaZonaAcordada = (Time.GetTicksUsec() - g0) / (double)voltasDoGanho;
			decisoesAcordado = _decisoesDaMente - decisoesAcordado;

			int acordados = (int)(decisoesAcordado / voltasDoGanho);
			double usPorCorpoAcordado = acordados > 0
				? (usComUmaZonaAcordada - usTudoDormindo) / acordados : 0;
			double economiaNoMundoCheio = usPorCorpoAcordado * corposTotais;
			double orcamentoDoTique = Protocol.TickSeconds * 1_000_000.0;

			GD.Print($"  MEDIDA  GANHO com {corposTotais} corpos e NINGUEM online:");
			GD.Print($"            todos dormindo            {usTudoDormindo,7:0.00} us/tique  ({decisoesDormindo} decisoes)");
			GD.Print($"            {acordados,3} acordados em '{zonaMaisCheia.Name}'  {usComUmaZonaAcordada,7:0.00} us/tique  ({decisoesAcordado} decisoes)");
			GD.Print($"            => um corpo ACORDADO custa {usPorCorpoAcordado:0.000} us/tique");
			GD.Print($"            => EXTRAPOLADO: acordar os {corposTotais} custaria "
				   + $"{economiaNoMundoCheio:0.0} us/tique -- {economiaNoMundoCheio / orcamentoDoTique * 100:0.00}% "
				   + $"do orcamento de {orcamentoDoTique:0} us. E o que o congelamento economiza.");
			GD.Print($"            => e o 'grande intervalo' (1 pensamento a cada 5 s) economizaria "
				   + $"{economiaNoMundoCheio * 149 / 150:0.0} us: a diferenca entre DESLIGADA e RARA e "
				   + $"{economiaNoMundoCheio / 150:0.00} us/tique.");

			Checa("PRECONDICAO: o par do ganho e um par de verdade -- com todo mundo dormindo o "
				+ "servidor nao tomou UMA decisao, e com a zona mais cheia acordada tomou uma por "
				+ "corpo por tique",
				  decisoesDormindo == 0 && acordados > 0
				  && decisoesAcordado == (long)acordados * voltasDoGanho,
				  $"{decisoesDormindo} x {decisoesAcordado}");

			Checa("o congelamento paga o proprio codigo: com o mundo cheio, dormir custa menos do que "
				+ "acordar uma zona",
				  usTudoDormindo < usComUmaZonaAcordada,
				  $"{usTudoDormindo:0.00} x {usComUmaZonaAcordada:0.00} us");

			MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
		}
		catch (Exception ex)
		{
			// ABORTAR NO MEIO NAO PODE PARECER SUCESSO -- a licao da `--povoteste`.
			falhou++;
			GD.PrintErr($"  FALHA a bancada morreu no meio: {ex}");
		}
		finally
		{
			foreach (ServerPlayer c in forjados)
			{
				// O `Peer` EMPRESTADO SAI ANTES. O boneco forjado da familia 8 carrega o `Peer` do
				// host de proposito, e `RemoverNpc` se recusa a tirar do mundo um corpo com dono na
				// tela (*"corpo de jogador nao se remove, se solta"*) -- sem esta linha o forjado
				// ficaria no `_players` pra sempre, contaminando toda medida que viesse depois.
				c.Peer = null;
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			}

			MoveToZone(pl.Id, zonaGuardada, posGuardada);
			_vazioDesde.Clear();
			_embaralhosDaZona.Clear();

			GD.Print($"===== FIM: {ok} OK, {falhou} FALHA =====\n");
		}
	}

	/// <summary>
	/// ============================ O TIQUE DE UM MUNDO CONGELADO -- E ELE E VAZIO DE PROPOSITO ============================
	/// O defeito que a familia 9 injeta e "pendurar os relogios do MUNDO no mesmo `_zonasComGente` da
	/// mente". Congelar por zona vazia e, literalmente, **nao chamar nada** -- e por isso o corpo
	/// deste metodo e o corpo do defeito, escrito por extenso em vez de deixado como um laco vazio que
	/// o proximo leitor apagaria por parecer engano.
	///
	/// Ele existe pra as nove linhas daquela familia terem como ficar VERMELHAS: cada uma delas mede
	/// uma diferenca (o dia virou, a lealdade caiu, a fornada venceu), e a diferenca produzida por
	/// isto aqui e zero em todas. Sem esta comparacao, "o mundo andou" seria uma frase sem contraste.
	/// ================================================================================================================
	/// </summary>
	private void TickDoTempoQueNaoPassa() { }
}
