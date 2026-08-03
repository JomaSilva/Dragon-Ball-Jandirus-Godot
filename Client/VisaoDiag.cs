using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// DIAGNOSTICO DA SOMBRA (`--diagvisao`). Sem janela, sem rede, sem servidor.
///
/// POR QUE ISTO EXISTE. O veu ja foi refeito duas vezes por parecer errado NA TELA, e olhar
/// pra tela e uma forma ruim de saber se a geometria fecha: um erro de meio tile e invisivel,
/// um vertice que pisca so aparece andando, e uma quina que vaza luz e 1 px. Aqui a pergunta
/// vira numero.
///
/// COMO SE PROVA. Ha duas maneiras independentes de responder "eu vejo aquela celula?":
///
///   1. VERDADE -- lancar um raio direto ate ela e ver se bateu em parede antes de chegar.
///      Caro (um raio por celula), mas nao tem como estar errado.
///   2. O LEQUE -- <see cref="Visao.Ve"/>, que consulta o poligono montado a partir das
///      QUINAS. E o que o jogo usa, e o que pode estar errado.
///
/// Se as duas discordam em alguma celula, o leque perdeu uma quina -- que e exatamente o
/// defeito que produzia a borda serrilhada. O teste roda as duas e conta as divergencias.
/// </summary>
public partial class VisaoDiag : Node
{
	private const string Mapa = "res://Assets/Maps/z01_Earth.vis";
	private const int T = ZoneCollision.TileSize;

	/// <summary>Janela de teste: 40x24 celulas, a mesma ordem de grandeza de uma tela em zoom 2.</summary>
	private const int Larg = 40, Alt = 24;

	public override void _Ready()
	{
		if (!Godot.FileAccess.FileExists(Mapa)) { GD.Print($"[visao] sem {Mapa}"); Sair(); return; }
		ZoneCollision? m = ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(Mapa));
		if (m == null) { GD.Print("[visao] .vis ilegivel"); Sair(); return; }

		GD.Print($"[visao] mapa {m.Width}x{m.Height}");

		foreach ((string nome, int cx, int cy) in Locais(m))
		{
			var olho = new Vector2(cx * T + 16, cy * T + 16);
			Conferir(m, nome, olho);
		}

