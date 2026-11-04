using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// (2026-11-01, o pedido 218) O PALCO DAS FOTOS do robo `--diagbuu` (`Client/RoboDaSagaDoBuu.cs`) -- o dono: *"tire foto de tudo pra revisao"*.
/// O Boo nasce pela porta de producao da cadeia (`NascerChefeDaSaga`) e vai pro lado do jogador; dali em diante quem anda a saga e o servidor
/// rodando -- o `TickDoBoo`, o `TickDoMajin`, a fuga do Kid --, e o palco so empurra o que levaria tempo de jogo (o relogio da divisao, o
/// nocaute do jogador, a imagem vencida) e leva o jogador ao Ceu pra ver o Kid. No fim a cadeia, a Terra e o jogador voltam ao que eram.
/// </summary>
public partial class GameServer
{
	private EstadoDasSagas? _sagasAntesDoPalcoBoo;
	private EstadoDoChefe? _ecDoPalcoBoo;

	/// <summary>(r2) O escudo de quem chega ao Ceu pra foto do Kid: o robo fotografa em ~2 s, e a folga cobre a espera do corpo na tela.</summary>
	private const double SegundosDoFotografoNoCeu = 10;

	private ServerPlayer? BooDoPalco => _ecDoPalcoBoo is { } ec && _players.TryGetValue(ec.Corpo, out ServerPlayer? b) ? b : null;

	/// <summary>A cadeia so com o Boo, nascido pela porta de producao e posto a 3 tiles do jogador, na Terra. Devolve o id dele (0 = falhou).</summary>
	internal int PalcoBoo_Nascer(int idJogador)
	{
		if (!_players.TryGetValue(idJogador, out ServerPlayer? pl) || _moldes?.Get(SagaDoBuu.Molde) is not { } molde) return 0;
		_sagasAntesDoPalcoBoo ??= _sagas;
		Zerar();
		EloDaSaga elo = Array.Find(_cadeia, e => e.Id == "boo")!;
		EstadoDoElo est = Elo("boo");
		est.Disparou = true;
		var ec = new EstadoDoChefe { Molde = SagaDoBuu.Molde, Fase = FaseDoChefe.ACaminho, Bps = Sagas.Pinar(molde, MediaDoServidor()) };
		est.Chefes.Add(ec);
		NascerChefeDaSaga(elo, est, ec);
		_ecDoPalcoBoo = ec;
		if (BooDoPalco is not { } boo) return 0;

		var terra = ZoneKey.Premade("Earth");
		if (pl.Zone.Hash != terra.Hash) Transportar(pl, terra, PontoDeNascimento(terra));
		Vec2 aoLado = MapaDaZonaOuCatalogo(terra)?.PontoLivrePerto(pl.Pos + new Vec2(3 * ZoneCollision.TileSize, 0)) ?? pl.Pos;
		Transportar(boo, terra, aoLado);
		boo.CenaSegundos = 0;
		return boo.Id;
	}

	/// <summary>O Boo agora: o degrau, o corpo, a zona, o golpe, quantos ha dentro dele, e se ele esta caindo atras de alguem.</summary>
	internal (int Degrau, int Corpo, string Zona, bool Letal, int Dentro, int Presa, bool EmCena) PalcoBoo_Estado() =>
		BooDoPalco is { Papel: { } p } b
			? (p.Estagio, b.Visual.Corpo, b.Zone.Name, b.Combate.Letal, AbsorvidosPor(b.Id).Count, PresaDoNpc(b)?.Id ?? 0, EmCena(b))
			: (-1, 0, "", false, 0, 0, false);

	/// <summary>O relogio da divisao vence agora -- o proximo segundo do servidor divide o Fat Boo.</summary>
	internal void PalcoBoo_Dividir()
	{
		if (_ecDoPalcoBoo is { } ec) ec.SegundosNoDegrau = SagaDoBuu.SegundosAteADivisao;
	}

	/// <summary>O jogador cai nocauteado a 2 tiles do Boo: o Super Boo o absorve no proximo segundo (o bote).</summary>
	internal void PalcoBoo_Derrubar(int idJogador)
	{
		if (!_players.TryGetValue(idJogador, out ServerPlayer? pl) || BooDoPalco is not { } boo) return;
		Transportar(pl, boo.Zone, MapaDaZonaOuCatalogo(boo.Zone)?.PontoLivrePerto(boo.Pos + new Vec2(2 * ZoneCollision.TileSize, 0)) ?? boo.Pos);
		// (r2) o escudo da foto do Super Boo (`PalcoBoo_FotografoNaCarencia`) fica aqui: com ele o preso entraria intocavel no duelo la dentro
		pl.Combate.Carencia = 0;
		pl.Combate.Nocautear(MeleeResolver.TetoDoNocaute);
		MandarFicha(pl);
	}

