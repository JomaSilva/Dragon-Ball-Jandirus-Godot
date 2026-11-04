using Jandirus.Core.Appearance;
using Jandirus.Core.World;

namespace Jandirus.Core.Races;

/// <summary>
/// ============================ A RACA PROCEDURAL DE UM MUNDO COM CIVILIZACAO (o pedido 227, 2026-11-02) ============================
/// O dono: *"a raça vai ter uma aparencia procedural, podendo pega aparencia de humano mas com cor de pele anormal ou eles usam sprites da
/// raça alien, etc, nunca faça eles como uma das raças normais/principais do jogo, eles podem ter aparencia parecida mas nunca terao as
/// passivas e o nome das raças pré feitas, mantenha um padrao de cor de pele e sprite no planeta [...] uma cor principal (q podem varia a
/// tonalidade um pouco mas sempre mantendo a mesma cor original) [...] cor de cabelo pode ser mais livre, mas assim como na terra tem a cor
/// mais comum de cabelo e a mais exotica, eles falam a lingua nativa deles (nome da raça procedural tb, coloque muita variaçao de nome pra
/// n repetir nomes, n precisa ser um nome pronunciavel), eles ja vem acostumados com a gravidade padrao do planeta natal deles, e quanto
/// mais forte a gravidade maior a media de poder deles"*.
///
/// O NOME VEM DA SEED DO MUNDO (<see cref="NomeDoMundo"/>), E TUDO O MAIS VEM DO NOME (<see cref="Do"/>). E o que deixa o CLIENTE desenhar
/// o povo sem receber nada alem do que ja recebe de todo corpo (a raca e a aparencia): o catalogo visual pergunta "que folha e essa raca?"
/// pelo nome dela (`VisualCatalog.CorposDe`). Um nome procedural se reconhece pela MARCA (<see cref="EhProcedural"/>): o gerador so aceita
/// nome cujo hash cai num residuo de 1.009 -- e nenhum nome do jogo (raca, bicho, deus, droide) pode passar por um.
///
/// NAO E RACA DO JOGO, por construcao: o nome nunca e o de uma raca pre-feita (<see cref="RacasDoJogo"/>), e as passivas do jogo sao todas
/// perguntadas pelo nome da raca -- um nome que o codigo nao conhece nao acende nenhuma. Os numeros do corpo (<see cref="Proto"/>) sao os
/// de um humano mexidos pela seed, sem classe nenhuma.
///
/// A LINGUA E A DA RACA, de graca: `Linguas.NativaDaRaca` devolve a propria raca, e o habitante (`TipoDeNpc.Cidadao`) fala a nativa
/// (o pedido 221) -- a "língua Zhrovaq" do povo Zhrovaq.
/// ===============================================================================================================================
/// </summary>
public sealed class RacaProcedural
{
	/// <summary>A raca do corpo de PELE LIVRE (gente pintada): o Kai, cuja folha aceita a cor somada (`BodyOptions.CorLivre`).</summary>
	public const string CorpoDePeleLivre = "Kai";

	/// <summary>A raca de onde saem as folhas de alien.</summary>
	public const string CorpoDeAlien = "Alien";

	/// <summary>
	/// AS FOLHAS DE ALIEN que um povo procedural pode vestir: as da lista do Alien que nao sao a cara de uma raca do jogo (o Arlian, o
	/// Kanassan, o Jiren dos Gray, o Majin, os Demonios e o Konatsu do Kai ficam de fora).
	/// </summary>
	public static readonly string[] FolhasDeAlien =
		["Metamori", "Yukenojin", "Hatchiyack", "Bio Experiment", "Immecka", "BuffFrog", "Kratos", "Goron", "Zora", "El Hermano"];

	/// <summary>Os tons da folha de alien: o brilho dela, do mais claro ao mais escuro (<see cref="BrilhoDoTom"/>).</summary>
	public static readonly string[] Tons = ["Muito claro", "Claro", "Medio", "Escuro", "Muito escuro"];

	private static readonly float[] Brilhos = [1.22f, 1.1f, 1f, 0.9f, 0.8f];

	/// <summary>O brilho de um tom (o indice da aparencia). Fora da lista, o do meio.</summary>
	public static float BrilhoDoTom(int tom) => tom >= 0 && tom < Brilhos.Length ? Brilhos[tom] : 1f;

