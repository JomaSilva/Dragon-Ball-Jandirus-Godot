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
		if (!ParalisiaAtiva(id)) return false;
		return _rng.Next(1, 13) <= 11;
	}

	/// <summary>
	/// A PARALISIA ESTA EM VIGOR? -- sem a fresta e sem sorteio. E a metade de <see cref="Paralisado"/> que
	/// nao e do passo: quem pergunta por um deslocamento que NAO e passo (o arranque do `Aproximar`, que
	/// salta ate quinze tiles de uma vez) quer saber se as pernas estao trancadas, e nao se esta tentativa
	/// escapou. Esquece o prazo vencido, como o `Paralisado` sempre fez.
	/// </summary>
	private bool ParalisiaAtiva(int id)
	{
		if (!_paralisadoAte.TryGetValue(id, out long ate)) return false;
		if (NowMs() >= ate) { _paralisadoAte.Remove(id); return false; }
		return true;
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

		/// <summary>
		/// Segundos que ainda faltam de carga. Zero ou menos = a carga FECHOU -- e o raio ja saiu da mao
		/// (<see cref="Atirando"/>) ou esta sendo segurado nela (<see cref="Segurando"/>).
		/// </summary>
		public double CargaRestante;

		/// <summary>O tiro que esta saindo da mao. Nulo enquanto carrega e enquanto a carga fechada e segurada.</summary>
		public Projetil? Raio;

		/// <summary>
		/// O QUE O CORPO GRITA QUANDO O RAIO SAI, se nao for o nome da tecnica: o `S.shout` de uma tecnica inventada
		/// com o `doshout` ligado (`customattacks.dm:412-413`). Nulo = grita o nome, como todo raio portado.
		/// </summary>
		public string? Grito;

		/// <summary>Quanto Ki cada ciclo de 0,2 s cobra enquanto o raio esta de pe.</summary>
		public double CustoPorCiclo;

		/// <summary>(v0.4.0) O SHATTERING THE LIMIT desta carga: 2 quando ela custou 10%+ do Ki de entao (`EstadoRacial.FatorDoLimite`), 1 no resto.</summary>
		public double Limite = 1;

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

		/// <summary>O RAIO JA SAIU DA MAO -- o `beaming` do DM.</summary>
		public bool Atirando => Raio != null;

		/// <summary>
		/// A CARGA FECHOU E O RAIO AINDA ESTA NA MAO, esperando o segundo aperto do verb: o `charging` do DM depois
		/// do limiar (`accum >= 10*chargedelay/3`, `beams.dm:279`). So a receita que SEGURA para aqui
		/// (<see cref="ReceitaDeProjetil.SeguraACarga"/>); nas outras a carga vira raio no mesmo tique em que fecha.
		///
		/// DERIVADO, como o resto do estado do canal: nao ha bit de "segurando" pra alguem esquecer de apagar. Quem
		/// solta escreve o <see cref="Raio"/>, e a pergunta muda de resposta sozinha.
		/// </summary>
		public bool Segurando => CargaRestante <= 0 && Raio == null;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada --tecnicateste): a carga que devia ficar na mao sai sozinha quando fecha -- o
	/// jogo de antes, em que o botao "sai sozinho" da mesa nao separava nada.
	/// </summary>
	internal bool CargaSeguradaSaiSozinhaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --tecnicateste): o raio inventado grita o nome dele, e nao o grito que o jogador escreveu.</summary>
	internal bool GritoDaMesaIgnoradoDeTeste;

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
	/// (2026-10-19) A CARGA DESTE CORPO E A DO SHATTERING THE LIMIT (o raio que vai sair 2x)? E o que pinta de vermelho a bola na mao
	/// (`EntityState.CanalDoLimite`), como o raio (`Projetil.DoLimite`); `Passivas.LimiteSemVermelhoDeTeste` apaga os dois.
	/// </summary>
	private bool CargaDoLimite(int id) =>
		!Jandirus.Core.Races.Passivas.LimiteSemVermelhoDeTeste && _canais.TryGetValue(id, out CanalDeKi? c) && c.Limite > 1;

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
		GanhoDeKi(pl);   // so rende se a bola encostar em algo -- ver `GameServer.Ganhos.cs`
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
		if (alvo == null || alvo == pl || EhCadaver(alvo) || !alvo.Zone.Equals(pl.Zone))
		{
			alvo = null;
			// O AVISO E DO DM, literal: *"Sem alvo selecionado: o ataque voa reto."* Ele existe
			// porque o tiro sai igual nos dois casos, e sem a frase o jogador acha que a tecnica
			// nao funciona quando na verdade e ele que nao marcou ninguem.
			Avisar(pl, "sem alvo marcado: a esfera vai voar reto. (Marque alguem com duplo clique.)");
		}

		pl.Ficha.Ki -= custo;
		GanhoDeKi(pl, vezes: 2);   // `usr.Blast_Gain()` duas vezes -- a esfera custa e rende dobrado (quando encosta)

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
	/// <summary>
	/// DEFEITO INJETADO (bancada `--v030teste`): o tiro volta a recusar TODO morto, inclusive o de pe no Outro Mundo -- o
	/// jogo de antes. Lido pela primeira recusa do <see cref="PodeAtirar"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool MortoNaoAtiraDeTeste;

	private bool PodeAtirar(ServerPlayer pl, double custo, out string porque)
	{
		porque = "";
		if (pl.Combate == null) { porque = "seu corpo ainda nao esta pronto."; return false; }
		// (2026-10-15) O MORTO DE PE ATIRA -- so o CADAVER nao. O dono, no Outro Mundo: *"n ta dando pra usar ataques de ki
		// tb como morto"*. Era o `dead` cru, e la todo mundo esta morto; o soco, o voo e o Zanzoken ja perguntavam pelo cadaver.
		if (MortoNaoAtiraDeTeste ? pl.Ficha.dead : EhCadaver(pl)) { porque = "voce esta morto."; return false; }
		if (pl.Ficha.KO) { porque = "voce esta caido."; return false; }
		// O `canfight` QUE TODO VERB DE KI DO DM PERGUNTA (`if(KO || med || train || !canfight || charging ||
		// beaming) return 0`, `customattacks.dm:486`; `&&canfight` nos raios, `Kamehameha.dm:48`), e que aqui e o
		// atordoamento (ver `ProntoPraGolpeG3`): faltava nesta porta, e quem acabava de levar o golpe atirava no
		// meio dele. A mesma frase das tecnicas de corpo.
		if (!AtiraAtordoadoDeTeste && pl.Combate.Stun > 0) { porque = "voce ainda esta atordoado."; return false; }
		if (pl.Ficha.med) { porque = "nao da pra atirar meditando."; return false; }
		if (pl.Ficha.train) { porque = "pare o treino antes de atirar."; return false; }
		if (_canais.ContainsKey(pl.Id)) { porque = "voce ja esta com um raio na mao."; return false; }
		// O `blasting` das tecnicas sustentadas do lote G12 (Death Ball, Buster Barrage, as rajadas, a Genkidama).
		if (BlastingG12(pl.Id)) { porque = "voce ja esta com uma tecnica de ki no ar."; return false; }
		// (2026-10-17) ...e o do grito do desabafo: os 3 s em que o ki sai sozinho, pra todo lado (`AndarOGrito`).
		if (pl.Racial.GritoDesde >= 0) { porque = "você está desabafando: o ki sai sozinho, pra todo lado."; return false; }
		// ...e o dos dois tiros do Deus da Destruicao, enquanto a carga deles corre (`GameServer.KitDoDeus.cs`).
		if (CarregandoTiroDoKit(pl.Id)) { porque = "voce ja esta com uma tecnica de ki no ar."; return false; }
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
	/// <param name="naMao">
	/// (v0.3.1) A BOLA NASCE NA MAO, e nao e atirada: a Genkidama se formando e renascendo maior a cada pulso (o "CRESCER E
	/// RENASCER" do `GameServer.Tecnicas.G12`). O corpo NAO faz o gesto de tiro (a pose), e o pacote de nascimento diz isso
	/// (`NascimentoDeProjetil.NaMao`): o cliente nao recomeca a pose nem toca o som da saida -- guarda o som pra quando ela
	/// SAIR (o snapshot desligar o `Inerte`, que quem liga continua sendo quem chama). O dono, 2026-10-16: *"sempre q a
	/// genkidama aumenta o personagem faz animaçao de soco oq n deveria ser"*.
	/// </param>
	/// <param name="doLimite">
	/// (2026-10-19) O RAIO DO SHATTERING THE LIMIT: nasce vermelho (`Projetil.DoLimite`). Parametro, e nao conta feita aqui, porque o 2x e
	/// decidido na CARGA (`CanalDeKi.Limite`, o Ki de antes de pagar) -- e o anuncio de nascimento sai daqui de dentro.
	/// </param>
	private Projetil Disparar(ServerPlayer pl, ReceitaDeProjetil r,
							  Vec2? rumoDado = null, Vec2? deOnde = null, string verbo = "", bool naMao = false, bool doLimite = false)
	{
		// COM ALGUEM MARCADO, O TIRO SAI NELE (dono, 2026-10-09) -- e o corpo vira junto. Ver `RumoDoTiro`.
		// Quem ja traz o rumo pronto (a rajada sorteada, a mina que nasce em volta do alvo) nao e mexido.
		Vec2 rumo = rumoDado ?? RumoDoTiro(pl);

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
			RumoDaSaida = rumo,
			// O RAIO DE RECEITA TELEGUIADA ja nasce com quem perseguir (a bola `Guided` recebe o alvo de quem a
			// atira, logo depois deste metodo). Ninguem marcado = zero = voa reto.
			Alvo = r.Teleguiado ? (AlvoDeTeleguiado(pl)?.Id ?? 0) : 0,
			Distancia = r.AlcanceTiles,
			MaxDistancia = r.AlcanceTiles,
			RangeMod = r.RangeMod,
			ModsBase = Projetil.ModsDoTiro(pl.Ficha, r.Tipo) * r.BaseDano,
			// `A.BP = expressedBP * wavemult` (`beams.dm:139`). Fora do Final Flash e do Massive Beam
			// o multiplicador e 1 e esta linha e a de antes.
			Bp = pl.Ficha.expressedBP * r.MultDeOnda,
			MultDeOnda = r.MultDeOnda,   // vai junto: a disputa refaz esta conta com o BP de agora
			BaseDano = r.BaseDano,
			MaxDano = r.MaxDano,
			Letal = pl.Combate?.Letal ?? false,
			Deflectivel = r.Deflectivel,
			Piercer = r.Piercer,
			// O RAIO QUE ESTOURA COMO BOLA (campo da receita), e o que cresce com o poder de quem o segura -- todo
			// raio, menos o do verbo que o dono excluiu (dono, 2026-10-08; ver `Projetil.CrescerComOPoder`). A
			// regua do crescimento comeca no poder deste instante.
			EstouraNoImpacto = r.EstouraNoImpacto,
			CresceComOPoder = r.Tipo == TipoDeProjetil.Beam && verbo != Projetil.VerboQueNaoCresce,
			PoderDeReferencia = pl.Ficha.expressedBP,
			Fisico = r.Fisico,
			Paralisia = r.Paralisia,
			Empurra = r.Empurra,
			Altitude = pl.Altitude,
			Nome = r.Nome,
			Arte = arte,
			Invisivel = r.Invisivel,
			DoLimite = doLimite,   // (2026-10-19) o raio do Shattering the Limit nasce vermelho -- ver `Projetil.DoLimite`
			ZonaCerta = r.ZonaCerta,
			Dom = r.Dom,
			Colossal = r.Colossal,   // (v0.3.0) a Genkidama -- ver `ReceitaDeProjetil.Colossal`
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
			// QUANTO MAIS RAPIDO QUE NO DM (dono, 2026-10-08) -- as distancias de aviso do original andam com ela.
			// A tecnica posta fora da regra do tipo declara a dela (`ReceitaDeProjetil.PressaSobreODm`).
			Pressa = r.PressaSobreODm > 0 ? r.PressaSobreODm
				: r.Tipo == TipoDeProjetil.Beam
					? Projetil.PressaDeRaio
					: Projetil.PressaDeBolaPorLag(Projetil.LagDeBolaNoDm(r.Velocidade)),
		};

		// (2026-10-16) A SYCOPHANTIC RAGE ARMADA: o ataque que estava carregando sai com o poder multiplicado. Ver `FuriaNoDisparo`.
		FuriaNoDisparo(pl, p);
		// (v0.4.0) o Hidden Talent do meio-Saiyajin: a tecnica dominada, e a mesma duas vezes seguidas -- `GameServer.Passivas.cs`
		FatorRacialDoDisparo(pl, p, verbo);

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

		// (v0.3.0) QUEM ESTA COLADO LEVA O TIRO: o trecho do centro de quem atira ate a boca e olhado antes de o tiro nascer --
		// ver `OlharOCano`. So o tiro que sai da MAO: o que nasce em volta do alvo (`deOnde`) nao tem cano.
		if (deOnde == null) OlharOCano(pl, p, berco);

		// O SOM COM QUE ELE SAI (dono, 2026-10-13): a tabela e do `SonsDeKi`, pela tecnica; viaja no nascimento.
		p.Som = SonsDeKi.DoDisparo(verbo, r.Tipo);

		lista.Add(p);
		_projeteisVivos++;
		AnunciarProjetil(pl.Zone.Hash, Protocol.ProjetilSub.Nasceu, p, naMao);
		if (!naMao) FazerAPoseDeTiro(pl);   // (v0.3.1) a bola na mao ainda nao e um tiro -- ver `naMao`
		PrenderOGanhoNoTiro(pl, p);   // o ganho de ki do gesto viaja com o tiro -- ver `GameServer.Ganhos.cs`
		return p;
	}

	/// <summary>
	/// **A POSE DE TIRO EM TODO ATAQUE DE KI** (dono, 2026-10-13: *"animaçao de socar ao usar qualquer ataque de
	/// ki"* -- a folha `blast_*`, que na maioria dos corpos e o proprio desenho do soco; ver `CharacterVisual.SetPose`).
	/// No DM todo verb de bola escreve `usr.icon_state="Blast"` e o desfaz tres tiques depois (`spawn(3)
	/// usr.icon_state=""`, `blasts.dm:82-83` e as irmas); aqui a pose so existia no raio canalizado e no sopro do
	/// Kiai. Quem a leva e o `ServerPlayer.GestoAte`, pelo snapshot: nao prende o corpo e nao cobra nada.
	///
	/// QUEM ESTA COM UM RAIO NA MAO NAO PASSA POR AQUI: o canal ja veste a mesma pose, pelo tempo que durar.
	/// </summary>
	private void FazerAPoseDeTiro(ServerPlayer pl)
	{
		if (TiroSemPoseDeTeste || _canais.ContainsKey(pl.Id)) return;
		pl.GestoAte = Math.Max(pl.GestoAte, NowMs() + PoseDeTiroMs);
	}

	/// <summary>Quanto dura a pose de tiro de uma bola: `spawn(3) usr.icon_state=""` (`blasts.dm:83`).</summary>
	private const long PoseDeTiroMs = 300;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o tiro volta a sair sem a pose -- so o raio canalizado e o Kiai a
	/// faziam. Lido pela propria <see cref="FazerAPoseDeTiro"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool TiroSemPoseDeTeste;

	// =====================================================================
	// A MIRA
	// =====================================================================
	/// <summary>
	/// PRA ONDE ESTE CORPO ATIRA AGORA -- e ele VIRA pra la.
	///
	/// ============================ COM ALGUEM MARCADO, O TIRO SAI NELE (dono, 2026-10-09) ============================
	/// *"ao ter um target e vc usar um ataque de ki o seu personagem ai virar pra direcao do seu target e usar o
	/// ataque de ki na direcao do target"*. A regra e a conta moram no `Core` (<see cref="MiraDeKi"/>, com o que
	/// diverge do DM); aqui e so a costura: o alvo e o MARCADO do combate (<see cref="Marcado"/> -- o mesmo pro qual
	/// o soco ja virava o corpo, e que a IA escreve pra quem enfrenta), e o olhar e cravado por
	/// <see cref="CravarOlhar"/>.
	///
	/// E A PORTA UNICA: o `Disparar` pergunta aqui quando ninguem lhe deu rumo, o `Canalizar` pergunta ao comecar a
	/// carga, e as tecnicas que montam o proprio leque (Buster Shell, as barragens, as balas continuas, a Death
	/// Ball e a Genkidama ao partir) perguntam no lugar do `MeleeArea.Frente(pl.Facing)` que usavam. Quem NAO
	/// pergunta e quem sorteia o rumo de proposito (Buster Barrage, Spin Blast) ou nasce parado em volta do alvo.
	///
	/// SEM NINGUEM MARCADO nada muda: o tiro sai pra onde o corpo olha, e o olhar continua do teclado.
	/// ==============================================================================================================
	/// </summary>
	private Vec2 RumoDoTiro(ServerPlayer pl)
	{
		if (Marcado(pl) is { } alvo && alvo != pl
			&& MiraDeKi.NoAlvo(pl.Pos, alvo.Pos, pl.Facing, out Vec2 rumo, out Facing olhar))
		{
			CravarOlhar(pl, olhar);
			return rumo;
		}
		return MeleeArea.Frente(pl.Facing);
	}

	/// <summary>
	/// O SERVIDOR VIRA ESTE CORPO, e por <see cref="MiraDeKi.OlharDoTiroMs"/> o olhar e dele e nao do teclado.
	///
	/// ============================ AS TRES TELAS TEM QUE CONTAR A MESMA HISTORIA ============================
	/// O `Facing` do servidor so viaja pros OUTROS, no snapshot: o corpo local desenha a propria direcao, e manda
	/// ela de volta a cada pacote de movimento (`pl.Facing = facing`, no fim do `Input`). Virar so aqui dava duas
	/// mentiras -- quem atirou se via de costas pro proprio tiro, e quem assistia via o atirador virar num snapshot
	/// e desvirar no seguinte, quando chegava o pacote que ja estava a caminho com o olhar de antes. E o defeito
	/// que o Zanzoken ja teve, contado em `GameServer.Zanzoken.cs`.
	///
	/// Entao sao duas metades: o dono recebe o olhar (`S2C.Olhar`, e o `LocalPlayer` o segura pelo mesmo prazo), e
	/// ate ele chegar la o `Input` nao deixa o olhar velho passar (`ServerPlayer.OlharCravadoAte`).
	/// ======================================================================================================
	/// </summary>
	private void CravarOlhar(ServerPlayer pl, Facing olhar)
	{
		pl.Facing = olhar;
		pl.OlharCravado = olhar;
		pl.OlharCravadoAte = NowMs() + MiraDeKi.OlharDoTiroMs;
		if (pl.Peer == null) return;   // NPC e corpo de bancada: nao ha tela pra avisar

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Olhar);
		w.Put((byte)olhar);
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O OLHAR QUE VALE NUM PACOTE DE MOVIMENTO: o que o cliente mandou -- menos enquanto o servidor tem o olhar
	/// deste corpo cravado (<see cref="CravarOlhar"/>). O pacote pode ter saido do cliente ANTES de ele saber que
	/// o tiro o virou, e traz o olhar velho: escreve-lo desviraria o corpo um snapshot depois de ele virar.
	/// </summary>
	private Facing OlharDoPacote(ServerPlayer pl, Facing doCliente)
		=> !MiraDeKi.OlharSoltoDeTeste && NowMs() < pl.OlharCravadoAte ? pl.OlharCravado : doCliente;

	/// <summary>
	/// QUEM UM TIRO TELEGUIADO DESTE CORPO PERSEGUE: o marcado, se estiver vivo e aqui -- a tripla do
	/// `CustomGuidedFire` (`if(T &amp;&amp; !T.dead &amp;&amp; T.z == z &amp;&amp; T != src)`, `customattacks.dm:533`).
	/// </summary>
	private ServerPlayer? AlvoDeTeleguiado(ServerPlayer pl)
		=> Marcado(pl) is { } a && a != pl && !EhCadaver(a) && a.Zone.Equals(pl.Zone) ? a : null;   // (2026-10-15) o fantasma e alvo

	/// <summary>
	/// O RAIO TELEGUIADO GIRA ATRAS DO ALVO -- reto, em torno da cauda. A regra (quanto, e ate onde) e do
	/// <see cref="Teleguiado"/>; aqui mora o que e do mundo:
	///   * A CABECA OCUPADA NAO PROCURA NINGUEM: plantada num corpo (moendo) ou levando alguem, ela e daquele corpo
	///     ate o ciclo vencer -- girar ali arrancaria a cabeca de quem ela esta empurrando;
	///   * A MAO ANDA COM O RUMO: a boca do cano e um tile projetado nele (<see cref="BocaDeCano"/>), entao o feixe
	///     alimentado continua saindo da mao em qualquer angulo;
	///   * A PAREDE SEGURA O RAIO: se o feixe girado atravessaria cenario, ele nao gira -- senao o tronco varreria
	///     por dentro de um muro pra alcancar quem se escondeu atras dele (a cabeca so confere parede ANDANDO);
	///   * O CORPO ACOMPANHA O FEIXE: quem o alimenta vira junto, pela mesma porta do disparo.
	/// </summary>
	private void GirarRaioTeleguiado(Projetil p, ServerPlayer mira, ServerPlayer? dono, ZoneCollision? mapa, float andaNoTique)
	{
		if (p.Encostado || p.Arrastando != 0) return;

		float comprimento = (p.Pos - p.Cauda).Length;
		Vec2 novo = Teleguiado.Virar(p.Rumo, mira.Pos - p.Cauda, p.RumoDaSaida,
									 Teleguiado.GiroDoRaio(andaNoTique, comprimento));
		if ((novo - p.Rumo).LengthSquared < 1e-10f) return;

		ServerPlayer? mao = p.Canalizando ? dono : null;
		Vec2 cauda = mao != null ? BocaDeCano.De(mao.Pos, novo) : p.Cauda;
		Vec2 cabeca = cauda + novo * comprimento;
		if (!Teleguiado.VarreParedeDeTeste && mapa != null && !Voo.AtravessaCenario(p.Altitude)
			&& mapa.PathBlocked(cauda, cabeca)) return;

		p.Rumo = novo;
		p.Cauda = cauda;
		p.Pos = cabeca;

		if (mao != null && MoveRules.FacingFrom(novo, mao.Facing) is var olhar && olhar != mao.Facing)
			CravarOlhar(mao, olhar);
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
	/// <param name="gritoAoSoltar">
	/// O que o corpo grita quando o raio SAI, no lugar do nome da tecnica -- ver <see cref="CanalDeKi.Grito"/>.
	/// </param>
	private void Canalizar(ServerPlayer pl, string verbo, double custo, ReceitaDeProjetil r,
						   double? custoPorTiro = null, string? gritoAoSoltar = null)
	{
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		// (v0.4.0) O SHATTERING THE LIMIT pergunta o custo contra o Ki de ANTES de pagar. (2026-10-19) O verb vai junto: o Kamehameha pronto
		// no degrau SSJ4 e o x10 sem os 10% -- ver `Passivas.VerboDoKamehameha`.
		double limite = pl.Racial.FatorDoLimite(custo, pl.Ficha.Ki, verbo);
		pl.Ficha.Ki -= custo;

		// O CORPO JA VIRA PRO MARCADO AO COMECAR A CARGA (dono, 2026-10-09): quem reune energia encara em quem
		// vai atirar. O RUMO do raio nao se decide aqui -- so quando a cabeca nasce (`Disparar`, no fim da carga),
		// com o alvo onde ele estiver naquela hora. O corpo plantado nao gira pelo teclado, entao o olhar fica.
		RumoDoTiro(pl);

		// A CARGA MINIMA VALE PRA TODO RAIO. Ela era ZERADA pra tecnica com o "sai sozinho" da mesa, e quem pagava
		// os 2 pontos nem via o personagem carregar -- ver `ReceitaDeProjetil.SeguraACarga`.
		double carga = Projetil.SegundosDeCarga(r.CargaMinima, pl.Ficha);

		_canais[pl.Id] = new CanalDeKi
		{
			Receita = r,
			Verbo = verbo,
			CargaRestante = carga,
			Grito = gritoAoSoltar,
			// `finalcost = lastbeamcost` e o ciclo cobra `(finalcost/4)*BaseDrain` (`beams.dm:83`),
			// com `lastbeamcost = custo_do_verb / 10` depois da fase de carga (`beams.dm:38`).
			CustoPorCiclo = (custoPorTiro ?? custo) / 10 / 4 * pl.Ficha.BaseDrain(),
			AteOProximoCiclo = Projetil.SegundosPorCicloDeBeam,
			// O DESENHO DA CARGA, resolvido UMA vez -- ver `CanalDeKi.Carga`. A mesma semente que a
			// arte do Kamehameha e a cor da aura ja consomem: sem campo no save, e por isso vale pro
			// NPC e pro clone tambem.
			Carga = Jandirus.Core.Combat.ArteDeProjetil.CargaDeRaio(pl.Race, pl.Class, SementeDeArte(pl)),
			Limite = limite,
		};

		// ============================ O PRAZO DE QUEM NAO TEM TECLADO NASCE ZERADO COM O CANAL (2026-10-07) ============================
		// `var/started = world.time` abre o `npc_beam_loop` (`BeamClash.dm:427`) e e LOCAL do laco: la os 12 s do
		// `BCL_NPC_BEAM_TIME` (`:430`) sao do RAIO, e cada raio conta os seus do zero. Aqui o relogio mora num
		// dicionario por CORPO (`_raioDaIaAte`, ver `TickDoPrazoDeRaioDaIa`), e a conta de um raio que caiu por outra
		// porta que nao o prazo -- golpe, nocaute, Ki, parede, fim de disputa, troca de zona -- ficava pra tras: a
		// faxina daquele tique so roda com algum canal aberto no mundo, e um NPC atirando sozinho e interrompido nao
		// deixa nenhum. O raio seguinte nascia com o tempo do anterior ja gasto; sobrando menos que a carga, o canal
		// fechava ANTES de a cabeca nascer -- o NPC pagava o Ki e nao atirava (tomada de Namek do trailer 2).
		//
		// POR QUE AQUI, NO NASCIMENTO, e nao em quem fecha nem na faxina:
		//   * o canal tem UMA porta de entrada -- a atribuicao acima; o `PodeAtirar` recusa quem ja tem um -- e mais
		//     de uma de saida: o `FecharCanal` e a remocao direta do `TickDosCanaisDeKi` pro corpo que sumiu. Apagar
		//     na saida e um bit que cada porta tem que lembrar de apagar; zerar aqui nao depende de nenhuma delas.
		//   * nem da ORDEM dos tiques. Limpar o dicionario quando o mundo fica sem canal so vale se o tique do prazo
		//     rodar ENTRE os dois raios -- e o soco de um jogador se resolve no pacote (`C2S.Action` -> `Atacar`),
		//     antes do `Tick` em que o corpo dirigido pode reatirar (`TickDosCorposSemDono`, que vem antes do prazo).
		// A faxina de la continua, e virou so higiene: a conta que ela deixa pra tras nunca mais e lida.
		// ================================================================================================================================
		if (!EmbateDeKi.PrazoHerdadoDeTeste) _raioDaIaAte.Remove(pl.Id);

		// A POSICAO VOLTA NA HORA. Sem isto o cliente continuaria andando por ate um tique com a
		// previsao local antes da primeira correcao chegar -- e o corpo pareceria escorregar pra
		// dentro do proprio raio.
		pl.Moving = false;
		Avisar(pl, r.SeguraACarga
			? $"voce planta os pes e comeca a reunir energia ({carga:0.#}s) -- quando ela fechar, aperte de novo pra soltar."
			: $"voce planta os pes e comeca a reunir energia ({carga:0.#}s).");
	}

	/// <summary>
	/// APERTOU O MESMO VERB COM O CANAL DE PE. Devolve verdadeiro quando havia canal deste verb -- e o que o
	/// aperto faz depende de onde o canal esta, como no DM:
	///   * raio na mao: desliga (`if(beaming) { canmove = 1; stopbeaming(); return }`, a primeira linha de todo
	///     verb de raio);
	///   * carga FECHADA sendo segurada: SOLTA o raio (`if(charging&amp;&amp;accum>=10*chargedelay/3) ... beaming=1`,
	///     `beams.dm:279-284`);
	///   * carga ainda correndo: desfaz (`else if(charging&amp;&amp;accum&lt;10*chargedelay/3) stopcharging()`, `:285-287`).
	/// </summary>
	private bool SoltarCanal(ServerPlayer pl, string verbo)
	{
		if (!_canais.TryGetValue(pl.Id, out CanalDeKi? c) || c.Verbo != verbo) return false;
		if (c.Segurando) { NascerORaio(pl, c); return true; }
		FecharCanal(pl.Id, c, "voce fecha a mao e o raio se apaga.");
		return true;
	}

	/// <summary>
	/// A CARGA VIRA RAIO: nasce a cabeca, ja canalizando, e o corpo grita. As duas portas chegam aqui -- o tique,
	/// pro raio que sai sozinho quando a carga fecha, e o segundo aperto do verb, pro que e segurado na mao.
	///
	/// O `c.Verbo` VAI JUNTO e e ele que da arte aos DEZ raios do jogo de uma vez: o canal ja guardava o id do
	/// verb (era so pra o "apertar de novo desliga"), e e exatamente a chave que a `ArteDeProjetil` pede.
	/// </summary>
	private void NascerORaio(ServerPlayer pl, CanalDeKi c)
	{
		// (2026-10-19) o raio do Shattering the Limit nasce VERMELHO (dono) -- a marca vai no anuncio de nascimento, ver `Projetil.DoLimite`
		Projetil raio = Disparar(pl, c.Receita, verbo: c.Verbo,
								 doLimite: c.Limite > 1 && !Jandirus.Core.Races.Passivas.LimiteSemVermelhoDeTeste);
		if (!raio.Vivo)
		{
			FecharCanal(pl.Id, c, "nao ha espaco pra mais energia solta aqui.");
			return;
		}
		raio.Canalizando = true;
		c.Raio = raio;
		FatorRacialDoDisparo(pl, raio, "", c.Limite);   // (v0.4.0) o Shattering the Limit (o Hidden Talent ja veio do `Disparar`)

		// O GRITO DA TECNICA INVENTADA, quando o jogador escreveu um e ligou a caixinha (`if (S.doshout == 1)
		// voice(S.shout)`, `customattacks.dm:412-413`). Este metodo so sabia gritar o NOME da receita, e o grito
		// do disparo de um raio da mesa nunca saia -- so o da bola (dono, playtest de 2026-10-12).
		//
		// E ELE NAO E ENGOLIDO PELO GRITO DA CARGA: as duas falas sao da mesma tecnica, e a carga mais curta da mesa
		// (0,13 s) cabe inteira dentro do intervalo entre duas falas (`MsEntreFalas`). So o grito ESCRITO passa por
		// cima do intervalo, uma vez por raio -- e cada raio paga o Ki e a carga dele, entao isto nao vira megafone.
		if (c.Grito is { Length: > 0 }) _ultimaFala.Remove(pl.Id);
		// (v0.4.0) o tiro do Shattering the Limit grita com o "10x " na frente
		Falar(pl, Protocol.Fala.Diz, (c.Limite > 1 ? Jandirus.Core.Races.Passivas.PrefixoDoLimite : "")
			  + (c.Grito is { Length: > 0 } grito && !GritoDaMesaIgnoradoDeTeste ? grito : c.Receita.Nome + "!!"));
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
			if (EhCadaver(pl) || pl.Ficha.KO || pl.Ficha.med || pl.Ficha.train)
			{
				FecharCanal(id, c, "voce perde a concentracao e o raio se desfaz.");
				continue;
			}

			if (!c.Atirando)
			{
				bool fechouAgora = false;
				if (c.CargaRestante > 0)
				{
					c.CargaRestante -= dt;
					if (c.CargaRestante > 0) continue;
					fechouAgora = true;
				}

				// A CARGA ESTA FECHADA. O raio que SEGURA fica na mao ate o verb ser apertado de novo
				// (`SoltarCanal`): no DM e o jogador quem passa de `charging` a `beaming`, e o `instantattack` da
				// mesa e quem dispensa o segundo aperto -- ver `ReceitaDeProjetil.SeguraACarga`.
				//
				// DIVERGENCIA DECLARADA: segurar nao custa Ki nem engrossa o raio. No DM o laco da carga cobra
				// `lastbeamcost` a cada 3 ciclos e faz o `wavemult` crescer (`beams.dm:36-47`); aqui a carga e uma
				// contagem regressiva (ver `ReceitaDeProjetil.MultDeOnda`), e o raio sai com o que a tecnica declara.
				if (c.Receita.SeguraACarga && !CargaSeguradaSaiSozinhaDeTeste)
				{
					if (fechouAgora) Avisar(pl, "a carga fechou na sua mao -- aperte de novo pra soltar.");
					continue;
				}

				NascerORaio(pl, c);
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
			// devagar, enquanto se paga por ele, e o BP so com ele em cima de algo (o ganho vai no pacote do raio).
			pl.Ficha.beamskill += 0.03;
			CreditarContador(pl, "beamcounter", 3);   // `proprietor.beamcounter += 3` (`objects.dm:317`)
			GanhoDeKi(pl, tiro: c.Raio);

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

					// O ENCONTRO DE DOIS FEIXES (a disputa) e o cruzamento sao testados DENTRO do avanco,
					// sub-passo a sub-passo -- ver o passo 6-pre do `AndarProjetil`. Ja moraram aqui, uma
					// vez por tique: a cabeca anda ate 107 px por tique e a outra tambem, e as duas se
					// ATRAVESSAVAM entre dois tiques sem o gatilho de 32 px ver nada -- "os beams tao se
					// sobrepondo as vezes" (dono, 2026-09-07). A poda continua a mesma (so cabeca de raio
					// canalizada testa disputa), entao o custo de uma zona cheia de bolas nao mudou.
					AndarProjetil(p, dt, corpos, mapa, chave, temChao, noEspaco, lista);

					// QUEM ENCOSTOU NO TRONCO CORTA O FEIXE -- ver `GameServer.Feixe.cs`.
					CortarOndeEncostaram(p, corpos, lista, zona);
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
	/// <param name="lista">
	/// Os tiros desta zona -- os OUTROS feixes que esta cabeca pode encontrar (a disputa, de frente; a
	/// espera, cruzando). Ver o passo 6-pre.
	/// </param>
	private void AndarProjetil(Projetil p, double dt, List<ServerPlayer> corpos, ZoneCollision? mapa,
							   ZoneKey zona, bool temChao, bool noEspaco, List<Projetil> lista)
	{
		ServerPlayer? dono = _players.GetValueOrDefault(p.Dono);
		p.Esperando = false;

		// 0-ante) O RAIO ENGROSSA COM O PODER DE QUEM O SEGURA (dono, 2026-10-08) -- ver `Projetil.CrescerComOPoder`.
		//         Antes de tudo, porque vale tambem pra cabeca presa numa disputa (o passo 1 sai cedo), e esse e o
		//         caso do pedido: o salto de poder no meio da colisao. So enquanto uma mao o alimenta -- o raio solto
		//         leva o tamanho que tinha quando a mao abriu.
		if (p.Canalizando && dono != null) p.CrescerComOPoder(dono.Ficha.expressedBP, dt);

		// 0-pre) QUEM RASPOU E JA SAIU DE CIMA DO TIRO VOLTA A SER ALVO. Antes de tudo, e pra todo estado do tiro
		//        (parado, em disputa, esvaziando): o que se confere e onde o CORPO esta, e ele anda fora deste tique.
		//        (QUEM SAIU DE LADO DA DISPUTA DE GUARDA volta pelo PRAZO dele, que corre aqui -- `Projetil.SaiuDeLado`.)
		p.DescontarAFolgaDeQuemSaiuDeLado(dt);
		if (p.ForaDaLinha != null) ReverQuemSaiuDaLinha(p, corpos);

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
		if (p.VidaRestante <= 0) { AcabarSemBaterEmNada(p, corpos, zona, temChao); return; }

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
				if ((p.Pos - p.Cauda).Length <= passo)
				{
					// (2026-10-13) O RAIO QUE ACABA EM CIMA DE ALGUEM ESTOURA ALI: a mao soltou e a cauda chegou na cabeca
					// plantada num corpo (ou levando um) -- e o fim do ataque, com o clarao e a cratera. Parado atras de
					// outro feixe, sem ninguem na ponta, ele so cessa, como sempre.
					if (p.EstouraSempreQueAcaba && (p.Encostado || p.Arrastando != 0)) ExplodirOndeAcabou(p, corpos, zona, temChao);
					else Matar(p, FimDeProjetil.Cessou);
					return;
				}
				p.Cauda += p.Rumo * passo;
			}
		}

		// 4) O RAIO ENCOSTADO QUE FERE MOI a cada ciclo de 0,2 s (`Projetil.SegundosPorCicloDeBeam`, o ciclo do
		//    `ShootBeam`) -- `Bump` nao apaga a cabeca de um `WaveAttack`. No DM ele bate a cada TIQUE, 12 vezes
		//    por segundo: divergencia declarada na constante.
		//
		//    ============================ E ENTRE UM CICLO E OUTRO ELE ANDA, SE ESTIVER CARREGANDO ============================
		//    Ate aqui "encostado" queria dizer PARADO: a cabeca ficava fincada no corpo e o relogio de
		//    0,2 s era a unica coisa que corria. Isso continua valendo pra quem o raio fere e NAO leva: quem
		//    nao e mais fraco que ele (`KiContraCorpo`, 2026-10-12), quem esta prensado contra um muro e quem
		//    o `PodeSerLevadoPeloFeixe` recusa -- o feixe para nele e moi. (No jogo de antes eram o corpo perto
		//    demais e o que ja tinha passado dos 10 tiles.)
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
				p.BatendoSemFerir = 0; // ...e quem ela so segurava sem ferir e testado de novo, como qualquer um
			}
			else if (p.Arrastando == 0)
			{
				// 4-bis) SEM NINGUEM PRA LEVAR, A CABECA FICA ONDE ESTA -- a nao ser que o corpo em que ela bate SEM
				//        FERIR ande contra ela: ai ela recua e continua na frente dele (`RecuarNaFrenteDeQuemAvanca`,
				//        o "walk through beams" de `objects.dm:451`). So a cabeca parada numa PAREDE entra ali
				//        (`EstourarSemFerir`; ate 2026-10-12, o ramo do corte dos fracos).
				RecuarNaFrenteDeQuemAvanca(p, corpos);
				return;
			}
		}

		// 4-ter) (v0.3.0) QUEM ESTAVA NO CANO quando o tiro saiu da mao leva o tiro AGORA, antes do primeiro passo -- ver
		//        `OlharOCano`. O raio que o pega fica plantado na boca, moendo (ou levando); a bola acaba nele.
		if (p.AlvoNoCano != 0 && AcertarQuemEstavaNoCano(p, corpos)) return;

		// 5) A PERSEGUICAO -- SO ATE ONDE A CURVA DEIXA (dono, 2026-10-09; a regra e os numeros sao do `Teleguiado`).
		//    O `walk_towards` do DM corrigia o rumo TODO passo, sem limite, e o tiro dava a volta em quem perseguia.
		//    Agora ele vira uns graus por tile que anda e nunca aponta a mais de 45 do rumo em que saiu: quem sai
		//    muito da frente dele e perdido. Vale pra bola (o tipo `Guided`) e pro raio de receita teleguiada.
		//
		//    A ESPERA VEM ANTES: enquanto ela corre a bola nao caca e (com o rumo nulo com que a
		//    Hellzone a pare) nao anda. E o `spawn(10)` do `blasts.dm:517` -- ver `Projetil.EsperaDeCaca`.
		if (p.EsperaDeCaca > 0) p.EsperaDeCaca -= dt;
		else if (p.Alvo != 0 && p.Tipo != TipoDeProjetil.Blast
			&& _players.TryGetValue(p.Alvo, out ServerPlayer? mira) && !EhCadaver(mira))
		{
			float andaNoTique = (float)(ZoneCollision.TileSize / p.SegundosPorTile * dt);
			if (p.Tipo == TipoDeProjetil.Beam) GirarRaioTeleguiado(p, mira, dono, mapa, andaNoTique);
			else
			{
				p.Rumo = Teleguiado.Virar(p.Rumo, mira.Pos - p.Pos, p.RumoDaSaida, Teleguiado.GiroDaBola(andaNoTique));
				// A BOLA QUE NASCEU PARADA (o cerco da Hellzone) SAI AGORA: e deste rumo que o leque dela conta.
				if (p.RumoDaSaida.LengthSquared < 1e-6f) p.RumoDaSaida = p.Rumo;
			}
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

		// (2026-10-12) LEVANDO ALGUEM, O RAIO ANDA NA VELOCIDADE DO ARRASTO: a cabeca fica colada no corpo, e o corpo
		// e empurrado pela diferenca de forca (`KiContraCorpo.FatorDoArrasto`, escrito no `Acertar`). O alcance paga
		// pelo que foi andado, entao o raio chega ao MESMO fim -- so demora mais. O piso impede a cabeca de parar.
		if (levado != null && !KiContraCorpo.RegraDeAntesDeTeste) restante *= (float)Math.Clamp(p.FatorDoArrasto, 0.05, 1);

		while (restante > 0.001f && p.Vivo)
		{
			float passo = MathF.Min(restante, Projetil.RaioDeImpacto);
			restante -= passo;
			andado += passo;

			Vec2 nova = p.Pos + p.Rumo * passo;

			// 6-pre) OUTRO FEIXE NO CAMINHO (dono, 2026-09-07). De FRENTE, a disputa comeca aqui -- e a
			//        cabeca passa a ser do embate. De frente SEM poder disputar (2026-09-23), quem e alimentado
			//        empurra quem nao e, e nos outros casos quem chega espera encostado: nenhuma cabeca atravessa
			//        a outra. CRUZANDO, quem bate no tronco (ou na cabeca mais forte) do outro ESPERA: o passo nao
			//        e dado e o alcance nao paga por ele. Ver `GameServer.Feixe.cs`.
			if (p.Tipo == TipoDeProjetil.Beam)
			{
				if (p.Canalizando && TentarEmbateDeFeixes(p, nova, lista)) return;
				if (CabecaDeFrenteNoCaminho(p, nova, lista)
					|| TroncoAlheioNoCaminho(p, nova, lista) || CabecaAlheiaNoCaminho(p, nova, lista))
				{
					p.Esperando = true;
					andado -= passo;
					break;
				}
			}

			// 6-pre-bis) OUTRO ATAQUE DE KI NO CAMINHO, que nao e raio contra raio (dono, 2026-10-13: *"permita a
			//        colisao de quaisquer ataques de ki, beam vs esferas gigantes, beams vs esferas de ki etc"*). A
			//        forca decide quem acaba -- ou, entre um raio alimentado e uma esfera grande, comeca a disputa.
			//        Ver `KiContraKi` e `ChocarComOutroAtaque`. Quem acabou (ou entrou na disputa) para aqui; quem
			//        venceu segue no mesmo sub-passo.
			if (!KiContraKi.SemChoqueDeTeste && ChocarComOutroAtaque(p, nova, lista, corpos, zona, temChao)
				&& (!p.Vivo || p.EmEmbate || p.Esvaziando)) return;

			// 6a) O CENARIO. Quem voa alto atravessa -- a mesma regra do corpo (`AtravessaCenario`),
			//     e ela e o que faz um raio disparado do alto passar por cima do muro.
			if (mapa != null && !Voo.AtravessaCenario(p.Altitude) && mapa.BlockedAt(nova))
			{
				p.Pos = nova;
				AcabarNoCenario(p, corpos, zona, temChao, nova);
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
			// Pela mesma razao que o 6a e o sulco do chao estao aqui: um tiro rapido anda ate 107 px por
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
				AoTiroEncostar(p, dono, GainKnobs.GanhoDeObjeto);   // o chao de um planeta rende como um objeto

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
				// (v0.3.0) A ESFERA COLOSSAL NAO MOI: prensando o corpo num muro ela ESTOURA ali -- o dono quer a Genkidama
				// "arrastar assim como um beam e explodir no final", e uma bola encostada num muro com alguem no meio e o final.
				// (O raio continua moendo: isso e dele.) Sem esta linha ela moia ate a vida dela acabar -- cem segundos.
				if (p.Colossal && !ColossalMoiNoMuroDeTeste)
				{
					ExplodirNoFimDoAlcance(p, levado, zona, temChao);
					return;
				}

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
			//     rapido anda ate 107 px por tique, e carimbar so no fim deixaria buraco de tres celulas
			//     no rastro. Quem faz a marca sair UMA VEZ por tile andado e a guarda de DISTANCIA do
			//     `CarimbarSulco`, e nao a cadencia da chamada.
			MarcarSulcoDoTiro(zona, p, temChao, mapa);

			// 6b) OS CORPOS. Uma varredura da lista da zona por sub-passo -- e o custo que o teto
			//     mede.
			if (Colidiu(p, corpos))
			{
				// (2026-10-12) O TIQUE DA BATIDA TAMBEM PAGA O ALCANCE: a cabeca que bateu e ficou PLANTADA no corpo sai
				// pelo passo 7, como em todo tique em que ela anda. Ate aqui a funcao voltava seca desta linha, ANTES do
				// desconto -- e a cabeca que LEVA alguem bate a cada 0,2 s: um tique em seis andado de graca, uns 20% de
				// caminho alem do alcance (medido: o raio de 12 tiles estourava com a cabeca a 14,25 e a 14,85 tiles da
				// mao; sozinho, a 12,00 cravados).
				//
				// QUEM CONTINUA VOLTANDO SECO: o tiro que acabou neste corpo (morto, ou ja engolindo o rastro) e o que
				// deixou de ser desta cabeca no impacto -- a disputa de guarda (`EmEmbate`) e o deflect, que a solta
				// (`Encostado` cai).
				if (BatidaNaoPagaOAlcanceDeTeste || !p.Vivo || p.Esvaziando || p.EmEmbate || !p.Encostado) return;

				// PAGA-SE O QUE A CABECA ANDOU, e nao os sub-passos que ela deu: o plantio a puxa pra frente do corpo
				// (`PlantarACabecaNaFrenteDe`). Parada em quem ela nao leva -- o igual, a parede --, ela da um sub-passo
				// e e replantada a cada ciclo: andou zero, paga zero. Cobrar o sub-passo esvaziaria o alcance de um raio
				// PARADO, e ele estouraria sozinho em cima de quem so o segura.
				Vec2 plantio = p.Pos - nova;
				andado += plantio.X * p.Rumo.X + plantio.Y * p.Rumo.Y;

				// E QUEM ESTA BATIDA ACABOU DE PEGAR JA VAI NA PONTA: se o alcance acaba neste mesmo tique, a explosao
				// inteira e dele (sem isto ele so entraria na area, como um vizinho).
				if (p.Arrastando != 0 && levado?.Id != p.Arrastando)
					foreach (ServerPlayer o in corpos)
						if (o.Id == p.Arrastando) { levado = o; break; }
				break;
			}
			if (p.Arrastando == 0) levado = null;   // quem ia sendo levado raspou e saiu da linha: a cabeca segue sem ele
		}

		// 7) O ALCANCE, contado em TILES como no DM (`distance--` a cada tile andado). E ele que
		//    tambem alimenta o `mods` por distancia e a forca do empurrao de perto.
		p.Distancia -= andado / ZoneCollision.TileSize;
		if (p.Distancia <= 0)
		{
			// (2026-10-12) O RAIO E A ESFERA GRANDE ESTOURAM NO FIM DO ALCANCE, com ou sem alguem na ponta -- ver
			// `ExplodirNoFimDoAlcance`. A bola comum so se apaga, como sempre.
			if (p.ExplodeNoFimDoAlcance) ExplodirNoFimDoAlcance(p, levado != null && p.Arrastando == levado.Id ? levado : null, zona, temChao);
			else AcabarSemBaterEmNada(p, corpos, zona, temChao);
		}
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
			// O CADAVER TAMBEM LEVA O TIRO (dono, 2026-09-05: "corpos mortos ainda sao corpos de personagem").
			if (o.Id == p.Dono || o.Combate == null || o.Combate.Intocavel) continue;

			// QUEM DEFLETIU NAO E ALVO DO TIRO QUE ELE MESMO DEFLETIU -- ver `Projetil.Desviador`: o pedaco de raio
			// que sai de lado nasce a um contato do corpo dele, e sem esta linha o deflect certo levava o proprio deflect.
			if (p.Desviador != 0 && o.Id == p.Desviador) continue;

			// QUEM RASPOU NESTE TIRO SAIU DA LINHA DELE, e nao e testado de novo enquanto nao sair de cima e voltar
			// -- um encontro, um sorteio. Ver `EncerrarOEncontroNoRaspao`.
			if (p.EstaForaDaLinha(o.Id)) continue;

			// QUEM JA ESTA SENDO LEVADO NAO E TESTADO DE NOVO ATE O CICLO VENCER.
			//
			// A cabeca que carrega alguem anda ENCOSTADA nele -- e a distancia entre os dois e menor
			// que o raio de impacto por construcao. Sem esta linha, cada sub-passo seria um `Acertar`:
			// o dano de um feixe encostado passaria das 5 batidas por segundo deste port
			// (`Projetil.SegundosPorCicloDeBeam`; no DM sao 12, divergencia declarada la) pros ~30 do tique do
			// servidor, ou seja seis vezes o dano combinado com nada avisando.
            //
			// `Encostado` e o relogio: ele cai quando o ciclo de 0,2 s vence (ver `AndarProjetil`
			// passo 4), e ai a vitima volta a ser vista, leva o tique de dano e o arrasto e regado.
			// (o `!= 0` nao e redundante: `Arrastando` zerado nao pode casar com um id de corpo, e
			//  um dia em que a numeracao comecar do zero e um dia em que esse corpo vira invisivel
			//  pro feixe sem ninguem entender por que.)
			if (p.Arrastando != 0 && o.Id == p.Arrastando && p.Encostado) continue;

			// O RAIO ENCOSTA COM A FRENTE (dono, 2026-09-07): o centro da cabeca a `DistanciaDeContato` do
			// centro do corpo e a frente dela na beirada dele -- que e exatamente onde `PlantarACabecaNaFrenteDe`
			// a deixa. Com o raio da cabeca sozinho (16 px) a cabeca plantada a 24 px nunca mais "encostava",
			// e um raio segurado em cima de alguem parava de moer: medido na familia 9 (2,5 moidas/s em vez
			// de 5). A bola continua sendo o raio dela: ela estoura no contato, nao fica na frente de ninguem.
			//
			// E O CONTATO E O DESTE TIRO (2026-09-23): a frente desenhada da cabeca dele (folha x escala) mais a
			// meia largura do corpo -- 24 px na `Beam3`, 136 no Final Flash. Com o numero fixo, a cabeca de 128 px
			// de frente do Final Flash so "encostava" com o centro a 24 px e ja cobria o corpo inteiro.
			// E PELO EIXO, NUMA FAIXA, e nao num circulo -- ver `Feixe.EncostaNoCorpo`. A bola continua sendo o
			// circulo dela.
			if (p.Tipo == TipoDeProjetil.Beam)
			{
				if (!Feixe.EncostaNoCorpo(p, o.Pos, o.Altitude)) continue;
			}
			else if ((o.Pos - p.Pos).LengthSquared > Projetil.RaioDeImpacto * Projetil.RaioDeImpacto) continue;

			// A ALTURA MANDA: um raio rasante nao acerta quem esta duas camadas acima, e quem esta
			// no chao nao e alvo de quem passa alto. Mesma assimetria do soco (`Voo.PodeAcertar`).
			if (!Voo.PodeAcertar(Voo.Andar(p.Altitude), Voo.Andar(o.Altitude))) continue;

			return Acertar(p, o);
		}
		return false;
	}

	// =====================================================================
	// O CANO (v0.3.0) -- quem esta colado leva o tiro
	// =====================================================================
	/// <summary>
	/// **QUEM ESTA DENTRO DO CANO LEVA O TIRO.** O dono: *"se alguem ta mt perto de vc o ataque de ki n acerta essa pessoa,
	/// simplesmente o ki aparece atras dela e erra ou passa por dentro dela"*. O tiro nasce na boca (`BocaDeCano`, um tile a
	/// frente) e so testava corpo depois do primeiro passo: quem estava entre o atirador e a boca ficava atras dele.
	///
	/// No DM o nascimento e `A.loc = src.loc; step(A, A.dir)` (`beams.dm:177-185`, `blasts.dm:109`), e o `step` e um Move: o mob
	/// denso no tile da frente o faz falhar com `Bump(M)` -- quem esta colado leva o tiro no tique em que ele nasce. Aqui, o
	/// corpo mais perto dentro do cano (`BocaDeCano.NoCano`, com a largura que o `Colidiu` usa: o raio de impacto da bola, o
	/// contato da cabeca do raio) fica marcado em `Projetil.AlvoNoCano`, e o primeiro tique o acerta antes do primeiro passo
	/// (`AcertarQuemEstavaNoCano`). MARCADO, E NAO ACERTADO AQUI, porque quem chama o `Disparar` ainda arruma o tiro depois
	/// dele -- o canal, a arma de fogo com o poder dela, a bola teleguiada com o alvo.
	///
	/// ONDE ELE APARECE: a BOLA nasce no corpo (no eixo, na altura dele) -- o estouro e desenhado em quem levou, e nao atras
	/// dele. O RAIO nasce na boca, como sempre: a cabeca nunca fica atras da mao (`Feixe.NaoAtrasDaCauda`), e o impacto a
	/// planta ali, moendo (ou levando) quem esta colado.
	///
	/// `boca` e de onde o tiro sairia sem ninguem no caminho: a boca de sempre, ja com a regra do muro (`Disparar`), ou a saida
	/// de uma bola grande (`LancarGenkidamaG12`).
	/// </summary>
	private void OlharOCano(ServerPlayer pl, Projetil p, Vec2 boca)
	{
		if (CanoCegoDeTeste) return;
		float largura = p.Tipo == TipoDeProjetil.Beam ? Feixe.ContatoNoCorpo(p) : Projetil.RaioDeImpacto;
		ServerPlayer? colado = null;
		float maisPerto = float.MaxValue;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			// OS MESMOS QUE O `Colidiu` ACERTA: nao o dono, so corpo com combate, nao o intocavel, e na altura do tiro.
			if (o.Id == pl.Id || o.Combate == null || o.Combate.Intocavel) continue;
			if (!Voo.PodeAcertar(Voo.Andar(p.Altitude), Voo.Andar(o.Altitude))) continue;
			if (!BocaDeCano.NoCano(pl.Pos, boca, o.Pos, largura, out float aoLongo) || aoLongo >= maisPerto) continue;
			colado = o;
			maisPerto = aoLongo;
		}
		if (colado == null) return;

		p.AlvoNoCano = colado.Id;
		if (p.Tipo != TipoDeProjetil.Beam) p.Pos = p.Cauda = pl.Pos + (boca - pl.Pos).Normalized() * maisPerto;
	}

	/// <summary>
	/// O PRIMEIRO TIQUE DO TIRO QUE ACHOU ALGUEM NO CANO (`OlharOCano`): o acerta antes do primeiro passo, uma vez so. Devolve
	/// verdadeiro quando o tiro acabou (ou parou) nele -- o `Acertar`, como no `Colidiu`. Quem deixou de ser alvo nesse meio
	/// tique (saiu da zona, ficou intocavel, subiu alem do tiro) nao e acertado: o tiro segue o voo dele.
	///
	/// SO COM O TIRO AINDA NO BERCO (`Pos == Cauda`, como o `Disparar` e o `OlharOCano` o deixaram): o cano e do lugar de onde
	/// ele saiu. Quem o pos noutro lugar depois de nascer tirou o tiro do cano -- a familia 7 da `--projetilteste` espalha os
	/// 256 tiros de um corpo no meio da multidao numa grade longe dela, e com a marca valendo todos morriam no primeiro tique
	/// em quem estava colado no atirador. (No raio canalizado a cauda e a boca, refeita pelo passo 3 com o corpo parado.)
	/// </summary>
	private bool AcertarQuemEstavaNoCano(Projetil p, List<ServerPlayer> corpos)
	{
		int id = p.AlvoNoCano;
		p.AlvoNoCano = 0;
		if ((p.Pos - p.Cauda).LengthSquared > 0.25f) return false;
		foreach (ServerPlayer o in corpos)
			if (o.Id == id)
				return o.Combate is { Intocavel: false } && !p.EstaForaDaLinha(o.Id)
					&& Voo.PodeAcertar(Voo.Andar(p.Altitude), Voo.Andar(o.Altitude)) && Acertar(p, o);
		return false;
	}

	/// <summary>
	/// DEFEITO INJETADO (`--v030teste`, familia CC): o cano nao e olhado -- o tiro nasce na boca e quem estava colado fica
	/// atras dele, o jogo de antes. Falso em jogo, sempre.
	/// </summary>
	internal static bool CanoCegoDeTeste;

	// =====================================================================
	// O IMPACTO -- e ele e decidido AQUI, nunca no cliente
	// =====================================================================
	/// <summary>
	/// O `Bump(mob)` do DM (`objects.dm:283-503`), na ordem dele: credito e tag de combate, treino
	/// dos dois lados, esquiva, dano, paralisia, o impacto que nao e golpe (`:355-357`),
	/// deflexao/reflexao/absorcao, membro, empurrao.
	///
	/// TRES DESSES PASSOS JA NAO SAO OS DO DM -- sao a escada por diferenca de poder (dono, 2026-10-12;
	/// `KiContraCorpo`): o impacto que nao e golpe e o de quem e PAREDE pro tiro (quatro vezes o poder
	/// dele), e nao mais o de dano final abaixo de 10 (o corte dos fracos); DEFLETIR pede o dobro do poder
	/// do tiro; e o empurrao e o do passo 5b -- o raio LEVA o mais fraco ate o fim do alcance, quem e mais
	/// forte apanha e fica, e so a bola (e o raio que estoura como ela) ainda arremessa de perto. O jogo de
	/// antes ficou atras de `KiContraCorpo.RegraDeAntesDeTeste`, que so as bancadas ligam.
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
		// E O PESO DA LUTA TAMBEM (`FatorDaLuta`): jogador fora de uma troca de golpes nao rende, e dentro dela rende
		// o dobro. O encosto paga ainda o ganho que o gesto deixou no pacote do tiro -- ver `GameServer.Ganhos.cs`.
		RegistrarAtaque(dono, alvo);
		double luta = FatorDaLuta(dono, alvo);
		if (luta > 0) dono.Ficha.BlastGain(_rng, luta * dono.Ficha.FightGainMult(alvo.Ficha, EhMeuMestre(dono, alvo), EhColegaDeMestre(dono, alvo)));
		AoTiroEncostar(p, dono, luta);
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

		// 2c) O PARRY CONTRA KI (dono, 2026-09-25) -- ver `GameServer.ParryDeKi.cs`. ANTES do dano e dos sorteios
		//     de deflexao, porque ele nao e sorte: e acertar o tempo da guarda. O tiro defletido NAO morre aqui (a
		//     esfera volta pra quem atirou, o raio sai de lado ate o fim do alcance): o que acaba e este impacto.
		if (TentarParryDeKi(p, alvo, dono)) return true;

		// 2d) O ULTRA INSTINTO DESVIA SOZINHO -- `if(ui_try_evade(M, "blast")) { ui_blast_evade_fx(M, src); return 1 }`
		//     (`objects.dm:320-322`): depois do credito e do treino, antes de qualquer conta de dano. O encontro se
		//     encerra como no raspao (a bola some, o raio segue). Ver `EsquivouDoTiroPorInstinto`.
		if (EsquivouDoTiroPorInstinto(p, alvo, dono)) return EncerrarOEncontroNoRaspao(p, alvo);

		double mods = p.ModsAgora();
		double dano = DanoDeKi.Final(mods, p.BaseDano, p.MaxDano, p.Bp, cd, cd.Bloqueando, p.Fisico);
		// 2e) O PODER DA DESTRUICAO -- `dmg = ue_blast_mitigate(M, src, dmg)` (`objects.dm:333`): a Hakai Infusion de
		//     quem atirou penetra, e a Aura of Destruction de quem leva devora parte do ki e reduz o resto.
		dano = DanoDeKiPelaDestruicao(alvo, dono, dano);
		dano = DanoDeKiRacial(p, dono, alvo, dano);   // (v0.4.0) as raciais de quem atira e o rugido de quem leva -- `GameServer.Passivas.cs`

		// A ESCADA POR DIFERENCA DE PODER (dono, 2026-10-12) -- ver `KiContraCorpo`. A razao e medida UMA vez por
		// impacto: o poder expresso de quem leva contra o poder do tiro.
		double razao = KiContraCorpo.Razao(alvo.Ficha.expressedBP, p.Bp);
		bool vaiSerLevado = !KiContraCorpo.RegraDeAntesDeTeste && KiContraCorpo.Arrasta(razao)
							&& p.PodeArrastar() && PodeSerLevadoPeloFeixe(alvo);
		// LEVANDO O CORPO, cada batida cobra so uma fracao: o grosso do dano e o da explosao no fim do alcance.
		if (vaiSerLevado) dano *= KiContraCorpo.FracaoDaBatidaNoArrasto;

		// 3) A DEFLEXAO. Metade da chance e a BARATA (o alvo sai da linha: a bola acaba ali sem dano, o
		//    raio segue viagem -- ver `EncerrarOEncontroNoRaspao`), metade e a cara (deflete de vez, ou o
		//    Android ABSORVE). Nocauteado nao defende nada.
		double chance = DanoDeKi.ChanceDeDeflexao(alvo.Ficha, p.Bp, mods, p.BaseDano,
												  cd.Bloqueando, p.Fisico);
		if (!p.Deflectivel || alvo.Ficha.KO || cd.Stun > 0) chance = 0;
		// `deflectchance = ue_infuse_deflect(src, deflectchance)` (`objects.dm:346`): o tiro infundido de Hakai e dificil de defletir.
		chance = DeflexaoContraAInfusao(dono, chance);

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

		// 3a-ter) O ATORDOADOR (v0.2.1) -- `if(paralysis==2&&!M.KO&&!M.hasForcefield)` (`objects.dm:351-354`), dentro do
		//         mesmo bloco da paralisia e portanto ANTES da parede e dos sorteios: quem ja esta quase caido cai,
		//         defletindo ou nao. O tiro acaba ali. Ver `DerrubarComOAtordoador`.
		if (DerrubarComOAtordoador(p, alvo, dono))
		{
			Matar(p, FimDeProjetil.Acertou);
			return true;
		}

		// 3a-bis) A PAREDE -- O IMPACTO QUE NAO E GOLPE (REGRA DO DONO, 2026-10-12; `KiContraCorpo`): quem tem quatro
		//         vezes o poder do tiro nao o sente. Nao fere, nao e sorteado, nao empurra, nao atordoa, nao abre
		//         embate de guarda (3b) e nao derruba o raio de quem leva. Ver `EstourarSemFerir`.
		//
		//         O LUGAR E O DO CORTE DOS FRACOS DO DM (`objects.dm:355-357`), e a posicao e a dele: DEPOIS de tudo que
		//         o `Bump` faz a qualquer tiro que encosta -- o credito e a tag (1), o treino dos dois lados (2), a
		//         paralisia (3a) -- e ANTES dos dois sorteios (`:358` e `:364`) e do ramo do dano (`:420-503`). O RAMO e
		//         o mesmo, e o que ele faz foi medido no BYOND (os numeros estao no `DanoDeKi.CorteDoFraco`); mudou
		//         QUEM cai nele. La e o impacto de dano final abaixo de 10, e entre iguais o raio caia quase sempre
		//         abaixo disso: era o vilao de mesmo poder atirando sem efeito. O corte por dano ficou como o defeito
		//         injetado (`KiContraCorpo.RegraDeAntesDeTeste`): e o outro lado do `?` logo abaixo.
		//
		//         O EMBATE DE GUARDA, que e deste port (o DM nao tem), FICA DEPOIS DA PAREDE, como ficava depois do
		//         corte -- decisao do dono (2026-10-08): so ha disputa quando ha o que perder, e o raio que nao feriria
		//         quem segura para na frente dele como para na frente de qualquer um. O que mudou naquele dia foi a
		//         ESCALA das maos (`EmbateDeKi.PoderDeSegurar`): elas se medem pelo MESMO `dano` da linha de baixo, sobre
		//         `EmbateDeKi.DanoQueAsMaosEmpatam`, e "segurar e devolver" deixou de ser cena de bancada (a
		//         `--embatekiteste` 13 mede a escada). A alternativa que ficou de fora -- `TentarEmbateDeGuarda` AQUI,
		//         antes do `EstourarSemFerir`, pra quem segura devolver tambem o raio que nao o fere -- abriria disputa
		//         sem aposta: quem a perdesse nao levaria nada, porque o raio continuaria sem ferir.
		if (KiContraCorpo.RegraDeAntesDeTeste ? DanoDeKi.FracoDemais(dano) : KiContraCorpo.EhParede(razao))
			return EstourarSemFerir(p, alvo, dono);

		// O BOTAO DE DESLIGAR A DEFLEXAO NAO EXISTE AQUI -- divergencia declarada (dono, 2026-10-08) do `&&M.DRenabled`
		// que fecha os dois sorteios do DM (`objects.dm:358` e `:364`; o verb e o `DeflectionSetting` de
		// `User Interface/Settings.dm:341-348`, e a variavel nasce ligada em `Skills/Buffs/globals/Body Expansion.dm:2`).
		// MEDIDO no BYOND 516: com ela em 0, os 200 raspoes certos de 200 bolas viraram 0 e 161 delas feriram. Aqui a
		// deflexao esta sempre ligada. (Com o corte por dano logo acima -- o jogo de antes -- ela so era sorteada com
		// dano >= 10, onde a chance nao passava de ~1% pra uma ficha nova. Hoje o sorteio so deixa de fora a parede, e
		// a chance e a que a conta der: `chance% x dano final` e uma constante de quem defende -- a conta esta no
		// `DanoDeKi.CorteDoFraco`.)
		if (Sorteio(chance / 2) && alvo.Ficha.Ki >= 5)
		{
			// `M.kidefensecounter += 4` (`objects.dm:359`) -- aparar de raspao e o que mais treina
			// defesa de ki: quatro vezes o que se ganha levando o tiro na cara.
			alvo.Ficha.kidefenseskill += 0.4;
			CreditarContador(alvo, "kidefensecounter", 4);
			// O RASPAO NAO SE ESCREVE MAIS (dono, 2026-09-25: *"a informacao de dano e de block nao precisa
			// aparecer no chat"*). Ele sai como ESQUIVA no relato do golpe -- o corpo troca pelas listras do
			// Zanzoken na tela --, que e o que o DM mostrava dele: `M.dir` girado e um `step` pro lado
			// (`objects.dm:358-363`), o corpo SAINDO da linha. A fala era a unica coisa que o contava aqui.
			AnunciarGolpe(dono, alvo, new GolpeResultado { Desfecho = Desfecho.Esquivou, Membro = "" }, nivel: 1,
						  ponto: Feixe.PontoDoImpacto(p, alvo.Pos));
			return EncerrarOEncontroNoRaspao(p, alvo);
		}

		// (2026-10-12) DEFLETIR PEDE O DOBRO DO PODER DO TIRO (`KiContraCorpo.PodeDefletir`), e acaba com ele: a
		// morte do tiro logo abaixo ja era o cancelamento. O raspao de cima e outra coisa -- o corpo SAI da linha.
		if ((KiContraCorpo.RegraDeAntesDeTeste || KiContraCorpo.PodeDefletir(razao)) && Sorteio(chance) && alvo.Ficha.Ki >= 5)
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

			// A DEFLEXAO NAO SE ESCREVE MAIS, pelo mesmo pedido do raspao logo acima: quem conta que o tiro
			// foi defletido e o escudo que o cliente desenha (`Desfecho.Rebateu`). A absorcao do androide continua
			// escrita -- ela nao e dano nem bloqueio: e Ki ENTRANDO, e so a fala o mostra.
			//
			// (2026-10-12) O TIRO DEFLETIDO NAO SOME: sai de lado e vai estourar no fim do alcance, como o do deflect
			// de guarda -- a mesma funcao (`DefletirOAtaque`, `GameServer.ParryDeKi.cs`).
			Vec2 ondeDefletiu = Feixe.PontoDoImpacto(p, alvo.Pos);
			DefletirOAtaque(p, alvo, dono);
			AnunciarGolpe(dono, alvo, new GolpeResultado { Desfecho = Desfecho.Rebateu, Membro = "" }, nivel: 2, ponto: ondeDefletiu);
			return true;
		}

		// 3b) ELE NAO DEFLETIU E NAO VAI ABAIXAR A GUARDA: o feixe encontra as MAOS dele, e isso e um
		//     EMBATE e nao um tique de dano. **Novo** -- ver `TentarEmbateDeGuarda`, que conta o que a
		//     guarda fazia contra ki ate aqui, e `EmbateDeKi.PoderDeSegurar`: as maos se medem pelo `dano` ali de cima.
		//
		//     DEPOIS DO SORTEIO DE DEFLEXAO, de proposito: quem teve sorte defende de graca, como
		//     sempre teve. E so pra RAIO -- uma bola nao da tempo de agarrar nada, e no DM a disputa
		//     tambem e privilegio do `WaveAttack`.
		//
		//     O RAIO QUE ESTOURA COMO BOLA (o Death Beam) FICA DE FORA, e pelo motivo da bola: ele nao para nas maos
		//     de ninguem. A guarda so divide o dano dele -- o `cd.Bloqueando` do `DanoDeKi.Final`, ali em cima.
		//
		//     (2026-10-12) SO QUEM NAO E MAIS FRACO QUE O RAIO O SEGURA NAS MAOS. O mais fraco que defende e levado por
		//     ele, devagar, com o dano nos membros da guarda -- o passo 4 e o 5, e `KiContraCorpo`.
		//
		//     (v0.3.2) E A GENKIDAMA (`Projetil.SeguraNasMaos`): a esfera que se segura -- o Kid Boo do dono. A disputa e a mesma, e nela a
		//     bomba vale o poder de AGORA de quem a lancou: transformar-se no meio vira o cabo de guerra.
		if (((p.Tipo == TipoDeProjetil.Beam && !p.EstouraComoBola) || (p.SeguraNasMaos && !GenkidamaNaoESeguradaDeTeste))
			&& (KiContraCorpo.RegraDeAntesDeTeste || !KiContraCorpo.Arrasta(razao)) && TentarEmbateDeGuarda(p, alvo)) return true;

		// 3c) O CAMPO DE FORCA (v0.2.1) -- `if(M.hasForcefield&&isobj(M.forcefieldID)) M.forcefieldID.takeDamage(...)`
		//     (`objects.dm:421-423`): no lugar exato em que o corpo levaria o dano, quem o leva e o campo ligado. Depois
		//     da parede e dos sorteios, como la: o tiro que nao feriria (ou que foi desviado) nao gasta o campo.
		if (CampoDeForcaAbsorve(p, alvo, dono))
		{
			AnunciarGolpe(dono, alvo, new GolpeResultado { Desfecho = Desfecho.Rebateu, Membro = "" }, nivel: 2,
						  ponto: Feixe.PontoDoImpacto(p, alvo.Pos));
			return true;
		}

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
		//
		// DIVERGENCIA DECLARADA (dono, 2026-10-08): no DM o tiro de ki NAO mira. O `selectzone` do `DamageLimb` de
		// `objects.dm:439` e o do OBJETO (`:35`), que nenhum verb de ki preenche -- e o sorteio de la pode nao achar
		// membro nenhum. A historia e os numeros estao em `MeleeResolver.AplicarDanoPronto`.
		// ==================================================================================================
		// (O TIRO QUE TRAZ A PROPRIA ZONA -- o `A.selectzone` escrito pelo verb, `GodOfDestruction.dm:279` -- vence a
		// mira de quem atira: ver `ReceitaDeProjetil.ZonaCerta`.)
		//
		// (2026-10-12) O KI QUE ENCONTRA A GUARDA FERE O MEMBRO QUE A SEGURA -- braco ou perna, o rodizio do soco
		// aparado: ver `MeleeResolver.AplicarDanoProntoNaGuarda`.
		string? zonaDoTiro = p.ZonaCerta.Length > 0 ? p.ZonaCerta : dono.Combate?.ZonaMirada;
		GolpeResultado r = cd.Bloqueando && !KiContraCorpo.RegraDeAntesDeTeste
			? MeleeResolver.AplicarDanoProntoNaGuarda(cd, dano, p.Letal, _rng, zonaDoTiro, porKi: !p.Fisico)
			: MeleeResolver.AplicarDanoPronto(cd, dano, p.Letal, _rng, zonaDoTiro, porKi: !p.Fisico);
		ResolverDesfecho(dono, alvo, r);
		AoTiroRacial(p, alvo, r, dano, cd.Bloqueando);   // (v0.4.0) o Limitless Power na guarda, o Saiyan's Resolve fora dela
		// O `OnSucCollWMob(M)` DO DM (`objects.dm:440`), logo depois do `DamageLimb` do impacto que feriu: o que este
		// tiro faz A MAIS em quem acertou. Ver `DomDeProjetil`.
		if (p.Dom == DomDeProjetil.Veneno) EnvenenarG14(dono, alvo);   // o Cuspe Acido -- lote G14
		else if (p.Dom != DomDeProjetil.Nenhum) AoFerirComTiroDoKit(p, dono, alvo);
		// A FAISCA NA CABECA, E NAO NO MEIO DO FEIXE (dono, 2026-09-23). O relato do soco manda so quem bateu
		// e quem apanhou, e o cliente estoura a faisca no MEIO dos dois -- num tiro, o meio do raio inteiro.
		// O ponto do impacto viaja junto (ver `HitEvent.Ponto`).
		//
		// E O KI QUE ENCONTROU A GUARDA SAI COMO BLOQUEIO (2026-09-25). O dano ja veio dividido por ela (o
		// `cd.Bloqueando` do `DanoDeKi.Final`, logo acima), mas o relato dizia `Acertou` -- e sem a linha de
		// texto, que saiu a pedido do dono, a tela nao tinha como dizer ao jogador que a guarda estava
		// segurando. So o RELATO muda: o resultado do corpo (`r`) continua o mesmo pro resto do impacto.
		GolpeResultado relato = r;
		if (cd.Bloqueando && r.Desfecho == Desfecho.Acertou) relato.Desfecho = Desfecho.Aparou;
		AnunciarGolpe(dono, alvo, relato, nivel: 2, ponto: Feixe.PontoDoImpacto(p, alvo.Pos));

		// APANHOU COM UM RAIO NA MAO (dono, 2026-09-07): fora de disputa o raio DELE cai; dentro dela o
		// golpe pesa no medidor. Um funil so pra soco, tiro, arremesso e agarrao -- ver `GameServer.Feixe.cs`.
		AoLevarGolpeComRaioNaMao(alvo, dono);

		// 5) O EMPURRAO. HOJE E A ESCADA DO RAIO, no passo 5b logo abaixo (dono, 2026-10-12). O que este passo era,
		//    e continua sendo no jogo de antes (`KiContraCorpo.RegraDeAntesDeTeste`), tem DOIS RAMOS que se
		//    excluem -- o `if`/`else` do `Projectiles.dm:573-591`, com o corte em 4 tiles que as duas fontes
		//    escrevem igual (`beam_stun_start = 4` no DU, `maxdistance-distance <= 4` no Finale).
		//
		//    PERTO: ARREMESSA -- forca cheia ate 2 tiles, metade ate 4 (*"harder to knock back at
		//    range"*). Impulso unico, pelo funil do soco, e o corpo sai voando pra longe do feixe. E O RAMO QUE
		//    SOBROU EM JOGO, so pra bola (e pro raio que estoura como ela): o ultimo `else if` da cadeia do 5b.
		// `p.Empurra` E A PRIMEIRA PERGUNTA, e ela e nova: no DM o empurrao mora dentro de
		// `if(WaveAttack)` / `else if(kiforceful)` (`objects.dm:450-460`), e uma bola que nao e nenhum
		// dos dois **nao arremessa ninguem**. A Paralysis e exatamente esse caso, e o empurrao dela
		// estava desmentindo a propria tecnica -- quem levava o tiro ficava sem andar E sem bater, que
		// e o stun que ela existe pra nao ser. Ver `ReceitaDeProjetil.Empurra`.
		double fator = p.FatorDeEmpurrao();
		if (KiContraCorpo.RegraDeAntesDeTeste)
		{
			// O JOGO DE ANTES: perto arremessa (o corpo morto tambem voa, ver `--cadaverteste` 5), longe carrega ate
			// dez tiles da mao (`step(P,dir,32)` a cada ciclo -- `Projectiles.dm:583-591`).
			if (p.Empurra && fator > 0 && r.Dano > 0.25 && alvo.TiquesDeVoo <= 0) Arremessar(alvo, p, r.Dano * fator);
			else if (p.PodeArrastar() && PodeSerLevadoPeloFeixe(alvo)) ComecarArrasto(p, alvo);
		}
		// 5b) (2026-10-12, REGRA DO DONO) A ESCADA DO RAIO -- `KiContraCorpo`:
		//     * O MAIS FRACO E LEVADO ate o fim do alcance, de qualquer distancia, na velocidade que a diferenca de
		//       forca da; se nao defendia, fica atordoado enquanto apanha. Ele e REGADO aqui a cada ciclo de 0,2 s; o
		//       que segura o corpo entre um ciclo e outro e o `ArrastarComOFeixe`.
		//     * QUEM E MAIS FORTE fica onde esta: sem defender, anda devagar enquanto o raio bate.
		//     * A BOLA continua empurrando de perto, como sempre (o corpo morto tambem voa, ver `--cadaverteste` 5).
		else if (vaiSerLevado)
		{
			p.FatorDoArrasto = KiContraCorpo.FatorDoArrasto(razao, cd.Bloqueando);
			ComecarArrasto(p, alvo);
			if (KiContraCorpo.Atordoa(razao, cd.Bloqueando)) cd.Stun = Math.Max(cd.Stun, 2 * Projetil.SegundosPorCicloDeBeam);
		}
		else if (p.Tipo == TipoDeProjetil.Beam && !p.EstouraComoBola)
		{
			if (KiContraCorpo.Posto(razao) == PostoContraKi.MaisForte && !cd.Bloqueando) AndarDevagarSobORaio(alvo);
		}
		else if (p.Empurra && fator > 0 && r.Dano > 0.25 && alvo.TiquesDeVoo <= 0) Arremessar(alvo, p, r.Dano * fator);

		// 5c) (2026-10-12) O ARRASTO JA COMECADO OBEDECE A ESCADA. A razao e relida a cada batida, e a resposta desta
		//     batida vale tambem pra quem a cabeca JA vinha levando: o corpo que deixou de ser mais fraco que o tiro no
		//     meio do caminho -- uma forma, uma explosao de raiva -- e SOLTO aqui. Dali ele e como qualquer um que o tiro
		//     encontra de igual a quatro vezes mais forte: fica onde esta, paga a batida CHEIA (a desta ja foi) e anda
		//     devagar se nao defende (logo acima); de guarda, as maos dele seguram o raio na batida seguinte (arrastado, o
		//     `TentarEmbateDeGuarda` o recusava); a esfera grande acaba nele e estoura ali (o fim deste metodo); e a
		//     explosao do fim do alcance nao o cobra como quem vinha na ponta (o passo 7 do `AndarProjetil` le o
		//     `Arrastando`) -- so a area, se ele estiver no raio dela.
		//
		//     MEDIDO SEM ESTA LINHA (o mais fraco pego a 6 tiles por um raio de 16, saltando pra 2x e pra 3,9x o poder do
		//     tiro no tique em que o arrasto comeca): levado por mais 70 tiques e 14 tiles, com a batida cheia a cada
		//     ciclo, e a explosao inteira no fim. O `vaiSerLevado` ja dava falso; o `Arrastando` e que so era zerado por
		//     quem virava PAREDE (`EstourarSemFerir`), e o `AndarProjetil` continuava empurrando o corpo com o fator velho.
		//     As outras saidas do impacto ja soltavam: o raspao, a deflexao, a disputa.
		//
		//     (O jogo de antes tem o arrasto dele, que nao le a escada -- logo acima --, e fica de fora.)
		if (!vaiSerLevado && p.Arrastando == alvo.Id
			&& !KiContraCorpo.RegraDeAntesDeTeste && !KiContraCorpo.ArrastoSoSoltaAParedeDeTeste) p.Arrastando = 0;

		// 6) O RAIO NAO MORRE EM QUEM ACERTA: ele EMPURRA. No DM o `Bump` de um `WaveAttack` nao
		//    apaga a cabeca -- ela fica presa contra o corpo e bate de novo a cada passo do `walk`
		//    (`objects.dm:241`), com `MiniStun`. E o que faz segurar um Kamehameha em cima de
		//    alguem ser diferente de acertar uma bola nele. (La e uma batida por tique; aqui, uma a cada
		//    0,2 s e sem o `MiniStun` -- divergencia declarada em `Projetil.SegundosPorCicloDeBeam`.)
		//
		//    MENOS O RAIO QUE ESTOURA (dono, 2026-10-08, sobre o Death Beam: *"ao se chocar ele diferente dos outros
		//    beams ele explode igual um blast causando 1 hit apenas"*). O golpe ja foi dado ali em cima, UMA vez; o
		//    raio acaba aqui, inteiro e na hora (ver `Matar`), e a mao de quem atirou fica livre no mesmo tique -- o
		//    canal fecha junto e calado: nao sobrou raio pra segurar nem aluguel pra cobrar.
		if (p.EstouraComoBola)
		{
			Matar(p, FimDeProjetil.Acertou);
			if (_canais.TryGetValue(dono.Id, out CanalDeKi? canal) && canal.Raio == p) FecharCanal(dono.Id, canal, null);
			return true;
		}

		if (p.Tipo == TipoDeProjetil.Beam)
		{
			// NA FRENTE DELE, e nao em cima (dono, 2026-09-07) -- ver `PlantarACabecaNaFrenteDe`.
			PlantarACabecaNaFrenteDe(p, alvo);
			p.Encostado = true;
			p.AteMoerDeNovo = Projetil.SegundosPorCicloDeBeam;
			return true;   // a cabeca para aqui neste tique; ela nao morreu
		}

		// (2026-10-12) A ESFERA GRANDE QUE LEVA O CORPO NAO ACABA NELE: segue empurrando, bate de novo a cada ciclo
		// (o mesmo relogio do raio encostado) e estoura no fim do alcance -- ver `ExplodirNoFimDoAlcance`.
		if (p.EsferaGrande && p.Arrastando == alvo.Id && !KiContraCorpo.RegraDeAntesDeTeste)
		{
			p.Encostado = true;
			p.AteMoerDeNovo = Projetil.SegundosPorCicloDeBeam;
			return true;
		}

		// O PIERCER ATRAVESSA. Sem ele a bola morre no primeiro corpo -- que e o normal.
		if (p.Piercer) return false;

		// (a esfera grande que acaba num corpo que ela nao leva estoura ali mesmo: o chao fica marcado e quem esta em
		//  volta apanha -- menos ele, que ja levou o golpe em cheio logo acima)
		if (p.EsferaGrande && !KiContraCorpo.RegraDeAntesDeTeste) Estourar(p, alvo.Zone, Espaco.EhPlaneta(alvo.Zone), alvo.Pos, jaFerido: alvo);
		// (2026-10-13) ...e a bola comum que estoura num corpo marca o chao com a cratera pequena.
		else if (!p.Invisivel) AbrirACrateraPequena(alvo.Zone, alvo.Pos, p.Altitude);
		Matar(p, FimDeProjetil.Acertou);
		return true;
	}

	// =====================================================================
	// A ESCADA DO RAIO (dono, 2026-10-12) -- o fim do alcance e o passo lento
	// =====================================================================
	/// <summary>
	/// **A EXPLOSAO DO FIM DO ALCANCE.** *"todo ataque de ki como esfera grande e beam explodem ao chegarem na
	/// distancia Maxima, acertando ou nao o inimigo, defletidos ou nao"* -- e *"n esquece de criar uma cratera grande
	/// na explosao"* (dono, 2026-10-12). O tiro ACABA AQUI, na hora (sem o esvaziamento do raio: o estouro e o fim do
	/// ataque inteiro), e o que ele faz ao mundo e o <see cref="Estourar"/>: a cratera GRANDE, a conta inteira pra
	/// quem vinha sendo levado e o dano em area pra quem esta em volta.
	/// </summary>
	private void ExplodirNoFimDoAlcance(Projetil p, ServerPlayer? levado, ZoneKey zona, bool temChao)
	{
		Vec2 onde = levado?.Pos ?? p.Pos;
		p.Arrastando = 0;

		// NA HORA, e nao pelo `Matar`: aquele poe o raio em esvaziamento (a cauda escorre ate a cabeca por varios
		// tiques), e o estouro e o fim do ataque inteiro -- a fita some junto com o clarao.
		p.Vivo = false;
		p.Fim = FimDeProjetil.Explodiu;
		ExplosoesDoFimDoAlcanceDeTeste++;
		Estourar(p, zona, temChao, onde, levado);
	}

	/// <summary>
	/// **O ESTOURO DE UM TIRO DE KI**, onde quer que ele aconteca -- o fim do alcance do raio e da esfera grande, e a
	/// esfera grande que acaba num corpo que ela nao leva ou no cenario. Tres coisas, nesta ordem:
	///
	///   1. A CRATERA GRANDE no chao (<see cref="AbrirACrateraDaExplosao"/>);
	///   2. QUEM VINHA NA PONTA (`levado`) paga a explosao INTEIRA: *"ao atingir o limite do ataque, acontece uma
	///      explosao ao qual ai sim toma bastante dano em praticamente toda a parte externa do corpo (...) o defensor
	///      fraco q foi arrastado defendendo (...) vai tomar o dano da explosao nos membros de defesa (porem o dano vai
	///      ser concentrado nesses membros q estavam defendendo, braços ou pernas, diferente do cara q n defendeu e
	///      tomou dano alto no corpo todo, porem por ser no corpo todo o dano é diluido)"*;
	///   3. **O DANO EM AREA** -- retorno do dono do mesmo dia, sobre a primeira entrega (que so feria quem vinha na
	///      ponta): *"A explosao de ki deveria dar dano em area sim mesmo quem n foi arrastado"*. Todo corpo dentro do
	///      raio da explosao (`KiContraCorpo.TilesDaExplosao`, que cresce com a escala do tiro) leva uma fracao dela
	///      pela distancia: inteira no centro, `KiContraCorpo.FracaoNaBeiraDaExplosao` na beirada.
	///
	/// QUEM A AREA NAO PEGA: quem atirou (o `avoidusr` do `Colidiu`), quem defletiu o tiro (`Projetil.Desviador`: ele
	/// nao e alvo do que ele mesmo tirou do caminho), quem ja levou este golpe em cheio (`jaFerido`), quem esta
	/// noutra altura (`Voo.PodeAcertar`, a regra de todo tiro) e quem e PAREDE pro tiro (quatro vezes mais forte:
	/// `KiContraCorpo.EhParede`, a mesma linha que decide que o raio nao o fere).
	///
	/// NO DU o fim do arrasto so abria uma cratera pequena (`Projectiles.dm:589-591`); o estouro e o dano sao novos.
	/// </summary>
	/// <param name="levado">Quem o tiro vinha carregando (paga a explosao inteira), ou nulo.</param>
	/// <param name="jaFerido">Quem ja levou ESTE golpe em cheio e nao entra na area, ou nulo.</param>
	private void Estourar(Projetil p, ZoneKey zona, bool temChao, Vec2 onde, ServerPlayer? levado = null, ServerPlayer? jaFerido = null)
	{
		AbrirACrateraDaExplosao(zona, temChao, onde, p.Altitude, enorme: p.Colossal);

		// (2026-10-13) O CENARIO EM VOLTA APANHA JUNTO: paredes, construcoes, naves, blocos erguidos e o chao -- ver
		// `EstragarOCenarioNaExplosao`. So a explosao perto do chao: a que acontece la no alto estoura no ar.
		if (!Voo.AtravessaCenario(p.Altitude))
			EstragarOCenarioNaExplosao(zona, onde, KiContraCorpo.TilesDaExplosao(p.EscalaVisual, p.Colossal) * ZoneCollision.TileSize,
									   p.Bp, _players.GetValueOrDefault(p.Dono), colossal: p.Colossal);

		// SEM DONO NAO HA DANO: nao ha a quem creditar a derrota de ninguem (ver o comeco do `Acertar`).
		if (!_players.TryGetValue(p.Dono, out ServerPlayer? dono)) return;

		if (levado != null) FerirComAExplosao(p, dono, levado, 1);
		if (KiContraCorpo.ExplosaoSemAreaDeTeste) return;

		double raio = KiContraCorpo.TilesDaExplosao(p.EscalaVisual, p.Colossal) * ZoneCollision.TileSize;
		int andar = Voo.Andar(p.Altitude);
		// (uma COPIA da lista: a consequencia do dano -- um nocaute, uma morte -- pode mexer na da zona)
		foreach (ServerPlayer o in ZoneList(zona.Hash).ToList())
		{
			if (o == levado || o == jaFerido || o.Id == p.Dono) continue;
			if (o.Id == p.Desviador && !KiContraCorpo.EstouroPegaQuemDefletiuDeTeste) continue;
			if (!Voo.PodeAcertar(andar, Voo.Andar(o.Altitude))) continue;
			double distancia = Vec2.Distance(o.Pos, onde);
			if (distancia > raio) continue;
			if (KiContraCorpo.EhParede(KiContraCorpo.Razao(o.Ficha.expressedBP, p.Bp))) continue;
			FerirComAExplosao(p, dono, o, KiContraCorpo.FracaoDaExplosao(distancia / raio));
		}
	}

	/// <summary>
	/// UM CORPO PAGA A EXPLOSAO: `fracao` do total de <see cref="KiContraCorpo.BatidasDaExplosao"/> batidas CHEIAS do
	/// tiro (a mesma conta do `Acertar`, com a guarda de agora). Quem defende o recebe nos membros da guarda -- os
	/// dois bracos, ou as duas pernas --; quem nao defende, dividido entre todos os membros de fora do corpo. O mesmo
	/// total em menos lugares e o que quebra um braco; em todos, e o que derruba a vida inteira um pouco.
	///
	/// O CADAVER TAMBEM LEVA (dono, 2026-09-05: *"corpos mortos ainda sao corpos de personagem"*), mas so no corpo:
	/// ele nao entra em combate, nao guarda rancor e nao perde luta nenhuma -- o `EhCadaver` do `ResolverDesfecho`.
	/// </summary>
	private void FerirComAExplosao(Projetil p, ServerPlayer dono, ServerPlayer alvo, double fracao)
	{
		if (alvo.Combate is not { Intocavel: false } cd || fracao <= 0) return;

		bool defendendo = cd.Bloqueando;
		double batida = DanoDeKi.Final(p.ModsAgora(), p.BaseDano, p.MaxDano, p.Bp, cd, defendendo, p.Fisico);
		double total = DanoDeKiPelaDestruicao(alvo, dono, batida) * KiContraCorpo.BatidasDaExplosao * fracao;
		if (total <= 0) return;

		var membros = new List<BodyPart>();
		if (defendendo && MeleeResolver.EscolherGuarda(cd.Corpo, _rng) is { } guarda)
			membros.AddRange(cd.Corpo.Partes.Where(m => m.Zona == guarda.Zona && !m.Aninhado && !m.Decepado));
		if (membros.Count == 0) membros.AddRange(cd.Corpo.Partes.Where(m => !m.Aninhado && !m.Decepado));
		if (membros.Count == 0) return;

		bool cadaver = EhCadaver(alvo);
		if (!cadaver)
		{
			cd.EntrarEmCombate();
			dono.Combate?.EntrarEmCombate();
		}
		int membrosAntes = MembrosDeForaPerdidos(cd);
		foreach (BodyPart m in membros) cd.Ferir(m, total / membros.Count, p.Letal);
		cd.SincronizarVida();
		AnunciarGolpe(dono, alvo, new GolpeResultado
		{
			Desfecho = defendendo ? Desfecho.Aparou : Desfecho.Acertou, Dano = total, Membro = membros[0].Nome,
		}, nivel: 3, ponto: alvo.Pos);
		if (cadaver) return;
		AoTiroEncostar(p, dono, FatorDaLuta(dono, alvo));
		MarcarAgressao(alvo, dono);
		ConsequenciaDoDano(alvo, dono, "na explosao de ki", p.Letal, porKi: !p.Fisico, perdeuMembro: MembrosDeForaPerdidos(cd) > membrosAntes);
	}

	/// <summary>
	/// A CRATERA GRANDE DA EXPLOSAO (`Protocol.Decal.CrateraGrande`, a mesma das transformacoes de raiva) e a fumaca
	/// que sobe dela. So onde ha chao pra abrir, e so pro tiro que estourou PERTO dele: no vacuo nao ha o que marcar,
	/// e o raio disparado la do alto estoura no ar.
	/// </summary>
	private void AbrirACrateraDaExplosao(ZoneKey zona, bool temChao, Vec2 onde, float altitude, bool enorme = false)
	{
		if (!temChao || Voo.AtravessaCenario(altitude)) return;
		MandarDecalque(zona, enorme ? Protocol.Decal.CrateraEnorme : Protocol.Decal.CrateraGrande, onde, Facing.South);
		MandarDecalque(zona, Protocol.Decal.Fumaca, onde, Facing.South);
		CraterasDeExplosaoDeTeste++;
		if (!enorme) return;

		// (v0.3.0) A CRATERA ENORME LEVANTA POEIRA NA BORDA TODA, e nao so no meio: quatro nuvens em volta dela.
		float longe = 4 * ZoneCollision.TileSize;
		for (int i = 0; i < 4; i++)
		{
			double a = i * Math.PI / 2 + Math.PI / 4;
			MandarDecalque(zona, Protocol.Decal.Fumaca, new Vec2(onde.X + longe * (float)Math.Cos(a), onde.Y + longe * (float)Math.Sin(a)),
								   Facing.South);
		}
		CraterasEnormesDeTeste++;
	}

	/// <summary>(v0.3.0) Quantas crateras ENORMES (a da Genkidama) foram abertas neste processo. So pra bancada (`--v030teste`, GD).</summary>
	internal int CraterasEnormesDeTeste;

	/// <summary>
	/// **O RAIO (OU A ESFERA GRANDE) ACABOU FORA DO FIM DO ALCANCE E ESTOURA ALI** (dono, 2026-10-13: *"a explosao
	/// do beam ta sem efeito nenhum, nem mesmo a cratera"*). Tres caminhos chegam aqui: a PAREDE, o PRAZO (o raio
	/// que leva alguem devagar gasta o prazo antes do alcance -- era por isso que ele sumia calado) e a MAO QUE
	/// SOLTOU o raio que estava em cima de alguem. E o mesmo estouro do fim do alcance
	/// (<see cref="ExplodirNoFimDoAlcance"/>): quem vinha sendo levado paga a explosao inteira, quem esta em volta
	/// paga pela distancia, e o cenario do raio dela apanha junto.
	/// </summary>
	private void ExplodirOndeAcabou(Projetil p, List<ServerPlayer> corpos, ZoneKey zona, bool temChao)
	{
		ServerPlayer? levado = null;
		if (p.Arrastando != 0)
			foreach (ServerPlayer o in corpos)
				if (o.Id == p.Arrastando) { levado = o; break; }
		ExplodirNoFimDoAlcance(p, levado, zona, temChao);
	}

	/// <summary>
	/// **O ALCANCE OU O PRAZO ACABARAM, E O TIRO NAO BATEU EM NADA.** O raio e a esfera grande estouram grande
	/// (<see cref="ExplodirOndeAcabou"/>); a bola comum estoura pequeno (<see cref="EstourarABola"/>); e so o que
	/// sobra se apaga -- o alvo de treino, a lamina que ninguem ve.
	///
	/// O RAIO QUE JA ESTA SE ESVAZIANDO NAO ESTOURA: o fim dele foi decidido antes (a parede, a disputa), e o
	/// prazo so o encerra.
	/// </summary>
	private void AcabarSemBaterEmNada(Projetil p, List<ServerPlayer> corpos, ZoneKey zona, bool temChao)
	{
		if (!p.Esvaziando)
		{
			if (p.EstouraSempreQueAcaba) { ExplodirOndeAcabou(p, corpos, zona, temChao); return; }
			if (p.BolaQueEstouraAoAcabar) { EstourarABola(p, zona, temChao); return; }
		}
		Matar(p, FimDeProjetil.Apagou);
	}

	/// <summary>
	/// **A BOLA COMUM ESTOURA ONDE ESTA** -- *"quando uma esfera de ki se afasta muito ela so some e n explode"*
	/// (dono, 2026-10-13). O clarao e do cliente, pelo motivo (`FimDeProjetil.Estourou`); daqui sai a cratera
	/// pequena. Sem dano em area: esse e do raio e da esfera grande.
	/// </summary>
	private void EstourarABola(Projetil p, ZoneKey zona, bool temChao)
	{
		if (temChao) AbrirACrateraPequena(zona, p.Pos, p.Altitude);
		Matar(p, FimDeProjetil.Estourou);
		BolasEstouradasDeTeste++;
	}

	/// <summary>
	/// **O TIRO ACABOU NUMA PAREDE** (ou no que barra como ela: a construcao, a nave pousada, o bloco erguido).
	///
	///   * O RAIO E A ESFERA GRANDE ESTOURAM ALI, com tudo o que o estouro faz -- a cratera, o dano em area e o
	///     cenario em volta: *"explosoes de colisao com esferas grandes idem (...) podendo destruid varias paredes
	///     em volta da explosao e tiles de chao tb"* (dono, 2026-10-13);
	///   * A BOLA COMUM BATE NA CELULA EM QUE ACABOU, pela regra do soco (*"esferas de ki, kiai etc deveriam quebrar
	///     paredes assim como soco"*), e deixa a cratera pequena.
	///
	/// A CELULA E A DO PONTO, sem o deslocamento dos pes: e a mesma conta do `ZoneCollision.BlockedAt` que acabou de
	/// dizer que ali ha parede.
	/// </summary>
	private void AcabarNoCenario(Projetil p, List<ServerPlayer> corpos, ZoneKey zona, bool temChao, Vec2 onde)
	{
		// A PAREDE RENDE COMO UM OBJETO, e paga DEPOIS do estouro: se ele pegou alguem, o pacote do tiro ja saiu com
		// o peso dessa pessoa (`FerirComAExplosao`), e a parede nao paga de novo. Ver `GameServer.Ganhos.cs`.
		ServerPlayer? dono = _players.GetValueOrDefault(p.Dono);
		if (p.EstouraSempreQueAcaba)
		{
			ExplodirOndeAcabou(p, corpos, zona, temChao);
			AoTiroEncostar(p, dono, GainKnobs.GanhoDeObjeto);
			return;
		}

		// (a v0.2.0: a esfera grande estourava no cenario sem o clarao grande -- hoje so o botao de defeito chega aqui)
		if (p.EsferaGrande && !KiContraCorpo.RegraDeAntesDeTeste) Estourar(p, zona, temChao, onde);
		else if (!KiContraCorpo.RegraDeAntesDeTeste)
		{
			BaterNoCenarioComKi(zona, (int)MathF.Floor(onde.X / ZoneCollision.TileSize),
								(int)MathF.Floor(onde.Y / ZoneCollision.TileSize), p.Bp, dono);
			if (temChao && !p.Invisivel) AbrirACrateraPequena(zona, onde, p.Altitude);
		}
		AoTiroEncostar(p, dono, GainKnobs.GanhoDeObjeto);
		Matar(p, FimDeProjetil.Cenario);
	}

	/// <summary>Onde ha cratera pequena aberta, por zona, e ate quando ela barra a vizinha. Ver <see cref="AbrirACrateraPequena"/>.</summary>
	private readonly Dictionary<ulong, List<(Vec2 Onde, long Ate)>> _craterasPequenas = [];

	/// <summary>
	/// **A CRATERA PEQUENA DO ESTOURO DE UMA BOLA** (`Protocol.Decal.Cratera`): *"so explode se bater em paredes ou
	/// pessoas e n cria a cratera"* (dono, 2026-10-13). UMA POR LUGAR: o DM so abre a dele se nao houver outra a
	/// tres tiles (`if(!locate(/obj/Crater/destroyed) in range(3)) createCrater(loc,1)`, `objects.dm:464`) -- sem
	/// isso a rajada num muro mandaria um decalque por tiro pra zona inteira. So perto do chao, e so onde ha chao.
	/// </summary>
	private void AbrirACrateraPequena(ZoneKey zona, Vec2 onde, float altitude)
	{
		if (KiContraCorpo.BolaSemCrateraDeTeste || KiContraCorpo.RegraDeAntesDeTeste || Voo.AtravessaCenario(altitude)) return;

		long agora = NowMs();
		if (!_craterasPequenas.TryGetValue(zona.Hash, out List<(Vec2 Onde, long Ate)>? abertas))
			_craterasPequenas[zona.Hash] = abertas = [];
		abertas.RemoveAll(c => c.Ate <= agora);

		float perto = (float)(KiContraCorpo.TilesEntreCraterasPequenas * ZoneCollision.TileSize);
		foreach ((Vec2 la, long _) in abertas)
			if (Vec2.Distance(la, onde) <= perto) return;

		// (a pergunta cara -- ver `TickDosProjeteis` -- so depois do crivo barato de cima)
		if (!Espaco.EhPlaneta(zona)) return;

		abertas.Add((onde, agora + (long)(KiContraCorpo.SegundosDaCrateraPequena * 1000)));
		MandarDecalque(zona, Protocol.Decal.Cratera, onde, Facing.South);
		CraterasPequenasDeTeste++;
	}

	/// <summary>Quantas bolas comuns estouraram ao acabar (alcance ou prazo) neste processo. So pra bancada (`--v021teste`).</summary>
	internal int BolasEstouradasDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--v030teste`, familia GD): a esfera colossal prensada num muro volta a MOER como um raio (ate a vida
	/// dela acabar) em vez de estourar -- o jogo de antes. Falso em jogo, sempre.
	/// </summary>
	internal static bool ColossalMoiNoMuroDeTeste;

	/// <summary>Quantas crateras pequenas de bola foram abertas neste processo. So pra bancada (`--v021teste`).</summary>
	internal int CraterasPequenasDeTeste;

	/// <summary>
	/// Quantas explosoes GRANDES aconteceram neste processo -- as do fim do alcance e, desde 2026-10-13, as de onde
	/// quer que o raio ou a esfera grande acabem. So pra bancada (`--v020teste`, `--v021teste`).
	/// </summary>
	internal int ExplosoesDoFimDoAlcanceDeTeste;

	/// <summary>Quantas crateras grandes de explosao foram abertas neste processo. So pra bancada (`--v020teste`).</summary>
	internal int CraterasDeExplosaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--projetilteste`, familia 9): o tique em que o tiro BATE volta a nao pagar o alcance
	/// -- o arrasto como saiu na v0.2.0 (2026-10-12), em que o raio que leva alguem andava uns 20% alem do alcance
	/// dele. Lido pela propria linha do conserto (`AndarProjetil`, passo 6b). Falso em jogo, sempre.
	/// </summary>
	internal bool BatidaNaoPagaOAlcanceDeTeste;

	/// <summary>
	/// **SOB O RAIO, QUEM E MAIS FORTE ANDA DEVAGAR**: cada batida do raio renova o prazo
	/// (<see cref="KiContraCorpo.MsSobORaio"/>), e o `RecalcularVelocidade` corta o passo enquanto ele durar. A
	/// velocidade e a ficha saem NA HORA na primeira batida -- esperar o tique de 5 Hz deixaria o cliente andando no
	/// passo cheio contra um servidor que ja o conta devagar (correcao por pacote: o corpo tremendo).
	/// </summary>
	private void AndarDevagarSobORaio(ServerPlayer alvo)
	{
		long agora = NowMs();
		bool jaEstava = agora < alvo.SobORaioAteMs;
		alvo.SobORaioAteMs = agora + KiContraCorpo.MsSobORaio;
		if (jaEstava) return;
		RecalcularVelocidade(alvo);
		MandarFicha(alvo);
	}

	// =====================================================================
	// O RASPAO -- um encontro, um sorteio
	// =====================================================================
	/// <summary>
	/// O CORPO SAIU DA LINHA DO TIRO (o raspao, `objects.dm:358-363`). Devolve verdadeiro quando o tiro acabou aqui.
	///
	/// ============================ O QUE ESTAVA ERRADO (medido em 2026-10-08) ============================
	/// O raspao devolvia falso ("o tiro continua") sem mais nada. Como o corpo deste port NAO sai do lugar (dono,
	/// 2026-09-25: a esquiva e desenhada), a bola continuava a menos de 16 px dele no sub-passo seguinte e o
	/// `Acertar` rodava de novo -- credito, treino e os DOIS sorteios outra vez. Medido com o raspao certo: 4
	/// `Acertar` por bola a 16 tiles/s (9 na velocidade do DM), 60 numa teleguiada, um por tique numa bola parada, 15
	/// em 18 tiques de um raio (que ainda era picotado: o corpo "esquivado" cortava o tronco que passava por ele).
	/// Com a chance de deflexao em 20%, de 815 tiros que rasparam 658 acertaram e 156 foram defletidos logo depois;
	/// um saiu ileso. Quem "esquivava" mostrava as listras e levava o tiro 16 px adiante.
	///
	/// ============================ A REGRA: A DO DM, MEDIDA NO BYOND ============================
	/// Ver o bloco "O RASPAO ENCERRA O ENCONTRO" em `Projetil` -- um `Bump`, um sorteio, e:
	///   * a bola que ANDA acaba aqui, sem dano e sem estouro. `Apagou` e o nome que o proprio DM da a isso: o
	///     ramo do `Move()` que a tira do mundo diz *"act like distance ran out"* (`objects.dm:94`);
	///   * o raio SEGUE, e a bola parada FICA -- e nenhum dos dois enxerga este corpo de novo enquanto ele continuar
	///     em cima do tiro (`Projetil.ForaDaLinha`, esvaziada pelo <see cref="ReverQuemSaiuDaLinha"/>).
	/// A cabeca que vinha LEVANDO este corpo o solta: quem saiu da linha nao e carregado.
	/// ==========================================================================================
	/// </summary>
	private static bool EncerrarOEncontroNoRaspao(Projetil p, ServerPlayer alvo)
	{
		if (Projetil.RaspaoSorteiaDeNovoDeTeste) return false;

		if (p.AcabaNoRaspao)
		{
			Matar(p, FimDeProjetil.Apagou);
			return true;
		}

		p.TirarDaLinha(alvo.Id);
		if (p.Arrastando == alvo.Id) p.Arrastando = 0;
		return false;
	}

	/// <summary>
	/// QUEM RASPOU E JA NAO ESTA EM CIMA DO TIRO VOLTA A SER ALVO DELE -- encostar de novo e outro encontro, com
	/// outro sorteio. A regua e a <see cref="Feixe.EmCimaDoTiro"/>; quem sumiu da zona sai da lista junto.
	///
	/// A BOLA QUE PASSOU A ANDAR ESQUECE TODO MUNDO: a memoria e de quem esbarrou nela PARADA (a espera da Hellzone,
	/// a mina). Quando ela parte, o passo e dela -- e no DM esse `Bump` e novo e a tira do mundo (`objects.dm:92-99`).
	///
	/// CUSTO: so roda pro tiro que tem alguem na lista, e a lista so nasce num raspao.
	/// </summary>
	private static void ReverQuemSaiuDaLinha(Projetil p, List<ServerPlayer> corpos)
	{
		List<int> fora = p.ForaDaLinha!;
		for (int i = fora.Count - 1; i >= 0; i--)
		{
			// QUEM SAIU DE LADO DA DISPUTA DE GUARDA e esquecido pelo PRAZO dele, e nao por esta regua: o raio que a
			// guarda vinha empurrando de volta ainda nao chegou nele, e "nao esta em cima" o devolveria a linha antes
			// de a cabeca passar (`Projetil.SaiuDeLado`; quem desconta o prazo e o passo 0-pre do `AndarProjetil`).
			if (fora[i] == p.SaiuDeLado && !KiContraCorpo.SaidaDeLadoPelaReguaDeTeste) continue;

			ServerPlayer? corpo = null;
			foreach (ServerPlayer o in corpos)
				if (o.Id == fora[i]) { corpo = o; break; }

			if (corpo == null || p.AcabaNoRaspao || !Feixe.EmCimaDoTiro(p, corpo.Pos, corpo.Altitude)) fora.RemoveAt(i);
		}
		if (fora.Count == 0) p.ForaDaLinha = null;
	}

	// =====================================================================
	// A PAREDE -- o impacto que nao e golpe (ate 2026-10-12, o corte dos fracos)
	// =====================================================================
	/// <summary>
	/// O TIRO ENCOSTOU EM QUEM E PAREDE PRA ELE, e nao o fere -- quatro vezes o poder do tiro, ou mais
	/// (`KiContraCorpo.EhParede`; o dono, 2026-10-12: *"nem precisa defender o ataque de ki, ao bater nele vai ter o
	/// efeito de colisao, e o defensor vai poder andar livremente como se ele fosse uma parede imparavel"*). Devolve
	/// verdadeiro: o tiro acabou, ou parou, neste corpo.
	///
	/// O RAMO E O DO CORTE DOS FRACOS DO DM (`objects.dm:355-357`: o impacto de dano final abaixo de 10, ver
	/// `DanoDeKi.CorteDoFraco`), que foi quem o trouxe pra ca em 2026-10-08. O QUE ELE FAZ nao mudou, e e o que esta
	/// medido abaixo; mudou QUEM cai nele -- no jogo de antes (`KiContraCorpo.RegraDeAntesDeTeste`) ainda e o dano.
	///
	/// ============================ O QUE O DM FAZ, MEDIDO NO BYOND 516 (2026-10-08) ============================
	///   * A BOLA sai do mundo no proprio `Bump`: `explodeme = 1` e, no fim dele, `if(!WaveAttack) if(explodeme)
	///     explode()` (`:565-566`). Andando, parada ou `piercer`, tanto faz -- o `density = 0` que deixa a perfurante
	///     passar mora DENTRO do ramo do dano (`:479-481`), e este tiro nao chega la. (Medido tambem com o corpo
	///     andando pra cima de uma bola parada: `mobBump.dm:57-58` chama o `Bump` dela, e ela some.)
	///   * A CABECA DO RAIO fica: o `stoopme = 1` a tira do mundo no `Move()` seguinte (`:149-154`), o segmento de
	///     tras vira cabeca no `KHH()` (`:230-241`), anda e bate de novo. Visto de fora, o raio continua PARADO NA
	///     FRENTE do corpo, batendo sem ferir, e o corpo anda raio adentro se quiser.
	///   * E MAIS NADA: sem `DamageLimb`, sem `Knockback`, sem `MiniStun`, sem `Add_Anger`, sem sorteio.
	/// ===========================================================================================================
	///
	/// ============================ COMO ISSO FICA NUM FEIXE QUE E UM OBJETO SO ============================
	/// A cabeca e PLANTADA na frente do corpo, como no golpe (`PlantarACabecaNaFrenteDe`), e o relogio do `Encostado`
	/// passa a contar a cadencia medida da batida que nao fere (`Projetil.SegundosPorBatidaSemFerir`): vencido o
	/// ciclo ela testa de novo -- se o corpo saiu, segue viagem; se continua ali, outra batida. Entre uma batida e
	/// outra ela lembra em quem esta batendo (`Projetil.BatendoSemFerir`) e RECUA na frente dele se ele avancar
	/// (ver `RecuarNaFrenteDeQuemAvanca`).
	///
	/// QUEM VINHA SENDO LEVADO E SOLTO: sem golpe nao ha empurrao. E o corpo que CRESCEU no meio do raio -- uma forma,
	/// uma explosao de raiva -- ate virar parede pra ele: para de ser carregado na batida seguinte, e a cabeca fica
	/// batendo nele sem ferir (`--projetilteste` 16i). (Quem cresce so ate deixar de ser mais fraco, sem chegar a
	/// parede, tambem e solto, mas nao por aqui: e o passo 5c do `Acertar`, e ele continua apanhando.) No jogo de
	/// antes era a batida em que o dano caia abaixo do corte -- o corpo que cresceu, ou o raio que enfraqueceu no
	/// caminho (`rangemod`) --, e o arrasto era a metade "longe" do empurrao (`objects.dm:450-457` e o DU).
	///
	/// O RAIO QUE ESTOURA COMO BOLA (o Death Beam, `Projetil.EstouraComoBola`) estoura: e a regra dele em todo
	/// impacto, e o canal fecha junto, calado, como no golpe.
	///
	/// A ESFERA GRANDE (a Death Ball, a Genkidama: `Projetil.EsferaGrande`) ESTOURA DE VERDADE -- regra do dono
	/// (2026-10-12), e nao do DM: ela acaba aqui como toda bola, mas com a cratera grande e o dano em area do
	/// `Estourar`. Quem a parou nao sente nada; quem esta em volta e nao e parede pra ela, sim.
	/// =======================================================================================================
	///
	/// ============================ O DESENHO: ESTOURA, E O CORPO NAO REAGE ============================
	/// O motivo da morte e `Acertou` -- o estouro de sempre, no ponto do impacto -- e NAO sai relato de golpe
	/// (`AnunciarGolpe`): sem clarao, sem faisca e sem ferida em quem levou. E a intencao escrita do DM
	/// (`explodeme`). La, medido, nem o estouro aparece (o `explode()` chama `spawnExplosion` com a `loc` ja nula),
	/// e isso vale pra tiro nenhum, forte ou fraco; aqui todo tiro que encosta num corpo estoura, e este tambem.
	/// (2026-10-12) O RAIO PARADO NUMA PAREDE ACENDE A ESTRELA DA COLISAO, so do lado do ataque: quem conta isso ao
	/// cliente e a <see cref="PrensadoContraUmCorpo"/>, logo abaixo.
	/// ====================================================================================================
	/// </summary>
	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): a estrela so acende em DISPUTA -- o jogo de antes de 2026-10-12, em
	/// que o raio empurrando a guarda de um mais fraco, ou parado em quem e parede, nao mostrava colisao nenhuma.
	/// Lido pela propria <see cref="PrensadoContraUmCorpo"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool EstrelaSoNaDisputaDeTeste;

	/// <summary>
	/// ESTE RAIO ESTA PRENSADO CONTRA UM CORPO? -- o dono (2026-10-12): *"ao usar a guarda pra bloquear um ataque de
	/// ki, o efeito da colisao de ki vai começar porem ja q so tem 1 ataque de ki colidindo a estrela da colisao vai
	/// ser so de um lado (o lado do ataque)"*, e *"se ele for mt mais forte ele nem precisa defender o ataque de ki
	/// ao bater nele vai ter o efeito de colisao"*.
	///
	/// A guarda de quem AGUENTA o raio ja abria disputa (`TentarEmbateDeGuarda`, que liga o `EmEmbate`). Faltavam os
	/// dois casos que a escada por forca criou, e os dois ja tem estado -- nada novo e guardado:
	///   * A GUARDA DO MAIS FRACO: o raio o leva (`Arrastando`) com a guarda erguida;
	///   * A PAREDE: a cabeca plantada em quem ela nao fere (`BatendoSemFerir`).
	/// O bit que viaja e o mesmo da disputa (`ProjetilState.Prensado`); o cliente desenha a estrela inteira quando
	/// ha DUAS pontas prensadas se encontrando, e so a metade do lado do ataque quando ha uma (`World.TickDosChoquesDeKi`).
	/// </summary>
	private bool PrensadoContraUmCorpo(Projetil p)
	{
		if (EstrelaSoNaDisputaDeTeste || KiContraCorpo.RegraDeAntesDeTeste || p.Tipo != TipoDeProjetil.Beam) return false;
		if (p.BatendoSemFerir != 0) return true;
		return p.Arrastando != 0 && _players.TryGetValue(p.Arrastando, out ServerPlayer? levado)
			   && levado.Combate?.Bloqueando == true;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`, 9j): a esfera grande que bate em quem e parede pra ela so SOME, sem
	/// cratera e sem dano em volta -- o buraco medido em 2026-10-12, antes de o dono fechar a regra (*"Estoura ali"*).
	/// Lido pela propria linha do estouro, no <see cref="EstourarSemFerir"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool EsferaSomeNaParedeDeTeste;

	private bool EstourarSemFerir(Projetil p, ServerPlayer alvo, ServerPlayer dono)
	{
		if (p.Arrastando == alvo.Id) p.Arrastando = 0;

		if (p.EstouraComoBola)
		{
			Matar(p, FimDeProjetil.Acertou);
			if (_canais.TryGetValue(dono.Id, out CanalDeKi? canal) && canal.Raio == p) FecharCanal(dono.Id, canal, null);
			return true;
		}

		if (p.Tipo == TipoDeProjetil.Beam)
		{
			PlantarACabecaNaFrenteDe(p, alvo);
			p.Encostado = true;
			p.AteMoerDeNovo = Projetil.SegundosPorBatidaSemFerir;
			p.BatendoSemFerir = alvo.Id;
			return true;   // a cabeca para aqui neste tique; ela nao morreu
		}

		// (2026-10-12, REGRA DO DONO) A ESFERA GRANDE ESTOURA TAMBEM EM QUEM E PAREDE PRA ELA -- *"todo ataque de ki como
		// esfera grande e beam explodem (...) acertando ou nao o inimigo"*, e, perguntado se ela estoura ali ou some:
		// *"Estoura ali"*. Ate esse dia ela so sumia: as chamadas do estouro moram no caminho de quem foi ferido (o fim
		// do `Acertar`), e este ramo devolve antes delas. A PAREDE NAO SENTE -- a area do `Estourar` a pula pela mesma
		// regua que a trouxe pra ca (`KiContraCorpo.EhParede`) --, mas o chao ganha a cratera e quem esta em volta
		// apanha. No jogo de antes nenhuma esfera estourava, e la quem cai neste ramo e o corte por dano.
		if (p.EsferaGrande && !KiContraCorpo.RegraDeAntesDeTeste && !EsferaSomeNaParedeDeTeste)
			Estourar(p, alvo.Zone, Espaco.EhPlaneta(alvo.Zone), alvo.Pos);
		Matar(p, FimDeProjetil.Acertou);
		return true;
	}

	/// <summary>
	/// O CORPO QUE O RAIO NAO FERE ANDOU CONTRA ELE: a cabeca recua e continua NA FRENTE do corpo.
	///
	/// ============================ POR QUE ISTO PRECISA EXISTIR ============================
	/// Uma cabeca `Encostado` fica parada ate o ciclo vencer (passo 4 do `AndarProjetil`). No golpe o ciclo e curto
	/// (0,2 s), e quem leva o raio e levado, atordoado, ou anda devagar sob ele (`AndarDevagarSobORaio`) com a cabeca
	/// replantada a cada batida. No ramo da parede (`EstourarSemFerir`; ate 2026-10-12, o do corte dos fracos) andar
	/// contra o raio E a cena -- *"they should be able to walk through beams"* (`objects.dm:451`) e, do dono, *"andar
	/// livremente como se ele fosse uma parede imparavel"* --, e o ciclo e mais longo
	/// (`Projetil.SegundosPorBatidaSemFerir`): sem este passo o corpo entrava ate 48 px na cabeca parada (a foto
	/// que o dono reprovou em 2026-09-07, a cabeca EM CIMA de quem ela acerta), e quem andasse mais depressa passava
	/// dela, cortava o tronco e soltava um toco de feixe pelas costas.
	///
	/// NO DM a cabeca e sempre o segmento COLADO no corpo: cada passo dele contra a cabeca densa e um `Bump` que a
	/// tira do mundo (`mobBump.dm:57-58`), cada passo sobre um segmento de tronco o apaga e promove o de tras
	/// (`Crossed`, `objects.dm:156-171`). Medido: quatro tiles raio adentro em ~20 tiques, ileso.
	/// DIVERGENCIA DECLARADA: la cada um desses passos e mais uma batida (e mais um `kidefensecounter`: 12 batidas
	/// em 40 tiques andando, contra 7 parado); aqui o corpo anda em pixels e a batida continua sendo a do ciclo.
	/// ====================================================================================
	///
	/// SO RECUA, e so pra quem continua na faixa da cabeca (`Feixe.EncostaNoCorpo`): quem saiu de lado, voou pra
	/// fora ou se afastou e assunto do ciclo, que solta a cabeca pra andar; quem pulou pra dentro do tronco e
	/// assunto do corte (`CortarOndeEncostaram`). O ALCANCE DEVOLVE o que a cabeca recuou, como no corte:
	/// `AndouTiles` e "a que distancia da mao a cabeca esta", e e ele que decide o `mods` de quem tem `rangemod`.
	/// </summary>
	private static void RecuarNaFrenteDeQuemAvanca(Projetil p, List<ServerPlayer> corpos)
	{
		if (p.BatendoSemFerir == 0 || Feixe.CabecaNaoRecuaDeTeste) return;

		ServerPlayer? corpo = null;
		foreach (ServerPlayer o in corpos)
			if (o.Id == p.BatendoSemFerir) { corpo = o; break; }
		if (corpo == null) { p.BatendoSemFerir = 0; return; }

		if (!Voo.PodeAcertar(Voo.Andar(p.Altitude), Voo.Andar(corpo.Altitude))) return;
		if (!Feixe.EncostaNoCorpo(p, corpo.Pos, corpo.Altitude)) return;

		Vec2 naFrente = Feixe.CabecaNaFrenteDe(corpo.Pos, corpo.Altitude, p);
		Vec2 recuo = p.Pos - naFrente;
		float quanto = recuo.X * p.Rumo.X + recuo.Y * p.Rumo.Y;
		if (quanto <= 0.01f) return;

		p.Pos = naFrente;
		p.Distancia += quanto / ZoneCollision.TileSize;
	}

	/// <summary>
	/// `Knockback(M, kbstr)` (`objects.dm:663`): `kbdur = min(strength, 10)` tiques, `kbdir` e a
	/// direcao do TIRO -- quem leva raio vai pro lado pra onde o raio ia, e nao pra longe de quem
	/// atirou. Reusa o mesmo arremesso do soco pesado (`TickDoEmpurrao`).
	/// </summary>
	private void Arremessar(ServerPlayer alvo, Projetil p, double forca)
	{
		if (alvo.Combate is { Inabalavel: true }) return;   // (v0.4.0) sem recuo -- Unstoppable Force, Primal Bloodlust
		// (2026-10-18) O MAJIN DESFEITO NAO VOA, pelo tiro tambem -- a razao inteira esta no `Arremessar` do `GameServer.Empurrao.cs`.
		if (alvo.Combate is { Desfeito: true } && !DesfeitoVoaDeTeste) return;
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
	/// ESTE CORPO PODE SER LEVADO POR UM FEIXE? -- pelo que o CORPO e e esta fazendo agora. Quem o raio leva pela
	/// FORCA e outra pergunta, feita antes desta no `Acertar`: so o mais fraco que ele (`KiContraCorpo.Arrasta`,
	/// dono 2026-10-12).
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
	///     chegou primeiro e ele tem prazo proprio; o feixe espera o corpo pousar. Desde 2026-10-12 o
	///     raio comum nao arremessa ninguem, e esse voo vem de fora dele -- um soco, uma bola, outro
	///     tiro. (No jogo de antes o arremesso do proprio feixe so nascia a menos de 4 tiles, onde o
	///     arrasto nem comecava: era um "quase nunca", que e exatamente o intervalo em que esse tipo de
	///     defeito mora.)
	/// ==========================================================================================================
	/// </summary>
	private bool PodeSerLevadoPeloFeixe(ServerPlayer alvo)
		=> alvo.Combate is not { Inabalavel: true }   // (v0.4.0) sem recuo: o feixe nao o arrasta (Unstoppable Force, Primal Bloodlust)
		   // (2026-10-18) ...nem o Majin desfeito em pedacos: o feixe passa pelo lugar dele sem levar nada (ver o `Arremessar`).
		   && (alvo.Combate is not { Desfeito: true } || DesfeitoVoaDeTeste)
		   && alvo.TiquesDeVoo <= 0
		   // QUEM ESTA NUMA DISPUTA DE KI NAO E LEVADO (2026-10-07): o feixe de um terceiro pesa no medidor dele
		   // (`AoLevarGolpeComRaioNaMao`), mas nao tira do lugar um corpo que a disputa plantou. Mesma regra, e
		   // mesmo motivo, do `Arremessar`.
		   && !_emEmbateDeKi.ContainsKey(alvo.Id)
		   && _players.ContainsKey(alvo.Id);   // vivo ou morto: o feixe carrega o corpo que esta na frente

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
	/// QUEM VEM PARAR AQUI, E ATE ONDE VAI (dono, 2026-10-12 -- `KiContraCorpo`): o mais fraco que o tiro, pego de
	/// qualquer distancia (`Acertar`, passo 5b), na velocidade que a diferenca de forca da (`Projetil.FatorDoArrasto`),
	/// ate o FIM DO ALCANCE -- e quem o solta la e o estouro (`ExplodirNoFimDoAlcance`), nao este metodo. ISSO
	/// ENQUANTO ELE CONTINUAR MAIS FRACO: a razao e relida a cada batida, e quem deixa de ser e solto antes -- virou
	/// parede, pelo `EstourarSemFerir`; ficou de igual a quatro vezes, pelo passo 5c do `Acertar`. No jogo de antes
	/// o arrasto so comecava alem de 4 tiles e largava o corpo aos 10 da mao, com uma cratera: e o ramo do
	/// `KiContraCorpo.RegraDeAntesDeTeste` la embaixo.
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
	/// embate e com a cabeca andando. Morreu, foi defletido, esbarrou num muro, entrou numa disputa, o
	/// dono soltou, ou o alcance acabou e ele estourou (2026-10-12) -- em todos, a rega para e o prazo
	/// escorre no <c>TickDoEmpurrao</c>.
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
	/// E acima do limiar de voo o modo e `PorCima` -- `Voo.ModoNaAltura`, a mesma linha que o `Input`
	/// escreve. Um feixe disparado por cima do muro leva a vitima por cima do muro; contra a parede de
	/// PREDIO ela fica prensada (2026-10-08: nao se voa por cima de predio, ver `ClasseDePredio`).
	/// ==============================================================================================
	/// </summary>
	private bool ArrastarComOFeixe(Projetil p, ServerPlayer alvo, Vec2 delta,
								   ZoneCollision? mapa, ZoneKey zona, bool temChao)
	{
		// ELE AINDA PODE SER LEVADO? Morreu, foi arremessado por outra coisa ou saiu do jogo no meio
		// do tique -- larga, e a cabeca segue viagem (nao e "prensado", entao devolve verdadeiro).
		if (!PodeSerLevadoPeloFeixe(alvo)) { p.Arrastando = 0; return true; }

		// O FIM DA CORDA DO JOGO DE ANTES (so com o `KiContraCorpo.RegraDeAntesDeTeste`): dez tiles da mao do
		// dono. `if(getdist(Owner,P)==10) BigCrater(...)` (`Projectiles.dm:589`) -- o DU larga o corpo ali e abre
		// uma cratera onde ele parou. O feixe nao morre junto: ele continua andando sozinho, so nao leva mais ninguem.
		// HOJE (2026-10-12) A CORDA E O ALCANCE DO TIRO: ele leva o corpo ate la, e la estoura -- quem solta o corpo
		// e o `ExplodirNoFimDoAlcance`, e esta funcao nao larga ninguem pela distancia.
		if (KiContraCorpo.RegraDeAntesDeTeste && p.AndouTiles >= Projetil.TilesDeArrasto)
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

		// PAREDE E AGUA -- ver a decisao 2 e a 3 no cabecalho. Acima do cenario o modo vira `PorCima`
		// (`Voo.ModoNaAltura`, a mesma linha do passo): o corpo arrastado passa por cima do muro solto e
		// para na parede de predio.
		if (mapa != null && MoveRules.Occupied(mapa, destino, Voo.ModoNaAltura(alvo.Altitude, ModoDeTravessiaDe(alvo)))) return false;

		alvo.Pos = destino;

		// A REGA. Enquanto ela acontece o corpo e do feixe; parando, ele se solta sozinho.
		alvo.ArrastoRestante = Empurrao.SegundosPorTique;

		// O SULCO NO CHAO **NAO** SAI DAQUI, e isso e deliberado: a cabeca do feixe ja esta carimbando
		// a mesma fileira de celulas neste mesmo sub-passo (`MarcarSulcoDoTiro`), e o corpo levado anda
		// coladinho nela. Duas marcas por celula leem como MANCHA e nao como rastro -- e a guarda de
		// distancia do `CarimbarSulco` e por dono de rastro, entao ela nao pegaria a segunda. E o mesmo
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
		// A AGUA NAO RECEBE TERRA REVIRADA -- e a pergunta e feita MARCA A MARCA, e nao so na cabeca do
		// raio: as marcas nascem interpoladas sobre a reta (`CarimbarSulco`), e um tique de 107 px
		// pode entrar e sair de um lago sem a cabeca parar dentro dele.
		bool naAgua = mapa != null && mapa.EhAguaEm(p.Pos);

		// A CELULA E A DA CABECA, e nao "a dos pes" do corpo: um raio nao tem pes. A cabeca e o
		// unico ponto denso dele (ver `Projetil.Pos`), e e ela que encosta no que estiver no caminho.
		//
		// A DIRECAO E O RUMO DO PROPRIO TIRO -- a mesma escolha do arremesso, que carimba pelo
		// `RumoDoVoo` e nao pelo `Facing`: o sulco e a marca do que PASSOU ali.
		if (naAgua && p.UltimoSulco.X == 0f && p.UltimoSulco.Y == 0f) return;   // nasceu na agua: sem rastro ate pisar terra
		CarimbarSulco(zona, Protocol.Decal.Sulco, p.Pos,
					  MoveRules.FacingFrom(p.Rumo, Facing.South), ref p.UltimoSulco,
					  mapa == null ? null : v => !mapa.EhAguaEm(v));
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
	///
	/// O RAIO QUE ESTOUROU NUM CORPO SOME DE UMA VEZ (<see cref="Projetil.EstouraComoBola"/>, o Death Beam): o
	/// estouro tem que nascer NO IMPACTO, e quem o leva pra tela e o `Morreu` -- que so sai quando o tiro deixa a
	/// lista. Engolindo o rastro primeiro, a explosao apareceria depois de o fio ja ter sumido. Pelos outros
	/// motivos (parede, alcance, a mao que soltou) ele se esvazia como qualquer raio.
	/// </summary>
	private static void Matar(Projetil p, FimDeProjetil porque)
	{
		if (!p.Vivo) return;

		bool estourou = porque == FimDeProjetil.Acertou && p.EstouraComoBola;
		if (p.Tipo == TipoDeProjetil.Beam && !estourou && !p.Esvaziando && p.Comprimento > Projetil.RaioDeImpacto)
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
	/// NASCEU / MORREU pra zona inteira, no canal CONFIAVEL. Ver <see cref="Protocol.S2C.Projetil"/>
	/// sobre por que estes dois momentos nao viajam junto da posicao.
	/// </summary>
	private void AnunciarProjetil(ulong hash, Protocol.ProjetilSub sub, Projetil p, bool naMao = false)
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
			// A ESCALA DE NASCENCA E ESTA, a que acabou de viajar -- escrita AQUI, e nao por cada um que faz um
			// tiro (o disparo, o corte), pra o servidor e o cliente nunca discordarem do ponto de
			// partida: dali em diante o snapshot so carrega a escala de quem saiu dela (`ProjetilState.Escala`).
			p.EscalaDeNascenca = p.EscalaVisual;

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
			// AS FLAGS VAO NO FIM (crescer pelo fim e o que mantem escritor e leitor faceis de conferir
			// lado a lado): bit 0 = nasceu invisivel (a lamina de ar do Kiai). Ver `Projetil.Invisivel`.
			// (v0.3.1) bit 1 = nasceu NA MAO (a Genkidama se formando): ver o `naMao` do `Disparar`.
			// (2026-10-19) bit 2 = o raio do Shattering the Limit, que o cliente pinta de vermelho: ver `Projetil.DoLimite`.
			w.Put((byte)((p.Invisivel ? 1 : 0) | (naMao ? 2 : 0) | (p.DoLimite ? 4 : 0)));
			// ...E DEPOIS DELAS, O SOM (2026-10-13): com que arquivo o tiro sai. Ver `SomDeKi`.
			w.Put((byte)p.Som);
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
	/// rapido tem sete sub-passos, e o teto da zona sao 256 tiros.
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
