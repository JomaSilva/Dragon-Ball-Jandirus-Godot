using System.Collections;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.World;
using Jandirus.Net;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA LENHA (`--diaglenha`) -- a metade do CLIENTE do pedido do dono de 2026-10-10: *"derrubar arvores no
/// soco [...] essas arvores dps de um tempo voltam ou spawnam em outro local."* As regras (quem paga, quanto, quando
/// volta, onde) sao da `--lenhateste`, no servidor; aqui se mede o que o JOGADOR ve e em que ele esbarra.
///
/// O que se afirma e lido de DOIS lados -- o servidor (no modo host, pela superficie de bancada do
/// `GameServer.Lenha.cs`) e a tela: o tile na cena, o tile na camada das rebrotas, o TOCO desenhado, o mapa de
/// colisao do cliente, o chat, e (com janela) a FOTO.
///
///   1. o SOCO DE VERDADE (o pacote de golpe do cliente) derruba a arvore: terra batida, colisao aberta, "+N
///      recursos" -- e fica um TOCO no lugar, tirado da arte dela, que nao barra o passo;
///   2. vencido o prazo, a arvore VOLTA (mesmo tile, numa camada propria, barrando nas duas pontas) e o toco some;
///   3. NA FOTO, a copa da que voltou e a mesma da que o mapa trazia (mesmo tile, mesma ancora, mesma ordem por Y);
///   4. a rebrota cai a soco, com poeira, paga de novo e deixa toco de novo;
///   5. o contra-exemplo: a rebrota que e so desenho (`RebrotasNaTela.SoDesenhaDeTeste`) nao barra;
///   6. QUEM CHEGA DEPOIS: ida a outro planeta e volta -- la nao sobra arvore nem toco do anterior, e na volta a
///      rebrota e o toco chegam no retrato com a cena vindo do cache;
///   7. LUGAR OCUPADO: a maquina posta em cima do toco faz o toco sumir, BARRA o corpo nas duas pontas (a celula e
///      parede do arquivo aberta pela queda -- o contra-exemplo e o `ZoneCollision.CelulaAbertaIgnoraObraDeTeste`),
///      e a arvore nasce noutra celula;
///   8. refeito o cenario pelo admin, a arvore do mapa volta pra cena e nao sobra rebrota nem toco;
///   9. EM ICER: a arvore branca (de folha de turf) deixa um toco DELA -- de gelo, e nao o do carvalho da Terra. O
///      contra-exemplo e o `TocosNaTela.UmaArteSoDeTeste`;
///  10. NUM PLANETA SORTEADO (o caso do pedido: o novato que cai num mundo gerado, 2x mais pesado que ele): a arvore
///      que o GERADOR pos cai e deixa toco, e o corpo ESMAGADO nao recebe -- ouvindo falar da gravidade, e nao de
///      briga (dono: "sim conta como combate"). Com a constante `Lenha.EsmagadoContaComoBriga` invertida ele recebe;
///      aclimatado, recebe.
///
/// Rode com janela (no segundo monitor) pra ter as fotos; headless ele mede tudo menos o que e pixel:
///   --host --rede 8151 --campoteste 8 --horateste 0.5 --bpteste 3000000 --diaglenha --raca Human --conta bancada_dlenha --nome Lenhador
/// </summary>
public partial class RoboDeLenha : Node
{
	private const int T = ZoneCollision.TileSize;

	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [];
	private readonly List<string> _falas = [];

	/// <summary>Quantas vezes o chat avisou que a gravidade esta esmagando o corpo (o aviso de 10 em 10 s do servidor).</summary>
	private int _rangeuSobAGravidade;

	/// <summary>Quantos socos o ultimo <see cref="SocarAte"/> deu.</summary>
	private int _socos;

	/// <summary>
	/// SEM TELA, A FOLHA PODE NAO TER LEITURA DE CPU: ai nenhum toco tem desenho, e so a LISTA julga. Descoberto no
	/// primeiro toco e dito uma vez; com janela isto nunca liga -- la o toco TEM de estar desenhado.
	/// </summary>
	private bool _semArte;

	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;

