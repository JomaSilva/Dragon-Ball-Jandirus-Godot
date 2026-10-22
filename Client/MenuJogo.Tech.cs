using Godot;
using Jandirus.Core.Economia;
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
/// Nivel, xp e cada oferta (com a recusa do servidor) vem no `S2C.Tech`; o catalogo so chega quando o
/// cliente pede (`SendTech("lista")`), e esta aba pede UMA vez -- ver <see cref="_pediCatalogo"/>.
///
/// O QUE O SERVIDOR JULGOU, FICA JULGADO: raca e tecnologia de cada oferta sao o `Recusa` que veio no
/// pacote. A ULTIMA pergunta -- "cabe no bolso?" -- e refeita aqui com a CARTEIRA de agora
/// (`S2C.Carteira`: zeni e recursos), pela mesma funcao do Core que o servidor usa ao cobrar
/// (<see cref="GameClient.VereditoDe"/>). Ela era a resposta congelada do pacote enquanto o zeni so mudava
/// por comando; quem MINERA ganha recurso a cada segundo, e reenviar cem ofertas por segundo pra acender um
/// botao seria pagar o catalogo inteiro por um numero.
///
/// ============================ DUAS MOEDAS, DOIS BOTOES (dono, 2026-10-10) ============================
/// *"a capacidade de construir tecnologias sem precisar de zenni com um material chamado resource [...] se
/// algo custo 1000 zennis pra fazer vc pode fazer com 2000 resources"*. Cada card mostra os DOIS precos e tem
/// um botao pra cada um: "Construir" paga em zeni (o de sempre) e "Com recursos" paga o dobro em recursos. A
/// escolha e do clique -- nao ha modo escondido pra lembrar -- e a pergunta de confirmar diz o preco na moeda
/// escolhida. O servidor cobra SO nela (`construir` / `construir_recursos`).
///
/// O BOTAO "Construir" MANDA `SendTech("construir", id)` -- e desde 2026-09-03 ESTA E A UNICA
/// GRADE DE FABRICAR DO JOGO: a da bancada de pesquisa (o "Fabricar..." da tecla E) foi apagada a
/// pedido do dono, porque duas grades pra mesma coisa e a segunda porta que este repo mais paga.
/// O que sai da fabrica vai pra MOCHILA (regra 1 do dono, ver `GameServer.Construir`); instalar no
/// chao continua sendo pela mochila (tecla I -> "Instalar no chão" -> `TelaDeConstrucao.Segurar`),
/// porque instalar pede um LUGAR, e lugar se escolhe com o mouse no mundo, nao num menu.
/// ====================================================================================
///
/// (2026-10-21) A BUSCA DO CATALOGO (a caixa "procurar construção...", pelo nome e pela descricao) mora em `MenuJogo.BuscaDeTech.cs`.
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

		// ---- o numero da aba: o nivel, com xp e a carteira (zeni e recursos) na legenda
		Label legenda = PartesDaFaixa(Faixa("Tecnologia", $"nível {cli.TechNivel:0.#}", LegendaDaTech(cli))).Legenda;

		// ---- estudo
		VBoxContainer estudo = Cartao("Estudo");
		LinhaViva xp = LinhaComBarra("Experiência", XpDaTech(cli), RazaoDoXp(cli), Tema.Destaque, estudo);
		Nota("Study, na bancada de pesquisa, é o único jeito de ganhar tecnologia. Cada nível abre construções novas no catálogo.", estudo);

		// A LEGENDA E A BARRA SAO VIVAS (2026-10-19): a carteira chega a cada segundo de mineracao, e reescrever dois
		// textos no lugar e o que deixa a aba aberta sem remontar os cem cards do catalogo a cada pazada (ver `Viva`).
		Viva(() =>
		{
			if (GameClient.Instance is not { } c) return;
			legenda.Text = LegendaDaTech(c);
			xp.Escrever(XpDaTech(c), RazaoDoXp(c), Tema.Destaque);
		});

		CatalogoDeObrasNaAba(cli);
		ObrasDaZona(cli);
	}

	/// <summary>A legenda da faixa: o xp e as duas carteiras. Uma conta so pra montagem e pra viva.</summary>
	private static string LegendaDaTech(GameClient c) =>
		$"{c.TechXp:0} / {c.TechXpAlvo:0} xp  ·  {c.Zeni:N0} zeni  ·  {c.Recursos:N0} recursos";

	/// <summary>O valor da linha "Experiência" e a razao da barra dela.</summary>
	private static string XpDaTech(GameClient c) => $"{c.TechXp:0} / {c.TechXpAlvo:0}";

	private static double RazaoDoXp(GameClient c) => c.TechXp / Math.Max(c.TechXpAlvo, 1e-9);

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

		// (2026-10-21) O TITULO COM A CAIXA DE BUSCA LOGO ABAIXO -- ver `MenuJogo.BuscaDeTech.cs`
		CabecalhoDaBuscaDaTech(cli.Catalogo.Count);
		Aviso("O que você fabrica vai pra mochila (tecla I); instalar no chão é de lá. Cada coisa se paga em zeni "
			+ "ou em recursos (o dobro) -- recursos se tiram dos planetas, com o verb Minerar. As possíveis vêm primeiro.");
		GridContainer grade = Colunas();
		foreach (GameClient.OfertaDeObra o in OfertasOrdenadas(cli)) grade.AddChild(CartaoDeOferta(cli, o));
		// A PAGINA NASCE JA FILTRADA: ela e refeita quando o catalogo ou a carteira chegam, com a busca escrita na caixa.
		if (!BuscaDaTechEsquecidaNaRemontagemDeTeste) FiltrarOCatalogo();
	}

	/// <summary>
	/// AS POSSIVEIS PRIMEIRO -- as que ALGUMA das duas carteiras paga --, depois por tecnologia e custo
	/// crescentes (a ordem do servidor), e o nome como desempate, pra mesma lista dar sempre a mesma grade.
	/// </summary>
	private static IEnumerable<GameClient.OfertaDeObra> OfertasOrdenadas(GameClient cli) =>
		cli.Catalogo.OrderBy(o => DaPraFabricar(cli, o) ? 0 : 1)
					.ThenBy(o => o.Tech).ThenBy(o => o.Custo)
					.ThenBy(o => o.Nome, StringComparer.OrdinalIgnoreCase);

	/// <summary>Alguma das duas carteiras paga esta oferta agora? E o que acende a borda do card e o poe na frente.</summary>
	private static bool DaPraFabricar(GameClient cli, GameClient.OfertaDeObra o) =>
		cli.VereditoDe(o, Moeda.Zeni) == RecusaObra.Pode || cli.VereditoDe(o, Moeda.Recursos) == RecusaObra.Pode;

	/// <summary>O motivo do servidor, em portugues. Nunca o nome do enum: `RecusaObra` e do Core, nao da tela.</summary>
	private static string TextoDaRecusa(RecusaObra r) => r switch
	{
		RecusaObra.Pode => "pode construir",
		RecusaObra.SemTech => "falta tecnologia",
		RecusaObra.SemZeni => "falta zeni",
		RecusaObra.SemRecursos => "faltam recursos",
		RecusaObra.RacaErrada => "não é da sua raça",
		RecusaObra.LugarOcupado => "lugar ocupado",
		RecusaObra.ZonaProibida => "não aqui",
		// (v0.2.1) a oferta fechada -- os itens de magia, que pedem um sistema que este jogo ainda nao tem (`TechFechada`)
		RecusaObra.ForaDoPort => "ainda não existe neste jogo",
		_ => "?",
	};

	/// <summary>Verde pro que da, vermelho pro que falta (tecnologia, zeni, recursos), apagado pro resto.</summary>
	private static Color CorDaRecusa(RecusaObra r) => r switch
	{
		RecusaObra.Pode => Tema.Bom,
		RecusaObra.SemTech or RecusaObra.SemZeni or RecusaObra.SemRecursos => Tema.Perigo,
		_ => Tema.TextoFraco,
	};

	/// <summary>O que a pilula diz quando NENHUMA das duas carteiras paga (e so a carteira e o problema).</summary>
	private const string TextoSemNenhumaCarteira = "falta zeni e recursos";

	/// <summary>
	/// A PILULA DE UM CARD, das duas respostas (zeni e recursos): "pode construir" se QUALQUER das duas paga --
	/// quem diz qual e o botao aceso --; se so a carteira falta, falta nas duas; e o resto (tecnologia, raca) e
	/// igual pras duas moedas, entao vale a resposta em zeni.
	/// </summary>
	private static (string Texto, Color Cor) PilulaDaOferta(RecusaObra emZeni, RecusaObra emRecursos)
	{
		if (emZeni == RecusaObra.Pode || emRecursos == RecusaObra.Pode) return (TextoDaRecusa(RecusaObra.Pode), Tema.Bom);
		if (emZeni == RecusaObra.SemZeni && emRecursos == RecusaObra.SemRecursos) return (TextoSemNenhumaCarteira, Tema.Perigo);
		return (TextoDaRecusa(emZeni), CorDaRecusa(emZeni));
	}

	/// <summary>
	/// UM CARD DE OFERTA. A borda laranja marca o que da pra construir AGORA -- a mesma marca do card de
	/// skill compravel da Learning. O clique em "Construir" (zeni) ou em "Com recursos" NAO fabrica na hora:
	/// ele abre a pergunta "Fabricar por N zeni?" / "...por N recursos?" no lugar da pilula, como a tela da
	/// mochila faz -- e dinheiro de verdade, e um clique perdido nao pode custar meio milhao.
	/// </summary>
	private Control CartaoDeOferta(GameClient cli, GameClient.OfertaDeObra o)
	{
		// AS DUAS RESPOSTAS, com a carteira de agora -- ver `GameClient.VereditoDe`.
		RecusaObra emZeni = cli.VereditoDe(o, Moeda.Zeni), emRecursos = cli.VereditoDe(o, Moeda.Recursos);
		bool podeZeni = emZeni == RecusaObra.Pode, podeRecursos = emRecursos == RecusaObra.Pode;
		bool pode = podeZeni || podeRecursos;
		double custoEmRecursos = Recurso.CustoEmRecursos(o.Custo);

		var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		card.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, pode ? Tema.BordaViva : Tema.Borda, 8));
		// A IDENTIDADE EM METADADO, e nao no `Name` (o Godot renomeia irmaos homonimos): a bancada
		// conta um card por oferta e le as duas recusas de cada um por aqui.
		card.SetMeta("cartao", "oferta");
		card.SetMeta("titulo", o.Nome);
		card.SetMeta("id", o.Id);
		card.SetMeta("recusa", (int)emZeni);
		card.SetMeta("recusa_recursos", (int)emRecursos);
		// A DESCRICAO DO CATALOGO vira tooltip: o card e curto de proposito, e a frase inteira so
		// interessa a quem parou o mouse em cima.
		string desc = Jandirus.Core.Items.CatalogoDeItens.Get(o.Id)?.Descricao ?? "";
		if (desc.Length > 0) card.TooltipText = desc;
		// (2026-10-21) ...e e nela, com o nome, que a busca do catalogo procura (`MenuJogo.BuscaDeTech.cs`).
		NaBuscaDaTech(card, o.Nome, desc);

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

		// ---- nome, os dois precos, pilula e os dois botoes
		var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		v.AddThemeConstantOverride("separation", 2);
		v.AddChild(Titulo(o.Nome, pode ? Tema.Texto : Tema.TextoFraco, 14));
		// O METADADO `preco` leva o texto: e a linha que a bancada le pra cobrar "os DOIS precos, e o segundo
		// e o dobro".
		Nota($"{o.Custo:N0} zeni  ou  {custoEmRecursos:N0} recursos  ·  tech {o.Tech:0.#}", v).SetMeta("preco", o.Id);

		// O RODAPE QUEBRA LINHA quando o card e estreito: a pilula mais larga ("falta zeni e recursos") com os
		// dois botoes passa da metade da pagina, e uma fileira rigida empurraria a grade pra fora da tela.
		var rodape = new HFlowContainer();
		rodape.AddThemeConstantOverride("h_separation", 6);
		rodape.AddThemeConstantOverride("v_separation", 3);
		(string textoDaPilula, Color corDaPilula) = PilulaDaOferta(emZeni, emRecursos);
		HFlowContainer pilula = Pilulas((textoDaPilula, corDaPilula));
		pilula.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		pilula.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		rodape.AddChild(pilula);

		// APAGADO QUANDO O SERVIDOR RECUSARIA. Nao e a guarda de verdade (o servidor confere de novo em
		// `Construir`, na moeda do botao); e o que impede o botao de prometer o que a pilula ao lado ja disse
		// que nao da.
		var construir = new Button
		{
			Text = "Construir",
			Disabled = !podeZeni,
			TooltipText = podeZeni
				? $"fabrica {o.Nome} por {o.Custo:N0} zeni. Vai pra sua mochila (tecla I)."
				: "o servidor recusaria: " + TextoDaRecusa(emZeni),
		};
		construir.AddThemeFontSizeOverride("font_size", 12);
		rodape.AddChild(construir);

		// O MESMO FABRICAR, PAGO EM RECURSOS -- o dobro do preco. Botao proprio, sempre ao lado do outro: os dois
		// precos estao na linha de cima, e a escolha e o clique.
		var comRecursos = new Button
		{
			Text = "Com recursos",
			Disabled = !podeRecursos,
			TooltipText = podeRecursos
				? $"fabrica {o.Nome} por {custoEmRecursos:N0} recursos (o dobro do preço em zeni). Vai pra sua mochila (tecla I)."
				: "o servidor recusaria: " + TextoDaRecusa(emRecursos),
		};
		comRecursos.AddThemeFontSizeOverride("font_size", 12);
		rodape.AddChild(comRecursos);
		v.AddChild(rodape);

		// ---- a pergunta, escondida ate o clique (sempre na arvore: mesma ficha, mesmos nodes). O texto e o
		// preco na moeda do botao apertado -- escrito na hora do clique.
		var confirma = new HBoxContainer { Visible = false };
		confirma.AddThemeConstantOverride("separation", 6);
		var pergunta = new Label
		{
			Text = "",
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
		// A MOEDA E A DO BOTAO APERTADO, e vive so entre o clique e a confirmacao deste card.
		Moeda escolhida = Moeda.Zeni;
		void Perguntar(Moeda moeda)
		{
			escolhida = moeda;
			pergunta.Text = moeda == Moeda.Recursos
				? $"Fabricar por {custoEmRecursos:N0} recursos?"
				: $"Fabricar por {o.Custo:N0} zeni?";
			rodape.Visible = false;
			confirma.Visible = true;
		}
		construir.Pressed += () => Perguntar(Moeda.Zeni);
		comRecursos.Pressed += () => Perguntar(Moeda.Recursos);
		nao.Pressed += () => { confirma.Visible = false; rodape.Visible = true; };
		sim.Pressed += () =>
		{
			// O PACOTE DE FABRICAR (`ComandoDeTech "construir"` / `"construir_recursos"`). O servidor responde
			// no chat, cobra NA MOEDA DO COMANDO, poe na mochila e reenvia o catalogo e a carteira -- e sao eles
			// que remontam esta pagina.
			cli.SendTech(escolhida == Moeda.Recursos ? "construir_recursos" : "construir", id);
			// A ARMADURA ABRE A TELA DA ARTE (dono, 2026-10-12) -- quando a mochila confirmar que ela foi fabricada,
			// e nao no clique: um pedido recusado nao tem o que moldar. Ver `TelaDeArmadura.EsperarAFabricada`.
			if (id == Jandirus.Core.Items.ArmaduraDeTech.Id) TelaDeArmadura.Instancia?.EsperarAFabricada();
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
	/// <summary>O titulo do cartao, sem a contagem. Uma casa so: a bancada (`--diagabas`) procura por ele.</summary>
	internal const string TituloDasMinhasObras = "Suas construções nesta zona";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagabas`): o cartao volta a listar TUDO o que esta de pe na zona -- o estado
	/// de antes de 2026-10-12, com as macieiras do mapa e a bancada dos outros no meio. Falso em jogo, sempre.
	/// </summary>
	internal static bool ObrasDeTodosNaAbaDeTeste;

	/// <summary>
	/// AS MINHAS, de pe nesta zona, na ordem em que a aba as lista. A lista `Obras` do cliente e da ZONA (o
	/// `S2C.Construcoes` leva tudo o que ha pra desenhar: bancadas dos outros, banco, macieira do mapa), e o
	/// crivo do dono e o NOME -- o mesmo campo que o servidor manda em cada obra.
	/// </summary>
	internal static List<GameClient.ObraInfo> MinhasObrasNaZona(GameClient cli) =>
	[
		.. cli.Obras
			.Where(o => ObrasDeTodosNaAbaDeTeste
						|| (o.Dono.Length > 0 && string.Equals(o.Dono, cli.LocalName, StringComparison.OrdinalIgnoreCase)))
			.OrderBy(x => x.Nome, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id),
	];

	/// <summary>
	/// O QUE EU TENHO DE PE NESTA ZONA -- e so o que e meu (dono, 2026-10-12: *"so deve aparecer construçoes minhas
	/// e nao todas"*). O cartao listava a zona inteira: quatro macieiras do mapa e seis bancadas alheias, com o
	/// rodape "Nenhuma é sua".
	///
	/// A posicao e a CELULA (a mesma conta do servidor, `CatalogoDeObras.Celula`), e nao o pixel: e
	/// a unidade em que se anda ate uma coisa.
	/// </summary>
	private void ObrasDaZona(GameClient cli)
	{
		List<GameClient.ObraInfo> minhas = MinhasObrasNaZona(cli);
		VBoxContainer corpo = Cartao($"{TituloDasMinhasObras}  ({minhas.Count})");
		if (minhas.Count == 0)
		{
			Nota("você não tem nenhuma construção nesta zona.", corpo);
			return;
		}

		foreach (GameClient.ObraInfo o in minhas) corpo.AddChild(LinhaDeObra(o));
		if (minhas.Exists(o => !o.Aparafusada && Interacoes.PedeParafuso(o.Tipo)))
			Nota("A que está solta não funciona: aparafuse (tecla E ao lado dela, ou o verb Bolt).", corpo);
	}

	/// <summary>
	/// Nome a esquerda, a celula a direita, e a pilula "aparafusada"/"solta" -- so em quem TEM parafuso
	/// (`Interacoes.PedeParafuso`: a macieira e a Drill trabalham assentadas). Carrega o metadado `linha` com o
	/// nome, como toda linha rotulo/valor: `ValorDesenhado("Tech", nome)` le a celula.
	/// </summary>
	private static HBoxContainer LinhaDeObra(GameClient.ObraInfo o)
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
			(!Interacoes.PedeParafuso(o.Tipo) ? "" : o.Aparafusada ? "aparafusada" : "solta",
			 o.Aparafusada ? Tema.Bom : Tema.Destaque));
		pilulas.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		h.AddChild(pilulas);
		return h;
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`). A basica cobre o nivel e
	/// as CONTAGENS de obras e ofertas. O que esta aba desenha alem disso:
	///
	///   * as DUAS RECUSAS de cada oferta, a em zeni e a em recursos, com a carteira de agora (elas mudam
	///     quando o saldo muda, sem a contagem mudar) -- um caractere por oferta e por moeda, na ordem do
	///     catalogo;
	///   * se o catalogo JA FOI PEDIDO: a nota "pedindo..." vira "não ofereceu nada" na remontagem
	///     seguinte, e sem este pedaco a pagina vazia ficaria com a frase velha pra sempre;
	///   * o "aparafusada/solta" de cada obra, que o Bolt troca sem mudar a contagem.
	///
	/// O XP E AS DUAS CARTEIRAS NAO ENTRAM desde 2026-10-19: a faixa e a barra do estudo sao VIVAS (reescritas no
	/// lugar a cada pedido, ver `AbaTech`). O que a carteira muda nos CARDS chega pelas recusas acima.
	///
	/// A BUSCA DO CATALOGO TAMBEM NAO ENTRA (2026-10-21): a letra esconde e mostra os cards ja montados, sem remontar a
	/// pagina -- ver `MenuJogo.BuscaDeTech.cs`.
	/// </summary>
	private string ExtraDaAssinaturaDeTech(SheetState f)
	{
		if (GameClient.Instance is not { } c) return "";
		return string.Concat(c.Catalogo.Select(o => (char)('0' + Math.Clamp((int)c.VereditoDe(o, Moeda.Zeni), 0, 9))))
			 + "|" + string.Concat(c.Catalogo.Select(o => (char)('0' + Math.Clamp((int)c.VereditoDe(o, Moeda.Recursos), 0, 9))))
			 + $"|{_pediCatalogo}|"
			 + string.Join(',', c.Obras.Select(o => $"{o.Id}{(o.Aparafusada ? '+' : '-')}"));
	}
}
