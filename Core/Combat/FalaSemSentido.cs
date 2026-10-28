using System.Text;

namespace Jandirus.Core.Combat;

/// <summary>
/// A FALA SEM SENTIDO DE QUEM ESTA COM O CEREBRO FERIDO (dono, 2026-10-27, o pedido 127): *"e se o brain health estiver em 40% ou menos"*,
/// com a ideia do jogador gingo -- as palavras de LIGACAO ficam (*"Opperative words shouldnt be translated"*), pra frase manter a estrutura e
/// so virar nonsense: *"Hey, Nova, How's the training Going?"* vira algo como *"Hey, Nova, Why are Goats Flying?"*.
///
/// Palavra a palavra: fica a palavra de ligacao (artigo, pronome, preposicao, conjuncao, verbo auxiliar, pergunta, saudacao), fica o NOME de
/// quem esta no mundo (quem chama passa a lista), fica a palavra de ate duas letras e o som sem vogal ("hmm", "kkk"); o resto vira uma
/// palavra boba da MESMA LINGUA da frase, com o mesmo jeito do fim (o "-ing" vira outro "-ing", o "-ndo" outro "-ndo", o plural outro plural)
/// e a mesma caixa (Maiuscula, MAIUSCULAS ou minusculas). Pontuacao, numeros e espacos passam como estao. O sorteio sai da propria fala,
/// como o da <see cref="Gagueira"/>: o mesmo texto sai igual -- e o que deixa a bancada ler o resultado. Quem decide QUANDO e o servidor
/// (`GameServer.Falar`: a fala digitada de boca, de pe ou caido -- a escolha do dono).
/// </summary>
public static class FalaSemSentido
{
	/// <summary>Daqui pra baixo (fracao de vida do cerebro, 0..1) a fala perde o sentido -- o "40% ou menos" do dono.</summary>
	public const double CerebroConfuso = 0.40;

	/// <summary>Um cerebro nesta fracao de vida troca as palavras?</summary>
	public static bool Confunde(double fracaoDoCerebro) => fracaoDoCerebro <= CerebroConfuso;

	/// <summary>
	/// A FALA SEM SENTIDO: as palavras de conteudo trocadas, as de ligacao e os <paramref name="nomes"/> no lugar. A lingua e a da frase
	/// (<see cref="EhIngles"/>).
	/// </summary>
	public static string Embaralhar(string texto, IEnumerable<string>? nomes = null)
	{
		if (string.IsNullOrEmpty(texto)) return texto;

		var guardados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (nomes != null)
			foreach (string nome in nomes)
				foreach ((int ini, int fim) in Palavras(nome)) guardados.Add(nome[ini..fim]);

		List<(int Ini, int Fim)> palavras = Palavras(texto);
		bool ingles = EhIngles(texto, palavras);
		ulong s = Gagueira.Semente(texto);

		var sb = new StringBuilder(texto.Length + 16);
		int feito = 0;
		foreach ((int ini, int fim) in palavras)
		{
			sb.Append(texto, feito, ini - feito);
			string palavra = texto[ini..fim];
			sb.Append(Fica(palavra, guardados) ? palavra : NaCaixaDe(palavra, Trocar(palavra, ingles, ref s)));
			feito = fim;
		}
		sb.Append(texto, feito, texto.Length - feito);
		return sb.ToString();
	}

	/// <summary>A lingua que a troca usa pra esta frase: ingles? (Ver <see cref="EhIngles"/>; a bancada `--cabecateste` le por aqui.)</summary>
	public static bool FraseEmIngles(string texto) => EhIngles(texto, Palavras(texto));

