namespace Jandirus.Core.Ai;

/// <summary>
/// QUANDO UM CORPO DIRIGIDO ABRE A BOCA -- e o `npc_combat_chat` (`NPCAI.dm:227`) mais o
/// `npc_resource_warnings` (`:322`), que eram o maior buraco de fidelidade que a medicao achou.
///
/// ============================ POR QUE ISTO E A COISA DE MAIOR RETORNO POR LINHA ============================
/// O criterio do dono e literal: *"pra parecer q sao inteligentes ... players possam CONFUNDIR NPCS
/// COM PLAYERS"*. Um jogador **fala** quando luta: xinga quando erra, comemora quando derruba,
/// reclama quando esta sem folego. O NPC deste port fazia tudo o que um jogador faz -- andava,
/// aparava, recarregava, transformava, atirava -- e era **mudo**, que e a unica coisa que denuncia
/// um boneco em dois segundos de briga.
///
/// No DM sao NOVE pontos de fala, e eles nao sao enfeite: cada um esta amarrado a uma DECISAO que a
/// IA acabou de tomar. "Hora de acabar com isso!" sai exatamente quando o modo finalizador liga;
/// "Meu ki esta se esgotando!" sai exatamente quando a economia de Ki passa a valer. Quem esta do
/// outro lado aprende a LER a IA pela fala dela -- e e por isso que a fala e informacao de jogo, e
/// nao decoracao.
/// ========================================================================================================
///
/// ============================ POR QUE A COOLDOWN MORA AQUI, E SO AQUI ============================
/// O DM tem DOIS relogios independentes e eles nao competem -- o autor escreveu isso por extenso no
/// `npc_warn_say` (*"cooldown proprio (nao competente com as falas de combate)"*, `:315`):
///
///   * COMBATE (`ai_next_chat`) -- `isBoss ? 30 : 50` tiques = 3,0 s / 5,0 s (`:229`);
///   * AVISO   (`ai_warn_cd`)   -- `NPC_AI_WARN_CD 300` tiques = 30 s (`:15`).
///
/// Os dois moram neste tipo e em nenhum outro lugar. Os pontos de fala espalhados pelo
/// <see cref="Cerebro"/> **so dizem a OCASIAO**; quem decide se sai som e este objeto. Um relogio por
/// receita seria a armadilha que o `Comando.Marcar` ja custou uma vez a este port: quatro copias da
/// mesma regra esperando a quinta nascer em branco.
/// ============================================================================================
///
/// ============================ AS FRASES SAO AS DO DM, EM PORTUGUES ============================
/// As quatro listas nomeadas do original (`npc_ki_warn_lines`, `npc_stam_warn_lines`,
/// `npc_recharge_lines`, `npc_finisher_lines`, `npc_hurt_warn_lines`, `NPCAI.dm:32-36`) ja estao em
/// portugues e vieram LITERAIS. As frases soltas, escritas em linha dentro do `npc_combat_action`
/// (`:378, :401, :415, :419, :549, :781, :795, :797`), estao em INGLES no original -- restos da base
/// antes da traducao, na mesma funcao que chama as listas em portugues. Elas vieram traduzidas, e a
/// escolha esta declarada aqui: o jogo do dono e em portugues (o guia, a interface, as descricoes de
/// tecnica), e um NPC que grita "Take THIS!" e depois "Meu ki esta se esgotando!" nao parece um
/// jogador -- parece um bug de localizacao.
/// ==========================================================================================
///
/// **SEM ESTADO DE MUNDO AQUI DENTRO.** Este tipo nao conhece zona, alcance nem quem ouve: quem
/// decide isso e o `GameServer.Falar`, o MESMO funil do jogador, com o mesmo raio de vista e a
/// mesma regra de o "!" virar grito. E por ele que a fala de um NPC chega no chat pelo caminho por
/// onde chega a de uma pessoa.
/// </summary>
public sealed class Falatorio
{
	// =====================================================================
	// OS DOIS RELOGIOS -- ver o cabecalho
	// =====================================================================

	/// <summary>`ai_next_chat` de um corpo comum: `50` tiques (`NPCAI.dm:229`).</summary>
	public const double PausaDeCombate = 5.0;

	/// <summary>...e a de um CHEFE, que fala mais: `30` tiques (mesma linha).</summary>
	public const double PausaDeCombateDeChefe = 3.0;

