using System.Text.Json.Nodes;
using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA PROVA (`--provateste`) -- as SETE perguntas do dono, e cada uma com o defeito
/// INJETADO.
///
/// ============================ POR QUE ELA EXISTE, TENDO `--povoteste` E `--sagateste` ============================
/// Aquelas duas afirmam. Esta **prova que a afirmacao tem dentes.**
///
/// As duas anteriores tem 139 checagens verdes entre elas, e de todas so UMA -- o mob-zumbi -- ja
/// tinha sido vista reprovando. As outras 138 nunca foram vermelhas na vida: ninguem sabe se elas
/// SABEM ficar vermelhas. Uma checagem que so foi vista passando e indistinguivel de `Checa("...",
/// true)`, e este projeto ja pagou exatamente esse preco tres vezes -- a API de sigilo de BP escrita
/// e orfa, o bit de Admin apagado doze linhas depois, o canal de FLAGS extraido e morto. Nos tres, o
/// que faltou nao foi codigo: foi alguem obrigar a regra a falhar uma vez.
///
/// Entao o desenho aqui e um so, repetido sete vezes (ver <see cref="Mutacao"/>):
///
///     1. o criterio e uma FUNCAO NOMEADA, e ela mede o codigo de PRODUCAO      -> tem que passar
///     2. injeta-se um defeito de verdade -- dado torto, regra desligada, atalho -> tem que REPROVAR
///     3. desfaz-se o defeito                                                    -> tem que passar de novo
///
/// O passo 3 nao e enfeite: sem ele, "reprovou" pode ser um estrago permanente que a bancada mesma
/// causou, e a partir dali tudo o que vier depois esta medindo um mundo quebrado. O passo 1 tambem
/// nao: se o criterio ja reprova ANTES do defeito, nao ha nada sendo injetado.
///
/// **O criterio e o MESMO objeto nas tres vezes.** Escrever a versao "com defeito" a mao mediria a
/// copia -- e a copia e onde o teste concorda consigo mesmo e discorda do jogo.
/// ============================================================================================================
///
/// ============================ AS SETE FAMILIAS, E COMO CADA UMA REPROVA ============================
///   1. CIDADAO NA RACA E NO PLANETA CERTOS -> reprova se um corpo do mundo tiver `PlanetaNatal(raca)`
///      diferente do planeta em que nasceu. Defeito injetado: o pool de racas voltando a ser a tabela
///      do MENU DE CRIACAO, que poe Icer em Vegeta -- o defeito historico, literal.
///   2. INIMIGO COMUM NAO NASCE         -> reprova se existir corpo de tipo `Inimigo` no mundo, e
///      reprova tambem **no dia em que alguem religar o interruptor** -- que e o pedido do dono
///      virado numa linha que obriga a conversa. Defeito injetado: um corpo de inimigo vivo.
///   3. O TETO DISPARA                  -> reprova se a lotacao passar de `MaxPorZona`. Defeito
///      injetado: o teto com folga grande demais (corolario 0.7 na letra).
///   4. VINTE NPCs POR MILHARES DE TIQUES -> reprova se o terco final custar 1,5x o terco inicial, ou
///      se um corpo virar estatua. Defeitos injetados: lista que so cresce, e um cerebro apagado.
///   5. A SAGA                          -> reprova se o marco disparar fora do BP, se o BP do chefe
///      andar depois do anuncio, ou se dois elos correrem juntos. Defeitos injetados: `gatilhoBp`
///      torto, o pino recomputado na chegada, o pino que nao chegou no corpo, e um SEGUNDO CHAMADOR
///      que dispara o elo pulando o portao.
///   6. TEM A FORMA E NAO A USA         -> reprova se ele subir a escada sozinho, **e** se ele nao
///      tiver a forma (que e o caso que separa "nao quer" de "nao pode"). Defeito injetado: o
///      `ascendePorDecisao` religado no molde.
///   7. SOBREVIVE AO REINICIO           -> reprova se o chefe nao voltar, voltar noutro degrau ou com
///      outro BP. Defeitos injetados: o save apagado, e -- o sorrateiro -- o save inteiro menos o
///      vetor do BP pinado.
/// ==============================================================================================
///
/// ============================ O QUE ELA MEXE, E POR QUE SO COM A FLAG ============================
/// Ela povoa o mundo, dispara sagas de verdade, poe e tira chefes, ESCREVE no `sagas.json` e no
/// `reputacao.json`, e -- de proposito -- estraga cada um deles uma vez. Tudo o que ela toca e
/// fotografado no comeco e devolvido no `finally`, inclusive o texto do arquivo de sagas.
///
///     Godot --headless --path . --host --rede 7963 --conta bancada_prova --senha teste
///            --nome Prova --raca Saiyan --provateste
/// ============================================================================================
/// </summary>
public partial class GameServer
{
	private bool _provaDeTeste;

	/// <summary>
	/// ============================ MEDE, ESTRAGA, MEDE, CONSERTA, MEDE ============================
	/// O padrao desta bancada inteira, numa funcao -- e ele existe como FUNCAO e nao como receita
	/// escrita no cabecalho por um motivo pratico: assim o passo 1 e o passo 3 nao tem como ser
	/// esquecidos numa familia. Sete blocos escritos a mao seriam sete oportunidades de alguem so
	/// escrever o passo 2 e achar que provou alguma coisa.
	///
	/// O `finally` em volta do `consertar` nao e cerimonia: se o proprio criterio explodir com o
	/// defeito no ar (e um criterio que le o mundo pode explodir num mundo estragado), o mundo TEM que
	/// voltar antes de a excecao subir -- senao a familia seguinte mede um destroco e ninguem entende
	/// por que sete checagens caíram em cascata.
	/// =========================================================================================
	/// </summary>
	/// <param name="oQue">A afirmacao, do jeito que ela vale hoje.</param>
	/// <param name="oDefeito">O que foi injetado, em uma linha -- e o que o console mostra.</param>
	private static void Mutacao(Checagem Checa, string oQue, string oDefeito,
								Func<bool> criterio, Action estragar, Action consertar)
	{
		Checa(oQue, criterio(),
			  "o criterio ja reprova ANTES do defeito -- nao ha nada sendo injetado aqui");

		bool caiu;
		try
		{
			estragar();
			caiu = !criterio();
		}
		finally { consertar(); }

		Checa($"   DEFEITO INJETADO ({oDefeito}): o MESMO criterio REPROVA", caiu,
			  "a checagem de cima e decoracao -- ela nao sabe ficar vermelha");
		Checa("   ...e desfeito o defeito ele volta a passar (era a causa, e nao um estrago que ficou)",
			  criterio());
	}

