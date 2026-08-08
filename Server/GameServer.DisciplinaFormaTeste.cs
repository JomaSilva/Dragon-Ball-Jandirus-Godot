using Jandirus.Core.Forms;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DA MAESTRIA DAS FORMAS DE DISCIPLINA -- roda dentro do `--formasteste`.
///
/// ============================ A REGRA, NA PALAVRA DO DONO ============================
/// *"o ultra instinct e perfected ultra instinct estao ganhando maestria como transformacoes, ambas
/// essas formas sao da skill de ultra instinct, entao usar elas aumenta a maestria na SKILL e nao
/// nelas em si"* -- e depois *"sim pode mexer no ultra ego e destroyer tb e a mesma coisa"*. Quatro
/// formas (`ui_sign`, `ui_perfected`, `destroyer`, `ultra_ego`), duas disciplinas, uma regra.
///
/// ============================ POR QUE A BANCADA DO CORE NAO BASTA ============================
/// A secao [7] do `DisciplinasBench` prova o que se decide sem mundo: que a derivacao e uma so, que
/// cada forma cai na disciplina certa, que o livro recusa as quatro e que o save descarta falando.
/// Nada disso responde a pergunta que o dono faria olhando a tela -- **usar a forma sobe a
/// proficiencia?** --, porque quem sobe alguma coisa e o TIQUE, e o tique nao roda la.
///
/// E ele nao e um tique: sao DOIS, em arquivos diferentes (`TickDaForma` sobe maestria de forma,
/// `TickDasDisciplinas` sobe proficiencia de skill), e a regra vive exatamente na costura entre eles.
/// Um teste que rodasse so um dos dois provaria metade e a metade errada: rodando so o da forma,
/// "a proficiencia nao subiu" seria verdade e nao seria defeito.
///
/// ============================ E POR QUE OS **DOIS SENTIDOS**, SEMPRE ============================
/// "a maestria da forma nao sobe" sozinho passa verde num jogo onde NADA sobe -- tique desligado,
/// corpo em cena, Ki zerado que derrubou a forma no primeiro decimo de segundo. Entao cada bloco aqui
/// cobra as duas metades no MESMO tique, mais uma TERCEIRA que e o controle de que o tique correu de
/// verdade: a energia ATUAL, que drena 0,35/s dentro da forma. Ela e a testemunha independente -- se
/// ela caiu, o corpo estava na forma e o tique passou por ali.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A FICHA LENTA QUE SAIU NO FIO, capturada em <see cref="MandarAtributos"/>. Nula em jogo.
	///
	/// ============================ ELA MEDE O LEITOR QUE MORA NA OUTRA MAQUINA ============================
	/// A aba de formas (tecla P) e um dos dois leitores que passaram a perguntar a proficiencia no lugar
	/// da maestria -- ela escreve "Proficiencia em Ultra Instinto -- x%" nas quatro formas divinas. Esse
	/// leitor roda no CLIENTE, e o unico jeito de ele estar certo e o numero CHEGAR: sem pacote, a barra
	/// congela no valor do login e a aba mostra 0% pra quem esta vivendo dentro do Ultra Instinto -- que
	/// e exatamente o defeito que este rework removeu do servidor, reaparecendo do outro lado do fio.
	///
	/// E ha uma armadilha concreta pra vigiar: a assinatura de deduplicacao do `MandarAtributos` **nao
	/// inclui** `DiscReal`. Quem invalida a assinatura e o `AplicarDisciplina` (`SigAtributos = ""`), que
	/// roda dentro do tique. Alguem que "economize" aquela linha um dia deixaria a proficiencia subir no
	/// servidor e nunca mais sair no fio -- sem erro, sem log, sem nada na tela mudando.
	/// ================================================================================================
	/// </summary>
	internal static List<Protocol.AtributosState>? EscutaDeAtributos;

	/// <summary>
	/// UM TIQUE DE FORMA E DE DISCIPLINA, NA ORDEM DO <see cref="Tick"/> -- forma primeiro
	/// (`GameServer.cs`, o laco do `TickDaForma`), disciplinas depois (o laco do `TickDasDisciplinas`).
	///
	/// A ordem nao e decorativa e nao e minha: as duas mexem em coisas que a outra le (a forma decide o
	/// `emForma` da disciplina; a disciplina reescreve a esquiva e a reducao no corpo). Chamar na ordem
	/// inversa mediria um jogo que nao existe -- e a mesma razao pela qual o
	/// <see cref="TiqueDosRelogiosDeKi"/> existe em vez de a bancada chamar dois dos tres relogios de Ki.
	///
	/// So esta bancada chama isto.
	/// </summary>
	private void UmTiqueDeFormaEDisciplina(ServerPlayer pl, double dt)
	{
		TickDaForma(pl, dt);
		TickDasDisciplinas(pl, dt);
	}

	/// <summary>
	/// TIQUA <paramref name="segundos"/> SEGUNDOS e devolve quanto Ki o PRIMEIRO tique cobrou.
	///
	/// O TANQUE E REPOSTO A CADA TIQUE, e isso e escolha: as quatro formas divinas drenam 5%-6,5% do Ki
	/// maximo POR SEGUNDO, ou seja um tanque cheio a cada ~20 s. Sem repor, os 30 s que estas medidas
	/// pedem terminariam com o `Reverter` por Ki zerado -- e a bancada mediria "a proficiencia parou de
	/// subir" sobre um corpo que ja tinha voltado pra base. O dreno tem bancada propria (ver o bloco "o
	/// Ki no fim derruba a forma" do `RodarBancadaDeFormas`); aqui o assunto e a maestria.
	///
	/// E O KI DO PRIMEIRO TIQUE VOLTA COMO RESULTADO porque ele e o CONTROLE dos blocos em que nada deve
	/// subir: "a proficiencia nao se moveu" e indistinguivel de "o tique nao correu", e o dreno de Ki e
	/// a unica coisa que corre em toda forma, de disciplina ou nao.
	/// </summary>
	private double Tiquear(ServerPlayer pl, double segundos)
	{
		const double dt = 0.1;
		double cobrado = -1;

		for (double t = 0; t < segundos; t += dt)
		{
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			double antes = pl.Ficha.Ki;
			UmTiqueDeFormaEDisciplina(pl, dt);
			if (cobrado < 0) cobrado = antes - pl.Ficha.Ki;
		}
		return cobrado;
	}

	/// <summary>
	/// APAGA AS DUAS DISCIPLINAS. Chamado antes de cada bloco porque as escolas se EXCLUEM: quem
	/// deixasse a anterior aprendida faria o <see cref="DisciplinaAtiva"/> devolver sempre a primeira,
	/// e metade dos blocos abaixo mediria a disciplina errada dizendo o nome da certa.
	/// </summary>
	private static void SoUmaDisciplina(ServerPlayer pl, TipoDeDisciplina? qual, double real)
	{
		foreach (DisciplinaDef d in Disciplinas.Todas)
		{
			EstadoDeDisciplina e = EstadoDe(pl, d.Tipo);
			e.Aprendida = false;
			e.Ligada = false;
			e.Real = 0;
			e.Atual = 0;
		}
		pl.Disciplina = qual;
		if (qual is not { } tipo) return;

		EstadoDeDisciplina est = EstadoDe(pl, tipo);
		est.Aprendida = true;
		est.Real = real;
		est.Atual = real;
	}

	/// <summary>A primeira faixa desta disciplina que concede uma forma.</summary>
	private static Degrau PrimeiraFormaDe(DisciplinaDef def) =>
		Array.Find(def.Degraus, g => g.Forma.Length > 0);

	/// <summary>
	/// A MAESTRIA DAS QUATRO FORMAS DIVINAS, NO CORPO VIVO.
	///
	/// Guarda e repoe tudo o que mexe -- forma, liberadas, estreias, a maestria do `ssj1`, as duas
	/// fichas de disciplina, o Ki, o prazo de cena e a tag de combate --, pelo mesmo motivo dos outros
	/// blocos deste arquivo: o estranho de um bloco nao pode virar o resultado do seguinte. Aqui isso e
	/// especialmente caro porque o bloco APRENDE uma disciplina, e uma disciplina aprendida fecha a
	/// outra pra sempre (`PodeAprender`).
	/// </summary>
	private void AMaestriaDasFormasDeDisciplina(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		string formaAntes = pl.Forma.Atual;
		var liberadasAntes = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasAntes = new HashSet<int>(pl.Forma.EstreiaVista);
		double ssj1Antes = pl.Forma.Maestria.De("ssj1");
		double kiAntes = pl.Ficha.Ki, cenaAntes = pl.CenaSegundos;
		double combateAntes = pl.Combate?.EmCombate ?? 0;
		TipoDeDisciplina? disciplinaAntes = pl.Disciplina;
		(bool, double, double, bool)[] fichasAntes =
		[
			.. Disciplinas.Todas.Select(d => EstadoDe(pl, d.Tipo))
				.Select(e => (e.Aprendida, e.Real, e.Atual, e.Ligada)),
		];

		try
		{
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.CenaSegundos = 0;

			// A PROFICIENCIA SO CRESCE LUTANDO, e isso e do DM (`ui_instinct_tick`: o ganho pede
			// `combatTag || IsInFight`). Sem a tag, TODO bloco abaixo mediria zero de proficiencia e a
			// bancada acusaria o defeito que ela existe pra negar.
			pl.Combate!.EntrarEmCombate();

			// =====================================================================
			// 1. AS QUATRO FORMAS: A PROFICIENCIA SOBE, A MAESTRIA DELAS NAO
			//
			// O laco varre o DADO (as faixas de cada disciplina) e nao uma lista de quatro ids: um
			// degrau novo -- ou uma TERCEIRA disciplina -- entra nestas checagens sozinho. E o mesmo
			// motivo pelo qual a derivacao do Core e por PERTENCER A DISCIPLINA.
			// =====================================================================
			foreach (DisciplinaDef def in Disciplinas.Todas)
			{
				EstadoDeDisciplina est = EstadoDe(pl, def.Tipo);
				EstadoDeDisciplina outra = EstadoDe(pl, Disciplinas.Oposta(def.Tipo));

				foreach (Degrau g in def.Degraus)
				{
					if (g.Forma.Length == 0) continue;

					// A PROFICIENCIA COMECA NA FAIXA QUE CONCEDE A FORMA (nunca abaixo): entrar no
					// Perfected com 40% seria um corpo que o jogo nao produz, e a bancada nao deve
					// medir estados impossiveis.
					SoUmaDisciplina(pl, def.Tipo, Math.Max(40, g.Pct));
					pl.Forma.Entrar(g.Forma);
					AplicarForma(pl);
					AplicarDisciplina(pl);

					double realAntes = est.Real, atualAntes = est.Atual, outraAntes = outra.Real;
					double maestriaAntes = pl.Forma.Maestria.De(g.Forma);
					const double segundos = 30;

					double cobrado = Tiquear(pl, segundos);

					// --- (a) O SENTIDO QUE TEM QUE SUBIR ---
					double subiu = est.Real - realAntes;
					Checa($"{g.Forma}: usar a forma sobe a proficiencia em {def.Nome} "
						+ $"(+{subiu:0.###} em {segundos:0}s)", subiu > 0, $"{realAntes:0.##} -> {est.Real:0.##}");
					Checa($"{g.Forma}: ...e sobe na taxa do DM ({def.RealPorSegundo:0.###}/s)",
						  Math.Abs(subiu - def.RealPorSegundo * segundos) < 1e-6,
						  $"{subiu:0.####} contra {def.RealPorSegundo * segundos:0.####}");

					// --- (b) O SENTIDO QUE **NAO** PODE SUBIR ---
					Checa($"{g.Forma}: a maestria da FORMA nao saiu do lugar",
						  Math.Abs(pl.Forma.Maestria.De(g.Forma) - maestriaAntes) < 1e-12,
						  $"{maestriaAntes:0.####} -> {pl.Forma.Maestria.De(g.Forma):0.####}");
					Checa($"{g.Forma}: ...e ela e ZERO (o livro nunca a guardou)",
						  Math.Abs(pl.Forma.Maestria.De(g.Forma)) < 1e-12, $"{pl.Forma.Maestria.De(g.Forma)}");

					// --- (c) O CONTROLE: O TIQUE CORREU ---
					// Duas testemunhas independentes, e as duas precisam do corpo DENTRO da forma: o
					// Ki (que so o `TickDaForma` cobra) e a energia ATUAL (que so drena `DrenoNaForma`
					// se o `emForma` do `TickDasDisciplinas` for verdade). Sem elas, um tique desligado
					// daria verde na linha (b) -- que e o pior verde possivel, porque e o defeito
					// original disfarcado de conserto.
					Checa($"{g.Forma}: o tique CORREU -- o dreno cobrou {cobrado:0.###} de Ki no "
						+ "primeiro decimo de segundo", cobrado > 0, $"{cobrado}");
					double gastou = atualAntes - est.Atual;
					Checa($"{g.Forma}: ...e o corpo estava DENTRO da forma (a energia caiu "
						+ $"{gastou:0.##}, a {def.DrenoNaForma:0.##}/s)",
						  Math.Abs(gastou - def.DrenoNaForma * segundos) < 1e-6,
						  $"{gastou:0.####} contra {def.DrenoNaForma * segundos:0.####}");

					// --- (d) E A OUTRA ESCOLA NAO SE MEXEU ---
					Checa($"{g.Forma}: a outra disciplina nao ganhou nada",
						  Math.Abs(outra.Real - outraAntes) < 1e-12, $"{outra.Real}");

					// --- (e) O LEITOR RESPONDE A PROFICIENCIA, E NAO ZERO ---
					// Os dois numeros DIFEREM neste instante (proficiencia > 0, livro == 0), e e essa
					// diferenca que faz a linha valer: num corpo onde os dois fossem iguais ela nao
					// distinguiria o leitor novo do antigo.
					Checa($"{g.Forma}: o jogo LE a proficiencia como maestria desta forma "
						+ $"({MaestriaDaForma(pl, g.Forma):0.##}%, e o livro diz "
						+ $"{pl.Forma.Maestria.De(g.Forma):0.##}%)",
						  Math.Abs(MaestriaDaForma(pl, g.Forma) - est.Real) < 1e-12
						  && MaestriaDaForma(pl, g.Forma) > 0, "");
				}
			}

			// =====================================================================
			// 2. CADA UMA CREDITA A **PROPRIA** DISCIPLINA
			//
			// ============================ SEM ESTE BLOCO, UM `+=` NO CAMPO ERRADO PASSA ============================
			// O bloco 1 confere "subiu" na disciplina que o corpo trilhou e "nao subiu" na outra -- mas
			// no bloco 1 a outra nem esta aprendida, entao ela nao subiria de jeito nenhum. Um
			// `FormaDaDisciplina` que perdesse o recorte `par.Def.Tipo == def.Tipo` (uma "simplificacao"
			// muito plausivel: o `DaForma` ja devolve a ficha certa, pra que comparar de novo?) faria
			// **estar na Destroyer creditar o Ultra Instinto** -- e o bloco 1 continuaria todo verde.
			//
			// Aqui o corpo trilha UMA escola e veste a forma da OUTRA. Nada pode subir. E O TOGGLE FICA
			// DESLIGADO de proposito: com ele ligado, `emUso` seria verdade pelo toggle e a proficiencia
			// subiria por um motivo legitimo -- o teste passaria a verde e deixaria de medir a forma.
			// ==================================================================================================
			// =====================================================================
			foreach (DisciplinaDef def in Disciplinas.Todas)
			{
				DisciplinaDef oposta = Disciplinas.Def(Disciplinas.Oposta(def.Tipo))!;
				Degrau formaDaOutra = PrimeiraFormaDe(oposta);

				SoUmaDisciplina(pl, def.Tipo, 40);
				pl.Forma.Entrar(formaDaOutra.Forma);
				AplicarForma(pl);
				AplicarDisciplina(pl);

				EstadoDeDisciplina est = EstadoDe(pl, def.Tipo);
				double realAntes = est.Real, atualAntes = est.Atual;

				double cobrado = Tiquear(pl, 30);

				Checa($"vestir `{formaDaOutra.Forma}` NAO move a proficiencia em {def.Nome}",
					  Math.Abs(est.Real - realAntes) < 1e-12, $"{realAntes:0.####} -> {est.Real:0.####}");
				Checa($"...nem a energia ATUAL (a forma da outra escola nao e 'uso' desta)",
					  Math.Abs(est.Atual - atualAntes) < 1e-12, $"{atualAntes:0.##} -> {est.Atual:0.##}");
				Checa($"...e nem por isso a forma acumulou maestria propria",
					  Math.Abs(pl.Forma.Maestria.De(formaDaOutra.Forma)) < 1e-12, "");
				Checa($"...e o tique CORREU mesmo assim (o dreno cobrou {cobrado:0.###} de Ki)",
					  cobrado > 0, $"{cobrado}");
			}

			// =====================================================================
			// 3. O CONTRA-EXEMPLO: UMA FORMA QUE **NAO** E DE DISCIPLINA
			//
			// Sem ele, "ninguem sobe maestria" passaria verde -- e um jogo em que a maestria de forma
			// parou de funcionar pra TODAS as 34 formas leria exatamente como este rework.
			// =====================================================================
			{
				SoUmaDisciplina(pl, TipoDeDisciplina.UltraInstinct, 40);
				pl.Forma.Maestria.Por("ssj1", 0);
				pl.Forma.Entrar("ssj1");
				AplicarForma(pl);
				AplicarDisciplina(pl);

				EstadoDeDisciplina ui = pl.UltraInstinct;
				double realAntes = ui.Real;
				const double segundos = 30;

				Tiquear(pl, segundos);

				double m = pl.Forma.Maestria.De("ssj1");
				Checa($"`ssj1` CONTINUA subindo a maestria dela ({m:0.####}% em {segundos:0}s)", m > 0,
					  "a maestria de forma parou de funcionar pro jogo inteiro");
				Checa("...e na taxa do catalogo",
					  Math.Abs(m - Catalogo.MaestriaPorSegundo * segundos) < 1e-9,
					  $"{m:0.######} contra {Catalogo.MaestriaPorSegundo * segundos:0.######}");
				Checa("...e ela NAO virou proficiencia de disciplina (a forma nao e de nenhuma)",
					  Math.Abs(ui.Real - realAntes) < 1e-12, $"{realAntes:0.####} -> {ui.Real:0.####}");
				Checa("...e o leitor devolve o LIVRO pra ela, e nao a proficiencia",
					  Math.Abs(MaestriaDaForma(pl, "ssj1") - m) < 1e-12
					  && Math.Abs(MaestriaDaForma(pl, "ssj1") - ui.Real) > 1e-9,
					  $"leu {MaestriaDaForma(pl, "ssj1"):0.####}");

				// E O TOGGLE, FORA DE FORMA, CONTINUA PAGANDO. E o controle da linha "nao virou
				// proficiencia": sem ele, aquele zero poderia ser um tique de disciplina quebrado em vez
				// da regra funcionando. O `ui_active` do DM (`UltraInstinct.dm:209`) e esta metade.
				ui.Ligada = true;
				double antesDoToggle = ui.Real;
				Tiquear(pl, 10);
				Checa("o TOGGLE ligado sobe a proficiencia mesmo fora de forma (o tique nao esta quebrado)",
					  ui.Real > antesDoToggle, $"{antesDoToggle:0.####} -> {ui.Real:0.####}");
				ui.Ligada = false;
			}

			// =====================================================================
			// 4. OS LEITORES, PELO CAMINHO DE PRODUCAO
			// =====================================================================
			OsLeitoresDaProficiencia(pl, Checa);
		}
		finally
		{
			for (int i = 0; i < Disciplinas.Todas.Length; i++)
			{
				EstadoDeDisciplina e = EstadoDe(pl, Disciplinas.Todas[i].Tipo);
				(e.Aprendida, e.Real, e.Atual, e.Ligada) = fichasAntes[i];
			}
			pl.Disciplina = disciplinaAntes;
			pl.Forma.Maestria.Por("ssj1", ssj1Antes);
			pl.Forma.Entrar(formaAntes);
			pl.Forma.Liberadas.Clear();
			pl.Forma.Liberadas.UnionWith(liberadasAntes);
			pl.Forma.EstreiaVista.Clear();
			pl.Forma.EstreiaVista.UnionWith(estreiasAntes);
			pl.Ficha.Ki = kiAntes;
			pl.CenaSegundos = cenaAntes;
			if (pl.Combate != null) pl.Combate.EmCombate = combateAntes;
			EscutaDeAnuncios = null;
			EscutaDeAtributos = null;
			AplicarForma(pl);
			AplicarDisciplina(pl);
		}
	}

	/// <summary>
	/// OS DOIS LEITORES QUE PASSARAM A PERGUNTAR A PROFICIENCIA -- pelo caminho de producao, nao pela
	/// funcao pura.
	///
	/// ============================ QUAIS SAO, E O QUE ACONTECE SE ELES LEREM ZERO ============================
	/// Desde que o livro recusou as quatro formas, quem lesse `Maestria.De("ui_sign")` leria ZERO pra
	/// sempre. Dos oito leitores que o levantamento achou, seis sao inertes (multiplicador e dreno saem
	/// de tabelas de um elemento; nome e cabelo so olham o `ssj1`; nenhuma entrada do catalogo pede
	/// maestria destas). Sobraram dois, e os dois doem:
	///
	///   1. **A CINEMATICA.** `Cinematicas.Degrau` dispensa a cena a partir de 50% de maestria. Lendo
	///      zero, TODA transformacao de Ultra Instinto fora da estreia ficaria presa nos 8,8 s da cena
	///      curta ETERNAMENTE -- e o jogador nao teria como se livrar, porque o numero que a dispensa
	///      nunca mais subiria. E aqui isso e medido no funil de verdade (`AnunciarForma`), lendo o
	///      degrau que saiu no fio e o prazo que o servidor anotou no corpo.
	///   2. **A ABA DE FORMAS**, que mora no CLIENTE. O que o servidor pode provar dela e a metade que
	///      e dele: que o numero SAI no fio quando muda. Ver <see cref="EscutaDeAtributos"/>.
	///
	/// O CONTRA-FATO E A METADE QUE DA SENTIDO A ISTO: em cada par, a mesma pergunta feita ao LIVRO
	/// (que e o leitor antigo) tem que dar a resposta ERRADA. Sem essa linha, "o degrau saiu Nenhuma"
	/// nao distingue o leitor novo de um jogo que dispensa cena pra todo mundo.
	/// ====================================================================================================
	/// </summary>
	private void OsLeitoresDaProficiencia(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		DisciplinaDef def = Disciplinas.UltraInstinct;
		string forma = PrimeiraFormaDe(def).Forma;

		// --- (a) A CINEMATICA, PELO `AnunciarForma` ---
		// ABAIXO DOS 50%: cena curta, e o corpo fica preso nela.
		SoUmaDisciplina(pl, def.Tipo, Cinematicas.MaestriaQueDispensaCena - 1);
		pl.Forma.Entrar(forma);
		pl.Forma.EstreiaVista.Add(Catalogo.Rede(forma));   // a estreia ja foi paga: aqui o assunto e a maestria
		pl.CenaSegundos = 0;
		EscutaDeAnuncios = [];
		AnunciarForma(pl, Catalogo.IdBase, forma, estreia: false);

		DegrauDeCena curto = EscutaDeAnuncios.Count > 0 ? EscutaDeAnuncios[^1].Degrau : DegrauDeCena.Estreia;
		Checa($"com {pl.UltraInstinct.Real:0}% de proficiencia a transformacao ainda tem cena "
			+ $"({curto})", curto == DegrauDeCena.Curta, curto.ToString());
		Checa("...e o servidor prendeu o corpo por ela", pl.CenaSegundos > 0, $"{pl.CenaSegundos:0.#}s");

		// ACIMA DOS 50%: sem cena, e sem corpo preso. O livro continua em zero -- e por isso este
		// `Nenhuma` so pode ter vindo da proficiencia.
		SoUmaDisciplina(pl, def.Tipo, Cinematicas.MaestriaQueDispensaCena + 10);
		pl.CenaSegundos = 0;
		EscutaDeAnuncios.Clear();
		AnunciarForma(pl, Catalogo.IdBase, forma, estreia: false);

		DegrauDeCena livre = EscutaDeAnuncios.Count > 0 ? EscutaDeAnuncios[^1].Degrau : DegrauDeCena.Estreia;
		Checa($"com {pl.UltraInstinct.Real:0}% de proficiencia a cena e DISPENSADA ({livre})",
			  livre == DegrauDeCena.Nenhuma, livre.ToString());
		Checa("...e o corpo nao fica preso em nada", Math.Abs(pl.CenaSegundos) < 1e-9, $"{pl.CenaSegundos}");
		Checa("...e o LIVRO desta forma continua em zero (o `Nenhuma` veio da proficiencia)",
			  Math.Abs(pl.Forma.Maestria.De(forma)) < 1e-12, "");
		Checa("[contra-fato] lendo o LIVRO, a mesma transformacao voltaria pra cena curta",
			  Cinematicas.Degrau(Catalogo.Def(forma), false, pl.Forma.Maestria.De(forma))
				  == DegrauDeCena.Curta, "");

		// --- (b) O BIT DE **DOMINADA** ---
		SoUmaDisciplina(pl, def.Tipo, 100);
		Checa("a 100% de proficiencia o jogo considera a forma DOMINADA",
			  Dominou(pl, forma) && Math.Abs(pl.Forma.Maestria.De(forma)) < 1e-12, "");
		SoUmaDisciplina(pl, def.Tipo, 99);
		Checa("...e a 99% nao (o teto DISPARA, nao e sempre-verdade)", !Dominou(pl, forma), "");

		// --- (c) O NUMERO SAI NO FIO ---
		// ============================ E ELE SAI PORQUE O TIQUE INVALIDA A ASSINATURA ============================
		// A assinatura de deduplicacao do `MandarAtributos` nao inclui `DiscReal`: o pacote so voltaria a
		// sair quando algum ATRIBUTO mudasse -- ou seja, minutos depois, ou nunca. Quem salva isso e o
		// `AplicarDisciplina` zerando `SigAtributos` dentro do tique. As tres linhas abaixo medem
		// exatamente essa costura: dois envios sem tique nao repetem o pacote (a deduplicacao funciona),
		// e UM tique faz o numero novo sair.
		// ====================================================================================================
		SoUmaDisciplina(pl, def.Tipo, 42);
		pl.Forma.Entrar(forma);
		pl.CenaSegundos = 0;
		AplicarDisciplina(pl);

		EscutaDeAtributos = [];
		MandarAtributos(pl);
		Checa($"a ficha lenta sai no fio com a proficiencia ({EscutaDeAtributos.Count} pacote(s))",
			  EscutaDeAtributos.Count == 1
			  && Math.Abs(EscutaDeAtributos[^1].DiscReal - 42) < 0.01
			  && EscutaDeAtributos[^1].Disciplina == Disciplinas.Rede(def.Tipo),
			  EscutaDeAtributos.Count > 0
				  ? $"disc {EscutaDeAtributos[^1].Disciplina}, real {EscutaDeAtributos[^1].DiscReal:0.##}"
				  : "nenhum pacote");

		EscutaDeAtributos.Clear();
		MandarAtributos(pl);
		Checa("...e nao sai de novo sem nada mudar (a deduplicacao funciona)",
			  EscutaDeAtributos.Count == 0, $"{EscutaDeAtributos.Count}");

		pl.Combate!.EntrarEmCombate();
		EscutaDeAtributos.Clear();
		Tiquear(pl, 1.0);
		MandarAtributos(pl);
		Checa("...e depois de UM segundo de tique o numero NOVO sai (senao a aba congela em 42%)",
			  EscutaDeAtributos.Count == 1 && EscutaDeAtributos[^1].DiscReal > 42,
			  EscutaDeAtributos.Count > 0 ? $"{EscutaDeAtributos[^1].DiscReal:0.####}" : "nenhum pacote");

		EscutaDeAnuncios = null;
		EscutaDeAtributos = null;
	}
}
