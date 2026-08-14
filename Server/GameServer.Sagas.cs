using System.Text.Json;
using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A CADEIA DE SAGAS EM JOGO -- o `datum/boss_events` do original (BossEvents.dm:300), sem os
/// quatro conjuntos de estado dele.
///
/// ============================ O QUE ESTE ARQUIVO E, E O QUE ELE NAO E ============================
/// Ele e o RELOGIO e a CONTABILIDADE de um evento: o marco de BP que acorda a saga, a contagem em
/// dias in-game ate a chegada, quem esta no mundo, quem caiu, quem paga a recompensa e o que vai
/// pro disco. As quatro sagas do DM sao DADO (`npcs.json`, secao `sagas`) -- nao ha uma linha de
/// codigo com o nome "Freeza" aqui dentro.
///
/// Ele **nao** e o comportamento do chefe. Quem move o corpo e o `Cerebro`, e quem sobe a ficha
/// dele degrau a degrau e o `TickDoRoteiro` -- os dois ja existiam, e e por isso que este arquivo e
/// contabilidade e nao IA.
/// ============================================================================================
///
/// ============================ AS DUAS REGRAS QUE A ESPECIFICACAO NOMEIA ============================
///   * **O BP DO CHEFE E FIXADO QUANDO A SAGA COMECA.** `Sagas.Pinar` roda uma vez, no disparo, e o
///     vetor vai pro save. Entre o anuncio e a chegada passam de 2 a 7 dias in-game -- tempo de todo
///     mundo treinar e de tres pessoas mais fortes entrarem. Sem o pino, quem chega depois empurra a
///     dificuldade e o numero anunciado vira mentira.
///   * **A SAGA SO COMECA QUANDO A ANTERIOR TERMINOU.** `Sagas.PodeComecar`, por indice. O marco de
///     BP nao se perde: ele espera a vez e dispara no instante em que o elo anterior fecha.
/// ==============================================================================================
///
/// **FICAM DE FORA, POR DECISAO DO DONO**: o *Hell Boss Rush* e as masmorras. Nao ha nada aqui que
/// se pareca com uma raide, e "mais chefes" se resolve acrescentando ELO ao `npcs.json`.
/// </summary>
public partial class GameServer
{
	private EloDaSaga[] _cadeia = [];
	private EstadoDasSagas _sagas = new();

	/// <summary>
	/// Os chefes que estavam no mundo quando o servidor caiu ainda precisam voltar. Feito no PRIMEIRO
	/// tique e nao no boot, pelo mesmo motivo do `while(worldloading) sleep(1)` do `Boss_Events_Init`
	/// (BossEvents.dm:966): no boot as zonas ainda estao assentando, e um corpo nascido no meio disso
	/// cai na colisao errada.
	/// </summary>
	private bool _sagasPorRetomar;

	/// <summary>
	/// O CONTADOR DE LUGARES dos chefes -- o terceiro argumento da semente, como no povoamento. Comeca
	/// alto pra nao encostar na numeracao dos habitantes (que comeca em 1 por planeta) e **nao
	/// persiste**: dois Freezas do mesmo elo em reinicios diferentes serem fichas diferentes e o
	/// desejavel, e o que MANDA no poder dele -- o vetor pinado -- persiste.
	/// </summary>
	private ulong _lugarDasSagas = 5_000_000;

	/// <summary>O que a cadeia anunciou -- ligada so pela bancada. Mesmo desenho da `EscutaDoRoteiro`.</summary>
	internal static List<string>? EscutaDasSagas;

	private string CaminhoDasSagas => System.IO.Path.Combine(_store?.Pasta ?? ".", "sagas.json");

	// =====================================================================
	// O DIA IN-GAME
	// =====================================================================
	/// <summary>
	/// QUE DIA IN-GAME E HOJE. O `Days` do WorldClock do original -- mas um INDICE monotonico e nao
	/// uma data de calendario (o DM detecta a virada por `Days != last_day_seen`, o que faz o
	/// rollover 28->1 do mes contar como virada; um indice nao tem esse caso especial).
	///
	/// Sai do <see cref="TempoDoMundo"/>, que e o relogio de parede do universo -- entao ele **anda
	/// com o servidor desligado**, e a nave de Freeza que ia levar 5 dias leva 5 dias mesmo que o
	/// servidor passe a noite fora. E como a bancada do ceu ja tem o `_adiantoDoCeu`, a bancada das
	/// sagas adianta dias pela MESMA manivela, exercitando esta funcao de producao em vez de chamar
	/// uma virada de dia direto.
	/// </summary>
	public long DiaDoMundo => (long)Math.Floor(TempoDoMundo / Ceu.SegundosPorDia);

