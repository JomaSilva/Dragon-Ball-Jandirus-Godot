namespace Jandirus.Core.Ranks;

/// <summary>
/// O KIT DO DEUS DA DESTRUICAO -- os numeros e as regras PURAS de `Code/Modules/Ranks/GodOfDestruction.dm`.
///
/// ============================ O QUE MORA AQUI, E O QUE NAO ============================
/// Aqui estao os `#define GOD_*` do topo do arquivo do DM (`:18-39`), um por constante, e as contas que nao
/// precisam de servidor (o Hakai pega? o surto dispara?). Quem sabe QUEM carrega o titulo e o servidor (o
/// cargo mora na conta -- `GameServer.Ranks.cs`), e quem aplica os efeitos e o `GameServer.KitDoDeus.cs`.
///
/// O que ja tinha casa NAO se repete aqui: o duelo formal e as tarefas (`PortasDeCargo.cs` +
/// `GameServer.CargoDuelo.cs`), o folego no vacuo (`World/Vacuo.cs`, `CargosQueRespiram`) e o fator de 1,25
/// das formas do Poder da Destruicao (`Forms/Disciplinas.cs`, `AmpliacaoDoCargo`).
///
/// "Perdeu o titulo = perde TUDO na hora" (`:10`) e a regra que amarra o arquivo: nada daqui e do
/// personagem, e do TRONO. Por isso nenhuma destas habilidades e skill de livro (`DadivaDeCargo` deixa o kit
/// vazio de proposito): cada verbo pergunta ao servidor "voce E o Deus agora?" na hora de usar.
/// ======================================================================================
/// </summary>
public static class KitDoDeus
{
	/// <summary>A chave do cargo em `Cargos.Todos` -- o `God_Of_Destruction` do DM.</summary>
	public const string Cargo = "godofdestruction";

	// =====================================================================
	// OS VERBS -- `god_apply_powers` (`:119-133`)
	// =====================================================================
	/// <summary>
	/// Os ids dos verbs do kit no canal de habilidade (`C2S.Habilidade`), um por verb que o `god_apply_powers` entrega:
	/// `God_Hakai`, `God_Power_Flick`, `God_Fury_Beam`, `God_Destroyer_Sphere` e `God_Energy_Nullification`. (O sexto,
	/// `God_Fury`, e o <see cref="IdDaFury"/>.) A chave de cada um na tabela de requisitos e `hab:` + o id.
	/// </summary>
	public const string VerbHakai = "god_hakai", VerbFlick = "god_flick", VerbFuria = "god_furia",
						VerbEsfera = "god_esfera", VerbAnular = "god_anular";

	/// <summary>
	/// Os dois verbs que o titulo EMPRESTA ao Deus que trilha o Instinto (`:134-138`): `Power_of_Destruction` (a Aura
	/// of Destruction) e `Hakai_Infusion`, a <see cref="EmprestimoDoCaminho"/> de eficiencia. Tem id proprio porque a
	/// chave decide quem VE o botao: os de quem trilha a Destruicao (`ue_toggle`, `ue_hakai`) sao da disciplina, estes
	/// sao do titulo.
	/// </summary>
	public const string VerbAuraEmprestada = "god_aura", VerbInfusaoEmprestada = "god_infusao";

	/// <summary>
	/// Os nomes dos dois tiros na tabela de arte (`Combat.ArteDeProjetil`): sao os dos verbs do DM, como os de toda
	/// tecnica que atira.
	/// </summary>
	public const string TiroDaFuria = "God_Fury_Beam", TiroDaEsfera = "God_Destroyer_Sphere";

	// =====================================================================
	// A FORMA -- `obj/buff/GodFury` (`:388-419`)
	// =====================================================================
	/// <summary>
	/// O id da forma Fury no <see cref="Forms.Catalogo"/>. **E CONTRATO**: o NPC Bills e qualquer outro sistema a
	/// usam por este id.
	/// </summary>
	public const string IdDaFury = "god_fury";

	/// <summary>`GOD_FURY_MULT 72` (`:18`): o multiplicador da Fury. E fixo -- nao ha maestria nem curva.</summary>
	public const double FuryMult = 72;

	/// <summary>
	/// O roxo da aura colada: `color = rgb(190, 80, 255)` sobre a `god - grey.dmi` (`:389-390`). Em hexa, sem `#`.
	/// </summary>
	public const string RoxoDaFury = "be50ff";

	// =====================================================================
	// O HAKAI -- `god_hakai_try` (`:182-194`)
	// =====================================================================
	/// <summary>`GOD_HAKAI_TORSO_PCT 15` (`:19`): o torso do alvo tem de estar NESTE percentual do maximo, ou abaixo.</summary>
	public const double HakaiTorsoPct = 15;

