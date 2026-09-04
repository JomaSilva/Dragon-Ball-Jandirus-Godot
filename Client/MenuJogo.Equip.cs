using Godot;
using Jandirus.Core.Items;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA EQUIP -- o que esta vestido, ligado e carregado: scouter, pesos, aparelhos e roupas, e as
/// escolhas de combate. Ate 2026-09-03 ela era um AVISO ("vem com o sistema de itens") com a mochila
/// ja portada atras dela (`GameClient.Mochila`, `Protocol.Poder.Scouter`, `AtributosState.PesoMult`).
///
/// ============================ O QUE ELA E, E O QUE ELA NAO E ============================
/// O `ui_tab_equip` do original (`HtmlUI.dm:284-300`) tinha duas secoes: COMBAT (damage, penetration,
/// accuracy, deflection, attack delay) e ACCESSORIES (o que estava `equipped`). Aqui:
///
///   * o cliente NAO TEM dano, penetracao, precisao nem deflexao -- eles nao viajam na ficha, e a
///     tela nao inventa numero que nao recebeu. Da secao de combate sobra o que o cliente sabe:
///     a cadencia do soco (`SocoMs`), o golpe letal e a guarda (bits do `Estado`);
///   * "equipado" no port e ESTAR COM O ITEM (ver o comentario do Traje em `CatalogoDeItens`): a
///     roupa espacial protege por estar na mochila, o Nav System abre a aba por estar na mochila.
///     A unica coisa que liga e desliga de verdade e o scouter (`GameServer.Equipar`), e os pesos
///     tem quantidade (`PesoMult`, o `weight` do `WeightTick`).
///
/// A MOCHILA INTEIRA continua na tecla I (`TelaDeInventario`); esta aba e o resumo do que VALE agora,
/// e os dois botoes dela mandam os MESMOS verbos que a mochila manda (`item_equipar`, `item_tirar`).
/// ======================================================================================
/// </summary>
public partial class MenuJogo
{
	/// <summary>Os aparelhos e roupas que a aba lista, na ordem: os cinco que mudam o jogo por estar na mochila.</summary>
	private static readonly string[] AparelhosDaAbaEquip =
	[
		CatalogoDeItens.NavSystem, CatalogoDeItens.Radar, CatalogoDeItens.Traje,
		CatalogoDeItens.Respirador, CatalogoDeItens.BrincosPotara,
	];

	private void AbaEquip(SheetState f)
	{
		Inventario mochila = GameClient.Instance?.Mochila ?? new Inventario();

		GridContainer grade = Colunas();
		CartaoDoScouter(mochila, grade);
		CartaoDosPesos(mochila, grade);
		CartaoDosAparelhos(mochila, grade);
		CartaoDeCombate(f, grade);
	}

	// =====================================================================
	// SCOUTER -- LIGADO / DESLIGADO / nao tem, e o botao do verbo de producao
	// =====================================================================
	/// <summary>
	/// O SCOUTER: a pilula de estado e o botao que manda o MESMO verbo da mochila (`item_equipar`,
	/// que no servidor e o `Equipar` de `GameServer.Mochila.cs` -- ele ALTERNA o bit `Poder.Scouter`,
	/// e por isso o mesmo botao liga e desliga). O cartao acende em laranja quando ligado: e o estado
	/// que muda o menu inteiro (o BP vira numero na Stats, a aba Sense vira Scan).
	///
	/// O BOTAO EXISTE SEMPRE e fica APAGADO sem o item: um botao que some nao ensina o que o cartao
	/// faz, e a descricao embaixo dele diz o que falta.
	/// </summary>
	private void CartaoDoScouter(Inventario mochila, Control pai)
	{
		bool tem = mochila.Quantos(CatalogoDeItens.Scouter) > 0;
		bool ligado = _atributos.Tem(Protocol.Poder.Scouter);
		VBoxContainer c = Cartao("Scouter", pai, destaque: ligado);

		(string Texto, Color Cor) estado = ligado ? ("LIGADO", Tema.Bom)
			: tem ? ("DESLIGADO", Tema.TextoFraco)
			: ("não tem", Tema.TextoFraco);
		c.AddChild(CabecalhoDeItem(CatalogoDeItens.Scouter, estado));

		var b = new Button { Text = ligado ? "Desligar o scouter" : "Ligar o scouter", Disabled = !tem };
		b.Pressed += () => GameClient.Instance?.SendVerbo("item_equipar", CatalogoDeItens.Scouter);
		c.AddChild(BotaoComDescricao(b, !tem
			? "precisa de um scouter na mochila (tecla I)"
			: ligado ? "os números voltam a ser \"???\""
			: "liga com um bipe: o poder de quem você olha vira número"));

		Nota("Ligado, o Battle Power da aba Stats vira número e a aba Scan lê o poder de quem você olha. Ele acorda desligado a cada relog.", c);
	}

