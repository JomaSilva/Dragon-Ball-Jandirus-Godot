namespace Jandirus.Core.Skills;

/// <summary>
/// ============================ O SENSE EM CINCO NIVEIS (dono, 2026-10-18) ============================
/// *"adicione niveis na skill de sense, assim aumentando a area de efeito, começando no campo de visao, dps regiao, dps planeta, dps
/// sistema, e dps o universo (no caso do universo o player so vai sentir chefes e jogadores, pq se nao ele vai sentir todos os npcs do jogo
/// e seria terrivel), vc pode escolher qual level de sense libera oq"*.
///
/// O QUE CADA NIVEL ABRE (a escolha e minha, pela licenca do dono):
///   1. CAMPO DE VISAO -- a mesma zona, ate <see cref="TilesDoCampoDeVisao"/> tiles (o `get_dist &lt;= 15` do DM): todo mundo, NPC inclusive;
///   2. REGIAO -- a mesma zona, ate <see cref="TilesDaRegiao"/> tiles: todo mundo, NPC inclusive;
///   3. PLANETA -- a zona inteira: so jogadores e chefes (o povo de uma cidade inteira seria uma lista de cem nomes);
///   4. SISTEMA -- os outros planetas do mesmo sistema estelar (e o espaco dele): jogadores e chefes, com o lugar em vez da distancia;
///   5. UNIVERSO -- qualquer lugar: so jogadores e chefes (o dono, por escrito).
/// O nivel 0 (a skill comprada e ainda nao treinada) sente como o 1.
///
/// COMO SOBE: sozinho, com o tempo (o `if(prob(50)) exp++` das skills que sobem por estar vivas, `skill.dm:86-95`) -- e o TRIPLO meditando.
/// A barreira e 1000 no nivel 0 (a do `Sense2.0.dm` no `niveis.json`) e triplica a cada nivel: ~20 min pro 2, ~1 h pro 3, ~3 h pro 4 e
/// ~9 h pro 5, so andando; um terco disso meditando.
///
/// O PISO PELA ARVORE DA MENTE: o contador `kiawarenessskill` (que no DM abria os alcances) continua valendo como piso -- 20 abre o
/// planeta e 60 o sistema (`GameServer.NivelDoSense`). Quem ja tinha treinado a arvore nao perde alcance; o universo so pelo nivel 5.
///
/// DIVERGENCIA DECLARADA: no DM a skill tem UM nivel (`maxlevel = 1`) e os tres alcances (perto/planeta/galaxia acima de 5 milhoes)
/// acendem so pelo contador (`Sense2.0.dm:20-35`, `HtmlUI.dm:360-398`). O piso dos 5 milhoes da galaxia saiu: no universo o corte e
/// "jogador ou chefe".
/// ================================================================================================
/// </summary>
public static class SenseEmNiveis
{
	/// <summary>O typepath da skill -- o mesmo do DM.</summary>
	public const string Path = "/datum/skill/sense";

	/// <summary>Os cinco niveis, pelo que cada um abre.</summary>
	public const int CampoDeVisao = 1, Regiao = 2, Planeta = 3, Sistema = 4, Universo = 5;

	/// <summary>O teto da skill.</summary>
	public const int NivelMaximo = Universo;

	/// <summary>Ate onde o campo de visao sente: 15 tiles, o `get_dist(src, D) &gt; 15` do DM (`HtmlUI.dm:371`).</summary>
	public const int TilesDoCampoDeVisao = 15;

	/// <summary>Ate onde a regiao sente: 60 tiles, quatro vezes a vista.</summary>
	public const int TilesDaRegiao = 60;

	/// <summary>A barreira do nivel 0 -- a `barreira` que o `niveis.json` traz do `Sense2.0.dm`.</summary>
	public const double BarreiraInicial = 1000;

	/// <summary>Quanto a barreira cresce por nivel: triplica.</summary>
	public const double CrescimentoPorNivel = 3;

	/// <summary>O exp a mais por tique, meditando (o ganho por tempo da 0,5 por tique; isto soma 1 -- o triplo).</summary>
	public const double GanhoMeditando = 1;

	/// <summary>O nome de cada alcance, pra tela e pros avisos.</summary>
	public static string NomeDoAlcance(int nivel) => nivel switch
	{
		<= CampoDeVisao => "campo de visão",
		Regiao => "região",
		Planeta => "planeta",
		Sistema => "sistema",
		_ => "universo",
	};

	/// <summary>A regra de nivel do Sense -- registrada junto das semeadas a mao (`RegrasDeNivel`).</summary>
	internal static RegraDeNivel Regra() => new()
	{
		Path = Path,
		Barreira = BarreiraInicial,
		Crescimento = CrescimentoPorNivel,
		GanhoPorTempo = true,
		PorEstado = [new RegraDeNivel.GanhoPorEstado { Quanto = GanhoMeditando, Quando = RegraDeNivel.Estado.Meditando }],
		Degraus =
		[
			new Degrau { Nivel = CampoDeVisao, Aviso = "você sente as presenças à sua volta -- o seu Sense alcança o campo de visão." },
			new Degrau { Nivel = Regiao, Aviso = $"o seu Sense se expande: agora você sente a região inteira à sua volta ({TilesDaRegiao} tiles)." },
			new Degrau { Nivel = Planeta, Aviso = "o seu Sense cobre o planeta inteiro: você sente os lutadores e os chefes de qualquer canto dele." },
			new Degrau { Nivel = Sistema, Aviso = "o seu Sense atravessa o espaço: você sente os lutadores e os chefes dos outros planetas deste sistema." },
			new Degrau { Nivel = Universo, Aviso = "o seu Sense alcança o universo inteiro: onde houver um lutador ou um chefe, você o sente." },
		],
	};

	/// <summary>O teto da skill no catalogo: cinco niveis, e nao o um do DM. A carga do catalogo chama (`SkillCatalog.Parse`).</summary>
	internal static void Aplicar(SkillCatalog cat)
	{
		if (cat.Get(Path) is { } s) s.MaxNivel = NivelMaximo;
	}
}