	/// <summary>
	/// (r2) O FOTOGRAFO NA CARENCIA, o escudo do palco da tropa (`PalcoT_FotografoNaCarencia`; o `Invulneravel` nao serve, o estado racial o
	/// reescreve a cada tique): a foto do Super Boo espera a poeira da cena assentar na tela, e ele, solto e agressivo, derrubaria o jogador antes.
	/// </summary>
	internal void PalcoBoo_FotografoNaCarencia(int idJogador, double segundos)
	{
		if (_players.TryGetValue(idJogador, out ServerPlayer? pl)) pl.Combate.Carencia = segundos;
	}

	/// <summary>(r2) O id da imagem de dentro do Boo (0 = nenhuma): o robo espera por ela NA TELA antes da foto de dentro.</summary>
	internal int PalcoBoo_Imagem() =>
		BooDoPalco is { } boo && AbsorvidosPor(boo.Id) is { Count: > 0 } dentro ? dentro[0].Guardiao : 0;

	/// <summary>
	/// (r2) A IMAGEM PELO LADO DO SERVIDOR, pra frase da foto de dentro: na r1 ela entrou no campo de visao do cliente e nao saiu na foto. A
	/// frase diz onde ela esta -- a zona, a distancia do jogador, o voo (quem voa alto some pra quem esta no chao), o nocaute, o passo.
	/// </summary>
	internal string PalcoBoo_DaImagem(int idJogador)
	{
		int id = PalcoBoo_Imagem();
		if (!_players.TryGetValue(id, out ServerPlayer? img)) return $"imagem #{id} fora do mundo";
		_players.TryGetValue(idJogador, out ServerPlayer? pl);
		string longe = pl != null && pl.Zone.Hash == img.Zone.Hash ? $"a {(img.Pos - pl.Pos).Length / ZoneCollision.TileSize:0.0} tiles de mim" : "em OUTRA zona";
		return $"imagem #{id} {longe}, voo {img.Voando} (altura {img.Altitude:0.##}), KO {img.Ficha.KO}, andando {img.Moving}, oculta {EstaOculto(id)}; "
			   + $"eu KO {pl?.Ficha.KO}";
	}

	/// <summary>
	/// (2026-11-01, a r4 da gosma) O BOO E O JOGADOR NO SERVIDOR: a distancia entre os dois e as duas posicoes, em tiles -- pro robo comparar
	/// com o que a tela desenha (a r3 viu o Kid a 15 tiles na tela logo depois de o palco o trazer a 3).
	/// </summary>
	internal (double Tiles, Vec2 Eu, Vec2 Boo) PalcoBoo_NoServidor(int idJogador)
	{
		if (!_players.TryGetValue(idJogador, out ServerPlayer? pl) || BooDoPalco is not { } boo || boo.Zone.Hash != pl.Zone.Hash)
			return (-1, default, default);
		float t = ZoneCollision.TileSize;
		return ((boo.Pos - pl.Pos).Length / t, new Vec2(pl.Pos.X / t, pl.Pos.Y / t), new Vec2(boo.Pos.X / t, boo.Pos.Y / t));
	}

	/// <summary>(r2) O jogador esta vivo e de pe? A foto do Kid no Ceu e com o jogador inteiro.</summary>
	internal bool PalcoBoo_JogadorDePe(int idJogador) =>
		_players.TryGetValue(idJogador, out ServerPlayer? pl) && !pl.Ficha.dead && !pl.Ficha.KO;

	/// <summary>A imagem de dentro do Boo cai nocauteada: o `TickDoMajin` solta todos, e o Boo vira o Kid.</summary>
	internal bool PalcoBoo_VencerAImagem()
	{
		if (BooDoPalco is not { } boo || AbsorvidosPor(boo.Id) is not { Count: > 0 } dentro) return false;
		if (!_players.TryGetValue(dentro[0].Guardiao, out ServerPlayer? imagem)) return false;
		imagem.Combate.Nocautear(60);
		return true;
	}

