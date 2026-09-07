using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO ESTRAGO QUE CHEGA DE UMA VEZ (`--cenarioteste`).
///
/// A queixa do dono (2026-09-06): *"ao voltar/entrar em um local que teve o cenario destruido, o jogo
/// carrega o cenario normal e depois destroi o que deve estar destruido pra sincronizar com o
/// server; o problema e que o jogo roda a animacao de destruicao pra todos os tiles destruidos ao
/// mesmo tempo, e isso faz o loading, dependendo do nivel de destruicao, aumentar consideravelmente"*.
///
/// A metade do SERVIDOR e medida aqui, no fio: quem chega recebe UM pacote (o retrato, com a zona)
/// em vez de um por celula; a queda ao vivo e a limpeza continuam sendo os pacotes que eram. A metade
/// do CLIENTE -- aplicar sem poeira, por pedaco, sem replantar terra revirada -- e o robo
/// `--diagcenario` (`Client/RoboDeCenario.cs`), que precisa de um corpo de verdade viajando: com a
/// chave, quem loga vai a Namek e volta sozinho (<see cref="AgendarViagemDoCenario"/>).
/// </summary>
public sealed partial class GameServer
{
	private int _cenrOk, _cenrFalhou;

	/// <summary>`--cenarioteste` ligado: quem loga faz a viagem de ida e volta pro robo medir.</summary>
	private bool _cenarioDeTeste;

	/// <summary>
	/// Tudo que sai pelo `S2C.Cenario` enquanto uma bancada escuta: pra quem (o id; 0 = a zona ou
	/// todos) e o fio. Nulo fora da bancada -- e o mesmo molde do `EscutaDeRetratosDePecas`.
	/// </summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDeCenario;

