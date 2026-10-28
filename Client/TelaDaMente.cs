using Godot;
using Jandirus.Core.Npc;

namespace Jandirus.Client;

/// <summary>
/// ============================ A LISTA DA MENTE (2026-10-27, o pedido 124c) ============================
/// Quem enfrentar na meditacao profunda, por categoria (voce, os habitantes, os jogadores e os chefes de saga), com a FOTO de cada um -- a
/// escolha do dono, *"como ele pediu, com uma lista com foto de cada um"*. Abre quando o servidor manda (`S2C.MenteOponentes`: ao entrar
/// na propria mente e depois de cada vitoria) e fecha na escolha. O botao manda `mente_oponente:&lt;tipo&gt;:&lt;id&gt;` pelo canal de
/// habilidade; quem decide tudo (se pode, quem nasce) e o servidor (`GameServer.MenteLista.cs`).
///
/// A FOTO e o mesmo retrato da aba People (`MenuJogo.Gente.Retrato`): um `CharacterVisual` numa moldura que recorta. Dos habitantes e dos
/// chefes ela vem do servidor (um corpo do molde); do jogador tambem (a cara de quando voce o viu, a mesma que veste a lembranca) -- e, em
/// ficha de antes da lista, da memoria deste cliente (`VistosDeGente`); e o reflexo e voce (`World.MeuLook`).
///
/// A FORMA DO CHEFE: com mais de uma forma vista, o servidor pergunta qual (`S2C.Escolhas` com o verbo `EtapasDaMente.Verbo`) e a pergunta
/// aparece AQUI, no lugar da lista -- antes ela so era desenhada na aba Learning do menu P (`MenuJogo.MenteChefe`), que ninguem abre no
/// meio da escolha.
///
/// Sem ESC: dentro da mente sem luta, a saida e o "Acordar" (o mesmo verbo do botao do alto da tela). O molde da caixa e o da
/// `TelaDeRefugio`.
/// </summary>
public partial class TelaDaMente : CanvasLayer
{
	public static TelaDaMente? Instancia { get; private set; }

	private const int Escala = 2;

	private Control _raiz = null!;
	private VBoxContainer _lista = null!;

