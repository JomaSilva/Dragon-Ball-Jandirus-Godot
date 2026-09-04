using Godot;
using Jandirus.Core;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G10 -- OS GOLPES DO MOLDE DO G7 QUE O CENSO ACHOU MUDOS (e os tres da Trindade).
///
/// ============================ DE ONDE VEM, VERB A VERB ============================
/// O censo (`audit_final.md`, TABELA 3 / grupo F2) achou vinte verbos que a arvore ja concede e o
/// servidor nao atendia. Dezenove entram aqui; nenhum inventa entidade nova -- cada um e uma RECEITA
/// (custo, alcance, dano somado, recarga, requisito) pendurada nas pecas que ja existem:
///
///     verb                    DM                                   a peca do port
///     ----------------------  -----------------------------------  ---------------------------------------
///     Shock                   Assassain Skills.dm:2-26             AbrirPunhoG7 + GolpeG3 + dano residual (relogio G10)
///     Reverb                  Assassain Skills.dm:27-51            idem, residual ESPALHADO (EspalharDanoG3)
///     Precise_Explosion       Assassain Skills.dm:52-72            idem, dano atrasado NUM membro (FerirUmMembroG10)
///     Hokuto_Hyakuretsu_Ken   Assassain Skills.dm:73-119           duas metades: folego+ultiCD, depois a moldura de punho
///     Trip                    Assassain Skills.dm:175-193          EspalharDanoG3 nao-letal + Stun (sem rolagem)
///     Revenge_Demon           Beserker Skills.dm:2-38              GolpeG3 x2 + Arremessar (o funil unico)
///     Gigantic_Spike          Beserker Skills.dm:39-84             AlternarAgarrao (pega+levanta) + corrida + GolpeG3 + DerrubarCelula
///     Power_Drag              Beserker Skills.dm:85-121            AlternarAgarrao + corrida carregando + GolpeG3
///     Seismic_Press           Beserker Skills.dm:122-146           GolpeG3 + DerrubarCelula em raio + Stun
///     Clench/Hold/Power_Slam/Suplex  Wrestling Skills.dm:2-63      AlternarAgarrao + GolpeG3 no PRESO + contador/Stun
///     Rapid_Movement/Zanzoken_Dash   speedy.dm:135-161             AvancarG3 de tres tiles + AnunciarZanzo
///     Zanzoken_Combo          Physical Skills.dm:8-25              teleporte pra tras do alvo (sem golpe)
///     Zanzoken_Rush           Martial Skill Attacks.dm:55-98       o Light Buster em LACO (teleporte + GolpeG3 por salto)
///     Taunt/Counter_Taunt/Slap  Bodybuilding.dm:191-230            Mirar/rancor/presa, Stun, FerirUmMembroG10
///
/// ============================ O QUE ESTE LOTE NAO ESCREVE ============================
/// Nao ha segundo funil de golpe (<see cref="GolpeG3"/> e o unico), de agarrao
/// (<see cref="AlternarAgarrao"/> e o gesto, <see cref="LevarNoColo"/> leva o corpo) nem de arremesso
/// (<see cref="Arremessar"/>). O unico caminho NOVO de dano e o <see cref="FerirUmMembroG10"/>, que e
/// o `damage_mob` do DM (calcs.dm:168-176): dano DIRETO, sem rolagem, num membro sorteado pela zona
/// mirada. O port tinha o irmao dele (<see cref="EspalharDanoG3"/>, o `SpreadDamage`) e nao ele --
/// e Shock, Precise_Explosion e Counter_Taunt sao exatamente `damage_mob`.
///
/// ============================ OS DEFEITOS DO DM: O QUE O DONO MANDOU CONSERTAR, E O QUE FICA ============================
/// Este lote nasceu MANTENDO os defeitos do original com o numero a vista (a regra da casa: consertar e
/// decisao do dono). Em 2026-09-02 o dono decidiu, com estas palavras -- *"corrija esses bugs q vc
/// citou"* --, e cinco deles passaram a fazer o que o NOME e a DESCRICAO do verb prometem. A citacao do
/// DM fica em cada um: e a prova de que o desvio e consciente.
///
///   CONSERTADOS (decisao do dono, 2026-09-02):
///   * `Revenge_Demon` lia `grabbee.Ephysoff` sem ninguem agarrado (`:27`) -> runtime -> o
///     `damage_mob` do `:30` nunca rodava. Aqui roda, com a ofensiva+tecnica do ALVO no divisor
///     (o `grabbee` e residuo de copiar-e-colar dos verbs de agarrao): <see cref="DanoExtraRevengeDemonG10"/>.
///   * `Hokuto` faz `if(BarrageAttack(...))` e o `BarrageAttack` nao tem `return` (`attack_proc.dm:120`)
///     -> o `damage_mob(dmg)` + `damage_mob(70)` do `:113-116` nunca rodavam. Aqui, se a rajada ENCOSTOU,
///     o `NormDamageCalc` entra na hora e os 70 um segundo depois (<see cref="GolpeFinalDoHokutoG10"/>).
///     O `beatdown = round(unarmedskill/5)` foi REMOVIDO (ver <see cref="HokutoG10"/>: a maestria que o
///     alimenta nao existe neste port, e sem ela o proprio DM da zero).
///   * `Gigantic_Spike` batia em `target` e nao em `grabbee` (`:72`). Aqui esmaga quem esta NOS BRACOS.
///   * `Clench`/`Hold` escreviam `grabbee.grabCounter = max(0, grabCounter - N)` lendo o contador de QUEM
///     APERTA (`:13`, `:25`) -- zeravam o contador do preso. Aqui tiram N do contador DO PRESO.
///   * `get_me_a_grab(1)` com alguem JA seguro (modo 1) devolvia TRUE sem levantar (`Grabbing.dm:332`);
///     `Gigantic_Spike`/`Power_Drag` cobravam e nada acontecia. Aqui quem ja segura LEVANTA e o golpe sai
///     (<see cref="AgarrarSePrecisoG10"/>).
///
///   MANTIDOS (o dono nao os citou; ficam com o numero a vista):
///   * `Zanzoken_Dash` liga `rapidmovement=1`, chama `rapidProc()` (que nao dorme) e desliga: o ramo do
///     `movement handler.dm:118` nunca ve a flag. E o MESMO verb que o Rapid_Movement.
///   * `Taunt`/`Slap`/`Counter_Taunt` conferem `Ki >= kireq` e NAO descontam (`Bodybuilding.dm:194-230`).
///   * `Zanzoken_Combo` cobra ANTES de olhar se ha alvo no alcance (`Physical Skills.dm:12-15`).
///
/// A OUTRA EXCECAO segue um precedente do proprio port: `Rapid_Movement` confere `kiReq`
/// e desconta `kiReq*BaseDrain` (`speedy.dm:147-149`, BaseDrain AO QUADRADO). E a mesma familia que o
/// G7 ja fechou quatro vezes (Scattering_Bullet, Guided Ball, Kill Driver, cura): cobra-se o que se
/// confere.
///
/// ============================ O QUE NAO CHEGA INTEIRO, E ESTA DECLARADO ============================
///   * `Power_Drag` da N golpes de `(base+5)/N` (`AttackMultiple(..., m_dist)` divide por `isBarrage`);
///     o funil do port nao divide o dano BASE, entao os N golpes viram UM de `base+5` -- o mesmo total.
///   * `Hokuto` despeja ATE 100 golpes de `dano/100` (`BarrageAttack(..., 100, 1)`): pelo mesmo motivo,
///     vira UM golpe inteiro; o que fica dele e o que o jogador sente -- os 10 s de atordoamento.
///   * `ThrowMe(dir, 1)` e um tile; o <see cref="Arremessar"/> anda dois por tique e o menor voo e um
///     tique: o Revenge_Demon joga dois tiles.
///   * O `zanzorange` do DM e recalculado a cada 50 tiques do efetor e nasce em 1; aqui sai da conta
///     ao vivo (<see cref="ZanzorangeG10"/>).
///   * O exp do Zanzoken_Rush (`if(savant.currush) exp+=1` a cada 2 tiques, tambem durante a exaustao)
///     entra por salto e de uma vez no comeco da exaustao -- o mesmo total, sem um relogio a mais.
///   * O `rushmod` dos degraus 1-3 (`niveis.json`) e escrito num campo que o `Fighter` ainda nao tem
///     (lote A); enquanto ele nao existe, <see cref="RushmodG10"/> le o degrau direto.
///   * O menu do CLIENTE so monta botao pra verb de SKILL (`Habilidades.DasSkills`): os dezesseis
///     verbos de DEGRAU deste lote (como os treze do G7) ficam sem botao ate o cliente conhecer niveis.
/// ==============================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// NUMEROS DO DM
	// =====================================================================
	/// <summary>`view(10)` dos tres verbs da Trindade (`Bodybuilding.dm:197`, `:210`, `:224`), em pixels.</summary>
	private const float RaioDaProvocacaoG10 = 10 * ZoneCollision.TileSize;

	/// <summary>`get_dist(usr, usr.target) &lt; 20` do `rapidProc` (`speedy.dm:148`) e do rush (`:63`).</summary>
	private const float AlcanceDaCorridaG10 = 20 * ZoneCollision.TileSize;

	/// <summary>`/effect/stun/Hundred_Fists`: `duration = 40` tiques (`Movement Effects.dm:101-103`) -- 4 s.</summary>
	private const double HundredFistsSegundosG10 = 4.0;

	/// <summary>`ThrowMe(dir, 1)` (`Beserker Skills.dm:20`): um tile. O menor voo do funil e um tique.</summary>
	private const int TiquesDoArremessoDoDemonioG10 = 1;

	/// <summary>
	/// O `damage_mob(target, dmg*BPModulus/4)` do Revenge_Demon (`Beserker Skills.dm:21-30`):
	///
	///     base = Ephysoff
	///     phystechcalc         = Ephysoff + Etechnique        se Ephysoff > 1 || Etechnique > 1 (senao null -> 0)
	///     opponentphystechcalc = X.Ephysoff + X.Etechnique    idem, pro outro (null -> o DamageCalc poe 1)
	///     dmg = DamageCalc(phystechcalc, opponentphystechcalc, base) = up/down * base     (`calcs.dm:1-7`)
	///     damage_mob(target, dmg * BPModulus(expressedBP, target.expressedBP) / 4)
	///
	/// NO DM ISTO NUNCA RODAVA: o `X` do divisor e `grabbee` (`:27`), e ninguem esta agarrado num verb que
	/// nao agarra -- "Cannot read null.Ephysoff", e o proc morria tres linhas antes do `damage_mob`. O
	/// `grabbee` e residuo de copiar-e-colar dos verbs de luta livre; a pessoa em que o golpe bate e
	/// `target`, e e a ofensiva+tecnica DELE que entra no divisor. Por decisao do dono (2026-09-02,
	/// "corrija esses bugs q vc citou") o dano roda, com o alvo no lugar do agarrado. (O que o runtime
	/// deixava pra tras -- `knockbackon` preso em 0 e `target.stagger` preso em +1 -- e residuo de crash,
	/// nao comportamento, e nao veio.)
	/// </summary>
	private static double DanoExtraRevengeDemonG10(Fighter a, Fighter alvo)
	{
		double up = a.Ephysoff > 1 || a.Etechnique > 1 ? a.Ephysoff + a.Etechnique : 0;
		double down = alvo.Ephysoff > 1 || alvo.Etechnique > 1 ? alvo.Ephysoff + alvo.Etechnique : 1;
		double dmg = up / down * a.Ephysoff;
		return dmg * CombatMath.BpModulus(a.expressedBP, alvo.expressedBP) / 4;
	}

	/// <summary>
	/// `if(BarrageAttack(...)) { damage_mob(target, NormDamageCalc(target)); sleep(10); damage_mob(target, 70) }`
	/// (`Assassain Skills.dm:112-116`). O `BarrageAttack` termina em `attacking=0` sem `return`
	/// (`attack_proc.dm:120-155`): devolvia null e o bloco nunca entrava. Por decisao do dono (2026-09-02)
	/// o bloco entra quando a rajada ENCOSTOU (o `firstBarrage` do `:139` -- o primeiro `doAttack` >= 2):
	/// o `NormDamageCalc` (`calcs.dm:9-21`, que e o <see cref="CombatMath.DanoBase"/>) na hora, e estes 70
	/// um segundo depois (`sleep(10)`), direto num membro, pelo relogio do lote.
	/// </summary>
	private const double GolpeFinalDoHokutoG10 = 70;

	/// <summary>`sleep(10)` entre o `NormDamageCalc` e o golpe final de 70 (`Assassain Skills.dm:115`).</summary>
	private const long AtrasoDoGolpeFinalDoHokutoMsG10 = 1000;

	/// <summary>`view(N)` do BYOND nao passa de 34 -- e o teto do `ArrasarEmVoltaG10`, que sem ele cresceria com o `Ephysoff`.</summary>
	private const int RaioMaximoDeViewG10 = 34;

	private const string PathDoRushG10 = "/datum/skill/MartialSkill/Zanzoken_Rush";

	// =====================================================================
	// ESTADO DO LOTE
	// =====================================================================
	/// <summary>`ultiCD` (`Assassain Skills.dm:79`, `:104`): a recarga PROPRIA dos "melee special skills".</summary>
	private readonly Dictionary<int, long> _ultiG10 = [];

	/// <summary>Um Zanzoken Rush em andamento (`currush == 1`).</summary>
	private sealed class RushG10
	{
		public int Alvo;
		public int Faltam;
		public long ProximoMs;
		public long PassoMs;
		public double ExpPorSalto;
		public double ExpNaExaustao;
		public long ExaustaoMs;
	}

	/// <summary>Um dano que o DM entrega DEPOIS (`spawn while(a > 0 && oldt)`, `sleep(20)`).</summary>
	private sealed class AtrasoG10
	{
		public int Autor, Alvo;
		public long QuandoMs;
		public double Dano;
		public bool Espalhado;   // SpreadDamage (todos os membros) ou damage_mob (um membro)
		public bool Letal;
		public string Nome = "";
	}

	private readonly Dictionary<int, RushG10> _rushG10 = [];
	private readonly Dictionary<int, long> _rushProntoG10 = [];   // `currush == 2` ate quando
	private readonly List<AtrasoG10> _atrasosG10 = [];

	// =====================================================================
	// REGISTRO
	// =====================================================================
	private void RegistrarTecnicasG10()
	{
		IniciarLote("G10");
		Vivo("Shock", ChoqueG10);
		Vivo("Reverb", ReverberacaoG10);
		Vivo("Precise_Explosion", ExplosaoPrecisaG10);
		Vivo("Hokuto_Hyakuretsu_Ken", HokutoG10);
		Vivo("Trip", RasteiraG10);
		Vivo("Revenge_Demon", DemonioDaVingancaG10);
		Vivo("Gigantic_Spike", EspigaoGiganteG10);
		Vivo("Power_Drag", ArrastoBrutalG10);
		Vivo("Seismic_Press", PrensaSismicaG10);
		Vivo("Clench", ApertoG10);
		Vivo("Hold", ChaveG10);
		Vivo("Power_Slam", PowerSlamG10);
		Vivo("Suplex", SuplexG10);
		foreach (string corrida in new[] { "Rapid_Movement", "Zanzoken_Dash" })
			Vivo(corrida, pl => CorridaRapidaG10(pl, corrida));
		Vivo("Zanzoken_Combo", ZanzokenComboG10);
		Vivo("Zanzoken_Rush", ZanzokenRushG10);
		Vivo("Taunt", ProvocacaoG10);
		Vivo("Counter_Taunt", ContraProvocacaoG10);
		Vivo("Slap", TapaG10);

		InscreverNoPulso(() => _rushG10.Count > 0 || _atrasosG10.Count > 0, PulsoG10);
	}

	/// <summary>
	/// O `damage_mob(M, dmg)` DO DM (`calcs.dm:168-176`): dano DIRETO, sem rolagem de pontaria, guarda ou
	/// esquiva, num membro sorteado pela zona mirada (`M.DamageLimb(dmg, src.selectzone, murderToggle)`).
	///
	/// E o irmao do <see cref="EspalharDanoG3"/> (o `SpreadDamage`): aquele bate o MESMO valor em cada
	/// membro; este bate UM membro. O port so tinha o primeiro, e tres verbs deste lote (Shock,
	/// Precise_Explosion, Counter_Taunt) sao o segundo. A cauda (nocaute, morte, Zenkai) e a mesma dele,
	/// pelo mesmo funil da derrota.
	///
	/// O `ArmorCalc(Esuperkiarmor)` e a mitigacao do Ultra Ego do original NAO entram -- o
	/// `EspalharDanoG3` tambem nao os aplica, e dois caminhos de dano direto com regras diferentes de
	/// armadura seria a divergencia que a casa proibe.
	/// </summary>
	private void FerirUmMembroG10(ServerPlayer vitima, ServerPlayer autor, double dano, bool letal)
	{
		CombatState? c = vitima.Combate;
		if (c == null || dano <= 0 || vitima.Ficha.dead || c.Intocavel) return;

		BodyPart? membro = c.Corpo.Sortear(autor.Combate?.ZonaMirada, _rng);
		if (membro == null) return;

		c.EntrarEmCombate();
		if (autor != vitima) autor.Combate?.EntrarEmCombate();
		// O FUNIL ARRANCA: e o `DamageLimb` -> `DamageMe` -> `LopLimb` do DM inteiro numa chamada
		// (ver `CombatState.Ferir`). O membro que zera por golpe letal cai, e a peca sai pelo
		// `AoDecepar` -- antes, este caminho feria e parava.
		c.Ferir(membro, dano, letal);
		c.SincronizarVida();
		if (autor != vitima) MarcarAgressao(vitima, autor);

		ConsequenciaDoDano(vitima, autor, $"com dano direto ({membro.Nome})");   // a mesma cauda do `EspalharDanoG3`
	}

	/// <summary>
	/// `get_me_a_grab(grb_type)` (`Grabbing.dm:331-353`): se JA segura alguem, devolve TRUE e nao mexe no
	/// modo; senao pega quem esta na frente, e com `grb_type` levanta na mesma hora.
	///
	/// Pelo GESTO que ja existe: o primeiro <see cref="AlternarAgarrao"/> e o `grabbee = M` do `:342`, o
	/// segundo e o `grabMode = 2; grabberSTR *= 1.5` do `:348-351`. Um segundo caminho de "pegar" seria
	/// a segunda resposta pra quem pode ser agarrado.
	///
	/// O "NAO MEXE NO MODO" ERA UM DEFEITO: com alguem so SEGURO (modo 1) e `grb_type = 1`, o DM devolvia
	/// TRUE sem levantar (`:332`), e o `if(grabMode==2)` do Gigantic_Spike/Power_Drag falhava DEPOIS de
	/// cobrar o Ki -- pagava e nada acontecia. Por decisao do dono (2026-09-02, "corrija esses bugs q vc
	/// citou") quem ja segura e precisa carregar LEVANTA aqui, pelo mesmo segundo toque.
	/// </summary>
	private bool AgarrarSePrecisoG10(ServerPlayer pl, bool levantar)
	{
		if (pl.AgarrandoId == 0)
		{
			AlternarAgarrao(pl);
			if (pl.AgarrandoId == 0) return false;
		}
		if (levantar && pl.ModoDoAgarrao == ModoDeAgarrao.Segurando) AlternarAgarrao(pl);
		return true;
	}

	/// <summary>
	/// A ABERTURA DOS VERBS DE AGARRAO (`Wrestling Skills.dm`, `Beserker Skills.dm:39-121`), na ordem do DM:
	///
	///     kireq = Ephysoff*BaseDrain*N
	///     get_me_a_grab(grb_type)                 // ANTES do if: pega quem esta na frente mesmo que depois recuse
	///     if(!med && !train && !KO && Ki>=kireq && !basicCD && grabbee)
	///         basicCD += 15 ; Ki -= kireq
	///
	/// NAO pergunta `canfight` (segurar alguem ja o zera, `movement handler.dm:175`), entao nao passa pelo
	/// <see cref="ProntoPraGolpeG3"/>: quem esta atordoado ainda aperta quem tem nos bracos. A recarga e
	/// a do mob (`_prontoG3`), como em todo punho do jogo.
	/// </summary>
	private bool AbrirGolpeDeAgarraoG10(ServerPlayer pl, double multKi, bool levantar,
										 out double custo, out ServerPlayer? preso)
	{
		AgarrarSePrecisoG10(pl, levantar);
		preso = QuemEuSeguro(pl);

		// A ABERTURA E A DE TODO PUNHO (`AbrirPunhoG7`), sem `canfight` e cobrando so depois de saber
		// que ha alguem preso: a unica pergunta propria destes verbs.
		if (!AbrirPunhoG7(pl, multKi, BasicCdG3, out custo, cobrar: false, exigirCanfight: false)) return false;
		if (preso == null)
		{
			Avisar(pl, "voce precisa ter alguem AGARRADO (aperte agarrar; duas vezes pra carregar).");
			return false;
		}
		CobrarPunho(pl, custo, BasicCdG3);
		return true;
	}

	// =====================================================================
	// 1) O ASSASSINO -- `Assassain Skills.dm`
	// =====================================================================
	/// <summary>
	/// SHOCK (`:2-26`). `kireq = Ephysoff*BaseDrain*8`, `basicCD += 15`, `AttackMultiple(target, 2, ...)` e,
	/// se o golpe FOI DADO, `spawn while(a > 0 && oldt) { damage_mob(oldt, 1 + Ephysoff/2 + Etechnique/2);
	/// a--; sleep(15) }` com `a = 2`: uma dose agora e outra 1,5 s depois, DIRETO no membro mirado.
	///
	/// "FOI DADO" e nao "acertou": o `AttackMultiple` devolve TRUE quando o golpe saiu (`attack cmn.dm:139`),
	/// sem olhar o `hitProc`. O residual entra mesmo com o golpe aparado -- a energia ficou no corpo.
	/// </summary>
	private void ChoqueG10(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 8, BasicCdG3, out double custo)) return;
		ServerPlayer? alvo = AproximarDoAlvo(pl, "chocar");
		if (alvo == null) return;

		Travar(alvo, 0.4);
		GolpeG3(pl, alvo, addDano: 2, nivel: 2);
		if (alvo.Ficha.dead || alvo.Combate.Intocavel) return;

		double dose = 1 + pl.Ficha.Ephysoff / 2 + pl.Ficha.Etechnique / 2;
		FerirUmMembroG10(alvo, pl, dose, pl.Combate.Letal);
		AgendarAtrasoG10(pl, alvo, NowMs() + 1500, dose, espalhado: false, letal: pl.Combate.Letal, nome: "choque");
		Avisar(pl, $"voce crava um punho em {alvo.Name}: {dose:0.#} de dano no membro agora e de novo em 1,5 s (-{custo:0} de energia).");
		Avisar(alvo, $"a energia do golpe de {pl.Name} fica presa no seu corpo.");
	}

	/// <summary>
	/// REVERB (`:27-51`). `kireq = Ephysoff*BaseDrain*12`, `basicCD += 15`, `AttackMultiple(target, 2, ...)` e
	/// `spawn while(a > 0 && oldt) { oldt.SpreadDamage(5 + Ephysoff + Etechnique, murderToggle); a--;
	/// sleep(20) }` com `a = 3`: tres ondas ESPALHADAS, a cada dois segundos, letais se o atacante estiver
	/// no modo letal.
	/// </summary>
	private void ReverberacaoG10(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 12, BasicCdG3, out double custo)) return;
		ServerPlayer? alvo = AproximarDoAlvo(pl, "acertar");
		if (alvo == null) return;

		Travar(alvo, 0.4);
		GolpeG3(pl, alvo, addDano: 2, nivel: 2);
		if (alvo.Ficha.dead || alvo.Combate.Intocavel) return;

		double onda = 5 + pl.Ficha.Ephysoff + pl.Ficha.Etechnique;
		bool letal = pl.Combate.Letal;
		EspalharDanoG3(alvo, pl, onda, letal);
		long agora = NowMs();
		AgendarAtrasoG10(pl, alvo, agora + 2000, onda, espalhado: true, letal: letal, nome: "eco");
		AgendarAtrasoG10(pl, alvo, agora + 4000, onda, espalhado: true, letal: letal, nome: "eco");
		Avisar(pl, $"seu punho reverbera em {alvo.Name}: {onda:0.#} em cada membro, tres vezes, a cada 2 s (-{custo:0} de energia).");
		Avisar(alvo, $"o golpe de {pl.Name} ecoa pelo seu corpo inteiro.");
	}

	/// <summary>
	/// PRECISE EXPLOSION (`:52-72`). `kireq = Ephysoff*BaseDrain*15`, `basicCD += 20`,
	/// `AttackMultiple(target, 2, ..., "sticks a finger into")` e, depois de `sleep(20)`,
	/// `damage_mob(oldt, 70 + Ephysoff + Etechnique)`: dois segundos depois o membro mirado estoura.
	/// </summary>
	private void ExplosaoPrecisaG10(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 15, 2000, out double custo)) return;
		ServerPlayer? alvo = AproximarDoAlvo(pl, "acertar");
		if (alvo == null) return;

		Travar(alvo, 0.4);
		GolpeG3(pl, alvo, addDano: 2, nivel: 2);
		if (alvo.Ficha.dead || alvo.Combate.Intocavel) return;

		double estouro = 70 + pl.Ficha.Ephysoff + pl.Ficha.Etechnique;
		AgendarAtrasoG10(pl, alvo, NowMs() + 2000, estouro, espalhado: false, letal: pl.Combate.Letal, nome: "estouro");
		Avisar(pl, $"voce crava um dedo em {alvo.Name}: em 2 s o membro mirado leva {estouro:0.#} de dano (-{custo:0} de energia).");
		Avisar(alvo, $"o dedo de {pl.Name} deixa alguma coisa pulsando no seu corpo.");
	}

	/// <summary>
	/// HOKUTO HYAKURETSU KEN (`:73-119`) -- o verb tem DUAS metades coladas, e as duas vieram na ordem:
	///
	///   1. (`:76-104`) mao livre, `ultiCD`, `canfight/KO/med/stamina >= 18`, alvo colado;
	///      `target.AddEffect(/effect/stun/Hundred_Fists)` (4 s); `stamina -= 18`; `ultiCD = 18*Eactspeed` tiques.
	///   2. (`:105-119`) a moldura de punho: `kireq = Ephysoff*BaseDrain*20`, `basicCD += 30`; com o alvo
	///      colado, `stunCount += 100` (DEZ segundos), `BarrageAttack(0,0,0,..., 100, 1)` e, SE A RAJADA
	///      ENCOSTOU, `damage_mob(NormDamageCalc)` na hora e `damage_mob(70)` um segundo depois -- o bloco
	///      que o DM nunca entrava (ver <see cref="GolpeFinalDoHokutoG10"/>; decisao do dono, 2026-09-02).
	///
	/// ============================ O `beatdown` FOI REMOVIDO, E POR QUE ============================
	/// `beatdown = round(usr.unarmedskill/5); while(beatdown) MeleeAttack(target, 0.4)` (`:98-102`).
	/// `unarmedskill` e a maestria Unarmed Fighting (`Melee Masteries.dm:262-300`: +0,4 por nivel, ate 40
	/// -> ate 8 golpes), e ela e a UNICA coisa que o escreve: um personagem do DM sem essa maestria
	/// tambem da zero golpes. Este port nao tem as maestrias de melee, entao nao ha substituto que
	/// EXISTA -- `Etechnique` e uma razao (~1..3), nao uma escada 0..40, e `round(Etechnique/5)` seria
	/// zero pra todo mundo: um port de mentira. Removido em 2026-09-02 pela opcao que o dono deu
	/// ("ou remova o beatdown dizendo por que"); no dia em que as maestrias de melee vierem, o numero vem
	/// com elas.
	/// ==================================================================================================
	///
	/// A metade 1 NAO olha o `basicCD`: quem esta com os punhos em espera paga o folego e arma o `ultiCD`
	/// e so entao ouve a recusa da metade 2 -- e assim no original, e esta assim aqui.
	/// </summary>
	private void HokutoG10(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;

		// `if(!unarmed && (weaponeq > 1 || twohanding))` -- o port tem `weaponeq`; `unarmed`/`twohanding` nao.
		if (f.weaponeq > 1) { Avisar(pl, "voce precisa de uma mao livre pra isso."); return; }

		if (EmEspera(pl, _ultiG10, "os golpes especiais de corpo ainda estao em espera")) return;
		long agora = NowMs();
		if (pl.Combate == null || f.dead || f.KO || f.med || pl.Combate.Stun > 0 || f.stamina < 18)
		{
			Avisar(pl, "voce nao consegue usar isso agora.");
			return;
		}

		// `if(!target || get_dist > 1) for(M in oview(1)) target = M` / "You must be next to your target"
		ServerPlayer? alvo = AlvoDeTecnica(pl, ColadoG7);
		if (alvo == null) { Avisar(pl, "voce precisa estar colado no seu alvo pra usar isso."); return; }

		Travar(alvo, HundredFistsSegundosG10);
		f.stamina -= 18;
		_ultiG10[pl.Id] = agora + (long)(18 * f.Eactspeed * TempoDoDm.MsPorTique);

		// ---- a metade 2 ----
		if (!AbrirPunhoG7(pl, 20, 3000, out double custo))
		{
			Avisar(pl, "o folego saiu e a rajada nao: a recusa acima e da segunda metade do golpe.");
			return;
		}
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7) return;   // `if(target in view(1))`

		Travar(alvo, 10.0);   // `stunCount += 100`
		Falar(pl, Protocol.Fala.Diz, "ATATATATATATATATATA!");
		// `BarrageAttack(..., 100, 1)`: ate cem golpes de dano/100 = um golpe inteiro (ver o cabecalho).
		GolpeResultado rajada = GolpeG3(pl, alvo, addDano: 0, nivel: 3);
		Avisar(pl, $"voce despeja a rajada em {alvo.Name}: ele fica dez segundos sem reagir (-18 de folego, -{custo:0} de energia).");
		Avisar(alvo, $"{pl.Name} te enche de socos: voce nao consegue reagir.");

		// `if(BarrageAttack(...)) { damage_mob(target, NormDamageCalc(target)); sleep(10); damage_mob(target, 70) }`
		// (`:112-116`): o bloco que o DM nunca entrava (ver GolpeFinalDoHokutoG10). "Encostou" e o
		// `firstBarrage` -- o primeiro golpe da rajada entrou (acertou, critico ou aparado).
		if (!rajada.Encostou || alvo.Ficha.dead || alvo.Combate.Intocavel) return;
		double golpeFinal = CombatMath.DanoBase(pl.Ficha, alvo.Ficha);
		FerirUmMembroG10(alvo, pl, golpeFinal, pl.Combate.Letal);
		AgendarAtrasoG10(pl, alvo, NowMs() + AtrasoDoGolpeFinalDoHokutoMsG10, GolpeFinalDoHokutoG10,
						 espalhado: false, letal: pl.Combate.Letal, nome: "golpe final do Hokuto");
		Avisar(pl, $"a rajada entrou: {golpeFinal:0.##} direto no membro agora, e {GolpeFinalDoHokutoG10:0} mais em 1 s.");
	}

	/// <summary>
	/// TRIP (`:175-193`). `kireq = Ephysoff*BaseDrain*15`, `basicCD += 15`; colado e SE `!target.flight`:
	/// `stunCount += 30` (3 s), `SpreadDamage(1 + Etechnique, 0)` (nao-letal, sem rolagem) e a frase pra
	/// quem ve. Contra quem voa: nada, e o Ki ja foi.
	/// </summary>
	private void RasteiraG10(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 15, BasicCdG3, out double custo)) return;
		ServerPlayer? alvo = AproximarDoAlvo(pl, "derrubar");
		if (alvo == null) return;

		Travar(alvo, 0.4);
		if (alvo.Voando)
		{
			Avisar(pl, $"{alvo.Name} esta no ar: nao ha chao pra ele tropecar (-{custo:0} de energia).");
			return;
		}

		double dano = 1 + pl.Ficha.Etechnique;
		Travar(alvo, 3.0);
		EspalharDanoG3(alvo, pl, dano, letal: false);
		AvisarPertoG3(pl, 8 * ZoneCollision.TileSize, $"{pl.Name} passa uma rasteira em {alvo.Name}!!!");
		Avisar(pl, $"{alvo.Name} cai: tres segundos sem reagir e {dano:0.#} em cada membro (-{custo:0} de energia).");
	}

	// =====================================================================
	// 2) O BERSERKER -- `Beserker Skills.dm`
	// =====================================================================
	/// <summary>
	/// REVENGE DEMON (`:2-38`). `kireq = Ephysoff*BaseDrain*15`, `basicCD += 15`; com `knockbackon = 0`,
	/// `doAttack(target, null, ..., 1)` e, SE ENTROU: `AttackMultiple(target, 2, ..., "jabs")`,
	/// `target.ThrowMe(dir, 1)` com `ThrowStrength = (expressedBP/2)*Ephysoff*Etechnique` (a mesma
	/// <see cref="Agarrao.ForcaDoArremesso"/> do agarrao) e o `damage_mob(dmg*BPModulus/4)` que o DM
	/// nunca chegava a rodar e aqui roda (<see cref="DanoExtraRevengeDemonG10"/>; decisao do dono,
	/// 2026-09-02). Se o primeiro nao entrou: `stagger += 1; spawn(4) -= 1`.
	/// </summary>
	private void DemonioDaVingancaG10(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 15, BasicCdG3, out double custo)) return;
		ServerPlayer? alvo = AproximarDoAlvo(pl, "acertar");
		if (alvo == null) return;

		Travar(alvo, 0.4);

		// `var/hdkb = knockbackon; knockbackon = 0` -- o soco nao pode arremessar por conta propria: quem
		// arremessa e o verb, um tile, depois do jab. Guardado e devolvido, como o Furacao do lobo.
		bool guardado = pl.Knockback;
		pl.Knockback = false;
		GolpeResultado r = GolpeG3(pl, alvo, addDano: 0, nivel: 2);
		pl.Knockback = guardado;

		if (!r.Encostou)
		{
			TropecarG7(pl);
			Avisar(pl, "o soco nao entra e voce se desequilibra.");
			return;
		}

		GolpeG3(pl, alvo, addDano: 2, nivel: 2);
		// `damage_mob(target, dmg*BPModulus/4)` (`:30`) -- o que o runtime do DM nunca deixava rodar, com a
		// ofensiva+tecnica do ALVO no divisor (ver DanoExtraRevengeDemonG10). Entra ANTES do voo porque o
		// DM o aplica no mesmo instante do `ThrowMe` (o voo e um spawn) e um corpo em voo pode estar
		// intocavel na porta do `FerirUmMembroG10`.
		double extra = DanoExtraRevengeDemonG10(pl.Ficha, alvo.Ficha);
		FerirUmMembroG10(alvo, pl, extra, pl.Combate.Letal);
		AvisarPertoG3(pl, 8 * ZoneCollision.TileSize, $"{pl.Name} arremessa {alvo.Name} pra frente!");
		if (!alvo.Ficha.dead)
			Arremessar(alvo, MeleeArea.Frente(pl.Facing), Agarrao.ForcaDoArremesso(pl.Ficha),
					   TiquesDoArremessoDoDemonioG10);
		Avisar(pl, $"soco, jab na cara e {alvo.Name} voa pra frente (+{extra:0.##} direto no membro; -{custo:0} de energia).");
	}

	/// <summary>
	/// GIGANTIC SPIKE (`:39-84`). `kireq = Ephysoff*BaseDrain*12`, `get_me_a_grab(1)`, `basicCD += 15`; SO
	/// com `grabMode == 2`:
	///
	///     dist = round(Espeed + Etechnique + Ephysoff) ; dmg = 1
	///     while(dist) { dist-- ; dmg++
	///         if(!canmove) break
	///         parede na frente: dmg += 2 ; destrutivel e mais fraca que o BP -> cai ; senao dmg += 2 (e o passo bate nela)
	///         step(src, dir) }
	///     if(grabbee in view(1)) AttackMultiple(target, 16 + dmg, crit, ..., 3) ; turfs em view(Ephysoff/2 + 1) caem
	///     else stagger 0,4 s
	///
	/// O `break` do `else` da parede quebra o `for` dos turfs e NAO o `while` (`:66`): a corrida continua
	/// contando e somando +4 por passo contra uma parede que resiste. Portado como esta.
	/// O corpo carregado vem junto a cada passo pelo <see cref="LevarNoColo"/> -- o `grab()` do original.
	///
	/// ============================ DOIS DEFEITOS DO DM CONSERTADOS (decisao do dono, 2026-09-02) ============================
	/// * O `AttackMultiple(target, ...)` do `:72` batia no MARCADO e nao em quem esta nos bracos: com outro
	///   alvo marcado o preso fazia a viagem inteira e quem apanhava era o outro; sem marcado, ninguem
	///   (`AttackMultiple(null)` devolve na porta). O verb "slams a person into a wall or ground after
	///   picking them up" (`:39`): aqui o esmagado e o PRESO (`grabbee`), que e quem chegou junto.
	/// * `get_me_a_grab(1)` com alguem JA seguro (modo 1) devolvia TRUE sem levantar (`Grabbing.dm:332`) e
	///   o `if(grabMode==2)` falhava: o Ki ia embora e nada acontecia. Aqui quem ja segura LEVANTA
	///   (<see cref="AgarrarSePrecisoG10"/>) e a corrida sai -- o `if(grabMode==2)` deixou de ter ramo falso.
	/// ===========================================================================================================
	/// </summary>
	private void EspigaoGiganteG10(ServerPlayer pl)
	{
		if (!AbrirGolpeDeAgarraoG10(pl, 12, levantar: true, out double custo, out ServerPlayer? preso)) return;

		Fighter f = pl.Ficha;
		int dist = (int)Math.Max(DmMath.Round(f.Espeed + f.Etechnique + f.Ephysoff, 1), 0);
		double dmg = 1;
		int passos = 0, paredes = 0;
		Vec2 frente = MeleeArea.Frente(pl.Facing);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		long agora = NowMs();

		while (dist > 0)
		{
			dist--;
			dmg++;
			if (!PodeMexerOCorpo(pl)) { Avisar(pl, "sua corrida falha porque voce nao consegue se mover!"); break; }

			Vec2 aFrente = pl.Pos + frente * ZoneCollision.TileSize;
			(int cx, int cy) = CelulaDoPonto(aFrente);
			if (mapa != null && mapa.BlockedCell(cx, cy))
			{
				dmg += 2;
				paredes++;
				bool caiu = f.expressedBP >= Empurrao.ResistenciaPadrao && !mapa.NaBorda(cx, cy)
							&& DerrubarCelula(pl.Zone, cx, cy);
				if (!caiu) dmg += 2;
			}
			if (mapa != null && MoveRules.PathOccupied(mapa, pl.Pos, aFrente)) continue;   // o `step` bate na parede
			pl.Pos = aFrente;
			passos++;
			if (preso != null) LevarNoColo(pl, preso, agora);
		}
		if (passos > 0)
		{
			CravarPosicao(pl, pl.Pos);
			AnunciarZanzo(pl, pl.Pos - frente * (passos * ZoneCollision.TileSize), vulto: false);
		}

		if (preso == null || Vec2.Distance(preso.Pos, pl.Pos) > ColadoG7)
		{
			TropecarG7(pl);
			Avisar(pl, "voce falhou porque perdeu o inimigo. Ficou atordoado!");
			return;
		}

		// `AttackMultiple(target, 16 + dmg, 1, null, "slams", null, 3)` (`:72`) -- no PRESO, que e quem fez a
		// viagem nos bracos (o DM batia no marcado: ver o cabecalho).
		GolpeG3(pl, preso, addDano: 16 + dmg, nivel: 3);
		Avisar(pl, $"voce esmaga {preso.Name} no fim da corrida (+{16 + dmg:0}: {passos} passos, {paredes} paredes; -{custo:0} de energia).");
		AvisarSePessoa(preso, $"{pl.Name} te esmaga no chao no fim da corrida!");
		int celulas = RacharChao(pl.Zone, pl.Pos, f.expressedBP,
								 raio: Math.Clamp((int)Math.Floor(f.Ephysoff / 2 + 1), 0, RaioMaximoDeViewG10), chance: 1);
		if (celulas > 0) Avisar(pl, $"o chao em volta racha ({celulas} celulas).");
	}

	/// <summary>
	/// POWER DRAG (`:85-121`). `kireq = Ephysoff*BaseDrain*12`, `get_me_a_grab(1)`, `basicCD += 15`; SO com
	/// `grabMode == 2`: `grabbee.stagger += 1`, `dist = round(Espeed + Etechnique + Ephysoff)` passos na
	/// direcao em que voce olha, cada um com `spawn AttackMultiple(grabbee, 5, ..., "drags", m_dist)`;
	/// se o preso escapou no meio, `stagger` 0,4 s em voce.
	///
	/// OS N GOLPES VIRAM UM: `isBarrage = m_dist` divide cada golpe por N (`attack cmn.dm:132`), e o funil
	/// do port nao divide o dano base. Um `GolpeG3(+5)` no fim do arrasto e o mesmo total que os N do DM.
	///
	/// O `get_me_a_grab(1)` com alguem so SEGURO devolvia TRUE sem levantar e o `if(grabMode==2)` (`:91`)
	/// falhava depois de cobrar -- o mesmo defeito do Gigantic_Spike, consertado no mesmo lugar
	/// (<see cref="AgarrarSePrecisoG10"/>, decisao do dono 2026-09-02): quem ja segura levanta e arrasta.
	/// </summary>
	private void ArrastoBrutalG10(ServerPlayer pl)
	{
		if (!AbrirGolpeDeAgarraoG10(pl, 12, levantar: true, out double custo, out ServerPlayer? preso)) return;

		Fighter f = pl.Ficha;
		Travar(preso, 0.4);
		int dist = (int)Math.Max(DmMath.Round(f.Espeed + f.Etechnique + f.Ephysoff, 1), 0);
		int passos = 0;
		Vec2 frente = MeleeArea.Frente(pl.Facing);
		Vec2 de = pl.Pos;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		long agora = NowMs();

		while (dist > 0)
		{
			dist--;
			if (!PodeMexerOCorpo(pl)) { Avisar(pl, "sua corrida falha porque voce nao consegue se mover!"); break; }
			Vec2 aFrente = pl.Pos + frente * ZoneCollision.TileSize;
			if (mapa != null && MoveRules.PathOccupied(mapa, pl.Pos, aFrente)) continue;
			pl.Pos = aFrente;
			passos++;
			if (preso != null) LevarNoColo(pl, preso, agora);
		}
		if (passos > 0)
		{
			CravarPosicao(pl, pl.Pos);
			AnunciarZanzo(pl, de, vulto: false);
		}

		if (pl.AgarrandoId == 0 || preso == null)
		{
			TropecarG7(pl);
			Avisar(pl, "voce cai porque perdeu o inimigo. Ficou atordoado!");
			return;
		}

		GolpeG3(pl, preso, addDano: 5, nivel: 1);
		Avisar(pl, $"voce arrasta {preso.Name} por {passos} tiles e ele sai machucado (+5; -{custo:0} de energia).");
		AvisarSePessoa(preso, $"{pl.Name} te arrasta pelo chao!");
	}

	/// <summary>
	/// SEISMIC PRESS (`:122-146`). `kireq = Ephysoff*BaseDrain*18`, `basicCD += 15`; colado:
	/// `AttackMultiple(target, 15, ..., "heavily slams")`, todo turf em `view(Ephysoff)` mais fraco que o BP
	/// cai, `stunCount += 20` (2 s).
	/// </summary>
	private void PrensaSismicaG10(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 18, BasicCdG3, out double custo)) return;
		ServerPlayer? alvo = AproximarDoAlvo(pl, "prensar");
		if (alvo == null) return;

		Travar(alvo, 0.4);
		GolpeG3(pl, alvo, addDano: 15, nivel: 3);
		int raio = (int)Math.Floor(pl.Ficha.Ephysoff);
		int celulas = RacharChao(pl.Zone, pl.Pos, pl.Ficha.expressedBP, raio: Math.Clamp(raio, 0, RaioMaximoDeViewG10), chance: 1);
		Travar(alvo, 2.0);

		// o tremor e de quem esta perto (o `emit_Sound('kiplosion.wav')` + a poeira dos turfs)
		float raioPx = (raio + 2) * ZoneCollision.TileSize;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (Vec2.Distance(o.Pos, pl.Pos) <= raioPx) MandarEfeito(o, "terremoto", 600);

		Avisar(pl, $"voce prensa {alvo.Name} (+15): ele fica dois segundos sem reagir e o chao racha em {raio} tiles ({celulas} celulas; -{custo:0} de energia).");
		Avisar(alvo, $"a prensa de {pl.Name} te deixa sem reacao.");
	}

	// =====================================================================
	// 3) A LUTA LIVRE -- `Wrestling Skills.dm` (todos no PRESO, sem `target`)
	// =====================================================================
	/// <summary>
	/// CLENCH (`:2-17`). `kireq = Ephysoff*BaseDrain*9`; `grabbee.stagger += 1`, `AttackMultiple(grabbee, 4)`,
	/// `grabbee.grabCounter = max(0, grabCounter - 4)`.
	///
	/// O `grabCounter` SEM PREFIXO (`:13`) e o de QUEM APERTA -- que nao luta pra escapar de ninguem e vale
	/// zero: o DM ZERAVA a luta do preso em vez de tirar 4 dela. O verb promete "destroys some grab escape
	/// stacks" (`:2`); por decisao do dono (2026-09-02, "corrija esses bugs q vc citou") saem 4 do
	/// contador DO PRESO.
	/// </summary>
	private void ApertoG10(ServerPlayer pl)
	{
		if (!AbrirGolpeDeAgarraoG10(pl, 9, levantar: false, out double custo, out ServerPlayer? preso)) return;
		Travar(preso, 0.4);
		GolpeG3(pl, preso!, addDano: 4, nivel: 2);
		preso!.ContadorDaLuta = Math.Max(0, preso.ContadorDaLuta - 4);
		Avisar(pl, $"voce aperta {preso.Name} (+4) e desfaz parte da luta dele pra escapar (-4; -{custo:0} de energia).");
		AvisarSePessoa(preso, $"{pl.Name} aperta voce: parte do que voce tinha lutado pra sair se desfaz.");
	}

	/// <summary>
	/// HOLD (`:18-32`). `kireq = Ephysoff*BaseDrain*12`; `grabbee.grabCounter = max(0, grabCounter - 15)`,
	/// `AttackMultiple(grabbee, null)`, `grabbee.stunCount += 50` (5 s).
	///
	/// O mesmo `grabCounter` sem prefixo do Clench (`:25`): o DM zerava a luta do preso. Por decisao do dono
	/// (2026-09-02) saem 15 do contador DO PRESO -- "mainly destroys grab stacks" (`:18`).
	/// </summary>
	private void ChaveG10(ServerPlayer pl)
	{
		if (!AbrirGolpeDeAgarraoG10(pl, 12, levantar: false, out double custo, out ServerPlayer? preso)) return;
		preso!.ContadorDaLuta = Math.Max(0, preso.ContadorDaLuta - 15);
		GolpeG3(pl, preso, addDano: 0, nivel: 2);
		Travar(preso, 5.0);
		Avisar(pl, $"voce trava {preso.Name} numa chave: cinco segundos sem reagir e a luta dele pra escapar cai 15 (-{custo:0} de energia).");
		AvisarSePessoa(preso, $"{pl.Name} te prende numa chave: voce nao consegue reagir.");
	}

	/// <summary>
	/// POWER SLAM (`:33-47`). `kireq = Ephysoff*BaseDrain*20`; `grabbee.stagger += 1`,
	/// `doAttack(grabbee, 10, 1, null, "POWER SLAMS", null, 3)` -- crit forcado e `Type = 3`, que no funil
	/// do port e o `nivel 3` (o critico se sorteia dentro do resolvedor, como no Backstab do G7).
	/// </summary>
	private void PowerSlamG10(ServerPlayer pl)
	{
		if (!AbrirGolpeDeAgarraoG10(pl, 20, levantar: false, out double custo, out ServerPlayer? preso)) return;
		Travar(preso, 0.4);
		GolpeG3(pl, preso!, addDano: 10, nivel: 3);
		Falar(pl, Protocol.Fala.Diz, "POWER SLAM!");
		Avisar(pl, $"voce levanta e esmaga {preso!.Name} no chao (+10; -{custo:0} de energia).");
	}

	/// <summary>
	/// SUPLEX (`:48-63`). `kireq = Ephysoff*BaseDrain*15`; `grabbee.stagger += 1`,
	/// `AttackMultiple(grabbee, 5, ..., "SUPLEXES")`, `grabbee.stunCount += 20` (2 s).
	/// </summary>
	private void SuplexG10(ServerPlayer pl)
	{
		if (!AbrirGolpeDeAgarraoG10(pl, 15, levantar: false, out double custo, out ServerPlayer? preso)) return;
		Travar(preso, 0.4);
		GolpeG3(pl, preso!, addDano: 5, nivel: 3);
		Travar(preso, 2.0);
		Avisar(pl, $"SUPLEX em {preso!.Name} (+5): dois segundos sem reagir (-{custo:0} de energia).");
		AvisarSePessoa(preso, $"{pl.Name} te aplica um suplex!");
	}

	// =====================================================================
	// 4) A CORRIDA DE KI -- `speedy.dm:135-161`
	// =====================================================================
	/// <summary>
	/// RAPID MOVEMENT e ZANZOKEN DASH (`:135-142`) sao o MESMO `rapidProc()` (`:144-161`):
	///
	///     if(dashtired) to_chat(...)                       // avisa e NAO devolve
	///     kiReq = 10*BaseDrain/speed
	///     if(Ki >= kiReq && target && target != usr && get_dist < 20 && !KO && !dashtired)
	///         Ki -= kiReq*BaseDrain                        // BaseDrain ao quadrado -- cobra-se o que se confere (precedente do G7)
	///         step(src, get_dir(src,target)) x3
	///     if(!target || get_dist >= 20 || target == usr) "You need a valid target..."
	///
	/// EXIGE ALVO MARCADO (`usr.target`, sem `get_me_a_target()`), como o Lariat. O `dashtired` e o mesmo
	/// que o Lariat do G7 arma (`_prontoG3`, 3 s): esta corrida so o LE -- o `stopDashing()` que o
	/// armaria e chamado de um `testRM()` comentado (`:157-158`), entao a corrida nunca cansa sozinha.
	///
	/// O `Zanzoken_Dash` liga `rapidmovement = 1` antes e desliga depois (`:140-142`); o `rapidProc` nao
	/// dorme, o laco de movimento (`movement handler.dm:118`) nunca ve a flag, e "rodear o inimigo" nao
	/// acontece. Os dois ids caem aqui de proposito.
	/// </summary>
	private void CorridaRapidaG10(ServerPlayer pl, string id)
	{
		Fighter f = pl.Ficha;
		long agora = NowMs();
		bool cansado = EmEspera(pl, _prontoG3, out _);
		if (cansado) Avisar(pl, "voce nao consegue usar isso agora (a investida ainda esta se recompondo).");

		double kiReq = 10 * f.BaseDrain() / Math.Max(f.speed, 0.01);
		ServerPlayer? alvo = Marcado(pl);
		bool alvoValido = alvo != null && alvo != pl && Vec2.Distance(alvo.Pos, pl.Pos) < AlcanceDaCorridaG10;

		if (f.Ki >= kiReq && alvoValido && !f.KO && !cansado && pl.Combate != null)
		{
			ServerPlayer marcado = alvo!;
			f.Ki -= kiReq;
			Avisar(pl, $"voce bombeia Ki no corpo e acelera contra {marcado.Name} (-{kiReq:0.#} de energia).");
			Vec2 de = pl.Pos;
			AvancarG3(pl, marcado, 3 * ZoneCollision.TileSize);
			if (Vec2.Distance(de, pl.Pos) > 0.5f)
				AnunciarZanzo(pl, de, vulto: pl.Livro?.Sabe(PathDoZanzoken) == true);
			return;
		}
		if (!alvoValido) Avisar(pl, "voce precisa de um alvo MARCADO a menos de vinte tiles. (Duplo clique.)");
		else if (!cansado && f.Ki < kiReq) Avisar(pl, $"isso pede {kiReq:0.#} de energia.");
	}

	// =====================================================================
	// 5) OS DOIS TELEPORTES
	// =====================================================================
	/// <summary>
	/// `zanzorange = round(1.2 * max(Ekiskill, Etechnique) * Espeed, -1)` (`misc.dm:55`) -- `round(x, -1)` e o
	/// inteiro mais proximo. Piso 1 (`mob/var zanzorange = 1`, `:101`). O DM so recalcula a cada 50 tiques
	/// do efetor da Afterimage; aqui e ao vivo.
	/// </summary>
	private static int ZanzorangeG10(Fighter f)
		=> Math.Max((int)Math.Round(1.2 * Math.Max(f.Ekiskill, f.Etechnique) * f.Espeed), 1);

	/// <summary>
	/// ZANZOKEN COMBO (`Physical Skills.dm:8-25`). `kireq = Ephysoff*BaseDrain*4`, `basicCD += 15`, e SO
	/// DEPOIS de cobrar: `if(target in view(zanzorange + 2))` -> `tT = get_step(target, get_dir(src, target))`
	/// (o tile do OUTRO lado do alvo, continuando a sua direcao); `if(!tT.density) Move(tT); dir =
	/// get_dir(src, target)` (olhando pra ele); senao `Move(target.loc, target.dir)` -- que contra um mob
	/// denso o `Move` recusa: voce fica onde esta. NAO ha golpe: o combo e o soco que voce da em seguida.
	/// </summary>
	private void ZanzokenComboG10(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 4, BasicCdG3, out double custo)) return;

		int zanzorange = ZanzorangeG10(pl.Ficha);
		ServerPlayer? alvo = AlvoDeTecnica(pl, (zanzorange + 2) * ZoneCollision.TileSize);
		if (alvo == null)
		{
			Avisar(pl, $"ninguem a ate {zanzorange + 2} tiles pra aparecer atras (-{custo:0} de energia, como no original).");
			return;
		}

		Vec2 de = pl.Pos;
		if (PontoLivre(pl.Zone, alvo.Pos + (alvo.Pos - pl.Pos).Normalized() * DistanciaDeParada) is not { } atras)
		{
			Avisar(pl, $"nao ha espaco atras de {alvo.Name}: voce fica onde esta (-{custo:0} de energia).");
			return;
		}

		CravarPosicao(pl, atras);
		pl.Facing = MoveRules.FacingFrom(alvo.Pos - pl.Pos, pl.Facing);
		AnunciarZanzo(pl, de);
		MandarEfeito(alvo, "zanzoken", 300);
		Avisar(pl, $"voce some e reaparece atras de {alvo.Name}, olhando pra ele (-{custo:0} de energia).");
		Avisar(alvo, $"{pl.Name} some e reaparece nas suas costas.");
	}

	/// <summary>
	/// O `rushmod` do lutador: `mob/var rushmod = 1` (`Martial Skill Attacks.dm:2`), escrito pelos degraus
	/// 1-3 da skill (`:38`, `:45`, `:52` -> 2, 3, 4). O `niveis.json` ja traz `rushmod=N` nas `flags` dos
	/// degraus; o campo no `Fighter` e do lote A. Enquanto ele nao existe, o degrau e lido daqui -- do
	/// DADO, e nao de uma escada digitada -- e no dia em que o campo nascer esta funcao vira uma linha.
	/// </summary>
	private static double RushmodG10(ServerPlayer pl)
	{
		double rushmod = 1;
		int nivel = pl.Niveis.Nivel(PathDoRushG10);
		if (RegrasDeNivel.Get(PathDoRushG10) is { } regra)
			foreach (Degrau d in regra.Degraus)
				if (d.Nivel > 0 && d.Nivel <= nivel && d.Flags.TryGetValue("rushmod", out double v))
					rushmod = Math.Max(rushmod, v);
		return rushmod;
	}

	/// <summary>
	/// ZANZOKEN RUSH (`Martial Skill Attacks.dm:55-98`) -- o Light Buster em LACO:
	///
	///     staminaReq = angerBuff*5/(Ephysoff+Etechnique)*BaseDrain     // e Ki, apesar do nome (como o Lariat)
	///     jumpspeed  = Eactspeed*globalmeleeattackspeed/2 tiques ; zrcd = jumpspeed*20
	///     get_me_a_target()
	///     if(Ki >= req && target && target != usr && get_dist < 20 && !KO && currush < 1)
	///         Ki -= req ; rushmax = max(round(rushmod*log(Espeed)), 1) ; currush = 1
	///         while(rushcount < rushmax): !canmove||KO -> break ; z diferente -> break ; teleporta pra
	///             (target.x +-1, target.y +-1) ; Move falhou -> "obstaculo" break ; vira ; MeleeAttack() ; sleep(jumpspeed)
	///         currush = 2 ; sleep(zrcd) ; currush = 0
	///
	/// `log(Espeed)` e o logaritmo NATURAL (o `log(base, x)` do BYOND leva dois argumentos). O primeiro salto
	/// sai agora e os outros entram no relogio do lote, a `jumpspeed` de distancia; a exaustao vale
	/// SEMPRE, ate quando o rush quebrou no meio (nao ha `return` nos `break`).
	/// </summary>
	private void ZanzokenRushG10(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		long agora = NowMs();
		double custo = f.angerBuff * 5 / Math.Max(f.Ephysoff + f.Etechnique, 0.01) * f.BaseDrain();
		double jumpTiques = Math.Max(f.Eactspeed * CombatKnobs.VelocidadeGlobal / 2, 1);
		double zrcd = jumpTiques * 20;

		// `get_me_a_target()`: o marcado, senao o mais proximo colado; depois `get_dist < 20`.
		ServerPlayer? alvo = Marcado(pl) ?? AlvoDeTecnica(pl, PassoDeAproximacaoG7);
		bool alvoValido = alvo != null && alvo != pl && !alvo.Ficha.dead
						  && Vec2.Distance(alvo.Pos, pl.Pos) < AlcanceDaCorridaG10;
		bool emRush = _rushG10.ContainsKey(pl.Id);
		bool exausto = EmEspera(pl, _rushProntoG10, out long restanteDoRush);

		if (f.Ki >= custo && alvoValido && !f.KO && !emRush && !exausto && pl.Combate != null)
		{
			ServerPlayer presa = alvo!;
			Avisar(pl, $"voce tenta aparecer ao lado de {presa.Name}! (-{custo:0.#} de energia)");
			f.Ki -= custo;
			int rushmax = Math.Max((int)DmMath.Round(RushmodG10(pl) * Math.Log(Math.Max(f.Espeed, 1e-9)), 1), 1);
			var r = new RushG10
			{
				Alvo = presa.Id,
				Faltam = rushmax,
				ProximoMs = agora,
				PassoMs = (long)(jumpTiques * TempoDoDm.MsPorTique),
				ExpPorSalto = jumpTiques / 2,       // `exp += 1` a cada 2 tiques do efetor, durante o salto
				ExpNaExaustao = zrcd / 2,           // ...e durante a exaustao inteira (`currush == 2` tambem conta)
				ExaustaoMs = (long)(zrcd * TempoDoDm.MsPorTique),
			};
			_rushG10[pl.Id] = r;
			SaltarG10(pl, r, agora);
			if (_rushG10.ContainsKey(pl.Id)) LigarPulso();
			return;
		}

		if (!alvoValido) Avisar(pl, "voce precisa de um alvo valido a menos de vinte tiles.");
		else if (f.Ki < custo) Avisar(pl, $"voce precisa de pelo menos {custo:0.#} de energia pra usar isso.");
		else if (emRush) Avisar(pl, "voce ja esta usando isso!");
		else if (exausto) Avisar(pl, $"voce ainda esta exausto do rush ({restanteDoRush / 1000.0:0.#}s).");
	}

	/// <summary>Um salto do rush: teleporte pra um vizinho diagonal do alvo, virada, e o soco cru.</summary>
	private void SaltarG10(ServerPlayer pl, RushG10 r, long agora)
	{
		ServerPlayer? alvo = CorpoNaMinhaZona(pl, r.Alvo);
		if (alvo == null || alvo.Ficha.dead || alvo.Combate == null || alvo.Combate.Intocavel)
		{ EncerrarRushG10(pl, r, "seu alvo esta fora de alcance!"); return; }
		if (pl.Ficha.KO || !PodeMexerOCorpo(pl))
		{ EncerrarRushG10(pl, r, "seu ataque falha porque voce nao consegue se mover!"); return; }

		// `locate(target.x + pick(-1,1), target.y + pick(-1,1), target.z)`
		var diagonal = new Vec2(
			alvo.Pos.X + (_rng.Next(2) == 0 ? -ZoneCollision.TileSize : ZoneCollision.TileSize),
			alvo.Pos.Y + (_rng.Next(2) == 0 ? -ZoneCollision.TileSize : ZoneCollision.TileSize));
		if (PontoLivre(pl.Zone, diagonal) is not { } destino)
		{ EncerrarRushG10(pl, r, "seu ataque foi barrado por um obstaculo!"); return; }

		Vec2 de = pl.Pos;
		CravarPosicao(pl, destino);
		pl.Facing = MoveRules.FacingFrom(alvo.Pos - pl.Pos, pl.Facing);
		AnunciarZanzo(pl, de);
		GolpeG3(pl, alvo, addDano: 0, nivel: 2);   // `usr.MeleeAttack()`
		AvisarPertoG3(alvo, 3 * ZoneCollision.TileSize, $"{pl.Name} aparece e golpeia {alvo.Name}!");
		pl.Niveis.Creditar(PathDoRushG10, r.ExpPorSalto);

		r.Faltam--;
		r.ProximoMs = agora + r.PassoMs;
		if (r.Faltam <= 0) EncerrarRushG10(pl, r, null);
	}

	/// <summary>`currush = 2; sleep(zrcd); currush = 0` -- a exaustao, e o exp que o efetor credita durante ela.</summary>
	private void EncerrarRushG10(ServerPlayer pl, RushG10 r, string? motivo)
	{
		_rushG10.Remove(pl.Id);
		_rushProntoG10[pl.Id] = NowMs() + r.ExaustaoMs;
		pl.Niveis.Creditar(PathDoRushG10, r.ExpNaExaustao);
		if (motivo != null) Avisar(pl, motivo);
		Avisar(pl, $"o rush acaba e a exaustao vem: {r.ExaustaoMs / 1000.0:0.#}s ate o proximo.");
	}

	// =====================================================================
	// 6) A TRINDADE -- `Bodybuilding.dm:191-230`
	// =====================================================================
	/// <summary>
	/// A ABERTURA DOS TRES: `kireq = Ephysoff*BaseDrain`, `if(!med && !train && !KO && Ki >= kireq &&
	/// !basicCD && canfight) { basicCD += 10; ... }`. **Nao ha `Ki -= kireq` em nenhum dos tres**: o Ki e
	/// conferido e nao cobrado. Mantido, e dito na recusa.
	/// </summary>
	private bool AbrirProvocacaoG10(ServerPlayer pl, out double custo)
	{
		custo = pl.Ficha.Ephysoff * pl.Ficha.BaseDrain();
		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return false; }
		if (EmEspera(pl, _prontoG3, "seus golpes especiais ainda se recompoem")) return false;
		long agora = NowMs();
		if (pl.Ficha.Ki < custo)
		{
			Avisar(pl, $"isso pede {custo:0} de energia (o original confere e nao cobra -- mas confere).");
			return false;
		}
		_prontoG3[pl.Id] = agora + 1000;   // `basicCD += 10`
		return true;
	}

	/// <summary>`for(var/mob/M in view(10))` -- todo corpo vivo a ate dez tiles, menos voce.</summary>
	private List<ServerPlayer> EmVoltaG10(ServerPlayer pl, float raioPx)
	{
		var l = new List<ServerPlayer>();
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl && !o.Ficha.dead && o.Combate != null && Vec2.Distance(o.Pos, pl.Pos) <= raioPx)
				l.Add(o);
		return l;
	}

	/// <summary>
	/// `if(M.target)` -- este corpo esta lutando com alguem? Pro jogador e a mira; pro corpo dirigido e a
	/// presa engajada (hostil) ou o rancor de pe (pacifico) -- os tres lugares em que o port guarda "o
	/// meu alvo", cada um lido por quem o escreve (`Marcado`, `PresaDoHostil`, `PresaDoNpc`).
	/// </summary>
	private bool TemAlvoG10(ServerPlayer m)
		=> m.AlvoId != 0 || m.PresaEngajada != 0 || NowMs() < m.RancorAte;

	/// <summary>
	/// `M.target = usr` -- e o corpo dirigido tem que OBEDECER: o cerebro reescreve a mira a cada tique a
	/// partir da presa (`Cerebro.Montar`: `Marcar = p.IdDoAlvo`), entao mirar sozinho duraria um tique.
	/// A presa vem de dois lugares e os dois recebem: o hostil pela <see cref="ServerPlayer.PresaEngajada"/>
	/// (que o `PresaDoHostil` honra antes de procurar), o pacifico pelo rancor (`PresaDoNpc` responde o
	/// `UltimoAgressor` enquanto `RancorAte` vale) -- pelo mesmo <see cref="MarcarAgressao"/> do soco.
	/// </summary>
	private void VirarAlvoDeG10(ServerPlayer m, ServerPlayer pl)
	{
		Mirar(m, pl.Id);
		if (m.Cerebro == null && m.Papel == null) return;
		if (m.Papel is { Pacifico: true }) MarcarAgressao(m, pl);
		else if (EhJogador(pl)) m.PresaEngajada = pl.Id;
	}

	/// <summary>
	/// TAUNT (`:191-202`). `basicCD += 10`; "[usr]: Fuck you!" pra quem ve; `for(M in view(10)) if(M.target &&
	/// M != usr && prob(100 - M.Ewillpower*20)) M.target = usr`.
	/// </summary>
	private void ProvocacaoG10(ServerPlayer pl)
	{
		if (!AbrirProvocacaoG10(pl, out _)) return;
		Falar(pl, Protocol.Fala.Diz, "Fuck you!");

		int pegos = 0;
		foreach (ServerPlayer m in EmVoltaG10(pl, RaioDaProvocacaoG10))
		{
			if (!TemAlvoG10(m)) continue;
			if (_rng.NextDouble() * 100 >= 100 - m.Ficha.Ewillpower * 20) continue;
			VirarAlvoDeG10(m, pl);
			AvisarSePessoa(m, $"{pl.Name} te irrita demais! Seu alvo agora e ele!");
			pegos++;
		}
		Avisar(pl, pegos == 0
			? "ninguem em volta estava lutando com alguem -- ou ninguem caiu."
			: $"{pegos} {(pegos == 1 ? "pessoa passa" : "pessoas passam")} a lutar com VOCE.");
	}

	/// <summary>
	/// SLAP (`:204-216`). `basicCD += 10`; "[usr]: You like this, baby?"; `for(M in view(10)) if(M.target &&
	/// M != usr && prob(100 - M.Ewillpower*25)) { M.stagger += 1; spawn(15) M.stagger -= 1 }` -- 1,5 s.
	/// </summary>
	private void TapaG10(ServerPlayer pl)
	{
		if (!AbrirProvocacaoG10(pl, out _)) return;
		Falar(pl, Protocol.Fala.Diz, "You like this, baby?");

		int pegos = 0;
		foreach (ServerPlayer m in EmVoltaG10(pl, RaioDaProvocacaoG10))
		{
			if (!TemAlvoG10(m)) continue;
			if (_rng.NextDouble() * 100 >= 100 - m.Ficha.Ewillpower * 25) continue;
			Travar(m, 1.5);
			AvisarSePessoa(m, $"{pl.Name} bate na propria bunda e voce fica um segundo e meio pasmo!");
			pegos++;
		}
		Avisar(pl, pegos == 0
			? "ninguem em volta estava lutando com alguem -- ou ninguem se abalou."
			: $"{pegos} {(pegos == 1 ? "pessoa fica" : "pessoas ficam")} um segundo e meio sem reagir.");
	}

	/// <summary>
	/// COUNTER TAUNT (`:218-230`). `basicCD += 10`; "[usr]: Oh yeah? Well fuck you too buddy!"; `for(M in
	/// view(10)) if(M == usr.target && prob(100 - M.Ewillpower*22)) damage_mob(M, attackCalcs(M,0,0,0,2) *
	/// BPModulus * 0.25)` -- dano MENTAL, sem rolagem, num membro.
	///
	/// O `attackCalcs` do DM e o `NormDamageCalc` mais o `Type` somado e uma fila de temperos (estilo,
	/// flanco, arma, freio de velocidade, freio de tecnica) que so o resolvedor de golpe deste port aplica
	/// -- e um dano sem rolagem nao passa por ele. Entra o miolo: `(DanoBase + Type) * BPModulus * 0,25`.
	/// </summary>
	private void ContraProvocacaoG10(ServerPlayer pl)
	{
		if (!AbrirProvocacaoG10(pl, out _)) return;
		Falar(pl, Protocol.Fala.Diz, "Oh yeah? Well fuck you too buddy!");

		ServerPlayer? m = Marcado(pl);
		if (m == null || Vec2.Distance(m.Pos, pl.Pos) > RaioDaProvocacaoG10)
		{
			Avisar(pl, "a resposta se perde: seu alvo marcado nao esta a dez tiles.");
			return;
		}
		if (_rng.NextDouble() * 100 >= 100 - m.Ficha.Ewillpower * 22)
		{
			Avisar(pl, $"{m.Name} nem se abala.");
			return;
		}

		double dmg = (CombatMath.DanoBase(pl.Ficha, m.Ficha) + 2)
					 * CombatMath.BpModulus(pl.Ficha.expressedBP, m.Ficha.expressedBP) * 0.25;
		FerirUmMembroG10(m, pl, dmg, pl.Combate.Letal);
		AvisarSePessoa(m, $"{pl.Name} te causa um dano mental!! ({dmg:0.#})");
		Avisar(pl, $"{m.Name} leva {dmg:0.#} de dano mental, direto no corpo.");
	}

	// =====================================================================
	// O RELOGIO DO LOTE -- os danos atrasados e os saltos do rush
	// =====================================================================

	private void AgendarAtrasoG10(ServerPlayer autor, ServerPlayer alvo, long quandoMs, double dano,
								  bool espalhado, bool letal, string nome)
	{
		_atrasosG10.Add(new AtrasoG10
		{
			Autor = autor.Id, Alvo = alvo.Id, QuandoMs = quandoMs, Dano = dano,
			Espalhado = espalhado, Letal = letal, Nome = nome,
		});
		LigarPulso();
	}

	/// <summary>A agenda deste lote no pulso de 10 Hz: os saltos do rush e os danos atrasados. Chamada tambem pela bancada, que avanca o relogio na mao.</summary>
	private void PulsoG10()
	{
		long agora = NowMs();

		foreach (int id in _rushG10.Keys.ToList())
		{
			RushG10 r = _rushG10[id];
			if (agora < r.ProximoMs) continue;
			if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null)
			{ _rushG10.Remove(id); continue; }
			SaltarG10(pl, r, agora);
		}

		for (int i = _atrasosG10.Count - 1; i >= 0; i--)
		{
			AtrasoG10 a = _atrasosG10[i];
			if (agora < a.QuandoMs) continue;
			_atrasosG10.RemoveAt(i);

			// `while(a > 0 && oldt)`: o autor e o alvo ainda tem que existir -- e na mesma zona.
			if (!_players.TryGetValue(a.Autor, out ServerPlayer? autor)) continue;
			if (CorpoNaMinhaZona(autor, a.Alvo) is not { } alvo) continue;

			if (a.Espalhado) EspalharDanoG3(alvo, autor, a.Dano, a.Letal);
			else FerirUmMembroG10(alvo, autor, a.Dano, a.Letal);
			AvisarSePessoa(alvo, $"o {a.Nome} de {autor.Name} ainda ecoa no seu corpo ({a.Dano:0.#}).");
		}
	}

	// =====================================================================
	// LIMPEZA
	// =====================================================================
	/// <summary>
	/// QUEM SAIU LEVA O ESTADO DELE JUNTO -- id de jogador se REUSA (ver <see cref="EsquecerG7"/>). Sem isto o
	/// proximo a entrar herdaria a exaustao do rush, o `ultiCD` do Hokuto ou um estouro agendado contra
	/// um desconhecido.
	/// </summary>
	private void EsquecerG10(int id)
	{
		_ultiG10.Remove(id);
		_rushG10.Remove(id);
		_rushProntoG10.Remove(id);
		_atrasosG10.RemoveAll(a => a.Autor == id || a.Alvo == id);
	}
}
