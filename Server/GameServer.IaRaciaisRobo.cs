using Jandirus.Core.Ai;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DO ROBO `--diagiaraciais` (cliente, com o servidor neste processo): as tres cenas em que a IA aperta uma ativa racial que se
/// VE -- a Ira do Freeza (o kiai), o desabafo do Overflowing Power (a rajada) e o Demonic Saiyan (a esfera verde). O corpo do mundo nasce
/// pela fabrica (`NascerNpc`), o alvo e imposto pelo roteiro (`PapelDeNpc.PresaDoRoteiro`) e quem dirige e o laco de producao
/// (`TickDosCorposSemDono`): o robo so monta, espera e fotografa.
/// </summary>
public partial class GameServer
{
	/// <summary>Uma cena montada: o corpo do mundo e o alvo de pano dele.</summary>
	internal readonly record struct CenaDaIaRacial(int Npc, int Alvo);

	/// <summary>
	/// MONTA UMA CENA do lado do corpo <paramref name="id"/>: "ira" (o Freeza da fabrica colado num alvo), "desabafo" (um Saiyajin Lendario
	/// com o Overflowing Power ja alem da folga, colado no alvo) e "demonico" (um Lendario Primal no LSSJ dominado, em combate ha mais de 2
	/// min, com o alvo a nove tiles). O alvo e um cidadao da fabrica sem cerebro -- parado, e com o poder do lutador, pra nao cair no
	/// primeiro soco.
	/// </summary>
	internal CenaDaIaRacial? MontarCenaDaIaRacialDeTeste(int id, string cena)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return null;
		const float T = ZoneCollision.TileSize;
		(string molde, float alvoEm) = cena switch
		{
			"ira" => ("freeza_vegeta", 1.2f),
			"desabafo" => ("guardiao_saiyajin", 1.2f),
			"demonico" => ("guardiao_saiyajin", 7.5f),
			_ => ("", 0f),
		};
		if (molde.Length == 0) return null;
		// A DIREITA DO CORPO, A QUATRO TILES: na camera da bancada (1280x720, tile de 96 px) o canto de cima a esquerda e do painel de vida e
		// o de baixo a esquerda e do chat -- a primeira rodada montou a cena atras do painel --, e o kiai da Ira joga longe quem esta a tres
		// tiles (a segunda rodada: o corpo do robo voou, e a camera foi junto). O Demonic nao empurra ninguem: logo abaixo do corpo.
		Vec2 onde = pl.Pos + (cena == "demonico" ? new Vec2(0, 1.2f * T) : new Vec2(4 * T, 0.5f * T));
		ServerPlayer? npc = NascerNpc(molde, pl.Zone, onde, 9100 + (ulong)cena.Length);
		// O ALVO FICA ENTRE O LUTADOR E O CORPO DO ROBO: o soco e o kiai o empurram pra LONGE do lutador, e o lutador vai atras -- com o alvo
		// do lado de fora (a terceira rodada), os dois sairam da tela. O Demonic e o contrario: o alvo longe, fora da tela, e ninguem se mexe.
		ServerPlayer? alvo = NascerNpc("cidadao", pl.Zone, onde + new Vec2((cena == "demonico" ? alvoEm : -alvoEm) * T, 0), 9200 + (ulong)cena.Length);
		if (npc == null || alvo == null)
		{
			if (npc != null) RemoverNpc(npc);
			if (alvo != null) RemoverNpc(alvo);
			return null;
		}
		npc.Perfil = PerfilDeCombate.SoCorpo;   // a cena e a da raca: sem os raios do chefe, que segurariam as maos
		alvo.Cerebro = null;                    // o alvo de pano: parado
		alvo.Ficha.BP = npc.Ficha.BP;
		alvo.Ficha.Statify();
		switch (cena)
		{
			case "desabafo":
				VirarRacaDeTeste(npc, "Saiyan", Passivas.ClasseLendaria, "Saiyan");
				// O TRANSBORDAR LIGADO HA MAIS QUE O PRAZO E A FOLGA: a proxima decisao do cerebro e desabafar (sem esperar 40 s de cena)
				npc.Racial.TransbordarDesde = NowMs() - (long)((Passivas.SegundosPraDesabafar + Cerebro.FolgaDoDesabafo + 1) * 1000);
				break;
			case "demonico":
				VirarRacaDeTeste(npc, "Saiyan", Passivas.ClasseLendariaPrimal, "Primal Saiyan");
				NaFormaDoDemonicoIr(npc);
				break;
		}
		npc.Combate.EmCombate = Math.Max(npc.Combate.EmCombate, 90);
		npc.Papel!.PresaDoRoteiro = alvo.Id;
		TickDasRaciais(npc, 0.05);
		return new CenaDaIaRacial(npc.Id, alvo.Id);
	}

	/// <summary>Troca a raca e a classe de um corpo do mundo, como o sorteio as teria dado, e refaz a entrada racial.</summary>
	private void VirarRacaDeTeste(ServerPlayer pl, string raca, string classe, string linhagem)
	{
		pl.Race = pl.Ficha.Race = raca;
		pl.Class = pl.Ficha.Class = classe;
		pl.Ficha.SaiyanLineage = linhagem;
		pl.Ficha.Statify();
		TickDasRaciais(pl, 0.05);
	}

	internal void DesmontarCenaDaIaRacialDeTeste(CenaDaIaRacial c)
	{
		foreach (int quem in new[] { c.Npc, c.Alvo })
			if (_players.TryGetValue(quem, out ServerPlayer? corpo)) RemoverNpc(corpo);
	}

	internal bool IraDeTeste(int npc) => _players.TryGetValue(npc, out ServerPlayer? p) && p.Racial.IraDesde >= 0;

	internal bool TransbordandoDeTeste(int npc) => _players.TryGetValue(npc, out ServerPlayer? p) && p.Racial.TransbordarDesde >= 0;

	internal bool CarregandoODemonicoDaIaDeTeste(int npc) => _players.TryGetValue(npc, out ServerPlayer? p) && p.Racial.DemonicoCargaDesde >= 0;

	/// <summary>(2026-10-17) Este corpo esta gritando o desabafo -- e o snapshot o manda assim (`Ocupacao.GritoDoDesabafo`)?</summary>
	internal bool GritandoDeTeste(int npc) =>
		_players.TryGetValue(npc, out ServerPlayer? p) && p.Racial.GritoDesde >= 0 && OcupacaoDe(p) == Ocupacao.GritoDoDesabafo;

	internal Vec2? PosicaoDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? p) ? p.Pos : null;

	/// <summary>A IA deste corpo apertou este verb? (A escuta de producao, `EscutaDeTecnicasDaIa` -- o robo a liga.)</summary>
	internal bool IaApertouDeTeste(int npc, string verb) =>
		EscutaDeTecnicasDaIa?.Exists(x => x.Quem == npc && x.Tecnica == verb) == true;

	/// <summary>Os tiros vivos deste corpo, pelos ids que o cliente desenha.</summary>
	internal List<int> TirosVivosDeTeste(int npc) =>
		_players.TryGetValue(npc, out ServerPlayer? p) ? [.. ProjeteisDaZona(p.Zone.Hash).Where(t => t.Dono == npc && t.Vivo).Select(t => t.Id)] : [];
}