	/// <summary>
	/// (r3) O KID AO LADO DO JOGADOR, logo que ele nasce -- antes da esfera, que sai de onde ele estiver. O Boo cospe os presos no chao seco
	/// mais perto DELE (`OndeOMajinCospe`), e na r2 ele voava sobre a agua: o jogador caiu a 19 tiles, e as fotos do Kid e da esfera sairam so
	/// com ele na trilha. O palco poe o Kid a 3 tiles, como o `PalcoBoo_Nascer` pos o Fat Boo.
	/// </summary>
	internal bool PalcoBoo_TrazerOKid(int idJogador)
	{
		if (!_players.TryGetValue(idJogador, out ServerPlayer? pl) || BooDoPalco is not { } kid || kid.Zone.Hash != pl.Zone.Hash) return false;
		Vec2 aoLado = MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(pl.Pos + new Vec2(3 * ZoneCollision.TileSize, 0)) ?? pl.Pos;
		Transportar(kid, pl.Zone, aoLado);
		return true;
	}

	/// <summary>O jogador vai ao Ceu, a 3 tiles do Kid -- o lugar pra onde ele fugiu.</summary>
	internal bool PalcoBoo_IrProCeu(int idJogador)
	{
		if (!_players.TryGetValue(idJogador, out ServerPlayer? pl) || BooDoPalco is not { } kid || kid.Zone.Name != SagaDoBuu.ZonaDoKid) return false;
		Transportar(pl, kid.Zone, MapaDaZonaOuCatalogo(kid.Zone)?.PontoLivrePerto(kid.Pos + new Vec2(3 * ZoneCollision.TileSize, 0)) ?? kid.Pos);
		// (r2) E CHEGA NA CARENCIA (o escudo do `PalcoBoo_FotografoNaCarencia`): o Kid e letal e caca quem aparece -- na r1 ele matou o
		// jogador antes da foto, e a `buu-8-kid-no-ceu` saiu na mesa do Enma, sem Kid nenhum.
		pl.Combate.Carencia = SegundosDoFotografoNoCeu;
		return pl.Zone.Hash == kid.Zone.Hash;
	}

	/// <summary>A Terra condenada pela esfera?</summary>
	internal bool PalcoBoo_TerraCondenada() => ZonaCondenada(ZoneKey.Premade("Earth"));

	/// <summary>Tira o Boo, devolve a Terra, a cadeia e o jogador (vivo e na Terra).</summary>
	/// <summary>
	/// (2026-11-02, a r5 da gosma) O BOO SEM CEREBRO NAS FOTOS (ou devolvido): a fuga do Kid, a esfera e o salto pro Ceu sao do relogio da
	/// saga, e nao do cerebro. Solto, o Kid trazido a 3 tiles saia atras de outra presa (o R4 da r3 e da r4: a 11-15 tiles no SERVIDOR, com
	/// o meu corpo desenhado onde o servidor me tem) e no Ceu me derrubava (o R7). A porta dos palcos (`PalcoGosma_SemCerebro`).
	/// </summary>
	internal void PalcoBoo_SemCerebro(bool sem)
	{
		if (BooDoPalco is { } boo) PalcoGosma_SemCerebro(boo.Id, sem);
	}

	internal void PalcoBoo_Desfazer(int idJogador)
	{
		PalcoBoo_SemCerebro(false);
		_fugaDoKid = null;
		_booViraKid.Clear();
		if (BooDoPalco is { } boo)
		{
			CuspirTodosDoMajin(boo.Id, "");
			RemoverNpc(boo);
		}
		_ecDoPalcoBoo = null;
		var terra = ZoneKey.Premade("Earth");
		if (ZonaCondenada(terra)) RessuscitarPlaneta(terra);
		if (_sagasAntesDoPalcoBoo != null)
		{
			_sagas = _sagasAntesDoPalcoBoo;
			_sagasAntesDoPalcoBoo = null;
			_sagasPorRetomar = false;
			SalvarSagas();
		}
		if (_players.TryGetValue(idJogador, out ServerPlayer? pl))
		{
			// o Kid caca quem chega no Ceu: o jogador da foto pode ter morrido la -- volta pelo berco, como o palco da v030
			if (pl.Ficha.dead) { pl.Ficha.passe_do_enma_ate = 0; Renascer(pl); }
			if (pl.Ficha.KO) pl.Combate.Curar();
			if (pl.Zone.Hash != terra.Hash) Transportar(pl, terra, PontoDeNascimento(terra));
			MandarFicha(pl);
		}
	}
}
