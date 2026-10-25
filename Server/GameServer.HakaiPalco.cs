using Jandirus.Core.Combat;
using Jandirus.Core.Ranks;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO DA ESFERA DO HAKAI (`--diaghakaibills`, no modo host) -- este arquivo nao roda bancada nenhuma: e o que o robo do
/// cliente (`Client/RoboDoHakaiDoBills.cs`) encomenda a AUTORIDADE pra fotografar EM JOGO os dois tiros do kit do Deus da Destruicao
/// com a arte nova (dono, 2026-10-23: *"faca bills usar skills de hakai q tem esse efeito nos ataques de ki dele como na imagem"*). A
/// arte e decidida pela tabela (`ArteDeProjetil`) e a bancada do servidor a mede (`--godkitteste`, a 5b); o robo mede a TELA.
///
/// O QUE O PALCO FAZ: a historia de producao poe o Bills na Terra (o `BillsNaTerraDeTeste` do palco dele); daqui se tira o povo de
/// perto, se poe o host ao lado (fora da linha do tiro) e se escolhe o lado livre; e o Bills solta os dois tiros PELO VERB de producao
/// (`AtirarDoKit`, o mesmo que o cerebro dele aperta na luta), consagrado so pro tiro e SEM o Hakai -- a esfera nunca apaga ninguem na
/// foto. Ele fica parado no posto (o cochilo): parado e virado pro lado livre, o tiro sai sempre no mesmo pedaco de tela. Por fim o
/// host recebe o TITULO (o trono do cargo, como o jogador que o ganhou) e aperta o verb do botao.
/// </summary>
public partial class GameServer
{
	/// <summary>Pra onde os tiros da foto saem: o lado livre, longe do Whis. Escolhido no preparo.</summary>
	private Facing _hakaiRumo = Facing.East;

	/// <summary>Onde o host fica nas fotos. Escolhido no preparo; o host volta pra ca antes do tiro dele.</summary>
	private Vec2 _hakaiAncora;

	/// <summary>O dono do trono antes de o palco dar o titulo ao host ("" = vago); nulo = o palco nao mexeu. Devolvido no fim.</summary>
	private string? _hakaiTronoAntes;

	/// <summary>
	/// Quantos tiles sem parede o tiro precisa na frente de quem atira: a bola anda 18 tiles por segundo (`Projetil.AtrasoDeBola(2)`),
	/// e a foto a pega entre um e cinco tiles da boca.
	/// </summary>
	private const int TilesLivresProTiro = 8;

