using Godot;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// OS COMANDOS DO TERMINAL DO SERVIDOR DEDICADO -- o que o dono digita no console da maquina (a VPS, ou a janela do
/// `servidor.bat`). Quem le o teclado e o <see cref="TerminalDoServidor"/>; quem executa e isto, na thread principal.
///
/// ============================ POR QUE EXISTE ============================
/// Num servidor dedicado ninguem clicou em "Hospedar", entao ninguem entra admin sozinho (`GameServer.Anfitriao.cs`).
/// A unica porta era o `--admin &lt;conta&gt;`, e ela so marca conta que JA EXISTE quando o servidor sobe: pra promover
/// alguem, o dono esperava a pessoa criar a conta, derrubava o servidor e o ligava de novo com a flag -- numa VPS,
/// derrubar todo mundo pra promover um. Aqui a promocao acontece com o servidor no ar e vale na hora pra quem estiver
/// online.
///
/// ============================ AS MESMAS PORTAS DO PAINEL ============================
/// Todo comando que muda uma conta passa pelo MESMO funil do verb do painel de admin (`MarcarAdmin`,
/// `MarcarBanimento`, `MarcarMudez`, `ExpulsarGravando`, `GravarTudoAgora`, em `GameServer.Admin.cs`): o disco, as
/// copias vivas da conta e quem esta online mudam juntos, que e onde o painel ja errou mais de uma vez. Aqui so mora
/// o que e do terminal: ler a linha, achar a conta sem um jogador pra avisar, e responder no console. O rastro vai pro
/// mesmo `admin.log`, assinado por <see cref="AssinaturaDoTerminal"/>.
///
/// O TERMINAL E O DONO DA MAQUINA, e por isso a recusa "voce e o unico administrador" do painel nao vale aqui: ela
/// existe porque um servidor sem admin ficava sem ninguem pra promover, e o terminal sempre promove. Banir
/// administrador continua pedindo rebaixar antes, como no painel: sao dois gestos de proposito, e o terminal nao abre
/// o atalho que o painel fechou.
/// =====================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Quem assina o `admin.log` quando a ordem veio do terminal.</summary>
	private const string AssinaturaDoTerminal = "o terminal do servidor";

	/// <summary>
	/// Quanto o `parar` espera entre avisar quem esta jogando e derrubar. Sem a espera o aviso e a desconexao saem no
	/// mesmo quadro, e o aviso nao chega a ninguem.
	/// </summary>
	private const double SegundosDoAvisoDeParada = 1.5;

	private bool _terminalPediuParada;

	/// <summary>
	/// UMA LINHA DO TERMINAL: o primeiro pedaco e o comando (sem caixa), o resto e o argumento. Chamado pelo
	/// <see cref="TerminalDoServidor"/>, na thread principal.
	/// </summary>
	/// <param name="arvore">A arvore que o `parar` fecha.</param>
	internal void ExecutarDoTerminal(string linha, SceneTree arvore)
	{
		linha = linha.Trim();
		if (linha.Length == 0) return;

		int corte = linha.IndexOfAny([' ', '\t']);
		string cmd = (corte < 0 ? linha : linha[..corte]).ToLowerInvariant();
		string arg = corte < 0 ? "" : linha[(corte + 1)..].Trim();

		switch (cmd)
		{
			case "ajuda" or "help" or "comandos" or "?": TerminalAjuda(); break;
			case "status": TerminalStatus(); break;
			case "online" or "quem" or "list": TerminalOnline(); break;
			case "contas": TerminalContas(); break;
			case "admins": TerminalAdmins(); break;
			case "admin" or "promover" or "op": TerminalAdmin(arg, virar: true); break;
			case "desadmin" or "rebaixar" or "deop": TerminalAdmin(arg, virar: false); break;
			case "banir" or "ban": TerminalBanir(arg, banir: true); break;
			case "perdoar" or "desbanir" or "unban": TerminalBanir(arg, banir: false); break;
			case "expulsar" or "kick": TerminalExpulsar(arg); break;
			case "calar" or "mute": TerminalCalar(arg, calar: true); break;
			case "descalar" or "unmute": TerminalCalar(arg, calar: false); break;
			case "senha": TerminalSenha(arg); break;
			case "anunciar" or "aviso" or "say": TerminalAnunciar(arg); break;
			case "salvar" or "save": TerminalSalvar(); break;
			case "parar" or "desligar" or "stop": TerminalParar(arvore); break;
			default: Dizer($"comando desconhecido: '{cmd}' -- digite `ajuda` pra ver a lista."); break;
		}
	}

	private static void Dizer(string texto) => GD.Print("[terminal] " + texto);

	private static void TerminalAjuda()
	{
		Dizer("-- comandos (onde diz <conta>, vale o nome da CONTA ou de um personagem dela) --");
		Dizer("  admin <conta>              da administrador -- na hora, com a pessoa online ou nao");
		Dizer("  desadmin <conta>           tira o administrador");
		Dizer("  admins                     quem e administrador");
		Dizer("  contas                     todas as contas deste servidor");
		Dizer("  online                     quem esta conectado agora");
		Dizer("  banir <conta> [| motivo]   bane, e derruba quem estiver usando a conta");
		Dizer("  perdoar <conta>            tira o banimento");
		Dizer("  expulsar <personagem>      derruba do servidor (o personagem e gravado antes)");
		Dizer("  calar <conta>              a conta nao fala em canal nenhum (descalar <conta> desfaz)");
		Dizer("  senha <conta> <nova>       troca a senha de quem esqueceu");
		Dizer("  anunciar <texto>           aviso pra todo mundo que esta jogando");
		Dizer("  salvar                     grava tudo agora");
		Dizer("  status                     versao, porta, gente online e onde ficam os saves");
		Dizer("  parar                      avisa quem esta jogando, grava tudo e desliga o servidor");
	}

	private void TerminalStatus()
	{
		var v = MinhaVersao;
		TimeSpan noAr = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
		Dizer($"versao {v.Texto} (protocolo {v.DigitalCurta}) | porta {_net.LocalPort} UDP | no ar ha {(int)noAr.TotalHours}h{noAr.Minutes:00}");
		Dizer($"{Jogadores.Count()} no mundo, {_logados.Count} na tela de selecao | {_store?.Quantas() ?? 0} conta(s)");
		Dizer($"saves em: {_store?.Pasta ?? "(nenhum -- este servidor nao guarda nada em disco)"}");
	}

	private void TerminalOnline()
	{
		List<ServerPlayer> gente = [.. Jogadores.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
		Dizer(gente.Count == 0 ? "ninguem no mundo agora." : $"-- {gente.Count} no mundo --");
		foreach (ServerPlayer p in gente)
			Dizer($"  #{p.Id}  {p.Name}  (conta {p.Conta})  {p.Race}  em {p.Zone.Name}  {p.Peer?.Address.ToString() ?? "?"}"
				  + (EhAdmin(p) ? "  [ADMIN]" : "") + (EstaCalado(p) ? "  [CALADO]" : ""));

		// QUEM AINDA NAO ESCOLHEU PERSONAGEM tambem aparece: e o caso de quem acabou de criar a conta pra ser promovido.
		List<string> naSelecao = [.. _logados.Values.Select(a => a.Conta).Order(StringComparer.OrdinalIgnoreCase)];
		if (naSelecao.Count > 0) Dizer($"  na tela de selecao: {string.Join(", ", naSelecao)}");
	}

	private void TerminalContas()
	{
		if (_store == null) { Dizer("este servidor nao guarda contas em disco."); return; }
		List<AccountSave> contas = _store.Todas();
		Dizer(contas.Count == 0 ? "nenhuma conta ainda -- a conta nasce no primeiro login de cada pessoa." : $"-- {contas.Count} conta(s) --");
		foreach (AccountSave a in contas)
		{
			string personagens = string.Join(", ", a.Slots.Where(s => s != null).Select(s => $"{s!.Nome} ({s.Raca})"));
			Dizer($"  {a.Conta}" + (a.Admin ? "  [ADMIN]" : "") + (a.Banida ? "  [BANIDA]" : "") + (a.Calada ? "  [CALADA]" : "")
				  + (ContaEmUso(a.Conta) ? "  [online]" : "") + (personagens.Length > 0 ? $"  -- {personagens}" : "  -- sem personagem"));
		}
	}

	private void TerminalAdmins()
	{
		if (_store == null) { Dizer("este servidor nao guarda contas em disco."); return; }
		List<string> marcadas = [.. _store.Todas().Where(a => a.Admin).Select(a => a.Conta + (ContaEmUso(a.Conta) ? " [online]" : ""))];
		Dizer(marcadas.Count == 0
			? "nenhuma conta e administradora ainda -- use `admin <conta>` (a pessoa precisa ter entrado uma vez no servidor)."
			: $"administradores ({marcadas.Count}): {string.Join(", ", marcadas)}");
	}

	/// <summary>Alguem esta usando esta conta agora, no mundo ou na tela de selecao?</summary>
	private bool ContaEmUso(string conta) =>
		_players.Values.Any(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase))
		|| _logados.Values.Any(a => string.Equals(a.Conta, conta, StringComparison.OrdinalIgnoreCase));

	private void TerminalAdmin(string quem, bool virar)
	{
		if (_store == null) { Dizer("este servidor nao guarda contas em disco."); return; }
		if (quem.Length == 0) { Dizer($"uso: {(virar ? "admin" : "desadmin")} <conta ou personagem>"); return; }

		AccountSave? a = AcharConta(quem, Dizer);
		if (a == null)
		{
			// A CONTA NASCE NO PRIMEIRO LOGIN, com a senha de quem chegou primeiro: promover um nome que ainda nao e de
			// ninguem entregaria o servidor a quem o registrasse antes. A mesma regra do `--admin`.
			if (virar) Dizer("a conta nasce no primeiro login: peca pra pessoa entrar uma vez no servidor e repita o comando.");
			return;
		}
		if (a.Admin == virar)
		{
			Dizer($"a conta '{a.Conta}' {(virar ? "ja e" : "nao e")} administradora.");
			return;
		}

		int online = MarcarAdmin(a, virar);
		RastroDaLinhaDeComando($"{(virar ? "promoveu" : "rebaixou")} a conta '{a.Conta}'", AssinaturaDoTerminal);
		Dizer($"conta '{a.Conta}' {(virar ? "agora e administradora" : "nao e mais administradora")}"
			  + (online > 0 ? $" -- ja valeu pra quem esta online ({online} personagem)." : " -- vale no proximo login dela."));
	}

	private void TerminalBanir(string arg, bool banir)
	{
		if (_store == null) { Dizer("este servidor nao guarda contas em disco."); return; }
		string[] partes = arg.Split('|', 2);
		string quem = partes[0].Trim();
		string motivo = partes.Length > 1 ? partes[1].Trim() : "";
		if (quem.Length == 0) { Dizer(banir ? "uso: banir <conta ou personagem> [| motivo]" : "uso: perdoar <conta ou personagem>"); return; }

		AccountSave? a = AcharConta(quem, Dizer);
		if (a == null) return;
		if (banir && (a.Admin || EstaOnlineComoAdmin(a.Conta)))
		{
			Dizer($"'{a.Conta}' e administradora -- tire antes com `desadmin {a.Conta}`.");
			return;
		}
		if (a.Banida == banir)
		{
			Dizer($"a conta '{a.Conta}' {(banir ? "ja esta" : "nao esta")} banida.");
			return;
		}

		MarcarBanimento(a, banir, motivo.Length > 0 ? $"banida pelo terminal: {motivo}" : "banida pelo terminal");
		RastroDaLinhaDeComando($"{(banir ? "baniu" : "perdoou")} a conta '{a.Conta}'" + (motivo.Length > 0 ? $" ({motivo})" : ""),
							   AssinaturaDoTerminal);
		Dizer($"conta '{a.Conta}' {(banir ? "banida" : "perdoada")}.");
	}

	private void TerminalExpulsar(string quem)
	{
		if (quem.Length == 0) { Dizer("uso: expulsar <personagem, conta ou #id> (veja `online`)"); return; }
		string busca = quem.TrimStart('#');
		ServerPlayer? p = PorNome(busca) is { } achado && EhJogador(achado)
			? achado
			: Jogadores.FirstOrDefault(j => string.Equals(j.Conta, busca, StringComparison.OrdinalIgnoreCase));
		if (p == null) { Dizer($"ninguem no mundo com o nome '{quem}' -- veja `online`."); return; }

		ExpulsarGravando(p);
		RastroDaLinhaDeComando($"expulsou {p.Name} ({p.Conta})", AssinaturaDoTerminal);
		Dizer($"{p.Name} foi desconectado (o personagem foi gravado antes).");
	}

	private void TerminalCalar(string quem, bool calar)
	{
		if (_store == null) { Dizer("este servidor nao guarda contas em disco."); return; }
		if (quem.Length == 0) { Dizer($"uso: {(calar ? "calar" : "descalar")} <conta ou personagem>"); return; }

		AccountSave? a = AcharConta(quem, Dizer);
		if (a == null) return;
		if (a.Calada == calar)
		{
			Dizer($"a conta '{a.Conta}' {(calar ? "ja esta" : "nao esta")} calada.");
			return;
		}

		MarcarMudez(a, calar);
		foreach (ServerPlayer p in Jogadores.Where(j => string.Equals(j.Conta, a.Conta, StringComparison.OrdinalIgnoreCase)))
			Avisar(p, calar ? "um administrador te calou." : "voce voltou a poder falar.");
		RastroDaLinhaDeComando($"{(calar ? "calou" : "descalou")} a conta '{a.Conta}'", AssinaturaDoTerminal);
		Dizer($"conta '{a.Conta}' {(calar ? "calada" : "pode falar de novo")}.");
	}

	private void TerminalSenha(string arg)
	{
		if (_store == null) { Dizer("este servidor nao guarda contas em disco."); return; }
		// A SENHA E A ULTIMA PALAVRA e o resto e a conta: nome de conta pode ter espaco, a senha digitada aqui nao.
		int corte = arg.LastIndexOfAny([' ', '\t']);
		if (corte < 0) { Dizer("uso: senha <conta> <senha nova>"); return; }
		string quem = arg[..corte].Trim(), nova = arg[(corte + 1)..];
		// AS BEIRADAS DO LOGIN: ele recusa menos de 3, e o pacote dele le no maximo 64. Uma senha fora disso trancaria a
		// conta pra sempre, porque nenhum cliente conseguiria manda-la.
		if (nova.Length < 3 || nova.Length > 64) { Dizer("a senha nova precisa ter de 3 a 64 caracteres."); return; }

		AccountSave? a = AcharConta(quem, Dizer);
		if (a == null) return;

		(string sal, string hash) = AccountStore.Cadastrar(nova);
		a.Sal = sal;
		a.Hash = hash;
		_store.Gravar(a);
		// AS COPIAS VIVAS TAMBEM: quem esta online grava a conta A PARTIR da copia dele no proximo `Persistir`, e a copia
		// velha devolveria a senha antiga ao disco -- o mesmo tombo do `EmTodaCopiaViva`.
		EmTodaCopiaViva(a.Conta, v => { v.Sal = sal; v.Hash = hash; });

		// A SENHA NAO VAI PRA LOG NENHUM, nem pro `admin.log` nem pro console: so o fato de ter sido trocada.
		RastroDaLinhaDeComando($"trocou a senha da conta '{a.Conta}'", AssinaturaDoTerminal);
		Dizer($"senha da conta '{a.Conta}' trocada -- vale no proximo login (quem ja esta dentro continua dentro).");
	}

	private void TerminalAnunciar(string texto)
	{
		if (texto.Length == 0) { Dizer("uso: anunciar <texto>"); return; }
		string linha = $"[aviso] {texto}";
		// O CLIENTE LE O CHAT COM TETO (`GetString(Protocol.MaxFala)`), e o teto devolve VAZIO, nao cortado: sem este
		// corte um aviso comprido chegaria como uma linha em branco na tela de todo mundo.
		if (linha.Length > Protocol.MaxFala) linha = linha[..Protocol.MaxFala];
		Anunciar(linha);
		RastroDaLinhaDeComando($"anunciou: {texto}", AssinaturaDoTerminal);
		Dizer($"aviso entregue a {Jogadores.Count()} jogador(es).");
	}

	private void TerminalSalvar()
	{
		int n = GravarTudoAgora();
		RastroDaLinhaDeComando("forcou o save de tudo", AssinaturaDoTerminal);
		Dizer($"{n} personagem(ns), o mundo e os cargos foram gravados.");
	}

	private void TerminalParar(SceneTree arvore)
	{
		if (_terminalPediuParada) { Dizer("ja estou desligando..."); return; }
		_terminalPediuParada = true;

		RastroDaLinhaDeComando("desligou o servidor", AssinaturaDoTerminal);
		Anunciar("[aviso] o servidor vai desligar agora -- o seu personagem e gravado na saida.");
		Dizer($"avisei quem esta jogando; gravando tudo e desligando em {SegundosDoAvisoDeParada:0.#} s...");

		// A MESMA PORTA DO X DA JANELA E DO Ctrl+C (`Saida.Encerrar`): o servidor dedicado de producao grava e desliga,
		// e a arvore fecha. Um segundo caminho de saida aqui seria o primeiro a esquecer de gravar alguma coisa.
		arvore.CreateTimer(SegundosDoAvisoDeParada).Timeout += () => Jandirus.Client.Saida.Encerrar(arvore, "o terminal pediu: parar");
	}
}