	/// <summary>Roda uma vez, no primeiro login. Ver o cabecalho -- ela MEXE no mundo inteiro.</summary>
	private void RodarBancadaDaProva(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DA PROVA (as sete familias, cada uma com o defeito injetado) =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ---- A FOTOGRAFIA DO QUE VAI SER MEXIDO -----------------------------
		EstadoDasSagas sagasGuardadas = _sagas;
		double ceuGuardado = _adiantoDoCeu, mundoGuardado = _relogioDoMundo;
		double bpGuardado = pl.Ficha.BP;
		string racaGuardada = pl.Race;
		string[] racasDoCidadaoGuardadas = _moldes?.Get("cidadao")?.Racas ?? [];
		double relativoVegetaGuardado = _moldes?.Get("freeza_vegeta")?.BpRelativo ?? 0;
		double relativoNamekGuardado = _moldes?.Get("freeza_namek")?.BpRelativo ?? 0;
		bool ascendeGuardado = _moldes?.Get("guardiao_saiyajin")?.AscendePorDecisao ?? false;
		var quantosGuardados = new Dictionary<string, int>(StringComparer.Ordinal);
		var repGuardada = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		string? arquivoDeSagasGuardado = null;
		var forjados = new List<ServerPlayer>();

		try
		{
			if (_moldes == null || _racas == null)
			{
				Checa("o npcs.json e o races.json carregaram", false);
				return;
			}
			foreach (LinhaDePovoamento l in _moldes.Plano)
				quantosGuardados[l.Planeta + "/" + l.Molde] = l.Quantos;
			foreach (LinhaDePovoamento l in _moldes.Plano)
				repGuardada[l.Planeta] = ReputacaoDe(l.Planeta, pl.Conta);
			repGuardada["Vegeta"] = ReputacaoDe("Vegeta", pl.Conta);
			repGuardada["Namek"] = ReputacaoDe("Namek", pl.Conta);

			// =====================================================================
			// O MUNDO NASCE PRIMEIRO -- pelo caminho de producao, e so por ele
			// =====================================================================
			// Um laco proprio chamando `NascerNpc` mediria o atalho (regra 0.7). O que roda aqui e o
			// MESMO `TickDoPovoamento` que o tique do servidor chama na linha 2575.
			int tiquesPraPovoar = PovoarPeloTique(600);
			int vivos = CorposSemDonoNoServidor();
			int planejados = _moldes.Plano.Sum(l => l.Quantos);

			GD.Print($"  ---- mundo povoado em {tiquesPraPovoar} tiques: {vivos} corpos sem dono "
				   + $"({planejados} planejados) ----");
			Checa($"o plano de producao nasceu inteiro ({planejados} habitantes)",
				  _moldes.Plano.All(l => ContarHabitantes(l.Planeta, l.Molde) == l.Quantos),
				  string.Join(" | ", _moldes.Plano.Select(
					  l => $"{l.Planeta} {ContarHabitantes(l.Planeta, l.Molde)}/{l.Quantos}")));

			// =====================================================================
			// FAMILIA 1 -- A RACA CERTA NO PLANETA CERTO
			// =====================================================================
			FamiliaDaRaca(Checa, forjados);

			// =====================================================================
			// FAMILIA 2 -- O RECORTE DO DONO
			// =====================================================================
			FamiliaDoRecorte(Checa, forjados);

			// =====================================================================
			// FAMILIA 3 -- O TETO DISPARA
			// =====================================================================
			FamiliaDoTeto(Checa);

			// =====================================================================
			// FAMILIA 4 -- O CUSTO AO LONGO DE MILHARES DE TIQUES
			// =====================================================================
			FamiliaDoMobZumbi(Checa);

			// =====================================================================
			// FAMILIA 5 -- A CADEIA DE SAGAS
			// =====================================================================
			// O texto do `sagas.json` e fotografado ANTES de a familia 5 escrever nele: a familia 7
			// estraga o arquivo de proposito, e sem esta copia o servidor ficaria com o estrago.
			arquivoDeSagasGuardado = System.IO.File.Exists(CaminhoDasSagas)
				? System.IO.File.ReadAllText(CaminhoDasSagas) : "";

			FamiliaDaSaga(Checa, pl);

			// =====================================================================
			// FAMILIA 6 -- TEM A FORMA E NAO A USA
			// =====================================================================
			FamiliaDaForma(Checa, forjados);

			// =====================================================================
			// FAMILIA 7 -- SOBREVIVE AO REINICIO
			// =====================================================================
			FamiliaDoReinicio(Checa);

			Checa("a bancada chegou ao fim (ver o `catch`: sem ele, abortar no meio reportava '0 falhas')",
				  true);
		}
		// ABORTAR NO MEIO NAO PODE PARECER SUCESSO. Mesma licao da `--povoteste`: uma excecao subia pro
		// tratador de pacotes, o `finally` imprimia "0 falha(s)" e a bancada parecia verde. Aqui vale
		// mais ainda, porque esta bancada ESTRAGA o mundo de proposito -- uma excecao com o defeito no
		// ar e o unico caminho por onde um estrago sobreviveria, e ele tem que sair gritando.
		catch (Exception ex) { Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString()); }
		finally
		{
			EscutaDasSagas = null;

			foreach (ServerPlayer f in forjados) if (_players.ContainsKey(f.Id)) RemoverNpc(f);
			foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);

			if (_moldes != null)
			{
				if (_moldes.Get("cidadao") is { } cid) cid.Racas = racasDoCidadaoGuardadas;
				if (_moldes.Get("freeza_vegeta") is { } fv) fv.BpRelativo = relativoVegetaGuardado;
				if (_moldes.Get("freeza_namek") is { } fn) fn.BpRelativo = relativoNamekGuardado;
				if (_moldes.Get("guardiao_saiyajin") is { } g) g.AscendePorDecisao = ascendeGuardado;
				foreach (LinhaDePovoamento l in _moldes.Plano)
					if (quantosGuardados.TryGetValue(l.Planeta + "/" + l.Molde, out int q)) l.Quantos = q;
			}

			pl.Ficha.BP = bpGuardado;
			pl.Race = racaGuardada;
			_adiantoDoCeu = ceuGuardado;
			_relogioDoMundo = mundoGuardado;

			// O LIVRO-CAIXA VOLTA PELO FUNIL, e nao escrevendo no dicionario por baixo: `SomarReputacao`
			// nao tem "desfazer" e nao deve ter. Quem rodar esta bancada nao pode ficar heroi de Vegeta
			// pra sempre por causa de um Freeza de teste.
			foreach ((string planeta, double antes) in repGuardada)
			{
				double excedente = ReputacaoDe(planeta, pl.Conta) - antes;
				if (Math.Abs(excedente) > 0.001)
					SomarReputacao(planeta, pl, -excedente, "fim-da-bancada-da-prova");
			}

			_sagas = sagasGuardadas;
			_sagasPorRetomar = false;
			SalvarSagas();

			// E O ARQUIVO VOLTA AO TEXTO ORIGINAL. O `SalvarSagas` de cima ja escreveu o estado
			// guardado, mas a familia 7 mexe no ARQUIVO e nao so no estado -- se ela morrer no meio, e
			// esta linha que impede o servidor de subir amanha com um `sagas.json` mutilado.
			try
			{
				if (arquivoDeSagasGuardado is { Length: > 0 })
					System.IO.File.WriteAllText(CaminhoDasSagas, arquivoDeSagasGuardado);
			}
			catch (Exception e) { GD.PushWarning($"[server] prova: nao deu pra devolver o sagas.json: {e.Message}"); }

