using Godot;

namespace Jandirus.Client;

/// <summary>
/// ESTE PROCESSO E UM SERVIDOR DEDICADO DE PRODUCAO?
///
/// O dono (2026-10-11), sobre o servidor dedicado que nao gravava ao fechar: *"Sim clicar no x deveria salvar"*.
///
/// ============================ POR QUE EXISTE UM CRITERIO ============================
/// O fechamento do jogo so gravava o servidor local numa SESSAO DE JOGADOR (<see cref="SessaoDeJogador"/>: a
/// tela de login chegou a aparecer). Toda sessao sem jogador saia pelo `Stop` sozinho, que nao grava -- e isso
/// existe por causa das BANCADAS: elas mexem no mundo em memoria de proposito (corpos forjados, planetas
/// mortos, cargos de mentira), e um X apertado no meio de uma delas gravaria esse estado por cima do mundo do
/// dono. So que o servidor dedicado de verdade (`servidor.bat`) TAMBEM e uma sessao sem jogador: ele caia no
/// mesmo saco, e fechar a janela dele levava ate dois minutos de todo mundo que estava conectado.
///
/// ============================ O CRITERIO: SO ARGUMENTOS DE PRODUCAO ============================
/// Um processo e um servidor dedicado de PRODUCAO quando foi aberto com `--server` e NADA na linha de comando
/// foge da lista de producao: o que o `servidor.bat` passa (`--headless --path . --server --port N`), o
/// `--sem-admin-local`, e um punhado de opcoes do motor que nao mexem no mundo (onde a janela abre, o log).
///
/// Toda bancada deste projeto se liga por um argumento proprio (`--paradateste`, `--diagqueda`, `--bercovivo`,
/// `--mundoprova`...), e nenhum deles esta na lista -- entao qualquer argumento DESCONHECIDO faz do processo
/// uma corrida de bancada. A pergunta nao e "isto parece nome de bancada?" (seria adivinhar por prefixo, e os
/// nomes nao tem prefixo comum): e "isto e TUDO de producao?", que e uma lista curta e fechada.
///
/// O ERRO CAI PRO LADO SEGURO. Um argumento novo de producao que alguem esquecer de por aqui faz o servidor
/// NAO gravar ao fechar -- o que ele ja fazia antes, com o save periodico de dois minutos por baixo --, e
/// nunca faz uma bancada gravar. E nao e calado: a decisao e dita no log na hora em que o servidor sobe.
/// ===================================================================================
/// </summary>
public partial class Boot
{
	/// <summary>Os argumentos de producao que valem sozinhos: os do jogo e os do motor que nao mexem no mundo.</summary>
	private static readonly HashSet<string> ArgumentosDeProducao = new(StringComparer.Ordinal)
	{
		"--server", "--sem-admin-local",
		"--headless", "--verbose", "-v", "--quiet", "-q", "--windowed", "-w", "--minimized",
	};

	/// <summary>Os argumentos de producao que levam um valor logo depois (a porta, a pasta do projeto, onde a janela abre...).</summary>
	private static readonly HashSet<string> ArgumentosDeProducaoComValor = new(StringComparer.Ordinal)
	{
		"--port",
		"--path", "--position", "--resolution", "--audio-driver", "--display-driver", "--rendering-driver", "--log-file",
	};

	/// <summary>
	/// A decisao deste processo, tomada no ramo `--server` do `_Ready`. Quem le e o <see cref="Saida"/>: junto da
	/// <see cref="SessaoDeJogador"/>, e o que manda o fechamento GRAVAR o servidor local.
	/// </summary>
	public static bool DedicadoDeProducao { get; private set; }

	/// <summary>O CRITERIO, puro: esta linha de comando e a de um servidor dedicado de producao? `porque` diz o que decidiu.</summary>
	public static bool EhDedicadoDeProducao(IReadOnlyList<string> args, out string porque)
	{
		if (!args.Contains("--server"))
		{
			porque = "nao e um servidor dedicado (falta o --server)";
			return false;
		}
		for (int i = 0; i < args.Count; i++)
		{
			if (ArgumentosDeProducao.Contains(args[i])) continue;
			if (ArgumentosDeProducaoComValor.Contains(args[i])) { i++; continue; }
			porque = $"o argumento '{args[i]}' nao e de producao";
			return false;
		}
		porque = "so argumentos de producao";
		return true;
	}

	/// <summary>
	/// DECIDE por esta linha de comando, guarda e DIZ no log. O `_Ready` chama com a do processo; a bancada chama
	/// com a do `servidor.bat` pra exercitar o caminho de producao, e devolve a de verdade no fim.
	/// </summary>
	public static bool DecidirSeEDedicadoDeProducao(IReadOnlyList<string> args)
	{
		DedicadoDeProducao = EhDedicadoDeProducao(args, out string porque);
		GD.Print(DedicadoDeProducao
			? "[server] servidor dedicado de PRODUCAO: fechar a janela (ou Ctrl+C no console) GRAVA tudo antes de sair"
			: $"[server] sessao sem jogador que NAO e de producao ({porque}): fechar nao grava o mundo -- e o caminho das bancadas");
		return DedicadoDeProducao;
	}
}
