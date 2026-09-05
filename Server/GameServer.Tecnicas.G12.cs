using Godot;
using Jandirus.Core;
using Jandirus.Core.Ai;
using Jandirus.Core.Appearance;
using Jandirus.Core.Combat;
using Jandirus.Core.Items;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O DISFARCE DA IMITACAO -- o que o mundo VE no lugar da ficha, enquanto o Shapeshifter imita.
///
/// ============================ POR QUE NAO ESCREVER EM `Name`/`Visual` ============================
/// O DM escreve direto: `usr.name = A.name`, `usr.icon = A.icon` (`shapeshifter.dm:91-96`) e guarda
/// o original no proprio obj (`oname`, `imitatoricon`). Aqui `ServerPlayer.Name` e `Visual` vao pro
/// DISCO a cada dois minutos (`Persistir`) -- um disfarce escrito ali sobreviveria ao relog como
/// nome de verdade. E o mesmo desenho do `LookDeFusao`/`NomeDeFusao`, pelo mesmo motivo: o que o
/// mundo le sai do funil (`NomeVisivel`/`VisualVisivel`), a ficha nunca e tocada.
///
/// RACA E GENERO VAO JUNTO porque o corpo desenhado sai da raca (`IconesDeRaca`): copiar so o
/// cabelo e a roupa daria um Shapeshifter com o penteado do outro -- e nao o outro.
/// ==========================================================================================
/// </summary>
public sealed class DisfarceG12
{
	public string Nome = "";
	public string Raca = "";
	public string Genero = "";
	public Appearance Visual = new();
}

