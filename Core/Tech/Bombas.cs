using Jandirus.Core.Items;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Tech;

/// <summary>
/// AS BOMBAS -- `obj/items/Bomb/Nuke`, `obj/items/Toxic_Waste`, `obj/items/Smoke_Bomb` (`Code/Modules/Tech/Bombs.dm`)
/// e o `obj/items/Detonator` (`Tier2.dm:254-297`). A explosao e a de `Character Customization/explosions.dm`.
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// A bomba tem FORCA (`strength`, o poder expresso da explosao), RAIO em tiles, um CODIGO e uma CONTAGEM. O `Use()`
/// roda a contagem e, no zero, `explode()` chama `spawnExplosion(turf, icone, strength, radius)`. O detonador tem
/// codigo e contagem proprios, e no zero estoura TODA bomba do mundo com o mesmo codigo (`Tier2.dm:295-297`).
///
/// A explosao (`obj/explosions/proc/explosion`) olha tudo o que ha no raio e, pra cada corpo
/// (`explosion_M_throw`, `explosions.dm:154-177`):
///
///     testback = min(round(rand(1, 2*BPModulus(strength, M.expressedBP)) / M.Ephysdef), 15)   // o arremesso
///     dmg = DamageCalc(BPModulus(strength, M.expressedBP), (M.Ephysoff + M.Etechnique) * dist)
///     if(harmful) { dmg /= 1.5; M.SpreadDamage(dmg); if(gibbify) cada membro perde `dmg` de novo }
///     if(radioactive) M.Mutations += 1
///
/// onde `dist` e 1 pra quem esta na metade de dentro do raio e 2 pro resto; os objetos levam `takeDamage(strength)`
/// e o tile cai quando `Resistance &lt;= strength / dist`, com 80% de chance.
///
/// ============================ NO ORIGINAL NADA DISSO ACONTECE, E SAO TRES DEFEITOS ============================
///   1. O `explosion()` abre com um laco *"one explosion per turf"* cujo `return` esta FORA do `if`
///      (`explosions.dm:191-195`): a explosao se acha a si mesma no proprio tile e sai antes de olhar o raio.
///   2. O laco do raio tem outro `return` no fim do corpo (`:225`): so o PRIMEIRO atomo seria processado.
///   3. O `DamageCalc` e chamado com dois argumentos e o `basedamage` fica nulo (`:162`): o dano multiplica por zero.
/// A bomba do original e um desenho. O que se porta e o que as contas escritas pretendem -- a mesma decisao ja
/// tomada pra explosao do embate de raios (`EmbateDeKi.DanoDoEstouro`).
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * O `basedamage` QUE FALTA E 40 -- o unico que o arquivo escreve, na explosao irma (`SonicBoom`, `:74`).
///   * O CENARIO APANHA PELA REGRA DAS EXPLOSOES DESTE PORT (`GameServer.EstragarOCenarioNaExplosao`): a forca cai
///     do centro pra beirada, as obras levam na armadura, a parede e o chao cedem pelo limiar e pela chance de la.
///   * RADIOATIVA ENVENENA. O `Mutations += 1` nao tem pra onde ir (este port nao tem mutacoes); o que a explosao
///     radioativa e o barril de lixo toxico deixam em quem pegam e o veneno que o port ja tem (o do Cuspe Acido).
///   * ARMAR E LARGAR ARMADA: a bomba cai aos pes de quem a arma, ja contando, e quem a PEGA do chao a desarma (o
///     `Cancel()` do original pede estar a um tile dela -- *"Detonators can't cancel bombs at a distance"*). O
///     detonador estoura as bombas do codigo dele onde estiverem: no chao, numa mochila ou guardadas num bau.
///   * A BOMBA NASCE SEM CODIGO (la, com um sorteado): sem codigo gravado nenhum detonador a alcanca.
///   * A FUMACA E SO FUMACA, como la: trinta segundos (`animlast = 300`) de nuvem que tapa quem esta dentro.
/// ==================================================================================
/// </summary>
public static class Bombas
{
	public const string Nuke = "Nuke", LixoToxico = "Toxic_Waste", Fumaca = "Smoke_Bomb", Detonador = "Detonator";

