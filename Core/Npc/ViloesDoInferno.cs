namespace Jandirus.Core.Npc;

/// <summary>
/// ============================ OS VILOES DE SAGA NO INFERNO (dono, 2026-10-16) ============================
/// *"faça com q viloes de saga derrotados as vezes apareçam no inferno no outro mundo como uma boss fight e quem vencer ganha zenni
/// e honra"*. Perguntado, o dono escolheu: a HONRA e o KARMA que ja existe (o mesmo +30 de derrubar um chefe de saga,
/// `Karma.PorDerrotarChefe`), e a FORCA e a da media de AGORA -- o vilao volta como luta de chefe pro servidor de hoje, e nao com o
/// poder que tinha quando caiu (aquele viraria luta facil com o tempo, o Freeza no Inferno do anime).
///
/// QUEM VOLTA: os chefes da cadeia de sagas que CAIRAM (<see cref="FaseDoChefe.Derrotado"/>) e nao estao voltando por conta
/// propria (o Cell que ressurge, <see cref="EstadoDoChefe.RessurgeEm"/>). O CONSUMIDO fica de fora: o androide que o Cell absorveu
/// nao foi derrotado por ninguem. Volta no degrau em que caiu (<see cref="EstadoDoChefe.Degrau"/>), sorteado entre os que podem.
///
/// ============================ O QUE NAO E ISTO ============================
///   * NAO E O *HELL BOSS RUSH* do DM (`DLC/HellBossRush`), que o dono deixou de fora (*"funcionava mal"*): la eram chefes
///     inventados (o Majin Ooze, o Rapini) que davam gemas. Aqui sao os viloes que o PROPRIO servidor derrotou.
///   * NAO MEXE NA CADEIA DE SAGAS: o vilao do Inferno nao entra em `EstadoDoChefe` nenhum (como o chefe convocado na mente), entao
///     nao ha saga pra reabrir, ultimato pra correr nem reputacao de planeta pra pagar.
///   * O ORIGINAL NAO TEM ISTO. O DM nao poe ninguem no Inferno alem dos `Afterlife_Fighter` de mapa (`NPClist.dm:324`), inimigo
///     comum que o recorte do dono mantem desligado (`Povoamento.InimigoComumLigado`).
/// ===========================================================================
///
/// AQUI FICAM OS NUMEROS e as contas puras, todos com nome. Quem poe o corpo no Inferno, avisa, paga e o tira e o
/// `GameServer.ViloesDoInferno.cs`. Os numeros sao ESCOLHA MINHA (o dono so pediu "as vezes") e estao listados pra ele no log.
/// </summary>
public static class ViloesDoInferno
{
	/// <summary>A chave geral. Desligada, nenhum vilao novo aparece (o que esta de pe segue ate o prazo).</summary>
	public static bool Ligado = true;

	/// <summary>
	/// O "AS VEZES": entre um vilao (aparecido, derrotado ou ido embora) e o proximo passam de <see cref="IntervaloMinS"/> a
	/// <see cref="IntervaloMaxS"/> segundos -- contados so enquanto ha jogador no Outro Mundo. O relogio comeca quando o primeiro chega
	/// e para quando o ultimo sai: ninguem e recebido no Inferno por um vilao no instante em que morre.
	/// </summary>
	public const double IntervaloMinS = 20 * 60, IntervaloMaxS = 50 * 60;

	/// <summary>Quanto tempo o vilao fica no Inferno, se ninguem o derrotar, antes de afundar de volta.</summary>
	public const double DuracaoS = 15 * 60;

	/// <summary>
	/// A FORCA: o degrau mais forte da ficha vale este tanto vezes a MEDIA de BP base dos jogadores online (a mesma media que pina
	/// todo chefe de saga, `GameServer.MediaDoServidor`). 25 e a razao TIPICA dos chefes do `npcs.json` contra o marco de BP que acorda
	/// a saga deles -- o Freeza de Vegeta 26,5x os 20 mil, os androides e o Boo 20x, o Freeza de Namek 80x (40x no DM: o dono dobrou a forma
	/// final em 2026-10-18) --: o vilao volta tao
	/// dificil quanto foi na saga dele, so que contra o servidor de hoje.
	/// </summary>
	public const double FatorDaMedia = 25;

