using Godot;
using Jandirus.Core;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO KIT DO DEUS DA DESTRUICAO (`--godkitteste`) -- pedido do dono de 2026-10-11: *"usar a forma fury
/// (forma exclusiva do god of destruction, q tem detalhada no DM mas n lembro se ta aqui no port tem q verificar)"*
/// e *"n esqueca de ver tb se as passivas do god of destruction tb foram portadas"*.
///
/// No boot, como as outras de servidor. Os corpos sao forjados (sem `Peer`) e TUDO passa pelo funil de producao:
/// os verbs entram pelo `UsarHabilidade` -- a mesma porta do `C2S.Habilidade` --, o trono e escrito no `_tronos`
/// de verdade (fotografado e devolvido no `finally`) e a reconciliacao e a `ReconciliarDadiva` de producao.
///
///   1. A FURY: o que o catalogo diz, quem entra, quanto vale, o que a tira e o que NAO a tira.
///   2. O HAKAI: os dois criterios, o agarrao, a marca e o dobro no Enma.
///   3. O POWER FLICK: solta, fere o torso com dois golpes pesados e arremessa.
///   4. O DESTRUCTION GOD'S FURY: a carga, a receita, a zona do tiro e a guarda que cai.
///   5. A DESTROYER SPHERE: a receita e o Hakai no contato.
///   6. A ENERGY NULLIFICATION: o que some, o que fica, e a recarga que so arma com algo anulado.
///   7. O GOD'S TEMPER: dez danos sem guarda, os dez segundos, a recarga, e a guarda que zera.
///   8. OS CAMINHOS: o que o titulo desperta (a Destruicao; o Instinto do Anjo), amplifica (1,25) e empresta (0,25),
///      e o lembrete da tarefa pendente no login.
///   9. O ULTRA INSTINTO contra o `UltraInstinct.dm`: a passiva na forma, os portoes, o ki, as pilhas, a renovacao,
///      o Godly Display.
///  10. O PODER DA DESTRUICAO contra o `UltraEgo.dm`: a aura na forma, o recuo, o ki devorado, a infusao, o Unbound
///      Ego, a explosao.
///
/// Cada regra tem o seu defeito injetado (`...DeTeste`), ligado em `try/finally`.
///
///     Godot --headless -- --server --port 8340 --godkitteste
/// </summary>
public partial class GameServer
{
	private int _gdkOk, _gdkFalhou;

