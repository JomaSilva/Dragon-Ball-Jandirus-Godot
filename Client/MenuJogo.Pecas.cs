using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A LINGUA VISUAL COMUM DAS ABAS DO MENU P ============================
/// O dono redesenhou a aba Learning com o agente e depois olhou o resto: *"ta mt cru o resto, da uma
/// boa melhorada pra deixar mais profissional"*. O que fazia a Learning parecer pronta e o resto
/// parecer prototipo nao era cor nem fonte (o tema ja e um so): era ESTRUTURA. Learning tem cartoes
/// com chapa e borda, uma faixa com o numero grande, linhas de tier, pilulas de estado; as outras
/// abas eram uma coluna de "rotulo ..... valor" e muros de texto.
///
/// Estas sao as pecas que a Learning usa, tiradas de la e postas num lugar so, pra que toda aba
/// fale a mesma lingua:
///
///   * <see cref="Cartao"/>      -- uma secao com chapa, borda e titulo pequeno em caixa alta;
///   * <see cref="Colunas"/>     -- duas colunas de cartoes (a grade das arvores da Learning);
///   * <see cref="Faixa"/>       -- o numero GRANDE com rotulo e legenda (a faixa de marcos);
///   * <see cref="LinhaComBarra"/> -- rotulo/valor com uma barra fina embaixo: le-se de relance;
///   * <see cref="Pilula"/>      -- um estado numa capsula colorida (SUA, EM USO, VAGO, alto/baixo);
///   * <see cref="BotaoComDescricao"/> -- o botao de verb com a frase do que ele faz embaixo;
///   * <see cref="Nota"/>        -- o texto apagado de explicacao, dentro de um cartao.
///
/// ============================ O CONTRATO COM AS BANCADAS NAO MUDA ============================
/// `MenuJogo.ValorDesenhado(aba, rotulo)` e a porta por onde a `--diagbancada` e a `--diagmostrador`
/// leem o que a aba ESCREVEU ("Battle Power", "Ki", "Poder efetivo", "Multiplicador total"...). Toda
/// linha rotulo/valor daqui carrega o metadado `linha` com o rotulo, e a porta procura por ele em
/// qualquer profundidade -- uma linha que desce pra dentro de um cartao continua legivel. Os TEXTOS
/// dos valores tambem nao mudam de forma: "120 / 120   (100%)" continua assim, porque e assim que a
/// bancada os desmonta.
/// ================================================================================================
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// UM CARTAO DE SECAO: chapa clara, borda, titulo pequeno em caixa alta -- a mesma cara do card de
	/// arvore da Learning. Devolve o CORPO (a coluna de dentro) pra quem chama encher. `destaque`
	/// troca a borda pela laranja: e o cartao que a aba quer que se olhe primeiro (o pedido de amizade
	/// pendente, a forma em uso).
	/// </summary>
	private VBoxContainer Cartao(string titulo, Control? pai = null, bool destaque = false)
	{
		var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		card.AddThemeStyleboxOverride("panel",
			destaque ? Tema.Caixa(Tema.PainelClaro, Tema.BordaViva, 10) : Tema.Caixa(Tema.PainelClaro, Tema.Borda, 10));
		// A IDENTIDADE VAI EM METADADO, e nao no `Name` (o Godot renomeia irmaos homonimos) -- e a
		// bancada conta cartoes por ele, como ja conta os da Learning.
		card.SetMeta("cartao", "secao");
		card.SetMeta("titulo", titulo);

		var corpo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		corpo.AddThemeConstantOverride("separation", 4);
		if (titulo.Length > 0)
		{
			Label t = Tema.Rotulo(titulo);
			t.AddThemeColorOverride("font_color", destaque ? Tema.Destaque : Tema.TextoFraco);
			corpo.AddChild(t);
		}
		card.AddChild(corpo);
		(pai ?? _conteudo).AddChild(card);
		return corpo;
	}

	/// <summary>DUAS COLUNAS de cartoes: a grade das arvores da Learning. Cada filho ocupa metade.</summary>
	private GridContainer Colunas(Control? pai = null, int colunas = 2)
	{
		var grade = new GridContainer { Columns = colunas, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		grade.AddThemeConstantOverride("h_separation", 8);
		grade.AddThemeConstantOverride("v_separation", 8);
		(pai ?? _conteudo).AddChild(grade);
		return grade;
	}

	/// <summary>Um respiro vertical entre blocos.</summary>
	private void Espaco(int px = 6, Control? pai = null) =>
		(pai ?? _conteudo).AddChild(new Control { CustomMinimumSize = new Vector2(0, px), MouseFilter = Control.MouseFilterEnum.Ignore });

	/// <summary>
	/// A FAIXA: o numero GRANDE com o rotulo a esquerda e a legenda a direita -- a mesma cara da faixa
	/// de marcos da Learning. E o lugar do UNICO numero que a aba quer que se leia primeiro (o BP, o
	/// multiplicador, o nivel de tech). O metadado `faixa` leva o rotulo, pra bancada achar.
	/// </summary>
	private PanelContainer Faixa(string rotulo, string grande, string legenda, Color? cor = null, Control? pai = null)
	{
		var p = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		p.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.Borda, 8));
		p.SetMeta("faixa", rotulo);

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 12);
		// A FAIXA TAMBEM E UMA LINHA pra porta de bancada (`ValorDesenhado`): o rotulo no metadado, e o
		// valor lido e "grande + legenda" -- "??? (sem scouter)", "3.000.000 (base 2.500.000)" -- que e
		// exatamente o texto que a linha de sempre escrevia e que a `--diagbancada` desmonta.
		linha.SetMeta("linha", rotulo);
		linha.SetMeta("faixa", rotulo);
		Label r = Tema.Rotulo(rotulo);
		r.VerticalAlignment = VerticalAlignment.Center;
		linha.AddChild(r);

		var g = new Label { Text = grande, VerticalAlignment = VerticalAlignment.Center, Name = "Grande" };
		g.AddThemeFontSizeOverride("font_size", 26);
		g.AddThemeColorOverride("font_color", cor ?? Tema.Destaque);
		linha.AddChild(g);

		var l = new Label
		{
			Text = legenda, VerticalAlignment = VerticalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		l.AddThemeFontSizeOverride("font_size", 12);
		l.AddThemeColorOverride("font_color", Tema.TextoFraco);
		linha.AddChild(l);

		p.AddChild(linha);
		(pai ?? _conteudo).AddChild(p);
		return p;
	}

	/// <summary>
	/// A LINHA ROTULO/VALOR, montada sem pendurar: rotulo apagado a esquerda, valor a direita na cor.
	/// E o que <see cref="Linha"/> sempre foi; mora aqui pra poder cair dentro de um cartao. O metadado
	/// `linha` e o que a porta de bancada <see cref="ValorDesenhado"/> procura.
	/// </summary>
	private static HBoxContainer LinhaSolta(string rotulo, string valor, Color? cor = null)
	{
		var h = new HBoxContainer();
		h.SetMeta("linha", rotulo);
		var a = new Label { Text = rotulo, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		a.AddThemeColorOverride("font_color", Tema.TextoFraco);
		a.AddThemeFontSizeOverride("font_size", 13);
		h.AddChild(a);
		var b = new Label { Text = valor, HorizontalAlignment = HorizontalAlignment.Right };
		b.AddThemeColorOverride("font_color", cor ?? Tema.Texto);
		b.AddThemeFontSizeOverride("font_size", 13);
		h.AddChild(b);
		return h;
	}

	/// <summary>Uma linha rotulo/valor DENTRO de um pai (um cartao, uma coluna).</summary>
	private static HBoxContainer Linha(string rotulo, string valor, Color? cor, Control pai)
	{
		HBoxContainer h = LinhaSolta(rotulo, valor, cor);
		pai.AddChild(h);
		return h;
	}

	/// <summary>
	/// A LINHA COM BARRA: rotulo/valor em cima e uma barra fina embaixo, cheia na razao dada. E como
	/// se le vida, Ki, maestria e atributo de relance -- o numero continua la (e e ele que a bancada
	/// le), a barra e o que o olho pega antes de ler.
	///
	/// `teto` acima de 1 e o trilho do Ki: a barra tem pra onde crescer alem do tanque, como a do HUD.
	/// </summary>
	private static void LinhaComBarra(string rotulo, string valor, double razao, Color cor, Control pai,
									  Color? corDoValor = null, double teto = 1)
	{
		var caixa = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		caixa.AddThemeConstantOverride("separation", 2);
		caixa.AddChild(LinhaSolta(rotulo, valor, corDoValor));

		double max = Math.Max(teto, 1e-9);
		var b = new ProgressBar
		{
			CustomMinimumSize = new Vector2(0, 6),
			ShowPercentage = false,
			MaxValue = max,
			Value = Math.Clamp(double.IsNaN(razao) ? 0 : razao, 0, max),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		var cheio = new StyleBoxFlat { BgColor = cor };
		cheio.SetCornerRadiusAll(3);
		b.AddThemeStyleboxOverride("fill", cheio);
		b.SetMeta("barra", rotulo);
		caixa.AddChild(b);
		pai.AddChild(caixa);
	}

	/// <summary>
	/// UMA PILULA: texto curto numa capsula com a borda e a letra na cor, e a chapa na mesma cor bem
	/// transparente. E o estado que se le sem ler: "SUA", "EM USO", "VAGO", "alto", "KO". O metadado
	/// `pilula` leva o texto, pra bancada achar.
	/// </summary>
	private static PanelContainer Pilula(string texto, Color cor)
	{
		var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		var s = new StyleBoxFlat
		{
			BgColor = new Color(cor, 0.16f),
			BorderColor = new Color(cor, 0.9f),
			ContentMarginLeft = 7, ContentMarginRight = 7, ContentMarginTop = 1, ContentMarginBottom = 1,
		};
		s.SetBorderWidthAll(1);
		s.SetCornerRadiusAll(9);
		p.AddThemeStyleboxOverride("panel", s);
		p.SetMeta("pilula", texto);
		var l = new Label { Text = texto, MouseFilter = Control.MouseFilterEnum.Ignore };
		l.AddThemeFontSizeOverride("font_size", 10);
		l.AddThemeColorOverride("font_color", cor);
		p.AddChild(l);
		return p;
	}

	/// <summary>Uma fileira de pilulas, que quebra linha quando nao cabe.</summary>
	private static HFlowContainer Pilulas(params (string Texto, Color Cor)[] itens)
	{
		var f = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		f.AddThemeConstantOverride("h_separation", 4);
		f.AddThemeConstantOverride("v_separation", 3);
		foreach ((string texto, Color cor) in itens)
			if (texto.Length > 0) f.AddChild(Pilula(texto, cor));
		return f;
	}

	/// <summary>Um titulo dentro de um cartao (o nome de uma forma, de um cargo, de uma pessoa).</summary>
	private static Label Titulo(string texto, Color? cor = null, int tamanho = 15)
	{
		var l = new Label { Text = texto, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		l.AddThemeFontSizeOverride("font_size", tamanho);
		l.AddThemeColorOverride("font_color", cor ?? Tema.Texto);
		return l;
	}

	/// <summary>Titulo e pilulas na mesma linha: o cabecalho de um cartao de item (forma, cargo, pessoa).</summary>
	private static HBoxContainer Cabecalho(string titulo, Color? cor, params (string Texto, Color Cor)[] pilulas)
	{
		var h = new HBoxContainer();
		h.AddThemeConstantOverride("separation", 8);
		Label t = Titulo(titulo, cor);
		t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		h.AddChild(t);
		HFlowContainer p = Pilulas(pilulas);
		p.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		h.AddChild(p);
		return h;
	}

	/// <summary>A NOTA: o texto apagado de explicacao, dentro de um pai. E o <see cref="Aviso"/> com endereco.</summary>
	private static Label Nota(string texto, Control pai)
	{
		var l = new Label { Text = texto, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		l.AddThemeColorOverride("font_color", Tema.TextoFraco);
		l.AddThemeFontSizeOverride("font_size", 12);
		pai.AddChild(l);
		return l;
	}

	/// <summary>
	/// O BOTAO COM A FRASE DO QUE ELE FAZ embaixo. O botao continua sendo um `Button` com o nome do
	/// verb no texto -- e assim que as bancadas o acham e e assim que a busca o lista; a frase e um
	/// rotulo apagado, e nao um tooltip, porque tooltip so aparece pra quem ja parou o mouse em cima.
	/// </summary>
	private static Control BotaoComDescricao(Button b, string descricao)
	{
		var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		v.AddThemeConstantOverride("separation", 1);
		b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		v.AddChild(b);
		if (descricao.Length > 0)
		{
			var l = new Label { Text = descricao, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			l.AddThemeFontSizeOverride("font_size", 11);
			l.AddThemeColorOverride("font_color", Tema.TextoFraco);
			v.AddChild(l);
		}
		return v;
	}

	/// <summary>A cor de "quanto disto esta bom": verde cheio, branco no meio, vermelho no fim.</summary>
	private static Color CorDaRazao(double razao, double bom = 0.66, double ruim = 0.33) =>
		razao >= bom ? Tema.Bom : razao <= ruim ? Tema.Perigo : Tema.Texto;
}
