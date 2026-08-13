using Jandirus.Core.Forms;
using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>
/// O QUE O CORPO ESTA TENTANDO FAZER AGORA. Ele dura no minimo
/// <see cref="Cerebro.TempoMinimoNoPlano"/> -- ver o compromisso.
/// </summary>
public enum Plano
{
	/// <summary>Alvo no chao (ou corpo caido): nada a fazer. Continuar socando um corpo no chao nao
	/// muda nada no jogo e deixa a cena feia.</summary>
	Nada,

	/// <summary>
	/// ANDAR SEM ALVO -- o macaco vagando e derrubando o que estiver no caminho.
	///
	/// ============================ ELE SE PERDEU NA PRIMEIRA VERSAO DESTA CAMADA ============================
	/// O cerebro antigo nao tinha planos: ele andava na direcao de `posAlvo`, e quando nao havia
	/// presa o servidor passava um PONTO (`RumoDaFera`) no lugar do alvo. Com os planos, `!TemAlvo`
	/// caiu em `Nada` -- e a fera e a furia lendaria pararam de andar, o que a bancada de formas
	/// pegou na hora ("o corpo possuido se move sozinho" reprovou com a posicao identica).
	///
	/// A licao e sobre o TIPO: a percepcao carrega `DoAlvo` mesmo sem alvo, e um plano so pra esse
	/// caso e o que impede o ponto de virar um alvo (com soco e guarda) ou de sumir.
	/// ==================================================================================================
	/// </summary>
	Vagar,

	/// <summary>Fechar a distancia e bater. O estado normal.</summary>
	Pressionar,

	/// <summary>Afastar guardando. O "respirar" que o clone sempre teve.</summary>
	Recuar,

	/// <summary>Afastar ate <see cref="Cerebro.DistanciaSegura"/>, PARAR e carregar. O `rechargeState` (NPCAI.dm:666).</summary>
	Recuperar,

	/// <summary>Subir um degrau da escada de formas. O `npc_try_transform` (NPCAI.dm:361).</summary>
	Escalar,

	/// <summary>Decolar e subir ate o andar do alvo. Nao existe no DM -- ver o cabecalho.</summary>
	Alcancar,

	/// <summary>
	/// ABRIR DISTANCIA, PLANTAR O PE, CONJURAR E SOLTAR. **Nunca escolhido hoje** -- o arsenal de
	/// todo corpo do jogo esta vazio porque nenhuma tecnica portada viaja (ver `TecnicasDeLonge`).
	///
	/// Ele existe agora, e nao no dia do beam, porque o que e caro nao e o `case` do disparo: e o
	/// LUGAR dele na ordem das perguntas, o compromisso, as interrupcoes e o preco. Escrever isso
	/// junto com um projetil novo seria escrever duas coisas de uma vez e nao saber qual das duas
	/// esta errada quando o NPC ficar parado olhando pro horizonte.
	/// </summary>
	Atirar,
}

/// <summary>
/// O CEREBRO DE UM CORPO QUE O SERVIDOR ESTA DIRIGINDO.
///
/// E logica PURA (mora no Core, nao conhece Godot, nem rede, nem `ServerPlayer`): recebe o mundo
/// como caberia numa tela (<see cref="Percepcao"/>) e o que o jogo ja disse que este corpo pode
/// (<see cref="Capacidades"/>), e devolve **o que apertar** (<see cref="Comando"/>). Ele nao move,
/// nao soca, nao transforma e nao voa: quem faz isso e o servidor, pelas MESMAS funcoes do jogador.
///
/// ============================ O QUE MUDOU NESTA CAMADA, E O QUE NAO ============================
/// Ele NAO foi reescrito. As quatro manivelas que o Oozaru e a furia lendaria ja escreviam
/// continuam com o mesmo nome, o mesmo tipo e o mesmo default -- <see cref="IntervaloDeDecisao"/>,
/// <see cref="DistanciaIdeal"/>, <see cref="VidaCautelosa"/>, <see cref="ChanceDePesado"/> --, e o
/// comportamento delas e o mesmo: `VidaCautelosa = 0` continua querendo dizer "nunca recua, nunca
/// guarda por medo".
///
/// O que mudou:
///   * `Decisao` virou <see cref="Comando"/> (estado x pulso) -- ver o cabecalho daquele arquivo;
///   * `Pensar` passou a receber <see cref="Percepcao"/> em vez de sete argumentos soltos, porque
///     a altitude e o poder do alvo entrariam como o oitavo e o nono;
///   * nasceram quatro capacidades novas (voar, guardar por RITMO, carregar, transformar) e uma
///     manivela pra cada uma poder ser DESLIGADA por tempero (<see cref="Disciplina"/>,
///     <see cref="Inteligencia"/>). A fera continua nao guardando porque a manivela dela e zero,
///     e nao porque o codigo da guarda nao existe.
/// ==========================================================================================
///
/// ============================ DOIS RELOGIOS, E ISSO NAO E DETALHE ============================
/// **Decidir e devagar; reagir e rapido.** O plano e reconsiderado a cada
/// <see cref="IntervaloDeDecisao"/> (0,2 a 0,5 s conforme o tempero) e MANTIDO no meio-tempo -- e o
/// que da a hesitacao humana e o que impede o boneco de tremer entre duas opcoes empatadas.
///
/// Os REFLEXOS (a guarda, a queda de proposito) rodam a CADA tique, por fora da decisao. Uma IA que
/// decide a 4 Hz e bloqueia a 4 Hz leva soco parada em ate 250 ms de janela, e isso nao e questao
/// de calibragem: um ciclo so nao tem duas velocidades.
/// =========================================================================================
///
/// ============================ E O QUE DELE VEIO DO DM, LINHA POR LINHA ============================
/// Tres dos quatro gestos desta camada existem no `Code/Modules/NPCs/NPCAI.dm` e estao portados com
/// os numeros de la (cada constante cita a linha):
///
///   guarda      `npc_defensive_check` (:288-293) e o brace do `attack()` (:189-191)
///   carga       `rechargeState` (:666-698), o estado inteiro
///   transformar `npc_try_transform` (:361-368), chamado pelo `npc_power_up` (:234)
///
/// O QUARTO NAO EXISTE LA: **a IA do DM nunca voa.** Nao ha um `isflying`, um `Fly` nem um
/// `flight` no arquivo inteiro -- e nao e esquecimento, e que no BYOND nao ha altura (voar la e
/// sair da conta de colisao, no mesmo z). O voo e a altitude sao desenho novo deste port, e estao
/// marcados como tal onde aparecem.
/// ============================================================================================
/// </summary>
public sealed class Cerebro
{
	// =====================================================================
	// AS QUATRO MANIVELAS ANTIGAS -- o Oozaru e a furia escrevem estas
	// =====================================================================

	/// <summary>Quanto tempo entre duas DECISOES. Nao e o tempo de reacao: ver <see cref="TempoDeReacao"/>.</summary>
	public double IntervaloDeDecisao = 0.25;

	/// <summary>A que distancia ele tenta ficar. Um tile: dentro do alcance do soco.</summary>
	public float DistanciaIdeal = 34f;

	/// <summary>Fracao de vida abaixo da qual ele passa a guardar e a recuar. Zero = nunca (a fera).</summary>
	public double VidaCautelosa = 0.45;

	/// <summary>Chance de escolher o golpe pesado quando pode. O resto sai leve.</summary>
	public double ChanceDePesado = 0.35;

	// =====================================================================
	// AS MANIVELAS NOVAS
	// =====================================================================

