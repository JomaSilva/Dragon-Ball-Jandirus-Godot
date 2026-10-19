using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagesferae` (2026-10-18): uma Esfera do Dragao trazida pra perto do corpo, e o largar no fim.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// UMA ESFERA A UM TILE DO CORPO: uma de um set DESTE mundo -- a policia de planeta devolve ao mundo dela a esfera de fora (a primeira rodada,
	/// o job97, trouxe uma de Namek pra Terra: a dica acendeu e o E ouviu "nao ha nenhuma ao seu alcance"). Se o mundo nao tem set, ele e montado
	/// como o `ErguerEstatua` o monta -- o set e as sete pelo `RefazerAsEsferas` de producao. A estatua em si nao esta sob teste, e o verbo dela
	/// pede um Namekuseijin do Cla do Dragao (o job98: o robo e Humano, e a recusa deixou o palco sem esfera). Devolve o numero da esfera, ou 0.
	/// </summary>
	internal int PalcoEE_EsferaAoLado(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		Esfera? DesteMundo() => _esferas.FirstOrDefault(x => x.Portador == 0 && SetPorId(x.Set) is { } s && s.Zona.Hash == pl.Zone.Hash);
		if (DesteMundo() == null)
		{
			int idDoSet = ProximoIdDeSet();
			if (idDoSet < 0) return 0;
			var set = new SetDeEsferas
			{
				Id = idDoSet, Ex = pl.Pos.X, Ey = pl.Pos.Y, CriadorSig = pl.Assinatura, CriadorConta = pl.Conta, CriadorNome = pl.Name,
				Poder = pl.Ficha.BP, Desejos = 1, OffTime = Jandirus.Core.Magic.Esferas.OffTimeDeJogador,
				NomeDoDragao = "SHENRON", TituloDoDragao = "O DRAGÃO ETERNO", Dragao = "shenron", Folha = "comum",
			};
			set.PorZona(pl.Zone);
			_sets.Add(set);
			RefazerAsEsferas(set);
		}
		if (DesteMundo() is not { } e) return 0;
		e.X = pl.Pos.X + ZoneCollision.TileSize;
		e.Y = pl.Pos.Y;
		e.PrecisaAssentar = false;
		SalvarEsferas();
		MandarEsferas(pl.Zone);
		return e.Numero;
	}

	/// <summary>O corpo larga o que carrega -- o verbo de producao (`db_largar`).</summary>
	internal void PalcoEE_Largar(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && QuantasCarrega(pl.Id) > 0) LargarEsferas(pl);
	}
}
