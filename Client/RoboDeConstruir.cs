using System.Collections;
using Godot;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO MODO DE CONSTRUIR (`--diagconstruir`) -- a metade do CLIENTE dos pedidos do dono de 2026-10-09:
/// a tecla B, a barra, o fantasma, o clique que ergue, a paleta, a pergunta da senha, a tecla E na porta,
/// o botao direito que desmancha, e o desenho da porta do MAPA que cai.
///
/// TUDO PELA ENTRADA DE VERDADE: teclas e cliques empurrados no viewport, botoes apertados pelo sinal. O que
/// se afirma e lido de DOIS lados -- o servidor (`GameServer.BlocoDeTeste`, no modo host) e a tela (o tile
/// desenhado, o node da porta, o mapa de colisao do cliente, o pixel da foto).
///
/// O PALCO: um campo aberto (`--campoteste 8`), ao meio-dia (`--horateste 0.5`) e sem chuva. O robo ergue uma casinha de 3x3 a
/// leste do corpo -- sete paredes, uma porta com senha virada pra ele e um piso no meio -- e a fotografa.
/// No fim desmancha tudo: a pasta de saves da bancada nao guarda base de uma rodada pra outra.
///
/// Rode com janela (no segundo monitor) -- as fotos e a medida do breu precisam de desenho:
///   --host --rede 7982 --campoteste 8 --horateste 0.5 --diagconstruir --raca Human --conta bancada_construir --nome Pedreiro
/// </summary>
public partial class RoboDeConstruir : Node
{
	private const int T = ZoneCollision.TileSize;

	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [];
	private IEnumerator? _roteiro;
	private double _espera = 2.5;
	private bool _acabou;

