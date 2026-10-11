using System.Collections;
using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO MAPA E DO RADAR (`--diagmapa`) -- a metade de TELA dos dois pedidos do dono de 2026-10-10: o mapa
/// do mundo na aba World (com as coordenadas) e o disco do Dragon Radar no canto inferior direito.
///
/// As regras puras (a cor de cada celula, o marcador, o sinal na borda do disco) sao da bancada `mapa` do
/// pipeline; o que o servidor conta e a quem e da `--radarteste`. Aqui se mede o que so existe com janela:
///
///   * o cartao "Mapa" abre com o mundo desenhado, o marcador em cima do corpo e as coordenadas da celula
///     dele -- num planeta DESENHADO (a Terra) e num GERADO de 1000x1000;
///   * o marcador anda com o corpo SEM a pagina remontar;
///   * a imagem e composta UMA vez por lugar (abrir a aba de novo nao compoe), e FORA do quadro: o pior
///     quadro da abertura nao e maior que o de uma abertura com a imagem ja guardada. Com o defeito ligado
///     (`MapasDosLugares.NoQuadroDeTeste`) o engasgo aparece -- e o numero dele sai no log;
///   * no espaco nao ha mapa, ha o aviso e as coordenadas;
///   * sem o Dragon Radar nao ha disco; com ele, sete bolinhas amarelas nos lugares certos (norte em cima,
///     a de longe presa na borda), o clique troca o alcance, a esfera na mao vai pro centro, o convite do
///     torneio nao fica embaixo do disco, e perder o radar apaga o disco.
///
/// Rode com janela (no segundo monitor), no modo host:
///   --host --rede 8231 --horateste 0.5 --diagmapa --raca Human --conta bancada_mapa_ag --nome Cartografo --pasta &lt;fotos&gt;
/// </summary>
public partial class RoboDoMapa : Node
{
	private const int T = ZoneCollision.TileSize;

	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [], _fotos = [];
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;
	private string _pasta = "";

	// o pior intervalo entre dois quadros desde a ultima marca, em ms
	private ulong _quadroAnterior;
	private double _piorQuadro;
	private readonly List<double> _quadros = [];