	// ---- as acoes da mochila ----
	public const string AcaoArmar = "bomba_armar", AcaoCodigo = "bomba_codigo", AcaoReforcar = "bomba_reforcar",
						AcaoInfo = "bomba_info", AcaoRomper = "bomba_romper", AcaoSoltarFumaca = "bomba_fumaca",
						AcaoDetonar = "bomba_detonar";

	/// <summary>O teto de letras de um codigo: ele mora no id do item.</summary>
	public const int MaxDeCodigo = 12;

	/// <summary>Entre quantos segundos se arma uma bomba ou um detonador. (La o `input` aceita qualquer numero.)</summary>
	public const int MinDeContagem = 3, MaxDeContagem = 600;

	// =====================================================================
	// A NUKE -- `strength = 100`, `radius = 35`, `strengthMod = 2`, `radioactive = 1`, `gibbify = 1`
	// =====================================================================
	public const double ForcaDaNuke = 100;
	public const int RaioDaNuke = 35;

	/// <summary>`strength = usr.intBPcap * strengthMod`: a nuke reforcada vale o dobro do teto de tecnologia de quem pagou.</summary>
	public const double MultDeReforco = 2;

	/// <summary>`log(usr.intBPcap)*60000` -- o preco do reforco (o `log` do BYOND e natural).</summary>
	public static double CustoDeReforcar(double tetoDeTecnologia) => Math.Log(Math.Max(tetoDeTecnologia, Math.E)) * 60_000;

	/// <summary>
	/// A gaveta do cofre em que mora a forca REFORCADA desta bomba (zero = a de fabrica). O numero nao viaja no id:
	/// ver `TechDeBolso.Gaveta`. Quem a resolve e o servidor (`GameServer.ForcaDaBomba`).
	/// </summary>
	public static int Cofre(string id) => TechDeBolso.Base(id) == Nuke ? TechDeBolso.Gaveta(TechDeBolso.Campo(id, 0)) : 0;

	/// <summary>O codigo gravado nesta bomba ou neste detonador. Vazio = nenhum.</summary>
	public static string Codigo(string id) => TechDeBolso.Campo(id, TechDeBolso.Base(id) == Detonador ? 0 : 1);

	public static string EscreverNuke(int cofre, string codigo) => TechDeBolso.Com(Nuke, TechDeBolso.EscreverGaveta(cofre), codigo);

	public static string EscreverDetonador(string codigo) => TechDeBolso.Com(Detonador, codigo);

	// =====================================================================
	// O LIXO TOXICO -- `spawnExplosionType(/obj/explosions/ToxicBoom, 10, GetTurf(), 3)`
	// =====================================================================
	public const double ForcaDoLixo = 10;
	public const int RaioDoLixo = 3;

	// =====================================================================
	// A FUMACA -- `SmokeBoom`: `animlast = 300`, a nuvem aumentada oito vezes
	// =====================================================================
	public const double SegundosDeFumaca = 30;

	/// <summary>De quanto em quanto a nuvem solta um tufo novo (o desenho de cada um dura pouco mais de dois segundos).</summary>
	public const double SegundosPorTufo = 0.5;

	/// <summary>Ate onde os tufos se espalham, em tiles: a nuvem do original e uma folha aumentada oito vezes.</summary>
	public const double RaioDaFumacaEmTiles = 2.5;

	// =====================================================================
	// A EXPLOSAO -- `explosions.dm`
	// =====================================================================
	/// <summary>O `basedamage` da explosao: o da `SonicBoom` (`explosions.dm:74`) -- ver o cabecalho.</summary>
	public const double DanoBase = 40;

	/// <summary>
	/// O DEGRAU DE DISTANCIA: 1 pra quem esta na metade de dentro do raio, 2 pro resto (`explosions.dm:210-214` --
	/// o degrau 3 de la pede estar ALEM do raio, e quem esta alem nao e olhado).
	/// </summary>
	public static int Degrau(double distanciaEmTiles, double raioEmTiles) => distanciaEmTiles < raioEmTiles / 2 ? 1 : 2;