	private void AfirmarCenario(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _cenrOk++; GD.Print($"[cenario]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _cenrFalhou++; GD.PrintErr($"[cenario]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoCenario()
	{
		_cenrOk = _cenrFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[cenario] ================ O ESTRAGO QUE CHEGA DE UMA VEZ (pedido do dono, 2026-09-06) ================");
		try
		{
			ServerPlayer pl = Forjar("cen: quem chega", CorredorLivre(8), 5_000);
			string zona = pl.Zone.Name;

			// O ESTRAGO, pelo caminho de producao: o chao racha em volta do corpo, raio 3, sem sorteio.
			int antes = Caidas(zona);
			int rachadas = RacharChao(pl.Zone, pl.Pos, 1_000_000, raio: 3, chance: 1.0);
			int agora = Caidas(zona);
			AfirmarCenario("a bancada derrubou celulas na Terra pelo caminho de producao (`RacharChao`)",
					   agora > antes, $"{antes} -> {agora} ({rachadas} rachadas)");

			// 1) O RETRATO: um pacote so, com tudo.
			EscutaDeCenario = [];
			MandarCenario(pl);
			List<(int Para, byte[] Fio)> pacotes = EscutaDeCenario.Where(r => r.Para == pl.Id).ToList();
			EscutaDeCenario = null;
			AfirmarCenario($"quem chega recebe UM pacote com o estrago inteiro da zona (antes eram {agora}, um por celula)",
					   pacotes.Count == 1, $"{pacotes.Count} pacote(s)");
			if (pacotes.Count == 1)
			{
				(byte modo, ulong z, List<(int X, int Y)> cel) = LerPacoteDeCenario(pacotes[0].Fio);
				AfirmarCenario("...no modo RETRATO, e com a zona dentro", modo == Protocol.CenarioRetrato && z == pl.Zone.Hash, $"modo {modo}");
				AfirmarCenario("...com TODAS as celulas caidas da zona, sem repetir nem faltar",
						   cel.Count == agora && cel.ToHashSet().SetEquals(_cenarioCaido[zona]), $"{cel.Count} de {agora}");
				AfirmarCenario("...em 4 bytes por celula mais 14 de cabecalho (opcode, modo, zona, contagem)",
						   pacotes[0].Fio.Length == 14 + 4 * agora, $"{pacotes[0].Fio.Length} bytes pra {agora} celulas");
			}

			// 2) ZONA SEM ESTRAGO: o retrato sai mesmo assim, vazio, e diz de que zona e o nada.
			ZoneKey namek = ZoneKey.Premade("Namek");
			(byte m2, ulong z2, List<(int X, int Y)> c2) = LerPacoteDeCenario(PacoteDeRetratoDeCenario(namek).CopyData());
			AfirmarCenario("zona sem estrago: o retrato sai VAZIO, mas sai, e com a zona (o cliente precisa saber de que chao e o nada)",
					   m2 == Protocol.CenarioRetrato && z2 == namek.Hash && c2.Count == 0, $"{c2.Count} celulas");

			// 3) A QUEDA AO VIVO continua sendo um pacote de celula (esse o cliente anima), e a limpeza um de limpar.
			EscutaDeCenario = [];
			MandarCelulaCaida(pl.Zone, 7, 9);
			MandarLimpezaDeCenario(pl.Zone);
			List<(int Para, byte[] Fio)> vivos = EscutaDeCenario;
			EscutaDeCenario = null;
			AfirmarCenario("a queda ao vivo e a limpeza saem como dois pacotes proprios", vivos.Count == 2, $"{vivos.Count}");
			if (vivos.Count == 2)
			{
				(byte mc, ulong _, List<(int X, int Y)> cc) = LerPacoteDeCenario(vivos[0].Fio);
				(byte ml, ulong zl, _) = LerPacoteDeCenario(vivos[1].Fio);
				AfirmarCenario("a celula que cai AGORA vai no modo CELULA, com a coordenada (e so ela)",
						   mc == Protocol.CenarioCelula && cc.Count == 1 && cc[0] == (7, 9) && vivos[0].Fio.Length == 6, $"{vivos[0].Fio.Length} bytes");
				AfirmarCenario("a limpeza do admin vai no modo LIMPAR, com a zona", ml == Protocol.CenarioLimpar && zl == pl.Zone.Hash);
			}

			// 4) O LEITOR E O MESMO DOS TRES: um pacote de modo desconhecido nao e confundido com nenhum.
			NetDataWriter torto = Protocol.Begin(Protocol.S2C.Cenario);
			torto.Put((byte)9);
			(byte m9, ulong _, List<(int X, int Y)> c9) = LerPacoteDeCenario(torto.CopyData());
			AfirmarCenario("contra-exemplo: um modo desconhecido nao vira retrato nem celula", m9 == 9 && c9.Count == 0);
		}
		finally { EscutaDeCenario = null; LimparTudoDaBancada(); }
		GD.Print($"[cenario] ================ {_cenrOk} OK, {_cenrFalhou} FALHA(S) ================");
	}

	private int Caidas(string zona) =>
		_cenarioCaido.TryGetValue(zona, out HashSet<(int X, int Y)>? c) ? c.Count : 0;

	/// <summary>Le um pacote `S2C.Cenario` como o cliente le: opcode, modo, e o corpo de cada modo.</summary>
	private static (byte Modo, ulong Zona, List<(int X, int Y)> Celulas) LerPacoteDeCenario(byte[] fio)
	{
		var r = new NetDataReader(fio);
		r.GetByte();   // o opcode
		byte modo = r.GetByte();
		ulong zona = 0;
		var celulas = new List<(int X, int Y)>();
		if (modo == Protocol.CenarioRetrato)
		{
			zona = r.GetULong();
			int n = r.GetInt();
			for (int i = 0; i < n; i++) celulas.Add((r.GetUShort(), r.GetUShort()));
		}
		else if (modo == Protocol.CenarioLimpar) zona = r.GetULong();
		else if (modo == Protocol.CenarioCelula) celulas.Add((r.GetUShort(), r.GetUShort()));
		return (modo, zona, celulas);
	}

	/// <summary>
	/// A VIAGEM DE IDA E VOLTA de quem loga com `--cenarioteste`: seis segundos depois de entrar o corpo
	/// vai pra Namek, e seis segundos depois volta pro mesmo ponto da Terra. O robo `--diagcenario` mede
	/// a volta: e nela que o estrago da Terra chega como retrato pra um chao que vem do cache.
	/// </summary>
	private void AgendarViagemDoCenario(ServerPlayer pl)
	{
		int id = pl.Id;
		SceneTreeTimer ida = GetTree().CreateTimer(6.0);
		ida.Timeout += () =>
		{
			if (!_players.TryGetValue(id, out ServerPlayer? p) || p.Zone.Name != "Earth") return;
			Vec2 volta = p.Pos;
			ZoneKey namek = ZoneKey.Premade("Namek");
			Vec2 chegada = _catalogo?.Get(namek)?.Mapa?.PontoLivrePerto(new Vec2(249 * ZoneCollision.TileSize + 16, 250 * ZoneCollision.TileSize + 16))
						   ?? new Vec2(8000, 8000);
			GD.Print("[cenario] BANCADA: a viagem de ida, pra Namek");
			MoveToZone(id, namek, chegada);

			SceneTreeTimer retorno = GetTree().CreateTimer(6.0);
			retorno.Timeout += () =>
			{
				if (!_players.ContainsKey(id)) return;
				GD.Print("[cenario] BANCADA: a viagem de volta, pra Terra");
				MoveToZone(id, ZoneKey.Premade("Earth"), volta);
			};
		};
	}
}
