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
/// As ~33 tecnicas ja portadas sao TODAS instantaneas: `AlvoDeTecnicaG3` escolhe alguem por raio e
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

		public bool Atirando => CargaRestante <= 0;
	}

	/// <summary>Este corpo esta preso carregando ou segurando um raio? Ver <see cref="PodeMexerOCorpo"/>.</summary>
	public bool EnraizadoPorKi(int id) => _canais.ContainsKey(id);

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
	private static void RegistrarTecnicasDeProjetil()
	{
		Tecnicas.Registrar("Ki_Wave", "Onda de Ki", Modo.Sustentada,
			"Concentra Ki na mao e solta um RAIO continuo. Aperte uma vez pra carregar, de novo pra "
			+ "soltar, e de novo pra parar. Enquanto carrega e enquanto atira voce fica PLANTADO no "
			+ "lugar -- e o preco de um raio, e e por isso que ele e a arma de quem ja ganhou espaco.");

		Tecnicas.Registrar("Basic_Blast", "Bola de Ki", Modo.Instantanea,
			"Uma esfera de energia solta na direcao em que voce olha. Sai na hora, nao prende voce e "
			+ "custa pouco -- em troca voa devagar e perde forca contra quem sabe se defender de Ki.");

		Tecnicas.Registrar("Guided_Ball", "Esfera Teleguiada", Modo.Instantanea,
			"Uma esfera pesada que PERSEGUE o alvo marcado ate acerta-lo ou se apagar. Custa caro e "
			+ "e lenta, mas nao adianta sair da frente. Sem alvo marcado, ela voa reto e voce e "
			+ "avisado disso.");
	}

	/// <summary>
	/// O despacho dos tres. Chamado de dentro do `switch` do <see cref="UsarTecnica"/>, DEPOIS do
	/// gate de "voce sabe esta tecnica?" -- por isso nenhum dos tres o repete.
	/// </summary>
	private void UsarTecnicasDeProjetil(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case "Ki_Wave": KiWave(pl); break;
			case "Basic_Blast": BasicBlast(pl); break;
			case "Guided_Ball": GuidedBall(pl); break;
		}
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
		long agora = NowMs();
		if (_blastPronto.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{
			Avisar(pl, "sua mao ainda esta juntando energia.");
			return;
		}

		double custo = 10 * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		// `reload = Eactspeed/5`, com piso de 3 tiques -- e o `Eactspeed` cai quando se carrega Ki,
		// entao carregar poder tambem faz atirar mais rapido. Mesma promessa da cadencia do soco.
		double tiques = Math.Max(pl.Ficha.Eactspeed / 5, 3);
		_blastPronto[pl.Id] = agora + (long)(tiques * 100);

		pl.Ficha.Ki -= custo;
		pl.Ficha.BlastGain(_rng);
		pl.Ficha.blastskill += 0.05;

		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = pl.Ficha.Ekioff * Math.Log(Math.Max(pl.Ficha.blastskill, 10)) / Math.Log(10),
			Velocidade = 1,
			AlcanceTiles = 30,
			Nome = "Bola de Ki",
		});
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
		});
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
	private Projetil Disparar(ServerPlayer pl, ReceitaDeProjetil r,
							  Vec2? rumoDado = null, Vec2? deOnde = null)
	{
		Vec2 rumo = rumoDado ?? MeleeArea.Frente(pl.Facing);
		Vec2 berco = deOnde ?? pl.Pos;

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
			Altitude = pl.Altitude,
			Nome = r.Nome,
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
				Projetil raio = Disparar(pl, c.Receita);
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

		foreach ((ulong zona, List<Projetil> lista) in _projeteis)
		{
			if (lista.Count == 0) continue;

			List<ServerPlayer> corpos = ZoneList(zona);
			ZoneCollision? mapa = null;
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
						mapa = MapaDaZonaDoHash(zona);
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

					AndarProjetil(p, dt, corpos, mapa);
				}

				if (p.Vivo) continue;

				lista.RemoveAt(i);
				_projeteisVivos--;
				AnunciarProjetil(zona, Protocol.ProjetilSub.Morreu, p);
			}
		}
	}

	/// <summary>Um passo de um tiro: prazo, rastro, perseguicao, avanco e o que ele encontrou.</summary>
	private void AndarProjetil(Projetil p, double dt, List<ServerPlayer> corpos, ZoneCollision? mapa)
	{
		ServerPlayer? dono = _players.GetValueOrDefault(p.Dono);

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
			if (p.Canalizando && dono != null) p.Cauda = dono.Pos;
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
			if (p.Canalizando && dono != null) p.Cauda = dono.Pos;

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

		// 4) O RAIO ENCOSTADO nao anda: ele MOI. `Bump` nao apaga a cabeca -- ela fica empurrando, e
		//    a cada ciclo de 0,2 s (o `sleep(2)` do `ShootBeam`) um segmento novo bate de novo.
		if (p.Encostado)
		{
			p.AteMoerDeNovo -= dt;
			if (p.AteMoerDeNovo > 0) return;
			p.AteMoerDeNovo = Projetil.SegundosPorCicloDeBeam;
			p.Encostado = false;   // volta a testar: se o alvo saiu, a cabeca segue viagem
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

			p.Pos = nova;

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
		if (!alvo.Ficha.KO) alvo.Ficha.kidefenseskill += 0.1;

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
			// `M.kidefensecounter += 4` -- aparar de raspao e o que mais treina defesa de ki.
			alvo.Ficha.kidefenseskill += 0.4;
			Avisar(alvo, $"voce desvia {p.Nome} de raspao.");
			return false;   // o tiro CONTINUA: foi o corpo que saiu da linha
		}

		if (Sorteio(chance) && alvo.Ficha.Ki >= 5)
		{
			alvo.Ficha.kidefenseskill += 0.1;
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

		// 5) O EMPURRAO, e ele so vale DE PERTO -- `maxdistance - distance <= 2` com forca cheia,
		//    ate 4 tiles com metade (*"harder to knock back at range"*).
		double fator = p.FatorDeEmpurrao();
		if (fator > 0 && r.Dano > 0.25 && alvo.TiquesDeVoo <= 0 && !alvo.Ficha.dead)
			Arremessar(alvo, p, r.Dano * fator);

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
		alvo.TiquesDeVoo = tiques;
		alvo.TiquesIniciaisDoVoo = tiques;
		alvo.RumoDoVoo = p.Rumo;
		alvo.ForcaDoVoo = p.Bp;
		alvo.VooNoTique = 0;
		alvo.UltimoSulco = default;
		MarcarSulco(alvo, Protocol.Decal.SulcoPonta);
		MandarFicha(alvo);
	}

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
	private void EscreverProjeteis(NetDataWriter w, ulong hash)
	{
		if (!_projeteis.TryGetValue(hash, out List<Projetil>? l) || l.Count == 0)
		{
			w.Put((ushort)0);
			return;
		}

		w.Put((ushort)l.Count);
		foreach (Projetil p in l)
			new ProjetilState
			{
				Id = p.Id, Pos = p.Pos, Tipo = (byte)p.Tipo, Cauda = p.Cauda,
			}.Write(w);
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
	/// </summary>
	private ZoneCollision? MapaDaZonaDoHash(ulong hash)
	{
		List<ServerPlayer> l = ZoneList(hash);
		return l.Count == 0 ? null : MapaDaZonaOuCatalogo(l[0].Zone);
	}
}
