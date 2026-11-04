using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O MAJIN BOO DA SAGA -- GORDO, SUPER E KID (2026-11-01, o pedido 218) ============================
/// A regra e os numeros estao em <see cref="SagaDoBuu"/> (Core); aqui mora o que o servidor FAZ com o corpo do molde `majin_boo` que a
/// cadeia de sagas poe na Terra. Tres degraus da MESMA ficha, e cada um muda o que o corpo e:
///
///   * GORDO (o Fat Boo, 30 bilhoes): NEUTRO -- a presa dele e a do habitante (so quem bateu nele, `PresaDoNpc`), e sem presa ele passeia
///     pela Terra pela rotina de todo chefe (`TicarARotina`). O relogio da divisao (<see cref="SagaDoBuu.SegundosAteADivisao"/>) anda no
///     <see cref="TickDoBoo"/> e vai pro disco (`EstadoDoChefe.SegundosNoDegrau`). Vencido, ele toca a cena do SUPER MAJIN (a outra
///     metade voltando pra dentro, `Cinematicas.MajinForma3`) e sobe.
///   * SUPER (45 bilhoes): agressivo e de golpe NAO-LETAL (`AjustarOBooAoDegrau`). Quem ele derruba ele absorve pela mesma porta do
///     Majin jogador (`SelarNoMajin`: a roupa, as tecnicas, o interior) -- com UMA imagem so pra todos la dentro
///     (<see cref="ImagemCompartilhadaDoBoo"/>). A imagem vencida solta todos de uma vez (o `TickDoMajin`), e ele vira o Kid.
///   * KID (55 bilhoes): a cena da carne encolhendo (a da Forma Pura, `Cinematicas.MajinPura`), a esfera ROSA na Terra (a Death Ball do
///     ultimato, rosa -- `AtaqueDoUltimato.EsferaRosa`), os cinco minutos de destruicao (`ComecarDestruicao`) e a saida pela Transmissao
///     Instantanea pro Ceu (<see cref="TickDaFugaDoKid"/>), onde ele fica e caca quem aparecer. Morto la, a saga acaba pela porta de
///     sempre (`MorreuOChefe`).
///
/// ============================ A MORTE QUE NAO E O FIM ============================
/// Em toda etapa ele e Majin: o soco o despedaca e ele se refaz (`CombatState.QuandoONucleoCede`, a raca dele). So o KI letal o mata --
/// e ai, no gordo e no super, a morte e NEGADA (<see cref="NegarAMorteDoBoo"/>) e vira a etapa seguinte: a saga nao acaba no Fat Boo nem
/// pula o Kid. ESCOLHA MINHA, a confirmar: o pedido conta a saga ate o Kid no Ceu.
/// ==================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Os Boos de saga que viram Kid no proximo tique do Boo: a imagem de dentro caiu (o `TickDoMajin`) ou o super morreu de ki.</summary>
	private readonly HashSet<int> _booViraKid = [];

	/// <summary>O id do Kid que esta saltando pro Ceu agora -- a unica viagem de corpo de saga que o `MoveToZone` deixa passar.</summary>
	private int _booIndoProCeu;

	private enum FaseDaFugaDoKid : byte { Cena, Esfera, Saindo }

	/// <summary>A fuga do Kid em curso: a cena da forma, a esfera rosa no ar, a saida piscando. Uma so -- ha um Boo de saga por vez.</summary>
	private sealed class FugaDoKid
	{
		public required ServerPlayer Kid;
		public required EstadoDoChefe Estado;
		public FaseDaFugaDoKid Fase;
		public long Desde;
	}

	private FugaDoKid? _fugaDoKid;

	// =====================================================================
	// QUEM
	// =====================================================================
	/// <summary>
	/// Este corpo e o molde do Boo posto por um evento de saga -- a cadeia, o Inferno (o vilao que volta la), o vilao solto: o GOLPE de cada
	/// degrau vale pra todos eles (`AjustarOBooAoDegrau`). O desenho, desde o pedido 229, e do dado e vale pra qualquer corpo do molde.
	/// </summary>
	private static bool EhCorpoDoBoo(ServerPlayer npc) =>
		npc.Papel is { DaSaga: true } p && string.Equals(p.Molde.Id, SagaDoBuu.Molde, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// O BOO DA CADEIA -- o corpo do chefe do elo da Terra, no mundo. So ELE e neutro, absorve, foge pro Ceu e nao morre antes da hora: o
	/// Boo que volta no Inferno e o vilao solto continuam brigando como todo vilao (a regra e da saga, e nao do molde).
	/// </summary>
	private bool EhOBooDaSaga(ServerPlayer npc) => EhCorpoDoBoo(npc) && ChefeDaCadeia(npc) != null;

	/// <summary>O estado do chefe da cadeia que este corpo e (nulo = nenhum).</summary>
	private EstadoDoChefe? ChefeDaCadeia(ServerPlayer npc)
	{
		foreach (EloDaSaga elo in _cadeia)
			if (_sagas.Do(elo.Id) is { Fase: FaseDaSaga.EmCurso } est
				&& est.Chefes.Find(c => c.Corpo == npc.Id && c.Fase == FaseDoChefe.NoMundo
										&& string.Equals(c.Molde, SagaDoBuu.Molde, StringComparison.OrdinalIgnoreCase)) is { } ec)
				return ec;
		return null;
	}

	/// <summary>O Fat Boo: neutro, so revida quem bate nele.</summary>
	private bool BooNeutro(ServerPlayer npc) => EhOBooDaSaga(npc) && npc.Papel!.Estagio == SagaDoBuu.DegrauGordo;

	/// <summary>Este molde de chefe, neste degrau salvo, nasce no Ceu -- o Kid que ja fugiu (ou cuja Terra ja esta condenada).</summary>
	private bool NasceNoCeu(EloDaSaga elo, EstadoDoChefe ec) =>
		string.Equals(ec.Molde, SagaDoBuu.Molde, StringComparison.OrdinalIgnoreCase) && ec.Degrau >= SagaDoBuu.DegrauKid
		&& (ec.NoCeu || ZonaCondenada(ZoneKey.Premade(elo.Planeta)));

	// =====================================================================
	// O DEGRAU
	// =====================================================================
	/// <summary>
	/// CHAMADO NO FIM DO `EntrarNoDegrau` (o nascimento e cada subida): o GOLPE do degrau do Boo de saga -- o Super Boo nocauteia em vez de
	/// matar: *"ele n luta no letal, ele luta pra te nocautear e absorver"*. (2026-11-02, o pedido 229) O DESENHO saiu daqui: o corpo, a cor
	/// e a roupa de cada degrau sao do dado (o `"corpoDoBoo"` do `npcs.json`, no `EntrarNoDegrau`, pra qualquer corpo); aqui fica so o que
	/// o degrau FAZ -- o dono: *"padroniza sim, mas mantendo as caracteristicas de cada degrau (super buu n mata, ele absorve etc)"*.
	/// </summary>
	private void AjustarOBooAoDegrau(ServerPlayer npc)
	{
		if (!EhCorpoDoBoo(npc)) return;
		npc.Combate.Letal = npc.Papel!.Estagio != SagaDoBuu.DegrauSuper && !ChefeNaoLetalDeTeste;
	}

	// =====================================================================
	// O TIQUE (1 Hz)
	// =====================================================================
	/// <summary>Uma vez por segundo: o relogio da divisao, a caca do Super Boo e a virada pro Kid.</summary>
	private void TickDoBoo()
	{
		foreach (EloDaSaga elo in _cadeia)
		{
			if (_sagas.Do(elo.Id) is not { Fase: FaseDaSaga.EmCurso } est) continue;
			foreach (EstadoDoChefe ec in est.Chefes)
			{
				if (ec.Fase != FaseDoChefe.NoMundo || !string.Equals(ec.Molde, SagaDoBuu.Molde, StringComparison.OrdinalIgnoreCase)) continue;
				if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? boo) || !EhOBooDaSaga(boo) || boo.Ficha.dead) continue;
				PassoDoBoo(ec, boo);
			}
		}
	}

	private void PassoDoBoo(EstadoDoChefe ec, ServerPlayer boo)
	{
		switch (boo.Papel!.Estagio)
		{
			case SagaDoBuu.DegrauGordo:
				ec.SegundosNoDegrau += 1;
				if (ec.SegundosNoDegrau >= SagaDoBuu.SegundosAteADivisao) DividirOBoo(ec, boo);
				break;

			case SagaDoBuu.DegrauSuper:
				if (_booViraKid.Remove(boo.Id)) { VirarOKidBoo(ec, boo); break; }
				// (2026-11-01, o pedido 219h) O SUPER BOO LARGA A GOSMA do Majin -- so ele, *"os outros buus como fat e kid nao podem usar"*. Sem
				// gosma no mapa, o relogio do degrau conta, e no prazo ele larga uma onde esta (sem o preco dos 27%: o poder dele e o da saga).
				if (!TemGosma(boo.Id) && !EmCena(boo) && !boo.Ficha.KO && !boo.Combate.Desfeito
					&& (ec.SegundosNoDegrau += 1) >= Jandirus.Core.Races.GosmaDoMajin.SegundosAteAGosmaDoSuperBoo)
				{
					ec.SegundosNoDegrau = 0;
					PorAGosma(boo, cobra: false);
				}
				CacarPraAbsorver(boo);
				break;

			case SagaDoBuu.DegrauKid:
				_booViraKid.Remove(boo.Id);
				if (!ec.NoCeu && _fugaDoKid == null) ComecarAFugaDoKid(ec, boo);
				break;
		}
	}

	/// <summary>
	/// A DIVISAO: *"apos um tempo o jogo vai falar q ele se dividiu em 2 (evil buu e fat buu) e o evil absorveu o fat, assim nascendo o super
	/// buu"*. A cena VEM ANTES do degrau (o corpo novo que chega no meio dela espera a virada, `World._pendentes`): e a do Majin jogador que
	/// devora a propria outra metade -- a passagem `super majin transform`. Caido ou ja em cena, ele espera o proximo segundo.
	/// </summary>
	private void DividirOBoo(EstadoDoChefe ec, ServerPlayer boo)
	{
		if (boo.Ficha.KO || EmCena(boo)) return;
		ec.SegundosNoDegrau = 0;
		CenaDoMajin(boo, Cinematicas.CenaMajin.Forma3);
		AvancarEstagio(boo);
		ec.Degrau = boo.Papel!.Estagio;
		boo.Combate.Curar();
		AnunciarNoMundo(SagaDoBuu.FraseDaDivisao);
		GD.Print($"[server] SAGA DO BOO: o Fat Boo se dividiu -- SUPER BOO, degrau {ec.Degrau + 1}, BP {boo.Ficha.BP:N0}");
		SalvarSagas();
	}

	// =====================================================================
	// O SUPER BOO ABSORVE
	// =====================================================================
	/// <summary>
	/// A CACA DO SUPER BOO: o caido mais perto que da pra absorver (gente de verdade, nocauteada e viva -- a regra do Majin jogador). Perto
	/// (<see cref="SagaDoBuu.TilesDoBote"/>), o bote e a absorcao; longe, o alvo imposto pelo roteiro o leva ate la -- a caca normal pula quem
	/// esta no chao. Sem caido, a presa volta a ser a do chefe (o planeta inteiro).
	/// </summary>
	private void CacarPraAbsorver(ServerPlayer boo)
	{
		PapelDeNpc papel = boo.Papel!;
		if (boo.Ficha.KO || EmCena(boo) || boo.Combate.Desfeito) { papel.PresaDoRoteiro = 0; return; }

		ServerPlayer? caido = null;
		float perto = float.MaxValue;
		foreach (ServerPlayer o in ZoneList(boo.Zone.Hash))
		{
			if (o == boo || !EhPessoa(o) || !PodeSerAbsorvidoPeloMajin(o) || CorpoDerivado(o) || NaMente(o) || DentroDeUmMajin(o)) continue;
			if (PorQueNaoLevaOPreso(o).Length > 0) continue;
			float d2 = (o.Pos - boo.Pos).LengthSquared;
			if (d2 < perto) { perto = d2; caido = o; }
		}
		if (caido == null) { papel.PresaDoRoteiro = 0; return; }

		float bote = SagaDoBuu.TilesDoBote * ZoneCollision.TileSize;
		if (perto > bote * bote) { papel.PresaDoRoteiro = caido.Id; return; }
		papel.PresaDoRoteiro = 0;
		Transportar(boo, caido.Zone, caido.Pos + new Vec2(ZoneCollision.TileSize, 0));
		SelarNoMajin(boo, caido);
		GD.Print($"[server] SAGA DO BOO: o Super Boo absorveu {caido.Name} ({AbsorvidosPor(boo.Id).Count} dentro)");
	}

	/// <summary>
	/// A IMAGEM UNICA DO SUPER BOO: *"vai ter apenas 1 clone dele no interior q ao ser derrotado todos os jogadores sao expulsos ao mesmo
	/// tempo"*. A de qualquer absorvido que ainda esta no mundo serve pro proximo -- e por ser a mesma, vence-la solta todos no mesmo tique
	/// (o `TickDoMajin` le a imagem de cada registro: a removida conta como vencida). Nula = a primeira (quem chama ergue) ou nao e o Boo.
	/// </summary>
	private ServerPlayer? ImagemCompartilhadaDoBoo(ServerPlayer majin)
	{
		if (!EhOBooDaSaga(majin) || !_dentroDoMajin.TryGetValue(majin.Id, out List<AbsorvidoPeloMajin>? lista)) return null;
		foreach (AbsorvidoPeloMajin r in lista)
			if (r.Guardiao != 0 && _players.TryGetValue(r.Guardiao, out ServerPlayer? g) && !g.Ficha.dead) return g;
		return null;
	}

	/// <summary>Esta imagem e a do Super Boo de saga (o anfitriao do interior em que ela esta)?</summary>
	private bool EhImagemDoBoo(ServerPlayer imagem) =>
		InteriorDoMajin.Anfitriao(imagem.Zone) is int anfitriao && _players.TryGetValue(anfitriao, out ServerPlayer? m) && EhOBooDaSaga(m);

	/// <summary>
	/// QUEM A IMAGEM DO SUPER BOO ENFRENTA AGORA: o prisioneiro DE PE mais perto, la dentro; sem ninguem de pe, o mais perto de todos. Nulo =
	/// ninguem mais la dentro (e ela sai do mundo).
	/// </summary>
	private ServerPlayer? PresoDaImagemDoBoo(ServerPlayer imagem)
	{
		ServerPlayer? dePe = null, qualquer = null;
		float pertoDePe = float.MaxValue, perto = float.MaxValue;
		foreach (ServerPlayer o in ZoneList(imagem.Zone.Hash))
		{
			if (o == imagem || !EhPessoa(o) || o.Ficha.dead) continue;
			float d2 = (o.Pos - imagem.Pos).LengthSquared;
			if (d2 < perto) { perto = d2; qualquer = o; }
			if (!o.Ficha.KO && d2 < pertoDePe) { pertoDePe = d2; dePe = o; }
		}
		return dePe ?? qualquer;
	}

	// =====================================================================
	// O KID BOO
	// =====================================================================
	/// <summary>
	/// A IMAGEM CAIU (ou o super morreu de ki): todos que ainda estavam dentro saem, a cena da carne encolhendo toca ANTES do degrau (o corpo
	/// novo espera a virada), e ele vira o Kid -- 55 bilhoes, de golpe letal, inteiro. A esfera vem no fim da cena (<see cref="TickDaFugaDoKid"/>).
	/// </summary>
	private void VirarOKidBoo(EstadoDoChefe ec, ServerPlayer boo)
	{
		DesfazerAsGosmasDe(boo.Id, "");   // (2026-11-01, o pedido 219h) o Kid nao usa a gosma: a do Super se desfaz na virada
		CuspirTodosDoMajin(boo.Id, "", fuga: true);
		CenaDoMajin(boo, Cinematicas.CenaMajin.KidBoo);
		AvancarEstagio(boo);
		ec.Degrau = boo.Papel!.Estagio;
		ec.SegundosNoDegrau = 0;
		ec.NoCeu = false;
		boo.Combate.Curar();
		AnunciarNoMundo(SagaDoBuu.FraseDoKid);
		GD.Print($"[server] SAGA DO BOO: KID BOO, degrau {ec.Degrau + 1}, BP {boo.Ficha.BP:N0}");
		SalvarSagas();
		ComecarAFugaDoKid(ec, boo);
	}

	private void ComecarAFugaDoKid(EstadoDoChefe ec, ServerPlayer kid) =>
		_fugaDoKid = new FugaDoKid { Kid = kid, Estado = ec, Fase = FaseDaFugaDoKid.Cena, Desde = NowMs() };

	/// <summary>
	/// A FUGA DO KID, no relogio fino (30 Hz): *"no exato momento q ele virar kid buu ele vai carregar uma super esfera de energia rosa ... e
	/// jogar na direção da terra, apos andar uns tiles e explodir a terra vai começar a entrar no modo dos 5 minutos de destruição, e logo em
	/// seguida kid buu vai sumir da terra no mesmo efeito do instant transmission e ir pro planeta heaven"*. Tres tempos:
	///   1. o fim da cena da forma: a esfera sobe (`ComecarASupernova` com a rosa -- parado e intocavel ate a explosao, pela porta do ultimato);
	///   2. a explosao (a carga e a queda do Core, `Supernova.SegundosAteAExplosao`): a destruicao com o BP do Kid, e o clarao da SAIDA;
	///   3. <see cref="TransmissaoEmNiveis.AtrasoDaSaidaMs"/> depois, o salto pro Ceu (<see cref="LevarOKidProCeu"/>) -- os mesmos tempos do IT.
	/// </summary>
	private void TickDaFugaDoKid()
	{
		// (2026-11-02, o pedido 227m) O KID QUE JA ESTA NO CEU se entedia sem luta -- ver `GameServer.ViloesInvasores.cs`
		VigiarOTedioDoKid();
		if (_fugaDoKid is not { } f) return;
		if (!_players.TryGetValue(f.Kid.Id, out ServerPlayer? kid) || !ReferenceEquals(kid, f.Kid) || kid.Ficha.dead)
		{
			_fugaDoKid = null;
			return;
		}
		long agora = NowMs();
		switch (f.Fase)
		{
			case FaseDaFugaDoKid.Cena:
				if (EmCena(kid) || kid.Ficha.KO) return;
				ComecarASupernova(kid, Jandirus.Core.Npc.AtaqueDoUltimato.EsferaRosa);
				f.Fase = FaseDaFugaDoKid.Esfera;
				f.Desde = agora;
				return;

			case FaseDaFugaDoKid.Esfera:
				if (agora - f.Desde < Jandirus.Core.Npc.Supernova.SegundosAteAExplosao * 1000) return;
				// A ESFERA BATEU: o corpo volta a ser livre (sem o "se desfez" da Supernova cortada -- esta acabou), e a Terra entra na morte
				kid.SupernovaDesde = 0;
				kid.CenaSegundos = 0;
				if (!ComecarDestruicao(kid.Zone, kid.Ficha.expressedBP, "saga 'boo': a esfera rosa do Kid Boo", kid.Id))
					GD.PushWarning($"[server] a esfera do Kid caiu em '{kid.Zone.Name}', mas a destruicao nao pegou (ja condenado, ou nao e planeta)");
				PiscarNaZona(kid.Zone, kid.Id, kid.Pos);
				f.Fase = FaseDaFugaDoKid.Saindo;
				f.Desde = agora;
				return;

			case FaseDaFugaDoKid.Saindo:
				if (agora - f.Desde < TransmissaoEmNiveis.AtrasoDaSaidaMs) return;
				_fugaDoKid = null;
				LevarOKidProCeu(kid, f.Estado);
				return;
		}
	}

	/// <summary>
	/// O SALTO PRO CEU: a unica viagem de corpo de saga que o `MoveToZone` deixa passar (<see cref="_booIndoProCeu"/>), e o clarao da CHEGADA
	/// 0,1 s depois, pela viagem em curso do IT na fase `Chegando` (o mesmo caminho do chefe que volta do alem, `ChegarAoLadoDoMatador`).
	/// Ele fica la: e o planeta dele agora, e quem o derrota la termina a saga.
	/// </summary>
	private void LevarOKidProCeu(ServerPlayer kid, EstadoDoChefe ec)
	{
		var ceu = ZoneKey.Premade(SagaDoBuu.ZonaDoKid);
		_booIndoProCeu = kid.Id;
		try { Transportar(kid, ceu, PontoDeHabitante(ceu, ++_lugarDasSagas)); }
		finally { _booIndoProCeu = 0; }
		if (kid.Zone.Hash != ceu.Hash)
		{
			GD.PushWarning($"[server] o Kid Boo nao chegou ao Ceu (ficou em {kid.Zone.Name})");
			return;
		}
		_transmissoesEmCurso[kid.Id] = new TransmissaoEmCurso
		{
			Quem = kid, Pedido = new TransmissaoG11 { Alvo = kid.Id }, Fase = FaseDaTransmissao.Chegando, Desde = NowMs(),
		};
		ec.NoCeu = true;
		AnunciarNoMundo(SagaDoBuu.FraseDaFuga);
		GD.Print($"[server] SAGA DO BOO: o Kid Boo saltou pro Ceu ({SagaDoBuu.ZonaDoKid})");
		SalvarSagas();
	}

	// =====================================================================
	// A MORTE NEGADA
	// =====================================================================
	/// <summary>
	/// O KI LETAL QUE MATARIA O GORDO OU O SUPER vira a etapa seguinte (ver o cabecalho): o gordo se divide no proximo segundo (o relogio da
	/// divisao vence agora), o super vira o Kid (soltando quem estiver dentro). O corpo fica INTEIRO e de pe -- a mesma resposta do chefe com
	/// forma por vir (`NegarMorteDoChefe`). Nada muda de zona aqui: isto roda de dentro dos lacos de dano, e quem solta gente e o tique.
	/// </summary>
	private bool NegarAMorteDoBoo(ServerPlayer npc)
	{
		if (!EhOBooDaSaga(npc) || npc.Papel!.Estagio >= SagaDoBuu.DegrauKid) return false;
		npc.Combate.Curar();
		if (npc.Papel.Estagio == SagaDoBuu.DegrauGordo)
		{
			if (ChefeDaCadeia(npc) is { } ec) ec.SegundosNoDegrau = SagaDoBuu.SegundosAteADivisao;
		}
		else _booViraKid.Add(npc.Id);
		GD.Print($"[server] SAGA DO BOO: o ki que mataria {npc.Name} no degrau {npc.Papel.Estagio + 1} virou a etapa seguinte");
		return true;
	}
}
