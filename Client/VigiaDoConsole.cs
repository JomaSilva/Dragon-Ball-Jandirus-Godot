using System.Runtime.InteropServices;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// O CONSOLE DO SERVIDOR DEDICADO E UMA PORTA DE SAIDA TAMBEM.
///
/// O `servidor.bat` sobe o jogo SEM JANELA e diz *"Feche esta janela ou Ctrl+C para derrubar"*. Nenhum dos
/// dois gestos passava por lugar nenhum: o X da janela do jogo vira `NotificationWMCloseRequest` (e o `Boot`
/// o manda pro <see cref="Saida"/>), mas um processo sem janela nao recebe isso -- ele so morria, levando o
/// que os conectados tinham feito desde o ultimo save periodico (ate dois minutos).
///
/// ============================ O QUE ISTO FAZ ============================
/// Pede ao .NET pra avisar dos sinais do console (`PosixSignalRegistration`, que no Windows sao os eventos de
/// controle: Ctrl+C, Ctrl+Break, o fechamento da janela do console e o desligamento da sessao) e, quando um
/// chega, manda o processo sair pela MESMA porta de todo mundo -- o `Saida.Encerrar`, que grava e desliga.
///
/// DUAS THREADS, e a divisao e a regra: o sinal chega numa thread do SISTEMA, e o servidor so pode ser mexido
/// na principal. Entao a thread do sinal so levanta um pedido e ESPERA; quem atende e o `_Process` deste node,
/// na principal. A espera tem de existir: no fechamento da janela do console o Windows mata o processo assim
/// que o tratador devolve (ou em uns 5 s, o que vier primeiro) -- devolver antes da gravacao seria nao gravar.
/// Se a principal nao atender no prazo (travada), o tratador desiste e deixa o sinal fazer o que sempre fez.
///
/// ============================ O QUE NAO DA PRA PEGAR ============================
/// Matar o processo pelo Gerenciador de Tarefas (ou `taskkill /F`), a queda de energia e o crash nao avisam
/// ninguem. Pra esses a rede de seguranca continua sendo a de sempre: o save periodico de dois minutos e a
/// gravacao de cada jogador na saida dele.
///
/// SO O SERVIDOR DEDICADO DE PRODUCAO ARMA ISTO (ver `Boot.DedicadoDeProducao`): uma bancada nao pode gravar
/// o mundo dela ao ser fechada, e quem joga numa janela ja tem o X.
/// =======================================================================
/// </summary>
public partial class VigiaDoConsole : Node
{
	/// <summary>
	/// Quanto a thread do sinal espera a principal gravar e desligar, em ms. Abaixo dos ~5 s que o Windows da a
	/// um processo depois de fechar a janela do console dele.
	/// </summary>
	public const int EsperaPelaGravacaoMs = 4500;

	private static readonly PosixSignal[] Sinais = [PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGHUP, PosixSignal.SIGTERM];

	private readonly List<PosixSignalRegistration> _registros = [];

	/// <summary>0 = ninguem pediu; 1 = pediram e a principal ainda nao atendeu; 2 = atendido.</summary>
	private static int _pedido;
	private static string _motivo = "";
	private static readonly ManualResetEventSlim Atendido = new(false);

	/// <summary>Quantos sinais este processo esta vigiando (zero numa bancada, que nao arma o vigia).</summary>
	public static int SinaisVigiados { get; private set; }

	public override void _Ready()
	{
		foreach (PosixSignal sinal in Sinais)
		{
			try { _registros.Add(PosixSignalRegistration.Create(sinal, AoSinal)); }
			catch (Exception e) { GD.PushWarning($"[saida] nao consegui vigiar o sinal {sinal} do console: {e.Message}"); }
		}
		SinaisVigiados = _registros.Count;
		GD.Print($"[saida] console vigiado ({SinaisVigiados} de {Sinais.Length} sinais): Ctrl+C e fechar a janela do console gravam antes de sair");
	}

	public override void _Process(double delta) => Atender(GetTree());

	public override void _ExitTree()
	{
		foreach (PosixSignalRegistration registro in _registros) registro.Dispose();
		_registros.Clear();
		SinaisVigiados = 0;
	}

	/// <summary>O sinal chegou (thread do SISTEMA): pede, espera a gravacao, e so cancela a morte padrao se ela aconteceu.</summary>
	private static void AoSinal(PosixSignalContext contexto) =>
		contexto.Cancel = Pedir($"o console pediu pra fechar ({contexto.Signal})", EsperaPelaGravacaoMs);

	/// <summary>
	/// O PEDIDO, de QUALQUER thread: levanta o pedido de fechamento e espera a thread principal atende-lo. Devolve
	/// se ela atendeu no prazo. Nao toca em nada do jogo -- so num inteiro e num sinalizador.
	/// </summary>
	public static bool Pedir(string motivo, int esperaMs)
	{
		_motivo = motivo;
		Interlocked.CompareExchange(ref _pedido, 1, 0);
		return Atendido.Wait(esperaMs);
	}

	/// <summary>
	/// A THREAD PRINCIPAL ATENDE: se ha um pedido, sai pela porta de sempre (<see cref="Saida.Encerrar"/>: grava
	/// o servidor local, despede o cliente, encerra a arvore) e solta quem esperava. Devolve se havia pedido.
	/// </summary>
	/// <param name="arvore">A arvore a encerrar. Nula so na bancada, que nao deixa o processo morrer.</param>
	public static bool Atender(SceneTree? arvore)
	{
		if (Interlocked.CompareExchange(ref _pedido, 2, 1) != 1) return false;
		try { Saida.Encerrar(arvore, _motivo); }
		finally { Atendido.Set(); }
		return true;
	}

	/// <summary>So pra bancada: desfaz o pedido atendido, entre rodadas.</summary>
	public static void RearmarDeTeste()
	{
		Interlocked.Exchange(ref _pedido, 0);
		Atendido.Reset();
	}
}
