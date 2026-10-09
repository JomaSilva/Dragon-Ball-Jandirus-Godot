using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// UM ABSORVIDO -- o `datum/MajinAbsorbed` (`Magic/MajinSaga.dm:35-41`), campo a campo.
///
/// NAO VAI PRO DISCO, e isso e do original: la a lista ate e salva, mas o `DoLogoutStuff` a esvazia antes do
/// save (`Login/Login.dm:198-202`, *"a volatile pocket-dimension state that can't survive a relog"*).
/// </summary>
public sealed class AbsorvidoPeloMajin
{
	/// <summary>Numero de ordem, so pra o botao de expelir saber de quem fala. Nao e id de corpo.</summary>
	public int Numero;

	/// <summary>O nome de quem foi absorvido, pro menu de expelir e pras frases.</summary>
	public string Nome = "";

	/// <summary>`who`: o corpo do prisioneiro. Zero = DEVORADO (um NPC): *"nao ha o que devolver"* (`:136`).</summary>
	public int Quem;

	/// <summary>`bp_bonus`: os 10% do BP dele que o Majin esta usando.</summary>
	public double BonusDeBp;

	/// <summary>`added_verbs`: os verbs que o Majin NAO tinha e passou a ter por causa deste (`:160-164`).</summary>
	public readonly List<string> Verbos = [];

	/// <summary>`guardian`: a imagem do Majin que guarda este prisioneiro. Zero = nenhuma (o devorado nao tem).</summary>
	public int Guardiao;
}