	public override void _Ready()
	{
		Layer = 4;
		Instancia = this;

		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);
		var fundo = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.55f),
			AnchorRight = 1, AnchorBottom = 1,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
		};
		_raiz.AddChild(fundo);
		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);
		PanelContainer painel = Tema.Painel1(18);
		centro.AddChild(painel);
		var coluna = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
		coluna.AddThemeConstantOverride("separation", 8);
		painel.AddChild(coluna);

		var titulo = new Label { Text = "QUEM VOCÊ VAI ENFRENTAR?", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 20);
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		coluna.AddChild(titulo);
		var lead = new Label
		{
			Text = "Quem você já viu ou conheceu ganha forma na sua mente. A luta só acaba quando um dos dois cai: vencer paga BP pela força "
				 + "dele, e perder te acorda -- o foco abalado te deixa 30 min sem mergulhar.",
			AutowrapMode = TextServer.AutowrapMode.Word,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		lead.AddThemeColorOverride("font_color", Tema.TextoFraco);
		lead.AddThemeFontSizeOverride("font_size", 13);
		coluna.AddChild(lead);

		var rolagem = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(560, 400),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		coluna.AddChild(rolagem);
		_lista = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_lista.AddThemeConstantOverride("separation", 6);
		rolagem.AddChild(_lista);

		var acordar = new Button { Text = "Acordar (sair da mente)", Name = "Acordar" };
		acordar.Pressed += () => GameClient.Instance?.SendVerbo(SaidaDaMente.Verbo);
		coluna.AddChild(acordar);

		// METODO NOMEADO E NAO LAMBDA: assinatura com lambda nao da pra cancelar (ver `TelaDeRefugio`).
		if (GameClient.Instance is { } cli)
		{
			cli.OponentesDaMenteMudaram += AoMudar;
			cli.EscolhasChegaram += AoChegarEscolhas;
		}
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli)
		{
			cli.OponentesDaMenteMudaram -= AoMudar;
			cli.EscolhasChegaram -= AoChegarEscolhas;
		}
		if (Instancia == this) Instancia = null;
	}

	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	private void AoMudar(bool abrir)
	{
		if (!abrir) { _raiz.Visible = false; return; }
		Redesenhar();
		_raiz.Visible = true;
	}

	/// <summary>A PERGUNTA DA FORMA (o chefe com mais de uma forma vista): ela toma o lugar da lista. Lista vazia = o servidor limpou.</summary>
	private void AoChegarEscolhas(string verbo)
	{
		if (verbo != EtapasDaMente.Verbo || !SaidaDaMente.NaMente) return;
		if (GameClient.Instance?.Escolhas is not { Itens.Count: > 0 } e) return;
		DesenharEtapas(e.Itens);
		_raiz.Visible = true;
	}

	private void DesenharEtapas(List<string> itens)
	{
		Limpar();
		_lista.AddChild(Tema.Rotulo("Em qual forma?"));
		foreach (string item in itens)
		{
			string arg = item;
			var b = new Button { Text = EtapasDaMente.NomeDoItem(item), Alignment = HorizontalAlignment.Left };
			b.SetMeta("etapa_da_mente", arg);
			b.Pressed += () =>
			{
				GameClient.Instance?.SendVerbo(EtapasDaMente.Verbo, arg);
				_raiz.Visible = false;
			};
			_lista.AddChild(b);
		}
		var voltar = new Button { Text = "Voltar pra lista", Name = "Voltar" };
		voltar.Pressed += Redesenhar;
		_lista.AddChild(voltar);
	}

	private void Limpar()
	{
		foreach (Node f in _lista.GetChildren())
		{
			_lista.RemoveChild(f);   // ja fora da arvore: quem varre a lista logo depois nao ve o no velho (o `IsQueuedForDeletion`)
			f.QueueFree();
		}
	}

	private void Redesenhar()
	{
		Limpar();
		List<GameClient.OponenteDaMente> todos = GameClient.Instance?.OponentesDaMente ?? [];
		foreach (MenteOponentes.Tipo tipo in new[] { MenteOponentes.Tipo.Reflexo, MenteOponentes.Tipo.Habitante, MenteOponentes.Tipo.Jogador,
													 MenteOponentes.Tipo.Chefe })
		{
			List<GameClient.OponenteDaMente> desta = todos.Where(o => o.Tipo == (byte)tipo).ToList();
			if (desta.Count == 0) continue;
			_lista.AddChild(Tema.Rotulo(MenteOponentes.Categoria(tipo)));
			foreach (GameClient.OponenteDaMente o in desta) _lista.AddChild(Linha(o));
		}
	}

	private Control Linha(GameClient.OponenteDaMente o)
	{
		var linha = new HBoxContainer { Name = "Linha" };
		linha.AddThemeConstantOverride("separation", 10);
		linha.AddChild(Retrato(o));
		var nome = new Label
		{
			Text = o.Nome, Name = "Nome",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.Word,
		};
		linha.AddChild(nome);
		var lutar = new Button { Text = "Enfrentar", Name = "Enfrentar", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		string pedido = MenteOponentes.Pedido((MenteOponentes.Tipo)o.Tipo, o.Id);
		lutar.Pressed += () => Escolher(pedido);
		linha.AddChild(lutar);
		return linha;
	}

	private void Escolher(string pedido)
	{
		GameClient.Instance?.SendHabilidade(pedido);
		_raiz.Visible = false;
	}

	/// <summary>A foto: a do servidor (habitante, chefe, jogador visto), a da memoria (jogador de ficha antiga) ou a minha (o reflexo).</summary>
	private static (Jandirus.Core.Appearance.Appearance? Ap, string Raca, string Genero) Foto(GameClient.OponenteDaMente o)
	{
		if (o.Foto != null) return (o.Foto, o.Raca, o.Genero);
		if (o.Tipo == (byte)MenteOponentes.Tipo.Reflexo && World.Instancia?.MeuLook is { } eu) return (eu.Ap, eu.Raca, eu.Genero);
		if (o.Tipo == (byte)MenteOponentes.Tipo.Jogador && VistosDeGente.De(o.Nome) is { } v) return (v.Aparencia, v.Raca, v.Genero);
		return (null, o.Raca, o.Genero);
	}

	/// <summary>O retrato da aba People (`MenuJogo.Gente.Retrato`): a moldura recorta, o boneco olha pra frente.</summary>
	private static Control Retrato(GameClient.OponenteDaMente o)
	{
		int lado = 32 * Escala + 16;
		var moldura = new Panel
		{
			Name = "Moldura",
			CustomMinimumSize = new Vector2(lado, lado),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			ClipContents = true,
		};
		moldura.AddThemeStyleboxOverride("panel", Tema.Caixa(new Color("0e1017"), Tema.Borda, 6));
		(Jandirus.Core.Appearance.Appearance? ap, string raca, string genero) = Foto(o);
		if (ap != null && VistosDeGente.Catalogo is { } cat)
		{
			var boneco = new CharacterVisual
			{
				Name = "Boneco",
				Position = new Vector2(lado / 2f, lado / 2f + 2f * Escala),
				Scale = new Vector2(Escala, Escala),
				RelogioDoMundo = true,
			};
			moldura.AddChild(boneco);
			boneco.Vestir(cat, ap, raca, genero);
			boneco.SetMotion(Jandirus.Core.World.Facing.South, false);
		}
		else
		{
			var semFoto = new Label
			{
				Text = "sem foto", Name = "SemFoto",
				AnchorRight = 1, AnchorBottom = 1,
				HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
			};
			semFoto.AddThemeColorOverride("font_color", Tema.TextoFraco);
			semFoto.AddThemeFontSizeOverride("font_size", 11);
			moldura.AddChild(semFoto);
		}
		return moldura;
	}

	// ---- o robo de fotos (`--diagmentelista`) ----

	/// <summary>Os nomes na lista, por categoria, e quantos tem foto (o boneco).</summary>
	internal List<(string Nome, bool ComFoto)> LinhasDeTeste() =>
		[.. _lista.GetChildren().OfType<HBoxContainer>().Select(l =>
			(l.GetNode<Label>("Nome").Text, l.GetNode<Panel>("Moldura").GetNodeOrNull("Boneco") != null))];

	/// <summary>As formas na pergunta do chefe (vazia = a pergunta nao esta na tela).</summary>
	internal List<string> EtapasDeTeste() =>
		[.. _lista.GetChildren().OfType<Button>().Where(b => b.HasMeta("etapa_da_mente")).Select(b => b.Text)];

	/// <summary>Aperta a forma com este nome, pelo mesmo sinal do clique.</summary>
	internal bool EscolherEtapaDeTeste(string nome)
	{
		Button? b = _lista.GetChildren().OfType<Button>().FirstOrDefault(x => x.HasMeta("etapa_da_mente") && x.Text == nome);
		if (b == null) return false;
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	/// <summary>Aperta o "Enfrentar" da linha com este nome, pelo mesmo sinal do clique.</summary>
	internal bool EnfrentarDeTeste(string nome)
	{
		HBoxContainer? linha = _lista.GetChildren().OfType<HBoxContainer>().FirstOrDefault(l => l.GetNode<Label>("Nome").Text == nome);
		if (linha == null) return false;
		linha.GetNode<Button>("Enfrentar").EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}
}
