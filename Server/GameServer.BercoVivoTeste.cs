using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DO BERCO (`--bercovivo`) -- a IRMA da `--diagberco`.
///
/// ============================ A DIVISAO DE TRABALHO ENTRE AS DUAS ============================
/// A `--diagberco` mede a REGRA: uma funcao pura mais o catalogo de zonas, sem corpo nenhum. Ela
/// responde "o berco deste personagem seria X" e confere X contra a colisao do mapa.
///
/// Esta aqui mede a CORRENTE, que e onde este port ja quebrou muitas vezes:
///
///     ficha no disco -> `AccountStore.ParaJogador` -> `BercoDe` -> `DestinoDoBerco` -> corpo no
///     mundo -> `TickDoEspaco` -> `PousarEmProcedural` -> geracao fora do tique -> `MoveToZone`
///     -> um par (zona, ponto) em que o chao EXISTE.
///
/// A diferenca nao e academica: conferir uma coordenada contra um `.col` e conferir a REGRA duas
/// vezes (foi a regra que escolheu a coordenada). Poe-se um corpo la e roda-se o tique, e aparecem
/// as coisas que nenhuma leitura mostra -- que o mundo nunca foi encomendado, que o corpo ficou em
/// orbita pra sempre, que a chegada do pre-feito e o (249,250) da TERRA em todo mapa.
/// ==========================================================================================
///
/// ============================ AS SETE FAMILIAS, E COMO CADA UMA REPROVA ============================
///  1. CADA RACA NO SEU PLANETA -- varredura do CONJUNTO das racas (o `races.json` extraido do DM
///     mais as do menu de criacao), nunca uma lista escrita a mao aqui. Reprova se uma raca nova
///     entrar no jogo sem berco, se a tabela desabar num planeta so, ou se o mundo natal nao tiver
///     ficha propria no `planetas.json` (o defeito de nome que a spec ja pegou na Sala do Tempo:
///     a busca erra, cai no padrao, e a gravidade fica 1 calada).
///  2. O CLASSE BAIXA E O CONTRA-EXEMPLO -- o Saiyajin de classe baixa na Terra **e o de classe alta
///     NAO**. Sem a segunda metade, "todo Saiyajin na Terra" passaria verde. As classes saem do
///     `Birth.RollClass` de verdade, sorteadas, e nao de uma lista.
///  3. O EXILIO -- LONGE, DIFERENTE por personagem e IGUAL pro mesmo personagem em dois
///     carregamentos. As tres juntas: "sempre igual" seria nao sortear, "sempre diferente" seria
///     rerrolar a cada login, e cada uma sozinha passa verde no bug da outra.
///  4. O BERCO TEM CHAO -- corpo posto no mundo e POUSADO pelo funil do jogo, nao coordenada
///     conferida. Reprova se alguem ficar no espaco, se a zona nao tiver colisao, ou se o ponto
///     for parede.
///  5. NASCER == RENASCER -- o que o `CreateChar` grava no save, onde o corpo pousa, e onde o
///     `Renascer` o devolve. E a checagem que pega o MEIO-CONSERTO (ligar o berco so no
///     nascimento e ressuscitar todo mundo na Terra).
///  6. O PERSONAGEM ANTIGO -- save sem o campo carrega, ganha berco derivado e estavel entre dois
///     carregamentos DE DISCO. E o RENOMEADO, que e o unico caso em que a semente gravada e a
///     derivada divergem -- ou seja, o unico teste que prova que o campo viaja no save.
///  7. AS DUAS PONTAS -- a assinatura do terreno, como o resto do universo. O cliente refaz o
///     mundo a partir do ENDERECO (Sx,Sy,K) e tem que chegar no mesmo numero que o servidor gerou.
/// ==============================================================================================
///
/// ============================ ELA CHAMA O CODIGO DE PRODUCAO, E SO ELE ============================
/// Nenhuma linha aqui recalcula regra nem monta atalho. O corpo nasce por `Birth.Nascer`, vira save
/// pelo mesmo caminho do `CreateChar`, vira jogador por `AccountStore.ParaJogador`, entra no mundo
/// por `PorNoMundo`, pousa por `TickDoEspaco` e morre por `Renascer`. O disco e o `AccountStore`.
/// Uma bancada com caminho proprio testaria o atalho (regra 0.7).
///
/// O UNICO PRIVILEGIO e a CADENCIA: o laco de pouso chama `TickDasGeracoes`/`TickDoEspaco` na mao
/// em vez de esperar o `_Process`. Sem isso a bancada nao rodaria no boot -- e ela precisa rodar no
/// boot pra nao depender de ninguem logar.
/// =============================================================================================
///
///     Godot --headless --path . --server --port 7953 --bercovivo
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- longe do `_nextId`, do convivio (90.100) e do sol (90.400).</summary>
	private const int IdBaseDoBercoVivo = 90_600;

	/// <summary>
	/// Teto de tiques por pouso. 30 Hz x 900 = 30 s in-game.
	///
	/// Ele existe pra que "o corpo nunca pousou" seja uma FALHA e nao um travamento: o pouso num
	/// mundo frio espera uma thread do pool, e se `TickDasGeracoes` parasse de colher (ou se o
	/// `PousarEmProcedural` deixasse de ser chamado) o laco rodaria pra sempre em silencio.
	/// </summary>
	private const int TiquesDePouso = 900;

	/// <summary>
	/// A conta de disco desta bancada. Ela e GRAVADA de verdade e APAGADA no fim -- ver
	/// <see cref="ApagarContaDaBancada"/>. Serializar por fora seria um segundo caminho de save, e
	/// e justamente o caminho de save que a familia 6 desconfia.
	/// </summary>
	private const string ContaDaBancadaDeBerco = "bancada_berco_vivo";

	private int _bvOk, _bvFalhou;

	private void Afirmar(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _bvOk++; GD.Print($"[bercovivo]   OK    {oque}"); return; }
		_bvFalhou++;
		GD.PrintErr($"[bercovivo]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoBercoVivo()
	{
		_bvOk = _bvFalhou = 0;
		GD.Print("[bercovivo] ================ O BERCO, COM CORPO E COM CHAO ================");

		try
		{
			CadaRacaNoSeuPlaneta();
			OClasseBaixaEOContraExemplo();
			OExilioDoLendario();
			TodoBercoTemChao();
			NascerEhOMesmoQueRenascer();
			OPersonagemAntigoEORenomeado();
			AsDuasPontasConcordam();
		}
		finally
		{
			// A CONTA SAI MESMO SE A BANCADA ESTOURAR. Um arquivo de bancada esquecido na pasta de
			// saves vira uma conta de verdade no painel de admin -- e ele so seria notado por quem
			// abrisse o painel meses depois.
			ApagarContaDaBancada();
		}

		GD.Print($"[bercovivo] ================ {_bvOk} passaram, {_bvFalhou} falharam ================");
	}

	// =====================================================================
	// 1) CADA RACA NASCE NO PLANETA DELA
	// =====================================================================
	/// <summary>
	/// A varredura e do CONJUNTO, e o conjunto vem de duas fontes independentes: o `races.json`
	/// (extraido do DM pelo AssetPipeline -- 24 racas, e ele conhece a "Dog", que nenhuma lista
	/// escrita a mao lembraria) e o menu de criacao (`CharacterDraft.RacasDoPlaneta`, os gates `can*`
	/// do BYOND). A uniao das duas e o que o jogo pode entregar; qualquer raca que entre amanha em
	/// qualquer uma das duas cai nesta bancada sozinha.
	///
	/// A CLASSE E SORTEADA DE VERDADE (`Birth.RollClass`, via `Birth.Nascer`) e nao cravada em
	/// "Normal": cravar esconderia justamente as duas excecoes do dono, que sao consequencia da
	/// classe. Por isso a afirmacao e "o berco e o natal OU uma das duas excecoes declaradas" -- ela
	/// reprova qualquer desvio que nao seja um dos dois que o dono pediu.
	/// </summary>
	private void CadaRacaNoSeuPlaneta()
	{
		GD.Print("[bercovivo] -- 1) CADA RACA NO PLANETA DELA (varredura do conjunto, com pouso)");

		List<string> racas = ConjuntoDeRacas();
		Afirmar($"o conjunto de racas foi lido das duas fontes ({racas.Count} racas)", racas.Count >= 20,
				string.Join(",", racas));

		var porMundo = new Dictionary<string, int>(StringComparer.Ordinal);
		var rng = new Random(20260812);
		int excecoes = 0, semChao = 0;

		foreach (string raca in racas)
		{
			// A LINHAGEM E A PRIMEIRA DO MENU quando a raca tem escolha. Nao e detalhe: sem ela o
			// Namekuseijin nasceria com a classe "None" e o Saiyajin sem `SaiyanLineage`, e a segunda
			// trava do exilio (a da linhagem) nunca seria exercitada.
			string[] escolhas = CharacterDraft.EscolhasDeClasse(raca);
			string linhagem = escolhas.Length > 0 ? escolhas[0] : "";

			CharacterSave c = SaveDeBancada($"Cobaia {raca}", raca, linhagem, "", false, rng);
			Berco b = BercoDe(c);
			string natal = Bercos.PlanetaNatal(raca);

			bool excecao = b.Despejado || b.Motivo == MotivoDoBerco.ExilioDoLendario;
			if (excecao) excecoes++;

			Afirmar($"{raca}: o berco e o natal da raca, ou uma excecao declarada",
					b.Planeta == natal || excecao, $"nasceu em {b.Planeta}, natal {natal}, motivo {b.Motivo}");

			// A FICHA DO MUNDO TEM QUE EXISTIR NO `planetas.json`. Este e o defeito que a spec ja
			// documentou na Sala do Tempo: o nome da zona nao bate com o do catalogo, a busca cai no
			// padrao e a gravidade fica 1 -- calada, pra sempre. Enquanto todo mundo nascia na Terra
			// (gravidade 1 mesmo) isso era invisivel; com berco de verdade, o Frost Demon nasce num
			// mundo de 15g e a diferenca passa a valer treino.
			Afirmar($"{raca}: o natal {natal} tem ficha propria no planetas.json",
					TemFichaPropria(natal), "cai na ficha PADRAO -- gravidade 1 calada");

			porMundo[b.Planeta] = porMundo.GetValueOrDefault(b.Planeta) + 1;

			// E AGORA O CORPO. Nascer e pousar pelo funil de verdade -- ver `TodoBercoTemChao` pro
			// porque de isso nao ser a mesma pergunta que "a coordenada esta livre".
			ServerPlayer pl = CorpoDoSave(c, 1);
			try
			{
				int tiques = PousarDeVerdade(pl);
				bool chao = PisouEmChao(pl, out string porque);
				if (!chao) semChao++;

				Afirmar($"{raca}: o corpo pisou em chao de verdade ({porque})", chao && tiques > 0,
						tiques < 0 ? "NUNCA POUSOU" : porque);

				// ============================ E ACORDOU NA ZONA DO BERCO -- A LINHA QUE FALTAVA ============================
				// Esta familia ficou **TODA VERDE (72 provas) com 13 das 24 racas acordando em Namek**. O
				// motivo esta na linha de cima e na de baixo: ela afirmava `b.Planeta` -- o resultado da
				// funcao PURA `Bercos.Onde`, que continuava certissima ("Earth" pra quem e da Terra) -- e a
				// zona em que o corpo de fato acordou era so IMPRESSA no console. Com a Terra marcada como
				// destruida no save, o funil desviava o corpo pro primeiro pre-feito vivo e nenhuma linha
				// ficava vermelha: a bancada media a INTENCAO, e o jogador vive o EFEITO.
				//
				// A comparacao e pelo NOME e nao pela `ZoneKey` inteira de proposito: o berco gerado (o
				// exilio do Lendario, o vizinho) chega pelo espaco e o pouso monta a zona dele; o que se
				// afirma aqui e "acordou no planeta que o berco prometeu", que e a frase do dono.
				// ======================================================================================================
				Afirmar($"{raca}: o corpo acordou NA ZONA do berco ({b.Planeta})",
						pl.Zone.Name == b.Planeta,
						$"o berco prometeu {b.Planeta} e o corpo acordou em {pl.Zone.Name}"
						+ (ZonaMorta(b.Zona) ? $" -- '{b.Planeta}' esta DESTRUIDO no save deste servidor" : ""));

				GD.Print($"[bercovivo]      {raca,-14} {c.Ficha.Class,-24} -> {b.Planeta,-18}"
					+ $" {(b.PreFeito ? "pre-feito" : "gerado   ")} | {pl.Zone.Name} @ "
					+ $"({pl.Pos.X / ZoneCollision.TileSize:0},{pl.Pos.Y / ZoneCollision.TileSize:0}) | {tiques} tique(s)");
			}
			finally { Recolher(pl); }
		}

		// ============================ A TABELA NAO PODE SER DEGENERADA ============================
		// "Todo mundo na Terra" satisfaz "cada raca nasce no planeta dela" se a tabela disser Terra
		// pra todos -- e e exatamente o estado de onde este sistema partiu (a `SpawnZone` cravada).
		// Sem estas duas linhas, apagar a tabela inteira deixaria a familia 1 verde.
		// =====================================================================================
		Afirmar($"a tabela espalha as racas por {porMundo.Count} mundos distintos", porMundo.Count >= 5,
				string.Join(", ", porMundo.Select(k => $"{k.Key}={k.Value}")));
		Afirmar("nenhum mundo unico recebe TODAS as racas",
				porMundo.Values.Max() < racas.Count, $"maior grupo: {porMundo.Values.Max()} de {racas.Count}");
		Afirmar("todas as racas do conjunto acharam chao", semChao == 0, $"{semChao} sem chao");

		GD.Print($"[bercovivo]      {porMundo.Count} mundos | {excecoes} excecao(oes) sorteada(s) na varredura");
	}

	/// <summary>
	/// O CONJUNTO DE RACAS -- uniao das duas fontes que o jogo tem, sem nenhuma escrita aqui.
	///
	/// `races.json` e o que o AssetPipeline extraiu do DM (e o que o `Birth.Nascer` consulta);
	/// `RacasDoPlaneta` e o que a tela de criacao oferece. As duas podem divergir -- e divergem: o
	/// Halfbreed e o Android nao estao no menu, a "Dog" nao esta em lugar nenhum da tela --, e e a
	/// uniao que responde "o que o jogo pode entregar".
	/// </summary>
	private List<string> ConjuntoDeRacas()
	{
		var set = new SortedSet<string>(StringComparer.Ordinal);
		if (_racas != null)
			foreach (string r in _racas.Protos.Keys) set.Add(r);

		foreach (string planeta in CharacterDraft.Planetas)
			foreach (string r in CharacterDraft.RacasDoPlaneta(planeta)) set.Add(r);

		return [.. set];
	}

	/// <summary>
	/// ESTA ZONA TEM FICHA PROPRIA NO `planetas.json`, ou cai no padrao?
	///
	/// `CatalogoDePlanetas.De` NUNCA devolve nulo -- zona desconhecida ganha uma ficha nova de
	/// gravidade 1 (o `Planetgrav = 1` de antes do `switch` do DM). Isso e o certo em jogo e e um
	/// pesadelo pra bancada, porque o modo de falha e uma resposta VALIDA. O jeito de distinguir e
	/// perguntar se o objeto devolvido e um dos que o arquivo produziu.
	/// </summary>
	private bool TemFichaPropria(string zona) =>
		_planetas != null && _planetas.Todas.Any(f => ReferenceEquals(f, _planetas.De(zona)));

	// =====================================================================
	// 2) O CLASSE BAIXA -- E O CONTRA-EXEMPLO
	// =====================================================================
	/// <summary>
	/// *"saiyajin de classe baixa deve nascer na TERRA ou em um planeta proximo a terra"* -- e a
	/// outra metade, que e o que faz a afirmacao significar alguma coisa: **o de classe ALTA nao**.
	///
	/// AS CLASSES SAO SORTEADAS, e nao listadas: `Birth.RollClass` e a fonte, com as duas linhagens
	/// Saiyajin. Uma lista escrita aqui envelheceria calada no dia em que a tabela do
	/// `statsaiyan.dm` ganhasse um degrau -- e o degrau novo nasceria sem berco testado.
	///
	/// E as DUAS metades do "ou" entram: sem `pediuVizinho` o classe-baixa nasce NA Terra, com
	/// `pediuVizinho` ele nasce numa orbita irma da estrela DA TERRA (e nao da de Vegeta) -- que e o
	/// jeito como a excecao foi escrita: ela reescreve o NATAL, nao o destino.
	/// </summary>
	private void OClasseBaixaEOContraExemplo()
	{
		GD.Print("[bercovivo] -- 2) O CLASSE BAIXA VAI PRA TERRA, O CLASSE ALTA NAO");

		var rng = new Random(4242);
		var classes = new SortedSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < 20_000; i++)
		{
			classes.Add(Birth.RollClass(_racas!, "Saiyan", "Saiyan", rng));
			classes.Add(Birth.RollClass(_racas!, "Saiyan", "Primal Saiyan", rng));
		}
		Afirmar($"o sorteio de classe do Saiyajin produziu {classes.Count} classes", classes.Count >= 5,
				string.Join(" / ", classes));

		List<PlanetaNoEspaco> irmasDaTerra = Bercos.IrmasDoNatal("Earth");
		List<PlanetaNoEspaco> irmasDeVegeta = Bercos.IrmasDoNatal("Vegeta");
		int altosNaTerra = 0;

		foreach (string classe in classes)
			foreach (bool perto in new[] { false, true })
			{
				string linhagem = classe.Contains("Primal") ? "Primal Saiyan" : "Saiyan";
				CharacterSave c = SaveDeBancada($"Saiyajin {classe} {perto}", "Saiyan", linhagem, classe, perto, rng);
				Berco b = BercoDe(c);

				bool lendario = Bercos.EhLendario("Saiyan", classe, linhagem);
				bool baixa = classe == "Low-Class";

				if (lendario)
				{
					// O Lendario nao e desta familia -- ele so entra aqui pra provar que NAO cai na
					// regra do classe-baixa (nem na do classe-alta).
					Afirmar($"{classe}: nao e despejado nem fica no natal (ele e exilado)",
							b.Motivo == MotivoDoBerco.ExilioDoLendario && !b.Despejado, b.Motivo.ToString());
					continue;
				}

				if (baixa)
				{
					Afirmar($"{classe} (perto={perto}): o natal foi REESCRITO pra Terra",
							b.Despejado && b.Natal == "Earth", $"natal {b.Natal}, despejado {b.Despejado}");

					Afirmar($"{classe} (perto={perto}): nasce na Terra ou numa irma DELA",
							perto
								? irmasDaTerra.Any(p => p.Nome == b.Planeta && p.Seed == b.Seed)
								: b.Planeta == "Earth",
							$"nasceu em {b.Planeta}");
				}
				else
				{
					// ============================ O CONTRA-EXEMPLO ============================
					// Sem esta metade, uma regra que mandasse TODO Saiyajin pra Terra passaria verde
					// na metade de cima. Ela e a unica linha da familia 2 que reprova o excesso.
					// =====================================================================
					if (b.Planeta == "Earth" || irmasDaTerra.Any(p => p.Nome == b.Planeta)) altosNaTerra++;

					Afirmar($"{classe} (perto={perto}): NAO e despejado -- o natal continua Vegeta",
							!b.Despejado && b.Natal == "Vegeta", $"natal {b.Natal}, despejado {b.Despejado}");

					Afirmar($"{classe} (perto={perto}): nasce em Vegeta ou numa irma DELA, nunca na Terra",
							perto
								? irmasDeVegeta.Any(p => p.Nome == b.Planeta && p.Seed == b.Seed)
								: b.Planeta == "Vegeta",
							$"nasceu em {b.Planeta}");
				}

				GD.Print($"[bercovivo]      {classe,-24} perto={perto,-5} -> {b.Planeta,-18} natal {b.Natal} ({b.Motivo})");
			}

		Afirmar("nenhuma classe alta caiu na vizinhanca da Terra", altosNaTerra == 0, $"{altosNaTerra} caso(s)");

		// E O CORPO, pros dois: o classe-baixa tem que ACORDAR na Terra e o Elite em Vegeta. A regra
		// ja foi conferida acima; isto confere que o funil nao desfaz o que a regra decidiu.
		PousarEConferirMundo("Saiyan", "Saiyan", "Low-Class", perto: false, esperado: "Earth");
		PousarEConferirMundo("Saiyan", "Saiyan", "Elite", perto: false, esperado: "Vegeta");
		PousarEConferirMundo("Saiyan", "Saiyan", "Normal", perto: false, esperado: "Vegeta");
	}

	/// <summary>Nasce, pousa pelo funil e afirma em que ZONA o corpo parou.</summary>
	private void PousarEConferirMundo(string raca, string linhagem, string classe, bool perto, string esperado)
	{
		CharacterSave c = SaveDeBancada($"Pouso {raca} {classe}", raca, linhagem, classe, perto, new Random(7));
		ServerPlayer pl = CorpoDoSave(c, 2);
		try
		{
			PousarDeVerdade(pl);
			Afirmar($"{raca}/{classe}: o corpo acordou em {esperado}",
					pl.Zone.Name == esperado && PisouEmChao(pl, out _), $"acordou em {pl.Zone.Name}");
		}
		finally { Recolher(pl); }
	}

	// =====================================================================
	// 3) O EXILIO DO LENDARIO
	// =====================================================================
	/// <summary>
	/// *"um LSSJ e enviado sempre a um planeta ALEATORIO pelo fato da sociedade saiyajin ter MEDO
	/// dele"* -- tres afirmacoes que so valem JUNTAS:
	///
	///   LONGE     -- a distancia ate a estrela de Vegeta em celulas, contra o anel declarado. Sem
	///                ela, "aleatorio" poderia devolver a orbita ao lado e o exilio seria uma
	///                mudanca de bairro;
	///   DIFERENTE -- enderecos distintos entre personagens. Sozinha, ela passa verde num
	///                `Random.Shared` (que seria o bug pior de todos);
	///   IGUAL     -- o MESMO personagem, lido do disco duas vezes, no mesmo lugar. Sozinha, ela
	///                passa verde numa constante.
	///
	/// Cada uma sozinha e compativel com um defeito que a outra pega. E por isso que as tres estao
	/// na mesma familia, e nao espalhadas.
	/// </summary>
	private void OExilioDoLendario()
	{
		GD.Print("[bercovivo] -- 3) O LENDARIO: longe, diferente por personagem, igual em dois carregamentos");

		SistemaSolar vegeta = Sistemas.ComPreFeito.First(s =>
			string.Equals(s.PreFeito.Nome, "Vegeta", StringComparison.OrdinalIgnoreCase));

		const int Amostras = 400;
		var enderecos = new HashSet<(int, int, int)>();
		var rng = new Random(1);
		int exilados = 0, ignoraramOPedido = 0, minCel = int.MaxValue, maxCel = 0;

		for (int i = 0; i < Amostras; i++)
		{
			bool perto = i % 2 == 0;
			CharacterSave c = SaveDeBancada($"Lendario {i}", "Saiyan", "Saiyan", "Legendary", perto, rng);
			Berco b = BercoDe(c);

			if (b.Motivo == MotivoDoBerco.ExilioDoLendario) exilados++;
			// O PEDIDO DO JOGADOR E IGNORADO NO EXILIO -- metade das amostras pediu vizinho.
			if (perto && b.Motivo != MotivoDoBerco.VizinhoDoNatal) ignoraramOPedido++;

			enderecos.Add((b.Sx, b.Sy, b.K));
			int cel = Math.Max(Math.Abs(b.Sx - vegeta.Id.Sx), Math.Abs(b.Sy - vegeta.Id.Sy));
			minCel = Math.Min(minCel, cel);
			maxCel = Math.Max(maxCel, cel);
		}

		Afirmar($"todo Lendario e exilado ({exilados}/{Amostras})", exilados == Amostras);
		Afirmar($"o exilio ignora o pedido de nascer perto de casa ({ignoraramOPedido}/{Amostras / 2})",
				ignoraramOPedido == Amostras / 2);
		Afirmar($"LONGE: o mais perto ficou a {minCel} celulas de Vegeta (anel minimo {Bercos.ExilioAnelMinimo})",
				minCel >= Bercos.ExilioAnelMinimo, $"{minCel} celulas");
		Afirmar($"...e o mais longe a {maxCel} (anel maximo {Bercos.ExilioAnelMaximo})",
				maxCel <= Bercos.ExilioAnelMaximo, $"{maxCel} celulas");
		Afirmar($"DIFERENTE: {enderecos.Count} enderecos distintos em {Amostras} Lendarios",
				enderecos.Count >= Amostras * 0.9, $"so {enderecos.Count}");

		// ============================ IGUAL EM DOIS CARREGAMENTOS ============================
		// E o disco de verdade, e nao duas chamadas seguidas da mesma funcao: o que precisa ser
		// provado e que o berco sobrevive a ida e volta pelo `AccountStore` -- que e o caminho que
		// existe entre um login e o proximo. Duas chamadas em memoria provariam so que a funcao e
		// pura, que a `--diagberco` ja mede.
		// =============================================================================
		for (int i = 0; i < 3; i++)
		{
			CharacterSave c = SaveDeBancada($"Lendario {i}", "Saiyan", "Saiyan", "Legendary", false, rng);
			Berco antes = BercoDe(c);
			Berco depois = BercoDepoisDeDoisCarregamentos(c, i, out string trilha);

			Afirmar($"IGUAL: 'Lendario {i}' volta do disco no mesmo exilio ({antes.Planeta})",
					MesmoLugar(antes, depois), $"{antes.Planeta} [{antes.Sx}:{antes.Sy}]k{antes.K} -> {trilha}");
		}

		// E ELE POUSA. O planeta do exilio e gerado e frio -- ninguem nunca viajou ate la.
		for (int i = 0; i < 2; i++)
		{
			CharacterSave c = SaveDeBancada($"Lendario exilado {i}", "Saiyan", "Saiyan", "Legendary", false, rng);
			ServerPlayer pl = CorpoDoSave(c, 3);
			try
			{
				int tiques = PousarDeVerdade(pl);
				Afirmar($"o exilado {i} pousou no mundo dele ({pl.Berco.Planeta}, {pl.Berco.Gravidade:0.#}g)",
						tiques > 0 && PisouEmChao(pl, out _), tiques < 0 ? "NUNCA POUSOU" : pl.Zone.Name);
				Afirmar($"...e o mundo dele passa no crivo de gravidade ({Bercos.GravidadeMaximaDeBerco}g)",
						pl.Berco.Gravidade <= Bercos.GravidadeMaximaDeBerco, $"{pl.Berco.Gravidade}g");
			}
			finally { Recolher(pl); }
		}
	}

	// =====================================================================
	// 4) O BERCO EXISTE E TEM CHAO
	// =====================================================================
	/// <summary>
	/// *"pouse de verdade pelo funil do jogo, nao confira coordenada. Ninguem pode nascer no
	/// espaco."*
	///
	/// Sao TRES caminhos diferentes ate o chao, e so o primeiro deles a `--diagberco` conseguia ver:
	///   * PRE-FEITO -- o corpo ja nasce na zona, e o ponto vem do `PontoLivrePerto`;
	///   * GERADO FRIO -- o corpo nasce em ORBITA e o mundo nem existe. Quem o faz existir e o
	///     `TickDoEspaco` -> `PousarEmProcedural` -> `Encomendar` (thread) -> `TickDasGeracoes`;
	///   * ORBITA SOBRE PRE-FEITO -- o outro ramo do `TickDoEspaco`, que e por onde passa quem cai
	///     num mundo feito a mao vindo do espaco.
	///
	/// O terceiro so entrou nesta bancada porque o berco criou o caso: enquanto ninguem nascia em
	/// Icer, ninguem chegava la de orbita, e a chegada cravada em (249,250) -- o campo aberto da
	/// TERRA -- nunca tinha sido perguntada a colisao de Icer, onde ela e ROCHA.
	/// </summary>
	private void TodoBercoTemChao()
	{
		GD.Print("[bercovivo] -- 4) TODO BERCO TEM CHAO (pouso pelo funil, nos tres caminhos)");

		var rng = new Random(99);

		// --- (a) o VIZINHO de cada pre-feito com raca propria: mundo GERADO, frio, um por sistema.
		(string Raca, string Natal)[] vizinhancas =
		[
			("Human", "Earth"), ("Saiyan", "Vegeta"), ("Namekian", "Namek"),
			("Icer", "Icer"), ("Arlian", "Arlia"), ("Makyo", "Makyo_Star"),
		];

		foreach ((string raca, string natal) in vizinhancas)
		{
			// "Normal" cravado no Saiyajin de proposito: um Low-Class sorteado aqui trocaria a
			// vizinhanca de Vegeta pela da Terra e a bancada mediria outro sistema sem avisar.
			CharacterSave c = SaveDeBancada($"Vizinho {raca}", raca, raca == "Saiyan" ? "Saiyan" : "", "Normal", true, rng);
			Berco b = BercoDe(c);
			ServerPlayer pl = CorpoDoSave(c, 4);
			try
			{
				int tiques = PousarDeVerdade(pl);
				bool chao = PisouEmChao(pl, out string porque);

				Afirmar($"vizinho de {natal} ({b.Planeta}): o corpo pousou em chao", chao && tiques > 0,
						tiques < 0 ? "NUNCA POUSOU -- ficou em orbita" : porque);
				Afirmar($"vizinho de {natal}: e um mundo GERADO, com colisao viva no servidor",
						!b.PreFeito && MapaDaZona(pl.Zone) != null, b.PreFeito ? "pre-feito" : "sem colisao");

				GD.Print($"[bercovivo]      {natal,-12} -> {b.Planeta,-18} {b.Gravidade:0.#}g | {tiques} tique(s)"
					+ $" | chegada ({pl.Pos.X / ZoneCollision.TileSize:0},{pl.Pos.Y / ZoneCollision.TileSize:0})");
			}
			finally { Recolher(pl); }
		}

		// --- (b) O TERCEIRO CAMINHO: cair de orbita num PRE-FEITO.
		//
		// Nao passa por berco nenhum -- e o `TickDoEspaco` puro, o mesmo que atende quem viajou de
		// nave. Entra nesta bancada porque o berco e quem manda gente pra Icer pela primeira vez.
		foreach (PlanetaNoEspaco mundo in Espaco.PreFeitos())
		{
			ServerPlayer pl = CorpoEmOrbita(mundo, 5);
			try
			{
				int tiques = PousarDeVerdade(pl);
				bool chao = PisouEmChao(pl, out string porque);

				Afirmar($"quem cai de orbita em {mundo.Nome} pisa em chao livre", chao && tiques > 0,
						tiques < 0 ? "NUNCA POUSOU" : porque);
				GD.Print($"[bercovivo]      orbita -> {mundo.Nome,-12} caiu em "
					+ $"({pl.Pos.X / ZoneCollision.TileSize:0},{pl.Pos.Y / ZoneCollision.TileSize:0}) | {porque}");
			}
			finally { Recolher(pl); }
		}

		MedirADividaDaGravidadeNatal();
	}

	/// <summary>
	/// A GRAVIDADE NATAL -- **esta divida foi PAGA, e agora isto e uma checagem.**
	///
	/// ============================ O QUE ERA, E O QUE MUDOU ============================
	/// O DM acostuma o corpo a gravidade do PROPRIO BERCO: `GravMastered = max(GravMastered,
	/// PlanetGravity(spawnPlanet))` (`race.dm:130-131`), com o comentario do autor *"so high-grav
	/// races aren't crushed/frozen at spawn"*. O port so tinha a metade da RACA
	/// (<see cref="Birth.GravidadeNatal"/>), e o `max` do berco nunca tinha sido ligado.
	///
	/// Este bloco existia pra MEDIR a divida em vez de reprovar por ela -- a regra certa enquanto a
	/// divida era conhecida e declarada, porque vermelho permanente e vermelho que ninguem le. Ele
	/// media 17 bercos mais pesados que a maestria da raca, e o pior deles (Icer Planet, 15g) dava
	/// razao 15 contra maestria 1.
	///
	/// **A camada 2 da Sala do Tempo ligou o esmagamento**, e com ele a divida deixou de ser teorica:
	/// razao 4 PRENDE o corpo no chao e 3 de vida por segundo comecam a cair. Um Frost Demon nasceria
	/// imovel, morrendo, sem ter dado um passo. Entao o `max` entrou
	/// (<see cref="Birth.AclimatarAoBerco"/>, chamado pelo `AplicarGravidade` e pelo pouso
	/// procedural) e este bloco virou o que ele sempre quis ser: uma checagem.
	///
	/// ELE MEDE O CORPO, E NAO A TABELA. A conta antiga comparava `GravidadeNatal(raca)` com a
	/// gravidade do berco -- e essa diferenca CONTINUA existindo, porque a tabela da raca nao mudou.
	/// O que mudou e o que acontece com o corpo: ele e aclimatado ao chegar. Por isso a checagem
	/// pergunta ao <see cref="Esmagamento"/>, que e quem o jogo consulta.
	/// ==============================================================================
	/// </summary>
	private void MedirADividaDaGravidadeNatal()
	{
		var rng = new Random(555);
		var pesados = new List<string>();
		var esmagados = new List<string>();

		foreach (string raca in ConjuntoDeRacas())
			foreach (bool perto in new[] { false, true })
			{
				string[] escolhas = CharacterDraft.EscolhasDeClasse(raca);
				CharacterSave c = SaveDeBancada($"Grav {raca} {perto}", raca,
					escolhas.Length > 0 ? escolhas[0] : "", "", perto, rng);
				Berco b = BercoDe(c);

				// A gravidade do berco: a do mundo gerado, ou a ficha do pre-feito no `planetas.json`.
				double doChao = b.PreFeito ? _planetas?.De(b.Planeta).Gravidade ?? 1 : b.Gravidade;
				double daRaca = Birth.GravidadeNatal(raca);
				if (doChao <= daRaca + 1e-9) continue;

				pesados.Add($"{raca}{(perto ? "+vizinho" : "")} nasce em {b.Planeta} ({doChao:0.#}g) "
						  + $"e a raca domina {daRaca:0.#}g");

				// O CORPO PELO CAMINHO DE PRODUCAO: nasce pela porta unica e e aclimatado pela mesma
				// funcao que o servidor chama ao poe-lo no chao. Nada cravado a mao -- era justamente a
				// atribuicao que faltava.
				Jandirus.Core.Stats.Fighter f = Birth.Nascer(_racas!, raca,
					escolhas.Length > 0 ? escolhas[0] : "", rng, $"Grav {raca}");
				Birth.AclimatarAoBerco(f, doChao);
				f.Planetgrav = doChao;
				f.Tick();

				if (Jandirus.Core.Stats.Esmagamento.Prende(f))
					esmagados.Add($"{raca} em {b.Planeta} ({doChao:0.#}g), maestria {f.GravMastered:0.#}");
			}

		// O TETO DESTA CHECAGEM DISPARA: se a lista de bercos pesados esvaziar (alguem mudou a tabela
		// de bercos, ou o `planetas.json`), ela para de provar qualquer coisa -- e passaria verde por
		// ausencia, que e o modo de falha registrado na PARTE 0.7 do plano.
		Afirmar($"ha bercos mais pesados que a maestria da raca ({pesados.Count}) -- a checagem abaixo "
				+ "tem o que testar", pesados.Count > 0);
		Afirmar($"e NENHUM deles nasce esmagado: o `max` do race.dm:130-131 esta LIGADO "
				+ $"({pesados.Count} berco(s) conferidos)", esmagados.Count == 0,
				string.Join(" | ", esmagados.Take(4)));

		foreach (string p in pesados.Take(8)) GD.Print($"[bercovivo]         {p}");
		if (pesados.Count > 8) GD.Print($"[bercovivo]         ... e mais {pesados.Count - 8}");
	}

	// =====================================================================
	// 5) NASCER E RENASCER SAO O MESMO LUGAR
	// =====================================================================
	/// <summary>
	/// A checagem que pega o MEIO-CONSERTO. Sao TRES pontos e nao dois, porque sao tres caminhos
	/// diferentes no codigo pra a mesma pergunta do jogador ("onde eu apareco?"):
	///
	///   1. o que o `CreateChar` GRAVA no save (`AplicarBercoNoSave`) -- e o que o `Entrar` le no
	///      proximo login;
	///   2. onde o corpo PARA depois de pousar;
	///   3. onde o `Renascer` o devolve depois de morrer, de outro planeta.
	///
	/// O `Renascer` de verdade e chamado aqui (nao o `DestinoDoBerco` sozinho): entre um e outro
	/// existe o `MandarProBerco`, com `MoveToZone`, carimbo de chunk e vizinhanca -- e e ali que um
	/// meio-conserto se esconderia.
	/// </summary>
	private void NascerEhOMesmoQueRenascer()
	{
		GD.Print("[bercovivo] -- 5) NASCER E RENASCER DAO O MESMO LUGAR");

		(string Raca, string Linhagem, string Classe, bool Perto)[] perfis =
		[
			("Human", "", "Normal", false),
			("Human", "", "Normal", true),
			("Saiyan", "Saiyan", "Normal", false),
			("Saiyan", "Saiyan", "Low-Class", false),
			("Saiyan", "Saiyan", "Low-Class", true),
			("Saiyan", "Saiyan", "Legendary", false),
			("Icer", "", "Normal", false),
			("Namekian", "Warrior clan", "Warrior clan", true),
		];

		var rng = new Random(31337);
		foreach ((string raca, string linhagem, string classe, bool perto) in perfis)
		{
			CharacterSave c = SaveDeBancada($"Ciclo {raca} {classe} {perto}", raca, linhagem, classe, perto, rng);
			ServerPlayer pl = CorpoDoSave(c, 6);
			string rotulo = $"{raca}/{classe} (perto={perto})";
			try
			{
				PousarDeVerdade(pl);
				ZoneKey zNasceu = pl.Zone;
				Vec2 pNasceu = pl.Pos;

				Afirmar($"{rotulo}: o save do nascimento aponta pro mesmo destino do corpo",
						MesmoDestino(c, zNasceu, pNasceu),
						$"save {c.Zona} ({c.X:0},{c.Y:0}) x corpo {zNasceu.Name} ({pNasceu.X:0},{pNasceu.Y:0})");

				// MORRE LONGE DE CASA. Sem esta mudanca de zona, `Renascer` cairia no ramo "mesma
				// zona" e o teste nao passaria pelo `MandarProBerco` -- que e justamente o pedaco
				// que so a morte usa.
				MoveToZone(pl.Id, SpawnZone, PontoDeNascimento(SpawnZone));
				Afirmar($"{rotulo}: o corpo foi mesmo levado pra longe antes de morrer",
						pl.Zone.Hash != zNasceu.Hash || zNasceu.Name == "Earth", pl.Zone.Name);

				Renascer(pl);
				PousarDeVerdade(pl);

				Afirmar($"{rotulo}: renasceu na mesma ZONA em que nasceu ({zNasceu.Name})",
						pl.Zone.Hash == zNasceu.Hash, $"{zNasceu.Name} -> {pl.Zone.Name}");
				Afirmar($"{rotulo}: ...e no mesmo PONTO",
						MesmoPonto(pl.Pos, pNasceu),
						$"({pNasceu.X:0},{pNasceu.Y:0}) -> ({pl.Pos.X:0},{pl.Pos.Y:0})");
			}
			finally { Recolher(pl); }
		}
	}

	// =====================================================================
	// 6) O PERSONAGEM ANTIGO -- E O RENOMEADO
	// =====================================================================
	/// <summary>
	/// O save de quem ja existia nao tem `SeedDoBerco` (zero = deriva). Ele tem que CARREGAR, ganhar
	/// um berco e ganhar o MESMO em todo login -- senao ele renasceria num planeta diferente a cada
	/// vez que morresse, e a causa seria invisivel.
	///
	/// ============================ E O RENOMEADO E O UNICO TESTE DE VERDADE DO CAMPO ============================
	/// Enquanto a semente gravada for IGUAL a derivada -- e ela e, em todo personagem criado hoje
	/// (`CreateChar` grava `SementeDoBerco(nome, criadoEm)`) --, apagar o campo do save nao muda
	/// nada: a derivacao devolve o mesmo numero. Ou seja, `SeedDoBerco` some do `DeJogador` e
	/// NENHUM teste percebe. Foi exatamente assim que os `Limiares` sumiram por meses.
	///
	/// O renomeado e o unico caso em que os dois numeros divergem, e e por isso que o campo existe
	/// (ver o cabecalho de `Bercos.SementeDoBerco`: *"no dia em que existir um verb de renomear, um
	/// jogador renomeado renasceria noutro planeta, calado"*). Este teste constroi esse dia.
	/// =======================================================================================================
	/// </summary>
	private void OPersonagemAntigoEORenomeado()
	{
		GD.Print("[bercovivo] -- 6) O PERSONAGEM ANTIGO, E O RENOMEADO");

		// --- (a) O SAVE DE 2020: sem `SeedDoBerco`, sem `PertoDeCasa`, com a Terra cravada.
		var velho = new CharacterSave
		{
			Nome = "Veterano de 2020",
			Raca = "Namekian",
			Linhagem = "Warrior clan",
			CriadoEm = 1_600_000_000_000,
			SeedDoBerco = 0,
			Zona = "Earth",
			X = SpawnPos.X,
			Y = SpawnPos.Y,
			Ficha = new Fighter { Name = "Veterano de 2020", Race = "Namekian", Class = "Warrior clan", BP = 500 },
		};

		Berco d1 = BercoDe(velho);
		Berco d2 = BercoDepoisDeDoisCarregamentos(velho, 10, out string trilhaVelho);

		Afirmar("o save sem `SeedDoBerco` CARREGA e ganha um berco", d1.Planeta is { Length: > 0 }, "vazio");
		Afirmar($"o berco derivado e o natal da raca dele ({d1.Planeta})", d1.Planeta == Bercos.PlanetaNatal("Namekian"));
		Afirmar("o berco derivado e estavel entre dois carregamentos de DISCO",
				MesmoLugar(d1, d2), $"{d1.Planeta} -> {trilhaVelho}");

		// ELE NAO E MOVIDO: continua onde deslogou. O berco so manda a partir da proxima morte.
		ServerPlayer plVelho = CorpoDoSave(velho, 7);
		try
		{
			Afirmar("o personagem antigo NAO e movido no login (acorda onde deslogou)",
					plVelho.Zone.Name == "Earth", plVelho.Zone.Name);
			Afirmar("...mas o berco dele ja aponta pra Namek", plVelho.Berco.Planeta == "Namek",
					plVelho.Berco.Planeta);
		}
		finally { Recolher(plVelho); }

		// --- (b) O RENOMEADO. A semente e a do nome VELHO; o nome no save e o NOVO.
		long nasceuEm = 1_650_000_000_000;
		ulong sementeDoNomeVelho = Bercos.SementeDoBerco("Kakaroto", nasceuEm);
		var renomeado = new CharacterSave
		{
			Nome = "Son Goku",                      // <- ja renomeado
			Raca = "Saiyan",
			Linhagem = "Saiyan",
			CriadoEm = nasceuEm,
			SeedDoBerco = sementeDoNomeVelho,       // <- mas a semente e a de antes
			PertoDeCasa = true,
			Zona = "Earth",
			Ficha = new Fighter { Name = "Son Goku", Race = "Saiyan", Class = "Elite", BP = 500 },
		};

		Berco comCampo = BercoDe(renomeado);
		Berco derivado = Bercos.Onde(SeedDoUniverso, "Saiyan", "Elite", "Saiyan",
									 Bercos.SementeDoBerco("Son Goku", nasceuEm), true);

		Afirmar("o renomeado E um caso divergente (a semente gravada nao e a do nome novo)",
				!MesmoLugar(comCampo, derivado),
				"as duas contas deram o mesmo planeta -- este teste nao esta provando nada");

		Berco depois = BercoDepoisDeDoisCarregamentos(renomeado, 11, out string trilhaNovo);
		Afirmar($"o renomeado volta do disco no MESMO berco ({comCampo.Planeta})",
				MesmoLugar(comCampo, depois), $"{comCampo.Planeta} -> {trilhaNovo}");

		// --- (c) O BIT DO PEDIDO tambem tem que atravessar o disco.
		Afirmar("o pedido de nascer perto de casa sobrevive ao disco",
				depois.Motivo == MotivoDoBerco.VizinhoDoNatal, depois.Motivo.ToString());
	}

	// =====================================================================
	// 7) AS DUAS PONTAS CONCORDAM
	// =====================================================================
	/// <summary>
	/// A prova que a spec pede (secao 11): *"a assinatura do terreno bate com a do servidor"*.
	///
	/// ============================ O CLIENTE E REFEITO AQUI, PASSO A PASSO ============================
	/// A bancada nao "pergunta ao cliente": ela ANDA o caminho dele. O cliente recebe uma `ZoneKey`
	/// (nome + seed) no `ZoneChanged` e faz exatamente duas coisas (`PlanetaProcedural.Nascer`):
	/// `MundoProcedural.DaSeed(seed, nome)` e `GeradorDeTerreno.Gerar(ficha.Parametros())`. As mesmas
	/// duas chamadas estao aqui, na mesma ordem -- e a assinatura sai do mesmo `TerrenoGerado`.
	///
	/// E ELA PARTE DO ENDERECO, e nao do pacote: `Sistemas.Do(seed, Sx, Sy).Planeta(K)`. E o que a
	/// spec quer dizer com *"a escolha guarda a SEED, nao o nome"* levado ao fim -- se o endereco
	/// gravado no save nao reconstruir o mesmo corpo, o jogador renasce noutro planeta com o mesmo
	/// nome (e os nomes COLIDEM: a fase 1 achou "Deserto-120" saindo de duas celulas diferentes).
	/// ============================================================================================
	/// </summary>
	private void AsDuasPontasConcordam()
	{
		GD.Print("[bercovivo] -- 7) AS DUAS PONTAS CONCORDAM SOBRE ONDE ELE NASCE");

		var rng = new Random(2026);
		(string Nome, string Raca, string Linhagem, string Classe, bool Perto)[] casos =
		[
			("Terraqueo vizinho", "Human", "", "Normal", true),
			("Frost vizinho", "Icer", "", "Normal", true),
			("Lendario exilado", "Saiyan", "Saiyan", "Legendary", false),
		];

		foreach ((string nome, string raca, string linhagem, string classe, bool perto) in casos)
		{
			CharacterSave c = SaveDeBancada(nome, raca, linhagem, classe, perto, rng);
			Berco b = BercoDe(c);
			ServerPlayer pl = CorpoDoSave(c, 8);
			try
			{
				PousarDeVerdade(pl);

				// (i) O ENDERECO RECONSTROI O CORPO. Nome, seed, posicao e raio saem da trinca.
				SistemaSolar? sis = Sistemas.Do(SeedDoUniverso, b.Sx, b.Sy);
				Afirmar($"{nome}: o endereco [{b.Sx}:{b.Sy}]k{b.K} tem sistema", sis != null);
				if (sis is not { } s) continue;

				PlanetaNoEspaco corpo = s.Planeta(b.K);
				Afirmar($"{nome}: o endereco reconstroi o MESMO corpo ({corpo.Nome})",
						corpo.Nome == b.Planeta && corpo.Seed == b.Seed && corpo.Pos.Equals(b.Pos),
						$"{corpo.Nome}/{corpo.Seed} x {b.Planeta}/{b.Seed}");

				var zona = corpo.Premade ? ZoneKey.Premade(corpo.Nome) : ZoneKey.Procedural(corpo.Nome, corpo.Seed);
				Afirmar($"{nome}: a zona reconstruida e a zona em que o corpo esta",
						zona.Hash == pl.Zone.Hash, $"{zona.Name} x {pl.Zone.Name}");

				if (corpo.Premade) continue;   // mapa feito a mao nao tem assinatura de geracao

				// (ii) A ASSINATURA. O servidor gerou o mundo la atras, na thread; o "cliente" gera
				// agora, aqui, a partir so da `ZoneKey`.
				ZonaGerada? doServidor = _zonasGeradas.GetValueOrDefault(pl.Zone.Hash);
				Afirmar($"{nome}: o servidor tem o mundo gerado em maos", doServidor != null);
				if (doServidor == null) continue;

				MundoProcedural ficha = MundoProcedural.DaSeed(zona.Seed, zona.Name);
				TerrenoGerado doCliente = GeradorDeTerreno.Gerar(ficha.Parametros());

				Afirmar($"{nome}: as duas pontas geraram o MESMO mundo "
						+ $"(assinatura {doCliente.Assinatura():X16})",
						doCliente.Assinatura() == doServidor.Assinatura,
						$"cliente {doCliente.Assinatura():X16} x servidor {doServidor.Assinatura:X16}");

				Afirmar($"{nome}: as duas pontas concordam no PONTO DE CHEGADA",
						MesmoPonto(doCliente.Spawn, doServidor.Chegada) && MesmoPonto(pl.Pos, doServidor.Chegada),
						$"cliente ({doCliente.Spawn.X:0},{doCliente.Spawn.Y:0}) x servidor "
						+ $"({doServidor.Chegada.X:0},{doServidor.Chegada.Y:0}) x corpo ({pl.Pos.X:0},{pl.Pos.Y:0})");

				// (iii) E A TELA DE CRIACAO. `IrmasDoNatal` e o que ela mostra, e ela roda ANTES de
				// existir conexao -- sem seed do universo e sem um byte de pacote. Se a lista dela nao
				// contiver o mundo que o servidor escolheu, a tela promete uma escolha que o mundo
				// ignora, e o jogador so descobre isso ao acordar noutro lugar.
				if (b.Motivo == MotivoDoBerco.VizinhoDoNatal)
					Afirmar($"{nome}: a tela de criacao lista este mundo entre as irmas de {b.Natal}",
							Bercos.IrmasDoNatal(b.Natal).Any(p => p.Nome == b.Planeta && p.Seed == b.Seed),
							$"{b.Planeta} nao esta na lista da tela");
			}
			finally { Recolher(pl); }
		}
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// UM SAVE IGUAL AO QUE O `CreateChar` GRAVA -- e nada alem dele.
	///
	/// A ordem e a de la e importa: `Birth.Nascer` primeiro (a classe e sorteada), o berco depois
	/// (ele DEPENDE da classe). Invertido, um Lendario receberia o berco de um Saiyajin normal --
	/// que e o comentario que o proprio `CreateChar` carrega.
	///
	/// `classeForcada` vazia = classe sorteada de verdade. E o parametro que o `Birth.Nascer` ja
	/// tinha pros moldes de NPC, e nao um atalho aberto pra bancada.
	/// </summary>
	private CharacterSave SaveDeBancada(string nome, string raca, string linhagem, string classe,
										bool pertoDeCasa, Random rng, long criadoEm = 1_760_000_000_000)
	{
		Fighter lutador = Birth.Nascer(_racas!, raca, linhagem, rng, nome, classeForcada: classe);

		var c = new CharacterSave
		{
			Nome = nome,
			Raca = raca,
			Genero = "Male",
			Linhagem = linhagem,
			Idade = 18,
			Ficha = lutador,
			CriadoEm = criadoEm,
			PertoDeCasa = pertoDeCasa,
			SeedDoBerco = Bercos.SementeDoBerco(nome, criadoEm),
		};

		AplicarBercoNoSave(c, BercoDe(c));
		return c;
	}

	/// <summary>
	/// O CORPO DE UM SAVE, montado como o `Entrar` monta: `ParaJogador` traduz o disco, a zona volta
	/// inteira (tipo + nome + seed), o berco e calculado uma vez, e `PorNoMundo` faz o resto.
	///
	/// `Peer = null` e o que faz dele um corpo sem dono -- os envios viram no-op e nenhum pacote sai
	/// no fio, que e o que permite esta bancada rodar no boot, sem cliente e sem rede.
	/// </summary>
	private ServerPlayer CorpoDoSave(CharacterSave c, int faixa)
	{
		var pl = new ServerPlayer
		{
			// A FAIXA SEPARA AS FAMILIAS no log e o contador garante a unicidade. Sem o contador,
			// dois corpos da mesma familia dividiriam o id -- e `_players[id] = corpo` sobrescreve
			// calado, entao o segundo apagaria o primeiro da lista da zona sem erro nenhum.
			Id = IdBaseDoBercoVivo + faixa * 1000 + _bvCorpos++,
			Peer = null,
			Conta = ContaDaBancadaDeBerco,
			Slot = 0,
			LastInputMs = NowMs(),
		};

		AccountStore.ParaJogador(c, pl);
		pl.Zone = new ZoneKey(c.ZonaTipo, c.Zona, c.ZonaSeed);
		pl.Berco = BercoDe(c);
		PorNoMundo(pl);
		return pl;
	}

	private int _bvCorpos;

	/// <summary>Um corpo pairando exatamente sobre o disco de um planeta -- o caso do `PlanetaSob`.</summary>
	private ServerPlayer CorpoEmOrbita(PlanetaNoEspaco mundo, int faixa)
	{
		var pl = new ServerPlayer
		{
			// A FAIXA SEPARA AS FAMILIAS no log e o contador garante a unicidade. Sem o contador,
			// dois corpos da mesma familia dividiriam o id -- e `_players[id] = corpo` sobrescreve
			// calado, entao o segundo apagaria o primeiro da lista da zona sem erro nenhum.
			Id = IdBaseDoBercoVivo + faixa * 1000 + _bvCorpos++,
			Peer = null,
			Name = $"Orbita de {mundo.Nome}",
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Conta = ContaDaBancadaDeBerco,
			Zone = ZonaDoEspaco,
			Pos = mundo.Pos,
			Ficha = new Fighter { Race = "Human", Class = "Normal", BP = 100 },
		};
		PorNoMundo(pl);
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		return pl;
	}

	/// <summary>
	/// POUSA PELO CAMINHO DE VERDADE, e devolve em quantos tiques -- ou -1 se nunca pousou.
	///
	/// As duas funcoes do laco sao as MESMAS que o `_Process` do servidor chama, na mesma ordem
	/// (`GameServer.cs`: `TickDasGeracoes()` e depois `TickDoEspaco` por jogador). Elas nao podem ser
	/// trocadas por um `PousarEmProcedural` direto: quem faz o mundo NASCER e a colheita da thread,
	/// e sem ela o pouso devolve falso pra sempre e o corpo fica em orbita.
	///
	/// O `Sleep(1)` so acontece enquanto ha mundo em voo, e e o que separa esta bancada de um laco
	/// de espera ocupada de 900 iteracoes: a geracao leva dezenas de milissegundos numa thread do
	/// pool, e nada aqui pode ser colhido antes de ela terminar.
	/// </summary>
	private int PousarDeVerdade(ServerPlayer pl)
	{
		for (int t = 0; t < TiquesDePouso; t++)
		{
			TickDasGeracoes();
			TickDoEspaco(pl);

			if (!Espaco.EhEspaco(pl.Zone)) return t + 1;
			if (_gerando.Count > 0) System.Threading.Thread.Sleep(1);
		}
		return -1;
	}

	/// <summary>
	/// ESTE CORPO ESTA EM CHAO DE VERDADE?
	///
	/// Mais duro que o `ChaoDeVerdade` da `--diagberco`, e de proposito: la o espaco e uma resposta
	/// LEGITIMA (o corpo esta em orbita e vai pousar). Aqui o pouso ja aconteceu, entao o espaco e
	/// falha -- e a frase do pedido e literal: *"ninguem pode nascer no espaco"*.
	/// </summary>
	private bool PisouEmChao(ServerPlayer pl, out string porque)
	{
		if (Espaco.EhEspaco(pl.Zone)) { porque = "FICOU NO ESPACO"; return false; }

		ZoneCollision? m = MapaDaZonaOuCatalogo(pl.Zone);
		if (m == null) { porque = $"a zona {pl.Zone.Name} NAO TEM COLISAO"; return false; }

		if (m.BlockedAt(pl.Pos)) { porque = "DENTRO DE PAREDE"; return false; }

		porque = "chao livre";
		return true;
	}

	/// <summary>
	/// IDA E VOLTA PELO DISCO, DUAS VEZES -- e o que separa "a funcao e pura" de "o berco sobrevive
	/// a um logout".
	///
	/// O caminho e o de producao inteiro e na ordem de producao: `ParaJogador` (login) -> `DeJogador`
	/// (o `Persistir`) -> `Gravar` (JSON no disco) -> `Carregar` (o proximo login) -> `ParaJogador`
	/// de novo. E nesse trecho que mora a armadilha do campo listado a mao: `DeJogador` monta o save
	/// do ZERO a cada `Persistir`, entao um campo que ninguem escreveu ali some do disco calado --
	/// foi assim que os `Limiares` de todo Saiyajin do servidor sumiram.
	/// </summary>
	private Berco BercoDepoisDeDoisCarregamentos(CharacterSave original, int faixa, out string trilha)
	{
		trilha = "";
		if (_store == null) return default;

		Berco atual = default;
		CharacterSave atualSave = original;

		for (int volta = 0; volta < 2; volta++)
		{
			ServerPlayer pl = CorpoDoSave(atualSave, faixa);
			try
			{
				var conta = new AccountSave { Conta = ContaDaBancadaDeBerco };
				conta.Slots[0] = AccountStore.DeJogador(pl, NowMs());
				_store.Gravar(conta);

				AccountSave? lida = _store.Carregar(ContaDaBancadaDeBerco);
				if (lida?.Slots[0] is not { } voltou) { trilha = "NAO VOLTOU DO DISCO"; return default; }

				atualSave = voltou;
				atual = BercoDe(voltou);
				trilha += $"{(volta > 0 ? " -> " : "")}{atual.Planeta} [{atual.Sx}:{atual.Sy}]k{atual.K}";
			}
			finally { Recolher(pl); }
		}

		return atual;
	}

	/// <summary>Apaga o arquivo da conta de bancada. Ver o `finally` do <see cref="RodarBancadaDoBercoVivo"/>.</summary>
	private void ApagarContaDaBancada()
	{
		if (_store == null) return;
		try
		{
			string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDeBerco + ".json");
			if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
		}
		catch (Exception e) { GD.PushWarning($"[bercovivo] nao consegui apagar a conta de bancada: {e.Message}"); }
	}

	/// <summary>
	/// DOIS BERCOS SAO O MESMO LUGAR? Compara o ENDERECO, e nao o nome.
	///
	/// O nome nao serve de chave e a fase 1 mediu por que: `SistemaSolar.Planeta` monta
	/// `$"{bioma}-{|Sx|%1000}{|Sy|%1000}{k}"` e PERDE O SINAL da celula, entao dois mundos de
	/// celulas diferentes saem com o mesmo rotulo. Comparar por nome deixaria passar exatamente o
	/// defeito que esta bancada procura.
	/// </summary>
	private static bool MesmoLugar(Berco a, Berco b) =>
		a.Sx == b.Sx && a.Sy == b.Sy && a.K == b.K && a.Seed == b.Seed && a.Planeta == b.Planeta;

	/// <summary>Meio tile de folga: o ponto passa por `float` no save e volta com o ultimo bit trocado.</summary>
	private static bool MesmoPonto(Vec2 a, Vec2 b) =>
		Math.Abs(a.X - b.X) < ZoneCollision.TileSize / 2f && Math.Abs(a.Y - b.Y) < ZoneCollision.TileSize / 2f;

	/// <summary>O que o save do nascimento diz bate com onde o corpo parou depois de pousar?</summary>
	private bool MesmoDestino(CharacterSave c, ZoneKey zona, Vec2 pos)
	{
		var doSave = new ZoneKey(c.ZonaTipo, c.Zona, c.ZonaSeed);

		// O SAVE PODE APONTAR PRA ORBITA, e isso e correto e nao divergencia: um mundo gerado e frio
		// no instante da criacao, entao o `AplicarBercoNoSave` grava o espaco -- que e onde o corpo
		// de fato estava. Quem se desconectar entre a criacao e o pouso acorda la, e pousa de novo.
		if (Espaco.EhEspaco(doSave))
			return Espaco.PlanetaSob(SeedDoUniverso, new Vec2(c.X, c.Y))?.Nome == zona.Name;

		return doSave.Hash == zona.Hash && MesmoPonto(new Vec2(c.X, c.Y), pos);
	}
}
