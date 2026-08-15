using Godot;
using Jandirus.Core.Items;

namespace Jandirus.Client;

/// <summary>
/// A TECLA I: a mochila.
///
/// ============================ UMA GRADE, E NAO UMA LISTA ============================
/// O original mostra o inventario como linhas de texto numa aba HTML ("Maca x3", com botoes ao
/// lado). Funciona e e feio -- e pior, e lento de ler: achar uma coisa entre trinta linhas de texto
/// exige LER trinta linhas.
///
/// O dono pediu grade com icone: "vai ter slots e cada item vai ficar em um slot (o icone de cada
/// um la) se tiver mais de um item igual vai ficar a foto do item com um numero logo embaixo
/// mostrando a quantidade". Reconhecer um desenho e instantaneo; ler nao e.
/// ====================================================================================
///
/// ============================ ELA NAO DECIDE NADA ============================
/// As acoes de cada item vem do <see cref="CatalogoDeItens"/>, que o servidor le tambem, e cada
/// botao manda um verbo `item_<acao>`. Quem confere se voce TEM o item e se a acao PERTENCE a ele
/// e o servidor -- esconder um botao e conveniencia, nao permissao.
/// =============================================================================
/// </summary>
public partial class TelaDeInventario : CanvasLayer
{
	/// <summary>Quantas colunas a grade tem. Seis por cinco fecham os 30 slots do original.</summary>
	private const int Colunas = 6;

	/// <summary>O lado de cada slot, em pixels. O sprite tem 32; o dobro se le sem esforco.</summary>
	private const int LadoDoSlot = 64;

	private Control _raiz = null!;
	private GridContainer _grade = null!;
	private Label _contagem = null!;

	/// <summary>O painel de acoes do item clicado. Nulo = nenhum item aberto.</summary>
	private PanelContainer? _acoes;

	public override void _Ready()
	{
		Layer = 4;   // a mesma do menu de interacao: sao as duas telas "do mundo"
		Montar();

		if (GameClient.Instance is { } cli)
			cli.MochilaMudou += AoMudarMochila;
	}

