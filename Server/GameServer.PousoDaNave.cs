using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ O POUSO DA NAVE: UM LUGAR SORTEADO, E O POD ABRE CRATERA (2026-10-18) ============================
/// O dono: *"faça a pod e a nave quando chegarem no planeta pousarem em algum local aleatorio com terra firme, e a pod quando pousa faz
/// uma cratera grande"*.
///
/// O LUGAR: quem desce do espaco PILOTANDO (o pod, a Capital Ship ao leme) e a Capital Ship que segue sozinha pousam num ponto sorteado do
/// mapa inteiro, em terra firme a ceu aberto -- a regra e a do Core (`PousoSorteado`). As tres portas de chegada perguntam aqui: o pouso
/// no pre-feito (`TickDoEspaco`), no gerado (`PousarEmProcedural`) e o da nave sozinha (`PousarNaveSozinha`). Sem lugar que sirva, cada
/// uma pousa no ponto de sempre (o berco, a chegada do gerador). Quem desce VOANDO sozinho continua chegando no berco: o pedido e da nave.
///
/// A CRATERA: o pod que desce do espaco abre a dele no ponto do pouso (`Protocol.Decal.CrateraDoPod`), com a poeira no meio e na borda. E
/// DESENHO, e nao estrago: nao derruba cenario e nao fere ninguem. O estrondo e o tremor sao do cliente, ao plantar a cratera
/// (`World.EstrondoDoPod`) -- e e por isso que o decalque que chega junto de uma troca de zona espera o mapa novo montar
/// (`World.Decalques`): sem isso o piloto, o unico que com certeza esta olhando, nunca a veria. A Capital Ship pousa sem cratera (o pedido
/// e do pod), e o foguete volta pra onde subiu (`TicarAReentrada`, o `pickTurf` do DM) -- ele nao CHEGA a um planeta, ele volta.
///
/// DIVERGENCIAS DECLARADAS (o pedido do dono): o DM pousa no miolo do mapa (`locate(rand(240,260),rand(240,260),z)`, `Planets.dm:184-215`)
/// e o pod de la nao abre cratera nenhuma (o `PlanetTech.dm` nao chama o `createCrater`).
/// ==============================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 8): a nave volta a pousar no ponto de sempre -- o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool PousoNoPontoDeSempreDeTeste;

	/// <summary>DEFEITO INJETADO (`--naveteste`, camada 8): o pod pousa sem cratera -- o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool PodSemCrateraDeTeste;

	/// <summary>Quantas crateras de pod o servidor abriu -- so pras bancadas.</summary>
	internal int CraterasDePodDeTeste;

	/// <summary>
	/// O LUGAR DO POUSO DE QUEM DESCE DO ESPACO: sorteado se ele esta PILOTANDO (com a folga do veiculo dele); nulo se nao, ou se nenhum lugar
	/// serviu -- e quem chama pousa no ponto de sempre.
	/// </summary>
	private Vec2? PousoDeQuemPilota(ServerPlayer pl, ZoneKey zona) =>
		EstaPilotando(pl.Id) ? PousoSorteadoEm(zona, PilotaNaveGrande(pl.Id)) : null;

	/// <summary>
	/// UM PONTO SORTEADO EM TERRA FIRME NESTA ZONA, longe dos corpos que ja estao nela. Nulo sem mapa (a zona gerada que ainda nasce) ou sem
	/// lugar que sirva.
	/// </summary>
	private Vec2? PousoSorteadoEm(ZoneKey zona, bool naveGrande)
	{
		if (PousoNoPontoDeSempreDeTeste || MapaDaZonaOuCatalogo(zona) is not { } mapa) return null;
		List<Vec2> corpos = [.. ZoneList(zona.Hash).Select(c => c.Pos)];
		return PousoSorteado.Sortear(mapa, _rng, naveGrande ? PousoSorteado.FolgaDaNaveGrande : PousoSorteado.FolgaDoPod, corpos);
	}

	/// <summary>
	/// O PILOTO ACABOU DE POUSAR, VINDO DO ESPACO -- as duas portas de pouso chamam aqui logo depois do `MoveToZone` (`TickDoEspaco` e
	/// `PousarEmProcedural`). O pod abre a cratera dele no ponto do pouso; a Capital Ship, nao.
	/// </summary>
	private void AoPousarDoEspaco(ServerPlayer pl)
	{
		if (NaveDoPiloto(pl) is { } n && Jandirus.Core.Tech.Naves.EhPod(n.Tipo)) AbrirACrateraDoPod(pl.Zone, pl.Pos);
	}

	/// <summary>
	/// A CRATERA GRANDE DO POD, no ponto do pouso: a cratera e a poeira no meio, e mais quatro nuvens na borda (a borda inteira levanta, como
	/// na cratera enorme da Genkidama -- `AbrirACrateraDaExplosao`). Pra zona inteira, pela porta unica de marca de chao (`MandarDecalque`).
	/// </summary>
	private void AbrirACrateraDoPod(ZoneKey zona, Vec2 onde)
	{
		if (PodSemCrateraDeTeste) return;
		MandarDecalque(zona, Protocol.Decal.CrateraDoPod, onde, Facing.South);
		MandarDecalque(zona, Protocol.Decal.Fumaca, onde, Facing.South);
		float longe = 2 * ZoneCollision.TileSize;
		for (int i = 0; i < 4; i++)
		{
			double a = i * Math.PI / 2 + Math.PI / 4;
			MandarDecalque(zona, Protocol.Decal.Fumaca, new Vec2(onde.X + longe * (float)Math.Cos(a), onde.Y + longe * (float)Math.Sin(a)),
						   Facing.South);
		}
		CraterasDePodDeTeste++;
	}
}