			GD.Print($"===== FIM: {ok} ok, {falhou} falha(s) =====\n");
			if (falhou > 0) GD.PushError($"[server] bancada da prova: {falhou} falha(s)");
			Avisar(pl, $"bancada da prova: {ok} ok, {falhou} falha(s) -- veja o console.");
		}
	}

	// =====================================================================
	// FAMILIA 1 -- CIDADAO NASCE NA RACA CERTA, NO PLANETA CERTO
	// =====================================================================
	/// <summary>
	/// ============================ A VARREDURA E DO CONJUNTO DAS RACAS, E NAO DE UM EXEMPLO ============================
	/// Tres perguntas, e so a primeira e obvia:
	///
	///   * **NINGUEM ESTA NO PLANETA ERRADO** -- o criterio, e ele varre o mundo inteiro corpo a corpo.
	///   * **TODA RACA TEM UM LUGAR SO** -- a `PlanetaNatal` lida ao contrario tem que ser uma PARTICAO:
	///     nenhuma raca em dois planetas, nenhuma raca que povoa e nao aparece em lugar nenhum. Uma
	///     tabela que devolvesse a mesma raca em dois planetas nao produziria corpo errado nenhum --
	///     ela produziria um jogo em que "de onde este povo e" nao tem resposta.
	///   * **E QUEM DEVIA NASCER NASCEU** -- o conjunto das racas que de fato apareceram tem que ser o
	///     conjunto INTEIRO do berco daquele planeta, e nao um subconjunto. Um pool que silenciosamente
	///     colapsasse numa raca so passaria numa checagem de "todo mundo esta no planeta certo": os
	///     quarenta humanos da Terra estao todos no planeta certo. O que estaria errado e a Terra ter
	///     virado um planeta de humanos e mais nada.
	/// =============================================================================================================
	/// </summary>
	private void FamiliaDaRaca(Checagem Checa, List<ServerPlayer> forjados)
	{
		GD.Print("\n  ### FAMILIA 1 -- a raca certa, no planeta certo ###");
		string[] todasAsRacas = [.. _racas!.Protos.Keys];

		// ---- O CRITERIO -----------------------------------------------------
		// Sobre o MUNDO e nao sobre a tabela: e a unica pergunta que continua valendo no dia em que
		// alguem acrescentar um segundo caminho de nascimento (um admin, uma quest, um ovo).
		List<string> Forasteiros() =>
			[.. _players.Values
				.Where(p => p.Peer == null && p.Papel is { Tipo: TipoDeNpc.Cidadao }
						 && !string.Equals(Bercos.PlanetaNatal(p.Race), p.Berco.Natal,
										   StringComparison.OrdinalIgnoreCase))
				.Select(p => $"{p.Name} ({p.Race}) em {p.Berco.Natal}")];

		bool NinguemNoPlanetaErrado() => Forasteiros().Count == 0;

		// ---- A PARTICAO -----------------------------------------------------
		var moram = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string r in todasAsRacas)
		{
			if (!Bercos.PovoaUmPlaneta(r)) continue;
			string casa = Bercos.PlanetaNatal(r);
			if (!moram.TryGetValue(casa, out List<string>? lista)) moram[casa] = lista = [];
			lista.Add(r);
		}

		int repetidas = todasAsRacas.Count(
			r => Bercos.PovoaUmPlaneta(r)
			  && moram.Count(kv => kv.Value.Contains(r)) != 1);

		Checa($"a tabela do berco e uma PARTICAO: {moram.Sum(kv => kv.Value.Count)} racas povoadoras "
			+ $"em {moram.Count} planetas, nenhuma repetida", repetidas == 0, $"{repetidas}");

		// O QUE NAO POVOA, E POR QUE. Sai no console porque e informacao do dono: estas racas nunca
		// terao cidadao em planeta nenhum, e isso e decisao e nao esquecimento.
		GD.Print("       nao povoam (nascem na coordenada de quem as criou, ou nao sao raca do jogo): "
			   + string.Join(", ", todasAsRacas.Where(r => !Bercos.PovoaUmPlaneta(r))));

		// AS RACAS SEM CIDADAO NENHUM: o berco delas nao esta no plano de povoamento. Nao e defeito --
		// e a lista do que o dono ganha de graca no dia em que puser aquele planeta no plano.
		string[] semPlano = [.. moram.Keys
			.Where(p => !_moldes!.Plano.Any(l => string.Equals(l.Planeta, p, StringComparison.OrdinalIgnoreCase)))
			.OrderBy(p => p, StringComparer.Ordinal)];
		GD.Print($"       planetas com berco e SEM linha no plano ({semPlano.Length}): "
			   + string.Join(", ", semPlano.Select(p => $"{p} [{string.Join(",", moram[p])}]")));

		// ---- E QUEM DEVIA NASCER NASCEU --------------------------------------
		foreach (LinhaDePovoamento l in _moldes!.Plano)
		{
			string[] deviam = Bercos.RacasNascidasEm(l.Planeta, todasAsRacas);
			string[] nasceram = [.. _players.Values
				.Where(p => p.Papel is { Tipo: TipoDeNpc.Cidadao }
						 && string.Equals(p.Berco.Natal, l.Planeta, StringComparison.OrdinalIgnoreCase))
				.Select(p => p.Race).Distinct().OrderBy(r => r, StringComparer.Ordinal)];

			GD.Print($"       {l.Planeta} ({l.Quantos} corpos): {string.Join(", ", nasceram)}");
			Checa($"'{l.Planeta}': TODAS as {deviam.Length} racas do berco apareceram nos {l.Quantos} "
				+ "corpos (o pool nao colapsou numa raca so)",
				  deviam.All(nasceram.Contains),
				  "faltaram: " + string.Join(",", deviam.Where(r => !nasceram.Contains(r))));
		}

		// ---- A MUTACAO ------------------------------------------------------
		// ============================ O DEFEITO INJETADO E O DEFEITO HISTORICO ============================
		// O sorteio lia `CharacterDraft.RacasDoPlaneta` -- a tabela do MENU DE CRIACAO --, que **nao e**
		// a inversa do berco: ela poe Icer em Vegeta. Um cidadao de Vegeta podia nascer Frost Demon
		// enquanto o jogador Frost Demon nasce em Icer.
		//
		// O caminho por onde a tabela errada volta a valer hoje e o campo `racas` do molde (quando ele
		// nao esta vazio, o pool do berco nem e consultado -- `SorteioDeNpc:86`), entao e por ele que o
		// defeito entra. E ele entra com a raca EXATA da divergencia, uma so, em vez da tabela inteira:
		// um defeito que so aparece em 1 de cada 20 nascimentos e um defeito que a bancada as vezes nao
		// ve, e uma bancada que as vezes ve nao e bancada.
		// ============================================================================================
		Checa("a tabela do MENU de criacao realmente poe Icer em Vegeta (e por isso ela serve de defeito)",
			  CharacterDraft.RacasDoPlaneta("Vegeta").Contains("Icer")
			  && !Bercos.RacasNascidasEm("Vegeta", todasAsRacas).Contains("Icer"));

		MoldeDeNpc cidadao = _moldes.Get("cidadao")!;
		string[] original = cidadao.Racas;
		var deVegeta = ZoneKey.Premade("Vegeta");
		ServerPlayer? intruso = null;

		Mutacao(Checa,
			"nenhum cidadao do mundo esta no planeta errado (a raca sai do BERCO, "
			+ $"que e a `PlanetaNatal` lida ao contrario) -- {CorposSemDonoNoServidor()} corpos varridos",
			"o pool de racas volta a ser o do MENU DE CRIACAO, que poe Icer em Vegeta",
			NinguemNoPlanetaErrado,
			() =>
			{
				cidadao.Racas = ["Icer"];
				intruso = NascerNpc("cidadao", deVegeta,
									PontoDeHabitante(deVegeta, 970_001), 970_001);
				if (intruso != null) forjados.Add(intruso);
			},
			() =>
			{
				cidadao.Racas = original;
				if (intruso != null && _players.ContainsKey(intruso.Id))
				{
					RemoverNpc(intruso);
					forjados.Remove(intruso);
				}
				intruso = null;
			});

		Checa("...e o forasteiro injetado era mesmo um Frost Demon morando em Vegeta (o defeito foi o "
			+ "que se quis, e nao um nascimento que falhou)",
			  Forasteiros().Count == 0, string.Join(" | ", Forasteiros()));
	}

	// =====================================================================
	// FAMILIA 2 -- O RECORTE DO DONO
	// =====================================================================
	/// <summary>
	/// ============================ ESTA E A FAMILIA QUE TEM QUE REPROVAR UM DIA ============================
	/// O pedido foi *"NPCs INIMIGOS que nao sao chefe POR ENQUANTO NAO SPAWNAM pois vamos rever essa
	/// parte"*. "Por enquanto" e uma data que ninguem marcou, e o jeito de ela nao se perder e uma
	/// checagem que **fica vermelha no dia em que alguem religar o interruptor** -- obrigando quem
	/// religou a voltar aqui e atualizar a conversa, em vez de o recorte sumir num commit.
	///
	/// Por isso a primeira linha afirma o INTERRUPTOR e nao o efeito dele. Afirmar so o efeito ("nenhum
	/// inimigo no mundo") ficaria verde num servidor onde o interruptor foi ligado e simplesmente nao
	/// nasceu inimigo ainda.
	/// ==================================================================================================
	/// </summary>
	private void FamiliaDoRecorte(Checagem Checa, List<ServerPlayer> forjados)
	{
		GD.Print("\n  ### FAMILIA 2 -- inimigo comum NAO nasce ###");

		Checa($"O INTERRUPTOR ESTA DESLIGADO ({Povoamento.MotivoDoInimigoDesligado}). "
			+ "SE ESTA LINHA REPROVAR, o recorte foi religado -- volte e atualize a conversa com o dono",
			  !Povoamento.InimigoComumLigado);

		// O RECORTE E UMA FUNCAO SOBRE O TIPO, e a bancada pergunta por TIPO e nao por nome: molde novo
		// no `npcs.json` ja nasce submetido, e a checagem nao envelhece com o arquivo.
		Checa("o recorte e uma funcao do TIPO: cidadao sim, chefe sim, inimigo nao",
			  Povoamento.PodeNascer(TipoDeNpc.Cidadao)
			  && Povoamento.PodeNascer(TipoDeNpc.Chefe)
			  && !Povoamento.PodeNascer(TipoDeNpc.Inimigo));

		MoldeDeNpc[] inimigos = [.. _moldes!.Todos.Where(m => m.Tipo == TipoDeNpc.Inimigo)];
		Checa($"os {inimigos.Length} moldes de inimigo continuam no arquivo (o caminho esta pronto, nao apagado)",
			  inimigos.Length > 0);
		if (inimigos.Length == 0 || _moldes.Plano.Length == 0) return;   // sem cobaia nao ha o que injetar

		var zona = ZoneKey.Premade("Namek");
		ulong lugar = 960_000;
		foreach (MoldeDeNpc m in inimigos)
		{
			ServerPlayer? bicho = NascerNpc(m.Id, zona, PontoDeHabitante(zona, lugar), lugar++);
			if (bicho != null) forjados.Add(bicho);
			Checa($"'{m.Id}' nao nasce", bicho == null);
		}

		// ---- O CRITERIO -----------------------------------------------------
		// SOBRE O MUNDO, e nao sobre a porta. Assim ele pega tambem a porta que ninguem escreveu ainda:
		// no dia em que houver um segundo caminho de nascimento que esqueca o funil, o sintoma sera um
		// corpo de inimigo vivo -- e e disso que esta funcao pergunta.
		int Inimigos() => _players.Values.Count(p => p.Papel is { Tipo: TipoDeNpc.Inimigo });
		bool SemInimigoNoMundo() => Inimigos() == 0;

		MoldeDeNpc cobaia = inimigos[0];
		ServerPlayer? corpo = null;

		Mutacao(Checa,
			"nenhum corpo de tipo INIMIGO existe no mundo",
			$"um corpo de '{cobaia.Id}' vivo no mundo -- nao importa por qual porta ele entrou",
			SemInimigoNoMundo,
			() =>
			{
				// O TIPO E O QUE O RECORTE LE, entao e o tipo que se mexe -- e ele volta ao lugar ANTES
				// do criterio rodar, pra o corpo que ficou no mundo ser de verdade um corpo de inimigo.
				TipoDeNpc antes = cobaia.Tipo;
				cobaia.Tipo = TipoDeNpc.Cidadao;
				corpo = NascerNpc(cobaia.Id, zona, PontoDeHabitante(zona, lugar), lugar++);
				cobaia.Tipo = antes;
			},
			() =>
			{
				if (corpo != null && _players.ContainsKey(corpo.Id)) RemoverNpc(corpo);
				corpo = null;
			});

		// ---- E O CONTROLE, que e o que separa "o recorte funciona" de "nada nasce" ----
		// Sem esta metade, as quatro recusas de cima passariam com `NascerNpc` devolvendo nulo por
		// qualquer motivo: races.json ausente, molde incoerente, uma excecao engolida.
		{
			TipoDeNpc antes = cobaia.Tipo;
			cobaia.Tipo = TipoDeNpc.Cidadao;
			ServerPlayer? agora = NascerNpc(cobaia.Id, zona, PontoDeHabitante(zona, lugar), lugar++);
			cobaia.Tipo = antes;
			Checa($"...e o MESMO molde ('{cobaia.Id}') NASCE assim que o tipo muda -- era o recorte, "
				+ "e nao outra recusa", agora != null);
			if (agora != null) RemoverNpc(agora);
		}

		// ---- E A OUTRA METADE DO PEDIDO: chefe de saga NASCE ----
		{
			ServerPlayer? chefe = NascerNpc("freeza_vegeta", zona, PontoDeHabitante(zona, lugar), lugar++);
			Checa("o CHEFE DE SAGA nasce (o dono pediu os dois lados do corte)",
				  chefe is { Papel.Tipo: TipoDeNpc.Chefe });
			if (chefe != null) RemoverNpc(chefe);
		}

		// ---- E O PLANO NAO PODE CITAR INIMIGO, e isso tambem tem defeito injetado ----
		// Uma linha de povoamento apontando um molde de inimigo encheria a fila com pedidos que o funil
		// recusa um por um, pra sempre, gritando por tique. A guarda existe; aqui ela e obrigada a
		// disparar uma vez.
		{
			LinhaDePovoamento cobaiaDoPlano = _moldes.Plano[0];
			string moldeOriginal = cobaiaDoPlano.Molde;

			Mutacao(Checa,
				"o plano de povoamento nao tem contradicao nenhuma",
				$"a primeira linha do plano passa a pedir '{cobaia.Id}', que e inimigo comum",
				() => Povoamento.Problemas(_moldes.Plano, _moldes).Count == 0,
				() => cobaiaDoPlano.Molde = cobaia.Id,
				() => cobaiaDoPlano.Molde = moldeOriginal);
		}
	}

	// =====================================================================
	// FAMILIA 3 -- O TETO DISPARA
	// =====================================================================
	/// <summary>
	/// ============================ O COROLARIO 0.7, NA LETRA ============================
	/// *"Um teto que nunca e atingido e indistinguivel de teto nenhum."* A `--povoteste` aperta o teto
	/// pra 1 e ve a recusa; isso prova que o CAMINHO de recusa existe, e nao que o numero de producao
	/// (80) rejeita alguma coisa.
	///
	/// Aqui e o numero de producao que rejeita: uma linha do plano passa a pedir `MaxPorZona + 40`, o
	/// mundo nasce pelo `TickDoPovoamento` de producao com os tetos de producao, e a lotacao para em 80
	/// com quarenta habitantes recusados no console.
	///
	/// E o defeito injetado e o que a regra 0.7 descreve com todas as letras: **o mesmo teto, com folga
	/// grande demais**. Nao um teto removido -- um teto de 10 mil, que e como um teto morre de verdade
	/// (alguem "so pra destravar o teste" poe um numero grande e ninguem nunca mais o ve disparar).
	/// ==============================================================================
	/// </summary>
	private void FamiliaDoTeto(Checagem Checa)
	{
		GD.Print("\n  ### FAMILIA 3 -- o teto dispara ###");
		if (_moldes!.Plano.Length == 0) { Checa("ha plano de povoamento pra apertar contra o teto", false); return; }

		// A LINHA COBAIA E A MENOR DO PLANO: ela tem a maior folga ate o teto de zona (12 de 80), entao
		// e a que sobra espaco pra crescer sem encostar no teto do SERVIDOR -- que e outro teto e mediria
		// outra coisa.
		LinhaDePovoamento linha = _moldes.Plano.OrderBy(l => l.Quantos).First();
		int quantosOriginal = linha.Quantos;
		var zona = ZoneKey.Premade(linha.Planeta);
		int pedido = Povoamento.MaxPorZona + 40;

		var idsAntes = new HashSet<int>(_players.Keys);

		// O CRITERIO: a lotacao nao passa do teto, **e** o plano nao foi satisfeito. As duas metades sao
		// necessarias -- "cabem 80" sozinho passaria num plano que pedisse 80, e ai o teto nao teria
		// disparado; "o plano nao foi satisfeito" sozinho passaria com o povoamento quebrado.
		bool OTetoSegurou()
		{
			int naZona = CorposSemDonoNaZona(zona);
			return naZona <= Povoamento.MaxPorZona && ContarHabitantes(linha.Planeta, linha.Molde) < pedido;
		}

		void LimparOsExtras()
		{
			foreach (ServerPlayer p in _players.Values.ToList())
				if (!idsAntes.Contains(p.Id) && p.Papel is { Tipo: TipoDeNpc.Cidadao }) RemoverNpc(p);
		}

		linha.Quantos = pedido;
		PovoarPeloTique(400);
		GD.Print($"  ---- '{linha.Planeta}' pediu {pedido}, o teto e {Povoamento.MaxPorZona}: "
			   + $"ficaram {CorposSemDonoNaZona(zona)} corpos na zona ----");

		Mutacao(Checa,
			$"com o plano pedindo {pedido} em '{linha.Planeta}', o teto de producao "
			+ $"({Povoamento.MaxPorZona}/zona) SEGUROU a lotacao",
			"o mesmo teto com folga grande demais (10 mil) -- o teto que existe e nunca dispara",
			OTetoSegurou,
			() =>
			{
				// O MESMO `TickDoPovoamento`, os mesmos parametros de producao com outro numero. Os
				// parametros existem por isto (o `teto` do `Bercos.ServeDeBerco` existe pelo mesmo
				// motivo): a alternativa seria uma copia da manutencao, e a copia testaria a copia.
				_proximaManutencao = 0;
				for (int t = 0; t < 400; t++) TickDoPovoamento(Protocol.TickSeconds, 10_000, 10_000);
			},
			() =>
			{
				LimparOsExtras();
				linha.Quantos = quantosOriginal;
				PovoarPeloTique(200);
			});

		// ---- E O TETO DO SERVIDOR, que e outro teto e tambem tem que disparar ----
		Checa("o teto do SERVIDOR tambem recusa (a conta inclui a fila, senao ele so dispararia tarde)",
			  !CabeMaisUmCorpo(zona, Povoamento.MaxPorZona, tetoServidor: 1, out _));
		Checa("...e com os tetos de producao ele aceita -- entao a recusa acima e o teto, e nao a "
			+ "populacao ja completa",
			  CabeMaisUmCorpo(ZoneKey.Premade("Namek"), Povoamento.MaxPorZona, Povoamento.MaxNoServidor, out _));

		Checa($"o plano de producao inteiro ({_moldes.Plano.Sum(l => l.Quantos)}) cabe nos tetos "
			+ $"({Povoamento.MaxPorZona}/zona, {Povoamento.MaxNoServidor}/servidor)",
			  _moldes.Plano.All(l => l.Quantos <= Povoamento.MaxPorZona)
			  && _moldes.Plano.Sum(l => l.Quantos) <= Povoamento.MaxNoServidor);

		Checa("...e o mundo voltou ao tamanho do plano depois da familia 3",
			  _moldes.Plano.All(l => ContarHabitantes(l.Planeta, l.Molde) == l.Quantos),
			  string.Join(" | ", _moldes.Plano.Select(
				  l => $"{l.Planeta} {ContarHabitantes(l.Planeta, l.Molde)}/{l.Quantos}")));
	}

	// =====================================================================
	// FAMILIA 4 -- MILHARES DE TIQUES, E A CURVA
	// =====================================================================
	/// <summary>
	/// ============================ O MOB-ZUMBI NAO E UM TIQUE CARO, E UM TIQUE QUE FICA CARO ============================
	/// Por isso o eixo e o TIQUE e nao o relogio de parede -- o estado que vaza vaza por tique -- e por
	/// isso a medida e uma CURVA de seis janelas e nao um ponto. Um ponto responde "quanto custa"; so a
	/// curva responde "esta ficando mais caro".
	///
	/// O dono pediu vinte corpos. A medida corre com a populacao de PRODUCAO inteira, que e mais que o
	/// dobro disso, porque ela e a unica configuracao que responde "quanto custa o povoamento" -- e
	/// porque um numero forjado mediria o numero forjado. O console imprime os dois: quantos corpos ha e
	/// quantos estao na zona do jogador.
	///
	/// **DOIS DEFEITOS INJETADOS, e eles sao modos de falha diferentes:** a lista que so cresce (o custo
	/// sobe) e o cerebro que some (o custo NAO sobe -- o corpo simplesmente para de ser dirigido, calado,
	/// que e a forma do defeito que o DM pagou em `NPCAI.dm:751`). Um criterio so nao pega os dois.
	/// ==============================================================================================================
	/// </summary>
	private void FamiliaDoMobZumbi(Checagem Checa)
	{
		GD.Print("\n  ### FAMILIA 4 -- milhares de tiques, e a curva ###");

		int corpos = CorposSemDonoNoServidor();
		const int janelas = 6, tiquesPorJanela = 900;   // 5400 tiques = 3 min de jogo a 30 Hz

		double[] Medir(int quantosTiques, Action? porTique = null)
		{
			var us = new double[janelas];
			for (int j = 0; j < janelas; j++)
			{
				double soma = 0;
				for (int t = 0; t < quantosTiques; t++)
				{
					ulong t0 = Time.GetTicksUsec();
					TickDoPovoamento(Protocol.TickSeconds);
					TickDosCorposSemDono(Protocol.TickSeconds);
					TickCombate(Protocol.TickSeconds);
					porTique?.Invoke();
					soma += Time.GetTicksUsec() - t0;
				}
				us[j] = soma / quantosTiques;
			}
			return us;
		}

		// O CRITERIO: terco final contra terco inicial, folga de 1,5x e piso absoluto. Nao e "a ultima
		// janela e menor que a primeira" -- ruido de maquina inverte isso sozinho. Vazamento de verdade
		// nao cresce 50%; ele cresce ordens de grandeza.
		static bool Progrediu(double[] j)
		{
			int terco = Math.Max(1, j.Length / 3);
			return j.Skip(j.Length - terco).Average() > j.Take(terco).Average() * 1.5 + 10;
		}

		double[] us = Medir(tiquesPorJanela);
		GD.Print($"  ---- {corpos} corpos sem dono ({CorposSemDonoNaZona(SpawnZone)} na zona do jogador), "
			   + $"{janelas * tiquesPorJanela} tiques (3 min de jogo) ----");
		for (int j = 0; j < janelas; j++)
			GD.Print($"       janela {j + 1}: {us[j]:0.0} us/tique ({us[j] / 333.0:0.00}% do orcamento)");

		Checa($"o tique NAO fica mais caro com o tempo ({us[0]:0.0} -> {us[^1]:0.0} us, {corpos} corpos)",
			  !Progrediu(us), string.Join(" -> ", us.Select(v => $"{v:0.0}")));

		// ---- DEFEITO 1: a lista que so cresce e e varrida todo tique ----------
		{
			var zumbis = new List<ServerPlayer>();
			long lixeira = 0;
			List<ServerPlayer> vivos = [.. _players.Values.Where(p => p.Papel != null)];

			double[] comVazamento = Medir(150, () =>
			{
				for (int k = 0; k < 60; k++) zumbis.Add(vivos[k % vivos.Count]);
				foreach (ServerPlayer z in zumbis) lixeira += z.Id;
			});
			_ = lixeira;

			GD.Print($"       (injetado) lista que so cresce: "
				   + string.Join(" -> ", comVazamento.Select(v => $"{v:0.0}")) + " us");
			Checa("   DEFEITO INJETADO (lista que so cresce e e varrida todo tique -- o `NPCAI.dm:751`): "
				+ "o MESMO criterio REPROVA", Progrediu(comVazamento),
				  string.Join(" -> ", comVazamento.Select(v => $"{v:0.0}")));
		}

		// ---- DEFEITO 2: o corpo que para de ser dirigido, calado --------------
		// Este NAO aparece no relogio: um corpo a menos e um tique mais BARATO. Ele so aparece na
		// pergunta certa, e a pergunta certa nao depende de contagem nenhuma -- "todo corpo vivo
		// continua sendo dirigido?".
		{
			int Estatuas() => _players.Values.Count(p => p.Papel != null && p.Cerebro == null && !p.Ficha.dead);
			bool NinguemVirouEstatua() => Estatuas() == 0;

			ServerPlayer? vitima = _players.Values.FirstOrDefault(p => p.Papel != null && p.Cerebro != null);
			Jandirus.Core.Ai.Cerebro? guardado = vitima?.Cerebro;

			Mutacao(Checa,
				$"nenhum dos {corpos} corpos virou estatua em 3 min (todo NPC vivo continua sendo dirigido)",
				"um cerebro apagado sem nada dizendo -- o corpo para de ser dirigido e o tique fica mais BARATO",
				NinguemVirouEstatua,
				() => { if (vitima != null) vitima.Cerebro = null; },
				() => { if (vitima != null) vitima.Cerebro = guardado; });
		}

		Checa("...e a lista de dirigidos e REUSADA, nao acumulada",
			  _dirigidos.Count == _players.Values.Count(p => p.Cerebro != null),
			  $"{_dirigidos.Count}");
		Checa("...e a fila do povoamento nao inchou (a manutencao completa, ela nao acumula)",
			  _filaDoPovoamento.Count == 0, $"{_filaDoPovoamento.Count} pendurados");
	}

	// =====================================================================
	// FAMILIA 5 -- A CADEIA DE SAGAS
	// =====================================================================
	/// <summary>
	/// Tres perguntas do dono num bloco so, porque as tres compartilham o palco (uma cadeia zerada, um
	/// jogador Saiyajin e a manivela do ceu) e montar o palco tres vezes seria a bancada medindo a si
	/// mesma tres vezes.
	/// </summary>
	private void FamiliaDaSaga(Checagem Checa, ServerPlayer pl)
	{
		GD.Print("\n  ### FAMILIA 5 -- a saga: BP do marco, BP do chefe, e a ordem ###");

		if (_cadeia.Length < 2) { Checa("a cadeia tem pelo menos dois elos pra medir a ordem", false); return; }

		EloDaSaga elo1 = _cadeia[0], elo2 = _cadeia[1];
		MoldeDeNpc freezaV = _moldes!.Get(elo1.Chefes[0].Molde)!;
		MoldeDeNpc freezaN = _moldes.Get(elo2.Chefes[0].Molde)!;

		// ============================ COM `bpRelativo`, O PINO PASSA A SER VISIVEL ============================
		// Ficha de BP constante torna "pinar" indistinguivel de "ler o arquivo": o numero e o mesmo nos
		// dois casos. Ligando o campo que o proprio molde ja tem -- poder relativo a MEDIA de quem esta
		// online --, a diferenca entre decidir no ANUNCIO e decidir na CHEGADA vira um numero, e e ele
		// que esta em jogo quando a especificacao diz *"quem chegar depois empurraria a dificuldade"*.
		//
		// O de NAMEK tambem e ligado, e por causa da familia 7: se o pino for igual ao arquivo, apagar o
		// pino do save nao muda nada e a checagem do reinicio ficaria verde com o defeito no ar.
		// ================================================================================================
		freezaV.BpRelativo = 100;
		freezaN.BpRelativo = 50;

		pl.Race = "Saiyan";

		// ---------------------------------------------------------------------
		// 5a. O MARCO DISPARA NO BP CERTO -- os dois sentidos
		// ---------------------------------------------------------------------
		// O CRITERIO E O SENTIDO NEGATIVO, que e o que se perde primeiro: com um triz ABAIXO do marco,
		// nada acontece. O sentido positivo e conferido logo abaixo, e ele nao pode ser o criterio --
		// "a saga aconteceu" fica verde numa cadeia que dispara com qualquer BP.
		//
		// ============================ O BP DA MEDIDA E CAPTURADO, E NAO LIDO DO DADO ============================
		// A primeira versao usava `pl.Ficha.BP = elo1.GatilhoBp - 1` DENTRO do criterio, e o defeito
		// injetado nao foi detectado: baixando o `gatilhoBp` pra 1, o criterio baixou o BP do jogador
		// pra 0 junto e a saga continuou (certissimamente) sem disparar. **Um criterio que le o proprio
		// dado que vai ser mutado anda junto com o defeito e nao mede nada** -- ele nao ficaria vermelho
		// nem com a regra apagada. O numero e capturado ANTES e e uma constante daqui pra frente.
		// ====================================================================================================
		double gatilhoOriginal = elo1.GatilhoBp;
		double umTrizAbaixo = gatilhoOriginal - 1;

		bool SoDisparaNoMarco()
		{
			Zerar();
			pl.Ficha.BP = umTrizAbaixo;
			for (int t = 0; t < 3; t++) TickDasSagas();
			return !Elo(elo1.Id).Disparou;
		}

		Mutacao(Checa,
			$"com BP um triz abaixo do marco ({gatilhoOriginal:N0}) a saga '{elo1.Id}' NAO acorda",
			"um `gatilhoBp` de 1 no npcs.json -- o erro de digitacao que faz a saga disparar com qualquer BP",
			SoDisparaNoMarco,
			() => elo1.GatilhoBp = 1,
			() => elo1.GatilhoBp = gatilhoOriginal);

		// O SENTIDO POSITIVO, e o portao de raca junto: os dois no mesmo palco, so o BP muda.
		Zerar();
		pl.Ficha.BP = 2_000_000_000;          // acima dos QUATRO marcos ao mesmo tempo
		pl.Race = "Human";
		TickDasSagas();
		Checa("um nao-Saiyajin com BP de sobra NAO acorda a saga 1 (o `bev_is_saiyan`)",
			  !Elo(elo1.Id).Disparou);

		pl.Race = "Saiyan";
		TickDasSagas();
		Checa("o MESMO BP num Saiyajin acorda (era o portao de raca, e nao outra recusa)",
			  Elo(elo1.Id) is { Disparou: true, Fase: FaseDaSaga.Contando });

		// ---------------------------------------------------------------------
		// 5c-i. A CADEIA E SEQUENCIAL -- ninguem corre junto
		// ---------------------------------------------------------------------
		// O CRITERIO: no maximo um elo correndo, e todos os anteriores a ele terminados. Ele e mais forte
		// que "a saga 2 nao disparou": pega tambem a ordem trocada, que "so uma correndo" deixaria passar.
		bool UmEloDeCadaVez()
		{
			int correndo = _sagas.Elos.Count(e => e.Fase is FaseDaSaga.Contando or FaseDaSaga.EmCurso);
			if (correndo > 1) return false;

			int i = _sagas.Elos.FindIndex(e => e.Fase is FaseDaSaga.Contando or FaseDaSaga.EmCurso);
			if (i < 0) return true;
			for (int k = 0; k < i; k++)
				if (!Sagas.Terminou(_sagas.Elos[k].Fase)) return false;
			return true;
		}

		Checa($"com BP acima dos {_cadeia.Length} marcos ao mesmo tempo, so o PRIMEIRO elo acordou",
			  _sagas.Elos.Skip(1).All(e => !e.Disparou && e.Fase == FaseDaSaga.Adormecida),
			  string.Join(",", _sagas.Elos.Where(e => e.Disparou).Select(e => e.Id)));

		Mutacao(Checa,
			"no maximo um elo da cadeia esta correndo, e todos os anteriores a ele ja terminaram",
			$"um SEGUNDO CHAMADOR disparando '{elo2.Id}' direto, pulando o `Sagas.PodeComecar`",
			UmEloDeCadaVez,
			() => DispararElo(elo2, Elo(elo2.Id), pl),
			() =>
			{
				foreach (ServerPlayer c in ChefesNoMundo())
					if (Elo(elo2.Id).Chefes.Exists(x => x.Corpo == c.Id)) RemoverNpc(c);
				EstadoDoElo e2 = Elo(elo2.Id);
				e2.Disparou = false;
				e2.Fase = FaseDaSaga.Adormecida;
				e2.Chefes.Clear();
				e2.Condenado = false;
			});

		// ---------------------------------------------------------------------
		// 5b. O BP DO CHEFE E FIXADO NO DISPARO
		// ---------------------------------------------------------------------
		// O palco: a cadeia zerada, media do servidor de 20 mil no ANUNCIO, e mil vezes mais na CHEGADA.
		Zerar();
		pl.Ficha.BP = 20_000;                 // media do servidor -> pino = 100 x 20 mil = 2 M
		TickDasSagas();

		EstadoDoElo est1 = Elo(elo1.Id);
		EstadoDoChefe ec1 = est1.Chefes[0];
		double pinado = ec1.Bps.FirstOrDefault();

		Checa($"o marco pinou o BP no ANUNCIO (100 x a media de 20 mil = 2 M): {pinado:N0}",
			  Math.Abs(pinado - 2_000_000) < 1);
		Checa($"...e a contagem saiu em DIAS IN-GAME, na faixa {elo1.Chefes[0].DiasMin}..{elo1.Chefes[0].DiasMax}",
			  ec1.DiasRestantes >= elo1.Chefes[0].DiasMin && ec1.DiasRestantes <= elo1.Chefes[0].DiasMax,
			  $"{ec1.DiasRestantes}");

		// O SERVIDOR FICA MIL VEZES MAIS FORTE ENTRE O ANUNCIO E A CHEGADA.
		pl.Ficha.BP = 20_000_000;             // recomputar agora daria 2 BILHOES

		// O NUMERO DE VOLTAS E LIDO ANTES DO LACO: `DiasRestantes` DESCE a cada volta, e `d <
		// ec.DiasRestantes` se encontraria no meio do caminho -- defeito que a `--sagateste` ja pagou
		// numa rodada inteira em cascata.
		int diasAteChegar = ec1.DiasRestantes;
		for (int d = 1; d < diasAteChegar; d++)
		{
			AvancarUmDia();
			Checa($"dia {d} de {diasAteChegar}: o chefe AINDA NAO chegou (o prazo e um prazo)",
				  ec1.Fase == FaseDoChefe.ACaminho, $"{ec1.Fase}");
		}
		AvancarUmDia();
		Checa($"no dia {diasAteChegar} ele chega", ec1.Fase == FaseDoChefe.NoMundo, $"{ec1.Fase}");

		ServerPlayer? chefe = Corpo(ec1);
		if (chefe?.Papel == null) { Checa("o corpo do chefe esta no mundo pra medir o pino", false); return; }

		// O CRITERIO: o BP do corpo e o numero que foi ANUNCIADO, e nao o que a media virou depois.
		bool BpEhOPinado() => Math.Abs(chefe.Ficha.BP - pinado) < 1;

		Mutacao(Checa,
			$"o BP do chefe e o PINADO ({pinado:N0}) e nao o recomputado com a media nova "
			+ $"({MediaDoServidor() * freezaV.BpRelativo:N0}) nem o do arquivo "
			+ $"({freezaV.Estagios[0].Bp:N0})",
			"o pino recomputado NA CHEGADA com a media de agora -- quem chegou depois empurrou a dificuldade",
			BpEhOPinado,
			() => { chefe.Papel!.BpsPinados = Sagas.Pinar(freezaV, MediaDoServidor()); TickDoRoteiro(); },
			() => { chefe.Papel!.BpsPinados = ec1.Bps; TickDoRoteiro(); });

		// ---- E ELE NAO SE MEXE DURANTE A LUTA ---------------------------------
		// O `NPCTicker()` do EventBoss. Nao e decoracao: o corpo de um chefe passa pelos MESMOS lacos de
		// um jogador (`TickFichas` chama `Treinar` e `Ficha.Tick` pra todo mundo em `_players`, e o
		// Zenkai paga na hora da derrota), entao um chefe Saiyajin numa luta longa terminaria com um BP
		// que ninguem anunciou.
		bool VoltaSozinho()
		{
			chefe.Ficha.BP *= 3;
			TickDoRoteiro();
			return Math.Abs(chefe.Ficha.BP - pinado) < 1;
		}

		Mutacao(Checa,
			"empurrar o BP do chefe pra cima e desfeito no tique seguinte (BP anunciado = BP real)",
			"o vetor pinado nao chegou no corpo -- a reancoragem passa a devolver o numero do ARQUIVO",
			VoltaSozinho,
			() => chefe.Papel!.BpsPinados = [],
			() => { chefe.Papel!.BpsPinados = ec1.Bps; TickDoRoteiro(); });

		// ---------------------------------------------------------------------
		// 5c-ii. A PROXIMA SO COMECA DEPOIS -- o outro sentido
		// ---------------------------------------------------------------------
		Checa($"a saga '{elo2.Id}' NAO disparou enquanto a '{elo1.Id}' corria (o BP dela esta batido "
			+ "ha muito tempo)", !Elo(elo2.Id).Disparou);

		// O chefe cai -- pelo funil de agressao, que e quem identifica o matador.
		EscutaDasSagas = [];
		MarcarAgressao(chefe, pl);
		chefe.Ficha.dead = true;
		TickDasSagas();

		Checa($"o chefe morto fecha a saga '{elo1.Id}' como VENCIDA", est1.Fase == FaseDaSaga.Vencida,
			  $"{est1.Fase}");
		Checa($"...e o matador ganhou +{Jandirus.Core.Social.Reputacao.HeroiPorChefe:0} de reputacao "
			+ $"com o povo de {elo1.Planeta}",
			  ReputacaoDe(elo1.Planeta, pl.Conta) >= Jandirus.Core.Social.Reputacao.HeroiPorChefe,
			  $"{ReputacaoDe(elo1.Planeta, pl.Conta):0}");

		// O CRITERIO: o elo seguinte dispara no primeiro tique depois de o anterior fechar. O marco dele
		// nao se perdeu enquanto esperava -- ele so estava esperando a vez.
		bool AProximaDestrava()
		{
			TickDasSagas();
			return Elo(elo2.Id) is { Disparou: true, Fase: FaseDaSaga.Contando or FaseDaSaga.EmCurso };
		}

		Mutacao(Checa,
			$"a saga '{elo2.Id}' dispara no primeiro tique depois de a '{elo1.Id}' fechar -- "
			+ "o marco esperou a vez, ele nao se perdeu",
			$"a '{elo1.Id}' volta a NAO ter terminado (um chefe dela ainda a caminho)",
			AProximaDestrava,
			() =>
			{
				// RESSUSCITA O ELO 1 SEM RESSUSCITAR O CHEFE: um chefe `ACaminho` faz o elo voltar a
				// contar sem precisar de corpo no mundo. Deixar o elo `EmCurso` sem chefe nenhum nao
				// serviria -- o `MonitorarElos` o fecharia como vencido no mesmo tique, e o defeito se
				// desfaria sozinho antes de o criterio olhar.
				EstadoDoElo e2 = Elo(elo2.Id);
				e2.Disparou = false; e2.Fase = FaseDaSaga.Adormecida; e2.Chefes.Clear();
				est1.Fase = FaseDaSaga.Contando;
				ec1.Fase = FaseDoChefe.ACaminho;
				ec1.DiasRestantes = 5;
			},
			() =>
			{
				ec1.Fase = FaseDoChefe.Derrotado;
				ec1.DiasRestantes = 0;
				est1.Fase = FaseDaSaga.Vencida;
			});

		EscutaDasSagas = null;
	}

	// =====================================================================
	// FAMILIA 6 -- TEM A FORMA E NAO A USA
	// =====================================================================
	/// <summary>
	/// ============================ O CASO QUE SEPARA "NAO PODE" DE "NAO QUER" ============================
	/// Um corpo parado na base pode estar parado por dois motivos completamente diferentes: porque a
	/// escada dele nao tem degrau nenhum (nao pode) ou porque ele decidiu nao subir (nao quer). Uma
	/// checagem de `NaBase` sozinha da o mesmo verde nos dois casos -- e o pior dos dois e o primeiro,
	/// porque ele significa que a ficha do chefe nunca abriu forma nenhuma e ninguem percebeu.
	///
	/// Entao o criterio tem DUAS metades obrigatorias: **ele TEM a forma** (`Despertou`) **e ele fica na
	/// base** depois de a mesma funcao da tecla C ser chamada. E o controle e um cidadao, que fica na
	/// base pelo outro motivo -- a bancada afirma que a primeira metade reprova nele.
	/// ================================================================================================
	///
	/// ============================ E O CONTROLE MEXE EM UM BIT SO ============================
	/// A `--npcteste` faz o controle tirando o `Papel` do corpo, o que funciona e prova menos: tirar o
	/// papel tira sete coisas de uma vez (o molde, o degrau, o pino, a presa do roteiro...). Aqui o
	/// controle e o proprio `ascendePorDecisao` -- o `ai_no_powerup` do original --, entao o mesmo corpo,
	/// com a mesma forma, o mesmo BP e o mesmo papel muda de comportamento por causa de UM bit. E o bit
	/// e exatamente o que o dono pediu.
	/// ====================================================================================
	/// </summary>
	private void FamiliaDaForma(Checagem Checa, List<ServerPlayer> forjados)
	{
		GD.Print("\n  ### FAMILIA 6 -- tem a forma e nao a usa ###");

		var zona = ZoneKey.Premade("Vegeta");
		ServerPlayer? g = NascerNpc("guardiao_saiyajin", zona, PontoDeHabitante(zona, 980_001), 980_001);
		if (g?.Papel == null) { Checa("o chefe com ficha pronta nasceu", false); return; }
		forjados.Add(g);

		string aForma = g.Papel.Molde.Formas.FirstOrDefault() ?? "ssj1";

		bool TemAForma(ServerPlayer quem) => quem.Forma.Despertou(aForma);

		// O CRITERIO, com as duas metades juntas -- separa-las em duas checagens deixaria a porta aberta
		// pra alguem "consertar" a segunda apagando a primeira.
		bool TemENaoUsa()
		{
			for (int i = 0; i < 3; i++) Transformar(g, subir: true);
			return TemAForma(g) && g.Forma.NaBase;
		}

		Checa($"o chefe com ficha pronta TEM a forma '{aForma}' (o `Despertou()` dele e verdadeiro, "
			+ "o multiplicador existe)", TemAForma(g), string.Join(",", g.Forma.Liberadas));

		Mutacao(Checa,
			"...e apertar C tres vezes NAO o move da base (a guarda mora no funil `Transformar`, "
			+ "e nao num comentario)",
			"o `ascendePorDecisao` religado no molde -- o `ai_no_powerup` do original desligado",
			TemENaoUsa,
			() => g.Papel!.Molde.AscendePorDecisao = true,
			() =>
			{
				g.Papel!.Molde.AscendePorDecisao = false;
				while (!g.Forma.NaBase) Transformar(g, subir: false);
			});

		// ---- O CONTROLE: quem fica na base porque NAO PODE --------------------
		ServerPlayer? cid = _players.Values.FirstOrDefault(p => p.Papel is { Tipo: TipoDeNpc.Cidadao });
		if (cid == null) Checa("ha um cidadao no mundo pra servir de controle", false);
		else
		{
			for (int i = 0; i < 3; i++) Transformar(cid, subir: true);
			Checa("o CIDADAO tambem fica na base -- e uma checagem que so olhasse `NaBase` teria "
				+ "chamado isso de 'chefe bem comportado'", cid.Forma.NaBase, cid.Forma.Atual);
			Checa("...mas ele NAO tem a forma, e e por isso que a primeira metade do criterio existe "
				+ "(`nao pode` != `nao quer`)", !TemAForma(cid),
				  string.Join(",", cid.Forma.Liberadas));
		}
	}

	// =====================================================================
	// FAMILIA 7 -- A SAGA SOBREVIVE AO REINICIO
	// =====================================================================
	/// <summary>
	/// ============================ "SOBREVIVE" TEM DOIS JEITOS DE SER MENTIRA ============================
	/// O primeiro e obvio -- o save nao existe, e o mundo volta sem saga nenhuma. O segundo e o que
	/// custa caro: o save existe, o chefe volta, **e um campo dele nao atravessou**. Foi assim que o
	/// autor do original perdeu o Boo ativo no reboot (*"faltava esta linha"*, BossEvents.dm:940), e e
	/// literalmente o que um `[JsonIgnore]` no campo errado faz aqui: o corpo volta, no degrau certo, e
	/// com o BP do arquivo em vez do BP que foi anunciado ao servidor inteiro dias atras.
	///
	/// Os dois sao injetados. Uma checagem que so afirmasse "o chefe voltou" ficaria verde no segundo.
	/// ================================================================================================
	/// </summary>
	private void FamiliaDoReinicio(Checagem Checa)
	{
		GD.Print("\n  ### FAMILIA 7 -- a saga sobrevive ao reinicio ###");

		if (_cadeia.Length < 2) { Checa("a cadeia tem o elo 2 pra medir o reinicio", false); return; }
		EloDaSaga elo2 = _cadeia[1];
		EstadoDoElo est2 = Elo(elo2.Id);
		if (est2.Chefes.Count == 0) { Checa("a saga 2 esta correndo pra medir o reinicio", false); return; }

		EstadoDoChefe ec = est2.Chefes[0];
		for (int d = 0; d <= 10 && ec.Fase == FaseDoChefe.ACaminho; d++) AvancarUmDia();

		ServerPlayer? corpo = Corpo(ec);
		if (corpo?.Papel == null) { Checa($"o chefe de '{elo2.Id}' chegou ao mundo", false, $"{ec.Fase}"); return; }

		// SOBE DOIS DEGRAUS PELO CAMINHO DE PRODUCAO (o roteiro le o pior membro). Voltar no degrau 1
		// seria uma checagem mais fraca: o degrau 1 e o valor inicial do campo, entao um `Degrau` que
		// nao persistisse passaria.
		for (int passo = 0; passo < 2 && corpo.Papel.GatilhoAtual >= 0; passo++)
		{
			PorPiorMembroEm(corpo, corpo.Papel.GatilhoAtual - 0.01);
			TickDoRoteiro();
		}
		TickDasSagas();                        // copia o degrau pro estado da saga
		SalvarSagas();

		int degrauSalvo = ec.Degrau;
		double bpSalvo = corpo.Ficha.BP;
		double doArquivo = corpo.Papel.Molde.Estagios[degrauSalvo].Bp;

		GD.Print($"  ---- antes do 'reinicio': '{corpo.Name}' no degrau {degrauSalvo + 1}, "
			   + $"BP {bpSalvo:N0} (a ficha do npcs.json diz {doArquivo:N0}) ----");
		Checa("o BP pinado e diferente do BP do arquivo -- sem isso, apagar o pino do save nao mudaria "
			+ "nada e a checagem do reinicio ficaria verde com o defeito no ar",
			  Math.Abs(bpSalvo - doArquivo) > 1, $"{bpSalvo:N0} vs {doArquivo:N0}");

		string bom = System.IO.File.Exists(CaminhoDasSagas)
			? System.IO.File.ReadAllText(CaminhoDasSagas) : "";
		Checa("o estado da cadeia foi mesmo pro disco", bom.Length > 0);

		// ---- O CRITERIO: o reinicio de verdade -------------------------------
		// O mundo esquece TUDO e le do disco. Nao ha atalho aqui: `CarregarSagas` e `TickDasSagas` sao
		// as duas funcoes que o servidor chama no boot e no primeiro tique.
		bool VoltouIgual()
		{
			foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
			_sagas = new EstadoDasSagas();
			CarregarSagas();
			TickDasSagas();

			EstadoDoChefe? depois = Elo(elo2.Id).Chefes.FirstOrDefault();
			ServerPlayer? voltou = depois == null ? null : Corpo(depois);
			return voltou?.Papel != null
				&& voltou.Papel.Estagio == degrauSalvo
				&& Math.Abs(voltou.Ficha.BP - bpSalvo) < 1
				&& Elo(_cadeia[0].Id).Fase == FaseDaSaga.Vencida;
		}

		Mutacao(Checa,
			$"depois do reinicio o chefe VOLTA ao mundo, no degrau {degrauSalvo + 1} e com o BP pinado "
			+ $"({bpSalvo:N0}) -- e a saga ja vencida continua vencida",
			"o `sagas.json` apagado -- o save que nunca foi escrito",
			VoltouIgual,
			() => { if (System.IO.File.Exists(CaminhoDasSagas)) System.IO.File.Delete(CaminhoDasSagas); },
			() => System.IO.File.WriteAllText(CaminhoDasSagas, bom));

		// ---- E O SORRATEIRO: o save inteiro, menos o vetor do BP -------------
		Mutacao(Checa,
			"o mesmo criterio depois de o arquivo ser reescrito igualzinho (o controle: reescrever nao "
			+ "estraga)",
			"o save COMPLETO menos o vetor `Bps` -- o campo que um `[JsonIgnore]` errado apagaria",
			VoltouIgual,
			() => System.IO.File.WriteAllText(CaminhoDasSagas, SemOPino(bom)),
			() => System.IO.File.WriteAllText(CaminhoDasSagas, bom));
	}

	/// <summary>
	/// O MESMO SAVE, com o vetor de BP pinado esvaziado em todo chefe de todo elo. Nao e um arquivo
	/// escrito a mao: e o arquivo de VERDADE passado por uma tesoura, pra que o unico campo diferente
	/// entre o texto bom e o torto seja o que se quer medir.
	/// </summary>
	private static string SemOPino(string json)
	{
		JsonNode? raiz = JsonNode.Parse(json);
		if (raiz?["Elos"] is not JsonArray elos) return json;

		foreach (JsonNode? e in elos)
			if (e?["Chefes"] is JsonArray chefes)
				foreach (JsonNode? c in chefes)
					if (c != null) c["Bps"] = new JsonArray();

		return raiz.ToJsonString();
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// POVOA PELO TIQUE DE PRODUCAO. Nao ha laco proprio chamando `NascerNpc`: o que roda aqui e o
	/// mesmo <see cref="TickDoPovoamento"/> da linha 2575 do tique do servidor, com os mesmos tetos.
	///
	/// A manutencao e forcada na primeira volta (`_proximaManutencao = 0`) porque ela e de 5 em 5
	/// minutos e uma bancada nao espera cinco minutos -- adiantar o RELOGIO dela e o mesmo gesto que o
	/// `_adiantoDoCeu` faz com os dias in-game.
	/// </summary>
	private int PovoarPeloTique(int maxTiques)
	{
		_proximaManutencao = 0;
		int t = 0;
		for (; t < maxTiques; t++)
		{
			TickDoPovoamento(Protocol.TickSeconds);
			if (t > 0 && _filaDoPovoamento.Count == 0 && !FaltaHabitante()) break;
		}
		return t;

		bool FaltaHabitante() =>
			_moldes!.Plano.Any(l => ContarHabitantes(l.Planeta, l.Molde) < l.Quantos);
	}
}
