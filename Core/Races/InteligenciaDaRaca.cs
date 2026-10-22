namespace Jandirus.Core.Races;

/// <summary>
/// ============================ A INTELIGENCIA DA RACA (2026-10-18) ============================
/// A "inteligencia" do jogo e o `techmod` -- o multiplicador do aprendizado de tecnologia (`techXp += xp * techmod`, `Fighter.Tech`), do teto
/// do critico das armas e do teto de tecnologia. No DM ela vem do genoma: o `"Tech Modifier"` da raca (`Genetic_Datum.dm:123`) e escrito na
/// ficha ao nascer (`savant.techmod = misc_stats["Tech Modifier"]`, `:302`) -- e e nele que o desejo de inteligencia soma (`WishTable.dm:374`,
/// `add_to_stat("Tech Modifier",2)`).
///
/// O PORT NAO ESCREVIA: o `Fighter.FromGenome` lia os outros "misc" da raca e deixava o `techmod` no 1 da declaracao -- toda raca nascia com a
/// mesma inteligencia (o Humano do DM tem 3; o Tsujin, 7; o Yardrat, 0,5). Agora a ficha nasce com a da raca, e o personagem que ja existia
/// ganha a diferenca UMA vez no login (`GameServer.MigrarInteligencia`), com o que ele ja tinha somado (o desejo, o virus) preservado.
///
/// E POR CIMA, A REGRA DO DONO: *"dobre a inteligencia dos humanos e a raça tsujin (q deveria ser tsufurujin no jogo) teria o dobro da
/// inteligencia dos humanos"* -- o Humano vai de 3 a 6, e o Tsufurujin a 12. Mora aqui, em codigo e nao no `races.json` (que o pipeline extrai
/// do DM), pra uma re-extracao nao a apagar. Os mesticos herdam pelo genoma (a media ponderada do `Genome.Build`).
/// ==========================================================================================
/// </summary>
public static class InteligenciaDaRaca
{
	/// <summary>A chave do genoma -- a do DM.</summary>
	public const string Chave = "Tech Modifier";

	/// <summary>*"dobre a inteligencia dos humanos"*.</summary>
	public const double FatorDoHumano = 2;

	/// <summary>*"teria o dobro da inteligencia dos humanos"* -- o Tsufurujin sobre o Humano ja dobrado.</summary>
	public const double TsufurujinSobreOHumano = 2;

	/// <summary>A chave da raca do Tsufurujin -- a do DM (`stattsujin.dm`); na tela ela e "Tsufurujin" (`NomeDaRaca`).</summary>
	public const string Tsufurujin = "Tsujin";

	/// <summary>DEFEITO INJETADO (`--inteligenciateste`): o catalogo sem a regra do dono -- o Humano e o Tsujin do DM (3 e 7). Falso em jogo, sempre.</summary>
	public static bool SemARegraDoDonoDeTeste;

	/// <summary>DEFEITO INJETADO (`--inteligenciateste`): a ficha nasce com a inteligencia 1, ignorando a raca -- o port de antes. Falso em jogo, sempre.</summary>
	public static bool FichaIgnoraARacaDeTeste;

	/// <summary>A REGRA DO DONO nos prototipos raciais -- a carga do catalogo chama (`RaceCatalog.Parse`).</summary>
	internal static void Aplicar(RaceCatalog cat)
	{
		if (SemARegraDoDonoDeTeste || cat.Get("Human") is not { } humano) return;
		double h = humano.Misc.GetValueOrDefault(Chave, 1) * FatorDoHumano;
		humano.Misc[Chave] = h;
		foreach (Dictionary<string, double> classe in humano.ClassStats.Values)
			if (classe.ContainsKey(Chave)) classe[Chave] *= FatorDoHumano;
		if (cat.Get(Tsufurujin) is not { } tsu) return;
		tsu.Misc[Chave] = h * TsufurujinSobreOHumano;
		foreach (Dictionary<string, double> classe in tsu.ClassStats.Values)
			if (classe.ContainsKey(Chave)) classe[Chave] = h * TsufurujinSobreOHumano;
	}

	/// <summary>A inteligencia com que a ficha nasce, do bloco do genoma dela (`Fighter.FromGenome`).</summary>
	public static double DoBloco(StatBlock bloco) => FichaIgnoraARacaDeTeste ? 1 : bloco.Misc.GetValueOrDefault(Chave, 1);

	/// <summary>A INTELIGENCIA QUE A RACA DESTA FICHA DA: a do genoma (o mestico pela media), ou a do prototipo quando a ficha nao tem genoma.</summary>
	public static double DaFicha(RaceCatalog cat, Stats.Fighter f)
	{
		if (f.Genoma != null) return f.Genoma.Build(cat.Protos).Misc.GetValueOrDefault(Chave, 1);
		if (cat.Corpo(f.Race) is not { } p) return 1;
		return p.ClassStats.TryGetValue(f.Class, out Dictionary<string, double>? c) && c.TryGetValue(Chave, out double v)
			? v : p.Misc.GetValueOrDefault(Chave, 1);
	}
}
