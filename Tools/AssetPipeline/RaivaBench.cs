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
///   [1] O NIVEL DE CADA UMA DAS 35 ENTRADAS -- quais pedem `Extrema`, quais pedem `Lendaria`,
///       quais nao pedem nada. Tabela escrita a mao contra o catalogo inteiro, nos dois sentidos.
///   [2] A LINHA LEGENDARY INTEIRA cai no desconto -- nenhum degrau dela pede a furia do luto.
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
		ALinhaLegendary();
		OFunilCobra();
		QuemNaoPedeNaoEhCobrado();
		OSsj3();
		OSsj4EODourado();
		AsDivinas();
		OGanchoLigado();
		Cobertura();
		ACinematicaDaFuria();

		Console.WriteLine($"\n=== {_ok} OK, {_falhou} FALHA ===");
		return _falhou == 0 ? 0 : 1;
	}

	// =====================================================================
	// A TABELA DE VERDADE -- escrita a mao, conferida contra o dono
	// =====================================================================
	/// <summary>
	/// AS FORMAS QUE PEDEM RAIVA, E DE QUAL. **Escrita a mao de proposito**: se ela fosse gerada
	/// pelo `Catalogo.RaivaExigida` o teste passaria por construcao e nao provaria nada -- e o
	/// mesmo motivo pelo qual os multiplicadores da bancada `formas` estao a mao.
	///
	/// Quem nao esta aqui nao pede raiva nenhuma. Ver a secao [1] pros dois sentidos da conferencia.
	/// </summary>
	private static readonly (string Id, NivelDeRaiva Nivel)[] Esperado =
	[
		// O TRONCO DE SANGUE: "a mesma condicao do Beast" -- um amigo proximo morre na sua frente.
		("ssj1",               NivelDeRaiva.Extrema),
		("ssj2",               NivelDeRaiva.Extrema),
		("future_ssj",         NivelDeRaiva.Extrema),

		// A FERA: a unica divina por raiva, e ela e do degrau de cima junto com o tronco.
		("beast",              NivelDeRaiva.Extrema),

		// A LINHA LEGENDARY INTEIRA: o desconto da skill `legendary anger` -- basta ver um amigo cair.
		("wrathful",           NivelDeRaiva.Lendaria),
		("c_type",             NivelDeRaiva.Lendaria),
		("legendary",          NivelDeRaiva.Lendaria),
		("primal_c_type",      NivelDeRaiva.Lendaria),
		("primal_legendary",   NivelDeRaiva.Lendaria),
		("primal_legendary2",  NivelDeRaiva.Lendaria),

		// O HERAN. Ele entra aqui pelo mesmo motivo do tronco Saiyajin -- `heran.dm:20-52` roda o
		// MESMO `switch(savant.Emotion)` do `supersaiyan.dm` nos dois degraus, e o
		// `mst_form_needs_rage` (`MasterStudent.dm:246-249`) lista `heran1` e `heran2` ao lado de
		// `ssj` e `ssj2`. Ele nao ganha o desconto da `legendary anger`: aquela skill e da linha
		// Legendary e de mais ninguem.
		//
		// **E ELE E A ENTRADA QUE PEGOU UM DEFEITO REAL NA DERIVACAO.** Antes desta sessao o
		// `RaivaExigida` listava so `Saiyajin` e `Futuro`, com um comentario prometendo que a linha
		// Heran seria pega sozinha quando viesse. Nao seria: linha nova cai no `_ => Nenhuma`, e as
		// duas formas teriam nascido de graca, caladas.
		("heran1",             NivelDeRaiva.Extrema),
		("heran2",             NivelDeRaiva.Extrema),

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
		Console.WriteLine($"  -> LENDARIA: {Lista(NivelDeRaiva.Lendaria)}");
		Console.WriteLine();

		static string Lista(NivelDeRaiva n) =>
			string.Join(", ", Catalogo.Todas.Where(d => Catalogo.RaivaExigida(d) == n).Select(d => d.Id));
	}

	// =====================================================================
	// 2. A LINHA LEGENDARY INTEIRA CAI NO DESCONTO
	// =====================================================================
	/// <summary>
	/// A afirmacao do dono, dita como REGRA e nao como lista: *"vale pra `wrathful`, `c_type`,
	/// `legendary`, `primal_legendary2` **e o resto da linha**"*. O "resto da linha" e o que esta
	/// secao existe pra cobrir -- inclusive o degrau que ainda nao foi escrito.
	///
	/// COMO REPROVA SE A REGRA SUMIR: junte os quatro bracos de linha do `RaivaExigida` num so (a
	/// "simplificacao" obvia, ja que os quatro compartilham a conta do `tronco`) e a linha Legendary
	/// inteira passa a pedir a furia do luto -- o desconto some, e some CALADO, porque nao ha
	/// numero nem barra na tela que o mostre. Estas tres linhas caem na hora.
	/// </summary>
	private static void ALinhaLegendary()
	{
		Console.WriteLine("[2] A LINHA LEGENDARY INTEIRA CAI NA RAIVA LENDARIA");

		FormaDef[] daLinha = [.. Catalogo.Todas.Where(d => d.Linha is LinhaDeForma.Legendary
														   or LinhaDeForma.LegendaryPrimal)];
		Checa("a linha Legendary existe no catalogo (o teste nao rodou no vazio)",
			  daLinha.Length >= 8, $"{daLinha.Length} entradas");

		string[] furiosos = [.. daLinha.Where(d => Catalogo.RaivaExigida(d) == NivelDeRaiva.Extrema)
									   .Select(d => d.Id)];
		Checa("NENHUM degrau Legendary pede a furia do luto",
			  furiosos.Length == 0, string.Join(", ", furiosos));

		string[] descontados = [.. daLinha.Where(d => Catalogo.RaivaExigida(d) == NivelDeRaiva.Lendaria)
										  .Select(d => d.Id)];
		Checa($"e {descontados.Length} degraus dela abrem com a raiva lendaria",
			  descontados.Length >= 6, string.Join(", ", descontados));

		// OS QUE NAO PEDEM RAIVA NENHUMA SAO OS QUE SAIRAM DO TRONCO, e nao "os que sobraram". Sem
		// esta linha, um degrau Legendary que perdesse a raiva por engano se esconderia aqui dentro.
		foreach (FormaDef d in daLinha.Where(d => Catalogo.RaivaExigida(d) == NivelDeRaiva.Nenhuma))
			Checa($"`{d.Id}` nao pede raiva porque saiu do tronco (e treino)",
				  d.ForaDoTronco || d.PedeMaestria > 0 || d.PedeFormaDespertada.Length > 0,
				  "esta no tronco e mesmo assim nao pede raiva");

		// E O TRONCO SAIYAJIN NAO PEGOU O DESCONTO -- o par da checagem de cima. Se alguem trocar os
		// dois bracos de lugar, [1] reprova pelo nome e esta reprova pela regra.
		string[] baratos = [.. Catalogo.Todas
			.Where(d => d.Linha is LinhaDeForma.Saiyajin or LinhaDeForma.Futuro or LinhaDeForma.Mistico)
			.Where(d => Catalogo.RaivaExigida(d) == NivelDeRaiva.Lendaria).Select(d => d.Id)];
		Checa("nenhum degrau fora da linha Legendary pega o desconto",
			  baratos.Length == 0, string.Join(", ", baratos));

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
				(EstadoDeForma e2, PerfilDeFormas p2, _) = Preparado(d, NivelDeRaiva.Lendaria);
				RecusaForma r2 = e2.Avaliar(d.Id, bp, 1, false, p2);
				Checa($"`{d.Id}` com raiva LENDARIA so: ainda recusado (o desconto nao e dele)",
					  r2 == RecusaForma.SemFuria, r2.ToString());
			}

			// --- um degrau ACIMA: abre (o `>=`) --------------------------------------------
			if (pede == NivelDeRaiva.Lendaria)
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
	/// amigo de quem", e o `GameServer.LutoNaVizinhanca` e o chamador. O que a secao mede agora e o
	/// oposto e igualmente estrutural:
	///
	///   * o gancho TEM chamador de producao -- se alguem apagar o convivio, o SSJ1 volta a so sair
	///     por admin e ninguem perceberia, porque uma forma que nunca vem parece balanceamento;
	///   * e o chamador e UM SO. As duas janelas de raiva continuam sendo escritas exclusivamente
	///     pelo `AmigoAbatido` -- um segundo lugar que mexesse nelas na mao seria a porta dos fundos
	///     que este projeto ja abriu varias vezes sem querer.
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
		string[] permitidos =
			["GameServer.Formas.cs", "GameServer.FormasTeste.cs", "GameServer.RaivaTeste.cs",
			 "GameServer.ConvivioTeste.cs", "GameServer.RaciaisTeste.cs"];

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
		string[] podemEscreverAJanela =
			["GameServer.cs", "GameServer.Formas.cs", "GameServer.FormasTeste.cs",
			 "GameServer.RaivaTeste.cs", "GameServer.ConvivioTeste.cs", "GameServer.MestreTeste.cs",
			 "GameServer.MestreVivoTeste.cs", "GameServer.RaciaisTeste.cs"];

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
						if (Array.IndexOf(permitidos, nome) < 0) chamadores.Add($"{nome}:{i + 1}");
					}

					// E AS DUAS JANELAS DIRETAMENTE: quem quiser burlar o gancho escreveria no campo.
					bool escreve = (l.Contains("FuriaExtremaAte") || l.Contains("RaivaLendariaAte"))
								   && l.Contains('=') && !l.Contains("==") && !l.Contains(">");
					if (escreve && Array.IndexOf(podemEscreverAJanela, nome) < 0)
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
		Checa("e o chamador de producao e o `GameServer.Convivio.cs`, e so ele",
			  chamadores.All(c => c.StartsWith("GameServer.Convivio.cs")),
			  string.Join(" | ", chamadores));

		Checa("ninguem escreve as duas janelas de raiva por fora do gancho",
			  escritoresDaJanela.Count == 0, string.Join(" | ", escritoresDaJanela));

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
			  NivelDeRaiva.Nenhuma < NivelDeRaiva.Lendaria
			  && NivelDeRaiva.Lendaria < NivelDeRaiva.Extrema);

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
					if (l.Contains("estavaEmFuria")) wasRaging++;
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
		Checa("condicao 2: o `wasRaging` e lido (nao toca duas vezes na mesma furia)", wasRaging >= 2,
			  $"{wasRaging} usos -- ele nasce no gancho e e lido no gatilho");
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
}
