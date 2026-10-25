using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagtropa` (2026-10-18): o fotografo levado a um canto vazio de Namek, tres soldados da tropa trazidos pra
/// perto, um Namekuseijin e uma esfera postos na frente de um soldado, e o soldado nocauteado. O resto e o jogo de producao rodando: o
/// "Chegar agora" do admin, a tropa que desembarca (`TickDaTropa`), a mente do soldado (`GuiarOSoldado`) e o funil das esferas.
///
/// O FREEZA FICA NOCAUTEADO do outro lado do planeta enquanto as fotos sao tiradas: ele caca o planeta inteiro com o golpe letal, e mataria
/// o fotografo. Nocauteado ele continua no planeta -- e a tropa com ele.
/// </summary>
public partial class GameServer
{
	/// <summary>Os corpos que o palco pos no mundo (os Namekuseijins), pra tirar no fim.</summary>
	private readonly List<int> _corposDoPalcoT = [];

	/// <summary>A esfera que o palco pos na frente do soldado, e onde ela estava antes.</summary>
	private Esfera? _esferaDoPalcoT;
	private Vec2 _ondeAEsferaEstava;

	private EloDaSaga? EloComTropa() => Array.Find(_cadeia, e => e.Tropa != null);

	/// <summary>O Freeza da saga de Namek esta de pe (ou deitado, mas vivo) no planeta? A tropa so vem com ele.</summary>
	internal bool PalcoT_FreezaEmNamek() => EloComTropa() is { } elo && _sagas.Do(elo.Id) is { } est && ChefeDoEloNoPlaneta(elo, est) != null;

	/// <summary>Quantos soldados da tropa estao no mundo agora, e quantos de armadura.</summary>
	internal (int DePe, int DeArmadura) PalcoT_Tropa()
	{
		List<ServerPlayer> l = EloComTropa() is { } elo ? SoldadosDoElo(elo.Id) : [];
		return (l.Count, l.Count(s => s.Visual.Roupa.Exists(r => r.Caminho.Contains("Armor", StringComparison.OrdinalIgnoreCase))));
	}

	/// <summary>(2026-10-24) Os ids dos soldados da tropa no mundo agora -- o robo le no CLIENTE a folha de cada um que esta na tela (R3b).</summary>
	internal List<int> PalcoT_IdsDaTropa() => EloComTropa() is { } elo ? SoldadosDoElo(elo.Id).Select(s => s.Id).ToList() : [];

	/// <summary>
	/// O FOTOGRAFO NUM CANTO VAZIO DE NAMEK (terra firme, ninguem a trinta tiles), e o Freeza deitado por dez minutos. Devolve se deu.
	/// </summary>
	internal bool PalcoT_LevarANamek(int id)
	{
		if (!_players.ContainsKey(id)) return false;
		ZoneKey namek = ZoneKey.Premade("Namek");
		if (MapaDaZonaOuCatalogo(namek) is not { } mapa || CantoVazioDeNamek(mapa, namek, 14) is not { } canto) return false;
		foreach (ServerPlayer f in ChefesNoMundo())
			if (f.Zone.Hash == namek.Hash) f.Combate.Nocautear(600);
		MoveToZone(id, namek, canto);
		return true;
	}

	/// <summary>TRES SOLDADOS DA TROPA EM VOLTA DO FOTOGRAFO, pra foto da armadura. Devolve quantos vieram.</summary>
	internal int PalcoT_SoldadosEmVolta(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || EloComTropa() is not { } elo) return 0;
		const float t = ZoneCollision.TileSize;
		Vec2[] em = [new(-3 * t, -1 * t), new(3 * t, -1 * t), new(0, 2.5f * t)];
		int n = 0;
		foreach (ServerPlayer s in SoldadosDoElo(elo.Id).Where(s => !s.Ficha.KO).Take(em.Length))
			PorPerto(s, pl, em[n++]);
		return n;
	}

	/// <summary>
	/// UM NAMEKUSEIJIN E UM SOLDADO na frente do fotografo, do lado pedido (+1 a direita, -1 a esquerda): o Namekuseijin pelo molde `cidadao`
	/// de producao (a raca sai do berco de Namek, entao o palco tenta lugares ate sair um), a cinco tiles; o soldado, um da tropa, dois tiles
	/// alem dele. Devolve os dois ids, ou zeros.
	/// </summary>
	internal (int Nk, int Soldado) PalcoT_NamekuseijinESoldado(int id, int lado)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || EloComTropa() is not { Tropa: { } tropa } elo) return (0, 0);
		const float t = ZoneCollision.TileSize;
		if (NamekuseijinDoPalco(pl.Zone, PontoLivreDe(pl.Zone, pl.Pos + new Vec2(5 * t * lado, 0))) is not { } nk) return (0, 0);
		ServerPlayer? s = SoldadosDoElo(elo.Id).FirstOrDefault(x => !x.Ficha.KO && !_esferas.Exists(e => e.Portador == x.Id))
						  ?? NascerSoldado(elo, tropa, pl.Zone);
		if (s == null) return (nk.Id, 0);
		PorPerto(s, nk, new Vec2(2 * t * lado, 0));
		return (nk.Id, s.Id);
	}

	/// <summary>
	/// A CENA DO CONTRA-EXEMPLO, ISOLADA: os outros soldados a quarenta tiles saem (e o povo do palco), UM soldado fica seis tiles a esquerda do
	/// fotografo e um Namekuseijin seis a direita e quatro abaixo. Com o defeito o soldado vira o hostil comum, que so ve jogador, e vai pra cima
	/// do FOTOGRAFO -- e o Namekuseijin fica fora da linha da briga. Na primeira versao ele ficava ao lado do fotografo, no meio de varios
	/// soldados: o raspao da luta o arranhou e a regua reprovou por um tiro perdido, e nao pela escolha da presa. Devolve os dois ids, ou zeros.
	/// </summary>
	internal (int Nk, int Soldado) PalcoT_CenaDoContraExemplo(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || EloComTropa() is not { Tropa: { } tropa } elo) return (0, 0);
		const float t = ZoneCollision.TileSize;
		const float quarenta = 40 * ZoneCollision.TileSize;
		ServerPlayer? s = SoldadosDoElo(elo.Id).FirstOrDefault(x => !x.Ficha.KO && !_esferas.Exists(e => e.Portador == x.Id))
						  ?? NascerSoldado(elo, tropa, pl.Zone);
		if (s == null) return (0, 0);
		foreach (ServerPlayer outro in SoldadosDoElo(elo.Id))
			if (outro.Id != s.Id && (outro.Pos - pl.Pos).LengthSquared < quarenta * quarenta)
			{
				_tropa.Remove(outro.Id);
				RemoverNpc(outro);
			}
		PalcoT_TirarOPovo();
		if (NamekuseijinDoPalco(pl.Zone, PontoLivreDe(pl.Zone, pl.Pos + new Vec2(6 * t, 4 * t))) is not { } nk) return (0, s.Id);
		PorPerto(s, pl, new Vec2(-6 * t, 0));
		return (nk.Id, s.Id);
	}

	/// <summary>
	/// UM NAMEKUSEIJIN pelo molde `cidadao` de producao, num ponto: a raca sai do berco de Namek, entao o palco tenta lugares ate sair um (os
	/// de outra raca saem na hora). Cada cena tem lugares proprios -- dois Namekuseijins do palco nao saem iguais.
	/// </summary>
	private ServerPlayer? NamekuseijinDoPalco(ZoneKey zona, Vec2 ponto)
	{
		ulong inicio = 9_200_000 + (ulong)_lugaresDoPalcoT++ * 100;
		for (ulong lugar = inicio; lugar < inicio + 60; lugar++)
		{
			ServerPlayer? n = NascerNpc("cidadao", zona, ponto, lugar);
			if (n == null) continue;
			if (!Jandirus.Core.Social.Fusao.EhNamekuseijin(n.Race)) { RemoverNpc(n); continue; }
			_corposDoPalcoT.Add(n.Id);
			n.Pos = ponto;
			return n;
		}
		return null;
	}

	/// <summary>Quantas cenas de Namekuseijin o palco ja montou -- o comeco dos lugares da proxima.</summary>
	private int _lugaresDoPalcoT;

	/// <summary>A vida de um corpo (0..100), ou -1 se ele nao esta no mundo.</summary>
	internal double PalcoT_Vida(int id) => _players.TryGetValue(id, out ServerPlayer? p) ? p.Ficha.HP : -1;

	/// <summary>Liga (ou desliga) o defeito do soldado no ramo do hostil comum -- o contra-exemplo da foto.</summary>
	internal void PalcoT_DefeitoHostilComum(bool ligado) => SoldadoComoHostilComumDeTeste = ligado;

	/// <summary>
	/// A PRESA QUE A MENTE DO SOLDADO ESCOLHEU: a do ramo dele (`GuiarOSoldado`, guardada na tropa) -- ou, com o defeito ligado, a do ramo do
	/// hostil comum (`PresaDoHostil`, a `PresaEngajada`). E a regua da foto R4 e do contra-exemplo: a vida do Namekuseijin mede tambem o raspao
	/// de uma briga ao lado, e a presa mede a ESCOLHA.
	/// </summary>
	internal int PalcoT_PresaDe(int id) =>
		!_players.TryGetValue(id, out ServerPlayer? p) ? 0
		: SoldadoComoHostilComumDeTeste ? p.PresaEngajada
		: _tropa.TryGetValue(id, out SoldadoDaTropa? s) ? s.Presa : 0;

	/// <summary>
	/// O FOTOGRAFO NA CARENCIA -- os segundos intocaveis de quem acabou de renascer, que entram no `Intocavel` e contam pra baixo sozinhos (o
	/// palco dos pesos usa a mesma). No contra-exemplo o soldado, hostil comum, vai pra cima dele, e o golpe que o arremessava levava a camera
	/// pra longe da cena (uma rodada saiu com ele na beira da agua, a 31% de vida). O `Invulneravel` nao serve: o estado racial o reescreve a
	/// cada tique (`GameServer.Passivas.cs`), e a marca do palco sumia no seguinte.
	/// </summary>
	internal void PalcoT_FotografoNaCarencia(int id, double segundos)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Combate.Carencia = segundos;
	}

	/// <summary>
	/// O FOTOGRAFO DE PE E INTEIRO de novo: no contra-exemplo o soldado vira o hostil comum, que so ve jogador -- e o jogador a sete tiles e
	/// o fotografo. As cenas seguintes nao podem comecar com ele no chao.
	/// </summary>
	internal void PalcoT_DePe(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (pl.Ficha.KO) pl.Combate.Levantar();
		pl.Combate.Corpo.Curar(100);
		pl.Combate.SincronizarVida();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
	}

	/// <summary>Os Namekuseijins do palco saem -- antes da cena da esfera, em que o soldado nao pode ter povo a vinte tiles.</summary>
	internal void PalcoT_TirarOPovo()
	{
		foreach (int id in _corposDoPalcoT)
			if (_players.TryGetValue(id, out ServerPlayer? p)) RemoverNpc(p);
		_corposDoPalcoT.Clear();
	}

	/// <summary>
	/// UMA ESFERA DE NAMEK NO CHAO, tres tiles a direita do fotografo, e um soldado seis tiles alem dela: ele vem ate a esfera DENTRO da tela.
	/// (Depois de pega-la ele sai voando atras da proxima, e na rodada em que a esfera ficava longe do fotografo a foto o pegava na borda.) Os
	/// OUTROS soldados a quarenta tiles saem antes: a esfera do chao mais perto e o alvo de todos eles, e a cena e de um so (os que chegam
	/// depois nascem longe, um a cada vinte segundos). Devolve o soldado e o numero da esfera, ou zeros.
	/// </summary>
	internal (int Soldado, int Numero) PalcoT_EsferaNaFrenteDoSoldado(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || EloComTropa() is not { Tropa: { } tropa } elo) return (0, 0);
		Esfera? e = _esferas.Find(x => x.Portador == 0 && x.Zona.Hash == pl.Zone.Hash);
		ServerPlayer? s = SoldadosDoElo(elo.Id).FirstOrDefault(x => !x.Ficha.KO && !_esferas.Exists(b => b.Portador == x.Id))
						  ?? NascerSoldado(elo, tropa, pl.Zone);
		if (e == null || s == null) return (0, 0);
		const float t = ZoneCollision.TileSize;
		const float quarenta = 40 * ZoneCollision.TileSize;
		foreach (ServerPlayer outro in SoldadosDoElo(elo.Id))
			if (outro.Id != s.Id && (outro.Pos - pl.Pos).LengthSquared < quarenta * quarenta)
			{
				_tropa.Remove(outro.Id);
				RemoverNpc(outro);
			}
		PorPerto(s, pl, new Vec2(9 * t, 0));
		_esferaDoPalcoT = e;
		_ondeAEsferaEstava = new Vec2(e.X, e.Y);
		Vec2 chao = PontoLivreDe(pl.Zone, pl.Pos + new Vec2(3 * t, 0));
		e.X = chao.X;
		e.Y = chao.Y;
		e.PrecisaAssentar = false;
		SalvarEsferas();
		MandarEsferas(pl.Zone);
		return (s.Id, e.Numero);
	}

	/// <summary>Quem carrega a esfera do palco: o id, 0 no chao, -1 sem esfera.</summary>
	internal int PalcoT_QuemCarrega() => _esferaDoPalcoT?.Portador ?? -1;

	/// <summary>O soldado nocauteado, pela porta de producao do nocaute (o soco que derruba chama a mesma).</summary>
	internal void PalcoT_Nocautear(int soldado)
	{
		if (_players.TryGetValue(soldado, out ServerPlayer? s)) s.Combate.Nocautear(20);
	}

	/// <summary>O fim: o povo do palco, a tropa e o Freeza saem, a esfera volta pro lugar dela, e as sagas voltam a dormir.</summary>
	internal void PalcoT_Limpar()
	{
		SoldadoComoHostilComumDeTeste = false;
		PalcoT_TirarOPovo();
		TirarATropaDaBancada();
		if (_esferaDoPalcoT is { } e)
		{
			PorNoChao(e, _ondeAEsferaEstava, e.Zona);
			SalvarEsferas();
			MandarEsferas(e.Zona);
			_esferaDoPalcoT = null;
		}
		SagasLimpas();
		SalvarSagas();
	}

	/// <summary>Um corpo posto ao lado de outro, numa celula livre (a colisao responde).</summary>
	private void PorPerto(ServerPlayer quem, ServerPlayer deQuem, Vec2 desvio)
	{
		quem.Pos = PontoLivreDe(deQuem.Zone, deQuem.Pos + desvio);
		quem.Moving = false;
	}

	private Vec2 PontoLivreDe(ZoneKey zona, Vec2 desejado) => MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(desejado) ?? desejado;
}
