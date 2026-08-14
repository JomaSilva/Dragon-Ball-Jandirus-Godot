using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DE "QUEM E GENTE" (`--genteteste`) ============================
/// Ela nasceu de uma queixa literal do dono -- *"npcs de saga estao spawnando mesmo sem jogador
/// atingir o nivel minimo de bp ... se algum npc passa do minimo ele ATIVA A SAGA"* -- e mede o
/// conserto: o crivo unico do <see cref="Gente"/>, ligado em cada varredura que TOMA UMA DECISAO.
///
/// ============================ POR QUE UMA BANCADA INTEIRA PRA UM `if` ============================
/// Porque o defeito nao era um `if`: era uma PERGUNTA que o port passou a ter e nao fazia em lugar
/// nenhum. Enquanto quase todo corpo do mundo era um jogador, "varre todo mundo" e "varre os
/// jogadores" davam o mesmo numero -- e o defeito ficou invisivel por meses. Deixou de dar quando
/// chegaram, no mesmo mes, o povoamento por planeta, os proprios CHEFES DE SAGA (os corpos mais
/// fortes do mundo), o clone da mente, a fera possuida e o boneco do corpo largado.
///
/// Uma bancada que so afirmasse "nenhuma saga acordou" seria indistinguivel de uma bancada com o
/// laco MORTO -- e "nada aconteceu" e exatamente o que se ve nos dois casos. Por isso **toda familia
/// aqui tem as duas metades**: a linha que tem que ficar VERDE e a linha de CONTROLE que tem que
/// acontecer. E onde da, o defeito e INJETADO (ver <see cref="Mutacao"/>): mede, estraga, mede de
/// novo, conserta, mede de novo.
/// ==========================================================================================
///
/// ============================ AS OITO FAMILIAS, E COMO CADA UMA REPROVA ============================
///   1. **O PREDICADO** (`Core/Npc/Gente.cs`, sem mundo) -> reprova se alguem trocar a CONJUNCAO por
///      um marcador so. Cada perna tem a sua linha: `Peer` sozinho conta o boneco, `Papel` sozinho
///      conta o clone E o boneco, e o boneco so cai pela terceira (`DonoDoCorpoLargado`).
///   2. **O MARCO DE BP** (a queixa) -> reprova se um corpo sem dono de 2 bilhoes acender saga com o
///      jogador em 1 BP. Defeito injetado: o cidadao passa a responder "sou jogador" -- que e,
///      literalmente, a resposta que o crivo antigo (`Peer != null`) dava por ele. Controle: o
///      jogador em 20.000 ACENDE. Mais o BP BASE x EXPRESSO (o `M.BP` do DM, `BossEvents.dm:375`).
///   3. **A CADEIA** -> reprova se, com o elo 1 FECHADO e um corpo sem dono de 3,5 bilhoes vivo, o elo
///      seguinte acordar. E a implicacao mais feia do relato: um NPC forte descarregaria a cadeia
///      inteira. Controle: o portao estava mesmo aberto (o jogador no marco 2 acende).
///   4. **O JOGADOR POSSUIDO** (Oozaru / furia lendaria) -> reprova se o crivo virar um filtro cego
///      por "tem Cerebro". O corpo esta com a IA; a pessoa nao sumiu, e ela conta.
///   5. **CLONE E BONECO** -> reprovam se entrarem na saga, na MEDIA que pina o BP dos chefes, na
///      lotacao de planeta ou na lista de gente do servidor. Os dois nascem do caminho de PRODUCAO
///      (`EntrarNaMente`), e nao a mao.
///   6. **O ULTIMATO** -> reprova se um cidadao revidando marcar a saga como "engajada" e encurtar o
///      prazo de destruicao de Vegeta de 18 pra 6 minutos. Controle: o jogador batendo marca.
///   7. **A CACA** (`AI nao ataca AI`, `NPCAI.dm:439`) -> reprova se um chefe cacar o vilarejo. O
///      defeito e injetado LITERALMENTE aqui: o mesmo corpo, com `soJogadores: false`, acha o
///      cidadao. Mais a excecao do Cell (que NAO pode ter morrido junto) e o congelamento anti-lag.
///   8. **OS OUTROS CONSUMIDORES** -> DNA colhido de cidadao nocauteado, NPC subindo de nivel
///      sozinho, e o `TopBP` (que escala o ganho de TODO mundo) pinado por um chefe.
/// ==========================================================================================
///
///     Godot --headless --path . --host --rede 7994 --genteteste --raca Saiyan
///                      --conta bancada_gente --nome QuemEGente
///
/// Ela dispara sagas de verdade, poe chefes e cidadaos no mundo, entra na mente, possui o corpo do
/// jogador, ergue um laboratorio e ESCREVE no `sagas.json` e no `mundo.json`. Tudo o que ela toca e
/// fotografado no comeco e devolvido no `finally`. So com a flag, e em conta e porta proprias.
/// ==================================================================================================
/// </summary>
public partial class GameServer
{
	private bool _genteDeTeste;

	/// <summary>Faixa de lugares propria -- longe da dos habitantes (1..N) e da das sagas (5 M).</summary>
	private ulong _lugarDaBancadaDeGente = 8_100_000;