	private static GameClient? C => GameClient.Instance;
	private static World? M => World.Instancia;
	private static ModoDeConstruir? Modo => ModoDeConstruir.Instancia;
	private static GameServer? S => GameServer.Instance;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[construir]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[construir]   FALHA {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[construir] " + s);

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
		World.BlocoSoDesenhaDeTeste = World.PortaCaidaFicaDesenhadaDeTeste = false;
		GD.Print($"\n[construir] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[construir] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}

	// =====================================================================
	// A ENTRADA
	// =====================================================================
	private static Vector2 Meio(int cx, int cy) => new((cx + 0.5f) * T, (cy + 0.5f) * T);

	private Vector2 NaTela(int cx, int cy) => M!.GetCanvasTransform() * Meio(cx, cy);

	/// <summary>O mouse vai pra celula. `inLocalCoords: true` pelo motivo escrito no `RoboDeInstalar.ClicarNoMundo`.</summary>
	private void Mover(int cx, int cy)
	{
		Vector2 p = NaTela(cx, cy);
		GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);
	}

	private void Botao(int cx, int cy, MouseButton b, bool desce)
	{
		Vector2 p = NaTela(cx, cy);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = b, Pressed = desce, Position = p, GlobalPosition = p }, true);
	}

	/// <summary>Um clique inteiro: mexe, desce e solta.</summary>
	private void Clicar(int cx, int cy, MouseButton b = MouseButton.Left)
	{
		Mover(cx, cy);
		Botao(cx, cy, b, true);
		Botao(cx, cy, b, false);
	}

	private void Tecla(Key k) =>
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });

	/// <summary>Gira a roda ate a casa escolhida ter um bloco desta classe (no maximo uma volta).</summary>
	private bool RodarAte(ClasseDeBloco classe, int cx, int cy)
	{
		for (int i = 0; i < 9; i++)
		{
			if (BlocosNoCliente.Catalogo.Get(Modo!.NaMaoDeTeste) is { } d && d.Classe == classe) return true;
			Botao(cx, cy, MouseButton.WheelDown, true);
		}
		return BlocosNoCliente.Catalogo.Get(Modo!.NaMaoDeTeste) is { } f && f.Classe == classe;
	}

	private Image? Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza): rode com janela"); return null; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
		return img;
	}

	/// <summary>O maior canal (0..255) entre nove amostras do miolo de uma celula, na foto. -1 = fora da foto.</summary>
	private int Brilho(Image foto, int cx, int cy)
	{
		Viewport v = GetViewport();
		int maior = -1;
		for (int iy = 1; iy <= 3; iy++)
			for (int ix = 1; ix <= 3; ix++)
			{
				Vector2 p = v.GetFinalTransform() * (v.CanvasTransform * new Vector2((cx + ix / 4f) * T, (cy + iy / 4f) * T));
				int x = (int)p.X, y = (int)p.Y;
				if (x < 0 || y < 0 || x >= foto.GetWidth() || y >= foto.GetHeight()) continue;
				Color c = foto.GetPixel(x, y);
				maior = Math.Max(maior, (int)MathF.Round(MathF.Max(c.R, MathF.Max(c.G, c.B)) * 255f));
			}
		return maior;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		GameClient cli = C!;
		World mundo = M!;
		if (S is not { } srv) { Afirmar("(montagem) o robo roda no modo host, com o servidor no mesmo processo", false); yield break; }
		if (Modo is not { } modo) { Afirmar("(montagem) o modo de construir existe no mundo", false); yield break; }

		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);
		srv.ZerarBasesDeTeste();
		yield return 1.0;

		Vector2 eu = mundo.PosicaoLocal!.Value;
		(int ex, int ey) = RegrasDeBloco.CelulaDoCorpo(new Vec2(eu.X, eu.Y));
		ZoneKey zona = cli.Zone;
		Nota($"o corpo esta na celula ({ex},{ey}) de {zona.Name}");

		// a casinha: o anel de 3x3 em volta de (ex+3, ey), com a porta no lado de ca
		(int X, int Y) miolo = (ex + 3, ey), vao = (ex + 2, ey);
		List<(int X, int Y)> paredes = [];
		for (int y = ey - 1; y <= ey + 1; y++)
			for (int x = ex + 2; x <= ex + 4; x++)
				if ((x, y) != miolo && (x, y) != vao) paredes.Add((x, y));

		// ------------------------------------------------------------ 1) a tecla e a barra
		Nota("-- 1) a tecla B liga o modo --");
		Afirmar("(montagem) antes da tecla o modo esta desligado, sem barra e sem fantasma", !modo.NaTela && modo.FantasmaDeTeste == null);
		Tecla(Key.B);
		yield return 0.3;
		Mover(paredes[0].X, paredes[0].Y);
		yield return 0.2;
		Afirmar("a tecla B liga: a barra aparece com nove casas e ha um bloco na mao", modo.NaTela && modo.BarraDeTeste.Count == 9 && modo.NaMaoDeTeste.Length > 0,
				$"na mao: {modo.NaMaoDeTeste}");
		Sprite2D? fant = modo.FantasmaDeTeste;
		Afirmar("o fantasma e desenho local (filho do mundo), meio transparente, na celula sob o mouse",
				fant != null && fant.GetParent() == mundo && fant.Visible && fant.Modulate.A < 0.9f
				&& fant.Position.DistanceTo(new Vector2(paredes[0].X * T, paredes[0].Y * T)) < 0.5f,
				fant != null ? $"em {fant.Position}, alfa {fant.Modulate.A:0.00}" : "sem fantasma");
		Afirmar("nada viajou so por mostrar o fantasma: o servidor nao tem bloco nenhum", srv.BlocosDeTeste(zona) == 0);
		Fotografar("construir-01-barra-e-fantasma");

		// ------------------------------------------------------------ 2) o clique ergue
		Nota("-- 2) clicar ergue; o que nao pode fica vermelho e diz por que --");
		bool temParede = RodarAte(ClasseDeBloco.Parede, paredes[0].X, paredes[0].Y);
		Afirmar("(montagem) a roda do mouse acha uma parede na barra", temParede, modo.NaMaoDeTeste);
		string idDaParede = modo.NaMaoDeTeste;

		Mover(ex, ey);   // em cima de mim mesmo
		yield return 0.15;
		Afirmar("em cima do proprio corpo o fantasma fica vermelho e a barra diz o motivo",
				modo.FantasmaDeTeste is { } f1 && f1.Modulate.G < 0.6f && modo.AvisoDeTeste == RegrasDeBloco.Motivo(RecusaDeBloco.Corpo),
				modo.AvisoDeTeste);
		Clicar(ex, ey);
		yield return 0.4;
		Afirmar("...e o clique ali nao ergue nada", srv.BlocoDeTeste(zona, ex, ey) == null);

		Clicar(paredes[0].X, paredes[0].Y);
		yield return 0.5;
		(int px0, int py0) = paredes[0];
		Afirmar("o clique numa celula livre ergue a parede NO SERVIDOR, e ela e minha",
				srv.BlocoDeTeste(zona, px0, py0) is { } b0 && b0.Tipo == idDaParede && b0.Dono == "bancada_construir",
				srv.BlocoDeTeste(zona, px0, py0)?.ToString() ?? "nada");
		Afirmar("...o cliente a desenha, e o mapa de colisao DELE barra a celula (as duas pontas concordam)",
				mundo.BlocoDesenhadoDeTeste(px0, py0) != null && mundo.Colisao?.BlockedCell(px0, py0) == true && srv.BarraDeTeste(zona, px0, py0));

		// o contra-exemplo do cliente: a parede que e so desenho
		World.BlocoSoDesenhaDeTeste = true;
		Clicar(paredes[1].X, paredes[1].Y);
		yield return 0.5;
		Afirmar("(defeito injetado: o bloco erguido e so desenho) a parede aparece e o mapa do cliente NAO barra",
				mundo.BlocoDesenhadoDeTeste(paredes[1].X, paredes[1].Y) != null && mundo.Colisao?.BlockedCell(paredes[1].X, paredes[1].Y) == false);
		World.BlocoSoDesenhaDeTeste = false;
		Clicar(paredes[1].X, paredes[1].Y, MouseButton.Right);
		yield return 0.4;

		// ------------------------------------------------------------ 3) segurar e arrastar pinta
		Nota("-- 3) segurar e arrastar pinta as outras paredes --");
		Mover(paredes[1].X, paredes[1].Y);
		Botao(paredes[1].X, paredes[1].Y, MouseButton.Left, true);
		yield return 0.1;
		foreach ((int x, int y) in paredes.Skip(2))
		{
			Mover(x, y);
			yield return 0.12;
		}
		Botao(paredes[^1].X, paredes[^1].Y, MouseButton.Left, false);
		yield return 0.5;
		int dePe = paredes.Count(c => srv.BlocoDeTeste(zona, c.X, c.Y) != null && mundo.BlocoDesenhadoDeTeste(c.X, c.Y) != null);
		Afirmar("as sete paredes do anel estao de pe no servidor e desenhadas no cliente", dePe == paredes.Count, $"{dePe} de {paredes.Count}");

		// ------------------------------------------------------------ 4) a paleta
		Nota("-- 4) a paleta (E): todos os blocos, e escolher um o poe na casa --");
		Tecla(Key.E);
		yield return 0.4;
		Afirmar("com o modo ligado a tecla E abre a paleta (e nao o menu de interacao), com todos os blocos do catalogo",
				modo.PaletaNaTela && modo.BlocosNaPaletaDeTeste == BlocosNoCliente.Catalogo.Total && MenuDeInteracao.Instancia?.NaTela != true,
				$"{modo.BlocosNaPaletaDeTeste} de {BlocosNoCliente.Catalogo.Total}");
		Fotografar("construir-02-paleta");
		BlocoDef? pisoDef = BlocosNoCliente.Catalogo.Get("woodfloor") ?? BlocosNoCliente.Catalogo.Todos.First(d => d.Classe == ClasseDeBloco.Piso);
		int casaAntes = modo.CasaDeTeste;
		Afirmar("clicar num bloco da paleta o poe na casa escolhida", modo.ApertarNaPaleta(pisoDef.Nome) && modo.NaMaoDeTeste == pisoDef.Id && modo.CasaDeTeste == casaAntes,
				modo.NaMaoDeTeste);
		Tecla(Key.E);
		yield return 0.3;
		Afirmar("a tecla E fecha a paleta", !modo.PaletaNaTela);

		Clicar(miolo.X, miolo.Y);
		yield return 0.5;
		Afirmar("o piso sobe no miolo: nao barra, e a celula passa a estar sob teto no cliente",
				srv.BlocoDeTeste(zona, miolo.X, miolo.Y)?.Tipo == pisoDef.Id && mundo.BlocoDesenhadoDeTeste(miolo.X, miolo.Y) != null
				&& mundo.Colisao?.BlockedCell(miolo.X, miolo.Y) == false && mundo.CelulaSobTeto(miolo.X, miolo.Y));

		// ------------------------------------------------------------ 5) a porta pergunta a senha
		Nota("-- 5) a porta SEMPRE pergunta a senha --");
		bool temPorta = RodarAte(ClasseDeBloco.Porta, vao.X, vao.Y);
		Afirmar("(montagem) a roda acha uma porta na barra", temPorta, modo.NaMaoDeTeste);
		Clicar(vao.X, vao.Y);
		yield return 0.4;
		Afirmar("clicar com a porta na mao NAO ergue: abre a pergunta da senha", modo.PerguntaNaTela && srv.BlocoDeTeste(zona, vao.X, vao.Y) == null);
		Fotografar("construir-03-a-pergunta-da-senha");
		Afirmar("\"Com senha\" em branco nao ergue: pede a senha", modo.ResponderDeTeste("Com senha") && modo.PerguntaNaTela
				&& srv.BlocoDeTeste(zona, vao.X, vao.Y) == null, modo.AvisoDeTeste);
		modo.ResponderDeTeste("Com senha", "1234");
		yield return 0.6;
		Afirmar("respondida com senha, a porta sobe TRANCADA e fechada no servidor",
				srv.BlocoDeTeste(zona, vao.X, vao.Y) is { Trancado: true, Aberto: false } && !modo.PerguntaNaTela);
		Afirmar("...o cliente desenha a porta (um node `Porta`, e nao tile) e barra a celula",
				mundo.PortaErguidaDeTeste(vao.X, vao.Y) != null && mundo.BlocoDesenhadoDeTeste(vao.X, vao.Y) == null
				&& mundo.Colisao?.BlockedCell(vao.X, vao.Y) == true);
		Afirmar("...e o que chegou diz que ela e minha e esta trancada", cli.Blocos.TryGetValue(vao, out GameClient.BlocoInfo minhaPorta)
				&& minhaPorta is { Meu: true, Trancado: true, SeiASenha: true, Aberto: false });

		// ------------------------------------------------------------ 6) de fora, a base e breu
		Nota("-- 6) de fora, o que esta dentro nao se ve --");
		Tecla(Key.B);
		yield return 0.6;
		Afirmar("a tecla B desliga: somem a barra e o fantasma", !modo.NaTela && modo.FantasmaDeTeste == null);
		Mover(ex - 3, ey - 3);   // o mouse longe da casa, pra nada cobrir a foto
		yield return 0.5;
		if (Fotografar("construir-04-a-casa-de-fora") is { } foto)
		{
			int dentro = Brilho(foto, miolo.X, miolo.Y), fora = Brilho(foto, ex, ey + 2), parede = Brilho(foto, vao.X, vao.Y - 1);
			Afirmar("na foto, o piso de dentro e BREU, o chao de fora e claro e a parede virada pra mim e clara",
					dentro >= 0 && dentro <= 12 && fora >= 40 && parede >= 40, $"dentro {dentro}, fora {fora}, parede {parede}");
		}

		// ------------------------------------------------------------ 7) a tecla E na porta
		Nota("-- 7) a tecla E na minha porta --");
		MenuDeInteracao? menu = MenuDeInteracao.Instancia;
		Afirmar("perto da porta (a duas celulas) a dica diz que ela e minha", menu?.DicaNaTela == "[E] Sua porta", menu?.DicaNaTela ?? "");
		Tecla(Key.E);
		yield return 0.4;
		Afirmar("a tecla E abre o menu da porta, com a troca de senha",
				menu is { NaTela: true } && menu.TituloDesenhado == "Sua porta" && menu.BotoesDesenhados().Contains("Trocar a senha"),
				menu != null ? string.Join(" | ", menu.BotoesDesenhados()) : "");
		Fotografar("construir-05-o-menu-da-porta");
		menu?.ApertarDesenhado("Trocar a senha");
		yield return 0.3;
		menu?.DigitarNaCaixa("");
		yield return 0.6;
		Afirmar("trocar por uma senha em branco destranca: a porta passa a abrir pra qualquer um",
				srv.BlocoDeTeste(zona, vao.X, vao.Y) is { Trancado: false } && cli.Blocos.TryGetValue(vao, out GameClient.BlocoInfo aberta) && !aberta.Trancado);

		// ------------------------------------------------------------ 8) desmanchar
		Nota("-- 8) o botao direito desmancha, e o chao volta --");
		Tecla(Key.B);
		yield return 0.3;
		Clicar(paredes[0].X, paredes[0].Y, MouseButton.Right);
		yield return 0.5;
		Afirmar("o botao direito na minha parede a desmancha nas duas pontas: sem bloco, sem tile, sem barrar",
				srv.BlocoDeTeste(zona, px0, py0) == null && mundo.BlocoDesenhadoDeTeste(px0, py0) == null
				&& mundo.Colisao?.BlockedCell(px0, py0) == false && !srv.BarraDeTeste(zona, px0, py0));
		Tecla(Key.Escape);
		yield return 0.3;
		Afirmar("o Esc tambem sai do modo", !modo.NaTela);

		// ------------------------------------------------------------ 9) a porta do MAPA que cai
		Nota("-- 9) a porta do mapa derrubada some da tela --");
		if (srv.PortaDoMapaDeTeste(zona) is { } pm)
		{
			Afirmar("(montagem) a porta do mapa esta desenhada e fechada", mundo.PortaDoMapaDeTeste(pm.X, pm.Y) != null && mundo.Colisao?.BlockedCell(pm.X, pm.Y) == true,
					$"({pm.X},{pm.Y})");
			srv.DerrubarCelulaDeTeste(zona, pm.X, pm.Y);
			yield return 0.6;
			Afirmar("derrubada, o desenho dela sai e a celula abre no cliente", mundo.PortaDoMapaDeTeste(pm.X, pm.Y) == null && mundo.Colisao?.BlockedCell(pm.X, pm.Y) == false);

			srv.ConsertarCenarioDeTeste(cli.LocalId);
			yield return 2.5;
			Afirmar("refeito o cenario, a porta volta: desenhada e fechada", mundo.PortaDoMapaDeTeste(pm.X, pm.Y) != null && mundo.Colisao?.BlockedCell(pm.X, pm.Y) == true);

			World.PortaCaidaFicaDesenhadaDeTeste = true;
			srv.DerrubarCelulaDeTeste(zona, pm.X, pm.Y);
			yield return 0.6;
			Afirmar("(defeito injetado: a porta caida fica desenhada) a celula abre e a porta continua na tela, fechada",
					mundo.PortaDoMapaDeTeste(pm.X, pm.Y) != null && mundo.Colisao?.BlockedCell(pm.X, pm.Y) == false);
			World.PortaCaidaFicaDesenhadaDeTeste = false;
			srv.ConsertarCenarioDeTeste(cli.LocalId);
			yield return 2.5;
		}
		else Afirmar("(montagem) a zona tem porta de mapa", false);

		Afirmar("o cenario refeito nao levou a base: os blocos continuam desenhados", mundo.BlocoDesenhadoDeTeste(miolo.X, miolo.Y) != null
				&& mundo.PortaErguidaDeTeste(vao.X, vao.Y) != null && mundo.Colisao?.BlockedCell(paredes[^1].X, paredes[^1].Y) == true);

		// o palco volta: nada desta rodada fica no `bases.json` da bancada
		srv.ZerarBasesDeTeste();
		yield return 0.6;
		Afirmar("zeradas as bases no servidor, o cliente apaga tudo: sem tile, sem porta, sem barrar",
				cli.Blocos.Count == 0 && mundo.BlocoDesenhadoDeTeste(miolo.X, miolo.Y) == null && mundo.PortaErguidaDeTeste(vao.X, vao.Y) == null
				&& mundo.Colisao?.BlockedCell(paredes[^1].X, paredes[^1].Y) == false && !mundo.CelulaSobTeto(miolo.X, miolo.Y));
	}
}