	/// <summary>`GOD_HAKAI_BP_FRAC 0.75` (`:20`): o BP expresso do alvo tem de ser no maximo esta fracao do do Deus.</summary>
	public const double HakaiFracaoDeBp = 0.75;

	/// <summary>
	/// `GOD_HAKAI_REVIVE_MULT 2` (`1A Defines.dm:45`): quem foi apagado por Hakai paga o revive do Enma vezes isto
	/// (`SkyNPCs.dm:189`). A marca (`hakai_mark`) sai junto com a volta a vida (`Death.dm:148`).
	/// </summary>
	public const double HakaiPrecoDoRevive = 2;

	/// <summary>
	/// O HAKAI PEGA ESTE ALVO? Os dois criterios do `god_hakai_try`, na ordem dele:
	///
	///     if(T.health / T.maxhealth > GOD_HAKAI_TORSO_PCT / 100) return 0     // :190
	///     if(M.expressedBP > expressedBP * GOD_HAKAI_BP_FRAC) return 0        // :192
	///
	/// As duas comparacoes sao as de la: exatamente 15% de torso PEGA, e exatamente 75% do BP PEGA.
	/// Torso sem vida maxima (`T.maxhealth <= 0`, `:189`) nao pega.
	/// </summary>
	public static bool HakaiPega(double vidaDoTorso, double vidaMaxDoTorso, double bpDoAlvo, double bpDoDeus)
	{
		if (vidaMaxDoTorso <= 0) return false;
		if (vidaDoTorso / vidaMaxDoTorso > HakaiTorsoPct / 100) return false;
		return bpDoAlvo <= bpDoDeus * HakaiFracaoDeBp;
	}

	// =====================================================================
	// O GOD'S TEMPER -- `god_temper_hit` (`:363-383`)
	// =====================================================================
	/// <summary>`GOD_TEMPER_HITS 10` (`:22`): danos seguidos SEM bloquear que disparam o surto.</summary>
	public const int TemperGolpes = 10;

	/// <summary>`GOD_TEMPER_PCT 0.10` (`:23`): o surto soma esta fracao do ataque e da defesa fisica EFETIVOS.</summary>
	public const double TemperPct = 0.10;

	/// <summary>`GOD_TEMPER_TICKS 100` (`:24`): o surto dura 10 s.</summary>
	public const double TemperSegundos = 10;

	/// <summary>`GOD_TEMPER_CD 300` (`:25`): 30 s entre dois surtos, contados do INICIO do surto (`:369`).</summary>
	public const double TemperRecargaSegundos = 30;

	/// <summary>O id do surto no registro de buffs do servidor (`god_temper_on`).</summary>
	public const string IdDoTemper = "God_Temper";

	/// <summary>
	/// O SURTO DISPARA NESTE GOLPE? A linha do meio do `god_temper_hit` (`:366`):
	///
	///     if(god_temper_hits &lt; GOD_TEMPER_HITS || god_temper_on || world.time &lt; god_temper_cd) return
	///
	/// O contador NAO zera enquanto o surto esta de pe ou em recarga -- ele continua subindo, e o primeiro dano
	/// sem guarda depois da recarga dispara de novo. Quem zera e a GUARDA (`:364`) e o proprio disparo (`:367`).
	/// </summary>
	public static bool TemperDispara(int golpes, bool surtoDePe, bool emRecarga) =>
		golpes >= TemperGolpes && !surtoDePe && !emRecarga;

	// =====================================================================
	// OS DOIS TIROS -- `God_Fury_Beam` (`:258-288`) e `God_Destroyer_Sphere` (`:301-331`)
	// =====================================================================
	/// <summary>`GOD_FURYBEAM_KI 40` (`:26`): o custo do Destruction God's Fury, vezes o `BaseDrain`.</summary>
	public const double FuriaKi = 40;

	/// <summary>`A.basedamage = 1.5` (`:276`).</summary>
	public const double FuriaBaseDano = 1.5;

	/// <summary>`GOD_SPHERE_KI 60` (`:27`): o custo da Destroyer Sphere, vezes o `BaseDrain`.</summary>
	public const double EsferaKi = 60;

	/// <summary>`A.basedamage = 2` (`:320`).</summary>
	public const double EsferaBaseDano = 2;

	/// <summary>`maxdistance = 30` / `distance = 30`: o alcance de todo `obj/attack` que nao escreve o dele (`objects.dm:17-18`).</summary>
	public const double AlcanceDosTiros = 30;

