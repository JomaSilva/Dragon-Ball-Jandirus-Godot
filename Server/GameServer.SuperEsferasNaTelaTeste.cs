using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS FIXTURES DA CENA "AS SUPER ESFERAS NA TELA" -- o passo 10b da `--diagdesejos` (`Client/RoboDeDesejos.cs`).
///
/// O robo roda no MESMO processo do servidor (`--host`) e chama estes metodos direto; nenhum e alcancavel pelo
/// fio. Cada um POSA um estado pelo caminho que a producao ja tem -- nao ha regra nova aqui:
///
///   * chegar ao espaco COMO QUEM DECOLA (a chunk de chegada ja carimbada), que e o caminho em que o pacote das
///     Super nao saia;
///   * um ponto do espaco LONGE de todas as sete, pra medir que la nao se desenha nada;
///   * outro corpo TOMAR uma esfera (o fecho de producao da disputa), pra medir que a legenda de quem esta
///     olhando troca de dono na hora.
///
/// As regras medidas (o pacote que acompanha a troca de zona; a posse que avisa todo mundo) moram em
/// `GameServer.cs`, `GameServer.SuperEsferas.cs` e `GameServer.SuperShenron.cs`, cada uma com o botao de defeito
/// na propria linha.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// CHEGA AO ESPACO COMO QUEM DECOLA: as tres linhas do `Decolar` (`GameServer.Espaco.cs`), do planeta que
	/// morre (`Destruicao.cs`) e do gerado (`Procedural.cs`) -- troca de zona, a chunk de chegada carimbada como
	/// a atual (pra a vizinhanca nao sair duas vezes) e a vizinhanca.
	///
	/// E O CARIMBO QUE IMPORTA: com ele o tique nao ve "mudou de chunk", e a lista das Super Esferas -- que so
	/// saia naquele ramo -- nao chegava a quem acabou de decolar ao lado de uma. O `MoveToZone` cru das outras
	/// bancadas nao carimba, e por isso nunca mostrou o defeito.
	/// </summary>
	internal bool ChegarAoEspacoComoQuemDecolaDeTeste(int id, Vec2 onde)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		MoveToZone(id, ZonaDoEspaco, onde);
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);
		return true;
	}

	/// <summary>
	/// UM PONTO DO ESPACO FORA DO QUE O SERVIDOR MANDA: nenhuma das sete no recorte de chunk dali (o
	/// `Espaco.PertoDeMim` do pacote), nenhum mundo por perto (encostar num planeta POUSA) e fora do disco de
	/// todo sistema (perto da estrela queima). Anda da esfera pra FORA do sistema mais proximo, de chunk em
	/// chunk, ate achar. Devolve a propria posicao da esfera se nao achou -- e a bancada reprova a montagem.
	/// </summary>
	internal Vec2 LongeDasSupersDeTeste(int numero)
	{
		Vec2 esfera = OndeEstaASuper(numero);

		var sistemas = new List<SistemaSolar>(9);
		Sistemas.PorPerto(SeedDoUniverso, esfera, sistemas);

		var rumo = new Vec2(1, 0);
		float maisPerto = float.MaxValue;
		foreach (SistemaSolar s in sistemas)
		{
			Vec2 d = esfera - s.Estrela.Pos;
			if (d.Length < 1 || d.Length >= maisPerto) continue;
			maisPerto = d.Length;
			rumo = d.Normalized();
		}

		for (int chunks = 3; chunks <= 12; chunks++)
		{
			Vec2 p = esfera + rumo * (chunks * (float)Espaco.ChunkPx);

			if (_supers.Exists(s => Espaco.PertoDeMim(p, OndeEstaASuper(s.Numero)))) continue;
			if (Espaco.PorPerto(SeedDoUniverso, ChunkId.De(p)).Count > 0) continue;

			var la = new List<SistemaSolar>(9);
			Sistemas.PorPerto(SeedDoUniverso, p, la);
			if (la.Exists(s => (p - s.Estrela.Pos).Length <= s.RaioDoSistema)) continue;

			return p;
		}
		return esfera;
	}

	/// <summary>
	/// OUTRO CORPO TOMA A SUPER ESFERA `numero`, pelo fecho de producao (<see cref="FecharADisputa"/>: as duas
	/// vitorias -- a reivindicacao e a tomada -- passam por ele). O canal de dez segundos (ou de cinco minutos)
	/// que vem antes e medido pela `--esferateste`; aqui o que se quer e o INSTANTE em que a esfera troca de mao,
	/// com alguem olhando pra ela.
	///
	/// O corpo e forjado, fecha a disputa e SAI do mundo: a posse e por assinatura e fica, como a de um jogador
	/// que deslogou. (De pe no vacuo, sem roupa, ele sufocaria em vinte segundos no meio da cena.)
	/// </summary>
	/// <returns>O nome de quem tomou, ou vazio se a esfera nao existe.</returns>
	internal string OutroTomaASuperDeTeste(int numero)
	{
		if (_supers.Find(s => s.Numero == numero) is not { } alvo) return "";

		ServerPlayer ladrao = Forjar("Freeza", OndeEstaASuper(numero), ZonaDoEspaco, "bancada_supers_ladrao");
		try
		{
			FecharADisputa(ladrao, alvo, new DisputaDeSuper
			{
				Numero = numero,
				Quem = ladrao.Id,
				Tomada = alvo.Dono.Length > 0,
				DonoNoInicio = alvo.Dono,
			});
		}
		finally { RemoverNpc(ladrao); }
		return ladrao.Name;
	}
}
