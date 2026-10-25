using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ A SAGA DO CORRUPTED MAJIN, LADO DO SERVIDOR (2026-10-16) ============================
/// O dono: *"um amigo meu americano fez um sistema legal pro corrupted majin (classe majin mais rara), quando o majin corrupted
/// fizer toda a saga majin, e no final virar a pure form vai acontecer o seguinte"* -- e *"no dm tem tudo certinho la, cada
/// sprite, cada trigger tudo"*. As regras e os numeros moram no Core (`Races.SagaDoMajin`); aqui mora o que e ESTADO e GESTO.
///
/// ============================ A SAGA -- `Magic/MajinSaga.dm:395-577`, NA ORDEM ============================
///   1. absorver um KAI -> forma 1 (<see cref="SagaAoAbsorver"/>, chamado pelas duas portas da absorcao);
///   2. um amigo MORTO por um inimigo na vista -> forma 2, e a OUTRA METADE salta do corpo (<see cref="SagaNaFuriaDaMorte"/>,
///      pendurado no luto da morte -- `Death/Death.dm:82`);
///   3. absorver a propria outra metade, VENCIDA -> forma 3 (<see cref="TentarAbsorverAOutraMetade"/>);
///   4. tres JOGADORES absorvidos na forma 3 -> forma 4;
///   5. a FORMA PURA destrava: forma 4, ninguem dentro, BP base na porta (<see cref="ConferirAFormaPura"/>).
/// Cada degrau toca a cena dele (`Cinematicas.MajinForma1..4`, pelo `S2C.CenaDoMajin`), troca o corpo (a faixa reservada de
/// `Appearance.Corpo`), soma +1 no `BPMod` e da o marco -- tudo no <see cref="AvancarASaga"/>.
///
/// ============================ O SISTEMA DO AMIGO, DEPOIS DA FORMA PURA ============================
/// A FORMA PURA E O REPOUSO: o <see cref="AjustarFormaDoMajin"/> e o zelador -- ele escreve o `ServerPlayer.PisoDoMajin` (que o
/// `Perfil` entrega ao catalogo como `PisoProprio`) e poe o corpo onde o piso manda. Com o piso no lugar, cair, reverter (X) e
/// entrar no jogo levam o Majin pra Forma Pura pelos MESMOS caminhos que levam todo mundo pro proprio repouso -- e so absorver
/// alguem (o piso some), enlouquecer (o piso vira a insana) e dormir (o piso some) o tiram dela.
///
/// A SANIDADE anda no <see cref="TickDaLoucuraDoMajin"/>: cai na Pura sa, sobe com alguem dentro, perde 50 com a Sycophantic
/// Rage e 25 quando o primeiro absorvido escapa. No zero, a INSANA: a IA dirige, o corpo grita, o Sense e o teletransporte somem,
/// o Ki cai sem regenerar -- e no fim dele (ou no nocaute), dois minutos de sono e a cabeca de volta.
/// =========================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// DEFEITOS INJETADOS (bancada `--sagamajinteste`) -- falsos em jogo, sempre
	// =====================================================================
	/// <summary>A furia da forma 2 nao solta a outra metade (o `majin_spawn_clone` esquecido).</summary>
	internal static bool SagaSemOutraMetadeDeTeste;

	/// <summary>A outra metade e absorvida DE PE, sem precisar ser vencida (o `if(!(M.KO || M.dead || M.HP &lt;= 15))` esquecido).</summary>
	internal static bool OutraMetadeSemVencerDeTeste;

	/// <summary>
	/// A Forma Pura deixa de ser o repouso: a queda (e o login) a desfazem, como qualquer forma. O X nao entra aqui -- a recusa
	/// dele e da forma, e a raca nem tem escada pra descer.
	/// </summary>
	internal static bool PuraSemPisoDeTeste;

	/// <summary>A insana regenera Ki como qualquer corpo -- e o Ki nunca acaba (*"se nao o ki nunca vai acabar"*).</summary>
	internal static bool LoucuraComRegenDeTeste;

	/// <summary>Expulsar quem esta dentro custa sanidade como uma fuga (*"expulsar n conta como jogador fugir"*).</summary>
	internal static bool ExpelirCustaSanidadeDeTeste;

	/// <summary>A insana nao passa as redeas pra IA: o jogador continua dirigindo o corpo louco.</summary>
	internal static bool LoucuraSemIaDeTeste;

	/// <summary>A mente do insano continua inteira: Sense e teletransporte funcionando (o Fragmented Mind esquecido).</summary>
	internal static bool MenteInteiraNaLoucuraDeTeste;

	/// <summary>A Sycophantic Rage cobra a sanidade e nao mexe no tiro.</summary>
	internal static bool FuriaSemTiroDeTeste;

	/// <summary>O Corrupted Majin com a Forma Pura masteriza no ritmo de todo mundo (o Echoed Insanity esquecido).</summary>
	internal static bool EcoSemFatorDeTeste;

	/// <summary>
	/// A saga anda direto no `_players.Values` dos relogios do corpo, sem a lista propria -- e a outra metade que nasce no fim da cena
	/// estoura o laco ("Collection was modified"), como na primeira rodada da `--diagsagamajin`.
	/// </summary>
	internal static bool SagaNoDicionarioDeTeste;

	// =====================================================================
	// QUEM
	// =====================================================================
	/// <summary>Este corpo e um Corrupted Majin -- o unico que anda na saga (`is_corrupted_majin()`, `MajinSaga.dm:43-44`).</summary>
	private static bool CorrompidoDaSaga(ServerPlayer pl) =>
		SagaDoMajin.EhCorrompido(pl.Race, pl.Ficha.ParentRace, pl.Ficha.Class);

	/// <summary>Este corpo destravou a Forma Pura -- e com ela o sistema inteiro da sanidade.</summary>
	internal static bool ComAFormaPura(ServerPlayer pl) => pl.Ficha.majin_pure_unlocked && CorrompidoDaSaga(pl);

	/// <summary>A sanidade acabou? (Dormindo tambem conta: a cabeca so volta no acordar.)</summary>
	internal static bool InsanoDaSaga(ServerPlayer pl) => ComAFormaPura(pl) && SagaDoMajin.Insano(pl.Ficha.majin_sanidade);

	/// <summary>
	/// O FRAGMENTED MIND -- *"Corrupted Majins who are insane lose the ability to use Ki Sense and all variants of Instant
	/// Transmission"*. Lido pelo `AplicarPoderes` (o bit do Sense) e pelos teletransportes do lote G11.
	/// </summary>
	internal static bool MenteFragmentada(ServerPlayer pl) => !MenteInteiraNaLoucuraDeTeste && InsanoDaSaga(pl);

	/// <summary>
	/// O ECHOED INSANITY -- *"Corrupted Majins master everything 20% slower"*: o fator de TODA maestria deste corpo (forma, ki
	/// divino, nivel de skill, Kaio-ken, divisao do corpo). Depois da Forma Pura, como o resto do sistema do amigo.
	/// </summary>
	internal static double FatorDeMaestriaDoCorpo(ServerPlayer pl) =>
		!EcoSemFatorDeTeste && ComAFormaPura(pl) ? SagaDoMajin.FatorDeMaestria : 1;

	/// <summary>
	/// Este Majin segura alguem dentro agora? A chave do `_dentroDoMajin` some quando a lista esvazia. O NPC devorado que voltou do
	/// disco no login conta (dono, 2026-10-23: "Conta") -- o `AcordarSemAbsorcao` recria o registro dele.
	/// </summary>
	private bool AlguemDentro(ServerPlayer pl) => _dentroDoMajin.ContainsKey(pl.Id);

	// =====================================================================
	// O ESTADO DE SESSAO -- o que nao vai pro disco
	// =====================================================================
	/// <summary>
	/// O QUE O MAJIN PURO GUARDA SO ENQUANTO ESTA NO MUNDO. A sanidade e a saga vao pro disco (`Fighter`); isto morre com a sessao
	/// -- e quem sai DORMINDO acorda na saida (<see cref="SairDoJogoSagaDoMajin"/>), pra o sono nao virar loucura eterna no relog.
	/// </summary>
	private sealed class MajinPuroVivo
	{
		/// <summary>Ate quando o corpo dorme (ms). Zero = acordado.</summary>
		public long DormeAte;

		/// <summary>Quando a Sycophantic Rage volta (ms).</summary>
		public long FuriaProntaEm;

		/// <summary>Por quanto o ataque de ki que estava carregando sai (1 = nada armado), e o desconto de Ki dele.</summary>
		public double FuriaNoTiro = 1, DescontoNoTiro;

		/// <summary>Quando o insano grita de novo (ms).</summary>
		public long ProximoGritoEm;

		/// <summary>O ultimo byte de sanidade que o cliente viu (-1 = nenhum), e se ele viu a loucura.</summary>
		public int SanidadeMandada = -1;
		public bool InsanoMandado;

		/// <summary>O relogio de um segundo: a porta da Forma Pura pelo BP e a outra metade que se refaz.</summary>
		public double RelogioLento;

		/// <summary>
		/// O DEGRAU CUJA CENA ESTA TOCANDO, esperando o fim dela pra fechar (0 = nenhum), e o marco que ele rompeu. Ver
		/// <see cref="FecharODegrau"/>.
		/// </summary>
		public int DegrauNoFimDaCena;
		public double MarcoDoDegrau;
	}

	private readonly Dictionary<int, MajinPuroVivo> _majinsPuros = [];

	private MajinPuroVivo VivoDoMajin(ServerPlayer pl)
	{
		if (!_majinsPuros.TryGetValue(pl.Id, out MajinPuroVivo? v)) _majinsPuros[pl.Id] = v = new MajinPuroVivo();
		return v;
	}

	/// <summary>O id saiu do mundo: o estado de sessao some com ele (inscrito no `InscreverEsquecimentos`).</summary>
	private void EsquecerOMajinPuro(int id)
	{
		_majinsPuros.Remove(id);
		_metadeVoltaEm.Remove(id);
	}

	/// <summary>Gancho de bancada: toda cena da saga que saiu (quem, qual). Nulo fora de teste.</summary>
	internal static List<(int Quem, Cinematicas.CenaMajin Cena)>? EscutaDeCenasDoMajin;

	/// <summary>Gancho de bancada: todo pacote de loucura que saiu (quem, sanidade, insano). Nulo fora de teste.</summary>
	internal static List<(int Quem, int Sanidade, bool Insano)>? EscutaDaLoucura;

	// =====================================================================
	// A SAGA -- os gatilhos
	// =====================================================================
	/// <summary>
	/// O QUE ACABOU DE SER ABSORVIDO MOVE A SAGA? -- as duas metades de `MajinSaga.dm`: o NPC devorado (`:145-148`, so o Kai) e o
	/// jogador selado (`:226-234`, o Kai e os tres da forma 3). Chamado pelas duas portas da absorcao, DEPOIS de o registro existir.
	///
	/// E ABSORVER TIRA DA FORMA PURA -- o dono: *"se ele absorver um player ou npc ele sai da pure form"*. Quem tira e o zelador: com
	/// alguem dentro, o piso deixa de ser a Pura.
	/// </summary>
	private void SagaAoAbsorver(ServerPlayer pl, ServerPlayer vitima, bool jogador)
	{
		if (!CorrompidoDaSaga(pl)) return;
		Fighter f = pl.Ficha;

		if (SagaDoMajin.EhKai(vitima.Race, vitima.Ficha.ParentRace) && !f.majin_kai_absorbed && f.majin_saga_form == 0)
		{
			f.majin_kai_absorbed = true;
			AvancarASaga(pl, 1);
		}
		else if (jogador && f.majin_saga_form == 3)
		{
			f.majin_form3_players++;
			Avisar(pl, $"jogadores consumidos nesta forma: {f.majin_form3_players}/{SagaDoMajin.JogadoresPraUltimaForma}.");   // `:233`
			if (f.majin_form3_players >= SagaDoMajin.JogadoresPraUltimaForma) AvancarASaga(pl, SagaDoMajin.UltimaForma);
			else Persistir(pl);
		}

		AjustarFormaDoMajin(pl);
	}

	/// <summary>
	/// A FURIA DA MORTE PARTE O MAJIN -- `if(M.is_corrupted_majin() &amp;&amp; M.majin_saga_form == 1) spawn M.majin_advance_form(2, ...)`
	/// (`Death/Death.dm:82`). So o caminho da MORTE (um amigo morto por um inimigo, na vista): ver um amigo ser absorvido acende
	/// a mesma furia (`MajinSaga.dm:173`) e NAO move a saga -- e o DM.
	///
	/// Pendurado no `LutoNaVizinhanca` FORA do teste de erupcao, como la: o `Do_Anger_Stuff` e a saga sao duas linhas
	/// independentes, e quem ja estava furioso tambem se parte.
	/// </summary>
	private void SagaNaFuriaDaMorte(ServerPlayer enlutado)
	{
		if (CorrompidoDaSaga(enlutado) && enlutado.Ficha.majin_saga_form == 1) AvancarASaga(enlutado, 2);
	}

	// =====================================================================
	// O DEGRAU -- `majin_advance_form()` (`MajinSaga.dm:410-429`)
	// =====================================================================
	/// <summary>
	/// O CORPO SOBE UM DEGRAU DA SAGA. Na ordem do bio-androide (`SubirDegrauDoBio`), que e a mesma do DM com o corpo trocado na
	/// virada: o estado inteiro agora (degrau, marco, `BPMod`, o indice do corpo), a CENA antes da aparencia nova (o cliente guarda
	/// o corpo novo ate o `Assumir`), e as frases do original.
	///
	/// O `majin_saga_busy` DO DM NAO VEIO: la ele impede um segundo degrau durante a cena; aqui cada gatilho e um degrau so e o
	/// estado e escrito de uma vez -- um segundo gatilho no meio da cena sobe o degrau seguinte e toca a cena dele por cima.
	/// </summary>
	private void AvancarASaga(ServerPlayer pl, int novo)
	{
		Fighter f = pl.Ficha;
		if (novo <= f.majin_saga_form || novo > SagaDoMajin.UltimaForma) return;

		f.majin_saga_form = novo;
		string marco = SagaDoMajin.MarcoDaForma(novo);
		double rompeu = marco.Length > 0 ? f.ReachMilestone(marco) : 0;
		f.BPMod += SagaDoMajin.PoderDeLutaPorDegrau;   // `genome.add_to_stat("Battle Power", 1)` (`:419`)

		// O CORPO DA SAGA -- `majin_apply_form_icon(newform)` (`:418`). Na ficha (e por ela no disco e na rede), e na roupa da ultima
		// refeicao tambem: aquela e uma COPIA da aparencia (`VestirAUltimaRefeicao`), e sem esta linha o Majin vestido com a roupa
		// de alguem continuaria no corpo velho ate a proxima absorcao.
		pl.Visual.Corpo = SagaDoMajin.CorpoDaForma(novo);
		if (pl.LookDoMajin != null) pl.LookDoMajin.Corpo = pl.Visual.Corpo;

		f.Statify();
		RepercutirPoder(pl);
		pl.SigAtributos = "";

		// A CENA ANTES DA APARENCIA: o cliente guarda o corpo novo ate a virada (`World._pendentes`).
		CenaDoMajin(pl, (Cinematicas.CenaMajin)novo);
		TrocarAparencias(pl);
		MandarFicha(pl);

		GD.Print($"[server] {pl.Name} (Corrupted Majin) subiu pra forma {novo} da saga"
				 + (rompeu > 0 ? $", marco {marco} = {rompeu:0.#}x" : "") + $", BPMod {f.BPMod:0.##}");

		// O FIM DA CENA FECHA O DEGRAU: as frases, a outra metade e a porta da Forma Pura vem DEPOIS do `MajinSagaCinematic` no DM
		// (`:414-429` -- la a cena e sincrona, com `sleep` dentro). Sem cena (um byte que o Core nao conhece), fecha na hora.
		MajinPuroVivo v = VivoDoMajin(pl);
		v.DegrauNoFimDaCena = novo;
		v.MarcoDoDegrau = rompeu;
		if (!EmCena(pl)) FecharODegrau(pl, v);

		Persistir(pl);
	}

	/// <summary>
	/// O FIM DA CENA DE UM DEGRAU -- o resto do `majin_advance_form` depois do `MajinSagaCinematic(newform, animstate)` (`:414-429`): as
	/// frases, o marco, a outra metade SALTANDO (`majin_spawn_clone()`, `:425`) e a porta da Forma Pura (`majin_check_pure_unlock()`,
	/// `:428`). No DM a cena e sincrona, entao tudo isso acontece com o corpo ja solto -- e a outra metade nasce com o mestre podendo
	/// se defender, e nao cacando um corpo preso numa cinematica de 25 s. Chamado pelo tique quando o prazo da cena acaba.
	///
	/// QUEM SAI DO JOGO NO MEIO DA CENA PERDE SO AS FRASES: a outra metade e a porta da Pura sao reconferidas pelo relogio de um
	/// segundo (<see cref="TickLentoDaSaga"/>) e pelo login (<see cref="PrepararOMajinPuroNoLogin"/>).
	/// </summary>
	private void FecharODegrau(ServerPlayer pl, MajinPuroVivo v)
	{
		int novo = v.DegrauNoFimDaCena;
		double rompeu = v.MarcoDoDegrau;
		v.DegrauNoFimDaCena = 0;
		v.MarcoDoDegrau = 0;
		if (novo == 0) return;

		// AS FRASES DO DM (`:421-429`), traduzidas.
		Avisar(pl, novo switch
		{
			1 => "a essência do Kai reescreve o seu corpo: você assume a sua primeira forma Majin de verdade.",
			2 => "a dor e a fúria partem o seu ser ao meio!",
			3 => "você devora a sua outra metade e explode em SUPER MAJIN!",
			_ => "três almas alimentam a sua ascensão: você chegou à sua forma final.",
		}, Protocol.Fala.AvisoDeRaiva);
		if (rompeu > 0) Avisar(pl, $"você rompeu um patamar: daqui pra frente treinar rende {rompeu:0.#}x.");

		if (novo == 2) SoltarAOutraMetade(pl);
		if (novo == SagaDoMajin.UltimaForma) ConferirAFormaPura(pl, calado: false);
	}

	/// <summary>
	/// A CENA DE UM DEGRAU DA SAGA, pra zona -- irma do `CenaDoBio`, pelo mesmo motivo escrito la: mandar o pacote e ANOTAR o prazo
	/// em que o corpo fica preso sao uma coisa so.
	/// </summary>
	private void CenaDoMajin(ServerPlayer pl, Cinematicas.CenaMajin qual)
	{
		pl.CenaSegundos = Cinematicas.DoMajin(qual)?.SegundosPreso ?? 0;
		EscutaDeCenasDoMajin?.Add((pl.Id, qual));

		var w = Protocol.Begin(Protocol.S2C.CenaDoMajin);
		w.Put(pl.Id);
		w.Put((byte)qual);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// A OUTRA METADE -- `mob/npc/MajinClone` e `majin_spawn_clone()` (`MajinSaga.dm:474-508`)
	// =====================================================================
	/// <summary>Uma outra metade viva: de quem ela e, e o BP em que ela foi PINADA (`clone_seed_bp`, `:481-487`).</summary>
	private sealed record OutraMetade(int Mestre, double Bp);

	/// <summary>As outras metades no mundo, pelo id do corpo delas.</summary>
	private readonly Dictionary<int, OutraMetade> _outrasMetades = [];

	/// <summary>Quando a outra metade de um mestre se refaz (ms) -- ver `SagaDoMajin.SegundosPraOutraMetadeVoltar`.</summary>
	private readonly Dictionary<int, long> _metadeVoltaEm = [];

	/// <summary>A outra metade viva deste mestre, ou nula.</summary>
	private ServerPlayer? OutraMetadeDe(int mestre)
	{
		foreach ((int id, OutraMetade m) in _outrasMetades)
			if (m.Mestre == mestre && _players.TryGetValue(id, out ServerPlayer? c)) return c;
		return null;
	}

	/// <summary>
	/// A OUTRA METADE SALTA DO CORPO. `makeCopy(2, Race, Class, /mob/npc/MajinClone, TRUE)` (`:490`): a copia de split
	/// (`CopyMaker.dm:34-46` -- METADE do poder expresso, os stats do dono), com o NOME do dono (`clone.name = name`), o corpo da
	/// FORMA 1 tingido pela mesma cor (`:496-497`), hostil ao mestre (`foundTarget(src)`, `:507`) e PINADA no BP com que nasceu.
	///
	/// O CEREBRO E O DE FABRICA: o `mob/npc/MajinClone` nao declara tempero (so `monster = 1`, que no DM e caçar direto), e o
	/// default e o literal -- como a copia do Split Form, que sai do mesmo `makeCopy`. E o `CopySkills(z)` do DM esta COMENTADO
	/// (`CopyMaker.dm:65`): a copia nao leva tecnica nenhuma, e aqui tambem nao.
	/// </summary>
	private void SoltarAOutraMetade(ServerPlayer pl)
	{
		if (SagaSemOutraMetadeDeTeste || OutraMetadeDe(pl.Id) != null) return;
		_metadeVoltaEm.Remove(pl.Id);
		Fighter d = pl.Ficha;

		// `step(clone, turn(dir, 180))` (`:504`): UM passo pras COSTAS do mestre, no chao livre mais proximo.
		Vec2 atras = pl.Pos - MeleeArea.Frente(pl.Facing) * ZoneCollision.TileSize;
		Vec2 onde = MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(atras) ?? atras;

		var metade = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = NomeVisivel(pl),
			Zone = pl.Zone,
			Pos = onde,
			Facing = pl.Facing,
			Race = pl.Race,
			Class = pl.Class,
			Genero = pl.Genero,
			Planeta = pl.Planeta,
			Idade = pl.Idade,
			Visual = VisualVisivel(pl).Copiar(),
			LastInputMs = NowMs(),
			Cerebro = new Cerebro(),
			Ficha = new Fighter(),
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		metade.Visual.Corpo = SagaDoMajin.CorpoDaForma(1);   // `clone.icon = 'Majin - Form 1.dmi'` (`:496`)

		Fighter c = metade.Ficha;
		c.Race = d.Race; c.Class = d.Class; c.Idade = d.Idade; c.ParentRace = d.ParentRace;
		c.BP = SagaDoMajin.BpDaOutraMetade(d.expressedBP);
		c.BPMod = d.BPMod;
		c.physoff = d.physoff; c.physdef = d.physdef; c.technique = d.technique;
		c.kioff = d.kioff; c.kidef = d.kidef; c.kiskill = d.kiskill; c.speed = d.speed; c.magiskill = d.magiskill;
		c.physoffMod = d.physoffMod; c.physdefMod = d.physdefMod; c.techniqueMod = d.techniqueMod;
		c.kioffMod = d.kioffMod; c.kidefMod = d.kidefMod; c.kiskillMod = d.kiskillMod; c.speedMod = d.speedMod; c.magiMod = d.magiMod;
		c.HP = 100;
		c.KO = false; c.dead = false;
		c.maxstamina = d.maxstamina; c.stamina = d.maxstamina;
		c.Statify();
		c.Ki = c.MaxKi;
		c.PowerLevel();

		PorNoMundo(metade);
		metade.Combate.Letal = false;   // `murderToggle` de um mob novo e 0: ela derruba, nao mata
		_outrasMetades[metade.Id] = new OutraMetade(pl.Id, c.BP);
		TrocarAparencias(metade);

		Falar(pl, Protocol.Fala.Emote, "se rasga por dentro -- um segundo Majin salta pra fora do corpo dele!");   // `:508`
		Avisar(pl, "a sua outra metade se soltou e quer você morto. Vença-a e ABSORVA-a de volta.", Protocol.Fala.AvisoDeRaiva);
		GD.Print($"[server] {pl.Name}: a outra metade nasceu (id {metade.Id}, BP {c.BP:N0} pinado)");
	}

	/// <summary>
	/// O RAMO DA OUTRA METADE NO `TicarUmCorpo`: a presa e o mestre, e so ele (`foundTarget(src)`, `MajinSaga.dm:507`). O BP fica
	/// pinado (o `NPCTicker()` do clone, `:483-487`). Sem mestre na zona ela fica parada -- e quem a tira do mundo quando o mestre sai
	/// do jogo e o <see cref="SairDoJogoSagaDoMajin"/>.
	/// </summary>
	private bool GuiarAOutraMetade(ServerPlayer npc, OutraMetade metade, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = npc.Pos;
		if (npc.Ficha.dead || npc.Ficha.KO) { npc.Moving = false; return false; }

		if (Math.Abs(npc.Ficha.BP - metade.Bp) > 0.5)
		{
			npc.Ficha.BP = metade.Bp;
			npc.Ficha.Statify();
			npc.Ficha.PowerLevel();
			npc.SigAtributos = "";
		}

		if (!_players.TryGetValue(metade.Mestre, out ServerPlayer? mestre) || mestre.Zone.Hash != npc.Zone.Hash || EhCadaver(mestre))
		{
			npc.Moving = false;
			return false;
		}
		presa = mestre;
		destino = mestre.Pos;
		return true;
	}

	/// <summary>
	/// ABSORVER A OUTRA METADE -- a primeira coisa do `majin_absorb` (`MajinSaga.dm:124-130`), antes de qualquer outro alvo. Devolve
	/// verdadeiro quando o gesto era sobre ela (vencida ou nao), e o verb para aqui.
	///
	/// `del(M)`: ela nao morre nem vira registro de absorvido -- ela VOLTA pra dentro. Sem bonus de BP, sem cadaver.
	/// </summary>
	private bool TentarAbsorverAOutraMetade(ServerPlayer pl)
	{
		ServerPlayer? metade = AlvoDeTecnica(pl, RaioDaAbsorcao,
			o => _outrasMetades.TryGetValue(o.Id, out OutraMetade? m) && m.Mestre == pl.Id);
		if (metade == null) return false;

		if (!OutraMetadeSemVencerDeTeste && !SagaDoMajin.OutraMetadeVencida(metade.Ficha.KO, metade.Ficha.dead, metade.Ficha.HP))
		{
			Avisar(pl, "você precisa vencer a sua outra metade antes de tomá-la de volta.");   // `:125`
			return true;
		}

		_outrasMetades.Remove(metade.Id);
		RemoverNpc(metade);
		Falar(pl, Protocol.Fala.Emote, "devora o Majin menor!");   // `:127`
		GD.Print($"[server] {pl.Name} absorveu a propria outra metade");
		if (CorrompidoDaSaga(pl) && pl.Ficha.majin_saga_form == 2) AvancarASaga(pl, 3);
		return true;
	}

	// =====================================================================
	// A FORMA PURA -- a porta (`majin_check_pure_unlock`, `MajinSaga.dm:511-521`)
	// =====================================================================
	/// <summary>
	/// A FORMA PURA DESTRAVA? Forma 4, ninguem dentro, BP base na porta. Chamado nos tres lugares do DM: ao chegar na forma 4
	/// (`:429`), ao soltar o ultimo absorvido (`:278`) e calado, periodicamente, pro BP que cruza a porta depois (`Stats.dm:61`).
	///
	/// A FRASE DO "AINDA NAO" FALA EM PROPORCAO, e nao em numero: o DM mostra `(BP base: X / 2.000.000.000)`, e a regra principal
	/// do BP deste port e que ninguem sabe numero de BP sem medidor.
	/// </summary>
	private void ConferirAFormaPura(ServerPlayer pl, bool calado)
	{
		Fighter f = pl.Ficha;
		if (f.majin_pure_unlocked || f.majin_saga_form != SagaDoMajin.UltimaForma || !CorrompidoDaSaga(pl)) return;
		if (AlguemDentro(pl)) return;   // *"precisa ter PERDIDO todos os absorvidos"*

		if (!SagaDoMajin.DestravaAFormaPura(f.majin_saga_form, f.majin_pure_unlocked, AlguemDentro(pl), f.BP))
		{
			if (!calado)
				Avisar(pl, "vazio de almas, você sente o próprio núcleo... mas ele ainda não basta pra Forma Pura "
						 + $"(o seu poder base está em {Math.Floor(100 * f.BP / SagaDoMajin.BpDaFormaPura):0}% do que ela pede).");
			return;
		}

		f.majin_pure_unlocked = true;
		f.majin_sanidade = SagaDoMajin.SanidadeCheia;
		GD.Print($"[server] {pl.Name}: a FORMA PURA destravou");
		Avisar(pl, "despido de tudo o que roubou, o seu próprio poder cru desperta: a FORMA PURA é sua.", Protocol.Fala.AvisoDeRaiva);   // `:521`
		Avisar(pl, "ela é o seu corpo agora -- só absorver alguém tira você dela. Viver nela gasta a sua SANIDADE (a tela "
				 + "escurece nas bordas); segurar alguém lá dentro a devolve. Ganhou a Sycophantic Rage (aba Skills) e o "
				 + "Mindless Gambit (passiva: o tronco aguenta mais quanto menos sanidade sobra).");
		MandarSkills(pl);   // a Sycophantic Rage entrou na lista de tecnicas
		Persistir(pl);
		AjustarFormaDoMajin(pl);
	}

	// =====================================================================
	// O ZELADOR DA FORMA -- a Forma Pura e o repouso
	// =====================================================================
	/// <summary>
	/// ONDE O CORPO DESTE MAJIN DESCANSA AGORA -- vazio = o repouso de sempre (o corpo de absorcao, a forma 4 da saga).
	///   * dormindo: o corpo apagou (vazio);
	///   * insano: a INSANA, e ela vence o resto (a loucura cospe quem estava dentro);
	///   * segurando alguem: o corpo de absorcao (vazio) -- *"se ele absorver um player ou npc ele sai da pure form"*;
	///   * sozinho: a Pura sa -- *"Corrupted Majin's have their base form replaced with Pure Form"*.
	/// </summary>
	private string PisoDoMajinAgora(ServerPlayer pl)
	{
		if (!ComAFormaPura(pl)) return "";
		if (_majinsPuros.TryGetValue(pl.Id, out MajinPuroVivo? v) && v.DormeAte != 0) return "";
		if (SagaDoMajin.Insano(pl.Ficha.majin_sanidade)) return Catalogo.IdDaFormaPuraInsana;
		if (AlguemDentro(pl)) return "";
		return Catalogo.IdDaFormaPura;
	}

	/// <summary>
	/// O ZELADOR: escreve o repouso imposto (`ServerPlayer.PisoDoMajin`, que o `Perfil` entrega como `PisoProprio`) e poe o corpo
	/// onde ele manda. Roda todo tique pra quem tem a Forma Pura, e de toda porta que muda a resposta (absorver, soltar, enlouquecer,
	/// dormir, acordar).
	///
	/// A SA ESPERA o corpo poder: caido ou preso numa cena, ela entra no tique em que ele levantar. A INSANA nao espera -- a loucura
	/// pega quem estiver como estiver (e quem esta caido dorme no tique seguinte).
	///
	/// FORMA DE FORA DA LINHA NAO E MEXIDA quando o piso e o corpo de absorcao: quem segura alguem e subiu numa forma por conta
	/// propria (um Mistico concedido, o ki divino) continua nela. O piso so manda no que e dele.
	/// </summary>
	private void AjustarFormaDoMajin(ServerPlayer pl)
	{
		if (!ComAFormaPura(pl)) { pl.PisoDoMajin = ""; return; }

		string piso = PisoDoMajinAgora(pl);
		pl.PisoDoMajin = piso;
		string atual = pl.Forma.Atual;

		if (piso.Length > 0)
		{
			if (atual == piso) return;
			bool insana = piso == Catalogo.IdDaFormaPuraInsana;
			if (!insana && (CaidoPraForma(pl) || EmCena(pl))) return;
			PorNaFormaDoMajin(pl, piso, semCena: insana,
				insana ? "" : "ninguém mais dentro de você: o corpo volta à FORMA PURA.");
			return;
		}

		if (Catalogo.FormaDoMajinPuro(pl.Forma.Def))
			PorNaFormaDoMajin(pl, Catalogo.IdBase, semCena: true,
				_majinsPuros.TryGetValue(pl.Id, out MajinPuroVivo? v) && v.DormeAte != 0
					? ""
					: "com alguém dentro de você, a Forma Pura cede ao corpo de absorção -- e a cabeça descansa.");
	}

	/// <summary>
	/// O CORPO VAI PRA ESTA FORMA, pelas mesmas tres chamadas de toda troca de forma (`Entrar`, `AplicarForma`, `AnunciarForma`) --
	/// e NAO pelo `EntrarNaForma` da escada, que ENCHE o tanque na estreia: a Pura do DM nao enche Ki nenhum (`majin_enter_pure`,
	/// `:531-540`), e a insana enchendo o tanque esticaria a loucura que o dono quer que acabe. E o desenho da Fury
	/// (`GameServer.KitDoDeus.EntrarNaFury`).
	/// </summary>
	private void PorNaFormaDoMajin(ServerPlayer pl, string para, bool semCena, string frase)
	{
		string antes = pl.Forma.Atual;
		bool estreia = pl.Forma.Entrar(para);
		if (para != Catalogo.IdDaFormaPuraInsana) pl.Forma.LoucuraSegundos = 0;
		AplicarForma(pl);
		if (frase.Length > 0) Avisar(pl, frase);
		if (estreia && para == Catalogo.IdDaFormaPura)
			AvisarPertoG3(pl, RaioDaVista, $"a carne de {NomeVisivel(pl)} ferve e encolhe -- um poder antigo e puro sobe à superfície!",
						  excetoCentro: true);   // `:564`
		GD.Print($"[server] {pl.Name}: {antes} -> {para} (saga do Majin)" + (estreia ? "  <- PRIMEIRA VEZ" : ""));
		AnunciarForma(pl, antes, para, estreia, semCena);
	}

	// =====================================================================
	// O RELOGIO -- a sanidade, a loucura e o sono
	// =====================================================================
	/// <summary>
	/// O TIQUE DO CORRUPTED MAJIN, junto dos outros relogios do corpo (`GameServer.RelogiosDoCorpo`): DEPOIS da forma (que cobra Ki e
	/// paga maestria), da fera e da furia (as possessoes) -- e ANTES de quem executa a IA, pra a loucura tomar o corpo no mesmo
	/// quadro em que acontece.
	///
	/// O CAMINHO DE QUASE TODO CORPO SAO DOIS CAMPOS: sem saga nem Forma Pura, nada aqui roda.
	/// </summary>
	private void TickDaLoucuraDoMajin(ServerPlayer pl, double dt)
	{
		Fighter f = pl.Ficha;
		if (f.majin_saga_form == 0 && !f.majin_pure_unlocked) return;
		if (!CorrompidoDaSaga(pl)) return;
		MajinPuroVivo v = VivoDoMajin(pl);

		// O DEGRAU QUE ESPERAVA A CENA ACABAR FECHA AQUI -- ver `FecharODegrau`.
		if (v.DegrauNoFimDaCena != 0 && !EmCena(pl)) FecharODegrau(pl, v);

		v.RelogioLento += dt;
		if (v.RelogioLento >= 1)
		{
			v.RelogioLento = 0;
			// COM UM DEGRAU ESPERANDO A CENA, o relogio lento espera junto: a outra metade ainda nao saltou (ela nao "sumiu"), e a porta
			// da Pura e do fim da cena.
			if (v.DegrauNoFimDaCena == 0) TickLentoDaSaga(pl);
		}

		if (!f.majin_pure_unlocked) return;

		if (v.DormeAte != 0)
		{
			if (NowMs() >= v.DormeAte) Acordar(pl, v);
		}
		else
		{
			bool eraInsano = SagaDoMajin.Insano(f.majin_sanidade);
			if (!EmCena(pl))
				f.majin_sanidade = SagaDoMajin.PassoDaSanidade(f.majin_sanidade, dt,
					naPuraSa: pl.Forma.Atual == Catalogo.IdDaFormaPura, alguemDentro: AlguemDentro(pl));
			if (!eraInsano && SagaDoMajin.Insano(f.majin_sanidade)) Enlouquecer(pl, v);
			else if (pl.Forma.Atual == Catalogo.IdDaFormaPuraInsana) TickDaInsana(pl, v, dt);
		}

		AjustarFormaDoMajin(pl);
		if (pl.Combate != null)
			pl.Combate.Corpo.ReducaoNoTronco = SagaDoMajin.ReducaoNoTronco(f.majin_sanidade, SagaDoMajin.Insano(f.majin_sanidade));
		pl.Niveis.FatorDeGanho = FatorDeMaestriaDoCorpo(pl);

		// A FURIA ARMADA SO VALE ENQUANTO O ATAQUE CARREGA: soltou (o raio acabou, a esfera saiu e passou), desarma.
		if (v.FuriaNoTiro > 1 && !CarregandoAtaqueDeKi(pl)) { v.FuriaNoTiro = 1; v.DescontoNoTiro = 0; }

		MandarALoucura(pl, v);
	}

	/// <summary>
	/// O RELOGIO DE UM SEGUNDO: a porta da Forma Pura que o BP cruza depois (`Stats.dm:61`, *"re-checa quando o BP cruza o gate
	/// DEPOIS de ja ter expelido todos"*), e a outra metade que sumiu sem ser absorvida.
	/// </summary>
	private void TickLentoDaSaga(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (f.majin_saga_form == SagaDoMajin.UltimaForma && !f.majin_pure_unlocked) ConferirAFormaPura(pl, calado: true);

		if (f.majin_saga_form != 2) return;
		foreach (int id in _outrasMetades.Keys.ToArray())
			if (!_players.ContainsKey(id)) _outrasMetades.Remove(id);
		if (OutraMetadeDe(pl.Id) != null) return;

		long agora = NowMs();
		if (!_metadeVoltaEm.TryGetValue(pl.Id, out long volta))
		{
			_metadeVoltaEm[pl.Id] = agora + (long)(SagaDoMajin.SegundosPraOutraMetadeVoltar * 1000);
			Avisar(pl, "a sua outra metade se foi sem voltar pra você -- ela se refaz em um minuto.");
			return;
		}
		if (agora >= volta && !CaidoPraForma(pl) && !EmCena(pl)) SoltarAOutraMetade(pl);
	}

	/// <summary>
	/// A SANIDADE ACABOU -- *"When insane they are forced into Pure Form (Insane) until they run out of Ki"*. A loucura cospe quem
	/// estava dentro (expulsar, e nao fuga: sem custo), o Sense apaga, a insana entra SEM cena e a IA assume -- o dono: *"ao ficar
	/// insano a IA controla o jogador ate ele recobrar a sanidade"*.
	/// </summary>
	private void Enlouquecer(ServerPlayer pl, MajinPuroVivo v)
	{
		if (AlguemDentro(pl)) CuspirTodosDoMajin(pl.Id, "a loucura cospe pra fora todo mundo que você segurava.");
		pl.Forma.LoucuraSegundos = 0;
		AplicarPoderes(pl);   // o Fragmented Mind: o bit do Sense apaga
		AjustarFormaDoMajin(pl);
		TomarAsRedeasDaLoucura(pl);
		v.ProximoGritoEm = NowMs() + 1000;

		Avisar(pl, "A SUA SANIDADE ACABOU. A Forma Pura INSANA toma o corpo -- você não controla mais o que ele faz.",
			   Protocol.Fala.AvisoDeRaiva);
		Avisar(pl, "o Ki cai sem voltar (o corpo não regenera); quando ele acabar, o corpo apaga e dorme dois minutos -- "
				 + "e você acorda com a cabeça no lugar.", Protocol.Fala.AvisoDeRaiva);
		AvisarPertoG3(pl, RaioDaVista, $"os olhos de {NomeVisivel(pl)} se esvaziam -- ele perdeu a razão!", excetoCentro: true);
		GD.Print($"[server] {pl.Name}: a sanidade acabou -- INSANO");
	}

	/// <summary>
	/// A IA ASSUME O CORPO LOUCO, pelo funil de toda posse (`AssumirOCorpo`). Idempotente: o relog de quem saiu insano passa por aqui
	/// no primeiro tique.
	///
	/// ============================ O TEMPERO E O DA FURIA LENDARIA, MAIS FURIOSO ============================
	///   * `VidaCautelosa = 0` e `Disciplina = 0` -- nao recua e nao apara: e um bicho;
	///   * `Inteligencia = 0` -- nao recarrega, nao decide transformar e nao recua pra atirar (ele ATIRA o que estiver na janela,
	///     como o "burro" do `Cerebro`);
	///   * `ChanceDePesado = 0.7` e `Agressividade = 1` -- bate forte e sempre: o dono pediu *"estilo kid boo"*. Escolha minha.
	/// ======================================================================================================
	/// </summary>
	private void TomarAsRedeasDaLoucura(ServerPlayer pl)
	{
		if (LoucuraSemIaDeTeste || pl.CerebroDaPosse != null || pl.DonoDoClone != 0) return;
		AssumirOCorpo(pl, new Cerebro
		{
			VidaCautelosa = 0,
			ChanceDePesado = 0.7,
			IntervaloDeDecisao = 0.2,
			Disciplina = 0,
			Inteligencia = 0,
			Agressividade = 1,
		});
	}

	/// <summary>
	/// O TIQUE DA INSANA: a subida (o `LoucuraSegundos` que o catalogo le), o DRENO que cresce com ela (*"faz o ki cair bem
	/// devagar"* -- e a regeneracao passiva esta desligada no `TickDaCarga`), os gritos, a furia que a IA usa -- e o fim: o Ki
	/// zerado (ou o corpo derrubado) apaga o corpo.
	/// </summary>
	private void TickDaInsana(ServerPlayer pl, MajinPuroVivo v, double dt)
	{
		Fighter f = pl.Ficha;
		if (f.KO || EhCadaver(pl) || f.Ki <= 0) { Adormecer(pl, v); return; }
		if (EmCena(pl)) return;

		pl.Forma.LoucuraSegundos += dt;
		double mult = MultiplicadorDaForma(pl);
		f.Ki = Math.Max(0, f.Ki - SagaDoMajin.DrenoInsano(mult) * f.MaxKi * dt);

		TomarAsRedeasDaLoucura(pl);

		long agora = NowMs();
		if (agora >= v.ProximoGritoEm)
		{
			Falar(pl, Protocol.Fala.Diz, SagaDoMajin.Gritos[_rng.Next(SagaDoMajin.Gritos.Length)]);
			v.ProximoGritoEm = agora + (long)((SagaDoMajin.GritoMinimo
				+ _rng.NextDouble() * (SagaDoMajin.GritoMaximo - SagaDoMajin.GritoMinimo)) * 1000);
		}

		// A FURIA DA IA: com um ataque carregando e a recarga vencida, o bicho a usa -- *"When insane, Sycophantic Rage increases
		// the power ... by 200% and reduces their Ki cost by 25%"*.
		if (agora >= v.FuriaProntaEm && CarregandoAtaqueDeKi(pl)) UsarAFuriaBajuladora(pl);
	}

	/// <summary>
	/// O CORPO APAGA -- *"then fall asleep for two minutes"*. O sono e o NOCAUTE do jogo (o corpo no chao, sem acao, a vista de
	/// todos), por dois minutos; a posse acaba (dormindo nao ha o que dirigir) e a insana cede ao corpo de absorcao.
	/// </summary>
	private void Adormecer(ServerPlayer pl, MajinPuroVivo v)
	{
		if (pl.CerebroDaPosse != null) DevolverAsRedeas(pl);
		v.DormeAte = NowMs() + (long)(SagaDoMajin.SegundosDormindo * 1000);
		pl.Forma.LoucuraSegundos = 0;
		if (!EhCadaver(pl))
		{
			if (!pl.Ficha.KO) pl.Combate.Nocautear(SagaDoMajin.SegundosDormindo);
			else pl.Combate.NocauteRestante = Math.Max(pl.Combate.NocauteRestante, SagaDoMajin.SegundosDormindo);
		}
		AjustarFormaDoMajin(pl);
		Avisar(pl, "o corpo APAGA: você cai num sono pesado (dois minutos).", Protocol.Fala.AvisoDeRaiva);
		Falar(pl, Protocol.Fala.Emote, "desaba no chão e apaga, roncando alto.");
		MandarFicha(pl);
		GD.Print($"[server] {pl.Name}: o insano apagou -- dorme {SagaDoMajin.SegundosDormindo:0} s");
	}

	/// <summary>*"wake up with 10% Ki and completely refilled Sanity Meter"* -- e o corpo volta pra Pura sa.</summary>
	private void Acordar(ServerPlayer pl, MajinPuroVivo v)
	{
		v.DormeAte = 0;
		Fighter f = pl.Ficha;
		f.majin_sanidade = SagaDoMajin.SanidadeCheia;
		if (f.KO) pl.Combate.Levantar();
		f.Ki = f.MaxKi * SagaDoMajin.KiAoAcordar;
		AplicarPoderes(pl);   // o Sense volta
		Avisar(pl, "você acorda com a cabeça no lugar: a sanidade voltou inteira (e o Ki em 10%).", Protocol.Fala.AvisoDeRaiva);
		AjustarFormaDoMajin(pl);
		MandarFicha(pl);
		Persistir(pl);
		GD.Print($"[server] {pl.Name}: acordou -- sanidade cheia, Ki em 10%");
	}

	/// <summary>
	/// A TELA DA LOUCURA -- o pacote `S2C.Loucura`, so pro dono, so quando muda de um ponto. Quem desenha e o `LoucuraNaTela`.
	/// Dormindo, o tunel apaga (o corpo dorme; a cabeca so volta no acordar, mas nao ha o que ver).
	/// </summary>
	private void MandarALoucura(ServerPlayer pl, MajinPuroVivo v)
	{
		int s = pl.Ficha.majin_pure_unlocked ? (int)Math.Round(Math.Clamp(pl.Ficha.majin_sanidade, 0, 100)) : 255;
		bool insano = s != 255 && SagaDoMajin.Insano(pl.Ficha.majin_sanidade) && v.DormeAte == 0;
		if (s == v.SanidadeMandada && insano == v.InsanoMandado) return;
		v.SanidadeMandada = s;
		v.InsanoMandado = insano;
		EscutaDaLoucura?.Add((pl.Id, s, insano));

		if (pl.Peer is not { } peer) return;
		var w = Protocol.Begin(Protocol.S2C.Loucura);
		w.Put((byte)s);
		w.Put(insano);
		peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// AS PORTAS DE FORA -- a absorcao, o login e o logout
	// =====================================================================
	/// <summary>
	/// UM ABSORVIDO SAIU DE DENTRO DESTE MAJIN. Chamado pelo `CuspirDoMajin` no fim, com a pergunta ja respondida: ERA O PRIMEIRO
	/// dos que estavam dentro, e saiu FUGINDO (venceu a imagem, morreu la dentro, o Majin caiu)? Entao a sanidade paga -- *"having
	/// their first absorb freed"*. Expulsar e sair do jogo nao sao fuga (*"expulsar n conta como jogador fugir pra ele perder
	/// sanidade"*).
	///
	/// E A LISTA QUE ESVAZIA CONFERE A FORMA PURA (`spawn majin_check_pure_unlock()`, `:278`) e devolve o corpo a ela.
	/// </summary>
	private void AoSoltarDoMajin(ServerPlayer majin, bool oPrimeiroFugiu)
	{
		if (oPrimeiroFugiu && ComAFormaPura(majin) && !SagaDoMajin.Insano(majin.Ficha.majin_sanidade))
		{
			majin.Ficha.majin_sanidade = Math.Max(0, majin.Ficha.majin_sanidade - SagaDoMajin.CustoDaFuga);
			Avisar(majin, $"o primeiro que você absorveu escapou -- e um pedaço da sua cabeça foi junto (sanidade -{SagaDoMajin.CustoDaFuga:0}).",
				   Protocol.Fala.AvisoDeRaiva);
		}
		if (!AlguemDentro(majin)) ConferirAFormaPura(majin, calado: false);
		AjustarFormaDoMajin(majin);
	}

	/// <summary>
	/// O LOGIN, ANTES DA FORMA VOLTAR DO DISCO: o repouso imposto tem que estar escrito quando o `RestaurarFormaEDisciplina` perguntar
	/// pelo piso -- o dono: *"quando ele entrar no jogo novamente ele vai estar na pure form novamente"*. Quem saiu insano volta
	/// insano (a sanidade foi pro disco), e a IA o assume no primeiro tique. Quem estava na forma 2 ganha a outra metade de volta.
	///
	/// (2026-10-23) SO SEM NINGUEM DENTRO: o NPC devorado volta com ele (o `AcordarSemAbsorcao`, que o `Entrar` chama ANTES disto) e
	/// conta como alguem dentro -- o dono: o Corrupted volta FORA da Forma Pura ate expelir, e a sanidade sobe com ele la dentro.
	/// </summary>
	private void PrepararOMajinPuroNoLogin(ServerPlayer pl)
	{
		pl.PisoDoMajin = ComAFormaPura(pl) ? PisoDoMajinAgora(pl) : "";
		if (CorrompidoDaSaga(pl) && pl.Ficha.majin_saga_form == 2) _metadeVoltaEm[pl.Id] = NowMs();
	}

	/// <summary>
	/// O LOGOUT, ANTES DO SAVE: quem sai DORMINDO acorda na saida (o sono e de sessao; sem isto ele relogaria com a sanidade em zero
	/// e enlouqueceria de novo, sem ter feito nada), e a outra metade sai do mundo junto com o mestre (volta no login).
	/// </summary>
	private void SairDoJogoSagaDoMajin(ServerPlayer pl)
	{
		if (_majinsPuros.TryGetValue(pl.Id, out MajinPuroVivo? v) && v.DormeAte != 0)
		{
			v.DormeAte = 0;
			pl.Ficha.majin_sanidade = SagaDoMajin.SanidadeCheia;
			pl.Ficha.Ki = pl.Ficha.MaxKi * SagaDoMajin.KiAoAcordar;
		}
		if (OutraMetadeDe(pl.Id) is { } metade)
		{
			_outrasMetades.Remove(metade.Id);
			RemoverNpc(metade);
		}
	}

	// =====================================================================
	// SYCOPHANTIC RAGE -- a ativa
	// =====================================================================
	/// <summary>O lote da ativa: um verb so. As linhas-espelho estao em `Core/Skills/Tecnicas.Portadas.cs`.</summary>
	private void RegistrarTecnicasDaSagaDoMajin()
	{
		IniciarLote("MajinPuro");
		Vivo(SagaDoMajin.VerboDaFuria, UsarAFuriaBajuladora);
	}

	/// <summary>
	/// HA UM ATAQUE DE KI CARREGANDO NESTE CORPO? Um raio na mao (carregando ou saindo), uma tecnica sustentada de pe (a Death Ball,
	/// a Genkidama, as rajadas) ou a carga de um tiro do kit do Deus -- a mesma pergunta do `charging || beaming` da esquiva do
	/// Instinto (`InstintoDesvia`).
	/// </summary>
	private bool CarregandoAtaqueDeKi(ServerPlayer pl) =>
		_canais.ContainsKey(pl.Id) || BlastingG12(pl.Id) || CarregandoTiroDoKit(pl.Id);

	/// <summary>
	/// A SYCOPHANTIC RAGE -- *"increase the power of their currently charging Ki Attacks by 100% ... no extra Ki cost, but instead at
	/// the cost of 50% of their sanity gauge"*; insano, *"by 200% and reduces their Ki cost by 25%"*; *"Cooldown: 1 Minute"*.
	///
	/// ARMA O ATAQUE QUE ESTA CARREGANDO: o tiro que ja saiu da mao (o raio sendo segurado) leva o fator agora; o que ainda vai sair
	/// (a carga fechando, a esfera sendo lancada, cada disparo da rajada) o leva no `Disparar` (<see cref="FuriaNoDisparo"/>). O
	/// fator multiplica o PODER do tiro -- o `A.BP = expressedBP * wavemult` do DM (`Projetil.Bp` e `MultDeOnda`), que e o que
	/// fere e o que empurra numa colisao.
	///
	/// SEM NADA CARREGANDO NAO HA O QUE ALIMENTAR: a recusa nao cobra sanidade nem recarga.
	/// </summary>
	private void UsarAFuriaBajuladora(ServerPlayer pl)
	{
		if (!ComAFormaPura(pl)) { Avisar(pl, "só o Corrupted Majin que destravou a Forma Pura tem essa fúria."); return; }
		MajinPuroVivo v = VivoDoMajin(pl);
		long agora = NowMs();
		if (agora < v.FuriaProntaEm) { Avisar(pl, $"a fúria ainda não voltou ({Math.Ceiling((v.FuriaProntaEm - agora) / 1000.0):0} s)."); return; }
		if (!CarregandoAtaqueDeKi(pl)) { Avisar(pl, "não há ataque de Ki carregando pra fúria alimentar."); return; }

		bool insano = SagaDoMajin.Insano(pl.Ficha.majin_sanidade);
		double fator = SagaDoMajin.FatorDaFuria(insano);
		double desconto = SagaDoMajin.DescontoDaFuria(insano);
		v.FuriaProntaEm = agora + SagaDoMajin.RecargaDaFuriaMs;
		if (!insano) pl.Ficha.majin_sanidade = Math.Max(0, pl.Ficha.majin_sanidade - SagaDoMajin.CustoDaFuria);

		if (!FuriaSemTiroDeTeste)
		{
			v.FuriaNoTiro = fator;
			v.DescontoNoTiro = desconto;
			if (_canais.TryGetValue(pl.Id, out CanalDeKi? canal))
			{
				if (canal.Raio is { } raio) { raio.Bp *= fator; raio.MultDeOnda *= fator; }
				canal.CustoPorCiclo *= 1 - desconto;
			}
		}

		Falar(pl, Protocol.Fala.Emote, insano
			? "uiva e despeja a própria loucura no ataque que segura!"
			: "range os dentes e despeja a própria sanidade no ataque que segura!");
		Avisar(pl, insano
			? "SYCOPHANTIC RAGE: o ataque sai com o TRIPLO de poder e custa um quarto a menos de Ki."
			: $"SYCOPHANTIC RAGE: o ataque sai com o DOBRO de poder (sanidade -{SagaDoMajin.CustoDaFuria:0}).",
			Protocol.Fala.AvisoDeRaiva);
		GD.Print($"[server] {pl.Name}: Sycophantic Rage x{fator:0.#}" + (insano ? " (insano)" : ""));
		MandarALoucura(pl, v);
	}

	/// <summary>
	/// O TIRO QUE SAI COM A FURIA ARMADA LEVA O FATOR -- chamado pelo `Disparar`, o funil de todo projetil. O fator vale enquanto o
	/// ataque carrega (`TickDaLoucuraDoMajin` desarma quando ele acaba), entao todo tiro desse ataque o leva: o raio que sai, cada
	/// disparo de uma rajada de pe.
	/// </summary>
	private void FuriaNoDisparo(ServerPlayer pl, Jandirus.Core.Combat.Projetil p)
	{
		if (_majinsPuros.Count == 0 || !_majinsPuros.TryGetValue(pl.Id, out MajinPuroVivo? v) || v.FuriaNoTiro <= 1) return;
		p.Bp *= v.FuriaNoTiro;
		p.MultDeOnda *= v.FuriaNoTiro;
	}
}
