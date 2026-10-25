using System.Text.Json;
using System.Text.RegularExpressions;
using Jandirus.Core.Forms;
using Jandirus.Core.Lingua;
using Jandirus.Core.Skills;
using Chao = Jandirus.Core.Items.Chao;

namespace Jandirus.Tools;

/// <summary>
/// BANCADA DO TRADUTOR (`lingua`) -- o pedido do dono de 2026-10-22: o jogo inteiro em ingles por opcao, e a frase
/// automatica traduzida na tela de quem joga em ingles. Ver `Core/Lingua/Tradutor.cs` e `Client/Idioma.cs`.
///
/// ============================ O QUE ELA PROVA ============================
///   1. O MOTOR, com um dicionario escrito AQUI A MAO (as respostas esperadas tambem): a frase inteira, a caixa, as
///      bordas, o molde com pedaco no lugar que o ingles der, o pedaco que e texto do jogo traduzido por sua vez, o
///      pedaco CRU (`{n!}`, o que uma pessoa escreveu), o pedaco repetido, a frase composta e o molde fraco que nao pode
///      sequestrar frase longa. Cada familia com o seu contra-exemplo: o botao de defeito do Core ligado, a MESMA regua
///      reprova.
///   1b. AS PALAVRAS DE COMANDO (o segundo pedido do dono, 2026-10-22): o servidor entende a palavra do aviso em portugues e a
///      do aviso em ingles -- "sim" e "yes", "tudo" e "all" (`Core/Lingua/PalavrasDeComando.cs`).
///   2. O DICIONARIO DE VERDADE (`Assets/Data/lingua-en.json`), lido pelo mesmo `DoJson` do jogo: toda frase inteira
///      sai igual ao ingles dela, e todo molde, preenchido com um nome qualquer, sai igual ao ingles dele preenchido com o
///      mesmo nome -- e a prova de que nenhum molde mais generico "rouba" a frase de outro na volta (a traducao e feita
///      pelo texto ja montado, e dois moldes podem casar com o mesmo texto). Os pedacos de cada ingles existem no
///      portugues dele. E onde o portugues cita uma palavra de comando, o ingles cita a inglesa que o servidor entende.
///   3. AS PORTAS DO SERVIDOR, lidas no codigo: nenhum verbo compara o argumento direto com a palavra portuguesa (o
///      servidor nao roda aqui -- a leitura e o que alcanca o verbo que voltasse a perguntar so pelo "sim").
///   4. O QUE O JOGO MONTA PEDACO A PEDACO (o pedido do dono de 2026-10-23): a FICHA de cada skill do catalogo de verdade
///      (o que a compra e cada nivel dao, como a tela a pinta) e a FALA e a NARRACAO de cada cinematica, como o balao e o chat
///      as mostram -- nada pode sair parecendo portugues.
/// ==========================================================================
///
///     dotnet run --project Tools/AssetPipeline -- lingua [caminho/do/lingua-en.json]
/// </summary>
public static class LinguaBench
{
	private static int _ok, _falhou;

