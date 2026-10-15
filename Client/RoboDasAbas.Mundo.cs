using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Mundo (World) da `--diagabas` (F11). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ============================ O QUE ELA PROVA, E COMO ============================
/// A faixa do lugar pelo `ValorDesenhado`; o cartao do MAPA (o quadro, o nome e o tamanho do mundo e as
/// coordenadas do corpo); os cartoes Céu e Clima pelo metadado; o disco da lua
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
	/// <summary>
	/// O disco do menu e o do HUD contam a mesma verdade, e ela e a do ceu -- COM UMA DIFERENCA DE PROPOSITO:
	/// sob teto o do HUD some (de dentro de casa nao se ve o ceu: `LuaNoCeu.Aplicar`), e o do menu continua,
	/// porque a ficha do planeta mostra a lua do LUGAR, e nao a que se ve dali.
	///
	/// A REGRA NASCEU SEM O TETO e ficou vermelha no dia em que o interior ganhou ceu proprio: o berco da Terra
	/// e DENTRO do Banco, o robo nasce sob teto e o HUD esconde a lua cheia com razao. Em 2026-10-10 a mesma
	/// linha vermelha apareceu no log de cinco sessoes, e cada uma culpou outra coisa (o clima do dia, o modo de
	/// um processo so) -- porque o detalhe da checagem nao dizia que havia teto. Agora diz.
	/// </summary>
	private static bool DuasTelasConcordam(bool discoDoMenu, bool discoDoHud, bool luaNoCeu, bool sobTeto = false) =>
		discoDoMenu == luaNoCeu && discoDoHud == (luaNoCeu && !sobTeto);

	/// <summary>O corpo do robo esta sob teto agora? E o mesmo fato que o HUD passa pro disco dele.</summary>
	private static bool SobTeto => World.Instancia?.EuEstouSobTeto ?? false;

	/// <summary>
	/// AS PALAVRAS QUE NAO PODEM SOBRAR EM ROTULO NENHUM -- a mesma lista da `--diagembarque`
	/// (`RoboDeEmbarque.PalavrasDeNave`), copiada porque ela e privada de la e o que se afirma aqui e
	/// "aquela bancada continua verde nesta aba".
	/// </summary>
	private static readonly string[] PalavrasDeNaveNasAbas =
		["nave", "leme", "desembarc", "recondicion", "lançar", "lancar", "pilotar", "embarc"];

	private static bool SemPalavraDeNaveNosRotulos(IEnumerable<string> rotulos) =>
		!rotulos.Any(r => PalavrasDeNaveNasAbas.Any(p => r.Contains(p, StringComparison.OrdinalIgnoreCase)));

	/// <summary>
	/// O CARTAO DO PEDIDO, depois de 2026-10-10: nenhuma caixa de texto (o id digitado saiu: o desejo se escolhe
	/// na janela de desejos), o botao "Ver os desejos" de pe, e nenhum dos botoes que mandavam o que a caixa
	/// tinha escrito.
	/// </summary>
	private static bool CartaoDoPedidoSemCaixa(int caixas, IEnumerable<string> botoes) =>
		caixas == 0 && botoes.Contains("Ver os desejos")
		&& !botoes.Any(b => b is "PEDIR" or "PRONUNCIAR" or "ACEITO O PREÇO");

	/// <summary>"Ver os desejos" pede o MENU ao servidor -- e nao a lista no chat, que era o `db_desejar` sem argumento.</summary>
	private static bool PedeAJanelaDeDesejos(string? verbo, string? arg) => verbo == "db_menu" && arg == "";

	/// <summary>
	/// "Derrubar a estátua" existe se e so se este mundo TEM uma estatua (a que chegou no `S2C.Esferas`): botao
	/// que nunca poderia funcionar dali nao aparece, e onde ha o que derrubar ele nao pode faltar.
	/// </summary>
	private static bool DerrubarSoOndeHaEstatua(bool temOBotao, bool haEstatuaAqui) => temOBotao == haEstatuaAqui;

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
		// O NOME QUE SE MOSTRA: o batizado, se alguem batizou este planeta; senao o da zona (ver `NomeDoLugar`).
		Checa($"a FAIXA 'Lugar' diz o nome do lugar do cliente ('{cli.NomeDoLugar}')",
			  lugar != null && cli.NomeDoLugar.Length > 0 && lugar.StartsWith(cli.NomeDoLugar, StringComparison.Ordinal), lugar ?? "(nula)");
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

		// ---------------- 1b. O MAPA DO LUGAR (dono, 2026-10-10) ----------------
		// O contrato do cartao novo: o quadro do mapa, o nome e o tamanho do mundo, e as COORDENADAS do corpo --
		// que sao as do cliente (a celula dos pes), e nao um texto parado. O desenho, o custo de quadro e os
		// outros lugares (gerado, espaco) sao da `--diagmapa`.
		PanelContainer? cartaoDoMapa = CartaoComTitulo(pg, "Mapa");
		MapaDoLugarNaTela? quadroDoMapa = cartaoDoMapa == null ? null : Todos(cartaoDoMapa).OfType<MapaDoLugarNaTela>().FirstOrDefault();
		Checa("o cartao 'Mapa' existe, com o quadro do mapa dentro e ANTES dos cartoes Céu e Clima",
			  cartaoDoMapa != null && quadroDoMapa != null && ceu != null && cartaoDoMapa.GetGlobalRect().Position.Y < ceu.GetGlobalRect().Position.Y,
			  $"mapa {cartaoDoMapa?.GetGlobalRect()} ceu {ceu?.GetGlobalRect()}");
		Checa("...com as linhas 'Mundo' (o nome do lugar), 'Tamanho' e 'Coordenadas' dentro dele",
			  cartaoDoMapa != null && DentroDoCartao(LinhaEscrita(pg, "Mundo"), cartaoDoMapa) && DentroDoCartao(LinhaEscrita(pg, "Tamanho"), cartaoDoMapa)
			  && DentroDoCartao(LinhaEscrita(pg, "Coordenadas"), cartaoDoMapa) && menu.ValorDesenhado("World", "Mundo") == cli.NomeDoLugar,
			  $"Mundo='{menu.ValorDesenhado("World", "Mundo")}' Tamanho='{menu.ValorDesenhado("World", "Tamanho")}' Coordenadas='{menu.ValorDesenhado("World", "Coordenadas")}'");
		bool mapaPronto = await Ate(() => quadroDoMapa is { EstadoDeTeste: MapasDosLugares.Estado.Pronto }, 8);
		Checa("o mapa deste planeta fica PRONTO, com o marcador dentro do retangulo do mapa",
			  mapaPronto && quadroDoMapa!.MarcadorDeTeste is { } marcador && quadroDoMapa.RetanguloDeTeste.Grow(1).HasPoint(marcador),
			  $"{quadroDoMapa?.EstadoDeTeste} marcador {quadroDoMapa?.MarcadorDeTeste} em {quadroDoMapa?.RetanguloDeTeste}");
		string coordenadasNaAba = menu.ValorDesenhado("World", "Coordenadas") ?? "";
		Vector2 corpoNoMundo = World.Instancia?.PosicaoLocal ?? Vector2.Zero;
		string coordenadasDoCorpo = Jandirus.Core.World.MapaDoLugar.Coordenadas(new Jandirus.Core.World.Vec2(corpoNoMundo.X, corpoNoMundo.Y));
		Checa("a linha 'Coordenadas' diz a celula do corpo do cliente (X ... · Y ...)",
			  coordenadasNaAba == coordenadasDoCorpo && coordenadasNaAba.StartsWith("X ", StringComparison.Ordinal), $"aba '{coordenadasNaAba}' corpo '{coordenadasDoCorpo}'");
		Injeta("a regra das coordenadas reprova a celula ao lado (um tile a leste da outro texto)",
			   Jandirus.Core.World.MapaDoLugar.Coordenadas(new Jandirus.Core.World.Vec2(corpoNoMundo.X + 32, corpoNoMundo.Y)) != coordenadasDoCorpo);

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
			  lua != null && DuasTelasConcordam(lua.Visible, hud, noCeu, SobTeto), $"menu {lua?.Visible} hud {hud} ceu.LuaNoCeu {noCeu} sob teto {SobTeto}");

		// ---------------- 3. OS CONTRATOS DAS BANCADAS VIZINHAS ----------------
		Checa("contrato --diagnav/--diagembarque: os botoes 'Ver os desejos' e 'Conquistar planeta' continuam na aba World",
			  Botao(pg, "Ver os desejos") != null && Botao(pg, "Conquistar planeta") != null);
		Checa("...e os blocos de dominio, esferas e pedido sao CARTOES",
			  CartaoComTitulo(pg, "Domínio planetário") != null && CartaoComTitulo(pg, "Esferas do Dragão") != null
			  && CartaoComTitulo(pg, "O pedido ao dragão") != null);

		// ============================ A CAIXA DO PEDIDO SAIU, DE PROPOSITO (2026-10-10) ============================
		// Esta linha cobrava "a caixa do pedido (LineEdit) dentro do cartao do pedido". O dono mandou tirar a
		// caixa: *"ao invocar o dragao das esferas, deveria aparecer um menu com varios desejos q vc pode escolher
		// ... e nao ter q abrir um menu e colocar um id de pedido"*. O desejo se escolhe na janela de desejos
		// (`MenuDeDesejos`, medida pela `--diagdesejos`), e o que se cobra daqui virou o contrario: o cartao NAO
		// tem caixa, e o botao que ficou pede a janela ao servidor.
		// ===========================================================================================================
		PanelContainer? pedido = CartaoComTitulo(pg, "O pedido ao dragão");
		List<string> botoesDoPedido = pedido == null ? []
			: [.. Todos(pedido).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text)];
		int caixasDoPedido = pedido == null ? -1 : Todos(pedido).OfType<LineEdit>().Count();
		Checa("...e o cartao do pedido NAO tem caixa de texto: fica 'Ver os desejos', sem 'PEDIR'",
			  pedido != null && CartaoDoPedidoSemCaixa(caixasDoPedido, botoesDoPedido),
			  $"{caixasDoPedido} caixa(s); botoes: {string.Join(" | ", botoesDoPedido)}");
		// A METADE COM ESTATUA (o botao presente, e o que ele abre) e da `--diagdesejos`, que ergue uma.
		bool haEstatuaAqui = cli.Esferas.Any(e => e.Tipo == Jandirus.Net.Protocol.CoisaDeEsfera.Estatua);
		Checa("...e 'Derrubar a estátua' so aparece num mundo que TEM estatua (aqui: " + (haEstatuaAqui ? "tem" : "nao tem") + ")",
			  DerrubarSoOndeHaEstatua(botoesDoPedido.Contains("Derrubar a estátua"), haEstatuaAqui),
			  $"estatua aqui: {haEstatuaAqui}; botoes: {string.Join(" | ", botoesDoPedido)}");

		// O QUE O BOTAO FAZ, pelo funil dos verbos, com o espiao ENGOLINDO (a bancada das abas nao abre janela
		// nenhuma: quem abre e anda a janela e a `--diagdesejos`).
		(string Cmd, string Arg)? saiu = null;
		Func<string, string, bool>? espiaoDeAntes = GameClient.EspiaoDeVerbos;
		GameClient.EspiaoDeVerbos = (c, a) => { saiu ??= (c, a); return false; };
		try { if (Botao(pg, "Ver os desejos") is { } ver) await Clicar(ver); }
		finally { GameClient.EspiaoDeVerbos = espiaoDeAntes; }
		Checa("'Ver os desejos' pede a JANELA ao servidor (`db_menu`), e nao a lista no chat (`db_desejar`)",
			  PedeAJanelaDeDesejos(saiu?.Cmd, saiu?.Arg), saiu is { } s ? $"{s.Cmd} '{s.Arg}'" : "nao saiu verbo nenhum");
		Checa("...e a janela de desejos existe no mundo, fechada (ela so abre quando o servidor responde)",
			  MenuDeDesejos.Instancia is { NaTela: false });

		List<string> textos = RotulosEBotoes(pg);
		Checa("contrato --diagembarque: nenhum rotulo nem botao da aba fala de nave, leme, lancar, pilotar ou embarcar",
			  SemPalavraDeNaveNosRotulos(textos), string.Join(" | ", textos.Where(t => !SemPalavraDeNaveNosRotulos([t]))));
		Checa("sem berco destruido, nao ha o cartao de destaque 'Refúgio'",
			  !cli.RefugioPrecisa && CartaoComTitulo(pg, "Refúgio") == null);

		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? f1 = await Foto();
		await Guardar("world-01-meio-dia", f1);

		// ---- A FRASE DO CEU NAO PODE NEGAR A TERRA NA TERRA (o segundo relato do dono) ----
		// "a hora e o ceu daqui nao sao os da Terra" saia em TODO planeta -- inclusive na Terra. A frase
		// depende de onde se esta; o que se cobra e a coerencia com a zona do cliente.
		bool naTerra = string.Equals(cli.Zone.Name, "Earth", StringComparison.OrdinalIgnoreCase);
		List<string> frasesDoCeu = Rotulos(pg).Select(l => l.Text).Where(t => t.Contains("Terra", StringComparison.Ordinal)).ToList();
		Checa(naTerra ? "na TERRA a nota do ceu fala do dia da Terra e NAO diz 'não são os da Terra'"
					  : "fora da Terra a nota do ceu diz que o dia daqui nao e o da Terra",
			  naTerra ? frasesDoCeu.Any(t => t.StartsWith("Este é o dia e o céu da Terra")) && !frasesDoCeu.Any(t => t.Contains("não são os da Terra"))
					  : frasesDoCeu.Any(t => t.Contains("não são os da Terra")),
			  $"zona={cli.Zone.Name}: {string.Join(" | ", frasesDoCeu)}");
		// O CARTAO DO MAPA EMPURROU O CEU PRA BAIXO: a foto do topo da pagina (a `world-01`) pega o mapa, e o ceu
		// pode ficar cortado pela rolagem. A chapa dele se mede numa foto propria, com ele rolado pra dentro da tela.
		// A PAGINA SE REFAZ SOZINHA quando o texto do ceu muda (o decimo de hora, o minuto da lua cheia), e o cartao
		// guardado la em cima pode ser de uma montagem que ja morreu -- foi assim que esta bancada estourou com
		// `ObjectDisposedException` em 2026-10-11. Pega-se o cartao de novo, e a foto so vale se ele continua vivo
		// depois dela (senao a pagina se refez no meio, e mede-se outra vez).
		Image? fotoDoCeu = null;
		for (int vez = 0; vez < 3; vez++)
		{
			ceu = CartaoComTitulo(pg, "Céu");
			await RolarAteVer(ceu);
			fotoDoCeu = await Foto();
			if (ceu != null && GodotObject.IsInstanceValid(ceu)) break;
		}
		ChapaDoCartaoNaPaleta("a chapa do cartao Céu e a clara do tema (Tema.PainelClaro)", fotoDoCeu,
							  ceu != null && GodotObject.IsInstanceValid(ceu) ? ceu : null, Tema.PainelClaro);
		if (Ancestral<ScrollContainer>(pg) is { } aoTopo) aoTopo.ScrollVertical = 0;

		// O CEU ANDA, e a regua do determinismo compara DUAS FOTOS da arvore com uma remontagem FORCADA no meio. Ela
		// so vale se a forcada foi a unica: uma automatica no intervalo quer dizer que o texto do ceu mudou (e a
		// arvore, com razao). Nesse caso mede-se de novo -- o que se cobra e "mesmo estado, mesma arvore".
		bool igual = false;
		int nodes = 0, automaticas = 0;
		for (int vez = 0; vez < 4; vez++)
		{
			int antesDaMedida = menu.RemontagensDeTeste;
			(igual, nodes) = await RemontaIgual(menu, pg);
			automaticas = menu.RemontagensDeTeste - antesDaMedida - 1;   // -1: a forcada
			if (automaticas <= 0) break;
		}
		Checa("DETERMINISMO: duas montagens do mesmo ceu dao a MESMA arvore de nodes", igual && automaticas <= 0,
			  $"{nodes} nodes, {Math.Max(automaticas, 0)} remontagem(ns) automatica(s) no meio da medida");

		// ============================ QUANTAS VEZES A PAGINA REMONTA SOZINHA ============================
		// A assinatura da World (`MenuJogo.AssinaturaDoCeu`) levava o "quanto falta pra lua cheia" em SEGUNDOS
		// inteiros, enquanto a linha desenhada e em MINUTOS: a pagina inteira se refazia UMA VEZ POR SEGUNDO. Em
		// 2026-09-03 isto era so uma suspeita medida (1 remontagem em 3 s); em 2026-10-11 a medida deu 3 em 3 s, a
		// bancada estourou segurando um cartao morto, e quem digitasse no batismo do planeta perderia o texto a cada
		// segundo. O conserto e o que a nota antiga ja dizia: assinar o TEXTO da linha (`MenuJogo.CheiaNaAssinatura`).
		// ============================================================================================
		Checa("a assinatura da World leva o TEXTO da linha 'Lua cheia': dois instantes do mesmo minuto dao o MESMO pedaco",
			  MenuJogo.CheiaNaAssinatura(3599.4) == MenuJogo.CheiaNaAssinatura(3598.2)
			  && MenuJogo.CheiaNaAssinatura(3599.4) == MenuJogo.TextoDaLuaCheia(3599.4),
			  $"'{MenuJogo.CheiaNaAssinatura(3599.4)}' e '{MenuJogo.CheiaNaAssinatura(3598.2)}'");
		Checa("...e no ULTIMO minuto ela anda de segundo em segundo, como a linha ('AGORA' e 'nunca' sao pedacos proprios)",
			  MenuJogo.CheiaNaAssinatura(42.3) != MenuJogo.CheiaNaAssinatura(41.2) && MenuJogo.CheiaNaAssinatura(0.4) == "AGORA"
			  && MenuJogo.CheiaNaAssinatura(-1) != MenuJogo.CheiaNaAssinatura(0.4) && MenuJogo.CheiaNaAssinatura(-1).StartsWith("nunca", StringComparison.Ordinal),
			  $"'{MenuJogo.CheiaNaAssinatura(42.3)}' / '{MenuJogo.CheiaNaAssinatura(41.2)}' / '{MenuJogo.CheiaNaAssinatura(0.4)}' / '{MenuJogo.CheiaNaAssinatura(-1)}'");

		// AO VIVO, parado na aba. O TETO sai do relogio do lugar: na janela cabem as viradas de DECIMO DE HORA (o dia
		// do lugar dividido em 240) e mais UMA do minuto da lua. So ha o que medir com uma lua de contagem longa: sem
		// lua a contagem e fixa (e o defeito nao muda nada), e a menos de um minuto da cheia a linha anda por segundo
		// de proposito.
		const double JanelaDeRemontagem = 5.0;
		Jandirus.Core.World.RelogioDoPlaneta relogioDaqui = World.Instancia?.RelogioDoLugar ?? Jandirus.Core.World.RelogioDoPlaneta.Padrao;
		double decimoDeHora = relogioDaqui.SegundosPorDia / 240.0;
		double faltaPraCheia = World.Instancia is { } mundoDaqui ? Jandirus.Core.World.Ceu.SegundosAteACheia(relogioDaqui, mundoDaqui.TempoDoMundo) : -1;
		if (faltaPraCheia < 60 + 3 * JanelaDeRemontagem)
			_naoMedidos.Add($"World: quantas vezes a pagina se refaz sozinha (a lua cheia deste mundo esta a {faltaPraCheia:0} s -- sem contagem longa nao ha o que medir)");
		else
		{
			int teto = (int)Math.Ceiling(JanelaDeRemontagem / Math.Max(decimoDeHora, 0.5)) + 1;
			int antesDaJanela = menu.RemontagensDeTeste;
			await Segundos(JanelaDeRemontagem);
			int sozinhas = menu.RemontagensDeTeste - antesDaJanela;
			Checa($"parado na aba World por {JanelaDeRemontagem:0} s, a pagina se refaz no maximo {teto} vez(es) -- so quando o texto do ceu muda",
				  sozinhas <= teto, $"{sozinhas} remontagem(ns); um decimo de hora daqui dura {decimoDeHora:0.0} s");
			try
			{
				MenuJogo.CheiaEmSegundosNaAssinaturaDeTeste = true;
				antesDaJanela = menu.RemontagensDeTeste;
				await Segundos(JanelaDeRemontagem);
				int comDefeito = menu.RemontagensDeTeste - antesDaJanela;
				Injeta("(defeito injetado: a lua cheia em SEGUNDOS na assinatura) a pagina volta a se refazer a cada segundo, e a regua de cima REPROVA",
					   comDefeito > teto, $"{comDefeito} remontagem(ns) em {JanelaDeRemontagem:0} s (teto {teto})");
			}
			finally { MenuJogo.CheiaEmSegundosNaAssinaturaDeTeste = false; }
		}

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
			Checa("...e o disco APARECE no cartao Céu e no HUD ao mesmo tempo (no HUD, so a ceu aberto)",
				  lua != null && DuasTelasConcordam(lua.Visible, hud, true, SobTeto), $"menu {lua?.Visible} hud {hud} sob teto {SobTeto}");
			string? linhaLua = menu.ValorDesenhado("World", "Lua");
			Checa("...a linha 'Lua' diz LUA CHEIA (no céu)",
				  linhaLua != null && linhaLua.Contains("LUA CHEIA", StringComparison.Ordinal) && linhaLua.Contains("(no céu)", StringComparison.Ordinal),
				  linhaLua ?? "(nula)");
			Checa("...e 'Lua cheia' diz AGORA", menu.ValorDesenhado("World", "Lua cheia") == "AGORA", menu.ValorDesenhado("World", "Lua cheia") ?? "(nula)");

			// A PAGINA SE REFAZ NO MEIO DA FOTO (o caso da chapa do ceu, la em cima): o disco e o cartao lidos ANTES dos awaits
			// podem ser de uma montagem que ja morreu -- a regressao de 2026-10-15 estourou aqui com `ObjectDisposedException` no
			// `LuaNoCeu`. Os dois se pegam de novo a cada foto, a foto so vale se eles continuam vivos depois dela, e os
			// retangulos se leem ANTES do proximo await. A pagina se refaz quando o TEXTO do ceu muda: o decimo de hora e a forca
			// do clima em porcentagem inteira, que numa transicao de 45 s anda a cada ~0,45 s -- com a janela a ~4 quadros por
			// segundo (uma tentativa = ~7 quadros = ~1,7 s) as tres tentativas podem cair numa remontagem, e entao a medida diz
			// quantas houve e o que andou, em vez de culpar o disco.
			Image? f2 = null;
			bool vivos = false;
			int remontagensAntes = menu.RemontagensDeTeste;
			string ceuAntes = $"clima {World.Instancia?.TempoQueFaz?.Forca ?? 0:P0}, hora {(World.Instancia?.Ceu?.Hora ?? 0) * 24:00.0}";
			for (int vez = 0; vez < 3 && !vivos; vez++)
			{
				ceu = CartaoComTitulo(pg, "Céu");
				lua = DiscoDoMenu(pg);
				await RolarAteVer(ceu);
				f2 = await Foto();
				vivos = ceu != null && GodotObject.IsInstanceValid(ceu) && lua != null && GodotObject.IsInstanceValid(lua);
			}
			TextureRect? disco = vivos ? Todos(lua!).OfType<TextureRect>().FirstOrDefault(t => t.Name == "Disco") : null;
			Rect2? caixaDoDisco = disco?.GetGlobalRect();
			Rect2 caixaDoCeu = vivos ? ceu!.GetGlobalRect() : default;
			await Guardar("world-02-lua-cheia", f2);
			if (caixaDoDisco is { } cd)
			{
				Rect2I r = Caixa(cd, 2);
				float limiar = Luminancia(Tema.PainelClaro) + 0.15f;
				float clara = FracaoClara(f2, r, limiar);
				ChecaNoPixel("o disco esta PINTADO na foto: mais de um terco do retangulo dele e mais claro que a chapa do cartao",
							 f2 != null, clara > 0.33f, $"{clara * 100:0}% claro em {r} (limiar de luminancia {limiar:0.00})");
				// CONTRA-EXEMPLO NA MESMA FOTO: um pedaco da chapa do cartao, longe do disco, nao e claro.
				Rect2I chapa = new((int)caixaDoCeu.Position.X + 4, (int)caixaDoCeu.Position.Y + 4, 30, 6);
				ChecaNoPixel("CONTRA-EXEMPLO na mesma foto: a chapa do cartao ao lado do disco NAO e clara",
							 f2 != null, FracaoClara(f2, chapa, limiar) < 0.2f, $"{FracaoClara(f2, chapa, limiar) * 100:0}% claro em {chapa}");
			}
			else if (!vivos)
				Checa("a foto da lua cheia pegou o disco e o cartao Céu VIVOS (a pagina parou de se refazer em uma das 3 tentativas)", false,
					  $"{menu.RemontagensDeTeste - remontagensAntes} remontagem(ns) durante as fotos; {ceuAntes} -> clima "
					  + $"{World.Instancia?.TempoQueFaz?.Forca ?? 0:P0}, hora {(World.Instancia?.Ceu?.Hora ?? 0) * 24:00.0}");
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
			  dia && lua != null && DuasTelasConcordam(lua.Visible, hud, false, SobTeto), $"dia {dia} menu {lua?.Visible} hud {hud} sob teto {SobTeto}");

		// ---------------- 6. AS INJECOES ----------------
		Injeta("a regra das duas telas reprova o menu aceso com o HUD apagado (a ceu aberto)", !DuasTelasConcordam(true, false, true));
		Injeta("...e reprova as duas acesas com a lua abaixo do horizonte", !DuasTelasConcordam(true, true, false));
		Injeta("...e, SOB TETO, reprova o HUD aceso (de dentro de casa nao se ve o ceu)", !DuasTelasConcordam(true, true, true, sobTeto: true));
		Injeta("...e aceita, sob teto, o menu aceso com o HUD apagado (senao a de cima nao provaria nada)", DuasTelasConcordam(true, false, true, sobTeto: true));
		Injeta("a regra de nave reprova 'Lançar nave' e 'Pilotar'", !SemPalavraDeNaveNosRotulos(["Ver os desejos", "Lançar nave"]) && !SemPalavraDeNaveNosRotulos(["Pilotar"]));
		Injeta("...e aceita a lista de hoje (senao a linha de cima nao provaria nada)", SemPalavraDeNaveNosRotulos(["Ver os desejos", "Conquistar planeta", "Coletar tributo"]));
		Injeta("a regra do cartao do pedido reprova a caixa de texto de volta", !CartaoDoPedidoSemCaixa(1, ["Ver os desejos", "Derrubar a estátua"]));
		Injeta("...reprova o botao 'PEDIR' de volta", !CartaoDoPedidoSemCaixa(0, ["Ver os desejos", "PEDIR"]));
		Injeta("...e aceita o cartao de hoje, com e sem estatua no mundo",
			   CartaoDoPedidoSemCaixa(0, ["Ver os desejos", "Derrubar a estátua"]) && CartaoDoPedidoSemCaixa(0, ["Ver os desejos"]));
		Injeta("a regra do botao reprova 'Ver os desejos' religado ao verbo antigo (`db_desejar`)", !PedeAJanelaDeDesejos("db_desejar", ""));
		Injeta("a regra de derrubar reprova o botao num mundo SEM estatua (o que o cartao fazia: aparecia em todo planeta)", !DerrubarSoOndeHaEstatua(true, false));
		Injeta("...e reprova a falta dele num mundo que tem uma", !DerrubarSoOndeHaEstatua(false, true));
	}
}
