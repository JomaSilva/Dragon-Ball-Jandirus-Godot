using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DO CONTROLE MARCAVEL (`--diagmarcavel`) ============================
/// O RELATO DO DONO, literal: *"um bug visual de leve, todos os botoes de checkmark como na imagem,
/// ao selecionar eles (colocar o ok) e colocar o mouse em cima, ele fica bugado o texto, a segunda
/// imagem mostra q sem o ok ele fica normal com mouse em cima"*.
///
/// Duas palavras dele mandam no desenho desta bancada:
///   * **"todos"** -- nao e uma tela nem um node, e a CLASSE inteira. Por isso ela mede em DUAS
///     telas de producao e em TRES tipos de controle, e nao so na caixa do microfone da foto.
///   * **"com o mouse em cima"** -- o defeito so existe no cruzamento de dois estados. Uma prova que
///     olhe so "marcado" ou so "com mouse" passa verde com o bug na tela.
///
/// ============================ ELA NAO RECONSTROI TELA NENHUMA ============================
/// A fase 1 conferiu o conserto numa cena montada a mao -- fiel, mas MINHA. Uma cena que eu escrevo
/// concorda comigo por construcao. Aqui os controles sao os do jogador: a `PauseMenu` que o `Boot`
/// pendurou no lobby (a tela de OPCOES, onde mora a caixa "usar o microfone" da foto do dono) e a
/// `CreationScreen` montada como o `Boot.PrepararCriacao` a monta. Os alvos sao ACHADOS varrendo a
/// arvore pelo texto, como quem olha a tela -- se alguem trocar o rotulo, a bancada fica vermelha,
/// que e o certo, porque o dono tambem procura pelo rotulo.
///
/// ============================ O QUE ELA MEDE E O PIXEL, E SO O PIXEL ============================
/// Este projeto ja assinou *"o corpo esta branco"* lendo um uniform, com a foto mostrando 0,0% de
/// branco. Entao a pergunta central desta bancada -- **de que cor e o pixel ATRAS DO TEXTO** -- e
/// respondida lendo a foto: a moda das cores dentro da faixa onde a palavra esta desenhada. O que o
/// tema DECLARA entra como segunda testemunha e nunca como a primeira: cada estado cobra
/// `pixel medido == BgColor que o motor resolveu`, e as duas so podem concordar se o desenho
/// aconteceu de verdade.
///
/// ============================ ANTES E DEPOIS, NA MESMA BANCADA ============================
/// `--pasta <dir>` diz onde gravar; `--comparar <dir>` diz onde estao as fotos da rodada anterior.
/// A rodada com o defeito de volta no `Tema.cs` grava em `antes/`; a rodada com o conserto grava em
/// `depois/` e COBRA, foto a foto:
///   * os tres estados que ja estavam certos tem que sair **identicos** (zero pixel diferente) --
///     e assim que se prova que o conserto nao vazou pra fora do estado que faltava;
///   * o quarto estado tem que sair **diferente** -- e assim que se prova que a rodada mediu duas
///     coisas diferentes, e nao a mesma coisa duas vezes.
///
/// COMO RODAR (janela no SEGUNDO monitor -- o dono trabalha no principal):
///     Godot --path . --diagmarcavel --pasta &lt;dir&gt; [--comparar &lt;dir&gt;] --position 1920,0 --resolution 1200x900
///
/// **PRECISA DE JANELA.** Em `--headless` nao ha foto, e sem foto esta bancada nao tem nada pra
/// dizer: as linhas de pixel se anunciam PULADAS e o placar sai com o terceiro contador aceso. Ver
/// `ChecaNoPixel` -- a licao de que *"sem janela nao e passou, e nao olhei"* ja foi paga aqui ao
/// lado, na `RoboDeOpcoes`.
///
/// ============================ RESIDUO: ZERO, POR CONSTRUCAO ============================
/// Ela marca e desmarca caixas de PRODUCAO, e os `Toggled` delas gravam `config.json` e ate trocam o
/// modo da janela ("tela cheia"). Por isso todo estado e posto com <see cref="BaseButton.SetPressedNoSignal"/>:
/// o desenho e exatamente o mesmo (e `status.pressed` que o `DRAW_HOVER_PRESSED` le), e nenhum
/// manipulador roda. No fim cada controle volta ao valor que tinha.
/// ==========================================================================================
/// </summary>
public partial class RoboDoMarcavel : Node
{
	// =====================================================================
	// PLACAR -- tres, como manda a casa: o que passou, o que falhou, e o que NAO FOI OLHADO
	// =====================================================================
	private int _ok, _falha, _pulados;
	private readonly List<string> _reprovadas = [];
	private readonly List<string> _naoMedidos = [];

	private static void Nota(string linha) => GD.Print("[marcavel] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	/// <summary>Uma prova que so existe se houver foto. Sem foto ela e PULADA, e nunca verde.</summary>
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

	/// <summary>
	/// Uma prova que so existe se a foto tiver o que medir. O MOTIVO do pulo entra na linha: um
	/// "sem janela" generico escondendo um "nao achei glifo nenhum" e uma bancada mentindo sobre o
	/// que ela nao olhou -- e o terceiro placar existe justamente pra nao deixar isso passar.
	/// </summary>
	private void ChecaSePode(string oque, bool podeMedir, string razao, bool passou, string detalhe = "")
	{
		if (!podeMedir)
		{
			Nota("  PULADA  " + oque + $"   [{razao}]");
			_pulados++;
			_naoMedidos.Add($"{oque}  ({razao})");
			return;
		}
		Checa(oque, passou, detalhe);
	}

	// =====================================================================
	// ONDE GRAVAR, E CONTRA O QUE COMPARAR
	// =====================================================================
	private string _pasta = "";
	private string _antes = "";

	/// <summary>Toda foto gravada nesta rodada: nome curto -> imagem. E o que a comparacao percorre.</summary>
	private readonly Dictionary<string, Image> _fotos = [];

	private static string Arg(string chave)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, chave);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	private static string Hex(Color c) =>
		$"#{(int)(c.R * 255 + 0.5f):x2}{(int)(c.G * 255 + 0.5f):x2}{(int)(c.B * 255 + 0.5f):x2}";

