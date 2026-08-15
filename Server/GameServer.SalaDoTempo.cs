using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A SALA DO TEMPO -- A PORTA. Porte de `Code/Modules/Globals/TimeChamber.dm` + `Code/Turfs.dm:157`.
///
/// ============================ ESTA FASE E A PORTA, E SO ELA ============================
/// A Sala do Tempo tem cinco partes (porta, sessao cronometrada, comida, prisao e o vazio
/// procedural em volta). Este arquivo e a PRIMEIRA, e ela vinha primeiro por um motivo concreto:
/// **nao havia como entrar na sala por caminho nenhum**.
///
/// O `Tools/AssetPipeline/Passagens.cs` tirou `/turf/Teleporters/tohbtc` da tabela de passagens
/// automaticas com a justificativa certa -- no DM a entrada passa por `htc_try_enter()`, que confere
/// autorizacao do Guardiao e uma recarga de 24 h REAIS antes de mover o corpo, e exporta-la como
/// "pisou, foi" burlaria as duas. So que o SUBSTITUTO nunca foi feito, e a sala ficou trancada
/// desde entao. Ela e o mapa z13, com 248 mil celulas convertidas, sem entrada.
///
/// O substituto e este: a porta virou MOBILIA (`Time_Chamber_Door` no catalogo de construcoes, ver
/// `DmTechScanner.MobiliaDeMapa`), que e o mesmo caminho do banco e da macieira -- ela sai do
/// tilemap, vira node, bloqueia, aparece pra todo mundo pelo mesmo pacote, e a tecla E a alcanca.
/// O gate mora AQUI, no servidor, e nao no gesto: o menu so manda o verbo, e quem decide se o corpo
/// se move e este arquivo.
///
/// **PORTA E NAO PASSAGEM, e a diferenca e do proprio DM**: o `Enter()` do `tohbtc` devolve **0**
/// (nunca se atravessa a porta em si) e delega a `htc_try_enter()`. Ou seja, la ela ja era um
/// objeto que RESPONDE com desenho de porta, e nao um chao que leva a algum lugar.
/// =======================================================================================
///
/// ============================ O DM TEM DOIS SISTEMAS DE SALA, E ISSO E ACIDENTE ============================
/// O BYOND roda `TimeChamber.dm` (novo: 280x de ganho, sessao de 2 anos, recarga de 24 h) E o
/// legado de `Stats.dm:486-510` (`HBTCMod = 25` multiplicando `Egains`, mais um `HBTCTime` proprio
/// que expulsa em ~1 h real) **ao mesmo tempo**. Nos caminhos de treino os dois se SOMAM e dao
/// 7000x. Ninguem desenhou isso.
///
/// O port porta so o NOVO. `Fighter.HBTCMod` fica **valendo 1 para sempre** -- ele existe no port
/// porque `Fighter.Power.cs:135` o le dentro de `Egains`, e apagar o campo mexeria numa formula
/// que nao e desta fase. O 25 foi descartado de proposito; quem for ligar o multiplicador da sala
/// (13.4) escreve `zoneGainMult = GainKnobs.TimeChamberMult` (280) e **nao** mexe no `HBTCMod`,
/// senao o ganho da sala volta a ser 7000x sem ninguem ter pedido.
/// ========================================================================================================
///
/// ============================ O RISCO DA PRISAO, DECIDIDO ============================
/// A regra do dono (13.6c) e que a sala **nao expulsa: ela prende**. Depois de 50 minutos a porta
/// deixa de aceitar a pessoa, e so o Guardiao da Terra a tira de la. Isso cria um estado em que um
/// jogador **nao pode jogar** -- e um jogo que depende de outro jogador estar online pra devolver o
/// seu personagem.
///
/// As tres saidas honestas eram: (1) um admin poder soltar; (2) um tempo maximo de prisao; (3)
/// aceitar o custo e avisar bem claro na porta. **A escolha e (1) MAIS (3)**, e ela esta escrita
/// aqui em codigo, nao so em prosa:
///
///   * <see cref="ComandoDaSalaDoTempo"/> trata `admin_sala_soltar` -- a valvula. O admin sempre
///     solta, esteja o Guardiao online ou nao;
///   * <see cref="RegrasDaPorta"/> e o aviso, e ele e uma ACAO DO MENU DA PORTA: ninguem entra sem
///     ter tido a chance de ler o que a sala cobra. O texto diz, com todas as letras, que passar de
///     50 minutos prende e quem solta.
///
/// O (2) -- um prazo maximo de prisao -- foi RECUSADO porque ele apaga a regra do dono em silencio:
/// uma prisao que se abre sozinha em N minutos e a expulsao do original com outro nome, e a sala
/// voltaria a "so perder tempo". Se o custo tem que existir pra a regra existir, o que se deve
/// ao jogador e uma VALVULA e um AVISO, e nao um prazo que finge que o custo nao esta la.
/// =====================================================================================
///
/// ============================ A TERCEIRA SAIDA E MORRER, E ELA E DE PROPOSITO ============================
/// **NAO CONSERTE ISTO.** Quem ler <see cref="AMorteSaiDaSala"/> vai pensar "a morte fura a tranca"
/// e vai querer gatear o caminho da morte junto com a porta e a passagem. E o contrario: e a
/// DECISAO DO DONO, tomada na mesma frase em que ele tirou o `Goto Spawn` das maos do jogador --
/// *"sair da sala do tempo dps de preso so morrendo pra sair"*.
///
/// Os dois andam juntos e formam um desenho so:
///   * o `Goto Spawn` SAI (era teleporte de graca, com um botao no menu "Other" e nenhum preco);
///   * a MORTE FICA -- e assim a prisao deixa de ser absoluta e passa a ter um PRECO.
///
/// E o preco existe: morrer neste jogo ja custa karma, o julgamento do Enma, o debuff de revive
/// por Zeni e idade. Um preso sem Guardiao online nao fica sem jogo -- fica com uma escolha cara.
/// Um caminho de saida gratuito e uma tranca que nao tranca; um caminho de saida caro e uma regra.
///
/// EM CODIGO: `IrProAlem` (`GameServer.Alem.cs`) chama <see cref="AMorteSaiDaSala"/>, que LIMPA o
/// bit de prisao. Limpar e a metade que importa -- sem ela o corpo renasce do lado de fora e
/// continua marcado como preso pra sempre, e a porta o recusaria na proxima vida inteira.
///
/// A CHAMADA MOROU NO `Renascer` ATE A MORTE VIRAR UM PERCURSO. Enquanto morrer e voltar a vida
/// eram o mesmo instante, tanto fazia; com a viagem pro Outro Mundo no meio, deixa-la no fim
/// manteria o preso MARCADO durante todo o tempo em que ele estivesse morto no alem, e a
/// `SalaSessao` seguiria contando o tempo dele numa sala de outro planeta. Ela subiu pro PRIMEIRO
/// passo que de fato tira o corpo da sala. **Nao a reponha no `Renascer` "por seguranca"**: seria a
/// segunda copia, e ela chamaria `EsquecerSessaoDaSala` numa sessao ja esquecida.
/// ======================================================================================================
///
/// ============================ A SALA TEM CLIMA, E ISSO FOI ESCOLHIDO ============================
/// **NAO TIRE.** Ordem do dono, literal: *"mantenha o clima na sala do tempo"*.
///
/// A ficha da Sala em `planetas.json` traz `temclima: true` com oito climas (chuva, neve, neblina,
/// tempestade, smog, chuva de sangue, nevasca, tempestade de areia), e o ciclo natural roda la
/// dentro como em qualquer planeta -- `Clima.DaZona` so pergunta ao catalogo, e nao conhece a Sala.
///
/// PARECE ERRADO, E POR ISSO ESTA ESCRITO AQUI. A Sala e um quarto branco no meio de um vazio
/// branco infinito (<see cref="Jandirus.Core.World.SalaDoTempo.SemBorda"/>), e ver nevasca caindo
/// nesse vazio da vontade de "consertar" -- e o conserto obvio seria uma linha em `Clima.DaZona`
/// ("interior e a Sala nao tem ceu") ou um `temclima: false` no JSON. As duas apagariam a decisao
/// do dono em silencio, e ninguem descobriria olhando o codigo por que o ceu de la ficou limpo.
///
/// A bancada `--salateste` cobra isto (secao 18): a decisao virou checagem justamente pra que o
/// "conserto" reprove em vez de passar despercebido.
/// ==============================================================================================
///
/// A SESSAO, A COMIDA E A PRISAO MORAM EM `GameServer.SalaSessao.cs` -- este arquivo continua sendo
/// A PORTA (quem entra, quem autoriza, quem solta). A divisao e a mesma que separa `Interacao` de
/// `Tech`: aqui se decide QUEM PASSA, la se conta o tempo de quem ja passou.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O NOME DA ZONA DA SALA -- o mesmo do manifesto de mapas (z13), que e o nome da AREA do .dmm.
	///
	/// NAO E O NOME DA FICHA DE PLANETA: o `planetas.json` a chama de "Hyperbolic Time Dimension"
	/// (o nome que o `switch(Planet)` do DM usa), e quem casa os dois e o apelido em
	/// `CatalogoDePlanetas.Apelido`. Ja foi consertado, e e por isso que a gravidade aqui e 10 e
	/// nao 1 -- conferido pela bancada `AssetPipeline -- gravidade`.
	///
	/// A STRING SAIU DAQUI. Ela virou <see cref="Jandirus.Core.World.SalaDoTempo.Zona"/> quando o
	/// vazio procedural (13.3) fez o CLIENTE precisar da mesma resposta -- ele pinta o branco em
	/// volta e liga o `SemBorda` da colisao pelo mesmo criterio. O gate da porta continua lendo
	/// daqui; e o mesmo nome, e agora ha uma casa so.
	/// </summary>
	public const string ZonaDaSala = Jandirus.Core.World.SalaDoTempo.Zona;

	/// <summary>O tipo da obra que e a porta. Sai do catalogo (`construcoes.json`).</summary>
	private const string TipoDaPorta = "Time_Chamber_Door";

	/// <summary>
	/// LOTACAO: DUAS PESSOAS. **Regra do dono, e ela DIVERGE do original de proposito** -- no DM
	/// nao ha limite nenhum (`TimeChamber.dm` nao conta ninguem).
	///
	/// Dois e o numero do anime, e ele tem consequencia de jogo: a sala e o maior multiplicador de
	/// treino que existe (280x), e sem teto ela viraria o lugar onde o servidor inteiro mora.
	/// </summary>
	private const int LotacaoDaSala = 2;

	/// <summary>
	/// A RECARGA, EM HORAS REAIS (`HTC_COOLDOWN_HOURS = 24`).
	///
	/// **REAIS e nao in-game**, e isso e do original: e um descanso do JOGADOR, nao do personagem.
	/// E ela e marcada NA ENTRADA (ver <see cref="EntrarNaSala"/>), tambem como no DM -- marcar na
	/// saida deixaria uma reentrada de graca pra quem saisse no primeiro minuto.
	/// </summary>
	private const long MsDeRecargaDaSala = 24L * 60 * 60 * 1000;

	/// <summary>
	/// ONDE A PORTA DEIXA O CORPO, em coordenadas BYOND: `HTC_ENTRY_LOC = locate(146,160,13)`.
	///
	/// As coordenadas do DM sao 1-based e o Y cresce PRA CIMA; as do port sao 0-based com o Y pra
	/// baixo. A conversao e a mesma que o `MapConverter` faz nas passagens (`x-1`, `altura-y`), e
	/// ela mora aqui em vez de virar um par de pixels cravados porque **o numero do DM e a fonte** --
	/// um pixel cravado seria a mesma coisa dita duas vezes, e a segunda envelhece calada.
	/// </summary>
	private const int EntradaByondX = 146, EntradaByondY = 160;

	/// <summary>
	/// QUEM ESTA DENTRO -- por ASSINATURA de personagem, e nao por id de corpo.
	///
	/// ============================ POR QUE NAO BASTA CONTAR QUEM ESTA NA ZONA ============================
	/// A regra do dono (13.6a) diz que **quem cai continua ocupando a vaga**, e essa e a resposta
	/// coerente: o estado da sessao persiste, entao a vaga so vaga quando a sessao dele termina.
	/// Contar `ZoneList` responderia outra pergunta -- "quantos corpos estao desenhados la dentro
	/// agora" --, e um jogador que perde a conexao sumiria de `_players` liberando a vaga na hora.
	///
	/// A ASSINATURA sobrevive ao logout porque ela e (conta, slot) e nao um id de sessao. E a
	/// mesma chave do discipulado e do convivio, pelo mesmo motivo.
	///
	/// ============================ A SESSAO CHEGOU, E ESTE DICIONARIO NAO VIROU ESPELHO DELA ============================
	/// Este texto prometia *"quando a SESSAO existir (13.5), este dicionario vira o espelho vivo
	/// dela"*. A sessao existe (`GameServer.SalaSessao.cs`, bancada `--salateste`) e o que ela
	/// guarda -- dias contados, idade, prazo da prisao -- **esta no disco**, em
	/// `ServerPlayer.SalaDiasDaSessao` e companhia.
	///
	/// A METADE QUE NAO SE CUMPRIU foi de proposito, e nao por esquecimento: este dicionario
	/// responde outra pergunta ("quem ocupa vaga agora", inclusive quem caiu) e continua EM
	/// MEMORIA. Um reinicio de servidor esvazia a sala, que e o mesmo que o DM faz (`htc_inside` e
	/// `tmp`) -- e a vaga volta sozinha no login, porque <see cref="SalaDoTempoNoLogin"/> re-ocupa
	/// quem acorda dentro da zona. A sessao dele, essa, continua correndo do disco: os dois estados
	/// se reencontram sem que nenhum precise ser espelho do outro.
	/// ============================================================================================================
	/// ================================================================================================
	/// </summary>
	private readonly Dictionary<string, long> _naSala = new(StringComparer.Ordinal);

	// =====================================================================
	// O CANAL DE VERBOS
	// =====================================================================
	/// <summary>
	/// Trata os verbos da sala. Devolve `true` quando o comando era deste canal -- inclusive quando
	/// ele foi RECUSADO, pelo mesmo contrato do resto do despacho.
	/// </summary>
	private bool ComandoDaSalaDoTempo(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "sala_entrar": EntrarNaSala(pl); return true;
			case "sala_regras": RegrasDaPorta(pl); return true;
			case "sala_comer": ComerNaSala(pl); return true;
			case "sala_autorizar": AutorizarEntrada(pl, arg); return true;
			case "sala_soltar": SoltarDaSala(pl, arg, porAdmin: false); return true;
			default: return false;
		}
	}

	// =====================================================================
	// A PORTA
	// =====================================================================
	/// <summary>
	/// A TENTATIVA DE ENTRAR -- o `htc_try_enter()` do DM (`TimeChamber.dm:52-71`), com as duas
	/// regras do dono somadas (lotacao e prisao).
	///
	/// A ORDEM DAS RECUSAS E A MENSAGEM QUE O JOGADOR LE, e por isso ela e deliberada: primeiro o
	/// que e culpa do corpo (morto, nocauteado, voando), depois o que e culpa da porta (prisao,
	/// lotacao), e so entao o que exige alguem (autorizacao) ou o relogio (recarga). Um jogador sem
	/// autorizacao E sem recarga precisa ouvir primeiro que falta a chave, senao ele espera 24 h
	/// pra descobrir que faltava o Guardiao.
	/// </summary>
	private void EntrarNaSala(ServerPlayer pl)
	{
		// PERTO DA PORTA? A pergunta e do servidor. O menu do cliente le a mesma tabela
		// (`Interacoes`), mas esconder botao nunca foi permissao -- um cliente mexido manda o verbo
		// de qualquer canto do mapa.
		if (ObraQueAceita(pl, "sala_entrar") == null)
		{
			Avisar(pl, "não há porta da Sala do Tempo por perto.");
			return;
		}

		// O `if(!client || dead || KO) return` do DM, e o `if(!M.flight)` do turf. Voar tem peso
		// novo aqui: no BYOND `flight` era um booleano, e neste port o voo tem ALTITUDE -- entrar
		// no ar significaria aparecer la dentro a dez tiles do chao.
		if (pl.Ficha.dead) { Avisar(pl, "mortos não treinam."); return; }
		if (pl.Ficha.KO) { Avisar(pl, "você mal se sustenta de pé. Levante primeiro."); return; }
		if (pl.Voando) { Avisar(pl, "pouse antes de atravessar a porta."); return; }

		if (pl.SalaPreso)
		{
			Avisar(pl, "a porta não se abre pra você: sua sessão passou do limite e você ficou "
					   + "preso lá dentro. Só o Guardião da Terra (ou um administrador) solta.");
			return;
		}

		// A LOTACAO ANTES DA AUTORIZACAO: gastar a chave do Guardiao numa porta cheia seria queimar
		// uma autorizacao que ele so da uma vez.
		int dentro = QuantosNaSala();
		if (dentro >= LotacaoDaSala)
		{
			Avisar(pl, $"a Sala do Tempo já tem {dentro} pessoa(s) dentro, que é o máximo. "
					   + "Espere alguém sair.");
			return;
		}

		// A AUTORIZACAO DO GUARDIAO (`permission = 1` em `EarthRanks.dm`), e ela vale pra UMA
		// entrada -- e consumida logo abaixo.
		if (!pl.SalaAutorizada)
		{
			Avisar(pl, "uma barreira mágica bloqueia a porta. Só o Guardião da Terra pode "
					   + "autorizar a sua entrada na Sala do Tempo.");
			return;
		}

		long agora = NowMs();
		long falta = pl.SalaUltimaEntrada + MsDeRecargaDaSala - agora;
		if (pl.SalaUltimaEntrada > 0 && falta > 0)
		{
			// O DM mostra horas e minutos, e isso importa: "ainda não" faria o jogador voltar de
			// dez em dez minutos pra descobrir quando.
			long h = falta / 3_600_000, m = falta % 3_600_000 / 60_000;
			Avisar(pl, $"seu corpo ainda não se recuperou da distorção temporal. Você poderá "
					   + $"entrar de novo em {h}h{m:00}m (tempo real).");
			return;
		}

		if (_catalogo?.Get(ZoneKey.Premade(ZonaDaSala)) is not { } ficha)
		{
			// FALHAR ALTO. Sem a zona no manifesto a porta nao tem pra onde levar, e mover o corpo
			// pra uma zona que o servidor nao sabe carregar e pior que recusar.
			Avisar(pl, "a porta não se abre. (A Sala do Tempo não está no manifesto de mapas.)");
			GD.PushError($"[server] sala do tempo: a zona '{ZonaDaSala}' nao esta no manifesto");
			return;
		}

		// A AUTORIZACAO E CONSUMIDA E A RECARGA E ARMADA **NA ENTRADA**, os dois como no DM
		// (`permission = 0; htc_last_visit = world.realtime`). Marcar a recarga na saida abriria a
		// reentrada de graca pra quem entrasse e saisse no mesmo minuto.
		pl.SalaAutorizada = false;
		pl.SalaUltimaEntrada = agora;

		// SESSAO NOVA COMECA A CONTA DO ZERO -- o `htc_session_years = 0` do DM, na mesma linha em
		// que ele arma a recarga. E aqui, e nao no tique: e a ENTRADA que abre uma sessao, e um
		// tique que zerasse sozinho apagaria a conta de quem relogou la dentro.
		pl.SalaDiasDaSessao = 0;
		pl.SalaJanelaAte = 0;
		_avisoDaSala.Remove(pl.Id);

		if (pl.Assinatura.Length > 0) _naSala[pl.Assinatura] = agora;

		const int t = ZoneCollision.TileSize;
		var destino = new Vec2((EntradaByondX - 1) * t + t / 2f,
							   (ficha.H - EntradaByondY) * t + t / 2f);

		// QUEM FICA VE ELE ENTRAR, e o aviso sai ANTES da mudanca: depois de `MoveToZone` o corpo ja
		// esta no z13, e a lista da zona seria a de dentro da sala -- a frase chegaria pra quem ele
		// acabou de deixar pra tras, nao. E o mesmo `to_chat(view(HTC_EXIT_LOC), ...)` do DM.
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o != pl) Avisar(o, $"{pl.Name} atravessa a porta da Sala do Tempo.");

		MoveToZone(pl.Id, ZoneKey.Premade(ZonaDaSala), destino);
		Avisar(pl, "você atravessa a porta da Sala do Tempo. Um ano se passa aqui a cada dia.");
		Avisar(pl, $"a sessão dura {Jandirus.Core.World.SalaDoTempo.SessaoEmDias:0} dias in-game "
				   + $"(~{Jandirus.Core.World.SalaDoTempo.SessaoEmMinutosReais:0} min) e você sai "
				   + $"{Jandirus.Core.World.SalaDoTempo.SessaoEmDias * Jandirus.Core.World.SalaDoTempo.AnosPorDia:0} "
				   + "anos mais velho. Depois disso a porta só aceita você por 2 minutos.");

		// A COMIDA NASCE COM A SESSAO (13.6b). Ver `ReporComidaDaSala`.
		AbastecerComidaDaSala();

		// ============================ O NUMERO, NA HORA EM QUE ELE PASSA A VALER ============================
		// O `MoveToZone` acabou de chamar o `AplicarGravidade`, que e quem escreve os dois ritmos --
		// entao a ficha aqui ja e a de DENTRO, e o numero e o de verdade e nao uma promessa.
		//
		// A aba Stats mostra o multiplicador completo (13.4 item 5), mas ela exige apertar P: a linha
		// que faz o jogador ENTENDER o que acabou de acontecer com ele e esta, no instante da porta.
		// Sem ela, a Sala rende 280x e o unico sinal disso e o BP subir mais rapido -- que e
		// exatamente a queixa que abriu esta camada.
		// ==================================================================================================
		Avisar(pl, $"o chão puxa {pl.Ficha.Planetgrav:0.##}x e o tempo comprime o treino em "
				   + $"{pl.Ficha.zoneGainMult:0}x: aqui você rende {pl.Ficha.MultiplicadorDeGanho():0}x "
				   + "o que renderia lá fora. (A maestria de forma sobe 4x -- ela é costume, não tempo.)");
		GD.Print($"[server] {pl.Name} entrou na Sala do Tempo ({dentro + 1}/{LotacaoDaSala} dentro)");
	}

	/// <summary>
	/// O AVISO DA PORTA -- e ele e metade da decisao sobre o risco da prisao (ver o cabecalho).
	///
	/// ELE E UMA ACAO DO MENU, e nao uma mensagem que aparece depois de entrar: um custo que so se
	/// descobre do lado de dentro nao e um custo, e uma pegadinha.
	/// </summary>
	private void RegrasDaPorta(ServerPlayer pl)
	{
		if (ObraQueAceita(pl, "sala_regras") == null)
		{
			Avisar(pl, "não há porta da Sala do Tempo por perto.");
			return;
		}

		Avisar(pl, "-- A PORTA DA SALA DO TEMPO --");
		Avisar(pl, "  o chão puxa 10x mais forte, e um dia lá dentro vale um ano de treino.");
		Avisar(pl, $"  cabem {LotacaoDaSala} pessoas por vez, e o Guardião da Terra precisa autorizar "
				   + "a sua entrada.");
		Avisar(pl, "  depois de entrar, seu corpo leva 24 horas REAIS pra aguentar de novo.");
		Avisar(pl, "  a sessão rende por 2 dias in-game (~48 min). Depois disso a comida some, os");
		Avisar(pl, "  bônus acabam, e VOCÊ TEM 2 MINUTOS PRA SAIR PELA PORTA.");
		Avisar(pl, "  ATENÇÃO: passando disso a porta deixa de aceitar você. Você fica PRESO, e só");
		Avisar(pl, "  o Guardião da Terra -- ou um administrador -- solta.");

		// ============================ O PRECO DA SAIDA TEM QUE SER LIDO ANTES ============================
		// A outra saida da prisao e MORRER (decisao do dono -- ver o cabecalho), e ela e cara: karma,
		// o julgamento do Enma, o debuff de revive por Zeni, idade. Um custo que so se descobre do
		// lado de dentro nao e custo, e pegadinha -- que e a razao de este aviso ser uma acao do
		// MENU DA PORTA, e nao uma mensagem de boas-vindas. Quem vai ser preso tem que saber, antes
		// de atravessar, que o unico jeito de sair sozinho e morrendo.
		// =============================================================================================
		Avisar(pl, "  preso e sem ninguém pra soltar, só resta MORRER pra sair -- e morrer cobra o");
		Avisar(pl, "  preço de sempre (karma, o julgamento do Enma, o revive por Zeni, idade).");

		string dono = DonoDoCargo("guardian");
		Avisar(pl, dono.Length > 0
			? $"  o Guardião da Terra agora é {dono}."
			: "  não há Guardião da Terra no momento: só um administrador poderia soltar você.");
	}

	// =====================================================================
	// OS DOIS VERBOS QUE NASCEM JUNTOS: AUTORIZAR E SOLTAR
	// =====================================================================
	/// <summary>
	/// O `verb/Permission` do Guardiao (`EarthRanks.dm`): ele marca alguem, e aquele alguem ganha
	/// UMA entrada.
	///
	/// AUTORIZAR E SOLTAR NASCEM JUNTOS porque a prisao so e justa se a chave e o resgate estiverem
	/// na mesma mao. O cargo ja existia em `Core/Ranks/Ranks.cs` -- e a descricao dele ja prometia
	/// isto ("Faz chaves da Sala do Tempo e autoriza a entrada") -- mas nenhum dos dois verbos
	/// existia. Era promessa escrita e nao ligada.
	///
	/// ============================ O PORTAO E A SKILL, E NAO O TRONO ============================
	/// Isto mudou quando a dadiva dos cargos passou a existir de verdade
	/// (<see cref="DadivaDeCargo"/>), e a mudanca corrige um erro visivel do jogo:
	///
	///   * no DM quem autoriza e quem TEM `/datum/skill/rank/Permission` -- a skill assina o verb
	///     ao ser aprendida e o TIRA ao ser esquecida (`EarthRanks.dm:106-110`);
	///   * e o `growbranches()` da Terra a liga para DOIS cargos, nao um: o Guardiao (`:22`) **e o
	///     Mestre Korin** (`:33`). O `Concede` do Korin sempre disse isso em texto
	///     ("Autorizar Sala do Tempo", `Ranks.cs:350`) e o port respondia a ele "só o Guardião da
	///     Terra abre a Sala do Tempo" -- um botao que o cargo entrega e a Sala recusa.
	///
	/// Perguntar pela skill fecha os dois lados de uma vez: quem perde o cargo perde a skill pela
	/// reconciliacao, e o portao se fecha sozinho sem que ninguem precise lembrar de fecha-lo aqui.
	///
	/// O <see cref="SoltarDaSala"/> continua no <see cref="EhGuardiao"/> de proposito: soltar quem
	/// ficou preso NAO EXISTE no DM (a prisao e regra do dono deste port), entao nao ha skill do
	/// original para consultar, e a valvula e do Guardiao e do admin.
	/// =========================================================================================
	/// </summary>
	private void AutorizarEntrada(ServerPlayer pl, string arg)
	{
		if (pl.Livro == null || !pl.Livro.Sabe(SkillDaChaveDaSala))
		{
			Avisar(pl, "você não sabe fazer a chave da Sala do Tempo. "
					 + "Só o Guardião da Terra e o Mestre Korin fazem.");
			return;
		}

		ServerPlayer? o = PorNome(arg);
		if (o == null || !EhPessoa(o))
		{
			Avisar(pl, "marque alguém antes (duplo clique nele) pra autorizar a entrada.");
			return;
		}

		if (o.SalaAutorizada) { Avisar(pl, $"{o.Name} já está autorizado."); return; }

		o.SalaAutorizada = true;
		Avisar(pl, $"você faz a chave da Sala do Tempo pra {o.Name}. Ela vale por UMA entrada.");
		Avisar(o, $"{pl.Name} autorizou a sua entrada na Sala do Tempo. A chave vale uma vez só.");
	}

	/// <summary>
	/// SOLTAR QUEM FICOU PRESO. Duas portas pro mesmo ato, e as duas sao a decisao do risco:
	///
	///   * o GUARDIAO -- a regra do dono, e o caminho normal;
	///   * o ADMIN (`admin_sala_soltar`) -- a valvula, e ela existe porque um jogador preso sem
	///     Guardiao online e um jogador que **nao pode jogar**. O admin nao depende de ninguem
	///     estar de pe no Templo.
	///
	/// SOLTAR NAO E TELEPORTAR: ele so tira a tranca. Quem estava preso continua onde estava e sai
	/// pela porta, andando -- e assim o resgate nao vira um teleporte de graca pra quem quisesse
	/// usar a sala como atalho.
	/// </summary>
	private void SoltarDaSala(ServerPlayer pl, string arg, bool porAdmin)
	{
		if (!porAdmin && !EhGuardiao(pl))
		{
			Avisar(pl, "só o Guardião da Terra tira alguém da Sala do Tempo.");
			return;
		}

		ServerPlayer? o = PorNome(arg);
		if (o == null || !EhPessoa(o))
		{
			Avisar(pl, "marque alguém antes (duplo clique nele) pra soltar.");
			return;
		}

		if (!o.SalaPreso) { Avisar(pl, $"{o.Name} não está preso na Sala do Tempo."); return; }

		o.SalaPreso = false;
		Avisar(pl, $"você destranca a porta pra {o.Name}.");
		Avisar(o, porAdmin
			? "a porta da Sala do Tempo se destranca sozinha. Saia enquanto pode."
			: $"{pl.Name} destranca a porta da Sala do Tempo. Saia enquanto pode.");
		GD.Print($"[server] sala do tempo: {pl.Name} soltou {o.Name}"
				 + (porAdmin ? " (por admin -- a valvula do risco)" : ""));
	}

	/// <summary>O caminho de admin, chamado do despacho de admin (ver `GameServer.Admin.cs`).</summary>
	private void AdminSoltarDaSala(ServerPlayer adm, string arg) => SoltarDaSala(adm, arg, porAdmin: true);

	/// <summary>
	/// MORRER TIRA VOCE DA SALA -- **a saida cara, e ela e a decisao do dono** (ver o cabecalho).
	///
	/// ============================ O QUE ESTE METODO FAZ, E O QUE ELE NAO FAZ ============================
	/// Ele NAO move ninguem: quem move e o `IrProAlem` (que leva o morto pro Outro Mundo) e, um
	/// minuto depois, o `Renascer` (que o poe no berco pelo funil de sempre). Nenhum dos dois
	/// pergunta nada a Sala. O caminho da morte atravessa a tranca porque nao ha gate nele -- e a
	/// decisao e que continue assim.
	///
	/// O QUE ELE FAZ E LIMPAR O ESTADO, que e a metade que faltava e que ninguem veria faltar: o
	/// `SalaPreso` vai pro DISCO (`CharacterStore`). Sem esta limpeza o morto renasce na Terra
	/// **ainda marcado como preso** -- e ai a porta o recusa ("sua sessao passou do limite"), pra
	/// sempre, num planeta onde ele nem esta. O sintoma apareceria dias depois, sem nada ligando a
	/// causa ao efeito, e o unico jeito de desfazer seria um admin usando a valvula de solta num
	/// jogador que nao esta preso em lugar nenhum.
	///
	/// A JANELA some junto pelo mesmo motivo: ela e um relogio de dentro da Sala, e um relogio de
	/// dentro correndo num corpo que esta na Terra so pode dar resposta errada.
	///
	/// A CONTA DE DIAS **NAO** SE APAGA AQUI, de proposito. Morrer nao devolve a sessao gasta -- a
	/// regra e a mesma de sair pela porta (ver `EsquecerSessaoDaSala`): quem gastou dia gastou, e
	/// quem zera a conta e a proxima ENTRADA. A recarga de 24 h tambem fica de pe; se morrer
	/// devolvesse a recarga, morrer viraria o atalho barato pra reentrar na sala.
	///
	/// A VAGA (`_naSala`) TAMBEM NAO SE MEXE AQUI: quem a devolve e o <see cref="TickDaSalaDoTempo"/>,
	/// que pergunta "este corpo ainda esta no z13?" -- e depois do `MandarProBerco` a resposta e nao.
	/// Escrever a remocao aqui seria a mesma regra em dois lugares, e o segundo envelheceria calado.
	/// ================================================================================================
	/// </summary>
	private void AMorteSaiDaSala(ServerPlayer pl)
	{
		if (!pl.SalaPreso && pl.SalaJanelaAte == 0) return;

		bool estavaPreso = pl.SalaPreso;
		pl.SalaPreso = false;
		pl.SalaJanelaAte = 0;
		EsquecerSessaoDaSala(pl.Id);

		if (!estavaPreso) return;

		// A FRASE EXISTE PORQUE O PRECO PRECISA SER LEGIVEL. Quem morreu pra sair tem que entender
		// que foi isso que aconteceu -- senao a unica saida da prisao continua parecendo um acaso.
		Avisar(pl, "a morte te tira da Sala do Tempo. Era o único caminho que restava -- e ele "
				   + "cobrou o preço de sempre.");
		GD.Print($"[server] sala do tempo: {pl.Name} saiu MORRENDO (a saida cara -- decisao do dono)");
	}

	// =====================================================================
	// A CONTA DE QUEM ESTA DENTRO
	// =====================================================================
	/// <summary>Quantas pessoas ocupam a sala agora -- inclusive quem caiu. Ver <see cref="_naSala"/>.</summary>
	private int QuantosNaSala() => _naSala.Count;

	/// <summary>
	/// A VAGA VAGA QUANDO A PESSOA SAI DA ZONA, e nao quando ela desloga.
	///
	/// Roda no tique cheio junto das passagens porque e exatamente a saida pela passagem
	/// (`fromhbtc`, as celulas 144..147 x 338 do z13) que tira alguem de la -- e o corpo pode ser
	/// levado pra fora por outras vias tambem (a MORTE e o admin; o `Goto Spawn` do jogador deixou
	/// de existir, ver o cabecalho). A pergunta e sempre a mesma: este corpo ainda esta no z13?
	///
	/// QUEM NAO TEM CORPO VIVO CONTINUA CONTANDO. E o `if(!client) continue` do DM virando regra de
	/// lotacao: desconectar la dentro nao devolve a vaga (13.6a).
	/// </summary>
	private void TickDaSalaDoTempo()
	{
		if (_naSala.Count == 0) return;

		foreach (string assinatura in _naSala.Keys.ToList())
		{
			ServerPlayer? corpo = null;
			foreach (ServerPlayer p in _players.Values)
				if (string.Equals(p.Assinatura, assinatura, StringComparison.Ordinal)) { corpo = p; break; }

			// sem corpo no mundo = deslogado la dentro: a vaga continua dele
			if (corpo == null) continue;
			if (string.Equals(corpo.Zone.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase)) continue;

			_naSala.Remove(assinatura);
			GD.Print($"[server] sala do tempo: {corpo.Name} saiu ({_naSala.Count}/{LotacaoDaSala} dentro)");
		}
	}

	/// <summary>
	/// O `htc_login_check()` do DM: quem entra no mundo JA DENTRO da sala volta a ocupar a vaga.
	///
	/// Sem isto, relogar seria o jeito de liberar a vaga sem sair -- e a lotacao viraria uma
	/// sugestao. Chamado do caminho de login, depois de a zona do personagem estar montada.
	/// </summary>
	private void SalaDoTempoNoLogin(ServerPlayer pl)
	{
		if (!string.Equals(pl.Zone.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase)) return;
		if (pl.Assinatura.Length == 0) return;
		_naSala.TryAdd(pl.Assinatura, pl.SalaUltimaEntrada > 0 ? pl.SalaUltimaEntrada : NowMs());

		// ============================ A MESA SE POE DE NOVO NO LOGIN ============================
		// A comida vive so em memoria (como as macas), entao um REINICIO de servidor a apaga -- e a
		// sessao, essa, esta no disco e continua correndo. Sem esta linha, quem voltasse depois de um
		// reinicio passaria o resto dos 48 minutos sem nada pra comer, sem nada explicando por que.
		//
		// E ISTO NAO E O TEMPORIZADOR VOLTANDO PELA JANELA: o gatilho e o LOGIN de alguem com sessao
		// viva, que e um gesto, e o teto de duas porcoes continua sendo o mesmo. Ver
		// `GameServer.SalaSessao.cs`.
		if (Jandirus.Core.World.SalaDoTempo.SessaoValendo(pl.SalaDiasDaSessao)) AbastecerComidaDaSala();
	}

	/// <summary>
	/// A SKILL QUE FAZ A CHAVE -- `/datum/skill/rank/Permission` (`EarthRanks.dm:93`), que os kits
	/// do Guardiao e do Korin concedem em `Core/Ranks/DadivaDeCargo.cs`.
	///
	/// O typepath mora numa constante e nao solto no `if` porque ele e o unico fio entre a dadiva e
	/// esta porta: escrito duas vezes, uma das duas envelhece calada.
	/// </summary>
	private const string SkillDaChaveDaSala = "/datum/skill/rank/Permission";

	/// <summary>Esta conta ocupa o trono do Guardiao da Terra?</summary>
	private bool EhGuardiao(ServerPlayer pl) =>
		string.Equals(CargoDe(pl.Conta), "guardian", StringComparison.OrdinalIgnoreCase);

	/// <summary>O nome de quem ocupa um cargo, ou "" se o trono esta vago.</summary>
	private string DonoDoCargo(string chave)
	{
		if (!_tronos.TryGetValue(chave, out string? conta) || conta.Length == 0) return "";
		foreach (ServerPlayer p in _players.Values)
			if (string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase)) return p.Name;
		return conta;
	}
}
