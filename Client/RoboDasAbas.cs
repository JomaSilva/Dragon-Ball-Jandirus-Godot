using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DAS OUTRAS ABAS DO MENU P (`--diagabas`) ============================
/// O PEDIDO DO DONO, literal: *"agora vc vai melhorar a aba de stats, other etc do menu do botao P assim
/// como vc melhorou a aba learn com as skills. ta mt cru o resto, da uma boa melhorada pra deixar mais
/// profissional"*. "Cru" e "profissional" nao se medem; o que se mede e o que a `--diagskills` ja mede
/// na aba Learning e que as outras abas NAO tinham: se cada aba diz o que promete, com a mesma lingua
/// visual (cartoes, barras, faixas, pilulas do tema), se as duas telas (HUD e menu) continuam
/// concordando, e se a foto de cada aba existe pra alguem olhar.
///
/// ============================ ELA NASCE DENTRO DO MUNDO E APERTA O QUE O DEDO APERTARIA ============================
/// Como a `--diagskills`: entra como jogador, aperta P, clica em cada aba pelo BOTAO dela (o mesmo
/// caminho do dedo), fotografa, e le a arvore de cena por TEXTO. Dois processos, pelo mesmo motivo
/// daquela (ver `testar-as-abas.bat`).
///
/// A PRIMEIRA RODADA E A DO "ANTES": fotografa cada aba como ela esta hoje, pra tira antes x depois
/// (F-final), e o placar mede so o que nao pode piorar. As familias de "depois" vem com cada aba
/// redesenhada.
/// =========================================================================================================
///
/// COMO RODAR -- pelo `testar-as-abas.bat` (DOIS PROCESSOS, janela no SEGUNDO monitor):
///     Godot --headless --path . --server --port 7983 --marcosteste 40 --horateste 0.5     (o servidor)
///     Godot --path . --connect 127.0.0.1 --rede 7983 --diagabas --semfoco                 (este cliente)
///           --raca Saiyan --conta bancada_abas --nome Bancada
///           --pasta &lt;dir&gt; [--antes &lt;dir com as fotos de antes&gt;] --position 1920,0 --resolution 1280x720
///
/// **PRECISA DE JANELA.** Sem foto as linhas de pixel sao PULADAS e entram no terceiro placar.
/// </summary>
public partial class RoboDasAbas : Node
{
	// =====================================================================
	// PLACAR -- tres, como manda a casa: provas, injecoes, e o que NAO foi olhado
	// =====================================================================
	private int _ok, _falha, _pulados, _injOk, _injFalha, _intrusoes;
	private readonly List<string> _reprovadas = [], _naoMedidos = [], _injPassouBatido = [];
	private readonly List<string> _fotosGravadas = [];

	private static void Nota(string linha) => GD.Print("[abas] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void ChecaNoPixel(string oque, bool temFoto, bool passou, string detalhe = "")
	{
		if (!temFoto)
		{
			Nota("  PULADA  " + oque + "   [sem janela: nao ha foto pra medir]");
			_pulados++;
			_naoMedidos.Add(oque);
			return;
		}
		Checa(oque, passou, detalhe);
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou " : "  PASSOU") + "  (injecao) " + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _injPassouBatido.Add(oque); }
	}

	// =====================================================================
	// ONDE GRAVAR, E CONTRA O QUE COMPARAR
	// =====================================================================
	private string _pasta = "", _antes = "";
	private readonly Dictionary<string, Image> _fotos = [];

	private static string Arg(string chave)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, chave);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	private static string Hex(Color c) =>
		$"#{(int)(c.R * 255 + 0.5f):x2}{(int)(c.G * 255 + 0.5f):x2}{(int)(c.B * 255 + 0.5f):x2}";

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;