	// =====================================================================
	// CARGA
	// =====================================================================
	private void CarregarSagas()
	{
		_cadeia = _moldes?.Cadeia ?? [];
		if (_cadeia.Length == 0)
		{
			GD.Print("[server] sagas: nenhuma no npcs.json -- a cadeia de chefes esta vazia");
			return;
		}

		if (_moldes != null)
			foreach (string p in Sagas.Problemas(_cadeia, _moldes))
				GD.PushError($"[server] npcs.json (sagas): {p}");

		try
		{
			if (System.IO.File.Exists(CaminhoDasSagas))
				_sagas = JsonSerializer.Deserialize<EstadoDasSagas>(
					System.IO.File.ReadAllText(CaminhoDasSagas),
					new JsonSerializerOptions { IncludeFields = true }) ?? new EstadoDasSagas();
		}
		catch (Exception e) { GD.PushWarning($"[server] sagas.json ilegivel: {e.Message}"); }

		// ============================ O ESTADO SE ALINHA A CADEIA, E NAO O CONTRARIO ============================
		// O `npcs.json` e editado pelo dono: um elo pode ter sido acrescentado (a lacuna entre Cell e
		// Boo e o lugar apontado pela especificacao) ou renomeado desde o ultimo save. Um elo novo nasce
		// adormecido; um estado orfao (elo que sumiu do dado) e DESCARTADO com aviso, e nao mantido --
		// mante-lo faria a cadeia esperar para sempre por uma saga que ninguem mais consegue terminar.
		// ==================================================================================================
		var vivos = new List<EstadoDoElo>();
		foreach (EloDaSaga e in _cadeia)
			vivos.Add(_sagas.Do(e.Id) ?? new EstadoDoElo { Id = e.Id });

		foreach (EstadoDoElo orfao in _sagas.Elos)
			if (!Array.Exists(_cadeia, e => string.Equals(e.Id, orfao.Id, StringComparison.OrdinalIgnoreCase)))
				GD.PushWarning($"[server] sagas: o save tem o elo '{orfao.Id}', que nao existe mais no "
							 + "npcs.json -- estado descartado");

		_sagas.Elos = vivos;
		_sagasPorRetomar = true;

		GD.Print($"[server] sagas: {_cadeia.Length} elo(s) na cadeia | "
			   + string.Join(", ", _sagas.Elos.Select(e => $"{e.Id}={e.Fase}")));
	}