	private static GameClient? C => GameClient.Instance;
	private static World? M => World.Instancia;
	private static GameServer? S => GameServer.Instance;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dmapa]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[dmapa]   FALHA {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[dmapa] " + s);

	public override void _Process(double delta)
	{
		ulong agora = Time.GetTicksUsec();
		if (_quadroAnterior != 0)
		{
			double ms = (agora - _quadroAnterior) / 1000.0;
			_piorQuadro = Math.Max(_piorQuadro, ms);
			_quadros.Add(ms);
			if (_quadros.Count > 240) _quadros.RemoveAt(0);
		}
		_quadroAnterior = agora;

		if (_acabou) return;
		if (C is not { Connected: true } || M?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		try
		{
			_roteiro ??= Roteiro().GetEnumerator();
			if (!_roteiro.MoveNext()) { Fim(); return; }
			_espera = _roteiro.Current is double d ? d : 0;
		}
		catch (Exception e)
		{
			Afirmar("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
			Fim();
		}
	}

	private void Fim()
	{
		_acabou = true;
		MapasDosLugares.NoQuadroDeTeste = false;
		if (S is { } srv) { srv.RadarSemPortaoDeTeste = false; srv.LimparEsferasDeFoto(); }
		foreach (string f in _fotos) Nota("foto " + f);
		if (_falhou > 0) GD.PrintErr("[dmapa] vermelhas: " + string.Join(" | ", _vermelhas));
		GD.Print(_falhou == 0 ? $"\n[dmapa] ===== TUDO OK ({_ok} OK) =====" : $"\n[dmapa] ===== {_falhou} FALHA(S), {_ok} OK =====");
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private Image? Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza): rode com janela"); return null; }
		string caminho = _pasta + nome + ".png";
		img.SavePng(caminho);
		_fotos.Add(caminho);
		return img;
	}

	private static bool Perto(Color a, Color b, float tol) =>
		Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;

	private static Color Pixel(Image foto, Vector2 p)
	{
		int x = Math.Clamp((int)p.X, 0, foto.GetWidth() - 1), y = Math.Clamp((int)p.Y, 0, foto.GetHeight() - 1);
		return foto.GetPixel(x, y);
	}

	private static double Mediana(List<double> v)
	{
		if (v.Count == 0) return 0;
		var o = new List<double>(v);
		o.Sort();
		return o[o.Count / 2];
	}

	private static Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);

	private static MapaDoLugarNaTela? MapaDaAba() =>
		MenuJogo.Instancia?.PaginaDeTeste("World") is { } pg ? Todos(pg).OfType<MapaDoLugarNaTela>().FirstOrDefault() : null;

	private static string Linha(string rotulo) => MenuJogo.Instancia?.ValorDesenhado("World", rotulo) ?? "(nula)";

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		GameClient cli = C!;
		if (S is not { } srv || MenuJogo.Instancia is not { } menu) { Afirmar("(montagem) o robo roda no modo host, com o menu montado", false); yield break; }

		string[] args = OS.GetCmdlineArgs();
		int ip = Array.IndexOf(args, "--pasta");
		_pasta = ip >= 0 && ip + 1 < args.Length ? args[ip + 1] : ProjectSettings.GlobalizePath("user://");
		if (!_pasta.EndsWith('/') && !_pasta.EndsWith('\\')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);
		Nota($"fotos em {_pasta} | janela {DisplayServer.WindowGetSize()}");

		// =====================================================================
		// A. O MAPA NUM PLANETA DESENHADO -- a Terra
		// =====================================================================
		Nota("--- A. a Terra (mapa desenhado a mao, 500x500) ---");
		srv.RadarDeFoto(cli.LocalId, false);
		srv.MoveToZone(cli.LocalId, ZoneKey.Premade("Earth"), No(291, 97));
		yield return 5.0;
		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);
		MapasDosLugares.EsquecerDeTeste();
		int montadosAntes = MapasDosLugares.MontadosDeTeste;

		menu.Abrir();
		menu.IrPara("World");
		yield return 0.1;
		MapaDoLugarNaTela? mapa = MapaDaAba();
		Afirmar("o cartao 'Mapa' da aba World tem o quadro do mapa", mapa != null);
		if (mapa == null) yield break;
		for (int i = 0; i < 50 && mapa.EstadoDeTeste != MapasDosLugares.Estado.Pronto; i++) { yield return 0.1; mapa = MapaDaAba() ?? mapa; }

		Afirmar("o mapa da Terra fica PRONTO, e foi composto uma vez", mapa.EstadoDeTeste == MapasDosLugares.Estado.Pronto
				&& MapasDosLugares.MontadosDeTeste == montadosAntes + 1, $"{mapa.EstadoDeTeste}, {MapasDosLugares.MontadosDeTeste - montadosAntes} composicao(oes)");
		MapasDosLugares.Mapa? m = mapa.MapaDeTeste;
		Afirmar("a imagem tem um pixel por tile (500x500) e nenhuma celula na cor de reserva",
				m is { LarguraEmTiles: 500, AlturaEmTiles: 500, NaReserva: 0 } && m.Textura is { } tx && tx.GetWidth() == 500 && tx.GetHeight() == 500,
				m == null ? "sem mapa" : $"{m.LarguraEmTiles}x{m.AlturaEmTiles} tiles, {m.Textura?.GetWidth()} px, {m.NaReserva} na reserva");
		Nota($"    custo na Terra: compor {m?.MsDeMontar:0.0} ms na thread de trabalho | subir pra placa {m?.MsDeSubir:0.00} ms na principal");

		Afirmar("a linha 'Tamanho' diz 500 x 500 tiles e a 'Mundo' diz o nome do lugar",
				Linha("Tamanho") == "500 × 500 tiles" && Linha("Mundo") == cli.NomeDoLugar, $"{Linha("Tamanho")} | {Linha("Mundo")}");
		Vector2 eu = M!.PosicaoLocal!.Value;
		Afirmar("a linha 'Coordenadas' diz a celula do corpo: X 291 · Y 97",
				Linha("Coordenadas") == "X 291 · Y 97" && Linha("Coordenadas") == MapaDoLugar.Coordenadas(new Vec2(eu.X, eu.Y)), Linha("Coordenadas"));

		Rect2 r = mapa.RetanguloDeTeste;
		Vector2 esperado = r.Position + new Vector2(291.5f / 500f, 97.5f / 500f) * r.Size;
		Afirmar("o marcador esta em cima do corpo: (291,5 / 500; 97,5 / 500) do retangulo do mapa",
				mapa.MarcadorDeTeste is { } mk && mk.DistanceTo(esperado) <= 1.5f && !mapa.ForaDeTeste, $"{mapa.MarcadorDeTeste} x {esperado}");
		Afirmar("a dica do clique esta escrita no cartao (ha mapa pra clicar)",
				Todos(menu.PaginaDeTeste("World")!).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text.StartsWith("Clique no mapa", StringComparison.Ordinal)));
		Afirmar("o quadro do mapa e quadrado e cabe no cartao", Math.Abs(r.Size.X - MapaDoLugarNaTela.Lado) < 1 && Math.Abs(r.Size.Y - MapaDoLugarNaTela.Lado) < 1, r.ToString());

		yield return 0.4;
		// A PAGINA REMONTA SOZINHA (o texto do ceu muda de segundos em segundos) e o node do mapa nasce de novo:
		// depois de cada espera ele e pego outra vez. E o que faz a imagem guardada e a vista serem do processo.
		mapa = MapaDaAba() ?? mapa;
		m = mapa.MapaDeTeste;
		Image? foto = Fotografar("mapa-01-terra");
		if (foto != null && m?.Textura?.GetImage() is { } tex && mapa.MarcadorDeTeste is { } marca)
		{
			Vector2 origem = mapa.GlobalPosition + r.Position;
			Afirmar("NA FOTO: o miolo do marcador e o laranja do tema", Perto(Pixel(foto, mapa.GlobalPosition + marca), Tema.Destaque, 0.12f),
					Pixel(foto, mapa.GlobalPosition + marca).ToHtml());

			// O QUE ESTA NA FOTO E A IMAGEM DO MAPA: 144 amostras em grade, cada uma contra o texel dela -- e,
			// como contra-exemplo na mesma foto, contra um texel DESLOCADO (o mapa desenhado no lugar errado).
			int certas = 0, deslocadas = 0, total = 0;
			var cores = new HashSet<int>();
			for (int gy = 0; gy < 12; gy++)
				for (int gx = 0; gx < 12; gx++)
				{
					int txl = 20 + gx * 40, tyl = 20 + gy * 40;
					Vector2 naTela = origem + new Vector2((txl + 0.5f) / 500f, (tyl + 0.5f) / 500f) * r.Size;
					if ((naTela - (mapa.GlobalPosition + marca)).Length() < 20) continue;
					Color daFoto = Pixel(foto, naTela);
					total++;
					if (Perto(daFoto, tex.GetPixel(txl, tyl), 0.2f)) certas++;
					if (Perto(daFoto, tex.GetPixel((txl + 170) % 500, (tyl + 130) % 500), 0.2f)) deslocadas++;
					cores.Add(((int)(daFoto.R * 7) << 6) | ((int)(daFoto.G * 7) << 3) | (int)(daFoto.B * 7));
				}
			Afirmar("NA FOTO: o que esta no quadro e a imagem do mapa, no lugar (amostras em grade batem com o texel de cada uma)",
					total > 100 && certas >= total * 0.75, $"{certas} de {total}");
			Afirmar("...e a mesma conta com o mapa DESLOCADO bate bem menos (a checagem sabe ver um mapa fora do lugar)",
					deslocadas < certas - total * 0.15, $"{deslocadas} de {total} deslocadas contra {certas} certas");
			Afirmar("NA FOTO: o mapa tem varias cores (mar, grama, areia, neve), e nao uma chapa", cores.Count >= 5, $"{cores.Count} faixas de cor");
		}
		else Nota("  PULADA  as checagens de pixel do mapa (sem foto)");

		// ---- a vista de perto: um clique no quadro ----
		Vector2 noQuadro = mapa.GlobalPosition + new Vector2(MapaDoLugarNaTela.Lado * 0.8f, MapaDoLugarNaTela.Lado * 0.8f);
		void CliqueNoMapa()
		{
			GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = noQuadro, GlobalPosition = noQuadro }, true);
			GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = noQuadro, GlobalPosition = noQuadro }, true);
		}
		CliqueNoMapa();
		yield return 0.3;
		mapa = MapaDaAba() ?? mapa;
		(float jx, float jy, float jl, float ju, float jv) = MapaDoLugar.VistaDePerto(new Vec2(eu.X, eu.Y), 500, 500);
		Vector2 noMeio = mapa.RetanguloDeTeste.Position + new Vector2(ju, jv) * mapa.RetanguloDeTeste.Size;
		Afirmar("um CLIQUE no mapa liga a vista de perto: o recorte e a janela de 128 tiles em volta do corpo, e o marcador vai pro meio do quadro",
				MapaDoLugarNaTela.DePertoDeTeste && mapa.RecorteDeTeste is { } rc && Math.Abs(rc.Position.X - jx) < 0.01f && Math.Abs(rc.Position.Y - jy) < 0.01f
				&& Math.Abs(rc.Size.X - jl) < 0.01f && jl == MapaDoLugar.TilesDaVistaDePerto
				&& mapa.MarcadorDeTeste is { } mp && mp.DistanceTo(noMeio) <= 1.5f && mp.DistanceTo(new Vector2(MapaDoLugarNaTela.Lado, MapaDoLugarNaTela.Lado) / 2f) <= 1.5f,
				$"recorte {mapa.RecorteDeTeste} | marcador {mapa.MarcadorDeTeste} x {noMeio}");
		yield return 0.2;
		Fotografar("mapa-01b-terra-de-perto");
		CliqueNoMapa();
		yield return 0.3;
		mapa = MapaDaAba() ?? mapa;
		Afirmar("...e outro clique volta pro mundo inteiro, com o marcador no lugar do corpo no planeta",
				!MapaDoLugarNaTela.DePertoDeTeste && mapa.RecorteDeTeste == null && mapa.MarcadorDeTeste is { } mv && mv.DistanceTo(esperado) <= 1.5f, $"{mapa.MarcadorDeTeste}");

		// ---- o marcador anda com o corpo, e a pagina nao remonta ----
		srv.MoveToZone(cli.LocalId, ZoneKey.Premade("Earth"), No(391, 197));
		yield return 2.5;
		MapaDoLugarNaTela? depois = MapaDaAba();
		Vector2 esperado2 = r.Position + new Vector2(391.5f / 500f, 197.5f / 500f) * r.Size;
		Afirmar("o corpo andou 100 tiles pra leste e 100 pro sul: o marcador foi junto, e as coordenadas dizem X 391 · Y 197",
				depois?.MarcadorDeTeste is { } mk2 && mk2.DistanceTo(esperado2) <= 1.5f && Linha("Coordenadas") == "X 391 · Y 197",
				$"{depois?.MarcadorDeTeste} x {esperado2} | {Linha("Coordenadas")}");

		// SEM REMONTAR A PAGINA. O corpo pula (teleporte do proprio cliente) e, um quadro depois, o MESMO node
		// ja mostra o lugar novo. Ate tres tentativas: a pagina remonta sozinha quando o texto do ceu muda (no
		// maximo uma vez por segundo), e uma remontagem que caia na janela nao e do marcador.
		bool andouSemRemontar = false;
		string comoFoi = "";
		for (int tentativa = 0; tentativa < 3 && !andouSemRemontar; tentativa++)
		{
			MapaDoLugarNaTela? no = MapaDaAba();
			int remontagens = menu.RemontagensDeTeste;
			Vec2 destino = No(120 + tentativa * 60, 300 - tentativa * 40);
			M!.TeleportarLocalDeTeste(destino);
			yield return 0.0;   // UM quadro: antes de o servidor devolver o corpo pro lugar (o pulo e so do cliente)
			Vector2 la = r.Position + new Vector2((120 + tentativa * 60 + 0.5f) / 500f, (300 - tentativa * 40 + 0.5f) / 500f) * r.Size;
			andouSemRemontar = no != null && ReferenceEquals(no, MapaDaAba()) && menu.RemontagensDeTeste == remontagens
				&& no.MarcadorDeTeste is { } mk3 && mk3.DistanceTo(la) <= 1.5f && Linha("Coordenadas") == MapaDoLugar.Coordenadas(destino);
			comoFoi = $"tentativa {tentativa + 1}: {menu.RemontagensDeTeste - remontagens} remontagem(ns), marcador {no?.MarcadorDeTeste} x {la}, '{Linha("Coordenadas")}'";
		}
		Afirmar("...e o marcador anda SEM a pagina remontar: o corpo pula, e no quadro seguinte o MESMO node mostra o lugar e as coordenadas novas",
				andouSemRemontar, comoFoi);
		srv.MoveToZone(cli.LocalId, ZoneKey.Premade("Earth"), No(291, 97));
		yield return 2.5;
		mapa = MapaDaAba() ?? mapa;

		// ---- uma vez por lugar ----
		int n0 = MapasDosLugares.MontadosDeTeste;
		for (int i = 0; i < 3; i++) { menu.ForcarRedesenho(); yield return 0.25; }
		Afirmar("remontar a pagina tres vezes NAO compoe a imagem de novo (ela esta guardada)", MapasDosLugares.MontadosDeTeste == n0
				&& MapaDaAba()?.EstadoDeTeste == MapasDosLugares.Estado.Pronto, $"{MapasDosLugares.MontadosDeTeste - n0} composicao(oes) a mais");
		MapasDosLugares.EsquecerDeTeste();
		menu.ForcarRedesenho();
		yield return 0.6;
		Afirmar("(contra-exemplo) com o guardado esquecido, a abertura seguinte compoe UMA vez -- o contador ve uma composicao",
				MapasDosLugares.MontadosDeTeste == n0 + 1, $"{MapasDosLugares.MontadosDeTeste - n0}");

		// ---- fora do quadro ----
		foreach (object passo in MedirOQuadro(menu, "a Terra (500x500)")) yield return passo;

		// =====================================================================
		// B. O MAPA NUM PLANETA GERADO DE 1000x1000
		// =====================================================================
		Nota("--- B. um planeta GERADO do maior tamanho que a gravidade sorteia ---");
		// A SEED DO MAIOR MUNDO: a primeira que a gravidade leva a 1000 tiles (ou, nao havendo em 300 mil, a maior).
		ulong seed = 1;
		int lado = 0;
		for (ulong s = 1; s <= 300_000 && lado < MundoProcedural.LadoMaximo; s++)
			if (MundoProcedural.DaSeed(s, "").Lado is var l && l > lado) { lado = l; seed = s; }
		int imagem = (lado + MapaDoLugar.BlocoPara(lado, lado) - 1) / MapaDoLugar.BlocoPara(lado, lado);
		string nome = "Colosso-" + seed;
		Nota($"    seed {seed}: {MundoProcedural.DaSeed(seed, nome).Descricao()}");
		bool pousou = false;
		for (int i = 0; i < 100 && !pousou; i++)
		{
			pousou = srv.PousarNoGeradoDeFoto(cli.LocalId, seed, nome);
			if (!pousou) yield return 0.3;
		}
		yield return 6.0;
		Afirmar($"o corpo pousou no planeta gerado de {lado} tiles de lado (o maior e {MundoProcedural.LadoMaximo})",
				pousou && cli.Zone.Kind == ZoneKey.KindProcedural && M!.Colisao?.Width == lado && lado >= 900, $"{cli.Zone} {M!.Colisao?.Width}");
		mapa = MapaDaAba();
		for (int i = 0; i < 60 && mapa?.EstadoDeTeste != MapasDosLugares.Estado.Pronto; i++) { yield return 0.1; mapa = MapaDaAba(); }
		m = mapa?.MapaDeTeste;
		Afirmar($"o mapa do gerado fica PRONTO: {lado} x {lado} tiles numa imagem de {imagem}x{imagem} (blocos de 2x2), sem celula na reserva",
				mapa?.EstadoDeTeste == MapasDosLugares.Estado.Pronto && m is { NaReserva: 0 } && m.LarguraEmTiles == lado && m.AlturaEmTiles == lado
				&& m.Textura?.GetWidth() == imagem && Linha("Tamanho") == $"{lado} × {lado} tiles",
				m == null ? "sem mapa" : $"{m.LarguraEmTiles} tiles, {m.Textura?.GetWidth()} px, {m.NaReserva} na reserva, linha '{Linha("Tamanho")}'");
		Nota($"    custo no gerado de {lado}x{lado}: compor {m?.MsDeMontar:0.0} ms na thread de trabalho | subir pra placa {m?.MsDeSubir:0.00} ms na principal");
		if (mapa != null && M!.PosicaoLocal is { } noGerado)
		{
			(float u, float v, bool _) = MapaDoLugar.Marcador(new Vec2(noGerado.X, noGerado.Y), lado, lado);
			Vector2 la = mapa.RetanguloDeTeste.Position + new Vector2(u, v) * mapa.RetanguloDeTeste.Size;
			Afirmar("o marcador esta em cima do corpo tambem no gerado, e as coordenadas sao a celula dele",
					mapa.MarcadorDeTeste is { } mk4 && mk4.DistanceTo(la) <= 1.5f && Linha("Coordenadas") == MapaDoLugar.Coordenadas(new Vec2(noGerado.X, noGerado.Y)),
					$"{mapa.MarcadorDeTeste} x {la} | {Linha("Coordenadas")}");
		}
		yield return 0.4;
		Fotografar("mapa-02-gerado-1000");
		MapaDaAba()?.AlternarVista();
		yield return 0.4;
		mapa = MapaDaAba();
		Afirmar("no gerado, a vista de perto mostra os 128 tiles em volta do corpo (64 pixels da imagem, que esta a 2 tiles por pixel)",
				MapaDoLugarNaTela.DePertoDeTeste && mapa?.RecorteDeTeste is { } rg && Math.Abs(rg.Size.X * (m?.Bloco ?? 1) - MapaDoLugar.TilesDaVistaDePerto) < 0.01f,
				$"{mapa?.RecorteDeTeste} a {m?.Bloco} tile(s) por pixel");
		Fotografar("mapa-02b-gerado-de-perto");
		MapaDaAba()?.AlternarVista();
		yield return 0.2;
		foreach (object passo in MedirOQuadro(menu, $"o gerado ({lado}x{lado})")) yield return passo;

		// =====================================================================
		// C. NO ESPACO NAO HA MAPA
		// =====================================================================
		Nota("--- C. o espaco ---");
		srv.MoveToZone(cli.LocalId, srv.ZonaDoEspaco, new Vec2(123456f, -65432f));
		yield return 2.5;
		mapa = MapaDaAba();
		Afirmar("no espaco o quadro diz SEM MAPA, e a frase explica (a carta estelar e quem mostra os mundos)",
				mapa?.EstadoDeTeste == MapasDosLugares.Estado.SemMapa && mapa.MarcadorDeTeste == null
				&& Todos(menu.PaginaDeTeste("World")!).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text.StartsWith("Sem mapa aqui", StringComparison.Ordinal)),
				$"{mapa?.EstadoDeTeste}");
		Afirmar("...e as coordenadas continuam la: a celula do corpo no espaco, e o tamanho 'sem fim'",
				M!.PosicaoLocal is { } noEspaco && Linha("Coordenadas") == MapaDoLugar.Coordenadas(new Vec2(noEspaco.X, noEspaco.Y)) && Linha("Tamanho") == "sem fim",
				$"{Linha("Coordenadas")} | {Linha("Tamanho")}");
		Afirmar("...e a dica do clique some junto: onde nao ha mapa nao ha o que clicar",
				!Todos(menu.PaginaDeTeste("World")!).OfType<Label>().Any(l => l.IsVisibleInTree() && l.Text.StartsWith("Clique no mapa", StringComparison.Ordinal)));
		yield return 0.3;
		Fotografar("mapa-03-espaco");

		srv.MoveToZone(cli.LocalId, ZoneKey.Premade("Earth"), No(291, 97));
		yield return 5.0;
		menu.Fechar();
		yield return 0.5;

		// =====================================================================
		// R. O DISCO DO DRAGON RADAR
		// =====================================================================
		Nota("--- R. o disco do Dragon Radar ---");
		RadarNaTela? radar = RadarNaTela.Instancia;
		Afirmar("o HUD tem o node do disco", radar != null);
		if (radar == null) yield break;

		(float Dx, float Dy)[] onde =
		[
			(70 * T, -70 * T), (-90 * T, -30 * T), (-50 * T, 80 * T), (110 * T, 60 * T),   // NE, NO, SO, SE
			(0, -130 * T),                                                                 // ao norte
			(400 * T, 0),                                                                  // longe, a leste
			(0, 4 * T),                                                                    // logo ao sul (na tela)
		];
		List<Vec2> postas = srv.EsferasDeFoto(cli.LocalId, onde);
		Afirmar("(palco) sete esferas acordadas em volta do corpo", postas.Count == Esferas.Total, $"{postas.Count}");
		int pacotes = cli.PacotesDeRadarDeTeste;
		yield return 2.6;

		bool SemDisco() => !radar.Visible && !cli.RadarLigado;
		Afirmar("SEM o Dragon Radar: nenhum disco na tela, e nenhum pacote de radar chegou em 2,6 s (com sete esferas acordadas em volta)",
				SemDisco() && cli.PacotesDeRadarDeTeste == pacotes, $"visivel {radar.Visible}, ligado {cli.RadarLigado}, {cli.PacotesDeRadarDeTeste - pacotes} pacote(s)");
		srv.RadarSemPortaoDeTeste = true;
		yield return 2.4;
		bool reprovou = !SemDisco();
		srv.RadarSemPortaoDeTeste = false;
		yield return 2.4;
		Afirmar("(defeito injetado: o servidor manda a quem nao tem o radar) o disco APARECE e a checagem reprova; sem o defeito ele some de novo",
				reprovou && SemDisco());
		Fotografar("radar-00-sem-radar");

		srv.RadarDeFoto(cli.LocalId, true);
		yield return 2.6;
		Afirmar("COM o Dragon Radar na mochila o disco aparece, com sete sinais", radar.Visible && cli.RadarLigado && radar.PontosDeTeste.Count == Esferas.Total,
				$"visivel {radar.Visible}, {radar.PontosDeTeste.Count} ponto(s)");

		Vector2 tela = GetViewport().GetVisibleRect().Size;
		Rect2 caixa = radar.GetGlobalRect();
		Afirmar($"o disco ocupa {RadarNaTela.Diametro} px no canto INFERIOR DIREITO, a {RadarNaTela.Margem} px das bordas",
				Math.Abs(caixa.Size.X - RadarNaTela.Diametro) < 1 && Math.Abs(tela.X - caixa.End.X - RadarNaTela.Margem) < 1 && Math.Abs(tela.Y - caixa.End.Y - RadarNaTela.Margem) < 1,
				$"{caixa} numa tela de {tela}");

		bool NosLugares(out string detalhe)
		{
			detalhe = "";
			if (M!.PosicaoLocal is not { } p || radar.PontosDeTeste.Count != onde.Length) { detalhe = "sem pontos"; return false; }
			float alcance = radar.AlcanceDeTeste * (float)T;
			for (int i = 0; i < onde.Length; i++)
			{
				(float x, float y, bool naBorda) = RadarDeEsferas.NoDisco(postas[i].X - p.X, postas[i].Y - p.Y, alcance);
				Vector2 la = radar.Centro + new Vector2(x, y) * radar.RaioUtil;
				RadarNaTela.Ponto ponto = radar.PontosDeTeste[i];
				if (ponto.Onde.DistanceTo(la) > 1.5f || ponto.NaBorda != naBorda) { detalhe = $"esfera {i + 1}: {ponto.Onde} x {la}"; return false; }
			}
			RadarNaTela.Ponto ne = radar.PontosDeTeste[0], so = radar.PontosDeTeste[2], norte = radar.PontosDeTeste[4];
			if (!(ne.Onde.X > radar.Centro.X && ne.Onde.Y < radar.Centro.Y)) { detalhe = "a esfera do nordeste nao esta em cima a direita"; return false; }
			if (!(so.Onde.X < radar.Centro.X && so.Onde.Y > radar.Centro.Y)) { detalhe = "a esfera do sudoeste nao esta embaixo a esquerda"; return false; }
			if (!(Math.Abs(norte.Onde.X - radar.Centro.X) < 1.5f && norte.Onde.Y < radar.Centro.Y)) { detalhe = "a esfera do norte nao esta reta pra cima"; return false; }
			return true;
		}
		bool certo = NosLugares(out string porque);
		Afirmar("cada bolinha esta na direcao e na distancia da esfera dela: nordeste em cima a direita, sudoeste embaixo a esquerda, norte reto pra cima",
				certo, porque);
		RadarNaTela.Ponto longe = radar.PontosDeTeste.Count > 5 ? radar.PontosDeTeste[5] : default;
		Afirmar($"a esfera a 400 tiles (alcance de {radar.AlcanceDeTeste}) esta PRESA NA BORDA leste do disco",
				longe.NaBorda && Math.Abs(longe.Onde.X - (radar.Centro.X + radar.RaioUtil)) < 1.5f && Math.Abs(longe.Onde.Y - radar.Centro.Y) < 1.5f, longe.ToString());
		RadarDeEsferas.NorteParaBaixoDeTeste = true;
		yield return 0.2;
		bool reprovouONorte = !NosLugares(out string _);
		RadarDeEsferas.NorteParaBaixoDeTeste = false;
		yield return 0.2;
		Afirmar("(defeito injetado: o eixo Y do disco invertido) a checagem dos lugares REPROVA, e volta a passar sem ele", reprovouONorte && NosLugares(out string _));

		yield return 0.3;
		foto = Fotografar("radar-01-sete-esferas");
		if (foto != null)
		{
			int amarelas = radar.PontosDeTeste.Count(p => Perto(Pixel(foto, radar.GlobalPosition + p.Onde), RadarNaTela.Amarelo, 0.2f));
			Afirmar("NA FOTO: no lugar de cada sinal ha uma bolinha AMARELA", amarelas == Esferas.Total, $"{amarelas} de {Esferas.Total}");
			Color fundo = Pixel(foto, radar.GlobalPosition + radar.Centro + new Vector2(radar.RaioUtil * 0.5f, radar.RaioUtil * 0.62f));
			Afirmar("...e (contra-exemplo na mesma foto) um ponto vazio do disco NAO e amarelo: e o verde-escuro do aparelho",
					!Perto(fundo, RadarNaTela.Amarelo, 0.2f) && fundo.G > fundo.R && fundo.G > fundo.B, fundo.ToHtml());
		}

		// ---- o clique troca o alcance ----
		int antes = radar.AlcanceDeTeste;
		Vector2 centroNaTela = radar.GlobalPosition + radar.Centro + new Vector2(20, 24);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = centroNaTela, GlobalPosition = centroNaTela }, true);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = centroNaTela, GlobalPosition = centroNaTela }, true);
		yield return 0.4;
		Afirmar("um clique no disco troca o alcance (150 -> 500 tiles), e as bolinhas se recolhem pro centro: a de 400 tiles sai da borda",
				antes == 150 && radar.AlcanceDeTeste == 500 && NosLugares(out string _) && radar.PontosDeTeste.Count > 5 && !radar.PontosDeTeste[5].NaBorda,
				$"{antes} -> {radar.AlcanceDeTeste}");
		yield return 0.3;
		Fotografar("radar-02-alcance-500");
		radar.TrocarOAlcance();
		yield return 0.3;
		Afirmar("no alcance curto (50 tiles) as seis de longe ficam na borda e a de 4 tiles ao sul fica perto do centro",
				radar.AlcanceDeTeste == 50 && NosLugares(out string _) && radar.PontosDeTeste.Count(p => p.NaBorda) == 6
				&& radar.PontosDeTeste.Count > 6 && !radar.PontosDeTeste[6].NaBorda, $"{radar.PontosDeTeste.Count(p => p.NaBorda)} na borda");
		Fotografar("radar-03-alcance-50");
		radar.TrocarOAlcance();

		// ---- a esfera na mao ----
		srv.CarregarEsferaDeFoto(cli.LocalId, 7);
		yield return 2.6;
		Afirmar("a esfera que eu carrego vai pro CENTRO do disco (uma so marcada como minha), e as outras seis ficam onde estavam",
				radar.PontosDeTeste.Count(p => p.Comigo) == 1 && radar.PontosDeTeste.Any(p => p.Comigo && p.Onde.DistanceTo(radar.Centro) < 14f)
				&& radar.PontosDeTeste.Count == Esferas.Total, $"{radar.PontosDeTeste.Count(p => p.Comigo)} comigo");

		// ---- o convite do torneio divide o canto ----
		PainelDoTorneio? convite = Hud.Instancia == null ? null : Todos(Hud.Instancia).OfType<PainelDoTorneio>().FirstOrDefault();
		if (convite != null)
		{
			convite.Convidar(1, 30, "");
			yield return 0.4;
			Rect2 a = convite.GetGlobalRect(), b = radar.GetGlobalRect();
			Afirmar("com o disco na tela, o convite do torneio encosta a ESQUERDA dele: um nao tapa o outro", convite.Visible && !a.Intersects(b) && a.End.X <= b.Position.X, $"convite {a} | disco {b}");
			Fotografar("radar-04-com-o-convite-do-torneio");
			convite.Fechar();
		}
		else Afirmar("o HUD tem o convite do torneio", false);

		// ---- a janela de desejos mora no mesmo canto: com ela aberta o disco sai, e volta quando ela fecha ----
		cli.SendVerbo("db_menu");
		yield return 1.2;
		MenuDeDesejos? desejos = MenuDeDesejos.Instancia;
		Afirmar("'Ver os desejos' abre a janela de desejos, e com ela na tela o disco do radar SAI (em 720p ela o cobria)",
				desejos is { NaTela: true } && !radar.Visible, $"janela {desejos?.NaTela}, disco {radar.Visible}");
		bool discoComODefeito;
		RadarNaTela.DiscoPorBaixoDaJanelaDeTeste = true;
		try
		{
			yield return 0.3;
			discoComODefeito = radar.Visible;
		}
		finally { RadarNaTela.DiscoPorBaixoDaJanelaDeTeste = false; }
		desejos?.Fechar();
		yield return 0.4;
		Afirmar("(defeito injetado: o disco fica por baixo da janela) a mesma checagem reprova; e fechada a janela o disco VOLTA",
				discoComODefeito && radar.Visible && desejos is { NaTela: false }, $"com o defeito {discoComODefeito}, depois de fechar {radar.Visible}");

		// ---- perdeu o radar ----
		srv.RadarDeFoto(cli.LocalId, false);
		yield return 2.6;
		Afirmar("perdeu o radar: o disco SOME", SemDisco(), $"visivel {radar.Visible}, ligado {cli.RadarLigado}");
		if (convite != null)
		{
			convite.Convidar(1, 30, "");
			yield return 0.3;
			Afirmar("...e sem o disco o convite do torneio volta pro canto (14 px da borda)", Math.Abs(tela.X - convite.GetGlobalRect().End.X - 14) < 1, convite.GetGlobalRect().ToString());
			convite.Fechar();
		}
		Fotografar("radar-05-perdeu-o-radar");
		srv.LimparEsferasDeFoto();
	}

	/// <summary>
	/// O PIOR QUADRO DE ABRIR A ABA, tres jeitos: com a imagem guardada (o controle: so a pagina remonta), sem
	/// ela (a thread compoe) e com o defeito (compoe no quadro). Quatro repeticoes de cada, e vale o MENOR dos
	/// piores -- a maquina tem outras sessoes compilando, e um engasgo de fora nao e do mapa.
	/// </summary>
	private IEnumerable MedirOQuadro(MenuJogo menu, string onde)
	{
		double[] menor = [double.MaxValue, double.MaxValue, double.MaxValue];
		for (int modo = 0; modo < 3; modo++)
			for (int rep = 0; rep < 4; rep++)
			{
				yield return 0.3;
				// ESQUECER E REABRIR NO MESMO PASSO: com a pagina de pe, o proprio quadro do mapa pediria a imagem
				// de novo no quadro seguinte -- e a composicao cairia fora da janela medida.
				MapasDosLugares.NoQuadroDeTeste = modo == 2;
				if (modo != 0) MapasDosLugares.EsquecerDeTeste();
				_piorQuadro = 0;
				menu.ForcarRedesenho();
				yield return 0.7;
				menor[modo] = Math.Min(menor[modo], _piorQuadro);
				MapasDosLugares.NoQuadroDeTeste = false;
			}

		double quadro = Mediana(_quadros);
		Nota($"    pior quadro ao abrir a aba em {onde} (menor de 4; o quadro comum aqui e {quadro:0.0} ms): "
			 + $"imagem guardada {menor[0]:0.0} ms | compondo na THREAD {menor[1]:0.0} ms | compondo NO QUADRO (defeito) {menor[2]:0.0} ms");
		Afirmar($"abrir a aba em {onde} compondo o mapa NAO custa quadro: o pior quadro fica a menos de 6 ms do de uma abertura com a imagem guardada",
				menor[1] <= menor[0] + 6.0, $"{menor[1]:0.0} ms contra {menor[0]:0.0} ms");
		Afirmar("(defeito injetado: a imagem composta na thread principal) o engasgo APARECE: o pior quadro passa em mais de 6 ms o do controle",
				menor[2] > menor[0] + 6.0, $"{menor[2]:0.0} ms contra {menor[0]:0.0} ms");
	}
}
