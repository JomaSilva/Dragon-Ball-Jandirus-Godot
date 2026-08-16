using System.Reflection;
using Jandirus.Core.Social;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DO CONVIVIO (`convivio`) -- o substrato social portado de `Contacts.dm` e
/// `Friendship.dm`, e as tres perguntas que decidem se alguem entra em furia.
///
/// ============================ O QUE ELA COBRE ============================
///   [1] OS NUMEROS DO DM, um a um -- e, ao lado deles, os que **nao** sao do DM: o limiar em
///       minutos, o piso negativo e o preco de matar (a divergencia pedida pelo dono).
///   [2] A PROXIMIDADE, iterada: o passo 499 ainda nao e amizade e o 500 e -- **conviver faz amigo
///       sozinho aqui**, ao contrario do original, e o degrau exato e medido dos dois lados.
///   [3] AS TRES PERGUNTAS DA RAIVA, contra as NOVE relacoes, uma por uma. Sao as listas de
///       `Death.dm:79`, `KO.dm:36` e `Death.dm:75` -- e a tabela e escrita a mao aqui de proposito:
///       ela e a copia independente contra a qual o codigo e conferido. Um `if` a mais no Core
///       aparece como divergencia em vez de virar a nova verdade.
///   [4] OS ROTULOS e os precos das declaracoes -- os numeros que o proprio verb do DM lista.
///   [5] O ODIO: so contra rival declarado, com teto.
///   [5b] A QUEDA PRO NEGATIVO: matar tira pontos, o zero vira inimizade declarada, e o inimigo
///       nao se perdoa por convivencia -- so por um pedido aceito. Nada disto tem original.
///   [6] O ESTADO SOBREVIVE A UM SERIALIZADOR DE CAMPOS -- por reflexao. Este projeto ja perdeu as
///       cores de roupa por um `readonly` que o `System.Text.Json` ignorava CALADO; a lista de
///       amigos sumindo do mesmo jeito apareceria como "o SSJ1 as vezes nao vem".
/// ========================================================================
///
///     dotnet run --project Tools/AssetPipeline -- convivio
/// </summary>
public static class ConvivioBench
{
	private static int _ok, _falhou;

