using Jandirus.Core.Stats;

namespace Jandirus.Core.Races;

/// <summary>
/// O SANGUE DO BIO-ANDROIDE (v0.3.0) -- o que ele herda das RACAS que o fizeram. O dono (2026-10-15): *"bio androide deveria
/// pegar isso o gasto de vigor/ki das racas q foram usadas pra criar ele, bio androide sozinho N tem nada, ele pega as
/// passivas e skills das racas q foram usadas pra criar ele, zenkai se tiver saiyajin, regeneracao e semi imortalidade se
/// tiver majin"*.
///
/// ============================ O QUE MUDOU EM RELACAO AO DM, DECLARADO ============================
/// No DM o bio nasce com o genoma do proto Bio-Android (`DNALabs.dm:460`), e as passivas sao DESSE proto: Zenkai pra todo
/// bio (`combatgains.dm:14`), `Regeneration = 30` -- cura em combate e membro de volta --, meio tanque de vigor garantido
/// (`StaminaDrain.dm:19-20`) e o folego no vacuo (`statbiodroid.dm:51`). Dos doadores o DM so tira o BP, as tecnicas e o SSJ
/// do sangue Saiyajin. A regra do dono e o avesso: SOZINHO O BIO NAO TEM NADA, e o que ele tem vem das racas doadoras:
///   * o Zenkai, so com sangue Saiyajin;
///   * a regeneracao (o <see cref="Combat.PerfilDeRegen"/>) e a da doadora que mais regenera -- o Majin da a dele;
///   * a semi-imortalidade do Majin (se desfazer em vez de morrer, fora do ki), o nao sangrar e o nao mancar, com sangue Majin;
///   * o vigor que trava no cheio (Majin), o vigor e o ki que nao baixam de 100% (o androide de energia infinita);
///   * as ARVORES raciais das doadoras -- as skills de raca delas. A do proprio bio continua: e nela que mora a absorcao
///     que o faz evoluir, e sem ela a larva nunca passaria de larva;
///   * o folego no vacuo continua vindo da doadora que respira (`Fighter.bio_dna_respira`, assim desde 2026-10).
/// ==============================================================================================
///
/// ============================ UMA PERGUNTA SO: A "RACA COM SANGUE" ============================
/// Pro bio ela e `"BioAndroid+Saiyan+Majin"`; pra todo o resto, a propria raca. As perguntas de raca que viram passiva
/// (`Feridas.NaoSangra`, `DebuffsDeMembro.SemAtrasoDeFerimento`, `SkillCatalog.ArvoresDe`/`Permitida`) leem as PARTES dela
/// (<see cref="Racas"/>) -- e pra quem nao e bio a string e a mesma de antes, entao a resposta tambem.
/// ==============================================================================================
///
/// ============================ AS PERGUNTAS NAO ALOCAM (achado da `--iateste`) ============================
/// `Carrega`, `Tem` e `TemSaiyajin` sao perguntadas no caminho quente: a leitura de capacidades da IA (o corpo de Majin, a
/// regeneracao) e o cadeado do vigor no tique do corpo (`Nutricao.VigorSempreCheio`, 30 Hz por corpo). A primeira versao
/// era LINQ com lambda que capturava a raca -- 88 bytes de lixo por pergunta (o delegate e o fecho), e a bancada da IA,
/// que mede a leitura de capacidades em ZERO bytes, reprovou. Por isso aqui ha lacos por indice e `Span`, e nao `Any`.
/// ==============================================================================================
/// </summary>
public static class SangueDoBio
{
	/// <summary>O separador das partes da raca com sangue: `"BioAndroid+Saiyan"`.</summary>
	public const char Separador = '+';

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v030teste` e `--bioteste`): o sangue nao atravessa -- o bio volta a nao herdar passiva
	/// nem arvore das doadoras. Lido por <see cref="Doadoras"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool SangueNaoPassaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v030teste` e `--transeteste`): o bio volta a ter as passivas PROPRIAS do DM -- o Zenkai
	/// de todo bio, o piso de meio tanque de vigor e a regeneracao do proto (30). Falso em jogo, sempre.
	/// </summary>
	public static bool BioComOsPassivosDeAntesDeTeste;

	/// <summary>Esta ficha e de um Bio-Androide?</summary>
	public static bool EhBio(Fighter f) => BioAndroids.EhBio(f.Race);

	/// <summary>
	/// AS RACAS DOADORAS DESTE BIO -- gravadas no parto (`Fighter.bio_racas_dna`), na ordem das amostras. Vazio pra quem nao
	/// e bio. O bio nascido antes da v0.3.0 nao tem a lista: dele so o bit do sangue Saiyajin foi gravado
	/// (`Fighter.bio_saiyan_dna`), e e so isso que ele carrega.
	/// </summary>
	public static IReadOnlyList<string> Doadoras(Fighter f)
	{
		if (!EhBio(f) || SangueNaoPassaDeTeste) return [];
		if (f.bio_racas_dna.Count > 0) return f.bio_racas_dna;
		return f.bio_saiyan_dna ? SoOSaiyajin : [];
	}

	/// <summary>A raca Saiyajin no `races.json` -- a que o bio antigo carrega pelo bit.</summary>
	public const string RacaSaiyajin = "Saiyan";

	/// <summary>O sangue do bio antigo, UMA instancia: a pergunta e de caminho quente, e uma lista nova por chamada seria lixo.</summary>
	private static readonly string[] SoOSaiyajin = [RacaSaiyajin];

	/// <summary>A RACA COM SANGUE deste corpo: `"BioAndroid+Saiyan+Majin"` pro bio com doadoras, e a propria raca pro resto.</summary>
	public static string RacaComSangue(string raca, Fighter f) => RacaComSangue(raca, Texto(f));

	/// <summary>A mesma, montada do que veio no fio (`AtributosState.Sangue`): o cliente nao tem a ficha.</summary>
	public static string RacaComSangue(string raca, string sangue) =>
		sangue.Length == 0 ? raca : raca + Separador + sangue;

	/// <summary>O SANGUE EM TEXTO, pro fio: `"Saiyan+Majin"`, ou vazio.</summary>
	public static string Texto(Fighter f) => string.Join(Separador, Doadoras(f));

	/// <summary>As PARTES de uma raca com sangue. Pra quem nao e bio, uma parte so: a raca.</summary>
	public static IEnumerable<string> Racas(string? racaComSangue) =>
		(racaComSangue ?? "").Split(Separador, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	/// <summary>Esta raca com sangue tem ESTA raca entre as partes? Parte a parte, sem `Split` -- ver o cabecalho.</summary>
	public static bool Tem(string? racaComSangue, string raca)
	{
		if (string.IsNullOrEmpty(racaComSangue)) return false;
		ReadOnlySpan<char> resto = racaComSangue;
		while (true)
		{
			int i = resto.IndexOf(Separador);
			if ((i < 0 ? resto : resto[..i]).Trim().Equals(raca, StringComparison.OrdinalIgnoreCase)) return true;
			if (i < 0) return false;
			resto = resto[(i + 1)..];
		}
	}

	/// <summary>Este bio carrega o sangue desta raca? Falso pra quem nao e bio (a pergunta do resto e a raca dele).</summary>
	public static bool Carrega(Fighter f, string raca)
	{
		IReadOnlyList<string> doadoras = Doadoras(f);
		for (int i = 0; i < doadoras.Count; i++)
			if (string.Equals(doadoras[i], raca, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>Este bio carrega sangue Saiyajin -- puro ou meio, pelo mesmo predicado da escada (`Catalogo.EhSaiyajin`)?</summary>
	public static bool TemSaiyajin(Fighter f)
	{
		IReadOnlyList<string> doadoras = Doadoras(f);
		for (int i = 0; i < doadoras.Count; i++)
			if (Forms.Catalogo.EhSaiyajin(doadoras[i])) return true;
		return false;
	}

	/// <summary>
	/// O MOTOR DE ENERGIA INFINITA veio no sangue: uma doadora era um androide de energia infinita (`Amostra.Infinito`). E o
	/// unico dom que nao e da RACA e sim da maquina -- o androide de absorcao, da mesma raca, nao o passa.
	/// </summary>
	public static bool InfinitoNoSangue(Fighter f) => EhBio(f) && !SangueNaoPassaDeTeste && f.bio_dna_infinito;

	/// <summary>
	/// A REGENERACAO DO BIO: a da doadora que mais regenera, pelo `misc_stats["Regeneration"]` de cada uma
	/// (<paramref name="regeneracaoDa"/>, que o servidor le do `races.json`). Sozinho, o comum -- o Humano, 1.
	///
	/// NO EMPATE GANHA QUEM NAO E NAMEKUSEIJIN: o ramo dele (`PerfilDeRegen.De`) nao cura em combate nem devolve membro com o
	/// MESMO numero que da as duas coisas ao Kai -- com o Namekuseijin e o Kai no sangue, o bio fica com o melhor dos dois.
	/// </summary>
	public static (string Raca, double Regeneration) RegeneracaoDoBio(Fighter f, Func<string, double> regeneracaoDa)
	{
		string melhor = "Human";
		double reg = 1;
		foreach (string r in Doadoras(f))
		{
			double v = regeneracaoDa(r);
			if (v > reg || (v == reg && melhor == "Namekian" && r != "Namekian")) { melhor = r; reg = v; }
		}
		return (melhor, reg);
	}

	/// <summary>
	/// O SANGUE DE UM DOADOR, na hora da agulha: um bio colhido passa as doadoras DELE; os outros, a raca e a do pai (o
	/// meio-sangue vale pelos dois -- o `Race == X || Parent_Race == X` de todo requisito racial do port).
	/// </summary>
	public static List<string> DeUmDoador(string raca, string racaDoPai, Fighter ficha)
	{
		if (BioAndroids.EhBio(raca)) return [.. Doadoras(ficha)];
		var l = new List<string>();
		foreach (string r in new[] { raca, racaDoPai })
			if (r.Length > 0 && !l.Contains(r, StringComparer.OrdinalIgnoreCase)) l.Add(r);
		return l;
	}

	/// <summary>
	/// O SANGUE DE UMA FORNADA: a uniao das amostras, na ordem em que entraram, sem repetir e sem o proprio Bio-Androide (o
	/// bio colhido ja passou as doadoras dele, e "BioAndroid" no sangue nao traria nada -- sozinho ele nao tem nada).
	/// </summary>
	public static List<string> DaFornada(IEnumerable<IEnumerable<string>> amostras)
	{
		var l = new List<string>();
		foreach (IEnumerable<string> a in amostras)
			foreach (string r in a)
				if (r.Length > 0 && !BioAndroids.EhBio(r) && !l.Contains(r, StringComparer.OrdinalIgnoreCase)) l.Add(r);
		return l;
	}
}
