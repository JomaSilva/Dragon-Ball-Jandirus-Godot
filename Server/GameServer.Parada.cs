using Godot;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// A PARADA DO SERVIDOR -- o que ela faz com QUEM ESTA CONECTADO e com o que a SESSAO deixou armado.
///
/// O dono (2026-10-11): *"sim pode consertar o stop do servidor"*; sobre os quatro achados daquela volta,
/// *"pode consertar os 4, faca a parada derrubar cada jogador"*; e sobre as tres perguntas que sobraram,
/// *"Sim clicar no x deveria salvar, o torneio deve ser cancelado nesse caso de parada, a fusao e perdoada"*.
///
/// ============================ A REGUA ============================
/// PARAR E SUBIR NO MESMO PROCESSO DEIXA O SERVIDOR IGUAL A FECHAR O JOGO E ABRIR DE NOVO. E o gesto de quem
/// hospeda, sai e hospeda de novo (`PauseMenu` -> `SalvarEParar` -> `Boot.Hospedar` -> `Start`), e nada nele
/// reinicia o processo: o que a parada nao desfaz fica de heranca pra proxima subida.
///
/// ============================ AS DUAS PARADAS ============================
///   * `SalvarEParar()` -- a de quem JOGA (o "Desconectar" de quem hospeda; o X da janela numa sessao de
///     jogador) e a do SERVIDOR DEDICADO DE PRODUCAO (o X da janela dele, e o Ctrl+C ou o fechar do console:
///     `Client/Boot.Dedicado.cs` e `Client/VigiaDoConsole.cs`). Na ordem: 1) cada conectado SAI pelo `Drop`,
///     e e o `Drop` dele que grava o personagem, uma vez, depois de desfazer o que o save nao sabe descrever
///     (<see cref="DerrubarCadaJogadorNaParada"/>); 2) o mundo, as bases e os cargos; 3) o `Stop()`.
///   * `Stop()` sozinho -- desliga SEM GRAVAR E SEM DERRUBAR NINGUEM, de proposito: e o caminho das corridas
///     de BANCADA (`Client/Saida.cs`: uma bancada mexe no mundo em memoria, e o X no meio dela nao pode
///     escrever esse estado no disco). Ele fecha a rede, joga os corpos fora e ESQUECE A SESSAO na mao
///     (<see cref="EsquecerASessaoQueAcabou"/>) -- o equivalente a matar o processo.
///
/// A SAIDA DE CADA UM TEM DE SER CHAMADA, e antes de a rede fechar: o `NetManager.Stop()` nao derruba par
/// nenhum deste lado (MEDIDO, LiteNetLib 1.3.1: ele manda a despedida pelo fio, fecha o soquete e joga fora
/// os eventos pendentes -- o `PeerDisconnected` de quem parou nunca dispara).
///
/// ============================ O QUE A PARADA FAZ COM CADA EFEITO DO `Drop` ============================
/// O `Drop` foi escrito pra UM jogador que sai enquanto o mundo CONTINUA. Na parada o mundo acaba junto, e
/// ela nao e culpa de ninguem: nada do que existe pra cobrar de quem "sai no meio da luta" pode cair em quem
/// foi derrubado por ela.
///
///   fora do corpo (mente, leme)     IGUAL: volta pro corpo antes do save (senao o save grava o bolso, ou o
///                                   vazio do espaco, como o lugar do personagem)
///   gole da Agua Ultra Divina       IGUAL: quem aguentava leva o premio (e beneficio)
///   danca, puxao ou cena de fusao   IGUAL: aborta sem fundir e sem recarga
///   FUNDIDO                         a fusao SE SEPARA (`Separar`) e cada um e gravado como PESSOA -- o dono com
///                                   o que treinou durante a fusao, o passageiro ao lado dele. **DIFERENTE num
///                                   ponto: NINGUEM PAGA A RECARGA DE 1 h** (dono: "a fusao e perdoada"). Quem
///                                   decide e o <see cref="_paradaEmCurso"/>, lido no `CobrarARecargaDeFusao`;
///                                   no logout normal o preco continua. Antes o `Persistir` recusava os dois, e
///                                   o que tinha sido feito durante a fusao se perdia
///   absorcao do Majin               IGUAL: quem esta dentro e solto e o poder emprestado zera -- e o que o
///                                   login depois de uma queda ja faz (`AcordarSemAbsorcao`)
///   ZanzoClash                      IGUAL: o proprio `Drop` desfaz o embate SEM vencedor e antes do save
///                                   (`GameServer.SaidaDoEmbate.cs`) -- ninguem e recolocado, ninguem apanha,
///                                   ninguem vai pro disco oculto
///   `Persistir`                     IGUAL, e e AQUI que cada um e gravado. O `SalvarEParar` nao grava mais
///                                   ninguem por fora: seriam duas gravacoes do mesmo corpo, em estados
///                                   diferentes
///   disputa de ki                   fecha como no logout (e o `Resolver` que solta as duas cabecas). O feixe
///                                   "vencedor" nao chega em ninguem: quem o leva e o tique, e nao ha mais tique
///   `DespedirCorpo`                 IGUAL: relogios por id, raio na mao, tecnicas, projeteis, voz, `PeerLeft`
///   quem esta na SELECAO            IGUAL: sai de `_logados`; nao ha o que gravar
///
/// O TORNEIO EM DISPUTA E CANCELADO (dono: "o torneio deve ser cancelado nesse caso de parada"), pelo mesmo
/// `EncerrarTorneio` do `trn_cancelar` do admin -- ver <see cref="EsquecerASessaoQueAcabou"/>. Ninguem perde
/// por W.O.: o que cobra ausencia e o tique, e depois da parada nao ha tique nem torneio.
///
/// SO OS CONECTADOS sao derrubados. `_players` guarda tambem os corpos sem dono (habitantes, clones, bonecos,
/// chefes), e esses somem com o `_players.Clear()` do `Stop`, como sempre.
/// =======================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--paradateste`): o `SalvarEParar` volta a NAO derrubar ninguem -- a parada de
	/// antes, que gravava cada corpo COMO ESTAVA (o fundido, recusado pelo `Persistir`) e nao passava ninguem
	/// pelo `Drop`. Lido pela propria linha do conserto, em <see cref="DerrubarCadaJogadorNaParada"/>.
	/// </summary>
	internal bool ParadaNaoDerrubaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--paradateste`): a parada cobra a recarga da fusao que ela mesma desfaz, como
	/// um logout comum. Lido pela propria linha do conserto, a que levanta o <see cref="_paradaEmCurso"/>.
	/// </summary>
	internal bool ParadaCobraAFusaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--paradateste`): o `Stop` volta a NAO esquecer a sessao -- o de antes de
	/// 2026-10-11, que deixava pra proxima subida quem estava logado, o relogio do povoamento, o desarme do
	/// admin por endereco, as vagas da Sala do Tempo e o torneio em disputa. Lido pela propria linha do
	/// conserto, em <see cref="EsquecerASessaoQueAcabou"/>.
	/// </summary>
	internal bool ParadaLembraDaSessaoDeTeste;

	/// <summary>
	/// A PARADA ESTA DERRUBANDO OS JOGADORES. Quem le e o que a parada PERDOA em quem ela derruba: hoje, a
	/// recarga da fusao que ela mesma desfaz (`CobrarARecargaDeFusao`, em `GameServer.Fusao.cs`).
	/// </summary>
	private bool _paradaEmCurso;

	/// <summary>
	/// CADA CONECTADO SAI PELO CAMINHO DE SEMPRE, com a rede ainda aberta. Chamado pelo `SalvarEParar`, antes
	/// de o mundo ser gravado. Devolve quantos personagens passaram pela saida.
	///
	/// Primeiro quem esta no MUNDO (o `Drop` inteiro: de volta ao corpo, desfundido, cuspido, solto do embate,
	/// GRAVADO, despedido), depois quem esta na SELECAO (o mesmo `Drop`, que pra ele e so deixar de estar
	/// logado).
	///
	/// UM QUE QUEBRA NAO CUSTA A SAIDA DOS OUTROS: a excecao de um jogador e dita no console e a fila segue.
	/// Ele perde o que fez desde a ultima gravacao periodica (dois minutos no maximo), como numa queda do
	/// processo -- e a conta dele nao fica presa, porque o `Stop` que vem em seguida esquece a sessao inteira.
	/// </summary>
	private int DerrubarCadaJogadorNaParada()
	{
		if (ParadaNaoDerrubaDeTeste)
		{
			int comoEstavam = 0;
			foreach (ServerPlayer p in Jogadores.ToList()) { Persistir(p); comoEstavam++; }
			return comoEstavam;
		}

		List<NetPeer> noMundo = [.. _byPeer.Keys];
		List<NetPeer> naSelecao = [.. _logados.Keys];

		_paradaEmCurso = !ParadaCobraAFusaoDeTeste;
		try
		{
			foreach (NetPeer par in noMundo.Concat(naSelecao))
			{
				try { Drop(par); }
				catch (Exception e) { GD.PushError($"[server] parada: a saida de {par.Address} quebrou, os outros seguem: {e}"); }
			}
		}
		finally
		{
			_paradaEmCurso = false;
		}
		return noMundo.Count;
	}

	/// <summary>
	/// O SERVIDOR PAROU: O QUE ERA DA SESSAO NAO PASSA PRA PROXIMA SUBIDA. Chamado pelo `Stop()`, depois de a
	/// rede fechar e junto das tres colecoes que ele ja esvaziava (`_players`, `_byPeer`, `_zones`).
	///
	/// Sao cinco estados que so existem em memoria e que o boot encontra zerados. Nenhum `Drop` os desfaz: o
	/// primeiro so e consequencia dele no `SalvarEParar`, e os outros quatro sao do MUNDO, nao de um jogador.
	/// </summary>
	private void EsquecerASessaoQueAcabou()
	{
		if (ParadaLembraDaSessaoDeTeste) return;

		// 1. QUEM ESTAVA LOGADO -- `_logados` (na selecao) e `_contas` (em jogo). Depois do `SalvarEParar` as
		// duas ja estao vazias, porque cada `Drop` tirou a sua; aqui e o que cobre o `Stop` sozinho, que nao
		// derruba ninguem, e o `Drop` que quebrou no meio. Sem isto a mesma conta nao loga de novo: o `NetPeer`
		// compara pelo ENDERECO, entao o par de quem volta do mesmo soquete e IGUAL a chave velha -- a primeira
		// linha do `Login` sai muda ("conectando..." pra sempre) e, de outro soquete, a recusa e "essa conta ja
		// esta conectada".
		_logados.Clear();
		_contas.Clear();

		// 2. O POVOAMENTO. Os habitantes sairam com o `_players.Clear()`, e o relogio da manutencao continuava
		// contando de onde estava: o mundo da proxima subida ficava SEM NINGUEM por ate cinco minutos
		// (`Povoamento.SegundosEntreManutencoes`). Zerado, a primeira volta do tique repovoa, como no boot -- e
		// com os mesmos habitantes do boot, porque o contador de lugares recomeca junto.
		_filaDoPovoamento.Clear();
		_lugaresDoPovoamento.Clear();
		_proximaManutencao = 0;
		_relogioDoPovoamento = 0;

		// 3. O ADMIN POR ENDERECO. O desarme automatico (duas contas locais ao mesmo tempo) e da SESSAO que as
		// viu e se desfaz com ela: a subida seguinte comeca com a chave armada, como o boot. O `--sem-admin-local`
		// e do processo e FICA -- quem separa os dois e o `_enderecoAmbiguo` (`GameServer.Admin.cs`).
		if (_enderecoAmbiguo)
		{
			AdminPorEndereco = true;
			_enderecoAmbiguo = false;
		}

		// 4. AS VAGAS DA SALA DO TEMPO. O `_naSala` guarda a vaga de quem caiu la dentro e so existe em memoria:
		// a propria sala escreve que "um reinicio de servidor esvazia a sala" e que a vaga volta no login
		// (`SalaDoTempoNoLogin`). Sem isto a lotacao da subida seguinte ja comecava ocupada por quem nao esta.
		_naSala.Clear();

		// 5. O TORNEIO EM DISPUTA. Ele so existe em memoria (um reinicio do processo nao tem torneio: a agenda
		// foi marcada e gravada quando ele COMECOU, entao ele nao se repete), e atravessando a parada as lutas
		// se decidiam por W.O. e dupla ausencia na subida seguinte, sem ninguem no palco. Cai pelo
		// `EncerrarTorneio` de producao -- o mesmo do `trn_cancelar` do admin e da limpeza total: solta quem
		// estava preso na area de espera, some com os lutadores convidados e fecha o chaveamento. Nao ha taxa de
		// inscricao nem aposta pra devolver, ninguem recebe premio, e nada do torneio vai pro save do
		// personagem (a trava da espera e o "nao letal" sao da sessao): quem estava na arena e gravado onde
		// estava, livre.
		if (_torneio != null) EncerrarTorneio(_torneio);
	}
}