	/// <summary>
	/// `walk(A, A.dir, 2)` (`:286`, `:329`): o lag do passo dos dois tiros, em decimos de segundo. Na moeda deste port
	/// (`ReceitaDeProjetil.Velocidade`, onde `lag = max(1, round(4 - speed))`) lag 2 e `speed` 2.
	/// </summary>
	public const double VelocidadeDosTiros = 2;

	/// <summary>
	/// `sleep(max(round(usr.Eactspeed / 3), 1))` (`:269`, `:313`): a "carga" dos dois tiros, em TIQUES de 0,1 s -- um
	/// terco da velocidade de acao, com piso de um tique.
	/// </summary>
	public static double TiquesDeCarga(double eactspeed) => Math.Max(Stats.DmMath.Round(eactspeed / 3), 1);

	/// <summary>
	/// O QUE UM DOS DOIS TIROS COBRA DESTE CORPO, em Ki: `GOD_FURYBEAM_KI * usr.BaseDrain` (`:263-264`) ou
	/// `GOD_SPHERE_KI * usr.BaseDrain` (`:306-307`). E a conta do VERB (`GameServer.AtirarDoKit`) e a que a IA le pra
	/// decidir (`Ai.KitDoDeusNaIa`): uma expressao so, pra o preco que se decide ser o preco que se paga.
	/// </summary>
	public static double CustoDoTiro(bool esfera, Stats.Fighter f) => (esfera ? EsferaKi : FuriaKi) * f.BaseDrain();

	// =====================================================================
	// O POWER FLICK -- `God_Power_Flick` (`:225-245`)
	// =====================================================================
	/// <summary>`NormDamageCalc(M) * 2` (`:238`): o peteleco vale dois golpes pesados sem carga.</summary>
	public const double FlickGolpes = 2;

	/// <summary>`M.kbdur = 9` (`:244`): por quantos tiques de 0,1 s o corpo voa.</summary>
	public const int FlickTiquesDeVoo = 9;

	// =====================================================================
	// A ENERGY NULLIFICATION -- `God_Energy_Nullification` (`:336-357`)
	// =====================================================================
	/// <summary>`GOD_NULLIFY_CD 3000` (`:28`): 5 minutos REAIS de recarga -- e ela so comeca se algo foi anulado (`:351`).</summary>
	public const double AnulacaoRecargaSegundos = 300;

	/// <summary>`GOD_NULLIFY_RANGE 8` (`:29`): o `view(8)` da anulacao, em tiles.</summary>
	public const int AnulacaoTiles = 8;

	/// <summary>
	/// ESTE ATAQUE DE KI E APAGADO? `if(A.BP < usr.expressedBP)` (`:346`): *"contem menos energia que voce possui
	/// agora"*. Estrito: um ataque com exatamente o poder do Deus NAO some.
	/// </summary>
	public static bool Anula(double bpDoAtaque, double bpDoDeus) => bpDoAtaque < bpDoDeus;

	// =====================================================================
	// OS CAMINHOS -- `ue_kit_factor` (`UltraEgo.dm:188-192`)
	// =====================================================================
	/// <summary>
	/// `GOD_PATH_BORROW 0.25` (`1A Defines.dm:48`): o Deus que trilha o INSTINTO recebe a Aura of Destruction e a Hakai
	/// Infusion EMPRESTADAS a esta eficiencia, movidas pela precisao do Ultra Instinto dele, sem as formas.
	/// (O outro lado -- `GOD_PATH_BOOST`, o 1,25 de quem trilha a Destruicao -- e o
	/// <see cref="Forms.Disciplinas.AmpliacaoDoCargo"/>.)
	/// </summary>
	public const double EmprestimoDoCaminho = 0.25;

	/// <summary>
	/// O FATOR DO KIT DA DESTRUICAO -- o `ue_kit_factor(M)` do DM, inteiro:
	///
	///     if(M.ue_learned) return god_is_god(M) ? (1 + GOD_PATH_BOOST) : 1     // 1,25 ou 1
	///     if(M.ui_learned &amp;&amp; god_is_god(M)) return GOD_PATH_BORROW             // 0,25
	///     return 0                                                            // nao tem o kit
	///
	/// Ele multiplica TUDO o que o Poder da Destruicao faz: a reducao de dano, o ki devorado, o recuo, a
	/// explosao, o Unbound Ego, a restauracao, a penetracao e o corte de deflexao da infusao, e o que ela devora
	/// num embate de raios.
	/// </summary>
	public static double FatorDoKit(bool trilhouADestruicao, bool trilhouOInstinto, bool portaOTitulo)
	{
		if (trilhouADestruicao) return portaOTitulo ? 1 + Forms.Disciplinas.AmpliacaoDoCargo : 1;
		if (trilhouOInstinto && portaOTitulo) return EmprestimoDoCaminho;
		return 0;
	}
}