	/// <summary>
	/// DISCIPLINA (0..1): o quanto este corpo APARA. Zero = nunca ergue a guarda.
	///
	/// E o `e_behavior_vals[4]` (logica) do DM, que gateia o `npc_defensive_check`
	/// (`NPCAI.dm:291`: `e_behavior_vals[4] >= 35`). O default 0,35 e o `NPC_AI_INT_DEFAULT`.
	///
	/// **A fera e a furia lendaria a poem em ZERO**, e nao por economia: o `legendary_berserk_loop`
	/// nao tem um unico ramo de guarda, e o macaco sem controle *"sai batendo em qualquer coisa"*.
	/// Furia que se protege nao e furia -- e o mesmo argumento que ja justificava `VidaCautelosa = 0`
	/// nos dois.
	/// </summary>
	public double Disciplina = 0.35;

	/// <summary>
	/// INTELIGENCIA (0..1): o `ai_intelligence` (`NPC_AI_INT_DEFAULT 35`, `NPC_AI_INT_BOSS 85`).
	///
	/// Ela nao deixa o NPC "melhor em tudo": ela decide **quais planos existem pra ele**. Um bicho
	/// burro nunca recarrega e nunca paira rasante -- ele avanca e bate. Erro CARACTERISTICO e
	/// reconhecivel ("aquele bicho nunca recua"); erro aleatorio e indistinguivel de bug.
	/// </summary>
	public double Inteligencia = 0.35;

	/// <summary>
	/// TEMPO DE REACAO BASE, em segundos. Nao e o intervalo de decisao: e quanto ele demora pra
	/// RESPONDER a uma coisa que acabou de acontecer (um soco chegando).
	///
	/// 0,25 s e o tempo de reacao visual de um humano razoavelmente atento. O piso duro de
	/// <see cref="ReacaoMinima"/> existe pra que nenhum sorteio produza um reflexo sobre-humano --
	/// e a bancada afirma esse piso sobre mil amostras.
	/// </summary>
	public double TempoDeReacao = 0.25;

	// =====================================================================
	// OS NUMEROS. Portados quando ha original; marcados quando nao ha.
	// =====================================================================

	/// <summary>Piso absoluto do tempo de reacao, em segundos. Abaixo disto e clarividencia.</summary>
	public const double ReacaoMinima = 0.10;

	/// <summary>Quanto tempo um plano dura no minimo, em segundos. E o COMPROMISSO -- ver `Repensar`.</summary>
	public const double TempoMinimoNoPlano = 1.2;

	/// <summary>Guarda erguida dura de 0,8 a 1,8 s. LITERAL: `spawn(rand(8,18))` (`NPCAI.dm:293`).</summary>
	public const double GuardaMin = 0.8, GuardaMax = 1.8;

	/// <summary>Chance de aparar quando pressionado. `prob(35)` (`NPCAI.dm:291`).</summary>
	public const double ChanceDeApararSobPressao = 0.35;

	/// <summary>Chance de SOLTAR a guarda quando a pressao passa. `prob(40)` (`NPCAI.dm:289`).</summary>
	public const double ChanceDeSoltarAGuarda = 0.40;

	/// <summary>Abaixo desta vida ele se considera pressionado. `HP &lt;= 35` (`NPCAI.dm:291`).</summary>
	public const double VidaPressionada = 0.35;

	/// <summary>Acima desta vida a pressao passou. `HP &gt; 40` (`NPCAI.dm:289`).</summary>
	public const double VidaAliviada = 0.40;

	/// <summary>Chance base de ANTECIPAR um golpe pelo ritmo. `prob(15)` do brace (`NPCAI.dm:190`).</summary>
	public const double ChanceDeAntecipar = 0.15;

	/// <summary>Ki critico: `NPC_AI_KI_CRIT 0.10` (`NPCAI.dm:9`).</summary>
	public const double KiCritico = 0.10;

	/// <summary>Folego critico: `NPC_AI_STAM_CRIT 0.12` (`NPCAI.dm:12`).</summary>
	public const double FolegoCritico = 0.12;

	/// <summary>Carrega ate esta fracao antes de voltar a lutar: `NPC_AI_RECHARGE_TO 0.75` (`NPCAI.dm:19`).</summary>
	public const double CarregarAte = 0.75;

	/// <summary>Distancia "segura" pra parar de recuar e comecar a carregar: `NPC_AI_RECHARGE_DIST 6` tiles (`NPCAI.dm:22`).</summary>
	public const float DistanciaSegura = 6 * ZoneCollision.TileSize;

	/// <summary>Teto do modo recarga: `NPC_AI_RECHARGE_MAX 200` tiques do DM = 20 s (`NPCAI.dm:23`).</summary>
	public const double PrazoDaRecarga = 20;

	/// <summary>Apanhou isto enquanto carregava: desiste. `NPC_AI_RECHARGE_ABORT_DMG 15` de HP (`NPCAI.dm:24`).</summary>
	public const double DanoQueAbortaACarga = 0.15;

	/// <summary>
	/// KI MINIMO PRA TENTAR TRANSFORMAR. **LITERAL**: `if(Ki &lt; MaxKi * 0.25) return`
	/// (`npc_try_transform`, `NPCAI.dm:365`).
	/// </summary>
	public const double KiParaTransformar = 0.25;

	/// <summary>Vida abaixo da qual vale a pena escalar: `HP &lt;= 45` (`NPCAI.dm:517`).</summary>
	public const double VidaQuePedeForma = 0.45;

	/// <summary>Alvo tantas vezes mais forte que eu -> escalar: `target.expressedBP &gt;= expressedBP * 1.5` (`NPCAI.dm:517`).</summary>
	public const double PoderQuePedeForma = 1.5;

	/// <summary>Carencia entre duas tentativas de escalar: `ai_powerup_cd = world.time + 150` = 15 s (`NPCAI.dm:240`).</summary>
	public const double CarenciaDeAscensao = 15;

	/// <summary>
	/// KI MINIMO PRA DECOLAR (fracao). **NAO HA ORIGINAL** -- a IA do DM nao voa. O numero e o mesmo
	/// da transformacao de proposito: os dois respondem a mesma pergunta ("da pra bancar o gesto ou
	/// vou cair dele?"), e um so numero e uma manivela a menos pra calibrar errado.
	/// </summary>
	public const double KiParaDecolar = 0.25;

	/// <summary>
	/// POUSAR DE PROPOSITO abaixo disto (fracao do tanque). **DESENHO NOVO.**
	///
	/// Ser derrubado do ceu e uma FALHA -- o corpo cai a 16 tiles por segundo e chega no chao sem
	/// guarda, sem folego e no meio do inimigo. Um lutador desce ANTES. O piso absoluto
	/// (<see cref="Voo.KiQueDerruba"/> x 4) anda junto porque aquele numero e absoluto: 5 de Ki e 5
	/// de Ki tanto pro saibaman quanto pro Freeza.
	/// </summary>
	public const double KiQueMandaPousar = 0.12;

	/// <summary>Inteligencia minima pra a receita de PAIRAR RASANTE existir. **DESENHO NOVO.**</summary>
	public const double InteligenciaQuePairaRasante = 0.6;

	/// <summary>Chance de simplesmente ERRAR um reflexo. E o que faz o jogador poder vencer no ritmo.</summary>
	public const double ChanceDeErrar = 0.15;

	// =====================================================================
	// O TIRO DE LONGE. **NAO HA ORIGINAL NO DM** -- a IA de la nao usa tecnica ativa nenhuma
	// (`NPCAI.dm` nao chama `assignverb` nem nenhum dos verbs de skill). Sao numeros de desenho,
	// e cada um diz de onde saiu.
	// =====================================================================

	/// <summary>
	/// PRAZO DE UMA CONJURACAO, em segundos. Nao e o tempo de conjuracao da tecnica (esse e dela,
	/// e vem no <see cref="Tiro.TempoDeConjuracao"/>): e o teto do PLANO, pra um golpe que nunca
	/// completa -- porque o alvo fica entrando e saindo da janela -- nao virar um NPC plantado.
	///
	/// Mesmo papel do `PrazoDaRecarga` (o `RECHARGE_MAX 200` do DM), e por isso e a quarta parte
	/// dele: conjurar e um gesto curto; carregar Ki e um estado.
	/// </summary>
	public const double PrazoDaConjuracao = 5;

