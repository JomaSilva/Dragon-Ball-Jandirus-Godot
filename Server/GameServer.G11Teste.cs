using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// `--g11teste` -- A BANCADA DO LOTE G11: as skills que ja estavam na arvore sem efeito.
///
/// ============================ O QUE ELA AFIRMA, E COMO ============================
/// Uma prova de EFEITO NOMEADO por verb, pelo caminho de producao (`UsarHabilidade` -> `UsarTecnica`
/// -> o registro de tecnicas vivas do G11, o `Zanzoken` de verdade pro fio, o `AlternarAgarrao` de verdade pro braco
/// esticado, o `ComandoDeCargo` de verdade pra resposta a oferta). Em cada verb, as DUAS metades:
/// sem o requisito ele recusa E nao cobra; com o requisito ele acontece. Em cada buff com prazo:
/// acende, dura, APAGA e o stat volta ao valor de antes -- MEDIDO no campo, nunca lido do razao.
///
/// OS ATALHOS QUE ELA USA SAO DE RELOGIO, e so de relogio: `_sneakAteG11 = agora - 1`,
/// `ExpiraEm = agora - 1`, `ProximoMs = agora - 1`, o `_adiantoDoCeu` da Terra. Quem responde a
/// cada pergunta continua sendo a funcao de producao (o efetor, o `TickDosBuffs`, o `Ceu`).
///
/// OS CORPOS SAO FORJADOS (`Forjar` da bancada de projeteis): sem `Peer`, mas com conta -- e por isso
/// o lote pergunta `EhPessoa` onde o DM pergunta `.client`, e o efetor do lote nao usa o crivo
/// `EhJogador` (que exige tela). Um crivo que barrasse os corpos da bancada deixaria o lote sem prova.
/// =================================================================================
/// </summary>
public sealed partial class GameServer
{
	private int _g11Ok, _g11Falhou;

