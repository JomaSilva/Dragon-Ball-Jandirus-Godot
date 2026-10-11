using System.Globalization;
using System.Reflection;
using System.Text;
using Jandirus.Core;
using LiteNetLib.Utils;

namespace Jandirus.Net;

/// <summary>
/// ============================ O APERTO DE MAO DE VERSAO, ESCRITO E LIDO NUM LUGAR SO ============================
/// O dono (2026-10-11): *"ao entrar em um server o server checa a versao do cliente / cliente checa a versao do
/// server, e se n bater o jogo manda pro menu avisando q o cliente ta em alguma versao na frente ou atras"*.
///
/// SAO TRES MOMENTOS, e os tres levam o MESMO bloco (<see cref="EscreverBloco"/> / <see cref="LerBloco"/>):
///
///   1. O PEDIDO DE CONEXAO (cliente -> servidor), antes de existir conexao: a senha de entrada de sempre
///      (<see cref="Protocol.ConnectionKey"/>) e, depois dela, o bloco do CLIENTE. O servidor julga
///      (<see cref="JulgarPedido"/>) e, se nao bate, RECUSA -- nenhum pacote de jogo chega a existir.
///   2. A CARGA DA RECUSA (servidor -> cliente): o bloco do SERVIDOR. E com ele que o cliente sabe dizer se
///      esta ATRASADO ou ADIANTADO (`DisconnectInfo.AdditionalData`, lido em `GameClient.Versao.cs`).
///   3. O PRIMEIRO PACOTE DEPOIS DO ACEITE (<see cref="Protocol.S2C.Versao"/>): o bloco do SERVIDOR de novo.
///      O cliente confere por conta propria ANTES de mandar o login -- a outra direcao que o dono pediu, e a
///      unica que pega o servidor que aceita qualquer um.
///
/// ============================ O QUE O LITENETLIB 1.3.1 FAZ (MEDIDO, dois `NetManager` em 127.0.0.1) ============================
///   * dados A MAIS no pedido nao incomodam o servidor de antes: o `AcceptIfKey` dele le a senha e ignora o
///     resto -- cliente novo em servidor ANTIGO e ACEITO, e o servidor nunca manda versao (por isso o cliente
///     tem um prazo: `GameClient.PrazoDaVersaoMs`);
///   * `ConnectionRequest.Reject(writer)` entrega a carga inteira no `AdditionalData` do `PeerDisconnected` do
///     cliente (`ConnectionRejected`), em ~45 ms; recusa SEM carga chega com `AvailableBytes == 0`, nao nulo;
///   * `Accept()` devolve o par ja conectado, e um `Send` confiavel feito ALI chega ao cliente logo depois do
///     `PeerConnected` dele -- o primeiro pacote do canal confiavel e o da versao, por construcao;
///   * `TryGetString`/`TryGet*` nunca lancam nem leem alem do fim (o `Get*` cru le -- ver
///     `GameClient.PacotesMalLidos`); por isso este arquivo so usa leitura CONFERIDA.
///
/// ============================ ISTO NAO E SEGURANCA ============================
/// A checagem existe pra AVISAR O JOGADOR HONESTO de que o jogo dele e o servidor sao builds diferentes, em vez
/// de deixa-lo entrar e quebrar no meio. Ela nao barra ninguem mal-intencionado: quem adultera o cliente manda
/// o numero que quiser. Por isso a forja de bancada (`--versaoforjada`, <see cref="LerLinhaDeComando"/>) nao e
/// um buraco: forjar no CLIENTE so faz o servidor julga-lo pelo numero que ele disser -- e um cliente de outra
/// versao que mente pra entrar ganha exatamente os pacotes tortos que a checagem queria poupar. Quem decide
/// quem entra continua sendo o servidor, com a propria versao; e o que protege o jogo de um cliente mentiroso
/// e o que sempre protegeu -- o servidor valida cada pedido, um a um.
/// ==========================================================================================================
/// </summary>
public static class FioDaVersao
{
	/// <summary>Abre o bloco. No fio (little-endian) sao os bytes 4A 56 45 52 -- "JVER". Lixo nao passa por acaso.</summary>
	public const uint Marca = 0x5245564A;

	/// <summary>Marca (4) + numero da versao (4) + impressao digital do protocolo (8).</summary>
	public const int BytesDoBloco = 4 + 4 + 8;

