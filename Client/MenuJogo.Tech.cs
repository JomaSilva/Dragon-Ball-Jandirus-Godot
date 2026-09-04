using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Core.Tech;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA TECH -- nivel tecnologico, zeni, o catalogo de construcoes e as construcoes da zona.
///
/// Ate 2026-09-03 ela era um AVISO ("vem com o sistema de tecnologia") com o sistema ja portado
/// atras dela (`GameClient.TechNivel/Zeni/Catalogo/Obras`, `SendTech`). O original tinha uma aba
/// magra (`ui_tab_tech`, HtmlUI.dm:623: zenni, "tem bancada por perto?" e o botao de construir a
/// bancada); o que este port tem a mais e o CATALOGO com o motivo de cada nao, que ate aqui so
/// aparecia na tela da mochila.
///
/// ============================ O CLIENTE DESENHA O QUE RECEBE ============================
/// Nivel, xp, zeni e cada oferta (com a recusa ja decidida) vem do servidor no `S2C.Tech`. A aba
/// nao recalcula "cabe no bolso?": o `Recusa` de cada oferta E a resposta do servidor, e e ela que
/// acende ou apaga o botao. O catalogo so chega quando o cliente pede (`SendTech("lista")`), e esta
/// aba pede UMA vez -- ver <see cref="_pediCatalogo"/>.
///
/// O BOTAO "Construir" MANDA `SendTech("construir", id)` -- e desde 2026-09-03 ESTA E A UNICA
/// GRADE DE FABRICAR DO JOGO: a da bancada de pesquisa (o "Fabricar..." da tecla E) foi apagada a
/// pedido do dono, porque duas grades pra mesma coisa e a segunda porta que este repo mais paga.
/// O que sai da fabrica vai pra MOCHILA (regra 1 do dono, ver `GameServer.Construir`); instalar no
/// chao continua sendo pela mochila (tecla I -> "Instalar no chão" -> `TelaDeConstrucao.Segurar`),
/// porque instalar pede um LUGAR, e lugar se escolhe com o mouse no mundo, nao num menu.
/// ====================================================================================
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// JA PEDI O CATALOGO NESTA SESSAO? O mesmo desenho do `_pediContas` da aba Admin: o pedido sai
	/// UMA vez, e nao a cada remontagem. Se o servidor devolvesse uma lista VAZIA (um personagem cuja
	/// raca nao pode erguer nada), pedir dentro do desenho viraria um pedido a cada pacote de ficha --
	/// cinco por segundo, pra sempre. O botao "pedir de novo" continua sendo a saida manual.
	/// </summary>
	private bool _pediCatalogo;

	/// <summary>O lado do icone de uma oferta: o primeiro quadro do sprite, do tamanho de um tile.</summary>
	private const int LadoDoIconeDaObra = 36;

	private void AbaTech()
	{
		if (GameClient.Instance is not { } cli) { AindaNao("Tech"); return; }

		// ---- o numero da aba: o nivel, com xp e zeni na legenda
		double alvo = Math.Max(cli.TechXpAlvo, 1e-9);
		Faixa("Tecnologia", $"nível {cli.TechNivel:0.#}",
			  $"{cli.TechXp:0} / {cli.TechXpAlvo:0} xp  ·  {cli.Zeni:N0} zeni");

		// ---- estudo
		VBoxContainer estudo = Cartao("Estudo");
		LinhaComBarra("Experiência", $"{cli.TechXp:0} / {cli.TechXpAlvo:0}", cli.TechXp / alvo, Tema.Destaque, estudo);
		Nota("Study, na bancada de pesquisa, é o único jeito de ganhar tecnologia. Cada nível abre construções novas no catálogo.", estudo);

		CatalogoDeObrasNaAba(cli);
		ObrasDaZona(cli);
	}

	// =====================================================================
	// O CATALOGO
	// =====================================================================
	/// <summary>
	/// O CATALOGO EM CARDS, DOIS POR LINHA -- a grade das arvores da Learning. Cada card e uma oferta:
	/// icone, nome, custo e tecnologia, a pilula com "pode construir" ou o motivo do nao, e o botao.
	///
	/// A GRADE FICA NO NIVEL DA PAGINA, sob um rotulo de secao, e nao dentro de um cartao: card dentro
	/// de cartao e chapa clara sobre chapa clara -- o card sumiria. E o mesmo desenho de "SUAS ÁRVORES".
	/// </summary>
	private void CatalogoDeObrasNaAba(GameClient cli)
	{
		if (cli.Catalogo.Count == 0)
		{
			VBoxContainer vazio = Cartao("Catálogo de construções");
			Nota(_pediCatalogo ? "o servidor não ofereceu nada pra você ainda." : "pedindo o catálogo ao servidor…", vazio);
			if (!_pediCatalogo) { _pediCatalogo = true; cli.SendTech("lista"); }
			var pedir = new Button { Text = "pedir de novo", Alignment = HorizontalAlignment.Left };
			pedir.Pressed += () => cli.SendTech("lista");
			vazio.AddChild(pedir);
			return;
		}

		Secao($"Catálogo de construções  ({cli.Catalogo.Count})");
		Aviso("O que você fabrica vai pra mochila (tecla I); instalar no chão é de lá. As possíveis vêm primeiro.");
		GridContainer grade = Colunas();
		foreach (GameClient.OfertaDeObra o in OfertasOrdenadas(cli.Catalogo)) grade.AddChild(CartaoDeOferta(cli, o));
	}

	/// <summary>
	/// AS POSSIVEIS PRIMEIRO, depois por tecnologia e custo crescentes (a ordem do servidor), e o nome
	/// como desempate -- pra mesma lista dar sempre a mesma grade.
	/// </summary>
	private static IEnumerable<GameClient.OfertaDeObra> OfertasOrdenadas(IEnumerable<GameClient.OfertaDeObra> catalogo) =>
		catalogo.OrderBy(o => o.Recusa == (int)RecusaObra.Pode ? 0 : 1)
				.ThenBy(o => o.Tech).ThenBy(o => o.Custo)
				.ThenBy(o => o.Nome, StringComparer.OrdinalIgnoreCase);

	/// <summary>O motivo do servidor, em portugues. Nunca o nome do enum: `RecusaObra` e do Core, nao da tela.</summary>
	private static string TextoDaRecusa(RecusaObra r) => r switch
	{
		RecusaObra.Pode => "pode construir",
		RecusaObra.SemTech => "falta tecnologia",
		RecusaObra.SemZeni => "falta zeni",
		RecusaObra.RacaErrada => "não é da sua raça",
		RecusaObra.LugarOcupado => "lugar ocupado",
		RecusaObra.ZonaProibida => "não aqui",
		_ => "?",
	};

	/// <summary>Verde pro que da, vermelho pro que falta (tecnologia, zeni), apagado pro resto.</summary>
	private static Color CorDaRecusa(RecusaObra r) => r switch
	{
		RecusaObra.Pode => Tema.Bom,
		RecusaObra.SemTech or RecusaObra.SemZeni => Tema.Perigo,
		_ => Tema.TextoFraco,
	};

	/// <summary>
	/// UM CARD DE OFERTA. A borda laranja marca o que da pra construir AGORA -- a mesma marca do card de
	/// skill compravel da Learning. O clique em "Construir" NAO fabrica na hora: ele abre a pergunta
	/// "Fabricar por N zeni?" no lugar da pilula, como a tela da mochila faz -- e zeni de verdade, e um
	/// clique perdido nao pode custar meio milhao.
	/// </summary>
	private Control CartaoDeOferta(GameClient cli, GameClient.OfertaDeObra o)
	{
		var recusa = (RecusaObra)o.Recusa;
		bool pode = recusa == RecusaObra.Pode;

		var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		card.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, pode ? Tema.BordaViva : Tema.Borda, 8));
		// A IDENTIDADE EM METADADO, e nao no `Name` (o Godot renomeia irmaos homonimos): a bancada
		// conta um card por oferta e le a recusa de cada um por aqui.
		card.SetMeta("cartao", "oferta");
		card.SetMeta("titulo", o.Nome);
		card.SetMeta("id", o.Id);
		card.SetMeta("recusa", o.Recusa);
		// A DESCRICAO DO CATALOGO vira tooltip: o card e curto de proposito, e a frase inteira so
		// interessa a quem parou o mouse em cima.
		string desc = Jandirus.Core.Items.CatalogoDeItens.Get(o.Id)?.Descricao ?? "";
		if (desc.Length > 0) card.TooltipText = desc;

		var h = new HBoxContainer();
		h.AddThemeConstantOverride("separation", 8);

		// ---- o icone: o mesmo carregador da mochila (primeiro quadro do sprite), ou um vulto
		Texture2D? arte = Miniaturas.De(o.Arte, o.Estado);
		h.AddChild(arte != null
			? new TextureRect
			{
				Texture = arte,
				CustomMinimumSize = new Vector2(LadoDoIconeDaObra, LadoDoIconeDaObra),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			}
			: VultoDaObra(o.Nome));

		// ---- nome, preco, pilula e botao
		var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		v.AddThemeConstantOverride("separation", 2);
		v.AddChild(Titulo(o.Nome, pode ? Tema.Texto : Tema.TextoFraco, 14));
		Nota($"custo {o.Custo:N0} zeni  ·  tech {o.Tech:0.#}", v);

		var rodape = new HBoxContainer();
		rodape.AddThemeConstantOverride("separation", 6);
		HFlowContainer pilula = Pilulas((TextoDaRecusa(recusa), CorDaRecusa(recusa)));
		pilula.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		pilula.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		rodape.AddChild(pilula);

		// APAGADO QUANDO O SERVIDOR RECUSARIA. Nao e a guarda de verdade (o servidor confere de novo em
		// `Construir`); e o que impede o botao de prometer o que a pilula ao lado ja disse que nao da.
		var construir = new Button
		{
			Text = "Construir",
			Disabled = !pode,
			TooltipText = pode
				? $"fabrica {o.Nome} por {o.Custo:N0} zeni. Vai pra sua mochila (tecla I)."
				: "o servidor recusaria: " + TextoDaRecusa(recusa),
		};
		construir.AddThemeFontSizeOverride("font_size", 12);
		rodape.AddChild(construir);
		v.AddChild(rodape);

		// ---- a pergunta, escondida ate o clique (sempre na arvore: mesma ficha, mesmos nodes)
		var confirma = new HBoxContainer { Visible = false };
		confirma.AddThemeConstantOverride("separation", 6);
		var pergunta = new Label
		{
			Text = $"Fabricar por {o.Custo:N0} zeni?",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			VerticalAlignment = VerticalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		pergunta.AddThemeFontSizeOverride("font_size", 12);
		confirma.AddChild(pergunta);
		var sim = new Button { Text = "Fabricar" };
		sim.AddThemeFontSizeOverride("font_size", 12);
		var nao = new Button { Text = "Cancelar" };
		nao.AddThemeFontSizeOverride("font_size", 12);
		confirma.AddChild(sim);
		confirma.AddChild(nao);
		v.AddChild(confirma);

		// COPIA LOCAL do id: o clique leva o id DESTE card, e o `cli` -- nada que prenda o menu.
		string id = o.Id;
		construir.Pressed += () => { rodape.Visible = false; confirma.Visible = true; };
		nao.Pressed += () => { confirma.Visible = false; rodape.Visible = true; };
		sim.Pressed += () =>
		{
			// O PACOTE DE FABRICAR (`ComandoDeTech "construir"`). O servidor responde no chat, cobra o
			// zeni, poe na mochila e reenvia o catalogo -- e e o catalogo novo que remonta esta pagina.
			cli.SendTech("construir", id);
			confirma.Visible = false;
			rodape.Visible = true;
		};

		h.AddChild(v);
		card.AddChild(h);
		return card;
	}

	/// <summary>Sem arte, um quadrado apagado com a inicial: feio e honesto, como o vulto da mochila.</summary>
	private static Control VultoDaObra(string nome)
	{
		var p = new PanelContainer
		{
			CustomMinimumSize = new Vector2(LadoDoIconeDaObra, LadoDoIconeDaObra),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		p.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelApagado, Tema.BordaApagada, 0));
		var l = new Label
		{
			Text = nome.Length > 0 ? nome[..1].ToUpperInvariant() : "?",
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		l.AddThemeColorOverride("font_color", Tema.TextoFraco);
		p.AddChild(l);
		return p;
	}

	// =====================================================================
	// AS CONSTRUCOES DA ZONA
	// =====================================================================
	/// <summary>
	/// O QUE ESTA DE PE NESTA ZONA. A lista `Obras` e da ZONA, nao do jogador (o `S2C.Construcoes`
	/// leva tudo o que o cliente precisa desenhar: bancadas, banco, mobilia), entao o cartao diz isso
	/// no titulo em vez de fingir que e "suas obras" -- e marca as suas com a pilula.
	///
	/// A posicao e a CELULA (a mesma conta do servidor, `CatalogoDeObras.Celula`), e nao o pixel: e
	/// a unidade em que se anda ate uma coisa.
	/// </summary>
	private void ObrasDaZona(GameClient cli)
	{
		VBoxContainer corpo = Cartao($"Construções nesta zona  ({cli.Obras.Count})");
		if (cli.Obras.Count == 0)
		{
			Nota("nenhuma construção nesta zona.", corpo);
			return;
		}

		int minhas = 0;
		foreach (GameClient.ObraInfo o in cli.Obras.OrderBy(x => x.Nome, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id))
		{
			bool minha = o.Dono.Length > 0 && string.Equals(o.Dono, cli.LocalName, StringComparison.OrdinalIgnoreCase);
			if (minha) minhas++;
			corpo.AddChild(LinhaDeObra(o, minha));
		}
		Nota((minhas > 0 ? $"{minhas} sua(s). " : "Nenhuma é sua. ")
			+ "Sem aparafusar (Bolt, ao lado dela) uma construção não funciona.", corpo);
	}

	/// <summary>
	/// Nome a esquerda, a celula a direita, e as pilulas ("sua", "aparafusada"/"solta"). Carrega o
	/// metadado `linha` com o nome, como toda linha rotulo/valor: `ValorDesenhado("Tech", nome)` le a
	/// celula.
	/// </summary>
	private static HBoxContainer LinhaDeObra(GameClient.ObraInfo o, bool minha)
	{
		var h = new HBoxContainer();
		h.AddThemeConstantOverride("separation", 8);
		h.SetMeta("linha", o.Nome);

		var nome = new Label { Text = o.Nome, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		nome.AddThemeFontSizeOverride("font_size", 13);
		nome.AddThemeColorOverride("font_color", Tema.Texto);
		h.AddChild(nome);

		(int cx, int cy) = CatalogoDeObras.Celula(o.Pos.X, o.Pos.Y);
		var onde = new Label { Text = $"célula {cx}, {cy}" };
		onde.AddThemeFontSizeOverride("font_size", 12);
		onde.AddThemeColorOverride("font_color", Tema.TextoFraco);
		h.AddChild(onde);

		HFlowContainer pilulas = Pilulas(
			(minha ? "sua" : "", Tema.Bom),
			(o.Aparafusada ? "aparafusada" : "solta", o.Aparafusada ? Tema.Bom : Tema.Destaque));
		pilulas.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		h.AddChild(pilulas);
		return h;
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`). A basica cobre nivel,
	/// zeni, xp e as CONTAGENS de obras e ofertas. O que esta aba desenha alem disso:
	///
	///   * a RECUSA de cada oferta (ela muda quando o zeni muda, sem a contagem mudar) -- um caractere
	///     por oferta, na mesma ordem do catalogo;
	///   * o xp do proximo nivel (a barra e a razao dele);
	///   * se o catalogo JA FOI PEDIDO: a nota "pedindo..." vira "não ofereceu nada" na remontagem
	///     seguinte, e sem este pedaco a pagina vazia ficaria com a frase velha pra sempre;
	///   * o "aparafusada/solta" de cada obra, que o Bolt troca sem mudar a contagem.
	/// </summary>
	private string ExtraDaAssinaturaDeTech(SheetState f)
	{
		if (GameClient.Instance is not { } c) return "";
		return string.Concat(c.Catalogo.Select(o => (char)('0' + Math.Clamp(o.Recusa, 0, 9))))
			 + $"|{c.TechXpAlvo:0}|{_pediCatalogo}|"
			 + string.Join(',', c.Obras.Select(o => $"{o.Id}{(o.Aparafusada ? '+' : '-')}"));
	}
}