	public override void _Ready() => _ = Rodar();

	// =====================================================================
	// OS QUATRO ESTADOS -- o cruzamento que o dono descreveu
	// =====================================================================
	/// <summary>Marcado? Mouse em cima? -- as quatro combinacoes, na ordem das fotos do dono.</summary>
	private static readonly (int N, bool Marcado, bool Mouse, string Rotulo)[] Estados =
	[
		(1, false, false, "desmarcado sem mouse"),
		(2, false, true,  "desmarcado COM MOUSE"),
		(3, true,  false, "marcado sem mouse"),
		(4, true,  true,  "marcado COM MOUSE"),
	];

	/// <summary>O nome do stylebox que o Godot pede em cada modo de desenho. E a tabela do motor.</summary>
	private static string CaixaDo(BaseButton.DrawMode m) => m switch
	{
		BaseButton.DrawMode.Normal => "normal",
		BaseButton.DrawMode.Hover => "hover",
		BaseButton.DrawMode.Pressed => "pressed",
		BaseButton.DrawMode.HoverPressed => "hover_pressed",
		BaseButton.DrawMode.Disabled => "disabled",
		_ => "?",
	};

	/// <summary>A cor da letra que o Godot pede em cada modo de desenho.</summary>
	private static string CorDo(BaseButton.DrawMode m) => m switch
	{
		BaseButton.DrawMode.Normal => "font_color",
		BaseButton.DrawMode.Hover => "font_hover_color",
		BaseButton.DrawMode.Pressed => "font_pressed_color",
		BaseButton.DrawMode.HoverPressed => "font_hover_pressed_color",
		BaseButton.DrawMode.Disabled => "font_disabled_color",
		_ => "font_color",
	};

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async System.Threading.Tasks.Task Rodar()
	{
		_pasta = Arg("--pasta");
		_antes = Arg("--comparar");
		if (_pasta.Length == 0) _pasta = ProjectSettings.GlobalizePath("user://");
		if (!_pasta.EndsWith('/')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);

		Nota("==================================================================================");
		Nota(" FASE 2 -- A PROVA NO PIXEL, NAS TELAS DE PRODUCAO");
		Nota("==================================================================================");
		Nota($"  pasta desta rodada : {_pasta}");
		Nota($"  comparando contra  : {(_antes.Length == 0 ? "(nada -- esta e a primeira rodada)" : _antes)}");
		Nota($"  janela             : {DisplayServer.WindowGetSize()}  em {DisplayServer.WindowGetPosition()}");

		await Quadros(6);

		QuemResponde();

		await AsOpcoes();
		await ACriacao();

		Comparar();

		Placar();
		GetTree().Quit(_falha == 0 && _pulados == 0 ? 0 : 1);
	}

	private async System.Threading.Tasks.Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	// =====================================================================
	// 1. QUEM RESPONDE PELO ESTADO: O TEMA OU O GODOT DE FABRICA
	// =====================================================================
	/// <summary>
	/// A pergunta ANTES do pixel: quando o motor procura `hover_pressed` pra um `CheckBox`, quem
	/// devolve? Se for o tema de fabrica, o pixel ja esta perdido -- e isto aqui diz de onde ele vem
	/// sem eu ter que adivinhar pela cor.
	///
	/// A cadeia e a do Godot: pro node, o tema da arvore e consultado com o nome da CLASSE e com o de
	/// cada ancestral dela (CheckBox -> Button -> BaseButton -> Control); so depois vem o de fabrica.
	/// E por isso que uma linha em `"Button"` veste a familia inteira.
	/// </summary>
	private void QuemResponde()
	{
		Nota("");
		Nota("==================================================================================");
		Nota(" 1. QUEM RESPONDE PELO `hover_pressed`: O TEMA OU O GODOT DE FABRICA");
		Nota("==================================================================================");

		Theme meu = Tema.Atual;
		Theme fab = ThemeDB.Singleton.GetDefaultTheme();

		foreach (string cl in new[] { "CheckBox", "CheckButton", "Button", "OptionButton", "MenuButton", "ColorPickerButton" })
		{
			string quem = "FABRICA";
			string onde = "";
			string c = cl;
			while (c.Length > 0)
			{
				if (meu.HasStylebox("hover_pressed", c)) { quem = "TEMA"; onde = c; break; }
				if (c == "Control") break;
				c = ClassDB.GetParentClass(c);
			}
			Checa($"{cl}: o `hover_pressed` vem do TEMA", quem == "TEMA", $"{quem}{(onde.Length > 0 ? $" (registrado em \"{onde}\")" : "")}");
		}

		// O QUE A FABRICA DARIA, dito por ela e nao por mim -- e a coisa que o dono viu na tela.
		StyleBox fabCb = fab.GetStylebox("hover_pressed", "CheckBox");
		Nota($"  o que a FABRICA daria pro CheckBox: {(fabCb is StyleBoxFlat f2 ? $"Flat bg={Hex(f2.BgColor)}" : fabCb.GetType().Name)}"
			 + $"  margem={fabCb.ContentMarginLeft:0}");
	}

