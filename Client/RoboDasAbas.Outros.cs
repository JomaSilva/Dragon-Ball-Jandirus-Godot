using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Other da `--diagabas` (F13). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA COBRA: que cada verb de `Verbos.Da(Verbos.Outros)` continue sendo um `Button` com o
/// texto EXATO (o contrato da `--diagtecla`), que a frase do que ele faz esteja VISIVEL, que os verbs
/// estejam em cartoes por tema na ordem fixa da tabela e alfabeticos dentro do cartao, que nenhum
/// verb de Admin vaze pra ca -- e, por INJECAO, que a regra "3+ cartoes" fique vermelha quando a
/// tabela some (todo mundo em "Diversos") sem que nenhum verb se perca no caminho.
///
/// Os helpers de leitura desta frente (botao por texto exato, cartoes por tema, a cor da borda de
/// cima de um cartao) moram aqui e servem as quatro familias F13-F16.
/// </summary>
public partial class RoboDasAbas
{
	private async System.Threading.Tasks.Task F13_Outros(MenuJogo menu, GameClient cli)
	{
		Nota("--- F13: Other: os verbs por tema, cada um com a frase do que faz ---");
		Button? aba = Botao(menu, Verbos.Outros);
		if (aba == null) { Checa("achei a aba Other na barra", false); return; }
		await Clicar(aba);
		await Quadros(2);
		Control? pg = menu.PaginaDeTeste(Verbos.Outros);
		Checa("a aba Other esta na tela", pg is { Visible: true } && menu.AbaDeTeste == Verbos.Outros, menu.AbaDeTeste);
		if (pg == null) return;

		List<Verbo> verbos = Verbos.Da(Verbos.Outros).ToList();
		Checa("ha verbs em Other pra desenhar (os fixos do VerbosDoJogo mais os das habilidades)", verbos.Count >= 10, $"{verbos.Count} verbs");

		// ---- O CONTRATO: um Button visivel por verb, com o texto EXATO, e a frase embaixo
		List<string> semBotao = verbos.Where(v => BotaoDeTextoExato(pg, v.Nome) == null).Select(v => v.Nome).ToList();
		Checa($"CONTRATO: cada um dos {verbos.Count} verbs e um Button visivel com o texto EXATO (e por ele que a --diagtecla acha 'Toggle Knockback')",
			  semBotao.Count == 0, string.Join(", ", semBotao));
		List<string> semFrase = verbos.Where(v => v.Descricao.Length > 0 && Rotulo(pg, v.Descricao) == null).Select(v => v.Nome).ToList();
		Checa("cada verb tem a frase do que faz VISIVEL embaixo do botao (nao so no tooltip)", semFrase.Count == 0, string.Join(", ", semFrase));
		Checa("nenhum verb ficou fora de um cartao por tema", verbos.All(v => BotaoDeTextoExato(pg, v.Nome) is { } b && CartaoDoTema(b) != null));

		// ---- OS CARTOES POR TEMA
		List<PanelContainer> temas = CartoesDeTema(pg);
		List<string> titulos = temas.Select(c => c.GetMeta("titulo").AsString()).ToList();
		Checa("os verbs estao em 3+ cartoes por TEMA (metadado 'grupo')", temas.Count >= 3, string.Join(" | ", titulos));
		List<string> ordem = MenuJogo.OrdemDosGruposDeOutrosDeTeste.ToList();
		List<int> posicoes = titulos.Select(t => ordem.IndexOf(t)).ToList();
		Checa("...na ordem fixa da tabela ('Treino e estudo' primeiro, 'Diversos' por ultimo)",
			  posicoes.All(i => i >= 0) && posicoes.SequenceEqual(posicoes.OrderBy(i => i)), string.Join(",", posicoes));
		Checa("...cada cartao com o titulo em caixa alta, como todo cartao da Learning",
			  temas.All(c => Todos(c).OfType<Label>().Any(l => l.Text == c.GetMeta("titulo").AsString().ToUpperInvariant())));
		Checa("dentro de cada cartao os verbs continuam em ordem alfabetica (a de Verbos.Da)", temas.All(c =>
		{
			List<string> nomes = Todos(c).OfType<Button>().Select(b => b.Text).ToList();
			return nomes.SequenceEqual(nomes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
		}));
		Checa("cartao com 2+ verbs usa DUAS colunas; cartao de um verb so, largura toda", temas.All(c =>
		{
			int n = Todos(c).OfType<Button>().Count();
			bool grade = Todos(c).OfType<GridContainer>().Any(g => g.Columns == 2);
			return n >= 2 ? grade : !grade;
		}));
		int contados = temas.Sum(c => Todos(c).OfType<Button>().Count());
		Checa("a soma dos botoes dos cartoes e exatamente a lista de verbs (nenhum repetido, nenhum a mais)", contados == verbos.Count, $"{contados} botoes pra {verbos.Count} verbs");

		// ---- CONTRA-EXEMPLOS
		List<string> deAdmin = Verbos.Da(Verbos.Admin).Where(v => BotaoDeTextoExato(pg, v.Nome) != null).Select(v => v.Nome).ToList();
		Checa("CONTRA-EXEMPLO: nenhum verb de Admin aparece na Other", deAdmin.Count == 0, string.Join(", ", deAdmin));
		Checa("CONTRA-EXEMPLO: nenhum verb de Skills ('Nadar') nem de Learning aparece na Other",
			  !Verbos.Da(Verbos.Skills).Concat(Verbos.Da(Verbos.Aprendizado)).Any(v => BotaoDeTextoExato(pg, v.Nome) != null));
		Button? tk = BotaoDeTextoExato(pg, "Toggle Knockback");
		Checa("'Toggle Knockback' esta ACESO (a --diagtecla F1 aperta o sinal deste botao)", tk is { Disabled: false }, tk == null ? "nao achei" : $"Disabled={tk.Disabled}");
		if (cli.AlvoId == 0)
		{
			Button? rp = BotaoDeTextoExato(pg, "Remember Person");
			Checa("CONTRA-EXEMPLO: 'Remember Person' esta APAGADO sem alvo marcado (o verb indisponivel aparece, so que apagado -- e o que a --diagtecla F2 le)",
				  rp is { Disabled: true }, rp == null ? "nao achei" : $"Disabled={rp.Disabled}");
		}

		// ---- QUEM VE O QUE (2026-09-03, o segundo relato do dono): verbs de cargo so pra quem tem o cargo,
		//      os de Namek so pra Namek, e "Decolar" foi embora ----
		// A bancada e Saiyajin sem cargo nenhum. Entao: nada de Grande Anciao, Rei de Vegeta, Presidente,
		// Guardiao nem Deus da Destruicao; nada de assento de Anciao; e a Linha de Sucessao APARECE, porque
		// e coisa de Saiyajin. O contra-exemplo e o cargo chegando: `admin_cargo_dar` em si mesmo poe o
		// personagem na Presidencia, a lista de cargos volta do servidor, e "Fund Earth" nasce na aba
		// sem reabrir nada -- e some de novo quando o trono e esvaziado.
		string[] escondidos = ["Appoint Elder", "Accept Elder Seat", "Decline Elder Seat", "Name Heir", "Remove Heir",
							   "Accept Challenge", "Postpone Challenge", "Rank Duty", "Fund Earth",
							   "Time Chamber: Authorize", "Time Chamber: Release", "Decolar"];
		List<string> vazaram = escondidos.Where(n => BotaoDeTextoExato(pg, n) != null).ToList();
		Checa("QUEM VE O QUE: sem cargo e sem ser Namek, nenhum verb de cargo/Namek esta na aba -- nem 'Decolar', que morreu",
			  vazaram.Count == 0, vazaram.Count == 0 ? "" : string.Join(", ", vazaram));
		Checa("...e nem na BUSCA (o mesmo filtro `Verbos.Visivel` vale pros dois balcoes)",
			  !escondidos.Any(n => Verbos.Buscar(n).Any(v => v.Nome == n)));
		Checa("CONTRA-EXEMPLO: 'Line of Succession' APARECE pra um Saiyajin (o trono de Vegeta e assunto dele)",
			  string.Equals(cli.Atributos.Raca, "Saiyan", StringComparison.OrdinalIgnoreCase) && BotaoDeTextoExato(pg, "Line of Succession") != null,
			  $"raca={cli.Atributos.Raca}");
		Checa("CONTRA-EXEMPLO: 'Title Status' e 'Challenge God of Destruction' continuam pra todo mundo (sao de quem NAO tem o titulo)",
			  BotaoDeTextoExato(pg, "Title Status") != null && BotaoDeTextoExato(pg, "Challenge God of Destruction") != null);

		cli.SendVerbo("admin_cargo_dar", $"{cli.LocalId}|president");
		bool virouPresidente = await Ate(() => cli.Cargos.Any(c => c.Chave == "president" && c.Dono == cli.LocalName), 8);
		await Quadros(3);
		pg = menu.PaginaDeTeste(Verbos.Outros)!;
		Checa("dado o cargo de Presidente (admin_cargo_dar em si mesmo), a lista de cargos volta com o meu nome no trono",
			  virouPresidente, string.Join(",", cli.Cargos.Where(c => c.Dono.Length > 0).Select(c => $"{c.Chave}={c.Dono}")));
		Checa("...e 'Fund Earth' e 'Rank Duty' NASCEM na aba Other sem reabrir o menu (a lista e a assinatura da pagina)",
			  BotaoDeTextoExato(pg, "Fund Earth") != null && BotaoDeTextoExato(pg, "Rank Duty") != null);
		Checa("...enquanto os do Guardiao continuam escondidos (o cargo certo, e nao 'qualquer cargo')",
			  BotaoDeTextoExato(pg, "Time Chamber: Authorize") == null && BotaoDeTextoExato(pg, "Appoint Elder") == null);

		cli.SendVerbo("admin_cargo_tirar", $"{cli.LocalId}|president");
		bool perdeu = await Ate(() => !cli.Cargos.Any(c => c.Chave == "president" && c.Dono == cli.LocalName), 8);
		await Quadros(3);
		pg = menu.PaginaDeTeste(Verbos.Outros)!;
		Checa("esvaziado o trono, 'Fund Earth' some de novo -- e 'Rank Duty' junto",
			  perdeu && BotaoDeTextoExato(pg, "Fund Earth") == null && BotaoDeTextoExato(pg, "Rank Duty") == null);

		// ---- A FOTO E O PIXEL
		// A LISTA `temas` ENVELHECEU: a secao de cargo acima REFEZ a pagina duas vezes (a lista de cargos
		// muda a assinatura, a assinatura redesenha), e os PanelContainer antigos foram LIBERADOS pelo
		// Godot. `GetGlobalRect()` num node liberado lanca ObjectDisposedException -- e como o roteiro e
		// uma Task solta (`_ = Rodar()`), a excecao morria CALADA e a bancada ficava viva pra sempre sem
		// placar (foi exatamente o que aconteceu em 2026-09-04). Le-se a pagina de novo, como quem olha.
		temas = CartoesDeTema(pg);
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? foto = await Foto();
		await Guardar("depois-10-other", foto);
		if (temas.Count > 0)
		{
			(Color cor, float frac) = Moda(foto, Caixa(temas[0].GetGlobalRect(), 4));
			ChecaNoPixel("o primeiro cartao e pintado com a paleta do tema (moda de pixel do retangulo dele)", foto != null,
						 NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}% do cartao), paleta {string.Join(" ", Paleta().Select(Hex))}");
			Color borda = CorDaBordaDeCima(foto, temas[0]);
			ChecaNoPixel("...e a borda dele e a borda comum do tema (Tema.Borda), nao a laranja nem a vermelha", foto != null,
						 CorDaBordaMaisProxima(borda) == "Borda", $"{Hex(borda)} -> {CorDaBordaMaisProxima(borda)}");
		}

		// ---- A INJECAO: sem a tabela, todo mundo em "Diversos"
		MenuJogo.SemTabelaDeGruposDeTeste = true;
		menu.ForcarRedesenho();
		await Quadros(2);
		pg = menu.PaginaDeTeste(Verbos.Outros);
		List<PanelContainer> semTabela = pg == null ? [] : CartoesDeTema(pg);
		Injeta("sem a tabela de temas todo verb cai em 'Diversos' e a regra '3+ cartoes' fica VERMELHA",
			   semTabela.Count < 3, $"{semTabela.Count} cartao(oes): {string.Join(" | ", semTabela.Select(c => c.GetMeta("titulo").AsString()))}");
		Checa("...e mesmo sem a tabela nenhum verb se perde (o fallback desenha todos)", pg != null && verbos.All(v => BotaoDeTextoExato(pg, v.Nome) != null));
		MenuJogo.SemTabelaDeGruposDeTeste = false;
		menu.ForcarRedesenho();
		await Quadros(2);
		pg = menu.PaginaDeTeste(Verbos.Outros);
		Checa("com a tabela de volta, os cartoes por tema voltam", pg != null && CartoesDeTema(pg).Count >= 3);
	}