/// <summary>
/// LOTE G12 -- OS PROJETEIS QUE FALTAVAM E OS "SISTEMAS PEQUENOS".
///
/// ============================ DE ONDE VEIO ============================
/// O censo de 2026-09-02 (`audit_final.md`, tabela 1, familia F2) listou as skills que JA ESTAO na
/// arvore, que o jogador consegue comprar, e cujo efeito o port nao aplica. Este lote e a fatia
/// "projeteis + sistemas pequenos" dela:
///
///     verbo                       skill / degrau                              fonte no DM
///     --------------------------  ------------------------------------------  ----------------------------------
///     Death_Ball                  `/datum/skill/rank/DeathBall` (kit)         `Ki/blasts/DeathBall.dm:23-113`
///     BusterBarrage               `/datum/skill/rank/BusterBarrage` (kit)     `Ki/blasts/BusterBarrage.dm:26-87`
///     Continuous_Energy_Bullets   Basic_Volley_Mastery nivel 20               `Ki/blasts.dm:263-331`
///     Spin_Blast                  Basic_Volley_Mastery nivel 50               `Ki/blasts.dm:333-402`
///     SpiritBomb                  `/datum/skill/rank/SpiritBomb` (kit)        `Ki/blasts/SpiritBomb.dm:33-180`
///     Soul_Absorb                 `/datum/skill/demon/soulabsorb`             `Race Trees/demon.dm:34-53`
///     Absorb_Android              `/datum/skill/general/androidabsorb`        `Magic/Absorption.dm:17-75`
///     Imitation                   `/datum/skill/general/imitation`            `Race Trees/shapeshifter.dm:66-98`
///     SplitForm                   `/datum/skill/general/splitform`            `Split Forms.dm:76-106`
///     Grow_Senzu_Bean             `/datum/skill/rank/Grow_Senzu` (kit)        `Stamina/Food.dm:2-13` (+ o item :19-48)
///     Ki_Targets                  Ki_Unlocked nivel 50                        `Stats/Training/Meditate.dm:236-289`
///     (precognition)              `/datum/skill/kanassajin/precognition`      `Race Trees/kanassa-jin.dm:38-43` -- SEM verbo: e um `effector()`
///
/// ============================ AS REGRAS DA CASA QUE ESTE ARQUIVO SEGUE ============================
///  * NUMEROS 1:1 com o DM, com arquivo:linha ao lado. Onde o DM tinha um defeito OBVIO, o lote nasceu
///    mantendo-o com nome; em 2026-09-02 o dono mandou consertar os citados (*"corrija esses bugs q vc
///    citou"*) e quatro passaram a fazer o que a descricao promete: a Death Ball cujo dano por estagio
///    era 40/20/30/40 (agora 10/20/30/40); a Spin Blast que "atira em todas as direcoes" e saia toda
///    pra frente (agora os oito rumos); a Spirit Bomb que jogava fora o poder acumulado no disparo
///    (agora leva); o Senzu que curava quem DA em vez de quem recebe (agora cura o caido). A citacao do
///    DM fica em cada um -- e a prova de que o desvio e consciente.
///  * TODO SUSTENTADO DESLIGA: ao apertar de novo, ao acabar o Ki, no nocaute, no relog
///    (<see cref="EsquecerG12"/>) e ao trocar de planeta. Um toggle que nao desliga vira poder de graca.
///  * `sleep(N)` do DM e N/10 s (ver a memoria da unidade de tempo). Todo relogio deste lote conta
///    em SEGUNDOS SIMULADOS (<see cref="_relogioG12"/>, somado do `dt`), e nao em relogio de parede:
///    a bancada anda 60 s de jogo em 1800 tiques sem esperar um minuto, e um servidor com tique
///    atrasado nao muda "1,5 s por estagio" pra outra coisa.
///  * A BOLA QUE AINDA ESTA SENDO FORMADA (Death Ball carregando, Spirit Bomb crescendo, o alvo de
///    treino do Ki Targets) e um <see cref="Projetil"/> comum com <see cref="Projetil.Inerte"/> ligado:
///    nasce pelo `Disparar` (a porta unica), o cliente a desenha, e o tique dos projeteis nao a move
///    nem a faz colidir. Escrever um segundo tipo de entidade pra "bola parada" seria o segundo
///    projetil que o `Projeteis.cs` proibe no cabecalho.
///  * CRESCER E RENASCER. O pacote `Nasceu` leva a escala UMA vez e o cliente ignora um `Nasceu`
///    repetido do mesmo id (`World.AoNascerTiro`). Entao cada estagio da Death Ball e cada pulso da
///    Genkidama MATA a bola (com `FimDeProjetil.Nenhum`, que o cliente apaga sem efeito) e pare outra,
///    maior, no mesmo lugar. E um par de pacotes por estagio -- barato -- e e o unico jeito de a
///    bola crescer NA TELA sem abrir um opcode novo.
/// ==============================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O typepath da skill passiva. Quem a tem no livro desvia de blast sozinho.</summary>
	private const string PathDaPrecognicaoG12 = "/datum/skill/kanassajin/precognition";

	/// <summary>
	/// AS ONZE TECNICAS DESTE LOTE. Chamado do `RegistrarTecnicas` junto dos outros lotes. As
	/// linhas-espelho estao em `Core/Skills/Tecnicas.Portadas.cs` (a `--catalogoteste` cobra).
	/// </summary>
	private void RegistrarTecnicasG12()
	{
		IniciarLote("G12");
		Vivo("Death_Ball", DeathBallG12);
		Vivo("BusterBarrage", BusterBarrageG12);
		Vivo("Continuous_Energy_Bullets", pl => VoleiG12(pl, giro: false));
		Vivo("Spin_Blast", pl => VoleiG12(pl, giro: true));
		Vivo("SpiritBomb", GenkidamaG12);
		Vivo("Soul_Absorb", AbsorverAlmaG12);
		Vivo("Absorb_Android", DrenoDeEnergiaG12);
		Vivo("Imitation", ImitarG12);
		Vivo("SplitForm", DividirOCorpoG12);
		Vivo("Grow_Senzu_Bean", CultivarSenzuG12);
		Vivo("Ki_Targets", AlvosDeKiG12);
	}

	/// <summary>
	/// O RELOGIO DESTE LOTE, em segundos SIMULADOS. Ver o cabecalho: e o `world.time` do BYOND, e a
	/// razao de existir e a bancada -- um `sleep(600)` (60 s) medido em relogio de parede seria
	/// inexercitavel, e o `_relogioDoMundo` da IA ja passou por essa descoberta.
	/// </summary>
	private double _relogioG12;

	/// <summary>
	/// O `blasting` DO DM PRA ESTE LOTE. Quem esta com uma destas tecnicas de pe nao atira outra
	/// coisa -- e `PodeAtirar` (Projeteis.cs) pergunta aqui, pra a recusa valer pra todo tiro do jogo.
	/// </summary>
	private bool BlastingG12(int id) =>
		_deathBallG12.ContainsKey(id) || _busterG12.ContainsKey(id) || _voleiG12.ContainsKey(id)
		|| _genkidamaG12.ContainsKey(id);

	/// <summary>
	/// O `move = 0` / `canmove = 0` DO DM PRA ESTE LOTE: Death Ball (da carga ao fim da guia), as
	/// duas rajadas e a Genkidama (ate 3 s depois do disparo). Entra pelo `PodeMexerOCorpo`, o funil
	/// de VETOR -- o input continua chegando (senao nao daria pra apertar o verb de novo).
	/// </summary>
	private bool PresoPeloG12(int id) =>
		_deathBallG12.ContainsKey(id) || _voleiG12.ContainsKey(id) || _genkidamaG12.ContainsKey(id);

	/// <summary>
	/// O TIQUE DO LOTE, a 30 Hz. Roda ANTES do `TickDosProjeteis`, pra a bola que nasce neste quadro
	/// andar neste quadro. Cada bloco confere `_players` a cada volta (mob-zumbi: o corpo pode ter
	/// sumido no tique anterior).
	/// </summary>
	private void TickG12(double dt)
	{
		_relogioG12 += dt;
		TickDaDeathBallG12();
		TickDoBusterG12();
		TickDoVoleiG12();
		TickDaGenkidamaG12();
		TickDasMortesPendentesG12();
		TickDoDrenoG12();
		TickDosSplitformsG12();
		TickDoSenzuG12(dt);
		TickDosAlvosDeKiG12();
		TickDaPrecognicaoG12(dt);
	}

	/// <summary>
	/// ESTE CORPO FOI EMBORA (relog, desconexao). Tudo que este lote guardava por id morre junto --
	/// id se reusa, e um estado herdado seria um toggle ligado por outra pessoa.
	/// </summary>
	private void EsquecerG12(int id)
	{
		if (_deathBallG12.Remove(id, out EstadoDaDeathBallG12? db) && db.Bola is { Vivo: true }) Matar(db.Bola, FimDeProjetil.Nenhum);
		if (_busterG12.Remove(id)) { }
		_voleiG12.Remove(id);
		if (_genkidamaG12.Remove(id, out EstadoDaGenkidamaG12? g) && g.Bola is { Vivo: true }) Matar(g.Bola, FimDeProjetil.Nenhum);
		foreach (int doador in _ofertasDeGenkidamaG12.Keys.ToList())
			if (doador == id || _ofertasDeGenkidamaG12[doador] == id) _ofertasDeGenkidamaG12.Remove(doador);
		_absorvendoAteG12.Remove(id);
		_drenoG12.Remove(id);
		foreach (int quem in _drenoG12.Keys.ToList()) if (_drenoG12[quem].Alvo == id) _drenoG12.Remove(quem);
		_mortesPendentesG12.RemoveAll(m => m.Vitima == id || m.Algoz == id);
		_senzuG12.Remove(id);
		_senzuNoCorpoG12.Remove(id);
		if (_alvosDeKiG12.Remove(id, out EstadoDosAlvosDeKiG12? ak))
			foreach (Projetil t in ak.Alvos) if (t.Vivo) Matar(t, FimDeProjetil.Apagou);
		// AS COPIAS MORREM COM O DONO: `if(isnull(master)) del(src)` (`Split Forms.dm:22`).
		foreach (int copia in _splitformsG12.Keys.ToList())
			if (_splitformsG12[copia].Master == id) DesfazerSplitformG12(copia);
		_splitformsG12.Remove(id);
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Disfarce = null;
	}

	/// <summary>Os verbos de menu do lote (aba Other do cliente) -- as ordens das copias e a resposta a Genkidama.</summary>
	private bool ComandoG12(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "splitform_seguir": OrdenarSplitformsG12(pl, FuncaoDeSplitformG12.Seguir); return true;
			case "splitform_parar": OrdenarSplitformsG12(pl, FuncaoDeSplitformG12.Parado); return true;
			case "splitform_alvo": OrdenarSplitformsG12(pl, FuncaoDeSplitformG12.AtacarAlvo); return true;
			case "splitform_perto": OrdenarSplitformsG12(pl, FuncaoDeSplitformG12.AtacarPerto); return true;
			case "splitform_destruir": DestruirSplitformsG12(pl); return true;
			case "genkidama_doar": ResponderGenkidamaG12(pl, aceitou: true); return true;
			case "genkidama_negar": ResponderGenkidamaG12(pl, aceitou: false); return true;
		}
		return false;
	}

	// =====================================================================
	// 1. DEATH BALL -- `Ki/blasts/DeathBall.dm:23-113`
	// =====================================================================
	private sealed class EstadoDaDeathBallG12
	{
		public Projetil? Bola;
		/// <summary>O `movestrength`: 1 ao nascer, ate 4.</summary>
		public int Estagio = 1;
		/// <summary>O `KiReq` VIVO: comeca em 150*BaseDrain e sobe `movestrength*10` (flat) por estagio.</summary>
		public double KiReq;
		public double ProximoEstagioEm;
		public bool Carregando = true;
		/// <summary>O `usr.Guiding`: nasce LIGADO junto com a carga (`:34`).</summary>
		public bool Guiando = true;
		public bool Lancada;
		public double ProximoPulsoDeGuiaEm;
		public Vec2 Ancora;
		public ulong Zona;
	}

	private readonly Dictionary<int, EstadoDaDeathBallG12> _deathBallG12 = [];

	/// <summary>`sleep(15)` por estagio (`DeathBall.dm:75`).</summary>
	private const double SegundosPorEstagioDaDeathBallG12 = 1.5;

	/// <summary>
	/// A DEATH BALL. Tres apertos, tres significados -- e a ordem dos `if` do verb decide qual:
	///
	///     if(usr.Guiding)  Guiding = 0            // 1o aperto DEPOIS de comecar: larga a guia
	///     else if(charging) charging = 0          // so alcancavel com a guia ja largada: sai na hora
	///     else if(!med && !train) ...comeca       // o primeiro
	///
	/// Repare que `Guiding = 1` e escrito NO COMECO da carga (`:34`), entao apertar durante a carga
	/// desliga a GUIA (a bola vai sair e ficar parada), e so um terceiro aperto encurta a carga.
	/// Nao e bonito e e o do DM.
	///
	/// O CUSTO: `150*BaseDrain` na entrada, e a cada estagio `Ki -= KiReq/3` com `KiReq += movestrength*10`
	/// (dez de Ki FLAT por estagio, sem BaseDrain -- literal, `:77-78`). O Ki vai a zero e nao abaixo:
	/// o DM deixa ficar negativo, e Ki negativo aqui quebraria a razao de carga (`kiratio`).
	///
	/// ============================ O DANO POR ESTAGIO: O DEFEITO DO DM, CONSERTADO ============================
	/// A bola nascia com `basedamage = 40` (`:62`) e cada estagio escrevia `basedamage = 10*movestrength`
	/// (`:80`): o estagio 2 valia 20, o 3 valia 30 e so o 4 voltava aos 40 com que ela nasceu --
	/// carregar ate o fim rendia o MESMO que soltar na hora, com o dobro do tamanho. A descricao promete
	/// "ate quatro vezes a forca"; por decisao do dono (2026-09-02, "corrija esses bugs q vc citou") o
	/// dano CRESCE com a carga: `10*movestrength` em todo estagio, 10/20/30/40 -- a linha `:80` vale
	/// desde o nascimento.
	///
	/// A GUIA: `A.dir = usr.dir` e um `step(A, A.dir)` a cada `Eactspeed/5` tiques (`:93-102`) -- a bola
	/// anda UM tile por pulso, no rumo do seu olhar, enquanto `Guiding`. Nao ha `walk()` (esta
	/// comentado, `:92`): largar a guia PARA a bola onde ela esta, e `A.Burnout()` a apaga 5 s depois.
	/// O `blasthoming(target)` (`:91`) e letra morta: `homingchance = 0` (`:64`) e o `prob(0)` nunca
	/// da um passo.
	/// =========================================================================================================
	/// </summary>
	private void DeathBallG12(ServerPlayer pl)
	{
		if (_deathBallG12.TryGetValue(pl.Id, out EstadoDaDeathBallG12? db))
		{
			if (db.Guiando)
			{
				db.Guiando = false;
				Avisar(pl, db.Lancada ? "voce solta a guia da Death Ball." : "voce larga a guia: a esfera vai sair e ficar onde cair.");
				if (db.Lancada) EncerrarDeathBallG12(pl, db);
				return;
			}
			if (db.Carregando)
			{
				db.Carregando = false;   // o terceiro aperto: sai com o estagio de agora
				Avisar(pl, "voce interrompe a carga.");
				return;
			}
			Avisar(pl, "a Death Ball ja saiu da sua mao.");
			return;
		}

		Fighter f = pl.Ficha;
		if (f.med || f.train) { Avisar(pl, "nao da meditando ou treinando."); return; }
		double kiReq = 150 * f.BaseDrain();
		if (!PodeAtirar(pl, kiReq, out string porque)) { Avisar(pl, porque); return; }

		// `!basicCD && canfight` -- a recarga da bola basica e a mesma (`basicCD += 15`, `:36`).
		if (EmEspera(pl, _blastPronto, "sua mao ainda esta juntando energia")) return;
		long agora = NowMs();
		if (pl.Combate == null || !pl.Combate.PodeAtacar()) { Avisar(pl, "voce nao esta em condicoes de lutar."); return; }
		_blastPronto[pl.Id] = agora + 1500;

		f.Ki -= kiReq;
		CreditarContador(pl, "blastcounter", 1);   // `usr.blastcounter++` (`:41`)

		var estado = new EstadoDaDeathBallG12
		{
			KiReq = kiReq,
			ProximoEstagioEm = _relogioG12 + SegundosPorEstagioDaDeathBallG12,
			Ancora = pl.Pos,
			Zona = pl.Zone.Hash,
		};
		_deathBallG12[pl.Id] = estado;
		estado.Bola = NascerDeathBallG12(pl, estado);
		if (estado.Bola == null)
		{
			_deathBallG12.Remove(pl.Id);
			Avisar(pl, "nao ha espaco pra mais energia solta aqui.");
			return;
		}
		pl.Moving = false;
		Avisar(pl, "voce ergue as maos e uma esfera de morte comeca a se formar sobre a sua cabeca. "
				   + "Cada 1,5 s ela cresce um estagio (ate quatro); aperte de novo pra largar a guia.");
		Falar(pl, Protocol.Fala.Emote, "ergue uma esfera escura sobre a cabeca!");
		GD.Print($"[server] {pl.Name} comecou a carregar a Death Ball (custo {kiReq:0})");
	}

	/// <summary>
	/// A BOLA (RE)NASCE no estagio de agora: `basedamage` e escala do `:80-82`, o `mods` de `:65`
	/// dobrado pra dentro do `BaseDano` (o `Disparar` ja poe `Ekioff*Ekiskill`; os dois logaritmos
	/// entram aqui -- o produto e o mesmo numero em toda a cadeia). `loc = (x, y+1)`: um tile ao
	/// NORTE, que no eixo do port e `y - 32`.
	/// </summary>
	private Projetil? NascerDeathBallG12(ServerPlayer pl, EstadoDaDeathBallG12 db)
	{
		Fighter f = pl.Ficha;
		double baseDano = 10 * db.Estagio   // `:80` em todo estagio (o `:62` dava 40 ao nascer -- ver o cabecalho)
						  * DanoDeKi.Log10Min(f.kieffusionskill, 2) * DanoDeKi.Log10Min(f.blastskill, 2);
		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = baseDano,
			Velocidade = 1,
			AlcanceTiles = 1000,          // ela anda por `step()`, sem `distance`: quem a limita e o prazo
			Nome = "Death Ball",
			EscalaVisual = 1 + db.Estagio / 4.0,   // `nA.Scale(1+movestrength/4, ...)` (`:81`); o estagio 1 e a matriz identidade
		}, rumoDado: Vec2.Zero, deOnde: pl.Pos + new Vec2(0, -ZoneCollision.TileSize), verbo: "Death_Ball");
		if (!p.Vivo) return null;
		p.Inerte = true;
		p.VidaRestante = 3600;   // enquanto carrega ela nao envelhece de verdade; o prazo real chega no disparo
		return p;
	}

	private void TickDaDeathBallG12()
	{
		foreach ((int id, EstadoDaDeathBallG12 db, ServerPlayer pl) in Varrer(_deathBallG12))
		{
			Fighter f = pl.Ficha;

			// CAIR, MORRER, MEDITAR OU TROCAR DE PLANETA DESFAZ TUDO. O DM nao trata nenhum dos quatro
			// (a carga continua num corpo caido); aqui a regra da casa e "sustentado cai com o corpo".
			if (f.dead || f.KO || f.med || f.train || pl.Zone.Hash != db.Zona || db.Bola is not { Vivo: true })
			{
				if (db.Bola is { Vivo: true } && !db.Lancada) Matar(db.Bola, FimDeProjetil.Nenhum);
				if (db.Bola is { Vivo: true } && db.Lancada) { /* a bola solta continua; so a guia acaba */ }
				EncerrarDeathBallG12(pl, db, aviso: db.Lancada ? null : "a esfera de morte se dispersa.");
				continue;
			}

			if (!db.Lancada)
			{
				// A ANCORA: `usr.move = 0` (`:33`). Qualquer passo que o cliente tenha dado e desfeito.
				Ancorar(pl, db.Ancora);

				if (db.Carregando && db.Estagio < 4 && _relogioG12 >= db.ProximoEstagioEm)
				{
					// `while(charging && A && movestrength < 4) { sleep(15); movestrength++; ... }` (`:74-83`)
					db.Estagio++;
					f.Ki = Math.Max(f.Ki - db.KiReq / 3, 0);        // `usr.Ki -= KiReq/3` (`:77`), sem deixar negativo
					db.KiReq += db.Estagio * 10;                    // `KiReq += movestrength*10` (`:78`) -- dez FLAT
					CreditarContador(pl, "blastcounter", 1);        // `usr.blastcounter++` (`:79`)
					db.ProximoEstagioEm = _relogioG12 + SegundosPorEstagioDaDeathBallG12;

					// CRESCER E RENASCER -- ver o cabecalho do arquivo.
					if (db.Bola is { Vivo: true }) Matar(db.Bola, FimDeProjetil.Nenhum);
					db.Bola = NascerDeathBallG12(pl, db);
					if (db.Bola == null) { EncerrarDeathBallG12(pl, db, aviso: "nao ha espaco pra mais energia solta aqui."); continue; }
					Avisar(pl, $"a esfera cresce: estagio {db.Estagio} de 4.");
					continue;
				}
				if (db.Carregando && db.Estagio < 4) continue;

				// A CARGA FECHOU (quatro estagios, ou o terceiro aperto): LANCAR (`:84-92`).
				LancarDeathBallG12(pl, db);
				continue;
			}

			// LANCADA E GUIADA: `spawn while(A && usr.Guiding) { A.dir = usr.dir; sleep(Eactspeed/5) }`
			// e `while(A && A.loc && usr.Guiding) { sleep(Eactspeed/5); Blast_Gain(); blastcounter+=3;
			// guidedcounter+=3; step(A, A.dir) }` (`:93-102`).
			if (!db.Guiando) { EncerrarDeathBallG12(pl, db); continue; }
			if (_relogioG12 < db.ProximoPulsoDeGuiaEm) continue;
			db.ProximoPulsoDeGuiaEm = _relogioG12 + Math.Max(f.Eactspeed / 5, 1) * TempoDoDm.SegundosPorTique;
			db.Bola.Rumo = MeleeArea.Frente(pl.Facing);
			f.BlastGain(_rng);
			CreditarContador(pl, "blastcounter", 3);
			CreditarContador(pl, "guidedcounter", 3);
		}
	}

	/// <summary>
	/// O DISPARO (`DeathBall.dm:84-92`): a bola vai pra boca do cano, ganha dois prazos
	/// (`spawn(Ekioff*Ekiskill*40) del(A)` e `Burnout(1200)` -- vale o menor) e, se a guia esta de pe,
	/// passa a andar um tile a cada `Eactspeed/5` tiques no rumo do olhar. Sem guia ela fica parada
	/// onde nasceu e o `Burnout()` de 5 s a apaga.
	/// </summary>
	private void LancarDeathBallG12(ServerPlayer pl, EstadoDaDeathBallG12 db)
	{
		Fighter f = pl.Ficha;
		Projetil p = db.Bola!;
		db.Lancada = true;
		db.Carregando = false;
		p.Inerte = false;
		p.VidaRestante = Math.Min(f.Ekioff * f.Ekiskill * 4.0, 120);   // `*40` tiques = *4 s; `Burnout(1200)` = 120 s
		Vec2 frente = MeleeArea.Frente(pl.Facing);
		p.Pos = BocaDeCano.De(pl.Pos, frente);
		p.Cauda = p.Pos;
		p.SegundosPorTile = Math.Max(f.Eactspeed / 5, 1) * TempoDoDm.SegundosPorTique;   // um `step()` por pulso de `Eactspeed/5` tiques
		Falar(pl, Protocol.Fala.Diz, "Death Ball!!");
		if (db.Guiando)
		{
			p.Rumo = frente;
			db.ProximoPulsoDeGuiaEm = _relogioG12 + p.SegundosPorTile;
			Avisar(pl, "a esfera sai e OBEDECE ao seu olhar. Aperte de novo pra solta-la.");
		}
		else
		{
			p.Rumo = Vec2.Zero;
			EncerrarDeathBallG12(pl, db, aviso: "sem guia, a esfera fica onde nasceu e se dispersa em cinco segundos.");
		}
	}

	/// <summary>
	/// O FIM DA GUIA (`DeathBall.dm:103-113`): `Guiding = 0; A.Burnout(); 4x Blast_Gain(); blasting = 0;
	/// move = 1`. A bola solta PARA (nao ha `walk`) e o `Burnout()` de 5 s a apaga -- o menor dos prazos vence.
	/// </summary>
	private void EncerrarDeathBallG12(ServerPlayer pl, EstadoDaDeathBallG12 db, string? aviso = null)
	{
		_deathBallG12.Remove(pl.Id);
		if (db.Bola is { Vivo: true } && db.Lancada)
		{
			db.Bola.Rumo = Vec2.Zero;
			db.Bola.VidaRestante = Math.Min(db.Bola.VidaRestante, Projetil.SegundosDeBurnout);
		}
		if (db.Lancada) for (int i = 0; i < 4; i++) pl.Ficha.BlastGain(_rng);   // `:105-108`
		if (aviso != null) Avisar(pl, aviso);
	}

	// =====================================================================
	// 2. BUSTER BARRAGE -- `Ki/blasts/BusterBarrage.dm:26-87`
	// =====================================================================
	private sealed class BusterG12
	{
		public double ProximoEm;
		/// <summary>0 = a esfera A (depois `sleep(Eactspeed/4)`), 1 = a esfera B (depois `sleep(Eactspeed/2)`).</summary>
		public int Fase;
		public bool AvisouComoParar;
		public ulong Zona;
		/// <summary>Quantas esferas ja saíram e em quantos dos oito rumos -- o instrumento da bancada (as esferas morrem na parede antes de serem contadas).</summary>
		public int Cuspidas;
		public readonly HashSet<int> RumosVistos = [];
	}

	private readonly Dictionary<int, BusterG12> _busterG12 = [];

	/// <summary>
	/// O BUSTER BARRAGE -- um toggle. Ligado, cada ciclo cobra `1*BaseDrain` (`:38`) e cospe DUAS
	/// esferas em rumos sorteados entre os oito (`:47-55` e `:61-69`), com `walk(A, dir, rand(1,2))`
	/// (um ou dois tiques por tile) e um prazo de `kiskill*50*kioff` tiques (`:57`, `:75`). Entre a
	/// primeira e a segunda, `sleep(Eactspeed/4)`; depois da segunda, `sleep(Eactspeed/2)`.
	///
	/// A ESFERA E A DO `Create_Blast()` (`Ki/tools/copypaste.dm:2-37`): `basedamage = (0.5+Ekioff)*Ephysoff`
	/// -- sim, a ofensiva FISICA multiplica um tiro de ki; e literal e fica --, `mods = Ekioff*Ekiskill`,
	/// sem `Burnout` (o prazo e o `del` do verb) e sem `distance` que a limite. O `A.x/y += rand(-1,1)`
	/// de `:45-46` (o tremor de 0,3 s depois de nascer) entra como o berco da esfera.
	///
	/// A CONDICAO DE CADA CICLO E A DO DM (`:37`): `Ki >= 1 && !KO && !med && !train`. Ficar sem Ki, cair,
	/// sentar pra meditar ou trocar de planeta DESLIGA -- e apertar de novo tambem (`:83-87`).
	/// </summary>
	private void BusterBarrageG12(ServerPlayer pl)
	{
		if (_busterG12.Remove(pl.Id))
		{
			MandarEfeito(pl, "BusterBarrage", 0);
			Avisar(pl, "voce fecha as maos e a barragem para.");
			return;
		}
		// `if(usr.Ki>=1 && !usr.KO && !usr.med && !usr.train && !usr.blasting)`: e o `PodeAtirar`, com 1 de Ki
		if (!PodeAtirar(pl, 1, out string porque)) { Avisar(pl, porque); return; }

		_busterG12[pl.Id] = new BusterG12 { ProximoEm = _relogioG12, Zona = pl.Zone.Hash };
		MandarEfeito(pl, "BusterBarrage", -1);   // o `USEDUNDERLAY` (`Brolly1.dmi` tingido, `:32-35`)
		Falar(pl, Protocol.Fala.Emote, "acende a aura e comeca a cuspir esferas de energia em todas as direcoes!");
	}

	private void TickDoBusterG12()
	{
		foreach ((int id, BusterG12 b, ServerPlayer pl) in Varrer(_busterG12))
		{
			if (_relogioG12 < b.ProximoEm) continue;
			Fighter f = pl.Ficha;

			// `if(usr.Ki>=1&&!usr.KO&&!usr.med&&!usr.train) ... else { desliga }` (`:37` e `:78-82`).
			if (f.Ki < 1 || f.KO || f.dead || f.med || f.train || pl.Zone.Hash != b.Zona)
			{
				_busterG12.Remove(id);
				MandarEfeito(pl, "BusterBarrage", 0);
				Avisar(pl, f.Ki < 1 ? "sua energia acaba e a barragem morre." : "a barragem para.");
				continue;
			}

			if (b.Fase == 0)
			{
				f.Ki -= 1 * f.BaseDrain();   // `usr.Ki -= 1*BaseDrain` (`:38`), UMA vez por ciclo
				if (!b.AvisouComoParar) { b.AvisouComoParar = true; Avisar(pl, "aperte de novo pra parar de atirar."); }
			}
			CuspirEsferaDoBusterG12(pl, b);
			f.BlastGain(_rng);   // `usr.Blast_Gain()` depois de cada esfera (`:58`, `:76`)

			// `sleep(usr.Eactspeed/4)` depois da A, `sleep(usr.Eactspeed/2)` depois da B (`:59`, `:77`).
			double tiques = b.Fase == 0 ? f.Eactspeed / 4 : f.Eactspeed / 2;
			b.ProximoEm = _relogioG12 + Math.Max(tiques, 1) * TempoDoDm.SegundosPorTique;
			b.Fase = 1 - b.Fase;
		}
	}

	/// <summary>Uma esfera do Buster Barrage: rumo sorteado nos oito, berco tremido, um ou dois tiques por tile.</summary>
	private void CuspirEsferaDoBusterG12(ServerPlayer pl, BusterG12 b)
	{
		Fighter f = pl.Ficha;
		int qual = _rng.Next(MoveRules.OitoRumos.Length);
		Vec2 rumo = MoveRules.OitoRumos[qual];
		b.Cuspidas++;
		b.RumosVistos.Add(qual);
		Vec2 berco = pl.Pos + new Vec2((_rng.Next(3) - 1) * ZoneCollision.TileSize, (_rng.Next(3) - 1) * ZoneCollision.TileSize);
		int lag = _rng.Next(1, 3);   // `rand(1,2)`
		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = (0.5 + f.Ekioff) * f.Ephysoff,   // `Create_Blast`: `basedamage = 0.5+Ekioff`, `A.basedamage = basedamage*Ephysoff`
			Velocidade = lag == 1 ? 3 : 2,              // `AtrasoDeBola`: 3 -> 1 tique/tile, 2 -> 2 tiques/tile
			AlcanceTiles = 1000,                        // sem `distance`: quem a apaga e o `del` abaixo
			Nome = "esfera do Buster Barrage",
		}, rumoDado: rumo, deOnde: berco, verbo: "BusterBarrage");
		if (p.Vivo) p.VidaRestante = Math.Max(f.kiskill * 5.0 * f.kioff, 0.5);   // `spawn(kiskill*50*kioff) del(A)`
	}

	// =====================================================================
	// 3. AS DUAS RAJADAS -- `Ki/blasts.dm:263-331` e `:333-402`
	// =====================================================================
	private sealed class EstadoDoVoleiG12
	{
		public bool Giro;
		/// <summary>O `kireq` VIVO: 30 (ou 50) x BaseDrain na entrada; `max(ln(duration),1)*300 (ou 500)*BaseDrain` depois.</summary>
		public double KiReq;
		public int Duracao;
		public double ProximoEm;
		public bool Desligando;
		public Vec2 Ancora;
		public ulong Zona;
		/// <summary>Em quantos dos oito rumos a Giratoria ja cuspiu -- o instrumento da bancada (as esferas morrem na parede antes de serem contadas).</summary>
		public readonly HashSet<int> RumosVistos = [];
	}

	private readonly Dictionary<int, EstadoDoVoleiG12> _voleiG12 = [];

	/// <summary>
	/// BALAS CONTINUAS (`blasts.dm:263`) e RAJADA GIRATORIA (`:333`): o mesmo verb com numeros trocados,
	/// e por isso um metodo so:
	///
	///                      entrada        por esfera depois            dano     cadencia           espera
	///     Continuous       30*BaseDrain   max(ln(n),1)*300*BaseDrain   0,7x     1 por 0,1 s        50 tiques = 5 s
	///     Spin_Blast       50*BaseDrain   max(ln(n),1)*500*BaseDrain   1,1x     2 por 0,1 s        80 tiques = 8 s
	///
	/// `ln` e o `log(x)` de um argumento so do BYOND (natural), como nas barragens do G5.
	///
	/// O LACO: `while(volleying && !KO && Ki > kireq)`; a cada volta `Ki -= kireq; if(Ki < kireq) Ki = 0`
	/// (sim: se depois de pagar sobrar menos que a proxima, o DM ZERA o Ki -- literal, `:286-287`),
	/// nasce a esfera, `duration++`, e o `kireq` e recalculado. Com a segunda esfera custando dez vezes
	/// a entrada, a rajada de um personagem comum dura um punhado de esferas -- e o do DM.
	///
	/// ============================ O QUE MUDOU, E POR QUE ============================
	///  * `walk(A, curdir)` (`:314`) usa o `curdir` do MOB -- a tecla de direcao SEGURADA
	///    (`movement handler.dm:191`), que continua sendo lida com `canmove = 0`; sem tecla, `curdir = 0`
	///    e a esfera nasce parada no proprio tile. Aqui o input de um corpo plantado devolve antes de
	///    escrever o olhar (`Input`, o ramo do `PodeMexerOCorpo`), entao NAO da pra mirar segurando a
	///    tecla: a rajada sai no olhar que voce tinha ao apertar. Fica anotado.
	///  * `step(A, pick(turn(curdir,-45), curdir, turn(curdir,45)))` (`:313`): a esfera nasce um tile a
	///    frente e mais um num leque de +-45 graus, e SO ENTAO voa reta. E o que faz a rajada ser um
	///    feixe largo e nao um fio -- e esta portado.
	///  * `blasthoming` com `homingchance = min(Ekiskill*kicontrolskill*homingskill/100, 100)` (`:294`,
	///    `:311-312`): um passo pro alvo com essa chance a cada 0,8 s. Nao portado, pelo mesmo motivo das
	///    barragens do G5 (o projetil do port nao tem "chance de caca"); a esfera voa reta.
	///  * `A.inaccuracy` / `BlastControl` (o tremor): idem, nao portado.
	///
	/// ============================ A RAJADA GIRATORIA AGORA GIRA (o DM nao girava) ============================
	/// `step_rand(A); A.dir = get_step_rand(A); walk(A, dir)` (`:382-384`). O `get_step_rand` devolve
	/// um TURF (nao uma direcao) e o `walk` usa `dir` -- que dentro de um verb do mob e o `dir` DO MOB.
	/// Resultado no DM: a esfera nascia num tile adjacente sorteado e voava TODA no rumo do olhar do
	/// atirador. A descricao promete "em todas as direcoes" ("Fire an endless barrage of energy blasts
	/// in all directions", `:335`); por decisao do dono (2026-09-02, "corrija esses bugs q vc citou")
	/// cada esfera sorteia UM dos oito rumos e nasce no tile adjacente DESSE rumo -- o `step_rand` e o
	/// `walk` que a linha claramente quis.
	/// `canfight = 0` (`:341`) tambem nao entrou: o port nao tem um bit de "nao pode socar" por
	/// tecnica, e criar um so pra isto seria a segunda porta de ataque.
	/// ===================================================================================================
	/// </summary>
	private void VoleiG12(ServerPlayer pl, bool giro)
	{
		if (_voleiG12.TryGetValue(pl.Id, out EstadoDoVoleiG12? v))
		{
			// `else if(volleying) usr.volleying = 0` (`:328-329`): apertar de novo desliga -- qualquer das duas.
			v.Desligando = true;
			Avisar(pl, "voce interrompe a rajada.");
			return;
		}
		Fighter f = pl.Ficha;
		double kireq = (giro ? 50 : 30) * f.BaseDrain();
		if (!PodeAtirar(pl, kireq, out string porque)) { Avisar(pl, porque); return; }
		if (EmEspera(pl, _volleyPronto, "suas maos ainda estao quentes da ultima barragem")) return;
		if (pl.Combate == null || !pl.Combate.PodeAtacar()) { Avisar(pl, "voce nao esta em condicoes de lutar."); return; }

		// `canmove = 0; barrageCD = 1; Ki -= kireq; Blast_Gain(); blasting = 1; volleying = 1` (`:270-276`).
		_volleyPronto[pl.Id] = NowMs() + 3_600_000;   // "ocupado" enquanto durar; o prazo real e escrito no fim
		f.Ki -= kireq;
		f.BlastGain(_rng);
		_voleiG12[pl.Id] = new EstadoDoVoleiG12
		{
			Giro = giro, KiReq = kireq, ProximoEm = _relogioG12, Ancora = pl.Pos, Zona = pl.Zone.Hash,
		};
		pl.Moving = false;
		Avisar(pl, giro ? "voce planta os pes e comeca a expelir esferas em volta de si!"
						: "voce planta os pes e comeca a cuspir uma rajada continua de esferas!");
	}

	private void TickDoVoleiG12()
	{
		foreach ((int id, EstadoDoVoleiG12 v, ServerPlayer pl) in Varrer(_voleiG12))
		{
			Fighter f = pl.Ficha;

			// `while(volleying && !usr.KO && usr.Ki > kireq)` (`:280`) -- mais o relog/planeta da casa.
			if (v.Desligando || f.KO || f.dead || f.Ki <= v.KiReq || pl.Zone.Hash != v.Zona)
			{
				// `reload = 50 (80); barrageCD = reload; canmove = 1; blasting = 0; volleying = 0` (`:318-327`).
				_voleiG12.Remove(id);
				_volleyPronto[id] = NowMs() + (v.Giro ? 8000 : 5000);
				Avisar(pl, f.Ki <= v.KiReq && !v.Desligando ? "sua energia nao paga a proxima esfera: a rajada morre." : "a rajada acaba.");
				continue;
			}

			Ancorar(pl, v.Ancora);
			if (_relogioG12 < v.ProximoEm) continue;

			// A GIRATORIA cospe DUAS por tique de 0,1 s (`if(duration % 2 == 0) sleep(1)`, `:387-388`).
			int porVolta = v.Giro ? 2 : 1;
			for (int i = 0; i < porVolta; i++)
			{
				if (f.Ki <= v.KiReq) break;
				if (v.Duracao % 3 == 0)   // `:281-284` e `:351-354`
				{
					f.BlastGain(_rng);
					CreditarContador(pl, "blastcounter", 1);
					CreditarContador(pl, "volleycounter", 1);
				}
				f.Ki -= v.KiReq;
				if (f.Ki < v.KiReq) f.Ki = 0;   // `:286-287` -- literal
				CuspirEsferaDoVoleiG12(pl, v);
				v.Duracao++;
				v.KiReq = Math.Max(Math.Log(v.Duracao), 1) * (v.Giro ? 500 : 300) * f.BaseDrain();   // `:316`, `:386`
			}
			v.ProximoEm = _relogioG12 + TempoDoDm.SegundosPorTique;
		}
	}

	/// <summary>Uma esfera da rajada: dano de `:293`/`:363`, mods de `:296`/`:366` dobrados no `BaseDano`, berco e rumo do verb.</summary>
	private void CuspirEsferaDoVoleiG12(ServerPlayer pl, EstadoDoVoleiG12 v)
	{
		Fighter f = pl.Ficha;
		Vec2 rumo = MeleeArea.Frente(pl.Facing);
		Vec2 berco;
		if (v.Giro)
		{
			// `step_rand(A); A.dir = get_step_rand(A); walk(A, dir)` (`:382-384`) como a linha quis: um dos
			// oito rumos sorteado, e a esfera nasce no tile adjacente DESSE rumo (ver o cabecalho).
			int qual = _rng.Next(MoveRules.OitoRumos.Length);
			rumo = MoveRules.OitoRumos[qual];
			v.RumosVistos.Add(qual);
			berco = pl.Pos + rumo * ZoneCollision.TileSize;
		}
		else
		{
			// `step(A, usr.dir)` + `step(A, pick(turn(curdir,-45), curdir, turn(curdir,45)))`.
			// `turn(curdir, +-45)` e uma DIAGONAL, e o `step()` anda um tile inteiro nos dois eixos
			// (dx = 1, dy = +-1): o vetor vai SEM normalizar de proposito.
			Vec2 leque = _rng.Next(3) switch
			{
				0 => rumo + rumo.Girado90(),
				1 => rumo,
				_ => rumo - rumo.Girado90(),
			};
			berco = pl.Pos + rumo * ZoneCollision.TileSize + leque * ZoneCollision.TileSize;
		}
		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = (v.Giro ? 1.1 : 0.7) * f.Ekioff * DanoDeKi.Log10Min(f.blastskill, 10) * DanoDeKi.DanoGlobalDeKi
					   * DanoDeKi.Log10Min(f.kieffusionskill, 2) * DanoDeKi.Log10Min(f.blastskill, 2),
			Velocidade = 3,       // `walk(A, dir)` sem lag: um tile por tique
			AlcanceTiles = 30,    // o `distance = 30` de todo `/obj/attack/blast` (`objects.dm:17-18`)
			Nome = v.Giro ? "esfera da Rajada Giratoria" : "bala de energia",
		}, rumoDado: rumo, deOnde: berco, verbo: v.Giro ? "Spin_Blast" : "Continuous_Energy_Bullets");
	}

	// =====================================================================
	// 4. A GENKIDAMA (SPIRIT BOMB) -- `Ki/blasts/SpiritBomb.dm:33-180`
	// =====================================================================
	private sealed class EstadoDaGenkidamaG12
	{
		public Projetil? Bola;
		/// <summary>O `prevscale`: 1 ao nascer, +0,1 por pulso e por doacao.</summary>
		public double Escala = 1;
		/// <summary>O `A.BP` VIVO durante a espera: cresce 1% por pulso e recebe as doacoes. Ver o disparo.</summary>
		public double BpAcumulado;
		/// <summary>0 = formando (3 s); 1 = crescendo/segurando; 2 = disparo pedido (2 s); 3 = lancada (3 s de trava).</summary>
		public int Fase;
		public double FaseAte;
		/// <summary>O `presetiterations` (3 -> 0) e o `SpiritsGivenPower` da volta atual.</summary>
		public int Iteracoes = 3;
		public int PulsosRestantes;
		public double ProximoPulsoEm;
		/// <summary>A espera de 110 s entre a primeira volta e a segunda (`sleep(1100)`, `:130`).</summary>
		public double EsperaAte;
		public bool AvisouUniverso;
		/// <summary>O `PeopleGivenPower`: doacoes ainda nao convertidas em +0,1 de escala.</summary>
		public int DoacoesPendentes;
		public double ProximaDoacaoEm;
		public int Doadores;
		public Vec2 Ancora;
		public ulong Zona;
	}

	private readonly Dictionary<int, EstadoDaGenkidamaG12> _genkidamaG12 = [];

	/// <summary>Quem esta sendo convidado a doar -> quem esta formando a bomba.</summary>
	private readonly Dictionary<int, int> _ofertasDeGenkidamaG12 = [];

	/// <summary>
	/// A GENKIDAMA. O verb inteiro, na ordem dele:
	///
	///  1. `cost = MaxKi*0.9`; `!KO && !med && !train && !blasting`; `Ki >= cost` senao "You need 90% Energy" (`:35-37`, `:180`).
	///  2. `Ki -= cost; blasting = 1; move = 0`; `baseKi += kicapcheck(0.05*BPrestriction*KiMod)` (`:38-44`).
	///  3. A bola nasce em `(x, y+1)` (um tile ao norte) e FORMA por `sleep(30)` = 3 s (`:53`, `:60`).
	///  4. `3x Blast_Gain(); deflectable = 0; shockwave = 1; basedamage = 30; BP = expressedBP; mods = Ekioff*Ekiskill` (`:61-69`).
	///  5. QUEM MEDITA no mesmo mundo recebe a pergunta "Give power? It'll drain 10% of your Ki" (`:78-81`).
	///     Quem diz sim: `A.BP += M.expressedBP*(M.Ki*0.1); M.Ki *= 0.9` (`:85-86`) e, 2,5 s depois, `+0,1`
	///     de escala (`:132-147`). Aqui a pergunta e um aviso e a resposta e um verb da aba Other
	///     (`genkidama_doar`/`genkidama_negar`) -- o mesmo desenho da oferta de juventude do G8.
	///  6. O CRESCIMENTO (`:105-131`): tres voltas. `SpiritsGivenPower` comeca em 10 e ganha +10 por volta;
	///     cada pulso (a cada 1,5 s) da `+0,1` de escala e `A.BP += A.BP*0.01`. Volta 1 = 20 pulsos (30 s);
	///     depois `sleep(1100)` = 110 s e a frase do "universo inteiro" (`:130-131`); volta 2 = 10 pulsos;
	///     volta 3 = 10 pulsos. **A frase do "sistema solar" (`:126-128`) e INALCANCAVEL**: o `switch` le
	///     `presetiterations` DEPOIS do decremento, e ele nunca e 3 ali. Fica como esta.
	///  7. "Fire it when ready!" -- aqui, apertar o verb de novo. `sleep(20)` = 2 s, e a bola sai
	///     (`:169-173`): `A.loc = get_step(usr, usr.dir); walk(A, usr.dir)` (sem lag: um tile por tique),
	///     `Burnout(1000)` = 100 s. `sleep(30)` = 3 s depois: `blasting = 0; move = 1`.
	///
	/// ============================ O DEFEITO DO DM, CONSERTADO POR DECISAO DO DONO ============================
	/// `A.BP = expressedBP` (`SpiritBomb.dm:170`), UMA linha antes de a bola sair. Tudo que os pulsos
	/// (`A.BP += A.BP*0.01`, `:122`) e as doacoes (`A.BP += M.expressedBP*(M.Ki*0.1)`, `:85`) somaram em
	/// `A.BP` durante a espera era JOGADO FORA no instante do disparo: a Genkidama saia com o poder de
	/// quem a formou e nada mais, e o que a espera deixava de verdade era so o TAMANHO (a `transform`)
	/// e o bit `mega`. A descricao promete que doar "engorda" a bola; em 2026-09-02 o dono mandou
	/// consertar ("corrija esses bugs q vc citou") e o projetil parte com `Bp = BpAcumulado` -- a bola
	/// VALE o que foi doado e o que os pulsos renderam. O `BpAcumulado` continua nascendo em
	/// `expressedBP` (`:68`): sem doador e sem pulso o disparo e o de antes.
	/// =========================================================================================================
	///
	/// O `mega` (a bola que destroi turfs em `view(1)` enquanto voa, `:159-167`) nao entrou: com 40+N
	/// pulsos a escala 12 e inalcancavel na pratica, e o port teria que inventar a destruicao de chao por
	/// projetil. O "No" da caixa de disparo (`:151-158`, que apaga a bola) tambem nao: o canal de
	/// habilidade manda um id e mais nada, e o segundo aperto ja e o "Yes".
	/// </summary>
	private void GenkidamaG12(ServerPlayer pl)
	{
		if (_genkidamaG12.TryGetValue(pl.Id, out EstadoDaGenkidamaG12? g))
		{
			if (g.Fase == 0) { Avisar(pl, "a esfera ainda esta se formando."); return; }
			if (g.Fase >= 2) { Avisar(pl, "a Genkidama ja foi disparada."); return; }
			// "Fire it when ready!" -> "Yes": `holding = 0` e `sleep(20)` (`:148-150`, `:169`).
			g.Fase = 2;
			g.FaseAte = _relogioG12 + 2.0;
			foreach (int doador in _ofertasDeGenkidamaG12.Keys.ToList())
				if (_ofertasDeGenkidamaG12[doador] == pl.Id) _ofertasDeGenkidamaG12.Remove(doador);
			Avisar(pl, "voce ergue a Genkidama: ela sai em dois segundos, no rumo do seu olhar.");
			return;
		}

		Fighter f = pl.Ficha;
		double custo = f.MaxKi * 0.9;   // `if(usr.Ki >= usr.MaxKi*0.9)` (`:180`) -- e o `PodeAtirar` quem recusa, com o numero
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		f.Ki -= custo;
		if (f.baseKi <= f.baseKiMax) f.baseKi += f.KiCapCheck(0.05 * f.BPrestriction * f.KiMod);   // `:44`

		var estado = new EstadoDaGenkidamaG12
		{
			Fase = 0,
			FaseAte = _relogioG12 + 3.0,   // `sleep(30)`
			Ancora = pl.Pos,
			Zona = pl.Zone.Hash,
			BpAcumulado = f.expressedBP,
		};
		_genkidamaG12[pl.Id] = estado;
		estado.Bola = NascerGenkidamaG12(pl, estado);
		if (estado.Bola == null) { _genkidamaG12.Remove(pl.Id); Avisar(pl, "nao ha espaco pra mais energia solta aqui."); return; }
		pl.Moving = false;
		Avisar(pl, "voce ergue as maos e sente a forca vital do planeta correr pra dentro da Genkidama!");   // `:57`
		Falar(pl, Protocol.Fala.Emote, "ergue as duas maos ao ceu!");
		GD.Print($"[server] {pl.Name} comecou a formar a Genkidama (custo {custo:0})");
	}

	/// <summary>A bola (re)nasce na escala de agora, inerte, um tile ao norte do dono. `basedamage = 30`, nao defletivel.</summary>
	private Projetil? NascerGenkidamaG12(ServerPlayer pl, EstadoDaGenkidamaG12 g)
	{
		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 30,          // `A.basedamage = 30` (`:67`)
			Velocidade = 3,         // `walk(A, usr.dir)` sem lag no disparo: um tile por tique
			AlcanceTiles = 1000,    // quem a limita e o `Burnout(1000)`
			Deflectivel = false,    // `A.deflectable = 0` (`:64`)
			Nome = "Genkidama",
			EscalaVisual = g.Escala,
		}, rumoDado: Vec2.Zero, deOnde: pl.Pos + new Vec2(0, -ZoneCollision.TileSize), verbo: "SpiritBomb");
		if (!p.Vivo) return null;
		p.Inerte = true;
		p.VidaRestante = 3600;
		return p;
	}

	private void TickDaGenkidamaG12()
	{
		foreach ((int id, EstadoDaGenkidamaG12 g, ServerPlayer pl) in Varrer(_genkidamaG12))
		{
			Fighter f = pl.Ficha;

			if (g.Fase < 3 && (f.dead || f.KO || pl.Zone.Hash != g.Zona || g.Bola is not { Vivo: true }))
			{
				if (g.Bola is { Vivo: true }) Matar(g.Bola, FimDeProjetil.Nenhum);
				EncerrarGenkidamaG12(pl, g, "a Genkidama se desfaz no ar.");
				continue;
			}
			if (g.Fase < 3) Ancorar(pl, g.Ancora);

			switch (g.Fase)
			{
				case 0:
					if (_relogioG12 < g.FaseAte) break;
					// `:61-69` e o convite aos que meditam (`:77-100`).
					for (int i = 0; i < 3; i++) f.BlastGain(_rng);
					g.Fase = 1;
					g.PulsosRestantes = 0;
					g.ProximoPulsoEm = _relogioG12;
					g.ProximaDoacaoEm = _relogioG12 + 1.0;
					ConvidarDoadoresG12(pl, g);
					Avisar(pl, "a Genkidama esta formada e comeca a crescer. Aperte de novo pra dispara-la.");
					break;

				case 1:
					PulsarGenkidamaG12(pl, g);
					break;

				case 2:
					if (_relogioG12 < g.FaseAte) break;
					LancarGenkidamaG12(pl, g);
					break;

				case 3:
					// `sleep(30); blasting = 0; move = 1` (`:174-176`).
					if (_relogioG12 >= g.FaseAte) EncerrarGenkidamaG12(pl, g, null);
					break;
			}
		}
	}

	/// <summary>
	/// OS DOIS LACOS DE CRESCIMENTO (`SpiritBomb.dm:105-147`), um tique por vez. O primeiro e o das tres
	/// voltas (pulsos "do planeta"); o segundo e o das doacoes, que checa a cada 1 s e converte UMA
	/// doacao pendente em +0,1 de escala 1,5 s depois.
	/// </summary>
	private void PulsarGenkidamaG12(ServerPlayer pl, EstadoDaGenkidamaG12 g)
	{
		bool cresceu = false;

		// ---- as tres voltas ----
		if (g.Iteracoes > 0 && _relogioG12 >= g.EsperaAte)
		{
			if (g.PulsosRestantes <= 0 && g.ProximoPulsoEm <= _relogioG12)
			{
				// `presetiterations -= 1; SpiritsGivenPower += 10` (`:108-109`): comeca a volta.
				g.Iteracoes--;
				g.PulsosRestantes += 10 + (g.Iteracoes == 2 ? 10 : 0);   // a primeira volta parte dos 10 iniciais: 20 pulsos
				g.ProximoPulsoEm = _relogioG12 + 1.5;
			}
			else if (g.PulsosRestantes > 0 && _relogioG12 >= g.ProximoPulsoEm)
			{
				g.PulsosRestantes--;
				g.Escala += 0.1;
				g.BpAcumulado += g.BpAcumulado * 0.01;   // `A.BP += (A.BP*0.01)` (`:122`)
				g.ProximoPulsoEm = _relogioG12 + 1.5;
				cresceu = true;
				if (g.PulsosRestantes == 0 && g.Iteracoes == 2)
				{
					// `switch(presetiterations) if(2) sleep(1100); to_chat(...)` (`:129-131`).
					g.EsperaAte = _relogioG12 + 110.0;
					g.ProximoPulsoEm = g.EsperaAte;
				}
			}
		}
		else if (g.Iteracoes == 2 && !g.AvisouUniverso && g.PulsosRestantes == 0 && _relogioG12 >= g.EsperaAte - 0.05)
		{
			g.AvisouUniverso = true;
			Avisar(pl, "voce acaba de receber a energia de todas as plantas e animais do universo inteiro! A Genkidama nao cresce mais que isto!");
		}

		// ---- as doacoes ----
		if (g.DoacoesPendentes > 0 && _relogioG12 >= g.ProximaDoacaoEm)
		{
			g.DoacoesPendentes--;
			g.Escala += 0.1;
			g.ProximaDoacaoEm = _relogioG12 + 2.5;   // `sleep(10)` + `sleep(15)` (`:133`, `:142`)
			cresceu = true;
		}

		if (!cresceu) return;
		if (g.Bola is { Vivo: true }) Matar(g.Bola, FimDeProjetil.Nenhum);
		g.Bola = NascerGenkidamaG12(pl, g);
		if (g.Bola == null) EncerrarGenkidamaG12(pl, g, "nao ha espaco pra mais energia solta aqui.");
	}

	/// <summary>`for(var/mob/M in player_list) if(M.client && M.med && M.z == usr.z && M != usr)` (`:78-79`) -- o convite.</summary>
	private void ConvidarDoadoresG12(ServerPlayer pl, EstadoDaGenkidamaG12 g)
	{
		int convidados = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			// "GENTE" AQUI E QUEM TEM FICHA PROPRIA E MEDITA: jogador (com tela) ou corpo de bancada; nunca um
			// NPC do mundo nem o reflexo da mente. O `M.client` do DM existia porque so gente medita.
			if (o == pl || o.Papel != null || o.DonoDoClone != 0 || !o.Ficha.med || o.Ficha.dead || o.Ficha.KO) continue;
			_ofertasDeGenkidamaG12[o.Id] = pl.Id;
			Avisar(o, $"{pl.Name} esta formando uma GENKIDAMA. Doar custa um decimo do seu Ki -- responda na aba Other "
					  + "(Doar a Genkidama / Negar).");
			convidados++;
		}
		if (convidados > 0) Avisar(pl, $"{convidados} pessoa(s) meditando neste mundo sentem a Genkidama e podem doar energia.");
	}

	/// <summary>A resposta do doador -- o `alert("Give power?...")` (`:81-99`), do lado de quem recebe.</summary>
	private void ResponderGenkidamaG12(ServerPlayer pl, bool aceitou)
	{
		if (!_ofertasDeGenkidamaG12.Remove(pl.Id, out int dono)
			|| !_players.TryGetValue(dono, out ServerPlayer? quem)
			|| !_genkidamaG12.TryGetValue(dono, out EstadoDaGenkidamaG12? g) || g.Fase != 1)
		{
			Avisar(pl, "ninguem esta pedindo a sua energia agora.");
			return;
		}
		if (!aceitou) { Avisar(pl, "voce guarda a sua energia."); return; }

		// `if(choice=="Yes" && holding==1 && A)` (`:82-91`).
		g.BpAcumulado += pl.Ficha.expressedBP * (pl.Ficha.Ki * 0.1);   // `A.BP += M.expressedBP*(M.Ki*0.1)` -- literal, unidades e tudo
		pl.Ficha.Ki *= 0.9;
		g.DoacoesPendentes++;
		g.Doadores++;
		Avisar(pl, "voce sente um decimo da sua energia escapar.");
		Avisar(quem, "voce acaba de receber mais energia de algum ponto do planeta!");
	}

	/// <summary>
	/// O DISPARO (`:159-179`). O `A.BP = expressedBP` do `:170` NAO veio -- ver o cabecalho: a bola parte
	/// com o poder ACUMULADO na espera (decisao do dono, 2026-09-02). Ela deixa de ser inerte, vai pra
	/// boca do cano e voa no rumo do olhar, um tile por tique, por 100 s.
	/// </summary>
	private void LancarGenkidamaG12(ServerPlayer pl, EstadoDaGenkidamaG12 g)
	{
		Fighter f = pl.Ficha;
		Projetil p = g.Bola!;
		g.Fase = 3;
		g.FaseAte = _relogioG12 + 3.0;
		p.Inerte = false;
		p.Bp = g.BpAcumulado;              // o `A.BP = expressedBP` do `:170` descartava isto -- ver o cabecalho
		p.VidaRestante = 100;              // `A.Burnout(1000)` (`:178`)
		Vec2 frente = MeleeArea.Frente(pl.Facing);
		p.Pos = BocaDeCano.De(pl.Pos, frente);
		p.Cauda = p.Pos;
		p.Rumo = frente;
		p.SegundosPorTile = TempoDoDm.SegundosPorTique;   // `walk(A, usr.dir)` sem lag
		Falar(pl, Protocol.Fala.Diz, "GENKIDAMA!!");
		Avisar(pl, $"a Genkidama parte com escala {g.Escala:0.0} ({g.Doadores} doador(es)) e o poder acumulado na espera: "
				   + $"{g.BpAcumulado:N0} (o seu proprio e {f.expressedBP:N0}).");
		GD.Print($"[server] Genkidama de {pl.Name}: escala {g.Escala:0.0}, {g.Doadores} doador(es), BP {g.BpAcumulado:0} (o expresso do dono e {f.expressedBP:0})");
	}

	private void EncerrarGenkidamaG12(ServerPlayer pl, EstadoDaGenkidamaG12 g, string? aviso)
	{
		_genkidamaG12.Remove(pl.Id);
		foreach (int doador in _ofertasDeGenkidamaG12.Keys.ToList())
			if (_ofertasDeGenkidamaG12[doador] == pl.Id) _ofertasDeGenkidamaG12.Remove(doador);
		if (aviso != null) Avisar(pl, aviso);
	}

	// =====================================================================
	// 5. AS DUAS ABSORCOES -- `demon.dm:34-53` e `Absorption.dm:17-75`, sobre `Absorption.dm:329-392`
	// =====================================================================
	/// <summary>O `absorbing` (`sleep(20)` = 2 s entre uma absorcao e a proxima): id -> livre em (relogio).</summary>
	private readonly Dictionary<int, double> _absorvendoAteG12 = [];

	/// <summary>O `absorbable = 0` da vitima (`absorbproc()`, `Absorption.dm:10-13`: 300 s): id -> livre em.</summary>
	private readonly Dictionary<int, double> _absorvivelEmG12 = [];

	/// <summary>
	/// O `absorber_effectiveness` (`Absorption.dm:374-375`): cada absorcao multiplica por 0,9 por 600 s.
	/// Guardado como a lista dos vencimentos; a eficacia e `0,9 ^ (quantos ainda valem)`.
	/// </summary>
	private readonly Dictionary<int, List<double>> _eficaciaG12 = [];

	/// <summary>`spawn(10) M.Death()` (`Absorption.dm:377`): a vitima morre UM segundo depois. Vitima, algoz, quando.</summary>
	private readonly List<(int Vitima, int Algoz, double Em)> _mortesPendentesG12 = [];

	private double EficaciaDeAbsorcaoG12(int id)
	{
		if (!_eficaciaG12.TryGetValue(id, out List<double>? venc)) return 1;
		venc.RemoveAll(v => v <= _relogioG12);
		return Math.Pow(0.9, venc.Count);
	}

	/// <summary>
	/// ABSORVER A ALMA (`Race Trees/demon.dm:34-53`).
	///
	///     GainAbsorb(1)
	///     if(!absorbing && M.absorbable && !KO && Planet != "Sealed")
	///         absorbing = 1
	///         if(M == usr) return                         // e o `absorbing` fica LIGADO pra sempre -- defeito do DM
	///         if(M.KO && !M.dead && M.HasSoul)
	///             M.HasSoul = 0; AbsorbDatum.absorb(M, 2, 6); Ki += M.Ki; ...; genome Lifespan += (M.DeclineAge-M.Age)/100
	///         sleep(20); absorbing = 0
	///
	/// ============================ ELA MATA, E O CENSO DIZIA QUE NAO ============================
	/// O `absorb(M, 2, 6)` do datum (`Absorption.dm:329-392`) tem dois ramos: o "major" (sela a vitima numa
	/// dimensao -- o motor do Majin, fora deste lote) e o comum, que faz `spawn(10) M.Death()` (`:377`).
	/// O `GainAbsorb(1)` deixa `WillTakeBody = 0` em toda chamada menos a PRIMEIRA da vida do personagem
	/// (o datum nasce com 1 e a caixa "Make this mob a major absorb?" aparece uma vez). Sem caixa aqui,
	/// vale o ramo comum: **a vitima morre um segundo depois**. O censo e o pedido chamavam isto de
	/// "variante que nao mata", lendo so o verb; a descricao da skill irma diz o contrario em voz alta
	/// (`lifeabsorb`: *"This is the only type of absorb that doesn't kill the other person"*). Fica o DM.
	///
	/// AS CONTAS DO RAMO COMUM (`:383-391`), com `upscaler = 2`, `downscaler = 6`, `eff = absorber_effectiveness`:
	///     mais fraco que a vitima:  absorbadd += capcheck( eff * (M.BP/M.BPMod) * BPMod / (6/2) )
	///     mais forte ou igual:      absorbadd += capcheck( eff * (M.BP/M.BPMod) / 6 )
	///     e sempre:                 if(absorbadd < eff*(M.BP + M.absorbadd)*(M.Anger/100)) absorbadd += isso
	///     vitima NPC:               absorbadd += capcheck( relBPmax/300 * Egains * eff * 2 ) / 6
	/// `absorbadd` e o `AbsorbBP` do port (a mesma traducao do Life Suck, G4). `relBPmax` nao existe mais no
	/// port (virou `BpGainBase()`, ver a memoria do ganho linear) -- entra `BpGainBase()` no lugar.
	/// O `Lifespan` do genoma (`demon.dm:49`) nao entrou: o port nao tem tempo de vida (mesma lacuna do Life Suck).
	/// ==========================================================================================
	/// </summary>
	private void AbsorverAlmaG12(ServerPlayer pl)
	{
		if (!PodeAbsorverG12(pl, out string porque)) { Avisar(pl, porque); return; }

		ServerPlayer? alvo = AlvoDeTecnica(pl, RaioDeUmTileG4, o => o.Ficha.KO && !o.Ficha.dead && AbsorvivelG12(o));
		if (alvo == null)
		{
			Avisar(pl, "o alvo precisa estar NOCAUTEADO, vivo, ao seu lado, e nao ter sido absorvido nos ultimos cinco minutos.");
			return;
		}
		_absorvendoAteG12[pl.Id] = _relogioG12 + 2.0;
		if (!alvo.Ficha.HasSoul)
		{
			// "They also must have a soul. (This can only be done once.)" (`demon.dm:50`)
			Avisar(pl, $"{alvo.Name} nao tem mais alma nenhuma pra tomar.");
			return;
		}

		alvo.Ficha.HasSoul = false;
		AbsorverPoderG12(pl, alvo, upscaler: 2, downscaler: 6);
		Fighter f = pl.Ficha;
		f.Ki = Math.Min(f.Ki + alvo.Ficha.Ki, Math.Max(f.MaxKi, f.Ki));   // `usr.Ki += M.Ki`, com o teto do port

		Avisar(pl, $"voce sente a ressonancia da alma de {alvo.Name}, alcanca o espaco dela e a arranca pra dentro de si!");
		Avisar(alvo, $"uma dor extrema: {pl.Name} arrancou a sua alma. Voce nao tem mais alma -- e isso muda algumas coisas.");
		Falar(pl, Protocol.Fala.Emote, $"parece sugar a alma direto de dentro de {alvo.Name}!");
		Persistir(pl);
		Persistir(alvo);
		GD.Print($"[server] {pl.Name} absorveu a ALMA de {alvo.Name} (AbsorbBP {f.AbsorbBP:0})");
	}

	/// <summary>`!usr.absorbing && !usr.KO` (o `Planet != "Sealed"` nao existe no port).</summary>
	private bool PodeAbsorverG12(ServerPlayer pl, out string porque)
	{
		porque = "";
		if (Caido(pl)) { porque = "nao da, caido."; return false; }
		if (_absorvendoAteG12.TryGetValue(pl.Id, out double ate) && _relogioG12 < ate) { porque = "seu corpo ainda esta absorvendo a ultima."; return false; }
		if (_drenoG12.ContainsKey(pl.Id)) { porque = "voce ja esta drenando alguem."; return false; }
		return true;
	}

	private bool AbsorvivelG12(ServerPlayer o) =>
		!_absorvivelEmG12.TryGetValue(o.Id, out double em) || _relogioG12 >= em;

	/// <summary>
	/// O RAMO COMUM DO `absorb()` (`Absorption.dm:373-391`): eficacia x0,9 por 600 s, a vitima fica
	/// inabsorvivel por 300 s (`absorbproc`), morre em 1 s, e o poder entra no `AbsorbBP` do absorvedor.
	/// </summary>
	private void AbsorverPoderG12(ServerPlayer pl, ServerPlayer alvo, double upscaler, double downscaler)
	{
		Fighter f = pl.Ficha, m = alvo.Ficha;
		double eff = EficaciaDeAbsorcaoG12(pl.Id);
		if (!_eficaciaG12.TryGetValue(pl.Id, out List<double>? venc)) _eficaciaG12[pl.Id] = venc = [];
		venc.Add(_relogioG12 + 600.0);            // `container.absorber_effectiveness *= 0.9; spawn(6000) /= 0.9`
		_absorvivelEmG12[alvo.Id] = _relogioG12 + 300.0;   // `spawn M.absorbproc()`
		_mortesPendentesG12.Add((alvo.Id, pl.Id, _relogioG12 + 1.0));   // `spawn(10) M.Death()`

		if (EhJogador(alvo) || alvo.Papel == null)
		{
			f.AbsorbBP += GanhoDeAbsorcao(f, m, eff, upscaler, downscaler);   // `Absorption.dm:383-386`
			double piso = eff * (m.BP + m.AbsorbBP) * (m.Anger / 100);
			if (f.AbsorbBP < piso) f.AbsorbBP += piso;
		}
		else
		{
			f.AbsorbBP += f.CapCheck(f.BpGainBase() * (1.0 / 300) * f.Egains * eff * upscaler) / downscaler;
		}
		f.Statify();
		RepercutirPoder(pl);
		Avisar(alvo, $"voce esta sendo consumido por {pl.Name}...");
	}

	private void TickDasMortesPendentesG12()
	{
		if (_mortesPendentesG12.Count == 0) return;
		foreach ((int vitima, int algoz, double em) in _mortesPendentesG12.ToList())
		{
			if (_relogioG12 < em) continue;
			_mortesPendentesG12.Remove((vitima, algoz, em));
			if (!_players.TryGetValue(vitima, out ServerPlayer? v) || v.Ficha.dead || v.Combate == null) continue;
			_players.TryGetValue(algoz, out ServerPlayer? a);
			// PELA PORTA, como a absorcao do bio: `Morrer` + o funil de derrota (cadaver, Outro Mundo, Zenkai, luto).
			bool morreu = v.Combate.Morrer(ignorarSeguro: false);
			if (morreu && a != null) AoPerderALuta(v, a, morreu: true);
			if (morreu) Avisar(v, "seu corpo, esvaziado, cede.");
		}
	}

	private sealed class DrenoG12
	{
		public int Alvo;
		/// <summary>O `prehp`: levar QUALQUER dano interrompe (`Absorption.dm:42`).</summary>
		public double VidaAoComecar;
		public double ProximoEm;
	}

	private readonly Dictionary<int, DrenoG12> _drenoG12 = [];

	/// <summary>
	/// O DRENO DE ENERGIA do androide (`Absorption.dm:17-69`, `obj/Absorb_Android/verb/Energy_Drain`).
	///
	///     if(M.KO && !M.dead)
	///         if(M.Race == "Android")  -> absorve inteiro (`absorb(M,2,6)`, `Ki += M.Ki`, teto MaxKi)      `:23-30`
	///         else                     -> DRENO sustentado                                                   `:31-63`
	///     else if(usr.absorbing && action) -> apertar de novo PARA o dreno                                   `:66-68`
	///
	/// O DRENO: a cada 0,7 s (`sleep(7)`), com o alvo em `oview(1)`: `Ki += M.Ki*0.1; M.Ki -= M.Ki*0.1;
	/// overcharge = 1`; se M e jogador `absorbadd += M.BP/50 + M.absorbadd*(M.PowerPcnt/100)*(M.Anger/100)`,
	/// senao `absorbadd += bp_gain_base()*BPTick/250`; e `if(M.Ki < M.MaxKi*0.1) spawn M.Death()` -- **o
	/// dreno mata** quem fica sem um decimo da energia. Para: ao levar dano (`prehp > HP`), ao cair, ao
	/// alvo sair de perto, ou no segundo aperto. No fim: `baseKi += kicapcheck(5*KiMod)` (`:63`).
	///
	/// O teto de Ki e do port (o mesmo do Life Suck e da absorcao do bio): o `overcharge = 1` do DM
	/// existe pra deixar o Ki passar do maximo e vazar; aqui o `kiratio` nao tem teto por cima, e Ki
	/// acima do maximo viraria poder de graca.
	/// </summary>
	private void DrenoDeEnergiaG12(ServerPlayer pl)
	{
		if (_drenoG12.Remove(pl.Id, out DrenoG12? aberto))
		{
			if (_players.TryGetValue(aberto.Alvo, out ServerPlayer? largado)) MandarEfeito(largado, "Absorb_Android", 0);
			FecharDrenoG12(pl, "voce solta o alvo e o dreno para.");
			return;
		}
		if (!PodeAbsorverG12(pl, out string porque)) { Avisar(pl, porque); return; }

		ServerPlayer? alvo = AlvoDeTecnica(pl, RaioDeUmTileG4, o => o.Ficha.KO && !o.Ficha.dead && AbsorvivelG12(o));
		if (alvo == null)
		{
			Avisar(pl, "o alvo precisa estar NOCAUTEADO, vivo, ao seu lado, e nao ter sido absorvido nos ultimos cinco minutos.");
			return;
		}
		Fighter f = pl.Ficha;
		_absorvendoAteG12[pl.Id] = _relogioG12 + 2.0;

		if (string.Equals(alvo.Race, "Android", StringComparison.OrdinalIgnoreCase))
		{
			// `:23-30`: o androide caido e absorvido INTEIRO.
			AbsorverPoderG12(pl, alvo, upscaler: 2, downscaler: 6);
			f.Ki = Math.Min(f.Ki + alvo.Ficha.Ki, f.MaxKi);   // `if(usr.Ki>usr.MaxKi) usr.Ki=usr.MaxKi` -- aqui o teto e do DM
			Avisar(pl, $"voce absorve {alvo.Name}, e os materiais dele escorrem pra dentro de voce!");
			Avisar(alvo, "FALHA DE INTEGRIDADE ESTRUTURAL");
			Falar(pl, Protocol.Fala.Emote, $"absorve {alvo.Name}!");
			Persistir(pl);
			GD.Print($"[server] {pl.Name} absorveu o androide {alvo.Name}");
			return;
		}

		// `:31-40`: comeca o dreno.
		_drenoG12[pl.Id] = new DrenoG12 { Alvo = alvo.Id, VidaAoComecar = f.HP, ProximoEm = _relogioG12 + 0.7 };
		MandarEfeito(pl, "Absorb_Android", -1);   // o `AbsorbSparks.dmi` nos dois
		MandarEfeito(alvo, "Absorb_Android", -1);
		Avisar(pl, $"voce comeca a drenar a energia de {alvo.Name}. Aperte de novo pra parar.");
		Falar(pl, Protocol.Fala.Emote, $"comeca a drenar {alvo.Name} da energia dele!");
	}

	private void FecharDrenoG12(ServerPlayer pl, string aviso)
	{
		Fighter f = pl.Ficha;
		MandarEfeito(pl, "Absorb_Android", 0);
		if (f.baseKi <= f.baseKiMax) f.baseKi += f.KiCapCheck(5 * f.KiMod);   // `:63`
		Avisar(pl, aviso);
	}

	private void TickDoDrenoG12()
	{
		foreach ((int id, DrenoG12 d, ServerPlayer pl) in Varrer(_drenoG12))
		{
			if (_relogioG12 < d.ProximoEm) continue;
			Fighter f = pl.Ficha;
			_players.TryGetValue(d.Alvo, out ServerPlayer? alvo);

			// `if(prehp > usr.HP) break; if(usr.KO) break; ... if(breakme) break` (`:42-55`).
			bool alvoPerto = alvo != null && !alvo.Ficha.dead && alvo.Zone.Hash == pl.Zone.Hash
							 && Vec2.Distance(alvo.Pos, pl.Pos) <= RaioDeUmTileG4;
			if (f.HP < d.VidaAoComecar || f.KO || f.dead || !alvoPerto)
			{
				_drenoG12.Remove(id);
				if (alvo != null) MandarEfeito(alvo, "Absorb_Android", 0);
				FecharDrenoG12(pl, f.HP < d.VidaAoComecar ? "o golpe interrompe o dreno." : "o dreno para.");
				continue;
			}
			d.ProximoEm = _relogioG12 + 0.7;   // `sleep(7)`

			Fighter m = alvo!.Ficha;
			double sugado = m.Ki * 0.1;
			f.Ki = Math.Min(f.Ki + sugado, Math.Max(f.MaxKi, f.Ki));
			m.Ki -= sugado;
			if (EhJogador(alvo) || alvo.Papel == null)
				f.AbsorbBP += m.BP / 50 + m.AbsorbBP * m.powerMod * (m.Anger / 100);   // `:51`
			else
				f.AbsorbBP += f.BpGainBase() * GainKnobs.BPTick / 250;                   // `:53`
			f.Statify();
			if (m.Ki < m.MaxKi * 0.1) _mortesPendentesG12.Add((alvo.Id, pl.Id, _relogioG12));   // `spawn M.Death()` (`:54`)
		}
	}

	// =====================================================================
	// 6. IMITACAO -- `Race Trees/shapeshifter.dm:66-98`
	// =====================================================================
	/// <summary>`oview(usr)` sem alcance = `world.view`, o padrao 5 do BYOND (o `World.dm` nao o muda).</summary>
	private const float RaioDaImitacaoG12 = 5 * ZoneCollision.TileSize;

	/// <summary>
	/// QUANTOS DISFARCES FORAM VESTIDOS DESDE O BOOT -- o instrumento da bancada. O disfarce mora no
	/// `ServerPlayer` (que a impressao digital da `--catalogoteste` nao le); sem um numero do lado do
	/// servidor, vestir um rosto seria indistinguivel de nao fazer nada.
	/// </summary>
	private int _disfarcesVestidosG12;

	/// <summary>
	/// IMITAR. Um toggle (`if(usr.IM) ...volta... else ...copia...`). A escolha `input("Imitate who?") in
	/// People` virou o alvo MARCADO (se estiver a cinco tiles) ou o mais proximo a cinco tiles -- a
	/// convencao de todo alvo deste port. O DM so lista `A.client` (jogadores); aqui qualquer corpo com
	/// aparencia serve, porque todo corpo daqui tem uma (la o filtro existia porque NPC nao tinha
	/// `overlayList` pra copiar). O `isimitate` (a raca copiada, que so a Imitacao Permanente le -- F3,
	/// fora deste lote) fica no proprio disfarce.
	///
	/// A aparencia copiada vai pra zona pelo `TrocarAparencias`, o mesmo cano da fusao.
	/// </summary>
	private void ImitarG12(ServerPlayer pl)
	{
		if (pl.Disfarce != null)
		{
			pl.Disfarce = null;
			TrocarAparencias(pl);
			Avisar(pl, "voce volta a ter o seu proprio rosto.");
			Falar(pl, Protocol.Fala.Emote, "treme, e volta a ser quem era.");
			return;
		}
		if (RecusarCaido(pl)) return;

		ServerPlayer? alvo = AlvoDeTecnica(pl, RaioDaImitacaoG12, o => o.Disfarce == null && !o.Ficha.dead);
		if (alvo == null) { Avisar(pl, "nao ha ninguem a cinco tiles pra imitar (marque alguem com duplo clique)."); return; }

		pl.Disfarce = new DisfarceG12
		{
			Nome = NomeVisivel(alvo),
			Raca = alvo.Race,
			Genero = alvo.Genero,
			Visual = VisualVisivel(alvo).Copiar(),
		};
		_disfarcesVestidosG12++;
		TrocarAparencias(pl);
		Avisar(pl, $"voce assume o rosto, o corpo e o nome de {NomeVisivel(alvo)}. Aperte de novo pra voltar.");
		Falar(pl, Protocol.Fala.Emote, $"treme e toma a forma de {NomeVisivel(alvo)}!");
		GD.Print($"[server] {pl.Name} imita {alvo.Name}");
	}

	// =====================================================================
	// 7. DIVISAO DO CORPO -- `Split Forms.dm:76-106`
	// =====================================================================
	private enum FuncaoDeSplitformG12 { Parado, Seguir, AtacarAlvo, AtacarPerto }

	private sealed class SplitformG12
	{
		public int Master;
		public FuncaoDeSplitformG12 Funcao;
		/// <summary>O `timelimit = 1000`, descontado de 10 a cada `sleep(10)`: 100 s de vida.</summary>
		public double ExpiraEm;
		public double ProximoTiqueEm;
	}

	/// <summary>id da copia -> o que ela e. O dono e lido pelo `Master`.</summary>
	private readonly Dictionary<int, SplitformG12> _splitformsG12 = [];

	/// <summary>
	/// O `Splitformskill` do obj (comeca em 1 e sobe de um em um) DERIVADO da `splitformMastery` da
	/// ficha, que sobe 0,2 a cada subida do skill (`Split Forms.dm:98-101`) -- os dois andam em
	/// compasso, e a maestria e a que o save ja guarda.
	/// </summary>
	private static int SplitformskillG12(Fighter f) => 1 + (int)Math.Round(f.splitformMastery / 0.2);

	/// <summary>
	/// DIVIDIR O CORPO. `if(Splitforms < Splitformskill)`: `makeCopy(2, Race, "None", /mob/npc/Splitform, FALSE)`
	/// -- a copia nasce com `BP = expressedBP/2` (`CopyMaker.dm:35`), o nome "[name] Copy", a mesma
	/// aparencia, `HP = max(HP, 100)`, um passo a frente do dono; `Ki -= MaxKi*(0.5/Splitformskill)`;
	/// e com `prob(50/(Splitformskill*5))` o skill sobe (e a maestria +0,2). A copia e um `/mob/npc`
	/// com `hasAI = 0`: fica PARADA ate o dono clicar nela e escolher Follow / Stop / Attack Target /
	/// Attack Nearest / Destroy (`:28-68`). Aqui o clique virou cinco verbos da aba Other, e a ordem vale
	/// pra TODAS as suas copias de uma vez (o port nao tem clique em NPC).
	///
	/// O DM nao confere o Ki antes de cobrar (`:97`) e deixa negativo; aqui cobra-se o que se confere,
	/// como o Guided Ball. O icone do Bio (Cell Jr) e do Tsujin (androide) nao entrou: a copia veste a
	/// aparencia do dono. E copias irmas NAO se atacam no "Attack Nearest" (no DM o `foundTarget` roda
	/// em todo mob menos o dono e a propria -- as irmas entram; ficou de fora por ser autofagia).
	/// </summary>
	private void DividirOCorpoG12(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (RecusarCaido(pl)) return;
		int skill = SplitformskillG12(f);
		int vivas = _splitformsG12.Values.Count(s => s.Master == pl.Id);
		f.splitformCount = vivas;   // `usr.splitformCount = Splitforms` (`:81-84`)
		if (vivas >= skill)
		{
			Avisar(pl, $"voce nao tem pericia pra manter mais que {skill} copia(s).");   // `:103`
			return;
		}
		double custo = f.MaxKi * (0.5 / skill);
		if (f.Ki < custo) { Avisar(pl, $"dividir o corpo pede {custo:0} de energia."); return; }

		ServerPlayer copia = CriarSplitformG12(pl);
		f.Ki -= custo;
		f.splitformCount = vivas + 1;
		f.Statify();
		RepercutirPoder(pl);

		if (_rng.NextDouble() * 100 < 50.0 / (skill * 5))   // `prob(50/(Splitformskill*5))`
		{
			f.splitformMastery += 0.2;
			Avisar(pl, "sua pericia de divisao aumentou.");
		}
		Avisar(pl, "voce divide o seu corpo em dois! (ordens na aba Other: seguir, parar, atacar o alvo, atacar o mais perto, desfazer)");
		Falar(pl, Protocol.Fala.Emote, "se divide em dois!");
		Persistir(pl);
		GD.Print($"[server] {pl.Name} criou a copia '{copia.Name}' (id {copia.Id}, BP {copia.Ficha.BP:0})");
	}

	/// <summary>
	/// A copia -- o `makeCopy(2, ...)` (`CopyMaker.dm:34-46`, `:60-78`) e o `Split Forms.dm:86-96`.
	///
	/// ============ POR QUE ISTO NAO CHAMA O <see cref="EspelharODono"/>, QUE PARECE IGUAL ============
	/// O bloco de stats abaixo e, linha por linha, o do reflexo da mente -- e MESMO ASSIM sao duas
	/// regras, porque no DM sao dois procs diferentes: o reflexo e escrito a mao no
	/// `spawn_clone` (`MindMeditate.dm:254-275`) e a copia sai do `makeCopy` generico
	/// (`AssignDupeVars` + `CopyMaker.dm:35`). As duas discordam em tres pontos, e todos importam:
	///
	///   1. O PODER. Aqui `z.BP = expressedBP/2` (`CopyMaker.dm:35`) -- METADE. La
	///      `C.mind_seed_bp = max(round(expressedBP), 1)` (`:254`) -- INTEIRO. Espelhar dobraria a copia.
	///   2. O PINO. O `EspelharODono` escreve <see cref="ServerPlayer.BpDaMente"/>, e o
	///      `TicarUmCorpo` REESCREVE `Ficha.BP = BpDaMente` a cada tique enquanto o pino for > 0
	///      (`GameServer.Clone.cs`, o `NPCTicker()` do `MindClone`). A copia do Splitform nao tem
	///      `mind_seed_bp` nenhum no DM: dar-lhe um congelaria o poder dela pra sempre.
	///   3. O RESTO DO NASCIMENTO. `Anger = 100`, `staminadeBuff = 100` e a barriga cheia sao do
	///      `spawn_clone` (`:269-272`); a copia herda os dela do `AssignDupeVars`.
	///
	/// Ou seja: o que se parece nao e o mesmo conceito escrito duas vezes -- sao duas receitas do DM
	/// que por acaso compartilham o meio. Unificar aqui trocaria a regra de uma delas em silencio.
	/// =============================================================================================
	/// </summary>
	private ServerPlayer CriarSplitformG12(ServerPlayer dono)
	{
		Fighter d = dono.Ficha;
		var copia = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = dono.Name + " Copy",
			Zone = dono.Zone,
			Pos = dono.Pos + MeleeArea.Frente(dono.Facing) * ZoneCollision.TileSize,   // `step(nS, usr.dir)`
			Facing = dono.Facing,
			Race = dono.Race,
			Class = dono.Class,
			Genero = dono.Genero,
			Planeta = dono.Planeta,
			Idade = dono.Idade,
			Visual = dono.Visual.Copiar(),
			LastInputMs = NowMs(),
			Cerebro = new Cerebro(),
			Ficha = new Fighter(),
			Livro = new SkillBook(),
		};
		Fighter c = copia.Ficha;
		c.Race = d.Race; c.Class = d.Class; c.Idade = d.Idade;
		c.BP = Math.Max(d.expressedBP / 2, 1);   // `z.BP = expressedBP/2`
		c.BPMod = d.BPMod;
		c.physoff = d.physoff; c.physdef = d.physdef; c.technique = d.technique;
		c.kioff = d.kioff; c.kidef = d.kidef; c.kiskill = d.kiskill; c.speed = d.speed; c.magiskill = d.magiskill;
		c.physoffMod = d.physoffMod; c.physdefMod = d.physdefMod; c.techniqueMod = d.techniqueMod;
		c.kioffMod = d.kioffMod; c.kidefMod = d.kidefMod; c.kiskillMod = d.kiskillMod; c.speedMod = d.speedMod; c.magiMod = d.magiMod;
		c.HP = 100;   // `nS.HP = max(usr.HP, 100)`
		c.KO = false; c.dead = false;
		c.maxstamina = d.maxstamina; c.stamina = d.maxstamina;
		c.Statify();
		c.Ki = c.MaxKi;
		c.PowerLevel();

		PorNoMundo(copia);
		copia.Combate.Letal = false;   // `murderToggle` de um mob novo e 0
		foreach (ServerPlayer o in ZoneList(copia.Zone.Hash)) MandarLook(o, copia);
		_splitformsG12[copia.Id] = new SplitformG12
		{
			Master = dono.Id,
			Funcao = FuncaoDeSplitformG12.Parado,
			ExpiraEm = _relogioG12 + 100.0,
			ProximoTiqueEm = _relogioG12 + 1.0,
		};
		return copia;
	}

	/// <summary>
	/// O RAMO DA COPIA NO `TicarUmCorpo` (`GameServer.Clone.cs`): quem ela persegue e pra onde vai saem
	/// da ORDEM do dono, e nao do molde nem da fera. Devolve falso quando o corpo nao pensa neste tique.
	///
	///   Follow         -> `if(d >= 2) step_towards(src, usr)` (`:47-52`): anda ate ficar a dois tiles
	///   Stop           -> `hasAI = 0` (`:53-55`): parada
	///   Attack Target  -> `foundTarget(choice)` no alvo escolhido (`:56-66`): o alvo MARCADO do dono
	///   Attack Nearest -> `foundTarget(nE)` em `range(MAX_AGGRO_RANGE = 20)` (`:38-42`), menos o dono
	/// </summary>
	private bool GuiarSplitformG12(ServerPlayer copia, SplitformG12 sf, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = copia.Pos;
		if (!_players.TryGetValue(sf.Master, out ServerPlayer? dono) || dono.Zone.Hash != copia.Zone.Hash)
		{
			DesfazerSplitformG12(copia.Id);
			return false;
		}
		if (copia.Ficha.dead || copia.Ficha.KO) { copia.Moving = false; return false; }

		switch (sf.Funcao)
		{
			case FuncaoDeSplitformG12.Seguir:
				if (Vec2.Distance(copia.Pos, dono.Pos) >= 2 * ZoneCollision.TileSize) destino = dono.Pos;
				break;
			case FuncaoDeSplitformG12.AtacarAlvo:
				if (dono.AlvoId != 0 && dono.AlvoId != copia.Id && _players.TryGetValue(dono.AlvoId, out ServerPlayer? m)
					&& m.Zone.Hash == copia.Zone.Hash && !m.Ficha.dead)
				{ presa = m; destino = m.Pos; }
				break;
			case FuncaoDeSplitformG12.AtacarPerto:
			{
				float perto = 20 * ZoneCollision.TileSize;
				perto *= perto;
				foreach (ServerPlayer o in ZoneList(copia.Zone.Hash))
				{
					if (o.Id == copia.Id || o.Id == dono.Id || o.Ficha.dead || o.Ficha.KO) continue;
					if (_splitformsG12.TryGetValue(o.Id, out SplitformG12? irma) && irma.Master == dono.Id) continue;
					float d2 = (o.Pos - copia.Pos).LengthSquared;
					if (d2 >= perto) continue;
					perto = d2; presa = o;
				}
				if (presa != null) destino = presa.Pos;
				break;
			}
		}
		return true;
	}

	/// <summary>O `spawn while(src) { sleep(10); timelimit -= 10; if(HP<=0 || (HP<=8 && KO)) del; if(isnull(master) || timelimit<=0) del }` (`:15-23`).</summary>
	private void TickDosSplitformsG12()
	{
		foreach (int id in _splitformsG12.Keys.ToList())
		{
			SplitformG12 sf = _splitformsG12[id];
			if (!_players.TryGetValue(id, out ServerPlayer? copia)) { _splitformsG12.Remove(id); AtualizarContagemDeSplitformsG12(sf.Master); continue; }
			if (_relogioG12 < sf.ProximoTiqueEm) continue;
			sf.ProximoTiqueEm = _relogioG12 + 1.0;

			bool masterSumiu = !_players.TryGetValue(sf.Master, out ServerPlayer? dono) || dono.Ficha.dead;
			if (copia.Ficha.dead || (copia.Ficha.KO && copia.Ficha.HP <= 8) || masterSumiu || _relogioG12 >= sf.ExpiraEm)
			{
				if (!copia.Ficha.dead && !masterSumiu)
					AvisarPertoG3(copia, RaioDaVista, $"{copia.Name} foi derrotado.");   // `to_chat(view(src), "[src] has been defeated.")`
				DesfazerSplitformG12(id);
			}
		}
	}

	/// <summary>`Del(): master.splitformCount = max(0, master.splitformCount-1)` (`:24-27`) -- e o corpo sai do mundo.</summary>
	private void DesfazerSplitformG12(int id)
	{
		if (!_splitformsG12.Remove(id, out SplitformG12? sf)) return;
		if (_players.TryGetValue(id, out ServerPlayer? copia)) RemoverNpc(copia);
		AtualizarContagemDeSplitformsG12(sf.Master);
	}

	private void AtualizarContagemDeSplitformsG12(int master)
	{
		if (!_players.TryGetValue(master, out ServerPlayer? dono)) return;
		dono.Ficha.splitformCount = _splitformsG12.Values.Count(s => s.Master == master);
		dono.Ficha.Statify();
		RepercutirPoder(dono);
	}

	private void OrdenarSplitformsG12(ServerPlayer pl, FuncaoDeSplitformG12 funcao)
	{
		int n = 0;
		foreach (SplitformG12 sf in _splitformsG12.Values)
			if (sf.Master == pl.Id) { sf.Funcao = funcao; n++; }
		if (n == 0) { Avisar(pl, "voce nao tem nenhuma copia de pe."); return; }
		if (funcao == FuncaoDeSplitformG12.AtacarAlvo && pl.AlvoId == 0) { Avisar(pl, "marque um alvo primeiro (duplo clique)."); return; }
		Avisar(pl, funcao switch
		{
			FuncaoDeSplitformG12.Seguir => $"sua(s) {n} copia(s) passa(m) a seguir voce.",
			FuncaoDeSplitformG12.AtacarAlvo => $"sua(s) {n} copia(s) ataca(m) o seu alvo.",
			FuncaoDeSplitformG12.AtacarPerto => $"sua(s) {n} copia(s) ataca(m) quem estiver mais perto.",
			_ => $"sua(s) {n} copia(s) para(m).",
		});
	}

	/// <summary>`"Destroy Splitforms": for(A in NPC_list) if(A.displaykey == usr.key) del(A)` (`:43`).</summary>
	private void DestruirSplitformsG12(ServerPlayer pl)
	{
		int n = 0;
		foreach (int id in _splitformsG12.Keys.ToList())
			if (_splitformsG12[id].Master == pl.Id) { DesfazerSplitformG12(id); n++; }
		Avisar(pl, n == 0 ? "voce nao tem nenhuma copia de pe." : $"voce reabsorve {n} copia(s).");
	}

	// =====================================================================
	// 8. CULTIVAR SENZU -- `Stamina/Food.dm:2-13` (o item: `:19-48`)
	// =====================================================================
	/// <summary>O `GrowingBean`: id -> quando a semente fica pronta (relogio). E `tmp` no DM: relogar perde a semente.</summary>
	private readonly Dictionary<int, double> _senzuG12 = [];

	/// <summary>
	/// O `Senzu` do mob (`Food.dm:15`): quantas "doses" de semente ainda estao no corpo. Sobe 4 por semente
	/// comida, so aceita comer com `Senzu + 4 <= 4` (ou seja, com o corpo LIMPO), e decai por `prob(2)`
	/// a cada tique do folego (`StaminaDrain.dm:42-44`). Nao e salvo aqui (no DM e); e um freio de
	/// minutos, e o relog ja custa mais que isso.
	/// </summary>
	private readonly Dictionary<int, int> _senzuNoCorpoG12 = [];

	private double _acumuloDoSenzuG12;

	/// <summary>
	/// `if(!GrowingBean) { GrowingBean = 1; sleep(600); new Senzu em (x, y-1); GrowingBean = 0 }` -- 60 s.
	/// A semente nasce NA MOCHILA em vez de no chao: o port nao tem item largado no mundo ("largar apaga",
	/// `GameServer.Mochila.cs`), e uma semente no chao que ninguem consegue pegar e uma semente perdida.
	/// </summary>
	private void CultivarSenzuG12(ServerPlayer pl)
	{
		if (_senzuG12.ContainsKey(pl.Id)) { Avisar(pl, "espere esta terminar de crescer."); return; }   // `:13`
		_senzuG12[pl.Id] = _relogioG12 + 60.0;
		Avisar(pl, "voce comeca a cultivar uma Semente Senzu. Leva um minuto...");   // `:7`
	}

	private void TickDoSenzuG12(double dt)
	{
		foreach (int id in _senzuG12.Keys.ToList())
		{
			if (_relogioG12 < _senzuG12[id]) continue;
			_senzuG12.Remove(id);
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			if (Guardar(pl, CatalogoDeItens.Senzu)) Avisar(pl, "pronto: a Semente Senzu esta na sua mochila.");   // `:11`
		}

		// O DECAIMENTO: um tique de folego por segundo (`StaminaDrain.dm:42-44`).
		_acumuloDoSenzuG12 += dt;
		if (_acumuloDoSenzuG12 < 1.0) return;
		_acumuloDoSenzuG12 -= 1.0;
		foreach (int id in _senzuNoCorpoG12.Keys.ToList())
		{
			int n = Math.Min(_senzuNoCorpoG12[id], 4);
			if (n > 0 && _rng.Next(100) < 2) n--;
			if (n <= 0) _senzuNoCorpoG12.Remove(id); else _senzuNoCorpoG12[id] = n;
		}
	}

	/// <summary>
	/// COMER A SEMENTE (`Food.dm:38-48`): `if(!KO && CanEat && Senzu+4 <= 4)`: `Senzu += 4`, `sensuuse()`
	/// (todo membro nao decepado abaixo de 100 vai a `25*Increase` = 100, e `SpreadHeal(100)`) e a
	/// nutricao da comida (10). O DM, com o corpo ainda cheio de semente, cai fora de TODOS os `else` e
	/// NAO DIZ NADA; aqui a recusa fala, porque verb mudo e a regra 5 da casa.
	/// </summary>
	private void ComerSenzuG12(ServerPlayer pl, ItemDef def)
	{
		Fighter f = pl.Ficha;
		int noCorpo = _senzuNoCorpoG12.GetValueOrDefault(pl.Id);
		if (f.KO) { Avisar(pl, "voce nao consegue comer uma Semente Senzu desacordado."); return; }
		if (noCorpo + 4 > 4) { Avisar(pl, "seu corpo ainda digere a ultima semente -- espere um pouco."); return; }

		pl.Mochila.Tirar(def.Id);
		MandarMochila(pl);
		_senzuNoCorpoG12[pl.Id] = noCorpo + 4;
		pl.Combate?.Corpo.Curar(100);   // `C.health = 25*Increase` + `SpreadHeal(min((HP+25)*Increase, 100))`
		pl.Combate?.SincronizarVida();
		// A SENZU E CURA ANTES DE SER COMIDA: entra mesmo de estomago cheio (`mesmoCheio`), so nao enche alem do teto.
		Nutricao.Refeicao r = Nutricao.Comer(f, def.Nutricao, mesmoCheio: true);
		Avisar(pl, "voce come a Semente Senzu e o corpo inteiro se refaz.");
		if (r.Aviso.Length > 0) Avisar(pl, r.Aviso);
		Falar(pl, Protocol.Fala.Emote, "poe uma Semente Senzu na boca.");
	}

	/// <summary>
	/// DAR A SEMENTE A UM CAIDO -- o `Use_on(mob/M in oview(1))` (`Food.dm:57-66`): `if(M.KO) { M.Un_KO();
	/// sensuuse(usr); M.Senzu += Increase; del(src) }`.
	///
	/// ============================ O DEFEITO DO DM, CONSERTADO ============================
	/// `sensuuse(usr)` (`:63`): a cura completa ia pra QUEM DA, e nao pra quem esta caido -- que so
	/// recebia o `Un_KO()` (25 de vida nos vitais, `KO.dm:133`) e as quatro doses no corpo. E um erro de
	/// variavel de uma linha (`usr` onde devia ser `M`, como o `M.Un_KO()` e o `M.Senzu` ao lado). Por
	/// decisao do dono (2026-09-02, "corrija esses bugs q vc citou") a semente cura quem a RECEBE:
	/// `sensuuse(M)`.
	/// ======================================================================================
	/// </summary>
	private void AcudirComSenzuG12(ServerPlayer pl, ItemDef def)
	{
		ServerPlayer? alvo = AlvoDeTecnica(pl, RaioDeUmTileG4, o => o.Ficha.KO && !o.Ficha.dead && o.Combate != null);
		if (alvo == null) { Avisar(pl, "isto so serve em alguem DESACORDADO ao seu lado."); return; }   // `:66`

		pl.Mochila.Tirar(def.Id);
		MandarMochila(pl);
		alvo.Combate!.Levantar();   // `M.Un_KO()`
		alvo.Combate.Corpo.Curar(100);   // `sensuuse(M)` -- em quem RECEBE (o DM escrevia `usr`: ver o cabecalho)
		alvo.Combate.SincronizarVida();
		_senzuNoCorpoG12[alvo.Id] = _senzuNoCorpoG12.GetValueOrDefault(alvo.Id) + 4;
		Avisar(alvo, $"{pl.Name} poe uma Semente Senzu na sua boca: voce acorda e o corpo inteiro se refaz.");
		Avisar(pl, $"voce da a semente a {alvo.Name} e o corpo dele se refaz inteiro.");
		Falar(pl, Protocol.Fala.Emote, $"da uma Semente Senzu a {alvo.Name}.");
	}

	// =====================================================================
	// 9. ALVOS DE KI -- `Stats/Training/Meditate.dm:236-289`
	// =====================================================================
	private sealed class EstadoDosAlvosDeKiG12
	{
		public double ProximoAlvoEm;
		public double ProximoPassoEm;
		/// <summary>O `AtaqueAte` do dono na ultima leitura: ele muda em TODO soco (`GameServer.Combat.cs:456`), tambem no soco no ar.</summary>
		public long UltimoSocoVisto;
		public List<Projetil> Alvos = [];
		public ulong Zona;
	}

	private readonly Dictionary<int, EstadoDosAlvosDeKiG12> _alvosDeKiG12 = [];

	/// <summary>
	/// ALVOS DE KI -- PARCIAL, e o que falta esta dito aqui.
	///
	/// O verb (`:236-256`): toggle; `if(!move || !hasTime) return; med = 1;` e um laco que, enquanto
	/// `shdbox && med && !deepmeditation`, cria um `obj/training_obj/Ki_Target` num turf sorteado em
	/// `view(4)` a cada `sleep(35)` = 3,5 s. O alvo (`:258-289`) da um passo sorteado a cada 0,5 s e some
	/// em 5 s. Ele rende de DUAS formas no DM:
	///   * `Click()` do dono (`:280-289`): `missile()` de enfeite, `Blast_Gain(1, TRUE)` e +5 na skill
	///     focada (`focusskill`, do Focus_Skill -- nao portado);
	///   * SOCO do dono (`attack_proc.dm:62-65`): `Attack_MasteryGain(1)` + `TrainHit` = `Train_Gain(10*gainscale)`,
	///     com `gainscale = max((1 - BP/TopBP)^2, 0.2)`.
	///
	/// ============================ O QUE FALTA: O CLIQUE (e por isso e PARCIAL) ============================
	/// O port nao tem clique em entidade que nao e corpo (o duplo clique marca gente), e a outra
	/// traducao possivel -- "acerte o alvo com um tiro seu" -- morre no `PodeAtirar`: quem medita nao
	/// atira ("nao da pra atirar meditando"), e o verb OBRIGA a meditar. Sobra o segundo caminho do
	/// DM, que esta 1:1: o SOCO (`attack_proc.dm:62-65`, `Train_Gain(10*gainscale)`; o
	/// `Attack_MasteryGain(1)` nao existe no port). O `Blast_Gain(1, TRUE)` e o `KiSkillGains(5)` da
	/// skill focada do clique ficam de fora, e a diferenca e esta: o verb rende ganho de TREINO, nao de
	/// tiro. O alvo e um <see cref="Projetil"/> INERTE do dono (nao colide com ninguem, nao machuca):
	/// quem o move e este tique, um tile sorteado a cada 0,5 s.
	/// =====================================================================================================
	/// </summary>
	private void AlvosDeKiG12(ServerPlayer pl)
	{
		if (_alvosDeKiG12.Remove(pl.Id, out EstadoDosAlvosDeKiG12? velho))
		{
			foreach (Projetil t in velho.Alvos) if (t.Vivo) Matar(t, FimDeProjetil.Apagou);
			Avisar(pl, "voce para de treinar com alvos de Ki.");   // `:245`
			return;
		}
		if (RecusarCaido(pl)) return;
		if (!PodeMexerOCorpo(pl)) { Avisar(pl, "voce esta preso demais pra isso agora."); return; }   // `if(!move) return`

		pl.Ficha.med = true;   // `med = 1` (`:248`)
		pl.Ficha.train = false;
		_alvosDeKiG12[pl.Id] = new EstadoDosAlvosDeKiG12
		{
			ProximoAlvoEm = _relogioG12, ProximoPassoEm = _relogioG12 + 0.5, UltimoSocoVisto = pl.AtaqueAte, Zona = pl.Zone.Hash,
		};
		Avisar(pl, "voce medita e esferas do seu Ki passam a aparecer em volta. Soque cada uma (rende ganho de treino). "
				   + "Voce precisa continuar meditando.");   // `:249`
	}

	private void TickDosAlvosDeKiG12()
	{
		foreach ((int id, EstadoDosAlvosDeKiG12 ak, ServerPlayer pl) in Varrer(_alvosDeKiG12))
		{
			Fighter f = pl.Ficha;
			ak.Alvos.RemoveAll(t => !t.Vivo);

			// `while(shdbox && med && !deepmeditation) { if(!move || !hasTime) break; ... }` (`:251-252`).
			if (!f.med || f.KO || f.dead || pl.Zone.Hash != ak.Zona || NaMente(pl))
			{
				_alvosDeKiG12.Remove(id);
				foreach (Projetil t in ak.Alvos) if (t.Vivo) Matar(t, FimDeProjetil.Apagou);
				Avisar(pl, "voce para de treinar com alvos de Ki.");
				continue;
			}

			// UM ALVO NOVO a cada 3,5 s, num tile sorteado em `view(4)`, fora de parede.
			if (_relogioG12 >= ak.ProximoAlvoEm)
			{
				ak.ProximoAlvoEm = _relogioG12 + 3.5;
				ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
				for (int tentativa = 0; tentativa < 8; tentativa++)
				{
					Vec2 onde = pl.Pos + new Vec2((_rng.Next(9) - 4) * ZoneCollision.TileSize, (_rng.Next(9) - 4) * ZoneCollision.TileSize);
					if (mapa != null && mapa.BlockedAt(onde)) continue;
					Projetil t = Disparar(pl, new ReceitaDeProjetil
					{
						Tipo = TipoDeProjetil.Blast, BaseDano = 0, Deflectivel = false, Empurra = false,
						AlcanceTiles = 1000, Nome = "alvo de Ki",
					}, rumoDado: Vec2.Zero, deOnde: onde, verbo: "Ki_Targets");
					if (!t.Vivo) break;
					t.Inerte = true;
					t.VidaRestante = 5.0;   // `spawn(50) deleteMe()` (`:267`)
					ak.Alvos.Add(t);
					break;
				}
			}

			// O PASSEIO: `dir = pick(8 rumos); step(src, dir); sleep(5)` (`:276-279`).
			if (_relogioG12 >= ak.ProximoPassoEm)
			{
				ak.ProximoPassoEm = _relogioG12 + 0.5;
				ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
				foreach (Projetil t in ak.Alvos)
				{
					Vec2 passo = MoveRules.OitoRumos[_rng.Next(MoveRules.OitoRumos.Length)] * ZoneCollision.TileSize;
					if (mapa != null && mapa.BlockedAt(t.Pos + passo)) continue;
					t.Pos += passo;
					t.Cauda = t.Pos;
				}
			}

			// O ACERTO: o SOCO do dono com o alvo no cone do golpe -> `Train_Gain(10 * gainscale)`
			// (`attack_proc.dm:62-65` -> `Train.dm:141-143`, `defgains = 10`, `gainscale = max((1-BP/TopBP)^2, 0.2)`).
			// Um soco por tique, no maximo um alvo por soco -- o `B` do `attack_proc` e um objeto so.
			if (pl.AtaqueAte == ak.UltimoSocoVisto) continue;
			ak.UltimoSocoVisto = pl.AtaqueAte;
			foreach (Projetil t in ak.Alvos.ToList())
			{
				if (!t.Vivo) continue;
				Vec2 ate = t.Pos - pl.Pos;
				if (ate.Length > 1.5f * ZoneCollision.TileSize || MeleeArea.Angulo(MeleeArea.Frente(pl.Facing), ate) > 60) continue;
				double gainscale = Math.Max(Math.Pow(1 - f.BP / Math.Max(GainKnobs.TopBP, 1), 2), 0.2);
				f.TrainGain(_rng, 10 * gainscale);
				_acertosDeAlvoDeKiG12++;
				Matar(t, FimDeProjetil.Acertou);
				ak.Alvos.Remove(t);
				break;
			}
		}
	}

	/// <summary>Quantos alvos de Ki ja foram acertados desde o boot -- o instrumento da bancada.</summary>
	private int _acertosDeAlvoDeKiG12;

	// =====================================================================
	// 10. PRECOGNICAO -- `Race Trees/kanassa-jin.dm:38-43` (passiva)
	// =====================================================================
	/// <summary>Quem tem a skill, refeito a cada segundo (varrer todo livro a 5 Hz seria pagar por quem nao a tem).</summary>
	private readonly List<int> _precognitivosG12 = [];
	private double _proximaVarreduraDePrecognicaoG12;
	private double _proximoReflexoDePrecognicaoG12;

	/// <summary>Quantas esquivas a precognicao ja deu desde o boot -- o instrumento da bancada.</summary>
	private int _esquivasDePrecognicaoG12;

	/// <summary>
	/// O `effector()` da Precognicao, que o motor de skills chama a cada `sleep(2)` = 0,2 s (`skill.dm:39-42`):
	///
	///     if(!savant.blasting && savant.move)
	///         for(var/obj/attack/blast/A in view(1)) if(A.proprietor != savant)
	///             savant.dir = turn(A.dir, 90); step(savant, savant.dir); break
	///
	/// `view(1)` e o 3x3 em volta; `/obj/attack/blast` e todo tiro (bola E segmento de raio). O passo e de
	/// UM tile, perpendicular ao rumo do tiro (90 graus anti-horarios), e respeita parede como todo
	/// `step()`. `blasting` e o `BlastingG12` + o raio na mao; `move` e o `PodeMexerOCorpo`.
	/// E o mesmo gesto da esquiva autonoma do Ultra Instinto, uma esquiva por tique e sem chance: quem
	/// ve o futuro nao erra o passo.
	/// </summary>
	private void TickDaPrecognicaoG12(double dt)
	{
		if (_projeteisVivos == 0) return;
		if (_relogioG12 >= _proximaVarreduraDePrecognicaoG12)
		{
			_proximaVarreduraDePrecognicaoG12 = _relogioG12 + 1.0;
			_precognitivosG12.Clear();
			foreach (ServerPlayer p in _players.Values)
				if (p.Livro?.Sabe(PathDaPrecognicaoG12) == true) _precognitivosG12.Add(p.Id);
		}
		if (_precognitivosG12.Count == 0 || _relogioG12 < _proximoReflexoDePrecognicaoG12) return;
		_proximoReflexoDePrecognicaoG12 = _relogioG12 + 0.2;

		const float alcance = 1.5f * ZoneCollision.TileSize;   // o 3x3: ate um tile e meio do centro
		foreach (int id in _precognitivosG12)
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			if (pl.Ficha.KO || pl.Ficha.dead || BlastingG12(id) || _canais.ContainsKey(id) || !PodeMexerOCorpo(pl)) continue;
			List<Projetil> tiros = ProjeteisDaZona(pl.Zone.Hash);
			if (tiros.Count == 0) continue;
			foreach (Projetil t in tiros)
			{
				if (!t.Vivo || t.Inerte || t.Dono == id || t.Rumo.LengthSquared < 1e-6f) continue;
				if (Math.Abs(t.Pos.X - pl.Pos.X) > alcance || Math.Abs(t.Pos.Y - pl.Pos.Y) > alcance) continue;

				Vec2 rumo = t.Rumo.Girado90().Normalized();
				Vec2 destino = pl.Pos + rumo * ZoneCollision.TileSize;
				ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
				pl.Facing = MoveRules.FacingFrom(rumo, pl.Facing);   // `savant.dir = turn(...)`
				if (mapa == null || !mapa.BlockedAt(destino))         // `step()` falha contra parede
				{
					pl.Pos = destino;
					pl.SeqDoTeleporte = pl.SeqInput;   // o input montado antes deste instante nao opina sobre onde o corpo esta
					pl.CorrecaoEsperadaAte = NowMs() + 400;
					MandarCorrecaoG3(pl);
					_esquivasDePrecognicaoG12++;
				}
				break;
			}
		}
	}
}
