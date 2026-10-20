namespace Jandirus.Core.Tech;

/// <summary>
/// (2026-10-19) O CASCO ISOLA QUEM PILOTA -- o numero e as frases. Quem aplica e o `GameServer.Leme.cs`: o cabecalho de la conta o
/// pedido inteiro e onde cada metade mora.
///
/// O dono: *"planetas com gravidade quando pilota a nave o personagem piloto sofre os efeitos da gravidade, oq n deveria acontecer, e tb
/// ta dando pra usar after image enquanto pilota a nave, faça com q nenhum efeito externo afete o personagem enquanto esta pilotando a
/// nave e n da pra usar nenhuma skill ou poder enquanto esta pilotando"*.
///
/// DESIGN NOVO DO DONO, e contra o DM: la o pod copia a posicao do piloto (`PlanetTech.dm:140-144`) e o piloto e um corpo comum no mapa --
/// sente o `Planetgrav` do z em que esta, apanha e usa tudo o que sabe.
/// </summary>
public static class Leme
{
	/// <summary>
	/// A GRAVIDADE QUE O CORPO AO LEME SENTE: zero, a do espaco (o `if("Space") Planetgrav=0` do DM). Quem pesa no chao e o casco; o corpo
	/// la dentro nao e esmagado, nao treina e nao ganha nem perde poder pela gravidade de fora -- a regra que o espaco ja fazia valer pra
	/// quem viaja ("ninguem treina viajando"). Zero e um dao o mesmo em toda conta de gravidade do Core (todas pisam em `Math.Max(g, 1)`);
	/// o zero e o que apaga a pilula "Nx grav" da aba Stats, como no espaco.
	/// </summary>
	public const double GravidadeAoLeme = 0;

	/// <summary>
	/// QUANTO SE ESPERA ENTRE DOIS AVISOS DE "ao leme nao se usa poder". Os pacotes de corpo chegam em rajada (o soco segurado, a tecla
	/// apertada de novo): um aviso por pacote enterraria o chat.
	/// </summary>
	public const double SegundosEntreAvisos = 3;

	/// <summary>A FRASE DA RECUSA, e o que fazer pra voltar a usar o que se sabe -- na nave grande se volta a ponte; no pod, desembarca-se.</summary>
	/// <param name="tipoDaNave">O tipo da nave ao leme.</param>
	public static string NaoSeUsaPoder(string tipoDaNave) => NaveGrande.EhNaveGrande(tipoDaNave)
		? "você está ao leme: daqui não se usa skill nem poder. Volte à ponte (E > Voltar à ponte) pra lutar."
		: "você está pilotando: daqui não se usa skill nem poder. Desembarque pra lutar.";

	/// <summary>A FRASE DO EMBARQUE, quando havia tecnica acesa: o que se apagou, e por que.</summary>
	public const string AsTecnicasSeApagam =
		"ao leme, as técnicas acesas se apagam (buffs, Kaio-ken, miragens, carga, guarda e treino): pilotando não se usa skill nem poder.";
}