	/// <summary>
	/// DEPOIS DE ATIRAR, ESPERA ISTO antes de pensar em atirar de novo.
	///
	/// **NAO E A RECARGA DA TECNICA** -- essa mora no efeito, e e ele quem recusa (o `_solarPronto`
	/// do Solar Flare e o modelo). Isto aqui e a mesma coisa que a <see cref="CarenciaDeAscensao"/>
	/// resolve pro C: impedir que o cerebro martele uma porta fechada 4 vezes por segundo. Se a
	/// tecnica tiver recarga maior, quem manda e ela; se tiver menor, o NPC atira no ritmo daqui --
	/// e ser um pouco mais lento que o teorico e exatamente o que um jogador tambem e.
	/// </summary>
	public const double PausaDepoisDoTiro = 1.0;

	/// <summary>
	/// QUANTO RISCO DE ERRAR ELE ACEITA. Sai da <see cref="Inteligencia"/>: o bicho burro larga o
	/// golpe em alvo correndo no limite do alcance; o esperto espera o alvo parar.
	///
	/// E o mesmo desenho do `prob(ai_intelligence)` que gateia a recarga no DM (`NPCAI.dm:522`) --
	/// inteligencia decide QUE ERRO ele comete, e nao "o quanto ele acerta". Erro caracteristico e
	/// reconhecivel; erro aleatorio e indistinguivel de bug.
	/// </summary>
	private double ToleranciaDeErro => 0.75 - 0.5 * Math.Clamp(Inteligencia, 0, 1);

	/// <summary>Hesitacao, em segundos: quanto o corpo trava quando o mundo vira do avesso.</summary>
	public const double HesitacaoMin = 0.15, HesitacaoMax = 0.40;

	// =====================================================================
	// ESTADO VIVO
	// =====================================================================

	/// <summary>O que o jogo respondeu que este corpo pode. Escrito pelo servidor, a 1 Hz.</summary>
	public Capacidades Poderes;

	/// <summary>O plano em curso e ha quanto tempo ele esta de pe.</summary>
	public Plano Atual { get; private set; } = Plano.Nada;

	private double _relogioDeDecisao;
	private double _relogioDasCapacidades;
	private double _noPlano;

	// --- guarda ---
	private double _guardaAte;        // enquanto > 0, a guarda fica erguida
	private double _reacaoDaGuarda;   // atraso ate a guarda subir de fato

	// --- o RITMO do oponente (o que substitui a clarividencia) ---
	private const int MarcasDeGolpe = 4;
	private readonly double[] _quandoVi = new double[MarcasDeGolpe];
	private int _marcas;
	private double _relogio;          // tempo do cerebro, em segundos desde que ele nasceu

	// --- carga ---
	private double _vidaAoCarregar;
	private double _carregandoHa;

	// --- escalar ---
	private double _carenciaDeForma;

	// --- o tiro de longe (inerte hoje: ver `Plano.Atirar`) ---
	private int _tiroEscolhido = -1;   // indice no `Poderes.DeLonge`; -1 = nenhum
	private double _conjurandoHa;
	private double _pausaDoTiro;
	private double _vidaAoConjurar;

	// --- humanidade ---
	private double _hesitaAte;
	private double _deriva;           // passeio lento do tempo de reacao (ver Reagir)
	private double _ultimoPoderDoAlvo;
	private double _voltaDoSoco;      // ritmo em RAJADA: ver EmRajada

	/// <summary>
	/// LIGA O RELATO. Desligado por padrao, e a razao e de ORCAMENTO e nao de gosto: montar a frase
	/// e uma string interpolada por decisao (4 Hz por corpo), e com 20 NPCs isso e lixo constante
	/// pro coletor -- que numa sessao de horas vira pausa, e ninguem liga a pausa a IA.
	///
	/// Quem quiser entender uma decisao liga isto naquele corpo. E o mesmo desenho do `_diagGolpe`
	/// do servidor: o diagnostico existe sempre, e custa so quando alguem esta olhando.
	/// </summary>
	public bool Explicando;

	/// <summary>
	/// A ULTIMA CONTA, pra quem quiser olhar. Vazia enquanto <see cref="Explicando"/> for falso.
	/// Nao alimenta decisao nenhuma -- e a metade de "a decisao vira dado" que cabe nesta camada.
	/// </summary>
	/// (E o relato e escrito com um `if (Explicando)` na frente e nao por um `Func&lt;string&gt;`
	/// preguicoso: a <see cref="Percepcao"/> viaja como `in`, e parametro `in` nao pode ser
	/// capturado por lambda. O `if` cru tambem e o unico jeito de a interpolacao nem existir.)
	public string Porque { get; private set; } = "";

	/// <summary>
	/// AS CAPACIDADES VENCERAM? O servidor pergunta, e so paga a leitura (que varre catalogo de
	/// formas) quando este metodo diz sim. E o rodizio da camada cara: 1 Hz por corpo.
	/// </summary>
	public bool PrecisaLerCapacidades(double dt)
	{
		_relogioDasCapacidades -= dt;
		if (_relogioDasCapacidades > 0) return false;
		_relogioDasCapacidades = 1.0;
		return true;
	}

	/// <summary>
	/// EU VI UM GOLPE CHEGAR. Chamado pelo servidor quando este corpo e ALVO de um ataque --
	/// acertando, sendo aparado ou errando, tanto faz: o que se ve e o gesto.
	///
	/// ============================ E ISTO E TUDO O QUE ELE SABE DO OPONENTE ============================
	/// O servidor tem na mao o `alvo.Combate.Recarga` e o `alvo.AtaqueAte` -- daria pra saber o
	/// instante exato do proximo soco e apara-lo sempre. Seria uma IA impossivel de enganar e
	/// chata de enfrentar, porque o jogador nao teria o que descobrir.
	///
	/// Em vez disso ele guarda as ULTIMAS QUATRO vezes em que apanhou, estima a cadencia pela
	/// media dos intervalos e prepara a guarda pra o proximo. **Erra quando o jogador quebra o
	/// ritmo** -- que e exatamente a coisa que se quer que o jogador descubra sozinho.
	/// ============================================================================================
	/// </summary>
	public void ViuUmGolpe()
	{
		_quandoVi[_marcas % MarcasDeGolpe] = _relogio;
		_marcas++;
	}

	/// <summary>
	/// A CADENCIA ESTIMADA do oponente, em segundos. Zero = ainda nao da pra dizer.
	///
	/// Duas marcas ja dao um intervalo, mas um intervalo so e ruido; com tres ele ja tem media de
	/// dois. O PRIMEIRO golpe de uma luta portanto NUNCA e aparado por antecipacao, e isso e o
	/// certo: ninguem apara o que ainda nao viu acontecer.
	/// </summary>
	private double Cadencia()
	{
		int n = Math.Min(_marcas, MarcasDeGolpe);
		if (n < 3) return 0;

		// as marcas estao num anel; ordena por valor, que e o mesmo que ordenar por tempo
		Span<double> t = stackalloc double[MarcasDeGolpe];
		for (int i = 0; i < n; i++) t[i] = _quandoVi[i];
		t[..n].Sort();

		double soma = 0;
		for (int i = 1; i < n; i++) soma += t[i] - t[i - 1];
		double media = soma / (n - 1);

		// cadencia absurda (dois golpes no mesmo tique, ou um minuto entre eles) nao e ritmo
		return media is > 0.15 and < 4.0 ? media : 0;
	}

