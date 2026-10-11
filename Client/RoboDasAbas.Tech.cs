using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Tech da `--diagabas` (F14). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA COBRA: a faixa com o nivel e a carteira, a barra de xp, o catalogo chegando do servidor com um
/// card por oferta e a pilula com o motivo em portugues, os DOIS precos e os DOIS botoes de cada card
/// ("Construir" em zeni e "Com recursos" pelo dobro -- dono, 2026-10-10), nenhum deles aceso onde o servidor
/// recusaria, a ordem "possiveis primeiro", a trava de "pede o catalogo UMA vez", e o cartao das construcoes
/// da zona. A F14b fabrica uma coisa com recursos, pelo dedo (so com `--host`).
/// </summary>
public partial class RoboDasAbas
{
	/// <summary>
	/// A PILULA QUE CADA MOTIVO DO SERVIDOR TEM QUE VIRAR. Escrita AQUI de novo, e nao lida da aba:
	/// uma bancada que perguntasse a producao qual texto esperar aceitaria qualquer texto.
	/// </summary>
	private static readonly Dictionary<int, string> PilulaEsperadaDaRecusa = new()
	{
		[(int)RecusaObra.Pode] = "pode construir",
		[(int)RecusaObra.SemTech] = "falta tecnologia",
		[(int)RecusaObra.SemZeni] = "falta zeni",
		[(int)RecusaObra.RacaErrada] = "não é da sua raça",
		[(int)RecusaObra.LugarOcupado] = "lugar ocupado",
		[(int)RecusaObra.ZonaProibida] = "não aqui",
		[(int)RecusaObra.NaoExiste] = "?",
	};