	/// <summary>
	/// A FRASE E EM INGLES? Pelos sinais de cada lingua: a palavra de ligacao que so uma delas tem vale dois, o acento e o cedilha valem dois
	/// pro portugues, e o fim tipico vale um ("-ing", "-ly", "w", "y" pro ingles; "-ndo", "-mente", "nh", "lh", "-ao" pro portugues). Empate e
	/// portugues, a lingua da casa.
	/// </summary>
	internal static bool EhIngles(string texto, List<(int Ini, int Fim)> palavras)
	{
		int en = 0, pt = 0;
		foreach ((int ini, int fim) in palavras)
		{
			string p = Chave(texto[ini..fim]);
			bool ligaEn = LigacaoEn.Contains(p), ligaPt = LigacaoPt.Contains(p);
			if (ligaEn && !ligaPt) en += 2;
			if (ligaPt && !ligaEn) pt += 2;
			if (p.IndexOfAny(Acentos) >= 0) pt += 2;
			if (p.EndsWith("ing") || p.EndsWith("ed") || p.EndsWith("ly") || p.EndsWith("th") || p.EndsWith("tion") || p.Contains('w') || p.Contains('y')) en++;
			if (p.EndsWith("ndo") || p.EndsWith("mente") || p.Contains("nh") || p.Contains("lh") || p.EndsWith("ao") || p.EndsWith("oes")) pt++;
		}
		return en > pt;
	}

	/// <summary>Esta palavra fica como esta? A de ligacao, o nome, a curta e o som sem vogal (o riso, o "hmm").</summary>
	private static bool Fica(string palavra, HashSet<string> guardados)
	{
		if (palavra.Length <= 2 || guardados.Contains(palavra)) return true;
		string p = Chave(palavra);
		if (LigacaoEn.Contains(p) || LigacaoPt.Contains(p)) return true;
		if (!p.Any(c => "aeiouyáàâãéêíóôõú".IndexOf(c) >= 0)) return true;
		// O RISO COM VOGAL: "haha", "hehe", "huehue" -- tirado o h, so vogal
		return p.Count(c => c == 'h') >= 2 && p.Where(c => c != 'h').All(c => "aeiu".IndexOf(c) >= 0);
	}

	/// <summary>A palavra boba que entra no lugar: a da lingua da frase, com o jeito do fim da original.</summary>
	private static string Trocar(string palavra, bool ingles, ref ulong s)
	{
		string p = palavra.ToLowerInvariant();
		string[] lista;
		string sufixo = "";
		if (ingles)
			lista = p.Length >= 5 && p.EndsWith("ing") ? IngEn
				: p.Length >= 4 && p.EndsWith("ed") ? EdEn
				: p.Length >= 4 && p.EndsWith("ly") ? LyEn
				: p.Length >= 4 && p.EndsWith('s') && !p.EndsWith("ss") ? PluralEn
				: BaseEn;
		else if (p.Length >= 6 && (p.EndsWith("ados") || p.EndsWith("idos"))) { lista = ParticipioM; sufixo = "s"; }
		else if (p.Length >= 6 && (p.EndsWith("adas") || p.EndsWith("idas"))) { lista = ParticipioF; sufixo = "s"; }
		else
			lista = p.Length >= 5 && p.EndsWith("ndo") ? Gerundio
				: p.Length >= 7 && p.EndsWith("mente") ? Adverbio
				: p.Length >= 5 && (p.EndsWith("ado") || p.EndsWith("ido")) ? ParticipioM
				: p.Length >= 5 && (p.EndsWith("ada") || p.EndsWith("ida")) ? ParticipioF
				: p.Length >= 4 && (p.EndsWith("ar") || p.EndsWith("er") || p.EndsWith("ir")) ? Infinitivo
				: p.Length >= 4 && p.EndsWith('s') ? PluralPt
				: SingularPt;
		string troca = lista[(int)(Gagueira.Proximo(ref s) % (ulong)lista.Length)];
		// a mesma palavra de volta nao seria troca: mais um sorteio
		if (troca == p) troca = lista[(int)(Gagueira.Proximo(ref s) % (ulong)lista.Length)];
		return troca + sufixo;
	}

	/// <summary>A troca na caixa da original: MAIUSCULAS, Maiuscula ou minusculas.</summary>
	private static string NaCaixaDe(string original, string troca)
	{
		if (original.Where(char.IsLetter).All(char.IsUpper)) return troca.ToUpperInvariant();
		if (char.IsUpper(original[0])) return char.ToUpperInvariant(troca[0]) + troca[1..];
		return troca;
	}

