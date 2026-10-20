using Godot;
using Jandirus.Core.Forms;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA PUPILA DO GRADE 3 (`--diagpupilagrade3`, 2026-10-19). O dono: *"super saiyajin grade 3 ficar com as pupilas brancas (igual no
/// lssj descontrolado)"*. A regra mora no Core (`Catalogo.CorDoOlho`: o `fcfdfd` da esclerotica que a furia lendaria ja usava) e a bancada
/// pura `formas` a confere; este robo responde o que numero nenhum responde -- o que a TELA mostra. Com janela e `--host`:
///   P0. a premissa: o ceu limpo e de dia ja na tela, e a esclerotica branca do rosto (forma base) aparecendo na caixa dos olhos --
///       foto `pupila-0-base.png`;
///   P1. o Grade 2, o padrao: mesmo cabelo `USSj` e mesmo corpo musculoso do Grade 3, e a pupila VERDE da escada na tela -- foto
///       `pupila-1-grade2-verde.png`;
///   P2. o Grade 3: a pupila BRANCA sem iris -- nenhum pixel verde onde a camada de olhos desenha, e mais branco que no Grade 2 (a iris
///       virou esclerotica; nao sumiu) -- foto `pupila-2-grade3-branca.png`;
///   P3. CONTRA-EXEMPLO (`Catalogo.Grade3DeIrisVerdeDeTeste`, o jogo de antes): o mesmo Grade 3 de pupila verde, e a regua de P2 REPROVA --
///       foto `pupila-3-defeito-verde.png`.
/// Cada foto tem a cabeca ampliada ao lado (`-cabeca.png`), e as quatro cabecas saem juntas em `pupila-CONTATO.png`.
///
/// ============================ A REGUA E DE PIXEL, SO ONDE O OLHO DESENHA ============================
/// A caixa e a da camada de olhos (`CharacterVisual.CaixaDosOlhosDeTeste`, a mesma do `RoboDeOlharDoBio`), com um pixel de mundo de folga
/// pra pegar a esclerotica encostada na iris: la dentro so ha iris, esclerotica, cilio e pele -- o cabelo dourado, a pele e a aura nao
/// passam por verde, e a grama e os transeuntes ficam de fora. VERDE e o canal verde passando os outros dois com folga (o `40a060` da
/// escada sai ~(55,138,79) na luz do meio-dia); BRANCO sao os tres canais altos e juntos (o branco puro sai ~(220,220,210) -- ver o
/// `RoboDoCorpoDoFrost`). As formas sao vestidas no cliente pelo caminho de producao sem cena (`World.AoMudarForma` com
/// `DegrauDeCena.Nenhuma`, como o `RoboDeOlhada`), com a aura apagada antes da foto.
/// ==================================================================================================
///
///     Godot --path . --host --rede 9901 --campoteste 8 --horateste 0.5 --semfauna --diagpupilagrade3 --raca Saiyan --conta bancada_pupilagradetres --nome Pupila
/// </summary>
public partial class RoboDaPupilaDoGrade3 : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<Image> _cabecas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>A folga da caixa dos olhos, em pixels de mundo: o bastante pra esclerotica encostada na iris entrar.</summary>
	private const float FolgaDaCaixa = 1f;

	/// <summary>VERDE: o canal verde passa os outros dois por isto (0 a 1).</summary>
	private const float VerdeSobra = 0.12f;

	/// <summary>BRANCO: os tres canais acima de <see cref="BrancoMinimo"/>, e o maior a menos de <see cref="BrancoJunto"/> do menor.</summary>
	private const float BrancoMinimo = 0.65f, BrancoJunto = 0.12f;

	/// <summary>O lado do recorte da cabeca, em pixels de mundo, e a ampliacao (vizinho mais proximo) da foto dela.</summary>
	private const int LadoDaCabeca = 20, Ampliacao = 8;

	private int _eu;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	/// <summary>O defeito injetado nunca sobrevive ao robo, nem se ele cair no meio.</summary>
	public override void _ExitTree() => Catalogo.Grade3DeIrisVerdeDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Catalogo.Grade3DeIrisVerdeDeTeste = false;
		Fechar();
	}

	private CharacterVisual? Visual() =>
		World.Instancia?.CorpoDeTeste(_eu != 0 ? _eu : C?.LocalId ?? 0)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>O que a regua leu numa foto: os pixels da caixa dos olhos e o uniform que a camada recebeu.</summary>
	private readonly record struct Leitura(int Verdes, int Brancos, int Pixels, Vector3? Tinta)
	{
		public bool TintaBranca => Tinta is { } t && t.X > 0.8f && t.Y > 0.8f && t.Z > 0.8f;

		public override string ToString() =>
			$"{Verdes} px verdes e {Brancos} brancos em {Pixels} da caixa dos olhos; uniform do olho "
			+ (Tinta is { } t ? $"({t.X:0.##},{t.Y:0.##},{t.Z:0.##})" : "nenhum");
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && S != null && Visual() != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o corpo esta na tela e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		_eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dpupila] ===== A PUPILA DO SUPER SAIYAJIN GRADE 3 (dono, 2026-10-19) =====");
		// MEIO-DIA DA ZONA DO ROBO, e nao da Terra: o `--horateste 0.5` crava a hora da TERRA, e o campo de teste anda no relogio dele --
		// a segunda rodada caiu de NOITE em dois disparos seguidos (forca da noite 1,00) e de dia no outro. O `CravarMeioDiaDaVariedade`
		// resolve o meio-dia pelo relogio da zona (a conta do `admin_meio_dia`) e limpa o ceu.
		srv.CravarMeioDiaDaVariedade(_eu);

		// ---------------------------------------------------------- P0. a luz da foto
		// O CEU LIMPO TEM DE CHEGAR NA TELA, e de dia: o clima sorteado no login escurece a cena e o veu dele demora a sair. A primeira
		// rodada fotografou logo depois de pedir o ceu limpo e, em 4 de 6 disparos, mediu 0 verde e 0 branco nas tres fotos -- a
		// esclerotica `fcfdfd` saia cinza. O molde e o `RoboDaEsferaDemoniaca`: dois segundos seguidos sem clima nenhum na tela.
		Iluminacao? ceu = World.Instancia?.GetNodeOrNull<Iluminacao>("Iluminacao");
		double desde = -1;
		bool claro = await Ate(() =>
		{
			double t = Time.GetTicksMsec() / 1000.0;
			if (ceu is not { TempoQueFaz.Ativo: false } || Iluminacao.ForcaDaNoite() > 0.05f) desde = -1;
			else if (desde < 0) desde = t;
			return desde >= 0 && t - desde >= 2;
		}, 30);
		// E O ROSTO CLAREOU: a esclerotica tem de passar por branco na caixa antes da primeira forma (mais 10 s no maximo, sem foto).
		await Ate(() => Medir(null) is { Brancos: > 0 }, 10);
		Leitura? rosto = Medir("0-base");
		Conferir(claro && rosto is { Brancos: > 0 },
				 "P0. (premissa) o ceu esta LIMPO e de DIA na tela ha 2 s, e a esclerotica branca do rosto aparece na caixa dos olhos -- a luz "
				 + $"da foto deixa julgar cor [ceu {(ceu is { TempoQueFaz.Ativo: false } ? "limpo" : "com clima")}, noite "
				 + $"{Iluminacao.ForcaDaNoite():0.00}; forma base: {rosto}]");
		if (!claro || rosto is not { Brancos: > 0 }) return;

		// ---------------------------------------------------------- P1. o padrao: o Grade 2
		Leitura? g2 = await VestirEMedir("grade2", "1-grade2-verde");
		Conferir(g2 is { Verdes: > 0, TintaBranca: false },
				 $"P1. (padrao) no Grade 2 a pupila VERDE da escada esta na tela -- a regua enxerga a iris [{g2}]");

		// ---------------------------------------------------------- P2. o Grade 3
		Leitura? g3 = await VestirEMedir(Catalogo.IdGrade3, "2-grade3-branca");
		Conferir(SemIris(g3, g2),
				 "P2. NO GRADE 3 A PUPILA E BRANCA, SEM IRIS: nenhum pixel verde onde o olho desenha, mais branco que no Grade 2 (a iris "
				 + $"virou esclerotica), e o uniform branco [{g3}]");

		// ---------------------------------------------------------- P3. o contra-exemplo: o jogo de antes
		Catalogo.Grade3DeIrisVerdeDeTeste = true;
		try
		{
			Leitura? d = await VestirEMedir(Catalogo.IdGrade3, "3-defeito-verde");
			Conferir(!SemIris(d, g2) && d is { Verdes: > 0 },
					 "P3. CONTRA-EXEMPLO (defeito injetado: o Grade 3 no verde da escada, o jogo de antes): a pupila volta a ser verde na tela "
					 + $"e a regua de P2 REPROVA [{d}]");
		}
		finally { Catalogo.Grade3DeIrisVerdeDeTeste = false; }

		Contato();
	}

	/// <summary>A regua de P2: zero verde na caixa, mais branco que no padrao, e o uniform da camada branco.</summary>
	private static bool SemIris(Leitura? l, Leitura? padrao) =>
		l is { } g && padrao is { } p && g.Verdes == 0 && g.Brancos > p.Brancos && g.TintaBranca;

	/// <summary>
	/// Veste a forma no cliente sem cena e mede. PASSA PELA BASE antes: sem isto duas poses seguidas da mesma forma (P2 e P3) chegariam
	/// como `de == para` -- ver o `RoboDeOlhada.Vestir`.
	/// </summary>
	private async System.Threading.Tasks.Task<Leitura?> VestirEMedir(string forma, string rotulo)
	{
		if (World.Instancia is not { } mundo) return null;
		int b = Catalogo.Rede(Catalogo.IdBase);
		mundo.AoMudarForma(_eu, b, b, DegrauDeCena.Nenhuma);
		mundo.AoMudarForma(_eu, b, Catalogo.Rede(forma), DegrauDeCena.Nenhuma);
		await Segundos(0.6);
		SemAura();
		await Quadros(3);   // a foto e o quadro JA desenhado: a aura apagada tem de chegar nele
		return Medir(rotulo);
	}

	/// <summary>Apaga a aura, os raios e o contorno da forma -- o mesmo do `RoboDeOlhada.SemAura`: a luz da forma nao entra na regua.</summary>
	private void SemAura()
	{
		if (World.Instancia?.CorpoDeTeste(_eu) is not { } corpo) return;
		corpo.GetNodeOrNull<Aura>("Aura")?.Apagar();
		corpo.GetNodeOrNull<RaiosDaForma>("Raios")?.Definir(false, Colors.White, 0);
		Visual()?.AuraDaForma(Colors.White, 0f, null);
	}

	// =====================================================================
	// A REGUA
	// =====================================================================
	/// <summary>
	/// Fotografa e conta, na caixa dos olhos levada pra foto, os pixels verdes e os brancos. Guarda a cabeca ampliada. Rotulo nulo = so
	/// a conta, sem gravar nada (a espera da luz no P0).
	/// </summary>
	private Leitura? Medir(string? rotulo)
	{
		Image? foto = rotulo == null ? Quadro() : Fotografar($"user://pupila-{rotulo}.png");
		CharacterVisual? vis = Visual();
		if (foto == null || vis?.CaixaDosOlhosDeTeste is not { } caixa)
		{
			if (rotulo != null) Nota($"[{rotulo}] sem medida (foto {foto != null}, caixa dos olhos {vis?.CaixaDosOlhosDeTeste != null})");
			return null;
		}

		// O PIXEL DA FOTO CONTA QUANDO O CENTRO DELE CAI NA CAIXA: a camada e desenhada sem filtro, entao cada pixel da tela mostra um pixel
		// de mundo inteiro, e um pixel de borda que so encosta na caixa e do vizinho de fora (o cabelo, a pele).
		Transform2D t = vis.GetGlobalTransformWithCanvas();
		Rect2 local = caixa.Grow(FolgaDaCaixa);
		Vector2 a = t * local.Position, z = t * local.End;
		Rect2 naTela = new Rect2(a, Vector2.Zero).Expand(z);
		int verdes = 0, brancos = 0, pixels = 0;
		for (int y = Math.Max(0, (int)naTela.Position.Y); y < Math.Min(foto.GetHeight(), (int)Math.Ceiling(naTela.End.Y)); y++)
			for (int x = Math.Max(0, (int)naTela.Position.X); x < Math.Min(foto.GetWidth(), (int)Math.Ceiling(naTela.End.X)); x++)
			{
				if (!naTela.HasPoint(new Vector2(x + 0.5f, y + 0.5f))) continue;
				Color c = foto.GetPixel(x, y);
				pixels++;
				if (Verde(c)) verdes++;
				else if (Branco(c)) brancos++;
			}

		if (rotulo != null) Cabeca(foto, t * local.GetCenter(), t.X.Length(), rotulo);
		return new Leitura(verdes, brancos, pixels, vis.TintaDoOlhoDeTeste);
	}

	private static bool Verde(Color c) => c.G - Math.Max(c.R, c.B) > VerdeSobra;

	private static bool Branco(Color c) =>
		Math.Min(c.R, Math.Min(c.G, c.B)) > BrancoMinimo && Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) < BrancoJunto;

	/// <summary>A cabeca recortada em volta dos olhos e ampliada sem interpolacao -- interpolar inventaria cor entre a iris e a pele.</summary>
	private void Cabeca(Image foto, Vector2 centro, float escala, string rotulo)
	{
		int lado = Math.Min((int)(LadoDaCabeca * escala), Math.Min(foto.GetWidth(), foto.GetHeight()));
		int x = Math.Clamp((int)centro.X - lado / 2, 0, foto.GetWidth() - lado);
		int y = Math.Clamp((int)centro.Y - lado / 2, 0, foto.GetHeight() - lado);
		Image cabeca = foto.GetRegion(new Rect2I(x, y, lado, lado));
		cabeca.Resize(lado * Ampliacao, lado * Ampliacao, Image.Interpolation.Nearest);
		string caminho = ProjectSettings.GlobalizePath($"user://pupila-{rotulo}-cabeca.png");
		cabeca.SavePng(caminho);
		_cabecas.Add(cabeca);
	}

	/// <summary>As cabecas lado a lado, na ordem do roteiro: a base, o verde do padrao, o branco do Grade 3 e o verde do defeito.</summary>
	private void Contato()
	{
		if (_cabecas.Count == 0) return;
		int lado = _cabecas.Min(c => Math.Min(c.GetWidth(), c.GetHeight()));
		Image folha = Image.CreateEmpty(lado * _cabecas.Count, lado, false, Image.Format.Rgba8);
		for (int i = 0; i < _cabecas.Count; i++)
			folha.BlitRect(_cabecas[i], new Rect2I(0, 0, lado, lado), new Vector2I(i * lado, 0));
		string caminho = ProjectSettings.GlobalizePath("user://pupila-CONTATO.png");
		folha.SavePng(caminho);
		Nota("as cabecas lado a lado em " + caminho);
	}

	// =====================================================================
	// O DE SEMPRE DOS ROBOS
	// =====================================================================
	/// <summary>O quadro ja desenhado, em RGBA8, sem gravar -- nulo no headless (que nao renderiza).</summary>
	private Image? Quadro()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	private Image? Fotografar(string destino)
	{
		if (Quadro() is not { } img) { Nota("sem foto (headless nao renderiza)"); return null; }
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
		foreach (string l in _linhas) GD.Print("[dpupila] " + l);
		GD.Print(_falhas.Count == 0 ? "[dpupila] ===== TUDO OK =====" : $"[dpupila] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dpupila]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
