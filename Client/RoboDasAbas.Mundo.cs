using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Mundo (World) da `--diagabas` (F11). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ============================ O QUE ELA PROVA, E COMO ============================
/// A faixa do lugar pelo `ValorDesenhado`; os cartoes Céu e Clima pelo metadado; o disco da lua
/// pelo NODE (o proprio `LuaNoCeu`, o widget do HUD) e pela regra de que ele conta a MESMA verdade
/// que o do HUD -- visivel se e so se a lua esta no ceu; os contratos das bancadas vizinhas (os
/// botoes "Ver os desejos" e "Conquistar planeta", nenhuma palavra de nave em rotulo nenhum).
///
/// E COM O CONTRA-EXEMPLO VIVO: a bancada nasce ao meio-dia (`--horateste 0.5`, lua abaixo do
/// horizonte: disco escondido nas duas telas). O verb `admin_lua_cheia` adianta o relogio do ceu
/// ate a proxima noite de lua cheia -- o disco tem que APARECER nas duas telas ao mesmo tempo e a
/// linha "Lua" tem que dizer LUA CHEIA (no céu); `admin_meio_dia` traz o dia de volta e o disco some
/// de novo. A foto da noite mede o disco no PIXEL: mais de um terco do retangulo dele mais claro que
/// a chapa do cartao.
/// ==================================================================================
/// </summary>
public partial class RoboDasAbas
{
	// =====================================================================
	// AS REGRAS -- puras, pra rodada de injecao chamar as MESMAS
	// =====================================================================
	/// <summary>O disco do menu e o do HUD contam a mesma verdade, e ela e a do ceu.</summary>
	private static bool DuasTelasConcordam(bool discoDoMenu, bool discoDoHud, bool luaNoCeu) =>
		discoDoMenu == discoDoHud && discoDoMenu == luaNoCeu;

	/// <summary>
	/// AS PALAVRAS QUE NAO PODEM SOBRAR EM ROTULO NENHUM -- a mesma lista da `--diagembarque`
	/// (`RoboDeEmbarque.PalavrasDeNave`), copiada porque ela e privada de la e o que se afirma aqui e
	/// "aquela bancada continua verde nesta aba".
	/// </summary>
	private static readonly string[] PalavrasDeNaveNasAbas =
		["nave", "leme", "desembarc", "recondicion", "lançar", "lancar", "pilotar", "embarc"];

	private static bool SemPalavraDeNaveNosRotulos(IEnumerable<string> rotulos) =>
		!rotulos.Any(r => PalavrasDeNaveNasAbas.Any(p => r.Contains(p, StringComparison.OrdinalIgnoreCase)));

	/// <summary>Que fracao de um retangulo da foto e mais clara que um limiar de luminancia.</summary>
	private static float FracaoClara(Image? img, Rect2I r, float limiar)
	{
		if (img == null) return 0;
		int claros = 0, total = 0;
		int x1 = Math.Min(img.GetWidth(), r.Position.X + r.Size.X);
		int y1 = Math.Min(img.GetHeight(), r.Position.Y + r.Size.Y);
		for (int y = Math.Max(0, r.Position.Y); y < y1; y++)
			for (int x = Math.Max(0, r.Position.X); x < x1; x++)
			{
				Color c = img.GetPixel(x, y);
				if (0.299f * c.R + 0.587f * c.G + 0.114f * c.B > limiar) claros++;
				total++;
			}
		return total == 0 ? 0 : claros / (float)total;
	}

	private static float Luminancia(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

	/// <summary>O disco do menu (o `LuaNoCeu` dentro do cartao Céu), ou nulo.</summary>
	private static LuaNoCeu? DiscoDoMenu(Control pg) =>
		CartaoComTitulo(pg, "Céu") is { } ceu ? Todos(ceu).OfType<LuaNoCeu>().FirstOrDefault() : null;

	/// <summary>Os textos de todos os botoes e rotulos visiveis de uma pagina.</summary>
	private static List<string> RotulosEBotoes(Control pg) =>
		[.. Todos(pg).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text),
		 .. Rotulos(pg).Select(l => l.Text)];

