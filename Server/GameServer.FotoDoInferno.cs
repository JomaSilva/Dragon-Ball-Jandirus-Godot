using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DA FOTO DO OUTRO MUNDO POVOADO E DO VILAO DO INFERNO (`--diaginferno`, `Client/RoboDoInferno.cs`, 2026-10-16).
///
/// SO MONTAM A CENA E LEEM O ESTADO -- as regras sao da `--infernoteste`. O que nasce, nasce pelas portas de producao: a alma pelo
/// `NascerNpc` do molde de verdade (e o povoamento so e trocado pela POSICAO, perto do fotografo, porque o dele espalha as almas pelo
/// mapa inteiro), o vilao pelo `NascerVilaoDoInferno`, e a derrota pelo funil unico (`AoPerderALuta`).
/// </summary>
public partial class GameServer
{
	/// <summary>Os corpos que esta foto pos no mundo -- o <see cref="LimparAFotoDoInferno"/> os tira.</summary>
	private readonly List<int> _fotoDoInfernoNascidos = [];

	/// <summary>
	/// O CORPO LOCAL, MORTO, NO OUTRO MUNDO (ao lado da mesa do Enma), NO CEU ou NO INFERNO (no portao) -- de pe e de aureola, o estado
	/// do palco da `--diagv030` (`PalcoV30_MortoAoLadoDoEnma`).
	/// </summary>
	internal bool MortoNaFotoDoInferno(int id, string zona)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		ZoneKey z = ZoneKey.Premade(zona);
		if (_catalogo?.Get(z) == null) return false;
		pl.Ficha.dead = true;
		pl.Ficha.KO = false;
		pl.Combate.EmCombate = 0;
		pl.Combate.LutandoDeVerdade = 0;
		pl.MorteJaViajou = true;
		pl.RelogioDaMorte = long.MaxValue;
		pl.Karma = Math.Max(pl.Karma, 0);
		pl.Ficha.hell_lockout_until = 0;
		Vec2 onde = z.Name == Alem.ZonaDoOutroMundo ? MesaDoEnma(z) + new Vec2(0, 3 * ZoneCollision.TileSize)
				  : z.Name == Alem.ZonaDoInferno ? PortaoDoInferno(z)
				  : PontoDeNascimento(z);
		MoveToZone(pl.Id, z, onde);
		// (2026-10-23) ACLIMATADO AO LUGAR, como as almas que nascem nele: o morto de pe passou a sentir o chao (dono), e no Inferno (10x) quem
		// fotografa ficaria preso e se machucando -- e, passados ~30 s, a segunda morte o levaria a mesa do Enma no meio da sessao. O assunto
		// desta foto nao e a gravidade (essa e da `--gravmetateste`).
		pl.Ficha.GravMastered = Math.Max(pl.Ficha.GravMastered, Jandirus.Core.Stats.Esmagamento.Gravidade(pl.Ficha));
		MandarFicha(pl);
		return true;
	}

	/// <summary>
	/// ALMAS EM VOLTA DO CORPO, em roda a dois tiles e meio -- o molde do lugar (a condenada no Inferno). Devolve os ids. As ALMAS sao as
	/// de producao: so a posicao e escolhida aqui, e a vida diaria delas comeca na hora.
	///
	/// DOIS E MEIO, E NAO TRES E MEIO (a 1a versao): no zoom 3 a janela de 1280x720 mostra 3,75 tiles acima do fotografo, e a alma do alto
	/// da roda ficava com a aureola a uns 20 px da borda -- um passo da vida diaria e ela saia do quadro. O robo cobra a aureola INTEIRA
	/// dentro da janela (`RoboDoInferno.AureolaNaTela`).
	/// </summary>
	internal List<int> AlmasNaFotoDoInferno(int id, int quantas)
	{
		List<int> ids = [];
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return ids;
		string molde = pl.Zone.Name == Alem.ZonaDoInferno ? "alma_condenada" : "alma";
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		for (int i = 0; i < quantas; i++)
		{
			double angulo = i * Math.PI * 2 / Math.Max(quantas, 1);
			Vec2 alvo = pl.Pos + new Vec2((float)Math.Cos(angulo), (float)Math.Sin(angulo)) * (2.5f * ZoneCollision.TileSize);
			Vec2 onde = mapa?.PontoLivrePerto(alvo) ?? alvo;
			ServerPlayer? alma = NascerNpc(molde, pl.Zone, onde, 0x7140_0000UL + (ulong)(_fotoDoInfernoNascidos.Count + 1));
			if (alma == null) continue;
			_fotoDoInfernoNascidos.Add(alma.Id);
			ids.Add(alma.Id);
		}
		return ids;
	}

	/// <summary>
	/// O VILAO NO INFERNO, AGORA: o chefe <paramref name="molde"/> marcado como derrotado no degrau <paramref name="degrau"/> (0-based) num
	/// estado de saga SO PRA ESTA CHAMADA, e o nascimento de producao (`NascerVilaoDoInferno`). Depois, a DOIS tiles do fotografo, do
	/// primeiro lado que tem CHAO, e SEM CEREBRO: com 25x a media ele derrubaria quem fotografa, e a segunda morte mandaria o fotografo a
	/// mesa do Enma no meio da sessao. Que ele caca quem esta la e da `--infernoteste` (`PresaDoNpc`). Devolve o id do corpo.
	///
	/// O LADO COM CHAO, E NAO "CINCO TILES A LESTE" (a 1a versao): o Inferno e uma passarela de madeira de norte a sul no meio da lava, e a
	/// leste do portao so ha lava -- o `PontoLivrePerto` jogava o corpo pro alto da passarela, com a cabeca e a aureola FORA da foto. Norte
	/// e sul primeiro porque a roda das almas (<see cref="AlmasNaFotoDoInferno"/>) nao tem ninguem nesses dois rumos.
	/// </summary>
	internal int VilaoNaFotoDoInferno(int id, string molde, int degrau)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		if (_vilaoDoInferno is { } velho && _players.TryGetValue(velho.Npc, out ServerPlayer? corpoVelho)) RemoverNpc(corpoVelho);
		_vilaoDoInferno = null;

		EstadoDasSagas antes = _sagas;
		var so = new EstadoDasSagas();
		foreach (EloDaSaga elo in _cadeia)
		{
			var est = new EstadoDoElo { Id = elo.Id };
			foreach (ChefeDaSaga c in elo.Chefes)
				if (string.Equals(c.Molde, molde, StringComparison.OrdinalIgnoreCase))
					est.Chefes.Add(new EstadoDoChefe { Molde = c.Molde, Fase = FaseDoChefe.Derrotado, Degrau = degrau });
			so.Elos.Add(est);
		}
		_sagas = so;
		ServerPlayer? vilao;
		try { vilao = NascerVilaoDoInferno(NowMs()); }
		finally { _sagas = antes; }
		if (vilao == null) return 0;

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(vilao.Zone);
		Vec2[] lados = [new(0, -2), new(0, 2), new(-2, 0), new(2, 0)];
		Vec2 norte = pl.Pos + lados[0] * ZoneCollision.TileSize;
		Vec2 onde = mapa?.PontoLivrePerto(norte) ?? norte;   // sem lado nenhum com chao (nao acontece no Outro Mundo nem no Inferno)
		foreach (Vec2 lado in lados)
		{
			Vec2 alvo = pl.Pos + lado * ZoneCollision.TileSize;
			if (mapa == null || !mapa.ServeDeChao((int)MathF.Floor(alvo.X / ZoneCollision.TileSize), (int)MathF.Floor(alvo.Y / ZoneCollision.TileSize)))
				continue;
			onde = alvo;
			break;
		}
		vilao.Pos = onde;
		vilao.Cerebro = null;
		return vilao.Id;
	}

	/// <summary>
	/// O CONTROLE DA MOLDURA: o vilao <paramref name="tiles"/> tiles ao NORTE de onde esta, sem olhar o chao (e so pro desenho, e ele e
	/// derrotado logo depois). O robo confere que a aureola SAI da janela -- a prova de que a conta de tela dele enxerga o corte que a
	/// foto 4 das duas primeiras rodadas tinha (o Freeza no alto, a cabeca fora do quadro).
	/// </summary>
	internal bool AfastarNaFotoDoInferno(int tiles)
	{
		if (_vilaoDoInferno is not { } v || !_players.TryGetValue(v.Npc, out ServerPlayer? vilao)) return false;
		vilao.Pos += new Vec2(0, -tiles * ZoneCollision.TileSize);
		return true;
	}

	/// <summary>O NOCAUTE no vilao, com o corpo local como algoz, pelo funil de producao. Devolve se ele foi derrotado, o zeni e o karma de quem fotografa.</summary>
	internal (bool Derrotado, double Zeni, int Karma) DerrotarNaFotoDoInferno(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _vilaoDoInferno is not { } v
			|| !_players.TryGetValue(v.Npc, out ServerPlayer? vilao)) return default;
		vilao.Combate.Nocautear(30);
		AoPerderALuta(vilao, pl, morreu: false);
		MandarFicha(pl);
		return (_vilaoDoInferno?.Derrotado == true, pl.Ficha.Zeni, pl.Karma);
	}

	/// <summary>
	/// (2026-10-23) O GOLPE DE VERDADE NO VILAO, com o corpo local como quem bate -- a foto do Majin Boo que so cai pela morte por ki: o dano
	/// pronto do soco ou do tiro num NUCLEO (`MeleeResolver.AplicarDanoPronto`, o `porKi` separa um do outro) e o desfecho pelo
	/// `ResolverDesfecho`, o mesmo par do projetil. O vilao em pedacos (o Majin socado) e refeito ANTES, na hora -- o relogio de 30 s e da
	/// `--v030teste`, e em pedacos ele e intocavel. Devolve se ele esta derrotado, se esta em pedacos, e o zeni e o karma de quem fotografa.
	/// </summary>
	internal (bool Derrotado, bool Desfeito, double Zeni, int Karma) GolpeNaFotoDoInferno(int id, bool letal, bool porKi)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _vilaoDoInferno is not { } v
			|| !_players.TryGetValue(v.Npc, out ServerPlayer? vilao)) return default;
		if (vilao.Combate.Desfeito || vilao.Ficha.KO) vilao.Combate.Curar();
		foreach (BodyPart p in vilao.Combate.Corpo.Partes)
			if (p.Papel != Vitalidade.Nucleo) p.Chance = 0;
		MarcarAgressao(vilao, pl);
		GolpeResultado r = MeleeResolver.AplicarDanoPronto(vilao.Combate, 1e9, letal, _rng, "cabeca", porKi);
		ResolverDesfecho(pl, vilao, r);
		MandarFicha(pl);
		return (_vilaoDoInferno?.Derrotado == true, vilao.Combate.Desfeito, pl.Ficha.Zeni, pl.Karma);
	}

	/// <summary>Tira do mundo o que a foto pos (as almas e o vilao) e devolve o corpo local a vida, pelo `Renascer` de producao.</summary>
	internal void LimparAFotoDoInferno(int id)
	{
		foreach (int n in _fotoDoInfernoNascidos)
			if (_players.TryGetValue(n, out ServerPlayer? c)) RemoverNpc(c);
		_fotoDoInfernoNascidos.Clear();
		if (_vilaoDoInferno is { } v && _players.TryGetValue(v.Npc, out ServerPlayer? cv)) RemoverNpc(cv);
		_vilaoDoInferno = null;
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Ficha.dead) Renascer(pl);
	}
}
