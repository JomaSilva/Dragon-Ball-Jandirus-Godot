using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-23) A TELA DO KAI KAI -- o `input() in list(...)` do `kai.dm:117`, virado tela. O dono: *"faça o kai kai abrir uma tela com
/// todos os planetas conhecidos por jogadores e ao clicar ele teleporta pra eles no mesmo estilo do IT"*.
///
/// ELA NAO DECIDE NADA. Abre quando o servidor manda a lista (`S2C.DestinosDoKaiKai`, a resposta ao verbo apertado sem destino -- o
/// botao do verbo ou a tecla); cada linha tem a arte do planeta, o nome que se le (o batismo, se houver) e o botao "Teletransportar"
/// (o mesmo texto do Instant Transmission na aba Sense), que manda `Kai_Kai:chave`. Quem confere a porta, o Ki cheio e se o destino
/// ainda esta na lista e o servidor (`GameServer.KaiKai.cs`); a resposta vem no chat. A tela fecha no clique, como o `input()` do DM.
///
/// DUAS PARTES: os doze de sempre numa grade de duas colunas (cabem sem rolar numa tela de 720) e, embaixo, os mundos gerados que
/// algum jogador ja pisou, um por linha, com a gravidade (que e por que se vai a um mundo gerado). No meio da tela, por cima do jogo,
/// como a janela da estatua: e um gesto que se faz parado. Nenhum botao pega o foco do teclado (ENTER e a tecla de falar).
/// </summary>
public partial class TelaDoKaiKai : CanvasLayer
{
	/// <summary>A tela viva, ou nula antes do mundo. E por aqui que o `GameClient` e o robo a alcancam.</summary>
	public static TelaDoKaiKai? Instancia { get; private set; }

	/// <summary>DEFEITO INJETADO (robo `--diagkaikai`): a lista chega e a tela NAO abre. Lido por <see cref="Abrir"/>. Falso em jogo, sempre.</summary>
	public static bool NaoAbreDeTeste;

	/// <summary>DEFEITO INJETADO (robo `--diagkaikai`): o botao manda o verbo SEM o destino -- o servidor so manda a lista de novo e
	/// ninguem viaja. Lido pelo clique (<see cref="Ir"/>). Falso em jogo, sempre.</summary>
	public static bool BotaoSemDestinoDeTeste;

	/// <summary>DEFEITO INJETADO (robo `--diagkaikai`): as linhas saem sem a arte do planeta. Falso em jogo, sempre.</summary>
	public static bool SemArteDeTeste;

	private const int Largura = 780;
	private const int Margem = 36;
	private const int LadoDaArte = 40;

	/// <summary>O meta que cada botao de destino carrega: a chave que ele manda. O robo acha o botao certo por ele.</summary>
	public const string MetaDoDestino = "kaikai";

	private Control _raiz = null!;
	private VBoxContainer _conteudo = null!;
	private readonly List<DestinoDoKaiKai> _destinos = [];
	private readonly Dictionary<string, (Button? Ir, TextureRect Arte, Label Nome)> _porChave = new(StringComparer.Ordinal);

	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>A lista que esta (ou estaria) na tela -- o robo le por aqui.</summary>
	public IReadOnlyList<DestinoDoKaiKai> Destinos => _destinos;

	/// <summary>O botao "Teletransportar" de um destino (nulo = o destino nao esta na tela, ou e onde se esta).</summary>
	public Button? BotaoDe(string chave) => _porChave.TryGetValue(chave, out var l) ? l.Ir : null;

	/// <summary>A arte desenhada na linha de um destino (nula = sem arte, ou o destino nao esta la).</summary>
	public Texture2D? ArteDe(string chave) => _porChave.TryGetValue(chave, out var l) ? l.Arte.Texture : null;

	/// <summary>O nome escrito na linha de um destino (vazio = nao esta la).</summary>
	public string NomeDe(string chave) => _porChave.TryGetValue(chave, out var l) ? l.Nome.Text : "";

	/// <summary>O que o ultimo botao apertado mandou ao servidor -- o robo confere a chave por aqui.</summary>
	public string UltimoPedido { get; private set; } = "";

	/// <summary>Quantas listas o servidor ja mandou (abrindo a tela ou nao) -- o robo sabe por aqui que a resposta chegou.</summary>
	public int Recebidas { get; private set; }

	public override void _Ready()
	{
		Layer = 5;
		Instancia = this;
		Montar();
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
	}