	/// <summary>
	/// PREPARA A CENA, com o Bills ja na Terra: o povo sai do mundo num raio de 30 tiles (os dois divinos e o host ficam), o host vai
	/// pra um tile e meio ao sul do Bills (senao ao norte) -- fora da linha do tiro, com o Bills e o tiro no alto da tela -- e o lado
	/// do tiro e o horizontal longe do Whis, se as duas fileiras (a do Bills e a do host) tiverem corredor; senao o outro.
	/// </summary>
	internal (bool Ok, Facing Rumo, int SemPovo, string Porque) HakaiPalco_Preparar(int idDoHost)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? pl)) return (false, _hakaiRumo, 0, "sem host");
		if (_bills.Corpo is not { } b) return (false, _hakaiRumo, 0, "o Bills nao esta no mundo");
		if (MapaDaZonaOuCatalogo(b.Zone) is not { } mapa) return (false, _hakaiRumo, 0, "sem mapa da zona do Bills");
		const int T = ZoneCollision.TileSize;

		// O POVO SAI: um cidadao no corredor levaria o tiro e revidaria (a `--diaglimite` pagou uma rodada por isso).
		List<ServerPlayer> povo = [.. ZoneList(b.Zone.Hash).Where(o => o.Peer == null && o != pl && !ReferenceEquals(o, _bills.Corpo)
			&& !ReferenceEquals(o, _whis.Corpo) && Vec2.Distance(o.Pos, b.Pos) < 30 * T)];
		foreach (ServerPlayer o in povo) RemoverNpc(o);

		Facing longeDoWhis = _whis.Corpo is { } w && w.Zone.Hash == b.Zone.Hash && w.Pos.X > b.Pos.X ? Facing.West : Facing.East;
		Facing[] lados = [longeDoWhis, longeDoWhis == Facing.East ? Facing.West : Facing.East];
		int cx = (int)MathF.Floor(b.Pos.X / T), cy = (int)MathF.Floor(b.Pos.Y / T);
		foreach (float dy in (float[])[1.5f, -1.5f])
		{
			var onde = new Vec2(b.Pos.X, b.Pos.Y + dy * T);
			int hy = (int)MathF.Floor(onde.Y / T);
			if (!mapa.ServeDeChao(cx, hy)) continue;
			foreach (Facing lado in lados)
			{
				if (!CorredorDoHakai(mapa, cx, cy, lado) || !CorredorDoHakai(mapa, cx, hy, lado)) continue;
				_hakaiRumo = lado;
				_hakaiAncora = onde;
				CravarPosicao(pl, onde);
				pl.Moving = false;
				return (true, lado, povo.Count, "");
			}
		}
		return (false, _hakaiRumo, povo.Count, "sem chao ao lado do Bills com corredor livre pros dois lados");
	}

	private static bool CorredorDoHakai(ZoneCollision mapa, int cx, int cy, Facing lado)
	{
		int dx = lado == Facing.East ? 1 : -1;
		for (int k = 1; k <= TilesLivresProTiro; k++)
			if (mapa.BlockedCell(cx + dx * k, cy)) return false;
		return true;
	}

	/// <summary>
	/// O BILLS SOLTA UM TIRO DO KIT -- pelo verb de producao (`AtirarDoKit`), o mesmo que o cerebro dele aperta na luta. Consagrado so
	/// pra isto e SEM o Hakai (a esfera continua a esfera, com o dom; so nao apaga ninguem), parado, virado pro lado livre e de tanque
	/// cheio. Devolve a recusa (vazio = a carga correu; quem solta e o pulso do kit).
	/// </summary>
	internal string HakaiPalco_BillsAtira(bool esfera)
	{
		if (_bills.Corpo is not { } b) return "o Bills nao esta no mundo";
		ConsagrarPraLuta(b, comHakai: false);
		b.AlvoId = 0;
		b.Moving = false;
		b.Facing = _hakaiRumo;
		b.Ficha.Ki = Math.Max(b.Ficha.Ki, b.Ficha.MaxKi);
		AtirarDoKit(b, esfera ? DomDeProjetil.Hakai : DomDeProjetil.DerrubaAGuarda);
		return CarregandoTiroDoKit(b.Id) ? "" : "o verb do kit nao carregou";
	}

	/// <summary>
	/// O HOST COM O TITULO SOLTA UM TIRO DO KIT -- pelo verb do botao (`UsarHabilidade`). O titulo e o trono do cargo, como o do
	/// jogador que o ganhou (o dono de antes e guardado e volta no <see cref="HakaiPalco_Limpar"/> do fim). Com o trono ocupado o
	/// Bills deixa de existir (ele so existe enquanto o cargo esta vago) -- por isso esta e a ultima cena. Devolve a recusa.
	/// </summary>
	internal string HakaiPalco_HostAtira(int idDoHost, bool esfera)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? pl)) return "sem host";
		_hakaiTronoAntes ??= _tronos.TryGetValue(KitDoDeus.Cargo, out string? dono) ? dono : "";
		_tronos[KitDoDeus.Cargo] = pl.Conta;
		pl.AlvoId = 0;
		pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, pl.Ficha.MaxKi);
		UsarHabilidade(pl, esfera ? KitDoDeus.VerbEsfera : KitDoDeus.VerbFuria);
		return CarregandoTiroDoKit(pl.Id) ? "" : "o verb do kit nao carregou";
	}

	/// <summary>O host volta pra ancora (pelo funil de "o servidor moveu voce"): o robo o fez andar um passo pra virar pro lado do tiro.</summary>
	internal void HakaiPalco_ReporOHost(int idDoHost)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? pl)) return;
		if (Vec2.Distance(pl.Pos, _hakaiAncora) > 0.5f) CravarPosicao(pl, _hakaiAncora);
		pl.Moving = false;
	}

	/// <summary>Pra onde o host olha, na conta do SERVIDOR (o tiro dele sai pra la).</summary>
	internal Facing HakaiPalco_OlharDoHost(int idDoHost) => _players.TryGetValue(idDoHost, out ServerPlayer? pl) ? pl.Facing : Facing.South;

	/// <summary>
	/// O TIRO DO KIT MAIS NOVO DESTE CORPO, vivo: o id (o node do cliente se chama `Tiro{id}`), a arte que o anuncio levou e quantos
	/// tiles ele ja andou.
	/// </summary>
	internal (bool Achou, int Id, ArteDeKi Arte, double Andou) HakaiPalco_Tiro(int idDoCorpo)
	{
		if (!_players.TryGetValue(idDoCorpo, out ServerPlayer? pl)) return (false, 0, ArteDeKi.Nenhuma, 0);
		Projetil? achado = null;
		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
			if (p.Vivo && p.Dono == idDoCorpo && p.Dom is (DomDeProjetil.Hakai or DomDeProjetil.DerrubaAGuarda) && (achado == null || p.Id > achado.Id))
				achado = p;
		return achado is { } a ? (true, a.Id, a.Arte, a.AndouTiles) : (false, 0, ArteDeKi.Nenhuma, 0);
	}

	/// <summary>
	/// ENTRE UMA FOTO E OUTRA: os tiros da cena somem, e o Bills deixa de carregar o kit no corpo (a mesma porta da luta que acaba).
	/// No FIM, o trono volta ao dono de antes.
	/// </summary>
	internal void HakaiPalco_Limpar(int idDoHost, bool fim)
	{
		if (_bills.Corpo is { } b)
		{
			LimparProjeteisDeUmDono(b.Id, b.Zone.Hash);
			if (EhDeusDaDestruicao(b)) DesconsagrarDaLuta(b);
		}
		if (_players.TryGetValue(idDoHost, out ServerPlayer? pl)) LimparProjeteisDeUmDono(pl.Id, pl.Zone.Hash);
		if (!fim || _hakaiTronoAntes is not { } antes) return;
		if (antes.Length > 0) _tronos[KitDoDeus.Cargo] = antes;
		else _tronos.Remove(KitDoDeus.Cargo);
		_hakaiTronoAntes = null;
	}
}
