using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A CASA DO BILLS NA CARTA ESTELAR -- o anel roxo, a linha da ficha e o botao que leva a carta ate la (dono,
/// 2026-10-11: derrotado na Terra, o Bills "fala as coordenadas galaticas" do planeta dele).
///
/// O ENDERECO E O UNICO DADO DE REDE: a carta enumera os mundos sozinha (funcao pura da semente do universo), e o
/// que o servidor conta e QUAL deles e a casa -- e so depois de o Bills a revelar (`S2C.CasaDeBills`). Quem nao
/// estava online na hora recebe o mesmo pacote ao entrar, e e isso que faz a carta de todo mundo marcar o planeta.
///
/// A regra de qual mundo e a casa esta em `Core/Npc/BillsEWhis.cs` (`Casas`); a carta nao a refaz -- ela so
/// reconhece o mundo que o servidor apontou.
/// </summary>
public partial class MenuJogo
{
	/// <summary>O roxo do Deus da Destruicao na interface: a marca da casa dele na carta estelar.</summary>
	internal static readonly Color RoxoDeBills = new("b47cff");

	/// <summary>Este corpo da carta e a casa do Bills? So depois de revelada; o mundo se reconhece pela semente.</summary>
	internal static bool CasaDeBills(PlanetaNoEspaco p) =>
		!p.Premade && GameClient.Instance is { CasaDeBills: { Revelada: true } casa } && casa.Semente == p.Seed;

	/// <summary>
	/// O MUNDO DA CASA, achado pelo ENDERECO que o servidor mandou (sistema + orbita) -- ou nulo antes da revelacao.
	/// A semente confere o endereco: se a celula nao der naquele mundo (semente de universo ainda nao chegou, ou
	/// de outro servidor), a resposta e "nao sei", e nao um mundo errado.
	/// </summary>
	internal static PlanetaNoEspaco? MundoDaCasaDeBills()
	{
		if (GameClient.Instance is not { SeedDoUniverso: not 0UL, CasaDeBills: { Revelada: true } casa } cli) return null;
		if (Sistemas.Do(cli.SeedDoUniverso, casa.Sx, casa.Sy) is not { } sistema) return null;
		if (casa.Orbita >= sistema.Orbitas) return null;

		PlanetaNoEspaco p = sistema.Planeta(casa.Orbita);
		return p.Seed == casa.Semente ? p : null;
	}
}
