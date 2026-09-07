using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO ESTRAGO QUE CHEGA DE UMA VEZ (`--diagcenario`).
///
/// A queixa do dono (2026-09-06): *"ao voltar/entrar em um local que teve o cenario destruido, o jogo
/// carrega o cenario normal e depois destroi o que deve estar destruido... roda a animacao de
/// destruicao pra todos os tiles destruidos ao mesmo tempo, e isso faz o loading aumentar
/// consideravelmente"*.
///
/// Roda no MESMO processo do servidor (`--host --cenarioteste --quebrarteste N --diagcenario`):
///   1) no nascimento N celulas caem AO VIVO em volta do corpo -- essas soltam poeira, e devem;
///   2) o servidor leva o corpo a Namek (retrato vazio, daquela zona) e o traz de volta;
///   3) NA VOLTA o estrago da Terra chega como UM retrato pra um chao que vem do cache: nenhuma
///      poeira nova, toda celula caida e terra batida no tilemap e chao aberto na colisao, a
///      terra revirada foi plantada uma vez por celula (o numero bate com a conta do desenho), e
///      reaplicar o estrago -- o que cada pedaco pintado faz -- nao replanta nada nem solta poeira;
///      um pedaco que nao contem celula caida nao toca em nenhuma.
/// </summary>
public partial class RoboDeCenario : Node
{
	private int _fase, _ok, _falhou;
	private double _t;
	private int _poeiraAntes;
	private ulong _terra;

	private void Conferir(bool cond, string nome, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[cenario]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[cenario]   FALHA {nome}   [{detalhe}]"); }
	}

	private void Encerrar()
	{
		GD.Print($"[cenario] ============ {_ok} OK, {_falhou} FALHA(S) ============");
		_fase = 99;
	}