	// =====================================================================
	// O BLOCO -- `uint marca` + `int numero` + `ulong digital`
	// =====================================================================
	/// <summary>
	/// SO NUMEROS, e nenhuma string: o texto da versao ("0.1.0") e refeito do numero em cada ponta. Assim nada
	/// que venha de fora vai parar na tela ou no log sem ter sido conferido.
	/// </summary>
	public static void EscreverBloco(NetDataWriter w, IdentidadeDeBuild id)
	{
		w.Put(Marca);
		w.Put(id.Numero);
		w.Put(id.Digital);
	}

	/// <summary>O que saiu da leitura de um bloco.</summary>
	public enum Leitura : byte
	{
		/// <summary>Bloco inteiro e valido.</summary>
		Lida,
		/// <summary>Nao ha byte nenhum: a outra ponta e de ANTES do aperto de mao (ou recusou sem dizer por que).</summary>
		Ausente,
		/// <summary>Ha bytes, mas nao sao um bloco: truncado, sem a marca, ou com um numero que nao e versao.</summary>
		Ilegivel,
	}

	/// <summary>
	/// Le um bloco SEM NUNCA LANCAR E SEM LER ALEM DO FIM, venha o que vier (nulo, vazio, truncado, lixo).
	/// O que sobrar DEPOIS do bloco e ignorado de proposito: campo novo se acrescenta no fim, e a ponta mais
	/// velha continua lendo o que conhece.
	/// </summary>
	public static Leitura LerBloco(NetDataReader? r, out IdentidadeDeBuild id)
	{
		id = default;
		if (r == null || r.IsNull || r.AvailableBytes <= 0) return Leitura.Ausente;
		if (r.AvailableBytes < BytesDoBloco) return Leitura.Ilegivel;
		if (r.GetUInt() != Marca) return Leitura.Ilegivel;
		int numero = r.GetInt();
		ulong digital = r.GetULong();
		if (numero < 0 || numero > Versao.NumeroMaximo) return Leitura.Ilegivel;
		id = new IdentidadeDeBuild(numero, digital);
		return Leitura.Lida;
	}

	// =====================================================================
	// O PEDIDO DE CONEXAO -- `string senha` + o bloco do cliente
	// =====================================================================
	/// <summary>
	/// O que o cliente manda no `NetManager.Connect`. A SENHA VAI PRIMEIRO, no mesmo lugar de sempre: e ali que
	/// o servidor de antes a procura (`AcceptIfKey`), e trocar a ordem faria o cliente novo ser recusado por um
	/// servidor antigo sem explicacao -- em vez de aceito, com o aviso de "servidor sem versao".
	/// </summary>
	/// <param name="provaDeAnfitriao">
	/// So o cliente do MESMO PROCESSO que esta hospedando manda isto: a prova que o servidor dele lhe entregou por
	/// dentro do processo (`GameServer.ProvaDoAnfitriaoPara`). Vai DEPOIS do bloco da versao -- um servidor de
	/// antes nem olha pra la, e o de agora trata a falta (ou uma prova errada) como "e um jogador comum".
	/// </param>
	public static NetDataWriter PedidoDeConexao(IdentidadeDeBuild cliente, byte[]? provaDeAnfitriao = null)
	{
		var w = new NetDataWriter();
		w.Put(Protocol.ConnectionKey);
		EscreverBloco(w, cliente);
		if (provaDeAnfitriao != null)
		{
			w.Put(MarcaDoAnfitriao);
			w.Put(provaDeAnfitriao);
		}
		return w;
	}

	/// <summary>"HOST" -- abre o trecho da prova de anfitriao no pedido de conexao.</summary>
	public const uint MarcaDoAnfitriao = 0x54534F48;

	/// <summary>O tamanho da prova de anfitriao: 32 bytes sorteados pelo servidor a cada hospedagem.</summary>
	public const int BytesDaProvaDeAnfitriao = 32;

	/// <summary>
	/// A PROVA DE ANFITRIAO que sobrou no pedido, DEPOIS de <see cref="JulgarPedido"/> ter lido a senha e a versao
	/// -- ou nulo se nao veio, veio pela metade, veio com sobra ou sem a marca. Nunca lanca. Quem decide se ela
	/// VALE e o servidor (`GameServer.Anfitriao.cs`): aqui so se le.
	/// </summary>
	public static byte[]? LerProvaDeAnfitriao(NetDataReader? r)
	{
		if (r == null || r.IsNull || r.AvailableBytes != sizeof(uint) + BytesDaProvaDeAnfitriao) return null;
		if (r.GetUInt() != MarcaDoAnfitriao) return null;
		var prova = new byte[BytesDaProvaDeAnfitriao];
		r.GetBytes(prova, BytesDaProvaDeAnfitriao);
		return prova;
	}

