using System.Collections.Concurrent;
using Godot;

namespace Jandirus.Server;

/// <summary>
/// O TERMINAL DO SERVIDOR DEDICADO: cada linha digitada no console vira um comando de administracao (`admin fulano`,
/// `banir`, `salvar`, `parar`...). Os comandos moram em `GameServer.Terminal.cs`; isto aqui e so a ponte entre o
/// teclado e o servidor.
///
/// O dono (2026-10-14): *"faca com q tenha comandos no terminal do server pra eu poder dar adm pra quem eu quiser etc."*
///
/// ============================ DUAS THREADS, a regra do `VigiaDoConsole` ============================
/// Ler a entrada padrao BLOQUEIA ate o Enter, entao a leitura mora numa thread propria, que so enfileira a linha.
/// Quem executa e o `_Process` deste node, na thread principal: o servidor so pode ser mexido nela, e um comando que
/// mexesse em `_players` do meio de outra thread brigaria com o tique.
///
/// ============================ ENTRADA FECHADA ============================
/// Sem terminal (um servico do systemd sem tmux, um `nohup ... &lt; /dev/null`) a leitura devolve nulo na primeira
/// chamada: a thread termina e o log diz UMA vez que os comandos nao vao chegar -- em vez de o dono digitar num
/// console que ninguem le. Os scripts de `ServidorLinux/` rodam o servidor dentro do tmux justamente pra ele ter um.
///
/// SO O SERVIDOR DEDICADO DE PRODUCAO ARMA ISTO (ver `Boot.DedicadoDeProducao`), pelo mesmo motivo do vigia: uma
/// bancada nao le teclado, e quem hospeda pelo botao ja tem o painel de admin.
/// ===================================================================================================
/// </summary>
public partial class TerminalDoServidor : Node
{
	private readonly ConcurrentQueue<string> _linhas = new();
	private volatile bool _entradaFechou;
	private volatile string _porqueFechou = "";
	private bool _avisouQueFechou;

	public override void _Ready()
	{
		// `IsBackground`: ela passa a vida parada no `ReadLine`, e nao pode segurar o processo vivo depois do `parar`.
		new Thread(LerAEntrada) { IsBackground = true, Name = "terminal do servidor" }.Start();
		GD.Print("[terminal] digite `ajuda` neste console pra ver os comandos do servidor (admin, banir, salvar, parar...)");
	}

	/// <summary>A thread da ENTRADA: le e enfileira, e mais nada. Nao toca em nada do jogo -- nem no log do Godot.</summary>
	private void LerAEntrada()
	{
		try
		{
			while (Console.In.ReadLine() is { } linha) _linhas.Enqueue(linha);
		}
		catch (Exception e) { _porqueFechou = $" ({e.GetType().Name}: {e.Message})"; }
		_entradaFechou = true;
	}

	public override void _Process(double delta)
	{
		while (_linhas.TryDequeue(out string? linha))
		{
			// UM COMANDO QUEBRADO NAO DERRUBA O SERVIDOR: quem esta jogando nao tem nada com a linha que o dono digitou.
			// A excecao inteira vai pro log, que e onde ela se conserta.
			try { GameServer.Instance?.ExecutarDoTerminal(linha, GetTree()); }
			catch (Exception e) { GD.PushError($"[terminal] o comando '{linha}' quebrou: {e}"); }
		}

		if (_entradaFechou && !_avisouQueFechou)
		{
			_avisouQueFechou = true;
			GD.Print($"[terminal] este processo nao tem terminal: a entrada padrao fechou{_porqueFechou}, e nada que se digitar chega ao servidor. "
				   + "Na VPS, ligue pelo `iniciar.sh` (o servidor roda dentro do tmux) e abra o console com `console.sh`.");
		}
	}
}