	private void SalvarSagas()
	{
		if (_store == null) return;
		try
		{
			string tmp = CaminhoDasSagas + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_sagas,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDasSagas, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar sagas.json: {e.Message}"); }
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O LOOP DA CADEIA, a 1 Hz (o `BEV_TICK 10` do original, que sao 10 decimos = 1 s -- literal).
	///
	/// UM ERRO AQUI NAO PODE MATAR A CADEIA CALADO. E a licao que o proprio original registra numa
	/// linha de comentario -- *"um erro de runtime NAO pode matar o loop silenciosamente (licao do
	/// freeze da IA)"* (BossEvents.dm:350) --, e o modo de falha e o pior possivel: o servidor segue
	/// rodando meses e as sagas simplesmente nunca acontecem.
	/// </summary>
	private void TickDasSagas()
	{
		if (_cadeia.Length == 0) return;

		try
		{
			if (_sagasPorRetomar) { _sagasPorRetomar = false; RetomarSagas(); }
			ContarOsDias();
			ConferirMarcos();
			MonitorarElos();
		}
		catch (Exception e) { GD.PushError($"[server] cadeia de sagas quebrou neste tique: {e}"); }
	}

	private void ContarOsDias()
	{
		long hoje = DiaDoMundo;

		// PRIMEIRA VOLTA (save novo): ancora e nao dispara nada. Sem isto o `while` abaixo tentaria
		// aplicar todas as viradas desde 1970.
		if (_sagas.UltimoDiaVisto < 0) { _sagas.UltimoDiaVisto = hoje; SalvarSagas(); return; }

		// O RELOGIO PODE RECUAR -- a bancada do ceu adianta e desfaz o `_adiantoDoCeu`. Recuar nao e
		// "desvirar" o dia (nada do que ja aconteceu se desfaz); e so reancorar, senao a proxima
		// virada de verdade seria engolida.
		if (hoje < _sagas.UltimoDiaVisto) { _sagas.UltimoDiaVisto = hoje; return; }

		// TETO DE VOLTAS. Um servidor parado por meses voltaria com milhares de viradas pendentes, e
		// cada uma pode NASCER um chefe -- e a regra 0.4 diz que nada pesado entra no tique. Com teto,
		// o atraso e absorvido em varios tiques de 1 Hz em vez de um congelamento.
		for (int i = 0; i < 64 && _sagas.UltimoDiaVisto < hoje; i++)
		{
			_sagas.UltimoDiaVisto++;
			VirouODia();
		}
	}

	// =====================================================================
	// OS MARCOS
	// =====================================================================
	/// <summary>
	/// ============================ ESTE JOGADOR CONTA PRO MARCO DE RACA? ============================
	/// E o `bev_is_saiyan` (BossEvents.dm:164), que testa `Race` **ou** `Parent_Race` -- ou seja, o
	/// mestico conta como Saiyajin pro marco. Este port nao tem `Parent_Race`; o que ele tem e a raca
	/// `Halfbreed`, que e IRMA e nao subtipo de `Saiyan` (a mesma armadilha de tipo que ja custou o
	/// rabo do mestico -- ver `TemRabo`). Entao a equivalencia e escrita aqui, uma vez.
	/// ==========================================================================================
	/// </summary>
	private static bool ContaProMarco(ServerPlayer pl, string raca)
	{
		if (raca.Length == 0) return true;
		if (string.Equals(pl.Race, raca, StringComparison.OrdinalIgnoreCase)) return true;
		return string.Equals(raca, "Saiyan", StringComparison.OrdinalIgnoreCase)
			&& string.Equals(pl.Race, "Halfbreed", StringComparison.OrdinalIgnoreCase);
	}

	private void ConferirMarcos()
	{
		for (int i = 0; i < _cadeia.Length; i++)
		{
			EloDaSaga elo = _cadeia[i];
			EstadoDoElo? est = _sagas.Do(elo.Id);
			if (est == null || est.Disparou) continue;

			// A CADEIA E SEQUENCIAL. O marco continua sendo conferido -- ele nao se perde, ele espera.
			if (!Sagas.PodeComecar(_cadeia, _sagas, i)) continue;

			foreach (ServerPlayer pl in _players.Values)
			{
				// ============================ SO JOGADOR, VIVO -- E A PERGUNTA E UMA SO ============================
				// O `check_triggers` do DM tem DUAS defesas: a colecao ja e so de jogadores
				// (`for(var/mob/M in player_list)`) E o `!M.client` por cima (BossEvents.dm:373-374).
				// Aqui `_players` guarda tudo, entao a segunda defesa e a unica -- e por isso ela e o
				// funil do `Core` (<see cref="Gente.EhJogador"/>) e nao um `Peer != null` escrito a mao.
				//
				// A razao pratica aparece com o povoamento ligado: um chefe de saga (que e NPC de 530
				// mil) dispararia o marco SEGUINTE sozinho, que nasce um chefe maior, que dispara o
				// proximo -- a cadeia inteira num servidor vazio. E "nem por ninguem que ele traga
				// junto": o cidadao com Zenkai, o clone da mente (que carrega o `expressedBP` do dono)
				// e o boneco do corpo largado (que COMPARTILHA a ficha do dono) tambem saem daqui pela
				// mesma linha, e nao por tres guardas escritas em tres dias diferentes.
				// ==========================================================================================
				if (!EhJogador(pl) || pl.Ficha.dead) continue;

				// **BP BASE**, e nao `expressedBP`. Armadilha 6 da PARTE 3: o expresso e outra escala.
				if (pl.Ficha.BP < elo.GatilhoBp) continue;
				if (!ContaProMarco(pl, elo.GatilhoRaca)) continue;

				DispararElo(elo, est, pl);
				break;
			}
		}
	}

	private void DispararElo(EloDaSaga elo, EstadoDoElo est, ServerPlayer culpado)
	{
		est.Disparou = true;

		// ============================ O PLANETA AINDA EXISTE? ============================
		// E o `bev_planet_ok` (BossEvents.dm:140): marco consumido SEM evento quando o alvo ja nao
		// esta la. Hoje a unica forma de um planeta "nao existir" e nao estar no manifesto de mapas --
		// nao ha destruicao de planeta neste port (ver `GanchoDeDestruicao`) --, mas a porta e a mesma
		// e no dia em que houver, ela ja e por onde a resposta passa.
		//
		// CANCELADA CONTA COMO TERMINADA: a saga seguinte destrava. Sem isso, destruir um planeta
		// travaria a cadeia inteira pra sempre.
		if (_catalogo?.Get(ZoneKey.Premade(elo.Planeta)) == null)
		{
			est.Fase = FaseDaSaga.Cancelada;
			GD.PushWarning($"[server] saga '{elo.Id}': o planeta '{elo.Planeta}' nao esta no manifesto "
						 + "de mapas -- marco consumido sem evento");
			SalvarSagas();
			return;
		}

		double media = MediaDoServidor();
		est.Fase = FaseDaSaga.Contando;
		est.Chefes.Clear();

		int primeiraChegada = int.MaxValue;
		foreach (ChefeDaSaga c in elo.Chefes)
		{
			MoldeDeNpc? m = _moldes?.Get(c.Molde);
			if (m == null) { GD.PushError($"[server] saga '{elo.Id}': molde '{c.Molde}' sumiu"); continue; }

			int dias = c.DiasMax > c.DiasMin ? _rng.Next(c.DiasMin, c.DiasMax + 1) : c.DiasMin;
			primeiraChegada = Math.Min(primeiraChegada, dias);

			// ============================ AQUI, E SO AQUI, O BP DO CHEFE E DECIDIDO ============================
			// Com a media de AGORA. Daqui pra frente o vetor e o que manda -- no nascimento (dias
			// in-game depois), em cada degrau da ficha, e em toda reancoragem do `TickDoRoteiro`.
			// ==============================================================================================
			est.Chefes.Add(new EstadoDoChefe
			{
				Molde = c.Molde,
				DiasRestantes = dias,
				Bps = Sagas.Pinar(m, media),
			});
		}

		if (est.Chefes.Count == 0) { est.Fase = FaseDaSaga.Cancelada; SalvarSagas(); return; }
		if (primeiraChegada == int.MaxValue) primeiraChegada = 0;

		AnunciarNoMundo(elo.AnuncioDoMarco.Replace("{dias}", primeiraChegada.ToString()));
		GD.Print($"[server] SAGA '{elo.Id}' disparada por {culpado.Name} (BP {culpado.Ficha.BP:N0}) | "
			   + $"media {media:N0} | BP pinado: "
			   + string.Join(" / ", est.Chefes.Select(c => $"{c.Molde} {c.Bps.FirstOrDefault():N0}")));

		// CHEGADA NO MESMO DIA (dias = 0) nao espera a proxima virada: sem isto os androides do elo do
		// Cell -- que no original nascem dentro do proprio `fire_m3` (BossEvents.dm:409) -- so
		// apareceriam na virada seguinte, ate 24 minutos reais depois do anuncio.
		VirouODia(soAsChegadasDeHoje: true);
		SalvarSagas();
	}

	// =====================================================================
	// A VIRADA DO DIA
	// =====================================================================
	private void VirouODia(bool soAsChegadasDeHoje = false)
	{
		foreach (EloDaSaga elo in _cadeia)
		{
			EstadoDoElo? est = _sagas.Do(elo.Id);
			if (est == null) continue;
			if (est.Fase is not (FaseDaSaga.Contando or FaseDaSaga.EmCurso)) continue;

			int menor = int.MaxValue;
			foreach (EstadoDoChefe ec in est.Chefes.ToList())
			{
				if (ec.Fase != FaseDoChefe.ACaminho) continue;
				if (!soAsChegadasDeHoje) ec.DiasRestantes--;

				if (ec.DiasRestantes <= 0) NascerChefeDaSaga(elo, est, ec);
				else menor = Math.Min(menor, ec.DiasRestantes);
			}

			// O LEMBRETE DIARIO, e nao um aviso unico. E do original, com a razao escrita nele: *"os
			// dias in-game passam rapido e um aviso unico se perdia"* (BossEvents.dm:430). Um por elo e
			// nao um por chefe -- tres androides a caminho nao sao tres lembretes.
			if (!soAsChegadasDeHoje && menor != int.MaxValue && elo.AnuncioDiario.Length > 0)
				AnunciarNoMundo(elo.AnuncioDiario.Replace("{dias}", menor.ToString()));
		}
		if (!soAsChegadasDeHoje) SalvarSagas();
	}

	// =====================================================================
	// O NASCIMENTO DE UM CHEFE
	// =====================================================================
	/// <summary>
	/// POE UM CHEFE NO MUNDO -- pela MESMA <see cref="NascerNpc"/> do povoamento, e nao por uma
	/// fabrica de chefe. O que ela ganha depois e o vetor pinado e o degrau salvo.
	///
	/// O `init_event_boss` do original tambem e a receita do `PlanetPopulation` com um passo a mais
	/// (`bev_pin_bp`), e pelo mesmo motivo: um chefe que nasce por outro caminho e um chefe a quem
	/// faltam as regras que forem acrescentadas ao nascimento amanha.
	/// </summary>
	/// <summary>
	/// ============================ TRES MOTIVOS PRA UM CHEFE APARECER, E TRES FRASES ============================
	/// O mesmo nascimento serve aos tres, e o que muda e o que o mundo ouve -- o que **nao** e
	/// cosmetico: quem estava online antes de um reinicio leria "Freeza CHEGOU a Namek" e iria ao
	/// planeta esperar uma nave que ja pousou.
	///
	/// O original tem o mesmo argumento (`resume`) e a mesma troca de frase (BossEvents.dm:488), e
	/// tem tambem a terceira -- o retorno da morte, que la e um `switch(form)` dentro do `spawn_cell`
	/// (:527-533). Aqui a frase do retorno e a do proprio DEGRAU da ficha, que ja existe pra quando
	/// ele e alcancado por dano: e a mesma forma, entao e a mesma linha.
	/// ======================================================================================================
	/// </summary>
	private enum ComoChegou { Agora, Retomando, Ressurgindo }

	private void NascerChefeDaSaga(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec,
								   ComoChegou como = ComoChegou.Agora)
	{
		var zona = ZoneKey.Premade(elo.Planeta);
		if (_catalogo?.Get(zona) == null)
		{
			GD.PushError($"[server] saga '{elo.Id}': zona '{elo.Planeta}' sumiu do manifesto");
			ec.Fase = FaseDoChefe.Consumido;
			return;
		}

		ulong lugar = ++_lugarDasSagas;
		ServerPlayer? npc = NascerNpc(ec.Molde, zona, PontoDeHabitante(zona, lugar), lugar);
		if (npc?.Papel == null)
		{
			GD.PushError($"[server] saga '{elo.Id}': o chefe '{ec.Molde}' nao nasceu");
			ec.Fase = FaseDoChefe.Consumido;
			return;
		}

		// O PINO E O DEGRAU, os dois antes de qualquer outra coisa tocar a ficha.
		npc.Papel.BpsPinados = ec.Bps;
		npc.Papel.Estagio = Math.Clamp(ec.Degrau, 0, Math.Max(npc.Papel.Molde.Estagios.Length - 1, 0));
		EntrarNoDegrau(npc, cura: 0, anunciar: false);

		ec.Corpo = npc.Id;
		ec.Fase = FaseDoChefe.NoMundo;
		ec.Engajou = false;
		ec.Carregando = false;
		est.Fase = FaseDaSaga.EmCurso;

		// O RELOGIO DO ULTIMATO comeca a correr na CHEGADA (o `s1_spawn_time`, BossEvents.dm:466).
		ec.UltimatoEm = elo.DestroiPlaneta ? _relogioDoMundo + elo.MinutosDeOcio * 60 : 0;

		ChefeDaSaga? ficha = Array.Find(elo.Chefes, c => c.Molde == ec.Molde);
		AnunciarNoMundo(como switch
		{
			ComoChegou.Retomando =>
				$"{npc.Name} CONTINUA em {NomeDoPlaneta(elo.Planeta)} -- a ameaça não passou.",

			// A FRASE DO DEGRAU: pro Cell que volta Super Perfeito e a mesma linha que ele diria se
			// tivesse chegado ali por dano. Sem ela ele ressuscitaria anunciando a propria estreia.
			ComoChegou.Ressurgindo =>
				npc.Papel.Degrau?.Anuncio is { Length: > 0 } linha
					? linha : $"{npc.Name} voltou -- e não está mais como antes.",

			_ => ficha?.AnuncioDaChegada ?? "",
		});

		GD.Print($"[server] SAGA '{elo.Id}': '{npc.Name}' chegou em {elo.Planeta} | degrau "
			   + $"{npc.Papel.Estagio + 1}/{npc.Papel.Molde.Estagios.Length} | BP PINADO "
			   + $"{npc.Ficha.BP:N0} (a ficha do arquivo diz {npc.Papel.Molde.Estagios[npc.Papel.Estagio].Bp:N0})");
	}

	/// <summary>
	/// OS CHEFES QUE ESTAVAM DE PE QUANDO O SERVIDOR CAIU VOLTAM -- o `respawn_active_bosses`
	/// (BossEvents.dm:916), inclusive na parte que o autor do original teve que consertar depois
	/// (*"Boo ativo no reboot NUNCA voltava (faltava esta linha)"*, :940).
	///
	/// Aqui nao ha linha pra faltar: e o mesmo laco sobre os mesmos estados. Um chefe salvo como
	/// `NoMundo` volta no degrau salvo, com o BP salvo -- e nao com o do arquivo.
	/// </summary>
	private void RetomarSagas()
	{
		int voltaram = 0;
		foreach (EloDaSaga elo in _cadeia)
		{
			EstadoDoElo? est = _sagas.Do(elo.Id);
			if (est is not { Fase: FaseDaSaga.EmCurso }) continue;

			foreach (EstadoDoChefe ec in est.Chefes.ToList())
			{
				if (ec.Fase != FaseDoChefe.NoMundo) continue;
				ec.Corpo = 0;                    // o corpo antigo nao atravessou o reinicio
				NascerChefeDaSaga(elo, est, ec, ComoChegou.Retomando);
				voltaram++;
			}
		}
		if (voltaram > 0) GD.Print($"[server] sagas: {voltaram} chefe(s) voltaram ao mundo depois do reinicio");
	}

	// =====================================================================
	// O MONITOR
	// =====================================================================
	private void MonitorarElos()
	{
		foreach (EloDaSaga elo in _cadeia)
		{
			EstadoDoElo? est = _sagas.Do(elo.Id);
			if (est is not { Fase: FaseDaSaga.EmCurso }) continue;

			foreach (EstadoDoChefe ec in est.Chefes.ToList())
			{
				switch (ec.Fase)
				{
					case FaseDoChefe.NoMundo: MonitorarChefe(elo, est, ec); break;

					// VOLTANDO DO ALEM (o Cell morto na forma perfeita). O relogio e o do MUNDO (`dt`
					// acumulado) e nao o de parede, pela mesma razao do passeio do habitante: o de
					// parede nao e controlavel por bancada nenhuma.
					case FaseDoChefe.Derrotado when ec.RessurgeEm > 0 && _relogioDoMundo >= ec.RessurgeEm:
						ec.RessurgeEm = 0;
						ec.Fase = FaseDoChefe.ACaminho;
						NascerChefeDaSaga(elo, est, ec, ComoChegou.Ressurgindo);
						SalvarSagas();
						break;
				}
			}

			// O ELO ACABA quando nao ha mais nada dele pra acontecer: ninguem no mundo, ninguem a
			// caminho e ninguem voltando. Um chefe `Derrotado` com `RessurgeEm` ainda armado NAO fecha
			// o elo -- e a diferenca entre "o Cell morreu" e "a Terra esta livre dele".
			bool pendente = est.Chefes.Exists(c =>
				c.Fase is FaseDoChefe.NoMundo or FaseDoChefe.ACaminho || c.RessurgeEm > 0);

			if (!pendente && est.Fase == FaseDaSaga.EmCurso)
			{
				est.Fase = FaseDaSaga.Vencida;
				if (elo.AnuncioDaVitoria.Length > 0) AnunciarNoMundo(elo.AnuncioDaVitoria);
				GD.Print($"[server] SAGA '{elo.Id}' VENCIDA -- o elo seguinte esta destravado");
				SalvarSagas();
			}
		}
	}

	private void MonitorarChefe(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec)
	{
		// ============================ O CORPO SUMIU SEM AVISO DE MORTE ============================
		// Limpeza externa, admin, um `RemoverNpc` de outro caminho. O original trata isso como VITORIA
		// (`if(!boss1) s1_state = 3`, BossEvents.dm:554) e o comentario diz por que: o contrario e a
		// saga travada para sempre, e com ela a cadeia inteira.
		// ====================================================================================
		if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? corpo) || corpo.Papel == null)
		{
			ec.Fase = FaseDoChefe.Derrotado;
			ec.Corpo = 0;
			SalvarSagas();
			return;
		}

		// O DEGRAU DA FICHA E DO PAPEL; aqui so se COPIA pro save. Escrever nos dois lados faria o
		// `TickDoRoteiro` e este monitor discordarem no primeiro degrau que um deles perdesse.
		ec.Degrau = corpo.Papel.Estagio;

		if (corpo.Ficha.dead) { MorreuOChefe(elo, est, ec, corpo); return; }

		// APANHOU = FOI ENFRENTADO. E o `if(!s1_engaged && boss1.target)` do original com o sinal que
		// este port ja tem: `UltimoAgressor` + `RancorAte` sao o funil unico de "alguem bateu em mim"
		// (ver `MarcarAgressao`). Usar "tem alvo" exigiria expor o alvo da IA, que hoje e uma variavel
		// local do tique -- e um campo novo so pra isto seria uma segunda verdade sobre a mesma coisa.
		//
		// ============================ ENFRENTADO **POR GENTE**, e o preco disto era o planeta ============================
		// `MarcarAgressao` aceita QUALQUER agressor -- cidadao, chefe, Oozaru, boneco. E `Engajou`
		// escolhe o prazo do ultimato: pra `freeza_vegeta` sao 6 minutos em vez de 18. A sequencia,
		// sem jogador nenhum: Freeza pousa em Vegeta (40 cidadaos), bate num deles, o cidadao revida,
		// `Engajou` sobe -- e o primeiro jogador que logar cai numa janela de 6 minutos que ele nao
		// abriu, contra um Freeza ja engajado com o povo. Se ele nao chegar a tempo, Vegeta explode:
		// e o "explodiu sozinho" com outro nome. No original o alvo do chefe so pode ser um `client`
		// (`NPCAI.dm:439`, *"AI nao ataca AI"*), entao la a pergunta nem existe.
		// ==========================================================================================================
		if (!ec.Engajou && _players.TryGetValue(corpo.UltimoAgressor, out ServerPlayer? quemBateu)
			&& EhJogador(quemBateu) && NowMs() < corpo.RancorAte)
		{
			ec.Engajou = true;
			if (elo.DestroiPlaneta)
			{
				ec.UltimatoEm = _relogioDoMundo + elo.MinutosDeLuta * 60;
				AnunciarNoMundo($"A batalha contra {corpo.Name} começou em {NomeDoPlaneta(elo.Planeta)}! "
							  + $"Derrotem-no antes que ele perca a paciência (~{elo.MinutosDeLuta:0} min).");
			}
		}

		AbsorverCompanheiroCaido(elo, est, ec, corpo);
		if (elo.DestroiPlaneta) CorrerOUltimato(elo, est, ec, corpo);
	}