	// =====================================================================
	// 2. TELA 1 -- AS OPCOES (a tela da foto do dono)
	// =====================================================================
	private async System.Threading.Tasks.Task AsOpcoes()
	{
		Nota("");
		Nota("==================================================================================");
		Nota(" 2. TELA 1: OPCOES (`PauseMenu`, a mesma da foto do dono)");
		Nota("==================================================================================");

		if (PauseMenu.Instancia is not { } menu)
		{
			Checa("a tela de opcoes existe no lobby", false, "PauseMenu.Instancia e nulo");
			return;
		}
		menu.Abrir();
		await Quadros(4);

		var micro = Achar<CheckBox>(menu, c => c.Text.Contains("microfone"));
		var comum = Achar<Button>(menu, b => b.Text.Contains("Configurar teclas"));
		var seletor = Achar<OptionButton>(menu, o => o.GetItemCount() > 0 && o.GetItemText(0).Contains("baixo"));

		Checa("achei a caixa \"usar o microfone\" na tela de opcoes", micro != null);
		Checa("achei um botao COMUM (nao marcavel) pra usar de controle", comum != null);
		Checa("achei o seletor \"Grafico\" (OptionButton)", seletor != null);

		if (micro != null) await Medir("opcoes", "CheckBox", "usar-o-microfone", micro, estrela: true);
		if (seletor != null) await Medir("opcoes", "OptionButton", "grafico", seletor);

		// ---- O CONTROLE DA EXPERIENCIA: um botao COMUM, que nao tem o estado do defeito ----
		// Ele existe pra responder a pergunta "o conserto vazou?". Se a foto dele mudar entre a
		// rodada com defeito e a rodada consertada, o `hover_pressed` mexeu em quem nao devia.
		if (comum != null)
		{
			await Rolar(comum);
			await ForaDoAlcance();
			await Guardar("opcoes-botao-comum-1-sem-mouse", Recorte(await Foto(), comum));
			await SobreOControle(comum);
			await Guardar("opcoes-botao-comum-2-com-mouse", Recorte(await Foto(), comum));
			ChecaNoPixel("o botao comum reage ao mouse (senao a foto de controle nao vale nada)",
						 !UltimaFotoVazia, comum.GetDrawMode() == BaseButton.DrawMode.Hover, $"{comum.GetDrawMode()}");
			await ForaDoAlcance();
		}

		// ---- O PAINEL INTEIRO, EM REPOUSO: a prova mais larga de "nada mais mudou" ----
		// Nada marcado, nada sob o mouse. Este recorte contem TODOS os controles da tela; se ele
		// sair identico entre as duas rodadas, o conserto nao encostou em nenhum outro estado.
		if (micro?.GetParent() is Control && Ancestral<PanelContainer>(micro) is { } moldura)
		{
			await ForaDoAlcance();
			await Guardar("opcoes-PAINEL-INTEIRO-em-repouso", Recorte(await Foto(), moldura));
		}

		menu.Fechar("a bancada terminou");
		await Quadros(3);
	}

	// =====================================================================
	// 3. TELA 2 -- A CRIACAO DE PERSONAGEM
	// =====================================================================
	/// <summary>
	/// A SEGUNDA TELA, e ela nao e escolhida por ser conveniente: e a unica outra tela de producao
	/// que nasce sem rede nenhuma (o proprio cabecalho da `CreationScreen` diz isso) e ela traz o
	/// terceiro TIPO de controle marcavel do jogo -- o `Button` de `ToggleMode`, que os cartoes de
	/// planeta e de raca usam. Montada como o `Boot.PrepararCriacao` a monta.
	/// </summary>
	private async System.Threading.Tasks.Task ACriacao()
	{
		Nota("");
		Nota("==================================================================================");
		Nota(" 3. TELA 2: CRIACAO DE PERSONAGEM (`CreationScreen`)");
		Nota("==================================================================================");

		var tela = new CreationScreen { Name = "CriacaoDaBancada" };
		AddChild(tela);
		await Quadros(8);

		// O CARTAO: `Button` com `ToggleMode`, o terceiro tipo marcavel do jogo.
		var cartao = Achar<Button>(tela, b => b.ToggleMode && b.Visible && b.IsVisibleInTree() && b.Size.X > 40);
		Checa("achei um cartao marcavel (Button de ToggleMode) na criacao", cartao != null,
			  cartao != null ? $"\"{cartao.Text.Replace("\n", " / ")}\"" : "");

		if (cartao != null) await Medir("criacao", "Button(toggle)", "cartao", cartao);

		tela.QueueFree();
		await Quadros(3);
	}

	// =====================================================================
	// A MEDIDA -- os quatro estados de UM controle, fotografados e lidos
	// =====================================================================
	private sealed record Medida(int N, string Rotulo, BaseButton.DrawMode Modo, string Caixa,
								 string Declarada, float Margem, string Atras, float Frac,
								 string Letra, string LetraDeclarada, int Tique, int Vao, int Palavra,
								 string Perfil, string PainelNu, Rect2 Linha);

