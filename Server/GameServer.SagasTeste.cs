using System.Text.Json;
using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA CADEIA DE SAGAS (`--sagateste`).
///
/// ============================ O QUE SO DAQUI SE RESPONDE ============================
///   1. **A CADEIA E SEQUENCIAL?** Com BP de sobra pros QUATRO marcos ao mesmo tempo, so o primeiro
///      dispara -- e o segundo dispara no instante em que o primeiro fecha. Um teste que so olhasse
///      "a saga 1 aconteceu" passaria com as quatro acontecendo juntas.
///   2. **O BP E FIXADO NO DISPARO?** A media do servidor e multiplicada por mil ENTRE o anuncio e a
///      chegada, e o corpo pousa com o numero pinado. Sem a metade de controle (a media mudando), a
///      checagem passaria com o pino apagado.
///   3. **A CONTAGEM E EM DIAS IN-GAME?** Os dias sao adiantados pela MESMA manivela do ceu
///      (`_adiantoDoCeu`), e a bancada confere que o chefe **nao chega no dia anterior** -- o teto que
///      dispara, do corolario 0.7, na forma de um prazo que ainda nao venceu.
///   4. **A RECOMPENSA CHEGA?** +100 de reputacao no matador, faixa de HEROI, e o numero no DISCO --
///      um livro-caixa que so vive em memoria some no proximo reinicio e ninguem percebe.
///   5. **O GANCHO DA DESTRUICAO DISPARA E E INERTE?** As duas metades. So a primeira seria mentira
///      (o planeta nao explode); so a segunda seria codigo morto.
///   6. **O CHEFE VOLTA IGUAL DEPOIS DO REINICIO?** Mesmo degrau, mesmo BP pinado -- lido do disco.
/// ================================================================================
///
///     Godot --headless -- --server --sagateste
/// </summary>
public partial class GameServer
{
	private bool _sagaDeTeste;

	/// <summary>
	/// A assinatura da checagem, como DELEGADO nomeado e nao `Action&lt;string,bool,string&gt;`: os
	/// blocos grandes desta bancada moram em metodos proprios e recebem o `Checa` como argumento, e um
	/// `Action` obrigaria as ~40 checagens sem detalhe a escrever `""` no fim.
	/// </summary>
	private delegate void Checagem(string nome, bool cond, string detalhe = "");