	// =====================================================================
	// A ABSORCAO (o Cell)
	// =====================================================================
	/// <summary>
	/// ============================ ENCOSTAR NUM COMPANHEIRO CAIDO SOBE UM DEGRAU ============================
	/// E o `cell_absorb_contact` (BossEvents.dm:680): o Cell alcanca um androide NOCAUTEADO -- por ele
	/// ou por um jogador -- e evolui NO LOCAL, na frente de todo mundo.
	///
	/// **NAO E UMA SEGUNDA MECANICA DE TRANSFORMACAO.** Ela chama o MESMO `AvancarEstagio` que o dano
	/// dispara: a ficha pronta do Cell e a mesma lista de degraus do Freeza, so que movida por um
	/// evento em vez de por um limiar de membro. Fosse um caminho proprio, a cura, o reancoramento do
	/// gatilho e o pino do BP precisariam ser escritos de novo -- e um deles ficaria de fora.
	///
	/// O CAMINHO ATE A VITIMA E O `bev_prey`: a caca normal pula quem esta no chao (e tem que pular),
	/// entao sem o alvo imposto o Cell nunca chegaria perto. Ver `PapelDeNpc.PresaDoRoteiro`.
	/// ====================================================================================================
	///
	/// FICA DE FORA a fuga da tela (`cell_absorb`, :710): o Cell ferido que some do mundo e volta dias
	/// depois ja evoluido. E um SEGUNDO caminho pro mesmo resultado, e o que ele acrescenta -- o susto
	/// do desaparecimento -- nao paga o estado a mais. O Cell ferido aqui luta ate o fim no degrau em
	/// que esta, que e o que o proprio original faz quando nao ha androide vivo pra absorver.
	/// </summary>
	private void AbsorverCompanheiroCaido(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec, ServerPlayer corpo)
	{
		ChefeDaSaga? ficha = Array.Find(elo.Chefes, c => c.Molde == ec.Molde);
		if (ficha is not { Absorve: true } || corpo.Papel == null) return;
		if (corpo.Ficha.KO || corpo.Papel.Proximo == null) return;   // caido nao absorve; no ultimo degrau nao ha pra onde subir

		// A VITIMA: outro chefe DESTE elo, no mundo, nocauteado e na mesma zona.
		EstadoDoChefe? presa = null;
		ServerPlayer? corpoDaPresa = null;
		foreach (EstadoDoChefe outro in est.Chefes)
		{
			if (outro == ec || outro.Fase != FaseDoChefe.NoMundo) continue;
			if (!_players.TryGetValue(outro.Corpo, out ServerPlayer? c2)) continue;
			if (!c2.Ficha.KO || c2.Ficha.dead || c2.Zone.Hash != corpo.Zone.Hash) continue;
			presa = outro; corpoDaPresa = c2; break;
		}

		if (presa == null || corpoDaPresa == null) { corpo.Papel.PresaDoRoteiro = 0; return; }

		// PRIORIDADE ABSOLUTA: larga a luta atual e vai. O canal e o mesmo que o `PresaDoNpc` consulta.
		corpo.Papel.PresaDoRoteiro = corpoDaPresa.Id;

		// UM TILE E MEIO -- o `get_dist(cell, koed) <= 1` do original, em pixels. Folga de meio tile
		// porque aqui a posicao e continua e nao uma grade: exigir a distancia exata de um tile faria
		// o Cell orbitar a vitima sem nunca "encostar".
		const float Contato = 48f;
		if ((corpoDaPresa.Pos - corpo.Pos).LengthSquared > Contato * Contato) return;

		string nome = corpoDaPresa.Name;
		presa.Fase = FaseDoChefe.Consumido;
		presa.Corpo = 0;
		RemoverNpc(corpoDaPresa);

		corpo.Papel.PresaDoRoteiro = 0;
		AvancarEstagio(corpo);
		ec.Degrau = corpo.Papel.Estagio;

		AnunciarNoMundo($"{corpo.Name.ToUpperInvariant()} ABSORVEU {nome.ToUpperInvariant()}!! "
					  + $"Um poder muito maior emana dele. (Forma {corpo.Papel.Estagio + 1})");
		GD.Print($"[server] SAGA '{elo.Id}': {corpo.Name} absorveu {nome} -> degrau "
			   + $"{corpo.Papel.Estagio + 1} | BP {corpo.Ficha.BP:N0}");
		SalvarSagas();
	}