	/// <summary>
	/// AS PALAVRAS DO TEXTO: as corridas de letras, com o apostrofo do meio dentro ("How's", "don't") -- o resto (espaco, pontuacao, numero)
	/// fica entre elas e passa igual.
	/// </summary>
	internal static List<(int Ini, int Fim)> Palavras(string texto)
	{
		var l = new List<(int, int)>();
		for (int i = 0; i < texto.Length;)
		{
			if (!char.IsLetter(texto[i])) { i++; continue; }
			int ini = i;
			while (i < texto.Length && (char.IsLetter(texto[i]) || (texto[i] is '\'' or '’' && i + 1 < texto.Length && char.IsLetter(texto[i + 1])))) i++;
			l.Add((ini, i));
		}
		return l;
	}

	/// <summary>A palavra como as listas a guardam: minuscula e com o apostrofo reto.</summary>
	private static string Chave(string palavra) => palavra.Replace('’', '\'').ToLowerInvariant();

	private static readonly char[] Acentos = "áàâãéêíóôõúç".ToCharArray();

	// =====================================================================
	// AS PALAVRAS DE LIGACAO -- ficam no lugar e seguram a estrutura da frase
	// =====================================================================
	private static readonly HashSet<string> LigacaoEn = new(StringComparer.Ordinal)
	{
		"a", "an", "the", "and", "or", "but", "nor", "if", "so", "because", "as", "than", "then", "that", "this", "these", "those",
		"of", "to", "in", "on", "at", "by", "for", "with", "from", "about", "into", "onto", "over", "under", "up", "down", "out", "off",
		"like", "through", "after", "before", "around",
		"i", "me", "my", "mine", "myself", "you", "your", "yours", "he", "him", "his", "she", "her", "hers", "it", "its", "we", "us", "our",
		"ours", "they", "them", "their", "theirs", "who", "whom", "whose", "what", "which", "where", "when", "why", "how", "there", "here",
		"is", "am", "are", "was", "were", "be", "been", "being", "do", "does", "did", "have", "has", "had", "will", "would", "can", "could",
		"shall", "should", "may", "might", "must", "not", "no", "yes", "yeah", "yep", "nope", "ok", "okay", "hey", "hi", "hello", "oh",
		"wow", "please", "thanks", "lol", "just", "very", "too", "also", "all", "some", "any",
		"i'm", "i'll", "i've", "i'd", "you're", "you'll", "you've", "it's", "that's", "what's", "how's", "where's", "who's", "there's",
		"let's", "don't", "doesn't", "didn't", "can't", "won't", "isn't", "aren't", "wasn't", "weren't", "he's", "she's", "we're",
		"they're", "gonna", "wanna",
	};

	private static readonly HashSet<string> LigacaoPt = new(StringComparer.Ordinal)
	{
		"o", "a", "os", "as", "um", "uma", "uns", "umas", "e", "ou", "mas", "se", "que", "porque", "pq", "como", "quando", "onde", "quem",
		"qual", "quais", "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas", "num", "numa", "por", "pelo", "pela", "pelos",
		"pelas", "para", "pra", "pro", "pras", "pros", "com", "sem", "sobre", "entre", "até", "ate", "ao", "aos", "à", "às",
		"eu", "tu", "você", "voce", "vc", "ele", "ela", "nós", "vocês", "voces", "vcs", "eles", "elas", "me", "te", "lhe", "lhes", "comigo",
		"contigo", "conosco", "meu", "minha", "meus", "minhas", "teu", "tua", "teus", "tuas", "seu", "sua", "seus", "suas", "nosso", "nossa",
		"nossos", "nossas", "dele", "dela", "deles", "delas", "isso", "isto", "aquilo", "esse", "essa", "esses", "essas", "este", "esta",
		"estes", "estas", "aquele", "aquela", "aqui", "aí", "ai", "ali", "lá", "la",
		"é", "eh", "não", "nao", "sim", "foi", "era", "ser", "sou", "são", "sao", "está", "tá", "ta", "tô", "to", "estou", "estão", "tão",
		"tem", "têm", "tenho", "ter", "vai", "vou", "vamos", "vão", "já", "ja", "mais", "menos", "muito", "muita", "bem", "também",
		"tambem", "tb", "só", "so", "então", "entao", "tipo", "né", "ne", "oi", "olá", "ola", "ei", "opa", "eai", "ok", "todo", "toda",
		"todos", "todas", "tudo", "nada", "algo", "cada", "outro", "outra", "mesmo", "ainda", "agora", "sempre", "nunca",
	};

