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
	/// O planeta que a familia 3 enfia na FRENTE da carta estelar. **Hera nao e um nome inventado**:
	/// ela tem mapa convertido (`Assets/Maps/manifest.json`, z11) e o cabecalho do
	/// <see cref="Bercos.PlanetaNatal"/> ja declara, com todas as letras, que o lar do Heran e ela e
	/// que faltam "uma LINHA em `Espaco.PreFeitos()`" pra ela virar corpo celeste.
	///
	/// Ou seja: a familia 3 nao simula um acidente improvavel -- ela simula a proxima mudanca que este
	/// arquivo ja prometeu que alguem vai fazer.
	/// </summary>
	private const string PlanetaIntruso = "Hera";

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
			AOrdemDaCarta();
			RenascerEhOOutroCaminho();
			OPovoNoMundoENaoNaTabela();
			PorQueNenhumaBancadaPegou();
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

		/// <summary>Quantos corpos acordaram em cada mundo. E o que a familia 6 usa pros agregados.</summary>
		public readonly Dictionary<string, int> PorMundoObtido = new(StringComparer.Ordinal);

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
	/// ESPERADO -- <see cref="Bercos.PlanetaNatal"/>, um `switch` de trinta linhas. E a frase do dono
	///             ("cada raca no seu devido planeta") escrita numa funcao so, sem servidor nenhum.
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
			string esperado = Bercos.PlanetaNatal(raca);

			CharacterSave c = SaveDeBancada($"Prova {raca}", raca, linhagem, "Normal", false, rng);
			ServerPlayer pl = CorpoDoSave(c, FaixaDaProva);
			try
			{
				PousarDeVerdade(pl);
				string obtido = pl.Zone.Name;

				r.PorMundoEsperado[esperado] = r.PorMundoEsperado.GetValueOrDefault(esperado) + 1;
				r.PorMundoObtido[obtido] = r.PorMundoObtido.GetValueOrDefault(obtido) + 1;

				if (string.Equals(obtido, esperado, StringComparison.OrdinalIgnoreCase)) r.Certas.Add(raca);
				else r.Desvios.Add((raca, esperado, obtido));

				if (detalhar)
					GD.Print($"[bercoprova]      {raca,-14} esperado {esperado,-12} obtido {obtido,-12}"
						   + $" @ ({pl.Pos.X / ZoneCollision.TileSize:0},{pl.Pos.Y / ZoneCollision.TileSize:0})"
						   + (string.Equals(obtido, esperado, StringComparison.OrdinalIgnoreCase) ? "" : "   <<< DESVIADA"));
			}
			finally { Recolher(pl); }
		}

		GD.Print($"[bercoprova]   [{rotulo}] {r.Resumo}");
		return r;
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
	// 2) O DEFEITO DE VOLTA -- as duas metades, uma de cada vez
	// =====================================================================
	/// <summary>
	/// ============================ A UNICA PROVA DE QUE UMA BANCADA FUNCIONA E VE-LA REPROVAR ============================
	/// A causa medida na fase 0 e reposta aqui **pelo caminho de producao**: `ComecarDestruicao` +
	/// `TickDaDestruicao` ate a fase virar `Destruido` -- os mesmos ~310 s que a Final Explosion da
	/// `--escudoteste` disparou no mundo do dono. Nao ha atalho escrevendo `FaseDaMorte.Destruido` no
	/// registro: o que se quer reproduzir e o ESTADO que o jogo produz, e nao um estado parecido.
	///
	/// E ele e reposto DUAS VEZES, uma por metade:
	///   * a TERRA morta -- a metade do relato. Tem que ficar vermelho nas racas da Terra;
	///   * NAMEK morta   -- a outra metade. Tem que ficar vermelho nas racas de NAMEK, e as da Terra
	///                      tem que continuar VERDES.
	///
	/// Sem a segunda, uma bancada que so soubesse dizer "todo mundo foi parar em Namek" ficaria verde
	/// no dia em que o desvio fosse pro outro lado -- e o pedido do dono e simetrico: *quem nasce na
	/// Terra nasce na Terra E quem nasce em Namek nasce em Namek*.
	/// ==============================================================================================================
	/// </summary>
	private void ODefeitoDeVolta()
	{
		GD.Print("[bercoprova] -- 2) O DEFEITO DE VOLTA (no palco: o save do dono nao e tocado)");

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
				   MatarPlanetaNoPalco(terra, "bercoprova: o defeito do relato, de volta"),
				   "a Terra nao chegou a `Destruido` -- a injecao nao aconteceu e o resto nao prova nada");

			Chamada c = ChamadaNominal("TERRA MORTA", detalhar: false);
			string[] daTerra = [.. ConjuntoDeRacas().Where(r => Bercos.PlanetaNatal(r) == "Earth")];

			Provar($"...e a chamada nominal FICOU VERMELHA ({c.Desvios.Count} desvios)",
				   c.Desvios.Count > 0,
				   "a bancada nao viu o defeito que causou o relato -- ela nao serve");

			Provar($"...vermelha em TODAS as {daTerra.Length} racas da Terra, e so nelas",
				   c.Desvios.Count == daTerra.Length
				   && c.Desvios.All(d => d.Esperado == "Earth"),
				   $"{c.Desvios.Count} desvios: {string.Join(",", c.Desvios.Select(d => d.Raca))}");

			Provar($"...e o destino do desvio foi UM so ({string.Join(",", c.DestinosErrados)})",
				   c.DestinosErrados.Length == 1, string.Join(",", c.DestinosErrados));

			// O NOME NAO E AFIRMADO, E ISTO E DE PROPOSITO: ele e uma consequencia da ORDEM da carta
			// (familia 3), e cravar "Namek" aqui seria gravar o acidente. O que se afirma e que ele
			// NAO e o planeta certo -- e a familia 3 troca a carta e ve o nome mudar.
			GD.Print($"[bercoprova]      o desvio levou {c.Desvios.Count} raca(s) da Terra pra "
				   + $"'{c.DestinosErrados.FirstOrDefault()}' -- e este nome sai da ORDEM de `Espaco.PreFeitos()`");

			Provar($"...e as racas de Namek/Vegeta continuaram certas ({c.Certas.Count} verdes)",
				   c.Certas.Contains("Namekian") && c.Certas.Contains("Saiyan"),
				   string.Join(",", c.Certas));
		}
		finally { palco.Dispose(); }

		Provar($"o palco viu a bancada matar {palco.MatouAqui} planeta ('{palco.NomesQueMorreram}')",
			   palco.MatouAqui == 1 && palco.NomesQueMorreram == "Earth",
			   $"{palco.MatouAqui} planeta(s): '{palco.NomesQueMorreram}' -- um palco que nao ve a arma "
			   + "disparar fica verde pra sempre no dia em que ela parar de disparar");

		Provar("a Terra voltou a existir quando o palco fechou",
			   !ZonaMorta(ZoneKey.Premade("Earth")), "o palco nao desfez a morte");

		Chamada volta = ChamadaNominal("depois do palco", detalhar: false);
		Provar($"...e a chamada nominal VOLTOU AO VERDE ({volta.Resumo})", volta.Desvios.Count == 0,
			   "a sonda ficou presa em vermelho -- ela estaria medindo a si mesma, e nao o mundo");

		// --- (b) A OUTRA METADE: NAMEK morta ---------------------------------
		using (PalcoDeMortesDeBancada())
		{
			var namek = ZoneKey.Premade("Namek");
			Provar("a injecao MATOU Namek de verdade",
				   MatarPlanetaNoPalco(namek, "bercoprova: a outra metade"), "Namek nao chegou a `Destruido`");

			Chamada c = ChamadaNominal("NAMEK MORTA", detalhar: false);
			string[] deNamek = [.. ConjuntoDeRacas().Where(r => Bercos.PlanetaNatal(r) == "Namek")];

			Provar($"...a chamada nominal ficou vermelha nas {deNamek.Length} racas de NAMEK",
				   c.Desvios.Count == deNamek.Length && c.Desvios.All(d => d.Esperado == "Namek"),
				   c.Resumo);

			Provar("...e as racas da TERRA continuaram VERDES (a metade que nao quebrou)",
				   c.Certas.Contains("Human") && c.Certas.Contains("Majin")
				   && !c.Desvios.Any(d => d.Esperado == "Earth"),
				   c.Resumo);

			// AQUI O RECUO NEM E CONSULTADO: a `SpawnZone` esta viva, entao o `ZonaDeRecuoViva`
			// devolve a Terra na primeira linha. E por isso que TODO desvio desta metade vai pra Terra
			// -- a assimetria e do codigo e nao da bancada, e vale registra-la.
			GD.Print($"[bercoprova]      com Namek morta o desvio vai pra '{c.DestinosErrados.FirstOrDefault()}'"
				   + " -- aqui o recuo nem desce a carta: a `SpawnZone` esta viva");
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
	// 3) A ORDEM DA CARTA -- a fragilidade e de LISTA
	// =====================================================================
	/// <summary>
	/// ============================ O DESTINO DO DEFEITO E UMA POSICAO NUMA LISTA ============================
	/// `ZonaDeRecuoViva` desce `Espaco.PreFeitos()` e devolve o PRIMEIRO vivo. Namek so foi o destino do
	/// relato porque e a **segunda linha** de um `yield return` (`Core/World/Espaco.cs:116`).
	///
	/// Isso significa duas coisas, e as duas sao medidas aqui:
	///   (a) matando os primeiros da carta em ordem, o destino ANDA -- Namek, depois Vegeta, depois
	///       Icer. Se ele nao andasse, a fragilidade nao seria de ordem e este comentario estaria
	///       mentindo;
	///   (b) uma linha NOVA na frente da lista muda o destino sem mudar mais nada. E o cabecalho do
	///       `Bercos.PlanetaNatal` ja avisa que essa linha vai ser escrita ("Hera... e uma LINHA em
	///       `Espaco.PreFeitos()`").
	///
	/// **Uma bancada que cravasse "com a Terra morta o corpo vai pra Namek" ficaria VERDE no dia da
	/// linha nova**, em cima do mesmo estrago. A sonda desta bancada nao sabe o nome do lugar errado:
	/// ela sabe o nome do lugar CERTO, que e o unico que nao muda.
	/// ====================================================================================================
	///
	/// E a terceira medida e a que impede a bancada de virar um alarme falso: com a carta intrusa e
	/// **tudo vivo**, nada pode mudar. Um recuo saudavel nunca e consultado.
	/// </summary>
	private void AOrdemDaCarta()
	{
		GD.Print("[bercoprova] -- 3) A ORDEM DA CARTA: o destino do defeito e uma posicao numa lista");

		string[] ordem = [.. Espaco.PreFeitos().Select(p => p.Nome)];
		GD.Print($"[bercoprova]      a carta, na ordem: {string.Join(" -> ", ordem)}");
		Provar("a carta estelar tem uma ORDEM observavel (a Terra e a primeira)",
			   ordem.Length >= 3 && ordem[0] == "Earth", string.Join(",", ordem));

		// --- (a) O DESTINO ANDA quando a frente da carta morre ----------------
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
				string destino = c.Desvios.Where(d => d.Esperado == "Earth")
									      .Select(d => d.Obtido).FirstOrDefault() ?? "(nenhum)";
				caminhados.Add(destino);

				Provar($"com '{vitima}' e os anteriores mortos, quem e da Terra foi parar em '{destino}'"
					   + " -- e a bancada VIU", c.Desvios.Count > 0, c.Resumo);
			}

			// O TETO DESTA FAMILIA DISPARA: se os tres destinos fossem iguais, a fragilidade nao seria
			// de ordem e a familia inteira estaria provando outra coisa.
			Provar($"o destino do desvio ANDOU pela carta ({string.Join(" -> ", caminhados)})",
				   caminhados.Distinct(StringComparer.Ordinal).Count() == caminhados.Count && caminhados.Count >= 2,
				   "os destinos se repetiram -- a fragilidade nao e de ordem, e este diagnostico esta errado");
		}

		// --- (b) A LINHA NOVA NA FRENTE DA CARTA ------------------------------
		// A carta intrusa e a de VERDADE com um planeta a mais NA FRENTE. Nada mais muda.
		IReadOnlyList<PlanetaNoEspaco> cartaIntrusa =
		[
			new PlanetaNoEspaco
			{
				Nome = PlanetaIntruso,
				Pos = new Vec2(0, 0),
				Raio = 170,
				Premade = true,
				Seed = Espaco.Hash64(PlanetaIntruso),
			},
			.. Espaco.PreFeitos(),
		];

		Provar($"'{PlanetaIntruso}' e uma zona de verdade (tem mapa no manifesto), e nao um nome solto",
			   _catalogo?.Get(ZoneKey.Premade(PlanetaIntruso)) != null,
			   "sem mapa o recuo a pularia e a injecao nao provaria nada");

		using (PalcoDeMortesDeBancada())
		using (OutraCartaDeRecuo(cartaIntrusa))
		{
			Provar("a injecao matou a Terra (de novo, com a carta intrusa)",
				   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: ordem + zona nova"));

			Chamada c = ChamadaNominal($"TERRA MORTA + '{PlanetaIntruso}' na frente", detalhar: false);

			Provar($"a chamada nominal ficou VERMELHA de novo ({c.Desvios.Count} desvios)",
				   c.Desvios.Count > 0,
				   "uma zona nova na frente da carta apagou o defeito da vista -- e o modo de falha "
				   + "que esta familia existe pra impedir");

			Provar($"...e agora o destino errado e '{PlanetaIntruso}', e nao mais Namek",
				   c.DestinosErrados.Length == 1 && c.DestinosErrados[0] == PlanetaIntruso,
				   string.Join(",", c.DestinosErrados));

			// O CONTRAFACTUAL, ESCRITO: esta e a linha que uma bancada tentada a gravar o acidente
			// teria escrito ("com a Terra morta o corpo vai parar em Namek"), e AQUI ELA FICARIA VERDE
			// -- porque nenhum corpo foi parar em Namek nesta rodada. Ela nao afirma o mundo: afirma
			// que aquela outra bancada seria cega, e e por isso que ela mora aqui e nao la.
			Provar("...e a bancada que cravasse 'Namek' ficaria VERDE nesta rodada (o contrafactual)",
				   !c.Desvios.Any(d => d.Obtido == "Namek"),
				   "alguem foi parar em Namek -- o contrafactual nao vale e esta linha nao prova nada");

			Provar("...com EXATAMENTE as mesmas racas desviadas de antes (o defeito e o mesmo)",
				   c.Desvios.All(d => d.Esperado == "Earth"), c.Resumo);
		}

		// --- (c) E COM TUDO VIVO, a carta intrusa nao muda NADA ---------------
		using (OutraCartaDeRecuo(cartaIntrusa))
		{
			Chamada c = ChamadaNominal($"'{PlanetaIntruso}' na frente, tudo VIVO", detalhar: false);
			Provar($"com tudo vivo, '{PlanetaIntruso}' na frente da carta nao muda nada ({c.Resumo})",
				   c.Desvios.Count == 0,
				   "a bancada reprovou por barulho: um recuo saudavel nunca chega a ser consultado");
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
	/// E ela e injetada tambem: com a Terra morta, o RENASCIMENTO tem que ficar vermelho junto. Um
	/// caminho que ninguem mede e onde o proximo defeito mora.
	/// </summary>
	private void RenascerEhOOutroCaminho()
	{
		GD.Print("[bercoprova] -- 4) RENASCER: o outro caminho, raca por raca");

		(int certas, List<string> erradas) sadio = RodadaDeRenascimento("mundo como esta");
		Provar($"todas as {sadio.certas} racas RENASCERAM no planeta delas",
			   sadio.erradas.Count == 0, string.Join(" | ", sadio.erradas.Take(6)));

		using (PalcoDeMortesDeBancada())
		{
			Provar("a injecao matou a Terra (rodada do renascimento)",
				   MatarPlanetaNoPalco(ZoneKey.Premade("Earth"), "bercoprova: renascimento"));

			(int certas, List<string> erradas) doente = RodadaDeRenascimento("TERRA MORTA");
			Provar($"...e o RENASCIMENTO tambem ficou vermelho ({doente.erradas.Count} racas)",
				   doente.erradas.Count > 0,
				   "o renascimento nao viu o defeito -- e ele e o caminho que o jogador usa mais");
			Provar("...nas racas da Terra, e so nelas",
				   doente.erradas.Count == ConjuntoDeRacas().Count(r => Bercos.PlanetaNatal(r) == "Earth"),
				   string.Join(" | ", doente.erradas.Take(6)));
		}
	}

	/// <summary>
	/// Uma volta completa por raca: nasce -> pousa -> **e levado pra longe** -> `Renascer` -> pousa.
	///
	/// O desvio pra longe nao e enfeite: sem ele o `Renascer` cai no ramo "mesma zona" e nunca chega
	/// no `MandarProBerco`, que e justamente o pedaco que so a morte usa.
	/// </summary>
	private (int Certas, List<string> Erradas) RodadaDeRenascimento(string rotulo)
	{
		var rng = new Random(31337);
		var erradas = new List<string>();
		int certas = 0;

		foreach (string raca in ConjuntoDeRacas())
		{
			string[] escolhas = CharacterDraft.EscolhasDeClasse(raca);
			CharacterSave c = SaveDeBancada($"Renasce {raca}", raca,
				escolhas.Length > 0 ? escolhas[0] : "", "Normal", false, rng);

			string esperado = Bercos.PlanetaNatal(raca);
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

				if (string.Equals(pl.Zone.Name, esperado, StringComparison.OrdinalIgnoreCase)) certas++;
				else erradas.Add($"{raca}: renasceu em {pl.Zone.Name}, esperado {esperado}");
			}
			finally { Recolher(pl); }
		}

		GD.Print($"[bercoprova]   [{rotulo}] renascimento: {certas} certas, {erradas.Count} erradas");
		return (certas, erradas);
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
		Provar($"o mundo esta MESMO estragado ({c.Desvios.Count} racas fora de casa)", c.Desvios.Count > 0);

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

		GD.Print($"[bercoprova]      => as tres cegueiras passam no mundo em que {c.Desvios.Count} racas "
			   + "nascem no planeta errado. O que corta e a afirmacao NOMINAL: uma linha por raca, com "
			   + "o nome do planeta esperado e o do obtido.");
	}
}
