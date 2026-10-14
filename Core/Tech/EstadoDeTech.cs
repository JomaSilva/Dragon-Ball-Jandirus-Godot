namespace Jandirus.Core.Tech;

/// <summary>
/// O ESTADO DE UMA MAQUINA PORTADA NA v0.2.1 -- os numeros dela, os textos dela e o que ela guarda.
///
/// ============================ POR QUE UM SACO SO, E NAO UMA CLASSE POR MAQUINA ============================
/// A maquina de gravidade, o regenerador e a Drill tem cada um a sua classe de estado dentro da `Obra`
/// (`GravidadeDaObra`, `RegeneradorDaObra`, `DrillDaObra`). O lote da v0.2.1 traz mais de vinte maquinas de uma vez
/// (o bau, a geladeira, a base, o simulador, a estacao de recarga, a lua artificial, o telepad...), e quase todas
/// guardam meia duzia de numeros: vinte classes de tres campos seriam vinte lugares pra esquecer um.
///
/// Aqui o estado e um saco com tres gavetas -- NUMEROS, TEXTOS e ITENS --, e quem da nome as chaves e a regra de
/// cada maquina, em `Core/Tech` (as constantes moram la, ao lado da regra que as le). Ele mora na `Obra`
/// (`Obra.Tech`) pelo argumento de sempre: nasce e morre com ela, e vai pro `mundo.json` de graca.
///
/// CHAVE QUE NAO EXISTE VALE ZERO (ou vazio): a maquina recem-assentada nao precisa ser "inicializada", e um
/// `mundo.json` de antes de uma melhoria nova volta do disco sem migracao.
/// ========================================================================================================
/// </summary>
public sealed class EstadoDeTech
{
	public Dictionary<string, double> N = [];
	public Dictionary<string, string> S = [];
	public List<Jandirus.Core.Items.Pilha> Itens = [];

	public double Num(string chave, double padrao = 0) => N.TryGetValue(chave, out double v) ? v : padrao;
	public string Txt(string chave) => S.TryGetValue(chave, out string? v) ? v : "";
	public bool Ligado(string chave) => Num(chave) > 0;

	/// <summary>Quantos itens deste id estao guardados aqui.</summary>
	public int Quantos(string id)
	{
		int n = 0;
		foreach (Jandirus.Core.Items.Pilha p in Itens)
			if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) n += p.Quantidade;
		return n;
	}
}

/// <summary>
/// O QUE GUARDA COISA: o bau, a geladeira, a base e a fortaleza.
///
/// ============================ O QUE O ORIGINAL FAZ ============================
///   * `obj/Container/Crate` ("Chest", `TechSupport.dm:62-77` e `Equipment/Container.dm`): `Put()` e `Loot()`, de
///     qualquer um que esteja a um tile.
///   * `obj/items/Fridge` (`Stamina/Food.dm:211-239`): so comida, e so aparafusada. La a comida guardada estraga
///     50% mais devagar; este port nao tem comida que estraga, entao ela e a despensa.
///   * `obj/PlayerBase` e `obj/PlayerFortress` (`PlanetTech.dm:446-583`): qualquer um GUARDA, so o dono TIRA; o
///     "Rest" cura 5 (15 na fortaleza) nos vitais e devolve 5% (15%) do Ki por clique; so o dono desmonta, e o que
///     estava guardado cai no chao.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * A TELA E A DA MOCHILA (a mesma de revistar um nocauteado), e nao uma lista de `input()`.
///   * O DESCANSO NAO VALE EM COMBATE e tem um segundo de intervalo: no original cada clique abre um menu (o que ja
///     o espaca), e nenhuma cura deste port funciona no meio da briga.
///   * AS VAGAS sao as da mochila (trinta). No original o bau nao tem teto.
///   * COM COISA DENTRO NAO SE RECOLHE: a obra recolhida vira um item sem identidade, e o que ela guardava sumiria.
/// ==================================================================================
/// </summary>
public static class Deposito
{
	public const string Bau = "Crate", Geladeira = "Fridge", Base = "Base_Camp", Fortaleza = "Player_Fortress";

	public const string VerboAbrir = "bau_abrir", VerboDescansar = "base_descansar", VerboDesmontar = "base_desmontar";

	/// <summary>Este tipo de obra guarda itens?</summary>
	public static bool Guarda(string tipo) => tipo is Bau or Geladeira or Base or Fortaleza;

	/// <summary>`if(usr.ckey != owner_ckey) "Only the owner can retrieve items"` (`PlanetTech.dm:489`).</summary>
	public static bool SoODonoTira(string tipo) => tipo is Base or Fortaleza;

	/// <summary>`if(!Bolted) return` (`Food.dm:220`): a geladeira so funciona aparafusada.</summary>
	public static bool PedeParafuso(string tipo) => tipo == Geladeira;

	/// <summary>A geladeira so aceita comida (`for(var/obj/items/food/F in usr.contents)`, `Food.dm:222`).</summary>
	public static bool SoComida(string tipo) => tipo == Geladeira;

	/// <summary>Este tipo tem o "Rest"? (A base e a fortaleza.)</summary>
	public static bool Descansa(string tipo) => tipo is Base or Fortaleza;

	/// <summary>`SpreadHeal(5, 1, 0)` + 5% do Ki na base; 15 e 15% na fortaleza (`PlanetTech.dm:466-468` e `:537-539`).</summary>
	public static (double Cura, double FracaoDoKi) Descanso(string tipo) => tipo == Fortaleza ? (15, 0.15) : (5, 0.05);

	/// <summary>Entre dois descansos.</summary>
	public const double SegundosEntreDescansos = 1;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia T): qualquer um tira da base -- a regra do dono some.</summary>
	public static bool QualquerUmTiraDeTeste;
}

/// <summary>
/// O SCANNER MEDICO -- `obj/items/Medical_Scanner` (`Tier 1.5.dm:422-441`): o `Click()` examina quem clicou e o
/// `Scan_Other(mob/M in view(2))` examina outro. O que ele le esta em `GameServer.ExaminarComOScanner`.
/// </summary>
public static class ScannerMedico
{
	public const string Tipo = "Medical_Scanner", VerboEu = "scan_eu", VerboAlvo = "scan_alvo";

	/// <summary>`mob/M in view(2)`: a que distancia do scanner o examinado pode estar.</summary>
	public const int AlcanceEmTiles = 2;
}