	/// <summary>`NPC_AI_WARN_CD 300` tiques (`NPCAI.dm:15`) -- o relogio dos avisos, separado.</summary>
	public const double PausaDeAviso = 30.0;

	/// <summary>
	/// DE QUANTO EM QUANTO TEMPO OS LIMIARES SAO OLHADOS -- e nao "de quanto em quanto tempo ele
	/// fala". O `npc_resource_warnings` e chamado pelo `checkState`, que dorme `sleep(5)` = 0,5 s
	/// (`NPCAI.dm:813`).
	///
	/// Os dois numeros precisam existir separados: quem fala e o relogio de 30 s, mas as HISTERESES
	/// (`ai_warned_*`) tem que ser reavaliadas muito mais rapido -- senao um corpo que se recuperou
	/// durante a pausa so descobriria isso meio minuto depois, e perderia o proximo aviso de verdade.
	/// </summary>
	public const double PeriodoDaLeituraDeAvisos = 0.5;

	/// <summary>
	/// A PAUSA EXTRA DEPOIS DE UM ANUNCIO DE ASCENSAO: `ai_next_chat = world.time + 20`
	/// (`npc_power_up`, `NPCAI.dm:255`) -- 2 s alem da pausa normal. Quem acabou de explodir de poder
	/// nao emenda uma piadinha no tique seguinte.
	/// </summary>
	public const double PausaDepoisDeAscender = 2.0;

	// =====================================================================
	// OS LIMIARES DOS AVISOS -- todos literais
	// =====================================================================

	/// <summary>`NPC_AI_WARN_KI 0.35` (`NPCAI.dm:12`).</summary>
	public const double KiQuePreocupa = 0.35;

	/// <summary>`NPC_AI_WARN_STAM 0.35` (`NPCAI.dm:13`).</summary>
	public const double FolegoQuePreocupa = 0.35;

	/// <summary>`NPC_AI_WARN_HP 25` (`NPCAI.dm:14`), em fracao.</summary>
	public const double VidaQuePreocupa = 0.25;

	/// <summary>
	/// `NPC_AI_WARN_HYST 0.15` (`NPCAI.dm:16`) -- e a HISTERESE, e ela e o que impede a fala de
	/// virar metralhadora.
	///
	/// Sem ela, um corpo oscilando em volta do limiar (e um corpo em luta oscila: leva golpe, regenera,
	/// leva golpe) reavisaria a cada travessia. Com ela, so volta a avisar depois de RECUPERAR de
	/// verdade -- que e a diferenca entre "ele avisou que estava sem ki" e "ele ficou repetindo isso".
	/// </summary>
	public const double Folga = 0.15;

	/// <summary>
	/// A folga da VIDA e maior: `HP >= NPC_AI_WARN_HP + 20` (`NPCAI.dm:339`) -- 0,45 em fracao. O
	/// original escolheu um numero diferente pra este e nao pros outros dois, e o motivo esta na
	/// escala: HP anda de 0 a 100 e as duas fracoes andam de 0 a 1.
	/// </summary>
	public const double FolgaDaVida = 0.20;

	// =====================================================================
	// AS FRASES
	// =====================================================================

	/// <summary>`npc_ki_warn_lines` (`NPCAI.dm:32`) -- LITERAL.</summary>
	private static readonly string[] SemKi =
	[
		"Droga, estou ficando sem ki...",
		"Meu ki esta se esgotando!",
		"Nao posso desperdicar mais energia...",
	];

	/// <summary>`npc_stam_warn_lines` (`NPCAI.dm:33`) -- LITERAL.</summary>
	private static readonly string[] SemFolego =
	[
		"huf huf... isso esta me cansando...",
		"Ofegante... preciso recuperar o folego...",
		"Minhas pernas estao pesadas...",
	];

	/// <summary>
	/// `npc_recharge_lines` (`NPCAI.dm:34`) -- LITERAL, e ela era **letra morta no original**.
	///
	/// A lista existe la desde que o Sistema 1 foi escrito e o unico chamador dela e o
	/// `rechargeState` (`:670`), que so roda quando ki E folego estao criticos ao mesmo tempo E o
	/// sorteio de inteligencia passa. Neste port o `Plano.Recuperar` e alcancado pelo mesmo gate --
	/// entao ela e usada aqui exatamente onde o autor a colocou.
	/// </summary>
	private static readonly string[] Carregando =
	[
		"Preciso recuperar minhas energias!",
		"Voce nao vai me derrotar assim... HAAAA!",
		"So um instante... HRAAA!",
	];