	// =====================================================================
	// PESOS -- quanto rendem, e "Tirar"
	// =====================================================================
	/// <summary>
	/// OS PESOS: quanto rendem (`PesoMult`, ate `GainKnobs.WeightGainMax`) numa barra, e "Tirar" quando
	/// vestidos -- o `item_tirar` da mochila. O AJUSTE fica na mochila (tecla I): ele pede um numero
	/// pelo teclado numerico, e esta pagina e refeita a cada mudanca de assinatura -- um teclado aberto
	/// aqui morreria embaixo do dedo. A nota diz onde ajustar.
	/// </summary>
	private void CartaoDosPesos(Inventario mochila, Control pai)
	{
		bool tem = mochila.Quantos(CatalogoDeItens.Pesos) > 0;
		double mult = _atributos.PesoMult;
		bool vestidos = mult > 1.001;
		VBoxContainer c = Cartao("Pesos", pai);

		c.AddChild(CabecalhoDeItem(CatalogoDeItens.Pesos, vestidos ? ("VESTIDOS", Tema.Destaque)
			: tem ? ("guardados", Tema.TextoFraco)
			: ("não tem", Tema.TextoFraco)));

		// A BARRA VAI DE 1x (sem peso) AO TETO DO SISTEMA: e a escala do `WeightTick`, lida do Core, e
		// nao uma regra refeita aqui -- o numero vem pronto do servidor.
		double teto = Jandirus.Core.Stats.GainKnobs.WeightGainMax;
		LinhaComBarra("Ganho no treino", $"{mult:0.##}x", (mult - 1) / Math.Max(teto - 1, 1e-9),
			Tema.Destaque, c, vestidos ? Tema.Bom : Tema.Texto);

		if (vestidos)
		{
			var b = new Button { Text = "Tirar os pesos" };
			b.Pressed += () => GameClient.Instance?.SendVerbo("item_tirar", CatalogoDeItens.Pesos);
			c.AddChild(BotaoComDescricao(b, "o ganho volta a 1x e o corpo para de ranger"));
		}

		Nota(tem
			? "Ajuste quanto vestir pela mochila (tecla I): cada passo pesa, e em troca o treino rende mais."
			: "Sem pesos. Eles se fabricam na aba Tech e se ajustam pela mochila (tecla I).", c);
	}