	private async System.Threading.Tasks.Task Medir(string tela, string tipo, string slug, BaseButton c, bool estrela = false)
	{
		Nota("");
		Nota($"  ---- {tela} / {tipo} / \"{Resumo(c)}\" ----");

		bool eraMarcado = c.ButtonPressed;
		await Rolar(c);

		var medidas = new List<Medida>();
		var recortes = new List<Image>();

		foreach ((int n, bool marcado, bool mouse, string rotulo) in Estados)
		{
			// SEM SINAL: o desenho e o mesmo (e `status.pressed` que o modo le), e nenhum
			// `Toggled` de producao roda -- ver o cabecalho, "residuo zero".
			c.SetPressedNoSignal(marcado);
			if (mouse) await SobreOControle(c); else await ForaDoAlcance();

			Image? foto = await Foto();
			Image? corte = Recorte(foto, c);
			recortes.Add(corte ?? Image.CreateEmpty(4, 4, false, Image.Format.Rgba8));
			await Guardar($"{tela}-{slug}-{n}-{Slug(rotulo)}", corte);

			medidas.Add(Ler(c, foto, n, rotulo));
		}

		foreach (Medida m in medidas)
			Nota($"    {m.N} {m.Rotulo,-22} modo={m.Modo,-13} caixa={m.Caixa,-13} margem={m.Margem:0}"
				 + $"\n       atras do texto={m.Atras} ({m.Frac * 100:0}%)  o tema declara={m.Declarada}"
				 + $"  letra={m.Letra} (declarada {m.LetraDeclarada})"
				 + $"\n       tique@{m.Tique} vao={m.Vao} palavra@{m.Palavra}"
				 + $"\n       |{m.Perfil}|");

		bool foto4 = medidas[3].Atras.Length > 1;
		string alvo = $"{tela}/{tipo}";

		// ---- 1. o estado 4 e mesmo o cruzamento. Sem isto, tudo abaixo mede outra coisa ----
		Checa($"{alvo}: o estado 4 e mesmo HoverPressed (marcado E com o mouse)",
			  medidas[3].Modo == BaseButton.DrawMode.HoverPressed, $"{medidas[3].Modo}");
		Checa($"{alvo}: o estado 2 e mesmo Hover (o mouse sintetico chega no controle)",
			  medidas[1].Modo == BaseButton.DrawMode.Hover, $"{medidas[1].Modo}");

		// ---- 2. A PROVA CENTRAL: as quatro cores atras do texto ----
		foreach (Medida m in medidas)
		{
			ChecaNoPixel($"{alvo}: estado {m.N} -- o pixel atras do texto e o que o tema declara",
						 m.Atras.Length > 1, m.Atras == m.Declarada, $"medido {m.Atras} / declarado {m.Declarada}");
			ChecaNoPixel($"{alvo}: estado {m.N} -- a cor atras do texto pertence a paleta do tema",
						 m.Atras.Length > 1, NaPaleta(c, m.Atras), $"{m.Atras}");
		}

		// AS QUATRO SAO DIFERENTES ENTRE SI: quatro estados que pintam a mesma cor sao um estado so.
		var cores = medidas.Select(m => m.Atras).ToList();
		ChecaNoPixel($"{alvo}: as quatro combinacoes pintam quatro cores DIFERENTES",
					 foto4, cores.Distinct().Count() == 4, string.Join(" ", cores));

		// ---- 3. o estado 4 nao e o cinza de fabrica ----
		// A fabrica nao pinta chapa nenhuma: ela devolve um stylebox VAZIO, e o que aparece atras do
		// texto passa a ser o painel nu. Entao a pergunta "e a fabrica?" se responde comparando o
		// pixel com o painel nu medido na MESMA foto, e nao com uma cor que eu tenha escrito aqui.
		ChecaNoPixel($"{alvo}: o estado 4 pinta chapa propria (nao e o painel nu da fabrica)",
					 foto4, medidas[3].Atras != medidas[3].PainelNu,
					 $"atras={medidas[3].Atras} painel nu={medidas[3].PainelNu}");
		ChecaNoPixel($"{alvo}: o estado 4 nao e branco nem cinza claro (o retangulo da foto do dono)",
					 foto4, Lum(medidas[3].Atras) < 0.25, $"luminancia {Lum(medidas[3].Atras):0.000}");

		// ---- 4. a derivacao: acesa como hover, afundada como pressed ----
		ChecaNoPixel($"{alvo}: o estado 4 e mais CLARO que o 3 (o mouse acende)",
					 foto4, Lum(medidas[3].Atras) > Lum(medidas[2].Atras),
					 $"{medidas[2].Atras} -> {medidas[3].Atras}");
		ChecaNoPixel($"{alvo}: o estado 4 e mais ESCURO que o 1 (continua marcado)",
					 foto4, Lum(medidas[3].Atras) < Lum(medidas[0].Atras),
					 $"{medidas[3].Atras} < {medidas[0].Atras}");

		// ---- 5. a letra ----
		bool temGlifo = medidas.All(m => m.Letra.Length > 1);
		ChecaSePode($"{alvo}: a letra do estado 4 e a mesma do 3 (o mouse nao troca a cor do texto)",
					foto4 && temGlifo, foto4 ? "nao achei glifo na faixa do texto" : "sem janela: nao ha foto",
					medidas[3].Letra == medidas[2].Letra, $"{medidas[2].Letra} -> {medidas[3].Letra}");
		ChecaSePode($"{alvo}: a letra do estado 4 nao e o #ffffff de fabrica",
					foto4 && temGlifo, foto4 ? "nao achei glifo na faixa do texto" : "sem janela: nao ha foto",
					medidas[3].Letra != "#ffffff", medidas[3].Letra);
		ChecaSePode($"{alvo}: a letra medida na foto e a que o tema declara pro estado 4",
					foto4 && temGlifo, foto4 ? "nao achei glifo na faixa do texto" : "sem janela: nao ha foto",
					medidas[3].Letra == medidas[3].LetraDeclarada,
					$"medida {medidas[3].Letra} / declarada {medidas[3].LetraDeclarada}");

		// ---- 6. a margem e o lugar da palavra: o "texto bugado" que o dono descreveu ----
		Checa($"{alvo}: a margem do estado 4 e a mesma do estado 1",
			  Mathf.IsEqualApprox(medidas[3].Margem, medidas[0].Margem),
			  $"{medidas[0].Margem:0} -> {medidas[3].Margem:0}");
		ChecaNoPixel($"{alvo}: a palavra comeca na MESMA coluna nos estados 3 e 4 (o texto nao anda)",
					 foto4 && medidas[2].Palavra > 0, medidas[3].Palavra == medidas[2].Palavra,
					 $"{medidas[2].Palavra} -> {medidas[3].Palavra}");

		// ---- a tira das quatro, pro olho do dono ----
		double pintada = TiraDeFotos.Montar(
			[.. recortes.Select((im, i) => new TiraDeFotos.Quadro(im, i + 1))],
			_pasta + $"TIRA-{tela}-{slug}-as-quatro.png");
		ChecaNoPixel($"{alvo}: a tira das quatro saiu pintada (nao e um retangulo de fundo)",
					 !UltimaFotoVazia, pintada > 0.5, $"{pintada * 100:0}% da tira e imagem");

		_medidos.Add((tela, slug, estrela, recortes));

		c.SetPressedNoSignal(eraMarcado);
		await ForaDoAlcance();
	}

