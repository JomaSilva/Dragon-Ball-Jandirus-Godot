using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ADMINISTRACAO: quem pode, e o que pode.
///
/// ============================ AS DUAS PORTAS DE ENTRADA ============================
/// 1. O HOST. Quem conecta da MESMA MAQUINA em que o servidor roda entra administrador. E o
///    pedido do dono, dito com estas palavras: "o server detecta que o ip do player que conectou
///    e do server sao os mesmos, logo ele ta conectando por ip local, logo e o host, logo e adm".
///    Faz sentido e o original ja fazia por outro caminho (o `Login.dm` dava nivel 6 a quem
///    subia o mundo): quem tem o processo na propria maquina ja pode desligar tudo e editar o
///    disco -- negar-lhe os verbs seria teatro.
///
/// 2. A CONTA MARCADA. Um admin pode promover OUTRA pessoa, e a marca fica no arquivo da conta
///    dela (<see cref="AccountSave.Admin"/>), do lado do hash da senha. E o modelo do original,
///    que amarrava admin ao `ckey` e nao ao mob (`Admin_Check.dm`).
/// ===================================================================================
///
/// ============================ O DEFEITO QUE ISTO CONSERTA ============================
/// O bit ja era aceso no login... e apagado doze linhas depois. `AplicarPoderes` refaz
/// `pl.Poderes` do zero a partir das skills, e o login chamava-o DEPOIS de marcar o host. O host
/// entrava admin e deixava de ser admin no mesmo instante -- a aba nunca aparecia, e nenhum log
/// acusava nada, porque as duas linhas estavam certas separadamente.
///
/// E o mesmo tombo que este projeto ja levou varias vezes: escrever a regra e LIGAR a regra sao
/// dois trabalhos. Aqui a marca mora em <see cref="ServerPlayer.PoderesConcedidos"/>, que o
/// recalculo nao varre.
/// =====================================================================================
///
/// PERMISSAO E CONFERIDA AQUI, e nao no cliente. O menu esconde a aba de quem nao e admin porque
/// mostrar seria confuso -- mas esconder botao nunca foi permissao.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// QUEM E O HOST
	// =====================================================================
	/// <summary>
	/// OS ENDERECOS DESTA MAQUINA, calculados uma vez.
	///
	/// Nao basta olhar o loopback. Quem hospeda pelo botao conecta em `127.0.0.1`, mas quem sobe o
	/// `servidor.bat` e depois abre o jogo costuma digitar o IP da propria rede ("192.168.0.10") --
	/// e a conexao chega com ESSE endereco, nao com o loopback. Sao a mesma maquina, e a regra
	/// pedida foi de maquina, nao de string.
	///
	/// Calculado uma vez e guardado: varrer as interfaces de rede a cada login seria uma chamada
	/// de sistema por conexao pra uma resposta que so muda quando o cabo muda.
	/// </summary>
	private static readonly HashSet<string> EnderecosLocais = LevantarEnderecosLocais();

	private static HashSet<string> LevantarEnderecosLocais()
	{
		var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (ni.OperationalStatus != OperationalStatus.Up) continue;
				foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
					s.Add(Normalizar(ip.Address));
			}
		}
		catch (Exception e)
		{
			// SEM AS INTERFACES O JOGO CONTINUA: o loopback sozinho ainda cobre quem hospeda pelo
			// botao, que e o caso comum. Falhar aqui nao pode impedir o servidor de subir.
			GD.PushWarning($"[server] nao consegui listar os enderecos desta maquina: {e.Message}");
		}
		return s;
	}

	/// <summary>
	/// Um endereco em forma comparavel.
	///
	/// O socket do servidor e dual-stack, entao uma conexao IPv4 chega MAPEADA em IPv6
	/// ("::ffff:192.168.0.10"). Comparar sem desmapear faria o mesmo micro parecer duas maquinas.
	/// O escopo do IPv6 local ("%15" no fim) tambem sai: e o indice da placa, nao o endereco.
	/// </summary>
	private static string Normalizar(IPAddress a)
	{
		if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
		if (a.AddressFamily == AddressFamily.InterNetworkV6 && a.ScopeId != 0)
			a = new IPAddress(a.GetAddressBytes());
		return a.ToString();
	}

	/// <summary>
	/// HOSPEDOU, E ADMIN: a conexao vem da mesma maquina em que este servidor roda.
	///
	/// Vale pros DOIS jeitos de subir o servidor -- pelo botao "Hospedar" e pelo `servidor.bat`.
	/// A versao anterior exigia o botao, e por isso quem rodava o `.bat` e entrava pelo IP da
	/// propria rede nunca era reconhecido.
	/// </summary>
	private bool EhHost(NetPeer? peer)
	{
		if (peer == null || !AdminPorEndereco) return false;
		IPAddress a = peer.Address;
		if (IPAddress.IsLoopback(a)) return true;
		return EnderecosLocais.Contains(Normalizar(a));
	}

	/// <summary>
	/// O ENDERECO AINDA PROVA ALGUMA COISA?
	///
	/// ============================ POR QUE ISTO PODE SER DESLIGADO ============================
	/// A regra pedida foi "mesmo IP = mesma maquina = host = admin", e ela vale num PC de dono
	/// unico. Mas o endereco so identifica maquina enquanto ninguem estiver traduzindo endereco no
	/// meio do caminho -- e hospedar jogo pra amigos quase sempre passa por isso:
	///
	///   * um tunel (playit.gg, ngrok, um `socat`) entrega TODO jogador como 127.0.0.1;
	///   * o proxy de userland do Docker entrega todo mundo como o gateway da bridge;
	///   * numa maquina compartilhada, qualquer outra pessoa logada ja e "local".
	///
	/// Nesses casos o mundo inteiro entraria administrador. Por isso duas coisas: a chave abaixo,
	/// e o desarme automatico do <see cref="ConferirAmbiguidadeDeHost"/> -- se DUAS contas
	/// diferentes chegam por endereco local ao mesmo tempo, o endereco deixou de distinguir
	/// qualquer coisa e para de valer na hora.
	/// =========================================================================================
	///
	/// Desligada por `--sem-admin-local`. Quem hospeda atras de tunel deve ligar isso e promover a
	/// propria conta uma vez (a marca fica no disco e vale de qualquer endereco).
	/// </summary>
	public bool AdminPorEndereco { get; private set; } = true;

	public void DesligarAdminPorEndereco() => AdminPorEndereco = false;

	/// <summary>As contas que ja entraram por endereco local nesta sessao.</summary>
	private readonly HashSet<string> _contasLocais = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// DESARMA a regra de endereco quando ela deixa de distinguir alguem.
	///
	/// Uma maquina de dono unico produz exatamente UMA conta local. Duas contas diferentes vindo
	/// de endereco local ao mesmo tempo so acontece em dois mundos: alguem tem tunel/proxy na
	/// frente (e entao "local" e todo mundo) ou a maquina e compartilhada. Nos dois, continuar
	/// dando admin por endereco e dar admin a estranhos.
	///
	/// Quem ja recebeu PERDE na hora -- inclusive quem estava jogando. E incomodo, e e menos
	/// incomodo que descobrir depois que o servidor inteiro pode banir o dono.
	/// </summary>
	private void ConferirAmbiguidadeDeHost(ServerPlayer pl)
	{
		if (!AdminPorEndereco || pl.Conta.Length == 0) return;
		if (!_contasLocais.Add(pl.Conta) || _contasLocais.Count < 2) return;

		AdminPorEndereco = false;
		GD.PushWarning($"[server] DUAS contas chegaram por endereco local ({string.Join(", ", _contasLocais)}) -- "
					 + "o endereco nao identifica mais a maquina do dono (tunel? proxy? maquina compartilhada?). "
					 + "Admin por endereco DESLIGADO. Promova a conta do dono pelo painel, que a marca vai pro disco.");

		foreach (ServerPlayer p in _players.Values.ToList())
		{
			// quem tem a marca NA CONTA nao perde nada: o admin dele nao vinha do endereco
			bool porConta = _contas.Values.Any(
				c => string.Equals(c.Conta, p.Conta, StringComparison.OrdinalIgnoreCase) && c.Admin);
			if (porConta || !EhAdmin(p)) continue;
			p.PoderesConcedidos &= ~Protocol.Poder.Admin;
			AplicarPoderes(p);
			Avisar(p, "o administrador por endereco foi desligado neste servidor.");
		}
	}

	private static bool EhAdmin(ServerPlayer pl) => (pl.Poderes & Protocol.Poder.Admin) != 0;

	// =====================================================================
	// O RASTRO
	// =====================================================================
	/// <summary>
	/// O QUE UM ADMIN FEZ, gravado em disco (`admin.log`, ao lado das contas).
	///
	/// ============================ POR QUE UM ARQUIVO E NAO SO O CONSOLE ============================
	/// Banir, matar e expulsar sao coisas que alguem vai contestar depois -- e o console do servidor
	/// some quando a janela fecha. O original tinha um sistema de logs inteiro (`Modules/Admin/logs.dm`,
	/// com o verb "All Logs"); aqui nao ha esse sistema, e o mais barato que resolve o problema real e
	/// uma linha por acao num arquivo de texto.
	///
	/// Sem isto, os verbs mais pesados desta aba nao deixam rastro NENHUM -- e um poder sem rastro e
	/// um poder em que ninguem confia, inclusive quem o tem.
	/// ==============================================================================================
	/// </summary>
	private void Registrar(ServerPlayer adm, string oque)
	{
		string linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {adm.Name} ({adm.Conta})  {oque}";
		GD.Print("[admin] " + linha);
		if (_store == null) return;
		try
		{
			Directory.CreateDirectory(_store.Pasta);
			// `System.Environment` escrito por extenso: `Godot.Environment` (o node de pos-processamento)
			// esta no escopo e o nome cru fica ambiguo.
			File.AppendAllText(Path.Combine(_store.Pasta, "admin.log"), linha + System.Environment.NewLine);
		}
		catch (Exception e)
		{
			// LOG QUE FALHA NAO PODE DERRUBAR A ACAO. Disco cheio, arquivo aberto noutro programa:
			// nada disso pode impedir um admin de banir alguem.
			GD.PushWarning($"[server] nao consegui gravar o admin.log: {e.Message}");
		}
	}

	/// <summary>
	/// Decide os poderes CONCEDIDOS de quem esta entrando. Chamado no login, antes do
	/// <c>AplicarPoderes</c> -- que agora soma este campo em vez de apaga-lo.
	/// </summary>
	private void ConcederPoderes(ServerPlayer pl, NetPeer peer, AccountSave acc)
	{
		if (EhHost(peer))
		{
			pl.PoderesConcedidos |= Protocol.Poder.Admin;
			GD.Print($"[server] {pl.Name} conectou de {peer.Address} -- e a maquina do servidor: entra como ADMIN");
			// ...mas o endereco so vale enquanto distinguir alguem. Ver `ConferirAmbiguidadeDeHost`.
			ConferirAmbiguidadeDeHost(pl);
		}
		else if (acc.Admin)
		{
			pl.PoderesConcedidos |= Protocol.Poder.Admin;
			GD.Print($"[server] {pl.Name} entra como ADMIN (conta '{acc.Conta}' promovida)");
		}

		// O MUTE TAMBEM VEM DO DISCO. Sem esta linha, sair e voltar desfazia a punicao.
		CarregarMute(pl, acc);
	}

	// =====================================================================
	// MUTE -- o `Mutes` do original (Admin.dm:459-472)
	// =====================================================================
	/// <summary>
	/// Contas caladas AGORA ONLINE -- o cache. A verdade mora em <see cref="AccountSave.Calada"/>,
	/// no disco.
	///
	/// Por CONTA e nao por personagem: trocar de slot nao pode desfazer a punicao, que era o buraco
	/// obvio do "mute no mob". O original ja fazia por `ckey` pela mesma razao.
	///
	/// O CONJUNTO SOZINHO NAO BASTAVA: ele morria com o processo, e no fluxo "Hospedar" o processo
	/// morre toda vez que o dono fecha o jogo. Bastava esperar o servidor reiniciar pra voltar a
	/// falar -- sem log, sem aviso, sem nada.
	/// </summary>
	private readonly HashSet<string> _calados = new(StringComparer.OrdinalIgnoreCase);

	public bool EstaCalado(ServerPlayer pl) => pl.Conta.Length > 0 && _calados.Contains(pl.Conta);

	/// <summary>Chamado no login: traz a marca do disco pro cache desta sessao.</summary>
	private void CarregarMute(ServerPlayer pl, AccountSave acc)
	{
		if (acc.Calada) _calados.Add(acc.Conta);
		else _calados.Remove(acc.Conta);
	}

	// =====================================================================
	// AS COPIAS VIVAS DE UMA CONTA
	// =====================================================================
	/// <summary>
	/// APLICA UMA MUDANCA EM TODA COPIA VIVA de uma conta -- e sao DUAS colecoes, nao uma.
	///
	/// ============================ O DEFEITO QUE ISTO MATA ============================
	/// Uma `AccountSave` viva pode estar em dois lugares: em `_logados` (passou pela senha, esta
	/// escolhendo personagem) ou em `_contas` (ja entrou no mundo). O objeto MIGRA de um pro outro
	/// dentro do `Entrar`.
	///
	/// Promover/banir carrega uma copia NOVA do disco, grava, e sincronizava so `_contas`. Quem
	/// estivesse na tela de selecao ficava com a copia velha -- e o `Persistir` seguinte regravava
	/// o arquivo A PARTIR DELA, desfazendo em silencio o que o admin acabara de fazer. A promocao
	/// sumia do disco; o banimento tambem.
	///
	/// Um metodo so pras duas colecoes porque toda marca futura na conta cai na mesma armadilha.
	/// =================================================================================
	/// </summary>
	private void EmTodaCopiaViva(string conta, Action<AccountSave> aplicar)
	{
		foreach (AccountSave viva in _contas.Values.Concat(_logados.Values))
			if (string.Equals(viva.Conta, conta, StringComparison.OrdinalIgnoreCase)) aplicar(viva);
	}

	/// <summary>Os peers presos na tela de selecao com esta conta -- eles nao tem `ServerPlayer`.</summary>
	private List<NetPeer> PeersNaSelecao(string conta) =>
		[.. _logados.Where(kv => string.Equals(kv.Value.Conta, conta, StringComparison.OrdinalIgnoreCase))
				   .Select(kv => kv.Key)];

	// =====================================================================
	// O PAINEL DE CONTAS -- e o que o dono pediu: "permita o adm dar administrador
	// a um dos perfis criados no server"
	// =====================================================================
	/// <summary>
	/// A LISTA DE CONTAS DO SERVIDOR, pro painel de admin.
	///
	/// Inclui QUEM ESTA OFFLINE, que e o caso normal: o dono do servidor promove um amigo que
	/// jogou ontem. Uma lista so dos online transformaria "promover" em "estar os dois na tela ao
	/// mesmo tempo".
	///
	/// A SENHA NAO VIAJA -- nem o sal, nem o hash. So sai o que o painel desenha: nome da conta,
	/// os personagens dela, e as duas marcas (admin, banida).
	/// </summary>
	private void MandarContas(ServerPlayer pl)
	{
		if (!EhAdmin(pl)) return;
		List<AccountSave> contas = _store?.Todas() ?? [];

		var w = Protocol.Begin(Protocol.S2C.Contas);
		w.Put((ushort)Math.Min(contas.Count, ushort.MaxValue));
		foreach (AccountSave a in contas.Take(ushort.MaxValue))
		{
			bool online = _players.Values.Any(
				p => string.Equals(p.Conta, a.Conta, StringComparison.OrdinalIgnoreCase));
			w.Put(a.Conta);
			w.Put(a.Admin);
			w.Put(a.Banida);
			w.Put(online);
			// CORTADO NO TAMANHO DO PACOTE. Tres personagens de nome longo passam de 160 bytes, e o
			// leitor (`GetString(160)`) devolve VAZIO -- nao truncado, VAZIO -- pra string maior que
			// o limite. Cortar aqui e a diferenca entre "Goku (Saiyan), Gohan (S..." e uma linha em
			// branco onde deviam estar os personagens da conta.
			string personagens = string.Join(", ", a.Slots.Where(s => s != null).Select(s => $"{s!.Nome} ({s.Raca})"));
			w.Put(personagens.Length <= 156 ? personagens : personagens[..156] + "...");
		}
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// Acha a conta que o admin digitou, ou explica por que nao deu.
	///
	/// RECUSA EMPATE. Nome de personagem nao e unico -- a criacao so confere contra quem esta
	/// online --, entao "promova o Goku" pode casar com duas contas. Escolher a primeira em ordem
	/// alfabetica seria dar admin a quem chegou primeiro no alfabeto; e, de proposito, seria um
	/// roubo barato: bastaria criar uma conta com um personagem homonimo e esperar.
	/// </summary>
	private AccountSave? AcharConta(ServerPlayer adm, string quem)
	{
		AccountSave? a = _store!.Achar(quem, out List<string> empate);
		if (a != null) return a;
		if (empate.Count > 1)
			Avisar(adm, $"'{quem}' e o nome de personagem de {empate.Count} contas ({string.Join(", ", empate)})"
					  + " -- use o nome da CONTA.");
		else
			Avisar(adm, $"nao achei conta nem personagem chamado '{quem}'.");
		return null;
	}

	/// <summary>Esta conta esta online com poder de admin AGORA? (inclui o host, que nao tem marca no disco)</summary>
	private bool EstaOnlineComoAdmin(string conta) =>
		_players.Values.Any(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase) && EhAdmin(p));

	/// <summary>
	/// PROMOVE OU REBAIXA UMA CONTA. E o `Admin()`/`AdminDemote()` do original, com uma diferenca:
	/// la havia cinco niveis, aqui ha um. Cinco niveis existiam pra separar quem podia banir de
	/// quem podia mexer em balanceamento -- e os verbs de balanceamento nao foram portados.
	///
	/// O EFEITO E IMEDIATO PRA QUEM ESTA ONLINE: o bit entra em `PoderesConcedidos` e o proximo
	/// pacote de atributos leva a aba pro menu dela. Sem isso, promover exigiria "agora desloga e
	/// loga de novo", que e a marca de um sistema que nao ligou as pontas.
	/// </summary>
	private void AdminPromover(ServerPlayer adm, string quem, bool virar)
	{
		if (_store == null) { Avisar(adm, "este servidor nao guarda contas em disco."); return; }

		AccountSave? a = AcharConta(adm, quem);
		if (a == null) return;

		// NINGUEM SE REBAIXA SOZINHO se for o unico. Um servidor sem admin nenhum e um servidor em
		// que ninguem mais promove ninguem -- so mexendo no JSON com o jogo fechado.
		if (!virar && string.Equals(a.Conta, adm.Conta, StringComparison.OrdinalIgnoreCase)
			&& (_store.Todas().Count(c => c.Admin) <= 1))
		{
			Avisar(adm, "voce e o unico administrador com conta marcada -- promova alguem antes.");
			return;
		}

		a.Admin = virar;
		_store.Gravar(a);

		// A conta em memoria do jogador e OUTRA instancia (carregada no login dele). Ver
		// `EmTodaCopiaViva`: sem atualizar as DUAS colecoes, o proximo save dele regravaria o
		// arquivo com a marca velha e desfaria isto sozinho.
		EmTodaCopiaViva(a.Conta, v => v.Admin = virar);

		// ============================ O BIT NAO ESPERA O PROXIMO TIQUE ============================
		// `AplicarPoderes` zera a assinatura pra o pacote de atributos sair "no proximo tique", e
		// era so isso que ligava a promocao a aba do menu do outro lado. O dono reportou o furo:
		// promover alguem online nao dava a aba a ele nem fechando e reabrindo o menu -- so depois
		// de relogar, que e o caminho do disco.
		//
		// Zerar assinatura e uma DICA ("da pra mandar"); mandar e outra coisa. Aqui o pacote sai na
		// hora, no mesmo lugar onde a decisao foi tomada, e a promocao deixa de depender de outro
		// laco lembrar dela.
		//
		// O CONTADOR EXISTE porque o modo de falhar deste laco e silencioso: se nenhum
		// `ServerPlayer` casar com a conta, a promocao grava em disco e some da sessao sem uma
		// linha sequer. Dizer "0 online" e a diferenca entre um bug visivel e um bug invisivel.
		int online = 0;
		foreach (ServerPlayer p in _players.Values.Where(
			p => string.Equals(p.Conta, a.Conta, StringComparison.OrdinalIgnoreCase)))
		{
			if (virar) p.PoderesConcedidos |= Protocol.Poder.Admin;
			else if (!EhHost(p.Peer)) p.PoderesConcedidos &= ~Protocol.Poder.Admin;   // o host nunca perde
			AplicarPoderes(p);
			MandarAtributos(p);
			Avisar(p, virar ? "voce recebeu poderes de administrador." : "seus poderes de administrador foram retirados.");
			online++;
		}

		GD.Print($"[server] conta '{a.Conta}' {(virar ? "promovida" : "rebaixada")}"
				 + $" -- {online} personagem(ns) online receberam o bit na hora");

		Registrar(adm, $"{(virar ? "promoveu" : "rebaixou")} a conta '{a.Conta}'");
		Avisar(adm, $"conta '{a.Conta}' {(virar ? "agora e administradora" : "nao e mais administradora")}.");
		MandarContas(adm);
	}

	/// <summary>
	/// BANE OU PERDOA UMA CONTA (`Punishments.dm`). Banir DERRUBA quem esta jogando -- deixar o
	/// banido terminando a luta seria banir amanha.
	/// </summary>
	private void AdminBanir(ServerPlayer adm, string quem, bool banir)
	{
		if (_store == null) { Avisar(adm, "este servidor nao guarda contas em disco."); return; }

		AccountSave? a = AcharConta(adm, quem);
		if (a == null) return;

		// NAO SE BANE ADMINISTRADOR -- e a pergunta e pelo PODER, nao pela marca no arquivo.
		//
		// A guarda antiga olhava so `a.Admin`, e o host nao tem essa marca: o admin dele vem do
		// endereco. Ou seja, um amigo recem-promovido podia banir o DONO do servidor, que era
		// derrubado na hora e barrado no `Login` seguinte -- antes de qualquer linha de
		// "o host nunca perde" ter chance de rodar.
		if (banir && (a.Admin || EstaOnlineComoAdmin(a.Conta)))
		{
			Avisar(adm, "rebaixe antes de banir um administrador.");
			return;
		}
		if (banir && string.Equals(a.Conta, adm.Conta, StringComparison.OrdinalIgnoreCase))
		{
			Avisar(adm, "banir a si mesmo nao e um poder, e um acidente.");
			return;
		}

		a.Banida = banir;
		a.MotivoDoBanimento = banir ? $"banida por {adm.Name}" : "";
		_store.Gravar(a);
		EmTodaCopiaViva(a.Conta, v => { v.Banida = banir; v.MotivoDoBanimento = a.MotivoDoBanimento; });

		if (banir)
		{
			foreach (ServerPlayer p in _players.Values
				.Where(p => string.Equals(p.Conta, a.Conta, StringComparison.OrdinalIgnoreCase)).ToList())
			{
				Avisar(p, "voce foi banido deste servidor.");
				p.Peer?.Disconnect();
			}
			// E QUEM ESTA NA TELA DE SELECAO. Essa gente nao tem `ServerPlayer`, entao o laco acima
			// nao a alcanca -- e sem isto ela clicava num slot e entrava no mundo banida.
			foreach (NetPeer preso in PeersNaSelecao(a.Conta)) preso.Disconnect();
		}

		Registrar(adm, $"{(banir ? "baniu" : "perdoou")} a conta '{a.Conta}'");
		Avisar(adm, $"conta '{a.Conta}' {(banir ? "banida" : "perdoada")}.");
		MandarContas(adm);
	}

	// =====================================================================
	// OS VERBS
	// =====================================================================
	/// <summary>
	/// O `switch` dos comandos de admin. Chamado pelo canal unico de verbs, DEPOIS de conferir
	/// <see cref="EhAdmin"/> -- ver `GameServer.Verbos.cs`.
	/// </summary>
	private bool VerboDeAdmin(ServerPlayer pl, string cmd, string arg)
	{
		// O RASTRO SAI DAQUI, do funil, e nao de dentro de cada verb.
		//
		// A primeira versao chamava `Registrar` em oito lugares escolhidos a dedo -- e ficaram de
		// fora justamente os que ninguem lembra de logar e alguem vai contestar depois: calar, dar
		// cem marcos, dominar a forma de alguem, derrubar a zona inteira. Uma linha aqui cobre
		// todos, inclusive os que ainda nao existem.
		//
		// Os `Registrar` de dentro de alguns verbs CONTINUAM: eles acrescentam o que so se sabe
		// depois de resolver (o nome de quem foi banido, quantas celulas voltaram).
		if (cmd != "admin_contas") Registrar(pl, arg.Length > 0 ? $"{cmd} [{arg}]" : cmd);

		switch (cmd)
		{
			// ---------------------------------------------------------- sobre o alvo marcado
			case "admin_curar": AdminCurar(pl, arg); break;
			case "admin_ir": AdminIr(pl, arg); break;
			case "admin_trazer": AdminTrazer(pl, arg); break;
			case "admin_kb": AdminNocautear(pl, arg); break;
			case "admin_matar": AdminMatar(pl, arg); break;
			case "admin_reviver": AdminReviver(pl, arg); break;
			case "admin_ficha": AdminFicha(pl, arg); break;
			case "admin_spawn_alvo": AdminMandarProSpawn(pl, arg); break;
			case "admin_expulsar": AdminExpulsar(pl, arg); break;
			case "admin_calar": AdminCalar(pl, arg); break;
			case "admin_marco": AdminMarcos(pl, arg, todos: false); break;
			case "admin_dominar": AdminDominarForma(pl, arg); break;
			// FERRAMENTAS DE TESTE: forcam o que o jogo cobra caro. Ver `AdminForcarForma`.
			case "admin_forma": AdminForcarForma(pl, arg); break;
			case "admin_liberar_ki": AdminLiberarKi(pl, arg); break;
			case "admin_skill_dar": AdminSkill(pl, arg, dar: true); break;
			case "admin_skill_tirar": AdminSkill(pl, arg, dar: false); break;
			case "admin_cargo_dar": AdminOutorgarCargo(pl, arg); break;
			case "admin_pm": AdminPm(pl, arg); break;

			// ---------------------------------------------------------- sobre o mundo
			case "admin_curar_todos": AdminCurarTodos(pl); break;
			case "admin_kb_todos": AdminNocautearTodos(pl); break;
			case "admin_trazer_todos": AdminTrazerTodos(pl); break;
			case "admin_marco_todos": AdminMarcos(pl, arg, todos: true); break;
			case "admin_descalar": AdminDescalarTodos(pl); break;
			case "admin_consertar_cenario": AdminConsertarCenario(pl); break;
			case "admin_salvar": AdminSalvarTudo(pl); break;
			case "admin_anunciar": AdminAnunciar(pl, arg); break;

			// ---------------------------------------------------------- ceu (ver GameServer.Clima.cs)
			case "admin_clima": AdminClima(pl, arg); break;
			case "admin_clima_natural": AdminClimaNatural(pl); break;
			case "admin_lua_cheia": AdminLuaCheia(pl); break;
			case "admin_meio_dia": AdminMeioDia(pl); break;
			case "admin_clima_ficha": AdminClimaFicha(pl); break;

			// ---------------------------------------------------------- informacao
			case "admin_quem": VerboQuem(pl); break;
			case "admin_fichas": AdminFichaDeTodos(pl); break;
			case "admin_racas": AdminRacas(pl); break;
			case "admin_ips": AdminIps(pl); break;
			case "admin_galaxia": AdminGalaxia(pl); break;
			// "me poe dentro da estrela mais proxima" -- ver GameServer.Universo.cs.
			case "admin_estrela": AdminIrParaAEstrela(pl); break;

			// ---------------------------------------------------------- sobre mim
			case "admin_invisivel": AdminInvisivel(pl); break;

			// ---------------------------------------------------------- contas
			case "admin_contas": MandarContas(pl); break;
			case "admin_promover": AdminPromover(pl, arg, virar: true); break;
			case "admin_rebaixar": AdminPromover(pl, arg, virar: false); break;
			case "admin_banir": AdminBanir(pl, arg, banir: true); break;
			case "admin_perdoar": AdminBanir(pl, arg, banir: false); break;

			default: return false;
		}
		return true;
	}

	/// <summary>
	/// Acha alguem pelo ID que o cliente mandou -- e o ALVO MARCADO dele.
	///
	/// Id e nao nome: nome se repete e nome se digita errado, e o jogador ja marca gente com duplo
	/// clique pra tudo o mais. Nulo/0 = "eu mesmo", que e o que os verbs que curam querem.
	/// </summary>
	private ServerPlayer? PorNome(string idTexto) =>
		int.TryParse(idTexto, out int id) && id != 0 && _players.TryGetValue(id, out ServerPlayer? p) ? p : null;

	private void AdminCurar(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo) ?? adm;   // sem alvo marcado, cura quem pediu
		Restaurar(p);
		Avisar(adm, $"{p.Name} curado.");
		if (p != adm) Avisar(p, "voce se sente inteiro de novo.");
	}

	/// <summary>
	/// Corpo inteiro, Ki cheio, de pe. E o `World_Heal` do original aplicado a um so.
	///
	/// Passa pelo `Reviver` do estado de combate em vez de escrever `HP = 100` na mao: e ele que
	/// devolve os MEMBROS (inclusive um decepado) e recalcula a media que vira a vida. Mexer so na
	/// ficha deixaria o boneco de pe com o braco quebrado.
	/// </summary>
	private void Restaurar(ServerPlayer p)
	{
		// A CARENCIA VEM JUNTO, como em toda ressurreicao do jogo (`Renascer`, e a tecnica de
		// reviver). Sem ela, curar alguem caido ao lado de quem o derrubou e devolve-lo pro mesmo
		// golpe no tique seguinte -- o laco de morte que a carencia existe pra cortar.
		p.Combate.Reviver(1, SegundosDeCarencia);

		// O RABO VOLTOU: o ritmo de treino tem que voltar junto.
		//
		// `Reviver` chama `Corpo.Restaurar()`, que descepa TUDO -- rabo incluso. Mas quem escreve
		// `tailgain` (1,25 sem rabo, 0,5 com) e o `AjustarGanhoDoRabo`, e ele nao roda sozinho. Sem
		// esta linha, um Saiyajin que perdesse o rabo e pedisse "Heal" a um admin ficava com o rabo
		// de volta E com o bonus de quem nao tem: 2,5x de ganho, pra sempre. O `Renascer` chama os
		// dois lado a lado justamente por isso.
		AjustarGanhoDoRabo(p);

		// CURAR NAO TIRA. Irmao do `Nutricao.cs:134` e do `if (primeira)` do `GameServer.Formas.cs`:
		// `Ki = MaxKi` seco e um PRESENTE que vira CORTE, e este verb tambem cura quem esta de pe --
		// um jogador com o Ki comprimido a 140% pela tecla C pedia "Heal" e voltava pra 100%. O
		// original nunca corta a sobrecarga por decreto (`Power Control.dm:113-134`: ela vaza).
		p.Ficha.Ki = Math.Max(p.Ficha.MaxKi, p.Ficha.Ki);
		p.RenasceEm = 0;
		MandarFicha(p);
		MandarCorpo(p);
	}

	private void AdminIr(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, "marque alguem antes (duplo clique nele)."); return; }
		MoveToZone(adm.Id, p.Zone, p.Pos);
		Avisar(adm, $"voce vai ate {p.Name}.");
	}

	private void AdminTrazer(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, "marque alguem antes (duplo clique nele)."); return; }
		MoveToZone(p.Id, adm.Zone, adm.Pos);
		Avisar(adm, $"voce traz {p.Name}.");
		Avisar(p, "uma forca invisivel te puxa.");
	}

	private void AdminNocautear(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, "marque alguem antes (duplo clique nele)."); return; }
		Nocautear(p);
		Avisar(adm, $"{p.Name} cai no chao.");
	}

	/// <summary>
	/// PARA de pe, pelo MESMO caminho de um nocaute de soco.
	///
	/// ============================ NAO E `Ficha.KO = true` ============================
	/// A versao anterior deste verb escrevia o campo na mao. Duas coisas quebravam com isso:
	///   * `NocauteRestante` ficava em zero, e e ele que faz o corpo LEVANTAR. Sem prazo, o
	///     nocaute virava permanente -- so um Heal tirava a pessoa do chao.
	///   * a guarda e o contra-ataque continuavam armados num corpo desmaiado.
	/// `CombatState.Nocautear` cuida dos tres, e e o que o `MeleeResolver` chama quando um soco
	/// derruba alguem. Um verb de admin que derruba diferente de um soco e um segundo caminho
	/// pra manter em sincronia.
	/// =================================================================================
	/// </summary>
	private void Nocautear(ServerPlayer p)
	{
		p.Combate.Nocautear(Jandirus.Core.Combat.MeleeResolver.SegundosDeNocaute);
		MandarFicha(p);
	}

	private void AdminMatar(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, "marque alguem antes (duplo clique nele)."); return; }
		// `Morrer()` E O CAMINHO DE QUALQUER MORTE, e faz o que um `dead = true` na mao nao faz:
		// zera KO, guarda, nocaute restante e as duas marcas de combate. Sem ele o ressuscitado
		// voltava com a tag de luta ainda correndo (90s) -- e o tick so regenera fora de combate,
		// entao ele ficava mais de um minuto parado com meia vida sem se recuperar. De quebra, aos
		// 12s o nocaute "vencia" e o console anunciava que um morto tinha levantado.
		// `ignorarSeguro: true` -- o verb de admin passa por cima da Aura of Destruction. Uma
		// ferramenta que uma mecanica de jogador bloqueia deixa de ser ferramenta.
		p.Combate.Morrer(ignorarSeguro: true);
		p.RenasceEm = NowMs() + MsAteRenascer;   // o mesmo prazo de qualquer morte
		MandarFicha(p);
		Avisar(adm, $"{p.Name} morreu.");
		Avisar(p, "uma mao invisivel apaga a sua luz.");
		Registrar(adm, $"matou {p.Name} ({p.Conta})");
	}

	private void AdminReviver(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo) ?? adm;
		Restaurar(p);
		Avisar(adm, $"{p.Name} esta de pe.");
		if (p != adm) Avisar(p, "voce volta a si.");
	}

	private void AdminMandarProSpawn(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, "marque alguem antes (duplo clique nele)."); return; }
		// PRO BERCO DELE, e nao pra Terra: o `Send_Spawn` do original (`Admin.dm:30`) chama o mesmo
		// `Locate()` do nascimento. Um admin que quer devolver alguem "pro comeco" quer o comeco
		// DAQUELA pessoa -- mandar todo mundo pra Terra seria uma mudanca de planeta disfarcada.
		MandarProBerco(p);
		Avisar(adm, $"{p.Name} foi mandado pro ponto de partida.");
		Avisar(p, "o mundo te devolve ao comeco.");
	}

	/// <summary>
	/// O alvo e uma PESSOA, e nao um corpo sem dono?
	///
	/// Duplo clique marca qualquer coisa que apareca na tela, e o clone da mente e os NPCs aparecem
	/// (`Peer == null`, `Cerebro != null`). Expulsar ou calar um deles nao faz nada -- e o verb
	/// diria que fez. Pior no caso do mute: a conta de um clone e "", e calar "" enfia uma entrada
	/// fantasma no conjunto que o "Unmute All" depois contaria como se fosse gente.
	/// </summary>
	private bool EhPessoa(ServerPlayer adm, ServerPlayer p)
	{
		if (p.Peer != null) return true;
		Avisar(adm, $"{p.Name} nao e um jogador -- e um corpo sem dono.");
		return false;
	}

	private void AdminExpulsar(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		if (p == null) { Avisar(adm, "marque alguem antes (duplo clique nele)."); return; }
		if (p == adm) { Avisar(adm, "expulsar a si mesmo nao e um poder."); return; }
		if (!EhPessoa(adm, p)) return;
		Avisar(p, "voce foi desconectado por um administrador.");
		// GRAVA ANTES DE DERRUBAR. O `Drop` do evento de desconexao tambem persiste, mas depender
		// dele e depender de o evento chegar -- e expulsar alguem nunca pode custar o progresso
		// dele. Salvar duas vezes nao machuca; salvar zero, sim.
		Persistir(p);
		Registrar(adm, $"expulsou {p.Name} ({p.Conta})");
		Avisar(adm, $"{p.Name} foi desconectado.");
		p.Peer?.Disconnect();
	}

	/// <summary>
	/// CALA OU DESCALA. Alterna, e a marca vai pro DISCO -- ver <see cref="AccountSave.Calada"/>.
	///
	/// Aceita o alvo marcado (o id, pra quem esta na tela) OU um nome digitado, que e o unico jeito
	/// de descalar quem ja saiu. Sem o segundo caminho, calar alguem que desconectasse em seguida
	/// era uma punicao sem botao de desfazer.
	/// </summary>
	private void AdminCalar(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo);
		AccountSave? conta;

		if (p != null)
		{
			if (!EhPessoa(adm, p)) return;
			conta = _store?.Carregar(p.Conta);
		}
		else
		{
			conta = AcharConta(adm, alvo);
			if (conta == null) return;   // o `AcharConta` ja explicou o motivo
		}
		if (conta == null) { Avisar(adm, "nao achei a conta desse jogador."); return; }

		bool calar = !conta.Calada;
		conta.Calada = calar;
		_store?.Gravar(conta);
		EmTodaCopiaViva(conta.Conta, v => v.Calada = calar);
		if (calar) _calados.Add(conta.Conta); else _calados.Remove(conta.Conta);

		string nome = p?.Name ?? conta.Conta;
		Avisar(adm, calar ? $"{nome} foi calado." : $"{nome} pode falar de novo.");
		if (p != null) Avisar(p, calar ? "um administrador te calou." : "voce voltou a poder falar.");
	}

	private void AdminDescalarTodos(ServerPlayer adm)
	{
		int n = 0;
		foreach (AccountSave a in _store?.Todas() ?? [])
		{
			if (!a.Calada) continue;
			a.Calada = false;
			_store!.Gravar(a);
			EmTodaCopiaViva(a.Conta, v => v.Calada = false);
			n++;
		}
		_calados.Clear();
		Avisar(adm, n == 0 ? "nao havia ninguem calado." : $"{n} conta(s) voltaram a poder falar.");
	}

	/// <summary>
	/// MARCOS DE SKILL -- o `Reward`/`Global_Points` do original ("Global - Give all Milestones").
	/// O argumento e quantos; sem numero, um.
	/// </summary>
	private void AdminMarcos(ServerPlayer adm, string arg, bool todos)
	{
		if (!int.TryParse(arg.Split('|').Last(), out int quantos) || quantos <= 0) quantos = 1;
		quantos = Math.Min(quantos, 100);   // teto de dedo escorregado, nao de balanceamento

		if (todos)
		{
			int n = 0;
			foreach (ServerPlayer p in Gente.ToList())
			{
				p.Livro.Conceder(quantos);
				MandarSkills(p, forcar: true);
				Avisar(p, $"voce recebeu {quantos} marco(s) de habilidade.");
				n++;
			}
			Avisar(adm, $"{quantos} marco(s) pra cada um dos {n}.");
			return;
		}

		ServerPlayer? alvo = PorNome(arg.Split('|').First()) ?? adm;
		alvo.Livro.Conceder(quantos);
		MandarSkills(alvo, forcar: true);
		Avisar(adm, $"{alvo.Name} recebeu {quantos} marco(s).");
		if (alvo != adm) Avisar(alvo, $"voce recebeu {quantos} marco(s) de habilidade.");
	}

	/// <summary>
	/// DOMINA A FORMA EM QUE O ALVO ESTA -- o `Edit_Masteries` do original.
	///
	/// So a forma ATUAL, de proposito: maestria e a coisa mais cara do jogo (tres horas dentro da
	/// forma, gastando Ki) e "dominar tudo" apagaria a progressao inteira de alguem num clique.
	/// Na base nao ha o que dominar.
	/// </summary>
	private void AdminDominarForma(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo) ?? adm;
		if (p.Forma.NaBase) { Avisar(adm, $"{p.Name} esta na forma base -- nao ha maestria a dar."); return; }

		p.Forma.Maestria.Por(p.Forma.Atual, 100);
		// O MULTIPLICADOR DEPENDE DA MAESTRIA (a forma so vale cheio a 100%). Escrever a maestria
		// sem reaplicar deixaria o BP no valor da maestria VELHA ate a proxima transformacao -- o
		// admin veria "100%" na aba e nada teria mudado de fato.
		AplicarForma(p);

		// ============================ O NOME SE LE DEPOIS DO `Por`, E ESSE E O PONTO ============================
		// Ele era lido ANTES (`string f = ...` la em cima), e neste verb especificamente isso e mentira:
		// este e o unico lugar do jogo onde a maestria pula pra 100 num instante, e e exatamente a
		// travessia dos 100% que renomeia o Super Saiyajin pra "Grade 4" (`Catalogo.NomeDe`). Lido
		// antes, o verb que ACABOU de dominar a forma a anunciaria pelo nome de quem nao a domina --
		// e o proximo texto que o jogador visse (a aba Formas, o cabelo `SSjFP`) ja diria outra coisa.
		// ====================================================================================================
		string f = Catalogo.NomeDe(p.Forma.Def, p.Forma.Maestria);
		Avisar(adm, $"{p.Name} agora domina {f}.");
		if (p != adm) Avisar(p, $"a forma {f} nao tem mais segredos pra voce.");
	}

	// =====================================================================
	// FORCAR UMA FORMA -- a ferramenta de teste que o dono pediu
	// =====================================================================
	/// <summary>
	/// A LISTA DE IDS, por linha. E o `admin_forma` sem argumento.
	///
	/// Por LINHA e nao numa fila so porque a lista tem mais de trinta entradas e a pergunta que o
	/// admin faz e sempre "quais sao os degraus da escada X". O mesmo desenho do `admin_clima`, que
	/// responde "aqui podem cair: ..." quando nao se diz qual.
	/// </summary>
	private void ListarFormas(ServerPlayer adm)
	{
		Avisar(adm, $"-- formas (use o id) -- volta ao normal: {Catalogo.IdBase}");
		foreach (IGrouping<LinhaDeForma, FormaDef> g in Catalogo.Todas
			.Where(d => d.Id != Catalogo.IdBase)
			.GroupBy(d => d.Linha))
			Avisar(adm, $"  {g.Key}: " + string.Join(", ", g.OrderBy(d => d.Ordem).Select(d => d.Id)));
	}

	/// <summary>
	/// Acha a forma pelo id ou pelo nome.
	///
	/// SEM CAIXA nos dois, e nao pelo <see cref="Catalogo.Def"/>: o indice dele e Ordinal, e o admin
	/// digita "SSJ1" olhando pra um id escrito "ssj1". Pelo nome porque e o que a aba Formas mostra
	/// ("Super Saiyajin"), e ninguem decora id de catalogo.
	/// </summary>
	private static FormaDef? AcharForma(string texto) =>
		Array.Find(Catalogo.Todas, d => string.Equals(d.Id, texto, StringComparison.OrdinalIgnoreCase))
		?? Array.Find(Catalogo.Todas, d => string.Equals(d.Nome, texto, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// FORCA UMA FORMA, IGNORANDO TODOS OS REQUISITOS -- BP, maestria, linhagem, classe, degrau
	/// anterior, raca e Ki.
	///
	/// ============================ POR QUE ELA NAO RESPEITA AS PORTAS ============================
	/// Porque as formas que interessa testar SAO as trancadas. Uma ferramenta que passasse pelo
	/// <see cref="EstadoDeForma.Avaliar"/> so alcancaria o que o personagem ja alcanca sozinho, e o
	/// pedido do dono foi literalmente o contrario: "forçar me transformar em qualquer transformaçao
	/// do jogo pra eu testar". Chegar ao SSJ4 pelo caminho legitimo custa Oozaru Dourado dominado
	/// (100% de maestria = horas dentro da fera); ao Blue, ki divino a 33%.
	///
	/// ============================ MAS ELA PASSA PELOS MESMOS FUNIS ============================
	/// `Entrar` -> `AplicarForma` -> `AnunciarForma` na escada, e <see cref="VirarFera"/> no macaco.
	/// Escrever `pl.Forma.Atual` na mao aqui daria um jogo que nao existe: sem `AplicarForma` o
	/// `ssjBuff` e o teto de Ki nao mudam (o admin veria o nome da forma e o mesmo BP), e sem
	/// `AnunciarForma` a zona inteira -- inclusive a tela do proprio dono -- continuaria desenhando o
	/// corpo base. Esse ultimo defeito ja aconteceu de verdade neste arquivo (ver o bloco "A ZONA NAO
	/// SABIA DISTO" no `DesfazerOozaru`), e o custo dele e o admin concluir que a forma esta quebrada
	/// quando o que esta quebrado e a ferramenta.
	///
	/// ============================ A CINEMATICA ROLA, PELA REGRA NORMAL ============================
	/// `primeira` e o retorno do `Entrar` e vai cru pro `AnunciarForma`, que deriva o degrau pelo
	/// <see cref="Cinematicas.Degrau"/> como qualquer transformacao: cena CHEIA na estreia, cena
	/// encurtada ate 50% de maestria, instantanea depois.
	///
	/// Nao ha `semCena: true` aqui, e a escolha e deliberada. A cinematica e justamente uma das
	/// coisas que o dono quer ver ao testar, e ela e a MAIS dificil de alcancar em jogo: so toca uma
	/// vez por personagem e so atras da porta da forma. Uma ferramenta de teste que a suprimisse
	/// tornaria intestavel exatamente o que ela existe pra mostrar. A unica `semCena: true` do jogo
	/// continua sendo a queda do Oozaru Dourado pro SSJ4, que nao e um gesto do jogador -- esta e.
	///
	/// Como o `Entrar` marca a estreia, forcar a mesma forma duas vezes da cena cheia na primeira e a
	/// regra normal na segunda -- que e o comportamento desejado: da pra testar as duas.
	/// =========================================================================================
	///
	/// O ARGUMENTO E "alvoId|forma" OU so "forma" (sobre mim), como o `admin_skill_dar`.
	/// </summary>
	private void AdminForcarForma(ServerPlayer adm, string arg)
	{
		string[] partes = arg.Split('|', 2);
		ServerPlayer p = (partes.Length > 1 ? PorNome(partes[0]) : null) ?? adm;
		string texto = (partes.Length > 1 ? partes[1] : partes[0]).Trim();

		if (texto.Length == 0) { ListarFormas(adm); return; }

		FormaDef? d = AcharForma(texto);
		if (d == null)
		{
			Avisar(adm, $"nao existe forma '{texto}' -- mande `admin_forma` sem argumento pra ver a lista.");
			return;
		}

		// ============================ O NOME E O DO ALVO, E NAO O DA ENTRADA ============================
		// Cinco frases deste metodo nomeiam a forma, e todas falam sobre `p` -- que pode ser OUTRO
		// jogador. Entao o livro que decide se o Super Saiyajin dele se chama "Grade 4" e o DELE
		// (`p.Forma.Maestria`), nunca o do admin. Ver `Catalogo.NomeDe`.
		//
		// (Este e o lado servidor da mesma decisao anotada no painel de formas do cliente, que mostra
		// `d.Nome` cru justamente porque LA a maestria do alvo nao existe.)
		// ============================================================================================
		string nome = Catalogo.NomeDe(d, p.Forma.Maestria);

		// ---------------------------------------------------------- 1. A BASE DESFAZ AS DUAS COISAS
		// `base` tem que estar na lista, e ela e a unica entrada que precisa mexer nos DOIS estados:
		// a escada (`EstadoDeForma.Atual`) e a fera (`ServerPlayer.Oozaru`), que sao paralelos. Sem a
		// primeira metade, forcar `base` num macaco nao faria nada visivel e o admin ficaria preso
		// nele ate o prazo vencer.
		if (d.Id == Catalogo.IdBase)
		{
			// `Reverter` E O FUNIL DA DESCIDA (GameServer.Formas.cs) -- ele ja recebe o motivo, ja
			// chama `AplicarForma` e ja anuncia pra zona. Uma copia local destas quatro linhas foi
			// escrita aqui e apagada: e a segunda copia que envelhece no dia em que a descida ganhar
			// mais um passo.
			bool mexeu = false;
			if (p.Oozaru != FormaOozaru.Nao) { DesfazerOozaru(p, "uma mao invisivel desfaz a fera."); mexeu = true; }
			if (!p.Forma.NaBase) { Reverter(p, "uma mao invisivel te devolve a forma base."); mexeu = true; }
			Avisar(adm, mexeu ? $"{p.Name} voltou a forma base." : $"{p.Name} ja estava na forma base.");
			return;
		}

		// ---------------------------------------------------------- 2. A FERA NAO ESTA NA ESCADA
		// O Oozaru e o Dourado moram no catalogo (pra terem nome, corpo e cor) mas o estado deles e
		// paralelo -- `Catalogo.NaoSeSobePraEla` existe justamente pra o C nunca os oferecer. Entrar
		// neles e o `VirarFera`, e nao o `Entrar`.
		if (d.Linha == LinhaDeForma.Oozaru)
		{
			FormaOozaru fera = d.Id == Oozaru.IdDourado ? FormaOozaru.Dourado : FormaOozaru.Regular;
			if (p.Oozaru == fera) { Avisar(adm, $"{p.Name} ja e {nome}."); return; }
			if (p.Oozaru != FormaOozaru.Nao) DesfazerOozaru(p, "a fera se desfaz pra dar lugar a outra.");

			VirarFera(p, fera);

			// O RABO DERRUBA A FERA NO TIQUE SEGUINTE (`TickDoOozaru`, passo 1: `if(!container.Tail)
			// DeBuff()`). Forcar sem rabo "funciona" por uma fracao de segundo e some -- e o admin
			// leria isso como a forca nao ter pegado. Avisar e mais barato que recusar: recusar
			// tiraria da ferramenta justamente o corpo sem rabo, que tambem e um caso a olhar.
			if (!TemRaboInteiro(p))
				Avisar(adm, "AVISO: sem rabo inteiro a fera cai no proximo tique -- cure o alvo antes.");

			// o rastro sai do funil do `VerboDeAdmin` (uma linha por comando) -- nao ha nada aqui que
			// so se saiba depois de resolver, e o `VirarFera` ja imprime o macaco no console
			Avisar(adm, $"{p.Name} agora e {nome}.");
			return;
		}

		// ---------------------------------------------------------- 3. A ESCADA
		if (p.Forma.Atual == d.Id) { Avisar(adm, $"{p.Name} ja esta em {nome}."); return; }

		// A FERA SAI PRIMEIRO, e nao e cortesia: `AplicarOozaru` escreve o multiplicador do macaco no
		// MESMO `ssjBuff` que a escada usa (o Dourado vale 18x ali). Forcar uma forma por cima
		// deixaria o corpo com sprite de macaco e poder de SSJ, ou o contrario -- e a `DesfazerOozaru`
		// e quem devolve as somas de physoff/speed/tecnica e as redeas, se a fera tiver assumido.
		if (p.Oozaru != FormaOozaru.Nao) DesfazerOozaru(p, "a fera se desfaz pra dar lugar a outra forma.");

		string anterior = p.Forma.Atual;
		bool primeira = p.Forma.Entrar(d.Id);

		// KI CHEIO SEMPRE, e nao so na estreia como no caminho normal (`Transformar`).
		//
		// O QUE SE GANHOU: a forma forcada dura o bastante pra ser olhada. `AplicarForma` preserva a
		// RAZAO de Ki na troca, entao um admin com 20% de tanque que forca SSJ3 entra com 20% de um
		// tanque maior e o `TickDaForma` o derruba em segundos -- "nao funcionou", diria ele.
		// O QUE SE PERDEU: forcar deixa de ser neutro em relacao ao Ki. E aceitavel num verb que ja
		// ignora BP, maestria e linhagem; e o `admin_curar` faz o mesmo com o corpo.
		//
		// ============================ E ELE ENCHE ANTES DO `AplicarForma`, NAO DEPOIS ============================
		// Encher depois enchia o tanque e deixava a CONTA DE PODER com a razao velha: o `kiratio` e
		// fator do `expressedBP` (`Fighter.Power.cs:50`) e quem o le e o `PowerLevel` que roda dentro
		// do `AplicarForma`. Era o segundo defeito do relato do dono -- o MESMO x6 imprimindo 560 e
		// depois 545, dois segundos e ~0,6 s de dreno de SSJ1 de diferenca.
		//
		// Encher ANTES resolve sem uma segunda conta: `AplicarForma` preserva a RAZAO, e a razao aqui
		// e 1. Cheio contra o teto velho vira cheio contra o teto novo, e o poder impresso e o de um
		// corpo com o tanque cheio -- que e o corpo que o admin acabou de criar.
		//
		// ============================ E `Max` E NAO ATRIBUICAO SECA ============================
		// A atribuicao seca fazia forcar uma forma DESFAZER a tecla C: um admin a 140% caia pra 100%.
		// Mesmo defeito do caminho do jogador (`GameServer.Formas.cs`, `if (primeira)`), do
		// `Nutricao.cs:134` e da absorcao Majin -- presente escrito como escrita absoluta vira corte.
		// O que este verb promete e "o tanque nao fica vazio", nao "o tanque fica exatamente em 100%".
		//
		// A BANCADA SEGUE VALENDO: `GameServer.AdminTeste.cs:235` planta 20% de tanque antes de cada
		// `admin_forma` justamente pra que "cheio no fim" so possa ter vindo daqui, e a 20% o `Max`
		// devolve o `MaxKi` igualzinho. O que muda e so o caso do admin JA sobrecarregado, e ai o
		// `kiratio` propagado e o do corpo que existe de verdade -- que e mais honesto que pina-lo.
		// ==================================================================================================
		p.Ficha.Ki = Math.Max(p.Ficha.MaxKi, p.Ficha.Ki);
		AplicarForma(p);

		AnunciarForma(p, anterior, d.Id, primeira);

		// CAIDO NAO SUSTENTA FORMA: o passo 2 do `TickDaForma` reverte quem esta KO ou morto. Mesmo
		// caso do rabo -- avisar em vez de recusar.
		if (p.Ficha.KO || p.Ficha.dead)
			Avisar(adm, "AVISO: o alvo esta caido e o tique vai desfazer a forma -- cure-o antes.");

		// ============================ O QUE ESTES DOIS NUMEROS SAO, E O QUE NAO SAO ============================
		// `BP` e o BP BASE (o que o treino sobe, o que vai no save) e `expressedBP` e o PODER (o que o
		// scouter le e o dano usa). `BP x mult` NUNCA vai bater com o segundo, e isso nao e defeito: no
		// meio passam idade, estado do corpo, gravidade, raiva e os buffs que somam na base -- ver a
		// taxonomia no topo do `Fighter.Power.cs`. Trocar o print por `BP * mult` seria trocar um
		// numero verdadeiro por um numero bonito.
		//
		// O QUE SE MOVE ENTRE DOIS COMANDOS IGUAIS, e e comportamento e nao bug: o `kiratio`
		// (`Ki/MaxKi`, um dos fatores do `statusBuff`). A forma nao masterizada DRENA Ki, e a razao
		// atravessa a volta pra base -- entao forcar SSJ1, esperar, voltar e forcar de novo daria dois
		// poderes diferentes. Aqui o `Math.Max(MaxKi, Ki)` acima o leva pra 1 sempre que o admin
		// chega com o tanque abaixo do cheio -- ou seja em todo caso normal e em toda a bancada. Ele
		// so continua se movendo pra quem chega SOBRECARREGADO pela tecla C, e ai a razao impressa e
		// a do corpo de verdade. No caminho normal do jogador (`Transformar`) ele se move mesmo, e deve.
		//
		// E A OUTRA SURPRESA LEGITIMA, pra nao ser reportada como bug uma segunda vez: forcar um RAMO
		// (os grades do SSJ1) nao levanta o piso dos vizinhos. O piso por ramo existe
		// (`Formas.cs:3485`), mas so vale pra forma com `PisoSobreAnterior > 0` -- o SSJ2, nao o SSJ1
		// -- e o portao dele e a MAESTRIA do tronco, nao a lista de `Liberadas`. Forcar nao move
		// maestria nenhuma, entao o multiplicador do SSJ1 e o mesmo antes e depois de forcar o Grade 2.
		// ==================================================================================================
		double mult = MultiplicadorDaForma(p);   // com o fator do cargo -- ver `MultiplicadorDaForma`
		GD.Print($"[server] {adm.Name} FORCOU {p.Name}: {anterior} -> {d.Id} "
				 + $"(x{mult:0.##}, BP {p.Ficha.BP:N0} -> {p.Ficha.expressedBP:N0})"
				 + (primeira ? "  <- ESTREIA (cena cheia)" : ""));

		Avisar(adm, $"{p.Name} agora esta em {nome} (x{mult:0.##})"
				  + (primeira ? " -- ESTREIA, a cinematica toca." : "."));
		if (p != adm) Avisar(p, $"uma vontade alheia te empurra pra {nome}.");
	}

	/// <summary>
	/// LIBERA O KI do alvo -- as duas pecas que a tecla C exige.
	///
	/// A concessao em si mora em <see cref="LiberarOKi"/> (GameServer.Skills.cs), que e a MESMA
	/// funcao que a flag de bancada `--kiteste` usa no nascimento. Aqui so ha o que e de runtime: a
	/// ordem em que as pontas se religam depois de o livro mudar.
	///
	/// ============================ A ORDEM DAS QUATRO CHAMADAS ============================
	/// `Niveis.Aplicar` escreve o `canPower` (o degrau 5) e o `AplicarEfeitos` escreve o
	/// `MeditateGivesKiRegen` (a skill). Sao canais DIFERENTES -- um vem do `niveis.json`, o outro do
	/// `skills.json` -- e o `AplicarEfeitos` termina em `Statify`, que e quem recalcula o teto de Ki
	/// com o `kicapacity` novo. Chamar so um dos dois da meio Ki liberado, que e o modo de falhar
	/// silencioso deste sistema (ver o comentario do `LiberarOKi`).
	///
	/// `MandarSkills(forcar: true)` fecha o circuito com o cliente: o degrau 5 concede os verbs
	/// `Power_Control` e `Conceal_Power`, e o menu so os desenha quando a lista chega.
	/// ==================================================================================
	///
	/// CONFIRMA COM OS NUMEROS, e nao com "pronto". "Nao deu erro" nunca provou que os dois canais
	/// pegaram -- e estes dois ja se romperam neste projeto. Ver <see cref="ConferirKiLiberado"/>.
	/// </summary>
	private void AdminLiberarKi(ServerPlayer adm, string alvo)
	{
		ServerPlayer p = PorNome(alvo) ?? adm;
		// livro de corpo sem dono nao vai pra disco nenhum -- mesma guarda do `admin_skill_dar`
		if (!EhPessoa(adm, p)) return;

		LiberarOKi(p);
		p.Niveis.Aplicar(p.Ficha);
		AplicarPoderes(p);
		AplicarEfeitos(p);        // termina em `Statify`: o teto de Ki ja sai com o `kicapacity` novo
		MandarSkills(p, forcar: true);
		MandarFicha(p);

		bool ok = ConferirKiLiberado(p, "admin_liberar_ki");

		Avisar(adm, ok
			? $"{p.Name}: Ki liberado -- canPower={p.Ficha.canPower:0}, "
			  + $"MeditateGivesKiRegen={p.Ficha.MeditateGivesKiRegen:0}, "
			  + $"powerupcap={p.Ficha.powerupcap:0.00} (o C passa de 100%)."
			: $"{p.Name}: a concessao NAO pegou (canPower={p.Ficha.canPower:0}, "
			  + $"MeditateGivesKiRegen={p.Ficha.MeditateGivesKiRegen:0}) -- veja o console do servidor.");

		if (p != adm && ok) Avisar(p, "voce sente o proprio Ki se soltar: segure C pra carregar.");
	}

	/// <summary>
	/// SO GENTE DE VERDADE.
	///
	/// `_players` guarda tambem os corpos SEM DONO -- o clone da mente, os NPCs (`Peer == null`,
	/// `Cerebro != null`). Eles andam no snapshot como qualquer um, e por isso todo laco "em massa"
	/// que esquecer este filtro vai curar, arrastar e contar robo junto com jogador. "3 no mundo"
	/// quando ha uma pessoa so e um numero que faz o admin duvidar do painel inteiro.
	/// </summary>
	private IEnumerable<ServerPlayer> Gente => _players.Values.Where(p => p.Peer != null);

	/// <summary>
	/// DA OU TIRA UMA SKILL do alvo marcado -- o `Give (Skill)` e o `Take_Skill` do original.
	///
	/// ============================ O `Esquecer` ERA ORFAO ============================
	/// `SkillBook.Esquecer` existe desde que o sistema de skills nasceu e NUNCA foi chamado por
	/// ninguem. Ou seja: um presente errado (ou uma skill dada por engano) nao tinha como ser
	/// desfeito -- so editando o JSON da conta com o servidor fechado. Este verb e a porta que
	/// faltava, e e o mesmo caso do `canPower` e da API do sigilo: regra escrita, regra nao ligada.
	/// ================================================================================
	///
	/// O ARGUMENTO E "alvoId|texto", e o texto casa por TYPEPATH ou por NOME. Typepath ninguem
	/// decora; nome e o que aparece na aba de skills.
	/// </summary>
	private void AdminSkill(ServerPlayer adm, string arg, bool dar)
	{
		string[] partes = arg.Split('|', 2);
		ServerPlayer? p = PorNome(partes[0]) ?? adm;
		if (!EhPessoa(adm, p)) return;   // livro de skill de clone nao vai pra disco nenhum
		string busca = (partes.Length > 1 ? partes[1] : "").Trim();
		if (busca.Length == 0) { Avisar(adm, "escreva o nome (ou o typepath) da skill."); return; }

		Jandirus.Core.Skills.Skill? s = _skills?.Get(busca)
			?? _skills?.Todas.FirstOrDefault(k => string.Equals(k.Nome, busca, StringComparison.OrdinalIgnoreCase));
		if (s == null) { Avisar(adm, $"nao achei skill chamada '{busca}'."); return; }

		if (dar)
		{
			if (p.Livro.Sabe(s.Path)) { Avisar(adm, $"{p.Name} ja sabe {s.Nome}."); return; }
			p.Livro.Dar(s.Path);
			Avisar(p, $"voce recebeu: {s.Nome}.");
		}
		else
		{
			if (!p.Livro.Sabe(s.Path)) { Avisar(adm, $"{p.Name} nao sabe {s.Nome}."); return; }
			p.Livro.Esquecer(s.Path);
			Avisar(p, $"voce esqueceu: {s.Nome}.");
		}

		// A MESMA TRINCA DE SEMPRE depois de mexer no livro (ver `GameServer.Skills.cs:119-124`):
		// a lista vai pro cliente, os bits de poder se refazem, e os efeitos da skill entram ou saem.
		MandarSkills(p, forcar: true);
		AplicarPoderes(p);
		AplicarEfeitos(p);
		Registrar(adm, $"{(dar ? "deu" : "tirou")} '{s.Nome}' {(dar ? "a" : "de")} {p.Name}");
		Avisar(adm, $"{s.Nome} {(dar ? "dada a" : "tirada de")} {p.Name}.");
	}

	/// <summary>
	/// OUTORGA UM CARGO ao alvo marcado, pulando os requisitos -- o `Give (Rank)` do original.
	///
	/// Hoje cargo so se consegue REIVINDICANDO, e a reivindicacao confere requisito. Sem esta
	/// porta, um cargo que ficou preso (o dono sumiu, o requisito mudou) nao tem conserto dentro
	/// do jogo. O trono continua UNICO: outorgar tira de quem estava.
	/// </summary>
	private void AdminOutorgarCargo(ServerPlayer adm, string arg)
	{
		string[] partes = arg.Split('|', 2);
		ServerPlayer? p = PorNome(partes[0]);
		string chave = (partes.Length > 1 ? partes[1] : "").Trim();
		if (p == null) { Avisar(adm, "marque alguem antes (duplo clique nele)."); return; }
		// CORPO SEM DONO NAO OCUPA TRONO. O clone da mente tem `Conta` vazia -- outorgar a ele
		// destronava o dono legitimo e gravava o trono no nome de ninguem.
		if (!EhPessoa(adm, p)) return;
		if (chave.Length == 0) { Avisar(adm, "escreva a chave do cargo (veja em Ranks)."); return; }

		Jandirus.Core.Ranks.RankDef? r = Jandirus.Core.Ranks.Cargos.Get(chave)
			?? Array.Find(Jandirus.Core.Ranks.Cargos.Todos,
				x => string.Equals(x.Nome, chave, StringComparison.OrdinalIgnoreCase));
		if (r == null) { Avisar(adm, $"nao existe cargo '{chave}'."); return; }

		Outorgar(r, p);
		Registrar(adm, $"outorgou o cargo '{r.Nome}' a {p.Name} ({p.Conta})");
		Avisar(adm, $"{p.Name} agora ocupa {r.Nome}.");
		Avisar(p, $"por decisao de um administrador, voce assume: {r.Nome}.");
	}

	/// <summary>
	/// Mensagem particular do admin pro alvo -- o `cmd_admin_pm` do original.
	///
	/// A COPIA VAI PROS OUTROS ADMINS, como no original: PM de staff que ninguem mais ve e como o
	/// admin esconde do admin.
	/// </summary>
	private void AdminPm(ServerPlayer adm, string arg)
	{
		string[] partes = arg.Split('|', 2);
		ServerPlayer? p = PorNome(partes[0]);
		string texto = (partes.Length > 1 ? partes[1] : "").Trim();
		if (p == null) { Avisar(adm, "marque alguem antes (duplo clique nele)."); return; }
		if (!EhPessoa(adm, p)) return;   // clone nao le mensagem
		if (texto.Length == 0) { Avisar(adm, "escreva a mensagem antes."); return; }

		Avisar(p, $"[admin] {adm.Name}: {texto}");
		Avisar(adm, $"[pra {p.Name}] {texto}");
		foreach (ServerPlayer o in Gente.ToList())
			if (o != adm && o != p && EhAdmin(o)) Avisar(o, $"[admin] {adm.Name} -> {p.Name}: {texto}");
	}

	private void AdminCurarTodos(ServerPlayer adm)
	{
		int n = 0;
		foreach (ServerPlayer p in Gente.ToList()) { Restaurar(p); n++; }
		Avisar(adm, $"{n} corpo(s) restaurados.");
		Anunciar("uma luz quente passa pelo mundo e fecha todas as feridas.");
	}

	/// <summary>O `KO_All_in_View`: so quem esta na MINHA zona, e nunca eu.</summary>
	private void AdminNocautearTodos(ServerPlayer adm)
	{
		int n = 0;
		foreach (ServerPlayer p in ZoneList(adm.Zone.Hash).ToList())
		{
			if (p == adm || p.Ficha.dead) continue;
			Nocautear(p);
			n++;
		}
		Avisar(adm, n == 0 ? "nao ha mais ninguem de pe por aqui." : $"{n} caiu(ram).");
	}

	private void AdminTrazerTodos(ServerPlayer adm)
	{
		int n = 0;
		foreach (ServerPlayer p in Gente.ToList())
		{
			if (p == adm) continue;
			MoveToZone(p.Id, adm.Zone, adm.Pos);
			Avisar(p, "uma forca invisivel te puxa.");
			n++;
		}
		Avisar(adm, $"{n} jogador(es) trazidos.");
	}

	/// <summary>
	/// DESFAZ A DESTRUICAO da minha zona -- o `Restaurar_Planeta` do original.
	///
	/// O buraco no chao e guardado num conjunto por zona (`_cenarioCaido`) e mandado ao cliente
	/// como uma lista de celulas caidas; nao ha "descaida" no protocolo, entao o conserto e
	/// esvaziar o conjunto e mandar todo mundo da zona recarregar o mapa. Recarregar e caro, mas
	/// isto e um verb de admin: acontece uma vez, quando alguem quis o cenario de volta.
	///
	/// A COLISAO VOLTA SOZINHA porque a queda nunca escreveu no dado do disco: `Abrir` poe a
	/// celula numa camada por cima, e `Fechar` tira dela (ver `ZoneCollision._abertas`). Foi essa
	/// separacao, feita pras portas, que deixou este verb ser oito linhas em vez de um recarregador
	/// de mapa.
	/// </summary>
	private void AdminConsertarCenario(ServerPlayer adm)
	{
		if (!_cenarioCaido.TryGetValue(adm.Zone.Name, out HashSet<(int X, int Y)>? caidas) || caidas.Count == 0)
		{
			Avisar(adm, "esta zona nao tem cenario quebrado.");
			return;
		}

		int n = caidas.Count;
		// So a colisao: o mapa do que CEGA e do cliente (o servidor nem o carrega), e e o proprio
		// cliente que o refecha ao receber a limpeza.
		if (_catalogo?.Get(adm.Zone)?.Mapa is { } mapa)
			foreach ((int x, int y) in caidas) mapa.Fechar(x, y);
		caidas.Clear();

		MandarLimpezaDeCenario(adm.Zone);

		// AS PORTAS TEM QUE SER REENVIADAS. O cliente NAO guarda o estado delas -- "o estado de
		// porta e do MAPA", diz o comentario do `PortasMudaram` -- e refazer o cenario joga a cena
		// fora e instancia outra do disco, onde toda porta nasce fechada. Sem esta volta, uma porta
		// que estava aberta apareceria fechada pra quem estava na zona, e so voltaria ao normal
		// quando alguem encostasse nela. E a mesma razao pela qual `MoveToZone` ja manda a lista.
		foreach (ServerPlayer p in ZoneList(adm.Zone.Hash))
		{
			MandarPortas(p);
			Avisar(p, "o chao se refaz sob os seus pes.");
		}
		Avisar(adm, $"{n} celula(s) de cenario restauradas.");
	}

	private void AdminSalvarTudo(ServerPlayer adm)
	{
		int n = 0;
		foreach (ServerPlayer p in Gente.ToList()) { Persistir(p); n++; }
		GravarMundo();
		SalvarCargos();
		Avisar(adm, $"{n} personagem(ns), o mundo e os cargos foram gravados.");
		Registrar(adm, "forcou o save de tudo");
	}

	/// <summary>
	/// O `Announce` do original: uma linha do servidor pra TODO MUNDO, sem canal de personagem.
	/// Vai no canal de sistema de proposito -- e o mundo falando, nao um jogador.
	/// </summary>
	private void AdminAnunciar(ServerPlayer adm, string texto)
	{
		texto = texto.Trim();
		if (texto.Length == 0) { Avisar(adm, "escreva o aviso antes."); return; }
		Anunciar($"[aviso] {texto}");
		Registrar(adm, $"anunciou: {texto}");
	}

	private void Anunciar(string texto)
	{
		foreach (ServerPlayer p in Gente.ToList()) Avisar(p, texto);
	}

	// =====================================================================
	// INFORMACAO -- os verbs que existem pra ENXERGAR
	// =====================================================================
	/// <summary>
	/// A FICHA COMPLETA DO ALVO, sem sigilo. E o `Assess` do original, e e o verb de admin que
	/// mais importa: sem ele o administrador enxerga "???" no BP como qualquer um, e nao tem como
	/// julgar uma denuncia de trapaca.
	///
	/// Este e o UNICO lugar do servidor que imprime o BP alheio sem passar pelo scouter. Ver
	/// `GameServer.Sigilo.cs` -- a regra continua valendo pro resto do jogo.
	/// </summary>
	private void AdminFicha(ServerPlayer adm, string alvo)
	{
		ServerPlayer? p = PorNome(alvo) ?? adm;

		Avisar(adm, $"-- {p.Name} --");
		Avisar(adm, $"  conta '{p.Conta}' slot {p.Slot + 1} · {p.Race}/{p.Ficha.Class} · {p.Genero} · {p.Idade} anos"
				  + (p.Linhagem.Length > 0 ? $" · {p.Linhagem}" : ""));
		Avisar(adm, $"  BP {p.Ficha.BP:N0} (expresso {p.Ficha.expressedBP:N0})");
		Avisar(adm, $"  vida {p.Ficha.HP:0}% · Ki {p.Ficha.Ki:N0}/{p.Ficha.MaxKi:N0}"
				  + (p.Ficha.dead ? " · MORTO" : p.Ficha.KO ? " · NOCAUTEADO" : ""));
		// OS NOMES PELO FUNIL, e a lista de maestrias e o lugar onde isso mais se ve: uma linha que
		// diz "Super Saiyajin 100%" ao lado de uma aba que chama a mesma forma de "Grade 4" e a
		// ferramenta do admin discordando do jogo. Ver `Catalogo.NomeDe(FormaDef, Maestrias)`.
		Avisar(adm, $"  forma {(p.Forma.Def is { } fd ? Catalogo.NomeDe(fd, p.Forma.Maestria) : p.Forma.Atual)}"
				  + (p.Forma.Maestria.Todas.Any()
					 ? " · maestrias " + string.Join(", ", p.Forma.Maestria.Todas
						 .Where(m => m.V > 0)
						 .Select(m => $"{(Catalogo.Def(m.Id) is { } md ? Catalogo.NomeDe(md, p.Forma.Maestria) : m.Id)} {m.V:0}%"))
					 : ""));
		Avisar(adm, $"  skills {p.Livro.Aprendidas.Count} · marcos {p.Livro.MarcosLivres}/{p.Livro.MarcosTotais}");
		Avisar(adm, $"  tech {p.Ficha.techskill:0.#} · zeni {p.Ficha.Zeni:N0}");
		Avisar(adm, $"  em {p.Zone.Name} ({p.Pos.X / ZoneCollision.TileSize:0},{p.Pos.Y / ZoneCollision.TileSize:0})"
				  + (EstaOculto(p.Id) ? " · INVISIVEL" : "")
				  + (EstaCalado(p) ? " · CALADO" : ""));

		var partido = p.Combate.Corpo.Partes.Where(m => m.Decepado || m.Vida < m.VidaMax).ToList();
		if (partido.Count > 0)
			Avisar(adm, "  corpo: " + string.Join(", ", partido.Select(
				m => $"{m.Nome} {(m.Decepado ? "DECEPADO" : $"{m.Vida / m.VidaMax * 100:0}%")}")));
	}

	/// <summary>O `AssessAll` + `BP_Lists`: quem esta no mundo, do mais forte pro mais fraco.</summary>
	private void AdminFichaDeTodos(ServerPlayer adm)
	{
		Avisar(adm, $"-- {Gente.Count()} no mundo, por poder --");
		foreach (ServerPlayer p in Gente.OrderByDescending(p => p.Ficha.expressedBP))
			Avisar(adm, $"  {p.Ficha.expressedBP,16:N0}  {p.Name} ({p.Race}) em {p.Zone.Name}"
					  + (p.Ficha.dead ? " [morto]" : p.Ficha.KO ? " [KO]" : ""));
	}

	/// <summary>O `Races()`: quantos de cada raca estao jogando agora.</summary>
	private void AdminRacas(ServerPlayer adm)
	{
		if (!Gente.Any()) { Avisar(adm, "nao ha ninguem no mundo."); return; }
		Avisar(adm, "-- racas no mundo --");
		foreach (IGrouping<string, ServerPlayer> g in Gente
			.GroupBy(p => p.Race).OrderByDescending(g => g.Count()))
			Avisar(adm, $"  {g.Key}: {g.Count()}");
	}

	/// <summary>
	/// O `AllIPs()`: de onde cada um esta conectado. E o verb com que se descobre que duas contas
	/// que trocam itens entre si sao a mesma pessoa.
	/// </summary>
	private void AdminIps(ServerPlayer adm)
	{
		Avisar(adm, "-- enderecos --");
		foreach (ServerPlayer p in Gente.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
			Avisar(adm, $"  {p.Name} ({p.Conta}): {p.Peer?.Address.ToString() ?? "?"}"
					  + (EhHost(p.Peer) ? "  [host]" : ""));
	}

	/// <summary>
	/// O `Galaxy_Status` do original, com o que ESTE port tem: a seed do universo, onde cada um
	/// esta e quais planetas existem por perto de mim.
	/// </summary>
	private void AdminGalaxia(ServerPlayer adm)
	{
		Avisar(adm, $"-- galaxia (seed {SeedDoUniverso}) --");
		Avisar(adm, $"  zonas com gente: {Gente.Select(p => p.Zone.Name).Distinct().Count()}");
		Avisar(adm, $"  construcoes de pe: {_noChao.Count}");

		if (!Espaco.EhEspaco(adm.Zone))
		{
			Avisar(adm, "  (entre no espaco pra ver os planetas por perto)");
			return;
		}
		ChunkId c = ChunkId.De(adm.Pos);
		Avisar(adm, $"  voce esta na chunk {c}");
		foreach (PlanetaNoEspaco pn in Espaco.PorPerto(SeedDoUniverso, c))
			Avisar(adm, $"  {pn.Nome}{(pn.Premade ? " (pre-feito)" : "")} raio {pn.Raio:0} seed {pn.Seed}");
	}

	/// <summary>
	/// O `Invisible()` do original. Usa a MESMA invisibilidade da tecnica -- o conjunto
	/// `_invisiveis` que o snapshot ja consulta --, entao nao ha um segundo caminho de "sumir"
	/// pra manter em sincronia.
	/// </summary>
	private void AdminInvisivel(ServerPlayer adm)
	{
		if (_invisiveis.Remove(adm.Id))
		{
			adm.Ficha.isconcealed = false;
			MandarEfeito(adm, "invisivel", 0);
			Avisar(adm, "voce volta a ser visto.");
			return;
		}

		// AS TRES COISAS, e nao so o conjunto.
		//
		// A tecnica de invisibilidade faz tres: entra em `_invisiveis`, escreve `isconcealed` e
		// manda o efeito pro cliente. Este verb fazia so a primeira -- e o resultado era pior que
		// incompleto: o tick das tecnicas varre `_invisiveis` e COBRA Ki de quem esta la, entao o
		// admin sangrava energia sem ter usado tecnica nenhuma e era chutado do conjunto assim que
		// o Ki baixava. A invisibilidade de admin caia sozinha, com uma mensagem de tecnica.
		//
		// (O dreno continua: e o mesmo conjunto e a mesma regra. Um poder de admin sem custo pediria
		// um conjunto proprio que o tick nao varresse -- e isso e outra mecanica, nao este verb.)
		_invisiveis.Add(adm.Id);
		adm.Ficha.isconcealed = true;
		MandarEfeito(adm, "invisivel", -1);
		Avisar(adm, "ninguem mais te ve. (a invisibilidade consome Ki, como a tecnica)");
	}
}