	/// <summary>Roda uma vez, no primeiro login. MEXE no estado das sagas e no mundo -- so com a flag.</summary>
	private void RodarBancadaDeSagas(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DA CADEIA DE SAGAS =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// Esta bancada dispara sagas de verdade, poe chefes de verdade no mundo e ESCREVE no
		// `sagas.json` e no `reputacao.json` do servidor. Tudo o que ela mexe e fotografado aqui e
		// devolvido no `finally` -- inclusive o livro-caixa, senao quem rodar a bancada uma vez fica
		// heroi do Planeta Vegeta pra sempre.
		// ==============================================================================
		EstadoDasSagas guardado = _sagas;
		double ceuGuardado = _adiantoDoCeu, relogioGuardado = _relogioDoMundo;
		double bpGuardado = pl.Ficha.BP;
		string racaGuardada = pl.Race;
		double repVegetaGuardada = ReputacaoDe("Vegeta", pl.Conta);
		double relativoGuardado = _moldes?.Get("freeza_vegeta")?.BpRelativo ?? 0;

		// O LIVRO DOS PLANETAS MORTOS TAMBEM. A familia do gancho leva a saga ate o COMMIT, e o commit
		// escreve num arquivo que nao e o `sagas.json`: `planetas-mortos.json`. Sem esta fotografia,
		// rodar esta bancada uma vez deixaria Namek destruido pra sempre no save do dono -- e a
		// devolucao do estado das sagas nao tocaria nisso, porque a destruicao tem livro PROPRIO.
		var mortosGuardados = _mortos.Todos.ToList();

		try
		{
			if (_cadeia.Length == 0 || _moldes == null)
			{
				Checa("a cadeia de sagas carregou do npcs.json", false);
				return;
			}

			// =====================================================================
			// 1. A CADEIA E AS DECLARACOES DA FICHA
			// =====================================================================
			Checa($"a cadeia tem os quatro elos do DM ({string.Join(" -> ", _cadeia.Select(e => e.Id))})",
				  _cadeia.Length == 4);
			Checa("...e nenhum deles e contraditorio",
				  Sagas.Problemas(_cadeia, _moldes).Count == 0,
				  string.Join(" | ", Sagas.Problemas(_cadeia, _moldes)));

			foreach (EloDaSaga e in _cadeia)
				foreach (ChefeDaSaga c in e.Chefes)
				{
					MoldeDeNpc m = _moldes.Get(c.Molde)!;

					// ============================ "TEM A FORMA E ESCOLHE NAO USAR" ============================
					// O contrato do dono, afirmado sobre TODO chefe da cadeia e nao sobre um exemplo:
					// nenhum deles sobe a escada por decisao propria. Quem os move e o roteiro.
					// ==================================================================================
					Checa($"'{c.Molde}' declara que NAO ascende por decisao (o ai_no_powerup)",
						  !m.AscendePorDecisao);
					Checa($"...e tem ficha PRONTA ({m.Estagios.Length} degrau(s)), nao BP sorteado",
						  m.EhChefe && m.Tipo == TipoDeNpc.Chefe);
				}

			Checa("o Freeza de VEGETA tem UM degrau (e por isso ele nao transforma)",
				  _moldes.Get("freeza_vegeta")!.Estagios.Length == 1);
			Checa("...e o de NAMEK tem CINCO (mesma fabrica, mesmo cerebro -- muda a LISTA)",
				  _moldes.Get("freeza_namek")!.Estagios.Length == 5);

			// =====================================================================
			// 2. O PORTAO DE RACA E A ORDEM DA CADEIA
			// =====================================================================
			Zerar();
			pl.Race = "Human";
			pl.Ficha.BP = 2_000_000_000;      // acima dos QUATRO marcos ao mesmo tempo
			TickDasSagas();

			Checa("um nao-Saiyajin com BP de sobra NAO acorda a saga 1 (o bev_is_saiyan)",
				  !Elo("freeza_vegeta").Disparou);
			Checa("...e as outras tres tambem nao, mesmo com o BP delas batido -- a cadeia e SEQUENCIAL",
				  _sagas.Elos.All(e => !e.Disparou),
				  string.Join(",", _sagas.Elos.Where(e => e.Disparou).Select(e => e.Id)));

			pl.Race = "Saiyan";
			TickDasSagas();
			Checa("o mesmo BP num SAIYAJIN acorda a saga 1 (era o portao de raca, nao outra recusa)",
				  Elo("freeza_vegeta") is { Disparou: true, Fase: FaseDaSaga.Contando });
			Checa("...e a 2, a 3 e a 4 CONTINUAM adormecidas, com o BP delas de sobra",
				  _sagas.Elos.Skip(1).All(e => !e.Disparou && e.Fase == FaseDaSaga.Adormecida));

			// =====================================================================
			// 3. O BP FIXADO NO DISPARO -- e a metade de controle
			// =====================================================================
			// ============================ COM `bpRelativo`, O PINO PASSA A SER VISIVEL ============================
			// Ficha de BP constante torna "pinar" indistinguivel de "ler o arquivo". Ligando o campo
			// que o proprio molde ja tem -- poder relativo a media de quem esta online --, a diferenca
			// entre decidir no ANUNCIO e decidir na CHEGADA vira um numero, e e ele que esta em jogo
			// quando a especificacao diz *"quem chegar depois empurraria a dificuldade"*.
			// ================================================================================================
			Zerar();
			MoldeDeNpc freezaV = _moldes.Get("freeza_vegeta")!;
			freezaV.BpRelativo = 100;

			pl.Race = "Saiyan";
			pl.Ficha.BP = 20_000;             // media do servidor = 20 mil -> pino = 100 x 20 mil = 2 M
			TickDasSagas();

			EstadoDoElo elo1 = Elo("freeza_vegeta");
			EstadoDoChefe ec1 = elo1.Chefes[0];
			Checa("o marco pinou o BP na hora do ANUNCIO (100 x a media de 20 mil = 2 M)",
				  Math.Abs(ec1.Bps[0] - 2_000_000) < 1, $"{ec1.Bps.FirstOrDefault():N0}");
			Checa($"...e a contagem saiu em DIAS IN-GAME, na faixa 3..5 ({ec1.DiasRestantes})",
				  ec1.DiasRestantes is >= 3 and <= 5);

			// O SERVIDOR FICA MIL VEZES MAIS FORTE ENTRE O ANUNCIO E A CHEGADA.
			pl.Ficha.BP = 20_000_000;         // media nova: recomputar daria 2 BILHOES

			int diasAteChegar = ec1.DiasRestantes;
			for (int d = 1; d < diasAteChegar; d++)
			{
				AvancarUmDia();
				Checa($"dia {d} de {diasAteChegar}: o chefe AINDA NAO chegou (o prazo e um prazo)",
					  ec1.Fase == FaseDoChefe.ACaminho, $"{ec1.Fase}");
			}
			AvancarUmDia();
			Checa($"no dia {diasAteChegar} ele chega", ec1.Fase == FaseDoChefe.NoMundo, $"{ec1.Fase}");

			ServerPlayer? freeza = Corpo(ec1);
			Checa("...e o corpo dele esta no mundo, na zona do planeta da saga",
				  freeza != null && ZoneList(ZoneKey.Premade("Vegeta").Hash).Contains(freeza));
			Checa("**O BP E O PINADO** (2 M), e nao o recomputado com a media nova (2 B) "
				+ "nem o do arquivo (530 mil)",
				  freeza != null && Math.Abs(freeza.Ficha.BP - 2_000_000) < 1, $"{freeza?.Ficha.BP:N0}");
			Checa("...e o vetor pinado viajou do estado da saga pro PAPEL do corpo",
				  freeza?.Papel?.BpsPinados.Length == ec1.Bps.Length
				  && Math.Abs(freeza!.Papel!.BpDoDegrau(0) - ec1.Bps[0]) < 1);

			freezaV.BpRelativo = relativoGuardado;

			// =====================================================================
			// 4. A REANCORAGEM -- nada mexe no BP durante a luta
			// =====================================================================
			if (freeza != null)
			{
				// E o `NPCTicker()` do EventBoss. O empurrao aqui e grosseiro de proposito: o que
				// acontece em jogo (treino no `TickFichas`, Zenkai no nocaute) e o mesmo efeito, so que
				// devagar -- e devagar e o jeito de um defeito passar despercebido por uma luta inteira.
				freeza.Ficha.BP *= 3;
				TickDoRoteiro();
				Checa("empurrar o BP do chefe pra cima e desfeito no tique seguinte (BP anunciado = BP real)",
					  Math.Abs(freeza.Ficha.BP - 2_000_000) < 1, $"{freeza.Ficha.BP:N0}");

				// E a guarda do "tem a forma e nao a usa" continua sendo do FUNIL, num corpo que
				// nasceu por uma saga e nao pela bancada de NPC.
				//
				// ============================ A PERGUNTA E "NAO MUDOU", E NAO "ESTA NA BASE" ============================
				// Esta linha afirmava `Forma.NaBase`, e ela ficou FALSA -- e nao por causa do
				// `Transformar`: o `NascerNpc` poe todo corpo no PISO da escada dele
				// (`Catalogo.IdDoPiso`, `GameServer.Npc.cs:242`), e o piso do Frost Demon e a forma
				// base DELE (`frost5`), nao o `IdBase`. Sem isso o roteiro deste mesmo chefe nao
				// conseguiria subir pra 1a Evolucao, porque o degrau anterior dela exige estar nela.
				// Ou seja: a bancada dizia "ele se transformou" sobre um corpo que nasceu assim, e a
				// falha nao apontava pra defeito nenhum.
				//
				// O que a guarda promete e que **apertar C nao move o chefe**, entao e isso que se
				// mede: a forma de antes contra a de depois.
				// ==================================================================================================
				string formaAntes = freeza.Forma.Atual;
				for (int i = 0; i < 3; i++) Transformar(freeza, subir: true);
				Checa("apertar C num chefe de saga nao o transforma (a guarda esta no `Transformar`)",
					  freeza.Forma.Atual == formaAntes, $"{formaAntes} -> {freeza.Forma.Atual}");
			}

			// =====================================================================
			// 5. A RECOMPENSA: +100 DE REPUTACAO DE HEROI, E ELA VAI PRO DISCO
			// =====================================================================
			EscutaDasSagas = [];
			double repAntes = ReputacaoDe("Vegeta", pl.Conta);
			if (freeza != null)
			{
				// QUEM MATOU E O `UltimoAgressor`, o funil unico de agressao deste port.
				freeza.UltimoAgressor = pl.Id;
				freeza.RancorAte = NowMs() + 90_000;
				freeza.Ficha.dead = true;
			}
			TickDasSagas();

			Checa("o chefe morto e contado como derrotado", ec1.Fase == FaseDoChefe.Derrotado, $"{ec1.Fase}");
			Checa($"o matador ganhou +{Reputacao.HeroiPorChefe:0} de reputacao com o povo de Vegeta",
				  Math.Abs(ReputacaoDe("Vegeta", pl.Conta) - (repAntes + 100)) < 0.001,
				  $"{ReputacaoDe("Vegeta", pl.Conta):0}");
			Checa("...e a faixa dele agora e HEROI (o mesmo limiar 50 que quatro cargos ja pedem)",
				  Reputacao.Faixa(ReputacaoDe("Vegeta", pl.Conta)) == "HERÓI do povo");
			Checa("...e o mundo ouviu a aclamacao",
				  EscutaDasSagas!.Any(t => t.Contains("HERÓI")),
				  string.Join(" | ", EscutaDasSagas!));
			Checa("a saga 1 fechou como VENCIDA", elo1.Fase == FaseDaSaga.Vencida, $"{elo1.Fase}");

			// O DISCO, e nao a memoria: reputacao que so vive em RAM some no proximo reinicio.
			Checa("o livro-caixa esta no disco com o numero certo",
				  LerReputacaoDoDisco("Vegeta", pl.Conta) is { } v && Math.Abs(v - (repAntes + 100)) < 0.001,
				  $"{LerReputacaoDoDisco("Vegeta", pl.Conta)}");

			// =====================================================================
			// 6. A CADEIA DESTRAVA -- o marco que esperava dispara agora
			// =====================================================================
			Checa("a saga 2 NAO tinha disparado enquanto a 1 corria (o BP dela ja estava batido ha muito)",
				  !Elo("freeza_namek").Disparou);
			TickDasSagas();
			Checa("...e ela dispara no primeiro tique depois de a 1 fechar -- o marco esperou a vez",
				  Elo("freeza_namek") is { Disparou: true, Fase: FaseDaSaga.Contando });

			// =====================================================================
			// 7. O CHEFE VOLTA DO REINICIO NO DEGRAU SALVO
			// =====================================================================
			EstadoDoElo elo2 = Elo("freeza_namek");
			EstadoDoChefe ec2 = elo2.Chefes[0];
			// O NUMERO DE VOLTAS E LIDO ANTES DO LACO. `DiasRestantes` DESCE a cada volta, entao
			// `d < ec2.DiasRestantes` se encontraria no meio do caminho e o laco pararia com tres dias
			// pendentes -- foi exatamente o que aconteceu na primeira rodada desta bancada, e as tres
			// checagens seguintes cairam em cascata por causa dele.
			int diasAteNamek = ec2.DiasRestantes;
			for (int d = 0; d <= diasAteNamek && ec2.Fase == FaseDoChefe.ACaminho; d++) AvancarUmDia();
			ServerPlayer? namek = Corpo(ec2);
			Checa("o Freeza de Namek chegou depois dos 7 dias in-game", namek != null, $"{ec2.Fase}");

			if (namek != null)
			{
				// SOBE A FICHA POR DANO, pelo caminho de producao (o roteiro le o pior membro).
				for (int passo = 0; passo < 2; passo++)
				{
					PorPiorMembroEm(namek, namek.Papel!.GatilhoAtual - 0.01);
					TickDoRoteiro();
				}
				Checa("o de Namek TRANSFORMA (a lista dele tem cinco) -- degrau 3",
					  namek.Papel!.Estagio == 2, $"degrau {namek.Papel!.Estagio + 1}");

				TickDasSagas();                 // copia o degrau pro estado da saga
				int degrauSalvo = ec2.Degrau;
				double bpSalvo = namek.Ficha.BP;
				SalvarSagas();

				// ---- o "reinicio": o mundo esquece tudo e le do disco ----
				foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
				_sagas = new EstadoDasSagas();
				CarregarSagas();                // le sagas.json e realinha a cadeia
				TickDasSagas();                 // e o RetomarSagas do primeiro tique

				EstadoDoChefe ec2b = Elo("freeza_namek").Chefes[0];
				ServerPlayer? voltou = Corpo(ec2b);
				Checa("depois do 'reinicio' o chefe VOLTOU ao mundo", voltou != null);
				Checa($"...no MESMO degrau salvo ({degrauSalvo + 1})",
					  voltou?.Papel?.Estagio == degrauSalvo, $"{voltou?.Papel?.Estagio}");
				Checa("...e com o MESMO BP pinado (lido do disco, nao do npcs.json)",
					  voltou != null && Math.Abs(voltou.Ficha.BP - bpSalvo) < 1,
					  $"{voltou?.Ficha.BP:N0} != {bpSalvo:N0}");
				Checa("...e a saga 1, que ja tinha sido vencida, continua vencida depois da leitura",
					  Elo("freeza_vegeta").Fase == FaseDaSaga.Vencida);
			}

			// =====================================================================
			// 8. O GANCHO DA DESTRUICAO -- ele DISPARA, e e INERTE
			// =====================================================================
			MedirOGancho(Checa);

			// =====================================================================
			// 9. A ABSORCAO -- a ficha do Cell movida por um evento, e nao por dano
			// =====================================================================
			MedirAAbsorcao(Checa);

			Checa("a bancada chegou ao fim (ver o `catch`: sem ele, abortar no meio reportava '0 falhas')",
				  true);
		}
		catch (Exception ex) { Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString()); }
		finally
		{
			EscutaDasSagas = null;
			foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);

