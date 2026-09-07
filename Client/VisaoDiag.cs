using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// DIAGNOSTICO DA SOMBRA (`--diagvisao`). Sem janela, sem rede, sem servidor.
///
/// POR QUE ISTO EXISTE. O veu ja foi refeito quatro vezes por parecer errado NA TELA, e olhar
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
///
/// ============================ O QUE MAIS SE MEDE, DESDE 2026-09-07 ============================
/// A foto do dono ("sombra que sai do meio da parede", "sombra que fica sobre tile e outras
/// nao") tinha causa geometrica, e a causa virou tres numeros que tem que ser ZERO:
///
///   * PARADAS FORA DE FACE -- raios que pararam em algo que nao e a fronteira livre/cega. E o
///     que a regra da travessia fazia num vao de portao e na ponta do muro (a saida lateral);
///   * CUNHAS -- paredes ESCURAS com algum ponto interno visivel: a aresta do leque cortando o
///     tile do muro em diagonal;
///   * FACES ERRADAS -- paredes que o leque diz claras e um raio direto diz escuras (ou o
///     contrario). E a prova de que "que parede esta clara" e a mesma resposta pelos dois lados.
///
/// E a DOBRA: o maior setor entre dois raios vizinhos, que passa de 180 graus quando o olho
/// esta fora do retangulo da tela -- a escuridao diagonal que o espectador do torneio via.
///
/// OS TRES DEFEITOS INJETADOS (a regra antiga, a parede sem furo, a tela sem o olho) rodam no
/// portao e tem que ficar VERMELHOS: uma bancada que nao fica vermelha com o defeito conhecido
/// nao mede nada -- ver a memoria "a bancada mede INTENCAO".
/// ================================================================================================
/// </summary>
public partial class VisaoDiag : Node
{
	private const string Mapa = "res://Assets/Maps/z01_Earth.vis";
	private const int T = ZoneCollision.TileSize;

	/// <summary>Janela de teste: 40x24 celulas, a mesma ordem de grandeza de uma tela em zoom 2.</summary>
	private const int Larg = 40, Alt = 24;

	private int _ok, _falhas;
	private readonly List<string> _erros = [];

	private void Conferir(bool ok, string oque)
	{
		if (ok) _ok++; else { _falhas++; _erros.Add(oque); }
		GD.Print($"[visao]   {(ok ? "ok   " : "FALHA")}  {oque}");
	}

	public override void _Ready()
	{
		if (!Godot.FileAccess.FileExists(Mapa)) { GD.Print($"[visao] sem {Mapa}"); Sair(); return; }
		ZoneCollision? m = ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(Mapa));
		if (m == null) { GD.Print("[visao] .vis ilegivel"); Sair(); return; }

		GD.Print($"[visao] mapa {m.Width}x{m.Height}");

		foreach ((string nome, int cx, int cy, int fileira) in Locais(m))
		{
			var olho = new Vector2(cx * T + 16, cy * T + 16);
			Medida md = Medir(m, nome, olho, TelaEmVolta(olho), fileira);
			Conferir(md.ParadasForaDeFace == 0, $"{nome}: toda parada de raio e uma fronteira livre/cega ({md.ParadasForaDeFace} fora de face)");
			Conferir(md.Cunhas == 0, $"{nome}: nenhuma parede escura tem ponto interno visivel ({md.Cunhas} cunhas)");
			Conferir(md.FacesErradas == 0, $"{nome}: o leque e o raio direto concordam sobre que parede esta clara ({md.FacesErradas} faces erradas)");
			Conferir(md.MaiorSetor < 180f, $"{nome}: o leque nao dobra (maior setor {md.MaiorSetor:0.0} graus)");
			Conferir(md.Erradas * 100 <= Math.Max(1, md.Livres), $"{nome}: fan e raio direto divergem em menos de 1% das celulas livres ({md.Erradas} de {md.Livres})");
			// SO A FILEIRA DO MURO: as outras paredes da janela podem alternar de verdade (um pedaco
			// tapado por outra coisa). O dente que importa e o do muro reto em campo aberto.
			if (nome.StartsWith("ao norte")) Conferir(md.DentesNaFileira == 0, $"{nome}: a fileira do muro nao tem dente de serra ({md.DentesNaFileira} nela, {md.Dentes} na janela toda)");
			if (nome.StartsWith("muro atras"))
			{
				Conferir(md.ParedesClaras > 0, $"{nome}: o muro da frente esta claro ({md.ParedesClaras} paredes claras)");
				Conferir(md.Paredes - md.ParedesClaras > 0, $"{nome}: o muro de tras continua escuro ({md.Paredes - md.ParedesClaras} paredes escuras)");
			}
			if (nome.StartsWith("o portao"))
			{
				Conferir(md.ParedesClaras > 0 && md.Sombra > 0, $"{nome}: ha parede clara ({md.ParedesClaras}) e chao escuro atras dela ({md.Sombra})");
				OsDefeitosInjetados(m, olho);
			}
		}

