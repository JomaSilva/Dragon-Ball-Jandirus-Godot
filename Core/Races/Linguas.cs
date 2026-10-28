using System.Text;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Races;

/// <summary>
/// AS LINGUAS DO SAY (2026-10-27) -- os pedidos 118 e 121 do dono: *"no say cada raça tenha sua lingua nativa, e caso um jogador fale com
/// algum jogador q nao entende sua lingua nativa, o jogo vai embaralhar [...] lingua teria tipo uma maestria, indo de 0% entendendo NADA,
/// ate 100% q vc entende tudo, as palavras vao ficando gradualmente menos confusas [...] a lingua comum galática q todas as raças sabem [...]
/// a lingua dos deuses [...] demora 3x mais [...] vc pode ensinar um outro jogador sua lingua, ele ganha automaticamente 10% de maestria e
/// um bonus de velocidade de aprendizado"*; e *"so poder usar as esferas de Namek aqueles q sabem pelomenos 75% da língua dos
/// Namekuseijins"*. As escolhas dele: uma lingua por raca (as criadas falam a de quem as cria, o mestico sabe as duas), ~2.000 falas pra
/// chegar a 100%, e o ensino da +10% e 2x pra sempre, uma vez por lingua.
///
/// TUDO AQUI E FUNCAO PURA DA FICHA: quem ouve, quem fala e quem ensina sao o servidor (`GameServer.Linguas.cs`), e quem desenha e o
/// cliente. A maestria das linguas APRENDIDAS mora na ficha (<see cref="Fighter.Linguas"/>, que vai pro save); a nativa, a Galactica e a dos
/// Deuses de quem tem a marca (<see cref="Fighter.godtongue"/>) valem 100% sem estar la.
/// </summary>
public static class Linguas
{
	/// <summary>A lingua comum galactica: todas as racas a sabem.</summary>
	public const string Galactica = "Galactic";

	/// <summary>A lingua dos Deuses: quem tem a marca (`godtongue`) a sabe; os outros a aprendem ouvindo, 3x mais devagar.</summary>
	public const string DosDeuses = "Gods";

	/// <summary>A lingua dos Namekuseijins -- a que as esferas de Namek pedem (o pedido 121).</summary>
	public const string Namekuseijin = "Namekian";

	/// <summary>Quanto uma fala OUVIDA ensina: +0,05% (a escolha do dono, ~2.000 falas pra chegar a 100%).</summary>
	public const double GanhoPorFala = 0.0005;

	/// <summary>A lingua dos Deuses se aprende ouvindo 3x mais devagar (o pedido 118).</summary>
	public const double RitmoDosDeuses = 1.0 / 3;

	/// <summary>Quem foi ensinado ganha isto na hora (o pedido 118: "automaticamente 10% de maestria").</summary>
	public const double BonusDoEnsino = 0.10;

	/// <summary>...e aprende aquela lingua 2x mais rapido, pra sempre (a escolha do dono).</summary>
	public const double RitmoDoEnsinado = 2;

	/// <summary>O quanto da lingua namekuseijin as esferas de Namek pedem (o pedido 121).</summary>
	public const double MaestriaDasEsferasDeNamek = 0.75;

	/// <summary>A lingua nativa de uma raca: uma por raca, e as criadas falam a de quem as cria (Android e Bio-Android a humana, Saibaman a saiyajin).</summary>
	public static string NativaDaRaca(string raca) => raca switch
	{
		"Android" or "BioAndroid" => "Human",
		"Saibaman" => "Saiyan",
		_ => raca,
	};

	/// <summary>As linguas nativas de um corpo: a da raca -- e o meio-Saiyajin nasce sabendo as duas (a saiyajin e a humana).</summary>
	public static IEnumerable<string> Nativas(Fighter f)
	{
		if (f.Race == Forms.Catalogo.RacaMeioSaiyajin)
		{
			yield return "Saiyan";
			yield return "Human";
			yield break;
		}
		yield return NativaDaRaca(f.Race);
	}