	/// <summary>
	/// PENSA E DEVOLVE O QUE APERTAR. Chamado a CADA tique -- os reflexos moram aqui dentro, e o
	/// plano so e reconsiderado quando o relogio de decisao vira.
	/// </summary>
	public Comando Pensar(in Percepcao p, double dt, Random rng)
	{
		_relogio += dt;
		_noPlano += dt;
		if (_guardaAte > 0) _guardaAte -= dt;
		if (_reacaoDaGuarda > 0) _reacaoDaGuarda -= dt;
		if (_carenciaDeForma > 0) _carenciaDeForma -= dt;
		if (_hesitaAte > 0) _hesitaAte -= dt;
		if (_voltaDoSoco > 0) _voltaDoSoco -= dt;
		if (_pausaDoTiro > 0) _pausaDoTiro -= dt;
		if (Atual == Plano.Recuperar) _carregandoHa += dt;

		// A CONJURACAO ANDA SOZINHA enquanto o plano estiver de pe. Quem a ZERA e a receita, quando
		// o corpo precisa se mexer (alvo colado demais) -- pelo mesmo motivo que a recarga de Ki nao
		// acontece andando: um gesto que exige o pe plantado nao sobrevive a um passo.
		if (Atual == Plano.Atirar && _pausaDoTiro <= 0) _conjurandoHa += dt;

		// CAIDO NAO FAZ NADA. Nem aperta tecla, nem guarda -- o `Guardar` do Core ja recusa em
		// corpo nocauteado, mas mandar o comando mesmo assim seria a IA apertando teclas que a
		// tela do jogador nem aceita.
		if (p.Caido) { Atual = Plano.Nada; Porque = "caido"; return Comando.Nenhum; }

		// --- 1. REFLEXOS (30 Hz, por fora da decisao) -------------------------
		bool susto = Sustos(p, rng);
		Reflexos(p, rng);

		// --- 2. DECISAO (na cadencia do tempero) ------------------------------
		if (_relogioDeDecisao <= 0 || susto)
		{
			_relogioDeDecisao = IntervaloDeDecisao;
			Repensar(p, rng);
		}
		else _relogioDeDecisao -= dt;

		// --- 3. O COMANDO, montado do plano + reflexos ------------------------
		return Montar(p, rng);
	}

	/// <summary>
	/// O QUE ASSUSTA: o alvo ficou muito mais forte de repente (transformou).
	///
	/// A hesitacao que isso produz e curta (0,15-0,40 s) e nao e cosmetica: durante ela o corpo
	/// recua guardando em vez de continuar avancando pra dentro de um golpe que agora vale o dobro.
	/// E tambem e o unico jeito de a transformacao do JOGADOR ter um efeito visivel no oponente --
	/// sem isto o NPC nao muda nada quando voce vira Super Saiyajin na cara dele.
	/// </summary>
	private bool Sustos(in Percepcao p, Random rng)
	{
		if (!p.TemAlvo) { _ultimoPoderDoAlvo = 0; return false; }

		double antes = _ultimoPoderDoAlvo;
		_ultimoPoderDoAlvo = p.PoderDoAlvo;
		if (antes <= 0 || p.PoderDoAlvo <= antes * 1.5) return false;

		_hesitaAte = HesitacaoMin + rng.NextDouble() * (HesitacaoMax - HesitacaoMin);
		_guardaAte = Math.Max(_guardaAte, _hesitaAte);
		Porque = "susto: o alvo ficou mais forte";
		return true;
	}

	/// <summary>
	/// OS REFLEXOS. Rodam a cada tique porque bloquear um soco nao pode esperar a proxima pensada.
	///
	/// Sao os tres do desenho, e cada um responde a uma coisa que ja aconteceu:
	///   1. RITMO       -- o proximo golpe esta pra chegar (ver <see cref="Cadencia"/>);
	///   2. PRESSAO     -- estou atordoado ou muito ferido, com ele colado (`npc_defensive_check`);
	///   3. SOLTAR      -- a pressao passou (`prob(40)` do mesmo proc).
	/// </summary>
	private void Reflexos(in Percepcao p, Random rng)
	{
		// A GUARDA DE VERDADE SOBE QUANDO O ATRASO DE REACAO VENCE. Sem isto o reflexo seria
		// instantaneo, que e a marca registrada de robo.
		if (_reacaoDaGuarda > 0) return;
		if (Disciplina <= 0) { _guardaAte = 0; return; }   // a fera nao apara: nao ha o que reagir
		if (!Poderes.TemComQueAparar) { _guardaAte = 0; return; }
		if (!p.TemAlvo || p.AlvoCaido) return;

		bool colado = p.Distancia <= DistanciaIdeal * 2f && p.EleMeAlcanca;

		// --- 3. SOLTAR: a pressao passou -----------------------------------
		// ============================ A CHANCE DO DM E POR VOLTA DO LACO, NAO POR TIQUE ============================
		// `prob(40)` (`NPCAI.dm:289`) roda dentro do `chaseState`, que dorme `chase_speed = 3`
		// tiques do DM -- 0,3 s. Aqui o reflexo roda a 30 Hz, ou seja 9 vezes naquele mesmo tempo.
		// Copiar o 40 cru daria uma guarda que cai no primeiro quadro em que a pressao alivia.
		//
		// A conversao honesta e `1 - (1-p)^9 = 0,40` -> p ~= 0,055; o fator 0,1 da 0,040, que fica
		// do lado seguro (solta um pouco mais devagar do que o DM). O numero exato importa menos
		// que a UNIDADE estar certa -- e este projeto ja pagou caro por um `sleep` convertido pela
		// cadencia errada.
		// ====================================================================================================
		if (_guardaAte > 0)
		{
			if (!p.Atordoado && p.VidaFrac > VidaAliviada && rng.NextDouble() < ChanceDeSoltarAGuarda * 0.1)
				_guardaAte = 0;
			return;
		}

		if (!colado) return;

		// ERRO OCASIONAL. Ele nao e ruido: e o que faz o jogador poder VENCER no ritmo, e o que
		// impede a taxa de bloqueio de ser 100% -- que e o tipo de numero que denuncia um robo.
		if (rng.NextDouble() < ChanceDeErrar) return;

		// --- 2. PRESSAO (o `npc_defensive_check`, NPCAI.dm:291) -------------
		bool pressionado = p.Atordoado || p.VidaFrac <= VidaPressionada;
		bool porPressao = pressionado && Disciplina >= 0.35 && rng.NextDouble() < ChanceDeApararSobPressao;

		// --- 1. RITMO (o brace do `attack()`, NPCAI.dm:190) -----------------
		bool porRitmo = false;
		double cad = Cadencia();
		if (cad > 0)
		{
			double desdeOUltimo = _relogio - UltimaMarca();
			// a janela abre um tempo de reacao ANTES do golpe esperado e fecha logo depois dele:
			// antecipar cedo demais e guardar a luta inteira, e tarde demais e nao guardar.
			double falta = cad - desdeOUltimo;
			if (falta <= TempoDeReacao && falta > -cad * 0.4)
				porRitmo = rng.NextDouble() < ChanceDeAntecipar + 0.40 * Disciplina;
		}

		if (!porPressao && !porRitmo) return;

		_reacaoDaGuarda = Reagir(rng);
		_guardaAte = _reacaoDaGuarda + GuardaMin + rng.NextDouble() * (GuardaMax - GuardaMin);
	}

	private double UltimaMarca()
	{
		double maior = 0;
		for (int i = 0; i < MarcasDeGolpe; i++) if (_quandoVi[i] > maior) maior = _quandoVi[i];
		return maior;
	}

	/// <summary>
	/// UM TEMPO DE REACAO, com variacao E com DERIVA.
	///
	/// ============================ DERIVA, E NAO RUIDO BRANCO ============================
	/// Sortear um numero independente a cada vez produz um oponente que reage em 120 ms, depois em
	/// 380, depois em 130 -- e isso NAO le como pessoa, le como tremor. Gente tem periodos: fica
	/// lenta por uns segundos, depois afia. Entao o sorteio de cada evento e um SINO estreito em
	/// volta de um centro que passeia devagar (<see cref="_deriva"/>), e e o passeio que da a
	/// impressao de atencao variando.
	/// ================================================================================
	/// </summary>
	private double Reagir(Random rng)
	{
		_deriva = Math.Clamp(_deriva + (rng.NextDouble() - 0.5) * 0.06, -0.35, 0.35);

		// sino barato: a soma de dois uniformes ja concentra no meio e some nas pontas
		double sino = (rng.NextDouble() + rng.NextDouble()) * 0.5;
		double t = TempoDeReacao * (1 + _deriva) * (0.6 + 0.8 * sino);
		return Math.Max(ReacaoMinima, t);
	}