	/// <summary>
	/// OS NOMES QUE UMA RACA PROCEDURAL NUNCA TEM: as racas do `races.json`, as que o jogo grava por outro caminho (o meio-Saiyajin, o
	/// Tuffle), e as linguas. Comparados sem caixa.
	/// </summary>
	public static readonly HashSet<string> RacasDoJogo = new(StringComparer.OrdinalIgnoreCase)
	{
		"Alien", "Android", "Arlian", "BioAndroid", "Demigod", "Demon", "Gray", "Heran", "Human", "Frost Demon", "Kai", "Kanassa", "Majin",
		"Makyo", "Meta", "Namekian", "Saibaman", "Saiyan", "Shapeshifter", "SpiritDoll", "Tsujin", "Yardrat", "Halfbreed", "Half-Saiyan",
		"Half-Breed", "Tuffle", "Neo Tuffle", "Galactic", "Gods", "Kaioshin", "Saiyajin", "Namekuseijin",
	};

	public required string Nome { get; init; }

	/// <summary>Gente pintada (a folha do Kai com a cor somada) -- ou falso: uma folha de alien com os tons de brilho.</summary>
	public required bool DePeleLivre { get; init; }

	/// <summary>A folha de alien (o nome do arquivo, sem extensao); vazio na pele livre.</summary>
	public required string Folha { get; init; }

	/// <summary>A COR PRINCIPAL DA PELE (so na pele livre): cada habitante varia o tom dela, nunca a cor.</summary>
	public required Rgb Pele { get; init; }

	/// <summary>As cores de cabelo do povo: a [0] e a mais comum, a [1] a segunda, e o resto as exoticas.</summary>
	public required Rgb[] Cabelos { get; init; }

	/// <summary>
	/// OS QUATRO PENTEADOS DO POVO, por genero -- guardados como SORTEIOS: o Core nao tem a lista de cabelos do catalogo visual, e quem
	/// veste (<see cref="Vestir"/>) os troca pelos nomes dela.
	/// </summary>
	public required int[] PenteadosM { get; init; }

	public required int[] PenteadosF { get; init; }

	// =====================================================================
	// O NOME
	// =====================================================================
	private static readonly string[] Inicios =
	[
		"", "", "b", "br", "c", "ch", "d", "dr", "f", "g", "gh", "gr", "h", "j", "k", "kh", "kr", "l", "m", "n", "p", "ph", "q", "qu", "r",
		"s", "sh", "sk", "st", "t", "th", "tr", "v", "vr", "w", "x", "y", "z", "zh", "zr", "xh", "kv", "tz", "dz", "ng", "mb",
	];

	private static readonly string[] Vogais =
	[
		"a", "e", "i", "o", "u", "y", "a", "e", "o", "aa", "ae", "ai", "ao", "au", "ea", "ei", "eo", "ia", "io", "oa", "oi", "ou", "ua", "ue",
		"ui", "uu", "ee", "oo", "yr", "ix",
	];

	private static readonly string[] Finais = ["", "", "", "", "n", "r", "s", "th", "x", "k", "l", "m", "q", "z", "sh", "rk", "nd", "ng", "t", "v", "rr"];

	/// <summary>O residuo da MARCA de um nome procedural: o hash do nome (sem caixa) modulo <see cref="ModuloDaMarca"/>.</summary>
	public const ulong ModuloDaMarca = 1009, ResiduoDaMarca = 617;

	/// <summary>DEFEITO INJETADO (`--civteste`): a marca nao e conferida -- qualquer nome que o jogo nao conhece vira raca procedural.</summary>
	public static bool SemMarcaDeTeste;

	private static bool TemAMarca(string nome) => Espaco.Hash64(nome.ToLowerInvariant()) % ModuloDaMarca == ResiduoDaMarca;

	/// <summary>
	/// ESTA RACA E PROCEDURAL? A marca no nome, e nada do que o jogo ja conhece (raca do arquivo, bicho, deus, droide de folha).
	/// </summary>
	public static bool EhProcedural(string? raca)
	{
		if (raca is not { Length: >= 3 and <= 24 } || RacasDoJogo.Contains(raca)) return false;
		if (SemMarcaDeTeste) return Conhecida(raca);
		// A RESPOSTA E GUARDADA: o desenho pergunta por corpo, e o hash do nome nao precisa ser refeito. Concorrente porque o Core roda
		// tambem nas threads de geracao do servidor.
		return _jaVistas.GetOrAdd(raca, n => TemAMarca(n) && Conhecida(n));
	}

	private static bool Conhecida(string raca) =>
		!Npc.Animais.EhAnimal(raca) && !Npc.BillsEWhis.EhRacaDivina(raca) && !Tech.Companheiros.EhDeFolha(raca);

	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _jaVistas = new(StringComparer.Ordinal);

