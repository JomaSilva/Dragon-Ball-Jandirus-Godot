using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DOS DOIS DEGRAUS DE RAIVA -- roda dentro do `--formasteste`.
///
/// ============================ O QUE SO DAQUI SE VE ============================
/// A bancada `raiva` (AssetPipeline) prova as REGRAS: quem pede `Extrema`, quem pede `Lendaria`, e
/// que o `EstadoDeForma.Avaliar` cobra as duas. Ela nao pode provar TRES coisas, e as tres sao
/// justamente onde este port ja quebrou antes:
///
///   1. **QUE O PERFIL DO JOGO CARREGA O CAMPO.** La o `PerfilDeFormas` e escrito a mao; aqui ele
///      sai do `Perfil(pl)`, e o valor tem que NASCER DO RELOGIO -- das duas janelas do
///      `ServerPlayer`. Apagar a linha `Raiva:` daquele construtor deixaria o Core inteiro verde e
///      destravaria o jogo todo, porque `Nenhuma` e o padrao do struct... e o padrao RECUSA. Ou
///      seja: o defeito seria o oposto -- tranca calada, forma que nunca vem, ninguem entende.
///   2. **QUE A TECLA C OBEDECE.** O jogador nao escolhe forma: ele aperta C e o servidor oferece
///      o degrau mais forte aberto (`Proxima`). Perguntar so ao `Avaliar` deixa de fora o unico
///      funil por onde a forma pode vazar em jogo.
///   3. **QUE A FRASE CERTA SAI PELA BOCA DO JOGO.** Sao dois precos, e a diferenca entre eles nao
///      tem numero nem barra na tela: ou esta na frase, ou o Legendary nunca descobre que o dele e
///      mais barato. Isso e pacote no fio, e so a escuta do servidor le.
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// OS DOIS DEGRAUS, NO CORPO VIVO. Guarda e repoe tudo o que mexe -- classe, forma, liberadas,
	/// maestrias e as duas janelas --, pelo mesmo motivo das outras secoes deste arquivo: o
	/// estranho de um bloco nao pode virar o resultado do seguinte.
	/// </summary>
	private void ADuplaRaivaAoVivo(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		string classeAntes = pl.Ficha.Class, formaAntes = pl.Forma.Atual;
		var liberadasAntes = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasAntes = new HashSet<int>(pl.Forma.EstreiaVista);
		long extremaAntes = pl.FuriaExtremaAte, lendariaAntes = pl.RaivaLendariaAte;
		double kiAntes = pl.Ficha.Ki;
		double raivaAntes = pl.Ficha.Anger, baseRaivaAntes = pl.Ficha.baseAnger;
		double lendarioAntes = pl.Ficha.legendaryAngerBonus;

		try
		{
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			pl.Forma.Entrar(Catalogo.IdBase);
			AplicarForma(pl);

			// ============================ 1. AS DUAS JANELAS, LIDAS DO RELOGIO ============================
			Checa("em paz, o perfil do JOGO diz calma",
				  Perfil(pl).Raiva == NivelDeRaiva.Nenhuma, Perfil(pl).Raiva.ToString());

			Checa("o gancho com grau LENDARIO erupciona",
				  AmigoAbatido(pl, "Krillin", NivelDeRaiva.Lendaria), "");
			Checa("...e o perfil do jogo passa a dizer LENDARIA",
				  Perfil(pl).Raiva == NivelDeRaiva.Lendaria, Perfil(pl).Raiva.ToString());
			Checa("...e repetir o mesmo grau so prolonga (a cena nao toca duas vezes)",
				  !AmigoAbatido(pl, "Yamcha", NivelDeRaiva.Lendaria), "");

			// SUBIR DE GRAU ERUPCIONA DE NOVO, e tem que erupcionar: e uma dor NOVA, e a cinematica
			// de luto que um dia pendurarem aqui nao pode ser engolida por uma janela mais fraca ja
			// aberta. `jaEstava` compara o nivel EFETIVO com o grau que chegou -- e nao um booleano.
			Checa("subir de LENDARIA pra EXTREMA erupciona de novo",
				  AmigoAbatido(pl, "Bulma", NivelDeRaiva.Extrema), "");
			Checa("...e o perfil passa a dizer EXTREMA (a maior das duas janelas manda)",
				  Perfil(pl).Raiva == NivelDeRaiva.Extrema, Perfil(pl).Raiva.ToString());

			// E UM NOCAUTE NO MEIO DO LUTO NAO REBAIXA NINGUEM. Este e o defeito que os dois campos
			// separados existem pra impedir: com uma janela so, este `AmigoAbatido` sobrescreveria
			// o grau e fecharia o SSJ1 na cara de quem acabou de ver um amigo morrer.
			AmigoAbatido(pl, "Chichi", NivelDeRaiva.Lendaria);
			Checa("um nocaute no meio do luto NAO rebaixa a raiva",
				  Perfil(pl).Raiva == NivelDeRaiva.Extrema, Perfil(pl).Raiva.ToString());

			// E CALMA NAO E EVENTO. Um chamador distraido passando `Nenhuma` nao pode apagar janela
			// nenhuma -- senao o jeito de tirar a forma de alguem seria "abater um amigo com grau 0".
			Checa("acender `Nenhuma` nao faz nada e devolve FALSE",
				  !AmigoAbatido(pl, "ninguem", NivelDeRaiva.Nenhuma)
				  && Perfil(pl).Raiva == NivelDeRaiva.Extrema, Perfil(pl).Raiva.ToString());

			// AS JANELAS FECHAM SOZINHAS -- o prazo e puxado pra tras em vez de esperar 2 minutos.
			pl.FuriaExtremaAte -= (long)(SegundosDeRaiva * 1000) + 500;
			Checa("passado o prazo do luto, sobra a raiva LENDARIA (que ainda corre)",
				  Perfil(pl).Raiva == NivelDeRaiva.Lendaria, Perfil(pl).Raiva.ToString());
			pl.RaivaLendariaAte -= (long)(SegundosDeRaiva * 1000) + 500;
			Checa("passado o prazo das duas, volta a calma sem ninguem apagar nada",
				  Perfil(pl).Raiva == NivelDeRaiva.Nenhuma, Perfil(pl).Raiva.ToString());

			ARaivaComoNumero(pl, Checa);
			ACinematicaDaFuriaAoVivo(pl, Checa);

			// ============================ 2. A TECLA C NUM SAIYAJIN COMUM ============================
			// O tronco pede a furia do LUTO. Com o desconto da linha Legendary aceso e mais nada, o
			// C nao pode sair da base -- e essa e a checagem que separa "dois degraus" de "um degrau
			// com dois nomes".
			pl.Ficha.Class = "Normal";
			pl.Forma.Entrar(Catalogo.IdBase);

			// ============================ E O CORPO PRECISA CHEGAR AQUI **LIMPO** ============================
			// Estas quatro linhas sao as MESMAS que o bloco do Legendary logo abaixo ja executava, e a
			// falta delas aqui era assimetria e nao economia. Duas coisas atravessavam de cima:
			//
			//   * **A JANELA DE LUTO.** `ACinematicaDaFuriaAoVivo` (chamado dez linhas acima) acende
			//     `NivelDeRaiva.Extrema` varias vezes, e o prazo dela e de DOIS MINUTOS de relogio real.
			//     A bancada inteira roda em segundos: quando esta checagem perguntava "com raiva LENDARIA
			//     o C nao sai da base?", o corpo ainda estava em luto -- e a escada abria com razao.
			//   * **AS FORMAS JA LIBERADAS.** A raiva paga a entrada UMA VEZ (`!Despertou(d.Id)` no passo 9
			//     do `Avaliar`, o `hasbeast` do DM). Uma forma despertada por um bloco anterior dispensa a
			//     furia pra sempre, entao bastaria o SSJ1 ter sido visto antes pra esta medicao nao medir
			//     mais nada.
			//
			// O DEFEITO ERA INVISIVEL ATE AGORA porque a checagem passava por OUTRO motivo: com a escada
			// Saiyajin fechada pra raca deste corpo (ver `LinhasAbertas`), a tecla C era recusada por
			// `LinhaFechada` antes de a raiva ser sequer consultada. Verde, e cega.
			// ==============================================================================================
			pl.Forma.Liberadas.Clear();
			pl.Forma.EstreiaVista.Clear();
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			AplicarForma(pl);

			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Lendaria);
			EscutaDeAvisos = [];
			SubirAteParar(pl);
			List<string> comDesconto = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;

			Checa("Saiyajin comum com raiva LENDARIA: a tecla C nao sai da base",
				  pl.Forma.NaBase, "chegou em " + pl.Forma.Atual);
			Checa("...e a recusa fala da DOR que ele ainda nao teve",
				  comDesconto.Any(a => a.Contains("dor", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", comDesconto));

			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			SubirAteParar(pl);
			Checa("acesa a furia do LUTO, o MESMO C sobe a escada Saiyajin",
				  !pl.Forma.NaBase, pl.Forma.Atual);
			Checa("...e o corpo recebe o multiplicador da forma em que parou",
				  pl.Ficha.ssjBuff > 1.5, $"ssjBuff {pl.Ficha.ssjBuff:0.###}");

			// ============================ 3. A TECLA C NUM LEGENDARY ============================
			// O desconto do dono, medido: o MESMO nocaute que nao move um Saiyajin comum move a
			// linha Legendary inteira. Sem esta metade, "a raiva lendaria existe" seria uma frase
			// sobre um enum -- ela so vira regra quando alguem sobe com ela e o vizinho nao sobe.
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Forma.Liberadas.Clear();
			pl.Forma.EstreiaVista.Clear();
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			pl.Ficha.Class = "Legendary";
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			AplicarForma(pl);

			EscutaDeAvisos = [];
			SubirAteParar(pl);
			List<string> emPaz = EscutaDeAvisos ?? [];
			EscutaDeAvisos = null;
			Checa("Legendary em paz: a tecla C tambem nao sai da base",
				  pl.Forma.NaBase, "chegou em " + pl.Forma.Atual);
			Checa("...e a recusa dele fala de ver alguem CAIR, e nao da morte de um amigo",
				  emPaz.Any(a => a.Contains("cair", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", emPaz));

			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Lendaria);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			SubirAteParar(pl);
			Checa("com o MESMO nocaute que nao moveu o Saiyajin comum, o Legendary sobe",
				  !pl.Forma.NaBase, pl.Forma.Atual);
			Checa("...e o degrau que saiu e da linha Legendary",
				  pl.Forma.Def?.Linha == LinhaDeForma.Legendary, pl.Forma.Def?.Linha.ToString() ?? "?");

			// E O LUTO TAMBEM SERVE PRA ELE -- o `>=` do passo 9, no corpo vivo. Com igualdade
			// estrita, um Legendary de luto ficaria preso na base sem entender por que.
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Forma.Liberadas.Clear();
			pl.RaivaLendariaAte = 0;
			pl.FuriaExtremaAte = 0;
			AmigoAbatido(pl, "Bulma", NivelDeRaiva.Extrema);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			SubirAteParar(pl);
			Checa("Legendary em LUTO tambem sobe (quem viu morrer viu cair)",
				  !pl.Forma.NaBase, pl.Forma.Atual);
		}
		finally
		{
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Forma.Liberadas.Clear();
			pl.Forma.Liberadas.UnionWith(liberadasAntes);
			pl.Forma.EstreiaVista.Clear();
			pl.Forma.EstreiaVista.UnionWith(estreiasAntes);
			pl.Ficha.Class = classeAntes;
			pl.FuriaExtremaAte = extremaAntes;
			pl.RaivaLendariaAte = lendariaAntes;
			pl.Ficha.Ki = kiAntes;
			pl.Ficha.baseAnger = baseRaivaAntes;
			pl.Ficha.legendaryAngerBonus = lendarioAntes;
			pl.Ficha.Statify();
			pl.Ficha.Anger = raivaAntes;
			if (formaAntes != Catalogo.IdBase) pl.Forma.Entrar(formaAntes);
			AplicarForma(pl);
			EscutaDeAvisos = null;
		}
	}

	/// <summary>
	/// ============================ A RAIVA COMO **NUMERO**: SOBE, DECAI, E VIRA PODER ============================
	/// A secao de cima prova o DEGRAU (quem pode virar o que). Esta prova a MAGNITUDE -- o `Anger` do
	/// DM, que e a unica entrada do `angerBuff` e portanto a unica forma de a raiva virar BP.
	///
	/// ============================ O QUE SO DAQUI SE VE, DE NOVO ============================
	/// A `StatBench` ja mede o `angerBuff` (ela escreve `f.Anger = 9999` na mao e confere o teto). O
	/// que ela **nao** pode medir e se alguem escreve aquele campo em jogo -- e por anos ninguem
	/// escreveu: `Anger` ficava em 100 pra sempre e o `angerBuff` era 1,0 num sistema inteiro que
	/// parecia ligado. Aqui o numero nasce de onde ele nasce em jogo (as duas janelas, pelo relogio),
	/// e as checagens sao sobre a CORRENTE: janela -> numero -> buff -> BP expresso.
	///
	/// E MEDE AS TRES COISAS QUE PODEM DAR ERRADO SEM ALARDE:
	///   1. **O 2x PERMANENTE** -- o defeito que fez o agente anterior se recusar a ligar isto. Se o
	///      numero nao voltar pra 100 sozinho quando o prazo vence, o primeiro enlutado do servidor
	///      fica 2x mais forte pra sempre e ninguem descobre (parece balanceamento).
	///   2. **A CADENCIA** -- um decaimento por acumulacao (`Anger -= passo`) andaria mais rapido no
	///      tique de 30 Hz que no de 5 Hz. Como aqui e funcao pura do prazo, chamar cem vezes
	///      seguidas tem que dar o mesmo numero, e e isso que se checa.
	///   3. **QUEM MANDA NO TETO** -- o `MaxAnger` sai do `Statify` (`Fighter.Statify.cs:117`). Se a
	///      projecao cravasse um teto proprio, mexer no `baseAnger` deixaria de mudar a raiva e o
	///      sistema de vontade/potencial sairia da conta calado.
	/// ==============================================================================================
	/// </summary>
	private void ARaivaComoNumero(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		// Entra em paz: a secao de cima terminou com as duas janelas vencidas.
		ProjetarRaiva(pl);
		pl.Ficha.Tick(agoraMs: NowMs());

		Checa("em paz, a raiva vale o piso 100 (que e o 1,0x, e nao zero)",
			  Math.Abs(pl.Ficha.Anger - 100) < 0.001, $"{pl.Ficha.Anger:0.###}");
		Checa("...e o angerBuff da exatamente 1x",
			  Math.Abs(pl.Ficha.angerBuff - 1) < 0.001, $"{pl.Ficha.angerBuff:0.####}");

		// ============================ O LUTO ACENDE O NUMERO NO MESMO INSTANTE ============================
		// Sem a projecao dentro do `AmigoAbatido`, isto so valeria no proximo tique de ficha -- e o
		// `anger_will_transform()` do DM le o `Anger` na LINHA seguinte ao `Do_Anger_Stuff`.
		double poderCalmo = pl.Ficha.expressedBP;
		AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema);
		Checa("o luto acende a raiva NO INSTANTE (sem esperar o tique de ficha)",
			  pl.Ficha.Anger > 100, $"{pl.Ficha.Anger:0.###}");
		Checa("...e o numero e o MaxAnger do Statify, e nao um teto proprio",
			  Math.Abs(pl.Ficha.Anger - pl.Ficha.MaxAnger) < 0.5,
			  $"Anger {pl.Ficha.Anger:0.###} x MaxAnger {pl.Ficha.MaxAnger:0.###}");

		pl.Ficha.Tick(agoraMs: NowMs());
		Checa("...e o angerBuff passa de 1x sem ninguem escrever multiplicador nenhum",
			  pl.Ficha.angerBuff > 1.0001, $"{pl.Ficha.angerBuff:0.####}");
		Checa("...e o BP EXPRESSO sobe junto (a raiva chega no numero que o mundo le)",
			  pl.Ficha.expressedBP > poderCalmo * 1.0001,
			  $"{poderCalmo:0} -> {pl.Ficha.expressedBP:0}");

		// ============================ E ELE CHEGA COM O VALOR EXATO, E NAO "MAIOR" ============================
		// A linha de cima diz que a raiva ENCOSTOU no poder; esta diz QUANTO. E a diferenca nao e
		// preciosismo: `angerBuff` e um dos oito fatores do `powerlevel()` (`Fighter.Power.cs:107`), e
		// meia duzia de acidentes plausiveis deixam a checagem do ">" verde -- multiplicar por
		// `Anger/100` cru (sem o teto), somar na base como a familia 2 em vez de multiplicar no fim,
		// ou o buff entrar duas vezes por alguem "reforcar" a raiva num segundo fator. Nos tres casos
		// o BP sobe, e nos tres o numero esta errado.
		//
		// AS DUAS MEDIDAS SAO DO MESMO CORPO NO MESMO INSTANTE, e e por isso que elas dividem: entre
		// uma e outra so a JANELA muda, entao todo o resto do `powerlevel()` (Ki, HP, gravidade,
		// idade, forma) e literalmente o mesmo numero e some na divisao. Medir com dois `Tick`
		// separados no tempo mediria tambem o que o tique faz -- e o `kiratio` sozinho ja moveria o
		// resultado.
		// ================================================================================================
		double PoderAgora()
		{
			pl.Ficha.PowerLevel(agoraMs: NowMs());
			return pl.Ficha.expressedBP;
		}

		long extremaGuardada = pl.FuriaExtremaAte, lendariaGuardada = pl.RaivaLendariaAte;
		pl.FuriaExtremaAte = 0;
		pl.RaivaLendariaAte = 0;
		ProjetarRaiva(pl);
		double emPaz = PoderAgora();
		pl.FuriaExtremaAte = extremaGuardada;
		pl.RaivaLendariaAte = lendariaGuardada;
		ProjetarRaiva(pl);
		double emFuria = PoderAgora();

		// A CONDICAO DA DIVISAO SER LIMPA: `expressedAdd` entra DEPOIS do `angerBuff` (soma de zeni,
		// magia), entao com ela diferente de zero a razao deixaria de ser o buff puro e esta checagem
		// viraria uma aproximacao sem dizer. Ela e afirmada, e nao suposta.
		Checa("este corpo nao tem soma no fim do powerlevel (a razao de BP e o buff puro)",
			  Math.Abs(pl.Ficha.expressedAdd) < 1e-9, $"expressedAdd {pl.Ficha.expressedAdd:0.###}");
		Checa("o BP com raiva e o BP em paz VEZES o angerBuff -- exatamente",
			  emPaz > 0 && Math.Abs(emFuria / emPaz - pl.Ficha.angerBuff) < 1e-6,
			  $"{emFuria / Math.Max(emPaz, 1e-9):0.######}x contra angerBuff {pl.Ficha.angerBuff:0.######}");

		// ============================ A CADENCIA NAO MUDA O NUMERO ============================
		double umaVez = pl.Ficha.Anger;
		for (int i = 0; i < 100; i++) ProjetarRaiva(pl);
		Checa("projetar cem vezes seguidas nao move a raiva (funcao pura do prazo, nao acumulador)",
			  Math.Abs(pl.Ficha.Anger - umaVez) < 0.5, $"{umaVez:0.###} -> {pl.Ficha.Anger:0.###}");

		// ============================ O DECAIMENTO E UMA CURVA, E E ASSIM QUE SE MEDE UMA ============================
		// Esta e a secao que mais importa do arquivo inteiro: **a ausencia dela e o 2x permanente**. E
		// ela mede a curva INTEIRA e nao um ponto porque um ponto so nao distingue as tres coisas que
		// podem estar erradas aqui, e as tres foram consideradas de verdade nesta sessao:
		//
		//   * A FORMA da queda. Um ponto ("caiu") passa verde com decaimento linear, exponencial, em
		//     degraus, ou com o numero simplesmente oscilando. O DM e RETO (`Stats.dm:443` tira o
		//     mesmo tanto por volta), entao a queda a 80% da janela tem que ser QUATRO vezes a queda
		//     a 20% -- e essa razao e a assinatura de uma reta, que nenhuma outra curva imita.
		//   * A VELOCIDADE. `folga/8000` por volta de 0,2 s = 1600 s pra folga inteira. Trocar aquele
		//     8000 por "por segundo" (o erro classico deste port, a nota da unidade de tempo do
		//     `sleep(N)`) daria uma raiva que morre em 8 s -- e o ponto unico "caiu, e caiu pouco"
		//     continuaria verde em metade dos erros de escala. Aqui cada amostra e conferida contra a
		//     FORMULA do DM, entao qualquer fator errado cai na primeira.
		//   * QUEM DECIDE. O gotejamento NAO PODE alcancar a calma sozinho: no ultimo instante da
		//     janela a raiva ainda tem que estar bem acima de 100, porque quem a derruba e o
		//     `rageExpire` (`Stats.dm:438-441`) e nao o escoamento. Se essa linha reprovar, os dois
		//     relogios trocaram de papel e o balanceamento do luto mudou sem ninguem escrever nada.
		//
		// AS AMOSTRAS SAO TOMADAS PUXANDO O PRAZO, e nao esperando: a janela e de 120 s e a bancada
		// roda no arranque do servidor. Como o numero e funcao pura do prazo (ver `RaivaComoNumero`),
		// puxar o fim pra perto e exatamente a mesma conta que deixar o tempo passar -- e essa
		// equivalencia e ela propria uma propriedade que o teste da cadencia, logo acima, ja tranca.
		// ============================================================================================
		double folga = pl.Ficha.MaxAnger - 100;
		Checa("o corpo tem folga de raiva pra medir (senao a curva inteira mede zero)",
			  folga > 1, $"MaxAnger {pl.Ficha.MaxAnger:0.###}");

		double[] fracoes = [0.0, 0.2, 0.4, 0.6, 0.8, 0.99];
		double[] amostras = new double[fracoes.Length];

		for (int i = 0; i < fracoes.Length; i++)
		{
			pl.FuriaExtremaAte = NowMs() + (long)((1 - fracoes[i]) * SegundosDeRaiva * 1000);
			ProjetarRaiva(pl);
			amostras[i] = pl.Ficha.Anger;

			// `Stats.dm:443` inteiro: `MaxAnger - folga * decorrido / 1600`. A tolerancia e o desvio
			// de relogio entre escrever o prazo e ler o `NowMs()` de dentro da projecao -- alguns
			// milissegundos, contra uma inclinacao de `folga/1600` por segundo.
			double esperado = pl.Ficha.MaxAnger - folga * (fracoes[i] * SegundosDeRaiva) / 1600.0;
			Checa($"a {fracoes[i] * 100:0}% da janela a raiva vale {esperado:0.###} (a formula do DM)",
				  Math.Abs(amostras[i] - esperado) < 0.05, $"deu {amostras[i]:0.###}");
		}

		bool soDesce = true;
		for (int i = 1; i < amostras.Length; i++) if (amostras[i] > amostras[i - 1]) soDesce = false;
		Checa("a curva so DESCE -- nenhuma amostra sobe no meio da janela", soDesce,
			  string.Join(" -> ", amostras.Select(a => a.ToString("0.###"))));

		// O ZERO DA MEDIDA E A **PRIMEIRA AMOSTRA**, e nao o valor que estava na ficha: aquele foi
		// escrito ha alguns milissegundos de tempo real e ja gotejou um fio. Aqui as duas quedas
		// saem da mesma referencia, entao a razao entre elas nao carrega o desvio de relogio.
		double queda20 = amostras[0] - amostras[1], queda80 = amostras[0] - amostras[4];
		Checa("...e ela e uma RETA: a queda a 80% da janela e 4x a queda a 20%",
			  queda20 > 1e-6 && Math.Abs(queda80 / queda20 - 4) < 0.05,
			  $"{queda80:0.####} / {queda20:0.####} = {queda80 / Math.Max(queda20, 1e-9):0.###}");

		// O GOTEJAMENTO NAO CHEGA NA CALMA -- ele nao decide nada, so arredonda a curva.
		Checa("no ULTIMO instante da janela a raiva ainda esta bem acima da calma "
			+ "(quem derruba e o PRAZO)",
			  amostras[^1] > 100 + folga * 0.5,
			  $"{amostras[^1]:0.###} contra o piso 100 (folga de {folga:0.###})");

		// E REACENDER LEVANTA A CURVA DE VOLTA AO TOPO. `Murder.dm:112-113` escreve `=` e nao soma: o
		// prazo REINICIA, entao o decorrido volta a zero e o numero volta ao `MaxAnger`. Sem esta
		// linha, uma implementacao que guardasse o quanto ja escorreu passaria em tudo acima e
		// deixaria o segundo luto de uma briga valer menos que o primeiro -- calado.
		AmigoAbatido(pl, "Yamcha", NivelDeRaiva.Extrema);
		Checa("reacender no meio da queda devolve a raiva ao topo (o prazo REINICIA, nao acumula)",
			  Math.Abs(pl.Ficha.Anger - pl.Ficha.MaxAnger) < 0.05,
			  $"{pl.Ficha.Anger:0.###} x MaxAnger {pl.Ficha.MaxAnger:0.###}");

		// ============================ E O PRAZO VENCE SOZINHO -- O 2x PERMANENTE NAO EXISTE ============================
		pl.FuriaExtremaAte = NowMs() - 1;
		pl.RaivaLendariaAte = 0;
		ProjetarRaiva(pl);
		pl.Ficha.Tick(agoraMs: NowMs());
		Checa("vencido o prazo, a raiva volta a 100 SOZINHA (Stats.dm:438-441)",
			  Math.Abs(pl.Ficha.Anger - 100) < 0.001, $"{pl.Ficha.Anger:0.###}");
		Checa("...e o angerBuff volta a 1x -- nao ha 2x permanente onde guardar",
			  Math.Abs(pl.Ficha.angerBuff - 1) < 0.001, $"{pl.Ficha.angerBuff:0.####}");

		// ============================ O TETO E O DO `Fighter.Power`, E O `Statify` MANDA NELE ============================
		// `angerBuff = min(Anger/100, 2 + legendaryAngerBonus/100)`. Com uma raiva de raca irascivel
		// o `MaxAnger` passa de 200 e o teto de 2x aparece; com a skill `Legendary Anger` ele vira 3x.
		double baseGuardada = pl.Ficha.baseAnger;
		pl.Ficha.baseAnger = 1000;               // MaxAnger MUITO acima do teto de 2x
		pl.Ficha.Statify();
		double maxIrascivel = pl.Ficha.MaxAnger;
		AmigoAbatido(pl, "Bulma", NivelDeRaiva.Extrema);
		pl.Ficha.Tick(agoraMs: NowMs());
		Checa("mexer no baseAnger mexe no MaxAnger (o Statify continua mandando)",
			  maxIrascivel > 500, $"MaxAnger {maxIrascivel:0.###}");
		Checa("...e o angerBuff para em 2x, por mais raiva que haja",
			  Math.Abs(pl.Ficha.angerBuff - 2) < 0.001, $"{pl.Ficha.angerBuff:0.####}");

		pl.Ficha.legendaryAngerBonus = 100;      // a skill `Legendary_Anger`, ja ligada pelo canal de flags
		pl.Ficha.Statify();
		ProjetarRaiva(pl);
		pl.Ficha.Tick(agoraMs: NowMs());
		Checa("...e com a Legendary Anger o teto vira 3x (e so ela muda isso)",
			  Math.Abs(pl.Ficha.angerBuff - 3) < 0.001, $"{pl.Ficha.angerBuff:0.####}");

		// E A RAIVA NUNCA PASSA DO MaxAnger. O `ClampAnger` do `Fighter.Tick` e o ultimo freio, e ele
		// existe porque a anomalia do 20x do DM foi exatamente um `Anger` solto acima do teto.
		Checa("a raiva nunca passa do MaxAnger (o ClampAnger e o ultimo freio)",
			  pl.Ficha.Anger <= pl.Ficha.MaxAnger + 0.001,
			  $"{pl.Ficha.Anger:0.###} x {pl.Ficha.MaxAnger:0.###}");

		pl.Ficha.legendaryAngerBonus = 0;
		pl.Ficha.baseAnger = baseGuardada;
		pl.Ficha.Statify();
		pl.FuriaExtremaAte = 0;
		pl.RaivaLendariaAte = 0;
		ProjetarRaiva(pl);
	}

	/// <summary>
	/// ============================ QUANDO A CINEMATICA DA FURIA TOCA, E QUANDO ELA NAO TOCA ============================
	/// As quatro condicoes do `Murder.dm:119` mais a recarga, exercitadas UMA A UMA no corpo vivo --
	/// cada cenario liga so a condicao que quer medir e deixa as outras satisfeitas.
	///
	/// ============================ POR QUE ISTO NAO CABE NA BANCADA DE FONTES ============================
	/// A `raiva` [10] varre os fontes e prova que as quatro condicoes ESTAO ESCRITAS no gatilho. Ela
	/// nao pode provar que elas estao escritas na ordem certa, nem que elas leem o estado certo -- um
	/// `estavaEmFuria` calculado DEPOIS de a janela ser escrita, por exemplo, seria sempre verdadeiro
	/// e a cena nunca tocaria. Uma varredura de texto ve as duas versoes iguais.
	///
	/// E o que ela mede tambem nao tem sintoma: a decisao nao muda estado nenhum, so faz um pacote
	/// sair ou nao sair. Sem a <see cref="EscutaDeFurias"/> a unica forma de conferir seria jogar com
	/// duas pessoas e reparar numa cena de cinco segundos.
	///
	/// ============================ O BP MANDA MAIS QUE A RAIVA AQUI, E ISSO E O ITEM 4 ============================
	/// O corpo desta bancada e um Saiyajin com BP de sobra: enfurecer um Saiyajin com BP de SSJ1 **nao**
	/// toca a cena da furia, porque a transformacao fica com o momento. Entao os cenarios que querem
	/// VER a cena baixam o BP -- e o cenario que quer ver a cena NAO tocar o levanta de volta. E o
	/// unico jeito de medir os dois lados da condicao 4 no mesmo corpo.
	/// ==================================================================================================================
	/// </summary>
	private void ACinematicaDaFuriaAoVivo(ServerPlayer pl, Action<string, bool, string> Checa)
	{
		double bpAntes = pl.Ficha.BP;
		long cenaAntes = pl.FuriaCenaAte;
		LiteNetLib.NetPeer? peerAntes = pl.Peer;

		// A ESCUTA E LOCAL A ESTE BLOCO: ela nasce aqui e morre no `finally`, como as outras deste
		// arquivo. Em jogo o campo e nulo e a linha de producao e uma comparacao contra null.
		EscutaDeFurias = [];

		int Furias() => EscutaDeFurias?.Count ?? 0;

		// LIMPA TUDO ANTES DE CADA CENARIO: as janelas (senao tudo vira prolongamento) e a recarga.
		void EmPaz()
		{
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			pl.FuriaCenaAte = 0;
			ProjetarRaiva(pl);
			EscutaDeFurias?.Clear();
		}

		try
		{
			// ============================ CONDICAO 4: A TRANSFORMACAO FICA COM O MOMENTO ============================
			// Com o BP de sobra, o proximo degrau deste Saiyajin e o SSJ1 -- que NASCE DA RAIVA. O DM
			// pula a cena da furia exatamente aqui: *"the transformation owns the moment"*.
			EmPaz();
			pl.Ficha.BP = bpAntes;
			pl.Forma.Entrar(Catalogo.IdBase);
			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema);
			Checa("furia que VAI virar transformacao nao toca a cena de raiva (o DM cede o momento)",
				  Furias() == 0, $"{Furias()} cena(s)");

			// ...E O AVESSO, no mesmo corpo. Sem BP pra degrau nenhum, `Proxima` devolve nulo e a
			// previsao da falso -- a cena e da furia. Se as duas metades nao discordassem, esta secao
			// estaria medindo "o pacote nunca sai", que passa verde com o gatilho apagado.
			EmPaz();
			pl.Ficha.BP = 1;
			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema);
			Checa("furia que NAO destrava forma nenhuma TOCA a cena de raiva", Furias() == 1,
				  $"{Furias()} cena(s)");
			Checa("...e ela e do enlutado", EscutaDeFurias?.Contains(pl.Id) == true, "");

			// ============================ A RECARGA (`rageCinematicCD`) ============================
			// Janelas fechadas (erupcao nova, legitima) e a recarga ainda correndo: o DM nao toca.
			// E o caso da briga em grupo -- ver o comentario do `ServerPlayer.FuriaCenaAte`.
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			ProjetarRaiva(pl);
			EscutaDeFurias?.Clear();
			Checa("a recarga de 60 s foi anotada",
				  pl.FuriaCenaAte > NowMs() && pl.FuriaCenaAte <= NowMs()
											   + (long)(Cinematicas.SegundosEntreFurias * 1000) + 50,
				  $"faltam {(pl.FuriaCenaAte - NowMs()) / 1000.0:0.#}s");
			AmigoAbatido(pl, "Yamcha", NivelDeRaiva.Extrema);
			Checa("uma erupcao NOVA dentro da recarga nao repete a cena", Furias() == 0,
				  $"{Furias()} cena(s)");

			// E VENCIDA A RECARGA, ela volta -- puxando o prazo pra tras em vez de esperar um minuto.
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			pl.FuriaCenaAte -= (long)(Cinematicas.SegundosEntreFurias * 1000) + 500;
			ProjetarRaiva(pl);
			EscutaDeFurias?.Clear();
			AmigoAbatido(pl, "Tenshinhan", NivelDeRaiva.Extrema);
			Checa("vencida a recarga, a cena volta a tocar", Furias() == 1, $"{Furias()} cena(s)");

			// ============================ CONDICAO 2: O `wasRaging` ============================
			// A janela continua aberta do evento anterior e a recarga vai a zero: o unico motivo pra
			// nao tocar e "ele ja estava enfurecido". E o caso do amigo que cai NO MEIO do luto.
			pl.FuriaCenaAte = 0;
			EscutaDeFurias?.Clear();
			AmigoAbatido(pl, "Chaos", NivelDeRaiva.Extrema);
			Checa("prolongar uma furia ja acesa nao toca a cena de novo (o `wasRaging`)",
				  Furias() == 0, $"{Furias()} cena(s)");

			// E O `wasRaging` E MAIS LARGO QUE O RETORNO DO GANCHO -- esta e a checagem que separa os
			// dois conceitos. Um nocaute abre a janela LENDARIA sem cena; a morte que vem em seguida e
			// erupcao EXTREMA de verdade (o gancho devolve TRUE), e mesmo assim o DM nao toca a cena,
			// porque o corpo ja estava enfurecido. Se alguem trocar `estavaEmFuria` pelo `!jaEstava`,
			// so esta linha cai.
			EmPaz();
			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Lendaria);
			Checa("ver um amigo CAIR nao toca cena nenhuma (o `extreme = 0` do DM)", Furias() == 0,
				  $"{Furias()} cena(s)");
			bool erupcao = AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema);
			Checa("...e a morte que vem DEPOIS dele e erupcao de grau novo", erupcao, "");
			Checa("...mas ainda assim nao toca cena: o corpo ja estava enfurecido", Furias() == 0,
				  $"{Furias()} cena(s)");

			// ============================ CONDICAO 3: CORPO SEM DONO ============================
			// O `client` do DM. NPC nao assiste cinematica -- e neste port "sem dono" e o `Peer` nulo.
			EmPaz();
			pl.Peer = null;
			AmigoAbatido(pl, "Krillin", NivelDeRaiva.Extrema);
			Checa("corpo SEM DONO nao dispara cinematica (o `client` do `Murder.dm:138`)",
				  Furias() == 0, $"{Furias()} cena(s)");
			Checa("...e nem gasta a recarga dele", pl.FuriaCenaAte == 0, $"{pl.FuriaCenaAte}");
		}
		finally
		{
			EscutaDeFurias = null;
			pl.Peer = peerAntes;
			pl.Ficha.BP = bpAntes;
			pl.FuriaCenaAte = cenaAntes;
			pl.FuriaExtremaAte = 0;
			pl.RaivaLendariaAte = 0;
			ProjetarRaiva(pl);
			pl.Ficha.Statify();
		}
	}

	/// <summary>
	/// APERTA C ATE PARAR DE SUBIR, passando as cinematicas.
	///
	/// PELO `Transformar` E NAO PELO `Avaliar`: e a MESMA funcao que a tecla C do jogador chama, e e
	/// o unico funil por onde uma forma pode vazar em jogo (`Proxima` escolhe o degrau mais forte
	/// aberto -- perguntar so ao `Avaliar` deixaria esse caminho de fora).
	///
	/// A CENA E QUEIMADA A CADA DEGRAU pelo <see cref="PassarACena"/> daqui do lado: enquanto ela
	/// prende o corpo, o proximo `Transformar` nao pega -- e a bancada concluiria "a escada parou"
	/// no meio de uma cinematica em vez de num gate.
	///
	/// TETO DE VOLTAS e nao `while`: um degrau que passasse a se repetir viraria laco infinito e a
	/// bancada TRAVARIA em vez de reprovar -- o unico jeito de um teste ser pior que nenhum.
	/// </summary>
	private void SubirAteParar(ServerPlayer pl)
	{
		for (int c = 0; c < 12; c++)
		{
			string antes = pl.Forma.Atual;
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Transformar(pl, subir: true);
			PassarACena(pl);
			if (pl.Forma.Atual == antes) return;
		}
		GD.PrintErr("[bancada] SubirAteParar bateu no teto de 12 degraus");
	}
}