	private static GameClient? C => GameClient.Instance;
	private static World? M => World.Instancia;
	private static GameServer? S => GameServer.Instance;
	private static bool TemTela => DisplayServer.GetName() != "headless";

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dlenha]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[dlenha]   FALHA {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[dlenha] " + s);

	public override void _Ready()
	{
		if (C is { } cli) cli.Falou += Ouviu;
	}

	public override void _ExitTree()
	{
		DesligarOsDefeitos();
		if (C is { } cli) cli.Falou -= Ouviu;
	}

	private static void DesligarOsDefeitos()
	{
		RebrotasNaTela.SoDesenhaDeTeste = false;
		TocosNaTela.UmaArteSoDeTeste = false;
		Lenha.EsmagadoContaComoBriga = true;
		ZoneCollision.CelulaAbertaIgnoraObraDeTeste = false;
	}

	private void Ouviu(Protocol.Fala canal, string autor, string texto)
	{
		_falas.Add(texto);
		if (texto.Contains("sob a gravidade")) _rangeuSobAGravidade++;
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } || M?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext()) { Fim(); return; }
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fim()
	{
		_acabou = true;
		DesligarOsDefeitos();
		GD.Print($"\n[dlenha] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[dlenha] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}

	// =====================================================================
	// O QUE A TELA TEM
	// =====================================================================

	/// <summary>A CENA da zona desenha este tile nesta celula, em alguma camada dela?</summary>
	private static bool NaCena(World mundo, int cx, int cy, TileDeArvore tile)
	{
		var cel = new Vector2I(cx, cy);
		foreach (TileMapLayer camada in mundo.CamadasDoCenarioDeTeste)
			if (GodotObject.IsInstanceValid(camada) && camada.GetCellSourceId(cel) == tile.Fonte
				&& camada.GetCellAtlasCoords(cel) == new Vector2I(tile.Ax, tile.Ay)) return true;
		return false;
	}

	/// <summary>A camada de chao da cena mostra terra batida (o `Ground8` do estrago) nesta celula?</summary>
	private static bool TerraBatida(World mundo, int cx, int cy)
	{
		TileMapLayer[] camadas = mundo.CamadasDoCenarioDeTeste;
		var cel = new Vector2I(cx, cy);
		return mundo.ChaoDestruidoDeTeste is { } g && camadas.Length > 0 && GodotObject.IsInstanceValid(camadas[0])
			   && camadas[0].GetCellSourceId(cel) == g.Fonte && camadas[0].GetCellAtlasCoords(cel) == g.Coord;
	}

	private static bool NaFaixa(double recursos) => recursos >= Lenha.RecursosMinimos && recursos <= Lenha.RecursosMaximos;

	/// <summary>Quantas rebrotas a camada delas tem pintadas (-1 = a camada nao existe).</summary>
	private static int Pintadas => RebrotasNaTela.Instancia?.PintadasDeTeste ?? -1;

	/// <summary>O tile de rebrota pintado nesta celula, ou nulo.</summary>
	private static (int Fonte, Vector2I Coord)? Pintada(int cx, int cy) => RebrotasNaTela.Instancia?.PintadaDeTeste(cx, cy);

	/// <summary>O toco desenhado nesta celula, ou nulo. Ver `TocosNaTela.PintadoDeTeste`.</summary>
	private static (Vector2I Tamanho, Vector2 Canto, Color Media, bool Sintetico, int AlturaDoCorte)? Toco(int cx, int cy) =>
		TocosNaTela.Instancia?.PintadoDeTeste(cx, cy);

	/// <summary>Quantos tocos estao desenhados (-1 = o node nao existe).</summary>
	private static int TocosPintados => TocosNaTela.Instancia?.PintadosDeTeste ?? -1;

	/// <summary>
	/// HA UM TOCO NESTA CELULA, NA LISTA E NA TELA? Com janela o desenho e obrigatorio. Sem tela, se a folha nao deu
	/// leitura (descobre-se no primeiro toco), julga so a lista -- e diz isso uma vez.
	/// </summary>
	private bool TemToco(GameClient cli, int cx, int cy, TileDeArvore daArvore)
	{
		if (!cli.Tocos.TryGetValue((cx, cy), out TileDeArvore t) || t != daArvore) return false;
		if (Toco(cx, cy) != null) return true;
		if (TemTela || !daArvore.TemDesenho) return !daArvore.TemDesenho;
		if (!_semArte)
		{
			_semArte = true;
			Nota("sem tela a folha da arvore nao deu leitura de CPU: os tocos existem na lista e nao tem desenho -- o desenho e da rodada com janela");
		}
		return true;
	}

	/// <summary>Nao ha toco nesta celula, nem na lista nem na tela.</summary>
	private static bool SemToco(GameClient cli, int cx, int cy) => !cli.Tocos.ContainsKey((cx, cy)) && Toco(cx, cy) == null;

	private Image? Fotografar(string nome)
	{
		// SEM TELA NAO SE PERGUNTA: o renderizador de mentira do headless devolve vazio E escreve um erro do motor no
		// log a cada `GetImage` -- quatro linhas vermelhas numa rodada em que nada falhou.
		if (!TemTela) { Nota($"sem a foto {nome} (headless nao renderiza): rode com janela"); return null; }

		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem a foto {nome}: a tela veio vazia"); return null; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
		return img;
	}

	/// <summary>
	/// QUANTO DUAS FOTOS SE PARECEM dentro de uma celula do mundo: a fracao de pixels iguais (com folga de 4/255 por
	/// canal). -1 = a celula esta fora da foto.
	/// </summary>
	private double Iguais(Image a, Image b, int cx, int cy)
	{
		Viewport v = GetViewport();
		Vector2 p0 = v.GetFinalTransform() * (v.CanvasTransform * new Vector2(cx * T, cy * T));
		Vector2 p1 = v.GetFinalTransform() * (v.CanvasTransform * new Vector2((cx + 1) * T, (cy + 1) * T));
		int x0 = Math.Max(0, (int)MathF.Ceiling(Math.Min(p0.X, p1.X))), y0 = Math.Max(0, (int)MathF.Ceiling(Math.Min(p0.Y, p1.Y)));
		int x1 = Math.Min(Math.Min(a.GetWidth(), b.GetWidth()), (int)MathF.Floor(Math.Max(p0.X, p1.X)));
		int y1 = Math.Min(Math.Min(a.GetHeight(), b.GetHeight()), (int)MathF.Floor(Math.Max(p0.Y, p1.Y)));
		if (x1 - x0 < 4 || y1 - y0 < 4) return -1;

		int iguais = 0, total = 0;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
				total++;
				if (MathF.Abs(ca.R - cb.R) <= 4 / 255f && MathF.Abs(ca.G - cb.G) <= 4 / 255f && MathF.Abs(ca.B - cb.B) <= 4 / 255f) iguais++;
			}
		return total == 0 ? -1 : iguais / (double)total;
	}

	/// <summary>
	/// A COPA, em tres fotos -- a arvore que estava la, o toco, e a que voltou. So julga se ali havia copa (a foto do
	/// toco difere da de antes): arvore baixa nao tem o que comparar em cima.
	///
	/// A CELULA OLHADA E A SEGUNDA ACIMA DO TRONCO, e nao a primeira: a queda deixa terra revirada nas oito vizinhas
	/// (`World.ChaoDanificadoEmVolta`, que e estado do mapa e fica), e ela aparece pelos vaos da copa e debaixo da
	/// sombra da arvore que voltou. Medido: na vizinha de cima a foto dava 92% igual com a arvore identica. Duas
	/// celulas acima so ha copa e o chao que a queda nao tocou.
	/// </summary>
	private void JulgarACopa(string onde, Image? antes, Image? toco, Image? depois, int cx, int cy)
	{
		if (antes == null || toco == null || depois == null) return;

		double semArvore = Iguais(antes, toco, cx, cy - 2), deVolta = Iguais(antes, depois, cx, cy - 2);
		if (semArvore >= 0 && semArvore < 0.8)
			Afirmar($"NA FOTO ({onde}), a copa da arvore que voltou e a mesma da que estava la (mesmo tile, mesma ancora, mesma ordem de desenho)",
					deVolta >= 0.97, $"duas celulas acima do tronco: {deVolta * 100:0.0}% igual a de antes (sem a arvore: {semArvore * 100:0.0}%)");
		else Nota($"{onde}: a copa da arvore escolhida nao chega a duas celulas acima do tronco ({semArvore * 100:0.0}% igual sem ela): a foto nao julga");
	}

	/// <summary>
	/// NA FOTO, O TOCO APARECE -- e so no pe da arvore. Fotografa de novo com o node dos tocos ESCONDIDO e compara:
	/// a celula do tronco tem de mudar (o desenho esta na tela, e nao so na lista), e duas celulas acima nada pode
	/// mudar (o toco e baixo: nao e a arvore inteira, nem um recorte fora do lugar). Sem janela nao ha o que medir.
	/// </summary>
	private IEnumerable JulgarOTocoNaFoto(string onde, Image? comToco, int cx, int cy)
	{
		if (comToco == null || TocosNaTela.Instancia is not { } tocos) yield break;

		tocos.Visible = false;
		yield return 0.3;
		Image? semToco = GetViewport()?.GetTexture()?.GetImage();
		tocos.Visible = true;
		yield return 0.2;
		if (semToco == null || semToco.IsEmpty()) yield break;

		double noPe = Iguais(comToco, semToco, cx, cy), noAlto = Iguais(comToco, semToco, cx, cy - 2);
		Afirmar($"NA FOTO ({onde}), O TOCO APARECE no pe da arvore: escondido o node dos tocos a celula do tronco muda, e duas celulas acima nada muda (ele e baixo)",
				noPe >= 0 && noPe < 0.985 && noAlto >= 0.999,
				$"celula do tronco {noPe * 100:0.0}% igual com e sem o toco; duas acima {noAlto * 100:0.0}%");
	}

	/// <summary>
	/// O MUNDO SORTEADO DO ROBO: o primeiro Jardim de gravidade leve e de lado pequeno da seed de fabrica -- o mesmo
	/// criterio do robo das fotos do procedural (`RoboDeFotoDoProcedural`). Funcao pura: toda rodada acha o mesmo.
	/// </summary>
	private static PlanetaNoEspaco? MundoSorteado()
	{
		for (int i = 2; i <= 400; i++)
		{
			if (Sistemas.Do(GameServer.SementeLegada, i * 2, i * 3 + 1) is not { Ancorado: false } sistema) continue;
			foreach (PlanetaNoEspaco p in sistema.Planetas())
			{
				if (p.Premade) continue;
				MundoProcedural m = MundoProcedural.DaSeed(p.Seed, p.Nome);
				if (m.Bioma == BiomaDeTerreno.Jardim && m.Gravidade <= 2 && m.Lado <= 256) return p;
			}
		}
		return null;
	}

	// =====================================================================
	// OS GESTOS
	// =====================================================================

	/// <summary>Soca (o pacote de golpe leve do cliente) ate a condicao valer, no maximo seis vezes. Deixa a conta em <see cref="_socos"/>.</summary>
	private IEnumerable SocarAte(GameClient cli, Func<bool> pronto)
	{
		_socos = 0;
		while (_socos < 6 && !pronto())
		{
			cli.SendAction(Protocol.Golpe.Leve);
			_socos++;
			yield return 0.9;
		}
	}

	/// <summary>DIA NESTE PLANETA: a manivela e a da Terra, e cada mundo tem a defasagem dele (o passo dos robos de foto).</summary>
	private static IEnumerable FazerDia(GameServer srv, World mundo)
	{
		for (int i = 0; i < 20; i++)
		{
			double daqui = mundo.Ceu is { } ceu ? ceu.Hora - Math.Floor(ceu.Hora) : -1;
			if (daqui is >= 0.42 and <= 0.6) yield break;
			srv.CeuDaTerraDeTeste(i / 20.0);
			yield return 0.35;
		}
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		GameClient cli = C!;
		World mundo = M!;
		if (S is not { } srv) { Afirmar("(montagem) o robo roda no modo host, com o servidor no mesmo processo", false); yield break; }

		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);
		yield return 1.0;

		ZoneKey zona = cli.Zone;
		int eu = cli.LocalId;
		Vector2 aqui = mundo.PosicaoLocal!.Value;
		Afirmar("(montagem) o robo esta num planeta (so planeta tem lenha)", Espaco.EhPlaneta(zona), zona.ToString());

		// UMA ARVORE SOZINHA: sem outra a menos de tres celulas, pra a copa de uma vizinha nao entrar na foto.
		(int X, int Y, TileDeArvore Tile)? achada = null;
		var vistas = new HashSet<(int X, int Y)>();
		for (int tentativa = 0; tentativa < 60 && achada == null; tentativa++)
		{
			if (srv.ArvoreSocavelPertoDeTeste(zona, new Vec2(aqui.X, aqui.Y), vistas) is not { } c) break;
			vistas.Add((c.X, c.Y));
			bool sozinha = true;
			for (int dy = -3; dy <= 3 && sozinha; dy++)
				for (int dx = -3; dx <= 3 && sozinha; dx++)
					if ((dx != 0 || dy != 0) && srv.ArvoreDePeDeTeste(zona, c.X + dx, c.Y + dy)) sozinha = false;
			if (sozinha) achada = c;
		}
		Afirmar("(montagem) ha uma arvore do mapa sozinha (sem outra a tres celulas) e socavel perto do robo", achada != null);
		if (achada is not { } arvore) yield break;
		(int ax, int ay, TileDeArvore tile) = arvore;
		Nota($"a arvore: celula ({ax},{ay}), tile {tile.Fonte}[{tile.Ax},{tile.Ay}]");

		srv.AoPeDaArvoreDeTeste(eu, ax, ay);
		yield return 2.0;

		// ------------------------------------------------------------ 1) o soco de verdade
		Nota("-- 1) o soco de verdade derruba a arvore do mapa, e fica o toco --");
		Afirmar("(montagem) a arvore esta desenhada na CENA com o tile que o servidor diz que ela tem, e barra nas duas pontas",
				NaCena(mundo, ax, ay, tile) && mundo.Colisao?.BlockedCell(ax, ay) == true && srv.BarraDeTeste(zona, ax, ay)
				&& Pintadas == 0 && cli.Rebrotas.Count == 0 && cli.Tocos.Count == 0 && TocosPintados == 0);
		Image? fotoDoMapa = Fotografar("lenha-01-a-arvore-do-mapa");

		double saldo = srv.RecursosDeTeste(eu);
		_falas.Clear();
		foreach (object passo in SocarAte(cli, () => cli.CenarioCaido.Contains((ax, ay)))) yield return passo;
		Afirmar("**O SOCO DE VERDADE** (o pacote de golpe do cliente, sem ninguem na frente) derruba a arvore: a celula entra no estrago da zona",
				cli.CenarioCaido.Contains((ax, ay)), $"{_socos} soco(s)");
		Afirmar("...a cena apaga o tile dela e pinta terra batida, e a colisao abre nas duas pontas",
				!NaCena(mundo, ax, ay, tile) && TerraBatida(mundo, ax, ay) && mundo.Colisao?.BlockedCell(ax, ay) == false
				&& !srv.BarraDeTeste(zona, ax, ay));
		double rendeu = srv.RecursosDeTeste(eu) - saldo;
		Afirmar("...e quem derrubou recebe e FICA SABENDO: o saldo sobe dentro da faixa, e o chat diz \"+N recursos\"",
				NaFaixa(rendeu) && _falas.Exists(f => f.Contains($"+{rendeu:0} recursos")), $"+{rendeu:0.##} | {string.Join(" | ", _falas)}");

		Afirmar("**FICA UM TOCO NO LUGAR DELA**: o cliente recebe o toco desta celula com o tile DA ARVORE que caiu, e o desenha -- sem barrar: a celula continua aberta nas duas pontas",
				TemToco(cli, ax, ay, tile) && srv.TocoAVistaDeTeste(zona, ax, ay) && cli.Tocos.Count == 1
				&& mundo.Colisao?.BlockedCell(ax, ay) == false && !srv.BarraDeTeste(zona, ax, ay),
				$"{cli.Tocos.Count} na lista, {TocosPintados} desenhado(s)");
		Color mediaDaTerra = Colors.Transparent;
		if (Toco(ax, ay) is { } t1)
		{
			mediaDaTerra = t1.Media;
			// O PE DO TOCO E O PE DA ARVORE: o desenho acaba dentro da celula do tronco, e e baixo (a arvore tem
			// quatro tiles e meio de altura; o toco, menos de um e meio).
			float pe = t1.Canto.Y + t1.Tamanho.Y;
			Afirmar("...o toco sai da ARTE dela: baixo, com o pe dentro da celula do tronco (onde estava o pe da arvore), e nao e o sintetico",
					!t1.Sintetico && t1.Tamanho.Y is >= 4 and <= 48 && t1.Tamanho.X >= 3 && pe > -T / 2f && pe <= T / 2f + 2
					&& t1.Canto.Y > -T * 1.5f,
					$"{t1.Tamanho.X}x{t1.Tamanho.Y} px, canto {t1.Canto}, corte a {t1.AlturaDoCorte} linhas da base, cor media {t1.Media.ToHtml(false)}");
			Nota($"derivar a arte de um toco custou no maximo {TocosNaTela.MaiorDerivacaoMsDeTeste:0.00} ms (uma leitura de folha + a conta)");
		}
		yield return 2.5;   // a poeira da queda assenta antes da foto
		Image? fotoDoToco = Fotografar("lenha-02-o-toco");
		foreach (object passo in JulgarOTocoNaFoto("na Terra", fotoDoToco, ax, ay)) yield return passo;

		// ------------------------------------------------------------ 2) a volta
		Nota("-- 2) vencido o prazo, a arvore volta e o toco some --");
		srv.VencerALenhaDeTeste(zona);
		yield return 1.0;
		Afirmar("**A ARVORE VOLTA**: o cliente recebe a rebrota desta celula, com o tile da arvore que caiu",
				cli.Rebrotas.TryGetValue((ax, ay), out TileDeArvore chegou) && chegou == tile && srv.RebrotaDaOrigemDeTeste(zona, ax, ay) == (ax, ay));
		Afirmar("...e a DESENHA com esse tile numa camada propria -- a cena continua com a terra batida por baixo",
				Pintada(ax, ay) is { } d1 && d1.Fonte == tile.Fonte && d1.Coord == new Vector2I(tile.Ax, tile.Ay)
				&& !NaCena(mundo, ax, ay, tile) && TerraBatida(mundo, ax, ay) && cli.CenarioCaido.Contains((ax, ay)));
		Afirmar("...e ela BARRA: o mapa de colisao do cliente e o do servidor dizem parede na mesma celula",
				mundo.Colisao?.BlockedCell(ax, ay) == true && mundo.Colisao?.Rebrotada(ax, ay) == true && srv.BarraDeTeste(zona, ax, ay));
		Afirmar("**...E O TOCO SOME** quando a arvore rebrota: sai da lista e da tela", SemToco(cli, ax, ay) && TocosPintados == 0 && !srv.TocoAVistaDeTeste(zona, ax, ay));
		Image? fotoDaRebrota = Fotografar("lenha-03-a-rebrota");

		JulgarACopa("no mapa pre-feito", fotoDoMapa, fotoDoToco, fotoDaRebrota, ax, ay);

		// ------------------------------------------------------------ 3) a rebrota cai a soco
		Nota("-- 3) a rebrota cai a soco, paga de novo e deixa toco de novo --");
		int poeira = PoeiraDeEstrago.PedidosDeTeste;
		saldo = srv.RecursosDeTeste(eu);
		_falas.Clear();
		foreach (object passo in SocarAte(cli, () => !cli.Rebrotas.ContainsKey((ax, ay)))) yield return passo;
		Afirmar("a REBROTA cai pelo soco de verdade: sai da lista, da camada e da colisao do cliente -- com poeira",
				!cli.Rebrotas.ContainsKey((ax, ay)) && Pintada(ax, ay) == null && mundo.Colisao?.BlockedCell(ax, ay) == false
				&& !srv.BarraDeTeste(zona, ax, ay) && PoeiraDeEstrago.PedidosDeTeste > poeira, $"{_socos} soco(s), poeira {poeira} -> {PoeiraDeEstrago.PedidosDeTeste}");
		rendeu = srv.RecursosDeTeste(eu) - saldo;
		Afirmar("...e paga de novo, dentro da faixa", NaFaixa(rendeu) && _falas.Exists(f => f.Contains($"+{rendeu:0} recursos")), $"+{rendeu:0.##}");
		Afirmar("...e o toco esta de volta no lugar dela", TemToco(cli, ax, ay, tile) && cli.Tocos.Count == 1);

		// ------------------------------------------------------------ 4) o contra-exemplo do cliente
		Nota("-- 4) a rebrota que e so desenho --");
		srv.VencerALenhaDeTeste(zona);
		yield return 1.0;
		RebrotasNaTela.SoDesenhaDeTeste = true;
		RebrotasNaTela.Instancia?.RemontarDeTeste();
		Afirmar("(defeito injetado: a rebrota e so desenho) ela aparece e o mapa do cliente NAO barra -- o corpo andaria pra dentro de onde o servidor ve parede",
				Pintada(ax, ay) != null && mundo.Colisao?.BlockedCell(ax, ay) == false && srv.BarraDeTeste(zona, ax, ay));
		RebrotasNaTela.SoDesenhaDeTeste = false;
		RebrotasNaTela.Instancia?.RemontarDeTeste();
		Afirmar("sem o defeito, reaplicar a lista devolve a parede (e nao duplica desenho)",
				mundo.Colisao?.BlockedCell(ax, ay) == true && Pintadas == cli.Rebrotas.Count);

		// ------------------------------------------------------------ 5) quem chega depois
		// Com os DOIS estados na zona: a rebrota da primeira arvore de pe, e o toco de uma segunda, derrubada agora.
		Nota("-- 5) outro planeta, e a volta: a rebrota e o toco chegam no retrato --");
		Vector2 ponto = mundo.PosicaoLocal!.Value;
		vistas.Add((ax, ay));
		if (srv.ArvoreSocavelPertoDeTeste(zona, new Vec2(ponto.X, ponto.Y), vistas) is not { } segunda)
		{
			Afirmar("(montagem) ha uma segunda arvore socavel perto", false);
			yield break;
		}
		(int bx, int by, TileDeArvore btile) = segunda;
		srv.AoPeDaArvoreDeTeste(eu, bx, by);
		yield return 1.5;
		foreach (object passo in SocarAte(cli, () => cli.CenarioCaido.Contains((bx, by)))) yield return passo;
		Afirmar("(montagem) a segunda arvore caiu e deixou o toco dela; a primeira esta de pe, rebrotada",
				TemToco(cli, bx, by, btile) && cli.Rebrotas.ContainsKey((ax, ay)) && cli.Tocos.Count == 1, $"segunda: ({bx},{by})");

		ZoneKey outra = ZoneKey.Premade(zona.Name == "Namek" ? "Earth" : "Namek");
		srv.ViajarDeTeste(eu, outra);
		for (int i = 0; i < 80 && mundo.ZonaMontadaDeTeste != outra.Hash; i++) yield return 0.25;
		yield return 1.5;
		Afirmar($"em {outra.Name}: as listas sao DAQUELA zona e estao vazias, e nenhuma arvore nem toco do planeta anterior ficou desenhado",
				mundo.ZonaMontadaDeTeste == outra.Hash && cli.ZonaDaLenha == outra.Hash && cli.Rebrotas.Count == 0 && cli.Tocos.Count == 0
				&& Pintadas == 0 && TocosPintados == 0,
				$"{cli.Rebrotas.Count} rebrota(s) e {cli.Tocos.Count} toco(s) na lista; {Pintadas} e {TocosPintados} desenhado(s)");

		srv.ViajarDeTeste(eu, zona, new Vec2(ponto.X, ponto.Y));
		for (int i = 0; i < 80 && mundo.ZonaMontadaDeTeste != zona.Hash; i++) yield return 0.25;
		yield return 2.0;
		Afirmar("**DE VOLTA (quem chega depois)**: a rebrota vem no retrato, esta desenhada e barra -- com a cena da zona vindo do cache",
				mundo.ZonaMontadaDeTeste == zona.Hash && cli.ZonaDaLenha == zona.Hash && cli.Rebrotas.ContainsKey((ax, ay))
				&& Pintada(ax, ay) != null && mundo.Colisao?.BlockedCell(ax, ay) == true);
		Afirmar("...por cima da terra batida, que tambem veio (o estrago da cena continua la)",
				cli.CenarioCaido.Contains((ax, ay)) && TerraBatida(mundo, ax, ay) && !NaCena(mundo, ax, ay, tile));
		Afirmar("**...E O TOCO TAMBEM VEM NO RETRATO**: quem chega depois ve o toco da arvore que ainda nao voltou, com a arte dela",
				TemToco(cli, bx, by, btile) && cli.Tocos.Count == 1 && (_semArte || TocosPintados == 1),
				$"{cli.Tocos.Count} na lista, {TocosPintados} desenhado(s)");

		// ------------------------------------------------------------ 6) lugar ocupado
		Nota("-- 6) lugar ocupado: o toco some debaixo da maquina, e a arvore nasce noutra celula --");
		srv.DerrubarCelulaDeTeste(zona, ax, ay);
		yield return 0.8;
		Afirmar("derrubada por FORA do soco (o funil do arremesso e do chao que racha), a rebrota some da tela e da colisao -- e deixa o toco, como a que cai a soco",
				Pintada(ax, ay) == null && mundo.Colisao?.BlockedCell(ax, ay) == false && !cli.Rebrotas.ContainsKey((ax, ay))
				&& TemToco(cli, ax, ay, tile));

		int maquina = srv.OcuparComMaquinaDeTeste(zona, ax, ay);
		yield return 1.8;   // a passada da lenha e de um em um segundo: o zelador ve a maquina
		Afirmar("**CONSTRUIRAM EM CIMA: O TOCO SOME** (da lista e da tela), e a arvore continua caida, com o prazo dela",
				SemToco(cli, ax, ay) && !srv.TocoAVistaDeTeste(zona, ax, ay) && srv.TocoDeTeste(zona, ax, ay) && TemToco(cli, bx, by, btile),
				$"{cli.Tocos.Count} na lista, {TocosPintados} desenhado(s)");

		// A MAQUINA NO LUGAR DA ARVORE CAIDA BARRA, NAS DUAS PONTAS. A celula e parede no arquivo e foi aberta pela queda:
		// ate 2026-10-10 o `BlockedCell` respondia "livre" ali sem olhar a obra, e o corpo atravessava o desenho.
		Afirmar("**A MAQUINA ASSENTADA NO TOCO BARRA NAS DUAS PONTAS**: o mapa de colisao do cliente e o do servidor dizem parede na celula dela",
				mundo.Colisao?.BlockedCell(ax, ay) == true && mundo.Colisao?.BloqueadaNoArquivo(ax, ay) == true
				&& mundo.Colisao?.Aberta(ax, ay) == true && srv.BarraDeTeste(zona, ax, ay));
		ZoneCollision.CelulaAbertaIgnoraObraDeTeste = true;
		bool atravessaria = mundo.Colisao?.BlockedCell(ax, ay) == false && !srv.BarraDeTeste(zona, ax, ay);
		ZoneCollision.CelulaAbertaIgnoraObraDeTeste = false;
		Afirmar("(defeito injetado: a celula aberta nao olha a obra) a maquina esta la, desenhada, e NAO barra em ponta nenhuma -- o corpo a atravessaria", atravessaria);

		// O QUE ESTAVA EM CIMA SAI ANTES DE A ARVORE VOLTAR: O TOCO VOLTA PRA TELA. A maquina sai, o zelador ve o chao
		// livre na passada seguinte e o servidor manda o toco de novo; posta outra vez, ele some outra vez.
		srv.TirarMaquinaDeTeste(maquina);
		yield return 1.8;
		bool voltouPraTela = TemToco(cli, ax, ay, tile) && srv.TocoAVistaDeTeste(zona, ax, ay) && mundo.Colisao?.BlockedCell(ax, ay) == false;
		maquina = srv.OcuparComMaquinaDeTeste(zona, ax, ay);
		yield return 1.8;
		Afirmar("**TIRARAM A MAQUINA DE CIMA: O TOCO VOLTA** (lista e tela) enquanto a arvore nao rebrota -- e posta de novo, ele some de novo",
				voltouPraTela && SemToco(cli, ax, ay) && !srv.TocoAVistaDeTeste(zona, ax, ay) && srv.TocoDeTeste(zona, ax, ay),
				$"voltou {voltouPraTela}; com a maquina de volta, {cli.Tocos.Count} na lista e {TocosPintados} desenhado(s)");

		srv.VencerALenhaDeTeste(zona);
		yield return 1.0;
		(int X, int Y)? nova = srv.RebrotaDaOrigemDeTeste(zona, ax, ay);
		Afirmar("**LUGAR OCUPADO**: com uma maquina em cima do lugar a arvore nasce NOUTRA celula -- o cliente a desenha la, com o mesmo tile, e barra la",
				nova is { } n1 && n1 != (ax, ay) && Pintada(n1.X, n1.Y) is { } d2 && d2.Fonte == tile.Fonte
				&& d2.Coord == new Vector2I(tile.Ax, tile.Ay) && mundo.Colisao?.BlockedCell(n1.X, n1.Y) == true && srv.BarraDeTeste(zona, n1.X, n1.Y),
				$"origem ({ax},{ay}) -> {nova}");
		Afirmar("...e no lugar de origem nao ha arvore nenhuma (so a maquina)", Pintada(ax, ay) == null);
		yield return 0.5;
		Fotografar("lenha-04-nasceu-noutro-lugar");
		srv.TirarMaquinaDeTeste(maquina);

		// ------------------------------------------------------------ 7) o cenario refeito
		Nota("-- 7) o admin refaz o cenario --");
		srv.ConsertarCenarioDeTeste(eu);
		yield return 3.5;
		Afirmar("refeito o cenario, a arvore do MAPA volta pra cena e nao sobra rebrota por cima dela, nem a que tinha mudado de lugar -- nem toco nenhum",
				cli.Rebrotas.Count == 0 && Pintadas == 0 && cli.Tocos.Count == 0 && TocosPintados == 0 && NaCena(mundo, ax, ay, tile)
				&& mundo.Colisao?.BlockedCell(ax, ay) == true && mundo.Colisao?.Rebrotada(ax, ay) == false
				&& (nova is not { } n2 || mundo.Colisao?.BlockedCell(n2.X, n2.Y) == false),
				$"{cli.Rebrotas.Count} rebrota(s) e {cli.Tocos.Count} toco(s) na lista; {Pintadas} e {TocosPintados} desenhado(s)");

		// ------------------------------------------------------------ 8) Icer: o toco bate com a arvore
		// *"arvore de neve em Icer nao vira toco de carvalho"*. Icer pesa mais que o corpo do robo: ele vai aclimatado
		// (a maestria cravada), que esmagado nao se soca direito -- a gravidade e a secao seguinte.
		Nota("-- 8) Icer: a arvore branca deixa o toco DELA --");
		double maestriaDeNovato = srv.DominarGravidadeDeTeste(eu, 1000);
		ZoneKey icer = ZoneKey.Premade("Icer");
		srv.ViajarDeTeste(eu, icer);
		for (int i = 0; i < 80 && mundo.ZonaMontadaDeTeste != icer.Hash; i++) yield return 0.25;
		yield return 2.0;
		Afirmar("(montagem) o robo chegou em Icer e o cliente montou o planeta",
				cli.Zone.Equals(icer) && mundo.ZonaMontadaDeTeste == icer.Hash && cli.Tocos.Count == 0, cli.Zone.ToString());
		if (!cli.Zone.Equals(icer) || mundo.PosicaoLocal is not { } emIcer) yield break;
		srv.EmbateDeFoto_CeuLimpo(eu);
		foreach (object passo in FazerDia(srv, mundo)) yield return passo;

		TileDeArvore? tileDaBranca = srv.TileDeArvoreDeTeste("Turfs 1", "frozentree");
		if (tileDaBranca is not { } branca || srv.ArvoreSocavelPertoDeTeste(icer, new Vec2(emIcer.X, emIcer.Y), null, branca) is not { } aBranca)
		{
			Afirmar("(montagem) Icer tem uma arvore branca (a de folha de turf) socavel", false);
			yield break;
		}
		(int ix, int iy, _) = aBranca;
		srv.AoPeDaArvoreDeTeste(eu, ix, iy);
		yield return 2.5;
		Afirmar("(montagem) a arvore branca esta na cena com o tile de folha de turf, e barra",
				NaCena(mundo, ix, iy, branca) && mundo.Colisao?.BlockedCell(ix, iy) == true && srv.BarraDeTeste(icer, ix, iy), $"({ix},{iy})");
		Fotografar("lenha-08-icer-a-arvore-branca");

		saldo = srv.RecursosDeTeste(eu);
		foreach (object passo in SocarAte(cli, () => cli.CenarioCaido.Contains((ix, iy)))) yield return passo;
		rendeu = srv.RecursosDeTeste(eu) - saldo;
		Afirmar("a arvore branca de Icer cai a soco, paga (o robo esta aclimatado) e deixa um toco",
				cli.CenarioCaido.Contains((ix, iy)) && NaFaixa(rendeu) && TemToco(cli, ix, iy, branca), $"{_socos} soco(s), +{rendeu:0.##}");
		yield return 2.5;

		// O TOCO DA BRANCA E PEQUENO (a arvore e de um tile so) e fica no pe da celula -- bem atras da cabeca de quem
		// esta colado ao sul dela. Pra foto o robo da dois passos pro lado, num chao livre.
		foreach ((int dx, int dy) in new[] { (2, 1), (-2, 1), (2, 2), (-2, 2), (0, 3), (3, 0), (-3, 0) })
		{
			if (srv.BarraDeTeste(icer, ix + dx, iy + dy)) continue;
			srv.ProceduralDeFoto_Levar(eu, ix + dx, iy + dy);
			break;
		}
		yield return 1.5;
		Image? fotoDoTocoBranco = Fotografar("lenha-09-icer-o-toco-da-branca");
		foreach (object passo in JulgarOTocoNaFoto("em Icer", fotoDoTocoBranco, ix, iy)) yield return passo;

		if (Toco(ix, iy) is { } tb && mediaDaTerra.A > 0)
		{
			// QUENTE E FRIO: a madeira do carvalho puxa pro vermelho; o gelo da branca, pro azul.
			Afirmar("**O TOCO BATE COM A ARVORE QUE CAIU**: o da branca de Icer e de GELO (azul >= vermelho), o do carvalho da Terra e de MADEIRA (vermelho > azul) -- duas artes, cada uma tirada da arvore dela",
					tb.Media.B >= tb.Media.R && mediaDaTerra.R > mediaDaTerra.B + 0.05f && tb.Tamanho.Y <= 16,
					$"Icer {tb.Media.ToHtml(false)} ({tb.Tamanho.X}x{tb.Tamanho.Y}), Terra {mediaDaTerra.ToHtml(false)}");

			TocosNaTela.UmaArteSoDeTeste = true;
			TocosNaTela.Instancia?.RemontarDeTeste();
			yield return 0.2;
			Color comDefeito = Toco(ix, iy)?.Media ?? Colors.Transparent;
			Afirmar("(defeito injetado: uma arte so pra todo toco) a arvore de gelo ganha o toco do CARVALHO da Terra -- o que o pedido proibe",
					comDefeito.A > 0 && comDefeito.R > comDefeito.B + 0.05f && comDefeito.IsEqualApprox(mediaDaTerra), comDefeito.ToHtml(false));
			TocosNaTela.UmaArteSoDeTeste = false;
			TocosNaTela.Instancia?.RemontarDeTeste();
			yield return 0.2;
			Afirmar("sem o defeito, o toco de gelo volta", Toco(ix, iy) is { } tb2 && tb2.Media.B >= tb2.Media.R);
		}
		else Nota("sem desenho de toco nesta rodada (headless): o \"bate com a arvore\" fica pra rodada com janela");

		// A OUTRA ARVORE DE ICER, a grande (branca de copa, tronco marrom): so pra a foto mostrar o toco dela tambem.
		if (srv.TileDeArvoreDeTeste("Tree Snow", "") is { } deNeve
			&& srv.ArvoreSocavelPertoDeTeste(icer, new Vec2(ix * T, iy * T), null, deNeve) is { } aDeNeve)
		{
			srv.AoPeDaArvoreDeTeste(eu, aDeNeve.X, aDeNeve.Y);
			yield return 2.5;
			Fotografar("lenha-10-icer-a-arvore-de-neve");
			foreach (object passo in SocarAte(cli, () => cli.CenarioCaido.Contains((aDeNeve.X, aDeNeve.Y)))) yield return passo;
			yield return 2.5;
			Afirmar("a arvore de neve grande de Icer tambem cai e deixa o toco dela (de tronco, e nao o sintetico)",
					TemToco(cli, aDeNeve.X, aDeNeve.Y, deNeve) && (_semArte || Toco(aDeNeve.X, aDeNeve.Y)?.Sintetico == false));
			Fotografar("lenha-11-icer-o-toco-da-arvore-de-neve");
		}
		else Nota("nao achei uma arvore de neve grande socavel em Icer: fica sem a foto dela");

		// ------------------------------------------------------------ 9) o planeta sorteado
		// O CASO DO PEDIDO: o novato que cai num mundo gerado. A regra e a mesma; o que muda e de onde vem a arvore --
		// la quem a pinta e o gerador do CLIENTE (da seed), e quem diz ao servidor que tile ela tem e a colheita do
		// mundo (`AoColherOMundo`). Se os dois discordassem, a arvore voltaria com outra cara.
		Nota("-- 9) um planeta sorteado, 2x mais pesado que o robo: a arvore, o toco, e a gravidade que conta como combate --");
		if (MundoSorteado() is not { } sorteado) { Afirmar("(montagem) a seed de fabrica tem um Jardim leve e pequeno", false); yield break; }
		ZoneKey zg = ZoneKey.Procedural(sorteado.Nome, sorteado.Seed);

		// O mundo nasce fora do tique: pede-se o pouso ate ele existir, e espera-se o cliente montar o chao.
		_rangeuSobAGravidade = 0;
		for (int i = 0; i < 80 && !srv.ProceduralDeFoto_Pousar(eu, sorteado.Nome, sorteado.Seed); i++) yield return 0.25;
		for (int i = 0; i < 80 && mundo.ZonaMontadaDeTeste != zg.Hash; i++) yield return 0.25;
		srv.DominarGravidadeDeTeste(eu, maestriaDeNovato);   // de volta a novato, ja no planeta pesado (em Icer ele ficaria prensado no chao)
		yield return 3.0;
		Afirmar($"(montagem) o robo pousou em {sorteado.Nome} e o cliente montou o mundo",
				cli.Zone.Equals(zg) && mundo.ZonaMontadaDeTeste == zg.Hash, cli.Zone.ToString());
		if (!cli.Zone.Equals(zg) || mundo.PosicaoLocal is not { } pouso) yield break;
		srv.EmbateDeFoto_CeuLimpo(eu);
		foreach (object passo in FazerDia(srv, mundo)) yield return passo;

		// TRES ARVORES perto do pouso: a do esmagado, a da constante invertida e a do aclimatado.
		var doMundo = new List<(int X, int Y, TileDeArvore Tile)>();
		var jaForam = new HashSet<(int X, int Y)>();
		while (doMundo.Count < 3 && srv.ArvoreSocavelPertoDeTeste(zg, new Vec2(pouso.X, pouso.Y), jaForam) is { } mais)
		{
			jaForam.Add((mais.X, mais.Y));
			doMundo.Add(mais);
		}
		Afirmar("(montagem) o mundo sorteado tem tres arvores socaveis", doMundo.Count == 3, $"{doMundo.Count}");
		if (doMundo.Count < 3) yield break;

		(int gx, int gy, TileDeArvore gtile) = doMundo[0];
		Nota($"a arvore do mundo sorteado: celula ({gx},{gy}), tile {gtile.Fonte}[{gtile.Ax},{gtile.Ay}]");
		srv.AoPeDaArvoreDeTeste(eu, gx, gy);
		yield return 2.5;

		Afirmar("MUNDO SORTEADO: a arvore que o gerador pos esta na CENA com o tile que o servidor anotou na colheita, e barra nas duas pontas",
				NaCena(mundo, gx, gy, gtile) && mundo.Colisao?.BlockedCell(gx, gy) == true && srv.BarraDeTeste(zg, gx, gy)
				&& cli.ZonaDaLenha == zg.Hash && cli.Rebrotas.Count == 0 && Pintadas == 0 && cli.Tocos.Count == 0);
		Image? fotoDoGerador = Fotografar("lenha-05-sorteado-a-arvore");

		// ESTE MUNDO PESA MAIS QUE O CORPO DO ROBO (2x contra 1x): o esmagamento o fere uma vez por segundo. E o que
		// acontece com o novato de verdade -- e o dono decidiu (2026-10-10): "sim conta como combate".
		(double gravidade, bool esmagado) = srv.GravidadeDeTeste(eu);
		Afirmar("(montagem) o corpo do robo esta sendo ESMAGADO pela gravidade deste mundo -- e o chat ja avisou",
				esmagado && gravidade > 1 && _rangeuSobAGravidade > 0, $"gravidade {gravidade:0.#}x, {_rangeuSobAGravidade} aviso(s)");

		saldo = srv.RecursosDeTeste(eu);
		_falas.Clear();
		foreach (object passo in SocarAte(cli, () => cli.CenarioCaido.Contains((gx, gy)))) yield return passo;
		rendeu = srv.RecursosDeTeste(eu) - saldo;
		Afirmar("...o soco de verdade a derruba la tambem: o tile sai, fica a terra batida e a colisao abre nas duas pontas",
				cli.CenarioCaido.Contains((gx, gy)) && !NaCena(mundo, gx, gy, gtile) && TerraBatida(mundo, gx, gy)
				&& mundo.Colisao?.BlockedCell(gx, gy) == false && !srv.BarraDeTeste(zg, gx, gy), $"{_socos} soco(s)");
		Afirmar("**O ESMAGADO NAO RECEBE** (dono: \"sim conta como combate\"): a arvore cai e o saldo nao sobe -- e o chat diz o motivo CERTO, a gravidade, e nao \"no meio de uma briga\"",
				rendeu == 0 && _falas.Exists(f => f.Contains("gravidade daqui te esmaga")) && !_falas.Exists(f => f.Contains("briga"))
				&& !_falas.Exists(f => f.Contains("recursos.")),
				$"+{rendeu:0.##} | {string.Join(" | ", _falas)}");
		Afirmar("...e o toco fica la tambem, tirado da arte da arvore do bioma", TemToco(cli, gx, gy, gtile) && cli.Tocos.Count == 1);
		yield return 2.5;   // a poeira assenta
		Image? fotoDoTocoSorteado = Fotografar("lenha-06-sorteado-o-toco");
		foreach (object passo in JulgarOTocoNaFoto("no mundo sorteado", fotoDoTocoSorteado, gx, gy)) yield return passo;

		srv.VencerALenhaDeTeste(zg);
		yield return 1.0;
		Afirmar("...e ela VOLTA: a rebrota chega com o tile do bioma, e desenhada na camada das rebrotas e barra nas duas pontas -- e o toco some",
				cli.Rebrotas.TryGetValue((gx, gy), out TileDeArvore voltou) && voltou == gtile
				&& Pintada(gx, gy) is { } d3 && d3.Fonte == gtile.Fonte && d3.Coord == new Vector2I(gtile.Ax, gtile.Ay)
				&& mundo.Colisao?.BlockedCell(gx, gy) == true && srv.BarraDeTeste(zg, gx, gy) && SemToco(cli, gx, gy));
		JulgarACopa("no mundo sorteado", fotoDoGerador, fotoDoTocoSorteado, Fotografar("lenha-07-sorteado-a-rebrota"), gx, gy);

		// A CONSTANTE INVERTIDA: o mesmo corpo esmagado, e a arvore paga.
		(int hx, int hy, _) = doMundo[1];
		srv.AoPeDaArvoreDeTeste(eu, hx, hy);
		yield return 1.5;
		saldo = srv.RecursosDeTeste(eu);
		_falas.Clear();
		Lenha.EsmagadoContaComoBriga = false;
		foreach (object passo in SocarAte(cli, () => cli.CenarioCaido.Contains((hx, hy)))) yield return passo;
		Lenha.EsmagadoContaComoBriga = true;
		rendeu = srv.RecursosDeTeste(eu) - saldo;
		Afirmar("(a constante `Lenha.EsmagadoContaComoBriga` invertida) o MESMO corpo esmagado derruba outra arvore e RECEBE -- e a linha que o dono troca pra tirar a arvore dessa regra",
				srv.GravidadeDeTeste(eu).Esmagado && cli.CenarioCaido.Contains((hx, hy)) && NaFaixa(rendeu)
				&& _falas.Exists(f => f.Contains($"+{rendeu:0} recursos")), $"+{rendeu:0.##} | {string.Join(" | ", _falas)}");

		// ACLIMATADO: a maestria alcanca a gravidade do chao, e a arvore paga na hora (a tag de combate que o
		// esmagamento deixou ainda esta acesa).
		(int kx, int ky, _) = doMundo[2];
		srv.DominarGravidadeDeTeste(eu, gravidade);
		srv.AoPeDaArvoreDeTeste(eu, kx, ky);
		yield return 1.5;
		saldo = srv.RecursosDeTeste(eu);
		_falas.Clear();
		foreach (object passo in SocarAte(cli, () => cli.CenarioCaido.Contains((kx, ky)))) yield return passo;
		rendeu = srv.RecursosDeTeste(eu) - saldo;
		Afirmar("**O ACLIMATADO RECEBE**: dominando a gravidade do planeta, o mesmo corpo no mesmo chao derruba e ganha os recursos, na hora",
				!srv.GravidadeDeTeste(eu).Esmagado && cli.CenarioCaido.Contains((kx, ky)) && NaFaixa(rendeu)
				&& _falas.Exists(f => f.Contains($"+{rendeu:0} recursos")), $"+{rendeu:0.##} | {string.Join(" | ", _falas)}");
		srv.DominarGravidadeDeTeste(eu, maestriaDeNovato);

		Nota($"a derivacao de toco mais cara da rodada custou {TocosNaTela.MaiorDerivacaoMsDeTeste:0.00} ms (uma vez por tipo de arvore, na primeira que cai)");
	}
}
