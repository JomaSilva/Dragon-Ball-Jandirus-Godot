using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS SALAS DO TEMPO DOS MUNDOS SORTEADOS -- a porta na montanha, a sessao sem guardiao e a recarga que vale
/// pra todas. As regras puras (quais mundos tem sala, a dilatacao, a celula da porta) moram no Core:
/// `Core/World/SalaDePlaneta.cs`.
///
/// ============================ O PEDIDO DO DONO (2026-10-11), E COMO CADA FRASE FOI LIDA ============================
/// *"essas salas do tempo n precisam de guardiao entao se ficar nelas o dobro do tempo da dilatação delas de uma
/// vez (sala do tempo de 1 ano vc fica 2 anos la dentro, sala do tempo de 5 anos vc fica 10 etc) vc fica preso
/// la dentro, obrigando vc a sair e sofrendo um cooldown igual é na terra, mas diretamente proporcional a
/// dilatação do tempo nessa sala do tempo, e o cooldown de uma sala do tempo compsrtilha com outras, ou seja,
/// ate o cooldown sair vc n pode entrar em nenhuma sala do tempo mesmo q a dilatação seja diferente (todas elas
/// tem gravidade 10x e o clima caotico)"*.
///
///   * "o dobro do tempo da dilatacao" -- a sessao e a MESMA da Terra, dois dias in-game
///     (`SalaDoTempo.SessaoEmDias`): numa sala que dilata D anos por dia isso sao 2xD anos la dentro;
///   * "vc fica preso la dentro, obrigando vc a sair" -- LEITURA DESTA CAMADA: como nao ha guardiao pra soltar
///     ninguem, a sala NAO prende. No fim da sessao ela EXPULSA: o corpo volta pra frente da porta, do lado de
///     fora (<see cref="DevolverAoMundo"/>). Prender de verdade nao teria saida nenhuma alem de morrer;
///   * "um cooldown igual e na terra, mas diretamente proporcional" -- a da Terra e 24 h reais, armada NA
///     ENTRADA (`MsDeRecargaDaSala`); a de uma sala D e 24 h x D, armada na entrada tambem (sair antes nao a
///     encurta, pelo motivo de la: marcar na saida daria reentrada de graca a quem saisse no primeiro minuto);
///   * "compartilha com outras" -- UM relogio por personagem (<see cref="FimDaRecargaDasSalas"/>), que vale pra
///     toda sala de planeta E pra da Terra: ele e o instante da ultima entrada (`SalaUltimaEntrada`, que ja
///     existia) mais a dilatacao da sala em que se entrou (`SalaDilatacao`);
///   * "gravidade 10x e o clima caotico" -- o interior e o MESMO mapa e a MESMA ficha de planeta da Sala da
///     Terra, pelo nome da zona: o funil `AplicarGravidade` e o `Clima.DaZona` respondem por ela sem saber.
/// ================================================================================================================
///
/// ============================ O QUE A SALA DE PLANETA NAO TEM (e a da Terra tem) ============================
/// DECISOES DESTA CAMADA, pelo mais simples -- e todas declaradas na porta (<see cref="RegrasDaSalaDePlaneta"/>):
///   * GUARDIAO, CHAVE, JANELA DE SAIDA E PRISAO: nao. O `sala_autorizar` e o `sala_soltar` continuam so da Terra;
///   * COMIDA: nao. As duas porcoes da Sala da Terra sao postas por ela; a porta na rocha de um mundo sem dono
///     nao tem quem ponha a mesa. Quem entra leva o que comer;
///   * VAGA DE QUEM CAIU: nao. A lotacao e a mesma (duas pessoas), mas conta quem ESTA la dentro em sessao: na
///     Terra quem cai segura a vaga, e sem guardiao isso deixaria um corpo deslogado segurar a sala de um mundo
///     inteiro. E, de brinde, a sala de planeta nao guarda NADA em memoria que o reinicio perca: a sessao esta
///     no save do personagem e a porta sai da seed.
///
/// O QUE VALE IGUAL: a lotacao de duas pessoas, os avisos de 10/5/1 minuto, a conta em dias in-game por tique
/// com a trava de parede, o envelhecimento (um ano por dia x a dilatacao) e a morte como saida.
/// ============================================================================================================
///
/// ============================ QUEM TEM SESSAO, E QUEM SO ESTA LA DENTRO ============================
/// A sessao de uma sala de planeta e de quem ATRAVESSOU A PORTA dela e ainda nao saiu: `ServerPlayer.SalaPlaneta`
/// guarda o nome do mundo desde a entrada e e apagado na saida, por qualquer caminho (a passagem, a expulsao, a
/// morte, um teleporte, um admin, virar passageiro de uma fusao). Vai pro disco: quem desloga la dentro volta com
/// a sessao onde parou.
///
/// QUEM ESTA LA DENTRO SEM SESSAO (o passageiro de uma fusao que se separou la, um corpo posto por admin, um NPC)
/// NAO RENDE: o funil de ritmo (`SessaoDaSalaValendo`) so paga quem tem sessao. Entrar de carona nao dribla a
/// recarga, e nao ha o que cronometrar em quem nao ganha nada.
/// =================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// AS PERGUNTAS QUE O RESTO DO SERVIDOR FAZ
	// =====================================================================
	/// <summary>Este corpo esta dentro da Sala do Tempo de um mundo sorteado?</summary>
	private static bool NaSalaDePlaneta(ServerPlayer pl) => SalaDoTempo.EhDePlaneta(pl.Zone);

	/// <summary>Este corpo esta numa Sala do Tempo -- a da Terra ou a de um mundo? (O que e do LUGAR: ver `SalaDoTempo.EhUmaSala`.)</summary>
	private static bool EmAlgumaSala(ServerPlayer pl) => SalaDoTempo.EhUmaSala(pl.Zone);

	/// <summary>Este corpo esta numa sala de planeta COM SESSAO ABERTA (ele atravessou a porta dela e nao saiu)?</summary>
	private static bool TemSessaoNaSalaDePlaneta(ServerPlayer pl) => NaSalaDePlaneta(pl) && TemSessaoDePlaneta(pl);

	/// <summary>
	/// A PORTA DA SALA de cada mundo sorteado VIVO no servidor, pela zona do mundo (<see cref="ZoneKey.Hash"/>).
	/// So na memoria, do tamanho do cache de mundos: nasce na colheita da geracao e sai na poda. E o que a saida
	/// consulta pra saber onde largar o corpo -- sem entrada aqui, o mundo esta fora da memoria e e acordado.
	/// </summary>
	private readonly Dictionary<ulong, PortaNoTerreno> _portasDasSalas = [];

	/// <summary>Quem pediu pra sair com o mundo de fora ainda dormindo: o tique completa a saida quando ele nascer.</summary>
	private readonly HashSet<int> _saidaPendenteDaSala = [];

	/// <summary>Quem ja ouviu "o mundo la fora ainda se forma" nesta saida. Anti-repeticao, por id.</summary>
	private readonly HashSet<int> _avisadosDaPortaFria = [];

	/// <summary>O buffer do tique: quem a sala devolve nesta volta. Reusado (`Clear` nao devolve a capacidade).</summary>
	private readonly List<ServerPlayer> _aDevolverDaSala = [];

	// =====================================================================
	// OS BOTOES DE DEFEITO (bancada --salasteste). Falsos em jogo, sempre.
	// =====================================================================
	/// <summary>DEFEITO INJETADO (bancada --salasteste): o mundo que nasce com sala na seed nasce SEM a porta de pe.</summary>
	internal bool PortaDaSalaNaoNasceDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --salasteste): no fim da sessao a sala de planeta nao expulsa -- o corpo fica la dentro.</summary>
	internal bool SalaNaoExpulsaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --salasteste): a porta de planeta nao pergunta pela recarga -- cada sala teria o seu relogio.</summary>
	internal bool RecargaSoDaTerraDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --salasteste): a recarga e de 24 h pra toda sala, sem a dilatacao.</summary>
	internal bool RecargaSemDilatacaoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --salasteste): a sala de planeta nao tem lotacao.</summary>
	internal bool SalaDePlanetaSemLotacaoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --salasteste): quem sai da sala por fora da porta (teleporte, admin) leva a sessao aberta no bolso.</summary>
	internal bool SairPorForaMantemASessaoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --salasteste): mergulhar na propria mente la dentro fecha a sessao -- quem acorda nao rende mais e a saida dele vira o berco.</summary>
	internal bool TranseFechaASessaoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --salasteste): quem esta na sala sem ter atravessado a porta rende como quem atravessou.</summary>
	internal static bool SemSessaoTambemRendeDeTeste;

	// =====================================================================
	// A RECARGA -- UM RELOGIO POR PERSONAGEM, PRA TODA SALA
	// =====================================================================
	/// <summary>
	/// QUANTO DURA A RECARGA de uma sala que dilata tantos anos por dia: as 24 h da Terra vezes a dilatacao
	/// (3 meses = 6 h; 1 ano = 24 h; 5 anos = 5 dias), em milissegundos reais.
	/// </summary>
	private long MsDeRecargaDaSalaQueDilata(double anosPorDia) =>
		(long)(MsDeRecargaDaSala * (RecargaSemDilatacaoDeTeste ? 1 : Math.Max(anosPorDia, 0)));

	/// <summary>
	/// ATE QUANDO ESTE PERSONAGEM NAO ENTRA EM SALA NENHUMA (`NowMs`; 0 = nunca entrou em uma).
	///
	/// E O UNICO RELOGIO DE RECARGA, lido pelas duas portas -- a do Templo (`EntrarNaSala`) e a de qualquer mundo
	/// (<see cref="EntrarNaSalaDePlaneta"/>): *"ate o cooldown sair vc n pode entrar em nenhuma sala do tempo mesmo q
	/// a dilatação seja diferente"*. Sao dois numeros do personagem, os dois no save: QUANDO ele entrou pela ultima
	/// vez numa sala (`SalaUltimaEntrada`) e QUANTO ela dilatava (`SalaDilatacao`).
	///
	/// SAVE ANTIGO: a dilatacao chega 0, e 0 se le como 1 -- a unica sala que existia era a da Terra. A recarga de
	/// quem ja estava esperando continua sendo as mesmas 24 h, sem migracao.
	/// </summary>
	private long FimDaRecargaDasSalas(ServerPlayer pl) =>
		pl.SalaUltimaEntrada <= 0
			? 0
			: pl.SalaUltimaEntrada + MsDeRecargaDaSalaQueDilata(pl.SalaDilatacao > 0 ? pl.SalaDilatacao : SalaDePlaneta.DilatacaoDaTerra);

	/// <summary>"3h20m", ou "4 dias e 2h" quando passa de dois dias -- a recarga de uma sala de 5 anos sao cinco dias.</summary>
	private static string TextoDeEspera(long ms)
	{
		ms = Math.Max(ms, 0);
		long h = ms / 3_600_000, m = ms % 3_600_000 / 60_000;
		return h >= 48 ? $"{h / 24} dias e {h % 24}h" : $"{h}h{m:00}m";
	}

	// =====================================================================
	// A PORTA NA MONTANHA
	// =====================================================================
	/// <summary>O centro de um corpo de pe numa celula (a posicao do corpo fica `FeetOffsetY` acima dos pes).</summary>
	private static Vec2 CorpoNaCelulaDaSala(int cx, int cy)
	{
		const int t = ZoneCollision.TileSize;
		return new Vec2(cx * t + t / 2f, cy * t + t / 2f - MoveRules.FeetOffsetY);
	}

	/// <summary>
	/// O MUNDO ACABOU DE NASCER: se a seed dele tem uma Sala do Tempo, a porta fica de pe na celula que o gerador
	/// escolheu (<see cref="TerrenoGerado.PortaDaSala"/>). Chamado pela colheita das geracoes (`TickDasGeracoes`),
	/// ao lado do pomar e pelo motivo dele: quem chega -- pousando, logando, saindo da propria sala -- ja a
	/// encontra, e o `MandarObras` do `MoveToZone` a leva no primeiro pacote.
	///
	/// E A MESMA OBRA DA PORTA DO TEMPLO (`Time_Chamber_Door`), posta como mobilia do mapa: desenha, responde a
	/// tecla E com os mesmos dois botoes, nao vai pro `mundo.json` (renasce da seed) e NAO CAI no soco (a guarda
	/// por tipo do `Estragar`). O que muda e o que o servidor faz com o verbo: a porta num mundo sorteado leva a
	/// sala DELE (<see cref="EntrarNaSalaDePlaneta"/>).
	/// </summary>
	private void AbrirAPortaDaSala(ZoneKey mundo, TerrenoGerado t)
	{
		if (PortaDaSalaNaoNasceDeTeste || t.PortaDaSala is not { } porta)
		{
			_portasDasSalas.Remove(mundo.Hash);
			return;
		}

		_portasDasSalas[mundo.Hash] = porta;

		if (_obras?.Get(TipoDaPorta) == null)
		{
			GD.PushWarning($"[server] sala do tempo de {mundo.Name}: '{TipoDaPorta}' nao esta no catalogo de "
						   + "construcoes -- a porta nao vai nascer (rode o AssetPipeline 'tech')");
			return;
		}

		// O MUNDO QUE RENASCE NO MESMO PROCESSO pode achar a propria porta ainda de pe (quem o tirou da memoria
		// nao passou pela poda): uma so por mundo.
		if (!_noChao.Exists(o => o.Tipo == TipoDaPorta && o.Zona.Equals(mundo)))
			PorMobiliaDePe(mundo, TipoDaPorta, porta.X, porta.Y);
		MandarObras(mundo);

		GD.Print($"[server] {mundo.Name} guarda uma SALA DO TEMPO: {SalaDePlaneta.TextoDaDilatacao(SalaDePlaneta.Dilatacao(mundo.Seed))} "
				 + $"por dia; a porta esta na rocha em ({porta.X},{porta.Y}), a {porta.Passos} passo(s) do pouso"
				 + (porta.Erguida ? " | ROCHEDO ERGUIDO (nao havia parede de montanha ao alcance)" : ""));
	}

	/// <summary>O MUNDO SAIU DA MEMORIA: a porta da sala sai com ele, e renasce com ele (como as macieiras do pomar).</summary>
	private void FecharAPortaDaSala(ulong hashDoMundo)
	{
		if (!_portasDasSalas.Remove(hashDoMundo)) return;
		_noChao.RemoveAll(o => o.DoMapa && o.Tipo == TipoDaPorta && o.Zona.Hash == hashDoMundo);
	}

	/// <summary>Quantas pessoas estao em sessao nesta sala de planeta agora. Ver o cabecalho: quem caiu nao segura vaga.</summary>
	private int QuantosNaSalaDePlaneta(ZoneKey sala)
	{
		int n = 0;
		foreach (ServerPlayer p in ZoneList(sala.Hash))
			if (TemSessaoDePlaneta(p)) n++;
		return n;
	}

	/// <summary>
	/// A TENTATIVA DE ENTRAR PELA PORTA DE UM MUNDO SORTEADO. Devolve falso quando o corpo nao esta num mundo
	/// sorteado -- ai a porta e a do Templo, e quem responde e o `EntrarNaSala` de sempre.
	///
	/// A ORDEM DAS RECUSAS e a de la, sem as duas que so a Terra tem (a prisao e a chave do Guardiao): primeiro o
	/// corpo (morto, caido, voando), depois a porta (lotacao), depois o relogio (a recarga).
	/// </summary>
	private bool EntrarNaSalaDePlaneta(ServerPlayer pl)
	{
		if (!EhZonaProcedural(pl.Zone)) return false;

		if (ObraQueAceita(pl, "sala_entrar") == null)
		{
			Avisar(pl, "não há porta da Sala do Tempo por perto.");
			return true;
		}

		if (pl.Ficha.dead) { Avisar(pl, "mortos não treinam."); return true; }
		if (pl.Ficha.KO) { Avisar(pl, "você mal se sustenta de pé. Levante primeiro."); return true; }
		if (pl.Voando) { Avisar(pl, "pouse antes de atravessar a porta."); return true; }
		// (2026-10-27) a Sala fechada no comeco do mundo vale aqui tambem (o pedido 112) -- ver `SalaDoTempoAberta`
		if (!SalaDoTempoAberta(NowMs())) { Avisar(pl, FraseDaSalaFechada(NowMs())); return true; }

		ZoneKey sala = SalaDePlaneta.Interior(pl.Zone);
		int dentro = QuantosNaSalaDePlaneta(sala);
		if (!SalaDePlanetaSemLotacaoDeTeste && dentro >= LotacaoDaSala)
		{
			Avisar(pl, $"esta Sala do Tempo já tem {dentro} pessoa(s) dentro, que é o máximo. Espere alguém sair.");
			return true;
		}

		long agora = NowMs();
		long falta = RecargaSoDaTerraDeTeste ? 0 : FimDaRecargaDasSalas(pl) - agora;
		if (falta > 0)
		{
			Avisar(pl, "seu corpo ainda não se recuperou da distorção temporal -- e a recarga vale pra TODA Sala do "
					   + $"Tempo, não só pra última. Você poderá entrar de novo em {TextoDeEspera(falta)} (tempo real).");
			return true;
		}

		if (_catalogo?.Get(ZoneKey.Premade(ZonaDaSala)) is not { } ficha)
		{
			// FALHAR ALTO, como a porta do Templo: sem o mapa no manifesto a porta nao tem pra onde levar.
			Avisar(pl, "a porta não se abre. (A Sala do Tempo não está no manifesto de mapas.)");
			GD.PushError($"[server] sala de planeta: a zona '{ZonaDaSala}' nao esta no manifesto");
			return true;
		}

		// (2026-10-21) O AGARRADO NAO FURA LOTACAO NEM RECARGA. Sem chave aqui: esta porta nao tem Guardiao.
		if (PorQueLevarOAgarradoBarraASala(pl, false) is { Length: > 0 } barraDaSala)
		{
			Avisar(pl, barraDaSala);
			return true;
		}

		// A RECARGA E ARMADA NA ENTRADA, com a dilatacao DESTA sala, e a sessao comeca do zero -- as mesmas tres
		// linhas da porta do Templo, mais o nome do mundo (a sessao aberta: ver o cabecalho).
		double dilatacao = SalaDePlaneta.Dilatacao(pl.Zone.Seed);
		string mundoNome = pl.Zone.Name;
		string mundo = NomeDoMundo(pl.Zone);
		AbrirSessaoDaSalaDePlaneta(pl, agora, dilatacao, mundoNome);

		// O MESMO PONTO DE CHEGADA DA SALA DA TERRA (`HTC_ENTRY_LOC`): e o mesmo quarto.
		const int t = ZoneCollision.TileSize;
		var destino = new Vec2((EntradaByondX - 1) * t + t / 2f, (ficha.H - EntradaByondY) * t + t / 2f);

		// QUEM FICA VE ELE ENTRAR, antes da mudanca de zona (depois dela a lista seria a de dentro).
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl) Avisar(o, $"{NomeVisivel(pl)} atravessa a porta na rocha.");

		ViajarVoluntario(pl, sala, destino, aoLevar: c => AbrirSessaoDaSalaDePlaneta(c, agora, dilatacao, mundoNome));

		string passa = SalaDePlaneta.TextoDaDilatacao(dilatacao);
		int anosMaisVelho = SalaDoTempo.AnosFechados(0, SalaDoTempo.SessaoEmDias, dilatacao);
		Avisar(pl, $"você atravessa a porta na rocha de {mundo}. Aqui dentro passam {passa} a cada dia lá de fora.");
		Avisar(pl, $"a sessão dura {SalaDoTempo.SessaoEmDias:0} dias in-game (~{SalaDoTempo.SessaoEmMinutosReais:0} min): "
				   + $"{SalaDePlaneta.TextoDaDilatacao(SalaDoTempo.AnosDaSessao(dilatacao))} de treino"
				   + (anosMaisVelho > 0
					   ? $", e você sai {anosMaisVelho} ano{(anosMaisVelho == 1 ? "" : "s")} mais velho."
					   : " -- pouco demais pra fechar um ano de idade.")
				   + " Não há guardião aqui: quando ela acaba, a sala devolve você à porta.");
		Avisar(pl, $"o chão puxa {pl.Ficha.Planetgrav:0.##}x e o tempo comprime o treino em "
				   + $"{pl.Ficha.zoneGainMult:0}x: aqui você rende {pl.Ficha.MultiplicadorDeGanho():0}x "
				   + "o que renderia lá fora. (A maestria de forma sobe 4x -- ela é costume, não tempo.)");
		Avisar(pl, $"seu corpo só aguenta outra Sala do Tempo -- esta, a de outro mundo ou a da Terra -- daqui a "
				   + $"{TextoDeEspera(MsDeRecargaDaSalaQueDilata(dilatacao))} (tempo real). Não há comida aqui dentro.");
		GD.Print($"[server] {pl.Name} entrou na Sala do Tempo de {pl.SalaPlaneta} ({passa}/dia; {dentro + 1}/{LotacaoDaSala} dentro)");
		return true;
	}

	/// <summary>
	/// O AVISO DA PORTA DE UM MUNDO SORTEADO -- o que esta sala cobra, lido ANTES de atravessar. Devolve falso
	/// quando o corpo nao esta num mundo sorteado (a porta e a do Templo: `RegrasDaPorta`).
	///
	/// A DILATACAO APARECE AQUI, com todas as letras: e do LUGAR, e nao de corpo nenhum.
	/// </summary>
	private bool RegrasDaSalaDePlaneta(ServerPlayer pl)
	{
		if (!EhZonaProcedural(pl.Zone)) return false;

		if (ObraQueAceita(pl, "sala_regras") == null)
		{
			Avisar(pl, "não há porta da Sala do Tempo por perto.");
			return true;
		}

		double d = SalaDePlaneta.Dilatacao(pl.Zone.Seed);
		int anos = SalaDoTempo.AnosFechados(0, SalaDoTempo.SessaoEmDias, d);
		Avisar(pl, "-- A PORTA NA ROCHA: UMA SALA DO TEMPO --");
		Avisar(pl, $"  um dia aqui fora são {SalaDePlaneta.TextoDaDilatacao(d)} lá dentro"
				   + (d == SalaDePlaneta.DilatacaoDaTerra ? " -- como na da Terra."
					  : d < SalaDePlaneta.DilatacaoDaTerra ? " -- menos que na da Terra (1 ano), e muito mais que treinar aqui fora."
					  : " -- mais que na da Terra (1 ano).")
				   + " O chão puxa 10x mais forte.");
		Avisar(pl, $"  cabem {LotacaoDaSala} pessoas por vez. Ninguém guarda esta porta: ela não pede chave.");
		Avisar(pl, $"  a sessão rende por {SalaDoTempo.SessaoEmDias:0} dias in-game (~{SalaDoTempo.SessaoEmMinutosReais:0} min): "
				   + $"{SalaDePlaneta.TextoDaDilatacao(SalaDoTempo.AnosDaSessao(d))} de treino"
				   + (anos > 0 ? $", e {anos} ano{(anos == 1 ? "" : "s")} a mais de idade." : ", sem fechar um ano de idade."));
		Avisar(pl, "  quando a sessão acaba, a sala DEVOLVE você a esta porta. Dá pra sair antes pela passagem de dentro.");
		Avisar(pl, $"  depois de entrar, seu corpo leva {TextoDeEspera(MsDeRecargaDaSalaQueDilata(d))} REAIS pra aguentar outra "
				   + "Sala do Tempo -- esta, a de outro mundo ou a da Terra. Sair antes não encurta a espera.");
		Avisar(pl, "  não há comida lá dentro: leve a sua.");

		long falta = FimDaRecargaDasSalas(pl) - NowMs();
		if (falta > 0) Avisar(pl, $"  hoje a porta não se abre pra você: faltam {TextoDeEspera(falta)} da sua recarga.");
		return true;
	}

	// =====================================================================
	// A SAIDA -- a passagem de dentro, e a expulsao
	// =====================================================================
	/// <summary>
	/// A PASSAGEM DE SAIDA, VISTA DE UMA SALA DE PLANETA. O mapa e o da Sala da Terra, e as celulas de saida dele
	/// (`fromhbtc`) levam ao Templo Sagrado; numa sala de planeta elas levam de volta a porta do mundo dela.
	/// Chamado do `TickDasPassagens`, no lugar do `Atravessar`. Devolve falso fora de sala de planeta.
	/// </summary>
	private bool ASalaDePlanetaTemOutraSaida(ServerPlayer pl)
	{
		if (!NaSalaDePlaneta(pl)) return false;

		// A CARENCIA E A MARCA DE "EMPURRANDO", como no `Atravessar`: a tecla que trouxe o corpo ate a saida
		// continua apertada do outro lado, e nao pode contar como gesto novo.
		_acabouDeAtravessar[pl.Id] = NowMs() + MsDeCarenciaDePassagem;
		_naBocaDaPassagem.Add(pl.Id);

		// SE O MUNDO DE FORA AINDA DORME, a saida fica pendente e o tique a completa (ver `DevolverAoMundo`).
		if (!DevolverAoMundo(pl, expulso: false)) _saidaPendenteDaSala.Add(pl.Id);
		return true;
	}

	/// <summary>
	/// A SALA DEVOLVE ESTE CORPO AO MUNDO DELA: pra frente da porta, do lado de fora. Vale pra quem sai pela
	/// passagem e pra quem a sessao expulsa. Devolve falso quando o mundo ainda esta nascendo no servidor -- quem
	/// chamou tenta de novo no tique seguinte.
	///
	/// OS QUATRO DESTINOS, nesta ordem:
	///   1. SEM SESSAO (entrou de carona, foi posto por admin): a sala nao sabe por que porta ele veio -- vai
	///      pro berco, que e o destino de quem nao tem pra onde voltar;
	///   2. O MUNDO FOI DESTRUIDO enquanto ele estava la dentro: a porta abre pro nada, e ele cai no espaco onde
	///      o planeta ficava -- o `SaidaParaUmMundoMorto` que a Sala da Terra, o Templo e as cavernas ja usam;
	///   3. O MUNDO ESTA FORA DA MEMORIA (todo mundo entrou na sala e a poda o soltou; o servidor reiniciou com
	///      alguem la dentro): ele e acordado (`AcordarOMundoGerado`) e a saida espera a colheita;
	///   4. O CASO DE SEMPRE: a soleira da porta.
	/// </summary>
	private bool DevolverAoMundo(ServerPlayer pl, bool expulso)
	{
		if (!TemSessaoDePlaneta(pl))
		{
			Avisar(pl, "a sala não reconhece por que porta você entrou, e te devolve pra casa.");
			FecharASessaoDePlaneta(pl);
			MandarProBerco(pl);
			return true;
		}

		ZoneKey mundo = SalaDePlaneta.MundoDe(pl.Zone, pl.SalaPlaneta);
		if (SaidaParaUmMundoMorto(pl, mundo)) { FecharASessaoDePlaneta(pl); return true; }

		if (!_portasDasSalas.TryGetValue(mundo.Hash, out PortaNoTerreno porta))
		{
			if (CorpoGerado.DaZona(SeedDoUniverso, mundo) == null || MundoFrioFicaFrioDeTeste)
			{
				// NAO E UM CORPO DESTE UNIVERSO (o save atravessou uma troca de semente): nao ha porta a que voltar.
				Avisar(pl, "a porta por onde você entrou não existe mais. A sala te devolve pra casa.");
				GD.Print($"[server] sala de planeta: '{mundo}' nao e um corpo deste universo -- {pl.Name} vai pro berco");
				FecharASessaoDePlaneta(pl);
				MandarProBerco(pl);
				return true;
			}

			AcordarOMundoGerado(mundo);
			if (_avisadosDaPortaFria.Add(pl.Id))
				Avisar(pl, "a porta range: do outro lado, o mundo ainda está se formando. Um instante.");
			return false;
		}

		(int sx, int sy) = porta.Soleira;
		Vec2 soleira = CorpoNaCelulaDaSala(sx, sy);
		soleira = MapaDaZonaOuCatalogo(mundo)?.PontoLivrePerto(soleira) ?? soleira;

		string nome = NomeDoMundo(mundo);
		long falta = FimDaRecargaDasSalas(pl) - NowMs();
		double dias = pl.SalaDiasDaSessao;
		FecharASessaoDePlaneta(pl);
		ViajarVoluntario(pl, mundo, soleira, aoLevar: c => { if (TemSessaoDePlaneta(c)) FecharASessaoDePlaneta(c); });

		Avisar(pl, expulso
			? $"a sessão acabou, e a Sala do Tempo te devolve à porta: você está de volta a {nome}."
			: $"você sai pela porta na rocha e está de volta a {nome}.");
		if (falta > 0)
			Avisar(pl, $"seu corpo precisa de {TextoDeEspera(falta)} (tempo real) antes de aguentar outra Sala do Tempo -- qualquer uma.");
		GD.Print($"[server] sala de planeta: {pl.Name} {(expulso ? "foi EXPULSO" : "saiu")} pra {mundo.Name} "
				 + $"({dias:0.##} de {SalaDoTempo.SessaoEmDias:0} dias gastos)");
		return true;
	}

	/// <summary>
	/// A SESSAO DE PLANETA DESTE CORPO ACABOU -- ele saiu, por onde quer que tenha sido. So apaga o que marca a
	/// sessao ABERTA: os dias gastos e a recarga ficam (quem gastou gastou, e quem zera a conta e a proxima porta).
	/// </summary>
	private void FecharASessaoDePlaneta(ServerPlayer pl)
	{
		pl.SalaPlaneta = "";
		EsquecerASalaDePlaneta(pl.Id);
		_avisoDaSala.Remove(pl.Id);
	}

	/// <summary>O ruido por id (id se reusa): a saida pendente e o aviso da porta fria. Chamado tambem no logout.</summary>
	private void EsquecerASalaDePlaneta(int id)
	{
		_saidaPendenteDaSala.Remove(id);
		_avisadosDaPortaFria.Remove(id);
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// A SESSAO DE QUEM ESTA NUMA SALA DE PLANETA: conta os dias, envelhece, avisa -- pelo MESMO passo da Sala da
	/// Terra (`CorrerASessao`, que le a dilatacao da zona) -- e, no fim, EXPULSA. Roda no tique cheio, depois do da
	/// sessao da Terra.
	///
	/// O CUSTO e uma comparacao de comprimento por corpo: quase ninguem tem sessao de planeta aberta.
	/// </summary>
	private void TickDasSalasDePlaneta(double dt)
	{
		if (PresencaNaSalaDeTeste) DispararABancadaDaPresenca();   // a bancada `--salapresencateste` (so com a flag) -- ver `GameServer.SalaPresencaTeste.cs`

		long agora = NowMs();
		_aDevolverDaSala.Clear();

		foreach (ServerPlayer pl in _players.Values)
		{
			if (!TemSessaoDePlaneta(pl)) continue;   // (a marca da Sala da TERRA nao e daqui: quem cuida dela e o `TickDaSalaDoTempo`)

			// SAIU DA SALA DE VERDADE (morreu, foi teleportado, um admin o trouxe, saiu dentro de quem saiu): a sessao
			// fecha onde ele estiver. A recarga ja estava armada desde a entrada.
			//
			// MENOS NUM BOLSO LA DENTRO -- a mesma regua da Sala da Terra (`GameServer.SalaPresenca.cs`): quem medita
			// fundo (o CORPO fica deitado na sala), vira passageiro de quem esta la ou e absorvido por quem esta la
			// NAO saiu. A sessao so para de contar (ele nao rende nem envelhece no bolso) e continua na volta. A trava
			// de parede segue valendo desde a entrada, entao ficar no bolso nao estica a sessao.
			if (!NaSalaDePlaneta(pl))
			{
				if (!TranseFechaASessaoDeTeste && NumaSalaDePlanetaDeVerdade(pl)) continue;
				if (!SairPorForaMantemASessaoDeTeste) FecharASessaoDePlaneta(pl);
				continue;
			}

			bool pendente = _saidaPendenteDaSala.Contains(pl.Id);
			if (!pendente && SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao))
			{
				CorrerASessao(pl, dt, agora);
				if (!NaSalaDePlaneta(pl)) continue;   // o ano que fechou o matou de velhice: ja foi pro alem
			}

			if (pendente || (!SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao) && !SalaNaoExpulsaDeTeste))
				_aDevolverDaSala.Add(pl);
		}

		// FORA DO LACO: devolver troca o corpo de zona, e o `MandarProBerco` pode mexer em mais coisa.
		foreach (ServerPlayer pl in _aDevolverDaSala)
			DevolverAoMundo(pl, expulso: !_saidaPendenteDaSala.Contains(pl.Id));
	}

	/// <summary>
	/// QUEM ENTRA NO MUNDO, VISTO PELAS SALAS DE PLANETA. Chamado do `SalaDoTempoNoLogin` pra quem nao esta na
	/// Sala da Terra.
	///
	///   * FORA de qualquer sala com a sessao de planeta aberta no save: e lixo de uma queda no meio da saida -- fecha;
	///   * DENTRO de uma sala de planeta com sessao: o mundo de fora comeca a nascer JA (o servidor pode ter
	///     reiniciado, e a expulsao vai precisar da porta). A conta de dias continua do disco; se a sessao venceu
	///     pela trava de parede enquanto ele dormia, o primeiro tique o expulsa.
	/// </summary>
	private void SalaDePlanetaNoLogin(ServerPlayer pl)
	{
		if (!NaSalaDePlaneta(pl))
		{
			if (pl.SalaPlaneta.Length > 0) pl.SalaPlaneta = "";
			return;
		}
		if (TemSessaoDePlaneta(pl)) AcordarOMundoGerado(SalaDePlaneta.MundoDe(pl.Zone, pl.SalaPlaneta));
	}

	// =====================================================================
	// O VERB DE ADMIN -- achar uma sala sem varrer a galaxia
	// =====================================================================
	/// <summary>Os raios (em celulas de sistema) que o `admin_sala_do_tempo` tenta, do mais perto pro mais longe.</summary>
	private static readonly int[] RaiosDaBuscaDeSala = [6, 12, 24, 48];

	/// <summary>
	/// `admin_sala_do_tempo [anos]`: LEVA O ADMIN A ORBITA DO MUNDO COM SALA DO TEMPO MAIS PERTO -- com o
	/// argumento, o mais perto cuja sala dilata PELO MENOS tantos anos por dia ("5" acha a melhor que existe).
	/// Nao tem original (o DM so tem a Sala da Terra). Existe pelo motivo do `admin_mundo_divino`: a sala mais
	/// perto fica a dezenas de minutos de voo, e a de 5 anos e um mundo em vinte mil.
	///
	/// SO NO ESPACO, como aquele e pelo motivo escrito la: no chao ele so LISTA as salas das celulas em volta (o
	/// nome do mundo, a dilatacao e a distancia), sem mudar o corpo de zona. Larga em orbita, logo acima do
	/// disco; pousar e encostar no planeta, e a porta fica na montanha mais perto do pouso.
	/// </summary>
	private void AdminIrParaASalaDoTempo(ServerPlayer adm, string arg)
	{
		double minimo = double.TryParse(arg.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
										System.Globalization.CultureInfo.InvariantCulture, out double pedido) ? pedido : 0;

		bool noEspaco = Espaco.EhEspaco(adm.Zone);
		Vec2 daqui = noEspaco ? adm.Pos : OndeEstouNaGalaxia(adm);
		SistemaId c = SistemaId.De(daqui);

		List<SalaNoUniverso> achadas = [];
		foreach (int raio in RaiosDaBuscaDeSala)
		{
			achadas = SalaDePlaneta.NaRegiao(SeedDoUniverso, c.Sx, c.Sy, raio)
				.Where(s => s.Dilatacao >= minimo && !ZonaMorta(Espaco.ZonaDe(s.Corpo)))
				.OrderBy(s => (s.Corpo.Pos - daqui).Length)
				.ToList();
			if (achadas.Count > 0) break;
		}

		if (achadas.Count == 0)
		{
			Avisar(adm, $"nenhuma Sala do Tempo {(minimo > 0 ? $"de {minimo:0.##}+ anos por dia " : "")}nas "
						+ $"{RaiosDaBuscaDeSala[^1]} células em volta.");
			return;
		}

		foreach (SalaNoUniverso s in achadas.Take(5))
			Avisar(adm, $"  Sala do Tempo em {NomeDoMundo(s.Corpo)} [{s.Sx}:{s.Sy}] -- {SalaDePlaneta.TextoDaDilatacao(s.Dilatacao)} por dia, "
						+ $"gravidade {MundoProcedural.GravidadeDaSeed(s.Corpo.Seed):0.#}x lá fora, a "
						+ $"{(s.Corpo.Pos - daqui).Length / Sistemas.CelulaPx:0.0} célula(s) daqui.");

		if (!noEspaco)
		{
			Avisar(adm, "decole primeiro: do espaço, este verb salta até a órbita da mais perto da lista.");
			return;
		}

		SalaNoUniverso alvo = achadas[0];
		MoveToZone(adm.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(alvo.Corpo));

		// O CARIMBO DE CHUNK E A VIZINHANCA VAO JUNTO -- o par de linhas do `AdminIrParaOMundoDivino`.
		adm.ChunkAtual = ChunkId.De(adm.Pos);
		MandarVizinhanca(adm);

		Avisar(adm, $"você salta até a órbita de {NomeDoMundo(alvo.Corpo)}. Encoste nele pra pousar: a porta fica na "
					+ "parede de montanha mais perto do pouso (menu P > World diz que este mundo guarda uma).");
		GD.Print($"[server] {adm.Name} foi levado a orbita de {alvo.Corpo.Nome} [{alvo.Sx}:{alvo.Sy}]k{alvo.K} "
				 + $"(sala do tempo, {alvo.Dilatacao:0.##} ano(s)/dia)");
	}

	/// <summary>
	/// ONDE ESTE CORPO ESTA NA GALAXIA quando nao esta no espaco: o disco do mundo em que ele pisa (sorteado ou do
	/// mapa), ou a Terra -- o zero do universo -- pra quem esta num interior.
	/// </summary>
	private Vec2 OndeEstouNaGalaxia(ServerPlayer pl)
	{
		if (CorpoGerado.DaZona(SeedDoUniverso, pl.Zone) is { } gerado) return gerado.Corpo.Pos;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, pl.Zone.Name, StringComparison.OrdinalIgnoreCase)) return p.Pos;
		return new Vec2(0, 0);
	}
}