	public override void _Process(double delta)
	{
		_t += delta;
		if (_fase == 99 || GameClient.Instance is not { } cli || World.Instancia is not { } w) return;

		switch (_fase)
		{
			case 0:
			{
				// A TERRA MONTADA E O ESTRAGO DO NASCIMENTO (`--quebrarteste`) JA NO CHAO.
				if (w.PosicaoLocal == null || w.ZonaMontadaDeTeste == 0) return;
				if (cli.CenarioCaido.Count < 10)
				{
					if (_t > 25) { Conferir(false, "o servidor derrubou celulas no nascimento (rode com --quebrarteste 40)"); Encerrar(); }
					return;
				}
				if (_t < 4) return;   // deixa a poeira das quedas ao vivo sair
				_terra = w.ZonaMontadaDeTeste;
				GD.Print("[cenario] -- 1) o nascimento: o estrago cai AO VIVO, com poeira --");
				Conferir(cli.RetratosDeCenarioDeTeste >= 1, "o retrato da zona chegou no login", $"{cli.RetratosDeCenarioDeTeste} retrato(s)");
				Conferir(cli.CenarioDaZona == _terra, "...e e desta zona");
				Conferir(PoeiraDeEstrago.PedidosDeTeste > 0,
					"as quedas AO VIVO do nascimento soltaram poeira (essas sao acontecimento, e devem)", $"{PoeiraDeEstrago.PedidosDeTeste} poeira(s)");
				ConferirChao(cli, w, "no nascimento");
				_poeiraAntes = PoeiraDeEstrago.PedidosDeTeste;
				_fase = 1; _t = 0;
				break;
			}
			case 1:
			{
				// A IDA: o servidor leva o corpo a Namek.
				if (w.ZonaMontadaDeTeste != 0 && w.ZonaMontadaDeTeste != _terra)
				{
					GD.Print("[cenario] -- 2) a ida: outra zona, retrato vazio --");
					Conferir(true, "o servidor levou o corpo pra outra zona");
					Conferir(cli.CenarioDaZona == w.ZonaMontadaDeTeste && cli.CenarioCaido.Count == 0,
						"la o retrato e VAZIO e e daquela zona (o nada tambem viaja, com endereco)", $"{cli.CenarioCaido.Count} celulas");
					Conferir(PoeiraDeEstrago.PedidosDeTeste == _poeiraAntes, "e nenhuma poeira saiu na ida", $"{_poeiraAntes} -> {PoeiraDeEstrago.PedidosDeTeste}");
					_fase = 2; _t = 0;
				}
				else if (_t > 25) { Conferir(false, "a ida nao aconteceu em 25 s (rode com --cenarioteste)"); Encerrar(); }
				break;
			}
			case 2:
			{
				if (w.ZonaMontadaDeTeste == _terra) { _fase = 3; _t = 0; }
				else if (_t > 25) { Conferir(false, "a volta nao aconteceu em 25 s"); Encerrar(); }
				break;
			}
			case 3:
			{
				// A VOLTA: dois segundos pro chao assentar (pedacos), e mede.
				if (_t < 2) return;
				GD.Print("[cenario] -- 3) a volta: o retrato entra de uma vez, sem poeira --");
				Conferir(cli.RetratosDeCenarioDeTeste >= 3, "tres retratos ate aqui: login, ida e volta", $"{cli.RetratosDeCenarioDeTeste}");
				Conferir(cli.CenarioDaZona == _terra && cli.CenarioCaido.Count >= 10,
					"o retrato da volta e da Terra e traz o estrago inteiro", $"{cli.CenarioCaido.Count} celulas");
				Conferir(PoeiraDeEstrago.PedidosDeTeste == _poeiraAntes,
					"NENHUMA poeira na volta: estrago velho e estado, nao acontecimento", $"{_poeiraAntes} -> {PoeiraDeEstrago.PedidosDeTeste}");
				ConferirChao(cli, w, "na volta");

				int esperado = EsperadoDeTerraRevirada(cli, w);
				Conferir(Decalques.PermanentesDeTeste == esperado,
					"a terra revirada foi plantada UMA vez por celula caida (o numero bate com a conta do desenho)",
					$"{Decalques.PermanentesDeTeste} decalque(s), esperava {esperado}");
				Conferir(w.TerraReviradaDeTeste == cli.CenarioCaido.Count,
					"...e o World lembra de todas as celulas caidas", $"{w.TerraReviradaDeTeste} de {cli.CenarioCaido.Count}");

				// REAPLICAR NAO DUPLICA: o caminho do pedaco pintado, duas vezes seguidas.
				GD.Print("[cenario] -- 4) reaplicar (o que cada pedaco pintado faz) nao duplica nada --");
				int pedidos = Decalques.PedidosDeTeste, permanentes = Decalques.PermanentesDeTeste, poeira = PoeiraDeEstrago.PedidosDeTeste;
				w.ReaplicarEstragoDeTeste();
				w.ReaplicarEstragoDeTeste();
				Conferir(Decalques.PedidosDeTeste == pedidos && Decalques.PermanentesDeTeste == permanentes,
					"reaplicar o estrago duas vezes NAO replanta a terra revirada", $"{permanentes} -> {Decalques.PermanentesDeTeste}");
				Conferir(PoeiraDeEstrago.PedidosDeTeste == poeira, "...nem solta poeira", $"{poeira} -> {PoeiraDeEstrago.PedidosDeTeste}");
				Conferir(w.UltimaReaplicacaoDeTeste == cli.CenarioCaido.Count,
					"...e cada reaplicacao inteira toca todas as celulas caidas", $"{w.UltimaReaplicacaoDeTeste}");
				ConferirChao(cli, w, "depois de reaplicar");

				// POR PEDACO: um retangulo sem celula caida nao toca nenhuma; o que contem todas toca todas.
				w.ReaplicarEstragoNoPedacoDeTeste(new Rect2I(0, 0, 1, 1));
				Conferir(w.UltimaReaplicacaoDeTeste == 0, "um pedaco que nao contem celula caida nao reaplica nenhuma", $"{w.UltimaReaplicacaoDeTeste}");
				(Vector2I min, Vector2I max) = Caixa(cli);
				w.ReaplicarEstragoNoPedacoDeTeste(new Rect2I(min, max - min + Vector2I.One));
				Conferir(w.UltimaReaplicacaoDeTeste == cli.CenarioCaido.Count,
					"o pedaco que contem todas as celulas caidas reaplica todas", $"{w.UltimaReaplicacaoDeTeste} de {cli.CenarioCaido.Count}");
				Encerrar();
				break;
			}
		}
	}

