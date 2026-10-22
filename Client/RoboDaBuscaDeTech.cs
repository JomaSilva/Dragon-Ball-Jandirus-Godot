using System.Globalization;
using System.Text;
using Godot;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA BUSCA DO CATALOGO DA ABA TECH (`--diagbuscatech`, 2026-10-21) -- pedido de jogador repassado pelo dono: *"Search no menu de
/// tech"*. A regra e a tela moram em `MenuJogo.BuscaDeTech.cs`. Com janela e `--host`, um personagem humano, com o menu P na aba Tech. A
/// ESCRITA E PELO TECLADO: o robo clica na caixa e empurra eventos de tecla de verdade no viewport -- o campo so recebe a letra se tiver o
/// foco, e e assim que se ve que o foco sobreviveu a uma remontagem.
///
/// OS TERMOS FORAM ESCOLHIDOS NAS DESCRICOES QUE O CLIENTE TEM (`CatalogoDeItens.Get`, a mesma do tooltip do card), e nao no
/// `construcoes.json`: o item de bolso troca a frase do catalogo (`TechDeBolso`) -- o DNA Container la e "Recipiente criogenico", e no
/// cliente "Extrai e guarda amostras de DNA de quem está nocauteado".
///
/// Cada termo se confere em tres partes, com o esperado calculado AQUI (o nome e a descricao de cada oferta, sem acento e sem caixa, numa
/// copia propria da conta -- uma regua que perguntasse a producao aceitaria qualquer resposta):
///   a) os cards na tela sao exatamente os que casam;  b) o "(N)" do titulo e o numero de cards na tela;  c) o cartao "Busca" aparece se, e
///   so se, nenhum card ficou.
///
///   T1. a caixa "procurar construção..." mora logo abaixo do titulo do catalogo; vazia, um card por oferta -- foto `buscatech-1-vazia`;
///   T2. "gaster", letra por letra: so o Gaster Blaster, e a pagina NAO e remontada (o filtro esconde e mostra) -- foto `buscatech-2-gaster`.
///       A MEDIDA: o custo por letra de esconder e mostrar, e o de remontar a pagina por letra (o defeito `BuscaDaTechRemontaDeTeste`, o
///       caminho da busca do Sense), que tambem e o contra-exemplo da regua da remontagem. CONTRA-EXEMPLO `BuscaDaTechCegaDeTeste`;
///   T3. PELA DESCRICAO, sem acento e sem caixa: "PÓLVORA" acha a municao e as armas de polvora (so a descricao delas diz "pólvora") e
///       "rapido" a pa e a furadeira ("mais rápido") -- foto `buscatech-3-pela-descricao`. CONTRA-EXEMPLOS `BuscaDaTechSoPeloNomeDeTeste` e
///       `BuscaDaTechCruaDeTeste`;
///   T4. NADA CASA: "zzzz" -- zero cards, "(0)" e o cartao "Busca" dizendo o termo -- foto `buscatech-4-nada`. CONTRA-EXEMPLOS
///       `BuscaDaTechContaTudoDeTeste` e `BuscaDaTechSemNotaDeTeste`;
///   T5. SO O CATALOGO: com a busca no meio, o cartao "Suas construções nesta zona" continua inteiro;
///   T6. A BUSCA E O FOCO SOBREVIVEM A REMONTAGEM (o catalogo ou a carteira chegando): o campo novo tem o texto, o foco e o filtro, e a
///       letra seguinte vai pra caixa -- nao abre a mochila -- foto `buscatech-5-depois-da-remontagem`. CONTRA-EXEMPLOS
///       `BuscaDaTechEsquecidaNaRemontagemDeTeste` e `MenuJogo.FocoMorreNaRemontagemDeTeste` (o de 2026-10-12);
///   T7. apagar devolve todos os cards -- foto `buscatech-6-apagada`.
///
///     Godot --path . --host --rede 10101 --campoteste 8 --horateste 0.5 --semfauna --diagbuscatech --raca Human --conta bancada_buscatech --nome Inventor
/// </summary>
public partial class RoboDaBuscaDeTech : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;

	/// <summary>As ofertas que os termos tem de achar (os ids do catalogo, escritos aqui de novo).</summary>
	private const string Gaster = "G_Blaster", Municao = "Ammo", Pa = "Shovel", Furadeira = "Hand_Drill";

	/// <summary>Os termos: um que so o NOME acha, e dois que so a DESCRICAO acha -- um com acento e caixa no termo, outro com o acento no texto.</summary>
	private const string PeloNome = "gaster", PelaDescricao = "PÓLVORA", SemAcento = "rapido";

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (ok) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => DesligarOsDefeitos();

	private static void DesligarOsDefeitos()
	{
		MenuJogo.BuscaDaTechCegaDeTeste = MenuJogo.BuscaDaTechSoPeloNomeDeTeste = MenuJogo.BuscaDaTechCruaDeTeste = false;
		MenuJogo.BuscaDaTechContaTudoDeTeste = MenuJogo.BuscaDaTechSemNotaDeTeste = MenuJogo.BuscaDaTechRemontaDeTeste = false;
		MenuJogo.BuscaDaTechEsquecidaNaRemontagemDeTeste = MenuJogo.FocoMorreNaRemontagemDeTeste = false;
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && M != null && World.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou e o mundo e o menu P nasceram");
		if (!pronto || C is not { } cli || M is not { } menu) return;
		await Segundos(1.5);
		GD.Print("[dbuscatech] ===== A BUSCA DO CATALOGO DA ABA TECH (pedido de jogador, 2026-10-21) =====");

		menu.Abrir();
		menu.IrPara("Tech");
		bool chegou = await Ate(() => cli.Catalogo.Count > 0, 12);
		await Assentar(menu);
		Conferir(chegou && Esperados(cli, "").Count == cli.Catalogo.Count,
				 $"(montagem) o catalogo chegou do servidor [{cli.Catalogo.Count} ofertas]");
		if (!chegou) return;
		Conferir(cli.Catalogo.Any(o => o.Id == Gaster) && cli.Catalogo.Any(o => o.Id == Municao) && cli.Catalogo.Any(o => o.Id == Pa)
				 && cli.Catalogo.Any(o => o.Id == Furadeira),
				 "(montagem) o catalogo deste humano tem o Gaster Blaster, a municao, a pa e a furadeira (os alvos dos termos)");
		try
		{
			await T1_ACaixa(menu, cli);
			await T2_OPrimeiroTermo(menu, cli);
			await T3_PelaDescricao(menu, cli);
			await T4_NadaCasa(menu, cli);
			await T5_SoOCatalogo(menu, cli);
			await T6_ARemontagem(menu, cli);
			await T7_Apagar(menu, cli);
		}
		finally
		{
			DesligarOsDefeitos();
			if (TelaDeInventario.Instancia is { MinhaAbertaDeTeste: true } inv) inv.Fechar();
			menu.Fechar();
		}
	}

	// =====================================================================
	// T1) A CAIXA
	// =====================================================================
	private async Tarefa T1_ACaixa(MenuJogo menu, GameClient cli)
	{
		LineEdit? campo = Campo(menu);
		Control? pg = menu.PaginaDeTeste("Tech");
		Label? titulo = Titulo(menu);
		int iTitulo = titulo == null || pg == null ? -9 : titulo.GetIndex();
		int iCampo = campo == null || campo.GetParent() != pg ? -1 : campo.GetIndex();
		Conferir(campo != null && titulo != null && iCampo == iTitulo + 2 && pg?.GetChild(iTitulo + 1) is HSeparator,
				 $"T1. a caixa \"{MenuJogo.DicaDaBuscaDaTech}\" mora LOGO ABAIXO do titulo do catalogo (o titulo, o separador, a caixa) "
				 + $"[titulo na posicao {iTitulo}, caixa na {iCampo}: \"{titulo?.Text}\"]");
		await Trio(menu, cli, "", "T1");
		await Fotografar(menu, "user://buscatech-1-vazia.png");
	}

	// =====================================================================
	// T2) O PRIMEIRO TERMO, A REMONTAGEM E A MEDIDA
	// =====================================================================
	private async Tarefa T2_OPrimeiroTermo(MenuJogo menu, GameClient cli)
	{
		bool focou = await Focar(menu);
		Conferir(focou && MenuJogo.Digitando && Foco.Digitando,
				 "T2. o clique na caixa da o teclado a ela (o jogo sabe que alguem escreve, e cala os atalhos)");

		var leve = new List<(double Sync, double Quadro)>();
		int r0 = menu.RemontagensDaAbaDeTeste("Tech");
		await Teclar(menu, PeloNome, leve);
		int remontou = menu.RemontagensDaAbaDeTeste("Tech") - r0;
		Conferir(Esperados(cli, PeloNome).SetEquals([Gaster]), $"(montagem) pela conta do robo, \"{PeloNome}\" casa so com o Gaster Blaster");
		await Trio(menu, cli, PeloNome, "T2");
		Conferir(remontou == 0, $"T2r. **AS SEIS LETRAS NAO REMONTARAM A PAGINA**: o filtro esconde e mostra os cards montados [{remontou} remontagem(ns)]");
		await Fotografar(menu, "user://buscatech-2-gaster.png");

		// A MEDIDA, E O CONTRA-EXEMPLO DA REMONTAGEM: o mesmo termo, cada letra remontando a pagina (o caminho da busca do Sense).
		await Apagar(menu);
		var pesada = new List<(double Sync, double Quadro)>();
		int r1 = menu.RemontagensDaAbaDeTeste("Tech");
		double ms1 = menu.MsRemontandoDaAbaDeTeste("Tech");
		MenuJogo.BuscaDaTechRemontaDeTeste = true;
		try { await Teclar(menu, PeloNome, pesada); }
		finally { MenuJogo.BuscaDaTechRemontaDeTeste = false; }
		int remontagens = menu.RemontagensDaAbaDeTeste("Tech") - r1;
		double msDeMontagem = menu.MsRemontandoDaAbaDeTeste("Tech") - ms1;
		Conferir(remontagens >= 6, "CONTRA-EXEMPLO (defeito injetado: cada letra remonta a pagina, como a busca do Sense): as seis letras remontam a "
								  + $"pagina -- a regua do T2r REPROVA [{remontagens} remontagem(ns)]");
		Nota($"MEDIDA -- esconder e mostrar (o caminho escolhido): {Media(leve, x => x.Sync):0.00} ms por letra no evento da tecla, "
			 + $"pior quadro {leve.Max(x => x.Quadro):0.0} ms | remontar por letra (o da busca do Sense): {Media(pesada, x => x.Sync):0.00} ms por "
			 + $"letra no evento ({(remontagens > 0 ? msDeMontagem / remontagens : 0):0.0} ms de montagem cada), pior quadro {pesada.Max(x => x.Quadro):0.0} ms "
			 + $"| {cli.Catalogo.Count} cards");

		// CONTRA-EXEMPLO DO FILTRO: a busca que ignora o termo.
		await Apagar(menu);
		MenuJogo.BuscaDaTechCegaDeTeste = true;
		try
		{
			await Teclar(menu, PeloNome);
			Conferir(!IdsNaTela(menu).SetEquals(Esperados(cli, PeloNome)),
					 $"CONTRA-EXEMPLO (defeito injetado: a busca que ignora o termo): \"{PeloNome}\" deixa todos os cards -- a regua do T2a REPROVA [{IdsNaTela(menu).Count} cards]");
		}
		finally { MenuJogo.BuscaDaTechCegaDeTeste = false; }
	}

	// =====================================================================
	// T3) PELA DESCRICAO, SEM ACENTO E SEM CAIXA
	// =====================================================================
	private async Tarefa T3_PelaDescricao(MenuJogo menu, GameClient cli)
	{
		await Escrever(menu, PelaDescricao);
		HashSet<string> polvora = Esperados(cli, PelaDescricao);
		Conferir(polvora.Contains(Municao) && polvora.Count >= 2 && polvora.All(id => !ONomeDiz(cli, id, PelaDescricao)),
				 $"(montagem) pela conta do robo, \"{PelaDescricao}\" casa com a municao e as armas de polvora, so pela descricao (nenhum nome diz) "
				 + $"[{string.Join(", ", polvora)}]");
		await Trio(menu, cli, PelaDescricao, "T3");
		await Fotografar(menu, "user://buscatech-3-pela-descricao.png");

		// O DEFEITO DA DESCRICAO mora no registro do card (na montagem): a pagina e remontada com ele ligado.
		MenuJogo.BuscaDaTechSoPeloNomeDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(3);
			Conferir(!IdsNaTela(menu).SetEquals(polvora),
					 $"CONTRA-EXEMPLO (defeito injetado: a busca so no nome): \"{PelaDescricao}\" nao acha a municao -- a regua do T3a REPROVA [{Lista(menu)}]");
		}
		finally { MenuJogo.BuscaDaTechSoPeloNomeDeTeste = false; }
		menu.ForcarRedesenho();
		await Quadros(3);

		MenuJogo.BuscaDaTechCruaDeTeste = true;
		try
		{
			await Escrever(menu, PelaDescricao);
			Conferir(!IdsNaTela(menu).SetEquals(polvora),
					 $"CONTRA-EXEMPLO (defeito injetado: a busca no texto cru, com acento e caixa): \"{PelaDescricao}\" nao acha o \"pólvora\" -- a regua do "
					 + $"T3a REPROVA [{Lista(menu)}]");
		}
		finally { MenuJogo.BuscaDaTechCruaDeTeste = false; }

		await Escrever(menu, SemAcento);
		HashSet<string> rapido = Esperados(cli, SemAcento);
		Conferir(rapido.Contains(Pa) && rapido.Contains(Furadeira) && rapido.All(id => !ONomeDiz(cli, id, SemAcento)),
				 $"(montagem) pela conta do robo, \"{SemAcento}\" casa com a pa e a furadeira, so pela descricao (\"mais rápido\") [{string.Join(", ", rapido)}]");
		await Trio(menu, cli, SemAcento, "T3r");
	}

	// =====================================================================
	// T4) NADA CASA
	// =====================================================================
	private async Tarefa T4_NadaCasa(MenuJogo menu, GameClient cli)
	{
		await Escrever(menu, "zzzz");
		await Trio(menu, cli, "zzzz", "T4");
		string nota = NotaDaBusca(menu);
		Conferir(nota.Contains("\"zzzz\""), $"T4d. o cartao \"Busca\" diz o termo que nao casou [\"{nota}\"]");
		await Fotografar(menu, "user://buscatech-4-nada.png");

		MenuJogo.BuscaDaTechContaTudoDeTeste = true;
		try
		{
			await Escrever(menu, "zzzz");
			Conferir(ContagemDoTitulo(menu) != CardsNaTela(menu).Count,
					 $"CONTRA-EXEMPLO (defeito injetado: o \"(N)\" contando o catalogo inteiro): o titulo diz ({ContagemDoTitulo(menu)}) com zero cards na tela -- a regua do T4b REPROVA");
		}
		finally { MenuJogo.BuscaDaTechContaTudoDeTeste = false; }

		MenuJogo.BuscaDaTechSemNotaDeTeste = true;
		try
		{
			await Escrever(menu, "zzzz");
			Conferir(!CartaoDaBuscaNaTela(menu), "CONTRA-EXEMPLO (defeito injetado: a pagina sem o cartao de nada casa): zero cards e nenhuma palavra -- a regua do T4c REPROVA");
		}
		finally { MenuJogo.BuscaDaTechSemNotaDeTeste = false; }
		await Escrever(menu, "zzzz");
	}

	// =====================================================================
	// T5) SO O CATALOGO
	// =====================================================================
	private async Tarefa T5_SoOCatalogo(MenuJogo menu, GameClient cli)
	{
		// Uma obra MINHA na lista do cliente (a mesma porta da `--diagabas`, F14): so dele, e sai no fim.
		var minha = new GameClient.ObraInfo(9_900_101, Jandirus.Core.Tech.Regenerador.Tipo, "Regenerator da busca", new Vector2(320, 320),
											Aparafusada: true, Lab: 0, Dono: cli.LocalName, Arte: "", Estado: "", Pixel: default, Densa: false);
		cli.Obras.Add(minha);
		try
		{
			menu.ForcarRedesenho();
			await Quadros(3);
			PanelContainer? obras = CartaoTitulado(menu, MenuJogo.TituloDasMinhasObras);
			int linhas = obras == null ? 0 : Todos(obras).OfType<HBoxContainer>().Count(h => h.HasMeta("linha") && h.IsVisibleInTree());
			Conferir(obras is { } o && o.IsVisibleInTree() && o.GetMeta("titulo").AsString().Contains("(1)") && linhas == 1,
					 $"T5. **SO O CATALOGO E FILTRADO**: com \"zzzz\" na caixa, o cartao \"{MenuJogo.TituloDasMinhasObras}\" continua na tela com a obra "
					 + $"do jogador [{obras?.GetMeta("titulo").AsString() ?? "(sem cartao)"}, {linhas} linha(s)]");
		}
		finally
		{
			cli.Obras.Remove(minha);
			menu.ForcarRedesenho();
			await Quadros(3);
		}
	}

	// =====================================================================
	// T6) A REMONTAGEM POR OUTRO MOTIVO
	// =====================================================================
	private async Tarefa T6_ARemontagem(MenuJogo menu, GameClient cli)
	{
		await Escrever(menu, PeloNome);
		ulong antes = Campo(menu)?.GetInstanceId() ?? 0;
		menu.ForcarRedesenho();   // o que o pacote do catalogo ou da carteira faz quando muda o que a aba desenha
		await Quadros(3);
		LineEdit? novo = Campo(menu);
		Conferir(novo != null && novo.GetInstanceId() != antes && novo.Text == PeloNome && novo.HasFocus() && MenuJogo.Digitando,
				 "T6. **A PAGINA REMONTOU E A CAIXA CONTINUA COM O TEXTO E O TECLADO**: e outro node, com \"gaster\" e o foco "
				 + $"[{(novo == null ? "(sem caixa)" : $"\"{novo.Text}\", foco {novo.HasFocus()}, outro node {novo.GetInstanceId() != antes}")}]");
		await Trio(menu, cli, PeloNome, "T6");
		await Fotografar(menu, "user://buscatech-5-depois-da-remontagem.png");

		await Teclar(menu, "i");
		bool naCaixa = Campo(menu)?.Text == PeloNome + "i", mochila = TelaDeInventario.Instancia?.MinhaAbertaDeTeste == true;
		Conferir(naCaixa && !mochila, $"T6f. ...e a letra seguinte vai pra caixa (\"gasteri\") e NAO abre a mochila [caixa \"{Campo(menu)?.Text}\", mochila {mochila}]");
		await Teclar(menu, "\b");

		MenuJogo.FocoMorreNaRemontagemDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(3);
			await Teclar(menu, "i");
			bool foiPraCaixa = Campo(menu)?.Text == PeloNome + "i";
			bool abriu = TelaDeInventario.Instancia?.MinhaAbertaDeTeste == true;
			Conferir(!foiPraCaixa, $"CONTRA-EXEMPLO (defeito injetado: a pagina remontada nasce sem foco, o de 2026-10-12): o \"i\" nao chega na caixa "
								   + $"-- a regua do T6f REPROVA [caixa \"{Campo(menu)?.Text}\", mochila aberta {abriu}]");
		}
		finally { MenuJogo.FocoMorreNaRemontagemDeTeste = false; }
		if (TelaDeInventario.Instancia is { MinhaAbertaDeTeste: true } inv) inv.Fechar();
		await Quadros(2);
		await Focar(menu);

		MenuJogo.BuscaDaTechEsquecidaNaRemontagemDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(3);
			Conferir(!IdsNaTela(menu).SetEquals(Esperados(cli, PeloNome)),
					 $"CONTRA-EXEMPLO (defeito injetado: a pagina remontada nasce sem o filtro): \"gaster\" na caixa e todos os cards na tela -- a regua do T6a REPROVA [{IdsNaTela(menu).Count} cards]");
		}
		finally { MenuJogo.BuscaDaTechEsquecidaNaRemontagemDeTeste = false; }
		menu.ForcarRedesenho();
		await Quadros(3);
	}

	// =====================================================================
	// T7) APAGAR
	// =====================================================================
	private async Tarefa T7_Apagar(MenuJogo menu, GameClient cli)
	{
		await Focar(menu);
		await Apagar(menu);
		Conferir(Campo(menu)?.Text == "", $"T7. a caixa ficou vazia pelo teclado [\"{Campo(menu)?.Text}\"]");
		await Trio(menu, cli, "", "T7");
		await Fotografar(menu, "user://buscatech-6-apagada.png");
	}

	// =====================================================================
	// AS TRES PARTES DE CADA TERMO
	// =====================================================================
	/// <summary>a) os cards na tela sao os que casam; b) o "(N)" e o numero de cards na tela; c) o cartao "Busca" so quando nao ficou nenhum.</summary>
	private async Tarefa Trio(MenuJogo menu, GameClient cli, string termo, string codigo)
	{
		await Quadros(2);
		HashSet<string> esperados = Esperados(cli, termo), naTela = IdsNaTela(menu);
		int n = CardsNaTela(menu).Count, titulo = ContagemDoTitulo(menu);
		bool cartao = CartaoDaBuscaNaTela(menu);
		string q = termo.Length == 0 ? "a busca vazia" : $"\"{termo}\"";
		Conferir(naTela.SetEquals(esperados), $"{codigo}a. {q}: os cards na tela sao exatamente os que casam [{naTela.Count} na tela, {esperados.Count} esperado(s): {Lista(menu)}]");
		Conferir(titulo == n, $"{codigo}b. {q}: o \"(N)\" do titulo e o numero de cards na tela [titulo ({titulo}), {n} na tela]");
		Conferir(cartao == (n == 0), $"{codigo}c. {q}: o cartao \"Busca\" aparece so quando nenhum card ficou [cartao {cartao}, {n} na tela]");
	}

	// =====================================================================
	// A REGUA DO ROBO -- a mesma conta da producao, escrita de novo
	// =====================================================================
	/// <summary>Sem acento, sem caixa e sem espaco nas pontas.</summary>
	private static string Normal(string s)
	{
		var sb = new StringBuilder(s.Length);
		foreach (char c in s.Trim().Normalize(NormalizationForm.FormD))
			if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
		return sb.ToString();
	}

	private static string Descricao(string id) => Jandirus.Core.Items.CatalogoDeItens.Get(id)?.Descricao ?? "";

	/// <summary>O NOME desta oferta ja diz o termo? (Quem so a descricao acha, nao.)</summary>
	private static bool ONomeDiz(GameClient cli, string id, string termo) =>
		Normal(cli.Catalogo.First(o => o.Id == id).Nome).Contains(Normal(termo), StringComparison.Ordinal);

	/// <summary>Os ids das ofertas que o termo acha: no nome ou na descricao. Vazio acha todas.</summary>
	private static HashSet<string> Esperados(GameClient cli, string termo)
	{
		string t = Normal(termo);
		return [.. cli.Catalogo.Where(o => t.Length == 0 || Normal(o.Nome).Contains(t, StringComparison.Ordinal)
										   || Normal(Descricao(o.Id)).Contains(t, StringComparison.Ordinal)).Select(o => o.Id)];
	}

	// =====================================================================
	// LER A TELA
	// =====================================================================
	private static Control? Pagina(MenuJogo menu) => menu.PaginaDeTeste("Tech");

	private static List<PanelContainer> CardsNaTela(MenuJogo menu) =>
		Pagina(menu) is { } pg
			? [.. Todos(pg).OfType<PanelContainer>().Where(c => c.HasMeta("cartao") && c.GetMeta("cartao").AsString() == "oferta" && c.IsVisibleInTree())]
			: [];

	private static HashSet<string> IdsNaTela(MenuJogo menu) => [.. CardsNaTela(menu).Select(c => c.GetMeta("id").AsString())];

	private static string Lista(MenuJogo menu)
	{
		List<string> nomes = [.. CardsNaTela(menu).Select(c => c.GetMeta("titulo").AsString())];
		return nomes.Count > 6 ? $"{string.Join(", ", nomes.Take(6))} e mais {nomes.Count - 6}" : string.Join(", ", nomes);
	}

	/// <summary>O titulo do catalogo (o `Secao` escreve em caixa alta).</summary>
	private static Label? Titulo(MenuJogo menu) =>
		Pagina(menu) is { } pg ? Todos(pg).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("CATÁLOGO DE CONSTRUÇÕES", StringComparison.Ordinal)) : null;

	/// <summary>O numero entre parenteses no titulo do catalogo, ou -1.</summary>
	private static int ContagemDoTitulo(MenuJogo menu)
	{
		string t = Titulo(menu)?.Text ?? "";
		int a = t.LastIndexOf('('), b = t.LastIndexOf(')');
		return a >= 0 && b > a && int.TryParse(t[(a + 1)..b], out int n) ? n : -1;
	}

	private static PanelContainer? CartaoTitulado(MenuJogo menu, string comeco) =>
		Pagina(menu) is { } pg
			? Todos(pg).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("cartao") && c.GetMeta("cartao").AsString() == "secao"
																	  && c.GetMeta("titulo").AsString().StartsWith(comeco, StringComparison.Ordinal))
			: null;

	private static bool CartaoDaBuscaNaTela(MenuJogo menu) => CartaoTitulado(menu, "Busca") is { } c && c.IsVisibleInTree();

	private static string NotaDaBusca(MenuJogo menu) =>
		CartaoTitulado(menu, "Busca") is { } c ? string.Join(" | ", Todos(c).OfType<Label>().Select(l => l.Text)) : "";

	private static LineEdit? Campo(MenuJogo menu) =>
		Pagina(menu) is { } pg ? Todos(pg).OfType<LineEdit>().FirstOrDefault(l => l.PlaceholderText == MenuJogo.DicaDaBuscaDaTech) : null;

	/// <summary>A arvore inteira, SEM os nos que ja vao embora (a pagina velha fica na arvore ate o fim do quadro da remontagem).</summary>
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f.IsQueuedForDeletion()) continue;
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	/// <summary>O CLIQUE na caixa, com o mouse de verdade (o campo pega o foco no clique).</summary>
	private async System.Threading.Tasks.Task<bool> Focar(MenuJogo menu)
	{
		if (Campo(menu) is not { } campo) return false;
		if (Ancestral<ScrollContainer>(campo) is { } rol) rol.EnsureControlVisible(campo);
		await Quadros(2);
		Vector2 p = campo.GetGlobalRect().GetCenter();
		GetViewport().PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = p, GlobalPosition = p, ButtonMask = MouseButtonMask.Left }, true);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = p, GlobalPosition = p }, true);
		await Quadros(2);
		return Campo(menu)?.HasFocus() == true;
	}

	/// <summary>
	/// AS TECLAS DE VERDADE, uma por quadro: o evento de tecla empurrado no viewport chega ao campo com o foco (e so a ele). `\b` e o
	/// backspace. Com `medidas`, guarda quanto o evento custou e quanto durou o quadro seguinte (o arranjo da grade e o desenho).
	/// </summary>
	private async Tarefa Teclar(MenuJogo menu, string texto, List<(double Sync, double Quadro)>? medidas = null)
	{
		foreach (char ch in texto)
		{
			Key k = ch == '\b' ? Key.Backspace
				  : ch is >= 'a' and <= 'z' ? (Key)((long)Key.A + (ch - 'a'))
				  : ch is >= 'A' and <= 'Z' ? (Key)((long)Key.A + (ch - 'A'))
				  : Key.None;
			long unicode = ch == '\b' ? 0 : ch;
			ulong t0 = Time.GetTicksUsec();
			GetViewport().PushInput(new InputEventKey { Keycode = k, PhysicalKeycode = k, Unicode = unicode, Pressed = true });
			double sync = (Time.GetTicksUsec() - t0) / 1000.0;
			GetViewport().PushInput(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
			ulong q0 = Time.GetTicksUsec();
			await Quadros(1);
			medidas?.Add((sync, (Time.GetTicksUsec() - q0) / 1000.0));
		}
		await Quadros(2);
	}

	/// <summary>Apaga a caixa pelo teclado: o cursor no fim e um backspace por letra.</summary>
	private async Tarefa Apagar(MenuJogo menu)
	{
		if (Campo(menu) is not { } campo) return;
		campo.CaretColumn = campo.Text.Length;
		await Teclar(menu, new string('\b', campo.Text.Length));
	}

	/// <summary>Troca o termo inteiro: apaga e escreve, pelo teclado.</summary>
	private async Tarefa Escrever(MenuJogo menu, string termo)
	{
		if (Campo(menu)?.HasFocus() != true && !await Focar(menu)) { Conferir(false, "(montagem) a caixa recebe o foco no clique"); return; }
		await Apagar(menu);
		await Teclar(menu, termo);
	}

	private static double Media(List<(double Sync, double Quadro)> l, Func<(double Sync, double Quadro), double> f) => l.Count == 0 ? 0 : l.Average(f);

	/// <summary>
	/// ESPERA A PAGINA ASSENTAR: a primeira visita da Tech pede o catalogo, e a resposta (e a carteira atras dela) remonta a pagina -- uma
	/// remontagem legitima que cairia no meio da conta do T2r. Assentada = 0,8 s seguidos sem remontar (no maximo 8 s).
	/// </summary>
	private async Tarefa Assentar(MenuJogo menu)
	{
		await Quadros(3);
		double fim = Agora + 8.0, quieta = Agora + 0.8;
		int r = menu.RemontagensDaAbaDeTeste("Tech");
		while (Agora < fim && Agora < quieta)
		{
			await Quadros(1);
			int agora = menu.RemontagensDaAbaDeTeste("Tech");
			if (agora != r) { r = agora; quieta = Agora + 0.8; }
		}
	}

	private static T? Ancestral<T>(Node n) where T : Node
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is T t) return t;
		return null;
	}

	/// <summary>A foto da tela, com a caixa e o comeco do catalogo a vista.</summary>
	private async Tarefa Fotografar(MenuJogo menu, string destino)
	{
		if (Titulo(menu) is { } t && Ancestral<ScrollContainer>(t) is { } rol)
		{
			rol.ScrollVertical = 0;
			await Quadros(1);
			rol.EnsureControlVisible(t);
		}
		await Quadros(3);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		Nota("foto salva em " + caminho);
	}

	private static double Agora => Time.GetTicksMsec() / 1000.0;

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Agora + s;
		while (Agora < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Agora + segundos;
		while (Agora < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fechar()
	{
		DesligarOsDefeitos();
		foreach (string l in _linhas) GD.Print("[dbuscatech] " + l);
		GD.Print($"[dbuscatech] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dbuscatech] ===== TUDO OK =====" : $"[dbuscatech] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dbuscatech]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
