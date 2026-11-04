using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO MAJIN BOO (`--buuteste`, 2026-11-01, o pedido 218). O jogador entra pela porta (e jogador de verdade: o rancor do Fat Boo so
/// conta gente) e a bancada anda a saga do Boo pelas portas de producao -- o `NascerChefeDaSaga`, o `TickDoBoo`, o `TickDoMajin`, a
/// `TickDaFugaDoKid`, o `Morrer()` e o `MonitorarElos`:
///
///   A. O DADO: o molde com os tres degraus (30, 45 e 55 bilhoes), Majin e Corrupted Majin, sem problema na validacao -- e (o pedido 229)
///      o corpo de cada um no dado, 1 = Fat, 2 = Super, 3 = Kid;
///   L. (o pedido 229) O CORPO PELO DADO: um Boo que NAO e de saga (a sequencia da lembranca da mente) veste a folha de cada degrau;
///   B. O FAT BOO: o corpo do Fat Boo do jogador (a forma 1 da saga), golpe letal, NEUTRO -- sem presa com o jogador ao lado, e com ele assim
///      que o jogador bate;
///   C. A DIVISAO: um segundo antes do relogio, gordo; no relogio, SUPER BOO -- o corpo da forma 3, 45 bilhoes, golpe nao-letal, a cena do
///      Super Majin e a frase;
///   D. O SUPER BOO ABSORVE: o jogador derrubado vai pro interior dele, e ele fica com a roupa e as tecnicas do jogador;
///   E. UMA IMAGEM SO: o segundo derrubado (um Kai) cai no mesmo interior, com a MESMA imagem -- e o Kai nao sobe a saga do Corrupted Majin
///      jogador no chefe;
///   F. A IMAGEM CAI: os dois saem no mesmo tique, e o Super vira o KID -- o corpo da Forma Pura, 55 bilhoes, golpe letal, a cena e a frase;
///   G. A ESFERA ROSA E A FUGA: a esfera sobe (a ocupacao rosa), a Terra entra na destruicao quando ela bate, e o Kid salta pro Ceu com o
///      clarao da chegada;
///   H. A MORTE QUE NAO E O FIM: o ki que mataria o gordo o divide, o que mataria o super o vira Kid, e o Kid morre de verdade -- a saga fecha;
///   I. O REINICIO: o Kid que fugiu volta no Ceu;
///   J. (o pedido 227m) O TEDIO DO KID NO CEU: vinte minutos sem lutar e ele vira um vilao solto, sumido;
///   K. (o pedido 228) O KID SOLTO INVADE: a frase diz que ele se teleporta, o jornal conta a invasao (o chat de ninguem), a cena de chegada
///      so vai pra quem esta no planeta invadido, e o corpo que nasce la e o do Kid -- o mesmo em que ele venceu a saga.
/// </summary>
public partial class GameServer
{
	private const string ContaDaBancadaDoBoo = "bancada_boo_ag";
	private const int IdBaseDaBancadaDoBoo = 0x7177_0000;

	private bool _booRodando, _booJaRodou;
	private int _booOk, _booFalhou;

	private void AfirmarBoo(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _booOk++; GD.Print($"[buu]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _booFalhou++; GD.PrintErr($"[buu]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoBoo(int porta)
	{
		if (_booRodando || _booJaRodou) return;
		_booRodando = _booJaRodou = true;
		_booOk = _booFalhou = 0;
		GD.Print("[buu] ================ O MAJIN BOO: GORDO, SUPER E KID (dono, 2026-11-01, o pedido 218) ================");

		EstadoDasSagas guardado = _sagas;
		double ceuGuardado = _adiantoDoCeu;
		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		var forjados = new List<ServerPlayer>();
		ServerPlayer? pl = null;
		double bpGuardado = 0;
		ZoneKey zonaGuardada = default;
		Vec2 posGuardada = default;
		var terra = ZoneKey.Premade("Earth");
		EscutaDasSagas = [];
		EscutaDeCenasDoMajin = [];
		try
		{
			pl = mesa.EntraNoMundo(cli, ContaDaBancadaDoBoo, "Goku");
			AfirmarBoo("(montagem) o cliente entra pela porta e e jogador; ha cadeia de sagas, moldes, skills e catalogo de aparencia",
					   pl != null && EhJogador(pl) && _cadeia.Length > 0 && _moldes != null && _skills != null && _visual != null);
			if (pl == null || _moldes == null || _skills == null || _visual == null) return;
			bpGuardado = pl.Ficha.BP;
			zonaGuardada = pl.Zone;
			posGuardada = pl.Pos;

			ODadoDoBoo();
			OCorpoPeloDado(pl);
			(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec, ServerPlayer? boo) = NascerOBooDaBancada();
			if (boo == null) { AfirmarBoo("(montagem) o Boo nasceu pela cadeia", false); return; }

			OFatBooNeutro(pl, boo);
			if (!ADivisao(ec, boo)) return;
			ServerPlayer? kai = OSuperBooAbsorve(pl, boo, forjados);
			if (kai == null) return;
			if (!AImagemCai(pl, kai, ec, boo)) return;
			AEsferaEAFuga(ec, boo, terra);
			OReinicioNoCeu(elo, est, ec, boo);
			OTedioDoKid(elo, est, ec);
			OKidSoltoInvade(pl, elo, est, ec);
			AMorteQueNaoEOFim(pl);
		}
		catch (Exception e)
		{
			AfirmarBoo("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			EscutaDasSagas = null;
			EscutaDeCenasDoMajin = null;
			_fugaDoKid = null;
			_booViraKid.Clear();
			foreach (int id in _dentroDoMajin.Keys.ToList()) CuspirTodosDoMajin(id, "");
			foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
			foreach (ServerPlayer f in forjados)
				if (_players.ContainsKey(f.Id)) { _players.Remove(f.Id); ZoneList(f.Zone.Hash).Remove(f); }
			if (ZonaCondenada(terra)) RessuscitarPlaneta(terra);
			if (pl != null)
			{
				pl.Ficha.BP = bpGuardado;
				if (pl.Ficha.KO) pl.Combate.Curar();
				if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			}
			_adiantoDoCeu = ceuGuardado;
			_sagas = guardado;
			_sagasPorRetomar = false;
			SalvarSagas();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDoBoo + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch { }
			_booRodando = false;
			GD.Print($"[buu] ================ {_booOk} OK, {_booFalhou} FALHA(S) ================");
		}
	}

	/// <summary>A cadeia do zero, com o elo do Boo em curso e o Boo nascendo pela porta de producao, na Terra.</summary>
	private (EloDaSaga Elo, EstadoDoElo Est, EstadoDoChefe Ec, ServerPlayer? Boo) NascerOBooDaBancada()
	{
		Zerar();
		EloDaSaga elo = Array.Find(_cadeia, e => e.Id == "boo")!;
		EstadoDoElo est = Elo("boo");
		est.Disparou = true;
		var ec = new EstadoDoChefe
		{
			Molde = SagaDoBuu.Molde, Fase = FaseDoChefe.ACaminho, Bps = Sagas.Pinar(_moldes!.Get(SagaDoBuu.Molde)!, MediaDoServidor()),
		};
		est.Chefes.Add(ec);
		NascerChefeDaSaga(elo, est, ec);
		return (elo, est, ec, Corpo(ec));
	}

	private static bool Disse(string trecho) => EscutaDasSagas?.Exists(t => t.Contains(trecho, StringComparison.Ordinal)) == true;

	// =====================================================================
	// A. O DADO
	// =====================================================================
	private void ODadoDoBoo()
	{
		MoldeDeNpc? m = _moldes!.Get(SagaDoBuu.Molde);
		EloDaSaga? elo = Array.Find(_cadeia, e => e.Id == "boo");
		AfirmarBoo("A. **O DADO**: o Majin Boo e Majin, Corrupted Majin, com TRES degraus -- o Fat Boo com os 30 bilhoes de hoje, o Super Boo com 45 e o "
				   + "Kid Boo com 55 (*\"45b de bp\"*, *\"55b de bp\"*) --, nenhum subindo por dano, e a validacao dos moldes nao reclama; o elo e o da Terra",
				   m is { Tipo: TipoDeNpc.Chefe, Racas: ["Majin"], Classe: "Corrupted Majin" }
				   && m.Estagios is [{ Bp: 30e9, GatilhoMembro: < 0, CorpoDoBoo: 1 }, { Bp: 45e9, GatilhoMembro: < 0, CorpoDoBoo: 2 }, { Bp: 55e9, CorpoDoBoo: 3 }]
				   && m.Problemas().Count == 0 && elo is { Planeta: "Earth", Chefes: [{ Molde: SagaDoBuu.Molde }] },
				   m == null ? "sem molde" : $"{string.Join(" / ", m.Estagios.Select(e => $"{e.Bp:N0} (corpoDoBoo {e.CorpoDoBoo})"))}; problemas: {string.Join(" | ", m.Problemas())}");
	}

	// =====================================================================
	// L. O CORPO PELO DADO (2026-11-02, o pedido 229)
	// =====================================================================
	/// <summary>
	/// *"Padroniza sim, mas mantendo as caracteristicas de cada degrau (super buu n mata, ele absorve etc)"* e *"No caso do npcs.json faça 1=
	/// fat 2= super 3 = kid"*. O corpo de cada degrau do Boo e do DADO (o `"corpoDoBoo"`), como o do Freeza e o do Cell: um corpo do molde que
	/// NAO e de saga -- a sequencia da lembranca da mente (`ChamarChefe`: `NascerNpc`, o degrau, o `EntrarNoDegrau`) -- veste a folha do
	/// degrau, com a cor dela e sem roupa. Antes do pedido o desenho era da saga, e esse corpo ficava com o corpo generico do molde. O que cada
	/// degrau FAZ e cobrado onde sempre foi: o Fat letal e neutro (B), o Super nao-letal (C) e absorvendo (D), o Kid letal (F).
	/// </summary>
	private void OCorpoPeloDado(ServerPlayer pl)
	{
		int[] esperado = [SagaDoBuu.CorpoDoFatBoo, SagaDoBuu.CorpoDoSuperBoo, SagaDoBuu.CorpoDoKidBoo];
		var vistos = new List<string>();
		bool todos = true;
		for (int d = 0; d < esperado.Length; d++)
		{
			ServerPlayer? npc = NascerNpc(SagaDoBuu.Molde, pl.Zone, pl.Pos + new Vec2(6 * ZoneCollision.TileSize, 0), 0x7177_7700UL + (ulong)d);
			if (npc?.Papel == null) { todos = false; vistos.Add($"degrau {d + 1}: nao nasceu"); continue; }
			npc.Papel.Estagio = d;
			EntrarNoDegrau(npc, cura: 0, anunciar: false);
			bool certo = !npc.Papel.DaSaga && npc.Visual.Corpo == esperado[d] && npc.Visual.CorPele == null && npc.Visual.Roupa.Count == 0;
			todos &= certo;
			vistos.Add($"degrau {d + 1}: corpo {npc.Visual.Corpo} (o do dado {esperado[d]}), cor {(npc.Visual.CorPele == null ? "da folha" : "sorteada")}, "
					   + $"{npc.Visual.Roupa.Count} roupa(s), de saga {npc.Papel.DaSaga}");
			RemoverNpc(npc);
		}
		AfirmarBoo("L. (o pedido 229) **O CORPO DE CADA DEGRAU DO BOO E DO DADO** (*\"1= fat 2= super 3 = kid\"*): um Boo que NAO e de saga (a sequencia "
				   + "da lembranca da mente) veste a folha do degrau -- o Fat, o Super e o Kid --, com a cor dela e sem roupa",
				   todos, string.Join("; ", vistos));
	}

	// =====================================================================
	// B. O FAT BOO
	// =====================================================================
	private void OFatBooNeutro(ServerPlayer pl, ServerPlayer boo)
	{
		AfirmarBoo("B. **O FAT BOO NASCE NA TERRA**: o corpo do Fat Boo do Corrupted Majin jogador (a forma 1 da saga), os 30 bilhoes, golpe letal, a "
				   + "cor das folhas (sem tinta) e sem roupa por cima",
				   boo.Zone.Name == "Earth" && boo.Papel!.Estagio == SagaDoBuu.DegrauGordo && boo.Visual.Corpo == SagaDoBuu.CorpoDoFatBoo
				   && Math.Abs(boo.Ficha.BP - 30e9) < 1 && boo.Combate.Letal && boo.Visual.CorPele == null && boo.Visual.Roupa.Count == 0,
				   $"zona {boo.Zone.Name}, corpo {boo.Visual.Corpo}, BP {boo.Ficha.BP:N0}, letal {boo.Combate.Letal}, cor {boo.Visual.CorPele}, roupa {boo.Visual.Roupa.Count}");

		MoveToZone(pl.Id, boo.Zone, MapaDaZonaOuCatalogo(boo.Zone)?.PontoLivrePerto(boo.Pos + new Vec2(3 * ZoneCollision.TileSize, 0))
										 ?? boo.Pos + new Vec2(3 * ZoneCollision.TileSize, 0));
		ServerPlayer? semBater = PresaDoNpc(boo);
		MarcarAgressao(boo, pl);
		ServerPlayer? depois = PresaDoNpc(boo);
		AfirmarBoo("B. **NEUTRO** (*\"ele vai ser um npc neutro ... e so ataca quem o atacar\"*): com o jogador a 3 tiles ele nao tem presa nenhuma; o "
				   + "jogador bate nele e ele revida -- o jogador e a presa",
				   semBater == null && depois == pl,
				   $"sem bater: {semBater?.Name ?? "ninguem"}; depois: {depois?.Name ?? "ninguem"}");
		boo.RancorAte = 0;
	}

	// =====================================================================
	// C. A DIVISAO
	// =====================================================================
	private bool ADivisao(EstadoDoChefe ec, ServerPlayer boo)
	{
		boo.CenaSegundos = 0;   // a cena da chegada pra todos nao conta: o gordo se divide fora de cena
		ec.SegundosNoDegrau = SagaDoBuu.SegundosAteADivisao - 2;
		TickDoBoo();
		bool aindaGordo = boo.Papel!.Estagio == SagaDoBuu.DegrauGordo;
		TickDoBoo();
		bool cena = EscutaDeCenasDoMajin!.Contains((boo.Id, Cinematicas.CenaMajin.Forma3));
		AfirmarBoo("C. **A DIVISAO** (*\"apos um tempo o jogo vai falar q ele se dividiu em 2 ... assim nascendo o super buu\"*): um segundo antes do "
				   + "relogio ainda e o gordo; no relogio, o SUPER BOO -- o corpo da forma 3 do jogador, 45 bilhoes, golpe NAO-LETAL, a cena do Super Majin "
				   + "pra zona e a frase pro mundo",
				   aindaGordo && boo.Papel.Estagio == SagaDoBuu.DegrauSuper && ec.Degrau == SagaDoBuu.DegrauSuper && boo.Visual.Corpo == SagaDoBuu.CorpoDoSuperBoo
				   && Math.Abs(boo.Ficha.BP - 45e9) < 1 && !boo.Combate.Letal && cena && Disse("SE DIVIDIU EM DOIS"),
				   $"antes gordo {aindaGordo}; degrau {boo.Papel.Estagio}, corpo {boo.Visual.Corpo}, BP {boo.Ficha.BP:N0}, letal {boo.Combate.Letal}, cena {cena}");
		boo.CenaSegundos = 0;   // a cena de 25 s prende o corpo: a bancada a pula
		return boo.Papel.Estagio == SagaDoBuu.DegrauSuper;
	}

	// =====================================================================
	// D-E. O SUPER BOO ABSORVE, COM UMA IMAGEM SO
	// =====================================================================
	private ServerPlayer? OSuperBooAbsorve(ServerPlayer pl, ServerPlayer boo, List<ServerPlayer> forjados)
	{
		const string Galic = "/datum/skill/rank/GalicGun";
		pl.Livro.Dar(Galic);
		string roupa = _visual!.Roupas.First();
		pl.Visual.Roupa.Clear();
		pl.Visual.Roupa.Add(new Jandirus.Core.Appearance.PecaDeRoupa { Caminho = roupa });
		List<string> verbosDoGalic = _skills!.Get(Galic)?.Verbos.ToList() ?? [];

		pl.Pos = MapaDaZonaOuCatalogo(boo.Zone)?.PontoLivrePerto(boo.Pos + new Vec2(3 * ZoneCollision.TileSize, 0)) ?? pl.Pos;
		pl.Combate.Nocautear(120);
		TickDoBoo();
		IReadOnlyList<AbsorvidoPeloMajin> dentro = AbsorvidosPor(boo.Id);
		bool noInterior = InteriorDoMajin.Anfitriao(pl.Zone) == boo.Id;
		bool vestiu = boo.LookDoMajin?.Roupa.Exists(p => p.Caminho == roupa) == true;
		bool tecnicas = verbosDoGalic.Count > 0 && verbosDoGalic.All(v => boo.VerbosAbsorvidos?.Contains(v) == true);
		AfirmarBoo("D. **O SUPER BOO NOCAUTEIA E ABSORVE** (*\"ele luta pra te nocautear e absorver, assim pegando a roupa e as skills e técnicas do "
				   + "player absorvido\"*): o jogador derrubado a 3 tiles vai pro interior dele, e o Boo veste a roupa do jogador e ganha as tecnicas "
				   + "que nao tinha (o Galick Gun)",
				   dentro.Count == 1 && noInterior && vestiu && tecnicas,
				   $"dentro {dentro.Count}; interior {noInterior} ({pl.Zone.Name}); roupa {vestiu}; tecnicas {tecnicas} ({string.Join(",", verbosDoGalic)})");
		if (dentro.Count != 1) return null;

		var kai = new ServerPlayer
		{
			Id = IdBaseDaBancadaDoBoo + forjados.Count, Peer = null, Name = "bancada: um Kai", Race = "Kai", Genero = "Male", Idade = 25,
			Zone = boo.Zone, Pos = MapaDaZonaOuCatalogo(boo.Zone)?.PontoLivrePerto(boo.Pos + new Vec2(-5 * ZoneCollision.TileSize, 0)) ?? boo.Pos,
			Conta = "bancada_boo_kai", Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Kai", BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		kai.Ficha.Class = "Normal";
		PorNoMundo(kai);
		forjados.Add(kai);
		kai.Combate.Nocautear(120);
		TickDoBoo();
		dentro = AbsorvidosPor(boo.Id);
		int imagens = dentro.Select(r => r.Guardiao).Distinct().Count();
		int corposDeImagem = ZoneList(InteriorDoMajin.De(boo.Id).Hash).Count(c => c.PresoGuardado != 0);
		AfirmarBoo("E. **UMA IMAGEM SO** (*\"vai ter apenas 1 clone dele no interior\"*): o segundo derrubado cai no MESMO interior, com a MESMA imagem -- "
				   + "e so ha uma imagem la dentro",
				   dentro.Count == 2 && imagens == 1 && dentro[0].Guardiao != 0 && corposDeImagem == 1
				   && InteriorDoMajin.Anfitriao(kai.Zone) == boo.Id,
				   $"dentro {dentro.Count}; imagens nos registros {imagens}; corpos de imagem {corposDeImagem}");
		AfirmarBoo("E. **O CHEFE NAO ANDA A SAGA DO JOGADOR**: absorver um Kai sobe o Corrupted Majin jogador pra forma 1 -- o Boo da saga (que e da classe "
				   + "Corrupted Majin) continua sem forma de saga",
				   boo.Ficha.majin_saga_form == 0 && !boo.Ficha.majin_kai_absorbed, $"forma {boo.Ficha.majin_saga_form}");
		return kai;
	}

	// =====================================================================
	// F. A IMAGEM CAI
	// =====================================================================
	private bool AImagemCai(ServerPlayer pl, ServerPlayer kai, EstadoDoChefe ec, ServerPlayer boo)
	{
		int id = AbsorvidosPor(boo.Id)[0].Guardiao;
		if (_players.TryGetValue(id, out ServerPlayer? imagem)) imagem.Combate.Nocautear(60);
		TickDoMajin(AbsorcaoMajin.SegundosEntreOlhadas + 0.1);
		bool sairam = AbsorvidosPor(boo.Id).Count == 0 && pl.Zone.Hash == boo.Zone.Hash && kai.Zone.Hash == boo.Zone.Hash;
		TickDoBoo();
		bool cena = EscutaDeCenasDoMajin!.Contains((boo.Id, Cinematicas.CenaMajin.KidBoo));
		AfirmarBoo("F. **A IMAGEM CAI E TODOS SAEM JUNTOS** (*\"ao ser derrotado todos os jogadores sao expulsos ao mesmo tempo\"*), e o Super vira o KID "
				   + "BOO: o corpo da Forma Pura, 55 bilhoes, golpe letal, a cena da carne encolhendo e a frase",
				   sairam && boo.Papel!.Estagio == SagaDoBuu.DegrauKid && ec.Degrau == SagaDoBuu.DegrauKid && boo.Visual.Corpo == SagaDoBuu.CorpoDoKidBoo
				   && Math.Abs(boo.Ficha.BP - 55e9) < 1 && boo.Combate.Letal && cena && Disse("KID BOO despertou") && _fugaDoKid != null,
				   $"sairam {sairam}; degrau {boo.Papel!.Estagio}, corpo {boo.Visual.Corpo}, BP {boo.Ficha.BP:N0}, letal {boo.Combate.Letal}, cena {cena}, "
				   + $"fuga {_fugaDoKid?.Fase}");
		return boo.Papel.Estagio == SagaDoBuu.DegrauKid && _fugaDoKid != null;
	}

	// =====================================================================
	// G. A ESFERA ROSA E A FUGA
	// =====================================================================
	private void AEsferaEAFuga(EstadoDoChefe ec, ServerPlayer kid, ZoneKey terra)
	{
		kid.CenaSegundos = 0;   // o fim da cena da forma
		TickDaFugaDoKid();
		bool esfera = kid.SupernovaDesde != 0 && kid.AtaqueDoUltimato == Jandirus.Core.Npc.AtaqueDoUltimato.EsferaRosa
					  && OcupacaoDe(kid, NowMs(), false) == Ocupacao.EsferaRosaDoUltimato;
		AfirmarBoo("G. **A ESFERA ROSA** (*\"no exato momento q ele virar kid buu ele vai carregar uma super esfera de energia rosa\"*): no fim da cena "
				   + "ela sobe -- a ocupacao que a tela desenha e a rosa, e o Kid fica parado e intocavel ate ela bater",
				   esfera && EmCena(kid), $"desde {kid.SupernovaDesde}, ataque {kid.AtaqueDoUltimato}, ocupacao {OcupacaoDe(kid, NowMs(), false)}");

		_fugaDoKid!.Desde -= (long)(Jandirus.Core.Npc.Supernova.SegundosAteAExplosao * 1000) + 50;
		TickDaFugaDoKid();
		bool condenada = ZonaCondenada(terra), aindaNaTerra = kid.Zone.Hash == terra.Hash;
		_fugaDoKid!.Desde -= Jandirus.Core.Skills.TransmissaoEmNiveis.AtrasoDaSaidaMs + 50;
		TickDaFugaDoKid();
		bool noCeu = kid.Zone.Name == SagaDoBuu.ZonaDoKid;
		bool clarao = _transmissoesEmCurso.TryGetValue(kid.Id, out TransmissaoEmCurso? t) && t.Fase == FaseDaTransmissao.Chegando;
		AfirmarBoo("G. **A TERRA E O CEU** (*\"apos andar uns tiles e explodir a terra vai começar a entrar no modo dos 5 minutos de destruição, e logo em "
				   + "seguida kid buu vai sumir da terra no mesmo efeito do instant transmission e ir pro planeta heaven\"*): quando a esfera bate, a Terra "
				   + "entra na destruicao com ele ainda la; 0,2 s depois ele esta no Ceu, com o clarao da chegada do IT na fila, e fica marcado la",
				   condenada && aindaNaTerra && noCeu && clarao && ec.NoCeu && kid.SupernovaDesde == 0 && Disse("rumo ao Céu"),
				   $"condenada {condenada}, na Terra na explosao {aindaNaTerra}, no Ceu {noCeu} ({kid.Zone.Name}), clarao {clarao}, marcado {ec.NoCeu}");
	}

	// =====================================================================
	// I. O REINICIO NO CEU
	// =====================================================================
	private void OReinicioNoCeu(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec, ServerPlayer kid)
	{
		RemoverNpc(kid);
		ec.Fase = FaseDoChefe.ACaminho;
		ec.Corpo = 0;
		NascerChefeDaSaga(elo, est, ec, ComoChegou.Retomando);
		ServerPlayer? volta = Corpo(ec);
		AfirmarBoo("I. **O REINICIO**: o Kid que fugiu volta NO CEU, Kid (o corpo e os 55 bilhoes), e nao na Terra condenada",
				   volta != null && volta.Zone.Name == SagaDoBuu.ZonaDoKid && volta.Papel!.Estagio == SagaDoBuu.DegrauKid
				   && volta.Visual.Corpo == SagaDoBuu.CorpoDoKidBoo && Math.Abs(volta.Ficha.BP - 55e9) < 1,
				   volta == null ? "nao nasceu" : $"{volta.Zone.Name}, degrau {volta.Papel!.Estagio}, corpo {volta.Visual.Corpo}, BP {volta.Ficha.BP:N0}");
		if (volta != null) RemoverNpc(volta);
	}

	// =====================================================================
	// J. O TEDIO DO KID NO CEU (2026-11-02, o pedido 227m)
	// =====================================================================
	/// <summary>
	/// *"se o kid buu n lutar enquanto estiver no heaven por 20 minutos, ele vai sumir com o efeito do instant transmission e o jogo vai falar q
	/// ele ficou entediado e foi procurar algum lugar pra destruir e fazer o caos"*. O Kid renasce no Ceu (o reinicio da familia I), e o relogio
	/// anda: lutando, o prazo recomeca; vinte minutos sem lutar, ele some -- e vira um vilao solto, sumido. O contra-exemplo: o Kid que nao se
	/// entedia (o jogo de antes) continua no Ceu.
	/// </summary>
	private void OTedioDoKid(EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec)
	{
		long adiantoAntes = AdiantoDoRelogioDeTeste;
		FaseDoChefe faseAntes = ec.Fase;
		bool ceuAntes = ec.NoCeu;
		void Avancar(double minutos)
		{
			AdiantoDoRelogioDeTeste += (long)(minutos * 60_000);
			_proximaVigiaDoKid = 0;
			VigiarOTedioDoKid();
		}
		ServerPlayer? Nascer()
		{
			ec.Fase = FaseDoChefe.ACaminho;
			ec.Corpo = 0;
			ec.NoCeu = true;
			NascerChefeDaSaga(elo, est, ec, ComoChegou.Retomando);
			_kidSemLutarDesde = 0;
			_proximaVigiaDoKid = 0;
			VigiarOTedioDoKid();   // a primeira olhada marca o comeco do sossego
			return Corpo(ec);
		}
		try
		{
			// ---- o contra-exemplo primeiro: o Kid que nao se entedia (o jogo de antes) continua no Ceu ----
			KidNaoSeEntediaDeTeste = true;
			ServerPlayer? teimoso = Nascer();
			Avancar(ViloesSoltos.MinutosDeTedioDoKid + 1);
			bool ficou = teimoso != null && _players.ContainsKey(teimoso.Id) && teimoso.Zone.Name == SagaDoBuu.ZonaDoKid;
			KidNaoSeEntediaDeTeste = false;
			if (teimoso != null) RemoverNpc(teimoso);
			AfirmarBoo("   CONTRA-EXEMPLO (defeito injetado: o Kid nunca se entedia, o jogo de antes): 21 minutos sem lutar e ele continua no Ceu -- a regua de J reprovaria",
					   ficou, teimoso == null ? "nao nasceu" : teimoso.Zone.Name);

			// ---- lutando, o prazo recomeca ----
			ServerPlayer? kid = Nascer();
			if (kid == null) { AfirmarBoo("(montagem) o Kid renasceu no Ceu", false); return; }
			Avancar(15);
			kid.Combate.EntrarEmCombate();
			Avancar(0.02);   // a olhada que ve a briga
			kid.Combate.EmCombate = 0;
			kid.Combate.BrigandoComAlguem = 0;
			Avancar(ViloesSoltos.MinutosDeTedioDoKid - 1);   // 19 minutos depois da briga
			bool aindaLa = _players.ContainsKey(kid.Id) && kid.Zone.Name == SagaDoBuu.ZonaDoKid;

			// ---- vinte minutos sem lutar: ele some ----
			EscutaDasSagas?.Clear();
			Avancar(2);
			VilaoSolto? solto = _soltos.Find(v => v.Elo == elo.Id);
			AfirmarBoo("J. **O KID SE ENTEDIA NO CEU** (*\"se o kid buu n lutar enquanto estiver no heaven por 20 minutos, ele vai sumir com o efeito do "
					   + "instant transmission e o jogo vai falar q ele ficou entediado\"*): a briga recomeca o prazo (19 min depois dela, ainda la); 20 min "
					   + "sem lutar e ele some, o mundo ouve, e ele vira um vilao solto SUMIDO (dias depois aparece num planeta com povo), o elo fechado",
					   aindaLa && !_players.ContainsKey(kid.Id) && Disse("ficou entediado") && solto is { Sumido: true, Jeito: ViloesSoltos.Jeito.Invasor }
					   && solto.Degrau == SagaDoBuu.DegrauKid && ec.Fase == FaseDoChefe.Consumido && !ec.NoCeu,
					   $"ainda la aos 19 min {aindaLa}; corpo {(_players.ContainsKey(kid.Id) ? "de pe" : "fora")}; solto {(solto == null ? "nenhum" : $"sumido {solto.Sumido}, degrau {solto.Degrau}")}; "
					   + $"fase {ec.Fase}");
		}
		finally
		{
			KidNaoSeEntediaDeTeste = false;
			AdiantoDoRelogioDeTeste = adiantoAntes;
			_soltos.RemoveAll(v => v.Elo == elo.Id);
			SalvarOsSoltos();
			if (Corpo(ec) is { } resto) RemoverNpc(resto);
			// o elo volta como a familia I o deixou: a H nasce o Boo dela pela cadeia
			ec.Fase = faseAntes;
			ec.NoCeu = ceuAntes;
			ec.Corpo = 0;
		}
	}

	// =====================================================================
	// K. O KID SOLTO INVADE (2026-11-02, o pedido 228)
	// =====================================================================
	/// <summary>
	/// *"O majin buu q invade planetas deveria ter o sprite do kid buu [...] essa mensagem so deve aparecer pra quem ta no planeta invadido, o
	/// resto dos jogadores so devem saber da invasao do kidbuu pelo jornal da patrulha galactica"* -- e os adendos *"Freeza, cell e majin buu
	/// devem manter os corpos da exata forma q eles estavam quando venceram a saga"* e *"na mensagem do kid buu n fale transmissao
	/// instantânea"*. O Kid renasce no Ceu e se entedia (a porta da familia J); os dias sumido passam com o jogador longe e ele volta a atacar
	/// um planeta com povo (o tique dos soltos); o corpo nasce la com o jogador LONGE (outra pessoa no planeta: a porta do corpo, direto), e
	/// de novo com o jogador la (o tique). O contra-exemplo de K4 e o mutante do conserto: o chefe de saga marcado depois do degrau.
	/// </summary>
	private void OKidSoltoInvade(ServerPlayer pl, EloDaSaga elo, EstadoDoElo est, EstadoDoChefe ec)
	{
		long adiantoAntes = AdiantoDoRelogioDeTeste;
		FaseDoChefe faseAntes = ec.Fase;
		bool ceuAntes = ec.NoCeu;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		List<(string Nome, string Planeta, string Frase, int Para)>? chegadasAntes = EscutaDeChegadas;
		ZoneKey prisao = ZoneKey.Premade(PatrulhaGalactica.ZonaDaPrisao);
		try
		{
			ec.Fase = FaseDoChefe.ACaminho;
			ec.Corpo = 0;
			ec.NoCeu = true;
			NascerChefeDaSaga(elo, est, ec, ComoChegou.Retomando);
			if (Corpo(ec) is not { } kid) { AfirmarBoo("(montagem) K: o Kid renasceu no Ceu", false); return; }
			int corpoDaVitoria = kid.Visual.Corpo;   // o do Kid que jogou a esfera na Terra: a forma em que ele venceu a saga
			string nome = kid.Name;

			// ---- ele se entedia (a porta da familia J) ----
			EscutaDasSagas?.Clear();
			KidEntediado(kid, elo, est, ec);
			string frase = string.Join(" | ", EscutaDasSagas ?? []);
			bool teleporta = Disse("se teleportou") && !Disse("Transmiss");
			if (_soltos.Find(v => v.Elo == elo.Id) is not { } solto) { AfirmarBoo("(montagem) K: o Kid entediado virou um vilao solto", false); return; }

			// ---- os dias sumido passam com o jogador longe (no planeta prisao, fora de todo sorteio): ele volta a atacar um planeta com povo ----
			MoveToZone(pl.Id, prisao, PontoDeNascimento(prisao));
			EscutaDasSagas?.Clear();
			SorteioDoPlanetaDeTeste = 0;   // o primeiro planeta com povo: um pre-feito, de mapa na memoria
			AdiantoDoRelogioDeTeste += solto.VoltaEm - NowMs() + 1000;
			TickDosSoltos();
			string onde = NomeDoPlaneta(solto.ZonaNome);
			bool noJornal = !solto.Sumido && NoticiasDoDiaDeTeste.Any(n => n.Tipo == TipoDeNoticia.VilaoAtacando
				&& n.Texto.Contains(nome, StringComparison.Ordinal) && n.Texto.Contains($"invade {onde}", StringComparison.Ordinal));
			bool noChat = Disse("invade") || Disse(onde);
			string ditos = string.Join(" | ", EscutaDasSagas ?? []);

			// ---- o corpo nasce com o jogador LONGE (outra pessoa la: a porta do corpo, direto): a cena de chegada nao vai pra ele ----
			EscutaDeChegadas = [];
			NascerOSolto(solto);
			int paraLonge = EscutaDeChegadas.Count == 1 ? EscutaDeChegadas[0].Para : -1;
			RecolherOSolto(solto);

			// ---- o jogador pisa no planeta: o corpo nasce pelo tique, e a cena vai pra ele ----
			EscutaDeChegadas = [];
			MoveToZone(pl.Id, solto.Zona, PontoDeNascimento(solto.Zona));
			TickDosSoltos();
			int paraLa = EscutaDeChegadas.Count == 1 ? EscutaDeChegadas[0].Para : -1;
			ServerPlayer? invasor = _players.GetValueOrDefault(solto.Corpo);

			AfirmarBoo("K1. (o pedido 228e) **A FRASE DO TEDIO DIZ QUE ELE SE TELEPORTA** (*\"n fale transmissao instantânea, diga 'majin buu se teleporta'\"*)",
					   teleporta, frase);
			AfirmarBoo("K2. (o pedido 228c) **A INVASAO DO KID SAI NO JORNAL DA PATRULHA, E NO CHAT DE NINGUEM**: passados os dias sumido, ele volta a "
					   + "atacar um planeta com povo",
					   noJornal && !noChat, $"em {solto.ZonaNome}; jornal {noJornal}, chat {noChat}; ditos: {ditos}");
			AfirmarBoo("K3. (o pedido 228b) **A CENA DE CHEGADA SO VAI PRA QUEM ESTA NO PLANETA INVADIDO**: o corpo nascido com o jogador longe nao a "
					   + "manda pra ele; com ele la, manda",
					   paraLonge == 0 && paraLa == 1, $"jogador longe: {paraLonge} cliente(s); no planeta: {paraLa}");
			AfirmarBoo("K4. (o pedido 228a/228d) **O BOO QUE INVADE E O KID BOO, NO CORPO EM QUE VENCEU A SAGA**: o desenho do degrau dele (o mesmo "
					   + "do Kid no Ceu), e nao o corpo generico do molde",
					   invasor != null && invasor.Papel?.Estagio == SagaDoBuu.DegrauKid && invasor.Visual.Corpo == SagaDoBuu.CorpoDoKidBoo
					   && invasor.Visual.Corpo == corpoDaVitoria,
					   invasor == null ? "nao nasceu"
					   : $"degrau {invasor.Papel?.Estagio}, corpo {invasor.Visual.Corpo} (o da vitoria {corpoDaVitoria}, o do Kid {SagaDoBuu.CorpoDoKidBoo})");
		}
		finally
		{
			EscutaDeChegadas = chegadasAntes;
			SorteioDoPlanetaDeTeste = null;
			foreach (VilaoSolto v in _soltos.Where(x => x.Elo == elo.Id).ToList()) RecolherOSolto(v);
			_soltos.RemoveAll(v => v.Elo == elo.Id);
			SalvarOsSoltos();
			AdiantoDoRelogioDeTeste = adiantoAntes;
			if (Corpo(ec) is { } resto) RemoverNpc(resto);
			ec.Fase = faseAntes;
			ec.NoCeu = ceuAntes;
			ec.Corpo = 0;
			MoveToZone(pl.Id, zonaAntes, posAntes);
		}
	}

	// =====================================================================
	// H. A MORTE QUE NAO E O FIM
	// =====================================================================
	private void AMorteQueNaoEOFim(ServerPlayer pl)
	{
		var terra = ZoneKey.Premade("Earth");
		if (ZonaCondenada(terra)) RessuscitarPlaneta(terra);
		(_, _, EstadoDoChefe ec, ServerPlayer? boo) = NascerOBooDaBancada();
		if (boo == null) { AfirmarBoo("(montagem) o Boo da familia H nasceu", false); return; }

		bool gordoNaoMorre = !boo.Combate.Morrer();
		TickDoBoo();
		bool virouSuper = boo.Papel!.Estagio == SagaDoBuu.DegrauSuper && !boo.Ficha.dead;
		boo.CenaSegundos = 0;
		bool superNaoMorre = !boo.Combate.Morrer();
		TickDoBoo();
		bool virouKid = boo.Papel.Estagio == SagaDoBuu.DegrauKid && !boo.Ficha.dead;
		AfirmarBoo("H. **SO O KI MATA, E NO GORDO E NO SUPER A MORTE VIRA A ETAPA SEGUINTE**: o ki que mataria o Fat Boo o divide, o que mataria o Super "
				   + "Boo o vira Kid -- a saga nao acaba no meio",
				   gordoNaoMorre && virouSuper && superNaoMorre && virouKid,
				   $"gordo vivo {gordoNaoMorre}, virou super {virouSuper}; super vivo {superNaoMorre}, virou kid {virouKid}");

		_fugaDoKid = null;
		boo.CenaSegundos = 0;
		bool kidMorre = boo.Combate.Morrer();
		MonitorarElos();
		AfirmarBoo("H. **O KID MORRE DE VERDADE, E A SAGA ACABA** (*\"se um player for la e derrotar kid buu a saga termina\"*): o elo fecha vencido, com a "
				   + "frase da vitoria",
				   kidMorre && Elo("boo").Fase == FaseDaSaga.Vencida && Disse("MAJIN BOO FOI DESTRUIDO"),
				   $"morreu {kidMorre}, elo {Elo("boo").Fase}");
	}
}