	private void AfirmarG11(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _g11Ok++; GD.Print($"[g11]   OK    {oque}"); return; }
		_g11Falhou++;
		GD.PrintErr($"[g11]   FALHA {oque}   {detalhe}");
	}

	private const string PathSneakG11T = "/datum/skill/Assassain/Sneak";
	private const string PathDebuffG11T = "/datum/skill/mind/Basic_Debuff_Mastery";
	private const string PathExpandG11T = "/datum/skill/expand";
	private const string PathMajinG11T = "/datum/skill/demon/Majin";
	private const string PathKaiKaiG11T = "/datum/skill/kai/Teleport";
	private const string PathDevilG11T = "/datum/skill/demon/Devil_Bringer";
	private const string PathShunkanG11T = "/datum/skill/shunkanido";
	private const string PathCatflexG11T = "/datum/skill/MartialSkill/Catflex";
	private const string PathStretchG11T = "/datum/skill/namek/Stretchy_Arms";
	private const string PathSelfDestructG11T = "/datum/skill/general/selfdestruct";
	private const string PathFreezeG11T = "/datum/skill/general/timefreeze";
	private const string PathObserveG11T = "/datum/skill/general/observe";
	private const string PathUnlockG11T = "/datum/skill/rank/Unlock_Potential";
	private const string PathGivePowerG11T = "/datum/skill/GivePower";
	private const string PathPerfectMetabG11T = "/datum/skill/Perfect_Metabolism";

	public void RodarBancadaG11()
	{
		_g11Ok = _g11Falhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		double adiantoAntes = _adiantoDoCeu;
		GD.Print("[g11] ================ AS SKILLS QUE JA ESTAVAM NA ARVORE SEM EFEITO (lote G11) ================");

		AfirmarG11("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			G11CatalogoEGate();
			G11SneakEGrilhao();
			G11ExpandEMajin();
			G11AstrosDoMakyo();
			G11SaltosDePlaneta();
			G11Teletransporte();
			G11CambalhotaEBracoEsticado();
			G11Autodestruicao();
			G11ZenkaiEmCombate();
			G11MetabolismoEMeditacao();
			G11CongelarEFio();
			G11ObservarEPotencial();
			G11DarPoderETimeStore();
		}
		finally
		{
			_adiantoDoCeu = adiantoAntes;
			LimparG11();
		}

		GD.Print($"[g11] ================ {_g11Ok} passaram, {_g11Falhou} falharam ================");
	}

	// =====================================================================
	// A INFRAESTRUTURA
	// =====================================================================
	/// <summary>
	/// Um corpo forjado que SABE as skills dadas -- pelo livro e pelo extrator (as flags chegam ao
	/// lutador aqui, e por isso este lote e o unico que liga o <c>efeitosDeSkill</c> do comum).
	/// </summary>
	private ServerPlayer ForjarG11(string nome, Vec2 onde, double bp, params string[] skills)
		=> ForjarComSkills(nome, onde, bp, skills, efeitosDeSkill: true);

	/// <summary>Sobe o nivel de uma skill pelo caminho do disco (`DoSave`), que e por onde o login o repoe.</summary>
	private static void SubirNivelG11(ServerPlayer pl, string path, int nivel)
	{
		NivelSave save = pl.Niveis.ParaSave();
		save.Skills[path] = [nivel, 0];
		pl.Niveis.DoSave(save);
	}

	/// <summary>Tira da bancada tudo que ela pos no mundo: buffs, estado do lote, paralisias e os corpos.</summary>
	private void LimparG11()
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (pl.Id < IdBaseDeProjetil) continue;
			DerrubarBuffs(pl);
			EsquecerG11(pl.Id);
			EsquecerParalisia(pl.Id);
			_prontoG3.Remove(pl.Id);
			_debuffPronto.Remove(pl.Id);
			if (pl.AgarrandoId != 0) Soltar(pl, MotivoDaSoltura.Tecla);
		}
		_ofertasDePotencialG11.Clear();
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 1) O CATALOGO E O GATE
	// =====================================================================
	private void G11CatalogoEGate()
	{
		GD.Print("[g11] -- 1) O CATALOGO CONHECE AS CATORZE, E O GATE CONTINUA FECHADO");

		var vivas = new HashSet<string>(Tecnicas.Vivas, StringComparer.OrdinalIgnoreCase);
		var faltam = IdsDoLote("G11").Where(id => !vivas.Contains(id)).ToList();
		AfirmarG11("os catorze verbs do lote estao VIVOS no catalogo", faltam.Count == 0, string.Join(" | ", faltam));
		AfirmarG11("...e um verb que este lote NAO portou (Stop, parar o tempo) continua NAO-PORTADO",
				   Tecnicas.Get("Stop")!.Modo == Modo.NaoPortada);
		AfirmarG11("...e nenhum dos catorze consta mais na fila de 'esperando sistema'",
				   IdsDoLote("G11").All(id => !CensoDeSkills.Esperando.ContainsKey(id)));

		Vec2 chao = CorredorLivre(12);
		ServerPlayer nu = Forjar("SemSkill", chao, bp: 5_000);
		double kiAntes = nu.Ficha.Ki;

		List<string> falas = ApertarEOuvir(nu, "Sneak");
		AfirmarG11("quem NAO comprou a skill ouve \"nao sabe\" no Sneak, e fica a vista",
				   Disse(falas, "nao sabe") && !_invisiveis.Contains(nu.Id), Ultimos(falas));
		falas = ApertarEOuvir(nu, "Kai_Kai:Namek");
		AfirmarG11("...e no Kai Kai (com argumento) tambem, sem sair do lugar",
				   Disse(falas, "nao sabe") && nu.Zone.Name == "Earth", Ultimos(falas));
		AfirmarG11("...e nenhuma das duas recusas cobrou Ki", Math.Abs(nu.Ficha.Ki - kiAntes) < 1e-9);

		LimparG11();
	}

	// =====================================================================
	// 2) SNEAK E GRILHAO -- buffs com prazo
	// =====================================================================
	private void G11SneakEGrilhao()
	{
		GD.Print("[g11] -- 2) SNEAK E GRILHAO: acendem, duram, APAGAM e o stat volta");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer pl = ForjarG11("Assassino", chao, 5_000, PathSneakG11T);
		SubirNivelG11(pl, PathSneakG11T, 2);   // o verb e do degrau 2 (`Assassain.dm:229-232`)
		AfirmarG11("Sneak e concedido pelo NIVEL 2 da skill (o degrau do disco)", SabeTecnica(pl, "Sneak"));

		// --- sem requisito: sem Ki recusa e nao cobra recarga ---
		pl.Ficha.Ki = 0;
		List<string> falas = ApertarEOuvir(pl, "Sneak");
		AfirmarG11("sem Ki o Sneak recusa dizendo o preco, e nao arma a recarga",
				   Disse(falas, "energia") && !_invisiveis.Contains(pl.Id) && !_prontoG3.ContainsKey(pl.Id),
				   Ultimos(falas));

		// --- com requisito: some, cobra `Ephysoff*BaseDrain*12`, dura `(10+Etechnique)` decimos ---
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		double custoEsperado = pl.Ficha.Ephysoff * pl.Ficha.BaseDrain() * 12;
		long duracaoEsperada = (long)((10 + pl.Ficha.Etechnique) * 100);
		long antesMs = NowMs();
		UsarHabilidade(pl, "Sneak");
		AfirmarG11("com Ki o Sneak SOME o corpo (invisivel + poder escondido)",
				   _invisiveis.Contains(pl.Id) && pl.Ficha.isconcealed);
		AfirmarG11("...cobrando `Ephysoff*BaseDrain*12`",
				   Math.Abs(pl.Ficha.MaxKi - pl.Ficha.Ki - custoEsperado) < 1e-6,
				   $"{pl.Ficha.MaxKi - pl.Ficha.Ki:0.###} vs {custoEsperado:0.###}");
		AfirmarG11("...por `(10 + Etechnique)` DECIMOS (a unidade do motor de efeitos do DM)",
				   _sneakAteG11.TryGetValue(pl.Id, out long ate) && Math.Abs(ate - antesMs - duracaoEsperada) <= 60,
				   $"{(_sneakAteG11.TryGetValue(pl.Id, out long a2) ? a2 - antesMs : -1)} vs {duracaoEsperada}");
		AfirmarG11("...e a recarga compartilhada dos golpes de assassino (basicCD, 6 s) ficou armada",
				   _prontoG3.TryGetValue(pl.Id, out long rec) && rec - antesMs >= 5900);

		double kiSneak = pl.Ficha.Ki;
		TickDasTecnicas();
		AfirmarG11("o Sneak NAO paga aluguel por segundo (o tique da Invisibility pula quem esta em Sneak)",
				   Math.Abs(pl.Ficha.Ki - kiSneak) < 1e-9 && _invisiveis.Contains(pl.Id));

		_prontoG3.Remove(pl.Id);
		falas = ApertarEOuvir(pl, "Sneak");
		AfirmarG11("ja invisivel, apertar de novo recusa (`!invisibility`) sem cobrar",
				   Disse(falas, "fora da vista") && Math.Abs(pl.Ficha.Ki - kiSneak) < 1e-9);

		// --- o prazo VENCE: o efetor devolve o corpo a vista ---
		_sneakAteG11[pl.Id] = NowMs() - 1;
		TickDoEfetorG11();
		AfirmarG11("vencido o prazo, o efetor APAGA o Sneak: corpo a vista, poder visivel de novo",
				   !_invisiveis.Contains(pl.Id) && !pl.Ficha.isconcealed && !_sneakAteG11.ContainsKey(pl.Id));

		// ---------------- GRILHAO ----------------
		ServerPlayer gr = ForjarG11("Grilhoeiro", chao, 50_000, PathDebuffG11T);
		SubirNivelG11(gr, PathDebuffG11T, 50);   // Shackle e do degrau 50 (`niveis.json`)
		gr.Ficha.MaxKi = Math.Max(gr.Ficha.MaxKi, 5_000_000);
		gr.Ficha.Ki = gr.Ficha.MaxKi;
		AfirmarG11("Shackle e concedido pelo nivel 50 de Basic Debuff Mastery", SabeTecnica(gr, "Shackle"));

		ServerPlayer alvo = Forjar("Grilhado", chao + new Vec2(3 * ZoneCollision.TileSize, 0), bp: 5_000);
		double tspeedAntes = alvo.Ficha.Tspeed;

		double kiGr = gr.Ficha.Ki;
		falas = ApertarEOuvir(gr, "Shackle");
		AfirmarG11("sem alvo marcado o Grilhao recusa com motivo e nao cobra",
				   Disse(falas, "alvo") && Math.Abs(gr.Ficha.Ki - kiGr) < 1e-9 && Math.Abs(alvo.Ficha.Tspeed - tspeedAntes) < 1e-12);

		gr.AlvoId = alvo.Id;
		double prazoEsperado = DmMath.Round(gr.Ficha.Ekiskill + gr.Ficha.kidebuffskill / 10, 1);
		long antesGr = NowMs();
		UsarHabilidade(gr, "Shackle");
		BuffAtivo? grilhao = BuffsDe(alvo.Id).Values.FirstOrDefault(b => b.Id.StartsWith("Shackle:", StringComparison.Ordinal));
		AfirmarG11("com alvo, o Grilhao BAIXA o Tspeed do alvo (medido no campo)",
				   alvo.Ficha.Tspeed < tspeedAntes - 1e-9, $"{tspeedAntes:0.###} -> {alvo.Ficha.Tspeed:0.###}");
		AfirmarG11("...exatamente pelo que o buff diz ter somado",
				   grilhao != null && Math.Abs(alvo.Ficha.Tspeed - (tspeedAntes + grilhao.Somas.GetValueOrDefault("Tspeed"))) < 1e-9);
		AfirmarG11("...com o prazo `round(Ekiskill + kidebuffskill/10)` segundos (um `sleep(10)` por unidade)",
				   grilhao != null && Math.Abs(grilhao.ExpiraEm - antesGr - Math.Max(prazoEsperado, 1) * 1000) <= 60,
				   $"{(grilhao != null ? grilhao.ExpiraEm - antesGr : -1)} vs {prazoEsperado * 1000}");
		AfirmarG11("...cobrando `900*BaseDrain` (o que se confere e o que se cobra)",
				   Math.Abs(kiGr - gr.Ficha.Ki - 900 * gr.Ficha.BaseDrain()) < 1e-6);

		// no DM cada grilhao e um `spawn` proprio: eles EMPILHAM
		_debuffPronto.Remove(gr.Id);
		double tspeedUm = alvo.Ficha.Tspeed;
		UsarHabilidade(gr, "Shackle");
		int quantos = BuffsDe(alvo.Id).Keys.Count(k => k.StartsWith("Shackle:", StringComparison.Ordinal));
		AfirmarG11("um segundo Grilhao EMPILHA (dois buffs, Tspeed mais baixo ainda)",
				   quantos == 2 && alvo.Ficha.Tspeed < tspeedUm - 1e-9, $"{quantos} buffs, {tspeedUm:0.###} -> {alvo.Ficha.Tspeed:0.###}");

		// os prazos VENCEM: o alicerce desfaz os dois e o stat volta ao de antes -- medido
		foreach (BuffAtivo b in BuffsDe(alvo.Id).Values) b.ExpiraEm = NowMs() - 1;
		TickDosBuffs();
		AfirmarG11("vencidos os prazos, o Tspeed do alvo VOLTA exatamente ao de antes",
				   Math.Abs(alvo.Ficha.Tspeed - tspeedAntes) < 1e-9 && BuffsDe(alvo.Id).Count == 0,
				   $"{alvo.Ficha.Tspeed:0.######} vs {tspeedAntes:0.######}");

		LimparG11();
	}

	// =====================================================================
	// 3) EXPAND BODY E MAJIN -- buffs sustentados que desfazem exato
	// =====================================================================
	private void G11ExpandEMajin()
	{
		GD.Print("[g11] -- 3) EXPAND BODY POR GRAU E A FORMA MAJIN: ligam, e desligam EXATO");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer pl = ForjarG11("Inflado", chao, 5_000, PathExpandG11T);
		Fighter f = pl.Ficha;
		f.MaxKi = Math.Max(f.MaxKi, 10_000);
		f.Ki = f.MaxKi;
		double offAntes = f.Tphysoff, defAntes = f.Tphysdef, spdAntes = f.Tspeed;

		List<string> falas = ApertarEOuvir(pl, "Expand_Body");
		AfirmarG11("sem grau, o Expand Body LISTA os graus e nao muda nada",
				   Disse(falas, "grau") && Math.Abs(f.Tphysoff - offAntes) < 1e-12 && !TemBuff(pl, "Expand_Body"));

		double kiAntes = f.Ki;
		double custo2 = 35 / Math.Max(f.Ekiskill, 0.01) * 2 + 5;
		UsarHabilidade(pl, "Expand_Body:2");
		AfirmarG11("2o grau: Tphysoff +1,25, Tphysdef +1,125 e Tspeed -(1 - 1/1,125) -- os numeros do `Loop()`",
				   Math.Abs(f.Tphysoff - (offAntes + 1.25)) < 1e-9
				   && Math.Abs(f.Tphysdef - (defAntes + 1.125)) < 1e-9
				   && Math.Abs(f.Tspeed - (spdAntes - (1 - 1 / 1.125))) < 1e-9,
				   $"off {f.Tphysoff - offAntes:0.####} def {f.Tphysdef - defAntes:0.####} spd {f.Tspeed - spdAntes:0.####}");
		AfirmarG11("...cobrando `(35/Ekiskill)*2 + 5` e marcando o grau",
				   Math.Abs(kiAntes - f.Ki - custo2) < 1e-6 && f.expandlevel == 2 && TemBuff(pl, "Expand_Body"));

		UsarHabilidade(pl, "Expand_Body:3");
		AfirmarG11("trocar pro 3o grau NAO empilha: e +1,50 sobre o de ANTES (o grau velho saiu exato)",
				   Math.Abs(f.Tphysoff - (offAntes + 1.5)) < 1e-9 && f.expandlevel == 3,
				   $"off {f.Tphysoff - offAntes:0.####}");

		UsarHabilidade(pl, "Expand_Body:0");
		AfirmarG11("grau 0 relaxa: os tres stats voltam EXATAMENTE ao de antes",
				   Math.Abs(f.Tphysoff - offAntes) < 1e-9 && Math.Abs(f.Tphysdef - defAntes) < 1e-9
				   && Math.Abs(f.Tspeed - spdAntes) < 1e-9 && !TemBuff(pl, "Expand_Body") && f.expandlevel == 0);

		falas = ApertarEOuvir(pl, "Expand_Body:4");
		AfirmarG11("o 4o grau e recusado dizendo que a Estrela Makyo nao existe aqui (nao foi inventada)",
				   Disse(falas, "Estrela") && !TemBuff(pl, "Expand_Body"));

		// o slot `sBUFF`: com um dos seis do G1 de pe, o Expand nao liga e nao cobra
		LigarBuff(pl, "Fighting_Power", "Fighting Power", new Dictionary<string, double> { ["Tphysoff"] = 5 });
		kiAntes = f.Ki;
		falas = ApertarEOuvir(pl, "Expand_Body:1");
		AfirmarG11("com Fighting Power de pe o Expand e recusado pelo SLOT, sem cobrar",
				   Disse(falas, "sustentando") && !TemBuff(pl, "Expand_Body") && Math.Abs(f.Ki - kiAntes) < 1e-9);
		DesligarBuff(pl, "Fighting_Power");

		// o `Loop()`: com Ki <= 10 o corpo relaxa sozinho (um segundo de efetor)
		UsarHabilidade(pl, "Expand_Body:1");
		AfirmarG11("(preparo) o 1o grau ligou", TemBuff(pl, "Expand_Body"));
		f.Ki = 5;
		for (int i = 0; i < 6; i++) TickDoEfetorG11();
		AfirmarG11("com Ki <= 10, o Loop de 1 s relaxa o corpo sozinho e o stat volta",
				   !TemBuff(pl, "Expand_Body") && Math.Abs(f.Tphysoff - offAntes) < 1e-9 && f.expandlevel == 0);

		// ---------------- MAJIN ----------------
		ServerPlayer bu = ForjarG11("Boo", chao, 10_000, PathMajinG11T);
		Fighter m = bu.Ficha;
		double bpaddAntes = m.BPadd, physoffModAntes = m.physoffMod, kiregenAntes = m.kiregenMod,
			   angerModAntes = m.angerMod, pcntAntes = m.MajinPcnt;
		double majinAddEsperado = m.BP * 1.2 * (m.MaxAnger / 100) / 10;

		UsarHabilidade(bu, "Majin");
		AfirmarG11("Majin liga: BPadd += BP*1,2*(MaxAnger/100)/10, physoffMod x1,3, kiregenMod +0,5, angerMod /1,2, MajinPcnt 1,2",
				   TemBuff(bu, "Majin")
				   && Math.Abs(m.BPadd - (bpaddAntes + majinAddEsperado)) < 1e-6
				   && Math.Abs(m.physoffMod - physoffModAntes * 1.3) < 1e-9
				   && Math.Abs(m.kiregenMod - (kiregenAntes + 0.5)) < 1e-9
				   && Math.Abs(m.angerMod - angerModAntes / 1.2) < 1e-9
				   && Math.Abs(m.MajinPcnt - (pcntAntes + 0.2)) < 1e-9,
				   $"BPadd {m.BPadd - bpaddAntes:0.##} vs {majinAddEsperado:0.##}");

		UsarHabilidade(bu, "Majin");
		AfirmarG11("apertar de novo dentro dos 2 s do `majining` nao desliga", TemBuff(bu, "Majin"));

		_prontoMajinG11.Remove(bu.Id);
		UsarHabilidade(bu, "Majin");
		AfirmarG11("desligar devolve os CINCO campos exatamente ao de antes (medido)",
				   !TemBuff(bu, "Majin")
				   && Math.Abs(m.BPadd - bpaddAntes) < 1e-9 && Math.Abs(m.physoffMod - physoffModAntes) < 1e-9
				   && Math.Abs(m.kiregenMod - kiregenAntes) < 1e-9 && Math.Abs(m.angerMod - angerModAntes) < 1e-9
				   && Math.Abs(m.MajinPcnt - pcntAntes) < 1e-9);

		LimparG11();
	}

	// =====================================================================
	// 4) OS ASTROS DO MAKYO -- de dia um vale, de noite o outro
	// =====================================================================
	private void G11AstrosDoMakyo()
	{
		GD.Print("[g11] -- 4) SOL, LUA E SOL SUPREMO: de dia um vale e de noite o outro (pelo relogio do Ceu)");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer pl = ForjarG11("Makyo", chao, 5_000, SkillSunG11);
		pl.Race = "Makyo";
		pl.Ficha.Race = "Makyo";
		Fighter f = pl.Ficha;
		double formsAntes = f.formsBuff;

		// MEIO-DIA (12h30 da Terra -> estagio 3): 2 + bonus(maestria 0 -> +1) = 3
		AjustarCeuDaTerra(hora: 12.5 / 24);
		TickDoEfetorG11();
		AfirmarG11("ao MEIO-DIA o Sol liga: formsBuff = 2 + 1 (o bonus base da maestria zero)",
				   TemBuff(pl, "Makyo_Sun") && Math.Abs(f.formsBuff - formsAntes * 3.0) < 1e-9,
				   $"formsBuff {f.formsBuff:0.###}, estagio {EstagioDoDiaG11(RelogioDaZona(pl.Zone), CeuDe(pl))}");
		AfirmarG11("...e a maestria do Sol sobe 0,02 por tique ao meio-dia",
				   Math.Abs(f.makyosunmastery - 0.02) < 1e-9, $"{f.makyosunmastery}");

		// AMANHECER (5h30 -> estagio 1): 1,2 + 1
		AjustarCeuDaTerra(hora: 5.5 / 24);
		TickDoEfetorG11();
		AfirmarG11("ao AMANHECER o mesmo buff vale 1,2 + 1 (re-ligado com o fator novo, sem empilhar)",
				   TemBuff(pl, "Makyo_Sun") && Math.Abs(f.formsBuff - formsAntes * 2.2) < 1e-9, $"{f.formsBuff:0.###}");
		AfirmarG11("...e fora do meio-dia a maestria NAO sobe", Math.Abs(f.makyosunmastery - 0.02) < 1e-9);

		// NOITE (22h30 -> estagio 7): o Sol se poe e o fator vai embora -- medido no campo
		AjustarCeuDaTerra(hora: 22.5 / 24);
		List<string> falas = Ouvir(() => TickDoEfetorG11());
		AfirmarG11("a NOITE o Sol se poe: buff apagado e formsBuff de volta ao de antes",
				   !TemBuff(pl, "Makyo_Sun") && Math.Abs(f.formsBuff - formsAntes) < 1e-9 && Disse(falas, "se poe"),
				   $"{f.formsBuff:0.###}");

		// ---------------- LUA ----------------
		pl.Livro.Esquecer(SkillSunG11);
		pl.Livro.Dar(SkillMoonG11);
		double tmagimonAntes = f.Tmagimon;
		TickDoEfetorG11();
		AfirmarG11("a NOITE a Lua liga: formsBuff = 1,2 + 1, e a maestria da Lua sobe",
				   TemBuff(pl, "Makyo_Moon") && Math.Abs(f.formsBuff - formsAntes * 2.2) < 1e-9
				   && Math.Abs(f.makyomoonmastery - 0.02) < 1e-9, $"{f.formsBuff:0.###}");
		AfirmarG11("...e o Tmagimon NAO muda: o `switch(currentMoonlight==5)` do DM nunca casa (bug mantido)",
				   Math.Abs(f.Tmagimon - tmagimonAntes) < 1e-12);

		AjustarCeuDaTerra(hora: 12.5 / 24);
		TickDoEfetorG11();
		AfirmarG11("de DIA a Lua se poe: buff apagado, formsBuff de volta",
				   !TemBuff(pl, "Makyo_Moon") && Math.Abs(f.formsBuff - formsAntes) < 1e-9);

		// ---------------- SOL SUPREMO (Above All), a metade solar ----------------
		pl.Livro.Esquecer(SkillMoonG11);
		pl.Livro.Dar(SkillAboveAllG11);
		pl.Livro.Dar(SkillSunG11);
		double tgainsAntes = f.tgains;
		f.Ki = f.MaxKi;
		f.overcharge = false;
		TickDoEfetorG11();
		AfirmarG11("ao meio-dia o Sol Supremo liga (2 + 1) e CALA o Sol comum (o guard `!locate(Above_All)`)",
				   TemBuff(pl, "Makyo_Above_All") && !TemBuff(pl, "Makyo_Sun") && Math.Abs(f.formsBuff - formsAntes * 3.0) < 1e-9,
				   $"{f.formsBuff:0.###}");
		AfirmarG11("...ao meio-dia `tgains *= 5` (buff proprio) e o Ki passivo sobe acima do maximo com `overcharge`",
				   TemBuff(pl, "Makyo_Gains") && Math.Abs(f.tgains - tgainsAntes * 5) < 1e-9
				   && f.Ki > f.MaxKi && f.overcharge,
				   $"tgains {f.tgains:0.##}, Ki {f.Ki:0.##}/{f.MaxKi:0.##}");

		AjustarCeuDaTerra(hora: 14.5 / 24);   // TARDE (estagio 4): 1,6 + 1 e os ganhos voltam
		TickDoEfetorG11();
		AfirmarG11("a TARDE os ganhos x5 saem (tgains de volta) e o fator vira 1,6 + 1",
				   !TemBuff(pl, "Makyo_Gains") && Math.Abs(f.tgains - tgainsAntes) < 1e-9
				   && Math.Abs(f.formsBuff - formsAntes * 2.6) < 1e-9, $"tgains {f.tgains:0.##} forms {f.formsBuff:0.###}");

		AjustarCeuDaTerra(hora: 22.5 / 24);
		TickDoEfetorG11();
		AfirmarG11("a NOITE o Sol Supremo se poe inteiro (a metade noturna e a Estrela Makyo, que nao existe)",
				   !TemBuff(pl, "Makyo_Above_All") && !TemBuff(pl, "Makyo_Gains") && Math.Abs(f.formsBuff - formsAntes) < 1e-9);

		LimparG11();
	}

	// =====================================================================
	// 5) OS SALTOS DE PLANETA -- Kai Kai e Devil Bringer, com carona
	// =====================================================================
	private void G11SaltosDePlaneta()
	{
		GD.Print("[g11] -- 5) KAI KAI E DEVIL BRINGER: o corpo e quem estava colado chegam; quem estava longe nao");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer kai = ForjarG11("Kaioshin", chao, 5_000, PathKaiKaiG11T);
		ServerPlayer colado = Forjar("Colado", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 1_000);
		ServerPlayer longe = Forjar("Longe", chao + new Vec2(6 * ZoneCollision.TileSize, 0), bp: 1_000);

		kai.Ficha.Ki = kai.Ficha.MaxKi / 2;
		List<string> falas = ApertarEOuvir(kai, "Kai_Kai:Namek");
		AfirmarG11("com Ki pela metade o Kai Kai recusa ('Ki cheio') e ninguem sai do lugar",
				   Disse(falas, "Ki cheio") && kai.Zone.Name == "Earth" && colado.Zone.Name == "Earth"
				   && Math.Abs(kai.Ficha.Ki - kai.Ficha.MaxKi / 2) < 1e-9, Ultimos(falas));

		// a lista vem DEPOIS da porta do Ki (`if(...Ki>=MaxKi...) { input }`, `kai.dm:115-117`)
		kai.Ficha.Ki = kai.Ficha.MaxKi;
		falas = ApertarEOuvir(kai, "Kai_Kai");
		AfirmarG11("sem destino ele LISTA (com o Ceu, que o Kai Kai alcanca) e nao viaja nem cobra",
				   Disse(falas, "Heaven") && kai.Zone.Name == "Earth" && Math.Abs(kai.Ficha.Ki - kai.Ficha.MaxKi) < 1e-9,
				   Ultimos(falas));

		UsarHabilidade(kai, "Kai_Kai:Namek");
		AfirmarG11("com Ki cheio o Kaioshin CHEGA em Namek, sem uma gota de Ki",
				   kai.Zone.Name == "Namek" && kai.Ficha.Ki == 0, $"{kai.Zone.Name}, Ki {kai.Ficha.Ki}");
		AfirmarG11("...quem estava COLADO chegou junto", colado.Zone.Name == "Namek", colado.Zone.Name);
		AfirmarG11("...e quem estava a seis tiles FICOU na Terra", longe.Zone.Name == "Earth", longe.Zone.Name);

		_saltoPronto.Remove(kai.Id);
		kai.Ficha.Ki = kai.Ficha.MaxKi;
		UsarHabilidade(kai, "Kai_Kai:Heaven");
		AfirmarG11("o Kai Kai alcanca o Ceu", kai.Zone.Name == "Heaven", kai.Zone.Name);

		ServerPlayer demo = ForjarG11("Demonio", CorredorLivre(12), 5_000, PathDevilG11T);
		falas = ApertarEOuvir(demo, "Devil_Bringer:Heaven");
		AfirmarG11("o Devil Bringer NAO entra no Ceu (recusa dizendo isso, Ki intacto)",
				   Disse(falas, "Ceu") && demo.Zone.Name == "Earth" && Math.Abs(demo.Ficha.Ki - demo.Ficha.MaxKi) < 1e-9,
				   Ultimos(falas));
		UsarHabilidade(demo, "Devil_Bringer:Hell");
		AfirmarG11("...mas alcanca o Inferno", demo.Zone.Name == "Hell" && demo.Ficha.Ki == 0, demo.Zone.Name);

		LimparG11();
	}

	// =====================================================================
	// 6) O TELETRANSPORTE POR ASSINATURA
	// =====================================================================
	private void G11Teletransporte()
	{
		GD.Print("[g11] -- 6) INSTANT TRANSMISSION: lista, concentra parado, chega ao lado (com carona) e a pericia cresce");

		Vec2 chao = CorredorLivre(30);
		ServerPlayer yd = ForjarG11("Yardrat", chao, 1_000, PathShunkanG11T);
		// A RACA ANTES DO EFEITO: `if(savant.Race=="Yardrat") savant.teleskill=70` (yardrat.dm:85-87) e
		// dado POR RACA (`porraca` do skills.json) desde 2026-09-02 -- ate entao a flag valia pra todo
		// aprendiz e esta bancada afirmava isso como verdade. O `Aplicar` e idempotente: reaplicar com a
		// raca certa e o que o login faz.
		yd.Race = yd.Ficha.Race = "Yardrat";
		EfeitosDeSkill.Aplicar(yd.Ficha, _skills!, yd.Livro.Aprendidas, yd.Livro.Escolhas);
		AfirmarG11("a pericia `teleskill=70` chega ao campo do lutador ao aprender -- SO PRO YARDRAT (`porraca`, yardrat.dm:85-87)",
				   Math.Abs(yd.Ficha.teleskill - 70) < 1e-9, $"{yd.Ficha.teleskill}");
		ServerPlayer ensinado = ForjarG11("Ensinado", chao + new Vec2(0, 3 * ZoneCollision.TileSize), 1_000, PathShunkanG11T);
		AfirmarG11("...e um HUMANO que aprende a mesma skill (ela e ensinavel) fica com a pericia de estreia, 1 -- nao vira Yardrat por dentro (antes virava)",
				   Math.Abs(ensinado.Ficha.teleskill - 1) < 1e-9, $"{ensinado.Ficha.teleskill} (raca {ensinado.Ficha.Race})");

		ServerPlayer farol = Forjar("Farol", chao + new Vec2(20 * ZoneCollision.TileSize, 0), bp: 5_000);
		farol.Conta = "g11_farol";   // assinatura propria (os forjados partilham a conta da bancada)
		ServerPlayer colado = Forjar("Colado", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 1_000);
		ServerPlayer longe = Forjar("Longe", chao + new Vec2(-6 * ZoneCollision.TileSize, 0), bp: 1_000);

		List<string> falas = ApertarEOuvir(yd, "Instant_Transmission");
		AfirmarG11("sem argumento ele LISTA: o desconhecido aparece pela ASSINATURA, com a razao de poder",
				   Disse(falas, farol.Assinatura) && Disse(falas, "x o seu poder") && !Disse(falas, "Farol:"),
				   Ultimos(falas));

		UsarHabilidade(yd, $"Instant_Transmission:{farol.Assinatura}");
		long esperaEsperada = (long)(Math.Max(600 / 70.0, 15) * 100);
		AfirmarG11("com a assinatura ele entra em concentracao por `max(600/teleskill, 15)` decimos",
				   _transmissaoG11.TryGetValue(yd.Id, out TransmissaoG11? t)
				   && Math.Abs(t.QuandoMs - NowMs() - esperaEsperada) <= 60, $"{esperaEsperada} ms");

		yd.Pos = yd.Pos + new Vec2(10, 0);   // "You moved!"
		falas = Ouvir(() => TickDoEfetorG11());
		AfirmarG11("quem se MEXE na concentracao perde o teletransporte ('You moved!')",
				   !_transmissaoG11.ContainsKey(yd.Id) && Disse(falas, "mexeu") && Vec2.Distance(yd.Pos, farol.Pos) > 10 * ZoneCollision.TileSize);
		yd.Pos = chao;

		UsarHabilidade(yd, $"Instant_Transmission:{farol.Assinatura}");
		_transmissaoG11[yd.Id].QuandoMs = NowMs() - 1;
		double kiAntesDaChegada = yd.Ficha.Ki;
		TickDoEfetorG11();
		AfirmarG11("vencida a concentracao, o Yardrat aparece ao LADO do farol",
				   Vec2.Distance(yd.Pos, farol.Pos) <= 1.5 * ZoneCollision.TileSize, $"{Vec2.Distance(yd.Pos, farol.Pos):0} px");
		AfirmarG11("...quem estava COLADO chegou junto, quem estava longe nao",
				   Vec2.Distance(colado.Pos, farol.Pos) <= 1.5 * ZoneCollision.TileSize
				   && Vec2.Distance(longe.Pos, farol.Pos) > 10 * ZoneCollision.TileSize);
		double kireq70 = Math.Min(yd.Ficha.MaxKi, yd.Ficha.MaxKi / (70.0 / 100));   // = MaxKi: ate teleskill 100 o custo e o tanque inteiro
		AfirmarG11("...e cobrou exatamente `kireq = min(MaxKi, MaxKi/(teleskill/100))` -- com teleskill 70 isso e o tanque INTEIRO, por conta propria e nao por um BaseDrain em cima (yardrat.dm:101)",
				   Math.Abs(kiAntesDaChegada - kireq70 - yd.Ficha.Ki) < 1e-6 && Math.Abs(kireq70 - yd.Ficha.MaxKi) < 1e-6,
				   $"ki {kiAntesDaChegada:0} -> {yd.Ficha.Ki:0} (kireq {kireq70:0})");
		AfirmarG11("...e a pericia cresceu +0,2 por tile (Yardrat): 70 -> 74",
				   Math.Abs(yd.Ficha.teleskill - 74) < 0.5, $"{yd.Ficha.teleskill:0.##}");

		// A SEGUNDA VIAGEM, com pericia 200: kireq = MaxKi/2 e SOBRA metade. O contra-exemplo e o DM, que
		// cobrava `kireq*BaseDrain` (`:146`) e zerava qualquer tanque acima de 140 -- consertado por decisao do dono.
		yd.Ficha.teleskill = 200;
		yd.Ficha.Ki = yd.Ficha.MaxKi;
		UsarHabilidade(yd, $"Instant_Transmission:{farol.Assinatura}");
		bool concentrou = _transmissaoG11.TryGetValue(yd.Id, out TransmissaoG11? t2);
		if (concentrou) { t2!.QuandoMs = NowMs() - 1; TickDoEfetorG11(); }
		AfirmarG11($"com teleskill 200 a viagem cobra kireq = MaxKi/2 e DEIXA metade do Ki (o DM cobrava kireq*BaseDrain = {yd.Ficha.MaxKi / 2 * yd.Ficha.BaseDrain():0} deste tanque de {yd.Ficha.MaxKi:0} -- e ZERA qualquer tanque acima de 140, yardrat.dm:146)",
				   concentrou && Math.Abs(yd.Ficha.Ki - yd.Ficha.MaxKi / 2) < 1e-6 && yd.Ficha.Ki > 0,
				   $"concentrou {concentrou}, ki {yd.Ficha.Ki:0} de {yd.Ficha.MaxKi:0}, BaseDrain {yd.Ficha.BaseDrain():0.##}");

		// conhecido pelo NOME
		yd.Social.Fotografar(farol.Assinatura, farol.Name, "Human", "Normal", "Male", 25, NowMs());
		yd.Social.SomarFamiliaridade(farol.Assinatura);
		falas = ApertarEOuvir(yd, "Instant_Transmission");
		AfirmarG11("conhecendo a pessoa, ela passa a aparecer pelo NOME", Disse(falas, "Farol:"), Ultimos(falas));

		LimparG11();
	}

	// =====================================================================
	// 7) CAMBALHOTA E BRACO ESTICADO -- o agarrao pelos dois lados
	// =====================================================================
	private void G11CambalhotaEBracoEsticado()
	{
		GD.Print("[g11] -- 7) FLIP (escapar) E STRETCHY ARMS (agarrar de longe)");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer grabber = Forjar("Agarrador", chao, bp: 5_000);
		ServerPlayer gato = ForjarG11("Gato", chao + new Vec2(ZoneCollision.TileSize, 0), 5_000, PathCatflexG11T);
		SubirNivelG11(gato, PathCatflexG11T, 2);
		AfirmarG11("Flip e concedido pelo nivel 2 de Catflex", SabeTecnica(gato, "Flip"));

		Prender(grabber, gato);
		AfirmarG11("(preparo) o gato esta preso", gato.AgarradoPorId == grabber.Id);

		gato.Ficha.Ki = 0;
		List<string> falas = ApertarEOuvir(gato, "Flip");
		AfirmarG11("sem Ki a cambalhota recusa e nao arma recarga",
				   Disse(falas, "energia") && gato.AgarradoPorId == grabber.Id && !_prontoG3.ContainsKey(gato.Id));

		gato.Ficha.Ki = gato.Ficha.MaxKi;
		double custo = gato.Ficha.Ephysoff * 12 * gato.Ficha.BaseDrain();
		gato.ContadorDaLuta = 0;
		UsarHabilidade(gato, "Flip");
		AfirmarG11("na primeira tentativa (contador 0) a chance e ZERO: continua preso, contador +5, Ki cobrado",
				   gato.AgarradoPorId == grabber.Id && Math.Abs(gato.ContadorDaLuta - 5) < 1e-9
				   && Math.Abs(gato.Ficha.MaxKi - gato.Ficha.Ki - custo) < 1e-6);

		// com poder de sobra a chance vira certeza: solta e MACHUCA quem segurava
		gato.Ficha.BP = 1e9;
		gato.Ficha.Tick(agoraMs: NowMs());
		gato.Ficha.Ki = gato.Ficha.MaxKi;
		_prontoG3.Remove(gato.Id);
		double vidaGrabber = grabber.Combate!.Corpo.Vida();
		UsarHabilidade(gato, "Flip");
		AfirmarG11("com chance de sobra o gato se SOLTA (os dois lados limpos) e quem segurava leva dano",
				   gato.AgarradoPorId == 0 && grabber.AgarrandoId == 0 && grabber.Combate.Corpo.Vida() < vidaGrabber,
				   $"vida {vidaGrabber:0.##} -> {grabber.Combate.Corpo.Vida():0.##}");

		// ---------------- STRETCHY ARMS ----------------
		// corredor PROPRIO: o Agarrador da cambalhota continua de pe no anterior, e estaria "na frente"
		Vec2 chaoNamek = CorredorLivre(12);
		ServerPlayer namek = ForjarG11("Namek", chaoNamek, 5_000, PathStretchG11T);
		AfirmarG11("a flag `can_stretch_arms=1` do extrator chegou no campo ao aprender", namek.Ficha.can_stretch_arms > 0);
		ServerPlayer alvo = Forjar("Distante", chaoNamek + new Vec2(5 * ZoneCollision.TileSize, 0), bp: 1_000);

		falas = ApertarEOuvir(namek, "agarrar");
		AfirmarG11("sem alvo marcado, o agarrao continua exigindo alguem NA FRENTE (recusa)",
				   namek.AgarrandoId == 0 && Disse(falas, "ao seu alcance"), Ultimos(falas));

		namek.AlvoId = alvo.Id;
		falas = ApertarEOuvir(namek, "agarrar");
		double forcaEsperada = Agarrao.Forca(namek.Ficha) / 3;
		AfirmarG11("com alvo marcado a 5 tiles, o braco ESTICA e agarra a distancia",
				   namek.AgarrandoId == alvo.Id && alvo.AgarradoPorId == namek.Id,
				   $"marcado={Marcado(namek)?.Name ?? "-"} pode={PodeMexerOCorpo(namek)} | {Ultimos(falas)}");
		AfirmarG11("...com UM TERCO da forca (`(Ephysoff*expressedBP)/3`)",
				   Math.Abs(alvo.ForcaDeQuemMeSegura - forcaEsperada) < 1e-6, $"{alvo.ForcaDeQuemMeSegura:0.##} vs {forcaEsperada:0.##}");
		TickDoAgarrao(0.1);
		AfirmarG11("...e o tique do agarrao, que recalcula a forca, MANTEM o terco",
				   namek.AgarrandoId == alvo.Id && Math.Abs(alvo.ForcaDeQuemMeSegura - forcaEsperada) < 1e-6,
				   $"{alvo.ForcaDeQuemMeSegura:0.##}");

		ServerPlayer humano = Forjar("Humano", CorredorLivre(12), bp: 5_000);
		ServerPlayer longe2 = Forjar("Distante2", humano.Pos + new Vec2(5 * ZoneCollision.TileSize, 0), bp: 1_000);
		humano.AlvoId = longe2.Id;
		UsarHabilidade(humano, "agarrar");
		AfirmarG11("sem a flag, o mesmo gesto NAO alcanca o alvo a 5 tiles", humano.AgarrandoId == 0);

		LimparG11();
	}

	// =====================================================================
	// 8) A AUTODESTRUICAO
	// =====================================================================
	private void G11Autodestruicao()
	{
		GD.Print("[g11] -- 8) SELF DESTRUCT: sem agarrao recusa; com agarrao carrega, mata o agarrado e o dano em si e o do DM");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer bomba = ForjarG11("Bomba", chao, 2_000, PathSelfDestructG11T);
		ServerPlayer refem = Forjar("Refem", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 1_000);

		double kiAntes = bomba.Ficha.Ki;
		List<string> falas = ApertarEOuvir(bomba, "Self_Destruct");
		AfirmarG11("sem ninguem agarrado ela recusa ('agarrando alguem'), nao carrega e nao cobra",
				   Disse(falas, "agarrando") && !_cargaG3.ContainsKey(bomba.Id) && Math.Abs(bomba.Ficha.Ki - kiAntes) < 1e-9);

		Prender(bomba, refem);
		UsarHabilidade(bomba, "Self_Destruct");
		AfirmarG11("com alguem agarrado o primeiro aperto ARMA a carga (contador 1)",
				   _cargaG3.TryGetValue(bomba.Id, out CargaG3? c) && c.Contador == 1);
		c!.ProximoMs = NowMs() - 1;
		PulsoG3();   // a carga anda no pulso de 10 Hz, como a Final Explosion
		AfirmarG11("...e a cada 2,5 s o contador sobe 5", c.Contador == 6, $"{c.Contador}");

		// o DANO EM SI: re-arma e detona com carga 1 -- power = (eBP*Ekioff)/(eBP_refem*Ekidef) * 5 * 1,
		// tirado de CADA membro (pequeno de proposito, pra nenhum membro bater no piso do nao-letal)
		CancelarAutodestruicaoG11(bomba);
		UsarHabilidade(bomba, "Self_Destruct");
		Fighter fb = bomba.Ficha, fr = refem.Ficha;
		double power = fb.expressedBP * fb.Ekioff / (fr.expressedBP * fr.Ekidef) * 5 * 1;
		// os membros que PENDEM de outro (`Dono`) levam a propagacao do funil do corpo por cima do golpe
		// direto -- regra do `Body.Ferir` do port, nao desta tecnica; a igualdade exata e nos raizes.
		var vidaAntes = bomba.Combate!.Corpo.Partes.Where(p => !p.Decepado && !p.Aninhado && string.IsNullOrEmpty(p.Dono)).ToDictionary(p => p.Nome, p => p.Vida);
		UsarHabilidade(bomba, "Self_Destruct");
		var desvios = vidaAntes.Select(kv =>
		{
			BodyPart? p = bomba.Combate.Corpo.Achar(kv.Key);
			return $"{kv.Key}:{(p == null ? "?" : (kv.Value - p.Vida).ToString("0.###"))}";
		}).ToList();
		bool danoBate = vidaAntes.All(kv =>
		{
			BodyPart? p = bomba.Combate.Corpo.Achar(kv.Key);
			return p != null && Math.Abs(kv.Value - p.Vida - power) < 1e-6;
		});
		AfirmarG11("detonar tira exatamente `power` de CADA membro de quem detona (o `usr.SpreadDamage(power)`)",
				   danoBate && !_cargaG3.ContainsKey(bomba.Id), $"power {power:0.###} | {string.Join(" ", desvios)}");
		AfirmarG11("...o Ki de quem detona ZERA e o agarrao e desfeito", fb.Ki == 0 && bomba.AgarrandoId == 0);
		AfirmarG11("...e quem detonou com carga <= 20 NAO morre (o sorteio de 75% so passa de 20)", !fb.dead);

		// A MORTE DO AGARRADO: um poder esmagador, carga 1, detonacao imediata
		ServerPlayer bomba2 = ForjarG11("Bomba2", CorredorLivre(12), 1_000_000, PathSelfDestructG11T);
		ServerPlayer refem2 = Forjar("Refem2", bomba2.Pos + new Vec2(ZoneCollision.TileSize, 0), bp: 1_000);
		ServerPlayer perto = Forjar("Perto", bomba2.Pos + new Vec2(2 * ZoneCollision.TileSize, 0), bp: 1_000);
		ServerPlayer fora = Forjar("Fora", bomba2.Pos + new Vec2(6 * ZoneCollision.TileSize, 0), bp: 1_000);
		double vidaFora = fora.Combate!.Corpo.Vida();
		Prender(bomba2, refem2);
		UsarHabilidade(bomba2, "Self_Destruct");
		UsarHabilidade(bomba2, "Self_Destruct");
		AfirmarG11("com poder esmagador o AGARRADO MORRE na explosao", refem2.Ficha.dead);
		AfirmarG11("...quem estava a 2 tiles tambem leva; quem estava a 6 NAO leva nada",
				   perto.Combate!.Corpo.Vida() < 100 && Math.Abs(fora.Combate.Corpo.Vida() - vidaFora) < 1e-9);
		AfirmarG11("...e quem detonou continua VIVO (o dano em si e nao-letal; so o sorteio mata)", !bomba2.Ficha.dead);

		LimparG11();
	}

	// =====================================================================
	// 9) O ZENKAI EM COMBATE -- Heran Power e Saiyan Power
	// =====================================================================
	private void G11ZenkaiEmCombate()
	{
		GD.Print("[g11] -- 9) HERAN POWER E SAIYAN POWER: em luta com alguem mais forte, o corpo ganha a cada 10*level tiques");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer heran = ForjarG11("Heran", chao, 1_000, SkillHeranPowerG11);
		ServerPlayer forte = Forjar("Forte", chao + new Vec2(2 * ZoneCollision.TileSize, 0), bp: 1_000_000);
		heran.Ficha.IsInFight = true;
		forte.Ficha.IsInFight = true;

		double bpAntes = heran.Ficha.BP, kiAntes = heran.Ficha.Ki;
		for (int i = 0; i < 12; i++) TickDoEfetorG11();
		AfirmarG11("Heran em luta contra alguem MAIS FORTE: depois de 10*level tiques o BP sobe (Attack_Gain(level+0,05))",
				   heran.Ficha.BP > bpAntes, $"{bpAntes:0.###} -> {heran.Ficha.BP:0.###}");
		AfirmarG11("...e ganha `10*BaseDrain` de Ki no surto",
				   Math.Abs(heran.Ficha.Ki - kiAntes - 10 * heran.Ficha.BaseDrain()) < 1e-6,
				   $"+{heran.Ficha.Ki - kiAntes:0.###} vs {10 * heran.Ficha.BaseDrain():0.###}");

		forte.Ficha.BP = 1;
		forte.Ficha.Tick(agoraMs: NowMs());
		double bpDepois = heran.Ficha.BP;
		for (int i = 0; i < 12; i++) TickDoEfetorG11();
		AfirmarG11("contra alguem MAIS FRACO nada acontece (o buffer nao sobe)", Math.Abs(heran.Ficha.BP - bpDepois) < 1e-12);

		heran.Ficha.IsInFight = false;
		forte.Ficha.BP = 1_000_000;
		forte.Ficha.Tick(agoraMs: NowMs());
		for (int i = 0; i < 12; i++) TickDoEfetorG11();
		AfirmarG11("fora de luta nada acontece", Math.Abs(heran.Ficha.BP - bpDepois) < 1e-12);

		ServerPlayer saiyan = ForjarG11("Saiyajin", chao, 1_000, SkillSaiyanPowerG11);
		saiyan.Ficha.IsInFight = true;
		forte.Ficha.IsInFight = true;
		double bpS = saiyan.Ficha.BP, kiS = saiyan.Ficha.Ki;
		for (int i = 0; i < 12; i++) TickDoEfetorG11();
		AfirmarG11("Saiyan Power: mesma funcao, `Attack_Gain(level+2)` e SEM o Ki extra",
				   saiyan.Ficha.BP > bpS && Math.Abs(saiyan.Ficha.Ki - kiS) < 1e-9, $"{bpS:0.###} -> {saiyan.Ficha.BP:0.###}");

		LimparG11();
	}

	// =====================================================================
	// 10) METABOLISMO PERFEITO E A MEDITACAO DOS GRAYS
	// =====================================================================
	private void G11MetabolismoEMeditacao()
	{
		GD.Print("[g11] -- 10) PERFECT METABOLISM (agua alimenta quem medita) E MEDITATE/BRAIN POWER (BP meditando)");

		ZoneCollision? mapa = _pjMapa;
		AfirmarG11("a Terra da bancada tem plano de agua", mapa is { TemAgua: true });

		Vec2? margem = null;
		if (mapa is { TemAgua: true })
			for (int y = 4; y < 250 && margem == null; y++)
				for (int x = 4; x < 250; x++)
				{
					if (mapa.BlockedCell(x, y) || mapa.EhAgua(x, y)) continue;
					bool temAgua = false;
					for (int dy = -1; dy <= 1 && !temAgua; dy++)
						for (int dx = -1; dx <= 1; dx++)
							if (mapa.EhAgua(x + dx, y + dy)) { temAgua = true; break; }
					if (temAgua) { margem = new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16); break; }
				}
		AfirmarG11("achei chao seco na MARGEM de um lago", margem != null);

		if (margem is { } m)
		{
			ServerPlayer namek = ForjarG11("Namek", m, 5_000, PathPerfectMetabG11T);
			AfirmarG11("a flag `partplant=1` do extrator chegou no campo ao aprender", namek.Ficha.partplant > 0);
			namek.Ficha.med = true;
			namek.Ficha.CurrentNutrition = 10;
			for (int i = 0; i < 300; i++) TickDoEfetorG11();   // 60 s: ~200 passadas de 0,3 s a 10%
			AfirmarG11("meditando na margem, a nutricao SOBE (prob 10% por passada de 0,3 s, +1% do tanque por tile de agua)",
					   namek.Ficha.CurrentNutrition > 10, $"{namek.Ficha.CurrentNutrition:0.###}");

			ServerPlayer comum = Forjar("Comum", m, bp: 5_000);
			comum.Ficha.med = true;
			comum.Ficha.CurrentNutrition = 10;
			for (int i = 0; i < 300; i++) TickDoEfetorG11();
			AfirmarG11("sem a skill, a agua nao alimenta ninguem", Math.Abs(comum.Ficha.CurrentNutrition - 10) < 1e-9);

			namek.Ficha.med = false;
			double nut = namek.Ficha.CurrentNutrition;
			for (int i = 0; i < 300; i++) TickDoEfetorG11();
			AfirmarG11("e sem meditar, tambem nao", Math.Abs(namek.Ficha.CurrentNutrition - nut) < 1e-9);
		}

		Vec2 chao = CorredorLivre(12);
		ServerPlayer gray = ForjarG11("Gray", chao, 1_000, SkillMeditatePowerG11);
		SubirNivelG11(gray, SkillMeditatePowerG11, 3);
		gray.Ficha.med = true;
		double bpAntes = gray.Ficha.BP;
		for (int i = 0; i < 200; i++) TickDoEfetorG11();
		AfirmarG11("Meditate Power nivel 3: meditando, o BP sobe em pulsos (prob 10% por tique)",
				   gray.Ficha.BP > bpAntes, $"{bpAntes:0.######} -> {gray.Ficha.BP:0.######}");

		gray.Ficha.med = false;
		double bpParado = gray.Ficha.BP;
		for (int i = 0; i < 200; i++) TickDoEfetorG11();
		AfirmarG11("...e sem meditar, nada", Math.Abs(gray.Ficha.BP - bpParado) < 1e-12);

		ServerPlayer hermano = ForjarG11("Hermano", chao, 1_000, SkillBrainPowerG11);
		hermano.Ficha.med = true;
		double bpH = hermano.Ficha.BP;
		for (int i = 0; i < 200; i++) TickDoEfetorG11();
		AfirmarG11("Brain Power: meditando, o BP sobe (prob 15% por tique, com o log4 da inteligencia)",
				   hermano.Ficha.BP > bpH, $"{bpH:0.######} -> {hermano.Ficha.BP:0.######}");

		LimparG11();
	}

	// =====================================================================
	// 11) CONGELAR O TEMPO E O FIO PSIQUICO -- a paralisia por outras portas
	// =====================================================================
	private void G11CongelarEFio()
	{
		GD.Print("[g11] -- 11) FREEZE (todos a vista) E PSYCHO THREAD (o fio aos pes): a paralisia do G5 por outras portas");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer alien = ForjarG11("Alien", chao, 5_000, PathFreezeG11T);
		ServerPlayer a1 = Forjar("Congelado1", chao + new Vec2(3 * ZoneCollision.TileSize, 0), bp: 1_000);
		ServerPlayer a2 = Forjar("Congelado2", chao + new Vec2(5 * ZoneCollision.TileSize, 0), bp: 1_000);
		ServerPlayer a3 = Forjar("Congelado3", chao + new Vec2(7 * ZoneCollision.TileSize, 0), bp: 1_000);

		alien.Ficha.Ki = alien.Ficha.MaxKi * 0.2;
		List<string> falas = ApertarEOuvir(alien, "Freeze");
		AfirmarG11("com um quinto do Ki o Freeze recusa e nao cobra nem congela",
				   Disse(falas, "pouco Ki") && Math.Abs(alien.Ficha.Ki - alien.Ficha.MaxKi * 0.2) < 1e-9
				   && !_paralisadoAte.ContainsKey(a1.Id));

		alien.Ficha.Ki = alien.Ficha.MaxKi;
		long antes = NowMs();
		UsarHabilidade(alien, "Freeze");
		long msEsperado1 = (long)(20 * alien.Ficha.Ekiskill / a1.Ficha.Ephysoff * 100);
		AfirmarG11("com Ki, os TRES a vista ficam com as pernas trancadas por `(20*Ekiskill)/Ephysoff` decimos",
				   _paralisadoAte.TryGetValue(a1.Id, out long p1) && _paralisadoAte.ContainsKey(a2.Id) && _paralisadoAte.ContainsKey(a3.Id)
				   && Math.Abs(p1 - antes - msEsperado1) <= 60, $"{msEsperado1} ms");
		AfirmarG11("...e o Ki caiu para METADE, uma vez so, com TRES congelados (o DM cobrava `Ki*=0.5` dentro do `for`, TimeStop.dm:14-16: 87,5% -- consertado por decisao do dono)",
				   Math.Abs(alien.Ficha.Ki - alien.Ficha.MaxKi * 0.5) < 1e-6, $"{alien.Ficha.Ki:0.##} de {alien.Ficha.MaxKi:0.##}");
		AfirmarG11("...e quem esta congelado quase nunca anda (a paralisia de producao)",
				   Enumerable.Range(0, 100).Count(_ => PodeMexerOCorpo(a1)) < 40);
		EsquecerParalisia(a1.Id);
		EsquecerParalisia(a2.Id);
		EsquecerParalisia(a3.Id);

		// ---------------- PSYCHO THREAD ----------------
		ServerPlayer heran = ForjarG11("Heran", CorredorLivre(12), 50_000, SkillPsychoThreadG11);
		heran.Ficha.MaxKi = Math.Max(heran.Ficha.MaxKi, 5_000_000);
		heran.Ficha.Ki = heran.Ficha.MaxKi;
		AfirmarG11("a flag `psythre=1` do extrator chegou no campo ao aprender (o fio nasce LIGADO)", heran.Ficha.psythre > 0);

		Vec2 pos = heran.Pos;
		int tirosAntes = ProjeteisDaZona(heran.Zone.Hash).Count;
		double kiAntes = heran.Ficha.Ki;
		Zanzoken(heran, pos + new Vec2(4 * ZoneCollision.TileSize, 0));   // o clique no chao de producao
		List<Projetil> tiros = ProjeteisDaZona(heran.Zone.Hash);
		Projetil? fio = tiros.Count > tirosAntes ? tiros[^1] : null;
		AfirmarG11("com o fio ligado, o duplo clique no chao ARMA um fio PARADO aos pes (e nao pisca)",
				   fio != null && fio.Paralisia && !fio.Deflectivel && fio.Rumo.LengthSquared < 1e-9
				   && Vec2.Distance(fio.Pos, pos) < 1 && Vec2.Distance(heran.Pos, pos) < 1,
				   $"{tiros.Count - tirosAntes} tiro(s)");
		AfirmarG11("...que vive 5 s (o `Burnout()` padrao) e cobra `100*BaseDrain` -- o mesmo numero que a porta confere",
				   fio != null && Math.Abs(fio.VidaRestante - 5) < 1e-9
				   && Math.Abs(kiAntes - heran.Ficha.Ki - 100 * heran.Ficha.BaseDrain()) < 1e-6);

		ServerPlayer pisou = Forjar("Pisou", pos + new Vec2(4, 0), bp: 1_000);
		for (int i = 0; i < 30 && !_paralisadoAte.ContainsKey(pisou.Id); i++) TickDosProjeteis(1.0 / 30);
		AfirmarG11("...e quem pisa no fio fica com as pernas trancadas", _paralisadoAte.ContainsKey(pisou.Id));
		AfirmarG11("...mas o proprio Heran nao (o dono do tiro nao pisa no proprio fio)", !_paralisadoAte.ContainsKey(heran.Id));
		EsquecerParalisia(pisou.Id);

		// COM 500x O DRENO-BASE (entre os 100x do custo e os 700x da porta velha): o fio ARMA e cobra 100x.
		// O contra-exemplo e o DM, que recusava aqui (`Ki >= 700*BaseDrain`, click.dm:5) -- consertado por decisao do dono.
		_debuffPronto.Remove(heran.Id);
		heran.Ficha.Ki = 500 * heran.Ficha.BaseDrain();
		int antesTiros = ProjeteisDaZona(heran.Zone.Hash).Count;
		double kiMeio = heran.Ficha.Ki;
		Zanzoken(heran, pos + new Vec2(4 * ZoneCollision.TileSize, 0));
		AfirmarG11("com 500x o dreno-base o fio ARMA e cobra 100x: a porta e o custo sao o MESMO numero (o DM recusava abaixo de 700x, click.dm:5)",
				   ProjeteisDaZona(heran.Zone.Hash).Count == antesTiros + 1 && Math.Abs(kiMeio - heran.Ficha.Ki - 100 * heran.Ficha.BaseDrain()) < 1e-6,
				   $"tiros +{ProjeteisDaZona(heran.Zone.Hash).Count - antesTiros}, ki {kiMeio:0} -> {heran.Ficha.Ki:0}");
		_debuffPronto.Remove(heran.Id);
		heran.Ficha.Ki = 50 * heran.Ficha.BaseDrain();
		antesTiros = ProjeteisDaZona(heran.Zone.Hash).Count;
		double kiPouco = heran.Ficha.Ki;
		falas = Ouvir(() => Zanzoken(heran, pos + new Vec2(4 * ZoneCollision.TileSize, 0)));
		AfirmarG11("abaixo dos 100*BaseDrain do custo o fio recusa dizendo o preco e nao cobra",
				   ProjeteisDaZona(heran.Zone.Hash).Count == antesTiros && Math.Abs(heran.Ficha.Ki - kiPouco) < 1e-9 && Disse(falas, "o fio pede"));

		UsarHabilidade(heran, "Psycho_Thread");
		AfirmarG11("o toggle DESLIGA o fio", heran.Ficha.psythre == 0);
		heran.Ficha.Ki = heran.Ficha.MaxKi;
		double kiDesl = heran.Ficha.Ki;
		Zanzoken(heran, pos + new Vec2(4 * ZoneCollision.TileSize, 0));
		AfirmarG11("desligado, o clique volta a ser o Zanzoken (que este corpo nao sabe): nenhum fio, nenhum Ki",
				   ProjeteisDaZona(heran.Zone.Hash).Count == antesTiros && Math.Abs(heran.Ficha.Ki - kiDesl) < 1e-9);

		LimparG11();
	}

	// =====================================================================
	// 12) OBSERVAR E DESPERTAR O POTENCIAL
	// =====================================================================
	private void G11ObservarEPotencial()
	{
		GD.Print("[g11] -- 12) OBSERVE (a projecao em texto) E UNLOCK POTENTIAL (uma vez por vida, com consentimento)");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer obs = ForjarG11("Observador", chao, 5_000, PathObserveG11T);
		ServerPlayer longe = Forjar("Vigiado", CorredorLivre(12), bp: 5_000);
		ServerPlayer robo = Forjar("Robo", longe.Pos + new Vec2(ZoneCollision.TileSize, 0), bp: 5_000);
		robo.Race = "Android";

		List<string> falas = ApertarEOuvir(obs, "Observe:Vigiado");
		AfirmarG11("Observe:<nome> projeta a mente: diz o mundo, o tile e a condicao, e marca `observingnow`",
				   obs.Ficha.observingnow > 0 && Disse(falas, "Earth") && Disse(falas, "condicao"), Ultimos(falas));
		AfirmarG11("...e enxerga quem esta em volta do vigiado", Disse(falas, "Robo"));

		falas = ApertarEOuvir(obs, "Observe:Robo");
		AfirmarG11("um Android nao tem energia pra achar: recusa", Disse(falas, "energia"), Ultimos(falas));

		longe.Ficha.isconcealed = true;
		falas = ApertarEOuvir(obs, "Observe:Vigiado");
		AfirmarG11("quem esconde o poder tambem nao", Disse(falas, "energia"));
		longe.Ficha.isconcealed = false;

		UsarHabilidade(obs, "Observe");
		AfirmarG11("Observe sem nome SOLTA a projecao", obs.Ficha.observingnow == 0);

		// ---------------- UNLOCK POTENTIAL ----------------
		ServerPlayer kai = ForjarG11("Anciao", chao, 5_000, PathUnlockG11T);
		ServerPlayer aprendiz = Forjar("Aprendiz", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 1_000);
		aprendiz.Conta = "g11_aprendiz";
		kai.AlvoId = aprendiz.Id;

		double bpAntes = aprendiz.Ficha.BP, kiskillAntes = aprendiz.Ficha.kiskill;
		double ganhoBaseMinimo = aprendiz.Ficha.CapCheck(aprendiz.Ficha.BP * 0.25 * Math.Max(aprendiz.Ficha.UPMod, 1));
		aprendiz.Ficha.BPBuffer = 0;

		falas = ApertarEOuvir(kai, "Unlock_Potential");
		AfirmarG11("o Anciao OFERECE ao marcado ao lado (nada muda ate ele aceitar)",
				   _ofertasDePotencialG11.ContainsKey(aprendiz.Conta) && Math.Abs(aprendiz.Ficha.BP - bpAntes) < 1e-12
				   && Disse(falas, "oferece"));

		ComandoDeCargo(aprendiz, "potencial_aceitar", "");
		AfirmarG11("ao aceitar, o potencial desperta: BP += pelo menos capcheck(BP*0,25*Potencial), kiskill +0,4, flag gravada",
				   aprendiz.Ficha.unlockPotential >= 1
				   && aprendiz.Ficha.BP - bpAntes >= ganhoBaseMinimo - 1e-6
				   && Math.Abs(aprendiz.Ficha.kiskill - kiskillAntes - 0.4) < 1e-9,
				   $"BP {bpAntes:0.##} -> {aprendiz.Ficha.BP:0.##} (min +{ganhoBaseMinimo:0.##})");
		AfirmarG11("...e o marco 'potential' (1,5x) foi alcancado", aprendiz.Ficha.bp_milestones_done.Contains("potential"));

		double bpUmaVez = aprendiz.Ficha.BP;
		falas = ApertarEOuvir(kai, "Unlock_Potential");
		AfirmarG11("a SEGUNDA vez e recusada ('ja foi despertado') e nao ha oferta nova",
				   Disse(falas, "ja foi despertado") && !_ofertasDePotencialG11.ContainsKey(aprendiz.Conta));
		ComandoDeCargo(aprendiz, "potencial_aceitar", "");
		AfirmarG11("...e aceitar sem oferta nao desperta nada de novo (UMA vez por vida)",
				   Math.Abs(aprendiz.Ficha.BP - bpUmaVez) < 1e-12);

		kai.AlvoId = 0;
		double bpKai = kai.Ficha.BP;
		UsarHabilidade(kai, "Unlock_Potential");
		AfirmarG11("sem marcado, o Anciao desperta o PROPRIO potencial na hora (o `view(1)` do DM inclui ele)",
				   kai.Ficha.unlockPotential >= 1 && kai.Ficha.BP > bpKai);

		LimparG11();
	}

	// =====================================================================
	// 13) DAR PODER E O TEMPO GUARDADO
	// =====================================================================
	private void G11DarPoderETimeStore()
	{
		GD.Print("[g11] -- 13) GIVE POWER (o Heal ao contrario, que termina em desmaio) E TIME STORE (o corpo preso no tempo)");

		Vec2 chao = CorredorLivre(12);
		ServerPlayer doador = ForjarG11("Doador", chao, 5_000, PathGivePowerG11T);
		AfirmarG11("a flag `cangivepower=1` do extrator chegou no campo ao aprender", doador.Ficha.cangivepower > 0);
		ServerPlayer alvo = Forjar("Recebe", chao + new Vec2(2 * ZoneCollision.TileSize, 0), bp: 5_000);
		alvo.Ficha.Ki = 0;
		doador.AlvoId = alvo.Id;

		doador.Ficha.Ki = doador.Ficha.MaxKi * 0.005;
		List<string> falas = ApertarEOuvir(doador, "Give_Power");
		AfirmarG11("sem Ki pra uma dose sequer a doacao recusa", !_doacaoG11.ContainsKey(doador.Id) && Disse(falas, "dose"));

		doador.Ficha.Ki = doador.Ficha.MaxKi;
		double dose = doador.Ficha.MaxKi * 0.01;
		UsarHabilidade(doador, "Give_Power");
		AfirmarG11("com Ki a doacao comeca, e a PRIMEIRA dose sai no aperto (a primeira volta do `while` vem antes do `sleep`)",
				   _doacaoG11.ContainsKey(doador.Id) && Math.Abs(alvo.Ficha.Ki - dose) < 1e-6, $"alvo {alvo.Ficha.Ki:0.##}");
		for (int i = 0; i < 5; i++) TickDoEfetorG11();
		AfirmarG11("cinco tiques depois o alvo recebeu 6 doses de 1% do MaxKi do doador, e o doador as perdeu",
				   Math.Abs(alvo.Ficha.Ki - 6 * dose) < 1e-6 && Math.Abs(doador.Ficha.MaxKi - doador.Ficha.Ki - 6 * dose) < 1e-6,
				   $"alvo {alvo.Ficha.Ki:0.##} doador {doador.Ficha.Ki:0.##}");
		double razaoDose = alvo.Ficha.MaxKi / doador.Ficha.MaxKi;
		AfirmarG11("...e o `CooldownAmount` do DOADOR subiu `M.MaxKi/MaxKi` por dose (o defeito visivel do DM; o decaimento de 1 s pode ter mordido 0,1%)",
				   doador.Ficha.CooldownAmount <= 6 * razaoDose + 1e-9 && doador.Ficha.CooldownAmount >= 6 * razaoDose * 0.999 - 1e-9,
				   $"{doador.Ficha.CooldownAmount:0.####} vs {6 * razaoDose:0.####}");

		UsarHabilidade(doador, "Give_Power");   // parar
		TickDoEfetorG11();
		AfirmarG11("parar encerra a doacao e o doador DESMAIA (o `spawn usr.KO()` do fim do laco)",
				   !_doacaoG11.ContainsKey(doador.Id) && doador.Ficha.KO);

		double cd = doador.Ficha.CooldownAmount;
		for (int i = 0; i < 6; i++) TickDoEfetorG11();
		AfirmarG11("...e o CooldownAmount DECAI a cada segundo (0,1% enquanto >= 1; abaixo disso zera)",
				   cd < 1 ? doador.Ficha.CooldownAmount == 0 : doador.Ficha.CooldownAmount < cd, $"{cd:0.####} -> {doador.Ficha.CooldownAmount:0.####}");

		// ---------------- TIME STORE ----------------
		ServerPlayer kan = ForjarG11("Kanassa", chao, 5_000, SkillTimeStoreG11);
		kan.Idade = 25;
		kan.Ficha.Idade = 25;
		TickDoEfetorG11();
		AfirmarG11("ao aprender, o Time Store PRENDE a idade (stuckage = 25)", Math.Abs(kan.Ficha.stuckage - 25) < 1e-9);
		EnvelhecerG2(kan, 1.0);   // o Toque do Tempo envelheceria...
		AfirmarG11("(preparo) o corpo envelheceu um ano por fora", kan.Idade == 26);
		TickDoEfetorG11();
		AfirmarG11("...e o efetor devolve a idade presa (`savant.Age = stuckage`)", kan.Idade == 25 && Math.Abs(kan.Ficha.Idade - 25) < 1e-9);

		double guardadoAntes = kan.Ficha.stored_time;
		RelogioDoPlaneta terra = RelogioDaZona(ZoneKey.Premade("Earth"));
		_adiantoDoCeu += DiasPorTempoGuardadoG11 * terra.SegundosPorDia;   // um MES da Terra
		TickDoEfetorG11();
		AfirmarG11("um mes (28 dias) do calendario da Terra depois, o tempo guardado sobe 1",
				   Math.Abs(kan.Ficha.stored_time - guardadoAntes - 1) < 1e-9, $"{guardadoAntes} -> {kan.Ficha.stored_time}");

		LimparG11();
	}
}