	// =====================================================================
	// AS PALAVRAS BOBAS -- por lingua e pelo jeito do fim
	// =====================================================================
	private static readonly string[] IngEn =
	[
		"flying", "dancing", "sneezing", "juggling", "wiggling", "bouncing", "singing", "melting", "swimming", "giggling", "tickling",
		"spinning", "baking", "yodeling", "wobbling", "sparkling", "sniffing", "hopping", "quacking", "snoring",
	];

	private static readonly string[] EdEn =
	[
		"danced", "sneezed", "juggled", "wiggled", "bounced", "melted", "giggled", "tickled", "baked", "yodeled", "wobbled", "sparkled",
		"sniffed", "hopped", "quacked", "exploded", "snored", "pickled",
	];

	private static readonly string[] LyEn =
	[
		"fluffily", "sneakily", "clumsily", "sleepily", "crunchily", "gloriously", "suspiciously", "bouncily", "noisily", "grumpily",
		"wobbily", "soggily",
	];

	private static readonly string[] PluralEn =
	[
		"goats", "bananas", "potatoes", "clouds", "spoons", "pickles", "ducks", "wizards", "toasters", "noodles", "penguins", "socks",
		"turnips", "waffles", "llamas", "buckets", "pancakes", "hamsters", "teapots", "carrots", "walruses", "muffins", "kettles",
	];

	private static readonly string[] BaseEn =
	[
		"goat", "banana", "potato", "cloud", "spoon", "pickle", "duck", "wizard", "toaster", "noodle", "penguin", "cactus", "sock", "turnip",
		"waffle", "llama", "bucket", "moon", "pancake", "hamster", "teapot", "carrot", "walrus", "umbrella", "muffin", "kettle", "cheese",
		"dance", "sneeze", "juggle", "wiggle", "bounce", "melt", "yodel", "wobble", "sniff", "hop", "quack",
	];

	private static readonly string[] Gerundio =
	[
		"voando", "dançando", "espirrando", "rebolando", "quicando", "cantando", "derretendo", "nadando", "gargalhando", "girando",
		"assando", "latindo", "tropeçando", "cochilando", "saltitando", "fungando", "pulando", "borbulhando", "miando", "cacarejando",
	];

	private static readonly string[] Adverbio =
	[
		"fofamente", "desajeitadamente", "cremosamente", "misteriosamente", "sonolentamente", "crocantemente", "gloriosamente",
		"suspeitosamente", "molengamente", "barulhentamente", "rabugentamente",
	];

	private static readonly string[] Infinitivo =
	[
		"voar", "dançar", "espirrar", "rebolar", "quicar", "cantar", "derreter", "nadar", "gargalhar", "girar", "assar", "latir",
		"tropeçar", "cochilar", "saltitar", "fungar", "pular", "borbulhar", "miar", "cacarejar",
	];

	private static readonly string[] ParticipioM =
	[
		"derretido", "amassado", "empanado", "enrolado", "cozido", "espremido", "açucarado", "entortado", "embolado", "chamuscado",
		"esfarelado", "melado",
	];

	private static readonly string[] ParticipioF =
	[
		"derretida", "amassada", "empanada", "enrolada", "cozida", "espremida", "açucarada", "entortada", "embolada", "chamuscada",
		"esfarelada", "melada",
	];

	private static readonly string[] PluralPt =
	[
		"bananas", "cabras", "batatas", "nuvens", "colheres", "pepinos", "patos", "magos", "pinguins", "cactos", "meias", "nabos", "lhamas",
		"baldes", "panquecas", "bules", "cenouras", "morsas", "bolinhos", "chaleiras", "pedras", "peixes", "vassouras", "queijos",
		"abacaxis", "galinhas", "sapos", "tamancos",
	];

	private static readonly string[] SingularPt =
	[
		"banana", "cabra", "batata", "nuvem", "colher", "pepino", "pato", "mago", "torradeira", "pinguim", "cacto", "meia", "nabo", "lhama",
		"balde", "lua", "panqueca", "bule", "cenoura", "morsa", "bolinho", "chaleira", "pedra", "peixe", "vassoura", "queijo", "abacaxi",
		"galinha", "sapo", "tamanco",
	];
}
