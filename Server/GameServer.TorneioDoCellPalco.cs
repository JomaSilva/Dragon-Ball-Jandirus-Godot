using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagtorneiocell` (2026-10-19) -- as fotos dos pedidos de hoje, pro dono ver (*"tire print de todos os testes
/// pra provar"*): o torneio do Cell inteiro, os androides fora do Scan e do Sense, e o Freeza caido em Namek. Cada uma monta a cena pelas
/// funcoes de PRODUCAO (o nascimento da saga, o tique do torneio, o monitor da saga, os sentidos), so cortando a espera; a Terra que o
/// Kamehameha condena e devolvida antes do commit.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// O TORNEIO DO CELL
	// =====================================================================

	/// <summary>
	/// O TORNEIO ABERTO: a saga do Cell em curso com ele a caminho do torneio, chegando hoje -- e o nascimento de producao o poe na arena. Pra
	/// foto, o Cell fica tres tiles dentro da beirada sul e o jogador quatro fora dela, olhando pra ele. Quem sobrou de uma cena anterior (o Cell,
	/// os Cell Jr) sai antes. Devolve o id do Cell (zero = nao abriu).
	/// </summary>
	internal int PalcoTC_Abrir(int jogador)
	{
		if (!_players.TryGetValue(jogador, out ServerPlayer? pl) || _moldes?.Get("cell") is not { } molde) return 0;
		EncerrarOTorneioDoCell();
		foreach (ServerPlayer chefe in _players.Values.Where(p => p.Papel is { EhChefe: true }).ToList()) RemoverNpc(chefe);
		foreach (int id in _cellJrs.ToList()) if (_players.TryGetValue(id, out ServerPlayer? jr)) RemoverNpc(jr);
		_cellJrs.Clear();
		if (_sagas.Do("cell") is not { } est) return 0;
		foreach (string saga in new[] { "freeza_vegeta", "freeza_namek" })
			if (_sagas.Do(saga) is { } outra) outra.Fase = FaseDaSaga.Vencida;
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		est.Chefes.Clear();
		est.Chefes.Add(new EstadoDoChefe
		{
			Molde = "cell", Fase = FaseDoChefe.ACaminho, DiasRestantes = 0, Degrau = 2, NoTorneio = true,
			Bps = Sagas.Pinar(molde, MediaDoServidor()),
		});
		VirouODia(soAsChegadasDeHoje: true);
		if (_torneioDoCell is not { } t || !_players.TryGetValue(t.Cell, out ServerPlayer? cell)) return 0;

		Arena a = t.Arena;
		int cx = (a.X1 + a.X2) / 2;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(t.Zona);
		Vec2 fora = new Vec2(cx + 0.5f, a.Y2 + 4 + 0.5f) * ZoneCollision.TileSize;
		Transportar(cell, t.Zona, new Vec2(cx + 0.5f, a.Y2 - 3 + 0.5f) * ZoneCollision.TileSize);
		Transportar(pl, t.Zona, mapa?.PontoLivrePerto(fora) ?? fora);
		CravarOlhar(pl, Facing.North);
		cell.Facing = Facing.South;
		return cell.Id;
	}

	/// <summary>
	/// O JOGADOR PISA NA ARENA, seis tiles dentro da beirada sul: o tique de producao o chama, e a contagem corre sozinha. Ele fica INTOCAVEL
	/// (a carencia da espera do torneio) -- a foto e do desenho, e um Cell Perfeito acabaria a luta do robo antes do clique.
	/// </summary>
	internal void PalcoTC_Desafiar(int jogador)
	{
		if (_torneioDoCell is not { } t || !_players.TryGetValue(jogador, out ServerPlayer? pl)) return;
		Arena a = t.Arena;
		pl.Combate.Carencia = CarenciaDeEspera;
		Transportar(pl, t.Zona, new Vec2((a.X1 + a.X2) / 2 + 0.5f, a.Y2 - 6 + 0.5f) * ZoneCollision.TileSize);
	}

	/// <summary>A fase do torneio agora (o robo espera a luta comecar).</summary>
	internal string PalcoTC_Fase() => _torneioDoCell?.Fase.ToString() ?? "";

	/// <summary>
	/// O CELL PISA FORA DA ARENA no meio da luta: tres tiles alem da beirada sul, perto do jogador, A PE -- o tique do juiz derruba a regra e
	/// solta os Cell Jr. Na hora do juiz o jogador esta DENTRO, dois tiles antes da beirada: fora, com a regra ainda de pe, o juiz o eliminaria
	/// por ring-out antes de olhar o Cell (a rodada d3 do robo caiu assim). Caida a regra, ele vai pra perto do Cell, pra foto da roda dos sete.
	/// </summary>
	internal bool PalcoTC_CellForaDaArena(int jogador)
	{
		if (_torneioDoCell is not { Fase: FaseDoTorneioDoCell.Luta } t || !_players.TryGetValue(t.Cell, out ServerPlayer? cell)
			|| !_players.TryGetValue(jogador, out ServerPlayer? pl)) return false;
		Arena a = t.Arena;
		int cx = (a.X1 + a.X2) / 2;
		// ELE POUSA NA MARRA: a regra so cai com o pe no chao (`TorneioDoCell.CellDerrubaARegra`, como o ring-out de quem desafia), e o
		// `AlternarVoo` recusa quem tem um ataque de ki na mao -- na rodada e2 do robo o Cell estava soltando um Kamehameha e seguiu voando
		cell.Voando = false;
		cell.Altitude = 0f;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(t.Zona);
		Vec2 fora = new Vec2(cx + 3 + 0.5f, a.Y2 + 3 + 0.5f) * ZoneCollision.TileSize;
		Transportar(cell, t.Zona, mapa?.PontoLivrePerto(fora) ?? fora);
		Transportar(pl, t.Zona, new Vec2(cx - 2 + 0.5f, a.Y2 - 2 + 0.5f) * ZoneCollision.TileSize);
		// ...e o juiz olha AGORA (o tique de producao, como a bancada faz): no tique seguinte o cerebro dele ja religou o voo -- a IA voa
		TickDoTorneioDoCell(0.05);
		// Com a regra no chao ninguem mais perde por sair: o jogador vai pra um tile a oeste do Cell, e a camera (que segue o jogador, a uns 13
		// por 7 tiles de quadro) pega ele e a roda dos sete -- com o jogador na arena eles ficavam no canto da foto (rodada e3).
		if (!t.RegraDaArena) Transportar(pl, t.Zona, cell.Pos + new Vec2(-ZoneCollision.TileSize, 0));
		return true;
	}

	/// <summary>Quantos Cell Jr estao de pe, e se a regra da arena caiu.</summary>
	internal (int CellJr, bool RegraCaiu) PalcoTC_CellJr() => (_cellJrs.Count(_players.ContainsKey), _torneioDoCell is { RegraDaArena: false });

	// ---- (2026-10-20) A CABECA DO CELL JR, de perto ----
	private int _palcoDoCellJr;

	/// <summary>
	/// (2026-10-20) UM CELL JR DE FRENTE, PRA FOTO DA CABECA -- o dono: *"os cell jr tao com o topo da cabeça cortados"*. Os passos do
	/// `NascerUmCellJr` (o molde, o corpo reservado, as aparencias), dois tiles a leste do jogador, olhando pro SUL e sem cerebro: parado, a foto
	/// e do desenho. Devolve o id (zero = nao nasceu).
	/// </summary>
	internal int PalcoTC_CellJrDeFrente(int jogador)
	{
		PalcoTC_LimparOCellJrDeFrente();
		if (!_players.TryGetValue(jogador, out ServerPlayer? pl)) return 0;
		ServerPlayer? jr = NascerNpc(TorneioDoCell.MoldeDoCellJr, pl.Zone, pl.Pos + new Vec2(2 * ZoneCollision.TileSize, 0), 7_600_201);
		if (jr?.Papel == null) return 0;
		jr.Visual.Corpo = TorneioDoCell.CorpoDoCellJr;
		TrocarAparencias(jr);
		jr.Cerebro = null;
		jr.Facing = Facing.South;
		jr.Combate.Carencia = CarenciaDeEspera;
		_palcoDoCellJr = jr.Id;
		return jr.Id;
	}

	/// <summary>O Cell Jr da foto da cabeca sai.</summary>
	internal void PalcoTC_LimparOCellJrDeFrente()
	{
		if (_players.TryGetValue(_palcoDoCellJr, out ServerPlayer? jr)) RemoverNpc(jr);
		_palcoDoCellJr = 0;
	}

	/// <summary>O CELL NOCAUTEADO PELO JOGADOR: o golpe final e do desafiante (o `UltimoAgressor`), e o proximo tique do juiz o da por morto na luta.</summary>
	internal bool PalcoTC_NocautearOCell()
	{
		if (_torneioDoCell is not { Fase: FaseDoTorneioDoCell.Luta } t || !_players.TryGetValue(t.Cell, out ServerPlayer? cell)) return false;
		cell.UltimoAgressor = t.Desafiante;
		cell.Combate.Nocautear(30);
		return true;
	}

	/// <summary>O Cell ja morreu na luta e a saga o pos voltando? Entao a volta e daqui a 2 s (o minuto do `ressurgeEmSegundos` e espera, nao regra).</summary>
	internal bool PalcoTC_AdiantarAVolta()
	{
		if (_sagas.Do("cell") is not { } est) return false;
		foreach (EstadoDoChefe ec in est.Chefes)
			if (ec is { Molde: "cell", Fase: FaseDoChefe.Derrotado } && ec.RessurgeEm > 0)
			{
				ec.RessurgeEm = _relogioDoMundo + 2;
				return true;
			}
		return false;
	}

	/// <summary>O Cell de agora no mundo (id e degrau da ficha, 0-based), e se o torneio ainda esta de pe.</summary>
	internal (int Id, int Degrau, bool Torneio) PalcoTC_OCell()
	{
		ServerPlayer? cell = _players.Values.FirstOrDefault(p => p.Papel?.Molde.Id == "cell" && !p.Ficha.dead);
		return (cell?.Id ?? 0, cell?.Papel?.Estagio ?? -1, _torneioDoCell != null);
	}

	/// <summary>
	/// O KAMEHAMEHA SEM OS 5 MIN (a fila vazia e medida na bancada): a luta da vez acaba (o desafiante desiste), o Cell vai pra beirada LESTE da
	/// arena, dois tiles dentro, e o jogador <paramref name="tilesAOeste"/> tiles a oeste dele, na mesma linha -- de lado os dois cabem no quadro
	/// (a camera tem uns 13 por 7 tiles). O Kamehameha comeca pela funcao de producao (`Kamehameha`: o canal da tecnica, 2x), e o jogador e o
	/// mais perto: e nele que o Cell mira. (2026-10-19) Ate a rodada e4 era a esfera desenhada, e o jogador ficava ao sul.
	/// </summary>
	internal bool PalcoTC_Kamehameha(int jogador, int tilesAOeste = 4)
	{
		if (_torneioDoCell is not { } t || !_players.TryGetValue(t.Cell, out ServerPlayer? cell) || !_players.TryGetValue(jogador, out ServerPlayer? pl))
			return false;
		if (t.Fase is FaseDoTorneioDoCell.Contagem or FaseDoTorneioDoCell.Luta)
			TerminarLutaDoCell(t, cell, _players.GetValueOrDefault(t.Desafiante), "desistencia");
		Arena a = t.Arena;
		int cy = (a.Y1 + a.Y2) / 2;
		cell.Voando = false;
		cell.Altitude = 0f;
		Transportar(cell, t.Zona, new Vec2(a.X2 - 2 + 0.5f, cy + 0.5f) * ZoneCollision.TileSize);
		Transportar(pl, t.Zona, new Vec2(a.X2 - 2 - tilesAOeste + 0.5f, cy + 0.5f) * ZoneCollision.TileSize);
		CravarOlhar(pl, Facing.East);
		_palcoDoCell = cell.Id;
		Kamehameha(t, cell);
		return true;
	}

	/// <summary>O Cell da ultima cena do Kamehameha (o torneio fecha quando ele morre ou a Terra explode, e as perguntas abaixo continuam).</summary>
	private int _palcoDoCell;

	/// <summary>O Cell esta com o Kamehameha na mao, carregando? (o canal da tecnica aberto, o raio ainda nao saiu)</summary>
	internal bool PalcoTC_CarregandoOKamehameha() => _canais.GetValueOrDefault(_palcoDoCell) is { Verbo: "Kamehameha", Raio: null };

	/// <summary>O raio do Kamehameha que saiu (id e escala do servidor; zero = ainda nao saiu).</summary>
	internal (int Id, double Escala) PalcoTC_RaioDoKamehameha() =>
		_canais.GetValueOrDefault(_palcoDoCell)?.Raio is { } r ? (r.Id, r.EscalaVisual)
		: _kamehamehaDoCell?.Raio is { } q ? (q.Id, q.EscalaVisual) : (0, 0);

	/// <summary>
	/// O JOGADOR VAI PRO FIM DO KAMEHAMEHA: a oeste do Cell, dois tiles antes de onde o raio acaba (o alcance da tecnica, contado da boca, um
	/// tile a frente do Cell). O Cell segue mirando o jogador mais perto, e a camera, que segue o jogador, pega o raio chegando e o estouro do fim.
	/// (2026-10-20) O Cell e o da cena (`_palcoDoCell`): o Super Perfeito perdendo nao tem torneio.
	/// </summary>
	internal bool PalcoTC_IrProFimDoKamehameha(int jogador)
	{
		if (!_players.TryGetValue(_palcoDoCell, out ServerPlayer? cell) || !_players.TryGetValue(jogador, out ServerPlayer? pl)) return false;
		double alcance = DegrauDoRaioG6("Kamehameha", cell.Ficha.beamskill).Alcance;
		Vec2 onde = cell.Pos + new Vec2((float)(-(1 + alcance - 2) * ZoneCollision.TileSize), 0);
		Transportar(pl, cell.Zone, onde);
		CravarOlhar(pl, Facing.East);
		return true;
	}

	/// <summary>A Terra comecou a explodir?</summary>
	internal bool PalcoTC_TerraExplodindo() => MorteDaZona(ZoneKey.Premade("Earth")) is { Fase: FaseDaMorte.Explodindo };

	// ---- O EMBATE CONTRA O KAMEHAMEHA ----
	private (int Id, double Bp) _bpDoPalco;

	/// <summary>
	/// O EMBATE, PRA FOTO: a cena do Kamehameha com o jogador SEIS tiles a oeste do Cell, de frente pra ele, com <paramref name="vezesOBp"/> vezes
	/// o BP do Cell (o dele fica guardado: `PalcoTC_DevolverOBp`) -- e o Kamehameha DELE pelo verb da tecnica, com a carga igualada a do Cell: os
	/// dois saem juntos e se encontram no meio. As letras da disputa o robo aperta (`PalcoTC_ApertarNoEmbate`): ele tem teclado, e o servidor nao
	/// aperta por quem tem.
	/// </summary>
	internal bool PalcoTC_EmbateDoKamehameha(int jogador, double vezesOBp) =>
		PalcoTC_Kamehameha(jogador, tilesAOeste: 6) && PalcoTC_DisputarOKamehameha(jogador, vezesOBp);

	/// <summary>
	/// (2026-10-20) O JOGADOR ENTRA NA DISPUTA do Kamehameha que o Cell da cena (`_palcoDoCell`) ja carrega -- o do torneio, ou o do Super
	/// Perfeito perdendo, que o vigia soltou: a parte do jogador do embate de cima, separada.
	/// </summary>
	internal bool PalcoTC_DisputarOKamehameha(int jogador, double vezesOBp)
	{
		if (!_players.TryGetValue(jogador, out ServerPlayer? pl) || !_players.TryGetValue(_palcoDoCell, out ServerPlayer? cell)) return false;
		_bpDoPalco = (pl.Id, pl.Ficha.BP);
		pl.Ficha.BP = cell.Ficha.BP * vezesOBp;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.AlvoId = cell.Id;
		if (_canais.TryGetValue(pl.Id, out CanalDeKi? velho)) FecharCanal(pl.Id, velho, null);
		RaioNomeadoG6(pl, "Kamehameha");
		if (!_canais.TryGetValue(pl.Id, out CanalDeKi? doJogador) || !_canais.TryGetValue(cell.Id, out CanalDeKi? doCell)) return false;
		doJogador.CargaRestante = doCell.CargaRestante;
		return true;
	}

	/// <summary>
	/// (2026-10-20) O SUPER PERFEITO PERDENDO, PRA FOTO: a saga do Cell em curso com ele no ULTIMO degrau e sem torneio (a luta livre) -- pelo
	/// nascimento de producao da saga (`NascerChefeDaSaga`, como quem retoma: sem a cena da chegada, que cobriria o quadro) --, na beirada leste
	/// da arena da Terra, e o jogador <paramref name="tilesAOeste"/> tiles a oeste, na mesma linha, intocavel (o Super Perfeito e letal). PERDENDO:
	/// o tronco a 22% -- abaixo dos 25% do dono, acima dos 20% do nocaute. <paramref name="nocauteado"/>: o que sobra de um golpe alto de uma vez,
	/// o tronco a 10% e ele no chao. E o tique de producao roda NA HORA (`TickDoTorneioDoCell`): o vigia o faz soltar o Kamehameha sozinho, antes
	/// de o cerebro dele agir. Devolve o id dele (zero = nao montou).
	/// </summary>
	internal int PalcoTC_SuperPerfeitoPerdendo(int jogador, int tilesAOeste = 4, bool nocauteado = false)
	{
		if (!_players.TryGetValue(jogador, out ServerPlayer? pl) || _moldes?.Get("cell") is not { } molde || _sagas.Do("cell") is not { } est
			|| Array.Find(_cadeia, e => e.Id == "cell") is not { } elo) return 0;
		PalcoTC_DevolverATerra(jogador);
		foreach (ServerPlayer chefe in _players.Values.Where(p => p.Papel is { EhChefe: true }).ToList()) RemoverNpc(chefe);
		foreach (int id in _cellJrs.ToList()) if (_players.TryGetValue(id, out ServerPlayer? jr)) RemoverNpc(jr);
		_cellJrs.Clear();
		foreach (string saga in new[] { "freeza_vegeta", "freeza_namek" })
			if (_sagas.Do(saga) is { } outra) outra.Fase = FaseDaSaga.Vencida;
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		est.Chefes.Clear();
		var ec = new EstadoDoChefe
		{
			Molde = "cell", Fase = FaseDoChefe.ACaminho, DiasRestantes = 0, Degrau = molde.Estagios.Length - 1, Bps = Sagas.Pinar(molde, MediaDoServidor()),
		};
		est.Chefes.Add(ec);
		NascerChefeDaSaga(elo, est, ec, ComoChegou.Retomando);
		if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? cell)) return 0;

		var palco = _torneioCfg.Terra;
		var zona = ZoneKey.Premade(palco.Zona);
		Arena a = palco.Arena;
		int cy = (a.Y1 + a.Y2) / 2;
		cell.Voando = false;
		cell.Altitude = 0f;
		Transportar(cell, zona, new Vec2(a.X2 - 2 + 0.5f, cy + 0.5f) * ZoneCollision.TileSize);
		Transportar(pl, zona, new Vec2(a.X2 - 2 - tilesAOeste + 0.5f, cy + 0.5f) * ZoneCollision.TileSize);
		CravarOlhar(pl, Facing.East);
		pl.Combate.Carencia = CarenciaDeEspera;
		_palcoDoCell = cell.Id;
		foreach (Jandirus.Core.Combat.BodyPart p in cell.Combate.Corpo.Partes)
			if (p.Nome == Jandirus.Core.Combat.Body.NomeDoTronco) p.Vida = p.VidaMax * (nocauteado ? 0.10 : 0.22);
		cell.Combate.SincronizarVida();
		if (nocauteado) cell.Combate.Nocautear(30);
		TickDoTorneioDoCell(0);
		return cell.Id;
	}

	/// <summary>
	/// (2026-10-20) O Cell da cena: carregando o Kamehameha do vigia? O degrau (0-based), se ha torneio, a fracao de vida do tronco dele e se esta
	/// caido.
	/// </summary>
	internal (bool Carregando, int Degrau, bool Torneio, double Tronco, bool Caido) PalcoTC_OSuperPerfeito()
	{
		if (!_players.TryGetValue(_palcoDoCell, out ServerPlayer? cell) || cell.Papel == null) return (false, -1, _torneioDoCell != null, 1, false);
		Jandirus.Core.Combat.BodyPart? tronco = cell.Combate.Corpo.Partes.Find(p => p.Nome == Jandirus.Core.Combat.Body.NomeDoTronco);
		return (PalcoTC_CarregandoOKamehameha() && _kamehamehaDoCell?.Cell == cell.Id, cell.Papel.Estagio, _torneioDoCell != null,
				tronco == null ? 1 : tronco.Vida / tronco.VidaMax, cell.Ficha.KO);
	}

	/// <summary>A disputa de ki do Cell da cena esta de pe?</summary>
	internal bool PalcoTC_EmEmbate() => _emEmbateDeKi.ContainsKey(_palcoDoCell);

	/// <summary>O robo aperta a letra que o jogador tem na tela agora (a mesma porta do teclado, `TeclaDeQualquerEmbate`).</summary>
	internal bool PalcoTC_ApertarNoEmbate(int jogador)
	{
		if (!_players.TryGetValue(jogador, out ServerPlayer? pl) || !_emEmbateDeKi.TryGetValue(pl.Id, out DisputaDeKi? d)) return false;
		LadoDeKi meu = d.A.Quem == pl ? d.A : d.B;
		if (meu.Letra == '\0') return false;
		TeclaDeQualquerEmbate(pl, meu.Letra);
		return true;
	}

	/// <summary>Depois do embate: o Cell da cena morreu? E a Terra esta inteira?</summary>
	internal (bool CellMorto, bool TerraInteira) PalcoTC_DepoisDoEmbate() =>
		(_players.TryGetValue(_palcoDoCell, out ServerPlayer? c) && c.Ficha.dead, MorteDaZona(ZoneKey.Premade("Earth")) == null);

	/// <summary>O BP do jogador volta ao de antes da cena do embate.</summary>
	internal void PalcoTC_DevolverOBp()
	{
		if (_bpDoPalco.Id == 0 || !_players.TryGetValue(_bpDoPalco.Id, out ServerPlayer? pl)) return;
		pl.Ficha.BP = _bpDoPalco.Bp;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		_bpDoPalco = default;
	}

	/// <summary>Depois do Kamehameha: a Terra que ele condenou volta (antes do commit), o torneio fecha e o jogador volta a ser tocavel.</summary>
	internal void PalcoTC_DevolverATerra(int jogador)
	{
		var terra = ZoneKey.Premade("Earth");
		if (MorteDaZona(terra) is { Fase: FaseDaMorte.Explodindo }) AbortarMorte(terra, "palco do torneio do Cell");
		EncerrarOTorneioDoCell();
		_kamehamehaDoCell = null;   // (2026-10-20) o Kamehameha que sobrou de uma cena acaba com ela -- com ou sem torneio
		// (2026-10-19) e os raios da cena do Kamehameha: o canal de quem ainda segura um, e a disputa, se sobrou
		foreach (int id in new[] { jogador, _palcoDoCell })
		{
			if (_emEmbateDeKi.ContainsKey(id)) SoltarDoEmbateDeKi(id);
			if (_canais.TryGetValue(id, out CanalDeKi? c)) FecharCanal(id, c, null);
		}
		if (_players.TryGetValue(jogador, out ServerPlayer? pl) && pl.Combate.Carencia >= CarenciaDeEspera) pl.Combate.Carencia = 0;
	}

	// =====================================================================
	// OS ANDROIDES FORA DO SCAN E DO SENSE
	// =====================================================================
	private readonly List<int> _palcoDosAndroides = [];

	/// <summary>
	/// QUATRO CORPOS A LESTE DO JOGADOR, todos conhecidos dele (a aba escreve o nome de quem se conhece): o Humano, o jogador androide de
	/// ENERGIA INFINITA (o "Numero 18"), o de ABSORCAO (o "Numero 19") e o ANDROIDE 17 NPC da saga do Cell. O jogador fica de scouter ligado.
	/// </summary>
	internal bool PalcoAN_Montar(int jogador)
	{
		if (!_players.TryGetValue(jogador, out ServerPlayer? pl)) return false;
		PalcoAN_Limpar(jogador);
		ServerPlayer Corpo(string nome, int tiles)
		{
			ServerPlayer c = ForjarComSkills(nome, pl.Pos + new Vec2(tiles * ZoneCollision.TileSize, 0), 30_000);
			c.Conta = "palco_androides_" + nome.ToLowerInvariant().Replace(' ', '_');
			if (c.Zone.Hash != pl.Zone.Hash) Transportar(c, pl.Zone, pl.Pos + new Vec2(tiles * ZoneCollision.TileSize, 0));
			pl.Social.Fotografar(c.Assinatura, c.Name, c.Race, "Male", 25, NowMs());
			_palcoDosAndroides.Add(c.Id);
			return c;
		}
		Corpo("Humano", 3);
		ServerPlayer infinito = Corpo("Numero 18", 5);
		infinito.Race = "Android";
		infinito.Ficha.AndroideInfinito = true;
		ServerPlayer absorcao = Corpo("Numero 19", 7);
		absorcao.Race = "Android";
		absorcao.Ficha.AndroideAbsorcao = true;
		TrocarAparencias(infinito);
		TrocarAparencias(absorcao);
		if (NascerNpc("androide_17", pl.Zone, pl.Pos + new Vec2(9 * ZoneCollision.TileSize, 0), 7_600_017) is { } n17) _palcoDosAndroides.Add(n17.Id);
		// (o 17 e chefe: solto, ele bateria no jogador do robo -- que fica intocavel enquanto a foto e tirada)
		pl.Combate.Carencia = CarenciaDeEspera;
		pl.PoderesConcedidos |= Protocol.Poder.Scouter;
		AplicarPoderes(pl);
		return true;
	}

	/// <summary>O defeito injetado dos androides (o jogo de antes: eles aparecem) -- pra a foto do contra-exemplo.</summary>
	internal void PalcoAN_Defeito(bool ligado) => AndroideAparecerDeTeste = ligado;

	/// <summary>Desliga o scouter e da o Sense no nivel 1 -- a aba vira Sense.</summary>
	internal void PalcoAN_Sense(int jogador)
	{
		if (!_players.TryGetValue(jogador, out ServerPlayer? pl)) return;
		pl.PoderesConcedidos &= ~Protocol.Poder.Scouter;
		PalcoSN_Sense(jogador, 1);
	}

	/// <summary>Tira os corpos, o scouter, a skill e o defeito.</summary>
	internal void PalcoAN_Limpar(int jogador)
	{
		AndroideAparecerDeTeste = false;
		foreach (int id in _palcoDosAndroides) if (_players.TryGetValue(id, out ServerPlayer? c)) RemoverNpc(c);
		_palcoDosAndroides.Clear();
		if (_players.TryGetValue(jogador, out ServerPlayer? pl))
		{
			pl.PoderesConcedidos &= ~Protocol.Poder.Scouter;
			if (pl.Livro!.Sabe(StSense)) pl.Livro.Esquecer(StSense);
			AplicarPoderes(pl);
			if (pl.Combate.Carencia >= CarenciaDeEspera) pl.Combate.Carencia = 0;
		}
	}

	// =====================================================================
	// O FREEZA CAIDO EM NAMEK
	// =====================================================================
	private int _palcoDoFreeza;

	/// <summary>
	/// O FREEZA DE NAMEK NA FORMA FINAL, caido a quatro tiles do jogador, com o prazo da Death Ball VENCENDO agora: o monitor da saga roda uma
	/// vez (`TickDasSagas`). Com o conserto ele fica no chao, sem esfera; com o defeito (o jogo de antes), a Death Ball sobe sobre o corpo caido.
	/// Devolve o id dele.
	/// </summary>
	internal int PalcoNM_FreezaCaido(int jogador, bool comODefeito)
	{
		if (!_players.TryGetValue(jogador, out ServerPlayer? pl) || _sagas.Do("freeza_namek") is not { } est) return 0;
		PalcoNM_Limpar();
		ServerPlayer? freeza = NascerNpc("freeza_namek", pl.Zone, pl.Pos + new Vec2(4 * ZoneCollision.TileSize, 0), 7_600_100);
		if (freeza?.Papel == null) return 0;
		_palcoDoFreeza = freeza.Id;
		freeza.Papel.Estagio = freeza.Papel.Molde.Estagios.Length - 1;
		EntrarNoDegrau(freeza, cura: 0, anunciar: false);
		est.Disparou = true;
		est.Fase = FaseDaSaga.EmCurso;
		est.Chefes.Clear();
		est.Chefes.Add(new EstadoDoChefe
		{
			Molde = "freeza_namek", Fase = FaseDoChefe.NoMundo, Corpo = freeza.Id, Degrau = freeza.Papel.Estagio,
			Bps = Sagas.Pinar(freeza.Papel.Molde, MediaDoServidor()), UltimatoEm = _relogioDoMundo - 1,
		});
		freeza.Combate.Nocautear(120);
		EsferaSobeNoChaoDeTeste = comODefeito;
		try { TickDasSagas(); }
		finally { EsferaSobeNoChaoDeTeste = false; }
		// (a foto do contra-exemplo) a tela desenha o NOCAUTE antes da esfera (`Ocupacao.De`): erguida no chao, a Death Ball so aparece quando
		// ele levanta -- e e o que o jogo de antes fazia, ninguem o finalizava ali e ele levantava com ela no ar. O palco o levanta pra camera.
		if (comODefeito && freeza.SupernovaDesde != 0) freeza.Combate.Levantar();
		return freeza.Id;
	}

	/// <summary>O Freeza do palco esta com a esfera no ar? (a ocupacao que a tela desenha)</summary>
	internal bool PalcoNM_ComAEsfera() => _players.TryGetValue(_palcoDoFreeza, out ServerPlayer? f) && f.SupernovaDesde != 0;

	/// <summary>A esfera se desfaz, o Freeza sai e a saga de Namek volta a dormir.</summary>
	internal void PalcoNM_Limpar()
	{
		if (_players.TryGetValue(_palcoDoFreeza, out ServerPlayer? f))
		{
			AcabarASupernova(f);
			RemoverNpc(f);
		}
		_palcoDoFreeza = 0;
		if (_sagas.Do("freeza_namek") is { } est) { est.Chefes.Clear(); est.Fase = FaseDaSaga.Adormecida; est.Disparou = false; }
	}
}