	/// <summary>
	/// SO PRA BANCADA: o pedido do cliente de ANTES de 2026-10-11 -- a senha e mais nada. E o que o
	/// `NetManager.Connect(host, porta, chave)` daquele cliente escreve.
	/// </summary>
	public static NetDataWriter PedidoDeConexaoAntigo()
	{
		var w = new NetDataWriter();
		w.Put(Protocol.ConnectionKey);
		return w;
	}

	/// <summary>O que o servidor decide de um pedido de conexao.</summary>
	public enum JuizoDoPedido : byte
	{
		/// <summary>Senha certa, mesma versao, mesmo protocolo.</summary>
		Aceito,
		/// <summary>Nao e um cliente deste jogo: senha errada, ou pedido que nem senha tem.</summary>
		SenhaErrada,
		/// <summary>Senha certa e nada depois dela: o cliente de antes do aperto de mao.</summary>
		SemVersao,
		/// <summary>Senha certa, mas o que vem depois nao e um bloco de versao.</summary>
		VersaoIlegivel,
		/// <summary>O cliente esta numa versao MENOR que a do servidor.</summary>
		ClienteAtrasado,
		/// <summary>O cliente esta numa versao MAIOR que a do servidor.</summary>
		ClienteAdiantado,
		/// <summary>Mesmo numero, impressao digital diferente.</summary>
		OutroBuild,
	}

	/// <summary>
	/// O JUIZO DO SERVIDOR, sem efeito nenhum (quem aceita, recusa e escreve no log e
	/// `GameServer.AoPedirConexao`). Nunca lanca: pedido vazio, truncado ou de lixo cai em
	/// <see cref="JuizoDoPedido.SenhaErrada"/> ou <see cref="JuizoDoPedido.VersaoIlegivel"/>.
	/// </summary>
	public static JuizoDoPedido JulgarPedido(NetDataReader? dados, IdentidadeDeBuild servidor, out IdentidadeDeBuild cliente)
	{
		cliente = default;
		if (dados == null || dados.IsNull || !dados.TryGetString(out string senha) || senha != Protocol.ConnectionKey)
			return JuizoDoPedido.SenhaErrada;

		switch (LerBloco(dados, out cliente))
		{
			case Leitura.Ausente: return JuizoDoPedido.SemVersao;
			case Leitura.Ilegivel: return JuizoDoPedido.VersaoIlegivel;
		}

		return Versao.Comparar(cliente, servidor) switch
		{
			Versao.Desencontro.JogoAtrasado => JuizoDoPedido.ClienteAtrasado,
			Versao.Desencontro.JogoAdiantado => JuizoDoPedido.ClienteAdiantado,
			Versao.Desencontro.OutroBuild => JuizoDoPedido.OutroBuild,
			_ => JuizoDoPedido.Aceito,
		};
	}

	// =====================================================================
	// A IMPRESSAO DIGITAL DO PROTOCOLO
	// =====================================================================
	private static string? _descricao;
	private static ulong? _digital;

	/// <summary>
	/// ============================ A SEGUNDA TRAVA: O BUILD QUE MUDOU SEM A VERSAO SUBIR ============================
	/// O numero da versao so protege se alguem lembrar de subi-lo. Esta conta e AUTOMATICA: uma descricao em
	/// texto do contrato de rede, tirada por reflexao do binario que esta rodando, e o FNV-1a dela
	/// (<see cref="Versao.Fnv1a64"/>). Mesma versao + digital diferente = "mesma versao, mas builds diferentes".
	///
	/// O QUE ENTRA (tudo o que mora no namespace `Jandirus.Net`, em ordem alfabetica):
	///   * todo `enum`, com nome e valor de cada item -- `C2S`, `S2C`, os subtipos de pacote, as poses...
	///     opcode novo, renumerado ou renomeado muda a digital;
	///   * toda constante (`const`): os bits de flag, os modos de pacote, os tetos de texto, o passo do servidor;
	///   * os campos (nome e tipo) dos `struct` -- os estados que viajam (`SheetState`, `EntityState`,
	///     `SlotInfo`...): campo novo num deles quase sempre e byte novo no fio.
	///
	/// O QUE ELA NAO VE, e por isso nao substitui subir a versao: um campo a mais escrito A MAO dentro de um
	/// pacote que ja existia (um `w.Put` novo no meio do `JoinAccepted`), e qualquer regra do `Core` -- as duas
	/// pontas calculam com o mesmo `Core`, e um `Core` diferente em cada uma nao muda nada aqui.
	///
	/// SO NOME E VALOR, nunca endereco nem numero de build: dois binarios compilados do mesmo fonte em maquinas
	/// diferentes dao a MESMA digital (o tipo de um campo entra pelo `ToString` dele, que nao leva a versao do
	/// runtime), e um conserto que nao toca no contrato de rede nao a muda.
	/// ================================================================================================================
	/// </summary>
	public static ulong DigitalDoProtocolo => _digital ??= Versao.Fnv1a64(DescricaoDoProtocolo());

