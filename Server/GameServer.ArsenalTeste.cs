using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--arsenalteste` -- AS CATORZE FOLHAS DO LOTE G5.
///
/// ============================ ELA EXERCITA A PRODUCAO, E SO ELA ============================
/// Nenhuma familia daqui chama o efeito direto. Toda tecnica entra por
/// <see cref="UsarHabilidade"/> -> <see cref="UsarTecnica"/> -> <see cref="UsarTecnicasG5"/> --
/// o mesmo caminho do pacote do jogador, com o mesmo `SabeTecnica` no meio. Uma bancada que
/// chamasse `MasenkoG5(pl)` na mao provaria que o metodo existe, e nao que o jogador o alcanca:
/// e exatamente esse o buraco que deixou 60 verbos concedidos e mudos por meses.
///
/// A INFRAESTRUTURA E EMPRESTADA da `--projetilteste` (`Forjar`, `CorredorLivre`,
/// `LimparTudoDaBancada`, `ZonaDaBancadaDeProjetil`). Escrever um segundo forjador seria a segunda
/// resposta pra "como nasce um corpo de bancada", e as duas divergiriam no dia em que alguem
/// mexesse numa.
/// ==========================================================================================
///
/// ============================ O QUE ELA TENTA REPROVAR ============================
/// Pela regra 0.7 da casa, cada familia carrega o DEFEITO INJETADO que ela existe pra pegar:
///
///  1. O GATE  -- uma tecnica que o jogador NAO comprou tem que ouvir "voce nao sabe" e nao
///                atirar; e um id que NAO e do lote nao pode ser engolido pelo `default`.
///  2. NIVEL   -- oito dos catorze verbos NAO vem de comprar skill nenhuma: vem de um DEGRAU. Sem
///                o degrau, `SabeTecnica` recusa; com ele, aceita. Este elo ja quebrou uma vez
///                (o `VerbosAtivos` ficou sem chamador e duas das tres tecnicas de tiro sumiram).
///  3. RAIOS   -- o Final Flash com `beamskill` ACIMA de 100, que no DM cai fora dos quatro ramos
///                e nao faz nada; e o custo por ciclo do Massive Beam, que tem que ser 15x o do
///                Ki Wave (era o parametro que o `Canalizar` nao tinha).
///  4. VOLEI   -- a recarga tem que ser COMPARTILHADA entre as duas barragens (senao o jogador
///                alterna os dois verbos e dobra o volume de fogo de graca), e a recusa por teto
///                de zona NAO PODE COBRAR o Ki da barragem que nao saiu.
///  5. CERCO   -- as minas do Ki Minefield tem que morrer pelo PRAZO (4 s) e nao pelo alcance, e
///                nao podem andar um pixel. A bola da Hellzone tem que ficar UM SEGUNDO parada
///                antes de cacar -- uma espera que "existe" mas nunca prende e espera nenhuma.
///  6. PARALISIA -- a fresta de 1 em 12 do DM tem que DISPARAR de verdade (um escape que nunca sai
///                e indistinguivel de paralisia absoluta); o segundo tiro NAO pode renovar o
///                prazo; e paralisia nao pode virar stun -- quem esta preso continua batendo.
/// ==================================================================================
/// </summary>
public partial class GameServer
{
	private int _arsOk, _arsFalhou;

	private void AfirmarArs(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _arsOk++; GD.Print($"[arsenal]   OK    {oque}"); return; }
		_arsFalhou++;
		GD.PrintErr($"[arsenal]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// OS OITO VERBOS QUE SO O NIVEL CONCEDE, com o degrau de onde saem (`Assets/Data/niveis.json`).
	///
	/// Estao escritos aqui porque sao o FATO que a familia 2 afirma. Ler o par do proprio json
	/// faria a bancada concordar consigo mesma: se o extrator um dia parasse de escrever o degrau,
	/// os dois lados sumiriam juntos e o teste continuaria verde sobre uma tecnica inalcancavel.
	/// </summary>
	private static readonly (string Verbo, string Path, int Nivel)[] PorDegrauG5 =
	[
		("Masenko", "/datum/skill/mind/Basic_Beam_Mastery", 30),
		("Massive_Beam", "/datum/skill/mind/Advanced_Beam_Mastery", 30),
		("Charged_Shot", "/datum/skill/mind/Basic_Blast_Mastery", 50),
		("Energy_Barrage", "/datum/skill/mind/Basic_Blast_Mastery", 30),
		("Scattershot", "/datum/skill/mind/Basic_Blast_Mastery", 75),
		("Kienzan", "/datum/skill/mind/Basic_Guided_Mastery", 30),
		("Ki_Bomb", "/datum/skill/mind/Basic_Ki_Control", 50),
		("Hellzone_Grenade", "/datum/skill/mind/Basic_Targeted_Mastery", 30),
	];

	/// <summary>Os seis que uma SKILL concede direto -- cinco deles sao `datum/skill/rank/*`.</summary>
	private static readonly (string Verbo, string Path)[] PorSkillG5 =
	[
		("Final_Flash", "/datum/skill/rank/FinalFlash"),
		("Makkankosappo", "/datum/skill/rank/Makkankosappo"),
		("BusterShell", "/datum/skill/rank/BusterShell"),
		("KillDriver", "/datum/skill/rank/KillDriver"),
		("Paralysis", "/datum/skill/rank/Paralysis"),
		("Stunlock", "/datum/skill/meta/Stunlock"),
	];

	public void RodarBancadaDoArsenal()
	{
		_arsOk = _arsFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[arsenal] ================ O ARSENAL DE KI NOMEADO (lote G5) ================");

		AfirmarArs("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			OCatalogoConheceAsCatorze();
			OVerboVemDoDegrauOuDaSkill();
			OsQuatroRaios();
			AsDuasBarragens();
			OsDoisCercos();
			APernaTrancada();
		}
		finally
		{
			LimparTudoDaBancada();
		}

		GD.Print($"[arsenal] ================ {_arsOk} passaram, {_arsFalhou} falharam ================");
	}

	// =====================================================================
	// 1) O CATALOGO E O GATE
	// =====================================================================
	/// <summary>
	/// AS CATORZE EXISTEM COMO PORTADAS, e o gate de "voce sabe?" continua fechado pra quem nao
	/// comprou.
	///
	/// `Tecnicas.Get` NUNCA devolve nulo -- ele sintetiza uma entrada `NaoPortada` pra qualquer id
	/// desconhecido, e essa e a armadilha: perguntar "existe?" da sempre sim. O que se afirma aqui
	/// e o MODO, que e a unica coisa que separa uma tecnica registrada de um id inventado.
	/// </summary>
	private void OCatalogoConheceAsCatorze()
	{
		GD.Print("[arsenal] -- 1) O CATALOGO CONHECE AS CATORZE, E O GATE CONTINUA FECHADO");

		var todos = new List<string>();
		foreach ((string v, _, _) in PorDegrauG5) todos.Add(v);
		foreach ((string v, _) in PorSkillG5) todos.Add(v);

		AfirmarArs("as catorze do lote sao exatamente as catorze desta bancada",
				   todos.Count == 14 && todos.TrueForAll(EhDoLoteG5),
				   $"{todos.Count} verbos");

		var mudas = todos.FindAll(v => Tecnicas.Get(v)!.Modo == Modo.NaoPortada);
		AfirmarArs("...e nenhuma delas continua marcada como NAO-PORTADA",
				   mudas.Count == 0, string.Join(" | ", mudas));

		// O DEFEITO INJETADO: um verbo de ki que EXISTE no DM e que este lote NAO portou nao pode
		// ser reconhecido. Sem esta afirmacao, um `EhDoLoteG5` que devolvesse sempre `true` faria
		// a familia inteira passar -- e o `default` do despacho engoliria toda tecnica muda em
		// silencio, que e o oposto do que este lote existe pra consertar.
		AfirmarArs("...e uma tecnica que este lote NAO portou (Death_Ball) continua fora dele",
				   !EhDoLoteG5("Death_Ball") && Tecnicas.Get("Death_Ball")!.Modo == Modo.NaoPortada);

		Vec2 chao = CorredorLivre(24);
		ServerPlayer nu = Forjar("SemSkill", chao, bp: 5_000);
		nu.Facing = Facing.East;

		EscutaDeAvisos = [];
		UsarHabilidade(nu, "Final_Flash");
		AfirmarArs("quem NAO comprou a skill ouve \"voce nao sabe\" e nao abre canal nenhum",
				   ProjeteisDaZona(nu.Zone.Hash).Count == 0 && !_canais.ContainsKey(nu.Id)
				   && EscutaDeAvisos.Exists(a => a.Contains("nao sabe", StringComparison.OrdinalIgnoreCase)),
				   string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos.Clear();
		double kiAntes = nu.Ficha.Ki;
		UsarHabilidade(nu, "Hellzone_Grenade");
		AfirmarArs("...e a recusa nao cobra Ki nenhum", Math.Abs(nu.Ficha.Ki - kiAntes) < 0.001);
		EscutaDeAvisos = null;

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 2) O VERBO VEM DE ONDE?
	// =====================================================================
	/// <summary>
	/// OITO DOS CATORZE NAO SAO COMPRAVEIS. Eles saem de um DEGRAU de nivel (`effector()` do DM,
	/// `assignverb` dentro do `if(level >= N)`), e quem responde por eles e
	/// <see cref="NiveisDeSkill.VerbosAtivos"/> -- nao o livro.
	///
	/// ESTE ELO JA QUEBROU: `VerbosAtivos` foi escrita e ficou sem um unico chamador, e por isso um
	/// jogador que subisse `Ki_Unlocked` ate 35 continuava ouvindo "voce nao sabe Bola de Ki". Com
	/// oito das catorze deste lote dependendo do mesmo elo, ele passa a ser a metade da porta.
	/// </summary>
	private void OVerboVemDoDegrauOuDaSkill()
	{
		GD.Print("[arsenal] -- 2) OITO VERBOS SAO DO DEGRAU, SEIS SAO DA SKILL");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer pl = Forjar("Estudioso", chao, bp: 5_000);

		var semNivel = new List<string>();
		foreach ((string v, _, _) in PorDegrauG5) if (SabeTecnica(pl, v)) semNivel.Add(v);
		AfirmarArs("com o livro e a ficha de niveis VAZIOS, nenhum dos oito e reconhecido",
				   semNivel.Count == 0, string.Join(" | ", semNivel));

		// SOBE OS DEGRAUS pelo caminho do disco (`DoSave`), que e por onde o login os repoe.
		var save = new NivelSave();
		foreach ((_, string path, int nivel) in PorDegrauG5) save.Skills[path] = [nivel, 0];
		pl.Niveis.DoSave(save);

		var faltando = new List<string>();
		foreach ((string v, _, _) in PorDegrauG5) if (!SabeTecnica(pl, v)) faltando.Add(v);
		AfirmarArs("...e com o degrau cruzado os OITO passam a ser reconhecidos",
				   faltando.Count == 0, string.Join(" | ", faltando));

		AfirmarArs("...e o menu do cliente enxerga os mesmos oito (uma lista so, nao duas)",
				   PorDegrauG5.All(t => TecnicasDe(pl).Contains(t.Verbo)));

		// OS SEIS DA SKILL: `Dar()` e o caminho do ENSINO -- e e por ele que um cargo concede.
		var semSkill = new List<string>();
		foreach ((string v, _) in PorSkillG5) if (SabeTecnica(pl, v)) semSkill.Add(v);
		AfirmarArs("os seis da skill tambem comecam fechados",
				   semSkill.Count == 0, string.Join(" | ", semSkill));

		foreach ((_, string path) in PorSkillG5) pl.Livro.Dar(path);

		var naoAbriu = new List<string>();
		foreach ((string v, _) in PorSkillG5) if (!SabeTecnica(pl, v)) naoAbriu.Add(v);
		AfirmarArs("...e aprender a skill abre os SEIS (cinco deles sao de CARGO)",
				   naoAbriu.Count == 0, string.Join(" | ", naoAbriu));

		LimparTudoDaBancada();
	}

	/// <summary>Um corpo com as catorze destravadas -- o ponto de partida das familias 3 a 6.</summary>
	private ServerPlayer ForjarArmado(string nome, Vec2 onde, double bp)
	{
		ServerPlayer pl = Forjar(nome, onde, bp);
		var save = new NivelSave();
		foreach ((_, string path, int nivel) in PorDegrauG5) save.Skills[path] = [nivel, 0];
		pl.Niveis.DoSave(save);
		foreach ((_, string path) in PorSkillG5) pl.Livro.Dar(path);

		// O KI DAS TECNICAS CARAS: a Paralysis pede 700 x `BaseDrain` e a Hellzone chega perto disso
		// vezes o numero de bolas. Sem esta linha metade das familias mediria a recusa por falta de
		// energia, que ja tem bancada propria na `--projetilteste`.
		pl.Ficha.MaxKi = Math.Max(pl.Ficha.MaxKi, 5_000_000);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		return pl;
	}

	// =====================================================================
	// 3) OS QUATRO RAIOS
	// =====================================================================
	private void OsQuatroRaios()
	{
		GD.Print("[arsenal] -- 3) OS QUATRO RAIOS: CANAL, CUSTO E OS DEGRAUS DO FINAL FLASH");

		Vec2 chao = CorredorLivre(40);
		ServerPlayer pl = ForjarArmado("Raiador", chao, bp: 50_000);
		pl.Facing = Facing.East;

		foreach (string verbo in new[] { "Masenko", "Makkankosappo", "Massive_Beam", "Final_Flash" })
		{
			UsarHabilidade(pl, verbo);
			bool abriu = _canais.ContainsKey(pl.Id);
			AfirmarArs($"{verbo}: apertar abre o canal (o corpo comeca a reunir energia)", abriu);
			UsarHabilidade(pl, verbo);   // apertar de novo solta
			AfirmarArs($"...e apertar de novo o fecha", !_canais.ContainsKey(pl.Id));
		}

		// O `custoPorTiro` -- o parametro que o `Canalizar` nao tinha. `lastbeamcost` do Massive Beam
		// e `150*BaseDrain` contra os `10*BaseDrain` do Ki Wave: QUINZE VEZES por ciclo de 0,2 s.
		pl.Livro.Dar("/datum/skill/ki/Ki_Wave");
		UsarHabilidade(pl, "Ki_Wave");
		double cicloWave = CustoDeCicloDoCanal(pl);
		UsarHabilidade(pl, "Ki_Wave");

		UsarHabilidade(pl, "Massive_Beam");
		double cicloMassivo = CustoDeCicloDoCanal(pl);
		UsarHabilidade(pl, "Massive_Beam");

		AfirmarArs("o Raio Colossal drena 15x o Ki Wave por ciclo (o `lastbeamcost` do DM chegou)",
				   cicloWave > 0 && Math.Abs(cicloMassivo / cicloWave - 15) < 0.5,
				   $"wave {cicloWave:0.###} | colossal {cicloMassivo:0.###} | razao {cicloMassivo / Math.Max(cicloWave, 1e-9):0.##}");

		// O DEFEITO DO DM: `else if(usr.beamskill==100)` -- igualdade exata num float que sobe em
		// fracoes. Acima de 100 nenhum ramo casa e o verb inteiro nao faz nada. Aqui tem que sair.
		pl.Ficha.beamskill = 150;
		UsarHabilidade(pl, "Final_Flash");
		AfirmarArs("Final Flash com `beamskill` ACIMA de 100 ainda sai (o `==100` do DM, consertado)",
				   _canais.ContainsKey(pl.Id), $"beamskill={pl.Ficha.beamskill}");
		UsarHabilidade(pl, "Final_Flash");

		// MASENKO CONTRA MAKANKOSAPPO: o `rangemod` e a tecnica inteira. Medido no `ModsAgora`, que
		// e a funcao de producao que decide isso -- e nao repetindo 0,95 e 1,03 aqui.
		var masenko = new Projetil { ModsBase = 1, RangeMod = 0.95, MaxDistancia = 20, Distancia = 0 };
		var makan = new Projetil { ModsBase = 1, RangeMod = 1.03, MaxDistancia = 40, Distancia = 0 };
		AfirmarArs("o Masenko chega ao fim do alcance MAIS FRACO do que saiu",
				   masenko.ModsAgora() < 0.5, $"{masenko.ModsAgora():0.###}");
		AfirmarArs("...e o Makankosappo chega MAIS FORTE (o avesso, e e a unica diferenca entre eles)",
				   makan.ModsAgora() > 3, $"{makan.ModsAgora():0.###}");

		LimparTudoDaBancada();
	}

	/// <summary>O custo por ciclo do canal aberto agora. Zero quando nao ha canal.</summary>
	private double CustoDeCicloDoCanal(ServerPlayer pl)
		=> _canais.TryGetValue(pl.Id, out CanalDeKi? c) ? c.CustoPorCiclo : 0;

	// =====================================================================
	// 4) AS DUAS BARRAGENS
	// =====================================================================
	private void AsDuasBarragens()
	{
		GD.Print("[arsenal] -- 4) AS BARRAGENS: QUANTAS BOLAS, E A RECARGA COMPARTILHADA");

		Vec2 chao = CorredorLivre(40);
		ServerPlayer pl = ForjarArmado("Metralha", chao, bp: 50_000);
		pl.Facing = Facing.East;

		UsarHabilidade(pl, "Energy_Barrage");
		int cru = ProjeteisDaZona(pl.Zone.Hash).Count;
		AfirmarArs("a Barragem de Energia sai com as 10 bolas da formula do DM", cru == 10, $"{cru}");

		// A RECARGA E COMPARTILHADA (`barrageCD`). O defeito injetado: se cada verbo tivesse a sua,
		// o jogador alternaria os dois e dobraria o volume de fogo sem pagar nada por isso.
		EscutaDeAvisos = [];
		UsarHabilidade(pl, "Scattershot");
		AfirmarArs("...e o Tiro Disperso e RECUSADO em seguida: a recarga e a MESMA das duas",
				   ProjeteisDaZona(pl.Zone.Hash).Count == cru
				   && EscutaDeAvisos.Exists(a => a.Contains("faltam", StringComparison.OrdinalIgnoreCase)),
				   string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;

		// OS CAMPOS ORFAOS ACORDARAM: `bonusShots` e `volleyskill` estavam sendo escritos por 10
		// degraus cada e caiam em `EfeitosDeSkill.Desconhecidos`. Se eles nao chegassem na conta, o
		// numero de bolas seria o mesmo dos dois lados desta afirmacao.
		_volleyPronto.Remove(pl.Id);
		LimparTudoDaBancada([pl]);
		pl.Ficha.bonusShots = 5;
		UsarHabilidade(pl, "Energy_Barrage");
		int comBonus = ProjeteisDaZona(pl.Zone.Hash).Count;
		AfirmarArs("`bonusShots` (10 degraus no disco, ate hoje sem consumidor) MUDA o numero de bolas",
				   comBonus == cru + 5, $"{cru} -> {comBonus}");

		_volleyPronto.Remove(pl.Id);
		LimparTudoDaBancada([pl]);
		pl.Ficha.bonusShots = 0;
		pl.Ficha.volleyskill = 100;
		UsarHabilidade(pl, "Energy_Barrage");
		int comPericia = ProjeteisDaZona(pl.Zone.Hash).Count;
		AfirmarArs("...e `volleyskill` tambem (log natural: 100 vale +5)",
				   comPericia > cru, $"{cru} -> {comPericia}");

		// O TETO DA ZONA: a barragem inteira e recusada ANTES de cobrar, e nao entregue pela metade.
		_volleyPronto.Remove(pl.Id);
		LimparTudoDaBancada([pl]);
		List<Projetil> lista = ProjeteisDaZona(pl.Zone.Hash);
		while (lista.Count < MaxProjeteisPorZona - 3)
		{
			lista.Add(new Projetil { Id = _proximoProjetil++, Dono = pl.Id, Pos = pl.Pos });
			_projeteisVivos++;
		}
		double kiAntes = pl.Ficha.Ki;
		EscutaDeAvisos = [];
		UsarHabilidade(pl, "Energy_Barrage");
		AfirmarArs("com a zona quase cheia a barragem e RECUSADA inteira, e nao entregue pela metade",
				   lista.Count == MaxProjeteisPorZona - 3
				   && EscutaDeAvisos.Exists(a => a.Contains("saturado", StringComparison.OrdinalIgnoreCase)),
				   $"{lista.Count} tiros | {string.Join(" | ", EscutaDeAvisos)}");
		AfirmarArs("...e a recusa NAO cobrou o Ki da barragem que nao saiu",
				   Math.Abs(pl.Ficha.Ki - kiAntes) < 0.001,
				   $"{kiAntes:0.#} -> {pl.Ficha.Ki:0.#}");
		EscutaDeAvisos = null;

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 5) OS DOIS CERCOS
	// =====================================================================
	private void OsDoisCercos()
	{
		GD.Print("[arsenal] -- 5) OS CERCOS: A MINA QUE NAO ANDA E A BOLA QUE ESPERA");

		Vec2 chao = CorredorLivre(40);
		ServerPlayer pl = ForjarArmado("Cercador", chao, bp: 50_000);
		pl.Facing = Facing.East;

		EscutaDeAvisos = [];
		UsarHabilidade(pl, "Ki_Bomb");
		AfirmarArs("sem alvo marcado o cerco e recusado com motivo",
				   ProjeteisDaZona(pl.Zone.Hash).Count == 0
				   && EscutaDeAvisos.Exists(a => a.Contains("alvo", StringComparison.OrdinalIgnoreCase)),
				   string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;

		ServerPlayer vitima = Forjar("Cercado", chao + new Vec2(6 * ZoneCollision.TileSize, 0), bp: 5_000);
		pl.AlvoId = vitima.Id;

		UsarHabilidade(pl, "Ki_Bomb");
		List<Projetil> minas = [.. ProjeteisDaZona(pl.Zone.Hash)];
		AfirmarArs("com alvo, o campo minado nasce (7 bolas pela formula do DM)",
				   minas.Count == 7, $"{minas.Count}");
		AfirmarArs("...e TODAS nascem em volta do alvo, nunca em cima dele nem na mao do dono",
				   minas.TrueForAll(m => Vec2.Distance(m.Pos, vitima.Pos) > ZoneCollision.TileSize * 0.9
										 && Vec2.Distance(m.Pos, vitima.Pos) < ZoneCollision.TileSize * 3.5));
		AfirmarArs("...e nenhuma delas persegue ninguem (`homingchance = 0`)",
				   minas.TrueForAll(m => m.Alvo == 0 && m.Tipo == TipoDeProjetil.Blast));

		// A MINA NAO ANDA -- e o alcance dela NAO e consumido. Sem a saida do passo 5b ela gastaria
		// os tiles parada e sumiria em menos de um segundo, que e o oposto de um campo minado.
		Vec2 ondeNasceu = minas[0].Pos;
		double alcanceAntes = minas[0].Distancia;
		for (int i = 0; i < 60; i++) TickDosProjeteis(1.0 / 30);
		AfirmarArs("depois de 2 s a mina esta EXATAMENTE onde nasceu",
				   Vec2.Distance(minas[0].Pos, ondeNasceu) < 0.001);
		AfirmarArs("...e o alcance dela nao foi consumido: quem a apaga e o PRAZO, nao a distancia",
				   Math.Abs(minas[0].Distancia - alcanceAntes) < 0.001,
				   $"{alcanceAntes:0.#} -> {minas[0].Distancia:0.#}");
		AfirmarArs("...e ela ainda esta viva aos 2 s (o `Burnout(40)` sao 4)", minas[0].Vivo);

		for (int i = 0; i < 75; i++) TickDosProjeteis(1.0 / 30);
		AfirmarArs("...e MORREU passados os 4 s do `Burnout(40)`",
				   !minas[0].Vivo && ProjeteisDaZona(pl.Zone.Hash).Count == 0);

		// A HELLZONE: a mesma semeadura, mas as bolas ESPERAM um segundo e so entao convergem.
		_alvoPronto.Remove(pl.Id);
		UsarHabilidade(pl, "Hellzone_Grenade");
		List<Projetil> cerco = [.. ProjeteisDaZona(pl.Zone.Hash)];
		AfirmarArs("a Hellzone nasce igual, mas TELEGUIADA e marcando o alvo",
				   cerco.Count > 0 && cerco.TrueForAll(m => m.Tipo == TipoDeProjetil.Guided
														   && m.Alvo == vitima.Id));

		Vec2 partiu = cerco[0].Pos;
		for (int i = 0; i < 15; i++) TickDosProjeteis(1.0 / 30);   // meio segundo
		AfirmarArs("...e no primeiro meio segundo ela NAO se mexeu (a espera do `spawn(10)`)",
				   Vec2.Distance(cerco[0].Pos, partiu) < 0.001);

		for (int i = 0; i < 30; i++) TickDosProjeteis(1.0 / 30);   // passa de 1 s
		AfirmarArs("...e passado o segundo ela ANDA em direcao ao alvo",
				   Vec2.Distance(cerco[0].Pos, partiu) > 1
				   && Vec2.Distance(cerco[0].Pos, vitima.Pos) < Vec2.Distance(partiu, vitima.Pos),
				   $"andou {Vec2.Distance(cerco[0].Pos, partiu):0.#} px");

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 6) A PERNA TRANCADA
	// =====================================================================
	/// <summary>
	/// A PARALISIA -- e ela e a unica coisa deste lote que grava estado em QUEM LEVOU.
	///
	/// A afirmacao mais importante aqui e a da FRESTA: o DM da 1 chance em 12 por tique de o corpo
	/// escapar (`movement handler.dm:89`), e um escape que nunca sai e paralisia absoluta com um
	/// sorteio decorativo em cima. Pela regra 0.7 da casa, um limite que nunca dispara e
	/// indistinguivel de limite nenhum -- entao ele tem que disparar aqui.
	/// </summary>
	private void APernaTrancada()
	{
		GD.Print("[arsenal] -- 6) A PARALISIA: A PERNA TRANCA, MAS O BRACO NAO");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer atirador = ForjarArmado("Trancador", chao, bp: 50_000);
		atirador.Facing = Facing.East;
		ServerPlayer alvo = Forjar("Trancado", chao + new Vec2(3 * ZoneCollision.TileSize, 0), bp: 5_000);

		AfirmarArs("antes do tiro, o alvo anda", PodeMexerOCorpo(alvo));

		UsarHabilidade(atirador, "Paralysis");
		AfirmarArs("a Paralysis saiu, e ela NAO da pra defletir",
				   ProjeteisDaZona(atirador.Zone.Hash).Count == 1
				   && !ProjeteisDaZona(atirador.Zone.Hash)[0].Deflectivel
				   && ProjeteisDaZona(atirador.Zone.Hash)[0].Paralisia);

		for (int i = 0; i < 60 && !_paralisadoAte.ContainsKey(alvo.Id); i++) TickDosProjeteis(1.0 / 30);
		AfirmarArs("...e ao encostar ela tranca as pernas do alvo", _paralisadoAte.ContainsKey(alvo.Id));

		// A FRESTA DE 1 EM 12, E ELA TEM QUE DISPARAR. Duzentas consultas: com 1/12 de chance por
		// consulta, a probabilidade de nenhuma passar e ~(11/12)^200, ou seja 4e-8 -- este teste nao
		// pisca. E se o sorteio sumisse (paralisia absoluta), ele reprovaria na hora.
		int escapou = 0, preso = 0;
		for (int i = 0; i < 200; i++) { if (PodeMexerOCorpo(alvo)) escapou++; else preso++; }
		AfirmarArs("na maior parte das tentativas o corpo NAO sai do lugar",
				   preso > 150, $"{preso}/200 presas");
		AfirmarArs("...mas a fresta de 1 em 12 do DM DISPARA (nao e paralisia absoluta)",
				   escapou > 0, $"{escapou}/200 escaparam");

		// PARALISIA NAO E STUN: o funil de ATAQUE continua aberto. Este e o motivo de ela nao ter
		// entrado no `CombatState.Stun`, e sem esta linha a distincao seria so um comentario.
		AfirmarArs("...e quem esta paralisado CONTINUA podendo atacar (nao virou stun)",
				   alvo.Combate!.PodeAtacar());

		// O SEGUNDO TIRO NAO RENOVA (`if(!M.paralysistime)`). Sem esta guarda, uma barragem de
		// paralisia prende pra sempre.
		long prazo = _paralisadoAte[alvo.Id];
		_debuffPronto.Remove(atirador.Id);
		UsarHabilidade(atirador, "Stunlock");
		for (int i = 0; i < 60; i++) TickDosProjeteis(1.0 / 30);
		AfirmarArs("um SEGUNDO tiro de paralisia nao renova o prazo do primeiro",
				   _paralisadoAte.TryGetValue(alvo.Id, out long depois) && depois == prazo);

		// E ELA ACABA SOZINHA. O prazo e de 5 a 10 s; a bancada empurra o relogio consultando depois
		// de mexer no mapa direto -- o unico atalho desta bancada, e ele mexe no PRAZO e nao na
		// regra: quem responde continua sendo o `Paralisado` de producao.
		_paralisadoAte[alvo.Id] = NowMs() - 1;
		int soltas = 0;
		for (int i = 0; i < 50; i++) if (PodeMexerOCorpo(alvo)) soltas++;
		AfirmarArs("vencido o prazo, a paralisia sai sozinha e o corpo anda SEMPRE",
				   soltas == 50 && !_paralisadoAte.ContainsKey(alvo.Id), $"{soltas}/50");

		// E O CORPO QUE VAI EMBORA NAO DEIXA A TRANCA PRO PROXIMO A ENTRAR com o mesmo id.
		_paralisadoAte[alvo.Id] = NowMs() + 60_000;
		EsquecerParalisia(alvo.Id);
		AfirmarArs("...e sair do jogo apaga a tranca (o id de sessao e reciclado)",
				   PodeMexerOCorpo(alvo));

		LimparTudoDaBancada();
	}
}