	// =====================================================================
	// A MORTE DE UM CHEFE
	// =====================================================================
	private void MorreuOChefe(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec, ServerPlayer corpo)
	{
		ec.Fase = FaseDoChefe.Derrotado;
		ec.Carregando = false;
		ec.UltimatoEm = 0;
		ec.Corpo = 0;

		ChefeDaSaga? ficha = Array.Find(elo.Chefes, c => c.Molde == ec.Molde);
		MoldeDeNpc? molde = _moldes?.Get(ec.Molde);

		// ============================ MORRER NESTE DEGRAU NAO E MORRER ============================
		// O Cell morto na forma perfeita volta Super Perfeito um minuto depois (BossEvents.dm:760). O
		// campo e 1-based porque e assim que o dono conta degrau no `npcs.json`; `ec.Degrau` e 0-based.
		// A conferencia do degrau seguinte existir e obrigatoria: sem ela um dado errado marcaria o
		// chefe pra voltar num degrau que nao existe e o elo nunca fecharia.
		// =====================================================================================
		if (ficha is { RessurgeNoDegrau: > 0 } && molde != null
			&& ec.Degrau + 1 == ficha.RessurgeNoDegrau
			&& ec.Degrau + 1 < molde.Estagios.Length)
		{
			ec.Degrau++;
			ec.RessurgeEm = _relogioDoMundo + ficha.RessurgeEmSegundos;
			AnunciarNoMundo($"{corpo.Name} foi morto... mas algo está errado. "
						  + "Uma única célula sobreviveu à explosão...");
			GD.Print($"[server] SAGA '{elo.Id}': {corpo.Name} caiu no degrau {ec.Degrau} e volta em "
				   + $"{ficha.RessurgeEmSegundos:0}s no degrau {ec.Degrau + 1}");
			SalvarSagas();
			return;
		}

		if (ficha is { AnuncioDaQueda.Length: > 0 }) AnunciarNoMundo(ficha.AnuncioDaQueda);

		PagarOHeroi(elo, ec, corpo, ficha?.Recompensa ?? Reputacao.HeroiPorChefe);
		SalvarSagas();
	}