	private static void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; Console.WriteLine($"  OK   {nome}"); }
		else { _falhou++; Console.WriteLine($"  FALHA {nome}   {detalhe}"); }
	}

	public static int Rodar()
	{
		Console.WriteLine("=== BANCADA DO CONVIVIO (o known-people, e o que ele destrava) ===\n");

		OsNumeros();
		AProximidade();
		AsTresPerguntas();
		OsRotulosEOsPrecos();
		OOdio();
		AQuedaParaONegativo();
		OEstadoAtravessaOSerializador();

		Console.WriteLine($"\n=== {_ok} OK, {_falhou} FALHA ===");
		return _falhou == 0 ? 0 : 1;
	}

	// =====================================================================
	// 1. OS NUMEROS
	// =====================================================================
	private static void OsNumeros()
	{
		Console.WriteLine("[1] OS NUMEROS DO `Friendship.dm`");

		Checa("FRIEND_RANGE = 6 tiles", Convivio.AlcanceDeConvivioTiles == 6);
		Checa("FRIEND_RATE = 0,1", Math.Abs(Convivio.TaxaDeAmizade - 0.1) < 1e-9);
		Checa("FRIEND_REQ = 50", Convivio.ExigenciaDeAmigo == 50);
		Checa("o teto de tudo e 200 (o degrau `Bonded`)", Convivio.TetoDeAmizade == 200);

		Checa("os 10 ciclos de 0,3 s do `GlobalStats` viraram 3 segundos",
			  Math.Abs(Convivio.SegundosEntreAproximacoes - 3) < 1e-9);

		Checa("ENMITY_HIT = 1", Convivio.InimizadePorGolpe == 1);
		Checa("ENMITY_FRIEND_KO = 25", Convivio.InimizadePorAmigoCaido == 25);
		Checa("ENMITY_FRIEND_KILL = 60", Convivio.InimizadePorAmigoMorto == 60);
		Checa("ENMITY_MAX = 200", Convivio.TetoDeInimizade == 200);

		// ============================ E OS NUMEROS QUE **NAO** SAO DO DM ============================
		// O `ACQUAINTANCE_CAP = 49` NAO foi portado (a pedido do dono: aqui amizade e convivio, nao
		// convite), entao a checagem que estava aqui virou o avesso dela mesma -- ela agora prende a
		// DIVERGENCIA. Sem esta linha, alguem "restaurando a fidelidade ao DM" poria o teto de volta e
		// o sintoma seria o dono dizendo *"eu fico horas com o cara e ele nunca vira amigo"*.
		Checa("**o limiar de amigo e alcancavel so por convivencia** (o 49 do DM ficou pra tras)",
			  Convivio.ExigenciaDeAmigo <= Convivio.TetoDeAmizade
			  && Convivio.ExigenciaDeAmigo > 0);

		// A CONTA QUE O DONO PEDIU EM MINUTOS, e ela e derivada -- se alguem mexer na taxa ou na
		// cadencia, e este numero que muda, nao um comentario.
		Checa("virar amigo custa 25 minutos de convivio",
			  Math.Abs(Convivio.MinutosParaVirarAmigo - 25) < 1e-9,
			  $"{Convivio.MinutosParaVirarAmigo:0.##} min");

		Checa("o piso e o espelho do teto (-200)", Convivio.PisoDeAmizade == -Convivio.TetoDeAmizade);
		Checa("**matar custa o mesmo que o odio ganha** (60 na morte, 25 no nocaute)",
			  Convivio.PerdaPorMorte == Convivio.InimizadePorAmigoMorto
			  && Convivio.PerdaPorNocaute == Convivio.InimizadePorAmigoCaido,
			  $"{Convivio.PerdaPorMorte} / {Convivio.PerdaPorNocaute}");
		Checa("...e uma morte derruba do limiar de amigo direto pro NEGATIVO",
			  Convivio.ExigenciaDeAmigo - Convivio.PerdaPorMorte < 0,
			  $"{Convivio.ExigenciaDeAmigo - Convivio.PerdaPorMorte}");
		Console.WriteLine();
	}

	// =====================================================================
	// 2. A PROXIMIDADE
	// =====================================================================
	private static void AProximidade()
	{
		Console.WriteLine("[2] CONVIVER FAZ AMIGO (a divergencia do dono), E O PASSO EXATO APARECE");

		var eu = new Convivio();
		const string ele = "conta#0";

		// 499 PASSOS E NAO 500: o que esta secao mede e o DEGRAU, e um degrau so existe se houver o
		// lado de ca. "Depois de muito tempo ele e amigo" passaria igual num sistema que faz amigo no
		// primeiro segundo -- que e exatamente o modo de falha desta mudanca.
		for (int i = 0; i < 499; i++) eu.Aproximar(ele);
		Checa("um passo antes do limiar ele AINDA nao e amigo", !eu.EhAmigo(ele),
			  $"{eu.PontosDeAmizade(ele):0.###} -> {Convivio.RotuloDeProximidade(eu.PontosDeAmizade(ele))}");

		// E O PASSO 500 DEVOLVE TRUE **UMA VEZ**: e nesse TRUE que o servidor pendura o aviso, entao
		// um `>=` trocado por `>` aqui apareceria como "o jogo nunca me disse que viramos amigos".
		Checa("**o passo 500 atravessa o limiar e AVISA (devolve TRUE)**", eu.Aproximar(ele));
		Checa("...e agora ele e amigo, com 50 cravados (sem deriva de ponto flutuante)",
			  eu.EhAmigo(ele) && eu.PontosDeAmizade(ele) == Convivio.ExigenciaDeAmigo,
			  $"{eu.PontosDeAmizade(ele):0.#################}");
		Checa("...e o passo seguinte NAO avisa de novo", !eu.Aproximar(ele));
		Checa("...e 500 passos de 3 s sao os 25 minutos que o `MinutosParaVirarAmigo` promete",
			  Math.Abs(500 * Convivio.SegundosEntreAproximacoes / 60.0
					   - Convivio.MinutosParaVirarAmigo) < 1e-9);

		// ACEITAR UM PEDIDO CONTINUA FUNCIONANDO -- ele virou ATALHO, e nao porta unica
		var atalho = new Convivio();
		atalho.AceitarAmizade("x");
		Checa("o pedido aceito continua fazendo amigo na hora (o atalho)", atalho.EhAmigo("x"),
			  $"{atalho.PontosDeAmizade("x"):0.###}");

		// E DEPOIS DE AMIGO A CONVIVENCIA VOLTA A SUBIR, ate 200
		for (int i = 0; i < 3000; i++) eu.Aproximar(ele);
		Checa("depois de amigo, a convivencia sobe ate `Ligado` (200)",
			  Math.Abs(eu.PontosDeAmizade(ele) - Convivio.TetoDeAmizade) < 1e-6,
			  $"{eu.PontosDeAmizade(ele):0.###}");

		// ACEITAR DE NOVO NAO REBAIXA: e o `max(...)` do `friend_request_resolve`.
		eu.AceitarAmizade(ele);
		Checa("aceitar um pedido de novo NAO rebaixa quem ja era `Ligado`",
			  eu.PontosDeAmizade(ele) == Convivio.TetoDeAmizade, $"{eu.PontosDeAmizade(ele):0.###}");

		// RIVAL DECLARADO NAO RENDE AMIZADE (`Friendship.dm:36`)
		var outro = new Convivio();
		const string inimigo = "conta#1";
		outro.AlternarRival(inimigo);
		for (int i = 0; i < 100; i++) outro.Aproximar(inimigo);
		Checa("rival declarado nao rende amizade nenhuma por proximidade",
			  outro.PontosDeAmizade(inimigo) == 0, $"{outro.PontosDeAmizade(inimigo):0.###}");

		// A FOTOGRAFIA: a primeira vez cria, e o throttle de 1 minuto segura a segunda
		var lista = new Convivio();
		Checa("fotografar alguem novo devolve TRUE (primeiro encontro)",
			  lista.Fotografar("s", "Goku", "Saiyan", "Normal", "Male", 30, 1000));
		Checa("...e fotografar de novo no mesmo minuto devolve FALSE",
			  !lista.Fotografar("s", "Goku", "Saiyan", "Normal", "Male", 30, 1500));
		Checa("...e nao reescreve a ficha antes da hora",
			  lista.Fotografar("s", "OUTRO NOME", "Namekian", "?", "?", 0, 1500) == false
			  && lista.Ficha("s")!.Nome == "Goku", lista.Ficha("s")!.Nome);
		lista.Fotografar("s", "Goku (velho)", "Saiyan", "Normal", "Male", 300,
						 1000 + Convivio.IntervaloDoRetratoMs + 1);
		Checa("...e passado o minuto ela e refeita", lista.Ficha("s")!.Idade == 300,
			  $"{lista.Ficha("s")!.Idade}");

		// A FAMILIARIDADE E A RELACAO SAO MINHAS -- a foto e dele. Refotografar nao pode zerar as
		// duas, senao passar na frente de alguem apagaria anos de convivio.
		lista.SomarFamiliaridade("s");
		lista.Ficha("s")!.Relacao = Relacao.Bom;
		lista.Fotografar("s", "Goku", "Saiyan", "Normal", "Male", 301,
						 1000 + 3 * Convivio.IntervaloDoRetratoMs);
		Checa("refotografar NAO zera a familiaridade nem a relacao",
			  lista.Familiaridade("s") == 1 && lista.RelacaoCom("s") == Relacao.Bom,
			  $"{lista.Familiaridade("s")} / {lista.RelacaoCom("s")}");
		Console.WriteLine();
	}

	// =====================================================================
	// 3. AS TRES PERGUNTAS DA RAIVA
	// =====================================================================
	/// <summary>
	/// ============================ A TABELA E ESCRITA A MAO, E ISSO E O METODO ============================
	/// As tres listas do DM sao curtas e diferentes entre si, e o jeito de errar e obvio: copiar a
	/// lista da MORTE pra a da QUEDA (as duas tem `is_friend` no `||`, entao o teste "amigo enfurece"
	/// passaria nas duas). Escrever aqui as nove relacoes com o resultado esperado, uma por uma, e
	/// o que faz uma lista errada aparecer como divergencia em vez de virar a nova verdade.
	///
	///   * `Death.dm:79` -- MORTE:  Good, Very Good, Love, Rival/Good   (|| is_friend)
	///   * `KO.dm:36`    -- QUEDA:  Very Good, Love                     (|| is_friend)
	///   * `Death.dm:75` -- ALGOZ:  NAO e inimigo se for Very Good, Love, Good, Rival/Good ou amigo
	/// ======================================================================================================
	/// </summary>
	private static void AsTresPerguntas()
	{
		Console.WriteLine("[3] AS TRES PERGUNTAS QUE DECIDEM A FURIA (as listas do DM, uma a uma)");

		(Relacao Rel, bool Morte, bool Queda, bool Inimigo)[] tabela =
		[
			//                        morte  queda  algoz-e-inimigo
			(Relacao.Nenhuma,         false, false, true),
			(Relacao.Neutro,          false, false, true),
			(Relacao.Bom,             true,  false, false),
			(Relacao.MuitoBom,        true,  true,  false),
			(Relacao.Amor,            true,  true,  false),
			(Relacao.RivalBom,        true,  false, false),
			(Relacao.RivalRuim,       false, false, true),
			(Relacao.Ruim,            false, false, true),
			(Relacao.MuitoRuim,       false, false, true),
			(Relacao.Odio,            false, false, true),
		];

		foreach ((Relacao rel, bool morte, bool queda, bool inimigo) in tabela)
		{
			var c = new Convivio();
			c.Fotografar("s", "Fulano", "?", "?", "?", 0, 0);
			c.Ficha("s")!.Relacao = rel;

			Checa($"[{rel}] MORTE enfurece? {morte}", c.LutoPorMorte("s") == morte);
			Checa($"[{rel}] QUEDA enfurece? {queda}", c.LutoPorQueda("s") == queda);
			Checa($"[{rel}] o algoz e inimigo? {inimigo}", c.AlgozEhInimigo("s") == inimigo);
		}

		// E O `||` COM A AMIZADE, que e a outra metade das duas primeiras listas: sem relacao
		// declarada nenhuma, ser amigo ja basta pras duas.
		var amigo = new Convivio();
		amigo.AceitarAmizade("s");
		Checa("so ser AMIGO ja enfurece por morte", amigo.LutoPorMorte("s"));
		Checa("so ser AMIGO ja enfurece por queda", amigo.LutoPorQueda("s"));
		Checa("e um amigo NUNCA e o inimigo que justifica a furia (`Death.dm:75`)",
			  !amigo.AlgozEhInimigo("s"));

		// E QUEM NAO E NADA: desconhecido morrendo na sua frente nao move ninguem, mas quem mata
		// um desconhecido E inimigo (porque nao ha nada dizendo que nao e).
		var estranho = new Convivio();
		Checa("um desconhecido morrendo nao enfurece", !estranho.LutoPorMorte("s"));
		Checa("mas quem mata E inimigo aos olhos de quem nao o conhece", estranho.AlgozEhInimigo("s"));
		Console.WriteLine();
	}

	// =====================================================================
	// 4. ROTULOS E PRECOS
	// =====================================================================
	private static void OsRotulosEOsPrecos()
	{
		Console.WriteLine("[4] OS DEGRAUS E O PRECO DE CADA DECLARACAO");

		Checa("0 -> Mal conhecido", Convivio.RotuloDeProximidade(0) == "Mal conhecido");
		Checa("5 -> Conhecido", Convivio.RotuloDeProximidade(5) == "Conhecido");
		Checa("20 -> Familiar", Convivio.RotuloDeProximidade(20) == "Familiar");
		Checa("49 -> Familiar (ainda NAO e amigo)", Convivio.RotuloDeProximidade(49) == "Familiar");
		Checa("50 -> Amigo", Convivio.RotuloDeProximidade(50) == "Amigo");
		Checa("200 -> Ligado", Convivio.RotuloDeProximidade(200) == "Ligado");

		// A FAIXA NEGATIVA, que no DM nao existe -- e o -1 e o degrau que importa: e onde o jogador
		// le, na aba, que aquela pessoa deixou de ser neutra.
		Checa("-1 -> Desafeto", Convivio.RotuloDeProximidade(-1) == "Desafeto");
		Checa("-49 -> Desafeto", Convivio.RotuloDeProximidade(-49) == "Desafeto");
		Checa("-50 -> Inimigo (o espelho exato do +50)", Convivio.RotuloDeProximidade(-50) == "Inimigo");
		Checa("-200 -> Inimigo mortal (o espelho do `Ligado`)",
			  Convivio.RotuloDeProximidade(-200) == "Inimigo mortal");

		Checa("0 de odio -> nenhum rotulo", Convivio.RotuloDeInimizade(0) == "");
		Checa("5 -> Antipatizado", Convivio.RotuloDeInimizade(5) == "Antipatizado");
		Checa("25 -> Rival", Convivio.RotuloDeInimizade(25) == "Rival");
		Checa("75 -> Rival odiado", Convivio.RotuloDeInimizade(75) == "Rival odiado");
		Checa("150 -> Nemesis", Convivio.RotuloDeInimizade(150) == "Nemesis");

		// OS PRECOS QUE O PROPRIO `input()` DO DM LISTA (`Contacts.dm:117`)
		Checa("Neutro nao custa nada", Convivio.FamiliaridadeExigida(Relacao.Neutro) == 0);
		Checa("Ruim custa 10", Convivio.FamiliaridadeExigida(Relacao.Ruim) == 10);
		Checa("Rival (bom ou ruim) custa 15",
			  Convivio.FamiliaridadeExigida(Relacao.RivalBom) == 15
			  && Convivio.FamiliaridadeExigida(Relacao.RivalRuim) == 15);
		Checa("Muito ruim custa 20", Convivio.FamiliaridadeExigida(Relacao.MuitoRuim) == 20);
		Checa("Bom custa 50", Convivio.FamiliaridadeExigida(Relacao.Bom) == 50);
		Checa("Odio custa 50", Convivio.FamiliaridadeExigida(Relacao.Odio) == 50);
		Checa("Muito bom custa 100", Convivio.FamiliaridadeExigida(Relacao.MuitoBom) == 100);
		Checa("Amor custa 200", Convivio.FamiliaridadeExigida(Relacao.Amor) == 200);
		Checa("`Nenhuma` nao se declara (preco impossivel)",
			  Convivio.FamiliaridadeExigida(Relacao.Nenhuma) == int.MaxValue);

		// AS NOVE OPCOES DO MENU sao as nove do `input()` -- nem uma a mais, nem uma a menos, e
		// nenhuma delas e a `Nenhuma`.
		Checa("o menu oferece as 9 declaracoes do original", Convivio.Declaraveis.Length == 9,
			  $"{Convivio.Declaraveis.Length}");
		Checa("...e `Nenhuma` nao esta entre elas",
			  Array.IndexOf(Convivio.Declaraveis, Relacao.Nenhuma) < 0);
		Checa("...e toda declaravel vai e volta pelo nome",
			  Convivio.Declaraveis.All(r => Convivio.RelacaoPorNome(Convivio.NomeDaRelacao(r)) == r),
			  string.Join(",", Convivio.Declaraveis.Select(
				  r => $"{r}->{Convivio.NomeDaRelacao(r)}->{Convivio.RelacaoPorNome(Convivio.NomeDaRelacao(r))}")));
		Checa("...e o nome em ingles do original tambem casa",
			  Convivio.RelacaoPorNome("Very Good") == Relacao.MuitoBom
			  && Convivio.RelacaoPorNome("Rival/Good") == Relacao.RivalBom
			  && Convivio.RelacaoPorNome("Love") == Relacao.Amor);
		Console.WriteLine();
	}

	// =====================================================================
	// 5. O ODIO
	// =====================================================================
	private static void OOdio()
	{
		Console.WriteLine("[5] O ODIO SO CRESCE CONTRA QUEM VOCE ESCOLHEU");

		var c = new Convivio();
		const string foe = "conta#9";

		c.SomarInimizade(foe, 100);
		Checa("sem declarar rival, o odio nao anda", c.PontosDeInimizade(foe) == 0,
			  $"{c.PontosDeInimizade(foe):0}");

		Checa("declarar devolve TRUE", c.AlternarRival(foe));
		c.SomarInimizade(foe, 100);
		Checa("declarado, ele anda", c.PontosDeInimizade(foe) == 100, $"{c.PontosDeInimizade(foe):0}");

		c.SomarInimizade(foe, 500);
		Checa("e para no teto de 200", c.PontosDeInimizade(foe) == Convivio.TetoDeInimizade,
			  $"{c.PontosDeInimizade(foe):0}");

		Checa("o mesmo gesto desfaz a rivalidade", !c.AlternarRival(foe));
		Checa("...e desfeita, ela nao duplica na lista", c.Rivais.Count == 0, $"{c.Rivais.Count}");

		// E O ODIO GUARDADO SOBREVIVE A DESISTENCIA -- tirar alguem de rival nao apaga o que ja
		// aconteceu. E o DM tambem nao apaga (`Declare_Rival` so mexe em `rivals`).
		Checa("mas o odio ja acumulado continua la", c.PontosDeInimizade(foe) == 200,
			  $"{c.PontosDeInimizade(foe):0}");
		Console.WriteLine();
	}

	// =====================================================================
	// 5b. A QUEDA PRO NEGATIVO -- **A PARTE QUE NAO E PORTE**
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTA SECAO PRENDE ============================
	/// Tudo aqui e invencao do dono (no DM a amizade NUNCA diminui), entao nao ha original pra
	/// conferir -- o que faz da bancada a unica definicao escrita da regra. Ela mede as tres coisas
	/// que o pedido dele pede, e a quarta que ele nao pediu mas que decide se o sistema e jogavel:
	///
	///   1. matar TIRA pontos, e o numero e o do odio;
	///   2. cruzar o zero DECLARA a rivalidade sozinho -- e e por isso que "ser inimigo" faz algo;
	///   3. inimigo NAO volta a ser amigo so por ficar por perto;
	///   4. **e ele nao fica condenado pra sempre**: um pedido aceito (os dois lados) desfaz.
	/// ==================================================================================
	/// </summary>
	private static void AQuedaParaONegativo()
	{
		Console.WriteLine("[5b] MATAR CUSTA O LACO, E O NEGATIVO E INIMIZADE (pedido do dono)");

		// O CASO COMUM: um estranho te mata. Zero pontos, e o golpe leva pro negativo de uma vez.
		var eu = new Convivio();
		const string assassino = "conta#7";
		Checa("**um estranho que te mata vira INIMIGO no mesmo golpe (devolve TRUE)**",
			  eu.Afastar(assassino, Convivio.PerdaPorMorte));
		Checa("...e os pontos sao -60", eu.PontosDeAmizade(assassino) == -Convivio.PerdaPorMorte,
			  $"{eu.PontosDeAmizade(assassino):0.###}");
		Checa("...e `EhInimigo` diz sim", eu.EhInimigo(assassino), "");
		Checa("...e ele entrou na lista de RIVAIS sozinho (e o que liga tudo o mais)",
			  eu.EhRival(assassino), "");
		Checa("...e o odio subiu na mesma medida (o mesmo evento no outro livro)",
			  eu.PontosDeInimizade(assassino) == Convivio.PerdaPorMorte,
			  $"{eu.PontosDeInimizade(assassino):0}");
		Checa("...e ele NAO avisa de novo no segundo golpe (so a travessia)",
			  !eu.Afastar(assassino, Convivio.PerdaPorMorte));
		Checa("...mas o odio continua somando depois de declarado",
			  eu.PontosDeInimizade(assassino) == 2 * Convivio.PerdaPorMorte,
			  $"{eu.PontosDeInimizade(assassino):0}");

		// E FICAR PERTO DELE NAO PERDOA: rival declarado nao rende amizade (`Friendship.dm:36`), e e
		// essa linha velha que sustenta a regra nova.
		double antes = eu.PontosDeAmizade(assassino);
		for (int i = 0; i < 1000; i++) eu.Aproximar(assassino);
		Checa("**conviver com o inimigo nao o perdoa** (mil passos, nem um ponto)",
			  eu.PontosDeAmizade(assassino) == antes, $"{eu.PontosDeAmizade(assassino):0.###}");

		// O PISO SEGURA: sem ele, morrer em serie levaria a amizade a -infinito e o rotulo a mentir.
		for (int i = 0; i < 100; i++) eu.Afastar(assassino, Convivio.PerdaPorMorte);
		Checa("o piso de -200 segura a queda", eu.PontosDeAmizade(assassino) == Convivio.PisoDeAmizade,
			  $"{eu.PontosDeAmizade(assassino):0.###}");

		// A RECONCILIACAO: o pedido aceito atravessa o negativo E desfaz a rivalidade -- senao o
		// `Aproximar` recusaria crescer a amizade que acabou de nascer.
		eu.AceitarAmizade(assassino);
		Checa("um pedido aceito reconcilia (de -200 pra amigo)", eu.EhAmigo(assassino),
			  $"{eu.PontosDeAmizade(assassino):0.###}");
		Checa("...e tira a marca de rival, senao a amizade nova nao cresceria",
			  !eu.EhRival(assassino), "");
		Checa("...e o convivio volta a render", eu.Aproximar(assassino) == false
			  && eu.PontosDeAmizade(assassino) > Convivio.ExigenciaDeAmigo,
			  $"{eu.PontosDeAmizade(assassino):0.###}");
		Checa("...mas o ODIO acumulado nao se apaga (fazer as pazes nao apaga o que houve)",
			  eu.PontosDeInimizade(assassino) == Convivio.TetoDeInimizade,
			  $"{eu.PontosDeInimizade(assassino):0}");

		// O AMIGO DE LONGA DATA TEM CREDITO: 200 - 60 ainda e amizade. E o que o numero quer dizer.
		var laco = new Convivio();
		laco.Amizade["s"] = Convivio.TetoDeAmizade;
		Checa("um vinculo `Ligado` (200) sobrevive a UMA morte", !laco.Afastar("s", Convivio.PerdaPorMorte)
			  && laco.EhAmigo("s"), $"{laco.PontosDeAmizade("s"):0.###}");
		laco.Afastar("s", Convivio.PerdaPorMorte);
		laco.Afastar("s", Convivio.PerdaPorMorte);
		Checa("...e cai pro negativo na quarta", laco.Afastar("s", Convivio.PerdaPorMorte)
			  && laco.EhInimigo("s"), $"{laco.PontosDeAmizade("s"):0.###}");

		// ============================ A DECLARACAO DE AFETO CAI COM O AFETO ============================
		// Achado pela bancada AO VIVO, e o defeito era mecanico: a relacao declarada e a SEGUNDA porta
		// da raiva (`is_friend() || check_relation(...)`), entao um "muito bom" sobrevivendo ao proprio
		// assassinato deixava a vitima entrando em furia pela morte de quem a matou -- e, no outro
		// sentido, mantinha o assassino fora da lista de inimigos, calando a plateia na proxima vez.
		var declarou = new Convivio();
		declarou.Fotografar("m", "O assassino", "?", "?", "?", 0, 0);
		declarou.Ficha("m")!.Relacao = Relacao.MuitoBom;
		Checa("(antes) uma declaracao de afeto ja enfurece por morte", declarou.LutoPorMorte("m"));

		declarou.Afastar("m", Convivio.PerdaPorMorte);
		Checa("**cair pro negativo derruba a declaracao de AFETO**",
			  declarou.RelacaoCom("m") == Relacao.Nenhuma, declarou.RelacaoCom("m").ToString());
		Checa("...e por isso ele nao enfurece mais ninguem por voce", !declarou.LutoPorMorte("m"));
		Checa("...e passa a contar como INIMIGO na pergunta que acende a furia da plateia",
			  declarou.AlgozEhInimigo("m"));

		// MAS A DECLARACAO DE DESAFETO FICA: apaga-la seria apagar o que a pessoa disse justamente
		// quando ela se provou certa.
		var jaDizia = new Convivio();
		jaDizia.Fotografar("n", "O sujeito", "?", "?", "?", 0, 0);
		jaDizia.Ficha("n")!.Relacao = Relacao.Odio;
		jaDizia.Afastar("n", Convivio.PerdaPorMorte);
		Checa("...mas um `odio` declarado NAO e apagado pela queda",
			  jaDizia.RelacaoCom("n") == Relacao.Odio, jaDizia.RelacaoCom("n").ToString());

		// QUEM JA ERA RIVAL DECLARADO NAO E "DES-DECLARADO" PELA QUEDA -- o bug que o `TornarRival`
		// existe pra impedir (o `AlternarRival` teria tirado a rivalidade justo de quem mais merece).
		var jaOdiava = new Convivio();
		jaOdiava.AlternarRival("z");
		jaOdiava.Afastar("z", Convivio.PerdaPorMorte);
		Checa("**cair pro negativo NAO desfaz uma rivalidade ja declarada**", jaOdiava.EhRival("z"), "");
		Console.WriteLine();
	}

	// =====================================================================
	// 6. O ESTADO ATRAVESSA UM SERIALIZADOR DE CAMPOS
	// =====================================================================
	/// <summary>
	/// ============================ O DEFEITO QUE ESTA SECAO EXISTE PRA PEGAR ============================
	/// O `CharacterSave` e serializado pelo `System.Text.Json` com `IncludeFields = true`. Isso quer
	/// dizer que um campo `readonly`, ou uma propriedade sem `set`, e **ignorado em silencio**: grava
	/// vazio, volta vazio, e nada acusa. Este projeto ja pagou exatamente isso -- as cores de roupa
	/// foram escritas, lidas e usadas por meses sem NUNCA persistir.
	///
	/// Aqui o estrago seria pior e mais confuso: a lista de amigos de todo mundo zeraria a cada
	/// logout, e o sintoma que o dono veria seria *"o SSJ1 as vezes nao vem"* -- que ninguem liga a
	/// um serializador.
	///
	/// A checagem e por REFLEXAO e nao por ida-e-volta de JSON de proposito: a ida-e-volta precisaria
	/// que eu reescrevesse aqui as `JsonSerializerOptions` do `AccountStore` (que mora no projeto do
	/// jogo e esta ferramenta nao referencia), e ai o teste passaria a medir a minha copia das
	/// opcoes. A ida-e-volta de verdade, com as opcoes de verdade, esta na bancada ao vivo
	/// (`GameServer.ConvivioTeste.ASobrevivenciaNoDisco`).
	/// ====================================================================================================
	/// </summary>
	private static void OEstadoAtravessaOSerializador()
	{
		Console.WriteLine("[6] TODO O ESTADO E GRAVAVEL (o `readonly` que apagou as cores de roupa)");

		foreach (Type t in new[] { typeof(Convivio), typeof(Conhecido) })
		{
			foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
				Checa($"{t.Name}.{f.Name} nao e readonly", !f.IsInitOnly);

			foreach (PropertyInfo pr in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
				Checa($"{t.Name}.{pr.Name} tem `set`", pr.CanWrite, "propriedade so-leitura");

			// E NAO PODE HAVER ESTADO ESCONDIDO: um campo privado nao vai pro JSON, entao ele
			// desapareceria no logout enquanto o resto sobrevive -- que e pior que perder tudo,
			// porque so metade do sistema fica errada.
			FieldInfo[] privados = [.. t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)];
			Checa($"{t.Name} nao tem estado privado (invisivel pro save)", privados.Length == 0,
				  string.Join(",", privados.Select(f => f.Name)));
		}

		// E OS NUMEROS DO ENUM SAO CRAVADOS: renumerar `Relacao` transformaria o "Amor" de todo
		// mundo em outra coisa na proxima carga, sem quebrar compilacao nenhuma.
		Checa("Relacao.Nenhuma = 0 (o padrao e o lado que RECUSA)", (byte)Relacao.Nenhuma == 0);
		Checa("Relacao.Neutro = 1", (byte)Relacao.Neutro == 1);
		Checa("Relacao.RivalRuim = 2", (byte)Relacao.RivalRuim == 2);
		Checa("Relacao.RivalBom = 3", (byte)Relacao.RivalBom == 3);
		Checa("Relacao.Ruim = 4", (byte)Relacao.Ruim == 4);
		Checa("Relacao.Bom = 5", (byte)Relacao.Bom == 5);
		Checa("Relacao.MuitoRuim = 6", (byte)Relacao.MuitoRuim == 6);
		Checa("Relacao.MuitoBom = 7", (byte)Relacao.MuitoBom == 7);
		Checa("Relacao.Odio = 8", (byte)Relacao.Odio == 8);
		Checa("Relacao.Amor = 9", (byte)Relacao.Amor == 9);
		Console.WriteLine();
	}
}
