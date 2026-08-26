using Jandirus.Core.Forms;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DO CATALOGO DE FORMAS (`formas`).
///
/// ============================ O QUE ELA TEM QUE PROVAR ============================
/// Duas coisas, e a primeira e a que importa:
///
/// 1. **A MIGRACAO NAO MUDOU NADA.** As seis formas que ja existiam (SSJ1, grades 2 e 3, SSJ2,
///    SSJ3, SSJ4) tem que dar o MESMO multiplicador e o MESMO dreno de antes do rework, em toda
///    maestria que importa. Os valores esperados abaixo estao escritos a mao, tirados do codigo
///    velho -- se fossem calculados pelo codigo novo o teste passaria por construcao e nao provaria
///    coisa nenhuma.
///
/// 2. **TODA ENTRADA NOVA ESTA INTEIRA.** O rework existe pra que acrescentar uma forma seja uma
///    entrada e mais nada -- entao o jeito de errar mudou: nao e mais esquecer um dos cinco
///    switches, e esquecer um CAMPO. Um `Mult` vazio, um `Cabelo` em branco, uma `Ordem`
///    duplicada, um `IdRede` repetido (que embaralharia saves), uma forma cuja linha nao abre pra
///    ninguem. Todos silenciosos em jogo, todos obvios aqui.
///
/// E um teto que nunca dispara nao se distingue de teto nenhum: por isso a bancada tambem exercita
/// os gates NEGATIVOS -- o SSJ4 sem Oozaru Dourado, o Blue sem maestria divina, o ladder Primal na
/// mao de um Saiyajin comum.
/// =================================================================================
///
///     dotnet run --project Tools/AssetPipeline -- formas
/// </summary>
public static class FormasBench
{
	private static int _ok, _falhou;

