using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A SUPERFICIE DE BANCADA DO CACHE DE ZONAS (`--diagcachezona`) ============================
/// Este arquivo nao roda bancada nenhuma: ele e a UNICA coisa que o robo do cliente
/// (`Client/RoboDoCacheDeZona.cs`) precisa encomendar a AUTORIDADE -- por o corpo na orbita de um mundo
/// sorteado. O resto do roteiro e do jogador e da producao: meditar e mergulhar, abrir os olhos, voar
/// contra o disco do planeta (quem pousa e o `TickDoEspaco`) e decolar pelo canal de habilidade.
///
/// ============================ POR QUE ESTE ATALHO, E SO ESTE ============================
/// O defeito que a bancada mede e do CLIENTE (o `World` guardando a zona de onde se sai), e o que ele
/// precisa pra aparecer e de `ZoneChanged` de verdade pra a MESMA zona gerada, varias vezes. O mundo
/// sorteado mais perto de um berco fica a horas de voo, entao a bancada cria a POSICAO -- e nada alem
/// dela: o ponto e o mesmo em que uma decolagem daquele planeta deixaria o corpo
/// (`Espaco.PontoDeDecolagem`), a 90 px do disco. Dali pra baixo quem decide e o jogo.
/// ====================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// PoE O CORPO NA ORBITA DE UM MUNDO SORTEADO e devolve qual -- nulo se nao ha nenhum vivo por perto.
	///
	/// O achador e o das bancadas de servidor (`AcharPlanetaGerado`: o universo de producao, varrido com a
	/// semente DESTE servidor), e as tres linhas de baixo sao as do fim do `Decolar`: a chunk de chegada
	/// carimbada e a vizinhanca forcada, senao o cliente chega ao espaco sem planeta nenhum desenhado.
	///
	/// PLANETA MORTO NAO SERVE: o `TickDoEspaco` recusa o pouso em campo de destrocos, e a bancada ficaria
	/// esperando uma descida que a producao, com razao, nao faz.
	/// </summary>
	internal PlanetaNoEspaco? PorNaOrbitaDeUmGeradoNoTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return null;
		if (AcharPlanetaGerado(SeedDoUniverso) is not { } mundo || PlanetaMorto(mundo)) return null;

		MoveToZone(pl.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(mundo));
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);
		GD.Print($"[cachezona] #{id} na orbita de {mundo.Nome} (seed {mundo.Seed}, raio {mundo.Raio:0} px)");
		return mundo;
	}
}
