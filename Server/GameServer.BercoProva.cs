using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A PROVA DO BERCO (`--bercoprova`) -- **a bancada que consegue ficar VERMELHA**.
///
/// ============================ O RELATO, E POR QUE ELE PRECISOU DE UMA TERCEIRA BANCADA ============================
/// **Pedido do dono, literal**: *"por algum motivo todas as racas q eram pra nascer na terra tao
/// nascendo em namek (isso n e problema do export pq ta acontecendo ate mesmo com a build dentro do
/// godot)"*.
///
/// Ele estava certo em tudo, inclusive em ter descartado o empacotamento. A cadeia medida foi:
/// alguem rodou a `--escudoteste` no mundo de verdade, ela detonou uma Final Explosion de raio maximo
/// na TERRA pelo funil de producao (`GameServer.Tecnicas.G3.cs:644`), o `SalvarPlanetasMortos` gravou
/// a Terra como destruida **na pasta de saves do dono** e, a partir dali, o `DestinoDoBerco` fez a
/// coisa CERTA -- ele se recusa a por um corpo pra nascer num cadaver
/// (`GameServer.Berco.cs`, o comentario "NINGUEM NASCE NUM CADAVER") -- e desviou todo mundo pro
/// primeiro pre-feito vivo da carta, que e Namek (`Core/World/Espaco.cs:116`).
///
/// **Nao havia uma linha errada no berco.** E havia DEZENAS de bancadas verdes.
/// ==============================================================================================================
///
/// ============================ AS TRES CEGUEIRAS QUE ESTA BANCADA EXISTE PRA FECHAR ============================
/// A familia 6 nao explica isto: ela MEDE. Cada uma das tres e afirmada com o defeito injetado, e as
/// tres ficam VERDES no mundo estragado -- que e o que prova que elas eram cegas.
///
///   1. A INTENCAO NAO E O EFEITO. A `--bercovivo` afirmava `b.Planeta` -- o resultado da funcao PURA
///      `Bercos.Onde`, que continuava certissima ("Earth" pra quem e da Terra) -- e so IMPRIMIA a zona
///      em que o corpo acordou. 72 provas verdes com 13 racas acordando em Namek.
///
///   2. MEDIR A FUNCAO EM VEZ DO MUNDO. A `--povoteste` mede `Bercos.RacasNascidasEm` direto, e o
///      comentario dela (`GameServer.PovoamentoTeste.cs:376`) DIZ o motivo: *"no save vivo,
///      `planetas-mortos.json` tem Vegeta E TERRA condenados"* e o mundo daria "verde por ausencia".
///      O sintoma estava escrito num comentario de bancada como se fosse paisagem.
///
///   3. O AGREGADO ESCONDE O NOME. "a tabela espalha as racas por >= 5 mundos" e "nenhum mundo unico
///      recebe TODAS as racas" continuam verdes com a Terra morta **mesmo medidas no efeito** -- sao
///      5 mundos e o maior grupo tem 17 de 24. So a afirmacao NOMINAL, uma linha por raca, corta.
/// ==========================================================================================================
///
/// ============================ A DIVISAO DE TRABALHO ENTRE AS TRES IRMAS ============================
///   `--diagberco`  -- a REGRA: funcao pura + catalogo de zonas, sem corpo nenhum.
///   `--bercovivo`  -- a CORRENTE: ficha no disco -> corpo no mundo -> pouso -> chao de verdade.
///   `--bercoprova` -- **a bancada como REU**: ela poe o mundo em cada estado que ja quebrou o jogo e
///                     exige o placar certo em cada um. Uma checagem que so foi vista PASSANDO e
///                     indistinguivel de `Provar("...", true)`.
///
/// A diferenca estrutural e que a chamada nominal aqui e uma SONDA reusavel
/// (<see cref="ChamadaNominal"/>), rodada seis vezes contra seis mundos diferentes -- e nao uma
/// varredura que acontece uma vez, no mundo que por acaso estiver no disco.
/// ================================================================================================
///
/// ============================ E ELA TEM UMA SEGUNDA METADE, NOUTRO ARQUIVO ============================
/// As familias 8, 9 e 10 moram em `GameServer.RefugioProva.cs` e rodam nesta mesma bancada. Elas
/// existem porque a familia 7 mede a escolha do refugio com UM personagem, e o pedido do dono nao fala
/// de um personagem: *"quando uma RACA fica sem planeta natal..."*. La a prova e nominal nas duas
/// metades (natal de pe e natal morto), passa pelas bordas -- inclusive **todos os planetas mortos** --
/// e poe de volta a injecao do planeta ficticio na frente da carta, agora exigindo que ninguem se mova.
///
/// **AVISO DE STDERR**: esta bancada passa a deixar DUAS linhas de `WARNING` no stderr, e as duas sao a
/// prova funcionando: a familia 8 tenta destruir `Heaven` e `Hell` de verdade pra mostrar que o
/// `ComecarDestruicao` recusa. Stderr com essas duas linhas e nenhuma outra e o placar limpo.
/// ==================================================================================================
///
/// ============================ E O MUNDO DO DONO NAO PAGA A CONTA ============================
/// Toda morte de planeta desta bancada acontece dentro do `using PalcoDeMortesDeBancada()`
/// (`GameServer.Destruicao.cs`): a gravacao e recusada, o registro/tremores/cargas/ceu voltam no fim,
/// e o `planetas-mortos.json` do dono nunca e tocado. **Foi exatamente essa falta que causou o
/// relato** -- uma bancada matando um planeta de verdade --, entao esta aqui matando planetas seria
/// intoleravel sem o palco.
/// =========================================================================================
///
///     Godot --headless --path . --server --port 7961 --bercoprova
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A faixa de ids desta bancada dentro do <see cref="CorpoDoSave"/>. 20 e nao 1..8 pra nao dividir
	/// numeracao com as sete familias da `--bercovivo`: as duas podem rodar no mesmo boot (e rodam --
	/// e assim que o placar comparativo do relatorio foi medido), e `_players[id] = corpo` sobrescreve
	/// CALADO, entao uma colisao apagaria um corpo da lista da zona sem erro nenhum.
	/// </summary>
	private const int FaixaDaProva = 20;

	/// <summary>
	/// A CONTA DA BANCADA DO REFUGIO -- a que finca bandeira na familia 7. Separada da conta das
	/// outras familias porque dominio e do PERSONAGEM (a assinatura), e um dominio deixado no livro
	/// por uma familia mudaria o destino das outras sem ninguem entender por que.
	/// </summary>
	private const string ContaDoRefugio = "bancada_refugio";

	private int _pbOk, _pbFalhou;

	private void Provar(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _pbOk++; GD.Print($"[bercoprova]   OK    {oque}"); return; }
		_pbFalhou++;
		GD.PrintErr($"[bercoprova]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaProvaDoBerco()
	{
		_pbOk = _pbFalhou = 0;
		GD.Print("[bercoprova] ============ A PROVA DO BERCO: uma linha por raca, e o defeito de volta ============");

		try
		{
			OMundoComoEleEsta();
			ODefeitoDeVolta();
			AOrdemDaCartaNaoImportaMais();
			RenascerEhOOutroCaminho();
			OPovoNoMundoENaoNaTabela();
			PorQueNenhumaBancadaPegou();
			AEscolhaDoRefugio();

			// AS TRES DE `GameServer.RefugioProva.cs` -- a familia 7 mede a escolha com UM personagem,
			// e estas medem com as 24, nas bordas, e com a carta trocada. Ver o cabecalho de la.
			AEscolhaRacaPorRaca();
			AsBordasDoRefugio();
			AListaMorreuDeVerdade();
		}
		catch (Exception ex)
		{
			// ABORTAR NO MEIO NAO PODE PARECER SUCESSO -- a mesma licao que a `--povoteste` ja pagou
			// (`GameServer.PovoamentoTeste.cs:348`): sem esta linha uma excecao subia, o placar saia
			// "0 falharam" e a unica pista era a contagem de checagens ser menor que a de sempre.
			Provar("a bancada chegou ao fim sem excecao", false, ex.ToString());
		}
		finally
		{
			ApagarContaDaBancada();
		}

		GD.Print($"[bercoprova] ============ {_pbOk} passaram, {_pbFalhou} falharam ============");
		if (_pbFalhou > 0) GD.PushError($"[bercoprova] {_pbFalhou} falha(s) -- veja o console");
	}

	// =====================================================================
	// A SONDA -- a chamada nominal, uma linha por raca
	// =====================================================================
	/// <summary>
	/// O RESULTADO DE UMA CHAMADA NOMINAL. Ele NAO afirma nada: quem afirma e a familia que o pediu,
	/// e e isso que permite a mesma sonda ser exigida VERDE numa hora e VERMELHA na outra.
	/// </summary>
	internal sealed class Chamada
	{
		public readonly List<string> Certas = [];
		public readonly List<(string Raca, string Esperado, string Obtido)> Desvios = [];

		/// <summary>
		/// QUEM ACORDOU NO REFUGIO -- certo, mas fora de casa, porque casa nao existe mais.
		///
		/// ============================ SEM ESTA LISTA A FAMILIA 2 FICARIA VERDE POR IMOBILIDADE ============================
		/// Depois da regra nova, "a chamada nominal ficou verde com a Terra morta" e verdade tanto se o
		/// refugio funcionou quanto se NADA ACONTECEU -- porque o conjunto esperado passou a incluir os
		/// mundos de refugio. As duas metades tem que ser exigidas juntas: **ninguem foi pro lugar
		/// errado** (`Desvios`) e **todo mundo saiu de casa** (esta lista). E o mesmo par positiva/negativa
		/// da familia 1, aplicado ao mundo estragado.
		/// ==========================================================================================================
		/// </summary>
		public readonly List<(string Raca, string Natal, string Obtido)> Refugiados = [];

		/// <summary>Quantos corpos acordaram em cada mundo. E o que a familia 6 usa pros agregados.</summary>
		public readonly Dictionary<string, int> PorMundoObtido = new(StringComparer.Ordinal);

		/// <summary>
		/// ONDE CADA RACA ACORDOU, pelo nome dela -- a sonda inteira num mapa.
		///
		/// ============================ SEM ELE A FAMILIA 10 NAO TERIA COMO SER NOMINAL ============================
		/// As outras colecoes desta classe respondem "quantos" e "quem se desviou". A pergunta da
		/// familia da carta e outra e nao cabe em nenhuma delas: *este corpo foi parar no MESMO lugar
		/// nas duas rodadas?*. Comparar `PorMundoObtido` nao serve -- ele ficaria identico se duas
		/// racas simplesmente trocassem de mundo entre si, que e exatamente o desvio que uma carta
		/// trocada poderia causar.
		/// ====================================================================================================
		/// </summary>
		public readonly Dictionary<string, string> Destino = new(StringComparer.Ordinal);

		/// <summary>
		/// A ZONA em que cada raca acordou -- a CHAVE, e nao o rotulo.
		///
		/// O <see cref="Destino"/> guarda nome porque nome e o que se le num placar. Nome nao serve pra
		/// perguntar ao registro de mortos: a chave de um mundo gerado e a SEED
		/// (<see cref="ChaveDePlaneta"/>), e dois mundos de celulas diferentes chegam a sair com o mesmo
		/// rotulo. A familia 9(b) precisa afirmar *"nenhum destino esta no registro de mortos"*, e essa
		/// pergunta so tem resposta com a chave na mao.
		/// </summary>
		public readonly Dictionary<string, ZoneKey> ZonaDe = new(StringComparer.Ordinal);

		/// <summary>
		/// QUEM NAO PISOU EM CHAO DE VERDADE (<see cref="PisouEmChao"/>) -- ficou no espaco, caiu numa
		/// zona sem colisao ou dentro de parede.
		///
		/// A sonda media a ZONA e nada mais, e zona certa nao e chao: um refugio que devolvesse a
		/// orbita de um mundo que nunca nasce ficaria VERDE em toda afirmacao nominal deste arquivo.
		/// A frase do pedido do dono e literal -- *"ninguem pode nascer no espaco"*.
		/// </summary>
		public readonly List<string> ForaDoChao = [];

		/// <summary>Quantos corpos a TABELA prometia pra cada mundo. A outra metade do "quem nasce aqui".</summary>
		public readonly Dictionary<string, int> PorMundoEsperado = new(StringComparer.Ordinal);

		public int Total => Certas.Count + Desvios.Count;

		/// <summary>Os mundos ERRADOS pra onde os corpos foram parar, sem repetir.</summary>
		public string[] DestinosErrados =>
			[.. Desvios.Select(d => d.Obtido).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)];

		public string Resumo => Desvios.Count == 0
			? $"{Certas.Count}/{Total} no planeta certo"
			: $"{Certas.Count}/{Total} certas; {Desvios.Count} DESVIADAS -> "
			  + string.Join(" | ", Desvios.Take(6).Select(d => $"{d.Raca}: {d.Esperado}->{d.Obtido}"))
			  + (Desvios.Count > 6 ? $" (+{Desvios.Count - 6})" : "");
	}

	/// <summary>
	/// **A SONDA.** Uma raca por vez, pelo caminho de PRODUCAO inteiro, dizendo a zona ESPERADA e a
	/// OBTIDA.
	///
	/// ============================ AS DUAS PONTAS SAO DE DONOS DIFERENTES, E E ISSO QUE FAZ A PROVA ============================
	/// ESPERADO -- <see cref="OndeEsteCorpoDeveriaAcordar"/>: com o natal vivo, a
	///             <see cref="Bercos.PlanetaNatal"/> (a frase do dono numa funcao so); com o natal
	///             MORTO, os mundos vivos mais perto dele, RE-DERIVADOS A MAO das primitivas da carta.
	/// OBTIDO   -- `pl.Zone.Name` DEPOIS de `Birth.Nascer` -> `AplicarBercoNoSave` ->
	///             `AccountStore.ParaJogador` -> `PorNoMundo` -> `TickDoEspaco` -> pouso. O servidor
	///             inteiro.
	///
	/// A `--bercovivo` comparava `b.Planeta` (o resultado de `Bercos.Onde`) com `Bercos.PlanetaNatal` --
	/// **as duas pontas eram o mesmo arquivo**, e o desvio inteiro do relato acontece DEPOIS dele, no
	/// `DestinoDoBerco`. Uma bancada em que as duas pontas tem o mesmo dono nao mede a corrente: mede a
	/// coerencia de um arquivo consigo mesmo.
	/// ========================================================================================================================
	///
	/// ============================ O "ESPERADO" DEIXOU DE SER UM NOME ============================
	/// **Esta e a mudanca que a regra nova obrigou.** Enquanto o destino de quem perdia o berco era o
	/// primeiro pre-feito vivo da carta, "o esperado" era sempre um NOME (o natal), e todo desvio era
	/// erro. Com o refugio, quem perde o natal ACERTA indo pra outro lugar -- e o esperado vira um
	/// CONJUNTO: o natal enquanto ele existir, os mundos vivos mais perto dele quando ele acabar.
	///
	/// Uma sonda que continuasse cravando o nome ficaria vermelha exatamente onde o pedido do dono
	/// esta sendo cumprido, e a leitura obvia seria "conserta o refugio" -- ou seja, ela empurraria a
	/// proxima pessoa a desfazer o trabalho.
	/// =======================================================================================
	///
	/// ============================ A CLASSE E CRAVADA EM "Normal", E ISSO E DE PROPOSITO ============================
	/// As duas excecoes do dono dependem da CLASSE (o Low-Class vai pra Terra, o Lendario e exilado) e
	/// as duas sao sorteadas -- deixar o dado rolar aqui faria a chamada nominal ter linhas cujo
	/// "esperado" muda de rodada pra rodada, e a sonda precisa ser IDENTICA nos seis mundos pra que a
	/// diferenca entre eles seja o mundo e nao o dado. As duas excecoes tem prova propria e nominal em
	/// <see cref="OMundoComoEleEsta"/>.
	/// ==========================================================================================================
	/// </summary>
	/// <param name="rotulo">Aparece em cada linha do console -- e o nome do MUNDO em que a sonda rodou.</param>
	/// <param name="detalhar">Falso nas rodadas em que se espera vermelho, pra nao encher o console.</param>
	private Chamada ChamadaNominal(string rotulo, bool detalhar = true)
	{
		var r = new Chamada();

		// SEMENTE FIXA: a sonda tem que ser a MESMA funcao nos seis mundos. Um `Random` novo por
		// rodada faria a diferenca entre duas rodadas ser o dado, e nao o estado do mundo.
		var rng = new Random(20260816);

		foreach (string raca in ConjuntoDeRacas())
		{
			string[] escolhas = CharacterDraft.EscolhasDeClasse(raca);
			string linhagem = escolhas.Length > 0 ? escolhas[0] : "";
			string natal = Bercos.PlanetaNatal(raca);
			HashSet<string> aceitos = OndeEsteCorpoDeveriaAcordar(natal);
			string esperado = string.Join("|", aceitos.OrderBy(x => x, StringComparer.Ordinal));

			CharacterSave c = SaveDeBancada($"Prova {raca}", raca, linhagem, "Normal", false, rng);
			ServerPlayer pl = CorpoDoSave(c, FaixaDaProva);
			try
			{
				PousarDeVerdade(pl);
				string obtido = pl.Zone.Name;

				r.PorMundoEsperado[natal] = r.PorMundoEsperado.GetValueOrDefault(natal) + 1;
				r.PorMundoObtido[obtido] = r.PorMundoObtido.GetValueOrDefault(obtido) + 1;
				r.Destino[raca] = obtido;
				r.ZonaDe[raca] = pl.Zone;
				if (!PisouEmChao(pl, out string porque)) r.ForaDoChao.Add($"{raca} em {obtido}: {porque}");

				bool certa = aceitos.Contains(obtido);
				if (certa)
				{
					r.Certas.Add(raca);
					// SAIU DE CASA E ACERTOU -- e o refugio funcionando. Ver `Chamada.Refugiados`:
					// sem esta metade, "verde" tambem seria o nome de "nada aconteceu".
					if (!string.Equals(obtido, natal, StringComparison.OrdinalIgnoreCase))
						r.Refugiados.Add((raca, natal, obtido));
				}
				else r.Desvios.Add((raca, esperado, obtido));

				if (detalhar)
					GD.Print($"[bercoprova]      {raca,-14} esperado {esperado,-28} obtido {obtido,-16}"
						   + $" @ ({pl.Pos.X / ZoneCollision.TileSize:0},{pl.Pos.Y / ZoneCollision.TileSize:0})"
						   + (certa ? "" : "   <<< DESVIADA"));
			}
			finally { Recolher(pl); }
		}

		GD.Print($"[bercoprova]   [{rotulo}] {r.Resumo}");
		return r;
	}

	/// <summary>
	/// **O CONJUNTO DE LUGARES CERTOS PRA UM NATAL** -- a outra ponta da sonda, e ela e escrita A MAO.
	///
	/// ============================ ELA NAO PODE CHAMAR O REFUGIO, E E ISSO QUE A FAZ VALER ============================
	/// Se o esperado saisse de `Refugios.MundosPertoDe` (o codigo de producao), as duas pontas da
	/// sonda teriam o MESMO dono e a bancada mediria a coerencia de um arquivo consigo mesmo -- que e
	/// exatamente o defeito da `--bercovivo` que fez esta bancada existir.
	///
	/// Entao aqui a regra do dono ("o mundo vivo mais perto de casa") e reescrita por um caminho
	/// diferente: **varredura burra do 3x3 de celulas, sem anel, sem parada antecipada e sem top-N
	/// incremental** -- junta tudo, ordena, corta. A producao faz aneis com parada exata e insercao
	/// ordenada numa lista de tres. Duas implementacoes da mesma frase; a bancada e a diferenca.
	///
	/// (O 3x3 e a leitura direta de <see cref="Refugios.CelulasDeBusca"/> = 1. Ele nao esta cravado:
	/// se aquele numero mudar, esta varredura muda junto -- o que se recusa a compartilhar e o
	/// CAMINHO, nao o parametro.)
	/// ==========================================================================================================
	///
	/// A ordem dos desfechos e a mesma da regra, e todos sao alcancaveis pela bancada: o natal vivo,
	/// os vizinhos que servem, a RESERVA (so sobrou mundo pesado) e o ESPACO ABERTO.
	/// </summary>
	private HashSet<string> OndeEsteCorpoDeveriaAcordar(string natal)
	{
		var aceitos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// O NATAL VIVO E A RESPOSTA INTEIRA -- e a ancora que a bancada nunca abre mao (ver as tres
		// linhas de ancora da familia 1).
		if (!ZonaMorta(ZoneKey.Premade(natal))) { aceitos.Add(natal); return aceitos; }

		// A POSICAO DO NATAL, tirada da carta e nao de um `Berco`: Paraiso e Inferno nao sao corpos, e
		// pra eles a ancora e a ORIGEM da carta -- a mesma leitura que o refugio faz de `K < 0`.
		Vec2 ancora = default;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, natal, StringComparison.OrdinalIgnoreCase)) ancora = p.Pos;

		var servem = new List<(string Nome, double D)>();
		var pesados = new List<(string Nome, double D)>();

		SistemaId c0 = SistemaId.De(ancora);
		for (int dy = -Refugios.CelulasDeBusca; dy <= Refugios.CelulasDeBusca; dy++)
			for (int dx = -Refugios.CelulasDeBusca; dx <= Refugios.CelulasDeBusca; dx++)
			{
				if (Sistemas.Do(SeedDoUniverso, c0.Sx + dx, c0.Sy + dy) is not { } s) continue;

				for (int k = 0; k < s.Orbitas; k++)
				{
					PlanetaNoEspaco p = s.Planeta(k);
					if (_mortos.Morto(p)) continue;
					if (p.Premade && _catalogo?.Get(ZoneKey.Premade(p.Nome)) == null) continue;

					double ex = p.Pos.X - ancora.X, ey = p.Pos.Y - ancora.Y;
					(Bercos.ServeDeBerco(p) ? servem : pesados).Add((p.Nome, Math.Sqrt(ex * ex + ey * ey)));
				}
			}

		List<(string Nome, double D)> fonte = servem.Count > 0 ? servem : pesados;
		foreach ((string nome, double _) in fonte.OrderBy(t => t.D).Take(Refugios.MundosGuardados))
			aceitos.Add(nome);

		// NAO SOBROU NADA: o ultimo recurso e o ESPACO ABERTO, e ele e um lugar de verdade -- a zona
		// do universo, de onde se alcanca todos os outros. Ver o bloco do ultimo recurso do refugio.
		if (aceitos.Count == 0) aceitos.Add(Espaco.NomeDoEspaco);

		return aceitos;
	}

	// =====================================================================
	// 1) O MUNDO COMO ELE ESTA -- e as DUAS metades
	// =====================================================================
	/// <summary>
	/// ============================ UMA AFIRMACAO SO FICA VERDE NUM MUNDO SEM NINGUEM ============================
	/// *"ninguem nasceu no lugar errado"* e verdade num servidor em que ninguem nasce, e e exatamente o
	/// modo de falha que a `--povoteste` ja documentou vivendo ("a secao 4 mede o mundo e imprime
	/// `Earth: ` e `Vegeta: ` VAZIOS"). Entao esta familia afirma as DUAS metades, sempre:
	///
	///   NEGATIVA -- nenhuma raca acordou fora do planeta dela (a chamada nominal, zero desvios);
	///   POSITIVA -- todo planeta que e o natal de alguem RECEBEU alguem, e recebeu a conta certa
	///               (`PorMundoObtido[p] == PorMundoEsperado[p]`, planeta por planeta).
	///
	/// A positiva e a que impede "verde por ausencia", e ela e por PLANETA e nao um total: um total
	/// bate mesmo quando dois planetas trocam de povo entre si.
	/// =======================================================================================================
	/// </summary>
	private void OMundoComoEleEsta()
	{
		GD.Print("[bercoprova] -- 1) O MUNDO COMO ELE ESTA: uma linha por raca, e as DUAS metades");

		List<string> racas = ConjuntoDeRacas();
		Provar($"o conjunto de racas veio das duas fontes ({racas.Count} racas), e nao de uma amostra",
			   racas.Count >= 20, string.Join(",", racas));

		// ============================ OS TRES NOMES QUE O DONO ESCREVEU ============================
		// A sonda compara `PlanetaNatal(raca)` com o mundo. Isso pega o desvio -- e nao pegaria alguem
		// "consertando" a bancada pelo lado da tabela: mudar `PlanetaNatal("Human")` pra "Namek"
		// deixaria a chamada nominal inteira verde com todo humano nascendo em Namek.
		//
		// Estas tres linhas sao a ANCORA. Sao os tres unicos nomes escritos a mao neste arquivo, e sao
		// os tres que o dono citou nominalmente nos dois pedidos dele.
		// ========================================================================================
		Provar("ancora: o berco do Human e a Terra", Bercos.PlanetaNatal("Human") == "Earth");
		Provar("ancora: o berco do Namekian e Namek", Bercos.PlanetaNatal("Namekian") == "Namek");
		Provar("ancora: o berco do Saiyan e Vegeta", Bercos.PlanetaNatal("Saiyan") == "Vegeta");

		Chamada c = ChamadaNominal("mundo como esta");

		// --- A METADE NEGATIVA
		Provar($"NEGATIVA: nenhuma das {c.Total} racas acordou fora do planeta dela",
			   c.Desvios.Count == 0, c.Resumo);

		// --- A METADE POSITIVA, planeta por planeta
		foreach ((string planeta, int prometidos) in c.PorMundoEsperado.OrderBy(k => k.Key, StringComparer.Ordinal))
		{
			int chegaram = c.PorMundoObtido.GetValueOrDefault(planeta);
			Provar($"POSITIVA: '{planeta}' e o berco de {prometidos} raca(s) e RECEBEU {chegaram} corpo(s)",
				   chegaram == prometidos && prometidos > 0,
				   $"prometidos {prometidos}, chegaram {chegaram}");
		}

		Provar($"...e os corpos se espalharam por {c.PorMundoObtido.Count} mundos de verdade",
			   c.PorMundoObtido.Count >= 5,
			   string.Join(", ", c.PorMundoObtido.Select(k => $"{k.Key}={k.Value}")));

		// ============================ AS DUAS EXCECOES, NOMINALMENTE ============================
		// A sonda crava a classe em "Normal" e por isso nao ve nenhuma das duas. Elas entram aqui, com
		// nome e esperado proprio -- e a do classe-baixa tem o CONTRA-EXEMPLO colado nela, senao uma
		// regra que mandasse todo Saiyajin pra Terra passaria verde.
		// ====================================================================================
		ProvarUmCorpo("Saiyan", "Saiyan", "Low-Class", "Earth", "o classe-baixa foi despejado na Terra");
		ProvarUmCorpo("Saiyan", "Saiyan", "Elite", "Vegeta", "o classe-alta NAO foi (contra-exemplo)");
		ProvarUmCorpo("Saiyan", "Saiyan", "Normal", "Vegeta", "o classe-normal tambem NAO foi");

		// O LENDARIO NAO TEM PLANETA ESPERADO -- o esperado dele e "longe de Vegeta e nao pre-feito".
		// A `--bercovivo` ja mede o anel; o que interessa aqui e que ele nao caiu no recuo junto com os
		// outros, que e o modo de falha desta bancada.
		var rng = new Random(7);
		CharacterSave lendario = SaveDeBancada("Prova Lendario", "Saiyan", "Saiyan", "Legendary", false, rng);
		Berco bl = BercoDe(lendario);
		Provar($"o Lendario continua exilado ({bl.Planeta}), e nao recolhido pro recuo",
			   bl.Motivo == MotivoDoBerco.ExilioDoLendario && bl.Planeta != "Vegeta",
			   $"{bl.Planeta} / {bl.Motivo}");
	}

	/// <summary>Um corpo, uma classe cravada, um planeta esperado -- pelo funil inteiro.</summary>
	private void ProvarUmCorpo(string raca, string linhagem, string classe, string esperado, string frase)
	{
		CharacterSave c = SaveDeBancada($"Prova {raca} {classe}", raca, linhagem, classe, false, new Random(11));
		ServerPlayer pl = CorpoDoSave(c, FaixaDaProva);
		try
		{
			PousarDeVerdade(pl);
			Provar($"{raca}/{classe}: {frase} -- esperado {esperado}",
				   string.Equals(pl.Zone.Name, esperado, StringComparison.OrdinalIgnoreCase)
				   && PisouEmChao(pl, out _),
				   $"acordou em {pl.Zone.Name}");
		}
		finally { Recolher(pl); }
	}

	// =====================================================================
	// 2) O BERCO MORTO -- e a regra nova, as duas metades
	// =====================================================================
	/// <summary>
	/// ============================ A UNICA PROVA DE QUE UMA BANCADA FUNCIONA E VE-LA REPROVAR ============================
	/// A causa medida na fase 0 e reposta aqui **pelo caminho de producao**: `ComecarDestruicao` +
	/// `TickDaDestruicao` ate a fase virar `Destruido` -- os mesmos ~310 s que a Final Explosion da
	/// `--escudoteste` disparou no mundo do dono. Nao ha atalho escrevendo `FaseDaMorte.Destruido` no
	/// registro: o que se quer reproduzir e o ESTADO que o jogo produz, e nao um estado parecido.
	/// ==============================================================================================================
	///
	/// ============================ O QUE ESTA FAMILIA AFIRMAVA, E POR QUE VIROU O CONTRARIO ============================
	/// Ela exigia **vermelho**: com a Terra morta, *"a chamada nominal FICOU VERMELHA"*, *"o destino
	/// do desvio foi UM so"*. Aquilo era a descricao correta do recuo por lista -- todo mundo ia parar
	/// no mesmo planeta, o segundo da carta, e isso ERA o defeito.
	///
	/// Com o refugio a mesma injecao tem que ficar **VERDE**, e verde por dois motivos ao mesmo tempo,
	/// exigidos juntos (a armadilha aqui e grande: depois que o esperado virou um CONJUNTO, "verde"
	/// tambem seria o nome de "nada aconteceu"):
	///   * NEGATIVA -- ninguem foi parar fora do conjunto certo;
	///   * POSITIVA -- **todo mundo da Terra SAIU da Terra** (`Chamada.Refugiados`), e foi parar num
	///                 mundo perto de casa, e nao no segundo planeta de uma lista.
	///
	/// E a terceira afirmacao e a que enterra a regra velha: **nenhum destino e um pre-feito da
	/// carta**. Nao e gosto -- e geometria: o pre-feito mais proximo da Terra e Makyo_Star, a 68,9 min
	/// de voo, ou 10 celulas de sistema, e a busca do refugio para em 2. Se um corpo aparecer em
	/// Namek de novo, e porque alguem ressuscitou a lista.
	/// ============================================================================================================
	///
	/// E ela e reposta DUAS VEZES, uma por metade -- Terra e depois Namek --, porque o pedido do dono
	/// e simetrico: *quem nasce na Terra nasce na Terra E quem nasce em Namek nasce em Namek*. Sem a
	/// segunda, uma bancada que so soubesse olhar a Terra ficaria verde no dia em que o refugio
	/// quebrasse pro outro lado.
	/// </summary>
	private void ODefeitoDeVolta()
	{
		GD.Print("[bercoprova] -- 2) O BERCO MORTO: o refugio, e nao mais a lista (no palco)");

		string[] daCarta = [.. Espaco.PreFeitos().Select(p => p.Nome)];

		// --- (a) A TERRA MORTA: o mundo exato do relato -----------------------
		//
		// O `using` e ESCRITO A MAO (e nao `using (...) { }`) por um motivo: o `MatouAqui` do palco so
		// existe DEPOIS do `Dispose` -- e ele e o que prova que a injecao chegou a matar alguma coisa.
		// Com o bloco de `using` a variavel morre antes de dar pra ler o numero.
		PalcoDeMortes palco = PalcoDeMortesDeBancada();
		try
		{
			var terra = ZoneKey.Premade("Earth");
			Provar("a injecao MATOU a Terra de verdade (pelo funil de producao)",
				   MatarPlanetaNoPalco(terra, "bercoprova: o berco morto, de volta"),
				   "a Terra nao chegou a `Destruido` -- a injecao nao aconteceu e o resto nao prova nada");

			Chamada c = ChamadaNominal("TERRA MORTA", detalhar: false);
			string[] daTerra = [.. ConjuntoDeRacas().Where(r => Bercos.PlanetaNatal(r) == "Earth")];

			Provar($"NEGATIVA: com a Terra morta, nenhuma das {c.Total} racas foi parar fora do refugio",
				   c.Desvios.Count == 0,
				   "alguem acordou num lugar que a regra do refugio nao aceita -- " + c.Resumo);

			Provar($"POSITIVA: as {daTerra.Length} racas da Terra SAIRAM de casa (e so elas)",
				   c.Refugiados.Count == daTerra.Length && c.Refugiados.All(x => x.Natal == "Earth"),
				   $"{c.Refugiados.Count} refugiadas: "
				   + string.Join(", ", c.Refugiados.Take(6).Select(x => $"{x.Raca}->{x.Obtido}")));

			// A REGRA VELHA ENTERRADA, NOMINALMENTE. Ver o cabecalho: 68,9 min ate o pre-feito mais
			// proximo contra os 13,6 min de teto da busca -- nenhum destino pode ser da carta.
			string[] emPreFeito = [.. c.Refugiados.Select(x => x.Obtido)
										.Where(o => daCarta.Contains(o, StringComparer.OrdinalIgnoreCase))
										.Distinct(StringComparer.Ordinal)];

			Provar("...e NENHUM deles foi parar num pre-feito da carta (a lista morreu)",
				   emPreFeito.Length == 0,
				   "foram parar em " + string.Join(",", emPreFeito) + " -- o recuo por lista voltou");

			Provar("...em particular, NINGUEM foi parar em Namek (o destino da regra velha)",
				   !c.Refugiados.Any(x => string.Equals(x.Obtido, "Namek", StringComparison.OrdinalIgnoreCase)),
				   string.Join(",", c.Refugiados.Select(x => x.Obtido).Distinct(StringComparer.Ordinal)));

			GD.Print($"[bercoprova]      as {c.Refugiados.Count} racas da Terra se espalharam por "
				   + $"{c.Refugiados.Select(x => x.Obtido).Distinct(StringComparer.Ordinal).Count()} "
				   + "mundo(s) da propria estrela da Terra: "
				   + string.Join(", ", c.Refugiados.Select(x => x.Obtido).Distinct(StringComparer.Ordinal)));

			Provar($"...e as racas de Namek/Vegeta continuaram EM CASA ({c.Certas.Count} verdes)",
				   c.Certas.Contains("Namekian") && c.Certas.Contains("Saiyan")
				   && !c.Refugiados.Any(x => x.Natal is "Namek" or "Vegeta"),
				   string.Join(",", c.Refugiados.Select(x => $"{x.Raca}({x.Natal})")));
		}
		finally { palco.Dispose(); }

		Provar($"o palco viu a bancada matar {palco.MatouAqui} planeta ('{palco.NomesQueMorreram}')",
			   palco.MatouAqui == 1 && palco.NomesQueMorreram == "Earth",
			   $"{palco.MatouAqui} planeta(s): '{palco.NomesQueMorreram}' -- um palco que nao ve a arma "
			   + "disparar fica verde pra sempre no dia em que ela parar de disparar");

		Provar("a Terra voltou a existir quando o palco fechou",
			   !ZonaMorta(ZoneKey.Premade("Earth")), "o palco nao desfez a morte");

		Chamada volta = ChamadaNominal("depois do palco", detalhar: false);
		Provar($"...e TODO MUNDO VOLTOU PRA CASA ({volta.Resumo})",
			   volta.Desvios.Count == 0 && volta.Refugiados.Count == 0,
			   "a sonda ficou presa no refugio -- ela estaria medindo a si mesma, e nao o mundo");

		// --- (b) A OUTRA METADE: NAMEK morta ---------------------------------
		using (PalcoDeMortesDeBancada())
		{
			var namek = ZoneKey.Premade("Namek");
			Provar("a injecao MATOU Namek de verdade",
				   MatarPlanetaNoPalco(namek, "bercoprova: a outra metade"), "Namek nao chegou a `Destruido`");

			Chamada c = ChamadaNominal("NAMEK MORTA", detalhar: false);
			string[] deNamek = [.. ConjuntoDeRacas().Where(r => Bercos.PlanetaNatal(r) == "Namek")];

			Provar($"...as {deNamek.Length} racas de NAMEK acharam refugio, e nenhuma se perdeu",
				   c.Desvios.Count == 0
				   && c.Refugiados.Count == deNamek.Length
				   && c.Refugiados.All(x => x.Natal == "Namek"),
				   c.Resumo);

			Provar("...e as racas da TERRA continuaram EM CASA (a metade que nao quebrou)",
				   c.Certas.Contains("Human") && c.Certas.Contains("Majin")
				   && !c.Refugiados.Any(x => x.Natal == "Earth"),
				   c.Resumo);

			// ============================ A ASSIMETRIA MORREU, E ISTO E O REGISTRO ============================
			// Aqui estava escrito: *"com Namek morta o desvio vai pra 'Earth' -- aqui o recuo nem desce
			// a carta: a `SpawnZone` esta viva"*. Era verdade e era o retrato do defeito: o destino de
			// quem perdia Namek dependia de a TERRA estar viva, ou seja, de um planeta que nao tem nada
			// a ver com Namek. Agora nao depende: a ancora e o proprio natal, e a Terra esta a 167 min
			// de Namek -- longe demais pra a busca do refugio sequer olhar.
			// ==============================================================================================
			Provar("...e NENHUMA delas foi parar na Terra (a assimetria da `SpawnZone` morreu)",
				   !c.Refugiados.Any(x => string.Equals(x.Obtido, "Earth", StringComparison.OrdinalIgnoreCase)),
				   string.Join(",", c.Refugiados.Select(x => $"{x.Raca}->{x.Obtido}")));

			GD.Print("[bercoprova]      com Namek morta o refugio fica na estrela de NAMEK: "
				   + string.Join(", ", c.Refugiados.Select(x => x.Obtido).Distinct(StringComparer.Ordinal)));
		}

		Provar("Namek voltou a existir quando o palco fechou", !ZonaMorta(ZoneKey.Premade("Namek")));
	}

	/// <summary>
	/// MATA UM PLANETA PELO FUNIL DE PRODUCAO e espera o commit. So dentro de um
	/// <see cref="PalcoDeMortes"/> -- fora dele isto escreveria no `planetas-mortos.json` do dono, que
	/// e literalmente o defeito que originou este trabalho.
	/// </summary>
	private bool MatarPlanetaNoPalco(ZoneKey zona, string motivo)
	{
		if (!_mortesDeBancada)
		{
			GD.PushError("[bercoprova] recusei matar um planeta FORA do palco de bancada");
			return false;
		}

		ComecarDestruicao(zona, 1e12, motivo);

		// Um segundo por volta, sem atalho -- e o mesmo laco que a `--planetateste` ja usa
		// (`GameServer.DestruicaoTeste.cs:186`). A folga de 20 s cobre o arredondamento do prazo.
		for (int i = 0; i < (int)MortePlanetaria.SegundosDeExplosao + 20 && !ZonaMorta(zona); i++)
			TickDaDestruicao(1);

		return ZonaMorta(zona);
	}

	// =====================================================================
	// 3) A ORDEM DA CARTA NAO IMPORTA MAIS
	// =====================================================================
	/// <summary>
	/// ============================ ESTA FAMILIA E A INVERSAO LITERAL DA ANTIGA ============================
	/// Ela se chamava *"A ORDEM DA CARTA -- a fragilidade e de LISTA"* e provava, com todas as letras,
	/// que **o destino de quem perdia o berco era uma posicao numa lista**:
	///
	///   * matando os primeiros da carta em ordem, o destino ANDAVA -- Namek, depois Vegeta, depois
	///     Icer --, e a afirmacao era `caminhados.Distinct().Count() == caminhados.Count`;
	///   * enfiando um planeta novo ("Hera") na FRENTE de `Espaco.PreFeitos()` por um campo trocavel
	///     (`_cartaDeRecuo`), o destino mudava de planeta sem mais nada mudar.
	///
	/// **Aquilo era o retrato do defeito, e o defeito foi deletado.** As duas injecoes deixaram de ter
	/// objeto: o campo `_cartaDeRecuo` morreu junto com o `ZonaDeRecuoViva`, e uma injecao sem objeto
	/// nao fica vermelha nunca mais -- ela fica **verde para sempre**, que e o modo de falha que este
	/// projeto ja catalogou (afirmacao verde num sistema morto).
	///
	/// Entao a familia foi virada do avesso e mede o OPOSTO, com a mesma arma (matar planetas na ordem
	/// da carta, pelo funil de producao):
	///
	///   (a) matar Earth -> Namek -> Vegeta **nao move** o destino de quem e da Terra. A afirmacao e
	///       `Distinct().Count() == 1` -- a negacao exata da antiga;
	///   (b) matar o que esta PERTO move: com a estrela da Terra inteira destruida, o refugio anda pro
	///       anel seguinte. Sem esta metade, (a) ficaria verde num refugio que nunca mudasse de ideia;
	///   (c) e o corte dos <see cref="Refugios.MundosGuardados"/> DISPARA de verdade -- ha mais mundos
	///       vivos ao alcance do que os que sao oferecidos, e o destino esta sempre entre os mais
	///       proximos. Teto que nunca dispara e indistinguivel de teto nenhum.
	/// ==================================================================================================
	/// </summary>
	private void AOrdemDaCartaNaoImportaMais()
	{
		GD.Print("[bercoprova] -- 3) A ORDEM DA CARTA NAO IMPORTA MAIS: o destino e distancia, nao posicao");

		string[] ordem = [.. Espaco.PreFeitos().Select(p => p.Nome)];
		GD.Print($"[bercoprova]      a carta, na ordem: {string.Join(" -> ", ordem)}");
		Provar("a carta estelar CONTINUA tendo uma ordem observavel (a Terra e a primeira)",
			   ordem.Length >= 3 && ordem[0] == "Earth", string.Join(",", ordem));

		// --- (a) MATAR A FRENTE DA CARTA NAO MOVE O DESTINO -------------------
		using (PalcoDeMortesDeBancada())
		{
			var caminhados = new List<string>();

			foreach (string vitima in ordem.Take(3))
			{
				if (!MatarPlanetaNoPalco(ZoneKey.Premade(vitima), $"bercoprova: ordem, matando {vitima}"))
				{
					Provar($"a injecao matou '{vitima}'", false, "nao chegou a `Destruido`");
					break;
				}

				Chamada c = ChamadaNominal($"mortos ate {vitima}", detalhar: false);

				// O DESTINO DE QUEM E DA TERRA, como CONJUNTO: sao 10 racas e cada uma sorteia a sua
				// irma, entao "o destino" nunca foi um nome so -- o que tem que ficar parado e o
				// conjunto de mundos que a estrela da Terra oferece.
				string destino = string.Join("+", c.Refugiados.Where(x => x.Natal == "Earth")
													.Select(x => x.Obtido)
													.Distinct(StringComparer.Ordinal)
													.OrderBy(x => x, StringComparer.Ordinal));
				caminhados.Add(destino);

				Provar($"com '{vitima}' e os anteriores mortos, quem e da Terra continua achando refugio"
					   + $" ('{destino}')",
					   c.Desvios.Count == 0 && destino.Length > 0, c.Resumo);
			}

			// **A LINHA QUE INVERTE A FAMILIA.** A antiga exigia `Distinct().Count() == caminhados.Count`
			// ("o destino ANDOU pela carta") e o texto de falha dizia *"os destinos se repetiram -- a
			// fragilidade nao e de ordem"*. Hoje a repeticao E o resultado desejado.
			Provar($"o destino NAO ANDOU com a carta ({string.Join(" | ", caminhados)})",
				   caminhados.Count >= 2 && caminhados.Distinct(StringComparer.Ordinal).Count() == 1,
				   "matar Namek/Vegeta mexeu em quem e da TERRA -- o destino voltou a depender da "
				   + "ordem de `Espaco.PreFeitos()`, e a regra do refugio nao esta valendo");
		}

		// --- (b) MATAR O QUE ESTA PERTO, ESSE SIM, MOVE -----------------------
		//
		// Sem esta metade, (a) ficaria verde num refugio que devolvesse sempre a mesma coisa aconteca o
		// que acontecer -- inclusive num refugio quebrado que ignorasse o estado do mundo.
		SistemaSolar estrelaDaTerra = default;
		foreach (SistemaSolar s in Sistemas.ComPreFeito)
			if (s.PreFeito.Nome == "Earth") estrelaDaTerra = s;

		using (PalcoDeMortesDeBancada())
		{
			Provar("a injecao matou a Terra (rodada da vizinhanca)",
				   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: vizinhanca, a Terra"));

			Chamada antes = ChamadaNominal("Terra morta, estrela intacta", detalhar: false);
			string[] naEstrela = [.. antes.Refugiados.Where(x => x.Natal == "Earth")
									   .Select(x => x.Obtido).Distinct(StringComparer.Ordinal)];

			// AS IRMAS DE ORBITA DA TERRA, todas -- inclusive a pesada que o crivo de gravidade
			// reprovaria. A reserva do refugio pega essa, entao deixa-la viva nao esvaziaria o anel 0.
			var mortas = new List<string>();
			for (int k = 0; k < estrelaDaTerra.Orbitas; k++)
			{
				if (k == estrelaDaTerra.OrbitaPreFeita) continue;
				PlanetaNoEspaco irma = estrelaDaTerra.Planeta(k);
				if (MatarPlanetaNoPalco(ZoneKey.Procedural(irma.Nome, irma.Seed),
										$"bercoprova: vizinhanca, {irma.Nome}"))
					mortas.Add(irma.Nome);
			}

			Provar($"a injecao matou a ESTRELA DA TERRA inteira ({mortas.Count} irmas: {string.Join(",", mortas)})",
				   mortas.Count == estrelaDaTerra.Orbitas - 1,
				   "sem matar todas as irmas o anel 0 continua servindo e esta metade nao prova nada");

			Chamada depois = ChamadaNominal("estrela da Terra inteira morta", detalhar: false);
			string[] longe = [.. depois.Refugiados.Where(x => x.Natal == "Earth")
									 .Select(x => x.Obtido).Distinct(StringComparer.Ordinal)];

			// ============================ E ELE NAO E "TODO O CONJUNTO MUDOU" -- MEDIDO ============================
			// A primeira versao desta afirmacao exigia intersecao VAZIA entre o antes e o depois, e ela
			// ficou vermelha na primeira rodada -- **e o codigo estava certo**. A Terra tem 3 irmas de
			// orbita e uma delas (`Alienigena-002`, 30 g) e reprovada pelo crivo de gravidade, entao o
			// terceiro lugar do sorteio JA ERA de um mundo do anel 1 antes de qualquer injecao. Esse
			// mundo continua sendo o terceiro mais perto depois -- ele nao tinha por que sair.
			//
			// O que a metade (b) mede de verdade e outra coisa, e e mais exata: **nenhuma irma da
			// estrela da Terra sobrou no sorteio, e o conjunto mudou.** Exigir troca total seria exigir
			// do refugio uma reacao que a geometria nao pede.
			// ==================================================================================================
			Provar($"...e agora NENHUMA irma da estrela da Terra esta no sorteio ({string.Join(",", longe)})",
				   longe.Length > 0 && !longe.Intersect(mortas, StringComparer.Ordinal).Any(),
				   $"sobrou irma morta no sorteio: {string.Join(",", longe.Intersect(mortas, StringComparer.Ordinal))}");

			Provar("...e o conjunto de destinos MUDOU (o refugio reagiu ao mundo)",
				   !longe.OrderBy(x => x, StringComparer.Ordinal)
						 .SequenceEqual(naEstrela.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal),
				   $"antes {string.Join(",", naEstrela)} / depois {string.Join(",", longe)} -- "
				   + "o refugio nao reagiu a vizinhanca morrer, entao ele nao esta olhando o mundo");

			Provar($"...e ninguem se perdeu na mudanca ({depois.Resumo})", depois.Desvios.Count == 0,
				   "alguem acordou fora do conjunto certo quando a busca teve que crescer");

			Provar("...e continua NAO sendo um pre-feito da carta",
				   !longe.Any(o => ordem.Contains(o, StringComparer.OrdinalIgnoreCase)),
				   string.Join(",", longe));
		}

		// --- (c) O CORTE DOS TRES MUNDOS DISPARA DE VERDADE -------------------
		using (PalcoDeMortesDeBancada())
		{
			Provar("a injecao matou a Terra (rodada do corte)",
				   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: o corte dos tres"));

			// TODOS os mundos vivos ao alcance, contados A MAO -- e nao os tres que a producao guarda.
			int aoAlcance = 0;
			SistemaId c0 = SistemaId.De(estrelaDaTerra.PreFeito.Pos);
			for (int dy = -Refugios.CelulasDeBusca; dy <= Refugios.CelulasDeBusca; dy++)
				for (int dx = -Refugios.CelulasDeBusca; dx <= Refugios.CelulasDeBusca; dx++)
				{
					if (Sistemas.Do(SeedDoUniverso, c0.Sx + dx, c0.Sy + dy) is not { } s) continue;
					for (int k = 0; k < s.Orbitas; k++)
					{
						PlanetaNoEspaco p = s.Planeta(k);
						if (!_mortos.Morto(p) && Bercos.ServeDeBerco(p)) aoAlcance++;
					}
				}

			HashSet<string> oferecidos = OndeEsteCorpoDeveriaAcordar("Earth");

			Provar($"o corte de {Refugios.MundosGuardados} mundos DISPARA: ha {aoAlcance} mundos vivos "
				 + $"ao alcance e so {oferecidos.Count} entram no sorteio",
				   aoAlcance > Refugios.MundosGuardados && oferecidos.Count <= Refugios.MundosGuardados,
				   $"{aoAlcance} ao alcance, {oferecidos.Count} oferecidos -- um corte que nunca morde "
				   + "e indistinguivel de corte nenhum");

			Chamada c = ChamadaNominal("com o corte medido", detalhar: false);
			Provar("...e todo destino de verdade esta dentro desse corte",
				   c.Refugiados.Where(x => x.Natal == "Earth").All(x => oferecidos.Contains(x.Obtido)),
				   string.Join(",", c.Refugiados.Where(x => x.Natal == "Earth").Select(x => x.Obtido)));
		}

		// --- (d) DE ONDE SE MEDE "PERTO": O BERCO MAGRO DO NPC ----------------
		//
		// ============================ O DEFEITO QUE ESTA METADE FECHA, MEDIDO ============================
		// **Nem todo `Berco` deste jogo vem do `Bercos.Onde`.** O `GameServer.Npc.cs` monta um a mao com
		// TRES campos (`Planeta`, `Natal`, `PreFeito`) -- e o comentario dele explica por que --, entao
		// `Pos` fica em `(0,0)`. E `(0,0)` **e exatamente onde a Terra esta**.
		//
		// Um refugio que medisse a distancia a partir de `b.Pos` mandaria um cidadao de NAMEK, cujo
		// planeta acabou de explodir, procurar abrigo na vizinhanca da TERRA -- calado, plausivel, e
		// errado. Por isso a ancora sai do NOME do natal na carta (`AncoraDoRefugio`) e nao do campo.
		// A prova e nominal: um berco magro de Namek tem que achar refugio na estrela de Namek.
		// ============================================================================================
		using (PalcoDeMortesDeBancada())
		{
			Provar("a injecao matou Namek (rodada do berco magro)",
				   MatarPlanetaNoPalco(ZoneKey.Premade("Namek"), "bercoprova: berco magro"));

			// O BERCO EXATAMENTE COMO O `NascerNpc` O ESCREVE -- tres campos, `Pos` em (0,0).
			var magro = new Berco { Planeta = "Namek", Natal = "Namek", PreFeito = true };
			Provar("o berco magro tem MESMO `Pos` em (0,0) -- onde a Terra fica",
				   Math.Abs(magro.Pos.X) < 1 && Math.Abs(magro.Pos.Y) < 1,
				   "o `Berco` mudou de forma e esta prova perdeu o objeto");

			HashSet<string> deNamek = OndeEsteCorpoDeveriaAcordar("Namek");
			HashSet<string> daTerra = OndeEsteCorpoDeveriaAcordar("Earth");

			// UM CORPO DE VERDADE, E POUSADO. `DestinoDoBerco` sozinho devolveria "Espaco" e a prova
			// ficaria vermelha por medir cedo demais: um refugio GERADO nasce em ORBITA de proposito
			// (ver o comentario do `PousarNo`), e quem faz o mundo existir e o `TickDoEspaco`. Foi
			// exatamente assim que esta linha reprovou na primeira rodada -- e o codigo estava certo.
			CharacterSave cm = SaveDeBancada("Prova Magro", "Namekian", "Namekian", "Normal",
											 false, new Random(5));
			ServerPlayer plm = CorpoDoSave(cm, FaixaDaProva);
			try
			{
				PousarDeVerdade(plm);
				plm.Berco = magro;        // o berco do NPC, por cima do que o save calculou
				MandarProBerco(plm);
				PousarDeVerdade(plm);

				Provar($"...e mesmo assim ele acha refugio na estrela de NAMEK ('{plm.Zone.Name}')",
					   deNamek.Contains(plm.Zone.Name),
					   $"foi parar em {plm.Zone.Name}, que nao e vizinho de Namek -- a ancora voltou a "
					   + "sair do campo `Pos` em vez do nome do natal");

				Provar("...e NAO na vizinhanca da Terra (que e onde o (0,0) fica)",
					   !daTerra.Contains(plm.Zone.Name), plm.Zone.Name);
			}
			finally { Recolher(plm); }
		}
	}

	// =====================================================================
	// 4) RENASCER E O OUTRO CAMINHO
	// =====================================================================
	/// <summary>
	/// ============================ SAO DOIS CAMINHOS, E O CONSERTO PELA METADE E INVISIVEL ============================
	/// Nascer passa por `CreateChar` -> `AplicarBercoNoSave` -> `Entrar`. Renascer passa por
	/// `Renascer` -> `DestinoDe` -> `MandarProBerco` -> `MoveToZone` (`GameServer.Combat.cs:1597`). O
	/// funil e o mesmo (`DestinoDoBerco`), mas o TRECHO entre a morte e o corpo no chao e so do
	/// segundo -- e um conserto que ligasse o berco so no nascimento faria o jogador nascer em casa e
	/// ressuscitar noutro planeta pra sempre, sem uma linha de erro.
	///
	/// Por isso a prova e por RACA e nao por amostra: foi uma amostra ("Human, Saiyan, Icer,
	/// Namekian" -- os oito perfis da `--bercovivo`) que deixou o resto passar.
	/// ============================================================================================================
	///
	/// ============================ E AQUI ESTA A INVERSAO CENTRAL DA REGRA NOVA ============================
	/// Esta familia exigia, com a Terra morta: *"...e o RENASCIMENTO tambem ficou vermelho"*, com o
	/// texto de falha *"o renascimento nao viu o defeito"*. **Depois do refugio, vermelho aqui e que
	/// seria o defeito**: quem perdeu o planeta natal TEM que renascer noutro lugar, e esse outro
	/// lugar e a resposta certa.
	///
	/// O que a familia guarda e o que ela sempre mediu de verdade: que o renascimento e um caminho
	/// SEPARADO do nascimento (`DestinoDe` -> `MandarProBerco`, com o dominio no meio), e que ele
	/// chega no mesmo lugar. A afirmacao virou "renasceu no refugio, e no mesmo refugio", e a metade
	/// nova (`SaiuDeCasa`) impede que "verde" volte a significar "nada aconteceu".
	/// ==================================================================================================
	/// </summary>
	private void RenascerEhOOutroCaminho()
	{
		GD.Print("[bercoprova] -- 4) RENASCER: o outro caminho, raca por raca");

		(int certas, List<string> erradas, List<string> saiu) sadio = RodadaDeRenascimento("mundo como esta");
		Provar($"todas as {sadio.certas} racas RENASCERAM no planeta delas",
			   sadio.erradas.Count == 0 && sadio.saiu.Count == 0,
			   string.Join(" | ", sadio.erradas.Take(6)));

		using (PalcoDeMortesDeBancada())
		{
			Provar("a injecao matou a Terra (rodada do renascimento)",
				   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: renascimento"));

			(int certas, List<string> erradas, List<string> saiu) doente = RodadaDeRenascimento("TERRA MORTA");

			Provar($"...e o RENASCIMENTO achou o refugio ({doente.certas} certas, "
				 + $"{doente.erradas.Count} perdidas)",
				   doente.erradas.Count == 0,
				   "alguem RENASCEU fora do conjunto certo -- e este e o caminho que o jogador usa "
				   + "mais: " + string.Join(" | ", doente.erradas.Take(6)));

			Provar($"...e as {doente.saiu.Count} racas da Terra renasceram FORA de casa, e so elas",
				   doente.saiu.Count == ConjuntoDeRacas().Count(r => Bercos.PlanetaNatal(r) == "Earth"),
				   "se ninguem saiu de casa, a rodada ficou verde por imobilidade: "
				   + string.Join(" | ", doente.saiu.Take(6)));
		}
	}

	/// <summary>
	/// Uma volta completa por raca: nasce -> pousa -> **e levado pra longe** -> `Renascer` -> pousa.
	///
	/// O desvio pra longe nao e enfeite: sem ele o `Renascer` cai no ramo "mesma zona" e nunca chega
	/// no `MandarProBerco`, que e justamente o pedaco que so a morte usa.
	///
	/// `SaiuDeCasa` e a metade POSITIVA -- ver `Chamada.Refugiados` pro argumento: com o esperado
	/// virando um conjunto, "nenhuma errada" tambem e o placar de um renascimento que nao fez nada.
	/// </summary>
	private (int Certas, List<string> Erradas, List<string> SaiuDeCasa) RodadaDeRenascimento(string rotulo)
	{
		var rng = new Random(31337);
		var erradas = new List<string>();
		var saiu = new List<string>();
		int certas = 0;

		foreach (string raca in ConjuntoDeRacas())
		{
			string[] escolhas = CharacterDraft.EscolhasDeClasse(raca);
			CharacterSave c = SaveDeBancada($"Renasce {raca}", raca,
				escolhas.Length > 0 ? escolhas[0] : "", "Normal", false, rng);

			string natal = Bercos.PlanetaNatal(raca);
			HashSet<string> aceitos = OndeEsteCorpoDeveriaAcordar(natal);
			ServerPlayer pl = CorpoDoSave(c, FaixaDaProva);
			try
			{
				PousarDeVerdade(pl);

				// PRA LONGE: Makyo_Star e um pre-feito que nao e o natal de quase ninguem, entao ela
				// serve de "outro lugar" pra praticamente toda raca. Pro Makyo, a Terra.
				var longe = ZoneKey.Premade(raca == "Makyo" ? "Earth" : "Makyo_Star");
				MoveToZone(pl.Id, longe, PontoDeNascimento(longe));

				Renascer(pl);
				PousarDeVerdade(pl);

				if (!aceitos.Contains(pl.Zone.Name))
				{
					erradas.Add($"{raca}: renasceu em {pl.Zone.Name}, esperado "
							  + string.Join("|", aceitos.OrderBy(x => x, StringComparer.Ordinal)));
					continue;
				}

				certas++;
				if (!string.Equals(pl.Zone.Name, natal, StringComparison.OrdinalIgnoreCase))
					saiu.Add($"{raca}: {natal} -> {pl.Zone.Name}");
			}
			finally { Recolher(pl); }
		}

		GD.Print($"[bercoprova]   [{rotulo}] renascimento: {certas} certas, {erradas.Count} erradas, "
			   + $"{saiu.Count} no refugio");
		return (certas, erradas, saiu);
	}

	// =====================================================================
	// 5) O POVO -- no MUNDO, e nao na tabela
	// =====================================================================
	/// <summary>
	/// ============================ A `--povoteste` DESVIOU DA EVIDENCIA, E ESCREVEU ISSO ============================
	/// *"na TERRA deveria so spawnar HUMANO e em NAMEK so NAMEKUSEIJIN e no planeta VEGETA so
	/// SAIYAJIN"*. A `--povoteste` prova isso medindo `Bercos.RacasNascidasEm` e
	/// `SorteioDeNpc.Sortear` -- funcoes puras --, e o comentario dela explica por que nao mede o
	/// mundo (`GameServer.PovoamentoTeste.cs:376`): *"porque o mundo daria VERDE POR AUSENCIA nos dois
	/// planetas que o dono citou... no save vivo, `planetas-mortos.json` tem Vegeta E TERRA
	/// condenados"*.
	///
	/// Aquele paragrafo e a descricao exata do defeito do relato, escrita meses antes, tratada como
	/// paisagem. Esta familia faz o contrario: ela roda `TickDoPovoamento` -- o codigo de producao, o
	/// mesmo que o `_Process` chama -- e CONTA CORPOS. Um planeta com zero habitantes e VERMELHO aqui,
	/// e e assim que "verde por ausencia" deixa de ser possivel.
	/// ==========================================================================================================
	///
	/// A afirmacao e a do dono, nos dois sentidos: a Terra tem que ter humanos (>0) e **so** humanos.
	/// Nos tres planetas que ele nao citou o teste e mais frouxo de proposito ("no RESTO PODE MANTER"):
	/// so exige que tenham gente, e que a gente que tem tenha berco ali.
	/// </summary>
	private void OPovoNoMundoENaoNaTabela()
	{
		GD.Print("[bercoprova] -- 5) O POVO: contado no MUNDO, nao lido da tabela");

		if (_moldes == null) { Provar("ha moldes de NPC carregados", false, "sem npcs.json"); return; }

		// --- (a) O MUNDO SADIO ------------------------------------------------
		Dictionary<string, Dictionary<string, int>> censo = PovoarEContar();

		foreach ((string planeta, string povo) in Bercos.Povos)
		{
			Dictionary<string, int> daZona = censo.GetValueOrDefault(planeta) ?? [];
			int total = daZona.Values.Sum();
			int doPovo = daZona.GetValueOrDefault(povo);

			Provar($"'{planeta}' TEM habitantes ({total}) -- sem isto o resto e verde por ausencia",
				   total > 0, $"{planeta} nasceu VAZIO");
			Provar($"'{planeta}': os {total} habitantes sao TODOS '{povo}' ({doPovo})",
				   total > 0 && doPovo == total,
				   string.Join(", ", daZona.Select(k => $"{k.Key}={k.Value}")));
		}

		// OS TRES QUE O DONO NAO CITOU -- "no RESTO PODE MANTER". Aqui a exigencia e outra: gente
		// existe, e quem existe tem berco ali. Cravar uma raca seria escrever uma regra que o dono
		// nao pediu.
		foreach (LinhaDePovoamento linha in _moldes.Plano)
		{
			if (Bercos.PovoDoPlaneta(linha.Planeta).Length > 0) continue;   // ja conferido acima

			Dictionary<string, int> daZona = censo.GetValueOrDefault(linha.Planeta) ?? [];
			int total = daZona.Values.Sum();
			bool doBerco = daZona.Keys.All(r =>
				string.Equals(Bercos.PlanetaNatal(r), linha.Planeta, StringComparison.OrdinalIgnoreCase));

			Provar($"'{linha.Planeta}' (o RESTO): tem {total} habitante(s) e todos tem berco la",
				   total > 0 && doBerco, string.Join(", ", daZona.Select(k => $"{k.Key}={k.Value}")));
		}

		// --- (b) O DEFEITO DE VOLTA, no caminho do POVOAMENTO -----------------
		// E um caminho DIFERENTE do berco do jogador: quem recusa aqui e a `Manutencao`, pela
		// condenacao (`GameServer.Povoamento.cs:208`), e nao o `DestinoDoBerco`. Dois caminhos, duas
		// provas -- senao o proximo defeito mora no que ninguem mede.
		using (PalcoDeMortesDeBancada())
		{
			Provar("a injecao matou a Terra (rodada do povoamento)",
				   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: povoamento"));

			Dictionary<string, Dictionary<string, int>> doente = PovoarEContar();
			int naTerra = (doente.GetValueOrDefault("Earth") ?? []).Values.Sum();

			Provar($"...e a Terra nasceu VAZIA ({naTerra} habitantes) -- a bancada VE isso",
				   naTerra == 0, $"{naTerra} habitantes numa Terra destruida");

			int emNamek = (doente.GetValueOrDefault("Namek") ?? []).Values.Sum();
			Provar($"...enquanto Namek continuou povoada ({emNamek}) -- a metade que nao quebrou",
				   emNamek > 0, $"{emNamek}");

			// A TABELA CONTINUA DIZENDO QUE ESTA TUDO BEM -- e este e o ponto da familia 6.
			Provar("...e a FUNCAO pura continua respondendo '[Human]' pra Terra (verde por ausencia)",
				   Bercos.RacasNascidasEm("Earth", _racas!.Protos.Keys) is ["Human"],
				   "a funcao mudou -- entao a `--povoteste` teria pego, e este diagnostico esta errado");
		}
	}

	/// <summary>
	/// POVOA O MUNDO PELO CAMINHO DE PRODUCAO e devolve o censo por (zona, raca). Depois **desfaz**:
	/// os corpos saem, a fila esvazia e os contadores voltam ao que estavam.
	///
	/// O unico privilegio e a CADENCIA -- o relogio da manutencao e adiantado pra ela rodar agora, em
	/// vez de nos 5 min do `Povoamento.SegundosEntreManutencoes`. O resto (a `Manutencao`, o teto, o
	/// dreno, o `NascerNpc`, o `SorteioDeNpc`) e o codigo que o `_Process` chama.
	/// </summary>
	private Dictionary<string, Dictionary<string, int>> PovoarEContar()
	{
		var antes = new HashSet<int>(_players.Keys);

		double relogio = _relogioDoPovoamento, proxima = _proximaManutencao;
		var lugares = new Dictionary<string, ulong>(_lugaresDoPovoamento, StringComparer.Ordinal);
		var fila = new List<(string, ZoneKey, ulong)>(_filaDoPovoamento);

		var censo = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
		var nascidos = new List<ServerPlayer>();

		try
		{
			// AGORA, e nao daqui a cinco minutos. A primeira volta da `Manutencao` E o povoamento
			// inicial (nao ha caminho a parte -- ver `GameServer.Povoamento.cs:159`).
			_relogioDoPovoamento = 0;
			_proximaManutencao = 0;

			// 1/30 s por volta e o dt do servidor. O dreno nasce `NascimentosPorTique` por tique, e o
			// plano de producao tem 148 vagas -- 400 voltas cobrem com folga e o teto e uma FALHA
			// visivel (a fila sobrando) e nao um travamento.
			for (int t = 0; t < 400 && (t == 0 || _filaDoPovoamento.Count > 0); t++)
				TickDoPovoamento(1.0 / 30.0);

			foreach (ServerPlayer p in _players.Values)
			{
				if (antes.Contains(p.Id) || !EhNpcDoMundo(p) || p.Ficha.dead) continue;
				nascidos.Add(p);

				if (!censo.TryGetValue(p.Zone.Name, out Dictionary<string, int>? porRaca))
					censo[p.Zone.Name] = porRaca = new Dictionary<string, int>(StringComparer.Ordinal);
				porRaca[p.Race] = porRaca.GetValueOrDefault(p.Race) + 1;
			}

			foreach ((string zona, Dictionary<string, int> porRaca) in censo.OrderBy(k => k.Key, StringComparer.Ordinal))
				GD.Print($"[bercoprova]      censo {zona,-14} {porRaca.Values.Sum(),3} habitantes: "
					   + string.Join(", ", porRaca.Select(k => $"{k.Key}={k.Value}")));

			return censo;
		}
		finally
		{
			// OS CORPOS SAEM. Um vilarejo esquecido aqui viraria 148 NPCs vivos num servidor que o dono
			// vai abrir logo depois -- e eles seriam indistinguiveis dos de verdade.
			foreach (ServerPlayer p in nascidos) if (_players.ContainsKey(p.Id)) RemoverNpc(p);

			_filaDoPovoamento.Clear();
			foreach ((string, ZoneKey, ulong) p in fila) _filaDoPovoamento.Enqueue(p);

			_lugaresDoPovoamento.Clear();
			foreach ((string k, ulong v) in lugares) _lugaresDoPovoamento[k] = v;

			_relogioDoPovoamento = relogio;
			_proximaManutencao = proxima;
		}
	}

	// =====================================================================
	// 6) POR QUE NENHUMA BANCADA PEGOU
	// =====================================================================
	/// <summary>
	/// **A RESPOSTA A PERGUNTA, MEDIDA E NAO ARGUMENTADA.**
	///
	/// Cada uma das tres cegueiras e reescrita aqui como a bancada antiga a escrevia, e rodada no
	/// mundo ESTRAGADO. As tres tem que ficar **VERDES** -- e e a verdice delas que prova a cegueira.
	/// Se alguma ficasse vermelha, a bancada antiga teria pego o defeito e este diagnostico estaria
	/// errado; por isso as afirmacoes abaixo sao `== true` sobre a checagem antiga, e nao sobre o
	/// mundo.
	/// </summary>
	private void PorQueNenhumaBancadaPegou()
	{
		GD.Print("[bercoprova] -- 6) POR QUE NENHUMA BANCADA PEGOU (as tres cegueiras, medidas)");

		using PalcoDeMortes _ = PalcoDeMortesDeBancada();

		Provar("a injecao matou a Terra (rodada das cegueiras)",
			   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: as tres cegueiras"));

		Chamada c = ChamadaNominal("mundo estragado", detalhar: false);

		// ============================ "ESTRAGADO" MUDOU DE NOME, E NAO DE FATO ============================
		// Era `c.Desvios.Count > 0` -- "gente no lugar errado". Com o refugio ninguem esta no lugar
		// errado: as racas da Terra estao no lugar CERTO, que agora e outro. O que a familia 6 precisa
		// e que o mundo esteja MEXIDO -- gente longe de casa --, porque e isso que as tres cegueiras
		// deixavam passar. `Refugiados` e a mesma medida de antes lida pela regra de hoje.
		// =============================================================================================
		Provar($"o mundo esta MESMO mexido ({c.Refugiados.Count} racas longe de casa)",
			   c.Refugiados.Count > 0);

		// ---- CEGUEIRA 1: A INTENCAO NAO E O EFEITO -------------------------
		// A checagem antiga, literal (`GameServer.BercoVivoTeste.cs`, antes do conserto):
		//     Afirmar("o berco e o natal da raca", b.Planeta == natal || excecao)
		// `b.Planeta` sai de `Bercos.Onde`, funcao PURA -- ela nao sabe que a Terra morreu.
		var rng = new Random(20260816);
		int intencaoCerta = 0;
		foreach (string raca in ConjuntoDeRacas())
		{
			string[] escolhas = CharacterDraft.EscolhasDeClasse(raca);
			CharacterSave c2 = SaveDeBancada($"Cego {raca}", raca,
				escolhas.Length > 0 ? escolhas[0] : "", "Normal", false, rng);
			if (BercoDe(c2).Planeta == Bercos.PlanetaNatal(raca)) intencaoCerta++;
		}

		Provar($"CEGUEIRA 1: a checagem por INTENCAO fica VERDE no mundo estragado "
			 + $"({intencaoCerta}/{ConjuntoDeRacas().Count} racas)",
			   intencaoCerta == ConjuntoDeRacas().Count,
			   "a funcao pura mudou de resposta -- entao a checagem antiga teria pego, e o "
			   + "diagnostico esta errado");

		// ---- CEGUEIRA 2: MEDIR A FUNCAO EM VEZ DO MUNDO --------------------
		bool funcaoAindaDiz = Bercos.RacasNascidasEm("Earth", _racas!.Protos.Keys) is ["Human"]
						   && Bercos.ContradicoesDoPovo(_racas.Protos.Keys).Count == 0;

		Provar("CEGUEIRA 2: a checagem da `--povoteste` (funcao pura) fica VERDE com a Terra morta",
			   funcaoAindaDiz,
			   "as tabelas mudaram -- entao a `--povoteste` teria pego, e o diagnostico esta errado");

		// ---- CEGUEIRA 3: O AGREGADO ESCONDE O NOME -------------------------
		// E o teste mais duro dos tres, porque estes dois agregados sao medidos aqui **no EFEITO** (o
		// mundo estragado, `PorMundoObtido`) e mesmo assim passam.
		int mundos = c.PorMundoObtido.Count;
		int maiorGrupo = c.PorMundoObtido.Values.Max();

		Provar($"CEGUEIRA 3a: 'as racas se espalham por >= 5 mundos' fica VERDE ({mundos} mundos)",
			   mundos >= 5, $"{mundos}");
		Provar($"CEGUEIRA 3b: 'nenhum mundo unico recebe TODAS as racas' fica VERDE "
			 + $"(maior grupo {maiorGrupo} de {c.Total})",
			   maiorGrupo < c.Total, $"{maiorGrupo}/{c.Total}");

		GD.Print($"[bercoprova]      => as tres cegueiras passam no mundo em que {c.Refugiados.Count} racas "
			   + "acordam longe de casa. O que corta e a afirmacao NOMINAL: uma linha por raca, com o "
			   + "conjunto de lugares certos e o obtido.");
	}

	// =====================================================================
	// 7) A ESCOLHA -- o dominio, a vizinhanca, e o que sobra quando nao ha nenhum
	// =====================================================================
	/// <summary>
	/// **O PEDIDO DO DONO, MEDIDO NOS QUATRO ESTADOS.**
	///
	/// *"quando uma raca fica sem planeta natal, o jogador pode ou spawnar em um planeta q ele
	/// conquistou ou em um planeta proximo do planeta natal dele"*.
	///
	/// ============================ AS DUAS METADES, SEMPRE JUNTAS ============================
	/// Cada estado e afirmado por DUAS medidas que tem donos diferentes:
	///   * a ESCOLHA que existia (<see cref="EscolhaDeRefugio"/>) -- quantas saidas havia e se havia o
	///     que decidir. E o que manda a tela ser empurrada ou nao;
	///   * o DESTINO de verdade (`DestinoDe`, o funil que a morte usa) -- a zona e o ponto.
	///
	/// Uma sozinha seria medir intencao; a outra sozinha nao distinguiria *"foi pra vizinhanca porque
	/// escolheu"* de *"foi porque nao havia mais nada"*. Sao exatamente os dois erros que este projeto
	/// ja pagou.
	/// ====================================================================================
	///
	/// ============================ E OS DOIS RAMOS RAROS SAO ALCANCADOS DE VERDADE ============================
	/// *"So o dominio existe"* e *"nao existe nenhum dos dois"* nao acontecem em jogo -- o universo e
	/// infinito e sempre ha um mundo vivo por perto. Sem uma manivela eles seriam CODIGO MORTO com
	/// aparencia de codigo vivo. A manivela e o <see cref="_celulasDeRefugio"/> (desliga a busca),
	/// apertando o PARAMETRO contra a producao, e nao um caminho paralelo -- mesma disciplina do
	/// `teto` do <see cref="Bercos.ServeDeBerco"/>.
	/// =====================================================================================================
	///
	/// O LIVRO DE DOMINIOS E DEVOLVIDO INTACTO no fim (`finally`): o `FincarDominio` de producao grava
	/// `conquista.json` na pasta do dono, e uma bancada que deixasse uma bandeira plantada la mudaria
	/// o renascimento de um personagem de verdade.
	/// </summary>
	private void AEscolhaDoRefugio()
	{
		GD.Print("[bercoprova] -- 7) A ESCOLHA: o dominio conquistado ou a vizinhanca de casa");

		// O CONQUISTADO E NAMEK, e a distancia e o argumento: 167 min de voo da Terra, ou 24 celulas
		// de sistema contra as 2 que a busca do refugio olha. Ou seja, o dominio NUNCA pode ser
		// confundido com um vizinho -- se o corpo aparecer la, foi por B1 e por mais nada.
		PlanetaNoEspaco namek = default;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (p.Nome == "Namek") namek = p;

		var salvos = new List<Dominio>(_dominios);
		CharacterSave c = SaveDeBancada("Prova Refugio", "Human", "Human", "Normal", false, new Random(97));
		ServerPlayer pl = CorpoDoSave(c, FaixaDaProva);
		pl.Conta = ContaDoRefugio;   // assinatura propria: dominio e do PERSONAGEM, nao da conta

		try
		{
			PousarDeVerdade(pl);
			Provar("o corpo da familia 7 nasceu na Terra (o mundo ainda esta inteiro)",
				   pl.Zone.Name == "Earth", pl.Zone.Name);

			Dominio d = FincarDominio(pl, namek, namek.Pos);
			Provar("...e ele conquistou Namek pelo funil de producao (`FincarDominio`)",
				   DominioDe(d.Chave) is { } meu
				   && string.Equals(meu.Assinatura, pl.Assinatura, StringComparison.Ordinal),
				   "a bandeira nao entrou no livro -- o resto desta familia nao prova nada");

			// --- COM A TERRA VIVA, O VERBO SE RECUSA A ESCREVER ---------------
			// A fronteira com o `conq_spawn`, que exige estar DE PE JUNTO DA BANDEIRA. Sem esta prova,
			// o verb do refugio seria um segundo caminho pro mesmo bit sem a exigencia do primeiro --
			// "regra num chamador, esquecida no outro", o defeito mais repetido deste port.
			ComandoDeRefugio(pl, "refugio", d.Chave.Texto);
			Provar("com o berco VIVO, o verb do refugio NAO escreve (a regra da bandeira continua de pe)",
				   !d.EhOSpawn,
				   "o verb remoto marcou o dominio sem o jogador ir ate a bandeira -- ele apagou a "
				   + "exigencia do `conq_spawn` calado");

			using (PalcoDeMortesDeBancada())
			{
				Provar("a injecao matou a Terra (rodada da escolha)",
					   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: a escolha"));

				// --- ESTADO 1: AS DUAS EXISTEM -> ESCOLHA, e o padrao e a vizinhanca ----
				(Vec2 _, List<Dominio> dom1, Arredores perto1, bool escolha1) = EscolhaDeRefugio(pl);
				Provar($"ESTADO 1: ha as DUAS saidas -- {dom1.Count} dominio(s) e "
					 + $"{perto1.Mundos.Count} mundo(s) perto de casa",
					   escolha1 && dom1.Count == 1 && !perto1.Vazia,
					   $"dominios={dom1.Count} vizinhos={perto1.Mundos.Count}");

				(ZoneKey z1, Vec2 _) = DestinoDe(pl);
				Provar($"...e o PADRAO e a vizinhanca ('{z1.Name}'), e nao o dominio",
					   z1.Name != "Namek" && perto1.Mundos.Any(m => m.Corpo.Nome == z1.Name),
					   $"foi parar em {z1.Name} -- o padrao mudou, e o bit `EhOSpawn` do `conq_spawn` "
					   + "deixou de significar alguma coisa");

				// --- ESTADO 2: O JOGADOR ESCOLHE O DOMINIO -----------------------
				ComandoDeRefugio(pl, "refugio", d.Chave.Texto);
				Provar("ESTADO 2: com o berco morto, o verb do refugio ESCREVE (e o unico caso em que ir "
					 + "ate a bandeira e impossivel)", d.EhOSpawn);

				(ZoneKey z2, Vec2 _) = DestinoDe(pl);
				Provar($"...e agora o corpo vai pro DOMINIO ('{z2.Name}')", z2.Name == "Namek", z2.Name);

				// --- ESTADO 3: E ELE PODE VOLTAR ATRAS ---------------------------
				// Sem esta, a escolha seria uma porta de mao unica -- e o `conq_spawn` e um liga/desliga.
				ComandoDeRefugio(pl, "refugio", "vizinhanca");
				(ZoneKey z3, Vec2 _) = DestinoDe(pl);
				Provar($"ESTADO 3: escolhendo a vizinhanca, o corpo volta pra perto de casa ('{z3.Name}')",
					   !d.EhOSpawn && z3.Name == z1.Name, $"{z3.Name} / EhOSpawn={d.EhOSpawn}");

				// --- ESTADO 4: SO O DOMINIO EXISTE -> ele e o destino, sem perguntar ----
				using (SemVizinhancaDeRefugio())
				{
					(Vec2 _, List<Dominio> dom4, Arredores perto4, bool escolha4) = EscolhaDeRefugio(pl);
					Provar($"ESTADO 4: sem vizinhanca, sobra UMA saida ({dom4.Count} dominio) e NAO ha "
						 + "escolha -- ninguem e perguntado",
						   !escolha4 && dom4.Count == 1 && perto4.Vazia,
						   $"escolha={escolha4} dominios={dom4.Count} vizinhos={perto4.Mundos.Count}");

					(ZoneKey z4, Vec2 _) = DestinoDe(pl);
					Provar($"...e o corpo vai pro dominio ('{z4.Name}') mesmo com o `EhOSpawn` DESLIGADO",
						   z4.Name == "Namek" && !d.EhOSpawn,
						   $"{z4.Name} / EhOSpawn={d.EhOSpawn}");
				}

				// --- ESTADO 5: NENHUMA DAS DUAS -> o espaco aberto ---------------
				PerderDominio(d, "", "bercoprova: fim da familia 7", anunciar: false);

				using (SemVizinhancaDeRefugio())
				{
					(Vec2 _, List<Dominio> dom5, Arredores perto5, bool escolha5) = EscolhaDeRefugio(pl);
					Provar("ESTADO 5: sem dominio e sem vizinhanca, NAO HA saida nenhuma",
						   !escolha5 && dom5.Count == 0 && perto5.Vazia,
						   $"dominios={dom5.Count} vizinhos={perto5.Mundos.Count}");

					(ZoneKey z5, Vec2 p5) = DestinoDe(pl);

					// ============================ E ISTO E O QUE NAO PRENDE NINGUEM ============================
					// A regra velha desistia devolvendo "a Terra, viva ou MORTA" -- um corpo num cadaver,
					// e o login seguinte caia no mesmo funil e devolvia a mesma Terra morta: um LACO.
					// O espaco aberto e o unico lugar deste jogo de onde se alcanca TODOS os outros.
					// ======================================================================================
					Provar($"...e o ultimo recurso e o ESPACO ABERTO ('{z5.Name}'), e nao a Terra morta",
						   Espaco.EhEspaco(z5) && !ZonaMorta(z5),
						   $"{z5.Name} -- se isto voltar a ser um planeta, o laco do cadaver volta junto");

					Provar("...na coordenada exata de onde a Terra ficava (o corpo abre os olhos em casa)",
						   Math.Abs(p5.X) < 1 && Math.Abs(p5.Y) < 1, $"({p5.X:0.0},{p5.Y:0.0})");
				}
			}
		}
		finally
		{
			Recolher(pl);

			// O LIVRO VOLTA COMO ESTAVA -- e o `SalvarConquista` reescreve o arquivo do dono com o
			// mesmo conteudo. Sem isto, uma bandeira de bancada ficaria plantada em Namek pra sempre.
			_dominios.Clear();
			_dominios.AddRange(salvos);
			SalvarConquista();

			Provar($"o livro de dominios voltou intacto ({_dominios.Count} dominio(s), como antes)",
				   _dominios.Count == salvos.Count && !_dominios.Any(x => x.Planeta == "Namek"
					   && string.Equals(x.Conta, ContaDoRefugio, StringComparison.Ordinal)),
				   "a bancada deixou bandeira plantada na pasta do dono");
		}
	}
}