	/// <summary>O texto de que a digital e feita. Fica exposto pra a bancada (e pra quem quiser ver O QUE mudou entre dois builds).</summary>
	public static string DescricaoDoProtocolo() => _descricao ??= DescreverSemDerrubar();

	/// <summary>
	/// A CONTA DA DIGITAL NAO PODE DERRUBAR A ENTRADA NO JOGO. Ela roda na primeira tela e na porta do servidor,
	/// e e uma trava de reserva: se a reflexao lancar numa maquina, o processo grita no log e segue com uma
	/// descricao de emergencia -- e ai o pior que acontece e o aviso "builds diferentes" contra quem conseguiu
	/// fazer a conta, que e verdade o bastante.
	/// </summary>
	private static string DescreverSemDerrubar()
	{
		try { return Descrever(); }
		catch (Exception e)
		{
			Godot.GD.PushError($"[versao] nao deu pra descrever o protocolo ({e.GetType().Name}: {e.Message}) -- a impressao digital deste processo e a de EMERGENCIA");
			return "(descricao indisponivel)";
		}
	}

	private static string Descrever()
	{
		const BindingFlags Estaticos = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
		const BindingFlags DeInstancia = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

		// UM TIPO QUE NAO CARREGA NAO PODE DERRUBAR A CONTA: o binario do jogo tem tipos que dependem de outras
		// DLLs, e um processo sem uma delas ainda le os de `Jandirus.Net` inteiros.
		Type?[] todos;
		try { todos = typeof(Protocol).Assembly.GetTypes(); }
		catch (ReflectionTypeLoadException e) { todos = e.Types; }

		var sb = new StringBuilder(16384);
		// SEM OS TIPOS QUE O COMPILADOR INVENTA (o `<` no nome: fechos de lambda, iteradores): o nome deles leva
		// um contador que anda com qualquer mudanca vizinha, e eles nao sao contrato de ninguem.
		string? daqui = typeof(Protocol).Namespace;
		foreach (Type t in todos.OfType<Type>()
								.Where(t => t.Namespace == daqui && t.FullName is { } nome && !nome.Contains('<'))
								.OrderBy(t => t.FullName, StringComparer.Ordinal))
		{
			if (t.IsEnum)
			{
				sb.Append("enum ").Append(t.FullName).Append(':').Append(Enum.GetUnderlyingType(t).Name).Append('{');
				foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(f => f.Name, StringComparer.Ordinal))
					sb.Append(f.Name).Append('=').Append(Valor(f.GetRawConstantValue())).Append(',');
				sb.Append("}\n");
				continue;
			}

			foreach (FieldInfo f in t.GetFields(Estaticos).Where(f => f.IsLiteral).OrderBy(f => f.Name, StringComparer.Ordinal))
				sb.Append("const ").Append(t.FullName).Append('.').Append(f.Name).Append('=').Append(Valor(f.GetRawConstantValue())).Append('\n');

			if (t.IsValueType)
				foreach (FieldInfo f in t.GetFields(DeInstancia).OrderBy(f => f.Name, StringComparer.Ordinal))
					sb.Append("campo ").Append(t.FullName).Append('.').Append(f.Name).Append(':').Append(f.FieldType).Append('\n');
		}
		return sb.ToString();
	}

	/// <summary>O valor de uma constante como texto, igual em toda maquina (ponto decimal e nao virgula).</summary>
	private static string Valor(object? v) => v switch
	{
		null => "null",
		IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
		_ => v.ToString() ?? "",
	};

	// =====================================================================
	// A IDENTIDADE DESTE PROCESSO (e a forja de bancada)
	// =====================================================================
	// (As tres abaixo sao `static readonly`, e nao `const`, de proposito: toda constante desta pasta entra na
	// impressao digital do protocolo, e o nome de uma flag de bancada nao e contrato de rede.)

	/// <summary>A flag que forja a versao deste processo: `--versaoforjada +1`, `--versaoforjada -1` ou `--versaoforjada 0.3.0`.</summary>
	public static readonly string FlagDaVersaoForjada = "--versaoforjada";

	/// <summary>A flag que forja a impressao digital deste processo (a mesma versao, "outro build"): `--digitalforjada`.</summary>
	public static readonly string FlagDaDigitalForjada = "--digitalforjada";

	/// <summary>
	/// O que a digital forjada tem de diferente da de verdade: um XOR fixo (duas rodadas dao o mesmo numero) que
	/// mexe nos digitos da FRENTE -- sao eles que o aviso mostra (`IdentidadeDeBuild.DigitalCurta`).
	/// </summary>
	public static readonly ulong MascaraDaForja = 0xF0F0F0F0F0F0F0F0UL;

	private static bool _linhaLida;
	private static int? _numeroForjado;
	private static bool _digitalForjada;

	/// <summary>
	/// O QUE ESTE PROCESSO DIZ DE SI: a versao do <see cref="Versao"/> e a digital do protocolo -- ou o que a
	/// linha de comando mandou forjar. O cliente e o servidor do MESMO processo (o modo "Hospedar") leem esta
	/// mesma propriedade, e por isso quem hospeda passa direto pela propria checagem, forjado ou nao.
	/// </summary>
	public static IdentidadeDeBuild DoProcesso
	{
		get
		{
			// O UNICO PONTO DESTA PASTA QUE FALA COM O GODOT, e so pra ler os argumentos: assim ninguem precisa
			// lembrar de "iniciar" a versao antes de usa-la, e a forja vale igual pro selo do menu, pro cliente
			// e pro servidor.
			if (!_linhaLida) LerLinhaDeComando(Godot.OS.GetCmdlineArgs());
			return new IdentidadeDeBuild(_numeroForjado ?? Versao.Numero,
										 DigitalDoProtocolo ^ (_digitalForjada ? MascaraDaForja : 0UL));
		}
	}

	/// <summary>A versao deste processo foi forjada pela linha de comando? (O selo do menu e o log dizem isso em voz alta.)</summary>
	public static bool Forjada
	{
		get
		{
			if (!_linhaLida) LerLinhaDeComando(Godot.OS.GetCmdlineArgs());
			return _numeroForjado != null || _digitalForjada;
		}
	}

	/// <summary>
	/// A FORJA DE BANCADA. `--versaoforjada +1` (ou `-1`, ou um texto "0.3.0") faz ESTE processo se apresentar
	/// com outra versao -- no selo do menu, no pedido de conexao e no juizo do servidor; `--digitalforjada` troca
	/// so a impressao digital. Serve pra simular o desencontro entre dois processos do mesmo binario
	/// (`ag/versao/bancadas.sh`, casos `dupla_*`). Por que isto nao e um jeito de burlar a checagem esta no
	/// cabecalho ("ISTO NAO E SEGURANCA"). Devolve a frase do que foi forjado (vazia = nada).
	/// </summary>
	public static string LerLinhaDeComando(string[] args)
	{
		_linhaLida = true;
		_numeroForjado = null;
		_digitalForjada = Array.IndexOf(args, FlagDaDigitalForjada) >= 0;

		int i = Array.IndexOf(args, FlagDaVersaoForjada);
		if (i >= 0 && i + 1 < args.Length)
		{
			string pedido = args[i + 1];
			if (pedido.Length > 1 && pedido[0] is '+' or '-'
				&& int.TryParse(pedido, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int passo))
				_numeroForjado = Math.Clamp(Versao.Numero + passo, 0, Versao.NumeroMaximo);
			else if (Versao.TentarLer(pedido, out int exato))
				_numeroForjado = exato;
		}

		string frase = "";
		if (_numeroForjado is { } n) frase = $"versao FORJADA em {Versao.TextoDe(n)} (a de verdade e {Versao.Texto})";
		if (_digitalForjada) frase += (frase.Length > 0 ? " e " : "") + "impressao digital do protocolo FORJADA";
		if (frase.Length > 0)
			Godot.GD.Print($"[versao] ATENCAO: {frase} pela linha de comando. E coisa de BANCADA: este processo vai ser tratado como outro build.");
		return frase;
	}
}
