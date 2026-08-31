using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// **A PROVA DO REFUGIO** -- as familias 8, 9 e 10 da `--bercoprova`.
///
/// ============================ POR QUE ELAS EXISTEM, COM A FAMILIA 7 JA VERDE ============================
/// A familia 7 mede os cinco estados do pedido do dono com **UM personagem** -- um Human que conquistou
/// Namek. Ela e correta e continua de pe, e mesmo assim ela e uma AMOSTRA: foi exatamente uma amostra
/// ("Human, Saiyan, Icer, Namekian", os oito perfis da `--bercovivo`) que deixou passar o defeito que
/// fez estas bancadas existirem, com 13 racas acordando em Namek e dezenas de checagens verdes.
///
/// O pedido do dono nao fala de um personagem: *"quando uma RACA fica sem planeta natal..."*. Entao a
/// prova tem que ser **nominal, uma linha por raca, as 24** -- e nas DUAS metades:
///
///   * com o planeta natal DE PE, **ninguem escolhe nada**: o corpo volta pra casa, e o verb do
///     refugio se RECUSA a escrever mesmo que a pessoa tenha territorio conquistado;
///   * com ele MORTO, **a escolha aparece e leva ao destino escolhido**: as duas saidas existem, o
///     padrao e a vizinhanca, escolher o dominio move o corpo pro dominio, e voltar atras traz o
///     corpo de volta pro MESMO mundo vizinho de antes.
/// ====================================================================================================
///
/// ============================ E CADA AFIRMACAO TEM AS DUAS PONTAS ============================
/// Nenhuma linha daqui mede so a INTENCAO (`EscolhaDeRefugio`: "havia escolha") nem so o EFEITO ("o
/// corpo esta aqui"). As duas juntas, sempre, porque cada uma sozinha ja falhou neste projeto:
///   * so a intencao ficaria verde num refugio que oferecesse a escolha e ignorasse a resposta;
///   * so o efeito nao distinguiria *"foi pra vizinhanca porque escolheu"* de *"foi porque nao havia
///     mais nada"*.
///
/// E o EFEITO e o corpo NO CHAO (<see cref="PisouEmChao"/>), e nao a zona: um refugio que devolvesse a
/// orbita de um mundo que nunca nasce daria zona certa e corpo nenhum em lugar nenhum.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A faixa de ids destas familias dentro do <see cref="CorpoDoSave"/>. 21 e nao 20 pra nao dividir
	/// numeracao com as sete familias antigas: `_players[id] = corpo` sobrescreve CALADO, entao uma
	/// colisao apagaria um corpo da lista da zona sem erro nenhum.
	/// </summary>
	private const int FaixaDoRefugio = 21;

	/// <summary>
	/// O NOME DE UM PLANETA QUE NAO EXISTE -- o intruso da familia 10.
	///
	/// Ele e o herdeiro direto do "Hera" que a bancada antiga enfiava na frente de `Espaco.PreFeitos()`
	/// pra provar que o destino de quem perdia o berco era uma POSICAO NUMA LISTA. O nome ficou; a
	/// afirmacao inverteu. Ver <see cref="AListaMorreuDeVerdade"/>.
	/// </summary>
	private const string PlanetaIntruso = "Hera";

	// =====================================================================
	// 8) A ESCOLHA, RACA POR RACA -- as 24, nas duas metades
	// =====================================================================
	/// <summary>
	/// **O PEDIDO DO DONO, UMA LINHA POR RACA.** Ver o cabecalho do arquivo pro porque de a familia 7
	/// nao bastar.
	///
	/// ============================ AS RACAS SAO AGRUPADAS PELO NATAL, E ISSO NAO E ECONOMIA ============================
	/// Matar um planeta pelo funil de producao custa os ~310 s de tique da explosao, e as 24 racas
	/// dividem 8 natais. Matar "o natal de cada raca" seria matar os mesmos 8 planetas 24 vezes -- e,
	/// pior, faria a prova de duas racas do mesmo mundo rodar em mundos diferentes, porque cada palco
	/// desfaz o anterior.
	///
	/// Um palco por NATAL poe todas as racas daquele mundo no MESMO estado, que e o unico jeito de a
	/// afirmacao "as 10 racas da Terra" significar alguma coisa. As linhas continuam sendo 24.
	/// ============================================================================================================
	///
	/// ============================ DUAS DAS 24 NAO TEM COMO FICAR SEM CASA, E ISSO E MEDIDO ============================
	/// O Kai nasce em `Heaven` e o Demon em `Hell`, e **nenhum dos dois e corpo celeste**: eles nao
	/// estao em <see cref="Espaco.PreFeitos"/>, entao `ChaveDePlaneta.Da` devolve nulo e a destruicao e
	/// recusada na primeira linha do <see cref="ComecarDestruicao"/>. Estas duas racas nunca chegam ao
	/// refugio em jogo.
	///
	/// Isso nao vira um `continue` calado: vira uma linha NOMINAL por raca dizendo que a destruicao foi
	/// RECUSADA, com a tentativa feita de verdade. Raca pulada em silencio e raca sem prova, e e assim
	/// que 13 delas nasceram em Namek com o placar verde.
	/// ============================================================================================================
	/// </summary>
	private void AEscolhaRacaPorRaca()
	{
		GD.Print("[bercoprova] -- 8) A ESCOLHA, RACA POR RACA: as 24, com o natal DE PE e com ele MORTO");

		var salvos = new List<Dominio>(_dominios);
		try
		{
			var porNatal = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
			foreach (string raca in ConjuntoDeRacas())
			{
				string natal = Bercos.PlanetaNatal(raca);
				if (!porNatal.TryGetValue(natal, out List<string>? l)) porNatal[natal] = l = [];
				l.Add(raca);
			}

			int total = porNatal.Values.Sum(l => l.Count);
			Provar($"as {total} racas se dividem em {porNatal.Count} natais "
				 + $"({string.Join(", ", porNatal.Select(k => $"{k.Key}={k.Value.Count}"))})",
				   total >= 24 && porNatal.Count >= 5,
				   "o conjunto de racas encolheu -- as linhas nominais abaixo cobrem menos do que dizem");

			// ---- A METADE VIVA: NINGUEM ESCOLHE NADA -------------------------
			GD.Print("[bercoprova]    (8a) COM O NATAL DE PE: ninguem e perguntado, e o verb se recusa");
			foreach ((string natal, List<string> racas) in porNatal)
				foreach (string raca in racas) ComOBercoDePe(raca, natal);

			// ---- A METADE MORTA: A ESCOLHA APARECE E LEVA AO DESTINO ---------
			GD.Print("[bercoprova]    (8b) COM O NATAL MORTO: a escolha aparece, e o corpo vai pra onde ela diz");
			foreach ((string natal, List<string> racas) in porNatal) SemCasa(natal, racas);
		}
		finally
		{
			// O LIVRO VOLTA COMO ESTAVA -- ver o `finally` da familia 7: o `FincarDominio` de producao
			// grava `conquista.json`, e uma bandeira de bancada esquecida la mudaria o renascimento de
			// um personagem de verdade.
			_dominios.Clear();
			_dominios.AddRange(salvos);
			SalvarConquista();
		}
	}

	/// <summary>
	/// UMA RACA, COM O PLANETA NATAL DE PE. A metade que impede o verde por imobilidade do outro lado.
	///
	/// ============================ O DOMINIO E FINCADO DE PROPOSITO ============================
	/// Sem ele, *"ninguem escolhe nada"* seria verdade por nao haver o que escolher -- a mesma
	/// armadilha de "verde por ausencia" que a familia 1 ja fecha do outro jeito. Com um territorio
	/// conquistado no livro, a ausencia de escolha passa a ser uma DECISAO do codigo e nao uma
	/// consequencia de o mundo estar vazio.
	///
	/// E as tres coisas sao afirmadas juntas porque tem tres donos diferentes:
	///   * <see cref="PerdeuOBerco"/> -- a pergunta unica que a oferta, a tela e o verb leem;
	///   * o VERB (<see cref="ComandoDeRefugio"/>) -- ele nao pode escrever `EhOSpawn` com o berco de
	///     pe, senao seria um segundo caminho pro bit do `conq_spawn` sem a exigencia de estar de pe
	///     junto da bandeira;
	///   * o DESTINO (`DestinoDe`) -- o corpo volta pra casa.
	/// ======================================================================================
	/// </summary>
	private void ComOBercoDePe(string raca, string natal)
	{
		CharacterSave c = SaveDeBancada($"Vivo {raca}", raca, LinhagemDe(raca), "Normal", false,
										new Random(20260831));
		ServerPlayer pl = CorpoDoSave(c, FaixaDoRefugio);
		pl.Conta = ContaDoRefugio;   // assinatura propria: dominio e do PERSONAGEM, e nao da conta

		Dominio? d = null;
		try
		{
			PousarDeVerdade(pl);
			PlanetaNoEspaco longe = PreFeitoLongeDe(natal);
			d = FincarDominio(pl, longe, BandeiraEm(longe));

			ComandoDeRefugio(pl, "refugio", d.Chave.Texto);
			(ZoneKey z, Vec2 _) = DestinoDe(pl);
			bool perdeu = PerdeuOBerco(pl);

			Provar($"{raca}: com {natal} DE PE, ninguem e perguntado -- o verb recusa e o corpo volta "
				 + $"pra casa ('{z.Name}')",
				   !perdeu && !d.EhOSpawn
				   && string.Equals(z.Name, natal, StringComparison.OrdinalIgnoreCase),
				   $"PerdeuOBerco={perdeu} EhOSpawn={d.EhOSpawn} destino={z.Name} "
				 + $"(tinha {longe.Nome} conquistado, e mesmo assim nao devia ser perguntado)");
		}
		finally
		{
			if (d != null) PerderDominio(d, "", "bercoprova: fim da metade viva", anunciar: false);
			Recolher(pl);
		}
	}

	/// <summary>
	/// UM NATAL MORTO, E TODAS AS RACAS DELE. Um palco so -- ver o cabecalho da familia.
	///
	/// A recusa de destruir `Heaven`/`Hell` e afirmada com a tentativa FEITA, e nao deduzida: se um dia
	/// aqueles dois virarem corpos celestes, esta linha fica vermelha e o assunto volta pra mesa.
	/// </summary>
	private void SemCasa(string natal, List<string> racas)
	{
		var zona = ZoneKey.Premade(natal);

		using PalcoDeMortes _ = PalcoDeMortesDeBancada();

		// ---- O NATAL QUE NAO E CORPO CELESTE -----------------------------
		if (!Espaco.EhPlaneta(zona))
		{
			bool comecou = ComecarDestruicao(zona, 1e12, $"bercoprova: tentando matar {natal}");
			foreach (string raca in racas)
				Provar($"{raca}: '{natal}' NAO e corpo celeste -- a destruicao e RECUSADA, e por isso "
					 + "esta raca nunca fica sem casa",
					   !comecou && !ZonaMorta(zona),
					   $"a destruicao de '{natal}' foi aceita (comecou={comecou}, morta={ZonaMorta(zona)}) "
					 + "-- estas racas passaram a poder perder o natal e nada as cobre");
			return;
		}

		if (!MatarPlanetaNoPalco(zona, $"bercoprova: raca por raca, {natal}"))
		{
			Provar($"a injecao matou '{natal}' (rodada raca por raca)", false,
				   "nao chegou a `Destruido` -- as linhas nominais deste natal nao provariam nada");
			return;
		}

		HashSet<string> aceitos = OndeEsteCorpoDeveriaAcordar(natal);
		PlanetaNoEspaco longe = PreFeitoLongeDe(natal);
		double distancia = DistanciaAte(natal, longe.Pos);
		double alcance = (Refugios.CelulasDeBusca + 1) * Sistemas.CelulaPx;

		// ============================ O DOMINIO TEM QUE ESTAR FORA DE ALCANCE ============================
		// Se o territorio conquistado por acaso caisse dentro da vizinhanca da busca, "o corpo foi pro
		// dominio" e "o corpo foi pra vizinhanca" deixariam de ser distinguiveis -- e as quatro linhas
		// de cada raca ficariam verdes com o refugio fazendo qualquer uma das duas coisas.
		// ============================================================================================
		Provar($"o dominio da rodada de {natal} e '{longe.Nome}', FORA do alcance da busca "
			 + $"({distancia / Sistemas.CelulaPx:0.0} celulas contra {alcance / Sistemas.CelulaPx:0.0})",
			   distancia > alcance && !aceitos.Contains(longe.Nome),
			   $"{longe.Nome} esta perto demais de {natal} -- B1 e B2 deixariam de ser distinguiveis");

		foreach (string raca in racas) UmaRacaSemCasa(raca, natal, aceitos, longe);
	}

	/// <summary>
	/// **UMA RACA SEM CASA, DO NASCIMENTO A ESCOLHA E DE VOLTA.** As quatro linhas do pedido do dono.
	///
	/// ============================ A PRIMEIRA LINHA E O NASCIMENTO, E ELA E UMA BORDA ============================
	/// O corpo e forjado pelo `SaveDeBancada` -> `AplicarBercoNoSave`, que chama o funil com
	/// `dono: null`. **B1 e impossivel no nascimento** -- um personagem em criacao nao tem assinatura no
	/// livro, nao tem os 250.000 de BP que a conquista exige e nem existe ainda. Entao quem nasce sem
	/// casa nasce SEMPRE na vizinhanca, e essa e a unica das quatro linhas em que nao ha escolha.
	/// ========================================================================================================
	///
	/// As tres seguintes sao o RENASCIMENTO (`DestinoDe` -> `MandarProBerco`), que e o outro caminho --
	/// e o unico em que a escolha existe.
	/// </summary>
	private void UmaRacaSemCasa(string raca, string natal, HashSet<string> aceitos, PlanetaNoEspaco longe)
	{
		CharacterSave c = SaveDeBancada($"Morto {raca}", raca, LinhagemDe(raca), "Normal", false,
										new Random(20260831));
		ServerPlayer pl = CorpoDoSave(c, FaixaDoRefugio);
		pl.Conta = ContaDoRefugio;

		Dominio? d = null;
		try
		{
			// ---- (1) NASCER sem casa: so B2, e sem perguntar ----------------
			PousarDeVerdade(pl);
			string aoNascer = pl.Zone.Name;
			bool chao1 = PisouEmChao(pl, out string porque1);

			Provar($"{raca}: NASCEU sem {natal} e acordou em '{aoNascer}', perto de casa "
				 + "(no nascimento nao ha dominio pra escolher)",
				   aceitos.Contains(aoNascer)
				   && !string.Equals(aoNascer, natal, StringComparison.OrdinalIgnoreCase)
				   && chao1,
				   $"acordou em {aoNascer} -- {porque1}");

			// ---- (2) AS DUAS SAIDAS EXISTEM, e o padrao e a vizinhanca ------
			d = FincarDominio(pl, longe, BandeiraEm(longe));
			(Vec2 _, List<Dominio> dom, Arredores perto, bool escolha) = EscolhaDeRefugio(pl);
			bool perdeu = PerdeuOBerco(pl);

			MandarProBerco(pl);
			PousarDeVerdade(pl);
			string padrao = pl.Zone.Name;
			bool chao2 = PisouEmChao(pl, out string porque2);

			Provar($"{raca}: com {natal} MORTO ha ESCOLHA ({dom.Count} dominio + {perto.Mundos.Count} "
				 + $"vizinho(s)), e o PADRAO e a vizinhanca ('{padrao}')",
				   perdeu && escolha && dom.Count == 1 && !perto.Vazia
				   && aceitos.Contains(padrao)
				   && !string.Equals(padrao, longe.Nome, StringComparison.OrdinalIgnoreCase)
				   && chao2,
				   $"PerdeuOBerco={perdeu} escolha={escolha} dominios={dom.Count} "
				 + $"vizinhos={perto.Mundos.Count} destino={padrao} -- {porque2}");

			// ---- (3) ELE ESCOLHE O DOMINIO: B1 ------------------------------
			ComandoDeRefugio(pl, "refugio", d.Chave.Texto);
			MandarProBerco(pl);
			PousarDeVerdade(pl);
			bool chao3 = PisouEmChao(pl, out string porque3);

			Provar($"{raca}: escolhendo o territorio conquistado, o corpo acorda em {longe.Nome} "
				 + $"('{pl.Zone.Name}')",
				   d.EhOSpawn
				   && string.Equals(pl.Zone.Name, longe.Nome, StringComparison.OrdinalIgnoreCase)
				   && chao3,
				   $"EhOSpawn={d.EhOSpawn} destino={pl.Zone.Name} -- {porque3}");

			// ---- (4) E ELE PODE VOLTAR ATRAS: B2 de novo, no MESMO mundo ----
			// "No MESMO mundo" e a parte que importa: o sorteio da vizinhanca e funcao pura da semente
			// do personagem, entao voltar atras tem que devolver o corpo pro lugar de onde ele saiu --
			// e nao a um segundo sorteio, que mudaria o endereco da pessoa a cada arrependimento.
			ComandoDeRefugio(pl, "refugio", "vizinhanca");
			MandarProBerco(pl);
			PousarDeVerdade(pl);
			bool chao4 = PisouEmChao(pl, out string porque4);

			Provar($"{raca}: voltando atras, o corpo acorda no MESMO mundo vizinho de antes "
				 + $"('{pl.Zone.Name}')",
				   !d.EhOSpawn
				   && string.Equals(pl.Zone.Name, padrao, StringComparison.OrdinalIgnoreCase)
				   && chao4,
				   $"EhOSpawn={d.EhOSpawn} antes={padrao} depois={pl.Zone.Name} -- {porque4}");
		}
		finally
		{
			if (d != null) PerderDominio(d, "", "bercoprova: fim da raca sem casa", anunciar: false);
			Recolher(pl);
		}
	}

	// =====================================================================
	// 9) AS BORDAS, UMA A UMA
	// =====================================================================
	/// <summary>
	/// **OS ESTADOS DE CANTO QUE A FASE 0 MEDIU, cada um com a sua linha.**
	///
	/// A fase 0 mediu a cascata do recuo por lista e ela era brutal: Terra morta mandava 10 de 24 racas
	/// pra Namek; Terra+Namek mandavam 14 pra Vegeta; Terra+Namek+Vegeta mandavam **19 pra ICER**. E as
	/// sagas do `npcs.json` ja destroem Vegeta e Namek sozinhas, ou seja, o segundo estado daquela lista
	/// acontece sem ninguem fazer nada.
	///
	/// Cada borda daqui e um daqueles estados, reposto pelo funil de producao, e a afirmacao e a NEGACAO
	/// do numero medido: zero em Namek, zero em Vegeta, zero em Icer -- e cada raca no proprio anel.
	/// </summary>
	private void AsBordasDoRefugio()
	{
		GD.Print("[bercoprova] -- 9) AS BORDAS, UMA A UMA (inclusive 'todos os planetas mortos')");

		ACascataDaFaseZero();
		TodosOsPlanetasMortos();
		OLacoDoCadaver();
		OCorpoSemBerco();
		ABandeiraQueNaoServe();
	}

	/// <summary>
	/// (9e) **A BANDEIRA QUE NAO SERVE** -- o corpo escolhe o dominio e a bandeira nao e chao.
	///
	/// ============================ ESTA BORDA NASCEU DE UM ERRO DESTA BANCADA ============================
	/// A familia 8 plantou bandeira com a posicao do planeta NO ESPACO (milhoes de pixels) em vez da
	/// posicao na zona, e 22 das 24 racas chegaram ao proprio territorio **DENTRO DE PAREDE**. O erro de
	/// entrada era da bancada; o que ele encontrou era de producao:
	///
	///   `PontoLivrePerto` **nunca devolve nulo** -- sem celula livre em 64 tiles, ele devolve o ponto
	///   PEDIDO, intacto. E `is { }` sobre um `Vec2` casa sempre. Entao o ponto BOM que o funil ja tinha
	///   conferido era trocado, sem conferencia nenhuma, pelo ponto guardado no `conquista.json`.
	///
	/// O `Dominio` documenta o caso que torna isso real: *"save de outro universo"*. Um `Fx/Fy` de um
	/// mapa que mudou (ou uma obra levantada em cima da bandeira) poe o soberano dentro da rocha do
	/// proprio planeta -- e a familia 7 nao veria, porque ela afirma o NOME da zona, que estaria certo.
	///
	/// A borda usa a mesma entrada impossivel de proposito: e a unica que garante que os 64 tiles do
	/// `PontoLivrePerto` falham, e portanto a unica que exercita a conferencia nova.
	/// ================================================================================================
	/// </summary>
	private void ABandeiraQueNaoServe()
	{
		var salvos = new List<Dominio>(_dominios);
		using PalcoDeMortes palco = PalcoDeMortesDeBancada();

		Provar("a injecao matou a Terra (rodada da bandeira que nao serve)",
			   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: bandeira fora do mapa"));

		CharacterSave c = SaveDeBancada("Prova Bandeira", "Human", "Human", "Normal", false, new Random(23));
		ServerPlayer pl = CorpoDoSave(c, FaixaDoRefugio);
		pl.Conta = ContaDoRefugio;

		try
		{
			PousarDeVerdade(pl);
			PlanetaNoEspaco longe = PreFeitoLongeDe("Earth");

			// A BANDEIRA IMPOSSIVEL: a posicao do planeta no MAPA DO UNIVERSO, que esta a milhoes de
			// pixels de qualquer celula da zona dele. Nenhum dos 64 aneis do `PontoLivrePerto` acha
			// chao, e ele devolve este mesmo ponto.
			Dominio d = FincarDominio(pl, longe, longe.Pos);
			ComandoDeRefugio(pl, "refugio", d.Chave.Texto);

			MandarProBerco(pl);
			PousarDeVerdade(pl);
			bool chao = PisouEmChao(pl, out string porque);

			Provar($"com a bandeira FORA DO MAPA, o corpo ainda acorda em {longe.Nome} e em CHAO DE "
				 + $"VERDADE ({pl.Pos.X / ZoneCollision.TileSize:0},{pl.Pos.Y / ZoneCollision.TileSize:0})",
				   d.EhOSpawn
				   && string.Equals(pl.Zone.Name, longe.Nome, StringComparison.OrdinalIgnoreCase)
				   && chao,
				   $"destino={pl.Zone.Name} -- {porque}. O ponto conferido pelo funil foi trocado por um "
				 + "ponto que ninguem conferiu (ver o `DestinoDe`)");

			// A METADE POSITIVA: com uma bandeira BOA o corpo chega PERTO dela, e nao no ponto padrao.
			// Sem isto, a conferencia nova poderia estar simplesmente ignorando a bandeira sempre -- e o
			// "chegar na propria bandeira" teria morrido calado.
			Vec2 boa = BandeiraEm(longe);
			PerderDominio(d, "", "bercoprova: troca de bandeira", anunciar: false);
			Dominio d2 = FincarDominio(pl, longe, boa + new Vec2(ZoneCollision.TileSize * 3, 0));
			ComandoDeRefugio(pl, "refugio", d2.Chave.Texto);

			MandarProBerco(pl);
			PousarDeVerdade(pl);

			Provar("...e com uma bandeira BOA o corpo chega junto dela (o 'chegar na propria bandeira' "
				 + "continua vivo)",
				   PisouEmChao(pl, out _)
				   && (pl.Pos - (boa + new Vec2(ZoneCollision.TileSize * 3, 0))).Length < ZoneCollision.TileSize * 2,
				   $"chegou em ({pl.Pos.X:0},{pl.Pos.Y:0}) e a bandeira esta em "
				 + $"({boa.X + ZoneCollision.TileSize * 3:0},{boa.Y:0}) -- a conferencia nova esta "
				 + "descartando bandeira boa");
		}
		finally
		{
			Recolher(pl);
			_dominios.Clear();
			_dominios.AddRange(salvos);
			SalvarConquista();
		}
	}

	/// <summary>
	/// (9a) A CASCATA: Earth -> Namek -> Vegeta mortos, na ORDEM DA CARTA -- o experimento da fase 0.
	///
	/// Ela nao repete a familia 3(a): la a pergunta e *"o destino de quem e da TERRA andou?"*; aqui e
	/// *"os tres numeros que a fase 0 mediu ainda acontecem?"*. Sao 19 racas sem casa ao mesmo tempo, e
	/// a afirmacao e nominal -- uma linha por raca, as 24.
	/// </summary>
	private void ACascataDaFaseZero()
	{
		string[] emOrdem = ["Earth", "Namek", "Vegeta"];

		using PalcoDeMortes _ = PalcoDeMortesDeBancada();

		foreach (string v in emOrdem)
			if (!MatarPlanetaNoPalco(ZoneKey.Premade(v), $"bercoprova: a cascata, {v}"))
			{
				Provar($"a injecao matou '{v}' (rodada da cascata)", false, "nao chegou a `Destruido`");
				return;
			}

		Chamada c = ChamadaNominal("Earth+Namek+Vegeta mortos", detalhar: true);
		UmaLinhaPorRaca(c, "cascata");

		// ============================ OS TRES NUMEROS DA FASE 0, NEGADOS UM A UM ============================
		// "Icer" entra na lista porque era o destino do terceiro degrau (19 de 24 racas), e ele NAO esta
		// morto nesta rodada -- ou seja, se o recuo por lista voltasse, e exatamente pra la que todo
		// mundo iria.
		//
		// **A CONTA E DE REFUGIADOS E NAO DE CORPOS**, e a primeira versao errava nisso: a raca Icer
		// nasce em Icer, entao "zero corpos em Icer" ficava vermelha com o codigo CERTO -- ela estava
		// exigindo que uma raca abandonasse a propria casa viva. O que a fase 0 mediu foi gente
		// DESABRIGADA sendo recolhida pra la, e e essa a conta.
		// ================================================================================================
		foreach (string destinoAntigo in (string[])["Earth", "Namek", "Vegeta", "Icer"])
		{
			string[] recolhidos = [.. c.Refugiados.Where(x => string.Equals(x.Obtido, destinoAntigo, StringComparison.OrdinalIgnoreCase))
										.Select(x => x.Raca)];
			Provar($"a cascata da fase 0 esta morta: ZERO desabrigados recolhidos pra '{destinoAntigo}'",
				   recolhidos.Length == 0,
				   $"{recolhidos.Length} foram parar la ({string.Join(",", recolhidos)}) -- "
				 + "o recuo por posicao numa lista voltou");
		}

		int semCasa = ConjuntoDeRacas().Count(r => emOrdem.Contains(Bercos.PlanetaNatal(r)));
		Provar($"...e as {semCasa} racas dos tres mundos SAIRAM de casa (e so elas)",
			   c.Refugiados.Count == semCasa && c.Refugiados.All(x => emOrdem.Contains(x.Natal)),
			   $"{c.Refugiados.Count} refugiadas de {semCasa} esperadas: "
			 + string.Join(", ", c.Refugiados.Take(8).Select(x => $"{x.Raca}({x.Natal})->{x.Obtido}")));

		int aneis = c.Refugiados.Select(x => x.Obtido).Distinct(StringComparer.Ordinal).Count();
		Provar($"...e as {c.Refugiados.Count} se espalharam por {aneis} mundos -- tres vizinhancas "
			 + "diferentes, e nao um destino unico",
			   aneis >= 3, string.Join(",", c.Refugiados.Select(x => x.Obtido).Distinct(StringComparer.Ordinal)));

		Provar($"...e ninguem ficou fora do chao ({c.ForaDoChao.Count} casos)",
			   c.ForaDoChao.Count == 0, string.Join(" | ", c.ForaDoChao.Take(6)));
	}

	/// <summary>
	/// (9b) **TODOS OS PLANETAS MORTOS** -- a borda que o dono pediu pelo nome.
	///
	/// ============================ E ELA NAO E A MESMA COISA QUE "NAO HA PARA ONDE IR" ============================
	/// Matar os sete pre-feitos da carta nao esvazia o universo: 62,4% das celulas tem estrela e cada
	/// estrela ancorada tem <see cref="Sistemas.OrbitasAncoradas"/> orbitas, entao a vizinhanca de cada
	/// natal continua cheia de mundos gerados vivos. E exatamente por isso que esta borda e a prova mais
	/// dura da regra nova: **no recuo por lista este estado nao tinha resposta nenhuma** -- a carta
	/// inteira estaria morta e o `ZonaDeRecuoViva` desistia devolvendo "a Terra, viva ou MORTA", ou seja,
	/// as 22 racas destrutiveis acordariam num cadaver.
	///
	/// Aqui as 24 tem que acordar EM CHAO DE VERDADE, nenhuma num pre-feito e nenhuma num mundo morto.
	/// ========================================================================================================
	/// </summary>
	private void TodosOsPlanetasMortos()
	{
		string[] daCarta = [.. Espaco.PreFeitos().Select(p => p.Nome)];

		using PalcoDeMortes _ = PalcoDeMortesDeBancada();

		var mortos = new List<string>();
		foreach (string nome in daCarta)
			if (MatarPlanetaNoPalco(ZoneKey.Premade(nome), $"bercoprova: todos mortos, {nome}"))
				mortos.Add(nome);

		Provar($"a injecao matou a CARTA INTEIRA ({mortos.Count} de {daCarta.Length}: {string.Join(",", mortos)})",
			   mortos.Count == daCarta.Length,
			   "sobrou pre-feito vivo -- esta borda nao e 'todos os planetas mortos' e nao prova o que diz");

		Chamada c = ChamadaNominal("TODOS os pre-feitos mortos", detalhar: true);
		UmaLinhaPorRaca(c, "todos mortos");

		Provar($"NENHUMA das {c.Total} racas ficou fora do chao (nem no espaco, nem em parede)",
			   c.ForaDoChao.Count == 0, string.Join(" | ", c.ForaDoChao.Take(6)));

		string[] emPreFeito = [.. c.Destino.Values.Where(o => daCarta.Contains(o, StringComparer.OrdinalIgnoreCase))
									.Distinct(StringComparer.Ordinal)];
		Provar("...e NINGUEM acordou num pre-feito da carta (todos eles sao cadaveres agora)",
			   emPreFeito.Length == 0, string.Join(",", emPreFeito));

		// A ZONA E NAO O NOME: a chave de um mundo gerado e a SEED, entao perguntar ao registro de
		// mortos por nome nao responderia nada. Por isso a sonda guarda a `ZoneKey`.
		string[] emCadaver = [.. c.ZonaDe.Where(k => ZonaMorta(k.Value)).Select(k => $"{k.Key}->{k.Value.Name}")];
		Provar("...e nenhum destino esta no registro de mortos", emCadaver.Length == 0,
			   string.Join(",", emCadaver));

		// AS DUAS QUE NAO PODEM PERDER A CASA continuam nela -- e sao a metade que mantem esta borda
		// honesta: se TUDO tivesse mudado, "todo mundo se mexeu" tambem seria o placar de um refugio
		// que ignorasse o estado do mundo e mandasse todo mundo pra longe.
		Provar("...e as racas de natal nao-celeste (Kai/Heaven e Demon/Hell) continuam EM CASA",
			   c.Destino.GetValueOrDefault("Kai") == "Heaven"
			   && c.Destino.GetValueOrDefault("Demon") == "Hell",
			   $"Kai->{c.Destino.GetValueOrDefault("Kai")} Demon->{c.Destino.GetValueOrDefault("Demon")}");

		int destrutiveis = ConjuntoDeRacas()
			.Count(r => Espaco.EhPlaneta(ZoneKey.Premade(Bercos.PlanetaNatal(r))));

		Provar($"...e as outras {destrutiveis} SAIRAM de casa (senao o verde seria imobilidade)",
			   c.Refugiados.Count == destrutiveis,
			   $"{c.Refugiados.Count} de {destrutiveis}");

		// E O RENASCIMENTO, no mesmo estado -- as duas pontas do funil, e nao so a de nascer.
		(int certas, List<string> erradas, List<string> saiu) r = RodadaDeRenascimento("TODOS mortos");
		Provar($"...e o RENASCIMENTO tambem achou refugio pra todas ({r.certas} certas, "
			 + $"{r.erradas.Count} perdidas, {r.saiu.Count} fora de casa)",
			   r.erradas.Count == 0 && r.saiu.Count == destrutiveis,
			   string.Join(" | ", r.erradas.Take(6)));
	}

	/// <summary>
	/// (9c) **O LACO DO CADAVER** -- a borda que o recuo antigo criava e ninguem media.
	///
	/// ============================ O DEFEITO ORIGINAL, ESCRITO ============================
	/// O `ZonaDeRecuoViva` desistia devolvendo *"a Terra, viva ou MORTA"*. O corpo ia parar num cadaver;
	/// o login seguinte caia no mesmo funil e devolvia a mesma Terra morta. Era um LACO, e nenhuma
	/// bancada o cobria.
	///
	/// O ultimo recurso de hoje e o ESPACO ABERTO, na coordenada de onde o natal ficava. A familia 7 ja
	/// afirma que `DestinoDe` devolve o espaco -- e **isso nao basta**: o corpo fica pairando exatamente
	/// SOBRE o disco do planeta morto, e quem decide se ele desce e o `TickDoEspaco`. Se ele descesse, o
	/// laco estaria de volta com outro nome.
	/// ================================================================================
	///
	/// As quatro afirmacoes tem donos diferentes: o funil (`DestinoDe`), o TIQUE (`TickDoEspaco`, o
	/// codigo que roda 30 vezes por segundo), a FRASE que o jogador le e o LOGIN
	/// (`PousarNoBercoSemPacote`, que e por onde o laco se fechava.)
	/// </summary>
	private void OLacoDoCadaver()
	{
		using PalcoDeMortes _ = PalcoDeMortesDeBancada();

		Provar("a injecao matou a Terra (rodada do cadaver)",
			   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: o laco do cadaver"));

		CharacterSave c = SaveDeBancada("Prova Cadaver", "Human", "Human", "Normal", false, new Random(13));
		ServerPlayer pl = CorpoDoSave(c, FaixaDoRefugio);

		List<string>? escutaAntes = EscutaDeAvisos;
		try
		{
			using (SemVizinhancaDeRefugio())
			{
				// A ESCUTA DE AVISOS e o unico jeito de uma bancada de servidor conferir uma FRASE:
				// `Avisar` termina num `Peer.Send`, e pacote que saiu no fio nao volta. Ver
				// `GameServer.Chat.cs`.
				EscutaDeAvisos = [];
				MandarProBerco(pl);
				List<string> avisos = [.. EscutaDeAvisos];
				EscutaDeAvisos = null;

				Provar($"o ultimo recurso poe o corpo no ESPACO ('{pl.Zone.Name}'), na coordenada de "
					 + $"onde a Terra ficava ({pl.Pos.X:0},{pl.Pos.Y:0})",
					   Espaco.EhEspaco(pl.Zone) && Math.Abs(pl.Pos.X) < 1 && Math.Abs(pl.Pos.Y) < 1,
					   $"{pl.Zone.Name} @ ({pl.Pos.X:0.0},{pl.Pos.Y:0.0})");

				// ============================ O CORPO ESTA SOBRE O CADAVER, E MESMO ASSIM NAO DESCE ============================
				// `PlanetaSob` responde "Earth" -- o disco continua no ceu depois da explosao. Quem se
				// recusa e o `TickDoEspaco` (`if (PlanetaMorto(destino)) return`), e e essa recusa que
				// transforma o vacuo num lugar, em vez de numa antessala do cadaver.
				// ==========================================================================================================
				PlanetaNoEspaco? sob = Espaco.PlanetaSob(SeedDoUniverso, pl.Pos);
				Provar("...e o disco da Terra morta esta MESMO debaixo dele (senao esta borda nao "
					 + "estaria medindo o caso dificil)",
					   sob is { } corpo && corpo.Nome == "Earth" && PlanetaMorto(corpo),
					   sob?.Nome ?? "(nada sob o corpo)");

				int tiques = PousarDeVerdade(pl);
				Provar($"...e o corpo NAO POUSA no cadaver: {TiquesDePouso} tiques do `TickDoEspaco` de "
					 + $"producao e ele continua no espaco ('{pl.Zone.Name}')",
					   tiques < 0 && Espaco.EhEspaco(pl.Zone),
					   $"pousou em {pl.Zone.Name} no tique {tiques} -- o laco do cadaver voltou");

				// ============================ AS FRASES SAO IMPRESSAS, E ISSO NAO E ENFEITE ============================
				// A primeira versao desta linha procurava `"O chão vem em seguida"` -- **com acento** --
				// e a frase do `MandarProBerco` e `"O chao vem em seguida."`, sem. A afirmacao ficou
				// VERDE sem poder ficar vermelha nunca, que e o pior tipo de checagem que existe, e so
				// se descobriu isso porque o defeito que ela procurava existia mesmo.
				//
				// Imprimir o que foi capturado e o antidoto: uma comparacao de texto que ninguem ve e
				// uma comparacao que ninguem consegue conferir.
				// ==================================================================================================
				GD.Print($"[bercoprova]      as frases desta morte: {string.Join("  ||  ", avisos)}");

				// O `ContarORefugio` ja disse "voce abre os olhos no vacuo, nao ha chao"; se a chegada
				// disser em seguida que o chao vem, a pessoa fica esperando um chao que nunca vem.
				Provar("...e as frases da mesma morte NAO se contradizem (nenhuma promete chao)",
					   avisos.Any(a => a.Contains("vacuo"))
					   && !avisos.Any(a => a.Contains("vem em seguida")),
					   "a chegada prometeu chao sobre um cadaver: " + string.Join("  ||  ", avisos));

				// O LOGIN SEGUINTE: o outro caminho (`PousarNoBercoSemPacote`), que e o que rodava no
				// `Entrar` e fechava o laco.
				PousarNoBercoSemPacote(pl);
				Provar("...e o LOGIN seguinte devolve o espaco de novo, e nao a Terra morta "
					 + $"('{pl.Zone.Name}')",
					   Espaco.EhEspaco(pl.Zone) && !ZonaMorta(pl.Zone),
					   $"{pl.Zone.Name} -- se isto virar um planeta, o laco volta pelo login");
			}
		}
		finally
		{
			EscutaDeAvisos = escutaAntes;
			Recolher(pl);
		}
	}

	/// <summary>
	/// (9d) O CORPO SEM BERCO, e o natal QUE NAO ESTA NA CARTA -- os dois recuos da ancora.
	///
	/// O primeiro acontece em jogo (clone, NPC, corpo forjado): sem `Planeta`, o funil manda pro refugio
	/// e a ancora vira a ORIGEM da carta. O segundo e o `K < 0` que o comentario da
	/// <see cref="AncoraDoRefugio"/> cita -- Paraiso e Inferno existem como zona e nao como corpo.
	///
	/// **A borda de verdade e que os dois POUSAM.** Uma ancora que nao resolvesse devolveria (0,0) do
	/// mesmo jeito, e a diferenca entre "o recuo funcionou" e "o campo estava zerado" so aparece no
	/// corpo no chao.
	/// </summary>
	private void OCorpoSemBerco()
	{
		using PalcoDeMortes _ = PalcoDeMortesDeBancada();

		Provar("a injecao matou a Terra (rodada do corpo sem berco)",
			   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: corpo sem berco"));

		HashSet<string> daOrigem = OndeEsteCorpoDeveriaAcordar("Earth");

		CharacterSave c = SaveDeBancada("Prova SemBerco", "Human", "Human", "Normal", false, new Random(17));
		ServerPlayer pl = CorpoDoSave(c, FaixaDoRefugio);
		try
		{
			// ---- (i) SEM BERCO NENHUM ------------------------------------
			pl.Berco = default;
			MandarProBerco(pl);
			PousarDeVerdade(pl);
			bool chaoA = PisouEmChao(pl, out string porqueA);

			Provar($"o corpo SEM BERCO acha refugio a partir da origem da carta ('{pl.Zone.Name}') e "
				 + "pousa em chao de verdade",
				   daOrigem.Contains(pl.Zone.Name) && chaoA, $"{pl.Zone.Name} -- {porqueA}");

			// ---- (ii) NATAL FORA DA CARTA -------------------------------
			// A zona morta e a Terra (a unica destrutivel nesta rodada); o NATAL diz "Heaven", que nao e
			// corpo celeste. A ancora tem que cair no recuo -- `b.Pos`, e depois a origem -- em vez de
			// sair procurando na carta um nome que ela nao tem.
			pl.Berco = new Berco { Planeta = "Earth", Natal = "Heaven", PreFeito = true };
			MandarProBerco(pl);
			PousarDeVerdade(pl);
			bool chaoB = PisouEmChao(pl, out string porqueB);

			Provar($"o natal FORA DA CARTA ('Heaven') cai no recuo da ancora e tambem pousa "
				 + $"('{pl.Zone.Name}')",
				   daOrigem.Contains(pl.Zone.Name) && chaoB, $"{pl.Zone.Name} -- {porqueB}");
		}
		finally { Recolher(pl); }
	}

	// =====================================================================
	// 10) A LISTA MORREU -- a zona ficticia na frente da carta
	// =====================================================================
	/// <summary>
	/// **A INJECAO QUE PROVAVA O DEFEITO, AGORA PROVANDO O CONTRARIO DELE.**
	///
	/// ============================ A MESMA ARMA, APONTADA PRO OUTRO LADO ============================
	/// A bancada antiga enfiava um planeta ficticio ("Hera") na FRENTE de `Espaco.PreFeitos()` por um
	/// campo trocavel e media o destino de quem tinha perdido o berco. **O destino mudava de planeta** --
	/// nada no mundo tinha mudado, so a ordem de uma lista. Era essa a prova de que o recuo era uma
	/// POSICAO NUMA LISTA, e o pedido do dono substituiu exatamente isso.
	///
	/// A injecao continua sendo feita, no mesmo lugar e do mesmo jeito (<see cref="_cartaDoRefugio"/>),
	/// e a afirmacao inverteu: **NINGUEM pode se mover.** E ela nao e vazia -- o refugio AINDA le a
	/// carta: e o <see cref="AncoraDoRefugio"/> quem a percorre, procurando o natal pelo NOME. Se
	/// alguem trocar aquela busca por nome por um indice, uma posicao ou um "primeiro que serve", esta
	/// familia fica vermelha na mesma rodada.
	/// ==========================================================================================
	///
	/// ============================ E AS DUAS METADES SAO EXIGIDAS ============================
	/// Uma injecao que nao mexe em nada tambem deixaria "ninguem se moveu" verde. Entao a regra
	/// DELETADA e reescrita aqui em quatro linhas -- *"o primeiro pre-feito vivo da carta"* -- e usada
	/// como grupo de controle:
	///   * **o que a regra velha responderia MUDOU** (de 'Namek' pra 'Hera'), ou seja, a injecao chegou;
	///   * **o que a regra nova responde NAO MUDOU**, raca por raca, as 24.
	///
	/// Sem a primeira metade esta familia seria a "afirmacao verde num sistema morto" que este projeto
	/// ja catalogou; sem a segunda, ela nao afirmaria nada.
	/// ====================================================================================
	/// </summary>
	private void AListaMorreuDeVerdade()
	{
		GD.Print("[bercoprova] -- 10) A LISTA MORREU: um planeta ficticio na FRENTE da carta");

		using PalcoDeMortes _ = PalcoDeMortesDeBancada();

		Provar("a injecao matou a Terra (rodada da carta)",
			   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: a carta trocada"));

		Chamada antes = ChamadaNominal("carta de verdade", detalhar: false);
		string velhaAntes = PrimeiroPreFeitoVivo(Espaco.PreFeitos());

		// O INTRUSO: vivo, pre-feito e LONGE de todo mundo -- a 3x a distancia Terra-Namek, ou seja
		// fora do alcance de qualquer busca de vizinhanca. Se um corpo aparecer nele, foi pela POSICAO.
		var intruso = new PlanetaNoEspaco
		{
			Nome = PlanetaIntruso,
			Pos = new Vec2((float)(Espaco.DistanciaTerraNamek * 3.0), 0),
			Raio = 200,
			Premade = true,
			Seed = 0,
		};

		using (CartaDeRefugioCom(intruso))
		{
			string velhaDepois = PrimeiroPreFeitoVivo(_cartaDoRefugio!);

			Provar($"a injecao CHEGOU: '{PlanetaIntruso}' esta na frente da carta e vivo, e a regra "
				 + $"DELETADA teria mudado de resposta ('{velhaAntes}' -> '{velhaDepois}')",
				   velhaAntes != velhaDepois && velhaDepois == PlanetaIntruso,
				   $"a carta trocada nao mudou a resposta da regra velha ({velhaAntes} -> {velhaDepois}) "
				 + "-- a injecao nao aconteceu e o resto desta familia nao prova nada");

			// AS 24 LINHAS NOMINAIS AQUI VALEM DOBRADO: o `OndeEsteCorpoDeveriaAcordar` da bancada le a
			// carta DE VERDADE (`Espaco.PreFeitos()`), e a producao esta lendo a TROCADA. As duas tem
			// que continuar concordando -- ou seja, o intruso nao pode nem sequer mudar a ancora.
			Chamada depois = ChamadaNominal("com o intruso na frente", detalhar: false);
			UmaLinhaPorRaca(depois, "carta trocada");

			// A AFIRMACAO CENTRAL, NOMINAL: raca por raca, o MESMO mundo.
			var mudaram = new List<string>();
			foreach ((string raca, string destino) in antes.Destino.OrderBy(k => k.Key, StringComparer.Ordinal))
			{
				string agora = depois.Destino.GetValueOrDefault(raca, "(sumiu)");
				bool igual = string.Equals(agora, destino, StringComparison.Ordinal);
				if (!igual) mudaram.Add($"{raca}: {destino} -> {agora}");

				Provar($"{raca}: um planeta a mais na frente da carta NAO moveu o corpo ('{destino}')",
					   igual, $"{destino} -> {agora} -- a posicao numa lista voltou a decidir destino");
			}

			Provar($"...e o conjunto inteiro dos {antes.Destino.Count} destinos ficou IDENTICO",
				   mudaram.Count == 0 && antes.Destino.Count == depois.Destino.Count,
				   string.Join(" | ", mudaram.Take(6)));

			Provar($"...e NINGUEM foi parar em '{PlanetaIntruso}' (que e o que a regra velha faria)",
				   !depois.Destino.Values.Any(o => string.Equals(o, PlanetaIntruso, StringComparison.Ordinal)),
				   string.Join(",", depois.Destino.Where(k => k.Value == PlanetaIntruso).Select(k => k.Key)));
		}

		Provar("a carta de verdade voltou quando o escopo fechou",
			   _cartaDoRefugio == null, "a bancada deixou a carta trocada de pe");
	}

	/// <summary>
	/// A REGRA DELETADA, REESCRITA EM QUATRO LINHAS -- *o primeiro pre-feito vivo da carta*.
	///
	/// Ela nao existe mais no jogo e nao deve voltar; ela esta aqui como GRUPO DE CONTROLE. E a unica
	/// forma de mostrar que a injecao da familia 10 tem efeito sobre alguma coisa -- do contrario
	/// "ninguem se moveu" seria indistinguivel de "nada foi injetado".
	/// </summary>
	private string PrimeiroPreFeitoVivo(IEnumerable<PlanetaNoEspaco> carta)
	{
		foreach (PlanetaNoEspaco p in carta)
			if (!ZonaMorta(ZoneKey.Premade(p.Nome))) return p.Nome;
		return "(nenhum)";
	}

	// =====================================================================
	// AS FERRAMENTAS DESTAS TRES FAMILIAS
	// =====================================================================
	/// <summary>
	/// UMA LINHA POR RACA a partir de uma <see cref="Chamada"/> -- o que faz uma varredura virar prova.
	///
	/// A `ChamadaNominal` IMPRIME as 24 linhas e afirma o AGREGADO (`Desvios.Count == 0`). O agregado e
	/// suficiente pra um placar e insuficiente pra uma bancada: um total certo nao diz QUAL raca
	/// quebrou, e foi um agregado ("as racas se espalham por >= 5 mundos") que ficou verde com 13 delas
	/// nascendo em Namek -- a cegueira 3 que a familia 6 mede.
	/// </summary>
	private void UmaLinhaPorRaca(Chamada c, string rotulo)
	{
		foreach (string raca in c.Destino.Keys.OrderBy(x => x, StringComparer.Ordinal))
		{
			string natal = Bercos.PlanetaNatal(raca);
			HashSet<string> aceitos = OndeEsteCorpoDeveriaAcordar(natal);
			string obtido = c.Destino[raca];

			Provar($"[{rotulo}] {raca}: natal '{natal}' -> acordou em '{obtido}'",
				   aceitos.Contains(obtido),
				   $"esperado um de {string.Join("|", aceitos.OrderBy(x => x, StringComparer.Ordinal))}");
		}
	}

	/// <summary>
	/// A LINHAGEM PADRAO DE UMA RACA -- a primeira que a tela de criacao oferece.
	///
	/// Igual a que a <see cref="ChamadaNominal"/> usa, e pelo mesmo motivo: a linhagem muda a classe
	/// possivel, e uma linhagem sorteada faria a mesma raca ter esperados diferentes entre familias.
	/// </summary>
	private static string LinhagemDe(string raca)
	{
		string[] escolhas = CharacterDraft.EscolhasDeClasse(raca);
		return escolhas.Length > 0 ? escolhas[0] : "";
	}

	/// <summary>
	/// O PRE-FEITO VIVO MAIS LONGE DE UM NATAL -- o territorio conquistado das familias 8 e 9.
	///
	/// Ele e escolhido pela DISTANCIA e nao por nome cravado: um nome escrito aqui envelheceria calado
	/// no dia em que a carta mudasse, e a prova "o corpo foi pro dominio e nao pra vizinhanca" perderia
	/// o sentido se o dominio por acaso passasse a ser um vizinho. A propria familia afirma a distancia
	/// antes de usar (ver <see cref="SemCasa"/>).
	/// </summary>
	private PlanetaNoEspaco PreFeitoLongeDe(string natal)
	{
		Vec2 a = AncoraDaBancada(natal);
		PlanetaNoEspaco melhor = default;
		double maior = -1;

		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
		{
			if (string.Equals(p.Nome, natal, StringComparison.OrdinalIgnoreCase)) continue;
			if (PlanetaMorto(p) || _catalogo?.Get(ZoneKey.Premade(p.Nome)) == null) continue;

			double dx = p.Pos.X - a.X, dy = p.Pos.Y - a.Y, q = dx * dx + dy * dy;
			if (q > maior) { maior = q; melhor = p; }
		}

		return melhor;
	}

	/// <summary>
	/// A POSICAO DE UM NATAL, calculada AQUI e nao pedida ao <see cref="AncoraDoRefugio"/>.
	///
	/// As duas contas sao a mesma frase e sao de donos diferentes de proposito -- e a mesma disciplina
	/// do <see cref="OndeEsteCorpoDeveriaAcordar"/>. Uma bancada que perguntasse a producao onde e
	/// "perto" mediria a coerencia de um arquivo consigo mesmo.
	/// </summary>
	private static Vec2 AncoraDaBancada(string natal)
	{
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, natal, StringComparison.OrdinalIgnoreCase)) return p.Pos;
		return default;
	}

	private static double DistanciaAte(string natal, Vec2 alvo)
	{
		Vec2 a = AncoraDaBancada(natal);
		double dx = alvo.X - a.X, dy = alvo.Y - a.Y;
		return Math.Sqrt(dx * dx + dy * dy);
	}

	/// <summary>
	/// ONDE A BANDEIRA FICA FINCADA, em pixels DA ZONA -- e nao do espaco.
	///
	/// ============================ ISTO JA FOI UM DEFEITO DESTA BANCADA, E ELE ACHOU UM DE PRODUCAO ============================
	/// A primeira versao passava `p.Pos` -- a posicao do planeta NO MAPA DO UNIVERSO, na casa dos
	/// milhoes de pixels -- como ponto da bandeira, que e o que a familia 7 ja fazia. O campo certo e
	/// outro: `Dominio.Fx/Fy` e *"onde a bandeira ficou fincada, em pixels da zona"*, e em producao ele
	/// vem do `pl.Pos` de alguem que esta DE PE no planeta.
	///
	/// O erro nao ficou escondido: as 22 racas chegaram no dominio DENTRO DE PAREDE -- e a investigacao
	/// mostrou que a culpa nao era so da bancada. Ver o bloco novo do `DestinoDe`
	/// (`GameServer.Conquista.cs`): `PontoLivrePerto` devolve o ponto PEDIDO quando nao acha nada livre,
	/// e o `is { }` de um `Vec2` casa sempre, entao o ponto bom do funil era trocado por um ponto que
	/// ninguem tinha conferido. A borda 9(e) guarda esse caso pelo nome.
	/// ================================================================================================================
	/// </summary>
	private Vec2 BandeiraEm(PlanetaNoEspaco p) => PontoDeNascimento(Espaco.ZonaDe(p));
}
