using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// OS ATAQUES DE KI QUE VIAJAM -- a primeira entidade deste jogo que nao e um corpo.
///
/// ============================ O QUE NAO EXISTIA, E POR QUE ISTO NAO E O SEGUNDO ============================
/// As ~33 tecnicas ja portadas sao TODAS instantaneas: `AlvoDeTecnica` escolhe alguem por raio e
/// `GolpeG3` aplica o dano no mesmo instante, pelo funil do soco. Nao havia entidade com posicao
/// propria, nao havia opcode de projetil, e o `EntityState` do snapshot so sabia descrever corpo.
/// Entao nao ha "o projetil das 33 tecnicas" pra reusar: este e o primeiro, e as instantaneas
/// continuam instantaneas de proposito (o `Light_Buster` e um golpe de aproximacao, nao um raio
/// mal-feito; vira-lo projetil mudaria a tecnica).
///
/// O que se reusa e tudo que ja existia DEPOIS do impacto: `MeleeResolver.AplicarDanoPronto` fere o
/// membro, `ResolverDesfecho` trata nocaute/morte/Zenkai, `AnunciarGolpe` conta pra zona, e o
/// arremesso e o mesmo `TiquesDeVoo` do soco pesado. A cadeia de DANO e que e outra -- ver
/// `Core.Combat.DanoDeKi`, e o porque esta escrito la.
/// ==========================================================================================================
///
/// ============================ QUEM DECIDE O ACERTO ============================
/// O servidor, sempre, e sem excecao. O cliente nao integra posicao de projetil, nao testa colisao,
/// nao sorteia deflexao e nao sabe quanto doeu: ele recebe NASCEU (com tipo, cor e dono), a POSICAO
/// da cabeca 30 vezes por segundo dentro do snapshot, e MORREU (com o motivo). O `S2C.Hit` do
/// impacto e o mesmo do soco -- quem levou ja recebia esse relato pronto.
/// ==============================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// O TETO -- e ele DISPARA (regra 0.7 da casa)
	// =====================================================================
	/// <summary>
	/// QUANTOS TIROS PODEM ESTAR NO AR NUMA ZONA AO MESMO TEMPO.
	///
	/// ============================ ESTE NUMERO FOI MEDIDO, NAO ESCOLHIDO ============================
	/// A bancada `--projetilteste` (familia 7) cronometra o `TickDosProjeteis` com a zona CHEIA e 12
	/// corpos dentro dela -- mais gente do que este jogo ja teve num pedaco de planeta. O que ela
	/// mediu, em 300 tiques por amostra:
	///
	///      16 tiros x 12 corpos ->    5,9 us/tique
	///      64 tiros x 12 corpos ->   22,8 us/tique
	///     256 tiros x 12 corpos ->   91,2 us/tique   (0,3% do tique de 33,3 ms)
	///
	/// LINEAR EM N, como o desenho promete: o custo e O(tiros x corpos), cada cabeca varrendo a lista
	/// da zona uma vez por sub-passo de meio tile. Nao ha varredura de mapa -- colisao com cenario e
	/// uma consulta de celula --, nao ha alocacao por tiro e a zona sem tiro nenhum custa uma
	/// comparacao.
	///
	/// 256 e onde o teto foi posto porque com ele os projeteis ficam em 0,3% do tique, com tres
	/// ordens de grandeza de folga pro resto do servidor (combate, forma, carga, voo, IA, snapshot).
	/// A bancada REPROVA se um dia passarem de 1/10 do orcamento -- e ai o numero desce aqui.
	///
	/// E O TETO DISPARA DE VERDADE: a familia 6 enche a zona, afirma que ela aceitou exatamente 256 e
	/// que o 257o e RECUSADO -- pelas DUAS portas (a do jogador, `PodeAtirar`, com motivo escrito; e a
	/// interna, `Disparar`, por onde a tecnica customizada vai entrar). Um teto com folga grande
	/// demais e indistinguivel de teto nenhum, e este projeto ja pagou esse erro.
	/// ==============================================================================================
	/// </summary>
	public const int MaxProjeteisPorZona = 256;

	/// <summary>
	/// O teto do MUNDO. Existe pra que N zonas cheias nao somem N x 256 -- o orcamento de tique e
	/// do servidor inteiro, nao de uma zona. Quatro zonas lotadas ja e mais briga do que este jogo
	/// jamais teve num lugar so.
	/// </summary>
	public const int MaxProjeteisNoMundo = 1024;

	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>
	/// Os tiros vivos, POR ZONA. O dicionario por zona (e nao uma lista unica varrida com `if`) e o
	/// que faz um planeta em paz custar zero: a zona sem tiro nem aparece aqui.
	/// </summary>
	private readonly Dictionary<ulong, List<Projetil>> _projeteis = [];

	/// <summary>Quantos tiros vivos no mundo inteiro -- o contador do teto de cima, sem varredura.</summary>
	private int _projeteisVivos;

	/// <summary>Ids proprios, longe dos de corpo: o cliente guarda os dois em mapas separados.</summary>
	private int _proximoProjetil = 1;

	/// <summary>
	/// QUEM ESTA CARREGANDO OU SEGURANDO UM RAIO. Fora do <see cref="ServerPlayer"/> pelo mesmo
	/// motivo das outras tecnicas: e estado de sessao de UMA tecnica, e um campo por tecnica na
	/// ficha de todo mundo faz a ficha crescer uma linha por skill portada.
	/// </summary>
	private readonly Dictionary<int, CanalDeKi> _canais = [];

	/// <summary>Recarga do <c>Basic_Blast</c> -- `basicCD` do DM, por corpo.</summary>
	private readonly Dictionary<int, long> _blastPronto = [];

	/// <summary>
	/// QUEM SAIU LEVA A RECARGA JUNTO -- inscrito no `EsquecerTecnicas`. O canal (`_canais`) e a
	/// paralisia tem funis proprios (`SoltarDoRaio`, `EsquecerParalisia`), porque soltar um raio e mais
	/// que esquecer: a zona precisa ver o canal fechar.
	/// </summary>
	private void EsquecerProjeteis(int id) => _blastPronto.Remove(id);

	/// <summary>
	/// QUEM ESTA COM AS PERNAS TRANCADAS, e ate quando. O `paralyzed`/`paralysistime` do DM.
	///
	/// ============================ POR QUE NAO E O `Stun` DO COMBATE ============================
	/// O `CombatState.Stun` que ja existe recusa ATACAR (`PodeAtacar`) e zera a chance de defletir --
	/// e paralisia no DM nao faz nenhuma das duas. Quem levou uma Paralysis continua socando, continua
	/// bloqueando e continua defletindo o proximo tiro: o que ele nao consegue e SAIR DO LUGAR
	/// (`movement handler.dm:89`). Enfiar isso no Stun transformaria a tecnica de "voce nao foge" em
	/// "voce nao existe por dez segundos", que e outra tecnica.
	///
	/// Por isso ela entra pelo <see cref="PodeMexerOCorpo"/>, que e o funil de VETOR -- o mesmo do
	/// nocaute, da tecla C, do raio na mao e do embate. E entrando ali ela vale pra IA tambem, sem
	/// uma linha a mais: um NPC paralisado para de andar pela mesma regra que o jogador.
	/// ==========================================================================================
	/// </summary>
	private readonly Dictionary<int, long> _paralisadoAte = [];

	/// <summary>
	/// ESTE CORPO ESTA PARALISADO AGORA? Com a fresta do DM junto.
	///
	/// `if(paralyzed) { outToWork = rand(1,12); if(outToWork<=11) mobTime = 0 }`
	/// (`movement handler.dm:89`): UMA EM DOZE tentativas de passo escapa, e o proprio jogo comenta
	/// isso na tela (*"You manage to move despite your paralysis"*). Nao e ruido -- e o que impede a
	/// tecnica de ser uma sentenca: quem esta paralisado se arrasta, nao vira estatua.
	///
	/// O SORTEIO E POR CONSULTA, e a consulta e por tique de movimento -- o mesmo lugar em que o DM
	/// o faz. Sortear uma vez e guardar daria um resultado que dura o efeito inteiro.
	/// </summary>
	private bool Paralisado(int id)
	{
		if (!_paralisadoAte.TryGetValue(id, out long ate)) return false;
		if (NowMs() >= ate) { _paralisadoAte.Remove(id); return false; }
		return _rng.Next(1, 13) <= 11;
	}

	/// <summary>
	/// O CORPO FOI EMBORA: esqueca que ele estava paralisado.
	///
	/// Este mapa e o unico deste arquivo indexado por QUEM LEVOU, e nao por quem atirou -- e o id e
	/// de sessao. Sem esta limpeza, o proximo a entrar herdaria as pernas trancadas de um
	/// desconhecido, e o defeito apareceria a tres arquivos da causa.
	/// </summary>
	private void EsquecerParalisia(int id) => _paralisadoAte.Remove(id);

	/// <summary>
	/// A CARGA E O CANAL DE UM RAIO. Duas fases num estado so, porque no DM sao a mesma variavel
	/// caminhando: `charging=1` -> (accum enche) -> `beaming=1`, e o mesmo verb apertado de novo
	/// desliga a que estiver de pe (`beams.dm:271-274`).
	/// </summary>
	private sealed class CanalDeKi
	{
		public required ReceitaDeProjetil Receita;
		public required string Verbo;

		/// <summary>Segundos que ainda faltam de carga. Zero ou menos = ja esta atirando.</summary>
		public double CargaRestante;

		/// <summary>O tiro que esta saindo da mao. Nulo enquanto carrega.</summary>
		public Projetil? Raio;

		/// <summary>Quanto Ki cada ciclo de 0,2 s cobra enquanto o raio esta de pe.</summary>
		public double CustoPorCiclo;

		/// <summary>Quanto falta pro proximo ciclo cobrar.</summary>
		public double AteOProximoCiclo;

		/// <summary>
		/// QUAL DOS NOVE DESENHOS DE CARGA este corpo acende -- o `ChargeState` do DM. De 1 a 9.
		///
		/// ============================ POR QUE ELE E GUARDADO AQUI, E ISSO NAO CONTRADIZ NADA ============================
		/// A regra desta funcionalidade e "derive, nao guarde um bit que alguem tenha que apagar", e
		/// ela continua valendo: **o que nao se guarda e o ESTADO** (ha canal? esta atirando?), que sai
		/// do proprio dicionario. Isto aqui nao e estado -- e uma constante do personagem, funcao pura
		/// de nome + instante de criacao (`ArteDeProjetil.CargaDeRaio`), que nunca muda.
		///
		/// E ela mora NESTE registro, que morre junto com o canal, entao nao ha o que apagar: nasce no
		/// `Canalizar`, some no `FecharCanal`.
		///
		/// O MOTIVO DE NAO SER RECALCULADA NO `EstadoDe` E DE CUSTO, e ele e medido em alocacao: o
		/// sorteio constroi um `Random`, e o `EstadoDe` roda por corpo por tique (30 Hz). Um `Random`
		/// por corpo canalizando por tique e lixo gratuito num laco que este arquivo faz questao de
		/// manter sem alocacao (ver o cabecalho do `TickDosProjeteis`). Aqui a conta e feita UMA vez
		/// por raio.
		/// ==========================================================================================================
		/// </summary>
		public required int Carga;

		public bool Atirando => CargaRestante <= 0;
	}

	/// <summary>Este corpo esta preso carregando ou segurando um raio? Ver <see cref="PodeMexerOCorpo"/>.</summary>
	public bool EnraizadoPorKi(int id) => _canais.ContainsKey(id);

	/// <summary>
	/// O CANAL DE KI DESTE CORPO, PRO DESENHO -- existe? esta atirando?
	///
	/// ============================ E DERIVADO, E ISSO E A REGRA E NAO UM DETALHE ============================
	/// Nao ha bit de "estou com um raio na mao" guardado em lugar nenhum: a resposta sai do
	/// <see cref="_canais"/>, que e o MESMO dicionario que o <see cref="EnraizadoPorKi"/> ja le e a
	/// unica coisa que existe no jogo dizendo "este corpo tem um canal". A entrada nasce no
	/// <see cref="Canalizar"/> e morre no <see cref="FecharCanal"/> -- e o `FecharCanal` e o UNICO
	/// caminho de saida, por onde passam os quatro jeitos de o raio acabar (soltar, o Ki acabar,
	/// cair/meditar/treinar, e o raio morrer na parede).
	///
	/// Por isso a pose nao tem como ficar presa: ela nao e apagada por ninguem, ela **deixa de ser
	/// respondida** no tique em que a entrada sai do mapa. Este projeto perdeu tres vezes esta
	/// semana com a forma oposta -- a aureola presa no cadaver, o `MsNoAlem` sem consumidor, o
	/// relogio da morte que nao rearmava --, e as tres eram um bit que alguem tinha que lembrar de
	/// apagar.
	/// =====================================================================================================
	///
	/// VALE PRA IA DE GRACA, e pelo mesmo motivo: o mapa e indexado por id de corpo, e o corpo de um
	/// NPC e um corpo. Nao ha `if` de NPC aqui e nao ha um segundo caminho de tiro pra IA -- ela
	/// entra pelo mesmo <see cref="Canalizar"/>.
	/// </summary>
	/// <returns>
	/// `canal` = ha um canal de pe (carregando OU atirando); `atirando` = o raio ja saiu da mao (o
	/// `beaming` do DM, contra o `charging`); `carga` = qual dos nove desenhos de `BlastCharges` este
	/// corpo acende, ja resolvido (ver <see cref="CanalDeKi.Carga"/>).
	/// </returns>
	public (bool canal, bool atirando, int carga) CanalDeKiDe(int id) =>
		_canais.TryGetValue(id, out CanalDeKi? c) ? (true, c.Atirando, c.Carga) : (false, false, 0);

	/// <summary>
	/// APANHOU: O RAIO CAI. E o `if(KB) stopbeaming()` do laco do `ShootBeam` (`beams.dm:73-74`).
	///
	/// ============================ ESTA REGRA FALTAVA INTEIRA ============================
	/// O <see cref="TickDosCanaisDeKi"/> derrubava o canal por morte, nocaute, meditacao e treino --
	/// as quatro condicoes do `AreYaBeamingKid` (`beams.dm:59-63`) -- e mais nada. O `KB`, que e a
	/// QUINTA e mora num laco separado do DM, nao tinha porte: **no port ninguem conseguia cancelar
	/// o raio de outra pessoa batendo nela**. Metade do pedido do dono (*"ou pq ALGUEM BATEU NELE e
	/// cancelou o beam"*) nao tinha como acontecer.
	///
	/// O `KB` do DM e escrito em dois lugares (`Movement Improvement/Throw.dm:84` e o
	/// `/effect/knockback` de `Stats/Effects/Movement Effects.dm`), e os dois viram UMA coisa neste
	/// port: o <see cref="Arremessar"/>. Por isso o chamador e de la, e nao um `if` novo no tique --
	/// pendurar a regra no tique faria ela perguntar "ele esta sendo arremessado?" 30 vezes por
	/// segundo pra responder nao, e faria a pose sobreviver ao instante do golpe.
	/// ==================================================================================
	///
	/// ============================ E ELE NAO E O <see cref="SoltarDoRaio"/> ============================
	/// Aquele e a faxina de quem SAIU -- logout e troca de planeta --, e por isso ele fecha o canal
	/// CALADO (`FecharCanal(..., null)`) e leva junto as quatro recargas de tiro: nao ha tela pra ler
	/// recado nenhum, e as recargas nao devem atravessar uma sessao.
	///
	/// Aqui e o oposto em tudo: o corpo continua em jogo, precisa OUVIR por que o raio caiu, e as
	/// recargas continuam valendo -- perder o feixe por um golpe nao devolve o cooldown das outras
	/// tecnicas de graca. Sao duas frases diferentes, e juntar as duas num metodo com um `bool` teria
	/// dado a terceira, que nao e nenhuma delas.
	/// ================================================================================================
	/// </summary>
	private void DerrubarRaioPorGolpe(int id)
	{
		if (_canais.TryGetValue(id, out CanalDeKi? c))
			FecharCanal(id, c, "o golpe quebra a sua concentracao e o raio se desfaz.");
	}

	/// <summary>
	/// A SEMENTE DA ARTE DESTE CORPO -- a mesma dupla que a cor da aura ja usa: nome + instante de
	/// criacao (`CharacterStore.ParaJogador:771`, `CorDeAura.De`).
	///
	/// So o Kamehameha a consome (ver <see cref="ArteDeProjetil.SorteioDoKamehameha"/>). Vale pra
	/// **qualquer** corpo, inclusive os que nao tem save -- NPC, clone da meditacao, corpo de
	/// bancada: eles tem nome e tem `CriadoEm`, entao caem na mesma conta pura e ficam estaveis
	/// entre logins. Um corpo sem nome nenhum cai na semente zero, que e um sorteio valido e nao um
	/// caso especial.
	/// </summary>
	private static ulong SementeDeArte(ServerPlayer pl) =>
		Jandirus.Core.Forms.LimiaresPessoais.SementeDe(pl.Name, pl.CriadoEm);

	// =====================================================================
	// AS TRES TECNICAS QUE ATIRAM -- uma por tipo, todas do DM
	// =====================================================================
	/// <summary>
	/// Registra os tres verbs no catalogo. Chamado do mesmo lugar que os lotes G1-G4.
	///
	/// TRES E O MINIMO HONESTO: um por tipo. Portar so uma deixaria dois dos tres caminhos de voo
	/// sem nenhum chamador de producao -- e caminho sem chamador e onde este projeto ja escondeu
	/// codigo morto (uma API inteira de sigilo de BP escrita e nunca ligada).
	/// </summary>
	private void RegistrarTecnicasDeProjetil()
	{
		IniciarLote("projetil");
		Vivo("Ki_Wave", KiWave);
		Vivo("Basic_Blast", BasicBlast);
		Vivo("Guided_Ball", GuidedBall);
	}

	/// <summary>
	/// KI WAVE -- o raio basico (`beams.dm:270-301`). Ki `10*BaseDrain`, carga 1, `beamspeed` 1,
	/// `powmod` 1, alcance 30 tiles.
	///
	/// APERTAR DE NOVO DESLIGA, e essa e a primeira linha do verb original: um raio canalizado nao
	/// e um disparo, e um estado.
	/// </summary>
	private void KiWave(ServerPlayer pl)
	{
		if (SoltarCanal(pl, "Ki_Wave")) return;

		double custo = 10 * pl.Ficha.BaseDrain();
		Canalizar(pl, "Ki_Wave", custo, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam,
			BaseDano = 1,               // `powmod = 1`
			Velocidade = 1,             // `beamspeed = 1`
			AlcanceTiles = 30,          // `maxdistance = 30`
			CargaMinima = 1,            // `chargedelay = 1`
			Nome = "Onda de Ki",
		});
	}

	/// <summary>
	/// BASIC BLAST -- a bola (`blasts.dm:40-80`). Ki `10*BaseDrain`, recarga `max(Eactspeed/5, 3)`
	/// tiques, `basedamage = Ekioff * log_10(max(blastskill,10))`.
	///
	/// Ela nao prende ninguem: sai da mao e o corpo continua livre. E o unico dos tres que da pra
	/// usar correndo, e e por isso que ele e o tiro de todo dia.
	/// </summary>
	private void BasicBlast(ServerPlayer pl)
	{
		if (EmEspera(pl, _blastPronto, "sua mao ainda esta juntando energia")) return;
		long agora = NowMs();

		double custo = 10 * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		// `reload = Eactspeed/5`, com piso de 3 tiques -- e o `Eactspeed` cai quando se carrega Ki,
		// entao carregar poder tambem faz atirar mais rapido. Mesma promessa da cadencia do soco.
		double tiques = Math.Max(pl.Ficha.Eactspeed / 5, 3);
		_blastPronto[pl.Id] = agora + (long)(tiques * 100);

		pl.Ficha.Ki -= custo;
		pl.Ficha.BlastGain(_rng);
		pl.Ficha.blastskill += 0.05;
		CreditarContador(pl, "blastcounter", 1);   // `usr.blastcounter++` (`blasts.dm:54`)

		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = pl.Ficha.Ekioff * Math.Log(Math.Max(pl.Ficha.blastskill, 10)) / Math.Log(10),
			Velocidade = 1,
			AlcanceTiles = 30,
			Nome = "Bola de Ki",
		}, verbo: "Basic_Blast");
	}

	/// <summary>
	/// GUIDED BALL -- a esfera que persegue (`GuidedBall.dm:24-80`). `basedamage = 15` fixo, e ela
	/// vive `Burnout(1200)` = 120 s em vez dos 5 s de todo mundo: ela nao desiste.
	///
	/// ============================ UM DEFEITO DO ORIGINAL, CONSERTADO E ANOTADO ============================
	/// O verb CONFERE `Ki >= 50*BaseDrain` e logo abaixo COBRA `600*BaseDrain`. Quem tivesse entre
	/// 50 e 600 atirava e saia com Ki NEGATIVO -- e no DM o Ki negativo nao e recusado em lugar
	/// nenhum, so demora a voltar. Aqui a conferencia e do valor que se cobra.
	///
	/// O que se perdeu: um jogador espertinho nao consegue mais "financiar" um tiro. O que se
	/// ganhou: o preco na tela e o preco de verdade.
	/// =====================================================================================================
	/// </summary>
	private void GuidedBall(ServerPlayer pl)
	{
		double custo = 600 * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		ServerPlayer? alvo = Marcado(pl);
		if (alvo == null || alvo == pl || alvo.Ficha.dead || !alvo.Zone.Equals(pl.Zone))
		{
			alvo = null;
			// O AVISO E DO DM, literal: *"Sem alvo selecionado: o ataque voa reto."* Ele existe
			// porque o tiro sai igual nos dois casos, e sem a frase o jogador acha que a tecnica
			// nao funciona quando na verdade e ele que nao marcou ninguem.
			Avisar(pl, "sem alvo marcado: a esfera vai voar reto. (Marque alguem com duplo clique.)");
		}

		pl.Ficha.Ki -= custo;
		pl.Ficha.BlastGain(_rng);
		pl.Ficha.BlastGain(_rng);   // `usr.Blast_Gain()` duas vezes -- a esfera custa e rende dobrado

		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Guided,
			BaseDano = 15,
			Velocidade = 1,
			AlcanceTiles = 30,
			Nome = "Esfera Teleguiada",
		}, verbo: "Guided_Ball");
		if (p.Vivo)
		{
			p.Alvo = alvo?.Id ?? 0;
			p.VidaRestante = 120;   // `Burnout(1200)`
		}
	}

	/// <summary>
	/// AS CONDICOES COMUNS DE DISPARO -- o `CustomShotOK` do DM (`customattacks.dm:481`), menos a
	/// cobranca (quem cobra e cada tecnica, porque cada uma cobra uma coisa).
	///
	/// A ORDEM IMPORTA: a falta de Ki e a ULTIMA a ser dita. Quem esta nocauteado tambem esta sem
	/// Ki quase sempre, e "voce esta caido" e a resposta util.
	/// </summary>
	private bool PodeAtirar(ServerPlayer pl, double custo, out string porque)
	{
		porque = "";
		if (pl.Combate == null) { porque = "seu corpo ainda nao esta pronto."; return false; }
		if (pl.Ficha.dead) { porque = "voce esta morto."; return false; }
		if (pl.Ficha.KO) { porque = "voce esta caido."; return false; }
		if (pl.Ficha.med) { porque = "nao da pra atirar meditando."; return false; }
		if (pl.Ficha.train) { porque = "pare o treino antes de atirar."; return false; }
		if (_canais.ContainsKey(pl.Id)) { porque = "voce ja esta com um raio na mao."; return false; }
		// O `blasting` das tecnicas sustentadas do lote G12 (Death Ball, Buster Barrage, as rajadas, a Genkidama).
		if (BlastingG12(pl.Id)) { porque = "voce ja esta com uma tecnica de ki no ar."; return false; }
		if (pl.Ficha.Ki < custo) { porque = $"isso pede pelo menos {custo:0} de energia."; return false; }

		// O TETO, E ELE FALA. Um teto que recusa em silencio e indistinguivel de um tiro que sumiu.
		if (_projeteisVivos >= MaxProjeteisNoMundo)
		{
			porque = "ha energia demais solta no mundo agora.";
			return false;
		}
		if (ProjeteisDaZona(pl.Zone.Hash).Count >= MaxProjeteisPorZona)
		{
			porque = "o ar aqui ja esta saturado de energia -- espere alguma coisa estourar.";
			return false;
		}
		return true;
	}

	// =====================================================================
	// O DISPARO
	// =====================================================================
	/// <summary>
	/// PARIR UM PROJETIL -- a porta unica, e e por ela que a tecnica customizada do jogador vai
	/// entrar na camada seguinte.
	///
	/// Ela NAO cobra Ki nem confere pre-condicao: quem chama ja fez as duas coisas, e cada tecnica
	/// cobra o seu. Misturar as duas responsabilidades foi o que fez o `CustomShotOK` do DM cobrar
	/// escondido dentro de um "posso?".
	/// </summary>
	/// <param name="rumoDado">
	/// PRA ONDE ELE VAI, quando nao e "pra onde eu olho". As barragens do DM saem em direcao
	/// SORTEADA (`step(A,randdir)` no Scattershot, `step_rand` no Spin Blast) e o campo minado nasce
	/// virado pro dono -- sem isto cada tecnica dessas teria que reescrever o nascimento do tiro, que
	/// e exatamente como se ganha dois projeteis diferentes no mesmo jogo.
	/// </param>
	/// <param name="deOnde">
	/// DE ONDE ELE NASCE, quando nao e "da minha mao". O Ki Minefield e a Hellzone Grenade nascem em
	/// volta do ALVO (`A.loc = locate(pick(target.x+2,...), ...)`, `blasts.dm:434`) -- e a razao de
	/// existirem: a bola nao viaja ate o alvo, ela ja esta la.
	/// </param>
	/// <param name="verbo">
	/// QUAL TECNICA ESTA ATIRANDO -- e a chave da <see cref="ArteDeProjetil"/>, o unico lugar que
	/// decide qual folha cada tecnica desenha.
	///
	/// E PARAMETRO E NAO CAMPO DA RECEITA de proposito: a receita descreve O QUE VOA (dano, alcance,
	/// velocidade) e o verb e QUEM ATIROU. O `Canalizar` ja recebia o id pelo mesmo motivo, e passar
	/// o dele adiante (`Disparar(pl, c.Receita, verbo: c.Verbo)`) faz os DEZ raios do jogo herdarem
	/// a arte sem uma linha em cada verb.
	///
	/// VAZIO E LEGITIMO: e o que a bancada e os tiros sem tecnica passam, e a resposta e
	/// <see cref="ArteDeKi.Nenhuma"/> -- desenho por primitiva, como antes desta funcionalidade.
	/// </param>
	private Projetil Disparar(ServerPlayer pl, ReceitaDeProjetil r,
							  Vec2? rumoDado = null, Vec2? deOnde = null, string verbo = "")
	{
		Vec2 rumo = rumoDado ?? MeleeArea.Frente(pl.Facing);

		// ============================ O TIRO NASCE A FRENTE DO CORPO, NUNCA EM CIMA DELE ============================
		// `A.loc = src.loc` e, no mesmo tique, `step(A, A.dir)` (`beams.dm:177-185`). Ver
		// `BocaDeCano`: o passo e um TILE projetado no rumo (32 px cardeal, 45,25 diagonal), entao
		// ele MUDA com a direcao -- um numero fixo so acertaria as quatro cardeais.
		//
		// O ALCANCE NAO PAGA POR ESTE PASSO, e o DM concorda: `A.distance = maxdistance` e escrito
		// ANTES do `step` (`beams.dm:175-176`) e o `step` nao desconta nada. Aqui o desconto e do
		// passo 7 do `AndarProjetil`, que so conta o que o tiro andou DEPOIS de nascer.
		//
		// `deOnde` NAO ganha o passo, e e o ponto do parametro: a Hellzone e o Ki Minefield nascem
		// em volta do ALVO (`blasts.dm:434`) e nao na mao de ninguem.
		// =======================================================================================================
		Vec2 berco = deOnde ?? BocaDeCano.De(pl.Pos, rumo);

		// E O `step()` DO BYOND FALHA CONTRA PAREDE. La o `step` e um Move: se o tile da frente for
		// denso, o beam simplesmente FICA no tile do mob e bate no muro no ciclo seguinte. Sem esta
		// linha, um tiro dado colado numa parede nasceria DENTRO dela -- e na diagonal (45,25 px) o
		// nascimento poderia pular a espessura de uma celula inteira e sair do outro lado.
		//
		// Quem voa alto atravessa, a mesma regra do voo que o passo 6a ja aplica (`AtravessaCenario`).
		if (deOnde == null && !Voo.AtravessaCenario(pl.Altitude)
			&& MapaDaZonaOuCatalogo(pl.Zone) is { } chao && chao.BlockedAt(berco))
			berco = pl.Pos;

		// ============================ A ARTE: A RECEITA VENCE, A TABELA RESPONDE ============================
		// E o `if (S.attackicon != null) forceicon = S.attackicon else forceicon = usr.beamicon` do
		// DM (`customattacks.dm:437-440`), com a tabela no lugar do `beamicon`: uma tecnica que
		// declarou a propria folha (so a customizada, onde a arte e ESCOLHA do jogador) manda; todas
		// as outras perguntam pra o `ArteDeProjetil`, que e o unico que decide.
		ArteDeKi arte = r.Arte != ArteDeKi.Nenhuma
			? r.Arte
			: ArteDeProjetil.De(verbo, pl.Race, pl.Class, SementeDeArte(pl));

		var p = new Projetil
		{
			Id = _proximoProjetil++,
			Dono = pl.Id,
			Tipo = r.Tipo,
			Pos = berco,
			Cauda = berco,
			Rumo = rumo,
			Distancia = r.AlcanceTiles,
			MaxDistancia = r.AlcanceTiles,
			RangeMod = r.RangeMod,
			ModsBase = Projetil.ModsDoTiro(pl.Ficha, r.Tipo) * r.BaseDano,
			// `A.BP = expressedBP * wavemult` (`beams.dm:139`). Fora do Final Flash e do Massive Beam
			// o multiplicador e 1 e esta linha e a de antes.
			Bp = pl.Ficha.expressedBP * r.MultDeOnda,
			BaseDano = r.BaseDano,
			MaxDano = r.MaxDano,
			Letal = pl.Combate?.Letal ?? false,
			Deflectivel = r.Deflectivel,
			Piercer = r.Piercer,
			Fisico = r.Fisico,
			Paralisia = r.Paralisia,
			Empurra = r.Empurra,
			Altitude = pl.Altitude,
			Nome = r.Nome,
			Arte = arte,
			// `A.transform *= wavemult` (`beams.dm:149`) -- a OUTRA metade do `wavemult`, a que
			// engorda o sprite. Ver `Projetil.EscalaVisual` sobre por que ela nao mora no `Bp`.
			//
			// ============================ SO O RAIO ENGORDA, E ISSO E LITERAL ============================
			// A linha do `transform` esta no `ShootBeam` (`beams.dm:139-149`) e o caminho das BOLAS
			// (`blasts.dm`) nao tem nada parecido: la o `A.icon`/`A.icon_state` sao escritos e a
			// bola sai do tamanho que o artista desenhou.
			//
			// Sem esta guarda, duas tecnicas ja em producao sairiam do tamanho errado por um
			// multiplicador que no DM so mexe no PODER delas: o Tiro Carregado (`MultDeOnda = 1.2`,
			// que la e o `passbp = expressedBP*1.2`) e as duas paralisias, cujo `MultDeOnda` e
			// `log(kidebuffskill)/log(10 ou 11)` -- ou seja o tamanho da bola cresceria com a
			// PERICIA de quem atira, coisa que o original nao faz em lugar nenhum.
			// =======================================================================================
			// A TECNICA PODE PEDIR A ESCALA (`ReceitaDeProjetil.EscalaVisual`, lote G12: as bolas que crescem).
			EscalaVisual = r.EscalaVisual > 0 ? r.EscalaVisual : (r.Tipo == TipoDeProjetil.Beam ? r.MultDeOnda : 1),
			SegundosPorTile = r.Tipo == TipoDeProjetil.Beam
				? Projetil.AtrasoDeRaio(r.Velocidade)
				: Projetil.AtrasoDeBola(r.Velocidade),
		};

		// O TETO DE NOVO, AGORA COMO ULTIMA PORTA. `PodeAtirar` ja recusou, mas quem chama `Disparar`
		// direto (a bancada, e amanha a tecnica customizada) tem que esbarrar no mesmo limite -- um
		// teto que so vale num dos dois caminhos e um teto que nao vale.
		List<Projetil> lista = ProjeteisDaZona(pl.Zone.Hash);
		if (lista.Count >= MaxProjeteisPorZona || _projeteisVivos >= MaxProjeteisNoMundo)
		{
			p.Vivo = false;
			p.Fim = FimDeProjetil.Apagou;
			return p;
		}

		lista.Add(p);
		_projeteisVivos++;
		AnunciarProjetil(pl.Zone.Hash, Protocol.ProjetilSub.Nasceu, p);
		return p;
	}

	/// <summary>
	/// COMECAR A CARREGAR UM RAIO. Enquanto a carga corre o corpo fica PLANTADO -- e essa e a unica
	/// coisa que o raio tira de quem atira.
	///
	/// ============================ E UM GATE DE VETOR, NAO DE INPUT ============================
	/// No DM o verb faz `canmove = 0` e o `stopbeaming()` faz `canmove = 1` (`beams.dm:294` e
	/// `:229`). CANMOVE, e nao "canfight": a linha `//canfight = 0` esta la, comentada, nos quatro
	/// verbs de raio -- alguem ja tentou trancar tudo e desistiu.
	///
	/// Aqui isso entra pelo `PodeMexerOCorpo`, que e o MESMO funil de quem esta carregando Ki, caido
	/// ou num embate: o pacote de input continua chegando, o olhar continua girando, a guarda, a
	/// fala, e o proprio verb (pra soltar o raio) continuam funcionando. So o RUMO e recusado, e a
	/// posicao volta como correcao -- exatamente como ja acontece com a tecla C.
	///
	/// Trancar o input inteiro travaria o jogador dentro da propria tecnica: sem input nao ha como
	/// apertar o verb de novo, e a unica saida seria ficar sem Ki. Este projeto ja travou jogador
	/// assim uma vez (ver a nota do `BitSemRedeas` no `Protocol`).
	/// =========================================================================================
	/// </summary>
	/// <param name="custoPorTiro">
	/// O `lastbeamcost` DO VERB, quando ele nao e o mesmo que o `kireq` da entrada.
	///
	/// No Ki Wave e no Masenko os dois numeros coincidem, e foi por isso que o primeiro corte deste
	/// metodo usou um so. Nos outros nao: o Massive Beam pede 15 de entrada e ALIMENTA a 150
	/// (`beams.dm:428`), e o Final Flash multiplica o proprio `kireq` por 1, 10, 20 ou 30 conforme a
	/// pericia de raio (`beams/FinalFlash.dm:41-63`). Com um numero so, o raio mais caro do jogo
	/// custaria um decimo do que o original cobra por segundo -- e o preco e o unico contrapeso que
	/// ele tem.
	/// </param>
	private void Canalizar(ServerPlayer pl, string verbo, double custo, ReceitaDeProjetil r,
						   double? custoPorTiro = null)
	{
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		pl.Ficha.Ki -= custo;

		double carga = r.Instantaneo ? 0 : Projetil.SegundosDeCarga(r.CargaMinima, pl.Ficha);

		_canais[pl.Id] = new CanalDeKi
		{
			Receita = r,
			Verbo = verbo,
			CargaRestante = carga,
			// `finalcost = lastbeamcost` e o ciclo cobra `(finalcost/4)*BaseDrain` (`beams.dm:83`),
			// com `lastbeamcost = custo_do_verb / 10` depois da fase de carga (`beams.dm:38`).
			CustoPorCiclo = (custoPorTiro ?? custo) / 10 / 4 * pl.Ficha.BaseDrain(),
			AteOProximoCiclo = Projetil.SegundosPorCicloDeBeam,
			// O DESENHO DA CARGA, resolvido UMA vez -- ver `CanalDeKi.Carga`. A mesma semente que a
			// arte do Kamehameha e a cor da aura ja consomem: sem campo no save, e por isso vale pro
			// NPC e pro clone tambem.
			Carga = Jandirus.Core.Combat.ArteDeProjetil.CargaDeRaio(pl.Race, pl.Class, SementeDeArte(pl)),
		};

		// A POSICAO VOLTA NA HORA. Sem isto o cliente continuaria andando por ate um tique com a
		// previsao local antes da primeira correcao chegar -- e o corpo pareceria escorregar pra
		// dentro do proprio raio.
		pl.Moving = false;
		Avisar(pl, carga > 0
			? $"voce planta os pes e comeca a reunir energia ({carga:0.#}s)."
			: "a energia salta da sua mao.");
	}

	/// <summary>
	/// APERTOU O MESMO VERB COM O RAIO DE PE: solta. Devolve verdadeiro quando havia o que soltar --
	/// e o `if(beaming) { canmove = 1; stopbeaming(); return }` que abre todos os verbs de raio.
	/// </summary>
	private bool SoltarCanal(ServerPlayer pl, string verbo)
	{
		if (!_canais.TryGetValue(pl.Id, out CanalDeKi? c) || c.Verbo != verbo) return false;
		FecharCanal(pl.Id, c, "voce fecha a mao e o raio se apaga.");
		return true;
	}

	/// <summary>
	/// DESLIGA O CANAL. O raio que ja saiu NAO morre junto: ele para de ser alimentado e o rastro
	/// se esvazia andando (ver <see cref="TickDosProjeteis"/>) -- que e o que o DM faz quando o dono
	/// para de gerar segmentos.
	/// </summary>
	private void FecharCanal(int id, CanalDeKi c, string? aviso)
	{
		_canais.Remove(id);
		if (c.Raio != null) c.Raio.Canalizando = false;
		if (aviso != null && _players.TryGetValue(id, out ServerPlayer? pl)) Avisar(pl, aviso);
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O TIQUE DOS CANAIS: carrega, dispara, cobra o aluguel e derruba quem nao aguenta mais.
	///
	/// Roda no tique CHEIO (30 Hz) e nao no de 1 Hz: a carga mais curta que a loja de pontos permite
	/// e 0,2 s, e um relogio de 1 Hz erraria por cinco vezes a duracao dela.
	/// </summary>
	private void TickDosCanaisDeKi(double dt)
	{
		if (_canais.Count == 0) return;

		foreach (int id in _canais.Keys.ToList())
		{
			CanalDeKi c = _canais[id];
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _canais.Remove(id); continue; }

			// AS MESMAS CONDICOES DO `AreYaBeamingKid`: `if(KO||med||train) stopcharging/stopbeaming`.
			if (pl.Ficha.dead || pl.Ficha.KO || pl.Ficha.med || pl.Ficha.train)
			{
				FecharCanal(id, c, "voce perde a concentracao e o raio se desfaz.");
				continue;
			}

			if (!c.Atirando)
			{
				c.CargaRestante -= dt;
				if (c.CargaRestante > 0) continue;

				// A CARGA FECHOU: nasce a cabeca do raio, ja canalizando.
				//
				// O `c.Verbo` VAI JUNTO e e ele que da arte aos DEZ raios do jogo de uma vez: o
				// canal ja guardava o id do verb (era so pra o "apertar de novo desliga"), e e
				// exatamente a chave que a `ArteDeProjetil` pede. Nenhum verb de raio precisou
				// mudar por causa disto.
				Projetil raio = Disparar(pl, c.Receita, verbo: c.Verbo);
				if (!raio.Vivo)
				{
					FecharCanal(id, c, "nao ha espaco pra mais energia solta aqui.");
					continue;
				}
				raio.Canalizando = true;
				c.Raio = raio;
				Falar(pl, Protocol.Fala.Diz, c.Receita.Nome + "!!");
				continue;
			}

			// JA ESTA ATIRANDO: cobra por ciclo de 0,2 s, como o `ShootBeam`.
			c.AteOProximoCiclo -= dt;
			if (c.AteOProximoCiclo > 0) continue;
			c.AteOProximoCiclo += Projetil.SegundosPorCicloDeBeam;

			if (pl.Ficha.Ki < c.CustoPorCiclo)
			{
				FecharCanal(id, c, "sua energia acaba e o raio morre na mao.");
				continue;
			}
			pl.Ficha.Ki -= c.CustoPorCiclo;

			// TREINO: `beamcounter += 3` e `Blast_Gain()` a cada ciclo. Sustentar um raio treina --
			// devagar, e enquanto se paga por ele.
			pl.Ficha.beamskill += 0.03;
			CreditarContador(pl, "beamcounter", 3);   // `proprietor.beamcounter += 3` (`objects.dm:317`)
			pl.Ficha.BlastGain(_rng);

			// O raio pode ter morrido (parede, alvo, alcance) sem o dono soltar: o canal cai junto,
			// senao ele ficaria cobrando Ki por um raio que nao existe mais.
			if (c.Raio is not { Vivo: true }) FecharCanal(id, c, "o raio se desfaz.");
		}
	}

	/// <summary>
	/// O TIQUE DOS PROJETEIS -- o coracao desta camada.
	///
	/// ============================ NADA PESADO AQUI DENTRO ============================
	/// Custo por tique: O(tiros vivos x corpos da zona). Nao ha varredura de mapa, nao ha alocacao
	/// por tiro (a lista da zona e reusada), e a poda de cima faz um mundo em paz custar UMA
	/// comparacao. O avanco e fatiado em sub-passos de meio tile pra nao pular parede nem corpo --
	/// a mesma disciplina do `MoveRules.Advance` e do arremesso.
	/// ==============================================================================
	/// </summary>
	private void TickDosProjeteis(double dt)
	{
		if (_projeteisVivos == 0) return;

		// A MEMORIA DO CEU VALE UM TIQUE E NAO MAIS. Ver `_mundosPerto`: ela existe pra o mesmo
		// `PorPerto` nao ser refeito quatro vezes por tiro, e nao pra guardar o universo -- limpar
		// aqui e o que a mantem memoria de tique em vez de cache com dono nenhum.
		_mundosPerto.Clear();

		foreach ((ulong zona, List<Projetil> lista) in _projeteis)
		{
			if (lista.Count == 0) continue;

			List<ServerPlayer> corpos = ZoneList(zona);
			ZoneCollision? mapa = null;
			ZoneKey chave = default;
			bool temChao = false;
			bool noEspaco = false;
			bool mapaLido = false;

			for (int i = lista.Count - 1; i >= 0; i--)
			{
				Projetil p = lista[i];

				if (p.Vivo)
				{
					if (!mapaLido)
					{
						// UMA LEITURA DE MAPA POR ZONA POR TIQUE, e nao uma por tiro. Com a zona
						// lotada isso e a diferenca entre 1 e 256 buscas no catalogo.
						//
						// A CHAVE DA ZONA SAI JUNTO, e pela mesma razao: o rastro no chao precisa
						// dela (`MandarDecalque` e `Espaco.EhPlaneta` falam em `ZoneKey`, e a lista
						// de tiros so guarda o hash), e busca-la por tiro seria pagar a mesma conta
						// uma vez por projetil.
						mapa = MapaDaZonaDoHash(zona, out chave);

						// "EXISTE CHAO AQUI?" TAMBEM E UMA PERGUNTA POR ZONA, e ela nao e de graca:
						// `Espaco.EhPlaneta` percorre `PreFeitos()`, que e um ITERADOR -- cada
						// chamada monta sete objetos de planeta e um enumerador. Perguntada por
						// sub-passo de cada raio, ela sozinha viraria milhares de alocacoes por
						// tique. A resposta nao muda dentro do tique: uma vez por zona.
						temChao = Espaco.EhPlaneta(chave);

						// "E O ESPACO?" TAMBEM POR ZONA, e pelo mesmo motivo elevado ao quadrado: e
						// ela que liga o unico alvo do jogo que nao e corpo nem parede -- o PLANETA
						// visto de fora (ver `MundoNoCaminho`). Ela nao e o inverso de `temChao`: um
						// interior de nave nao tem chao de planeta e tambem nao e o espaco.
						noEspaco = Espaco.EhEspaco(chave);
						mapaLido = true;
					}

					// ============================ DOIS FEIXES SE ENCONTRARAM? ============================
					// O gatilho por PROXIMIDADE do DM (`objects.dm:246-254`), e o comentario de la diz
					// por que ele existe alem do frontal: dois feixes retos em fileiras vizinhas se
					// atravessavam sem nunca colidir, e so os teleguiados chegavam a disputar.
					//
					// A PODA E O QUE FAZ ISTO CABER NO TIQUE. A varredura interna e O(tiros da zona),
					// mas o portao externo e "cabeca de raio, e ainda canalizada" -- no maximo um por
					// pessoa com raio na mao. Uma zona com 256 bolas e zero raios paga UMA comparacao
					// de tipo por bola. Medido na familia 7 da bancada.
					// ================================================================================
					if (p.Tipo == TipoDeProjetil.Beam && p.Canalizando && !p.EmEmbate && !p.JaDisputou)
						TentarEmbateDeFeixes(p, lista);

					AndarProjetil(p, dt, corpos, mapa, chave, temChao, noEspaco);
				}

				if (p.Vivo) continue;

				lista.RemoveAt(i);
				_projeteisVivos--;
				AnunciarProjetil(zona, Protocol.ProjetilSub.Morreu, p);
			}
		}
	}

	/// <summary>Um passo de um tiro: prazo, rastro, perseguicao, avanco e o que ele encontrou.</summary>
	/// <param name="zona">
	/// A chave da zona -- so o rastro no chao precisa dela. Vem de fora (uma busca por zona, por
	/// tique) e nao daqui, pra nao pagar a mesma conta uma vez por projetil.
	/// </param>
	/// <param name="temChao">
	/// A zona e um PLANETA (tem chao pra arar)? Tambem vem pronta de fora, e pelo mesmo motivo elevado
	/// ao quadrado: a resposta custa uma varredura com alocacao e nao muda dentro do tique.
	/// </param>
	/// <param name="noEspaco">
	/// A zona e o ESPACO? Liga o unico alvo do jogo que nao e corpo nem parede -- o disco de um
	/// planeta visto de fora. Ver o passo 6a-quater e <see cref="MundoNoCaminho"/>.
	/// </param>
	private void AndarProjetil(Projetil p, double dt, List<ServerPlayer> corpos, ZoneCollision? mapa,
							   ZoneKey zona, bool temChao, bool noEspaco)
	{
		ServerPlayer? dono = _players.GetValueOrDefault(p.Dono);

		// 0) AINDA SENDO FORMADA (`Projetil.Inerte`, lote G12): nao anda, nao colide, nao gasta alcance.
		//    So o prazo corre -- e o alvo de treino do Ki Targets, que vive 5 s, e quem precisa disso.
		if (p.Inerte)
		{
			p.VidaRestante -= dt;
			if (p.VidaRestante <= 0) Matar(p, FimDeProjetil.Apagou);
			return;
		}

		// 1) PRESO NUMA DISPUTA: a cabeca nao anda, nao colide, nao morre de alcance -- e **nao
		//    envelhece**. Quem manda nela e o `TickDosEmbatesDeKi`. E o `if(!in_beamclash) walk(...)`
		//    do `objects.dm:241`.
		//
		//    ============================ POR QUE O PRAZO NAO CORRE AQUI ============================
		//    O `Burnout` sao 5 segundos e uma disputa pode durar 22 (`BCL_MAX_SECONDS`). Com o prazo
		//    correndo, os DOIS feixes se apagavam no meio do encontro e o vencedor nao tinha o que
		//    empurrar -- foi o que a bancada mediu: "o feixe do vencedor CONTINUOU vivo" reprovava, e
		//    o perdedor nao levava dano nenhum.
		//
		//    A regra nao e invencao: e a mesma que o DM escreve pro canal do NPC, com o motivo dele
		//    junto -- `while(... (beamclash || world.time - started < BCL_NPC_BEAM_TIME))`, *"numa
		//    DISPUTA o canal nao expira pelo tempo, senao o NPC desistiria no meio do clash"*
		//    (`BeamClash.dm:429`). Um feixe em embate nao esta apagando: ele esta sendo ALIMENTADO
		//    pelo dono, e o `side_ok` confere isso a cada ciclo.
		//
		//    O RASTRO CONTINUA: a cauda e a mao do dono, entao o raio de quem ganha ESTICA e o de quem
		//    perde ENCOLHE sozinho, conforme o encontro caminha.
		//    ==================================================================================
		if (p.EmEmbate)
		{
			// E ELE LARGA QUEM ESTAVA CARREGANDO. A cabeca deixou de andar (quem manda nela agora e o
			// ponto de encontro), entao o arrasto nao teria delta nenhum pra aplicar -- ele expiraria
			// sozinho pelo prazo. Soltar aqui e o explicito: enquanto dois feixes se medem, ninguem
			// esta sendo levado por nenhum dos dois, e o corpo que estava na frente volta a andar no
			// mesmo tique em vez de ficar um decimo de segundo preso a um empurrao que ja nao existe.
			p.Arrastando = 0;
			if (p.Canalizando && dono != null) p.Cauda = BocaDeCano.De(dono.Pos, p.Rumo);
			return;
		}

		// 2) O PRAZO (`Burnout`). Vale pros tres tipos e independe de andar: um raio segurado
		//    contra uma parede tambem se apaga.
		p.VidaRestante -= dt;
		if (p.VidaRestante <= 0) { Matar(p, FimDeProjetil.Apagou); return; }

		// 3) O RASTRO DO RAIO, e ele tem TRES estados -- ver `Projetil.Esvaziando`.
		if (p.Tipo == TipoDeProjetil.Beam)
		{
			float passo = (float)(ZoneCollision.TileSize / p.SegundosPorTile * dt);

			// (a) CANALIZANDO: a cauda E a mao do dono. O trem esta sendo alimentado.
			//
			//     E A MAO E A BOCA DO CANO, o mesmo ponto de onde a cabeca nasceu -- senao o pedaco
			//     `origin` (o que fecha o feixe do lado de quem atira) ficaria carimbado por cima do
			//     personagem, que e a metade "de tras" da queixa do dono.
			//
			//     O RUMO E O DO TIRO e nao o `Facing` de agora: quem canaliza continua podendo GIRAR
			//     o olhar (`PodeMexerOCorpo` so recusa o passo), e uma mao que anda em volta do corpo
			//     enquanto a cabeca segue reta desenharia um feixe TORTO.
			if (p.Canalizando && dono != null) p.Cauda = BocaDeCano.De(dono.Pos, p.Rumo);

			// (b) A CABECA PAROU: o rastro e engolido pra dentro do ponto onde ela parou, e SO ENTAO
			//     o projetil sai da lista, com o motivo que ja tinha sido decidido.
			else if (p.Esvaziando)
			{
				Vec2 ate = p.Pos - p.Cauda;
				if (ate.Length <= passo) { Matar(p, p.FimPendente); return; }
				p.Cauda += ate.Normalized() * passo;
				return;   // a cabeca nao anda mais: ela e o ponto de encontro
			}

			// (c) SOLTO E VOANDO: cauda e cabeca andam na MESMA velocidade, entao o comprimento fica
			//     constante. E o trem do DM: nenhum segmento anda mais rapido que o da frente.
			//
			//     A GUARDA DO ENCONTRO existe pro caso em que a cabeca esta PARADA moendo alguem com
			//     o dono ja tendo soltado: ali a cauda alcanca, e o raio acabou.
			else
			{
				if ((p.Pos - p.Cauda).Length <= passo) { Matar(p, FimDeProjetil.Cessou); return; }
				p.Cauda += p.Rumo * passo;
			}
		}

		// 4) O RAIO ENCOSTADO MOI a cada ciclo de 0,2 s (o `sleep(2)` do `ShootBeam`) -- `Bump` nao
		//    apaga a cabeca de um `WaveAttack`.
		//
		//    ============================ E ENTRE UM CICLO E OUTRO ELE ANDA, SE ESTIVER CARREGANDO ============================
		//    Ate aqui "encostado" queria dizer PARADO: a cabeca ficava fincada no corpo e o relogio de
		//    0,2 s era a unica coisa que corria. Isso continua valendo pra quem NAO pode ser levado
		//    (perto demais, prensado numa parede, ja passou dos 10 tiles) -- o feixe para nele e moi.
		//
		//    Quando ha arrasto, nao: a cabeca continua avancando e o corpo vai junto, no mesmo
		//    sub-passo (ver `ArrastarComOFeixe`). A CADENCIA DO DANO NAO MUDA por causa disso, e essa e
		//    a parte delicada -- quem aplica o dano e o `Colidiu` -> `Acertar`, e uma cabeca que anda
		//    encostada colidiria a cada sub-passo, ou seja ~30 vezes por segundo em vez de 5. Quem
		//    impede e o par `Encostado` + `Arrastando` lido dentro do `Colidiu`: enquanto o ciclo nao
		//    vence, a vitima que esta sendo levada nao e testada. Vencido o ciclo, `Encostado` cai, o
		//    teste volta a acontecer e o `Acertar` cobra o tique de dano E rega o arrasto.
		//    ==================================================================================================================
		if (p.Encostado)
		{
			p.AteMoerDeNovo -= dt;
			if (p.AteMoerDeNovo <= 0)
			{
				p.AteMoerDeNovo = Projetil.SegundosPorCicloDeBeam;
				p.Encostado = false;   // volta a testar: se o alvo saiu, a cabeca segue viagem
			}
			else if (p.Arrastando == 0) return;   // sem ninguem pra levar, a cabeca fica onde esta
		}

		// 5) A PERSEGUICAO. `walk_towards` do DM: o teleguiado corrige o rumo TODO tique, sem limite
		//    de angulo -- e por isso ele nao e um beam com `homeTarget` (aquele so aceita +-45 graus).
		//
		//    A ESPERA VEM ANTES: enquanto ela corre a bola nao caca e (com o rumo nulo com que a
		//    Hellzone a pare) nao anda. E o `spawn(10)` do `blasts.dm:517` -- ver `Projetil.EsperaDeCaca`.
		if (p.EsperaDeCaca > 0) p.EsperaDeCaca -= dt;
		else if (p.Tipo == TipoDeProjetil.Guided && p.Alvo != 0
			&& _players.TryGetValue(p.Alvo, out ServerPlayer? mira) && !mira.Ficha.dead)
		{
			Vec2 d = mira.Pos - p.Pos;
			if (d.LengthSquared > 1e-4f) p.Rumo = d.Normalized();
		}

		// 5b) A BOLA QUE NAO ANDA -- a MINA. `Ki_Bomb` cria as bolas em volta do alvo e NUNCA chama
		//     `walk` nem `step` nelas (`blasts.dm:404-450`): elas ficam densas onde nasceram ate o
		//     `Burnout(40)` de quatro segundos. Sao um campo minado, e a graca e o alvo ter que sair
		//     de dentro dele.
		//
		//     PRECISA SER UMA SAIDA, E NAO "andar zero": o passo 7 desconta o alcance a partir do
		//     `andado`, e o `andado` e consumido mesmo com o rumo nulo -- a mina gastaria os trinta
		//     tiles dela PARADA e se apagaria em um segundo. O teste de corpo continua acontecendo,
		//     uma vez por tique e no lugar onde ela esta: quem morre na mina e quem ANDA ATE ELA.
		if (p.Rumo.LengthSquared < 1e-6f)
		{
			Colidiu(p, corpos);
			return;
		}

		// 6) O AVANCO, FATIADO. O DM anda um tile inteiro de uma vez a cada `lag` tiques; a 30 Hz
		//    isso e um salto visivel de 32 px, e foi exatamente o defeito que o arremesso teve
		//    (ver `TickDoEmpurrao`). Aqui a velocidade total e a mesma e o caminho e percorrido.
		float restante = (float)(ZoneCollision.TileSize / p.SegundosPorTile * dt);
		float andado = 0;

		// QUEM ESTA SENDO LEVADO, resolvido UMA VEZ POR TIQUE e nao por sub-passo.
		//
		// A lista da zona e a mesma que o `Colidiu` ja varre, entao isto nao acrescenta ordem de custo
		// nenhuma -- so uma varredura a mais por tique, e so pra a cabeca que de fato carrega alguem
		// (zero em toda bola, em todo raio no ar e em todo raio encostado num muro).
		//
		// A BUSCA E NA LISTA DA ZONA E NAO NO `_players` de proposito: quem o feixe pode acertar sao os
		// corpos da zona, e nem todo corpo da zona esta no `_players` (o boneco largado nao esta). Ver
		// a recusa dele no `PodeSerLevadoPeloFeixe`.
		ServerPlayer? levado = null;
		if (p.Arrastando != 0)
		{
			foreach (ServerPlayer o in corpos)
				if (o.Id == p.Arrastando) { levado = o; break; }
			if (levado == null) p.Arrastando = 0;
		}

		while (restante > 0.001f && p.Vivo)
		{
			float passo = MathF.Min(restante, Projetil.RaioDeImpacto);
			restante -= passo;
			andado += passo;

			Vec2 nova = p.Pos + p.Rumo * passo;

			// 6a) O CENARIO. Quem voa alto atravessa -- a mesma regra do corpo (`AtravessaCenario`),
			//     e ela e o que faz um raio disparado do alto passar por cima do muro.
			if (mapa != null && !Voo.AtravessaCenario(p.Altitude) && mapa.BlockedAt(nova))
			{
				p.Pos = nova;
				Matar(p, FimDeProjetil.Cenario);
				return;
			}

			// ============================ 6a-quater) O PLANETA. **E ele e o cenario do espaco.** ============================
			// K1 do pedido do dono: *"pessoas q estao no espaco poderiam jogar ataques de KI no planeta
			// pra comecar a causar dano nele"*.
			//
			// ---- POR QUE AQUI, E NAO UM ALVO NOVO ----
			// A medicao da fase 0 foi clara: hoje um projetil so encontra DUAS coisas -- um corpo
			// (`Colidiu`) e uma parede (o passo 6a logo acima). No espaco ele nao encontra nem uma nem
			// outra: `mapa` e nulo (a zona do espaco nao tem arquivo de mapa), entao o passo 6a **nunca
			// roda** e o disco do planeta na tela era atravessado sem nada acontecer.
			//
			// Fazer do planeta um `ServerPlayer` de mentira, ou uma entidade nova com vida propria,
			// seria uma terceira classe de alvo -- com colisao propria, snapshot proprio e um segundo
			// jeito de "acertar". O que ele e, de verdade, e a PAREDE do espaco: uma coisa solida que
			// nao se mexe e onde o tiro acaba. Entao ele entra ao lado da parede, no mesmo laco de
			// sub-passos e com o mesmo desfecho (`FimDeProjetil.Cenario`, que o cliente ja desenha).
			//
			// ---- E O TESTE E O MESMO DO POUSO ----
			// `Espaco.PlanetaSob` (via `MundoNoCaminho`), a mesma pergunta que decide se um CORPO
			// encostou no planeta. Assim "encostar nele" quer dizer a mesma coisa pro tiro e pra
			// pessoa, e nao ha duas nocoes de acertar um mundo.
			//
			// ---- DENTRO DO LACO, E NAO NO FIM DO TIQUE ----
			// Pela mesma razao que o 6a e o sulco do chao estao aqui: um tiro rapido anda ate 53 px por
			// tique e um disco de planeta tem 220 a 440 px de diametro -- testar so no fim deixaria o
			// tiro entrar e sair pela borda de um mundo pequeno sem tocar nele.
			//
			// ---- ALTITUDE NAO CONTA AQUI ----
			// De proposito, e e a diferenca em relacao ao 6a: `AtravessaCenario` existe pra deixar um
			// raio passar POR CIMA de um muro de dois metros. Nao ha "por cima" de um planeta.
			// ==========================================================================================================
			if (noEspaco && MundoNoCaminho(nova) is { } mundo)
			{
				p.Pos = nova;
				AtingirMundoComKi(mundo, p, dono);

				// `Mundo` E NAO `Cenario`, e a diferenca e so de ESCALA -- ver o enum. A regra dos dois
				// e a mesma (o tiro acaba aqui); o que muda e que o desenho do `Cenario` foi
				// dimensionado pra a quina de um muro de 32 px e some em cima de um disco de 440.
				Matar(p, FimDeProjetil.Mundo);
				return;
			}

			// 6a-ter) O CORPO VAI JUNTO, e ele anda ANTES da cabeca.
			//
			//     A ordem nao e detalhe: se a cabeca avancasse primeiro, um corpo prensado contra um
			//     muro seria atravessado pela cabeca e o feixe passaria POR DENTRO de quem ele esta
			//     esmagando. Empurrando primeiro, "nao coube" vira a resposta da cabeca tambem -- ela
			//     para no ultimo ponto livre e volta a moer parada, que e o que um Kamehameha
			//     encostado numa pessoa contra uma parede deve fazer.
			if (levado != null
				&& !ArrastarComOFeixe(p, levado, p.Rumo * passo, mapa, zona, temChao))
			{
				// PRENSADO: a cabeca nao avanca -- **mas ela continua moendo**, e essa e a parte que
				// a bancada teve que ensinar. A primeira versao voltava seco daqui, e o `Colidiu`
				// nunca mais rodava: um Kamehameha segurado em cima de alguem prensado num muro
				// parava de causar dano nenhum, calado. Quem ARBITRA a cadencia continua sendo o
				// mesmo par `Encostado`/`AteMoerDeNovo` -- se o ciclo de 0,2 s ainda nao venceu, o
				// proprio `Colidiu` pula a vitima e nada acontece; vencido, ele cobra o tique.
				Colidiu(p, corpos);
				return;
			}
			if (p.Arrastando == 0) levado = null;   // largou no meio do tique (alcance, morte, zona)

			p.Pos = nova;

			// 6a-bis) O CHAO POR ONDE ELE PASSOU. Dentro do laco e nao no fim do tique de proposito:
			//     e a mesma disciplina do arremesso (`TickDoEmpurrao` chama por FATIA) -- um raio
			//     rapido anda ate 53 px por tique, e carimbar so no fim deixaria buraco de uma celula
			//     no rastro. Quem faz a marca sair UMA VEZ por tile e a guarda de celula do
			//     `CarimbarSulco`, e nao a cadencia da chamada.
			MarcarSulcoDoTiro(zona, p, temChao, mapa);

			// 6b) OS CORPOS. Uma varredura da lista da zona por sub-passo -- e o custo que o teto
			//     mede.
			if (Colidiu(p, corpos)) return;
		}

		// 7) O ALCANCE, contado em TILES como no DM (`distance--` a cada tile andado). E ele que
		//    tambem alimenta o `mods` por distancia e a forca do empurrao de perto.
		p.Distancia -= andado / ZoneCollision.TileSize;
		if (p.Distancia <= 0) Matar(p, FimDeProjetil.Apagou);
	}

	/// <summary>
	/// A CABECA ENCOSTOU EM ALGUEM? Devolve verdadeiro quando o tiro acabou aqui.
	///
	/// `avoidusr = 1` nos tres: nenhum deles machuca quem atirou. No DM isso e por tecnica (a Final
	/// Explosion machuca o dono de proposito), entao fica como campo e nao como lei.
	/// </summary>
	private bool Colidiu(Projetil p, List<ServerPlayer> corpos)
	{
		foreach (ServerPlayer o in corpos)
		{
			if (o.Id == p.Dono || o.Ficha.dead || o.Combate == null || o.Combate.Intocavel) continue;

			// QUEM JA ESTA SENDO LEVADO NAO E TESTADO DE NOVO ATE O CICLO VENCER.
			//
			// A cabeca que carrega alguem anda ENCOSTADA nele -- e a distancia entre os dois e menor
			// que o raio de impacto por construcao. Sem esta linha, cada sub-passo seria um `Acertar`:
			// o dano de um feixe encostado passaria dos 5 tiques por segundo do `sleep(2)` do DM pros
			// ~30 do tique do servidor, ou seja seis vezes o dano combinado com nada avisando.
            //
			// `Encostado` e o relogio: ele cai quando o ciclo de 0,2 s vence (ver `AndarProjetil`
			// passo 4), e ai a vitima volta a ser vista, leva o tique de dano e o arrasto e regado.
			// (o `!= 0` nao e redundante: `Arrastando` zerado nao pode casar com um id de corpo, e
			//  um dia em que a numeracao comecar do zero e um dia em que esse corpo vira invisivel
			//  pro feixe sem ninguem entender por que.)
			if (p.Arrastando != 0 && o.Id == p.Arrastando && p.Encostado) continue;

			if ((o.Pos - p.Pos).LengthSquared > Projetil.RaioDeImpacto * Projetil.RaioDeImpacto) continue;

			// A ALTURA MANDA: um raio rasante nao acerta quem esta duas camadas acima, e quem esta
			// no chao nao e alvo de quem passa alto. Mesma assimetria do soco (`Voo.PodeAcertar`).
			if (!Voo.PodeAcertar(Voo.Andar(p.Altitude), Voo.Andar(o.Altitude))) continue;

			return Acertar(p, o);
		}
		return false;
	}

	// =====================================================================
	// O IMPACTO -- e ele e decidido AQUI, nunca no cliente
	// =====================================================================
	/// <summary>
	/// O `Bump(mob)` do DM (`objects.dm:283-455`), na ordem dele: credito e tag de combate, treino
	/// dos dois lados, esquiva, dano, deflexao/reflexao/absorcao, membro, empurrao.
	///
	/// Devolve verdadeiro quando o projetil acabou neste corpo.
	/// </summary>
	private bool Acertar(Projetil p, ServerPlayer alvo)
	{
		// SEM DONO NAO HA IMPACTO, e isso e falha ALTA e nao silenciosa: um tiro cujo atirador sumiu
		// do `_players` no meio do voo nao tem a quem creditar o dano, e o funil de derrota
		// (`ResolverDesfecho` -> Zenkai, luto, raiva) precisa dos dois lados. O caminho normal ja
		// mata esses tiros na saida do dono (`LimparProjeteisDeUmDono`); se um escapar, ele APAGA em
		// vez de machucar sem relato -- ferir alguem sem ninguem no outro lado e o defeito que
		// ninguem consegue descrever.
		if (!_players.TryGetValue(p.Dono, out ServerPlayer? dono))
		{
			Matar(p, FimDeProjetil.Apagou);
			return true;
		}

		CombatState cd = alvo.Combate!;

		// 1) CREDITO E TAG. `M.lastDamager = proprietor` + `refresh_combat_tag()` dos dois: sem isto
		//    matar de longe nao daria Zenkai a quem caiu, e o comentario do proprio DM diz isso.
		alvo.UltimoAgressor = dono.Id;
		cd.EntrarEmCombate();
		dono.Combate?.EntrarEmCombate();

		// 2) TREINO DOS DOIS LADOS. Quem atira ganha `Blast_Gain(3-5 x fight_gain_mult)`; quem
		//    apanha ganha `kidefensecounter++`, que e a UNICA fonte de defesa contra ki.
		// O BIT DO MESTRE VALE PRO TIRO TAMBEM: no DM o `fight_gain_mult` e um so e o
		// `Blast_Gain` o consome igual ao soco. Ver `GameServer.Combat.cs` pro bloco inteiro.
		dono.Ficha.BlastGain(_rng, dono.Ficha.FightGainMult(alvo.Ficha, EhMeuMestre(dono, alvo)));
		// APANHAR DE KI E A UNICA FONTE DE DEFESA CONTRA KI, e o exp vai pra QUEM APANHOU (`alvo`) e
		// nao pra quem atirou: `M.kidefensecounter++` (`objects.dm:313`), onde `M` e o alvo do Bump.
		if (!alvo.Ficha.KO) { alvo.Ficha.kidefenseskill += 0.1; CreditarContador(alvo, "kidefensecounter", 1); }

		// 2b) OS COLETORES ABERTOS COMEM O TIRO INTEIRO -- e vem ANTES do dano e antes de qualquer
		//     sorteio, de proposito. Ver `EngoliuOAtaqueDeKi`: a postura do androide de absorcao e
		//     uma CERTEZA comprada com imobilidade, e nao uma chance a mais em cima da deflexao.
		//
		//     DEPOIS do credito e do treino de propósito: quem atirou continua marcado em combate e
		//     continua treinando o proprio ki. O que ele nao consegue e machucar.
		if (EngoliuOAtaqueDeKi(alvo, p.Nome, p.Fisico))
		{
			Avisar(dono, $"{alvo.Name} ABSORVE seu ataque -- ele nem sente.");
			Matar(p, FimDeProjetil.Defletido);
			return true;
		}

		double mods = p.ModsAgora();
		double dano = DanoDeKi.Final(mods, p.BaseDano, p.MaxDano, p.Bp, cd, cd.Bloqueando, p.Fisico);

		// 3) A DEFLEXAO. Metade da chance e a BARATA (o alvo so anda de lado e o tiro segue),
		//    metade e a cara (deflete de vez, ou o Android ABSORVE). Nocauteado nao defende nada.
		double chance = DanoDeKi.ChanceDeDeflexao(alvo.Ficha, p.Bp, mods, p.BaseDano,
												  cd.Bloqueando, p.Fisico);
		if (!p.Deflectivel || alvo.Ficha.KO || cd.Stun > 0) chance = 0;

		// 3a) A PARALISIA, e ela cai AQUI: entre a conta da chance de deflexao e os sorteios dela, na
		//     mesma ordem do `Bump` (`objects.dm:347`, logo depois de `if(M.KO||M.stagger)
		//     deflectchance=0` e logo antes de `prob(deflectchance/2)`). A posicao nao e detalhe --
		//     assim ela pega ate quem VAI defletir o tiro no sorteio seguinte, que e o que faz uma
		//     Paralysis valer alguma coisa contra quem se defende bem.
		//
		//     `if(!M.paralysistime)` no original: um segundo tiro em cima de quem JA esta paralisado
		//     NAO renova o prazo. Sem essa guarda uma barragem prenderia pra sempre -- e as tres
		//     tecnicas que carregam paralisia (Paralysis, Kill Driver, Stunlock) sao justamente as
		//     que dao pra repetir.
		if (p.Paralisia && !alvo.Ficha.KO && !_paralisadoAte.ContainsKey(alvo.Id))
		{
			double seg = Projetil.SegundosDeParalisia(p.Bp, alvo.Ficha);
			_paralisadoAte[alvo.Id] = NowMs() + (long)(seg * 1000);
			MandarEfeito(alvo, "paralisia", (long)(seg * 1000));
			Avisar(alvo, $"{p.Nome} tranca suas pernas: voce nao consegue andar ({seg:0.#}s).");
			Avisar(dono, $"{alvo.Name} fica paralisado.");
		}

		if (Sorteio(chance / 2) && alvo.Ficha.Ki >= 5)
		{
			// `M.kidefensecounter += 4` (`objects.dm:359`) -- aparar de raspao e o que mais treina
			// defesa de ki: quatro vezes o que se ganha levando o tiro na cara.
			alvo.Ficha.kidefenseskill += 0.4;
			CreditarContador(alvo, "kidefensecounter", 4);
			Avisar(alvo, $"voce desvia {p.Nome} de raspao.");
			return false;   // o tiro CONTINUA: foi o corpo que saiu da linha
		}

		if (Sorteio(chance) && alvo.Ficha.Ki >= 5)
		{
			alvo.Ficha.kidefenseskill += 0.1;
			CreditarContador(alvo, "kidefensecounter", 1);   // `M.kidefensecounter++` (`objects.dm:365`)
			alvo.Ficha.Ki -= 5 * alvo.Ficha.BaseDrain();

			// O ANDROIDE COME O TIRO: `M.Ki += 100`. E a unica defesa do jogo que LUCRA.
			// (a deflexao cara vem antes do embate de guarda de proposito -- ver logo abaixo)
			if (alvo.Race is "Android" or "Cyborg" && !p.Fisico)
			{
				alvo.Ficha.Ki += 100;
				Avisar(alvo, $"seu corpo ABSORVE {p.Nome}.");
			}
			else Avisar(alvo, $"voce defletiu {p.Nome}.");

			Avisar(dono, $"{alvo.Name} defletiu seu ataque.");
			Matar(p, FimDeProjetil.Defletido);
			return true;
		}

		// 3b) ELE NAO DEFLETIU E NAO VAI ABAIXAR A GUARDA: o feixe encontra as MAOS dele, e isso e um
		//     EMBATE e nao um tique de dano. **Novo** -- ver `TentarEmbateDeGuarda`, que conta o que a
		//     guarda fazia contra ki ate aqui e por que o poder das maos nao e um numero inventado.
		//
		//     DEPOIS DO SORTEIO DE DEFLEXAO, de proposito: quem teve sorte defende de graca, como
		//     sempre teve. E so pra RAIO -- uma bola nao da tempo de agarrar nada, e no DM a disputa
		//     tambem e privilegio do `WaveAttack`.
		if (p.Tipo == TipoDeProjetil.Beam && TentarEmbateDeGuarda(p, alvo)) return true;

		// 4) O DANO NO CORPO, pelo MESMO caminho do soco depois do numero pronto.
		//
		// ============================ A MIRA E DE QUEM ATIRA, E ERA DE QUEM APANHAVA ============================
		// Esta linha passava `cd.ZonaMirada` -- e `cd` e o corpo do ALVO. Quer dizer: mirar na cabeca
		// e soltar um raio nao fazia diferenca nenhuma, e quem escolhia onde ia levar o tiro era quem
		// estava levando. No soco a pergunta sempre foi a certa (`MeleeResolver.cs:152` sorteia com
		// `a.ZonaMirada`, o atacante) -- o tiro e que trocou os dois lados porque, aqui, quem esta na
		// mao e o defensor.
		//
		// Achado pela bancada `--kideponta`, que compara as duas chamadas em vez de contar em qual
		// membro o dado caiu -- o membro e SORTEADO, entao teste de comportamento mediria o dado.
		// ==================================================================================================
		GolpeResultado r = MeleeResolver.AplicarDanoPronto(cd, dano, p.Letal, _rng,
														   dono.Combate?.ZonaMirada);
		ResolverDesfecho(dono, alvo, r);
		AnunciarGolpe(dono, alvo, r, nivel: 2);

		// 5) O EMPURRAO, e ele tem DOIS RAMOS que se excluem -- o `if`/`else` do
		//    `Projectiles.dm:573-591`, com o corte em 4 tiles que as duas fontes escrevem igual
		//    (`beam_stun_start = 4` no DU, `maxdistance-distance <= 4` no Finale).
		//
		//    PERTO: ARREMESSA -- forca cheia ate 2 tiles, metade ate 4 (*"harder to knock back at
		//    range"*). Impulso unico, pelo funil do soco, e o corpo sai voando pra longe do feixe.
		// `p.Empurra` E A PRIMEIRA PERGUNTA, e ela e nova: no DM o empurrao mora dentro de
		// `if(WaveAttack)` / `else if(kiforceful)` (`objects.dm:450-460`), e uma bola que nao e nenhum
		// dos dois **nao arremessa ninguem**. A Paralysis e exatamente esse caso, e o empurrao dela
		// estava desmentindo a propria tecnica -- quem levava o tiro ficava sem andar E sem bater, que
		// e o stun que ela existe pra nao ser. Ver `ReceitaDeProjetil.Empurra`.
		double fator = p.FatorDeEmpurrao();
		if (p.Empurra && fator > 0 && r.Dano > 0.25 && alvo.TiquesDeVoo <= 0 && !alvo.Ficha.dead)
			Arremessar(alvo, p, r.Dano * fator);

		// 5b) LONGE: CARREGA. `step(P,dir,32)` a cada ciclo, ate 10 tiles da mao do dono -- o pedido
		//     literal do dono (*"deveriam EMPURRAR A PESSOA JUNTO conforme o beam vai indo"*), que e
		//     porte e nao invencao. Ver `Projetil.Arrastando`.
		//
		//     Ele e REGADO aqui e nao so armado: este bloco roda uma vez por ciclo de 0,2 s enquanto a
		//     cabeca estiver em cima da vitima, e o prazo do `ArrastoRestante` dura menos que isso --
		//     o que segura o corpo entre um ciclo e outro e o `ArrastarComOFeixe`, que rega a cada
		//     tique em que de fato empurra.
		else if (p.PodeArrastar() && PodeSerLevadoPeloFeixe(alvo)) ComecarArrasto(p, alvo);

		// 6) O RAIO NAO MORRE EM QUEM ACERTA: ele EMPURRA. No DM o `Bump` de um `WaveAttack` nao
		//    apaga a cabeca -- ela fica presa contra o corpo e, a cada ciclo de 0,2 s, o segmento
		//    seguinte bate de novo (mais o `MiniStun`). E o que faz segurar um Kamehameha em cima de
		//    alguem ser diferente de acertar uma bola nele.
		if (p.Tipo == TipoDeProjetil.Beam)
		{
			p.Encostado = true;
			p.AteMoerDeNovo = Projetil.SegundosPorCicloDeBeam;
			return true;   // a cabeca para aqui neste tique; ela nao morreu
		}

		// O PIERCER ATRAVESSA. Sem ele a bola morre no primeiro corpo -- que e o normal.
		if (p.Piercer) return false;

		Matar(p, FimDeProjetil.Acertou);
		return true;
	}

	/// <summary>
	/// `Knockback(M, kbstr)` (`objects.dm:663`): `kbdur = min(strength, 10)` tiques, `kbdir` e a
	/// direcao do TIRO -- quem leva raio vai pro lado pra onde o raio ia, e nao pra longe de quem
	/// atirou. Reusa o mesmo arremesso do soco pesado (`TickDoEmpurrao`).
	/// </summary>
	private void Arremessar(ServerPlayer alvo, Projetil p, double forca)
	{
		int tiques = (int)Math.Clamp(Math.Round(forca), 1, Empurrao.TiquesMax);
		alvo.ArrastoRestante = 0;   // arremesso ganha do arrasto -- a razao inteira esta no `Arremessar` do `GameServer.Empurrao.cs`
		alvo.TiquesDeVoo = tiques;
		alvo.TiquesIniciaisDoVoo = tiques;
		alvo.RumoDoVoo = p.Rumo;
		alvo.ForcaDoVoo = p.Bp;
		alvo.VooNoTique = 0;
		alvo.UltimoSulco = default;
		MarcarSulco(alvo, Protocol.Decal.SulcoPonta);
		MandarFicha(alvo);
	}

	// =====================================================================
	// O ARRASTO -- o feixe levando quem ele acertou
	// =====================================================================
	/// <summary>
	/// ESTE CORPO PODE SER LEVADO POR UM FEIXE?
	///
	/// ============================ O QUE O DM RECUSA, E O QUE ESTE PORT RECUSA A MAIS ============================
	/// A lista de recusas do DU esta no ramo do ARREMESSO e nao no do arrasto
	/// (`Projectiles.dm:573`): `P.type != /mob/Body && !P.KO && P.client`. Quer dizer que la o corpo
	/// largado, o nocauteado e o NPC caem no `else` -- ou seja **sao carregados**. Nao e descuido: o
	/// arremesso e um impulso que precisa de alguem em pe pra fazer sentido, e ser varrido por um
	/// muro de energia nao precisa de ninguem consciente.
	///
	/// Entao a decisao aqui e: **nocauteado E carregado, NPC E carregado** -- e por isso nao ha `if`
	/// pra nenhum dos dois nesta funcao. Quem apanha desacordado de um Kamehameha vai junto com ele,
	/// que e o que se ve no desenho e o que o DU escreve.
	///
	/// AS DUAS RECUSAS QUE EXISTEM SAO DO PORT, e as duas por razao propria:
	///
	///   * **CORPO LARGADO** -- e a pergunta e literalmente `_players.ContainsKey`, e nao um `if` de
	///     `Peer`/`Cerebro`. Duas razoes que apontam pro mesmo lado. A do jogo: o boneco e VOCE parado
	///     enquanto a sua atencao esta noutro lugar -- "ele nao anda, nao bate, nao decide; ele so
	///     ocupa o lugar e apanha" --, e move-lo mudaria de lugar o corpo de alguem que nao esta
	///     olhando. A da maquina, que e a que fecha a questao: **o boneco nao esta no `_players`** (o
	///     `GameServer.CorpoLargado` diz que essa e a guarda dele, e nao um `if`), e `_players` e
	///     exatamente a lista que o `TickDoEmpurrao` percorre pra escorrer o prazo. Carregar o boneco
	///     seria o unico corpo do jogo cujo `ArrastoRestante` ninguem desconta -- um congelamento
	///     permanente por desenho. Perguntar pela LISTA e o unico jeito de a recusa nao poder divergir
	///     de quem de fato solta o corpo. (O `/mob/Body` do DU tambem esta na lista de recusas de la,
	///     ainda que no outro ramo.)
	///
	///   * **JA ESTA VOANDO POR UM ARREMESSO** (`TiquesDeVoo > 0`). E a regra da casa: dois sistemas
	///     empurrando o mesmo corpo brigam, e quem ganha e quem escreveu `Pos` por ultimo. O arremesso
	///     chegou primeiro e ele tem prazo proprio; o feixe espera o corpo pousar. Na pratica isto quase
	///     nunca acontece (o arremesso do proprio feixe so nasce a menos de 4 tiles, onde o arrasto nem
	///     comeca), mas "quase nunca" e exatamente o intervalo em que esse tipo de defeito mora.
	/// ==========================================================================================================
	/// </summary>
	private bool PodeSerLevadoPeloFeixe(ServerPlayer alvo)
		=> alvo.TiquesDeVoo <= 0
		   && !alvo.Ficha.dead
		   && _players.ContainsKey(alvo.Id);

	/// <summary>
	/// A CABECA PEGOU ESTE CORPO. So aponta e avisa -- quem empurra e o <see cref="ArrastarComOFeixe"/>.
	/// </summary>
	private void ComecarArrasto(Projetil p, ServerPlayer alvo)
	{
		if (p.Arrastando == alvo.Id) return;   // ja estava levando: nao ha o que reanunciar

		p.Arrastando = alvo.Id;

		// O ANGULO DO CORPO SAI DAQUI, e sem campo novo: `ServerPlayer.DirecaoDeitado` ja le o
		// `RumoDoGolpe` quando nao ha arremesso, e "de onde veio o ultimo golpe" e literalmente o que
		// este vetor quer dizer. E o parente do `P.dir = turn(dir,180)` do DU (`Projectiles.dm:587`):
		// la a vitima e virada A FORCA pro lado do feixe, aqui ela e desenhada deitada no rumo dele
		// -- a mesma folha e a mesma tabela de rotacao do arremesso, que e o que o cliente ja sabe
		// desenhar quando o bit de "o servidor esta me dirigindo" acende.
		//
		// PELA PORTA DO CORPO (`ApontarRumoDoGolpe`): e la que o morto recusa girar. O feixe ja nao
		// leva morto (`PodeSerLevadoPeloFeixe`), mas a regra e do corpo e nao deste chamador.
		alvo.ApontarRumoDoGolpe(p.Rumo);
		alvo.Moving = false;

		// AS REDEAS SAEM NO INSTANTE DO IMPACTO, e nao no primeiro empurrao.
		//
		// A primeira versao deixava a rega so pro `ArrastarComOFeixe`, que roda no tique SEGUINTE (o
		// impacto acontece dentro do avanco, e o avanco do tique ja acabou). Resultado medido pela
		// bancada: por um tique inteiro o corpo estava "pego" pelo feixe e ainda passava no
		// `PodeMexerOCorpo` -- ou seja o jogador (e a IA) davam um passo proprio depois de ja terem
		// sido agarrados, e o bit `Empurrado` ia na ficha errada. Trinta e tres milissegundos de duas
		// autoridades sobre o mesmo corpo e exatamente o intervalo em que o tremor mora.
		alvo.ArrastoRestante = Empurrao.SegundosPorTique;

		// A FICHA SAI AGORA, e nao no proximo `TickFichas` (5 Hz). E a MESMA licao que o `Arremessar`
		// conta em detalhe: o bit `Empurrado` so viaja na ficha, e ate ela chegar o cliente continua
		// integrando tecla contra as correcoes -- o corpo tremendo. Com o canal confiavel, mandar aqui
		// poe a ficha NA FRENTE da primeira correcao.
		MandarFicha(alvo);
	}

	/// <summary>
	/// O CORPO ANDA O QUE A CABECA ANDOU. Devolve FALSO quando ele nao coube -- e ai a cabeca para
	/// nele.
	///
	/// ============================ AS TRES DECISOES QUE ESTE METODO TOMA ============================
	///
	/// **1. QUANDO O FEIXE MORRE, O CORPO PARA SECO -- nao ha inercia.**
	/// No DU o arrasto e uma sequencia de `step()`: movimento discreto, sem velocidade guardada em
	/// lugar nenhum. Acabou o feixe, acabou o empurrao no mesmo instante, e a vitima fica onde estava.
	/// Dar inercia seria inventar fisica que nem o DM tem nem o dono pediu -- e teria que ser desfeita
	/// por um segundo relogio, que e mais um dono pro mesmo corpo.
	///
	/// Aqui isso sai **de graca e sem ninguem precisar lembrar**: este metodo e o unico que rega o
	/// <see cref="ServerPlayer.ArrastoRestante"/>, e o feixe so chega ate ele estando vivo, fora de
	/// embate e com a cabeca andando. Morreu, foi defletido, esbarrou num muro, entrou numa disputa ou
	/// o dono soltou -- em todos, a rega para e o prazo escorre no <c>TickDoEmpurrao</c>.
	///
	/// **2. PRENSADO NUMA PAREDE: os dois param, e o feixe CONTINUA MOENDO.**
	/// E o que o DM faz, nos dois lugares em que ha um corpo empurrado: o `step()` do arrasto
	/// simplesmente falha contra densidade, e o `Knockback` escreve a mesma conclusao com todas as
	/// letras -- `if(loc == old_loc) { KB=0; break }` (`death.dm:230`). O feixe **nao explode e nao
	/// atravessa**: ele fica encostado e o ciclo de 0,2 s continua cobrando dano. Em jogo isso e o
	/// certo pelos dois lados -- prensar alguem contra um muro com um Kamehameha e a melhor coisa que
	/// pode acontecer pra quem atira, e a pior pra quem apanha, e nao um jeito de escapar.
	///
	/// **3. NA AGUA E NO AR, QUEM RESPONDE E O MODO DE TRAVESSIA DO PROPRIO CORPO.**
	/// `ModoDeTravessiaDe(alvo)` -- a MESMA funcao que valida o passo do jogador e o da IA. Quem esta
	/// nadando ou voando atravessa o lago sendo levado, quem esta a pe para na beira. Isso tambem e o
	/// DM: la o `step()` do arrasto passa pelo `Enter()` normal (o `testWaters()`), e nao pelo desvio
	/// que o `KB` tem. Repare que e diferente do ARREMESSO, que passa por cima da agua sempre
	/// (`ModoDeTravessia.Arremessado`, `Swim.dm:31`) -- e a diferenca existe no original tambem, e nao
	/// e uma escolha deste port: no DU o proprio `Knockback` corta na agua (`if(IsWater(T)&amp;&amp;!Flying)
	/// KB=0`), enquanto no Finale ele passa. Ficou a regra do Finale pro arremesso (que ja estava) e a
	/// do `Enter()` pro arrasto (que e a que o arrasto usa nas duas fontes).
	///
	/// E acima do limiar de voo nao ha mapa nenhum a consultar -- `AtravessandoCenario`, a mesma linha
	/// que o `Input` escreve. Um feixe disparado por cima do muro leva a vitima por cima do muro.
	/// ==============================================================================================
	/// </summary>
	private bool ArrastarComOFeixe(Projetil p, ServerPlayer alvo, Vec2 delta,
								   ZoneCollision? mapa, ZoneKey zona, bool temChao)
	{
		// ELE AINDA PODE SER LEVADO? Morreu, foi arremessado por outra coisa ou saiu do jogo no meio
		// do tique -- larga, e a cabeca segue viagem (nao e "prensado", entao devolve verdadeiro).
		if (!PodeSerLevadoPeloFeixe(alvo)) { p.Arrastando = 0; return true; }

		// O FIM DA CORDA: dez tiles da mao do dono. `if(getdist(Owner,P)==10) BigCrater(...)`
		// (`Projectiles.dm:589`) -- o DU larga o corpo ali e abre uma cratera onde ele parou. O feixe
		// nao morre junto: ele continua andando sozinho, so nao leva mais ninguem.
		if (p.AndouTiles >= Projetil.TilesDeArrasto)
		{
			p.Arrastando = 0;

			// A CRATERA SO NASCE ONDE HA CHAO PRA ABRIR. E a mesma conta que o rastro do arremesso
			// aprendeu do jeito caro: no vacuo a altitude e sempre zero, e sem esta pergunta um
			// arrasto no espaco carimbaria uma cratera no nada -- pra a `ZoneList` do espaco, que e o
			// universo inteiro. Ver `RastroVale`.
			if (temChao && alvo.Altitude <= 0f)
			{
				MandarDecalque(zona, Protocol.Decal.Cratera, alvo.Pos, alvo.Facing);
				MandarDecalque(zona, Protocol.Decal.Fumaca, alvo.Pos, alvo.Facing);
			}
			return true;
		}

		Vec2 destino = alvo.Pos + delta;

		// PAREDE E AGUA -- ver a decisao 2 e a 3 no cabecalho.
		ZoneCollision? chao = AtravessandoCenario(alvo) ? null : mapa;
		if (chao != null && MoveRules.Occupied(chao, destino, ModoDeTravessiaDe(alvo))) return false;

		alvo.Pos = destino;

		// A REGA. Enquanto ela acontece o corpo e do feixe; parando, ele se solta sozinho.
		alvo.ArrastoRestante = Empurrao.SegundosPorTique;

		// O SULCO NO CHAO **NAO** SAI DAQUI, e isso e deliberado: a cabeca do feixe ja esta carimbando
		// a mesma fileira de celulas neste mesmo sub-passo (`MarcarSulcoDoTiro`), e o corpo levado anda
		// coladinho nela. Duas marcas por celula leem como MANCHA e nao como rastro -- e a guarda de
		// celula do `CarimbarSulco` e por dono de rastro, entao ela nao pegaria a segunda. E o mesmo
		// defeito que o dono ja fotografou uma vez ("as vezes fica um pouco torto"), pela outra ponta.
		return true;
	}

	// =====================================================================
	// O RASTRO NO CHAO -- o MESMO do arremesso
	// =====================================================================
	/// <summary>
	/// O RAIO ARA A TERRA POR ONDE PASSA.
	///
	/// ============================ ISTO E DIVERGENCIA DELIBERADA DO DM ============================
	/// Pedido do dono, literal: *"ataques de ki como BEAM deveriam criar um RASTRO NO CHAO igual o
	/// knock back por onde passam"*. NENHUMA das duas fontes faz isso: o `craterseries.dmi`
	/// (`obj/impactditch`) so nasce de duas maos, e as duas sao CORPO arremessado -- o `Throw.dm:117`
	/// do Finale e o `Knockback` do `death.dm:217` do DU. O blast do DM mexe no cenario de outro
	/// jeito: DESTROI turf (`objects.dm:490`) e abre cratera na explosao. Fica anotado como
	/// acrescimo do port, e nao como porte.
	///
	/// O QUE E PORTE, ESSE SIM, e a onda na agua -- e ela mora do lado do cliente
	/// (`World.Decalques.cs`), porque nao depende de nada que so o servidor saiba.
	/// ============================================================================================
	///
	/// ============================ E A ARTE E A MESMA, DE PROPOSITO ============================
	/// O `Decal.Sulco` desenha o `craterseries` -- terra revirada. Um raio nao cava terra: ele
	/// QUEIMA. Mesmo assim a arte reusada e essa, por dois motivos: (1) o dono pediu "igual o knock
	/// back", ou seja a comparacao e com o rastro que ele ja ve; (2) a folha de queimado nao existe
	/// na conversao, e inventar uma agora seria escolher arte no lugar do dono. No dia em que houver
	/// uma, muda-se UMA linha -- o tipo passado aqui -- e nada mais.
	/// ==========================================================================================
	/// </summary>
	private void MarcarSulcoDoTiro(ZoneKey zona, Projetil p, bool temChao, ZoneCollision? mapa)
	{
		if (!RastroDoTiroVale(p, temChao)) return;

		// ============================ NA AGUA NAO SE ARA: NA AGUA SE ONDULA ============================
		// A celula molhada ja tem a marca dela -- a onda do `KiWater`, que o cliente abre sozinho
		// quando o tiro cruza a agua (ver `World.TickDaAguaDosTiros`). Carimbar terra revirada em cima
		// de um lago desenharia as DUAS coisas na mesma celula, e uma delas seria uma cratera de terra
		// boiando.
		//
		// Cada celula recebe a marca DO QUE ELA E, e quem sabe o que ela e, do lado do servidor, e o
		// plano de agua do `.col` (`ClasseDeAgua`). Custa um bit por marca, e so pra raio rasteiro.
		// ==============================================================================================
		if (mapa != null && mapa.EhAguaEm(p.Pos)) return;

		// A CELULA E A DA CABECA, e nao "a dos pes" do corpo: um raio nao tem pes. A cabeca e o
		// unico ponto denso dele (ver `Projetil.Pos`), e e ela que encosta no que estiver no caminho.
		//
		// A DIRECAO E O RUMO DO PROPRIO TIRO -- a mesma escolha do arremesso, que carimba pelo
		// `RumoDoVoo` e nao pelo `Facing`: o sulco e a marca do que PASSOU ali.
		CarimbarSulco(zona, Protocol.Decal.Sulco, p.Pos,
					  MoveRules.FacingFrom(p.Rumo, Facing.South), ref p.UltimoSulco);
	}

	/// <summary>
	/// ESTE TIRO ARA O CHAO? Tres perguntas, e as tres tem irmas no <c>RastroVale</c> do arremesso.
	///
	///   * (a quarta, a agua, mora no <see cref="MarcarSulcoDoTiro"/>, porque depende do MAPA e nao
	///     do tiro.)
	///   * SO O RAIO. A bola e uma esfera que voa e estoura -- ela nao ARRASTA nada pelo chao, e o
	///     campo minado da `Ki_Bomb` (sete bolas paradas) pintaria sete manchas de terra sem nada ter
	///     acontecido. O raio e um muro de energia encostado no chao pelo tempo todo em que existe, e
	///     e sobre ele que o dono falou. Ligar os outros dois e trocar esta linha.
	///   * NO CHAO. `Altitude <= 0` e a mesma guarda do corpo: quem voa nao ara a terra. Um
	///     Kamehameha disparado do alto passa por cima do muro (`Voo.AtravessaCenario`) e agora
	///     tambem passa por cima do chao sem risca-lo.
	///   * EXISTE CHAO (<paramref name="temChao"/>, que e o `Espaco.EhPlaneta` da zona) -- a conta que
	///     o arremesso ja aprendeu do jeito caro: no vacuo a altitude e sempre zero, e sem esta
	///     pergunta uma briga no espaco carimbaria terra batida no nada, pra a `ZoneList` do espaco,
	///     que e o universo INTEIRO. Ela chega PRONTA porque custa uma varredura com alocacao e a
	///     resposta e a mesma pra zona inteira -- ver `TickDosProjeteis`.
	/// </summary>
	private static bool RastroDoTiroVale(Projetil p, bool temChao)
		=> p.Tipo == TipoDeProjetil.Beam
		   && p.Altitude <= 0f
		   && temChao;

	/// <summary>`prob(n)` do DM: n em cem. Negativo nunca sai, acima de 100 sempre sai.</summary>
	private bool Sorteio(double porcento) => porcento > 0 && _rng.NextDouble() * 100 < porcento;

	/// <summary>
	/// O TIRO ACABOU. Pra bola e imediato; pro raio, a cabeca PARA e o rastro e engolido primeiro
	/// (ver <see cref="Projetil.Esvaziando"/>) -- um raio de vinte tiles nao pode sumir num quadro.
	/// </summary>
	private static void Matar(Projetil p, FimDeProjetil porque)
	{
		if (!p.Vivo) return;

		if (p.Tipo == TipoDeProjetil.Beam && !p.Esvaziando && p.Comprimento > Projetil.RaioDeImpacto)
		{
			p.Esvaziando = true;
			p.Canalizando = false;
			p.FimPendente = porque;
			return;
		}

		p.Vivo = false;
		p.Fim = porque;
	}

	// =====================================================================
	// A REDE
	// =====================================================================
	private List<Projetil> ProjeteisDaZona(ulong hash)
	{
		if (!_projeteis.TryGetValue(hash, out List<Projetil>? l)) _projeteis[hash] = l = [];
		return l;
	}

	/// <summary>
	/// OS TIROS DE UMA ZONA DENTRO DO SNAPSHOT -- o segundo bloco, depois dos corpos.
	///
	/// SEMPRE ESCREVE, mesmo com zero: o leitor do outro lado nao tem como adivinhar que o bloco
	/// nao veio, e um pacote de tamanho variavel sem marcador e o jeito classico de dessincronizar
	/// um protocolo binario em silencio. Zero tiro custa DOIS bytes.
	/// </summary>
	/// <param name="perto">
	/// ============================ O RECORTE DO ESPACO ============================
	/// Nulo nas zonas normais: quem esta na zona ve a zona inteira, e o bloco sai igual pra todo
	/// mundo (um buffer, uma escrita).
	///
	/// No ESPACO nao da: a zona e UMA pro universo inteiro, entao "todos os tiros da zona" seria
	/// todo tiro dado em qualquer canto da galaxia. Com a posicao na mao, o corte e o MESMO que o
	/// bloco de corpos logo acima ja usa (<see cref="Espaco.PertoDeMim"/>, chunks vizinhas) -- e nao
	/// uma segunda nocao de "perto".
	///
	/// O `Nasceu`/`Morreu` continua indo pra zona inteira de proposito: e por ele que o cliente
	/// CRIA o desenho (tipo, arte, escala, altura), e um tiro que nascesse longe e voasse pra ca
	/// chegaria sem nunca ter sido criado. Este bloco so MOVE o que ja existe.
	/// ==========================================================================
	/// </param>
	private void EscreverProjeteis(NetDataWriter w, ulong hash, Vec2? perto = null)
	{
		if (!_projeteis.TryGetValue(hash, out List<Projetil>? l) || l.Count == 0)
		{
			w.Put((ushort)0);
			return;
		}

		if (perto is not { } onde)
		{
			w.Put((ushort)l.Count);
			foreach (Projetil p in l)
				new ProjetilState
				{
					Id = p.Id, Pos = p.Pos, Tipo = (byte)p.Tipo, Cauda = p.Cauda,
				}.Write(w);
			return;
		}

		// DUAS VOLTAS, e nao uma lista temporaria: o contador vem ANTES dos itens no fio, e alocar
		// uma lista por jogador por tique num snapshot de 30 Hz seria lixo por quadro por pessoa.
		ushort quantos = 0;
		foreach (Projetil p in l) if (Espaco.PertoDeMim(onde, p.Pos)) quantos++;

		w.Put(quantos);
		foreach (Projetil p in l)
		{
			if (!Espaco.PertoDeMim(onde, p.Pos)) continue;
			new ProjetilState
			{
				Id = p.Id, Pos = p.Pos, Tipo = (byte)p.Tipo, Cauda = p.Cauda,
			}.Write(w);
		}
	}

	/// <summary>
	/// NASCEU / MORREU pra zona inteira, no canal CONFIAVEL. Ver <see cref="Protocol.S2C.Projetil"/>
	/// sobre por que estes dois momentos nao viajam junto da posicao.
	/// </summary>
	private void AnunciarProjetil(ulong hash, Protocol.ProjetilSub sub, Projetil p)
	{
		var w = Protocol.Begin(Protocol.S2C.Projetil);
		w.Put((byte)sub);
		w.Put(p.Id);
		if (sub == Protocol.ProjetilSub.Nasceu)
		{
			w.Put(p.Dono);
			w.Put((byte)p.Tipo);
			w.Put((ushort)p.Arte);
			w.Put(Protocol.EscalaDeProjetilEmByte(p.EscalaVisual));

			// A ALTURA, UM BYTE, UMA VEZ -- e a mesma escala do corpo (`Voo.ParaByte`, ~2,5 px por
			// degrau). Ela nao muda depois do nascimento (o `Altitude` do projetil e copiado do dono
			// no `Disparar` e ninguem mais escreve nele), entao ela e evento e nao estado: por o
			// campo no `ProjetilState` cobraria o mesmo byte por tiro POR TIQUE POR ZONA, a 30 Hz.
			//
			// E ELA PRECISA VIAJAR: o servidor JA usa a altura do tiro pra valer (`AtravessaCenario`,
			// `PodeAcertar`), mas ela nunca chegava ao cliente -- o feixe de quem voa era desenhado no
			// plano do chao, ate 160 px ABAIXO do corpo que o disparou (`Voo.AlturaMaxima` 640 x
			// `Voo.EscalaNaTela` 0,25). Ler a altura do DONO nao serve: ele pousa, o tiro nao.
			w.Put(Jandirus.Core.World.Voo.ParaByte(p.Altitude));
			w.PutVec(p.Pos);
		}
		else
		{
			w.Put((byte)p.Fim);
			w.PutVec(p.Pos);
		}

		foreach (ServerPlayer o in ZoneList(hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// SOLTA O RAIO DA MAO DE ALGUEM, sem aviso -- pra quem saiu do jogo ou trocou de planeta e nao
	/// tem mais tela onde ler o recado.
	/// </summary>
	private void SoltarDoRaio(int id)
	{
		if (_canais.TryGetValue(id, out CanalDeKi? c)) FecharCanal(id, c, null);
		_blastPronto.Remove(id);
		_volleyPronto.Remove(id);
		_alvoPronto.Remove(id);
		_debuffPronto.Remove(id);

		// A PARALISIA NAO SAI AQUI, DE PROPOSITO. Este metodo tambem roda na TROCA DE PLANETA
		// (`GameServer.cs:2987`), e limpar o efeito ali daria uma saida de graca: bastaria decolar
		// pra as pernas voltarem. Ela e apagada so quando o CORPO vai embora -- ver
		// <see cref="EsquecerParalisia"/>, chamado no caminho de desconexao.
	}

	/// <summary>
	/// OS TIROS SOLTOS DE UM DONO QUE FOI EMBORA MORREM.
	///
	/// ============================ POR QUE ELES NAO SOBREVIVEM AO ATIRADOR ============================
	/// No DM sobrevivem: o `proprietor` e uma referencia, o mob desloga e a bola continua voando. Aqui
	/// nao da, e o motivo e o funil de derrota: `ResolverDesfecho` credita Zenkai, luto e raiva ao
	/// ATACANTE, e sem ele o impacto teria que ferir sem relatar -- ou relatar pra um id que nao
	/// existe. As duas saidas sao piores que apagar o tiro.
	///
	/// O que se perdeu: nao da pra atirar e sair do planeta pra o raio acertar sozinho. O que se
	/// ganhou: nao existe dano sem algoz.
	/// ================================================================================================
	/// </summary>
	private void LimparProjeteisDeUmDono(int dono, ulong hash)
	{
		if (!_projeteis.TryGetValue(hash, out List<Projetil>? l) || l.Count == 0) return;

		for (int i = l.Count - 1; i >= 0; i--)
		{
			if (l[i].Dono != dono) continue;
			Projetil p = l[i];
			l.RemoveAt(i);
			_projeteisVivos--;

			// MORTE SECA, e nao pelo `Matar`: aquele poe o RAIO em esvaziamento (a cabeca para e o
			// rastro e engolido ao longo de varios tiques), e este tiro acabou de sair da lista --
			// ninguem mais o vai tiquear, entao ele ficaria eternamente "esvaziando" e o pacote de
			// morte sairia com motivo `Nenhum`.
			p.Vivo = false;
			p.Fim = FimDeProjetil.Cessou;
			AnunciarProjetil(hash, Protocol.ProjetilSub.Morreu, p);
		}
	}

	/// <summary>
	/// O MAPA DE UMA ZONA PELO HASH. O <see cref="MapaDaZonaOuCatalogo"/> pede a `ZoneKey` inteira,
	/// e a lista de projeteis so guarda o hash -- entao a chave sai do primeiro corpo da zona. Uma
	/// zona sem corpo nenhum nao tem quem colida com o cenario mesmo.
	///
	/// A CHAVE SAI JUNTO em vez de por um segundo metodo: o rastro no chao tambem precisa dela, e a
	/// busca e a mesma (a lista da zona). Dois metodos fariam a mesma varredura duas vezes por
	/// tique, e um deles ficaria pra tras no dia em que a origem da chave mudar.
	/// </summary>
	private ZoneCollision? MapaDaZonaDoHash(ulong hash, out ZoneKey chave)
	{
		List<ServerPlayer> l = ZoneList(hash);
		chave = l.Count == 0 ? default : l[0].Zone;
		return l.Count == 0 ? null : MapaDaZonaOuCatalogo(chave);
	}

	// =====================================================================
	// O CENARIO DO ESPACO -- os discos que um tiro pode encontrar la fora
	// =====================================================================
	/// <summary>
	/// O QUE HA NO CEU PERTO DE CADA CHUNK, memorizado por TIQUE.
	///
	/// ============================ POR QUE ISTO PRECISA DE MEMORIA ============================
	/// `Espaco.PorPerto` nao e uma consulta: ela varre um 3x3 de CELULAS de sistema, hasheia cada
	/// uma, monta os planetas de cada orbita que alcanca e **devolve uma lista nova**. Chamar isso
	/// por sub-passo de cada tiro seria a mesma armadilha que o proprio `TickDosProjeteis` ja
	/// documenta pro `Espaco.EhPlaneta` ("milhares de alocacoes por tique"), so que pior: um tiro
	/// rapido tem quatro sub-passos, e o teto da zona sao 256 tiros.
	///
	/// A resposta nao muda dentro de um tique (o universo e funcao pura da seed) e os tiros do espaco
	/// se amontoam em volta de quem atirou -- entao a memoria e tipicamente **uma entrada**, e ela e
	/// jogada fora no comeco de cada tique pra nunca virar cache.
	/// ====================================================================================
	/// </summary>
	private readonly Dictionary<ChunkId, List<PlanetaNoEspaco>> _mundosPerto = [];

	/// <summary>
	/// O MUNDO QUE ESTE PONTO ESTA TOCANDO -- nulo em ceu aberto. Ver <see cref="_mundosPerto"/>.
	///
	/// ============================ PLANETA DESTRUIDO E CEU ABERTO ============================
	/// `Espaco.PorPerto` nao filtra morto (ela e do Core e nao conhece o registro), entao o filtro e
	/// aqui -- e ele e obrigatorio pela mesma razao que o cliente parou de desenhar o disco de um
	/// mundo destruido: **o planeta some**. Um tiro estourando no nada, no lugar onde a Terra ficava,
	/// seria o mesmo defeito de familia visto do outro lado.
	/// ==================================================================================
	/// </summary>
	private PlanetaNoEspaco? MundoNoCaminho(Vec2 pos)
	{
		ChunkId c = ChunkId.De(pos);
		if (!_mundosPerto.TryGetValue(c, out List<PlanetaNoEspaco>? perto))
			_mundosPerto[c] = perto = Espaco.PorPerto(SeedDoUniverso, c);

		foreach (PlanetaNoEspaco p in perto)
		{
			// O MESMO TESTE DO POUSO (`Espaco.PlanetaSob`): distancia ao centro contra o raio do
			// disco. Escrito aqui em vez de chamado porque `PlanetaSob` refaz o `PorPerto` por
			// chamada -- e e justamente esse `PorPerto` que a memoria acima existe pra evitar.
			if ((pos - p.Pos).LengthSquared > p.Raio * p.Raio) continue;
			if (PlanetaMorto(p)) continue;
			return p;
		}
		return null;
	}
}
