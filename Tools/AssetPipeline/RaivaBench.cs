using Jandirus.Core.Forms;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DA RAIVA (`raiva`) -- de que furia cada forma nasce, e QUEM FICA SEM PORTA.
///
/// ============================ A REGRA DE OURO DESTA BANCADA ============================
/// **Toda checagem passa pelo mesmo funil do jogo -- `EstadoDeForma.Avaliar` --, e nenhuma refaz a
/// conta por fora.** A tentacao aqui era enorme: as regras da raiva sao derivadas, entao dava pra
/// testar `Catalogo.RaivaExigida` sozinho, ficar verde, e nunca descobrir que o `Avaliar` nao
/// pergunta por ela. Este port ja pagou esse preco varias vezes -- regra escrita, testada e nunca
/// ligada (o `PortaBp` do Oozaru Dourado, a API de sigilo do BP, os 35 atlas nunca importados).
///
/// Entao a divisao e: a tabela de niveis confere a DERIVACAO (secao 1), e todo o resto confere que
/// o `Avaliar` **cobra** aquilo, no mesmo caminho que a tecla C e a aba Formas usam.
/// ====================================================================================
///
/// ============================ O QUE ELA COBRE, E A LINHA QUE CADA UMA TRANCA ============================
///   [1] O NIVEL DE CADA ENTRADA -- a MENOR raiva que abre cada forma: a comum (`Comum`) pra toda forma
///       de raiva, a `Extrema` so pro Beast, nenhuma pro resto. Tabela escrita a mao, nos dois sentidos.
///   [2] TODA FORMA DE RAIVA ABRE COM A COMUM, MENOS O BEAST -- a linha Legendary nao tem mais desconto proprio.
///   [3] O `Avaliar` COBRA o degrau, forma por forma: sem raiva recusa, no degrau exato abre, e o
///       degrau de cima tambem abre (o `>=` que faz o luto valer por um nocaute).
///   [4] QUEM NAO PEDE RAIVA NAO E COBRADO -- o avesso, sem o qual [3] passaria num mundo em que
///       tudo pede furia e metade do catalogo esta morta.
///   [5] O SSJ3: nao pede raiva, pede 50% de maestria no SSJ2 **e** o poder pessoal.
///   [6] O SSJ4 nao pede raiva; o OOZARU DOURADO pede o SSJ1 dominado **e** o poder pessoal.
///   [7] NENHUMA DIVINA POR RAIVA, exceto o Beast -- pela regra e nao pelo `default` do switch.
///   [8] O GANCHO DA AMIZADE TEM DONO: varredura dos fontes atras do chamador de producao (e de
///       quem escreve as janelas de raiva por fora dele).
///   [9] COBERTURA: toda entrada do catalogo tem porta DECLARADA, e a porta DISPARA pelo `Avaliar`.
///  [10] A CINEMATICA DA FURIA: o roteiro contra os `sleep()` do `AngerCinematic()`, as quatro
///       regras de cena que este projeto ja pagou caro, e a varredura dos fontes atras de quem
///       dispara o pacote (e de quem NAO pode dispara-lo).
///  [11] OS DOIS DEGRAUS EM NUMERO E O SALTO DE KI (dono, 2026-10-11): o nocaute vale o "Angry" do DM,
///       a morte vale o topo, e so a furia extrema leva ao teto de carga o Ki de quem tem controle.
///  [12] A SUBIDA PELA RAIVA (dono, 2026-10-11): que forma NOVA a raiva estreia, e por quais degraus ja
///       despertados o corpo sobe ate ela -- o plano do Core (`SubidaPelaRaiva`), com os dois defeitos injetados.
///  [13] O PRECO DA RAIVA COMUM (dono, 2026-10-11): forma por forma, calmo nao entra, a furia extrema entra pela
///       porta de BP e a raiva comum so entra pela porta VEZES o agio do DM -- com os dois defeitos injetados.
/// ====================================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- raiva     (da RAIZ do repo -- a secao [8] le os fontes)
/// </summary>
public static class RaivaBench
{
	private static int _ok, _falhou;

