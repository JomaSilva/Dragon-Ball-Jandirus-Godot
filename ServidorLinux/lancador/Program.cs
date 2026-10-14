using System.Diagnostics;
using System.Runtime.InteropServices;

// ===============================================================================================================
// O LANCADOR DO SERVIDOR DEDICADO -- o `server.dll` que paineis de hospedagem ".NET" sabem rodar.
//
// O servidor do jogo NAO e um app .NET solto: e o binario do Godot rodando sem tela (jandirus-servidor.x86_64), que
// carrega as DLLs do jogo e o PROPRIO runtime .NET da pasta data_*. Um painel que so sabe fazer `dotnet <arquivo>.dll`
// nao tem como liga-lo; este programa e a ponte: acha o binario, liga o servidor com os argumentos de producao e
// devolve o codigo de saida dele.
//
// A ENTRADA E A SAIDA SAO HERDADAS, e nao copiadas: o console do painel fala direto com o servidor, e os comandos do
// terminal (`admin fulano`, `parar`...) chegam sem uma thread no meio pra perder linha ou trocar acento. Por isso este
// programa nunca le o Console.In -- ele disputaria as linhas com o servidor.
//
// O PAINEL DESLIGANDO (SIGTERM, SIGINT...) e repassado ao servidor como SIGTERM, que ele trata gravando tudo antes de
// sair (`Client/VigiaDoConsole.cs`). O lancador cancela a propria morte e espera: sair primeiro deixaria o servidor
// orfao, segurando a porta, e a gravacao sem ninguem esperando por ela.
// ===============================================================================================================

const string NomeDoBinario = "jandirus-servidor.x86_64";
const int Sigterm = 15;

List<string> pedidos = [.. args];
string? binario = Tirar(pedidos, "--binario") ?? AcharOBinario(NomeDoBinario);
if (binario == null)
{
	Console.Error.WriteLine($"[lancador] nao achei o binario do servidor ({NomeDoBinario}, ou um unico *.x86_64 / *.arm64) "
						  + "na pasta do server.dll nem nas de cima dela.");
	return 1;
}

// UPLOAD PELO PAINEL OU POR SFTP COSTUMA PERDER O BIT DE EXECUCAO, e sem ele o Linux recusa o binario.
if (!OperatingSystem.IsWindows())
	File.SetUnixFileMode(binario, File.GetUnixFileMode(binario) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

string pasta = Path.GetDirectoryName(Path.GetFullPath(binario))!;
var linha = new List<string> { "--headless", "--server" };
// A PORTA: a do argumento, ou a que o painel poe em SERVER_PORT (o padrao dos paineis no estilo Pterodactyl), ou 7777.
if (!pedidos.Contains("--port"))
	linha.AddRange(["--port", Environment.GetEnvironmentVariable("SERVER_PORT") is { Length: > 0 } porta ? porta : "7777"]);
// 60 quadros: o ritmo de quem hospeda pelo jogo numa tela de 60 Hz -- ver debian/servidor.conf.exemplo.
if (!pedidos.Contains("--max-fps"))
	linha.AddRange(["--max-fps", "60"]);
linha.AddRange(pedidos);

var inicio = new ProcessStartInfo(binario) { UseShellExecute = false, WorkingDirectory = pasta };
foreach (string a in linha) inicio.ArgumentList.Add(a);
// OS SAVES FICAM NA PASTA DO SERVIDOR (dados/), e nao no HOME: num painel o HOME pode morar fora do disco que sobrevive
// a reinstalacao, e o mundo sumiria junto com o conteiner.
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_DATA_HOME")))
	inicio.Environment["XDG_DATA_HOME"] = Path.Combine(pasta, "dados");
// A CULTURA EM QUE O JOGO SEMPRE RODOU (o Windows do dono: 1.234,5) -- o iniciar.sh faz o mesmo.
inicio.Environment["LANG"] = "pt_BR.UTF-8";

Console.WriteLine($"[lancador] ligando {Path.GetFileName(binario)} {string.Join(' ', linha)}");
Process servidor;
try { servidor = Process.Start(inicio)!; }
catch (Exception e)
{
	Console.Error.WriteLine($"[lancador] nao consegui ligar {binario}: {e.Message}");
	return 1;
}

PosixSignalRegistration[] vigias = OperatingSystem.IsWindows()
	? []
	: [.. new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGQUIT, PosixSignal.SIGHUP }
		.Select(sinal => PosixSignalRegistration.Create(sinal, Repassar))];

servidor.WaitForExit();
foreach (PosixSignalRegistration vigia in vigias) vigia.Dispose();
Console.WriteLine($"[lancador] o servidor saiu (codigo {servidor.ExitCode})");
return servidor.ExitCode;

void Repassar(PosixSignalContext contexto)
{
	contexto.Cancel = true;
	if (!servidor.HasExited) kill(servidor.Id, Sigterm);
}

static string? Tirar(List<string> lista, string chave)
{
	int i = lista.IndexOf(chave);
	if (i < 0 || i + 1 >= lista.Count) return null;
	string valor = lista[i + 1];
	lista.RemoveRange(i, 2);
	return valor;
}

// A pasta do server.dll, a pasta atual, e as de cima de cada uma: o painel pode compilar o lancador numa subpasta
// (bin/Release/net8.0/...), longe do binario. Em cada pasta vale o nome do pacote (`exportar-servidor-linux`) ou, na
// falta dele, o UNICO executavel Linux que houver -- um export feito pelo editor sai com o nome que o preset deu.
static string? AcharOBinario(string nome)
{
	foreach (string comeco in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
		for (DirectoryInfo? d = new(comeco); d != null; d = d.Parent)
		{
			string candidato = Path.Combine(d.FullName, nome);
			if (File.Exists(candidato)) return candidato;
			string[] linux;
			try { linux = [.. d.EnumerateFiles("*.x86_64").Concat(d.EnumerateFiles("*.arm64")).Select(f => f.FullName)]; }
			catch (UnauthorizedAccessException) { continue; }   // pasta de cima sem leitura (conteiner): segue subindo
			if (linux.Length == 1) return linux[0];
		}
	return null;
}

[DllImport("libc", SetLastError = true)]
static extern int kill(int pid, int sig);