	private void AfirmarGdk(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _gdkOk++; GD.Print($"[godkit]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _gdkFalhou++; GD.PrintErr($"[godkit]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>As contas da bancada: so letras e sublinhado, e nenhuma delas existe no disco.</summary>
	private const string ContaDoDeusDeBancada = "bancada_godkit_deus", ContaDoMortalDeBancada = "bancada_godkit_mortal";

	/// <summary>Um corpo forjado com raca e conta proprias, com a ficha recalculada (a ordem do `ForjarComSkills`).</summary>
	private ServerPlayer ForjarGdk(string nome, Vec2 onde, double bp, string raca = "Human", string conta = ContaDoMortalDeBancada)
	{
		ServerPlayer pl = Forjar(nome, onde, bp);
		pl.Conta = conta;
		pl.Race = raca;
		pl.Ficha.Race = raca;
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.ClampAnger();
		pl.Ficha.PowerLevel(agoraMs: NowMs());
		return pl;
	}

	public void RodarBancadaDoKitDoDeus()
	{
		_gdkOk = _gdkFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[godkit] ================ O KIT DO DEUS DA DESTRUICAO (pedido de 2026-10-11) ================");

		// O TRONO DE VERDADE E FOTOGRAFADO: a bancada escreve no `_tronos` de producao (so na memoria -- nada aqui
		// chama `SalvarCargos`) e devolve o dono que estava la.
		string donoAntes = _tronos.TryGetValue(KitDoDeus.Cargo, out string? d0) ? d0 : "";
		string anjoAntes = _tronos.TryGetValue(Disciplinas.UltraInstinct.RankQueEnsina, out string? a0) ? a0 : "";
		string? tronoVistoAntes = _tronoVistoPeloKit;
		long relogioDeAntes = AdiantoDoRelogioDeTeste;

		try
		{
			AFuryDoDeus();
			OHakaiDoDeus();
			OPowerFlickDoDeus();
			OTiroDaFuriaDoDeus();
			AEsferaDoDeus();
			AAnulacaoDoDeus();
			OSurtoDoDeus();
			OsCaminhosDoDeus();
			AsPassivasDoInstinto();
			AsPassivasDaDestruicao();
		}
		catch (Exception e)
		{
			AfirmarGdk("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			FuryFicaSemOTituloDeTeste = FuryAtropelaOutraFormaDeTeste = FuryCaiComOCorpoDeTeste = false;
			EstadoDeForma.SaiDaFuryPelaEscadaDeTeste = false;
			HakaiSemCriterioDeTeste = HakaiNaoEncareceDeTeste = FlickValeUmGolpeDeTeste = false;
			TiroDoKitSemCargaDeTeste = TiroDoKitSemDomDeTeste = false;
			AnulacaoApagaOsFortesDeTeste = AnulacaoSempreCobraDeTeste = TemperContaComGuardaDeTeste = false;
			ToggleLigaNaFormaDeTeste = PassivaSoComToggleDeTeste = EsquivaSemPortoesDeTeste = false;
			FormaNaoRenovaDeTeste = KiIgnoraADestruicaoDeTeste = DisplayMarcaEmVoltaDeTeste = false;
			LoginCalaATarefaDeTeste = false;
			if (anjoAntes.Length > 0) _tronos[Disciplinas.UltraInstinct.RankQueEnsina] = anjoAntes;
			else _tronos.Remove(Disciplinas.UltraInstinct.RankQueEnsina);
			_tronoVistoPeloKit = tronoVistoAntes;
			EscutaDeBaquesDoKit = null;
			EscutaDeGestos = null;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			if (donoAntes.Length > 0) _tronos[KitDoDeus.Cargo] = donoAntes; else _tronos.Remove(KitDoDeus.Cargo);
			_deusesSemDono.Clear();
			LimparTudoDaBancada();
		}

		GD.Print($"[godkit] ================ {_gdkOk} OK, {_gdkFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// 1) A FURY
	// =====================================================================
	private void AFuryDoDeus()
	{
		GD.Print("[godkit] -- 1) A FURY: a forma do titulo (`GodOfDestruction.dm:388-419`) --");
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;

		// ---------------------------------------------------------------- 1a. o catalogo
		FormaDef? fury = Catalogo.Def(KitDoDeus.IdDaFury);
		AfirmarGdk("o catalogo tem a forma `god_fury` (o id e contrato)", fury != null);
		if (fury == null) return;

		AfirmarGdk("**72x, FIXO E ABSOLUTO** (`GOD_FURY_MULT 72`, `:18`; `formsBuff = GOD_FURY_MULT`, `:398`)",
				  fury.Mult.Length == 1 && fury.Mult[0] == 72 && fury.Absoluta, $"x{string.Join("/", fury.Mult)}");
		AfirmarGdk("SEM DRENO DE KI (o `Loop()` do buff so confere o titulo, `:402-404`)",
				  Catalogo.DrenoPorSegundo(fury.Id, new Maestrias()) == 0);
		Colada[] coladas = Catalogo.Coladas(fury);
		AfirmarGdk("a aura e a `god - grey` COLADA, pintada de `rgb(190, 80, 255)` (`:388-391`)",
				  coladas.Length == 1 && coladas[0].Folha == FolhaColada.Ameacadora && coladas[0].Tinta == "be50ff",
				  string.Join("+", coladas.Select(c => $"{c.Folha}#{c.Tinta}")));
		AfirmarGdk("a chama grande continua sendo a do personagem, e a forma nao acumula maestria",
				  Catalogo.ChamaDoJogador(fury) && !Catalogo.SustentarTreina(fury));
		AfirmarGdk("ela tem cena propria -- e so na ESTREIA: da segunda vez em diante e instantanea",
				  Cinematicas.Para(fury) != null
				  && Cinematicas.Degrau(fury, estreia: true, 0) == DegrauDeCena.Estreia
				  && Cinematicas.Degrau(fury, estreia: false, 0) == DegrauDeCena.Nenhuma);
		AfirmarGdk("nenhum perfil tem a linha dela aberta: a escada (tecla C, tecla de forma) nunca a oferece",
				  !Catalogo.LinhaAberta(new PerfilDeFormas(Raca: "Saiyan", GodKi: 100, EnergiaUe: 100), fury.Linha)
				  && new EstadoDeForma().Avaliar(fury.Id, 1e15, 1, false, new PerfilDeFormas(Raca: "Saiyan", GodKi: 100))
					 == RecusaForma.LinhaFechada);

		// ---------------------------------------------------------------- a cena
		Vec2 chao = CorredorLivre(12);
		ServerPlayer deus = ForjarGdk("Deus", chao, 1e9, raca: "Saiyan", conta: ContaDoDeusDeBancada);
		ServerPlayer mortal = ForjarGdk("Mortal", chao + new Vec2(4 * T, 0), 1e9, raca: "Saiyan");
		_tronos[KitDoDeus.Cargo] = ContaDoDeusDeBancada;

		// ---------------------------------------------------------------- 1b. quem entra
		GD.Print("[godkit] -- 1b) quem entra --");
		List<string> falas = ApertarEOuvir(mortal, "god_fury");
		AfirmarGdk("SEM O TITULO o verb recusa, com a frase do kit, e o corpo fica na base (`if(!god_is_god(usr)) return`, `:414`)",
				  mortal.Forma.NaBase && Disse(falas, "do Deus da Destruição"), Ultimos(falas));
		AfirmarGdk("...e a porta de quem dirige um corpo tambem recusa (`EntrarNaFury`)", !EntrarNaFury(mortal) && mortal.Forma.NaBase);

		double bpAntes = deus.Ficha.expressedBP;
		double kiAntes = deus.Ficha.Ki;
		falas = ApertarEOuvir(deus, "god_fury");
		AfirmarGdk("**O DEUS DA DESTRUICAO ENTRA NA FURY PELO VERB**", EstaNaFury(deus) && Disse(falas, "FÚRIA"), Ultimos(falas));
		AfirmarGdk("o multiplicador da ficha e 72", Math.Abs(deus.Ficha.ssjBuff - 72) < 1e-9, $"ssjBuff {deus.Ficha.ssjBuff:0.##}");
		AfirmarGdk("e o PODER subiu 72 vezes na hora (sem esperar o tique de ficha)",
				  bpAntes > 0 && Math.Abs(deus.Ficha.expressedBP / bpAntes - 72) < 0.01,
				  $"razao {deus.Ficha.expressedBP / Math.Max(bpAntes, 1):0.###}");
		AfirmarGdk("entrar NAO mexe no Ki (o DM nao cobra nem enche)", Math.Abs(deus.Ficha.Ki - kiAntes) < 1e-6,
				  $"{kiAntes:0.##} -> {deus.Ficha.Ki:0.##}");

		// ---------------------------------------------------------------- 1c. o que ela custa: nada
		GD.Print("[godkit] -- 1c) sem dreno e sem maestria --");
		deus.CenaSegundos = 0;   // a estreia prende o corpo e congela o tique: a medida e do tique LIVRE
		double ki0 = deus.Ficha.Ki;
		for (int i = 0; i < 10; i++) TickDaForma(deus, 1.0);
		AfirmarGdk("dez segundos de Fury nao custam Ki nenhum", Math.Abs(deus.Ficha.Ki - ki0) < 1e-6, $"{ki0:0.##} -> {deus.Ficha.Ki:0.##}");
		AfirmarGdk("e nao rendem maestria (a forma nao tem)", deus.Forma.Maestria.De(KitDoDeus.IdDaFury) == 0);

		// O 1,25 DO CAMINHO DA DESTRUICAO NAO ENTRA NA FURY: `formsBuff = GOD_FURY_MULT`, a constante crua.
		deus.PoderDaDestruicao.Aprendida = true;
		AplicarForma(deus);
		AfirmarGdk("o Deus que trilha a Destruicao continua em 72x na Fury (o 1,25 e das formas do Poder da Destruicao, `UltraEgo.dm:544-547`)",
				  Math.Abs(deus.Ficha.ssjBuff - 72) < 1e-9, $"ssjBuff {deus.Ficha.ssjBuff:0.##}");
		deus.PoderDaDestruicao.Aprendida = false;

		// ---------------------------------------------------------------- 1d. o que NAO a tira
		GD.Print("[godkit] -- 1d) o que nao a tira: o corpo caido e o Ki zerado (`Revert.dm:12-23` nao lista o GodFury) --");
		deus.Combate.Nocautear(60);
		TickDaForma(deus, 0.1);
		AfirmarGdk("NOCAUTEADO, o Deus continua na Fury", EstaNaFury(deus), deus.Forma.Atual);
		FuryCaiComOCorpoDeTeste = true;
		try
		{
			TickDaForma(deus, 0.1);
			AfirmarGdk("(defeito injetado: a Fury cai com o corpo) o nocaute a desfaz, como a toda forma da escada",
					  !EstaNaFury(deus), deus.Forma.Atual);
		}
		finally { FuryCaiComOCorpoDeTeste = false; }
		deus.Combate.Levantar();
		EntrarNaFury(deus);
		deus.Ficha.Ki = 0;
		TickDaForma(deus, 0.1);
		AfirmarGdk("com o Ki ZERADO ela continua de pe (ela nao cobra Ki)", EstaNaFury(deus), deus.Forma.Atual);
		deus.Ficha.Ki = deus.Ficha.MaxKi;

		// ---------------------------------------------------------------- 1e. exclusividade
		GD.Print("[godkit] -- 1e) uma forma por vez (`slot = sFORM`, `:395`; `buffs.dm:110-113`) --");
		deus.Forma.Liberar("ssj1");   // ja despertou o Super Saiyajin: a raiva nao e o assunto desta prova
		falas = Ouvir(() => Verbo(deus, "forma", "ssj1"));
		AfirmarGdk("**NA FURY, A ESCADA NAO ENTRA**: pedir o Super Saiyajin e recusado e o corpo continua na Fury",
				  EstaNaFury(deus), $"{deus.Forma.Atual} | {Ultimos(falas)}");
		EstadoDeForma.SaiDaFuryPelaEscadaDeTeste = true;
		try
		{
			Ouvir(() => Verbo(deus, "forma", "ssj1"));
			AfirmarGdk("(defeito injetado: sai da Fury pela escada) o Super Saiyajin atropela a forma do titulo",
					  deus.Forma.Atual == "ssj1", deus.Forma.Atual);
		}
		finally { EstadoDeForma.SaiDaFuryPelaEscadaDeTeste = false; }

		// (o corpo ficou em Super Saiyajin pelo defeito: e a cena da prova seguinte)
		if (deus.Forma.Atual != "ssj1") { deus.Forma.Entrar("ssj1"); AplicarForma(deus); }
		falas = ApertarEOuvir(deus, "god_fury");
		AfirmarGdk("**COM OUTRA FORMA DE PE A FURY NAO ENTRA**, e a recusa fala (*\"Recolha a outra forma...\"*, `:416`)",
				  deus.Forma.Atual == "ssj1" && Disse(falas, "recolha a outra forma"), Ultimos(falas));
		FuryAtropelaOutraFormaDeTeste = true;
		try
		{
			ApertarEOuvir(deus, "god_fury");
			AfirmarGdk("(defeito injetado: a Fury atropela) ela entra por cima do Super Saiyajin", EstaNaFury(deus), deus.Forma.Atual);
		}
		finally { FuryAtropelaOutraFormaDeTeste = false; }

		// ---------------------------------------------------------------- 1f. o verb desliga
		if (!EstaNaFury(deus)) { deus.Forma.Entrar(Catalogo.IdBase); AplicarForma(deus); EntrarNaFury(deus); }
		falas = ApertarEOuvir(deus, "god_fury");
		AfirmarGdk("o verb de novo RECOLHE (*\"A Furia se recolhe.\"*, `:408`), e o corpo volta ao normal",
				  deus.Forma.NaBase && Disse(falas, "se recolhe") && Math.Abs(deus.Ficha.ssjBuff - 1) < 1e-9, Ultimos(falas));

		// ---------------------------------------------------------------- 1g. o titulo cai, a Fury cai
		GD.Print("[godkit] -- 1g) perdeu o titulo, perdeu a Fury -- NA HORA (`:402-403`, `:144`) --");
		EntrarNaFury(deus);
		_tronos.Remove(KitDoDeus.Cargo);
		FuryFicaSemOTituloDeTeste = true;
		try
		{
			ReconciliarDadiva(deus);
			AfirmarGdk("(defeito injetado: a Fury fica sem o titulo) o destituido continua em 72x", EstaNaFury(deus), deus.Forma.Atual);
		}
		finally { FuryFicaSemOTituloDeTeste = false; }
		falas = Ouvir(() => ReconciliarDadiva(deus));
		AfirmarGdk("**SEM O TITULO A FURY MORRE NA RECONCILIACAO** (a mesma chamada do `Destronar`, do `Entronizar` e do tique de 1 Hz)",
				  deus.Forma.NaBase && Math.Abs(deus.Ficha.ssjBuff - 1) < 1e-9 && Disse(falas, "sem o título"), Ultimos(falas));
		falas = ApertarEOuvir(deus, "god_fury");
		AfirmarGdk("...e o ex-Deus nao a religa", deus.Forma.NaBase && Disse(falas, "do Deus da Destruição"), Ultimos(falas));

		// ---------------------------------------------------------------- 1h. o corpo sem dono
		GD.Print("[godkit] -- 1h) o corpo SEM DONO (NPC) --");
		ServerPlayer npc = ForjarGdk("DeusSemDono", chao + new Vec2(8 * T, 0), 1e9, conta: "");
		AfirmarGdk("um corpo sem conta nao e Deus de ninguem, e a Fury o recusa", !EhDeusDaDestruicao(npc) && !EntrarNaFury(npc, semCena: true));
		ConsagrarDeusSemDono(npc);
		double bpNpc = npc.Ficha.expressedBP;
		AfirmarGdk("**CONSAGRADO, O CORPO SEM DONO ENTRA NA FURY** (sem conta, sem `Peer`, e sem tocar no trono de verdade)",
				  EhDeusDaDestruicao(npc) && EntrarNaFury(npc, semCena: true) && EstaNaFury(npc)
				  && Math.Abs(npc.Ficha.expressedBP / bpNpc - 72) < 0.01 && !_tronos.ContainsKey(KitDoDeus.Cargo),
				  $"razao {npc.Ficha.expressedBP / Math.Max(bpNpc, 1):0.###}");
		AfirmarGdk("sem cena pedida, o corpo nao fica preso", npc.CenaSegundos == 0, $"{npc.CenaSegundos:0.##}s");
		ConsagrarDeusSemDono(npc, consagrado: false);
		AfirmarGdk("desconsagrado, a Fury cai na hora", !EstaNaFury(npc) && !EhDeusDaDestruicao(npc), npc.Forma.Atual);
	}

	// =====================================================================
	// FERRAMENTAS DAS SECOES 2 A 7
	// =====================================================================
	/// <summary>O Deus de bancada no trono de verdade, e um corredor limpo. Devolve o Deus e o chao dele.</summary>
	private (ServerPlayer Deus, Vec2 Chao) CenaDoKit(double bp = 1e9, int tiles = 24)
	{
		LimparTudoDaBancada();
		Vec2 chao = CorredorLivre(tiles);
		ServerPlayer deus = ForjarGdk("Deus", chao, bp, conta: ContaDoDeusDeBancada);
		deus.Facing = Facing.East;
		_tronos[KitDoDeus.Cargo] = ContaDoDeusDeBancada;
		return (deus, chao);
	}

	/// <summary>Poe o torso deste corpo numa fracao da vida maxima (e a vida do corpo em dia).</summary>
	private static void TorsoEm(ServerPlayer pl, double fracao)
	{
		BodyPart torso = pl.Combate.Corpo.Achar("Torso")!;
		torso.Vida = torso.VidaMax * fracao;
		pl.Combate.SincronizarVida();
	}

	private static double FracaoDoTorso(ServerPlayer pl) => pl.Combate.Corpo.Achar("Torso")!.Fracao;

	/// <summary>
	/// O QUE CADA MEMBRO SORTEAVEL PERDEU POR GOLPE DIRETO: a perda dele, menos o que escorreu do dono.
	///
	/// Os aninhados ficam de fora (ninguem os acerta: so levam a propagacao). O REPRODUTOR e o caso torto: ele e
	/// sorteavel (`isnested = 0`) E pertence ao Abdomen -- um golpe no Abdomen escorre `Regras.Propagacao` pra ele.
	/// Contar esse escorrido como golpe fazia cinco golpes somarem cinco e um quinto, e um golpe so ferir "dois
	/// membros": a prova passava ou falhava conforme o dado caisse no Abdomen (9% das vezes mirando o torso).
	/// </summary>
	private static List<double> PerdasDiretas(ServerPlayer pl, Dictionary<string, double> antes)
	{
		Body corpo = pl.Combate.Corpo;
		List<double> perdas = [];
		foreach (BodyPart p in corpo.Partes)
		{
			if (p.Aninhado || !antes.TryGetValue(p.Nome, out double v)) continue;
			double perda = v - p.Vida;
			if (p.Dono != null && corpo.Achar(p.Dono) is { } dono && antes.TryGetValue(dono.Nome, out double vd))
				perda -= (vd - dono.Vida) * Regras.Propagacao;
			perdas.Add(perda);
		}
		return perdas;
	}

	/// <summary>A maior perda de vida por golpe direto num membro sorteavel.</summary>
	private static double MaiorPerda(ServerPlayer pl, Dictionary<string, double> antes) =>
		PerdasDiretas(pl, antes).DefaultIfEmpty(0).Max();

	private static Dictionary<string, double> VidasDe(ServerPlayer pl) =>
		pl.Combate.Corpo.Partes.ToDictionary(p => p.Nome, p => p.Vida);

	/// <summary>Os tiros vivos deste dono na zona da bancada.</summary>
	private List<Projetil> TirosDe(ServerPlayer dono) =>
		[.. ProjeteisDaZona(ZonaDaBancadaDeProjetil.Hash).Where(p => p.Vivo && p.Dono == dono.Id)];

	/// <summary>Aperta um dos dois tiros do kit SEM a carga (o defeito de bancada) e devolve a bola que nasceu.</summary>
	private Projetil? TiroDoKitNaHora(ServerPlayer deus, string verb)
	{
		deus.Ficha.Ki = deus.Ficha.MaxKi;
		HashSet<int> antes = [.. TirosDe(deus).Select(p => p.Id)];
		TiroDoKitSemCargaDeTeste = true;
		try { UsarHabilidade(deus, verb); }
		finally { TiroDoKitSemCargaDeTeste = false; }
		return TirosDe(deus).FirstOrDefault(p => !antes.Contains(p.Id));
	}

	/// <summary>
	/// O BP DE UM CORPO QUE ESTE TIRO FERE SEM DERRUBAR. A conta e a de producao (`DanoDeKi.Final`, com o tiro de
	/// verdade na mao), e a faixa vai de 15 a 60 -- um nucleo inteiro que leva 60 nao-letal fica acima do limiar do
	/// nocaute, e o nocaute baixa a guarda sozinho (`CombatState.Nocautear`): acima dela a prova mediria outra coisa.
	///
	/// O PISO DE 15 E UMA VEZ E MEIA O ANTIGO CORTE POR DANO (`DanoDeKi.CorteDoFraco`): ate 2026-10-12, abaixo de 10
	/// o tiro nem feria. Hoje quem o tiro nao fere e quem e parede pra ele (`KiContraCorpo`) -- e o corpo achado aqui,
	/// sempre mais fraco que o Deus, nunca e. O piso ficou como um dano que se le.
	///
	/// As razoes comecam em 1,5: o corpo achado tambem e fraco o bastante pro Hakai (75% do poder do Deus).
	/// </summary>
	private double BpQueOTiroFere(ServerPlayer deus, string verb, bool deGuarda)
	{
		Projetil? sonda = TiroDoKitNaHora(deus, verb);
		ServerPlayer saco = ForjarGdk("Sonda", deus.Pos + new Vec2(0, 5 * ZoneCollision.TileSize), deus.Ficha.BP);
		double achado = 0, danoAchado = 0;
		if (sonda != null)
			foreach (double razao in new[] { 1.5, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64 })
			{
				saco.Ficha.BP = deus.Ficha.BP / razao;
				saco.Ficha.Statify();
				saco.Ficha.PowerLevel(agoraMs: NowMs());
				double dano = DanoDeKi.Final(sonda.ModsAgora(), sonda.BaseDano, sonda.MaxDano, sonda.Bp, saco.Combate, deGuarda, sonda.Fisico);
				if (dano < DanoDeKi.CorteDoFraco * 1.5 || dano > 60) continue;
				achado = saco.Ficha.BP;
				danoAchado = dano;
				break;
			}
		AfirmarGdk($"(montagem) ha um corpo que este tiro fere sem derrubar{(deGuarda ? ", de guarda erguida" : "")}",
				  achado > 0, $"{verb}: BP {achado:0.###e0} contra {deus.Ficha.BP:0.###e0}, dano {danoAchado:0.#}");

		if (sonda is { Vivo: true }) Matar(sonda, FimDeProjetil.Apagou);
		TickDosProjeteis(Protocol.TickSeconds);
		EsquecerTecnicas(saco.Id);
		_players.Remove(saco.Id);
		ZoneList(saco.Zone.Hash).Remove(saco);
		return achado > 0 ? achado : deus.Ficha.BP / 2;
	}

	// =====================================================================
	// 2) O HAKAI
	// =====================================================================
	private void OHakaiDoDeus()
	{
		GD.Print("[godkit] -- 2) O HAKAI: o agarrao executor (`GodOfDestruction.dm:182-222`) --");
		const int T = ZoneCollision.TileSize;

		// ---------------------------------------------------------------- 2a. a regra pura
		AfirmarGdk("a regra pura: torso em EXATAMENTE 15% pega, e um fio acima nao (`> GOD_HAKAI_TORSO_PCT / 100`, `:190`)",
				  KitDoDeus.HakaiPega(15, 100, 50, 100) && !KitDoDeus.HakaiPega(15.01, 100, 50, 100));
		AfirmarGdk("...o alvo com EXATAMENTE 75% do poder do Deus pega, e um fio acima nao (`> expressedBP * GOD_HAKAI_BP_FRAC`, `:192`)",
				  KitDoDeus.HakaiPega(10, 100, 75, 100) && !KitDoDeus.HakaiPega(10, 100, 75.01, 100));
		AfirmarGdk("...e torso sem vida maxima nao pega (`T.maxhealth <= 0`, `:189`)", !KitDoDeus.HakaiPega(0, 0, 1, 100));

		(ServerPlayer deus, Vec2 chao) = CenaDoKit();
		ServerPlayer mortal = ForjarGdk("Mortal", chao + new Vec2(0, 3 * T), 1e9);
		mortal.Facing = Facing.East;

		// ---------------------------------------------------------------- 2b. quem usa
		List<string> falas = ApertarEOuvir(deus, KitDoDeus.VerbHakai);
		AfirmarGdk("sem ninguem nos bracos nem na frente o Deus ouve o que falta (`:219`)", Disse(falas, "agarre (ou encoste em)"), Ultimos(falas));

		ServerPlayer fraco = ForjarGdk("Fraco", chao + new Vec2(0.9f * T, 0), 5e8);
		ServerPlayer forte = ForjarGdk("Forte", chao + new Vec2(-0.9f * T, 0), 9e8);
		AfirmarGdk("(montagem) o fraco tem menos de 75% do poder expresso do Deus, e o forte tem mais",
				  fraco.Ficha.expressedBP <= deus.Ficha.expressedBP * 0.75 && forte.Ficha.expressedBP > deus.Ficha.expressedBP * 0.75,
				  $"fraco {fraco.Ficha.expressedBP / deus.Ficha.expressedBP:0.##}, forte {forte.Ficha.expressedBP / deus.Ficha.expressedBP:0.##}");

		TorsoEm(fraco, 0.10);
		falas = ApertarEOuvir(mortal, KitDoDeus.VerbHakai);
		AfirmarGdk("SEM O TITULO o verb recusa, e a vitima pronta continua viva (`if(!god_is_god(usr)) return`, `:215`)",
				  !fraco.Ficha.dead && Disse(falas, "do Deus da Destruição"), Ultimos(falas));

		// ---------------------------------------------------------------- 2c. os dois criterios
		GD.Print("[godkit] -- 2c) os dois criterios --");
		TorsoEm(fraco, 1.0);
		falas = ApertarEOuvir(deus, KitDoDeus.VerbHakai);
		AfirmarGdk("com o TORSO INTEIRO o alvo resiste, e a recusa diz os dois criterios (`:222`)",
				  !fraco.Ficha.dead && Disse(falas, "ainda resiste") && Disse(falas, "15%") && Disse(falas, "25%"), Ultimos(falas));
		TorsoEm(fraco, 0.16);
		ApertarEOuvir(deus, KitDoDeus.VerbHakai);
		AfirmarGdk("com o torso em 16% tambem", !fraco.Ficha.dead);

		deus.Facing = Facing.West;
		TorsoEm(forte, 0.05);
		falas = ApertarEOuvir(deus, KitDoDeus.VerbHakai);
		AfirmarGdk("**O ALVO FORTE DEMAIS RESISTE** mesmo com o torso em 5% (mais de 75% do poder do Deus)",
				  !forte.Ficha.dead && Disse(falas, "ainda resiste"), Ultimos(falas));

		HakaiSemCriterioDeTeste = true;
		try
		{
			TorsoEm(forte, 1.0);
			ApertarEOuvir(deus, KitDoDeus.VerbHakai);
			AfirmarGdk("(defeito injetado: o Hakai sem criterio) o forte, de torso inteiro, e apagado", forte.Ficha.dead);
		}
		finally { HakaiSemCriterioDeTeste = false; }

		// ---------------------------------------------------------------- 2d. o Hakai pega
		GD.Print("[godkit] -- 2d) o Hakai pega --");
		deus.Facing = Facing.East;
		TorsoEm(fraco, 0.10);
		List<int> baques = [];
		List<(int Quem, byte Gesto)> gestos = [];
		EscutaDeBaquesDoKit = baques;
		EscutaDeGestos = gestos;
		try { falas = ApertarEOuvir(deus, KitDoDeus.VerbHakai); }
		finally { EscutaDeBaquesDoKit = null; EscutaDeGestos = null; }
		AfirmarGdk("**TORSO EM 10% E 50% DO PODER: O ALVO E APAGADO**, e quem ve le o HAKAI (`:197`)",
				  fraco.Ficha.dead && Disse(falas, "HAKAI") && Disse(falas, "partículas de luz violeta"), Ultimos(falas));
		AfirmarGdk("...o Deus fica como o algoz (`M.lastDamager = src`, `:204`)", fraco.UltimoAgressor == deus.Id);
		AfirmarGdk("...a zona ve o baque em cima da vitima e ouve o gesto do Deus (onda de choque, poeira, tremor e o `scouterexplode`, `:198-201`)",
				  baques.Contains(fraco.Id) && gestos.Exists(g => g.Quem == deus.Id && g.Gesto == (byte)Protocol.GestoDoCorpo.Kiai),
				  $"{baques.Count} baque(s), {gestos.Count} gesto(s)");
		AfirmarGdk("**A ALMA FICA MARCADA** (`M.hakai_mark = 1`, `:203`)", fraco.Ficha.hakai_mark);

		falas = ApertarEOuvir(deus, KitDoDeus.VerbHakai);
		AfirmarGdk("o morto nao e apagado de novo (`M.dead`, `:184`)", Disse(falas, "ainda resiste") || Disse(falas, "agarre"), Ultimos(falas));

		// ---------------------------------------------------------------- 2e. pelo agarrao
		ServerPlayer preso = ForjarGdk("Preso", chao + new Vec2(-3 * T, 0), 5e8);
		Prender(deus, preso);
		TorsoEm(preso, 0.10);
		AfirmarGdk("(montagem) o Deus segura alguem que NAO esta na frente dele", QuemEuSeguro(deus) == preso && AlvoNaFrente(deus) != preso);
		falas = ApertarEOuvir(deus, KitDoDeus.VerbHakai);
		AfirmarGdk("**QUEM ESTA NOS BRACOS VEM PRIMEIRO** (`var/mob/M = usr.grabbee`, `:216`): o preso e apagado",
				  preso.Ficha.dead && preso.Ficha.hakai_mark, Ultimos(falas));

		// ---------------------------------------------------------------- 2f. o corpo sem dono
		ServerPlayer npc = ForjarGdk("DeusSemDono", chao + new Vec2(6 * T, 0), 1e9, conta: "");
		npc.Facing = Facing.East;
		ServerPlayer presa = ForjarGdk("Presa", chao + new Vec2(6.9f * T, 0), 5e8);
		TorsoEm(presa, 0.10);
		HakaiDoDeus(npc);
		bool antesDeConsagrar = presa.Ficha.dead;
		ConsagrarDeusSemDono(npc);
		HakaiDoDeus(npc);
		AfirmarGdk("**O CORPO SEM DONO CONSAGRADO APAGA** (e antes de consagrado, nao)", !antesDeConsagrar && presa.Ficha.dead);
		ConsagrarDeusSemDono(npc, consagrado: false);

		// ---------------------------------------------------------------- 2g. o Enma cobra o dobro
		GD.Print("[godkit] -- 2g) o dobro no Enma (`SkyNPCs.dm:189`) e a marca que sai na volta a vida (`Death.dm:148`) --");
		AfirmarGdk("o preco da volta de quem foi apagado e o DOBRO; o de quem nao foi, o de sempre",
				  Math.Abs(PrecoDoEnmaPara(fraco) - Alem.PrecoDoReviveDoEnma * 2) < 1e-6
				  && Math.Abs(PrecoDoEnmaPara(mortal) - Alem.PrecoDoReviveDoEnma) < 1e-6,
				  $"{PrecoDoEnmaPara(fraco):0} / {PrecoDoEnmaPara(mortal):0}");

		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		Obra? enma = _noChao.FirstOrDefault(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem));
		AfirmarGdk("(montagem) o Enma esta na mesa dele", enma != null);
		if (enma == null) return;

		var naMesa = new Vec2(enma.X + T, enma.Y);
		MoveToZone(fraco.Id, alem, naMesa);
		fraco.Pos = naMesa;
		fraco.Ficha.Zeni = Alem.PrecoDoReviveDoEnma * 1.5;
		falas = Ouvir(() => EnmaReviverPorZeni(fraco));
		AfirmarGdk("**COM UMA VEZ E MEIA O PRECO O APAGADO NAO VOLTA**: o Enma recusa, diz por que, e nao cobra nada",
				  fraco.Ficha.dead && Math.Abs(fraco.Ficha.Zeni - Alem.PrecoDoReviveDoEnma * 1.5) < 1e-6 && Disse(falas, "apagou você"),
				  Ultimos(falas));

		HakaiNaoEncareceDeTeste = true;
		try
		{
			Ouvir(() => EnmaReviverPorZeni(fraco));
			AfirmarGdk("(defeito injetado: o Hakai nao encarece) com uma vez e meia o preco ele volta, pagando o preco de todo mundo",
					  !fraco.Ficha.dead && Math.Abs(fraco.Ficha.Zeni - Alem.PrecoDoReviveDoEnma * 0.5) < 1e-6, $"zeni {fraco.Ficha.Zeni:0}");
		}
		finally { HakaiNaoEncareceDeTeste = false; }
		AfirmarGdk("...e a volta a vida APAGA a marca (`hakai_mark = 0`, `Death.dm:148`)", !fraco.Ficha.hakai_mark);

		// de novo, agora pagando o preco certo
		fraco.Combate.Morrer(ignorarSeguro: true);
		fraco.Ficha.hakai_mark = true;
		MoveToZone(fraco.Id, alem, naMesa);
		fraco.Pos = naMesa;
		fraco.Ficha.Zeni = Alem.PrecoDoReviveDoEnma * 2 + 7;
		falas = Ouvir(() => EnmaReviverPorZeni(fraco));
		AfirmarGdk("**COM O DOBRO DO PRECO ELE VOLTA**, e o Enma cobrou exatamente o dobro",
				  !fraco.Ficha.dead && Math.Abs(fraco.Ficha.Zeni - 7) < 1e-6 && !fraco.Ficha.hakai_mark, $"zeni {fraco.Ficha.Zeni:0} | {Ultimos(falas)}");
	}

	// =====================================================================
	// 3) O POWER FLICK
	// =====================================================================
	private void OPowerFlickDoDeus()
	{
		GD.Print("[godkit] -- 3) O POWER FLICK: o peteleco (`GodOfDestruction.dm:225-245`) --");
		const int T = ZoneCollision.TileSize;
		(ServerPlayer deus, Vec2 chao) = CenaDoKit();

		ServerPlayer alvo = ForjarGdk("Alvo", chao + new Vec2(0.9f * T, 0), 5e8);
		List<string> falas = ApertarEOuvir(deus, KitDoDeus.VerbFlick);
		AfirmarGdk("sem ninguem nos BRACOS o peteleco nao sai -- encostar nao basta (`if(!M)`, `:229-231`)",
				  Disse(falas, "AGARRANDO") && alvo.TiquesDeVoo == 0, Ultimos(falas));

		ServerPlayer mortal = ForjarGdk("Mortal", chao + new Vec2(0, T), 1e9);
		Prender(mortal, alvo);
		falas = ApertarEOuvir(mortal, KitDoDeus.VerbFlick);
		AfirmarGdk("SEM O TITULO o verb recusa, e o preso continua preso", Disse(falas, "do Deus da Destruição") && QuemEuSeguro(mortal) == alvo, Ultimos(falas));
		Soltar(mortal, MotivoDaSoltura.Tecla);

		Prender(deus, alvo);
		Dictionary<string, double> antes = VidasDe(alvo);
		double esperado = CombatMath.DanoBase(deus.Ficha, alvo.Ficha) * 2;
		falas = ApertarEOuvir(deus, KitDoDeus.VerbFlick);
		double perda = MaiorPerda(alvo, antes);
		AfirmarGdk("**O PETELECO SOLTA**: o aperto se desfaz dos dois lados (`:233-237`)",
				  deus.AgarrandoId == 0 && alvo.AgarradoPorId == 0 && Disse(falas, "PETELECO"), Ultimos(falas));
		AfirmarGdk("**FERE COM DOIS GOLPES PESADOS SEM CARGA** (`NormDamageCalc(M) * 2`, `:238`) -- direto num membro, sem o gap de poder",
				  esperado > 0 && Math.Abs(perda - esperado) < 1e-6, $"perdeu {perda:0.####}, esperado {esperado:0.####}");
		AfirmarGdk("**E ARREMESSA** por nove tiques (`M.kbdur = 9`, `:244`)", alvo.TiquesDeVoo == KitDoDeus.FlickTiquesDeVoo, $"{alvo.TiquesDeVoo} tiques");

		ServerPlayer outro = ForjarGdk("Outro", chao + new Vec2(0.9f * T, T), 5e8);
		Prender(deus, outro);
		antes = VidasDe(outro);
		esperado = CombatMath.DanoBase(deus.Ficha, outro.Ficha) * 2;
		FlickValeUmGolpeDeTeste = true;
		try
		{
			ApertarEOuvir(deus, KitDoDeus.VerbFlick);
			perda = MaiorPerda(outro, antes);
			AfirmarGdk("(defeito injetado: o peteleco vale um golpe) o dano cai pela metade",
					  Math.Abs(perda - esperado / 2) < 1e-6, $"perdeu {perda:0.####}, o certo seria {esperado:0.####}");
		}
		finally { FlickValeUmGolpeDeTeste = false; }
	}

	// =====================================================================
	// 4) O DESTRUCTION GOD'S FURY (o tiro)
	// =====================================================================
	private void OTiroDaFuriaDoDeus()
	{
		GD.Print("[godkit] -- 4) O DESTRUCTION GOD'S FURY: o tiro que derruba a guarda (`GodOfDestruction.dm:251-288`) --");
		const int T = ZoneCollision.TileSize;
		(ServerPlayer deus, Vec2 chao) = CenaDoKit();
		ServerPlayer mortal = ForjarGdk("Mortal", chao + new Vec2(0, T), 1e9);

		List<string> falas = ApertarEOuvir(mortal, KitDoDeus.VerbFuria);
		AfirmarGdk("SEM O TITULO o verb recusa e nao cobra", Disse(falas, "do Deus da Destruição") && TirosDe(mortal).Count == 0
				  && Math.Abs(mortal.Ficha.Ki - mortal.Ficha.MaxKi) < 1e-6, Ultimos(falas));

		// ---------------------------------------------------------------- 4a. a carga
		GD.Print("[godkit] -- 4a) cobra, carrega um terco da velocidade de acao, e so entao a bola nasce (`:263-270`) --");
		double custo = KitDoDeus.FuriaKi * deus.Ficha.BaseDrain();
		AfirmarGdk("(montagem) o tanque do Deus de bancada paga o tiro", custo < deus.Ficha.MaxKi, $"custo {custo:0.##}, MaxKi {deus.Ficha.MaxKi:0.##}");
		deus.Ficha.Ki = custo - 1;
		falas = ApertarEOuvir(deus, KitDoDeus.VerbFuria);
		AfirmarGdk("sem o Ki do custo (`GOD_FURYBEAM_KI * BaseDrain`) o tiro e recusado, e a recusa fala", TirosDe(deus).Count == 0
				  && !CarregandoTiroDoKit(deus.Id) && Disse(falas, "energia"), Ultimos(falas));

		deus.Ficha.Ki = deus.Ficha.MaxKi;
		double ki0 = deus.Ficha.Ki;
		long carga = (long)(KitDoDeus.TiquesDeCarga(deus.Ficha.Eactspeed) * TempoDoDm.MsPorTique);
		ApertarEOuvir(deus, KitDoDeus.VerbFuria);
		AfirmarGdk("**O KI E COBRADO NA HORA** (40 x BaseDrain) **E A BOLA AINDA NAO NASCEU**: a carga corre",
				  Math.Abs(ki0 - deus.Ficha.Ki - custo) < 1e-6 && TirosDe(deus).Count == 0 && CarregandoTiroDoKit(deus.Id),
				  $"cobrou {ki0 - deus.Ficha.Ki:0.##} de {custo:0.##}; carga de {carga} ms (Eactspeed {deus.Ficha.Eactspeed:0.##})");
		AfirmarGdk("a carga e `max(round(Eactspeed / 3), 1)` tiques de 0,1 s (`:269`)",
				  KitDoDeus.TiquesDeCarga(1) == 1 && KitDoDeus.TiquesDeCarga(9) == 3 && KitDoDeus.TiquesDeCarga(30) == 10);
		falas = ApertarEOuvir(deus, KitDoDeus.VerbEsfera);
		AfirmarGdk("com um tiro do kit na mao o outro nao sai (`usr.blasting`, `:262`)", Disse(falas, "tecnica de ki no ar"), Ultimos(falas));

		PulsoDoKitDoDeus();
		AfirmarGdk("antes de a carga vencer o pulso nao solta nada", TirosDe(deus).Count == 0);
		AdiantoDoRelogioDeTeste += carga + 1;
		PulsoDoKitDoDeus();
		List<Projetil> tiros = TirosDe(deus);
		AfirmarGdk("**VENCIDA A CARGA, A BOLA NASCE** -- uma, e a mao fica livre", tiros.Count == 1 && !CarregandoTiroDoKit(deus.Id), $"{tiros.Count} tiro(s)");
		if (tiros.Count == 1)
		{
			Projetil p = tiros[0];
			// (2026-10-23) a ARTE deixou de ser a `12.dmi` do DM: e a esfera do Hakai, a menor (o pedido do dono com a imagem)
			AfirmarGdk("a receita e a do DM: bola, `basedamage` 1,5, sem deflexao, `walk(A, dir, 2)`, 30 tiles, e nao empurra -- e a arte e a "
					  + "FURY DO HAKAI (dono, 2026-10-23), e nao mais a `12.dmi`",
					  p.Tipo == TipoDeProjetil.Blast && Math.Abs(p.BaseDano - 1.5) < 1e-9 && !p.Deflectivel && !p.Empurra
					  && Math.Abs(p.SegundosPorTile - Projetil.AtrasoDeBola(2)) < 1e-9 && Math.Abs(p.MaxDistancia - 30) < 1e-9
					  && p.Arte == ArteDeKi.HakaiFuria,
					  $"{p.Tipo} base {p.BaseDano} defl {p.Deflectivel} emp {p.Empurra} s/tile {p.SegundosPorTile:0.###} alc {p.MaxDistancia} arte {p.Arte}");
			AfirmarGdk("...ela traz a propria zona (`A.selectzone = \"torso\"`, `:279`) e o dom de derrubar a guarda (`godfury`, `:251-256`)",
					  p.ZonaCerta == "torso" && p.Dom == DomDeProjetil.DerrubaAGuarda, $"zona '{p.ZonaCerta}', dom {p.Dom}");
			AfirmarGdk("...e carrega o poder do Deus (`A.BP = usr.expressedBP`, `:277`)", Math.Abs(p.Bp - deus.Ficha.expressedBP) < 1e-6 * deus.Ficha.expressedBP);
		}
		LimparTudoDaBancada([deus, mortal]);

		Projetil? naHora = TiroDoKitNaHora(deus, KitDoDeus.VerbFuria);
		AfirmarGdk("(defeito injetado: o tiro sem carga) a bola nasce no mesmo instante do verb", naHora != null);
		LimparTudoDaBancada([deus, mortal]);

		// ---------------------------------------------------------------- 4b. a guarda cai
		GD.Print("[godkit] -- 4b) onde fere, a guarda cai (`M.blocking = 0`, `:255`) --");
		double bpDeGuarda = BpQueOTiroFere(deus, KitDoDeus.VerbFuria, deGuarda: true);
		ServerPlayer guardado = ForjarGdk("Guardado", chao + new Vec2(4 * T, 0), bpDeGuarda);
		guardado.Facing = Facing.West;
		guardado.Combate.Guardar(true);
		guardado.Combate.ContraPronto = false;   // guarda ja erguida ha tempo: nao e a janela do parry
		double vida0 = guardado.Combate.Corpo.Vida();
		Projetil? tiro = TiroDoKitNaHora(deus, KitDoDeus.VerbFuria);
		falas = Ouvir(() => { for (int i = 0; i < 300 && tiro is { Vivo: true }; i++) TickDosProjeteis(Protocol.TickSeconds); });
		AfirmarGdk("(montagem) o tiro acertou o corpo de guarda erguida e FERIU (passou do corte dos fracos)",
				  tiro is { Vivo: false, Fim: FimDeProjetil.Acertou } && guardado.Combate.Corpo.Vida() < vida0,
				  $"fim {tiro?.Fim}, vida {vida0:0.##} -> {guardado.Combate.Corpo.Vida():0.##}");
		AfirmarGdk("**A GUARDA CAIU**, e o alvo le por que", !guardado.Combate.Bloqueando && Disse(falas, "atravessa a sua guarda"), Ultimos(falas));
		LimparTudoDaBancada([deus]);

		ServerPlayer guardado2 = ForjarGdk("Guardado2", chao + new Vec2(4 * T, 0), bpDeGuarda);
		guardado2.Facing = Facing.West;
		guardado2.Combate.Guardar(true);
		guardado2.Combate.ContraPronto = false;
		TiroDoKitSemDomDeTeste = true;
		try
		{
			tiro = TiroDoKitNaHora(deus, KitDoDeus.VerbFuria);
			for (int i = 0; i < 300 && tiro is { Vivo: true }; i++) TickDosProjeteis(Protocol.TickSeconds);
			AfirmarGdk("(defeito injetado: o tiro sem dom) a mesma bola fere e a guarda continua de pe",
					  tiro is { Vivo: false, Fim: FimDeProjetil.Acertou } && guardado2.Combate.Bloqueando, $"fim {tiro?.Fim}");
		}
		finally { TiroDoKitSemDomDeTeste = false; }
		LimparTudoDaBancada([deus]);

		// ---------------------------------------------------------------- 4c. a zona do tiro
		GD.Print("[godkit] -- 4c) a zona e do TIRO: o Deus mira a cabeca e o feixe continua indo no torso --");
		deus.Combate.ZonaMirada = "cabeca";
		const int rodadas = 200;
		int noTorsoDoFeixe = AcertosNoTorso(deus, KitDoDeus.VerbFuria, rodadas, chao);
		int noTorsoDaEsfera = AcertosNoTorso(deus, KitDoDeus.VerbEsfera, rodadas, chao);
		AfirmarGdk($"**O FEIXE VAI NO TORSO** em mais de 40% dos {rodadas} impactos (a zona pesa o sorteio do membro, como no `DamageLimb`)",
				  noTorsoDoFeixe > rodadas * 0.40, $"{noTorsoDoFeixe} de {rodadas}");
		AfirmarGdk("...e a esfera, que nao escreve zona, segue a mira de quem atira: com o Deus mirando a cabeca, o torso leva bem menos",
				  noTorsoDaEsfera < rodadas * 0.30 && noTorsoDaEsfera < noTorsoDoFeixe, $"{noTorsoDaEsfera} de {rodadas}");
		deus.Combate.ZonaMirada = null;
	}

	/// <summary>Quantos de <paramref name="rodadas"/> impactos de um tiro do kit feriram o TORSO de um corpo inteiro.</summary>
	private int AcertosNoTorso(ServerPlayer deus, string verb, int rodadas, Vec2 chao)
	{
		ServerPlayer saco = ForjarGdk("Saco", chao + new Vec2(3 * ZoneCollision.TileSize, 0), BpQueOTiroFere(deus, verb, deGuarda: false));
		BodyPart torso = saco.Combate.Corpo.Achar("Torso")!;
		double pericia = saco.Ficha.kidefenseskill;
		int noTorso = 0;
		for (int i = 0; i < rodadas; i++)
		{
			saco.Combate.Curar();
			saco.Ficha.kidefenseskill = pericia;   // apanhar de ki treina a defesa (e encolhe o dano): cada rodada mede o mesmo tiro
			double antes = torso.Vida;
			if (TiroDoKitNaHora(deus, verb) is not { } p) continue;
			Acertar(p, saco);
			if (torso.Vida < antes - 1e-9) noTorso++;
			if (p.Vivo) Matar(p, FimDeProjetil.Apagou);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		LimparTudoDaBancada([deus]);
		return noTorso;
	}

	// =====================================================================
	// 5) A DESTROYER SPHERE
	// =====================================================================
	private void AEsferaDoDeus()
	{
		GD.Print("[godkit] -- 5) A DESTROYER SPHERE: o Hakai no contato (`GodOfDestruction.dm:294-331`) --");
		(ServerPlayer deus, Vec2 chao) = CenaDoKit();

		deus.Ficha.Ki = deus.Ficha.MaxKi;
		double custo = KitDoDeus.EsferaKi * deus.Ficha.BaseDrain();
		double ki0 = deus.Ficha.Ki;
		AfirmarGdk("(montagem) o tanque do Deus de bancada paga a esfera", custo < deus.Ficha.MaxKi, $"custo {custo:0.##}, MaxKi {deus.Ficha.MaxKi:0.##}");
		List<string> falas = ApertarEOuvir(deus, KitDoDeus.VerbEsfera);
		AfirmarGdk("o verb cobra 60 x BaseDrain, avisa quem ve (*\"condensa uma esfera de pura DESTRUICAO...\"*, `:312`) e carrega",
				  Math.Abs(ki0 - deus.Ficha.Ki - custo) < 1e-6 && Disse(falas, "condensa uma esfera") && CarregandoTiroDoKit(deus.Id),
				  $"cobrou {ki0 - deus.Ficha.Ki:0.##} de {custo:0.##} | {Ultimos(falas)}");
		AdiantoDoRelogioDeTeste += (long)(KitDoDeus.TiquesDeCarga(deus.Ficha.Eactspeed) * TempoDoDm.MsPorTique) + 1;
		PulsoDoKitDoDeus();
		List<Projetil> tiros = TirosDe(deus);
		AfirmarGdk("a esfera nasce: `basedamage` 2, sem deflexao, sem zona propria, com o dom do HAKAI (`goddestroyer`, `:294-299`) -- e a "
				  + "arte e a ESFERA DO HAKAI (dono, 2026-10-23)",
				  tiros.Count == 1 && Math.Abs(tiros[0].BaseDano - 2) < 1e-9 && !tiros[0].Deflectivel && tiros[0].ZonaCerta.Length == 0
				  && tiros[0].Dom == DomDeProjetil.Hakai && tiros[0].Arte == ArteDeKi.HakaiEsfera,
				  tiros.Count == 1 ? $"base {tiros[0].BaseDano} zona '{tiros[0].ZonaCerta}' dom {tiros[0].Dom} arte {tiros[0].Arte}" : $"{tiros.Count} tiro(s)");
		LimparTudoDaBancada([deus]);

		// O MEMBRO E SORTEADO, entao a prova e por INVARIANTE em varias rodadas: com o golpe nao-letal um impacto no
		// proprio torso o sobe pro piso do nao-letal (acima dos 15%), e ai o Hakai NAO pega -- e e essa a regra:
		// os criterios sao medidos DEPOIS do dano da esfera.
		const int rodadas = 16;
		double bpFraco = BpQueOTiroFere(deus, KitDoDeus.VerbEsfera, deGuarda: false), bpForte = deus.Ficha.BP * 0.9;
		(int apagados, int coerentes, int hakais) = RodadasDeEsfera(deus, chao, rodadas, torso: 0.10, bp: bpFraco);
		AfirmarGdk($"**QUEM A ESFERA FERE COM O TORSO EM 15% OU MENOS E APAGADO** -- em {rodadas} rodadas, morreu quem terminou "
				  + "o impacto dentro do criterio, e so ele",
				  coerentes == rodadas && apagados > 0 && hakais == apagados, $"{apagados} apagado(s), {coerentes} coerente(s), {hakais} HAKAI no chat");

		(apagados, coerentes, _) = RodadasDeEsfera(deus, chao, rodadas, torso: 0.60, bp: bpFraco);
		AfirmarGdk("com o torso em 60% a esfera so fere: ninguem e apagado", apagados == 0 && coerentes == rodadas, $"{apagados} apagado(s)");
		(apagados, _, _) = RodadasDeEsfera(deus, chao, rodadas, torso: 0.10, bp: bpForte);
		AfirmarGdk("e o alvo forte demais (mais de 75% do poder do Deus) tambem nao, de torso em 10%", apagados == 0, $"{apagados} apagado(s)");

		TiroDoKitSemDomDeTeste = true;
		try
		{
			(apagados, _, _) = RodadasDeEsfera(deus, chao, rodadas, torso: 0.10, bp: bpFraco);
			AfirmarGdk("(defeito injetado: o tiro sem dom) a esfera vira bola comum e ninguem e apagado", apagados == 0, $"{apagados} apagado(s)");
		}
		finally { TiroDoKitSemDomDeTeste = false; }

		AArteDoHakai(deus, chao);
	}

	/// <summary>
	/// 5b) A ESFERA DO HAKAI (dono, 2026-10-23: *"faca bills usar skills de hakai q tem esse efeito nos ataques de ki dele como na
	/// imagem"*). As decisoes: "os tiros do kit" (a Destroyer Sphere e a Fury) e "o titulo tambem" (a arte e do TIRO). Entao os
	/// dois tiros saem com a arte nova pros DOIS jeitos de carregar o kit: o titulo (o Deus de bancada esta no trono, como o
	/// jogador que o ganhou) e o corpo sem dono consagrado (como o Bills na luta). Quem desenha e o cliente
	/// (`--diagartedeki` familia 9, `--diaghakaibills`); aqui se mede a arte que o tiro LEVA no anuncio.
	/// </summary>
	private void AArteDoHakai(ServerPlayer deus, Vec2 chao)
	{
		GD.Print("[godkit] -- 5b) A ESFERA DO HAKAI (dono, 2026-10-23): o titulo e o corpo sem dono (o Bills) soltam a MESMA arte --");
		LimparTudoDaBancada([deus]);
		ServerPlayer semDono = ForjarGdk("DeusSemDono", chao + new Vec2(0, 3 * ZoneCollision.TileSize), 1e9, conta: "");
		ConsagrarDeusSemDono(semDono);
		try
		{
			(ArteDeKi Esfera, ArteDeKi Furia) titulo = ArtesDoKit(deus), bills = ArtesDoKit(semDono);
			AfirmarGdk("**OS DOIS TIROS DO KIT SAEM COMO A ESFERA DO HAKAI** -- pra quem tem o TITULO e pro corpo sem dono consagrado (o "
					  + "Bills): a Destroyer Sphere com a `HakaiEsfera`, a Fury com a `HakaiFuria` (a menor)",
					  titulo == (ArteDeKi.HakaiEsfera, ArteDeKi.HakaiFuria) && bills == titulo, $"titulo {titulo}, sem dono {bills}");

			ArteDeProjetil.DardoNosTirosDoDeusDeTeste = true;
			try
			{
				(ArteDeKi Esfera, ArteDeKi Furia) antes = ArtesDoKit(deus), antesDoBills = ArtesDoKit(semDono);
				AfirmarGdk("(defeito injetado: `ArteDeProjetil.DardoNosTirosDoDeusDeTeste`, o jogo de antes) os dois voltam ao dardo `12.dmi` "
						  + "nos dois jeitos -- a mesma regua REPROVA",
						  antes == (ArteDeKi.Blast12, ArteDeKi.Blast12) && antesDoBills == antes, $"titulo {antes}, sem dono {antesDoBills}");
			}
			finally { ArteDeProjetil.DardoNosTirosDoDeusDeTeste = false; }
		}
		finally { ConsagrarDeusSemDono(semDono, consagrado: false); }
		LimparTudoDaBancada([deus]);
	}

	/// <summary>A arte dos dois tiros do kit deste corpo: aperta cada verb sem a carga e le o tiro que nasceu.</summary>
	private (ArteDeKi Esfera, ArteDeKi Furia) ArtesDoKit(ServerPlayer quem)
	{
		ArteDeKi esfera = TiroDoKitNaHora(quem, KitDoDeus.VerbEsfera)?.Arte ?? ArteDeKi.Nenhuma;
		LimparTudoDaBancada(_players.Values.Where(p => p.Id >= IdBaseDeProjetil).ToList());
		ArteDeKi furia = TiroDoKitNaHora(quem, KitDoDeus.VerbFuria)?.Arte ?? ArteDeKi.Nenhuma;
		LimparTudoDaBancada(_players.Values.Where(p => p.Id >= IdBaseDeProjetil).ToList());
		return (esfera, furia);
	}

	/// <summary>
	/// <paramref name="rodadas"/> impactos da esfera, cada um num corpo novo com o torso em <paramref name="torso"/>.
	/// Devolve quantos morreram, em quantas rodadas "morreu" coincidiu com "terminou o impacto com o torso em 15%
	/// ou menos", e quantas vezes o HAKAI foi dito.
	/// </summary>
	private (int Apagados, int Coerentes, int Hakais) RodadasDeEsfera(ServerPlayer deus, Vec2 chao, int rodadas, double torso, double bp)
	{
		int apagados = 0, coerentes = 0, hakais = 0;
		for (int i = 0; i < rodadas; i++)
		{
			ServerPlayer v = ForjarGdk("Vitima", chao + new Vec2(3 * ZoneCollision.TileSize, 0), bp);
			TorsoEm(v, torso);
			if (TiroDoKitNaHora(deus, KitDoDeus.VerbEsfera) is not { } p) continue;
			List<string> falas = Ouvir(() => Acertar(p, v));
			if (p.Vivo) Matar(p, FimDeProjetil.Apagou);
			TickDosProjeteis(Protocol.TickSeconds);

			bool dentro = FracaoDoTorso(v) <= KitDoDeus.HakaiTorsoPct / 100 + 1e-9;
			bool forteDemais = v.Ficha.expressedBP > deus.Ficha.expressedBP * KitDoDeus.HakaiFracaoDeBp;
			if (v.Ficha.dead) apagados++;
			if (v.Ficha.dead == (dentro && !forteDemais)) coerentes++;
			if (Disse(falas, "HAKAI")) hakais++;
			LimparTudoDaBancada([deus]);
		}
		return (apagados, coerentes, hakais);
	}

	// =====================================================================
	// 6) A ENERGY NULLIFICATION
	// =====================================================================
	private void AAnulacaoDoDeus()
	{
		GD.Print("[godkit] -- 6) A ENERGY NULLIFICATION: o que some e o que fica (`GodOfDestruction.dm:336-357`) --");
		const int T = ZoneCollision.TileSize;
		(ServerPlayer deus, Vec2 chao) = CenaDoKit(tiles: 30);
		ServerPlayer mortal = ForjarGdk("Mortal", chao + new Vec2(0, T), 1e9);
		ServerPlayer fraco = ForjarGdk("AtiradorFraco", chao + new Vec2(5 * T, 0), 5e8);
		ServerPlayer forte = ForjarGdk("AtiradorForte", chao + new Vec2(6 * T, 0), 5e9);
		var bola = new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 60, Deflectivel = false };
		var treino = new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 0, AlcanceTiles = 1000, Empurra = false };
		Vec2 praLonge = new(1, 0), praCima = new(-1, 0);

		List<string> falas = ApertarEOuvir(deus, KitDoDeus.VerbAnular);
		AfirmarGdk("sem nada no ar a anulacao nao gasta: *\"so consome o poder se anular algo\"* (`:357`)", Disse(falas, "nenhum ataque anulável"), Ultimos(falas));
		falas = ApertarEOuvir(deus, KitDoDeus.VerbAnular);
		AfirmarGdk("...e por isso da pra tentar de novo na hora (a recarga NAO armou, `if(n)`, `:351`)", Disse(falas, "nenhum ataque anulável"), Ultimos(falas));

		AnulacaoSempreCobraDeTeste = true;
		try
		{
			ApertarEOuvir(deus, KitDoDeus.VerbAnular);
			falas = ApertarEOuvir(deus, KitDoDeus.VerbAnular);
			AfirmarGdk("(defeito injetado: a recarga arma sempre) a segunda tentativa no vazio ja esbarra nos cinco minutos", Disse(falas, "retorna em"), Ultimos(falas));
		}
		finally { AnulacaoSempreCobraDeTeste = false; }
		_kitDoDeus.Remove(deus.Id);

		// O DEFEITO PRIMEIRO, num Deus a parte: a bola do forte some.
		AnulacaoApagaOsFortesDeTeste = true;
		try
		{
			Projetil cobaia = Disparar(forte, bola, rumoDado: praLonge, deOnde: chao + new Vec2(4 * T, 0));
			AfirmarGdk("(montagem) a bola do forte carrega MAIS poder que o Deus", cobaia.Vivo && cobaia.Bp > deus.Ficha.expressedBP);
			ApertarEOuvir(deus, KitDoDeus.VerbAnular);
			AfirmarGdk("(defeito injetado: apaga os fortes) o ataque com MAIS poder que o Deus tambem some", !cobaia.Vivo);
		}
		finally { AnulacaoApagaOsFortesDeTeste = false; }
		_kitDoDeus.Remove(deus.Id);
		LimparTudoDaBancada([deus, mortal, fraco, forte]);

		// AS BOLAS: paradas onde nascem (a bancada nao anda o tique dos projeteis).
		Projetil perto1 = Disparar(fraco, bola, rumoDado: praLonge, deOnde: chao + new Vec2(3 * T, 0));
		Projetil perto2 = Disparar(fraco, bola, rumoDado: praLonge, deOnde: chao + new Vec2(8 * T, 0));
		Projetil doForte = Disparar(forte, bola, rumoDado: praLonge, deOnde: chao + new Vec2(4 * T, 0));
		Projetil longe = Disparar(fraco, bola, rumoDado: praLonge, deOnde: chao + new Vec2(14 * T, 0));
		Projetil vindo = Disparar(fraco, bola, rumoDado: praCima, deOnde: chao + new Vec2(14 * T, 0));
		Projetil meu = Disparar(deus, bola, rumoDado: praLonge, deOnde: chao + new Vec2(2 * T, 0));
		Projetil alvoDeTreino = Disparar(fraco, treino, rumoDado: Vec2.Zero, deOnde: chao + new Vec2(2 * T, T));
		alvoDeTreino.Inerte = true;
		AfirmarGdk("(montagem) sete bolas no ar: duas fracas perto (a 3 e a 8 tiles), a do forte, uma fraca longe indo embora, "
				  + "uma fraca longe VINDO, a do proprio Deus e um alvo de treino",
				  new[] { perto1, perto2, doForte, longe, vindo, meu, alvoDeTreino }.All(p => p.Vivo)
				  && doForte.Bp > deus.Ficha.expressedBP && perto1.Bp < deus.Ficha.expressedBP);

		falas = ApertarEOuvir(mortal, KitDoDeus.VerbAnular);
		AfirmarGdk("SEM O TITULO o verb recusa e nada some", Disse(falas, "do Deus da Destruição") && perto1.Vivo, Ultimos(falas));

		List<int> baques = [];
		EscutaDeBaquesDoKit = baques;
		try { falas = ApertarEOuvir(deus, KitDoDeus.VerbAnular); }
		finally { EscutaDeBaquesDoKit = null; }
		AfirmarGdk("**OS ATAQUES MAIS FRACOS, A ATE 8 TILES, DEIXAM DE EXISTIR** -- sem estouro (`A.loc = null`, `:347-349`)",
				  perto1 is { Vivo: false, Fim: FimDeProjetil.Apagou } && perto2 is { Vivo: false, Fim: FimDeProjetil.Apagou },
				  $"a 3 tiles: {perto1.Fim}; a 8 tiles: {perto2.Fim}");
		AfirmarGdk("**O ATAQUE COM MAIS PODER QUE O DEUS FICA** (`if(A.BP < usr.expressedBP)`, `:346`)", doForte.Vivo);
		AfirmarGdk("o ataque do proprio Deus fica (`if(A.proprietor == usr) continue`, `:345`), e o alvo de treino tambem (nao e `/obj/attack`)",
				  meu.Vivo && alvoDeTreino.Vivo);
		AfirmarGdk("o ataque fraco a 14 tiles, indo embora, fica (fora do `view(8)`)", longe.Vivo);
		AfirmarGdk("...e o que VEM pra cima do Deus de 14 tiles some: e o aviso que 8 tiles davam na velocidade do DM "
				  + "(regra da casa de 2026-10-08, `Projetil.VemPraCimaDe`)", !vindo.Vivo);
		AfirmarGdk("quem ve le a conta (3 ataques) e o baque e em volta do Deus (`createShockwavemisc(usr.loc, 3)`, `:353`)",
				  Disse(falas, "3 ataque(s)") && Disse(falas, "DEIXAM DE EXISTIR") && baques.Contains(deus.Id), Ultimos(falas));

		falas = ApertarEOuvir(deus, KitDoDeus.VerbAnular);
		AfirmarGdk("**AGORA A RECARGA ARMOU**: cinco minutos reais (`GOD_NULLIFY_CD`, `:352`)", Disse(falas, "retorna em"), Ultimos(falas));
		AdiantoDoRelogioDeTeste += (long)(KitDoDeus.AnulacaoRecargaSegundos * 1000) - 2000;
		falas = ApertarEOuvir(deus, KitDoDeus.VerbAnular);
		AfirmarGdk("...a dois segundos do fim ela ainda recusa", Disse(falas, "retorna em"), Ultimos(falas));
		AdiantoDoRelogioDeTeste += 2001;
		falas = ApertarEOuvir(deus, KitDoDeus.VerbAnular);
		AfirmarGdk("...e vencidos os cinco minutos o verb volta (nada fraco no ar: a frase e a do vazio)", Disse(falas, "nenhum ataque anulável"), Ultimos(falas));

		deus.Combate.Nocautear(60);
		Projetil outra = Disparar(fraco, bola, rumoDado: praLonge, deOnde: chao + new Vec2(3 * T, 0));
		falas = ApertarEOuvir(deus, KitDoDeus.VerbAnular);
		AfirmarGdk("NOCAUTEADO o Deus nao anula nada (o que este port cobra a mais que o DM: o corpo caido)", outra.Vivo, Ultimos(falas));
		deus.Combate.Levantar();
	}

	// =====================================================================
	// 7) O GOD'S TEMPER
	// =====================================================================
	private void OSurtoDoDeus()
	{
		GD.Print("[godkit] -- 7) O GOD'S TEMPER: dez danos sem guarda (`GodOfDestruction.dm:363-383`) --");
		const int T = ZoneCollision.TileSize;

		AfirmarGdk("a regra pura: dispara no DECIMO dano, e nunca com o surto de pe nem na recarga (`:366`)",
				  KitDoDeus.TemperDispara(10, false, false) && !KitDoDeus.TemperDispara(9, false, false)
				  && !KitDoDeus.TemperDispara(10, true, false) && !KitDoDeus.TemperDispara(25, false, true));

		(ServerPlayer deus, Vec2 chao) = CenaDoKit();
		ServerPlayer agressor = ForjarGdk("Agressor", chao + new Vec2(0.9f * T, 0), 1e9);
		ServerPlayer mortal = ForjarGdk("Mortal", chao + new Vec2(0, T), 1e9);
		void Bater(ServerPlayer em, int vezes)
		{
			for (int i = 0; i < vezes; i++) FerirUmMembroG10(em, agressor, 0.01, letal: false);
		}

		// ---------------------------------------------------------------- 7a. o decimo dano
		double off0 = deus.Ficha.Tphysoff, def0 = deus.Ficha.Tphysdef;
		Bater(deus, 9);
		AfirmarGdk("NOVE danos sem guarda: nada ainda", !TemBuff(deus, KitDoDeus.IdDoTemper) && Math.Abs(deus.Ficha.Tphysoff - off0) < 1e-12);
		double somaOff = deus.Ficha.Ephysoff * KitDoDeus.TemperPct, somaDef = deus.Ficha.Ephysdef * KitDoDeus.TemperPct;
		List<string> falas = Ouvir(() => Bater(deus, 1));
		AfirmarGdk("**O DECIMO TRANSBORDA**: quem ve le a ira do Deus (`:375`)", TemBuff(deus, KitDoDeus.IdDoTemper) && Disse(falas, "ira do Deus transborda"), Ultimos(falas));
		AfirmarGdk("**+10% DO ATAQUE E DA DEFESA FISICA EFETIVOS**, somados nos temporarios (`Tphysoff += Ephysoff * GOD_TEMPER_PCT`, `:370-373`)",
				  somaOff > 0 && Math.Abs(deus.Ficha.Tphysoff - off0 - somaOff) < 1e-9 && Math.Abs(deus.Ficha.Tphysdef - def0 - somaDef) < 1e-9,
				  $"Tphysoff {off0:0.####} -> {deus.Ficha.Tphysoff:0.####} (+{somaOff:0.####}); Tphysdef {def0:0.####} -> {deus.Ficha.Tphysdef:0.####} (+{somaDef:0.####})");

		Bater(deus, 12);
		AfirmarGdk("com o surto de pe, mais doze danos nao empilham outro", Math.Abs(deus.Ficha.Tphysoff - off0 - somaOff) < 1e-9);

		// ---------------------------------------------------------------- 7b. dez segundos, e sai exato
		AdiantoDoRelogioDeTeste += (long)(KitDoDeus.TemperSegundos * 1000) - 500;
		TickDosBuffs();
		AfirmarGdk("aos 9,5 s o surto continua de pe", TemBuff(deus, KitDoDeus.IdDoTemper));
		AdiantoDoRelogioDeTeste += 600;
		falas = Ouvir(TickDosBuffs);
		AfirmarGdk("**AOS DEZ SEGUNDOS ELE ACABA, E SAI EXATAMENTE O QUE ENTROU** (`Tphysoff -= god_temper_off`, `:378-379`)",
				  !TemBuff(deus, KitDoDeus.IdDoTemper) && Math.Abs(deus.Ficha.Tphysoff - off0) < 1e-9 && Math.Abs(deus.Ficha.Tphysdef - def0) < 1e-9,
				  $"Tphysoff {deus.Ficha.Tphysoff:0.####}, Tphysdef {deus.Ficha.Tphysdef:0.####} | {Ultimos(falas)}");

		// ---------------------------------------------------------------- 7c. a recarga de 30 s
		Bater(deus, 3);
		AfirmarGdk("na RECARGA (30 s do inicio do surto, `:369`) o contador ja passou de dez e nada dispara", !TemBuff(deus, KitDoDeus.IdDoTemper));
		AdiantoDoRelogioDeTeste += (long)((KitDoDeus.TemperRecargaSegundos - KitDoDeus.TemperSegundos) * 1000);
		Bater(deus, 1);
		AfirmarGdk("**VENCIDA A RECARGA, O PRIMEIRO DANO SEM GUARDA DISPARA DE NOVO** (o contador nao zerou na espera)", TemBuff(deus, KitDoDeus.IdDoTemper));

		// ---------------------------------------------------------------- 7d. a guarda zera
		GD.Print("[godkit] -- 7d) a guarda zera o contador (`if(blocking) { god_temper_hits = 0; return }`, `:364`) --");
		ServerPlayer npc = ForjarGdk("DeusSemDono", chao + new Vec2(4 * T, 0), 1e9, conta: "");
		ConsagrarDeusSemDono(npc);
		Bater(npc, 9);
		npc.Combate.Guardar(true);
		Bater(npc, 1);
		npc.Combate.Guardar(false);
		Bater(npc, 9);
		AfirmarGdk("nove sem guarda, UM de guarda erguida, mais nove sem guarda: nada (a guarda zerou a conta)", !TemBuff(npc, KitDoDeus.IdDoTemper));
		Bater(npc, 1);
		AfirmarGdk("...e o decimo SEGUIDO sem guarda dispara -- num corpo SEM DONO consagrado", TemBuff(npc, KitDoDeus.IdDoTemper));

		ServerPlayer npc2 = ForjarGdk("DeusSemDono2", chao + new Vec2(6 * T, 0), 1e9, conta: "");
		ConsagrarDeusSemDono(npc2);
		npc2.Combate.Guardar(true);
		Bater(npc2, 12);
		AfirmarGdk("doze danos de guarda erguida o tempo todo: nada", !TemBuff(npc2, KitDoDeus.IdDoTemper));
		TemperContaComGuardaDeTeste = true;
		try
		{
			Bater(npc2, 10);
			AfirmarGdk("(defeito injetado: a guarda nao zera) quem apanha bloqueando tambem transborda", TemBuff(npc2, KitDoDeus.IdDoTemper));
		}
		finally { TemperContaComGuardaDeTeste = false; }

		// ---------------------------------------------------------------- 7e. quem conta
		Bater(mortal, 20);
		AfirmarGdk("SEM O TITULO, vinte danos nao sao nada (`if(M.god_temper_armed)`, `calcs.dm:181`)", !TemBuff(mortal, KitDoDeus.IdDoTemper));

		ServerPlayer npc3 = ForjarGdk("DeusSemDono3", chao + new Vec2(8 * T, 0), 1e9, conta: "");
		ConsagrarDeusSemDono(npc3);
		var golpe = new GolpeResultado { Desfecho = Desfecho.Acertou, Dano = 0.01, Membro = "Torso" };
		for (int i = 0; i < 10; i++) ResolverDesfecho(agressor, npc3, golpe);
		AfirmarGdk("**O SOCO E O TIRO CONTAM** (a cauda `ResolverDesfecho`, divergencia declarada do `damage_m`): dez golpes que encostam disparam o surto",
				  TemBuff(npc3, KitDoDeus.IdDoTemper));
		ServerPlayer npc4 = ForjarGdk("DeusSemDono4", chao + new Vec2(10 * T, 0), 1e9, conta: "");
		ConsagrarDeusSemDono(npc4);
		var esquiva = new GolpeResultado { Desfecho = Desfecho.Esquivou, Membro = "" };
		for (int i = 0; i < 12; i++) ResolverDesfecho(agressor, npc4, esquiva);
		AfirmarGdk("...e o golpe que NAO encostou (esquiva) nao conta", !TemBuff(npc4, KitDoDeus.IdDoTemper));

		// ---------------------------------------------------------------- 7f. o titulo cai
		_tronos.Remove(KitDoDeus.Cargo);
		ReconciliarDadiva(deus);
		AfirmarGdk("perdido o titulo, o contador e a recarga somem com ele (`god_temper_armed = 0`, `:142`)", !_kitDoDeus.ContainsKey(deus.Id));
	}

	// =====================================================================
	// FERRAMENTAS DAS SECOES 8 A 10
	// =====================================================================
	/// <summary>Um corpo que TRILHOU uma disciplina, com a maestria e a energia pedidas.</summary>
	private ServerPlayer ForjarDisciplinado(string nome, Vec2 onde, TipoDeDisciplina tipo, double real = 100, double atual = 100,
										   double bp = 1e9, string conta = ContaDoMortalDeBancada)
	{
		ServerPlayer pl = ForjarGdk(nome, onde, bp, conta: conta);
		EstadoDeDisciplina est = EstadoDe(pl, tipo);
		est.Aprendida = true;
		est.Real = real;
		est.Atual = atual;
		pl.Disciplina = tipo;
		AplicarDisciplina(pl);
		return pl;
	}

	/// <summary>Poe o corpo numa forma pelo estado (sem cena, sem renovar energia): so o que as passivas perguntam.</summary>
	private void VestirForma(ServerPlayer pl, string id)
	{
		if (id != Catalogo.IdBase) pl.Forma.Liberar(id);
		pl.Forma.Entrar(id);
		AplicarForma(pl);
		AplicarDisciplina(pl);
	}

	/// <summary>Quanto de vida os golpes diretos tiraram, somados (ver <see cref="PerdasDiretas"/>).</summary>
	private static double SomaDasPerdas(ServerPlayer pl, Dictionary<string, double> antes) => PerdasDiretas(pl, antes).Sum();

	/// <summary>Quantos membros levaram golpe direto.</summary>
	private static int MembrosFeridos(ServerPlayer pl, Dictionary<string, double> antes) =>
		PerdasDiretas(pl, antes).Count(perda => perda > 1e-9);

	/// <summary>
	/// O dano de cada bola da <see cref="TirosEvitados"/>: tres vezes o antigo corte por dano
	/// (`DanoDeKi.CorteDoFraco`), e menos de um terco de um membro inteiro -- fere sempre e nao derruba ninguem.
	/// (Hoje "fere sempre" pede so que o alvo nao seja parede pro tiro, `KiContraCorpo`: quem atira nessa prova tem
	/// dez vezes o poder dele.)
	/// </summary>
	private const double DanoDaBolaDeMedida = 30;

	/// <summary>
	/// <paramref name="tiros"/> bolas de um atirador, uma a uma, direto no impacto: devolve quantas o alvo ATRAVESSOU
	/// SEM PERDER VIDA (a esquiva do Instinto).
	///
	/// A BOLA E CALIBRADA PRA FERIR, e a pericia de defesa volta ao que era a cada tiro. Apanhar de ki treina a
	/// defesa (`kidefenseskill += 0.1` por impacto) e a guarda divide o dano pela pericia DUAS vezes
	/// (`DanoDeKi.Final`): depois de 400 bolas o tiro aparado caia abaixo do corte dos fracos, nao feria, e "nao
	/// perdeu vida" deixava de querer dizer "desviou" -- a prova da guarda reprovava a regra certa (120 de 120).
	/// (Isso foi com o corte por dano, que valeu ate 2026-10-12. A pericia continua voltando ao que era a cada tiro:
	/// e ela que faz as 400 bolas serem a MESMA bola.)
	/// </summary>
	private int TirosEvitados(ServerPlayer alvo, ServerPlayer atirador, int tiros)
	{
		bool deGuarda = alvo.Combate.Bloqueando;
		double pericia = alvo.Ficha.kidefenseskill;

		ReceitaDeProjetil Bola(double baseDano) =>
			new() { Tipo = TipoDeProjetil.Blast, BaseDano = baseDano, Velocidade = 1, AlcanceTiles = 30, Deflectivel = false };

		// a conta e a de producao, com o tiro de verdade na mao: o dano final e linear na base (sem teto)
		Projetil sonda = Disparar(atirador, Bola(1));
		double porUnidade = DanoDeKi.Final(sonda.ModsAgora(), sonda.BaseDano, sonda.MaxDano, sonda.Bp, alvo.Combate, deGuarda, sonda.Fisico);
		Matar(sonda, FimDeProjetil.Apagou);
		TickDosProjeteis(Protocol.TickSeconds);
		ReceitaDeProjetil bola = Bola(DanoDaBolaDeMedida / Math.Max(porUnidade, 1e-12));

		int evitados = 0;
		for (int i = 0; i < tiros; i++)
		{
			alvo.Combate.Curar();
			alvo.Ficha.kidefenseskill = pericia;
			if (deGuarda) alvo.Combate.Guardar(true);
			double vida = alvo.Combate.Corpo.Vida();
			Projetil p = Disparar(atirador, bola);
			Acertar(p, alvo);
			if (alvo.Combate.Corpo.Vida() >= vida - 1e-9) evitados++;
			if (p.Vivo) Matar(p, FimDeProjetil.Apagou);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		alvo.Combate.Curar();
		alvo.Ficha.kidefenseskill = pericia;
		return evitados;
	}

	// =====================================================================
	// 8) OS CAMINHOS DO DEUS
	// =====================================================================
	private void OsCaminhosDoDeus()
	{
		GD.Print("[godkit] -- 8) OS CAMINHOS: o que o titulo desperta, amplifica e empresta (`GodOfDestruction.dm:93-102`, `UltraEgo.dm:188-208`) --");
		const int T = ZoneCollision.TileSize;
		LimparTudoDaBancada();
		_tronos.Remove(KitDoDeus.Cargo);
		Vec2 chao = CorredorLivre(24);

		// ---------------------------------------------------------------- 8a. sem caminho: o titulo desperta a Destruicao
		ServerPlayer semCaminho = ForjarGdk("DeusSemCaminho", chao, 1e9, conta: ContaDoDeusDeBancada);
		_tronoVistoPeloKit = "";
		_tronos[KitDoDeus.Cargo] = ContaDoDeusDeBancada;
		List<string> falas = Ouvir(() => ReconciliarDadiva(semCaminho));
		AfirmarGdk("**O TITULO DESPERTA O PODER DA DESTRUICAO em quem nao trilha caminho nenhum** (`if(!M.ue_learned && !M.ui_learned)`, `:95-98`)",
				  semCaminho.PoderDaDestruicao.Aprendida && !semCaminho.UltraInstinct.Aprendida && Disse(falas, "Poder da Destruição desperta"),
				  Ultimos(falas));
		AfirmarGdk("...e ele ouve o que e ser o Deus (`:105`), sem as frases dos outros dois caminhos",
				  Disse(falas, "DEUS DA DESTRUIÇÃO") && !Disse(falas, "o trono reconhece") && !Disse(falas, "canaliza a Destruição"), Ultimos(falas));
		AfirmarGdk("...com o kit AMPLIFICADO: fator 1,25 (`ue_kit_factor`, `UltraEgo.dm:190`)",
				  Math.Abs(FatorDoKitDaDestruicao(semCaminho) - 1.25) < 1e-9, $"{FatorDoKitDaDestruicao(semCaminho):0.###}");
		falas = Ouvir(() => { ReconciliarDadiva(semCaminho); ReconciliarDadiva(semCaminho); });
		AfirmarGdk("a reconciliacao e idempotente: de novo, nao fala nada", !Disse(falas, "desperta") && !Disse(falas, "DEUS DA DESTRUIÇÃO"), Ultimos(falas));

		_tronos.Remove(KitDoDeus.Cargo);
		ReconciliarDadiva(semCaminho);
		AfirmarGdk("**PERDER O TITULO NAO DESAPRENDE** (`UltraEgo.dm:7-8`): a Destruicao fica, e o fator volta a 1",
				  semCaminho.PoderDaDestruicao.Aprendida && Math.Abs(FatorDoKitDaDestruicao(semCaminho) - 1) < 1e-9);

		// ---------------------------------------------------------------- 8b. quem ja trilha a Destruicao
		falas = Ouvir(() => { _tronos[KitDoDeus.Cargo] = ContaDoDeusDeBancada; ReconciliarDadiva(semCaminho); });
		AfirmarGdk("coroado de novo, quem JA trilha a Destruicao ouve que o trono a amplifica em 25% (`:101-102`)",
				  Disse(falas, "o trono reconhece o seu caminho") && Disse(falas, "25%") && !Disse(falas, "desperta em você"), Ultimos(falas));
		semCaminho.PoderDaDestruicao.Real = 100;
		semCaminho.PoderDaDestruicao.Atual = 100;
		ApertarEOuvir(semCaminho, "ue_toggle");
		AfirmarGdk("**A AURA DO DEUS QUE TRILHA A DESTRUICAO REDUZ 31,25%** (25% x 1,25) -- e a de quem so a trilha, 25%",
				  Math.Abs(semCaminho.Combate.ReducaoDeDano - 31.25) < 1e-6, $"{semCaminho.Combate.ReducaoDeDano:0.###}%");
		_tronos.Remove(KitDoDeus.Cargo);
		ReconciliarDadiva(semCaminho);
		AplicarDisciplina(semCaminho);
		AfirmarGdk("...sem o titulo a mesma aura volta aos 25%", Math.Abs(semCaminho.Combate.ReducaoDeDano - 25) < 1e-6, $"{semCaminho.Combate.ReducaoDeDano:0.###}%");

		// ---------------------------------------------------------------- 8c. quem trilha o Instinto: o kit emprestado
		GD.Print("[godkit] -- 8c) o Deus que trilha o INSTINTO: a aura e a infusao emprestadas a 25% (`:99-100`, `:134-138`) --");
		LimparTudoDaBancada();
		ServerPlayer doInstinto = ForjarDisciplinado("DeusDoInstinto", chao, TipoDeDisciplina.UltraInstinct, conta: ContaDoDeusDeBancada);
		ServerPlayer mortalDoInstinto = ForjarDisciplinado("MortalDoInstinto", chao + new Vec2(0, 3 * T), TipoDeDisciplina.UltraInstinct);

		falas = ApertarEOuvir(doInstinto, KitDoDeus.VerbAuraEmprestada);
		AfirmarGdk("antes do titulo o emprestimo nao existe: o verb recusa", !doInstinto.PoderDaDestruicao.Ligada && Disse(falas, "do Deus da Destruição"), Ultimos(falas));

		_tronoVistoPeloKit = "";
		falas = Ouvir(() => { _tronos[KitDoDeus.Cargo] = ContaDoDeusDeBancada; ReconciliarDadiva(doInstinto); });
		AfirmarGdk("**COROADO, QUEM TRILHA O INSTINTO NAO GANHA A DESTRUICAO: ELE A PEGA EMPRESTADA** -- e ouve isso",
				  !doInstinto.PoderDaDestruicao.Aprendida && Disse(falas, "canaliza a Destruição") && Disse(falas, "25%")
				  && Math.Abs(FatorDoKitDaDestruicao(doInstinto) - 0.25) < 1e-9, $"fator {FatorDoKitDaDestruicao(doInstinto):0.###} | {Ultimos(falas)}");
		AfirmarGdk("o fato que mostra os dois botoes emprestados vale pra ele, e so pra ele",
				  FatosDe(doInstinto).HasFlag(Jandirus.Core.Verbos.Fatos.KitDaDestruicaoEmprestado)
				  && !FatosDe(mortalDoInstinto).HasFlag(Jandirus.Core.Verbos.Fatos.KitDaDestruicaoEmprestado));

		falas = ApertarEOuvir(mortalDoInstinto, KitDoDeus.VerbAuraEmprestada);
		AfirmarGdk("quem trilha o Instinto SEM o titulo nao tem aura emprestada", !mortalDoInstinto.PoderDaDestruicao.Ligada && Disse(falas, "do Deus da Destruição"), Ultimos(falas));

		falas = ApertarEOuvir(doInstinto, KitDoDeus.VerbAuraEmprestada);
		AfirmarGdk("**A AURA EMPRESTADA LIGA**, diz que e canalizada pelo titulo, e reduz 6,25% (25% x 0,25) pela PRECISAO do Instinto",
				  doInstinto.PoderDaDestruicao.Ligada && Disse(falas, "Canalizada pelo título")
				  && Math.Abs(doInstinto.Combate.ReducaoDeDano - 6.25) < 1e-6, $"{doInstinto.Combate.ReducaoDeDano:0.###}% | {Ultimos(falas)}");
		doInstinto.Combate.EntrarEmCombate();
		TickDasDisciplinas(doInstinto, 1.0);
		AfirmarGdk("...ela DRENA A PRECISAO do Instinto (0,5/s, `UltraEgo.dm:327-328`) e nao ensina Destruicao nenhuma (`:167`)",
				  Math.Abs(doInstinto.UltraInstinct.Atual - 99.5) < 1e-6 && doInstinto.PoderDaDestruicao.Real == 0,
				  $"precisao {doInstinto.UltraInstinct.Atual:0.###}, Destruicao real {doInstinto.PoderDaDestruicao.Real:0.###}");

		doInstinto.UltraInstinct.Real = 79;
		falas = ApertarEOuvir(doInstinto, KitDoDeus.VerbInfusaoEmprestada);
		AfirmarGdk("a Hakai Infusion emprestada pede a faixa de 80% -- medida no INSTINTO (`ue_eff_real`, `:201-202`): com 79 nao acende",
				  doInstinto.HakaiAte == 0, Ultimos(falas));
		doInstinto.UltraInstinct.Real = 80;
		doInstinto.Ficha.Ki = doInstinto.Ficha.MaxKi;
		ApertarEOuvir(doInstinto, KitDoDeus.VerbInfusaoEmprestada);
		AfirmarGdk("...e com 80 acende, com o corte da deflexao a um quarto do inteiro (`:257`): 40% vira 35%",
				  doInstinto.HakaiAte > NowMs() && Math.Abs(DeflexaoContraAInfusao(doInstinto, 40) - 35) < 1e-9,
				  $"{DeflexaoContraAInfusao(doInstinto, 40):0.###}");

		_tronos.Remove(KitDoDeus.Cargo);
		ReconciliarDadiva(doInstinto);
		AfirmarGdk("**O KIT EMPRESTADO MORRE COM O TITULO** (`god_strip_powers`, `:145-148`): a aura desliga, a infusao apaga, a reducao zera",
				  !doInstinto.PoderDaDestruicao.Ligada && doInstinto.HakaiAte == 0 && doInstinto.Combate.ReducaoDeDano == 0
				  && FatorDoKitDaDestruicao(doInstinto) == 0,
				  $"ligada {doInstinto.PoderDaDestruicao.Ligada}, infusao {doInstinto.HakaiAte}, reducao {doInstinto.Combate.ReducaoDeDano:0.##}");

		ServerPlayer daDestruicao = ForjarDisciplinado("DeusDaDestruicao", chao + new Vec2(3 * T, 0), TipoDeDisciplina.PoderDaDestruicao, conta: "bancada_godkit_outro");
		_tronos[KitDoDeus.Cargo] = "bancada_godkit_outro";
		falas = ApertarEOuvir(daDestruicao, KitDoDeus.VerbAuraEmprestada);
		AfirmarGdk("o Deus que trilha a DESTRUICAO nao usa os verbs emprestados (ele tem os da disciplina dele)",
				  !daDestruicao.PoderDaDestruicao.Ligada && Disse(falas, "a quem trilha o Instinto"), Ultimos(falas));
		_tronos.Remove(KitDoDeus.Cargo);

		// ---------------------------------------------------------------- 8d. o Anjo
		GD.Print("[godkit] -- 8d) o Anjo sempre possui o Ultra Instinto (`UltraInstinct.dm:99-101`) --");
		ServerPlayer anjo = ForjarGdk("Anjo", chao + new Vec2(6 * T, 0), 1e9, conta: "bancada_godkit_anjo");
		falas = Ouvir(() => { _tronos["angel"] = "bancada_godkit_anjo"; ReconciliarDadiva(anjo); });
		AfirmarGdk("**O CARGO DE ANJO ENTREGA O ULTRA INSTINTO** a quem nao trilha caminho nenhum, e ele passa a poder liga-lo",
				  anjo.UltraInstinct.Aprendida && !anjo.PoderDaDestruicao.Aprendida && Disse(falas, "como Anjo"), Ultimos(falas));
		falas = ApertarEOuvir(anjo, "ui_toggle");
		AfirmarGdk("...o toggle que antes respondia \"voce nao domina\" agora liga", anjo.UltraInstinct.Ligada, Ultimos(falas));
		ServerPlayer anjoDaDestruicao = ForjarDisciplinado("AnjoDaDestruicao", chao + new Vec2(9 * T, 0), TipoDeDisciplina.PoderDaDestruicao, conta: "bancada_godkit_anjob");
		_tronos["angel"] = "bancada_godkit_anjob";
		ReconciliarDadiva(anjoDaDestruicao);
		AfirmarGdk("os caminhos sao exclusivos: o Anjo que ja trilha a Destruicao NAO recebe o Instinto (`&& !ue_learned`, `:99`)",
				  !anjoDaDestruicao.UltraInstinct.Aprendida);

		// ---------------------------------------------------------------- 8e. o lembrete do login
		GD.Print("[godkit] -- 8e) o login lembra o Deus da tarefa pendente (`god_login_check`, `GodOfDestruction.dm:168-171`) --");
		// (a tarefa de producao e fotografada e devolvida: a bancada escreve nos campos de verdade, so na memoria)
		(string alvo0, string nome0, long prazo0, string chave0, string mundo0) =
			(_tarefaAlvo, _tarefaNomeDoAlvo, _tarefaPrazo, _tarefaPlanetaChave, _tarefaPlanetaNome);
		try
		{
			LimparTudoDaBancada();
			_tronos[KitDoDeus.Cargo] = ContaDoDeusDeBancada;
			_tronoVistoPeloKit = ContaDoDeusDeBancada;   // o trono ja era dele: isto e um LOGIN, e nao uma coroacao
			_tarefaPlanetaChave = _tarefaPlanetaNome = "";
			_tarefaAlvo = "bancada_godkit_vilao";
			_tarefaNomeDoAlvo = "Vilao";
			_tarefaPrazo = NowMs() + 2 * 3600_000L + 5 * 60_000L + 30_000L;

			ServerPlayer deusQueVolta = ForjarGdk("DeusQueVolta", chao, 1e9, conta: ContaDoDeusDeBancada);
			LoginCalaATarefaDeTeste = true;
			try
			{
				falas = Ouvir(() => ReconciliarDadiva(deusQueVolta));
				AfirmarGdk("(defeito injetado: o login cala a tarefa) o Deus entra no mundo e ninguem lhe diz o que o trono espera",
						  !Disse(falas, "tarefa pendente"), Ultimos(falas));
			}
			finally { LoginCalaATarefaDeTeste = false; }

			falas = Ouvir(() => ReconciliarDadiva(deusQueVolta));
			AfirmarGdk("**O DEUS QUE ENTRA NO MUNDO OUVE A TAREFA PENDENTE E O PRAZO** (`:171`) -- e nao as frases da coroacao",
					  Disse(falas, "tarefa pendente -- ELIMINAR a ameaça Vilao") && Disse(falas, "Prazo: 2h 5min")
					  && !Disse(falas, "você agora é"), Ultimos(falas));
			falas = Ouvir(() => { ReconciliarDadiva(deusQueVolta); ReconciliarDadiva(deusQueVolta); });
			AfirmarGdk("uma vez por entrada: os tiques seguintes da reconciliacao nao repetem", !Disse(falas, "tarefa pendente"), Ultimos(falas));

			_tarefaAlvo = _tarefaNomeDoAlvo = "";
			_tarefaPlanetaChave = "bancada";
			_tarefaPlanetaNome = "Mundo de Bancada";
			LimparTudoDaBancada();   // o corpo saiu do mundo: o proximo e outro login da mesma conta
			ServerPlayer deusDeNovo = ForjarGdk("DeusDeNovo", chao, 1e9, conta: ContaDoDeusDeBancada);
			falas = Ouvir(() => ReconciliarDadiva(deusDeNovo));
			AfirmarGdk("no login seguinte ele ouve de novo, e a tarefa de MUNDO sai com o nome do mundo (`god_task_desc`, `:523-526`)",
					  Disse(falas, "tarefa pendente -- DESTRUIR o mundo Mundo de Bancada"), Ultimos(falas));

			ServerPlayer mortalQueEntra = ForjarGdk("MortalQueEntra", chao + new Vec2(3 * T, 0), 1e9);
			falas = Ouvir(() => ReconciliarDadiva(mortalQueEntra));
			AfirmarGdk("quem nao porta o titulo nao ouve a tarefa de ninguem", !Disse(falas, "tarefa pendente"), Ultimos(falas));

			ServerPlayer semDono = ForjarGdk("DeusSemDonoQueEntra", chao + new Vec2(6 * T, 0), 1e9, conta: "");
			ConsagrarDeusSemDono(semDono);
			falas = Ouvir(() => ReconciliarDadiva(semDono));
			AfirmarGdk("nem o corpo sem dono consagrado: a tarefa e do trono, e ele nao esta nele", !Disse(falas, "tarefa pendente"), Ultimos(falas));
			ConsagrarDeusSemDono(semDono, consagrado: false);

			_tarefaPlanetaChave = _tarefaPlanetaNome = "";
			LimparTudoDaBancada();
			ServerPlayer deusSemTarefa = ForjarGdk("DeusSemTarefa", chao, 1e9, conta: ContaDoDeusDeBancada);
			falas = Ouvir(() => ReconciliarDadiva(deusSemTarefa));
			AfirmarGdk("sem tarefa de pe o login nao diz nada (`if(god_task_type)`)", !Disse(falas, "tarefa pendente"), Ultimos(falas));
		}
		finally
		{
			(_tarefaAlvo, _tarefaNomeDoAlvo, _tarefaPrazo, _tarefaPlanetaChave, _tarefaPlanetaNome) = (alvo0, nome0, prazo0, chave0, mundo0);
			_tronos.Remove(KitDoDeus.Cargo);
		}
	}

	// =====================================================================
	// 9) O ULTRA INSTINTO
	// =====================================================================
	private void AsPassivasDoInstinto()
	{
		GD.Print("[godkit] -- 9) O ULTRA INSTINTO, contra o `UltraInstinct.dm` --");
		const int T = ZoneCollision.TileSize;
		LimparTudoDaBancada();
		Vec2 chao = CorredorLivre(24);
		ServerPlayer ui = ForjarDisciplinado("Instinto", chao, TipoDeDisciplina.UltraInstinct);
		ui.Facing = Facing.East;

		// ---------------------------------------------------------------- 9a. a passiva vale com o toggle OU na forma
		AfirmarGdk("com tudo desligado o corpo nao desvia de nada", ui.Combate.ChanceEsquiva == 0);
		ApertarEOuvir(ui, "ui_toggle");
		AfirmarGdk("com o toggle ligado e a precisao cheia: 45% contra soco (`UI_EVADE_MELEE_PCT`)", Math.Abs(ui.Combate.ChanceEsquiva - 45) < 1e-9, $"{ui.Combate.ChanceEsquiva:0.##}");
		ApertarEOuvir(ui, "ui_toggle");
		VestirForma(ui, "ui_sign");
		AfirmarGdk("**NA FORMA, SEM O TOGGLE, A PASSIVA VALE** (`if(!M.ui_active && !M.ui_form) return 0`, `:168`)",
				  !ui.UltraInstinct.Ligada && Math.Abs(ui.Combate.ChanceEsquiva - 45) < 1e-9, $"{ui.Combate.ChanceEsquiva:0.##}");
		PassivaSoComToggleDeTeste = true;
		try
		{
			AplicarDisciplina(ui);
			AfirmarGdk("(defeito injetado: a passiva so com o toggle) transformado e sem o toggle, o corpo nao desvia", ui.Combate.ChanceEsquiva == 0);
		}
		finally { PassivaSoComToggleDeTeste = false; }

		List<string> falas = ApertarEOuvir(ui, "ui_toggle");
		AfirmarGdk("transformado nao ha o que ligar: o toggle recusa (*\"Transformado, o instinto ja guia seu corpo\"*, `:236-238`)",
				  !ui.UltraInstinct.Ligada && Disse(falas, "transformado, o instinto"), Ultimos(falas));
		ToggleLigaNaFormaDeTeste = true;
		try
		{
			ApertarEOuvir(ui, "ui_toggle");
			AfirmarGdk("(defeito injetado: o toggle liga na forma) ele liga por cima da forma", ui.UltraInstinct.Ligada);
			ui.UltraInstinct.Ligada = false;
		}
		finally { ToggleLigaNaFormaDeTeste = false; }
		VestirForma(ui, Catalogo.IdBase);

		// ---------------------------------------------------------------- 9b. os portoes
		GD.Print("[godkit] -- 9b) os portoes do `ui_try_evade` (`:166-172`) --");
		ApertarEOuvir(ui, "ui_toggle");
		ui.Ficha.med = true;
		AplicarDisciplina(ui);
		bool meditando = ui.Combate.ChanceEsquiva == 0;
		ui.Ficha.med = false;
		ui.Ficha.train = true;
		AplicarDisciplina(ui);
		bool treinando = ui.Combate.ChanceEsquiva == 0;
		ui.Ficha.train = false;
		AfirmarGdk("**MEDITANDO OU TREINANDO O CORPO NAO DESVIA** (`if(M.med || M.train) return 0`, `:169`)", meditando && treinando,
				  $"meditando {meditando}, treinando {treinando}");
		ui.Ficha.med = true;
		EsquivaSemPortoesDeTeste = true;
		try
		{
			AplicarDisciplina(ui);
			AfirmarGdk("(defeito injetado: a esquiva sem portoes) meditando, o corpo desvia", ui.Combate.ChanceEsquiva > 0);
		}
		finally { EsquivaSemPortoesDeTeste = false; ui.Ficha.med = false; }

		// ACCELERATED BATTLE: carregando um tiro, so desvia quem tem 40% de maestria. O tiro e o do kit do Deus, num
		// corpo sem dono consagrado -- e o unico "carregando ki" que a bancada liga sem um canal de verdade.
		ServerPlayer carregador = ForjarDisciplinado("Carregador", chao + new Vec2(0, 3 * T), TipoDeDisciplina.UltraInstinct, real: 39, conta: "");
		ConsagrarDeusSemDono(carregador);
		carregador.UltraInstinct.Ligada = true;
		AtirarDoKit(carregador, DomDeProjetil.DerrubaAGuarda);
		AplicarDisciplina(carregador);
		bool exposto = CarregandoTiroDoKit(carregador.Id) && carregador.Combate.ChanceEsquiva == 0;
		carregador.UltraInstinct.Real = 40;
		AplicarDisciplina(carregador);
		AfirmarGdk("**ACCELERATED BATTLE**: carregando um ataque de ki, com 39% de maestria o corpo fica exposto; com 40% ele desvia (`:172`)",
				  exposto && carregador.Combate.ChanceEsquiva > 0, $"exposto aos 39: {exposto}; aos 40: {carregador.Combate.ChanceEsquiva:0.##}%");
		ConsagrarDeusSemDono(carregador, consagrado: false);

		// ---------------------------------------------------------------- 9c. contra ki: 30%
		GD.Print("[godkit] -- 9c) a esquiva contra KI: 30% com a precisao cheia (`UI_EVADE_BLAST_PCT`, `objects.dm:320-322`) --");
		ServerPlayer atirador = ForjarGdk("Atirador", chao + new Vec2(-4 * T, 0), 1e10);
		const int tiros = 400;
		int evitados = TirosEvitados(ui, atirador, tiros);
		AfirmarGdk($"**O CORPO DESVIA SOZINHO DE ~30% DOS TIROS** ({tiros} bolas de quem e dez vezes mais forte)",
				  evitados > tiros * 0.21 && evitados < tiros * 0.39, $"{evitados} de {tiros} ({100.0 * evitados / tiros:0.#}%)");
		ui.Combate.Guardar(true);
		int deGuarda = TirosEvitados(ui, atirador, 120);
		ui.Combate.Guardar(false);
		AfirmarGdk("de guarda erguida ele se comprometeu a aparar: nao desvia de nenhum (`if(M.blocking && guard) return 0`, `:170`)",
				  deGuarda == 0, $"{deGuarda} de 120");
		ui.UltraInstinct.Ligada = false;
		AplicarDisciplina(ui);
		int desligado = TirosEvitados(ui, atirador, 120);
		AfirmarGdk("e com a passiva desligada (e fora de forma) todo tiro acerta", desligado == 0, $"{desligado} de 120");

		// ---------------------------------------------------------------- 9d. as pilhas
		GD.Print("[godkit] -- 9d) o bonus pos-esquiva: 5% do ataque e da velocidade EFETIVOS, cada pilha por 5 s (`ui_evade_stack`, `:180-191`) --");
		DerrubarBuffs(ui);
		_disciplinasVivas.Remove(ui.Id);
		ui.PilhasDeEsquiva = 0;
		ui.Ficha.Statify();
		double off0 = ui.Ficha.Tphysoff, spd0 = ui.Ficha.Tspeed;
		double doff = ui.Ficha.Ephysoff * Disciplinas.EsquivaBonus, dspd = ui.Ficha.Espeed * Disciplinas.EsquivaBonus;
		AoEsquivarPorInstinto(ui);
		AfirmarGdk("**UMA ESQUIVA SOMA 5% DO ATAQUE E DA VELOCIDADE EFETIVOS** nos temporarios (`doff = Ephysoff * UI_EVADE_BONUS`)",
				  ui.PilhasDeEsquiva == 1 && doff > 0 && Math.Abs(ui.Ficha.Tphysoff - off0 - doff) < 1e-9 && Math.Abs(ui.Ficha.Tspeed - spd0 - dspd) < 1e-9,
				  $"Tphysoff +{ui.Ficha.Tphysoff - off0:0.#####} (esperado {doff:0.#####}), Tspeed +{ui.Ficha.Tspeed - spd0:0.#####} (esperado {dspd:0.#####})");
		AdiantoDoRelogioDeTeste += 2000;
		for (int i = 0; i < 6; i++) AoEsquivarPorInstinto(ui);
		AfirmarGdk("o teto e de cinco pilhas (`UI_EVADE_STACK_MAX`)", ui.PilhasDeEsquiva == 5, $"{ui.PilhasDeEsquiva}");
		AdiantoDoRelogioDeTeste += 3100;
		TickDasPilhasDeEsquiva(ui);
		AfirmarGdk("**CADA PILHA SAI NO PRAZO DELA**: 5,1 s depois da primeira ela cai, e as quatro de 2 s depois continuam",
				  ui.PilhasDeEsquiva == 4, $"{ui.PilhasDeEsquiva} pilha(s)");
		AdiantoDoRelogioDeTeste += 2100;
		TickDasPilhasDeEsquiva(ui);
		AfirmarGdk("...e quando todas caem os temporarios voltam EXATAMENTE ao que eram",
				  ui.PilhasDeEsquiva == 0 && Math.Abs(ui.Ficha.Tphysoff - off0) < 1e-9 && Math.Abs(ui.Ficha.Tspeed - spd0) < 1e-9,
				  $"{ui.PilhasDeEsquiva} pilha(s); Tphysoff {ui.Ficha.Tphysoff:0.#####} (era {off0:0.#####})");

		// ---------------------------------------------------------------- 9e. transformar renova
		GD.Print("[godkit] -- 9e) transformar RENOVA a precisao: 80 no Sign, 100 no Perfected (`:466`) --");
		ui.UltraInstinct.Real = 25;
		ui.UltraInstinct.Atual = 10;
		FormaNaoRenovaDeTeste = true;
		try
		{
			EntrarNaForma(ui, Catalogo.Def("ui_sign")!, semCena: true);
			AfirmarGdk("(defeito injetado: a forma nao renova) entrar no Sign deixa a precisao em 10 -- era o jogo: a funcao existia e ninguem a chamava",
					  Math.Abs(ui.UltraInstinct.Atual - 10) < 1e-9, $"{ui.UltraInstinct.Atual:0.##}");
		}
		finally { FormaNaoRenovaDeTeste = false; }
		VestirForma(ui, Catalogo.IdBase);
		EntrarNaForma(ui, Catalogo.Def("ui_sign")!, semCena: true);
		AfirmarGdk("**ENTRAR NO SIGN POE A PRECISAO EM 80**, com 25% de maestria e tudo", Math.Abs(ui.UltraInstinct.Atual - 80) < 1e-9, $"{ui.UltraInstinct.Atual:0.##}");
		ui.Combate.EntrarEmCombate();
		TickDasDisciplinas(ui, 1.0);
		AfirmarGdk("...e o tique nao a corta na maestria: na forma ela so paga o dreno da forma (0,35/s)",
				  Math.Abs(ui.UltraInstinct.Atual - 79.65) < 1e-6, $"{ui.UltraInstinct.Atual:0.###}");
		EntrarNaForma(ui, Catalogo.Def("ui_perfected")!, semCena: true);
		AfirmarGdk("**E NO PERFECTED, EM 100**", Math.Abs(ui.UltraInstinct.Atual - 100) < 1e-9, $"{ui.UltraInstinct.Atual:0.##}");
		VestirForma(ui, Catalogo.IdBase);
		ui.UltraInstinct.Real = 100;
		ui.UltraInstinct.Atual = 100;

		// ---------------------------------------------------------------- 9f. o Godly Display
		GD.Print("[godkit] -- 9f) o GODLY DISPLAY: o avanco que marca e a rajada em rodizio (`:513-596`) --");
		LimparTudoDaBancada([ui]);
		DerrubarBuffs(ui);
		ui.Pos = chao;
		ui.Facing = Facing.East;
		ui.Ficha.Statify();
		ui.Ficha.Ki = ui.Ficha.MaxKi;
		falas = ApertarEOuvir(ui, "ui_godlydisplay");
		AfirmarGdk("com o Instinto desligado o verb pede que ele seja ligado (*\"Ative o Ultra Instinto primeiro.\"*, `:522-524`)",
				  ui.GdJanelaAte == 0 && Disse(falas, "ative o Ultra Instinto"), Ultimos(falas));

		ui.UltraInstinct.Ligada = true;
		ServerPlayer a = ForjarGdk("NoCaminhoA", chao + new Vec2(3 * T, 0), 1e9);
		ServerPlayer b = ForjarGdk("NoCaminhoB", chao + new Vec2(6 * T, 0), 1e9);
		ServerPlayer deLado = ForjarGdk("DeLado", chao + new Vec2(3 * T, 2 * T), 1e9);
		ServerPlayer atras = ForjarGdk("Atras", chao + new Vec2(-2 * T, 0), 1e9);

		DisplayMarcaEmVoltaDeTeste = true;
		try
		{
			ApertarEOuvir(ui, "ui_godlydisplay");
			AfirmarGdk("(defeito injetado: marca em volta) o primeiro toque marca os quatro, e o corpo nao sai do lugar",
					  ui.GdMarcados.Count == 4 && ui.Pos.Equals(chao), $"{ui.GdMarcados.Count} marcado(s)");
		}
		finally { DisplayMarcaEmVoltaDeTeste = false; }
		ui.GdMarcados.Clear();
		ui.GdJanelaAte = 0;
		ui.GdRecargaAte = 0;

		double ki0 = ui.Ficha.Ki;
		falas = ApertarEOuvir(ui, "ui_godlydisplay");
		AfirmarGdk("**O PRIMEIRO TOQUE AVANCA DEZ TILES** no rumo do olhar, atravessando os corpos (`ui_gd_rush`, `:556-567`)",
				  Math.Abs(ui.Pos.X - (chao.X + 10 * T)) < 0.5f && Math.Abs(ui.Pos.Y - chao.Y) < 0.5f, $"andou {(ui.Pos.X - chao.X) / T:0.##} tile(s)");
		AfirmarGdk("**E MARCA SO QUEM ESTAVA NO CAMINHO**: os dois da linha, e nao o que estava de lado nem o de tras",
				  ui.GdMarcados.Count == 2 && ui.GdMarcados.Contains(a.Id) && ui.GdMarcados.Contains(b.Id), $"{ui.GdMarcados.Count} marcado(s) | {Ultimos(falas)}");
		AfirmarGdk("o primeiro toque e de graca (o custo SO existe no segundo, `:533`)", Math.Abs(ui.Ficha.Ki - ki0) < 1e-9);

		b.Combate.Guardar(true);
		Dictionary<string, double> vidasA = VidasDe(a), vidasB = VidasDe(b);
		double golpeEmA = CombatMath.DanoBase(ui.Ficha, a.Ficha) * Disciplinas.GdDanoPorGolpe;
		double golpeEmB = CombatMath.DanoBase(ui.Ficha, b.Ficha) * Disciplinas.GdDanoPorGolpe * Disciplinas.GdBloqueioMult;
		double precisao0 = ui.UltraInstinct.Atual;
		falas = ApertarEOuvir(ui, "ui_godlydisplay");
		AfirmarGdk("**O SEGUNDO TOQUE COBRA 8% DO KI E 10 DE PRECISAO** (`UI_GD_KI_PCT`, `UI_GD_PROF_COST`)",
				  Math.Abs(ki0 - ui.Ficha.Ki - ui.Ficha.MaxKi * 0.08) < 1e-6 && Math.Abs(precisao0 - ui.UltraInstinct.Atual - 10) < 1e-9,
				  $"Ki -{ki0 - ui.Ficha.Ki:0.##} (8% = {ui.Ficha.MaxKi * 0.08:0.##}), precisao -{precisao0 - ui.UltraInstinct.Atual:0.##}");
		AfirmarGdk("**DEZ GOLPES EM RODIZIO**: cinco em cada um dos dois, cada golpe `NormDamageCalc x 0,5` (`:582-592`)",
				  golpeEmA > 0 && Math.Abs(SomaDasPerdas(a, vidasA) - 5 * golpeEmA) < 1e-6,
				  $"A perdeu {SomaDasPerdas(a, vidasA):0.####} (5 x {golpeEmA:0.####})");
		AfirmarGdk("**A GUARDA PROTEGE DOS GOLPES** (`dmg *= UI_GD_BLOCK_MULT`, `:590`): quem bloqueia leva 35% de cada um",
				  Math.Abs(SomaDasPerdas(b, vidasB) - 5 * golpeEmB) < 1e-6, $"B perdeu {SomaDasPerdas(b, vidasB):0.####} (5 x {golpeEmB:0.####})");
		falas = ApertarEOuvir(ui, "ui_godlydisplay");
		AfirmarGdk("depois da rajada, um minuto de recarga (`UI_GD_CD`)", Disse(falas, "se recompõe") && ui.GdJanelaAte == 0, Ultimos(falas));

		// a janela que fecha sem o segundo toque
		ui.GdRecargaAte = 0;
		ui.Pos = chao;
		ui.Facing = Facing.East;
		ApertarEOuvir(ui, "ui_godlydisplay");
		AdiantoDoRelogioDeTeste += (long)(Disciplinas.GdJanelaSegundos * 1000) + 300;
		falas = Ouvir(() => TickDasDisciplinas(ui, 0.1));
		List<string> depois = ApertarEOuvir(ui, "ui_godlydisplay");
		AfirmarGdk("**A JANELA QUE FECHA SEM O SEGUNDO TOQUE TAMBEM ARMA A RECARGA** (`:573-578`) -- e o corpo le que ela fechou",
				  Disse(falas, "janela do Godly Display se fecha") && Disse(depois, "se recompõe"), $"{Ultimos(falas)} || {Ultimos(depois)}");
	}

	// =====================================================================
	// 10) O PODER DA DESTRUICAO
	// =====================================================================
	private void AsPassivasDaDestruicao()
	{
		GD.Print("[godkit] -- 10) O PODER DA DESTRUICAO, contra o `UltraEgo.dm` --");
		const int T = ZoneCollision.TileSize;
		LimparTudoDaBancada();
		_tronos.Remove(KitDoDeus.Cargo);
		Vec2 chao = CorredorLivre(24);
		ServerPlayer ue = ForjarDisciplinado("Destruidor", chao, TipoDeDisciplina.PoderDaDestruicao);
		ServerPlayer agressor = ForjarGdk("Agressor", chao + new Vec2(0.9f * T, 0), 1e9);

		// ---------------------------------------------------------------- 10a. a aura vale com o toggle OU na forma
		AfirmarGdk("com tudo desligado a aura nao reduz nada", ue.Combate.ReducaoDeDano == 0 && !AuraDaDestruicaoLigada(ue));
		VestirForma(ue, "destroyer");
		AfirmarGdk("**NA FORMA, SEM O TOGGLE, A AURA VALE** (`return (M.ue_active || M.ue_form)`, `:208`): 25% de reducao com a energia cheia",
				  !ue.PoderDaDestruicao.Ligada && Math.Abs(ue.Combate.ReducaoDeDano - 25) < 1e-6, $"{ue.Combate.ReducaoDeDano:0.###}%");
		PassivaSoComToggleDeTeste = true;
		try
		{
			AplicarDisciplina(ue);
			AfirmarGdk("(defeito injetado: a passiva so com o toggle) transformado e sem o toggle, a aura nao reduz nada", ue.Combate.ReducaoDeDano == 0);
		}
		finally { PassivaSoComToggleDeTeste = false; }
		List<string> falas = ApertarEOuvir(ue, "ue_toggle");
		AfirmarGdk("transformado nao ha o que ligar: o toggle recusa (*\"Transformado, a destruicao ja reveste seu corpo\"*, `:361-363`)",
				  !ue.PoderDaDestruicao.Ligada && Disse(falas, "transformado, a destruição"), Ultimos(falas));

		// ---------------------------------------------------------------- 10b. o recuo
		GD.Print("[godkit] -- 10b) o recuo em quem bate (`ue_melee_recoil`, `:243-251`) --");
		Dictionary<string, double> antes = VidasDe(agressor);
		falas = Ouvir(() => AplicarAuraDepoisDoGolpe(ue, agressor, 1000, ehKi: false));
		AfirmarGdk("**QUEM BATE NA AURA LEVA 5% DO PROPRIO GOLPE** (1% + 0,04 x energia), num membro so, e le por que",
				  Math.Abs(MaiorPerda(agressor, antes) - 50) < 1e-6 && MembrosFeridos(agressor, antes) == 1 && Disse(falas, "QUEIMA os seus golpes"),
				  $"perdeu {MaiorPerda(agressor, antes):0.###} em {MembrosFeridos(agressor, antes)} membro(s) | {Ultimos(falas)}");
		AfirmarGdk("...e o golpe fica guardado pra explosao (`M.ue_last_hit = dmg`)", Math.Abs(ue.UltimoGolpeRecebido - 1000) < 1e-9);
		falas = Ouvir(() => AplicarAuraDepoisDoGolpe(ue, agressor, 1000, ehKi: false));
		AfirmarGdk("a frase sai no maximo de 5 em 5 s (`ue_proc_msg_cd`)", !Disse(falas, "QUEIMA"), Ultimos(falas));
		agressor.Combate.Curar();
		antes = VidasDe(agressor);
		AplicarAuraDepoisDoGolpe(ue, agressor, 1000, ehKi: true);
		AfirmarGdk("de longe (ki) nao ha recuo: quem atira nao encosta na aura", MaiorPerda(agressor, antes) == 0);

		// ---------------------------------------------------------------- 10c. o ki que chega
		GD.Print("[godkit] -- 10c) o ki que chega: a aura devora e reduz, a infusao penetra (`ue_blast_mitigate`, `:223-240`) --");
		ue.PoderDaDestruicao.Real = 50;
		double sobra = DanoDeKiPelaDestruicao(ue, agressor, 100);
		AfirmarGdk("**A AURA DEVORA 35% DO KI E REDUZ 25% DO RESTO**: de 100 sobram 48,75",
				  Math.Abs(sobra - 48.75) < 1e-9 && Math.Abs(ue.UltimoGolpeRecebido - 48.75) < 1e-9, $"{sobra:0.####}");
		AfirmarGdk("...e o gatilho ensina a Destruicao (o `UE_REAL_PER_PROC 0.1`, na escala das 3 h de 2026-10-25)",
				   Math.Abs(ue.PoderDaDestruicao.Real - (50 + Disciplinas.PoderDaDestruicao.RealPorGatilho)) < 1e-9, $"{ue.PoderDaDestruicao.Real:0.#####}");
		ue.PoderDaDestruicao.Real = 100;

		ServerPlayer infundido = ForjarDisciplinado("Infundido", chao + new Vec2(0, 3 * T), TipoDeDisciplina.PoderDaDestruicao);
		ServerPlayer comum = ForjarGdk("Comum", chao + new Vec2(3 * T, 0), 1e9);
		ApertarEOuvir(infundido, "ue_hakai");
		AfirmarGdk("(montagem) a Hakai Infusion acendeu", HakaiAtivo(infundido));
		AfirmarGdk("**O TIRO INFUNDIDO PENETRA 5%** mesmo contra quem nao tem aura (`UE_HKI_PEN_MULT`): 100 vira 105",
				  Math.Abs(DanoDeKiPelaDestruicao(comum, infundido, 100) - 105) < 1e-9, $"{DanoDeKiPelaDestruicao(comum, infundido, 100):0.####}");
		AfirmarGdk("**E CORTA A DEFLEXAO DO ALVO PELA METADE** (`UE_HKI_DEFLECT_MULT`): 40% vira 20%; sem infusao, 40% continua 40%",
				  Math.Abs(DeflexaoContraAInfusao(infundido, 40) - 20) < 1e-9 && Math.Abs(DeflexaoContraAInfusao(comum, 40) - 40) < 1e-9,
				  $"{DeflexaoContraAInfusao(infundido, 40):0.###} / {DeflexaoContraAInfusao(comum, 40):0.###}");

		// PELO FUNIL DE VERDADE: uma bola que fere quem tem a aura passa pela conta (o golpe fica guardado).
		ServerPlayer atirador = ForjarGdk("Atirador", chao + new Vec2(-4 * T, 0), 1e10);
		var bola = new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 30, Deflectivel = false };
		KiIgnoraADestruicaoDeTeste = true;
		try
		{
			ue.UltimoGolpeRecebido = 0;
			Projetil cega = Disparar(atirador, bola);
			Acertar(cega, ue);
			AfirmarGdk("(defeito injetado: o ki ignora a Destruicao) o tiro fere e a aura nem o ve -- era o jogo: as funcoes existiam, orfas",
					  ue.UltimoGolpeRecebido == 0 && Math.Abs(DanoDeKiPelaDestruicao(ue, infundido, 100) - 100) < 1e-9);
		}
		finally { KiIgnoraADestruicaoDeTeste = false; }
		ue.Combate.Curar();
		ue.UltimoGolpeRecebido = 0;
		Projetil vista = Disparar(atirador, bola);
		Acertar(vista, ue);
		AfirmarGdk("**O `Acertar` PASSA O TIRO PELA AURA**: o golpe de ki fica guardado pra explosao", ue.UltimoGolpeRecebido > 0, $"{ue.UltimoGolpeRecebido:0.###}");
		ue.Combate.Curar();
		LimparTudoDaBancada([ue, agressor]);

		// ---------------------------------------------------------------- 10d. o Unbound Ego
		GD.Print("[godkit] -- 10d) o UNBOUND EGO (`ue_ego_update`, `:292-316`) --");
		VestirForma(ue, Catalogo.IdBase);
		ue.PoderDaDestruicao.Ligada = true;
		int membros = ue.Combate.Corpo.Partes.Count(p => p.Nome != "Rabo");
		foreach (BodyPart p in ue.Combate.Corpo.Partes) p.Vida = p.VidaMax * 0.05;
		ue.Combate.SincronizarVida();
		ue.Ficha.Ki = ue.Ficha.MaxKi * 0.5;
		ue.Ficha.stamina = ue.Ficha.maxstamina * 0.5;
		ue.Combate.EntrarEmCombate();
		falas = Ouvir(() => TickDoUnboundEgo(ue));
		double esperado = 1 + (membros * 0.05 + 0.20);
		AfirmarGdk($"**TODOS OS {membros} MEMBROS POR UM FIO (5% de vida) = O BONUS CHEIO**: 5% por membro mais os 20% de todos no pico",
				  Math.Abs(ue.Ficha.ue_ego_mult - esperado) < 1e-9, $"x{ue.Ficha.ue_ego_mult:0.####} (esperado x{esperado:0.####}) -- a conta velha dava x1: tirava da soma o membro abaixo de 20%");
		AfirmarGdk("**...E DEVOLVE 25% DE KI E DE FOLEGO**, uma vez por luta (`:311-312`)",
				  Math.Abs(ue.Ficha.Ki - ue.Ficha.MaxKi * 0.75) < 1e-6 && Math.Abs(ue.Ficha.stamina - ue.Ficha.maxstamina * 0.75) < 1e-6
				  && Disse(falas, "UNBOUND EGO no limite"), $"Ki {ue.Ficha.Ki / ue.Ficha.MaxKi:P0}, folego {ue.Ficha.stamina / ue.Ficha.maxstamina:P0}");
		ue.Ficha.Ki = ue.Ficha.MaxKi * 0.5;
		TickDoUnboundEgo(ue);
		AfirmarGdk("...so uma vez: o segundo tique nao devolve de novo", Math.Abs(ue.Ficha.Ki - ue.Ficha.MaxKi * 0.5) < 1e-6);

		BodyPart braco = ue.Combate.Corpo.Achar("Braco esquerdo")!;
		braco.Vida = 0;
		TickDoUnboundEgo(ue);
		BodyPart? mao = ue.Combate.Corpo.Achar("Mao esquerda");
		int perdidos = ue.Combate.Corpo.Partes.Count(p => p.Nome != "Rabo" && (p.Decepado || p.Vida <= 0));
		AfirmarGdk("**UM MEMBRO ZERADO PERDE O BONUS DELE E DERRUBA O \"TODOS NO PICO\"** (`peaks >= parts`)",
				  Math.Abs(ue.Ficha.ue_ego_mult - (1 + (membros - perdidos) * 0.05)) < 1e-9,
				  $"x{ue.Ficha.ue_ego_mult:0.####} com {perdidos} perdido(s) de {membros} (mao em {mao?.Vida:0.##})");
		braco.Vida = braco.VidaMax * 0.05;

		ue.PoderDaDestruicao.Real = 39;
		TickDoUnboundEgo(ue);
		bool semAFaixa = ue.Ficha.ue_ego_mult == 1;
		ue.PoderDaDestruicao.Real = 100;
		ue.PoderDaDestruicao.Ligada = false;
		TickDoUnboundEgo(ue);
		AfirmarGdk("sem a faixa de 40% ou com a aura desligada, o ego nao da nada", semAFaixa && ue.Ficha.ue_ego_mult == 1);
		ue.PoderDaDestruicao.Ligada = true;

		// ---------------------------------------------------------------- 10e. a Destruction Explosion
		GD.Print("[godkit] -- 10e) a DESTRUCTION EXPLOSION: um golpe direto num membro de cada um a ate 3 tiles (`:276-287`) --");
		ue.Combate.Curar();
		AplicarDisciplina(ue);
		LimparTudoDaBancada([ue]);
		ServerPlayer perto = ForjarGdk("Perto", chao + new Vec2(3 * T, 0), 1e9);
		ServerPlayer longe = ForjarGdk("Longe", chao + new Vec2(4 * T, 0), 1e9);
		Dictionary<string, double> vidasPerto = VidasDe(perto), vidasLonge = VidasDe(longe);
		ue.UltimoGolpeRecebido = 40;
		DestructionExplosion(ue);
		AfirmarGdk("**QUEM ESTA A 3 TILES LEVA 100% DO ULTIMO GOLPE (energia cheia) EM UM MEMBRO SO** -- eram todos os membros, cada um com o golpe inteiro",
				  Math.Abs(MaiorPerda(perto, vidasPerto) - 40) < 1e-6 && MembrosFeridos(perto, vidasPerto) == 1,
				  $"perdeu {MaiorPerda(perto, vidasPerto):0.###} em {MembrosFeridos(perto, vidasPerto)} membro(s)");
		AfirmarGdk("...e quem esta a 4 tiles nao leva nada (`oview(UE_EXP_RANGE)`)", MembrosFeridos(longe, vidasLonge) == 0);
	}
}