	/// <summary>
	/// A DECISAO. Escolhe UM plano, e o compromisso o segura por
	/// <see cref="TempoMinimoNoPlano"/> a menos que uma INTERRUPCAO NOMEADA o quebre.
	///
	/// ============================ POR QUE O COMPROMISSO E O QUE FAZ PARECER GENTE ============================
	/// Sem ele, duas opcoes quase empatadas trocam de lugar a cada sorteio e o corpo fica indo e
	/// vindo no lugar -- o defeito que todo mundo reconhece como "IA" sem saber nomear. Com ele, o
	/// NPC **se compromete com um erro** por mais de um segundo, que e o que uma pessoa faz.
	///
	/// As interrupcoes sao nomeadas de proposito (e nao "se a pontuacao mudou muito"): cada uma e
	/// uma frase que da pra ler em jogo -- *"parei de carregar porque ele chegou perto"*.
	/// ====================================================================================================
	/// </summary>
	private void Repensar(in Percepcao p, Random rng)
	{
		// ============================ UM TIRO MORRE SEM A DECISAO MUDAR DE IDEIA ============================
		// Esta linha existe por causa de um buraco que a bancada achou, e o buraco e sobre a FORMA do
		// motor e nao sobre o tiro: as interrupcoes nomeadas so sao consultadas quando o plano NOVO e
		// diferente do atual (`if (novo == antes) return`). Um corpo apanhando no meio da conjuracao
		// continua achando que atirar e a melhor ideia -- o plano escolhido continua sendo `Atirar` --
		// e o `Interrompe` nunca chegava a ser perguntado. O golpe saia como se nada tivesse acontecido.
		//
		// As outras receitas nao tinham o problema porque as condicoes delas mudam a ESCOLHA (o tanque
		// enche e a recarga deixa de ser escolhida). Conjurar e o primeiro gesto deste cerebro cujo
		// fracasso nao muda de opiniao -- e por isso o aborto e explicito, aqui, antes de escolher.
		//
		// E ele COBRA a pausa: quem apanha no meio do gesto perde o gesto e mais um tempo, senao o
		// aborto vira um recomeco instantaneo e o jogador que acertou o soco nao ganhou nada com isso.
		// ==============================================================================================
		if (Atual == Plano.Atirar && TiroMorreu(p)) AbortarTiro();

		Plano antes = Atual;
		Plano novo = Escolher(p, rng);
		if (novo == antes) return;

		if (_noPlano < TempoMinimoNoPlano && !Interrompe(p, novo)) return;
		Atual = novo;
		_noPlano = 0;

		// o `startHP` do `rechargeState` (NPCAI.dm:671): a conta do "apanhei demais carregando" e
		// contra a vida de QUANDO COMECOU, e nao contra um limiar absoluto.
		if (novo == Plano.Recuperar) { _vidaAoCarregar = p.VidaFrac; _carregandoHa = 0; }

		// MESMA CONTA PRA CONJURACAO, e pelo mesmo motivo: quem apanha no meio do gesto larga o
		// gesto. Sem zerar o relogio aqui, um plano de atirar retomado herdaria a conjuracao do
		// anterior e o golpe sairia no primeiro tique -- sem a parada que o jogador precisa ver.
		if (novo == Plano.Atirar) { _vidaAoConjurar = p.VidaFrac; _conjurandoHa = 0; }
	}

	/// <summary>
	/// AS INTERRUPCOES NOMEADAS -- o que quebra um compromisso antes da hora.
	///
	/// Cada linha e um acontecimento, e nao uma comparacao de pontos: e por isso que da pra
	/// explicar o comportamento pra quem esta jogando contra.
	/// </summary>
	private bool Interrompe(in Percepcao p, Plano novo) =>
		!p.TemAlvo || p.AlvoCaido                                        // o alvo saiu de cena
		|| novo == Plano.Nada
		|| (Atual == Plano.Recuperar && p.Distancia < DistanciaSegura * 0.6f)   // ele chegou perto
		|| (Atual == Plano.Recuperar && p.VidaFrac < _vidaAoCarregar - DanoQueAbortaACarga)
		|| (Atual == Plano.Recuperar && _carregandoHa > PrazoDaRecarga)
		|| (Atual == Plano.Recuperar && p.KiFrac >= CarregarAte)
		|| (Atual == Plano.Alcancar && p.AlcancoPelaAltura)                     // ja alcanco: chega de subir
		|| (Atual == Plano.Escalar && !Poderes.PodeSubirForma && !Poderes.FormaSoFaltaKi);
	// O TIRO NAO APARECE NESTA LISTA de proposito: ele e abortado ANTES da escolha (ver `Repensar`),
	// porque a morte dele nao muda o plano escolhido -- e uma linha aqui seria codigo inalcancavel.

