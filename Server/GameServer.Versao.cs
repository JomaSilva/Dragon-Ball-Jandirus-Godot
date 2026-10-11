using Godot;
using Jandirus.Core;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A PORTA DE ENTRADA DO SERVIDOR: a senha de sempre E A VERSAO.
///
/// O dono (2026-10-11): *"ao entrar em um server o server checa a versao do cliente / cliente checa a versao do
/// server, e se n bater o jogo manda pro menu avisando q o cliente ta em alguma versao na frente ou atras do
/// server"*.
///
/// ============================ O QUE HAVIA ============================
/// `req.AcceptIfKey(Protocol.ConnectionKey)`: uma senha fixa e mais nada. O protocolo dizia, de proposito, "SEM
/// BUMP DE VERSAO ... cliente e servidor sobem juntos" -- e isso deixou de ser verdade no dia em que passou a
/// haver servidor dedicado e jogador com build de outro dia. O cliente desencontrado ENTRAVA, e quebrava depois:
/// pacote lido torto, corpo sem ficha, botao que o servidor nao conhece.
///
/// ============================ O QUE HA AGORA ============================
/// A checagem acontece NO PEDIDO DE CONEXAO, antes de existir par, login ou pacote de jogo:
///
///   * senha errada (ou pedido ilegivel)        -> recusa seca, como sempre foi;
///   * senha certa e NADA depois (cliente antigo) -> recusa, com a versao DESTE servidor na carga (ele nao sabe
///     ler, e mostra o aviso generico dele) e uma linha clara no log daqui;
///   * versao menor, maior, ou outro protocolo  -> recusa COM A VERSAO DESTE SERVIDOR NA CARGA: e com ela que o
///     cliente escreve "o seu jogo esta ATRASADO/ADIANTADO" no menu;
///   * tudo igual                                -> aceita, e o PRIMEIRO pacote do par e a versao daqui
///     (`S2C.Versao`): o cliente confere por conta propria antes de mandar o login.
///
/// O juizo e uma funcao pura (`FioDaVersao.JulgarPedido`); aqui mora so o efeito. QUEM HOSPEDA PASSA DIRETO sem
/// excecao nenhuma no codigo: o cliente e o servidor do mesmo processo leem a mesma `FioDaVersao.DoProcesso`.
/// Por que isto nao e (nem pretende ser) seguranca esta no cabecalho do `FioDaVersao`.
/// =========================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// SO PRA BANCADA: a identidade que ESTE servidor apresenta (nulo = a do processo). E o jeito de por o
	/// servidor "na frente" ou "atras" do cliente quando os dois moram no mesmo processo (`--diagversao`,
	/// `--diagqueda`, `--versaoteste`).
	/// </summary>
	internal IdentidadeDeBuild? VersaoDeTeste;

	/// <summary>O que este servidor diz de si: versao + impressao digital do protocolo.</summary>
	internal IdentidadeDeBuild MinhaVersao => VersaoDeTeste ?? FioDaVersao.DoProcesso;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--versaoteste` e `--diagversao`; flag `--servidorantigo`): a porta de ANTES de
	/// 2026-10-11 -- so a senha, e a versao nao viaja em lugar nenhum. E o "servidor antigo" que o cliente novo
	/// tem de perceber sozinho. Lido pela propria porta, em <see cref="AoPedirConexao"/>.
	/// </summary>
	internal bool PortaAntigaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--versaoteste` e `--diagversao`; flag `--servidoraceitatudo`): o servidor nao
	/// compara a versao -- aceita quem tiver a senha e ainda assim diz a dele. E o servidor que so o CLIENTE
	/// consegue barrar. Lido pela propria porta, em <see cref="AoPedirConexao"/>.
	/// </summary>
	internal bool AceitaQualquerVersaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--versaoteste` e `--diagversao`): a recusa por versao leva ESTES bytes no
	/// lugar da versao do servidor -- vazio (o cliente fica sabendo que foi recusado e nao sabe dizer se esta
	/// atrasado ou adiantado), um bloco truncado, ou lixo. Lido pela propria porta, em <see cref="AoPedirConexao"/>.
	/// </summary>
	internal byte[]? CargaDaRecusaDeTeste;

	/// <summary>O ULTIMO pedido de conexao julgado: o juizo, o que o cliente disse de si e a linha que foi (ou iria) pro log.</summary>
	internal (FioDaVersao.JuizoDoPedido Juizo, IdentidadeDeBuild Cliente, string Linha)? UltimoPedidoDeConexao { get; private set; }

	/// <summary>Quantos pedidos de conexao este servidor recusou POR VERSAO desde que subiu (antigo, atrasado, adiantado, outro build, ilegivel).</summary>
	internal int RecusasPorVersao { get; private set; }

	/// <summary>Quantas linhas de recusa foram de fato escritas no log (as repetidas dentro do silencio nao contam).</summary>
	internal int RecusasNoLog { get; private set; }

	/// <summary>
	/// De quanto em quanto tempo a MESMA recusa (mesmo endereco, mesmo motivo) volta a sair no log. A recusa em
	/// si nao espera nada; o que se poupa e o console de quem fica batendo na porta -- um cliente desatualizado
	/// que tenta de novo a cada segundo, ou alguem varrendo portas.
	/// </summary>
	private const long SilencioDaRecusaRepetidaMs = 10_000;

	/// <summary>Teto do caderno de recusas ja anotadas: passou disto, ele e esvaziado (nao cresce com quem varre a rede).</summary>
	private const int TetoDoCadernoDeRecusas = 256;

	/// <summary>
	/// Quantas linhas de recusa cabem no log por MINUTO, venham de quem vierem. O silencio por endereco nao segura
	/// uma enxurrada de enderecos diferentes (o pedido de conexao e um pacote UDP solto, e o remetente dele se
	/// forja); este teto segura. O que passar e contado e sai numa linha de resumo quando o minuto vira.
	/// </summary>
	internal const int TetoDeLinhasDeRecusaPorMinuto = 30;

	private readonly Dictionary<string, long> _recusasNoLog = [];

	/// <summary>O comeco do minuto corrente do teto de linhas, quantas linhas ja sairam nele e quantas recusas ele calou.</summary>
	private long _minutoDasRecusas;
	private int _linhasNoMinuto, _caladasNoMinuto;

	/// <summary>
	/// O PEDIDO DE CONEXAO. Roda dentro do `PollEvents`, antes de existir `NetPeer`: quem e recusado aqui nunca
	/// vira par, nunca dispara `PeerConnected` e nunca manda um pacote de jogo.
	/// </summary>
	private void AoPedirConexao(ConnectionRequest req)
	{
		if (PortaAntigaDeTeste)
		{
			req.AcceptIfKey(Protocol.ConnectionKey);
			return;
		}

		string de = req.RemoteEndPoint.Address.ToString();
		IdentidadeDeBuild minha = MinhaVersao;
		FioDaVersao.JuizoDoPedido juizo = FioDaVersao.JulgarPedido(req.Data, minha, out IdentidadeDeBuild dele);
		string linha = LinhaDoJuizo(juizo, de, dele, minha);
		UltimoPedidoDeConexao = (juizo, dele, linha);

		if (juizo == FioDaVersao.JuizoDoPedido.SenhaErrada)
		{
			req.Reject();
			AnotarRecusa(de, juizo, linha);
			return;
		}

		if (juizo != FioDaVersao.JuizoDoPedido.Aceito && !AceitaQualquerVersaoDeTeste)
		{
			// A RECUSA LEVA A VERSAO DAQUI. Sem ela o cliente so saberia que foi recusado; com ela ele compara
			// com a propria e escreve a DIRECAO no menu ("o seu jogo esta ATRASADO -- atualize o jogo").
			if (CargaDaRecusaDeTeste is { } forjada) req.Reject(forjada);
			else
			{
				var carga = new NetDataWriter();
				FioDaVersao.EscreverBloco(carga, minha);
				req.Reject(carga);
			}
			RecusasPorVersao++;
			AnotarRecusa(de, juizo, linha);
			return;
		}

		// ACEITO -- e a versao daqui e o PRIMEIRO pacote que este par recebe, escrito no mesmo tratador do aceite:
		// o `Accept` ja devolve o par conectado, entao nada do jogo consegue passar na frente no canal confiavel.
		// E a metade "o cliente checa a versao do servidor": ele so manda o login depois de conferir isto.
		if (req.Accept() is not { } par) return;
		NetDataWriter w = Protocol.Begin(Protocol.S2C.Versao);
		FioDaVersao.EscreverBloco(w, minha);
		par.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// A LINHA DO LOG de um juizo, escrita pra o dono do servidor: diz QUEM tem de atualizar. As versoes saem do
	/// NUMERO que o cliente mandou (`Versao.TextoDe`), nunca de um texto dele.
	/// </summary>
	internal static string LinhaDoJuizo(FioDaVersao.JuizoDoPedido juizo, string de, IdentidadeDeBuild cliente, IdentidadeDeBuild servidor) => juizo switch
	{
		FioDaVersao.JuizoDoPedido.Aceito =>
			$"[server] versao: {de} entrou na {cliente.Texto}, a mesma daqui",
		FioDaVersao.JuizoDoPedido.SenhaErrada =>
			$"[server] versao: RECUSEI {de} -- nao e um cliente deste jogo (senha de entrada errada, ou pedido ilegivel)",
		FioDaVersao.JuizoDoPedido.SemVersao =>
			$"[server] versao: RECUSEI {de} -- cliente SEM VERSAO (um build antigo, de antes da checagem). Este servidor esta na {servidor.Texto}: o jogador precisa atualizar o jogo",
		FioDaVersao.JuizoDoPedido.VersaoIlegivel =>
			$"[server] versao: RECUSEI {de} -- a senha de entrada veio certa, mas a versao veio ilegivel (pedido truncado ou adulterado). Este servidor esta na {servidor.Texto}",
		FioDaVersao.JuizoDoPedido.ClienteAtrasado =>
			$"[server] versao: RECUSEI {de} -- cliente na {cliente.Texto}, ATRAS deste servidor ({servidor.Texto}): o jogador precisa atualizar o jogo",
		FioDaVersao.JuizoDoPedido.ClienteAdiantado =>
			$"[server] versao: RECUSEI {de} -- cliente na {cliente.Texto}, NA FRENTE deste servidor ({servidor.Texto}): e este servidor que precisa ser atualizado",
		FioDaVersao.JuizoDoPedido.OutroBuild =>
			$"[server] versao: RECUSEI {de} -- cliente na MESMA versao ({cliente.Texto}) mas com outro protocolo (o dele {cliente.DigitalCurta}, o daqui {servidor.DigitalCurta}): "
			+ "sao builds diferentes -- alguem distribuiu um build sem subir a versao em Core/Versao.cs",
		_ => $"[server] versao: RECUSEI {de}",
	};

	/// <summary>
	/// Poe a recusa no log: uma vez por endereco+motivo a cada <see cref="SilencioDaRecusaRepetidaMs"/>, e no maximo
	/// <see cref="TetoDeLinhasDeRecusaPorMinuto"/> linhas por minuto ao todo. A RECUSA EM SI NAO DEPENDE DISTO -- o
	/// que se cala e so a linha.
	/// </summary>
	private void AnotarRecusa(string de, FioDaVersao.JuizoDoPedido juizo, string linha)
	{
		long agora = System.Environment.TickCount64;
		if (agora - _minutoDasRecusas >= 60_000)
		{
			if (_caladasNoMinuto > 0)
				GD.Print($"[server] versao: +{_caladasNoMinuto} recusa(s) de conexao sem linha no ultimo minuto (repetidas, ou alem do teto de {TetoDeLinhasDeRecusaPorMinuto} linhas por minuto)");
			_minutoDasRecusas = agora;
			_linhasNoMinuto = _caladasNoMinuto = 0;
		}

		string chave = de + "|" + (byte)juizo;
		if ((_recusasNoLog.TryGetValue(chave, out long antes) && agora - antes < SilencioDaRecusaRepetidaMs)
			|| _linhasNoMinuto >= TetoDeLinhasDeRecusaPorMinuto)
		{
			_caladasNoMinuto++;
			return;
		}
		if (_recusasNoLog.Count >= TetoDoCadernoDeRecusas) _recusasNoLog.Clear();
		_recusasNoLog[chave] = agora;
		_linhasNoMinuto++;
		RecusasNoLog++;
		GD.Print(linha);
	}

	/// <summary>
	/// O SERVIDOR ACABOU DE ABRIR A PORTA: diz a versao no log, confere a copia do `project.godot`, le as flags de
	/// bancada desta porta e roda as bancadas que precisam da porta ABERTA (elas batem nela de verdade): a
	/// `--versaoteste`, a `--paradateste`, a `--saidaembateteste` e a `--adminlocalteste`.
	///
	/// ISTO RODA A CADA SUBIDA, e nao so na primeira -- quem hospeda, sai e hospeda de novo passa por aqui outra
	/// vez. As tres ultimas FAZEM subidas de dentro delas (e parte do que elas medem), e por isso cada uma tem o
	/// seu trinco de reentrada; a `--versaoteste` nao roda enquanto a `--paradateste` esta no meio do ciclo.
	/// </summary>
	private void VersaoAoSubir(int porta)
	{
		string[] args = OS.GetCmdlineArgs();
		IdentidadeDeBuild v = MinhaVersao;
		GD.Print($"[server] versao {v.Texto} | protocolo {v.DigitalCurta} | so entra cliente desta mesma versao"
			   + (FioDaVersao.Forjada ? "   (FORJADA pela linha de comando -- bancada)" : ""));

		if (VersaoDoProjeto(Versao.Texto) is var (doProjeto, bate) && !bate)
			GD.PushWarning($"[server] a versao do project.godot (config/version = \"{doProjeto}\") NAO BATE com a do Core/Versao.cs (\"{Versao.Texto}\"): "
						 + "o .exe exportado sairia carimbado com a versao errada. Troque as duas juntas.");

		if (Array.IndexOf(args, "--servidorantigo") >= 0)
		{
			PortaAntigaDeTeste = true;
			GD.Print("[server] BANCADA (--servidorantigo): a porta de ANTES da checagem de versao -- so a senha, e este servidor nao diz a versao a ninguem");
		}
		if (Array.IndexOf(args, "--servidoraceitatudo") >= 0)
		{
			AceitaQualquerVersaoDeTeste = true;
			GD.Print("[server] BANCADA (--servidoraceitatudo): este servidor NAO compara a versao de quem entra (quem tem de barrar e o cliente)");
		}

		if (Array.IndexOf(args, "--versaoteste") >= 0 && !_paradaRodando) RodarBancadaDaVersao(porta);
		// `--paradateste`: o servidor que para e sobe de novo no mesmo processo. Ver `GameServer.ParadaTeste.cs`
		// (o trinco de reentrada e dela: esta linha e alcancada de novo a cada subida que ela faz).
		if (Array.IndexOf(args, "--paradateste") >= 0) RodarBancadaDaParada(porta);
		// `--saidaembateteste`: sair do jogo no meio de um ZanzoClash. Ver `GameServer.SaidaDoEmbateTeste.cs`.
		if (Array.IndexOf(args, "--saidaembateteste") >= 0) RodarBancadaDaSaidaDoEmbate(porta);
		// `--adminlocalteste`: o admin por endereco e o desarme dele. Ver `GameServer.AdminLocalTeste.cs`.
		if (Array.IndexOf(args, "--adminlocalteste") >= 0) RodarBancadaDoAdminLocal(porta);
	}

	/// <summary>
	/// A copia da versao que o EXPORT usa (`application/config/version`, no `project.godot`), e se ela bate com
	/// <paramref name="doCore"/> -- a do `Core/Versao.cs`. As duas pontas sao Godot, entao a pergunta se faz em
	/// runtime: no servidor ao subir, e na bancada (que tambem pergunta por uma versao que o projeto NAO tem).
	/// </summary>
	internal static (string DoProjeto, bool Bate) VersaoDoProjeto(string doCore)
	{
		string doProjeto = ProjectSettings.GetSetting("application/config/version", "").AsString();
		return (doProjeto, doProjeto == doCore);
	}
}
