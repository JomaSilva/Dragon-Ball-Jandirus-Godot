using Godot;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO BOTAO NOVO (`--diagbotoes`, 2026-11-01, o pedido 215): o dono pediu os botoes dos menus no estilo das fotos dele (*"Faca os
/// botoes dos menus do jogo nesse estilo, botoes de escolher gravidade, % do peso, etc, manter um padrao visual no jogo"*). Uma amostra de
/// quatro botoes DO TEMA numa camada por cima do jogo, em lugares conhecidos, fotografada e medida no PIXEL:
///   B1. o botao em repouso e AZUL, em DEGRADE (o alto mais claro que o pe);
///   B2. ele e INCLINADO: o canto de cima a esquerda, dentro do retangulo do botao, e o fundo, e o de baixo a esquerda e botao;
///   B3. o marcado (o destaque) e AMARELO-LARANJA;
///   B4. o desligado e CINZA;
///   B5. a letra do botao e engrossada e tem contorno -- foto `botoes-1-amostra`.
/// Os menus de verdade (gravidade, pesos, o menu P) saem nas fotos dos robos deles (`--diagmaquinas`, `--diagpesosfreeza`, `--diagabas`).
///
///     Godot --path . --host --rede 12842 --diagbotoes --raca Human --conta bancada_botoes --nome Botao
/// </summary>
public partial class RoboDosBotoes : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => GameClient.Instance is { Connected: true, LocalId: > 0 } && World.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou e o mundo nasceu");
		if (!pronto) return;
		await Segundos(1.5);
		GD.Print("[dbotoes] ===== O BOTAO NOVO (dono, 2026-11-01, o pedido 215) =====");

		// A AMOSTRA: quatro botoes do tema numa camada por cima de tudo, sobre o fundo do tema, em lugares conhecidos
		var camada = new CanvasLayer { Layer = 120 };
		AddChild(camada);
		camada.AddChild(new ColorRect { Color = Tema.Fundo, Position = new Vector2(40, 40), Size = new Vector2(760, 230) });
		var caixa = new Control { Position = new Vector2(40, 40), Size = new Vector2(760, 230), MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(caixa);
		camada.AddChild(caixa);
		Button Botao(string texto, Vector2 onde, bool marcado = false, bool desligado = false)
		{
			var b = new Button
			{
				Text = texto, Position = onde, Size = new Vector2(300, 56), ToggleMode = marcado, ButtonPressed = marcado, Disabled = desligado,
				FocusMode = Control.FocusModeEnum.None, MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			caixa.AddChild(b);
			return b;
		}
		Button repouso = Botao("Gravidade 10x", new Vector2(30, 30));
		Button marcado = Botao("Peso 50%", new Vector2(400, 30), marcado: true);
		Button desligado = Botao("Desligado", new Vector2(30, 130), desligado: true);
		Botao("Voltar", new Vector2(400, 130));
		await Segundos(0.6);
		Image? foto = await Foto();
		Fotografar(foto, "botoes-1-amostra");
		if (foto == null) { Conferir(false, "(montagem) ha foto (a bancada roda com janela)"); camada.QueueFree(); return; }

		Rect2 r = repouso.GetGlobalRect();
		Color alto = Px(foto, r.Position.X + r.Size.X * 0.5f, r.Position.Y + 9);
		Color pe = Px(foto, r.Position.X + r.Size.X * 0.5f, r.End.Y - 7);
		Conferir(Luz(alto) > Luz(pe) + 0.08f && alto.B > alto.R + 0.05f && pe.B > pe.R + 0.05f,
				 $"B1. O BOTAO EM REPOUSO E AZUL, EM DEGRADE: o alto mais claro que o pe, os dois azuis [alto {Hex(alto)}, pe {Hex(pe)}]");

		Color cantoDeCima = Px(foto, r.Position.X + 3, r.Position.Y + 4);
		Color cantoDeBaixo = Px(foto, r.Position.X + 5, r.End.Y - 6);
		Conferir(Perto(cantoDeCima, Tema.Fundo) && !Perto(cantoDeBaixo, Tema.Fundo),
				 $"B2. ELE E INCLINADO: o canto de cima a esquerda (dentro do retangulo) e o fundo, e o de baixo a esquerda e botao "
				 + $"[cima {Hex(cantoDeCima)}, baixo {Hex(cantoDeBaixo)}, fundo {Hex(Tema.Fundo)}]");

		Rect2 rm = marcado.GetGlobalRect();
		Color amarelo = Px(foto, rm.Position.X + rm.Size.X * 0.5f, rm.End.Y - 9);
		Conferir(amarelo.R > amarelo.B + 0.3f && amarelo.G > amarelo.B + 0.15f,
				 $"B3. O MARCADO (O DESTAQUE) E AMARELO-LARANJA [{Hex(amarelo)}]");

		Rect2 rd = desligado.GetGlobalRect();
		Color cinza = Px(foto, rd.Position.X + rd.Size.X * 0.5f, rd.End.Y - 9);
		Conferir(Math.Abs(cinza.R - cinza.G) < 0.08f && Math.Abs(cinza.G - cinza.B) < 0.12f && Luz(cinza) > 0.3f,
				 $"B4. O DESLIGADO E CINZA [{Hex(cinza)}]");

		Conferir(repouso.GetThemeConstant("outline_size") >= 3 && repouso.GetThemeFont("font") is FontVariation { VariationEmbolden: > 0.3f },
				 $"B5. A LETRA DO BOTAO E ENGROSSADA E TEM CONTORNO [contorno {repouso.GetThemeConstant("outline_size")} px; fonte "
				 + $"{repouso.GetThemeFont("font")?.GetType().Name}]");
		camada.QueueFree();
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private static Color Px(Image img, float x, float y) =>
		img.GetPixel(Math.Clamp((int)x, 0, img.GetWidth() - 1), Math.Clamp((int)y, 0, img.GetHeight() - 1));

	private static float Luz(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

	private static bool Perto(Color a, Color b) =>
		Math.Abs(a.R - b.R) <= 0.035f && Math.Abs(a.G - b.G) <= 0.035f && Math.Abs(a.B - b.B) <= 0.035f;

	private static string Hex(Color c) => c.ToHtml(false);

	private async System.Threading.Tasks.Task<Image?> Foto()
	{
		await Quadros(2);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	private void Fotografar(Image? img, string nome)
	{
		if (img == null) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
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
		foreach (string l in _linhas) GD.Print("[dbotoes] " + l);
		GD.Print(_falhas.Count == 0 ? "[dbotoes] ===== TUDO OK =====" : $"[dbotoes] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dbotoes]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
