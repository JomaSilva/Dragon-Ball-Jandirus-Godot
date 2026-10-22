using Godot;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO CELL POD (`--diagcellpod`, 2026-10-21). O pedido do dono: o mainframe de androides com o Bio-Android Lab ou o Neo Tuffle Lab
/// passa a ser desenhado no Cell Pod (`Assets/Sprites/DU/Items/Cell Pod.png`), e com o Android Lab continua o desenho padrao. A regra mora
/// em `Core/Tech/ArteDoLaboratorio.cs` e o servidor a manda no pacote das construcoes (`GameServer.ArteDaObra`). Com janela e `--host`, um
/// humano no campo aberto, com o palco do servidor (`GameServer.CellPodPalco.cs`): quatro mainframes CRUS, dele, numa fileira acima do corpo.
///
///   C1. crus, os quatro saem no desenho padrao -- foto `cellpod-1-crus`;
///   C2. PELOS VERBOS DE PRODUCAO, pelo fio: o humano chega perto do primeiro e instala o Android Lab, e do segundo e instala o Bio-Android
///       Lab; o palco poe o Neo Tuffle Lab no terceiro (so um Tsufurujin o instala; o menu dele e da `--diagtuffle`); o quarto fica cru;
///   C3. NA TELA: (a) o do bio e o do Neo Tuffle no Cell Pod -- a folha, a animacao `default` de 31 quadros de 48x87 em loop --, o do
///       androide e o cru no padrao (`64x64tech`, `android_pod`, 32x64); (b) o eixo de cada desenho no meio da celula dele e o pe no chao
///       dela (o pod de 48 px vai 8 px pra esquerda); (c) o pod ANDA (os quadros trocam) -- fotos `cellpod-2-os-quatro` e, de perto,
///       `cellpod-3-bio-de-perto`, `cellpod-4-neotuffle-de-perto`, `cellpod-5-android-de-perto`;
///   C4. NA FOTO, e nao no node: a foto do CHAO (as quatro obras transparentes) contra a com elas -- o que mudou em volta de cada uma e o
///       desenho dela. (a) a caixa do que mudou tem o tamanho do desenho que o laboratorio pede (48x87 no pod, 32x64 no padrao); (b) o meio
///       dela cai no meio da celula, e o pe no chao da celula;
///   C5. CONTRA-EXEMPLO (`ArteDoLaboratorio.LabSemCellPodDeTeste`, nenhum laboratorio troca o desenho): o servidor manda o padrao pros quatro,
///       e as reguas do C3a e do C4a REPROVAM -- foto `cellpod-6-defeito-sem-pod`.
///
///     Godot --path . --host --rede 10102 --campoteste 8 --horateste 0.5 --semfauna --diagcellpod --raca Human --conta bancada_cellpod --nome Cientista
/// </summary>
public partial class RoboDoCellPod : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];
	private int _ok, _eu;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>
	/// A FILEIRA: as colunas (em tiles a partir do corpo) e quantas linhas acima dele. Na camera do jogo (zoom 3, ~13 x 7 tiles na tela de
	/// 1280x720) os quatro cabem inteiros, o pod (2,7 tiles de altura) nao bate no alto da tela, e nenhum fica atras do corpo nem do nome dele.
	/// Tres tiles entre o do meio e o da ponta: o alcance da tecla E e de dois.
	/// </summary>
	private static readonly int[] Colunas = [-5, -2, 2, 5];
	private const int Linha = 1;

	/// <summary>O laboratorio que cada um da fileira recebe, na ordem: o Android Lab (pelo verbo), o Bio-Android Lab (pelo verbo), o Neo Tuffle Lab (pelo palco) e nenhum.</summary>
	private static readonly int[] Labs = [1, 2, 3, 0];
	private static readonly string[] Nomes = ["Android Lab", "Bio-Android Lab", "Neo Tuffle Lab", "cru"];

	// OS DESENHOS ESPERADOS, escritos aqui de novo (e nao lidos das constantes da producao): a folha, o estado e o tamanho do quadro.
	private const string FolhaDoPod = "res://Assets/Sprites/DU/Items/Cell Pod.tres", FolhaPadrao = "res://Assets/Sprites/Misc/Objects/Technology/64x64tech.tres";
	private static readonly Vector2 QuadroDoPod = new(48, 87), QuadroPadrao = new(32, 64);

	private static bool QuerOPod(int lab) => lab is 2 or 3;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = false;
		S?.PalcoCP_Limpar();
		Fechar();
	}

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private bool Disse(string trecho) => _ditas.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia?.PosicaoLocal != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || World.Instancia is not { } mundo) return;
		int eu = _eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dcellpod] ===== O CELL POD NO LABORATORIO DO BIO E DO NEO TUFFLE (dono, 2026-10-21) =====");
		srv.CravarMeioDiaDaVariedade(eu);   // o meio-dia DA ZONA e o ceu limpo: a foto do chao contra a com as obras sem nuvem passando
		cli.Falou += Ouvir;
		// O HUD SAI DAS FOTOS: o painel do alto da tela cobriria o topo do pod da ponta (o chat fica -- e ele mora embaixo).
		if (Hud.Instancia is { } hud) hud.Visible = false;
		try
		{
			await Segundos(2.0);
			List<(int X, int Y)> celulas = srv.PalcoCP_Montar(eu, Colunas, Linha);
			bool montou = celulas.Count == Colunas.Length && await Ate(() => celulas.All(c => mundo.ObraDeTeste(c.X, c.Y) != null), 6);
			Conferir(montou, $"(palco) os quatro mainframes crus de pe, e na minha tela [{string.Join(" ", celulas)}]");
			if (!montou) return;
			await Segundos(0.5);

			// ---------------------------------------------------------- C1. crus
			string[] crus = [.. celulas.Select(c => Contar(mundo.ObraDeTeste(c.X, c.Y)))];
			Conferir(celulas.All(c => DesenhoDe(mundo.ObraDeTeste(c.X, c.Y), 0)),
					 $"C1. crus (sem laboratorio), os quatro saem no desenho padrao do mainframe [{string.Join(" | ", crus)}]");
			await Fotografar("user://cellpod-1-crus.png");

			// ---------------------------------------------------------- C2. os laboratorios, pelos verbos
			srv.PalcoCP_IrAte(eu, 0);
			await Segundos(0.4);
			cli.SendTech("lab_androide");
			bool androide = await Ate(() => srv.PalcoCP_Lab(0) == 1 && Disse("vira um Android Lab"), 5);
			srv.PalcoCP_IrAte(eu, 1);
			await Segundos(0.4);
			cli.SendTech("lab_bio");
			bool bio = await Ate(() => srv.PalcoCP_Lab(1) == 2 && Disse("vira um Bio-Android Lab"), 5);
			srv.PalcoCP_PorOLab(2, 3);
			srv.PalcoCP_Voltar(eu);
			Conferir(androide && bio && srv.PalcoCP_Lab(2) == 3 && srv.PalcoCP_Lab(3) == 0,
					 "C2. PELOS VERBOS, PELO FIO: perto do primeiro o humano instala o Android Lab (`lab_androide`), perto do segundo o Bio-Android Lab "
					 + $"(`lab_bio`); o palco poe o Neo Tuffle Lab no terceiro, e o quarto fica cru [labs {srv.PalcoCP_Lab(0)}, {srv.PalcoCP_Lab(1)}, "
					 + $"{srv.PalcoCP_Lab(2)}, {srv.PalcoCP_Lab(3)}; ouvi: {string.Join(" | ", _ditas.TakeLast(2))}]");

			// O DESENHO NOVO CHEGA PELO PACOTE DAS CONSTRUCOES, e a camera volta pro meio da fileira
			await Ate(() => Enumerable.Range(0, 4).All(i => DesenhoDe(mundo.ObraDeTeste(celulas[i].X, celulas[i].Y), Labs[i])), 6);
			await Segundos(1.2);

			// ---------------------------------------------------------- C3. o que esta na tela
			ObraDesenhada?[] obras = [.. celulas.Select(c => mundo.ObraDeTeste(c.X, c.Y))];
			string agora = string.Join(" | ", Enumerable.Range(0, 4).Select(i => $"{Nomes[i]}: {Contar(obras[i])}"));
			Conferir(Enumerable.Range(0, 4).All(i => DesenhoDe(obras[i], Labs[i])),
					 $"C3a. **O DO BIO E O DO NEO TUFFLE NO CELL POD** (a folha, a animacao `default` de 31 quadros de 48x87 em loop) -- o do Android Lab e o cru "
					 + $"no desenho padrao (`64x64tech`, `android_pod`, 32x64) [{agora}]");
			Conferir(Enumerable.Range(0, 4).All(i => Centrado(obras[i], celulas[i])),
					 $"C3b. o eixo de cada desenho cai no meio da celula dele, e o pe no chao dela (o pod de 48 px vai 8 px pra esquerda; o padrao, de 32, nao sai do lugar) "
					 + $"[{string.Join(" | ", Enumerable.Range(0, 4).Select(i => $"{Nomes[i]}: {Eixo(obras[i], celulas[i])}"))}]");
			(bool andou, string quadros) = await Animado(obras[1], obras[2]);
			Conferir(andou, $"C3c. o pod ANDA: em 0,6 s os quadros do do bio e do do Neo Tuffle trocam (o loop a 10 por segundo) [{quadros}]");

			Image? foto = await Foto();
			await Guardar(foto, "user://cellpod-2-os-quatro.png");
			await GuardarDePerto(foto, obras[1], "user://cellpod-3-bio-de-perto.png");
			await GuardarDePerto(foto, obras[2], "user://cellpod-4-neotuffle-de-perto.png");
			await GuardarDePerto(foto, obras[0], "user://cellpod-5-android-de-perto.png");

			// ---------------------------------------------------------- C4. na foto
			(bool tamanho, bool lugar, string caixas) = await NaFoto(celulas);
			Conferir(tamanho, $"C4a. **NA FOTO**: o que muda em volta de cada mainframe (a foto do chao contra a com eles) tem o tamanho do desenho que o "
							  + $"laboratorio pede -- 48x87 nos dois pods, 32x64 no Android Lab e no cru [{caixas}]");
			Conferir(lugar, $"C4b. ...e o meio do que mudou cai no meio da celula de cada um, com o pe no chao dela [{caixas}]");

			// ---------------------------------------------------------- C5. contra-exemplo
			Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = true;
			bool reprovaC3, reprovaC4;
			string comDefeito, caixasDoDefeito;
			try
			{
				srv.PalcoCP_Reenviar(eu);
				await Ate(() => !DesenhoDe(mundo.ObraDeTeste(celulas[1].X, celulas[1].Y), 2), 5);
				await Segundos(0.6);
				ObraDesenhada?[] semPod = [.. celulas.Select(c => mundo.ObraDeTeste(c.X, c.Y))];
				comDefeito = string.Join(" | ", Enumerable.Range(0, 4).Select(i => $"{Nomes[i]}: {Contar(semPod[i])}"));
				reprovaC3 = !Enumerable.Range(0, 4).All(i => DesenhoDe(semPod[i], Labs[i]));
				(bool t2, _, caixasDoDefeito) = await NaFoto(celulas);
				reprovaC4 = !t2;
				await Guardar(await Foto(), "user://cellpod-6-defeito-sem-pod.png");
			}
			finally { Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = false; }
			Conferir(reprovaC3 && reprovaC4,
					 "CONTRA-EXEMPLO (defeito injetado: nenhum laboratorio troca o desenho): o servidor manda o desenho padrao pros quatro, e as reguas do "
					 + $"C3a e do C4a REPROVAM [{comDefeito} || {caixasDoDefeito}]");
			srv.PalcoCP_Reenviar(eu);
			await Ate(() => DesenhoDe(mundo.ObraDeTeste(celulas[1].X, celulas[1].Y), 2), 5);
		}
		finally
		{
			cli.Falou -= Ouvir;
			if (Hud.Instancia is { } h) h.Visible = true;
		}
	}

	// =====================================================================
	// A REGUA DO NODE
	// =====================================================================
	private static AnimatedSprite2D? Sprite(ObraDesenhada? o) => o?.GetChildren().OfType<AnimatedSprite2D>().FirstOrDefault();

	/// <summary>O desenho desta obra e o que este laboratorio pede? A folha e o estado que chegaram, e o quadro que o sprite desenha.</summary>
	private static bool DesenhoDe(ObraDesenhada? o, int lab)
	{
		if (o == null || Sprite(o) is not { SpriteFrames: { } f } s) return false;
		string anim = s.Animation;
		if (!f.HasAnimation(anim) || f.GetFrameTexture(anim, 0) is not { } q) return false;
		return QuerOPod(lab)
			? o.Arte == FolhaDoPod && o.Estado == "default" && anim == "default" && f.GetFrameCount(anim) == 31 && q.GetSize() == QuadroDoPod
			  && f.GetAnimationLoopMode(anim) == SpriteFrames.LoopMode.Linear
			: o.Arte == FolhaPadrao && o.Estado == "android_pod" && anim == "android_pod" && q.GetSize() == QuadroPadrao;
	}

	private static string Contar(ObraDesenhada? o) =>
		o == null ? "(nao desenhada)"
		: Sprite(o) is { SpriteFrames: { } f } s
			? $"{o.Arte.Split('/')[^1]}:{s.Animation} {f.GetFrameCount(s.Animation)}q {f.GetFrameTexture(s.Animation, 0)?.GetSize()} em {s.Position}"
			: $"{o.Arte.Split('/')[^1]}:(sem sprite)";

	/// <summary>O meio do desenho no meio da celula (x) e o pe na base dela (y), em px de mundo.</summary>
	private static bool Centrado(ObraDesenhada? o, (int X, int Y) c)
	{
		if (Sprite(o) is not { SpriteFrames: { } f } s || f.GetFrameTexture(s.Animation, 0) is not { } q) return false;
		const int t = Jandirus.Core.World.ZoneCollision.TileSize;
		float meio = o!.Position.X + s.Position.X + q.GetSize().X / 2f, pe = o.Position.Y + s.Position.Y + q.GetSize().Y;
		return Mathf.Abs(meio - (c.X * t + t / 2f)) < 0.5f && Mathf.Abs(pe - (c.Y + 1) * t) < 0.5f;
	}

	private static string Eixo(ObraDesenhada? o, (int X, int Y) c)
	{
		if (Sprite(o) is not { SpriteFrames: { } f } s || f.GetFrameTexture(s.Animation, 0) is not { } q) return "(sem sprite)";
		const int t = Jandirus.Core.World.ZoneCollision.TileSize;
		return $"meio {o!.Position.X + s.Position.X + q.GetSize().X / 2f - (c.X * t + t / 2f):+0.#;-0.#;0} px do meio da celula, "
			   + $"pe {o.Position.Y + s.Position.Y + q.GetSize().Y - (c.Y + 1) * t:+0.#;-0.#;0}";
	}

	/// <summary>Os quadros dos dois sprites em 0,6 s: quantos diferentes cada um mostrou.</summary>
	private async System.Threading.Tasks.Task<(bool, string)> Animado(ObraDesenhada? a, ObraDesenhada? b)
	{
		var vistosA = new HashSet<int>();
		var vistosB = new HashSet<int>();
		double fim = Agora + 0.6;
		while (Agora < fim)
		{
			if (Sprite(a) is { } sa) vistosA.Add(sa.Frame);
			if (Sprite(b) is { } sb) vistosB.Add(sb.Frame);
			await Quadros(1);
		}
		return (vistosA.Count >= 3 && vistosB.Count >= 3 && Sprite(a)?.IsPlaying() == true && Sprite(b)?.IsPlaying() == true,
				$"bio {vistosA.Count} quadro(s), Neo Tuffle {vistosB.Count}");
	}

	// =====================================================================
	// A REGUA DA FOTO
	// =====================================================================
	/// <summary>
	/// A FOTO DO CHAO contra a com as obras: as quatro ficam TRANSPARENTES (o `Modulate` -- o `Visible` delas o mundo reescreve a cada
	/// snapshot, pelo veu dos interiores) por tres quadros, foto; voltam, tres quadros, foto. Em volta de cada uma (a celula, com folga de dois
	/// tiles pros lados e tres pra cima), a caixa dos pixels que mudaram e o desenho dela. Devolve (tamanho certo, lugar certo, o relato).
	/// </summary>
	private async System.Threading.Tasks.Task<(bool, bool, string)> NaFoto(List<(int X, int Y)> celulas)
	{
		World mundo = World.Instancia!;
		ObraDesenhada?[] obras = [.. celulas.Select(c => mundo.ObraDeTeste(c.X, c.Y))];
		if (obras.Any(o => o == null)) return (false, false, "(obra nao desenhada)");
		foreach (ObraDesenhada? o in obras) o!.Modulate = new Color(1, 1, 1, 0);
		Image? chao;
		try
		{
			await Quadros(3);
			chao = await Foto();
		}
		finally { foreach (ObraDesenhada? o in obras) if (IsInstanceValid(o)) o!.Modulate = Colors.White; }
		await Quadros(3);
		Image? com = await Foto();
		if (chao == null || com == null) return (false, false, "(sem foto: headless nao renderiza)");

		bool tamanho = true, lugar = true;
		var relato = new List<string>();
		const int t = Jandirus.Core.World.ZoneCollision.TileSize;
		for (int i = 0; i < obras.Length; i++)
		{
			Transform2D tr = obras[i]!.GetGlobalTransformWithCanvas();
			float escala = tr.Scale.X;
			// A CELULA NA TELA (o no fica no canto de baixo, a esquerda), e a janela em volta dela: meio tile pros lados e tres e meio pra cima --
			// cabe o pod (que passa 8 px de cada lado e sobe 2,7 tiles) e nao cabe o corpo, nem o nome, nem o vizinho
			Vector2 baseEsq = tr * Vector2.Zero, baseDir = tr * new Vector2(t, 0);
			var janela = new Rect2I((int)(baseEsq.X - 0.5f * t * escala), (int)(baseEsq.Y - 3.5f * t * escala),
									(int)((baseDir.X - baseEsq.X) + t * escala), (int)(3.75f * t * escala));
			Rect2I? caixa = CaixaDaDiferenca(chao, com, janela);
			Vector2 quer = QuerOPod(Labs[i]) ? QuadroDoPod : QuadroPadrao;
			if (caixa is not { } cx)
			{
				tamanho = lugar = false;
				relato.Add($"{Nomes[i]}: nada mudou");
				continue;
			}
			float w = cx.Size.X / escala, h = cx.Size.Y / escala;
			float meio = (cx.Position.X + cx.Size.X / 2f - (baseEsq.X + baseDir.X) / 2f) / escala, pe = (cx.End.Y - baseEsq.Y) / escala;
			bool tamanhoAqui = Mathf.Abs(w - quer.X) <= 3 && Mathf.Abs(h - quer.Y) <= 3;
			bool lugarAqui = Mathf.Abs(meio) <= 2 && Mathf.Abs(pe) <= 3;
			tamanho &= tamanhoAqui;
			lugar &= lugarAqui;
			relato.Add($"{Nomes[i]}: {w:0}x{h:0} (quer {quer.X:0}x{quer.Y:0}), meio {meio:+0.#;-0.#;0}, pe {pe:+0.#;-0.#;0}");
		}
		return (tamanho, lugar, string.Join(" | ", relato));
	}

	/// <summary>A caixa dos pixels que diferem entre as duas fotos dentro da janela (mais de 0,06 em algum canal), ou nula.</summary>
	private static Rect2I? CaixaDaDiferenca(Image a, Image b, Rect2I janela)
	{
		int x0 = Math.Max(janela.Position.X, 0), y0 = Math.Max(janela.Position.Y, 0);
		int x1 = Math.Min(janela.End.X, Math.Min(a.GetWidth(), b.GetWidth())), y1 = Math.Min(janela.End.Y, Math.Min(a.GetHeight(), b.GetHeight()));
		int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
				if (Mathf.Max(Mathf.Abs(p.R - q.R), Mathf.Max(Mathf.Abs(p.G - q.G), Mathf.Abs(p.B - q.B))) <= 0.06f) continue;
				minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
				minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
			}
		return maxX < 0 ? null : new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
	}

	// =====================================================================
	// AS FOTOS
	// =====================================================================
	private async System.Threading.Tasks.Task<Image?> Foto()
	{
		await Quadros(2);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	private async Tarefa Guardar(Image? foto, string destino)
	{
		if (foto == null) { Nota("sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		foto.SavePng(caminho);
		Nota("foto salva em " + caminho);
		await Tarefa.CompletedTask;
	}

	private async Tarefa Fotografar(string destino) => await Guardar(await Foto(), destino);

	/// <summary>DE PERTO: o recorte em volta do desenho (um tile de folga pros lados e em cima), ampliado 2x sem filtro.</summary>
	private async Tarefa GuardarDePerto(Image? foto, ObraDesenhada? o, string destino)
	{
		if (foto == null || Sprite(o) is not { SpriteFrames: { } f } s || f.GetFrameTexture(s.Animation, 0) is not { } q) { Nota($"sem foto de perto pra {destino}"); return; }
		const int t = Jandirus.Core.World.ZoneCollision.TileSize;
		Transform2D tr = s.GetGlobalTransformWithCanvas();
		Vector2 a = tr * new Vector2(-t, -t), b = tr * (q.GetSize() + new Vector2(t, t / 2f));
		var r = new Rect2I((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y)).Intersection(new Rect2I(0, 0, foto.GetWidth(), foto.GetHeight()));
		if (r.Size.X <= 0 || r.Size.Y <= 0) { Nota($"o recorte de {destino} saiu fora da tela"); return; }
		Image recorte = foto.GetRegion(r);
		recorte.Resize(r.Size.X * 2, r.Size.Y * 2, Image.Interpolation.Nearest);
		await Guardar(recorte, destino);
	}

	// =====================================================================
	// O RELOGIO
	// =====================================================================
	private static double Agora => Time.GetTicksMsec() / 1000.0;

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Agora + s;
		while (Agora < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Agora + segundos;
		while (Agora < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[dcellpod] " + l);
		GD.Print($"[dcellpod] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dcellpod] ===== TUDO OK =====" : $"[dcellpod] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dcellpod]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