	private static void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; Console.WriteLine($"  OK   {nome}"); }
		else { _falhou++; Console.WriteLine($"  FALHA {nome}   {detalhe}"); }
	}

	public static int Rodar()
	{
		Console.WriteLine("=== BANCADA DA RAIVA (de que furia cada forma nasce) ===\n");

		OsNiveis();
		AComumAbreTodas();
		OFunilCobra();
		QuemNaoPedeNaoEhCobrado();
		OSsj3();
		OSsj4EODourado();
		AsDivinas();
		OGanchoLigado();
		Cobertura();
		ACinematicaDaFuria();
		OsDoisDegrausEOSaltoDeKi();
		ASubidaPelaRaiva();
		OPrecoDaRaivaComum();

		Console.WriteLine($"\n=== {_ok} OK, {_falhou} FALHA ===");
		return _falhou == 0 ? 0 : 1;
	}

	// =====================================================================
	// A TABELA DE VERDADE -- escrita a mao, conferida contra o dono
	// =====================================================================
	/// <summary>
	/// AS FORMAS QUE PEDEM RAIVA, E A MENOR QUE AS ABRE. **Escrita a mao de proposito**: se ela fosse
	/// gerada pelo `Catalogo.RaivaExigida` o teste passaria por construcao e nao provaria nada -- e o
	/// mesmo motivo pelo qual os multiplicadores da bancada `formas` estao a mao.
	///
	/// DESDE 2026-10-11 a raiva comum (`Comum`) abre toda forma de raiva menos o Beast -- por um preco de BP
	/// maior, que e a secao [13]. Ate ali esta tabela dizia `Extrema` pro tronco Saiyajin, o Futuro e o Heran.
	///
	/// Quem nao esta aqui nao pede raiva nenhuma. Ver a secao [1] pros dois sentidos da conferencia.
	/// </summary>
	private static readonly (string Id, NivelDeRaiva Nivel)[] Esperado =
	[
		// O TRONCO DE SANGUE: basta ver um amigo CAIR (o `if("Angry")` do `supersaiyan.dm:111-114` e `:128-131`).
		("ssj1",               NivelDeRaiva.Comum),
		("ssj2",               NivelDeRaiva.Comum),
		("future_ssj",         NivelDeRaiva.Comum),

		// A FERA: a unica divina por raiva, e a unica que so a furia extrema abre (`supersaiyan.dm:166`).
		("beast",              NivelDeRaiva.Extrema),

		// A LINHA LEGENDARY INTEIRA: idem (`supersaiyan.dm:67-70`, `:93-94`, `:101-102`).
		("wrathful",           NivelDeRaiva.Comum),
		("c_type",             NivelDeRaiva.Comum),
		("legendary",          NivelDeRaiva.Comum),
		("primal_c_type",      NivelDeRaiva.Comum),
		("primal_legendary",   NivelDeRaiva.Comum),
		("primal_legendary2",  NivelDeRaiva.Comum),

		// O HERAN. Ele entra aqui pelo mesmo motivo do tronco Saiyajin -- `heran.dm:20-52` roda o
		// MESMO `switch(savant.Emotion)` do `supersaiyan.dm` nos dois degraus, e o
		// `mst_form_needs_rage` (`MasterStudent.dm:246-249`) lista `heran1` e `heran2` ao lado de
		// `ssj` e `ssj2`. Os dois degraus tem ramo `"Angry"` la (`heran.dm:28-32` e `:44-48`), entao a raiva
		// comum os abre tambem.
		//
		// **E ELE E A ENTRADA QUE PEGOU UM DEFEITO REAL NA DERIVACAO.** Antes desta sessao o
		// `RaivaExigida` listava so `Saiyajin` e `Futuro`, com um comentario prometendo que a linha
		// Heran seria pega sozinha quando viesse. Nao seria: linha nova cai no `_ => Nenhuma`, e as
		// duas formas teriam nascido de graca, caladas.
		("heran1",             NivelDeRaiva.Comum),
		("heran2",             NivelDeRaiva.Comum),

		// O SUPER NAMEKUSEIJIN E AS DUAS FORMAS ALIEN **NAO** ENTRAM, e a ausencia e medida: elas se
		// COMPRAM (`FormaDef.PedeFlag`), e o `snamek()` / `Alien_Trans()` nao olham `Emotion` uma
		// unica vez. O que se compra nao se desperta.
	];

	private static NivelDeRaiva Esperada(string id) =>
		Esperado.FirstOrDefault(e => e.Id == id).Id == null
			? NivelDeRaiva.Nenhuma
			: Esperado.First(e => e.Id == id).Nivel;

	// =====================================================================
	// 1. O NIVEL DE CADA ENTRADA DO CATALOGO
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE VARRER AS 35 E NAO CONFERIR AS 10 ============================
	/// Porque a regra e DERIVADA do arco da linha, e derivacao muda por tabela: mexer no
	/// `ForaDoTronco` de um grade, no `PedeMaestria` de um Full Power ou no `PedeGodKi` de um degrau
	/// divino muda esta lista sem que ninguem esteja pensando em raiva. Conferir so as 10 esperadas
	/// pegaria a perda de um degrau e **nao pegaria a entrada de um degrau novo** -- que e o caso
	/// mais provavel, porque o catalogo existe justamente pra crescer.
	///
	/// A varredura e nos DOIS sentidos: toda entrada da tabela tem o nivel dito, e toda entrada do
	/// catalogo bate com a tabela (incluindo as que tem que dar `Nenhuma`).
	/// ================================================================================================
	/// </summary>
	private static void OsNiveis()
	{
		Console.WriteLine("[1] O NIVEL DE RAIVA DE CADA ENTRADA DO CATALOGO");

		foreach (FormaDef d in Catalogo.Todas)
		{
			NivelDeRaiva esperado = Esperada(d.Id), obtido = Catalogo.RaivaExigida(d);
			Checa($"`{d.Id}` pede raiva {esperado}", obtido == esperado, $"deu {obtido}");
		}

		// E A BASE NAO E TRANSFORMACAO NENHUMA -- descer nunca pode pedir raiva, senao um jogador
		// ficaria preso na forma em que despertou ate outro amigo morrer.
		Checa("a base nao pede raiva (descer sempre pode)",
			  Catalogo.RaivaExigida(Catalogo.Def(Catalogo.IdBase)) == NivelDeRaiva.Nenhuma);
		Checa("uma forma inexistente nao pede raiva (nulo nao concede nem cobra)",
			  Catalogo.RaivaExigida(null) == NivelDeRaiva.Nenhuma);

		// O RESUMO `NasceDaRaiva` SAI DESTA MESMA CONTA, e essa e a unica razao de ele continuar
		// existindo: o cliente o usa pra escolher o tamanho da cratera (`Transformacao.cs`). Se as
		// duas derivassem separado, um dia o jogo abriria uma forma cuja cratera diz que ela nao veio
		// da raiva -- e ninguem olharia pra ca.
		foreach (FormaDef d in Catalogo.Todas)
			Checa($"`{d.Id}`: NasceDaRaiva concorda com RaivaExigida",
				  Catalogo.NasceDaRaiva(d) == (Catalogo.RaivaExigida(d) != NivelDeRaiva.Nenhuma));

		int comRaiva = Catalogo.Todas.Count(d => Catalogo.RaivaExigida(d) != NivelDeRaiva.Nenhuma);
		Checa($"sao {Esperado.Length} formas por raiva no catalogo inteiro",
			  comRaiva == Esperado.Length, $"deu {comRaiva}");

		Console.WriteLine($"  -> EXTREMA: {Lista(NivelDeRaiva.Extrema)}");
		Console.WriteLine($"  -> COMUM: {Lista(NivelDeRaiva.Comum)}");
		Console.WriteLine();

		static string Lista(NivelDeRaiva n) =>
			string.Join(", ", Catalogo.Todas.Where(d => Catalogo.RaivaExigida(d) == n).Select(d => d.Id));
	}

	// =====================================================================
	// 2. TODA FORMA DE RAIVA ABRE COM A COMUM -- MENOS O BEAST
	// =====================================================================
	/// <summary>
	/// A regra do dono de 2026-10-11, dita como REGRA e nao como lista: *"o rage normal (...) tb faz vc se
	/// transformar da mesma forma q o extreme"*. Ate ali a raiva comum era desconto so da linha Legendary, e esta
	/// secao media o contrario do que mede hoje ("nenhum degrau fora da linha Legendary pega o desconto").
	///
	/// COMO REPROVA SE A REGRA SUMIR: o defeito injetado (`Catalogo.SoAFuriaAbreOTroncoDeTeste`, a regra antiga)
	/// faz o tronco Saiyajin, o Futuro e o Heran voltarem a pedir a furia do luto -- a lista de "so com a extrema"
	/// cresce de uma forma pra seis, e a checagem cai dizendo quais. O PRECO da raiva comum e a secao [13].
	/// </summary>
	private static void AComumAbreTodas()
	{
		Console.WriteLine("[2] TODA FORMA DE RAIVA ABRE COM A RAIVA COMUM, MENOS O BEAST");

		FormaDef[] deRaiva = [.. Catalogo.Todas.Where(d => Catalogo.NasceDaRaiva(d))];
		Checa("ha formas de raiva no catalogo (o teste nao rodou no vazio)", deRaiva.Length >= 10, $"{deRaiva.Length} entradas");

		string[] soExtrema = [.. deRaiva.Where(d => Catalogo.RaivaExigida(d) == NivelDeRaiva.Extrema).Select(d => d.Id)];
		Checa("a UNICA forma que so a furia extrema abre e o Beast",
			  soExtrema.Length == 1 && soExtrema[0] == "beast", string.Join(", ", soExtrema));

		string[] comAComum = [.. deRaiva.Where(d => Catalogo.RaivaExigida(d) == NivelDeRaiva.Comum).Select(d => d.Id)];
		LinhaDeForma[] deSangue = [LinhaDeForma.Saiyajin, LinhaDeForma.Futuro, LinhaDeForma.Heran,
								   LinhaDeForma.Legendary, LinhaDeForma.LegendaryPrimal];
		Checa($"e as outras {comAComum.Length} abrem com a raiva comum -- do tronco Saiyajin, do Futuro, do Heran e das duas linhas Legendary",
			  comAComum.Length == deRaiva.Length - 1
			  && deSangue.All(l => deRaiva.Any(d => d.Linha == l && Catalogo.RaivaExigida(d) == NivelDeRaiva.Comum)),
			  string.Join(", ", comAComum));

		// OS QUE NAO PEDEM RAIVA NENHUMA SAO OS QUE SAIRAM DO TRONCO, e nao "os que sobraram". Sem esta linha, um
		// degrau de sangue que perdesse a raiva por engano se esconderia aqui dentro.
		foreach (FormaDef d in Catalogo.Todas.Where(d => deSangue.Contains(d.Linha) && d.Id != Catalogo.IdBase
														  && Catalogo.RaivaExigida(d) == NivelDeRaiva.Nenhuma))
			Checa($"`{d.Id}` nao pede raiva porque saiu do tronco (e treino)",
				  d.ForaDoTronco || d.PedeMaestria > 0 || d.PedeFormaDespertada.Length > 0,
				  "esta no tronco e mesmo assim nao pede raiva");

		// ---- contra-exemplo: a regra de antes ----
		Catalogo.SoAFuriaAbreOTroncoDeTeste = true;
		try
		{
			string[] antes = [.. Catalogo.Todas.Where(d => Catalogo.RaivaExigida(d) == NivelDeRaiva.Extrema).Select(d => d.Id)];
			Checa("(defeito injetado: so a furia abre o tronco Saiyajin, o Futuro e o Heran -- a regra ate 2026-10-11) a lista 'so com a extrema' deixa de ser so o Beast: a checagem REPROVA",
				  antes.Length > 1 && antes.Contains("ssj1") && antes.Contains("heran1"), string.Join(", ", antes));
		}
		finally { Catalogo.SoAFuriaAbreOTroncoDeTeste = false; }

		Console.WriteLine();
	}

	// =====================================================================
	// O PREPARADOR -- um corpo com TUDO pago menos a raiva
	// =====================================================================
	/// <summary>
	/// MONTA UM PERSONAGEM QUE SATISFAZ **TODOS** OS GATES DE `d` MENOS A RAIVA.
	///
	/// ============================ POR QUE GENERICO E NAO UM CASO POR FORMA ============================
	/// Porque um caso por forma envelhece: o degrau novo entra no catalogo e nao entra na lista de
	/// casos, e a bancada continua verde sem nunca ter olhado pra ele. Lendo os proprios campos da
	/// entrada, o preparador acompanha o catalogo sozinho -- que e a mesma promessa que o rework das
	/// formas fez ("uma forma nova e uma entrada e mais nada").
	///
	/// ============================ E ELE TEM QUE PROVAR QUE CONSEGUIU ============================
	/// O risco de um preparador generico e o oposto do de uma lista: ele pode falhar em satisfazer
	/// algum gate e a forma ser recusada por OUTRO motivo -- e ai a checagem "recusado por SemFuria"
	/// vira "recusado por qualquer coisa" e passa por acidente. Por isso todo uso dele confere o
	/// caso POSITIVO tambem (com a raiva no degrau certo, tem que dar `Pode`): se o preparador nao
	/// deu conta de alguma entrada, a bancada reprova dizendo qual, em vez de mentir verde.
	/// ============================================================================================
	/// </summary>
	private static (EstadoDeForma est, PerfilDeFormas perfil, double bp) Preparado(FormaDef d,
																				   NivelDeRaiva raiva)
	{
		// --- A IDENTIDADE: classe e linhagem que a entrada pede, ou a que ABRE a linha ---------
		string classe = d.PedeClasseUmaDe.Length > 0 ? d.PedeClasseUmaDe[0] : d.Linha switch
		{
			LinhaDeForma.LegendaryPrimal => Oozaru.ClasseLegendaryPrimal,
			LinhaDeForma.Legendary => "Legendary",
			LinhaDeForma.Futuro => "Future Lineage",
			LinhaDeForma.Mistico => Catalogo.ClasseProdigial,
			_ => "Normal",
		};
		string linhagem = d.PedeLinhagem.Length > 0 ? d.PedeLinhagem : Oozaru.LinhagemPrimal;

		// A ORIGEM casa com linhagem OU classe -- ver `FormaDef.PedeOrigemUmaDe`. Se nenhuma das duas
		// bater, a linhagem cede (e ela que o campo descreve no Saiyajin puro).
		if (d.PedeOrigemUmaDe.Length > 0
			&& !Bate(d.PedeOrigemUmaDe, linhagem) && !Bate(d.PedeOrigemUmaDe, classe))
			linhagem = d.PedeOrigemUmaDe[0];

		// ============================ A RACA TAMBEM SAI DA LINHA -- e ela FALTAVA ============================
		// Este preparador cravava `Raca: "Saiyan"`, e enquanto todas as linhas de sangue eram Saiyajin
		// isso passava despercebido. A linha do Frost Demon ja tinha exposto metade do problema (com
		// raca errada ela cai em `LinhaFechada`), e sobreviveu porque nenhuma forma dela pede raiva --
		// so a secao [4], que e NEGATIVA, olhava pra ela. O Heran nao sobreviveria: ele pede raiva, e a
		// secao [3] cobra `Pode`.
		//
		// Derivado da linha e nao uma lista de ids, pelo mesmo argumento que o cabecalho deste metodo
		// ja faz: a proxima linha racial nasce com raca certa aqui sozinha.
		// ================================================================================================
		string raca = d.Linha switch
		{
			LinhaDeForma.FrostDemon => Jandirus.Core.Races.FormasDeFrost.Raca,
			LinhaDeForma.Namekuseijin => Catalogo.RacaNamekuseijin,
			LinhaDeForma.Heran => Catalogo.RacaHeran,
			LinhaDeForma.Alien => Catalogo.RacaAlien,
			LinhaDeForma.Tensao => Catalogo.RacaHumano,
			LinhaDeForma.SuperMajin => SuperMajin.Raca,   // (2026-10-23) o Majin que nao e Corrupted
			_ => "Saiyan",
		};

		// AS FLAGS DE SKILL QUE A ENTRADA PEDE, PAGAS -- o mesmo criterio de "tudo o que se aprende,
		// aprendido" que o ki divino e a proficiencia ja seguem tres linhas abaixo. Sem isto o
		// `snamek` e as formas Alien seriam recusadas por `SemHabilidade` e a secao [4] mediria o
		// gate errado (ela so proibe `SemFuria`, entao passaria verde sem ter chegado perto da raiva).
		Dictionary<string, double>? flags = d.PedeFlag is { } f
			? new Dictionary<string, double>(StringComparer.Ordinal) { [f.Campo] = f.Minimo }
			: null;

		var perfil = new PerfilDeFormas(
			Raca: raca, Classe: classe, Linhagem: linhagem,
			Legendary: d.Linha == LinhaDeForma.Legendary,
			Futuro: d.Linha == LinhaDeForma.Futuro,
			// TUDO O QUE SE APRENDE, APRENDIDO. O `-1` do ki divino so fica pra quem esta numa linha
			// de SANGUE: despertar o ki divino la abriria a escada divina junto, e o `Proxima` de
			// outras checagens passaria a oferecer o SSG no meio da escada Saiyajin.
			// AS LINHAS DE SANGUE ficam sem ki divino: despertar o ki divino nelas abriria a escada
			// divina junto, e o `Proxima` de outras checagens passaria a oferecer o SSG no meio da
			// escada. As quatro linhas raciais entraram nesta lista pelo mesmo motivo -- e nao por
			// simetria: um Heran com 100% de maestria divina teria o Blue disponivel ao lado do
			// Max Power, o que e outro jogo.
			GodKi: d.Linha is LinhaDeForma.Saiyajin or LinhaDeForma.Futuro
						   or LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal
						   or LinhaDeForma.FrostDemon or LinhaDeForma.Namekuseijin
						   or LinhaDeForma.Heran or LinhaDeForma.Alien ? -1 : 100,
			EnergiaUe: d.Linha == LinhaDeForma.UltraEgo ? 100 : 0,
			ProficienciaUi: d.Linha == LinhaDeForma.UltraInstinct ? 100 : 0,
			Raiva: raiva,
			FlagsDeSkill: flags);

		var est = new EstadoDeForma();

		// TODA MAESTRIA PAGA. Cobre `PedeMaestria` venha ela do degrau anterior ou do `PedeMaestriaDe`.
		foreach (FormaDef x in Catalogo.Todas) est.Maestria.Por(x.Id, 100);

		// AS FORMAS QUE A ENTRADA EXIGE JA CONQUISTADAS -- e so elas. A propria `d` NAO e liberada:
		// liberar dispensaria a raiva (o `hasbeast`), e ai a bancada testaria o toggle e nao a porta.
		if (d.PedeFormaDespertada.Length > 0) est.Liberar(d.PedeFormaDespertada);
		if (d.SoPorConcessao) est.Liberar(d.Id);

		// E O CORPO NO DEGRAU ANTERIOR. Quando a forma e CAMADA (`PedeFormaAtual`), o passo 5 do
		// `Avaliar` cobra o anterior por DESPERTADO e o passo 4 cobra estar na camada de baixo --
		// entao aqui vale entrar na primeira opcao de `PedeFormaAtual` e liberar o anterior.
		FormaDef? ant = Catalogo.Anterior(d);
		if (ant != null && ant.Id != Catalogo.IdBase) est.Liberar(ant.Id);
		string onde = d.PedeFormaAtual.Length > 0 ? d.PedeFormaAtual[0] : ant?.Id ?? Catalogo.IdBase;
		if (onde != Catalogo.IdBase) est.Liberar(onde);
		est.Entrar(onde);

		return (est, perfil, 1e15);

		static bool Bate(string[] lista, string v) =>
			lista.Any(x => string.Equals(x, v, StringComparison.OrdinalIgnoreCase));
	}

	// =====================================================================
	// 3. O FUNIL COBRA O DEGRAU -- forma por forma
	// =====================================================================
	/// <summary>
	/// A SECAO QUE MEDE O JOGO E NAO A TABELA. As secoes [1] e [2] provam que `RaivaExigida` responde
	/// certo; esta prova que **o `Avaliar` pergunta**. Sao coisas diferentes, e a segunda e a que
	/// faltou em varios sistemas deste port -- dado escrito, conferido e nunca lido.
	///
	/// TRES MEDIDAS POR FORMA, e cada uma tranca uma coisa diferente:
	///   * SEM RAIVA recusa -> o passo 9 do `Avaliar` existe;
	///   * NO DEGRAU EXATO abre -> a porta nao esta trancada por engano (e o preparador deu conta);
	///   * NO DEGRAU DE CIMA tambem abre -> o `>=`, que faz o luto valer por um nocaute. Com `==`
	///     esta e a unica que cai, e ela cai so pra linha Legendary -- que e exatamente o defeito
	///     mais dificil de ver jogando: um Legendary de luto descobrindo que nao consegue subir.
	/// </summary>
	private static void OFunilCobra()
	{
		Console.WriteLine("[3] O `Avaliar` COBRA A RAIVA, FORMA POR FORMA");

		foreach (FormaDef d in Catalogo.Todas)
		{
			NivelDeRaiva pede = Catalogo.RaivaExigida(d);
			if (pede == NivelDeRaiva.Nenhuma) continue;

			// --- sem raiva nenhuma: recusa ------------------------------------------------
			(EstadoDeForma e0, PerfilDeFormas p0, double bp) = Preparado(d, NivelDeRaiva.Nenhuma);
			RecusaForma r0 = e0.Avaliar(d.Id, bp, 1, false, p0);
			Checa($"`{d.Id}` em calma: recusado por SEM FURIA", r0 == RecusaForma.SemFuria, r0.ToString());

			// --- no degrau exato: abre ----------------------------------------------------
			(EstadoDeForma e1, PerfilDeFormas p1, _) = Preparado(d, pede);
			RecusaForma r1 = e1.Avaliar(d.Id, bp, 1, false, p1);
			Checa($"`{d.Id}` com raiva {pede}: abre", r1 == RecusaForma.Pode, r1.ToString());

			// --- um degrau ABAIXO: recusa (so faz sentido pra quem pede Extrema) -----------
			if (pede == NivelDeRaiva.Extrema)
			{
				(EstadoDeForma e2, PerfilDeFormas p2, _) = Preparado(d, NivelDeRaiva.Comum);
				RecusaForma r2 = e2.Avaliar(d.Id, bp, 1, false, p2);
				Checa($"`{d.Id}` com a raiva COMUM so, e poder sem fim: ainda recusado por furia (so a extrema o abre)",
					  r2 == RecusaForma.SemFuria, r2.ToString());
			}

			// --- um degrau ACIMA: abre (o `>=`) --------------------------------------------
			if (pede == NivelDeRaiva.Comum)
			{
				(EstadoDeForma e3, PerfilDeFormas p3, _) = Preparado(d, NivelDeRaiva.Extrema);
				RecusaForma r3 = e3.Avaliar(d.Id, bp, 1, false, p3);
				Checa($"`{d.Id}` com a furia do LUTO: abre tambem (quem viu morrer viu cair)",
					  r3 == RecusaForma.Pode, r3.ToString());
			}

			// --- DESPERTA UMA VEZ E VIRA TOGGLE: o `hasbeast` do `supersaiyan.dm:165` -------
			// Sem isto a regra seria outra e muito pior: a forma voltaria a fechar sozinha dois
			// minutos depois, e o jogador precisaria de um segundo amigo morto pra reusar o SSJ1.
			(EstadoDeForma e4, PerfilDeFormas p4, _) = Preparado(d, NivelDeRaiva.Nenhuma);
			e4.Liberar(d.Id);
			RecusaForma r4 = e4.Avaliar(d.Id, bp, 1, false, p4);
			Checa($"`{d.Id}` ja desperto: dispensa a raiva pra sempre", r4 == RecusaForma.Pode,
				  r4.ToString());
		}

		Console.WriteLine();
	}

	// =====================================================================
	// 4. O AVESSO: quem nao pede raiva nao e cobrado
	// =====================================================================
	/// <summary>
	/// SEM ESTA SECAO, A [3] PASSARIA NUM MUNDO EM QUE **TUDO** PEDE FURIA. E o modo de falha mais
	/// plausivel de todos, porque a "simplificacao" e tentadora: trocar `perfil.Raiva &lt; pede` por
	/// `perfil.Raiva == NivelDeRaiva.Nenhuma` no passo 9 gatearia o catalogo inteiro, o SSG e o
	/// Ultra Instinto virariam formas de raiva, e as secoes [1] a [3] continuariam verdes.
	///
	/// A checagem e negativa de proposito -- "nao pode ser `SemFuria`" e nao "tem que ser `Pode`":
	/// a maioria destas formas e recusada por outro gate no corpo do preparador (o `mistico` pede
	/// concessao, os `_full_power` pedem maestria que ja esta paga...), e exigir `Pode` aqui seria
	/// testar o preparador em vez da regra.
	/// </summary>
	private static void QuemNaoPedeNaoEhCobrado()
	{
		Console.WriteLine("[4] QUEM NAO PEDE RAIVA NUNCA OUVE `SemFuria`");

		int olhadas = 0;
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == Catalogo.IdBase) continue;
			if (Catalogo.RaivaExigida(d) != NivelDeRaiva.Nenhuma) continue;
			olhadas++;

			(EstadoDeForma est, PerfilDeFormas perfil, double bp) = Preparado(d, NivelDeRaiva.Nenhuma);
			RecusaForma r = est.Avaliar(d.Id, bp, 1, false, perfil);
			Checa($"`{d.Id}` nao e recusado por raiva", r != RecusaForma.SemFuria, r.ToString());
		}

		Checa($"a varredura olhou as {olhadas} formas sem raiva (nao rodou no vazio)",
			  olhadas >= 20, $"{olhadas}");
		Console.WriteLine();
	}

	// =====================================================================
	// 5. O SSJ3 -- maestria e poder pessoal, e NADA de raiva
	// =====================================================================
	/// <summary>
	/// A regra do dono, ao pe da letra: *"`ssj3` NAO libera com raiva. Ele pede 50% de maestria do
	/// `ssj2` e o poder minimo pessoal"*. O DM concorda -- `Transformation Controls.dm:46`:
	/// `if(usr.ssj==2 &amp;&amp; usr.BP>=usr.ssj3at/10)` com `usr.ssj2mastery >= 50`.
	///
	/// AS DUAS COISAS, E NAO UMA OU OUTRA: por isso as checagens sao cruzadas (maestria paga e BP
	/// curto; BP de sobra e maestria curta). Um `||` no lugar do `&amp;&amp;` passaria em qualquer
	/// teste que so medisse "com tudo pago abre".
	///
	/// E O PODER E O **PESSOAL**: cada Saiyajin sorteia o proprio `ssj3at` ao nascer
	/// (`statsaiyan.dm:52`), entao a porta e diferente de irmao pra irmao. A checagem usa dois
	/// limiares sorteados de verdade e exige que cada um obedeca ao SEU numero -- e nao a constante
	/// de fabrica, que e o que um port apressado usaria.
	/// </summary>
	private static void OSsj3()
	{
		Console.WriteLine("[5] O SSJ3: 50% DE MAESTRIA NO SSJ2 + O PODER PESSOAL, E ZERO RAIVA");

		FormaDef ssj3 = Catalogo.Def("ssj3")!;
		Checa("o SSJ3 nao pede raiva nenhuma",
			  Catalogo.RaivaExigida(ssj3) == NivelDeRaiva.Nenhuma,
			  Catalogo.RaivaExigida(ssj3).ToString());
		Checa("...e ele saiu da raiva pelo `PedeMaestria`, sem ninguem cita-lo pelo nome",
			  ssj3.PedeMaestria >= Catalogo.Ssj3PedeSsj2Pct, $"PedeMaestria = {ssj3.PedeMaestria}");
		Checa("...e a maestria cobrada e a do SSJ2, dita e nao herdada",
			  ssj3.PedeMaestriaDe == "ssj2", $"'{ssj3.PedeMaestriaDe}'");

		var saiyajin = new PerfilDeFormas(Raca: "Saiyan", Linhagem: Oozaru.LinhagemPrimal);
		const double bpDeSobra = 1e15;

		EstadoDeForma EmSsj2(double maestria)
		{
			var e = new EstadoDeForma { Atual = "ssj2" };
			e.Maestria.Por("ssj2", maestria);
			return e;
		}

		Checa("com 49% de maestria no SSJ2: recusado por MAESTRIA",
			  EmSsj2(Catalogo.Ssj3PedeSsj2Pct - 1).Avaliar("ssj3", bpDeSobra, 1, false, saiyajin)
				  == RecusaForma.SemMaestria);
		Checa("com 50%: abre",
			  EmSsj2(Catalogo.Ssj3PedeSsj2Pct).Avaliar("ssj3", bpDeSobra, 1, false, saiyajin)
				  == RecusaForma.Pode);

		// A RAIVA NAO SUBSTITUI A MAESTRIA. Se um dia o SSJ3 voltar pra lista de raiva, esta linha
		// continua exigindo os 50% -- ou seja ela pega o afrouxamento mesmo que [1] seja "corrigido".
		Checa("furia extrema NAO abre o SSJ3 sem os 50% (raiva nao paga treino)",
			  EmSsj2(0).Avaliar("ssj3", bpDeSobra, 1, false,
								saiyajin with { Raiva = NivelDeRaiva.Extrema }) == RecusaForma.SemMaestria);

		// --- E O PODER, QUE E DESTE PERSONAGEM ------------------------------------------
		// Dois limiares SORTEADOS de verdade, e a bancada procura um par que difira: se `Rolar`
		// deixar de sortear (virar constante), a busca falha e a checagem diz isso em vez de
		// passar comparando um numero consigo mesmo.
		LimiaresPessoais a = LimiaresPessoais.Rolar("Saiyan", "Normal", 12345);
		LimiaresPessoais? b = null;
		for (ulong s = 12346; s < 12446 && b == null; s++)
		{
			LimiaresPessoais tentativa = LimiaresPessoais.Rolar("Saiyan", "Normal", s);
			if (Math.Abs(tentativa.Porta(ssj3) - a.Porta(ssj3)) > 1) b = tentativa;
		}
		Checa("dois Saiyajins sorteiam portas de SSJ3 DIFERENTES", b != null,
			  "cem sementes deram a mesma porta -- o limiar virou constante?");

		if (b != null)
		{
			double portaA = a.Porta(ssj3), portaB = b.Porta(ssj3);
			double maior = Math.Max(portaA, portaB), menor = Math.Min(portaA, portaB);

			EstadoDeForma comA = EmSsj2(Catalogo.Ssj3PedeSsj2Pct); comA.Limiares = a;
			EstadoDeForma comB = EmSsj2(Catalogo.Ssj3PedeSsj2Pct); comB.Limiares = b;
			EstadoDeForma quemEhMaior = portaA > portaB ? comA : comB;
			EstadoDeForma quemEhMenor = portaA > portaB ? comB : comA;

			// NO MESMO BP: um passa e o outro nao. E a prova de que quem manda e o limiar DESTE
			// personagem, e nao a constante -- com a constante os dois dariam a mesma resposta.
			double bpEntre = (menor + maior) / 2;
			Checa("no MESMO BP, quem sorteou a porta menor ja vira SSJ3",
				  quemEhMenor.Avaliar("ssj3", bpEntre, 1, false, saiyajin) == RecusaForma.Pode,
				  quemEhMenor.Avaliar("ssj3", bpEntre, 1, false, saiyajin).ToString());
			Checa("...e quem sorteou a maior ainda ouve FALTA PODER",
				  quemEhMaior.Avaliar("ssj3", bpEntre, 1, false, saiyajin) == RecusaForma.SemPoder,
				  quemEhMaior.Avaliar("ssj3", bpEntre, 1, false, saiyajin).ToString());

			// E COM A MAESTRIA PAGA, O QUE FALTA E O PODER -- as duas cobradas, nao uma delas.
			Checa("com a maestria paga e o BP curto, a recusa e por PODER",
				  quemEhMaior.Avaliar("ssj3", quemEhMaior.Limiares!.Porta(ssj3) - 1, 1, false, saiyajin)
					  == RecusaForma.SemPoder);
			Checa("e na porta exata dele, abre",
				  quemEhMaior.Avaliar("ssj3", quemEhMaior.Limiares!.Porta(ssj3), 1, false, saiyajin)
					  == RecusaForma.Pode);
		}

		Console.WriteLine();
	}

	// =====================================================================
	// 6. O SSJ4 E O OOZARU DOURADO
	// =====================================================================
	/// <summary>
	/// Regra do dono: *"`ssj4` NAO precisa de raiva -- pede dominar o Golden Oozaru. E o Golden
	/// Oozaru pede dominar o `ssj1` mais um poder minimo, tambem requisito pessoal"*.
	///
	/// ============================ O GATE DO DOURADO NAO PASSA PELO `Avaliar` ============================
	/// E o detalhe que faz esta secao existir separada: a linha do Oozaru e recusada INTEIRA pelo
	/// `Avaliar` (`NaoSeSobePraEla` -- "nao se sobe pro Oozaru, ele acontece por olhar a lua"),
	/// entao perguntar ao `Avaliar` se o Dourado abre responderia sempre `LinhaFechada` e nao
	/// mediria nada. Quem decide de verdade e `Oozaru.QualSai`, chamado pelo `Apeshit` da lua --
	/// e e por ELE que esta secao pergunta, que e a regra de ouro desta bancada.
	/// ================================================================================================
	/// </summary>
	private static void OSsj4EODourado()
	{
		Console.WriteLine("[6] O SSJ4 SEM RAIVA, E O DOURADO PEDINDO SSJ1 DOMINADO + PODER PESSOAL");

		FormaDef ssj4 = Catalogo.Def("ssj4")!, dourado = Catalogo.Def(Oozaru.IdDourado)!;

		Checa("o SSJ4 nao pede raiva nenhuma",
			  Catalogo.RaivaExigida(ssj4) == NivelDeRaiva.Nenhuma,
			  Catalogo.RaivaExigida(ssj4).ToString());
		Checa("o Oozaru Dourado nao pede raiva nenhuma (a lua nao e furia)",
			  Catalogo.RaivaExigida(dourado) == NivelDeRaiva.Nenhuma,
			  Catalogo.RaivaExigida(dourado).ToString());

		// A FURIA NAO SUBSTITUI A FERA. Com o luto aceso e sem o Dourado, o SSJ4 continua recusado --
		// e pela recusa CERTA, que e a que manda o jogador procurar a lua e nao uma briga.
		var primal = new PerfilDeFormas(Raca: "Saiyan", Linhagem: Oozaru.LinhagemPrimal,
										Raiva: NivelDeRaiva.Extrema);
		var emSsj3 = new EstadoDeForma { Atual = "ssj3" };
		foreach (FormaDef x in Catalogo.Todas) emSsj3.Maestria.Por(x.Id, 100);
		Checa("furia extrema NAO abre o SSJ4 sem o Dourado despertado",
			  emSsj3.Avaliar("ssj4", 1e15, 1, false, primal) == RecusaForma.SemFormaAnterior,
			  emSsj3.Avaliar("ssj4", 1e15, 1, false, primal).ToString());

		// --- O DOURADO, PELO CAMINHO DA LUA ---------------------------------------------
		Checa("a linha do Oozaru e recusada pelo `Avaliar` (nao se sobe pra ela)",
			  emSsj3.Avaliar(Oozaru.IdDourado, 1e15, 1, false, primal) == RecusaForma.LinhaFechada,
			  emSsj3.Avaliar(Oozaru.IdDourado, 1e15, 1, false, primal).ToString());

		// A SEMENTE E PROCURADA, NAO ESCOLHIDA. O `ultrassjat` sai de `rand(9,13)/10`
		// (`statsaiyan.dm:53`), entao uma em cada cinco sementes cai exatamente na constante de
		// fabrica -- e uma semente cravada a dedo transforma esta checagem numa moeda. Procurando,
		// ou a bancada acha um sorteio que difere, ou ela diz que o sorteio morreu.
		LimiaresPessoais? sorteado = null;
		for (ulong s = 1; s < 200 && sorteado == null; s++)
		{
			LimiaresPessoais t = LimiaresPessoais.Rolar("Saiyan", "Normal", s);
			if (Math.Abs(t.Porta(dourado) - dourado.PortaBp) > 1) sorteado = t;
		}
		Checa("o limiar do Dourado e SORTEADO por personagem", sorteado != null,
			  "duzentas sementes deram a constante de fabrica -- o sorteio virou numero fixo?");

		LimiaresPessoais meus = sorteado ?? LimiaresPessoais.Rolar("Saiyan", "Normal", 999);
		double porta = meus.Porta(dourado);
		Checa("o Dourado tem porta de poder DECLARADA no catalogo", dourado.PortaBp > 0,
			  $"PortaBp = {dourado.PortaBp}");
		Checa("...e o gate le a porta PESSOAL, e nao a constante de fabrica",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, Dominio(100), dourado.PortaBp, meus)
				  == (dourado.PortaBp >= porta ? RecusaOozaru.Pode : RecusaOozaru.SemPoder),
			  $"pessoal {porta:N0} contra fabrica {dourado.PortaBp:N0}");

		Maestrias Dominio(double ssj1) { var m = new Maestrias(); m.Por("ssj1", ssj1); return m; }

		// A ORDEM DAS RECUSAS E A MENSAGEM, igual a do `Avaliar`: quem nao e Primal nunca ouve
		// "falta BP" sobre uma forma que nao e dele.
		Checa("nao-Primal: recusado por LINHAGEM",
			  Oozaru.PodeDourado("Saiyan", Dominio(100), 1e15, meus)
				  == RecusaOozaru.SemLinhagemPrimal);
		Checa("Primal com SSJ1 em 99%: recusado por MAESTRIA (e nao por poder)",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, Dominio(99), 1e15, meus)
				  == RecusaOozaru.SemMaestriaSsj);
		Checa("Primal com SSJ1 DOMINADO e BP curto: recusado por PODER",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, Dominio(100), porta - 1, meus)
				  == RecusaOozaru.SemPoder);
		Checa("Primal com SSJ1 DOMINADO e o poder pessoal: PODE",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, Dominio(100), porta, meus)
				  == RecusaOozaru.Pode);

		// E A LUA -- o caminho de producao, o mesmo que o `Apeshit` chama. Sem esta linha, o gate
		// acima poderia estar certo e nao estar LIGADO em `QualSai`, que e o defeito assinatura
		// deste port: regra escrita, testada e nunca chamada.
		Checa("na lua, com poder curto, o Primal em SSJ vira NADA (nem o regular)",
			  Oozaru.QualSai(true, Oozaru.LinhagemPrimal, Dominio(100), porta - 1, meus)
				  == FormaOozaru.Nao);
		Checa("na lua, com o poder pessoal pago, ele vira DOURADO",
			  Oozaru.QualSai(true, Oozaru.LinhagemPrimal, Dominio(100), porta, meus)
				  == FormaOozaru.Dourado);
		Checa("e fora do SSJ a lua da o Oozaru comum, sem porta nenhuma",
			  Oozaru.QualSai(false, Oozaru.LinhagemPrimal, Dominio(0), 0, meus)
				  == FormaOozaru.Regular);

		// --- E O SSJ4 DEPOIS DA FERA, sem raiva nenhuma no caminho -----------------------
		var semRaiva = new PerfilDeFormas(Raca: "Saiyan", Linhagem: Oozaru.LinhagemPrimal);
		emSsj3.Liberar(Oozaru.IdDourado);
		Checa("com o Dourado dominado, o SSJ4 abre SEM raiva nenhuma",
			  emSsj3.Avaliar("ssj4", 1e15, 1, false, semRaiva) == RecusaForma.Pode,
			  emSsj3.Avaliar("ssj4", 1e15, 1, false, semRaiva).ToString());

		Console.WriteLine();
	}

	// =====================================================================
	// 7. NENHUMA DIVINA POR RAIVA, EXCETO O BEAST
	// =====================================================================
	/// <summary>
	/// Ordem do dono, com o motivo junto: **forma divina e tecnica e de mente calma; raiva e mortal
	/// e bruta**. Elas vem de alguem ENSINANDO (o ritual do Kaioshin no Mistico, a cadeia Anjo ->
	/// aluno do Ultra Instinto, o Deus da Destruicao no Ultra Ego) ou de MADURAR o ki divino.
	///
	/// ============================ POR QUE VARRER AS LINHAS E NAO CONFERIR O `default` ============================
	/// Porque "cai no `_ => Nenhuma`" e uma verdade da IMPLEMENTACAO, e implementacao muda. Varrendo
	/// as cinco linhas divinas a checagem afirma a REGRA: uma linha divina nova pendurada no braco
	/// errado do switch reprova aqui em vez de estrear com a cratera da furia e a porta da furia.
	///
	/// E O BEAST E A EXCECAO PORQUE ELE NAO E UM DEUS: ele e o que sai quando o Prodigial se RECUSA
	/// a virar deus (`godki.dm:349-352` -- o ki divino dele nao vira SSG/Blue, vira FERA), e no DM
	/// ele desperta pelo mesmo `Emotion == "Very Angry"` do SSJ1/SSJ2 e pelo mesmo `effector`
	/// (`Mystic.dm:65-67`).
	/// ========================================================================================================
	/// </summary>
	private static void AsDivinas()
	{
		Console.WriteLine("[7] NENHUMA FORMA DIVINA PEDE RAIVA -- MENOS O BEAST");

		LinhaDeForma[] divinas =
		[
			LinhaDeForma.GodKi, LinhaDeForma.GodKiRose, LinhaDeForma.Mistico,
			LinhaDeForma.UltraInstinct, LinhaDeForma.UltraEgo,
		];

		int olhadas = 0;
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (Array.IndexOf(divinas, d.Linha) < 0) continue;
			olhadas++;

			if (d.Id == "beast")
			{
				Checa("`beast` e a UNICA divina por raiva, e pela EXTREMA",
					  Catalogo.RaivaExigida(d) == NivelDeRaiva.Extrema,
					  Catalogo.RaivaExigida(d).ToString());
				continue;
			}

			Checa($"`{d.Id}` e divina: nao pede raiva (mente calma, nao furia)",
				  Catalogo.RaivaExigida(d) == NivelDeRaiva.Nenhuma,
				  Catalogo.RaivaExigida(d).ToString());

			// E PELO FUNIL: nao basta a derivacao dizer `Nenhuma`, o `Avaliar` tem que abrir a forma
			// pra um corpo em CALMA que pagou o que ela pede.
			(EstadoDeForma est, PerfilDeFormas perfil, double bp) = Preparado(d, NivelDeRaiva.Nenhuma);
			RecusaForma r = est.Avaliar(d.Id, bp, 1, false, perfil);
			Checa($"...e um corpo em calma nao ouve `SemFuria` por `{d.Id}`",
				  r != RecusaForma.SemFuria, r.ToString());
		}

		Checa($"a varredura olhou as {olhadas} entradas divinas (nao rodou no vazio)",
			  olhadas >= 10, $"{olhadas}");

		// E CADA LINHA DIVINA DECLARA COMO SE ABRE. E o par positivo da regra: dizer "nao e por
		// raiva" sem dizer "e por isto" deixaria a porta de uma linha nova simplesmente vazia.
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (Array.IndexOf(divinas, d.Linha) < 0 || d.Id == "beast") continue;
			bool porEnsino = d.SoPorConcessao || d.PedeEnergiaUe > 0 || d.PedeProficienciaUi > 0;
			bool porKiDivino = d.PedeGodKi >= 0;
			Checa($"`{d.Id}` abre por ENSINO ou por KI DIVINO", porEnsino || porKiDivino,
				  "nao declara nem concessao, nem energia, nem ki divino");
		}

		Console.WriteLine();
	}

	// =====================================================================
	// 8. O GANCHO DA AMIZADE TEM DONO
	// =====================================================================
	/// <summary>
	/// ============================ A UNICA CHECAGEM DESTA BANCADA QUE LE O CODIGO-FONTE ============================
	/// Ela tem que ler porque a afirmacao e sobre QUEM CHAMA -- e isso nao aparece em campo nenhum,
	/// tela nenhuma, tique nenhum. Ou se le o fonte, ou a frase e palavra minha.
	///
	/// ============================ ELA ERA O AVESSO DISTO, E VIROU ============================
	/// Ate a sessao do Known-People esta secao se chamava "O GANCHO DA AMIZADE E INERTE" e reprovava
	/// se aparecesse **qualquer** chamador de producao. Nao era um teste que envelheceu mal: era um
	/// MARCADOR, posto de proposito pra que o dia em que alguem ligasse a amizade fosse tambem o dia
	/// de vir aqui atualizar a conversa -- em vez de a frase "o gancho e inerte" continuar escrita
	/// nos comentarios do projeto meses depois de deixar de ser verdade.
	///
	/// **Ele disparou, e esta e a atualizacao.** Hoje o `Core.Social.Convivio` responde "quem e
	/// amigo de quem", e os chamadores sao dois: o `GameServer.LutoNaVizinhanca` (o amigo cai ou
	/// morre) e o `GameServer.LutoPelaAbsorcao` (o amigo e absorvido por um Majin). A fusao
	/// Namekuseijin NAO e um deles, por decisao do dono. O que a secao mede agora e o oposto e
	/// igualmente estrutural:
	///
	///   * o gancho TEM chamador de producao -- se alguem apagar o convivio, o SSJ1 volta a so sair
	///     por admin e ninguem perceberia, porque uma forma que nunca vem parece balanceamento;
	///   * e os chamadores sao SO ESSES DOIS. As duas janelas de raiva continuam sendo escritas
	///     exclusivamente pelo `AmigoAbatido` -- um terceiro lugar que mexesse nelas na mao seria a
	///     porta dos fundos que este projeto ja abriu varias vezes sem querer.
	///
	/// (A lista de chamadores esteve vermelha de 2026-10-09 a 2026-10-11: a absorcao do Majin e duas
	/// bancadas novas passaram a chamar o gancho e esta bancada, que nao esta no lancador comum, nao
	/// foi rodada. As bancadas agora entram pelo NOME do arquivo -- ver `EhBancada`.)
	/// ==========================================================================================
	/// </summary>
	private static void OGanchoLigado()
	{
		Console.WriteLine("[8] O GANCHO DA AMIZADE TEM DONO (varredura dos fontes)");

		string raiz = Directory.GetCurrentDirectory();
		var chamadores = new List<string>();
		var escritoresDaJanela = new List<string>();
		var escritoresDaRaiva = new List<string>();
		var funil = new List<string>();
		var tiqueDaRaiva = new List<string>();
		int definicoes = 0, chamadas = 0, projecoes = 0;

		// OS ARQUIVOS QUE PODEM FALAR DE RAIVA SEM SEREM O CHAMADOR: a definicao do proprio gancho
		// e as bancadas ao vivo. Qualquer outro arquivo que o chame e um chamador de producao.
		// A `GameServer.RaciaisTeste.cs` ENTROU NAS DUAS LISTAS, e pelo motivo de sempre: ela e a
		// bancada que sobe a escada de CADA raca pela tecla C, e a unica linha racial que pede raiva
		// (o Heran) so tem as duas metades medidas se a bancada puder ACENDER o luto (pelo gancho) e
		// APAGA-LO (pelas janelas) entre uma checagem e outra. O prazo e de dois minutos de relogio
		// real contra uma bancada que roda em segundos: sem apagar, o luto de um corpo atravessaria
		// pro seguinte e a recusa por falta de raiva passaria verde sem nunca ter sido medida -- que
		// foi exatamente o defeito da secao 2 da `RaivaTeste`. O codigo de PRODUCAO continua fora.
		//
		// A `GameServer.Trailer2.cs` E PALCO, COMO AS BANCADAS: a cena do primeiro Super Saiyajin do
		// trailer das sagas diz o que a lista social diria (o amigo que morre e um NPC que acabou de
		// nascer, e NPC nao esta na lista de ninguem). Ela chama o gancho; nao escreve janela nenhuma.
		//
		// AS BANCADAS ENTRAM PELO NOME (`...Teste.cs`), e nao mais uma a uma (2026-10-11): a lista escrita a
		// mao envelheceu calada -- duas bancadas novas (`DoisCorposTeste`, `EscudoTeste`) e a propria
		// absorcao do Majin passaram a chamar o gancho, ninguem veio aqui, e esta secao ficou vermelha sem
		// que o jogo tivesse defeito nenhum. Bancada e palco por definicao; o que se vigia e a PRODUCAO.
		string[] permitidos = ["GameServer.Formas.cs", "GameServer.Trailer2.cs"];
		static bool EhBancada(string arquivo) => arquivo.EndsWith("Teste.cs", StringComparison.Ordinal);

		// OS DOIS CHAMADORES DE PRODUCAO, e cada um por um caminho do DM: o LUTO do funil da derrota
		// (`Death.dm:73-85`, `KO.dm:29-41`) e a ABSORCAO do Majin (`MajinSaga.dm:169-175`). A do bio-androide
		// MATA a vitima e chega pelo primeiro; a fusao Namekuseijin nao chega por nenhum (ver mais abaixo).
		// (2026-10-21) E MAIS DOIS, que sao regra do dono e nao do DM: o CORPO ACHADO depois (`GameServer.RaivaDoCorpo.cs`, 2026-10-18 --
		// chamava o gancho desde entao, e esta lista nao sabia) e o ALIADO, o habitante recrutado pela amizade (`GameServer.Aliados.cs`,
		// `LutoDoAliado`: *"como amigo jogador"*). Os quatro chegam ao gancho; nenhum escreve janela.
		string[] chamadoresDeProducao = ["GameServer.Convivio.cs", "GameServer.AbsorcaoMajin.cs", "GameServer.RaivaDoCorpo.cs", "GameServer.Aliados.cs"];

		// QUEM PODE ESCREVER NAS DUAS JANELAS: so quem as define (o `AmigoAbatido` mora la) e as
		// bancadas, que as puxam pra tras pra nao esperar dois minutos de teste.
		//
		// ============================ O DISCIPULADO ABRIU O SEGUNDO ACENDEDOR, E ELE NAO ENTRA AQUI ============================
		// O mestre que PROVOCA o aluno (`mst_ignite_anger`, `MasterStudent.dm:540`) acende a mesma
		// janela sem que ninguem tenha morrido. **`GameServer.Mestre.cs` continua fora desta lista
		// de proposito**: ele nao escreve os campos -- chama a `AcenderJanelaDeRaiva`, que mora no
		// `GameServer.Formas.cs` junto do `AmigoAbatido` e continua sendo a mao unica (e a unica que
		// lembra de chamar a `ProjetarRaiva`). Se um dia `GameServer.Mestre.cs` aparecer nesta
		// lista, e porque alguem furou o funil.
		//
		// O que entra e a BANCADA dele, pelo mesmo motivo das outras tres: ela zera as janelas entre
		// as checagens pra provar que a tentativa fadada NAO acende raiva nenhuma. Vale pras DUAS
		// bancadas do discipulado -- a de corpos forjados (`--mestreteste`) e a de personagens de
		// verdade (`--mestrevivo`); o codigo de producao delas, o `GameServer.Mestre.cs`, continua
		// fora desta lista.
		// ================================================================================================================
		string[] podemEscreverAJanela = ["GameServer.cs", "GameServer.Formas.cs"];

		// ============================ E QUEM PODE ESCREVER O `Anger` -- A OUTRA METADE DA MESMA REGRA ============================
		// O `Fighter.Anger` e DERIVADO das duas janelas (`GameServer.RaivaComoNumero`), e a unica
		// mao que o escreve e a `ProjetarRaiva`. Uma segunda mao seria pior que a porta dos fundos
		// da janela: `angerBuff` chega a 3x, entao um `Anger = MaxAnger` solto em qualquer arquivo
		// e ate 3x de BP que NUNCA DECAI -- e foi exatamente esse risco que manteve este sistema
		// desligado por varias sessoes. Sao tres arquivos, e cada um por um motivo diferente:
		//   * `Fighter.cs`         -- a declaracao do campo e o `ClampAnger` (o teto, que so corta);
		//   * `GameServer.Formas.cs` -- a `ProjetarRaiva`, a mao unica;
		//   * `GameServer.RaivaTeste.cs` -- a bancada ao vivo, que REPOE o valor de antes no `finally`.
		string[] podemEscreverARaiva =
			["Fighter.cs", "GameServer.Formas.cs", "GameServer.RaivaTeste.cs"];

		foreach (string dir in new[] { "Core", "Server", "Client" })
		{
			string caminho = Path.Combine(raiz, dir);
			if (!Directory.Exists(caminho)) continue;

			foreach (string arq in Directory.EnumerateFiles(caminho, "*.cs", SearchOption.AllDirectories))
			{
				string nome = Path.GetFileName(arq);
				string[] linhas = File.ReadAllLines(arq);
				for (int i = 0; i < linhas.Length; i++)
				{
					string l = linhas[i].Trim();
					if (l.StartsWith("//") || l.StartsWith("///")) continue;   // comentario

					if (l.Contains("internal bool AmigoAbatido")) { definicoes++; continue; }

					if (l.Contains("AmigoAbatido("))
					{
						chamadas++;
						if (Array.IndexOf(permitidos, nome) < 0 && !EhBancada(nome)) chamadores.Add($"{nome}:{i + 1}");
					}

					// E AS DUAS JANELAS DIRETAMENTE: quem quiser burlar o gancho escreveria no campo.
					bool escreve = (l.Contains("FuriaExtremaAte") || l.Contains("RaivaComumAte") || l.Contains("IrritadoAte"))
								   && l.Contains('=') && !l.Contains("==") && !l.Contains(">");
					if (escreve && Array.IndexOf(podemEscreverAJanela, nome) < 0 && !EhBancada(nome))
						escritoresDaJanela.Add($"{nome}:{i + 1}");

					// E O `Anger` EM SI. `\bAnger\s*=[^=]` pega o campo e so ele: `MaxAnger`,
					// `baseAnger` e `legendaryAngerBonus` nao tem fronteira de palavra antes do `A`,
					// e o `[^=]` deixa a comparacao `Anger ==` passar.
					//
					// APAGAR PODE EM QUALQUER LUGAR; ACENDER, NAO. `Anger = 100` e o PISO -- e
					// `angerBuff = 1,0x`, ou seja "este corpo esta calmo". O DM escreve exatamente
					// isso em cinco lugares que fabricam ou reciclam corpos
					// (`PlanetPopulation.dm:334`, `BossEvents.dm:240`, `Tournament.dm:189` e `:742`,
					// `PlanetConquest.dm:695`), e nenhum deles e uma porta dos fundos: nao existe
					// exploit em ficar calmo. O que esta regra protege e o outro sentido -- um
					// `Anger = MaxAnger` solto e ate 3x de BP que ninguem baixa.
					if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\bAnger\s*=[^=]")
						&& !System.Text.RegularExpressions.Regex.IsMatch(l, @"\bAnger\s*=\s*100\s*;")
						&& Array.IndexOf(podemEscreverARaiva, nome) < 0)
						escritoresDaRaiva.Add($"{nome}:{i + 1}");

					// E A PROJECAO: quem a define, e quem a chama DO TIQUE. Sem a chamada do tique o
					// `Anger` volta a ser o campo morto que era -- e o sintoma seria zero: nenhum
					// erro, nenhuma tela errada, so um sistema inteiro valendo 1,0x pra sempre.
					if (l.Contains("private void ProjetarRaiva")) { projecoes++; continue; }
					if (l.Contains("ProjetarRaiva(")) tiqueDaRaiva.Add(nome);

					// E QUEM CHAMA O FUNIL DA DERROTA. Ele e o unico caminho entre "perdeu a luta" e
					// o luto -- ver `AoPerderALuta`.
					if (l.Contains("AoPerderALuta(") && !l.Contains("private void AoPerderALuta"))
						funil.Add(nome);
				}
			}
		}

		Checa("a varredura achou a definicao do gancho (rodou da raiz do repo)", definicoes == 1,
			  $"{definicoes} definicoes -- rodou da pasta errada?");
		Checa("a varredura achou chamadas do gancho", chamadas >= 3, $"{chamadas}");

		// O CORACAO DESTA SECAO. Se isto reprovar com zero, o SSJ1 voltou a ser um verb de admin.
		Checa("o gancho TEM chamador de producao (o luto do convivio)", chamadores.Count > 0,
			  "nenhum -- o SSJ1 voltou a so sair por admin");
		Checa("os chamadores de producao sao o LUTO (`GameServer.Convivio.cs`), a ABSORCAO (`GameServer.AbsorcaoMajin.cs`), o CORPO ACHADO (`GameServer.RaivaDoCorpo.cs`) e o ALIADO (`GameServer.Aliados.cs`), e so eles",
			  chamadores.All(c => chamadoresDeProducao.Any(p => c.StartsWith(p + ":")))
			  && chamadoresDeProducao.All(p => chamadores.Any(c => c.StartsWith(p + ":"))),
			  string.Join(" | ", chamadores));

		// ============================ AS ABSORCOES QUE CHEGAM AO GANCHO -- E A QUE NAO CHEGA (dono, 2026-10-11) ============================
		// *"ao ver um amigo morrer ou ser absorvido"*: a do bio mata a vitima pelo funil da derrota, e a do Majin
		// chama o luto antes de levar a vitima pro bolsao. A FUSAO NAMEKUSEIJIN NAO: *"fusao namek n deveria dar o
		// trigger na raiva"* -- ela e consentida, e no DM tambem nao acende raiva (`Fusion.dm:301-310`). Uma rodada
		// deste trabalho ligou o luto dentro dela, e esta checagem cobrava isso; virou do avesso junto.
		//
		// A bancada ao vivo (`--raivateste`, secao 4) roda a fusao de verdade na frente de quem declarou o absorvido
		// amigo. Aqui se le o CORPO DO METODO, que e onde uma chamada de luto voltaria a morar -- e a mesma
		// varredura e passada no fonte do Majin pra provar que ela sabe achar uma.
		static bool AcendeRaiva(IEnumerable<string> linhas) => linhas.Any(l =>
		{
			string s = l.Trim();
			return !s.StartsWith("//")
				   && (s.Contains("LutoPela") || s.Contains("LutoNaVizinhanca(") || s.Contains("AmigoAbatido(")
					   || s.Contains("AoPerderALuta(") || s.Contains("AcenderJanelaDeRaiva("));
		});
		string[] namek = LerFonte(raiz, "Server", "GameServer.Namekuseijin.cs");
		string[] corpoDaFusao = CorpoDoMetodo(namek, "private void AbsorverNamekuseijin(");
		Checa("o corpo do `AbsorverNamekuseijin` foi extraido do fonte (a assinatura ainda bate, e e nele que o personagem e apagado)",
			  corpoDaFusao.Length > 40 && corpoDaFusao.Any(l => l.Contains("ApagarOPersonagemParaSempre(absorvido")),
			  $"{corpoDaFusao.Length} linhas");
		Checa("a fusao Namekuseijin NAO acende raiva em ninguem: o corpo dela nao chama luto, gancho, funil da derrota nem janela",
			  corpoDaFusao.Length > 40 && !AcendeRaiva(corpoDaFusao),
			  string.Join(" | ", corpoDaFusao.Where(l => AcendeRaiva([l])).Select(l => l.Trim())));
		string[] majinFonte = LerFonte(raiz, "Server", "GameServer.AbsorcaoMajin.cs");
		Checa("(contra-exemplo) a MESMA varredura, no fonte da absorcao do Majin, ACHA o luto -- ela sabe reprovar",
			  AcendeRaiva(majinFonte));
		int lutoDoMajin = Array.FindIndex(majinFonte, l => l.Trim().StartsWith("LutoPelaAbsorcao(m, pl)"));
		int viagem = Array.FindIndex(majinFonte, l => l.Contains("MoveToZone(m.Id, interior"));
		Checa("a absorcao do Majin avisa quem viu ANTES de levar a vitima pro bolsao (`MajinSaga.dm:169`)",
			  lutoDoMajin >= 0 && viagem > lutoDoMajin, $"luto na linha {lutoDoMajin + 1}, viagem na {viagem + 1}");
		string[] bio = LerFonte(raiz, "Server", "GameServer.Absorcao.cs");
		Checa("a absorcao do bio-androide mata a vitima pelo funil da derrota (`AoPerderALuta(alvo, pl, morreu: true)`)",
			  bio.Any(l => l.Contains("AoPerderALuta(alvo, pl, morreu: true)")));

		// E O SALTO DE KI TEM UMA MAO SO: definido uma vez, chamado do gancho (e de mais ninguem em producao).
		string[] formasFonte = LerFonte(raiz, "Server", "GameServer.Formas.cs");
		string[] raivaFonte = LerFonte(raiz, "Server", "GameServer.Raiva.cs");
		Checa("o salto de Ki da furia e chamado DO GANCHO (`AmigoAbatido`), e definido num lugar so",
			  formasFonte.Count(l => l.Trim().StartsWith("SaltoDeKiDaFuria(enlutado, grau)")) == 1
			  && raivaFonte.Count(l => l.Contains("private void SaltoDeKiDaFuria(")) == 1,
			  "esperado: 1 chamada em GameServer.Formas.cs, 1 definicao em GameServer.Raiva.cs");

		// ============================ O DESPERTAR AUTOMATICO PASSA PELA ESTREIA DE SEMPRE (dono, 2026-10-11) ============================
		// *"ao bater reqs de transformacao q precisam de raiva, o personagem se transforma AUTOMATICAMENTE"*. Sao
		// afirmacoes sobre QUEM CHAMA O QUE, e nenhuma deixa rastro que uma bancada de servidor consiga distinguir
		// de um atalho: o despertar anda no TIQUE da forma (e um estado, nao um instante); ele entra pelo
		// `EntrarNaForma`, o mesmo caminho da tecla C -- e de la que saem a cinematica de estreia, o tema, o marco
		// e o anuncio pra zona --, tanto a forma nova quanto os degraus que ja sao dele (esses sem cena, e ANTES);
		// e a previsao que cala a cena da raiva pergunta pelo MESMO plano que ele vai executar (com os grades
		// ligados o seletor da tecla aponta outro degrau, e as duas cenas tocariam).
		Checa("o despertar pela raiva e chamado do TIQUE da forma (`TickDaForma`), e definido num lugar so",
			  formasFonte.Count(l => l.Trim() == "DespertarPelaRaiva(pl);") == 1
			  && raivaFonte.Count(l => l.Contains("private bool DespertarPelaRaiva(")) == 1,
			  "esperado: 1 chamada em GameServer.Formas.cs, 1 definicao em GameServer.Raiva.cs");
		int entraNoAlvo = Array.FindIndex(raivaFonte, l => l.Trim() == "EntrarNaForma(pl, plano.Alvo);");
		int sobeOsDegraus = Array.FindIndex(raivaFonte, l => l.Trim() == "foreach (FormaDef degrau in plano.Degraus) EntrarNaForma(pl, degrau, semCena: true);");
		Checa("...ele entra na forma pelo `EntrarNaForma` (o caminho da estreia), e nao escrevendo a forma na mao",
			  entraNoAlvo >= 0 && !raivaFonte.Any(l => !l.TrimStart().StartsWith("//") && l.Contains("Forma.Entrar(")));
		Checa("...os degraus que ja sao dele entram pelo MESMO caminho, SEM cena, e ANTES da forma que estreia",
			  sobeOsDegraus >= 0 && entraNoAlvo > sobeOsDegraus, $"degraus na linha {sobeOsDegraus + 1}, estreia na {entraNoAlvo + 1}");
		Checa("...a previsao que cala a cena da raiva pergunta pelo MESMO plano que o despertar executa (nunca as duas cenas)",
			  formasFonte.Any(l => l.Contains("&& (PlanoDaRaiva(pl) != null"))
			  && raivaFonte.Any(l => l.Contains("if (PlanoDaRaiva(pl) is not { } plano) return false;")));
		Checa("...e o plano e o do Core, pedido num lugar so (`SubidaPelaRaiva.Planejar`)",
			  raivaFonte.Count(l => !l.TrimStart().StartsWith("//") && l.Contains("SubidaPelaRaiva.Planejar(")) == 1);

		// ============================ MEDITAR ACALMA, E SO PELA PORTA CONFERIDA (dono, 2026-10-11) ============================
		// `Meditate.dm:27-31`. A ordem e a regra -- a raiva so some quando a meditacao foi ACEITA pelo servidor --, e
		// a mao que apaga as janelas mora ao lado da que acende. A bancada ao vivo mede o resultado; isto mede quem
		// chama o que, que e o que sobra quando alguem "simplifica" o `DefinirAtividade`.
		string[] atividade = CorpoDoMetodo(LerFonte(raiz, "Server", "GameServer.Oozaru.cs"), "private void DefinirAtividade(");
		int aceite = Array.FindIndex(atividade, l => l.Contains("!AceitarTreinoOuMeditacao("));
		int calma = Array.FindIndex(atividade, l => l.Trim() == "if (comecaAMeditar) AcalmarPelaMeditacao(a);");
		Checa("meditar acalma dentro do `DefinirAtividade`, DEPOIS da conferencia do servidor (`AceitarTreinoOuMeditacao`)",
			  aceite >= 0 && calma > aceite, $"conferencia na linha {aceite + 1} do metodo, calma na {calma + 1}");
		Checa("...e a mao que apaga as duas janelas e uma so, ao lado da que acende (`AcalmarPelaMeditacao`, `GameServer.Formas.cs`)",
			  formasFonte.Count(l => l.Contains("private bool AcalmarPelaMeditacao(")) == 1
			  && formasFonte.Count(l => l.Contains("private void AcenderJanelaDeRaiva(")) == 1);

		Checa("ninguem escreve as duas janelas de raiva por fora do gancho",
			  escritoresDaJanela.Count == 0, string.Join(" | ", escritoresDaJanela));
		string[] irritadoFonte = LerFonte(raiz, "Server", "GameServer.Irritado.cs");
		Checa("o IRRITADO acende pela mao unica (`AcenderJanelaDeRaiva`), e o gatilho mora em `GameServer.Irritado.cs`",
			  irritadoFonte.Any(l => l.Contains("AcenderJanelaDeRaiva(pl, NivelDeRaiva.Irritado,"))
			  && irritadoFonte.Any(l => l.Contains("GatilhoDoIrritado.Acende("))
			  && !irritadoFonte.Any(l => !l.TrimStart().StartsWith("//") && l.Contains("IrritadoAte") && l.Contains('=') && !l.Contains("==")),
			  "a janela do irritado so pode ser escrita em GameServer.Formas.cs");

		// ============================ A RAIVA COMO NUMERO TEM UMA MAO SO, E ELA ANDA ============================
		Checa("ninguem ACENDE o `Anger` por fora da projecao (apagar pra 100 pode em qualquer lugar)",
			  escritoresDaRaiva.Count == 0, string.Join(" | ", escritoresDaRaiva));
		Checa("a projecao da raiva existe, e e uma so", projecoes == 1, $"{projecoes} definicoes");

		// SE ISTO REPROVAR, o `angerBuff` volta a valer 1,0x pra sempre e nada mais quebra: e o modo
		// de falha mais caro deste sistema, porque ele nao tem sintoma.
		Checa("a projecao e chamada DO TIQUE (senao o `Anger` vira campo morto de novo)",
			  tiqueDaRaiva.Contains("GameServer.cs"), string.Join(" | ", tiqueDaRaiva.Distinct()));
		Checa("...e tambem do gancho do luto (a raiva vale no instante, e nao no proximo tique)",
			  tiqueDaRaiva.Contains("GameServer.Formas.cs"),
			  string.Join(" | ", tiqueDaRaiva.Distinct()));

		// ============================ E O FUNIL E CHAMADO DOS DOIS LUGARES QUE RESOLVEM DERROTA ============================
		// O luto so acontece se `AoPerderALuta` for chamado, e ele so e chamado de onde uma derrota
		// e resolvida. Sao dois caminhos no port -- o SOCO (`GameServer.Combat.cs`) e o DANO EM AREA
		// das tecnicas (`GameServer.Tecnicas.G3.cs`) --, e o modo de falha aqui e o de sempre: ligar
		// num e esquecer o outro. Com esta checagem, morrer de Kamehameha na frente de um amigo nao
		// pode deixar de enfurece-lo em silencio.
		// ====================================================================================================================
		Checa("o funil da derrota e chamado do combate corpo a corpo",
			  funil.Contains("GameServer.Combat.cs"), string.Join(" | ", funil.Distinct()));
		Checa("...e tambem do dano em area das tecnicas",
			  funil.Contains("GameServer.Tecnicas.G3.cs"), string.Join(" | ", funil.Distinct()));

		// E O ZERO DO PERFIL CONTINUA SENDO O LADO SEGURO: um perfil que nascesse `Extrema` deixaria
		// o jogo inteiro destravado e nenhuma outra checagem desta bancada notaria (todas montam o
		// perfil a mao).
		Checa("o perfil padrao nasce em calma (o zero do struct e o lado que RECUSA)",
			  default(PerfilDeFormas).Raiva == NivelDeRaiva.Nenhuma
			  && PerfilDeFormas.Comum.Raiva == NivelDeRaiva.Nenhuma
			  && PerfilDeFormas.MeioSangue.Raiva == NivelDeRaiva.Nenhuma);
		Checa("e a calma e o MENOR nivel (a comparacao `>=` depende dessa ordem)",
			  NivelDeRaiva.Nenhuma < NivelDeRaiva.Irritado
			  && NivelDeRaiva.Irritado < NivelDeRaiva.Comum
			  && NivelDeRaiva.Comum < NivelDeRaiva.Extrema);

		Console.WriteLine("  -> quem acende a raiva e ver um amigo cair ou morrer na sua frente.");
		Console.WriteLine("     Quem responde 'e amigo?' e o Core.Social.Convivio.");
		Console.WriteLine();
	}

	// =====================================================================
	// 9. COBERTURA: NENHUMA FORMA SEM PORTA
	// =====================================================================
	/// <summary>
	/// ============================ O DEFEITO QUE ESTA SECAO EXISTE PRA PEGAR ============================
	/// Um degrau novo acrescentado ao catalogo **sem porta nenhuma** e alcancavel de graca -- e ele
	/// nao aparece como bug: aparece como uma forma que abre cedo demais, o que qualquer um confunde
	/// com balanceamento. Ja aconteceu neste projeto: a entrada `oozaru` nao tinha porta alguma e
	/// virou o degrau MAIS FORTE disponivel pra quem nem SSJ1 tinha, e a tecla C a oferecia.
	///
	/// ============================ SAO DUAS PASSADAS, E A SEGUNDA E A QUE VALE ============================
	///   * PASSADA A (declarativa): toda entrada declara pelo menos uma porta -- raiva, maestria,
	///     poder, forma anterior, ensino, ki divino, sangue, ou "a lua" (o Oozaru, que nao se sobe).
	///     Barata, e pega o campo esquecido.
	///   * PASSADA B (pelo `Avaliar`): o corpo do "almoco de graca" -- linha aberta, sangue casado,
	///     no degrau anterior, Ki cheio, e **nada conquistado** (maestria zero, nenhuma forma
	///     liberada, ki divino nao desperto, energias zeradas, calma). O BP entra logo ABAIXO da
	///     porta pessoal quando a entrada tem uma, e infinito quando nao tem. Nenhuma forma pode
	///     dar `Pode`. Esta e a que prova que a porta declarada esta LIGADA.
	///
	/// ============================ AS DUAS UNICAS ISENCOES, E POR QUE SAO ISENTAS ============================
	/// `ssg` e `rose_ssg` sao os degraus de ENTRADA das escadas divinas: a porta deles e o proprio
	/// despertar do ki divino, que o corpo desta passada nao tem -- e por isso eles sao recusados
	/// por `LinhaFechada` na passada B com o ki divino apagado. A isencao esta escrita a mao e nao
	/// derivada de proposito: uma terceira forma que virar "gratuita" tem que aparecer aqui como
	/// FALHA, e nao ser absorvida por uma regra generica.
	/// ================================================================================================
	/// </summary>
	private static void Cobertura()
	{
		Console.WriteLine("[9] COBERTURA: TODA FORMA TEM PORTA DECLARADA, E ELA DISPARA");

		// --- PASSADA A: a porta esta DECLARADA -------------------------------------------
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == Catalogo.IdBase) continue;

			var portas = new List<string>();
			if (Catalogo.RaivaExigida(d) != NivelDeRaiva.Nenhuma)
				portas.Add($"raiva {Catalogo.RaivaExigida(d)}");
			if (d.PedeMaestria > 0)
				portas.Add($"maestria {d.PedeMaestria:0}% em {(d.PedeMaestriaDe.Length > 0
					? d.PedeMaestriaDe : Catalogo.IdAnterior(d))}");
			if (d.PortaBp > 0) portas.Add($"poder ({d.ChaveDoLimiar})");
			if (d.PedeFormaDespertada.Length > 0) portas.Add($"forma {d.PedeFormaDespertada}");
			if (d.PedeFormaAtual.Length > 0) portas.Add("estar noutra forma");
			if (Catalogo.IdAnterior(d) != Catalogo.IdBase) portas.Add($"degrau {Catalogo.IdAnterior(d)}");
			if (d.SoPorConcessao) portas.Add("ensino (concessao)");
			if (d.PedeEnergiaUe > 0) portas.Add("ensino (Ultra Ego)");
			if (d.PedeProficienciaUi > 0) portas.Add("ensino (Ultra Instinto)");
			if (d.PedeGodKi >= 0) portas.Add($"ki divino {d.PedeGodKi:0}%");
			if (d.PedeLinhagem.Length > 0 || d.PedeClasseUmaDe.Length > 0 || d.PedeOrigemUmaDe.Length > 0)
				portas.Add("sangue");
			if (Catalogo.NaoSeSobePraEla(d)) portas.Add("a lua (nao se sobe)");
			// A FURY DO DEUS DA DESTRUICAO (a linha entrou no catalogo em 2026-10-11): a porta dela nao e campo
			// nenhum da entrada -- e o TITULO, que mora na conta, e quem a liga e o servidor
			// (`GameServer.KitDoDeus`). O funil recusa a escada inteira pra todo mundo, e a passada B confere.
			if (Catalogo.SoPeloTitulo(d)) portas.Add("o titulo de Deus da Destruicao (nao se sobe)");
			// (2026-10-16) A FORMA PURA DO CORRUPTED MAJIN: a porta e a SAGA (as quatro formas e o BP base), e o corpo vai pra ela como
			// repouso -- o C nao a alcanca (`EstadoDeForma.Avaliar` recusa a linha inteira). A passada B confere.
			if (Catalogo.FormaDoMajinPuro(d)) portas.Add("a saga do Corrupted Majin (nao se sobe)");

			// ============================ E HA UM DEGRAU QUE NAO SE CONQUISTA: O REPOUSO ============================
			// A forma BASE do Frost Demon nao cobra nada, e nao e um esquecimento: e onde o corpo dele
			// esta quando nao esta transformado (`Catalogo.PisoDaEscada`) -- o equivalente, pra a raca
			// dele, do que a `base` e pra todo mundo. Ela so nao E a `base` porque tem sprite proprio e
			// porque o Mutante repousa ainda mais abaixo, em 0,25x.
			//
			// **NAO E UMA ISENCAO POR ID.** A pergunta e do catalogo e ela e estreita: `Mult <= 1` e
			// nenhuma porta declarada. Nenhuma transformacao do jogo cabe ai -- a mais fraca de todas
			// vale 1,35x --, entao um degrau REALMENTE gratuito continua caindo como falha, que e o
			// motivo desta varredura existir.
			// ==================================================================================================
			if (Catalogo.PodeSerRepouso(d)) portas.Add("repouso (nao se conquista)");

			Checa($"`{d.Id}` declara porta: {string.Join(" + ", portas)}", portas.Count > 0,
				  "NENHUMA porta declarada -- este degrau e de graca");
		}

		// --- PASSADA B: a porta DISPARA pelo funil do jogo -------------------------------
		// AS ISENCOES. Ver o cabecalho: sao os degraus cuja unica porta e o despertar do ki divino.
		string[] isentas = ["ssg", "rose_ssg"];

		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == Catalogo.IdBase) continue;

			(EstadoDeForma est, PerfilDeFormas perfil, double bpTeto) = Preparado(d, NivelDeRaiva.Nenhuma);

			// TIRA TUDO O QUE SE CONQUISTA. O `Preparado` paga os gates de proposito; aqui a
			// bancada os DESPAGA, mantendo so a identidade (linha aberta, sangue casado) e a
			// posicao na escada -- que sao o que um jogador tem de graca por ter nascido.
			foreach (FormaDef x in Catalogo.Todas) est.Maestria.Por(x.Id, 0);
			est.Liberadas.Clear();
			FormaDef? ant = Catalogo.Anterior(d);
			string onde = d.PedeFormaAtual.Length > 0 ? d.PedeFormaAtual[0] : ant?.Id ?? Catalogo.IdBase;
			if (onde != Catalogo.IdBase) est.Liberar(onde);
			if (ant != null && ant.Id != Catalogo.IdBase) est.Liberar(ant.Id);
			est.Entrar(onde);
			perfil = perfil with { GodKi = -1, EnergiaUe = 0, ProficienciaUi = 0 };

			// O BP LOGO ABAIXO DA PORTA, quando ha porta. Com BP infinito uma forma gateada SO por
			// poder passaria -- e poder e porta legitima, entao o teste tem que respeita-la em vez
			// de fura-la.
			double bp = d.PortaBp > 0 ? d.PortaBp - 1 : bpTeto;

			RecusaForma r = est.Avaliar(d.Id, bp, 1, false, perfil);

			// ============================ O REPOUSO E ISENTO NAS **DUAS** PASSADAS ============================
			// A passada A ja aceitava `PodeSerRepouso` como porta declarada ("nao se conquista"); esta
			// nao perguntava, e sobrevivia por acidente: o `Preparado` cravava `Raca: "Saiyan"` e as
			// sete formas do Frost Demon caiam em `LinhaFechada` antes de chegar aqui. Com a raca vindo
			// da linha (ver o `Preparado`), elas passaram a ser ALCANCAVEIS -- que e o certo, e e o que
			// repouso significa -- e a passada B as reprovou.
			//
			// A mesma pergunta do catalogo nas duas, e nao uma segunda lista escrita a mao: se a
			// definicao de repouso mudar, as duas passadas mudam juntas. Uma isencao por id aqui era
			// exatamente o que o comentario da passada A diz pra nao fazer.
			// =============================================================================================
			bool isenta = Array.IndexOf(isentas, d.Id) >= 0 || Catalogo.PodeSerRepouso(d);

			Checa($"`{d.Id}` e recusado a quem nao conquistou nada -> {r}",
				  isenta || r != RecusaForma.Pode,
				  "ALCANCAVEL DE GRACA: linha aberta, degrau anterior, e nada mais");

			// E A RECUSA TEM QUE SER UMA PORTA DE VERDADE, e nao um acidente do corpo de teste.
			// Sem esta linha, "recusado" passaria tambem por `SemKi` ou `Caido` -- estados
			// passageiros que qualquer jogador desfaz em dez segundos e que nao gateiam nada.
			if (!isenta)
				Checa($"...e `{r}` e porta e nao acidente",
					  r is not (RecusaForma.SemKi or RecusaForma.Caido or RecusaForma.JaEsta),
					  $"`{d.Id}` so escapou por um estado passageiro");
		}

		// ============================ PASSADA B2: A METADE DIVINA, COM A LINHA ABERTA ============================
		// A passada B acima recusa TODA forma divina por `LinhaFechada`, porque o corpo dela nao
		// despertou o ki divino. Isso e verdade e e uma porta -- mas e uma porta SO, e ela esconde
		// as outras: com `LinhaFechada` respondendo por doze entradas, um degrau divino novo sem
		// porta nenhuma passaria batido.
		//
		// Aqui o corpo DESPERTA o ki divino em 0% de maestria -- o minimo que existe, e literalmente
		// o que um jogador ganha no instante em que aprende -- e nada alem disso. Dai em diante so
		// as duas entradas de ENTRADA (`ssg`, `rose_ssg`) podem abrir: o resto tem que cobrar
		// maestria divina, energia, ensino ou o degrau anterior.
		// ====================================================================================================
		LinhaDeForma[] divinas =
		[
			LinhaDeForma.GodKi, LinhaDeForma.GodKiRose, LinhaDeForma.Mistico,
			LinhaDeForma.UltraInstinct, LinhaDeForma.UltraEgo,
		];

		foreach (FormaDef d in Catalogo.Todas)
		{
			if (Array.IndexOf(divinas, d.Linha) < 0) continue;

			(EstadoDeForma est, PerfilDeFormas perfil, double bpTeto) = Preparado(d, NivelDeRaiva.Nenhuma);
			foreach (FormaDef x in Catalogo.Todas) est.Maestria.Por(x.Id, 0);
			est.Liberadas.Clear();

			// O `PedeProficienciaUi`/`PedeEnergiaUe` mora no perfil, mas a LINHA de UI/UE so abre
			// com 70% de ki divino -- entao aqui o ki divino desperta em 0 e as duas linhas ficam
			// fechadas de novo. Pra elas o corpo recebe os 70% (que e maestria divina, uma porta
			// declarada e paga) e continua sem energia nenhuma: o que sobra pra recusar e o ENSINO.
			double godki = d.Linha is LinhaDeForma.UltraInstinct or LinhaDeForma.UltraEgo
				? Catalogo.GodkiUiUePct : 0;
			perfil = perfil with { GodKi = godki, EnergiaUe = 0, ProficienciaUi = 0 };

			FormaDef? ant = Catalogo.Anterior(d);
			string onde = d.PedeFormaAtual.Length > 0 ? d.PedeFormaAtual[0] : ant?.Id ?? Catalogo.IdBase;
			if (onde != Catalogo.IdBase) est.Liberar(onde);
			if (ant != null && ant.Id != Catalogo.IdBase) est.Liberar(ant.Id);
			est.Entrar(onde);

			RecusaForma r = est.Avaliar(d.Id, d.PortaBp > 0 ? d.PortaBp - 1 : bpTeto, 1, false, perfil);
			bool isenta = Array.IndexOf(isentas, d.Id) >= 0;

			Checa($"divina `{d.Id}` com ki divino recem-desperto -> {r}",
				  isenta ? r == RecusaForma.Pode : r != RecusaForma.Pode,
				  isenta ? "a porta de entrada da escada divina deixou de abrir"
						 : "ALCANCAVEL so por ter despertado o ki divino");
		}

		Console.WriteLine();
	}

	// =====================================================================
	// 10. A CINEMATICA DA FURIA -- `AngerCinematic()`, `Murder.dm:136-163`
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTA SECAO MEDE, E O QUE ELA **NAO** MEDE ============================
	/// Ela mede o ROTEIRO (o dado do Core) e o ENCANAMENTO (varredura de fontes). O desenho -- a
	/// camera tremendo, o anel abrindo, a pedra nascendo -- e do tocador e quem o mede e o
	/// `--diagforma`, ao vivo, com o node na arvore. Repetir aqui as contas de la seria o "node
	/// isolado" que esta bancada inteira recusa.
	///
	/// ============================ AS QUATRO REGRAS DE CENA QUE ELA TRANCA ============================
	/// Sao as que este projeto pagou uma vez cada, e todas as quatro valem tambem pra uma cena que
	/// nao e de forma nenhuma:
	///   * a CRATERA cai no beat da virada -- e em nenhum outro;
	///   * a POEIRA so vem com a cratera (e a cauda, que e a mesma poeira baixando);
	///   * a PEDRA fica do inicio ao fim (`OChaoSeSolta`), e nao e beat;
	///   * a `PoeiraDeEstrago` NAO e chamada de cinematica -- o bit dela esta aposentado, e a
	///     varredura de fontes confere que o tocador continua sem chamar aquele sistema.
	///
	/// A primeira e a segunda sao conferidas no RESULTADO e nao na intencao: eu construo uma cena de
	/// sonda escrevendo cratera e poeira nos lugares errados, e o funil da `Cinematica.Beats` tem que
	/// apaga-las. Conferir a `Cinematicas.Furia` sozinha provaria que eu escrevi o roteiro certo --
	/// nao que o funil impede o roteiro errado, que e a regra de verdade.
	/// ==========================================================================================
	/// </summary>
	private static void ACinematicaDaFuria()
	{
		Console.WriteLine("[10] A CINEMATICA DA FURIA (o roteiro, as 4 regras de cena, o encanamento)");

		Cinematica c = Cinematicas.Furia;

		// ---- o roteiro contra os `sleep()` do DM ----
		Checa("a cena existe e tem os 8 instantes do `AngerCinematic()`", c.Beats.Length == 8,
			  $"{c.Beats.Length} beats");
		Checa("os beats estao em ordem de tempo",
			  c.Beats.Zip(c.Beats.Skip(1)).All(p => p.Second.Em >= p.First.Em));

		// 6 ciclos de `sleep(5)` (0,5 s) + a virada + `sleep(20)` (2,0 s) = 5,0 s de cena.
		Checa("ela mede 5,0 s -- os `sleep` do DM em decissegundo", Math.Abs(c.Segundos - 5.0) < 0.001,
			  $"{c.Segundos:0.###}s");

		// ============================ O CORPO NAO PARA -- `set waitfor = 0` ============================
		// A checagem mais importante desta secao, e a que separa esta cena de todas as outras 34. Se
		// isto reprovar, a furia passou a prender o jogador no meio de uma briga que outra pessoa
		// comecou. Ver `Cinematicas.Furia` e `Murder.dm:137`.
		Checa("ela NAO prende o corpo (o `set waitfor = 0` do DM)", c.SegundosPreso == 0,
			  $"{c.SegundosPreso:0.###}s preso");

		// ---- a virada ----
		int viradas = c.Beats.Count(b => b.Faz.HasFlag(Efeito.Assumir));
		Checa("tem UM beat de virada, e um so", viradas == 1, $"{viradas}");

		Beat virada = c.Beats.First(b => b.Faz.HasFlag(Efeito.Assumir));
		Checa("a virada cai aos 3,0 s (`createCrater` + `Quake` + `powerup.wav`, `Murder.dm:158-160`)",
			  Math.Abs(virada.Em - 3.0) < 0.001, $"{virada.Em:0.###}s");
		Checa("...e ela leva o `powerup.wav` do original", virada.Som == "powerup", virada.Som);

		// ---- REGRA 1: a cratera e do instante da virada, e so dele ----
		Checa("a cratera cai NO beat da virada", virada.Faz.HasFlag(Efeito.Cratera));
		Checa("e em nenhum outro beat da cena",
			  c.Beats.Count(b => b.Faz.HasFlag(Efeito.Cratera)) == 1);

		// ---- REGRA 2: poeira so com a cratera (e a cauda) ----
		Checa("a poeira nasce com a cratera", virada.Faz.HasFlag(Efeito.Poeira));
		Checa("nao ha poeira ANTES da virada",
			  !c.Beats.Any(b => b.Em < virada.Em && b.Faz.HasFlag(Efeito.Poeira)));

		Beat[] depois = [.. c.Beats.Where(b => b.Em > virada.Em)];
		Checa("depois da virada ha exatamente UM beat -- a poeira assentando", depois.Length == 1,
			  $"{depois.Length} beats de cauda");
		Checa("...e ele cai aos 4,0 s (o `sleep(20)` antes de a aura sair)",
			  depois.Length == 1 && Math.Abs(depois[0].Em - 4.0) < 0.001);

		// ---- REGRA 3: a pedra do inicio ao fim, e ela nao e beat ----
		Checa("o chao fica solto a cena inteira (a pedra nao e um instante)", c.OChaoSeSolta);

		// ---- e o que ela NAO tem ----
		// O CLARAO E DO INSTANTE EM QUE UMA FORMA FICA, e nesta cena nao fica forma nenhuma. O DM
		// tambem nao tem clarao de tela em lugar nenhum (ver `Efeito.ClaraoDeTela`).
		Checa("nao ha clarao de tela (nao ha forma pra distinguir)",
			  !c.Beats.Any(b => b.Faz.HasFlag(Efeito.ClaraoDeTela)));
		Checa("nao ha piscar de cabelo, degrau vestido, faisca nem banho de cor",
			  !c.Beats.Any(b => b.Faz.HasFlag(Efeito.PiscaCabelo) || b.Faz.HasFlag(Efeito.VesteDegrau)
								|| b.Faz.HasFlag(Efeito.Raios) || b.Faz.HasFlag(Efeito.BanhoDeCor)));
		Checa("o ceu nao descarrega (isso e da estreia do SSJ1, e so dela)", !c.OCeuDescarrega);

		// ---- SEM AURA VERMELHA (dono, 2026-10-11; a divergencia do `Murder.dm:146-150` esta em `Cinematicas.Furia`) ----
		static bool AcendeAChama(Cinematica cena) => cena.Beats.Any(b => b.Faz.HasFlag(Efeito.AuraGrande));
		Checa("nenhum beat da furia acende a chama da cena (a aura vermelha do DM saiu por pedido do dono)", !AcendeAChama(c));
		Checa("(contra-exemplo) a MESMA pergunta, feita a cena de estreia do Super Saiyajin, responde SIM", AcendeAChama(Cinematicas.Ssj1));
		Checa("...e os aneis continuam la: seis ondas de choque antes da virada (`Murder.dm:151-157`)",
			  c.Beats.Count(b => b.Em < virada.Em && b.Faz.HasFlag(Efeito.AnelDeChoque)) == 6);
		Checa("a furia acontece na cor do ki do PROPRIO corpo (`NaCorDoCorpo`) -- e a fusao, que tambem nao tem forma, nao mudou",
			  c.NaCorDoCorpo && !Cinematicas.Fusao.NaCorDoCorpo);
		Checa("a cor das cenas sem forma continua `ff2a2a` (e a cara da fusao e do bio, que este pedido nao tocou)",
			  Cinematicas.CorDaCenaSemForma == "ff2a2a", Cinematicas.CorDaCenaSemForma);

		// ---- ela nao e de forma nenhuma ----
		Checa("ela nao pertence a forma nenhuma (o id e vazio)", c.Forma.Length == 0, c.Forma);
		Checa("e nao esta na lista das cenas de forma (`Cinematicas.Todas`)",
			  !Cinematicas.Todas.Contains(c),
			  "dentro da `Todas` ela cairia no `Encurtar` e na regra 'toda forma tem cena'");
		Checa("nenhuma forma do catalogo cai nela",
			  Catalogo.Todas.All(d => !ReferenceEquals(Cinematicas.Para(d), c)));

		// ---- o tema ----
		Checa("ela tem o tema de raiva do DM", c.Musica.Contains("Anger Theme"), c.Musica);
		string musica = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Sounds", "Music", c.Musica);
		// EXISTIR NA PASTA NAO E ESTAR IMPORTADO -- e por isso o `.import` tambem. Um mp3 no disco sem
		// o `.import` do Godot carrega nulo e a cena fica MUDA, calada. Ver a mesma regra nas pedras.
		Checa("o arquivo do tema esta no disco", File.Exists(musica), musica);
		Checa("...e o Godot o importou", File.Exists(musica + ".import"));

		// ---- a recarga ----
		Checa("a recarga da cena e 60 s (o `rageCinematicCD` do DM)",
			  Math.Abs(Cinematicas.SegundosEntreFurias - 60) < 0.001,
			  $"{Cinematicas.SegundosEntreFurias}");
		// E ELA NAO E O PRAZO DA RAIVA. Amarrar os dois faria mexer no balanceamento do luto mudar,
		// em silencio, quantas vezes a cinematica toca -- ver o campo `ServerPlayer.FuriaCenaAte`.
		Checa("...e ela nao e o prazo da raiva (120 s): sao dois relogios",
			  Math.Abs(Cinematicas.SegundosEntreFurias - 120) > 0.001);

		// ---- REGRA 1 e 2 conferidas no FUNIL, e nao no roteiro ----
		// A CENA DE SONDA ESCREVE ERRADO DE PROPOSITO: cratera e poeira antes da virada, e cratera
		// depois dela. O `init` da `Cinematica` tem que apagar as tres coisas e por a cratera + poeira
		// exatamente no beat que vira. Se este bloco reprovar, o funil parou de ser o unico caminho --
		// e a proxima cena escrita a mao vai abrir o chao na hora errada, calada.
		var sonda = new Cinematica
		{
			Forma = "",
			SegundosPreso = 0,
			Beats =
			[
				new(0.0, Efeito.Cratera | Efeito.Poeira),
				new(1.0, Efeito.Poeira),
				new(2.0, Efeito.Assumir),
				new(3.0, Efeito.Cratera | Efeito.Poeira),
			],
		};
		Checa("o funil apaga a cratera escrita ANTES da virada", !sonda.Beats[0].Faz.HasFlag(Efeito.Cratera));
		Checa("...e a poeira tambem", !sonda.Beats[0].Faz.HasFlag(Efeito.Poeira)
									  && !sonda.Beats[1].Faz.HasFlag(Efeito.Poeira));
		Checa("o funil poe cratera E poeira no beat da virada",
			  sonda.Beats[2].Faz.HasFlag(Efeito.Cratera) && sonda.Beats[2].Faz.HasFlag(Efeito.Poeira));
		Checa("o funil apaga a cratera escrita DEPOIS da virada, e deixa a poeira assentar",
			  !sonda.Beats[3].Faz.HasFlag(Efeito.Cratera) && sonda.Beats[3].Faz.HasFlag(Efeito.Poeira));

		OEncanamentoDaFuria();
		Console.WriteLine();
	}

	/// <summary>
	/// ============================ O ENCANAMENTO: QUEM DISPARA A CENA, E QUEM NAO PODE ============================
	/// Varredura de fontes, como a secao [8] e pelo mesmo motivo: a decisao de tocar a cinematica **nao
	/// deixa rastro nenhum no estado**. Nao ha forma nova, nao ha buff novo (o `angerBuff` ja vinha da
	/// janela), nao ha campo pra perguntar depois -- so um pacote que sai ou nao sai. O modo de falha
	/// e o classico deste port: alguem "simplifica" o gatilho pra dentro do chamador, a cena passa a
	/// tocar em todo nocaute, e nada acusa.
	///
	/// Sao tres perguntas:
	///   * o pacote `S2C.Furia` tem UM emissor de producao, e ele mora no funil da raiva;
	///   * as quatro condicoes do `Murder.dm:119` estao TODAS escritas la (grau extremo, `wasRaging`,
	///     corpo com dono, e a previsao de transformacao) -- mais a recarga;
	///   * o cliente tem UM tocador, e ele passa `forma: null` (a cena da furia nao veste ninguem).
	/// ========================================================================================================
	/// </summary>
	private static void OEncanamentoDaFuria()
	{
		string raiz = Directory.GetCurrentDirectory();
		var emissores = new List<string>();
		var tocadores = new List<string>();
		int gatilhos = 0, previsao = 0, recarga = 0, semDono = 0, wasRaging = 0, grauExtremo = 0;
		bool estragoNaCena = false;

		// QUEM PODE FALAR DO PACOTE SEM SER O EMISSOR: o proprio protocolo (a declaracao), o cliente
		// (que o LE) e as bancadas ao vivo.
		string[] naoSaoEmissores =
			["Protocol.cs", "GameClient.cs", "GameServer.RaivaTeste.cs", "GameServer.FormasTeste.cs",
			 "RoboDeForma.cs"];

		foreach (string dir in new[] { "Core", "Server", "Client", "Net" })
		{
			string caminho = Path.Combine(raiz, dir);
			if (!Directory.Exists(caminho)) continue;

			foreach (string arq in Directory.EnumerateFiles(caminho, "*.cs", SearchOption.AllDirectories))
			{
				string nome = Path.GetFileName(arq);
				string[] linhas = File.ReadAllLines(arq);
				for (int i = 0; i < linhas.Length; i++)
				{
					string l = linhas[i].Trim();
					if (l.StartsWith("//") || l.StartsWith("///")) continue;

					if (l.Contains("Protocol.S2C.Furia") && Array.IndexOf(naoSaoEmissores, nome) < 0)
						emissores.Add($"{nome}:{i + 1}");

					if (l.Contains("private void TalvezACenaDaFuria")) { gatilhos++; continue; }
					if (l.Contains("AFuriaVaiVirarForma")) previsao++;
					if (l.Contains("FuriaCenaAte")) recarga++;
					if (l.Contains("enlutado.Peer == null")) semDono++;
					if (l.Contains("jaNaFuriaDaCena")) wasRaging++;
					if (l.Contains("grau != NivelDeRaiva.Extrema")) grauExtremo++;

					if (l.Contains("Cinematicas.Furia")) tocadores.Add($"{nome}:{i + 1}");

					// A `PoeiraDeEstrago` E DO CENARIO CAINDO, E NAO DE CINEMATICA. O dono mandou tirar
					// ("uns quadrados marrons caindo... TIRE esse efeito") e o bit `Cascalho` ficou
					// aposentado. Se ela voltar ao tocador, a cena nova traz o defeito de volta junto.
					if (nome == "Transformacao.cs" && l.Contains("PoeiraDeEstrago")) estragoNaCena = true;
				}
			}
		}

		Checa("o gatilho da cena existe, e e um so", gatilhos == 1, $"{gatilhos} definicoes");
		Checa("o pacote `S2C.Furia` tem emissor de producao", emissores.Count > 0,
			  "nenhum -- a cinematica nunca sai do servidor");
		Checa("...e ele e o funil da raiva, e so ele",
			  emissores.All(e => e.StartsWith("GameServer.Formas.cs")), string.Join(" | ", emissores));

		// AS QUATRO CONDICOES DO `Murder.dm:119`, uma a uma.
		Checa("condicao 1: so o grau EXTREMO toca a cena", grauExtremo >= 1);
		Checa("condicao 2: o `wasRaging` e lido (nao toca duas vezes na mesma furia EXTREMA)", wasRaging >= 3,
			  $"{wasRaging} usos de `jaNaFuriaDaCena` -- ele nasce no gancho, e passado e e lido no gatilho");
		Checa("condicao 3: corpo SEM DONO nao assiste cinematica (o `client` do DM)", semDono >= 1);
		Checa("condicao 4: a transformacao fica com o momento (`anger_will_transform`)", previsao >= 2,
			  $"{previsao} usos -- a definicao e a chamada");
		Checa("e a recarga de 60 s e lida E escrita", recarga >= 3,
			  $"{recarga} usos -- o campo, a leitura e a escrita");

		// O TOCADOR E UM SO, e ele mora no `World` (o funil de eventos do cliente).
		Checa("o cliente tem UM tocador pra a cena da furia",
			  tocadores.Count(t => t.StartsWith("World.cs")) == 1, string.Join(" | ", tocadores));

		Checa("a cinematica NAO chama o sistema de estrago de cenario", !estragoNaCena,
			  "a `PoeiraDeEstrago` voltou pro tocador -- ver `Efeito` 8192, aposentado");
	}

	/// <summary>Um fonte do repo, linha a linha (vazio se nao existir -- e a checagem que o le reprova).</summary>
	private static string[] LerFonte(string raiz, string pasta, string arquivo)
	{
		string caminho = Path.Combine(raiz, pasta, arquivo);
		return File.Exists(caminho) ? File.ReadAllLines(caminho) : [];
	}

	/// <summary>
	/// AS LINHAS DE UM METODO: da assinatura ate o `}` que o fecha (o primeiro na mesma indentacao da assinatura).
	/// Vazio se a assinatura nao for achada -- e quem le cobra o tamanho, pra uma assinatura renomeada reprovar em
	/// vez de a varredura passar por nao ter o que olhar.
	/// </summary>
	private static string[] CorpoDoMetodo(string[] fonte, string assinatura)
	{
		int i = Array.FindIndex(fonte, l => l.Contains(assinatura));
		if (i < 0) return [];
		string recuo = fonte[i][..(fonte[i].Length - fonte[i].TrimStart().Length)];
		int j = Array.FindIndex(fonte, i + 1, l => l.TrimEnd() == recuo + "}");
		return j < 0 ? [] : fonte[i..(j + 1)];
	}

	// =====================================================================
	// 11. OS DOIS DEGRAUS EM NUMERO E O SALTO DE KI
	// =====================================================================
	/// <summary>
	/// ============================ A REGRA DO DONO DE 2026-10-11, SO NA CONTA ============================
	/// *"amigo quando e nocauteado vc n fica com o mais alto nivel de raiva, vc fica angry apenas, o very angry e
	/// so em morte e absorcao (...) caso o usuario ja tenha um certo nivel de ki control etc e ele fique com
	/// extreme anger, instantaneamente o ki dele sobe pro maximo q ele pode carregar (...) raiva comum n acontece
	/// isso"*.
	///
	/// As secoes de cima medem a PORTA de cada forma; esta mede o que cada degrau VALE
	/// (`RaivaEmDegraus.Topo`) e a regra que so o de cima tem (`RaivaEmDegraus.SaltarOKi`), com os tres defeitos
	/// injetados. A CORRENTE -- quem acende qual degrau, a cinematica, o que o jogador le -- e da bancada ao vivo
	/// (`--raivateste`), e a tela e o som sao da `--diagraiva`.
	/// ====================================================================================================
	/// </summary>
	private static void OsDoisDegrausEOSaltoDeKi()
	{
		Console.WriteLine("[11] OS DOIS DEGRAUS EM NUMERO E O SALTO DE KI (dono, 2026-10-11)");

		// ---- o numero de cada degrau, em quatro tetos de raiva (o piso 120, um Saiyajin, o joelho do buff, um irascivel) ----
		foreach (double maxAnger in new[] { 120.0, 150.0, 200.0, 340.0 })
		{
			double folga = maxAnger - 100;
			double extrema = Jandirus.Core.Combat.RaivaEmDegraus.Topo(NivelDeRaiva.Extrema, maxAnger);
			double comum = Jandirus.Core.Combat.RaivaEmDegraus.Topo(NivelDeRaiva.Comum, maxAnger);

			Checa($"MaxAnger {maxAnger:0}: a furia EXTREMA vale o topo ({maxAnger:0}, `Murder.dm:112`)",
				  Math.Abs(extrema - maxAnger) < 1e-9, $"{extrema:0.###}");
			Checa($"MaxAnger {maxAnger:0}: a raiva COMUM vale 100 + folga/1,30 = {100 + folga / 1.30:0.##} (`Voting.dm:40`), abaixo do topo",
				  Math.Abs(comum - (100 + folga / 1.30)) < 1e-9 && comum < extrema, $"{comum:0.###}");

			// A FAIXA "Angry" DA REGUA DO DM (`Stats.dm:448-449`): de `100 + folga/1,66` ate antes de `100 + folga/1,25`.
			double piso = 100 + folga / 1.66, teto = 100 + folga / 1.25;
			Checa($"MaxAnger {maxAnger:0}: ela cai dentro do 'Angry' do DM (de {piso:0.##} ate {teto:0.##}), e nao no 'Very Angry'",
				  comum >= piso && comum < teto, $"{comum:0.###}");
			double noFim = comum - folga * 120 / 1600.0;
			Checa($"MaxAnger {maxAnger:0}: ...e continua 'Angry' depois dos 120 s de gotejamento (`Stats.dm:443`)",
				  noFim >= piso, $"{noFim:0.###} contra o piso {piso:0.###}");
		}

		Checa("calma vale 100 (o 1,0x), e sem folga os dois degraus tambem",
			  Jandirus.Core.Combat.RaivaEmDegraus.Topo(NivelDeRaiva.Nenhuma, 150) == 100
			  && Jandirus.Core.Combat.RaivaEmDegraus.Topo(NivelDeRaiva.Extrema, 100) == 100
			  && Jandirus.Core.Combat.RaivaEmDegraus.Topo(NivelDeRaiva.Comum, 100) == 100
			  && Jandirus.Core.Combat.RaivaEmDegraus.Topo(NivelDeRaiva.Extrema, 60) == 100);

		Jandirus.Core.Combat.RaivaEmDegraus.NocauteNoTopoDeTeste = true;
		try
		{
			Checa("(defeito injetado: o nocaute vale o topo) a checagem 'comum abaixo do topo' REPROVA",
				  !(Jandirus.Core.Combat.RaivaEmDegraus.Topo(NivelDeRaiva.Comum, 150) < 150));
		}
		finally { Jandirus.Core.Combat.RaivaEmDegraus.NocauteNoTopoDeTeste = false; }

		// ---- o salto de Ki ----
		static Jandirus.Core.Stats.Fighter Corpo(double ki, double teto = 1.4, bool sabeReunir = true, bool controle = true)
		{
			var f = new Jandirus.Core.Stats.Fighter();
			f.MaxKi = 1000;
			f.Ki = ki;
			f.powerupcap = teto;
			f.MeditateGivesKiRegen = sabeReunir ? 1 : 0;
			f.canPower = controle ? 1 : 0;
			return f;
		}
		static bool Salta(Jandirus.Core.Stats.Fighter f, NivelDeRaiva grau) => Jandirus.Core.Combat.RaivaEmDegraus.SaltarOKi(grau, f);

		Jandirus.Core.Stats.Fighter a = Corpo(50);
		bool subiu = Salta(a, NivelDeRaiva.Extrema);
		Checa("furia EXTREMA + controle de ki: o Ki vai de 5% pro teto de carga (140%), e o excesso nasce protegido",
			  subiu && Math.Abs(a.Ki - 1400) < 1e-9 && a.overcharge, $"Ki {a.Ki:0}");
		Checa("...e o teto do salto e o MESMO da tecla C (`CargaDeKi.TetoDeCarga`)",
			  Math.Abs(Jandirus.Core.Combat.RaivaEmDegraus.KiDoSalto(a) - Jandirus.Core.Combat.CargaDeKi.TetoDeCarga(a)) < 1e-9);

		foreach (double teto in new[] { 2.0, 3.8 })
		{
			Jandirus.Core.Stats.Fighter t = Corpo(1000, teto);
			Salta(t, NivelDeRaiva.Extrema);
			Checa($"com teto de {teto * 100:0}%: de 100% ele vai a {teto * 100:0}%", Math.Abs(t.Ki - 1000 * teto) < 1e-9, $"Ki {t.Ki:0}");
		}

		Jandirus.Core.Stats.Fighter b = Corpo(50);
		Checa("raiva COMUM (o nocaute) NAO salta, com o mesmo controle na mao",
			  !Salta(b, NivelDeRaiva.Comum) && b.Ki == 50 && !b.overcharge, $"Ki {b.Ki:0}");
		Checa("...nem a calma", !Salta(b, NivelDeRaiva.Nenhuma) && b.Ki == 50);
		Jandirus.Core.Stats.Fighter irritadoKi = Corpo(50);
		double topoIrritado = Jandirus.Core.Combat.RaivaEmDegraus.Topo(NivelDeRaiva.Irritado, 120);
		Checa("o IRRITADO vale o Annoyed do DM (100 + folga/4,25) e NAO salta o Ki",
			  Math.Abs(topoIrritado - (100 + 20 / 4.25)) < 1e-9
			  && !Salta(irritadoKi, NivelDeRaiva.Irritado) && irritadoKi.Ki == 50,
			  $"{topoIrritado:0.###}");

		Jandirus.Core.Stats.Fighter semControle = Corpo(50, controle: false);
		Checa("SEM o controle (so Ki Unlocked) a furia extrema nao mexe no Ki",
			  !Salta(semControle, NivelDeRaiva.Extrema) && semControle.Ki == 50, $"Ki {semControle.Ki:0}");
		Jandirus.Core.Stats.Fighter semReunir = Corpo(50, sabeReunir: false);
		Checa("...nem pra quem nao sabe reunir energia (sem Ki Unlocked)",
			  !Salta(semReunir, NivelDeRaiva.Extrema) && semReunir.Ki == 50);
		Jandirus.Core.Stats.Fighter travado = Corpo(50);
		travado.fd_ki_locked = true;
		Checa("...nem com o ki travado (`fd_ki_locked`: a tecla C esta morta, entao nao ha teto pra alcancar)",
			  !Salta(travado, NivelDeRaiva.Extrema) && travado.Ki == 50);

		Jandirus.Core.Stats.Fighter acima = Corpo(3000);
		Checa("quem ja esta ACIMA do teto de carga fica onde esta (o salto nunca desce o Ki, nem liga protecao)",
			  !Salta(acima, NivelDeRaiva.Extrema) && acima.Ki == 3000 && !acima.overcharge, $"Ki {acima.Ki:0}");
		Jandirus.Core.Stats.Fighter noTeto = Corpo(1400);
		Checa("...e quem esta exatamente nele nao 'salta' (nao ha o que avisar)", !Salta(noTeto, NivelDeRaiva.Extrema) && noTeto.Ki == 1400);

		Jandirus.Core.Combat.RaivaEmDegraus.SemSaltoDeKiDeTeste = true;
		try
		{
			Jandirus.Core.Stats.Fighter c = Corpo(50);
			Checa("(defeito injetado: a furia extrema nao mexe no Ki) a checagem do salto REPROVA",
				  !Salta(c, NivelDeRaiva.Extrema) && c.Ki == 50);
		}
		finally { Jandirus.Core.Combat.RaivaEmDegraus.SemSaltoDeKiDeTeste = false; }

		Jandirus.Core.Combat.RaivaEmDegraus.SaltoNaRaivaComumDeTeste = true;
		try
		{
			Jandirus.Core.Stats.Fighter c = Corpo(50);
			Checa("(defeito injetado: a raiva comum tambem salta) a checagem 'o nocaute nao salta' REPROVA",
				  Salta(c, NivelDeRaiva.Comum) && c.Ki > 50);
		}
		finally { Jandirus.Core.Combat.RaivaEmDegraus.SaltoNaRaivaComumDeTeste = false; }

		Console.WriteLine("  -> nocaute de amigo = raiva comum ('Angry'); morte ou absorcao = furia extrema ('Very Angry'),");
		Console.WriteLine("     e so nela o Ki de quem tem controle salta pro teto de carga.");
		Console.WriteLine();
	}

	// =====================================================================
	// 12. A SUBIDA PELA RAIVA -- que forma ela estreia, e por quais degraus
	// =====================================================================
	/// <summary>
	/// ============================ A REGRA DO DONO DE 2026-10-11, SO NO PLANO ============================
	/// *"se o personagem ja despertou a transformacao toca a cinematica da raiva normal, mas se ele tiver os reqs
	/// por exemplo do ssj2 e ele estiver na base e ficar com raiva, ele vai pro ssj1 e comeca a cinematica do
	/// ssj2"*.
	///
	/// Quem EXECUTA a subida e o servidor (a `--raivateste` mede: os dois anuncios, a cena, o marco); aqui se mede
	/// a PERGUNTA -- `SubidaPelaRaiva.Planejar`, a funcao pura que o servidor chama: que forma nova, por quais
	/// degraus, e os casos de borda (dupla estreia, grades, a linha Legendary, quem ja esta acima).
	///
	/// OS CORPOS SAEM DO <see cref="Preparado"/>, o mesmo das secoes de cima: tudo o que a forma pede esta pago
	/// menos a raiva, e o corpo esta no degrau ANTERIOR (ja estreado). Dali eu o desco pra base, que e o caso do
	/// dono. Toda checagem passa pelo `Avaliar` -- e o plano que o chama, de onde o corpo esta e de onde estara.
	/// ====================================================================================================
	/// </summary>
	private static void ASubidaPelaRaiva()
	{
		Console.WriteLine("[12] A SUBIDA PELA RAIVA: A FORMA NOVA, E OS DEGRAUS JA DESPERTADOS ATE ELA (dono, 2026-10-11)");

		FormaDef ssj1 = Catalogo.Def("ssj1")!, ssj2 = Catalogo.Def("ssj2")!;
		static string Texto(PlanoDeSubida? p) =>
			p == null ? "nenhum plano" : string.Join(" -> ", p.Degraus.Select(x => x.Id).Append($"[{p.Alvo.Id}]"));

		// ---- (a) o exemplo do dono: na BASE, com o Super Saiyajin ja estreado e tudo do 2 pago ----
		(EstadoDeForma e, PerfilDeFormas p, double bp) = Preparado(ssj2, NivelDeRaiva.Extrema);
		e.Entrar(Catalogo.IdBase);
		RecusaForma daBase = e.Avaliar("ssj2", bp, 1, false, p);
		Checa("(montagem) na BASE, com o Super Saiyajin estreado e a furia acesa, o 2 e recusado por FORA DE ORDEM: so falta o degrau",
			  e.NaBase && e.JaViuAEstreia("ssj1") && !e.Despertou("ssj2") && daBase == RecusaForma.ForaDeOrdem, daBase.ToString());
		PlanoDeSubida? plano = SubidaPelaRaiva.Planejar(e, bp, p);
		Checa("na BASE com os requisitos do Super Saiyajin 2: o plano e SUBIR PELO 1 e ESTREAR O 2",
			  plano is { Alvo.Id: "ssj2", Degraus.Length: 1 } && plano.Degraus[0].Id == "ssj1", Texto(plano));
		Checa("...e perguntar nao mexe no corpo: ele continua na base, o 2 continua por despertar, a estreia continua devendo",
			  e.NaBase && !e.Despertou("ssj2") && !e.JaViuAEstreia("ssj2"), e.Atual);
		Checa("...nem a pergunta 'e se eu estivesse no 1?' (`AvaliarEstandoEm`), que e a que acha o caminho",
			  e.AvaliarEstandoEm("ssj1", "ssj2", bp, 1, false, p) == RecusaForma.Pode && e.NaBase, e.Atual);

		// ---- (b) so a raiva que a forma pede ----
		Checa("em CALMA nao ha plano nenhum", SubidaPelaRaiva.Planejar(e, bp, p with { Raiva = NivelDeRaiva.Nenhuma }) == null);
		PlanoDeSubida? comAComum = SubidaPelaRaiva.Planejar(e, bp, p with { Raiva = NivelDeRaiva.Comum });
		Checa("...e a raiva COMUM, com poder de sobra, faz o MESMO plano: ela abre as mesmas formas, por um preco maior (secao [13])",
			  comAComum is { Alvo.Id: "ssj2", Degraus.Length: 1 } && comAComum.Degraus[0].Id == "ssj1", Texto(comAComum));

		// ---- (c) sem o poder do 2 nao ha forma NOVA: nenhum plano (e a cinematica que toca e a da raiva) ----
		double porta1 = e.PortaDeBp(ssj1), porta2 = e.PortaDeBp(ssj2);
		PlanoDeSubida? semODois = SubidaPelaRaiva.Planejar(e, (porta1 + porta2) / 2, p);
		Checa("com o poder do 1 e NAO o do 2: nenhum plano -- a forma que ja e dele nao vem sozinha, e nao ha nova pra estrear",
			  porta2 > porta1 && semODois == null, $"portas {porta1:0} / {porta2:0}; {Texto(semODois)}");

		// ---- (d) nenhum dos dois estreado: a estreia e do 1, e o 2 espera a cena dele ----
		(EstadoDeForma e0, PerfilDeFormas p0, _) = Preparado(ssj1, NivelDeRaiva.Extrema);
		PlanoDeSubida? primeiro = SubidaPelaRaiva.Planejar(e0, bp, p0);
		Checa("nem o 1 nem o 2 estreados, poder pros dois: o plano e ESTREAR O 1, sem degrau (sao duas estreias, uma de cada vez)",
			  e0.NaBase && primeiro is { Alvo.Id: "ssj1", Degraus.Length: 0 }, Texto(primeiro));

		// ---- (e) liberado sem nunca ter sido vestido nao serve de degrau ----
		e0.Liberar("ssj1");
		PlanoDeSubida? soLiberado = SubidaPelaRaiva.Planejar(e0, bp, p0);
		Checa("o 1 so LIBERADO (nunca vestido) nao serve de degrau: atravessa-lo calado queimaria a estreia dele",
			  !e0.JaViuAEstreia("ssj1") && soLiberado == null, Texto(soLiberado));

		// ---- (f) dentro do 1, e num grade: o 2 se alcanca dali, sem degrau ----
		(EstadoDeForma e1, PerfilDeFormas p1, _) = Preparado(ssj2, NivelDeRaiva.Extrema);
		PlanoDeSubida? deDentro = SubidaPelaRaiva.Planejar(e1, bp, p1);
		Checa("DENTRO do Super Saiyajin: estreia o 2 dali mesmo, sem degrau",
			  e1.Atual == "ssj1" && deDentro is { Alvo.Id: "ssj2", Degraus.Length: 0 }, Texto(deDentro));
		e1.Entrar("grade2");
		PlanoDeSubida? doGrade = SubidaPelaRaiva.Planejar(e1, bp, p1);
		Checa("num GRADE (o ramo lateral do 1): estreia o 2 de onde esta -- os grades nunca sao degrau do caminho",
			  e1.Atual == "grade2" && doGrade is { Alvo.Id: "ssj2", Degraus.Length: 0 }, Texto(doGrade));

		// ---- (g) a subida e do CATALOGO: a linha Legendary, dois degraus, com a raiva COMUM ----
		(EstadoDeForma el, PerfilDeFormas pl, _) = Preparado(Catalogo.Def("legendary")!, NivelDeRaiva.Comum);
		el.Entrar(Catalogo.IdWrathful);
		el.Entrar("c_type");
		el.Entrar(Catalogo.IdBase);
		PlanoDeSubida? lendario = SubidaPelaRaiva.Planejar(el, bp, pl);
		Checa("a subida e do CATALOGO, nao do Super Saiyajin: Legendary na base, Wrathful e C-Type ja estreados, raiva COMUM -- sobe pelos DOIS e estreia o Full Power",
			  lendario is { Alvo.Id: "legendary", Degraus.Length: 2 }
			  && lendario.Degraus[0].Id == Catalogo.IdWrathful && lendario.Degraus[1].Id == "c_type", Texto(lendario));
		el.EstreiaVista.Remove(Catalogo.Rede("c_type"));
		PlanoDeSubida? comBuraco = SubidaPelaRaiva.Planejar(el, bp, pl);
		Checa("...e com um degrau do MEIO nunca vestido a corrente nao fecha: nenhum plano (ninguem pula estreia)",
			  comBuraco == null, Texto(comBuraco));

		// ---- (h) so pra cima: numa forma mais forte que a que a raiva abriria ----
		(EstadoDeForma ea, PerfilDeFormas pa, _) = Preparado(ssj2, NivelDeRaiva.Extrema);
		ea.Entrar("ssj3");   // (palco) acima do 2 sem nunca te-lo despertado: o que se mede e a comparacao de forca
		RecusaForma doisDeCima = ea.Avaliar("ssj2", bp, 1, false, pa);
		PlanoDeSubida? praBaixo = SubidaPelaRaiva.Planejar(ea, bp, pa);
		Checa("A RAIVA SO SOBE: no Super Saiyajin 3 (16x), o 2 (ate 10x) esta 'aberto' pro funil e mesmo assim nao ha plano",
			  doisDeCima == RecusaForma.Pode && praBaixo == null, $"o 2 visto do 3: {doisDeCima}; {Texto(praBaixo)}");

		// ---- os dois defeitos injetados ----
		SubidaPelaRaiva.NaoSobeDegrausDeTeste = true;
		try
		{
			Checa("(defeito injetado: a raiva so desperta o que se alcanca de onde o corpo esta -- o DM, supersaiyan.dm:122) o exemplo do dono fica SEM plano: a checagem REPROVA",
				  SubidaPelaRaiva.Planejar(e, bp, p) == null);
		}
		finally { SubidaPelaRaiva.NaoSobeDegrausDeTeste = false; }

		SubidaPelaRaiva.PraBaixoDeTeste = true;
		try
		{
			PlanoDeSubida? caiu = SubidaPelaRaiva.Planejar(ea, bp, pa);
			Checa("(defeito injetado: a raiva desperta a forma mesmo sendo mais fraca) o corpo CAIRIA do 3 pro 2: a checagem 'so sobe' REPROVA",
				  caiu is { Alvo.Id: "ssj2" }, Texto(caiu));
		}
		finally { SubidaPelaRaiva.PraBaixoDeTeste = false; }

		Console.WriteLine("  -> a raiva estreia a forma NOVA mais forte que tudo o mais ja pagou, subindo sem cena pelos degraus que");
		Console.WriteLine("     ja sao do personagem; sem forma nova nao ha plano -- e a cinematica que toca e a da raiva.");
		Console.WriteLine();
	}

	// =====================================================================
	// 13. O PRECO DA RAIVA COMUM -- o `if("Angry")` do DM, forma por forma
	// =====================================================================
	/// <summary>
	/// O AGIO DE CADA FORMA, **escrito a mao** pelo mesmo motivo da tabela da secao [1]: gerado do catalogo ele
	/// passaria por construcao. Cada numero e o literal do ramo `if("Angry")` do `switch(savant.Emotion)` daquela
	/// forma -- a linha do DM esta ao lado. Quem nao esta aqui nao se abre com a raiva comum (o Beast) ou nao nasce
	/// da raiva.
	/// </summary>
	private static readonly (string Id, double Agio, string Dm)[] AgioEsperado =
	[
		("ssj1",              1.2, "supersaiyan.dm:112"),
		("ssj2",              1.2, "supersaiyan.dm:129"),
		("future_ssj",        1.2, "supersaiyan.dm:112, pela mesma SSj()"),
		("heran1",            1.3, "heran.dm:29"),
		("heran2",            1.2, "heran.dm:45"),
		("wrathful",          1.5, "supersaiyan.dm:68"),
		("c_type",            1.5, "supersaiyan.dm:94"),
		("legendary",         1.5, "supersaiyan.dm:102"),
		("primal_c_type",     1.2, "supersaiyan.dm:112 -- o Primal cai no bloco comum, :105"),
		("primal_legendary",  1.2, "supersaiyan.dm:129"),
		("primal_legendary2", 1.3, "supersaiyan.dm:155"),
	];

	/// <summary>
	/// ============================ A SECAO DO PEDIDO DE 2026-10-11 ============================
	/// O dono: *"no dm a raiva extrema faz vc se transformar mais facil nas formas q dependem de raiva (...), o rage
	/// normal (...) tb faz vc se transformar da mesma forma q o extreme, porem o requisito de bp pro rage normal
	/// liberar sua proxima transformacao e maior"*.
	///
	/// TRES CASOS POR FORMA, todos pelo `Avaliar`: calmo nao entra; a furia extrema entra com o BP MENOR (a porta);
	/// a raiva comum entra so com o MAIOR (a porta vezes o agio). Mais a estreia AUTOMATICA pela comum (o plano do
	/// `SubidaPelaRaiva`, que o servidor executa) e o preco proprio da assistida.
	///
	/// OS DOIS DEFEITOS INJETADOS sao os dois jeitos de a regra sumir calada: a comum pelo preco da extrema
	/// (`EstadoDeForma.RaivaComumSemAgioDeTeste`) e a comum sem abrir o tronco -- a regra de antes
	/// (`Catalogo.SoAFuriaAbreOTroncoDeTeste`).
	/// =========================================================================================
	/// </summary>
	private static void OPrecoDaRaivaComum()
	{
		Console.WriteLine("[13] O PRECO DA RAIVA COMUM: CALMO NAO ENTRA, A FURIA ENTRA PELA PORTA, A COMUM PELA PORTA VEZES O AGIO (dono, 2026-10-11)");

		// ---- a tabela, nos dois sentidos ----
		foreach (FormaDef d in Catalogo.Todas)
		{
			double esperado = AgioEsperado.FirstOrDefault(a => a.Id == d.Id).Agio;   // 0 pra quem nao esta na tabela
			Checa($"`{d.Id}`: agio da raiva comum {esperado:0.0#}", Math.Abs(d.AgioNaRaivaComum - esperado) < 1e-12,
				  $"deu {d.AgioNaRaivaComum}");
		}
		foreach ((string id, _, _) in AgioEsperado)
			Checa($"`{id}` existe no catalogo e nasce da raiva (a tabela nao aponta pro vazio)",
				  Catalogo.Def(id) is { } x && Catalogo.NasceDaRaiva(x));
		string[] soComAExtrema = [.. Catalogo.Todas.Where(d => Catalogo.NasceDaRaiva(d) && d.AgioNaRaivaComum <= 0).Select(d => d.Id)];
		Checa("a UNICA forma de raiva que a comum nao abre e o Beast (`supersaiyan.dm:166`: so ha o ramo \"Very Angry\")",
			  soComAExtrema.Length == 1 && soComAExtrema[0] == "beast", string.Join(", ", soComAExtrema));
		string[] agioSemRaiva = [.. Catalogo.Todas.Where(d => !Catalogo.NasceDaRaiva(d) && d.AgioNaRaivaComum != 0).Select(d => d.Id)];
		Checa("nenhuma forma que NAO nasce da raiva declara agio (seria dado morto)", agioSemRaiva.Length == 0,
			  string.Join(", ", agioSemRaiva));
		Checa("todo agio e MAIOR que 1: a raiva comum nunca sai mais barata que a furia",
			  AgioEsperado.All(a => a.Agio > 1) && Catalogo.Todas.All(d => d.AgioNaRaivaComum == 0 || d.AgioNaRaivaComum > 1));

		// ---- o funil, forma por forma: tres raivas, e o poder em volta dos dois precos ----
		double metade = Jandirus.Core.Skills.Discipulado.FatorAssistido;
		foreach ((string id, double agio, string dm) in AgioEsperado)
		{
			FormaDef d = Catalogo.Def(id)!;
			(EstadoDeForma e, PerfilDeFormas emPaz, _) = Preparado(d, NivelDeRaiva.Nenhuma);
			PerfilDeFormas comum = emPaz with { Raiva = NivelDeRaiva.Comum }, furia = emPaz with { Raiva = NivelDeRaiva.Extrema };
			double porta = e.PortaDeBp(d), cara = porta * agio;
			RecusaForma A(double bp, PerfilDeFormas p, double fator = 1) => e.Avaliar(id, bp, 1, false, p, fator);

			Checa($"`{id}` (montagem) tem porta de BP, e o preco da raiva comum fica acima dela ({dm})",
				  porta > 0 && cara > porta, $"porta {porta:0}, comum {cara:0}");

			RecusaForma calmo = A(cara * 10, emPaz);
			Checa($"`{id}` CALMO, com poder de sobra: NAO entra (falta a raiva, e so ela)", calmo == RecusaForma.SemFuria, calmo.ToString());

			RecusaForma furiaNaPorta = A(porta, furia), furiaAbaixo = A(porta * 0.999, furia);
			Checa($"`{id}` FURIA EXTREMA: entra com o BP MENOR -- a porta, e nem um fio abaixo dela",
				  furiaNaPorta == RecusaForma.Pode && furiaAbaixo == RecusaForma.SemPoder, $"na porta {furiaNaPorta}, abaixo {furiaAbaixo}");

			RecusaForma comumNaPorta = A(porta, comum), comumQuase = A(cara * 0.999, comum), comumNoPreco = A(cara, comum);
			Checa($"`{id}` RAIVA COMUM: com o poder que basta a furia NAO entra, nem um fio abaixo do preco dela",
				  comumNaPorta == RecusaForma.SemPoderPraRaivaComum && comumQuase == RecusaForma.SemPoderPraRaivaComum,
				  $"na porta {comumNaPorta}, a 99,9% do preco {comumQuase}");
			Checa($"`{id}` RAIVA COMUM: entra so com o BP MAIOR -- {agio:0.0#}x a porta", comumNoPreco == RecusaForma.Pode, comumNoPreco.ToString());

			RecusaForma comumSemPorta = A(porta * 0.999, comum);
			Checa($"`{id}` ...e abaixo da propria porta a recusa e por PODER, nao por raiva (a ordem do funil e a mensagem)",
				  comumSemPorta == RecusaForma.SemPoder, comumSemPorta.ToString());

			// a transformacao ASSISTIDA tem o preco dela (a metade), e o agio nao entra por cima
			RecusaForma assistida = A(porta * metade, comum, metade);
			Checa($"`{id}` ASSISTIDA por um mestre: a raiva comum entra pela METADE da porta, sem agio (`mst_form_apply`, MasterStudent.dm:361)",
				  assistida == RecusaForma.Pode, assistida.ToString());

			// o plano da raiva (a estreia AUTOMATICA) segue o mesmo preco: sem o poder da comum nao ha plano pra ela
			PlanoDeSubida? semPreco = SubidaPelaRaiva.Planejar(e, cara * 0.999, comum), comPreco = SubidaPelaRaiva.Planejar(e, cara, comum);
			Checa($"`{id}` a estreia AUTOMATICA pela raiva comum so existe com o preco pago",
				  semPreco?.Alvo.Id != id && comPreco?.Alvo.Id == id,
				  $"sem o preco: {semPreco?.Alvo.Id ?? "nenhum plano"}; com: {comPreco?.Alvo.Id ?? "nenhum plano"}");

			// ---- defeito injetado: a comum pelo preco da extrema ----
			EstadoDeForma.RaivaComumSemAgioDeTeste = true;
			try
			{
				Checa($"`{id}` (defeito injetado: a comum pelo preco da extrema) ela entra NA PORTA -- a checagem do preco REPROVA",
					  A(porta, comum) == RecusaForma.Pode && SubidaPelaRaiva.Planejar(e, porta, comum)?.Alvo.Id == id);
			}
			finally { EstadoDeForma.RaivaComumSemAgioDeTeste = false; }

			// depois de despertada a forma reentra pela porta de sempre, sem raiva nenhuma (o `hasssj` do DM)
			e.Liberar(id);
			RecusaForma reentra = A(porta, emPaz);
			Checa($"`{id}` JA DESPERTADA: reentra pela porta, sem raiva e sem agio", reentra == RecusaForma.Pode, reentra.ToString());
		}

		// ---- o Beast: a comum nao o abre por poder nenhum ----
		FormaDef beast = Catalogo.Def("beast")!;
		(EstadoDeForma eb, PerfilDeFormas pb, double muito) = Preparado(beast, NivelDeRaiva.Comum);
		RecusaForma beastComum = eb.Avaliar("beast", muito, 1, false, pb);
		RecusaForma beastFuria = eb.Avaliar("beast", muito, 1, false, pb with { Raiva = NivelDeRaiva.Extrema });
		Checa("o BEAST com a raiva COMUM e poder sem fim: recusado por FURIA -- e com a extrema abre",
			  beastComum == RecusaForma.SemFuria && beastFuria == RecusaForma.Pode, $"comum {beastComum}, extrema {beastFuria}");
		RecusaForma beastIrritado = eb.Avaliar("beast", muito, 1, false, pb with { Raiva = NivelDeRaiva.Irritado });
		Checa("o BEAST com o IRRITADO tambem nao abre (so a furia)",
			  beastIrritado == RecusaForma.SemFuria, beastIrritado.ToString());

		FormaDef ssjDaPorta = Catalogo.Def("ssj1")!;
		(EstadoDeForma ei, PerfilDeFormas pi, _) = Preparado(ssjDaPorta, NivelDeRaiva.Irritado);
		double portaI = ei.PortaDeBp(ssjDaPorta);
		PerfilDeFormas soIrritado = pi with { Raiva = NivelDeRaiva.Irritado };
		RecusaForma naPorta = ei.Avaliar("ssj1", portaI, 1, false, soIrritado);
		RecusaForma noPreco = ei.Avaliar("ssj1", portaI * Catalogo.PortaDoIrritado, 1, false, soIrritado);
		Checa("o IRRITADO abre o SSJ1 so em x2,5 da porta (a furia paga x1; a comum continua no agio dela)",
			  Math.Abs(Catalogo.PortaDoIrritado - 2.5) < 1e-12
			  && naPorta == RecusaForma.SemPoderPraIrritado && noPreco == RecusaForma.Pode,
			  $"na porta {naPorta}, a 2,5x {noPreco}");
		Checa("...e a estreia automatica usa o mesmo plano da raiva comum",
			  SubidaPelaRaiva.Planejar(ei, portaI * Catalogo.PortaDoIrritado, soIrritado)?.Alvo.Id == "ssj1");

		// ---- o outro defeito: a regra de antes (a comum nao abre o tronco) ----
		FormaDef ssj1 = Catalogo.Def("ssj1")!, wrathful = Catalogo.Def(Catalogo.IdWrathful)!;
		(EstadoDeForma es, PerfilDeFormas ps, _) = Preparado(ssj1, NivelDeRaiva.Comum);
		double caraDoSsj1 = es.PortaDeBp(ssj1) * ssj1.AgioNaRaivaComum;
		Catalogo.SoAFuriaAbreOTroncoDeTeste = true;
		try
		{
			RecusaForma antiga = es.Avaliar("ssj1", caraDoSsj1, 1, false, ps);
			Checa("(defeito injetado: a comum NAO abre o tronco Saiyajin, a regra ate 2026-10-11) com o preco pago o SSJ1 e recusado por furia e nao ha plano -- a checagem 'entra com o BP maior' REPROVA",
				  antiga == RecusaForma.SemFuria && SubidaPelaRaiva.Planejar(es, caraDoSsj1, ps) == null, antiga.ToString());
		}
		finally { Catalogo.SoAFuriaAbreOTroncoDeTeste = false; }

		// ---- o que mudou pro Legendary, medido: a comum deixou de abrir pela porta ----
		(EstadoDeForma ew, PerfilDeFormas pw, _) = Preparado(wrathful, NivelDeRaiva.Comum);
		double portaW = ew.PortaDeBp(wrathful);
		RecusaForma lendarioNaPorta = ew.Avaliar(Catalogo.IdWrathful, portaW, 1, false, pw);
		RecusaForma lendarioNoPreco = ew.Avaliar(Catalogo.IdWrathful, portaW * Catalogo.AgioComumDoLegendary, 1, false, pw);
		Checa("O LEGENDARY NAO TEM MAIS DESCONTO: com a raiva comum e o poder da porta o Wrathful NAO abre (ate 2026-10-11 abria) -- abre a 1,5x",
			  lendarioNaPorta == RecusaForma.SemPoderPraRaivaComum && lendarioNoPreco == RecusaForma.Pode,
			  $"na porta {lendarioNaPorta}, a 1,5x {lendarioNoPreco}");
		Checa("...e na raiva comum o Legendary paga MAIS que o Saiyajin comum, como no DM (1,5x contra 1,2x)",
			  Catalogo.AgioComumDoLegendary > Catalogo.AgioComumDoSaiyajin);

		Console.WriteLine("  -> calmo ninguem desperta; a furia extrema abre pela porta; a raiva comum abre as mesmas formas (menos o");
		Console.WriteLine("     Beast) pela porta vezes o literal do `if(\"Angry\")` de cada uma, e estreia sozinha do mesmo jeito.");
		Console.WriteLine();
	}
}
