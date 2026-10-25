using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA ESFERA DO HAKAI EM JOGO (`--diaghakaibills`, 2026-10-23). O dono: *"e faca bills usar skills de hakai q tem esse efeito
/// nos ataques de ki dele como na imagem"* -- e decidiu: "os tiros do kit" (a Destroyer Sphere e a Fury saem como a esfera roxa da
/// imagem; o Ki Wave e o Basic Blast do Bills ficam como estao) e "o titulo tambem" (quem tem o titulo solta a mesma). O laboratorio
/// (`--diagartedeki`, familia 9) mede a esfera parada, peca por peca; este robo a fotografa NO JOGO: o Bills de verdade, na Terra,
/// soltando os dois tiros pelo verb de producao, e o jogador com o titulo soltando a esfera pelo verb do botao.
///
/// Com janela e `--host`. O palco (`Server/GameServer.HakaiPalco.cs`) pousa o Bills pela historia de producao, tira o povo de perto,
/// poe o host ao lado (fora da linha do tiro) e escolhe o lado livre. As cenas:
///   B1. o Bills solta a DESTROYER SPHERE: o node do cliente veste `HakaiEsfera` (a arte veio no anuncio) com a cor do Hakai -- e nao a
///       do ki dele, sorteado como o de todo NPC -- e a FOTO mostra a esfera: tinta do tamanho dela, o corpo roxo, o miolo claro, a
///       borda escura e o escuro dos arcos e dos graos.
///   B2. o Bills solta a FURY: o mesmo, com `HakaiFuria`, menor.
///   C1. CONTRA-EXEMPLO (`ArteDeProjetil.DardoNosTirosDoDeusDeTeste`, o jogo de antes): a esfera volta ao dardo `12.dmi` -- a regua do
///       B1 REPROVA.
///   C2. CONTRA-EXEMPLO (`PintorDeKi.HakaiApagadoDeTeste`): a camada desligada -- a esfera sai roxa, sem borda nem arcos, e a regua do
///       B1 REPROVA.
///   T1. o host recebe o TITULO e solta a Destroyer Sphere pelo verb do botao: a MESMA esfera (com o trono ocupado o Bills vai embora,
///       por isso e a ultima cena).
/// Fotos: `hakai-NN-*.png` (a tela inteira, com o chat) e a tira `hakai-tira.png` (os recortes, de cima pra baixo, nesta ordem).
///
/// A REGUA E DE PIXEL, pelo obturador da `--diaglimite`: com a arvore pausada, o node escondido, mostrado e escondido de novo; tinta e
/// o pixel que difere das duas fotos sem ele, onde as duas concordam. Medida numa janela de 3,2 raios em volta do centro DESENHADO do
/// tiro, em raios do estilo vezes o zoom da camera. Os pisos sairam da conta de papel do shader (`ag/hakaibills/ferr/jogo_teste.py`),
/// com a folga do quadro sorteado (o lab crava o quadro; aqui o tiro e o do jogo).
///
///     Godot --path . --host --rede 10973 --horateste 0.5 --semfauna --diaghakaibills --raca Human --conta bancada_hakaibills --nome Fotografo
/// </summary>
public partial class RoboDoHakaiDoBills : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<Image> _tira = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>Quantos tiles o tiro anda antes da foto: fora do corpo de quem atira, e dentro do corredor livre (8 tiles).</summary>
	private const double TilesAntesDaFoto = 1.2;

	/// <summary>O 0,12 das outras bancadas de foto: abaixo disso e ruido do viewport.</summary>
	private const int Epsilon = 31;

	// ============================ OS PISOS (conta de papel: `ag/hakaibills/ferr/jogo_teste.py`) ============================
	// Na escala 1, zoom 3, sobre grama, em quatro quadros sorteados: a esfera deu 33 a 35 mil px de tinta, o corpo 85-88% roxo, 0,55 a
	// 0,77 raio^2 de escuro, a borda em 100% dos rumos e 300-425 px claros; a Fury, 15 mil, 77-90%, 0,63-0,93, 96-100% e 124-136. Com
	// a camada DESLIGADA: escuro 0 e borda 0. O dardo de antes: ~500 px de tinta e nada roxo.
	// ========================================================================================================
	/// <summary>A tinta minima, em fracao do disco de 2 raios: 0,35 (a esfera da 0,9; o dardo, 0,02 da area da esfera).</summary>
	private const float PisoDeTinta = 0.35f;

	/// <summary>Quanto do corpo (ate 0,8 raio) tem de ser roxo.</summary>
	private const float PisoDoRoxo = 0.5f;

	/// <summary>O escuro (a borda, os arcos e os graos), em raios ao quadrado na foto.</summary>
	private const float PisoDeEscuro = 0.15f;

	/// <summary>Em quantos dos 72 rumos ha escuro entre 0,8 e 1,08 raio (a borda).</summary>
	private const float PisoDaBorda = 0.55f;

	/// <summary>O miolo quase branco, em raios ao quadrado na foto.</summary>
	private const float PisoDoClaro = 0.02f;

	private CanvasItem? _escondido;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok     " : "  FALHA  ") + oque);
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	public override void _Ready()
	{
		// A ARVORE E PAUSADA PRA FOTOGRAFAR (ver `Obturar`); sem isto o robo congelaria junto.
		ProcessMode = ProcessModeEnum.Always;
		_ = Rodar();
	}

	public override void _ExitTree() => Desligar();

	/// <summary>Os dois defeitos desligados, a arvore solta e o node visivel -- haja o que houver.</summary>
	private void Desligar()
	{
		ArteDeProjetil.DardoNosTirosDoDeusDeTeste = false;
		PintorDeKi.HakaiApagadoDeTeste = false;
		if (IsInsideTree() && GetTree() is { Paused: true } t) t.Paused = false;
		if (_escondido != null && IsInstanceValid(_escondido)) _escondido.Visible = true;
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Desligar();
		if (C is { } cli && S is { } srv)
		{
			srv.HakaiPalco_Limpar(cli.LocalId, fim: true);
			srv.RecolherOPalcoDoBillsDeTeste(cli.LocalId);
		}
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "(montagem) em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo (modo host)");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		World mundo = World.Instancia!;
		// UM CORPO RECEM-CRIADO ENTRA NOCAUTEADO POR UM INSTANTE (a `--diagvariedade` perdeu o primeiro tiro assim)
		await Ate(() => srv.EstadoDaVariedade(eu) is { KO: false, Morto: false }, 30);
		await Segundos(1.0);
		GD.Print("[dhakai] ===== A ESFERA DO HAKAI EM JOGO: os dois tiros do kit do Bills e o do titulo (dono, 2026-10-23) =====");

		(int Bills, int Whis)? par = srv.BillsNaTerraDeTeste(eu);
		bool naTela = par is { } p0 && await Ate(() => mundo.CorpoDeTeste(p0.Bills) != null && mundo.CorpoDeTeste(p0.Whis) != null, 15);
		Conferir(naTela, "(montagem) o calendario de producao pousou o Bills e o Whis na Terra, e os dois estao na tela");
		if (par is not { } divinos || !naTela) return;
		srv.CravarMeioDiaDaVariedade(eu);
		await Segundos(2.0);   // o veu do clima no login (memoria da regua de pixel): o ceu limpo assenta

		var prep = srv.HakaiPalco_Preparar(eu);
		Conferir(prep.Ok, $"(montagem) o palco tirou o povo de perto ({prep.SemPovo} habitante(s)), pos o host ao lado do Bills, fora da linha "
						  + $"do tiro, e achou corredor pro {prep.Rumo}{(prep.Porque.Length > 0 ? $" -- {prep.Porque}" : "")}");
		if (!prep.Ok) return;
		await Segundos(1.2);   // a correcao de posicao atravessa o fio, e a camera chega

		// ---------------------------------------------------------- B1 e B2. o Bills, pelo verb de producao
		Foto b1 = await Disparo(srv, mundo, divinos.Bills, () => srv.HakaiPalco_BillsAtira(esfera: true), ArteDeKi.HakaiEsfera,
								"hakai-01-bills-destroyer-sphere");
		Conferir(b1.EhOHakai(ArteDeKi.HakaiEsfera),
				 $"B1 o BILLS solta a DESTROYER SPHERE pelo verb de producao e ela sai como a imagem: a arte `HakaiEsfera` veio no anuncio, a "
				 + $"cor e a do Hakai (e nao a do ki dele), e na FOTO o corpo roxo, o miolo claro, a borda e os arcos escuros [{b1}]");
		Foto b2 = await Disparo(srv, mundo, divinos.Bills, () => srv.HakaiPalco_BillsAtira(esfera: false), ArteDeKi.HakaiFuria,
								"hakai-02-bills-fury");
		Conferir(b2.EhOHakai(ArteDeKi.HakaiFuria),
				 $"B2 o BILLS solta a FURY e ela sai como a mesma esfera, MENOR: a arte `HakaiFuria`, a cor do Hakai, e na foto as mesmas pecas [{b2}]");
		Conferir(b1.Achou && b2.Achou && b2.Tinta > 0 && b1.Tinta > b2.Tinta * 1.4f,
				 $"a Destroyer Sphere e MAIOR que a Fury na tela ({b1.Tinta} contra {b2.Tinta} px de tinta)");

		// ---------------------------------------------------------- C1. o jogo de antes: o dardo `12.dmi`
		ArteDeProjetil.DardoNosTirosDoDeusDeTeste = true;
		try
		{
			Foto c1 = await Disparo(srv, mundo, divinos.Bills, () => srv.HakaiPalco_BillsAtira(esfera: true), ArteDeKi.HakaiEsfera,
									"hakai-c1-defeito-o-dardo-de-antes");
			Conferir(c1.Achou && c1.Arte == ArteDeKi.Blast12 && !c1.EhOHakai(ArteDeKi.HakaiEsfera),
					 $"C1 (defeito injetado: `ArteDeProjetil.DardoNosTirosDoDeusDeTeste`, o jogo de antes) a esfera do Bills volta ao dardo "
					 + $"`12.dmi` na cor do ki dele -- a regua do B1 REPROVA [{c1}]");
		}
		finally { ArteDeProjetil.DardoNosTirosDoDeusDeTeste = false; }

		// ---------------------------------------------------------- C2. a camada do Hakai desligada
		PintorDeKi.HakaiApagadoDeTeste = true;
		try
		{
			Foto c2 = await Disparo(srv, mundo, divinos.Bills, () => srv.HakaiPalco_BillsAtira(esfera: true), ArteDeKi.HakaiEsfera,
									"hakai-c2-defeito-sem-a-camada");
			Conferir(c2.Achou && c2.Arte == ArteDeKi.HakaiEsfera && c2.CorDoHakai && !c2.EhOHakai(ArteDeKi.HakaiEsfera),
					 $"C2 (defeito injetado: `PintorDeKi.HakaiApagadoDeTeste`) a esfera sai roxa, mas sem a borda e os arcos escuros -- a regua "
					 + $"do B1 REPROVA [{c2}]");
		}
		finally { PintorDeKi.HakaiApagadoDeTeste = false; }

		// ---------------------------------------------------------- T1. o jogador com o titulo, pelo verb do botao
		// PRO LADO DO TIRO, ANDANDO: o olhar do corpo local e previsao do cliente, e o pacote dele reescreveria um olhar posto so no
		// servidor (o `VirarPraLeste` da `--diagcargavoo`). Um passo, e o palco o devolve a ancora.
		Vector2 lado = prep.Rumo == Facing.East ? Vector2.Right : Vector2.Left;
		bool virou = await Ate(() => { mundo.AndarDeTeste(lado); return srv.HakaiPalco_OlharDoHost(eu) == prep.Rumo; }, 2);
		mundo.PararDeTeste();
		await Segundos(0.35);
		srv.HakaiPalco_ReporOHost(eu);
		await Segundos(1.1);
		Foto t1 = await Disparo(srv, mundo, eu, () => srv.HakaiPalco_HostAtira(eu, esfera: true), ArteDeKi.HakaiEsfera,
								"hakai-03-titulo-destroyer-sphere");
		Conferir(virou && t1.EhOHakai(ArteDeKi.HakaiEsfera),
				 $"T1 o JOGADOR com o titulo de Deus da Destruicao solta a Destroyer Sphere pelo verb do botao, e ela e a MESMA esfera do "
				 + $"Bills (a arte e do tiro) [virou pro {prep.Rumo}: {virou}; {t1}]");

		Colar("hakai-tira.png", _tira,
			  "A TIRA (de cima pra baixo): B1 a Destroyer Sphere do Bills, B2 a Fury do Bills, C1 o dardo de antes (defeito), C2 sem a camada "
			  + "(defeito) e T1 a Destroyer Sphere do jogador com o titulo");
	}

	// =====================================================================
	// UM TIRO
	// =====================================================================
	/// <summary>
	/// O QUE A FOTO DE UM TIRO MOSTROU: a arte e a cor do NODE (o que veio no anuncio e o que o `Vestir` fez dela) e, da foto, a tinta, a
	/// fracao roxa do corpo, o escuro, o claro e a borda -- tudo medido com o raio do estilo ESPERADO (o da esfera do Hakai), pra que
	/// um tiro errado (o dardo) seja medido pela mesma regua.
	/// </summary>
	private readonly record struct Foto(bool Achou, ArteDeKi Arte, Color Cor, float Raio, int Tinta, float RoxoNoCorpo, int Escuro, int Claro,
										float Borda, string Porque)
	{
		public bool CorDoHakai => Cor.IsEqualApprox(ArteDeKiNoCliente.CorDoHakai);

		/// <summary>A REGUA DA ESFERA DO HAKAI: a arte e a cor certas no node, e as pecas da imagem na foto.</summary>
		public bool EhOHakai(ArteDeKi esperada)
		{
			float r2 = Raio * Raio;
			return Achou && Arte == esperada && CorDoHakai && Tinta >= PisoDeTinta * MathF.PI * 4f * r2 && RoxoNoCorpo >= PisoDoRoxo
				   && Escuro >= PisoDeEscuro * r2 && Claro >= PisoDoClaro * r2 && Borda >= PisoDaBorda;
		}

		public override string ToString() =>
			!Achou ? $"SEM TIRO: {Porque}"
			: $"node: arte {Arte}, cor {Cor}{(CorDoHakai ? " (a do Hakai)" : "")}; foto (raio {Raio:0} px): {Tinta} px de tinta, corpo "
			  + $"{RoxoNoCorpo:P0} roxo, {Escuro} px escuros ({Escuro / Math.Max(1f, Raio * Raio):0.00} raio^2), {Claro} claros, borda em "
			  + $"{Borda:P0} dos rumos" + (Porque.Length > 0 ? $"; {Porque}" : "");
	}

	/// <summary>
	/// UM DISPARO: o palco aperta o verb, o robo espera o tiro nascer no SERVIDOR, andar <see cref="TilesAntesDaFoto"/> e o node dele
	/// existir no cliente, e fotografa. Depois limpa a cena.
	/// </summary>
	private async System.Threading.Tasks.Task<Foto> Disparo(Jandirus.Server.GameServer srv, World mundo, int quem, Func<string> apertar,
															ArteDeKi esperada, string foto)
	{
		srv.HakaiPalco_Limpar(C?.LocalId ?? 0, fim: false);
		await Segundos(0.3);
		string recusa = apertar();
		ProjetilDesenhado? no = null;
		bool pronto = recusa.Length == 0
					  && await Ate(() => srv.HakaiPalco_Tiro(quem) is { Achou: true, Andou: >= TilesAntesDaFoto } t
										&& (no = mundo.FindChild($"Tiro{t.Id}", recursive: true, owned: false) as ProjetilDesenhado) != null, 6);
		Foto f = pronto && no != null
			? await Fotografar(no, esperada, foto)
			: new Foto(false, ArteDeKi.Nenhuma, Colors.Black, 0, 0, 0, 0, 0, 0, recusa.Length > 0 ? $"o verb recusou: {recusa}" : "o tiro nao nasceu, ou nao chegou a tela");
		srv.HakaiPalco_Limpar(C?.LocalId ?? 0, fim: false);
		await Segundos(0.6);
		return f;
	}

	/// <summary>
	/// A FOTO DE UM TIRO: as tres do obturador, a de COM gravada (a tela inteira, com o chat), e a medida numa janela de 3,2 raios em
	/// volta do centro desenhado do node. O raio e o do estilo ESPERADO vezes o zoom da camera.
	/// </summary>
	private async System.Threading.Tasks.Task<Foto> Fotografar(ProjetilDesenhado no, ArteDeKi esperada, string arquivo)
	{
		ArteDeKi arte = no.Arte;
		Color cor = no.Cor;
		Vector2 centroNoMundo = no.GlobalPosition + no.SubidaNaTela;
		(Image? com, Image? sem, Image? sem2) = await Obturar(no);
		if (com == null || sem == null || sem2 == null)
			return new Foto(true, arte, cor, 0, 0, 0, 0, 0, 0, "a janela nao renderizou (headless?) -- sem foto");
		Gravar(com, arquivo + ".png");

		float raio = ArteDeKiNoCliente.EstiloDaBola(esperada).Raio * no.Escala * EscalaDaImagem(com);
		Vector2 c = NaImagem(com, centroNoMundo);
		int w = com.GetWidth(), h = com.GetHeight();
		byte[] bc = com.GetData(), bs = sem.GetData(), bs2 = sem2.GetData();
		bool Tinta(int x, int y)
		{
			int i = (y * w + x) * 4;
			return !Difere(bs, bs2, i) && Difere(bc, bs, i) && Difere(bc, bs2, i);
		}
		(byte R, byte G, byte B) Px(int x, int y) { int i = (y * w + x) * 4; return (bc[i], bc[i + 1], bc[i + 2]); }

		float janela = raio * 3.2f;
		int x0 = Math.Clamp((int)(c.X - janela), 0, w - 1), x1 = Math.Clamp((int)(c.X + janela), 0, w - 1);
		int y0 = Math.Clamp((int)(c.Y - janela), 0, h - 1), y1 = Math.Clamp((int)(c.Y + janela), 0, h - 1);
		int tinta = 0, escuro = 0, claro = 0, corpo = 0, roxo = 0;
		for (int y = y0; y <= y1; y++)
			for (int x = x0; x <= x1; x++)
			{
				float d = new Vector2(x + 0.5f - c.X, y + 0.5f - c.Y).Length();
				if (d > janela || !Tinta(x, y)) continue;
				tinta++;
				(byte r, byte g, byte b) = Px(x, y);
				if (Math.Max(r, Math.Max(g, b)) < 80) escuro++;
				if (r > 225 && b > 225 && g > 160) claro++;
				if (d > raio * 0.8f) continue;
				corpo++;
				if (r > 90 && b > 120 && g + 40 < r && g + 40 < b) roxo++;
			}

		int comBorda = 0;
		for (int k = 0; k < 72; k++)
		{
			float a = MathF.Tau * k / 72f;
			for (float d = raio * 0.80f; d <= raio * 1.08f; d += 0.5f)
			{
				int x = (int)MathF.Floor(c.X + MathF.Cos(a) * d), y = (int)MathF.Floor(c.Y + MathF.Sin(a) * d);
				if (x < 0 || y < 0 || x >= w || y >= h) break;
				(byte r, byte g, byte b) = Px(x, y);
				if (!Tinta(x, y) || Math.Max(r, Math.Max(g, b)) >= 80) continue;
				comBorda++;
				break;
			}
		}

		Image recorte = com.GetRegion(new Rect2I(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)));
		recorte.Convert(Image.Format.Rgba8);
		_tira.Add(recorte);
		return new Foto(true, arte, cor, raio, tinta, corpo > 0 ? roxo / (float)corpo : 0f, escuro, claro, comBorda / 72f, "");
	}

	private static bool Difere(byte[] p, byte[] q, int i) =>
		Math.Abs(p[i] - q[i]) > Epsilon || Math.Abs(p[i + 1] - q[i + 1]) > Epsilon || Math.Abs(p[i + 2] - q[i + 2]) > Epsilon;

	// =====================================================================
	// O OBTURADOR (o da `--diaglimite`)
	// =====================================================================
	/// <summary>
	/// ESCONDE, FOTOGRAFA, MOSTRA, FOTOGRAFA, ESCONDE, FOTOGRAFA -- com a arvore pausada e tres quadros de folga a cada troca (`GetImage`
	/// devolve o ULTIMO quadro renderizado). Devolve (com, sem, sem de novo), em RGBA8.
	/// </summary>
	private async System.Threading.Tasks.Task<(Image?, Image?, Image?)> Obturar(CanvasItem alvo)
	{
		_escondido = alvo;
		GetTree().Paused = true;
		try
		{
			alvo.Visible = false;
			await Quadros(3);
			Image? sem = Tela();
			if (!IsInstanceValid(alvo)) return (null, null, null);
			alvo.Visible = true;
			await Quadros(3);
			Image? com = Tela();
			if (!IsInstanceValid(alvo)) return (null, null, null);
			alvo.Visible = false;
			await Quadros(3);
			Image? sem2 = Tela();
			return (com, sem, sem2);
		}
		finally
		{
			if (IsInstanceValid(alvo)) alvo.Visible = true;
			_escondido = null;
			GetTree().Paused = false;
		}
	}

	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	/// <summary>De ponto do mundo pra pixel da imagem (a `CanvasTransform` e a razao imagem / viewport).</summary>
	private Vector2 NaImagem(Image img, Vector2 mundo)
	{
		Vector2 v = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;
		Vector2 tam = GetViewport()?.GetVisibleRect().Size ?? img.GetSize();
		return new Vector2(v.X * img.GetWidth() / tam.X, v.Y * img.GetHeight() / tam.Y);
	}

	/// <summary>Quantos pixels de imagem vale um pixel de mundo (o zoom da camera, e a razao imagem / viewport).</summary>
	private float EscalaDaImagem(Image img)
	{
		float e = (NaImagem(img, new Vector2(ZoneCollision.TileSize, 0)) - NaImagem(img, Vector2.Zero)).Length() / ZoneCollision.TileSize;
		return e > 0.01f ? e : 1f;
	}

	private void Gravar(Image img, string nome)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_linhas.Add("  foto   " + caminho);
		}
		catch (Exception e) { Nota($"{nome}: sem foto: {e.Message}"); }
	}

	/// <summary>Os recortes um embaixo do outro (o `BlitRect` exige o mesmo formato nos dois lados).</summary>
	private void Colar(string nome, List<Image> pedacos, string rotulo)
	{
		if (pedacos.Count == 0) return;
		const int Vao = 6;
		int larg = 0, alt = 0;
		foreach (Image p in pedacos) { larg = Math.Max(larg, p.GetWidth()); alt += p.GetHeight() + Vao; }
		Image colagem = Image.CreateEmpty(Math.Max(1, larg), Math.Max(1, alt - Vao), false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));
		int y = 0;
		foreach (Image p in pedacos)
		{
			colagem.BlitRect(p, new Rect2I(Vector2I.Zero, p.GetSize()), new Vector2I(0, y));
			y += p.GetHeight() + Vao;
		}
		Nota(rotulo);
		Gravar(colagem, nome);
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[dhakai] " + l);
		GD.Print($"[dhakai] ===== {_ok} OK, {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dhakai]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