	/// <summary>
	/// Todo alvo medido, guardado pra montar a tira ANTES x DEPOIS no fim. `Estrela` marca a caixa
	/// da foto do dono -- ela ganha a tira com o nome curto, que e o arquivo que ele abre primeiro.
	/// </summary>
	private readonly List<(string Tela, string Slug, bool Estrela, List<Image> Recortes)> _medidos = [];

	// =====================================================================
	// LER A FOTO
	// =====================================================================
	private Medida Ler(BaseButton c, Image? img, int n, string rotulo)
	{
		BaseButton.DrawMode modo = c.GetDrawMode();
		string caixa = CaixaDo(modo);
		StyleBox sb = c.GetThemeStylebox(caixa);
		string declarada = sb is StyleBoxFlat f ? Hex(f.BgColor) : "(VAZIO)";
		string letraDecl = Hex(c.GetThemeColor(CorDo(modo)));
		Rect2 r = c.GetGlobalRect();

		if (img == null || img.IsEmpty())
			return new Medida(n, rotulo, modo, caixa, declarada, (float)sb.ContentMarginLeft, "", 0, "",
							  letraDecl, 0, 0, 0, "(sem foto)", "", r);

		// O PAINEL NU: tres pixels ACIMA do controle. E o que apareceria se o stylebox fosse vazio,
		// medido na mesma foto -- e nao uma cor que eu tenha decidido que o fundo tem.
		Color nu = Amostra(img, (int)(r.Position.X + r.Size.X / 2), (int)r.Position.Y - 3);

		// A moda de TODO o retangulo do controle da a chapa; ela guia o perfil.
		(Color dom, _) = Moda(img, Caixa(r, 2));
		string perfil = Perfil(img, r, dom);
		(int tique, int fim, int vao, int palavra) = Vao(perfil);

		// ============================ A COR ATRAS DO TEXTO ============================
		// Nao e a moda do controle inteiro: e a moda da FAIXA ONDE A PALAVRA ESTA DESENHADA. E
		// literalmente "o pixel atras do texto" que o dono descreveu, e nao "a cor geral do botao".
		Rect2I faixa = FaixaDoTexto(img, r, dom);
		(Color atras, float frac) = Moda(img, faixa);
		Color letra = CorDaLetra(img, faixa, atras);

		return new Medida(n, rotulo, modo, caixa, declarada, (float)sb.ContentMarginLeft,
						  Hex(atras), frac, letra.A > 0 ? Hex(letra) : "", letraDecl,
						  tique, vao, faixa.Position.X - (int)r.Position.X, perfil, Hex(nu), r);
	}

	/// <summary>
	/// ============================ ONDE, NA FOTO, ESTA O TEXTO ============================
	/// Achada NA PROPRIA FOTO, e nao pela conta de layout do Godot refeita a mao aqui -- uma segunda
	/// copia daquela conta concordaria com a primeira ate o dia em que uma das duas mudasse.
	///
	/// Duas passadas, e as duas existem porque os controles marcaveis do jogo tem DOIS arranjos:
	///   * **linhas**: a ULTIMA banda horizontal de tinta e o rotulo. Numa caixa de marcar so ha uma
	///     banda (tique e palavra na mesma linha); num CARTAO o icone fica em cima e o nome embaixo,
	///     e sem esta passada a faixa cairia no icone -- foi o que aconteceu na primeira rodada.
	///   * **colunas**: dentro dessa banda, o primeiro bloco de tinta e o TIQUE (ou o icone) se
	///     houver um respiro de 3+ colunas depois dele e ainda sobrar palavra. Tres colunas e o
	///     discriminador certo porque o Godot separa icone e texto por `h_separation` (4 px), e
	///     letras vizinhas de uma mesma palavra se tocam ou deixam 1 px. Sem isto o tique -- que o
	///     motor pinta com a cor de ICONE, quase branca -- entraria na conta da cor da LETRA e a
	///     resposta seria o branco do tique, nao a cor do texto.
	/// ==========================================================================================
	/// </summary>
	private static Rect2I FaixaDoTexto(Image img, Rect2 r, Color chapa)
	{
		// ============================ POR QUE A FOLGA E 5 E NAO 2 ============================
		// **MEDIDO na primeira rodada**: com folga 2 a faixa saia com 99% de chapa nos estados 2, 3
		// e 4, e a cor da letra vinha uma mistura (`#89633a`) que o tema nunca escreveu. A causa e
		// que esses tres estados tem BORDA VIVA (`#f0a041`) e canto arredondado de raio 6: o arco
		// anti-serrilhado do canto ainda passa dentro da folga 2, e como ele e laranja sobre chapa
		// escura ele estoura o limiar de tinta em TODA linha e TODA coluna -- a faixa virava o
		// controle inteiro. No estado 1 nao acontecia, porque a borda dele e um cinza discreto que
		// nao estoura o limiar: uma diferenca de estado que so aparece no pixel, e nao no codigo.
		// A folga 5 fica dentro do arco e sobra de folga pra margem de conteudo, que e 10.
		// ==========================================================================================
		const int Folga = 5;
		int x0 = Math.Clamp((int)r.Position.X + Folga, 0, img.GetWidth() - 1);
		int y0 = Math.Clamp((int)r.Position.Y + Folga, 0, img.GetHeight() - 1);
		int larg = Math.Max(1, Math.Min((int)r.Size.X - Folga * 2, img.GetWidth() - x0));
		int alt = Math.Max(1, Math.Min((int)r.Size.Y - Folga * 2, img.GetHeight() - y0));

		bool Tinta(int x, int y)
		{
			Color c = img.GetPixel(x0 + x, y0 + y);
			return Math.Abs(c.R - chapa.R) + Math.Abs(c.G - chapa.G) + Math.Abs(c.B - chapa.B) > 0.25f;
		}

		// ---- a ultima banda de LINHAS com tinta ----
		int ly = alt - 1;
		while (ly >= 0 && Vazia(ly)) ly--;
		if (ly < 0) return new Rect2I(x0, y0, larg, alt);   // controle sem tinta nenhuma
		int fy = ly;
		while (fy > 0 && !Vazia(fy - 1)) fy--;

		bool Vazia(int y)
		{
			for (int x = 0; x < larg; x++) if (Tinta(x, y)) return false;
			return true;
		}

		// ---- dentro dela, as bandas de COLUNAS ----
		bool Coluna(int x)
		{
			for (int y = fy; y <= ly; y++) if (Tinta(x, y)) return true;
			return false;
		}

		int cx = 0;
		while (cx < larg && !Coluna(cx)) cx++;
		int fim = cx;
		while (fim < larg && Coluna(fim)) fim++;
		int depois = fim;
		while (depois < larg && !Coluna(depois)) depois++;

		int comeco = depois - fim >= 3 && larg - depois >= 12 ? depois : cx;

		// ---- e o mesmo corte do outro lado: o OptionButton tem a SETINHA na ponta direita ----
		// Ela e icone como o tique, e pela mesma razao nao pode entrar na conta da cor da letra.
		int dx = larg - 1;
		while (dx > comeco && !Coluna(dx)) dx--;
		int inicioDoUltimo = dx;
		while (inicioDoUltimo > comeco && Coluna(inicioDoUltimo - 1)) inicioDoUltimo--;
		int antes = inicioDoUltimo;
		while (antes > comeco && !Coluna(antes - 1)) antes--;
		int termino = inicioDoUltimo - antes >= 3 && antes - comeco >= 12 ? antes : dx + 1;

		return new Rect2I(x0 + comeco, y0 + fy,
						  Math.Max(2, termino - comeco), Math.Max(2, ly - fy + 1));
	}

