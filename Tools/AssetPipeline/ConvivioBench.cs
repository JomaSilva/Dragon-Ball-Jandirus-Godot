using System.Reflection;
using Jandirus.Core.Social;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DO CONVIVIO (`convivio`) -- o substrato social portado de `Contacts.dm` e
/// `Friendship.dm`, e as tres perguntas que decidem se alguem entra em furia.
///
/// ============================ O QUE ELA COBRE ============================
///   [1] OS NUMEROS DO DM, um a um -- e sobretudo o `49 &lt; 50`, que e a regra inteira: conviver
///       faz CONHECIDO, e so um pedido aceito faz AMIGO.
///   [2] A PROXIMIDADE, iterada: 600 passos param no teto, e depois de amigo ela volta a subir.
///   [3] AS TRES PERGUNTAS DA RAIVA, contra as NOVE relacoes, uma por uma. Sao as listas de
///       `Death.dm:79`, `KO.dm:36` e `Death.dm:75` -- e a tabela e escrita a mao aqui de proposito:
///       ela e a copia independente contra a qual o codigo e conferido. Um `if` a mais no Core
///       aparece como divergencia em vez de virar a nova verdade.
///   [4] OS ROTULOS e os precos das declaracoes -- os numeros que o proprio verb do DM lista.
///   [5] O ODIO: so contra rival declarado, com teto.
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
		Checa("ACQUAINTANCE_CAP = 49", Convivio.TetoDeConhecido == 49);
		Checa("o teto de tudo e 200 (o degrau `Bonded`)", Convivio.TetoDeAmizade == 200);

		// A REGRA INTEIRA NUMA LINHA. Se um dia alguem "arredondar" o 49 pra 50, conviver passaria
		// a fazer amigo sozinho -- e o pedido de amizade, que e o unico gesto social do jogo,
		// viraria enfeite. E ninguem notaria: o sintoma seria "o SSJ1 vem mais facil agora".
		Checa("**o teto da proximidade fica UM PONTO ABAIXO da exigencia de amigo**",
			  Convivio.TetoDeConhecido == Convivio.ExigenciaDeAmigo - 1,
			  $"{Convivio.TetoDeConhecido} vs {Convivio.ExigenciaDeAmigo}");

		Checa("os 10 ciclos de 0,3 s do `GlobalStats` viraram 3 segundos",
			  Math.Abs(Convivio.SegundosEntreAproximacoes - 3) < 1e-9);

		Checa("ENMITY_HIT = 1", Convivio.InimizadePorGolpe == 1);
		Checa("ENMITY_FRIEND_KO = 25", Convivio.InimizadePorAmigoCaido == 25);
		Checa("ENMITY_FRIEND_KILL = 60", Convivio.InimizadePorAmigoMorto == 60);
		Checa("ENMITY_MAX = 200", Convivio.TetoDeInimizade == 200);
		Console.WriteLine();
	}

	// =====================================================================
	// 2. A PROXIMIDADE
	// =====================================================================
	private static void AProximidade()
	{
		Console.WriteLine("[2] CONVIVER FAZ CONHECIDO, NAO AMIGO");

		var eu = new Convivio();
		const string ele = "conta#0";

		// 490 passos de 0,1 dariam exatamente 49; 600 sao seis dezenas a mais, pra o vazamento (se
		// houvesse) aparecer.
		for (int i = 0; i < 600; i++) eu.Aproximar(ele);

		Checa("600 passos param no teto de conhecido",
			  Math.Abs(eu.PontosDeAmizade(ele) - Convivio.TetoDeConhecido) < 1e-6,
			  $"{eu.PontosDeAmizade(ele):0.###}");
		Checa("...e ele NAO e amigo", !eu.EhAmigo(ele), Convivio.RotuloDeProximidade(eu.PontosDeAmizade(ele)));
		Checa("...e o rotulo dele e `Familiar` (nao `Amigo`)",
			  Convivio.RotuloDeProximidade(eu.PontosDeAmizade(ele)) == "Familiar");

		// O PEDIDO ACEITO E O QUE ATRAVESSA
		eu.AceitarAmizade(ele);
		Checa("o pedido aceito atravessa o teto e faz amigo", eu.EhAmigo(ele),
			  $"{eu.PontosDeAmizade(ele):0.###}");

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
