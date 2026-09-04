using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Tech;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Tech da `--diagabas` (F14). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA COBRA: a faixa com o nivel, a barra de xp, o catalogo chegando do servidor com um card
/// por oferta e a pilula com o motivo em portugues, NENHUM "Construir" aceso onde o servidor
/// recusaria (e a bancada nunca aperta um), a ordem "possiveis primeiro", a trava de "pede o
/// catalogo UMA vez", e o cartao das construcoes da zona.
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
		Checa("...e ela diz o nivel, o xp e o zeni do cliente ('nível N   X / Y xp · Z zeni')",
			  faixa != null && faixa.Contains($"nível {cli.TechNivel:0.#}") && faixa.Contains($"{cli.TechXp:0} / {cli.TechXpAlvo:0} xp") && faixa.Contains($"{cli.Zeni:N0} zeni"),
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

		// ---- a pilula certa em cada card
		var pilulasErradas = new List<string>();
		foreach (PanelContainer c in cards)
		{
			int recusa = c.GetMeta("recusa").AsInt32();
			string pilula = Todos(c).OfType<PanelContainer>().Where(p => p.HasMeta("pilula")).Select(p => p.GetMeta("pilula").AsString()).FirstOrDefault() ?? "(sem pilula)";
			if (!PilulaEsperadaDaRecusa.TryGetValue(recusa, out string? esperada) || pilula != esperada)
				pilulasErradas.Add($"{c.GetMeta("titulo").AsString()}: recusa {recusa} -> '{pilula}'");
		}
		Checa("cada card tem a pilula com o motivo do servidor, em portugues", pilulasErradas.Count == 0, string.Join("; ", pilulasErradas.Take(5)));
		Checa("nenhum rotulo do catalogo e o nome cru do enum RecusaObra", !Rotulos(pg).Any(l => Enum.GetNames<RecusaObra>().Contains(l.Text)));
		Nota("    recusas no catalogo deste personagem: " + string.Join(", ",
			 cards.GroupBy(c => c.GetMeta("recusa").AsInt32()).Select(g => $"{(RecusaObra)g.Key}={g.Count()}")));

		// ---- o botao Construir
		Checa("todo card tem o botao 'Construir' (a bancada NUNCA o aperta)", cards.All(c => BotaoDeTextoExato(c, "Construir") != null));
		List<string> acesosSemPoder = cards
			.Where(c => c.GetMeta("recusa").AsInt32() != (int)RecusaObra.Pode && BotaoDeTextoExato(c, "Construir") is { Disabled: false })
			.Select(c => c.GetMeta("titulo").AsString()).ToList();
		Checa("nenhum 'Construir' ACESO numa oferta que o servidor recusaria (Recusa != Pode)", acesosSemPoder.Count == 0, string.Join(", ", acesosSemPoder));
		List<PanelContainer> possiveis = cards.Where(c => c.GetMeta("recusa").AsInt32() == (int)RecusaObra.Pode).ToList();
		if (possiveis.Count > 0)
			Checa("CONTRA-EXEMPLO: a oferta possivel tem 'Construir' ACESO", possiveis.All(c => BotaoDeTextoExato(c, "Construir") is { Disabled: false }), $"{possiveis.Count} possiveis");
		else
			ProvaNaoMedida("CONTRA-EXEMPLO: a oferta possivel tem 'Construir' aceso",
						   $"este personagem nao pode construir nada (tech {cli.TechNivel:0.#}, {cli.Zeni:N0} zeni): nao ha oferta com Recusa=Pode pra medir");
		Checa("a pergunta 'Fabricar por N zeni?' fica ESCONDIDA ate o clique (nenhum 'Fabricar' visivel)",
			  !Todos(pg).OfType<Button>().Any(b => b.IsVisibleInTree() && b.Text == "Fabricar"));

		// ---- a ordem: possiveis primeiro, depois tech e custo crescentes
		var porId = cli.Catalogo.ToDictionary(o => o.Id, o => o);
		List<GameClient.OfertaDeObra> seq = cards.Select(c => porId[c.GetMeta("id").AsString()]).ToList();
		bool ordenado = true;
		for (int i = 1; i < seq.Count && ordenado; i++)
		{
			GameClient.OfertaDeObra a = seq[i - 1], b = seq[i];
			int ka = a.Recusa == (int)RecusaObra.Pode ? 0 : 1, kb = b.Recusa == (int)RecusaObra.Pode ? 0 : 1;
			if (ka != kb) { ordenado = ka < kb; continue; }
			if (a.Tech != b.Tech) { ordenado = a.Tech < b.Tech; continue; }
			ordenado = a.Custo <= b.Custo;
		}
		Checa("as possiveis vem primeiro, e o resto por tech e custo crescentes", ordenado);
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
	}

	/// <summary>Os cards de oferta do catalogo (metadado `cartao` = "oferta"), na ordem da tela.</summary>
	private static List<PanelContainer> CartoesDeOferta(Node raiz) =>
		Todos(raiz).OfType<PanelContainer>().Where(c => c.IsVisibleInTree() && c.HasMeta("cartao") && c.GetMeta("cartao").AsString() == "oferta").ToList();
}