	/// <summary>
	/// A ORDEM DAS PERGUNTAS. E a mesma do `chaseState` do DM (`NPCAI.dm:510-529`): fugir/escalar
	/// primeiro (sao decisoes sobre a luta inteira), recurso depois, e so entao a briga.
	/// </summary>
	private Plano Escolher(in Percepcao p, Random rng)
	{
		// SEM ALVO, ANDA -- e a fera vagando. O ponto pra onde ela vai chega em `DoAlvo` mesmo com
		// `TemAlvo` falso (ver `LerPercepcao`), e ele nunca e alcancado, porque e recalculado a
		// partir da posicao ATUAL: e o que a faz ANDAR na direcao em vez de parar num destino.
		if (!p.TemAlvo) { Porque = "vagar"; return Plano.Vagar; }
		if (p.AlvoCaido) { Porque = "alvo no chao"; return Plano.Nada; }

		// --- ESCALAR ------------------------------------------------------
		// `if(HP <= 45 || target.expressedBP >= expressedBP * 1.5) npc_power_up()` (NPCAI.dm:517),
		// que chama o `npc_try_transform` (:254). A carencia e o `ai_powerup_cd` (:240).
		bool valeEscalar = p.VidaFrac <= VidaQuePedeForma || p.RazaoDePoder >= PoderQuePedeForma;
		if (valeEscalar && _carenciaDeForma <= 0)
		{
			if (Poderes.PodeSubirForma && p.KiFrac >= KiParaTransformar)
			{
				if (Explicando) Porque = $"escalar: vida {p.VidaFrac:0.00}, razao de poder {p.RazaoDePoder:0.0}";
				return Plano.Escalar;
			}

			// A PRE-CONDICAO REPARAVEL. Falta folego pra a forma -- e folego se resolve carregando.
			// E a unica cadeia de duas etapas desta camada, e ela vem do proprio gate do DM
			// (`Ki < MaxKi * 0.25`): la o NPC simplesmente desiste; aqui ele vai buscar o Ki.
			if ((Poderes.FormaSoFaltaKi || (Poderes.PodeSubirForma && p.KiFrac < KiParaTransformar))
				&& Poderes.SabeReunirKi && Inteligencia >= 0.35)
			{
				// NAO HA UM `bool _voltarPraEscalar` GUARDADO, e a falta e de proposito: quando o
				// tanque chegar em `CarregarAte` (0,75) a interrupcao nomeada solta a recarga, esta
				// mesma funcao roda de novo e `p.KiFrac >= KiParaTransformar` (0,25) ja e verdade --
				// ela cai no ramo de cima sozinha. Um campo de intencao seria uma segunda verdade
				// sobre o que ele quer, e a que fica velha e sempre a segunda.
				Porque = "carregar PRA escalar (falta folego pra a forma)";
				return Plano.Recuperar;
			}
		}

		// --- RECUPERAR ----------------------------------------------------
		// `if(!ai_recharging && kr <= KI_CRIT && sr <= STAM_CRIT && prob(ai_intelligence))`
		// (NPCAI.dm:522). O gate probabilistico E a inteligencia: bicho burro morre batendo.
		if (Poderes.SabeReunirKi && p.KiFrac <= KiCritico && p.FolegoFrac <= FolegoCritico
			&& rng.NextDouble() < Inteligencia)
		{
			if (Explicando) Porque = $"recuperar: ki {p.KiFrac:0.00}, folego {p.FolegoFrac:0.00}";
			return Plano.Recuperar;
		}

		// --- ATIRAR (O GANCHO: hoje ele SEMPRE sai na primeira linha) ------
		// ============================ POR QUE AQUI, E NAO DEPOIS DO VOO ============================
		// A ordem das perguntas E a maior parte do comportamento, e esta posicao diz duas coisas:
		//
		//   * DEPOIS de escalar e de recarregar, porque as duas sao decisoes sobre a luta INTEIRA e
		//     um golpe de longe nao resolve estar fraco nem estar sem folego;
		//   * ANTES de alcancar, e este e o ponto interessante: quando o alvo esta num andar que ele
		//     nao encosta, atirar RESOLVE o mesmo problema que decolar -- e sem pagar o dreno de voo,
		//     que e o mais caro do jogo. Um lutador com raio nao sobe atras de quem esta voando; ele
		//     mira. Deixar o voo primeiro produziria o NPC que gasta o tanque subindo pra socar quem
		//     ele podia ter acertado do chao, e ninguem entenderia por que.
		// ======================================================================================
		if (EscolherTiro(p, out int qual))
		{
			_tiroEscolhido = qual;
			if (Explicando)
			{
				Tiro t = Poderes.DeLonge[qual];
				Porque = $"atirar {t.Id}: {p.Distancia:0} px na janela [{t.AlcanceMin:0}, {t.AlcanceMax:0}], "
					   + $"risco {Arsenal.RiscoDeErrar(t, p.Distancia, p.AlvoSeMovendo):0.00}";
			}
			return Plano.Atirar;
		}

		// --- ALCANCAR (DESENHO NOVO: o DM nao voa) -------------------------
		// Duas razoes pra subir, e as duas sao sobre ALCANCE e nao sobre gosto:
		//   * ele esta num andar de onde eu nao encosto -- a briga simplesmente nao acontece;
		//   * ele esta no chao e eu sou esperto o bastante pra explorar a assimetria do andar 1
		//     (`Voo.PodeAcertar`): eu bato nele, ele nao bate em mim.
		if (Poderes.PodeVoar && p.KiFrac >= KiParaDecolar)
		{
			bool naoAlcanco = !p.AlcancoPelaAltura;
			bool rasanteVale = Inteligencia >= InteligenciaQuePairaRasante
							   && p.AndarDoAlvo == 0 && !p.AlvoVoando && p.MeuAndar != 1;
			if (naoAlcanco || rasanteVale)
			{
				Porque = naoAlcanco ? "alcancar: ele esta noutro andar" : "pairar rasante: bato sem levar";
				return Plano.Alcancar;
			}
		}

		// --- RECUAR -------------------------------------------------------
		// O "respirar" que este cerebro sempre teve. `VidaCautelosa = 0` (a fera) o desliga.
		if (VidaCautelosa > 0 && p.VidaFrac < VidaCautelosa && rng.NextDouble() < 0.35)
		{
			if (Explicando) Porque = $"recuar: vida {p.VidaFrac:0.00}";
			return Plano.Recuar;
		}

		Porque = "pressionar";
		return Plano.Pressionar;
	}

	/// <summary>
	/// HA UM GOLPE DE LONGE QUE VALE A PENA AGORA? **Hoje devolve falso na primeira linha, sempre.**
	///
	/// ============================ ESTA FUNCAO E O GANCHO INTEIRO ============================
	/// E aqui que a decisao de "atacar de longe" nasce -- e ela faz, nesta ordem, as cinco perguntas
	/// que o dono nomeou, cada uma respondida por um dado que ja existe:
	///
	///   1. ALCANCE       -- `p.Distancia <= t.AlcanceMax`. Perto demais nao reprova aqui: a receita
	///                       sabe abrir distancia, e e dai que sai o "kitar" sem codigo de kite.
	///   2. LINHA DE VISAO-- `p.LinhaLivre`, tracada pelo servidor SO quando ha arsenal (e por isso
	///                       custa zero hoje). Falso = nao sei = nao atira.
	///   3. CUSTO DE KI   -- o Ki tem que sobrar DEPOIS do golpe, e sobrar o bastante pra nao cair do
	///                       ceu: a mesma reserva do `DeveDescerDoCeu`, porque largar um raio e
	///                       despencar em seguida e pior do que nao ter atirado.
	///   4. RISCO DE ERRAR-- `Arsenal.RiscoDeErrar` contra a <see cref="ToleranciaDeErro"/>, que sai
	///                       da inteligencia. E medido na distancia em que ele VAI atirar, e nao na
	///                       de agora -- senao um corpo colado recusaria o golpe que ele so daria
	///                       depois de recuar.
	///   5. TEMPO DE CONJURACAO -- nao entra na escolha, entra na RECEITA: e o tempo de pe parado, e
	///                       e o que da ao jogador a janela pra interromper. Ver <see cref="Disparo"/>.
	///
	/// A ESCOLHA entre dois golgos viaveis e `(1 - risco) * CustoDeKi`. O custo entra como PROXY de
	/// tamanho: neste jogo o dreno E o tamanho da tecnica (o DM cobra `100*BaseDrain`), entao o mais
	/// caro e o mais forte. Se um dia isso deixar de valer, o conserto e um campo `Poder` na linha da
	/// tabela -- dado --, e nao uma regra nova aqui.
	/// ====================================================================================
	/// </summary>
	private bool EscolherTiro(in Percepcao p, out int qual)
	{
		qual = -1;

		// A PODA, NA PRIMEIRA LINHA. Um `int` contra zero, 4 vezes por segundo por corpo: e este o
		// custo total do gancho enquanto nao houver ataque de longe no jogo.
		if (!Poderes.DeLonge.TemAlguma) return false;
		if (!p.TemAlvo || p.AlvoCaido) return false;
		if (_pausaDoTiro > 0) return false;

		// SO QUEM E ESPERTO ABRE DISTANCIA PRA ATIRAR. O burro usa o golpe se ele ja estiver na
		// janela, e senao continua avancando pra socar -- o mesmo desenho do `prob(ai_intelligence)`
		// que decide quem recarrega (`NPCAI.dm:522`). Erro CARACTERISTICO: "aquele bicho atira de
		// longe se voce der espaco, mas se voce colar nele ele esquece que tem raio".
		bool sabeRecuarPraAtirar = Inteligencia >= 0.35;

		double melhorNota = 0;
		for (int i = 0; i < Poderes.DeLonge.Quantas; i++)
		{
			Tiro t = Poderes.DeLonge[i];

			if (p.Distancia > t.AlcanceMax) continue;
			if (p.Distancia < t.AlcanceMin && !sabeRecuarPraAtirar) continue;
			if (t.PrecisaDeLinhaLivre && !p.LinhaLivre) continue;
			if (p.Ki - t.CustoDeKi < ReservaDeKi(p)) continue;

			// o risco medido ONDE ele vai atirar: dentro da janela, e nao onde ele esta agora
			float daJanela = Math.Clamp(p.Distancia, t.AlcanceMin, t.AlcanceMax);
			double risco = Arsenal.RiscoDeErrar(t, daJanela, p.AlvoSeMovendo);
			if (risco > ToleranciaDeErro) continue;

			double nota = (1 - risco) * t.CustoDeKi;
			if (nota <= melhorNota) continue;
			melhorNota = nota;
			qual = i;
		}
		return qual >= 0;
	}