	// =====================================================================
	// LER A TELA -- os helpers das familias F13-F16
	// =====================================================================
	/// <summary>Um botao VISIVEL pelo texto EXATO -- sem o "prefixo" do `Botao`, que acharia "Who (admin)" ao procurar "Who".</summary>
	private static Button? BotaoDeTextoExato(Node raiz, string texto) =>
		Todos(raiz).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == texto);

	/// <summary>Os cartoes por TEMA de verbs (os que carregam o metadado `grupo`), na ordem da tela.</summary>
	private static List<PanelContainer> CartoesDeTema(Node raiz) =>
		Todos(raiz).OfType<PanelContainer>().Where(c => c.IsVisibleInTree() && c.HasMeta("cartao") && c.HasMeta("grupo")).ToList();

	/// <summary>O cartao por tema que contem este node, ou nulo se ele esta solto na pagina.</summary>
	private static PanelContainer? CartaoDoTema(Node n)
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is PanelContainer c && c.HasMeta("grupo")) return c;
		return null;
	}

	/// <summary>
	/// A COR DA BORDA DE CIMA DE UM CARTAO NA FOTO: o pixel mais aceso de uma janelinha de 5x3 no meio
	/// da aresta superior. A borda tem 1 px e o Godot a suaviza com o fundo, entao ler UM pixel cai na
	/// mistura; o mais aceso da janela e o que menos se misturou.
	/// </summary>
	private static Color CorDaBordaDeCima(Image? img, Control c)
	{
		if (img == null) return new Color(0, 0, 0);
		Rect2 r = c.GetGlobalRect();
		int x0 = (int)(r.Position.X + r.Size.X / 2), y0 = (int)r.Position.Y;
		var melhor = new Color(0, 0, 0);
		for (int y = y0; y < y0 + 3; y++)
			for (int x = x0 - 2; x <= x0 + 2; x++)
			{
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
				Color p = img.GetPixel(x, y);
				if (p.R + p.G + p.B > melhor.R + melhor.G + melhor.B) melhor = p;
			}
		return melhor;
	}

	/// <summary>
	/// De qual borda do tema esta cor mais se aproxima, por DIRECAO (matiz) e nao por distancia
	/// absoluta -- a suavizacao escurece a borda sem mudar o matiz dela. As candidatas sao as quatro
	/// bordas que um cartao pode ter: a comum, a laranja de destaque, a verde e a vermelha de perigo.
	/// </summary>
	private static string CorDaBordaMaisProxima(Color c)
	{
		float topo = Math.Max(c.R, Math.Max(c.G, c.B));
		if (topo < 0.05f) return "preta";
		Vector3 d = new Vector3(c.R, c.G, c.B) / topo;
		static Vector3 Dir(Color k) => new Vector3(k.R, k.G, k.B) / Math.Max(k.R, Math.Max(k.G, k.B));
		(string Nome, Color Cor)[] candidatas =
			[("Perigo", Tema.Perigo), ("BordaViva", Tema.BordaViva), ("Bom", Tema.Bom), ("Borda", Tema.Borda)];
		return candidatas.OrderBy(k => (Dir(k.Cor) - d).Length()).First().Nome;
	}

	/// <summary>Uma prova que NAO da pra medir nesta rodada, e por que. Entra no terceiro placar.</summary>
	private void ProvaNaoMedida(string oque, string motivo)
	{
		Nota("  PULADA  " + oque + $"   [{motivo}]");
		_pulados++;
		_naoMedidos.Add(oque);
	}
}
