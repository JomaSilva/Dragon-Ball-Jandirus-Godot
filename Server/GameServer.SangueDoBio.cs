using Jandirus.Core.Races;
using Jandirus.Core.Skills;

namespace Jandirus.Server;

/// <summary>
/// O QUE O BIO-ANDROIDE HERDA DOS DOADORES (v0.3.0) -- o lado do servidor do <see cref="SangueDoBio"/>.
///
/// O dono, em duas mensagens do mesmo dia (2026-10-15):
///   * *"bio androide deveria pegar isso o gasto de vigor/ki das racas q foram usadas pra criar ele, bio androide sozinho N
///     tem nada, ele pega as passivas e skills das racas q foram usadas pra criar ele, zenkai se tiver saiyajin,
///     regeneracao e semi imortalidade se tiver majin"* -- o SANGUE (a raca de cada doador: ver `SangueDoBio`);
///   * *"bio androide tb vem com todas as skills da aba learn dos jogadores q tiveram o dna usado, do mesmo nivel q eles, e
///     as tecnicas customizadas tb e as liberadas"* -- o LIVRO de cada doador, inteiro, no nivel dele.
///
/// ============================ A AGULHA GRAVA TUDO, O PARTO ENTREGA TUDO ============================
/// A fornada morre no parto (`NascerBioAndroide` a destroi antes de tudo), entao o que o bio herda tem que estar ESCRITO
/// na amostra: a raca e o sangue do doador (`Amostra.Sangue`, `Amostra.Infinito`), o livro (`Amostra.Skills`), o nivel de
/// cada skill (`Amostra.Niveis`) e as tecnicas customizadas (`Amostra.Customizadas`). Quem colhe -- a agulha do laboratorio
/// e a do recipiente da mochila -- colhe pelo MESMO <see cref="AmostraDe"/>.
///
/// DIVERGENCIA DECLARADA: o DM copiava so os botoes do doador (`donor_verbs = M.Keyableverbs.Copy()`, `DNALabs.dm:312`):
/// nem as skills passivas, nem o nivel, nem as tecnicas customizadas. A regra e a do dono.
/// ==================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>A RACA COM SANGUE deste corpo (`"BioAndroid+Saiyan+Majin"`, ou a raca) -- ver <see cref="SangueDoBio.RacaComSangue(string, Jandirus.Core.Stats.Fighter)"/>.</summary>
	private static string RacaComSangue(ServerPlayer p) => SangueDoBio.RacaComSangue(p.Race, p.Ficha);

	/// <summary>
	/// A AMOSTRA DE UM CORPO -- a agulha do laboratorio (`ColherDna`) e a do recipiente (`ExtrairDna`) colhem o MESMO
	/// registro: a raca, o sangue, o BP, o livro inteiro com os niveis e as tecnicas customizadas.
	/// </summary>
	private Amostra AmostraDe(ServerPlayer vitima) => new()
	{
		Raca = vitima.Race,
		RacaDoPai = vitima.Ficha.ParentRace,   // ver `Amostra.RacaDoPai`: meio-sangue vale pelos dois
		Doador = vitima.Name,
		Assinatura = vitima.Assinatura,
		Bp = vitima.Ficha.BP,
		Skills = [.. SkillsHerdaveis(vitima)],
		Niveis = SkillsHerdaveis(vitima)
			.Select(p => (Path: p, Nivel: vitima.Niveis?.Nivel(p) ?? 0))
			.Where(x => x.Nivel > 0)
			.ToDictionary(x => x.Path, x => x.Nivel, StringComparer.OrdinalIgnoreCase),
		Customizadas = [.. vitima.Customizadas.Where(t => t.Criada).Select(t => t.Clonar())],
		Sangue = SangueDoBio.DeUmDoador(vitima.Race, vitima.Ficha.ParentRace, vitima.Ficha),
		Infinito = vitima.Ficha.AndroideInfinito || SangueDoBio.InfinitoNoSangue(vitima.Ficha),
	};

	/// <summary>
	/// AS SKILLS QUE ATRAVESSAM A AGULHA: o livro INTEIRO do doador -- o que ele comprou na aba de aprendizado, o que um
	/// mestre lhe ensinou e o que lhe foi dado (*"todas as skills da aba learn [...] e as liberadas"*). So fica de fora o que
	/// o catalogo nao conhece mais (uma skill aposentada num save velho).
	/// </summary>
	private IEnumerable<string> SkillsHerdaveis(ServerPlayer doador)
	{
		if (_skills == null || doador.Livro == null) yield break;
		foreach (string path in doador.Livro.Aprendidas)
			if (_skills.Get(path) != null) yield return path;
	}

	/// <summary>
	/// O SANGUE DE UMA AMOSTRA, em racas: o gravado na agulha, ou -- na amostra de antes da v0.3.0, que nao o tem -- a raca e
	/// a do pai.
	/// </summary>
	private static IEnumerable<string> SangueDaAmostra(Amostra a) =>
		a.Sangue.Count > 0 ? a.Sangue : new[] { a.Raca, a.RacaDoPai }.Where(r => r.Length > 0);

	/// <summary>
	/// O LIVRO DOS DOADORES ENTRA NO BIO: cada skill de cada amostra, ENSINADA (ele sabe e nao repassa -- o laboratorio nao
	/// vira lavanderia de skill), no MAIOR nivel em que um doador a tinha. Devolve quantas entraram.
	///
	/// (dono, 2026-10-23) MENOS A SKILL DE CARGO QUE O ENSINO NAO DA MAIS (a chave da Sala do Tempo de um Guardiao doador): a copia
	/// que o bio levaria e ensinada, e a reconciliacao do cargo a tiraria no tique seguinte -- o parto contaria uma skill que ele nao
	/// tem. A amostra continua guardando o livro inteiro; quem escolhe e o parto.
	/// </summary>
	private int HerdarOLivroDosDoadores(ServerPlayer pl, Gestacao g)
	{
		if (_skills == null) return 0;
		var niveis = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (Amostra a in g.Amostras)
			foreach ((string path, int nivel) in a.Niveis)
				niveis[path] = Math.Max(niveis.GetValueOrDefault(path), nivel);

		int herdadas = 0;
		foreach (string path in g.Tecnicas)
		{
			if (_skills.Get(path) == null || pl.Livro.Sabe(path)) continue;
			if (EnsinadasQueSaem.Contains(path) && !Jandirus.Core.Ranks.DadivaDeCargo.EnsinadaDoCargoFicaDeTeste) continue;
			pl.Livro.DarComoEnsinada(path);
			herdadas++;
		}
		// O NIVEL E O DO DOADOR, e ZERO quando ele nao tinha nenhum: o corpo ainda carrega os niveis do criador (o
		// `NiveisDeSkill` nao e trocado -- e o razao dele que desfaz os degraus do humano), e uma skill que os dois sabiam
		// ficaria no nivel do criador, que morreu.
		foreach (string path in g.Tecnicas)
			if (pl.Livro.Sabe(path))
				pl.Niveis.Por(path, NivelDoDoadorFicaDeForaDeTeste ? 0 : niveis.GetValueOrDefault(path));
		return herdadas;
	}

	/// <summary>DEFEITO INJETADO (`--v030teste`): o bio herda as skills e nao o NIVEL delas -- todas comecam do zero.</summary>
	internal static bool NivelDoDoadorFicaDeForaDeTeste;

	/// <summary>
	/// AS TECNICAS CUSTOMIZADAS DOS DOADORES ENTRAM NO BIO -- como a licao do mestre (`GameServer.Ensino`): uma COPIA
	/// ensinada (`TecnicaCustomizada.CopiaEnsinada`), no menor id livre, ate o teto da mesa (`TecnicaCustomizada.Maximo`).
	/// Quem passa do teto fica de fora, na ordem das amostras. Devolve quantas entraram.
	///
	/// AS DO CRIADOR MORREM COM ELE, como o livro dele (`NascerBioAndroide`: *"marcos, escolhas e skills ensinadas do humano
	/// que ele era morrem junto com ele"*) -- quem chama ja as apagou.
	/// </summary>
	private static int HerdarAsCustomizadas(ServerPlayer pl, Gestacao g)
	{
		int entraram = 0;
		foreach (TecnicaCustomizada t in g.Amostras.SelectMany(a => a.Customizadas))
		{
			if (pl.Customizadas.Count >= TecnicaCustomizada.Maximo) break;
			int livre = 1;
			while (pl.Customizadas.Exists(x => x.Id == livre)) livre++;
			pl.Customizadas.Add(t.CopiaEnsinada(livre));
			entraram++;
		}
		pl.Customizadas.Sort((a, b) => a.Id.CompareTo(b.Id));
		return entraram;
	}

	/// <summary>
	/// O QUE O SANGUE TROUXE, em frases -- uma por dom, e nenhuma pelo que ele nao tem (sozinho, o bio nao tem nada). Quem
	/// escreve e o parto (`NascerBioAndroide`), depois de a ficha estar inteira.
	/// </summary>
	private List<(string Frase, Jandirus.Net.Protocol.Fala Tom)> FrasesDoSangue(ServerPlayer pl)
	{
		var l = new List<(string, Jandirus.Net.Protocol.Fala)>();
		IReadOnlyList<string> doadoras = SangueDoBio.Doadoras(pl.Ficha);
		if (doadoras.Count == 0)
		{
			l.Add(("nenhum sangue de raca corre em você: sozinho, um bio-androide não tem dom nenhum.", Jandirus.Net.Protocol.Fala.Sistema));
			return l;
		}
		l.Add(($"o sangue de {string.Join(", ", doadoras.Select(NomeDaRaca.Bonito))} corre em você -- e as árvores raciais "
			   + "dessas raças se abrem na sua aba de aprendizado.", Jandirus.Net.Protocol.Fala.Sistema));
		if (pl.Ficha.HasZenkai())
			l.Add(("o seu corpo aprende com a derrota: o sangue Saiyajin dá Zenkai.", Jandirus.Net.Protocol.Fala.AvisoDeZenkai));
		if (CorpoDeMajin(pl))
			l.Add(("o sangue MAJIN é o seu agora: você não sangra, se refaz dos golpes, só o ki te mata de vez -- e o seu "
				   + "fôlego nunca acaba.", Jandirus.Net.Protocol.Fala.Sistema));
		else if (pl.Combate?.Corpo.Regen is { CuraEmCombate: true })
			l.Add(("o seu corpo se costura sozinho, até no meio da luta -- a regeneração veio no sangue.", Jandirus.Net.Protocol.Fala.Sistema));
		if (SangueDoBio.InfinitoNoSangue(pl.Ficha))
			l.Add(("o motor de energia infinita de uma das máquinas pulsa em você: o vigor e o ki nunca baixam de 100%.",
				   Jandirus.Net.Protocol.Fala.Sistema));
		return l;
	}
}
