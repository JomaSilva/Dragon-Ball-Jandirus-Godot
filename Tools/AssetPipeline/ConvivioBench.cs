using System.Reflection;
using System.Text.Json;
using Jandirus.Core.Races;
using Jandirus.Core.Social;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DO CONVIVIO (`convivio`) -- o substrato social da aba People: o CONVIVIO (que o jogo conta) e a
/// DECLARACAO (que o jogador escolhe), e as perguntas que decidem se alguem entra em furia.
///
/// ============================ O QUE ELA COBRE ============================
///   [1] OS NUMEROS: o alcance e a cadencia do DM, a taxa de proximidade (que era da afinidade e hoje
///       rende convivio) e os minutos que saem dela.
///   [2] O TEMPO JUNTO, iterado pelo passo de producao: o passo 9 ainda nao e ponto e o 10 e; 25 minutos
///       pagam o "amigo"; o teto do tempo segura em 200; e o defeito injetado (o tempo nao contar).
///   [3] **CONVIVER NAO FAZ AMIGO** -- o pedido do dono de 2026-10-10: com todo o convivio do mundo e
///       nenhuma declaracao, o jogo nao conclui nada. Com o defeito injetado (a afinidade de volta), conclui.
///   [4] AS PERGUNTAS DA FURIA, contra as NOVE declaracoes, uma por uma. A tabela e escrita a mao aqui de
///       proposito: e a copia independente contra a qual o codigo e conferido.
///   [5] A DECLARACAO: o portao de convivio de cada uma, os nomes, a regua e a ordem da lista.
///   [6] O JOGO NAO MEXE NA DECLARACAO: lutar, morrer e conviver nao a escrevem nem apagam.
///   [7] A FOTOGRAFIA da ficha (a primeira vez cria, o minuto segura a segunda).
///   [8] O SAVE: todo o estado e gravavel, os numeros do enum sao cravados, e o save de ANTES (com as tres
///       listas da afinidade) carrega, migra o tempo vivido pro convivio e para de carregar o campo velho.
///   [9] OS DETALHES (segundo pedido do dono, 2026-10-10): o convivio minimo que os abre, nas duas beiradas e
///       com o defeito injetado; o que se aprende de alguem (e quando); a historia limpa antes de outro ler; e
///       a lista FECHADA do que a ficha guarda -- sem classe, sem poder.
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
		Console.WriteLine("=== BANCADA DO CONVIVIO (o que o jogo conta, o que o jogador declara) ===\n");

		try
		{
			OsNumeros();
			OTempoJunto();
			ConviverNaoFazAmigo();
			AsPerguntasDaFuria();
			ADeclaracao();
			OJogoNaoMexeNaDeclaracao();
			AFotografia();
			OSave();
			OsDetalhes();
		}
		finally
		{
			// os quatro botoes de defeito sao estaticos: nenhum pode sobrar ligado, nem com excecao no meio
			Convivio.AmigoSoPeloConvivioDeTeste = false;
			Convivio.TempoJuntoNaoContaDeTeste = false;
			Convivio.DeclararSemConvivioDeTeste = false;
			Convivio.DetalhesSemConvivioDeTeste = false;
			Console.WriteLine($"\n=== {_ok} OK, {_falhou} FALHA ===");
		}
		return _falhou == 0 ? 0 : 1;
	}

	/// <summary>Uma lista com UMA pessoa conhecida, com este convivio e esta declaracao.</summary>
	private static Convivio Com(string sig, int convivio, Relacao relacao = Relacao.Nenhuma)
	{
		var c = new Convivio();
		c.Fotografar(sig, "Fulano", "Saiyan", "Male", 25, 0);
		c.Ficha(sig)!.Familiaridade = convivio;
		c.Ficha(sig)!.Relacao = relacao;
		return c;
	}

	// =====================================================================
	// 1. OS NUMEROS
	// =====================================================================
	private static void OsNumeros()
	{
		Console.WriteLine("[1] OS NUMEROS -- de onde vem o convivio");

		Checa("FRIEND_RANGE = 6 tiles", Convivio.AlcanceDeConvivioTiles == 6);
		Checa("os 10 ciclos de 0,3 s do `GlobalStats` viraram 3 segundos",
			  Math.Abs(Convivio.SegundosEntreAproximacoes - 3) < 1e-9);
		Checa("FRIEND_RATE = 0,1 por passo, escrito em inteiros: 10 passos por ponto",
			  Convivio.PassosPertoPorPonto == 10);
		Checa("...ou seja um ponto de convivio a cada 30 s ao lado da mesma pessoa",
			  Math.Abs(Convivio.SegundosPertoPorPonto - 30) < 1e-9, $"{Convivio.SegundosPertoPorPonto}");
		Checa("o teto do que so o tempo rende e 200 (o `min(cur + FRIEND_RATE, 200)`)",
			  Convivio.TetoDoConvivioPorTempo == 200);

		// OS MINUTOS QUE O DONO CONHECIA -- derivados, e por isso esta linha muda se alguem mexer na taxa
		Checa("**25 minutos lado a lado pagam o 'amigo'** (o relogio que era da afinidade)",
			  Math.Abs(Convivio.MinutosJuntosPara(Relacao.Bom) - 25) < 1e-9,
			  $"{Convivio.MinutosJuntosPara(Relacao.Bom):0.##} min");
		Checa("...50 minutos o 'melhor amigo', 100 o 'amor'",
			  Math.Abs(Convivio.MinutosJuntosPara(Relacao.MuitoBom) - 50) < 1e-9
			  && Math.Abs(Convivio.MinutosJuntosPara(Relacao.Amor) - 100) < 1e-9);
		Checa("...e o teto do tempo paga exatamente a declaracao mais cara",
			  Convivio.TetoDoConvivioPorTempo == Convivio.FamiliaridadeExigida(Relacao.Amor));

		Checa("conversa: 1% por fala ouvida (`prob(1)`)", Math.Abs(Convivio.ChanceDeFamiliaridadePorFala - 0.01) < 1e-9);
		Checa("luta: 10% por golpe que encosta, 10 por queda, 20 por morte",
			  Math.Abs(Convivio.ChanceDeFamiliaridadePorGolpe - 0.10) < 1e-9
			  && Convivio.FamiliaridadePorQueda == 10 && Convivio.FamiliaridadePorMorte == 20);
		Console.WriteLine();
	}

	// =====================================================================
	// 2. O TEMPO JUNTO
	// =====================================================================
	private static void OTempoJunto()
	{
		Console.WriteLine("[2] FICAR PERTO RENDE CONVIVIO (o passo de producao, iterado)");

		const string ele = "0000000001";
		Convivio eu = Com(ele, 0);

		// NOVE PASSOS E NAO DEZ: o que se mede e o DEGRAU. "Depois de um tempo o convivio subiu" passaria
		// igual num sistema que desse um ponto por passo.
		bool algumPonto = false;
		for (int i = 0; i < 9; i++) algumPonto |= eu.ConviverPerto(ele, out _);
		Checa("nove passos (27 s) ainda nao sao um ponto", !algumPonto && eu.Familiaridade(ele) == 0,
			  $"{eu.Familiaridade(ele)}");
		Checa("**o decimo passo (30 s) rende o ponto, e AVISA (devolve TRUE)**",
			  eu.ConviverPerto(ele, out bool cedo) && eu.Familiaridade(ele) == 1 && !cedo, $"{eu.Familiaridade(ele)}");
		Checa("...e o passo seguinte nao rende outro", !eu.ConviverPerto(ele, out _) && eu.Familiaridade(ele) == 1);

		// ATE A PORTA DO AMIGO: 500 passos de 3 s = 25 minutos (ja foram 11; estes levam ao passo 499)
		int abriu = 0;
		for (int i = 11; i < 499; i++) { eu.ConviverPerto(ele, out bool a); if (a) abriu++; }
		Checa("um passo antes dos 25 minutos o convivio e 49 (ainda nao paga o 'amigo')",
			  eu.Familiaridade(ele) == 49 && abriu == 0, $"{eu.Familiaridade(ele)}, abriu {abriu}x");
		eu.ConviverPerto(ele, out bool noPasso500);
		Checa("**no passo 500 ele chega a 50 e diz que a porta do amigo abriu**",
			  eu.Familiaridade(ele) == 50 && noPasso500, $"{eu.Familiaridade(ele)}");
		for (int i = 0; i < 40; i++) { eu.ConviverPerto(ele, out bool a); if (a) abriu++; }
		Checa("...e so diz UMA vez (os pontos seguintes nao reabrem a porta)", abriu == 0, $"{abriu}");

		// O TETO DO TEMPO
		for (int i = 0; i < 5000; i++) eu.ConviverPerto(ele, out _);
		Checa("o tempo sozinho para em 200 (o preco do 'amor')",
			  eu.Familiaridade(ele) == Convivio.TetoDoConvivioPorTempo, $"{eu.Familiaridade(ele)}");
		Checa("...e no teto o passo nao devolve mais ponto", !eu.ConviverPerto(ele, out _));
		eu.SomarFamiliaridade(ele, Convivio.FamiliaridadePorMorte);
		Checa("...mas a luta soma por cima do teto (como a fala, ela nao tem teto)",
			  eu.Familiaridade(ele) == Convivio.TetoDoConvivioPorTempo + Convivio.FamiliaridadePorMorte);

		// QUEM EU NAO CONHECO NAO RENDE: o servidor fotografa antes, e sem ficha nao ha onde guardar
		var vazio = new Convivio();
		for (int i = 0; i < 30; i++) vazio.ConviverPerto("ninguem", out _);
		Checa("passo de proximidade com quem nao tem ficha nao cria nada",
			  vazio.Conhecidos.Count == 0 && vazio.Familiaridade("ninguem") == 0);

		// OS PASSOS GUARDADOS SAO ESTADO (vao pro save): 5 passos, e outros 5 depois, dao o ponto
		Convivio aosPoucos = Com(ele, 0);
		for (int i = 0; i < 5; i++) aosPoucos.ConviverPerto(ele, out _);
		Checa("os passos ja dados ficam guardados na ficha", aosPoucos.Ficha(ele)!.PassosPerto == 5);
		bool veio = false;
		for (int i = 0; i < 5; i++) veio = aosPoucos.ConviverPerto(ele, out _);
		Checa("...e os que faltavam completam o ponto", veio && aosPoucos.Familiaridade(ele) == 1);

		// O DEFEITO: a afinidade sair sem o tempo ir pra lugar nenhum
		bool TempoRende()
		{
			Convivio c = Com(ele, 0);
			for (int i = 0; i < 500; i++) c.ConviverPerto(ele, out _);
			return c.Familiaridade(ele) == 50;
		}
		Checa("(criterio) 500 passos rendem 50", TempoRende());
		try
		{
			Convivio.TempoJuntoNaoContaDeTeste = true;
			Checa("   (defeito injetado: ficar perto nao rende convivio) o MESMO criterio REPROVA", !TempoRende());
		}
		finally { Convivio.TempoJuntoNaoContaDeTeste = false; }
		Checa("   ...e desfeito o defeito ele volta a passar", TempoRende());
		Console.WriteLine();
	}

	// =====================================================================
	// 3. CONVIVER NAO FAZ AMIGO
	// =====================================================================
	private static void ConviverNaoFazAmigo()
	{
		Console.WriteLine("[3] O JOGO NAO DEDUZ NADA DO CONVIVIO (pedido do dono, 2026-10-10)");

		const string ele = "0000000002";
		Convivio muito = Com(ele, 5000);   // muito alem de qualquer preco

		bool SoADeclaracaoFazAmigo() =>
			!muito.EhAmigo(ele) && !muito.LutoPorMorte(ele) && !muito.LutoPorQueda(ele)
			&& !muito.LutoPorAbsorcao(ele) && muito.AlgozEhInimigo(ele);

		Checa("**com 5000 de convivio e NENHUMA declaracao, a pessoa nao e amiga, nao enfurece e quem "
			  + "apanha dela apanhou de um inimigo**", SoADeclaracaoFazAmigo());
		try
		{
			Convivio.AmigoSoPeloConvivioDeTeste = true;
			Checa("   (defeito injetado: o jogo volta a concluir 'amigo' pelo convivio) o MESMO criterio REPROVA",
				  !SoADeclaracaoFazAmigo());
		}
		finally { Convivio.AmigoSoPeloConvivioDeTeste = false; }
		Checa("   ...e desfeito o defeito ele volta a passar", SoADeclaracaoFazAmigo());

		muito.Declarar(ele, Relacao.Bom);
		Checa("e no instante em que o JOGADOR diz 'amigo', ela passa a ser", muito.EhAmigo(ele) && muito.LutoPorMorte(ele));

		var estranho = new Convivio();
		Checa("um desconhecido morrendo nao enfurece", !estranho.LutoPorMorte(ele) && !estranho.LutoPorQueda(ele));
		Checa("mas quem mata E inimigo aos olhos de quem nao o conhece", estranho.AlgozEhInimigo(ele));
		Console.WriteLine();
	}

	// =====================================================================
	// 4. AS PERGUNTAS DA FURIA
	// =====================================================================
	/// <summary>
	/// ============================ A TABELA E ESCRITA A MAO, E ISSO E O METODO ============================
	/// As listas do DM, cada uma com a metade `|| is_friend` -- que hoje e "declarou amigo, melhor amigo ou
	/// amor" (era "50 pontos de amizade"):
	///
	///   * `Death.dm:79`       -- MORTE:    Good, Very Good, Love, Rival/Good   (|| is_friend)
	///   * `KO.dm:36`          -- QUEDA:    Very Good, Love                     (|| is_friend -> entra o Good)
	///   * `MajinSaga.dm:172`  -- ABSORCAO: Good, Very Good                     (|| is_friend -> entra o Love)
	///   * `Death.dm:75`       -- ALGOZ:    NAO e inimigo se for Very Good, Love, Good, Rival/Good ou amigo
	///
	/// Escrever aqui as nove declaracoes com o resultado esperado, uma por uma, e o que faz uma lista
	/// errada aparecer como divergencia em vez de virar a nova verdade.
	/// ======================================================================================================
	/// </summary>
	private static void AsPerguntasDaFuria()
	{
		Console.WriteLine("[4] AS PERGUNTAS QUE DECIDEM A FURIA, declaracao por declaracao");

		(Relacao Rel, bool Morte, bool Queda, bool Absorcao, bool Inimigo)[] tabela =
		[
			//                        morte  queda  absorcao  algoz-e-inimigo
			(Relacao.Nenhuma,         false, false, false,    true),
			(Relacao.Neutro,          false, false, false,    true),
			(Relacao.Bom,             true,  true,  true,     false),
			(Relacao.MuitoBom,        true,  true,  true,     false),
			(Relacao.Amor,            true,  true,  true,     false),
			(Relacao.RivalBom,        true,  false, false,    false),
			(Relacao.RivalRuim,       false, false, false,    true),
			(Relacao.Ruim,            false, false, false,    true),
			(Relacao.MuitoRuim,       false, false, false,    true),
			(Relacao.Odio,            false, false, false,    true),
		];

		foreach ((Relacao rel, bool morte, bool queda, bool absorcao, bool inimigo) in tabela)
		{
			Convivio c = Com("s", 0, rel);

			Checa($"[{rel}] MORTE enfurece? {morte}", c.LutoPorMorte("s") == morte);
			Checa($"[{rel}] QUEDA enfurece? {queda}", c.LutoPorQueda("s") == queda);
			Checa($"[{rel}] ABSORCAO enfurece? {absorcao}", c.LutoPorAbsorcao("s") == absorcao);
			Checa($"[{rel}] o algoz e inimigo? {inimigo}", c.AlgozEhInimigo("s") == inimigo);

			// A TELA PERGUNTA AS ESTATICAS (por declaracao) e o servidor as de instancia (por pessoa): se as
			// duas discordassem, a aba prometeria uma regra que o jogo nao cumpre.
			Checa($"[{rel}] ...e o que a aba ESCREVE desta escolha e o que o servidor FAZ com ela",
				  Convivio.EnfurecePorMorte(rel) == morte && Convivio.EnfurecePorQueda(rel) == queda
				  && Convivio.EnfurecePorAbsorcao(rel) == absorcao && Convivio.LutaEhTreino(rel) == !inimigo);
		}

		Checa("as tres declaracoes de afeto (amigo, melhor amigo, amor) sao as que fazem AMIGO, e so elas",
			  tabela.All(t => Convivio.DeclaraAmigo(t.Rel) == (t.Rel is Relacao.Bom or Relacao.MuitoBom or Relacao.Amor)));
		Console.WriteLine();
	}

	// =====================================================================
	// 5. A DECLARACAO
	// =====================================================================
	private static void ADeclaracao()
	{
		Console.WriteLine("[5] A DECLARACAO: QUEM ESCOLHE E O JOGADOR, E O CONVIVIO PAGA");

		// OS PRECOS QUE O PROPRIO `input()` DO DM LISTA (`Contacts.dm:117`)
		Checa("neutro nao custa nada", Convivio.FamiliaridadeExigida(Relacao.Neutro) == 0);
		Checa("desafeto (Bad) custa 10", Convivio.FamiliaridadeExigida(Relacao.Ruim) == 10);
		Checa("rival (amigo ou inimigo) custa 15",
			  Convivio.FamiliaridadeExigida(Relacao.RivalBom) == 15
			  && Convivio.FamiliaridadeExigida(Relacao.RivalRuim) == 15);
		Checa("inimigo (Very Bad) custa 20", Convivio.FamiliaridadeExigida(Relacao.MuitoRuim) == 20);
		Checa("amigo (Good) custa 50", Convivio.FamiliaridadeExigida(Relacao.Bom) == 50);
		Checa("inimigo mortal (Hate) custa 50", Convivio.FamiliaridadeExigida(Relacao.Odio) == 50);
		Checa("melhor amigo (Very Good) custa 100", Convivio.FamiliaridadeExigida(Relacao.MuitoBom) == 100);
		Checa("amor (Love) custa 200", Convivio.FamiliaridadeExigida(Relacao.Amor) == 200);
		Checa("`Nenhuma` nao se declara (preco impossivel)",
			  Convivio.FamiliaridadeExigida(Relacao.Nenhuma) == int.MaxValue);

		// O PORTAO, pelo metodo que o verb chama -- nas duas beiradas de cada preco
		bool PortoesFecham() => Convivio.Declaraveis.All(r =>
		{
			int preco = Convivio.FamiliaridadeExigida(r);
			bool abre = Com("s", preco).Declarar("s", r) == Declaracao.Feita;
			bool fecha = preco == 0 || Com("s", preco - 1).Declarar("s", r) == Declaracao.FaltaConvivio;
			return abre && fecha;
		});
		Checa("**cada declaracao passa com o convivio do preco e e RECUSADA com um ponto a menos**", PortoesFecham());
		try
		{
			Convivio.DeclararSemConvivioDeTeste = true;
			Checa("   (defeito injetado: declarar nao cobra convivio) o MESMO criterio REPROVA", !PortoesFecham());
		}
		finally { Convivio.DeclararSemConvivioDeTeste = false; }
		Checa("   ...e desfeito o defeito ele volta a passar", PortoesFecham());

		Convivio recusada = Com("s", 49, Relacao.Neutro);
		recusada.Declarar("s", Relacao.Bom);
		Checa("a declaracao recusada nao escreve nada (continua a que estava)", recusada.RelacaoCom("s") == Relacao.Neutro);
		Checa("declarar pra quem eu nao conheco e recusado (nao ha ficha onde escrever)",
			  new Convivio().Declarar("s", Relacao.Neutro) == Declaracao.NaoConhece);
		Checa("`Nenhuma` nao se declara: o jogador nao volta ao 'nunca disse nada' (a saida e o neutro)",
			  Com("s", 500, Relacao.Bom).Declarar("s", Relacao.Nenhuma) == Declaracao.NaoSeDeclara);

		// AS NOVE OPCOES sao as nove do `input()` -- nem uma a mais, nem uma a menos
		Checa("a aba oferece as 9 declaracoes do original", Convivio.Declaraveis.Length == 9
			  && Convivio.Declaraveis.Distinct().Count() == 9, $"{Convivio.Declaraveis.Length}");
		Checa("...e `Nenhuma` nao esta entre elas", Array.IndexOf(Convivio.Declaraveis, Relacao.Nenhuma) < 0);
		Checa("...na ordem da REGUA: amor numa ponta, inimigo mortal na outra e o neutro no meio",
			  Convivio.Declaraveis[0] == Relacao.Amor && Convivio.Declaraveis[^1] == Relacao.Odio
			  && Convivio.Declaraveis[Convivio.Declaraveis.Length / 2] == Relacao.Neutro);
		Checa("...com as que enfurecem todas de um lado so do neutro",
			  Convivio.Declaraveis.Take(4).All(Convivio.EnfurecePorMorte)
			  && !Convivio.Declaraveis.Skip(4).Any(Convivio.EnfurecePorMorte));

		// OS NOMES
		Checa("toda declaravel tem nome, e todos diferentes",
			  Convivio.Declaraveis.Select(Convivio.NomeDaRelacao).Where(n => n.Length > 0).Distinct().Count() == 9);
		Checa("...e vai e volta pelo nome", Convivio.Declaraveis.All(r => Convivio.RelacaoPorNome(Convivio.NomeDaRelacao(r)) == r),
			  string.Join(",", Convivio.Declaraveis.Select(
				  r => $"{r}->{Convivio.NomeDaRelacao(r)}->{Convivio.RelacaoPorNome(Convivio.NomeDaRelacao(r))}")));
		Checa("os nomes de ANTES ainda casam (macro e bancada antigas mandam 'muito bom', 'odio')",
			  Convivio.RelacaoPorNome("muito bom") == Relacao.MuitoBom && Convivio.RelacaoPorNome("bom") == Relacao.Bom
			  && Convivio.RelacaoPorNome("rival ruim") == Relacao.RivalRuim && Convivio.RelacaoPorNome("odio") == Relacao.Odio
			  && Convivio.RelacaoPorNome("ódio") == Relacao.Odio && Convivio.RelacaoPorNome("muito ruim") == Relacao.MuitoRuim);
		Checa("...e os do DM em ingles tambem",
			  Convivio.RelacaoPorNome("Very Good") == Relacao.MuitoBom
			  && Convivio.RelacaoPorNome("Rival/Good") == Relacao.RivalBom
			  && Convivio.RelacaoPorNome("Love") == Relacao.Amor && Convivio.RelacaoPorNome("Hate") == Relacao.Odio);
		Checa("um nome que nao existe nao vira declaracao nenhuma",
			  Convivio.RelacaoPorNome("ligado") == Relacao.Nenhuma && Convivio.RelacaoPorNome("") == Relacao.Nenhuma);

		// O DEGRAU E A ORDEM DA LISTA
		Checa("o degrau do convivio vai de 1 (so o neutro) a 7 (tudo)",
			  Convivio.Degrau(0) == 1 && Convivio.Degrau(9) == 1 && Convivio.Degrau(10) == 2 && Convivio.Degrau(15) == 3
			  && Convivio.Degrau(20) == 4 && Convivio.Degrau(50) == 5 && Convivio.Degrau(100) == 6
			  && Convivio.Degrau(200) == 7 && Convivio.Degrau(9999) == 7);

		var lista = new Convivio();
		void Por(string sig, string nome, int convivio, Relacao r)
		{
			lista.Fotografar(sig, nome, "?", "?", 0, 0);
			lista.Ficha(sig)!.Familiaridade = convivio;
			lista.Ficha(sig)!.Relacao = r;
		}
		Por("1", "Zeca", 0, Relacao.Nenhuma);
		Por("2", "Ana", 3, Relacao.Neutro);
		Por("3", "Beto", 60, Relacao.Nenhuma);
		Por("4", "Raditz", 55, Relacao.Odio);
		Por("5", "Kuririn", 120, Relacao.Bom);
		Por("6", "Gohan", 300, Relacao.Amor);
		Por("7", "Bia", 61, Relacao.Nenhuma);
		string ordem = string.Join(",", lista.EmOrdem().Select(c => c.Nome));
		Checa("a lista sai: quem eu classifiquei (do amor ao inimigo mortal), depois os neutros por degrau e nome",
			  ordem == "Gohan,Kuririn,Raditz,Beto,Bia,Ana,Zeca", ordem);
		lista.Ficha("7")!.Familiaridade = 99;   // Bia passa Beto em pontos, mas nao em DEGRAU
		Checa("...e um ponto a mais de convivio NAO troca dois cartoes de lugar (so o degrau troca)",
			  string.Join(",", lista.EmOrdem().Select(c => c.Nome)) == ordem);
		Console.WriteLine();
	}

	// =====================================================================
	// 6. O JOGO NAO MEXE NA DECLARACAO
	// =====================================================================
	private static void OJogoNaoMexeNaDeclaracao()
	{
		Console.WriteLine("[6] NADA DO QUE ACONTECE ESCREVE OU APAGA O QUE O JOGADOR DISSE");

		const string ele = "0000000003";
		Convivio eu = Com(ele, 100, Relacao.MuitoBom);

		// tudo o que o servidor faz com um par, em serie: conviver, apanhar, cair, morrer, ser refotografado
		for (int i = 0; i < 100; i++) eu.ConviverPerto(ele, out _);
		eu.SomarFamiliaridade(ele);
		eu.SomarFamiliaridade(ele, Convivio.FamiliaridadePorQueda);
		eu.SomarFamiliaridade(ele, Convivio.FamiliaridadePorMorte);
		eu.Fotografar(ele, "Outro Nome", "Namekian", "?", 99, 10 * Convivio.IntervaloDoRetratoMs);

		Checa("**depois de conviver, apanhar, cair e MORRER nas maos dele, a declaracao e a que eu fiz**",
			  eu.RelacaoCom(ele) == Relacao.MuitoBom, eu.RelacaoCom(ele).ToString());
		Checa("...e o convivio so SUBIU (o jogo so conta; nada o desconta)",
			  eu.Familiaridade(ele) == 100 + 10 + 1 + Convivio.FamiliaridadePorQueda + Convivio.FamiliaridadePorMorte,
			  $"{eu.Familiaridade(ele)}");
		Checa("somar zero ou negativo nao faz nada (nao ha caminho pra descer)",
			  !eu.SomarFamiliaridade(ele, 0) && !eu.SomarFamiliaridade(ele, -50)
			  && eu.Familiaridade(ele) == 100 + 10 + 1 + Convivio.FamiliaridadePorQueda + Convivio.FamiliaridadePorMorte);

		// A PORTA DO AMIGO AVISA UMA VEZ, por qualquer fonte
		Convivio quase = Com(ele, 45);
		Checa("a soma que atravessa os 50 devolve TRUE (e a luta tambem abre a porta)",
			  quase.SomarFamiliaridade(ele, Convivio.FamiliaridadePorQueda));
		Checa("...e a seguinte nao", !quase.SomarFamiliaridade(ele, Convivio.FamiliaridadePorQueda));
		Console.WriteLine();
	}

	// =====================================================================
	// 7. A FOTOGRAFIA
	// =====================================================================
	private static void AFotografia()
	{
		Console.WriteLine("[7] A FICHA E UMA FOTOGRAFIA (`update_my_contact`)");

		var lista = new Convivio();
		Checa("fotografar alguem novo devolve TRUE (primeiro encontro)",
			  lista.Fotografar("s", "Goku", "Saiyan", "Male", 30, 1000));
		Checa("...e fotografar de novo no mesmo minuto devolve FALSE",
			  !lista.Fotografar("s", "Goku", "Saiyan", "Male", 30, 1500));
		Checa("...e nao reescreve a ficha antes da hora",
			  lista.Fotografar("s", "OUTRO NOME", "Namekian", "?", 0, 1500) == false
			  && lista.Ficha("s")!.Nome == "Goku", lista.Ficha("s")!.Nome);
		lista.Fotografar("s", "Goku (velho)", "Saiyan", "Male", 300,
						 1000 + Convivio.IntervaloDoRetratoMs + 1);
		Checa("...e passado o minuto ela e refeita", lista.Ficha("s")!.Idade == 300,
			  $"{lista.Ficha("s")!.Idade}");

		// O CONVIVIO E A DECLARACAO SAO MEUS -- a foto e dele. Refotografar nao pode zerar os dois,
		// senao passar na frente de alguem apagaria anos de convivio.
		lista.SomarFamiliaridade("s");
		lista.Ficha("s")!.Relacao = Relacao.Bom;
		lista.Fotografar("s", "Goku", "Saiyan", "Male", 301,
						 1000 + 3 * Convivio.IntervaloDoRetratoMs);
		Checa("refotografar NAO zera o convivio nem a declaracao",
			  lista.Familiaridade("s") == 1 && lista.RelacaoCom("s") == Relacao.Bom,
			  $"{lista.Familiaridade("s")} / {lista.RelacaoCom("s")}");
		Checa("assinatura vazia (corpo sem dono) nao entra na lista de ninguem",
			  !lista.Fotografar("", "NPC", "?", "?", 0, 0) && lista.Conhecidos.Count == 1);
		Console.WriteLine();
	}

	// =====================================================================
	// 8. O SAVE
	// =====================================================================
	/// <summary>
	/// ============================ O DEFEITO QUE A PRIMEIRA METADE EXISTE PRA PEGAR ============================
	/// O `CharacterSave` e serializado pelo `System.Text.Json` com `IncludeFields = true`. Isso quer
	/// dizer que um campo `readonly`, ou uma propriedade sem `set`, e **ignorado em silencio**: grava
	/// vazio, volta vazio, e nada acusa. Este projeto ja pagou exatamente isso -- as cores de roupa
	/// foram escritas, lidas e usadas por meses sem NUNCA persistir.
	///
	/// A SEGUNDA METADE E O SAVE DE ANTES: a afinidade saiu do jogo (2026-10-10), e os saves de quem ja
	/// jogava trazem `Amizade`, `Inimizade` e `Rivais`. O texto abaixo e um save desses, escrito a mao.
	/// (A ida-e-volta pelo `AccountStore` de verdade, com as opcoes de verdade, esta na bancada do
	/// servidor, `--conhecidosteste`: esta ferramenta nao referencia o projeto do jogo.)
	/// ====================================================================================================
	/// </summary>
	private static void OSave()
	{
		Console.WriteLine("[8] O SAVE: TUDO E GRAVAVEL, E O DE ANTES CONTINUA CARREGANDO");

		foreach (Type t in new[] { typeof(Convivio), typeof(Conhecido) })
		{
			foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
				Checa($"{t.Name}.{f.Name} nao e readonly", !f.IsInitOnly);

			foreach (PropertyInfo pr in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
				Checa($"{t.Name}.{pr.Name} tem `set`", pr.CanWrite, "propriedade so-leitura");

			// E NAO PODE HAVER ESTADO ESCONDIDO: um campo privado nao vai pro JSON, entao ele
			// desapareceria no logout enquanto o resto sobrevive.
			FieldInfo[] privados = [.. t.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)];
			Checa($"{t.Name} nao tem estado privado (invisivel pro save)", privados.Length == 0,
				  string.Join(",", privados.Select(f => f.Name)));
		}

		// O QUE SOBROU NO ESTADO: a lista de fichas, e o campo de leitura do save antigo. Um quarto eixo
		// automatico voltando pra ca (pontos, odio, rivais) reprova aqui antes de chegar na tela.
		string campos = string.Join(",", typeof(Convivio).GetFields(BindingFlags.Public | BindingFlags.Instance)
			.Select(f => f.Name).Order());
		Checa("o estado do convivio e so a lista de conhecidos (+ o campo de leitura do save antigo)",
			  campos == "Amizade,Conhecidos", campos);

		// OS NUMEROS DO ENUM SAO CRAVADOS: renumerar `Relacao` transformaria o "Amor" de todo
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

		// ---------------- O SAVE DE ANTES DE 2026-10-10 ----------------
		// Quatro pessoas: o amigo de muito tempo (120 pontos, 3 de convivio, sem declaracao); o inimigo que o
		// jogo declarou sozinho (-60, rival, 60 de odio, 20 de convivio pela morte); a declarada 'muito bom'
		// (7) com 30 pontos; e um vinculo sem ficha (pontos com quem foi apagado da lista).
		const string antigo = """
			{
			  "Conhecidos": {
			    "1111111111": { "Assinatura": "1111111111", "Nome": "Kuririn", "Raca": "Human", "Classe": "Normal", "Genero": "Male", "Idade": 30, "Familiaridade": 3, "Relacao": 0, "VistoEm": 1000 },
			    "2222222222": { "Assinatura": "2222222222", "Nome": "Raditz", "Raca": "Saiyan", "Classe": "Low-Class", "Genero": "Male", "Idade": 35, "Familiaridade": 20, "Relacao": 0, "VistoEm": 2000 },
			    "3333333333": { "Assinatura": "3333333333", "Nome": "Gohan", "Raca": "Halfbreed", "Classe": "Normal", "Genero": "Male", "Idade": 9, "Familiaridade": 110, "Relacao": 7, "VistoEm": 3000 }
			  },
			  "Amizade": { "1111111111": 120.3, "2222222222": -60, "3333333333": 30, "4444444444": 77 },
			  "Inimizade": { "2222222222": 60 },
			  "Rivais": [ "2222222222" ]
			}
			""";
		var opcoes = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };

		Convivio? lido = null;
		string erro = "";
		try { lido = JsonSerializer.Deserialize<Convivio>(antigo, opcoes); }
		catch (Exception e) { erro = e.Message; }
		Checa("**o save antigo (com Amizade, Inimizade e Rivais) CARREGA sem excecao**", lido != null, erro);
		if (lido == null) { Console.WriteLine(); return; }

		Checa("...com as tres fichas, o convivio e a declaracao que ja estavam la",
			  lido.Conhecidos.Count == 3 && lido.Familiaridade("3333333333") == 110
			  && lido.RelacaoCom("3333333333") == Relacao.MuitoBom);

		bool tinha = lido.MigrarAfinidade();
		Checa("a migracao diz que havia afinidade antiga (e o que faz o servidor avisar o jogador)", tinha);
		Checa("**o tempo vivido vira convivio: os 120 pontos do amigo somam nos 3 que ele tinha**",
			  lido.Familiaridade("1111111111") == 123, $"{lido.Familiaridade("1111111111")}");
		Checa("...e os 30 pontos da declarada somam nos 110 dela",
			  lido.Familiaridade("3333333333") == 140, $"{lido.Familiaridade("3333333333")}");
		Checa("...o NEGATIVO nao vira nada (o 'inimigo' que o jogo declarou nao e convivio)",
			  lido.Familiaridade("2222222222") == 20, $"{lido.Familiaridade("2222222222")}");
		Checa("...e quem tinha pontos SEM ficha nao ganha ficha",
			  !lido.Conhece("4444444444") && lido.Conhecidos.Count == 3);
		Checa("**a migracao NAO declara nada na boca do jogador** (o amigo de 120 pontos continua sem declaracao)",
			  lido.RelacaoCom("1111111111") == Relacao.Nenhuma && !lido.EhAmigo("1111111111")
			  && lido.RelacaoCom("2222222222") == Relacao.Nenhuma);
		Checa("...mas o convivio migrado ja PAGA a declaracao que ele quiser fazer (amigo e melhor amigo)",
			  lido.Declarar("1111111111", Relacao.MuitoBom) == Declaracao.Feita && lido.EhAmigo("1111111111"));
		Checa("migrar de novo nao soma duas vezes", !lido.MigrarAfinidade() && lido.Familiaridade("1111111111") == 123);

		string regravado = JsonSerializer.Serialize(lido, opcoes);
		Checa("**regravado, o save nao carrega mais nenhuma das tres listas velhas**",
			  !regravado.Contains("Amizade") && !regravado.Contains("Inimizade") && !regravado.Contains("Rivais"),
			  regravado.Length > 300 ? regravado[..300] : regravado);
		// A CLASSE DOS CONHECIDOS saiu da ficha junto (ela nunca aparece -- `GameServer.Sigilo.cs`, regra 2): o
		// save antigo a traz em cada pessoa, a carga a ignora e a regravacao nao a escreve mais.
		Checa("**...nem a CLASSE de conhecido nenhum, que o save antigo trazia em cada ficha**",
			  antigo.Contains("\"Classe\": \"Low-Class\"") && !regravado.Contains("Classe") && !regravado.Contains("Low-Class"),
			  regravado.Length > 300 ? regravado[..300] : regravado);
		Convivio? volta = JsonSerializer.Deserialize<Convivio>(regravado, opcoes);
		Checa("...e a ida-e-volta do save NOVO preserva convivio, passos guardados e declaracao",
			  volta != null && volta.Familiaridade("1111111111") == 123
			  && volta.RelacaoCom("1111111111") == Relacao.MuitoBom && volta.Amizade == null,
			  volta == null ? "nulo" : $"{volta.Familiaridade("1111111111")} / {volta.RelacaoCom("1111111111")}");

		// O PERSONAGEM QUE NUNCA TEVE AFINIDADE (novo, ou ja migrado) nao tem o que migrar nem o que avisar
		Checa("um personagem novo nao tem o que migrar", !new Convivio().MigrarAfinidade());
		Console.WriteLine();
	}

	// =====================================================================
	// 9. OS DETALHES
	// =====================================================================
	/// <summary>
	/// ============================ O QUE SO O CORE PODE COBRAR DOS DETALHES ============================
	/// O pedido: *"clicar em pessoas q vc conhece (tem q ter um certo nivel de convivio minimo) vc pode ver mais
	/// detalhes da pessoa, como idade, a backstory dela"*. Quem decide o que SAI e o servidor
	/// (`GameServer.VerboDetalhes`, medido na `--conhecidosteste` pelos bytes do pacote), mas a PERGUNTA que ele
	/// faz mora aqui, e o texto que um jogador escreveu so chega na tela de outro depois de passar pelo
	/// `HistoriaLegivel` -- as duas coisas se medem sem rede nenhuma.
	/// ==================================================================================================
	/// </summary>
	private static void OsDetalhes()
	{
		Console.WriteLine("[9] OS DETALHES DE UM CONHECIDO: O CONVIVIO MINIMO, O QUE SE APRENDE, A HISTORIA LIMPA");

		const string ele = "0000000009";
		int minimo = Convivio.ConvivioParaDetalhes;

		// ---- o numero, e onde ele cai na regua ----
		Checa("o convivio minimo dos detalhes e 20: dez minutos lado a lado, ou uma morte, ou duas quedas",
			  minimo == 20 && Math.Abs(minimo * Convivio.SegundosPertoPorPonto / 60.0 - 10) < 1e-9
			  && minimo == Convivio.FamiliaridadePorMorte && minimo == 2 * Convivio.FamiliaridadePorQueda, $"{minimo}");
		Checa("...acima de zero (ver alguem passar nao abre nada) e ANTES do 'amigo' (le-se a historia pra decidir)",
			  minimo > 0 && minimo < Convivio.FamiliaridadeExigida(Relacao.Bom));

		// ---- o portao, nas duas beiradas ----
		bool OPortaoFecha() =>
			!Com(ele, minimo - 1).PodeVerDetalhes(ele) && Com(ele, minimo - 1).Detalhes(ele) == null
			&& !Com(ele, 0).PodeVerDetalhes(ele)
			&& Com(ele, minimo).PodeVerDetalhes(ele) && Com(ele, minimo).Detalhes(ele) != null;
		Checa($"**com {minimo - 1} de convivio os detalhes estao FECHADOS, e com {minimo} abrem**", OPortaoFecha());
		try
		{
			Convivio.DetalhesSemConvivioDeTeste = true;
			Checa("   (defeito injetado: os detalhes saem com qualquer convivio) o MESMO criterio REPROVA", !OPortaoFecha());
		}
		finally { Convivio.DetalhesSemConvivioDeTeste = false; }
		Checa("   ...e desfeito o defeito ele volta a passar", OPortaoFecha());
		Checa("de quem eu NAO conheco nao ha detalhe nenhum (nao ha ficha), por mais que eu conheca outra pessoa",
			  !new Convivio().PodeVerDetalhes(ele) && new Convivio().Detalhes(ele) == null
			  && !Com(ele, 500).PodeVerDetalhes("outro") && Com(ele, 500).Detalhes("outro") == null);
		Checa("a DECLARACAO nao compra os detalhes: quem eu chamei de 'desafeto' com 10 de convivio continua trancado",
			  !Com(ele, 10, Relacao.Ruim).PodeVerDetalhes(ele));

		// ---- o que se aprende de alguem, e quando ----
		const string contada = "Nasci na Terra e treino desde crianca.";
		Convivio cedo = Com(ele, minimo - 1);
		Checa("abaixo do minimo NADA e aprendido: a historia e o cargo nem entram na ficha",
			  !cedo.AprenderDetalhes(ele, contada, "Turtle Hermit", 5000)
			  && cedo.Ficha(ele)!.Historia == "" && cedo.Ficha(ele)!.Titulo == "" && cedo.Ficha(ele)!.DetalhesEm == 0);

		Convivio eu = Com(ele, minimo);
		Checa("no minimo, a primeira chamada ja guarda (nao espera o minuto da fotografia), e guarda a historia APARADA",
			  eu.AprenderDetalhes(ele, "  " + contada + "  ", "Turtle Hermit", 5000)
			  && eu.Ficha(ele)!.Historia == contada && eu.Ficha(ele)!.Titulo == "Turtle Hermit" && eu.Ficha(ele)!.DetalhesEm == 5000,
			  $"'{eu.Ficha(ele)!.Historia}' / '{eu.Ficha(ele)!.Titulo}' / {eu.Ficha(ele)!.DetalhesEm}");
		Checa("...dentro do mesmo minuto a ficha NAO e reescrita (e fotografia: o que a pessoa era quando a vi)",
			  !eu.AprenderDetalhes(ele, "outra historia inteira", "Outro Cargo", 5000 + Convivio.IntervaloDoRetratoMs - 1)
			  && eu.Ficha(ele)!.Titulo == "Turtle Hermit" && eu.Ficha(ele)!.Historia == contada);
		Checa("...e passado o minuto ela e refeita (a pessoa perdeu o cargo: o que eu sei muda no proximo encontro)",
			  eu.AprenderDetalhes(ele, contada, "", 5000 + Convivio.IntervaloDoRetratoMs)
			  && eu.Ficha(ele)!.Titulo == "" && eu.Ficha(ele)!.DetalhesEm == 5000 + Convivio.IntervaloDoRetratoMs);
		Convivio noZero = Com(ele, minimo);
		noZero.AprenderDetalhes(ele, contada, "", 0);
		Checa("aprender no instante 0 nao deixa o carimbo em zero (zero quer dizer 'nunca ouvi')",
			  noZero.Ficha(ele)!.DetalhesEm > 0, $"{noZero.Ficha(ele)!.DetalhesEm}");
		var ninguem = new Convivio();
		Checa("aprender de quem eu nao conheco nao cria ficha",
			  !ninguem.AprenderDetalhes(ele, contada, "", 1) && ninguem.Conhecidos.Count == 0);

		// ---- desde quando ----
		var lista = new Convivio();
		lista.Fotografar(ele, "Goku", "Saiyan", "Male", 30, 7000);
		lista.Fotografar(ele, "Goku", "Saiyan", "Male", 31, 7000 + 5 * Convivio.IntervaloDoRetratoMs);
		Checa("a ficha guarda DESDE QUANDO eu conheco a pessoa: a hora da primeira foto, que a refotografada nao muda",
			  lista.Ficha(ele)!.DesdeMs == 7000 && lista.Ficha(ele)!.VistoEm == 7000 + 5 * Convivio.IntervaloDoRetratoMs,
			  $"{lista.Ficha(ele)!.DesdeMs} / {lista.Ficha(ele)!.VistoEm}");

		// ---- a historia, do jeito que OUTRO jogador pode ler ----
		int teto = CharacterDraft.BackstoryMax;
		const string comum = "Nasci em Vegeta, filho de um guerreiro de classe baixa.\n\nFui mandado pra Terra ainda bebê \u2014 e esqueci a missão. \u2604";
		Checa("uma historia comum volta IGUAL (acento, travessao, paragrafo e simbolo ficam)",
			  Convivio.HistoriaLegivel(comum) == comum, Escapada(Convivio.HistoriaLegivel(comum)));
		Checa("nula, vazia ou so de espacos: vira vazio",
			  Convivio.HistoriaLegivel(null) == "" && Convivio.HistoriaLegivel("") == "" && Convivio.HistoriaLegivel(" \t\r\n ") == "");
		string semControle = Convivio.HistoriaLegivel("a\0b\ac\u001bd\u007fe\tf");
		Checa("caractere de CONTROLE sai (nulo, campainha, escape, apagar) e a tabulacao vira espaco",
			  semControle == "abcde f", Escapada(semControle));
		string semFormato = Convivio.HistoriaLegivel("abc\u202Edef\u200Bghi\uFEFF");
		Checa("caractere de FORMATO sai (o que inverte a direcao da linha, o espaco de largura zero, a marca de ordem)",
			  semFormato == "abcdefghi", Escapada(semFormato));
		// FORA DO PLANO BASICO um caractere sao dois `char`: o de formato U+E0001 (uma "etiqueta" invisivel) so e
		// pego por quem le o PAR; e a metade solta de um par nao e caractere nenhum.
		string semEtiqueta = Convivio.HistoriaLegivel("ab\U000E0001cd");
		string semMetade = Convivio.HistoriaLegivel("ab\uD83Dcd\uDE00ef");
		Checa("...inclusive o de formato que ocupa DOIS `char`, e a metade solta de um par substituto",
			  semEtiqueta == "abcd" && semMetade == "abcdef", $"'{Escapada(semEtiqueta)}' / '{semMetade.Length} chars'");
		string quebras =Convivio.HistoriaLegivel("um  \r\n  dois\rtres\n quatro ");
		Checa("os tres jeitos de quebrar linha (CR+LF, CR, LF) viram o mesmo, e cada linha e aparada",
			  quebras == "um\ndois\ntres\nquatro", Escapada(quebras));
		string brancas = Convivio.HistoriaLegivel("\n\n\num\n\n\n\n\ndois\n\n\n");
		Checa("linhas em branco seguidas viram UMA, e as do comeco e do fim somem",
			  brancas == "um\n\ndois", Escapada(brancas));
		string escada = Convivio.HistoriaLegivel(string.Join("\n", Enumerable.Range(1, 20).Select(i => "l" + i)));
		Checa($"vinte linhas de uma palavra: so as {Convivio.MaxQuebrasDaHistoria} primeiras quebras ficam, o resto vira espaco "
			  + "(a historia de alguem nao estica o cartao de quem a abre por telas inteiras)",
			  escada.Count(ch => ch == '\n') == Convivio.MaxQuebrasDaHistoria && escada.Contains("l13 l14")
			  && escada.EndsWith("l19 l20", StringComparison.Ordinal),
			  Escapada(escada));

		Checa($"texto maior que o da criacao e CORTADO em {teto}",
			  Convivio.HistoriaLegivel(new string('a', teto + 300)).Length == teto);
		Checa("...e o que cabe exato nao perde nada", Convivio.HistoriaLegivel(new string('b', teto)).Length == teto);
		// O PAR SUBSTITUTO NA BEIRADA: 499 letras + um emoji (dois `char`) -- o corte seco em 500 cairia no meio dele
		string cortada = Convivio.HistoriaLegivel(new string('a', teto - 1) + "\U0001F600" + "resto");
		Checa("...e o corte nao parte um emoji no meio (metade de um par substituto e um caractere invalido)",
			  cortada.Length == teto - 1 && !char.IsSurrogate(cortada[^1]), $"{cortada.Length} chars, ultimo U+{(int)cortada[^1]:X4}");
		string inteira = Convivio.HistoriaLegivel(new string('a', teto - 2) + "\U0001F600" + "resto");
		Checa("...mas o emoji que cabe inteiro antes do corte fica",
			  inteira.Length == teto && char.IsLowSurrogate(inteira[^1]) && char.IsHighSurrogate(inteira[^2]), $"{inteira.Length} chars");

		// LIMPAR DUAS VEZES DA NO MESMO: a historia e limpa ao entrar na ficha E ao sair pro fio, e a segunda
		// passada nao pode mexer no que a primeira deixou (senao o que eu leio mudaria a cada pedido).
		string[] amostras =
		[
			comum, "a\0b\ac\u001bd\u007fe\tf", "abc‮def​ghi﻿", "um  \r\n  dois\rtres\n quatro ",
			"\n\n\num\n\n\n\n\ndois\n\n\n", string.Join("\n", Enumerable.Range(1, 20).Select(i => "l" + i)),
			new string('a', teto - 1) + "\U0001F600" + "resto", new string('a', teto - 3) + "\n\n\n" + new string('b', 40),
			string.Concat(Enumerable.Repeat("palavra \n", 200)),
		];
		string[] mudaram = [.. amostras.Where(a => Convivio.HistoriaLegivel(Convivio.HistoriaLegivel(a)) != Convivio.HistoriaLegivel(a))];
		Checa($"limpar duas vezes da no mesmo, nas {amostras.Length} amostras (a historia e limpa ao entrar na ficha e ao sair pro fio)",
			  mudaram.Length == 0, $"{mudaram.Length} mudaram na segunda passada");
		Checa("...e nenhuma amostra limpa passa do teto nem acaba em espaco",
			  amostras.Select(Convivio.HistoriaLegivel).All(h => h.Length <= teto && h == h.Trim()));

		// ---- a primeira porta e a criacao: ela ja recusa o texto grande demais (a limpeza acima e a ultima) ----
		var rascunho = new CharacterDraft { Name = "Goku", Race = "Human", Age = 18, Backstory = new string('x', teto) };
		string noTeto = rascunho.Validar();
		rascunho.Backstory = new string('x', teto + 1);
		string acima = rascunho.Validar();
		Checa($"a criacao aceita a historia de {teto} caracteres e RECUSA a de {teto + 1}",
			  noTeto == "" && acima.Contains(teto.ToString()), $"'{noTeto}' / '{acima}'");

		// ---- a lista FECHADA do que a ficha de um conhecido guarda ----
		string campos = string.Join(",", typeof(Conhecido).GetFields(BindingFlags.Public | BindingFlags.Instance)
			.Select(f => f.Name).Order(StringComparer.Ordinal));
		Checa("**a ficha de um conhecido guarda SO isto** (nome, raca, genero, idade, cargo, historia, convivio, declaracao e "
			  + "datas) -- nem a CLASSE, nem poder, nem lugar: o que a foto nao guarda nao tem como vazar",
			  campos == "Assinatura,DesdeMs,DetalhesEm,Familiaridade,Genero,Historia,Idade,Nome,PassosPerto,Raca,Relacao,Titulo,VistoEm",
			  campos);

		// ---- e os detalhes atravessam o save; a ficha de antes deles carrega ----
		var opcoes = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
		lista.Ficha(ele)!.Familiaridade = minimo;
		lista.AprenderDetalhes(ele, comum, "Kami", 9000);
		Conhecido? v = JsonSerializer.Deserialize<Convivio>(JsonSerializer.Serialize(lista, opcoes), opcoes)?.Ficha(ele);
		Checa("a historia, o cargo e as duas datas voltam do save",
			  v is { Historia: comum, Titulo: "Kami", DetalhesEm: 9000, DesdeMs: 7000 },
			  v == null ? "nulo" : $"'{v.Titulo}' / {v.DetalhesEm} / {v.DesdeMs} / {v.Historia.Length} chars");

		Convivio? deAntes = JsonSerializer.Deserialize<Convivio>(
			"""{ "Conhecidos": { "1": { "Assinatura": "1", "Nome": "Kuririn", "Raca": "Human", "Classe": "Normal", "Familiaridade": 80, "VistoEm": 1000 } } }""",
			opcoes);
		Checa("a ficha de um save de ANTES (sem historia nem datas) carrega: o convivio dela ja abre os detalhes, "
			  + "e a historia fica 'nunca ouvida' ate o proximo encontro",
			  deAntes?.Ficha("1") is { Historia: "", Titulo: "", DetalhesEm: 0, DesdeMs: 0, Familiaridade: 80 } && deAntes.PodeVerDetalhes("1"));
		Console.WriteLine();
	}

	/// <summary>Um texto com os caracteres invisiveis ESCRITOS (a quebra, o U+202E...), pra linha de falha mostrar o que sobrou.</summary>
	private static string Escapada(string s) =>
		string.Concat(s.Select(ch => ch == '\n' ? "\\n"
			: ch < ' ' || char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.Format ? $"\\u{(int)ch:X4}"
			: ch.ToString()));
}