	/// <summary>QUANTO DESTA LINGUA O CORPO ENTENDE, de 0 a 1: a Galactica e a nativa inteiras, a dos Deuses de quem tem a marca, e o resto do que aprendeu.</summary>
	public static double Maestria(Fighter f, string lingua)
	{
		if (lingua == Galactica) return 1;
		if (lingua == DosDeuses && f.godtongue) return 1;
		foreach (string n in Nativas(f))
			if (n == lingua) return 1;
		return Math.Clamp(f.Linguas.GetValueOrDefault(lingua), 0, 1);
	}

	/// <summary>O corpo pode FALAR nesta lingua (o botao do chat a oferece)? So a que ele sabe inteira.</summary>
	public static bool PodeFalar(Fighter f, string lingua) => Maestria(f, lingua) >= 1;

	/// <summary>
	/// O OUVIDO: uma fala ouvida nesta lingua ensina um pouco -- <see cref="GanhoPorFala"/>, 3x menos na dos Deuses e 2x mais na que alguem
	/// ensinou. Devolve a maestria de depois.
	/// </summary>
	public static double Ouvir(Fighter f, string lingua)
	{
		double m = Maestria(f, lingua);
		if (m >= 1) return 1;
		double ganho = GanhoPorFala * (lingua == DosDeuses ? RitmoDosDeuses : 1) * (f.LinguasEnsinadas.Contains(lingua) ? RitmoDoEnsinado : 1);
		double novo = Math.Min(1, f.Linguas.GetValueOrDefault(lingua) + ganho);
		f.Linguas[lingua] = novo;
		return novo;
	}

	/// <summary>
	/// A LICAO: +10% na hora e 2x mais rapido pra sempre, uma vez por lingua. Falso quando nao ha o que ensinar (o aluno ja sabe a lingua
	/// inteira, ou ja foi ensinado nela).
	/// </summary>
	public static bool Ensinar(Fighter aluno, string lingua)
	{
		if (Maestria(aluno, lingua) >= 1 || aluno.LinguasEnsinadas.Contains(lingua)) return false;
		aluno.LinguasEnsinadas.Add(lingua);
		aluno.Linguas[lingua] = Math.Min(1, aluno.Linguas.GetValueOrDefault(lingua) + BonusDoEnsino);
		return true;
	}

	// =====================================================================
	// O EMBARALHADOR
	// =====================================================================
	/// <summary>
	/// O QUE QUEM OUVE ENTENDE desta fala, com esta maestria. Palavra a palavra:
	///   * a palavra e ENTENDIDA quando o sorteio dela (fixo pela lingua e pela palavra) fica abaixo da maestria -- entao a mesma palavra,
	///     uma vez entendida, continua entendida conforme a maestria sobe, e as mais comuns aparecem primeiro pra quem ouve muito;
	///   * a que nao e entendida vira um VOCABULO da lingua: silabas proprias dela, sempre as mesmas pra mesma palavra (a lingua soa como
	///     lingua, e nao como ruido), com as primeiras letras certas na proporcao da maestria -- "as palavras vao ficando menos confusas".
	/// Pontuacao, espacos e numeros passam como estao. Com a maestria inteira, o texto volta igual.
	/// </summary>
	public static string Embaralhar(string texto, string lingua, double maestria)
	{
		if (maestria >= 1 || texto.Length == 0) return texto;
		var sb = new StringBuilder(texto.Length + 8);
		int i = 0;
		while (i < texto.Length)
		{
			if (!char.IsLetter(texto[i]))
			{
				sb.Append(texto[i]);
				i++;
				continue;
			}
			int ini = i;
			while (i < texto.Length && char.IsLetter(texto[i])) i++;
			sb.Append(PalavraOuvida(texto[ini..i], lingua, maestria));
		}
		return sb.ToString();
	}