	/// <summary>
	/// O DANO EM CADA MEMBRO de quem a explosao pega:
	/// `DamageCalc(BPModulus(strength, M.expressedBP), (M.Ephysoff + M.Etechnique) * dist, 40) / 1.5`.
	/// </summary>
	public static double Dano(double forca, Fighter alvo, int degrau) =>
		Combat.CombatMath.BpModulus(forca, Math.Max(alvo.expressedBP, 1))
		/ Math.Max((alvo.Ephysoff + alvo.Etechnique) * Math.Max(degrau, 1), 1e-9) * DanoBase / 1.5;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X2): a explosao e so o desenho -- a bomba do original, que nao fere ninguem.</summary>
	public static bool ExplosaoSemDanoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X2): o detonador estoura toda bomba, tenha o codigo que tiver.</summary>
	public static bool DetonadorSemCodigoDeTeste;
}

/// <summary>
/// O CAMPO DE FORCA -- `obj/items/Forcefield` (`Tier2.dm:209-253`) e o ramo dele no impacto de um tiro
/// (`Skills/Ki/tools/objects.dm:421-423`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// Ligado (`Equip()`), o corpo ganha `hasForcefield`. No impacto de um `obj/attack/blast` -- bola, raio ou bala --,
/// no lugar exato em que o corpo levaria o dano, o campo o leva: `M.forcefieldID.takeDamage(dmg*BPModulus(BP))`. O
/// campo ignora o que nao passa de 5% da armadura maxima dele (`superCalc()`: `superarmor = 0.05*maxarmor`) e perde
/// o resto; a armadura maxima e o teto de tecnologia de quem o fabricou (`A.maxarmor = usr.intBPcap`,
/// `TechSupport.dm:55`). Zerada a armadura, o campo desliga, estoura e some (`testDestroy()`). O atordoador nao
/// derruba quem esta de campo ligado (`objects.dm:351`).
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * O QUE O CAMPO PERDE E O PODER DO TIRO, e nao o dano dele. La a conta poe um numero de DANO (dezenas, na escala
///     da vida de um membro) contra uma armadura na escala de PODER (o teto de tecnologia, milhares ou milhoes), e
///     `BPModulus(BP)` com um argumento so devolve 1: o campo praticamente nunca se gasta. Aqui ele se gasta como
///     toda coisa de armadura deste port se gasta (`GameServer.Estragar`): pelo poder de quem bate. Um tiro com o
///     poder do teto do campo leva 95% dele; um dez vezes mais fraco, 5%; um vinte vezes mais fraco, nada.
///   * A EXPLOSAO DO CAMPO QUE CAI e so o estalo: a de la tem a forca padrao (10) e nao fere ninguem.
///   * NAO PROTEGE DE EXPLOSAO EM AREA nem de soco, como la: so do tiro que ACERTA.
/// ==================================================================================
/// </summary>
public static class CampoDeForca
{
	public const string Id = "Forcefield";

	public const string AcaoLigar = "campo_ligar", AcaoInfo = "campo_info";

	/// <summary>`superarmor = 0.05*maxarmor`: o que nao passa disto o campo nem sente.</summary>
	public const double FracaoIgnorada = 0.05;

	/// <summary>Ligado?</summary>
	public static bool Ligado(string id) => TechDeBolso.Campo(id, 1) == "on";

	/// <summary>
	/// A gaveta do cofre em que moram a armadura de agora e a maxima deste campo (zero = o item recem-fabricado, que
	/// ainda nao foi ligado). As duas sao numeros na escala de PODER -- o teto de tecnologia de quem o ligou --, e
	/// nao viajam no id: ver `TechDeBolso.Gaveta`. Quem as resolve e o servidor (`GameServer.ArmaduraDoCampo`).
	/// </summary>
	public static int Cofre(string id) => TechDeBolso.Gaveta(TechDeBolso.Campo(id, 0));

	public static string Escrever(int cofre, bool ligado) => TechDeBolso.Com(Id, TechDeBolso.EscreverGaveta(cofre), ligado ? "on" : "");

	/// <summary>O que sobra de armadura depois de um tiro com este poder -- o `takeDamage` com o `superarmor` do campo.</summary>
	public static double Depois(double armadura, double maxima, double poderDoTiro)
	{
		double ignora = FracaoIgnorada * maxima;
		return poderDoTiro > ignora ? Math.Max(armadura - (poderDoTiro - ignora), 0) : armadura;
	}

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X2): o campo ligado nao segura tiro nenhum -- o item da v0.2.0, sem acao.</summary>
	public static bool NaoAbsorveDeTeste;
}