	// =====================================================================
	// ABRIR E FECHAR
	// =====================================================================
	/// <summary>A lista do servidor chegou: desenha e abre. O menu do P sai da frente (os dois moram no meio da tela, e quem apertou o
	/// verbo la dentro ja fez o que tinha pra fazer nele).</summary>
	public void Abrir(List<DestinoDoKaiKai> lista)
	{
		_destinos.Clear();
		_destinos.AddRange(lista);
		Recebidas++;
		Desenhar();
		if (NaoAbreDeTeste) return;
		if (MenuJogo.NaFrenteDoJogo) MenuJogo.Instancia?.Fechar();
		_raiz.Visible = true;
	}

	public void Fechar()
	{
		if (NaTela) _raiz.Visible = false;
	}

	/// <summary>ESC fecha -- o "Nevermind" do menu do DM.</summary>
	public override void _Input(InputEvent evento)
	{
		if (!NaTela) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.Escape) return;
		Fechar();
		GetViewport().SetInputAsHandled();
	}

	/// <summary>O CLIQUE: a tela fecha e o pedido vai. O resto (a porta, o Ki, se o destino ainda esta la) e do servidor.</summary>
	private void Ir(DestinoDoKaiKai d)
	{
		string pedido = BotaoSemDestinoDeTeste ? "Kai_Kai" : "Kai_Kai:" + d.Chave;
		UltimoPedido = pedido;
		Fechar();
		GameClient.Instance?.SendHabilidade(pedido);
	}

	// =====================================================================
	// A ARTE
	// =====================================================================
	/// <summary>
	/// O QUADRO DA FOLHA `Misc/Planets.tres` de cada destino. Os doze sao os icones do `obj/Planets` do DM (`Planets.dm:121-173`), pela
	/// mesma escolha da carta (<see cref="ArteDePlaneta.EstadoDoPreFeito"/>), com tres que a carta nao tem: o Desert usa a arte de Vampa
	/// (`planetState = "Vampa"` com `planetType = "Desert"`, `Planets.dm:141-144`), as duas estacoes tem a delas, e o Outro Mundo -- que
	/// o DM alcanca "via Interdimension" (`Planets.dm:26`) -- a da Interdimension. O mundo gerado usa o icone do BIOMA, a mesma escolha
	/// da carta e do disco no espaco (`ArteDePlaneta.EstadoDoBioma`), tirado da seed que a chave traz.
	/// </summary>
	public static string EstadoDaArte(DestinoDoKaiKai d)
	{
		if (d.Gerado)
			return ChaveDePlaneta.Ler(d.Chave, d.Nome, out ChaveDePlaneta c) && !c.PreFeito
				? ArteDePlaneta.EstadoDoBioma(GeradorDeTerreno.BiomaDaSeed(c.Seed))
				: "jungle";
		return d.Chave switch
		{
			"Desert" => "vampa",
			"Large_Space_Station" => "large_space_station",
			"Small_Space_Station" => "small_space_station",
			"Afterlife" => "interdimension",
			_ => ArteDePlaneta.EstadoDoPreFeito(d.Chave),
		};
	}

	/// <summary>A linha de baixo do mundo gerado: a gravidade, que sai da seed (a mesma conta do servidor, `MundoProcedural`).</summary>
	private static string DetalheDoGerado(DestinoDoKaiKai d) =>
		ChaveDePlaneta.Ler(d.Chave, d.Nome, out ChaveDePlaneta c) && !c.PreFeito
			? $"mundo gerado · gravidade {MundoProcedural.GravidadeDaSeed(c.Seed):0.##}x"
			: "mundo gerado";

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	private static Label Texto(string texto, int tamanho, Color cor)
	{
		var l = new Label { Text = texto };
		l.AddThemeFontSizeOverride("font_size", tamanho);
		l.AddThemeColorOverride("font_color", cor);
		return l;
	}

	private void Montar()
	{
		// A RAIZ NAO PEGA O MOUSE: fora do painel, a tela e do jogo.
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		// NO MEIO, com a altura da tela (ancoras, e nao pixels: se refaz sozinha num resize). Fundo OPACO: o painel fica por cima do chat.
		var painel = new PanelContainer { Name = "Painel", MouseFilter = Control.MouseFilterEnum.Stop };
		painel.AddThemeStyleboxOverride("panel", Tema.Caixa(new Color(Tema.Painel, 1f), Tema.Borda, 14));
		painel.AnchorLeft = 0.5f;
		painel.AnchorRight = 0.5f;
		painel.AnchorTop = 0;
		painel.AnchorBottom = 1;
		painel.OffsetLeft = -Largura / 2f;
		painel.OffsetRight = Largura / 2f;
		painel.OffsetTop = Margem;
		painel.OffsetBottom = -Margem;
		_raiz.AddChild(painel);

		var tudo = new VBoxContainer();
		tudo.AddThemeConstantOverride("separation", 6);
		painel.AddChild(tudo);

		Label titulo = Texto("KAI KAI", 22, Tema.Destaque);
		titulo.HorizontalAlignment = HorizontalAlignment.Center;
		tudo.AddChild(titulo);
		Label sub = Texto("Escolha o planeta: você aparece lá na hora, com quem estiver colado em você. Leva todo o seu Ki.", 13, Tema.TextoFraco);
		sub.HorizontalAlignment = HorizontalAlignment.Center;
		sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		tudo.AddChild(sub);
		tudo.AddChild(new HSeparator());

		var rolagem = new ScrollContainer
		{
			Name = "Rolagem",
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		tudo.AddChild(rolagem);
		_conteudo = new VBoxContainer { Name = "Destinos", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_conteudo.AddThemeConstantOverride("separation", 6);
		rolagem.AddChild(_conteudo);

		tudo.AddChild(new HSeparator());
		var pe = new HBoxContainer();
		pe.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
		var fechar = new Button { Text = "Fechar", Name = "Fechar", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(140, 0) };
		fechar.Pressed += Fechar;
		pe.AddChild(fechar);
		tudo.AddChild(pe);
	}

	/// <summary>
	/// REFAZ AS LINHAS com a lista de agora. Os nos velhos SAEM da arvore na hora (e nao so no fim do quadro, como o `QueueFree` faria):
	/// uma porta de robo que varre a arvore logo depois de um redesenho veria a tela velha junto da nova.
	/// </summary>
	private void Desenhar()
	{
		foreach (Node n in _conteudo.GetChildren())
		{
			_conteudo.RemoveChild(n);
			n.QueueFree();
		}
		_porChave.Clear();

		_conteudo.AddChild(Tema.Rotulo("os doze de sempre"));
		var grade = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		grade.AddThemeConstantOverride("h_separation", 8);
		grade.AddThemeConstantOverride("v_separation", 6);
		_conteudo.AddChild(grade);
		foreach (DestinoDoKaiKai d in _destinos.Where(d => !d.Gerado)) grade.AddChild(Linha(d));

		_conteudo.AddChild(Tema.Rotulo("mundos que algum jogador já pisou"));
		List<DestinoDoKaiKai> gerados = [.. _destinos.Where(d => d.Gerado)];
		if (gerados.Count == 0)
			_conteudo.AddChild(Texto("Nenhum mundo gerado foi pisado ainda.", 13, Tema.TextoFraco));
		foreach (DestinoDoKaiKai d in gerados) _conteudo.AddChild(Linha(d));
	}

	/// <summary>UMA LINHA: a arte, o nome (e, no gerado, a gravidade), e o botao -- ou "você está aqui", no lugar onde se esta.</summary>
	private PanelContainer Linha(DestinoDoKaiKai d)
	{
		var cartao = new PanelContainer { Name = "Destino_" + d.Chave.Replace('#', 'g'), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		cartao.AddThemeStyleboxOverride("panel", d.Aqui ? Tema.Caixa(Tema.PainelAceso, Tema.BordaViva, 6) : Tema.Caixa(Tema.PainelClaro, Tema.Borda, 6));
		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 8);
		cartao.AddChild(linha);

		var arte = new TextureRect
		{
			Name = "Arte",
			Texture = SemArteDeTeste ? null : ArteDePlaneta.Quadro(EstadoDaArte(d)),
			CustomMinimumSize = new Vector2(LadoDaArte, LadoDaArte),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		linha.AddChild(arte);

		var textos = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		textos.AddThemeConstantOverride("separation", 0);
		Label nome = Texto(d.Nome, 15, Tema.Texto);
		nome.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		textos.AddChild(nome);
		if (d.Gerado) textos.AddChild(Texto(DetalheDoGerado(d), 11, Tema.TextoFraco));
		linha.AddChild(textos);

		Button? ir = null;
		if (d.Aqui)
		{
			Label aqui = Texto("você está aqui", 12, Tema.Bom);
			aqui.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
			linha.AddChild(aqui);
		}
		else
		{
			ir = new Button { Text = "Teletransportar", Name = "Ir", FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
			ir.SetMeta(MetaDoDestino, d.Chave);
			DestinoDoKaiKai alvo = d;
			ir.Pressed += () => Ir(alvo);
			linha.AddChild(ir);
		}

		_porChave[d.Chave] = (ir, arte, nome);
		return cartao;
	}
}