	/// <summary>
	/// ============================ QUEM DERRUBOU O CHEFE VIRA HEROI DO POVO ============================
	/// A recompensa da cadeia inteira: **reputacao de heroi, +100** (`BEV_HERO_REP`, BossEvents.dm:92;
	/// o androide vale 50 pelo `BEV_HERO_REP_ANDROID`, e por isso o numero e do DADO e nao daqui).
	///
	/// O MATADOR E O `UltimoAgressor`, que e o funil unico de agressao deste port (`MarcarAgressao`).
	/// O original faz a mesma pergunta pelo `lastDamager` e tem um plano B -- o jogador em combate mais
	/// perto (`bev_resolve_killer`, :245) -- pra o caso de o golpe final ter vindo por um caminho que
	/// nao registra autor. Aqui esse plano B seria remendo: o caminho que nao registra autor e um
	/// defeito conhecido (o dano por PROJETIL escreve `UltimoAgressor` por fora do funil), e o conserto
	/// e no funil, nao numa heuristica de proximidade que acertaria a pessoa errada em silencio.
	/// ==============================================================================================
	///
	/// SEM MATADOR, NINGUEM RECEBE -- e o console diz. Um chefe que morre de queda, de sol ou nas maos
	/// de outro NPC nao fez ninguem heroi, e inventar um beneficiario seria pior que nao pagar.
	/// </summary>
	private void PagarOHeroi(EloDaSaga elo, EstadoDoChefe ec, ServerPlayer corpo, double quanto)
	{
		if (!_players.TryGetValue(corpo.UltimoAgressor, out ServerPlayer? matador) || !EhJogador(matador))
		{
			GD.Print($"[server] SAGA '{elo.Id}': '{ec.Molde}' caiu sem um matador identificavel "
				   + "-- ninguem ganhou reputacao");
			return;
		}

		SomarReputacao(elo.Planeta, matador, quanto, "chefe-de-saga-derrotado");
		AnunciarNoMundo($"{matador.Name} é aclamado(a) como HERÓI pelo povo de {NomeDoPlaneta(elo.Planeta)}!");
	}

