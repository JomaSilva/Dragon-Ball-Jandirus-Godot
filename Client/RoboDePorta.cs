using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DE PORTA, ao vivo (`--porta`, junto do `--portateste` no servidor).
///
/// ============================ POR QUE ELA PRECISA EXISTIR ============================
/// O banco de prova do pipeline (`AssetPipeline portas`) confere a REGRA: o `.portas` saiu, a
/// celula bloqueia fechada, o `VaiEntrar` acerta o alcance, o `Abrir` solta a passagem. Nada disso
/// prova que a porta abre NO JOGO -- entre a regra e o jogo tem o tique do servidor, o pacote, o
/// mapa cacheado do cliente e o node da animacao, e e exatamente ai que este projeto ja se perdeu
/// varias vezes: "escrever a regra e ligar a regra sao dois trabalhos".
///
/// O caso mais recente foi nesta mesma tarefa, noutro assunto: 35 atlas de animacao escritos no
/// disco, 178 estados reempacotados, relatorio limpo -- e ZERO deles importados pelo Godot. Nada
/// na ferramenta reclamava; so o log do jogo mostrava.
/// =====================================================================================
///
/// O QUE ELA MEDE, andando pro norte contra a porta mais proxima:
///   - fechada:  a celula bloqueia e cega? o node esta no quadro "closed"?
///   - encostou: a porta abriu? em quanto tempo?
///   - aberta:   a celula soltou nos DOIS mapas? o node esta animando?
///   - passou:   o corpo atravessou pro outro lado?
///   - fechou:   depois dos 5 s parado do outro lado, ela volta a bloquear?
/// </summary>
public partial class RoboDePorta : Node
{
	private double _t;
	private int _fase;
	private (int X, int Y) _alvo;
	private double _tAbriu = -1;
	private bool _passou;
	private int _falhas;

	/// <summary>Quanto tempo esperar pela porta fechar sozinha depois de passar.</summary>
	private const double EsperaDoFecho = 7.0;

	public override void _Process(double delta)
	{
		if (World.Instancia is not { } w || w.PosicaoLocal is not { } eu) return;
		if (w.Portas.Count == 0)
		{
			if (_fase == 0) { GD.Print("[porta] esta zona nao tem porta -- suba com --portateste"); _fase = 99; }
			return;
		}

		_t += delta;

		switch (_fase)
		{
			// ---------------------------------------------------- 0: a porta fechada
			case 0:
			{
				_alvo = MaisProxima(w, eu);
				bool bloqueia = w.Colisao?.BlockedCell(_alvo.X, _alvo.Y) ?? false;
				bool cega = w.Visao?.BlockedCell(_alvo.X, _alvo.Y) ?? false;
				string quadro = w.Portas[_alvo].Animation;

				GD.Print($"[porta] alvo na celula ({_alvo.X},{_alvo.Y}) | eu em ({eu.X:0},{eu.Y:0})");
				Conferir("fechada bloqueia", bloqueia, true);
				Conferir("fechada cega    ", cega, true);
				Conferir("fechada desenha ", quadro == "closed", true, quadro);
				_fase = 1;
				_t = 0;
				break;
			}

			// ---------------------------------------------------- 1: andar contra ela
			case 1:
			{
				Input.ActionPress("move_up");
				if (w.Colisao?.BlockedCell(_alvo.X, _alvo.Y) == false)
				{
					_tAbriu = _t;
					bool cega = w.Visao?.BlockedCell(_alvo.X, _alvo.Y) ?? true;
					string quadro = w.Portas[_alvo].Animation;
					GD.Print($"[porta] ABRIU depois de {_t:0.00}s andando contra ela");
					Conferir("aberta nao cega ", cega, false);
					Conferir("aberta anima    ", quadro is "opening" or "open", true, quadro);
					_fase = 2;
					break;
				}
				if (_t > 8) { Conferir("abriu ao encostar", false, true); _fase = 9; }
				break;
			}

			// ---------------------------------------------------- 2: atravessar
			case 2:
			{
				(int _, int cy) = Celula(eu);
				if (cy < _alvo.Y)
				{
					Input.ActionRelease("move_up");
					_passou = true;
					GD.Print($"[porta] ATRAVESSOU (estou na linha {cy}, a porta e a {_alvo.Y})");
					_fase = 3;
					_t = 0;
					break;
				}
				if (_t - _tAbriu > 8) { Conferir("atravessou", false, true); _fase = 9; }
				break;
			}

			// ---------------------------------------------------- 3: esperar fechar
			case 3:
			{
				if (w.Colisao?.BlockedCell(_alvo.X, _alvo.Y) == true)
				{
					string quadro = w.Portas[_alvo].Animation;
					GD.Print($"[porta] FECHOU sozinha {_t:0.0}s depois de eu passar"
							 + $" (o DM manda {PortasDaZona.SegundosAberta:0.#}s)");
					Conferir("fechada cega de novo", w.Visao?.BlockedCell(_alvo.X, _alvo.Y) ?? false, true);
					Conferir("fechada desenha     ", quadro is "closing" or "closed", true, quadro);
					_fase = 9;
					break;
				}
				if (_t > EsperaDoFecho) { Conferir("fechou sozinha", false, true); _fase = 9; }
				break;
			}

			// ---------------------------------------------------- 9: relatorio
			case 9:
				Input.ActionRelease("move_up");
				GD.Print(_falhas == 0 && _passou
					? "[porta] ===== TUDO OK ====="
					: $"[porta] ===== {_falhas} FALHA(S){(_passou ? "" : " | NAO ATRAVESSOU")} =====");
				_fase = 99;
				break;
		}
	}

	private void Conferir(string oque, bool deu, bool esperado, string detalhe = "")
	{
		bool ok = deu == esperado;
		if (!ok) _falhas++;
		GD.Print($"[porta]   {oque}: {(deu ? "sim" : "nao")}"
				 + (detalhe.Length > 0 ? $" ({detalhe})" : "")
				 + (ok ? "  ok" : $"  ERRADO (esperado {(esperado ? "sim" : "nao")})"));
	}

	private static (int X, int Y) Celula(Vector2 pos)
	{
		const int t = ZoneCollision.TileSize;
		return ((int)Mathf.Floor(pos.X / t), (int)Mathf.Floor((pos.Y + MoveRules.FeetOffsetY) / t));
	}

	private static (int X, int Y) MaisProxima(World w, Vector2 eu)
	{
		const int t = ZoneCollision.TileSize;
		(int X, int Y) melhor = default;
		float d = float.MaxValue;
		foreach ((int X, int Y) c in w.Portas.Keys)
		{
			float dd = eu.DistanceSquaredTo(new Vector2(c.X * t + t / 2f, c.Y * t + t / 2f));
			if (dd < d) { d = dd; melhor = c; }
		}
		return melhor;
	}
}