	private async System.Threading.Tasks.Task F14_Tech(MenuJogo menu, GameClient cli)
	{
		Nota("--- F14: Tech: o nivel, o catalogo com o motivo de cada nao, as construcoes da zona ---");
		Button? aba = Botao(menu, "Tech");
		if (aba == null) { Checa("achei a aba Tech na barra", false); return; }
		await Clicar(aba);
		await Quadros(2);
		Control? pg = menu.PaginaDeTeste("Tech");
		Checa("a aba Tech esta na tela", pg is { Visible: true } && menu.AbaDeTeste == "Tech", menu.AbaDeTeste);
		if (pg == null) return;
		Checa("a aba deixou de ser o aviso 'vem com o sistema de tecnologia'", !Rotulos(pg).Any(l => l.Text.Contains("Vem com o sistema de tecnologia")));

		// ---- a faixa e a barra
		Checa("a FAIXA 'Tecnologia' existe (metadado 'faixa')",
			  Todos(pg).OfType<PanelContainer>().Any(p => p.IsVisibleInTree() && p.HasMeta("faixa") && p.GetMeta("faixa").AsString() == "Tecnologia"));
		string? faixa = menu.ValorDesenhado("Tech", "Tecnologia");
		Checa("...e ela diz o nivel, o xp e a carteira do cliente ('nível N   X / Y xp · Z zeni · R recursos')",
			  faixa != null && faixa.Contains($"nível {cli.TechNivel:0.#}") && faixa.Contains($"{cli.TechXp:0} / {cli.TechXpAlvo:0} xp")
			  && faixa.Contains($"{cli.Zeni:N0} zeni") && faixa.Contains($"{cli.Recursos:N0} recursos"),
			  faixa ?? "(sem faixa)");
		string? exp = menu.ValorDesenhado("Tech", "Experiência");
		Checa("a linha 'Experiência' diz 'xp / alvo' com os numeros do cliente", exp == $"{cli.TechXp:0} / {cli.TechXpAlvo:0}", exp ?? "(sem linha)");
		ProgressBar? barra = Todos(pg).OfType<ProgressBar>().FirstOrDefault(b => b.HasMeta("barra") && b.GetMeta("barra").AsString() == "Experiência");
		Checa("...com uma barra embaixo, cheia na razao xp/alvo", barra != null
			  && Math.Abs(barra.Value / barra.MaxValue - cli.TechXp / Math.Max(cli.TechXpAlvo, 1e-9)) < 0.01,
			  barra == null ? "sem barra" : $"{barra.Value:0.###} de {barra.MaxValue}");

		// ---- o catalogo chega
		bool chegou = await Ate(() => cli.Catalogo.Count > 0, 10);
		if (!chegou) { cli.SendTech("lista"); chegou = await Ate(() => cli.Catalogo.Count > 0, 10); }
		Checa("o catalogo chegou do servidor (a aba pediu 'lista' ao abrir pela primeira vez)", chegou, $"{cli.Catalogo.Count} ofertas");
		if (!chegou) return;
		await Quadros(3);
		pg = menu.PaginaDeTeste("Tech")!;

		List<PanelContainer> cards = CartoesDeOferta(pg);
		Checa("um card por oferta do catalogo", cards.Count == cli.Catalogo.Count, $"{cards.Count} cards pra {cli.Catalogo.Count} ofertas");
		Checa("...numa grade de duas colunas", Todos(pg).OfType<GridContainer>().Any(g => g.Columns == 2 && g.GetChildren().OfType<PanelContainer>().Count() == cards.Count));
		Checa("...cada um com o nome da oferta escrito", cards.All(c => Rotulo(c, c.GetMeta("titulo").AsString()) != null));

		// ---- a pilula certa em cada card, os dois precos e os dois botoes (dono, 2026-10-10: zeni OU recursos)
		ChecarAsDuasMoedas(pg, cli, "");
		Checa("nenhum rotulo do catalogo e o nome cru do enum RecusaObra", !Rotulos(pg).Any(l => Enum.GetNames<RecusaObra>().Contains(l.Text)));
		Nota("    recusas (em zeni) no catalogo deste personagem: " + string.Join(", ",
			 cards.GroupBy(c => c.GetMeta("recusa").AsInt32()).Select(g => $"{(RecusaObra)g.Key}={g.Count()}")));

		List<PanelContainer> possiveis = cards.Where(c => c.GetMeta("recusa").AsInt32() == (int)RecusaObra.Pode).ToList();
		if (possiveis.Count > 0)
			Checa("CONTRA-EXEMPLO: a oferta possivel em zeni tem 'Construir' ACESO", possiveis.All(c => BotaoDeTextoExato(c, "Construir") is { Disabled: false }), $"{possiveis.Count} possiveis");
		else
			ProvaNaoMedida("CONTRA-EXEMPLO: a oferta possivel em zeni tem 'Construir' aceso",
						   $"este personagem nao pode construir nada com zeni (tech {cli.TechNivel:0.#}, {cli.Zeni:N0} zeni): nao ha oferta com Recusa=Pode pra medir");
		Checa("a pergunta 'Fabricar por N ...?' fica ESCONDIDA ate o clique (nenhum 'Fabricar' visivel)",
			  !Todos(pg).OfType<Button>().Any(b => b.IsVisibleInTree() && b.Text == "Fabricar"));
		Checa("todo card tem icone (o sprite do catalogo, ou o vulto com a inicial)",
			  cards.All(c => Todos(c).OfType<TextureRect>().Any(t => t.Texture != null) || Todos(c).OfType<Label>().Any(l => l.Text.Length == 1)));
		int comSprite = cards.Count(c => Todos(c).OfType<TextureRect>().Any(t => t.Texture != null));
		Nota($"    {comSprite} de {cards.Count} cards com o sprite do catalogo; o resto com o vulto");

		// ---- nao pede o catalogo a cada remontagem
		int listas = 0;
		GameClient.EspiaoDeTech = (cmd, _) => { if (cmd == "lista") listas++; return true; };
		for (int i = 0; i < 3; i++) { menu.ForcarRedesenho(); await Quadros(1); }
		GameClient.EspiaoDeTech = null;
		Checa("com o catalogo na mao, tres remontagens NAO pedem 'lista' de novo (seria 5 pedidos/s com o menu aberto)", listas == 0, $"{listas} pedido(s)");

		// ---- as construcoes da zona
		pg = menu.PaginaDeTeste("Tech")!;
		PanelContainer? obras = Todos(pg).OfType<PanelContainer>()
			.FirstOrDefault(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString().StartsWith("Construções nesta zona"));
		Checa("o cartao 'Construções nesta zona (N)' existe, com a contagem do cliente",
			  obras != null && obras.GetMeta("titulo").AsString().Contains($"({cli.Obras.Count})"), obras?.GetMeta("titulo").AsString() ?? "(sem cartao)");
		if (cli.Obras.Count > 0)
		{
			GameClient.ObraInfo primeira = cli.Obras.OrderBy(x => x.Nome, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).First();
			string? onde = menu.ValorDesenhado("Tech", primeira.Nome);
			Checa("cada obra e uma linha 'nome · célula x, y' com a pilula aparafusada/solta",
				  onde != null && onde.StartsWith("célula") && obras != null
				  && Todos(obras).OfType<PanelContainer>().Any(p => p.HasMeta("pilula") && p.GetMeta("pilula").AsString() is "aparafusada" or "solta"),
				  onde ?? "(sem linha)");
		}
		else Checa("...e sem obra nenhuma o cartao diz isso", obras != null && Rotulo(obras, "nenhuma construção nesta zona.") != null);

		// ---- OS ICONES DO CATALOGO (o segundo relato do dono: "na tela de construir do menu P ... esta errado")
		// A aba tinha a TERCEIRA copia do carregador de miniatura, sem o saneamento do nome do estado:
		// o Dragon Radar saia como uma bala e Pee-Kay Ball e Pet Food com o mesmo icone. A referencia que
		// o dono mostrou era a grade da bancada de pesquisa, cujo carregador saneava -- e que agora e o
		// UNICO (`Miniaturas`). O que se cobra: a oferta "Dragon Radar" do catalogo desenha o MESMO quadro
		// que o ITEM Dragon Radar da mochila, e ofertas de folhas/estados diferentes nao repetem quadro.
		List<PanelContainer> cardsDeIcone = CartoesDeOferta(pg);
		PanelContainer? cardRadar = cardsDeIcone.FirstOrDefault(c => Rotulos(c).Any(l => l.Text == "Dragon Radar"));
		Texture2D? texOferta = cardRadar == null ? null : Todos(cardRadar).OfType<TextureRect>().FirstOrDefault()?.Texture;
		Texture2D? texItem = Jandirus.Core.Items.CatalogoDeItens.Get(Jandirus.Core.Items.CatalogoDeItens.Radar) is { } defRadar ? Miniaturas.DoItem(defRadar) : null;
		Checa("a oferta 'Dragon Radar' do catalogo tem o MESMO quadro que o item Dragon Radar da mochila (um carregador so)",
			  texOferta != null && ReferenceEquals(texOferta, texItem), $"oferta={texOferta?.GetInstanceId()} item={texItem?.GetInstanceId()}");
		var porTextura = new Dictionary<ulong, List<GameClient.OfertaDeObra>>();
		foreach (GameClient.OfertaDeObra o in cli.Catalogo)
		{
			if (Miniaturas.De(o.Arte, o.Estado) is not { } t) continue;
			if (!porTextura.TryGetValue(t.GetInstanceId(), out List<GameClient.OfertaDeObra>? grupo)) porTextura[t.GetInstanceId()] = grupo = [];
			grupo.Add(o);
		}
		// QUANDO DIVIDIR UM QUADRO E DEFEITO: entre folhas DIFERENTES, sempre (e o carregador errado); na
		// MESMA folha, so se os dois estados EXISTEM nela e sao outros (entao deviam ser dois quadros).
		// Um estado que a folha NAO TEM cai no padrao dela -- fielmente ao BYOND, onde `icon_state`
		// inexistente mostra o estado padrao do `.dmi`: "Camera Computer" (`PDA.dmi`/"Computer") sai
		// igual ao PDA la e aqui, e a primeira versao desta prova acusava isso como se fosse o bug.
		static string Folha(GameClient.OfertaDeObra o) => System.IO.Path.GetFileName(o.Arte);
		static string RotuloDaOferta(GameClient.OfertaDeObra o) => $"{o.Nome} [{Folha(o)}:{o.Estado}]";
		List<string> repetidos = porTextura.Values.Where(g => g.Count > 1)
			.Where(g => g.Select(o => o.Arte).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
					 || g.Where(o => Miniaturas.TemEstado(o.Arte, o.Estado)).Select(o => Miniaturas.NomeDaAnimacao(o.Estado)).Distinct().Count() > 1)
			.Select(g => string.Join(" = ", g.Select(RotuloDaOferta))).ToList();
		Checa("nenhum quadro e dividido por folhas DIFERENTES, nem por dois estados que a folha TEM (era o Pee-Kay Ball igual ao Pet Food)",
			  repetidos.Count == 0, string.Join(" | ", repetidos.Take(3)));
		int fieis = porTextura.Values.Where(g => g.Count > 1).Count(g => !repetidos.Any(r => r.StartsWith(RotuloDaOferta(g[0]))));
		Nota($"       (quadros divididos FIELMENTE, estado que a folha nao tem -> padrao dela, como no BYOND: {fieis} grupo(s))");

		// ---- fotos e pixel
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? foto = await Foto();
		await Guardar("depois-12-tech", foto);
		List<PanelContainer> cardsAgora = CartoesDeOferta(pg);
		if (cardsAgora.Count > 0)
		{
			(Color cor, float frac) = Moda(foto, Caixa(cardsAgora[0].GetGlobalRect(), 4));
			ChecaNoPixel("o primeiro card do catalogo e pintado com a paleta do tema (moda de pixel)", foto != null, NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}%)");
		}
		if (obras != null && Ancestral<ScrollContainer>(pg) is { } rol2)
		{
			rol2.EnsureControlVisible(obras);
			await Quadros(3);
			await Guardar("depois-12b-tech-obras", await Foto());
		}