	// =====================================================================
	// O ULTIMATO E O GANCHO DA DESTRUICAO
	// =====================================================================
	private void CorrerOUltimato(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec, ServerPlayer corpo)
	{
		MoldeDeNpc? molde = _moldes?.Get(ec.Molde);

		// ============================ A SAGA 2 SO ARMA O PRAZO NA FORMA FINAL ============================
		// No original o `s2_deadline` e escrito DENTRO do `freeza_transform`, no ramo da ultima forma
		// (BossEvents.dm:672). Aqui isso e uma linha de dado (`ultimatoSoNoUltimoDegrau`) porque a
		// pergunta "ele esta no ultimo degrau?" a ficha ja responde.
		// ==========================================================================================
		if (elo.UltimatoSoNoUltimoDegrau && molde != null && ec.Degrau < molde.Estagios.Length - 1)
		{
			ec.UltimatoEm = 0;
			return;
		}
		if (ec.UltimatoEm <= 0)
			ec.UltimatoEm = _relogioDoMundo + (ec.Engajou ? elo.MinutosDeLuta : elo.MinutosDeOcio) * 60;

		// ============================ SERVIDOR VAZIO: O ULTIMATO ESPERA ============================
		// **LITERAL** e pela mesma razao escrita no original: sem ninguem online pra reagir, o planeta
		// "explodiria sozinho" de madrugada (BossEvents.dm:562, :592). Empurrar o prazo (em vez de
		// deixa-lo vencer) e o que faz o evento ser sobre os jogadores e nao sobre o relogio.
		// ====================================================================================
		// "NINGUEM ONLINE" E `EhJogador` E NAO `Peer != null`: os 40 cidadaos de Vegeta nao sao
		// alguem "pra reagir", e o boneco de quem esta meditando tambem nao -- o dono dele ja conta
		// uma vez, pelo proprio corpo.
		if (!_players.Values.Any(p => EhJogador(p) && !p.Ficha.dead))
		{
			ec.UltimatoEm = _relogioDoMundo + (ec.Engajou ? elo.MinutosDeLuta : elo.MinutosDeOcio) * 60;
			return;
		}

		if (_relogioDoMundo < ec.UltimatoEm) return;

		// ============================ A CARGA DE 30 s: A JANELA PRA INTERROMPER ============================
		// `BEV_PD_CHARGE 300` decimos = 30 s (BossEvents.dm:29), e o comentario diz o desenho: *"janela
		// p/ matar/nocautear e interromper"*. Sem ela o ultimato seria um instante e nao um momento --
		// e derrubar o chefe deixaria de ter um significado especifico no fim da luta.
		// ==============================================================================================
		if (!ec.Carregando)
		{
			ec.Carregando = true;
			ec.UltimatoEm = _relogioDoMundo + 30;
			AnunciarNoMundo(elo.AnuncioDoUltimato.Length > 0
				? elo.AnuncioDoUltimato.Replace("{planeta}", NomeDoPlaneta(elo.Planeta))
				: $"{corpo.Name} começou a concentrar uma energia COLOSSAL sobre "
				  + $"{NomeDoPlaneta(elo.Planeta)}! Nocauteiem-no em 30 SEGUNDOS para interromper!");
			return;
		}

		// NOCAUTEADO NA JANELA: interrompido, e ele tenta de novo em 2 min (`BEV_PD_RETRY`, :30).
		if (corpo.Ficha.KO || corpo.Ficha.dead)
		{
			ec.Carregando = false;
			ec.UltimatoEm = _relogioDoMundo + 120;
			AnunciarNoMundo($"O ataque de {corpo.Name} foi INTERROMPIDO! "
						  + "Ele foi nocauteado antes de liberar a energia!");
			return;
		}

		GanchoDeDestruicao(elo, est, ec, corpo);
	}

