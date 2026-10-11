using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO DO BILLS (`--diagbills`, no modo host) -- este arquivo nao roda bancada nenhuma: e o que o robo
/// do cliente (`Client/RoboDeBills.cs`) precisa encomendar a AUTORIDADE. A regra inteira e medida pela bancada do
/// servidor (`--billsteste`); o robo mede a TELA, e fotografa.
///
/// O QUE O PALCO FAZ E SO PULAR A ESPERA E A VIAGEM: o mes in-game (a fase vai "a caminho" com a chegada marcada
/// pra hoje -- quem pousa os dois e o passo de calendario de producao), o soco que derruba o Bills (a queda e
/// marcada em nome do host -- o roteiro da derrota, as coordenadas e a partida sao os de producao) e a viagem ate
/// a casa (o pouso de producao, com a maestria de gravidade que aquele mundo pede: a foto nao e a do
/// esmagamento). O resto do roteiro e do jogador: a tecla E, os botoes do menu, a luta.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O MUNDO DE UM PLANETA GERADO, NASCIDO DE VERDADE -- pelas duas funcoes do tique (`MundoDaZona` encomenda,
	/// `TickDasGeracoes` colhe). Nulo se nao ficou pronto no prazo. O prazo e folgado de proposito: a casa do Bills
	/// e o mundo mais pesado da regiao, e mundo pesado e mundo grande (ate mil por mil tiles).
	/// </summary>
	internal ZoneKey? MundoDaCasaProntoDeTeste(PlanetaNoEspaco p, double prazoSegundos = 60)
	{
		ZoneKey z = Espaco.ZonaDe(p);
		var relogio = System.Diagnostics.Stopwatch.StartNew();
		while (relogio.Elapsed.TotalSeconds < prazoSegundos)
		{
			if (MundoDaZona(z, p) != null) return z;
			TickDasGeracoes();
			System.Threading.Thread.Sleep(2);
		}
		return null;
	}

	/// <summary>
	/// CENA 1 -- A VISITA. A historia recomeca (os dois tronos vagos), o mes "passa" e o passo do calendario os pousa
	/// na Terra; o host e posto tres tiles ao sul do Bills. Devolve os ids dos dois corpos.
	/// </summary>
	internal (int Bills, int Whis)? BillsNaTerraDeTeste(int idDoHost)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? pl)) return null;

		ZerarBills();
		_tronos.Remove(BillsEWhis.CargoDoBills);
		_tronos.Remove(BillsEWhis.CargoDoWhis);
		_visita.Fase = FaseDaVisita.ACaminho;
		_visita.DiaDaChegada = DiaDoMundo;
		PassoDaVisitaDeBills();
		if (_bills.Corpo is not { } b || _whis.Corpo is not { } w) return null;

		var aoLado = new Vec2(b.Pos.X + ZoneCollision.TileSize, b.Pos.Y + 3 * ZoneCollision.TileSize);
		if (pl.Zone.Equals(TerraDaVisita)) CravarPosicao(pl, aoLado);
		else MoveToZone(pl.Id, TerraDaVisita, aoLado);
		return (b.Id, w.Id);
	}

	/// <summary>
	/// CENA 2 -- A DERROTA. O Bills cai, e a queda e do host (o `UltimoAgressor`, que e o que o soco e o raio
	/// escrevem). Dali em diante e producao: ele se levanta, fala, conta as coordenadas e os dois vao embora.
	/// </summary>
	internal bool DerrubarOBillsNaTerraDeTeste(int idDoHost)
	{
		if (_visita.Fase != FaseDaVisita.NaTerra || _bills.Corpo is not { } b || !_players.ContainsKey(idDoHost)) return false;
		b.UltimoAgressor = idDoHost;
		b.Combate.Nocautear(BillsEWhis.SegundosNoChao * 4);
		return true;
	}

	/// <summary>Em que pe a historia esta, se ha roteiro falado no ar e quais dos dois estao no mundo. Pro robo saber a hora da foto.</summary>
	internal (FaseDaVisita Fase, bool EmCena, bool Bills, bool Whis) EstadoDoBillsDeTeste() =>
		(_visita.Fase, _cenaDivina, _bills.Corpo != null, _whis.Corpo != null);

	/// <summary>O endereco da casa como o SERVIDOR o sabe (pra o robo comparar com o que chegou pelo fio e com o que a carta enumera).</summary>
	internal (int Sx, int Sy, int K, ulong Semente, double Gravidade, string Falado)? CasaDoBillsDeTeste() =>
		CasaAtual() is { } c ? (c.Sx, c.Sy, c.K, c.Corpo.Seed, c.Gravidade, BillsEWhis.Coordenadas(c)) : null;

	/// <summary>
	/// CENA 3 -- A CASA. O host pousa no mundo deles pelo pouso de producao (orbita, o mundo nascendo fora do tique,
	/// `PousarEmProcedural`), com a maestria de gravidade que o mundo pede. Devolve os ids dos dois corpos, ou nulo
	/// se a historia nao esta "em casa", se o mundo nao nasceu ou se o corpo ficou em orbita.
	/// </summary>
	internal (int Bills, int Whis)? PousarNaCasaDoBillsDeTeste(int idDoHost)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? pl)) return null;
		if (_visita.Fase != FaseDaVisita.EmCasa || CasaAtual() is not { } casa) return null;
		if (MundoDaCasaProntoDeTeste(casa.Corpo) == null) return null;

		pl.Ficha.GravMastered = Math.Max(pl.Ficha.GravMastered, casa.Gravidade + 20);
		if (!PousarDeVerdade(pl, casa.Corpo)) return null;

		GarantirOsDivinos();
		return _bills.Corpo is { } b && _whis.Corpo is { } w ? (b.Id, w.Id) : null;
	}

	/// <summary>
	/// Poe o host a tantos tiles de um dos dois, pelo funil de "o servidor moveu voce". O lado e escolhido: ao sul
	/// dele, senao do lado de fora do par, senao ao norte (ali os dois caem embaixo do chat, na foto) -- o primeiro
	/// que for CHAO FIRME e em que ELE seja o mais perto dos dois (a tecla E oferece o mais perto, e a foto e a do
	/// menu de quem se pediu). Falso se nenhum serve.
	/// </summary>
	internal bool AoLadoDoDivinoDeTeste(int idDoHost, bool whis, float tiles = 1.25f)
	{
		CorpoDivino c = whis ? _whis : _bills, outro = whis ? _bills : _whis;
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? pl) || c.Corpo is not { } npc) return false;
		if (MapaDaZonaOuCatalogo(npc.Zone) is not { } mapa) return false;

		float d = tiles * ZoneCollision.TileSize;
		float fora = outro.Corpo is { } o && o.Pos.X > npc.Pos.X ? -d : d;
		foreach (Vec2 passo in (Vec2[])[new(0, d), new(fora, 0), new(fora, d), new(0, -d)])
		{
			Vec2 onde = npc.Pos + passo;
			if (!mapa.ServeDeChao((int)MathF.Floor(onde.X / ZoneCollision.TileSize), (int)MathF.Floor(onde.Y / ZoneCollision.TileSize))) continue;
			if (outro.Corpo is { } vizinho && (vizinho.Pos - onde).LengthSquared <= (npc.Pos - onde).LengthSquared) continue;
			CravarPosicao(pl, onde);
			return true;
		}
		return false;
	}

	/// <summary>O ki divino desperto no host: e o que o desafio pelo titulo cobra de quem desafia.</summary>
	internal void DespertarOHostDeTeste(int idDoHost)
	{
		if (_players.TryGetValue(idDoHost, out ServerPlayer? pl))
			pl.Ficha.godki = new GodKiState { awakened = true, mastery = 100 };
	}

	/// <summary>O host inteiro de novo (o `trn_heal` do torneio): o adversario tem o triplo do poder dele, e a foto precisa dos dois de pe.</summary>
	internal void ManterOHostDePeDeTeste(int idDoHost)
	{
		if (_players.TryGetValue(idDoHost, out ServerPlayer? pl) && !pl.Ficha.dead) CurarPraLuta(pl);
	}

	/// <summary>
	/// O retrato de um dos dois NO SERVIDOR: o modo, a luta em que esta (e com quem), a forma, e se a passiva da
	/// disciplina dele esta ligada. O robo compara com o que a TELA mostra.
	/// </summary>
	internal (string Modo, int Parceiro, bool Desafio, bool Lutando, string Forma, bool Disciplina, bool Consagrado)? DivinoDeTeste(bool whis)
	{
		CorpoDivino c = whis ? _whis : _bills;
		if (c.Corpo is not { } npc) return null;
		return (c.Modo.ToString(), c.Sessao?.Parceiro.Id ?? 0, c.Sessao?.Desafio == true, c.Sessao?.Lutando == true,
				npc.Forma.Atual, EstadoDe(npc, DisciplinaDoDivino(c)).Ligada, EhDeusDaDestruicao(npc));
	}

	/// <summary>A historia volta a dormir e os dois saem do mundo: o robo deixa o palco como achou.</summary>
	internal void RecolherOPalcoDoBillsDeTeste()
	{
		ZerarBills();
		SalvarBills();
	}
}