			if (_moldes?.Get("freeza_vegeta") is { } fv) fv.BpRelativo = relativoGuardado;
			pl.Ficha.BP = bpGuardado;
			pl.Race = racaGuardada;
			_adiantoDoCeu = ceuGuardado;
			_relogioDoMundo = relogioGuardado;

			// O LIVRO-CAIXA VOLTA AO QUE ERA. `SomarReputacao` e o funil de producao e nao tem
			// "desfazer" -- e nao deve ter --, entao a devolucao e feita com o delta ao contrario,
			// pelo mesmo funil, e nao escrevendo no dicionario por baixo.
			double excedente = ReputacaoDe("Vegeta", pl.Conta) - repVegetaGuardada;
			if (Math.Abs(excedente) > 0.001) SomarReputacao("Vegeta", pl, -excedente, "fim-da-bancada");

			_sagas = guardado;
			_sagasPorRetomar = false;
			SalvarSagas();

			// E O LIVRO DOS MORTOS TAMBEM -- ele e OUTRO arquivo, e a linha acima nao o alcança. A
			// familia do gancho leva a saga ao commit, e sem isto Namek ficaria destruido no save.
			_mortos.Limpar();
			foreach (EstadoDaMorte e in mortosGuardados) _mortos.Por(e);
			_proximoTremor.Clear();
			SalvarPlanetasMortos();
			MandarMortosPraTodos();