	/// <summary>Uma palavra, pelo ouvido de quem tem esta maestria. Ver <see cref="Embaralhar"/>.</summary>
	private static string PalavraOuvida(string palavra, string lingua, double maestria)
	{
		ulong h = Hash(lingua, palavra.ToLowerInvariant());
		if (h % 10_000 / 10_000.0 < maestria) return palavra;

		string falso = Vocabulo(palavra.Length, lingua, h);
		int certas = Math.Min((int)Math.Floor(palavra.Length * Math.Max(maestria, 0) * 0.5), palavra.Length);
		string r = palavra[..certas] + falso[certas..];
		return char.IsUpper(palavra[0]) ? char.ToUpperInvariant(r[0]) + r[1..] : r;
	}

	/// <summary>
	/// O VOCABULO: silabas da lingua (consoantes e vogais escolhidas pelo nome dela, entao cada lingua tem o seu som) ate o tamanho da
	/// palavra, sorteadas pela palavra -- a mesma palavra soa sempre igual.
	/// </summary>
	private static string Vocabulo(int tamanho, string lingua, ulong semente)
	{
		const string Consoantes = "bcdfghjklmnprstvzxkq";
		const string Vogais = "aeiouy";
		ulong hl = Hash(lingua, "som");
		var cs = new StringBuilder();
		for (int k = 0; k < 7; k++) cs.Append(Consoantes[(int)((hl >> (k * 5)) % (ulong)Consoantes.Length)]);
		var vs = new StringBuilder();
		for (int k = 0; k < 3; k++) vs.Append(Vogais[(int)((hl >> (40 + k * 7)) % (ulong)Vogais.Length)]);

		var sb = new StringBuilder(tamanho + 2);
		ulong s = semente | 1;
		while (sb.Length < tamanho)
		{
			s ^= s << 13; s ^= s >> 7; s ^= s << 17;
			sb.Append(cs[(int)(s % (ulong)cs.Length)]);
			if (sb.Length < tamanho) sb.Append(vs[(int)((s >> 8) % (ulong)vs.Length)]);
		}
		return sb.ToString(0, tamanho);
	}

	/// <summary>FNV-1a de 64 bits -- fixo entre processos (o `string.GetHashCode` do .NET muda a cada execucao).</summary>
	private static ulong Hash(string a, string b)
	{
		ulong h = 14695981039346656037UL;
		foreach (char c in a) { h ^= c; h *= 1099511628211UL; }
		h ^= '|'; h *= 1099511628211UL;
		foreach (char c in b) { h ^= c; h *= 1099511628211UL; }
		return h;
	}

	// =====================================================================
	// OS NOMES
	// =====================================================================
	/// <summary>O nome da lingua na tela, em portugues -- o que vai em "a língua {0}" (os avisos) e no <see cref="Rotulo"/>.</summary>
	public static string NomeDe(string lingua) => lingua switch
	{
		Galactica => "Galáctica",
		DosDeuses => "dos Deuses",
		"Saiyan" => "Saiyajin",
		"Namekian" => "Namekuseijin",
		"Human" => "Terráquea",
		"Frost Demon" => "dos Demônios do Frio",
		"Kai" => "Kaioshin",
		"Demon" => "Demoníaca",
		"Demigod" => "dos Semideuses",
		"Arlian" => "Arliana",
		"Tsujin" => "Tsufurujin",
		"Kanassa" => "Kanassana",
		"Heran" => "Herana",
		"Alien" => "Alienígena",
		"Shapeshifter" => "dos Metamorfos",
		"SpiritDoll" => "das Bonecas Espirituais",
		_ => lingua,
	};

	/// <summary>
	/// O ROTULO da lingua: a etiqueta da fala ("[língua Saiyajin] ...") e o botao do chat. A frase inteira, e nao so o nome: "[dos Deuses]"
	/// sozinho nao diria o que e, e o ingles a monta inteira pelo dicionario ("Saiyan language", "Gods' language").
	/// </summary>
	public static string Rotulo(string lingua) => $"língua {NomeDe(lingua)}";
}
