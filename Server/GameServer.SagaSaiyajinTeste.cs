using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA SAGA SAIYAJIN (`--saiyajinteste`, 2026-11-01, o pedido 210). O jogador entra pela porta (e jogador de verdade pro marco) e a
/// bancada anda a saga inteira pelas portas de producao (o `TickDasSagas`, a virada do dia, a morte do chefe, o tique da IA):
///
///   A. O DADO: a cadeia na ordem do anime (a escolha do dono), os tres moldes (BP, raca, classe, cabelo, armadura) e o nivel de cada um --
///      o Raditz de iniciante, o Nappa mediano, o Vegeta um pouco melhor, e os tres bem abaixo do Freeza;
///   B. O MARCO NA TERRA: depois do Freeza no Vegeta, 500 de BP SO NA TERRA; antes dele, nem na Terra;
///   C. A SPACE POD: o Raditz chega na hora, ao lado da pod dele, com a cratera, a cena e a ficha certa;
///   D. A QUEDA DO RADITZ avisa os 7 dias, e o elo de Nappa e Vegeta comeca a contar -- com Namek esperando;
///   E. OS 7 DIAS: no sexto ninguem chegou; no setimo, duas pods longe uma da outra, e a cena do Vegeta espera a do Nappa acabar;
///   E2. (2026-11-01, o pedido 214) OS SEIS SAIBAMEN: brotam ao lado do Nappa (1.500 cada, sem pod nem cena), um luta de cada vez, cada
///       queda poe o proximo na luta, e a sexta poe o Nappa -- com o Vegeta ainda so observando;
///   F. O VEGETA OBSERVA: intocavel e sem presa, perto do Nappa; o Nappa cai e ele entra na luta NA HORA, com a fala dele;
///   G. O VEGETA CAI: a saga fecha e Namek, que esperava, comeca;
///   H. A AUDITORIA DO BOOT nao acha no disco o marco de quem esta no planeta (e acha o de quem nao precisa estar).
/// </summary>
public partial class GameServer
{
	private const string ContaDaBancadaSaiyajin = "bancada_saiyajin_ag";

	/// <summary>(2026-11-01, o adendo 210k) O corpo de pele branca do Saiyajin no catalogo (`visual.json`) -- o mesmo do Androide 17.</summary>
	private const string CorpoDePeleBranca = "NewPaleMale";

	/// <summary>O arquivo do corpo que este corpo veste (a folha da lista da raca dele, sem extensao).</summary>
	private string CorpoDesenhado(ServerPlayer c) =>
		_visual?.CorposDe(c.Race).Para(c.Genero) is { Count: > 0 } l ? System.IO.Path.GetFileNameWithoutExtension(l[Math.Clamp(c.Visual.Corpo, 0, l.Count - 1)]) : "";

	/// <summary>(2026-11-01, o adendo 210l) Este corpo esta de scouter LIGADO -- o bit que o pacote da aparencia le pra desenhar o scouter no rosto.</summary>
	private static bool DeScouter(ServerPlayer c) => (c.PoderesConcedidos & Jandirus.Net.Protocol.Poder.Scouter) != 0 && TemScouter(c);

	private bool _saiyajinRodando, _saiyajinJaRodou;
	private int _syOk, _syFalhou;

