using Godot;
using Jandirus.Core;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O LADO DO CLIENTE NO APERTO DE MAO DE VERSAO.
///
/// O dono (2026-10-11): *"ao entrar em um server o server checa a versao do cliente / cliente checa a versao do
/// server, e se n bater o jogo manda pro menu avisando q o cliente ta em alguma versao na frente ou atras do
/// server"*.
///
/// ============================ OS QUATRO DESFECHOS DE UM "ENTRAR" ============================
///   1. O SERVIDOR RECUSA o pedido de conexao e manda a versao dele na carga: o cliente compara com a propria e
///      cai no menu com a frase e a DIRECAO (<see cref="MotivoDeVersao"/>). E o caso de todo dia.
///   2. O SERVIDOR ACEITA e a versao dele chega no primeiro pacote (`S2C.Versao`): o cliente CONFERE POR CONTA
///      PROPRIA, e so entao manda o login (<see cref="AoChegarAVersaoDoServidor"/>). Se nao bater -- um
///      servidor que aceita qualquer um --, ele mesmo desliga, com a mesma frase.
///   3. O SERVIDOR ACEITA e NAO DIZ VERSAO NENHUMA: e um servidor de antes da checagem (a porta dele so olha a
///      senha, e aceita o cliente novo). O login NAO sai; depois de <see cref="PrazoDaVersaoMs"/> o cliente
///      desliga e avisa "servidor sem versao (antigo)" (<see cref="VigiarOPrazoDaVersao"/>). Sem o prazo, o
///      jogador ficaria em "conectando..." pra sempre.
///   4. A carga da recusa vem vazia, truncada ou de lixo: aviso GENERICO ("o servidor recusou a conexao"), sem
///      excecao -- o leitor do bloco nunca lanca.
///
/// TODOS CAEM NO MESMO CAMINHO DA QUEDA: `QuedaPendente` acende e quem leva o jogador ao menu e mostra o painel
/// e o `Boot` (`Boot.Queda.cs`). O que este arquivo acrescenta e o AVISO de versao, que o `Boot` toma com
/// <see cref="TomarAvisoDeVersao"/> pra trocar o titulo e a frase do painel.
///
/// O LOGIN SO SAI DEPOIS DA VERSAO CONFERIDA: nenhum pacote de jogo -- nem a conta e a senha -- vai pra um
/// servidor de outro build.
/// ============================================================================================
/// </summary>
public partial class GameClient
{
	/// <summary>
	/// Quanto o cliente espera, depois de CONECTADO, o servidor dizer a versao dele.
	///
	/// TRES SEGUNDOS SAO FOLGA, e nao aposta: o servidor escreve a versao no MESMO tratador em que aceita o
	/// pedido (`GameServer.AoPedirConexao`), entao ela sai junto do aceite e chega logo atras do
	/// `PeerConnected` daqui -- o atraso do quadro do servidor ja esta pago quando a espera comeca. O que este
	/// prazo cobre e a retransmissao de um pacote perdido. Quem estoura e o servidor que NUNCA manda.
	/// </summary>
	public const int PrazoDaVersaoMs = 3000;

	/// <summary>
	/// SO PRA BANCADA: a identidade que ESTE cliente apresenta (nulo = a do processo). Ver
	/// `GameServer.VersaoDeTeste`, a irma dela do outro lado do fio.
	/// </summary>
	internal IdentidadeDeBuild? VersaoDeTeste;