	/// <summary>
	/// O KI QUE ELE NAO GASTA. No chao, nenhum -- o tanque vazio so custa velocidade. No ar, a
	/// MESMA folga do <see cref="DeveDescerDoCeu"/>: um golpe que o derruba do ceu nao valeu a pena,
	/// porque quem cai chega no chao sem guarda e no meio do inimigo.
	/// </summary>
	private static double ReservaDeKi(in Percepcao p) => p.EstouVoando ? Voo.KiQueDerruba * 4 : 0;

	/// <summary>
	/// O TIRO EM CURSO MORREU? As interrupcoes NOMEADAS do plano de atirar -- cada uma e uma frase
	/// que da pra ler em jogo, e nao uma comparacao de pontos.
	/// </summary>
	private bool TiroMorreu(in Percepcao p)
	{
		// O ARSENAL ENCOLHEU DEBAIXO DELE. As capacidades sao relidas a 1 Hz: uma skill esquecida,
		// um debuff, uma forma que fechou a linha -- e o indice guardado passa a apontar pra fora.
		// Sem esta linha seria um `IndexOutOfRange` dentro do `try` por corpo, ou seja um NPC virando
		// estatua sem ninguem saber por que.
		if (_tiroEscolhido < 0 || _tiroEscolhido >= Poderes.DeLonge.Quantas) return true;

		Tiro t = Poderes.DeLonge[_tiroEscolhido];
		return p.Distancia > t.AlcanceMax                              // ele saiu do alcance
			|| (t.PrecisaDeLinhaLivre && !p.LinhaLivre)                // entrou parede no meio
			|| p.Ki - t.CustoDeKi < ReservaDeKi(p)                     // o folego acabou
			|| p.VidaFrac < _vidaAoConjurar - DanoQueAbortaACarga      // apanhou demais conjurando
			|| _conjurandoHa > PrazoDaConjuracao;                      // nunca completa: desiste
	}

	/// <summary>
	/// LARGA O GESTO. Zera a conjuracao, esquece a opcao escolhida, paga a pausa e devolve o corpo
	/// pra decisao livre -- o <see cref="_noPlano"/> e liberado porque um plano que acabou de morrer
	/// nao pode segurar o compromisso contra o proximo.
	/// </summary>
	private void AbortarTiro()
	{
		_pausaDoTiro = Math.Max(_pausaDoTiro, PausaDepoisDoTiro);
		_conjurandoHa = 0;
		_tiroEscolhido = -1;
		Atual = Plano.Nada;
		_noPlano = TempoMinimoNoPlano;
		Porque = "o tiro morreu no meio";
	}

	/// <summary>
	/// ABRIR DISTANCIA, PLANTAR O PE, CONJURAR, SOLTAR. **Nunca executada hoje** -- ver <see cref="Plano.Atirar"/>.
	///
	/// As tres fases sao visiveis de fora, e isso e o ponto: o jogador ve o NPC recuar, ve ele parar
	/// e ve o golpe sair. E na parada que mora a janela pra interromper -- um NPC que atira no mesmo
	/// tique em que decide nao da nada pro jogador fazer.
	/// </summary>
	private Comando Disparo(in Percepcao p, bool guarda)
	{
		if (_tiroEscolhido < 0 || _tiroEscolhido >= Poderes.DeLonge.Quantas)
			return new Comando { Guardar = guarda };

		Tiro t = Poderes.DeLonge[_tiroEscolhido];

		// acabou de atirar: espera a pausa de pe, mirado, sem recomecar a conjuracao
		if (_pausaDoTiro > 0)
			return new Comando { Guardar = guarda, Marcar = p.IdDoAlvo };

		// COLADO DEMAIS: recua, e a conjuracao VOLTA A ZERO. Nao e punicao -- e a mesma regra do
		// `rechargeState`: gesto que pede o pe plantado nao sobrevive a um passo.
		if (p.Distancia < t.AlcanceMin)
		{
			_conjurandoHa = 0;
			return new Comando { Rumo = p.Direcao * -1f, Guardar = guarda, Marcar = p.IdDoAlvo };
		}

		// NA JANELA: para e conjura. `Rumo` zero de proposito.
		if (_conjurandoHa < t.TempoDeConjuracao)
			return new Comando { Guardar = guarda, Marcar = p.IdDoAlvo };

		// SOLTA -- um PULSO so, pelo mesmo canal do jogador. Quem cobra recarga, Ki e "voce nao sabe
		// isso" e a tecnica, la no `UsarHabilidade`; aqui nao se confere nada.
		_pausaDoTiro = PausaDepoisDoTiro;
		_conjurandoHa = 0;
		return new Comando { Habilidade = t.Id, Marcar = p.IdDoAlvo, Guardar = guarda };
	}

	/// <summary>
	/// DO PLANO PRA AS TECLAS. Todo tique -- as teclas de ESTADO precisam ser reafirmadas, e as de
	/// PULSO so saem no tique em que valem.
	/// </summary>
	private Comando Montar(in Percepcao p, Random rng)
	{
		bool guarda = _guardaAte > 0 && _reacaoDaGuarda <= 0;

		// HESITANDO: recua guardando e nao ataca. Curto -- ver `Sustos`.
		if (_hesitaAte > 0)
			return new Comando { Rumo = p.Direcao * -1f, Guardar = guarda, QuerDescer = false };

		return Atual switch
		{
			Plano.Escalar => Escalada(guarda),
			Plano.Atirar => Disparo(p, guarda),
			Plano.Recuperar => Recuperacao(p, guarda),
			Plano.Alcancar => Alcance(p, guarda, rng),
			Plano.Recuar => new Comando { Rumo = p.Direcao * -1f, Guardar = guarda, QuerDescer = QuerDescerPara(p), QuerSubir = QuerSubirPara(p) },
			Plano.Pressionar => Pressao(p, guarda, rng),

			// VAGAR: so o rumo. Sem soco (o ponto nao e ninguem) e sem guarda (nao ha de quem se
			// defender) -- e exatamente o que o cerebro antigo fazia quando `posAlvo` era um ponto.
			Plano.Vagar => new Comando { Rumo = p.Direcao },

			_ => new Comando { Guardar = guarda },
		};
	}

	/// <summary>
	/// SUBIR UM DEGRAU. Um PULSO so, e depois a carencia -- sem ela o corpo apertaria C 4 vezes por
	/// segundo contra uma porta fechada, que e o `if(!hasssj) return` do DM visto de fora
	/// (`NPCAI.dm:361`: *"never forced onto a random monster"*).
	///
	/// E ele PARA pra transformar. No anime ninguem vira Super Saiyajin correndo, e mecanicamente e
	/// o certo tambem: e o instante em que ele esta vulneravel, e o jogador tem que poder ver.
	/// </summary>
	private Comando Escalada(bool guarda)
	{
		if (_carenciaDeForma > 0) return new Comando { Guardar = guarda };
		_carenciaDeForma = CarenciaDeAscensao;
		return new Comando { SubirForma = true, Guardar = guarda };
	}