			GD.Print($"===== FIM: {ok} ok, {falhou} falha(s) =====\n");
			if (falhou > 0) GD.PushError($"[server] bancada de sagas: {falhou} falha(s)");
			Avisar(pl, $"bancada de sagas: {ok} ok, {falhou} falha(s) -- veja o console.");
		}
	}

	// =====================================================================
	// O GANCHO -- E ELE MORDE
	// =====================================================================
	/// <summary>
	/// ============================ AS DUAS METADES DO GANCHO ============================
	/// **Que ele dispara**: o prazo vence, a carga de 30 s comeca, o nocaute a INTERROMPE, e o
	/// ultimato acaba condenando o planeta e fechando o elo como consumado.
	///
	/// **E que ele destroi de verdade**: o planeta entra na fase de explosao, o ceu de la vira o da
	/// Destruicao e, cinco minutos depois, o commit mata os habitantes e poe o planeta na lista. Ate a
	/// destruicao existir, a segunda metade dizia o CONTRARIO ("inerte: a zona continua no manifesto,
	/// nenhum habitante morreu") -- e o cabecalho deste arquivo prometia que ela inverteria. Inverteu.
	///
	/// ============================ E O COMMIT SO E EXERCITADO AQUI COM O ALGOZ NULO ============================
	/// O `GanchoDeDestruicao` **remove o corpo do chefe** ao consumar. Cinco minutos de tique depois, o
	/// commit vai procurar o algoz pelo `IdDoAlgoz` e nao vai achar ninguem. E o unico caminho do jogo
	/// em que isso e garantido -- pelo verbo, o algoz costuma estar vivo e online --, e por isso a
	/// familia leva a saga ate o fim em vez de parar em "Explodindo".
	/// ====================================================================================================
	/// </summary>
	private void MedirOGancho(Checagem Checa)
	{
		// O ELO QUE DESTROI **E** TEM CHEFE NO MUNDO. Procurar so pelo primeiro que destroi pegava a
		// saga 1, ja vencida e sem corpo nenhum -- e a checagem falhava por nao ter o que medir.
		EloDaSaga? elo = Array.Find(_cadeia, e => e.DestroiPlaneta
			&& Elo(e.Id).Chefes.Exists(c => c.Fase == FaseDoChefe.NoMundo));
		EstadoDoElo? est = elo == null ? null : Elo(elo.Id);
		EstadoDoChefe? ec = est?.Chefes.Find(c => c.Fase == FaseDoChefe.NoMundo);
		ServerPlayer? corpo = ec == null ? null : Corpo(ec);

		if (elo == null || est == null || ec == null || corpo?.Papel == null)
		{
			Checa("ha um chefe de um elo que destroi planeta no mundo pra medir o ultimato", false);
			return;
		}

		// SO NO ULTIMO DEGRAU (a saga 2). Sobe a ficha ate o fim pelo caminho de producao.
		for (int i = 0; i < 8 && corpo.Papel.GatilhoAtual >= 0; i++)
		{
			PorPiorMembroEm(corpo, corpo.Papel.GatilhoAtual - 0.01);
			TickDoRoteiro();
		}
		Checa("o ultimato de Namek so entra em cena com o Freeza no ULTIMO degrau da ficha",
			  corpo.Papel.Proximo == null, $"degrau {corpo.Papel.Estagio + 1}");

		EscutaDasSagas = [];
		int habitantesAntes = _players.Values.Count(p => p.Peer == null && p.Papel is { EhChefe: false });

		// ============================ O PRAZO SO NASCE QUANDO ELE CHEGA NO ULTIMO DEGRAU ============================
		// E o `s2_deadline = world.time + BEV_FREEZA2_FINAL_TIMER` escrito DENTRO do `freeza_transform`
		// (BossEvents.dm:673): os cinco minutos de Namek comecam na forma final e nao na chegada.
		//
		// Esta linha foi o que a primeira rodada desta bancada errou -- ela adiantou o relogio ANTES de
		// o prazo existir, e as cinco checagens seguintes cairam. Vale a pena guardar: o relogio andar
		// antes de o prazo ser armado nao adianta prazo nenhum, e isso e verdade no jogo tambem.
		// ======================================================================================================
		TickDasSagas();
		Checa("...e e SO ENTAO que o prazo de 5 min e armado (nao na chegada dele em Namek)",
			  ec.UltimatoEm > _relogioDoMundo, $"{ec.UltimatoEm:0} vs {_relogioDoMundo:0}");

		// O RELOGIO DO MUNDO E ADIANTADO -- e so ele. Mesma manivela que o `_adiantoDoCeu` e pelo mesmo
		// motivo: a regra medida e o PRAZO, e esperar cinco minutos reais nao e uma bancada.
		_relogioDoMundo += elo.MinutosDeLuta * 60 + elo.MinutosDeOcio * 60 + 1;
		TickDasSagas();
		Checa("vencido o prazo, ele comeca a CARGA de 30 s (a janela pra interromper)",
			  ec.Carregando, "");

		// NOCAUTE NA JANELA INTERROMPE -- e o significado que o KO tem no fim de uma luta de chefe.
		corpo.Combate.Nocautear(60);
		_relogioDoMundo += 31;
		TickDasSagas();
		Checa("nocautea-lo dentro da janela INTERROMPE o ultimato",
			  !ec.Carregando && !est.Condenado && EscutaDasSagas!.Any(t => t.Contains("INTERROMPIDO")),
			  string.Join(" | ", EscutaDasSagas!));

		// e ele tenta de novo -- 2 min depois (`BEV_PD_RETRY`)
		corpo.Ficha.KO = false;
		_relogioDoMundo += 121;
		TickDasSagas();          // re-arma a carga
		_relogioDoMundo += 31;
		TickDasSagas();          // e desta vez ela vai ate o fim

		Checa("sem interrupcao, o gancho de destruicao DISPARA", est.Condenado);
		Checa("...e o elo fecha como CONSUMADA (o vilao venceu)",
			  est.Fase == FaseDaSaga.Consumada, $"{est.Fase}");

		// ============================ ESTAS DUAS AFIRMAVAM O CONTRARIO, E ISSO E O PONTO ============================
		// Ate a destruicao de planeta existir, elas diziam *"INERTE: a zona continua no manifesto"* e
		// *"INERTE: nenhum habitante morreu ou sumiu"* -- e o cabecalho deste arquivo prometia que
		// **elas inverteriam** no dia em que o gancho fosse ligado. Inverteram.
		//
		// E o teste de que o gancho e o gancho MESMO: nao ha um segundo caminho de destruicao. O que
		// a saga acende e o `ComecarDestruicao` de producao, o mesmo do verb e o mesmo do admin.
		// ======================================================================================================
		Checa("**A SAGA DESTROI O PLANETA DE VERDADE**: ele esta explodindo agora",
			  MorteDaZona(ZoneKey.Premade(elo.Planeta))?.Fase == FaseDaMorte.Explodindo,
			  $"{MorteDaZona(ZoneKey.Premade(elo.Planeta))?.Fase}");

		Checa("...com o BP EXPRESSO do chefe fixado como o do algoz (o `mexpressedBP`)",
			  MorteDaZona(ZoneKey.Premade(elo.Planeta))?.BpDoAlgoz > 0);

		Checa("...e o ceu de la virou o da DESTRUICAO (o `currentWeather = \"Destruction\"`)",
			  ClimaAgora(ZoneKey.Premade(elo.Planeta)).Tipo == TipoDeClima.Destruicao);

		// A ZONA CONTINUA NO MANIFESTO -- e isso NAO e inercia. O manifesto e a lista de MAPAS
		// convertidos, que e dado de disco e nao muda; quem sabe que o planeta morreu e o registro
		// de mortos. Confundir os dois faria "destruir" querer dizer "apagar um arquivo".
		Checa("(a zona continua no manifesto de mapas -- morrer nao apaga o .col do disco)",
			  _catalogo?.Get(ZoneKey.Premade(elo.Planeta)) != null);

		// E OS HABITANTES SO MORREM NO COMMIT, cinco minutos depois -- nao no instante do ultimato.
		Checa("...e os habitantes ainda estao vivos: o commit e daqui a 5 min, nao agora",
			  _players.Values.Count(p => p.Peer == null && p.Papel is { EhChefe: false }) == habitantesAntes);

		// ============================ E A SAGA VAI ATE O FIM, PELO MESMO COMMIT ============================
		// Ate aqui esta bancada parava em "Explodindo" -- ou seja, ela provava que a saga ACENDE o
		// pavio, e ninguem nunca tinha visto uma saga chegar ao COMMIT. A diferenca nao e academica: o
		// commit resolve o algoz pelo `IdDoAlgoz`, e o `GanchoDeDestruicao` **acabou de remover o corpo
		// do chefe do mundo**. Esse e o unico caminho do jogo em que o algoz e garantidamente nulo na
		// hora de aplicar o dano, e ele so era exercitado aqui.
		//
		// O relogio e adiantado pela funcao de PRODUCAO (`TickDaDestruicao`), um segundo por volta --
		// nao ha atalho que ponha `Fase = Destruido` na mao.
		// ============================================================================================
		var planetaDaSaga = ZoneKey.Premade(elo.Planeta);
		Checa("(montagem) o corpo do chefe ja saiu do mundo -- o algoz do commit sera NULO",
			  !_players.ContainsKey(corpo.Id));

		// ============================ O HABITANTE PRECISA EXISTIR, E ISSO CUSTOU UM DEFEITO INJETADO ============================
		// A primeira versao desta checagem contava habitantes VIVOS na zona e exigia zero. Ela era
		// **vacuamente verdadeira**: no primeiro login a fila do povoamento ainda esta cheia (ela solta
		// um corpo por tique), entao Namek esta VAZIA na hora da bancada -- e "todos morreram" ficava
		// verde sem ter ninguem pra morrer. Ela so foi desmascarada quando o commit foi quebrado de
		// proposito pra pular NPC: a bancada inteira continuou verde. E o "teto que nunca dispara" da
		// PARTE 3, dentro da propria bancada.
		//
		// Duas correcoes. A montagem GARANTE um habitante (forja um quando o planeta ainda esta vazio),
		// e a afirmacao passou a ser sobre um corpo NOMEADO e nao sobre uma contagem: morrer tira o
		// corpo da zona (o `RemoverNpc` da morte), entao contar quem ficou na lista nunca distinguiria
		// "morreu" de "nunca esteve la".
		// ================================================================================================================
		ServerPlayer? habitanteDaSaga =
			ZoneList(planetaDaSaga.Hash).FirstOrDefault(p => p.Papel is { EhChefe: false });
		habitanteDaSaga ??= ForjarHabitanteEm(planetaDaSaga, "CidadaoDaSaga", 999_999);
		Checa("(montagem) ha um habitante no planeta da saga pro commit matar",
			  habitanteDaSaga is { Ficha.dead: false },
			  habitanteDaSaga == null ? "sem molde 'cidadao' no npcs.json" : "ja estava morto");

		for (int i = 0; i < 400 && MorteDaZona(planetaDaSaga)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);

		Checa("**E A SAGA CHEGA AO COMMIT PELO MESMO CAMINHO DO VERBO**: o planeta esta DESTRUIDO",
			  MorteDaZona(planetaDaSaga)?.Fase == FaseDaMorte.Destruido,
			  $"{MorteDaZona(planetaDaSaga)?.Fase}");
		Checa("...e ele entrou na lista de mortos (a mesma `PlanetDisableList` do Planet Destroy)",
			  ZonaMorta(planetaDaSaga));
		Checa("...**mesmo com o algoz ja fora do mundo** (o dano entra sem autor, e nao explode)",
			  !_players.ContainsKey(corpo.Id));
		Checa("...e AGORA o habitante de la morreu (o `isNPC` do commit, sem checagem de BP)",
			  habitanteDaSaga is { Ficha.dead: true },
			  $"dead={habitanteDaSaga?.Ficha.dead}");

		// ============================ E A BANCADA NAO PODE DEIXAR O PLANETA MORTO ============================
		// Sem isto, o `finally` desta bancada devolveria o `sagas.json` e o `reputacao.json` -- e
		// deixaria Namek DESTRUIDO no save do dono, porque a destruicao tem livro PROPRIO. Depois do
		// commit o `AbortarMorte` ja nao serve (ele so age antes do fim): a porta e a unica volta que
		// existe, o `RessuscitarPlaneta`.
		// ==================================================================================================
		Checa("...e restaurar o planeta o tira da lista (a unica volta que existe)",
			  RessuscitarPlaneta(planetaDaSaga));

		// E A CADEIA CONTINUA: "consumada" tambem termina o elo, senao destruir um planeta travaria
		// a fila pra sempre.
		TickDasSagas();
		Checa("...e mesmo assim a saga SEGUINTE destrava (consumada tambem e 'terminou')",
			  Elo("cell").Disparou);
	}

	// =====================================================================
	// A ABSORCAO
	// =====================================================================
	private void MedirAAbsorcao(Checagem Checa)
	{
		EstadoDoElo est = Elo("cell");
		if (!est.Disparou) { Checa("a saga do Cell disparou pra medir a absorcao", false); return; }

		EstadoDoChefe e17 = est.Chefes.Find(c => c.Molde == "androide_17")!;
		EstadoDoChefe eCell = est.Chefes.Find(c => c.Molde == "cell")!;

		Checa("os androides chegaram NO MESMO DIA do anuncio (diasMin 0)",
			  e17.Fase == FaseDoChefe.NoMundo, $"{e17.Fase}");
		Checa("...e o Cell ainda esta a caminho (dois dias depois -- a onda e por chefe)",
			  eCell.Fase == FaseDoChefe.ACaminho, $"{eCell.Fase}");

		for (int d = 0; d < 4 && eCell.Fase == FaseDoChefe.ACaminho; d++) AvancarUmDia();

		ServerPlayer? cell = Corpo(eCell);
		ServerPlayer? a17 = Corpo(e17);
		Checa("o Cell chegou dois dias in-game depois", cell != null, $"{eCell.Fase}");
		Checa("...no primeiro degrau da ficha dele (150 M)",
			  cell != null && cell.Papel!.Estagio == 0 && Math.Abs(cell.Ficha.BP - 150_000_000) < 1,
			  $"{cell?.Ficha.BP:N0}");

		if (cell == null || a17 == null) return;

		// ---- o androide cai, e o Cell larga tudo pra ir atras dele ----
		a17.Combate.Nocautear(120);
		cell.Pos = new Vec2(a17.Pos.X + 600, a17.Pos.Y);     // longe: nada de contato ainda
		TickDasSagas();

		Checa("com um androide NOCAUTEADO, o roteiro impoe o alvo ao Cell (o bev_prey)",
			  cell.Papel!.PresaDoRoteiro == a17.Id, $"{cell.Papel!.PresaDoRoteiro}");
		Checa("...e a caca NORMAL nunca daria esse alvo (ela pula quem esta no chao)",
			  PresaDaFera(cell, soJogadores: true)?.Id != a17.Id);
		Checa("...e o alvo imposto e o que o funil de presa devolve",
			  PresaDoNpc(cell)?.Id == a17.Id);
		Checa("ainda longe, ele NAO absorveu (o contato e um contato)",
			  cell.Papel!.Estagio == 0 && _players.ContainsKey(a17.Id));

		// ---- e agora encostado ----
		cell.Pos = a17.Pos;
		EscutaDasSagas = [];
		TickDasSagas();

		Checa("encostando no androide caido, o Cell o ABSORVE e ele sai do mundo",
			  !_players.ContainsKey(a17.Id) && e17.Fase == FaseDoChefe.Consumido, $"{e17.Fase}");
		Checa("...e o Cell sobe um degrau da PROPRIA ficha (o mesmo AvancarEstagio do dano)",
			  cell.Papel!.Estagio == 1 && Math.Abs(cell.Ficha.BP - 500_000_000) < 1,
			  $"degrau {cell.Papel!.Estagio + 1} BP {cell.Ficha.BP:N0}");
		Checa("...e o mundo ouviu",
			  EscutaDasSagas!.Any(t => t.Contains("ABSORVEU")), string.Join(" | ", EscutaDasSagas!));
		Checa("...e o alvo imposto foi solto depois (senao ele ficaria preso num corpo que nao existe)",
			  cell.Papel!.PresaDoRoteiro == 0);

		// ---- morrer no degrau 3 nao e morrer ----
		EstadoDoChefe e18 = est.Chefes.Find(c => c.Molde == "androide_18")!;
		if (Corpo(e18) is { } a18)
		{
			a18.Combate.Nocautear(120);
			cell.Pos = a18.Pos;
			TickDasSagas();
		}
		Checa("absorvendo a segunda, ele chega ao degrau 3 (a forma Perfeita, 3,5 B)",
			  cell.Papel!.Estagio == 2 && Math.Abs(cell.Ficha.BP - 3_500_000_000) < 1,
			  $"degrau {cell.Papel!.Estagio + 1} BP {cell.Ficha.BP:N0}");

		EscutaDasSagas = [];
		cell.UltimoAgressor = 0;                 // sem matador: a recompensa nao e o que se mede aqui
		cell.Ficha.dead = true;
		TickDasSagas();

		Checa("morto no degrau 3, ele NAO fecha o elo -- ele esta voltando",
			  eCell.Fase == FaseDoChefe.Derrotado && eCell.RessurgeEm > 0
			  && est.Fase == FaseDaSaga.EmCurso, $"{est.Fase}");

		_relogioDoMundo += 61;
		TickDasSagas();
		ServerPlayer? voltou = Corpo(eCell);
		Checa("...e um minuto depois ele volta no degrau 4, com 5 B",
			  voltou != null && voltou.Papel!.Estagio == 3
			  && Math.Abs(voltou.Ficha.BP - 5_000_000_000) < 1,
			  $"{voltou?.Papel?.Estagio} / {voltou?.Ficha.BP:N0}");
	}

	// =====================================================================
	// FERRAMENTAS DA BANCADA
	// =====================================================================
	/// <summary>Estado novo, alinhado a cadeia -- e o mundo sem chefe nenhum.</summary>
	private void Zerar()
	{
		foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
		_sagas = new EstadoDasSagas { UltimoDiaVisto = DiaDoMundo };
		foreach (EloDaSaga e in _cadeia) _sagas.Elos.Add(new EstadoDoElo { Id = e.Id });
		_sagasPorRetomar = false;
	}

	/// <summary>
	/// UM DIA IN-GAME, pela manivela do CEU. E o ponto da checagem: a contagem da saga nao tem relogio
	/// proprio -- ela le o `DiaDoMundo`, que sai do `TempoDoMundo`, que e o mesmo relogio da lua e do
	/// ciclo dia/noite. Adiantar por aqui exercita o caminho de producao inteiro; chamar `VirouODia()`
	/// direto mediria o atalho.
	/// </summary>
	private void AvancarUmDia()
	{
		_adiantoDoCeu += Ceu.SegundosPorDia;
		TickDasSagas();
	}

	private EstadoDoElo Elo(string id) => _sagas.Do(id)!;

	private ServerPlayer? Corpo(EstadoDoChefe ec) =>
		_players.TryGetValue(ec.Corpo, out ServerPlayer? c) ? c : null;

	/// <summary>
	/// TODO CORPO COM FICHA DE CHEFE. A pergunta e `Papel` e SO `Papel`: ela era `Peer == null &&
	/// Papel is { EhChefe: true }`, e a metade do `Peer` ja derrubou uma medicao -- a bancada do
	/// `--genteteste` empresta um `Peer` ao chefe pro defeito injetado, este helper nao o achava, o elo
	/// nunca fechava e a medida saiu errada (ver `GameServer.GenteTeste.cs`). Um corpo com ficha de
	/// chefe e um chefe, tenha quem tiver emprestado o que for pra ele.
	/// </summary>
	private List<ServerPlayer> ChefesNoMundo() =>
		[.. _players.Values.Where(p => p.Papel is { EhChefe: true })];

	/// <summary>
	/// LE A REPUTACAO DIRETO DO ARQUIVO. Ler do dicionario em memoria provaria que `SomarReputacao`
	/// escreveu num dicionario -- que e exatamente o defeito que faz um livro-caixa sumir no proximo
	/// reinicio sem ninguem perceber.
	/// </summary>
	private double? LerReputacaoDoDisco(string planeta, string conta)
	{
		try
		{
			if (!System.IO.File.Exists(CaminhoDaReputacao)) return null;
			using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(CaminhoDaReputacao));
			if (!doc.RootElement.TryGetProperty("Planetas", out JsonElement ps)) return null;
			foreach (JsonProperty p in ps.EnumerateObject())
			{
				if (!string.Equals(p.Name, planeta, StringComparison.OrdinalIgnoreCase)) continue;
				foreach (JsonProperty c in p.Value.EnumerateObject())
					if (string.Equals(c.Name, conta, StringComparison.OrdinalIgnoreCase))
						return c.Value.GetDouble();
			}
		}
		catch { /* arquivo ausente ou torto: a checagem falha, que e o que se quer ver */ }
		return null;
	}
}