	/// <summary>Um nome na fonetica do gerador (sem a marca): duas a quatro silabas, as vezes com apostrofo ou hifen.</summary>
	private static string Silabas(Random r, int silabas)
	{
		var sb = new System.Text.StringBuilder();
		for (int s = 0; s < silabas; s++)
		{
			if (s > 0 && r.Next(100) < 9) sb.Append(r.Next(2) == 0 ? '\'' : '-');
			sb.Append(Inicios[r.Next(Inicios.Length)]);
			sb.Append(Vogais[r.Next(Vogais.Length)]);
			if (s == silabas - 1 || r.Next(100) < 25) sb.Append(Finais[r.Next(Finais.Length)]);
		}
		string n = sb.ToString().Trim('\'', '-');
		return n.Length == 0 ? n : char.ToUpperInvariant(n[0]) + n[1..];
	}

	/// <summary>
	/// O NOME DA RACA DE UM MUNDO: silabas sorteadas pela seed ate sair um nome com a marca, de 4 a 14 letras, que nao e de raca do jogo.
	/// Sao ~1.000 tentativas em media (uma fracao de milissegundo); a variedade e a das silabas, e a marca nao a encolhe.
	/// </summary>
	public static string NomeDoMundo(ulong seedDoMundo)
	{
		for (ulong k = 0; ; k++)
		{
			var r = new Random((int)(Espaco.Misturar(seedDoMundo, Espaco.Hash64("raca procedural"), k) & 0x7FFFFFFF));
			string n = Silabas(r, 2 + r.Next(3));
			if (n.Length is >= 4 and <= 14 && !RacasDoJogo.Contains(n) && TemAMarca(n)) return n;
		}
	}

	/// <summary>
	/// OS NOMES DAS PESSOAS DO POVO, na mesma fonetica da raca e sem repetir: o habitante do mundo Zhrovaq se chama como um Zhrovaq.
	/// </summary>
	public static string[] NomesDePessoas(string raca, int quantos)
	{
		var r = new Random((int)(Espaco.Hash64(raca + "#pessoas") & 0x7FFFFFFF));
		var nomes = new List<string>();
		for (int t = 0; nomes.Count < quantos && t < quantos * 40; t++)
		{
			string n = Silabas(r, 2 + r.Next(2));
			if (n.Length is >= 3 and <= 12 && !nomes.Contains(n) && !string.Equals(n, raca, StringComparison.OrdinalIgnoreCase)) nomes.Add(n);
		}
		return [.. nomes];
	}

	// =====================================================================
	// O CORPO, A PELE E O CABELO -- tudo do nome
	// =====================================================================
	/// <summary>Os tons de pele "anormais" da gente pintada: azul, verde, roxo, vermelho, laranja, amarelo, ciano, rosa, verde-agua, magenta, lima e cinza.</summary>
	private static readonly double[] MatizesDaPele = [215, 125, 275, 0, 28, 52, 185, 325, 165, 300, 90, -1];

	/// <summary>As cores de cabelo "de gente" (a comum e a segunda de cada povo saem daqui): preto, castanhos, branco, prata, azul e verde escuros, vinho, loiro.</summary>
	private static readonly Rgb[] CabelosComuns =
	[
		new(26, 26, 26), new(59, 42, 26), new(107, 68, 35), new(232, 232, 232), new(176, 176, 192), new(28, 42, 74), new(31, 61, 42),
		new(74, 26, 26), new(216, 192, 112), new(16, 24, 48),
	];

	/// <summary>A RACA PELO NOME: o corpo, a pele, o cabelo. Funcao pura (as duas pontas tiram o mesmo).</summary>
	public static RacaProcedural Do(string nome)
	{
		var r = new Random((int)(Espaco.Hash64(nome + "#corpo") & 0x7FFFFFFF));
		bool peleLivre = r.Next(2) == 0;
		string folha = peleLivre ? "" : FolhasDeAlien[r.Next(FolhasDeAlien.Length)];

		double matiz = MatizesDaPele[r.Next(MatizesDaPele.Length)];
		Rgb pele = matiz < 0
			? Hsv(0, 0, 0.55 + r.NextDouble() * 0.25)
			: Hsv(matiz + r.Next(-8, 9), 0.42 + r.NextDouble() * 0.33, 0.62 + r.NextDouble() * 0.26);

		Rgb comum = CabelosComuns[r.Next(CabelosComuns.Length)];
		Rgb segunda = CabelosComuns[r.Next(CabelosComuns.Length)];
		var cabelos = new List<Rgb> { comum, segunda };
		for (int i = 0; i < 3; i++) cabelos.Add(Hsv(r.Next(360), 0.75 + r.NextDouble() * 0.25, 0.8 + r.NextDouble() * 0.2));

		return new RacaProcedural
		{
			Nome = nome,
			DePeleLivre = peleLivre,
			Folha = folha,
			Pele = pele,
			Cabelos = [.. cabelos],
			PenteadosM = [.. Enumerable.Range(0, 4).Select(_ => r.Next(1_000_000))],
			PenteadosF = [.. Enumerable.Range(0, 4).Select(_ => r.Next(1_000_000))],
		};
	}