	public override void _Ready() => _ = Rodar();

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async System.Threading.Tasks.Task Rodar()
	{
		_pasta = Arg("--pasta");
		_antes = Arg("--antes");
		if (_pasta.Length == 0) _pasta = ProjectSettings.GlobalizePath("user://");
		if (!_pasta.EndsWith('/') && !_pasta.EndsWith('\\')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);

		Nota("==================================================================================");
		Nota(" AS OUTRAS ABAS DO MENU P -- Stats, Equip, Body, Forms, Ki, People, World, Cargos, Other, Tech, Admin");
		Nota("==================================================================================");
		Nota($"  fotos em           : {_pasta}");
		Nota($"  fotos de ANTES em  : {(_antes.Length == 0 ? "(nenhuma -- sem tira antes x depois)" : _antes)}");
		Nota($"  janela             : {DisplayServer.WindowGetSize()} em {DisplayServer.WindowGetPosition()}");

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 }
										&& c.SkillsArvores.Count > 0 && M != null, 90);
		Checa("o mundo chegou (conexao, raca na ficha lenta, estado das arvores no pacote de skills)", pronto,
			  $"raca={C?.Atributos.Raca} arvores={C?.SkillsArvores.Count} marcos={C?.MarcosLivres}");
		if (!pronto || M is not { } menu || C is not { } cli) { Placar(); GetTree().Quit(2); return; }
		await Segundos(1.0);

		await F0_AbreOMenu(menu);
		await F1_CadaAbaFotografada(menu, cli);
		await F2_Stats(menu, cli);
		await F3_Equip(menu, cli);
		await F4_Corpo(menu, cli);
		await F5_Ki(menu, cli);
		await F6_Sentidos(menu, cli);
		await F7_Formas(menu, cli);
		await F8_Cargos(menu, cli);
		await F10_Gente(menu, cli);
		await F11_Mundo(menu, cli);
		await F12_Nav(menu, cli);
		await F13_Outros(menu, cli);
		await F14_Tech(menu, cli);
		await F15_Skills(menu, cli);
		await F16_Admin(menu, cli);
		F99_ATiraAntesXDepois();

		menu.Fechar();
		Placar();
		GetTree().Quit(_falha == 0 && _injFalha == 0 ? 0 : 1);
	}

	// =====================================================================
	// F0 -- O MENU ABRE COM P
	// =====================================================================
	private async System.Threading.Tasks.Task F0_AbreOMenu(MenuJogo menu)
	{
		Nota("--- F0: P abre o menu ---");
		var p = new InputEventKey { Keycode = Key.P, PhysicalKeycode = Key.P, Unicode = 'p', Pressed = true };
		GetViewport().PushInput(p);
		await Quadros(3);
		Checa("apertar P abre o menu", menu.Visible && MenuJogo.Aberto);
		if (!menu.Visible) menu.Abrir();
	}

	// =====================================================================
	// F1 -- CADA ABA, PELO BOTAO DELA, FOTOGRAFADA
	// =====================================================================
	/// <summary>
	/// A ORDEM E A DA BARRA (`AbasDeTeste`), que e a ordem do original. Cada aba e aberta pelo BOTAO
	/// (o caminho do dedo), e o que se registra por aba e o que a tela tem: quantos rotulos, quantos
	/// botoes, quantos cartoes -- e a foto. Uma aba que nao abre, ou que abre vazia, e vermelha aqui.
	/// </summary>
	private async System.Threading.Tasks.Task F1_CadaAbaFotografada(MenuJogo menu, GameClient cli)
	{
		Nota("--- F1: cada aba, pelo botao dela ---");
		int i = 0;
		foreach (string aba in menu.AbasDeTeste)
		{
			i++;
			Button? b = Botao(menu, aba);
			Checa($"a aba '{aba}' tem botao na barra", b != null);
			if (b == null) continue;
			await Clicar(b);
			await Quadros(4);
			Checa($"...e clicar nele abre '{aba}'", menu.AbaDeTeste == aba, menu.AbaDeTeste);

			Control? pg = menu.PaginaDeTeste(aba);
			int rotulos = pg == null ? 0 : Rotulos(pg).Count();
			int botoes = pg == null ? 0 : Todos(pg).OfType<Button>().Count(x => x.IsVisibleInTree());
			int cartoes = pg == null ? 0 : Todos(pg).OfType<PanelContainer>().Count(x => x.IsVisibleInTree() && x.HasMeta("cartao"));
			Nota($"    {aba,-9} rotulos {rotulos,3}  botoes {botoes,3}  cartoes {cartoes,3}");
			Checa($"...e a pagina de '{aba}' tem alguma coisa escrita", rotulos > 0 || botoes > 0, $"{rotulos} rotulos, {botoes} botoes");

			if (Ancestral<ScrollContainer>(pg!) is { } rol) rol.ScrollVertical = 0;
			await Quadros(2);
			Image? foto = await Foto();
			await Guardar($"aba-{i:00}-{aba.ToLowerInvariant()}", foto);
		}
	}

	// =====================================================================
	// F9 -- A TIRA ANTES x DEPOIS
	// =====================================================================
	private void F99_ATiraAntesXDepois()
	{
		Nota("--- F99: a tira antes x depois ---");
		if (_antes.Length == 0)
		{
			Nota("  PULADA  tira antes x depois   [sem --antes]");
			_pulados++;
			_naoMedidos.Add("tira antes x depois");
			return;
		}
		string dir = _antes.EndsWith('/') || _antes.EndsWith('\\') ? _antes : _antes + "/";
		int pares = 0;
		foreach ((string nome, Image depois) in _fotos)
		{
			string caminho = dir + nome + ".png";
			if (!Godot.FileAccess.FileExists(caminho)) continue;
			Image antes = Image.LoadFromFile(caminho);
			if (antes == null || antes.IsEmpty()) continue;
			int w = antes.GetWidth() + depois.GetWidth() + 8, h = Math.Max(antes.GetHeight(), depois.GetHeight());
			var tira = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
			tira.Fill(new Color(0, 0, 0));
			antes.Convert(Image.Format.Rgba8);
			var d = (Image)depois.Duplicate();
			d.Convert(Image.Format.Rgba8);
			tira.BlitRect(antes, new Rect2I(0, 0, antes.GetWidth(), antes.GetHeight()), Vector2I.Zero);
			tira.BlitRect(d, new Rect2I(0, 0, d.GetWidth(), d.GetHeight()), new Vector2I(antes.GetWidth() + 8, 0));
			string saida = _pasta + "tira-" + nome + ".png";
			tira.SavePng(saida);
			_fotosGravadas.Add(saida);
			pares++;
		}
		Checa("a tira antes x depois saiu pra pelo menos uma aba", pares > 0, $"{pares} pares");
	}

	// =====================================================================
	// LER A TELA
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private static T? Ancestral<T>(Node n) where T : Node
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is T t) return t;
		return null;
	}

	/// <summary>Um botao pelo TEXTO, exato primeiro e prefixo depois. So conta se estiver visivel.</summary>
	private static Button? Botao(Node raiz, string texto, bool soVisivel = true)
	{
		List<Button> vistos = Todos(raiz).OfType<Button>().Where(b => !soVisivel || b.IsVisibleInTree()).ToList();
		return vistos.FirstOrDefault(b => string.Equals(b.Text, texto, StringComparison.OrdinalIgnoreCase))
			?? vistos.FirstOrDefault(b => b.Text.StartsWith(texto, StringComparison.OrdinalIgnoreCase));
	}

	private static IEnumerable<Label> Rotulos(Node raiz) => Todos(raiz).OfType<Label>().Where(l => l.IsVisibleInTree());

	private static Label? Rotulo(Node raiz, string texto) => Rotulos(raiz).FirstOrDefault(l => l.Text == texto);

	// =====================================================================
	// O CLIQUE, A ESPERA, A FOTO
	// =====================================================================
	private async System.Threading.Tasks.Task Clicar(Button b)
	{
		b.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(3);
	}

	private async System.Threading.Tasks.Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async System.Threading.Tasks.Task Segundos(double s)
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

	private async System.Threading.Tasks.Task<Image?> Foto()
	{
		// A PAUSA ABERTA E TECLA DE FORA, E ELA CEGA A FOTO -- ver a mesma nota na `--diagskills`.
		if (PauseMenu.Instancia is { Aberto: true } pausa)
		{
			_intrusoes++;
			Nota("  AVISO  o menu de PAUSA estava aberto (tecla de fora chegou na janela) -- fechando pra fotografar");
			pausa.Fechar("bancada fechou a pausa aberta por tecla de fora");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	private async System.Threading.Tasks.Task Guardar(string nome, Image? foto)
	{
		if (foto == null) { _pulados++; _naoMedidos.Add($"foto {nome}"); Nota($"  --     sem foto pra {nome} (headless?)"); return; }
		string caminho = _pasta + nome + ".png";
		foto.SavePng(caminho);
		_fotos[nome] = foto;
		_fotosGravadas.Add(caminho);
		Nota("  foto   " + caminho);
		await System.Threading.Tasks.Task.CompletedTask;
	}

	private static Rect2I Caixa(Rect2 r, int folga) => new(
		(int)r.Position.X + folga, (int)r.Position.Y + folga,
		Math.Max(1, (int)r.Size.X - folga * 2), Math.Max(1, (int)r.Size.Y - folga * 2));

	/// <summary>A cor mais frequente de um retangulo da foto, e que fracao dele ela ocupa.</summary>
	private static (Color, float) Moda(Image? img, Rect2I r)
	{
		if (img == null) return (new Color(0, 0, 0), 0);
		var conta = new Dictionary<uint, int>();
		int total = 0;
		int x1 = Math.Min(img.GetWidth(), r.Position.X + r.Size.X);
		int y1 = Math.Min(img.GetHeight(), r.Position.Y + r.Size.Y);
		for (int y = Math.Max(0, r.Position.Y); y < y1; y++)
			for (int x = Math.Max(0, r.Position.X); x < x1; x++)
			{
				Color c = img.GetPixel(x, y);
				uint k = ((uint)(c.R * 255) << 16) | ((uint)(c.G * 255) << 8) | (uint)(c.B * 255);
				conta[k] = conta.GetValueOrDefault(k) + 1;
				total++;
			}
		if (total == 0) return (new Color(0, 0, 0), 0);
		(uint chave, int vezes) = conta.MaxBy(kv => kv.Value);
		return (new Color(((chave >> 16) & 255) / 255f, ((chave >> 8) & 255) / 255f, (chave & 255) / 255f), vezes / (float)total);
	}

	private static IEnumerable<Color> Paleta() =>
		[Tema.Fundo, Tema.Painel, Tema.PainelClaro, Tema.PainelAceso, Tema.PainelApagado, new Color(0.06f, 0.07f, 0.10f)];

	private static bool Perto(Color a, Color b) =>
		Math.Abs(a.R - b.R) <= 0.035f && Math.Abs(a.G - b.G) <= 0.035f && Math.Abs(a.B - b.B) <= 0.035f;

	private static bool NaPaleta(Color c) => Paleta().Any(p => Perto(c, p));

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private void Placar()
	{
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA, {_pulados} NAO MEDIDO");
		Nota($" INJECOES: {_injOk} pegas, {_injFalha} passaram batido");
		if (_intrusoes > 0) Nota($" INTRUSOES: a pausa abriu {_intrusoes}x por tecla de fora -- a rodada nao foi limpa");
		foreach (string r in _reprovadas) Nota("   FALHA  " + r);
		foreach (string r in _injPassouBatido) Nota("   PASSOU (injecao) " + r);
		foreach (string r in _naoMedidos) Nota("   nao medido  " + r);
		foreach (string f in _fotosGravadas) Nota("   foto  " + f);
		Nota("==================================================================================");
	}
}
