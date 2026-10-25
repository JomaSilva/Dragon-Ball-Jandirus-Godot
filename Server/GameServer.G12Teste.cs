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
/// o registro de tecnicas vivas do lote G12, o caminho do pacote do jogador, com o `SabeTecnica` no meio; os
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
///     CRESCENTE (10, 20, 30, 40 -- o DM dava 40/20/30/40), depois SAI e obedece ao olhar; prende o
///     corpo enquanto dura; o segundo aperto larga a guia e a bola para;
///  3. BUSTER BARRAGE: liga, cospe duas esferas por ciclo em rumos sortidos, cobra um dreno-base por
///     ciclo, e DESLIGA no aperto, sem Ki, no nocaute e ao trocar de planeta;
///  4. AS DUAS RAJADAS: dez (e vinte) esferas por segundo, plantado, custo que sobe dez vezes, DESLIGA
///     quando a energia nao paga a proxima e ao apertar de novo; a Giratoria nasce em volta e sai nos
///     OITO rumos (o DM soltava tudo pra frente: o contra-exemplo e 1 rumo);
///  5. GENKIDAMA: 90% do Ki, 3 s de forma, pulsos de +0,1; SEM doador a escala e X, COM um doador
///     X + 0,1; o disparo leva o poder ACUMULADO -- sem doador X, com doador X mais o poder INTEIRO dele
///     (o DM descartava tudo em `:170`). E as regras do dono (v0.3.2): uma hora de recarga depois de
///     LANCADA; o convite pra todo mundo do planeta; o POVO doa com reputacao amigavel; so na FORMA BASE;
///     e, lancada, ela se SEGURA nas maos -- a disputa le o poder de agora do dono (transformar-se vira o
///     cabo de guerra) e o acerto, o acumulado;
///  6. SOUL ABSORB: a alma sai, o poder entra, a vitima MORRE um segundo depois, e a alma so sai uma vez;
///  7. DRENO DE ENERGIA: o androide caido e absorvido; o humano caido e drenado 10% a cada 0,7 s, e o
///     dreno para no golpe, no aperto e mata quem fica sem um decimo;
///  8. IMITACAO: nome, raca, genero e aparencia copiados campo a campo; volta ao proprio no segundo aperto;
///  9. DIVISAO DO CORPO: a copia EXISTE, tem cerebro, metade do poder, obedece a seguir / atacar /
///     desfazer, some no prazo, e o dono perde poder por copia viva;
/// 10. SENZU: a semente brota NO CHAO, um passo ao sul, aos 60 s E NAO ANTES; comer cura; acudir levanta E cura o
///     caido, nao quem da (o DM curava quem da);
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

	/// <summary>
	/// Um corpo com as skills pedidas, Ki cheio. `degraus` sao (path, nivel).
	///
	/// UM TANQUE DE VERDADE: o `Forjar` nasce com `baseKi = 100` (MaxKi 100), e a Death Ball pede
	/// `150*BaseDrain` -- 173 num tanque desses. Cinco mil e o Ki de quem ja treinou um pouco.
	///
	/// ============ ELA JA FOI DUAS LINHAS A MAIS, E ERAM UM REMENDO ============
	/// Ate 2026-09-02 esta forja repetia `Ki = MaxKi; Tick()` depois do <see cref="ForjarComSkills"/>,
	/// porque o comum enchia o tanque DEPOIS da conta de poder e o `expressedBP` deste lote nascia em
	/// jejum -- 1,667x baixo demais. Era o remendo de quem precisava da ordem certa. O comum passou a
	/// forjar na ordem de producao e o remendo saiu: esta forja e hoje so a CONFIGURACAO (o `baseKi`)
	/// que e dela.
	///
	/// O QUE ESTE PARAGRAFO DIZIA ERRADO ate hoje: que sem o remendo "o corte do `splitformdeBuff` nao
	/// aparecia". Nao era o corte -- ele nao existia em ramo nenhum (ver a divergencia declarada em
	/// `Fighter.Power.cs`). O que o jejum escondia era o `kiratio`, que e o que a prova media de fato.
	/// A frase estava creditando a um mecanismo morto o efeito de outro, exatamente como a prova.
	/// ==========================================================================
	/// </summary>
	private ServerPlayer ForjarG12(string nome, Vec2 onde, double bp, string[]? skills = null,
								  (string Path, int Nivel)[]? degraus = null)
		=> ForjarComSkills(nome, onde, bp, skills, degraus, baseKi: 5_000);

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
		foreach (string id in IdsDoLote("G12"))
			if (Tecnicas.Get(id) is not { Modo: not Modo.NaoPortada }) naoPortadas.Add(id);
		AfirmarG12("as onze estao registradas como PORTADAS", naoPortadas.Count == 0, string.Join(" | ", naoPortadas));

		ServerPlayer nu = ForjarG12("Nu", CorredorLivre(24), bp: 5_000);
		var passou = new List<string>();
		foreach (string id in IdsDoLote("G12"))
			if (!Disse(ApertarEOuvir(nu, id), "voce nao sabe")) passou.Add(id);
		AfirmarG12("quem nao comprou nada ouve 'voce nao sabe' nos onze", passou.Count == 0, string.Join(" | ", passou));

		ServerPlayer sabe = ForjarG12("Sabe", CorredorLivre(24), bp: 5_000,
			skills: [PathDeathBallG12, PathBusterG12, PathSpiritBombG12, PathSoulAbsorbG12, PathAndroidAbsorbG12,
					 PathImitationG12, PathSplitformG12, PathGrowSenzuG12],
			degraus: [(PathVolleyG12, 50), (PathKiUnlockedG12, 50)]);
		var faltou = new List<string>();
		foreach (string id in IdsDoLote("G12")) if (!SabeTecnica(sabe, id)) faltou.Add(id);
		AfirmarG12("com as skills e os degraus, os onze sao reconhecidos (8 por skill, 3 por degrau)",
				   faltou.Count == 0, string.Join(" | ", faltou));

		ServerPlayer meio = ForjarG12("MeioVolei", CorredorLivre(24), bp: 5_000, degraus: [(PathVolleyG12, 20)]);
		AfirmarG12("...e o nivel 20 do Volley da as Balas Continuas mas NAO a Giratoria (que e do 50)",
				   SabeTecnica(meio, "Continuous_Energy_Bullets") && !SabeTecnica(meio, "Spin_Blast"));

		List<string> menu = TecnicasDe(sabe);
		AfirmarG12("...e o menu do cliente enxerga os onze", IdsDoLote("G12").All(menu.Contains));
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

		List<string> falou = ApertarEOuvir(pl, "Death_Ball");
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

		// OS QUATRO ESTAGIOS: 1,5 s cada, escala 1,25 / 1,5 / 1,75 / 2,0 e dano 10 / 20 / 30 / 40 (o DM dava 40 ao
		// nascer, DeathBall.dm:62, e o estagio 1 valia mais que o 2 -- consertado por decisao do dono).
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
		AfirmarG12("...e o dano CRESCE com a carga: 10, 20, 30, 40 (`10*movestrength` em todo estagio; o DM dava 40/20/30/40 -- o estagio 1 valia MAIS que o 2, DeathBall.dm:62/:80)",
				   danos.Count == 4 && danos.Zip(new[] { 10.0, 20.0, 30.0, 40.0 }).All(z => Math.Abs(z.First - z.Second) < 1e-6),
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
		ApertarEOuvir(pl, "Death_Ball");
		AfirmarG12("o segundo aperto larga a guia: a bola para onde esta e ganha o prazo de 5 s do `Burnout()`",
				   !_deathBallG12.ContainsKey(pl.Id) && solta.Rumo.LengthSquared < 1e-6f && solta.VidaRestante <= 5.0 + 1e-6,
				   $"rumo {solta.Rumo} vida {solta.VidaRestante:0.##}");
		AfirmarG12("...e o corpo se solta", PodeMexerOCorpo(pl));
		TiquesG12(TiquesDeG12(5.1));
		// (2026-10-13) A ESFERA GRANDE QUE ACABA ESTOURA, onde quer que seja -- e o `Burnout()` do DM tambem termina em
		// `explode()` (`objects.dm:652-661`). Ate a v0.2.0 ela so se apagava.
		AfirmarG12("...e cinco segundos depois ela ESTOURA onde parou (toda esfera grande acaba num estouro)",
				   !solta.Vivo && solta.Fim == FimDeProjetil.Explodiu, $"vivo {solta.Vivo} fim {solta.Fim}");

		// O TERCEIRO APERTO DURANTE A CARGA: larga a guia (2o) e ENCURTA a carga (3o) -- sai no estagio de agora, parada.
		ServerPlayer curto = ForjarG12("Curto", CorredorLivre(24), bp: 50_000, skills: [PathDeathBallG12]);
		ApertarEOuvir(curto, "Death_Ball");
		TiquesG12(TiquesDeG12(1.6));
		ApertarEOuvir(curto, "Death_Ball");   // larga a guia
		ApertarEOuvir(curto, "Death_Ball");   // interrompe a carga
		TiquesG12(2);
		bool saiuParada = !_deathBallG12.ContainsKey(curto.Id) && TirosDeG12(curto).Count == 1
						  && TirosDeG12(curto)[0] is { Inerte: false } b2 && b2.Rumo.LengthSquared < 1e-6f && b2.EscalaVisual == 1.5;
		AfirmarG12("apertar 2x durante a carga larga a guia e interrompe a carga: a bola do estagio 2 sai e fica parada",
				   saiuParada, $"tiros {TirosDeG12(curto).Count}");

		// O NOCAUTE DERRUBA A CARGA.
		ServerPlayer caido = ForjarG12("Caido", CorredorLivre(24), bp: 50_000, skills: [PathDeathBallG12]);
		ApertarEOuvir(caido, "Death_Ball");
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
		// DE CIMA, ONDE O CENARIO NAO ALCANCA -- a mesma razao da Giratoria (ver `AsDuasRajadasG12`): as esferas saem em
		// rumos sorteados de uma praca de 3x3 tiles, e a 18 ou 20 tiles por segundo a que acabou de nascer ja saiu
		// da praca dois tiques depois. Se sobrava alguma viva pra medir era sorte do mapa em volta.
		pl.Altitude = Voo.AlturaQueAtravessa;
		Fighter f = pl.Ficha;
		double kiAntes = f.Ki;
		ApertarEOuvir(pl, "BusterBarrage");
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
		// O `lag` e o do DM (`rand(1,2)` tiques por tile); a velocidade e a de toda bola com esse lag (`AtrasoDeBolaPorLag`).
		AfirmarG12("...e as vivas VOAM (rumo nao nulo, na velocidade de uma bola de lag 1 ou de lag 2)",
				   tiros.Count >= 1 && tiros.All(t => t.Rumo.LengthSquared > 0.9f
					   && (Math.Abs(t.SegundosPorTile - Projetil.AtrasoDeBolaPorLag(1)) < 1e-9 || Math.Abs(t.SegundosPorTile - Projetil.AtrasoDeBolaPorLag(2)) < 1e-9)),
				   $"{tiros.Count} vivas");
		AfirmarG12("...em rumos DIFERENTES (o `rand(1,8)`)", estado.RumosVistos.Count >= 2, $"{estado.RumosVistos.Count} rumos");
		AfirmarG12("...com o dano do `Create_Blast`: (0,5 + Ekioff) x Ephysoff",
				   tiros.Count >= 1 && tiros.All(t => Math.Abs(t.BaseDano - (0.5 + f.Ekioff) * f.Ephysoff) < 1e-6));

		ApertarEOuvir(pl, "BusterBarrage");
		AfirmarG12("o segundo aperto DESLIGA", !_busterG12.ContainsKey(pl.Id));
		int vivos = TirosDeG12(pl).Count;
		TiquesG12(TiquesDeG12(2.0));
		AfirmarG12("...e desligada ela nao cospe mais nada", TirosDeG12(pl).Count <= vivos);

		// SEM KI: desliga sozinha.
		ServerPlayer seco = ForjarG12("Seco", CorredorLivre(24), bp: 50_000, skills: [PathBusterG12]);
		ApertarEOuvir(seco, "BusterBarrage");
		TiquesG12(1);
		seco.Ficha.Ki = 0.5;
		TiquesG12(TiquesDeG12(2.0));
		AfirmarG12("com Ki abaixo de 1 a barragem DESLIGA sozinha (`if(usr.Ki>=1 ...) else blasting=0`)", !_busterG12.ContainsKey(seco.Id));

		// NOCAUTE: desliga.
		ServerPlayer caido = ForjarG12("CaidoB", CorredorLivre(24), bp: 50_000, skills: [PathBusterG12]);
		ApertarEOuvir(caido, "BusterBarrage");
		DerrubarG12(caido);
		TiquesG12(TiquesDeG12(2.0));
		AfirmarG12("o nocaute DESLIGA a barragem", !_busterG12.ContainsKey(caido.Id));

		// TROCAR DE PLANETA: desliga.
		ServerPlayer viajante = ForjarG12("Viajante", CorredorLivre(24), bp: 50_000, skills: [PathBusterG12]);
		ApertarEOuvir(viajante, "BusterBarrage");
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

		ApertarEOuvir(pl, "Continuous_Energy_Bullets");
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
		AfirmarG12("...todas pra FRENTE (leste), na velocidade da bola mais rapida (o `walk` sem lag do DM)",
				   tiros.Count >= 5 && tiros.All(t => t.Rumo.X > 0.99 && Math.Abs(t.SegundosPorTile - Projetil.AtrasoDeBolaPorLag(1)) < 1e-9), $"{tiros.Count} vivas");
		AfirmarG12("...nascidas num LEQUE de +-45 graus (o Y das vivas varia entre tres fileiras)",
				   tiros.Select(t => Math.Round(t.Pos.Y)).Distinct().Count() >= 2 && tiros.All(t => Math.Abs(t.Pos.Y - pl.Pos.Y) <= 32.5));
		AfirmarG12("...com o dano do DM: 0,7 x Ekioff x log10(max(blastskill,10)) x dano global x os dois logs do `mods`",
				   tiros.All(t => Math.Abs(t.BaseDano - 0.7 * f.Ekioff * DanoDeKi.Log10Min(f.blastskill, 10) * DanoDeKi.DanoGlobalDeKi * DanoDeKi.Log10Min(f.kieffusionskill, 2) * DanoDeKi.Log10Min(f.blastskill, 2)) < 1e-6));

		// ============================ A PISCADA LEVA A RAJADA JUNTO (`click.dm:54` nao le `canmove`) ============================
		// O `canmove = 0` do verb (`blasts.dm:270`) e a trava do PASSO (`movement handler.dm:130`) -- e o `turf/DblClick`
		// nao o le: no DM o corpo pisca e a esfera seguinte nasce do `locate(usr.x,usr.y,usr.z)` NOVO (`:300`, dentro do
		// `while`). Aqui havia uma ancora que puxava o corpo de volta no tique seguinte, com o Ki e o salto ja pagos.
		// A PISCADA E A DE PRODUCAO (o `Zanzoken` do `C2S.Zanzoken`), tres tiles pro LESTE -- ao longo do corredor livre --,
		// com a Afterimage no livro e a pericia/velocidade treinadas pra o `zanzorange` cobrir os tres tiles.
		// COMO ELA REPROVA: volte o `Ancorar(pl, v.Ancora)` no `TickDoVoleiG12` e a primeira diz "voltou pra ...".
		// ======================================================================================================================
		pl.Livro.Dar(PathDoZanzoken);
		pl.Niveis.Por(PathDoZanzoken, 1);
		f.kiskill = 3;
		f.speed = 3;
		f.Statify();
		Vec2 antesDaPiscada = pl.Pos;
		Vec2 ondePisca = pl.Pos + new Vec2(3 * ZoneCollision.TileSize, 0);
		var jaVoavam = new HashSet<Projetil>(TirosDeG12(pl));
		Zanzoken(pl, ondePisca);
		bool piscou = Vec2.Distance(pl.Pos, ondePisca) < 0.5f;
		TiquesG12(TiquesDeG12(0.5));
		AfirmarG12("com a rajada LIGADA a piscada sai, e o corpo FICA no ponto novo -- a rajada nao ancora o corpo",
				   piscou && _voleiG12.ContainsKey(pl.Id) && Vec2.Distance(pl.Pos, ondePisca) < 0.5f,
				   $"piscou={piscou}, rajada {(_voleiG12.ContainsKey(pl.Id) ? "ligada" : "DESLIGADA")}, "
				   + $"corpo em ({pl.Pos.X:0},{pl.Pos.Y:0}), piscou pra ({ondePisca.X:0},{ondePisca.Y:0}), saiu de ({antesDaPiscada.X:0},{antesDaPiscada.Y:0})");
		List<Projetil> novas = TirosDeG12(pl).FindAll(t => !jaVoavam.Contains(t));
		// ONDE A MAIS NOVA NASCEU, e nao onde ela esta: um tile a frente e um no leque (`step` + `step`) do corpo. A
		// regua era a posicao de agora com tres tiles de folga -- e isso so separava o ponto novo do velho enquanto a
		// esfera andava um tile a cada tres tiques: a 20 tiles por segundo (dono, 2026-10-08) uma esfera nascida do
		// ponto VELHO ja estaria a frente do novo em tres tiques, e a prova passaria com o defeito de volta. O
		// nascimento sai da propria esfera: onde ela esta menos o que ela andou (`AndouTiles`).
		Projetil? maisNova = novas.Count == 0 ? null : novas.MinBy(t => t.AndouTiles);
		float nasceuEmX = maisNova == null ? 0 : maisNova.Pos.X - maisNova.Rumo.X * (float)(maisNova.AndouTiles * ZoneCollision.TileSize);
		AfirmarG12("...e a esfera seguinte nasce do ponto NOVO (o `locate(usr.x,usr.y,usr.z)` de dentro do `while`, `:300`)",
				   maisNova != null && nasceuEmX > ondePisca.X && nasceuEmX - ondePisca.X <= 2 * ZoneCollision.TileSize + 0.5f
				   && Math.Abs(maisNova.Pos.Y - ondePisca.Y) <= 32.5f,
				   maisNova == null ? "nenhuma esfera nova"
				   : $"a mais nova nasceu em x={nasceuEmX:0} (esta em ({maisNova.Pos.X:0},{maisNova.Pos.Y:0})), o corpo em ({ondePisca.X:0},{ondePisca.Y:0})");

		// APERTAR DE NOVO DESLIGA, e abre a espera de 5 s no `_volleyPronto` (o `barrageCD`).
		ApertarEOuvir(pl, "Continuous_Energy_Bullets");
		TiquesG12(1);
		AfirmarG12("o segundo aperto DESLIGA a rajada e solta o corpo", !_voleiG12.ContainsKey(pl.Id) && PodeMexerOCorpo(pl));
		AfirmarG12("...e deixa a familia de barragem em espera por 5 s (`reload = 50`)",
				   _volleyPronto.TryGetValue(pl.Id, out long pronto) && pronto - NowMs() > 4_000 && pronto - NowMs() <= 5_000, $"{pronto - NowMs()} ms");

		// SEM KI: desliga sozinha.
		ServerPlayer seco = ForjarG12("SecoV", CorredorLivre(24), bp: 50_000, degraus: [(PathVolleyG12, 20)]);
		ApertarEOuvir(seco, "Continuous_Energy_Bullets");
		TiquesG12(1);
		bool ligada = _voleiG12.ContainsKey(seco.Id);
		seco.Ficha.Ki = 1;   // abaixo do proximo `kireq`
		TiquesG12(2);
		AfirmarG12("quando a energia nao paga a proxima esfera a rajada DESLIGA sozinha", ligada && !_voleiG12.ContainsKey(seco.Id));

		// A GIRATORIA: duas por tique, nasce em volta, e sai nos OITO rumos (o DM soltava tudo pra frente: 1 rumo).
		ServerPlayer giro = ForjarG12("Giro", PracaLivre(), bp: 50_000, degraus: [(PathVolleyG12, 50)]);
		giro.Facing = Facing.East;
		// DE CIMA, ONDE O CENARIO NAO ALCANCA (`Voo.AtravessaCenario`). A praca so garante 3x3 tiles livres e as
		// esferas saem pros oito lados: a 20 tiles por segundo (dono, 2026-10-08) elas cruzam a praca em dois tiques
		// e batiam no que houvesse em volta -- quantas chegavam vivas ao fim do segundo era sorte do sorteio de rumos
		// (4 ou 5, com a regua em 5). O que esta cena mede e a tecnica, e nao o mapa em volta da praca.
		giro.Altitude = Voo.AlturaQueAtravessa;
		Fighter g = giro.Ficha;
		g.Ki = g.MaxKi = 5_000_000_000;
		double kiG = g.Ki;
		ApertarEOuvir(giro, "Spin_Blast");
		AfirmarG12("a Giratoria cobra 50x o dreno-base na entrada", Math.Abs(kiG - 50 * g.BaseDrain() - g.Ki) < 1);
		TiquesG12(TiquesDeG12(1.0));
		List<Projetil> giros = TirosDeG12(giro);
		int cuspidas = _voleiG12.TryGetValue(giro.Id, out EstadoDoVoleiG12? vg) ? vg.Duracao : -1;
		AfirmarG12("um segundo de Giratoria sao ~vinte esferas cuspidas (duas por 0,1 s)", cuspidas >= 20 && cuspidas <= 22, $"{cuspidas} cuspidas, {giros.Count} vivas");
		AfirmarG12("...e as vivas voam a partir de um tile ADJACENTE ao corpo (ja andaram; nasceram a ate um tile)",
				   giros.Count >= 5 && giros.Select(t => (Math.Round(t.Pos.Y))).Distinct().Count() >= 2, $"{giros.Count} vivas");
		int rumosDistintos = vg?.RumosVistos.Count ?? 0;
		AfirmarG12("...e saem em TODAS as direcoes: ~20 esferas cobrem pelo menos 5 dos 8 rumos e as vivas NAO voam todas pra leste (o DM usava o `dir` do MOB em `walk(A, dir)`, blasts.dm:384: um rumo so -- consertado por decisao do dono)",
				   rumosDistintos >= 5 && giros.Count > 0 && !giros.All(t => t.Rumo.X > 0.99)
				   && giros.All(t => Math.Abs(t.Rumo.LengthSquared - 1) < 1e-3),
				   $"{rumosDistintos} rumos distintos em {cuspidas} cuspidas; {giros.Count(t => t.Rumo.X > 0.99)} de {giros.Count} vivas pra leste");
		AfirmarG12("...com 1,1x o dano das Balas Continuas",
				   giros.All(t => Math.Abs(t.BaseDano - 1.1 * g.Ekioff * DanoDeKi.Log10Min(g.blastskill, 10) * DanoDeKi.DanoGlobalDeKi * DanoDeKi.Log10Min(g.kieffusionskill, 2) * DanoDeKi.Log10Min(g.blastskill, 2)) < 1e-6));
		ApertarEOuvir(giro, "Spin_Blast");
		TiquesG12(1);
		AfirmarG12("...e a espera dela e de 8 s (`reload = 80`)",
				   _volleyPronto.TryGetValue(giro.Id, out long pg) && pg - NowMs() > 7_000 && pg - NowMs() <= 8_000);

		// COM A RAJADA NO AR, OUTRO TIRO E RECUSADO (o `blasting` do DM, pelo `PodeAtirar`).
		ServerPlayer duplo = ForjarG12("Duplo", CorredorLivre(24), bp: 50_000, degraus: [(PathVolleyG12, 20), (PathKiUnlockedG12, 50)]);
		duplo.Ficha.Ki = duplo.Ficha.MaxKi = 5_000_000_000;
		ApertarEOuvir(duplo, "Continuous_Energy_Bullets");
		AfirmarG12("com a rajada no ar, a Bola de Ki e recusada ('tecnica de ki no ar')",
				   Disse(ApertarEOuvir(duplo, "Basic_Blast"), "tecnica de ki no ar"));
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
		ApertarEOuvir(pl, "SpiritBomb");
		bool ligou = _genkidamaG12.TryGetValue(pl.Id, out EstadoDaGenkidamaG12? g);
		// (v0.3.0) EM CIMA DA CABECA, e nao mais um tile ao norte (dono: "a genkidama enquanto carrega seria bom ficar em cima da
		// cabeca do personagem") -- a regua e a de producao (`BocaDeCano.EmCimaDaCabeca`); a familia GC da `--v030teste` mede o resto.
		AfirmarG12("o aperto cobra 90% do Ki maximo, planta o corpo e pare a esfera INERTE em cima da cabeca",
				   ligou && Math.Abs(f.Ki - (kiAntes - 0.9 * f.MaxKi)) < 0.5 && !PodeMexerOCorpo(pl)
				   && g!.Bola is { Vivo: true, Inerte: true }
				   && (g.Bola.Pos - BocaDeCano.EmCimaDaCabeca(pl.Pos, ArteDeProjetil.RaioDaGenkidama)).Length < 0.5f,
				   $"ki {kiAntes:0} -> {f.Ki:0}, bola em {g?.Bola?.Pos}, corpo em {pl.Pos}");
		if (!ligou) { LimparTudoG12(); return; }
		AfirmarG12("...com dano-base 30 e NAO defletivel", g!.Bola!.BaseDano == 30 && !g.Bola.Deflectivel);
		ServerPlayer cansado = ForjarG12("Cansado", CorredorLivre(24), bp: 50_000, skills: [PathSpiritBombG12]);
		cansado.Ficha.Ki = cansado.Ficha.MaxKi * 0.5;
		AfirmarG12("...e sem Ki o verb recusa por energia (o `PodeAtirar`, com o numero: 90% do maximo)",
				   Disse(ApertarEOuvir(cansado, "SpiritBomb"), "energia") && Math.Abs(cansado.Ficha.Ki - cansado.Ficha.MaxKi * 0.5) < 1e-9);

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

		// O DISPARO: 2 s depois a bola sai com o poder ACUMULADO (o `:170` do DM descartava e saia com o do dono), escala mantida.
		double acumulado = g.BpAcumulado;
		ApertarEOuvir(pl, "SpiritBomb");
		AfirmarG12("o segundo aperto pede o disparo (fase 2) e a bola ainda esta parada", g.Fase == 2 && g.Bola is { Inerte: true });
		TiquesG12(TiquesDeG12(2.0) + 1);
		Projetil? bomba = g.Bola;
		AfirmarG12("2 s depois a Genkidama SAI: nao inerte, pra frente, na velocidade da bola mais rapida, 100 s de prazo, escala mantida",
				   g.Fase == 3 && bomba is { Vivo: true, Inerte: false } && bomba.Rumo.X > 0.99
				   && Math.Abs(bomba.SegundosPorTile - Projetil.AtrasoDeBolaPorLag(1)) < 1e-9
				   && Math.Abs(bomba.VidaRestante - 100) < 0.2 && Math.Abs(bomba.EscalaVisual - semDoador) < 1e-9,
				   bomba == null ? "sem bola" : $"fase {g.Fase} inerte {bomba.Inerte} rumo {bomba.Rumo} vida {bomba.VidaRestante:0.#}");
		double bpSemDoador = bomba?.Bp ?? 0;
		AfirmarG12("...e o BP dela e o ACUMULADO (expressedBP x 1,01 x 1,01 dos dois pulsos), e nao o expressedBP cru do dono (o `A.BP = expressedBP` de SpiritBomb.dm:170 descartava -- consertado por decisao do dono)",
				   bomba != null && Math.Abs(bomba.Bp - acumulado) < 1e-6 && acumulado > f.expressedBP
				   && Math.Abs(bomba.Bp - f.expressedBP * 1.01 * 1.01) < 1e-6 * f.expressedBP,
				   $"bp {bomba?.Bp:0} expresso {f.expressedBP:0} acumulado {acumulado:0}");
		AfirmarG12("...e o corpo continua preso por 3 s depois do disparo", !PodeMexerOCorpo(pl));
		TiquesG12(TiquesDeG12(3.1));
		AfirmarG12("...e se solta aos 3 s", PodeMexerOCorpo(pl) && !_genkidamaG12.ContainsKey(pl.Id));

		// ---- (v0.3.2) UMA HORA DE RECARGA, E SO DEPOIS DE LANCADA (dono, 2026-10-16) ----
		long recarga = pl.Ficha.genkidama_pronta_em - NowMs();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		List<string> deNovo = ApertarEOuvir(pl, "SpiritBomb");
		AfirmarG12("**A GENKIDAMA LANCADA SO VOLTA EM UMA HORA** (*\"coloque um cooldown grande nela de 1 hora q so acontece quando vc lança ela\"*): o lancamento arma a recarga, e o aperto seguinte e recusado dizendo quanto falta",
				   recarga > MsDeRecargaDaGenkidama - 60_000 && recarga <= MsDeRecargaDaGenkidama && !_genkidamaG12.ContainsKey(pl.Id) && Disse(deNovo, "faltam"),
				   $"recarga {recarga / 60_000.0:0.#} min | {Ultimos(deNovo)}");
		GenkidamaSemRecargaDeTeste = true;
		try
		{
			ApertarEOuvir(pl, "SpiritBomb");
			AfirmarG12("(defeito injetado: a Genkidama sem recarga, o jogo de antes) o mesmo aperto forma outra na hora -- a regua de cima REPROVA",
					   _genkidamaG12.ContainsKey(pl.Id));
		}
		finally { GenkidamaSemRecargaDeTeste = false; }
		LimparTudoG12();

		// ---- A FRASE DO DISPARO NAO DIZ PODER EM NUMERO SEM SCOUTER (dono, 2026-10-12) ----
		// "o poder acumulado na espera: N (o seu proprio e M)" saia pra qualquer um. A regra principal do BP: numero so
		// com medidor (`PoderNaFrase`); sem ele a bomba e medida CONTRA o poder de quem a solta. Tres bombas inteiras:
		// a frase so sai no disparo, pelo tique de producao -- e o acumulado ja nao muda depois do segundo aperto.
		ServerPlayer leitor = ForjarG12("GokuLeitor", CorredorLivre(24), bp: 50_000, skills: [PathSpiritBombG12]);
		leitor.Facing = Facing.East;
		var bombas = new List<double>();
		var (semScouter, comScouter, comDefeito) = AsTresLeiturasDoPoder(leitor, () =>
		{
			leitor.Ficha.Ki = leitor.Ficha.MaxKi;
			leitor.Ficha.genkidama_pronta_em = 0;   // (v0.3.2) tres bombas seguidas: a hora de recarga e medida na primeira, acima
			UsarHabilidade(leitor, "SpiritBomb");
			TiquesG12(TiquesDeG12(3.1));   // a esfera se forma
			TiquesG12(TiquesDeG12(4.0));   // dois pulsos de crescimento
			UsarHabilidade(leitor, "SpiritBomb");   // pede o disparo
			if (_genkidamaG12.TryGetValue(leitor.Id, out EstadoDaGenkidamaG12? gl)) bombas.Add(gl.BpAcumulado);
			TiquesG12(TiquesDeG12(2.0) + 1);   // sai
			TiquesG12(TiquesDeG12(3.1));       // o corpo se solta
			LimparTudoDaBancada(manter: [leitor]);   // a bomba que ficou voando nao entra na rodada seguinte
		});
		AfirmarG12("SEM scouter, o disparo da Genkidama mede a bomba CONTRA o poder de quem a solta ('maior que o seu') e nao traz o acumulado em numero",
				   bombas.Count == 3 && Disse(semScouter, "maior que o seu") && !TrazOPoder(semScouter, bombas[0]), Ultimos(semScouter));
		AfirmarG12("COM o scouter ligado, a mesma frase traz o acumulado em numero",
				   bombas.Count == 3 && TrazOPoder(comScouter, bombas[1]), Ultimos(comScouter));
		AfirmarG12("(defeito injetado: o numero sem medidor) sem scouter o numero volta -- a regua de cima reprovaria",
				   bombas.Count == 3 && TrazOPoder(comDefeito, bombas[2]), Ultimos(comDefeito));
		LimparTudoG12();

		// ---- COM UM DOADOR (v0.3.2: meditando ou nao; 2026-10-25: quem o Sense de quem forma alcanca -- aqui, o planeta inteiro) ----
		ServerPlayer dono = ForjarG12("Goku2", CorredorLivre(24), bp: 50_000, skills: [PathSpiritBombG12]);
		dono.Niveis.Por(SenseEmNiveis.Path, SenseEmNiveis.Planeta);
		ServerPlayer doador = ForjarG12("Krillin", CorredorLivre(24), bp: 20_000);
		ServerPlayer longe = ForjarG12("Yamcha", CorredorLivre(24), bp: 20_000);
		MoveToZone(longe.Id, ZoneKey.Premade("Namek"), PontoDeNascimento(ZoneKey.Premade("Namek")));   // em outro planeta
		double kiDoador = doador.Ficha.Ki, bpDoador = doador.Ficha.expressedBP;
		ApertarEOuvir(dono, "SpiritBomb");
		TiquesG12(TiquesDeG12(3.1));
		AfirmarG12("**AO FORMAR, QUEM O SENSE ALCANCA E CONVIDADO** -- com o Sense no planeta, o planeta inteiro -- meditando ou nao (*\"[...] n precisa meditar mais\"*): quem esta de pe, sem meditar, recebe a pergunta; quem esta em outro planeta, nao",
				   !doador.Ficha.med && _ofertasDeGenkidamaG12.ContainsKey(doador.Id) && !_ofertasDeGenkidamaG12.ContainsKey(longe.Id));
		EstadoDaGenkidamaG12 g2 = _genkidamaG12[dono.Id];
		double bpAntes = g2.BpAcumulado;
		AfirmarG12("quem nao foi convidado (o de outro planeta) e responde ouve 'ninguem esta pedindo'",
				   ApertarVerboG12(longe, "genkidama_doar", "ninguem esta pedindo"));
		AfirmarG12("**A DOACAO SOMA O PODER INTEIRO DO DOADOR** (*\"100% seria bom\"*): o doador aceita pelo verb da aba Other, perde 10% do Ki, e a bomba acumula o poder expresso dele -- e nao o `expressedBP x Ki x 0,1` do DM (o Ki absoluto: os 600 milhoes)",
				   ApertarVerboG12(doador, "genkidama_doar", "um decimo") && Math.Abs(doador.Ficha.Ki - kiDoador * 0.9) < 1e-6
				   && Math.Abs(g2.BpAcumulado - bpAntes - bpDoador) < 1e-3,
				   $"ki {kiDoador:0} -> {doador.Ficha.Ki:0}, acumulado {bpAntes:0} -> {g2.BpAcumulado:0} (o poder do doador e {bpDoador:0})");
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

		// O DISPARO COM DOADOR: a bola vale o acumulado -- X (o disparo sem doador) MAIS a doacao, pelo menos.
		double doacao = bpDoador;
		double acumuladoComDoador = g2.BpAcumulado;
		ApertarEOuvir(dono, "SpiritBomb");
		TiquesG12(TiquesDeG12(2.0) + 1);
		Projetil? bombaComDoador = g2.Bola;
		AfirmarG12("COM um doador o disparo leva o acumulado: o BP da bola e exatamente o `BpAcumulado` e passa do disparo sem doador por pelo menos a doacao (o poder do doador)",
				   g2.Fase == 3 && bombaComDoador is { Vivo: true, Inerte: false }
				   && Math.Abs(bombaComDoador.Bp - acumuladoComDoador) < 1e-6
				   && bombaComDoador.Bp >= bpSemDoador + doacao - 1e-6,
				   $"bp {bombaComDoador?.Bp:0} = acumulado {acumuladoComDoador:0}; sem doador {bpSemDoador:0} + doacao {doacao:0}");
		LimparTudoG12();

		// O NOCAUTE DESFAZ (uma bomba nova: a de cima ja saiu da mao).
		ServerPlayer caido = ForjarG12("Goku3", CorredorLivre(24), bp: 50_000, skills: [PathSpiritBombG12]);
		ApertarEOuvir(caido, "SpiritBomb");
		TiquesG12(TiquesDeG12(3.1));
		DerrubarG12(caido);
		TiquesG12(2);
		AfirmarG12("o nocaute desfaz a Genkidama e apaga a bola", !_genkidamaG12.ContainsKey(caido.Id) && TirosDeG12(caido).Count == 0);
		AfirmarG12("...e a que se desfaz ANTES de sair nao arma a recarga de uma hora (*\"so acontece quando vc lança ela\"*)",
				   caido.Ficha.genkidama_pronta_em == 0, $"{caido.Ficha.genkidama_pronta_em}");
		LimparTudoG12();

		// ---- (v0.3.2) OS DOIS JOGOS DE ANTES: a pergunta so pra quem medita, e a doacao pelo Ki absoluto ----
		ServerPlayer dono3 = ForjarG12("Goku4", CorredorLivre(24), bp: 50_000, skills: [PathSpiritBombG12]);
		dono3.Niveis.Por(SenseEmNiveis.Path, SenseEmNiveis.Planeta);   // (2026-10-25) o doador esta ao alcance: so o defeito o tira da lista
		ServerPlayer doador3 = ForjarG12("Krillin4", CorredorLivre(24), bp: 20_000);
		double kiDoador3 = doador3.Ficha.Ki, bpDoador3 = doador3.Ficha.expressedBP;
		GenkidamaSoConvidaQuemMeditaDeTeste = true;
		GenkidamaDoacaoComoODmDeTeste = true;
		try
		{
			ApertarEOuvir(dono3, "SpiritBomb");
			TiquesG12(TiquesDeG12(3.1));
			bool semConvite = !_ofertasDeGenkidamaG12.ContainsKey(doador3.Id);
			AfirmarG12("(defeito injetado: so quem medita, o jogo de antes) quem esta de pe sem meditar nao recebe a pergunta -- a regua do convite REPROVA",
					   semConvite);
			_ofertasDeGenkidamaG12[doador3.Id] = dono3.Id;   // a pergunta que so quem medita receberia
			EstadoDaGenkidamaG12 g3 = _genkidamaG12[dono3.Id];
			double antes3 = g3.BpAcumulado;
			ApertarVerboG12(doador3, "genkidama_doar", "um decimo");
			double soma = g3.BpAcumulado - antes3;
			AfirmarG12("(defeito injetado: a doacao do DM) a bomba soma `expressedBP x Ki x 0,1` (o Ki absoluto), e nao o poder do doador -- a regua da doacao REPROVA",
					   Math.Abs(soma - bpDoador3 * (kiDoador3 * 0.1)) < 1e-3 && Math.Abs(soma - bpDoador3) > 1,
					   $"somou {soma:0} (o poder do doador e {bpDoador3:0}, o Ki dele {kiDoador3:0})");
		}
		finally { GenkidamaSoConvidaQuemMeditaDeTeste = false; GenkidamaDoacaoComoODmDeTeste = false; }
		LimparTudoG12();

		// ---- (2026-10-25) O CONVITE VAI ATE ONDE O SENSE DE QUEM FORMA ALCANCA -- e nao aos mortos do Outro Mundo ----
		// O dono: *"genkidama manda pedido de doacao pra todos aqueles q estao dentro do seu sense (menos os mortos q estao no outro mundo), assim
		// com sense 5, vc pode pedir energia pra todos os jogadores vivos do universo"*. A mesma Genkidama, formada com o Sense em tres niveis, e
		// quatro corpos parados: a 6 tiles (a vista), a 35 (a regiao), em outro planeta, e um MORTO no Outro Mundo.
		{
			const float TGs = ZoneCollision.TileSize;
			Vec2 pontoGs = CorredorLivre(24);
			ServerPlayer formadorGs = ForjarG12("GokuDoSense", pontoGs, bp: 50_000, skills: [PathSpiritBombG12]);
			ServerPlayer pertoGs = ForjarG12("NaVista", pontoGs + new Vec2(6 * TGs, 0), bp: 20_000);
			ServerPlayer meioGs = ForjarG12("NaRegiao", pontoGs + new Vec2(35 * TGs, 0), bp: 20_000);
			ServerPlayer foraGs = ForjarG12("NoutroPlaneta", CorredorLivre(24), bp: 20_000);
			MoveToZone(foraGs.Id, ZoneKey.Premade("Namek"), PontoDeNascimento(ZoneKey.Premade("Namek")));
			ServerPlayer mortoGs = ForjarG12("NoOutroMundo", CorredorLivre(24), bp: 20_000);
			ZoneKey alemGs = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
			MoveToZone(mortoGs.Id, alemGs, PontoDeNascimento(alemGs));
			mortoGs.Ficha.dead = true;
			ServerPlayer[] todosGs = [pertoGs, meioGs, foraGs, mortoGs];

			// FORMA A GENKIDAMA COM O SENSE NESTE NIVEL e devolve quem recebeu o convite -- e a desfaz em seguida, antes de sair (a que se desfaz
			// antes de sair nao arma a recarga de uma hora)
			string ConvidadosGs(int nivel)
			{
				formadorGs.Niveis.Por(SenseEmNiveis.Path, nivel);
				formadorGs.Ficha.Ki = formadorGs.Ficha.MaxKi;
				ApertarEOuvir(formadorGs, "SpiritBomb");
				TiquesG12(TiquesDeG12(3.1));
				string listaGs = string.Join(",", todosGs.Where(o => _ofertasDeGenkidamaG12.TryGetValue(o.Id, out int d) && d == formadorGs.Id).Select(o => o.Name));
				if (_genkidamaG12.TryGetValue(formadorGs.Id, out EstadoDaGenkidamaG12? gGs))
				{
					if (gGs.Bola is { Vivo: true }) Matar(gGs.Bola, FimDeProjetil.Nenhum);
					EncerrarGenkidamaG12(formadorGs, gGs, null);
				}
				return listaGs;
			}

			AfirmarG12("(montagem) os quatro estao onde a familia precisa: a 6 e a 35 tiles de quem forma, em Namek, e o morto no Outro Mundo",
					   DistanciaEmTiles(formadorGs, pertoGs) <= SenseEmNiveis.TilesDoCampoDeVisao
					   && DistanciaEmTiles(formadorGs, meioGs) is > SenseEmNiveis.TilesDoCampoDeVisao and <= SenseEmNiveis.TilesDaRegiao
					   && foraGs.Zone.Name == "Namek" && mortoGs.Zone.Name == Alem.ZonaDoOutroMundo && mortoGs.Ficha.dead,
					   $"{DistanciaEmTiles(formadorGs, pertoGs)} e {DistanciaEmTiles(formadorGs, meioGs)} tiles; {foraGs.Zone.Name}; {mortoGs.Zone.Name}");
			string c1Gs = ConvidadosGs(SenseEmNiveis.CampoDeVisao), c3Gs = ConvidadosGs(SenseEmNiveis.Planeta), c5Gs = ConvidadosGs(SenseEmNiveis.Universo);
			AfirmarG12("**O CONVITE VAI ATE ONDE O SENSE ALCANCA** (o dono: 'genkidama manda pedido de doacao pra todos aqueles q estao dentro do seu "
					   + "sense'): com o Sense no campo de visao, so quem esta a 6 tiles recebe a pergunta -- quem esta a 35, nao",
					   c1Gs == "NaVista", c1Gs);
			AfirmarG12("...com o Sense no planeta, quem esta a 35 tiles tambem -- e quem esta em outro planeta, nao", c3Gs == "NaVista,NaRegiao", c3Gs);
			AfirmarG12("**COM O SENSE 5, TODO JOGADOR VIVO DO UNIVERSO** (*\"com sense 5, vc pode pedir energia pra todos os jogadores vivos do "
					   + "universo\"*): o de outro planeta tambem -- e o MORTO QUE ESTA NO OUTRO MUNDO, nao (*\"menos os mortos q estao no outro mundo\"*)",
					   c5Gs == "NaVista,NaRegiao,NoutroPlaneta", c5Gs);

			// OS DOIS JOGOS DE ANTES: o convite pro planeta (sem o Sense), e o morto do Outro Mundo convidado
			GenkidamaConvidaOPlanetaDeTeste = true;
			try
			{
				string p1Gs = ConvidadosGs(SenseEmNiveis.CampoDeVisao), p5Gs = ConvidadosGs(SenseEmNiveis.Universo);
				AfirmarG12("(defeito injetado: o convite pro planeta, o jogo de antes) com o Sense no campo de visao o de 35 tiles TAMBEM e convidado, "
						   + "e com o Sense 5 o de outro planeta NAO -- as reguas do alcance REPROVAM",
						   p1Gs.Contains("NaRegiao") && !p5Gs.Contains("NoutroPlaneta"), $"{p1Gs} / {p5Gs}");
			}
			finally { GenkidamaConvidaOPlanetaDeTeste = false; }
			GenkidamaConvidaOsMortosDeTeste = true;
			try
			{
				string m5Gs = ConvidadosGs(SenseEmNiveis.Universo);
				AfirmarG12("(defeito injetado: os mortos do Outro Mundo tambem) com o Sense 5 o morto e convidado -- a regua do universo REPROVA",
						   m5Gs.Contains("NoOutroMundo"), m5Gs);
			}
			finally { GenkidamaConvidaOsMortosDeTeste = false; }

			// E O INGLES do que o convite passou a dizer (o dono: *"N esqueca de fazer versao ingles de tudo"*)
			var inglesGs = Jandirus.Core.Lingua.Tradutor.DoJson(Godot.FileAccess.GetFileAsString("res://Assets/Data/lingua-en.json"));
			string[] novasGs =
			[
				$"3 pessoa(s) ao alcance do seu Sense ({SenseEmNiveis.NomeDoAlcance(SenseEmNiveis.Universo)}) sentem a Genkidama e podem doar energia.",
				"voce acaba de receber mais energia de alguem ao alcance do seu Sense!",
			];
			string[] semInglesGs = [.. novasGs.Where(s => inglesGs.ParecePortugues(inglesGs.Traduzir(s)))];
			AfirmarG12("...e o que o convite passou a dizer tem ingles no dicionario do jogo", semInglesGs.Length == 0,
					   string.Join(" | ", novasGs.Select(inglesGs.Traduzir)));
			LimparTudoG12();
		}

		// ---- (v0.3.2) SO NA FORMA BASE: o coracao puro (dono, 2026-10-16) ----
		Vec2 praca = CorredorLivre(24);
		ServerPlayer transformado = ForjarG12("GokuSsj", praca, bp: 50_000, skills: [PathSpiritBombG12]);
		VestirForma(transformado, "ssj1");
		double kiTransformado = transformado.Ficha.Ki;
		List<string> recusa = ApertarEOuvir(transformado, "SpiritBomb");
		AfirmarG12("**SO NA FORMA BASE** (*\"so daria pra usar a genkidama na forma base, pois necessita do coraçao puro\"*): transformado, o aperto e recusado pelo coracao puro -- e nao cobra o Ki",
				   !transformado.Forma.NaBase && !_genkidamaG12.ContainsKey(transformado.Id) && Disse(recusa, "coracao puro")
				   && Math.Abs(transformado.Ficha.Ki - kiTransformado) < 1e-6,
				   $"forma {transformado.Forma.Atual} | {Ultimos(recusa)}");
		GenkidamaEmQualquerFormaDeTeste = true;
		try
		{
			ApertarEOuvir(transformado, "SpiritBomb");
			AfirmarG12("(defeito injetado: em qualquer forma, o jogo de antes) o mesmo aperto, transformado, forma a bomba -- a regua de cima REPROVA",
					   _genkidamaG12.ContainsKey(transformado.Id));
		}
		finally { GenkidamaEmQualquerFormaDeTeste = false; }
		LimparTudoG12();

		ServerPlayer noMeio = ForjarG12("GokuMeio", praca, bp: 50_000, skills: [PathSpiritBombG12]);
		ApertarEOuvir(noMeio, "SpiritBomb");
		TiquesG12(TiquesDeG12(3.1));   // formada, crescendo
		Projetil? bolaDoMeio = _genkidamaG12.TryGetValue(noMeio.Id, out EstadoDaGenkidamaG12? gMeio) ? gMeio.Bola : null;
		VestirForma(noMeio, "ssj1");
		List<string> desfez = Ouvir(() => TiquesG12(2));
		AfirmarG12("transformar-se COM ELA NA MAO desfaz a bomba (o coracao deixou de ser puro) e apaga a bola",
				   bolaDoMeio is { Vivo: false } && !_genkidamaG12.ContainsKey(noMeio.Id) && Disse(desfez, "coracao puro"),
				   $"bola {(bolaDoMeio == null ? "nenhuma" : bolaDoMeio.Vivo ? "viva" : "apagada")} | {Ultimos(desfez)}");
		LimparTudoG12();

		// ---- (v0.3.2) O POVO DOA A QUEM ELE GOSTA (dono, 2026-10-16) ----
		// *"se vc tem uma boa reputaçao no planeta, todos os npcs doariam o bp deles pra genkidama tb"*. Tres habitantes de 1 milhao de pe e
		// um de 1 bilhao NOCAUTEADO; o que o convite soma e o que o povo deu (o primeiro pulso so vem 1,5 s depois). O povo do proprio mapa
		// doa junto, e por isso a regua e "pelo menos os tres de pe, e nunca o caido".
		(double Somou, double Tres, double Caido, List<string> Ouviu) OPovoDa(double reputacao)
		{
			const string conta = "bancada_g12_heroi";
			ServerPlayer heroi = ForjarG12("GokuHeroi", praca, bp: 50_000, skills: [PathSpiritBombG12]);
			heroi.Conta = conta;
			string planeta = heroi.Zone.Name;
			if (!_reputacao.TryGetValue(planeta, out Dictionary<string, double>? livro))
				_reputacao[planeta] = livro = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
			livro[conta] = reputacao;
			try
			{
				double tres = 0;
				foreach (string nome in new[] { "PovoUm", "PovoDois", "PovoTres" })
					tres += ForjarHabitanteEm(heroi.Zone, nome, 1_000_000)?.Ficha.expressedBP ?? double.NaN;
				ServerPlayer? caido = ForjarHabitanteEm(heroi.Zone, "PovoCaido", 1e9);
				if (caido != null) DerrubarG12(caido);
				ApertarEOuvir(heroi, "SpiritBomb");
				if (!_genkidamaG12.TryGetValue(heroi.Id, out EstadoDaGenkidamaG12? gh)) return (double.NaN, tres, double.NaN, new List<string>());
				double antes = gh.BpAcumulado;
				List<string> ouviu = Ouvir(() => TiquesG12(TiquesDeG12(3.1)));
				return (gh.BpAcumulado - antes, tres, caido?.Ficha.expressedBP ?? double.NaN, ouviu);
			}
			finally { livro.Remove(conta); LimparTudoG12(); }
		}
		var amigavel = OPovoDa(Jandirus.Core.Social.Reputacao.LimiarDeAmigavel);
		AfirmarG12("**COM REPUTACAO AMIGAVEL O POVO DOA SOZINHO** (*\"se vc tem uma boa reputaçao no planeta, todos os npcs doariam o bp deles pra genkidama tb\"*): os habitantes de pe somam o poder deles a bomba ao formar -- e o caido, nao",
				   amigavel.Somou >= amigavel.Tres - 1e-6 && amigavel.Somou < amigavel.Tres + amigavel.Caido && Disse(amigavel.Ouviu, "habitante"),
				   $"somou {amigavel.Somou:0}; os tres de pe {amigavel.Tres:0}; o caido {amigavel.Caido:0} | {Ultimos(amigavel.Ouviu)}");
		var morna = OPovoDa(Jandirus.Core.Social.Reputacao.LimiarDeAmigavel - 5);
		AfirmarG12("...e abaixo de amigavel o povo nao doa nada (a bomba so tem o dono)",
				   morna.Somou == 0 && !Disse(morna.Ouviu, "habitante"), $"somou {morna.Somou:0}");
		PovoNaoDoaDeTeste = true;
		(double Somou, double Tres, double Caido, List<string> Ouviu) semPovo;
		try { semPovo = OPovoDa(Jandirus.Core.Social.Reputacao.LimiarDeAmigavel); }
		finally { PovoNaoDoaDeTeste = false; }
		AfirmarG12("(defeito injetado: o povo nao doa, o jogo de antes) com a mesma reputacao amigavel a bomba nao recebe nada -- a regua de cima REPROVA",
				   semPovo.Somou == 0, $"somou {semPovo.Somou:0}");

		// ---- (v0.3.2) LANCADA, ELA SE SEGURA NAS MAOS: o Kid Boo do dono ----
		// *"caso tenha um clash com a genkidama ou o inimigo defenda e tente se defender dela dps de lançar, vc pode se transformar pra
		// ganhar vantagem nesses casos (igual goku com kid boo)"* e *"pro clash usa apenas o poder atual do usuario dela, mas na hora q ela
		// acerta o alvo o dano é baseado no poder acumulado nela"*. A cena: a bomba vale 1,5 vez quem a lanca (as doacoes -- a soma delas e
		// medida acima), e o Kid Boo tem o DOBRO dela: nem e levado nem e parede (`KiContraCorpo.Posto`), segura. Numeros perto um do
		// outro de proposito: o dano da bomba nas maos e o que pesa na disputa (`EmbateDeKi.PoderDeSegurar`), e longe demais ele vira zero.
		// A regua e a RAZAO dos dois poderes, que nao tem teto -- a vantagem e cortada entre 1/6 e 6 (`EmbateDeKi.TetoDaVantagem`).
		Vec2 pista = TerraFirmeG12(24);
		(ServerPlayer Goku, ServerPlayer Boo, Projetil Bomba, double Acumulado)? CenaDoKidBoo(string sufixo)
		{
			ServerPlayer goku = ForjarG12("GokuKid" + sufixo, pista, bp: 50_000, skills: [PathSpiritBombG12]);
			goku.Facing = Facing.East;
			ApertarEOuvir(goku, "SpiritBomb");
			if (!_genkidamaG12.TryGetValue(goku.Id, out EstadoDaGenkidamaG12? gk)) return null;
			TiquesG12(TiquesDeG12(3.1));
			gk.BpAcumulado = 1.5 * goku.Ficha.expressedBP;
			double acumulado = gk.BpAcumulado;
			ServerPlayer boo = ForjarG12("KidBoo" + sufixo, pista + new Vec2(12 * ZoneCollision.TileSize, 0), bp: 50_000);
			boo.Facing = Facing.West;
			SegurarAGuarda(boo);
			boo.Ficha.expressedBP = 2 * acumulado;
			ApertarEOuvir(goku, "SpiritBomb");
			TiquesG12(TiquesDeG12(2.0) + 1);   // sai
			Projetil? bomba = gk.Bola;
			for (int i = 0; i < TiquesDeG12(3.0) && bomba is { Vivo: true } && !_emEmbateDeKi.ContainsKey(boo.Id); i++) TiquesG12(1);
			return bomba == null ? null : (goku, boo, bomba, acumulado);
		}
		double RazaoDaBomba(DisputaDeKi d) => PoderDoLado(d.A) / Math.Max(PoderDoLado(d.B), 1e-300);
		void FecharADisputaDe(ServerPlayer quem) { if (_emEmbateDeKi.TryGetValue(quem.Id, out DisputaDeKi? aberta)) Fechar(aberta); }

		var cena = CenaDoKidBoo("A");
		DisputaDeKi? dk = null;
		bool segurou = cena is { } c0 && _emEmbateDeKi.TryGetValue(c0.Boo.Id, out dk) && dk.Tipo == Protocol.TipoDeEmbate.FeixeContraGuarda
						   && ReferenceEquals(dk.A.Feixe, c0.Bomba) && dk.B.DeMaos;
		AfirmarG12("**A GENKIDAMA SE SEGURA NAS MAOS** (*\"o inimigo defenda e tente se defender dela [...] igual goku com kid boo\"*): quem tem o dobro dela e esta de guarda a SEGURA -- o embate de guarda, que era so do raio",
				   segurou, cena is { } c1 ? $"bomba viva {c1.Bomba.Vivo}; em disputa {_emEmbateDeKi.ContainsKey(c1.Boo.Id)}" : "sem bomba");
		if (segurou && cena is { } c && dk != null)
		{
			double donoNaHora = c.Goku.Ficha.expressedBP;
			AfirmarG12("**NA DISPUTA ELA VALE O PODER DE AGORA DE QUEM A LANCOU** (*\"pro clash usa apenas o poder atual do usuario dela\"*), e nao o acumulado nela",
					   Math.Abs(c.Bomba.Bp - donoNaHora * c.Bomba.MultDeOnda) < 1e-6 * donoNaHora && c.Bomba.Bp < c.Acumulado / 1.2,
					   $"bomba {c.Bomba.Bp:0}; o dono agora {donoNaHora:0}; o acumulado {c.Acumulado:0}");

			// TRANSFORMAR-SE DEPOIS DE LANCAR: a forma nova entra na disputa no tique seguinte (a disputa rele o poder a cada tique). As duas
			// medidas saem da mesma conta de poder (`PowerLevel`), com o mesmo tanque.
			c.Goku.Ficha.PowerLevel(agoraMs: NowMs());
			TickDosEmbatesDeKi(Protocol.TickSeconds);
			double antes = c.Goku.Ficha.expressedBP, razaoAntes = RazaoDaBomba(dk);
			VestirForma(c.Goku, "ssj1");
			c.Goku.Ficha.PowerLevel(agoraMs: NowMs());
			double depois = c.Goku.Ficha.expressedBP;
			TiquesG12(1);
			TickDosEmbatesDeKi(Protocol.TickSeconds);
			double razaoDepois = RazaoDaBomba(dk);
			AfirmarG12("**TRANSFORMAR-SE DEPOIS DE LANCAR VIRA O CABO DE GUERRA** (*\"vc pode se transformar pra ganhar vantagem nesses casos\"*): a bomba nao se desfaz, continua na disputa, e a forca dela contra as maos sobe com a forma nova",
					   depois > antes * 1.1 && c.Bomba.Vivo && _emEmbateDeKi.ContainsKey(c.Boo.Id) && razaoDepois > razaoAntes * 1.1
					   && Math.Abs(c.Bomba.Bp - depois * c.Bomba.MultDeOnda) < 1e-6 * depois,
					   $"o dono {antes:0} -> {depois:0} ({c.Goku.Forma.Atual}); bomba contra as maos {razaoAntes:0.####} -> {razaoDepois:0.####}; vantagem {dk.A.Vantagem:0.###}");

			// A BOMBA VENCE E SEGUE PRO ALVO: com o ACUMULADO
			Resolver(dk, dk.A, dk.B, "a bancada");
			AfirmarG12("**QUANDO ELA ACERTA O ALVO, O DANO E O DO ACUMULADO** (*\"na hora q ela acerta o alvo o dano é baseado no poder acumulado nela\"*): a bomba que vence a disputa segue pro Kid Boo com o poder acumulado, e nao com o do dono",
					   !c.Bomba.EmEmbate && Math.Abs(c.Bomba.Bp - c.Acumulado) < 1e-6 * c.Acumulado,
					   $"bomba {c.Bomba.Bp:0}; acumulado {c.Acumulado:0}; o dono {c.Goku.Ficha.expressedBP:0}");
		}
		LimparTudoG12();

		// os tres jogos de antes, cada um numa cena propria
		GenkidamaNaoESeguradaDeTeste = true;
		try
		{
			var semMaos = CenaDoKidBoo("B");
			AfirmarG12("(defeito injetado: a Genkidama nao se segura, o jogo de antes) ela chega no Kid Boo de guarda e ninguem a segura -- a regua do embate de guarda REPROVA",
					   semMaos is { } sm && !_emEmbateDeKi.ContainsKey(sm.Boo.Id), semMaos is { } sm1 ? $"bomba viva {sm1.Bomba.Vivo}" : "sem bomba");
			if (semMaos is { } sm2) FecharADisputaDe(sm2.Boo);
		}
		finally { GenkidamaNaoESeguradaDeTeste = false; }
		LimparTudoG12();

		GenkidamaDisputaComOAcumuladoDeTeste = true;
		try
		{
			var comAcumulado = CenaDoKidBoo("C");
			AfirmarG12("(defeito injetado: a disputa com o acumulado, a regra que o dono corrigiu) na disputa a bomba vale o acumulado -- a regua do poder de agora REPROVA",
					   comAcumulado is { } ca && _emEmbateDeKi.ContainsKey(ca.Boo.Id) && Math.Abs(ca.Bomba.Bp - ca.Acumulado) < 1e-6 * ca.Acumulado,
					   comAcumulado is { } ca1 ? $"bomba {ca1.Bomba.Bp:0}; acumulado {ca1.Acumulado:0}" : "sem bomba");
			if (comAcumulado is { } ca2) FecharADisputaDe(ca2.Boo);
		}
		finally { GenkidamaDisputaComOAcumuladoDeTeste = false; }
		LimparTudoG12();

		GenkidamaPerdeOAcumuladoNaDisputaDeTeste = true;
		try
		{
			var semAcumulado = CenaDoKidBoo("D");
			bool reprova = false;
			string visto = "a disputa nao abriu";
			if (semAcumulado is { } sa && _emEmbateDeKi.TryGetValue(sa.Boo.Id, out DisputaDeKi? dd))
			{
				Resolver(dd, dd.A, dd.B, "a bancada");
				double doDono = sa.Goku.Ficha.expressedBP * Math.Max(sa.Bomba.MultDeOnda, 1);
				reprova = Math.Abs(sa.Bomba.Bp - doDono) < 1e-6 * doDono && sa.Bomba.Bp < sa.Acumulado / 1.2;
				visto = $"bomba {sa.Bomba.Bp:0}; acumulado {sa.Acumulado:0}; o dono {doDono:0}";
			}
			AfirmarG12("(defeito injetado: o acumulado se perde na disputa, o jogo de antes) a bomba que vence segue com o poder do DONO -- a regua do acerto REPROVA",
					   reprova, visto);
		}
		finally { GenkidamaPerdeOAcumuladoNaDisputaDeTeste = false; }
		LimparTudoG12();
	}

	/// <summary>Aperta um verb de MENU (`C2S.Verbo`) pelo funil de producao e diz se a resposta trouxe o trecho.</summary>
	private bool ApertarVerboG12(ServerPlayer pl, string cmd, string trecho, string arg = "")
		=> Disse(Ouvir(() => Verbo(pl, cmd, arg)), trecho);

	/// <summary>O ultimo `S2C.MinhasCopias` que este jogador recebeu, aberto (os ids). Nulo se nenhum.</summary>
	private static List<int>? UltimasCopiasG12(int para)
	{
		if (EscutaDeCopiasG12 == null) return null;
		for (int i = EscutaDeCopiasG12.Count - 1; i >= 0; i--)
		{
			(int quem, byte[] fio) = EscutaDeCopiasG12[i];
			if (quem != para || fio.Length == 0 || fio[0] != (byte)Protocol.S2C.MinhasCopias) continue;
			var r = new LiteNetLib.Utils.NetDataReader(fio);
			r.GetByte();
			int n = r.GetInt();
			var ids = new List<int>(n);
			for (int k = 0; k < n; k++) ids.Add(r.GetInt());
			return ids;
		}
		return null;
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

		AfirmarG12("contra alguem DE PE o verb recusa em voz alta ('NOCAUTEADO')", Disse(ApertarEOuvir(demo, "Soul_Absorb"), "NOCAUTEADO"));
		DerrubarG12(vitima);
		double kiAntes = f.Ki, kiVitima = vitima.Ficha.Ki, absorbAntes = f.AbsorbBP;
		List<string> falou = ApertarEOuvir(demo, "Soul_Absorb");
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
		AfirmarG12("um caido que ja nao tem alma e recusado ('nao tem mais alma')", Disse(ApertarEOuvir(demo, "Soul_Absorb"), "nao tem mais alma") && !oca.Ficha.dead);
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
		AfirmarG12("contra alguem de pe o verb recusa ('NOCAUTEADO')", Disse(ApertarEOuvir(c17, "Absorb_Android"), "NOCAUTEADO"));

		DerrubarG12(robo);
		double absorbAntes = f.AbsorbBP;
		ApertarEOuvir(c17, "Absorb_Android");
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
		ApertarEOuvir(c17, "Absorb_Android");
		AfirmarG12("o HUMANO caido abre o dreno sustentado", _drenoG12.ContainsKey(c17.Id));
		TiquesG12(TiquesDeG12(0.7) + 1);
		AfirmarG12("0,7 s depois um decimo do Ki dele passou pra voce", Math.Abs(humano.Ficha.Ki - kiH * 0.9) < 1e-6 && Math.Abs(f.Ki - (kiC + kiH * 0.1)) < 1e-6,
				   $"vitima {kiH:0} -> {humano.Ficha.Ki:0}; voce {kiC:0} -> {f.Ki:0}");
		TiquesG12(TiquesDeG12(0.7));
		AfirmarG12("...e mais um decimo do que sobrou, 0,7 s depois (o dreno CONTINUA)", Math.Abs(humano.Ficha.Ki - kiH * 0.81) < 1e-6 && _drenoG12.ContainsKey(c17.Id));
		AfirmarG12("...e o AbsorbBP cresce M.BP/50 por tique", f.AbsorbBP > absorbAntes);
		ApertarEOuvir(c17, "Absorb_Android");
		AfirmarG12("o segundo aperto PARA o dreno", !_drenoG12.ContainsKey(c17.Id));

		// O GOLPE INTERROMPE.
		ServerPlayer humano2 = ForjarG12("Humano2", chao + new Vec2(0, 28), bp: 30_000);
		DerrubarG12(humano2);
		c17.AlvoId = humano2.Id;
		TiquesG12(TiquesDeG12(2.1));
		ApertarEOuvir(c17, "Absorb_Android");
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
		ApertarEOuvir(c17, "Absorb_Android");
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
		AfirmarG12("sem ninguem a cinco tiles o verb recusa", Disse(ApertarEOuvir(ForjarG12("Sozinho", CorredorLivre(24), bp: 5_000, skills: [PathImitationG12]), "Imitation"), "ninguem"));
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
		ApertarEOuvir(puar, "Imitation");
		AfirmarG12("o aperto copia o NOME visivel do modelo (e o nome do save continua o seu)",
				   puar.Disfarce != null && NomeVisivel(puar) == modelo.Name && puar.Name == nomeReal);
		Jandirus.Core.Appearance.Appearance v = VisualVisivel(puar);
		AfirmarG12("...e a aparencia campo a campo: cabelo, corpo, cor de pele, raca e genero",
				   v.Cabelo == modelo.Visual.Cabelo && v.Corpo == modelo.Visual.Corpo && Equals(v.CorPele, modelo.Visual.CorPele)
				   && puar.Disfarce!.Raca == "Namekian" && puar.Disfarce.Genero == "Female");
		AfirmarG12("...por COPIA e nao por referencia (mexer no modelo depois nao muda o disfarce)",
				   ReferenceEquals(v, modelo.Visual) == false);
		AfirmarG12("...e a ficha de aparencia de verdade nao foi tocada", puar.Visual.Cabelo != "Spiky" && puar.Visual.Corpo != 3);

		ApertarEOuvir(puar, "Imitation");
		AfirmarG12("o segundo aperto devolve o proprio rosto e o proprio nome",
				   puar.Disfarce == null && NomeVisivel(puar) == nomeReal && ReferenceEquals(VisualVisivel(puar), puar.Visual));

		// O RELOG derruba o disfarce.
		ApertarEOuvir(puar, "Imitation");
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

		ApertarEOuvir(tien, "SplitForm");
		ServerPlayer? copia = CopiaDeG12(tien);
		AfirmarG12("o aperto poe uma copia NOVA no mundo, com cerebro (IA), o nome '<dono> Copy' e METADE do poder expresso",
				   copia != null && _players.Count == corposAntes + 1 && copia.Cerebro != null && copia.Name == tien.Name + " Copy"
				   && Math.Abs(copia.Ficha.BP - expressoAntes / 2) < 0.5,
				   copia == null ? "sem copia" : $"bp {copia.Ficha.BP:0} (metade de {expressoAntes:0} = {expressoAntes / 2:0})");
		if (copia == null) { LimparTudoG12(); return; }
		AfirmarG12("...um passo a frente do dono, com a mesma aparencia e sem tela (Peer nulo)",
				   Math.Abs(copia.Pos.X - (chao.X + 32)) < 0.5 && copia.Peer == null && copia.Visual.Cabelo == tien.Visual.Cabelo);
		AfirmarG12("...cobrando MaxKi x 0,5 / Splitformskill (skill 1)", Math.Abs(kiAntes - f.MaxKi * 0.5 - f.Ki) < 0.5, $"{kiAntes:0} -> {f.Ki:0}");
		AfirmarG12("...e o dono passa a contar UMA copia viva", f.splitformCount == 1, $"count {f.splitformCount}");

		// ======================== AS DUAS CAUSAS, SEPARADAS ========================
		// A prova que morava aqui dizia "o `splitformdeBuff` corta o poder expresso dele" e conferia
		// `f.expressedBP < expressoAntes`. Ela passava, mas NAO pelo motivo que a frase dava: quem
		// derrubava o numero era o KI COBRADO pela divisao (`MaxKi*0,5`), que leva o `kiratio` de 1,0
		// pro piso de 0,6. Medido em 2026-09-02: 49020 -> 29412, razao 0,600 exata. O corte por copia
		// nao existia (ver a divergencia declarada em `Fighter.Power.cs`) e a frase o creditava.
		//
		// UMA AFIRMACAO SO NAO CONSEGUE dizer qual de duas causas simultaneas ela mediu -- e foi por
		// isso que ela ficou verde por cima de um mecanismo morto. Agora sao duas, cada uma segurando
		// a outra variavel parada, e cada uma com o CONTRA-EXEMPLO que a derruba se ela voltar a medir
		// a causa errada.
		// ===========================================================================
		double kiDepoisDaDivisao = f.Ki;
		f.splitformMastery = 0;   // o `prob(50/(Splitformskill*5))` pode ter subido o skill na primeira: a bancada zera o dado

		// As DUAS chamadas que a producao faz quando a contagem muda (`AtualizarContagemDeSplitformsG12`):
		// o `Statify` escreve o `splitformdeBuff` e o `PowerLevel` -- dentro do `RepercutirPoder` -- o gasta.
		// Medir pelo mesmo funil do jogo e o que impede a bancada de provar uma conta que so ela faz.
		double PoderAgora() { f.Statify(); RepercutirPoder(tien); return f.expressedBP; }

		// ---- CAUSA 1: O KI QUE A DIVISAO COBROU ----
		// A copia viva fica FIXA (contagem 1 nos dois lados); so o tanque muda. (2026-10-24) O Ki so tira poder ABAIXO DA METADE do tanque
		// (o dono, `Fighter.RazaoDoKiNoPoder`): a divisao de um tanque cheio para na metade e nao custa nada -- custa quando o leva abaixo
		// dela. A bancada poe o tanque em 30% (o que a segunda divisao seguida deixaria) e o devolve.
		f.Ki = f.MaxKi * 0.3;
		double comKiGasto = PoderAgora();
		f.Ki = f.MaxKi;
		double comKiCheio = PoderAgora();
		AfirmarG12("CAUSA 1 -- o Ki cobrado pela divisao derruba o poder expresso pelo `kiratio` quando leva o tanque ABAIXO DA METADE: com a MESMA copia viva de pe, devolver o tanque levanta o numero de volta",
				   comKiCheio > comKiGasto, $"Ki 30% -> poder {comKiGasto:0}; Ki cheio -> poder {comKiCheio:0} (a divisao deixou {kiDepoisDaDivisao:0})");

		// O CONTRA-EXEMPLO DA CAUSA 1: (2026-10-24) da METADE do tanque pra cima gastar Ki nao move poder nenhum -- e e ali que a divisao de
		// um tanque cheio para. (Antes o trecho sem efeito era o de baixo, sob o piso de 0,6 do `kiratio`; hoje o piso so chega no vazio.)
		f.Ki = f.MaxKi * 0.9;
		double acimaDaMetade = PoderAgora();
		f.Ki = f.MaxKi * 0.55;
		double renteAMetade = PoderAgora();
		AfirmarG12("...e o CONTRA-EXEMPLO dela: da metade do tanque pra cima gastar MAIS Ki nao move o poder -- a divisao de um tanque cheio (que para na metade) nao custa nada",
				   Math.Abs(acimaDaMetade - renteAMetade) < 0.5, $"Ki 90% -> {acimaDaMetade:0}; Ki 55% -> {renteAMetade:0}");

		// ---- CAUSA 2: A COPIA VIVA ----
		// Agora o Ki fica CHEIO nos dois lados e so a contagem muda. Este e o corte que nao existia.
		f.Ki = f.MaxKi;
		double comUmaCopia = PoderAgora();
		f.splitformCount = 0;
		double semCopiaNenhuma = PoderAgora();
		f.splitformCount = 1;
		double razaoDoCorte = comUmaCopia / Math.Max(semCopiaNenhuma, 1);
		AfirmarG12("CAUSA 2 -- com o Ki CHEIO nos dois lados, a copia viva SOZINHA corta o poder expresso pela METADE (`(1+0)/(1+1)`)",
				   comUmaCopia < semCopiaNenhuma && Math.Abs(razaoDoCorte - 0.5) < 0.01,
				   $"sem copia {semCopiaNenhuma:0}, com uma {comUmaCopia:0} -- razao {razaoDoCorte:0.####} (esperado 0,5)");

		// O CONTRA-EXEMPLO DA CAUSA 2: quem paga o custo e a MAESTRIA. Com maestria 1 a MESMA copia viva
		// deixa de custar (`(1+1)/(1+1) = 1`) e o poder volta INTEIRO. Se a prova de cima passar a medir
		// qualquer outra coisa que a divisao mexeu no corpo, esta aqui nao volta ao numero inteiro.
		f.splitformMastery = 1;
		double comCopiaMasterizada = PoderAgora();
		AfirmarG12("...e o CONTRA-EXEMPLO dela: com maestria 1 a MESMA copia viva para de custar e o poder volta INTEIRO",
				   Math.Abs(comCopiaMasterizada - semCopiaNenhuma) < 0.5,
				   $"cortado {comUmaCopia:0} -> masterizado {comCopiaMasterizada:0} (inteiro {semCopiaNenhuma:0})");

		// devolve o corpo ao estado de PRODUCAO: uma copia viva, maestria zerada, Ki como a divisao deixou
		f.splitformMastery = 0;
		f.Ki = kiDepoisDaDivisao;
		PoderAgora();
		AfirmarG12("com skill 1, a segunda divisao e recusada ('pericia')", Disse(ApertarEOuvir(tien, "SplitForm"), "pericia"));

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
		// O Ki NAO muda no caminho do 'Destroy' (`AtualizarContagemDeSplitformsG12` so refaz a conta),
		// entao a diferenca de poder daqui pra logo depois e a CONTAGEM e mais nada.
		double expressoComACopiaViva = PoderAgora();
		ApertarVerboG12(tien, "splitform_destruir", "reabsorve");
		AfirmarG12("'Destroy': a copia sai do mundo, o dono volta a contar zero copias, o corte por copia some -- e com o MESMO Ki o poder expresso sobe de volta",
				   !_players.ContainsKey(copia.Id) && f.splitformCount == 0 && Math.Abs(f.splitformdeBuff - 1) < 1e-9
				   && f.expressedBP > expressoComACopiaViva,
				   $"count {f.splitformCount}, debuff {f.splitformdeBuff:0.###}, expresso {expressoComACopiaViva:0} -> {f.expressedBP:0}");

		// O PRAZO: 100 s.
		f.Ki = f.MaxKi;
		ApertarEOuvir(tien, "SplitForm");
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
		ApertarEOuvir(tien, "SplitForm");
		ServerPlayer? terceira = CopiaDeG12(tien);
		if (terceira != null)
		{
			terceira.Ficha.HP = 5;
			DerrubarG12(terceira);
			TiquesG12(TiquesDeG12(1.1));
			AfirmarG12("a copia nocauteada com 8 de vida ou menos e desfeita (`HP<=8 && KO`)", !_players.ContainsKey(terceira.Id));
		}

		// ---- (2026-10-24) O CLIQUE NUMA COPIA SO: o `Click()` da copia (`Split Forms.dm:28-68`) ----
		// O dono: *"Eu clicava na copia como no DM e nao aparecia nada"*. O clique do cliente abre o menu na copia clicada, e a ordem
		// sai com o id DELA: so ela obedece. Os verbos da aba Other (sem argumento) seguem valendo pra todas. E o cliente so sabe quais
		// corpos sao copias dele pelo `S2C.MinhasCopias`, que sai a cada copia que nasce ou se desfaz.
		EscutaDeCopiasG12 = [];
		try
		{
			f.splitformMastery = Math.Max(f.splitformMastery, 0.2);   // skill 2: duas copias de pe
			foreach (int c in _splitformsG12.Where(kv => kv.Value.Master == tien.Id).Select(kv => kv.Key).ToList()) DesfazerSplitformG12(c);
			f.Ki = f.MaxKi;
			Ouvir(() => DividirOCorpoG12(tien));   // direto: o gesto e o da ordem, e nao o da tecla
			f.Ki = f.MaxKi;
			Ouvir(() => DividirOCorpoG12(tien));   // direto: o gesto e o da ordem, e nao o da tecla
			List<int> duas = [.. _splitformsG12.Where(kv => kv.Value.Master == tien.Id).Select(kv => kv.Key)];
			AfirmarG12("(montagem) com skill 2 o dono tem DUAS copias de pe", duas.Count == 2, $"{duas.Count} copia(s)");
			List<int>? contadas = UltimasCopiasG12(tien.Id);
			AfirmarG12("**O DONO RECEBE A LISTA DAS COPIAS DELE** (`S2C.MinhasCopias`): o ultimo pacote, depois da segunda divisao, traz as duas -- e so elas",
					   contadas != null && contadas.Count == 2 && duas.All(contadas.Contains),
					   contadas == null ? "nenhum pacote" : string.Join(",", contadas));
			if (duas.Count == 2)
			{
				int a = duas[0], b = duas[1];
				bool SoAClicada()
				{
					ApertarVerboG12(tien, "splitform_parar", "para");
					ApertarVerboG12(tien, "splitform_seguir", "segui", a.ToString());
					return _splitformsG12[a].Funcao == FuncaoDeSplitformG12.Seguir && _splitformsG12[b].Funcao == FuncaoDeSplitformG12.Parado;
				}
				AfirmarG12("**A ORDEM DO CLIQUE E SO DA COPIA CLICADA** (dono, 2026-10-24): 'Follow' com o id dela -- ela segue, a irma continua parada",
						   SoAClicada(), $"{_splitformsG12[a].Funcao} / {_splitformsG12[b].Funcao}");
				OrdemDaCopiaPraTodasDeTeste = true;
				try
				{
					AfirmarG12("(defeito injetado: a ordem do clique vale pra todas, o jogo de antes) a mesma regua REPROVA -- a irma segue junto",
							   !SoAClicada(), $"{_splitformsG12[a].Funcao} / {_splitformsG12[b].Funcao}");
				}
				finally { OrdemDaCopiaPraTodasDeTeste = false; }
				ApertarVerboG12(tien, "splitform_parar", "para");
				ApertarVerboG12(tien, "splitform_seguir", "seguir");
				AfirmarG12("...e a aba Other continua mandando em TODAS: 'Follow' sem argumento, as duas seguem",
						   _splitformsG12[a].Funcao == FuncaoDeSplitformG12.Seguir && _splitformsG12[b].Funcao == FuncaoDeSplitformG12.Seguir);
				AfirmarG12("...e a ordem pro id de uma copia que ja se desfez e recusada, e nao cai nas outras",
						   ApertarVerboG12(tien, "splitform_parar", "ja se desfez", "999999")
						   && _splitformsG12[a].Funcao == FuncaoDeSplitformG12.Seguir && _splitformsG12[b].Funcao == FuncaoDeSplitformG12.Seguir);

				ApertarVerboG12(tien, "splitform_destruir", "reabsorve", a.ToString());
				List<int>? depois = UltimasCopiasG12(tien.Id);
				AfirmarG12("**'Destroy Splitforms' (o do DM: todas)**, mesmo do menu de uma: as duas se desfazem, e a lista que chega ao dono vem VAZIA",
						   !_players.ContainsKey(a) && !_players.ContainsKey(b) && depois is { Count: 0 },
						   depois == null ? "nenhum pacote" : string.Join(",", depois));
			}

			CopiasCaladasDeTeste = true;
			try
			{
				int antes = EscutaDeCopiasG12.Count;
				f.Ki = f.MaxKi;
				Ouvir(() => DividirOCorpoG12(tien));   // direto: o gesto e o da ordem, e nao o da tecla
				AfirmarG12("(defeito injetado: o pacote das copias calado) a copia nasce e NENHUMA lista sai -- a regua do pacote REPROVARIA",
						   CopiaDeG12(tien) != null && EscutaDeCopiasG12.Count == antes);
			}
			finally { CopiasCaladasDeTeste = false; }
		}
		finally { EscutaDeCopiasG12 = null; }
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
		ApertarEOuvir(korin, "Grow_Senzu_Bean");
		AfirmarG12("o aperto comeca a cultivar", _senzuG12.ContainsKey(korin.Id));
		AfirmarG12("...e apertar de novo enquanto cresce e recusado ('espere')", Disse(ApertarEOuvir(korin, "Grow_Senzu_Bean"), "espere"));
		List<Largado> noChao = LargadosDe(korin.Zone.Hash);
		Largado? Broto() => noChao.Find(l => l.Tipo == TipoDeLargado.Item && l.Item == CatalogoDeItens.Senzu);
		const float tile = ZoneCollision.TileSize;
		// A CELULA AO SUL TEM DE SER CHAO pra a regra aparecer -- contra a parede ou a agua a semente brota aos pes. O
		// corredor da bancada e uma fileira solta (ao sul dele nao ha chao em parte alguma), entao Korin cultiva numa
		// celula que TENHA sul: a fileira de cima do corredor (o sul dela e o proprio corredor) ou, se ela for toda
		// parede, a primeira dupla livre do mapa. Depois de colher ele volta pro corredor, pro resto da cena.
		if (_pjMapa is { } mapaDoSenzu)
		{
			bool EhChao(int x, int y) => x >= 0 && y >= 0 && x < mapaDoSenzu.Width && y < mapaDoSenzu.Height && !mapaDoSenzu.BlockedCell(x, y) && !mapaDoSenzu.EhAgua(x, y);
			(int cx0, int cy0) = ((int)MathF.Floor(chao.X / tile), (int)MathF.Floor(chao.Y / tile));
			(int X, int Y)? achei = null;
			for (int k = 0; k < 24 && achei == null; k++)
				if (EhChao(cx0 + k, cy0 - 1) && EhChao(cx0 + k, cy0)) achei = (cx0 + k, cy0 - 1);
			for (int y = 8; y < mapaDoSenzu.Height - 8 && achei == null; y++)
				for (int x = 8; x < mapaDoSenzu.Width - 8 && achei == null; x++)
					if (EhChao(x, y) && EhChao(x, y + 1)) achei = (x, y);
			if (achei is { } onde) korin.Pos = new Vec2(onde.X * tile + tile / 2, onde.Y * tile + tile / 2);
		}
		TiquesG12(TiquesDeG12(59.0));
		AfirmarG12("aos 59 s ainda NAO ha semente -- nem na mochila, nem no chao", korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 0 && Broto() == null);
		TiquesG12(TiquesDeG12(1.1));
		// (2026-10-13) ELA BROTA NO CHAO, um passo ao sul: `A.loc = locate(x, (y-1), z)` (`Food.dm:10`). Ate a v0.2.0
		// nascia na mochila, porque o port nao tinha item largado no mundo.
		Largado? broto = Broto();
		AfirmarG12("aos 60 s a Semente Senzu BROTA NO CHAO, um passo ao SUL de quem cultivou -- e a mochila continua sem ela",
				   broto is { Quantidade: 1 } && !_senzuG12.ContainsKey(korin.Id) && korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 0
				   && MathF.Floor(broto.X / tile) == MathF.Floor(korin.Pos.X / tile) && MathF.Floor(broto.Y / tile) == MathF.Floor(korin.Pos.Y / tile) + 1,
				   broto == null ? "nada no chao" : $"no chao em ({broto.X:0},{broto.Y:0}); Korin em ({korin.Pos.X:0},{korin.Pos.Y:0})");
		Verbo(korin, Chao.VerboPegar, "");
		AfirmarG12("...e pegar do chao a poe na mochila", korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 1 && Broto() == null);

		SenzuBrotaNaMochilaDeTeste = true;
		try
		{
			ServerPlayer deAntes = ForjarG12("KorinDeAntes", korin.Pos + new Vec2(2 * tile, 0), bp: 5_000, skills: [PathGrowSenzuG12]);
			ApertarEOuvir(deAntes, "Grow_Senzu_Bean");
			TiquesG12(TiquesDeG12(60.1));
			AfirmarG12("(defeito injetado: a semente nasce na mochila) nada brota no chao -- a v0.2.0",
					   deAntes.Mochila.Quantos(CatalogoDeItens.Senzu) == 1 && Broto() == null);
			TirarDoMundoV2(deAntes);   // (senao ele fica ao lado, e o "acudir" de mais abaixo o acha no lugar do caido)
		}
		finally { SenzuBrotaNaMochilaDeTeste = false; }
		korin.Pos = chao;

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
		bool recusou = Disse(Ouvir(() => ComandoDeItem(korin, "item_comer", CatalogoDeItens.Senzu)), "digere");
		AfirmarG12("...e uma segunda semente logo depois e recusada (`Senzu + 4 <= 4`): o corpo ainda digere",
				   recusou && korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 1);

		// ACUDIR: o caido levanta E e curado; quem DA fica como estava (o DM curava quem da: `sensuuse(usr)`, Food.dm:63).
		ServerPlayer caido = ForjarG12("CaidoS", chao + new Vec2(28, 0), bp: 5_000);
		DerrubarG12(caido);
		BodyPart meu = korin.Combate.Corpo.Partes.First(x => x.Papel == Vitalidade.Membro);
		meu.Vida = 40;
		BodyPart dele = caido.Combate.Corpo.Partes.First(x => x.Papel == Vitalidade.Membro);
		dele.Vida = 40;
		ComandoDeItem(korin, "item_acudir", CatalogoDeItens.Senzu);
		AfirmarG12("acudir um caido o LEVANTA e consome a semente", !caido.Ficha.KO && korin.Mochila.Quantos(CatalogoDeItens.Senzu) == 0);
		AfirmarG12("...e cura QUEM RECEBE (o membro do caido volta de 40 ao cheio) e NAO quem da (o meu fica em 40) -- o DM fazia o contrario (`sensuuse(usr)`, Food.dm:63; consertado por decisao do dono)",
				   Math.Abs(dele.Vida - dele.VidaMax) < 1e-6 && Math.Abs(meu.Vida - 40) < 1e-6, $"meu {meu.Vida:0}, dele {dele.Vida:0}");
		korin.Mochila.Guardar(CatalogoDeItens.Senzu);
		bool semCaido = Disse(Ouvir(() => ComandoDeItem(korin, "item_acudir", CatalogoDeItens.Senzu)), "DESACORDADO");
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
		ApertarEOuvir(pl, "Ki_Targets");
		AfirmarG12("o aperto poe o corpo em MEDITACAO e liga os alvos", pl.Ficha.med && _alvosDeKiG12.ContainsKey(pl.Id));
		TiquesG12(1);
		List<Projetil> alvos = TirosDeG12(pl);
		AfirmarG12("o primeiro alvo nasce no primeiro tique: um projetil INERTE do dono, sem dano, ate 4 tiles",
				   alvos.Count == 1 && alvos[0].Inerte && alvos[0].BaseDano == 0 && Math.Abs(alvos[0].Pos.X - chao.X) <= 4 * 32 + 1 && Math.Abs(alvos[0].Pos.Y - chao.Y) <= 4 * 32 + 1,
				   $"{alvos.Count} alvos");
		Projetil alvo = alvos[0];
		Vec2 nasceu = alvo.Pos;
		// SAIU DO LUGAR EM ALGUM TIQUE, e nao "esta noutro lugar no fim": sao quatro passos sorteados em oito
		// rumos, e um em cada vinte passeios desses VOLTA ao tile de onde saiu (leste e oeste, norte e sul) -- a
		// regua que so olhava o fim reprovava um alvo que andou os quatro passos.
		bool vagou = false;
		for (int i = TiquesDeG12(2.0); i > 0; i--)
		{
			TiquesG12(1);
			vagou |= Vec2.Distance(alvo.Pos, nasceu) > 1;
		}
		AfirmarG12("...e VAGA (um tile sorteado a cada 0,5 s)", vagou || !alvo.Vivo, $"{nasceu} -> {alvo.Pos}");
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
		ApertarEOuvir(atirador, "Basic_Blast");
		AfirmarG12("o tiro inimigo saiu", TirosDeG12(atirador).Count == 1);
		TiquesG12(TiquesDeG12(1.5));
		AfirmarG12("quem tem a skill da um passo pro lado (norte: `turn(EAST, 90)`) e vira pra la, sem apertar nada",
				   _esquivasDePrecognicaoG12 >= esquivasAntes + 1 && vidente.Pos.Y <= chao.Y - 32 + 0.5 && Math.Abs(vidente.Pos.X - chao.X) < 0.5
				   && vidente.Facing == Facing.North,
				   $"esquivas +{_esquivasDePrecognicaoG12 - esquivasAntes}, pos {chao} -> {vidente.Pos}, olhar {vidente.Facing}");

		// ============================ DE QUALQUER DISTANCIA, E NAO SO DA QUE CALHA DE DAR CERTO ============================
		// A Precognicao e amostrada a 5 Hz, e a bola comum anda 3,2 tiles entre duas amostras (dono, 2026-10-08): com
		// a caixa de 1,5 tile do DM parada, o tiro so seria visto se uma amostra calhasse de cair no pedaco de tile em
		// que ele ja esta na caixa e ainda nao acertou. O aviso anda com a pressa do tiro (`Projetil.VemPraCimaDe`),
		// e por isso quem ve o futuro esquiva de TODOS -- o atirador a 3, a 3,5 ... a 6 tiles.
		// ====================================================================================================================
		// A PISTA E DE TERRA FIRME NAS TRES FILEIRAS (o passo de lado precisa do tile de cima livre) e o atirador
		// fica DENTRO dela -- na cena de cima ele mora quatro tiles a oeste do corredor, fora do trecho garantido.
		// Os dois corpos voltam pro corredor deles no fim e a faixa volta pro mapa: as familias seguintes nao
		// perdem chao por causa desta.
		int faixaDaPista = _pjProximoCorredor;
		Vec2 posto = TerraFirmeG12(10) + new Vec2(7 * ZoneCollision.TileSize, 0);
		(int esquivou, int tiros, string semEsquiva) = OVidenteContraTirosDeTodaDistancia(vidente, atirador, posto);
		AfirmarG12($"quem ve o futuro esquiva do tiro que vem de QUALQUER distancia ({esquivou} de {tiros})",
				   tiros >= 6 && esquivou == tiros, "nao esquivou de: " + semEsquiva);

		Projetil.AvisoSemPressaDeTeste = true;
		try
		{
			(int comDefeito, int total, string quais) = OVidenteContraTirosDeTodaDistancia(vidente, atirador, posto);
			AfirmarG12("(defeito injetado: a caixa de 1,5 tile do DM, sem a pressa do tiro) a bola atravessa a caixa entre duas "
					   + $"amostras e quem ve o futuro LEVA o tiro ({total - comDefeito} de {total})",
					   comDefeito < total, "nao esquivou de: " + quais);
		}
		finally { Projetil.AvisoSemPressaDeTeste = false; }
		vidente.Pos = chao + new Vec2(0, -ZoneCollision.TileSize);
		atirador.Pos = chao - new Vec2(4 * ZoneCollision.TileSize, 0);
		_pjProximoCorredor = faixaDaPista;

		// QUEM NAO TEM A SKILL NAO SE MEXE.
		Vec2 chao2 = CorredorLivre(24);
		ServerPlayer cego = ForjarG12("Cego", chao2, bp: 5_000);
		ServerPlayer atirador2 = ForjarG12("Atirador2", chao2 - new Vec2(4 * 32, 0), bp: 5_000, degraus: [(PathKiUnlockedG12, 50)]);
		atirador2.Facing = Facing.East;
		ApertarEOuvir(atirador2, "Basic_Blast");
		int esquivas = _esquivasDePrecognicaoG12;
		TiquesG12(TiquesDeG12(1.5));
		AfirmarG12("quem NAO tem a skill fica onde esta", Vec2.Distance(cego.Pos, chao2) < 0.5 && _esquivasDePrecognicaoG12 == esquivas);

		// O PROPRIO TIRO NAO ASSUSTA.
		Vec2 chao3 = CorredorLivre(24);
		ServerPlayer proprio = ForjarG12("Proprio", chao3, bp: 5_000, skills: [PathDaPrecognicaoG12], degraus: [(PathKiUnlockedG12, 50)]);
		proprio.Facing = Facing.East;
		TiquesG12(TiquesDeG12(1.1));
		ApertarEOuvir(proprio, "Basic_Blast");
		esquivas = _esquivasDePrecognicaoG12;
		TiquesG12(TiquesDeG12(1.0));
		AfirmarG12("o PROPRIO tiro nao dispara a esquiva (`A.proprietor != savant`)", Vec2.Distance(proprio.Pos, chao3) < 0.5 && _esquivasDePrecognicaoG12 == esquivas);
		LimparTudoG12();
	}

	/// <summary>
	/// UMA BOLA COMUM POR DISTANCIA, do atirador a 3 ate 6 tiles de quem ve o futuro, de meio em meio tile: de
	/// quantas ele esquivou, quantas foram, e de quais nao. A bola nao machuca nem empurra (o que se mede e o
	/// PASSO DE LADO, e um corpo arremessado ou nocauteado nao da passo nenhum).
	/// </summary>
	private (int Esquivou, int Tiros, string SemEsquiva) OVidenteContraTirosDeTodaDistancia(ServerPlayer vidente, ServerPlayer atirador, Vec2 posto)
	{
		int esquivou = 0, tiros = 0;
		var semEsquiva = new System.Text.StringBuilder();
		for (float tiles = 3; tiles <= 6.01f; tiles += 0.5f)
		{
			ApagarOsTirosDaZonaG12(vidente);
			vidente.Pos = posto;
			atirador.Pos = posto - new Vec2(tiles * ZoneCollision.TileSize, 0);
			atirador.Facing = Facing.East;

			int antes = _esquivasDePrecognicaoG12;
			Disparar(atirador, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, BaseDano = 0, Velocidade = 1, AlcanceTiles = 12,
				Deflectivel = false, Empurra = false, Nome = "bola de prova",
			});
			TiquesG12(TiquesDeG12(1.0));
			tiros++;
			if (_esquivasDePrecognicaoG12 > antes && Vec2.Distance(vidente.Pos, posto) > ZoneCollision.TileSize / 2f) esquivou++;
			else semEsquiva.Append($"{tiles:0.#} tiles; ");
		}
		ApagarOsTirosDaZonaG12(vidente);
		return (esquivou, tiros, semEsquiva.ToString());
	}

	/// <summary>Apaga todo tiro vivo da zona deste corpo e roda um tique, pra a lista esvaziar de verdade.</summary>
	private void ApagarOsTirosDaZonaG12(ServerPlayer quem)
	{
		foreach (Projetil velho in ProjeteisDaZona(quem.Zone.Hash).ToList())
			if (velho.Vivo) Matar(velho, FimDeProjetil.Apagou);
		TiquesG12(1);
	}

	// =====================================================================
	// 13) O RELOG DERRUBA TUDO
	// =====================================================================
	private void ORelogDerrubaTudoG12()
	{
		GD.Print("[g12] -- 13) O RELOG DERRUBA TUDO");

		ServerPlayer pl = ForjarG12("Relogado", CorredorLivre(24), bp: 50_000, skills: [PathBusterG12, PathGrowSenzuG12, PathSplitformG12]);
		ApertarEOuvir(pl, "BusterBarrage");
		ApertarEOuvir(pl, "Grow_Senzu_Bean");
		ApertarEOuvir(pl, "SplitForm");
		bool tudoLigado = _busterG12.ContainsKey(pl.Id) && _senzuG12.ContainsKey(pl.Id) && _splitformsG12.Values.Any(s => s.Master == pl.Id);
		EsquecerG12(pl.Id);
		AfirmarG12("com barragem, semente e copia de pe, o relog (`EsquecerG12`) apaga os tres",
				   tudoLigado && !_busterG12.ContainsKey(pl.Id) && !_senzuG12.ContainsKey(pl.Id) && !_splitformsG12.Values.Any(s => s.Master == pl.Id));
		AfirmarG12("...e a copia saiu do mundo junto com o dono", !_players.Values.Any(p => p.Name == pl.Name + " Copy"));
		LimparTudoG12();
	}
}