		await F14b_PagarComRecursos(menu, cli);
	}

	/// <summary>Os cards de oferta do catalogo (metadado `cartao` = "oferta"), na ordem da tela.</summary>
	private static List<PanelContainer> CartoesDeOferta(Node raiz) =>
		Todos(raiz).OfType<PanelContainer>().Where(c => c.IsVisibleInTree() && c.HasMeta("cartao") && c.GetMeta("cartao").AsString() == "oferta").ToList();

	// =====================================================================
	// AS DUAS MOEDAS (dono, 2026-10-10) -- zeni, ou o dobro em recursos
	// =====================================================================
	/// <summary>
	/// O QUE UMA OFERTA DEVERIA MOSTRAR, calculado AQUI e nao perguntado a aba: o servidor ja julgou raca e
	/// tecnologia (so `Pode` e `SemZeni` passam por elas), e cada carteira paga ou nao paga o preco dela -- em
	/// recursos, o DOBRO. O 2 esta escrito de novo de proposito: e o numero do pedido do dono (*"se algo custo
	/// 1000 zennis pra fazer vc pode fazer com 2000 resources"*), e ler a constante do jogo aceitaria qualquer
	/// valor que ela tivesse.
	/// </summary>
	private static (bool Estrutural, bool EmZeni, bool EmRecursos) EsperadoDe(GameClient.OfertaDeObra o, GameClient cli)
	{
		bool estrutural = o.Recusa is (int)RecusaObra.Pode or (int)RecusaObra.SemZeni;
		return (estrutural, estrutural && cli.Zeni >= o.Custo, estrutural && cli.Recursos >= o.Custo * 2);
	}

	/// <summary>
	/// OS ERROS DA GRADE contra o esperado, card por card: a pilula, os dois precos escritos, os dois botoes (cada
	/// um aceso so se a carteira DELE paga) e a ordem "possiveis primeiro". Devolve a lista em vez de pontuar, pra
	/// a injecao poder chamar a MESMA regua e exigir que ela reprove.
	/// </summary>
	private static List<string> ErrosDasDuasMoedas(Control pg, GameClient cli)
	{
		var erros = new List<string>();
		var porId = cli.Catalogo.ToDictionary(o => o.Id, o => o);
		var seq = new List<(GameClient.OfertaDeObra O, bool Possivel)>();

		foreach (PanelContainer c in CartoesDeOferta(pg))
		{
			string titulo = c.GetMeta("titulo").AsString();
			if (!porId.TryGetValue(c.GetMeta("id").AsString(), out GameClient.OfertaDeObra o)) { erros.Add($"{titulo}: card sem oferta no catalogo"); continue; }
			(bool estrutural, bool emZeni, bool emRecursos) = EsperadoDe(o, cli);
			seq.Add((o, emZeni || emRecursos));

			string pilula = Todos(c).OfType<PanelContainer>().Where(p => p.HasMeta("pilula")).Select(p => p.GetMeta("pilula").AsString()).FirstOrDefault() ?? "(sem pilula)";
			string esperada = emZeni || emRecursos ? "pode construir"
							: estrutural ? "falta zeni e recursos"
							: PilulaEsperadaDaRecusa.GetValueOrDefault(o.Recusa, "(recusa desconhecida)");
			if (pilula != esperada) erros.Add($"{titulo}: pilula '{pilula}', esperada '{esperada}'");

			string preco = Rotulos(c).FirstOrDefault(l => l.HasMeta("preco"))?.Text ?? "(sem linha de preco)";
			if (!preco.Contains($"{o.Custo:N0} zeni") || !preco.Contains($"{o.Custo * 2:N0} recursos"))
				erros.Add($"{titulo}: precos '{preco}' (esperado {o.Custo:N0} zeni e {o.Custo * 2:N0} recursos)");

			Button? deZeni = BotaoDeTextoExato(c, "Construir"), deRecursos = BotaoDeTextoExato(c, "Com recursos");
			if (deZeni == null || deRecursos == null) { erros.Add($"{titulo}: falta o botao '{(deZeni == null ? "Construir" : "Com recursos")}'"); continue; }
			if (deZeni.Disabled == emZeni)
				erros.Add($"{titulo}: 'Construir' {(deZeni.Disabled ? "APAGADO com zeni que paga" : "ACESO sem zeni que pague")}");
			if (deRecursos.Disabled == emRecursos)
				erros.Add($"{titulo}: 'Com recursos' {(deRecursos.Disabled ? "APAGADO com recursos que pagam" : "ACESO sem recursos que paguem")}");
		}

		for (int i = 1; i < seq.Count; i++)
		{
			(GameClient.OfertaDeObra a, bool pa) = seq[i - 1];
			(GameClient.OfertaDeObra b, bool pb) = seq[i];
			bool emOrdem = pa != pb ? pa : a.Tech != b.Tech ? a.Tech < b.Tech : a.Custo <= b.Custo;
			if (!emOrdem) { erros.Add($"ordem: '{a.Nome}' veio antes de '{b.Nome}'"); break; }
		}
		return erros;
	}

	private void ChecarAsDuasMoedas(Control pg, GameClient cli, string cenario)
	{
		List<string> erros = ErrosDasDuasMoedas(pg, cli);
		string onde = cenario.Length > 0 ? $" [{cenario}]" : "";
		Checa($"cada card: a pilula certa, os DOIS precos (zeni e o dobro em recursos), 'Construir' e 'Com recursos' acesos so "
			+ $"com a carteira que paga, e as possiveis primeiro{onde}", erros.Count == 0,
			  erros.Count == 0 ? $"{CartoesDeOferta(pg).Count} cards, {cli.Zeni:N0} zeni, {cli.Recursos:N0} recursos"
							   : $"{erros.Count} erro(s): " + string.Join("; ", erros.Take(4)));
	}

	/// <summary>
	/// PAGAR COM RECURSOS, PELO DEDO: a carteira e acertada pelo servidor (so com ele neste processo, `--host`) e
	/// o robo le o card, aperta "Com recursos", le a pergunta, aperta "Fabricar" e confere o fio, a carteira e a
	/// mochila. O contra-exemplo e o jogo sem o dobro (`Recurso.SemDobroDeTeste`): a regua tem de reprovar.
	/// </summary>
	private async System.Threading.Tasks.Task F14b_PagarComRecursos(MenuJogo menu, GameClient cli)
	{
		Nota("--- F14b: Tech: pagar com RECURSOS (o dobro), e nunca completar com zeni ---");
		bool hospedando = Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0;
		if (!hospedando || Jandirus.Server.GameServer.Instance is not { } srv)
		{
			ProvaNaoMedida("pagar com recursos pela aba Tech (carteira acertada pelo servidor)",
						   "servidor dedicado: nao ha como acertar a carteira daqui; com --host esta parte roda");
			return;
		}

		double zeniAntes = cli.Zeni, recursosAntes = cli.Recursos, techAntes = cli.TechNivel;
		try
		{
			// TECNOLOGIA BASTANTE PRA HAVER O QUE FABRICAR, e a carteira ZERADA.
			srv.EconomiaDeTeste(cli.LocalId, zeni: 0, recursos: 0, tech: 20);
			bool zerou = await Ate(() => cli.TechNivel >= 20 && cli.Zeni == 0 && cli.Recursos == 0
										 && cli.Catalogo.Any(o => o.Recusa == (int)RecusaObra.SemZeni), 8);
			Checa("(montagem) o servidor acertou tecnologia 20 e carteira zerada, e o catalogo novo chegou", zerou,
				  $"tech {cli.TechNivel:0.#}, {cli.Zeni:N0} zeni, {cli.Recursos:N0} recursos");
			if (!zerou) return;

			// O ALVO: a oferta mais barata que so a carteira impede. Barata de proposito: ela vai ser fabricada.
			GameClient.OfertaDeObra alvo = cli.Catalogo.Where(o => o.Recusa == (int)RecusaObra.SemZeni && o.Custo > 0)
													  .OrderBy(o => o.Custo).ThenBy(o => o.Nome, StringComparer.Ordinal).First();
			double emRecursos = alvo.Custo * 2;
			Nota($"    alvo: {alvo.Nome} ({alvo.Custo:N0} zeni ou {emRecursos:N0} recursos, tech {alvo.Tech:0.#})");

			async System.Threading.Tasks.Task<PanelContainer?> CardDoAlvo()
			{
				await Quadros(4);
				return menu.PaginaDeTeste("Tech") is { } p
					? CartoesDeOferta(p).FirstOrDefault(c => c.GetMeta("id").AsString() == alvo.Id) : null;
			}
			static string PilulaDe(Node card) =>
				Todos(card).OfType<PanelContainer>().Where(p => p.HasMeta("pilula")).Select(p => p.GetMeta("pilula").AsString()).FirstOrDefault() ?? "";

			// ---- 1. sem nada: as duas faltam
			PanelContainer? card = await CardDoAlvo();
			ChecarAsDuasMoedas(menu.PaginaDeTeste("Tech")!, cli, "carteira zerada");
			Checa("carteira zerada: o alvo diz 'falta zeni e recursos' e os DOIS botoes estao apagados",
				  card != null && PilulaDe(card) == "falta zeni e recursos"
				  && BotaoDeTextoExato(card, "Construir") is { Disabled: true } && BotaoDeTextoExato(card, "Com recursos") is { Disabled: true },
				  card == null ? "sem card" : PilulaDe(card));

			// ---- 2. faltando UM recurso: nao acende
			srv.EconomiaDeTeste(cli.LocalId, zeni: 0, recursos: emRecursos - 1);
			await Ate(() => cli.Recursos == emRecursos - 1, 8);
			card = await CardDoAlvo();
			Checa($"com {emRecursos - 1:N0} recursos (falta UM pro dobro), 'Com recursos' continua apagado",
				  card != null && BotaoDeTextoExato(card, "Com recursos") is { Disabled: true }, $"cliente {cli.Recursos:N0}");

			// ---- 3. o dobro exato em recursos, zeni ZERO: so o botao de recursos acende
			srv.EconomiaDeTeste(cli.LocalId, zeni: 0, recursos: emRecursos);
			await Ate(() => cli.Recursos == emRecursos, 8);
			card = await CardDoAlvo();
			ChecarAsDuasMoedas(menu.PaginaDeTeste("Tech")!, cli, "so recursos");
			Button? comRecursos = card == null ? null : BotaoDeTextoExato(card, "Com recursos");
			Checa($"com {emRecursos:N0} recursos e ZERO zeni: 'Com recursos' ACESO, 'Construir' apagado, e a pilula diz 'pode construir'",
				  card != null && comRecursos is { Disabled: false } && BotaoDeTextoExato(card, "Construir") is { Disabled: true }
				  && PilulaDe(card) == "pode construir", card == null ? "sem card" : PilulaDe(card));
			string? faixa = menu.ValorDesenhado("Tech", "Tecnologia");
			Checa("a faixa da aba escreve os recursos da carteira ('N recursos')", faixa != null && faixa.Contains($"{cli.Recursos:N0} recursos"), faixa ?? "(sem faixa)");
			if (Ancestral<ScrollContainer>(menu.PaginaDeTeste("Tech")!) is { } rol) rol.ScrollVertical = 0;
			await Quadros(2);
			await Guardar("depois-12c-tech-recursos", await Foto());

			// ---- 3b. CONTRA-EXEMPLO: o jogo SEM o dobro acende o botao com a metade -- e a regua reprova
			srv.EconomiaDeTeste(cli.LocalId, zeni: 0, recursos: alvo.Custo);
			await Ate(() => cli.Recursos == alvo.Custo, 8);
			Jandirus.Core.Economia.Recurso.SemDobroDeTeste = true;
			try
			{
				menu.ForcarRedesenho();
				await Quadros(4);
				List<string> erros = ErrosDasDuasMoedas(menu.PaginaDeTeste("Tech")!, cli);
				Injeta("sem o dobro (`Recurso.SemDobroDeTeste`), a metade dos recursos acende 'Com recursos' -- e a regua das duas moedas reprova",
					   erros.Count > 0, erros.Count > 0 ? erros[0] : "a regua ficou verde com o defeito");
			}
			finally { Jandirus.Core.Economia.Recurso.SemDobroDeTeste = false; }
			menu.ForcarRedesenho();
			await Quadros(3);

			// ---- 4. fabricar com recursos, pelos dois botoes -- e o que vai no fio
			srv.EconomiaDeTeste(cli.LocalId, zeni: 0, recursos: emRecursos);
			await Ate(() => cli.Recursos == emRecursos, 8);
			card = await CardDoAlvo();
			comRecursos = card == null ? null : BotaoDeTextoExato(card, "Com recursos");
			if (card == null || comRecursos is not { Disabled: false }) { Checa("o botao 'Com recursos' do alvo esta aceso pra apertar", false); return; }

			int naMochilaAntes = cli.Mochila.Quantos(alvo.Id);
			var noFio = new List<string>();
			GameClient.EspiaoDeTech = (cmd, arg) => { noFio.Add($"{cmd} {arg}"); return true; };
			try
			{
				await Clicar(comRecursos);
				Checa($"apertar 'Com recursos' abre a pergunta com o preco NA MOEDA ('Fabricar por {emRecursos:N0} recursos?')",
					  Rotulo(card, $"Fabricar por {emRecursos:N0} recursos?") != null,
					  string.Join(" | ", Rotulos(card).Select(l => l.Text)));
				Button? fabricar = BotaoDeTextoExato(card, "Fabricar");
				Checa("...com o 'Fabricar' ao lado", fabricar != null);
				if (fabricar != null) await Clicar(fabricar);
			}
			finally { GameClient.EspiaoDeTech = null; }

			Checa("o pacote que saiu e `construir_recursos <id>` (e nenhum `construir` seco)",
				  noFio.Contains($"construir_recursos {alvo.Id}") && !noFio.Contains($"construir {alvo.Id}"), string.Join(", ", noFio));
			bool pagou = await Ate(() => cli.Recursos == 0 && cli.Mochila.Quantos(alvo.Id) == naMochilaAntes + 1, 8);
			Checa($"o servidor cobrou os {emRecursos:N0} recursos, entregou {alvo.Nome} na mochila e NAO tocou no zeni",
				  pagou && cli.Zeni == 0, $"recursos {cli.Recursos:N0}, zeni {cli.Zeni:N0}, mochila {cli.Mochila.Quantos(alvo.Id)} (antes {naMochilaAntes})");

			// ---- 5. so zeni: o outro botao, a outra pergunta (e a bancada cancela: nao compra de novo)
			srv.EconomiaDeTeste(cli.LocalId, zeni: alvo.Custo, recursos: 0);
			await Ate(() => cli.Zeni == alvo.Custo && cli.Recursos == 0, 8);
			card = await CardDoAlvo();
			ChecarAsDuasMoedas(menu.PaginaDeTeste("Tech")!, cli, "so zeni");
			Button? construir = card == null ? null : BotaoDeTextoExato(card, "Construir");
			Checa($"com {alvo.Custo:N0} zeni e ZERO recursos: 'Construir' ACESO e 'Com recursos' apagado",
				  card != null && construir is { Disabled: false } && BotaoDeTextoExato(card, "Com recursos") is { Disabled: true });
			if (card != null && construir is { Disabled: false })
			{
				await Clicar(construir);
				Checa($"apertar 'Construir' pergunta em ZENI ('Fabricar por {alvo.Custo:N0} zeni?')",
					  Rotulo(card, $"Fabricar por {alvo.Custo:N0} zeni?") != null, string.Join(" | ", Rotulos(card).Select(l => l.Text)));
				if (BotaoDeTextoExato(card, "Cancelar") is { } cancelar) await Clicar(cancelar);
				Checa("...e 'Cancelar' devolve os dois botoes sem gastar nada",
					  BotaoDeTextoExato(card, "Construir") != null && BotaoDeTextoExato(card, "Fabricar") == null && cli.Zeni == alvo.Custo);
			}
		}
		finally
		{
			Jandirus.Core.Economia.Recurso.SemDobroDeTeste = false;
			GameClient.EspiaoDeTech = null;
			srv.EconomiaDeTeste(cli.LocalId, zeniAntes, recursosAntes, techAntes);
			await Ate(() => cli.Zeni == zeniAntes && cli.Recursos == recursosAntes, 5);
		}
	}
}