	/// <summary>Solta a assinatura -- ver a nota no `MenuJogo._Ready`: o `GameClient` sobrevive ao logout.</summary>
	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.MochilaMudou -= AoMudarMochila;
	}

	private void AoMudarMochila() { if (_raiz.Visible) Redesenhar(); }

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(16);
		centro.AddChild(painel);

		var caixa = new VBoxContainer();
		caixa.AddThemeConstantOverride("separation", 8);
		painel.AddChild(caixa);

		var titulo = new Label { Text = "MOCHILA", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(titulo);

		_contagem = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_contagem.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_contagem.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(_contagem);
		caixa.AddChild(new HSeparator());

		_grade = new GridContainer { Columns = Colunas };
		_grade.AddThemeConstantOverride("h_separation", 6);
		_grade.AddThemeConstantOverride("v_separation", 6);
		caixa.AddChild(_grade);

		// O TEXTO LE O REGISTRO: quem religou a mochila pra outra tecla tem que ler a tecla DELE
		// aqui, senao o botao ensina errado justamente quem mudou.
		var fechar = new Button { Text = $"Fechar ({Teclas.NomeDaAcao("ui_mochila")} ou Esc)" };
		fechar.Pressed += Fechar;
		caixa.AddChild(fechar);
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		// O "I" E UMA DAS LETRAS QUE O EMBATE SORTEIA -- ver `Foco.AtalhosMudos`. Sem esta troca,
		// responder ao quick time event abria a mochila por cima dele.
		if (Foco.AtalhosMudos) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		if (_raiz.Visible && k.Keycode == Key.Escape)
		{
			Fechar();
			GetViewport().SetInputAsHandled();
			return;
		}

		// PELO REGISTRO -- ver a mesma linha em `MenuDeInteracao` e o cabecalho do `Teclas`.
		if (!Teclas.Bate("ui_mochila", k)) return;

		if (_raiz.Visible) Fechar(); else Abrir();
		GetViewport().SetInputAsHandled();
	}

	private void Abrir() { _raiz.Visible = true; Redesenhar(); }

	private void Fechar()
	{
		FecharAcoes();
		FecharTeclado();
		_raiz.Visible = false;
	}

	private void Redesenhar()
	{
		FecharAcoes();
		foreach (Node n in _grade.GetChildren()) n.QueueFree();

		Inventario inv = GameClient.Instance?.Mochila ?? new Inventario();
		_contagem.Text = $"{inv.Ocupados} de {Inventario.Slots} espaços";

		// A GRADE TEM SEMPRE OS 30 SLOTS, cheios ou nao. Desenhar so o que existe faria a caixa
		// mudar de tamanho a cada maca colhida, e a tela inteira dancaria embaixo do mouse.
		for (int i = 0; i < Inventario.Slots; i++)
		{
			if (i < inv.Pilhas.Count) _grade.AddChild(SlotCheio(inv.Pilhas[i]));
			else _grade.AddChild(SlotVazio());
		}
	}

	private static Control SlotVazio()
	{
		var p = new PanelContainer { CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot) };
		p.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.Borda, 4));
		return p;
	}

	/// <summary>
	/// UM SLOT COM COISA: o icone grande, e a quantidade no canto de baixo.
	///
	/// O NUMERO SO APARECE COM MAIS DE UM. Um "1" em cada slot seria ruido em toda a grade, e a
	/// pergunta que ele responde ("quantos?") so existe quando ha mais de um.
	/// </summary>
	private Control SlotCheio(Pilha pilha)
	{
		ItemDef? def = CatalogoDeItens.Get(pilha.Id);

		var botao = new Button
		{
			CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot),
			TooltipText = def == null ? pilha.Id : $"{def.Nome}\n{def.Descricao}",
			ExpandIcon = true,
			IconAlignment = HorizontalAlignment.Center,
		};

		Texture2D? icone = def == null ? null : Miniatura(def);
		if (icone != null) botao.Icon = icone;
		else botao.Text = def?.Nome[..Math.Min(4, def.Nome.Length)] ?? "?";

		botao.Pressed += () => AbrirAcoes(pilha, botao);

		if (pilha.Quantidade <= 1) return botao;

		// O NUMERO POR CIMA do botao, e nao ao lado: ele pertence ao slot, e um `Label` irmao
		// empurraria a grade inteira.
		var pilhaVisual = new Control { CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot) };
		pilhaVisual.AddChild(botao);
		botao.AnchorRight = 1;
		botao.AnchorBottom = 1;

		var n = new Label
		{
			Text = pilha.Quantidade.ToString(),
			AnchorLeft = 0, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1,
			OffsetTop = -18, OffsetRight = -4,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		n.AddThemeFontSizeOverride("font_size", 14);
		n.AddThemeColorOverride("font_color", Tema.Destaque);
		// CONTORNO PRETO: o numero cai em cima do sprite, e sprite claro comeria um texto claro.
		n.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
		n.AddThemeConstantOverride("outline_size", 4);
		pilhaVisual.AddChild(n);

		return pilhaVisual;
	}

	/// <summary>O primeiro quadro do sprite do item, virado textura pro botao.</summary>
	private static Texture2D? Miniatura(ItemDef def)
	{
		var f = ResourceLoader.Load<SpriteFrames>(def.Arte);
		if (f == null) return null;

		if (def.Estado.Length > 0 && f.HasAnimation(def.Estado) && f.GetFrameCount(def.Estado) > 0)
			return f.GetFrameTexture(def.Estado, 0);

		foreach (StringName anim in f.GetAnimationNames())
			if (f.GetFrameCount(anim) > 0) return f.GetFrameTexture(anim, 0);

		return null;
	}

	/// <summary>
	/// AS OPCOES DO ITEM, num painel flutuante ao lado do slot.
	///
	/// FLUTUANTE e nao uma coluna fixa: ele nasce onde o dedo esta, que e onde o olho ja esta
	/// olhando. Um painel no canto da tela obrigaria a atravessar a grade inteira com a vista
	/// depois de cada clique.
	/// </summary>
	private void AbrirAcoes(Pilha pilha, Control perto)
	{
		FecharAcoes();
		ItemDef? def = CatalogoDeItens.Get(pilha.Id);
		if (def == null) return;

		_acoes = Tema.Painel1(8);
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0) };
		caixa.AddThemeConstantOverride("separation", 4);
		_acoes.AddChild(caixa);

		var nome = new Label { Text = def.Nome, HorizontalAlignment = HorizontalAlignment.Center };
		caixa.AddChild(nome);
		caixa.AddChild(new HSeparator());

		// "LARGAR" ENTRA AQUI e nao no catalogo: ela vale pra todo item, e repeti-la em cada linha
		// seria uma chance por item de esquecer. Ver `ItemDef.AcoesDoItem`.
		foreach (string acao in def.AcoesDoItem.Append("largar"))
		{
			string a = acao;
			var b = new Button { Text = Rotulo(a) };
			b.Pressed += () =>
			{
				// AS ACOES QUE PEDEM UM NUMERO abrem o mesmo teclado do menu de interacao. O valor
				// viaja colado no id ("Weights/40") porque o canal de verbos tem um argumento so --
				// ver `GameServer.ComandoDeItem`.
				if (a == "ajustar") { AbrirTeclado(def); return; }

				// POSICIONAR NAO MANDA VERBO: ela poe a construcao na MAO e fecha o inventario. O
				// verbo sai depois, quando o jogador clicar no chao -- ver `TelaDeConstrucao`.
				if (a == "posicionar")
				{
					Fechar();
					TelaDeConstrucao.Instancia?.Segurar(def.Id);
					return;
				}

				GameClient.Instance?.SendVerbo($"item_{a}", def.Id);
				FecharAcoes();
			};
			caixa.AddChild(b);
		}

		_raiz.AddChild(_acoes);
		_acoes.GlobalPosition = perto.GlobalPosition + new Vector2(LadoDoSlot + 6, 0);
	}

	/// <summary>
	/// O TECLADO PRA QUEM PEDE UM NUMERO -- hoje so os pesos, que se ajustam em porcentagem do
	/// proprio corpo. Ver `TecladoNumerico`, que e o mesmo widget do menu de interacao.
	/// </summary>
	private CenterContainer? _teclado;

	private void AbrirTeclado(ItemDef def)
	{
		FecharAcoes();
		FecharTeclado();

		var t = new TecladoNumerico($"{def.Nome} — % do corpo", 0, 100,
			v =>
			{
				GameClient.Instance?.SendVerbo("item_ajustar", $"{def.Id}/{v:0}");
				FecharTeclado();
			},
			FecharTeclado);

		_teclado = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_teclado.AddChild(t);
		_raiz.AddChild(_teclado);
	}

	private void FecharTeclado()
	{
		_teclado?.QueueFree();
		_teclado = null;
	}

	private static string Rotulo(string acao) => acao switch
	{
		"comer" => "Comer",
		"equipar" => "Equipar / tirar",
		"ajustar" => "Ajustar o peso",
		"tirar" => "Tirar os pesos",
		"usar" => "Usar",
		"cavar" => "Cavar aqui",
		"posicionar" => "Assentar no chão",
		"largar" => "Jogar fora",
		_ => char.ToUpperInvariant(acao[0]) + acao[1..],
	};

	private void FecharAcoes()
	{
		if (_acoes == null) return;
		_acoes.QueueFree();
		_acoes = null;
	}
}