	/// <summary>`npc_finisher_lines` (`NPCAI.dm:35`) -- LITERAL.</summary>
	private static readonly string[] Finalizando =
	[
		"Hora de acabar com isso!",
		"Voce esta acabado!",
		"E o seu fim!",
	];

	/// <summary>`npc_hurt_warn_lines` (`NPCAI.dm:36`) -- LITERAL.</summary>
	private static readonly string[] Ferido =
	[
		"Argh... estou muito ferido...",
		"Nao sei quanto tempo mais aguento isso...",
		"*cospe sangue* Isso... vai deixar marca...",
		"Meu corpo esta no limite...!",
	];

	/// <summary>`"Get back!","Away from me!","Enough!"` (`NPCAI.dm:401`) -- traduzidas.</summary>
	private static readonly string[] AbrindoEspaco =
	[
		"Pra tras!",
		"Sai de perto de mim!",
		"Chega!",
	];

	/// <summary>`"RAAAH!","Disappear!","I'll finish this!","Take THIS!"` (`NPCAI.dm:415`) -- traduzidas.</summary>
	private static readonly string[] EmRajada =
	[
		"RAAAH!",
		"Desapareca!",
		"Eu acabo com isso agora!",
		"Toma ISTO!",
	];

	/// <summary>
	/// `"HIYAH!","Take that!","Is that all?!","Pathetic!","You call that fighting?!"`
	/// (`NPCAI.dm:419`) -- traduzidas.
	/// </summary>
	private static readonly string[] Socando =
	[
		"HIYAH!",
		"Toma essa!",
		"So isso?!",
		"Patetico!",
		"Voce chama isso de lutar?!",
	];

	/// <summary>
	/// `"Too easy.","As expected.","You never stood a chance.","Know your place!","Don't waste my time."`
	/// (`NPCAI.dm:549`) -- traduzidas. Sai quando o alvo cai, no fim do `attackState`.
	/// </summary>
	private static readonly string[] Vencendo =
	[
		"Facil demais.",
		"Era o esperado.",
		"Voce nunca teve chance.",
		"Saiba o seu lugar!",
		"Nao me faca perder tempo.",
	];

	/// <summary>`"Tch!","That hurt...","You'll regret that!","Not bad."` (`NPCAI.dm:797`) -- traduzidas.</summary>
	private static readonly string[] Apanhando =
	[
		"Tch!",
		"Isso doeu...",
		"Voce vai se arrepender!",
		"Nada mal.",
	];

	/// <summary>
	/// `"I won't fall here!","Gah... you're stronger than I thought!","Is this... the end?!"`
	/// (`NPCAI.dm:795`) -- traduzidas. E a MESMA ocasiao da de cima, com a vida no fio.
	/// </summary>
	private static readonly string[] NoFio =
	[
		"Eu nao caio aqui!",
		"Gah... voce e mais forte do que eu pensava!",
		"Isso... e o fim?!",
	];

	/// <summary>
	/// `"HAAAAAA!!","Tome ISTO!","Nao vai me vencer nisso!"` (`NPCAI.dm:378`) -- LITERAL. E a lista
	/// que o original usa pra **soltar um raio**, e por isso ela serve as duas ocasioes de tiro:
	/// <see cref="Momento.ContraFeixe"/> e <see cref="Momento.Disparar"/>.
	/// </summary>
	private static readonly string[] SoltandoORaio =
	[
		"HAAAAAA!!",
		"Tome ISTO!",
		"Nao vai me vencer nisso!",
	];

	// =====================================================================
	// ESTADO VIVO -- os dois relogios e as tres histereses
	// =====================================================================

	private double _proximaDeCombate;
	private double _proximoAviso;

	/// <summary>
	/// Ja avisei disto e ainda nao me recuperei? Os `ai_warned_ki` / `ai_warned_stam` / `ai_warned_hp`
	/// (`NPCAI.dm:328, :333, :338`), zerados no `resetState` (`:732-734`) -- ver <see cref="Esquecer"/>.
	/// </summary>
	private bool _avisouKi, _avisouFolego, _avisouVida;

	/// <summary>ESTE CORPO E CHEFE? So muda a CADENCIA e as chances -- nunca as frases.</summary>
	public bool Chefe;