	private static void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; Console.WriteLine($"  OK   {nome}"); }
		else { _falhou++; Console.WriteLine($"  FALHA {nome}   {detalhe}"); }
	}

	/// <summary>A regua da familia, e a mesma regua com o defeito ligado: tem que passar sem ele e reprovar com ele.</summary>
	private static void ComContraExemplo(string nome, Func<bool> regua, Action ligar, Action desligar, string detalhe = "")
	{
		Checa(nome, regua(), detalhe);
		ligar();
		try { Checa("(defeito injetado) " + nome + " -- a mesma regua REPROVA", !regua()); }
		finally { desligar(); }
	}

	private static Tradutor Mao() => new(new Dictionary<string, string>
	{
		["Treinar"] = "Train",
		["Meditar"] = "Meditate",
		["Semente dos deuses"] = "Senzu Bean",
		["Braço esquerdo"] = "Left arm",
		["Você ganhou {0} zeni."] = "You got {0} zeni.",
		["larga {0} no chão."] = "drops {0} on the ground.",
		["{0} ganhou de {1} na final."] = "{1} lost the final to {0}.",
		["{0}?! Fala comigo, {0}!"] = "{0}?! Talk to me, {0}!",
		["(Relógio) {0} diz: \"{1}\""] = "(Watch) {0} says: \"{1!}\"",
		["{0} com {1}"] = "{0} with {1}",
		["{1} NÃO!"] = "{1}, NO!",
		["Fechar o jogo"] = "Quit game",
		["voce esta calado."] = "you are muted.",
		["Chaves {{ e }} ficam."] = "Braces {{ and }} stay.",
		["[aviso] {0}"] = "[notice] {0!}",
		["COM VOCÊ"] = "WITH YOU",
		["{0} não existe mais"] = "{0} no longer exists",
		["{0} NÃO EXISTE MAIS"] = "{0} NO LONGER EXISTS",
		["   próximo degrau: {0}"] = "   next step: {0}",
		["médio"] = "average",
		["madrugada"] = "late night",
		[" (no céu)"] = " (in the sky)",
		["crescente gibosa"] = "waxing gibbous",
		["domínio corporal"] = "body mastery",
		["arma equipada"] = "weapon equipped",
		["abre com: {0}   ·   regra de {1}"] = "unlocks with: {0}   ·   rule from {1}",
		["Ignora o BP."] = "Ignores BP.",
		["Cai em VOCÊ. Ignora a raça."] = "Applies to YOU. Ignores race.",
		["Ignora a raça; o Ki entra cheio."] = "Ignores race; Ki comes in full.",
		["{0} vago{1}"] = "{0} vacant",
		["chuva"] = "rain",
		["neve"] = "snow",
		["neblina"] = "fog",
		["Acontece aqui naturalmente: {0}"] = "Happens here naturally: {0}",
		["o chão começa a tremer."] = "the ground begins to shake.",
		["escolha única entre:"] = "pick one of:",
	});

	public static int Rodar(string? caminho)
	{
		Console.WriteLine("== 1. o motor, com um dicionario feito a mao ==");
		Tradutor t = Mao();

		Checa("a frase inteira", t.Traduzir("Treinar") == "Train", t.Traduzir("Treinar"));
		Checa("a frase que nao esta no dicionario volta como veio", t.Traduzir("Kamehameha!!") == "Kamehameha!!");
		Checa("numero e simbolo nem sao procurados", t.Traduzir("1.200 / 3") == "1.200 / 3");
		Checa("CAIXA ALTA sai em caixa alta (o `Tema.Rotulo` grava o rotulo assim)", t.Traduzir("TREINAR") == "TRAIN", t.Traduzir("TREINAR"));
		Checa("a primeira letra minuscula fica minuscula", t.Traduzir("fechar o jogo") == "quit game", t.Traduzir("fechar o jogo"));
		Checa("o aviso que comeca minusculo continua minusculo", t.Traduzir("voce esta calado.") == "you are muted.");
		Checa("os espacos das bordas ficam", t.Traduzir("  Treinar ") == "  Train ", $"[{t.Traduzir("  Treinar ")}]");
		Checa("a pontuacao e o enfeite das bordas voltam pro lugar", t.Traduzir("» Treinar:") == "» Train:", t.Traduzir("» Treinar:"));
		Checa("chave dobrada e chave de verdade", t.Traduzir("Chaves { e } ficam.") == "Braces { and } stay.", t.Traduzir("Chaves { e } ficam."));
		Checa("a chave em CAIXA ALTA pedida em minusculas sai minuscula inteira", t.Traduzir("com você") == "with you", t.Traduzir("com você"));
		Checa("dois moldes empatados: ganha o da caixa do pedido", t.Traduzir("Goku não existe mais") == "Goku no longer exists"
			  && t.Traduzir("GOKU NÃO EXISTE MAIS") == "GOKU NO LONGER EXISTS", t.Traduzir("Goku não existe mais"));
		Checa("o molde que comeca com espacos casa com o pedido aparado, e os espacos ficam",
			  t.Traduzir("   próximo degrau: SSJ2") == "   next step: SSJ2" && t.Traduzir("próximo degrau: SSJ2") == "next step: SSJ2",
			  $"[{t.Traduzir("   próximo degrau: SSJ2")}]");

		ComContraExemplo("o molde: o numero volta no lugar dele",
			() => t.Traduzir("Você ganhou 1.500 zeni.") == "You got 1.500 zeni.",
			() => { Tradutor.SemMoldesDeTeste = true; t = Mao(); }, () => { Tradutor.SemMoldesDeTeste = false; t = Mao(); },
			t.Traduzir("Você ganhou 1.500 zeni."));
		ComContraExemplo("o pedaco que e texto do jogo tambem se traduz",
			() => t.Traduzir("larga Semente dos deuses no chão.") == "drops Senzu Bean on the ground.",
			() => { Tradutor.PedacoCruDeTeste = true; t = Mao(); }, () => { Tradutor.PedacoCruDeTeste = false; t = Mao(); },
			t.Traduzir("larga Semente dos deuses no chão."));
		Checa("o pedaco vai pro lugar que o INGLES der", t.Traduzir("Goku ganhou de Vegeta na final.") == "Vegeta lost the final to Goku.",
			t.Traduzir("Goku ganhou de Vegeta na final."));
		Checa("o pedaco repetido tem de ser o mesmo nas duas vezes (casou)", t.Traduzir("Goku?! Fala comigo, Goku!") == "Goku?! Talk to me, Goku!");
		Checa("...e com nomes diferentes nao e o mesmo molde", t.Traduzir("Goku?! Fala comigo, Vegeta!") == "Goku?! Fala comigo, Vegeta!");
		Checa("o molde em CAIXA ALTA (o grito da furia com o nome do amigo)", t.Traduzir("KURIRIN NÃO!") == "KURIRIN, NO!", t.Traduzir("KURIRIN NÃO!"));
		Checa("o pedaco CRU ({1!}) e o que uma pessoa escreveu: nao se traduz",
			t.Traduzir("(Relógio) Bulma diz: \"Treinar\"") == "(Watch) Bulma says: \"Treinar\"", t.Traduzir("(Relógio) Bulma diz: \"Treinar\""));
		Checa("a frase composta: cada pedaco, com a MESMA junta", t.Traduzir("Treinar  ·  Meditar") == "Train  ·  Meditate", t.Traduzir("Treinar  ·  Meditar"));
		Checa("...e linha por linha", t.Traduzir("Treinar\nMeditar") == "Train\nMeditate");
		Checa("...e frase por frase", t.Traduzir("Você ganhou 3 zeni. Treinar") == "You got 3 zeni. Train", t.Traduzir("Você ganhou 3 zeni. Treinar"));
		Checa("o molde FRACO nao sequestra frase longa (meio a meio nao vale)",
			t.Traduzir("o guerreiro luta com a espada do avo") == "o guerreiro luta com a espada do avo",
			t.Traduzir("o guerreiro luta com a espada do avo"));
		Checa("...mas vale quando o resultado sai inteiro em ingles", t.Traduzir("Treinar com Meditar") == "Train with Meditate",
			t.Traduzir("Treinar com Meditar"));
		Checa("...e o pedaco CRU nao conta: o aviso do admin em portugues sai com a moldura em ingles",
			t.Traduzir("[aviso] o servidor vai reiniciar agora") == "[notice] o servidor vai reiniciar agora",
			t.Traduzir("[aviso] o servidor vai reiniciar agora"));
		Checa("o detalhe entre parenteses colado num numero ('20   (médio)')", t.Traduzir("20   (médio)") == "20   (average)",
			  t.Traduzir("20   (médio)"));
		Checa("o nome com o detalhe entre parenteses no fim ('crescente gibosa (no céu)', 'madrugada (05,6h)')",
			  t.Traduzir("crescente gibosa (no céu)") == "waxing gibbous (in the sky)" && t.Traduzir("madrugada (05,6h)") == "late night (05,6h)",
			  t.Traduzir("crescente gibosa (no céu)") + " | " + t.Traduzir("madrugada (05,6h)"));
		Checa("a conta e o 'e' do requisito de arvore, dentro do molde do cartao",
			  t.Traduzir("abre com: domínio corporal ≥ 2 e arma equipada   ·   regra de Strength of Body")
			  == "unlocks with: body mastery ≥ 2 and weapon equipped   ·   rule from Strength of Body",
			  t.Traduzir("abre com: domínio corporal ≥ 2 e arma equipada   ·   regra de Strength of Body"));
		Checa("...e o 'e' nao vira 'and' quando um dos lados nao tem traducao",
			  t.Traduzir("domínio corporal ≥ 2 e cabelo comprido") == "body mastery ≥ 2 e cabelo comprido",
			  t.Traduzir("domínio corporal ≥ 2 e cabelo comprido"));
		Checa("as frases coladas pelo codigo: o GRUPO que o dicionario conhece, e a frase solta depois",
			  t.Traduzir("Cai em VOCÊ. Ignora a raça. Ignora o BP.") == "Applies to YOU. Ignores race. Ignores BP.",
			  t.Traduzir("Cai em VOCÊ. Ignora a raça. Ignora o BP."));
		Checa("o corte que sai inteiro em ingles ganha do que corta o grupo no meio (o '; ' de dentro da frase)",
			  t.Traduzir("Treinar. Ignora a raça; o Ki entra cheio.") == "Train. Ignores race; Ki comes in full.",
			  t.Traduzir("Treinar. Ignora a raça; o Ki entra cheio."));
		Checa("o molde com o plural colado na palavra ('{0} vago{1}' acha '30 vagos' e '1 vago')",
			  t.Traduzir("30 vagos") == "30 vacant" && t.Traduzir("1 vago") == "1 vacant", t.Traduzir("30 vagos") + " | " + t.Traduzir("1 vago"));
		Checa("a lista separada por virgula, dentro do molde",
			  t.Traduzir("Acontece aqui naturalmente: chuva, neve, neblina") == "Happens here naturally: rain, snow, fog",
			  t.Traduzir("Acontece aqui naturalmente: chuva, neve, neblina"));
		// (2026-10-23) OS CORTES DA FICHA DE SKILL: o nome do campo com o numero COM SINAL colado no fim -- so o nome se traduz, o
		// numero fica como veio (virgula decimal e tudo), tambem atras do marcador da tela e dentro do molde --, e a seta que cola
		// a casa escolhida e a tecnica dela
		ComContraExemplo("os cortes da ficha: o nome com o numero com sinal no fim ('domínio corporal +0,4', '×1,2', '-1', '−3') e a seta",
			() => t.Traduzir("domínio corporal +0,4") == "body mastery +0,4" && t.Traduzir("domínio corporal ×1,2") == "body mastery ×1,2"
				  && t.Traduzir("arma equipada -1") == "weapon equipped -1" && t.Traduzir("domínio corporal −3") == "body mastery −3"
				  && t.Traduzir("• domínio corporal +1") == "• body mastery +1"
				  && t.Traduzir("Acontece aqui naturalmente: domínio corporal +2, arma equipada ×1,5")
					 == "Happens here naturally: body mastery +2, weapon equipped ×1,5"
				  && t.Traduzir("Treinar → Meditar, Treinar → madrugada") == "Train → Meditate, Train → late night",
			() => { Tradutor.SemOsCortesDaFichaDeTeste = true; t = Mao(); }, () => { Tradutor.SemOsCortesDaFichaDeTeste = false; t = Mao(); },
			t.Traduzir("domínio corporal +0,4") + " | " + t.Traduzir("• domínio corporal +1") + " | " + t.Traduzir("Treinar → Meditar, Treinar → madrugada"));
		Checa("...e o numero SEM sinal no fim nao e cortado (fica com quem ja o traduzia)",
			  t.Traduzir("domínio corporal 2") == "domínio corporal 2", t.Traduzir("domínio corporal 2"));
		// (2026-10-23) A PONTUACAO DO FIM QUE E DA FRASE: a narracao da cinematica chega ao chat como "* frase." e a chave do
		// dicionario tem o ponto; o marcador da ficha, idem com os dois-pontos
		ComContraExemplo("a borda devolve a pontuacao que e da frase ('* o chão começa a tremer.', '• escolha única entre:')",
			() => t.Traduzir("* o chão começa a tremer.") == "* the ground begins to shake." && t.Traduzir("• escolha única entre:") == "• pick one of:"
				  && t.Traduzir("» Treinar:") == "» Train:",
			() => { Tradutor.BordaComeAPontuacaoDeTeste = true; t = Mao(); }, () => { Tradutor.BordaComeAPontuacaoDeTeste = false; t = Mao(); },
			t.Traduzir("* o chão começa a tremer.") + " | " + t.Traduzir("• escolha única entre:"));
		t.Traduzir("uma frase que ninguem traduziu");
		Checa("o portugues sem traducao entra nas faltas", t.Faltas.Contains("uma frase que ninguem traduziu"));
		Checa("...e o ingles que veio de fabrica nao", !t.Faltas.Contains("Kamehameha!!"));
		Checa("parece portugues: acento, e palavra que so o portugues usa",
			Tradutor.PareceTextoPortugues("Você ganhou") && Tradutor.PareceTextoPortugues("nao tem Ki") && !Tradutor.PareceTextoPortugues("You got 30 zeni")
			&& !Tradutor.PareceTextoPortugues("Kamehameha!!"));

		Console.WriteLine("== 1b. as palavras de comando: a do aviso em portugues e a do aviso em ingles ==");
		ComContraExemplo("o 'yes' confirma como o 'sim' (e a caixa e os espacos nao contam)",
			() => PalavrasDeComando.E("yes", "sim") && PalavrasDeComando.E(" YES ", "sim") && PalavrasDeComando.E("Sim", "sim"),
			() => PalavrasDeComando.SoPortuguesDeTeste = true, () => PalavrasDeComando.SoPortuguesDeTeste = false);
		Checa("as outras do aviso em ingles: all, none, stop, random, default",
			PalavrasDeComando.E("all", "tudo") && PalavrasDeComando.E("none", "nenhuma") && PalavrasDeComando.E("stop", "parar")
			&& PalavrasDeComando.E("random", "sortear") && PalavrasDeComando.E("default", "padrao") && PalavrasDeComando.E("random", "aleatorio"));
		Checa("...e as grafias que o servidor ja aceitava continuam (com acento, o plural do modo)",
			PalavrasDeComando.E("padrão", "padrao") && PalavrasDeComando.E("aleatório", "aleatorio") && PalavrasDeComando.E("aleatórias", "aleatorio"));
		Checa("CONTRA-EXEMPLO: a palavra de OUTRO comando, a negativa e o quase-igual nao confirmam",
			!PalavrasDeComando.E("all", "sim") && !PalavrasDeComando.E("no", "sim") && !PalavrasDeComando.E("nao", "sim")
			&& !PalavrasDeComando.E("yess", "sim") && !PalavrasDeComando.E("", "sim") && !PalavrasDeComando.E("random", "padrao"));
		ComContraExemplo("largar com 'all' larga a pilha inteira, como com 'tudo'",
			() => Chao.Quanto("all", 7) == 7 && Chao.Quanto("ALL", 7) == 7 && Chao.Quanto("tudo", 7) == 7,
			() => PalavrasDeComando.SoPortuguesDeTeste = true, () => PalavrasDeComando.SoPortuguesDeTeste = false,
			$"all -> {Chao.Quanto("all", 7)}");
		Checa("...e o numero, o vazio e a palavra que nao e de comando continuam como eram (3 de 7; vazio = 1; 'todos' = 0)",
			Chao.Quanto("3", 7) == 3 && Chao.Quanto("", 7) == 1 && Chao.Quanto("todos", 7) == 0);

		Console.WriteLine("== 2. o dicionario de verdade ==");
		caminho ??= Path.Combine("Assets", "Data", "lingua-en.json");
		if (!File.Exists(caminho))
		{
			Checa($"o dicionario existe ({caminho})", false);
			return Fim();
		}
		string json = File.ReadAllText(caminho);
		Tradutor real = Tradutor.DoJson(json);
		var textos = new Dictionary<string, string>(StringComparer.Ordinal);
		using (JsonDocument doc = JsonDocument.Parse(json))
			foreach (JsonProperty p in doc.RootElement.GetProperty("textos").EnumerateObject())
				textos[p.Name] = p.Value.GetString() ?? "";
		Console.WriteLine($"  {textos.Count} entradas: {real.Exatas} frases inteiras, {real.Moldes} moldes, {real.MoldesSemAncora.Count} moldes fora do indice");
		Checa("o dicionario tem as frases do jogo (mais de 5 mil)", textos.Count > 5000, textos.Count.ToString());

		var buraco = new Regex(@"(?<!\{)\{(\d+)!?\}(?!\})");
		int pecasErradas = 0;
		var exemplosPecas = new List<string>();
		foreach ((string pt, string en) in textos)
		{
			var dePt = buraco.Matches(pt).Select(m => m.Groups[1].Value).ToHashSet();
			var deEn = buraco.Matches(en).Select(m => m.Groups[1].Value).ToHashSet();
			if (deEn.IsSubsetOf(dePt)) continue;
			pecasErradas++;
			if (exemplosPecas.Count < 5) exemplosPecas.Add($"{pt} => {en}");
		}
		Checa("todo pedaco do ingles existe no portugues", pecasErradas == 0, string.Join(" | ", exemplosPecas));

		// AS PALAVRAS DE COMANDO NO INGLES: quem joga em ingles digita o que o aviso DELE diz
		Regex citaPt = Citada(Citadas.Select(c => c.Pt)), citaEn = Citada(Citadas.Select(c => c.En).Distinct());
		List<string> emPortugues = [.. textos.Values.Where(en => citaPt.IsMatch(en))];
		Checa("nenhum ingles cita a palavra de comando em portugues ('sim', Focus_Skill:nenhuma, \"tudo\"...)", emPortugues.Count == 0,
			string.Join(" | ", emPortugues.Take(4)));
		int citam = 0;
		var semAInglesa = new List<string>();
		foreach ((string pt, string en) in textos)
		{
			List<string> doPortugues = Achadas(citaPt, pt);
			if (doPortugues.Count == 0) continue;
			citam++;
			HashSet<string> doIngles = [.. Achadas(citaEn, en)];
			foreach (string p in doPortugues)
			{
				var c = Citadas.First(x => x.Pt == p);
				if (!doIngles.Contains(c.En) || (c.DoServidor && !PalavrasDeComando.E(c.En, c.Pt))) semAInglesa.Add($"{p}: {en}");
			}
		}
		Checa($"onde o portugues cita uma palavra de comando, o ingles cita a inglesa -- e o servidor a entende ({citam} avisos)",
			citam > 0 && semAInglesa.Count == 0, string.Join(" | ", semAInglesa.Take(4)));

		// A VOLTA: cada entrada, do jeito que o jogo a montaria, tem de sair no ingles DELA
		int exatasCertas = 0, exatas = 0, moldesCertos = 0, moldes = 0;
		var errados = new List<string>();
		foreach ((string pt, string en) in textos)
		{
			if (en == pt) continue;
			bool temBuraco = buraco.IsMatch(pt);
			string montado = Preencher(pt, buraco);
			string esperado = Preencher(en, buraco);
			string saiu = real.Traduzir(montado);
			if (temBuraco)
			{
				if (real.MoldesSemAncora.Contains(pt.Trim())) continue;
				moldes++;
				if (saiu == esperado) moldesCertos++;
				else if (errados.Count < 40) errados.Add($"[molde] {montado}  =>  {saiu}   (esperado: {esperado})");
			}
			else
			{
				exatas++;
				if (saiu == esperado) exatasCertas++;
				else if (errados.Count < 40) errados.Add($"[frase] {montado}  =>  {saiu}   (esperado: {esperado})");
			}
		}
		foreach (string e in errados) Console.WriteLine("    " + e);
		Checa($"toda frase inteira volta no ingles dela ({exatasCertas}/{exatas})", exatasCertas == exatas);
		double razao = moldes == 0 ? 1 : (double)moldesCertos / moldes;
		Checa($"os moldes voltam no ingles deles ({moldesCertos}/{moldes} = {razao:P1}; o resto e molde que casa com o texto de outro)",
			razao >= 0.97);

		Console.WriteLine("== 3. as portas do servidor: o verbo pergunta pela palavra de comando, nao pela portuguesa ==");
		// (a raiz do jogo e a pasta de cima do Assets/ do dicionario lido: na fila e o repo, no rascunho e a sobreposicao)
		string raiz = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(caminho))!, "..", ".."));
		var direta = new Regex(@"(?:Equals\(\s*[\w.]+(?:\.Trim\(\))?\s*,\s*|\.Equals\(\s*|[=!]=\s*)(?:""(?:sim|tudo|nenhuma|parar|sortear|padrao|aleatorio)""|PalavraDeTudo)|""(?:padrao|aleatorio)""\s+or\b");
		int lidos = 0, chamadas = 0;
		var diretas = new List<string>();
		foreach (string pasta in new[] { "Server", "Core" })
		{
			string onde = Path.Combine(raiz, pasta);
			if (!Directory.Exists(onde)) continue;
			foreach (string f in Directory.EnumerateFiles(onde, "*.cs", SearchOption.AllDirectories))
			{
				string nome = Path.GetFileName(f);
				// as bancadas e o robo do trailer mandam a palavra portuguesa de proposito (o "sim" do teste, a opcao do trailer);
				// o obj/ e o bin/ sao do compilador
				if (nome.Contains("Teste") || nome.Contains("Prova") || nome.Contains("Trailer")
					|| Path.GetRelativePath(raiz, f).Split(Path.DirectorySeparatorChar).Any(p => p is "obj" or "bin")) continue;
				lidos++;
				string[] linhas = File.ReadAllLines(f);
				for (int i = 0; i < linhas.Length; i++)
				{
					if (linhas[i].TrimStart().StartsWith("//")) continue;
					if (linhas[i].Contains("PalavrasDeComando.E(")) chamadas++;
					if (direta.IsMatch(linhas[i])) diretas.Add($"{nome}:{i + 1}");
				}
			}
		}
		Checa($"os verbos perguntam pela `PalavrasDeComando` ({chamadas} chamadas em {lidos} arquivos de {raiz})", lidos > 0 && chamadas > 0);
		Checa("...e nenhum compara direto com 'sim', 'tudo', 'nenhuma', 'parar', 'sortear', 'padrao' ou 'aleatorio' (o 'yes' ficaria de fora)",
			diretas.Count == 0, string.Join(", ", diretas));

		Console.WriteLine("== 4. o que o jogo monta pedaco a pedaco: a ficha de cada skill e a fala de cada cinematica ==");
		AsFichas(json);
		AsCinematicas(json);
		AsTarefasDeCargo(json);
		OSsj3DoDm(json);
		return Fim();
	}

	// =====================================================================
	// 4. O QUE O JOGO MONTA PEDACO A PEDACO
	// =====================================================================
	/// <summary>
	/// A FICHA DE CADA SKILL, como a tela a pinta -- o pedido do dono de 2026-10-23: *"falta traduzir tb oq cada nivel de kill
	/// faz"* (o "kill" e "skill": o que cada NIVEL de skill da; o que e letal ja estava no dicionario). A ficha e montada no Core
	/// (`FichaDeSkill.Montar`) colando pedacos, e o molde de fora ("nivel {0}: {1}") estava no dicionario -- os de dentro nao: o
	/// nome do campo com o numero ("ataque fisico +0,4"), a "habilidade nova: Kiai", o "gene de ...". Medido antes do conserto,
	/// com o tradutor de verdade: 294 das 304 linhas por nivel, 403 das 481 da compra, as 59 somas no topo e 272 dos 303 resumos.
	///
	/// A REGUA: cada linha da ficha de cada skill do catalogo de verdade (`skills.json` + `niveis.json`, lidos como o cliente os
	/// le), do jeito que a tela a pinta (o marcador de item ou o recuo da casa da escolha unica -- `MenuJogo.Skills.LinhaDeEfeito` --,
	/// a nota de como sobe, a soma no topo e o resumo da aba Skills), passa pelo tradutor de verdade e nao pode sair parecendo
	/// portugues. Contra-exemplo: os cortes da ficha desligados (`Tradutor.SemOsCortesDaFichaDeTeste`) -- a mesma regua
	/// reprova. Tradutor NOVO a cada medida: ele guarda o que ja traduziu, e o defeito ligado depois leria a memoria.
	/// </summary>
	private static void AsFichas(string json)
	{
		string dados = Path.Combine("Assets", "Data");
		string cs = Path.Combine(dados, "skills.json"), ct = Path.Combine(dados, "skilltrees.json"), cn = Path.Combine(dados, "niveis.json");
		if (!File.Exists(cs) || !File.Exists(ct) || !File.Exists(cn))
		{
			Checa("o catalogo de skills existe (skills.json, skilltrees.json, niveis.json, lidos da raiz do repo)", false);
			return;
		}
		SkillCatalog cat = SkillCatalog.Parse(File.ReadAllText(cs), File.ReadAllText(ct));
		RegrasDoDisco.Carregar(File.ReadAllText(cn));

		var linhas = new List<(string Canal, string Texto)>();
		foreach (Skill s in cat.Todas.Where(s => !s.Arvore).OrderBy(s => s.Path, StringComparer.Ordinal))
		{
			FichaDeSkill.Texto f = FichaDeSkill.Montar(cat, s, RegrasDeNivel.Get(s.Path));
			foreach (string l in f.NaCompra) linhas.Add(("compra", ComoALinhaDeEfeito(l)));
			foreach (string l in f.PorNivel) linhas.Add(("por nivel", ComoALinhaDeEfeito(l)));
			if (f.Progressao.Length > 0) linhas.Add(("como sobe", f.Progressao));
			if (f.NoTopo.Length > 0) linhas.Add(("no topo", f.NoTopo));
			if (f.Resumo.Length > 0)
			{
				linhas.Add(("resumo", "      " + f.Resumo));
				// A DICA DO CARTAO (`MenuJogo.Skills`): nome, descricao e o resumo, numa linha so -- o resumo desce um andar a mais
				// la dentro. So o pedaco do resumo e julgado (ver `NoResumoDaDica`): a descricao e a do DM, em ingles de fabrica.
				linhas.Add(("dica", s.Nome + (s.Desc.Length > 0 ? "\n" + s.Desc : "") + "\n\n" + f.Resumo));
			}
		}
		string[] canais = ["compra", "por nivel", "como sobe", "no topo", "resumo", "dica"];

		List<(string Canal, string Texto, string Saiu)> Sobras()
		{
			Tradutor t = Tradutor.DoJson(json);
			return [.. linhas.Select(l => (l.Canal, l.Texto, Saiu: NoResumoDaDica(l.Canal, t.Traduzir(l.Texto)))).Where(l => t.ParecePortugues(l.Saiu))];
		}

		List<(string Canal, string Texto, string Saiu)> agora = Sobras();
		foreach (string c in canais)
			Console.WriteLine($"  {c}: {agora.Count(x => x.Canal == c)} de {linhas.Count(x => x.Canal == c)} linhas ainda em portugues");
		foreach (var x in agora.Take(25)) Console.WriteLine($"    [{x.Canal}] {x.Texto.Trim()}  =>  {x.Saiu.Trim()}");
		ComContraExemplo($"toda linha da ficha de toda skill sai em ingles ({linhas.Count} linhas de {cat.Todas.Count(s => !s.Arvore)} skills)",
			() => linhas.Count > 300 && Sobras().Count == 0,
			() => Tradutor.SemOsCortesDaFichaDeTeste = true, () => Tradutor.SemOsCortesDaFichaDeTeste = false,
			$"{agora.Count} em portugues");

		// (2026-10-24) A CASA DA RACA ("se a raça for Yardrat: ..."), que a regua de cima deixava passar: o "se for {0}: {1}" de antes nao
		// tinha letra fixa pra ser molde forte, o corte por pedacos deixava o "se for Yardrat" em portugues -- e o `ParecePortugues` nao o
		// via ("for" e palavra inglesa). O dono viu no print de um jogador (o Instant Transmission). A regua: nenhuma linha da ficha com a
		// casa da raca sai com o portugues dela. Contra-exemplo: o dicionario sem a entrada da casa.
		var daRaca = linhas.Where(l => l.Texto.Contains("se a raça for ", StringComparison.Ordinal)).ToList();
		string semACasa = Regex.Replace(json, "\n  \"se a raça for \\{0\\}: \\{1\\}\": \"[^\"]*\",", "");
		string dicDaCasa = json;
		ComContraExemplo($"a casa da raca da ficha ('se a raça for Yardrat: ...') sai em ingles ({daRaca.Count} linhas)",
			() =>
			{
				Tradutor t = Tradutor.DoJson(dicDaCasa);
				return daRaca.Count > 0 && semACasa != json
					&& daRaca.All(l => !Regex.IsMatch(t.Traduzir(l.Texto), "\\bse (a raça )?for\\b"));
			},
			() => dicDaCasa = semACasa, () => dicDaCasa = json,
			string.Join(" | ", daRaca.Take(3).Select(l => Tradutor.DoJson(json).Traduzir(l.Texto).Trim())));

		// AS CONDICOES DE EXP TEM NOME DE GENTE: a que cai no `default` do `NomesLegiveis.Estado` sai com o nome do codigo nas duas
		// linguas ("Ganha experiencia: ComLaminaDeKi antes do nivel 1") -- as quatro do adendo 18 sairam assim de 2026-10-16 a 2026-10-23
		RegraDeNivel.Estado[] estados = Enum.GetValues<RegraDeNivel.Estado>();
		string[] crus = [.. estados.Where(e => NomesLegiveis.Estado(e) == e.ToString()).Select(e => e.ToString())];
		Checa($"toda condicao de exp da ficha tem nome de gente, e nao o do codigo ({estados.Length} condicoes)", crus.Length == 0,
			  string.Join(", ", crus));
	}

	/// <summary>Da dica traduzida, so o resumo (o que vem depois da linha em branco); das outras linhas, a linha inteira.</summary>
	private static string NoResumoDaDica(string canal, string saiu) =>
		canal == "dica" && saiu.LastIndexOf("\n\n", StringComparison.Ordinal) is int i and >= 0 ? saiu[(i + 2)..] : saiu;

	/// <summary>A linha de efeito como a ficha a pinta: o marcador, ou o recuo da casa da escolha unica (`MenuJogo.Skills.LinhaDeEfeito`).</summary>
	private static string ComoALinhaDeEfeito(string linha) =>
		linha.StartsWith("    ", StringComparison.Ordinal) ? "      " + linha.TrimStart() : "• " + linha;

	/// <summary>
	/// 4b. AS TAREFAS DE CARGO (dono, 2026-10-24: *"reescreva essas quests de rank de uma forma mais entendivel"*, com o print de um
	/// jogador que joga em ingles). Todo aviso de tarefa que o servidor manda (`MissoesDeCargo`, no Core) -- cada tipo de tarefa, as duas
	/// vocacoes do servico, o cargo maligno e o que nao e -- passa pelo tradutor de verdade e nao pode sobrar portugues: nem pela regua
	/// de sempre, nem pelas palavras das tarefas que ela nao conhece ("fique", "mate-o", "prazo", "falha", "cargo"). Contra-exemplo: o
	/// dicionario sem as linhas das tarefas (as que comecam pelos verbos delas) reprova a mesma regua.
	/// </summary>
	private static void AsTarefasDeCargo(string json)
	{
		var avisos = new List<string>();
		foreach (Jandirus.Core.Ranks.TipoDeTarefa tipo in Enum.GetValues<Jandirus.Core.Ranks.TipoDeTarefa>())
		{
			if (tipo == Jandirus.Core.Ranks.TipoDeTarefa.Nenhuma) continue;
			var f = new Jandirus.Core.Ranks.FichaDeMissao
			{
				Tarefa = tipo, Planeta = "Earth", AlvoNome = "Freeza", Progresso = 4, Meta = 15, Falhas = 1, Cumpridas = 2,
			};
			foreach (bool outroMundo in new[] { false, true })
				foreach (bool maligno in new[] { false, true })
				{
					avisos.AddRange(Jandirus.Core.Ranks.MissoesDeCargo.AvisoDeTarefaNova("Crane Hermit", f, outroMundo, maligno));
					avisos.Add(Jandirus.Core.Ranks.MissoesDeCargo.AvisoDeCumprida("Crane Hermit",
						Jandirus.Core.Ranks.MissoesDeCargo.NomeCurto(f, outroMundo), f, maligno,
						Jandirus.Core.Ranks.MissoesDeCargo.TextoDeRenome(f.Cumpridas, !maligno)));
				}
			avisos.Add(Jandirus.Core.Ranks.MissoesDeCargo.AvisoDeServico("Crane Hermit", f));
			avisos.Add(Jandirus.Core.Ranks.MissoesDeCargo.AvisoDeFalhada("Crane Hermit", f));
		}
		avisos.Add(Jandirus.Core.Ranks.MissoesDeCargo.AvisoDeAlvoQueSaiu("Crane Hermit"));
		avisos.Add(Jandirus.Core.Ranks.MissoesDeCargo.AvisoDeAscensao("North Kai", Jandirus.Core.Ranks.MissoesDeCargo.TextoDosDegraus("nkai")));
		avisos = [.. avisos.Distinct()];

		Regex dasTarefas = new(@"\b(ou|fique|jogadores|mate-o|prazo|falhas?|cargo|tarefas?|cumprir|cumprida|apaga|pontos|almas|treinando|vivos?|mortos|libertar|financiar|deposite|planeta|renome|reivindique)\b",
							   RegexOptions.IgnoreCase);
		List<string> Sobras(string dic)
		{
			Tradutor t = Tradutor.DoJson(dic);
			return [.. avisos.Select(t.Traduzir).Where(s => t.ParecePortugues(s) || dasTarefas.IsMatch(s))];
		}
		// o dicionario sem as linhas das tarefas: as chaves que comecam pelos verbos e pelas molduras delas
		string semAsTarefas = Regex.Replace(json, @"\n  ""(\{0\}: NOVA TAREFA|Prazo: \{0\}\. Se o prazo|Cumprir paga|ATENDER ALMAS|ENSINAR: estando|CAÇAR o vilão \{0\}:|ESMAGAR o herói \{0\}:|LIBERTAR \{0\}:|FINANCIAR a Terra)[^\n]*", "");
		string dicDasTarefas = json;
		List<string> agora = Sobras(json);
		foreach (string s in agora.Take(8)) Console.WriteLine($"    [tarefa] {s}");
		ComContraExemplo($"4b. todo aviso de tarefa de cargo sai em ingles ({avisos.Count} avisos: cada tipo, as duas vocacoes, maligno ou nao)",
			() => avisos.Count > 20 && semAsTarefas != json && Sobras(dicDasTarefas).Count == 0,
			() => dicDasTarefas = semAsTarefas, () => dicDasTarefas = json,
			$"{agora.Count} com portugues");
	}

	/// <summary>
	/// A FALA E A NARRACAO DE CADA CINEMATICA, como a tela as mostra -- o pedido do dono de 2026-10-23: *"e tb a cinematica do ssj3"*.
	/// A fala vira BALAO (o texto traduzido inteiro, `Transformacao.Disparar`) e linha de chat ("Voce: ..." pra quem virou, "Fulano:
	/// ..." pra quem assiste); a narracao vira "* " + o texto, traduzido inteiro pelo `Chat.Sistema`. O "* " e o ponto final
	/// escondiam a frase do dicionario (a chave tem o ponto, a borda o tirava) -- as narracoes de todas as cenas saiam em
	/// portugues. Contra-exemplo: a borda que come a pontuacao da frase (`Tradutor.BordaComeAPontuacaoDeTeste`). O BALAO em si
	/// (que antes recebia a fala crua) e do cliente: quem o prova e o robo `--diaglingua`.
	/// </summary>
	private static void AsCinematicas(string json)
	{
		var cenas = typeof(Cinematicas).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
			.Where(f => f.FieldType == typeof(Cinematica)).Select(f => (Nome: f.Name, Cena: (Cinematica)f.GetValue(null)!)).ToList();

		(int Falas, int Narras, List<string> Sobras) Medir()
		{
			Tradutor t = Tradutor.DoJson(json);
			int falas = 0, narras = 0;
			var sobras = new List<string>();
			foreach ((string nome, Cinematica c) in cenas)
				foreach (Beat b in c.Beats)
				{
					if (b.Fala.Length > 0)
					{
						falas++;
						foreach (string tela in new[] { t.Traduzir(b.Fala), t.Traduzir("Você: " + b.Fala), t.Traduzir("Zyx: " + b.Fala) })
							if (t.ParecePortugues(tela)) sobras.Add($"{nome}: {tela}");
					}
					if (b.Narra.Length > 0)
					{
						narras++;
						string tela = t.Traduzir("* " + b.Narra);
						if (t.ParecePortugues(tela)) sobras.Add($"{nome}: {tela}");
					}
				}
			return (falas, narras, sobras);
		}

		(int falas, int narras, List<string> agora) = Medir();
		foreach (string x in agora.Take(25)) Console.WriteLine("    " + x);
		ComContraExemplo($"toda fala e toda narracao das {cenas.Count} cinematicas sai em ingles no balao e no chat ({falas} falas, {narras} narracoes)",
			() => Medir() is var m && cenas.Count >= 50 && m.Falas > 0 && m.Narras > 100 && m.Sobras.Count == 0,
			() => Tradutor.BordaComeAPontuacaoDeTeste = true, () => Tradutor.BordaComeAPontuacaoDeTeste = false,
			$"{agora.Count} em portugues");
	}

	/// <summary>
	/// (2026-10-25) AS FALAS DO SSJ3 EM INGLES SAO AS DO DM -- onde o original tem o texto em ingles, o jogo em ingles diz o dele, e nao uma
	/// traducao de volta do portugues. Quatro linhas da cena (`SSJ3Cinematic.dm`): a fala do ascendido (`:32`, com o "what as known" do DM
	/// acertado pra "what is known"), os punhos (`:52`), o grito (`:65`) e o oceano (`:132`) -- o nome de quem vira e os pontos de
	/// exclamacao seguem a cena do port (a narracao ja traz o nome na frente; "deste poder" e o "[src]'s immense power" do DM). As quatro
	/// sao conferidas pela chave que a cena DE VERDADE usa (o beat do `Cinematicas`), e nao por uma copia da frase. Contra-exemplo: o
	/// dicionario com os quatro ingleses de antes (traducoes livres do portugues) -- a mesma regua reprova.
	/// </summary>
	private static void OSsj3DoDm(string json)
	{
		(string Pt, string Dm, string Antes)[] quatro =
		[
			("E isto... isto é o que se conhece como um Super Saiyajin que ascendeu além de um Super Saiyajin.",
			 "And this... this is what is known as a Super Saiyan that has ascended past a Super Saiyan.",
			 "And this... this is what's known as a Super Saiyan who has ascended past a Super Saiyan."),
			("se encolhe e fecha os punhos ao lado do corpo!", "leans inward and pumps their fists next to their sides!",
			 "hunches down and clenches their fists at their sides!"),
			("grita, e uma quantidade inacreditável de energia se solta.", "screams as they release an unbelievable amount of energy!",
			 "screams, unleashing an unbelievable amount of energy."),
			("o próprio oceano se afasta deste poder!", "the ocean itself is curling away from this immense power!",
			 "the ocean itself recoils from this power!"),
		];
		var beats = typeof(Cinematicas).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
			.Where(f => f.FieldType == typeof(Cinematica)).SelectMany(f => ((Cinematica)f.GetValue(null)!).Beats).ToList();
		string antes = json;
		foreach (var (_, dm, velho) in quatro) antes = antes.Replace("\"" + dm + "\"", "\"" + velho + "\"");
		string dic = json;
		bool Regua() => Tradutor.DoJson(dic) is var t && quatro.All(q => t.Traduzir(q.Pt) == q.Dm && beats.Any(b => b.Fala == q.Pt || b.Narra == q.Pt));
		Tradutor agora = Tradutor.DoJson(json);
		ComContraExemplo("4c. AS FALAS DO SSJ3 EM INGLES SAO AS DO DM (`SSJ3Cinematic.dm`): o ascendido, os punhos, o grito e o oceano -- e as quatro sao chaves da cena de verdade",
			Regua, () => dic = antes, () => dic = json, string.Join(" | ", quatro.Select(q => agora.Traduzir(q.Pt))));
	}

	/// <summary>
	/// AS PALAVRAS DE COMANDO QUE OS AVISOS CITAM, e a inglesa que o aviso em ingles cita no lugar. `DoServidor`: o verbo
	/// pergunta pela `PalavrasDeComando`, e a bancada confere que ela entende a inglesa. As outras tres o servidor ja
	/// entendia por conta propria: o `Go_To_Heaven_Or_Hell` compara com "heaven"/"hell", e a invasao aceita o que comeca
	/// com "forc".
	/// </summary>
	private static readonly (string Pt, string En, bool DoServidor)[] Citadas =
	[
		("sim", "yes", true), ("tudo", "all", true), ("nenhuma", "none", true), ("parar", "stop", true),
		("sortear", "random", true), ("padrao", "default", true), ("aleatorio", "random", true),
		("ceu", "heaven", false), ("inferno", "hell", false), ("forca", "force", false),
	];

	/// <summary>
	/// A palavra CITADA como comando: entre aspas ou crases, colada no verbo (`Focus_Skill:nenhuma`), na linha de uso
	/// (`admin_modo_racas padrao | aleatorio`) ou na coluna da ajuda do terminal. A palavra solta numa frase nao conta:
	/// "nenhuma bomba respondeu" e prosa, e o "inferno verde" do ingles e ingles.
	/// </summary>
	private static Regex Citada(IEnumerable<string> palavras)
	{
		string w = string.Join("|", palavras);
		return new Regex($@"['""`]({w})['""`]|\w:({w})(?![^\W\d_])|admin_modo_racas ({w})(?![^\W\d_])|\| ({w})(?![^\W\d_])|^ {{2,}}({w}) {{2,}}",
						 RegexOptions.Multiline);
	}

	private static List<string> Achadas(Regex citada, string texto) =>
		[.. citada.Matches(texto).Select(m => m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value)];

	/// <summary>O molde montado como o jogo montaria: cada pedaco vira um nome que nenhum dicionario tem.</summary>
	private static string Preencher(string molde, Regex buraco)
	{
		string r = buraco.Replace(molde, m => "Zyx" + m.Groups[1].Value);
		return r.Replace("{{", "{").Replace("}}", "}");
	}

	private static int Fim()
	{
		Console.WriteLine($"=== {_ok} OK, {_falhou} FALHA ===");
		return _falhou == 0 ? 0 : 1;
	}
}