	/// <summary>
	/// O ZENI, por ponto da `recompensa` que o chefe tem na saga dele (a reputacao de heroi: 100 nos chefes, 50 nos androides,
	/// `BEV_HERO_REP` e `BEV_HERO_REP_ANDROID` do DM): 250 mil num chefe, 125 mil num androide. Um quarto do revive do Enma
	/// (`Alem.PrecoDoReviveDoEnma`, 1 milhao) e duas tarefas e meia de cargo (`MissoesDeCargo.ZeniPorTarefa`, 100 mil).
	/// </summary>
	public const double ZeniPorPontoDeRecompensa = 2_500;

	/// <summary>O intervalo sorteado: <paramref name="sorteio"/> e um numero em [0,1).</summary>
	public static double ProximoIntervaloS(double sorteio) =>
		IntervaloMinS + (IntervaloMaxS - IntervaloMinS) * Math.Clamp(sorteio, 0, 1);

	/// <summary>
	/// O BP DE CADA DEGRAU NO INFERNO: o mais forte vale <see cref="FatorDaMedia"/> vezes a media; os outros guardam a PROPORCAO da
	/// ficha (o Freeza de Namek na 1a forma tem 530 mil de 8 milhoes, e continua com 6,6% do topo). Sem piso no BP que ele tinha na
	/// saga -- e a escolha do dono, "pela media de agora" --, so o de 1, pra uma media zero nao fazer um chefe de BP zero.
	/// </summary>
	public static double[] Bps(MoldeDeNpc molde, double media)
	{
		int n = Math.Max(molde.Estagios.Length, 1);
		var bps = new double[n];
		double topo = Math.Max(media * FatorDaMedia, 1);
		double maior = 0;
		foreach (EstagioDeChefe e in molde.Estagios) maior = Math.Max(maior, e.Bp);
		for (int i = 0; i < n; i++)
			bps[i] = maior > 0 && i < molde.Estagios.Length ? Math.Max(topo * molde.Estagios[i].Bp / maior, 1) : topo;
		return bps;
	}

	/// <summary>
	/// QUEM PODE VOLTAR AGORA: todo chefe da cadeia que foi derrotado e nao esta ressurgindo, na ordem da cadeia. A ficha da saga vai
	/// junto (e dela que sai o premio) e o estado tambem (e dele que sai o degrau em que ele caiu).
	/// </summary>
	public static List<(EloDaSaga Elo, ChefeDaSaga Ficha, EstadoDoChefe Estado)> Candidatos(EloDaSaga[] cadeia, EstadoDasSagas estado)
	{
		var l = new List<(EloDaSaga, ChefeDaSaga, EstadoDoChefe)>();
		foreach (EloDaSaga elo in cadeia)
		{
			if (estado.Do(elo.Id) is not { } est) continue;
			foreach (EstadoDoChefe ec in est.Chefes)
			{
				if (ec.Fase != FaseDoChefe.Derrotado || ec.RessurgeEm > 0) continue;
				ChefeDaSaga? ficha = Array.Find(elo.Chefes, c => string.Equals(c.Molde, ec.Molde, StringComparison.OrdinalIgnoreCase));
				if (ficha != null) l.Add((elo, ficha, ec));
			}
		}
		return l;
	}

	/// <summary>O zeni que o vilao carrega e que vai pra quem o derrota -- ver <see cref="ZeniPorPontoDeRecompensa"/>.</summary>
	public static double Premio(ChefeDaSaga ficha) => Math.Max(ficha.Recompensa, 0) * ZeniPorPontoDeRecompensa;
}