	/// <summary>
	/// A FALA ESTA LIGADA? **Nasce DESLIGADA, e a direcao do erro foi escolhida.**
	///
	/// ============================ FALHA CALADA, E NAO FALHA FALANTE ============================
	/// E a mesma disciplina da `Percepcao.LinhaLivre` (*"o default e `false` = nao sei = nao atira"*).
	/// Se alguem escrever o proximo cerebro montado a mao e esquecer desta linha, o defeito e um NPC
	/// mudo -- que e o comportamento que o jogo teve ate hoje e que ninguem estranha. Nascendo ligada,
	/// o esquecimento seria o corpo de um JOGADOR possuido (a fera do Oozaru, a furia lendaria)
	/// gritando "Toma essa!" no chat com o nome dele, dizendo o que ele nao digitou.
	///
	/// Quem liga e o `Temperamento.Montar` -- ou seja, so corpo que saiu de um molde do `npcs.json`.
	/// Ver <see cref="Cerebro.Boca"/>.
	/// ======================================================================================
	/// </summary>
	public bool Ligado;

	/// <summary>Os dois relogios andam. Chamado uma vez por tique, pelo <see cref="Cerebro.Pensar"/>.</summary>
	public void Tique(double dt)
	{
		if (_proximaDeCombate > 0) _proximaDeCombate -= dt;
		if (_proximoAviso > 0) _proximoAviso -= dt;
	}

	/// <summary>
	/// DESENGAJOU: esquece o que ja avisou. E o `resetState` (`NPCAI.dm:732-734`), e sem ele um
	/// cidadao que levou uma surra ontem nunca mais reclamaria de estar sem ki -- o bit ficaria
	/// ligado pra sempre e a histerese trabalharia contra a fala em vez de a favor.
	/// </summary>
	public void Esquecer()
	{
		_avisouKi = false;
		_avisouFolego = false;
		_avisouVida = false;
	}

	/// <summary>
	/// COBRA A PAUSA EXTRA DO ANUNCIO DE ASCENSAO -- o `ai_next_chat = world.time + 20` do
	/// `npc_power_up` (`NPCAI.dm:255`).
	/// </summary>
	public void AcabouDeAscender() =>
		_proximaDeCombate = Math.Max(_proximaDeCombate, PausaDepoisDeAscender);

	/// <summary>
	/// UMA FALA DE COMBATE, ou nulo. **Este e o unico lugar que cobra o relogio de combate.**
	///
	/// A CHANCE E A DO ORIGINAL, ocasiao por ocasiao (ver <see cref="ChanceDe"/>), e ela e sorteada
	/// ANTES do relogio de proposito: o DM tambem sorteia primeiro (`if(prob(35)) npc_combat_chat(...)`)
	/// e um sorteio perdido nao gasta a pausa de ninguem. Trocar a ordem faria o corpo ficar mudo por
	/// cinco segundos por causa de uma frase que nem chegou a sair.
	/// </summary>
	public string? DeCombate(Momento m, Random rng)
	{
		if (!Ligado) return null;
		if (rng.NextDouble() >= ChanceDe(m)) return null;
		if (_proximaDeCombate > 0) return null;

		_proximaDeCombate = Chefe ? PausaDeCombateDeChefe : PausaDeCombate;
		return Escolher(m, rng);
	}