	/// <summary>
	/// ============================ O GANCHO DA DESTRUICAO DE PLANETA -- AGORA ELE MORDE ============================
	/// Este metodo era **inerte de proposito**, e o comentario que estava aqui prometia: *"quando a
	/// destruicao existir, o que muda e o CORPO deste metodo -- nem a cadeia, nem o relogio, nem o
	/// save"*. Foi exatamente isso que aconteceu: a cadeia, o prazo em dias in-game, o ultimato que
	/// espera o servidor encher, os 30 s de janela e o KO que interrompe **nao mudaram uma linha**.
	///
	/// O que ele faz agora, na ordem:
	///   1. fecha o elo como **consumado** (o vilao venceu -- o `s1_state = 4` do original), o que
	///      destrava a saga seguinte;
	///   2. marca o planeta como condenado no `sagas.json` (isto ja existia, e continua sendo a
	///      resposta da CADEIA -- a destruicao tem livro proprio);
	///   3. **acende a destruicao de verdade** (`GameServer.Destruicao.ComecarDestruicao`), com o BP
	///      EXPRESSO do chefe como `mexpressedBP`. E o mesmo numero que o `Planet_Destroy` fixa
	///      (`Planets.dm:342`), e ele decide quem sobrevive ao commit cinco minutos depois.
	///
	/// ============================ E O CHEFE SAI **DEPOIS** DE ACENDER ============================
	/// A ordem importa e ja mordeu uma vez: `RemoverNpc(corpo)` tira o corpo de `_players`, e o
	/// commit resolve o algoz pelo `IdDoAlgoz`. Removendo antes, o id ja nao existiria e a explosao
	/// teria dono nulo -- que funciona (o dano entra sem autor), mas seria por acidente. O BP e lido
	/// antes de tudo, pelo mesmo motivo: e um numero, e ele nao pode depender de o corpo ainda estar
	/// no mundo.
	///
	/// **NAO HA PERDA DE RANK** por destruir um planeta -- confirmado no original: nenhuma leitura
	/// de karma ou de cargo no caminho do `DestroyPlanet`.
	/// ======================================================================================
	/// </summary>
	private void GanchoDeDestruicao(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec, ServerPlayer corpo)
	{
		est.Condenado = true;
		est.Fase = FaseDaSaga.Consumada;
		ec.Fase = FaseDoChefe.Consumido;
		ec.Carregando = false;
		ec.UltimatoEm = 0;
		ec.Corpo = 0;

		AnunciarNoMundo($"A energia de {corpo.Name} varre {NomeDoPlaneta(elo.Planeta)}... "
					  + "e ninguém pôde impedi-lo.");

		// O `mexpressedBP` (`Planets.dm:342`) e o id do algoz, LIDOS ANTES de o corpo sair de cena.
		double bpDoChefe = corpo.Ficha.expressedBP;
		int idDoChefe = corpo.Id;

		// O PLANETA DA SAGA E UM PRE-FEITO por construcao (`elo.Planeta` e nome de zona do
		// manifesto: Vegeta, Namek, Earth). Nao ha caso procedural aqui.
		if (!ComecarDestruicao(ZoneKey.Premade(elo.Planeta), bpDoChefe,
							   $"saga '{elo.Id}': {corpo.Name}", idDoChefe))
			GD.PushWarning($"[server] a saga '{elo.Id}' consumou sobre '{elo.Planeta}', mas a "
						 + "destruicao nao pegou (o planeta ja estava condenado ou nao e um planeta)");

		RemoverNpc(corpo);
		SalvarSagas();
	}

	// =====================================================================
	// O ANUNCIO
	// =====================================================================
	/// <summary>
	/// UMA LINHA PRA TODO MUNDO, em qualquer planeta. E o `bev_announce` (BossEvents.dm:102), menos o
	/// letreiro de tela e o trovao -- os dois sao do cliente e nao ha canal pra eles hoje. A licao que
	/// o original registra ali (*"uma linha de chat se afoga no spam de combate"*) continua valendo e
	/// fica anotada: o letreiro e uma divida do lado do cliente, nao uma decisao.
	///
	/// SO QUEM TEM `Peer`. Mandar aviso de saga pros 148 habitantes seriam 148 `Send` no-op por linha.
	/// </summary>
	private void AnunciarNoMundo(string texto)
	{
		if (texto.Length == 0) return;
		foreach (ServerPlayer p in _players.Values) if (p.Peer != null) Avisar(p, texto);
		EscutaDasSagas?.Add(texto);
		GD.Print($"[saga] {texto}");
	}
}