/// <summary>
/// AS LUVAS E AS BOTAS DE DRENO -- `obj/items/Energy_Drain_Gloves` e `Energy_Drain_Boots` (`Tier 1.5.dm:252-323`):
/// *"Increases Max Energy gain, increases Energy drain."*
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// Vestida, cada peca: tira `2 * BaseDrain` de Ki por segundo (o `Ticker()` dela); soma um ao `HasEnergyDrain`, que
/// tira mais `0.0001 * MaxKi` por volta da regeneracao (`Stats/Ki.dm:47`); soma 0,05 ao gene "Energy Level" (o
/// `KiMod`, que multiplica todo ganho de energia maxima); e aparece no corpo (`overlayList += icon`). Uma de cada
/// tipo por corpo.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * O `BPrestriction += 1.25` NAO VEM. La ele e escrito no `Equip()` e apagado no tique seguinte: o tique dos
///     pesos reescreve `BPrestriction` inteiro, com peso ou sem (`Stats.dm:384-388`, aqui `Fighter.WeightTick`). O
///     que sobrevive no original e o dreno e o `KiMod` -- e e o que se porta.
///   * O DRENO PELO `HasEnergyDrain` e cobrado por SEGUNDO, junto do outro: o tique de Ki deste port nao e o de la.
///   * VESTIDA E UM ESTADO DO ITEM (`Energy_Drain_Gloves|on`), e o bonus de `KiMod` acompanha o que esta na mochila
///     a cada segundo (`Fighter.drenoVestido` guarda quantas pecas ja estao somadas): a peca que sai da mochila por
///     qualquer caminho -- largada, roubada, guardada num bau -- leva o bonus embora.
/// ==================================================================================
/// </summary>
public static class DrenoDeEnergia
{
	public const string Luvas = "Energy_Drain_Gloves", Botas = "Energy_Drain_Boots";

	public const string AcaoVestir = "dreno_vestir";

	/// <summary>`usr.genome.add_to_stat("Energy Level", 0.05)`, por peca vestida.</summary>
	public const double KiModPorPeca = 0.05;

	/// <summary>`user.Ki -= 2 * user.BaseDrain`, por segundo e por peca.</summary>
	public const double DrenoPorPeca = 2;

	/// <summary>`Ki -= 0.0001*HasEnergyDrain*MaxKi`, por peca.</summary>
	public const double FracaoDoKiMaximoPorPeca = 0.0001;

	public static bool EhPeca(string id) => TechDeBolso.Base(id) is Luvas or Botas;

	public static bool Vestida(string id) => EhPeca(id) && TechDeBolso.Campo(id, 0) == "on";

	public static string Escrever(string @base, bool vestida) => vestida ? TechDeBolso.Com(@base, "on") : @base;

	/// <summary>Quantas pecas este corpo veste (no maximo uma de cada tipo conta), e quais.</summary>
	public static (bool Luvas, bool Botas) Vestidas(Inventario mochila)
	{
		bool luvas = false, botas = false;
		foreach (Pilha p in mochila.Pilhas)
		{
			if (p.Quantidade <= 0 || !Vestida(p.Id)) continue;
			if (TechDeBolso.Base(p.Id) == Luvas) luvas = true; else botas = true;
		}
		return (luvas, botas);
	}

	/// <summary>O Ki que <paramref name="pecas"/> pecas tiram deste corpo por segundo.</summary>
	public static double DrenoPorSegundo(Fighter f, int pecas) =>
		pecas <= 0 ? 0 : pecas * (DrenoPorPeca * f.BaseDrain() + FracaoDoKiMaximoPorPeca * f.MaxKi);

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X2): a peca vestida nao drena nem treina nada -- a roupa sem acao da v0.2.0.</summary>
	public static bool NaoFazNadaDeTeste;
}
