using System.Collections;
using Godot;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DAS FOTOS DE VEGETA (`--fotovegeta`) -- o lado do JOGO do conserto dos "icones cortados".
///
/// O dono (2026-10-09): *"no planeta vegeta tem varios sprites q estao cortados ou incompletos, principalmente
/// na cidade do planeta vegeta onde tem as casas"*. O conserto foi no conversor de mapas, e quem cobra o
/// `.pedacos` e a bancada do pipeline (`cidade`, familia 7). Este robo confere a outra ponta: leva o corpo ate
/// la, le o tile que o JOGO pintou em cada peca consertada e fotografa -- a janela inteira, a mesa redonda, o
/// computador, a aresta do lago, o mosaico da agua e o balcao do banco do castelo.
///
/// Rode com janela (no segundo monitor), no modo host:
///   --host --rede 7983 --horateste 0.5 --fotovegeta --raca Saiyan --conta bancada_vegeta --nome Fotografo
/// </summary>
public partial class RoboDeFotoDeVegeta : Node
{
	private const int T = ZoneCollision.TileSize;

	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[fotovegeta]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[fotovegeta]   FALHA {nome}   [{detalhe}]"); }
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } || World.Instancia?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext())
		{
			_acabou = true;
			GD.Print($"\n[fotovegeta] ===== {_ok} OK, {_falhou} FALHA(S) =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { GD.Print("[fotovegeta] sem foto (headless nao renderiza): rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		GD.Print($"[fotovegeta] foto {caminho}");
	}

	/// <summary>O tile pintado numa celula, na camada de nome dado ("Chao", "Decor", "Objetos"): fonte e coordenada no atlas.</summary>
	private static (int Fonte, int X, int Y)? Pintado(string camada, int cx, int cy)
	{
		foreach (TileMapLayer l in World.Instancia!.CamadasDoCenarioDeTeste)
		{
			if (l.Name != camada) continue;
			var c = new Vector2I(cx, cy);
			int fonte = l.GetCellSourceId(c);
			if (fonte < 0) return null;
			Vector2I a = l.GetCellAtlasCoords(c);
			return (fonte, a.X, a.Y);
		}
		return null;
	}

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }

		const string indice = "res://Assets/Data/tiles.json";
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(Godot.FileAccess.FileExists(indice) ? Godot.FileAccess.GetFileAsString(indice) : "");
		ZoneKey vegeta = ZoneKey.Premade("Vegeta");
		Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);

		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);

		// ------------------------------------------------------------ a casa 1, pelo norte: as janelas
		srv.MoveToZone(cli.LocalId, vegeta, No(82, 152));
		yield return 6.0;
		Afirmar("(montagem) o corpo esta em Vegeta", cli.Zone.Name == "Vegeta", cli.Zone.Name);

		// MEIO-DIA EM VEGETA. O `--horateste` crava a hora da TERRA, e cada planeta tem a rotacao e a
		// defasagem dele (`Ceu.DefasagemDoNome`): a manivela da Terra e girada ate o relogio DAQUI dar dia.
		for (int i = 0; i < 20; i++)
		{
			double daqui = World.Instancia?.Ceu is { } ceu ? ceu.Hora - Math.Floor(ceu.Hora) : -1;
			if (daqui is >= 0.47 and <= 0.6) break;
			srv.CeuDaTerraDeTeste(i / 20.0);
			yield return 0.35;
		}
		Afirmar("(montagem) e dia em Vegeta, pra foto", World.Instancia?.Ceu is { } c2 && !Ceu.EhNoite(c2.Hora - Math.Floor(c2.Hora)),
				World.Instancia?.Ceu is { } c3 ? Ceu.NomeDaHora(c3.Hora) : "sem ceu");
		yield return 0.6;
		(int Fonte, int X, int Y)? inteira = tiles.Achar("castle_wall", "window"), meia = tiles.Achar("castle_wall", "window top");
		Afirmar("(montagem) o indice de tiles tem a janela inteira e a meia-janela, em quadros diferentes",
				inteira != null && meia != null && inteira != meia, $"{inteira} / {meia}");
		int inteiras = 0, meias = 0;
		foreach ((int x, int y) in new[] { (80, 154), (84, 154), (77, 158), (87, 158) })
		{
			if (Pintado("Chao", x, y) == inteira) inteiras++;
			if (Pintado("Chao", x, y) == meia) meias++;
		}
		Afirmar("as quatro janelas da casa 1 sao pintadas INTEIRAS no jogo (e nenhuma com o arco solto)", inteiras == 4 && meias == 0,
				$"{inteiras} inteiras, {meias} meias");
		// A CAMA ENCOSTADA NA PAREDE DE CIMA (78,155) tem 32x64: de fora, a metade de cima dela aparecia por
		// cima da fachada. O tile alto de um interior que nao se ve e apagado, como a maquina e o corpo.
		World mundo = World.Instancia!;
		Afirmar("(montagem) a casa tem tiles altos sob teto anotados (camas e estantes)", mundo.AltosSobTetoDeTeste >= 4, $"{mundo.AltosSobTetoDeTeste}");
		Afirmar("de fora, a cama colada na parede NAO e pintada (o tile sai enquanto o interior e breu)",
				mundo.AltoEscondidoDeTeste(78, 155) && Pintado("Objetos", 78, 155) == null);
		Fotografar("vegeta-01-a-casa-pelo-norte");

		World.AltoNoBreuFicaDeTeste = true;
		yield return 0.6;
		Afirmar("(defeito injetado: o tile alto no breu continua pintado) a cama aparece por cima da parede, vista de fora",
				Pintado("Objetos", 78, 155) != null);
		Fotografar("vegeta-01b-defeito-a-cama-por-cima-da-parede");
		World.AltoNoBreuFicaDeTeste = false;
		yield return 0.4;

		// ------------------------------------------------------------ dentro da casa 1: a mesa redonda
		srv.MoveToZone(cli.LocalId, vegeta, No(82, 158));
		yield return 5.0;
		(int Fonte, int X, int Y)? mesa = tiles.Achar("!!!  house furniture", "round table");
		Afirmar("as duas mesas redondas da casa 1 estao pintadas na camada de objetos",
				mesa != null && Pintado("Objetos", 84, 155) == mesa && Pintado("Objetos", 84, 156) == mesa,
				$"{Pintado("Objetos", 84, 155)} / esperado {mesa}");
		Afirmar("...e a mesa barra o corpo, no cliente", World.Instancia!.Colisao?.BlockedCell(84, 155) == true);
		Afirmar("de dentro, a cama esta pintada de novo", !mundo.AltoEscondidoDeTeste(78, 155) && Pintado("Objetos", 78, 155) != null);
		Fotografar("vegeta-02-a-casa-por-dentro");

		// ------------------------------------------------------------ o laboratorio 1: os computadores
		srv.MoveToZone(cli.LocalId, vegeta, No(65, 164));
		yield return 5.0;
		(int Fonte, int X, int Y)? pedaco = tiles.Achar("Lab", "computer2");
		(int Fonte, int X, int Y)? pc = Pintado("Objetos", 64, 159);
		Afirmar("o computador do laboratorio nao e mais o pedaco de outra maquina (`computer2`), e a folha e a mesma",
				pc != null && pedaco != null && pc != pedaco && pc.Value.Fonte == pedaco.Value.Fonte, $"pintado {pc}, o pedaco era {pedaco}");
		Fotografar("vegeta-03-o-laboratorio");

		// ------------------------------------------------------------ o lago ao sul, a agua e o banco do castelo
		srv.MoveToZone(cli.LocalId, vegeta, No(52, 201));
		yield return 5.0;
		Fotografar("vegeta-04-a-margem-do-lago");

		srv.MoveToZone(cli.LocalId, vegeta, No(117, 177));
		yield return 5.0;
		Fotografar("vegeta-05-a-agua-ao-lado-da-cidade");

		srv.MoveToZone(cli.LocalId, vegeta, No(121, 217));
		yield return 5.0;
		Afirmar("o balcao do banco do castelo esta pintado por cima do piso (camada Decor)", Pintado("Decor", 120, 215) != null,
				$"{Pintado("Decor", 120, 215)}");
		Fotografar("vegeta-06-o-banco-do-castelo");
	}
}
