using System.Text.Json;
using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// A DESTRUICAO DE PLANETA -- os dois caminhos do `Area_Death.dm`, inteiros.
///
/// ============================ OS DOIS CAMINHOS, E POR QUE SAO UM SISTEMA SO ============================
///   (a) **MORTE LENTA** (`area/proc/Planet_Death`, `Area_Death.dm:8-50`): quatro estagios de 200,
///       300, 300 e 400 s -- **20 minutos**. A cada estagio morre uma fatia dos habitantes
///       (25/50/75/100%), a partir do 1 a noite nao acaba mais, e a partir do 2 o chao comeca a se
///       desfazer perto de quem esta olhando. No fim, ela **chama a destruicao**.
///   (b) **DESTRUICAO IMEDIATA** (`DestroyPlanet`, `Area_Death.dm:75-147`): ~5 minutos de tremor,
///       explosao e ceu de poeira, e entao o **commit** -- dano em area, morte de quem estava
///       caido, evacuacao pro espaco e o planeta na lista de mortos.
///
/// Os dois terminam no MESMO commit, e e por isso que sao um arquivo so: (a) e um prologo de (b).
/// ==================================================================================================
///
/// ============================ O QUE PERSISTE, E POR QUE ISSO E O CORACAO DO ARQUIVO ============================
/// O original guarda o estado em tres vars de area (`Area_Death.dm:5-7`) e **so uma delas persiste**:
/// `planet_dying` volta do disco, `planet_death_stage` e `death_proc_running` sao `tmp`. O tique de
/// `Weather.dm:72-74` le a primeira e re-`spawn`a o `Planet_Death` -- do estagio 0. Resultado: um
/// planeta com o pavio aceso **recomeca a morrer a cada boot**, para sempre. Foi preciso um remendo
/// no `Boss_Events_Init` (`BossEvents.dm:966-971`) pra desarmar isso na mao.
///
/// Aqui **o registro inteiro persiste junto**, numa escrita so: fase, estagio, quanto FALTA do passo
/// atual, o BP do algoz e o motivo (ver <see cref="EstadoDaMorte"/>). O boot nao reconstroi nada --
/// ele **continua**. E o que falta e guardado em SEGUNDOS QUE RESTAM, e nao num instante do relogio
/// do universo: aquele relogio anda com o servidor desligado, e um prazo absoluto faria uma noite
/// fora consumir o pavio inteiro.
/// ==========================================================================================================
///
/// ============================ SERVIDOR VAZIO ADIA -- E SO O PAVIO ============================
/// A licao esta escrita no proprio original, no ultimato do Freeza (`BossEvents.dm:562`): o planeta
/// nao pode explodir de madrugada sem ninguem pra impedir. **Vale aqui, e vale so pra (a)**:
///   * o PAVIO LENTO congela com o servidor vazio. Os estagios 2 e 3 sao literalmente "destruir chao
///     perto de um jogador" (`Area_Death.dm:64`) -- sem jogador eles nao tem o que fazer, e deixar o
///     relogio correr transformaria vinte minutos de aviso em "o planeta ja era quando voce entrou".
///   * a EXPLOSAO nao congela. Ela so comeca com alguem online (o ultimato da saga ja garante isso,
///     e o verb exige um jogador vivo no planeta), e uma vez comecada ela e um FATO e nao um
///     espetaculo: se todo mundo deslogar no meio dos cinco minutos, o mundo acaba assim mesmo.
/// =====================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>
	/// O LIVRO DOS MORTOS -- a `PlanetDisableList` (`Area_Death.dm:144`) com chave honesta.
	///
	/// Ver <see cref="ChaveDePlaneta"/>: o DM chaveia por NOME, e no port o nome de um planeta
	/// procedural nao e unico. Aqui a chave e a seed.
	/// </summary>
	private readonly RegistroDeMortos _mortos = new();

	/// <summary>
	/// ============================ ELE PRECISA ENTRAR NA LISTA DO `CharacterStore` ============================
	/// Este arquivo mora na MESMA pasta das contas, e o `CharacterStore` monta o nome de uma conta
	/// como "conta saneada + .json". Sem a entrada em `CharacterStore.NaoSaoContas`, o painel de admin
	/// cuspiria "conta ilegivel" a cada abertura E uma conta chamada "planetas mortos" gravaria por
	/// cima do estado do mundo. Ja esta la -- e esta nota existe pra que ninguem tire.
	/// ================================================================================================
	/// </summary>
	private string CaminhoDosMortos => System.IO.Path.Combine(_store?.Pasta ?? ".", "planetas-mortos.json");

	/// <summary>O relogio dos tremores da explosao, por chave de planeta. NAO persiste -- e cadencia visual.</summary>
	private readonly Dictionary<string, double> _proximoTremor = [];

	/// <summary>
	/// A CARGA DE 30 s DO VERB `Planet_Destroy`, por jogador -- `sleep(300)` (`Planets.dm:355`).
	///
	/// NAO E FASE DO PLANETA, e essa distincao e do original: durante a carga o `DestroyPlanet` ainda
	/// nao foi chamado e a area nao tem estado nenhum. Se quem carrega cai, **nada aconteceu com o
	/// planeta**. Por isso mora aqui, no ator, e nao no registro de mortos.
	/// </summary>
	private readonly Dictionary<int, (double Faltam, ZoneKey Zona, double Bp)> _cargaDoPlanetDestroy = [];

	// =====================================================================
	// CARGA E SALVAMENTO
	// =====================================================================
	private void CarregarPlanetasMortos()
	{
		try
		{
			if (!System.IO.File.Exists(CaminhoDosMortos)) return;

			var lista = JsonSerializer.Deserialize<List<EstadoDaMorte>>(
				System.IO.File.ReadAllText(CaminhoDosMortos),
				new JsonSerializerOptions { IncludeFields = true });
			if (lista == null) return;

			foreach (EstadoDaMorte e in lista)
			{
				// ============================ ESTADO IMPOSSIVEL E DESCARTADO, E GRITA ============================
				// Mesma disciplina do `CarregarSagas`: um registro sem chave, ou com uma fase que nao
				// existe, e save corrompido -- e o silencio esconderia um planeta que morreu por engano.
				// ==========================================================================================
				if (e.Chave.Length == 0)
				{
					GD.PushWarning("[server] planetas-mortos.json: registro sem chave -- descartado");
					continue;
				}
				if (e.Fase == FaseDaMorte.Vivo)
				{
					GD.PushWarning($"[server] planetas-mortos.json: '{e.Nome}' esta no arquivo como VIVO "
								 + "-- ausencia e a resposta pra vivo; registro descartado");
					continue;
				}
				_mortos.Por(e);
			}

			int destruidos = 0, morrendo = 0;
			foreach (EstadoDaMorte e in _mortos.Todos)
				if (MortePlanetaria.EstaMorto(e.Fase)) destruidos++; else morrendo++;

			GD.Print($"[server] planetas mortos: {destruidos} destruido(s), {morrendo} em curso "
				   + $"({string.Join(", ", _mortos.Todos.Select(e => $"{e.Nome}={e.Fase}"))})");
		}
		catch (Exception e) { GD.PushWarning($"[server] planetas-mortos.json ilegivel: {e.Message}"); }
	}

	/// <summary>
	/// GRAVA O LIVRO INTEIRO, numa escrita atomica (`.tmp` + `File.Move`). Mesmo desenho do
	/// `SalvarSagas` -- e a mesma razao: uma escrita cortada no meio custaria o estado do mundo.
	///
	/// E grava **tudo de uma vez** de proposito. Gravar campo a campo, ou so "o que mudou", e
	/// exatamente como o original se meteu na armadilha de metade do estado sobreviver ao boot.
	/// </summary>
	private void SalvarPlanetasMortos()
	{
		if (_store == null) return;
		try
		{
			string tmp = CaminhoDosMortos + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(
				_mortos.Todos.ToList(),
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDosMortos, overwrite: true);
		}
		catch (Exception e)
		{
			GD.PushWarning($"[server] nao deu pra salvar planetas-mortos.json: {e.Message}");
		}
	}

	// =====================================================================
	// AS PERGUNTAS QUE O RESTO DO JOGO FAZ
	// =====================================================================
	/// <summary>Esta zona e a superficie de um planeta DESTRUIDO? (`testPlanetbump`, `Planets.dm:178`).</summary>
	public bool ZonaMorta(ZoneKey z) => _mortos.Morto(z);

	/// <summary>Este corpo do mapa do universo esta destruido? (`if(D.pobj && D.pobj.isDestroyed)`).</summary>
	public bool PlanetaMorto(PlanetaNoEspaco p) => _mortos.Morto(p);

	/// <summary>Ha morte em curso ou consumada aqui -- o `planet_dying` do DM.</summary>
	public bool ZonaCondenada(ZoneKey z) => _mortos.Condenado(z);

	/// <summary>O estado de morte de uma zona (nulo = viva). Publico pra bancada e pro painel.</summary>
	public EstadoDaMorte? MorteDaZona(ZoneKey z) => _mortos.De(z);

	/// <summary>Quantos planetas ja morreram de vez.</summary>
	public int PlanetasDestruidos => _mortos.Todos.Count(e => MortePlanetaria.EstaMorto(e.Fase));

	/// <summary>
	/// TEM ALGUEM ONLINE? A pergunta do "servidor vazio adia".
	///
	/// **Vivo e com teclado**: o `!p.Ficha.dead` esta aqui pela mesma razao do ultimato das sagas --
	/// um servidor em que todo mundo esta no Outro Mundo nao tem ninguem pra impedir nada.
	///
	/// E "com teclado" e o crivo unico (`Gente.EhJogador`) e nao `Peer != null`: os 148 habitantes do
	/// povoamento nao sao alguem pra impedir nada, e o boneco de quem esta meditando ja e contado pelo
	/// proprio dono.
	/// </summary>
	private bool AlguemOnline() =>
		_players.Values.Any(p => EhJogador(p) && !p.Ficha.dead);

	// =====================================================================
	// (a) A MORTE LENTA -- `Planet_Death`
	// =====================================================================
	/// <summary>
	/// ACENDE O PAVIO LENTO. Devolve falso quando nao ha o que acender.
	///
	/// No DM quem chama isto e a colheita da Tree of Might (`Plants.dm:647-661`) -- e o `Ticker` da
	/// area, que e o retomador defeituoso. Aqui a arvore ainda nao foi portada; os chamadores hoje
	/// sao o verb de admin e a bancada, e o gancho fica pronto pra ela.
	///
	/// **Nao reacende o que ja esta aceso** -- e o `if(death_proc_running) return` (`:10`), que no
	/// original existe porque o Ticker e a propagacao entre areas irmas criavam instancias duplas do
	/// proc. Aqui nao ha areas irmas (a zona e o planeta), mas a guarda vale pelo mesmo motivo: dois
	/// pavios no mesmo planeta correriam em cadencias diferentes.
	/// </summary>
	public bool ComecarMorteLenta(ZoneKey zona, double bpDoAlgoz, string motivo, int idDoAlgoz = 0)
	{
		if (ChaveDePlaneta.Da(zona) is not { } chave)
		{
			GD.PushWarning($"[server] morte lenta pedida em '{zona.Name}', que nao e um planeta");
			return false;
		}
		if (_mortos.De(chave) != null) return false;   // ja esta morrendo, ou ja morreu

		_mortos.Por(new EstadoDaMorte
		{
			Chave = chave.Texto,
			Nome = zona.Name,
			Fase = FaseDaMorte.Morrendo,
			Estagio = 0,
			Faltam = MortePlanetaria.SegundosDoEstagio[0],
			BpDoAlgoz = bpDoAlgoz,
			IdDoAlgoz = idDoAlgoz,
			Motivo = motivo,
		});
		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		// O estagio 0 ja mata a primeira fatia -- `limit_life()` roda no COMECO do estagio
		// (`Area_Death.dm:29`), e nao no fim dele.
		LimitarVida(zona, 0);

		AnunciarNoPlaneta(zona, $"Algo está errado com {zona.Name}. O ar ficou pesado, "
							  + "e os animais sumiram.");
		GD.Print($"[server] MORTE LENTA acesa em '{zona.Name}' ({motivo}) -- "
			   + $"{MortePlanetaria.PavioInteiro:0}s ate a destruicao");
		return true;
	}

	/// <summary>
	/// O `limit_life()` (`Area_Death.dm:52-55`): `prob((estagio+1)*25)` de cada NPC daqui morrer.
	///
	/// **Pela porta unica.** O DM chama `M.mobDeath()` direto; aqui a morte de um corpo sem dono
	/// passa pelo `CombatState.Morrer()` como a de qualquer um -- e um NPC que tenha um seguro
	/// pendurado (a Aura of Destruction, hoje o unico) sobrevive ao estagio, o que e a resposta
	/// certa e nao um caso especial.
	/// </summary>
	private void LimitarVida(ZoneKey zona, int estagio)
	{
		double chance = MortePlanetaria.ChanceDeMorrerPct(estagio);
		int mortos = 0;

		// COPIA: `Morrer` mexe no mundo, e o `RemoverNpc` da morte tira o corpo da lista da zona.
		foreach (ServerPlayer npc in ZoneList(zona.Hash).ToList())
		{
			// `if(M.isNPC)` (`:54`). O `Papel` e o `isNPC` deste port -- e ele exclui de graca o
			// clone da mente e o corpo de bancada, que tambem tem `Peer` nulo e nao moram aqui.
			if (npc.Papel == null) continue;
			if (npc.Ficha.dead || npc.Combate == null) continue;

			// O HABITANTE INTOCAVEL NAO ENTRA NO SORTEIO. Terceira morte deste arquivo que nao passa
			// por dano nenhum -- e a mais traicoeira das tres, porque e SORTEADA: um NPC transformando
			// morreria aqui em 25% a 100% dos estagios, e o mesmo teste rodado de novo daria outro
			// resultado. Ver o `protegido` do `ConcluirDestruicao`, mesma regra e mesma razao.
			if (npc.Combate.Intocavel) continue;

			if (_rng.NextDouble() * 100 >= chance) continue;

			if (!npc.Combate.Morrer()) continue;
			mortos++;
		}

		if (mortos > 0)
			GD.Print($"[server] '{zona.Name}' estagio {estagio}: {mortos} habitante(s) morreram "
				   + $"(chance {chance:0}%)");
	}

	/// <summary>
	/// O `death_turf_destroy()` (`Area_Death.dm:57-74`): **um pedaco de chao por jogador**, de tempos
	/// em tempos, perto de quem esta olhando.
	///
	/// O comentario do original explica a escolha melhor do que eu conseguiria: *"why affect land
	/// that won't affect players?"*. Aqui isso vale duas vezes, porque cada celula derrubada e um
	/// pacote **confiavel** por pessoa na zona (`MandarCelulaCaida`): "os turfs viram Stars" num mapa
	/// 500x500 seriam dezenas de milhares deles.
	///
	/// Reusa o `DerrubarCelula` da destruicao de cenario, que ja sabe abrir a colisao dos dois lados
	/// e ja tem a guarda de borda. Um segundo caminho pro chao cair divergiria do primeiro.
	/// </summary>
	private void QuebrarChaoPertoDosJogadores(ZoneKey zona)
	{
		foreach (ServerPlayer pl in ZoneList(zona.Hash).ToList())
		{
			if (pl.Peer == null) continue;

			int cx = (int)(pl.Pos.X / ZoneCollision.TileSize) + _rng.Next(-10, 11);
			int cy = (int)(pl.Pos.Y / ZoneCollision.TileSize) + _rng.Next(-10, 11);
			if (cx < 0 || cy < 0) continue;

			// UMA por volta, como no original (o `break` do `:74`). O sorteio pode nao achar celula
			// derrubavel, e tudo bem -- na proxima volta ele tenta de novo.
			DerrubarCelula(zona, cx, cy);
		}
	}

	// =====================================================================
	// (b) A DESTRUICAO -- `DestroyPlanet`
	// =====================================================================
	/// <summary>
	/// ============================ COMECA OS ~5 MINUTOS ============================
	/// E o `area/proc/DestroyPlanet(mexpressedBP)` (`Area_Death.dm:75-147`), do `Quake()` inicial ate
	/// o `sleep(3100)`. O commit vem depois, no <see cref="ConsumarDestruicao"/>.
	///
	/// **O BP DO ALGOZ E FIXADO AQUI** -- `var/mexpressedBP = usr.expressedBP` (`Planets.dm:342`), e
	/// e ele que decide quem sobrevive ao fim. Mesma disciplina do BP pinado das sagas: entre o
	/// comeco e o commit passam cinco minutos, e sem o pino quem se transformasse no meio mudaria
	/// retroativamente quem morre.
	///
	/// **Nao ha volta a partir daqui** a nao ser pelo <see cref="AbortarMorte"/> (admin), e e isso
	/// que o `isBeingDestroyed = 1` do original quer dizer.
	/// ==============================================================
	/// </summary>
	public bool ComecarDestruicao(ZoneKey zona, double bpDoAlgoz, string motivo, int idDoAlgoz = 0)
	{
		if (ChaveDePlaneta.Da(zona) is not { } chave)
		{
			GD.PushWarning($"[server] destruicao pedida em '{zona.Name}', que nao e um planeta");
			return false;
		}

		EstadoDaMorte? e = _mortos.De(chave);
		if (e is { Fase: FaseDaMorte.Explodindo or FaseDaMorte.Destruido }) return false;

		e ??= new EstadoDaMorte { Chave = chave.Texto, Nome = zona.Name };
		e.Fase = FaseDaMorte.Explodindo;
		e.Estagio = MortePlanetaria.UltimoEstagio + 1;   // o `planet_death_stage = 4` do `:78`
		e.Faltam = MortePlanetaria.SegundosDeExplosao;
		e.BpDoAlgoz = bpDoAlgoz;
		e.IdDoAlgoz = idDoAlgoz;
		e.Motivo = motivo;
		_mortos.Por(e);
		_proximoTremor[e.Chave] = 0;

		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		// O CEU VIRA "Destruction" -- `IsWeathering = 1; currentWeather = "Destruction"` (`:94-95`).
		// Passa pela porta unica do clima, que e publica e ja citava este uso pelo nome.
		//
		// A DURACAO PEDE UM POUCO A MAIS que a explosao: o clima forcado sai suave (45 s de
		// transicao, ver `Clima.De`), e um prazo exato faria o ceu comecar a clarear justamente no
		// minuto final -- o mundo acabaria com o sol voltando.
		ForcarClima(zona, TipoDeClima.Destruicao, MortePlanetaria.SegundosDeExplosao + 60, 1,
					$"destruicao de {zona.Name}");

		foreach (ServerPlayer pl in ZoneList(zona.Hash))
		{
			if (pl.Peer == null) continue;
			Avisar(pl, "*O PLANETA COMEÇA A SE PARTIR AO SEU REDOR!!*");
			MandarEfeito(pl, "terremoto", 1800);
		}

		AnunciarNoMundo($"{zona.Name} está se despedaçando. Quem estiver lá tem minutos.");
		GD.Print($"[server] DESTRUICAO de '{zona.Name}' comecou ({motivo}); BP do algoz fixado em "
			   + $"{bpDoAlgoz:0}; {MortePlanetaria.SegundosDeExplosao:0}s ate o fim");
		return true;
	}

	/// <summary>
	/// ============================ O COMMIT -- `Area_Death.dm:130-147` ============================
	/// O que acontece, na ordem do original:
	///   1. quem ja esta morto ou saiu do planeta e PULADO (`:132-133`, o fix do "chovia explosao no
	///      z6": a lista de jogadores da area do DM e pegajosa);
	///   2. todo NPC daqui morre, sem chance (`M.buudead = "force"; M.Death()`, `:134-136`);
	///   3. quem tem `expressedBP <= mexpressedBP` leva **99 de dano em cada membro** (`:137-139`);
	///   4. quem estava NOCAUTEADO e nao morreu ainda **morre de verdade** (`:140-142`) -- o
	///      comentario do original diz por que: antes ele ficava "jogado no espaco, em combate
	///      eterno, sem nunca morrer";
	///   5. o planeta entra na lista e vira `isDestroyed` (`:143-145`);
	///   6. quem sobrou e jogado pro espaco (no DM isso e um tique de `Stats.dm:427-434`; aqui e
	///      feito aqui, ver <see cref="EvacuarParaOEspaco"/>).
	///
	/// **TUDO PELA PORTA UNICA.** O dano vai por `EspalharDanoG3` (o `SpreadDamage` deste port) e a
	/// morte por `CombatState.Morrer()`, que e onde o seguro da Aura of Destruction se pendura. Um
	/// caminho proprio aqui faria a unica morte do jogo que ignora o seguro ser justamente a maior.
	/// ======================================================================================
	/// </summary>
	private void ConsumarDestruicao(EstadoDaMorte e, ZoneKey zona)
	{
		e.Fase = FaseDaMorte.Destruido;
		e.Faltam = 0;
		_proximoTremor.Remove(e.Chave);

		// O ALGOZ RESOLVIDO -- ou `null`. O id e uma PISTA e nao identidade: cinco minutos depois ele
		// pode ter deslogado, morrido ou (no caso da saga) sido removido do mundo. Sem algoz o dano
		// entra como dano SEM autor, que e o mesmo caminho que a Final Explosion ja usa pra ferir
		// quem a soltou -- ninguem vira heroi nem leva Zenkai por um planeta que explodiu.
		ServerPlayer? algoz = e.IdDoAlgoz != 0 && _players.TryGetValue(e.IdDoAlgoz, out ServerPlayer? a)
							  && !a.Ficha.dead ? a : null;

		int npcsMortos = 0, feridos = 0, mortos = 0, evacuados = 0;

		foreach (ServerPlayer pl in ZoneList(zona.Hash).ToList())
		{
			if (pl.Ficha.dead) continue;                      // `:132` -- ja esta no Outro Mundo
			if (!pl.Zone.Equals(zona)) continue;              // `:133` -- saiu antes do fim
			if (pl.Combate == null) continue;

			// ============================ O CORPO INTOCAVEL ATRAVESSA O FIM DO MUNDO ============================
			// Aqui ha DUAS mortes que nao passam por dano nenhum -- o habitante que morre direto
			// (`:134-136` do DM, sem checagem de BP) e o nocauteado que morre depois do estrago -- e por
			// isso nem o funil do `CombatState.Ferir` nem o crivo do `EspalharDanoG3` as alcancam. Sem
			// esta bandeira, a explosao do planeta seria o UNICO jeito de matar quem esta transformando,
			// e o NPC morreria por ela sem sequer um teste de poder: o "as vezes" mais dificil de
			// reproduzir do jogo, porque exige dois eventos raros no mesmo instante.
			//
			// Vale tambem pra carencia de renascimento, que e a outra metade do `Intocavel`: renascer no
			// planeta no segundo em que ele estoura ja era uma morte que o escudo devia ter barrado.
			//
			// **BANDEIRA E NAO `continue`**: quem sobrevive ao fim do mundo ainda tem que SUBIR
			// (`EvacuarParaOEspaco`, la embaixo). Pular a volta inteira deixaria o transformado de pe num
			// planeta que nao existe mais -- trocar uma morte injusta por um corpo preso no nada.
			// ==============================================================================================
			bool protegido = pl.Combate.Intocavel;

			// ============================ 2. O HABITANTE NAO SOBREVIVE ============================
			// `if(M.isNPC) { M.buudead = "force"; M.Death() }` (`:134-136`) -- **sem checagem de BP**.
			// O cidadao de Vegeta com BP de milhoes morre igual ao de BP 3.
			//
			// O crivo e o `Papel` e nao o `Peer`, e a diferenca importa: `Peer == null` tambem pega o
			// clone da mente e os corpos de bancada, que nao sao habitantes de lugar nenhum. `Papel`
			// e o `isNPC` deste port.
			// ================================================================================
			if (pl.Papel != null)
			{
				if (!protegido && pl.Combate.Morrer()) npcsMortos++;
				continue;
			}

			// 3. `if(M.expressedBP<=mexpressedBP) M.SpreadDamage(99)`.
			//
			// QUEM ESTA ACIMA DO ALGOZ ATRAVESSA O FIM DO MUNDO ILESO, e isso e literal: e a regra
			// que faz o Planet Destroy ser uma ameaca aos FRACOS e nao um botao que mata todo mundo.
			if (!protegido && pl.Ficha.expressedBP <= e.BpDoAlgoz)
			{
				EspalharDanoG3(pl, algoz ?? pl, MortePlanetaria.DanoDoCommit, letal: true);
				Avisar(pl, "a explosão do planeta rasga você inteiro.");
				feridos++;

				// 4. NOCAUTEADO MORRE. Depois do dano, e conferindo `dead` de novo: o `SpreadDamage`
				// acima ja pode ter matado, e chamar `Morrer()` duas vezes contaria duas mortes.
				if (pl.Ficha.KO && !pl.Ficha.dead && pl.Combate.Morrer())
				{
					mortos++;
					continue;
				}
			}

			if (pl.Ficha.dead) { mortos++; continue; }

			// 6. QUEM SOBROU SOBE.
			EvacuarParaOEspaco(pl, zona);
			evacuados++;
		}

		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		// O CEU FORCADO SAI JUNTO: nao ha mais planeta pra ter ceu, e deixar o clima pendurado num
		// hash de zona morta seria estado orfao vivendo pra sempre em memoria.
		ForcarClima(zona, TipoDeClima.Limpo, 0);

		AnunciarNoMundo($"{zona.Name} NÃO EXISTE MAIS.");
		GD.Print($"[server] '{zona.Name}' DESTRUIDO ({e.Motivo}): {npcsMortos} habitante(s) mortos, "
			   + $"{feridos} ferido(s), {mortos} morto(s), {evacuados} evacuado(s); "
			   + $"BP do algoz {e.BpDoAlgoz:0}");
	}

	/// <summary>
	/// ============================ A EVACUACAO ============================
	/// No DM ela e um tique de `Stats` (`Stats.dm:427-434`): enquanto a area estiver destruida, todo
	/// mob com `client` e sorteado pra um turf em `view(1, P)` -- ou seja, **orbita**, repetindo pra
	/// sempre. E a rede que impede alguem de voltar.
	///
	/// Aqui e o caminho do DECOLAR, que ja existe inteiro e ja esta certo (`GameServer.Espaco.cs:67`).
	/// Os tres carimbos que um teleporte ingenuo esqueceria, e que sao o motivo de isto nao ser um
	/// `MoveToZone` solto:
	///   * `PlanetaDeOrigem` -- pra onde a nave "volta";
	///   * `ChunkAtual` -- sem ele o tique do espaco veria "mudou de chunk" no quadro seguinte e
	///     mandaria a vizinhanca DUPLICADA;
	///   * `MandarVizinhanca` forcado -- sem ele a pessoa chega ao espaco **sem nenhum planeta
	///     desenhado**, inclusive sem o cadaver do que ela acabou de deixar.
	///
	/// E o ponto de chegada e o `PontoDeDecolagem`, que fica FORA do raio do disco -- entao o teste
	/// de pouso nao dispara no mesmo quadro e ninguem cai de volta. O `pspace_noland_until` do DM
	/// (`NewTurfs.dm:57`) nao foi portado e, por esta rota, nao faz falta: quem impede a volta e o
	/// filtro de planeta morto no <see cref="TickDoEspaco"/>.
	/// ====================================================
	/// </summary>
	private void EvacuarParaOEspaco(ServerPlayer pl, ZoneKey zona)
	{
		PlanetaNoEspaco? corpo = null;

		// O planeta gerado sabe onde fica pelo registro de zonas vivas; o pre-feito, pela carta.
		if (EhZonaProcedural(zona) && _zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? viva))
			corpo = viva.NoEspaco;
		else
			foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
				if (string.Equals(p.Nome, zona.Name, StringComparison.OrdinalIgnoreCase)) { corpo = p; break; }

		if (corpo is not { } onde)
		{
			// SEM LUGAR NO UNIVERSO: o berco e a resposta, e nao "fica onde esta". Um corpo numa zona
			// morta ficaria preso num planeta que nao existe mais.
			GD.PushWarning($"[server] '{zona.Name}' nao esta no mapa do universo -- {pl.Name} vai pro berco");
			MandarProBerco(pl);
			return;
		}

		pl.PlanetaDeOrigem = onde.Nome;
		MoveToZone(pl.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(onde));
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);
		Avisar(pl, $"o chão some debaixo de você. {zona.Name} vira pó, e você fica no vácuo.");
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O TIQUE DA MORTE DE PLANETA. Roda no bloco de 1 Hz, junto do das sagas.
	///
	/// **1 Hz basta e 30 Hz seria desperdicio**: o passo mais curto deste sistema sao os 3 s entre
	/// dois tremores, e os estagios sao medidos em MINUTOS. O laco varre so o que esta no registro
	/// (planeta vivo nao aparece nele), entao num servidor sem nenhuma morte em curso ele e uma
	/// comparacao de contador.
	/// </summary>
	private void TickDaDestruicao(double dt)
	{
		TickDaCargaDeDestruicao(dt);
		if (_mortos.Quantos == 0) return;

		bool mudou = false;
		foreach (EstadoDaMorte e in _mortos.Todos.ToList())
		{
			if (MortePlanetaria.EstaMorto(e.Fase)) continue;

			ZoneKey zona = e.Zona();
			switch (e.Fase)
			{
				case FaseDaMorte.Morrendo:
					// SERVIDOR VAZIO ADIA -- ver o cabecalho deste arquivo.
					if (!AlguemOnline()) continue;
					if (PassoDaMorteLenta(e, zona)) mudou = true;
					break;

				case FaseDaMorte.Explodindo:
					// A EXPLOSAO NAO ESPERA NINGUEM. Ver o cabecalho.
					TremorDaExplosao(e, zona, dt);
					e.Faltam -= dt;
					if (e.Faltam <= 0) { ConsumarDestruicao(e, zona); return; }
					break;
			}
		}

		// SALVA UMA VEZ POR TIQUE, e so quando ALGO MUDOU DE ESTAGIO. O `Faltam` desce a cada
		// segundo, e gravar por isso seria uma escrita em disco por segundo por planeta morrendo --
		// pra proteger, no pior caso, um segundo de pavio. O que nao pode se perder e o ESTAGIO.
		if (mudou) { SalvarPlanetasMortos(); MandarMortosPraTodos(); }
	}

	/// <summary>
	/// UM SEGUNDO DO PAVIO LENTO. Devolve verdadeiro quando o ESTAGIO mudou (o que pede save).
	///
	/// A ordem e a do `switch` do original (`Area_Death.dm:27-45`): o estagio faz o que tem que
	/// fazer, dorme o tempo dele, e so entao sobe.
	/// </summary>
	private bool PassoDaMorteLenta(EstadoDaMorte e, ZoneKey zona)
	{
		// O CHAO SE DESFAZENDO acontece DENTRO do estagio, e nao na virada dele -- e um laco proprio
		// no original (`spawn while(...)`, `:62`), com 7 a 15 s entre uma celula e outra.
		if (e.Estagio >= MortePlanetaria.EstagioQueQuebraOChao)
		{
			double proximo = _proximoTremor.GetValueOrDefault(e.Chave);
			if (proximo <= 0)
				_proximoTremor[e.Chave] = 7 + _rng.NextDouble() * 8;
			else if ((_proximoTremor[e.Chave] = proximo - 1) <= 0)
				QuebrarChaoPertoDosJogadores(zona);
		}

		if ((e.Faltam -= 1) > 0) return false;

		e.Estagio++;

		// `if(planet_death_stage==4) goto destroy` (`:46`) -- o pavio acabou, a destruicao comeca.
		if (e.Estagio > MortePlanetaria.UltimoEstagio)
		{
			ComecarDestruicao(zona, e.BpDoAlgoz, e.Motivo + " (pavio lento)", e.IdDoAlgoz);
			return false;   // o `ComecarDestruicao` ja salvou e ja avisou
		}

		e.Faltam = MortePlanetaria.SegundosDoEstagio[e.Estagio];
		LimitarVida(zona, e.Estagio);

		// ============================ A NOITE QUE NAO ACABA ============================
		// `Weather.dm:166-167`: com `planet_death_stage` entre 1 e 3, o `daylightcycle` e forcado
		// pra >= 6, ou seja, escuro pra sempre. O ceu deste port e funcao pura do tempo e nao tem
		// como ser forcado por planeta -- entao o equivalente honesto e o CLIMA, que tem gancho: um
		// ceu de destruicao permanente encobre 0,98, que apaga o dia tao bem quanto a noite do DM.
		//
		// O que se perde: a lua nao some (ela e do relogio, nao do clima), entao um Saiyajin ainda
		// pode virar Oozaru num planeta agonizando. O que se ganha: nao ha um segundo relogio de
		// dia/noite por zona, que e a coisa que a regra 0.2 proibe.
		// ===========================================================
		if (e.Estagio >= MortePlanetaria.EstagioDaNoiteEterna)
			ForcarClima(zona, TipoDeClima.Destruicao,
						MortePlanetaria.SegundosDoEstagio[e.Estagio] + 60, 0.55,
						$"{zona.Name} agonizando");

		AnunciarNoPlaneta(zona, e.Estagio switch
		{
			1 => $"O sol de {zona.Name} não nasce mais. O céu ficou da cor de ferrugem.",
			2 => $"O chão de {zona.Name} começa a se abrir sozinho.",
			_ => $"Não sobrou quase ninguém em {zona.Name}. O planeta está morrendo.",
		});
		GD.Print($"[server] '{zona.Name}' entrou no estagio {e.Estagio} da morte lenta "
			   + $"({MortePlanetaria.SegundosDoEstagio[e.Estagio]:0}s)");
		return true;
	}

	/// <summary>
	/// O TREMOR E A EXPLOSAO da fase final -- `Area_Death.dm:96-116`, com o `sleep(20 + rand(10,90))`
	/// (3 a 11 s) e o `pick(1,2,3)`.
	///
	/// O `if(4)` do original e **inalcancavel** (`pick(1,2,3)` nunca sorteia 4) e por isso os efeitos
	/// dele -- poeira, relampago no chao, cratera -- nunca rodaram no jogo de verdade. Nao portei um
	/// ramo morto: o que existe aqui sao os tres que existiam.
	/// </summary>
	private void TremorDaExplosao(EstadoDaMorte e, ZoneKey zona, double dt)
	{
		double falta = _proximoTremor.GetValueOrDefault(e.Chave) - dt;
		if (falta > 0) { _proximoTremor[e.Chave] = falta; return; }

		_proximoTremor[e.Chave] = MortePlanetaria.TremorMin
								+ _rng.NextDouble() * (MortePlanetaria.TremorMax - MortePlanetaria.TremorMin);

		foreach (ServerPlayer pl in ZoneList(zona.Hash).ToList())
		{
			if (pl.Peer == null || pl.Ficha.dead) continue;   // `:98`

			MandarEfeito(pl, "terremoto", 1200);

			switch (_rng.Next(1, 4))
			{
				case 1:
					Avisar(pl, "um estrondo desce das nuvens.");
					break;

				// `spawnExplosion(randturf, null, mexpressedBP/100, 5)` -- explosao PERTO, nao em
				// cima. O port nao tem explosao de cenario com raio; o que existe e o efeito de tela
				// e o chao caindo, e os dois juntos leem como a mesma coisa.
				case 2:
					MandarEfeito(pl, "explosao_final", 500);
					QuebrarChaoPertoDosJogadores(zona);
					break;

				// `T.Destroy()` e `prob(50)` de virar `/turf/Other/Stars` -- o chao virando vacuo.
				case 3:
					QuebrarChaoPertoDosJogadores(zona);
					break;
			}
		}
	}

	/// <summary>
	/// A CARGA DE 30 s DO VERB. Um relogio por pessoa, e ele **e interrompivel**.
	///
	/// ============================ ONDE ISTO DIVERGE DO ORIGINAL, E POR QUE ============================
	/// No DM a carga e um `sleep(300)` cru (`Planets.dm:355`): nada a interrompe -- nem morrer. Aqui
	/// ela cai se quem carrega for nocauteado, morrer ou sair do planeta.
	///
	/// **O que se ganha**: os trinta segundos viram uma JANELA, que e exatamente o desenho que o
	/// proprio original escolheu pro ultimato de chefe (`BEV_PD_CHARGE`, `BossEvents.dm:29`, com o
	/// comentario *"janela p/ matar/nocautear e interromper"*). Ter dois trinta-segundos com regras
	/// opostas no mesmo jogo seria o defeito, nao a fidelidade.
	/// **O que se perde**: um vilao muito mais forte que todo mundo perde a garantia de que o planeta
	/// explode -- ele agora precisa sobreviver meio minuto. O que, sendo ele muito mais forte, ele faz.
	/// ==========================================================================================
	/// </summary>
	private void TickDaCargaDeDestruicao(double dt)
	{
		if (_cargaDoPlanetDestroy.Count == 0) return;

		foreach (int id in _cargaDoPlanetDestroy.Keys.ToList())
		{
			(double faltam, ZoneKey zona, double bp) = _cargaDoPlanetDestroy[id];

			if (!_players.TryGetValue(id, out ServerPlayer? pl))
			{
				_cargaDoPlanetDestroy.Remove(id);
				continue;
			}

			if (pl.Ficha.KO || pl.Ficha.dead || !pl.Zone.Equals(zona))
			{
				_cargaDoPlanetDestroy.Remove(id);
				MandarEfeito(pl, "carga_final", 0);
				Avisar(pl, "a energia que você juntava se desfaz.");
				AnunciarNoPlaneta(zona, $"A energia que {pl.Name} juntava sobre {zona.Name} SE DESFEZ!");
				continue;
			}

			faltam -= dt;
			if (faltam > 0) { _cargaDoPlanetDestroy[id] = (faltam, zona, bp); continue; }

			_cargaDoPlanetDestroy.Remove(id);
			MandarEfeito(pl, "carga_final", 0);
			ComecarDestruicao(zona, bp, $"Planet Destroy de {pl.Name}", pl.Id);
		}
	}

	// =====================================================================
	// O VERB DO JOGADOR -- `Planet_Destroy` (`Planets.dm:318-370`)
	// =====================================================================
	public static void RegistrarTecnicasDaDestruicao() =>
		Tecnicas.Registrar("Planet_Destroy", "Planet Destroy", Modo.Instantanea,
			"Concentra toda a sua energia sobre o planeta em que você está e o parte ao meio. "
			+ "Custa 1000 de Ki, exige BP expresso de 10.000 vezes a gravidade daqui, e leva trinta "
			+ "segundos de carga -- se você for nocauteado nesse meio-tempo, não acontece nada. "
			+ "Depois disso o planeta tem cinco minutos, e some do mapa PARA SEMPRE. Só um vilão.");

	/// <summary>
	/// O VERB, literal onde da e explicito onde nao da.
	///
	/// A ORDEM DAS RECUSAS E A DO ORIGINAL:
	///   1. `if(!usr.isVillain)` (`:323`) -- ver <see cref="EhVilao"/>;
	///   2. `Ki >= 1000 && expressedBP >= 10000 * Planetgrav` (`:326`);
	///   3. o lugar tem que ser um planeta destruivel, e nao o espaco (`:331`);
	///   4. o Ki e cobrado ANTES da confirmacao (`:338`).
	///
	/// ============================ A CAIXA DE DIALOGO NAO FOI PORTADA ============================
	/// O original abre um `input("Destroy this Planet?")` (`:339`) **depois** de cobrar os 1000 de Ki
	/// -- ou seja, clicar "No" custa mil de Ki e nao faz nada. Nao portei o defeito nem a caixa: o
	/// canal de habilidade deste port manda um id e mais nada, e a confirmacao virou os trinta
	/// segundos de carga, que sao publicos e canceláveis. Quem apertou sem querer e nocauteado pelos
	/// outros -- o que e uma confirmacao melhor do que um botao.
	/// ====================================================================================
	/// </summary>
	private void PlanetDestroy(ServerPlayer pl)
	{
		if (_cargaDoPlanetDestroy.ContainsKey(pl.Id))
		{
			Avisar(pl, "você já está juntando essa energia.");
			return;
		}

		// 1. SO VILAO.
		if (!EhVilao(pl))
		{
			Avisar(pl, "só um vilão tem vontade de arrasar um planeta.");
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condições."); return; }

		// 3. O LUGAR. (Antes do custo, porque recusar por lugar e mais barato que recusar por Ki, e
		// porque cobrar Ki de quem esta no espaco seria cobrar por nada.)
		ZoneKey zona = pl.Zone;
		if (ChaveDePlaneta.Da(zona) is not { } chave)
		{
			Avisar(pl, "não dá pra usar Planet Destroy aqui.");
			return;
		}
		if (_mortos.De(chave) != null)
		{
			Avisar(pl, $"{zona.Name} já está condenado.");
			return;
		}

		// 2. O CUSTO -- os dois numeros do `1A Defines` do original.
		double bpExigido = MortePlanetaria.BpExigido(pl.Ficha.Planetgrav);
		if (pl.Ficha.Ki < MortePlanetaria.KiDaDestruicao || pl.Ficha.expressedBP < bpExigido)
		{
			Avisar(pl, $"você não tem energia. São {MortePlanetaria.KiDaDestruicao:0} de Ki e "
					   + $"{bpExigido:0} de BP expresso aqui (a gravidade deste planeta é "
					   + $"{pl.Ficha.Planetgrav:0.##}x). Você tem {pl.Ficha.Ki:0} de Ki e "
					   + $"{pl.Ficha.expressedBP:0} de BP.");
			return;
		}

		// 4. O KI SAI AGORA (`:338`).
		pl.Ficha.Ki -= MortePlanetaria.KiDaDestruicao;

		// 6. `var/mexpressedBP = usr.expressedBP` (`:342`) -- fixado AQUI, antes da carga.
		_cargaDoPlanetDestroy[pl.Id] = (MortePlanetaria.SegundosDeCarga, zona, pl.Ficha.expressedBP);
		MandarEfeito(pl, "carga_final", -1);

		Avisar(pl, $"você começa a concentrar tudo o que tem sobre {zona.Name}. Trinta segundos.");
		AnunciarNoPlaneta(zona, $"*{pl.Name} está concentrando energia para DESTRUIR {zona.Name}!* "
							  + "Nocauteiem-no em 30 SEGUNDOS!");
		GD.Print($"[server] {pl.Name} comecou a carga do Planet Destroy em '{zona.Name}' "
			   + $"(BP fixado em {pl.Ficha.expressedBP:0})");
	}

	/// <summary>
	/// ============================ QUEM E VILAO NESTE PORT ============================
	/// O DM responde com um `mob/var/isVillain`, e o comentario da propria skill diz como ele e
	/// preenchido: *"only an admin-designated Villain can learn it"* (`Planets.dm:382`). Ou seja: **a
	/// designacao e um ato de admin**, e nao uma consequencia do que a pessoa fez.
	///
	/// Portei isso literalmente: `Fighter.isVillain`, persistido junto da ficha, escrito pelo verb
	/// `admin_vilao`. As alternativas foram consideradas e recusadas:
	///   * **reputacao <= -30** (`Reputacao.LimiarDeVilao`) -- e por PLANETA, e faria "ser inimigo do
	///     povo da Terra" abrir a arma que apaga a Terra. Um ciclo, e um que o DM nao tem.
	///   * **karma / PK** -- o sistema de karma nao foi portado.
	///
	/// **Isto e a unica skill `vilao: 1` do catalogo inteiro** (medido: 1 de 366 entradas do
	/// `skills.json`). Entao "quem e vilao" nao e uma pergunta de sistema social -- e o interruptor
	/// desta arma, e so dela.
	/// ==================================================================
	/// </summary>
	public static bool EhVilao(ServerPlayer pl) => pl.Ficha.isVillain;

	// =====================================================================
	// DESFAZER -- e por que so ha UMA porta
	// =====================================================================
	/// <summary>
	/// INTERROMPE UMA MORTE EM CURSO. E o `abort_planet_destroy` do original
	/// (`BossEvents.dm:836-855`), que existe porque ate o DM precisou de uma faxina.
	///
	/// **So funciona antes do commit.** Depois de destruido, o caminho e o
	/// <see cref="RessuscitarPlaneta"/>.
	/// </summary>
	public bool AbortarMorte(ZoneKey zona, string motivo)
	{
		if (ChaveDePlaneta.Da(zona) is not { } chave) return false;
		EstadoDaMorte? e = _mortos.De(chave);
		if (e == null || MortePlanetaria.EstaMorto(e.Fase)) return false;

		_mortos.Tirar(chave);
		_proximoTremor.Remove(chave.Texto);
		ForcarClima(zona, TipoDeClima.Limpo, 0);
		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		AnunciarNoPlaneta(zona, $"O céu de {zona.Name} se abre. O planeta vai viver.");
		GD.Print($"[server] morte de '{zona.Name}' ABORTADA ({motivo})");
		return true;
	}

	/// <summary>
	/// ============================ "HEAL PLANET": A DECISAO, ESCRITA ============================
	/// No original ha tres desfazimentos: o desejo de Dragon Ball `"Heal Planet"`
	/// (`WishTable.dm:327-343`), o verb de admin `Restaurar_Planeta` (`Planets.dm:254-280`) e o
	/// `Planet_Options` (`:282-311`).
	///
	/// **O desejo nao existe neste port** -- nao ha Esferas do Dragao, nem coleta, nem invocacao, nem
	/// tabela de desejos; portar o desejo seria portar um sistema do tamanho de uma saga. E o desejo
	/// do original e defeituoso de qualquer jeito: ele zera `isDestroyed` e **nao tira da
	/// `PlanetDisableList`**, entao o planeta volta a morrer no boot seguinte -- foi justamente isso
	/// que o verb de admin, escrito depois com `Save_Settings()`, veio consertar.
	///
	/// Entao a escolha aqui e a **(A)**: destruicao PERMANENTE, com valvula de admin. E a unica das
	/// tres que fecha o sistema sem inventar escopo. A consequencia esta anotada e e real: a saga 1
	/// destroi **Vegeta**, que e o berco dos Saiyajin -- e por isso o berco tem filtro (ver
	/// `GameServer.Berco.cs`), senao um recem-nascido acordaria num cadaver.
	/// ======================================================================================
	/// </summary>
	public bool RessuscitarPlaneta(ZoneKey zona)
	{
		if (ChaveDePlaneta.Da(zona) is not { } chave) return false;
		if (!_mortos.Tirar(chave)) return false;

		_proximoTremor.Remove(chave.Texto);
		SalvarPlanetasMortos();
		MandarMortosPraTodos();

		AnunciarNoMundo($"{zona.Name} voltou a existir.");
		GD.Print($"[server] '{zona.Name}' RESTAURADO -- fora da lista de mortos");
		return true;
	}

	// =====================================================================
	// O CANAL PRO CLIENTE
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE O CLIENTE PRECISA DA LISTA INTEIRA ============================
	/// Um bit no `S2C.Vizinhanca` **nao resolveria**: a carta estelar do cliente **enumera planetas
	/// sozinha**, chamando `Espaco.PreFeitos()` e `Sistemas.Do` direto (`Client/MapaEstelar.cs`), e
	/// ela desenha o que esta a anos-luz da vizinhanca ativa. Sem a lista, ela poria um botao
	/// "Viajar" em cima de um planeta que nao existe mais -- e a carta mentiria.
	///
	/// **E ela e pequena por construcao**: so entra planeta que alguem matou. Num servidor com as
	/// quatro sagas consumadas sao duas ou tres entradas.
	/// ==============================================================================================
	/// </summary>
	internal void MandarMortos(ServerPlayer pl)
	{
		if (pl.Peer == null) return;

		var w = Protocol.Begin(Protocol.S2C.Mortos);
		w.Put((byte)Math.Min(_mortos.Quantos, 255));
		foreach (EstadoDaMorte e in _mortos.Todos.Take(255))
		{
			w.Put(e.Chave);
			w.Put(e.Nome);
			w.Put((byte)e.Fase);
			w.Put((byte)Math.Clamp(e.Estagio, 0, 255));
		}
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private void MandarMortosPraTodos()
	{
		foreach (ServerPlayer p in _players.Values) if (p.Peer != null) MandarMortos(p);
	}

	// =====================================================================
	// OS VERBS DE ADMIN
	// =====================================================================
	/// <summary>
	/// LIGA/DESLIGA O BIT DE VILAO do alvo marcado -- o `isVillain` que so o admin escreve
	/// (`Planets.dm:382`). Sem alvo, em mim mesmo (a convencao dos outros verbs deste arquivo).
	///
	/// **Re-manda as skills forcado**: a lista de compraveis do cliente muda com este bit, e a
	/// assinatura anti-repeticao nao mudaria por conta propria (marcos e aprendidas continuam iguais).
	/// </summary>
	private void AdminVilao(ServerPlayer adm, string arg)
	{
		ServerPlayer alvo = PorNome(arg) ?? adm;
		alvo.Ficha.isVillain = !alvo.Ficha.isVillain;

		MandarSkills(alvo, forcar: true);
		Persistir(alvo);

		Avisar(adm, $"{alvo.Name} {(alvo.Ficha.isVillain ? "AGORA E" : "nao e mais")} um vilao.");
		if (alvo != adm)
			Avisar(alvo, alvo.Ficha.isVillain
				? "algo mudou em voce. O mundo passa a ser um alvo."
				: "a vontade de destruir te abandona.");
	}

	/// <summary>Acende o pavio lento no planeta em que o admin esta -- 20 min ate a explosao.</summary>
	private void AdminMorteLenta(ServerPlayer adm)
	{
		if (!ComecarMorteLenta(adm.Zone, adm.Ficha.expressedBP, $"admin {adm.Name}", adm.Id))
		{
			Avisar(adm, $"nao da pra matar {adm.Zone.Name} devagar (nao e planeta, ou ja esta condenado).");
			return;
		}
		Avisar(adm, $"{adm.Zone.Name} comecou a morrer. {MortePlanetaria.PavioInteiro / 60:0} minutos, "
				  + "quatro estagios, e entao a explosao.");
	}

	/// <summary>Pula o pavio: os cinco minutos e o commit. O BP do commit e o do admin.</summary>
	private void AdminDestruirPlaneta(ServerPlayer adm)
	{
		if (!ComecarDestruicao(adm.Zone, adm.Ficha.expressedBP, $"admin {adm.Name}", adm.Id))
		{
			Avisar(adm, $"nao da pra destruir {adm.Zone.Name} (nao e planeta, ou ja esta explodindo).");
			return;
		}
		Avisar(adm, $"{adm.Zone.Name} tem {MortePlanetaria.SegundosDeExplosao / 60:0.#} minutos. "
				  + $"Quem tiver BP expresso ate {adm.Ficha.expressedBP:0} nao sobrevive.");
	}

	private void AdminAbortarMorte(ServerPlayer adm)
	{
		if (!AbortarMorte(adm.Zone, $"admin {adm.Name}"))
			Avisar(adm, $"{adm.Zone.Name} nao esta morrendo (ou ja morreu -- ai e Restore Planet).");
		else
			Avisar(adm, $"a morte de {adm.Zone.Name} foi interrompida.");
	}

	/// <summary>
	/// O `Restaurar_Planeta` (`Planets.dm:254-280`) -- **a unica volta que existe neste port**.
	///
	/// Ele age sobre a zona em que o admin ESTA, e isso e uma diferenca do original de proposito:
	/// la o verb pede o nome numa lista, e aqui o planeta destruido nao aceita pouso -- entao o
	/// admin precisa de outra forma de chegar. Ele chega **em orbita** (o pouso e recusado) e o verb
	/// atua na zona sob ele... o que nao funcionaria. Por isso este verb tambem aceita o planeta que
	/// esta LOGO ABAIXO de quem esta no espaco.
	/// </summary>
	private void AdminRestaurarPlaneta(ServerPlayer adm)
	{
		ZoneKey alvo = adm.Zone;

		// NO ESPACO, o alvo e o disco sob o corpo -- senao um planeta morto seria inalcancavel: ele
		// recusa pouso (e por isso que ele esta morto), e o admin ficaria sem como apontar pra ele.
		if (Espaco.EhEspaco(alvo))
		{
			if (Espaco.PlanetaSob(SeedDoUniverso, adm.Pos) is not { } sob)
			{
				Avisar(adm, "voce esta no vazio -- va ate a orbita do planeta que quer restaurar.");
				return;
			}
			alvo = sob.Premade ? ZoneKey.Premade(sob.Nome) : ZoneKey.Procedural(sob.Nome, sob.Seed);
		}

		if (!RessuscitarPlaneta(alvo))
		{
			Avisar(adm, $"{alvo.Name} nao esta na lista de mortos.");
			return;
		}
		Avisar(adm, $"{alvo.Name} volta a existir. Da pra pousar, e o povo volta na proxima manutencao.");
	}

	/// <summary>
	/// UMA LINHA PRA QUEM ESTA NO PLANETA. Nao usa o `AnunciarNoMundo` (que fala com o servidor
	/// inteiro): a maior parte dos avisos da morte lenta so faz sentido pra quem esta olhando o ceu.
	/// </summary>
	private void AnunciarNoPlaneta(ZoneKey zona, string texto)
	{
		foreach (ServerPlayer p in ZoneList(zona.Hash))
			if (p.Peer != null) Avisar(p, texto);
		GD.Print($"[planeta] {zona.Name}: {texto}");
	}
}