	private static Rect2I Caixa(Rect2 r, int folga) => new(
		(int)r.Position.X + folga, (int)r.Position.Y + folga,
		Math.Max(1, (int)r.Size.X - folga * 2), Math.Max(1, (int)r.Size.Y - folga * 2));

	private static Color Amostra(Image img, int x, int y) =>
		x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()
			? new Color(0, 0, 0) : img.GetPixel(x, y);

	/// <summary>A cor mais frequente de um retangulo da foto, e que fracao dele ela ocupa.</summary>
	private static (Color, float) Moda(Image img, Rect2I r)
	{
		var conta = new Dictionary<uint, int>();
		int total = 0;
		int x1 = Math.Min(img.GetWidth(), r.Position.X + r.Size.X);
		int y1 = Math.Min(img.GetHeight(), r.Position.Y + r.Size.Y);
		for (int y = Math.Max(0, r.Position.Y); y < y1; y++)
			for (int x = Math.Max(0, r.Position.X); x < x1; x++)
			{
				Color c = img.GetPixel(x, y);
				conta[Chave(c)] = conta.GetValueOrDefault(Chave(c)) + 1;
				total++;
			}
		if (total == 0) return (new Color(0, 0, 0), 0);
		KeyValuePair<uint, int> top = conta.OrderByDescending(kv => kv.Value).First();
		return (Cor(top.Key), top.Value / (float)total);
	}

	private static uint Chave(Color c) =>
		((uint)(c.R * 255 + 0.5f) << 16) | ((uint)(c.G * 255 + 0.5f) << 8) | (uint)(c.B * 255 + 0.5f);

	private static Color Cor(uint k) =>
		Color.Color8((byte)(k >> 16), (byte)((k >> 8) & 0xff), (byte)(k & 0xff));

	/// <summary>
	/// O PERFIL HORIZONTAL: quanta tinta ha em cada coluna. O bloco cheio da esquerda e o tique, o
	/// buraco depois dele e o respiro, e o resto e a palavra. Foi neste desenho que a fase 1 viu o
	/// respiro ir a zero -- o "texto bugado" do relato.
	/// </summary>
	private static string Perfil(Image img, Rect2 r, Color dom)
	{
		int y0 = (int)r.Position.Y + 6, y1 = (int)(r.Position.Y + r.Size.Y) - 6;
		var sb = new System.Text.StringBuilder();
		int fim = (int)Math.Min(r.Position.X + Math.Min(r.Size.X, 122), img.GetWidth());
		for (int x = (int)r.Position.X + 2; x < fim; x++)
		{
			int n = 0;
			for (int y = Math.Max(0, y0); y < Math.Min(img.GetHeight(), y1); y++)
				if (Mathf.Abs(img.GetPixel(x, y).Luminance - dom.Luminance) > 0.10f) n++;
			sb.Append(n == 0 ? '.' : n > 10 ? '#' : '+');
		}
		return sb.ToString();
	}

	private static (int tique, int fim, int vao, int palavra) Vao(string perfil)
	{
		int i = 0;
		while (i < perfil.Length && perfil[i] == '.') i++;
		int tique = i;
		while (i < perfil.Length && perfil[i] != '.') i++;
		int fim = i;
		while (i < perfil.Length && perfil[i] == '.') i++;
		return (tique, fim, i - fim, i);
	}