/// <summary>
/// ============================ A ABSORCAO DO MAJIN -- O BOLSAO, O GUARDIAO E AS SAIDAS ============================
/// Dono, 2026-10-09: *"cheque pra mim se a absorçao do majin ta funcionando e se o jogador absorvido vai pro
/// interior do corpo do majin enfrentar o clone do corpo original pra escapar (assim como era no DM)"*.
///
/// NAO ESTAVA: o port so tinha a do bio-androide (`GameServer.Absorcao.cs`, que CONSOME a vitima), e o
/// cabecalho de la ja dizia que a do Majin era outra -- *"o Majin manda a vitima pra uma dimensao interna e
/// pode cospi-la de volta"*. Este arquivo e ela: porte de `Magic/MajinSaga.dm:120-393` e dos verbs do
/// `obj/Buu_Absorb` (`Magic/Absorption.dm:76-107` e `:172-191`).
///
/// ============================ O QUE ACONTECE, NA ORDEM DO ORIGINAL ============================
///   * o alvo tem que estar NOCAUTEADO e vivo, ao lado (`:131`; `oview(1)` no verb);
///   * **NPC e DEVORADO** (`:132-151`): morre, e rende 10% do BP ate o Majin cair. Sem bolsao, sem guardiao;
///   * **JOGADOR e SELADO** (`:152-225`): vai VIVO pro interior do Majin (`Core.World.InteriorDoMajin`),
///     chega acordado, destravado e inteiro, e encontra uma IMAGEM do Majin com metade do poder dele.
///     Enquanto estiver la dentro o Majin usa 10% do BP dele, os verbs dele e veste a roupa dele;
///   * quem assistiu e gostava do absorvido reage como se o tivesse visto MORRER (`:169-175`).
///
/// ============================ AS CINCO SAIDAS ============================
///   1. VENCER A IMAGEM -- nocauteada, morta ou com a vida em 15 (`guard_watch`, `:329-342`): sai de pe;
///   2. MORRER LA DENTRO (`Death/Death.dm:9`): sai, e a morte segue o caminho dela do lado de fora;
///   3. O MAJIN CAIR (`CombatMechanics/KO.dm:13-14`): todos saem, e saem NOCAUTEADOS (`:272`);
///   4. O MAJIN EXPELIR (`Absorption.dm:176-191`): todos, ou um, de pe;
///   5. alguem DESLOGAR (`Login.dm:198-202`): o prisioneiro que sai e solto antes do save; o Majin que sai
///      cospe todo mundo.
/// Nas cinco o Majin perde o que ganhou DAQUELE absorvido (`majin_release`, `:252-278`).
///
/// ============================ NAO HA CAMPO DE "ESTOU ABSORVIDO" ============================
/// O `absorbed_into` do DM (`:31`) e o lugar: quem esta num `InteriorDoMajin` esta absorvido, e o dono sai
/// da chave da zona. O que este arquivo guarda e so a lista de registros por Majin -- o `majin_absorbed`.
/// E toda borda e DERIVADA dela a cada tique (<see cref="TickDoMajin"/>): o Majin caiu? o prisioneiro
/// morreu? saiu por outra porta? a imagem foi vencida? Nenhuma delas e um aviso que alguem precise lembrar
/// de mandar -- ha dezenas de caminhos que derrubam, matam ou teleportam um corpo neste port.
///
/// ============================ DIVERGENCIAS DECLARADAS ============================
///   * A SAGA DO CORRUPTED MAJIN NAO FOI PORTADA (as formas 1 a 4, a outra metade que se devora, a Forma
///     Pura: `:124-130`, `:146-148`, `:227-234`, `:395-577`). Os tres ganchos dela dentro da absorcao ficam
///     de fora; a absorcao em si nao depende deles.
///   * O RAMO DE QUEM NAO E MAJIN (`Absorption.dm:88-101`, o `AbsorbDatum` antigo) nao foi portado: fora do
///     corpo de um Majin o verb recusa.
///   * `M.absorbable` (`Absorption.dm:81`) nao existe no port -- o `absorbproc` que o desliga so e chamado
///     pelo sistema antigo. O `Planet != "Sealed"` e o `Selo.Preso`.
///   * O GUARDIAO SABE VOAR. No DM voar nao tira ninguem do alcance de ninguem (nao ha altura, so um bit);
///     aqui quem paira bate em quem esta no chao sem levar de volta (`Voo.PodeAcertar`). A imagem recebe a
///     maestria de Ki do Majin com piso no degrau que destrava o voo -- e com ela a rajada basica, que e o
///     `isBlaster = 1` de `:302`. Tecnica nenhuma alem disso, e forma nenhuma.
///   * O QUE O MAJIN GANHA EM VERBS e o que o `TecnicasDe` do absorvido lista (skills e degraus). As
///     tecnicas INVENTADAS e as FORMAS nao entram: nao sao verbs do livro neste port.
///   * FUGIR DE DENTRO POR TELEPORTE e recusado (`PodeSaltarDePlaneta`). No DM a pergunta nem foi feita.
///   * QUEM E SOLTO APARECE UM PASSO A FRENTE DO MAJIN, e nao no mesmo tile (`:239`): aqui dois corpos nao
///     ocupam o mesmo lugar. E se ali nao cabe um corpo a pe (parede, beirada do mapa, AGUA -- o Majin
///     sobrevoando o mar), no chao livre mais proximo: a rede de todo pouso deste port.
/// =================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// QUEM ESTA DENTRO DE QUEM -- o `majin_absorbed` (`MajinSaga.dm:29`), por id do Majin. A entrada some
	/// quando a lista esvazia: "tem alguem dentro" e a presenca da chave.
	/// </summary>
	private readonly Dictionary<int, List<AbsorvidoPeloMajin>> _dentroDoMajin = [];

	private int _proximoAbsorvido;

	/// <summary>O relogio do `guard_watch()`: a cada <see cref="AbsorcaoMajin.SegundosEntreOlhadas"/>.</summary>
	private double _relogioDosGuardioes;

	/// <summary>Este corpo sela em vez de consumir? `Race=="Majin"||Parent_Race=="Majin"` (`Absorption.dm:83`).</summary>
	private static bool CorpoDeMajin(ServerPlayer pl) => AbsorcaoMajin.EhMajin(pl.Race, pl.Ficha.ParentRace);

	/// <summary>Este corpo esta dentro de um Majin agora? A pergunta e do LUGAR (ver o cabecalho).</summary>
	private static bool DentroDeUmMajin(ServerPlayer pl) => InteriorDoMajin.EhOInterior(pl.Zone);

	/// <summary>Os registros deste Majin (vazio = ninguem). So leitura: quem escreve sao as portas daqui.</summary>
	internal IReadOnlyList<AbsorvidoPeloMajin> AbsorvidosPor(int majinId) =>
		_dentroDoMajin.TryGetValue(majinId, out List<AbsorvidoPeloMajin>? l) ? l : [];

	// =====================================================================
	// O VERB -- `obj/Buu_Absorb/verb/Absorb` (`Absorption.dm:77-87`)
	// =====================================================================
	/// <summary>
	/// ABSORVER. Chega pelo mesmo `absorver` do bio-androide (`UsarHabilidade`): no DM tambem e um verb so,
	/// que se bifurca pela raca la dentro (`Absorption.dm:83`).
	/// </summary>
	private void AbsorverMajin(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		long agora = NowMs();

		bool emRecarga = _prontoAbsorver.TryGetValue(pl.Id, out long pronto) && agora < pronto;
		if (AbsorcaoMajin.PorQueNaoAbsorve(SabeTecnica(pl, AbsorcaoMajin.Verbo), f.KO, f.dead, pl.Selo.Preso, emRecarga)
			is { Length: > 0 } recusa)
		{ Avisar(pl, recusa); return; }

		// NADA DISTO ACONTECE DENTRO DE UMA MENTE: la ninguem esta de corpo presente (o corpo ficou no mapa,
		// largado), e selar uma consciencia arrancaria o dono do proprio corpo por um caminho que o
		// `VoltarProCorpo` nao conhece.
		if (NaMente(pl)) { Avisar(pl, "aqui dentro nada e real: nao ha quem absorver."); return; }

		// `usr.absorbing = 1` ... `sleep(20)` ... `usr.absorbing = 0` (`:82-86`): os dois segundos valem
		// tenha a absorcao dado certo ou nao. E `usr.AbsorbDeterminesBP = 1` (`:80`), que o verb liga antes
		// de olhar pra raca -- no Majin ele nao muda conta nenhuma enquanto o `AbsorbBP` for zero
		// (`base.dm:110`: `if(AbsorbDeterminesBP&&AbsorbBP)`), e a absorcao dele nao escreve la.
		_prontoAbsorver[pl.Id] = agora + AbsorcaoMajin.RecargaMs;
		f.AbsorbDeterminesBP = true;

		ServerPlayer? alvo = AlvoDeTecnica(pl, RaioDaAbsorcao, PodeSerAbsorvidoPeloMajin);
		if (alvo == null)
		{ Avisar(pl, "o alvo precisa estar NOCAUTEADO, vivo, e ao seu lado."); return; }

		if (EhPessoa(alvo)) SelarNoMajin(pl, alvo);
		else DevorarPeloMajin(pl, alvo);
	}

	/// <summary>
	/// `if(!(M.KO) || M.dead)` (`MajinSaga.dm:131`) -- nocauteado e vivo. O CADAVER e o CORPO LARGADO ficam
	/// de fora: o primeiro ja morreu; o segundo carrega a ficha de alguem que esta noutro lugar (na mente, no
	/// leme), e absorve-lo seria absorver essa pessoa por uma porta lateral.
	/// </summary>
	private static bool PodeSerAbsorvidoPeloMajin(ServerPlayer o) =>
		o.Ficha.KO && !o.Ficha.dead && !o.ECadaver && o.DonoDoCorpoLargado == 0 && o.Combate != null;

	// =====================================================================
	// O NPC -- DEVORADO POR INTEIRO (`MajinSaga.dm:132-151`)
	// =====================================================================
	/// <summary>
	/// *"NPCs agora PODEM ser absorvidos: sao devorados por inteiro (sem bolsao/guardiao) e rendem os mesmos
	/// 10% de BP ate o Majin ser nocauteado"* (`:132`).
	/// </summary>
	private void DevorarPeloMajin(ServerPlayer pl, ServerPlayer npc)
	{
		var rec = new AbsorvidoPeloMajin
		{
			Numero = ++_proximoAbsorvido,
			Nome = NomeVisivel(npc),
			Quem = 0,   // `nrec.who = null`: devorado, nao ha o que devolver
			BonusDeBp = AbsorcaoMajin.BonusDeBp(npc.Ficha.BP),
		};
		pl.Ficha.majin_absorb_bp += rec.BonusDeBp;
		RegistrosDoMajin(pl.Id).Add(rec);

		CurarOMajinQueAbsorveu(pl);
		Falar(pl, Protocol.Fala.Emote, $"devora {rec.Nome} por inteiro!");

		// ANTES DE O NPC SAIR DO MUNDO, como la (`:144`): a roupa e copiada do corpo que ainda existe.
		VestirAUltimaRefeicao(pl, npc);

		// `spawn(2) if(eaten) eaten.mobDeath()` (`:150`), com o comentario do autor: *"cidadao devorado
		// tambem conta contra a sua reputacao com o planeta dele"*. Pela porta da morte e pelo funil da
		// derrota, como a absorcao do bio: e dali que saem a reputacao, o rancor e a retirada do corpo.
		// DEVORAR E AGREDIR (o agarrao ja e): quem cobra a reputacao le o ultimo agressor.
		MarcarAgressao(npc, pl);
		if (npc.Combate.Morrer(ignorarSeguro: true)) AoPerderALuta(npc, pl, morreu: true);

		RecalcularOMajin(pl);
		Avisar(pl, $"voce devora {rec.Nome} (+{rec.BonusDeBp:N0} de poder, enquanto voce nao cair).");
		GD.Print($"[server] {pl.Name} (Majin) DEVOROU {rec.Nome}: +{rec.BonusDeBp:0} de BP emprestado");
	}

	// =====================================================================
	// O JOGADOR -- SELADO NO INTERIOR (`MajinSaga.dm:152-225`)
	// =====================================================================
	private void SelarNoMajin(ServerPlayer pl, ServerPlayer m)
	{
		// QUEM E ABSORVIDO SEGURANDO GENTE SOLTA ESSA GENTE ANTES. No DM isso ja aconteceu: o `KO()` dele
		// chamou o `majin_escape_all()` (`KO.dm:13-14`). Aqui a borda e do tique, e a absorcao pode chegar
		// no mesmo tique do nocaute.
		CuspirTodosDoMajin(m.Id, "");

		var rec = new AbsorvidoPeloMajin
		{
			Numero = ++_proximoAbsorvido,
			Nome = NomeVisivel(m),
			Quem = m.Id,
			BonusDeBp = AbsorcaoMajin.BonusDeBp(m.Ficha.BP),
		};
		pl.Ficha.majin_absorb_bp += rec.BonusDeBp;

		// OS VERBS -- `for(var/V in M.Keyableverbs) if(!(V in verbs))` (`:160-164`): so os que o Majin NAO
		// tinha entram, e sao esses (e so esses) que saem quando este absorvido sair.
		List<string> meus = TecnicasDe(pl);
		foreach (string v in TecnicasDe(m))
		{
			if (meus.Contains(v, StringComparer.OrdinalIgnoreCase)) continue;
			pl.VerbosAbsorvidos ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (pl.VerbosAbsorvidos.Add(v)) rec.Verbos.Add(v);
		}

		VestirAUltimaRefeicao(pl, m);
		RegistrosDoMajin(pl.Id).Add(rec);

		// QUEM VIU -- ANTES da viagem, pelo motivo que o original escreve (`:169`): *"done BEFORE the move,
		// while M is still in the world so view(M) catches the onlookers"*.
		LutoPelaAbsorcao(m, pl);

		// PRO BOLSAO, VIVO, num ponto sorteado (`:177-182`).
		ZoneKey interior = InteriorDoMajin.De(pl.Id);
		(int X, int Y) cel = InteriorDoMajin.CelDoPrisioneiro(_rng);
		DestravarOPrisioneiro(m);
		MoveToZone(m.Id, interior, InteriorDoMajin.PixelDe(cel));

		// E CHEGA INTEIRO pro duelo (`:211-216`, *"era absorvido a 5% de HP -- luta impossivel"*): todo
		// membro que ainda existe vai ao maximo (`if(!BL.lopped) BL.health = BL.maxhealth`), Ki e folego
		// cheios. O que foi decepado nao volta por isto.
		foreach (BodyPart p in m.Combate.Corpo.Partes)
			if (!p.Decepado) p.Vida = p.VidaMax;
		m.Combate.SincronizarVida();
		m.Ficha.Ki = Math.Max(m.Ficha.Ki, m.Ficha.MaxKi);
		m.Ficha.stamina = Math.Max(m.Ficha.stamina, m.Ficha.maxstamina);
		MandarFicha(m);

		// A IMAGEM DO MAJIN, ao lado dele (`:217-218`). Uma por prisioneiro.
		rec.Guardiao = ErguerOGuardiaoDoMajin(pl, m, interior, InteriorDoMajin.PixelDe(InteriorDoMajin.CelDoGuardiao(cel))).Id;

		CurarOMajinQueAbsorveu(pl);
		RecalcularOMajin(pl);

		Avisar(m, $"{NomeVisivel(pl)} absorve voce! Preso dentro dele, voce encara uma copia de {NomeVisivel(pl)} -- "
				  + "DERROTE-A pra escapar. Se perder, continua absorvido.");
		Avisar(pl, $"voce absorve {rec.Nome} -- o poder, as tecnicas e as roupas dele sao seus enquanto ele "
				   + $"estiver dentro de voce (+{rec.BonusDeBp:N0} de poder).");
		Falar(pl, Protocol.Fala.Emote, $"absorve {rec.Nome}!");
		GD.Print($"[server] {pl.Name} (Majin) ABSORVEU {rec.Nome}: +{rec.BonusDeBp:0} de BP, {rec.Verbos.Count} verb(s), "
				 + $"guardiao id {rec.Guardiao} com BP {AbsorcaoMajin.BpDoGuardiao(pl.Ficha.expressedBP):0}");
	}

	/// <summary>
	/// *"o prisioneiro chega VIVO, ACORDADO e DESTRAVADO: ele foi absorvido NOCAUTEADO (e muitas vezes agarrado
	/// / em knockback) -- o "KO = 0" cru de antes deixava TODOS os travamentos do KO/agarrao pendurados, e o
	/// prisioneiro ficava parado sem conseguir lutar"* (`MajinSaga.dm:183-210`).
	///
	/// A lista de la (`grabber`, `KO`, `grabParalysis`, `KB`, `KBParalysis`, `omegastun`, `paralyzed`,
	/// `stagger`, `attacking`, `blocking`...) traduzida pro que prende um corpo neste port.
	/// </summary>
	private void DestravarOPrisioneiro(ServerPlayer m)
	{
		// O AGARRAO -- `if(M.grabber ...)` (`:186-193`): *"o Majin costuma agarrar pra absorver"*.
		if (m.AgarradoPorId != 0 && CorpoNaMinhaZona(m, m.AgarradoPorId) is { } quemSegura)
			Soltar(quemSegura, MotivoDaSoltura.Absorvido);
		if (m.AgarrandoId != 0) Soltar(m, MotivoDaSoltura.Tecla);

		m.Ficha.KO = false;
		m.Combate.NocauteRestante = 0;
		m.Combate.NocautePorVital = false;
		m.Combate.Stun = 0;
		m.Combate.Recarga = 0;
		m.Combate.Guardar(false);

		m.TiquesDeVoo = 0;   // `KB = 0`: o arremesso em curso acaba aqui
		m.VooNoTique = 0;
		m.ArremessadoNoAr = false;
		EsquecerParalisia(m.Id);
	}

	/// <summary>`SpreadHeal(100, 1, 0)`, `Ki = MaxKi` e `overcharge = 1` (`MajinSaga.dm:141-142` e `:220-222`).</summary>
	private static void CurarOMajinQueAbsorveu(ServerPlayer pl)
	{
		// O `SpreadHeal` DE FOCO NOS VITAIS (`Injuries.dm:89-126`): havendo vital abaixo de 70%, cura SO esses;
		// nao havendo, cura todo membro ferido. (O terceiro argumento, zero, deixa de fora o membro
		// artificial -- que este port nao tem.)
		Body corpo = pl.Combate.Corpo;
		bool vitalFerido = corpo.Partes.Any(p => !p.Decepado && p.Papel != Vitalidade.Membro && p.Vida <= p.VidaMax * 0.7);
		if (vitalFerido) corpo.CurarVitais(AbsorcaoMajin.CuraDoMajin);
		else corpo.Curar(AbsorcaoMajin.CuraDoMajin);
		pl.Combate.SincronizarVida();

		// O `Math.Max` e o mesmo da absorcao do bio: la o `Ki = MaxKi` cru DERRUBAVA quem estivesse
		// comprimido acima dos 100% pela tecla C. O `overcharge` segura a sangria do excesso ate o Ki
		// voltar pra baixo do teto (`CargaDeKi.PrecoDoExcesso`).
		pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, pl.Ficha.MaxKi);
		pl.Ficha.overcharge = true;
	}

	/// <summary>O poder do Majin mudou (entrou ou saiu um bonus): a conta e a ficha saem agora, e nao em 200 ms.</summary>
	private void RecalcularOMajin(ServerPlayer pl)
	{
		pl.Ficha.Statify();
		RepercutirPoder(pl);
		pl.SigAtributos = "";
		MandarFicha(pl);
		MandarAbsorvidosDoMajin(pl);
	}

	private List<AbsorvidoPeloMajin> RegistrosDoMajin(int majinId)
	{
		if (!_dentroDoMajin.TryGetValue(majinId, out List<AbsorvidoPeloMajin>? l)) _dentroDoMajin[majinId] = l = [];
		return l;
	}

	// =====================================================================
	// O GUARDA-ROUPA -- `majin_wear_victim_outfit` (`MajinSaga.dm:83-118`)
	// =====================================================================
	/// <summary>
	/// *"o Majin veste COPIAS-OVERLAY (so visual, sem funcao) das roupas/equipamentos da ULTIMA vitima
	/// absorvida -- da pra saber quem foi a ultima refeicao. Nova absorcao troca a roupa; nada e transferido
	/// de verdade"* (`:85-88`).
	///
	/// A ROUPA FICA DEPOIS QUE O ABSORVIDO SAI, e isso e o que o original FAZ: quem a tira e so a absorcao
	/// seguinte (`majin_clear_outfit`, chamado de um lugar so, `:99`) -- o `rec.clothes` que o
	/// `majin_release` limparia (`:259`) nunca e preenchido. Como la a lista e `tmp`, aqui ela tambem nao
	/// vai pro disco: e um campo de sessao (<see cref="ServerPlayer.LookDoMajin"/>).
	///
	/// POR CIMA DA ROUPA DELE, como overlay que e: as pecas da vitima entram depois das do Majin, e no teto
	/// de pecas do port (<see cref="Jandirus.Core.Appearance.Appearance.MaxRoupa"/>) quem cede lugar sao as dele.
	/// </summary>
	private void VestirAUltimaRefeicao(ServerPlayer pl, ServerPlayer vitima)
	{
		pl.LookDoMajin = null;   // `majin_clear_outfit()`: a roupa da vitima ANTERIOR sai

		List<Jandirus.Core.Appearance.PecaDeRoupa> dela = VisualVisivel(vitima).Roupa;
		if (dela.Count > 0)
		{
			Jandirus.Core.Appearance.Appearance look = pl.Visual.Copiar();
			look.Roupa.AddRange(dela);
			int sobra = look.Roupa.Count - Jandirus.Core.Appearance.Appearance.MaxRoupa;
			if (sobra > 0) look.Roupa.RemoveRange(0, sobra);
			pl.LookDoMajin = look;

			Falar(pl, Protocol.Fala.Emote,
				  $"se molda... e as roupas de {NomeVisivel(vitima)} surgem na superficie do corpo dele!");
		}
		TrocarAparencias(pl);
	}

	// =====================================================================
	// QUEM VIU -- `MajinSaga.dm:169-175`
	// =====================================================================
	/// <summary>
	/// *"a friend who watches you get absorbed reacts as if they watched you DIE -> extreme anger"*.
	///
	/// E o gancho que o cabecalho do <see cref="AmigoAbatido"/> esperava (*"o dia em que a saga vier ela
	/// chama isto com `Extrema`"*). NAO PASSA PELO <see cref="LutoNaVizinhanca"/> porque as condicoes sao
	/// outras, e as duas diferencas sao do DM: a lista de afeto e mais curta que a da morte (`Good` e `Very
	/// Good`, sem `Love` nem `Rival/Good`) e ninguem pergunta se o algoz era inimigo da vitima -- ser
	/// engolido nao e duelo entre amigos.
	/// </summary>
	private void LutoPelaAbsorcao(ServerPlayer vitima, ServerPlayer majin)
	{
		if (!EhPessoa(vitima)) return;
		float raio2 = RaioDeTestemunha * RaioDeTestemunha;

		foreach (ServerPlayer o in ZoneList(vitima.Zone.Hash).ToList())
		{
			if (o == vitima || o == majin || !EhPessoa(o)) continue;   // `A == M || A == src || A.isNPC`
			if ((o.Pos - vitima.Pos).LengthSquared > raio2) continue;
			if (!o.Social.LutoPorAbsorcao(vitima.Assinatura)) continue;

			if (AmigoAbatido(o, vitima.Name, NivelDeRaiva.Extrema)) AnunciarRaiva(o, extrema: true);
		}
	}

	// =====================================================================
	// A IMAGEM -- `mob/npc/AbsorbGuardian` e `majin_spawn_guardian` (`MajinSaga.dm:296-387`)
	// =====================================================================
	/// <summary>
	/// *"a fightable copy of the Majin guarding one prisoner; beat it (HP&lt;=15) and that prisoner escapes"*.
	///
	/// ============================ UM ESPELHO "BURRO", DE PROPOSITO ============================
	/// O original ja teve este guardiao feito pelo `makeCopy` e desistiu (`:344-347`): a genetica falhava e
	/// *"o clone NUNCA aparecia"*. O que ficou e o que esta aqui -- um corpo comum com os SETE stats de
	/// combate do Majin (`physoff`, `physdef`, `technique`, `kioff`, `kidef`, `kiskill`, `speed`, `:369-375`),
	/// metade do poder EXPRESSO dele, e a cara dele.
	///
	/// O QUE ELE **NAO** TEM, porque la tambem nao tem: a raca do Majin por dentro (nao regenera membro, nao
	/// cura no meio da luta -- e um `mob/npc` sem genoma, com a regeneracao de fabrica, `Injuries.dm:202-205`),
	/// os multiplicadores raciais dele, as formas e as tecnicas dele.
	/// ========================================================================================
	/// </summary>
	private ServerPlayer ErguerOGuardiaoDoMajin(ServerPlayer majin, ServerPlayer preso, ZoneKey interior, Vec2 onde)
	{
		var g = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = $"imagem de {NomeVisivel(majin)}",   // `guard.name = "[name]'s image"` (`:356`)
			Zone = interior,
			Pos = onde,
			// A RACA E O GENERO SAO DO DESENHO: o indice de corpo da aparencia e relativo a lista deles (e
			// por isso valem os do disfarce, quando ha um). A regra de corpo -- a regeneracao -- e reescrita
			// logo abaixo.
			Race = majin.Disfarce?.Raca ?? majin.Race,
			Class = majin.Class,
			// A CARA QUE O MUNDO VE, SEM A ROUPA -- `guard.icon = icon` (`:359`, o icone VIVO do Majin) mais
			// o cabelo e os olhos (`:362-365`). As roupas la sao overlays a parte e NAO sao copiadas: a
			// imagem nao veste nem a roupa dele, nem a da ultima refeicao. A lista e esvaziada logo abaixo.
			Visual = VisualVisivel(majin).Copiar(),
			Genero = majin.Disfarce?.Genero ?? majin.Genero,
			Planeta = majin.Planeta,
			Idade = majin.Idade,
			LastInputMs = NowMs(),
			// O TEMPERO DE FABRICA: o `AbsorbGuardian` nao declara `behavior_vals`, entao vale o padrao de
			// todo `mob/npc` -- `list(50,50,50,50)` (`NPCs/NPCAI.dm:76`), que e o `new Cerebro()` daqui.
			Cerebro = new Cerebro(),
			PresoGuardado = preso.Id,
			Ficha = new Fighter(),
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};

		g.Visual.Roupa.Clear();

		Fighter f = g.Ficha, dele = majin.Ficha;
		f.Idade = dele.Idade;
		f.BP = AbsorcaoMajin.BpDoGuardiao(dele.expressedBP);
		f.physoff = dele.physoff; f.physdef = dele.physdef; f.technique = dele.technique;
		f.kioff = dele.kioff; f.kidef = dele.kidef; f.kiskill = dele.kiskill; f.speed = dele.speed;
		f.HP = 100;
		f.Anger = 100;            // `:379`
		f.staminadeBuff = 100;    // `:380`
		g.BpDoGuardiao = f.BP;    // `guard_seed_bp` (`:367`): o pino que o tique reescreve

		PorNoMundo(g);

		// O CORPO E O DE UM NPC SEM GENOMA (`Injuries.dm:202-205` e `Death/DeathRegen.dm:2`): `passiveRegen =
		// 0.05`, `canheallopped = 0`, `activeRegen = 1`, `DeathRegen = 0`, sem cura em combate. O `PorNoMundo`
		// acabou de dar a ele o perfil da RACA do Majin, que refaz membro no meio da briga.
		g.Combate.Corpo.Regen = new PerfilDeRegen { Regeneration = 0, CuraEmCombate = false, Passiva = 0.05, Ativa = 1, RegenDeMorte = 0 };
		g.Combate.Letal = false;   // `murderToggle` de um mob novo e 0: ele derruba, nao mata

		f.Ki = f.MaxKi;            // `:377-378`
		f.stamina = f.maxstamina;
		f.CurrentNutrition = Nutricao.Tanque(f.Metabolism);   // `:381-382`
		f.PowerLevel();            // `:383`

		// O VOO E A RAJADA -- ver a divergencia no cabecalho. A maestria de Ki do Majin, com piso no degrau
		// do voo; nada mais do livro dele.
		g.Livro.Dar(SkillDoKi);
		g.Niveis.Por(SkillDoKi, Math.Max(majin.Niveis.Nivel(SkillDoKi), MaestriaQueDestravaVoo));

		TrocarAparencias(g);
		return g;
	}

	/// <summary>
	/// O RAMO DO GUARDIAO NO <see cref="TicarUmCorpo"/>: a presa dele e o prisioneiro que ele guarda, e so
	/// ele -- o `guard.foundTarget(M)` de `:386`. Devolve falso quando nao ha o que fazer neste tique.
	/// </summary>
	private bool GuiarOGuardiaoDoMajin(ServerPlayer npc, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = npc.Pos;

		// O PRISIONEIRO SAIU (solto, deslogou, foi levado): nao ha por que este corpo existir. Quem solta
		// ja tira a imagem; esta linha e a rede pra quem saiu por uma porta que nao passou por la.
		if (!_players.TryGetValue(npc.PresoGuardado, out ServerPlayer? preso) || preso.Zone.Hash != npc.Zone.Hash)
		{
			RemoverNpc(npc);
			return false;
		}

		// VENCIDO, NAO AGE: quem ve isso e solta o prisioneiro e a olhada do `TickDoMajin`.
		if (npc.Ficha.dead || npc.Ficha.KO) { npc.Moving = false; return false; }

		// O BP PINADO -- o `NPCTicker()` dele (`:314-318`): lutar renderia BP pra ele tambem, e a imagem
		// acabaria mais forte que a metade do Majin que ela e.
		if (npc.BpDoGuardiao > 0 && Math.Abs(npc.Ficha.BP - npc.BpDoGuardiao) > 0.5)
		{
			npc.Ficha.BP = npc.BpDoGuardiao;
			npc.Ficha.Statify();
			npc.Ficha.PowerLevel();
			npc.SigAtributos = "";
		}

		presa = preso;
		destino = preso.Pos;
		return true;
	}

	// =====================================================================
	// AS BORDAS -- derivadas, uma vez por tique
	// =====================================================================
	/// <summary>
	/// Chamado do <see cref="TickCombate"/>, DEPOIS do laco dos corpos (soltar gente mexe nas listas de duas
	/// zonas, e erguer/tirar corpo mexe no `_players`). Ver "NAO HA CAMPO" no cabecalho.
	/// </summary>
	private void TickDoMajin(double dt)
	{
		if (_dentroDoMajin.Count == 0) return;

		_relogioDosGuardioes += dt;
		bool olhar = _relogioDosGuardioes >= AbsorcaoMajin.SegundosEntreOlhadas;
		if (olhar) _relogioDosGuardioes = 0;

		foreach (int majinId in _dentroDoMajin.Keys.ToArray())
		{
			if (!_dentroDoMajin.TryGetValue(majinId, out List<AbsorvidoPeloMajin>? lista)) continue;
			_players.TryGetValue(majinId, out ServerPlayer? majin);

			// (3) O MAJIN CAIU -- `KO()` chama o `majin_escape_all()` (`KO.dm:13-14`), e o `Death()` chama o
			// `KO(-1)`. Sem Majin no mundo (uma borda que o logout ja cobre) a resposta e a mesma: saem.
			bool caiu = majin == null || majin.Ficha.KO || majin.Ficha.dead;
			if (caiu && !AbsorcaoMajin.NocauteNaoSoltaDeTeste)
			{
				CuspirTodosDoMajin(majinId, "todos os que voce segurava dentro de si se libertaram!");
				continue;
			}

			for (int i = lista.Count - 1; i >= 0; i--)
			{
				AbsorvidoPeloMajin rec = lista[i];
				if (rec.Quem == 0) continue;   // devorado: so o bonus, ate o Majin cair

				// SAIU POR OUTRA PORTA (um admin o puxou, o torneio o chamou): o registro vai embora com o
				// que o Majin tinha por causa dele. Ele ja esta onde quer que o tenham posto.
				if (!_players.TryGetValue(rec.Quem, out ServerPlayer? preso) || InteriorDoMajin.Anfitriao(preso.Zone) != majinId)
				{ CuspirDoMajin(majinId, rec); continue; }

				// (2) MORREU LA DENTRO -- `if(absorbed_into) absorbed_into.majin_release_by_mob(src)`, a
				// primeira linha util do `Death()` (`Death.dm:9`). A morte segue o caminho dela do lado de fora.
				if (preso.Ficha.dead) { CuspirDoMajin(majinId, rec); continue; }

				if (!olhar) continue;

				// (1) A IMAGEM FOI VENCIDA -- o `guard_watch()`. Imagem que sumiu do mundo conta como vencida:
				// o prisioneiro nunca fica la dentro sem ter quem vencer.
				_players.TryGetValue(rec.Guardiao, out ServerPlayer? g);
				if (g != null && !AbsorcaoMajin.GuardiaoVencido(g.Ficha.KO, g.Ficha.dead, g.Ficha.HP)) continue;

				if (AbsorcaoMajin.GuardiaoVencidoNaoSoltaDeTeste)
				{
					if (g != null) RemoverNpc(g);
					continue;
				}
				if (majin != null) Avisar(majin, "um prisioneiro venceu a sua imagem e se libertou!");   // `:291`
				CuspirDoMajin(majinId, rec);
			}
		}
	}

	// =====================================================================
	// SOLTAR -- `majin_release` (`MajinSaga.dm:252-278`)
	// =====================================================================
	/// <summary>
	/// UM ABSORVIDO SAI, e o Majin perde o que tinha por causa dele. A porta unica das cinco saidas.
	///
	/// *"ALWAYS spill them out"* (`:264-267`): quem esta no registro e esta dentro deste Majin sai, sem
	/// outra condicao. Sai NOCAUTEADO so quando o proprio Majin esta caido (`if(KO) ... M.KO()`, `:272`) --
	/// *"vencer o guardiao interno / expulsao voluntaria = sai ACORDADO e de pe"*.
	/// </summary>
	private void CuspirDoMajin(int majinId, AbsorvidoPeloMajin rec)
	{
		if (!_dentroDoMajin.TryGetValue(majinId, out List<AbsorvidoPeloMajin>? lista) || !lista.Remove(rec)) return;
		if (lista.Count == 0) _dentroDoMajin.Remove(majinId);
		_players.TryGetValue(majinId, out ServerPlayer? majin);

		// `if(rec.guardian) del(rec.guardian)` (`:254`)
		if (rec.Guardiao != 0 && _players.TryGetValue(rec.Guardiao, out ServerPlayer? g)) RemoverNpc(g);
		rec.Guardiao = 0;

		if (majin != null)
		{
			majin.Ficha.majin_absorb_bp = Math.Max(majin.Ficha.majin_absorb_bp - rec.BonusDeBp, 0);   // `:255`
			foreach (string v in rec.Verbos) majin.VerbosAbsorvidos?.Remove(v);                       // `:256-258`
			if (majin.VerbosAbsorvidos is { Count: 0 }) majin.VerbosAbsorvidos = null;
		}

		if (rec.Quem != 0 && _players.TryGetValue(rec.Quem, out ServerPlayer? m) && InteriorDoMajin.Anfitriao(m.Zone) == majinId)
		{
			(ZoneKey zona, Vec2 pos) = OndeOMajinCospe(majin);
			MoveToZone(m.Id, zona, pos);

			bool majinCaido = majin != null && (majin.Ficha.KO || majin.Ficha.dead);
			if (majinCaido && !m.Ficha.KO && !m.Ficha.dead)
			{
				// `M.KO()` sem prazo: `spawn(rand(2000,2500)*KOMult) Un_KO()` (`KO.dm:112-114`), que e de
				// onde sai o teto do nocaute deste port.
				m.Combate.Nocautear(MeleeResolver.TetoDoNocaute);
				MandarFicha(m);
			}
			Avisar(m, "voce e cuspido de volta pro mundo.");   // `:273`
			GD.Print($"[server] {m.Name} saiu de dentro do Majin #{majinId} em {zona.Name}"
					 + (majinCaido ? " (nocauteado: o Majin caiu)" : ""));
		}

		// `spawn majin_check_pure_unlock()` (`:278`) e da saga, que nao foi portada -- ver o cabecalho.
		if (majin != null) RecalcularOMajin(majin);
	}

	/// <summary>`majin_escape_all()` (`MajinSaga.dm:389-393`), e o "Todos" do `Absorb_Expel()`.</summary>
	private void CuspirTodosDoMajin(int majinId, string aviso)
	{
		if (!_dentroDoMajin.TryGetValue(majinId, out List<AbsorvidoPeloMajin>? lista)) return;
		foreach (AbsorvidoPeloMajin rec in lista.ToArray()) CuspirDoMajin(majinId, rec);
		if (aviso.Length > 0 && _players.TryGetValue(majinId, out ServerPlayer? majin)) Avisar(majin, aviso);
	}

	/// <summary>
	/// `majin_safe_release_turf()` (`MajinSaga.dm:236-240`): *"next to me when I'm on a real overworld turf,
	/// otherwise a hard overworld fallback. NEVER a pocket z"* -- e a alternativa e o meio da Terra,
	/// `locate(rand(240,260), rand(240,260), 1)`.
	///
	/// "EU" E O CORPO DO MAJIN: se ele esta fora do corpo (em transe, ao leme), quem esta no mundo e o corpo
	/// que ele largou, e e ao lado DELE que o cuspido cai -- a zona da mente e de uma pessoa so.
	///
	/// PELO `PontoLivrePerto`, como todo corpo que este port poe no mundo: a celula a frente pode ser parede,
	/// beirada ou AGUA (o Majin voa sobre o mar, e quem sai dele sai a pe), e ai o ponto e o chao seco mais
	/// proximo. A primeira corrida da `--majinteste` mediu isso sem querer -- o palco dela estava no mar da
	/// Terra, e o solto apareceu a 17 tiles, na praia. Hoje e a familia 14 de la.
	/// </summary>
	private (ZoneKey Zona, Vec2 Pos) OndeOMajinCospe(ServerPlayer? majin)
	{
		ServerPlayer? corpo = majin?.BonecoLargado ?? majin;
		if (corpo != null && !DentroDeUmMajin(corpo) && !NaMente(corpo))
		{
			Vec2 aFrente = corpo.Pos + MeleeArea.Frente(corpo.Facing) * ZoneCollision.TileSize;
			return (corpo.Zone, MapaDaZonaOuCatalogo(corpo.Zone)?.PontoLivrePerto(aFrente) ?? aFrente);
		}

		var meio = new Vec2((_rng.Next(240, 261) + 0.5f) * ZoneCollision.TileSize,
							(_rng.Next(240, 261) + 0.5f) * ZoneCollision.TileSize);
		return (SpawnZone, MapaDaZonaOuCatalogo(SpawnZone)?.PontoLivrePerto(meio) ?? meio);
	}

	// =====================================================================
	// EXPELIR -- `Absorb_Expel()` (`Absorption.dm:172-191`)
	// =====================================================================
	/// <summary>
	/// O verb "Expel" do mesmo objeto. La e uma caixa (*"Expulsar quem do seu corpo?"* -- Todos, Um,
	/// Cancelar) e depois uma lista; aqui sao botoes: um pra todos e um por absorvido, com o numero dele
	/// (<see cref="MandarAbsorvidosDoMajin"/>). <paramref name="qual"/> vazio = todos.
	/// </summary>
	private void ExpelirDoMajin(ServerPlayer pl, string qual)
	{
		if (!_dentroDoMajin.TryGetValue(pl.Id, out List<AbsorvidoPeloMajin>? lista) || lista.Count == 0)
		{ Avisar(pl, "nao ha ninguem dentro de voce."); return; }

		if (qual.Length == 0)
		{
			CuspirTodosDoMajin(pl.Id, "voce expulsa todos que estavam dentro de voce.");
			return;
		}

		AbsorvidoPeloMajin? rec = int.TryParse(qual, out int numero) ? lista.Find(r => r.Numero == numero) : null;
		if (rec == null) { Avisar(pl, "esse ja nao esta dentro de voce."); return; }

		CuspirDoMajin(pl.Id, rec);
		Avisar(pl, $"voce expulsa {rec.Nome} do seu corpo.");
	}

	/// <summary>
	/// QUEM ESTA DENTRO DESTE MAJIN, pro menu dele -- a lista do `input(...) in pick_list` (`:183-187`), que
	/// la mostra o nome de quem esta online e *"Absorvido (sig)"* de quem nao esta.
	/// </summary>
	private void MandarAbsorvidosDoMajin(ServerPlayer pl)
	{
		if (pl.Peer is not { } peer) return;

		IReadOnlyList<AbsorvidoPeloMajin> lista = AbsorvidosPor(pl.Id);
		var w = Protocol.Begin(Protocol.S2C.AbsorvidosDoMajin);
		w.Put((byte)Math.Min(lista.Count, byte.MaxValue));
		for (int i = 0; i < lista.Count && i < byte.MaxValue; i++)
		{
			w.Put(lista[i].Numero);
			w.Put(lista[i].Nome);
			w.Put(lista[i].Quem == 0);   // devorado: expelir so devolve o poder dele
		}
		peer.Send(w, Protocol.ChannelReliable, LiteNetLib.DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// SAIR DO JOGO E VOLTAR -- `Login.dm:181-202` e `:356`
	// =====================================================================
	/// <summary>
	/// QUEM SAI DO JOGO DESFAZ A ABSORCAO ANTES DO SAVE -- o `DoLogoutStuff`: *"Tear it down BEFORE the save
	/// so nobody is stranded inside a dead z and no stale rec persists"*. Na ordem de la: *"I'm the one
	/// absorbed -> free me first"*, depois *"I'm the Majin -> spit everyone back out"*.
	/// </summary>
	private void DesfazerAAbsorcaoAoSair(ServerPlayer pl)
	{
		if (InteriorDoMajin.Anfitriao(pl.Zone) is int dono and not 0)
		{
			AbsorvidoPeloMajin? rec = AbsorvidosPor(dono).FirstOrDefault(r => r.Quem == pl.Id);
			if (rec != null) CuspirDoMajin(dono, rec);

			// AINDA LA DENTRO (sem registro: um admin o pos la): o save nao grava um bolsao. E o
			// `if(z in majin_interior_zs) loc = locate(...)` de `Login.dm:181-182`.
			if (DentroDeUmMajin(pl))
			{
				(ZoneKey zona, Vec2 pos) = OndeOMajinCospe(null);
				MoveToZone(pl.Id, zona, pos);
			}
		}

		CuspirTodosDoMajin(pl.Id, "");
		pl.Ficha.majin_absorb_bp = 0;
	}

	/// <summary>
	/// O LOGIN DE QUEM FICOU PRA TRAS (o servidor caiu com gente absorvida): o bolsao nao existe mais, e o
	/// poder emprestado tambem nao. E a *"limbo recovery"* de `Login.dm:356` -- *"never leave a finished
	/// character in ... a dead Majin pocket z"*.
	/// </summary>
	private void AcordarSemAbsorcao(ServerPlayer pl)
	{
		pl.Ficha.majin_absorb_bp = 0;
		if (!DentroDeUmMajin(pl)) return;

		GD.Print($"[server] {pl.Name} entrou dentro de um Majin que nao existe mais -- devolvido ao berco");
		PousarNoBercoSemPacote(pl);
		Avisar(pl, "o corpo que prendia voce nao existe mais. Voce acorda em terra firme.");
	}
}