	/// <summary>O que este cliente diz de si: versao + impressao digital do protocolo.</summary>
	internal IdentidadeDeBuild MinhaVersao => VersaoDeTeste ?? FioDaVersao.DoProcesso;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagversao`): o cliente nao confere a versao que o servidor manda no
	/// primeiro pacote -- aceita o que vier e manda o login. Contra um servidor que tambem nao confere, ele
	/// ENTRA em outro build. Lido pela propria regra, em <see cref="AoChegarAVersaoDoServidor"/>.
	/// </summary>
	internal static bool NaoConfereOServidorDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagversao`): a espera pela versao do servidor nao vence nunca -- contra um
	/// servidor antigo o cliente fica conectado, mudo, em "conectando...". Lido pela propria regra, em
	/// <see cref="VigiarOPrazoDaVersao"/>.
	/// </summary>
	internal static bool SemPrazoDaVersaoDeTeste;

	/// <summary>Em que pe esta o aperto de mao desta conexao.</summary>
	private enum Aperto : byte
	{
		/// <summary>Sem conexao em curso (ou ja encerrada).</summary>
		Nenhum,
		/// <summary>O pedido de conexao saiu, com a minha versao dentro.</summary>
		Discando,
		/// <summary>Conectado: esperando o `S2C.Versao`. O login esta SEGURO na mao.</summary>
		EsperandoAVersao,
		/// <summary>A versao do servidor bateu com a minha; o login foi mandado.</summary>
		Conferido,
	}

	private Aperto _aperto;

	/// <summary>A hora (`Time.GetTicksMsec`) em que a espera pela versao do servidor vence.</summary>
	private ulong _prazoDaVersao;

	/// <summary>O aviso de versao da queda que esta pendente, ou nulo (queda comum). Ver <see cref="TomarAvisoDeVersao"/>.</summary>
	private Versao.Aviso? _avisoDeVersao;

	/// <summary>
	/// O AVISO DE VERSAO DA QUEDA PENDENTE -- e ele sai da mao de quem pergunta: o `Boot` o toma UMA vez, junto
	/// do `TratarQueda`, e a queda seguinte (uma comum) nao herda o titulo desta.
	/// </summary>
	public Versao.Aviso? TomarAvisoDeVersao()
	{
		Versao.Aviso? aviso = _avisoDeVersao;
		_avisoDeVersao = null;
		return aviso;
	}

	/// <summary>
	/// DISCA, com a senha de entrada e a MINHA VERSAO no pedido de conexao. E o que o servidor julga antes de
	/// existir conexao -- ver `FioDaVersao.PedidoDeConexao`.
	///
	/// E COM A PROVA DE ANFITRIAO, quando o alvo e o servidor que ESTE processo esta hospedando: quem a entrega e
	/// o proprio servidor, por dentro do processo, e ele so a entrega pra esse alvo (nulo pra qualquer outro --
	/// a prova nunca vai pro servidor de outra pessoa). E ela que faz de quem clicou em "Hospedar partida" o
	/// admin. Ver `GameServer.Anfitriao.cs`.
	/// </summary>
	private NetPeer Discar(string host, int port)
	{
		_aperto = Aperto.Discando;
		_avisoDeVersao = null;
		byte[]? provaDeAnfitriao = Jandirus.Server.GameServer.Instance?.ProvaDoAnfitriaoPara(host, port);
		return _net.Connect(host, port, FioDaVersao.PedidoDeConexao(MinhaVersao, provaDeAnfitriao));
	}

	/// <summary>
	/// CONECTEI -- e ainda nao mando o login. Era aqui que ele saia; agora ele espera o servidor dizer a versao
	/// (o primeiro pacote dele), com prazo.
	/// </summary>
	private void EsperarAVersaoDoServidor()
	{
		_aperto = Aperto.EsperandoAVersao;
		_prazoDaVersao = Time.GetTicksMsec() + PrazoDaVersaoMs;
	}

	/// <summary>
	/// O `S2C.Versao`: o servidor disse a versao dele. O CLIENTE CONFERE TAMBEM -- a direcao que so ele cobre e a
	/// do servidor que aceita qualquer um. Bateu: o login sai agora.
	/// </summary>
	private void AoChegarAVersaoDoServidor(NetDataReader reader)
	{
		// SO DECIDE ENQUANTO EU ESPERO: depois de conferido (ou com a conexao ja desfeita) ele nao muda nada.
		if (_aperto is not (Aperto.EsperandoAVersao or Aperto.Discando)) return;

		if (FioDaVersao.LerBloco(reader, out IdentidadeDeBuild servidor) != FioDaVersao.Leitura.Lida)
		{
			GD.Print("[client] versao: o servidor mandou uma versao ILEGIVEL -- desligando");
			CairPorVersao(null, "o servidor mandou uma versão que este jogo não entende");
			return;
		}

		if (!NaoConfereOServidorDeTeste && Versao.AvisoDoDesencontro(MinhaVersao, servidor) is { } aviso)
		{
			GD.Print($"[client] versao: o servidor ACEITOU, mas esta na {servidor.Texto} (protocolo {servidor.DigitalCurta}) e eu na "
				   + $"{MinhaVersao.Texto} (protocolo {MinhaVersao.DigitalCurta}) -- desligando: {aviso.Resumo}");
			CairPorVersao(aviso, aviso.Resumo);
			return;
		}

		_aperto = Aperto.Conferido;
		GD.Print($"[client] versao conferida com o servidor: {servidor.Texto} (protocolo {servidor.DigitalCurta})");
		MandarLogin();
	}

	/// <summary>O login na conta -- o primeiro pacote MEU, e ele so sai com a versao conferida.</summary>
	private void MandarLogin()
	{
		NetDataWriter w = Protocol.Begin(Protocol.C2S.Login);
		w.Put(_conta);
		w.Put(_senha);
		_peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// A CADA QUADRO, DEPOIS do `PollEvents` (o que chegou neste quadro ja foi lido): a espera pela versao do
	/// servidor venceu? Entao ele e um servidor ANTIGO -- aceitou so pela senha e nunca vai dizer versao.
	///
	/// POR ESTADO, como a queda: o prazo e conferido aqui e nao num temporizador solto, e uma travada deste
	/// processo nao o estoura por engano -- o pacote que chegou durante a travada e lido ANTES desta pergunta.
	/// </summary>
	private void VigiarOPrazoDaVersao()
	{
		if (_aperto != Aperto.EsperandoAVersao) return;
		if (_peer == null) { _aperto = Aperto.Nenhum; return; }   // sai (ou cai) no meio da espera: nao ha o que vigiar
		if (SemPrazoDaVersaoDeTeste || Time.GetTicksMsec() < _prazoDaVersao) return;

		Versao.Aviso aviso = Versao.AvisoDeServidorSemVersao(MinhaVersao);
		GD.Print($"[client] versao: conectado ha {PrazoDaVersaoMs} ms e o servidor nao disse a versao dele -- e um servidor antigo. Desligando");
		CairPorVersao(aviso, aviso.Resumo);
	}

	/// <summary>
	/// A CONEXAO ACABOU SEM EU PEDIR: foi uma RECUSA POR VERSAO? Entao devolve o motivo (o resumo do aviso) e
	/// guarda o aviso inteiro pro `Boot`; senao devolve nulo, e a queda segue com o motivo comum.
	///
	/// A carga e a versao do SERVIDOR (`GameServer.AoPedirConexao`). Carga vazia, truncada, de lixo -- ou uma
	/// versao IGUAL a minha, que nao explica recusa nenhuma -- cai no aviso generico: o leitor do bloco nao lanca.
	/// </summary>
	private string? MotivoDeVersao(DisconnectInfo info)
	{
		_aperto = Aperto.Nenhum;   // qualquer fim de conexao encerra a espera
		_avisoDeVersao = null;
		if (info.Reason != DisconnectReason.ConnectionRejected) return null;
		if (FioDaVersao.LerBloco(info.AdditionalData, out IdentidadeDeBuild servidor) != FioDaVersao.Leitura.Lida) return null;
		if (Versao.AvisoDoDesencontro(MinhaVersao, servidor) is not { } aviso) return null;

		_avisoDeVersao = aviso;
		GD.Print($"[client] versao: RECUSADO -- o servidor esta na {servidor.Texto} (protocolo {servidor.DigitalCurta}) e eu na "
			   + $"{MinhaVersao.Texto} (protocolo {MinhaVersao.DigitalCurta}): {aviso.Resumo}");
		return aviso.Resumo;
	}

	/// <summary>
	/// EU DESLIGO, E ISTO E QUEDA: o jogador nao pediu pra sair -- foi a versao que nao deixou entrar. Acende a
	/// mesma `QuedaPendente` de uma queda de servidor (o `Boot` leva ao menu e mostra o painel), com o aviso de
	/// versao junto quando ha um.
	/// </summary>
	private void CairPorVersao(Versao.Aviso? aviso, string motivo)
	{
		_aperto = Aperto.Nenhum;
		// A DESPEDIDA LOCAL QUE VEM A SEGUIR E MINHA, e ela nao pode virar uma SEGUNDA queda (nem apagar esta):
		// o `PeerDisconnected` dela sai pela porta do "por vontade", que nao toca na `QuedaPendente`.
		_saiuPorVontade = _peer != null;
		_peer?.Disconnect();
		_peer = null;

		_avisoDeVersao = aviso;
		QuedaPendente = motivo;
		Caiu?.Invoke(motivo);
	}
}