	/// <summary>
	/// RECUAR ATE A DISTANCIA SEGURA, PARAR, E SO ENTAO CARREGAR -- o `rechargeState` inteiro
	/// (`NPCAI.dm:673-694`), inclusive a parte que se esquece facil: **enquanto recua, ele NAO
	/// carrega**. O comentario de la explica por que (*"is_drawing deixaria o passo mais lento"*), e
	/// aqui a razao e ainda mais dura: o servidor RECUSA andar carregando (ver o portao do `Input`),
	/// entao um comando com rumo E carga junto sairia como "parado, carregando" -- o NPC ficaria
	/// plantado a um passo do inimigo achando que estava recuando.
	/// </summary>
	private Comando Recuperacao(in Percepcao p, bool guarda)
	{
		if (p.TemAlvo && p.Distancia < DistanciaSegura)
			return new Comando { Rumo = p.Direcao * -1f, Guardar = guarda };

		// longe o bastante: planta o pe e carrega. Rumo ZERO e obrigatorio -- andar SUSPENDE a carga
		// (`CargaDeKi.Passo` recebe o `Moving`), e um NPC que carrega andando nao carrega nada.
		return new Comando { Carregar = true, Guardar = guarda };
	}

	/// <summary>
	/// DECOLAR E SUBIR ATE O ANDAR DELE. **Desenho novo** -- ver o cabecalho.
	///
	/// O toggle do voo e um PULSO e a subida e um ESTADO, e sao as mesmas duas teclas do jogador:
	/// a de ligar o voo e a de segurar espaco.
	/// </summary>
	private Comando Alcance(in Percepcao p, bool guarda, Random rng)
	{
		if (!p.EstouVoando)
			return new Comando { AlternarVoo = true, Guardar = guarda };

		// ja no ar: sobe/desce ate o andar certo e continua indo pra cima dele
		return new Comando
		{
			Rumo = p.Distancia > DistanciaIdeal * 1.4f ? p.Direcao : Vec2.Zero,
			QuerSubir = QuerSubirPara(p),
			QuerDescer = QuerDescerPara(p),
			Guardar = guarda,
			Leve = p.AlcancoPelaAltura && p.Distancia <= DistanciaIdeal * 1.6f && EmRajada(rng),
		};
	}

	/// <summary>
	/// O ESTADO NORMAL: manter a distancia de soco e bater. Com a altitude acompanhando, porque o
	/// alvo pode subir no meio da troca.
	/// </summary>
	private Comando Pressao(in Percepcao p, bool guarda, Random rng)
	{
		Vec2 rumo = Vec2.Zero;
		if (p.Distancia > DistanciaIdeal * 1.4f) rumo = p.Direcao;
		else if (p.Distancia < DistanciaIdeal * 0.6f) rumo = p.Direcao * -1f;

		bool noAlcance = p.Distancia <= DistanciaIdeal * 1.6f && p.AlcancoPelaAltura;
		bool temFolego = p.KiFrac > 0.15;
		bool bate = noAlcance && !guarda && EmRajada(rng);

		// UM SORTEIO SO decide leve/pesado. Dois sorteios independentes (um pra cada campo) davam
		// um tique em que os dois saiam falsos -- a rajada engolia um golpe sem nada explicando.
		bool pesado = bate && temFolego && rng.NextDouble() < ChanceDePesado;

		// DESCER DE PROPOSITO quando o folego acaba no ar. Ser derrubado e uma FALHA: o corpo cai a
		// 16 tiles por segundo e chega no chao sem guarda, sem Ki e no meio do inimigo. Descer usa a
		// MESMA tecla do jogador (segurar control), e nao um desligamento por dentro.
		bool pousar = p.EstouVoando && DeveDescerDoCeu(p);

		return new Comando
		{
			Rumo = rumo,
			Guardar = guarda,
			Leve = bate && !pesado,
			Pesado = pesado,
			QuerSubir = !pousar && QuerSubirPara(p),
			QuerDescer = pousar || QuerDescerPara(p),
		};
	}

	/// <summary>
	/// O RITMO EM RAJADA. Ninguem soca num metronomo: vem uma sequencia, vem uma pausa.
	///
	/// Isto e mecanicamente CERTO alem de parecer melhor -- o `Combo` do `CombatState` (`:85`) so
	/// conta enquanto os golpes ficam dentro da <see cref="Jandirus.Core.Combat.CombatState.JanelaDeCombo"/>,
	/// entao socar em rajada e o que faz o baque escalar de pequeno pra grande. E o NPC **nunca**
	/// ataca no instante exato em que a recarga zera: essa precisao e a assinatura de um robo.
	/// </summary>
	private bool EmRajada(Random rng)
	{
		if (_voltaDoSoco > 0) return false;
		// pausa curta entre golpes da mesma rajada, longa quando a rajada acaba
		_voltaDoSoco = rng.NextDouble() < 0.72 ? 0.05 + rng.NextDouble() * 0.12
											   : 0.45 + rng.NextDouble() * 0.75;
		return true;
	}

	/// <summary>
	/// DESCER DO CEU DE PROPOSITO. Duas contas, e as duas precisam existir:
	///   * a FRACAO pega o lutador grande, cujo 12% ainda sao milhares de Ki;
	///   * o ABSOLUTO pega o pequeno, porque <see cref="Voo.KiQueDerruba"/> e 5 e nao 5%.
	/// O fator 4 e a folga: quatro vezes o Ki que derruba da tempo de descer os 20 tiles.
	/// </summary>
	private static bool DeveDescerDoCeu(in Percepcao p) =>
		p.KiFrac < KiQueMandaPousar || p.Ki < Voo.KiQueDerruba * 4;

	/// <summary>
	/// A QUE ALTURA ELE QUER FICAR, em pixels. **Este e o pedido literal do dono** -- *"acompanhar a
	/// ALTITUDE etc"*.
	///
	/// ============================ O ALVO E O MEIO DO ANDAR, E NAO A ALTURA DO OUTRO ============================
	/// Mirar na altura exata do alvo parece obvio e nao funciona: quem alcanca quem e decidido por
	/// ANDAR (`Voo.Andar`, faixas de 6,67 tiles), e a zona morta que impede o chacoalho tem largura
	/// -- entao parar "perto o bastante" da altura dele pode deixar os dois em andares diferentes,
	/// e ai o NPC fica pairando ao lado do alvo sem conseguir encostar nele, que e pior do que nao
	/// ter subido.
	///
	/// Mirando no MEIO do andar, a zona morta inteira cabe dentro daquele andar (conta:
	/// [(N-0,83)H, (N-0,17)H] esta contido em ((N-1)H, N·H]) -- ou seja, parar em qualquer ponto
	/// tolerado ainda garante o alcance.
	/// ======================================================================================================
	///
	/// CHAO E CHAO: com o alvo em pe e sem a receita de rasante, o desejo e ZERO -- ele desce ate
	/// pousar. Ficar pairando um tiquinho acima do chao daria a vantagem assimetrica do andar 1 de
	/// graca a um bicho burro, e essa vantagem tem que ser uma DECISAO de quem e esperto.
	/// </summary>
	private float AlturaDesejada(in Percepcao p)
	{
		bool rasante = Inteligencia >= InteligenciaQuePairaRasante && p.AndarDoAlvo == 0 && !p.AlvoVoando;
		int andar = rasante ? 1 : p.AndarDoAlvo;
		return andar <= 0 ? 0f : (andar - 0.5f) * (Voo.AlturaMaxima / Voo.Andares);
	}

	private bool QuerSubirPara(in Percepcao p) =>
		p.EstouVoando && p.TemAlvo && AlturaDesejada(p) - p.MinhaAltitude > ZonaMortaDeAltura;

	/// <summary>
	/// Descer tem uma folga a MENOS que subir: pra o CHAO nao ha meio termo. Com a zona morta
	/// valendo tambem pra o zero, o corpo pararia de descer a 71 px -- que e o andar 1 -- e o
	/// "pouso" nunca aconteceria.
	/// </summary>
	private bool QuerDescerPara(in Percepcao p)
	{
		if (!p.EstouVoando || !p.TemAlvo) return false;
		float d = AlturaDesejada(p);
		return p.MinhaAltitude - d > (d <= 0f ? 0f : ZonaMortaDeAltura);
	}

	/// <summary>A folga da zona morta de altura: um terco de andar.</summary>
	public const float ZonaMortaDeAltura = Voo.AlturaMaxima / Voo.Andares / 3f;
}