	// =====================================================================
	// F11 -- WORLD
	// =====================================================================
	private async System.Threading.Tasks.Task F11_Mundo(MenuJogo menu, GameClient cli)
	{
		Nota("--- F11: Mundo: a faixa do lugar, Céu (com o disco do HUD) e Clima, os blocos de acao em cartoes ---");
		cli.Falou += EcoarFalaDoJogo;   // os verbs de ceu respondem pelo chat; ver `EcoarFalaDoJogo`
		try { await F11_MundoDeVerdade(menu, cli); }
		finally { cli.Falou -= EcoarFalaDoJogo; }
	}

	private async System.Threading.Tasks.Task F11_MundoDeVerdade(MenuJogo menu, GameClient cli)
	{
		menu.IrPara("World");
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
		Control? pg = menu.PaginaDeTeste("World");
		Checa("a pagina de World existe e esta na tela", pg is { Visible: true });
		if (pg == null) return;

		// ---------------- 1. O LUGAR E OS DOIS CARTOES ----------------
		// O BERCO DO SAIYAJIN VARIA (Terra ou Vegeta, sorteado no nascimento: a primeira rodada nasceu
		// em Vegeta e a foto de "antes" na Terra), entao o que se afirma e "a faixa diz O NOME DA ZONA em
		// que o cliente esta", e nao um planeta cravado.
		Checa("PRECONDICAO: a bancada esta num PLANETA (senao o dominio nem se desenha)", Jandirus.Core.World.Espaco.EhPlaneta(cli.Zone), cli.Zone.Name);
		string? lugar = menu.ValorDesenhado("World", "Lugar");
		Checa($"a FAIXA 'Lugar' diz o nome da zona do cliente ('{cli.Zone.Name}')",
			  lugar != null && cli.Zone.Name.Length > 0 && lugar.StartsWith(cli.Zone.Name, StringComparison.Ordinal), lugar ?? "(nula)");
		Checa("...com a hora e o ciclo do dia na legenda", lugar != null && lugar.Contains("h)", StringComparison.Ordinal) && lugar.Contains("dia de", StringComparison.Ordinal), lugar ?? "(nula)");

		PanelContainer? ceu = CartaoComTitulo(pg, "Céu");
		PanelContainer? clima = CartaoComTitulo(pg, "Clima");
		Checa("os cartoes 'Céu' e 'Clima' existem, lado a lado (mesma linha da grade)",
			  ceu != null && clima != null && Math.Abs(ceu.GetGlobalRect().Position.Y - clima.GetGlobalRect().Position.Y) < 2,
			  $"ceu {ceu?.GetGlobalRect()} clima {clima?.GetGlobalRect()}");
		Checa("o cartao Céu tem as linhas 'Lua' e 'Lua cheia'",
			  ceu != null && DentroDoCartao(LinhaEscrita(pg, "Lua"), ceu) && DentroDoCartao(LinhaEscrita(pg, "Lua cheia"), ceu),
			  $"Lua='{menu.ValorDesenhado("World", "Lua")}' Lua cheia='{menu.ValorDesenhado("World", "Lua cheia")}'");
		Checa("o cartao Clima tem a linha 'Clima' e o que pode cair em pilulas",
			  clima != null && DentroDoCartao(LinhaEscrita(pg, "Clima"), clima) && PilulasEm(clima).Count > 0,
			  clima == null ? "sem cartao" : $"Clima='{menu.ValorDesenhado("World", "Clima")}' pode cair [{string.Join("|", PilulasEm(clima))}]");

		// ---------------- 2. O DISCO DA LUA: o mesmo node do HUD, a mesma verdade ----------------
		LuaNoCeu? lua = DiscoDoMenu(pg);
		Checa("o disco da lua (o proprio `LuaNoCeu` do HUD) esta dentro do cartao Céu", lua != null);
		bool noCeu = World.Instancia?.Ceu?.LuaNoCeu ?? false;
		bool hud = Hud.Instancia?.Lua.Visible ?? false;
		// A LUA AO MEIO-DIA NAO E UM FATO, E UMA FASE: a nova nasce e se poe COM o sol (esta no ceu ao
		// meio-dia), a cheia esta do lado oposto (abaixo). A primeira versao afirmava "abaixo do horizonte"
		// e ficou verde por sorte de fase na rodada da frente -- e vermelha na primeira rodada do repo
		// principal, com "lua nova (no céu)". O que se afirma e o que vale em qualquer fase: a LINHA diz
		// a mesma coisa que o ceu do cliente. O contra-exemplo de verdade vem logo abaixo, pelo relogio
		// (`admin_lua_cheia` / `admin_meio_dia`), que e deterministico.
		string linhaDaLua = menu.ValorDesenhado("World", "Lua") ?? "";
		Checa("a linha 'Lua' diz o que o ceu do cliente diz: '(no céu)' com a lua no ceu, '(abaixo do horizonte)' sem",
			  noCeu ? linhaDaLua.Contains("(no céu)", StringComparison.Ordinal)
					: linhaDaLua.Contains("(abaixo do horizonte)", StringComparison.Ordinal) || linhaDaLua.Contains("não tem lua", StringComparison.Ordinal),
			  $"{linhaDaLua} | ceu.LuaNoCeu {noCeu}");
		Checa("o disco do menu conta a MESMA verdade que o do HUD: escondido nas duas telas com a lua abaixo do horizonte",
			  lua != null && DuasTelasConcordam(lua.Visible, hud, noCeu), $"menu {lua?.Visible} hud {hud} ceu.LuaNoCeu {noCeu}");

		// ---------------- 3. OS CONTRATOS DAS BANCADAS VIZINHAS ----------------
		Checa("contrato --diagnav/--diagembarque: os botoes 'Ver os desejos' e 'Conquistar planeta' continuam na aba World",
			  Botao(pg, "Ver os desejos") != null && Botao(pg, "Conquistar planeta") != null);
		Checa("...e os blocos de dominio, esferas e pedido sao CARTOES",
			  CartaoComTitulo(pg, "Domínio planetário") != null && CartaoComTitulo(pg, "Esferas do Dragão") != null
			  && CartaoComTitulo(pg, "O pedido ao dragão") != null);
		Checa("...com a caixa do pedido (LineEdit) dentro do cartao do pedido",
			  CartaoComTitulo(pg, "O pedido ao dragão") is { } ped && Todos(ped).OfType<LineEdit>().Any());
		List<string> textos = RotulosEBotoes(pg);
		Checa("contrato --diagembarque: nenhum rotulo nem botao da aba fala de nave, leme, lancar, pilotar ou embarcar",
			  SemPalavraDeNaveNosRotulos(textos), string.Join(" | ", textos.Where(t => !SemPalavraDeNaveNosRotulos([t]))));
		Checa("sem berco destruido, nao ha o cartao de destaque 'Refúgio'",
			  !cli.RefugioPrecisa && CartaoComTitulo(pg, "Refúgio") == null);

		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? f1 = await Foto();
		await Guardar("world-01-meio-dia", f1);
		ChapaDoCartaoNaPaleta("a chapa do cartao Céu e a clara do tema (Tema.PainelClaro)", f1, ceu, Tema.PainelClaro);

		(bool igual, int nodes) = await RemontaIgual(menu, pg);
		Checa("DETERMINISMO: duas montagens do mesmo ceu dao a MESMA arvore de nodes", igual, $"{nodes} nodes");

		// ============================ QUANTAS VEZES A PAGINA REMONTA SOZINHA ============================
		// So MEDIDO, e nao cobrado: a assinatura da World mora em `MenuJogo.cs` (compartilhado, fora
		// desta frente) e leva o "quanto falta pra lua cheia" em SEGUNDOS inteiros (`AssinaturaDoCeu`),
		// enquanto o texto desenhado e em MINUTOS -- a suspeita era uma remontagem por segundo, que
		// recriaria a caixa do pedido vazia (e sem foco) na mao de quem digita. MEDIDO em 2026-09-03:
		// 1 remontagem em 3 s parado na aba, ou seja a suspeita NAO se confirmou nesta rodada; a linha
		// fica pra o dia em que o numero subir, e o conserto -- assinar o TEXTO da linha "Lua cheia" --
		// e de la, nao daqui.
		// ============================================================================================
		int remontagensAntes = menu.RemontagensDeTeste;
		await Segundos(3.0);
		Nota($"    remontagens da World em 3 s parado na aba: {menu.RemontagensDeTeste - remontagensAntes}"
			 + "   (0 ou 1 = a assinatura segura; 3 = ela esta em segundos e remonta por segundo)");

		// ---------------- 4. O CONTRA-EXEMPLO VIVO: a noite de lua cheia ----------------
		// `admin_lua_cheia` adianta o relogio do ceu ate a proxima noite de lua cheia; o cliente chega
		// na mesma lua sozinho (o ceu e funcao pura do tempo). As duas telas tem que acender juntas.
		cli.SendVerbo("admin_lua_cheia");
		bool noite = await Ate(() => World.Instancia?.Ceu is { LuaNoCeu: true, Cheia: true }, 12);
		Checa("admin_lua_cheia: o ceu do cliente virou lua cheia NO CEU", noite,
			  $"fase {World.Instancia?.Ceu?.Fase} altura {World.Instancia?.Ceu?.Altura:0.00}");
		if (noite)
		{
			menu.ForcarRedesenho();
			await Quadros(3);
			lua = DiscoDoMenu(pg);
			hud = Hud.Instancia?.Lua.Visible ?? false;
			Checa("...e o disco APARECE no cartao Céu e no HUD ao mesmo tempo",
				  lua != null && DuasTelasConcordam(lua.Visible, hud, true), $"menu {lua?.Visible} hud {hud}");
			string? linhaLua = menu.ValorDesenhado("World", "Lua");
			Checa("...a linha 'Lua' diz LUA CHEIA (no céu)",
				  linhaLua != null && linhaLua.Contains("LUA CHEIA", StringComparison.Ordinal) && linhaLua.Contains("(no céu)", StringComparison.Ordinal),
				  linhaLua ?? "(nula)");
			Checa("...e 'Lua cheia' diz AGORA", menu.ValorDesenhado("World", "Lua cheia") == "AGORA", menu.ValorDesenhado("World", "Lua cheia") ?? "(nula)");

			ceu = CartaoComTitulo(pg, "Céu");
			await RolarAteVer(ceu);
			Image? f2 = await Foto();
			await Guardar("world-02-lua-cheia", f2);
			TextureRect? disco = lua != null ? Todos(lua).OfType<TextureRect>().FirstOrDefault(t => t.Name == "Disco") : null;
			if (disco != null)
			{
				Rect2I r = Caixa(disco.GetGlobalRect(), 2);
				float limiar = Luminancia(Tema.PainelClaro) + 0.15f;
				float clara = FracaoClara(f2, r, limiar);
				ChecaNoPixel("o disco esta PINTADO na foto: mais de um terco do retangulo dele e mais claro que a chapa do cartao",
							 f2 != null, clara > 0.33f, $"{clara * 100:0}% claro em {r} (limiar de luminancia {limiar:0.00})");
				// CONTRA-EXEMPLO NA MESMA FOTO: um pedaco da chapa do cartao, longe do disco, nao e claro.
				Rect2I chapa = new((int)ceu!.GetGlobalRect().Position.X + 4, (int)ceu.GetGlobalRect().Position.Y + 4, 30, 6);
				ChecaNoPixel("CONTRA-EXEMPLO na mesma foto: a chapa do cartao ao lado do disco NAO e clara",
							 f2 != null, FracaoClara(f2, chapa, limiar) < 0.2f, $"{FracaoClara(f2, chapa, limiar) * 100:0}% claro em {chapa}");
			}
			else Checa("o disco (TextureRect 'Disco') existe dentro do LuaNoCeu", false);
		}

		// ---------------- 5. E O DIA DE VOLTA ----------------
		cli.SendVerbo("admin_meio_dia");
		bool dia = await Ate(() => World.Instancia?.Ceu is { LuaNoCeu: false }, 12);
		menu.ForcarRedesenho();
		await Quadros(3);
		lua = DiscoDoMenu(pg);
		hud = Hud.Instancia?.Lua.Visible ?? false;
		Checa("admin_meio_dia: a lua volta pra baixo do horizonte e o disco some das DUAS telas",
			  dia && lua != null && DuasTelasConcordam(lua.Visible, hud, false), $"dia {dia} menu {lua?.Visible} hud {hud}");

		// ---------------- 6. AS INJECOES ----------------
		Injeta("a regra das duas telas reprova o menu aceso com o HUD apagado", !DuasTelasConcordam(true, false, true));
		Injeta("...e reprova as duas acesas com a lua abaixo do horizonte", !DuasTelasConcordam(true, true, false));
		Injeta("a regra de nave reprova 'Lançar nave' e 'Pilotar'", !SemPalavraDeNaveNosRotulos(["Ver os desejos", "Lançar nave"]) && !SemPalavraDeNaveNosRotulos(["Pilotar"]));
		Injeta("...e aceita a lista de hoje (senao a linha de cima nao provaria nada)", SemPalavraDeNaveNosRotulos(["Ver os desejos", "Conquistar planeta", "Coletar tributo"]));
	}
}