		GD.Print(_falhas == 0 ? $"[visao] ==== {_ok} OK, TUDO OK ====" : $"[visao] ==== {_ok} OK, {_falhas} FALHA(S) ====");
		foreach (string e in _erros) GD.Print("[visao]   - " + e);
		Sair();
	}

	private static Rect2 TelaEmVolta(Vector2 olho)
		=> new(olho - new Vector2(Larg * T / 2, Alt * T / 2), new Vector2(Larg * T, Alt * T));

	/// <summary>
	/// A BANCADA FICA VERMELHA COM O DEFEITO CONHECIDO? Cada um dos tres e ligado, medido e
	/// desligado; o que se confere e que o numero correspondente saiu do zero.
	/// </summary>
	private void OsDefeitosInjetados(ZoneCollision m, Vector2 olho)
	{
		Rect2 tela = TelaEmVolta(olho);

		Visao.ParaNaSaidaDeTeste = true;
		Medida antiga = Medir(m, "DEFEITO: o raio para na SAIDA da celula (regra antiga)", olho, tela, -1);
		Visao.ParaNaSaidaDeTeste = false;
		Conferir(antiga.ParadasForaDeFace > 0 || antiga.Cunhas > 0,
				 $"DEFEITO INJETADO (parar na saida) fica vermelho: {antiga.ParadasForaDeFace} paradas fora de face, {antiga.Cunhas} cunhas");

		Visao.SemFacesDeTeste = true;
		Medida semFuro = Medir(m, "DEFEITO: parede sem furo", olho, tela, -1);
		Visao.SemFacesDeTeste = false;
		Conferir(semFuro.ParedesClaras == 0 && semFuro.FacesErradas > 0,
				 $"DEFEITO INJETADO (sem furo) fica vermelho: {semFuro.ParedesClaras} paredes claras, {semFuro.FacesErradas} faces erradas");

		// O ESPECTADOR: o olho a 30 celulas ao sul de uma tela que nao o contem.
		Vector2 longe = olho + new Vector2(0, 30 * T);
		Visao.TelaSemOlhoDeTeste = true;
		Medida dobrada = Medir(m, "DEFEITO: tela sem o olho (a camera do espectador)", longe, tela, -1);
		Visao.TelaSemOlhoDeTeste = false;
		Conferir(dobrada.MaiorSetor >= 180f, $"DEFEITO INJETADO (tela sem o olho) fica vermelho: o leque dobra (maior setor {dobrada.MaiorSetor:0.0} graus)");

		Medida contida = Medir(m, "a tela alargada ate conter o olho", longe, Visao.ComOOlhoDentro(tela, longe), -1);
		Conferir(contida.MaiorSetor < 180f && contida.ParadasForaDeFace == 0,
				 $"com a tela contendo o olho o leque fecha (maior setor {contida.MaiorSetor:0.0} graus)");
	}

	/// <summary>
	/// Escolhe pontos de teste PELO MAPA, nao a dedo: um em campo aberto, um colado numa
	/// parede e um cercado de parede. Sao os tres regimes em que a geometria falha diferente.
	/// </summary>
	private static List<(string, int, int, int)> Locais(ZoneCollision m)
	{
		// (nome, cx do olho, cy do olho, fileira do muro que se olha -- ou -1)
		var achados = new List<(string, int, int, int)>();
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

				if (aberto < 0 && vizinhos == 0) { aberto = 1; achados.Add(("campo aberto", cx, cy, -1)); }
				if (colado < 0 && encostado) { colado = 1; achados.Add(("colado na parede", cx, cy, -1)); }
				if (vizinhos > melhorDenso && vizinhos < 120) { melhorDenso = vizinhos; denso = cy * 100000 + cx; }
			}

		if (denso >= 0) achados.Add(($"cercado ({melhorDenso} paredes em volta)", denso % 100000, denso / 100000, -1));

		// O CASO DA PRIMEIRA FOTO: de pe em campo aberto, ao NORTE de um muro comprido e reto. Era
		// ali que cada tile projetava sombra no tile do lado.
		if (MuroComprido(m) is { } mc) achados.Add(("ao norte de um muro comprido", mc.Item1, mc.Item2, mc.Item3));

		// MURO ATRAS DE MURO, com um vao no meio: o da frente claro, o de tras escuro -- "clara, a nao
		// ser que a sombra de outra parede a cubra" (dono, 2026-08-03).
		if (MuroAtrasDeMuro(m) is { } mm) achados.Add(("muro atras de muro", mm.Item1, mm.Item2, mm.Item3));

		// O PORTAO -- a foto de 2026-09-07: muro com um vao de duas a quatro celulas, visto de quatro
		// celulas ao sul e um pouco a oeste do vao. E onde a travessia produzia a cunha.
		if (Portao(m) is { } po) achados.Add(("o portao da foto", po.Item1, po.Item2, po.Item3));
		return achados;
	}

	/// <summary>
	/// Acha uma fileira reta de parede com campo aberto em cima, e devolve um ponto a 3 celulas
	/// ao norte do meio dela. E a posicao da primeira foto.
	/// </summary>
	private static (int, int, int)? MuroComprido(ZoneCollision m)
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
				if (corrida >= Minimo) return (cx - Minimo / 2, cy - 3, cy);
			}
		}
		return null;
	}

	/// <summary>Duas fileiras de parede separadas por uma livre, seis celulas seguidas; o olho 3 ao norte da primeira.</summary>
	private static (int, int, int)? MuroAtrasDeMuro(ZoneCollision m)
	{
		const int Minimo = 6;
		for (int cy = 40; cy < m.Height - 40; cy++)
		{
			int corrida = 0;
			for (int cx = 40; cx < m.Width - 40; cx++)
			{
				bool par = m.BlockedCell(cx, cy) && !m.BlockedCell(cx, cy + 1) && m.BlockedCell(cx, cy + 2)
						&& !m.BlockedCell(cx, cy - 1) && !m.BlockedCell(cx, cy - 3);
				corrida = par ? corrida + 1 : 0;
				if (corrida >= Minimo) return (cx - Minimo / 2, cy - 3, cy);
			}
		}
		return null;
	}

	/// <summary>
	/// Fileira de muro (5+) | vao de 2..4 | fileira de muro (5+), com chao livre nas duas linhas ao
	/// sul; o olho 4 celulas ao sul e 5 a oeste do vao, como na foto.
	/// </summary>
	private static (int, int, int)? Portao(ZoneCollision m)
	{
		for (int cy = 10; cy < m.Height - 10; cy++)
		{
			int cx = 10;
			while (cx < m.Width - 10)
			{
				if (!m.BlockedCell(cx, cy)) { cx++; continue; }
				int x0 = cx;
				while (cx < m.Width && m.BlockedCell(cx, cy)) cx++;
				int x1 = cx - 1, vx0 = cx;
				while (cx < m.Width && !m.BlockedCell(cx, cy)) cx++;
				int vao = cx - vx0;
				if (cx >= m.Width) break;
				int x2 = cx;
				while (cx < m.Width && m.BlockedCell(cx, cy)) cx++;
				int x3 = cx - 1;
				if (x1 - x0 + 1 < 5 || x3 - x2 + 1 < 5 || vao < 2 || vao > 4) { cx = x2; continue; }
				int livres = 0;
				for (int x = x0; x <= x3; x++)
					if (!m.BlockedCell(x, cy + 1) && !m.BlockedCell(x, cy + 2) && !m.BlockedCell(x, cy + 3) && !m.BlockedCell(x, cy + 4)) livres++;
				if (livres * 10 >= (x3 - x0) * 7 && !m.BlockedCell(vx0 - 5, cy + 4)) return (vx0 - 5, cy + 4, cy);
				cx = x2;
			}
		}
		return null;
	}

	private readonly record struct Medida(int Paredes, int ParedesClaras, int FacesErradas, int Cunhas, int Dentes,
										  int DentesNaFileira, int ParadasForaDeFace, float MaiorSetor, int Livres, int Sombra, int Erradas);

	private static Medida Medir(ZoneCollision m, string nome, Vector2 olho, Rect2 tela, int fileira)
	{
		var v = new Visao { Mapa = m };
		v.Recalcular(olho, tela);

		int cx0 = (int)MathF.Floor(tela.Position.X / T), cy0 = (int)MathF.Floor(tela.Position.Y / T);
		int cx1 = (int)MathF.Floor(tela.End.X / T), cy1 = (int)MathF.Floor(tela.End.Y / T);
		int erradas = 0, sombra = 0, livres = 0, paredes = 0, claras = 0, facesErradas = 0, cunhas = 0, dentes = 0, dentesNaFileira = 0;
		var mapa = new System.Text.StringBuilder();

		// O DENTE DA FILEIRA so conta entre duas paredes que TEM a face virada pro olho livre (o chao
		// do lado do olho): uma parede tapada por outra coisa na frente escurece de verdade, e a troca
		// clara/escura ali e geometria, nao dente. `ladoDoOlho` = a linha do chao entre o muro e o olho.
		int ladoDoOlho = olho.Y < fileira * T ? -1 : 1;

		for (int cy = cy0; cy <= cy1; cy++)
		{
			bool? anterior = null, anteriorComFace = null;
			for (int cx = cx0; cx <= cx1; cx++)
			{
				if (m.BlockedCell(cx, cy))
				{
					// A PAREDE: clara pelo leque (o furo) contra clara por raio direto ate as
					// mesmas amostras de face. `W` = clara, `#` = escura, `!` = os dois discordam,
					// `c` = escura com ponto interno visivel (a cunha).
					paredes++;
					bool clara = v.ParedeIluminada(cx, cy);
					bool verdade = FaceVistaPorRaio(m, v, olho, cx, cy);
					if (clara) claras++;
					// O ANEL DE FORA NAO ENTRA NA COMPARACAO: o leque acaba na borda do retangulo e o raio
					// direto nao -- uma face meio pixel alem da borda e "escura" pro leque e "vista" pro raio,
					// e as duas respostas estao certas. E uma celula de margem, fora da tela de verdade.
					bool noAnel = cx == cx0 || cx == cx1 || cy == cy0 || cy == cy1;
					if (!noAnel && clara != verdade) facesErradas++;
					bool cunha = !clara && PontoInternoVisivel(v, cx, cy);
					if (cunha) cunhas++;
					if (anterior is { } ant && ant != clara) dentes++;
					anterior = clara;
					bool comFace = cy == fileira && !m.BlockedCell(cx, cy + ladoDoOlho);
					if (comFace && anteriorComFace is { } antF && antF != clara) dentesNaFileira++;
					anteriorComFace = comFace ? clara : null;
					mapa.Append(cunha ? 'c' : clara != verdade ? '!' : clara ? 'W' : '#');
					continue;
				}
				anterior = null; anteriorComFace = null;   // a fileira de parede acabou

				livres++;
				var alvo = new Vector2(cx * T + 16, cy * T + 16);

				// VERDADE: marcha ate o alvo e ve se chegou
				Vector2 d = alvo - olho;
				float dist = d.Length();
				bool chega = dist < 1f || v.Marchar(olho, d / dist, dist) >= dist - 0.5f;

				// O LEQUE
				bool leque = v.Ve(alvo);

				if (!chega) sombra++;
				if (chega != leque) { erradas++; mapa.Append('X'); }
				else mapa.Append(chega ? '.' : ' ');
			}
			mapa.Append('\n');
		}

		int cxo = (int)MathF.Floor(olho.X / T) - cx0, cyo = (int)MathF.Floor(olho.Y / T) - cy0;
		var linhas = mapa.ToString().Split('\n');
		if (cyo >= 0 && cyo < linhas.Length && cxo >= 0 && cxo < linhas[cyo].Length)
			linhas[cyo] = linhas[cyo][..cxo] + "@" + linhas[cyo][(cxo + 1)..];

		// tempo: refaz o leque 200 vezes andando 1 px, que e o pior caso real (correndo)
		ulong t0 = Time.GetTicksUsec();
		for (int i = 0; i < 200; i++) v.Recalcular(olho + new Vector2(i % 7, 0), tela);
		double ms = (Time.GetTicksUsec() - t0) / 1000.0 / 200.0;
		v.Recalcular(olho, tela);

		GD.Print($"\n[visao] === {nome} em ({(int)olho.X},{(int)olho.Y}) ===");
		GD.Print($"[visao] raios {v.QuantosRaios} | parede {paredes} (claras {claras}, faces erradas {facesErradas}, cunhas {cunhas}, dentes {dentes}, na fileira {dentesNaFileira})"
				 + $" | paradas fora de face {v.ParadasForaDeFace} | maior setor {v.MaiorSetorGraus:0.0} graus"
				 + $" | sombra {sombra} de {livres} livres | DIVERGENCIAS {erradas} | {ms:0.000} ms por recalculo");
		GD.Print(string.Join('\n', linhas));

		var md = new Medida(paredes, claras, facesErradas, cunhas, dentes, dentesNaFileira, v.ParadasForaDeFace, v.MaiorSetorGraus, livres, sombra, erradas);
		// o Visao nunca entrou na arvore, entao ninguem vai liberar o CanvasItem por nos
		v.Free();
		return md;
	}

	/// <summary>
	/// A VERDADE DA FACE: as mesmas tres amostras por face que o veu usa (meio pixel pra fora, so
	/// nas faces que dao pra celula livre), so que alcancadas por raio DIRETO, sem o leque.
	/// </summary>
	internal static bool FaceVistaPorRaio(ZoneCollision m, Visao v, Vector2 olho, int cx, int cy)
	{
		const float Recuo = 0.5f;
		float x0 = cx * T, y0 = cy * T;
		(float x, float y, float dx, float dy, bool livre)[] faces =
		[
			(x0, y0 - Recuo, T, 0, !m.BlockedCell(cx, cy - 1)),
			(x0, y0 + T + Recuo, T, 0, !m.BlockedCell(cx, cy + 1)),
			(x0 - Recuo, y0, 0, T, !m.BlockedCell(cx - 1, cy)),
			(x0 + T + Recuo, y0, 0, T, !m.BlockedCell(cx + 1, cy)),
		];
		foreach ((float x, float y, float dx, float dy, bool livre) in faces)
		{
			if (!livre) continue;
			foreach (float k in new[] { 1f / 6f, 0.5f, 5f / 6f })
			{
				var alvo = new Vector2(x + dx * k, y + dy * k);
				Vector2 d = alvo - olho;
				float dist = d.Length();
				if (dist < 1f || v.Marchar(olho, d / dist, dist) >= dist - 0.25f) return true;
			}
		}
		return false;
	}

	/// <summary>Os quatro pontos a um quarto de tile das bordas, por dentro da celula.</summary>
	private static bool PontoInternoVisivel(Visao v, int cx, int cy)
	{
		float x0 = cx * T, y0 = cy * T;
		return v.Ve(new Vector2(x0 + T / 4f, y0 + T / 4f)) || v.Ve(new Vector2(x0 + 3 * T / 4f, y0 + T / 4f))
			|| v.Ve(new Vector2(x0 + T / 4f, y0 + 3 * T / 4f)) || v.Ve(new Vector2(x0 + 3 * T / 4f, y0 + 3 * T / 4f));
	}

	private static void Sair() => Engine.GetMainLoop().CallDeferred("quit");
}
