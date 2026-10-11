using System.Net;
using System.Security.Cryptography;
using Godot;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// O ANFITRIAO -- quem clicou em "Hospedar partida" e o unico admin automatico.
///
/// O dono (2026-10-11): *"faca o adm ser so pra quem clicou em hospedar"*.
///
/// ============================ O QUE HAVIA, E POR QUE SAIU ============================
/// O admin automatico vinha do ENDERECO: quem conectava da maquina do servidor entrava administrador. So que o
/// endereco nao identifica ninguem quando ha um tunel na frente (playit.gg, ngrok, um proxy): todo jogador
/// chega como 127.0.0.1. Uma chave (`--sem-admin-local`) e um desarme automatico ("duas contas locais ao mesmo
/// tempo") tapavam parte disso, e sobrava o caso que nao tem tapa: um estranho que entra SOZINHO pelo tunel,
/// com o dono fora do jogo, era o "dono da maquina". Tudo isso saiu -- a chave, o desarme e a regra.
///
/// ============================ A PROVA ============================
/// Admin automatico agora e de UMA conexao: a do cliente do MESMO PROCESSO que subiu este servidor pelo botao
/// (ou pelo `--host`, que e o mesmo gesto feito por um robo). O que a identifica e um segredo, e nao um
/// endereco nem um bit que o cliente declara:
///
///   * NASCE aqui, em <see cref="SubirComAnfitriao"/>: 32 bytes do sorteador criptografico do sistema, novos a
///     cada hospedagem. O servidor dedicado (`--server`) sobe pelo `Start` e nunca tem um -- la ninguem
///     hospedou, e ninguem e admin por estar perto.
///   * E ENTREGUE POR DENTRO DO PROCESSO: o `GameClient` pede a prova a este objeto na hora de discar
///     (<see cref="ProvaDoAnfitriaoPara"/>), e so a recebe se esta discando no proprio servidor (este
///     computador, a porta que este servidor abriu). Ela nunca e escrita em log, arquivo ou pacote de jogo.
///   * VIAJA no pedido de conexao, depois da senha de entrada e da versao (`FioDaVersao.PedidoDeConexao`), pelo
///     laco local do proprio computador.
///   * E CONFERIDA no aceite (`AoPedirConexao`), byte a byte e em tempo constante. Bateu: a CONEXAO fica
///     carimbada (o `Tag` do par) -- a conexao, e nao o endereco: outra conexao do mesmo endereco, ou a mesma
///     porta local reusada por outro programa, e outro par e nasce sem carimbo. Nao bateu: entra como
///     qualquer jogador.
///   * MORRE na parada (`EsquecerASessaoQueAcabou`): hospedar de novo sorteia outra, e a anterior nao abre
///     mais nada.
///
/// QUEM NAO TEM COMO FORJAR: qualquer cliente de OUTRO processo -- na mesma maquina, na rede ou atras de um
/// tunel. Ele nao le a memoria deste processo e nao ve o laco local dele. (Quem ja e administrador do
/// computador le a memoria de qualquer programa; contra esse nao ha prova que sirva, e ele nao precisa de uma.)
///
/// ============================ O SERVIDOR DEDICADO ============================
/// Ali ninguem clicou em nada, entao nao ha admin automatico NENHUM. O primeiro administrador nasce de quem
/// LIGA o servidor: `--admin &lt;conta&gt;` poe a marca na conta (<see cref="AccountSave.Admin"/>, a mesma do
/// painel), e a marca vale de qualquer endereco. Ver <see cref="PromoverAContaDaLinhaDeComando"/>.
/// =======================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A PROVA DESTA HOSPEDAGEM, ou nulo quando este servidor nao tem anfitriao (dedicado, ou parado). E tambem o
	/// CARIMBO: a conexao que provou guarda ESTE objeto no `Tag` dela, e um carimbo de outra hospedagem e outro
	/// objeto.
	/// </summary>
	private byte[]? _provaDoAnfitriao;

	/// <summary>Quantos pedidos de conexao chegaram com uma prova de anfitriao ERRADA desde a ultima subida.</summary>
	internal int ProvasDeAnfitriaoRecusadas { get; private set; }

	/// <summary>
	/// DEFEITO INJETADO (bancada `--adminlocalteste`): a regra de antes de 2026-10-11 -- quem conecta do proprio
	/// computador (127.0.0.1, que e como TODO jogador chega atras de um tunel) e tratado como o anfitriao. Lido
	/// pela propria linha do conserto, em <see cref="EhHost"/>.
	/// </summary>
	internal bool AdminPeloEnderecoDeTeste;

	/// <summary>
	/// SOBE O SERVIDOR PRA QUEM ESTA HOSPEDANDO NESTE PROCESSO -- o botao "Hospedar partida" e o `--host`. E o
	/// `Start` de sempre, mais o sorteio da prova desta hospedagem.
	/// </summary>
	public bool SubirComAnfitriao(int port)
	{
		if (!Start(port)) return false;
		// (por extenso: o `Godot.RandomNumberGenerator` tem o mesmo nome e NAO e criptografico)
		_provaDoAnfitriao ??= System.Security.Cryptography.RandomNumberGenerator.GetBytes(FioDaVersao.BytesDaProvaDeAnfitriao);
		return true;
	}

	/// <summary>
	/// PRO CLIENTE DESTE PROCESSO, na hora de discar: a prova, se o alvo e o servidor que este processo esta
	/// hospedando (este computador, a porta aberta aqui). Pra qualquer outro alvo devolve nulo -- a prova nao
	/// sai deste computador, nem vai pro servidor de outra pessoa.
	/// </summary>
	public byte[]? ProvaDoAnfitriaoPara(string host, int port)
	{
		if (!Running || _provaDoAnfitriao == null || port != _net.LocalPort) return null;
		bool esteComputador = IPAddress.TryParse(host, out IPAddress? ip)
			? IPAddress.IsLoopback(ip)
			: string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
		return esteComputador ? _provaDoAnfitriao : null;
	}

	/// <summary>
	/// NO ACEITE DE UMA CONEXAO (`AoPedirConexao`): a prova que veio no pedido e a desta hospedagem? Bateu, o par
	/// sai carimbado. Comparada em tempo constante; a errada e contada e dita no log UMA vez por subida (um
	/// curioso mandando provas ao acaso nao enche o console).
	/// </summary>
	private void CarimbarSeForOAnfitriao(NetPeer par, byte[]? prova)
	{
		if (prova == null) return;
		if (_provaDoAnfitriao != null && CryptographicOperations.FixedTimeEquals(prova, _provaDoAnfitriao))
		{
			par.Tag = _provaDoAnfitriao;
			return;
		}
		if (ProvasDeAnfitriaoRecusadas++ == 0)
			GD.PushWarning($"[server] {par.Address} mandou uma prova de anfitriao que NAO e a desta hospedagem -- entra como um jogador comum. "
						 + "(As proximas tentativas desta subida so sao contadas.)");
	}

	/// <summary>
	/// ESTA CONEXAO E A DE QUEM HOSPEDA? -- a do cliente deste processo, que provou no aperto de mao. E a unica
	/// fonte de admin automatico.
	/// </summary>
	private bool EhHost(NetPeer? peer)
	{
		if (peer == null) return false;
		if (_provaDoAnfitriao != null && ReferenceEquals(peer.Tag, _provaDoAnfitriao)) return true;
		return AdminPeloEnderecoDeTeste && IPAddress.IsLoopback(peer.Address);
	}

	/// <summary>O servidor parou: a prova desta hospedagem morre com ela. Chamado pelo `EsquecerASessaoQueAcabou`.</summary>
	private void EsquecerOAnfitriao()
	{
		_provaDoAnfitriao = null;
		ProvasDeAnfitriaoRecusadas = 0;
	}

	// =====================================================================
	// O PRIMEIRO ADMIN DE UM SERVIDOR DEDICADO -- `--admin <conta>`
	// =====================================================================
	/// <summary>O argumento do servidor que marca UMA conta como administradora.</summary>
	public const string FlagDoAdmin = "--admin";

	/// <summary>
	/// `--admin &lt;conta&gt;`, lido a cada subida: poe a MARCA DE ADMIN na conta, no disco -- a mesma do painel de
	/// contas (<see cref="AccountSave.Admin"/>), que vale de qualquer endereco e continua valendo sem a flag.
	///
	/// ============================ SO UMA CONTA QUE JA EXISTE ============================
	/// Conta neste servidor nasce no primeiro login, com a senha de quem chegou primeiro. Promover um nome que
	/// ainda nao e de ninguem seria entregar o servidor a quem o registrasse antes do dono. Entao a flag so marca
	/// conta que JA EXISTE quando o servidor sobe; se nao existe, ninguem e promovido e o log diz o que fazer
	/// (entrar uma vez com a conta, o que a cria, e ligar o servidor de novo).
	/// ====================================================================================
	/// </summary>
	/// <returns>A conta foi (ou ja estava) marcada.</returns>
	private bool PromoverAContaDaLinhaDeComando(string[] args)
	{
		int i = Array.IndexOf(args, FlagDoAdmin);
		if (i < 0) return false;
		string conta = i + 1 < args.Length ? args[i + 1] : "";
		if (conta.Length == 0 || conta.StartsWith('-'))
		{
			GD.PushWarning($"[server] {FlagDoAdmin}: faltou o nome da conta ({FlagDoAdmin} <conta>) -- ninguem foi promovido");
			return false;
		}
		if (_store?.Carregar(conta) is not { } a)
		{
			GD.PushWarning($"[server] {FlagDoAdmin}: a conta '{conta}' AINDA NAO EXISTE neste servidor -- ninguem foi promovido. "
						 + "Entre uma vez no servidor com ela (isso a cria, com a senha que voce escolher) e ligue o servidor de novo.");
			return false;
		}
		if (!a.Admin)
		{
			a.Admin = true;
			_store.Gravar(a);
			EmTodaCopiaViva(a.Conta, viva => viva.Admin = true);
			RastroDaLinhaDeComando($"promoveu a conta '{a.Conta}' ({FlagDoAdmin})");
		}
		GD.Print($"[server] {FlagDoAdmin}: a conta '{a.Conta}' e ADMINISTRADORA deste servidor (marca gravada no disco: vale de qualquer endereco, com ou sem a flag)");
		return true;
	}

	/// <summary>
	/// O RASTRO de uma promocao que nao veio de um admin em jogo: uma linha no `admin.log`, no mesmo formato do
	/// <see cref="Registrar"/>, com "a linha de comando do servidor" no lugar do nome de quem fez.
	/// </summary>
	private void RastroDaLinhaDeComando(string oque)
	{
		string linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  a linha de comando do servidor  {oque}";
		GD.Print("[admin] " + linha);
		if (_store == null) return;
		try
		{
			Directory.CreateDirectory(_store.Pasta);
			File.AppendAllText(Path.Combine(_store.Pasta, "admin.log"), linha + System.Environment.NewLine);
		}
		catch (Exception e)
		{
			GD.PushWarning($"[server] nao consegui gravar o admin.log: {e.Message}");
		}
	}
}
