using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Items;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--g12teste` -- OS ONZE VERBOS E A PASSIVA DO LOTE G12.
///
///     Godot --headless --path . --server --port 7961 --g12teste
///
/// ============================ ELA EXERCITA A PRODUCAO, E SO ELA ============================
/// Toda tecnica entra por <see cref="UsarHabilidade"/> -> <see cref="UsarTecnica"/> ->
/// <see cref="UsarTecnicasG12"/>, o caminho do pacote do jogador, com o `SabeTecnica` no meio; os
/// verbos de menu entram por <see cref="ComandoG12"/> (o `C2S.Verbo`); o item, por
/// <see cref="ComandoDeItem"/>. O tempo anda pelos MESMOS tiques do servidor (`TickG12`,
/// `TickDosProjeteis`, `TickDosCorposSemDono`, `TickCombate`), chamados na mao -- e o unico
/// privilegio, e e o que faz 60 s de jogo caberem em 1800 voltas de laco.
///
/// A infraestrutura (`Forjar`, `CorredorLivre`, `LimparTudoDaBancada`) e a da `--projetilteste`.
/// ==========================================================================================
///
/// ============================ UMA PROVA DE EFEITO NOMEADO POR VERBO ============================
///  1. o CATALOGO e o GATE (quem nao comprou ouve "voce nao sabe"; quem comprou, ou cruzou o degrau, e reconhecido);
///  2. DEATH BALL: nasce INERTE sobre a cabeca, quatro estagios com quatro escalas CRESCENTES e o dano
///     do DM (40, 20, 30, 40), depois SAI e obedece ao olhar; prende o corpo enquanto dura; o segundo
///     aperto larga a guia e a bola para;
///  3. BUSTER BARRAGE: liga, cospe duas esferas por ciclo em rumos sortidos, cobra um dreno-base por
///     ciclo, e DESLIGA no aperto, sem Ki, no nocaute e ao trocar de planeta;
///  4. AS DUAS RAJADAS: dez (e vinte) esferas por segundo, plantado, custo que sobe dez vezes, DESLIGA
///     quando a energia nao paga a proxima e ao apertar de novo; a Giratoria nasce em volta e sai pra
///     frente (o defeito do DM, medido);
///  5. GENKIDAMA: 90% do Ki, 3 s de forma, pulsos de +0,1; SEM doador a escala e X, COM um doador
///     meditando e X + 0,1; o disparo sai com o poder do dono e NAO com o acumulado (`:170`);
///  6. SOUL ABSORB: a alma sai, o poder entra, a vitima MORRE um segundo depois, e a alma so sai uma vez;
///  7. DRENO DE ENERGIA: o androide caido e absorvido; o humano caido e drenado 10% a cada 0,7 s, e o
///     dreno para no golpe, no aperto e mata quem fica sem um decimo;
///  8. IMITACAO: nome, raca, genero e aparencia copiados campo a campo; volta ao proprio no segundo aperto;
///  9. DIVISAO DO CORPO: a copia EXISTE, tem cerebro, metade do poder, obedece a seguir / atacar /
///     desfazer, some no prazo, e o dono perde poder por copia viva;
/// 10. SENZU: a semente aparece na mochila aos 60 s E NAO ANTES; comer cura; acudir levanta;
/// 11. ALVOS DE KI: meditando, um alvo nasce a cada 3,5 s, vaga, some em 5 s, e o SOCO o acerta;
/// 12. PRECOGNICAO: quem tem a skill da um passo perpendicular ao tiro inimigo; quem nao tem, nao;
/// 13. RELOG e NOCAUTE derrubam tudo (`EsquecerG12` e as guardas dos tiques).
/// ================================================================================================
/// </summary>
public partial class GameServer
{
	private int _g12Ok, _g12Falhou;

	private void AfirmarG12(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _g12Ok++; GD.Print($"[g12]   OK    {oque}"); return; }
		_g12Falhou++;
		GD.PrintErr($"[g12]   FALHA {oque}   {detalhe}");
	}

	private const string PathDeathBallG12 = "/datum/skill/rank/DeathBall";
	private const string PathBusterG12 = "/datum/skill/rank/BusterBarrage";
	private const string PathSpiritBombG12 = "/datum/skill/rank/SpiritBomb";
	private const string PathSoulAbsorbG12 = "/datum/skill/demon/soulabsorb";
	private const string PathAndroidAbsorbG12 = "/datum/skill/general/androidabsorb";
	private const string PathImitationG12 = "/datum/skill/general/imitation";
	private const string PathSplitformG12 = "/datum/skill/general/splitform";
	private const string PathGrowSenzuG12 = "/datum/skill/rank/Grow_Senzu";
	private const string PathVolleyG12 = "/datum/skill/mind/Basic_Volley_Mastery";
	private const string PathKiUnlockedG12 = "/datum/skill/mind/Ki_Unlocked";

	public void RodarBancadaG12()
	{
		_g12Ok = _g12Falhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[g12] ================ LOTE G12: PROJETEIS QUE FALTAVAM E SISTEMAS PEQUENOS ================");
		AfirmarG12("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			OCatalogoEOGateG12();
			ADeathBallG12();
			OBusterBarrageG12();
			AsDuasRajadasG12();
			AGenkidamaG12();
			OSoulAbsorbG12();
			ODrenoDeEnergiaG12();
			AImitacaoG12();
			ADivisaoDoCorpoG12();
			OSenzuG12();
			OsAlvosDeKiG12();
			APrecognicaoG12();
			ORelogDerrubaTudoG12();
		}
		finally
		{
			LimparTudoG12();
		}

		GD.Print($"[g12] ================ {_g12Ok} passaram, {_g12Falhou} falharam ================");
	}

	// =====================================================================
	// A INFRAESTRUTURA
	// =====================================================================
	/// <summary>Anda N tiques de 30 Hz pelos tiques de PRODUCAO. `corpos` liga a IA e o combate (as copias precisam).</summary>
	private void TiquesG12(int n, bool corpos = false)
	{
		for (int i = 0; i < n; i++)
		{
			TickG12(Protocol.TickSeconds);
			TickDosProjeteis(Protocol.TickSeconds);
			if (corpos)
			{
				// A GRADE DE CORPOS E ESTADO DERIVADO, refeita a cada tique pelo `Tick()` de producao: sem
				// refaze-la aqui a copia esbarra num dono que a bancada teleportou pra longe -- a grade
				// ainda o tinha no lugar antigo, colado nela.
				MontarAsGrades();
				TickCombate(Protocol.TickSeconds);
				TickDosCorposSemDono(Protocol.TickSeconds);
			}
		}
	}

	private static int TiquesDeG12(double segundos) => (int)Math.Ceiling(segundos / Protocol.TickSeconds);

	/// <summary>
	/// UM CORREDOR DE N TILES DE TERRA FIRME pra direita: sem parede, sem AGUA e longe da borda, nas
	/// tres fileiras (a de cima e a de baixo tambem, porque a caixa do corpo encosta nelas).
	///
	/// O <see cref="CorredorLivre"/> so promete que nao ha PAREDE -- e boa parte da Terra e mar. Um
	/// tiro atravessa o mar; uma copia que anda A PE (`ModoDeTravessia.APe`) para na primeira onda,
	/// e foi assim que o 'Follow' mediu 160 -> 160 px com o cerebro decidindo certo o tempo todo.
	/// </summary>
	private Vec2 TerraFirmeG12(int tiles)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return CorredorLivre(tiles);
		for (int y = _pjProximoCorredor; y < 250; y++)
			for (int x = 4; x < 250 - tiles; x++)
			{
				bool ok = true;
				for (int i = -1; i <= tiles && ok; i++)
					for (int dy = -1; dy <= 1 && ok; dy++)
						ok &= !mapa.BlockedCell(x + i, y + dy) && !mapa.EhAgua(x + i, y + dy) && !mapa.NaBorda(x + i, y + dy);
				if (!ok) continue;
				_pjProximoCorredor = y + 3;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}
		AfirmarG12("achei um corredor de terra firme no mapa da bancada", false, "varredura falhou");
		return CorredorLivre(tiles);
	}

	/// <summary>Um corpo com as skills pedidas, Ki cheio. `degraus` sao (path, nivel).</summary>
	private ServerPlayer ForjarG12(string nome, Vec2 onde, double bp, string[]? skills = null,
								  (string Path, int Nivel)[]? degraus = null)
	{
		ServerPlayer pl = Forjar(nome, onde, bp);
		// UM TANQUE DE VERDADE: o `Forjar` nasce com `baseKi = 100` (MaxKi 100), e a Death Ball pede
		// `150*BaseDrain` -- 173 num tanque desses. Cinco mil e o Ki de quem ja treinou um pouco.
		pl.Ficha.baseKi = 5_000;
		if (skills != null) foreach (string s in skills) pl.Livro!.Dar(s);
		if (degraus != null)
		{
			var save = new NivelSave();
			foreach ((string path, int nivel) in degraus) save.Skills[path] = [nivel, 0];
			pl.Niveis.DoSave(save);
		}
		pl.Ficha.Statify();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.Tick(agoraMs: NowMs());
		return pl;
	}

	/// <summary>Aperta o verb pelo funil de producao e devolve o que o servidor DISSE.</summary>
	private List<string> ApertarG12(ServerPlayer pl, string id)
	{
		EscutaDeAvisos = [];
		UsarHabilidade(pl, id);
		List<string> falou = EscutaDeAvisos;
		EscutaDeAvisos = null;
		return falou;
	}

	private static bool Disse(List<string> falas, string trecho) =>
		falas.Exists(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	/// <summary>Derruba um corpo: o `KO()` do DM, pela porta do combate.</summary>
	private static void DerrubarG12(ServerPlayer pl) => pl.Combate.Nocautear(60);

	private List<Projetil> TirosDeG12(ServerPlayer pl) =>
		ProjeteisDaZona(pl.Zone.Hash).FindAll(p => p.Vivo && p.Dono == pl.Id);

	private void LimparTudoG12()
	{
		foreach (ServerPlayer pl in _players.Values.ToList())
			if (pl.Id >= IdBaseDeProjetil) { DestruirSplitformsG12(pl); EsquecerG12(pl.Id); }
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 1) O CATALOGO E O GATE
	// =====================================================================
	private void OCatalogoEOGateG12()
	{
		GD.Print("[g12] -- 1) O CATALOGO E O GATE");

		var naoPortadas = new List<string>();
		foreach (string id in IdsG12)
			if (Tecnicas.Get(id) is not { Modo: not Modo.NaoPortada }) naoPortadas.Add(id);
		AfirmarG12("as onze estao registradas como PORTADAS", naoPortadas.Count == 0, string.Join(" | ", naoPortadas));

		ServerPlayer nu = ForjarG12("Nu", CorredorLivre(24), bp: 5_000);
		var passou = new List<string>();
		foreach (string id in IdsG12)
			if (!Disse(ApertarG12(nu, id), "voce nao sabe")) passou.Add(id);
		AfirmarG12("quem nao comprou nada ouve 'voce nao sabe' nos onze", passou.Count == 0, string.Join(" | ", passou));

		ServerPlayer sabe = ForjarG12("Sabe", CorredorLivre(24), bp: 5_000,
			skills: [PathDeathBallG12, PathBusterG12, PathSpiritBombG12, PathSoulAbsorbG12, PathAndroidAbsorbG12,
					 PathImitationG12, PathSplitformG12, PathGrowSenzuG12],
			degraus: [(PathVolleyG12, 50), (PathKiUnlockedG12, 50)]);
		var faltou = new List<string>();
		foreach (string id in IdsG12) if (!SabeTecnica(sabe, id)) faltou.Add(id);
		AfirmarG12("com as skills e os degraus, os onze sao reconhecidos (8 por skill, 3 por degrau)",
				   faltou.Count == 0, string.Join(" | ", faltou));

		ServerPlayer meio = ForjarG12("MeioVolei", CorredorLivre(24), bp: 5_000, degraus: [(PathVolleyG12, 20)]);
		AfirmarG12("...e o nivel 20 do Volley da as Balas Continuas mas NAO a Giratoria (que e do 50)",
				   SabeTecnica(meio, "Continuous_Energy_Bullets") && !SabeTecnica(meio, "Spin_Blast"));

		List<string> menu = TecnicasDe(sabe);
		AfirmarG12("...e o menu do cliente enxerga os onze", IdsG12.All(menu.Contains));
		LimparTudoG12();
	}

	// =====================================================================
	// 2) DEATH BALL
	// =====================================================================
	private void ADeathBallG12()
	{
		GD.Print("[g12] -- 2) DEATH BALL: quatro estagios crescentes, depois a guia");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer pl = ForjarG12("Freezer", chao, bp: 50_000, skills: [PathDeathBallG12]);
		pl.Facing = Facing.East;
		Fighter f = pl.Ficha;
		double kiAntes = f.Ki;
		double custo = 150 * f.BaseDrain();

		List<string> falou = ApertarG12(pl, "Death_Ball");
		bool ligou = _deathBallG12.TryGetValue(pl.Id, out EstadoDaDeathBallG12? db);
		AfirmarG12("o aperto abre a carga e cobra 150x o dreno-base", ligou && Math.Abs(kiAntes - custo - f.Ki) < 0.01,
				   $"ki {kiAntes:0} -> {f.Ki:0}, custo {custo:0}, falou: {string.Join(" / ", falou)}");
		if (!ligou) { LimparTudoG12(); return; }

		Projetil? bola = db!.Bola;
		AfirmarG12("a bola NASCE inerte, um tile ao NORTE da cabeca, no estagio 1",
				   bola is { Vivo: true, Inerte: true } && Math.Abs(bola.Pos.X - chao.X) < 0.5 && Math.Abs(bola.Pos.Y - (chao.Y - 32)) < 0.5
				   && db.Estagio == 1,
				   bola == null ? "sem bola" : $"pos {bola.Pos} inerte {bola.Inerte}");
		AfirmarG12("...e o corpo fica PLANTADO (`move = 0`) enquanto carrega", !PodeMexerOCorpo(pl));
		AfirmarG12("...e a bola inerte nao anda nem envelhece de verdade num segundo",
				   Avancou(() => TiquesG12(30), () => bola!.Pos, out Vec2 p0, out Vec2 p1) == false && bola!.Vivo,
				   $"{p0} -> {p1}");

		// OS QUATRO ESTAGIOS: 1,5 s cada, escala 1,25 / 1,5 / 1,75 / 2,0 e dano 40 / 20 / 30 / 40 (o defeito do DM).
		double k = Math.Log(2) / Math.Log(10);
		k *= k;   // `log10(max(kieffusionskill,2)) * log10(max(blastskill,2))` num corpo novo
		var escalas = new List<double> { bola!.EscalaVisual };
		var danos = new List<double> { bola.BaseDano / k };
		var kis = new List<double> { f.Ki };
		for (int estagio = 2; estagio <= 4; estagio++)
		{
			double kiReqAntes = db.KiReq;
			TiquesG12(TiquesDeG12(1.5) - 30);   // ja andaram 30 tiques na afirmacao de cima
			TiquesG12(31);
			if (db.Bola == null) break;
			escalas.Add(db.Bola.EscalaVisual);
			danos.Add(db.Bola.BaseDano / k);
			kis.Add(f.Ki);
			AfirmarG12($"estagio {estagio}: chega em 1,5 s, cobra KiReq/3 e sobe o KiReq em {estagio * 10} (flat)",
					   db.Estagio == estagio && Math.Abs(kis[^2] - kiReqAntes / 3 - f.Ki) < 0.01
					   && Math.Abs(db.KiReq - (kiReqAntes + estagio * 10)) < 0.01,
					   $"estagio {db.Estagio}, ki {kis[^2]:0} -> {f.Ki:0} (KiReq {kiReqAntes:0} -> {db.KiReq:0})");
			if (estagio < 4) AfirmarG12($"...e a bola do estagio {estagio} ainda esta INERTE e viva", db.Bola is { Inerte: true, Vivo: true });
		}
		AfirmarG12("as quatro escalas sao CRESCENTES: 1,25 / 1,5 / 1,75 / 2,0",
				   escalas.Count == 4 && escalas.Zip(new[] { 1.25, 1.5, 1.75, 2.0 }).All(z => Math.Abs(z.First - z.Second) < 1e-9),
				   string.Join(", ", escalas.Select(e => e.ToString("0.00"))));
		AfirmarG12("...e o dano por estagio e o do DM: 40, 20, 30, 40 (o estagio 2 vale MENOS que o 1 -- mantido)",
				   danos.Count == 4 && danos.Zip(new[] { 40.0, 20.0, 30.0, 40.0 }).All(z => Math.Abs(z.First - z.Second) < 1e-6),
				   string.Join(", ", danos.Select(d => d.ToString("0.#"))));

		// NO FIM DO ESTAGIO 4 ELA SAI: deixa de ser inerte, vai pra boca do cano e obedece ao olhar.
		Projetil? solta = db.Bola;
		AfirmarG12("depois do estagio 4 a bola SAI da mao: nao e mais inerte, vai pra frente e segue o olhar (leste)",
				   solta is { Vivo: true, Inerte: false } && db.Lancada && solta.Rumo.X > 0.99,
				   solta == null ? "sem bola" : $"inerte {solta.Inerte} rumo {solta.Rumo} lancada {db.Lancada}");
		double prazo = Math.Min(f.Ekioff * f.Ekiskill * 4.0, 120);
		AfirmarG12("...com o prazo do DM: min(Ekioff*Ekiskill*4 s, 120 s) (ela saiu ha pouco mais de um segundo)",
				   solta != null && prazo - solta.VidaRestante >= 0 && prazo - solta.VidaRestante < 1.5,
				   $"restante {solta?.VidaRestante:0.##} de {prazo:0.##}");
		Vec2 antesDeAndar = solta!.Pos;
		TiquesG12(30);
		AfirmarG12("...e ela ANDA pra leste enquanto e guiada", solta.Vivo && solta.Pos.X > antesDeAndar.X + 16,
				   $"{antesDeAndar} -> {solta.Pos}");

		// A GUIA: virar o olhar vira a bola no proximo pulso (`A.dir = usr.dir` a cada `Eactspeed/5` tiques).
		pl.Facing = Facing.North;
		TiquesG12(TiquesDeG12(Math.Max(f.Eactspeed / 5, 1) * 0.1) + 1);
		AfirmarG12("virar o olhar pro NORTE vira a bola no pulso seguinte", solta.Vivo && solta.Rumo.Y < -0.99, $"rumo {solta.Rumo}");
		AfirmarG12("...e o corpo continua plantado enquanto guia", !PodeMexerOCorpo(pl));

		// O SEGUNDO APERTO: larga a guia -- a bola PARA e se apaga em 5 s; o corpo se solta; 4x Blast_Gain.
		ApertarG12(pl, "Death_Ball");
		AfirmarG12("o segundo aperto larga a guia: a bola para onde esta e ganha o prazo de 5 s do `Burnout()`",
				   !_deathBallG12.ContainsKey(pl.Id) && solta.Rumo.LengthSquared < 1e-6f && solta.VidaRestante <= 5.0 + 1e-6,
				   $"rumo {solta.Rumo} vida {solta.VidaRestante:0.##}");
		AfirmarG12("...e o corpo se solta", PodeMexerOCorpo(pl));
		TiquesG12(TiquesDeG12(5.1));
		AfirmarG12("...e cinco segundos depois ela se apagou", !solta.Vivo && solta.Fim == FimDeProjetil.Apagou, $"vivo {solta.Vivo} fim {solta.Fim}");

		// O TERCEIRO APERTO DURANTE A CARGA: larga a guia (2o) e ENCURTA a carga (3o) -- sai no estagio de agora, parada.
		ServerPlayer curto = ForjarG12("Curto", CorredorLivre(24), bp: 50_000, skills: [PathDeathBallG12]);
		ApertarG12(curto, "Death_Ball");
		TiquesG12(TiquesDeG12(1.6));
		ApertarG12(curto, "Death_Ball");   // larga a guia
		ApertarG12(curto, "Death_Ball");   // interrompe a carga
		TiquesG12(2);
		bool saiuParada = !_deathBallG12.ContainsKey(curto.Id) && TirosDeG12(curto).Count == 1
						  && TirosDeG12(curto)[0] is { Inerte: false } b2 && b2.Rumo.LengthSquared < 1e-6f && b2.EscalaVisual == 1.5;
		AfirmarG12("apertar 2x durante a carga larga a guia e interrompe a carga: a bola do estagio 2 sai e fica parada",
				   saiuParada, $"tiros {TirosDeG12(curto).Count}");

		// O NOCAUTE DERRUBA A CARGA.
		ServerPlayer caido = ForjarG12("Caido", CorredorLivre(24), bp: 50_000, skills: [PathDeathBallG12]);
		ApertarG12(caido, "Death_Ball");
		DerrubarG12(caido);
		TiquesG12(2);
		AfirmarG12("o nocaute durante a carga desfaz a Death Ball e solta o corpo (na medida do nocaute)",
				   !_deathBallG12.ContainsKey(caido.Id) && TirosDeG12(caido).Count == 0);
		LimparTudoG12();
	}

	/// <summary>Mede uma posicao antes e depois de um passo de tempo. Verdadeiro se mudou mais de 1 px.</summary>
	private static bool Avancou(Action passo, Func<Vec2> onde, out Vec2 antes, out Vec2 depois)
	{
		antes = onde();
		passo();
		depois = onde();
		return Vec2.Distance(antes, depois) > 1;
	}

	// =====================================================================
	// 3) BUSTER BARRAGE
	// =====================================================================
	private void OBusterBarrageG12()
	{
		GD.Print("[g12] -- 3) BUSTER BARRAGE: liga, sustenta, DESLIGA");

		ServerPlayer pl = ForjarG12("Broly", PracaLivre(), bp: 50_000, skills: [PathBusterG12]);
		Fighter f = pl.Ficha;
		double kiAntes = f.Ki;
		ApertarG12(pl, "BusterBarrage");
		AfirmarG12("o aperto liga a barragem", _busterG12.ContainsKey(pl.Id));
		AfirmarG12("...e ela NAO planta o corpo (o DM nao mexe em `canmove`)", PodeMexerOCorpo(pl));

		TiquesG12(1);
		AfirmarG12("no primeiro tique sai a esfera A, cobrando um dreno-base",
				   TirosDeG12(pl).Count == 1 && Math.Abs(kiAntes - f.BaseDrain() - f.Ki) < 0.01,
				   $"tiros {TirosDeG12(pl).Count}, ki {kiAntes:0} -> {f.Ki:0} (dreno {f.BaseDrain():0.##})");
		TiquesG12(TiquesDeG12(f.Eactspeed / 4 * 0.1) + 1);
		AfirmarG12($"Eactspeed/4 tiques depois ({f.Eactspeed / 4 * 0.1:0.##} s) sai a esfera B sem cobrar de novo",
				   _busterG12.TryGetValue(pl.Id, out BusterG12? bb) && bb.Cuspidas == 2 && Math.Abs(kiAntes - f.BaseDrain() - f.Ki) < 0.01,
				   $"cuspidas {bb?.Cuspidas}, ki {f.Ki:0}, Eactspeed {f.Eactspeed:0.##}, proximo em {(bb != null ? bb.ProximoEm - _relogioG12 : double.NaN):0.###} s, fase {bb?.Fase}");
		TiquesG12(TiquesDeG12(3.0));
		List<Projetil> tiros = TirosDeG12(pl);
		BusterG12 estado = _busterG12[pl.Id];
		AfirmarG12("em tres segundos e meio saíram seis esferas (duas por ciclo de Eactspeed*3/4 tiques)",
				   estado.Cuspidas == 6, $"{estado.Cuspidas} cuspidas, {tiros.Count} vivas");
		AfirmarG12("...e as vivas VOAM (rumo nao nulo, um ou dois tiques por tile)",
				   tiros.Count >= 1 && tiros.All(t => t.Rumo.LengthSquared > 0.9f && (Math.Abs(t.SegundosPorTile - 0.1) < 1e-9 || Math.Abs(t.SegundosPorTile - 0.2) < 1e-9)),
				   $"{tiros.Count} vivas");
		AfirmarG12("...em rumos DIFERENTES (o `rand(1,8)`)", estado.RumosVistos.Count >= 2, $"{estado.RumosVistos.Count} rumos");
		AfirmarG12("...com o dano do `Create_Blast`: (0,5 + Ekioff) x Ephysoff",
				   tiros.Count >= 1 && tiros.All(t => Math.Abs(t.BaseDano - (0.5 + f.Ekioff) * f.Ephysoff) < 1e-6));

		ApertarG12(pl, "BusterBarrage");
		AfirmarG12("o segundo aperto DESLIGA", !_busterG12.ContainsKey(pl.Id));
		int vivos = TirosDeG12(pl).Count;
		TiquesG12(TiquesDeG12(2.0));
		AfirmarG12("...e desligada ela nao cospe mais nada", TirosDeG12(pl).Count <= vivos);

		// SEM KI: desliga sozinha.
		ServerPlayer seco = ForjarG12("Seco", CorredorLivre(24), bp: 50_000, skills: [PathBusterG12]);
		ApertarG12(seco, "BusterBarrage");
		TiquesG12(1);
		seco.Ficha.Ki = 0.5;
		TiquesG12(TiquesDeG12(2.0));
		AfirmarG12("com Ki abaixo de 1 a barragem DESLIGA sozinha (`if(usr.Ki>=1 ...) else blasting=0`)", !_busterG12.ContainsKey(seco.Id));

		// NOCAUTE: desliga.
		ServerPlayer caido = ForjarG12("CaidoB", CorredorLivre(24), bp: 50_000, skills: [PathBusterG12]);
		ApertarG12(caido, "BusterBarrage");
		DerrubarG12(caido);
		TiquesG12(TiquesDeG12(2.0));
		AfirmarG12("o nocaute DESLIGA a barragem", !_busterG12.ContainsKey(caido.Id));

		// TROCAR DE PLANETA: desliga.
		ServerPlayer viajante = ForjarG12("Viajante", CorredorLivre(24), bp: 50_000, skills: [PathBusterG12]);
		ApertarG12(viajante, "BusterBarrage");
		viajante.Zone = new ZoneKey(ZoneKey.KindPremade, "Namek");
		TiquesG12(TiquesDeG12(2.0));
		AfirmarG12("trocar de planeta DESLIGA a barragem", !_busterG12.ContainsKey(viajante.Id));
		viajante.Zone = ZonaDaBancadaDeProjetil;
		LimparTudoG12();
	}

	// =====================================================================
	// 4) AS DUAS RAJADAS
	// =====================================================================
	private void AsDuasRajadasG12()
	{
		GD.Print("[g12] -- 4) BALAS CONTINUAS E RAJADA GIRATORIA");

		ServerPlayer pl = ForjarG12("Metralha", CorredorLivre(24), bp: 50_000, degraus: [(PathVolleyG12, 50)]);
		pl.Facing = Facing.East;
		Fighter f = pl.Ficha;
		f.Ki = f.MaxKi = 5_000_000_000;   // a rajada custa dez vezes a entrada por esfera: sem um tanque enorme ela dura duas esferas
		double kiAntes = f.Ki;
		double entrada = 30 * f.BaseDrain();

		ApertarG12(pl, "Continuous_Energy_Bullets");
		AfirmarG12("o aperto liga a rajada, cobra 30x o dreno-base e PLANTA o corpo",
				   _voleiG12.ContainsKey(pl.Id) && Math.Abs(kiAntes - entrada - f.Ki) < 1 && !PodeMexerOCorpo(pl),
				   $"ki {kiAntes:0} -> {f.Ki:0}");
		TiquesG12(1);
		double kiDepoisDaPrimeira = f.Ki;
		AfirmarG12("a primeira esfera sai no primeiro tique, cobrando a entrada de novo (o `kireq` ainda e 30x)",
				   TirosDeG12(pl).Count == 1 && Math.Abs(kiAntes - 2 * entrada - f.Ki) < 1, $"tiros {TirosDeG12(pl).Count} ki {f.Ki:0}");
		AfirmarG12("...e a partir dai cada esfera custa `max(ln(n),1)*300*BaseDrain` -- dez vezes a entrada",
				   _voleiG12.TryGetValue(pl.Id, out EstadoDoVoleiG12? v) && Math.Abs(v.KiReq - 300 * f.BaseDrain()) < 1, $"{v?.KiReq:0}");
		TiquesG12(TiquesDeG12(1.0));
		List<Projetil> tiros = TirosDeG12(pl);
		int cuspidasC = _voleiG12.TryGetValue(pl.Id, out EstadoDoVoleiG12? vc) ? vc.Duracao : -1;
		AfirmarG12("um segundo depois saíram ~dez esferas a mais (uma por 0,1 s)", cuspidasC >= 11 && cuspidasC <= 12, $"{cuspidasC} cuspidas, {tiros.Count} vivas");
		AfirmarG12("...todas pra FRENTE (leste), um tile por tique",
				   tiros.Count >= 5 && tiros.All(t => t.Rumo.X > 0.99 && Math.Abs(t.SegundosPorTile - 0.1) < 1e-9), $"{tiros.Count} vivas");
		AfirmarG12("...nascidas num LEQUE de +-45 graus (o Y das vivas varia entre tres fileiras)",
				   tiros.Select(t => Math.Round(t.Pos.Y)).Distinct().Count() >= 2 && tiros.All(t => Math.Abs(t.Pos.Y - pl.Pos.Y) <= 32.5));
		AfirmarG12("...com o dano do DM: 0,7 x Ekioff x log10(max(blastskill,10)) x dano global x os dois logs do `mods`",
				   tiros.All(t => Math.Abs(t.BaseDano - 0.7 * f.Ekioff * Log10G12(f.blastskill, 10) * DanoDeKi.DanoGlobalDeKi * Log10G12(f.kieffusionskill, 2) * Log10G12(f.blastskill, 2)) < 1e-6));

		// APERTAR DE NOVO DESLIGA, e abre a espera de 5 s no `_volleyPronto` (o `barrageCD`).
		ApertarG12(pl, "Continuous_Energy_Bullets");
		TiquesG12(1);
		AfirmarG12("o segundo aperto DESLIGA a rajada e solta o corpo", !_voleiG12.ContainsKey(pl.Id) && PodeMexerOCorpo(pl));
		AfirmarG12("...e deixa a familia de barragem em espera por 5 s (`reload = 50`)",
				   _volleyPronto.TryGetValue(pl.Id, out long pronto) && pronto - NowMs() > 4_000 && pronto - NowMs() <= 5_000, $"{pronto - NowMs()} ms");

		// SEM KI: desliga sozinha.
		ServerPlayer seco = ForjarG12("SecoV", CorredorLivre(24), bp: 50_000, degraus: [(PathVolleyG12, 20)]);
		ApertarG12(seco, "Continuous_Energy_Bullets");
		TiquesG12(1);
		bool ligada = _voleiG12.ContainsKey(seco.Id);
		seco.Ficha.Ki = 1;   // abaixo do proximo `kireq`
		TiquesG12(2);
		AfirmarG12("quando a energia nao paga a proxima esfera a rajada DESLIGA sozinha", ligada && !_voleiG12.ContainsKey(seco.Id));

		// A GIRATORIA: duas por tique, nasce em volta, e sai TODA pra frente (o defeito do DM, medido).
		ServerPlayer giro = ForjarG12("Giro", PracaLivre(), bp: 50_000, degraus: [(PathVolleyG12, 50)]);
		giro.Facing = Facing.East;
		Fighter g = giro.Ficha;
		g.Ki = g.MaxKi = 5_000_000_000;
		double kiG = g.Ki;
		ApertarG12(giro, "Spin_Blast");
		AfirmarG12("a Giratoria cobra 50x o dreno-base na entrada", Math.Abs(kiG - 50 * g.BaseDrain() - g.Ki) < 1);
		TiquesG12(TiquesDeG12(1.0));
		List<Projetil> giros = TirosDeG12(giro);
		int cuspidas = _voleiG12.TryGetValue(giro.Id, out EstadoDoVoleiG12? vg) ? vg.Duracao : -1;
		AfirmarG12("um segundo de Giratoria sao ~vinte esferas cuspidas (duas por 0,1 s)", cuspidas >= 20 && cuspidas <= 22, $"{cuspidas} cuspidas, {giros.Count} vivas");
		AfirmarG12("...e as vivas voam a partir de um tile ADJACENTE ao corpo (ja andaram; nasceram a ate um tile)",
				   giros.Count >= 5 && giros.Select(t => (Math.Round(t.Pos.Y))).Distinct().Count() >= 2, $"{giros.Count} vivas");
		AfirmarG12("...e TODAS voam pra frente (leste) -- `walk(A, dir)` usa o `dir` do MOB; o defeito do DM fica",
				   giros.Count > 0 && giros.All(t => t.Rumo.X > 0.99));
		AfirmarG12("...com 1,1x o dano das Balas Continuas",
				   giros.All(t => Math.Abs(t.BaseDano - 1.1 * g.Ekioff * Log10G12(g.blastskill, 10) * DanoDeKi.DanoGlobalDeKi * Log10G12(g.kieffusionskill, 2) * Log10G12(g.blastskill, 2)) < 1e-6));
		ApertarG12(giro, "Spin_Blast");
		TiquesG12(1);
		AfirmarG12("...e a espera dela e de 8 s (`reload = 80`)",
				   _volleyPronto.TryGetValue(giro.Id, out long pg) && pg - NowMs() > 7_000 && pg - NowMs() <= 8_000);

		// COM A RAJADA NO AR, OUTRO TIRO E RECUSADO (o `blasting` do DM, pelo `PodeAtirar`).
		ServerPlayer duplo = ForjarG12("Duplo", CorredorLivre(24), bp: 50_000, degraus: [(PathVolleyG12, 20), (PathKiUnlockedG12, 50)]);
		duplo.Ficha.Ki = duplo.Ficha.MaxKi = 5_000_000_000;
		ApertarG12(duplo, "Continuous_Energy_Bullets");
		AfirmarG12("com a rajada no ar, a Bola de Ki e recusada ('tecnica de ki no ar')",
				   Disse(ApertarG12(duplo, "Basic_Blast"), "tecnica de ki no ar"));
		LimparTudoG12();
	}

	// =====================================================================
	// 5) GENKIDAMA
	// =====================================================================
	private void AGenkidamaG12()
	{
		GD.Print("[g12] -- 5) GENKIDAMA: cresce em pulsos, cresce com doador, sai com o poder do dono");

		// ---- SEM DOADOR ----
		ServerPlayer pl = ForjarG12("Goku", CorredorLivre(24), bp: 50_000, skills: [PathSpiritBombG12]);
		pl.Facing = Facing.East;
		Fighter f = pl.Ficha;
		double kiAntes = f.Ki;
		ApertarG12(pl, "SpiritBomb");
		bool ligou = _genkidamaG12.TryGetValue(pl.Id, out EstadoDaGenkidamaG12? g);
		AfirmarG12("o aperto cobra 90% do Ki maximo, planta o corpo e pare a esfera INERTE ao norte",
				   ligou && Math.Abs(f.Ki - (kiAntes - 0.9 * f.MaxKi)) < 0.5 && !PodeMexerOCorpo(pl)
				   && g!.Bola is { Vivo: true, Inerte: true } && Math.Abs(g.Bola.Pos.Y - (pl.Pos.Y - 32)) < 0.5,
				   $"ki {kiAntes:0} -> {f.Ki:0}");
		if (!ligou) { LimparTudoG12(); return; }
		AfirmarG12("...com dano-base 30 e NAO defletivel", g!.Bola!.BaseDano == 30 && !g.Bola.Deflectivel);
		ServerPlayer cansado = ForjarG12("Cansado", CorredorLivre(24), bp: 50_000, skills: [PathSpiritBombG12]);
		cansado.Ficha.Ki = cansado.Ficha.MaxKi * 0.5;
		AfirmarG12("...e sem Ki o verb recusa com a frase do DM ('90% da energia')", Disse(ApertarG12(cansado, "SpiritBomb"), "90%"));

		TiquesG12(TiquesDeG12(2.9));
		AfirmarG12("aos 2,9 s ela ainda esta se FORMANDO (fase 0, escala 1)", g.Fase == 0 && g.Escala == 1);
		TiquesG12(TiquesDeG12(0.2));
		AfirmarG12("aos 3 s a esfera esta formada e comeca a crescer", g.Fase == 1);
		TiquesG12(TiquesDeG12(4.0));
		double semDoador = g.Escala;
		AfirmarG12("quatro segundos de crescimento sem doador = 2 pulsos = escala 1,2 (e a bola na tela tem essa escala)",
				   Math.Abs(semDoador - 1.2) < 1e-9 && g.Bola is { Vivo: true } && Math.Abs(g.Bola.EscalaVisual - 1.2) < 1e-9,
				   $"escala {g.Escala:0.00}, bola {g.Bola?.EscalaVisual:0.00}");
		AfirmarG12("...e o poder acumulado subiu 1% por pulso", Math.Abs(g.BpAcumulado - f.expressedBP * 1.01 * 1.01) < 1e-6 * f.expressedBP);

		// O DISPARO: 2 s depois a bola sai com o poder do DONO, nao com o acumulado (`:170`), escala mantida.
		double acumulado = g.BpAcumulado;
		ApertarG12(pl, "SpiritBomb");
		AfirmarG12("o segundo aperto pede o disparo (fase 2) e a bola ainda esta parada", g.Fase == 2 && g.Bola is { Inerte: true });
		TiquesG12(TiquesDeG12(2.0) + 1);
		Projetil? bomba = g.Bola;
		AfirmarG12("2 s depois a Genkidama SAI: nao inerte, pra frente, um tile por tique, 100 s de prazo, escala mantida",
				   g.Fase == 3 && bomba is { Vivo: true, Inerte: false } && bomba.Rumo.X > 0.99 && Math.Abs(bomba.SegundosPorTile - 0.1) < 1e-9
				   && Math.Abs(bomba.VidaRestante - 100) < 0.2 && Math.Abs(bomba.EscalaVisual - semDoador) < 1e-9,
				   bomba == null ? "sem bola" : $"fase {g.Fase} inerte {bomba.Inerte} rumo {bomba.Rumo} vida {bomba.VidaRestante:0.#}");
		AfirmarG12("...e o BP dela e o expressedBP do dono, NAO o acumulado (o defeito do DM, mantido e dito)",
				   bomba != null && Math.Abs(bomba.Bp - f.expressedBP) < 1e-6 && acumulado > f.expressedBP,
				   $"bp {bomba?.Bp:0} expresso {f.expressedBP:0} acumulado {acumulado:0}");
		AfirmarG12("...e o corpo continua preso por 3 s depois do disparo", !PodeMexerOCorpo(pl));
		TiquesG12(TiquesDeG12(3.1));
		AfirmarG12("...e se solta aos 3 s", PodeMexerOCorpo(pl) && !_genkidamaG12.ContainsKey(pl.Id));
		LimparTudoG12();

		// ---- COM UM DOADOR MEDITANDO ----
		ServerPlayer dono = ForjarG12("Goku2", CorredorLivre(24), bp: 50_000, skills: [PathSpiritBombG12]);
		ServerPlayer doador = ForjarG12("Krillin", CorredorLivre(24), bp: 20_000);
		ServerPlayer distraido = ForjarG12("Yamcha", CorredorLivre(24), bp: 20_000);
		doador.Ficha.med = true;
		double kiDoador = doador.Ficha.Ki;
		ApertarG12(dono, "SpiritBomb");
		TiquesG12(TiquesDeG12(3.1));
		AfirmarG12("ao formar, quem MEDITA no mundo e convidado; quem nao medita, nao",
				   _ofertasDeGenkidamaG12.ContainsKey(doador.Id) && !_ofertasDeGenkidamaG12.ContainsKey(distraido.Id));
		EstadoDaGenkidamaG12 g2 = _genkidamaG12[dono.Id];
		double bpAntes = g2.BpAcumulado;
		AfirmarG12("quem nao foi convidado e responde ouve 'ninguem esta pedindo'",
				   ApertarVerboG12(distraido, "genkidama_doar", "ninguem esta pedindo"));
		AfirmarG12("o doador aceita pelo verb da aba Other: perde 10% do Ki e a bomba acumula expressedBP x Ki x 0,1",
				   ApertarVerboG12(doador, "genkidama_doar", "um decimo") && Math.Abs(doador.Ficha.Ki - kiDoador * 0.9) < 1e-6
				   && Math.Abs(g2.BpAcumulado - bpAntes - doador.Ficha.expressedBP * (kiDoador * 0.1)) < 1e-3,
				   $"ki {kiDoador:0} -> {doador.Ficha.Ki:0}, acumulado {bpAntes:0} -> {g2.BpAcumulado:0}");
		TiquesG12(TiquesDeG12(4.0));
		AfirmarG12("COM um doador, os mesmos 4 s de crescimento dao X + 0,1: escala 1,3 contra 1,2",
				   Math.Abs(g2.Escala - (semDoador + 0.1)) < 1e-9, $"escala {g2.Escala:0.00} (sem doador: {semDoador:0.00})");
		AfirmarG12("...e a segunda resposta do mesmo doador nao vale (a oferta foi consumida)",
				   ApertarVerboG12(doador, "genkidama_doar", "ninguem esta pedindo"));

		// A ESPERA LONGA: 20 pulsos e 110 s parados, depois mais 10 e 10 -- o teto e 40 pulsos (escala 5,0 + doadores).
		TiquesG12(TiquesDeG12(28.0));
		AfirmarG12("depois de 20 pulsos a bomba PARA de crescer (a espera de 110 s)", Math.Abs(g2.Escala - 3.1) < 1e-9 && g2.Iteracoes == 2, $"escala {g2.Escala:0.00} it {g2.Iteracoes}");
		TiquesG12(TiquesDeG12(30.0));
		AfirmarG12("...e 30 s dentro da espera ela continua na mesma escala", Math.Abs(g2.Escala - 3.1) < 1e-9);

		// O NOCAUTE DESFAZ.
		DerrubarG12(dono);
		TiquesG12(2);
		AfirmarG12("o nocaute desfaz a Genkidama e apaga a bola", !_genkidamaG12.ContainsKey(dono.Id) && TirosDeG12(dono).Count == 0);
		LimparTudoG12();
	}

	/// <summary>Aperta um verb de MENU (`C2S.Verbo`) pelo funil de producao e diz se a resposta trouxe o trecho.</summary>
	private bool ApertarVerboG12(ServerPlayer pl, string cmd, string trecho)
	{
		EscutaDeAvisos = [];
		Verbo(pl, cmd, "");
		bool ok = Disse(EscutaDeAvisos, trecho);
		EscutaDeAvisos = null;
		return ok;
	}

	// =====================================================================
	// 6) SOUL ABSORB
	// =====================================================================
	private void OSoulAbsorbG12()
	{
		GD.Print("[g12] -- 6) SOUL ABSORB: a alma sai uma vez, o poder entra, a vitima morre");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer demo = ForjarG12("Dabura", chao, bp: 50_000, skills: [PathSoulAbsorbG12]);
		ServerPlayer vitima = ForjarG12("Vitima", chao + new Vec2(28, 0), bp: 30_000);
		Fighter f = demo.Ficha;
		f.Ki = f.MaxKi * 0.3;

		AfirmarG12("contra alguem DE PE o verb recusa em voz alta ('NOCAUTEADO')", Disse(ApertarG12(demo, "Soul_Absorb"), "NOCAUTEADO"));
		DerrubarG12(vitima);
		double kiAntes = f.Ki, kiVitima = vitima.Ficha.Ki, absorbAntes = f.AbsorbBP;
		List<string> falou = ApertarG12(demo, "Soul_Absorb");
		AfirmarG12("contra o caido a alma SAI (`HasSoul = 0`)", !vitima.Ficha.HasSoul, string.Join(" / ", falou));
		AfirmarG12("...o Ki da vitima vira seu (com o teto do port)", Math.Abs(f.Ki - Math.Min(kiAntes + kiVitima, f.MaxKi)) < 1e-6, $"{kiAntes:0} + {kiVitima:0} -> {f.Ki:0}");
		AfirmarG12("...e uma parte do poder dela entra no AbsorbBP pela conta do `absorb(M,2,6)`", f.AbsorbBP > absorbAntes, $"{absorbAntes:0} -> {f.AbsorbBP:0}");
		double eff = 1;
		double esperado = vitima.Ficha.BP <= f.BP
			? f.CapCheck(eff * (vitima.Ficha.BP / Math.Max(vitima.Ficha.BPMod, 0.1)) / 6)
			: f.CapCheck(eff * (vitima.Ficha.BP / Math.Max(vitima.Ficha.BPMod, 0.1)) * f.BPMod / 3);
		double piso = eff * (vitima.Ficha.BP + vitima.Ficha.AbsorbBP) * (vitima.Ficha.Anger / 100);
		if (absorbAntes + esperado < piso) esperado += piso;
		AfirmarG12("...com o numero literal: capcheck(M.BP/M.BPMod / 6) e o piso de (M.BP + M.absorbadd) x Anger/100",
				   Math.Abs(f.AbsorbBP - absorbAntes - esperado) < 1e-3, $"esperado {esperado:0.##}, veio {f.AbsorbBP - absorbAntes:0.##}");
		AfirmarG12("...a vitima ainda esta VIVA no instante (o `spawn(10) M.Death()`)", !vitima.Ficha.dead);
		TiquesG12(TiquesDeG12(1.0) + 1);
		AfirmarG12("...e MORRE um segundo depois -- o Soul Absorb mata (o ramo comum do `absorb()`), ao contrario do que o censo dizia",
				   vitima.Ficha.dead);
		AfirmarG12("...e a vitima fica inabsorvivel por 300 s (`absorbproc`)", !AbsorvivelG12(vitima));
		AfirmarG12("...e a eficacia do absorvedor caiu pra 0,9", Math.Abs(EficaciaDeAbsorcaoG12(demo.Id) - 0.9) < 1e-9);

		// SEM ALMA NAO HA O QUE TOMAR: a segunda vitima ja sem alma.
		ServerPlayer oca = ForjarG12("Oca", chao + new Vec2(-28, 0), bp: 30_000);
		oca.Ficha.HasSoul = false;
		DerrubarG12(oca);
		TiquesG12(TiquesDeG12(2.1));   // a recarga de 2 s do `absorbing`
		AfirmarG12("um caido que ja nao tem alma e recusado ('nao tem mais alma')", Disse(ApertarG12(demo, "Soul_Absorb"), "nao tem mais alma") && !oca.Ficha.dead);
		LimparTudoG12();
	}

	// =====================================================================
	// 7) DRENO DE ENERGIA
	// =====================================================================
	private void ODrenoDeEnergiaG12()
	{
		GD.Print("[g12] -- 7) DRENO DE ENERGIA: o androide caido e absorvido, o resto e drenado");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer c17 = ForjarG12("Dezessete", chao, bp: 50_000, skills: [PathAndroidAbsorbG12]);
		ServerPlayer robo = ForjarG12("Robo", chao + new Vec2(28, 0), bp: 30_000);
		robo.Race = "Android";
		Fighter f = c17.Ficha;
		f.Ki = f.MaxKi * 0.3;
		AfirmarG12("contra alguem de pe o verb recusa ('NOCAUTEADO')", Disse(ApertarG12(c17, "Absorb_Android"), "NOCAUTEADO"));

		DerrubarG12(robo);
		double absorbAntes = f.AbsorbBP;
		ApertarG12(c17, "Absorb_Android");
		AfirmarG12("o ANDROIDE caido e absorvido inteiro: AbsorbBP sobe e o Ki e somado ate o teto do DM (MaxKi)",
				   f.AbsorbBP > absorbAntes && f.Ki <= f.MaxKi + 1e-6 && !_drenoG12.ContainsKey(c17.Id), $"{absorbAntes:0} -> {f.AbsorbBP:0}");
		TiquesG12(TiquesDeG12(1.1));
		AfirmarG12("...e ele morre um segundo depois", robo.Ficha.dead);

		// O HUMANO: dreno sustentado.
		ServerPlayer humano = ForjarG12("Humano", chao + new Vec2(-28, 0), bp: 30_000);
		DerrubarG12(humano);
		c17.AlvoId = humano.Id;
		TiquesG12(TiquesDeG12(2.1));
		f.Ki = f.MaxKi * 0.3;
		double kiH = humano.Ficha.Ki, kiC = f.Ki;
		ApertarG12(c17, "Absorb_Android");
		AfirmarG12("o HUMANO caido abre o dreno sustentado", _drenoG12.ContainsKey(c17.Id));
		TiquesG12(TiquesDeG12(0.7) + 1);
		AfirmarG12("0,7 s depois um decimo do Ki dele passou pra voce", Math.Abs(humano.Ficha.Ki - kiH * 0.9) < 1e-6 && Math.Abs(f.Ki - (kiC + kiH * 0.1)) < 1e-6,
				   $"vitima {kiH:0} -> {humano.Ficha.Ki:0}; voce {kiC:0} -> {f.Ki:0}");
		TiquesG12(TiquesDeG12(0.7));
		AfirmarG12("...e mais um decimo do que sobrou, 0,7 s depois (o dreno CONTINUA)", Math.Abs(humano.Ficha.Ki - kiH * 0.81) < 1e-6 && _drenoG12.ContainsKey(c17.Id));
		AfirmarG12("...e o AbsorbBP cresce M.BP/50 por tique", f.AbsorbBP > absorbAntes);
		ApertarG12(c17, "Absorb_Android");
		AfirmarG12("o segundo aperto PARA o dreno", !_drenoG12.ContainsKey(c17.Id));

		// O GOLPE INTERROMPE.
		ServerPlayer humano2 = ForjarG12("Humano2", chao + new Vec2(0, 28), bp: 30_000);
		DerrubarG12(humano2);
		c17.AlvoId = humano2.Id;
		TiquesG12(TiquesDeG12(2.1));
		ApertarG12(c17, "Absorb_Android");
		bool abriu = _drenoG12.ContainsKey(c17.Id);
		f.HP -= 1;   // o `prehp > usr.HP`
		TiquesG12(TiquesDeG12(0.8));
		AfirmarG12("levar dano interrompe o dreno (`if(prehp > usr.HP) break`)", abriu && !_drenoG12.ContainsKey(c17.Id));

		// SEM UM DECIMO, A VITIMA MORRE.
		ServerPlayer fraco = ForjarG12("Fraco", chao + new Vec2(0, -28), bp: 30_000);
		DerrubarG12(fraco);
		fraco.Ficha.Ki = fraco.Ficha.MaxKi * 0.11;
		c17.AlvoId = fraco.Id;
		TiquesG12(TiquesDeG12(2.1));
		ApertarG12(c17, "Absorb_Android");
		TiquesG12(TiquesDeG12(0.8));
		AfirmarG12("a vitima que fica abaixo de um decimo do Ki maximo MORRE (`spawn M.Death()`)", fraco.Ficha.dead, $"ki {fraco.Ficha.Ki:0} / {fraco.Ficha.MaxKi:0}");
		LimparTudoG12();
	}

	// =====================================================================
	// 8) IMITACAO
	// =====================================================================
	private void AImitacaoG12()
	{
		GD.Print("[g12] -- 8) IMITACAO: copia campo a campo, e volta");

		// O SOZINHO VEM PRIMEIRO: as fileiras da bancada ficam a tres tiles uma da outra, dentro dos cinco.
		AfirmarG12("sem ninguem a cinco tiles o verb recusa", Disse(ApertarG12(ForjarG12("Sozinho", CorredorLivre(24), bp: 5_000, skills: [PathImitationG12]), "Imitation"), "ninguem"));
		LimparTudoG12();

		Vec2 chao = CorredorLivre(24);
		ServerPlayer puar = ForjarG12("Puar", chao, bp: 5_000, skills: [PathImitationG12]);
		ServerPlayer modelo = ForjarG12("Modelo", chao + new Vec2(64, 0), bp: 5_000);
		modelo.Race = "Namekian";
		modelo.Genero = "Female";
		modelo.Visual.Cabelo = "Spiky";
		modelo.Visual.Corpo = 3;
		modelo.Visual.CorPele = new Jandirus.Core.Appearance.Rgb(10, 200, 30);

		string nomeReal = puar.Name;
		ApertarG12(puar, "Imitation");
		AfirmarG12("o aperto copia o NOME visivel do modelo (e o nome do save continua o seu)",
				   puar.Disfarce != null && NomeVisivel(puar) == modelo.Name && puar.Name == nomeReal);
		Jandirus.Core.Appearance.Appearance v = VisualVisivel(puar);
		AfirmarG12("...e a aparencia campo a campo: cabelo, corpo, cor de pele, raca e genero",
				   v.Cabelo == modelo.Visual.Cabelo && v.Corpo == modelo.Visual.Corpo && Equals(v.CorPele, modelo.Visual.CorPele)
				   && puar.Disfarce!.Raca == "Namekian" && puar.Disfarce.Genero == "Female");
		AfirmarG12("...por COPIA e nao por referencia (mexer no modelo depois nao muda o disfarce)",
				   ReferenceEquals(v, modelo.Visual) == false);
		AfirmarG12("...e a ficha de aparencia de verdade nao foi tocada", puar.Visual.Cabelo != "Spiky" && puar.Visual.Corpo != 3);

		ApertarG12(puar, "Imitation");
		AfirmarG12("o segundo aperto devolve o proprio rosto e o proprio nome",
				   puar.Disfarce == null && NomeVisivel(puar) == nomeReal && ReferenceEquals(VisualVisivel(puar), puar.Visual));

		// O RELOG derruba o disfarce.
		ApertarG12(puar, "Imitation");
		EsquecerG12(puar.Id);
		AfirmarG12("o relog derruba o disfarce", puar.Disfarce == null);
		LimparTudoG12();
	}

	// =====================================================================
	// 9) DIVISAO DO CORPO
	// =====================================================================
	private void ADivisaoDoCorpoG12()
	{
		GD.Print("[g12] -- 9) DIVISAO DO CORPO: a copia existe, pensa, obedece e some");

		Vec2 chao = TerraFirmeG12(24);   // a copia anda A PE: nada de mar
		ServerPlayer tien = ForjarG12("Tenshinhan", chao, bp: 50_000, skills: [PathSplitformG12]);
		tien.Facing = Facing.East;
		Fighter f = tien.Ficha;
		double expressoAntes = f.expressedBP, kiAntes = f.Ki;
		int corposAntes = _players.Count;

		ApertarG12(tien, "SplitForm");
		ServerPlayer? copia = CopiaDeG12(tien);
		AfirmarG12("o aperto poe uma copia NOVA no mundo, com cerebro (IA), o nome '<dono> Copy' e METADE do poder expresso",
				   copia != null && _players.Count == corposAntes + 1 && copia.Cerebro != null && copia.Name == tien.Name + " Copy"
				   && Math.Abs(copia.Ficha.BP - expressoAntes / 2) < 0.5,
				   copia == null ? "sem copia" : $"bp {copia.Ficha.BP:0} (metade de {expressoAntes:0} = {expressoAntes / 2:0})");
		if (copia == null) { LimparTudoG12(); return; }
		AfirmarG12("...um passo a frente do dono, com a mesma aparencia e sem tela (Peer nulo)",
				   Math.Abs(copia.Pos.X - (chao.X + 32)) < 0.5 && copia.Peer == null && copia.Visual.Cabelo == tien.Visual.Cabelo);
		AfirmarG12("...cobrando MaxKi x 0,5 / Splitformskill (skill 1)", Math.Abs(kiAntes - f.MaxKi * 0.5 - f.Ki) < 0.5, $"{kiAntes:0} -> {f.Ki:0}");
		AfirmarG12("...e o dono passa a contar UMA copia viva -- o `splitformdeBuff` corta o poder expresso dele",
				   f.splitformCount == 1 && f.expressedBP < expressoAntes, $"expresso {expressoAntes:0} -> {f.expressedBP:0}");
		f.splitformMastery = 0;   // o `prob(50/(Splitformskill*5))` pode ter subido o skill na primeira: a bancada zera o dado
		AfirmarG12("com skill 1, a segunda divisao e recusada ('pericia')", Disse(ApertarG12(tien, "SplitForm"), "pericia"));

		// SEGUIR: o dono se afasta e a copia vem atras (pela IA de producao).
		AfirmarG12("a copia nasce PARADA (o `hasAI = 0` do DM)", _splitformsG12[copia.Id].Funcao == FuncaoDeSplitformG12.Parado);
		ApertarVerboG12(tien, "splitform_seguir", "seguir");
		tien.Pos = chao + new Vec2(6 * 32, 0);
		float distAntes = Vec2.Distance(copia.Pos, tien.Pos);
		TiquesG12(TiquesDeG12(3.0), corpos: true);
		AfirmarG12("'Follow': em 3 s a copia se aproximou do dono (e para a dois tiles)",
				   Vec2.Distance(copia.Pos, tien.Pos) < distAntes - 32,
				   $"{distAntes:0} -> {Vec2.Distance(copia.Pos, tien.Pos):0} px; plano {copia.Cerebro?.Atual} ({copia.Cerebro?.Porque}), moving {copia.Moving}");

		// PARAR: nao anda mais.
		ApertarVerboG12(tien, "splitform_parar", "para");
		tien.Pos = chao + new Vec2(12 * 32, 0);
		Vec2 parada = copia.Pos;
		TiquesG12(TiquesDeG12(1.5), corpos: true);
		AfirmarG12("'Stop': a copia fica onde esta", Vec2.Distance(copia.Pos, parada) < 4, $"{parada} -> {copia.Pos}");

		// ATACAR O MAIS PERTO: um inimigo a dois tiles da copia, e o dono longe -- ela vai no inimigo, nao no dono.
		ServerPlayer inimigo = ForjarG12("Inimigo", copia.Pos + new Vec2(3 * 32, 0), bp: 5_000);
		ApertarVerboG12(tien, "splitform_perto", "mais perto");
		float distIni = Vec2.Distance(copia.Pos, inimigo.Pos);
		TiquesG12(TiquesDeG12(3.0), corpos: true);
		AfirmarG12("'Attack Nearest': a copia vai pra cima do inimigo (e nao do dono)",
				   Vec2.Distance(copia.Pos, inimigo.Pos) < distIni - 16 || inimigo.UltimoAgressor == copia.Id || inimigo.Combate.EmCombate > 0,
				   $"{distIni:0} -> {Vec2.Distance(copia.Pos, inimigo.Pos):0} px, agressor {inimigo.UltimoAgressor}");

		// ATACAR O ALVO: o alvo marcado do dono.
		ServerPlayer marcado = ForjarG12("Marcado", copia.Pos + new Vec2(0, -3 * 32), bp: 5_000);
		tien.AlvoId = marcado.Id;
		ApertarVerboG12(tien, "splitform_alvo", "alvo");
		bool guiou = GuiarSplitformG12(copia, _splitformsG12[copia.Id], out ServerPlayer? presa, out _);
		AfirmarG12("'Attack Target': a presa da copia e o alvo marcado do dono", guiou && presa == marcado);

		// DESFAZER: some, e o dono volta a contar zero.
		ApertarVerboG12(tien, "splitform_destruir", "reabsorve");
		AfirmarG12("'Destroy': a copia sai do mundo e o dono volta a contar zero copias -- e o corte de poder (`splitformdeBuff`) some",
				   !_players.ContainsKey(copia.Id) && f.splitformCount == 0 && Math.Abs(f.splitformdeBuff - 1) < 1e-9,
				   $"count {f.splitformCount}, debuff {f.splitformdeBuff:0.###}");

		// O PRAZO: 100 s.
		f.Ki = f.MaxKi;
		ApertarG12(tien, "SplitForm");
		ServerPlayer? segunda = CopiaDeG12(tien);
		AfirmarG12("uma nova divisao e possivel depois de desfazer a primeira", segunda != null);
		if (segunda != null)
		{
			TiquesG12(TiquesDeG12(99.0));
			bool viva = _players.ContainsKey(segunda.Id);
			TiquesG12(TiquesDeG12(2.0));
			AfirmarG12("a copia vive 100 s (`timelimit = 1000`): esta la aos 99 e sumiu aos 101", viva && !_players.ContainsKey(segunda.Id));
		}

		// A COPIA QUE CAI SOME.
		f.Ki = f.MaxKi;
		ApertarG12(tien, "SplitForm");
		ServerPlayer? terceira = CopiaDeG12(tien);
		if (terceira != null)
		{
			terceira.Ficha.HP = 5;
			DerrubarG12(terceira);
			TiquesG12(TiquesDeG12(1.1));
			AfirmarG12("a copia nocauteada com 8 de vida ou menos e desfeita (`HP<=8 && KO`)", !_players.ContainsKey(terceira.Id));
		}
		LimparTudoG12();
	}

	/// <summary>A copia viva de um dono (a primeira, se houver mais de uma).</summary>
	private ServerPlayer? CopiaDeG12(ServerPlayer dono)
	{
		foreach ((int id, SplitformG12 sf) in _splitformsG12)
			if (sf.Master == dono.Id && _players.TryGetValue(id, out ServerPlayer? c)) return c;
		return null;
	}

	// =====================================================================
	// 10) SENZU
	// =====================================================================
	private void OSenzuG12()
	{
		GD.Print("[g12] -- 10) SENZU: cresce em 60 s e nao antes; comer cura; acudir levanta");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer korin = ForjarG12("Korin", chao, bp: 5_000, skills: [PathGrowSenzuG12]);
		ApertarG12(korin, "Grow_Senzu_Bean");
		AfirmarG12("o aperto comeca a cultivar", _senzuG12.ContainsKey(korin.Id));
		AfirmarG12("...e apertar de novo enquanto cresce e recusado ('espere')", Disse(ApertarG12(korin, "Grow_Senzu_Bean"), "espere"));
		TiquesG12(TiquesDeG12(59.0));
		AfirmarG12("aos 59 s a mochila ainda NAO tem a semente", korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 0);
		TiquesG12(TiquesDeG12(1.1));
		AfirmarG12("aos 60 s a Semente Senzu APARECE na mochila", korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 1 && !_senzuG12.ContainsKey(korin.Id));

		// COMER: um membro ferido volta a 100 e a nutricao sobe 10.
		BodyPart membro = korin.Combate.Corpo.Partes.First(x => x.Papel == Vitalidade.Membro);
		membro.Vida = 40;
		korin.Combate.SincronizarVida();
		korin.Ficha.CurrentNutrition = 20;
		double comida = korin.Ficha.CurrentNutrition;
		ComandoDeItem(korin, "item_comer", CatalogoDeItens.Senzu);
		AfirmarG12("comer a semente cura o membro ferido ate o cheio e alimenta 10",
				   Math.Abs(membro.Vida - membro.VidaMax) < 1e-6 && korin.Ficha.CurrentNutrition >= comida + 10 - 1e-6 && korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 0,
				   $"vida {membro.Vida:0}, comida {comida:0} -> {korin.Ficha.CurrentNutrition:0}");
		korin.Mochila.Guardar(CatalogoDeItens.Senzu);
		EscutaDeAvisos = [];
		ComandoDeItem(korin, "item_comer", CatalogoDeItens.Senzu);
		bool recusou = Disse(EscutaDeAvisos, "digere");
		EscutaDeAvisos = null;
		AfirmarG12("...e uma segunda semente logo depois e recusada (`Senzu + 4 <= 4`): o corpo ainda digere",
				   recusou && korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 1);

		// ACUDIR: o caido levanta; quem DA e curado (o defeito do DM, mantido).
		ServerPlayer caido = ForjarG12("CaidoS", chao + new Vec2(28, 0), bp: 5_000);
		DerrubarG12(caido);
		BodyPart meu = korin.Combate.Corpo.Partes.First(x => x.Papel == Vitalidade.Membro);
		meu.Vida = 40;
		BodyPart dele = caido.Combate.Corpo.Partes.First(x => x.Papel == Vitalidade.Membro);
		dele.Vida = 40;
		ComandoDeItem(korin, "item_acudir", CatalogoDeItens.Senzu);
		AfirmarG12("acudir um caido o LEVANTA e consome a semente", !caido.Ficha.KO && korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 0);
		AfirmarG12("...e cura QUEM DA (`sensuuse(usr)`), nao quem recebe -- o defeito do DM, mantido",
				   Math.Abs(meu.Vida - meu.VidaMax) < 1e-6 && dele.Vida < dele.VidaMax, $"meu {meu.Vida:0}, dele {dele.Vida:0}");
		korin.Mochila.Guardar(CatalogoDeItens.Senzu);
		EscutaDeAvisos = [];
		ComandoDeItem(korin, "item_acudir", CatalogoDeItens.Senzu);
		bool semCaido = Disse(EscutaDeAvisos, "DESACORDADO");
		EscutaDeAvisos = null;
		AfirmarG12("...e sem caido ao lado, acudir recusa e nao gasta a semente", semCaido && korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 1);
		LimparTudoG12();
	}

	// =====================================================================
	// 11) ALVOS DE KI
	// =====================================================================
	private void OsAlvosDeKiG12()
	{
		GD.Print("[g12] -- 11) ALVOS DE KI: nascem meditando, vagam, somem, e o soco acerta");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer pl = ForjarG12("Mirador", PracaLivre(), bp: 5_000, degraus: [(PathKiUnlockedG12, 50)]);
		chao = pl.Pos;
		ApertarG12(pl, "Ki_Targets");
		AfirmarG12("o aperto poe o corpo em MEDITACAO e liga os alvos", pl.Ficha.med && _alvosDeKiG12.ContainsKey(pl.Id));
		TiquesG12(1);
		List<Projetil> alvos = TirosDeG12(pl);
		AfirmarG12("o primeiro alvo nasce no primeiro tique: um projetil INERTE do dono, sem dano, ate 4 tiles",
				   alvos.Count == 1 && alvos[0].Inerte && alvos[0].BaseDano == 0 && Math.Abs(alvos[0].Pos.X - chao.X) <= 4 * 32 + 1 && Math.Abs(alvos[0].Pos.Y - chao.Y) <= 4 * 32 + 1,
				   $"{alvos.Count} alvos");
		Projetil alvo = alvos[0];
		Vec2 nasceu = alvo.Pos;
		TiquesG12(TiquesDeG12(2.0));
		AfirmarG12("...e VAGA (um tile sorteado a cada 0,5 s)", Vec2.Distance(alvo.Pos, nasceu) > 1 || !alvo.Vivo, $"{nasceu} -> {alvo.Pos}");
		TiquesG12(TiquesDeG12(1.6));
		AfirmarG12("aos 3,5 s nasce o segundo", TirosDeG12(pl).Count >= 2, $"{TirosDeG12(pl).Count}");
		TiquesG12(TiquesDeG12(1.5));
		AfirmarG12("...e o primeiro se apagou aos 5 s", !alvo.Vivo && alvo.Fim == FimDeProjetil.Apagou);

		// O SOCO ACERTA: o alvo na frente do corpo, um soco de producao, e o alvo morre com ganho de treino.
		Projetil vivo = TirosDeG12(pl).First();
		vivo.Pos = chao + new Vec2(32, 0);
		pl.Facing = Facing.East;
		int acertosAntes = _acertosDeAlvoDeKiG12;
		double bpAntes = pl.Ficha.BP;
		Atacar(pl, Protocol.Golpe.Leve);
		TiquesG12(1);
		AfirmarG12("um SOCO no alvo a frente o acerta (`attack_proc.dm:62`): ele some com `Acertou` e o treino rende",
				   !vivo.Vivo && vivo.Fim == FimDeProjetil.Acertou && _acertosDeAlvoDeKiG12 == acertosAntes + 1 && pl.Ficha.BP >= bpAntes,
				   $"vivo {vivo.Vivo} fim {vivo.Fim} acertos {_acertosDeAlvoDeKiG12 - acertosAntes}");
		AfirmarG12("...e o soco NAO e recusado por meditar (o `PodeAtacar` nao pergunta isso)", pl.Ficha.med);

		// PARAR DE MEDITAR ENCERRA.
		pl.Ficha.med = false;
		TiquesG12(1);
		AfirmarG12("parar de meditar encerra os alvos e apaga os que restavam", !_alvosDeKiG12.ContainsKey(pl.Id) && TirosDeG12(pl).Count == 0);
		LimparTudoG12();
	}

	// =====================================================================
	// 12) PRECOGNICAO
	// =====================================================================
	private void APrecognicaoG12()
	{
		GD.Print("[g12] -- 12) PRECOGNICAO: um passo perpendicular ao tiro inimigo");

		Vec2 chao = CorredorLivre(24);
		ServerPlayer vidente = ForjarG12("Vidente", chao, bp: 5_000, skills: [PathDaPrecognicaoG12], degraus: [(PathKiUnlockedG12, 50)]);
		ServerPlayer atirador = ForjarG12("Atirador", chao - new Vec2(4 * 32, 0), bp: 5_000, degraus: [(PathKiUnlockedG12, 50)]);
		atirador.Facing = Facing.East;
		TiquesG12(TiquesDeG12(1.1));   // a lista de quem tem a skill e refeita a cada 1 s: o corpo acabou de nascer
		int esquivasAntes = _esquivasDePrecognicaoG12;
		ApertarG12(atirador, "Basic_Blast");
		AfirmarG12("o tiro inimigo saiu", TirosDeG12(atirador).Count == 1);
		TiquesG12(TiquesDeG12(1.5));
		AfirmarG12("quem tem a skill da um passo pro lado (norte: `turn(EAST, 90)`) e vira pra la, sem apertar nada",
				   _esquivasDePrecognicaoG12 >= esquivasAntes + 1 && vidente.Pos.Y <= chao.Y - 32 + 0.5 && Math.Abs(vidente.Pos.X - chao.X) < 0.5
				   && vidente.Facing == Facing.North,
				   $"esquivas +{_esquivasDePrecognicaoG12 - esquivasAntes}, pos {chao} -> {vidente.Pos}, olhar {vidente.Facing}");

		// QUEM NAO TEM A SKILL NAO SE MEXE.
		Vec2 chao2 = CorredorLivre(24);
		ServerPlayer cego = ForjarG12("Cego", chao2, bp: 5_000);
		ServerPlayer atirador2 = ForjarG12("Atirador2", chao2 - new Vec2(4 * 32, 0), bp: 5_000, degraus: [(PathKiUnlockedG12, 50)]);
		atirador2.Facing = Facing.East;
		ApertarG12(atirador2, "Basic_Blast");
		int esquivas = _esquivasDePrecognicaoG12;
		TiquesG12(TiquesDeG12(1.5));
		AfirmarG12("quem NAO tem a skill fica onde esta", Vec2.Distance(cego.Pos, chao2) < 0.5 && _esquivasDePrecognicaoG12 == esquivas);

		// O PROPRIO TIRO NAO ASSUSTA.
		Vec2 chao3 = CorredorLivre(24);
		ServerPlayer proprio = ForjarG12("Proprio", chao3, bp: 5_000, skills: [PathDaPrecognicaoG12], degraus: [(PathKiUnlockedG12, 50)]);
		proprio.Facing = Facing.East;
		TiquesG12(TiquesDeG12(1.1));
		ApertarG12(proprio, "Basic_Blast");
		esquivas = _esquivasDePrecognicaoG12;
		TiquesG12(TiquesDeG12(1.0));
		AfirmarG12("o PROPRIO tiro nao dispara a esquiva (`A.proprietor != savant`)", Vec2.Distance(proprio.Pos, chao3) < 0.5 && _esquivasDePrecognicaoG12 == esquivas);
		LimparTudoG12();
	}

	// =====================================================================
	// 13) O RELOG DERRUBA TUDO
	// =====================================================================
	private void ORelogDerrubaTudoG12()
	{
		GD.Print("[g12] -- 13) O RELOG DERRUBA TUDO");

		ServerPlayer pl = ForjarG12("Relogado", CorredorLivre(24), bp: 50_000, skills: [PathBusterG12, PathGrowSenzuG12, PathSplitformG12]);
		ApertarG12(pl, "BusterBarrage");
		ApertarG12(pl, "Grow_Senzu_Bean");
		ApertarG12(pl, "SplitForm");
		bool tudoLigado = _busterG12.ContainsKey(pl.Id) && _senzuG12.ContainsKey(pl.Id) && _splitformsG12.Values.Any(s => s.Master == pl.Id);
		EsquecerG12(pl.Id);
		AfirmarG12("com barragem, semente e copia de pe, o relog (`EsquecerG12`) apaga os tres",
				   tudoLigado && !_busterG12.ContainsKey(pl.Id) && !_senzuG12.ContainsKey(pl.Id) && !_splitformsG12.Values.Any(s => s.Master == pl.Id));
		AfirmarG12("...e a copia saiu do mundo junto com o dono", !_players.Values.Any(p => p.Name == pl.Name + " Copy"));
		LimparTudoG12();
	}
}