	/// <summary>
	/// A COR DO MIOLO DOS GLIFOS dentro da faixa do texto.
	///
	/// **NAO e a moda do que nao e chapa**, e a primeira rodada mostrou por que: a 14 px de fonte a
	/// maioria esmagadora dos pixels de uma letra e BORDA ANTI-SERRILHADA -- mistura entre a tinta e
	/// a chapa. A moda devolvia `#89633a`, uma cor que o tema nunca escreveu em lugar nenhum, e a
	/// checagem virava uma discussao sobre serrilhado em vez de sobre a cor do texto.
	///
	/// O que se pergunta aqui e outra coisa: **qual e a tinta mais PURA que aparece na faixa** -- a
	/// cor mais distante da chapa que ocorre pelo menos tres vezes (tres, e nao uma, pra um pixel
	/// solto de outra coisa nao virar a resposta). Num texto anti-serrilhado essa cor e exatamente a
	/// dos pixels de cobertura cheia, que e a cor que o tema mandou pintar.
	/// </summary>
	private static Color CorDaLetra(Image img, Rect2I faixa, Color chapa)
	{
		var conta = new Dictionary<uint, int>();
		int x1 = Math.Min(img.GetWidth(), faixa.Position.X + faixa.Size.X);
		int y1 = Math.Min(img.GetHeight(), faixa.Position.Y + faixa.Size.Y);
		for (int y = Math.Max(0, faixa.Position.Y); y < y1; y++)
			for (int x = Math.Max(0, faixa.Position.X); x < x1; x++)
			{
				Color c = img.GetPixel(x, y);
				conta[Chave(c)] = conta.GetValueOrDefault(Chave(c)) + 1;
			}

		double Dist(uint k)
		{
			Color c = Cor(k);
			return Math.Abs(c.R - chapa.R) + Math.Abs(c.G - chapa.G) + Math.Abs(c.B - chapa.B);
		}

		var candidatos = conta.Where(kv => kv.Value >= 3 && Dist(kv.Key) > 0.20)
							  .OrderByDescending(kv => Dist(kv.Key)).ToList();
		return candidatos.Count == 0 ? new Color(0, 0, 0, 0) : Cor(candidatos[0].Key);
	}

	private static double Lum(string hex)
	{
		if (hex.Length < 7) return -1;
		return new Color(hex).Luminance;
	}

	/// <summary>
	/// A PALETA DO TEMA, perguntada AO CONTROLE: as chapas dos quatro estados que ele mesmo resolve.
	/// Uma lista escrita a mao aqui seria uma segunda copia do tema, e as duas concordariam ate o dia
	/// em que alguem mudasse uma delas.
	/// </summary>
	private static bool NaPaleta(BaseButton c, string hex) =>
		new[] { "normal", "hover", "pressed", "hover_pressed" }
			.Select(n => c.GetThemeStylebox(n))
			.OfType<StyleBoxFlat>()
			.Any(f => Hex(f.BgColor) == hex);

	// =====================================================================
	// A FOTO
	// =====================================================================
	private bool UltimaFotoVazia = true;

