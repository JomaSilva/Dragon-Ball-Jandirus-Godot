using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-31, o pedido 198i) O PLANETA DA MISSAO DA PATRULHA na carta estelar -- o botao "missão da Patrulha" da barra da carta (so pra
/// quem e da Patrulha, `MenuJogo.Nav.cs`) leva a carta ate ele. O endereco vem do servidor (`S2C.MissaoDaPatrulha`), como o da casa do
/// Bills, e a conferencia e pela semente: um endereco velho que caisse noutro mundo nao seleciona nada.
/// </summary>
public partial class MenuJogo
{
	internal static PlanetaNoEspaco? MundoDaMissaoDaPatrulha()
	{
		if (GameClient.Instance is not { SeedDoUniverso: not 0UL, MissaoDaPatrulha: { Membro: true, TemMissao: true, NaCarta: true } m } cli) return null;
		if (Sistemas.Do(cli.SeedDoUniverso, m.Sx, m.Sy) is not { } sistema || m.Orbita >= sistema.Orbitas) return null;
		PlanetaNoEspaco p = sistema.Planeta(m.Orbita);
		return p.Seed == m.Semente ? p : null;
	}
}