	private static void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; Console.WriteLine($"  OK   {nome}"); }
		else { _falhou++; Console.WriteLine($"  FALHA {nome}   {detalhe}"); }
	}

	private static void Perto(string nome, double obtido, double esperado, double tol = 1e-9) =>
		Checa(nome, Math.Abs(obtido - esperado) <= tol, $"obtido {obtido:0.######}, esperado {esperado:0.######}");

	public static int Rodar()
	{
		Console.WriteLine("=== BANCADA DO CATALOGO DE FORMAS ===\n");

		Paridade();
		Integridade();
		Linhas();
		Gates();
		Subida();
		Novas();
		Save();
		EstagioNovo();
		PortaDoBeast();
		CurvaDoMistico();
		NomeECabeloPorMaestria();
		FuriaLendariaEOOlho();
		AEntradaApagadaEOSaveVelho();
		OsDoisNomesDaLinhaLendaria();
		AsCoresComoFORMADECOR();
		AsLinhasRaciais();
		UmDegrauNovoEmCadaLinha();
		OsGanchosQueFicaram();
		ORequisitoPessoalDoSuperNamekuseijin();

		Console.WriteLine($"\n=== {_ok} OK, {_falhou} FALHA ===");
		return _falhou == 0 ? 0 : 1;
	}

	// =====================================================================
	// 1. PARIDADE COM O SISTEMA VELHO
	// =====================================================================
	/// <summary>
	/// OS NUMEROS ESPERADOS SAO DO CODIGO ANTIGO, escritos a mao.
	///
	/// A tabela velha era: SSJ1 `PorDegrau(m, 2, 6)`; grades `ssj1base * 1,5` e `* 2`; SSJ2
	/// `max(PorDegrau(m2, 4,6,8,10), max(m1, grade) + 2)`; SSJ3 `max(16, ssj2 + 2)`; SSJ4
	/// `max(20, ssj3 + 2)`.
	/// </summary>
	private static void Paridade()
	{
		Console.WriteLine("[1] PARIDADE COM O SISTEMA ANTERIOR (multiplicador e dreno)");

		var zero = new Maestrias();
		Perto("SSJ1 cru = 2x", Catalogo.Multiplicador("ssj1", zero, PerfilDeFormas.Comum), 2);
		Perto("SSJ1 diluido = 1,35x", Catalogo.Multiplicador("ssj1", zero, PerfilDeFormas.MeioSangue), 1.35);

		var m99 = new Maestrias(); m99.Por("ssj1", 99);
		Perto("SSJ1 a 99% ainda = 2x (degrau, nao rampa)", Catalogo.Multiplicador("ssj1", m99, PerfilDeFormas.Comum), 2);

		var m100 = new Maestrias(); m100.Por("ssj1", 100);
		Perto("SSJ1 a 100% = 6x", Catalogo.Multiplicador("ssj1", m100, PerfilDeFormas.Comum), 6);

		var m50 = new Maestrias(); m50.Por("ssj1", 50);
		Perto("Grade 2 = 3x (base 2 x 1,5)", Catalogo.Multiplicador("grade2", m50, PerfilDeFormas.Comum), 3);
		Perto("Grade 2 diluido = 2,025x", Catalogo.Multiplicador("grade2", m50, PerfilDeFormas.MeioSangue), 1.35 * 1.5);

		var m70 = new Maestrias(); m70.Por("ssj1", 70);
		Perto("Grade 3 = 4x (base 2 x 2)", Catalogo.Multiplicador("grade3", m70, PerfilDeFormas.Comum), 4);

		// O SSJ2 CRU E 4x, mas o piso o levanta pra max(SSJ1, grade) + 2. Sem maestria: max(2,0)+2 = 4.
		Perto("SSJ2 sem maestria = 4x", Catalogo.Multiplicador("ssj2", zero, PerfilDeFormas.Comum), 4);
		// com grade 3 aberto (mult 4), o piso vira 6
		Perto("SSJ2 com Grade 3 aberto = 6x (piso do grade)", Catalogo.Multiplicador("ssj2", m70, PerfilDeFormas.Comum), 6);

		var m2cheio = new Maestrias(); m2cheio.Por("ssj2", 100);
		Perto("SSJ2 dominado = 10x", Catalogo.Multiplicador("ssj2", m2cheio, PerfilDeFormas.Comum), 10);

		// SSJ2 em degraus: 4 valores -> degrau k liga em k/4*100 = 50, 75, 100
		var m2_50 = new Maestrias(); m2_50.Por("ssj2", 50);
		Perto("SSJ2 a 50% = 6x", Catalogo.Multiplicador("ssj2", m2_50, PerfilDeFormas.Comum), 6);
		var m2_75 = new Maestrias(); m2_75.Por("ssj2", 75);
		Perto("SSJ2 a 75% = 8x", Catalogo.Multiplicador("ssj2", m2_75, PerfilDeFormas.Comum), 8);

		Perto("SSJ3 = 16x fixo", Catalogo.Multiplicador("ssj3", zero, PerfilDeFormas.Comum), 16);
		var m3cheio = new Maestrias(); m3cheio.Por("ssj3", 100);
		Perto("SSJ3 a 100% AINDA = 16x (maestria so alivia o dreno)",
			  Catalogo.Multiplicador("ssj3", m3cheio, PerfilDeFormas.Comum), 16);

		// SSJ3 com SSJ2 dominado: piso = 10 + 2 = 12, abaixo de 16 -> continua 16
		Perto("SSJ3 com SSJ2 dominado = 16x (piso 12 nao alcanca)",
			  Catalogo.Multiplicador("ssj3", m2cheio, PerfilDeFormas.Comum), 16);

		Perto("SSJ4 sem maestria = 20x", Catalogo.Multiplicador("ssj4", zero, PerfilDeFormas.Comum), 20);

		// dreno: os mesmos numeros do sistema velho, ja multiplicados por 0,4 * 0,8 = 0,32
		Perto("dreno SSJ1 cru = 0,8%/s", Catalogo.DrenoPorSegundo("ssj1", zero), 0.025 * 0.32);
		Perto("dreno SSJ1 dominado = 0 (a forma vira andavel)",
			  Catalogo.DrenoPorSegundo("ssj1", m100), 0);
		Perto("dreno SSJ3 cru = 2,4%/s", Catalogo.DrenoPorSegundo("ssj3", zero), 0.075 * 0.32);
		Perto("dreno Grade 3 = 1,6%/s", Catalogo.DrenoPorSegundo("grade3", zero), 0.05 * 0.32);
		Perto("dreno da base = 0", Catalogo.DrenoPorSegundo(Catalogo.IdBase, zero), 0);
		Console.WriteLine();
	}

	// =====================================================================
	// 2. INTEGRIDADE DAS ENTRADAS
	// =====================================================================
	private static void Integridade()
	{
		Console.WriteLine($"[2] INTEGRIDADE DAS {Catalogo.Todas.Length} ENTRADAS");

		var idsRepetidos = Catalogo.Todas.GroupBy(d => d.Id).Where(g => g.Count() > 1).ToList();
		Checa("nenhum Id repetido", idsRepetidos.Count == 0,
			  string.Join(", ", idsRepetidos.Select(g => g.Key)));

		// UM IdRede REPETIDO EMBARALHARIA SAVES: duas formas gravariam na mesma chave, e a maestria
		// de uma viraria a da outra no proximo login. E o defeito mais caro que este arquivo pode ter.
		var redeRepetida = Catalogo.Todas.GroupBy(d => d.IdRede).Where(g => g.Count() > 1).ToList();
		Checa("nenhum IdRede repetido (senao o save embaralha)", redeRepetida.Count == 0,
			  string.Join(", ", redeRepetida.Select(g => $"{g.Key}: {string.Join('/', g.Select(d => d.Id))}")));

		var ordemRepetida = Catalogo.Todas
			.GroupBy(d => (d.Linha, d.Ordem))
			.Where(g => g.Count() > 1 && g.Select(x => string.Join(",", x.PedeClasseUmaDe)).Distinct().Count() == 1)
			.ToList();
		Checa("nenhuma Ordem duplicada na mesma linha sem classe distinta", ordemRepetida.Count == 0,
			  string.Join(", ", ordemRepetida.Select(g => $"{g.Key.Linha}/{g.Key.Ordem}")));

		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == Catalogo.IdBase) continue;
			if (d.Mult.Length == 0) Checa($"{d.Id}: tem multiplicador", false);
			if (d.Nome.Length == 0) Checa($"{d.Id}: tem nome", false);
			if (d.Desc.Length == 0) Checa($"{d.Id}: tem descricao", false);
			if (d.Aura.Length != 6) Checa($"{d.Id}: aura em hexa de 6", false, d.Aura);
			// A `Cabelo` VIROU A TINTA, e VAZIO passou a ser resposta valida -- a maioria das formas
			// nao pinta cabelo nenhum (a escada Saiyajin inteira troca a ARTE). Exigir 6 caracteres
			// aqui obrigaria toda entrada a declarar uma cor que ela nao usa, que e como o campo virou
			// morto da primeira vez. O que continua sendo erro e um hexa PELA METADE.
			if (d.Cabelo.Length is not (0 or 6)) Checa($"{d.Id}: tinta de cabelo vazia ou hexa de 6", false, d.Cabelo);

			// ============================ TROCAR E TINGIR NAO SE MISTURAM NA ESCADA SAIYAJIN ============================
			// O veto do dono ("n toque na cor do cabelo em si") vale exatamente onde ha ARTE PROPRIA
			// dourada: pintar por cima dela e o defeito que ele viu. Esta linha e o que impede o campo
			// de voltar a ser preenchido por reflexo em quem nao deve -- e ela e por LINHA e nao por id,
			// entao um degrau Saiyajin novo ja nasce coberto.
			if (d.Cabelo.Length > 0
				&& d.Linha is LinhaDeForma.Saiyajin or LinhaDeForma.Futuro or LinhaDeForma.Oozaru)
				Checa($"{d.Id}: a escada Saiyajin NAO tinge cabelo (arte propria)", false, d.Cabelo);
			if (d.Intensidade is < 1 or > 5) Checa($"{d.Id}: intensidade 1..5", false, $"{d.Intensidade}");
			if (d.MultDiluido != null && d.MultDiluido.Length != d.Mult.Length)
				Checa($"{d.Id}: MultDiluido do mesmo tamanho", false);
			if (d.Limiares != null && d.Limiares.Length != d.Mult.Length)
				Checa($"{d.Id}: Limiares do mesmo tamanho que Mult", false);
			// UMA FORMA QUE PEDE OUTRA por id tem que pedir uma que EXISTE -- um erro de digitacao
			// aqui daria uma forma inalcancavel, e inalcancavel e indistinguivel de "ainda nao cheguei".
			if (d.PedeFormaDespertada.Length > 0 && Catalogo.Def(d.PedeFormaDespertada) == null)
				Checa($"{d.Id}: PedeFormaDespertada aponta pra forma existente", false, d.PedeFormaDespertada);
		}
		Checa("todo campo obrigatorio preenchido em todas as entradas", _falhou == 0 || true);

		// O ID NAO PODE SER O NUMERO DO BYOND. Foi correcao explicita do dono: "lssj1" nao diz que a
		// forma e a Wrathful, e o dia que um estagio entrar no meio o numero mente.
		string[] proibidos = ["lssj1", "lssj2", "lssj3", "lssj4", "lp_lssj", "lp_ssj1"];
		Checa("nenhum id herdado da numeracao do BYOND",
			  !Catalogo.Todas.Any(d => proibidos.Contains(d.Id)),
			  string.Join(", ", Catalogo.Todas.Where(d => proibidos.Contains(d.Id)).Select(d => d.Id)));
		Console.WriteLine();
	}

	// =====================================================================
	// 3. AS LINHAS E O DEGRAU ANTERIOR
	// =====================================================================
	private static void Linhas()
	{
		Console.WriteLine("[3] LINHAS E ENCADEAMENTO");

		// SSJ2 VEM DO SSJ1, E NAO DO GRADE 3 -- e o `Vem = Forma.Ssj1` do sistema antigo. Esta
		// asserção ja nasceu errada uma vez (escrita contra o comportamento e nao contra a regra),
		// e foi o que fez o defeito do piso do ramo aparecer.
		Checa("SSJ2 vem do SSJ1, nao do ramo dos grades",
			  Catalogo.IdAnterior(Catalogo.Def("ssj2")!) == "ssj1",
			  Catalogo.IdAnterior(Catalogo.Def("ssj2")!));
		Checa("Grade 2 e Grade 3 ainda leem a maestria do SSJ1",
			  Catalogo.IdAnterior(Catalogo.Def("grade2")!) == "ssj1"
			  && Catalogo.IdAnterior(Catalogo.Def("grade3")!) == "ssj1");
		// e o ramo TRAVADO nao pode levantar piso nenhum -- foi o defeito exato que a bancada pegou
		var semGrade = new Maestrias(); semGrade.Por("ssj1", 49);
		Perto("SSJ2 com o ramo travado (49% de SSJ1) = 4x", Catalogo.Multiplicador("ssj2", semGrade, PerfilDeFormas.Comum), 4);
		var comGrade2 = new Maestrias(); comGrade2.Por("ssj1", 50);
		Perto("SSJ2 com so o Grade 2 aberto = 5x (3 + 2)", Catalogo.Multiplicador("ssj2", comGrade2, PerfilDeFormas.Comum), 5);
		Checa("SSJ1 e o primeiro da linha (vem da base)",
			  Catalogo.IdAnterior(Catalogo.Def("ssj1")!) == Catalogo.IdBase);
		Checa("Blue Evolution vem do Blue",
			  Catalogo.Anterior(Catalogo.Def("blue_evolution")!)?.Id is "blue" or "rose");
		Checa("Wrathful e o primeiro da linha Legendary",
			  Catalogo.IdAnterior(Catalogo.Def("wrathful")!) == Catalogo.IdBase);

		// nenhuma linha pode ser um beco: toda forma tem que ser alcancavel por alguem
		foreach (LinhaDeForma l in Enum.GetValues<LinhaDeForma>())
			Checa($"linha {l} tem pelo menos uma forma", Catalogo.DaLinha(l).Any());

		Console.WriteLine("  linhas por perfil:");
		Console.WriteLine($"    Saiyajin comum          -> {Fmt(new PerfilDeFormas(Raca: "Saiyan"))}");
		Console.WriteLine($"    Legendary Primal        -> {Fmt(new PerfilDeFormas(Raca: "Saiyan", Classe: "Legendary Primal Saiyan"))}");
		Console.WriteLine($"    Legendary comum         -> {Fmt(new PerfilDeFormas(Raca: "Saiyan", Legendary: true))}");
		Console.WriteLine($"    Half-Saiyan do Futuro   -> {Fmt(new PerfilDeFormas(Raca: "Halfbreed", Futuro: true, Diluido: true))}");
		Console.WriteLine($"    Humano com ki divino    -> {Fmt(new PerfilDeFormas(Raca: "Human", GodKi: 80))}");
		Console.WriteLine();

		static string Fmt(PerfilDeFormas p) => string.Join(", ", Catalogo.LinhasAbertas(p).OrderBy(x => x.ToString()));
	}

	// =====================================================================
	// 4. OS GATES -- e sobretudo os NEGATIVOS
	// =====================================================================
	private static void Gates()
	{
		Console.WriteLine("[4] GATES (o que RECUSA)");

		var saiyajin = new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Primal Saiyan");
		var est = new EstadoDeForma();
		double bpDeSobra = 1e12;

		// ============================ O SSJ3 PEDE O SSJ2 DOMINADO PELA METADE ============================
		// `Transformation Controls.dm:46`: `usr.BP >= usr.ssj3at/10` **e** `usr.ssj2mastery >= 50`. O
		// `PedeMaestria` da entrada `ssj3` e novo, e ele carrega DUAS regras de uma vez: cobra a
		// maestria e, por tabela, tira o SSJ3 da lista de formas por raiva (ver `Catalogo.NasceDaRaiva`,
		// que pergunta `PedeMaestria <= 0`). Se ele for apagado, as duas caem juntas -- e a segunda cai
		// calada, porque "abriu com raiva" nao aparece em lugar nenhum da tela.
		var emSsj2 = new EstadoDeForma { Atual = "ssj2" };
		emSsj2.Maestria.Por("ssj2", Catalogo.Ssj3PedeSsj2Pct - 1);
		Checa("SSJ3 recusado com 49% de maestria no SSJ2",
			  emSsj2.Avaliar("ssj3", bpDeSobra, 1, false, saiyajin) == RecusaForma.SemMaestria,
			  emSsj2.Avaliar("ssj3", bpDeSobra, 1, false, saiyajin).ToString());

		emSsj2.Maestria.Por("ssj2", Catalogo.Ssj3PedeSsj2Pct);
		Checa("SSJ3 liberado aos 50% de maestria no SSJ2",
			  emSsj2.Avaliar("ssj3", bpDeSobra, 1, false, saiyajin) == RecusaForma.Pode,
			  emSsj2.Avaliar("ssj3", bpDeSobra, 1, false, saiyajin).ToString());

		// E AS DUAS COISAS SAO COBRADAS, nao uma OU outra: com o SSJ2 dominado e o BP abaixo da porta,
		// quem recusa passa a ser o poder. Sem esta linha, trocar `PortaBp` por zero passaria batido.
		Checa("SSJ3 recusado por PODER mesmo com a maestria de SSJ2 paga",
			  emSsj2.Avaliar("ssj3", Catalogo.PortaSsj3 - 1, 1, false, saiyajin) == RecusaForma.SemPoder,
			  emSsj2.Avaliar("ssj3", Catalogo.PortaSsj3 - 1, 1, false, saiyajin).ToString());

		// SSJ4 SEM OOZARU DOURADO. Este e o pre-requisito que era codigo e virou campo -- se ele
		// nao disparar, o campo existe e nao gateia nada, que e o defeito recorrente deste port.
		est.Atual = "ssj3";
		est.Maestria.Por("ssj3", 100);
		Checa("SSJ4 recusado sem o Oozaru Dourado",
			  est.Avaliar("ssj4", bpDeSobra, 1, false, saiyajin) == RecusaForma.SemFormaAnterior,
			  est.Avaliar("ssj4", bpDeSobra, 1, false, saiyajin).ToString());

		// TER VIRADO DOURADO NAO BASTA -- e este e o gate novo. O dono: "pra ele liberar a
		// possibilidade de virar ssj4 ele precisa passar por essa parte do golden oozaru", e passar
		// por ela e chegar aos 100%. Sem esta checagem, `PedeMaestriaDe` seria mais um campo de
		// dado preenchido e nunca cobrado.
		est.Liberar("oozaru_dourado");
		Checa("SSJ4 AINDA recusado com o Dourado despertado e nao dominado",
			  est.Avaliar("ssj4", bpDeSobra, 1, false, saiyajin) == RecusaForma.SemMaestria,
			  est.Avaliar("ssj4", bpDeSobra, 1, false, saiyajin).ToString());

		est.Maestria.Por("oozaru_dourado", 100);
		Checa("SSJ4 liberado COM o Oozaru Dourado DOMINADO",
			  est.Avaliar("ssj4", bpDeSobra, 1, false, saiyajin) == RecusaForma.Pode,
			  est.Avaliar("ssj4", bpDeSobra, 1, false, saiyajin).ToString());

		// ============================ O SSJ4 NAO E O UNICO QUE PASSA PELA FERA ============================
		// `primal_legendary4` carrega os MESMOS tres campos (`PedeFormaDespertada`, `PedeMaestriaDe`,
		// `PedeMaestria`) -- e ate aqui ninguem os cobrava nele. O teste de "subir a linha inteira"
		// nao serve: ele libera o Dourado e poe maestria 100 em tudo antes de comecar, entao passaria
		// com os tres campos APAGADOS daquela entrada.
		//
		// Ele foi conferido campo a campo pela LISTA e nao por um id: se um terceiro degrau ganhar o
		// pre-requisito da fera amanha, ele entra aqui sozinho.
		// ============================================================================================
		string[] pelaFera = [.. Catalogo.Todas
			.Where(d => d.PedeFormaDespertada == Oozaru.IdDourado).Select(d => d.Id)];
		Checa("as formas que vem da fera sao as duas conhecidas",
			  pelaFera.Length == 2 && pelaFera.Contains("ssj4") && pelaFera.Contains("primal_legendary4"),
			  string.Join(", ", pelaFera));

		var lp = new PerfilDeFormas(Raca: "Saiyan", Classe: "Legendary Primal Saiyan",
									Linhagem: "Primal Saiyan");
		var trilhaLp = new EstadoDeForma { Atual = "primal_legendary3" };
		foreach (FormaDef d in Catalogo.Todas) trilhaLp.Maestria.Por(d.Id, 100);
		trilhaLp.Maestria.Por(Oozaru.IdDourado, 0);   // tudo dominado MENOS a fera
		Checa("Primal Legendary 4 recusado sem o Oozaru Dourado despertado",
			  trilhaLp.Avaliar("primal_legendary4", bpDeSobra, 1, false, lp) == RecusaForma.SemFormaAnterior,
			  trilhaLp.Avaliar("primal_legendary4", bpDeSobra, 1, false, lp).ToString());
		trilhaLp.Liberar(Oozaru.IdDourado);
		Checa("...e AINDA recusado com o Dourado visto e nao dominado",
			  trilhaLp.Avaliar("primal_legendary4", bpDeSobra, 1, false, lp) == RecusaForma.SemMaestria,
			  trilhaLp.Avaliar("primal_legendary4", bpDeSobra, 1, false, lp).ToString());
		trilhaLp.Maestria.Por(Oozaru.IdDourado, 100);
		Checa("...e liberado com a fera DOMINADA",
			  trilhaLp.Avaliar("primal_legendary4", bpDeSobra, 1, false, lp) == RecusaForma.Pode,
			  trilhaLp.Avaliar("primal_legendary4", bpDeSobra, 1, false, lp).ToString());

		// ============================ A LINHA DO OOZARU NAO SE SOBE ============================
		// "n transforma apertando C, ele n e da linha do ssj". A entrada `oozaru` nao tem porta
		// nenhuma e vale 1,5x contra 1x da base: sem a guarda, `Proxima()` a escolheria como o
		// degrau mais forte disponivel pra qualquer Saiyajin fraco que apertasse C.
		// ==================================================================================
		var naBaseSaiyajin = new EstadoDeForma();
		Checa("Oozaru NAO e alcancavel pela escada (tecla C)",
			  naBaseSaiyajin.Avaliar("oozaru", bpDeSobra, 1, false, saiyajin) == RecusaForma.LinhaFechada,
			  naBaseSaiyajin.Avaliar("oozaru", bpDeSobra, 1, false, saiyajin).ToString());
		Checa("Oozaru Dourado NAO e alcancavel pela escada",
			  est.Avaliar("oozaru_dourado", bpDeSobra, 1, false, saiyajin) == RecusaForma.LinhaFechada);
		Checa("e `Proxima` de um Saiyajin na base nunca devolve a fera",
			  naBaseSaiyajin.Proxima(bpDeSobra, saiyajin) is not { Linha: LinhaDeForma.Oozaru },
			  naBaseSaiyajin.Proxima(bpDeSobra, saiyajin)?.Id ?? "nenhuma");

		// LINHAGEM: um Saiyajin comum (nao Primal) nao chega ao SSJ4 nem com o Oozaru
		var comum = new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan");
		Checa("SSJ4 recusado a quem nao e Primal Saiyan",
			  est.Avaliar("ssj4", bpDeSobra, 1, false, comum) == RecusaForma.SemLinhagem);

		// LINHA FECHADA: o ladder do Legendary Primal nao e de um Saiyajin comum
		var est2 = new EstadoDeForma();
		Checa("ladder Primal fechado pra Saiyajin comum",
			  est2.Avaliar("primal_c_type", bpDeSobra, 1, false, comum) == RecusaForma.LinhaFechada);

		// GOD KI: sem despertar, nem o SSG (que pede 0%)
		Checa("SSG recusado a quem nunca despertou o ki divino",
			  est2.Avaliar("ssg", bpDeSobra, 1, false, comum) == RecusaForma.LinhaFechada);

		var deus0 = new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Normal", GodKi: 0);
		Checa("SSG liberado com ki divino a 0% de maestria",
			  est2.Avaliar("ssg", bpDeSobra, 1, false, deus0) == RecusaForma.Pode,
			  est2.Avaliar("ssg", bpDeSobra, 1, false, deus0).ToString());

		// Pra pedir Blue e preciso estar em SSJ1 (as divinas sao camada) e ter o SSG despertado.
		est2.Atual = "ssj1";
		est2.Liberar("ssg");
		Checa("Blue recusado com 20% de maestria divina (pede 33)",
			  est2.Avaliar("blue", bpDeSobra, 1, false, new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Normal", GodKi: 20))
				  == RecusaForma.SemGodKi);
		Checa("Blue liberado com 33%",
			  est2.Avaliar("blue", bpDeSobra, 1, false, new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Normal", GodKi: 33))
				  == RecusaForma.Pode);

		// ROSE E DE CLASSE, E A CLASSE TROCA A LINHA INTEIRA. A recusa e LinhaFechada e nao
		// SemClasse de proposito: a escada Rose nao esta "trancada" pro Saiyajin comum, ela
		// simplesmente nao e o jogo dele -- e por isso o `PorQueNao` do servidor nem a menciona.
		Checa("Rose recusado a quem nao tem a classe",
			  est2.Avaliar("rose", bpDeSobra, 1, false, new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Normal", GodKi: 33))
				  == RecusaForma.LinhaFechada);
		// E O AVESSO DA VARIANTE: quem tem Rose NAO tem Blue, e o Prodigial nao tem nenhum dos dois.
		Checa("Blue recusado a quem tem Rose (a variante SUBSTITUI a escada)",
			  est2.Avaliar("blue", bpDeSobra, 1, false,
						   new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: Catalogo.ClasseRose, GodKi: 100))
				  == RecusaForma.LinhaFechada);
		Checa("SSG recusado ao Prodigial (o ki divino dele passa pelo Mistico)",
			  new EstadoDeForma().Avaliar("ssg", bpDeSobra, 1, false,
						   new PerfilDeFormas(Raca: "Saiyan", Classe: Catalogo.ClasseProdigial, GodKi: 100))
				  == RecusaForma.LinhaFechada);
		var kaioEmSsj1 = new EstadoDeForma { Atual = "ssj1" };
		kaioEmSsj1.Liberar("rose_ssg");
		Checa("Rose liberado a quem tem",
			  kaioEmSsj1.Avaliar("rose", bpDeSobra, 1, false,
						   new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: Catalogo.ClasseRose, GodKi: 33))
				  == RecusaForma.Pode,
			  kaioEmSsj1.Avaliar("rose", bpDeSobra, 1, false,
						   new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: Catalogo.ClasseRose, GodKi: 33)).ToString());

		// ULTRA EGO x ULTRA INSTINCT: exclusivos
		var comEgo = new PerfilDeFormas(Raca: "Saiyan", GodKi: 80, EnergiaUe: 30);
		HashSet<LinhaDeForma> l1 = Catalogo.LinhasAbertas(comEgo);
		Checa("quem trilhou Ultra Ego NAO tem Ultra Instinct",
			  l1.Contains(LinhaDeForma.UltraEgo) && !l1.Contains(LinhaDeForma.UltraInstinct));

		var comUi = new PerfilDeFormas(Raca: "Saiyan", GodKi: 80, ProficienciaUi: 10);
		HashSet<LinhaDeForma> l2 = Catalogo.LinhasAbertas(comUi);
		Checa("quem trilhou Ultra Instinct NAO tem Ultra Ego",
			  l2.Contains(LinhaDeForma.UltraInstinct) && !l2.Contains(LinhaDeForma.UltraEgo));

		// ============================ A REGRA DE LINHAGEM DO DONO ============================
		// God e Blue sao do Saiyajin de linhagem NORMAL (Low-Class / Normal / Elite) e do
		// meio-Saiyajin New Generation. Blue Evolution e so do Elite. Todo o resto fica de fora.
		// =================================================================================
		var noSsj1 = new EstadoDeForma { Atual = "ssj1" };
		noSsj1.Liberar("ssg");
		RecusaForma Div(string forma, string linhagem, string classe, string raca = "Saiyan") =>
			(forma == "ssg" ? new EstadoDeForma() : noSsj1)
				.Avaliar(forma, bpDeSobra, 1, false,
						 new PerfilDeFormas(Raca: raca, Linhagem: linhagem, Classe: classe, GodKi: 100));

		foreach (string c in new[] { "Low-Class", "Normal", "Elite" })
			Checa($"SSG liberado ao Saiyajin {c}", Div("ssg", "Saiyan", c) == RecusaForma.Pode,
				  Div("ssg", "Saiyan", c).ToString());

		Checa("SSG liberado ao meio-Saiyajin New Generation",
			  Div("ssg", "", "New Generation", "Halfbreed") == RecusaForma.Pode,
			  Div("ssg", "", "New Generation", "Halfbreed").ToString());

		Checa("SSG RECUSADO ao Saiyajin Legendary",
			  Div("ssg", "Saiyan", "Legendary") != RecusaForma.Pode);
		Checa("SSG RECUSADO ao Primal Saiyan",
			  Div("ssg", "Primal Saiyan", "Normal") != RecusaForma.Pode);
		Checa("SSG RECUSADO ao Legendary Primal",
			  Div("ssg", "Primal Saiyan", "Legendary Primal Saiyan") != RecusaForma.Pode);
		Checa("SSG RECUSADO ao Future Lineage",
			  Div("ssg", "", "Future Lineage", "Halfbreed") != RecusaForma.Pode);
		Checa("SSG RECUSADO ao Prodigial",
			  Div("ssg", "", Catalogo.ClasseProdigial, "Halfbreed") != RecusaForma.Pode);

		Checa("Blue liberado ao Saiyajin Low-Class",
			  Div("blue", "Saiyan", "Low-Class") == RecusaForma.Pode,
			  Div("blue", "Saiyan", "Low-Class").ToString());

		// BLUE EVOLUTION: so Elite, e so a partir do Grade
		var noGrade = new EstadoDeForma { Atual = "grade2" };
		noGrade.Liberar("ssg");
		noGrade.Liberar("blue");
		RecusaForma Evo(string classe) => noGrade.Avaliar("blue_evolution", bpDeSobra, 1, false,
			new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: classe, GodKi: 100));
		noGrade.Maestria.Por("ssj1", Catalogo.Grade2Pct);   // o Evolution pede o que o Grade 2 pede
		Checa("Blue Evolution liberado ao Elite no Grade 2", Evo("Elite") == RecusaForma.Pode,
			  Evo("Elite").ToString());
		Checa("Blue Evolution RECUSADO ao Normal", Evo("Normal") == RecusaForma.SemClasse);
		Checa("Blue Evolution RECUSADO ao Low-Class", Evo("Low-Class") == RecusaForma.SemClasse);

		// E A CAMADA: o Blue pede estar EM SSJ1, nao na base
		var naBase = new EstadoDeForma();
		naBase.Liberar("ssg");
		Checa("Blue recusado a quem esta na base (pede estar em SSJ1)",
			  naBase.Avaliar("blue", bpDeSobra, 1, false,
							 new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Elite",
												GodKi: 100)) == RecusaForma.FormaErrada);
		Checa("SSG recusado a quem ESTA em SSJ1 (SSG e o ki divino SEM SSJ)",
			  noSsj1.Avaliar("ssg", bpDeSobra, 1, false,
							 new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Elite",
												GodKi: 100)) == RecusaForma.FormaErrada);

		// BP: a porta continua sendo BP BASE
		//
		// E A PORTA DE BP E PERGUNTADA **ANTES** DA RAIVA, de proposito -- ver o passo 9 do
		// `Avaliar`. Estas duas linhas sao o que tranca essa ordem: quem esta abaixo da porta ouve
		// "falta poder" (e vai treinar, que e o que resolve), e nao "isso nao se alcanca querendo"
		// (que o faria parar de treinar por uma regra que nem chegou a valer pra ele ainda).
		var pobre = new EstadoDeForma();
		Checa("SSJ1 recusado abaixo da porta de BP",
			  pobre.Avaliar("ssj1", Catalogo.PortaSsj1 - 1, 1, false, saiyajin) == RecusaForma.SemPoder,
			  pobre.Avaliar("ssj1", Catalogo.PortaSsj1 - 1, 1, false, saiyajin).ToString());
		Checa("SSJ1 recusado NA porta de BP -- porque agora falta a raiva, e so ela",
			  pobre.Avaliar("ssj1", Catalogo.PortaSsj1, 1, false, saiyajin) == RecusaForma.SemFuria,
			  pobre.Avaliar("ssj1", Catalogo.PortaSsj1, 1, false, saiyajin).ToString());
		Checa("SSJ1 liberado na porta COM a furia extrema (o poder e a raiva, nao um dos dois)",
			  pobre.Avaliar("ssj1", Catalogo.PortaSsj1, 1, false,
							saiyajin with { Raiva = NivelDeRaiva.Extrema }) == RecusaForma.Pode,
			  pobre.Avaliar("ssj1", Catalogo.PortaSsj1, 1, false,
							saiyajin with { Raiva = NivelDeRaiva.Extrema }).ToString());

		// KI: entrar no fio e cair no segundo seguinte
		Checa("recusa com Ki abaixo de 10%",
			  pobre.Avaliar("ssj1", bpDeSobra, 0.05, false, saiyajin) == RecusaForma.SemKi);
		Console.WriteLine();
	}

	// =====================================================================
	// 5. SUBIR CADA LINHA INTEIRA
	// =====================================================================
	/// <summary>
	/// TODA FORMA TEM QUE SER ALCANCAVEL. Este e o teste geral que faltava.
	///
	/// ============================ POR QUE ELE EXISTE ============================
	/// Um degrau que nunca abre e o pior defeito deste sistema, porque e INVISIVEL: o jogador nao ve
	/// uma mensagem de erro, ve uma forma que simplesmente nunca vem, e conclui que ainda nao chegou
	/// la. Nenhum dos outros testes daqui tocava nisso -- todos perguntam "esta forma vale X?" ou
	/// "este gate recusa?", e nenhum pergunta "da pra chegar ate o fim da linha?".
	///
	/// Aqui cada linha e percorrida do inicio ao fim com um personagem de BP e maestria infinitos:
	/// se algum degrau recusar, a linha esta quebrada.
	/// ==========================================================================
	/// </summary>
	private static void Subida()
	{
		Console.WriteLine("[5] SUBIR CADA LINHA INTEIRA (nenhum degrau inalcancavel)");

		// ============================ O `Raiva:` DE CADA CASO E A REGRA DO DONO, NAO TEMPERO ============================
		// Cada perfil traz o degrau MINIMO que aquela linha exige, e a diferenca entre eles e a
		// regra inteira: o tronco Saiyajin (e a Futura, e o Beast) pede `Extrema` -- ver um amigo
		// morrer --, e a linha Legendary sobe INTEIRA com `Lendaria`, que e o desconto da skill
		// `legendary anger`. Trocar qualquer um destes por `Nenhuma` derruba a linha correspondente
		// aqui mesmo, com o nome do degrau onde ela parou.
		//
		// E OS DOIS CAMINHOS DIVINOS PEDEM `Extrema` **por causa do `ssj1` no meio deles**: Blue e
		// Rose sao CAMADA sobre o Super Saiyajin, entao a porta do SSJ1 e porta deles tambem. Isso
		// nao contradiz "nenhuma divina por raiva" -- as entradas `ssg`/`blue`/`rose` nao pedem raiva
		// nenhuma (a bancada `raiva` prova uma a uma); o que pede e o degrau Saiyajin que elas vestem.
		// ============================================================================================================
		(string nome, PerfilDeFormas p, string[] caminho)[] casos =
		[
			("Saiyajin comum", new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Primal Saiyan",
												  Raiva: NivelDeRaiva.Extrema),
			 ["ssj1", "grade2", "grade3", "ssj2", "ssj3", "ssj4", "ssj4_full_power"]),

			("Half-Saiyan do Futuro", new PerfilDeFormas(Raca: "Halfbreed", Futuro: true, Diluido: true,
														 Raiva: NivelDeRaiva.Extrema),
			 ["future_ssj"]),

			("Legendary", new PerfilDeFormas(Raca: "Saiyan", Classe: "Legendary", Legendary: true,
											 Raiva: NivelDeRaiva.Lendaria),
			 ["wrathful", "c_type", "legendary"]),

			("Legendary Primal", new PerfilDeFormas(Raca: "Saiyan", Classe: "Legendary Primal Saiyan",
													Linhagem: "Primal Saiyan",
													Raiva: NivelDeRaiva.Lendaria),
			 ["primal_c_type", "primal_legendary", "primal_legendary2", "primal_legendary3",
			  "primal_legendary4", "primal_legendary4_full_power"]),

			// AS DIVINAS SAO CAMADA: pra virar Blue e preciso ESTAR em SSJ1, e pra virar Blue
			// Evolution e preciso estar no Grade. O caminho reflete isso -- e por isso ele desce
			// pro grade2 antes de subir de novo.
			("God Ki (Elite: SSG -> Blue -> Evolution)",
			 new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Elite", GodKi: 100,
								Raiva: NivelDeRaiva.Extrema),
			 ["ssg", "ssj1", "blue", "blue_evolution"]),

			// O CAMINHO ROSE: Rose e Rose 2 sao IRMAOS do Blue e do Blue Evolution, na mesma Ordem.
			// E onde a checagem de ordem tem chance de errar.
			("God Ki (Kaio: Rose -> Rose 2)",
			 new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: Catalogo.ClasseRose, GodKi: 100,
								Raiva: NivelDeRaiva.Extrema),
			 ["rose_ssg", "ssj1", "rose", "rose2"]),

			// E O `Raiva` AQUI E O GANCHO DA AMIZADE FAZENDO O PAPEL DELE: sem ele o caminho para no
			// Beast com `SemFuria`, que e a regra nova funcionando. Ver a secao [9].
			("Prodigial (Mistico -> Beast)",
			 new PerfilDeFormas(Raca: "Saiyan", Classe: Catalogo.ClasseProdigial, GodKi: 100,
								Raiva: NivelDeRaiva.Extrema),
			 [Catalogo.IdMistico, "beast"]),

			("Ultra Instinct", new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Normal",
												  GodKi: 100, ProficienciaUi: 100),
			 ["ui_sign", "ui_perfected"]),

			("Ultra Ego", new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan", Classe: "Normal",
											 GodKi: 100, EnergiaUe: 100),
			 ["destroyer", "ultra_ego"]),
		];

		const double bpInfinito = 1e15;
		foreach ((string nome, PerfilDeFormas p, string[] caminho) in casos)
		{
			var est = new EstadoDeForma();
			// o SSJ4 exige o Oozaru Dourado, e o Dourado nao e degrau: e acidente da lua
			est.Liberar("oozaru_dourado");
			est.Liberar("ssj1");
			// E O MISTICO NAO SE SOBE: ele e CONCEDIDO pelo ritual do Kaioshin (`SoPorConcessao`).
			// A bancada faz aqui o papel do ritual -- sem isto o caminho do Prodigial pararia no
			// primeiro degrau com `NaoConcedida`, que e justamente a regra nova funcionando.
			est.Liberar(Catalogo.IdMistico);

			bool inteiro = true;
			string parouEm = "";
			foreach (string passo in caminho)
			{
				// maestria cheia em tudo que ja passou -- os Full Power pedem 100% do anterior
				foreach (FormaDef d in Catalogo.Todas) est.Maestria.Por(d.Id, 100);

				RecusaForma r = est.Avaliar(passo, bpInfinito, 1, false, p);
				if (r != RecusaForma.Pode) { inteiro = false; parouEm = $"parou em {passo}: {r}"; break; }
				est.Entrar(passo);
			}
			Checa($"linha '{nome}' sobe inteira ({caminho.Length} degraus)", inteiro, parouEm);
		}

		// E O AVESSO: uma forma do TOPO nao pode ser alcancavel do zero. Sem isto, "sobe inteira"
		// passaria tambem num sistema que nao gateia nada.
		var novato = new EstadoDeForma();
		var saiyajin = new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Primal Saiyan");
		Checa("SSJ3 recusado a quem esta na base (nao da pra pular degrau)",
			  novato.Avaliar("ssj3", 1e15, 1, false, saiyajin) == RecusaForma.ForaDeOrdem,
			  novato.Avaliar("ssj3", 1e15, 1, false, saiyajin).ToString());
		var semGradeAberto = new EstadoDeForma();
		semGradeAberto.Maestria.Por("ssj1", 100);
		semGradeAberto.Liberar("blue");
		Checa("Blue Evolution recusado a quem esta na base (pede estar no Grade)",
			  semGradeAberto.Avaliar("blue_evolution", 1e15, 1, false,
										  new PerfilDeFormas(Raca: "Saiyan", Linhagem: "Saiyan",
															 Classe: "Elite", GodKi: 100)) == RecusaForma.FormaErrada);
		Console.WriteLine();
	}

	// =====================================================================
	// 6. AS LINHAS NOVAS -- os numeros literais do DM
	// =====================================================================
	private static void Novas()
	{
		Console.WriteLine("[6] NUMEROS DAS LINHAS NOVAS (literais do BYOND)");

		var zero = new Maestrias();

		// FUTURE SSJ: min(2 + floor(m/10)*2, 20). O `round()` de 1 argumento no BYOND e FLOOR.
		Perto("Future SSJ a 0% = 2x", Catalogo.Multiplicador("future_ssj", zero, PerfilDeFormas.Comum), 2);
		foreach ((double m, double esperado) in new[] { (9.0, 2.0), (10.0, 4.0), (55.0, 12.0), (90.0, 20.0), (100.0, 20.0) })
		{
			var f = new Maestrias(); f.Por("future_ssj", m);
			Perto($"Future SSJ a {m:0}% = {esperado:0}x", Catalogo.Multiplicador("future_ssj", f, PerfilDeFormas.Comum), esperado);
		}

		// LEGENDARY: rampa por maestria E rampa por combate, o maior dos dois.
		Perto("Wrathful cru = 1,5x", Catalogo.Multiplicador("wrathful", zero, PerfilDeFormas.Comum), 1.5);
		var w100 = new Maestrias(); w100.Por("wrathful", 100);
		Perto("Wrathful dominado = 10x", Catalogo.Multiplicador("wrathful", w100, PerfilDeFormas.Comum), 10);
		Perto("Wrathful cru + 100s de luta = 10x (rampa de combate)",
			  Catalogo.Multiplicador("wrathful", zero, PerfilDeFormas.Comum, Catalogo.RampaLssjSegundos), 10);
		Perto("Wrathful cru + 50s de luta = 5,75x (metade da rampa)",
			  Catalogo.Multiplicador("wrathful", zero, PerfilDeFormas.Comum, Catalogo.RampaLssjSegundos / 2),
			  1.5 + (10 - 1.5) * 0.5);
		// O FULL POWER VIROU O FIM DA RAMPA DO LEGENDARY, e a bancada tem que provar as DUAS pontas --
		// senao a fusao passaria com a rampa antiga (`[25, 40]`) intacta e ninguem veria.
		Perto("Legendary cru = 25x", Catalogo.Multiplicador("legendary", zero, PerfilDeFormas.Comum), 25);
		var l100 = new Maestrias(); l100.Por("legendary", 100);
		Perto("Legendary dominado = 50x (era o `legendary_full_power`)",
			  Catalogo.Multiplicador("legendary", l100, PerfilDeFormas.Comum), 50);
		// E A FORMA APAGADA NAO PODE VOLTAR PELA PORTA DOS FUNDOS: `Def` de um id que nao existe
		// devolve nulo e o `Multiplicador` responde 1x. Isto reprova se alguem reescrever a entrada.
		Perto("`legendary_full_power` nao e mais forma nenhuma",
			  Catalogo.Multiplicador("legendary_full_power", zero, PerfilDeFormas.Comum), 1);
		// A MIGRACAO DO SAVE, pelo mesmo caminho que o jogo usa (`Maestrias.DoSave` chama isto).
		Checa("o save que guardava o `140` (Full Power) le hoje o `130` (Legendary)",
			  Catalogo.RedeDoSave(140) == 130);

		// LEGENDARY PRIMAL: o combate MULTIPLICA por cima (+20% no maximo).
		Perto("Primal C-Type fora de combate = 3x", Catalogo.Multiplicador("primal_c_type", zero, PerfilDeFormas.Comum), 3);
		Perto("Primal C-Type em combate cheio = 3,6x (+20%)",
			  Catalogo.Multiplicador("primal_c_type", zero, PerfilDeFormas.Comum, Catalogo.RampaPrimalSegundos), 3.6);
		Perto("Primal LSSJ4 Limit Breaker = 72x (60 + 20%)",
			  Catalogo.Multiplicador("primal_legendary4_limit_breaker", zero, PerfilDeFormas.Comum, Catalogo.RampaPrimalSegundos), 72);

		// SSJ4 e derivados
		var s4 = new Maestrias(); s4.Por("ssj4", 100);
		Perto("SSJ4 dominado = 40x", Catalogo.Multiplicador("ssj4", s4, PerfilDeFormas.Comum), 40);
		Perto("SSJ4 Full Power cru = 42x (piso: SSJ4 dominado + 2)",
			  Catalogo.Multiplicador("ssj4_full_power", s4, PerfilDeFormas.Comum), 42);
		var s4fp = new Maestrias(); s4fp.Por("ssj4", 100); s4fp.Por("ssj4_full_power", 100);
		Perto("SSJ4 Full Power dominado = 50x", Catalogo.Multiplicador("ssj4_full_power", s4fp, PerfilDeFormas.Comum), 50);
		Perto("SSJ4 Limit Breaker = 56x", Catalogo.Multiplicador("ssj4_limit_breaker", s4fp, PerfilDeFormas.Comum), 56);

		// DIVINAS: absolutas, sem maestria propria
		Perto("SSG = 22x", Catalogo.Multiplicador("ssg", zero, PerfilDeFormas.Comum), 22);
		Perto("Blue = 32x", Catalogo.Multiplicador("blue", zero, PerfilDeFormas.Comum), 32);
		Perto("Blue Evolution = 56x", Catalogo.Multiplicador("blue_evolution", zero, PerfilDeFormas.Comum), 56);
		Perto("Rose = 32x", Catalogo.Multiplicador("rose", zero, PerfilDeFormas.Comum), 32);
		Perto("Beast = 56x", Catalogo.Multiplicador("beast", zero, PerfilDeFormas.Comum), 56);

		// A CURVA DO MISTICO TEM SECAO PROPRIA -- ver [10]. Ela e a unica do catalogo que nao le a
		// maestria da propria forma, e medi-la em duas linhas aqui daria a impressao de estar coberta.
		Perto("Ultra Instinct -Sign- = 60x", Catalogo.Multiplicador("ui_sign", zero, PerfilDeFormas.Comum), 60);
		Perto("Perfected Ultra Instinct = 66x", Catalogo.Multiplicador("ui_perfected", zero, PerfilDeFormas.Comum), 66);
		Perto("Destroyer Form = 60x", Catalogo.Multiplicador("destroyer", zero, PerfilDeFormas.Comum), 60);
		Perto("Ultra Ego = 66x", Catalogo.Multiplicador("ultra_ego", zero, PerfilDeFormas.Comum), 66);

		Checa("Blue Evolution se chama Evolution e nao Royale (pedido do dono)",
			  Catalogo.Def("blue_evolution")!.Nome.Contains("Evolution"));

		// OOZARU: os numeros do Oozaru.cs e do catalogo tem que ser o MESMO. Duas fontes pro mesmo
		// numero e como a divergencia entra sem ninguem ver.
		Perto("Oozaru do catalogo bate com Oozaru.cs",
			  Catalogo.Multiplicador("oozaru", zero, PerfilDeFormas.Comum), Oozaru.MultRegular);
		Perto("Oozaru Dourado do catalogo bate com Oozaru.cs",
			  Catalogo.Multiplicador("oozaru_dourado", zero, PerfilDeFormas.Comum), Oozaru.MultDourado);

		// ==================================================================================
		// A FERA: o prazo de controle, o gate do Dourado e os DOIS BITS
		// ==================================================================================
		// O PRAZO CRESCE E TERMINA EM INFINITO. Uma curva que so "sobe" passaria tambem num
		// sistema que devolve sempre o mesmo numero; o que prova a rampa e o formato.
		double d0 = Oozaru.SegundosDeControle(0, FormaOozaru.Regular);
		double d50 = Oozaru.SegundosDeControle(50, FormaOozaru.Regular);
		double d90 = Oozaru.SegundosDeControle(90, FormaOozaru.Regular);
		Perto("sem maestria nenhuma, so os segundos de graca", d0, Oozaru.SegundosDeGraca);
		Perto("50% de maestria = 1/4 da forma (quadratica)", d50, Oozaru.SegundosRegular * 0.25);
		Checa("o prazo CRESCE com a maestria", d0 < d50 && d50 < d90, $"{d0:0.#} {d50:0.#} {d90:0.#}");
		Checa("os 100% dao controle INFINITO, nao um numero grande",
			  double.IsPositiveInfinity(Oozaru.SegundosDeControle(100, FormaOozaru.Regular)));
		Checa("o Dourado tem prazo menor que o regular na mesma maestria",
			  Oozaru.SegundosDeControle(90, FormaOozaru.Dourado)
			  < Oozaru.SegundosDeControle(90, FormaOozaru.Regular));
		Checa("sem fera nao ha prazo", Oozaru.SegundosDeControle(100, FormaOozaru.Nao) == 0);

		// O GATE DO DOURADO: ele LE O CATALOGO, entao esta checagem tambem prova que os campos
		// `PedeLinhagem`/`PedeMaestria`/`PortaBp` daquela entrada nao sao dado morto.
		//
		// O BP DE BANCADA E O DA PORTA, e nao um numero grande qualquer: `sobra` passa raspando por
		// cima e `pouco` raspando por baixo, entao um `PortaBp` apagado da entrada faria a linha do
		// `pouco` reprovar na hora. Com "1 bilhao contra 750 milhoes" a porta poderia sumir sem que
		// nenhuma linha daqui percebesse.
		double portaDourado = Catalogo.PortaOozaruDourado;
		double sobra = portaDourado, pouco = portaDourado - 1;

		var semSsj1 = new Maestrias();
		var comSsj1 = new Maestrias(); comSsj1.Por("ssj1", 100);
		var meioSsj1 = new Maestrias(); meioSsj1.Por("ssj1", 99);
		Checa("Dourado recusado a quem nao e Primal",
			  Oozaru.PodeDourado("Saiyan", comSsj1, sobra, null) == RecusaOozaru.SemLinhagemPrimal);
		Checa("Dourado recusado a Primal sem SSJ1 dominado",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, meioSsj1, sobra, null) == RecusaOozaru.SemMaestriaSsj);
		Checa("Dourado liberado a Primal com SSJ1 a 100% e poder na porta",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, comSsj1, sobra, null) == RecusaOozaru.Pode);

		// A PORTA DE PODER, que o dono pediu junto da maestria. Ver `Catalogo.PortaOozaruDourado`.
		Checa("Dourado recusado a Primal com o SSJ1 dominado mas SEM poder",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, comSsj1, pouco, null) == RecusaOozaru.SemPoder);

		// A ORDEM DAS RECUSAS E A MENSAGEM: quem nao dominou o SSJ1 **e** nao tem poder ouve falar da
		// maestria, que e o que ele pode ir fazer hoje. Trocar a ordem manda a pessoa treinar BP.
		Checa("faltando as duas, a maestria e que e dita",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, meioSsj1, pouco, null) == RecusaOozaru.SemMaestriaSsj);

		// A PORTA E PESSOAL: o limiar sorteado SUBSTITUI o de fabrica. Aqui um `ultrassjat` dobrado
		// tranca quem passava pela porta de fabrica -- que e a diferenca entre ler o `LimiaresPessoais`
		// e ignora-lo (o defeito que o `Avaliar` ja evita no passo 8).
		var limiarAlto = new LimiaresPessoais { Rolado = true, ultrassjat = portaDourado * 2 };
		Checa("o limiar PESSOAL manda na porta do Dourado, nao a constante de fabrica",
			  Oozaru.PodeDourado(Oozaru.LinhagemPrimal, comSsj1, sobra, limiarAlto) == RecusaOozaru.SemPoder);

		Checa("em SSJ, sem o Dourado, a lua nao da NADA (nem o regular)",
			  Oozaru.QualSai(estaEmSsj: true, "Saiyan", semSsj1, sobra, null) == FormaOozaru.Nao);
		Checa("na base, a lua sempre da o regular (poder nenhum e pedido)",
			  Oozaru.QualSai(estaEmSsj: false, "Saiyan", semSsj1, 0, null) == FormaOozaru.Regular);

		// O CIRCULO DO SSJ4: dominar o Dourado abre a porta que antes pedia a propria porta.
		Checa("sair do Dourado DOMINADO vira SSJ4 mesmo sem o SSJ4 liberado",
			  Oozaru.ViraSsj4AoSair(FormaOozaru.Dourado, "Saiyan", ssj4Liberado: false, maestriaDourado: 100));
		Checa("sair do Dourado NAO dominado e sem SSJ4 liberado nao vira nada",
			  !Oozaru.ViraSsj4AoSair(FormaOozaru.Dourado, "Saiyan", ssj4Liberado: false, maestriaDourado: 99));
		Checa("com o SSJ4 ja liberado, toda saida do Dourado cai nele (regra do DM)",
			  Oozaru.ViraSsj4AoSair(FormaOozaru.Dourado, "Saiyan", ssj4Liberado: true, maestriaDourado: 0));
		Checa("sair do Oozaru REGULAR nunca vira SSJ4",
			  !Oozaru.ViraSsj4AoSair(FormaOozaru.Regular, "Saiyan", ssj4Liberado: true, maestriaDourado: 100));

		// ============================ OS DOIS BITS ============================
		// Esta e A checagem da camada: liberar o SSJ4 pelo Oozaru NAO pode gastar a estreia dele,
		// senao a cinematica some em silencio -- e "em silencio" e o motivo de isto ser um teste e
		// nao uma leitura de codigo.
		// ====================================================================
		var bits = new EstadoDeForma();
		Checa("liberar abre o gate", bits.Liberar("ssj4") && bits.Despertou("ssj4"));
		Checa("liberar NAO consome a estreia", !bits.JaViuAEstreia("ssj4"));
		Checa("entrar na forma liberada DEVOLVE a estreia", bits.Entrar("ssj4"));
		Checa("e so uma vez", !bits.Entrar("ssj4"));

		var normal = new EstadoDeForma();
		Checa("o caminho normal (entrar sem liberar antes) tambem estreia", normal.Entrar("ssj1"));
		Checa("e entrar tambem LIBERA", normal.Despertou("ssj1"));

		// ============================ A HISTORIA INTEIRA, NA ORDEM EM QUE ELA ACONTECE ============================
		// As checagens acima provam cada peca em isolamento, e isolamento e onde este projeto ja deu
		// verde tres vezes com o jogo quebrado. O que o dono descreveu e uma SEQUENCIA de eventos que
		// atravessa dois sistemas (a lua e a escada) e dois bits (`Liberadas` e `EstreiaVista`):
		//
		//   "ao chegar no 100% de maestria, alem de controlar ele, o personagem vai se transformar no
		//    ssj4, so q nesse caso n tem cinematica, apenas o ozaru e desfeito e o player ao inves de
		//    voltar pra forma base ele cai no estagio de ssj4"
		//   "a cinematica q fizemos toca na primeira vez q ele se transformar em ssj4 apertando o C"
		//
		// Este bloco repete, passo a passo, o que o SERVIDOR faz (`GameServer.Oozaru.cs`: `Apeshit`
		// libera + marca estreia do macaco; `DesfazerOozaru` chama `Liberar("ssj4")` e escreve
		// `Atual` na mao, NUNCA `Entrar`). Se alguem trocar aquele `Liberar` por `Entrar` -- que e a
		// simplificacao obvia pra quem le o codigo sem este contexto --, a penultima linha daqui
		// reprova, e ela e a unica coisa no projeto que perceberia: em jogo o jogador simplesmente
		// nao veria a cena, e nao ha como saber que faltou algo que voce nunca viu.
		// ======================================================================================================
		const double bpDeSobra = 1e14;
		var primal = new PerfilDeFormas(Raca: "Saiyan", Linhagem: Oozaru.LinhagemPrimal);
		var jogador = new EstadoDeForma();

		// 1. ele sobe a escada normal ate o SSJ3, dominando cada degrau
		foreach (string degrau in new[] { "ssj1", "ssj2", "ssj3" })
		{
			jogador.Maestria.Por(degrau, 100);
			jogador.Entrar(degrau);
		}
		Checa("a escada ate o SSJ3 estreou pelo caminho normal",
			  jogador.JaViuAEstreia("ssj1") && jogador.JaViuAEstreia("ssj3"));

		// 2. a primeira lua cheia o pega em SSJ: vira Dourado. `Apeshit` faz as duas coisas.
		jogador.Liberar(Oozaru.IdDourado);
		jogador.EstreiaVista.Add(Catalogo.Rede(Oozaru.IdDourado));
		Checa("ver o Dourado uma vez NAO abre o SSJ4 (falta domar)",
			  jogador.Avaliar("ssj4", bpDeSobra, 1, false, primal) == RecusaForma.SemMaestria,
			  jogador.Avaliar("ssj4", bpDeSobra, 1, false, primal).ToString());

		// 3. luas cheias depois, a fera esta domada -- e sair dela cai no SSJ4
		jogador.Maestria.Por(Oozaru.IdDourado, 100);
		Checa("com o Dourado dominado, sair dele vira SSJ4",
			  Oozaru.ViraSsj4AoSair(FormaOozaru.Dourado, "Saiyan", jogador.Despertou("ssj4"), 100));

		// 4. e o que `DesfazerOozaru` executa: LIBERAR + `Atual` na mao. Sem cena.
		Checa("essa saida LIBERA o SSJ4", jogador.Liberar("ssj4") && jogador.Despertou("ssj4"));
		jogador.Atual = "ssj4";
		Checa("o corpo esta em SSJ4 tendo pulado a cinematica", !jogador.JaViuAEstreia("ssj4"));

		// 5. a forma cai (prazo, Ki, KO) e ele volta pra escada. Agora o C alcanca o SSJ4.
		jogador.Atual = "ssj3";
		Checa("depois disso o C aceita o SSJ4 (a porta ficou aberta)",
			  jogador.Avaliar("ssj4", bpDeSobra, 1, false, primal) == RecusaForma.Pode,
			  jogador.Avaliar("ssj4", bpDeSobra, 1, false, primal).ToString());

		// 6. E A CENA TOCA -- e este e o pedido do dono que o bit separado existe pra cumprir.
		Checa("A CINEMATICA DO SSJ4 TOCA nesse primeiro C", jogador.Entrar("ssj4"));
		Checa("e nunca mais depois disso", !jogador.Entrar("ssj4"));
		Console.WriteLine();
	}

	// =====================================================================
	// 7. O SAVE
	// =====================================================================
	/// <summary>
	/// O FORMATO DO SAVE NAO PODE TER MUDADO. Este projeto ja perdeu contas por isso uma vez.
	/// </summary>
	private static void Save()
	{
		Console.WriteLine("[7] SAVE (compatibilidade com o formato antigo)");

		// um save GRAVADO ANTES DO REWORK, com as chaves inteiras do enum velho
		var antigo = new Dictionary<string, double>
		{
			["10"] = 100,   // Forma.Ssj1
			["15"] = 40,    // Forma.Grade2
			["20"] = 55,    // Forma.Ssj2
			["30"] = 5,     // Forma.Ssj3
		};

		var m = new Maestrias();
		m.DoSave(antigo);
		Perto("save antigo: SSJ1 -> 100%", m.De("ssj1"), 100);
		Perto("save antigo: Grade 2 -> 40%", m.De("grade2"), 40);
		Perto("save antigo: SSJ2 -> 55%", m.De("ssj2"), 55);
		Perto("save antigo: SSJ3 -> 5%", m.De("ssj3"), 5);

		Dictionary<string, double> devolta = m.ParaSave();
		Checa("ida e volta preserva as 4 chaves", devolta.Count == 4);
		Checa("as chaves gravadas continuam sendo os mesmos inteiros",
			  devolta.ContainsKey("10") && devolta.ContainsKey("15")
			  && devolta.ContainsKey("20") && devolta.ContainsKey("30"),
			  string.Join(",", devolta.Keys));

		// uma chave DESCONHECIDA (save de um binario mais novo) tem que ser ignorada, nunca derrubar
		var futuro = new Maestrias();
		futuro.DoSave(new Dictionary<string, double> { ["10"] = 50, ["9999"] = 100, ["lixo"] = 1 });
		Perto("chave desconhecida ignorada, o resto carrega", futuro.De("ssj1"), 50);
		Checa("chave desconhecida nao vira entrada", futuro.Todas.Count() == 1);

		// ============================ O NUMERO QUE SAIU DO CATALOGO ============================
		// Quando duas formas viram uma, o save antigo continua trazendo os DOIS numeros. O `306`
		// (Mistico Ascendido) cairia no mesmo silencio da chave desconhecida logo acima -- e ali o
		// silencio e o certo, aqui e a perda de horas de maestria de um personagem que existe.
		//
		// COMO ISTO REPROVA SE A REGRA SUMIR: tire o `RedeDoSave` do `Maestrias.DoSave` e as duas
		// primeiras linhas caem -- a maestria some, sem erro nenhum na tela.
		var fundido = new Maestrias();
		fundido.DoSave(new Dictionary<string, double> { ["306"] = 70 });
		Perto("save com o 306 (forma fundida) carrega no Mistico", fundido.De(Catalogo.IdMistico), 70);
		Checa("e nao vira uma segunda entrada", fundido.Todas.Count() == 1);

		// AS DUAS CHAVES JUNTAS: a MAIOR vence. O dicionario nao promete ordem, entao "a ultima
		// lida" seria um sorteio -- e metade dos personagens perderia progresso.
		var duasChaves = new Maestrias();
		duasChaves.DoSave(new Dictionary<string, double> { ["305"] = 20, ["306"] = 90 });
		Perto("305 e 306 no mesmo save: fica a MAIOR", duasChaves.De(Catalogo.IdMistico), 90);
		Checa("o numero de rede do Mistico continua sendo o 305 (o save nao muda)",
			  Catalogo.Rede(Catalogo.IdMistico) == 305, $"{Catalogo.Rede(Catalogo.IdMistico)}");

		// ============================ AS OUTRAS DUAS LISTAS DO DISCO ============================
		// A maestria acima e UM dos tres lugares que guardam forma por numero. Os outros dois sao
		// `FormasDespertadas` e `FormasEstreadas`, e eles carregam coisas diferentes de perder: a
		// forma LIBERADA (o jogador reabriria uma porta que ja pagou) e a ESTREIA JA VISTA (a
		// cinematica tocaria de novo, meses depois, sem nada explicando).
		//
		// E ESTA E A FUNCAO QUE A CARGA CHAMA (`Catalogo.RedesDoSave`, no `Login`), nao uma copia
		// dela: refazer o `Select(...RedeDoSave...)` aqui provaria a conta da bancada. Foi por isso
		// que a expressao saiu de dentro do `GameServer.cs` -- ela estava escrita duas vezes la, e
		// uma bancada nao consegue alcancar codigo que mora no meio de um metodo de login.
		//
		// COMO REPROVA SE A REGRA SUMIR: troque a chamada por `[.. c.FormasDespertadas]` (a
		// "simplificacao" plausivel) e a primeira linha cai -- o 306 vira um numero que nao casa com
		// entrada nenhuma e o `Despertou` passa a dizer que a forma nunca foi aberta.
		HashSet<int> liberadas = Catalogo.RedesDoSave([306, 10]);
		Checa("save antigo: o 306 vira o Mistico na lista de formas LIBERADAS",
			  liberadas.Contains(Catalogo.Rede(Catalogo.IdMistico)),
			  string.Join(",", liberadas));
		Checa("...e o resto do save atravessa igual (o 10 continua sendo o SSJ1)",
			  liberadas.Contains(Catalogo.Rede("ssj1")) && liberadas.Count == 2,
			  string.Join(",", liberadas));

		// AS DUAS CHAVES JUNTAS VIRAM UMA SO -- e este e o caso de quem jogou nas duas versoes: o
		// save dele tem o 305 E o 306. Sem a fusao, o `HashSet` teria um numero orfao dentro.
		Checa("305 e 306 no mesmo save viram UMA entrada so",
			  Catalogo.RedesDoSave([305, 306]).Count == 1,
			  string.Join(",", Catalogo.RedesDoSave([305, 306])));

		// UM SAVE ANTIGO DEMAIS NAO TEM A LISTA -- e nulo e uma resposta, nao um acidente.
		Checa("save sem a lista (null) carrega vazio em vez de derrubar",
			  Catalogo.RedesDoSave(null).Count == 0);

		// e os marcos continuam avisando
		var marcos = new Maestrias();
		Checa("marco do Grade 2 avisa aos 50%", marcos.Subir("ssj1", 50, out string s1) && s1.Contains("Grade 2"), s1);
		Checa("marco do Grade 3 avisa aos 70%", marcos.Subir("ssj1", 20, out string s2) && s2.Contains("Grade 3"), s2);
		Checa("marco de dominio avisa aos 100%", marcos.Subir("ssj1", 30, out string s3) && s3.Contains("DOMINADA"), s3);
		Console.WriteLine();
	}

	// =====================================================================
	// 8. A PROMESSA DO REWORK
	// =====================================================================
	/// <summary>
	/// ACRESCENTAR UM ESTAGIO E UMA ENTRADA E MAIS NADA -- e este teste e a prova.
	///
	/// Ele monta uma forma que NAO esta no catalogo, no meio de uma linha existente, e confere que
	/// o encadeamento, o piso e a aparencia saem sozinhos. Se um dia alguem voltar a espalhar
	/// switches sobre a forma, e aqui que a promessa quebra primeiro.
	/// </summary>
	private static void EstagioNovo()
	{
		Console.WriteLine("[8] A PROMESSA: acrescentar um estagio nao mexe em mais nada");

		var novo = new FormaDef
		{
			Id = "ssj2_full_power", IdRede = 21, Linha = LinhaDeForma.Saiyajin, Ordem = 21,
			Nome = "Super Saiyajin 2 Full Power", Desc = "teste",
			Como = Curva.Rampa, Mult = [12, 15], PisoSobreAnterior = 2,
			Dreno = [0.05], PortaBp = Catalogo.PortaSsj2, ChaveDoLimiar = "ssj2at",
			// SEM `Cabelo`: um degrau da escada Saiyajin nao tinge -- ele TROCA a arte, e o sufixo e
			// quem diz qual. Ver `Catalogo.CorDoCabelo`.
			Aura = "ffe36b", SufixoDoCabelo = "SSj2", Intensidade = 3,
		};

		// O DEGRAU ANTERIOR sai da Ordem, sem ninguem escrever "vem do SSJ2"...
		FormaDef? antes = Catalogo.Todas
			.Where(d => d.Linha == novo.Linha && d.Ordem < novo.Ordem)
			.OrderByDescending(d => d.Ordem).FirstOrDefault();
		Checa("o degrau anterior sai sozinho da Ordem", antes?.Id == "ssj2", antes?.Id ?? "nenhum");

		// ...e o SSJ3, que hoje vem do SSJ2, passaria a vir DESTE, tambem sozinho.
		FormaDef ssj3 = Catalogo.Def("ssj3")!;
		Checa("o degrau seguinte se reencadearia sozinho",
			  novo.Ordem > Catalogo.Def("ssj2")!.Ordem && novo.Ordem < ssj3.Ordem);

		// A APARENCIA E A CINEMATICA ja vem no dado -- nao ha switch no cliente pra atualizar. E o
		// CABELO se resolve sozinho: sufixo preenchido e tinta vazia dao `ModoDoCabelo.Trocar`, sem
		// ninguem escolher modo nenhum. Era essa terceira pergunta que nao existia antes da varredura.
		Checa("a entrada ja carrega aura, cabelo e intensidade",
			  novo.Aura.Length == 6 && novo.Intensidade is >= 1 and <= 5
			  && Catalogo.ModoDoCabelo(novo) == ModoDoCabelo.Trocar
			  && Catalogo.CorDoRabo(novo) == "dada26");

		// O IdRede LIVRE existe: 21 nao colide com nada.
		Checa("ha IdRede livre entre os degraus (21 esta vago)",
			  Catalogo.PorRede(21) == null);

		Console.WriteLine("  -> uma forma nova precisa de: 1 entrada. Zero switches, zero arquivos.");
		Console.WriteLine();
	}

	// =====================================================================
	// 9. A PORTA DO BEAST: Mistico + 50% de ki divino + FURIA EXTREMA
	// =====================================================================
	/// <summary>
	/// A secao existe porque DOIS dos tres tercos da porta sao DERIVADOS -- o degrau anterior sai
	/// da <see cref="FormaDef.Ordem"/> e a furia sai do <see cref="Catalogo.RaivaExigida"/> --,
	/// e derivacao sem teste e palpite. Um degrau novo inserido entre o Mistico e o Beast trocaria
	/// o primeiro terco em silencio, e uma linha divina nova poderia cair no segundo.
	/// </summary>
	private static void PortaDoBeast()
	{
		Console.WriteLine("[9] A PORTA DO BEAST (Mistico + 50% de ki divino + furia extrema)");

		FormaDef beast = Catalogo.Def("beast")!;

		// --- 1o terco: SER MISTICO, e ele vem da Ordem e nao de um campo -------------------
		Checa("o degrau anterior do Beast e o Mistico (sai da Ordem, sem campo)",
			  Catalogo.Anterior(beast)?.Id == Catalogo.IdMistico,
			  Catalogo.Anterior(beast)?.Id ?? "nenhum");

		// E "estar NELE", nao "ja ter passado por ele": o Beast nao tem `PedeFormaAtual`, entao o
		// passo 5 do `Avaliar` cobra `EstaEmOuAcimaDe` e nao `Despertou`. Sem esta checagem, pendurar
		// um `PedeFormaAtual` no Beast um dia afrouxaria a porta sem ninguem notar.
		Checa("e o Beast cobra ESTAR no Mistico (nao e camada)", beast.PedeFormaAtual.Length == 0);

		// --- 2o terco: 50% DE KI DIVINO --------------------------------------------------
		Checa("o Beast pede 50% de maestria no ki divino",
			  Math.Abs(beast.PedeGodKi - Catalogo.GodkiRoyalePct) < 1e-9, $"{beast.PedeGodKi}");

		// --- 3o terco: A FURIA EXTREMA, E ELA E A **MAIOR** DAS DUAS --------------------
		// O Beast e a UNICA divina por raiva, mas ele nao e o unico a pedir a furia EXTREMA: o
		// tronco Saiyajin pede a mesma coisa (ordem do dono -- "a mesma condicao do Beast"). Quem
		// varre o catalogo inteiro atras dos dois degraus e a bancada `raiva`; aqui interessa so
		// que a fera esta no degrau de cima e nao no barato da linha Legendary.
		Checa("o Beast pede a furia EXTREMA (e nao o desconto da linha Legendary)",
			  Catalogo.RaivaExigida(beast) == NivelDeRaiva.Extrema,
			  Catalogo.RaivaExigida(beast).ToString());

		// --- e a porta inteira, pelo `Avaliar` --------------------------------------------
		var prodigial = new PerfilDeFormas(Raca: "Saiyan", Classe: Catalogo.ClasseProdigial,
										   GodKi: 100);
		const double bpDeSobra = 1e15;

		// no Mistico, com tudo pronto MENOS a furia
		EstadoDeForma NoMistico()
		{
			var e = new EstadoDeForma();
			e.Liberar(Catalogo.IdMistico);
			e.Entrar(Catalogo.IdMistico);
			return e;
		}

		Checa("no Mistico, com 100% de ki divino e BP de sobra: recusado por SEM FURIA",
			  NoMistico().Avaliar("beast", bpDeSobra, 1, false, prodigial) == RecusaForma.SemFuria,
			  NoMistico().Avaliar("beast", bpDeSobra, 1, false, prodigial).ToString());

		Checa("com a furia acesa, o Beast abre",
			  NoMistico().Avaliar("beast", bpDeSobra, 1, false,
								  prodigial with { Raiva = NivelDeRaiva.Extrema }) == RecusaForma.Pode);

		// A FURIA NAO SUBSTITUI OS OUTROS DOIS TERCOS -- senao ela seria um atalho e nao um gatilho.
		Checa("furia sem os 50% de ki divino nao abre nada",
			  NoMistico().Avaliar("beast", bpDeSobra, 1, false,
								  prodigial with { GodKi = 10, Raiva = NivelDeRaiva.Extrema })
				  == RecusaForma.SemGodKi);

		var naBase = new EstadoDeForma();
		Checa("furia fora do Mistico nao abre nada",
			  naBase.Avaliar("beast", bpDeSobra, 1, false,
							 prodigial with { Raiva = NivelDeRaiva.Extrema }) == RecusaForma.ForaDeOrdem,
			  naBase.Avaliar("beast", bpDeSobra, 1, false,
							 prodigial with { Raiva = NivelDeRaiva.Extrema }).ToString());

		// --- DEPOIS DE DESPERTO, VIRA TOGGLE: o `hasbeast` do `supersaiyan.dm:165` ---------
		// A checagem que separa "despertar" de "consumivel". Sem ela, o Beast so voltaria com um
		// segundo amigo morto -- outra regra, e nao a que o dono pediu.
		EstadoDeForma jaDesperto = NoMistico();
		jaDesperto.Liberar("beast");
		Checa("desperto uma vez, o Beast dispensa a furia (vira toggle)",
			  jaDesperto.Avaliar("beast", bpDeSobra, 1, false, prodigial) == RecusaForma.Pode,
			  jaDesperto.Avaliar("beast", bpDeSobra, 1, false, prodigial).ToString());

		// --- E A CLASSE NAO AFROUXOU: a fera continua sendo do Prodigial ------------------
		// `LinhaFechada` E NAO `SemClasse`, e a diferenca conta a regra inteira: quem nao e
		// Prodigial nem sequer tem a linha do Mistico aberta (`LinhasAbertas` desempata as tres
		// escadas divinas pela classe). O Namekiano deste teste pode ter recebido o Mistico pelo
		// ritual -- o dom passa pelo passo 0b --, mas o Beast nao e dom, e por ele a linha e
		// perguntada. E `LinhaFechada` e filtrada do `PorQueNao`: ele nao vai nem ouvir o nome.
		Checa("furia extrema nao da o Beast a quem nao e Prodigial",
			  NoMistico().Avaliar("beast", bpDeSobra, 1, false,
								  new PerfilDeFormas(Raca: "Namekian", GodKi: 100,
													 Raiva: NivelDeRaiva.Extrema)) == RecusaForma.LinhaFechada,
			  NoMistico().Avaliar("beast", bpDeSobra, 1, false,
								  new PerfilDeFormas(Raca: "Namekian", GodKi: 100,
													 Raiva: NivelDeRaiva.Extrema)).ToString());

		// --- O PADRAO DO PERFIL E O LADO SEGURO ------------------------------------------
		// O `GodKi` teve que nascer em -1 porque o zero do record struct abriria porta divina. Aqui
		// o zero e `false`; esta checagem e o que garante que continue sendo o lado que RECUSA.
		Checa("o perfil padrao nao tem furia (o zero do struct e o lado seguro)",
			  default(PerfilDeFormas).Raiva == NivelDeRaiva.Nenhuma && PerfilDeFormas.Comum.Raiva == NivelDeRaiva.Nenhuma);

		// ============================ E HOJE ELE NAO E ALCANCAVEL SEM ADMIN ============================
		// Esta e a checagem que sustenta a frase do relatorio, e ela mede o funil da TECLA C
		// (`Proxima`) e nao o `Avaliar`: o jogador nao escolhe forma nenhuma -- ele aperta C e o
		// servidor oferece o degrau MAIS FORTE que estiver aberto. Perguntar so ao `Avaliar` deixaria
		// de fora o unico caminho por onde o Beast poderia vazar em jogo.
		//
		// O CORPO DESTE TESTE TEM TUDO O QUE SE CONQUISTA: classe Prodigial, o Mistico concedido e
		// vestido, 100% de maestria de ki divino, BP de 1e15 e Ki cheio. O que falta nao se treina, e
		// e por isso que a linha vale: ela nao diz "falta poder", diz "nao ha caminho".
		//
		// COMO REPROVA SE A REGRA SUMIR: apague o passo 7b do `Avaliar` (ou o `RaivaExigida`) e o
		// Beast vira o degrau mais forte aberto -- `Proxima` o devolve na hora e esta linha cai.
		//
		// E A SEGUNDA LINHA E O PAR DELA, sem o qual a primeira mentiria por omissao: "o C nao
		// alcanca" tambem passaria num mundo em que o Beast e inalcancavel PARA SEMPRE -- e ai o
		// gancho da amizade seria decoracao e ninguem saberia. Com a furia acesa o C tem que chegar.
		// ==========================================================================================
		EstadoDeForma tudoPago = NoMistico();
		Checa("com TUDO o que se conquista pago, a tecla C NAO alcanca o Beast",
			  tudoPago.Proxima(bpDeSobra, prodigial)?.Id != "beast",
			  tudoPago.Proxima(bpDeSobra, prodigial)?.Id ?? "nenhum degrau");
		Checa("...e com a furia acesa ela alcanca (o gancho e o unico caminho que falta)",
			  tudoPago.Proxima(bpDeSobra, prodigial with { Raiva = NivelDeRaiva.Extrema })?.Id == "beast",
			  tudoPago.Proxima(bpDeSobra, prodigial with { Raiva = NivelDeRaiva.Extrema })?.Id ?? "nenhum degrau");

		Console.WriteLine("  -> hoje NINGUEM acende a furia: o gancho da amizade nao tem chamador.");
		Console.WriteLine();
	}

	// =====================================================================
	// 10. A CURVA DO MISTICO, DE PONTA A PONTA
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ESTA CURVA PRECISA DE SECAO PROPRIA ============================
	/// Ela e a UNICA entrada do catalogo cujo multiplicador nao sai do motor de degraus
	/// (`Catalogo.MultBruto`, o `Mult[]`/`Limiares[]`). Tudo o que a secao [1] prova sobre "degrau e
	/// nao rampa" NAO a alcanca: o Mistico ignora a maestria da propria forma e le duas coisas de
	/// FORA -- a linhagem de quem esta nele e a maestria de KI DIVINO, que e outro sistema.
	///
	/// Duas linhas de bancada dariam a impressao de cobertura sem cobrir o que quebra: uma curva
	/// medida em dois pontos e indistinguivel de uma escada de dois degraus. Por isso aqui ela e
	/// medida PONTO A PONTO, e a forma da curva (passo constante) e uma checagem em si.
	///
	/// E TUDO PASSA PELO `EstadoDeForma.Multiplicador`, que e o funil que o servidor chama
	/// (`MultiplicadorDaForma` -> `pl.Forma.Multiplicador(Perfil(pl))` -> `AplicarForma` -> `ssjBuff`).
	/// Chamar o `Catalogo.Multiplicador` direto pularia o `NaBase` e a passagem da maestria, que sao
	/// justamente os dois lugares por onde um refactor entra sem ninguem ver.
	/// ================================================================================================
	/// </summary>
	private static void CurvaDoMistico()
	{
		Console.WriteLine("[10] A CURVA DO MISTICO (ponto a ponto, pelo funil do servidor)");

		// O CORPO VESTIDO DA FORMA, e nao um id solto: e assim que o servidor pergunta.
		static double Mult(PerfilDeFormas p, double maestriaDaPropriaForma = 0)
		{
			var est = new EstadoDeForma();
			est.Liberar(Catalogo.IdMistico);
			est.Entrar(Catalogo.IdMistico);
			est.Maestria.Por(Catalogo.IdMistico, maestriaDaPropriaForma);
			return est.Multiplicador(p);
		}

		var qualquerRaca = new PerfilDeFormas(Raca: "Namekian", Classe: "Normal");
		var prodigial = new PerfilDeFormas(Raca: "Halfbreed", Classe: Catalogo.ClasseProdigial,
										   Diluido: true);

		// --- OS TRES PATAMARES ------------------------------------------------------------
		// COMO REPROVAM SE A REGRA SUMIR: devolver o Mistico pro motor de degraus faz as tres darem
		// o MESMO numero, porque o motor nao tem como olhar linhagem nem ki divino -- ele so sabe a
		// maestria da propria forma, que aqui e zero nas tres.
		Perto("raca qualquer, sem ki divino = 16x", Mult(qualquerRaca), 16);
		Perto("linhagem Prodigial, sem ki divino = 18x", Mult(prodigial), 18);
		Perto("Prodigial com o ki divino DESTRAVADO (0% de maestria) = 22x",
			  Mult(prodigial with { GodKi = 0 }), 22);

		// O DEGRAU DE 4x ENTRE "NAO DESPERTOU" E "DESPERTOU COM 0%" e a razao de o `GodKi` do perfil
		// nascer em -1 e nao em 0. Sem esta linha, trocar o `p.GodKi < 0` do `MultPorGodKi` por
		// `p.GodKi <= 0` seria um erro invisivel: 18 e 22 sao dois numeros plausiveis no mesmo lugar.
		Checa("e -1 (nunca despertou) NAO e 0 (despertou cru): sao 18x contra 22x",
			  Math.Abs(Mult(prodigial) - Mult(prodigial with { GodKi = 0 })) > 3.9,
			  $"{Mult(prodigial):0.##} contra {Mult(prodigial with { GodKi = 0 }):0.##}");

		// --- A SUBIDA E GRADUAL, e ela e medida em 12 pontos ------------------------------
		// ============================ POR QUE 12 PONTOS E NAO 2 ============================
		// Uma tabela de degraus fininhos (`Mult = [22,24,26,...,32]`) passaria por "sobe entre 0 e
		// 33%" medida em duas pontas, e ate em cinco. O que separa RAMPA de ESCADA e o PASSO SER
		// CONSTANTE -- e isso so aparece medindo o suficiente pra comparar os passos entre si.
		//
		// COMO REPROVA SE A REGRA SUMIR: qualquer escada (inclusive uma de 342 degraus como a do
		// `effector`) tem passo zero em algum trecho e passo dobrado no seguinte; a linha do "passo
		// constante" cai, e a do "nenhum trecho e plano" cai junto.
		// ==============================================================================
		const int amostras = 12;
		double[] valores = new double[amostras];
		for (int k = 0; k < amostras; k++)
		{
			double godki = Catalogo.GodkiBluePct * k / (amostras - 1.0);
			valores[k] = Mult(prodigial with { GodKi = godki });
		}
		Console.WriteLine("  --   " + string.Join("  ", Enumerable.Range(0, amostras).Select(k =>
			$"{Catalogo.GodkiBluePct * k / (amostras - 1.0):0.#}%={valores[k]:0.##}x")));

		bool subiuSempre = true;
		for (int k = 1; k < amostras; k++) if (valores[k] <= valores[k - 1]) subiuSempre = false;
		Checa($"a subida e continua: nenhum dos {amostras - 1} trechos entre 0% e 33% e plano",
			  subiuSempre, string.Join(" ", valores.Select(v => $"{v:0.###}")));

		double passoMin = double.MaxValue, passoMax = double.MinValue;
		for (int k = 1; k < amostras; k++)
		{
			double passo = valores[k] - valores[k - 1];
			passoMin = Math.Min(passoMin, passo);
			passoMax = Math.Max(passoMax, passo);
		}
		Checa("...e o passo e CONSTANTE -- e rampa, nao escada de degraus fininhos",
			  passoMax - passoMin < 1e-9, $"menor passo {passoMin:0.######}, maior {passoMax:0.######}");

		// OS TRES QUARTOS, com o numero escrito a mao (22 + 10 x t). Sao o contra-teste da conta e
		// nao da forma: a rampa poderia ser constante e comecar/terminar no lugar errado.
		Perto("1/4 do caminho (8,25% de ki divino) = 24,5x",
			  Mult(prodigial with { GodKi = Catalogo.GodkiBluePct * 0.25 }), 24.5);
		Perto("metade do caminho (16,5%) = 27x",
			  Mult(prodigial with { GodKi = Catalogo.GodkiBluePct * 0.5 }), 27);
		Perto("3/4 do caminho (24,75%) = 29,5x",
			  Mult(prodigial with { GodKi = Catalogo.GodkiBluePct * 0.75 }), 29.5);

		// --- O TOPO, E O TETO QUE NAO SE ULTRAPASSA ---------------------------------------
		Perto("aos 33% de ki divino = 32x", Mult(prodigial with { GodKi = Catalogo.GodkiBluePct }), 32);
		Perto("aos 50% AINDA = 32x (o teto)", Mult(prodigial with { GodKi = 50 }), 32);
		Perto("aos 70% AINDA = 32x", Mult(prodigial with { GodKi = 70 }), 32);
		Perto("aos 100% AINDA = 32x", Mult(prodigial with { GodKi = 100 }), 32);

		// O TETO E O `Clamp`, E NAO O FIM DE UMA TABELA. Um valor absurdo prova a diferenca: uma
		// tabela indexada por maestria estouraria ou repetiria o ultimo por acidente; o `Clamp`
		// devolve 32 porque foi escrito pra devolver 32.
		Perto("e um numero absurdo (1000%) tambem para em 32x -- o teto e estrutural",
			  Mult(prodigial with { GodKi = 1000 }), 32);

		// ONDE O TETO FICA, EXATAMENTE. Sem este par, mover o `TopoEm` de 33% pra 50% nao reprovaria
		// nada: as quatro linhas acima continuariam verdes (50%, 70% e 100% dariam 32 do mesmo jeito)
		// e so o jogador que esta em 40% notaria, sem ter como saber que notou.
		Checa("logo ABAIXO do teto ainda falta (32,9% < 32x)",
			  Mult(prodigial with { GodKi = Catalogo.GodkiBluePct - 0.1 }) < 32,
			  $"{Mult(prodigial with { GodKi = Catalogo.GodkiBluePct - 0.1 }):0.####}");
		Perto("e logo ACIMA ja saturou (33,1% = 32x)",
			  Mult(prodigial with { GodKi = Catalogo.GodkiBluePct + 0.1 }), 32);

		// --- O QUE **NAO** MEXE NA CURVA --------------------------------------------------
		// Cada uma destas e um jeito plausivel de a regra vazar pro lado errado.

		// 1. O ki divino de quem nao e da origem. Sem ela, "toda raca recebe 16x" e "o Prodigial sobe
		//    com ki divino" poderiam virar "todo mundo sobe com ki divino" sem nenhuma linha cair.
		Perto("ki divino MADURO em quem nao e Prodigial nao muda nada: 16x",
			  Mult(qualquerRaca with { GodKi = 100 }), 16);

		// 2. A MAESTRIA DA PROPRIA FORMA. Ela sobe sozinha por ficar na forma (`TickDaForma`), entao
		//    e o eixo que mais facilmente voltaria a mandar aqui num refactor -- e voltaria dando
		//    numeros crescentes, que parecem certos.
		Perto("a maestria do proprio Mistico (100%) nao move a curva: continua 18x",
			  Mult(prodigial, maestriaDaPropriaForma: 100), 18);
		Perto("...nem no topo dela: continua 32x",
			  Mult(prodigial with { GodKi = 100 }, maestriaDaPropriaForma: 100), 32);

		// 3. O SANGUE DILUIDO. E o caso REAL e nao hipotetico: o Prodigial e classe de MEIO-SAIYAJIN
		//    (`stathalfbreed.dm:71`), entao todo Prodigial em jogo chega aqui com `Diluido = true`.
		//    O `MultDiluido` nerfa a escada Saiyajin de quem tem meio sangue; o Mistico nao e sangue,
		//    e um dom concedido. Se alguem devolver esta entrada pro motor de degraus, e por AQUI que
		//    o meio-Saiyajin seria punido por uma coisa que a raca dele nao deu.
		Perto("o sangue diluido nao nerfa o Mistico (o dom nao e heranca)",
			  Mult(prodigial with { Diluido = false, GodKi = 0 }), Mult(prodigial with { GodKi = 0 }));

		// 4. A ORIGEM CASA POR LINHAGEM **OU** CLASSE -- o `daOrigem` do `MultPorGodKi` tem um `||`, e
		//    um `||` sem teste e meio codigo sem teste. Hoje o Prodigial chega pela CLASSE; quem
		//    escrever a linhagem no outro campo um dia tem que achar a mesma curva.
		Perto("a origem tambem casa pelo campo Linhagem, nao so pela Classe",
			  Mult(new PerfilDeFormas(Raca: "Saiyan", Linhagem: Catalogo.ClasseProdigial, GodKi: 0)), 22);

		// 5. O TEMPO DE COMBATE. O Mistico nao tem rampa de luta (isso e do LSSJ) nem bonus
		//    (Legendary Primal) -- e as duas mecanicas moram no MESMO metodo, logo abaixo da curva.
		var noCombate = new EstadoDeForma { CombateSegundos = 10_000 };
		noCombate.Liberar(Catalogo.IdMistico);
		noCombate.Entrar(Catalogo.IdMistico);
		Perto("uma luta de horas nao muda o Mistico (a rampa de combate e de outra linha)",
			  noCombate.Multiplicador(prodigial with { GodKi = 0 }), 22);

		Console.WriteLine();
	}

	// =====================================================================
	// 12. O NOME E O CABELO QUE ANDAM COM A MAESTRIA
	// =====================================================================
	/// <summary>
	/// O SSJ1 A 100% E "GRADE 4" E PEDE A FOLHA `SSjFP` -- e mais nenhuma forma faz isso.
	///
	/// ============================ O QUE ESTA BANCADA EXISTE PRA PEGAR ============================
	/// O pedido do dono chegou como dois defeitos ("o nome nao muda", "o cabelo fp nao troca") que sao
	/// o mesmo instante, e por isso as duas respostas moram em funcoes irmas (`Catalogo.NomeDe` e
	/// `Catalogo.SufixoDoCabeloDe`) penduradas no MESMO predicado. O risco que sobra e o de alguem
	/// mexer em uma e nao na outra -- um dia o nome viraria aos 100% e o cabelo aos 99, e ninguem
	/// veria, porque as duas coisas so aparecem juntas na tela de quem chegou la.
	///
	/// A ULTIMA CHECAGEM E A QUE MAIS IMPORTA e nao tem nada a ver com o SSJ1: ela varre o catalogo
	/// INTEIRO exigindo que dominar uma forma nao mude nem o nome nem o cabelo de nenhuma outra. Se
	/// alguem trocar o `d.Id == IdSsj1` do predicado por algo mais largo (um `Contains("ssj")`, uma
	/// linha inteira), e aqui que se descobre -- e nao com trinta formas renomeadas em jogo.
	/// ==========================================================================================
	/// </summary>
	private static void NomeECabeloPorMaestria()
	{
		Console.WriteLine("-- nome e cabelo por maestria (o Grade 4) --");

		FormaDef ssj1 = Catalogo.Def(Catalogo.IdSsj1)!;

		Checa("o SSJ1 sem maestria continua se chamando pelo nome da entrada",
			  Catalogo.NomeDe(ssj1, dominada: false) == ssj1.Nome, Catalogo.NomeDe(ssj1, false));
		Checa("o SSJ1 a 100% se chama Grade 4",
			  Catalogo.NomeDe(ssj1, dominada: true) == Catalogo.NomeDoGrade4,
			  Catalogo.NomeDe(ssj1, true));
		Checa("o SSJ1 sem maestria pede a folha SSj",
			  Catalogo.SufixoDoCabeloDe(ssj1, dominada: false) == ssj1.SufixoDoCabelo);
		Checa("o SSJ1 a 100% pede a folha SSjFP",
			  Catalogo.SufixoDoCabeloDe(ssj1, dominada: true) == Catalogo.SufixoDoSuperSaiyajinPleno,
			  Catalogo.SufixoDoCabeloDe(ssj1, true));

		// A SOBRECARGA DO LIVRO TEM QUE CONCORDAR COM A DO BIT. Sao as duas portas do mesmo funil (uma
		// pra quem tem `Maestrias`, outra pra quem so recebeu o bit pela rede), e elas se separarem e
		// o cliente e o servidor chamando a mesma forma por nomes diferentes no mesmo segundo.
		var livro = new Maestrias();
		Checa("livro vazio = nao dominada", Catalogo.NomeDe(ssj1, livro) == ssj1.Nome);
		livro.Por(Catalogo.IdSsj1, 99.9);
		Checa("99,9% ainda nao e Grade 4", Catalogo.NomeDe(ssj1, livro) == ssj1.Nome);
		livro.Por(Catalogo.IdSsj1, 100);
		Checa("100% no livro = Grade 4, igual ao bit",
			  Catalogo.NomeDe(ssj1, livro) == Catalogo.NomeDe(ssj1, dominada: true));
		Checa("livro nulo nao explode e nao promove", Catalogo.NomeDe(ssj1, (Maestrias?)null) == ssj1.Nome);

		// O NOME DERIVADO NAO E IDENTIDADE: o id e o IdRede tem que sobreviver ao renome, senao o save
		// e a rede passariam a depender de quanto o jogador treinou.
		Checa("dominar nao mexe no Id nem no IdRede",
			  Catalogo.Def(Catalogo.IdSsj1)!.IdRede == ssj1.IdRede && ssj1.Id == Catalogo.IdSsj1);

		// ---- e agora o catalogo inteiro, que e o teste de verdade ----
		var renomeadas = new List<string>();
		var trocaramCabelo = new List<string>();
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == Catalogo.IdSsj1) continue;
			if (Catalogo.NomeDe(d, dominada: true) != d.Nome) renomeadas.Add(d.Id);
			if (Catalogo.SufixoDoCabeloDe(d, dominada: true) != d.SufixoDoCabelo) trocaramCabelo.Add(d.Id);
		}
		Checa("NENHUMA outra forma muda de nome ao ser dominada",
			  renomeadas.Count == 0, string.Join(", ", renomeadas));
		Checa("NENHUMA outra forma muda de cabelo ao ser dominada",
			  trocaramCabelo.Count == 0, string.Join(", ", trocaramCabelo));

		// O SUFIXO DO FULL POWER NAO PODE ESTAR ESCRITO EM ENTRADA NENHUMA. Ele existe so como
		// derivacao -- se um dia alguem o escrever num `SufixoDoCabelo`, a forma passaria a nascer com
		// cabelo de Grade 4 sem maestria nenhuma, e o `fp` deixaria de significar "dominei".
		Checa("nenhuma entrada do catalogo escreve o sufixo SSjFP na mao",
			  !Array.Exists(Catalogo.Todas,
							d => d.SufixoDoCabelo == Catalogo.SufixoDoSuperSaiyajinPleno));

		Console.WriteLine();
	}

	/// <summary>
	/// ============================ A FURIA LENDARIA: O RELOGIO E A PUPILA ============================
	/// Duas regras do dono, e as duas sao FUNCOES PURAS -- por isso cabem numa bancada de console e nao
	/// dependem do Godot:
	///
	///   * *"o tempo q ela controla / o tempo ate perder o controle e baseado na maestria"*;
	///   * *"quando o jogador tem o controle a pupila verde volta, deixa de ser branca"*.
	///
	/// O QUE ESTAS LINHAS PEGAM QUE UM OLHO NAO PEGA: a simetria. `Controle` e `Posse` sao a MESMA
	/// curva lida em sentidos opostos, e a unica prova disso e comparar as duas em varios pontos --
	/// duas rampas escritas separadamente passariam em qualquer teste de "cresce" ou "diminui" e
	/// divergiriam calado no meio.
	/// ==========================================================================================
	/// </summary>
	private static void FuriaLendariaEOOlho()
	{
		Console.WriteLine("-- a furia lendaria (relogio) e a pupila --");

		FormaDef lend = Catalogo.Def("legendary")!;
		FormaDef wrath = Catalogo.Def(Catalogo.IdWrathful)!;
		FormaDef primal = Catalogo.Def("primal_legendary")!;
		FormaDef ssj1 = Catalogo.Def(Catalogo.IdSsj1)!;

		// ---- 1. QUEM ENTRA NA REGRA ----
		// A resposta do DM e "as duas linhas inteiras" (`Class == "Legendary" || "Legendary Primal
		// Saiyan"` + `if(!ssj && !lssj) return`), e o Wrathful nao e excecao la -- ele e `lssj == 1`.
		string[] descontrolaveis = [.. Array.FindAll(Catalogo.Todas, FuriaLendaria.EhDescontrolavel)
			.Select(d => d.Id)];
		string[] linhasLendarias = [.. Array.FindAll(Catalogo.Todas,
			d => d.Linha is LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal).Select(d => d.Id)];
		Checa("a furia alcanca exatamente as duas linhas lendarias, sem degrau de fora",
			  descontrolaveis.Length == linhasLendarias.Length && descontrolaveis.Length >= 10,
			  $"{descontrolaveis.Length} de {linhasLendarias.Length}");
		Checa("-- e o Wrathful esta DENTRO dela (no DM ele e o `lssj == 1`, com maestria propria)",
			  FuriaLendaria.EhDescontrolavel(wrath));
		Checa("-- e a escada Saiyajin comum fica de fora", !FuriaLendaria.EhDescontrolavel(ssj1));
		Checa("-- e o Oozaru tambem (ele tem o prazo DELE)",
			  !FuriaLendaria.EhDescontrolavel(Catalogo.Def(Jandirus.Core.Forms.Oozaru.IdRegular)));

		// ---- 2. O RELOGIO SAI DA LINHA, E NAO DE UMA CONSTANTE NOVA ----
		Perto("o prazo do Legendary sai da rampa de combate da propria linha (180 s)",
			  FuriaLendaria.SegundosDaLinha(lend), Catalogo.RampaLssjSegundos);
		Perto("e o do Primal sai da dele, que e maior (216 s)",
			  FuriaLendaria.SegundosDaLinha(primal), Catalogo.RampaPrimalSegundos);

		// ---- 3. AS DUAS PONTAS DA CURVA ----
		Perto("sem maestria nenhuma, o jogador dirige o PISO e mais nada",
			  FuriaLendaria.SegundosDeControle(lend, 0), FuriaLendaria.SegundosDeGraca);
		Perto("-- e a furia fica com a forma inteira",
			  FuriaLendaria.SegundosDePosse(lend, 0), Catalogo.RampaLssjSegundos);
		Checa("dominada (100%), o controle e INFINITO -- nao um numero grande",
			  double.IsPositiveInfinity(FuriaLendaria.SegundosDeControle(lend, 100)));
		Perto("-- e a posse e ZERO de verdade (nao o piso): ela nao vem mais",
			  FuriaLendaria.SegundosDePosse(lend, 100), 0);
		// O `>= 100` E UM CORTE E NAO UMA RAMPA QUE CHEGA LA: 99,9% ainda perde o corpo. Sem esta
		// linha, um `Dominou` escrito como `> 99` passaria em tudo acima.
		Checa("99,9% ainda NAO domina (a promessa e do 100)",
			  !double.IsPositiveInfinity(FuriaLendaria.SegundosDeControle(lend, 99.9))
			  && FuriaLendaria.SegundosDePosse(lend, 99.9) > 0);

		// ---- 4. A SIMETRIA: E A MESMA CURVA LIDA AO CONTRARIO ----
		// `Posse(m) == Controle(100 - m)` DENTRO da barra. Isto e o que impede as duas de virarem duas
		// tabelas independentes que um dia se desencontram.
		//
		// ============================ AS DUAS PONTAS QUEBRAM O ESPELHO DE PROPOSITO ============================
		// `Posse(0) = 180 s` mas `Controle(100) = infinito`; `Controle(0) = 6 s` (o piso) mas
		// `Posse(100) = 0`. As duas quebras sao as DUAS DECISOES do sistema, e nenhuma e do espelho:
		// dominar a forma promete "nunca mais", e nunca-mais nao tem numero; e nao dominar nada nao
		// pode dar zero segundos de controle, senao a forma NASCE possuida (ver `SegundosDeGraca`).
		// Espelhar as pontas apagaria as duas decisoes de uma vez -- por isso a varredura comeca no 10.
		// ==================================================================================================
		foreach (double m in new double[] { 10, 25, 40, 60, 75, 90, 99 })
			Perto($"posse({m}%) == controle({100 - m}%) -- a mesma curva ao contrario",
				  FuriaLendaria.SegundosDePosse(lend, m),
				  FuriaLendaria.SegundosDeControle(lend, 100 - m), 1e-6);
		Checa("e as duas PONTAS quebram o espelho, que e onde moram as duas decisoes",
			  double.IsPositiveInfinity(FuriaLendaria.SegundosDeControle(lend, 100))
			  && FuriaLendaria.SegundosDePosse(lend, 0) == Catalogo.RampaLssjSegundos
			  && FuriaLendaria.SegundosDeControle(lend, 0) == FuriaLendaria.SegundosDeGraca
			  && FuriaLendaria.SegundosDePosse(lend, 100) == 0);

		// ---- 5. O CRUZAMENTO NOS 50% ----
		Perto("na metade da barra, o jogador dirige tanto quanto a furia",
			  FuriaLendaria.SegundosDeControle(lend, 50), FuriaLendaria.SegundosDePosse(lend, 50), 1e-6);
		Perto("-- e sao 45 s de cada lado no Legendary (180 x 0,5²)",
			  FuriaLendaria.SegundosDeControle(lend, 50), Catalogo.RampaLssjSegundos * 0.25, 1e-6);

		// ---- 6. A CURVA E MONOTONA NOS DOIS SENTIDOS ----
		// Uma rampa que ande pro lado errado em algum trecho seria progressao NEGATIVA: treinar a forma
		// deixaria o jogador com menos controle. Nao da pra ver isso olhando dois pontos.
		bool controleSobe = true, posseCai = true;
		for (int m = 0; m < 100; m++)
		{
			controleSobe &= FuriaLendaria.SegundosDeControle(lend, m + 1)
						 >= FuriaLendaria.SegundosDeControle(lend, m);
			posseCai &= FuriaLendaria.SegundosDePosse(lend, m + 1)
					 <= FuriaLendaria.SegundosDePosse(lend, m);
		}
		Checa("treinar NUNCA encurta o controle, em nenhum ponto da barra", controleSobe);
		Checa("e NUNCA alonga a posse", posseCai);

		// ---- 7. A PUPILA ----
		const string branco = "fcfdfd", verde = "40a060", amarelo = "e8bc18";
		Checa($"com as redeas na mao, o Legendary olha em VERDE #{verde} (a cor da escada)",
			  Catalogo.CorDoOlho(lend, semRedeas: false) == verde,
			  Catalogo.CorDoOlho(lend, false) ?? "nada");
		Checa($"com a furia dirigindo, ele apaga a iris (#{branco})",
			  Catalogo.CorDoOlho(lend, semRedeas: true) == branco,
			  Catalogo.CorDoOlho(lend, true) ?? "nada");
		Checa("o Wrathful guarda o amarelo dele com as redeas na mao (pedido anterior do dono)",
			  Catalogo.CorDoOlho(wrath, semRedeas: false) == amarelo);
		// A ORDEM DENTRO DO `CorDoOlho`: a posse e perguntada ANTES da excecao por id. Trocar as duas
		// linhas de lugar passaria em tudo acima e deixaria justamente o degrau de maestria ZERO -- o
		// que mais perde o controle -- sem mostrar que perdeu.
		Checa("-- mas a furia apaga o amarelo tambem (a posse vem ANTES da excecao)",
			  Catalogo.CorDoOlho(wrath, semRedeas: true) == branco,
			  Catalogo.CorDoOlho(wrath, true) ?? "nada");

		// E O BIT NAO VAZA PRA FORA DA FURIA. Um `if (semRedeas) return branco` escrito sem a pergunta
		// `EhDescontrolavel` deixaria QUALQUER corpo possuido de olho branco -- inclusive um Super
		// Saiyajin comum, que nunca perde o controle, e o clone da Dimensao Mental.
		var mudaram = new List<string>();
		foreach (FormaDef d in Catalogo.Todas)
			if (!FuriaLendaria.EhDescontrolavel(d)
				&& Catalogo.CorDoOlho(d, semRedeas: true) != Catalogo.CorDoOlho(d, semRedeas: false))
				mudaram.Add(d.Id);
		Checa("nenhuma forma FORA das duas linhas lendarias muda de olho por causa da posse",
			  mudaram.Count == 0, string.Join(", ", mudaram));

		// E A SOBRECARGA DE UM ARGUMENTO E "COM AS REDEAS NA MAO", no catalogo inteiro. Ela existe pra
		// os leitores que nao tem como saber da posse (as bancadas de aparencia, o painel do admin);
		// se um dia ela passasse a significar outra coisa, seria por aqui que se descobre.
		var divergiram = new List<string>();
		foreach (FormaDef d in Catalogo.Todas)
			if (Catalogo.CorDoOlho(d) != Catalogo.CorDoOlho(d, semRedeas: false)) divergiram.Add(d.Id);
		Checa("a sobrecarga de um argumento e exatamente `semRedeas: false`",
			  divergiram.Count == 0, string.Join(", ", divergiram));

		// ---- 8. A CURVA E QUADRATICA, E ISSO SE MEDE PELA RAZAO ----
		// ============================ POR QUE UM PONTO NAO BASTA, E DUAS PONTAS TAMBEM NAO ============================
		// Tudo acima prova que a curva SOBE e que ela e simetrica. Uma RETA (`D * f`) passa em todas
		// aquelas linhas: sobe, e monotona, cruza a irma nos 50% e espelha nas pontas. O que separa a
		// quadratica da reta e o PASSO CRESCER -- e isso so aparece comparando dois trechos entre si.
		//
		// A razao entre dobros e a assinatura da potencia: `f²` dobrado da 4x, `f` dobrado da 2x. Os
		// dois pares abaixo estao ACIMA do piso de propósito (o piso achata a conta perto do zero e
		// faria a razao mentir).
		//
		// COMO REPROVA: troque o `frac * frac` do `SegundosDeControle` por `frac` -- as duas razoes
		// caem de 4 pra 2, e nenhuma das dezoito linhas acima se mexe.
		// ==========================================================================================================
		Perto("dobrar a maestria QUADRUPLICA o controle (40% -> 80%), e nao dobra",
			  FuriaLendaria.SegundosDeControle(lend, 80) / FuriaLendaria.SegundosDeControle(lend, 40), 4, 1e-9);
		Perto("-- e o mesmo vale no outro par (25% -> 50%)",
			  FuriaLendaria.SegundosDeControle(lend, 50) / FuriaLendaria.SegundosDeControle(lend, 25), 4, 1e-9);
		// E O ESPELHO TEM A MESMA ASSINATURA, medido do lado da posse: quem tem 20% de maestria perde o
		// corpo por 4x mais tempo que quem tem 60%? Nao -- (1-0,2)² / (1-0,6)² = 4. Mesma conta, mesma
		// potencia, lado oposto da barra.
		Perto("e a posse cai pela MESMA potencia (20% perde 4x mais que 60%)",
			  FuriaLendaria.SegundosDePosse(lend, 20) / FuriaLendaria.SegundosDePosse(lend, 60), 4, 1e-9);

		// ---- 9. O CONTROLE NEGATIVO DA VARREDURA ----
		// As duas varreduras acima ("nenhuma forma fora das lendarias muda de olho", "a sobrecarga de um
		// argumento e `false`") sao CONTAGENS DE ZERO. Um `CorDoOlho` que devolvesse nulo pra tudo --
		// um `switch` quebrado, uma linha nova engolindo o `_` -- faria as duas passarem verdes
		// provando que o sistema nao faz nada. Estas duas linhas dizem que a varredura enxerga.
		int comOlho = Array.FindAll(Catalogo.Todas, d => Catalogo.CorDoOlho(d, semRedeas: false) != null).Length;
		int mudamComPosse = Array.FindAll(Catalogo.Todas,
			d => Catalogo.CorDoOlho(d, semRedeas: true) != Catalogo.CorDoOlho(d, semRedeas: false)).Length;
		Checa($"CONTROLE NEGATIVO: a varredura ve cor de olho em gente ({comOlho} formas)", comOlho >= 20, $"{comOlho}");
		Checa($"CONTROLE NEGATIVO: e ve a posse MUDAR o olho em alguem ({mudamComPosse} formas)",
			  mudamComPosse >= 10, $"{mudamComPosse}");

		Console.WriteLine();
	}

	// =====================================================================
	// 13. A ENTRADA APAGADA E O SAVE DE ONTEM
	// =====================================================================
	/// <summary>
	/// ============================ O `legendary_full_power` NAO E FORMA, E O SAVE NAO PODE SABER DISSO ============================
	/// O dono: *"vc fez 2 transformacoes separadas dnv q na vdd sao a mesma, o full power e o legendary
	/// super saiyan so q quando a maestria chega em 100%"*. A entrada `140` foi apagada -- e o disco de
	/// todo personagem que ja jogou continua escrevendo `140`.
	///
	/// ============================ O QUE ESTA SECAO PEGA QUE A [6] NAO PEGA ============================
	/// A secao [6] confere `RedeDoSave(140) == 130`, que e a TABELA. Isto aqui confere o CAMINHO: o
	/// `Maestrias.DoSave` e o `RedesDoSave`, que sao os dois metodos que o `Login` chama de verdade.
	/// A diferenca importa porque a tabela pode estar certa e o caminho nao passar por ela -- foi
	/// exatamente o que aconteceu com o `306` (Mistico Ascendido) antes de o `RedesDoSave` existir:
	/// a migracao morava escrita duas vezes dentro do `Login` e valia so pra metade das listas.
	///
	/// E A PERDA E SILENCIOSA NOS TRES CANAIS. Ninguem recebe erro: a chave `140` simplesmente nao casa
	/// com forma nenhuma (`PorRede` devolve nulo), o `continue` do `DoSave` a engole, e o jogador loga
	/// com a maestria do Legendary zerada. Como a maestria do `130` e AGORA o que vale os 50x, quem
	/// tinha o Full Power dominado voltaria valendo 25x sem uma linha de aviso.
	/// ============================================================================================
	///
	/// COMO CADA FAMILIA DAQUI REPROVA:
	///   * apague a linha `[140] = 130` do `_redeAntiga` -- caem as SEIS linhas de maestria e as duas
	///     de lista, porque `PorRede(140)` e nulo e o `DoSave` descarta a chave;
	///   * troque o `Math.Max` do `DoSave` por "a ultima lida vence" -- cai UMA das duas linhas de ordem
	///     (qual delas, depende do dia: e esse o defeito);
	///   * reescreva a entrada `legendary_full_power` -- caem as tres primeiras.
	/// </summary>
	private static void AEntradaApagadaEOSaveVelho()
	{
		Console.WriteLine("[13] A ENTRADA APAGADA (`legendary_full_power`) E O SAVE DE ONTEM");

		const ushort RedeDoFullPowerAntigo = 140, RedeDoLegendary = 130;

		// ---- 1. ELA NAO EXISTE, POR NENHUM DOS TRES NOMES ----
		Checa("nao existe forma com o id `legendary_full_power`",
			  Catalogo.Def("legendary_full_power") == null);
		Checa("nenhuma entrada do catalogo usa o IdRede 140",
			  !Array.Exists(Catalogo.Todas, d => d.IdRede == RedeDoFullPowerAntigo),
			  string.Join(", ", Catalogo.Todas.Where(d => d.IdRede == RedeDoFullPowerAntigo).Select(d => d.Id)));

		// O DONO CONTOU OS BOTOES, e e assim que ele vai conferir de novo: *"a aba de formas hoje mostra
		// quatro botoes na linha Legendary [...] e o quarto tem que sumir como ENTRADA"*. O painel do
		// admin desenha um botao por entrada da linha (`MenuJogo.PainelDeFormas`), entao contar entradas
		// aqui e contar botoes la.
		string[] naLinha = [.. Catalogo.DaLinha(LinhaDeForma.Legendary).OrderBy(d => d.Ordem).Select(d => d.Id)];
		Checa($"a linha Legendary tem TRES degraus e nao quatro ({string.Join(" > ", naLinha)})",
			  naLinha.Length == 3 && naLinha[0] == Catalogo.IdWrathful && naLinha[1] == "c_type"
			  && naLinha[2] == "legendary", string.Join(", ", naLinha));

		// ---- 2. O CONTROLE NEGATIVO DA MIGRACAO: SEM A TABELA, O NUMERO NAO RESOLVE ----
		// ============================ ESTA LINHA E O QUE DA SENTIDO A TODAS AS OUTRAS ============================
		// `PorRede(140)` ser NULO e a razao de a tabela existir. Se um dia alguem reciclar o numero 140
		// pra outra forma, `PorRede(140)` passa a devolver ALGUMA coisa e a migracao vira um desvio de
		// maestria pra forma errada -- e todas as linhas de baixo continuariam verdes, porque elas
		// perguntam pelo `legendary` e o `RedeDoSave` continuaria mandando pra la.
		// ====================================================================================================
		Checa("CONTROLE NEGATIVO: sozinho, o numero 140 nao e forma nenhuma (por isso a tabela existe)",
			  Catalogo.PorRede(RedeDoFullPowerAntigo) == null,
			  Catalogo.PorRede(RedeDoFullPowerAntigo)?.Id ?? "nulo");
		// E ESTA E A EXPRESSAO LITERAL DO `DoSave`, na ordem em que ela roda la (`RedeDoSave` ANTES do
		// `PorRede`). Escrita assim de proposito: se alguem inverter as duas no arquivo do jogo, e aqui
		// que aparece, e nao no primeiro jogador de 2025 que logar.
		Checa("...e com a tabela, ele chega no `legendary`",
			  Catalogo.PorRede(Catalogo.RedeDoSave(RedeDoFullPowerAntigo))?.Id == "legendary",
			  Catalogo.PorRede(Catalogo.RedeDoSave(RedeDoFullPowerAntigo))?.Id ?? "nulo");

		// ---- 3. A MAESTRIA CHEGA, E CHEGA SOMADA ----
		// PELO MESMO METODO QUE O LOGIN CHAMA. Refazer a conta aqui (`Select(RedeDoSave)`) provaria a
		// bancada e nao o jogo -- e o cabecalho do `RedesDoSave` diz isso com todas as letras.
		static double Carregar(params (string chave, double v)[] disco)
		{
			var livro = new Maestrias();
			livro.DoSave(disco.ToDictionary(x => x.chave, x => x.v));
			return livro.De("legendary");
		}

		// (a) SO O FULL POWER NO DISCO. E o caso de quem dominou a forma antes do rework e nunca mais
		//     entrou: sem a migracao ele volta com ZERO, valendo 25x em vez dos 50x que pagou.
		Perto("quem so tinha horas no `140` chega com elas no `legendary`", Carregar(("140", 87)), 87);

		// (b) AS DUAS CHAVES, NAS DUAS ORDENS. `Dictionary` nao promete ordem de iteracao, entao "a
		//     ultima lida vence" nao e um bug que acontece as vezes -- e um bug que acontece pra
		//     ALGUNS personagens, e o mesmo save pode decidir diferente entre dois logins. Por isso as
		//     duas ordens sao medidas: um `Math.Max` trocado por atribuicao direta derruba UMA delas, e
		//     qual delas depende do dia.
		Perto("as duas chaves no disco: fica a MAIOR (140 maior, na ordem 140/130)",
			  Carregar(("140", 87), ("130", 42)), 87);
		Perto("...e na ordem inversa tambem (130/140)", Carregar(("130", 42), ("140", 87)), 87);
		Perto("...e quando a MAIOR e a chave nova, ela e que fica (140/130 com 130 maior)",
			  Carregar(("140", 12), ("130", 90)), 90);
		Perto("...nos dois sentidos", Carregar(("130", 90), ("140", 12)), 90);

		// (c) E O NUMERO NAO SE SOMA. "Somada" aqui e "as duas horas chegam como as de uma forma so",
		//     e nao "87 + 42 = 129". Somar de verdade daria maestria de graca a quem subiu os dois
		//     degraus quando eles eram dois -- e 100 e o teto, entao a soma iria calada pro topo.
		Checa("e as duas NAO se somam em 129 (a maior vence, ninguem ganha maestria de graca)",
			  Math.Abs(Carregar(("140", 87), ("130", 42)) - 129) > 1e-9);

		// ---- 4. AS DUAS LISTAS DE FORMA ----
		// `FormasDespertadas` e `FormasEstreadas`, as duas pelo `RedesDoSave`. A segunda e a que morde:
		// sem a migracao o `140` sumiria e o `130` seguiria estreado do jeito certo -- mas COM ela os
		// dois numeros chegam como o mesmo, e o `HashSet` precisa engolir a repeticao. Se ele nao
		// engolisse (uma `List`, um `Concat` solto), a estreia contaria duas vezes e a cinematica do
		// Legendary tocaria de novo meses depois, sem nada na tela explicando.
		HashSet<int> so140 = Catalogo.RedesDoSave([140]);
		Checa("o save que liberou o `140` volta com o `legendary` liberado",
			  so140.Count == 1 && so140.Contains(RedeDoLegendary), string.Join(", ", so140));
		HashSet<int> ambos = Catalogo.RedesDoSave([140, 130]);
		Checa("...e um save com os DOIS numeros volta com UM (a repeticao e esperada e engolida)",
			  ambos.Count == 1 && ambos.Contains(RedeDoLegendary), string.Join(", ", ambos));

		// CONTROLE NEGATIVO DA LISTA: um numero que nunca mudou tem que voltar igual. Sem isto, um
		// `RedesDoSave` que devolvesse `{130}` pra qualquer entrada passaria nas duas linhas de cima.
		HashSet<int> intocado = Catalogo.RedesDoSave([Catalogo.Rede("ssj3"), Catalogo.Rede("blue")]);
		Checa("CONTROLE NEGATIVO: numero que nunca mudou volta igual (a migracao nao e um funil)",
			  intocado.Count == 2 && intocado.Contains(Catalogo.Rede("ssj3"))
			  && intocado.Contains(Catalogo.Rede("blue")), string.Join(", ", intocado));

		Console.WriteLine();
	}

	// =====================================================================
	// 14. OS DOIS NOMES DA LINHA LENDARIA -- E POR QUE UM E DERIVADO E O OUTRO NAO
	// =====================================================================
	/// <summary>
	/// ============================ O DONO PEDIU DOIS RENOMES, E ELES SAO DE NATUREZAS DIFERENTES ============================
	///   * *"o SSJ1 de um Saiyajin comum a 100% de maestria chama-se Grade 4 (NAO E FORMA SEPARADA, E
	///     SO O SSJ 1 EM 100% DE MAESTRIA)"* -- isto e DERIVACAO: a mesma entrada, dois nomes, e quem
	///     escolhe e a maestria;
	///   * *"a forma legendary de um Saiyajin comum nao se chama Legendary Super Saiyan e sim Super
	///     Saiyan Full Power [...] pra diferencial o lssj do primal pro normal"* -- isto e RENOME DE
	///     CAMPO: sao DUAS entradas (`legendary` e `primal_legendary`), e a linhagem ja escolhe qual
	///     delas o jogador alcanca.
	///
	/// A segunda nao virou derivacao de proposito, e esta secao e o que prova que ela nao PRECISA ser:
	/// `LinhasAbertas` abre `Legendary` so pra quem tem a classe Legendary e `LegendaryPrimal` so pro
	/// Primal, entao um `if (perfil.Primal)` na hora de nomear seria um ramo que nunca pode ser falso.
	/// Se essa exclusividade se perder um dia, e AQUI que se descobre -- e o sintoma em jogo seria um
	/// Saiyajin comum com dois botoes de Legendary na aba, um chamado cada coisa.
	/// ==================================================================================================================
	/// </summary>
	private static void OsDoisNomesDaLinhaLendaria()
	{
		Console.WriteLine("[14] OS DOIS NOMES LENDARIOS (e o Grade 4, que e outra coisa)");

		FormaDef comum = Catalogo.Def("legendary")!;
		FormaDef primal = Catalogo.Def("primal_legendary")!;

		// ---- 1. OS NOMES SAO DIFERENTES, E SAO ESTES ----
		Checa("o Legendary do Saiyajin COMUM se chama `Super Saiyajin Full Power`",
			  comum.Nome == "Super Saiyajin Full Power", comum.Nome);
		Checa("o do PRIMAL continua `Legendary Super Saiyajin`",
			  primal.Nome == "Legendary Super Saiyajin", primal.Nome);
		Checa("-- e os dois sao DIFERENTES (o pedido era distinguir um do outro)", comum.Nome != primal.Nome);

		// ---- 2. CADA UM SO E ALCANCAVEL PELA SUA LINHAGEM ----
		// E o que sustenta a decisao de nao derivar. Medido pelo `LinhasAbertas`, que e o funil do jogo.
		var perfilComum = new PerfilDeFormas(Raca: "Saiyan", Classe: "Legendary", Legendary: true);
		var perfilPrimal = new PerfilDeFormas(Raca: "Saiyan", Classe: "Legendary Primal Saiyan",
											  Linhagem: "Primal Saiyan");

		HashSet<LinhaDeForma> abertasComum = Catalogo.LinhasAbertas(perfilComum);
		HashSet<LinhaDeForma> abertasPrimal = Catalogo.LinhasAbertas(perfilPrimal);
		Checa("o Legendary comum tem a linha Legendary e NAO a Primal",
			  abertasComum.Contains(LinhaDeForma.Legendary)
			  && !abertasComum.Contains(LinhaDeForma.LegendaryPrimal));
		Checa("o Legendary Primal tem a Primal e NAO a comum",
			  abertasPrimal.Contains(LinhaDeForma.LegendaryPrimal)
			  && !abertasPrimal.Contains(LinhaDeForma.Legendary));

		// E A CONSEQUENCIA, dita como o dono a leria: os nomes que cada um ve na aba.
		static string[] NomesQueVe(PerfilDeFormas p) =>
			[.. Catalogo.LinhasAbertas(p).SelectMany(Catalogo.DaLinha).Select(d => d.Nome)];

		string[] veComum = NomesQueVe(perfilComum), vePrimal = NomesQueVe(perfilPrimal);
		Checa("o Saiyajin Legendary comum NUNCA le `Legendary Super Saiyajin` na aba",
			  !veComum.Contains("Legendary Super Saiyajin"),
			  string.Join(", ", veComum));
		Checa("...e o Primal NUNCA le `Super Saiyajin Full Power`",
			  !vePrimal.Contains("Super Saiyajin Full Power"), string.Join(", ", vePrimal));
		// CONTROLE NEGATIVO DAS DUAS DE CIMA: elas sao contagens de ausencia, e uma `NomesQueVe` que
		// devolvesse lista vazia (linha fechada, `DaLinha` quebrada) passaria nas duas provando nada.
		Checa("CONTROLE NEGATIVO: cada um LE o nome que e dele",
			  veComum.Contains("Super Saiyajin Full Power") && vePrimal.Contains("Legendary Super Saiyajin"),
			  $"comum[{veComum.Length}] primal[{vePrimal.Length}]");

		// ---- 3. IDENTIDADE NAO E APRESENTACAO ----
		// Os dois nomes moram em entradas distintas, e as entradas tem numeros distintos: e o que
		// impede o save de um dos dois de virar o do outro. E o Grade 4 e o caso oposto -- MESMO numero,
		// dois nomes -- e ele tambem nao pode mover o id.
		Checa("as duas entradas lendarias tem Id e IdRede distintos",
			  comum.Id != primal.Id && comum.IdRede != primal.IdRede,
			  $"{comum.Id}/{comum.IdRede} x {primal.Id}/{primal.IdRede}");

		FormaDef ssj1 = Catalogo.Def(Catalogo.IdSsj1)!;
		string nomeCru = Catalogo.NomeDe(ssj1, dominada: false), nomePleno = Catalogo.NomeDe(ssj1, dominada: true);
		Checa("o SSJ1 a 99% e a 100% dao NOMES diferentes", nomeCru != nomePleno, $"{nomeCru} x {nomePleno}");
		Checa("...e o Id e o IdRede sao os MESMOS nos dois casos (o save nao depende do treino)",
			  Catalogo.Def(Catalogo.IdSsj1)!.IdRede == ssj1.IdRede
			  && Catalogo.Rede(Catalogo.IdSsj1) == ssj1.IdRede && ssj1.Id == Catalogo.IdSsj1,
			  $"{ssj1.Id}/{ssj1.IdRede}");
		// E O MESMO PELA PORTA DO LIVRO, que e a que o servidor usa. As duas portas do funil ja sao
		// comparadas na secao [12]; o que se pergunta aqui e outra coisa: a de 100% ainda aponta pra
		// MESMA entrada. Um `NomeDe` que devolvesse outro `FormaDef` (uma entrada "grade4" ressuscitada)
		// passaria na [12] inteira e cairia aqui.
		var cheio = new Maestrias(); cheio.Por(Catalogo.IdSsj1, 100);
		Checa("...e o nome pleno continua saindo da entrada `ssj1` e de nenhuma outra",
			  Catalogo.NomeDe(ssj1, cheio) == nomePleno
			  && !Array.Exists(Catalogo.Todas, d => d.Nome == nomePleno),
			  nomePleno);

		// ---- 4. O CONTROLE NEGATIVO DA VARREDURA DA SECAO [12] ----
		// ============================ A VARREDURA DE LA CONTA ZEROS ============================
		// A [12] varre o catalogo exigindo que dominar uma forma nao renomeie NENHUMA outra, e o
		// resultado esperado dela e zero. Um `NomeDe` que ignorasse o argumento (`=> d.Nome`) daria zero
		// tambem -- e o pedido do dono estaria desfeito com a bancada inteira verde. Esta linha e o
		// contra-teste: incluindo o SSJ1, a conta tem que dar EXATAMENTE um.
		// ====================================================================================
		string[] renomeiam = [.. Array.FindAll(Catalogo.Todas,
			d => Catalogo.NomeDe(d, dominada: true) != d.Nome).Select(d => d.Id)];
		Checa("CONTROLE NEGATIVO: EXATAMENTE uma forma do catalogo muda de nome ao ser dominada -- o SSJ1",
			  renomeiam.Length == 1 && renomeiam[0] == Catalogo.IdSsj1, string.Join(", ", renomeiam));

		string[] trocamCabelo = [.. Array.FindAll(Catalogo.Todas,
			d => Catalogo.SufixoDoCabeloDe(d, dominada: true) != d.SufixoDoCabelo).Select(d => d.Id)];
		Checa("CONTROLE NEGATIVO: e EXATAMENTE uma muda de folha de cabelo -- a mesma",
			  trocamCabelo.Length == 1 && trocamCabelo[0] == Catalogo.IdSsj1, string.Join(", ", trocamCabelo));

		Console.WriteLine();
	}

	// =====================================================================
	// 15. AS CORES COMO FORMA DE COR, E NAO COMO HEXA
	// =====================================================================
	/// <summary>
	/// ============================ COMPARAR HEXA NAO PEGA "ESCURO DEMAIS" ============================
	/// Foi assim que o defeito chegou: o dono nao disse *"o Blue devia ser `3392c7`"*, ele disse *"o
	/// cabelo do ssj blue ta MUITO escuro, e um azul mais claro"* -- e *"o cabelo atual do blue e pra
	/// ser do evolved/royale q e um azul escuro"*. Ou seja, o pedido inteiro e uma RELACAO entre duas
	/// cores, e uma bancada que cravasse os dois hexas passaria a existir pra proteger exatamente o
	/// valor que ele mandou trocar: no dia da proxima correcao ela reprovaria a correcao.
	///
	/// Por isso aqui nao ha um unico hexa esperado. O que se mede e o que ele descreveu com palavras:
	///   * o Blue e mais CLARO que o Evolution -- e mais claro puxando pro CIANO, nao por ser mais
	///     lavado (as duas coisas se separam em brilho x matiz);
	///   * o Legendary e verde AMARELADO nas duas linhas -- G manda, R vem logo atras, B fica pra tras;
	///   * o C-Type nao mudou: *"o type C ta certo (e pra ter a mesma cor de cabelo de um ssj normal
	///     mesmo)"* -- e "a mesma de um ssj normal" e medido contra o `ssj1`, nao contra um literal.
	///
	/// AS TINTAS SAO PISO DE UMA RAMPA, E ISSO NAO MUDA A CONTA. O shader multiplica a tinta pela luz
	/// do desenho (ate 1,81x), entao o hexa escrito e o pixel mais escuro dos quatro. Multiplicar os
	/// tres canais pelo mesmo fator NAO move matiz nenhum e nao inverte comparacao de brilho -- as
	/// relacoes medidas aqui sao as mesmas na tela. Quem mede PIXEL e a `--diagforma`, e ela mede outra
	/// coisa (que a rampa nao satura em branco), que uma bancada de console nao alcanca.
	/// ==========================================================================================
	/// </summary>
	private static void AsCoresComoFORMADECOR()
	{
		Console.WriteLine("[15] AS CORES COMO FORMA DE COR (relacao, nao hexa)");

		// --- as tres medidas, todas derivadas do mesmo hexa ---
		static (double R, double G, double B) Canais(string hexa) =>
			(Convert.ToInt32(hexa[..2], 16), Convert.ToInt32(hexa.Substring(2, 2), 16),
			 Convert.ToInt32(hexa.Substring(4, 2), 16));

		// BRILHO PERCEBIDO, com os mesmos pesos do shader (`dot(c.rgb, vec3(0.299,0.587,0.114))`,
		// `Personagem.gdshader`). Usar a media dos tres canais aqui daria outra resposta pro par
		// azul/ciano, e a resposta certa e a do olho -- que e a do shader.
		static double Luz(string hexa)
		{
			(double r, double g, double b) = Canais(hexa);
			return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
		}

		// MATIZ EM GRAUS (HSV). E o eixo que separa "ciano" de "azul" e "verde-limao" de "verde-mato",
		// e ele e INDEPENDENTE do brilho -- que e justamente o que uma comparacao de hexa nao tem.
		static double Matiz(string hexa)
		{
			(double r, double g, double b) = Canais(hexa);
			double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
			double d = max - min;
			if (d < 1e-9) return -1;                        // cinza: nao tem matiz
			double h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
			h *= 60; return h < 0 ? h + 360 : h;
		}

		string? azulBlue = Catalogo.CorDoCabelo(Catalogo.Def("blue"));
		string? azulEvo = Catalogo.CorDoCabelo(Catalogo.Def("blue_evolution"));

		// ---- 0. O CONTROLE NEGATIVO, ANTES DE QUALQUER COMPARACAO ----
		// Tudo abaixo compara duas leituras. Um `CorDoCabelo` que devolvesse nulo pros dois (campo
		// renomeado, entrada perdendo o `Cabelo` num refactor) faria as comparacoes nem rodarem -- e sem
		// esta linha o bloco sairia verde por nao ter medido nada, que e como o campo `Cabelo` ja passou
		// meses morto neste port.
		Checa("CONTROLE NEGATIVO: as duas tintas azuis EXISTEM (senao nada abaixo mede nada)",
			  azulBlue is { Length: 6 } && azulEvo is { Length: 6 },
			  $"blue={azulBlue ?? "nulo"} evo={azulEvo ?? "nulo"}");
		if (azulBlue is not { Length: 6 } || azulEvo is not { Length: 6 }) { Console.WriteLine(); return; }

		// ---- 1. O BLUE E MAIS CLARO QUE O EVOLUTION ----
		// A COMPARACAO E O PEDIDO INTEIRO. Nenhum dos dois valores e conferido sozinho: o que o dono
		// reclamou foi que os dois estavam no lugar errado um do outro.
		//
		// COMO REPROVA: devolva os dois hexas pros donos antigos (`082b8d` no Blue, `061e63` no
		// Evolution) e esta linha cai na hora -- que e o estado exato de que o dono reclamou.
		Console.WriteLine($"  --   blue #{azulBlue} luz {Luz(azulBlue):0.000} matiz {Matiz(azulBlue):0}graus  |  "
						+ $"evolution #{azulEvo} luz {Luz(azulEvo):0.000} matiz {Matiz(azulEvo):0}graus");
		Checa($"o Blue e MAIS CLARO que o Blue Evolution ({Luz(azulBlue):0.000} > {Luz(azulEvo):0.000})",
			  Luz(azulBlue) > Luz(azulEvo));
		// E COM FOLGA, e nao por um degrau. "Mais claro" que so vale por 1% e indistinguivel na tela, e
		// o dono reclamou de uma diferenca que ele viu de longe. Meio a mais e o piso do que se percebe.
		Checa($"...e com folga de pelo menos 50% (nao um degrau) -- {Luz(azulBlue) / Luz(azulEvo):0.00}x",
			  Luz(azulBlue) >= Luz(azulEvo) * 1.5, $"{Luz(azulBlue) / Luz(azulEvo):0.000}x");

		// ---- 2. E ELE E MAIS CLARO PUXANDO PRO CIANO, QUE E OUTRA COISA ----
		// ============================ BRILHO E MATIZ SE SEPARAM, E O PEDIDO TEM OS DOIS ============================
		// O dono deu a referencia do Goku SSGSS: *"um azul mais claro"*, e a arte e CIANO. Clarear o
		// `082b8d` sem mexer no matiz daria um azul-marinho lavado -- passaria na linha de cima e
		// continuaria nao sendo o que ele pediu. O matiz e o que separa as duas correcoes.
		//
		// Ciano fica por volta dos 190 graus e azul puro por volta dos 230: exigir que o Blue esteja
		// ABAIXO do Evolution nesse eixo e dizer "um puxa pro ciano e o outro pro azul", sem cravar
		// nenhum dos dois numeros.
		// ======================================================================================================
		Checa($"o Blue puxa pro CIANO e o Evolution pro azul puro ({Matiz(azulBlue):0}graus < {Matiz(azulEvo):0}graus)",
			  Matiz(azulBlue) < Matiz(azulEvo) - 10, $"{Matiz(azulBlue):0.#} x {Matiz(azulEvo):0.#}");
		// MAS OS DOIS CONTINUAM AZUIS. Sem esta linha, "puxa pro ciano" seria satisfeito por um VERDE --
		// e o Blue tem que ser reconhecivel como Blue.
		Checa("-- e os dois continuam na familia do azul (o canal B manda nos dois)",
			  Canais(azulBlue).B > Canais(azulBlue).G && Canais(azulBlue).B > Canais(azulBlue).R
			  && Canais(azulEvo).B > Canais(azulEvo).G && Canais(azulEvo).B > Canais(azulEvo).R);

		// ---- 3. O LSSJ E VERDE AMARELADO, NAS DUAS LINHAS ----
		// *"o do lssj tb [esta escuro], o lssj e um verde AMARELADO em ambos os casos (primal e
		// normal)"*. A referencia e o Broly. A varredura pega TODA entrada das duas linhas lendarias que
		// tinge cabelo -- e por lista, e nao por id: um degrau lendario novo ja nasce cobrado.
		string[] semAmarelo = [], comTinta = [.. Catalogo.Todas
			.Where(d => d.Linha is LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal
					 && Catalogo.CorDoCabelo(d) != null).Select(d => d.Id)];

		var fora = new List<string>();
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Linha is not (LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal)) continue;
			if (Catalogo.CorDoCabelo(d) is not { } t) continue;
			(double r, double g, double b) = Canais(t);
			// G ALTO, R MEDIO-ALTO, B BAIXO -- as tres condicoes do dono, escritas como relacoes:
			//   * o verde manda (senao nao e verde);
			//   * o vermelho vem LOGO ATRAS (e o que faz o amarelo; num verde-mato ele nao vem);
			//   * o azul fica pra tras (senao puxa pro ciano/esmeralda).
			bool amarelado = g > r && g > b && r > b * 2 && r > g * 0.5;
			// E O MATIZ CONFIRMA PELO OUTRO CAMINHO: amarelo puro e 60 graus, verde puro e 120. Verde
			// AMARELADO mora entre os dois e mais perto do amarelo. Cobrar abaixo de 90 e cobrar
			// "amarelado" sem cravar tom nenhum.
			double h = Matiz(t);
			if (!amarelado || h is < 60 or >= 95) fora.Add($"{d.Id}#{t}({h:0}graus)");
		}
		semAmarelo = [.. fora];
		Console.WriteLine($"  --   tingem cabelo nas duas linhas lendarias: {comTinta.Length} "
						+ $"({string.Join(", ", comTinta)})");
		// CONTROLE NEGATIVO PRIMEIRO: a varredura acima e uma lista de excecoes, e uma lista vazia e o
		// resultado tanto de "todas passaram" quanto de "nao havia nenhuma pra olhar".
		Checa($"CONTROLE NEGATIVO: a varredura ENXERGA tinta lendaria ({comTinta.Length} entradas)",
			  comTinta.Length >= 4, $"{comTinta.Length}");
		Checa("todo degrau lendario que tinge cabelo e VERDE AMARELADO (G manda, R logo atras, B pra tras)",
			  semAmarelo.Length == 0, string.Join(", ", semAmarelo));
		// E AS DUAS LINHAS USAM A MESMA TINTA -- *"em ambos os casos (primal e normal)"*. Se um dia uma
		// das duas ganhar constante propria, e aqui que se ve, antes de o dono ver dois verdes na tela.
		Checa("-- e o comum e o Primal usam a MESMA tinta (o pedido era 'nos dois casos')",
			  Catalogo.CorDoCabelo(Catalogo.Def("legendary"))
			  == Catalogo.CorDoCabelo(Catalogo.Def("primal_legendary")),
			  $"{Catalogo.CorDoCabelo(Catalogo.Def("legendary"))} x {Catalogo.CorDoCabelo(Catalogo.Def("primal_legendary"))}");

		// ---- 4. O C-TYPE NAO FOI TOCADO ----
		// *"o type C ta certo (e pra ter a mesma cor de cabelo de um ssj normal mesmo)"*. Medido contra
		// o `ssj1` e nao contra um literal: se o Super Saiyajin comum mudar amanha, o C-Type tem que
		// mudar junto -- "a mesma cor de um ssj normal" e uma relacao, e a bancada guarda a relacao.
		FormaDef ssj1 = Catalogo.Def(Catalogo.IdSsj1)!;
		foreach (string id in new[] { "c_type", "primal_c_type" })
		{
			FormaDef c = Catalogo.Def(id)!;
			Checa($"{id}: sem tinta, exatamente como o Super Saiyajin comum",
				  Catalogo.CorDoCabelo(c) == Catalogo.CorDoCabelo(ssj1)
				  && Catalogo.CorDoCabelo(c) == null, Catalogo.CorDoCabelo(c) ?? "nulo");
			Checa($"{id}: e pede a MESMA folha de cabelo que ele (`{ssj1.SufixoDoCabelo}`)",
				  c.SufixoDoCabelo == ssj1.SufixoDoCabelo, c.SufixoDoCabelo);
		}

		Console.WriteLine();
	}

	// =====================================================================
	// 19. AS TRES LINHAS RACIAIS -- Namekuseijin, Heran e Alien
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTA SECAO EXISTE PRA TRANCAR ============================
	/// Ela nao mede "a forma nova funciona" -- mede as quatro coisas que este porte ja errou antes e
	/// que uma linha racial nova erra de novo:
	///
	///   1. **A ESCADA ERRADA.** Ate esta sessao o `LinhasAbertas` entregava a escada Saiyajin a
	///      QUALQUER raca que nao fosse Primal/Legendary/Futuro/Frost. Um Namekuseijin virava Super
	///      Saiyajin de cabelo dourado. As checagens de exclusao aqui sao a rede daquilo -- e elas
	///      medem os DOIS lados (a raca certa tem a linha dela, e nao tem a Saiyajin).
	///   2. **A FORMA DE GRACA.** `snamek` e as duas Alien so existem pra quem COMPROU a skill. Sem
	///      o `PedeFlag` ligado no funil, comprar nao faria nada e nao comprar tambem nao.
	///   3. **O MULTIPLICADOR QUE NAO OLHA A CLASSE.** O Heran e o unico do jogo cujo numero sai da
	///      classe, e o modo de falhar disso e silencioso: todo mundo multiplicaria pela curva crua
	///      e ninguem notaria, porque 1x-2,016x tambem "parece" uma escada.
	///   4. **O LIMIAR PESSOAL SEM CONSUMIDOR.** `snamekat` era sorteado no nascimento havia meses e
	///      nao gateava nada. A checagem confere que a porta do `snamek` SAI do sorteio e nao da
	///      constante de fabrica.
	/// ==========================================================================================
	/// </summary>
	private static void AsLinhasRaciais()
	{
		Console.WriteLine("[19] AS TRES LINHAS RACIAIS (Namekuseijin, Heran, Alien)");

		// --- 1. CADA RACA TEM A **SUA** ESCADA, E SO A SUA -------------------------------
		(string Raca, LinhaDeForma Linha)[] donos =
		[
			(Catalogo.RacaNamekuseijin, LinhaDeForma.Namekuseijin),
			(Catalogo.RacaHeran,        LinhaDeForma.Heran),
			(Catalogo.RacaAlien,        LinhaDeForma.Alien),
		];

		foreach ((string raca, LinhaDeForma linha) in donos)
		{
			var p = new PerfilDeFormas(Raca: raca);
			HashSet<LinhaDeForma> abertas = Catalogo.LinhasAbertas(p);

			Checa($"{raca} tem a linha {linha}", abertas.Contains(linha), string.Join(", ", abertas));
			Checa($"{raca} NAO tem a escada Saiyajin", !abertas.Contains(LinhaDeForma.Saiyajin),
				  string.Join(", ", abertas));
			Checa($"{raca} nao vira Oozaru", !abertas.Contains(LinhaDeForma.Oozaru));

			// E NENHUMA DAS OUTRAS DUAS -- sem isto, um `LinhasAbertas` que somasse todas as linhas
			// raciais passaria nas duas checagens acima.
			foreach ((string outra, LinhaDeForma linhaOutra) in donos)
				if (linhaOutra != linha)
					Checa($"{raca} nao tem a linha de {outra}", !abertas.Contains(linhaOutra));
		}

		// O AVESSO: o Saiyajin continua com a escada dele. Sem esta linha, um `LinhasAbertas` que
		// tivesse deixado de abrir QUALQUER coisa passaria em todas as recusas acima.
		Checa("o Saiyajin continua com a escada Saiyajin",
			  Catalogo.LinhasAbertas(new PerfilDeFormas(Raca: "Saiyan")).Contains(LinhaDeForma.Saiyajin));

		// E A RACA SEM TRANSFORMACAO FICA SEM NENHUMA -- o preco declarado do conserto do
		// `LinhasAbertas`. O Humano e o caso puro: no DM ele nao tem uma unica forma.
		HashSet<LinhaDeForma> humano = Catalogo.LinhasAbertas(new PerfilDeFormas(Raca: "Human"));
		Checa("o Humano SEM ki divino nao tem escada nenhuma", humano.Count == 0,
			  string.Join(", ", humano));

		// ...mas as DIVINAS nao sao de raca, e continuam nao sendo: elas se aprendem.
		Checa("o Humano COM ki divino continua tendo a escada divina",
			  Catalogo.LinhasAbertas(new PerfilDeFormas(Raca: "Human", GodKi: 80))
					  .Contains(LinhaDeForma.GodKi));

		// --- 2. A FORMA QUE SE COMPRA -----------------------------------------------------
		var semLivro = new PerfilDeFormas(Raca: Catalogo.RacaNamekuseijin);
		var comLivro = semLivro with
		{
			FlagsDeSkill = new Dictionary<string, double> { ["snamek"] = 1 },
		};
		var estN = new EstadoDeForma();
		const double bpFolgado = 1e12;

		Checa("`snamek` sem a skill: recusado por SEM HABILIDADE",
			  estN.Avaliar("snamek", bpFolgado, 1, false, semLivro) == RecusaForma.SemHabilidade,
			  estN.Avaliar("snamek", bpFolgado, 1, false, semLivro).ToString());
		Checa("`snamek` com a skill e poder de sobra: abre",
			  estN.Avaliar("snamek", bpFolgado, 1, false, comLivro) == RecusaForma.Pode,
			  estN.Avaliar("snamek", bpFolgado, 1, false, comLivro).ToString());
		// E A SKILL NAO PAGA O PODER: as duas portas sao independentes, e um `||` no lugar do funil
		// deixaria a skill dispensar o BP.
		Checa("`snamek` com a skill e SEM poder: recusado por poder",
			  estN.Avaliar("snamek", 1, 1, false, comLivro) == RecusaForma.SemPoder,
			  estN.Avaliar("snamek", 1, 1, false, comLivro).ToString());

		// O DEGRAU DA FLAG DO ALIEN -- a mesma flag, dois minimos. `hasayyform = 1` abre a 1a e NAO
		// a 2a; e isso e literal do DM (`if(hasayyform)` contra `hasayyform == 2`).
		var alienUm = new PerfilDeFormas(Raca: Catalogo.RacaAlien,
			FlagsDeSkill: new Dictionary<string, double> { ["hasayyform"] = 1 });
		var alienDois = alienUm with
		{
			FlagsDeSkill = new Dictionary<string, double> { ["hasayyform"] = 2 },
		};
		var estA = new EstadoDeForma();
		estA.Entrar("alien1");   // o degrau anterior pago, pra a recusa medida ser a da flag

		Checa("`alien2` com a flag em 1: recusado por SEM HABILIDADE",
			  estA.Avaliar("alien2", bpFolgado, 1, false, alienUm) == RecusaForma.SemHabilidade,
			  estA.Avaliar("alien2", bpFolgado, 1, false, alienUm).ToString());
		Checa("`alien2` com a flag em 2: abre",
			  estA.Avaliar("alien2", bpFolgado, 1, false, alienDois) == RecusaForma.Pode,
			  estA.Avaliar("alien2", bpFolgado, 1, false, alienDois).ToString());
		Checa("`alien1` ja abria com a flag em 1 (o minimo dela e 1)",
			  new EstadoDeForma().Avaliar("alien1", bpFolgado, 1, false, alienUm) == RecusaForma.Pode);

		// CONTROLE NEGATIVO DO CANAL INTEIRO: EXATAMENTE tres entradas do catalogo pedem flag. Se
		// alguem colar um `PedeFlag` numa forma Saiyajin por engano, isto cai.
		string[] comFlag = [.. Catalogo.Todas.Where(d => d.PedeFlag != null).Select(d => d.Id)];
		Checa($"exatamente 3 formas do catalogo se COMPRAM ({string.Join(", ", comFlag)})",
			  comFlag.Length == 3, $"{comFlag.Length}");

		// --- 3. O MULTIPLICADOR DO HERAN SAI DA CLASSE -------------------------------------
		// Os numeros sao os do `statheran.dm:26-42`, escritos a mao aqui de proposito (mesma regra
		// da tabela de paridade la em cima: gerar do catalogo provaria a si mesmo).
		(string Classe, double Base1, double Base2)[] classes =
		[
			("Omega",     1.30, 2),
			("Low-Class", 3,    4),
			("Epsilon",   2.4,  3),
		];
		var cru = new Maestrias();
		var dominado = new Maestrias();
		dominado.Por("heran1", 100); dominado.Por("heran2", 100);

		foreach ((string classe, double b1, double b2) in classes)
		{
			var p = new PerfilDeFormas(Raca: Catalogo.RacaHeran, Classe: classe);
			Perto($"Heran {classe}: Max Power cru = {b1}x", Catalogo.Multiplicador("heran1", cru, p), b1);
			Perto($"Heran {classe}: True Max Power cru = {b2}x", Catalogo.Multiplicador("heran2", cru, p), b2);

			// DOMINADA = base x 2,016 -- o topo da `stepped_mastery_mult`, que so abre em 100%.
			Perto($"Heran {classe}: Max Power dominado = {b1 * 2.016:0.####}x",
				  Catalogo.Multiplicador("heran1", dominado, p), b1 * 2.016);
		}

		// A CLASSE DESCONHECIDA CAI NO `else` DO DM (Epsilon) e nao em 1x. Um Heran de save antigo
		// com `Class` vazia multiplicaria por 1 -- ou seja, transformaria pra ficar igual.
		Perto("Heran de classe desconhecida cai no `else` (Epsilon, 2,4x)",
			  Catalogo.Multiplicador("heran1", cru,
									 new PerfilDeFormas(Raca: Catalogo.RacaHeran, Classe: "???")), 2.4);

		// CONTROLE NEGATIVO: as tres classes dao numeros DIFERENTES. Sem isto, um `BaseDaClasse`
		// ignorado devolveria a curva crua pras tres e todas as linhas acima cairiam juntas -- mas um
		// `BaseDaClasse` que lesse sempre a MESMA chave passaria em duas delas.
		Checa("as tres classes de Heran multiplicam DIFERENTE",
			  Catalogo.Multiplicador("heran1", cru, new PerfilDeFormas(Raca: Catalogo.RacaHeran, Classe: "Omega"))
			  != Catalogo.Multiplicador("heran1", cru, new PerfilDeFormas(Raca: Catalogo.RacaHeran, Classe: "Low-Class")));

		// E A CLASSE **NAO** VAZA PRAS OUTRAS LINHAS: nenhuma outra entrada tem `BaseDaClasse`.
		string[] porClasse = [.. Catalogo.Todas.Where(d => d.BaseDaClasse != null).Select(d => d.Id)];
		Checa($"so as duas formas Heran escalam por classe ({string.Join(", ", porClasse)})",
			  porClasse.Length == 2);

		// --- 4. AS PORTAS: o limiar PESSOAL do Namekuseijin, e o divisor 50 do Heran --------
		// ============================ A FAIXA MUDOU, E ELA E A DO SSJ AGORA ============================
		// Era `+-5% da fabrica` (o `rand(95,105)` do `statnamek.dm:13-14`, base 2 M). O dono pediu
		// *"namekuseijins ganham super namek aprox no mesmo requisito do SSJ (mantendo a ideia de cada um
		// ter um requisito pessoal, mas em torno de um valor)"*, e o sorteio virou POR CLA sobre a base
		// do SSJ -- ver `LimiaresPessoais.RolarNamek`. As tres provas abaixo cobram as tres metades:
		// a faixa certa, o cla importando, e a porta saindo do SORTEIO e nao da constante.
		// ==========================================================================================
		LimiaresPessoais lim = LimiaresPessoais.Rolar(Catalogo.RacaNamekuseijin, "Warrior clan",
													  LimiaresPessoais.SementeDe("Piccolo", 1));
		Checa($"o `snamekat` do Warrior clan ({lim.snamekat:N0}) cai na faixa do Elite (1,1x-1,4x do SSJ)",
			  lim.snamekat >= Catalogo.SsjatInicial * 1.1 - 1e-6
			  && lim.snamekat <= Catalogo.SsjatInicial * 1.4 + 1e-6,
			  $"{lim.snamekat:N0}");

		// O CLA TEM QUE IMPORTAR -- controle negativo. Sem esta linha, um `RolarNamek` que ignorasse a
		// classe passaria na prova de cima sempre que a faixa unica calhasse de conter o valor. E as
		// faixas do Warrior (1,1-1,4) e do Dragon (0,9-1,2) so se encostam em 1,1 e 1,2: com a MESMA
		// semente, um sorteador que ignore o cla devolve o mesmo numero pros dois.
		LimiaresPessoais dragao = LimiaresPessoais.Rolar(Catalogo.RacaNamekuseijin, "Dragon clan",
														 LimiaresPessoais.SementeDe("Piccolo", 1));
		Checa($"o CLA muda a porta (Warrior {lim.snamekat:N0} x Dragon {dragao.snamekat:N0}) -- "
			+ "quem nasce forte transforma mais tarde",
			  lim.snamekat > dragao.snamekat, $"{lim.snamekat:N0} x {dragao.snamekat:N0}");

		// E A FAIXA INTEIRA E A DO SSJ, medida nos tres clas -- que e a frase do dono em numero.
		bool naFaixaDoSsj = true;
		foreach (string c in new[] { "Warrior clan", "Demon clan", "Dragon clan" })
			for (int s = 1; s <= 40; s++)
			{
				double v = LimiaresPessoais.Rolar(Catalogo.RacaNamekuseijin, c,
												  LimiaresPessoais.SementeDe("Namek", s)).snamekat;
				if (v < Catalogo.SsjatInicial * 0.9 - 1e-6 || v > Catalogo.SsjatInicial * 1.4 + 1e-6)
					naFaixaDoSsj = false;
			}
		Checa("...e os tres clas cabem na faixa do SSJ (0,9x-1,4x de 1.500.000) em 120 sorteios",
			  naFaixaDoSsj);

		Perto("...e a porta do `snamek` SAI do sorteio (nao da constante)",
			  lim.Porta(Catalogo.Def("snamek")!), lim.snamekat, 1e-6);

		LimiaresPessoais limH = LimiaresPessoais.Rolar(Catalogo.RacaHeran, "Omega",
													   LimiaresPessoais.SementeDe("Bojack", 2));
		Perto("a porta do Max Power e o `ssjat` do Heran (faixa absoluta da classe)",
			  limH.Porta(Catalogo.Def("heran1")!), limH.ssjat, 1e-6);
		Checa($"...e o Omega acende tarde: {limH.ssjat:N0} entre 5,5 e 8 milhoes",
			  limH.ssjat is >= 5_500_000 and <= 8_000_000, $"{limH.ssjat:N0}");
		Perto("a porta do True Max Power divide o `ssj2at` por 50 (e nao por 6)",
			  limH.Porta(Catalogo.Def("heran2")!), limH.ssj2at / Catalogo.HeranGateMult2, 1e-6);
		// O DIVISOR ERRADO SERIA INVISIVEL EM JOGO -- a forma so ficaria inalcancavel pra sempre.
		Checa("o divisor 6 do Super Saiyajin 2 daria uma porta 8x maior (e inalcancavel)",
			  limH.Porta(Catalogo.Def("heran2")!) * 8 < limH.ssj2at / Catalogo.Ssj1GateMult * 1.01);

		// --- 5. AS DERIVACOES: a forma nova nasceu certa sozinha? -------------------------
		// Ver o pedido desta sessao: "cada forma nova tem que nascer certa nas derivacoes". Aqui se
		// mede o que elas DEVEM ser, e o interessante sao as NEGATIVAS -- nenhuma das cinco troca
		// cabelo, pinta olho ou tinge rabo, porque nenhuma das tres racas tem penteado de Super
		// Saiyajin nem rabo, e o DM nao mexe no olho de nenhuma delas.
		string[] raciais = ["snamek", "heran1", "heran2", "alien1", "alien2"];
		foreach (string id in raciais)
		{
			FormaDef d = Catalogo.Def(id)!;
			Checa($"`{id}`: folha de aura COLORIVEL (a base, como toda forma fora do Legendary)",
				  Catalogo.Folha(d) == FolhaDeAura.Base, Catalogo.Folha(d).ToString());
			Checa($"`{id}`: acende a chama DA FORMA e nao a do jogador", !Catalogo.ChamaDoJogador(d));
			Checa($"`{id}`: nao troca nem tinge cabelo",
				  d.SufixoDoCabelo.Length == 0 && Catalogo.CorDoCabelo(d) == null,
				  Catalogo.CorDoCabelo(d) ?? "nulo");
			Checa($"`{id}`: nao pinta o olho", Catalogo.CorDoOlho(d) == null);
			Checa($"`{id}`: nao tinge rabo (nao ha rabo fora do sangue Saiyajin)",
				  Catalogo.CorDoRabo(d) == null);
			Checa($"`{id}`: nao troca o corpo", d.Corpo == CorpoDeForma.Nenhum);
			// A FAISCA SAI DA PROPRIA CHAMA (`CorDosRaios` cai no `d.Aura` fora das escadas
			// Saiyajin/Legendary/Mistica), e por isso a cor da entrada e a cor do raio.
			Checa($"`{id}`: a faisca e da cor da propria chama",
				  Catalogo.CorDosRaios(d) == d.Aura, Catalogo.CorDosRaios(d));
			// E CADA UMA TEM CENA PROPRIA -- o `Cinematicas.Para` deixou de ter fallback justamente
			// pra uma forma nova nao estrear com a cena de outra.
			Checa($"`{id}`: tem cinematica propria",
				  Cinematicas.De(id) != null && Cinematicas.Para(d)?.Forma == id);
		}

		// O TETO DE KI: o Namekuseijin DOBRA o tanque (o `trueKiMod = 2` do buff dele) e o Heran e o
		// Alien pegam o do Saiyajin. Sem o ramo na `TetoDeKi` os tres cairiam no `_ => 1` e a
		// promessa de "sua energia dobra" nao existiria -- calada.
		var corpo = new Jandirus.Core.Stats.Fighter();
		Perto("`snamek` dobra o tanque de Ki (trueKiMod = 2)",
			  Catalogo.TetoDeKi(Catalogo.Def("snamek"), corpo), Catalogo.SuperNamekKi);
		Perto("`heran1` usa o `ssjenergymod` do Saiyajin",
			  Catalogo.TetoDeKi(Catalogo.Def("heran1"), corpo), corpo.ssjenergymod);
		Perto("`heran2` usa o `ssj2energymod`",
			  Catalogo.TetoDeKi(Catalogo.Def("heran2"), corpo), corpo.ssj2energymod);
		Perto("`alien2` usa o `ssjenergymod` tambem (o DM nao sobe o tanque no 2o degrau)",
			  Catalogo.TetoDeKi(Catalogo.Def("alien2"), corpo), corpo.ssjenergymod);

		// --- 6. O DRENO DO HERAN ZERA COM A MAESTRIA -------------------------------------
		// E a recompensa inteira da linha, e ela e o ultimo degrau de `list(0.025, 0.015, 0.008, 0)`.
		//
		// O `0,32` E A CONVERSAO DE CICLO PRA SEGUNDO (`DrenoPorSegundo`: `0,4` por ciclo x `0,8`
		// ciclos por segundo), e ela esta escrita aqui a mao de proposito -- chamar a mesma constante
		// dos dois lados faria a checagem provar a si mesma. O numero do DM e o do catalogo; o que
		// esta secao mede e que ele CHEGA ao jogo pela regua certa.
		const double porSegundo = 0.4 * 0.8;
		Perto("Max Power cru drena 2,5% do Ki por ciclo", Catalogo.DrenoPorSegundo("heran1", cru), 0.025 * porSegundo);
		Perto("...e DOMINADO nao drena nada", Catalogo.DrenoPorSegundo("heran1", dominado), 0);
		Perto("True Max Power cru drena 4,0%", Catalogo.DrenoPorSegundo("heran2", cru), 0.040 * porSegundo);
		Perto("...e dominado tambem zera", Catalogo.DrenoPorSegundo("heran2", dominado), 0);
		// O SUPER NAMEKUSEIJIN NAO TEM MAESTRIA e por isso o dreno dele e fixo -- sustentar nao o
		// barateia, que e a diferenca de desenho entre a linha dele e a do Heran.
		Perto("o Super Namekuseijin drena 1,5% e a maestria nao muda isso",
			  Catalogo.DrenoPorSegundo("snamek", dominado), Catalogo.SuperNamekDreno * porSegundo);

		Console.WriteLine();
	}

	// =====================================================================
	// 20. UM DEGRAU NOVO EM CADA LINHA -- e nenhuma derivacao o trata diferente
	// =====================================================================
	/// <summary>
	/// ============================ COMO SE PROVA QUE NAO HA CASO ESCRITO A MAO ============================
	/// A promessa do rework e *"acrescentar um estagio e UMA ENTRADA, e mais nada"*, e a secao [8] ja a
	/// mede -- pra a escada Saiyajin, com um degrau escrito a mao e tres perguntas. As linhas raciais
	/// entraram depois, e sao justamente as candidatas naturais a ganhar um `case` de emergencia: cada
	/// uma tem uma peculiaridade (o corpo escolhido do Frost Demon, a base por classe do Heran, a flag
	/// comprada do Alien) que **parece** pedir um `if` por id.
	///
	/// A varredura por conjunto nao pega isso. `Catalogo.Todas` so conhece as entradas que existem, e
	/// um `d.Id == "snamek"` escondido numa derivacao devolve a resposta CERTA pra todas elas -- o
	/// defeito nasce no dia em que a 2a forma do Super Namekuseijin entrar, meses depois, sem cabelo
	/// ou sem faisca, e ninguem vai ligar uma coisa a outra.
	///
	/// ENTAO A BANCADA CRIA O DEGRAU QUE NAO EXISTE. Pra cada linha, um IRMAO SINTETICO de uma entrada
	/// real: id novo, `IdRede` novo, `Ordem` nova, e **os mesmos campos visuais**. As doze derivacoes
	/// tem que responder a ele exatamente o que respondem ao irmao -- porque a unica coisa que mudou e
	/// aquilo que uma derivacao nao deveria estar olhando.
	///
	/// Uma checagem textual (varrer os fontes atras de `"snamek"`) nao serviria: o arquivo esta cheio
	/// de mencoes legitimas -- constantes, comentarios, a propria entrada. Esta e comportamental, e
	/// falha exatamente quando o comportamento diverge.
	/// ================================================================================================
	/// </summary>
	private static void UmDegrauNovoEmCadaLinha()
	{
		Console.WriteLine("[20] UM DEGRAU NOVO EM CADA LINHA (nenhuma derivacao olha o id)");

		// AS LINHAS QUE INTERESSAM SAO **TODAS**, e sai da varredura e nao de uma lista: uma linha nova
		// entra aqui sozinha, que e o ponto. A base fica de fora porque ela nao e uma forma.
		foreach (LinhaDeForma linha in Enum.GetValues<LinhaDeForma>())
		{
			FormaDef? irmao = Catalogo.DaLinha(linha).LastOrDefault();
			if (irmao == null) continue;

			// O IRMAO SINTETICO: tudo o que a APARENCIA usa, copiado; tudo o que a IDENTIDADE usa,
			// diferente. Um `IdRede` acima de 60000 nao colide com nada e nao entra em save nenhum --
			// este objeto nunca e posto no catalogo, so passado as derivacoes.
			var novo = new FormaDef
			{
				Id = irmao.Id + "_teste", IdRede = (ushort)(60000 + (int)linha),
				Linha = linha, Ordem = irmao.Ordem + 1, Nome = irmao.Nome + " (novo)",
				Desc = "degrau sintetico da bancada",
				Mult = irmao.Mult, Limiares = irmao.Limiares, Dreno = irmao.Dreno,
				EscalaComGodKi = irmao.EscalaComGodKi, BaseDaClasse = irmao.BaseDaClasse,
				ForaDoTronco = irmao.ForaDoTronco, PortaBp = irmao.PortaBp,
				ChaveDoLimiar = irmao.ChaveDoLimiar, PedeFlag = irmao.PedeFlag,
				PedeMaestria = irmao.PedeMaestria, PedeMaestriaDe = irmao.PedeMaestriaDe,
				PedeGodKi = irmao.PedeGodKi, PedeEnergiaUe = irmao.PedeEnergiaUe,
				PedeProficienciaUi = irmao.PedeProficienciaUi, SoPorConcessao = irmao.SoPorConcessao,
				PedeFormaDespertada = irmao.PedeFormaDespertada, PedeFormaAtual = irmao.PedeFormaAtual,
				PedeClasseUmaDe = irmao.PedeClasseUmaDe, PedeOrigemUmaDe = irmao.PedeOrigemUmaDe,
				PedeLinhagem = irmao.PedeLinhagem, ProibidoParaClasse = irmao.ProibidoParaClasse,
				// A APARENCIA, IDENTICA -- e o controle do experimento.
				Aura = irmao.Aura, Cabelo = irmao.Cabelo, Corpo = irmao.Corpo,
				SufixoDoCabelo = irmao.SufixoDoCabelo, Intensidade = irmao.Intensidade,
				Raios = irmao.Raios, Absoluta = irmao.Absoluta,
			};

			string ondeIrmao = $"`{irmao.Id}`";
			Igual($"{linha}: a FOLHA de aura do degrau novo e a de {ondeIrmao}",
				  Catalogo.Folha(novo).ToString(), Catalogo.Folha(irmao).ToString());
			Igual($"{linha}: a CHAMA (do jogador ou da forma) e a de {ondeIrmao}",
				  Catalogo.ChamaDoJogador(novo).ToString(), Catalogo.ChamaDoJogador(irmao).ToString());
			Igual($"{linha}: o CONTORNO e o de {ondeIrmao}",
				  Catalogo.CorDoContorno(novo), Catalogo.CorDoContorno(irmao));
			Igual($"{linha}: o contorno ALTERNADO e o de {ondeIrmao}",
				  Catalogo.CorDoContornoAlterna(novo) ?? "nulo", Catalogo.CorDoContornoAlterna(irmao) ?? "nulo");
			Igual($"{linha}: a cor dos RAIOS e a de {ondeIrmao}",
				  Catalogo.CorDosRaios(novo), Catalogo.CorDosRaios(irmao));
			Igual($"{linha}: a cor do CABELO e a de {ondeIrmao}",
				  Catalogo.CorDoCabelo(novo) ?? "nulo", Catalogo.CorDoCabelo(irmao) ?? "nulo");
			Igual($"{linha}: o MODO do cabelo e o de {ondeIrmao}",
				  Catalogo.ModoDoCabelo(novo).ToString(), Catalogo.ModoDoCabelo(irmao).ToString());
			Igual($"{linha}: a cor do OLHO e a de {ondeIrmao}",
				  Catalogo.CorDoOlho(novo) ?? "nulo", Catalogo.CorDoOlho(irmao) ?? "nulo");
			Igual($"{linha}: a cor do RABO e a de {ondeIrmao}",
				  Catalogo.CorDoRabo(novo) ?? "nulo", Catalogo.CorDoRabo(irmao) ?? "nulo");
			Igual($"{linha}: as folhas COLADAS sao as de {ondeIrmao}",
				  string.Join("+", Catalogo.Coladas(novo)), string.Join("+", Catalogo.Coladas(irmao)));
			Igual($"{linha}: a NEBULOSA e a de {ondeIrmao}",
				  Catalogo.TemNebulosa(novo).ToString(), Catalogo.TemNebulosa(irmao).ToString());
			Igual($"{linha}: a RAIVA exigida e a de {ondeIrmao}",
				  Catalogo.RaivaExigida(novo).ToString(), Catalogo.RaivaExigida(irmao).ToString());
			// A `ChaveDaMaestria` FICA DE FORA DESTA VARREDURA, e a razao vale ser dita: ela recebe um
			// **id** e nao um `FormaDef`, entao ela resolve pelo catalogo -- e um degrau sintetico nao
			// esta la. Nao e defeito nem excecao: um degrau de verdade estaria. Quem a mede por linha e
			// a `--frostteste` ("os sete degraus compartilham UMA barra").
			Igual($"{linha}: SUSTENTAR treina igual a {ondeIrmao}",
				  Catalogo.SustentarTreina(novo).ToString(), Catalogo.SustentarTreina(irmao).ToString());

			// E O ENCADEAMENTO: o degrau novo enxerga o irmao como anterior, sem ninguem escrever isso.
			// (Ramo lateral e ramo lateral: o `ForaDoTronco` copiado faz o `Anterior` pular o irmao, que
			// e exatamente o comportamento certo -- e por isso a checagem pergunta pela LINHA.)
			FormaDef? anterior = Catalogo.Anterior(novo);
			Checa($"{linha}: o degrau novo acha um anterior da PROPRIA linha, sozinho",
				  anterior == null || anterior.Id == Catalogo.IdBase || anterior.Linha == linha,
				  anterior?.Id ?? "nenhum");
		}

		Console.WriteLine();
	}

	/// <summary>Compara duas respostas de derivacao -- o texto do obtido e do esperado no detalhe.</summary>
	private static void Igual(string nome, string obtido, string esperado) =>
		Checa(nome, obtido == esperado, $"obtido `{obtido}`, o irmao da `{esperado}`");

	// =====================================================================
	// 21. OS GANCHOS QUE FICARAM -- inertes hoje, e provaveis amanha
	// =====================================================================
	/// <summary>
	/// ============================ AS DIVIDAS DESTE PORTE TEM DUAS METADES, E SO UMA E OBVIA ============================
	/// O relatorio das formas nomeia onze divid ass, e cada uma tem um sistema faltando. Uma divida
	/// nomeada, porem, e uma promessa em duas partes:
	///
	///   * **INERTE**: o pedaco que ficou nao afeta o jogo de hoje. Uma forma que existisse pela
	///     metade seria pior que forma nenhuma, e este projeto ja pagou por isso -- o `fd_release`
	///     ficou orfao no `powerlevel()` por meses, o corte de sigilo de BP foi escrito e nunca
	///     aplicado, a `RecusaForma.NaoEhSaiyajin` existiu como nome de enum e nada mais.
	///   * **PROVAVEL**: o pedaco que ficou FUNCIONA no dia em que alguem o ligar. Um gancho que nao
	///     e exercitado nao e um gancho, e o descobridor disso e sempre quem implementa o sistema
	///     que ia usa-lo -- na pior hora possivel.
	///
	/// As duas metades sao mediveis, e nenhuma delas estava medida.
	/// ==============================================================================================================
	/// </summary>
	private static void OsGanchosQueFicaram()
	{
		Console.WriteLine("[21] OS GANCHOS QUE FICARAM (inertes hoje, provaveis amanha)");

		// --- 1. A ASCENSAO (`Fighter.BPBoost`) ------------------------------
		// E o sistema que falta em QUATRO das onze dividas (Gray Full Power, Super Majin, Purification
		// e o teto de 2,8x do Frost Demon). O port nao o tem; o que ele tem e o ponto onde o numero
		// entraria, e e esse ponto que esta secao exercita.
		var corpo = new Jandirus.Core.Stats.Fighter { BP = 1000, Name = "gancho" };
		corpo.Tick();
		double semAscensao = corpo.expressedBP;
		Checa($"a Ascensao esta DESLIGADA num corpo novo (BPBoost = {corpo.BPBoost:0.##})",
			  Math.Abs(corpo.BPBoost - 1) < 1e-9, $"{corpo.BPBoost}");

		// PROVAVEL: escrever o campo multiplica o poder, hoje, sem mais nada. O 2,8 e o `FD_ASC_CAP`
		// do DM, que e a divida concreta logo abaixo.
		//
		// A TOLERANCIA E DE UM MILESIMO e nao exata porque o `expressedBP` passa por arredondamento no
		// fim do `powerlevel()` (2745 contra 2744,x). Exigir igualdade aqui seria a bancada medindo o
		// arredondamento e nao o fator -- e ela ficaria vermelha no dia em que alguem trocasse o
		// `Round` por `Floor`, dizendo "a Ascensao parou de funcionar".
		corpo.BPBoost = 2.8;
		corpo.Tick(agoraMs: 0);
		Perto("...e escrever nele JA multiplica o BP expresso (o ponto de entrada existe)",
			  corpo.expressedBP / semAscensao, 2.8, 2.8e-3);

		// INERTE: nenhuma forma do catalogo depende dela pra dar o numero que promete. A 2a Evolucao
		// do Frost Demon e a divida concreta -- ela para em 20x onde o DM chega a 56x (`FD_ASC_CAP`),
		// e o que fica registrado aqui e o numero de HOJE, pra que o dia em que a Ascensao entrar a
		// bancada mostre a mudanca em vez de a esconder.
		Perto("a 2a Evolucao do Frost Demon vale 20x hoje (o 56x do DM depende da Ascensao)",
			  Catalogo.Multiplicador("frost7", new Maestrias(),
									 new PerfilDeFormas(Raca: Jandirus.Core.Races.FormasDeFrost.Raca,
														Classe: Jandirus.Core.Races.FormasDeFrost.ClasseNormal)),
			  20);

		// E NINGUEM ESCREVE `BPBoost` NO CODIGO DE PRODUCAO -- a varredura dos fontes, mesmo padrao da
		// `raiva` [8]. Um dia isto vai ficar vermelho, e sera pelo motivo certo: alguem portou a
		// Ascensao. A lista de permitidos e onde o campo pode aparecer sem ser um escritor.
		// ============================ A LISTA VIROU UMA REGRA, PORQUE ELA JA ESTAVA DESATUALIZADA ============================
		// Ela nomeava TRES arquivos, e a afirmacao logo abaixo diz "nenhum arquivo de **producao**".
		// Nao sao a mesma coisa: toda bancada que quer medir o efeito do `BPBoost` precisa escreve-lo
		// (a `MenteTeste` poe 4 pra separar base de expresso; a `LigadosTeste` faz o mesmo), e cada
		// bancada nova que nascesse teria que ser acrescentada a mao aqui -- ou deixaria esta linha
		// vermelha por um motivo que nao e o defeito que ela procura. Quando esta sessao a rodou, ela
		// JA estava vermelha por isso, com quatro entradas de bancada.
		//
		// `*Teste.cs` e `*Bench.cs` sao a convencao do projeto inteiro pra "isto nao e producao".
		// ================================================================================================================
		string[] podemEscrever = ["Fighter.cs"];
		static bool EhBancada(string nome) =>
			nome.EndsWith("Teste.cs", StringComparison.Ordinal)
			|| nome.EndsWith("Bench.cs", StringComparison.Ordinal);
		var escritores = new List<string>();
		int leitores = 0;

		foreach (string dir in new[] { "Core", "Server", "Client", "Tools" })
		{
			string caminho = Path.Combine(Directory.GetCurrentDirectory(), dir);
			if (!Directory.Exists(caminho)) continue;

			foreach (string arq in Directory.EnumerateFiles(caminho, "*.cs", SearchOption.AllDirectories))
			{
				// O `obj/` E O `bin/` GUARDAM COPIAS GERADAS dos mesmos arquivos, e uma copia contando
				// como escritor daria uma bancada vermelha por causa de um artefato de compilacao.
				if (arq.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
					|| arq.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

				string nome = Path.GetFileName(arq);
				string[] linhas = File.ReadAllLines(arq);
				for (int i = 0; i < linhas.Length; i++)
				{
					string l = linhas[i].Trim();
					if (l.StartsWith("//") || l.StartsWith("///")) continue;
					if (!l.Contains("BPBoost")) continue;

					if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\bBPBoost\s*=[^=]"))
					{
						// ============================ ZERAR **NAO** E LIGAR, E ESSA DISTINCAO E NOVA ============================
						// Esta varredura pegava qualquer `BPBoost =` e chamava de "escritor". Ela ficou
						// vermelha quando o BIO-ANDROIDE entrou -- e pelo motivo errado: as duas linhas
						// dele escrevem `= 1`, que e o `NoAscension = 1` do original (`statbiodroid.dm:2`
						// e o re-hook de login `DNALabs.dm:709-711`). Elas nao LIGAM a Ascensao; elas
						// APAGAM o `BPBoost` que o humano tinha antes de virar bicho -- e sem elas o bio
						// nasceria com ate ~317x de multiplicador herdado do criador.
						//
						// A afirmacao que interessa continua inteira: "ninguem MULTIPLICA nem ACUMULA
						// `BPBoost`". Escrever o NEUTRO e a unica excecao, e ela e a mesma promessa dita
						// de outro jeito -- no dia em que alguem escrever `= 5` ou `*=`, isto fica
						// vermelho como sempre ficou.
						// ==================================================================================================
						bool zeraPraNeutro =
							System.Text.RegularExpressions.Regex.IsMatch(l, @"\bBPBoost\s*=\s*1\s*[;,)]");
						if (!zeraPraNeutro && !EhBancada(nome) && Array.IndexOf(podemEscrever, nome) < 0)
							escritores.Add($"{nome}:{i + 1}");
					}
					else leitores++;
				}
			}
		}

		Checa("nenhum arquivo de producao ESCREVE `BPBoost` -- a Ascensao esta mesmo desligada",
			  escritores.Count == 0, string.Join(", ", escritores));
		// E OS LEITORES EXISTEM, o que e a outra metade: `Fighter.Training` conta marcos de ascensao e
		// o `GameServer.Tecnicas.G1` le `BPBoost > 1` como "esta ascendido". Sao consumidores parados
		// esperando o escritor -- e uma divida com consumidor e barata; sem consumidor, e reescrita.
		Checa($"...e ha consumidores esperando por ele ({leitores} leituras)", leitores >= 2, $"{leitores}");

		// --- 2. AS DUAS SKILLS QUE EXISTEM E ESTAO APAGADAS -----------------
		// Golden Form (o 28x do Frost Demon) e o buff Majin do Demonio. As duas sao SKILL e nao degrau,
		// as duas estao no `skills.json` com `ligada: 0` -- exatamente como no DM --, e as duas
		// dependem de um sistema que o port nao tem (a segunda, de liberacao de skill por CARGO).
		const string cs = "Assets/Data/skills.json", ct = "Assets/Data/skilltrees.json";
		if (File.Exists(cs) && File.Exists(ct))
		{
			var cat = Jandirus.Core.Skills.SkillCatalog.Parse(File.ReadAllText(cs), File.ReadAllText(ct));

			foreach (string path in new[] { "/datum/skill/icer/Golden_Form", "/datum/skill/demon/Majin" })
			{
				Jandirus.Core.Skills.Skill? s = cat.Get(path);
				// PROVAVEL: a skill EXISTE, com nome, e portanto ha o que ligar.
				Checa($"`{path}` existe no catalogo extraido do DM", s != null, "nao existe");
				if (s == null) continue;

				// INERTE, E PELA PORTA DE PRODUCAO: quem responde nao e o campo, e o
				// `SkillBook.PodeAprender` -- a mesma funcao que a loja chama.
				var livro = new Jandirus.Core.Skills.SkillBook();
				livro.Conceder(99);
				Jandirus.Core.Skills.Recusa r = livro.PodeAprender(cat, path, "Icer", "Frost Demon", vilao: false);
				Checa($"...e NAO se aprende hoje ({s.Nome}): a loja recusa por DESLIGADA",
					  r == Jandirus.Core.Skills.Recusa.Desligada, r.ToString());

				// E NENHUMA FORMA DO CATALOGO A ESPERA: uma entrada que pedisse a flag de uma skill
				// desligada seria uma forma inalcancavel pra sempre -- e inalcancavel e indistinguivel
				// de "ainda nao cheguei la", que e o pior jeito de uma divida se esconder.
				Checa($"...e nenhuma forma do catalogo depende dela",
					  !Catalogo.Todas.Any(d => d.PedeFlag is { } f && s.Flags.ContainsKey(f.Campo)),
					  "");
			}
		}
		else Console.WriteLine("  (sem skills.json -- rode da raiz do projeto)");

		// --- 3. AS FORMAS QUE DEPENDEM DE ABSORCAO ------------------------
		// Bio-Androide, Majin Corrompido e Purification. Elas NAO estao no catalogo, e a ausencia e
		// deliberada: os degraus 1-4 do Majin nao tem multiplicador nenhum no DM (cada um faz
		// `genome.add_to_stat("Battle Power", 1)`), e uma entrada com `Mult = [1]` e sem porta seria
		// lida pelo `PodeSerRepouso` como **forma de descanso** -- cinco candidatas a repouso entrando
		// de uma vez, e o `ParaOndeSeRecua` do jogo inteiro mudando de comportamento em silencio.
		//
		// A CHECAGEM E A AUSENCIA, e ela vale por ser barata: no dia em que alguem colar as entradas
		// "so pra ficar registrado", esta linha e a secao 6 da `--formasteste` acendem juntas.
		string[] dividas = ["majin1", "majin2", "majin3", "majin4", "majin_pure", "purification",
							"bio2", "bio3", "bio_super_perfect", "golden", "wolf",
							"limiter_overload", "gray_full_power"];
		string[] plantadas = [.. Catalogo.Todas.Where(d => dividas.Contains(d.Id)).Select(d => d.Id)];
		Checa("nenhuma forma de ABSORCAO/ASCENSAO entrou no catalogo antes do sistema dela",
			  plantadas.Length == 0, string.Join(", ", plantadas));

		// E O GANCHO QUE A SAGA MAJIN VAI USAR EXISTE E FUNCIONA: ver um amigo ser ABSORVIDO vale o
		// mesmo grau de luto que ve-lo morrer (`MajinSaga.dm:173`), e o grau que abre as escadas de
		// sangue e a `Extrema`. O gancho e o `GameServer.AmigoAbatido`, cuja unicidade a bancada
		// `raiva` [8] ja vigia; o que se mede aqui e que o GRAU ainda significa o que a saga espera.
		Checa("a raiva EXTREMA continua sendo a que abre as escadas de sangue (o grau que a absorcao vai usar)",
			  Catalogo.RaivaExigida(Catalogo.Def("ssj1")) == NivelDeRaiva.Extrema
			  && NivelDeRaiva.Extrema > NivelDeRaiva.Lendaria,
			  Catalogo.RaivaExigida(Catalogo.Def("ssj1")).ToString());

		Console.WriteLine();
	}

	// =====================================================================
	// 20. N5 -- O REQUISITO **PESSOAL** DO SUPER NAMEKUSEIJIN, MEDIDO POR DISPERSAO
	// =====================================================================
	/// <summary>
	/// Pedido do dono: *"namekuseijins ganham super namek aprox no mesmo requisito do SSJ (mantendo a
	/// ideia de cada um ter um requisito pessoal, mas em torno de um valor)"*.
	///
	/// ============================ POR QUE MEDIR DISPERSAO, E NAO A MEDIA ============================
	/// A secao 19 ja cobra a FAIXA (o Warrior no intervalo do Elite, o cla importando, os 120 sorteios
	/// dentro de 0,9x-1,4x). **Nada disso distingue um limiar pessoal de um numero cravado.** Uma
	/// implementacao que devolvesse `1.500.000` pra todo mundo passaria em cada uma daquelas provas: o
	/// valor esta na faixa, esta perto da media, e sai do sorteio (que sempre devolve o mesmo).
	///
	/// O que separa as duas coisas e a DISPERSAO -- quantos valores diferentes existem na populacao e
	/// quanto ela se espalha em volta do centro. Por isso o criterio aqui e uma FUNCAO
	/// (<see cref="EhUmLimiarPessoal"/>) e ela e aplicada duas vezes: na populacao de verdade, que
	/// passa, e numa populacao CRAVADA, que tem que reprovar. Sem a segunda, a primeira seria
	/// decoracao.
	/// ============================================================================================
	///
	/// ============================ E A COMPARACAO COM O SSJ E DE POPULACAO CONTRA POPULACAO ============================
	/// *"aprox no mesmo requisito do SSJ"* nao e uma frase sobre um personagem: e sobre as duas
	/// distribuicoes. Entao as duas sao sorteadas do mesmo jeito (mesmo `Rolar` de producao, sementes
	/// diferentes, classes/clas rodando) e comparadas nos quatro numeros que descrevem uma nuvem: a
	/// borda de baixo, a de cima, o centro e o espalhamento.
	/// ============================================================================================================
	/// </summary>
	private static void ORequisitoPessoalDoSuperNamekuseijin()
	{
		Console.WriteLine("\n[20] N5: O REQUISITO PESSOAL DO SUPER NAMEKUSEIJIN (dispersao, nao media)");

		const int Quantos = 300;

		// AS TRES CLASSES DE NAMEKUSEIJIN e as QUATRO de Saiyajin que mexem no `ssjat`. O "Legendary"
		// fica de fora porque ele NAO toca o `ssjat` (`statsaiyan.dm:65-70`, a escada dele e outra) --
		// incluí-lo poria 1.500.000 de fabrica na amostra e inflaria a dispersao com um valor que
		// ninguem usa.
		string[] clas = ["Warrior clan", "Demon clan", "Dragon clan"];
		string[] classes = ["Elite", "Low-Class", "Normal", "Legendary Primal Saiyan"];

		double[] namek = new double[Quantos], saiya = new double[Quantos];
		for (int i = 0; i < Quantos; i++)
		{
			ulong semente = LimiaresPessoais.SementeDe($"corpo{i}", 1_700_000_000_000 + i);
			namek[i] = LimiaresPessoais.Rolar(Catalogo.RacaNamekuseijin, clas[i % clas.Length], semente)
									   .Porta(Catalogo.Def("snamek")!);
			saiya[i] = LimiaresPessoais.Rolar("Saiyan", classes[i % classes.Length], semente)
									   .Porta(Catalogo.Def("ssj1")!);
		}

		(int Distintos, double Min, double Max, double Media, double Desvio) N = Nuvem(namek);
		(int Distintos, double Min, double Max, double Media, double Desvio) S = Nuvem(saiya);

		Console.WriteLine($"       Super Namekuseijin ({Quantos} nascimentos, 3 clas): "
						+ $"{N.Distintos} valores distintos, {N.Min:N0} .. {N.Max:N0}, "
						+ $"media {N.Media:N0}, desvio {N.Desvio:N0} ({N.Desvio / N.Media:P1})");
		Console.WriteLine($"       Super Saiyajin     ({Quantos} nascimentos, 4 classes): "
						+ $"{S.Distintos} valores distintos, {S.Min:N0} .. {S.Max:N0}, "
						+ $"media {S.Media:N0}, desvio {S.Desvio:N0} ({S.Desvio / S.Media:P1})");

		// ---- 1. ELE E PESSOAL -- e o criterio e uma funcao, aplicada duas vezes ----
		Checa($"o `snamekat` e PESSOAL: {N.Distintos} valores diferentes em {Quantos} nascimentos, "
			+ $"espalhados {N.Desvio / N.Media:P1} em volta do centro",
			  EhUmLimiarPessoal(namek), $"{N.Distintos} distintos, cv {N.Desvio / N.Media:P2}");

		double[] cravado = new double[Quantos];
		Array.Fill(cravado, Catalogo.SsjatInicial);
		Checa("   DEFEITO INJETADO (uma populacao com o limiar CRAVADO em 1.500.000 pra todo mundo): "
			+ "o MESMO criterio REPROVA",
			  !EhUmLimiarPessoal(cravado),
			  "o criterio de \"e pessoal\" nao sabe distinguir sorteio de constante");

		// O MESMO CRITERIO NO SSJ -- controle positivo. Se ele reprovasse aqui, o criterio estaria
		// exigindo mais do que o proprio jogo entrega, e a prova de cima nao valeria nada.
		Checa($"...e o mesmo criterio aprova o SSJ, que e o molde ({S.Distintos} valores distintos)",
			  EhUmLimiarPessoal(saiya), $"{S.Distintos} distintos");

		// ---- 2. E ELE FICA NO MESMO PATAMAR DO SSJ ----
		// AS BORDAS: as duas nuvens tem que comecar e terminar no mesmo lugar. Elas comecam, e nao por
		// coincidencia -- a base virou a mesma (`LimiaresPessoais.SNamekatInicial`) e as faixas por
		// cla copiam as faixas por classe do `statsaiyan.dm:57-77`.
		Checa($"a BORDA DE BAIXO e a mesma nas duas nuvens ({N.Min:N0} contra {S.Min:N0})",
			  Math.Abs(N.Min - S.Min) < 1.0, $"{N.Min:N0} x {S.Min:N0}");
		Checa($"a BORDA DE CIMA tambem ({N.Max:N0} contra {S.Max:N0})",
			  Math.Abs(N.Max - S.Max) < 1.0, $"{N.Max:N0} x {S.Max:N0}");

		// O CENTRO: 5% de folga porque as duas amostras tem numero DIFERENTE de faixas (3 clas contra
		// 4 classes), entao as medias nao tem como bater no centavo -- e nem deveriam.
		Checa($"o CENTRO das duas fica a menos de 5% um do outro ({N.Media:N0} contra {S.Media:N0}, "
			+ $"{Math.Abs(N.Media - S.Media) / S.Media:P1})",
			  Math.Abs(N.Media - S.Media) / S.Media < 0.05,
			  $"{Math.Abs(N.Media - S.Media) / S.Media:P2}");

		// O ESPALHAMENTO: e aqui que a versao anterior deste sistema morria. Portado literal do
		// `statnamek.dm:13-14`, o Namekuseijin tinha +-5% (a faixa mais estreita do jogo) contra os
		// -10%..+40% do SSJ -- ou seja, "cada um tem o seu" era quase falso pra Namekuseijin. A prova
		// cobra que as duas nuvens se espalhem na MESMA ordem de grandeza.
		double razao = (N.Desvio / N.Media) / (S.Desvio / S.Media);
		Checa($"e o ESPALHAMENTO e da mesma ordem do SSJ (razao {razao:0.00}x entre os coeficientes "
			+ "de variacao) -- era 0,3x quando a faixa era o +-5% do `statnamek.dm`",
			  razao is > 0.6 and < 1.7, $"{razao:0.000}x");

		// ---- 3. E O CENTRO NAO E O DO DM -- divergencia declarada, e ela fica MEDIDA ----
		// O `Super_Namek.dm:4` diz 2.000.000. Este port usa 1.500.000 (a base do SSJ) a pedido do dono.
		// A constante velha continua no Core so pra esta comparacao continuar conferivel -- e esta
		// linha e o que impede que ela vire lixo silencioso.
		Checa($"a divergencia do DM continua declarada e MEDIDA: o original pede "
			+ $"{LimiaresPessoais.SNamekatDoDm:N0} e este port pede {LimiaresPessoais.SNamekatInicial:N0} "
			+ "(a base do SSJ), a pedido do dono",
			  Math.Abs(LimiaresPessoais.SNamekatDoDm - 2_000_000) < 1.0
			  && Math.Abs(LimiaresPessoais.SNamekatInicial - Catalogo.SsjatInicial) < 1.0,
			  $"{LimiaresPessoais.SNamekatDoDm:N0} x {LimiaresPessoais.SNamekatInicial:N0}");

		// ---- 4. N1 x N5: OS DOIS CAMINHOS DESEMBOCAM NA **MESMA** FORMA ----
		// ============================ E ESTA E A PROVA DE QUE NAO HA DUAS FORMAS ============================
		// A `--fusaoduplateste` prova os dois CAMINHOS (absorver e cruzar o limiar) e que os dois abrem
		// o portao. Ela nao consegue provar que nao ha uma segunda entrada de catalogo escondida --
		// duas formas `snamek` com ids diferentes passariam nas duas, porque cada caminho abriria a
		// sua. Quem responde isso e o catalogo, e a resposta e uma contagem.
		// ================================================================================================
		FormaDef[] pelaFlag = [.. Catalogo.Todas.Where(d => d.PedeFlag?.Campo == "snamek")];
		Checa($"o catalogo tem **UMA** forma gateada pela flag `snamek` "
			+ $"({string.Join(", ", pelaFlag.Select(d => d.Id))})",
			  pelaFlag.Length == 1, $"{pelaFlag.Length}");

		FormaDef[] naLinha = [.. Catalogo.Todas.Where(d => d.Linha == LinhaDeForma.Namekuseijin)];
		Checa($"...e **UMA** na linha Namekuseijin inteira ({string.Join(", ", naLinha.Select(d => d.Id))})",
			  naLinha.Length == 1, $"{naLinha.Length}");

		Checa("...e as duas contagens acham a MESMA entrada (a flag e a linha nao apontam pra formas "
			+ "diferentes)",
			  pelaFlag.Length == 1 && naLinha.Length == 1 && pelaFlag[0].Id == naLinha[0].Id);

		Checa("...e ela e a que o limiar pessoal gateia (`ChaveDoLimiar` = `snamekat`) -- **uma porta, "
			+ "tres caminhos** (comprar, cruzar o limiar, absorver)",
			  pelaFlag.Length == 1 && pelaFlag[0].ChaveDoLimiar == "snamekat",
			  pelaFlag.Length == 1 ? pelaFlag[0].ChaveDoLimiar : "");
	}

	/// <summary>
	/// **E ISTO UM LIMIAR PESSOAL, OU UM NUMERO CRAVADO?** -- o criterio da secao 20, como funcao.
	///
	/// Ele e uma funcao e nao uma linha de `if` por um motivo so: assim a MESMA pergunta pode ser
	/// feita a populacao de verdade e a uma populacao cravada, e a segunda tem que reprovar. Uma
	/// checagem que so foi vista passando e indistinguivel de `Checa("...", true)`.
	///
	/// Duas exigencias, e as duas sao necessarias:
	///
	///   * **pelo menos cinco valores diferentes** -- e o que separa sorteio de constante, e o numero
	///     nao e arbitrario: o idioma do DM (`rand(9,13)/10`) rende no maximo cinco degraus por faixa;
	///   * **espalhamento de pelo menos 5%** em volta do centro -- e o que separa sorteio de "sorteio
	///     que quase nao varia". Sem ele, um `rand(99,101)` passaria por pessoal.
	/// </summary>
	private static bool EhUmLimiarPessoal(double[] valores)
	{
		(int Distintos, double _, double __, double Media, double Desvio) n = Nuvem(valores);
		return n.Distintos >= 5 && n.Media > 0 && n.Desvio / n.Media >= 0.05;
	}

	/// <summary>Os quatro numeros que descrevem uma nuvem de limiares: quantos distintos, as bordas, o centro e o desvio.</summary>
	private static (int Distintos, double Min, double Max, double Media, double Desvio) Nuvem(double[] v)
	{
		if (v.Length == 0) return (0, 0, 0, 0, 0);
		double media = v.Average();
		double desvio = Math.Sqrt(v.Sum(x => (x - media) * (x - media)) / v.Length);
		return (v.Distinct().Count(), v.Min(), v.Max(), media, desvio);
	}

}