	/// <summary>Toda celula caida e terra batida no tilemap e chao aberto na colisao do cliente.</summary>
	private void ConferirChao(GameClient cli, World w, string quando)
	{
		(int Fonte, Vector2I Coord)? g = w.ChaoDestruidoDeTeste;
		TileMapLayer[] camadas = w.CamadasDoCenarioDeTeste;
		int chao = 0, livres = 0;
		foreach ((int cx, int cy) in cli.CenarioCaido)
		{
			var cel = new Vector2I(cx, cy);
			if (camadas.Length > 0 && g is { } t && GodotObject.IsInstanceValid(camadas[0])
				&& camadas[0].GetCellSourceId(cel) == t.Fonte && camadas[0].GetCellAtlasCoords(cel) == t.Coord) chao++;
			if (w.Colisao is { } m && !m.BlockedCell(cx, cy)) livres++;
		}
		Conferir(chao == cli.CenarioCaido.Count, $"{quando}: toda celula caida mostra terra batida (Ground8) no tilemap", $"{chao} de {cli.CenarioCaido.Count}");
		Conferir(livres == cli.CenarioCaido.Count, $"{quando}: toda celula caida esta ABERTA na colisao do cliente", $"{livres} de {cli.CenarioCaido.Count}");
	}

	/// <summary>
	/// QUANTOS DECALQUES DE TERRA REVIRADA O DESENHO PLANTA, pela MESMA regra dele: para cada celula
	/// caida, na ordem da lista, os 8 vizinhos dentro do mapa que nao bloqueiam NAQUELE momento e
	/// que o embaralhador sorteia. "Naquele momento" porque a colisao e reaberta celula a celula na
	/// ordem da lista (a entrada na zona fecha tudo antes): um vizinho que e parede de ARQUIVO so
	/// esta aberto se veio antes na lista; um vizinho que ja era chao nunca bloqueou -- e `Aberta`
	/// nao distingue os dois, por isso o bit cru (`BloqueadaNoArquivo`).
	///
	/// Uma segunda implementacao da conta, de proposito: se as duas discordarem, uma delas esta
	/// errada, e e assim que se descobre.
	/// </summary>
	private static int EsperadoDeTerraRevirada(GameClient cli, World w)
	{
		if (w.Colisao is not { } m) return -1;
		var caidas = cli.CenarioCaido.ToHashSet();
		var abertas = new HashSet<(int, int)>();
		int n = 0;
		foreach ((int cx, int cy) in cli.CenarioCaido)
		{
			for (int dx = -1; dx <= 1; dx++)
				for (int dy = -1; dy <= 1; dy++)
				{
					if (dx == 0 && dy == 0) continue;
					int x = cx + dx, y = cy + dy;
					if (x < 0 || y < 0 || x >= m.Width || y >= m.Height) continue;
					bool bloqueava = caidas.Contains((x, y))
						? (m.BloqueadaNoArquivo(x, y) ? !abertas.Contains((x, y)) : m.BlockedCell(x, y))
						: m.BlockedCell(x, y);
					if (bloqueava) continue;
					if (!World.PintariaTerraReviradaDeTeste(x, y)) continue;
					n++;
				}
			abertas.Add((cx, cy));
		}
		return n;
	}

	private static (Vector2I Min, Vector2I Max) Caixa(GameClient cli)
	{
		var min = new Vector2I(int.MaxValue, int.MaxValue);
		var max = new Vector2I(int.MinValue, int.MinValue);
		foreach ((int cx, int cy) in cli.CenarioCaido)
		{
			min = new Vector2I(Math.Min(min.X, cx), Math.Min(min.Y, cy));
			max = new Vector2I(Math.Max(max.X, cx), Math.Max(max.Y, cy));
		}
		return (min, max);
	}
}
