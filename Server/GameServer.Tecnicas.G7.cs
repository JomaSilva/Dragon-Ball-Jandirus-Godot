using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G7 -- OS PUNHOS NOMEADOS (e as duas bolas que faltavam).
///
/// ============================ POR QUE ESTAS DEZESSEIS, E NAO AS VINTE E CINCO ============================
/// O censo (`--censoteste`) conta 101 verbos MUDOS. Vinte e cinco deles pareciam fechaveis "so com o
/// que ja existe". Lendo o DM verb por verb, dezesseis sao: entram pelo funil do soco
/// (<see cref="GolpeG3"/> + a agenda de barragem do lote G3) ou pela porta do tiro
/// (<see cref="Disparar"/>), e o que trazem do original e uma RECEITA -- custo, alcance, dano extra,
/// recarga.
///
/// AS OUTRAS NOVE NAO SAO, e a razao esta escrita no proprio DM. Ficam catalogadas aqui, com o nome
/// do sistema que cada uma espera, porque um verb meio-portado e pior que um verb mudo -- o jogador
/// aperta, ve alguma coisa acontecer, e nunca descobre que metade da tecnica nao existe:
///
///     o que falta            quem espera                                        onde esta no DM
///     ---------------------  -------------------------------------------------  ------------------------
///     AGARRAO (`grabbee`)    Suplex, Flip                                       `Wrestling Skills.dm:48`
///                                                                               `Martial Skill Attacks.dm:289`
///     DANO AO LONGO DO       Shock, Acid_Spit                                   `Assassain Skills.dm:2`
///     TEMPO                                                                     `Race Trees/arlian.dm:42`
///     JORRO SUSTENTADO       BusterBarrage, Continuous_Energy_Bullets,          `blasts/BusterBarrage.dm:26`
///     (segurar e despejar)   Spin_Blast, Energy_Wave_Volley                     `blasts.dm:263`, `:333`
///                                                                               `beams.dm:373`
///     CARGA DE BOLA          Death_Ball                                         `blasts/DeathBall.dm:23`
///
/// DUAS DESSAS NOVE ESTAVAM CLASSIFICADAS ERRADO no plano desta camada, e so o DM disse:
///   * `Suplex` foi listado como golpe de rua. Ele abre com `get_me_a_grab()` e o `if` inteiro pende
///     de `usr.grabbee` (`Wrestling Skills.dm:48-64`) -- e irmao do Clench, do Hold e do Power Slam,
///     que ja tinham sido postos de fora pelo mesmo motivo. Ele so falta pelo agarrao.
///   * `Flip` foi listado como golpe. Ele NAO ATACA NINGUEM: o corpo inteiro do verb e o calculo de
///     `escapechance` pra sair de um `grabber` (`Martial Skill Attacks.dm:289-322`). E a tecla de
///     ESCAPAR de um agarrao, e sem agarrao nao ha do que escapar.
///
/// E UMA DAS OITO "PRECISA DE SISTEMA" ESTAVA CLASSIFICADA ERRADO PRO OUTRO LADO: o `Spirit_Gun` foi
/// marcado como "cobra VIDA e nao Ki". Ele cobra FOLEGO (`usr.stamina -= kireq`, `Spirit.dm:352`), e
/// folego ja e moeda corrente neste port -- o Punho da Presa do Lobo ja gasta 8 dele
/// (`GameServer.Tecnicas.G6.cs`). Ele e receita pura, e entrou.
/// ========================================================================================================
///
/// ============================ A FORMA DE TODO VERB DE PUNHO DO DM ============================
/// As catorze folhas de punho deste lote sao a MESMA moldura, com quatro numeros trocados:
///
///     kireq = Ephysoff * BaseDrain * N        // N e a unica coisa que muda de verb pra verb
///     if(!med && !train && !KO && Ki>=kireq && !basicCD && canfight)
///         basicCD += 15                        // (25 no Cutthroat, 30 no Backstab)
///         Ki -= kireq
///         get_me_a_target()
///         if(target in view(2)) step(src, get_dir(src,target))
///         if(target in view(1)) target.stagger += 1; AttackMultiple/doAttack(...); target.stagger -= 1
///
/// Por isso o lote tem UM metodo de moldura (<see cref="SocoNomeadoG7"/>) e nao catorze copias: o
/// que cada verb declara e a tabela dele. Catorze copias seriam catorze lugares pra corrigir no dia
/// em que o `GolpeG3` mudar -- e ele ja mudou uma vez, quando o lote G6 lhe pendurou a escada.
/// ============================================================================================
///
/// ============================ O `stagger` DO ALVO VIROU `Stun` CURTO, E ISSO E UMA ESCOLHA ============================
/// `target.stagger += 1` antes da sequencia e `-= 1` depois e um TRAVA-ANIMACAO do BYOND: o alvo nao
/// age enquanto o combo corre. Aqui isso e <see cref="CombatState.Stun"/> pelo tempo da sequencia --
/// o mesmo canal que o Punho da Presa do Lobo ja usa (`AddEffect(/effect/stun)`). Nao inventei um
/// segundo tipo de trava; se um dia o `stagger` virar estado proprio, ha um lugar so pra mexer.
/// ================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// ESTADO DO LOTE
	// =====================================================================
	/// <summary>
	/// `scatterCD` -- a recarga PROPRIA do Scattering Bullet (`blasts.dm:533`).
	///
	/// Propria, e nao compartilhada com a familia de barragem (`barrageCD`), porque no DM ela e
	/// literalmente outra variavel: quem despejou um Scattering Bullet ainda pode emendar uma
	/// Barragem de Energia. Junta-las seria um balanceamento que o original nao fez.
	/// </summary>
	private readonly Dictionary<int, long> _scatterPronto = [];

	/// <summary>
	/// `view(1)` -- o "colado" do DM, em pixels.
	///
	/// Um tile e meio, e nao um tile: e o mesmo raio que o sopro e a onda de choque do lote G6 ja
	/// usam pro `oview(1)`, porque num mundo de posicao livre o tile vizinho do BYOND cobre ate a
	/// diagonal. Um valor diferente aqui faria "colado" querer dizer duas coisas.
	/// </summary>
	private const float ColadoG7 = ZoneCollision.TileSize * 1.5f;

	/// <summary>`view(2)` -- a distancia de onde o verb ainda AVANCA antes de bater.</summary>
	private const float PassoDeAproximacaoG7 = ZoneCollision.TileSize * 2.5f;

	// =====================================================================
	// REGISTRO
	// =====================================================================
	public static void RegistrarTecnicasG7()
	{
		// ---- boxe (`Physical Skills.dm:66,89,113,120`) ----
		Tecnicas.Registrar("One_Two", "Um-Dois", Modo.Instantanea,
			"O basico do boxe: um jab pra medir e um cruzado por cima, mais forte. Voce da um passo "
			+ "pra frente antes do primeiro, entao pega quem esta a dois tiles.");

		Tecnicas.Registrar("One_Two_Five", "Um-Dois-Cinco", Modo.Instantanea,
			"Jab, cruzado e uppercut, nessa ordem e cada um mais forte que o anterior. Custa metade "
			+ "a mais que o Um-Dois e o ultimo golpe e o que derruba.");

		Tecnicas.Registrar("Two_One_Four", "Dois-Um-Quatro", Modo.Instantanea,
			"Cruzado, jab e uppercut -- a combinacao que comeca forte. Nao avanca: e pra quem JA "
			+ "esta colado e quer despejar tres golpes sem dar um passo.");

		Tecnicas.Registrar("KO_Punch", "Soco de Nocaute", Modo.Instantanea,
			"Um uppercut so, com dano enorme somado. E o golpe mais caro do boxe e o unico que aposta "
			+ "tudo numa unica leitura -- se o outro bloquear, acabou.");

		// ---- chutes (`Physical Skills.dm:155,203,224`) ----
		Tecnicas.Registrar("Dropkick", "Voadora", Modo.Instantanea,
			"Voce se lanca contra o alvo e acerta com os dois pes. Quanto MENOS chao voce precisar "
			+ "correr, mais forte ela chega. Se o alvo nao estiver no fim da corrida, voce cai "
			+ "sozinho e fica atordoado.");

		Tecnicas.Registrar("Falling_Kick", "Chute Descendente", Modo.Instantanea,
			"Um chute pra baixo. Se o alvo estiver VOANDO, voce emenda um segundo golpe que o traz "
			+ "junto pro chao. Se errar, voce e quem se desequilibra.");

		Tecnicas.Registrar("Kickup", "Chute Ascendente", Modo.Instantanea,
			"Um chute de baixo pra cima, com dano extra. Barato, rapido, e o pao com manteiga de "
			+ "quem luta de perna.");

		// ---- artes marciais (`Martial Skill Attacks.dm:241,325,340,355`) ----
		Tecnicas.Registrar("Dash_Attack", "Investida", Modo.Instantanea,
			"Uma corrida longa terminada num golpe pesado -- ela alcanca MUITO mais longe que a "
			+ "Voadora e custa quase metade. O preco e o mesmo: chegar e nao achar ninguem deixa "
			+ "voce atordoado no lugar.");

		Tecnicas.Registrar("Spin_Attack", "Ataque Giratorio", Modo.Instantanea,
			"Voce gira e acerta ate TRES pessoas coladas em voce de uma vez. Nao precisa de alvo "
			+ "marcado e nao anda: e a resposta de quem esta cercado de perto.");

		Tecnicas.Registrar("Stun_Attack", "Golpe Atordoante", Modo.Instantanea,
			"Voce avanca ate dez tiles e acerta um golpe que deixa o alvo dois segundos e meio sem "
			+ "reagir. O dano nao e o ponto -- o silencio depois dele e.");

		Tecnicas.Registrar("Takedown", "Derrubada", Modo.Instantanea,
			"Voce agarra o alvo pelo tronco e o poe no chao. Contra alguem VOANDO ela e devastadora: "
			+ "tira o voo, bate duas vezes e deixa tres segundos e meio de atordoamento.");

		// ---- a investida de Ki (`speedy.dm:178`) ----
		Tecnicas.Registrar("Lariat", "Lariat", Modo.Instantanea,
			"Voce acende o Ki e se lanca contra o alvo marcado, de ate trinta e cinco tiles, "
			+ "terminando com um ombro no peito dele. Custa quase nada -- e quanto mais forte e "
			+ "tecnico voce for, MENOS custa.");

		// ---- assassino (`Assassain Skills.dm:122,141`) ----
		Tecnicas.Registrar("Cutthroat", "Degola", Modo.Instantanea,
			"Um corte curto que vale muito mais quando o alvo AINDA NAO ESTA EM COMBATE. Contra quem "
			+ "ja esta lutando e um golpe comum e caro. Deixa seus golpes especiais em espera por "
			+ "dois segundos e meio.");

		Tecnicas.Registrar("Backstab", "Punhalada", Modo.Instantanea,
			"Vale pelo lugar de onde sai: se voce estiver olhando PRO MESMO LADO que o alvo -- ou "
			+ "seja, nas costas dele --, o golpe crita. Fora de combate ele soma ainda mais. Tres "
			+ "segundos de espera depois.");

		// ---- as duas bolas (`blasts.dm:530`, `Core Trees/Spirit.dm:344`) ----
		Tecnicas.Registrar("Scattering_Bullet", "Bala Dispersa", Modo.Instantanea,
			"Uma nuvem de esferas que nasce ESPALHADA em volta de voce, fica um instante no ar e "
			+ "entao converge toda no alvo marcado, de qualquer angulo. Quantas saem depende da sua "
			+ "pericia de Ki e da sua forca. Precisa de alvo a ate trinta tiles.");

		Tecnicas.Registrar("Spirit_Gun", "Spirit Gun", Modo.Instantanea,
			"Uma bala de espirito disparada do dedo. Ela NAO gasta energia: gasta FOLEGO -- e por "
			+ "isso sai quando o Ki ja acabou. Treinar a arvore do Espirito a deixa mais barata e "
			+ "mais forte ao mesmo tempo.");
	}

	/// <summary>Os dezesseis ids deste lote -- lidos pelo `switch` do despacho geral.</summary>
	private static readonly string[] IdsG7 =
	[
		"One_Two", "One_Two_Five", "Two_One_Four", "KO_Punch",
		"Dropkick", "Falling_Kick", "Kickup",
		"Dash_Attack", "Spin_Attack", "Stun_Attack", "Takedown", "Lariat",
		"Cutthroat", "Backstab",
		"Scattering_Bullet", "Spirit_Gun",
	];

	public static bool EhDoLoteG7(string id) => Array.IndexOf(IdsG7, id) >= 0;

	/// <summary>
	/// O DESPACHO DO LOTE. Chamado de dentro do `switch` do <see cref="UsarTecnica"/>, DEPOIS do
	/// <see cref="SabeTecnica"/> geral -- igual ao G5 e ao G6, e pela mesma razao: quem nao comprou
	/// ouve "voce nao sabe" por uma porta so.
	/// </summary>
	private void UsarTecnicasG7(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case "One_Two": ComboDeBoxeG7(pl, id); break;
			case "One_Two_Five": ComboDeBoxeG7(pl, id); break;
			case "Two_One_Four": ComboDeBoxeG7(pl, id); break;
			case "KO_Punch": SocoDeNocauteG7(pl); break;

			case "Dropkick": VoadoraG7(pl); break;
			case "Falling_Kick": ChuteDescendenteG7(pl); break;
			case "Kickup": ChuteAscendenteG7(pl); break;

			case "Dash_Attack": InvestidaG7(pl); break;
			case "Spin_Attack": GiratorioG7(pl); break;
			case "Stun_Attack": AtordoanteG7(pl); break;
			case "Takedown": DerrubadaG7(pl); break;
			case "Lariat": LariatG7(pl); break;

			case "Cutthroat": DegolaG7(pl); break;
			case "Backstab": PunhaladaG7(pl); break;

			case "Scattering_Bullet": BalaDispersaG7(pl); break;
			case "Spirit_Gun": SpiritGunG7(pl); break;
		}
	}

	// =====================================================================
	// A MOLDURA COMUM
	// =====================================================================
	/// <summary>
	/// A ABERTURA DE TODO VERB DE PUNHO DESTE LOTE, na ordem exata do DM: condicoes, recarga
	/// compartilhada, preco, cobranca.
	///
	/// <paramref name="multKi"/> e o `N` de `kireq = Ephysoff * BaseDrain * N` -- o unico numero que
	/// muda entre catorze verbos. <paramref name="recargaMs"/> e o `basicCD += ...`, que e 15 tiques
	/// em doze deles, 25 no Cutthroat e 30 no Backstab.
	///
	/// A RECARGA E A DO LOTE G3 (`_prontoG3`), e nao uma nova: no DM `basicCD` e uma variavel do MOB,
	/// nao da tecnica. Quem acabou de dar um Um-Dois-Cinco tem o Sword Strike, o Multihit, a Presa do
	/// Lobo e os outros treze deste lote em espera junto -- e esse contador unico E o teto de dano
	/// por segundo do jogo. Uma recarga por tecnica multiplicaria esse teto por catorze.
	/// </summary>
	private bool AbrirPunhoG7(ServerPlayer pl, double multKi, long recargaMs, out double custo)
	{
		custo = pl.Ficha.Ephysoff * pl.Ficha.BaseDrain() * multKi;

		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return false; }

		long agora = NowMs();
		if (_prontoG3.TryGetValue(pl.Id, out long livre) && agora < livre)
		{
			Avisar(pl, $"seus golpes especiais ainda se recompoem (faltam {(livre - agora) / 1000.0:0.0}s).");
			return false;
		}
		if (_barragemG3.ContainsKey(pl.Id)) { Avisar(pl, "voce ja esta no meio de uma sequencia."); return false; }
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede {custo:0} de energia."); return false; }

		pl.Ficha.Ki -= custo;
		_prontoG3[pl.Id] = agora + recargaMs;
		return true;
	}

	/// <summary>
	/// UM GOLPE NOMEADO DE UM SO IMPACTO -- o `doAttack(target, addeddamage, ..., Type)` do DM.
	///
	/// Faz o que a moldura do verb faz DEPOIS de cobrar: acha o alvo, da o passo do
	/// `step(src, get_dir(src,target))` se ele estiver a dois tiles, e bate pelo funil unico.
	/// Devolve o resultado porque tres verbos deste lote decidem alguma coisa com ele (o Chute
	/// Descendente emenda contra quem voa, a Voadora e a Investida se desequilibram no vazio).
	/// </summary>
	private GolpeResultado? GolpeNomeadoG7(ServerPlayer pl, double addDano, int nivel,
										   double stunDoAlvo = 0)
	{
		// `get_me_a_target()` e depois `if(target in view(2))` -- o marcado primeiro, senao o mais
		// proximo. E o mesmo `AlvoDeTecnicaG3` que o Light Buster e o jokenpo ja usam.
		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, PassoDeAproximacaoG7);
		if (alvo == null) { Avisar(pl, "nao ha ninguem por perto pra acertar."); return null; }

		// `step(src, get_dir(src,target))`: UM passo pra cima do alvo antes de bater. O `AvancarG3`
		// respeita parede -- ver o bloco dele --, entao o golpe nao empurra ninguem pro cenario.
		AvancarG3(pl, alvo, ZoneCollision.TileSize);

		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7)
		{
			Avisar(pl, $"{alvo.Name} ficou longe demais e o golpe corta o ar.");
			return null;
		}

		// `target.stagger += 1` ... `-= 1`: a trava do alvo enquanto o golpe corre. Ver o cabecalho.
		if (stunDoAlvo > 0 && alvo.Combate != null)
			alvo.Combate.Stun = Math.Max(alvo.Combate.Stun, stunDoAlvo);

		return GolpeG3(pl, alvo, addDano: addDano, nivel: nivel);
	}

	/// <summary>
	/// O DESEQUILIBRIO DE QUEM ERRA A CORRIDA: `stagger += 1; sleep(4); stagger -= 1`
	/// (`Physical Skills.dm:196`, `Martial Skill Attacks.dm:275`).
	///
	/// Quatro decimos de segundo parado, e a frase e do original: *"You fell because you missed the
	/// enemy. You're stunned!"*. E o unico contrapeso que as duas corridas tem -- sem ele, correr
	/// quinze tiles atras de alguem seria de graca.
	/// </summary>
	private static void TropecarG7(ServerPlayer pl)
	{
		if (pl.Combate != null) pl.Combate.Stun = Math.Max(pl.Combate.Stun, 0.4);
	}

	// =====================================================================
	// 1) OS TRES COMBOS DE BOXE -- `Physical Skills.dm:66`, `:89`, `:120`
	// =====================================================================
	/// <summary>
	/// OS TRES SAO O MESMO VERB com a escada trocada -- ver o bloco da <see cref="BarragemG3.Escada"/>:
	///
	///                   kireq (x Ephysoff x BaseDrain)   avanca?   golpes (dano extra / atraso)
	///     One_Two             8                          sim       jab +0 | 0,2s cross +5
	///     One_Two_Five       12                          sim       jab +0 | 0,1s cross +4 | 0,2s upper +6
	///     Two_One_Four        9                          NAO       cross +2 | 0,2s jab +5 | 0,1s upper +7
	///
	/// O `Two_One_Four` E O UNICO QUE NAO DA O PASSO, e isso e do DM: os outros dois abrem com
	/// `if(target in view(2)) step(...)`, e ele abre direto com `if(target in view(1))`
	/// (`:127`). E a combinacao de quem JA esta na cara do outro, e o dano do primeiro golpe (+2
	/// contra +0) e o pagamento por essa exigencia.
	/// </summary>
	private void ComboDeBoxeG7(ServerPlayer pl, string id)
	{
		(double multKi, bool avanca, (double, long)[] escada) = id switch
		{
			"One_Two" => (8.0, true, new (double, long)[] { (0, 0), (5, 200) }),
			"One_Two_Five" => (12.0, true, new (double, long)[] { (0, 0), (4, 100), (6, 200) }),
			_ => (9.0, false, new (double, long)[] { (2, 0), (5, 200), (7, 100) }),
		};

		if (!AbrirPunhoG7(pl, multKi, BasicCdG3, out double custo)) return;

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, avanca ? PassoDeAproximacaoG7 : ColadoG7);
		if (alvo == null) { Avisar(pl, "nao ha ninguem por perto pra acertar."); return; }
		if (avanca) AvancarG3(pl, alvo, ZoneCollision.TileSize);
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7)
		{
			Avisar(pl, $"{alvo.Name} ficou longe demais e o combo corta o ar.");
			return;
		}

		long total = 0;
		foreach ((double _, long ms) in escada) total += ms;

		// A TRAVA DO ALVO DURA O COMBO INTEIRO (`target.stagger += 1` antes, `-= 1` depois): e o que
		// garante que os tres golpes achem o mesmo corpo no mesmo lugar.
		if (alvo.Combate != null)
			alvo.Combate.Stun = Math.Max(alvo.Combate.Stun, total / 1000.0 + 0.2);

		string nome = id switch
		{
			"One_Two" => "um jab e um cruzado",
			"One_Two_Five" => "jab, cruzado e uppercut",
			_ => "cruzado, jab e uppercut",
		};
		Avisar(pl, $"voce encaixa {nome} em {alvo.Name} (-{custo:0} de energia).");
		MandarEfeito(pl, "barragem", total + 200);

		// O PRIMEIRO GOLPE SAI AGORA e o resto entra na agenda de 0,1 s do lote G3 -- a mesma regra
		// do Multihit e da Presa do Lobo: uma tecnica cujo primeiro golpe e agendado parece travada.
		GolpeG3(pl, alvo, addDano: escada[0].Item1, nivel: 2);
		if (escada.Length <= 1) return;

		_barragemG3[pl.Id] = new BarragemG3
		{
			Alvo = alvo.Id,
			Faltam = escada.Length - 1,
			AddDano = 0,
			Escada = escada,
			EscadaIdx = 1,
			ProximoMs = NowMs() + escada[1].Item2,
		};
		LigarRelogioG3();
	}

	/// <summary>
	/// KO PUNCH (`Physical Skills.dm:113`). `kireq = Ephysoff*BaseDrain*15`,
	/// `doAttack(target, 16, 1, null, "KO Punches", null, 3)`.
	///
	/// UM GOLPE SO, E DEZESSEIS DE DANO SOMADO -- mais que qualquer outro punho do jogo, e mais que
	/// os tres golpes do Um-Dois-Cinco juntos (0+4+6). O `3` do ultimo argumento e o `Type`, que no
	/// funil do port e o `nivel` do golpe: e ele que decide o som e a violencia do anuncio.
	/// </summary>
	private void SocoDeNocauteG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 15, BasicCdG3, out _)) return;
		if (GolpeNomeadoG7(pl, addDano: 16, nivel: 3, stunDoAlvo: 0.4) == null) return;
		Falar(pl, Protocol.Fala.Diz, "KO!");
	}

	// =====================================================================
	// 2) OS TRES CHUTES -- `Physical Skills.dm:155`, `:203`, `:224`
	// =====================================================================
	/// <summary>
	/// DROPKICK (`:155`). `kireq = Ephysoff*BaseDrain*15`, alcance
	/// `dist = round(Espeed + Etechnique + Ephysoff/2)` tiles, `doAttack(target, 20+dmg, ..., 3)`.
	///
	/// ============================ O DANO CAI COM O CHAO PERCORRIDO ============================
	/// `dmg` comeca em 10 e o laco faz `dmg--` A CADA PASSO, ANTES de conferir se o alvo chegou
	/// (`:172-176`). Ou seja: acertar alguem colado vale +29 de dano somado; acertar alguem no fim de
	/// uma corrida de dez tiles vale +20. A tecnica premia quem ja fechou a distancia, e nao quem a
	/// usa como transporte -- e sem isso ela seria estritamente melhor que a Investida, que custa
	/// quase metade.
	/// =========================================================================================
	///
	/// ERRAR CUSTA: o `else` do original poe VOCE atordoado por quatro decimos.
	/// </summary>
	private void VoadoraG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 15, BasicCdG3, out _)) return;

		double tiles = Math.Max(
			DmMath.Round(pl.Ficha.Espeed + pl.Ficha.Etechnique + pl.Ficha.Ephysoff / 2, 1), 1);
		float alcance = (float)tiles * ZoneCollision.TileSize;

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, alcance);
		if (alvo == null)
		{
			Avisar(pl, "voce se lanca e nao ha ninguem no fim da corrida -- e cai sozinho.");
			TropecarG7(pl);
			return;
		}

		float antes = Vec2.Distance(alvo.Pos, pl.Pos);
		AvancarG3(pl, alvo, alcance);
		float depois = Vec2.Distance(alvo.Pos, pl.Pos);

		if (depois > ColadoG7)
		{
			// `if(!canmove) "Your rush fails since you can't move!"` -- e o caso da parede: o
			// `AvancarG3` recusa o passo quando ha cenario no caminho, e ai a corrida falha igual.
			Avisar(pl, "sua corrida falha e voce cai -- ficou atordoado.");
			TropecarG7(pl);
			return;
		}

		// `dmg--` uma vez por PASSO andado. Um passo do DM e um tile.
		int passos = (int)Math.Clamp(Math.Ceiling((antes - depois) / ZoneCollision.TileSize), 0, 10);
		GolpeNomeadoG7ComAlvo(pl, alvo, addDano: 20 + (10 - passos), nivel: 3, stunDoAlvo: 0.4);
		Falar(pl, Protocol.Fala.Diz, "HYAH!");
	}

	/// <summary>
	/// FALLING KICK (`:203`). `kireq = Ephysoff*BaseDrain*12`, `doAttack(target,...,"kicks")` e, SE
	/// o golpe entrou E o alvo estava VOANDO, `AttackMultiple(target, 4, ..., "slams")`.
	///
	/// O SEGUNDO GOLPE SO EXISTE CONTRA QUEM VOA (`if(target.flight)`, `:213`), e e a tecnica
	/// inteira: e o unico punho do jogo que pune especificamente estar no ar. No port isso e
	/// <see cref="ServerPlayer.Voando"/>, e o golpe extra TAMBEM tira o voo -- porque um "slam" que
	/// deixa o sujeito pairando nao e um slam.
	///
	/// ERRAR CUSTA: `else { stagger += 1; spawn(4) stagger -= 1 }` -- quem chuta o vazio se
	/// desequilibra por quatro decimos.
	/// </summary>
	private void ChuteDescendenteG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 12, BasicCdG3, out _)) return;

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, PassoDeAproximacaoG7);
		if (alvo == null) { Avisar(pl, "voce chuta o ar e se desequilibra."); TropecarG7(pl); return; }

		AvancarG3(pl, alvo, ZoneCollision.TileSize);
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7)
		{
			Avisar(pl, "voce chuta o ar e se desequilibra."); TropecarG7(pl); return;
		}

		bool voava = alvo.Voando;
		if (alvo.Combate != null) alvo.Combate.Stun = Math.Max(alvo.Combate.Stun, 0.4);
		GolpeResultado r = GolpeG3(pl, alvo, addDano: 0, nivel: 2);

		if (!r.Encostou) { Avisar(pl, "o chute passa raspando e voce se desequilibra."); TropecarG7(pl); return; }
		if (!voava) return;

		// `AttackMultiple(target, 4, ...)`: o segundo golpe, com +4, so contra quem estava no ar.
		GolpeG3(pl, alvo, addDano: 4, nivel: 3);
		alvo.Voando = false;
		MandarEfeito(alvo, "voo", 0);
		Avisar(alvo, $"{pl.Name} arranca voce do ar e te leva junto pro chao.");
		Avisar(pl, $"voce derruba {alvo.Name} do ar.");
	}

	/// <summary>
	/// KICKUP (`:224`). `kireq = Ephysoff*BaseDrain*12`,
	/// `doAttack(target, 3, 1, null, "kicks upwards", null, 3)`.
	///
	/// O verb mais simples do lote: um golpe, +3 de dano, `Type = 3`. O `sleep(2)` que ele faz
	/// depois de acertar nao tem efeito nenhum no jogo (nada roda no meio dele), e por isso nao veio.
	/// </summary>
	private void ChuteAscendenteG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 12, BasicCdG3, out _)) return;
		GolpeNomeadoG7(pl, addDano: 3, nivel: 3, stunDoAlvo: 0.4);
	}

	// =====================================================================
	// 3) AS ARTES MARCIAIS -- `Martial Skill Attacks.dm:241`, `:325`, `:340`, `:355`
	// =====================================================================
	/// <summary>
	/// DASH ATTACK (`:241`). `kireq = Ephysoff*8*BaseDrain`, alcance
	/// `round(Espeed + Etechnique + Ephysoff) + 5` tiles, `doAttack(target, 20, 1, null, ..., 3)`.
	///
	/// A CORRIDA MAIS LONGA DO JOGO e quase a mais barata: cinco tiles a mais que a Voadora de graca,
	/// com `Ephysoff` inteiro em vez de metade, por 8x em vez de 15x. O contrapeso e o dano fixo --
	/// ela NAO ganha nada por acertar de perto, entao usa-la colado e desperdicio.
	///
	/// ============================ UM DEFEITO DO ORIGINAL, CONSERTADO ============================
	/// O laco do DM varre `for(var/mob/M in oview(1))` e depois ataca... `target`, e nao `M`
	/// (`:268`). Com o alvo marcado longe e um estranho no caminho, a corrida parava no estranho e o
	/// golpe ia pro marcado -- a vinte tiles, atravessando parede. Aqui quem leva e quem esta no fim
	/// da corrida, que e o que a tecnica promete.
	///
	/// O QUE NAO VEIO: `start_flying()`/`start_superflight()` durante a corrida (`:255-261`). No DM
	/// isso existe pra a corrida atravessar agua e buraco; aqui o `AvancarG3` ja consulta o
	/// `MoveRules` do jeito certo pra quem esta no chao, e ligar voo no meio de um golpe mexeria no
	/// dreno de Ki do voo por um efeito que ninguem ve.
	/// ==========================================================================================
	/// </summary>
	private void InvestidaG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 8, BasicCdG3, out _)) return;

		double tiles = Math.Max(
			DmMath.Round(pl.Ficha.Espeed + pl.Ficha.Etechnique + pl.Ficha.Ephysoff, 1) + 5, 1);
		float alcance = (float)tiles * ZoneCollision.TileSize;

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, alcance);
		if (alvo == null)
		{
			Avisar(pl, "voce atravessa o campo e nao acha ninguem -- e cai. (Voce esta atordoado.)");
			TropecarG7(pl);
			return;
		}

		AvancarG3(pl, alvo, alcance);
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7)
		{
			Avisar(pl, "sua investida falha e voce cai -- ficou atordoado.");
			TropecarG7(pl);
			return;
		}

		GolpeNomeadoG7ComAlvo(pl, alvo, addDano: 20, nivel: 3, stunDoAlvo: 0.4);
	}

	/// <summary>
	/// SPIN ATTACK (`:325`). `kireq = Ephysoff*15*BaseDrain`, `for(var/mob/M in view(1))` com
	/// `if(amount > 2) break` -- ou seja, ATE TRES pessoas, e `doAttack(M)` sem dano extra.
	///
	/// O TETO DE TRES E LITERAL e vale a pena manter: sem ele a tecnica escalaria com o tamanho da
	/// multidao e viraria a resposta certa pra qualquer briga de grupo. Com ele, ela e "a resposta
	/// pra estar cercado por tres", que e outra coisa.
	///
	/// NAO PRECISA DE ALVO MARCADO e nao avanca -- e o unico punho do lote com as duas propriedades.
	/// </summary>
	private void GiratorioG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 15, BasicCdG3, out _)) return;

		int pegos = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (pegos >= 3) break;
			if (o == pl || o.Ficha.dead || o.Combate == null || o.Combate.Intocavel) continue;
			if (Vec2.Distance(o.Pos, pl.Pos) > ColadoG7) continue;
			if (!AlcancaPelaAltura(pl, o)) continue;

			GolpeG3(pl, o, addDano: 0, nivel: 2);
			pegos++;
		}

		Avisar(pl, pegos > 0
			? $"voce gira e acerta {pegos} de uma vez."
			: "voce gira no vazio.");
	}

	/// <summary>
	/// STUN ATTACK (`:340`). `kireq = Ephysoff*20*BaseDrain`, `if(target in view(10))
	/// RushAttack(target, 1.2)`, e no acerto `target.stunCount += 25`.
	///
	/// VINTE E CINCO DE `stunCount` sao 2,5 s: o contador cai de um em um por tique do
	/// `movement handler.dm:143`, que roda a 0,1 s. E o atordoamento mais longo que um punho impoe
	/// neste jogo -- quatro vezes o do soco comum (<see cref="CombatMath.DuracaoStun"/>, 0,6 s).
	///
	/// O DANO E O DO SOCO CRU (`MeleeAttack()` sem argumento, `:349`): a tecnica nao soma nada. Quem
	/// paga vinte vezes o `Ephysoff` esta comprando os dois segundos e meio, e nao o dano.
	/// </summary>
	private void AtordoanteG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 20, BasicCdG3, out _)) return;

		float alcance = 10 * ZoneCollision.TileSize;
		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, alcance);
		if (alvo == null) { Avisar(pl, "nao ha ninguem a dez tiles pra alcancar."); return; }

		AvancarG3(pl, alvo, alcance);
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7)
		{
			Avisar(pl, "voce nao chega a tempo e o golpe se perde.");
			return;
		}

		GolpeResultado r = GolpeG3(pl, alvo, addDano: 0, nivel: 2);
		if (!r.Encostou) { Avisar(pl, $"{alvo.Name} escapa do golpe atordoante."); return; }

		if (alvo.Combate != null) alvo.Combate.Stun = Math.Max(alvo.Combate.Stun, 2.5);
		Avisar(alvo, $"o golpe de {pl.Name} te deixa sem reacao.");
		Avisar(pl, $"{alvo.Name} fica sem reacao por dois segundos e meio.");
	}

	/// <summary>
	/// TAKEDOWN (`:355`). `kireq = Ephysoff*20*BaseDrain`, e contra quem esta VOANDO:
	/// `tdmg++` (dano DOBRADO), `stunCount += 35` e `flight = 0`.
	///
	/// ============================ O DOBRO VIROU DOIS GOLPES, E ISSO E DECLARADO ============================
	/// O DM multiplica: `dmg = NormDamageCalc(t_m) * tdmg`, e entrega por `damage_mob` -- dano
	/// DIRETO, sem rolagem de pontaria, esquiva ou guarda. Este port tem um funil so de golpe
	/// (`GolpeG3`), e ele nao aceita multiplicador: aceita `addeddamage`, que e soma.
	///
	/// Havia duas saidas. Abrir um segundo caminho de dano (o `damage_mob`) e o que a casa proibe --
	/// e a segunda resposta pra "quanto doeu", e as duas divergem no primeiro rebalanceamento. Entao
	/// o dobro e entregue como DOIS golpes pelo mesmo funil. O que muda: o segundo golpe pode ser
	/// aparado, e no DM nao podia. O que se ganha: um caminho so, e o `Takedown` continua a coisa
	/// mais brutal que se pode fazer com alguem que esta no ar.
	/// ==================================================================================================
	/// </summary>
	private void DerrubadaG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 20, BasicCdG3, out _)) return;

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, PassoDeAproximacaoG7);
		if (alvo == null) { Avisar(pl, "nao ha ninguem por perto pra derrubar."); return; }
		AvancarG3(pl, alvo, ZoneCollision.TileSize);
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7)
		{
			Avisar(pl, $"{alvo.Name} sai do seu alcance."); return;
		}

		bool voava = alvo.Voando;
		AvisarPertoG3(pl, 8 * ZoneCollision.TileSize, $"{alvo.Name} e derrubado por {pl.Name}!");

		if (voava)
		{
			alvo.Voando = false;
			MandarEfeito(alvo, "voo", 0);
			if (alvo.Combate != null) alvo.Combate.Stun = Math.Max(alvo.Combate.Stun, 3.5);
		}

		GolpeG3(pl, alvo, addDano: 0, nivel: 3);
		if (voava) GolpeG3(pl, alvo, addDano: 0, nivel: 3);   // o `tdmg = 2` -- ver o bloco acima
	}

	/// <summary>
	/// LARIAT (`speedy.dm:178`). `staminaReq = angerBuff*1.5/(Ephysoff+Etechnique)*BaseDrain`, alvo
	/// marcado a menos de 35 tiles, `RushAttack(target)` e `MeleeAttack(15)` no fim.
	///
	/// ============================ O PRECO E INVERSO, E NAO E ENGANO ============================
	/// `Ephysoff + Etechnique` esta no DENOMINADOR: quanto mais forte e mais tecnico voce for, MENOS
	/// a investida custa. E o unico golpe do jogo com essa propriedade, e ela e o motivo de o Lariat
	/// ser a corrida de todo dia -- ele nao e forte, ele e o que da pra usar sempre. O `angerBuff`
	/// no numerador e o contrapeso: brigar enfurecido encarece a corrida.
	///
	/// A VARIAVEL SE CHAMA `staminaReq` E O DESCONTO E `Ki -= staminaReq` (`:183`). O nome mente; o
	/// desconto e que vale. Portei o desconto.
	///
	/// A RECARGA DELE NAO E O `basicCD`: e o `dashtired`, que dura o proprio `sleep(30)` do verb --
	/// tres segundos. Fica no mesmo contador do lote (o basicCD), com o valor do original, porque um
	/// terceiro contador de recarga pra uma tecnica so e como se ganha uma tecnica que escapa do teto
	/// de dano por segundo.
	/// </summary>
	private void LariatG7(ServerPlayer pl)
	{
		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return; }

		long agora = NowMs();
		if (_prontoG3.TryGetValue(pl.Id, out long livre) && agora < livre)
		{
			Avisar(pl, $"voce ainda esta se recompondo (faltam {(livre - agora) / 1000.0:0.0}s).");
			return;
		}

		double custo = pl.Ficha.angerBuff * 1.5
					   / Math.Max(pl.Ficha.Ephysoff + pl.Ficha.Etechnique, 0.01)
					   * pl.Ficha.BaseDrain();
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede {custo:0.#} de energia."); return; }

		// `usr.target && get_dist < 35` -- o Lariat NAO usa `get_me_a_target()`: ele exige alvo
		// MARCADO, e recusa com *"You need a valid target..."*. E a unica corrida do lote assim.
		ServerPlayer? alvo = Marcado(pl);
		if (alvo == null || alvo == pl
			|| Vec2.Distance(alvo.Pos, pl.Pos) > 35 * ZoneCollision.TileSize)
		{
			Avisar(pl, "voce precisa de um alvo marcado a ate trinta e cinco tiles. (Duplo clique.)");
			return;
		}

		pl.Ficha.Ki -= custo;
		_prontoG3[pl.Id] = agora + 3000;   // o `sleep(30)` que segura o `dashtired`
		Avisar(pl, $"voce acende o Ki e se lanca contra {alvo.Name}.");

		AvancarG3(pl, alvo, 35 * ZoneCollision.TileSize);
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7)
		{
			Avisar(pl, "sua investida falha...");   // *"Your rush failed..."*
			return;
		}

		AvisarPertoG3(pl, 3 * ZoneCollision.TileSize, $"{pl.Name} se joga em cima de {alvo.Name}!");
		GolpeG3(pl, alvo, addDano: 15, nivel: 3);   // `MeleeAttack(15)`
	}

	// =====================================================================
	// 4) OS DOIS DO ASSASSINO -- `Assassain Skills.dm:122`, `:141`
	// =====================================================================
	/// <summary>
	/// CUTTHROAT (`:122`). `kireq = Ephysoff*BaseDrain*15`, `basicCD += 25`, e o dano extra depende
	/// de UMA pergunta: `if(!IsInFight) AttackMultiple(target, 1+Etechnique, ...) else
	/// AttackMultiple(target, null, ...)`.
	///
	/// FORA DE COMBATE ELA VALE `1 + Etechnique`; DENTRO, ZERO. Nao e um bonus: e a tecnica. Quinze
	/// vezes o `Ephysoff` por um soco comum e um pessimo negocio -- ela existe pra abrir a briga, nao
	/// pra participar dela. O `IsInFight` do port e a tag de combate
	/// (<see cref="CombatState.EmCombate"/>), que e o mesmo estado com outro nome.
	/// </summary>
	private void DegolaG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 15, 2500, out _)) return;

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, PassoDeAproximacaoG7);
		if (alvo == null) { Avisar(pl, "nao ha ninguem por perto."); return; }

		// A PERGUNTA E SOBRE O ALVO (`target.IsInFight` no `AttackMultiple`... nao: `!IsInFight` sem
		// prefixo, dentro de `mob/keyable/verb`, e o `src` -- ou seja, sobre QUEM ATACA). Portei o
		// que esta escrito: quem nao esta em briga corta fundo.
		bool emBriga = pl.Combate is { EmCombate: > 0 };
		double extra = emBriga ? 0 : 1 + pl.Ficha.Etechnique;

		AvancarG3(pl, alvo, ZoneCollision.TileSize);
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7) { Avisar(pl, "o corte passa longe."); return; }

		if (alvo.Combate != null) alvo.Combate.Stun = Math.Max(alvo.Combate.Stun, 0.4);
		GolpeG3(pl, alvo, addDano: extra, nivel: emBriga ? 2 : 3);
		Avisar(pl, emBriga
			? "voce ja esta na briga: o corte sai comum."
			: $"voce corta fundo, de surpresa (+{extra:0.#}).");
	}

	/// <summary>
	/// BACKSTAB (`:141`). `kireq = Ephysoff*BaseDrain*15`, `basicCD += 30`, e DUAS somas de
	/// `Etechnique/2` que se acumulam:
	///
	///     !IsInFight        -> dmg += Etechnique/2
	///     dir == target.dir -> dmg += Etechnique/2   E   crit = 1
	///
	/// `dir == target.dir` E "ESTAR ATRAS", e vale ler duas vezes: nao e "eu olho pra ele", e "nos
	/// dois olhamos pro mesmo lado" -- que num jogo de oito direcoes so acontece quando voce esta nas
	/// costas dele. O port tem <see cref="ServerPlayer.Facing"/> nos dois lados, entao a pergunta e
	/// literalmente a mesma.
	///
	/// O `crit = 1` do DM forca o critico do golpe. O funil do port sorteia o critico dentro do
	/// `MeleeResolver`, e nao aceita "critico forcado" de fora -- entao ele entra como o `nivel 3` do
	/// anuncio mais a segunda meia-tecnica de dano, que e o que o critico do DM valia. Fica anotado:
	/// e a unica parte deste lote que nao chega inteira.
	/// </summary>
	private void PunhaladaG7(ServerPlayer pl)
	{
		if (!AbrirPunhoG7(pl, 15, 3000, out _)) return;

		ServerPlayer? alvo = AlvoDeTecnicaG3(pl, PassoDeAproximacaoG7);
		if (alvo == null) { Avisar(pl, "nao ha ninguem por perto."); return; }

		AvancarG3(pl, alvo, ZoneCollision.TileSize);
		if (Vec2.Distance(alvo.Pos, pl.Pos) > ColadoG7) { Avisar(pl, "a punhalada passa longe."); return; }

		double dmg = 0;
		if (pl.Combate is not { EmCombate: > 0 }) dmg += pl.Ficha.Etechnique / 2;

		bool pelasCostas = pl.Facing == alvo.Facing;
		if (pelasCostas) dmg += pl.Ficha.Etechnique / 2;

		if (alvo.Combate != null) alvo.Combate.Stun = Math.Max(alvo.Combate.Stun, 0.4);
		GolpeG3(pl, alvo, addDano: dmg, nivel: pelasCostas ? 3 : 2);
		Avisar(pl, pelasCostas
			? $"voce acerta {alvo.Name} pelas costas (+{dmg:0.#})."
			: $"voce apunhala {alvo.Name} de frente -- vale menos (+{dmg:0.#}).");
	}

	// =====================================================================
	// 5) AS DUAS BOLAS
	// =====================================================================
	/// <summary>
	/// SCATTERING BULLET (`blasts.dm:530`).
	///
	///     kireq   = 60 * BaseDrain
	///     recarga = max(20 * Eactspeed, 70) tiques
	///     alvo    = marcado, a ate 30 tiles
	///     balls   = round(2*(Ekiskill + Ephysoff/3) + 2, 1) + bonusShots     (piso 1)
	///     espalha = rand(1, round(balls/2,1)) tiles numa das 8 direcoes
	///     bola    = basedamage 1, mods Ekioff*Ekiskill, `spawn(600) del` = 60 s de vida
	///
	/// ============================ ELA E O CERCO AO CONTRARIO ============================
	/// A Hellzone Grenade nasce em volta do ALVO e fecha nele; esta nasce em volta de VOCE, se abre
	/// no ar por dois segundos e SO ENTAO converge (`sleep(round(20/Ekiskill,0.1))` e depois
	/// `walk_towards(C, C.target)`, `:598-601`). O port ja tem as duas pecas: <c>deOnde</c> poe a bola
	/// onde ela nasce e <see cref="Projetil.EsperaDeCaca"/> a segura antes de cacar. Nenhuma linha de
	/// encanamento nova.
	///
	/// O QUE SE PERDEU: no DM as bolas ANDAM pra fora durante o espalhamento e so ficam densas depois
	/// (`density=1` so aparece no `:600`). Aqui elas ja nascem no ponto final do espalhamento e ficam
	/// paradas ate a hora de cacar. Visto de fora e a mesma nuvem convergindo; o que nao acontece e
	/// alguem levar uma bolada durante a abertura -- e no original ela tambem nao acontecia, porque
	/// nesse trecho elas nao eram densas.
	/// ===================================================================================
	///
	/// UM DEFEITO DO ORIGINAL, CONSERTADO: `var/kireq = 60*BaseDrain` e logo abaixo
	/// `usr.Ki -= kireq*BaseDrain` (`:542`) -- `BaseDrain` AO QUADRADO. Quem tem Ki maximo alto
	/// pagava dezenas de vezes o que o `if` conferiu. E a quarta vez que este port acha essa mesma
	/// familia de defeito (Guided Ball, Kill Driver, cura); como nas outras tres, cobra-se o que se
	/// confere.
	/// </summary>
	private void BalaDispersaG7(ServerPlayer pl)
	{
		if (EmEsperaG5(pl, _scatterPronto, "as suas maos ainda estao formigando")) return;

		double custo = 60 * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		// `if(target && get_dist(src,target)<=30) M = target else "You need a valid target..."` --
		// e recusa ANTES de cobrar, como no DM (o `return` do `:539` vem antes do `Ki -=`).
		ServerPlayer? alvo = Marcado(pl);
		if (alvo == null || alvo == pl || alvo.Ficha.dead || !alvo.Zone.Equals(pl.Zone)
			|| Vec2.Distance(alvo.Pos, pl.Pos) > 30 * ZoneCollision.TileSize)
		{
			Avisar(pl, "isso precisa de um alvo marcado a ate trinta tiles. (Duplo clique.)");
			return;
		}

		int quantas = (int)Math.Max(
			DmMath.Round(2 * (pl.Ficha.Ekiskill + pl.Ficha.Ephysoff / 3) + 2, 1)
			+ pl.Ficha.bonusShots, 1);

		// O TETO DA ZONA ANTES DA COBRANCA, como nas barragens do lote G5: um teto que cobra pelo que
		// nao entrega e pior que teto nenhum.
		if (ProjeteisDaZona(pl.Zone.Hash).Count + quantas > MaxProjeteisPorZona)
		{
			Avisar(pl, "o ar aqui ja esta saturado de energia -- nao cabe a nuvem inteira.");
			return;
		}

		double tiques = Math.Max(20 * pl.Ficha.Eactspeed, 70);
		_scatterPronto[pl.Id] = NowMs() + (long)(tiques * MsPorTique);
		pl.Ficha.Ki -= custo;
		for (int i = 0; i < 7; i++) pl.Ficha.BlastGain(_rng);   // sete `Blast_Gain()` literais
		pl.Ficha.blastskill += 0.05 * quantas;
		pl.Ficha.targetedskill += 0.05 * quantas;

		// `sleep(round(20/Ekiskill,0.1))` -- em tiques de 0,1 s. Com pericia zerada sao dois segundos
		// de nuvem aberta; um mestre de Ki fecha o cerco quase na hora.
		double espera = Math.Max(DmMath.Round(20 / Math.Max(pl.Ficha.Ekiskill, 0.1), 0.1), 1) / 10.0;

		// `maxdistance = round(balls/2,1)` -- quanto MAIS bolas, mais larga a nuvem.
		double raioTiles = Math.Max(DmMath.Round(quantas / 2.0, 1), 1);

		var receita = new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Guided,
			BaseDano = 1,                 // `A.basedamage = 1` -- fixo, como o Kienzan
			Velocidade = 1,
			AlcanceTiles = 200,           // quem apaga estas bolas e o prazo, nao o alcance
			Nome = "Bala Dispersa",
		};

		int saiu = 0;
		for (int i = 0; i < quantas; i++)
		{
			Vec2 berco = pl.Pos + DirecaoSorteadaG5()
						 * (float)(_rng.Next(1, (int)raioTiles + 1) * ZoneCollision.TileSize);

			Projetil p = Disparar(pl, receita, rumoDado: Vec2.Zero, deOnde: berco);
			if (!p.Vivo) continue;

			p.Alvo = alvo.Id;
			p.EsperaDeCaca = espera;
			p.VidaRestante = 60;          // `spawn(600) if(A) del(A)`
			saiu++;
		}

		Falar(pl, Protocol.Fala.Diz, "Scattering Bullet!");
		Avisar(pl, $"{saiu} esferas se abrem em volta de voce e miram {alvo.Name}.");
		Avisar(alvo, $"{saiu} esferas de {pl.Name} se acendem em volta dele -- e todas olham pra voce.");
	}

	/// <summary>
	/// SPIRIT GUN (`Core Trees/Spirit.dm:344`).
	///
	///     kireq   = angerBuff * Ephysoff * SpiritBallCost * 10     -- e sai da STAMINA
	///     recarga = basicCD += max(Eactspeed/6, 0.1) + 45 tiques
	///     bola    = basedamage 1*SpiritBallDamage, BP = expressedBP*SpiritBallDamage,
	///               mods = Ekioff*Ekiskill*Ephysoff, `Burnout()` = 5 s
	///
	/// ============================ ELA NAO GASTA KI, E ISSO E O QUE ELA E ============================
	/// `usr.stamina -= kireq` (`:352`). E o unico tiro do jogo inteiro pago em FOLEGO -- ou seja, o
	/// unico que sai depois de o Ki ter acabado. A arvore do Espirito e a arvore de quem luta com o
	/// corpo e nao quer depender do tanque de energia, e este verb e o argumento dela.
	///
	/// Folego ja e moeda deste port: o Punho da Presa do Lobo cobra 8 dele (lote G6) e a Taunt do
	/// lote G2 cobra a conta inteira dela em folego. Nenhum sistema novo.
	/// =============================================================================================
	///
	/// ============================ O QUE NAO CHEGA INTEIRO, E ESTA DECLARADO ============================
	/// O `mods` do DM tem um `* Ephysoff` a mais que o `mods` de qualquer outra bola do jogo -- e o
	/// que faz a bala de espirito crescer com a FORCA e nao so com o Ki. O port funde `mods` e
	/// `basedamage` num campo so (a divida declarada no cabecalho do lote G5), e o `ModsDoTiro` de
	/// bola devolve `Ekioff*Ekiskill` sem o `Ephysoff`. Escrever o `Ephysoff` no `BaseDano` faria
	/// ele entrar DUAS vezes na cadeia (uma como `mods`, outra como `basedamage`), inflando este tiro
	/// contra os outros dezoito ja em producao.
	///
	/// Entao ele fica de fora, e a ordem relativa que o DM escreveu se mantem. Quando a fusao for
	/// desfeita -- e ela e a divida numero um desta familia --, esta linha ganha o `Ephysoff` de
	/// volta em um lugar so.
	/// ==============================================================================================
	/// </summary>
	private void SpiritGunG7(ServerPlayer pl)
	{
		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return; }

		long agora = NowMs();
		if (_prontoG3.TryGetValue(pl.Id, out long livre) && agora < livre)
		{
			Avisar(pl, $"seus golpes especiais ainda se recompoem (faltam {(livre - agora) / 1000.0:0.0}s).");
			return;
		}

		double custo = pl.Ficha.angerBuff * pl.Ficha.Ephysoff * pl.Ficha.SpiritBallCost * 10;
		if (pl.Ficha.stamina < custo)
		{
			Avisar(pl, $"isso pede {custo:0.#} de folego (voce tem {pl.Ficha.stamina:0.#}).");
			return;
		}

		// O TETO DE TIROS: o `PodeAtirar` confere Ki, e esta tecnica nao gasta Ki. Conferir com custo
		// ZERO usa o mesmo funil (morto, KO, meditando, raio na mao, teto de projeteis) sem inventar
		// uma segunda lista de condicoes -- que e como se ganha duas regras pra "posso atirar?".
		if (!PodeAtirar(pl, 0, out string naoPode)) { Avisar(pl, naoPode); return; }

		pl.Ficha.stamina -= custo;

		// `reload = max(Eactspeed/6, 0.1)` e `basicCD += reload + 45` -- quatro segundos e meio de
		// espera, a mais longa deste lote inteiro. E o que paga o tiro que nao custa Ki.
		double tiques = Math.Max(pl.Ficha.Eactspeed / 6, 0.1) + 45;
		_prontoG3[pl.Id] = agora + (long)(tiques * MsPorTique);

		// `usr.Attack_Gain()` -- ela treina PUNHO, e nao tiro, e isso e coerente: o `mods` dela sai do
		// `Ephysoff`. O `SpiritBallFireCount++` do DM NAO veio: ele so alimenta o `exp+=1` do efetor
		// (`niveis.json`, `"cond": "savant.SpiritBallFireCount"`), e o port ainda nao le condicao de
		// efetor. Um campo escrito e nunca lido e exatamente o codigo morto que a casa proibe -- fica
		// catalogado, e volta junto com o consumidor.
		pl.Ficha.AttackGain(_rng);

		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 1 * pl.Ficha.SpiritBallDamage,
			Velocidade = 1,
			AlcanceTiles = AlcanceDeBolaG5,
			MultDeOnda = pl.Ficha.SpiritBallDamage,   // `passbp = expressedBP * SpiritBallDamage`
			Nome = "Spirit Gun",
		});

		Falar(pl, Protocol.Fala.Diz, "SPIRIT GUN!");
	}

	// =====================================================================
	// FERRAMENTAS E LIMPEZA
	// =====================================================================
	/// <summary>
	/// O golpe nomeado quando o alvo JA foi escolhido e a aproximacao JA aconteceu -- os tres verbos
	/// de corrida (Voadora, Investida, Derrubada) precisam medir a distancia percorrida antes de
	/// bater, e por isso nao cabem no <see cref="GolpeNomeadoG7"/>.
	/// </summary>
	private void GolpeNomeadoG7ComAlvo(ServerPlayer pl, ServerPlayer alvo, double addDano,
									   int nivel, double stunDoAlvo)
	{
		if (stunDoAlvo > 0 && alvo.Combate != null)
			alvo.Combate.Stun = Math.Max(alvo.Combate.Stun, stunDoAlvo);
		GolpeG3(pl, alvo, addDano: addDano, nivel: nivel);
	}

	/// <summary>
	/// QUEM SAIU LEVA O ESTADO DELE JUNTO -- o mesmo <see cref="EsquecerG6"/> deste lote, e pelo
	/// mesmo motivo: **id de jogador se REUSA**. Sem esta linha o proximo a entrar herdaria a recarga
	/// da Bala Dispersa de um desconhecido.
	/// </summary>
	private void EsquecerG7(int id) => _scatterPronto.Remove(id);
}
