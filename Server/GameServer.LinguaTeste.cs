using Jandirus.Core.Combat;
using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// AS DUAS PORTAS DO SERVIDOR QUE A BANCADA DA LINGUA (`--diaglingua`, `Client/RoboDaLingua.cs`) APERTA -- o robo e o
/// cliente de um processo que hospeda, e daqui ele faz o servidor falar pelos caminhos de PRODUCAO: o grito da furia
/// (`GritarNaErupcao`, a frase automatica que o dono citou: *"frases automatizadas por raiva ou transformaçao"*) e um
/// recado comum (`Avisar`). A fala DIGITADA o robo manda pelo `C2S.Chat` de verdade, como o teclado.
/// </summary>
public partial class GameServer
{
	/// <summary>O grito da FURIA (o amigo morto) pela boca do corpo <paramref name="id"/>: o nome com o "NAO" agora, a frase depois.</summary>
	internal void GritoDaFuriaDeTeste(int id, string nomeDoAmigo)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) GritarNaErupcao(pl, nomeDoAmigo, NivelDeRaiva.Extrema);
	}

	/// <summary>Um recado do servidor pro corpo <paramref name="id"/>, pelo funil de todo aviso.</summary>
	internal void AvisoDeTeste(int id, string texto)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) Avisar(pl, texto);
	}
}