	private async System.Threading.Tasks.Task<Image?> Foto()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		UltimaFotoVazia = img == null || img.IsEmpty();
		return UltimaFotoVazia ? null : img;
	}

	private static Image? Recorte(Image? img, Control c)
	{
		if (img == null) return null;
		Rect2 r = c.GetGlobalRect();
		var caixa = new Rect2I(
			Math.Clamp((int)r.Position.X, 0, img.GetWidth() - 1),
			Math.Clamp((int)r.Position.Y, 0, img.GetHeight() - 1),
			Math.Clamp((int)r.Size.X, 1, img.GetWidth()),
			Math.Clamp((int)r.Size.Y, 1, img.GetHeight()));
		caixa.Size = new Vector2I(
			Math.Min(caixa.Size.X, img.GetWidth() - caixa.Position.X),
			Math.Min(caixa.Size.Y, img.GetHeight() - caixa.Position.Y));
		return caixa.Size.X <= 0 || caixa.Size.Y <= 0 ? null : img.GetRegion(caixa);
	}

	private async System.Threading.Tasks.Task Guardar(string nome, Image? corte)
	{
		if (corte == null) { _pulados++; _naoMedidos.Add($"foto {nome}"); return; }
		corte.SavePng(_pasta + nome + ".png");
		_fotos[nome] = corte;
		await System.Threading.Tasks.Task.CompletedTask;
	}

	// =====================================================================
	// O MOUSE
	// =====================================================================
	/// <summary>
	/// O MOUSE SINTETICO, empurrado pela porta de entrada do proprio motor. Nao e um campo escrito
	/// na mao: o evento entra no `Viewport`, o `Viewport` decide quem esta embaixo, e o controle
	/// recebe o `MOUSE_ENTER` de verdade. Por isso toda medida cobra o `GetDrawMode` logo depois --
	/// se o evento nao tivesse chegado, a bancada estaria fotografando o estado errado calada.
	/// </summary>
	private void Mouse(Vector2 onde)
	{
		var m = new InputEventMouseMotion { Position = onde, GlobalPosition = onde, Relative = new Vector2(1, 1) };
		GetViewport().PushInput(m, true);
	}

	private async System.Threading.Tasks.Task SobreOControle(Control c)
	{
		Rect2 r = c.GetGlobalRect();
		Mouse(r.Position + r.Size / 2);
		await Quadros(3);
	}

	/// <summary>O canto da janela: dentro dela (senao o motor larga o `mouse_over`), fora de tudo.</summary>
	private async System.Threading.Tasks.Task ForaDoAlcance()
	{
		Mouse(new Vector2(2, 2));
		await Quadros(3);
	}

	private async System.Threading.Tasks.Task Rolar(Control c)
	{
		if (Ancestral<ScrollContainer>(c) is { } rol) rol.EnsureControlVisible(c);
		await Quadros(3);
	}

	// =====================================================================
	// ANTES x DEPOIS
	// =====================================================================
	/// <summary>
	/// ============================ A SEGUNDA METADE DA PROVA ============================
	/// Verde sozinho nao diz nada: um placar verde e compativel com uma bancada cega. Esta parte
	/// compara foto a foto com a rodada em que o defeito estava DE VOLTA no `Tema.cs`, e cobra as
	/// duas coisas ao mesmo tempo:
	///   * o que mudou tem que ser SO o estado 4 -- e a prova de que o conserto nao vazou;
	///   * o estado 4 tem que ter mudado -- e a prova de que as duas rodadas nao mediram a mesma
	///     coisa duas vezes (uma bancada que fotografasse o estado errado sairia identica nas duas).
	/// ==========================================================================================
	/// </summary>
	private void Comparar()
	{
		if (_antes.Length == 0) return;
		string ant = _antes.EndsWith('/') ? _antes : _antes + "/";

		Nota("");
		Nota("==================================================================================");
		Nota(" 4. ANTES (com o defeito) x DEPOIS (consertado), FOTO A FOTO");
		Nota("==================================================================================");

		int mudaram = 0, iguais = 0;
		foreach ((string nome, Image agora) in _fotos.OrderBy(k => k.Key))
		{
			string caminho = ant + nome + ".png";
			Image? antes = Image.LoadFromFile(caminho);
			if (antes == null || antes.IsEmpty())
			{
				Nota($"  PULADA  {nome}   [nao ha foto correspondente em {ant}]");
				_pulados++; _naoMedidos.Add($"antes x depois: {nome}");
				continue;
			}

			double dif = Diferenca(antes, agora);
			// O NOME DIZ O ESTADO: so o `-4-` e o cruzamento marcado+mouse, o unico que podia mudar.
			bool eraPraMudar = nome.Contains("-4-");
			bool passou = eraPraMudar ? dif > 0.02 : dif == 0;
			Nota((passou ? "  OK    " : "  FALHA ")
				 + $"{nome,-52} {(eraPraMudar ? "TINHA que mudar" : "tinha que ficar IDENTICA")}"
				 + $"   [{dif * 100:0.00}% dos pixels diferentes]");
			if (passou) _ok++; else { _falha++; _reprovadas.Add($"antes x depois: {nome}"); }
			if (dif > 0) mudaram++; else iguais++;
		}
		Nota($"  resumo: {iguais} foto(s) identica(s), {mudaram} foto(s) diferente(s)");

		// ---- AS TIRAS QUE O DONO ABRE: as quatro combinacoes, antes em cima e depois embaixo ----
		// Uma por alvo, e nao so pela caixa da foto: o pedido dele foi *"todos os botoes de
		// checkmark"*, e um alcance que so se le em tabela de numeros nao e um alcance que ele
		// consiga conferir com o olho.
		foreach ((string tela, string slug, bool estrela, List<Image> recortes) in _medidos)
		{
			var pares = new List<TiraDeFotos.Quadro>();
			for (int i = 0; i < recortes.Count; i++)
			{
				Image? antes = Image.LoadFromFile(ant + $"{tela}-{slug}-{i + 1}-{Slug(Estados[i].Rotulo)}.png");
				pares.Add(new TiraDeFotos.Quadro(antes == null ? recortes[i] : Empilhar(antes, recortes[i]), i + 1));
			}
			string caminho = _pasta + (estrela ? "TIRA-ANTES-x-DEPOIS.png" : $"TIRA-ANTES-x-DEPOIS-{tela}-{slug}.png");
			double pintada = TiraDeFotos.Montar(pares, caminho);
			Checa($"a tira ANTES x DEPOIS de {tela}/{slug} saiu pintada", pintada > 0.5, $"{pintada * 100:0}% da tira e imagem");
			Nota($"  tira: {caminho}   (cada coluna: em cima ANTES, embaixo DEPOIS)");
		}
	}

	/// <summary>Que fracao dos pixels difere entre duas fotos do mesmo recorte.</summary>
	private static double Diferenca(Image a, Image b)
	{
		if (a.GetWidth() != b.GetWidth() || a.GetHeight() != b.GetHeight()) return 1;
		var x = (Image)a.Duplicate(); x.Convert(Image.Format.Rgba8);
		var y = (Image)b.Duplicate(); y.Convert(Image.Format.Rgba8);
		int dif = 0, total = x.GetWidth() * x.GetHeight();
		for (int j = 0; j < x.GetHeight(); j++)
			for (int i = 0; i < x.GetWidth(); i++)
				if (Chave(x.GetPixel(i, j)) != Chave(y.GetPixel(i, j))) dif++;
		return total == 0 ? 1 : (double)dif / total;
	}

	/// <summary>
	/// Duas fotos, uma em cima da outra. O empilhamento vertical mora aqui e a fileira numerada mora
	/// na <see cref="TiraDeFotos"/> -- sao coisas diferentes, mas a licao e a mesma e ela ja foi paga
	/// la: `BlitRect` copia byte a byte e NAO converte formato, e sem o `Convert` a montagem sai
	/// preta com as duas fotos boas no disco.
	/// </summary>
	private static Image Empilhar(Image cima, Image baixo)
	{
		var a = (Image)cima.Duplicate(); a.Convert(Image.Format.Rgba8);
		var b = (Image)baixo.Duplicate(); b.Convert(Image.Format.Rgba8);
		const int Vao = 6;
		int larg = Math.Max(a.GetWidth(), b.GetWidth());
		Image fora = Image.CreateEmpty(larg, a.GetHeight() + b.GetHeight() + Vao, false, Image.Format.Rgba8);
		fora.Fill(TiraDeFotos.Fundo);
		fora.BlitRect(a, new Rect2I(0, 0, a.GetWidth(), a.GetHeight()), Vector2I.Zero);
		fora.BlitRect(b, new Rect2I(0, 0, b.GetWidth(), b.GetHeight()), new Vector2I(0, a.GetHeight() + Vao));
		return fora;
	}

	// =====================================================================
	// FERRAMENTA
	// =====================================================================
	private static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	private static T? Achar<T>(Node raiz, Func<T, bool> quer) where T : Node =>
		Todos(raiz).OfType<T>().FirstOrDefault(quer);

	private static T? Ancestral<T>(Node n) where T : Node
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is T t) return t;
		return null;
	}

	private static string Resumo(BaseButton c) =>
		(c is Button b ? b.Text : c.Name.ToString()).Replace("\n", " / ");

	private static string Slug(string s)
	{
		var sb = new System.Text.StringBuilder();
		foreach (char ch in s.ToLowerInvariant())
			sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
		return sb.ToString();
	}

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private void Placar()
	{
		Nota("");
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA, {_pulados} NAO MEDIDO");
		Nota("==================================================================================");
		foreach (string r in _reprovadas) Nota("  reprovada: " + r);
		foreach (string r in _naoMedidos) Nota("  nao medido: " + r);
		Nota($"  fotos em: {_pasta}");
	}
}
