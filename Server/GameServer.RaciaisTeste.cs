using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;

namespace Jandirus.Server;

/// <summary>
/// AS ESCADAS DE SANGUE, UMA RACA POR VEZ, PELA TECLA C -- secao da bancada `--formasteste`.
///
/// ============================ POR QUE ELA EXISTE, DEPOIS DE TUDO O QUE JA HAVIA ============================
/// O catalogo ja e varrido por conjunto em tres lugares (`formas` [19], `raiva` [4] e `--frostteste`),
/// e os tres perguntam a mesma coisa de jeitos diferentes: **"o `Avaliar` desta forma responde certo?"**.
/// Nenhum deles aperta a tecla C.
///
/// A diferenca nao e de rigor, e de CAMADA -- e este projeto ja pagou por ela duas vezes nesta mesma
/// area. O `LinhasAbertas` entregava a escada Saiyajin a QUALQUER raca durante meses com o `Avaliar`
/// perfeitamente testado; a punicao do Oozaru dizia o que aconteceu e nao dizia a saida, e nenhuma
/// bancada de Core teria como ver. O que so daqui se ve:
///
///   1. **O CORPO INTEIRO.** `Transformar` -> `AplicarForma` -> `powerlevel()`. Um multiplicador certo
///      que nao chega no `expressedBP` e invisivel no Core, onde `Multiplicador` e tudo o que existe.
///   2. **A BOCA DO JOGO.** As recusas viram FRASE no `PorQueNao`, e a frase e o unico jeito de o
///      jogador saber o que falta. Porta certa com frase errada manda a pessoa treinar a coisa errada
///      por horas -- foi literalmente o caso do SSJ4 e do Oozaru Dourado.
///   3. **O REPOUSO.** `Catalogo.IdDoPiso` so vira jogo quando alguem escreve `Forma.Atual`.
///   4. **O `races.json` INTEIRO.** So o servidor tem o catalogo de racas carregado, e e ele que
///      permite fazer a pergunta que nenhuma bancada fazia: *"existe raca que o resto do jogo trata
///      como transformavel e que a escada esqueceu?"* -- foi ela que achou o meio-Saiyajin.
///
/// ============================ ARQUIVO PROPRIO, MESMA FLAG, MESMO PLACAR ============================
/// Como a `RaivaTeste`, a `ConvivioTeste` e a `DisciplinaFormaTeste`, esta secao **troca a raca, a
/// classe, o livro de skills e a forma** de um personagem vivo, uma vez por linha de sangue. O
/// `finally` proprio e o que impede o estranho de um corpo virar o resultado do proximo. Nao ha
/// bancada irma: e `--formasteste`, e o `Checa` e o mesmo.
/// ==============================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// UM CORPO E A ESCADA QUE ELE VESTE.
	///
	/// A tabela existe pro que NAO da pra derivar: o `LinhasAbertas` sabe dizer que linha uma raca
	/// tem, mas nao com que CLASSE a criacao a produz nem que SKILL escreve a flag da forma. O que da
	/// pra derivar continua derivado -- a secao 1 confere esta tabela contra o `races.json` inteiro e
	/// reprova se uma raca com escada ficar de fora dela. Uma linha racial nova, portanto, **entra
	/// sozinha na bancada**: ela nasce vermelha ate ganhar sua linha aqui.
	/// </summary>
	/// <param name="Skill">Path da skill que escreve a flag da linha. Vazio = a linha nao se compra.</param>
	private readonly record struct EscadaRacial(string Raca, string Classe, LinhaDeForma Linha, string Skill);

	private static readonly EscadaRacial[] EscadasRaciais =
	[
		// O SAIYAJIN E O CONTROLE POSITIVO. Sem ele, um `LinhasAbertas` que tivesse parado de abrir
		// tudo passaria em todas as recusas cruzadas da secao 3 -- "ninguem tem escada nenhuma" e o
		// jeito mais barato de nunca subir a escada de outra raca.
		new("Saiyan", "Normal", LinhaDeForma.Saiyajin, ""),

		// O MEIO-SAIYAJIN NAO E SIMETRIA: e a raca que a secao 1 pegou SEM escada nenhuma, e a unica
		// do jogo cuja linha e a de outra raca (o `FormaDef.MultDiluido` existe so pra ele).
		new(Catalogo.RacaMeioSaiyajin, "New Generation", LinhaDeForma.Saiyajin, ""),

		new(FormasDeFrost.Raca, FormasDeFrost.ClasseNormal, LinhaDeForma.FrostDemon, ""),

		// AS DUAS QUE SE COMPRAM -- e elas chegam a mesma flag por caminhos DIFERENTES (secao 2).
		new(Catalogo.RacaNamekuseijin, "Warrior clan", LinhaDeForma.Namekuseijin,
			"/datum/skill/namek/SuperNamek"),
		new(Catalogo.RacaAlien, "", LinhaDeForma.Alien, "/datum/skill/alien/transformation"),

		// O HERAN E A UNICA LINHA RACIAL QUE PEDE RAIVA (`heran.dm:20-52`). A classe importa aqui
		// mais do que em qualquer outra: o multiplicador dele SAI dela (`FormaDef.BaseDaClasse`).
		new(Catalogo.RacaHeran, "Omega", LinhaDeForma.Heran, ""),
	];

	/// <summary>BP de sobra pra qualquer porta do catalogo -- a maior e a do `frost7`, 15 bilhoes.</summary>
	private const double BpDeSobraRacial = 1e13;

	// =====================================================================
	// O PONTO DE ENTRADA
	// =====================================================================
	private void AsEscadasRaciaisAoVivo(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		GD.Print("[raciais] -- AS ESCADAS DE SANGUE, PELA TECLA C");

		// GUARDA TUDO O QUE ESTA SECAO TROCA. Ela roda por ultimo justamente porque mexe em raca; o
		// `finally` existe pra que quem vier depois (hoje ninguem, amanha alguem) nao herde um corpo
		// de Frost Demon com skill de Namekuseijin no livro.
		string racaAntes = pl.Race, classeAntes = pl.Ficha.Class, classeJogadorAntes = pl.Class;
		EstadoDeForma formaAntes = pl.Forma;
		double bpAntes = pl.Ficha.BP;
		long extremaAntes = pl.FuriaExtremaAte, lendariaAntes = pl.RaivaLendariaAte;
		List<string> corposAntes = [.. pl.Visual.FormasDeFrost];
		var skillsDadas = new List<string>();

		try
		{
			OMapaDasRacas(Checa);

			foreach (EscadaRacial e in EscadasRaciais)
			{
				CadaCorpoSobeAEscadaDele(pl, e, Checa, skillsDadas);
				ENaoSobeADosOutros(pl, e, Checa);
				AsPortasPelaBocaDoJogo(pl, e, Checa, skillsDadas);
			}

			ARacaSemEscada(pl, Checa);
			ASupressaoNaoPEGOUNinguem(pl, Checa);
		}
		finally
		{
			foreach (string path in skillsDadas) pl.Livro.Esquecer(path);
			pl.Race = racaAntes;
			pl.Ficha.Race = racaAntes;
			pl.Ficha.Class = classeAntes;
			pl.Class = classeJogadorAntes;
			if (pl.Ficha.Genoma != null) pl.Ficha.Genoma.Class = classeAntes;
			pl.Visual.FormasDeFrost = corposAntes;
			pl.Forma = formaAntes;
			pl.Ficha.BP = bpAntes;
			pl.FuriaExtremaAte = extremaAntes;
			pl.RaivaLendariaAte = lendariaAntes;
			pl.Ficha.Statify();
			ProjetarRaiva(pl);
			AplicarEfeitos(pl);
			AplicarForma(pl);
			EscutaDeAvisos = null;
		}
	}

	// =====================================================================
	// 1. O MAPA DE RACAS -- e ele sai do `races.json`, nao desta bancada
	// =====================================================================
	/// <summary>
	/// ============================ A PERGUNTA QUE NENHUMA BANCADA FAZIA ============================
	/// Todas as varreduras de forma deste projeto comecam no CATALOGO e perguntam "de quem e esta
	/// forma?". Nenhuma comecava nas RACAS e perguntava "esta raca tem forma?" -- e por isso o buraco
	/// que elas nao viam nao era uma forma sobrando, era uma raca faltando.
	///
	/// ============================ E O AVISO VEM DE UMA SEGUNDA AUTORIDADE ============================
	/// Se a bancada perguntasse ao proprio `LinhasAbertas` quem deveria ter escada, ela concordaria
	/// com ele por construcao -- e concordancia consigo mesmo e o que uma bancada nunca mede.
	///
	/// A segunda autoridade e o **NASCIMENTO**: `LimiaresPessoais.Rolar` sorteia o limiar pessoal de
	/// transformacao de um personagem no instante em que ele nasce, e faz isso por raca, num arquivo
	/// que nao conhece o `LinhasAbertas`. Sortear o `ssjat` de alguem e dizer, em codigo, *"este corpo
	/// vai virar Super Saiyajin em algum ponto"*. Se o catalogo de linhas discorda, um dos dois esta
	/// errado -- e nao ha jeito de os dois estarem certos.
	///
	/// **ELA PEGOU O MEIO-SAIYAJIN.** O `Catalogo.EhSaiyajin` era `raca.Contains("Saiyan")`, e a raca
	/// do `races.json` chama-se `"Halfbreed"` -- que nao contem "Saiyan". Quando o `else` do
	/// `LinhasAbertas` deixou de dar a escada a todo mundo, o meio-Saiyajin ficou **sem escada
	/// nenhuma e sem Oozaru**, calado, enquanto o resto do jogo continuava tratando-o como Saiyajin
	/// (`GameServer.Combat.TemRabo`, `LimiaresPessoais.Rolar`, `Fighter.Training`, a lista de classes
	/// da criacao e ate o `TemEscada`, cujo comentario diz *"era `pl.Race is "Saiyan" or
	/// "Halfbreed"`"*). O conserto foi no `EhSaiyajin`; esta secao e o que o mantem consertado.
	/// ==========================================================================================
	/// </summary>
	private void OMapaDasRacas(Action<string, bool, string> Checa)
	{
		if (_racas == null) { Checa("o catalogo de racas carregou", false, "sem races.json"); return; }

		var linhasDeRaca = new HashSet<LinhaDeForma>();

		foreach (string raca in _racas.Protos.Keys.OrderBy(x => x, StringComparer.Ordinal))
		{
			// AS CLASSES QUE ESTE CORPO PODE TER, das DUAS fontes que o jogo usa: o sorteio do
			// `races.json` (`Class_Spread`) e a escolha da criacao. A cadeia vazia entra porque um save
			// antigo pode nao ter classe, e a escada nao pode depender disso.
			var classes = new List<string> { "" };
			classes.AddRange(_racas.Protos[raca].ClassSpread.Select(c => c.Classe));
			classes.AddRange(CharacterDraft.EscolhasDeClasse(raca));

			foreach (string classe in classes.Distinct(StringComparer.Ordinal))
			{
				var perfil = new PerfilDeFormas(Raca: raca, Classe: classe);
				HashSet<LinhaDeForma> abertas = Catalogo.LinhasAbertas(perfil);

				// O OOZARU SAI DA CONTA: ele nao e escada (`NaoSeSobePraEla`), ele ACONTECE por olhar a
				// lua. Uma raca que so tivesse ele continuaria sem ter pra onde apertar C.
				bool temEscada = abertas.Any(l => Catalogo.DaLinha(l).Any(d => !Catalogo.NaoSeSobePraEla(d)));

				// ============================ "SORTEOU" E **DIFERIR DA FABRICA**, E NAO `> 0` ============================
				// Primeira escrita desta linha: `lim.Porta(d) > 0`. Ela acusou as 24 racas de uma vez, e
				// estava errada -- os campos do `LimiaresPessoais` **nascem com o valor de fabrica** e nao
				// com zero, de proposito (o comentario deles diz: *"assim um save sem este bloco, ou uma
				// raca que nao rola nada, se comporta como o jogo se comportava antes desta classe
				// existir"*). Perguntar `> 0` e perguntar se a constante e positiva.
				//
				// A pergunta certa e se o `Rolar` MEXEU. E ela e feita com TRES sementes porque uma so
				// pode empatar com a fabrica por coincidencia -- o `Faixa("ssjat", 10, 12)` inclui o
				// 1,0 exato, e uma bancada que dependesse disso passaria a acusar ou a inocentar quando
				// alguem trocasse o gerador, sem que nada de verdade tivesse mudado.
				// =================================================================================================
				var fabrica = new LimiaresPessoais();
				string[] sorteadas = [.. Catalogo.Todas
					.Where(d => d.ChaveDoLimiar.Length > 0
								&& Enumerable.Range(1, 3).Any(s => Math.Abs(
									LimiaresPessoais.Rolar(raca, classe,
										LimiaresPessoais.SementeDe(raca + "/" + classe, s)).Porta(d)
									- fabrica.Porta(d)) > 1e-9))
					.Select(d => d.Id)];

				if (sorteadas.Length > 0)
					Checa($"{raca}/{(classe.Length > 0 ? classe : "sem classe")}: o NASCIMENTO sorteia "
						  + $"limiar de transformacao ({sorteadas.Length}), entao ha escada",
						  temEscada,
						  $"limiares de {string.Join(", ", sorteadas)} e nenhuma linha aberta");

				// SO O QUE A RACA ABRE SOZINHA entra no mapa: a classe pode abrir Legendary/Futuro e o
				// ki divino abre as divinas, e nenhuma das duas coisas e uma escada DE RACA.
				if (classe.Length == 0)
					foreach (LinhaDeForma l in abertas)
						if (Catalogo.DaLinha(l).Any(d => !Catalogo.NaoSeSobePraEla(d)))
							linhasDeRaca.Add(l);
			}
		}

		GD.Print($"[raciais] linhas abertas SO pela raca: {string.Join(", ", linhasDeRaca.OrderBy(x => x.ToString()))}");

		// ============================ E A TABELA DESTA BANCADA TEM QUE COBRIR TODAS ============================
		// E o que faz uma linha racial NOVA entrar sozinha: acrescentar uma entrada ao catalogo e abrir
		// a linha dela no `LinhasAbertas` deixa esta checagem VERMELHA ate alguem dizer com que corpo,
		// que classe e que skill ela se sobe -- que sao exatamente as tres coisas que nao se derivam.
		foreach (LinhaDeForma l in linhasDeRaca)
			Checa($"a linha {l} tem corpo nesta bancada",
				  EscadasRaciais.Any(e => e.Linha == l),
				  "nenhuma linha da tabela EscadasRaciais a veste");

		// O AVESSO: nenhum corpo da tabela veste uma linha que a raca dele nao abre. Sem isto, uma
		// linha da tabela apontando pra escada errada faria a secao 2 medir a escada de outro.
		foreach (EscadaRacial e in EscadasRaciais)
			Checa($"{e.Raca}/{e.Classe}: a raca abre mesmo a linha {e.Linha}",
				  Catalogo.LinhasAbertas(new PerfilDeFormas(Raca: e.Raca, Classe: e.Classe)).Contains(e.Linha),
				  string.Join(", ", Catalogo.LinhasAbertas(new PerfilDeFormas(Raca: e.Raca, Classe: e.Classe))));
	}

	// =====================================================================
	// 2. CADA CORPO SOBE A ESCADA DELE
	// =====================================================================
	private void CadaCorpoSobeAEscadaDele(ServerPlayer pl, EscadaRacial e,
										  Action<string, bool, string> Checa, List<string> skillsDadas)
	{
		VestirCorpoRacial(pl, e);
		PagarAFlagDaLinha(pl, e, Checa, skillsDadas);
		AcenderARaivaQueALinhaPede(pl, e);

		Medir(pl);
		double bpNoPiso = pl.Ficha.expressedBP;
		string piso = Catalogo.IdDoPiso(Perfil(pl));

		Checa($"{e.Raca}: descansa em `{piso}` (o piso da escada dele)",
			  pl.Forma.Atual == piso, pl.Forma.Atual);
		Checa($"{e.Raca}: BP expresso positivo antes de qualquer forma", bpNoPiso > 0, $"{bpNoPiso}");

		var visitadas = new List<string>();
		for (int i = 0; i < 12; i++)
		{
			string antes = pl.Forma.Atual;
			pl.Ficha.Ki = pl.Ficha.MaxKi;    // o dreno tem bancada propria; aqui o assunto e a escada
			Transformar(pl, subir: true);
			if (pl.Forma.Atual == antes) break;

			visitadas.Add(pl.Forma.Atual);
			Medir(pl);

			FormaDef d = pl.Forma.Def!;
			double esperado = Catalogo.Multiplicador(d.Id, pl.Forma.Maestria, Perfil(pl),
													 pl.Forma.CombateSegundos);

			Checa($"{e.Raca}/{d.Nome}: ssjBuff = {esperado:0.###}",
				  Math.Abs(pl.Ficha.ssjBuff - esperado) < 1e-6, $"ssjBuff {pl.Ficha.ssjBuff:0.####}");

			// ============================ O BP EXPRESSO ACOMPANHA, PRA CIMA **OU PRA BAIXO** ============================
			// A checagem obvia ("o BP subiu") e a que a `--diagadmin` teve que consertar depois do Frost
			// Demon: exigir que TODA forma levante o poder e exigir que uma supressao nao funcione. A
			// pergunta certa e se o `powerlevel()` foi na direcao que o multiplicador mandou -- e ela
			// pega os dois defeitos (o buff que nao chega e o buff que chega invertido) numa linha so.
			// ======================================================================================================
			bool acompanhou = esperado > 1
				? pl.Ficha.expressedBP > bpNoPiso * 1.05
				: pl.Ficha.expressedBP < bpNoPiso * 0.95;
			Checa($"{e.Raca}/{d.Nome}: o BP expresso ACOMPANHOU o multiplicador "
				  + $"(x{pl.Ficha.expressedBP / bpNoPiso:0.##})",
				  acompanhou, $"{pl.Ficha.expressedBP:N0} contra piso {bpNoPiso:N0}");
		}

		Checa($"{e.Raca}: a tecla C sobe pelo menos um degrau", visitadas.Count > 0,
			  $"ficou parado em {pl.Forma.Atual}");

		// ============================ E TODO DEGRAU VISITADO E DA LINHA DELE ============================
		// Este e o contra-exemplo POSITIVO da secao 3: la se pergunta se a forma alheia e recusada,
		// aqui se pergunta se ela apareceu. As duas sao necessarias -- uma linha que fosse recusada
		// pelo `Avaliar` e mesmo assim escolhida pelo `Proxima` (que varre o catalogo inteiro) passaria
		// na secao 3 e cairia aqui.
		string[] estranhas = [.. visitadas.Where(id => Catalogo.Def(id)!.Linha != e.Linha)];
		Checa($"{e.Raca}: TODO degrau que o C deu e da linha {e.Linha}",
			  estranhas.Length == 0, string.Join(", ", estranhas));

		GD.Print($"[raciais] {e.Raca}/{e.Classe}: {piso} -> {string.Join(" -> ", visitadas)}");
	}

	// =====================================================================
	// 3. E NAO SOBE A DOS OUTROS
	// =====================================================================
	/// <summary>
	/// O CONTRA-EXEMPLO. Sem ele, um `LinhasAbertas` que somasse TODAS as linhas raciais passaria em
	/// todas as checagens da secao 2 -- cada raca subiria a escada dela, e tambem a de todo mundo.
	///
	/// A recusa esperada e `LinhaFechada` e nao "qualquer recusa": recusar por falta de PODER seria
	/// uma forma que a raca alcanca quando ficar forte, e e outra coisa. O `PorQueNao` sabe disso e
	/// filtra `LinhaFechada` da mensagem de proposito -- contar de um jogo que nao e o seu.
	/// </summary>
	private void ENaoSobeADosOutros(ServerPlayer pl, EscadaRacial e, Action<string, bool, string> Checa)
	{
		PerfilDeFormas perfil = Perfil(pl);

		foreach (EscadaRacial outra in EscadasRaciais)
		{
			if (outra.Linha == e.Linha) continue;

			// O PRIMEIRO DEGRAU DA LINHA ALHEIA -- o mais barato dela, e portanto o que uma porta
			// frouxa deixaria passar primeiro.
			FormaDef alvo = Catalogo.DaLinha(outra.Linha).First();
			RecusaForma r = pl.Forma.Avaliar(alvo.Id, BpDeSobraRacial, 1, false, perfil);

			Checa($"{e.Raca} NAO alcanca `{alvo.Id}` (a linha de {outra.Raca}), com BP de sobra",
				  r == RecusaForma.LinhaFechada, r.ToString());
		}
	}

	// =====================================================================
	// 4. AS PORTAS, NOS DOIS SENTIDOS, PELA BOCA DO JOGO
	// =====================================================================
	/// <summary>
	/// ============================ A PORTA E A FRASE SAO A MESMA REGRA ============================
	/// O Core ja prova que `Avaliar` devolve `SemPoder`, `SemHabilidade` e `SemFuria` nos casos certos.
	/// O que ele nao pode provar e que o jogador FICA SABENDO -- e recusa muda so o que a frase diz.
	/// Este projeto ja teve a frase certa escrita, testada no Core e inalcancavel em jogo (a do SSJ4,
	/// que so aparecia depois de um `break` que nunca acontecia).
	///
	/// Sao tres portas e cada uma se paga num lugar diferente do jogo -- a loja de marcos, o treino e
	/// o luto --, entao a frase de cada uma tem que mandar a pessoa pro lugar certo. E as tres sao
	/// medidas NOS DOIS SENTIDOS: sem pagar o C nao sobe, pagando ele sobe no mesmo instante.
	/// ========================================================================================
	/// </summary>
	private void AsPortasPelaBocaDoJogo(ServerPlayer pl, EscadaRacial e,
										Action<string, bool, string> Checa, List<string> skillsDadas)
	{
		VestirCorpoRacial(pl, e);
		PagarAFlagDaLinha(pl, e, Checa, skillsDadas);
		AcenderARaivaQueALinhaPede(pl, e);

		if (pl.Forma.Proxima(pl.Ficha.BP, Perfil(pl)) is not { } primeiro)
		{
			Checa($"{e.Raca}: com tudo pago, ha um primeiro degrau pra medir", false, "`Proxima` deu nulo");
			return;
		}

		// --- (a) O PODER ---------------------------------------------------
		// BP 1 e nao "a porta menos um": o limiar e PESSOAL (sorteado por personagem), e uma bancada
		// que o recalculasse aqui estaria reescrevendo o `LimiaresPessoais` pra conferir a si mesma.
		double bpGuardado = pl.Ficha.BP;
		pl.Ficha.BP = 1;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		List<string> ditos = ApertarCEOuvir(pl);
		Checa($"{e.Raca}: sem PODER, o C nao sobe", pl.Forma.NaBase || pl.Forma.Atual == Catalogo.IdDoPiso(Perfil(pl)),
			  pl.Forma.Atual);
		Checa($"{e.Raca}: ...e a frase diz que a forma esta alem do alcance (e nao o numero secreto)",
			  ditos.Any(a => a.Contains("alem do seu alcance", StringComparison.OrdinalIgnoreCase))
			  && !ditos.Any(a => a.Contains(primeiro.PortaBp.ToString("0"), StringComparison.Ordinal)),
			  string.Join(" | ", ditos));
		pl.Ficha.BP = bpGuardado;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		// --- (b) A HABILIDADE COMPRADA -------------------------------------
		if (primeiro.PedeFlag is { } flag && e.Skill.Length > 0)
		{
			pl.Livro.Esquecer(e.Skill);
			AplicarEfeitos(pl);
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.Statify();
			pl.Ficha.Ki = pl.Ficha.MaxKi;

			Checa($"{e.Raca}: esquecer a skill APAGA a flag `{flag.Campo}` do corpo",
				  pl.Ficha.FlagsDeSkill.GetValueOrDefault(flag.Campo) < flag.Minimo,
				  $"{pl.Ficha.FlagsDeSkill.GetValueOrDefault(flag.Campo)}");

			ditos = ApertarCEOuvir(pl);
			Checa($"{e.Raca}: sem a SKILL, o C nao sobe (com BP de sobra)",
				  pl.Forma.Atual == Catalogo.IdDoPiso(Perfil(pl)), pl.Forma.Atual);

			// A FRASE TEM QUE NOMEAR A SKILL. E a unica recusa do jogo que se resolve na loja: "voce
			// ainda nao aprendeu isso" deixaria a pessoa procurando na aba errada.
			string nomeDaSkill = _skills?.Get(e.Skill)?.Nome ?? "";
			Checa($"{e.Raca}: ...e a frase NOMEIA a habilidade que falta (`{nomeDaSkill}`)",
				  ditos.Any(a => a.Contains("habilidade", StringComparison.OrdinalIgnoreCase)
							  && nomeDaSkill.Length > 0
							  && a.Contains(nomeDaSkill, StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", ditos));

			PagarAFlagDaLinha(pl, e, Checa, skillsDadas);
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.Statify();
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			Checa($"{e.Raca}: com a skill de volta, o C sobe no mesmo instante",
				  pl.Forma.Atual == primeiro.Id, pl.Forma.Atual);
			pl.Forma.Entrar(Catalogo.IdDoPiso(Perfil(pl)));
			AplicarForma(pl);
		}

		// --- (c) A RAIVA ---------------------------------------------------
		if (Catalogo.RaivaExigida(primeiro) is var nivel && nivel != NivelDeRaiva.Nenhuma)
		{
			// ZERAR AS DUAS JANELAS E O UNICO JEITO DE MEDIR O NEGATIVO. A bancada roda em segundos e o
			// prazo da raiva e de dois minutos de relogio real: sem isto, o luto aceso pelos blocos
			// anteriores atravessaria esta checagem e ela passaria verde sem ter chegado perto da porta
			// -- que e exatamente o defeito que a secao 2 da `RaivaTeste` tinha.
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			ProjetarRaiva(pl);
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.Statify();
			pl.Ficha.Ki = pl.Ficha.MaxKi;

			ditos = ApertarCEOuvir(pl);
			Checa($"{e.Raca}: sem RAIVA, o C nao sobe (com BP e skill pagos)",
				  pl.Forma.Atual == Catalogo.IdDoPiso(Perfil(pl)), pl.Forma.Atual);
			Checa($"{e.Raca}: ...e a frase DESENSINA em vez de mandar procurar briga",
				  ditos.Any(a => a.Contains("nao se alcanca querendo", StringComparison.OrdinalIgnoreCase)
							  || a.Contains("nao vem de treino", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", ditos));

			AcenderARaivaQueALinhaPede(pl, e);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			Checa($"{e.Raca}: com o luto aceso, o C sobe", pl.Forma.Atual == primeiro.Id, pl.Forma.Atual);
		}
		else
		{
			// A AUSENCIA TAMBEM E MEDIDA: as linhas que se COMPRAM nao pedem raiva no DM (`snamek()` e
			// `Alien_Trans()` nao olham `Emotion` uma unica vez), e o Frost Demon gateia por maestria.
			// Sem esta linha, uma raiva que vazasse pra elas passaria despercebida -- ninguem repara
			// numa forma que so sai depois de uma briga.
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			ProjetarRaiva(pl);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			Checa($"{e.Raca}: a linha dele NAO pede raiva -- o C sobe com o corpo em paz",
				  pl.Forma.Atual == primeiro.Id, pl.Forma.Atual);
		}
	}

	// =====================================================================
	// 5. A RACA SEM ESCADA NENHUMA
	// =====================================================================
	/// <summary>
	/// O PRECO DECLARADO do conserto do `LinhasAbertas`, medido em vez de prometido: quem nao tem
	/// transformacao no DM nao tem nenhuma aqui, e o jogo DIZ isso em vez de nao fazer nada.
	///
	/// O Humano e o caso puro -- ele nao tem uma unica forma no original --, e o `TemEscada` e a
	/// guarda que responde por ele. Uma guarda que apenas devolvesse sem avisar passaria na primeira
	/// metade desta checagem e deixaria o jogador apertando C sem entender por que nada acontece.
	/// </summary>
	private void ARacaSemEscada(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		VestirCorpoRacial(pl, new EscadaRacial("Human", "Normal", LinhaDeForma.Saiyajin, ""));
		AmigoAbatido(pl, "um amigo de bancada", NivelDeRaiva.Extrema);   // ate com luto na mao

		Checa("o Humano nao tem escada nenhuma",
			  !Catalogo.LinhasAbertas(Perfil(pl)).Any(l => Catalogo.DaLinha(l).Any(d => !Catalogo.NaoSeSobePraEla(d))),
			  string.Join(", ", Catalogo.LinhasAbertas(Perfil(pl))));

		List<string> ditos = ApertarCEOuvir(pl);
		Checa("o Humano aperta C e continua na base, com BP de 1e13 e luto na mao",
			  pl.Forma.NaBase, pl.Forma.Atual);
		Checa("...e o jogo DIZ que a raca dele nao tem essa escada",
			  ditos.Any(a => a.Contains("escada", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", ditos));
	}

	// =====================================================================
	// 6. A SUPRESSAO DO FROST DEMON NAO PEGOU MAIS NINGUEM
	// =====================================================================
	/// <summary>
	/// ============================ UMA REGRA QUE MORAVA NUM COMENTARIO ============================
	/// O `ParaOndeSeRecua` recua **um degrau** quando o degrau de baixo vale `Mult &lt;= 1`, e o proprio
	/// comentario de producao diz por que isso e seguro: *"Conferido entrada por entrada no catalogo
	/// de hoje: abaixo do SSJ2 esta o Grade 3 (4x), abaixo do SSJ3 esta o SSJ2 (4x), abaixo do
	/// `ui_perfected` esta o `ui_sign` (60x) -- nenhum deles passa"*.
	///
	/// **"Conferido entrada por entrada" e uma frase, nao um teste.** Ela envelhece no dia em que
	/// alguem acrescentar uma forma fraca -- e quase aconteceu: as quatro entradas que a saga Majin
	/// pediria (`genome.add_to_stat`, sem multiplicador) teriam `Mult = [1]`, e cinco candidatas a
	/// "forma de descanso" entrariam no catalogo de uma vez. O efeito seria mudo e caro: sair do SSJ3
	/// deixaria de voltar pra base e passaria a parar num degrau intermediario.
	///
	/// Aqui a frase vira medida, e por CONJUNTO -- o catalogo inteiro, por corpo racial.
	/// ========================================================================================
	/// </summary>
	private void ASupressaoNaoPEGOUNinguem(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		// --- (a) QUEM E REPOUSO, no catalogo inteiro -------------------------
		string[] repousos = [.. Catalogo.Todas.Where(Catalogo.PodeSerRepouso).Select(d => d.Id)];
		string[] foraDoFrost = [.. repousos.Where(id => Catalogo.Def(id)!.Linha != LinhaDeForma.FrostDemon)];

		Checa("NENHUMA forma fora da linha do Frost Demon e forma de repouso",
			  foraDoFrost.Length == 0, string.Join(", ", foraDoFrost));
		Checa("as cascas do Frost Demon (Ordem <= 5) SAO repouso -- senao o Mutante nasce sem corpo",
			  Catalogo.DaLinha(LinhaDeForma.FrostDemon).Where(d => d.Ordem <= 5).All(Catalogo.PodeSerRepouso),
			  string.Join(", ", Catalogo.DaLinha(LinhaDeForma.FrostDemon)
									   .Where(d => d.Ordem <= 5 && !Catalogo.PodeSerRepouso(d)).Select(d => d.Id)));
		Checa("e as duas Evolucoes NAO sao (elas custam BP, e repouso nao custa nada)",
			  Catalogo.DaLinha(LinhaDeForma.FrostDemon).Where(d => d.Ordem > 5).All(d => !Catalogo.PodeSerRepouso(d)),
			  "");

		// --- (b) RECUAR, DE CADA FORMA DE CADA ESCADA ------------------------
		foreach (EscadaRacial e in EscadasRaciais)
		{
			// A ESCADA DO FROST DEMON TEM BANCADA PROPRIA (`--frostteste`, "recuar e UM DEGRAU"), e
			// repeti-la aqui seria a bancada irma que este arquivo evita. O que falta la e justamente
			// isto: o resto do catalogo.
			if (e.Linha == LinhaDeForma.FrostDemon) continue;

			VestirCorpoRacial(pl, e);
			PerfilDeFormas perfil = Perfil(pl);
			string piso = Catalogo.IdDoPiso(perfil);

			foreach (FormaDef d in Catalogo.DaLinha(e.Linha))
			{
				string destino = ParaOndeSeRecua(new EstadoDeForma { Atual = d.Id }, perfil);
				Checa($"{e.Raca}: recuar de `{d.Id}` vai direto ao piso (`{piso}`), e nao a um degrau",
					  destino == piso, destino);
			}
		}
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// VESTE O CORPO -- raca, classe, corpos de Frost Demon, BP de sobra, Ki cheio e a forma de
	/// REPOUSO posta pelo mesmo caminho do login (`Catalogo.IdDoPiso`).
	///
	/// A CENA E ZERADA no fim pelo motivo que o `--frostteste` ja documentou: `Transformar` marca
	/// `CenaSegundos`, e cena PARA os relogios de Ki. Numa bancada que nao faz o tempo escorrer, uma
	/// estreia deixaria o corpo congelado e as checagens seguintes mediriam o congelamento.
	/// </summary>
	private void VestirCorpoRacial(ServerPlayer pl, EscadaRacial e)
	{
		pl.Race = e.Raca;
		pl.Ficha.Race = e.Raca;
		pl.Ficha.Class = e.Classe;
		pl.Class = e.Classe;
		if (pl.Ficha.Genoma != null) pl.Ficha.Genoma.Class = e.Classe;

		// A LISTA DE CORPOS SO EXISTE PRO FROST DEMON, e o `Sanear` e quem sabe disso -- e ele que a
		// criacao chama. Montar a lista aqui na mao daria um corpo que a criacao nunca produz.
		pl.Visual.FormasDeFrost = FormasDeFrost.EhFrost(e.Raca)
			? FormasDeFrost.Sanear(e.Classe, null) : [];

		pl.Ficha.KO = pl.Ficha.dead = false;
		pl.Ficha.BP = BpDeSobraRacial;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;

		pl.Forma = new EstadoDeForma { Atual = Catalogo.IdDoPiso(Perfil(pl)) };
		pl.CenaSegundos = 0;
		AplicarForma(pl);
		pl.CenaSegundos = 0;
	}

	/// <summary>
	/// ============================ A FLAG DA LINHA CHEGA POR DOIS CAMINHOS, E OS DOIS SAO DE PRODUCAO ============================
	/// Nenhuma linha desta bancada escreve `Ficha.FlagsDeSkill` na mao. O elo medido e o inteiro --
	/// **skill -> `after_learn` -> flag -> `PerfilDeFormas` -> gate** --, que e onde este projeto ja
	/// achou tres canais extraidos e sem consumidor.
	///
	/// E OS DOIS CAMINHOS SAO DIFERENTES DE PROPOSITO, porque o `skills.json` (extraido do DM) diz que
	/// eles sao:
	///
	///   * o **Super Namekuseijin** PENDE da arvore racial do Namekuseijin (`/datum/skill/tree/namek`
	///     lista `/datum/skill/namek/SuperNamek` nos galhos), entao ele se COMPRA com marcos -- e a
	///     bancada o compra, pelo `SkillBook.Aprender`, com a recusa conferida;
	///   * a **transformacao Alien** nao pende de arvore nenhuma. Nao e falha do porte: o
	///     `constituentskills` da `/datum/skill/tree/alien` do DM tambem nao a lista
	///     (`Skill Trees/Race Trees/alien.dm:10`). Skill solta so vem por concessao (`Dar`, que e o
	///     que o verb de admin faz), e e assim que ela entra aqui -- com a recusa `SemArvore` medida
	///     antes, pra que o dia em que alguem a pendurar numa arvore nao passe calado.
	/// =============================================================================================================
	/// </summary>
	private void PagarAFlagDaLinha(ServerPlayer pl, EscadaRacial e,
								   Action<string, bool, string> Checa, List<string> skillsDadas)
	{
		if (e.Skill.Length == 0 || _skills == null) return;
		if (pl.Livro.Sabe(e.Skill)) { AplicarEfeitos(pl); return; }

		pl.Livro.Conceder(20);
		Jandirus.Core.Skills.Recusa r =
			pl.Livro.Aprender(_skills, e.Skill, pl.Race, pl.Ficha.Class, vilao: false);

		if (r != Jandirus.Core.Skills.Recusa.Pode)
		{
			// A RECUSA E MEDIDA E NOMEADA, e nao engolida: `SemArvore` e o estado FIEL da transformacao
			// Alien (ver o cabecalho), e qualquer outra recusa aqui seria defeito de verdade.
			Checa($"{e.Raca}: `{e.Skill}` nao se compra na arvore, e a recusa e SemArvore",
				  r == Jandirus.Core.Skills.Recusa.SemArvore, r.ToString());
			pl.Livro.Dar(e.Skill);
		}
		else
		{
			Checa($"{e.Raca}: `{e.Skill}` se COMPRA na arvore racial (marcos)", true, "");
		}

		skillsDadas.Add(e.Skill);
		AplicarEfeitos(pl);

		if (Catalogo.DaLinha(e.Linha).FirstOrDefault(d => d.PedeFlag != null)?.PedeFlag is { } flag)
			Checa($"{e.Raca}: a skill escreveu `{flag.Campo}` no corpo (o elo skill -> flag)",
				  pl.Ficha.FlagsDeSkill.GetValueOrDefault(flag.Campo) >= flag.Minimo,
				  $"{pl.Ficha.FlagsDeSkill.GetValueOrDefault(flag.Campo)}");
	}

	/// <summary>
	/// Acende o luto SE a linha pedir -- pelo GANCHO (`AmigoAbatido`) e nao pelo campo, que e a mesma
	/// regra que a `--formasteste` ja segue: assim esta bancada continua valendo se o prazo, a janela
	/// ou o nome do campo mudarem.
	/// </summary>
	private void AcenderARaivaQueALinhaPede(ServerPlayer pl, EscadaRacial e)
	{
		bool pede = Catalogo.DaLinha(e.Linha).Any(d => Catalogo.NasceDaRaiva(d));
		if (pede) AmigoAbatido(pl, "um amigo de bancada", NivelDeRaiva.Extrema);
	}

	/// <summary>Aperta C uma vez com a escuta armada, e devolve o que o servidor disse.</summary>
	private List<string> ApertarCEOuvir(ServerPlayer pl)
	{
		EscutaDeAvisos = [];
		Transformar(pl, subir: true);
		List<string> ditos = EscutaDeAvisos ?? [];
		EscutaDeAvisos = null;
		return ditos;
	}
}
