using System.Collections;
using Godot;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DAS FOTOS DOS PLANETAS (`--fotoplanetas`) -- o lado do JOGO da repintura dos mapas pre-feitos.
///
/// O dono (2026-10-09): *"os problemas de sprite q vc encontrou no planeta vegeta tb acontecem nos outros
/// planetas pre feitos, cheque eles e resolva tb"*. Quem cobra o `.pedacos` celula a celula contra o DM e o
/// pipeline (`repintar` + `censo` + `cidade` + o pintor pixel a pixel). Este robo confere a ponta que o
/// pipeline nao alcanca: leva o corpo ate cada lugar consertado, ve se a zona CARREGA e PINTA com o tileset que
/// o jogo tem, e fotografa -- o penhasco da Terra, o mar de Namek, a costa e os pinheiros de Icer.
///
/// As paradas de fabrica sao as da evidencia do conversor. `--fotoplanetasarg "Zona,cx,cy,nome;Zona,cx,cy,nome"`
/// troca a lista sem recompilar (o corpo vai pra celula dada; a foto sai como `planeta-NN-nome.png`).
///
/// Rode com janela (no segundo monitor), no modo host:
///   --host --rede 7987 --horateste 0.5 --fotoplanetas --raca Human --conta bancada_planetas --nome Viajante
/// </summary>
public partial class RoboDeFotoDosPlanetas : Node
{
	private const int T = ZoneCollision.TileSize;

	private static readonly (string Zona, int Cx, int Cy, string Nome)[] DeFabrica =
	[
		("Earth", 291, 97, "terra-o-penhasco"),
		("Namek", 122, 12, "namek-o-mar"),
		("Namek", 152, 214, "namek-as-casas"),
		("Icer", 434, 372, "icer-a-costa"),
		("Icer", 143, 275, "icer-os-pinheiros"),
	];

	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[fotoplanetas]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[fotoplanetas]   FALHA {nome}   [{detalhe}]"); }
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
			GD.Print($"\n[fotoplanetas] ===== {_ok} OK, {_falhou} FALHA(S) =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private bool Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { GD.Print("[fotoplanetas] sem foto (headless nao renderiza): rode com janela"); return false; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		GD.Print($"[fotoplanetas] foto {caminho}");
		return true;
	}

	/// <summary>Quantas celulas do quadrado de lado `2r+1` em volta desta tem tile pintado nesta camada.</summary>
	private static int PintadasEmVolta(string camada, int cx, int cy, int r)
	{
		int n = 0;
		foreach (TileMapLayer l in World.Instancia!.CamadasDoCenarioDeTeste)
		{
			if (l.Name != camada) continue;
			for (int y = cy - r; y <= cy + r; y++)
				for (int x = cx - r; x <= cx + r; x++)
					if (l.GetCellSourceId(new Vector2I(x, y)) >= 0) n++;
		}
		return n;
	}

	private static List<(string Zona, int Cx, int Cy, string Nome)> Paradas()
	{
		string[] args = OS.GetCmdlineArgs();
		int i = Array.IndexOf(args, "--fotoplanetasarg");
		if (i < 0 || i + 1 >= args.Length) return [.. DeFabrica];

		var lista = new List<(string, int, int, string)>();
		foreach (string parada in args[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string[] p = parada.Split(',');
			if (p.Length == 4 && int.TryParse(p[1], out int cx) && int.TryParse(p[2], out int cy))
				lista.Add((p[0].Trim(), cx, cy, p[3].Trim()));
		}
		return lista;
	}

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }
		Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);

		int n = 0;
		foreach ((string zona, int cx, int cy, string nome) in Paradas())
		{
			n++;
			srv.MoveToZone(cli.LocalId, ZoneKey.Premade(zona), No(cx, cy));
			yield return 6.0;
			Afirmar($"{zona}: o corpo chegou e a zona carregou", cli.Zone.Name == zona, cli.Zone.Name);
			srv.EmbateDeFoto_CeuLimpo(cli.LocalId);

			// DIA NESTE PLANETA. O `--horateste` crava a hora da TERRA, e cada planeta tem a rotacao e a defasagem
			// dele (`Ceu.DefasagemDoNome`): a manivela da Terra e girada ate o relogio DAQUI dar dia.
			for (int i = 0; i < 20; i++)
			{
				double daqui = World.Instancia?.Ceu is { } ceu ? ceu.Hora - Math.Floor(ceu.Hora) : -1;
				if (daqui is >= 0.42 and <= 0.6) break;
				srv.CeuDaTerraDeTeste(i / 20.0);
				yield return 0.35;
			}
			yield return 1.0;

			int chao = PintadasEmVolta("Chao", cx, cy, 6);
			Afirmar($"{zona} ({cx},{cy}): o chao em volta esta PINTADO (169 celulas no quadrado de 13x13)",
					chao == 169, $"{chao} de 169");
			if (Fotografar($"planeta-{n:00}-{nome}")) yield return 0.3;
		}
	}
}