	private void AfirmarSy(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _syOk++; GD.Print($"[saiyajin]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _syFalhou++; GD.PrintErr($"[saiyajin]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDaSagaSaiyajin(int porta)
	{
		if (_saiyajinRodando || _saiyajinJaRodou) return;
		_saiyajinRodando = _saiyajinJaRodou = true;
		_syOk = _syFalhou = 0;
		GD.Print("[saiyajin] ================ A SAGA SAIYAJIN (dono, 2026-11-01, o pedido 210) ================");

		EstadoDasSagas guardado = _sagas;
		double ceuGuardado = _adiantoDoCeu;
		long cenaGuardada = UltimaCenaDePodMs;
		int obrasAntes = _noChao.Count;
		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		ServerPlayer? pl = null;
		double bpGuardado = 0;
		ZoneKey zonaGuardada = default;
		Vec2 posGuardada = default;
		try
		{
			pl = mesa.EntraNoMundo(cli, ContaDaBancadaSaiyajin, "Kakarotto");
			AfirmarSy("(montagem) o cliente entra pela porta e e jogador; ha cadeia de sagas, moldes e catalogo de aparencia",
					  pl != null && EhJogador(pl) && _cadeia.Length > 0 && _moldes != null && _visual != null);
			if (pl == null || _moldes == null || _visual == null) return;
			bpGuardado = pl.Ficha.BP;
			zonaGuardada = pl.Zone;
			posGuardada = pl.Pos;

			ODadoDaSagaSaiyajin();
			ServerPlayer? raditz = OMarcoNaTerra(pl);
			if (raditz != null && AQuedaDoRaditz(pl, raditz) && OsSeteDias(pl) is { } dupla)
			{
				// (2026-11-01, o pedido 214) os seis Saibamen do Nappa caem antes de o Nappa lutar
				if (OsSeisSaibamen(pl, dupla.Nappa, dupla.Vegeta))
				{
					ServerPlayer? vegeta = OVegetaObserva(pl, dupla.Nappa, dupla.Vegeta);
					if (vegeta != null) OVegetaCaiENamekVem(pl, vegeta);
				}
			}
			AAuditoriaDoBoot(pl);
		}
		catch (Exception e)
		{
			AfirmarSy("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			DiscoComAsBancadasDeTeste = false;
			EscutaDasSagas = null;
			EscutaDeChegadas = null;
			foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
			_observadoresDaSaga.Clear();
			_cenasDePodNaFila.Clear();
			_podsDosChefes.Clear();
			UltimaCenaDePodMs = cenaGuardada;
			// AS PODS QUE CAIRAM NA BANCADA SAEM DO CHAO (sao mobilia do mapa: nao iriam pro disco, mas ficariam na Terra ate o reinicio)
			if (_noChao.Count > obrasAntes)
			{
				var zonas = _noChao.Skip(obrasAntes).Select(o => o.Zona).Distinct().ToList();
				_noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
				foreach (ZoneKey z in zonas) { AplicarColisaoDasObras(z); MandarObras(z); }
			}
			if (pl != null)
			{
				pl.Ficha.BP = bpGuardado;
				if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			}
			_adiantoDoCeu = ceuGuardado;
			_sagas = guardado;
			_sagasPorRetomar = false;
			SalvarSagas();
			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaSaiyajin + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch { }
			_saiyajinRodando = false;
			GD.Print($"[saiyajin] ================ {_syOk} OK, {_syFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// A. O DADO
	// =====================================================================
	private void ODadoDaSagaSaiyajin()
	{
		string ordem = string.Join(" -> ", _cadeia.Select(e => e.Id));
		AfirmarSy("**A ORDEM DO ANIME** (a escolha do dono): o Freeza no Vegeta, o Raditz, Nappa e Vegeta, e so depois Namek, o Cell e o Boo -- e a "
				  + "cadeia nao tem contradicao",
				  ordem == "freeza_vegeta -> raditz -> saiyajins -> freeza_namek -> cell -> boo" && Sagas.Problemas(_cadeia, _moldes!).Count == 0,
				  $"{ordem}; problemas: {string.Join(" | ", Sagas.Problemas(_cadeia, _moldes!))}");

		EloDaSaga? r = Array.Find(_cadeia, e => e.Id == "raditz"), s = Array.Find(_cadeia, e => e.Id == "saiyajins");
		AfirmarSy("**O ELO DO RADITZ**: na Terra, 500 de BP, so de quem esta la, e ele chega na hora, de space pod (*\"quando um jogador na terra ... "
				  + "bater 500 de bp , raditz ira pousar na terra com sua spacepod\"*)",
				  r is { Planeta: "Earth", GatilhoBp: 500, GatilhoNoPlaneta: true, Chefes: [{ Molde: "raditz", DiasMin: 0, DiasMax: 0, ChegaDePod: true }] },
				  r == null ? "sem elo" : $"{r.Planeta} {r.GatilhoBp} {r.GatilhoNoPlaneta} {r.Chefes.Length} chefe(s)");
		// (2026-11-01, o pedido 214) e os seis Saibamen que o Nappa planta: o Nappa espera o sexto, cada um espera o anterior
		ChefeDaSaga[] cs = s?.Chefes ?? [];
		bool fila = cs.Length == 8
			&& cs[0] is { Molde: "nappa", DiasMin: 7, DiasMax: 7, ChegaDePod: true, ObservaAteCair: "saibaman_6", PlantadoPor: "" }
			&& cs[1] is { Molde: "vegeta_saiyajin", DiasMin: 7, DiasMax: 7, ChegaDePod: true, ObservaAteCair: "nappa", PlantadoPor: "" }
			&& Enumerable.Range(1, 6).All(k => cs[k + 1] is { DiasMin: 7, DiasMax: 7, ChegaDePod: false } c && c.Molde == $"saibaman_{k}"
										  && c.PlantadoPor == "nappa" && c.ObservaAteCair == (k == 1 ? "" : $"saibaman_{k - 1}")
										  && _moldes!.Get(c.Molde) is { Tipo: TipoDeNpc.Chefe, Estagios: [{ Bp: 1_500 }], Racas: ["Saibaman"] });
		AfirmarSy("**O ELO DE NAPPA E VEGETA**: na Terra, os dois em 7 dias, cada um na sua pod -- o Nappa planta seis Saibamen de 1.500 e so luta "
				  + "quando o sexto cai (um espera o outro), e o Vegeta so observa enquanto o Nappa estiver de pe",
				  s is { Planeta: "Earth" } && fila,
				  string.Join(" | ", cs.Select(c => $"{c.Molde} {c.DiasMin}-{c.DiasMax} pod {c.ChegaDePod} observa '{c.ObservaAteCair}' plantado '{c.PlantadoPor}'")));

		bool Ficha(string id, double bp, string classe, string cabelo, string armadura) =>
			_moldes!.Get(id) is { Tipo: TipoDeNpc.Chefe, Estagios: [{ } e], Racas: ["Saiyan"] } m && e.Bp == bp && m.Classe == classe && m.Cabelo == cabelo
			&& m.Corpo == CorpoDePeleBranca && m.Scouter   // (2026-11-01, os adendos 210k e 210l) sempre de pele branca, e de scouter
			&& m.Roupa.Length == 1 && m.Roupa[0] == armadura && _visual!.Peca(armadura) != null && _visual.Cabelos.Exists(c => c.Nome == cabelo)
			&& !m.EscadaAutomatica && !m.AscendePorDecisao;
		AfirmarSy("**AS TRES FICHAS**: Raditz 1.500 Low-Class com o cabelo e a armadura dele; Nappa 4.000 de classe media (Normal), careca, com a "
				  + "armadura que tem o nome dele; Vegeta 18.000 Elite com o cabelo dele e a armadura da saga Saiyajin -- as pecas existem no catalogo, "
				  + "nenhum sobe forma sozinho, os tres tem SEMPRE a pele branca (*\"Vegeta, nappa e raditz sao sempre cor de pele branca\"*) e usam "
				  + "o scouter (*\"Os 3 usam scouter tb equipado\"*)",
				  Ficha("raditz", 1_500, "Low-Class", "Raditz", "RaditzArmorTobiUchiha") && Ficha("nappa", 4_000, "Normal", "Bald", "Nappa Armor")
				  && Ficha("vegeta_saiyajin", 18_000, "Elite", "Vegeta", "Clothes_VegetaSaiyanSagaArmor"));

		MoldeDeNpc rd = _moldes!.Get("raditz")!, np = _moldes.Get("nappa")!, vg = _moldes.Get("vegeta_saiyajin")!, fz = _moldes.Get("freeza_vegeta")!;
		AfirmarSy("**O NIVEL DE CADA UM** (*\"bem de iniciantes\"*, *\"o nappa ... bem medianas, e o vegeta melhorzinhas, mas claro todos eles bem abaixo "
				  + "do freeza\"*): o nivel das skills e o orcamento de marcos sobem do Raditz pro Nappa e pro Vegeta, e o do Vegeta fica 20 pontos abaixo do "
				  + "Freeza",
				  rd.NivelDasSkills < np.NivelDasSkills && np.NivelDasSkills < vg.NivelDasSkills && vg.NivelDasSkills <= fz.NivelDasSkills - 20
				  && rd.MarcosBase < np.MarcosBase && np.MarcosBase < vg.MarcosBase && vg.MarcosBase < fz.MarcosBase
				  && rd.MarcosPorDecada < np.MarcosPorDecada && np.MarcosPorDecada < vg.MarcosPorDecada && vg.MarcosPorDecada < fz.MarcosPorDecada,
				  $"skills {rd.NivelDasSkills}/{np.NivelDasSkills}/{vg.NivelDasSkills} (Freeza {fz.NivelDasSkills}); marcos {rd.MarcosBase}+{rd.MarcosPorDecada} "
				  + $"/ {np.MarcosBase}+{np.MarcosPorDecada} / {vg.MarcosBase}+{vg.MarcosPorDecada} (Freeza {fz.MarcosBase}+{fz.MarcosPorDecada})");
	}

	// =====================================================================
	// B-C. O MARCO NA TERRA E A SPACE POD
	// =====================================================================
	/// <summary>A cadeia do zero, com o Freeza no Vegeta VENCIDO (ou nao).</summary>
	private void ZerarSy(bool freezaNoVegetaVencido)
	{
		Zerar();
		if (!freezaNoVegetaVencido) return;
		EstadoDoElo e = Elo("freeza_vegeta");
		e.Disparou = true;
		e.Fase = FaseDaSaga.Vencida;
	}

	private ServerPlayer? OMarcoNaTerra(ServerPlayer pl)
	{
		ZoneKey terra = ZoneKey.Premade("Earth"), vegeta = ZoneKey.Premade("Vegeta");

		ZerarSy(freezaNoVegetaVencido: false);
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		pl.Ficha.BP = 500;
		TickDasSagas();
		AfirmarSy("ANTES do Freeza no Vegeta cair, nem 500 de BP NA TERRA acordam o Raditz (*\"apos a saga do freeza no planeta vegeta\"*)",
				  !Elo("raditz").Disparou, $"{Elo("raditz").Fase}");

		ZerarSy(freezaNoVegetaVencido: true);
		MoveToZone(pl.Id, vegeta, PontoDeNascimento(vegeta));
		pl.Ficha.BP = 5_000;
		TickDasSagas();
		bool foraNaoAcorda = !Elo("raditz").Disparou && Elo("raditz").MarcoBatidoPor.Length == 0;
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		pl.Ficha.BP = 499;
		TickDasSagas();
		bool abaixoNaoAcorda = !Elo("raditz").Disparou;
		AfirmarSy("**O MARCO E DE QUEM ESTA NA TERRA** (*\"quando um jogador na terra ... bater 500 de bp\"*): com 5.000 de BP no Planeta Vegeta o Raditz "
				  + "nao vem (nem fica anotado), e na Terra com 499 tambem nao",
				  foraNaoAcorda && abaixoNaoAcorda, $"fora: {foraNaoAcorda}; 499: {abaixoNaoAcorda}");

		int podsAntes = PodsDeChefeDeTeste, craterasAntes = CraterasDePodDeTeste;
		EscutaDasSagas = [];
		EscutaDeChegadas = [];
		pl.Ficha.BP = 500;
		TickDasSagas();
		EstadoDoChefe? ec = Elo("raditz").Chefes.FirstOrDefault();
		ServerPlayer? raditz = ec == null ? null : Corpo(ec);
		AfirmarSy("...e COM 500 NA TERRA ele vem NA HORA: o elo dispara e o Raditz ja esta na Terra",
				  Elo("raditz").Disparou && ec is { Fase: FaseDoChefe.NoMundo } && raditz != null && raditz.Zone.Equals(terra),
				  $"fase {Elo("raditz").Fase}, chefe {ec?.Fase}; {string.Join(" | ", EscutaDasSagas)}");
		if (raditz == null) return null;

		const int T = ZoneCollision.TileSize;
		Obra? pod = _noChao.Skip(Math.Max(0, _noChao.Count - 3)).LastOrDefault(o => o.Tipo == "Spacepod" && o.DoMapa && o.Zona.Equals(terra));
		AfirmarSy("**ELE POUSA DE SPACE POD, COM A CRATERA** (*\"com sua spacepod fazendk a cratera etc\"*): uma pod no chao da Terra, a cratera dos "
				  + "pods aberta no ponto, e o Raditz a dois tiles dela",
				  pod != null && PodsDeChefeDeTeste == podsAntes + 1 && CraterasDePodDeTeste == craterasAntes + 1
				  && (raditz.Pos - new Vec2(pod.X, pod.Y)).Length <= 2 * T,
				  $"pods {PodsDeChefeDeTeste - podsAntes}; crateras {CraterasDePodDeTeste - craterasAntes}; distancia "
				  + $"{(pod == null ? -1 : (raditz.Pos - new Vec2(pod.X, pod.Y)).Length / T):0.#} tiles");
		AfirmarSy("**...E E O RADITZ DO DONO**: 1.500 de BP, Saiyajin Low-Class, o cabelo dele e a armadura dele no corpo, de pele branca e de scouter, golpe letal -- e a cena da "
				  + "chegada saiu na hora pra todo jogador",
				  Math.Abs(raditz.Ficha.BP - 1_500) < 1 && raditz.Race == "Saiyan" && raditz.Class == "Low-Class" && raditz.Visual.Cabelo == "Raditz"
				  && raditz.Visual.Roupa.Exists(p => p.Caminho.Contains("RaditzArmorTobiUchiha", StringComparison.Ordinal)) && raditz.Combate.Letal
				  && CorpoDesenhado(raditz) == CorpoDePeleBranca && DeScouter(raditz)
				  && EscutaDeChegadas.Any(c => c.Nome == raditz.Name),
				  $"BP {raditz.Ficha.BP:N0}; {raditz.Race}/{raditz.Class}; cabelo {raditz.Visual.Cabelo}; roupa "
				  + $"{string.Join(",", raditz.Visual.Roupa.Select(p => System.IO.Path.GetFileNameWithoutExtension(p.Caminho)))}; corpo {CorpoDesenhado(raditz)}; scouter {DeScouter(raditz)}; cenas {EscutaDeChegadas.Count}");
		return raditz;
	}

	/// <summary>
	/// Derruba um chefe pelo funil de producao -- o golpe que mata, como o `ResolverDesfecho` o resolve (o da `--raivateste`): o corpo cai, morre
	/// (deitado, o cadaver de sempre) e o funil da derrota roda; quem matou e o `UltimoAgressor`, e o tique das sagas ve a morte. (A d1 so
	/// escrevia o `dead`: o corpo do Nappa ficou DE PE na foto.)
	/// </summary>
	private void DerrubarSy(ServerPlayer chefe, ServerPlayer quem)
	{
		chefe.UltimoAgressor = quem.Id;
		chefe.RancorAte = NowMs() + 90_000;
		chefe.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
		_ = chefe.Combate.Morrer();
		AoPerderALuta(chefe, quem, morreu: true);
		TickDasSagas();
	}

	// =====================================================================
	// D. A QUEDA DO RADITZ
	// =====================================================================
	private bool AQuedaDoRaditz(ServerPlayer pl, ServerPlayer raditz)
	{
		EscutaDasSagas = [];
		DerrubarSy(raditz, pl);
		bool avisou = EscutaDasSagas.Any(t => t.Contains("Nappa e Vegeta", StringComparison.Ordinal) && t.Contains("em 7 dias", StringComparison.Ordinal));
		AfirmarSy("**QUANDO O RADITZ PERDE, ELE AVISA** (*\"quando o raditz perder ele avisara q em 7 dias(do jogo) nappa e vegeta irao pra terra\"*): o elo "
				  + "dele fecha, e o mundo ouve que Nappa e Vegeta chegam em 7 dias",
				  Elo("raditz").Fase == FaseDaSaga.Vencida && avisou, $"{Elo("raditz").Fase}; {string.Join(" | ", EscutaDasSagas)}");

		pl.Ficha.BP = 150_000;   // o marco de Namek (100 mil) batido: ele tem de ESPERAR a saga Saiyajin
		TickDasSagas();
		EstadoDoElo s = Elo("saiyajins");
		// (2026-11-01, o pedido 214) ...e os seis Saibamen que o Nappa planta vem com ele: oito a caminho
		AfirmarSy("...e o elo de NAPPA E VEGETA comeca a contar: os dois (e os seis Saibamen do Nappa) a caminho, 7 dias cada, com o anuncio dos dias",
				  s is { Disparou: true, Fase: FaseDaSaga.Contando } && s.Chefes.Count == 8 && s.Chefes.TrueForAll(c => c is { Fase: FaseDoChefe.ACaminho, DiasRestantes: 7 })
				  && EscutaDasSagas.Any(t => t.Contains("chegam em 7 dias", StringComparison.Ordinal)),
				  $"{s.Fase}; {string.Join(" | ", s.Chefes.Select(c => $"{c.Molde} {c.Fase} {c.DiasRestantes}"))}");
		AfirmarSy("**NAMEK ESPERA** (a ordem do anime, a escolha do dono): com 150 mil de BP o marco de Namek esta batido, e mesmo assim o Freeza nao parte "
				  + "enquanto Nappa e Vegeta nao cairem",
				  !Elo("freeza_namek").Disparou, $"{Elo("freeza_namek").Fase}");
		return s.Disparou;
	}

	// =====================================================================
	// E. OS 7 DIAS
	// =====================================================================
	private (ServerPlayer Nappa, ServerPlayer Vegeta)? OsSeteDias(ServerPlayer pl)
	{
		EstadoDoElo s = Elo("saiyajins");
		int podsAntes = PodsDeChefeDeTeste, craterasAntes = CraterasDePodDeTeste;
		for (int d = 0; d < 6; d++) AvancarUmDia();
		bool noSexto = s.Chefes.TrueForAll(c => c.Fase == FaseDoChefe.ACaminho);

		EscutaDeChegadas = [];
		UltimaCenaDePodMs = long.MinValue / 2;
		AvancarUmDia();
		EstadoDoChefe? en = s.Chefes.Find(c => c.Molde == "nappa"), ev = s.Chefes.Find(c => c.Molde == "vegeta_saiyajin");
		ServerPlayer? nappa = en == null ? null : Corpo(en), vegeta = ev == null ? null : Corpo(ev);
		AfirmarSy("**EM 7 DIAS** (do jogo): no sexto dia os dois ainda estao a caminho, e no setimo os dois estao na Terra",
				  noSexto && nappa != null && vegeta != null && nappa.Zone.Equals(ZoneKey.Premade("Earth")) && vegeta.Zone.Equals(nappa.Zone),
				  $"sexto: {noSexto}; {string.Join(" | ", s.Chefes.Select(c => $"{c.Molde} {c.Fase}"))}");
		if (nappa == null || vegeta == null) return null;

		const int T = ZoneCollision.TileSize;
		List<Obra> pods = [.. _noChao.Skip(Math.Max(0, _noChao.Count - 4)).Where(o => o.Tipo == "Spacepod" && o.DoMapa && o.Zona.Equals(nappa.Zone))];
		Obra? podN = pods.MinBy(o => (nappa.Pos - new Vec2(o.X, o.Y)).Length), podV = pods.MinBy(o => (vegeta.Pos - new Vec2(o.X, o.Y)).Length);
		AfirmarSy("**CADA UM NA SUA POD, LONGE UMA DA OUTRA**: duas pods novas e duas crateras, cada chefe a dois tiles da dele, e as pods a pelo menos "
				  + $"{PatrulhaGalactica.TilesEntrePods} tiles (duas crateras juntas bugam o desenho)",
				  PodsDeChefeDeTeste == podsAntes + 2 && CraterasDePodDeTeste == craterasAntes + 2 && podN != null && podV != null && podN != podV
				  && (nappa.Pos - new Vec2(podN.X, podN.Y)).Length <= 2 * T && (vegeta.Pos - new Vec2(podV.X, podV.Y)).Length <= 2 * T
				  && (new Vec2(podN.X, podN.Y) - new Vec2(podV.X, podV.Y)).Length >= PatrulhaGalactica.TilesEntrePods * T,
				  $"pods {PodsDeChefeDeTeste - podsAntes}, crateras {CraterasDePodDeTeste - craterasAntes}; entre as pods "
				  + $"{(podN == null || podV == null ? -1 : (new Vec2(podN.X, podN.Y) - new Vec2(podV.X, podV.Y)).Length / T):0.#} tiles");
		AfirmarSy("**AS DUAS FICHAS DO DONO**: Nappa 4.000, Saiyajin de classe media, careca e com a armadura dele; Vegeta 18.000, Elite, com o cabelo e a "
				  + "armadura da saga Saiyajin -- os dois de pele branca e de scouter",
				  Math.Abs(nappa.Ficha.BP - 4_000) < 1 && nappa.Class == "Normal" && nappa.Visual.Cabelo == "Bald"
				  && nappa.Visual.Roupa.Exists(p => p.Caminho.Contains("Nappa Armor", StringComparison.Ordinal))
				  && Math.Abs(vegeta.Ficha.BP - 18_000) < 1 && vegeta.Class == "Elite" && vegeta.Visual.Cabelo == "Vegeta"
				  && vegeta.Visual.Roupa.Exists(p => p.Caminho.Contains("Clothes_VegetaSaiyanSagaArmor", StringComparison.Ordinal))
				  && CorpoDesenhado(nappa) == CorpoDePeleBranca && CorpoDesenhado(vegeta) == CorpoDePeleBranca && DeScouter(nappa) && DeScouter(vegeta),
				  $"Nappa {nappa.Ficha.BP:N0} {nappa.Class} {nappa.Visual.Cabelo}; Vegeta {vegeta.Ficha.BP:N0} {vegeta.Class} {vegeta.Visual.Cabelo}; corpos "
				  + $"{CorpoDesenhado(nappa)}/{CorpoDesenhado(vegeta)}; scouter {DeScouter(nappa)}/{DeScouter(vegeta)}");

		bool soONappa = EscutaDeChegadas.Count == 1 && EscutaDeChegadas[0].Nome == nappa.Name;
		UltimaCenaDePodMs -= (long)(SegundosEntreCenasDePod * 1000);   // a cena do Nappa acabou
		TickDasSagas();
		AfirmarSy("**A CENA DO VEGETA ESPERA A DO NAPPA ACABAR** (uma cena nova tira a anterior da tela): na chegada so sai a do Nappa, e a do Vegeta sai "
				  + "depois dela",
				  soONappa && EscutaDeChegadas.Count == 2 && EscutaDeChegadas[1].Nome == vegeta.Name,
				  string.Join(" | ", EscutaDeChegadas.Select(c => c.Nome)));
		return (nappa, vegeta);
	}

	// =====================================================================
	// E2. (2026-11-01, o pedido 214) OS SEIS SAIBAMEN
	// =====================================================================
	/// <summary>
	/// O dono: *"Quando nappa e vegeta chegarem antes do nappa lutar ele ira colocar 6 saibamens, cada um com 1500 de poder de luta, um luta de
	/// cada vez, quando um for derrotado o outro entra, assim quando os 6 forem derrotados, o nappa comeca a lutar"*. Na chegada os seis brotam
	/// ao lado do Nappa (sem pod e sem cena); so o primeiro luta; cada queda poe o proximo na luta NA HORA; a sexta poe o Nappa -- e o Vegeta
	/// continua so observando.
	/// </summary>
	private bool OsSeisSaibamen(ServerPlayer pl, ServerPlayer nappa, ServerPlayer vegeta)
	{
		const int T = ZoneCollision.TileSize;
		EstadoDoElo s = Elo("saiyajins");
		ServerPlayer?[] sb = [.. Enumerable.Range(1, 6).Select(k => s.Chefes.Find(c => c.Molde == $"saibaman_{k}") is { } ec ? Corpo(ec) : null)];
		bool brotaram = sb.All(c => c != null && c.Zone.Equals(nappa.Zone) && (c.Pos - nappa.Pos).Length <= 6 * T && Math.Abs(c.Ficha.BP - 1_500) < 1
							   && c.Race == "Saibaman");
		bool contou = EscutaDasSagas?.Any(t => t.Contains("seis SAIBAMEN brotam", StringComparison.Ordinal)) == true;
		AfirmarSy("**O NAPPA PLANTA SEIS SAIBAMEN** (*\"antes do nappa lutar ele ira colocar 6 saibamens, cada um com 1500 de poder de luta\"*): na "
				  + "chegada os seis brotam ao lado dele, Saibamen de verdade com 1.500 de BP cada, e o mundo ouve -- sem pod e sem cena (as cenas "
				  + "continuam so as do Nappa e do Vegeta)",
				  brotaram && contou && EscutaDeChegadas?.Count == 2,
				  string.Join(" | ", sb.Select((c, i) => c == null ? $"{i + 1}: fora" : $"{i + 1}: {c.Race} {c.Ficha.BP:N0} a {(c.Pos - nappa.Pos).Length / T:0.#} tiles"))
				  + $"; ouvi a fala {contou}; cenas {EscutaDeChegadas?.Count}");
		if (!brotaram) return false;
		ServerPlayer[] seis = [.. sb.Select(c => c!)];

		AfirmarSy("**UM LUTA DE CADA VEZ, E O NAPPA ESPERA**: so o primeiro Saibaman luta; os outros cinco e o Nappa esperam a vez, intocaveis",
				  !ObservandoDeTeste(seis[0].Id) && !seis[0].Combate.NaoSeFere && seis.Skip(1).All(c => ObservandoDeTeste(c.Id) && c.Combate.NaoSeFere)
				  && ObservandoDeTeste(nappa.Id) && nappa.Combate.NaoSeFere,
				  string.Join(" | ", seis.Select((c, i) => $"{i + 1}: espera {ObservandoDeTeste(c.Id)}")) + $"; Nappa espera {ObservandoDeTeste(nappa.Id)}");

		EscutaDasSagas = [];
		var karmaAntes = pl.Karma;
		var trilha = new List<string>();
		bool emOrdem = true;
		for (int k = 0; k < 5; k++)
		{
			DerrubarSy(seis[k], pl);
			bool entrou = !ObservandoDeTeste(seis[k + 1].Id) && !seis[k + 1].Combate.NaoSeFere;
			bool restoEspera = seis.Skip(k + 2).All(c => ObservandoDeTeste(c.Id)) && ObservandoDeTeste(nappa.Id);
			trilha.Add($"{k + 1} caiu: o {k + 2} entrou {entrou}, o resto espera {restoEspera}");
			emOrdem &= entrou && restoEspera;
		}
		AfirmarSy("**QUANDO UM CAI, O OUTRO ENTRA** (*\"um luta de cada vez, quando um for derrotado o outro entra\"*): cada queda poe o proximo na "
				  + "luta na hora, com a fala, e o resto continua esperando",
				  emOrdem && EscutaDasSagas.Count(t => t.Contains("Saibaman pula pra luta", StringComparison.Ordinal)) == 5,
				  string.Join(" | ", trilha) + $"; ouvi: {string.Join(" | ", EscutaDasSagas)}");

		DerrubarSy(seis[5], pl);
		AfirmarSy("**OS SEIS CAEM E O NAPPA COMECA A LUTAR** (*\"quando os 6 forem derrotados, o nappa comeca a lutar\"*): ele para de esperar, "
				  + "volta a se ferir, diz a fala dele e caca o jogador -- e o Vegeta continua so observando",
				  !ObservandoDeTeste(nappa.Id) && !nappa.Combate.NaoSeFere && PresaDoNpc(nappa) == pl && ObservandoDeTeste(vegeta.Id)
				  && EscutaDasSagas.Any(t => t.Contains("quem vai brincar sou eu", StringComparison.Ordinal)),
				  $"Nappa espera {ObservandoDeTeste(nappa.Id)}, intocavel {nappa.Combate.NaoSeFere}, presa {PresaDoNpc(nappa)?.Name ?? "-"}; "
				  + $"Vegeta espera {ObservandoDeTeste(vegeta.Id)}; ouvi: {string.Join(" | ", EscutaDasSagas.TakeLast(2))}");
		AfirmarSy("**O SAIBAMAN E SEMENTE, NAO VILAO**: as seis quedas pagam so a reputacao pequena de cada um -- nenhuma aclamacao de heroi no "
				  + "chat e o karma de quem derrubou parado (o de chefe e do Nappa e do Vegeta)",
				  !EscutaDasSagas.Any(t => t.Contains("como HERÓI", StringComparison.Ordinal)) && pl.Karma == karmaAntes,
				  $"karma {karmaAntes} -> {pl.Karma}; aclamacoes {EscutaDasSagas.Count(t => t.Contains("como HERÓI", StringComparison.Ordinal))}");
		return !ObservandoDeTeste(nappa.Id);
	}

	// =====================================================================
	// F. O VEGETA OBSERVA
	// =====================================================================
	private ServerPlayer? OVegetaObserva(ServerPlayer pl, ServerPlayer nappa, ServerPlayer vegeta)
	{
		const int T = ZoneCollision.TileSize;
		AfirmarSy("**O VEGETA SO OBSERVA, E NAO SE FERE** (*\"Vegeta n luta e fica so observando o nappa lutar\"*): ele chega observando e intocavel -- e o "
				  + "Nappa luta e se fere como qualquer chefe",
				  ObservandoDeTeste(vegeta.Id) && vegeta.Combate.NaoSeFere && !nappa.Combate.NaoSeFere,
				  $"observando {ObservandoDeTeste(vegeta.Id)}; Vegeta intocavel {vegeta.Combate.NaoSeFere}; Nappa intocavel {nappa.Combate.NaoSeFere}");

		// O TIQUE DA IA DE VERDADE: o jogador a 3 tiles do Nappa, o Vegeta a 3 tiles do ponto de quem observa (do lado de la do Nappa)
		CravarPosicao(pl, nappa.Pos + new Vec2(3 * T, 0));
		nappa.Moving = false;
		Vec2 ponto = nappa.Pos + (nappa.Pos - pl.Pos).Normalized() * (TilesDeQuemObserva * T);
		vegeta.Pos = ponto + new Vec2(0, 3 * T);
		float maisPerto = float.MaxValue;
		for (int i = 0; i < 90; i++)
		{
			_zonasComGente.Add(pl.Zone.Hash);
			TicarUmCorpo(vegeta, 0.1, 0.1);
			maisPerto = MathF.Min(maisPerto, (vegeta.Pos - pl.Pos).Length);
		}
		float doPonto = (vegeta.Pos - ponto).Length;
		AfirmarSy("**...E NO TIQUE DA IA ELE NAO VAI ATRAS DO JOGADOR**: nove segundos com um jogador a tres tiles do Nappa, e o Vegeta vai pro lado de la "
				  + "do Nappa e fica olhando -- nunca chega perto de quem luta",
				  doPonto <= 2 * T && maisPerto > 4 * T,
				  $"do ponto {doPonto / T:0.#} tiles; o mais perto do jogador {maisPerto / T:0.#} tiles");

		EscutaDasSagas = [];
		DerrubarSy(nappa, pl);
		AfirmarSy("**O NAPPA PERDE E O VEGETA ENTRA NA LUTA NA HORA** (*\"quando nappa perder ai sim vegeta começa a lutar\"*): ele para de observar, "
				  + "volta a se ferir, diz a fala dele -- e caca o planeta como os outros chefes (a presa dele e o jogador)",
				  !ObservandoDeTeste(vegeta.Id) && !vegeta.Combate.NaoSeFere && PresaDoNpc(vegeta) == pl
				  && EscutaDasSagas.Any(t => t.Contains("Agora é a minha vez", StringComparison.Ordinal)),
				  $"observando {ObservandoDeTeste(vegeta.Id)}; intocavel {vegeta.Combate.NaoSeFere}; presa {PresaDoNpc(vegeta)?.Name ?? "-"}; "
				  + string.Join(" | ", EscutaDasSagas));
		return vegeta;
	}

	// =====================================================================
	// G. O VEGETA CAI, E NAMEK COMECA
	// =====================================================================
	private void OVegetaCaiENamekVem(ServerPlayer pl, ServerPlayer vegeta)
	{
		EscutaDasSagas = [];
		DerrubarSy(vegeta, pl);
		TickDasSagas();
		AfirmarSy("**O VEGETA CAI: A SAGA SAIYAJIN FECHA, E NAMEK COMECA** -- o Freeza parte pra Namek no tique seguinte, com o marco que ja estava batido",
				  Elo("saiyajins").Fase == FaseDaSaga.Vencida && Elo("freeza_namek") is { Disparou: true, Fase: FaseDaSaga.Contando }
				  && EscutaDasSagas.Any(t => t.Contains("NAPPA E VEGETA FORAM DERROTADOS", StringComparison.Ordinal)),
				  $"saiyajins {Elo("saiyajins").Fase}; namek {Elo("freeza_namek").Fase}; {string.Join(" | ", EscutaDasSagas)}");
	}

	// =====================================================================
	// H. A AUDITORIA DO BOOT
	// =====================================================================
	private void AAuditoriaDoBoot(ServerPlayer pl)
	{
		foreach (ServerPlayer c in ChefesNoMundo()) RemoverNpc(c);
		pl.Ficha.BP = 150_000;
		Persistir(pl);
		Zerar();
		DiscoComAsBancadasDeTeste = true;
		try { AuditarSagasNoBoot(); }
		finally { DiscoComAsBancadasDeTeste = false; }
		AfirmarSy("**A AUDITORIA DO BOOT NAO ACHA NO DISCO O MARCO DE QUEM ESTA NO PLANETA**: quem saiu do jogo nao esta na Terra -- o personagem de 150 mil "
				  + "no disco nao anota o do Raditz, e anota o de Namek (o controle: a auditoria rodou)",
				  Elo("raditz").MarcoBatidoPor.Length == 0 && Elo("freeza_namek").MarcoBatidoPor == pl.Name,
				  $"raditz '{Elo("raditz").MarcoBatidoPor}'; namek '{Elo("freeza_namek").MarcoBatidoPor}'");
	}
}
