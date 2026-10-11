using System.Collections;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO MODO DE CONSTRUIR (`--diagconstruir`) -- a metade do CLIENTE dos pedidos do dono de 2026-10-09:
/// a tecla B, a barra, o fantasma, o clique que ergue, a paleta, a pergunta da senha, a tecla E na porta,
/// o botao direito que desmancha, e o desenho da porta do MAPA que cai. E a do pedido de 2026-10-10 (a
/// familia 10): o seletor "Pagar com" do canto inferior direito -- zeni ou recursos, o preco, o saldo, a troca
/// e a escolha que fica.
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
		ModoDeConstruir.MoedaNaoGravaDeTeste = ModoDeConstruir.PagamentoNaoEInterfaceDeTeste = false;
		ContaDeObra.SemDescontarDeTeste = ContaDeObra.PartindoDaCarteiraVivaDeTeste = false;
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

	/// <summary>O mouse vai pra um ponto da TELA (e nao pra uma celula do mundo): e como se chega num controle.</summary>
	private void MoverNaTela(Vector2 p) =>
		GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);

	/// <summary>Um clique inteiro do botao esquerdo num ponto da tela -- o mouse de verdade em cima de um botao.</summary>
	private void ClicarNaTela(Vector2 p)
	{
		MoverNaTela(p);
		foreach (bool desce in new[] { true, false })
			GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = desce, Position = p, GlobalPosition = p }, true);
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

		// ERGUER CUSTA (familia 10). Nas nove primeiras o bolso nao e o assunto: o Pedreiro nasce sem um zeni, e
		// ganha de sobra. A familia 10 poe os saldos que ela mede.
		srv.SaldoDeObraDeTeste(cli.LocalId, 100_000, 100_000);
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

		// ------------------------------------------------------------ 10) pagar com: zeni ou recursos
		foreach (object passo in OPagamento(cli, srv, modo, zona, ex, ey)) yield return passo;

		// o palco volta: nada desta rodada fica no `bases.json` da bancada
		srv.ZerarBasesDeTeste();
		yield return 0.6;
		Afirmar("zeradas as bases no servidor, o cliente apaga tudo: sem tile, sem porta, sem barrar",
				cli.Blocos.Count == 0 && mundo.BlocoDesenhadoDeTeste(miolo.X, miolo.Y) == null && mundo.PortaErguidaDeTeste(vao.X, vao.Y) == null
				&& mundo.Colisao?.BlockedCell(paredes[^1].X, paredes[^1].Y) == false && !mundo.CelulaSobTeto(miolo.X, miolo.Y));
	}

	// =====================================================================
	// 10) PAGAR COM: ZENI OU RECURSOS (pedido do dono, 2026-10-10)
	// =====================================================================
	/// <summary>
	/// *"construir paredes etc tem custo em zenni/ recursos o usuario escolhe na hora de construir e essa opcao
	/// fica selecionada ate ele quiser trocar (e uma opcao no canto inferior direito)"*.
	///
	/// A metade da TELA: o seletor esta no canto certo, mostra o preco e o saldo, troca com um clique de
	/// verdade, o fantasma avisa de vermelho quando o bolso nao paga, o arrasto para na peca que nao da pra
	/// pagar, e a escolha fica -- no arquivo, ao sair e voltar pro modo, e numa sessao nova. Quem cobra e o
	/// servidor, e a conta dele e a familia 11 da `--blocoteste`; aqui os dois lados sao lidos juntos.
	///
	/// O bolso da cena e posto pelo servidor (`SaldoDeObraDeTeste`): duas paredes e um troco em cada moeda.
	/// </summary>
	private IEnumerable OPagamento(GameClient cli, GameServer srv, ModoDeConstruir modo, ZoneKey zona, int ex, int ey)
	{
		Nota("-- 10) pagar com: o seletor do canto, o preco, o saldo, a troca e a escolha que fica --");
		int eu = cli.LocalId;
		const ClasseDeBloco P = ClasseDeBloco.Parede;
		double pZ = PrecoDeBloco.Na(P, Moeda.Zeni), pR = PrecoDeBloco.Na(P, Moeda.Recursos);
		double z0 = 2 * pZ + Math.Floor(pZ / 2), r0 = 2 * pR + Math.Floor(pR / 4);   // 25 zeni e 45 recursos, com a tabela de hoje

		(double Zeni, double Recursos) Bolso() => srv.ObraPagaDeTeste(eu) is { } o ? (o.Zeni, o.Recursos) : (-1, -1);
		bool Sao(double zeni, double recursos) => Math.Abs(Bolso().Zeni - zeni) < 1e-6 && Math.Abs(Bolso().Recursos - recursos) < 1e-6;
		Moeda? NoServidor() => srv.ObraPagaDeTeste(eu)?.Moeda;
		string Tenho(double quanto, Moeda m) => $"Você tem {PrecoDeBloco.Quantia(quanto)} {PrecoDeBloco.Chave(m)}";
		string Custa(Moeda m) => $"Parede: {PrecoDeBloco.Etiqueta(P, m)}";
		bool DePe((int X, int Y) c) => srv.BlocoDeTeste(zona, c.X, c.Y) != null;

		// AS CELULAS LIVRES ficam a oeste do corpo (a casinha esta a leste) e acima do painel do chat.
		(int X, int Y) c1 = (ex - 2, ey - 1), c2 = (ex - 2, ey), c3 = (ex - 3, ey - 1), c4 = (ex - 3, ey);
		(int X, int Y)[] fila = [(ex - 1, ey - 2), (ex - 2, ey - 2), (ex - 3, ey - 2), (ex - 4, ey - 2)];

		// ---- o seletor na tela, no canto ----
		srv.SaldoDeObraDeTeste(eu, z0, r0);
		Tecla(Key.B);
		yield return 0.5;
		bool temParede = RodarAte(P, c1.X, c1.Y);
		Mover(c1.X, c1.Y);
		yield return 0.4;

		Vector2 tela = modo.GetViewport().GetVisibleRect().Size;
		Rect2 caixa = modo.PagamentoDeTeste;
		Afirmar("com o modo ligado o seletor \"Pagar com\" esta na tela, no CANTO INFERIOR DIREITO: encostado na direita, com 190 px livres por baixo (o lugar do radar)",
				temParede && modo.PagamentoNaTela
				&& Mathf.Abs(caixa.End.X - (tela.X - ModoDeConstruir.MargemDoPagamento)) < 1.5f
				&& Mathf.Abs(caixa.End.Y - (tela.Y - ModoDeConstruir.LivreEmbaixo)) < 1.5f
				&& caixa.Position.X > tela.X / 2 && caixa.Position.Y > tela.Y / 2 && caixa.Size.X > 150 && caixa.Size.Y > 60,
				$"de {caixa.Position} a {caixa.End}, numa tela de {tela}");
		Afirmar("quem nunca escolheu paga em ZENI: o botao aceso, o servidor de acordo, e o seletor mostra o preco da parede e o saldo",
				modo.MoedaDeTeste == Moeda.Zeni && modo.BotaoDeMoedaAcesoDeTeste(Moeda.Zeni) && !modo.BotaoDeMoedaAcesoDeTeste(Moeda.Recursos)
				&& NoServidor() == Moeda.Zeni && modo.PrecoDeTeste == Custa(Moeda.Zeni) && modo.SaldoDeTeste == Tenho(z0, Moeda.Zeni)
				&& !modo.SaldoEmVermelhoDeTeste,
				$"'{modo.PrecoDeTeste}' / '{modo.SaldoDeTeste}'");

		// ---- o fundo do seletor e interface, e nao mundo ----
		MoverNaTela(new Vector2(caixa.Position.X + caixa.Size.X / 2, caixa.Position.Y + 5));
		yield return 0.25;
		Afirmar("com o mouse em cima do seletor o fantasma some: ali o clique e da interface, e nao do mundo", modo.FantasmaDeTeste is { Visible: false });
		ModoDeConstruir.PagamentoNaoEInterfaceDeTeste = true;
		yield return 0.25;
		Afirmar("(defeito injetado: o fundo do seletor nao conta como interface) o fantasma aparece na celula que esta por baixo dele",
				modo.FantasmaDeTeste is { Visible: true });
		ModoDeConstruir.PagamentoNaoEInterfaceDeTeste = false;

		// ---- um clique troca a moeda, e a escolha vai pro arquivo ----
		int blocosAntes = srv.BlocosDeTeste(zona);
		ClicarNaTela(modo.BotaoDeMoedaDeTeste(Moeda.Recursos));
		yield return 0.5;
		Afirmar("um clique de verdade no botao \"Recursos\" troca a moeda: ele acende, o servidor passa a cobrar em recursos, e nada e erguido por baixo do botao",
				modo.MoedaDeTeste == Moeda.Recursos && modo.BotaoDeMoedaAcesoDeTeste(Moeda.Recursos) && !modo.BotaoDeMoedaAcesoDeTeste(Moeda.Zeni)
				&& NoServidor() == Moeda.Recursos && cli.MoedaDeObraNoServidor == Moeda.Recursos && srv.BlocosDeTeste(zona) == blocosAntes,
				$"cliente {modo.MoedaDeTeste}, servidor {NoServidor()}");
		Afirmar("a escolha foi pro `config.json` na hora da troca: relido do DISCO, ele diz \"recursos\"",
				Boot.Config.PagarObraCom == "recursos" && Settings.Carregar().PagarObraCom == "recursos", Settings.Carregar().PagarObraCom);

		Mover(c1.X, c1.Y);
		yield return 0.3;
		Afirmar("o seletor passa a mostrar o preco em recursos (mais caro que em zeni) e o saldo de recursos, e o fantasma da parede esta branco",
				modo.PrecoDeTeste == Custa(Moeda.Recursos) && pR > pZ && modo.SaldoDeTeste == Tenho(r0, Moeda.Recursos) && !modo.SaldoEmVermelhoDeTeste
				&& modo.FantasmaDeTeste is { Visible: true } branco && branco.Modulate.G > 0.9f,
				$"'{modo.PrecoDeTeste}' / '{modo.SaldoDeTeste}'");
		Fotografar("construir-06-pagar-com");

		// ---- a peca sai do bolso escolhido; sem saldo, o fantasma avisa e o clique nao manda nada ----
		Clicar(c1.X, c1.Y);
		yield return 0.5;
		Afirmar("a parede erguida agora sai dos RECURSOS (o dobro), e o zeni nao se mexe -- no servidor e no seletor",
				DePe(c1) && Sao(z0, r0 - pR) && modo.SaldoDeTeste == Tenho(r0 - pR, Moeda.Recursos) && modo.ACaminhoDeTeste == 0,
				$"servidor {Bolso()}, seletor '{modo.SaldoDeTeste}'");
		Clicar(c2.X, c2.Y);
		yield return 0.5;
		Mover(c3.X, c3.Y);
		yield return 0.3;
		double troco = r0 - 2 * pR;
		Afirmar("sem saldo pra proxima parede o fantasma fica VERMELHO, a barra diz a frase que o servidor diria, e o saldo do seletor fica vermelho",
				DePe(c2) && Sao(z0, troco) && modo.FantasmaDeTeste is { Visible: true } vermelho && vermelho.Modulate.G < 0.6f
				&& modo.AvisoDeTeste == PrecoDeBloco.FaltaSaldo(P, Moeda.Recursos, troco)
				&& modo.SaldoEmVermelhoDeTeste && modo.SaldoDeTeste == Tenho(troco, Moeda.Recursos),
				$"'{modo.AvisoDeTeste}' / '{modo.SaldoDeTeste}'");
		Fotografar("construir-07-sem-saldo");

		// A DICA "[E] ..." DO MENU DE INTERACAO some no modo de construir: ali o E abre a PALETA, e a dica da porta
		// vizinha caia em cima da linha de motivo da barra (a foto 07 de 2026-10-10 mostrava as duas sobrepostas).
		Afirmar("no modo de construir a dica [E] do que esta por perto NAO aparece (ali o E abre a paleta)",
				(MenuDeInteracao.Instancia?.DicaNaTela ?? "") == "", MenuDeInteracao.Instancia?.DicaNaTela ?? "");
		string dicaComODefeito;
		MenuDeInteracao.DicaNoModoDeConstruirDeTeste = true;
		try
		{
			yield return 0.2;
			dicaComODefeito = MenuDeInteracao.Instancia?.DicaNaTela ?? "";
		}
		finally { MenuDeInteracao.DicaNoModoDeConstruirDeTeste = false; }
		yield return 0.2;
		Afirmar("(defeito injetado: a dica volta no modo de construir) a mesma checagem reprova; sem o defeito ela some de novo",
				dicaComODefeito != "" && (MenuDeInteracao.Instancia?.DicaNaTela ?? "") == "", $"com o defeito '{dicaComODefeito}'");
		int pedidosAntes = cli.RespostasDePecaDeObra;
		Clicar(c3.X, c3.Y);
		yield return 0.5;
		Afirmar("...e o clique ali nao ergue nada, nao pede nada ao servidor e nao toca no zeni (a outra moeda nao completa)",
				!DePe(c3) && Sao(z0, troco) && cli.RespostasDePecaDeObra == pedidosAntes, $"servidor {Bolso()}");

		// ---- arrastar: para na peca que o bolso nao paga, sem pedir as outras ----
		// O mouse "pula" tres celulas de um quadro pro outro (o clique numa ponta e o movimento pra outra no MESMO
		// passo do robo): as quatro tentativas saem antes de a primeira cobranca voltar do servidor.
		int Arrastar()
		{
			int antes = cli.RespostasDePecaDeObra;
			Mover(fila[0].X, fila[0].Y);
			Botao(fila[0].X, fila[0].Y, MouseButton.Left, true);
			Mover(fila[^1].X, fila[^1].Y);
			return antes;
		}

		srv.SaldoDeObraDeTeste(eu, z0, r0);
		yield return 0.4;
		int respostasAntes = Arrastar();
		yield return 0.6;
		Botao(fila[^1].X, fila[^1].Y, MouseButton.Left, false);
		yield return 0.3;
		int pedidos = cli.RespostasDePecaDeObra - respostasAntes, subiram = fila.Count(DePe);
		Afirmar("ARRASTANDO por quatro celulas de uma vez, com bolso pra duas: sobem as duas primeiras e o cliente nem PEDE as outras -- ele desconta as pecas a caminho",
				subiram == 2 && DePe(fila[0]) && DePe(fila[1]) && pedidos == 2 && modo.ACaminhoDeTeste == 0 && Sao(z0, r0 - 2 * pR),
				$"{subiram} de pe, {pedidos} pedido(s), {modo.ACaminhoDeTeste} a caminho, servidor {Bolso()}");

		srv.ZerarBasesDeTeste();   // a fila fica livre de novo (a casinha vai junto: as fotos ja foram tiradas)
		srv.SaldoDeObraDeTeste(eu, z0, r0);
		yield return 0.6;
		ContaDeObra.SemDescontarDeTeste = true;
		respostasAntes = Arrastar();
		yield return 0.6;
		Botao(fila[^1].X, fila[^1].Y, MouseButton.Left, false);
		yield return 0.3;
		pedidos = cli.RespostasDePecaDeObra - respostasAntes;
		subiram = fila.Count(DePe);
		Afirmar("(defeito injetado: o cliente nao desconta as pecas a caminho) ele pede mais do que o bolso paga -- e as outras so nao sobem porque o SERVIDOR recusa",
				pedidos > 2 && subiram == 2, $"{subiram} de pe, {pedidos} pedido(s)");
		ContaDeObra.SemDescontarDeTeste = false;

		// ---- a conta das pecas a caminho, com a cobranca chegando PARTIDA entre dois quadros ----
		// A cobranca de uma peca chega em dois pacotes: a carteira nova (`S2C.Carteira`) e, logo atras, a resposta
		// da obra. A sequencia e montada a mao, na classe de producao: saem duas paredes (A e B) com bolso pra duas
		// e um troco; chegam a carteira de A, a resposta de A e a carteira de B -- e o quadro acaba antes da
		// resposta de B. A rede de verdade nao deixa escolher esse instante.
		var conta = new ContaDeObra();
		double depoisDeA = r0 - pR, depoisDeB = r0 - 2 * pR;
		double NoMeioDaCobranca()
		{
			conta.Limpar();
			conta.Pedir(Moeda.Recursos, pR, z0, r0);
			conta.Pedir(Moeda.Recursos, pR, z0, r0);
			conta.Responder(z0, depoisDeA);                      // a resposta de A, com a carteira de depois dela
			return conta.Saldo(Moeda.Recursos, z0, depoisDeB);   // a carteira VIVA ja e a de depois de B
		}
		double noMeio = NoMeioDaCobranca();
		Afirmar("a cobranca chega em dois pacotes: com a carteira de B ja chegada e a resposta de B ainda nao, a conta da o troco -- a parede B sai dela UMA vez",
				Math.Abs(noMeio - depoisDeB) < 1e-9 && conta.ACaminho == 1, $"saldo {noMeio:0.##} (o troco e {depoisDeB:0.##})");
		ContaDeObra.PartindoDaCarteiraVivaDeTeste = true;
		double duasVezes = NoMeioDaCobranca();
		ContaDeObra.PartindoDaCarteiraVivaDeTeste = false;
		Afirmar("(defeito injetado: a conta parte da carteira viva) a parede B sai da conta DUAS vezes -- o bolso que paga mais uma parede diria que nao paga",
				Math.Abs(duasVezes - (depoisDeB - pR)) < 1e-9, $"saldo {duasVezes:0.##}");
		conta.Responder(z0, depoisDeB);
		Afirmar("...e respondida a B nao ha mais nada a caminho: a conta e a carteira viva",
				conta.ACaminho == 0 && Math.Abs(conta.Saldo(Moeda.Recursos, z0, depoisDeB) - depoisDeB) < 1e-9);

		// ---- a escolha fica: no arquivo, numa sessao nova, e ao sair e voltar pro modo ----
		ModoDeConstruir.MoedaNaoGravaDeTeste = true;
		ClicarNaTela(modo.BotaoDeMoedaDeTeste(Moeda.Zeni));
		yield return 0.4;
		Afirmar("(defeito injetado: a troca nao vai pro `config.json`) o seletor troca pro zeni e o arquivo continua dizendo \"recursos\" -- a escolha morreria com a sessao",
				modo.MoedaDeTeste == Moeda.Zeni && Settings.Carregar().PagarObraCom == "recursos", Settings.Carregar().PagarObraCom);
		ModoDeConstruir.MoedaNaoGravaDeTeste = false;

		modo.RenascerDoDiscoDeTeste();
		Afirmar("uma SESSAO NOVA (o `config.json` relido do disco, pela linha de producao) nasce na moeda gravada: recursos",
				modo.MoedaDeTeste == Moeda.Recursos && modo.BotaoDeMoedaAcesoDeTeste(Moeda.Recursos) && !modo.BotaoDeMoedaAcesoDeTeste(Moeda.Zeni));

		Tecla(Key.B);
		yield return 0.4;
		srv.EsquecerMoedaDeObraDeTeste(eu);
		Afirmar("(montagem) fora do modo o seletor some, e o servidor esqueceu a escolha -- numa sessao nova ele so conhece o zeni",
				!modo.NaTela && !modo.PagamentoNaTela && NoServidor() == Moeda.Zeni);
		Tecla(Key.B);
		yield return 0.5;
		Afirmar("A ESCOLHA FICA: ligado de novo, o seletor continua em RECURSOS e o servidor volta a cobrar em recursos (o cliente repete a escolha ao ligar o modo)",
				modo.PagamentoNaTela && modo.MoedaDeTeste == Moeda.Recursos && modo.BotaoDeMoedaAcesoDeTeste(Moeda.Recursos)
				&& NoServidor() == Moeda.Recursos && cli.MoedaDeObraNoServidor == Moeda.Recursos,
				$"cliente {modo.MoedaDeTeste}, servidor {NoServidor()}");

		// ---- e ate ele trocar: de volta ao zeni ----
		ClicarNaTela(modo.BotaoDeMoedaDeTeste(Moeda.Zeni));
		srv.SaldoDeObraDeTeste(eu, z0, r0);
		yield return 0.5;
		Clicar(c4.X, c4.Y);
		yield return 0.5;
		Afirmar("trocado pro zeni com um clique (e gravado: o arquivo diz \"zeni\"), a parede seguinte sai do ZENI pelo preco da tabela, e os recursos nao se mexem",
				modo.MoedaDeTeste == Moeda.Zeni && Settings.Carregar().PagarObraCom == "zeni" && NoServidor() == Moeda.Zeni
				&& DePe(c4) && Sao(z0 - pZ, r0) && modo.PrecoDeTeste == Custa(Moeda.Zeni) && modo.SaldoDeTeste == Tenho(z0 - pZ, Moeda.Zeni),
				$"servidor {Bolso()}, seletor '{modo.PrecoDeTeste}' / '{modo.SaldoDeTeste}', arquivo {Settings.Carregar().PagarObraCom}");

		Tecla(Key.B);
		yield return 0.3;
		Afirmar("a tecla B desliga o modo, e o seletor some com ele", !modo.NaTela && !modo.PagamentoNaTela);
	}
}