	/// <summary>
	/// A CHANCE DE CADA OCASIAO -- literal, e o chefe fala mais em quatro delas.
	///
	/// Note o que NAO tem chance: o <see cref="Momento.ContraFeixe"/>. No DM ele e o unico
	/// `npc_combat_chat` sem `prob()` na frente (`:378`), e faz sentido -- responder um raio com o
	/// proprio raio e o gesto mais dramatico que a IA tem, e ele ja e raro por conta propria (tem
	/// recarga de evento). O relogio de combate continua valendo, entao ele nao vira spam.
	/// </summary>
	private double ChanceDe(Momento m) => m switch
	{
		Momento.Finalizar => 0.35,                  // `prob(35)`  (`NPCAI.dm:389`)
		Momento.AbrirEspaco => 0.40,                // `prob(40)`  (`:401`)
		Momento.Rajada => Chefe ? 0.45 : 0.30,      // `prob(isBoss ? 45 : 30)` (`:415`)
		Momento.Socar => Chefe ? 0.45 : 0.25,       // `prob(isBoss ? 45 : 25)` (`:418`)
		Momento.Vencer => Chefe ? 0.70 : 0.40,      // `prob(isBoss ? 70 : 40)` (`:548`)
		Momento.Apanhar or Momento.NoFio => Chefe ? 0.50 : 0.25,   // `prob(isBoss ? 50 : 25)` (`:793`)
		Momento.ContraFeixe => 1.0,                 // sem `prob()` no original -- ver acima

		// ============================ ESTA E A UNICA CHANCE SEM ORIGINAL, E O NUMERO E EMPRESTADO ============================
		// O `blast()` do `npc_combat_action` (`NPCAI.dm:407-411`) nao tem linha de fala. **E mesmo
		// assim um blaster do DM fala o tempo todo**, porque la o seletor de acao roda a cada 0,3 s e
		// alterna: quando o `prob(blast_chance)` falha, o corpo cai no `attack()` de baixo -- que TEM
		// fala (`:418`). O tiro nao precisa de linha propria porque o soco vem logo.
		//
		// **Neste port isso nao acontece, e a causa e o compromisso.** `Atirar` e um PLANO, e um plano
		// dura no minimo 1,2 s; um chefe com raio fica no ciclo conjurar-soltar-pausar e nunca chega
		// no ramo do soco. A bancada mediu: 20 s de briga de um `freeza_vegeta` nascido pela producao,
		// plano `Atirar` o tempo todo, **zero frases**. Um chefe cujo kit inteiro e raio seria mudo --
		// que e exatamente o defeito que esta camada existe pra consertar.
		//
		// Entao o numero e emprestado do lugar certo: **a mesma chance do golpe avulso** (`:418`),
		// porque e o mesmo papel -- a frase que acompanha "ataquei uma vez". E as frases sao as que o
		// proprio DM ja usa pra soltar um raio (`:378`). Nao ha lista nova nem numero inventado: ha
		// uma ocasiao que o port precisou nomear porque a arquitetura dele separou o que o DM mistura.
		// ================================================================================================================
		Momento.Disparar => Chefe ? 0.45 : 0.25,
		_ => 0,
	};

	private static string Escolher(Momento m, Random rng)
	{
		string[] lista = m switch
		{
			Momento.Finalizar => Finalizando,
			Momento.AbrirEspaco => AbrindoEspaco,
			Momento.Rajada => EmRajada,
			Momento.Socar => Socando,
			Momento.Vencer => Vencendo,
			Momento.Apanhar => Apanhando,
			Momento.NoFio => NoFio,
			_ => SoltandoORaio,   // `ContraFeixe` e `Disparar`: as duas sao "solto o raio"
		};
		return lista[rng.Next(lista.Length)];
	}

	/// <summary>
	/// OS AVISOS DE RECURSO -- o `npc_resource_warnings` (`NPCAI.dm:322`) inteiro, com as tres
	/// histereses. Devolve nulo quase sempre: o relogio proprio e de 30 s.
	///
	/// ============================ A ORDEM DAS TRES E A DO ORIGINAL, E ELA IMPORTA ============================
	/// Ki, folego, vida -- nessa ordem (`:326, :331, :336`). Como so UMA frase sai por chamada (o
	/// `npc_warn_say` cobra o relogio e devolve zero nas seguintes), a ordem e uma PRIORIDADE: um
	/// corpo sem ki e sem folego ao mesmo tempo reclama do ki. Inverter isso mudaria o que o jogador
	/// ouve na situacao mais comum da luta, que e ficar sem os dois juntos.
	///
	/// **E as histereses continuam sendo atualizadas mesmo quando o relogio recusa a fala.** E o
	/// original: la o `ai_warned_*` so e LIGADO quando o `npc_warn_say` devolve 1, mas o ramo de
	/// DESLIGAR (o `else if` da recuperacao) roda sempre. Sem isso, um corpo que recuperasse durante
	/// os 30 s de pausa ficaria com o bit ligado e perderia o proximo aviso de verdade.
	/// ====================================================================================================
	/// </summary>
	public string? Aviso(in Percepcao p, Random rng)
	{
		if (!Ligado) return null;

		// `if(!IsInFight && !target) return` (`NPCAI.dm:323`) -- quem nao esta em briga nao reclama
		// de estar cansado. E o que impede o vilarejo inteiro de resmungar sozinho pela cidade.
		if (!p.TemAlvo) return null;

		string? frase = null;

		if (p.KiFrac <= KiQuePreocupa) { if (!_avisouKi && Pode()) { _avisouKi = true; frase = SemKi[rng.Next(SemKi.Length)]; } }
		else if (p.KiFrac >= KiQuePreocupa + Folga) _avisouKi = false;

		if (p.FolegoFrac <= FolegoQuePreocupa)
		{
			if (frase == null && !_avisouFolego && Pode())
			{ _avisouFolego = true; frase = SemFolego[rng.Next(SemFolego.Length)]; }
		}
		else if (p.FolegoFrac >= FolegoQuePreocupa + Folga) _avisouFolego = false;

		if (p.VidaFrac <= VidaQuePreocupa)
		{
			if (frase == null && !_avisouVida && Pode())
			{ _avisouVida = true; frase = Ferido[rng.Next(Ferido.Length)]; }
		}
		else if (p.VidaFrac >= VidaQuePreocupa + FolgaDaVida) _avisouVida = false;

		return frase;
	}