		Sair();
	}

	private static void Sair() => Engine.GetMainLoop().CallDeferred("quit");

	/// <summary>
	/// Escolhe pontos de teste PELO MAPA, nao a dedo: um em campo aberto, um colado numa
	/// parede e um cercado de parede. Sao os tres regimes em que a geometria falha diferente.
	/// </summary>
	private static List<(string, int, int)> Locais(ZoneCollision m)
	{
		var achados = new List<(string, int, int)>();
		int aberto = -1, colado = -1, denso = -1, melhorDenso = -1;

		for (int cy = 30; cy < m.Height - 30 && achados.Count < 3; cy += 3)
			for (int cx = 30; cx < m.Width - 30; cx += 3)
			{
				if (m.BlockedCell(cx, cy)) continue;

				int vizinhos = 0;
				for (int y = -6; y <= 6; y++)
					for (int x = -6; x <= 6; x++)
						if (m.BlockedCell(cx + x, cy + y)) vizinhos++;

				bool encostado = m.BlockedCell(cx + 1, cy) || m.BlockedCell(cx - 1, cy)
							  || m.BlockedCell(cx, cy + 1) || m.BlockedCell(cx, cy - 1);

				if (aberto < 0 && vizinhos == 0) { aberto = 1; achados.Add(("campo aberto", cx, cy)); }
				if (colado < 0 && encostado) { colado = 1; achados.Add(("colado na parede", cx, cy)); }
				if (vizinhos > melhorDenso && vizinhos < 120) { melhorDenso = vizinhos; denso = cy * 100000 + cx; }
			}

		if (denso >= 0) achados.Add(($"cercado ({melhorDenso} paredes em volta)", denso % 100000, denso / 100000));

		// O CASO DA FOTO, e ele faltava. Os tres regimes acima sao os que a GEOMETRIA quebra
		// diferente; nenhum deles e o que o dono fotografou -- de pe em campo aberto, ao NORTE de
		// um muro comprido e reto. Era ali que cada tile projetava sombra no tile do lado, e como
		// o teste nunca ficava nessa posicao o defeito nao tinha como aparecer em numero.
		if (MuroComprido(m) is { } mc) achados.Add(("ao norte de um muro comprido", mc.Item1, mc.Item2));

		// A FACHADA DE DOIS MATERIAIS -- a segunda foto do dono: cerca de madeira na frente, muro
		// de pedra atras. E o UNICO caso em que a regra do tile faz diferenca, e sem uma posicao
		// destas o `cortes por tile` fica em zero nos quatro locais e o ramo novo viaja sem prova.
		// So 2% dos pares de celulas cegas vizinhas mudam de tile (155 na Terra), entao a chance
		// de cair numa por acaso e desprezivel.
		if (Fachada(m) is { } fa) achados.Add(("fachada de dois materiais", fa.Item1, fa.Item2));
		return achados;
	}

	/// <summary>
	/// Acha uma parede com CEU EM CIMA cujo vizinho de baixo e cego com OUTRO tile, e devolve um
	/// ponto tres celulas ao norte dela.
	/// </summary>
	private static (int, int)? Fachada(ZoneCollision m)
	{
		if (!m.TemGrupos) return null;
		for (int cy = 40; cy < m.Height - 40; cy++)
			for (int cx = 40; cx < m.Width - 40; cx++)
			{
				if (m.BlockedCell(cx, cy - 1) || m.BlockedCell(cx, cy - 3)) continue;
				if (!m.BlockedCell(cx, cy) || !m.BlockedCell(cx, cy + 1)) continue;
				if (m.Grupo(cx, cy) == m.Grupo(cx, cy + 1)) continue;
				return (cx, cy - 3);
			}
		return null;
	}

	/// <summary>
	/// Acha uma fileira reta de parede com campo aberto em cima, e devolve um ponto a 3 celulas
	/// ao norte do meio dela. E a posicao da foto.
	/// </summary>
	private static (int, int)? MuroComprido(ZoneCollision m)
	{
		const int Minimo = 10;   // tiles seguidos: abaixo disso nao da pra ver serrilhado nenhum
		for (int cy = 40; cy < m.Height - 40; cy++)
		{
			int corrida = 0;
			for (int cx = 40; cx < m.Width - 40; cx++)
			{
				// parede COM CEU EM CIMA: um muro no meio de um bloco macico nao tem face a ver
				bool linha = m.BlockedCell(cx, cy) && !m.BlockedCell(cx, cy - 1) && !m.BlockedCell(cx, cy - 4);
				corrida = linha ? corrida + 1 : 0;
				if (corrida >= Minimo) return (cx - Minimo / 2, cy - 3);
			}
		}
		return null;
	}

	private static void Conferir(ZoneCollision m, string nome, Vector2 olho)
	{
		var v = new Visao { Mapa = m };
		var tela = new Rect2(olho - new Vector2(Larg * T / 2, Alt * T / 2), new Vector2(Larg * T, Alt * T));

		v.CortesPorTile = 0;
		v.QuinasDeMaterial = 0;
		v.Recalcular(olho, tela);
		int cortes = v.CortesPorTile;
		int qm = v.QuinasDeMaterial;

		int cx0 = (int)(tela.Position.X / T), cy0 = (int)(tela.Position.Y / T);
		int erradas = 0, sombra = 0, paredes = 0, paredesVistas = 0;
		var mapa = new System.Text.StringBuilder();

		// SERRILHADO: quantas vezes duas paredes VIZINHAS na mesma fileira discordam sobre estar
		// na luz. Um muro reto e um obstaculo SO -- ou a fileira inteira encara o jogador ou nao
		// encara -- entao toda troca aqui e um dente de serra, que foi o defeito da foto. Zero e a
		// meta; o numero nao mede beleza, mede quantas vezes a face do muro se parte.
		int dentes = 0;
		bool? anterior = null;

		for (int y = 0; y < Alt; y++)
		{
			anterior = null;
			for (int x = 0; x < Larg; x++)
			{
				int cx = cx0 + x, cy = cy0 + y;
				if (!m.BlockedCell(cx, cy)) anterior = null;   // a fileira de parede acabou
				if (m.BlockedCell(cx, cy))
				{
					// A PAREDE AGORA E OLHADA, e nao pulada. A regra mudou: a sombra nasce ATRAS do
					// bloco, entao a face que encara o jogador tem que estar ILUMINADA. Enquanto
					// isto so contava celulas livres, a mudanca inteira passava despercebida pela
					// diagnostica -- ela media exatamente o que nao tinha mudado.
					//
					// `#` = parede no escuro (atras de outra); `W` = parede que da pra ver.
					paredes++;
					bool vista = v.Ve(new Vector2(cx * T + 16, cy * T + 16));
					if (vista) paredesVistas++;
					if (anterior is { } ant && ant != vista) dentes++;
					anterior = vista;
					mapa.Append(vista ? 'W' : '#');
					continue;
				}

				var alvo = new Vector2(cx * T + 16, cy * T + 16);

				// VERDADE: marcha ate o alvo e ve se chegou
				Vector2 d = alvo - olho;
				float dist = d.Length();
				bool verdade = dist < 1f || v.Marchar(olho, d / dist, dist) >= dist - 0.5f;

				// O LEQUE
				bool leque = v.Ve(alvo);

				if (!verdade) sombra++;
				if (verdade != leque) { erradas++; mapa.Append('X'); }
				else mapa.Append(verdade ? '.' : ' ');
			}
			mapa.Append('\n');
		}

		int cxo = (int)(olho.X / T) - cx0, cyo = (int)(olho.Y / T) - cy0;
		string arte = mapa.ToString();
		var linhas = arte.Split('\n');
		if (cyo >= 0 && cyo < linhas.Length && cxo >= 0 && cxo < linhas[cyo].Length)
			linhas[cyo] = linhas[cyo][..cxo] + "@" + linhas[cyo][(cxo + 1)..];

		// tempo: refaz o leque 200 vezes andando 1 px, que e o pior caso real (correndo)
		ulong t0 = Time.GetTicksUsec();
		for (int i = 0; i < 200; i++) v.Recalcular(olho + new Vector2(i % 7, 0), tela);
		double ms = (Time.GetTicksUsec() - t0) / 1000.0 / 200.0;

		GD.Print($"\n[visao] === {nome} em ({(int)olho.X},{(int)olho.Y}) ===");
		GD.Print($"[visao] raios {v.QuantosRaios} | parede {paredes} (visiveis {paredesVistas})"
				 + $" | DENTES {dentes} | cortes por tile {cortes} | quinas de material {qm}"
				 + $" | sombra {sombra} de {Larg * Alt - paredes} livres"
				 + $" | DIVERGENCIAS {erradas} | {ms:0.000} ms por recalculo");
		GD.Print(string.Join('\n', linhas));

		// o Visao nunca entrou na arvore, entao ninguem vai liberar o CanvasItem por nos
		v.Free();
	}
}
