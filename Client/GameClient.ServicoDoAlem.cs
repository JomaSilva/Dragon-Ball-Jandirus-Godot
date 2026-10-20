using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) O CARTAO DOS SERVICOS DO OUTRO MUNDO -- a metade do cliente do `S2C.ServicoDoAlem`. Isto e tudo o que o cliente sabe dos
/// servicos: quem decide e o servidor (`GameServer.ServicosDoAlem.cs`); quem desenha e a aba World (`MenuJogo.ServicoDoAlem.cs`).
/// </summary>
public partial class GameClient
{
	/// <summary>O ultimo cartao que chegou. Sem pacote nenhum ainda, nao mostra nada.</summary>
	public ServicoDoAlemState CartaoDoAlem { get; private set; } = new(false, "", "", 0, 0, []);

	/// <summary>Chegou um cartao novo (o servidor so manda quando ele muda).</summary>
	public event Action? CartaoDoAlemMudou;

	private void LerServicoDoAlem(NetDataReader reader)
	{
		CartaoDoAlem = FioDoServicoDoAlem.Ler(reader);
		CartaoDoAlemMudou?.Invoke();
	}
}
