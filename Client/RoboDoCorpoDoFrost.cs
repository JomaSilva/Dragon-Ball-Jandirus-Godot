using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO CORPO DO FROST DEMON (`--diagcorpofrost`, 2026-10-19). O dono, com um print: *"segunda forma do frost demon ta bugada"*, *"a
/// segunda forma fica por cima da forma base no frost demon"* e *"descobri q sao todas as formas de frost q estao com esse bug"* -- o corpo
/// da evolucao era desenhado POR CIMA do corpo da base, e o da base aparecia em volta. Com janela e `--host`, nascendo Humano no campo:
///   F0. a premissa (`GameServer.VirarFrostDeTeste`): o corpo do robo vira um Frost Demon NORMAL -- base "Changeling 1 Large", 1a
///       Evolucao "Changeling Full Power" (a do print), 2a "Koola Final Form" --, descansando na forma base -- foto `corpofrost-0-base.png`;
///   F1. o C, pelo fio: a 1a Evolucao. O corpo da base SOME, o da evolucao esta na tela, e nenhum pixel da base aparece em volta dele --
///       foto `corpofrost-1-primeira.png`;
///   F2. CONTRA-EXEMPLO (`Catalogo.FrostPorCimaDaBaseDeTeste`, a regra de antes): recua e sobe de novo, e o corpo da base volta a aparecer em
///       volta da evolucao -- o print do dono --, e a regua de F1 REPROVA -- foto `corpofrost-2-defeito-por-cima.png`;
///   F3. o C de novo: a 2a Evolucao, tambem sem o corpo de baixo -- foto `corpofrost-3-segunda.png`.
///
/// ============================ A REGUA E DE PIXEL, E ELA PROVA QUE ENXERGA ============================
/// "O node esta escondido" e o que a camada diz, e nao o que a tela mostra. A regua le a FOTO: cada pixel de cor viva do quadro da base que cai
/// FORA do desenho da evolucao -- sem nenhum pixel dele em volta, longe do contorno que a forma pinta na borda -- e procurado na tela, e
/// "aparece" quando a cor la e a do quadro da base. O preto do contorno do desenho fica de fora: ele se confunde com a sombra da grama. E o
/// desenho da EVOLUCAO e medido do mesmo jeito, pelo miolo: se a regua nao o achasse na foto, um vazamento zero nao provaria nada.
///
/// As camadas sao lidas da ARVORE (os nodes filhos do `CharacterVisual` ja carregam folha, animacao, quadro e a marca de `ordem`), sem API
/// nova no `CharacterVisual` -- como faz o `RoboDeColada`.
/// =======================================================================================================
///
///     Godot --path . --host --rede 8921 --campoteste 8 --horateste 0.5 --semfauna --diagcorpofrost --raca Human --conta bancada_corpofrost --nome Gelo
/// </summary>
public partial class RoboDoCorpoDoFrost : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>
	/// A base, a 1a e a 2a Evolucao. As evolucoes sao as do print do dono e da escada padrao do DM; a BASE e uma maior que a dele (o
	/// "Changeling Frieza 2"), porque o vazamento do print e todo colado no contorno do Full Power -- a regua, que guarda um pixel de
	/// distancia do desenho da evolucao, nao teria o que contar. A grande sobra 78 pixels contaveis em volta do Full Power e 43 em volta
	/// do Koola (conta de papel feita nas folhas, na pose parada).
	/// </summary>
	private static readonly string[] Corpos = ["Changeling 1 Large", "Changeling Full Power", "Koola Final Form"];

	/// <summary>BP de sobra pras duas portas (`FormasDeFrost.BpNecessario`: 250 milhoes e 15 bilhoes).</summary>
	private const double Bp = 2e10;

	/// <summary>
	/// O QUANTO A COR DA TELA PODE FUGIR DA DO QUADRO e ainda ser "o mesmo pixel", por canal (0 a 255): a luz do dia e o filtro da tela nao
	/// sao exatamente neutros.
	/// </summary>
	private const int Folga = 40;

	/// <summary>Um pixel do quadro e de COR VIVA quando o canal mais forte passa disto -- o preto do contorno fica de fora.</summary>
	private const int Viva = 80;

	/// <summary>A distancia (em pixels do quadro) que o pixel da base tem de guardar do desenho da evolucao pra contar como vazamento.</summary>
	private const int Margem = 1;

	/// <summary>
	/// QUANTO DO VAZAMENTO PODE "APARECER" POR COINCIDENCIA (um tufo de grama da cor do quadro), quanto TEM de aparecer no defeito, e quanto do
	/// desenho da evolucao a regua tem de achar pra valer.
	/// </summary>
	private const double ToleradoNoConserto = 0.10, ExigidoNoDefeito = 0.60, EvolucaoAchada = 0.75;

	/// <summary>O menor vazamento que deixa a cena dizer alguma coisa (ver <see cref="Corpos"/>).</summary>
	private const int VazamentoMinimo = 12;

	private int _eu;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	/// <summary>O defeito injetado nunca sobrevive ao robo, nem se ele cair no meio.</summary>
	public override void _ExitTree() => Catalogo.FrostPorCimaDaBaseDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Catalogo.FrostPorCimaDaBaseDeTeste = false;
		Fechar();
	}

	private static CharacterVisual? VisualDe(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	private static string FolhaDaForma(int id) => VisualDe(id)?.FolhaDoCorpoDaFormaDeTeste ?? "";

	/// <summary>
	/// O C (ou o X) pelo fio e a espera: o servidor na forma pedida e, quando se pede uma folha, a tela vestindo-a. A descida pro repouso
	/// nao pede folha: a camada do repouso e a mesma folha do corpo, e ela pode vir do cache sem caminho (ver `CharacterVisual.FolhaDoCorpoDeTeste`).
	/// </summary>
	private async System.Threading.Tasks.Task<bool> Subir(bool subir, string forma, string? folha)
	{
		C?.SendTransformar(subir);
		bool chegou = await Ate(() => S?.FormaDoCorpoDeTeste(_eu) == forma && S?.EmCenaDeTeste(_eu) == false
										  && (folha == null || FolhaDaForma(_eu) == folha), 10);
		await Segundos(1.5);   // o estalo da troca passa
		return chegou;
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = _eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dcorpofrost] ===== O CORPO DA FORMA DO FROST DEMON (dono, 2026-10-19) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		string folhaBase = FormasDeFrost.Caminho(Corpos[0]);
		string folha1 = FormasDeFrost.Caminho(Corpos[1]), folha2 = FormasDeFrost.Caminho(Corpos[2]);
		string base5 = Catalogo.IdDaBaseDoFrost;
		string evo1 = Catalogo.Todas.First(d => Catalogo.DegrauDoFrost(d) == FormasDeFrost.Evolucao).Id;
		string evo2 = Catalogo.Todas.First(d => Catalogo.DegrauDoFrost(d) == FormasDeFrost.Black).Id;

		// ---------------------------------------------------------- F0. a premissa
		bool virou = srv.VirarFrostDeTeste(eu, Corpos, Bp);
		bool vestiu = virou && await Ate(() => srv.FormaDoCorpoDeTeste(eu) == base5 && VisualDe(eu)?.FolhaDoCorpoDeTeste == folhaBase, 8);
		await Segundos(1);
		Image? foto0 = Fotografar("user://corpofrost-0-base.png");
		bool luz = vestiu && foto0 != null && MedirALuz(foto0, folhaBase);
		Conferir(vestiu && luz, $"F0. (premissa) o corpo do robo e um Frost Demon NORMAL na forma base, com o corpo \"{Corpos[0]}\" -- e a luz do "
								+ $"palco foi medida nele [forma {srv.FormaDoCorpoDeTeste(eu)}, corpo {Nome(VisualDe(eu)?.FolhaDoCorpoDeTeste)}, "
								+ $"luz {_luz.R:0.00}/{_luz.G:0.00}/{_luz.B:0.00}]");
		if (!vestiu || !luz) return;

		// ---------------------------------------------------------- F1. a 1a Evolucao
		bool subiu1 = await Subir(true, evo1, folha1);
		Leitura? l1 = Medir(folhaBase, folha1, "user://corpofrost-1-primeira.png");
		Conferir(subiu1 && Limpo(l1),
				 $"F1. A 1a EVOLUCAO TROCA O CORPO (o C, pelo fio): o \"{Corpos[1]}\" esta na tela e o \"{Corpos[0]}\" de baixo some -- nenhum "
				 + $"pixel dele aparece em volta da evolucao [{l1}]");

		// ---------------------------------------------------------- F2. o contra-exemplo: a regra de antes
		Catalogo.FrostPorCimaDaBaseDeTeste = true;
		try
		{
			bool desceu = await Subir(false, base5, null);
			bool subiuDeNovo = desceu && await Subir(true, evo1, folha1);
			Leitura? l2 = Medir(folhaBase, folha1, "user://corpofrost-2-defeito-por-cima.png");
			Conferir(subiuDeNovo && !Limpo(l2) && l2 is { BaseAcesa: true } d && d.FracaoQueAparece >= ExigidoNoDefeito,
					 "F2. CONTRA-EXEMPLO (defeito injetado: o corpo da forma do Frost desenhado POR CIMA do da base, sem esconde-lo): o corpo da base "
					 + $"volta a aparecer em volta da evolucao -- o print do dono -- e a regua de F1 REPROVA [{l2}]");
		}
		finally { Catalogo.FrostPorCimaDaBaseDeTeste = false; }

		// ---------------------------------------------------------- F3. a 2a Evolucao
		bool subiu2 = await Subir(true, evo2, folha2);
		Leitura? l3 = Medir(folhaBase, folha2, "user://corpofrost-3-segunda.png");
		Conferir(subiu2 && Limpo(l3),
				 $"F3. A 2a EVOLUCAO TAMBEM TROCA O CORPO: o \"{Corpos[2]}\" esta na tela e o \"{Corpos[0]}\" de baixo some [{l3}]");
	}

	/// <summary>A regua de F1 e F3: a base apagada, o vazamento existe no quadro, nada dele aparece na foto, e a evolucao aparece.</summary>
	private static bool Limpo(Leitura? l) =>
		l is { } d && !d.BaseAcesa && d.Vazamento >= VazamentoMinimo && d.FracaoQueAparece <= ToleradoNoConserto
		&& d.FracaoDaEvolucao >= EvolucaoAchada;

	// =====================================================================
	// A REGUA
	// =====================================================================
	/// <summary>O que a regua leu numa foto.</summary>
	/// <param name="Vazamento">Pixels de cor viva da base que caem fora do desenho da evolucao (a mais de <see cref="Margem"/> dele).</param>
	/// <param name="Aparece">Quantos deles a foto mostra com a cor do quadro da base.</param>
	/// <param name="Dentro">Pixels de cor viva do miolo do desenho da evolucao.</param>
	/// <param name="DentroAparece">Quantos deles a foto mostra com a cor do quadro da evolucao.</param>
	/// <param name="BaseAcesa">O node do corpo da base esta visivel.</param>
	private readonly record struct Leitura(int Vazamento, int Aparece, int Dentro, int DentroAparece, bool BaseAcesa, string Poses)
	{
		public double FracaoQueAparece => Vazamento == 0 ? 0 : (double)Aparece / Vazamento;
		public double FracaoDaEvolucao => Dentro == 0 ? 0 : (double)DentroAparece / Dentro;

		public override string ToString() =>
			$"base {(BaseAcesa ? "ACESA" : "apagada")}; vazamento possivel {Vazamento} px, aparecendo {Aparece} ({FracaoQueAparece:P0}); "
			+ $"evolucao achada {DentroAparece}/{Dentro} ({FracaoDaEvolucao:P0}); {Poses}";
	}

	/// <summary>
	/// A LUZ DO PALCO, por canal: o meio-dia do jogo nao e branco neutro -- o branco puro do quadro sai (220, 220, 210) na foto, e a primeira
	/// rodada reprovou o proprio desenho da evolucao por isso. A regua compara a tela com o quadro JA com esta luz.
	/// </summary>
	private Color _luz = new(1, 1, 1);

	/// <summary>A luz razoavel: escurece ou clareia um pouco, e nao apaga nem estoura um canal.</summary>
	private const float LuzMinima = 0.5f, LuzMaxima = 1.3f;

	/// <summary>
	/// Mede a <see cref="_luz"/> na foto do REPOUSO: por canal, a mediana de tela / quadro no miolo do corpo da base. No repouso a camada da
	/// forma e a mesma folha no mesmo lugar, entao o que esta na tela ali e esse desenho -- escondido ou nao o node de baixo.
	/// </summary>
	private bool MedirALuz(Image foto, string folhaBase)
	{
		if (VisualDe(_eu) is not { } v || CamadaDoCorpo(v, folhaBase) is not { } baixo || Quadro(baixo) is not { } qb) return false;
		var r = new List<float>();
		var g = new List<float>();
		var b = new List<float>();
		for (int y = 0; y < qb.GetHeight(); y++)
			for (int x = 0; x < qb.GetWidth(); x++)
			{
				Color cq = qb.GetPixel(x, y);
				if (cq.A < 0.5f || !NoMiolo(qb, x, y) || CorNaFoto(foto, NaTela(baixo, qb, x, y)) is not { } ct) continue;
				if (cq.R8 >= 100) r.Add(ct.R / cq.R);
				if (cq.G8 >= 100) g.Add(ct.G / cq.G);
				if (cq.B8 >= 100) b.Add(ct.B / cq.B);
			}
		if (r.Count < 10 || g.Count < 10 || b.Count < 10) { Nota($"poucos pixels pra medir a luz ({r.Count}/{g.Count}/{b.Count})"); return false; }
		_luz = new Color(Mediana(r), Mediana(g), Mediana(b));
		return _luz.R is >= LuzMinima and <= LuzMaxima && _luz.G is >= LuzMinima and <= LuzMaxima && _luz.B is >= LuzMinima and <= LuzMaxima;
	}

	private static float Mediana(List<float> v)
	{
		v.Sort();
		return v[v.Count / 2];
	}

	// AS DUAS CAMADAS, PELA ARVORE: o corpo e a camada de ordem 0, o corpo da forma a de ordem 1 (`CharacterVisual.NovaCamada`). O corpo pela
	// marca `src` que o `Trocar` escreve, como a `CharacterVisual.FolhaDoCorpoDeTeste`: o `ResourcePath` volta vazio pra folha que veio do
	// cache. A da forma pelo caminho, como a `FolhaDoCorpoDaFormaDeTeste` (as evolucoes chegam pelo `FolhasPresas`).
	private static AnimatedSprite2D? CamadaDoCorpo(CharacterVisual v, string folha) =>
		v.GetChildren().OfType<AnimatedSprite2D>()
		 .FirstOrDefault(s => s.SpriteFrames != null && s.GetMeta("ordem", -1).AsInt32() == 0 && s.GetMeta("src", "").AsString() == folha);

	private static AnimatedSprite2D? CamadaDaForma(CharacterVisual v, string folha) =>
		v.GetChildren().OfType<AnimatedSprite2D>()
		 .FirstOrDefault(s => s.SpriteFrames is { } sf && s.GetMeta("ordem", -1).AsInt32() == 1 && sf.ResourcePath == folha);

	/// <summary>Fotografa e le a foto -- a mesma imagem que vai pro disco e a que a regua mede.</summary>
	private Leitura? Medir(string folhaBase, string folhaDaForma, string destino)
	{
		Image? foto = Fotografar(destino);
		if (foto == null || VisualDe(_eu) is not { } v) return null;
		AnimatedSprite2D? baixo = CamadaDoCorpo(v, folhaBase), forma = CamadaDaForma(v, folhaDaForma);
		if (baixo == null || forma == null || Quadro(baixo) is not { } qb || Quadro(forma) is not { } qf)
		{
			Nota($"a regua nao achou as duas camadas (base {baixo != null}, forma {forma != null})");
			return null;
		}

		int vazamento = 0, aparece = 0, dentro = 0, dentroAparece = 0;
		for (int y = 0; y < qb.GetHeight(); y++)
			for (int x = 0; x < qb.GetWidth(); x++)
			{
				Color cb = qb.GetPixel(x, y);
				if (cb.A < 0.5f || !DeCorViva(cb)) continue;
				Vector2 tela = NaTela(baixo, qb, x, y);
				Vector2I emCima = NoQuadro(forma, qf, tela);
				if (!LongeDoDesenho(qf, emCima)) continue;
				vazamento++;
				if (CorNaFoto(foto, tela) is { } c && Parecido(c, cb)) aparece++;
			}
		for (int y = 0; y < qf.GetHeight(); y++)
			for (int x = 0; x < qf.GetWidth(); x++)
			{
				Color cf = qf.GetPixel(x, y);
				if (cf.A < 0.5f || !DeCorViva(cf) || !NoMiolo(qf, x, y)) continue;
				dentro++;
				if (CorNaFoto(foto, NaTela(forma, qf, x, y)) is { } c && Parecido(c, cf)) dentroAparece++;
			}
		return new Leitura(vazamento, aparece, dentro, dentroAparece, baixo.Visible,
						   $"poses {baixo.Animation}#{baixo.Frame} / {forma.Animation}#{forma.Frame}");
	}

	/// <summary>O quadro que a camada esta desenhando agora, em RGBA8.</summary>
	private static Image? Quadro(AnimatedSprite2D s)
	{
		if (s.SpriteFrames is not { } f || !f.HasAnimation(s.Animation) || f.GetFrameCount(s.Animation) == 0) return null;
		int i = Math.Clamp(s.Frame, 0, f.GetFrameCount(s.Animation) - 1);
		if (f.GetFrameTexture(s.Animation, i)?.GetImage() is not { } img || img.IsEmpty()) return null;
		var copia = (Image)img.Duplicate();
		copia.Convert(Image.Format.Rgba8);
		return copia;
	}

	/// <summary>O centro do pixel (x, y) do quadro desta camada, na foto. Toda camada e `Centered` (`CharacterVisual.NovaCamada`).</summary>
	private static Vector2 NaTela(AnimatedSprite2D s, Image quadro, int x, int y)
	{
		float w = quadro.GetWidth(), h = quadro.GetHeight();
		float lx = s.FlipH ? w - (x + 0.5f) : x + 0.5f;
		return s.GetGlobalTransformWithCanvas() * (new Vector2(lx - w / 2f, y + 0.5f - h / 2f) + s.Offset);
	}

	/// <summary>O pixel do quadro desta camada que cai neste ponto da foto -- pode cair fora do quadro.</summary>
	private static Vector2I NoQuadro(AnimatedSprite2D s, Image quadro, Vector2 tela)
	{
		float w = quadro.GetWidth(), h = quadro.GetHeight();
		Vector2 local = s.GetGlobalTransformWithCanvas().AffineInverse() * tela - s.Offset;
		float lx = local.X + w / 2f;
		if (s.FlipH) lx = w - lx;
		return new Vector2I(Mathf.FloorToInt(lx), Mathf.FloorToInt(local.Y + h / 2f));
	}

	/// <summary>Nenhum pixel opaco do quadro a menos de <see cref="Margem"/> deste ponto (que pode estar fora do quadro).</summary>
	private static bool LongeDoDesenho(Image quadro, Vector2I p)
	{
		for (int dy = -Margem; dy <= Margem; dy++)
			for (int dx = -Margem; dx <= Margem; dx++)
			{
				int x = p.X + dx, y = p.Y + dy;
				if (x >= 0 && y >= 0 && x < quadro.GetWidth() && y < quadro.GetHeight() && quadro.GetPixel(x, y).A >= 0.5f) return false;
			}
		return true;
	}

	/// <summary>O pixel e opaco e todos os vizinhos a menos de <see cref="Margem"/> tambem -- longe do contorno que a forma pinta na borda.</summary>
	private static bool NoMiolo(Image quadro, int x, int y)
	{
		for (int dy = -Margem; dy <= Margem; dy++)
			for (int dx = -Margem; dx <= Margem; dx++)
			{
				int vx = x + dx, vy = y + dy;
				if (vx < 0 || vy < 0 || vx >= quadro.GetWidth() || vy >= quadro.GetHeight() || quadro.GetPixel(vx, vy).A < 0.5f) return false;
			}
		return true;
	}

	private static bool DeCorViva(Color c) => Math.Max(c.R8, Math.Max(c.G8, c.B8)) >= Viva;

	private static Color? CorNaFoto(Image foto, Vector2 tela)
	{
		int x = Mathf.FloorToInt(tela.X), y = Mathf.FloorToInt(tela.Y);
		return x >= 0 && y >= 0 && x < foto.GetWidth() && y < foto.GetHeight() ? foto.GetPixel(x, y) : null;
	}

	/// <summary>A cor da tela e a do quadro sob a <see cref="_luz"/> do palco, por canal, dentro da <see cref="Folga"/>.</summary>
	private bool Parecido(Color tela, Color quadro) =>
		Math.Abs(tela.R8 - quadro.R8 * _luz.R) <= Folga && Math.Abs(tela.G8 - quadro.G8 * _luz.G) <= Folga
		&& Math.Abs(tela.B8 - quadro.B8 * _luz.B) <= Folga;

	private static string Nome(string? caminho) => caminho is { Length: > 0 } c ? c.GetFile().GetBaseName() : "-";

	// =====================================================================
	// O DE SEMPRE DOS ROBOS
	// =====================================================================
	private Image? Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza)"); return null; }
		img.Convert(Image.Format.Rgba8);
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		Nota("foto salva em " + caminho);
		return img;
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
		foreach (string l in _linhas) GD.Print("[dcorpofrost] " + l);
		GD.Print(_falhas.Count == 0 ? "[dcorpofrost] ===== TUDO OK =====" : $"[dcorpofrost] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dcorpofrost]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
