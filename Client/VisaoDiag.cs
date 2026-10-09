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
///
/// ============================ E O BREU, DESDE 2026-10-08 ============================
/// "A sombra deveria ser totalmente escura vendo de fora pra dentro de uma casa" (dono). A regra
/// esta no cabecalho do <see cref="Visao"/>; aqui ela e conferida no Banco da Terra, que e um
/// predio fechado de verdade no mapa de verdade: de fora, de fora com a porta aberta, de dentro, e
/// com o olho dentro da porta fechada. O que se mede e o que o desenho RECEBE (o byte da mascara
/// por celula e a resposta de <see cref="Visao.Esconde"/>); a cor do pixel e da bancada com janela
/// (`--diagsombra`).
/// ====================================================================================
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

		OInteriorEBreu(m);
		ACavernaEUmComodoSo();

		GD.Print(_falhas == 0 ? $"[visao] ==== {_ok} OK, TUDO OK ====" : $"[visao] ==== {_ok} OK, {_falhas} FALHA(S) ====");
		foreach (string e in _erros) GD.Print("[visao]   - " + e);
		Sair();
	}

	private static Rect2 TelaEmVolta(Vector2 olho)
		=> new(olho - new Vector2(Larg * T / 2, Alt * T / 2), new Vector2(Larg * T, Alt * T));

	// =====================================================================
	// O INTERIOR QUE NAO SE VE E BREU -- o Banco da Terra
	// =====================================================================
	private const string Colisao = "res://Assets/Maps/z01_Earth.col";
	private const string Dentro = "res://Assets/Maps/z01_Earth.dentro";

	/// <summary>
	/// O retangulo do Banco e a porta dele, em celulas. Sao os unicos numeros escritos a mao; o que e
	/// piso e o que e parede la dentro e CONTADO nos bitsets, e a primeira conferencia e que a contagem
	/// bate com o predio que se espera -- se o mapa mudar, quem fica vermelha e a precondicao, com o
	/// numero novo na frase.
	/// </summary>
	private const int BancoX0 = 67, BancoX1 = 78, BancoY0 = 257, BancoY1 = 262, PortaX = 73, PortaY = 262;

	private static Vector2 Centro((int X, int Y) c) => new(c.X * T + 16, c.Y * T + 16);

	/// <summary>
	/// A CAVERNA E INTERNA INTEIRA -- e o que a regra do breu NAO pode fazer e apagar o corredor em que
	/// se anda. De dentro dela, o chao ligado ao do olho e a rocha em volta continuam na sombra de
	/// sempre; e a varredura que acha esse "comodo" cobre o mapa inteiro, entao o tempo dela e medido
	/// aqui (ela se repete a cada parede que cai numa briga la dentro).
	///
	/// A CONEXAO E CONFERIDA POR UMA SEGUNDA VARREDURA, escrita aqui e so com o que esta na tela: o chao
	/// que a bancada alcanca andando de lado a partir do olho, sem sair do retangulo, tem que estar
	/// todo fora do breu. (E um subconjunto do comodo de verdade -- o que da a volta por fora da tela
	/// nao entra --, e por isso a afirmacao e "nenhum destes e breu", e nao uma igualdade.)
	/// </summary>
	private void ACavernaEUmComodoSo()
	{
		const string Base = "res://Assets/Maps/z23_Earth_Cave";
		GD.Print("\n[visao] === A CAVERNA E UM COMODO SO: z23_Earth_Cave ===");

		ZoneCollision? vis = Godot.FileAccess.FileExists(Base + ".vis") ? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(Base + ".vis")) : null;
		ZoneCollision? lido = Godot.FileAccess.FileExists(Base + ".col") ? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(Base + ".col")) : null;
		bool temPlano = vis != null && lido != null && Godot.FileAccess.FileExists(Base + ".dentro")
						&& lido.CarregarDentro(Godot.FileAccess.GetFileAsBytes(Base + ".dentro"));
		Conferir(temPlano, "PRECONDICAO: a caverna da Terra tem `.vis`, `.col` e o plano do que esta sob teto");
		if (!temPlano || vis == null || lido is not { } col) return;

		bool Teto(int cx, int cy) => CelulaInterna.SobTeto(col, cx, cy, caiu: false);

		// UM PONTO DE CORREDOR, achado no mapa: chao sob teto com parede a menos de quatro celulas (pra haver
		// sombra na tela) e com as oito vizinhas livres (pra o olho nao nascer espremido).
		(int X, int Y) aqui = (-1, -1);
		for (int cy = 40; cy < vis.Height - 40 && aqui.X < 0; cy += 2)
			for (int cx = 40; cx < vis.Width - 40; cx += 2)
			{
				bool limpo = true, paredePerto = false;
				for (int dy = -4; dy <= 4 && limpo; dy++)
					for (int dx = -4; dx <= 4; dx++)
					{
						bool cega = vis.BlockedCell(cx + dx, cy + dy);
						if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1 && (cega || !Teto(cx + dx, cy + dy))) { limpo = false; break; }
						if (cega) paredePerto = true;
					}
				if (limpo && paredePerto) { aqui = (cx, cy); break; }
			}
		Conferir(aqui.X >= 0, $"PRECONDICAO: ha um ponto de corredor sob teto na caverna (achado em {aqui.X},{aqui.Y})");
		if (aqui.X < 0) return;

		Vector2 olho = Centro(aqui);
		Rect2 tela = TelaEmVolta(olho);
		var v = new Visao { Mapa = vis, Colisao = col, SobTeto = Teto };
		try
		{
			v.Preparar(olho, tela);

			// a segunda varredura: o chao alcancado de lado a partir do olho, sem sair da tela
			int cx0 = (int)MathF.Floor(tela.Position.X / T), cy0 = (int)MathF.Floor(tela.Position.Y / T);
			int cx1 = (int)MathF.Floor(tela.End.X / T), cy1 = (int)MathF.Floor(tela.End.Y / T);
			var alcancado = new HashSet<(int, int)> { aqui };
			var fila = new Queue<(int X, int Y)>();
			fila.Enqueue(aqui);
			while (fila.Count > 0)
			{
				(int x, int y) = fila.Dequeue();
				foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
				{
					int nx = x + dx, ny = y + dy;
					if (nx < cx0 || nx > cx1 || ny < cy0 || ny > cy1 || vis.BlockedCell(nx, ny) || !Teto(nx, ny)) continue;
					if (alcancado.Add((nx, ny))) fila.Enqueue((nx, ny));
				}
			}
			int naSombra = alcancado.Count(c => !v.Ve(Centro(c))), breuNoCorredor = alcancado.Count(c => v.Breu(c.Item1, c.Item2));
			Conferir(naSombra > 0 && breuNoCorredor == 0,
					 $"de DENTRO da caverna: o corredor ligado ao do olho continua na sombra de sempre -- {alcancado.Count} celulas de chao na tela, {naSombra} delas fora da vista, {breuNoCorredor} marcadas como breu");

			// A SEGUNDA CAMADA DE ROCHA: a celula logo atras da parede que da pro corredor. E parede do MEU
			// comodo tanto quanto a primeira -- sem a regra de espalhar de parede em parede ela sairia preta.
			int segunda = 0, segundaBreu = 0;
			foreach ((int x, int y) in alcancado)
				foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
				{
					int px = x + dx, py = y + dy, sx = x + 2 * dx, sy = y + 2 * dy;
					if (sx < cx0 || sx > cx1 || sy < cy0 || sy > cy1) continue;
					if (!vis.BlockedCell(px, py) || !Teto(px, py) || !vis.BlockedCell(sx, sy) || !Teto(sx, sy)) continue;
					segunda++;
					if (v.Breu(sx, sy)) segundaBreu++;
				}
			Conferir(segunda > 0 && segundaBreu == 0,
					 $"...e a rocha atras da primeira camada de parede tambem: {segunda} celulas de segunda camada na tela, {segundaBreu} marcadas como breu");

			// O TEMPO DA VARREDURA: com o mapa invalidado a cada volta (o que uma parede caindo faz) contra o mesmo
			// recalculo sem invalidar. A diferenca e o comodo.
			const int Voltas = 40;
			ulong t0 = Time.GetTicksUsec();
			for (int i = 0; i < Voltas; i++) v.Preparar(olho, tela);
			ulong t1 = Time.GetTicksUsec();
			for (int i = 0; i < Voltas; i++) { v.InvalidarOComodo(); v.Preparar(olho, tela); }
			ulong t2 = Time.GetTicksUsec();
			double ms = ((t2 - t1) - (double)(t1 - t0)) / 1000.0 / Voltas;
			GD.Print($"[visao] o comodo da caverna: {v.CelulasDoComodo} celulas, {ms:0.00} ms por varredura");
			Conferir(v.CelulasDoComodo > 10_000 && ms < 25.0,
					 $"a varredura do comodo cobre a caverna ({v.CelulasDoComodo} celulas) e cabe num quadro ({ms:0.00} ms; o teto da bancada e 25)");
		}
		finally { v.Free(); }
	}

	private void OInteriorEBreu(ZoneCollision vis)
	{
		GD.Print("\n[visao] === O INTERIOR QUE NAO SE VE E BREU: o Banco da Terra ===");

		ZoneCollision? lido = Godot.FileAccess.FileExists(Colisao)
			? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(Colisao))
			: null;
		bool temPlano = lido != null && Godot.FileAccess.FileExists(Dentro)
						&& lido.CarregarDentro(Godot.FileAccess.GetFileAsBytes(Dentro));
		Conferir(temPlano, "PRECONDICAO: a Terra tem o plano do que esta sob teto (`.dentro`)");
		if (!temPlano || lido is not { } col) return;

		// A MESMA PERGUNTA QUE O JOGO FAZ (`TetoDaZona` passa por aqui), sem nada caido.
		bool Teto(int cx, int cy) => CelulaInterna.SobTeto(col, cx, cy, caiu: false);

		var piso = new List<(int X, int Y)>();
		var parede = new List<(int X, int Y)>();
		for (int cy = BancoY0; cy <= BancoY1; cy++)
			for (int cx = BancoX0; cx <= BancoX1; cx++)
				if (Teto(cx, cy)) (vis.BlockedCell(cx, cy) ? parede : piso).Add((cx, cy));

		Conferir(piso.Count == 30 && parede.Count == 42 && Teto(PortaX, PortaY) && vis.BlockedCell(PortaX, PortaY)
				 && !vis.BlockedCell(PortaX, PortaY + 1) && !Teto(PortaX, PortaY + 1),
				 $"PRECONDICAO: o Banco e o predio que se espera -- 30 de piso e 42 de parede sob teto, a porta fechada em ({PortaX},{PortaY}) e ar livre ao sul dela ({piso.Count} de piso, {parede.Count} de parede)");
		if (piso.Count == 0) return;

		// OUTRO PREDIO DO MAPA, pra as duas perguntas em que "o dos outros" tem que continuar escondido.
		(int X, int Y) outro = (-1, -1);
		for (int cy = 0; cy < vis.Height && outro.X < 0; cy++)
			for (int cx = BancoX1 + 40; cx < vis.Width; cx++)
				if (Teto(cx, cy) && !vis.BlockedCell(cx, cy)) { outro = (cx, cy); break; }
		Conferir(outro.X >= 0, $"PRECONDICAO: ha outro interior no mapa, longe do Banco (achado em {outro.X},{outro.Y})");

		var fora = new Vector2(PortaX * T + 16, (PortaY + 4) * T + 16);   // quatro celulas ao sul da porta
		Rect2 tela = TelaEmVolta(fora);
		var v = new Visao { Mapa = vis, Colisao = col, SobTeto = Teto };
		try
		{
			// ---- 1) de fora, porta fechada
			v.Preparar(fora, tela);
			int pisoBreu = piso.Count(c => v.Breu(c.X, c.Y)), pisoVisto = piso.Count(c => v.Ve(Centro(c)));
			Conferir(pisoBreu == piso.Count && pisoVisto == 0,
					 $"de FORA, com a porta fechada: as {piso.Count} celulas de piso do Banco saem como BREU, e nenhuma e vista ({pisoBreu} breu, {pisoVisto} vistas)");

			int fachada = parede.Count(c => v.ParedeIluminada(c.X, c.Y)), paredeBreu = parede.Count(c => v.Breu(c.X, c.Y));
			Conferir(fachada > 0 && fachada + paredeBreu == parede.Count,
					 $"...a fachada que da pro olho continua CLARA -- o furo vence o breu -- e o resto da parede e breu ({fachada} claras + {paredeBreu} breu de {parede.Count})");

			int breuAoArLivre = 0, penumbra = 0;
			(int X, int Y) naPenumbra = (-1, -1);
			int cx0 = (int)MathF.Floor(tela.Position.X / T), cy0 = (int)MathF.Floor(tela.Position.Y / T);
			int cx1 = (int)MathF.Floor(tela.End.X / T), cy1 = (int)MathF.Floor(tela.End.Y / T);
			for (int cy = cy0; cy <= cy1; cy++)
				for (int cx = cx0; cx <= cx1; cx++)
				{
					if (Teto(cx, cy)) continue;
					if (v.Breu(cx, cy)) breuAoArLivre++;
					if (!vis.BlockedCell(cx, cy) && !v.Ve(Centro((cx, cy)))) { penumbra++; naPenumbra = (cx, cy); }
				}
			Conferir(breuAoArLivre == 0 && penumbra > 0,
					 $"...e nada FORA do teto vira breu: ha {penumbra} celulas de chao na sombra comum (atras do predio, atras da cerca), e {breuAoArLivre} delas marcadas");

			bool somemLaDentro = piso.All(c => v.Esconde(Centro(c)));
			Conferir(somemLaDentro && !v.Esconde(fora) && naPenumbra.X >= 0 && !v.Esconde(Centro(naPenumbra)),
					 "quem tem os pes no piso do Banco NAO e desenhado; quem esta ao ar livre e -- a vista, ou na sombra comum");

			// ---- 2) o defeito: o veu de antes
			int breuComDefeito;
			bool escondeComDefeito;
			Visao.InteriorSemBreuDeTeste = true;
			try
			{
				v.Preparar(fora, tela);
				breuComDefeito = v.Breus;
				escondeComDefeito = v.Esconde(Centro(piso[0]));
			}
			finally { Visao.InteriorSemBreuDeTeste = false; }
			Conferir(breuComDefeito == 0 && !escondeComDefeito,
					 $"(defeito injetado: o interior que nao se ve nao e breu) o piso do Banco volta a ser so penumbra e o corpo la dentro volta a ser desenhado ({breuComDefeito} celulas de breu)");

			// ---- 3) de fora, porta ABERTA: o leque entra, e so o que ele alcanca aparece
			vis.Abrir(PortaX, PortaY);
			col.Abrir(PortaX, PortaY);
			try
			{
				v.InvalidarOComodo();
				v.Preparar(fora, tela);
				var corredor = piso.Where(c => c.X == PortaX).ToList();
				(int X, int Y) cantoA = (BancoX0 + 1, BancoY1 - 1), cantoB = (BancoX1 - 1, BancoY1 - 1);
				Conferir(corredor.Count == 3 && corredor.All(c => v.Ve(Centro(c)) && !v.Esconde(Centro(c))),
						 $"de FORA, com a porta ABERTA: as {corredor.Count} celulas na reta da porta sao VISTAS, e quem esta nelas e desenhado");
				Conferir(v.Breu(cantoA.X, cantoA.Y) && v.Breu(cantoB.X, cantoB.Y) && !v.Ve(Centro(cantoA)) && !v.Ve(Centro(cantoB))
						 && v.Esconde(Centro(cantoA)) && v.Esconde(Centro(cantoB)),
						 "...e os dois cantos do comodo, fora da reta, continuam breu -- a porta aberta mostra o que o olho alcanca, e nao o comodo");
			}
			finally { vis.Fechar(PortaX, PortaY); col.Fechar(PortaX, PortaY); }

			// ---- 4) de dentro: o proprio comodo fica com a sombra de sempre
			var dentro = Centro((BancoX0 + 5, BancoY0 + 3));
			v.InvalidarOComodo();
			v.Preparar(dentro, TelaEmVolta(dentro));
			int meuComodo = piso.Count(c => v.Escondida(c.X, c.Y)) + parede.Count(c => v.Escondida(c.X, c.Y));
			Conferir(meuComodo == 0 && v.Breus == 0,
					 $"de DENTRO: nenhuma celula do proprio Banco e breu -- piso, parede e os quatro cantos ({meuComodo} escondidas, {v.Breus} na mascara)");
			Conferir(outro.X >= 0 && v.Escondida(outro.X, outro.Y) && v.Esconde(Centro(outro)),
					 "...e o interior do OUTRO predio continua escondido pra quem esta debaixo deste teto");

			// ---- 5) o olho dentro de parede: nao ha leque, e o breu continua
			var naPorta = Centro((PortaX, PortaY));
			v.InvalidarOComodo();
			bool comLeque = v.Preparar(naPorta, TelaEmVolta(naPorta));
			Conferir(!comLeque && v.QuantosRaios == 0 && !v.Esconde(Centro(piso[0])) && outro.X >= 0 && v.Esconde(Centro(outro)),
					 "com o OLHO DENTRO DE PAREDE (a porta que fechou em cima de quem parou no vao) nao ha leque -- e o predio dos outros continua escondido, o proprio nao");
		}
		finally { v.Free(); }
	}

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