	/// <summary>
	/// VESTE UM HABITANTE: o tom da pele (a cor do povo, mais clara ou mais escura, um fio de matiz pra cada lado) ou o tom da folha de
	/// alien, e o cabelo (60% a cor comum, 25% a segunda, 15% uma exotica) num dos quatro penteados do povo. A folha de alien vai careca: o
	/// desenho dela ja tem a cabeca toda.
	/// </summary>
	/// <param name="penteados">Os nomes de cabelo do catalogo visual, na ordem dele (o primeiro e o "Bald").</param>
	public static void Vestir(Appearance.Appearance ap, string raca, string genero, ulong semente, IReadOnlyList<string> penteados)
	{
		RacaProcedural r = Do(raca);
		var s = new Random((int)(Espaco.Misturar(semente, Espaco.Hash64("veste"), 0) & 0x7FFFFFFF));
		if (r.DePeleLivre)
		{
			(double h, double sat, double v) = ParaHsv(r.Pele);
			ap.CorPele = Hsv(h + s.Next(-6, 7), Math.Clamp(sat * (0.9 + s.NextDouble() * 0.2), 0, 1), Math.Clamp(v * (0.8 + s.NextDouble() * 0.35), 0, 1));
		}
		else ap.Tom = s.Next(Tons.Length);

		if (!r.DePeleLivre || penteados.Count < 2) { ap.Cabelo = "Bald"; ap.CorCabelo = null; return; }
		int[] sorteios = genero == "Female" ? r.PenteadosF : r.PenteadosM;
		ap.Cabelo = penteados[1 + sorteios[s.Next(sorteios.Length)] % (penteados.Count - 1)];
		int sorte = s.Next(100);
		ap.CorCabelo = sorte < 60 ? r.Cabelos[0] : sorte < 85 ? r.Cabelos[1] : r.Cabelos[2 + s.Next(r.Cabelos.Length - 2)];
	}

	// =====================================================================
	// OS NUMEROS DO CORPO E O PODER
	// =====================================================================
	/// <summary>
	/// O PROTOTIPO DA RACA: os numeros de um humano, cada um mexido pela seed (de 80% a 125%), sem classe nenhuma e sem folego no vacuo.
	/// Nada de passiva: elas sao todas perguntadas pelo nome da raca, e este nome o jogo nao conhece.
	/// </summary>
	public static RaceProto Proto(string nome)
	{
		var r = new Random((int)(Espaco.Hash64(nome + "#numeros") & 0x7FFFFFFF));
		double M() => 0.8 + r.NextDouble() * 0.45;
		var p = new RaceProto { Name = nome };
		foreach ((string k, double v) in new (string, double)[]
		{
			("Physical Offense", 1), ("Physical Defense", 1), ("Ki Offense", 1), ("Ki Defense", 1), ("Ki Skill", 1), ("Technique", 1),
			("Speed", 1), ("Esoteric Skill", 1), ("Skillpoint Mod", 1.4), ("Ascension Mod", 5), ("Energy Level", 1.3), ("Battle Power", 1),
		})
			p.Stats[k] = Math.Round(v * M(), 2);
		foreach ((string k, double v) in new (string, double)[]
		{
			("Lifespan", 1), ("Potential", 3), ("Regeneration", 1), ("Breed Type", 1), ("Zanzoken Mod", 4), ("Gravity Mod", 1), ("Med Mod", 4),
			("Spar Mod", 1.5), ("Train Mod", 1), ("Ki Regeneration", 3), ("Anger", 1.3), ("Starting BP", 10), ("Tech Modifier", 3),
		})
			p.Misc[k] = Math.Round(v * M(), 2);
		p.Misc["Space Breath"] = 0;
		p.ClassSpread.Add(("Normal", 100));
		return p;
	}

	/// <summary>
	/// O PODER PELA GRAVIDADE: quanto mais pesado o mundo natal, mais forte o povo -- a media do habitante de sempre multiplicada pela
	/// gravidade do planeta (1x na de 1, 80x na de 80).
	/// </summary>
	public static double FatorDePoder(double gravidade) => Math.Max(1, gravidade);

	// =====================================================================
	// AS CORES
	// =====================================================================
	private static Rgb Hsv(double h, double s, double v)
	{
		h = ((h % 360) + 360) % 360;
		double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
		(double r, double g, double b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x) : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
		return new Rgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
	}

	private static (double H, double S, double V) ParaHsv(Rgb c)
	{
		double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
		double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
		double h = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
		return (h < 0 ? h + 360 : h, max == 0 ? 0 : d / max, max);
	}
}
