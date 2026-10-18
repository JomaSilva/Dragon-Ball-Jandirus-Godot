using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A ABSORCAO DO BIO-ANDROIDE -- `Absorption.dm`, o galho `obj/Bio_Absorb`.
///
/// ============================ ELA E O MOTOR DA ESCADA, E NAO UM ENFEITE ============================
/// Sem este arquivo o bio-androide para no degrau IMPERFEITO pra sempre: semi-perfeito e perfeito
/// nao se treinam, nao se compram e nao vem do tempo -- eles vem de COMER gente
/// (`bio_note_absorb`, `DNALabs.dm:534`). Portar o resto do sistema sem isto teria entregue tres
/// quartos de uma escada, que e pior que nao entregar nada: o jogador ve o degrau acontecer uma vez
/// e acredita que os outros vem.
///
/// ============================ O BIO **CONSOME**; O MAJIN SELA ============================
/// `Absorption.dm:354-361` e o unico trecho do arquivo com um `if` de raca, e ele existe pra dizer
/// isso: pro bio-androide a vitima **morre na hora**, `buudead = 6` (fura ate o death-regen dela),
/// `killer_stuff` de verdade -- e por isso o `AbsorbBP` dele e PERMANENTE, porque nao ha o que
/// regurgitar. O Majin manda a vitima pra uma dimensao interna e pode cospi-la de volta.
///
/// **NAO HA `Expel` AQUI**, e a ausencia e a regra: o verb existe nas tres arvores de absorcao do
/// DM, e pro bio ele nunca tem o que expelir. Escreve-lo seria um botao que nao faz nada.
///
/// ============================ E O `AbsorbBP` ENTRA COMO **MEDIA**, NAO COMO SOMA ============================
/// `base.dm:110-111` -- com `AbsorbDeterminesBP` ligado, `tempBP = (tempBP + AbsorbBP) / 2`. E um
/// NERF deliberado, e o proprio DM explica na linha (`Absorption.dm:203-205`): *"You absorb to
/// compensate, not as a goddamn training method."* A conta ja estava portada em
/// `Fighter.PowerLevel` e **os dois campos nunca eram escritos por ninguem** -- este arquivo e o
/// consumidor que faltava.
/// ==========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Um tile e meio, o mesmo alcance das outras tecnicas de contato.</summary>
	private const float RaioDaAbsorcao = 1.5f * ZoneCollision.TileSize;

	/// <summary>
	/// `sleep(20)` do `absorbing` -- dois segundos entre uma absorcao e a proxima. Segura o duplo
	/// clique, que num verb que MATA nao e detalhe.
	/// </summary>
	private const long RecargaDaAbsorcao = 2_000;

	private readonly Dictionary<int, long> _prontoAbsorver = [];

	/// <summary>
	/// ABSORVER. O verb `Absorb` do `obj/Bio_Absorb`, que a skill `bioabsorb` ("Tail Absorb") da.
	///
	/// A LARVA NAO ABSORVE, e a recusa e literal do original (`Absorption.dm:111-113`): *"os orgaos
	/// de absorcao so se formam ao amadurecer"*. E ela e a primeira coisa checada de proposito --
	/// dizer "nao ha ninguem caido" pra uma larva ensinaria que basta achar alguem.
	/// </summary>
	/// <summary>
	/// O NUCLEO DAS DUAS ABSORCOES DO DM -- `absorb()` (`Absorption.dm:383-386`) e `Life_Suck`
	/// (`demon.dm`): o poder do outro em moeda neutra (`M.BP / M.BPMod`), remultiplicado pelo SEU
	/// `BPMod` so quando ele e mais forte que voce, dividido por `downscaler/upscaler` (mais fraco) ou
	/// por `downscaler` (mais forte), com o `capcheck` do absorvedor.
	///
	/// ============================ SAO DUAS REGRAS, E NAO UMA ============================
	/// Elas dividem SO esta conta. O `absorb()` soma o resultado no `absorbadd` (o `AbsorbBP` do port),
	/// com eficacia que decai 10% por 600 s, vitima inabsorvivel por 300 s e morta em 1 s
	/// (`Absorption.dm:373-377`) e piso `eff*(M.BP+M.absorbadd)*Anger` (`:387-388`); e chamado com
	/// (2,6) pelo Soul_Absorb (`demon.dm:43`) e pelas absorcoes de Majin/bio (`Absorption.dm:28,90`).
	/// O `Life_Suck` (`demon.dm`, verb proprio) soma direto no `BP` -- permanente --, sem eficacia,
	/// sem matar, com `Ki += M.MaxKi` e a tecnologia do alvo, e piso `(M.BP/2+M.absorbadd)*PowerPcnt
	/// *Anger`: a "(2,8)" dele e a divisao por 4 e por 8 escrita a mao. Unificar os dois numa funcao
	/// apagaria uma das duas regras; o que se unifica e a conta.
	///
	/// OS DOIS `if` DO DM SAO INDEPENDENTES e, no Life_Suck, o primeiro muda o `BP` antes de o segundo
	/// ser testado -- com o alvo pouco acima de voce, os dois pagavam. Aqui e um `else`: sao o mesmo
	/// caso visto de dois lados. O `BPMod` do alvo tem piso porque o DM divide sem se proteger do zero.
	/// ================================================================================
	/// </summary>
	private static double GanhoDeAbsorcao(Fighter f, Fighter m, double eficacia, double upscaler, double downscaler)
	{
		double modAlvo = Math.Max(m.BPMod, 0.1);
		return f.BP < m.BP
			? f.CapCheck(eficacia * (m.BP / modAlvo) * f.BPMod / (downscaler / upscaler))
			: f.CapCheck(eficacia * (m.BP / modAlvo) / downscaler);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--verbsteste`): a absorcao do bio nao pergunta quem aperta -- o que este port
	/// fez ate 2026-10-10. Falso em jogo, sempre.
	/// </summary>
	internal bool AbsorcaoSemPortaoDeTeste;

	/// <summary>O verb que a skill "Tail Absorb" (`/datum/skill/general/bioabsorb`) concede, como o extrator o chama.</summary>
	private const string VerboDaAbsorcaoDoBio = "Bio_Absorb";

	/// <summary>
	/// ============================ QUEM TEM OS ORGAOS -- E ESTE PORTAO NAO EXISTIA ============================
	/// No DM o verb mora num OBJETO (`obj/Bio_Absorb`, `Absorption.dm:108`) que so entra no inventario de
	/// quem aprende a skill da arvore racial (`bioandroid.dm:21-23`): quem nao tem o objeto nao tem o que
	/// chamar. O port trocou o objeto por um id no canal de habilidade (`absorver`) e a pergunta "quem
	/// aperta?" ficou so no registro de botao do CLIENTE -- o `UsarHabilidade` manda todo corpo que nao e
	/// Majin pra ca, e daqui pra baixo nada conferia a raca. Qualquer cliente mexido (ou qualquer tecla
	/// ligada a mao) fazia um HUMANO consumir um nocauteado: a vitima morre furando ate o death-regen, e
	/// o poder dela entra em quem apertou.
	///
	/// PELA RACA, que e a regra deste port (o botao sempre foi do bio, larva inclusive), **ou** pela skill
	/// comprada -- o meio-sangue de pai bio-androide a tem na arvore (`Halfbreed.dm:46-47`).
	/// ======================================================================================================
	/// </summary>
	private bool TemOrgaosDeAbsorcao(ServerPlayer pl) =>
		AbsorcaoSemPortaoDeTeste
		|| Jandirus.Core.Races.BioAndroids.EhBio(pl.Race)
		|| SabeTecnica(pl, VerboDaAbsorcaoDoBio);

	/// <summary>
	/// (2026-10-17) A ABSORCAO DE EXECUCAO -- o adendo do dono: *"After knocking an opponent into the death state, you can perform an execution
	/// by absorbing their life essence, causing their body to dry up and vanish. This action kills the opponent and restores 20% of your HP,
	/// Stamina, and Ki. However, this execution takes 5 seconds"*. O caido e o mesmo do DM (nocauteado e vivo); o que mudou: ela LEVA TEMPO
	/// (5 s parado junto dele, como a execucao do Ruthless Mind -- andar, cair ou ele levantar desfaz), devolve 20% em vez do corpo inteiro e
	/// do tanque cheio, e o corpo da vitima some sem cadaver. A media do `AbsorbBP` continua (o DM; o adendo nao fala dela). O prazo e o
	/// que ela devolve moram no Core (`BioAndroids.SegundosDaExecucao`, `DevolveAExecucao`): a tela seca o corpo no mesmo prazo.
	/// </summary>
	private sealed class ExecucaoDoBio
	{
		public int Alvo;
		public long FechaEm;
		public Vec2 De;
	}

	/// <summary>As execucoes de absorcao correndo, por quem absorve.</summary>
	private readonly Dictionary<int, ExecucaoDoBio> _execucoesDoBio = [];

	/// <summary>(2026-10-17) Quando cada bio pode furar de novo (`BioAndroids.SegundosEntreEstocadas`).</summary>
	private readonly Dictionary<int, long> _prontoEstocar = [];

	/// <summary>
	/// (2026-10-17) A ABSORCAO DE COMBATE -- o adendo do dono, em resumo: depois de agarrar, a skill absorve vida, Ki e folego; cada uso
	/// devolve 5% do tronco, do Ki e do folego; cada estocada fura um membro, na ordem braco esquerdo, braco direito, perna esquerda, perna
	/// direita, cabeca e por fim o tronco; membro mecanico (o androide de absorcao, e nao o de energia infinita) e imune.
	///
	/// O MEMBRO e o da ordem (`BioAndroids.MembroDaEstocada`); o dano vai pelo funil do dano direto (`CombatState.Ferir`, com a cauda de sempre --
	/// nocaute, morte, Zenkai, rancor: `ConsequenciaDoDano`), letal ou nao pela escolha de quem fura, e a estocada e contada pra zona como um
	/// golpe (`AnunciarGolpe`: a faisca, o som, e o numero pros dois). Apertar dentro do segundo nao fura e nao fala.
	/// </summary>
	private void EstocadaDeAbsorcao(ServerPlayer pl, ServerPlayer alvo)
	{
		long agora = NowMs();
		if (_prontoEstocar.TryGetValue(pl.Id, out long pronta) && agora < pronta) return;
		if (alvo.Ficha.AndroideAbsorcao)
		{
			Avisar(pl, $"a cauda bate em metal: o corpo de {alvo.Name} é máquina por dentro -- não há essência nenhuma pra beber.");
			return;
		}
		Jandirus.Core.Combat.CombatState? c = alvo.Combate;
		if (c == null || c.Intocavel || CaidoPelaMorte(alvo)) return;
		if (Jandirus.Core.Races.BioAndroids.MembroDaEstocada(c.Corpo) is not { } membro)
		{
			Avisar(pl, $"não sobrou nada em {alvo.Name} pra cauda beber.");
			return;
		}
		_prontoEstocar[pl.Id] = agora + (long)(Jandirus.Core.Races.BioAndroids.SegundosEntreEstocadas * 1000);

		bool quebradoAntes = membro.Quebrado, decepadoAntes = membro.Decepado, caidoAntes = alvo.Ficha.KO, mortoAntes = alvo.Ficha.dead;
		double dano = membro.VidaMax * Jandirus.Core.Races.BioAndroids.FracaoDaEstocada;
		c.EntrarEmCombate(comAlguem: true);
		pl.Combate?.EntrarEmCombate();
		c.Ferir(membro, dano, pl.Combate?.Letal == true);
		c.SincronizarVida();

		// O QUE A CAUDA BEBE -- 5% do Ki e do folego de quem e furado...
		Fighter v = alvo.Ficha;
		v.Ki = Math.Max(0, v.Ki - v.MaxKi * Jandirus.Core.Races.BioAndroids.DrenoDaEstocada);
		v.stamina = Math.Max(0, v.stamina - v.maxstamina * Jandirus.Core.Races.BioAndroids.DrenoDaEstocada);
		// ...e o que ela devolve: 5% do tronco, do Ki e do folego de quem fura. O teto e o do port (quem ja estava acima fica onde estava).
		Fighter f = pl.Ficha;
		double volta = Jandirus.Core.Races.BioAndroids.DevolveAEstocada;
		if (pl.Combate?.Corpo.Achar(Jandirus.Core.Combat.Body.NomeDoTronco) is { Decepado: false } tronco)
		{
			tronco.Vida = Math.Min(tronco.VidaMax, tronco.Vida + tronco.VidaMax * volta);
			pl.Combate.SincronizarVida();
		}
		f.Ki = Math.Min(Math.Max(f.MaxKi, f.Ki), f.Ki + f.MaxKi * volta);
		f.stamina = Math.Min(Math.Max(f.maxstamina, f.stamina), f.stamina + f.maxstamina * volta);

		MarcarAgressao(alvo, pl);
		ConsequenciaDoDano(alvo, pl, "furado pela cauda");
		AnunciarGolpe(pl, alvo, new Jandirus.Core.Combat.GolpeResultado
		{
			Desfecho = Jandirus.Core.Combat.Desfecho.Acertou, Dano = dano, Membro = membro.Nome,
			Quebrou = !quebradoAntes && membro.Quebrado, Decepou = !decepadoAntes && membro.Decepado,
			Nocauteou = !caidoAntes && v.KO, Morreu = !mortoAntes && v.dead,
		}, nivel: 2);
		Falar(pl, Protocol.Fala.Emote, $"crava a cauda em {alvo.Name} ({Jandirus.Core.Combat.DebuffsDeMembro.NomeNaTela(membro.Nome).ToLowerInvariant()}) e bebe!");
		AvisarSePessoa(alvo, $"a cauda de {pl.Name} fura {MembroNaFrase(membro.Nome, seu: true)} -- e bebe a sua energia.");
	}

	/// <summary>
	/// ESTE CORPO E A VITIMA DE UMA EXECUCAO CORRENDO? -- a `Ocupacao.Secando` do snapshot (`OcupacaoDe`), que a tela desenha. De graca
	/// quando ninguem absorve ninguem, que e quase sempre.
	/// </summary>
	private bool SendoBebido(int id)
	{
		if (_execucoesDoBio.Count == 0) return false;
		foreach (ExecucaoDoBio e in _execucoesDoBio.Values)
			if (e.Alvo == id) return true;
		return false;
	}

	private void AbsorverBio(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;

		if (!TemOrgaosDeAbsorcao(pl)) { Avisar(pl, "seu corpo nao tem como absorver ninguem."); return; }

		// A LARVA NAO ABSORVE (`Absorption.dm:111-113`) -- a recem-nascida e (2026-10-17) a vestida: o dono, *"A forma de barata do bio androide
		// n consegue absorver"*.
		if (Jandirus.Core.Races.BioAndroids.EmLarva(f))
		{
			Avisar(pl, "a carapaça larval não tem os órgãos de absorção: amadureça primeiro (enterre-se).");
			return;
		}
		if (f.KO || f.dead) { Avisar(pl, "nao da, caido."); return; }
		if (RecusarAbsorcaoNaMente(pl)) return;

		// (2026-10-17) COM ALGUEM NA MAO, A ABSORCAO E DE COMBATE: a cauda fura um membro e bebe (`EstocadaDeAbsorcao`, o adendo do dono). Sem
		// ninguem na mao, e a execucao do caido -- quem quer executar o que segura, solta antes.
		if (QuemEuSeguro(pl) is { } preso) { EstocadaDeAbsorcao(pl, preso); return; }
		if (_execucoesDoBio.ContainsKey(pl.Id)) { Avisar(pl, "você já está absorvendo alguém."); return; }

		long agora = NowMs();
		if (_prontoAbsorver.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{ Avisar(pl, "seu corpo ainda esta absorvendo a ultima."); return; }

		// A VITIMA TEM QUE ESTAR NOCAUTEADA E VIVA -- `if(M.KO && !M.dead)`. Morto nao serve: nao ha biomassa viva pra reescrever. E TEM QUE
		// SER OUTRO CORPO: o reflexo da mente e a copia do Split Form nao sao biomassa de ninguem (`CorpoDerivado`, `GameServer.Validacoes.cs`).
		ServerPlayer? alvo = AlvoDeTecnica(pl, RaioDaAbsorcao, o => o.Ficha.KO && !o.Ficha.dead && !CorpoDerivado(o));
		if (alvo == null)
		{ Avisar(pl, "o alvo precisa estar NOCAUTEADO, vivo, e ao seu lado."); return; }

		// O ANDROIDE DE ABSORCAO E MAQUINA POR DENTRO (o dono: *"nao funciona em androides de absorçao de energia, mas em androides de energia
		// infinita funciona"*). O de energia infinita passa -- e e o atalho da escada (`ContarAbsorcaoDoBio`).
		if (alvo.Ficha.AndroideAbsorcao)
		{ Avisar(pl, $"o corpo de {alvo.Name} é máquina por dentro -- não há essência nenhuma pra beber."); return; }

		_execucoesDoBio[pl.Id] = new ExecucaoDoBio
		{
			Alvo = alvo.Id,
			FechaEm = agora + (long)(Jandirus.Core.Races.BioAndroids.SegundosDaExecucao * 1000),
			De = pl.Pos,
		};
		MarcarAgressao(alvo, pl);
		Falar(pl, Protocol.Fala.Emote, $"crava a cauda em {alvo.Name} e começa a beber a essência dele...");
		Avisar(pl, $"você crava a cauda em {alvo.Name}: {Jandirus.Core.Races.BioAndroids.SegundosDaExecucao:0} s PARADO junto dele -- andar, "
					  + "cair ou ele se levantar desfaz.");
		Avisar(alvo, $"a cauda de {pl.Name} atravessa o seu corpo -- e ele começa a te SECAR por dentro.");
	}

	/// <summary>
	/// AS EXECUCOES DE ABSORCAO CORRENDO, a cada tique (um passo desfaz na hora): desfeitas se quem absorve sair do lugar ou cair, ou se a
	/// vitima levantar, morrer, sumir ou se afastar; consumadas no prazo.
	/// </summary>
	private void TickDasExecucoesDoBio()
	{
		if (_execucoesDoBio.Count == 0) return;
		long agora = NowMs();
		foreach ((int id, ExecucaoDoBio e) in _execucoesDoBio.ToList())
		{
			_players.TryGetValue(id, out ServerPlayer? pl);
			_players.TryGetValue(e.Alvo, out ServerPlayer? alvo);
			bool desfez = pl == null || alvo == null || pl.Ficha.KO || pl.Ficha.dead
								  || !alvo.Ficha.KO || alvo.Ficha.dead || alvo.Zone.Hash != pl.Zone.Hash
								  || Vec2.Distance(pl.Pos, e.De) > 1 || Vec2.Distance(alvo.Pos, pl.Pos) > 2 * ZoneCollision.TileSize;
			if (desfez)
			{
				_execucoesDoBio.Remove(id);
				if (pl != null) Avisar(pl, "a absorção se desfaz.");
				continue;
			}
			if (agora < e.FechaEm) continue;
			_execucoesDoBio.Remove(id);
			ConsumirPelaExecucao(pl!, alvo!);
		}
	}

	/// <summary>A EXECUCAO SE CONSUMA: a vitima seca, morre e some; quem bebeu recupera 20% de tudo e anda na escada.</summary>
	private void ConsumirPelaExecucao(ServerPlayer pl, ServerPlayer alvo)
	{
		Fighter f = pl.Ficha;
		_prontoAbsorver[pl.Id] = NowMs() + RecargaDaAbsorcao;

		// ============================ `AbsorbDeterminesBP = 1` -- E ELE E PRA SEMPRE ============================
		// O DM o liga dentro do proprio verb (`Absorption.dm:114`), toda vez. Ligado uma vez, ele muda a conta de poder do corpo pro resto
		// da vida: o BP absorvido deixa de SOMAR e passa a tirar MEDIA com o proprio. Nao ha desligar, e nao deveria haver.
		// ====================================================================================================
		f.AbsorbDeterminesBP = true;

		double bpAlvo = alvo.Ficha.BP;
		f.AbsorbBP += bpAlvo;

		// O QUE A EXECUCAO DEVOLVE (o dono): 20% da vida, do folego e do Ki -- e nao mais o corpo inteiro e o tanque cheio do DM
		// (`usr.SpreadHeal(100,1,0)` + `Ki = MaxKi`). O teto e o do PORT, o do `SugarVidaG4`: quem ja estava acima do maximo fica onde estava.
		if (pl.Combate is { } c)
		{
			foreach (Jandirus.Core.Combat.BodyPart parte in c.Corpo.Partes)
				if (!parte.Decepado) parte.Vida = Math.Min(parte.VidaMax, parte.Vida + parte.VidaMax * Jandirus.Core.Races.BioAndroids.DevolveAExecucao);
			c.SincronizarVida();
		}
		f.Ki = Math.Min(Math.Max(f.MaxKi, f.Ki), f.Ki + f.MaxKi * Jandirus.Core.Races.BioAndroids.DevolveAExecucao);
		f.stamina = Math.Min(Math.Max(f.maxstamina, f.stamina), f.stamina + f.maxstamina * Jandirus.Core.Races.BioAndroids.DevolveAExecucao);
		f.CurrentNutrition = Math.Min(f.CurrentNutrition + 50, Nutricao.Tanque(f.Metabolism));

		Falar(pl, Protocol.Fala.Emote, $"bebe {alvo.Name} até a última gota -- o corpo dele SECA e se desfaz em pó!");
		Avisar(alvo, $"cada célula do seu corpo é bebida por {pl.Name}. Você seca... e morre.");

		// ---- A VITIMA MORRE, E O CORPO SOME ----
		// `ignorarSeguro: true` e o `buudead = 6` do original ("consumido ate a ultima celula: nem death-regen salva"). PELA PORTA
		// (`Combate.Morrer`) E COM O FUNIL DE DERROTA (`AoPerderALuta`): dali saem o relogio do cadaver, a viagem pro Outro Mundo, o Zenkai,
		// o LUTO de quem estava vendo e a sucessao de trono. E SEM CADAVER (`SumiuAbsorvido`): o NPC sai no dreno do fim do tique, o jogador
		// viaja sem deixar o corpo no chao (`IrProAlem`).
		alvo.SumiuAbsorvido = true;
		bool morreu = alvo.Combate?.Morrer(ignorarSeguro: true) ?? false;
		if (morreu) AoPerderALuta(alvo, pl, morreu: true);
		if (morreu && SomeAoSerAbsorvido(alvo) && !_npcsPraTirar.Contains(alvo)) _npcsPraTirar.Add(alvo);
		// O JOGADOR SECO VIAJA JA, e nao depois dos 2 s no chao (`Alem.MsNoChao`): o corpo dele secou e se desfez -- o adendo: *"dry up and
		// vanish"* --, e os 5 s da execucao ja foram a cena da morte. O NPC sai no dreno deste mesmo tique; o jogador, na triagem do seguinte.
		else if (morreu && EhJogador(alvo)) alvo.RelogioDaMorte = NowMs();

		f.Statify();
		RepercutirPoder(pl);

		// O NUMERO E O BP INTEIRO DA VITIMA, e so sai pra quem tem scouter (a regra principal do BP; ver `PoderNaFrase`). Sem ele a frase
		// diz a REGRA -- entrou o poder dele todo (`AbsorbBP += alvo.BP`).
		string absorvido = Jandirus.Core.Stats.PoderNaFrase.EmNumero(TemScouter(pl))
			? $"+{bpAlvo:N0} de poder absorvido"
			: $"{Jandirus.Core.Stats.PoderNaFrase.ParteDoPoderDele(bpAlvo, bpAlvo)} absorvido";
		Avisar(pl, $"você absorve {alvo.Name} por inteiro ({absorvido} -- "
				   + "e ele conta pela MÉDIA com o seu, não pela soma). Vida, fôlego e Ki: +20%.");

		// ---- E SO ENTAO A ESCADA ----
		// Depois da morte e depois do poder, como no DM (`Absorption.dm:143`): a evolucao e um EVENTO em cima de uma absorcao que ja aconteceu.
		ContarAbsorcaoDoBio(pl, alvo);
		Persistir(pl);

		GD.Print($"[server] {pl.Name} ABSORVEU {alvo.Name}: +{bpAlvo:0} AbsorbBP "
					+ $"(degrau {f.bio_stage}, marco {f.bio_marco}, passos {f.bio_passos:0.##})");
	}

	// =====================================================================
	// O SUPER SAIYAJIN 2 DO BIO-ANDROIDE -- `bio_ssj2_death_check` (`DNALabs.dm:649-700`)
	// =====================================================================
	/// <summary>
	/// ELE NAO DESPERTA PELA RAIVA: ELE DESPERTA MORRENDO.
	///
	/// ============================ ESTA E A UNICA PORTA, E O DM FECHA AS OUTRAS DUAS ============================
	/// O bio de laboratorio nasce com o SSJ1 (`canSSJ` + `hasssj`) mas o SSJ2 dele tem guarda propria
	/// em TRES lugares no original, todos dizendo a mesma coisa por caminhos diferentes:
	///   * `SSj2()` (`supersaiyanbuff.dm:417-421`) **zera** um `hasssj2` concedido por engano pela via
	///     saiyajin e recusa: *"seu nucleo bio exige um gatilho mais extremo que a raiva"*;
	///   * o ensino Mestre-Aluno recusa (`MasterStudent.dm:255, 338`);
	///   * o login REVOGA retroativamente quem pegou pela raiva (`DNALabs.dm:714-717`).
	///
	/// No port nao ha `hasssj2` solto pra vazar: a forma se libera por `EstadoDeForma.Liberar`, e o
	/// unico lugar do jogo inteiro que libera `ssj2` pra um bio e esta funcao. As tres guardas do DM
	/// existem porque la a var podia ser escrita de fora; aqui a porta e uma so, e ela e esta.
	///
	/// **E POR ISSO O SSJ3 DELE FICA ATRAS DESTA MESMA PORTA**, sem uma linha a mais: o SSJ3 pede 50%
	/// de maestria no SSJ2 (`FormaDef.PedeMaestriaDe`), e nao se treina uma forma que nao se tem.
	/// =========================================================================================================
	///
	/// PENDURADO NO `NegarMorte`, que e a porta unica da morte -- entao vale contra soco, contra ki,
	/// contra explosao em area e contra a Final Explosion, sem uma linha em cada um. E o que ele
	/// devolve nao e "eu te salvei": e "esta morte virou outra coisa", que e literalmente o que o
	/// `Death()` do DM faz ao abortar no `if` do topo.
	/// </summary>
	private bool DespertarSsj2DoBio(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;

		// A ORDEM E A DO ORIGINAL, e ela e barata primeiro: os tres testes de bit antes dos dois que
		// leem tabela.
		if (!f.bio_lab_born || !f.bio_saiyan_dna || !f.canSSJ) return false;
		if (f.bio_ssj2_by_death) return false;                       // uma vez na vida
		if (Jandirus.Core.Races.BioAndroids.EmLarva(f)) return false;   // (2026-10-17) a larva esta selada: nem a morte abre forma nela
		if (f.bio_stage < Jandirus.Core.Races.BioAndroids.Perfeito) return false;  // a FORMA PERFEITA
		if (pl.Forma.Maestria.De("ssj1") < 100) return false;        // SSJ1 100% dominado

		// O PODER: a MESMA regua da via saiyajin (`BP >= ssj2at / ssjmult`). Sai do limiar PESSOAL
		// ja sorteado no nascimento, pela mesma funcao que a tecla C usa -- um numero escrito aqui
		// seria a segunda verdade sobre onde o SSJ2 comeca.
		Jandirus.Core.Forms.FormaDef? ssj2 = Jandirus.Core.Forms.Catalogo.Def("ssj2");
		if (ssj2 == null) return false;
		double porta = pl.Forma.Limiares?.Porta(ssj2) ?? ssj2.PortaBp;
		if (f.BP < porta) return false;

		// MORRER DENTRO DA PROPRIA CABECA NAO CONTA (`z != mind_z`). Sem esta linha o despertar mais
		// caro do bio sairia de graca: entre em transe, perca pro proprio reflexo, saia com o SSJ2.
		// E o mesmo corte que o `AoPerderALuta` ja faz pelo Zenkai e pelo luto, e pelo mesmo motivo.
		if (NaMente(pl)) return false;

		// ---- A MORTE VIROU O DESPERTAR ----
		f.bio_ssj2_by_death = true;

		// O CORPO SE RECOMPOE NO LUGAR (`bio_ssj2_awaken`): cura total, Ki e folego cheios, de pe.
		pl.Combate?.Corpo.Restaurar();
		pl.Combate?.SincronizarVida();
		f.KO = false;
		f.Ki = f.MaxKi;
		f.stamina = f.maxstamina;

		// O DESCONTO DE DESPERTAR -- `ssj2at /= 2`, o MESMO da via saiyajin. Ele existe porque a
		// porta e calibrada pra quem sobe treinando; quem chegou la morrendo ja pagou o preco.
		if (pl.Forma.Limiares is { } lim) lim.ssj2at /= 2;

		// ============================ ELE VOLTA A VIDA **JA NO SSJ2**, E ISSO E O PEDIDO ============================
		// O dono: *"ele vai CANCELAR A MORTE e voltar a vida de forma INSTANTANEA so q agr no SSJ2
		// (super perfeito)"*. Aqui morava `pl.Forma.Liberar("ssj2")` -- que DESTRAVA a forma e nao a
		// veste. O DM nao para nesse meio termo: `bio_ssj2_awaken` termina em `SSj2()`
		// (`DNALabs.dm:697`), que e a proc que aplica a forma de verdade, e o comentario da linha diz
		// por que ela passa (*"o guard interno passa: bio_ssj2_by_death=1"*).
		//
		// A auditoria anterior deu VERDE nisto porque carimbava `Despertou("ssj2")` -- a pergunta que o
		// `Liberar` responde. Quem morresse com os requisitos se recompunha na BASE, com um item novo no
		// menu de formas. `Entrar` ja LIBERA por dentro (`EstadoDeForma.Entrar`), entao a linha some em
		// vez de virar duas.
		//
		// **SEM CENA, E ISSO TAMBEM E DO DM.** A cinematica saiyajin do SSJ2 e pulada pro bio -- ele tem
		// a propria, de 8,0 s, que este mesmo metodo dispara logo abaixo. `semCena: true` e o unico
		// caminho do port pra "esta forma chegou sem a cena dela", e ele existe desde a queda do Oozaru
		// Dourado; a alternativa era o jogador assistir a estreia do Super Saiyajin 2 saiyajin por cima
		// da propria recomposicao.
		//
		// E PELO `AnunciarForma` E NAO PELO `SincronizarFormas`: aquele diz "ele ESTA em" pra quem
		// chega, este diz "ele MUDOU pra" pra zona inteira -- e e o funil por onde passam o clima da
		// transformacao, o `mst_note_form` (quem VIU a forma pode ser ensinado nela) e o prazo da cena.
		// Com o `Sincronizar` no lugar dele, ninguem em volta via a forma acontecer.
		// =========================================================================================================
		string antes = pl.Forma.Atual;
		bool estreia = pl.Forma.Entrar("ssj2");
		double marco = f.ReachMilestone("bio_ssj2");

		f.Statify();
		AplicarForma(pl);
		pl.SigAtributos = "";
		AnunciarForma(pl, antes, "ssj2", estreia, semCena: true);

		// A CENA CURTA DO BIO -- `bio_ssj2_awaken` (`DNALabs.dm:672-698`): 8,0 s de raios, poeira e
		// tremor, sem folha de transformacao nenhuma. Ela sai DEPOIS do anuncio de proposito: o corpo
		// ja tem que estar em SSJ2 quando a cena comeca, porque o que ela mostra e a recomposicao de
		// alguem que JA voltou -- e nao uma transformacao em curso.
		CenaDoBio(pl, Jandirus.Core.Forms.Cinematicas.CenaBio.Ssj2PelaMorte);

		Persistir(pl);

		GD.Print($"[server] {pl.Name}: MORREU e despertou o SSJ2 do bio-androide (BP {f.BP:N0})");

		Avisar(pl, "seu corpo destrocado se RECOMPOE -- o nucleo bio-androide se recusa a morrer. "
				   + "Relampagos DOURADOS rasgam a carapaca: a MORTE acendeu o que faltava no DNA "
				   + "Saiyajin. O SUPER SAIYAJIN 2 e seu.");
		if (marco > 0) Avisar(pl, $"voce rompeu um patamar: daqui pra frente treinar rende {marco:0.#}x.");

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl) Avisar(o, $"o corpo destrocado de {pl.Name} esta se RECOMPONDO -- e ha "
								   + "relampagos dourados nas rachaduras da carapaca.");
		return true;
	}
}