	// =====================================================================
	// APARELHOS E ROUPAS -- estar na mochila E estar equipado
	// =====================================================================
	/// <summary>
	/// APARELHOS E ROUPAS: cinco linhas com icone, nome do catalogo e a pilula "na mochila" / "—".
	/// Estar na mochila E estar equipado (ver o cabecalho). O Nav System ganha a segunda pilula
	/// "aba Nav" quando o bit `Poder.Nav` esta aceso -- e o SERVIDOR que o acende olhando a mochila
	/// (`GameServer.Sigilo`), e a pilula so repete o que ele disse.
	/// </summary>
	private void CartaoDosAparelhos(Inventario mochila, Control pai)
	{
		VBoxContainer c = Cartao("Aparelhos e roupas", pai);

		foreach (string id in AparelhosDaAbaEquip)
		{
			ItemDef? def = CatalogoDeItens.Get(id);
			if (def == null) continue;
			bool tem = mochila.Quantos(id) > 0;

			var h = new HBoxContainer { TooltipText = def.Descricao };
			h.AddThemeConstantOverride("separation", 8);
			// A IDENTIDADE VAI EM METADADO (o id do item), e nao no `Name`: e por ele que a bancada
			// acha a linha de cada aparelho e compara a pilula com a mochila.
			h.SetMeta("aparelho", id);
			h.AddChild(IconeDoItem(def, 22));

			var nome = new Label { Text = def.Nome, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
			nome.AddThemeFontSizeOverride("font_size", 13);
			nome.AddThemeColorOverride("font_color", tem ? Tema.Texto : Tema.TextoFraco);
			h.AddChild(nome);

			var pilulas = new List<(string Texto, Color Cor)> { tem ? ("na mochila", Tema.Bom) : ("—", Tema.TextoFraco) };
			if (id == CatalogoDeItens.NavSystem && _atributos.Tem(Protocol.Poder.Nav)) pilulas.Add(("aba Nav", Tema.Destaque));
			HFlowContainer p = Pilulas([.. pilulas]);
			p.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
			h.AddChild(p);

			c.AddChild(h);
		}

		Nota("Basta estar com você: a roupa e o respirador valem no vácuo, o Nav System abre a aba Nav. Radar e brincos se usam pela mochila (tecla I).", c);
	}

	// =====================================================================
	// COMBATE -- o que o cliente SABE das escolhas de luta
	// =====================================================================
	/// <summary>
	/// COMBATE: a cadencia do soco e as duas escolhas em pilula. Dano, penetracao, precisao e deflexao
	/// do `ui_tab_equip` original ficaram no servidor (nao viajam na ficha), e por isso NAO estao aqui:
	/// a tela nao inventa numero.
	/// </summary>
	private void CartaoDeCombate(SheetState f, Control pai)
	{
		VBoxContainer c = Cartao("Combate", pai);

		Linha("Cadência do soco", $"{f.SocoMs} ms", null, c);
		LinhaComPilula("Golpe", f.Letal ? "LETAL" : "não-letal", f.Letal ? Tema.Perigo : Tema.Texto, c);
		LinhaComPilula("Guarda", f.Guarda ? "ERGUIDA" : "baixa", f.Guarda ? Tema.Bom : Tema.TextoFraco, c);

		// AS TECLAS SAEM DO REGISTRO (`Teclas`), e nao escritas a mao: quem religou o letal pro J tem
		// que ler J aqui, senao a nota ensina errado justamente quem mudou.
		Nota($"Letal alterna com {Teclas.NomeDaAcao("lethal")}. A guarda é {Teclas.NomeDaAcao("guard")}: apara com braço ou perna, e na hora certa vira contra-ataque.", c);
	}

	// =====================================================================
	// AS PECAS DE ITEM -- deveriam subir pra MenuJogo.Pecas.cs no dia em que outra aba mostrar item
	// =====================================================================
	/// <summary>
	/// O CABECALHO DE UM CARTAO DE ITEM: o icone do item (o mesmo sprite da mochila), o nome do
	/// catalogo e a pilula de estado, na mesma linha.
	/// </summary>
	private static HBoxContainer CabecalhoDeItem(string id, (string Texto, Color Cor) estado)
	{
		ItemDef? def = CatalogoDeItens.Get(id);
		var h = new HBoxContainer();
		h.AddThemeConstantOverride("separation", 10);
		if (def != null) h.AddChild(IconeDoItem(def, LadoDoIcone));
		HBoxContainer cab = Cabecalho(def?.Nome ?? id, null, estado);
		cab.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		h.AddChild(cab);
		return h;
	}

	/// <summary>Os sprites dos itens, carregados uma vez: a pagina e remontada a cada mudanca de assinatura.</summary>
	private static readonly Dictionary<string, Texture2D?> _miniaturasDeItem = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// O ICONE DE UM ITEM: o primeiro quadro do sprite dele, como o slot da mochila desenha
	/// (`TelaDeInventario.Miniatura`). COPIADO, e nao chamado: aquele e privado da tela da mochila,
	/// e as duas telas tem que continuar desenhando o MESMO quadro. Filtro `Nearest` porque e pixel
	/// art -- esticado com filtro linear o scouter vira uma mancha.
	/// </summary>
	private static TextureRect IconeDoItem(ItemDef def, int lado)
	{
		if (!_miniaturasDeItem.TryGetValue(def.Id, out Texture2D? tex))
		{
			tex = MiniaturaDoItem(def);
			_miniaturasDeItem[def.Id] = tex;
		}
		return new TextureRect
		{
			Texture = tex,
			CustomMinimumSize = new Vector2(lado, lado),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			Name = "Icone",
		};
	}

	private static Texture2D? MiniaturaDoItem(ItemDef def)
	{
		if (!ResourceLoader.Exists(def.Arte)) return null;
		var f = ResourceLoader.Load<SpriteFrames>(def.Arte);
		if (f == null) return null;

		if (def.Estado.Length > 0 && f.HasAnimation(def.Estado) && f.GetFrameCount(def.Estado) > 0)
			return f.GetFrameTexture(def.Estado, 0);

		foreach (StringName anim in f.GetAnimationNames())
			if (f.GetFrameCount(anim) > 0) return f.GetFrameTexture(anim, 0);

		return null;
	}

	/// <summary>
	/// Ver `MenuJogo.Assinatura`. A basica so tem o `comum` (busca, arvore aberta e o `Estado` -- que
	/// ja carrega letal e guarda). Entra aqui o que ESTA aba desenha: os bits de poder (scouter e Nav),
	/// o multiplicador dos pesos, a cadencia, a mochila (id e quantidade de cada pilha: sao os itens que
	/// decidem cada pilula e o botao apagado) e as teclas da nota de combate.
	/// </summary>
	private string ExtraDaAssinaturaDeEquip(SheetState f)
	{
		GameClient? c = GameClient.Instance;
		string mochila = c == null ? "" : string.Join(',', c.Mochila.Pilhas.Select(p => $"{p.Id}x{p.Quantidade}"));
		return $"{_atributos.Poderes}|{_atributos.PesoMult:0.##}|{f.SocoMs}|{mochila}"
			 + $"|{Teclas.NomeDaAcao("lethal")}|{Teclas.NomeDaAcao("guard")}";
	}
}