	private void RodarBancadaDeQuemEGente(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA: QUEM E GENTE PRA REGRA DE MUNDO =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// O `sagas.json` deste servidor ja chegou contaminado uma vez (uma bancada morta antes do
		// `finally`, ou um build anterior a guarda, e o latch `Disparou` nao se desfaz sozinho).
		// Fotografar e devolver e o que impede esta bancada de ser a proxima a contaminar.
		// ==============================================================================
		EstadoDasSagas guardado = _sagas;
		double bpGuardado = pl.Ficha.BP, expressoGuardado = pl.Ficha.expressedBP;
		bool medGuardado = pl.Ficha.med;
		string racaGuardada = pl.Race;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		double ceuGuardado = _adiantoDoCeu, relogioGuardado = _relogioDoMundo;
		double topGuardado = GainKnobs.TopBP;
		var forjados = new List<ServerPlayer>();
		Obra? labForjado = null;

		try
		{
			if (_cadeia.Length == 0 || _moldes == null)
			{
				Checa("a cadeia de sagas carregou do npcs.json (sem ela nao ha marco a medir)", false);
				return;
			}

			// ============================ A BANCADA ESTA SOZINHA? ============================
			// Metade das medidas e um numero exato ("a media do servidor e 1.000", "a lista de gente
			// conta 1"). Com uma segunda pessoa logada elas mudam, e mudariam por um motivo que nao
			// e o defeito. Melhor dizer isso na primeira linha do que explicar depois.
			Checa("PRECONDICAO: so o host tem dono na tela (as contas exatas abaixo dependem disso)",
				  _players.Values.Count(p => p.Peer != null) == 1,
				  $"{_players.Values.Count(p => p.Peer != null)} corpos com `Peer`");
			Checa("PRECONDICAO: o host E gente pelo crivo (senao a bancada inteira mediria um fantasma)",
				  EhJogador(pl));

			MedirOPredicado(Checa);
			ServerPlayer cidadao = MedirOMarco(Checa, pl, forjados);
			MedirACadeia(Checa, pl, cidadao, forjados);
			MedirOPossuido(Checa, pl, forjados);
			MedirCloneEBoneco(Checa, pl);
			MedirOUltimato(Checa, pl, forjados);
			MedirACaca(Checa, pl, forjados, zonaGuardada, posGuardada);
			labForjado = MedirOsOutrosConsumidores(Checa, pl, forjados);

			Checa("a bancada chegou ao fim (ver o `catch`: sem ele, abortar no meio reportava '0 falhas')",
				  true);
		}
		catch (Exception ex) { Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString()); }
		finally
		{
			// ============================ A ORDEM DA DEVOLUCAO IMPORTA ============================
			// O `Peer` emprestado sai ANTES da limpeza por dois motivos: um corpo forjado com `Peer`
			// sobrevivente seria um NPC que o servidor passa a tratar como jogador depois da bancada,
			// e o `RemoverNpc` RECUSA corpo com dono na tela (`GameServer.Npc.cs:267`) -- o robo
			// ficaria no mundo pra sempre, invisivel pra limpeza.
			// ================================================================================
			foreach (ServerPlayer p in _players.Values.ToList())
				if (p != pl && p.Peer != null) p.Peer = null;

			if (SemAsRedeas(pl)) DevolverAsRedeas(pl);
			if (NaMente(pl)) SairDaMente(pl, "a bancada terminou.");

			foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
			foreach (ServerPlayer c in forjados)
			{
				c.Peer = null;
				c.DonoDoCorpoLargado = 0;
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			}

			// O LABORATORIO SAI DO DISCO. `ColherDna` chama `GravarMundo` quando colhe -- entao a obra
			// forjada JA foi pro `mundo.json` do dono, e tirar da lista sem regravar a deixaria la.
			if (labForjado != null) { _noChao.Remove(labForjado); GravarMundo(); }

			pl.Ficha.BP = bpGuardado;
			pl.Ficha.expressedBP = expressoGuardado;
			pl.Ficha.med = medGuardado;
			pl.Race = racaGuardada;
			if (pl.Zone.Hash != zonaGuardada.Hash) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			pl.Pos = posGuardada;
			_adiantoDoCeu = ceuGuardado;
			_relogioDoMundo = relogioGuardado;
			GainKnobs.TopBP = topGuardado;

			_sagas = guardado;
			_sagasPorRetomar = false;
			SalvarSagas();

			GD.Print($"===== FIM: {ok} ok, {falhou} falha(s) =====\n");
			Avisar(pl, $"bancada de 'quem e gente': {ok} ok, {falhou} falha(s) -- veja o console.");
		}
	}

	// =====================================================================
	// 1. O PREDICADO -- a regra pura, sem mundo nenhum
	// =====================================================================
	/// <summary>
	/// ============================ AS TRES PERNAS, UMA LINHA CADA ============================
	/// A conjuncao existe porque nenhum marcador sozinho responde, e cada linha aqui e o corpo que a
	/// perna correspondente deixaria passar. Quem trocar a regra por um marcador so vai ver
	/// exatamente QUAL corpo voltou a contar -- e nao "o predicado mudou".
	///
	/// **`Cerebro` nao entra**: a funcao nem recebe esse marcador, e isso e o desenho. Ver a familia 4.
	/// ==================================================================================
	/// </summary>
	private void MedirOPredicado(Checagem Checa)
	{
		GD.Print("--- 1. O PREDICADO (Core/Npc/Gente.cs) ---");

		MoldeDeNpc? molde = _moldes?.Get("cidadao");
		Checa("PRECONDICAO: o molde 'cidadao' existe (e dele que sai o `Papel` de teste)", molde != null);
		if (molde == null) return;
		var papel = new PapelDeNpc(molde, 1);

		Checa("JOGADOR (dono na tela, sem papel, nao e boneco de ninguem) e gente",
			  Gente.EhJogador(true, null, 0));
		Checa("NPC com o `Peer` EMPRESTADO nao vira gente -- a 2a perna (`Papel`) recusa sozinha",
			  !Gente.EhJogador(true, papel, 0));
		Checa("BONECO do corpo largado (dono na tela, SEM papel) nao e gente -- so a 3a perna o pega",
			  !Gente.EhJogador(true, null, 7));
		Checa("CLONE da mente e corpo de bancada (sem dono na tela) nao sao gente -- a 1a perna",
			  !Gente.EhJogador(false, null, 0));

		Checa("NPC DO MUNDO e NPC do mundo (a lotacao de planeta e a reposicao contam ele)",
			  Gente.EhNpcDoMundo(false, papel));
		Checa("...e nao ha corpo que seja os DOIS: com `Peer` emprestado ele deixa de ser NPC do mundo",
			  !Gente.EhNpcDoMundo(true, papel) && !Gente.EhJogador(true, papel, 0));
		Checa("CLONE e BONECO nao sao jogador NEM NPC do mundo -- o terceiro grupo, e por isso sao "
			+ "DUAS funcoes e nao uma com `!`",
			  !Gente.EhJogador(false, null, 0) && !Gente.EhNpcDoMundo(false, null)
			  && !Gente.EhJogador(true, null, 9) && !Gente.EhNpcDoMundo(true, null));
	}

	// =====================================================================
	// 2. O MARCO DE BP -- a queixa do dono, medida no laco de producao
	// =====================================================================
	/// <summary>
	/// ============================ *"SE ALGUM NPC PASSA DO MINIMO ELE ATIVA A SAGA"* ============================
	/// A frase do dono virada em medida. O corpo errado especifico e um CIDADAO de 2 bilhoes em
	/// Vegeta -- Saiyajin, porque o marco 1 tem portao de raca (`bev_is_saiyan`) e recusar por raca
	/// mediria outra coisa.
	///
	/// O defeito injetado nao e "um `if` invertido": e o cidadao passando a RESPONDER `sou jogador`,
	/// que e literalmente o que o crivo antigo (`Peer != null` escrito a mao em cada chamador)
	/// respondia por ele. Se a linha de cima nao souber ficar vermelha com isso, ela e decoracao.
	/// ======================================================================================================
	/// </summary>
	/// <returns>O cidadao gigante -- a familia 3 o reaproveita como "alguem que o chefe traz junto".</returns>
	private ServerPlayer MedirOMarco(Checagem Checa, ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("--- 2. O MARCO DE BP (a queixa literal) ---");

		foreach (EloDaSaga e in _cadeia)
			GD.Print($"    elo '{e.Id}': marco {e.GatilhoBp:N0} BP"
				   + (e.GatilhoRaca.Length > 0 ? $" (so {e.GatilhoRaca})" : ""));

		Zerar();
		pl.Race = "Saiyan";
		pl.Ficha.BP = 1;
		pl.Ficha.expressedBP = 1;

		var vegeta = ZoneKey.Premade("Vegeta");
		ServerPlayer? cidadao = NascerNpc("cidadao", vegeta,
			PontoDeHabitante(vegeta, ++_lugarDaBancadaDeGente), _lugarDaBancadaDeGente);
		Checa("PRECONDICAO: nasceu um cidadao em Vegeta pra servir de cobaia", cidadao != null);
		if (cidadao == null) throw new InvalidOperationException("sem cobaia -- o povoamento nao nasce");
		forjados.Add(cidadao);

		cidadao.Race = "Saiyan";
		cidadao.Ficha.Race = "Saiyan";
		cidadao.Ficha.BP = 2_000_000_000;
		cidadao.Ficha.Tick(agoraMs: NowMs());
		PapelDeNpc papelDele = cidadao.Papel!;

		Checa($"PRECONDICAO: o corpo sem dono tem {cidadao.Ficha.BP:N0} BP -- acima dos QUATRO marcos",
			  cidadao.Ficha.BP >= _cadeia[^1].GatilhoBp);

		// O CRITERIO, como funcao nomeada e re-executavel: ele ZERA o estado das sagas antes de medir,
		// senao a segunda chamada leria o latch `Disparou` da primeira e todo mundo passaria.
		bool NenhumaSagaAcorda()
		{
			Zerar();
			TickDasSagas();
			TickDasSagas();
			return _sagas.Elos.All(e => !e.Disparou);
		}

		Mutacao(Checa,
			"um corpo SEM DONO de 2.000.000.000 BP nao acende saga nenhuma, com o jogador em 1 BP",
			"o cidadao passa a responder 'sou jogador' -- a resposta que o crivo antigo dava por ele",
			NenhumaSagaAcorda,
			() => { cidadao.Peer = pl.Peer; cidadao.Papel = null; },
			() => { cidadao.Peer = null; cidadao.Papel = papelDele; });

		// AS PERNAS, UMA A UMA. A mutacao acima ja provou que sem a 2a e a 3a o marco DISPARA; estas
		// duas linhas dizem qual delas basta sozinha.
		cidadao.Peer = pl.Peer;
		Checa("...com o `Peer` EMPRESTADO e o `Papel` intacto ele continua sem acender (a 2a perna sozinha)",
			  NenhumaSagaAcorda());
		cidadao.Peer = null;

		cidadao.Peer = pl.Peer;
		cidadao.Papel = null;
		cidadao.DonoDoCorpoLargado = pl.Id;
		Checa("...e disfarcado de BONECO (dono na tela, sem papel) tambem nao (a 3a perna sozinha)",
			  NenhumaSagaAcorda());
		cidadao.Peer = null;
		cidadao.Papel = papelDele;
		cidadao.DonoDoCorpoLargado = 0;

		// ============================ O CONTROLE, SEM O QUAL TUDO ACIMA E VACUO ============================
		// "Nenhuma saga acordou" e tambem o que se ve com o laco morto, com a cadeia nao carregada ou
		// com o dia que nunca vira. O corpo de 2 bilhoes CONTINUA no mundo aqui: o que mudou foi QUEM.
		// ==========================================================================================
		pl.Ficha.BP = 20_000;
		bool acendeu = !NenhumaSagaAcorda();
		Checa("CONTROLE: o JOGADOR em 20.000 BP ACENDE a saga 1 -- com o mesmo corpo de 2 B no mundo",
			  acendeu && Elo("freeza_vegeta").Disparou);

		// O BP BASE, E NAO O EXPRESSO -- o `M.BP` do DM (`BossEvents.dm:375`).
		pl.Ficha.BP = 19_999;
		pl.Ficha.expressedBP = 1_000_000_000;
		Checa("transformar-se NAO dispara marco que o BP BASE nao alcanca (base 19.999, expresso 1 B)",
			  NenhumaSagaAcorda());
		pl.Ficha.BP = 20_000;
		Checa("...e UM BP a mais no BASE dispara -- o corte e no numero, e nao 'nada dispara nunca'",
			  !NenhumaSagaAcorda());

		pl.Ficha.BP = 1;
		pl.Ficha.expressedBP = 1;
		return cidadao;
	}

	// =====================================================================
	// 3. A CADEIA -- o pior caso do relato
	// =====================================================================
	/// <summary>
	/// ============================ *"O CHEFE QUE NASCEU DISPARA O MARCO SEGUINTE"* ============================
	/// A implicacao mais feia do relato, e ela e testavel: se o chefe conta na varredura, o chefe que
	/// nasce dispara o marco seguinte, que nasce um chefe maior, que dispara o proximo -- a cadeia
	/// inteira num servidor vazio.
	///
	/// A medida e feita no pior caso de proposito: o elo 1 e aberto por um JOGADOR (como tem que ser),
	/// o chefe chega, o jogador cai pra 1 BP, e o mundo fica com um corpo sem dono de 3,5 bilhoes --
	/// acima de TODOS os quatro marcos. Entao o elo 1 FECHA, que e o instante exato em que o portao
	/// sequencial se abre e a antiga cascata dispararia.
	/// ======================================================================================================
	/// </summary>
	private void MedirACadeia(Checagem Checa, ServerPlayer pl, ServerPlayer cidadao,
							  List<ServerPlayer> forjados)
	{
		GD.Print("--- 3. A CADEIA (ela nao se descarrega sozinha) ---");

		Zerar();
		pl.Ficha.BP = 20_000;
		TickDasSagas();

		EstadoDoElo elo1 = Elo(_cadeia[0].Id);
		Checa("PRECONDICAO: o jogador em 20.000 BP abriu o elo 1", elo1.Disparou);
		if (!elo1.Disparou || elo1.Chefes.Count == 0) return;

		EstadoDoChefe ec1 = elo1.Chefes[0];
		for (int d = 0; d <= 8 && ec1.Fase == FaseDoChefe.ACaminho; d++) AvancarUmDia();
		ServerPlayer? chefe = Corpo(ec1);
		Checa("PRECONDICAO: o chefe do elo 1 chegou ao mundo", chefe != null);
		if (chefe == null) return;

		// O JOGADOR SAI DE CENA e o chefe TRAZ ALGUEM JUNTO: o cidadao gigante da familia 2 muda de
		// planeta pra ficar do lado dele. E o *"nem por ninguem que ele traga junto"* do pedido -- um
		// habitante com Zenkai acumulado, um sobrevivente de invasao, o que for.
		pl.Ficha.BP = 1;
		cidadao.Ficha.BP = 3_500_000_000;
		cidadao.Ficha.Tick(agoraMs: NowMs());
		MoverCorpoDeZona(cidadao, chefe.Zone, chefe.Pos + new Vec2(96, 0));

		Checa($"PRECONDICAO: ha corpo SEM DONO de {cidadao.Ficha.BP:N0} vivo no mundo, acima dos 4 marcos "
			+ "(senao a linha seguinte seria vacua)",
			  _players.Values.Any(p => !EhJogador(p) && !p.Ficha.dead
									&& p.Ficha.BP >= _cadeia[^1].GatilhoBp));
		Checa($"o chefe no mundo tem {chefe.Ficha.BP:N0} BP -- e o elo 2 (marco {_cadeia[1].GatilhoBp:N0}) "
			+ "continua adormecido enquanto o elo 1 CORRE (o portao sequencial)",
			  !Elo(_cadeia[1].Id).Disparou);

		// ---- e agora o elo 1 FECHA, com os dois robos vivos ----
		// O corpo sumindo e tratado como derrota (`if(!boss1) s1_state = 3`, BossEvents.dm:554).
		RemoverNpc(chefe);
		TickDasSagas();
		TickDasSagas();
		TickDasSagas();
		Checa("PRECONDICAO: o elo 1 FECHOU (o portao sequencial esta aberto agora)",
			  Elo(_cadeia[0].Id).Fase == FaseDaSaga.Vencida, $"{Elo(_cadeia[0].Id).Fase}");
		Checa("o elo fechado NAO descarrega a cadeia: 3,5 B sem dono vivo, jogador em 1 BP, "
			+ "e NENHUM elo seguinte acorda",
			  _sagas.Elos.Skip(1).All(e => !e.Disparou),
			  string.Join(",", _sagas.Elos.Where(e => e.Disparou).Select(e => e.Id)));

		pl.Ficha.BP = _cadeia[1].GatilhoBp;
		TickDasSagas();
		Checa($"CONTROLE: o portao estava mesmo aberto -- o JOGADOR em {_cadeia[1].GatilhoBp:N0} BP "
			+ "acende o elo 2 no mesmo mundo",
			  Elo(_cadeia[1].Id).Disparou);
		pl.Ficha.BP = 1;

		// ============================ E O DEFEITO INJETADO, NO PORTAO ABERTO ============================
		// A cadeia de verdade acima custa 8 viradas de dia; ela nao da pra repetir tres vezes dentro de
		// uma <see cref="Mutacao"/>. O portao aberto A MAO produz o MESMO estado -- elo 1 vencido, os
		// outros adormecidos -- e ai a mutacao cabe: o gigante responde "sou jogador" e a cascata
		// aparece inteira, que e o que o dono descreveu.
		// ==========================================================================================
		PapelDeNpc papelDele = cidadao.Papel!;
		bool CadeiaNaoSeDescarrega()
		{
			Zerar();
			EstadoDoElo e = Elo(_cadeia[0].Id);
			e.Disparou = true;
			e.Fase = FaseDaSaga.Vencida;
			TickDasSagas();
			TickDasSagas();
			return _sagas.Elos.Skip(1).All(x => !x.Disparou);
		}

		Mutacao(Checa,
			"com o portao aberto a mao, o corpo sem dono de 3,5 B nao acorda elo nenhum",
			"o gigante responde 'sou jogador' -- e a cascata do relato aparece",
			CadeiaNaoSeDescarrega,
			() => { cidadao.Peer = pl.Peer; cidadao.Papel = null; },
			() => { cidadao.Peer = null; cidadao.Papel = papelDele; });
	}

	// =====================================================================
	// 4. O JOGADOR POSSUIDO -- a linha que impede o filtro cego
	// =====================================================================
	/// <summary>
	/// ============================ O CORPO ESTA COM A FERA; A PESSOA NAO SUMIU ============================
	/// Esta e a familia que impede o conserto de virar um `if (p.Cerebro != null) continue`. Um jogador
	/// em Oozaru sem controle ou em furia lendaria TEM cerebro (`SemAsRedeas`) e continua sendo
	/// jogador: ele conta pro marco, pra media, pro "servidor vazio" e pra lotacao.
	///
	/// E ela mede tambem o OUTRO lado, que e o unico lugar do port onde "seja quem for" e a regra: o
	/// corpo possuido caca QUALQUER coisa (`soJogadores: false`, *"o corpo ataca TUDO que ve (player OU
	/// NPC)"*, `lssjbuff.dm:563`), enquanto o NPC hostil so caca gente (`NPCAI.dm:439`). Os dois
	/// chamadores da MESMA funcao sao opostos de proposito, e as duas linhas aqui provam isso no
	/// mesmo corpo, no mesmo instante.
	/// ==============================================================================================
	/// </summary>
	private void MedirOPossuido(Checagem Checa, ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("--- 4. O JOGADOR POSSUIDO (Oozaru / furia) ---");

		Checa("PRECONDICAO: o corpo do jogador esta livre antes", !SemAsRedeas(pl));

		ServerPlayer? vizinho = NascerNpc("cidadao", pl.Zone, pl.Pos + new Vec2(64, 0),
										  ++_lugarDaBancadaDeGente);
		Checa("PRECONDICAO: ha um cidadao a 2 tiles do jogador", vizinho != null);
		if (vizinho == null) return;
		forjados.Add(vizinho);

		// O CAMINHO DE PRODUCAO: e o mesmo `TomarAsRedeas` que o Oozaru selvagem chama
		// (`GameServer.Oozaru.cs:501`), e nao um `pl.Cerebro = new(...)` escrito aqui.
		TomarAsRedeas(pl);
		Checa("PRECONDICAO: o corpo do jogador esta POSSUIDO (a IA o dirige)",
			  SemAsRedeas(pl) && pl.Cerebro != null);

		Checa("o jogador POSSUIDO continua sendo GENTE -- o crivo nao e 'tem Cerebro'", EhJogador(pl));

		pl.Ficha.BP = 20_000;
		Zerar();
		TickDasSagas();
		Checa("...e ele ACENDE a saga igual: quem esta na tela nao deixou de existir porque perdeu as redeas",
			  Elo(_cadeia[0].Id).Disparou);
		pl.Ficha.BP = 1;

		Checa("o corpo POSSUIDO caca QUALQUER corpo (`soJogadores: false`): a presa e o CIDADAO",
			  PresaDaFera(pl, soJogadores: false) == vizinho,
			  $"presa: {PresaDaFera(pl, soJogadores: false)?.Name ?? "ninguem"}");
		Checa("...e o MESMO corpo com o crivo do NPC hostil ligado nao acharia presa nenhuma -- "
			+ "os dois chamadores sao opostos de proposito",
			  PresaDaFera(pl, soJogadores: true) == null);

		DevolverAsRedeas(pl);
		Checa("PRECONDICAO: as redeas voltaram ao dono", !SemAsRedeas(pl));

		RemoverNpc(vizinho);
		forjados.Remove(vizinho);
	}

	// =====================================================================
	// 5. CLONE E BONECO -- nascidos pelo caminho de producao
	// =====================================================================
	/// <summary>
	/// ============================ OS DOIS CORPOS QUE SAO O DONO ============================
	/// O reflexo da mente nasce com o **BP EXPRESSO** do dono (`EspelharODono`), e o boneco do corpo
	/// largado **COMPARTILHA a instancia da ficha** dele. Contar qualquer um dos dois e contar a mesma
	/// pessoa duas vezes -- e num deles com o numero errado, numa escala que nao e a do BP base.
	///
	/// Os dois sao criados pelo `EntrarNaMente` de producao, e nao a mao: um boneco montado pela
	/// bancada teria exatamente os campos que a bancada lembrasse de preencher.
	/// ==================================================================================
	/// </summary>
	private void MedirCloneEBoneco(Checagem Checa, ServerPlayer pl)
	{
		GD.Print("--- 5. CLONE E BONECO ---");

		pl.Ficha.BP = 1_000;
		pl.Ficha.med = true;

		int antesNaZona = CorposSemDonoNaZona(pl.Zone);
		int antesNoServidor = CorposSemDonoNoServidor();
		ZoneKey zonaDoCorpo = pl.Zone;

		EntrarNaMente(pl);

		ServerPlayer? boneco = pl.BonecoLargado;
		ServerPlayer? clone = _players.GetValueOrDefault(pl.CloneId);
		Checa("PRECONDICAO: o transe deixou o BONECO no chao e ergueu o REFLEXO na mente",
			  boneco != null && clone != null);
		if (boneco == null || clone == null) return;

		// ============================ O REFLEXO E ANCORADO NO EXPRESSO, E ISSO E O PONTO ============================
		// `EspelharODono` escreve `reflexo.BP = dono.expressedBP` -- ou seja, o BP BASE deste corpo nao
		// e o BP base de ninguem: e o EXPRESSO de outra pessoa, noutra escala. Quem entrasse na mente
		// em SSJ3 empurraria com ele o marco das sagas, a media que pina os chefes e o topo do
		// servidor, tudo em 20x o proprio poder.
		//
		// A afirmacao e sobre a ANCORAGEM (o numero do nascimento); o valor grande vem logo depois, a
		// mao, porque o que se mede aqui e "a varredura conta este corpo?" e nao "o espelho copia
		// certo" -- isso e da `--menteteste`. Sem o numero grande a familia seria vacua: um reflexo de
		// 1.110 BP nao acenderia saga nenhuma nem se fosse contado.
		// ======================================================================================================
		// O EXPRESSO DO DONO E LIDO **DEPOIS** DA ENTRADA, e isto ja custou uma linha vermelha: escrever
		// `expressedBP` a mao nao adianta, porque o primeiro `PowerLevel()` que passar recalcula o campo
		// a partir do BP base -- e o mirror le o numero recalculado, e nao o meu.
		Checa($"PRECONDICAO: o reflexo nasceu ancorado no BP EXPRESSO do dono ({clone.Ficha.BP:N0}), "
			+ "e o pino `BpDaMente` guarda o mesmo numero",
			  Math.Abs(clone.Ficha.BP - Math.Max(pl.Ficha.expressedBP, 1)) < 1
			  && Math.Abs(clone.BpDaMente - clone.Ficha.BP) < 1,
			  $"expresso do dono: {pl.Ficha.expressedBP:N0} | pino: {clone.BpDaMente:N0}");

		clone.Ficha.BP = 2_000_000_000;
		clone.Ficha.Tick(agoraMs: NowMs());
		Checa("PRECONDICAO: e o reflexo esta acima dos quatro marcos (senao nada abaixo seria vacuo)",
			  clone.Ficha.BP >= _cadeia[^1].GatilhoBp);

		Checa("o REFLEXO da mente nao e gente (conta-lo e contar o dono duas vezes, com a escala errada)",
			  !EhJogador(clone));
		Checa("o BONECO do corpo largado nao e gente (ele COMPARTILHA a ficha do dono)",
			  !EhJogador(boneco));
		Checa("...e nenhum dos dois e NPC DO MUNDO -- eles sao o terceiro grupo",
			  !EhNpcDoMundo(clone) && !EhNpcDoMundo(boneco));

		// O REFLEXO E O UNICO CORPO DO JOGO QUE SO A **PRIMEIRA** PERNA SEGURA: ele nao tem papel e nao
		// e boneco de ninguem. Dar um `Peer` a ele e, aqui, o defeito inteiro.
		bool ReflexoNaoAcende()
		{
			Zerar();
			TickDasSagas();
			TickDasSagas();
			return _sagas.Elos.All(e => !e.Disparou);
		}

		Mutacao(Checa,
			"o reflexo de 2 B nao acende saga nenhuma, com o dono em 1.000 BP",
			"o reflexo ganha um `Peer` -- e ele e o unico corpo que so a 1a perna segura",
			ReflexoNaoAcende,
			() => clone.Peer = pl.Peer,
			() => clone.Peer = null);

		// A MEDIA E O QUE PINA O BP DE TODO CHEFE no disparo do marco (`AverageBP`, NPClist.dm:72).
		// Incluir o reflexo aqui daria realimentacao: quem entrasse na mente em SSJ3 pinaria os chefes
		// do servidor em 20x o proprio poder.
		Checa($"a MEDIA que pina o BP dos chefes ve 1.000 e nao o reflexo de 2 B",
			  Math.Abs(MediaDoServidor() - 1_000) < 0.001, $"{MediaDoServidor():N2}");

		Checa("a lotacao da zona do corpo largado nao mudou por causa do boneco",
			  CorposSemDonoNaZona(zonaDoCorpo) == antesNaZona,
			  $"{antesNaZona} -> {CorposSemDonoNaZona(zonaDoCorpo)}");
		Checa("...e a do servidor nao mudou por causa do reflexo (senao o teto recusaria habitante "
			+ "por causa de quem esta MEDITANDO)",
			  CorposSemDonoNoServidor() == antesNoServidor,
			  $"{antesNoServidor} -> {CorposSemDonoNoServidor()}");
		Checa("a lotacao da MENTE conta zero, com dois corpos na lista da zona de la",
			  CorposSemDonoNaZona(clone.Zone) == 0 && ZoneList(clone.Zone.Hash).Count >= 2);

		Checa($"a lista de gente do servidor conta 1, com {_players.Count} corpos no mundo "
			+ "(e ela e a base dos ~20 lacos de admin, limpeza e conquista)",
			  Jogadores.Count() == 1, $"{Jogadores.Count()}");

		SairDaMente(pl, "a bancada terminou o transe.");
		pl.Ficha.med = false;
		Checa("PRECONDICAO: o transe acabou e o boneco saiu do mundo",
			  pl.BonecoLargado == null && !_players.ContainsKey(clone.Id));

		pl.Ficha.BP = 1;
		pl.Ficha.expressedBP = 1;
	}

	// =====================================================================
	// 6. O ULTIMATO -- o preco disto era o planeta
	// =====================================================================
	/// <summary>
	/// ============================ *"EXPLODIU SOZINHO"* COM OUTRO NOME ============================
	/// `MarcarAgressao` aceita QUALQUER agressor, e `MonitorarChefe` lia isso pra decidir se a saga
	/// esta "engajada" -- e engajada troca o prazo do ultimato de destruicao do planeta: pra
	/// `freeza_vegeta`, 6 minutos em vez de 18.
	///
	/// A sequencia, sem jogador nenhum: Freeza pousa em Vegeta (40 cidadaos), bate num deles, o
	/// cidadao revida, `Engajou` sobe -- e o primeiro jogador que logar cai numa janela de 6 minutos
	/// que ele nao abriu. Se ele nao chegar a tempo, Vegeta explode.
	/// ======================================================================================
	/// </summary>
	private void MedirOUltimato(Checagem Checa, ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("--- 6. O ULTIMATO (engajar e o prazo do planeta) ---");

		EloDaSaga elo = _cadeia[0];
		Checa("PRECONDICAO: o elo 1 e o que DESTROI o planeta (senao nao ha ultimato a medir)",
			  elo.DestroiPlaneta);

		Zerar();
		pl.Ficha.BP = 20_000;
		TickDasSagas();
		EstadoDoElo est = Elo(elo.Id);
		if (!est.Disparou || est.Chefes.Count == 0) { Checa("PRECONDICAO: o elo 1 abriu", false); return; }

		EstadoDoChefe ec = est.Chefes[0];
		for (int d = 0; d <= 8 && ec.Fase == FaseDoChefe.ACaminho; d++) AvancarUmDia();
		ServerPlayer? freeza = Corpo(ec);
		Checa("PRECONDICAO: o chefe esta em Vegeta", freeza != null);
		if (freeza == null) return;
		pl.Ficha.BP = 1;

		ServerPlayer? povo = NascerNpc("cidadao", freeza.Zone, freeza.Pos + new Vec2(64, 0),
									   ++_lugarDaBancadaDeGente);
		Checa("PRECONDICAO: ha um cidadao ao lado do chefe", povo != null);
		if (povo == null) return;
		forjados.Add(povo);

		// O CIDADAO REVIDA -- pelo funil unico de agressao (`MarcarAgressao`), que e por onde o soco,
		// a rajada e a aparada escrevem.
		MarcarAgressao(freeza, povo);
		TickDasSagas();
		Checa("um CIDADAO batendo no chefe NAO marca a saga como engajada", !ec.Engajou);

		double prazoOcio = ec.UltimatoEm - _relogioDoMundo;
		Checa($"...e o ultimato do planeta segue no prazo de OCIO ({elo.MinutosDeOcio:0} min) e nao no "
			+ $"de LUTA ({elo.MinutosDeLuta:0} min)",
			  prazoOcio > elo.MinutosDeLuta * 60 + 1, $"faltam {prazoOcio / 60:0.#} min");

		// ============================ O CONTROLE: O JOGADOR BATE ============================
		// Sem esta metade, "nao engajou" seria tambem o que se ve com o `Engajou` morto -- e ai a saga
		// nunca teria prazo de luta e o ultimato inteiro estaria quebrado no sentido oposto.
		MarcarAgressao(freeza, pl);
		TickDasSagas();
		Checa("CONTROLE: o JOGADOR batendo MARCA a saga como engajada", ec.Engajou);
		Checa($"...e ai sim o prazo cai pro de LUTA ({elo.MinutosDeLuta:0} min)",
			  ec.UltimatoEm - _relogioDoMundo <= elo.MinutosDeLuta * 60 + 1,
			  $"faltam {(ec.UltimatoEm - _relogioDoMundo) / 60:0.#} min");

		// ============================ SERVIDOR SO COM NPC: O PRAZO ADIA ============================
		// **LITERAL** (`BossEvents.dm:562, :592`): sem ninguem online pra reagir, o planeta explodiria
		// sozinho de madrugada. Os 40 cidadaos de Vegeta nao sao "alguem pra reagir" -- e essa e a
		// linha que os tira da conta. O `Peer` sai e volta no mesmo instante, sem tique no meio.
		LiteNetLib.NetPeer? peerGuardado = pl.Peer;
		double antes = ec.UltimatoEm, depois;
		try
		{
			pl.Peer = null;
			_relogioDoMundo += 5;
			TickDasSagas();
			depois = ec.UltimatoEm;
		}
		finally { pl.Peer = peerGuardado; }

		Checa("com o mundo so de NPC, o ultimato ADIA em vez de vencer (o planeta nao explode sozinho)",
			  depois > antes, $"{antes:0} -> {depois:0}");
		Checa("PRECONDICAO: o `Peer` do jogador voltou (senao tudo abaixo mediria um fantasma)",
			  EhJogador(pl));

		RemoverNpc(freeza);
		Zerar();
	}

	// =====================================================================
	// 7. A CACA -- `AI nao ataca AI`
	// =====================================================================
	/// <summary>
	/// ============================ O CHEFE CACA GENTE, E NAO O VILAREJO ============================
	/// Medido pelo funil de producao (`PresaDoNpc`), porque o que interessa e o CHAMADOR passar
	/// `soJogadores: true` -- e nao a funcao de baixo saber filtrar.
	///
	/// O defeito e injetado LITERALMENTE aqui, e e a unica familia em que isso da: o mesmo corpo, no
	/// mesmo instante, com o crivo desligado (`soJogadores: false`) acha o cidadao. Sem ele, os dois
	/// androides do elo do Cell -- que nascem no MESMO tique (`diasMin/Max = 0`) numa Earth de 40
	/// cidadaos -- cacavam um ao outro: 17 nocauteava 18, o Cell absorvia o caido e chegava a forma
	/// Perfeita com zero input de jogador.
	///
	/// E a EXCECAO DO CELL tem linha propria, porque um conserto que a matasse quebraria a saga no
	/// outro sentido: o alvo imposto pelo roteiro (`bev_prey`, BossEvents.dm:264) e respondido ANTES
	/// do crivo, e por isso ele continua alcancando o androide NOCAUTEADO.
	/// ======================================================================================
	/// </summary>
	private void MedirACaca(Checagem Checa, ServerPlayer pl, List<ServerPlayer> forjados,
							ZoneKey zonaDoJogador, Vec2 posDoJogador)
	{
		GD.Print("--- 7. A CACA (AI nao ataca AI) ---");

		// ============================ O JOGADOR SAI DO PLANETA PRIMEIRO ============================
		// E a primeira coisa que esta familia faz, e ela custou duas linhas vermelhas pra ficar
		// escrita: **um host Saiyajin nasce em VEGETA**, que e exatamente o planeta do elo 1. Com ele
		// parado la, o chefe achava presa (o proprio jogador) e a zona contava como habitada -- as
		// duas medidas mediam a bancada, e nao o crivo.
		// ======================================================================================
		var longe = ZoneKey.Premade("Namek");
		MoveToZone(pl.Id, longe, PontoDeHabitante(longe, ++_lugarDaBancadaDeGente));
		Checa("PRECONDICAO: o jogador esta NOUTRO planeta (um host Saiyajin nasce no planeta do elo 1)",
			  pl.Zone.Hash == longe.Hash);

		var vegeta = ZoneKey.Premade("Vegeta");
		ServerPlayer? chefe = NascerNpc(_cadeia[0].Chefes[0].Molde, vegeta,
			PontoDeHabitante(vegeta, ++_lugarDaBancadaDeGente), _lugarDaBancadaDeGente);
		Checa("PRECONDICAO: o chefe nasceu em Vegeta", chefe != null);
		if (chefe == null) return;
		forjados.Add(chefe);

		ServerPlayer? aldeao = NascerNpc("cidadao", chefe.Zone, chefe.Pos + new Vec2(64, 0),
										++_lugarDaBancadaDeGente);
		Checa("PRECONDICAO: ha um cidadao de pe a 2 tiles do chefe, na mesma zona",
			  aldeao != null && aldeao.Zone.Hash == chefe.Zone.Hash
			  && !aldeao.Ficha.KO && !aldeao.Ficha.dead);
		if (aldeao == null) return;
		forjados.Add(aldeao);

		Checa("o chefe com o vilarejo em volta e NENHUM jogador na zona nao tem presa (`NPCAI.dm:439`)",
			  PresaDoNpc(chefe) == null, $"presa: {PresaDoNpc(chefe)?.Name}");
		Checa("   DEFEITO INJETADO (o MESMO corpo com o crivo desligado -- `soJogadores: false`): "
			+ "a presa passa a ser o CIDADAO",
			  PresaDaFera(chefe, soJogadores: false) == aldeao,
			  "a linha de cima e decoracao -- ela nao sabe ficar vermelha");

		// ---- o congelamento anti-lag, que so engata quando nao ha presa ----
		// *"ANTI-LAG: planeta sem players = congela (nem anda)"* (PlanetPopulation.dm:129). Com 40
		// cidadaos de pe e o crivo desligado, `PresaDaFera` NUNCA devolvia nulo pra um chefe -- e o
		// congelamento nunca engatava, servidor vazio ou nao.
		TickDosCorposSemDono(0.05);
		bool semGente = !_zonasComGente.Contains(chefe.Zone.Hash);

		// ============================ E O CIDADAO NAO REVIDA EM ROBO ============================
		// A outra metade do mesmo defeito, e a que custava o planeta: o chefe encosta num cidadao, o
		// cidadao revida NELE, e esse revide marcava a saga como engajada (ver a familia 6). O
		// `provoke()` do DM exige `atk.client` nos DOIS ramos (`PlanetPopulation.dm:113-118`).
		// ==================================================================================
		MarcarAgressao(aldeao, chefe);
		Checa("o cidadao agredido por um CHEFE nao revida nele (o `atk.client` do `provoke()`)",
			  PresaDoNpc(aldeao) == null, $"presa: {PresaDoNpc(aldeao)?.Name}");

		// ---- e agora o jogador pousa, 4x mais LONGE que o cidadao ----
		MoveToZone(pl.Id, chefe.Zone, chefe.Pos + new Vec2(900, 0));
		ServerPlayer? presaComGente = PresaDoNpc(chefe);

		MarcarAgressao(aldeao, pl);
		bool revidaEmGente = PresaDoNpc(aldeao) == pl;

		TickDosCorposSemDono(0.05);
		bool comGente = _zonasComGente.Contains(chefe.Zone.Hash);
		MoveToZone(pl.Id, longe, PontoDeHabitante(longe, ++_lugarDaBancadaDeGente));

		Checa("um planeta com corpos sem dono e NENHUM jogador nao conta como habitado (o anti-lag engata)",
			  semGente);
		Checa("CONTROLE: o jogador pousa a 900 px (4x mais longe que o cidadao) e vira a presa na hora",
			  presaComGente == pl, $"presa: {presaComGente?.Name ?? "ninguem"}");
		Checa("CONTROLE: e o cidadao REVIDA quando quem bateu foi gente (senao o vilarejo seria de pedra)",
			  revidaEmGente);
		Checa("CONTROLE: ...e a zona conta como habitada no mesmo instante (senao nada descongelaria)",
			  comGente);

		// ---- os dois androides do elo do Cell, o corpo errado especifico ----
		var earth = ZoneKey.Premade("Earth");
		ServerPlayer? a17 = NascerNpc("androide_17", earth,
			PontoDeHabitante(earth, ++_lugarDaBancadaDeGente), _lugarDaBancadaDeGente);
		ServerPlayer? a18 = a17 == null ? null
			: NascerNpc("androide_18", a17.Zone, a17.Pos + new Vec2(64, 0), ++_lugarDaBancadaDeGente);
		Checa("PRECONDICAO: os dois androides do elo do Cell nasceram colados (como no `npcs.json`, "
			+ "onde os dois tem diasMin/Max = 0)", a17 != null && a18 != null);
		if (a17 == null || a18 == null) return;
		forjados.Add(a17);
		forjados.Add(a18);

		Checa("os dois androides, sozinhos numa Earth sem jogador, nao tem presa NENHUMA -- e era isto "
			+ "que resolvia a saga do Cell sozinha (17 nocauteia 18, o Cell absorve o caido)",
			  PresaDoNpc(a17) == null && PresaDoNpc(a18) == null,
			  $"{PresaDoNpc(a17)?.Name ?? "ninguem"} / {PresaDoNpc(a18)?.Name ?? "ninguem"}");

		// ---- a excecao do roteiro: o Cell TEM que alcancar o androide caido ----
		ServerPlayer? cell = NascerNpc("cell", a18.Zone, a18.Pos + new Vec2(64, 0),
									   ++_lugarDaBancadaDeGente);
		Checa("PRECONDICAO: o Cell nasceu ao lado deles", cell != null);
		if (cell == null) return;
		forjados.Add(cell);

		a18.Ficha.KO = true;
		cell.Papel!.PresaDoRoteiro = a18.Id;
		Checa("...e o CELL continua alcancando o androide NOCAUTEADO: o `bev_prey` e respondido ANTES "
			+ "do crivo, entao o conserto nao matou a absorcao",
			  PresaDoNpc(cell) == a18, $"presa: {PresaDoNpc(cell)?.Name ?? "ninguem"}");
		a18.Ficha.KO = false;
		cell.Papel.PresaDoRoteiro = 0;

		MoveToZone(pl.Id, zonaDoJogador, posDoJogador);
	}

	// =====================================================================
	// 8. OS OUTROS CONSUMIDORES
	// =====================================================================
	/// <summary>
	/// ============================ OS TRES QUE NAO SAO SAGA ============================
	/// Cada um deles e um achado da varredura, e cada um tem o corpo errado especifico:
	///
	///   * **`ColherDna`** -- o cientista escapava de lutar. Cidadao e gratis e infinito (a
	///     `Manutencao` repoe a populacao a cada 5 min) e o filtro aceita `KO`, nao exige matar;
	///   * **`TickDosNiveis`** -- NPC TEM `Livro` e `Niveis` (`GameServer.Npc.cs:191-192`), entao todo
	///     corpo sem dono acumulava exp pra sempre. No chefe isso furava o pino da saga (o
	///     `TickDoRoteiro` reancora o BP, nao os buffs de nivel); no cidadao, nada reancorava;
	///   * **`GainKnobs.TopBP`** -- "o maior BP base do servidor", que so SOBE e que entra na escala de
	///     ganho de TODO mundo. Um chefe de 1e12 fixava o topo do servidor pra sempre.
	/// ==========================================================================
	/// </summary>
	/// <returns>O laboratorio forjado, pra quem chamou tirar do disco no `finally`.</returns>
	private Obra? MedirOsOutrosConsumidores(Checagem Checa, ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("--- 8. DNA, NIVEIS E O TOPO DO SERVIDOR ---");

		// ---- 8a. COLHER DNA ----
		var lab = new Obra
		{
			Id = 990_001,
			Tipo = "Android_Creation_Mainframe",
			Aparafusada = true,
			Lab = 2,
			X = pl.Pos.X,
			Y = pl.Pos.Y,
			DonoConta = pl.Conta,
			DonoNome = pl.Name,
		};
		lab.PorZona(pl.Zone);
		_noChao.Add(lab);
		Checa("PRECONDICAO: ha um Bio-Android Lab aparafusado ao alcance do jogador", LabDeBio(pl) != null);

		ServerPlayer? caido = NascerNpc("cidadao", pl.Zone, pl.Pos + new Vec2(8, 0),
										++_lugarDaBancadaDeGente);
		Checa("PRECONDICAO: ha um cidadao NOCAUTEADO ao alcance", caido != null);
		if (caido == null) return lab;
		forjados.Add(caido);
		caido.Ficha.KO = true;
		PapelDeNpc papelDoCaido = caido.Papel!;

		bool TanqueVazio()
		{
			lab.Fornada = null;
			ColherDna(pl);
			return lab.Fornada == null || lab.Fornada.Dna.Count == 0;
		}

		Mutacao(Checa,
			"o cientista NAO colhe DNA de um cidadao nocauteado (o bio-androide continua custando "
			+ "derrubar gente)",
			"o cidadao caido responde 'sou jogador'",
			TanqueVazio,
			() => { caido.Peer = pl.Peer; caido.Papel = null; },
			() => { caido.Peer = null; caido.Papel = papelDoCaido; });

		lab.Fornada = null;
		caido.Ficha.KO = false;

		// ---- 8b. O EFETOR DE NIVEIS ----
		// A skill e DADA ao corpo sem dono de proposito: a familia tem que medir "o laco pula NPC", e
		// nao "este cidadao por acaso nao sabe nada que suba sozinho".
		Skill? sobeSozinha = _skills?.Todas.FirstOrDefault(s =>
			s.MaxNivel > 1 && RegrasDeNivel.Get(s.Path) is { } r
			&& (r.GanhoPorTempo || r.PorEstado.Any(g =>
					g.Quando is RegraDeNivel.Estado.Sempre or RegraDeNivel.Estado.Ocioso)));
		Checa("PRECONDICAO: ha skill que sobe SOZINHA no niveis.json (senao a familia seria vacua)",
			  sobeSozinha != null);

		if (sobeSozinha != null && caido.Livro != null)
		{
			caido.Livro.Dar(sobeSozinha.Path);

			string Assinatura() => string.Join(",", caido.Niveis.Todas
				.Select(t => $"{t.Path}:{t.Nivel}:{t.Exp:0.##}"));

			bool NpcNaoSobeSozinho()
			{
				caido.Niveis.Por(sobeSozinha.Path, 0, 0);
				string antes = Assinatura();
				for (int i = 0; i < 600; i++) TickDosNiveis();
				return Assinatura() == antes;
			}

			Mutacao(Checa,
				"um corpo SEM DONO nao acumula exp de skill sozinho em 600 tiques do efetor",
				"o corpo passa a responder 'sou jogador'",
				NpcNaoSobeSozinho,
				() => { caido.Peer = pl.Peer; caido.Papel = null; },
				() => { caido.Peer = null; caido.Papel = papelDoCaido; });
		}

		// ---- 8c. O TOPO DO SERVIDOR ----
		ServerPlayer? gigante = forjados.FirstOrDefault(c => c.Ficha.BP >= 1_000_000_000
														  && _players.ContainsKey(c.Id));
		Checa("PRECONDICAO: ha um corpo sem dono de 1 B+ vivo pro topo poder ser envenenado",
			  gigante != null);
		if (gigante == null) return lab;

		GainKnobs.TopBP = 1;
		pl.Ficha.BP = 1_000;
		TickFichas();
		double topo = GainKnobs.TopBP;

		Checa($"o TOPO do servidor (que escala o ganho de todo mundo) nao sobe pelo corpo sem dono de "
			+ $"{gigante.Ficha.BP:N0}",
			  topo < 1_000_000_000, $"TopBP = {topo:N0}");
		Checa("CONTROLE: e ele sobe pelo JOGADOR (senao a linha de cima passaria com o laco morto)",
			  topo >= 1_000, $"TopBP = {topo:N0}");

		return lab;
	}

	/// <summary>
	/// MUDA UM CORPO DE ZONA -- a mesma sequencia do `MoveToZone`, mas pra corpo SEM DONO (aquele
	/// manda pacote e busca por id de jogador). So a bancada usa: no jogo quem muda NPC de planeta e
	/// o nascimento ou a remocao.
	/// </summary>
	private void MoverCorpoDeZona(ServerPlayer corpo, ZoneKey destino, Vec2 pos)
	{
		ZoneList(corpo.Zone.Hash).Remove(corpo);
		corpo.Zone = destino;
		corpo.Pos = pos;
		if (!ZoneList(destino.Hash).Contains(corpo)) ZoneList(destino.Hash).Add(corpo);
	}
}
