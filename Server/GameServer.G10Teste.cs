using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--g10teste` -- OS VINTE DO LOTE G10.
///
/// ============================ ELA EXERCITA A PRODUCAO, E SO ELA ============================
/// Nenhuma familia chama o efeito direto. Toda tecnica entra por <see cref="UsarHabilidade"/> ->
/// <see cref="UsarTecnica"/> -> <see cref="UsarTecnicasG10"/> -- o pacote do jogador, com o mesmo
/// `SabeTecnica` no meio. A infraestrutura (`Forjar`, `CorredorLivre`, `LimparTudoDaBancada`) e a da
/// `--projetilteste`, como a `--punhoteste`.
///
/// UMA PROVA DE EFEITO NOMEADA POR VERB, com o efeito por extenso: cada linha diz o que o DM promete
/// ("o alvo AGARRADO fica 2 s sem reagir", "o membro perde 72 em 2 s") e mede isso no corpo, no Ki,
/// na posicao ou no chao -- nunca no proprio codigo do lote. E as DUAS METADES: sem o requisito o
/// verb RECUSA e nao gasta; com ele, o efeito acontece.
///
/// O QUE NAO SE MEDE PELO HP: o dano do golpe rolado passa pelo `MeleeResolver` (pontaria, guarda,
/// esquiva). Onde a prova precisa de um golpe que entra, o atacante ganha `Precisao = 1000` -- o
/// `accuracy` do DM, que entra na mesma soma da pontaria -- e o alvo baixa a guarda. Os danos SEM
/// rolagem (o `damage_mob` e o `SpreadDamage` residuais) sao medidos exatos.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	private int _g10Ok, _g10Falhou;

	private void AfirmarG10(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _g10Ok++; GD.Print($"[g10]   OK    {oque}"); return; }
		_g10Falhou++;
		GD.PrintErr($"[g10]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// OS DEZESSEIS QUE SO O DEGRAU CONCEDE (`Assets/Data/niveis.json`). Escritos aqui, e nao lidos do
	/// json, pelo motivo da bancada do punho: ler o par do proprio arquivo faria a bancada concordar
	/// consigo mesma.
	/// </summary>
	private static readonly (string Verbo, string Path, int Nivel)[] PorDegrauG10 =
	[
		("Shock", "/datum/skill/Assassain/Precise_Arts", 2),
		("Reverb", "/datum/skill/Assassain/Reverbrate", 2),
		("Precise_Explosion", "/datum/skill/Assassain/Omae_Wa_Moe", 2),
		("Hokuto_Hyakuretsu_Ken", "/datum/skill/Assassain/Hokuto_no_Shinken", 2),
		("Trip", "/datum/skill/Assassain/Trip", 2),
		("Power_Drag", "/datum/skill/MartialSkill/Beserker", 2),
		("Revenge_Demon", "/datum/skill/MartialSkill/Beserker", 3),
		("Hold", "/datum/skill/Wrestling/Grabber", 2),
		("Suplex", "/datum/skill/Wrestling/Main_Event", 2),
		("Power_Slam", "/datum/skill/Wrestling/Rasslin", 2),
		("Clench", "/datum/skill/Wrestling/Superstar", 2),
		("Seismic_Press", "/datum/skill/barrel", 2),
		("Gigantic_Spike", "/datum/skill/barrel", 3),
		("Zanzoken_Combo", "/datum/skill/ki/Afterimage", 2),
		("Rapid_Movement", "/datum/skill/rapidmovement", 1),
		("Zanzoken_Dash", "/datum/skill/rapidmovement", 2),
	];

	/// <summary>O unico que uma SKILL concede direto (`Assets/Data/skills.json`).</summary>
	private static readonly (string Verbo, string Path)[] PorSkillG10 =
	[
		("Zanzoken_Rush", "/datum/skill/MartialSkill/Zanzoken_Rush"),
	];

	/// <summary>
	/// A TRINDADE NAO E CONCEDIDA POR DADO NENHUM: o DM escolhe o verb por `TrinityType` dentro de um
	/// `switch` que o extrator poe em quarentena (`niveis.json`, `logica`). Pra exercitar os tres pelo
	/// caminho de producao, a bancada registra uma regra de nivel SINTETICA que os concede no degrau 1 --
	/// e a apaga no fim. E o mesmo `SabeTecnica` -> `VerbosAtivos()` que o jogo usa.
	/// </summary>
	private const string PathDaTrindadeDeBancadaG10 = "/bancada/g10/TheHolyTrinity";
	private static readonly string[] VerbosDaTrindadeG10 = ["Taunt", "Counter_Taunt", "Slap"];

	public void RodarBancadaG10()
	{
		_g10Ok = _g10Falhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[g10] ================ OS GOLPES DO MOLDE DO G7 QUE ESTAVAM MUDOS (lote G10) ================");

		AfirmarG10("a zona da bancada tem colisao carregada", _pjMapa != null);

		// O CENSO E MEDIDO ANTES da regra sintetica da Trindade entrar no mapa de degraus: ela e da
		// bancada, e um censo que a visse contaria tres verbos que nenhuma skill de verdade concede.
		G10OCenso();

		RegrasDeNivel.Registrar(new RegraDeNivel
		{
			Path = PathDaTrindadeDeBancadaG10,
			Degraus = [new Degrau { Nivel = 1, Verbos = VerbosDaTrindadeG10 }],
		});

		try
		{
			G10CatalogoEFronteira();
			G10DeOndeVemOVerbo();
			G10LutaLivre();
			G10Berserker();
			G10Assassino();
			G10CorridaETeleportes();
			G10Trindade();
			G10Limpeza();
		}
		finally
		{
			LimparTudoDaBancada();
			// a regra sintetica sai do mapa de degraus: sem verbos, ela nao concede mais nada a ninguem
			RegrasDeNivel.Registrar(new RegraDeNivel { Path = PathDaTrindadeDeBancadaG10, Degraus = [] });
			EscutaDeAvisos = null;
		}

		GD.Print($"[g10] ================ {_g10Ok} passaram, {_g10Falhou} falharam ================");
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	/// <summary>Um corpo com os vinte destravados pelo caminho de producao, Ki e folego cheios, golpes que entram.</summary>
	private ServerPlayer ForjarLutadorG10(string nome, Vec2 onde, double bp, int nivelDoRush = 0)
	{
		ServerPlayer pl = Forjar(nome, onde, bp);
		var save = new NivelSave();
		foreach ((_, string path, int nivel) in PorDegrauG10)
			if (!save.Skills.TryGetValue(path, out double[]? v) || v[0] < nivel) save.Skills[path] = [nivel, 0];
		save.Skills[PathDaTrindadeDeBancadaG10] = [1, 0];
		if (nivelDoRush > 0) save.Skills[PathDoRushG10] = [nivelDoRush, 0];
		pl.Niveis.DoSave(save);
		foreach ((_, string path) in PorSkillG10) pl.Livro.Dar(path);
		pl.Livro.Dar("/datum/skill/rapidmovement");

		pl.Ficha.Ki = pl.Ficha.MaxKi = Math.Max(pl.Ficha.MaxKi, 50_000_000);
		pl.Ficha.stamina = pl.Ficha.maxstamina = Math.Max(pl.Ficha.maxstamina, 100_000);
		pl.Combate.Precisao = 1000;   // o `accuracy` do DM: o golpe rolado ENTRA, e a prova mede o efeito
		return pl;
	}

	/// <summary>Dois corpos colados, A olhando pra D (que esta na frente dele, a leste), D de guarda baixa.</summary>
	private (ServerPlayer A, ServerPlayer D) DuplaG10(double bpA = 5_000, double bpD = 5_000, float tilesEntre = 0.9f)
	{
		Vec2 chao = CorredorAndavelG10();
		ServerPlayer a = ForjarLutadorG10("Lutador", chao, bpA);
		ServerPlayer d = ForjarLutadorG10("Alvo", chao + new Vec2(ZoneCollision.TileSize * tilesEntre, 0), bpD);
		a.Facing = Facing.East;
		d.Facing = Facing.West;
		a.AlvoId = d.Id;
		d.Combate.Bloqueando = false;
		return (a, d);
	}

	private static double VidaTotalG10(ServerPlayer pl)
	{
		double v = 0;
		foreach (BodyPart p in pl.Combate.Corpo.Partes) v += p.Vida;
		return v;
	}

	private static double CustoDePunhoG10(ServerPlayer pl, double mult)
		=> pl.Ficha.Ephysoff * pl.Ficha.BaseDrain() * mult;

	private static bool Perto(double a, double b, double folga = 0.01) => Math.Abs(a - b) <= folga;

	private int CelulasCaidasG10(ZoneKey zona)
		=> _cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? c) ? c.Count : 0;

	private static string Ultimos(List<string>? avisos) => string.Join(" | ", avisos ?? []);

	/// <summary>
	/// UM CORREDOR ANDAVEL, e nao so sem parede: o <see cref="CorredorLivre"/> pergunta a colisao
	/// (`BlockedCell`), e a agua nao bloqueia a colisao -- bloqueia o PASSO (`ClasseDeAgua`, via
	/// `MoveRules.Occupied`). Sete provas deste lote ANDAM (corrida, arrasto, teleporte pra um vizinho),
	/// e um corredor em cima do mar media a agua e nao o verb. Confere a fileira e as duas vizinhas.
	/// </summary>
	/// <param name="diagonaisEm">
	/// Um indice de tile na fileira cujos QUATRO vizinhos diagonais tambem precisam estar livres -- e onde
	/// o alvo do Zanzoken Rush fica, porque o salto cai em `(x +- 1, y +- 1)` do alvo e um vizinho de
	/// parede seria "barrado por um obstaculo" por sorteio, e nao por defeito. -1 = nao exige.
	/// </param>
	private Vec2 CorredorAndavelG10(int tiles = 24, int diagonaisEm = -1)
	{
		for (int tentativa = 0; tentativa < 60; tentativa++)
		{
			Vec2 c = CorredorLivre(tiles);
			if (_pjMapa is not { } mapa) return c;
			// a fileira inteira tem que ser ANDAVEL (a agua nao e `BlockedCell`, mas barra o passo)
			bool ok = !MoveRules.PathOccupied(mapa, c, c + new Vec2((tiles - 2) * ZoneCollision.TileSize, 0));
			if (ok && diagonaisEm >= 0)
				for (int dx = -1; dx <= 1 && ok; dx += 2)
					for (int dy = -1; dy <= 1 && ok; dy += 2)
						ok &= !MoveRules.Occupied(mapa, c + new Vec2((diagonaisEm + dx) * ZoneCollision.TileSize, dy * ZoneCollision.TileSize));
			if (ok) return c;
		}
		AfirmarG10("achei um corredor ANDAVEL (sem agua na fileira) pra bancada", false, "varredura falhou");
		return CorredorLivre(tiles);
	}

	// =====================================================================
	// 1) O CATALOGO E A FRONTEIRA
	// =====================================================================
	private void G10CatalogoEFronteira()
	{
		_pjProximoCorredor = 8;   // as fileiras andaveis sao poucas; a familia anterior ja saiu do mundo
		GD.Print("[g10] -- 1) OS VINTE EXISTEM, ESTAO NO ESPELHO E NAO ESTAO MAIS NA FILA DE ESPERA");

		var todos = new List<string>();
		foreach ((string v, _, _) in PorDegrauG10) todos.Add(v);
		foreach ((string v, _) in PorSkillG10) todos.Add(v);
		todos.AddRange(VerbosDaTrindadeG10);

		AfirmarG10("os vinte do lote sao exatamente os vinte desta bancada",
				   todos.Count == 20 && todos.TrueForAll(EhDoLoteG10), $"{todos.Count} verbos");

		var mudas = todos.FindAll(v => Tecnicas.Get(v)!.Modo == Modo.NaoPortada);
		AfirmarG10("...e nenhum deles continua marcado como NAO-PORTADO", mudas.Count == 0, string.Join(" | ", mudas));

		var foraDoEspelho = todos.FindAll(v => !Tecnicas.NoEspelho.Contains(v, StringComparer.OrdinalIgnoreCase));
		AfirmarG10("...e os vinte estao no espelho do Core (o console do extrator os conta)",
				   foraDoEspelho.Count == 0, string.Join(" | ", foraDoEspelho));

		List<string> contra = CensoDeSkills.Contradicoes(Tecnicas.Vivas, CensoDeSkills.PorOutroCanal, CensoDeSkills.Esperando);
		var minhas = contra.FindAll(c => todos.Exists(v => c.StartsWith(v + ":", StringComparison.OrdinalIgnoreCase)));
		AfirmarG10("...e nenhum deles continua escrito na fila de 'esperando um sistema' (as tres listas concordam)",
				   minhas.Count == 0, string.Join(" | ", minhas));

		Vec2 chao = CorredorAndavelG10();
		ServerPlayer nu = Forjar("SemSkill", chao, bp: 5_000);
		nu.Facing = Facing.East;
		Forjar("Saco", chao + new Vec2(ZoneCollision.TileSize * 0.9f, 0), bp: 5_000);

		EscutaDeAvisos = [];
		double kiAntes = nu.Ficha.Ki;
		UsarHabilidade(nu, "Suplex");
		AfirmarG10("quem NAO destravou o verbo ouve \"voce nao sabe\", nao gasta nada e nao agarra ninguem",
				   Perto(nu.Ficha.Ki, kiAntes) && nu.AgarrandoId == 0
				   && EscutaDeAvisos.Exists(a => a.Contains("nao sabe", StringComparison.OrdinalIgnoreCase)),
				   Ultimos(EscutaDeAvisos));
		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 2) DE ONDE VEM O VERBO
	// =====================================================================
	private void G10DeOndeVemOVerbo()
	{
		_pjProximoCorredor = 8;   // as fileiras andaveis sao poucas; a familia anterior ja saiu do mundo
		GD.Print("[g10] -- 2) DEZESSEIS SAO DO DEGRAU, UM E DA SKILL, TRES SAO DA TRINDADE");

		ServerPlayer pl = Forjar("Estudioso", CorredorAndavelG10(), bp: 5_000);

		var semNada = new List<string>();
		foreach ((string v, _, _) in PorDegrauG10) if (SabeTecnica(pl, v)) semNada.Add(v);
		foreach ((string v, _) in PorSkillG10) if (SabeTecnica(pl, v)) semNada.Add(v);
		foreach (string v in VerbosDaTrindadeG10) if (SabeTecnica(pl, v)) semNada.Add(v);
		AfirmarG10("com o livro e a ficha de niveis VAZIOS, nenhum dos vinte e reconhecido",
				   semNada.Count == 0, string.Join(" | ", semNada));

		var save = new NivelSave();
		foreach ((_, string path, int nivel) in PorDegrauG10)
			if (!save.Skills.TryGetValue(path, out double[]? v) || v[0] < nivel) save.Skills[path] = [nivel, 0];
		pl.Niveis.DoSave(save);
		var faltando = new List<string>();
		foreach ((string v, _, _) in PorDegrauG10) if (!SabeTecnica(pl, v)) faltando.Add(v);
		AfirmarG10("...e com o degrau cruzado os DEZESSEIS passam a ser reconhecidos",
				   faltando.Count == 0, string.Join(" | ", faltando));

		// O DEGRAU EXATO IMPORTA: Beserker nivel 2 da o Power_Drag e NAO o Revenge_Demon (nivel 3).
		var so2 = new NivelSave();
		so2.Skills["/datum/skill/MartialSkill/Beserker"] = [2, 0];
		ServerPlayer meio = Forjar("MeioBerserker", CorredorAndavelG10(), bp: 5_000);
		meio.Niveis.DoSave(so2);
		AfirmarG10("...e o nivel 2 do Beserker da o Power_Drag mas NAO o Revenge_Demon (nivel 3)",
				   SabeTecnica(meio, "Power_Drag") && !SabeTecnica(meio, "Revenge_Demon"));

		bool rushAntesDaSkill = SabeTecnica(pl, "Zanzoken_Rush");
		pl.Livro.Dar(PathDoRushG10);
		AfirmarG10("...o Zanzoken_Rush so vem da SKILL, e aprender a skill o abre",
				   !rushAntesDaSkill && SabeTecnica(pl, "Zanzoken_Rush"));

		AfirmarG10("...e a Trindade nao vem de dado nenhum: so o degrau sintetico da bancada a concede",
				   !SabeTecnica(pl, "Taunt") && !SabeTecnica(pl, "Slap") && !SabeTecnica(pl, "Counter_Taunt"));
		save.Skills[PathDaTrindadeDeBancadaG10] = [1, 0];
		pl.Niveis.DoSave(save);
		AfirmarG10("...e com ele os tres passam pelo mesmo `SabeTecnica` do jogo",
				   SabeTecnica(pl, "Taunt") && SabeTecnica(pl, "Slap") && SabeTecnica(pl, "Counter_Taunt"));

		List<string> menu = TecnicasDe(pl);
		AfirmarG10("...e o `TecnicasDe` do servidor lista os vinte (uma lista so, nao duas)",
				   PorDegrauG10.All(t => menu.Contains(t.Verbo)) && menu.Contains("Zanzoken_Rush")
				   && VerbosDaTrindadeG10.All(menu.Contains));

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 3) A LUTA LIVRE -- todos no PRESO
	// =====================================================================
	private void G10LutaLivre()
	{
		_pjProximoCorredor = 8;   // as fileiras andaveis sao poucas; a familia anterior ja saiu do mundo
		GD.Print("[g10] -- 3) CLENCH, HOLD, POWER SLAM E SUPLEX EXIGEM ALGUEM AGARRADO");

		// SEM NINGUEM NA FRENTE: recusa, nao gasta, nao agarra.
		ServerPlayer so = ForjarLutadorG10("Sozinho", CorredorAndavelG10(), 5_000);
		so.Facing = Facing.East;
		_prontoG3.Remove(so.Id);
		EscutaDeAvisos = [];
		double kiSo = so.Ficha.Ki;
		UsarHabilidade(so, "Suplex");
		AfirmarG10("Suplex SEM ninguem pra agarrar: recusa (\"precisa ter alguem AGARRADO\"), nao cobra Ki e nao arma recarga",
				   Perto(so.Ficha.Ki, kiSo) && so.AgarrandoId == 0 && !_prontoG3.ContainsKey(so.Id)
				   && EscutaDeAvisos.Exists(a => a.Contains("AGARRADO")),
				   Ultimos(EscutaDeAvisos));
		LimparTudoDaBancada();

		// COM ALGUEM NA FRENTE: o verb agarra (o `get_me_a_grab()`) e aplica o golpe no preso.
		(ServerPlayer a, ServerPlayer d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		EscutaDeAvisos = [];
		double ki = a.Ficha.Ki, vida = VidaTotalG10(d), custo = CustoDePunhoG10(a, 15);
		d.Combate.Stun = 0;
		UsarHabilidade(a, "Suplex");
		AfirmarG10("Suplex COM alguem na frente: o verb AGARRA quem esta ali (get_me_a_grab) e o alvo vira o preso",
				   a.AgarrandoId == d.Id && d.AgarradoPorId == a.Id, Ultimos(EscutaDeAvisos));
		AfirmarG10("...o preso leva o golpe (+5) e perde vida", VidaTotalG10(d) < vida - 0.5,
				   $"vida {vida:0.#} -> {VidaTotalG10(d):0.#}");
		AfirmarG10("...e fica 2,0 s sem reagir (stunCount += 20)", d.Combate.Stun >= 1.99, $"stun {d.Combate.Stun:0.##}");
		AfirmarG10($"...e custou Ephysoff*BaseDrain*15 = {custo:0.##} de energia e armou a recarga do mob (basicCD += 15)",
				   Perto(ki - a.Ficha.Ki, custo, 0.05) && _prontoG3.ContainsKey(a.Id),
				   $"ki {ki:0.##} -> {a.Ficha.Ki:0.##}");

		// A RECARGA E DO MOB: o Clench, logo depois, cai nela sem cobrar.
		EscutaDeAvisos.Clear();
		ki = a.Ficha.Ki;
		UsarHabilidade(a, "Clench");
		AfirmarG10("...e o Clench logo em seguida cai na MESMA recarga (basicCD do mob), sem cobrar",
				   Perto(a.Ficha.Ki, ki) && EscutaDeAvisos.Exists(s => s.Contains("recompoem")), Ultimos(EscutaDeAvisos));

		// CLENCH: zera a luta pra escapar (o DM le o contador de QUEM APERTA: max(0, 0 - 4) = 0).
		_prontoG3.Remove(a.Id);
		d.ContadorDaLuta = 12;
		a.ContadorDaLuta = 0;
		vida = VidaTotalG10(d);
		custo = CustoDePunhoG10(a, 9);
		ki = a.Ficha.Ki;
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Clench");
		AfirmarG10("Clench: o preso leva +4 e a luta dele pra escapar volta a ZERO (`max(0, grabCounter_de_quem_aperta - 4)`, Wrestling Skills.dm:13)",
				   VidaTotalG10(d) < vida - 0.5 && Perto(d.ContadorDaLuta, 0) && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"contador {d.ContadorDaLuta} | vida {vida:0.#}->{VidaTotalG10(d):0.#} | {Ultimos(EscutaDeAvisos)}");

		// HOLD: cinco segundos sem reagir + contador zerado.
		_prontoG3.Remove(a.Id);
		d.ContadorDaLuta = 30;
		d.Combate.Stun = 0;
		custo = CustoDePunhoG10(a, 12);
		ki = a.Ficha.Ki;
		UsarHabilidade(a, "Hold");
		AfirmarG10("Hold: o preso fica 5,0 s sem reagir (stunCount += 50) e a luta dele volta a zero (-15 do contador de quem aperta)",
				   d.Combate.Stun >= 4.99 && Perto(d.ContadorDaLuta, 0) && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"stun {d.Combate.Stun:0.##} contador {d.ContadorDaLuta}");

		// POWER SLAM: o golpe mais forte (+10, nivel 3), no preso.
		_prontoG3.Remove(a.Id);
		vida = VidaTotalG10(d);
		custo = CustoDePunhoG10(a, 20);
		ki = a.Ficha.Ki;
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Power_Slam");
		AfirmarG10("Power Slam: o preso perde vida (+10, Type 3) e custou Ephysoff*BaseDrain*20",
				   VidaTotalG10(d) < vida - 0.5 && Perto(ki - a.Ficha.Ki, custo, 0.05)
				   && EscutaDeAvisos.Exists(s => s.Contains("esmaga")),
				   $"vida {vida:0.#}->{VidaTotalG10(d):0.#} | {Ultimos(EscutaDeAvisos)}");

		// NADA DISSO SOLTA O PRESO: os quatro golpes acontecem com o agarrao de pe.
		AfirmarG10("...e depois dos quatro golpes o preso continua nos bracos (nenhum golpe de luta livre solta)",
				   a.AgarrandoId == d.Id && d.AgarradoPorId == a.Id);

		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 4) O BERSERKER
	// =====================================================================
	private void G10Berserker()
	{
		_pjProximoCorredor = 8;   // as fileiras andaveis sao poucas; a familia anterior ja saiu do mundo
		GD.Print("[g10] -- 4) REVENGE DEMON ARREMESSA; GIGANTIC SPIKE E POWER DRAG CORREM CARREGANDO; SEISMIC PRESS RACHA O CHAO");

		// REVENGE DEMON -- longe: nao arremessa (e o Ki ja foi, como no DM: cobra antes de olhar o alvo)
		(ServerPlayer a, ServerPlayer d) = DuplaG10(tilesEntre: 3.2f);
		_prontoG3.Remove(a.Id);
		long arremessos = _arremessosFeitos;
		EscutaDeAvisos = [];
		UsarHabilidade(a, "Revenge_Demon");
		AfirmarG10("Revenge Demon com o alvo a 3 tiles: ninguem e arremessado (o verb so alcanca view(2))",
				   _arremessosFeitos == arremessos && d.TiquesDeVoo == 0, Ultimos(EscutaDeAvisos));
		LimparTudoDaBancada();

		// REVENGE DEMON -- colado: soco, jab e o alvo VOA pra frente (ThrowMe(dir, 1)).
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		arremessos = _arremessosFeitos;
		double ki = a.Ficha.Ki, custo = CustoDePunhoG10(a, 15), vida = VidaTotalG10(d);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Revenge_Demon");
		Vec2 frente = MeleeArea.Frente(Facing.East);
		AfirmarG10("Revenge Demon colado: o alvo leva o soco e o jab (+2) e e ARREMESSADO pra frente (na direcao em que voce olha)",
				   _arremessosFeitos == arremessos + 1 && d.TiquesDeVoo >= 1
				   && d.RumoDoVoo.X == frente.X && d.RumoDoVoo.Y == frente.Y && VidaTotalG10(d) < vida - 0.5,
				   $"arremessos {arremessos}->{_arremessosFeitos} voo {d.TiquesDeVoo} | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10($"...com a forca do agarrao ((expressedBP/2)*Ephysoff*Etechnique = {Agarrao.ForcaDoArremesso(a.Ficha):0}) e por 1 tique (ThrowMe(dir,1))",
				   Perto(d.ForcaDoVoo, Agarrao.ForcaDoArremesso(a.Ficha), 0.5) && d.TiquesIniciaisDoVoo == 1,
				   $"forca {d.ForcaDoVoo:0} tiques {d.TiquesIniciaisDoVoo}");
		AfirmarG10($"...e custou Ephysoff*BaseDrain*15 = {custo:0.##}", Perto(ki - a.Ficha.Ki, custo, 0.05));
		AfirmarG10("...e o `damage_mob` extra do DM continua ZERO (o runtime do `grabbee.Ephysoff` nunca deixa ele rodar)",
				   DanoExtraRevengeDemonG10 == 0);
		LimparTudoDaBancada();

		// GIGANTIC SPIKE -- sem ninguem: recusa sem cobrar.
		ServerPlayer so = ForjarLutadorG10("Sozinho", CorredorAndavelG10(), 5_000);
		so.Facing = Facing.East;
		_prontoG3.Remove(so.Id);
		ki = so.Ficha.Ki;
		Vec2 antes = so.Pos;
		UsarHabilidade(so, "Gigantic_Spike");
		AfirmarG10("Gigantic Spike sem ninguem na frente: recusa, nao cobra e nao corre",
				   Perto(so.Ficha.Ki, ki) && Vec2.Distance(so.Pos, antes) < 0.5f && so.AgarrandoId == 0);
		LimparTudoDaBancada();

		// GIGANTIC SPIKE -- so SEGURANDO (modo 1): paga e nada acontece (get_me_a_grab(1) nao levanta quem ja esta seguro).
		(a, d) = DuplaG10();
		AlternarAgarrao(a);   // modo 1
		_prontoG3.Remove(a.Id);
		ki = a.Ficha.Ki; antes = a.Pos; custo = CustoDePunhoG10(a, 12);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Gigantic_Spike");
		AfirmarG10("Gigantic Spike com o alvo apenas SEGURO (modo 1): cobra o Ki e nao faz nada -- `get_me_a_grab(1)` devolve TRUE sem levantar e o `if(grabMode==2)` falha (Beserker Skills.dm:43-46)",
				   a.ModoDoAgarrao == ModoDeAgarrao.Segurando && Perto(ki - a.Ficha.Ki, custo, 0.05)
				   && Vec2.Distance(a.Pos, antes) < 0.5f && EscutaDeAvisos.Exists(s => s.Contains("nada acontece")),
				   Ultimos(EscutaDeAvisos));
		LimparTudoDaBancada();

		// GIGANTIC SPIKE -- com alguem na frente: agarra, LEVANTA, corre, esmaga o MARCADO, racha o chao.
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		ki = a.Ficha.Ki; antes = a.Pos; custo = CustoDePunhoG10(a, 12); vida = VidaTotalG10(d);
		int caidas = CelulasCaidasG10(a.Zone);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Gigantic_Spike");
		float andou = (a.Pos.X - antes.X) / ZoneCollision.TileSize;
		AfirmarG10("Gigantic Spike com alguem na frente: agarra E levanta (modo 2) e corre pra frente carregando o corpo (que chega junto)",
				   a.ModoDoAgarrao == ModoDeAgarrao.Carregando && a.AgarrandoId == d.Id && andou >= 1
				   && Vec2.Distance(d.Pos, a.Pos) < 1f,
				   $"andou {andou:0.##} tiles | preso a {Vec2.Distance(d.Pos, a.Pos):0.#}px | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10("...e esmaga o alvo MARCADO no fim (+16+dmg -- em `target`, como o DM, que aqui e o mesmo corpo)",
				   VidaTotalG10(d) < vida - 0.5 && EscutaDeAvisos.Exists(s => s.Contains("esmaga")),
				   $"vida {vida:0.#}->{VidaTotalG10(d):0.#}");
		AfirmarG10("...e o chao em volta racha (turfs em view(Ephysoff/2+1) caem) e custou Ephysoff*BaseDrain*12",
				   CelulasCaidasG10(a.Zone) > caidas && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"celulas {caidas}->{CelulasCaidasG10(a.Zone)}");
		LimparTudoDaBancada();

		// POWER DRAG -- carrega e arrasta N tiles; o preso sai machucado.
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		ki = a.Ficha.Ki; antes = a.Pos; custo = CustoDePunhoG10(a, 12); vida = VidaTotalG10(d);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Power_Drag");
		andou = (a.Pos.X - antes.X) / ZoneCollision.TileSize;
		int distEsperada = (int)DmMath.Round(a.Ficha.Espeed + a.Ficha.Etechnique + a.Ficha.Ephysoff, 1);
		AfirmarG10($"Power Drag: agarra, levanta e arrasta o corpo por round(Espeed+Etechnique+Ephysoff) = {distEsperada} tiles, com o preso colado",
				   a.ModoDoAgarrao == ModoDeAgarrao.Carregando && andou >= Math.Min(distEsperada, 3) - 0.5
				   && Vec2.Distance(d.Pos, a.Pos) < 1f,
				   $"andou {andou:0.##} tiles | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10("...e o arrastado perde vida (os N golpes de (base+5)/N do DM viraram UM de base+5) e custou Ephysoff*BaseDrain*12",
				   VidaTotalG10(d) < vida - 0.5 && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"vida {vida:0.#}->{VidaTotalG10(d):0.#}");
		LimparTudoDaBancada();

		// SEISMIC PRESS -- golpe pesado, 2 s de atordoamento, o chao racha em view(Ephysoff).
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		ki = a.Ficha.Ki; custo = CustoDePunhoG10(a, 18); vida = VidaTotalG10(d);
		caidas = CelulasCaidasG10(a.Zone);
		d.Combate.Stun = 0;
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Seismic_Press");
		AfirmarG10("Seismic Press: o alvo leva o golpe (+15) e fica 2,0 s sem reagir (stunCount += 20)",
				   VidaTotalG10(d) < vida - 0.5 && d.Combate.Stun >= 1.99,
				   $"stun {d.Combate.Stun:0.##} | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10($"...e todo turf em view(Ephysoff = {Math.Floor(a.Ficha.Ephysoff)}) mais fraco que o BP cai; custou Ephysoff*BaseDrain*18",
				   CelulasCaidasG10(a.Zone) > caidas && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"celulas {caidas}->{CelulasCaidasG10(a.Zone)}");
		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 5) O ASSASSINO
	// =====================================================================
	private void G10Assassino()
	{
		_pjProximoCorredor = 8;   // as fileiras andaveis sao poucas; a familia anterior ja saiu do mundo
		GD.Print("[g10] -- 5) SHOCK, REVERB E PRECISE EXPLOSION DEIXAM DANO PRA DEPOIS; TRIP SO NO CHAO; HOKUTO COBRA FOLEGO");

		// SHOCK: uma dose direta agora, outra em 1,5 s.
		(ServerPlayer a, ServerPlayer d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		_atrasosG10.Clear();
		double dose = 1 + a.Ficha.Ephysoff / 2 + a.Ficha.Etechnique / 2;
		double ki = a.Ficha.Ki, custo = CustoDePunhoG10(a, 8), vida = VidaTotalG10(d);
		EscutaDeAvisos = [];
		UsarHabilidade(a, "Shock");
		AfirmarG10($"Shock: alem do golpe (+2), o alvo perde {dose:0.##} (1+Ephysoff/2+Etechnique/2) DIRETO num membro agora...",
				   VidaTotalG10(d) <= vida - dose + 0.01 && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"vida {vida:0.#}->{VidaTotalG10(d):0.#} | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10("...e a segunda dose fica AGENDADA pra 1,5 s depois (o `sleep(15)` do `spawn while(a > 0)`)",
				   _atrasosG10.Count == 1 && _atrasosG10[0].Alvo == d.Id && !_atrasosG10[0].Espalhado
				   && Perto(_atrasosG10[0].Dano, dose) && _atrasosG10[0].QuandoMs > NowMs() + 1000,
				   $"{_atrasosG10.Count} atrasos");
		vida = VidaTotalG10(d);
		foreach (AtrasoG10 at in _atrasosG10) at.QuandoMs = 0;
		PulsoG10();
		AfirmarG10($"...e quando o relogio do lote chega la, o membro perde os mesmos {dose:0.##} e a agenda esvazia",
				   VidaTotalG10(d) <= vida - dose + 0.01 && _atrasosG10.Count == 0,
				   $"vida {vida:0.#}->{VidaTotalG10(d):0.#}");
		LimparTudoDaBancada();

		// REVERB: tres ondas ESPALHADAS, uma agora e duas agendadas (2 s, 4 s).
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		_atrasosG10.Clear();
		double onda = 5 + a.Ficha.Ephysoff + a.Ficha.Etechnique;
		BodyPart torso = d.Combate.Corpo.Achar("Torso")!;
		double torsoAntes = torso.Vida;
		custo = CustoDePunhoG10(a, 12); ki = a.Ficha.Ki;
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Reverb");
		AfirmarG10($"Reverb: alem do golpe (+2), TODO membro do alvo perde {onda:0.##} (5+Ephysoff+Etechnique) agora -- o torso inclusive...",
				   torso.Vida <= torsoAntes - onda + 0.01 && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"torso {torsoAntes:0.#}->{torso.Vida:0.#} | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10("...e ficam DUAS ondas agendadas, espalhadas, com o mesmo valor",
				   _atrasosG10.Count == 2 && _atrasosG10.TrueForAll(at => at.Espalhado && Perto(at.Dano, onda) && at.Alvo == d.Id),
				   $"{_atrasosG10.Count} atrasos");
		torsoAntes = torso.Vida;
		foreach (AtrasoG10 at in _atrasosG10) at.QuandoMs = 0;
		PulsoG10();
		AfirmarG10($"...e cada onda que o relogio entrega tira mais {onda:0.##} do torso (as duas de uma vez aqui: {2 * onda:0.##})",
				   torso.Vida <= torsoAntes - 2 * onda + 0.01 && _atrasosG10.Count == 0,
				   $"torso {torsoAntes:0.#}->{torso.Vida:0.#}");
		LimparTudoDaBancada();

		// PRECISE EXPLOSION: nada agora alem do golpe; em 2 s o membro estoura por 70+Ephysoff+Etechnique.
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		_atrasosG10.Clear();
		double estouro = 70 + a.Ficha.Ephysoff + a.Ficha.Etechnique;
		custo = CustoDePunhoG10(a, 15); ki = a.Ficha.Ki;
		long t0 = NowMs();
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Precise_Explosion");
		AfirmarG10($"Precise Explosion: o golpe (+2) entra e um ESTOURO de {estouro:0.##} (70+Ephysoff+Etechnique) fica agendado pra 2 s depois, num membro so",
				   _atrasosG10.Count == 1 && !_atrasosG10[0].Espalhado && Perto(_atrasosG10[0].Dano, estouro)
				   && _atrasosG10[0].QuandoMs >= t0 + 1900 && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"{_atrasosG10.Count} atrasos | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10("...e a recarga dela e a mais longa do assassino (basicCD += 20 = 2 s)",
				   _prontoG3.TryGetValue(a.Id, out long livre) && livre - t0 >= 1900 && livre - t0 <= 2100,
				   $"{(_prontoG3.TryGetValue(a.Id, out long l2) ? l2 - t0 : -1)} ms");
		vida = VidaTotalG10(d);
		foreach (AtrasoG10 at in _atrasosG10) at.QuandoMs = 0;
		PulsoG10();
		AfirmarG10("...e quando o relogio chega la o membro estoura (o corpo perde vida) e a agenda esvazia",
				   VidaTotalG10(d) < vida - 0.5 && _atrasosG10.Count == 0,
				   $"vida {vida:0.#}->{VidaTotalG10(d):0.#}");
		LimparTudoDaBancada();

		// TRIP -- no chao: 3 s sem reagir + 1+Etechnique em cada membro (nao-letal, sem rolagem).
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		d.Voando = false;
		d.Combate.Stun = 0;
		double rasteira = 1 + a.Ficha.Etechnique;
		torso = d.Combate.Corpo.Achar("Torso")!;
		torsoAntes = torso.Vida;
		custo = CustoDePunhoG10(a, 15); ki = a.Ficha.Ki;
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Trip");
		AfirmarG10($"Trip com o alvo NO CHAO: ele fica 3,0 s sem reagir (stunCount += 30) e cada membro perde {rasteira:0.##} (1+Etechnique), sem rolagem",
				   d.Combate.Stun >= 2.99 && torso.Vida <= torsoAntes - rasteira + 0.01 && Perto(ki - a.Ficha.Ki, custo, 0.05)
				   && EscutaDeAvisos.Exists(s => s.Contains("rasteira")),
				   $"stun {d.Combate.Stun:0.##} torso {torsoAntes:0.#}->{torso.Vida:0.#} | {Ultimos(EscutaDeAvisos)}");
		LimparTudoDaBancada();

		// TRIP -- voando: nada (e o Ki ja foi, como no DM).
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		d.Voando = true;
		d.Combate.Stun = 0;
		vida = VidaTotalG10(d);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Trip");
		AfirmarG10("Trip com o alvo VOANDO: nao atordoa e nao machuca (nao ha chao pra tropecar) -- o requisito e o chao",
				   d.Combate.Stun < 1.0 && VidaTotalG10(d) >= vida - 0.01 && EscutaDeAvisos.Exists(s => s.Contains("no ar")),
				   $"stun {d.Combate.Stun:0.##} | {Ultimos(EscutaDeAvisos)}");
		LimparTudoDaBancada();

		// HOKUTO -- sem folego: recusa e nada muda.
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		_ultiG10.Remove(a.Id);
		a.Ficha.stamina = 10;
		d.Combate.Stun = 0;
		ki = a.Ficha.Ki;
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Hokuto_Hyakuretsu_Ken");
		AfirmarG10("Hokuto com folego < 18: recusa, nao cobra folego nem Ki, nao atordoa",
				   Perto(a.Ficha.stamina, 10) && Perto(a.Ficha.Ki, ki) && d.Combate.Stun == 0 && !_ultiG10.ContainsKey(a.Id),
				   Ultimos(EscutaDeAvisos));
		LimparTudoDaBancada();

		// HOKUTO -- com folego e colado: -18 de folego, ultiCD, DEZ segundos sem reagir, Ki*20, basicCD 30.
		(a, d) = DuplaG10();
		_prontoG3.Remove(a.Id);
		_ultiG10.Remove(a.Id);
		d.Combate.Stun = 0;
		double folego = a.Ficha.stamina;
		ki = a.Ficha.Ki; custo = CustoDePunhoG10(a, 20); vida = VidaTotalG10(d);
		t0 = NowMs();
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Hokuto_Hyakuretsu_Ken");
		AfirmarG10("Hokuto colado e com folego: cobra 18 de FOLEGO, arma o ultiCD (18*Eactspeed tiques) e o alvo fica DEZ segundos sem reagir (stunCount += 100)",
				   Perto(folego - a.Ficha.stamina, 18) && _ultiG10.ContainsKey(a.Id) && d.Combate.Stun >= 9.99,
				   $"folego {folego:0.#}->{a.Ficha.stamina:0.#} stun {d.Combate.Stun:0.##} | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10($"...e a segunda metade cobra Ephysoff*BaseDrain*20 = {custo:0.##} de Ki, arma basicCD += 30 e o alvo perde vida",
				   Perto(ki - a.Ficha.Ki, custo, 0.05) && _prontoG3.TryGetValue(a.Id, out long l3) && l3 - t0 >= 2900
				   && VidaTotalG10(d) < vida - 0.5,
				   $"ki {ki:0.##}->{a.Ficha.Ki:0.##}");
		AfirmarG10("...e os dois extras do DM continuam ZERO: o `damage_mob` que pende de um BarrageAttack sem return, e o beatdown de um `unarmedskill` que nao existe",
				   DanoExtraHokutoG10 == 0 && BeatdownHokutoG10 == 0);
		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 6) A CORRIDA DE KI E OS DOIS TELEPORTES
	// =====================================================================
	private void G10CorridaETeleportes()
	{
		_pjProximoCorredor = 8;   // as fileiras andaveis sao poucas; a familia anterior ja saiu do mundo
		GD.Print("[g10] -- 6) RAPID MOVEMENT AVANCA TRES TILES; ZANZOKEN COMBO VAI PRAS COSTAS; ZANZOKEN RUSH SALTA E CANSA");

		// RAPID MOVEMENT -- sem alvo marcado: recusa, nao anda, nao cobra.
		(ServerPlayer a, ServerPlayer d) = DuplaG10(tilesEntre: 8f);
		a.AlvoId = 0;
		_prontoG3.Remove(a.Id);
		double ki = a.Ficha.Ki;
		Vec2 antes = a.Pos;
		EscutaDeAvisos = [];
		UsarHabilidade(a, "Rapid_Movement");
		AfirmarG10("Rapid Movement SEM alvo marcado: recusa (\"alvo MARCADO\"), nao anda e nao cobra",
				   Perto(a.Ficha.Ki, ki) && Vec2.Distance(a.Pos, antes) < 0.5f && EscutaDeAvisos.Exists(s => s.Contains("MARCADO")),
				   Ultimos(EscutaDeAvisos));

		// COM alvo marcado a 8 tiles: tres passos (tres tiles) na direcao dele, custo 10*BaseDrain/speed.
		a.AlvoId = d.Id;
		double kiReq = 10 * a.Ficha.BaseDrain() / a.Ficha.speed;
		ki = a.Ficha.Ki; antes = a.Pos;
		float distAntes = Vec2.Distance(a.Pos, d.Pos);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Rapid_Movement");
		float aproximou = (distAntes - Vec2.Distance(a.Pos, d.Pos)) / ZoneCollision.TileSize;
		AfirmarG10($"Rapid Movement COM alvo marcado a 8 tiles: avanca TRES tiles contra ele e cobra 10*BaseDrain/speed = {kiReq:0.##} (e nao kiReq*BaseDrain, ver o cabecalho)",
				   aproximou >= 2.5f && aproximou <= 3.5f && Perto(ki - a.Ficha.Ki, kiReq, 0.05),
				   $"aproximou {aproximou:0.##} tiles | ki {ki:0.##}->{a.Ficha.Ki:0.##} | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10("...e NAO arma recarga nenhuma: o `dashtired` so seria armado por um `stopDashing()` que o DM nunca chama",
				   !_prontoG3.ContainsKey(a.Id));

		// ZANZOKEN DASH: o mesmo verb (a flag `rapidmovement` nunca e vista pelo laco de movimento).
		antes = a.Pos; distAntes = Vec2.Distance(a.Pos, d.Pos); ki = a.Ficha.Ki;
		UsarHabilidade(a, "Zanzoken_Dash");
		float aproximou2 = (distAntes - Vec2.Distance(a.Pos, d.Pos)) / ZoneCollision.TileSize;
		AfirmarG10("Zanzoken Dash: o MESMO avanco de tres tiles e o mesmo custo (e o `rapidProc()` com uma flag que ninguem le)",
				   aproximou2 >= 2.5f && aproximou2 <= 3.5f && Perto(ki - a.Ficha.Ki, kiReq, 0.05),
				   $"aproximou {aproximou2:0.##} tiles");

		// ...e com a investida do Lariat armada (`dashtired`), a corrida recusa.
		_prontoG3[a.Id] = NowMs() + 3000;
		antes = a.Pos; ki = a.Ficha.Ki;
		UsarHabilidade(a, "Rapid_Movement");
		AfirmarG10("...e com o `dashtired` armado (a recarga que o Lariat do G7 usa) a corrida recusa sem cobrar",
				   Perto(a.Ficha.Ki, ki) && Vec2.Distance(a.Pos, antes) < 0.5f);
		LimparTudoDaBancada();

		// ZANZOKEN COMBO -- longe demais: cobra (como o DM) e nao move.
		(a, d) = DuplaG10(tilesEntre: 3f);
		int zanzorange = ZanzorangeG10(a.Ficha);
		d.Pos = a.Pos + new Vec2((zanzorange + 3.5f) * ZoneCollision.TileSize, 0);
		_prontoG3.Remove(a.Id);
		ki = a.Ficha.Ki; antes = a.Pos;
		double custo = CustoDePunhoG10(a, 4);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Zanzoken_Combo");
		AfirmarG10($"Zanzoken Combo com o alvo alem de zanzorange+2 (= {zanzorange + 2} tiles): nao move -- e cobra Ephysoff*BaseDrain*4 mesmo assim, como o DM (Physical Skills.dm:12-15)",
				   Vec2.Distance(a.Pos, antes) < 0.5f && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   Ultimos(EscutaDeAvisos));
		LimparTudoDaBancada();

		// ZANZOKEN COMBO -- no alcance: aparece do OUTRO lado do alvo, olhando pra ele.
		(a, d) = DuplaG10(tilesEntre: 3f);
		_prontoG3.Remove(a.Id);
		ki = a.Ficha.Ki; custo = CustoDePunhoG10(a, 4);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Zanzoken_Combo");
		AfirmarG10("Zanzoken Combo no alcance: voce reaparece ATRAS do alvo (o tile do outro lado, continuando a sua direcao) e vira pra ele",
				   a.Pos.X > d.Pos.X && Vec2.Distance(a.Pos, d.Pos) <= DistanciaDeParada + 1 && a.Facing == Facing.West
				   && Perto(ki - a.Ficha.Ki, custo, 0.05),
				   $"a {a.Pos.X:0},{a.Pos.Y:0} d {d.Pos.X:0},{d.Pos.Y:0} facing {a.Facing} | {Ultimos(EscutaDeAvisos)}");
		LimparTudoDaBancada();

		// ZANZOKEN RUSH -- o rushmod vem do DEGRAU (niveis.json: 2/3/4), e nasce 1.
		ServerPlayer novato = ForjarLutadorG10("Novato", CorredorAndavelG10(), 5_000);
		ServerPlayer veterano = ForjarLutadorG10("Veterano", CorredorAndavelG10(), 5_000, nivelDoRush: 3);
		ServerPlayer meio = ForjarLutadorG10("Meio", CorredorAndavelG10(), 5_000, nivelDoRush: 2);
		AfirmarG10("Zanzoken Rush: o rushmod nasce 1 e os degraus 1-3 o levam a 2/3/4 (lido do niveis.json, nao de uma escada digitada)",
				   Perto(RushmodG10(novato), 1) && Perto(RushmodG10(meio), 3) && Perto(RushmodG10(veterano), 4),
				   $"{RushmodG10(novato)} / {RushmodG10(meio)} / {RushmodG10(veterano)}");
		LimparTudoDaBancada();

		// ZANZOKEN RUSH -- sem alvo a 20 tiles: recusa sem cobrar.
		(a, d) = DuplaG10(tilesEntre: 25f);
		_rushG10.Remove(a.Id); _rushProntoG10.Remove(a.Id);
		ki = a.Ficha.Ki;
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Zanzoken_Rush");
		AfirmarG10("Zanzoken Rush com o alvo a 25 tiles: recusa (\"alvo valido a menos de vinte tiles\") e nao cobra",
				   Perto(a.Ficha.Ki, ki) && !_rushG10.ContainsKey(a.Id), Ultimos(EscutaDeAvisos));
		LimparTudoDaBancada();

		// ZANZOKEN RUSH -- com alvo a 5 tiles e nivel 2 (rushmod 3), Espeed 3: rushmax = max(round(3*ln 3), 1) = 3 saltos.
		Vec2 pista = CorredorAndavelG10(diagonaisEm: 5);
		d = ForjarLutadorG10("Alvo", pista + new Vec2(5 * ZoneCollision.TileSize, 0), 5_000);
		a = ForjarLutadorG10("Saltador", pista, 5_000, nivelDoRush: 2);
		a.Facing = Facing.East; a.AlvoId = d.Id;
		d.Facing = Facing.West; d.Combate.Bloqueando = false;
		a.Ficha.Espeed = 3;
		_rushG10.Remove(a.Id); _rushProntoG10.Remove(a.Id);
		int rushmax = Math.Max((int)DmMath.Round(RushmodG10(a) * Math.Log(a.Ficha.Espeed), 1), 1);
		double custoRush = a.Ficha.angerBuff * 5 / (a.Ficha.Ephysoff + a.Ficha.Etechnique) * a.Ficha.BaseDrain();
		double exp = a.Niveis.Exp(PathDoRushG10);
		ki = a.Ficha.Ki;
		double vida = VidaTotalG10(d);
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Zanzoken_Rush");
		AfirmarG10($"Zanzoken Rush com alvo a 5 tiles: cobra angerBuff*5/(Ephysoff+Etechnique)*BaseDrain = {custoRush:0.##}, o primeiro salto sai AGORA (voce aparece num vizinho diagonal do alvo) e golpeia",
				   Perto(ki - a.Ficha.Ki, custoRush, 0.05) && Vec2.Distance(a.Pos, d.Pos) <= 1.5f * ZoneCollision.TileSize
				   && VidaTotalG10(d) < vida - 0.5,
				   $"dist {Vec2.Distance(a.Pos, d.Pos) / ZoneCollision.TileSize:0.##} tiles | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10($"...e ficam rushmax-1 = {rushmax - 1} saltos na agenda do lote (rushmax = max(round(rushmod*ln(Espeed)),1) = {rushmax})",
				   rushmax > 1 && _rushG10.TryGetValue(a.Id, out RushG10? r) && r.Faltam == rushmax - 1,
				   $"{(_rushG10.TryGetValue(a.Id, out RushG10? r2) ? r2.Faltam : -1)} faltam");
		int pulsos = 0;
		while (_rushG10.TryGetValue(a.Id, out RushG10? rr) && pulsos < 10)
		{
			rr.ProximoMs = 0;
			PulsoG10();
			pulsos++;
		}
		AfirmarG10("...o relogio do lote entrega os saltos que faltavam e no fim vem a EXAUSTAO (currush = 2 por jumpspeed*20 tiques)",
				   !_rushG10.ContainsKey(a.Id) && _rushProntoG10.TryGetValue(a.Id, out long pronto) && pronto > NowMs()
				   && pulsos == rushmax - 1,
				   $"{pulsos} pulsos");
		AfirmarG10("...a skill ganhou exp pelo rush (o `if(savant.currush) exp+=1` do efetor, por salto e pela exaustao)",
				   a.Niveis.Exp(PathDoRushG10) > exp || a.Niveis.Nivel(PathDoRushG10) > 2,
				   $"exp {exp:0.#} -> {a.Niveis.Exp(PathDoRushG10):0.#} nivel {a.Niveis.Nivel(PathDoRushG10)}");
		ki = a.Ficha.Ki;
		EscutaDeAvisos.Clear();
		UsarHabilidade(a, "Zanzoken_Rush");
		AfirmarG10("...e usar de novo durante a exaustao recusa (\"exausto\") sem cobrar",
				   Perto(a.Ficha.Ki, ki) && EscutaDeAvisos.Exists(s => s.Contains("exausto")), Ultimos(EscutaDeAvisos));
		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 7) A TRINDADE
	// =====================================================================
	private void G10Trindade()
	{
		_pjProximoCorredor = 8;   // as fileiras andaveis sao poucas; a familia anterior ja saiu do mundo
		GD.Print("[g10] -- 7) TAUNT VIRA A MIRA DOS OUTROS PRA VOCE; SLAP PASMA; COUNTER TAUNT MACHUCA A MENTE");

		Vec2 chao = CorredorAndavelG10();
		ServerPlayer p = ForjarLutadorG10("Provocador", chao, 5_000);
		ServerPlayer m = ForjarLutadorG10("Brigao", chao + new Vec2(3 * ZoneCollision.TileSize, 0), 5_000);
		ServerPlayer n = ForjarLutadorG10("Pacato", chao + new Vec2(-3 * ZoneCollision.TileSize, 0), 5_000);
		ServerPlayer x = ForjarLutadorG10("Outro", chao + new Vec2(0, 3 * ZoneCollision.TileSize), 5_000);
		m.AlvoId = x.Id;          // o Brigao ja luta com o Outro
		n.AlvoId = 0;             // o Pacato nao luta com ninguem
		m.Ficha.Ewillpower = 0;   // prob(100 - 0*20) = prob(100): sem dado na prova
		n.Ficha.Ewillpower = 0;
		p.Facing = Facing.East;

		// um cidadao com rancor de OUTRO: o `M.target` do NPC pacifico e o rancor de pe
		ServerPlayer? cidadao = null;
		if (_moldes?.Get("cidadao") is { } molde)
		{
			cidadao = Forjar("Cidadao", chao + new Vec2(0, -3 * ZoneCollision.TileSize), 5_000);
			cidadao.Papel = new PapelDeNpc(molde, 7);
			cidadao.Ficha.Ewillpower = 0;
			MarcarAgressao(cidadao, x);
		}

		// TAUNT -- sem Ki suficiente: recusa (o DM confere o Ki e nao cobra; a conferencia vale).
		_prontoG3.Remove(p.Id);
		double kiGuardado = p.Ficha.Ki;
		p.Ficha.Ki = 0;
		EscutaDeAvisos = [];
		UsarHabilidade(p, "Taunt");
		AfirmarG10("Taunt sem Ki pra conferir (Ki < Ephysoff*BaseDrain): recusa e ninguem muda de alvo",
				   m.AlvoId == x.Id && EscutaDeAvisos.Exists(s => s.Contains("pede")), Ultimos(EscutaDeAvisos));
		p.Ficha.Ki = kiGuardado;

		// TAUNT -- com Ki: quem tinha alvo passa a mirar em voce; quem nao tinha, nao.
		_prontoG3.Remove(p.Id);
		double ki = p.Ficha.Ki;
		EscutaDeAvisos.Clear();
		UsarHabilidade(p, "Taunt");
		AfirmarG10("Taunt: quem ja lutava com alguem (o Brigao mirava o Outro) passa a mirar em VOCE -- `M.target = usr`",
				   m.AlvoId == p.Id, $"alvo do Brigao = {m.AlvoId} (provocador {p.Id}) | {Ultimos(EscutaDeAvisos)}");
		AfirmarG10("...quem nao lutava com ninguem (o Pacato) continua sem alvo -- o `if(M.target)` e o requisito",
				   n.AlvoId == 0, $"alvo do Pacato = {n.AlvoId}");
		AfirmarG10("...e o Ki e conferido e NAO cobrado (Bodybuilding.dm:194-201 nao tem `Ki -= kireq`), so o basicCD += 10 e armado",
				   Perto(p.Ficha.Ki, ki) && _prontoG3.ContainsKey(p.Id));
		if (cidadao != null)
			AfirmarG10("...e o NPC pacifico com rancor de outro passa a guardar rancor de VOCE (o `PresaDoNpc` le o UltimoAgressor enquanto RancorAte vale)",
					   cidadao.UltimoAgressor == p.Id && cidadao.RancorAte > NowMs(),
					   $"agressor {cidadao.UltimoAgressor} (provocador {p.Id})");
		else
			AfirmarG10("(sem npcs.json carregado: o ramo do NPC pacifico nao foi medido)", false, "molde 'cidadao' ausente");

		// SLAP -- quem tem alvo fica 1,5 s sem reagir; quem nao tem, nao.
		_prontoG3.Remove(p.Id);
		m.AlvoId = x.Id;
		m.Combate.Stun = 0; n.Combate.Stun = 0;
		EscutaDeAvisos.Clear();
		UsarHabilidade(p, "Slap");
		AfirmarG10("Slap: quem lutava com alguem fica 1,5 s sem reagir (stagger por 15 tiques); quem nao lutava, nao",
				   m.Combate.Stun >= 1.49 && n.Combate.Stun == 0,
				   $"Brigao {m.Combate.Stun:0.##} Pacato {n.Combate.Stun:0.##} | {Ultimos(EscutaDeAvisos)}");

		// COUNTER TAUNT -- so o SEU alvo marcado, dano mental direto, um quarto.
		_prontoG3.Remove(p.Id);
		p.AlvoId = 0;
		double vidaM = VidaTotalG10(m);
		EscutaDeAvisos.Clear();
		UsarHabilidade(p, "Counter_Taunt");
		AfirmarG10("Counter Taunt SEM alvo marcado: ninguem se machuca (o `M == usr.target` e o requisito)",
				   Perto(VidaTotalG10(m), vidaM), Ultimos(EscutaDeAvisos));
		_prontoG3.Remove(p.Id);
		p.AlvoId = m.Id;
		double mental = (CombatMath.DanoBase(p.Ficha, m.Ficha) + 2)
						* CombatMath.BpModulus(p.Ficha.expressedBP, m.Ficha.expressedBP) * 0.25;
		vidaM = VidaTotalG10(m);
		EscutaDeAvisos.Clear();
		UsarHabilidade(p, "Counter_Taunt");
		AfirmarG10($"Counter Taunt COM o Brigao marcado: ele perde {mental:0.##} de vida MENTAL num membro ((DanoBase+Type 2)*BPModulus*0,25), sem rolagem",
				   VidaTotalG10(m) <= vidaM - mental + 0.01 && VidaTotalG10(m) < vidaM,
				   $"vida {vidaM:0.##}->{VidaTotalG10(m):0.##} | {Ultimos(EscutaDeAvisos)}");

		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 8) O CENSO E A LIMPEZA
	// =====================================================================
	private void G10OCenso()
	{
		GD.Print("[g10] -- 0) O CENSO CONTA OS DEZESSETE CONCEDIDOS COMO PORTADOS (medido antes da regra sintetica)");

		if (_skills != null)
		{
			CensoDeSkills.Relatorio r = CensoDeSkills.Levantar(_skills, RegrasDeNivel.VerbosDeDegrau);
			var concedidos = new List<string>();
			foreach ((string v, _, _) in PorDegrauG10) concedidos.Add(v);
			foreach ((string v, _) in PorSkillG10) concedidos.Add(v);
			var naoPortados = concedidos.FindAll(v =>
				!r.Verbos.Exists(l => string.Equals(l.Verbo, v, StringComparison.OrdinalIgnoreCase)
									  && l.Situacao == CensoDeSkills.Situacao.Portada));
			AfirmarG10("o censo (`--censoteste`/`efeitos`) conta os DEZESSETE verbos concedidos deste lote como PORTADOS",
					   naoPortados.Count == 0, string.Join(" | ", naoPortados));
			AfirmarG10("...e os tres da Trindade NAO entram na conta dele (nenhuma skill os concede por dado -- e o que falta, e esta dito)",
					   !r.Verbos.Exists(l => VerbosDaTrindadeG10.Contains(l.Verbo, StringComparer.OrdinalIgnoreCase)));
		}
		else AfirmarG10("o catalogo de skills carregou (sem ele o censo nao mede nada)", false);
	}

	private void G10Limpeza()
	{
		_pjProximoCorredor = 8;   // as fileiras andaveis sao poucas; a familia anterior ja saiu do mundo
		GD.Print("[g10] -- 8) QUEM SAI LEVA O ESTADO");

		// QUEM SAI LEVA O ESTADO: id se reusa.
		(ServerPlayer a, ServerPlayer d) = DuplaG10();
		_ultiG10[a.Id] = NowMs() + 9999;
		_rushProntoG10[a.Id] = NowMs() + 9999;
		_atrasosG10.Add(new AtrasoG10 { Autor = a.Id, Alvo = d.Id, QuandoMs = NowMs() + 9999, Dano = 1 });
		EsquecerG10(a.Id);
		AfirmarG10("EsquecerG10 apaga o ultiCD, a exaustao do rush e os danos agendados de quem saiu",
				   !_ultiG10.ContainsKey(a.Id) && !_rushProntoG10.ContainsKey(a.Id)
				   && !_atrasosG10.Exists(at => at.Autor == a.Id || at.Alvo == a.Id));
		_atrasosG10.Clear();
		LimparTudoDaBancada();
	}
}