	/// <summary>
	/// A FALA DE QUEM VAI CARREGAR -- `npc_warn_say(pick(npc_recharge_lines))` na PRIMEIRA linha do
	/// `rechargeState` (`NPCAI.dm:670`). Paga o relogio dos AVISOS, e nao o de combate, porque no
	/// original ela sai pelo `npc_warn_say`.
	/// </summary>
	public string? DeRecarga(Random rng)
	{
		if (!Ligado || !Pode()) return null;
		return Carregando[rng.Next(Carregando.Length)];
	}

	/// <summary>
	/// O relogio dos AVISOS, cobrado no lugar do `npc_warn_say` (`NPCAI.dm:316-317`): ele confere e
	/// carimba na mesma funcao, e por isso quem chama nao precisa lembrar de nada.
	/// </summary>
	private bool Pode()
	{
		if (_proximoAviso > 0) return false;
		_proximoAviso = PausaDeAviso;
		return true;
	}
}

/// <summary>
/// AS OCASIOES EM QUE UM CORPO DIRIGIDO FALA. Uma por ponto de fala do `NPCAI.dm` -- a linha do
/// original esta citada em cada entrada, e a chance de cada uma no `ChanceDe` do
/// <see cref="Falatorio"/>.
///
/// ============================ E O QUE FICOU DE FORA, COM O MOTIVO ============================
/// O DM tem um decimo ponto: o aliado que cai na frente (`behavior_check`, `NPCAI.dm:777-781` --
/// *"That was my comrade!"*). Ele **nao entra**, e a razao e a regra de admissao da
/// <see cref="Percepcao"/>: responder "ha um aliado meu caido perto?" e varrer a zona procurando
/// corpos do mesmo molde, 30 vezes por segundo por corpo. E exatamente a forma de custo que este
/// port ja pagou caro uma vez (o "cenario por pedaco"), e o ganho seria uma frase a cada muitos
/// minutos.
///
/// O dia em que a percepcao tiver uma lista de vizinhos por outro motivo, este `enum` ganha a
/// entrada e o `Cerebro` ganha o ponto de fala. Enquanto nao tiver, escrever o ramo seria escrever
/// a varredura.
/// ========================================================================================
/// </summary>
public enum Momento
{
	/// <summary>O modo finalizador acabou de ligar: `npc_finisher_lines` (`NPCAI.dm:389`).</summary>
	Finalizar,

	/// <summary>Encurralado, soltando o sopro pra abrir espaco (`NPCAI.dm:401`).</summary>
	AbrirEspaco,

	/// <summary>Saiu uma RAJADA (o `BarrageAttack`, `NPCAI.dm:415`).</summary>
	Rajada,

	/// <summary>Saiu um golpe avulso (`NPCAI.dm:418`).</summary>
	Socar,

	/// <summary>O alvo caiu (`NPCAI.dm:548`).</summary>
	Vencer,

	/// <summary>Levou um golpe grande (`NPCAI.dm:797`).</summary>
	Apanhar,

	/// <summary>...e o mesmo, com a vida no fio (`NPCAI.dm:795`).</summary>
	NoFio,

	/// <summary>Respondeu um raio com o proprio raio (o BeamClash da IA, `NPCAI.dm:378`).</summary>
	ContraFeixe,

	/// <summary>
	/// SOLTOU UM TIRO DE LONGE. A unica ocasiao sem ponto de fala proprio no original -- ver a
	/// justificativa inteira no `ChanceDe` do <see cref="Falatorio"/>. Frases e chance sao
	/// emprestadas de dois lugares do DM; nenhuma das duas foi inventada.
	/// </summary>
	Disparar,
}
