using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA WORLD -- onde voce esta (ceu, lua, clima) e o que se faz com o LUGAR: dominio planetario,
/// esferas do dragao e o pedido. Os blocos de dominio/refugio/esferas/pedido tambem servem a aba Nav.
///
/// ============================ A LINGUA DA LEARNING, NESTA ABA ============================
/// Ela era oito linhas de "rotulo ..... valor" e tres fileiras de botoes soltos (foto de antes:
/// `aba-07-world.png`). Agora abre com a FAIXA do lugar (o nome da zona grande; hora e ciclo do dia
/// na legenda), depois dois cartoes lado a lado -- CEU, com o MESMO disco de lua do HUD (`LuaNoCeu`)
/// e as linhas da lua, e CLIMA, com o tempo de agora e o que pode cair aqui em pilulas --, e entao
/// os blocos de acao (refugio, dominio, esferas, pedido), cada um no seu cartao com os botoes numa
/// grade de tres colunas. Os TEXTOS, verbos, argumentos, tooltips e a condicao espaco/planeta dos
/// botoes sao os de antes: as bancadas `--diagnav` e `--diagembarque` os acham pelo texto ("Ver os
/// desejos", "Conquistar planeta"). As notas longas encurtaram; o que saiu do texto foi pro tooltip
/// do botao a que dizia respeito.
/// ==========================================================================================
/// </summary>
public partial class MenuJogo
{
	private void Mundo()
	{
		GameClient? cli = GameClient.Instance;
		Jandirus.Core.World.RelogioDoPlaneta r = World.Instancia?.RelogioDoLugar
											 ?? Jandirus.Core.World.RelogioDoPlaneta.Padrao;
		Jandirus.Core.World.EstadoDoCeu? ceu = World.Instancia?.Ceu;

		// ============================ A FAIXA: O LUGAR ============================
		// O nome da zona e o numero desta aba; a hora local e o ciclo do dia vao na legenda, nos MESMOS
		// arredondamentos da assinatura (`AssinaturaDoCeu`: decimo de hora) -- e a igualdade dos dois
		// que faz a pagina se refazer quando (e so quando) o texto muda.
		// ==========================================================================
		Faixa("Lugar", cli?.Zone.Name ?? "",
			  ceu is { } c
				  ? $"{Jandirus.Core.World.Ceu.NomeDaHora(c.Hora)} ({c.Hora * 24:00.0}h)  ·  {Jandirus.Core.World.Ceu.NomeDoCiclo(r)}"
				  : Jandirus.Core.World.Ceu.NomeDoCiclo(r));

		// CEU E CLIMA LADO A LADO: sao listas curtas de rotulo/valor, que e o caso das duas colunas.
		GridContainer duas = Colunas();
		CartaoDoCeu(duas, r, ceu);
		CartaoDoClima(duas);

		// ============================ A CONQUISTA E AS ESFERAS MORAM AQUI, E NAO NA NAV ============================
		// Elas estavam na aba Nav -- e o comentario que terminava a linha acima ("a conquista do planeta
		// entra aqui com o sistema dela") ja dizia onde era a casa delas.
		//
		// A MUDANCA E CONSEQUENCIA DO PORTAO, e nao gosto: a aba Nav passou a depender do item Nav System
		// (pedido do dono). Deixar dominio e esferas la dentro trancaria fincar bandeira, cobrar tributo,
		// invocar Shenron e erguer estatua atras de um aparelho de 550.000 zeni -- nada disso tem a ver com
		// navegar, e o Namekuseijin do Cla do Dragao ficaria sem caminho nenhum pras esferas dele. Este e o
		// UNICO lugar do cliente por onde esses verbos saem (nao ha segundo caminho), entao esconder a aba
		// sem move-los seria apagar duas features que ninguem pediu pra apagar.
		//
		// A ABA WORLD SEMPRE EXISTE (esta em `Fixas`) e e sobre o LUGAR -- e as duas sao sobre o lugar. O
		// preco e que World precisou de assinatura: ver `Assinatura`, que explica por que.
		// ==========================================================================================================
		if (cli != null)
		{
			// O REFUGIO VEM PRIMEIRO, e sem crivo de zona: ver `BlocoDoRefugio`. Ele so aparece quando
			// o planeta natal desta pessoa deixou de existir, e nesse dia e a coisa mais urgente desta
			// aba -- o dominio e as esferas continuam podendo esperar.
			BlocoDoRefugio(cli);
			BlocoDeDominio(cli);
			BlocoDasEsferas(cli);
		}
	}

	/// <summary>
	/// O CARTAO DO CEU: o disco da lua e as duas linhas dela (fase e quanto falta pra cheia).
	///
	/// ============================ O DISCO E O MESMO DO HUD ============================
	/// E o proprio `LuaNoCeu` -- o widget que o `Hud.MontarDireita` pendura no canto --, alimentado do
	/// mesmo `World.Instancia.Ceu` e do mesmo `TempoQueFaz.Encobre` que o `Hud._Process` lhe da. Um
	/// segundo desenho de lua aqui seria uma segunda verdade sobre a fase; com o mesmo node, as duas
	/// telas concordam por construcao (e a bancada `--diagabas` F11 cobra exatamente isso: o disco do
	/// menu visivel se e so se o do HUD esta).
	///
	/// ELE SO APARECE COM A LUA NO CEU, como no HUD (`LuaNoCeu.Aplicar` esconde o node de dia): de dia
	/// as linhas dizem em que fase ela esta e quando volta -- que e o que se consulta.
	///
	/// ALIMENTADO NA MONTAGEM, e nao a cada quadro: esta pagina se remonta sempre que a hora (a um
	/// decimo), a fase, a altura da lua ou o clima mudam de texto (ver `AssinaturaDoCeu`), e e nesses
	/// instantes que o disco tem alguma coisa nova a mostrar. O HUD, que nao remonta, precisa do
	/// `_Process`; aqui ele seria um segundo relogio pro mesmo ceu.
	/// ==================================================================================
	/// </summary>
	private void CartaoDoCeu(Control pai, Jandirus.Core.World.RelogioDoPlaneta r, Jandirus.Core.World.EstadoDoCeu? ceu)
	{
		VBoxContainer corpo = Cartao("Céu", pai);
		if (ceu is not { } c || World.Instancia is not { } mundo)
		{
			Nota("o céu daqui ainda não chegou.", corpo);
			return;
		}

		if (!r.Lua.Existe) Linha("Lua", "este mundo não tem lua", null, corpo);
		else
		{
			var lua = new LuaNoCeu { Name = "Lua" };
			corpo.AddChild(lua);
			// O `_Ready` do disco (que cria o shader) roda no `AddChild`, porque a pagina ja esta na
			// arvore; o `IsNodeReady` e a garantia disso, e o `Ready` e o plano B pro dia em que a
			// pagina for montada fora dela -- sem ele o `Aplicar` escreveria num shader que nao existe.
			double encoberta = mundo.TempoQueFaz?.Encobre ?? 0;
			if (lua.IsNodeReady()) lua.Aplicar(c, encoberta);
			else lua.Ready += () => lua.Aplicar(c, encoberta);

			Linha("Lua", Jandirus.Core.World.Ceu.NomeDaFase(c.Fase)
						 + (c.LuaNoCeu ? " (no céu)" : " (abaixo do horizonte)"),
				  c.Cheia && c.LuaNoCeu ? Tema.Destaque : null, corpo);

			// QUANTO FALTA PRA CHEIA. E a informacao com que se PLANEJA -- sem ela o ciclo
			// da lua e uma surpresa que cai na cabeca de quem tem rabo.
			double faltam = Jandirus.Core.World.Ceu.SegundosAteACheia(r, mundo.TempoDoMundo);
			Linha("Lua cheia", faltam < 0 ? "nunca (a lua não chega a nascer aqui)"
						 : faltam < 1 ? "AGORA"
						 : faltam < 60 ? $"em {faltam:0} s"
						 : $"em {faltam / 60:0} min",
				  faltam >= 0 && faltam < 1 ? Tema.Destaque : null, corpo);
		}

		// NA TERRA A FRASE ERA UM ABSURDO ("o ceu daqui nao e o da Terra" -- lido na Terra). A regra e a
		// mesma nos dois casos: cada mundo tem o proprio relogio; o que muda e de onde se olha.
		bool naTerra = string.Equals(GameClient.Instance?.Zone.Name, "Earth", StringComparison.OrdinalIgnoreCase);
		Nota(naTerra
				 ? "Este é o dia e o céu da Terra. Cada outro planeta corre o próprio dia e o próprio céu."
				 : "Cada planeta corre o próprio dia: a hora e o céu daqui não são os da Terra.", corpo);
	}

	/// <summary>
	/// O CARTAO DO CLIMA: o tempo de agora e o que pode cair aqui.
	///
	/// O QUE PODE CAIR AQUI vem do `allowedWeatherTypes` do DM, e e informacao de LUGAR: saber que em
	/// Vegeta chove sangue e nao agua e parte de conhecer Vegeta. Em PILULAS porque e uma lista de
	/// estados possiveis, e a linha de rotulo/valor nao quebra linha -- seis nomes nela saiam da metade
	/// da pagina pela direita.
	/// </summary>
	private void CartaoDoClima(Control pai)
	{
		VBoxContainer corpo = Cartao("Clima", pai);
		if (World.Instancia?.TempoQueFaz is not { } tq)
		{
			Nota("o tempo daqui ainda não chegou.", corpo);
			return;
		}

		Linha("Clima", tq.Ativo
				  ? $"{Jandirus.Core.World.Clima.Nome(tq.Tipo)} ({tq.Forca:P0})" + (tq.Forcado ? " -- forçado" : "")
				  : "céu limpo",
			  tq.Ativo ? Tema.Texto : Tema.Bom, corpo);

		Jandirus.Core.World.ClimaDoPlaneta cl = World.Instancia?.ClimaDoLugar
											?? Jandirus.Core.World.ClimaDoPlaneta.Nenhum;
		if (cl.Existe && cl.Permitidos.Length > 0)
		{
			var rotulo = new Label { Text = "Pode cair" };
			rotulo.AddThemeColorOverride("font_color", Tema.TextoFraco);
			rotulo.AddThemeFontSizeOverride("font_size", 13);
			corpo.AddChild(rotulo);
			corpo.AddChild(Pilulas([.. cl.Permitidos.Select(t => (Jandirus.Core.World.Clima.Nome(t), Tema.TextoFraco))]));
		}
		else Linha("Pode cair", "nada: o céu daqui não muda", null, corpo);
	}

	/// <summary>A grade de tres colunas em que os botoes de um bloco moram, todos com a mesma largura.</summary>
	private static GridContainer GradeDeBotoes(Control pai)
	{
		var g = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		g.AddThemeConstantOverride("h_separation", 4);
		g.AddThemeConstantOverride("v_separation", 4);
		pai.AddChild(g);
		return g;
	}

	/// <summary>
	/// UM BOTAO DE VERB DESTA ABA: o texto de sempre, a dica de sempre, esticado na celula da grade. O
	/// texto QUEBRA LINHA em vez de ser cortado: "Erguer + gravar o SUPREMO (2.000.000 zeni)" nao cabe
	/// num terco da pagina, e um botao com reticencias no meio do preco e um botao que esconde o preco.
	/// </summary>
	private static Button BotaoDeVerbo(GameClient cli, string rotulo, string cmd, string arg, string dica, Control pai)
	{
		var b = new Button
		{
			Text = rotulo, TooltipText = dica,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		b.AddThemeFontSizeOverride("font_size", 12);
		string c = cmd, a = arg;
		b.Pressed += () => cli.SendVerbo(c, a);
		pai.AddChild(b);
		return b;
	}

	/// <summary>
	/// ============================ A CONQUISTA MORA NA ABA WORLD, E NAO NUMA ABA PROPRIA ============================
	/// Ela e sobre PLANETAS, e uma aba nova custaria uma entrada permanente na barra pra uma coisa que
	/// so faz sentido quando voce esta pisando num mundo.
	///
	/// ============================ E SAO BOTOES, NAO UM PAINEL DE DADOS ============================
	/// Nenhum destes botoes desenha estado: eles mandam VERBO e o servidor responde no chat. Isso e
	/// deliberado e nao preguica -- no DM a conquista inteira e `alert()`, `input()` e `to_chat()`,
	/// e um painel de dominios exigiria um opcode novo (dominios, lealdade, tributo) pra ser sempre
	/// verdadeiro. "Ver este planeta" e "Meus domínios" respondem tudo isso em texto, e o servidor
	/// continua sendo a unica fonte.
	///
	/// QUEM AUTORIZA E O SERVIDOR: esconder botao nunca foi permissao neste projeto, e por isso os
	/// botoes aparecem sempre que se esta num planeta -- a recusa ("você é fraco demais", "chegue
	/// mais perto da bandeira") e a resposta do verbo, com o motivo.
	/// ======================================================================================================
	/// </summary>
	private void BlocoDeDominio(GameClient cli)
	{
		// EM TERRA FIRME SO. O crivo e o `Espaco.EhPlaneta` do Core -- a MESMA definicao de "planeta"
		// que o servidor usa pra recusar (e a mesma que a destruicao de planeta ja consulta). Duas
		// nocoes de planeta envelheceriam separadas, e a primeira a errar seria a do cliente.
		if (!Jandirus.Core.World.Espaco.EhPlaneta(cli.Zone)) return;

		VBoxContainer corpo = Cartao("Domínio planetário");
		GridContainer grade = GradeDeBotoes(corpo);

		// A ORDEM E A DAS DUAS FILEIRAS DE ANTES: ver e reivindicar primeiro, cuidar e largar depois.
		foreach ((string rotulo, string cmd, string arg, string dica) in
			new (string, string, string, string)[]
			{
				("Ver este planeta", "conq_ver", "",
					"quem manda aqui, quanto vale de tributo e quanto poder a invasão exige"),
				("Meus domínios", "conq_dominios", "",
					"seus planetas, a lealdade de cada um e o tributo acumulado"),
				("Conquistar planeta", "conq_invadir", "",
					"finca a bandeira. Herói de um povo sem dono reivindica em PAZ"),
				("...à força", "conq_invadir", "forca",
					"invade mesmo sendo herói do povo daqui -- matar defensores mancha sua reputação"),
				("Coletar tributo", "conq_tributo", "", "junto da sua bandeira"),
				("Renascer aqui", "conq_spawn", "", "liga/desliga este domínio como ponto de renascimento"),
				("Abandonar domínio", "conq_abandonar", "sim", "a posse se perde na hora"),
				("Arrancar bandeira", "conq_arrancar", "",
					"8s parado e sem levar dano frustram a invasão de outra pessoa"),
			})
			BotaoDeVerbo(cli, rotulo, cmd, arg, dica, grade);

		Nota("Domínio sem soberano por perto se perde: o tributo mingua, a guarnição afrouxa e o povo "
			 + "acaba derrubando a bandeira. Apareça.", corpo);
	}

	/// <summary>
	/// **A PORTA PERMANENTE DO REFUGIO** -- pra onde voce volta quando o seu planeta natal acabou.
	///
	/// ============================ POR QUE ELE NAO MORA DENTRO DO BLOCO DE DOMINIO ============================
	/// O <see cref="BlocoDeDominio"/> desiste na primeira linha quando o jogador nao esta na
	/// superficie de um planeta -- e o refugio e exatamente a tela de quem **nao esta**: quem precisa
	/// dela costuma estar morto no Outro Mundo (que nao e um planeta da carta) ou em orbita de um
	/// mundo que acabou de nascer sob ele. Dentro daquele bloco, o botao ficaria escondido justamente
	/// de quem precisa dele.
	/// ====================================================================================================
	///
	/// A tela e EMPURRADA uma vez por sessao (na chegada ao Outro Mundo, ver `GameServer.Refugio.cs`),
	/// e uma tela que so aparece sozinha e uma tela que se perde: quem a fechar sem ler nunca mais
	/// acha o caminho de volta. **Ele so existe com o berco destruido**, e a condicao e a do SERVIDOR
	/// (`RefugioPrecisa` vem de la, e nao de uma conta feita aqui). Cartao de DESTAQUE: no dia em que
	/// ele aparece, e a coisa mais urgente desta aba.
	/// </summary>
	private void BlocoDoRefugio(GameClient cli)
	{
		if (!cli.RefugioPrecisa) return;

		VBoxContainer corpo = Cartao("Refúgio", destaque: true);
		Nota($"{cli.RefugioNatal} foi destruída. Da próxima vez que você morrer, o seu corpo "
			 + "precisa de outro lugar para voltar.", corpo);

		var refugio = new Button
		{
			Text = "Escolher para onde eu volto",
			TooltipText = "um planeta que você conquistou, ou o mundo vivo mais perto de onde era casa",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		refugio.AddThemeColorOverride("font_color", Tema.Destaque);
		refugio.Pressed += () => TelaDeRefugio.Instancia?.Abrir();
		corpo.AddChild(refugio);
	}

	/// <summary>
	/// ============================ AS ESFERAS MORAM NA ABA WORLD, AO LADO DO DOMINIO ============================
	/// Pelo mesmo argumento que trouxe a conquista pra ca: elas sao sobre PLANETAS (a esfera comum
	/// pertence a um mundo e nunca sai dele) e sobre a GALAXIA (as Super se espalham por celulas de
	/// sistema, e o radar dourado e um instrumento de navegacao). Uma aba propria custaria uma entrada
	/// permanente na barra pra uma coisa que so faz sentido em dois lugares -- e os dois ja estao aqui.
	///
	/// ============================ E SAO BOTOES, NAO UM PAINEL ============================
	/// Nenhum destes botoes desenha estado: eles mandam VERBO e o servidor responde no chat. Mesma
	/// escolha (e mesma razao) do `BlocoDeDominio` -- no original a coisa toda e `alert()`, `input()` e
	/// `to_chat()`. As DUAS excecoes sao dados que o servidor ja empurra por conta propria e que
	/// mentiriam se fossem calculados aqui: o **placar das Super** e a **frase do radar dourado**.
	///
	/// QUEM AUTORIZA E O SERVIDOR: esconder botao nunca foi permissao neste projeto. "Erguer estátua"
	/// aparece pra todo mundo, e a recusa ("apenas Namekuseijins do Clã do Dragão...") e a resposta do
	/// verbo, com o motivo.
	/// ======================================================================================================
	/// </summary>
	private void BlocoDasEsferas(GameClient cli)
	{
		bool noEspaco = Jandirus.Core.World.Espaco.EhEspaco(cli.Zone);
		bool emPlaneta = Jandirus.Core.World.Espaco.EhPlaneta(cli.Zone);
		if (!noEspaco && !emPlaneta) return;

		VBoxContainer corpo = Cartao("Esferas do Dragão");

		// O SINAL DOURADO VEM PRONTO DO SERVIDOR (ver `Protocol.S2C.SuperEsferas`): o cliente nao
		// sabe onde as sete estao, e e assim que o mapa do tesouro nao viaja. Na cor de nutricao do
		// tema -- o dourado da paleta -- e nao num amarelo inventado: a bancada mede a paleta no pixel.
		if (cli.SinalDourado.Length > 0)
		{
			var l = new Label { Text = cli.SinalDourado, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			l.AddThemeColorOverride("font_color", Tema.Nutricao);
			l.AddThemeFontSizeOverride("font_size", 13);
			corpo.AddChild(l);
		}

		Linha("Super Esferas suas", $"{cli.MinhasSupers}/{Jandirus.Core.Magic.SuperEsferas.Total}",
			  cli.MinhasSupers > 0 ? Tema.Destaque : null, corpo);

		GridContainer grade = GradeDeBotoes(corpo);
		foreach ((string rotulo, string cmd, string arg, string dica, bool espaco) in
			new (string, string, string, string, bool)[]
			{
				("Ver as esferas daqui", "db_ver", "",
					"que estátua manda neste mundo, quantos pedidos ela dá e se as sete estão acordadas", false),
				("Radar", "db_radar", "",
					"precisa do Dragon Radar na mochila. Só acha esfera ACORDADA, e só deste mundo", false),
				("Pegar esfera", "db_pegar", "", "a que estiver ao seu alcance", false),
				("Largar tudo", "db_largar", "", "põe no chão as que você carrega", false),
				("INVOCAR", "db_invocar", "",
					"com as sete reunidas: no chão ao seu redor ou com você", false),

				("Erguer Estátua do Dragão", "db_estatua", "",
					"só Namekuseijin do Clã do Dragão, e só num mundo que ele domine (ou na Terra, se "
					+ "for o Guardião)", false),

				// ============================ ELE E A UNICA PORTA DO DESEJO SUPREMO ============================
				// `namekian.dm:101-109`: o "Strongest in the Universe" nao sai da escada de poder -- ele e
				// COMPRADO por 2.000.000 de zeni **na hora de erguer a estatua**, e nunca depois. No
				// original isso e um `alert()` que aparece sozinho; aqui virou argumento, e um argumento
				// sem botao seria um desejo que so existe pra quem digita verbo. Segundo botao, e nao um
				// campo de texto, porque a escolha e binaria.
				// ========================================================================================
				("Erguer + gravar o SUPREMO (2.000.000 zeni)", "db_estatua", "supremo",
					"grava nestas esferas o desejo O MAIS FORTE DO UNIVERSO -- a única forma de ele "
					+ "existir num set de jogador. Cobra na hora", false),
				("Despertar as esferas", "db_reviver", "",
					"só quem ergueu a estátua, e só vivo -- é a única volta de um set inerte", false),

				("Super Esferas: placar", "sdb_status", "",
					"quem tem quantas, e quantas ainda estão livres (sem dizer ONDE)", true),
				("Reivindicar Super Esfera", "sdb_reivindicar", "",
					"chegue perto dela no espaço. 10s se estiver livre, 5 MINUTOS se for de alguém -- "
					+ "e o dono é avisado", true),
				("Chamar o Super Shenron", "sdb_invocar", "",
					"com as sete Super Esferas. A Língua dos Deuses é a Fase 2", true),
			})
		{
			// O RECORTE E POR LUGAR e nao por permissao: reivindicar uma Super so acontece no espaco,
			// e erguer estatua so em terra. Botao que nunca poderia funcionar dali e ruido.
			if (espaco != noEspaco) continue;
			BotaoDeVerbo(cli, rotulo, cmd, arg, dica, grade);
		}

		Nota(noEspaco
			? "As Super Esferas têm o tamanho de um planetoide: não se carregam. Quem defende não "
			+ "precisa vencer o ladrão -- basta derrubá-lo ou afastá-lo da esfera."
			: "As sete esferas de um mundo nunca saem dele: largue uma em outro planeta e ela volta "
			+ "sozinha. Depois de um pedido, elas apagam e se espalham de novo.", corpo);

		BlocoDoPedido(cli, noEspaco);
	}

	/// <summary>O que o jogador escreveu na linha do desejo: o id, e o alvo quando o desejo pede um.</summary>
	private string _textoDoDesejo = "";

	/// <summary>
	/// **O PEDIDO** -- a linha de texto do desejo e os botoes que a usam, no cartao proprio.
	///
	/// ============================ POR QUE UMA CAIXA DE TEXTO, E NAO UMA LISTA DE BOTOES ============================
	/// A lista de desejos **nao e fixa**: ela depende do poder do set, de quantos pedidos ele da, de o
	/// criador ter comprado o supremo, do genoma Saiyajin de quem pede e do cargo dele. Desenhar isso
	/// aqui exigiria que o cliente refizesse a escada inteira do `WishTable.dm` -- uma SEGUNDA casa pra
	/// a mesma formula, que e o defeito que a regra da casa proibe por nome.
	///
	/// Entao quem manda a lista e o servidor, no chat, quando o verbo chega sem argumento -- e a caixa
	/// serve pra devolver a escolha. E o mesmo desenho do painel de admin ("escreva a skill ao lado") e
	/// pelo mesmo motivo: o conteudo e do servidor, o teclado e do jogador.
	///
	/// O TEXTO DIGITADO MORA FORA DA PAGINA (`_textoDoDesejo`): a pagina e destruida e remontada quando
	/// a assinatura muda, e a caixa nasceria vazia na mao de quem esta digitando.
	/// ========================================================================================================
	/// </summary>
	private void BlocoDoPedido(GameClient cli, bool noEspaco)
	{
		VBoxContainer corpo = Cartao(noEspaco ? "O pedido ao Super Shenron" : "O pedido ao dragão");

		var campo = new LineEdit
		{
			PlaceholderText = "id do desejo (e o alvo, se pedir)",
			Text = _textoDoDesejo,
			MaxLength = Jandirus.Net.Protocol.MaxArgDeVerbo - 4,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		campo.TextChanged += t => _textoDoDesejo = t;
		corpo.AddChild(campo);

		GridContainer grade = GradeDeBotoes(corpo);
		foreach ((string rotulo, string cmd, string dica, bool precisaTexto) in noEspaco
			? new (string, string, string, bool)[]
			{
				("Ver os desejos", "sdb_invocar", "com as sete, lista o que o Super Shenron atende", false),
				("PRONUNCIAR", "sdb_invocar",
					"pede o desejo escrito ao lado. Se você carrega uma PROCURAÇÃO, o que vale é o "
					+ "pedido de quem emprestou -- você não pode trocá-lo", true),
				("Emprestar as sete", "sdb_transferir",
					"escreva ao lado o nome de quem vai falar por você. Ele precisa estar do seu lado, "
					+ "e precisa aceitar. O desejo continua sendo SEU. Só quem fala a LÍNGUA DOS DEUSES "
					+ "desperta o Super Shenron: cargos divinos (atuais ou passados) a conhecem, e o sangue "
					+ "Kai ou Demigod nasce com ela", true),
				("Meu pedido", "sdb_pedido",
					"só quem emprestou escreve aqui: é o desejo que o porta-voz vai pronunciar -- ele não "
					+ "escolhe qual é", false),
				("Retomar a guarda", "sdb_revogar",
					"toma de volta as sete que você emprestou -- a qualquer momento", false),
				("Aceitar a guarda", "sdb_guarda_aceitar", "aceita falar em nome de quem te ofereceu", false),
				("Recusar", "sdb_guarda_recusar", "recusa a guarda das sete", false),
				("ACEITO O PREÇO", "sdb_aceito",
					"só para o desejo que cobra a VIDA: escreva ACEITO O PREÇO ao lado", true),
			}
			: new (string, string, string, bool)[]
			{
				("Ver os desejos", "db_desejar",
					"com o dragão de pé, lista o que ele atende. A lista depende do poder do set e de "
					+ "quantos pedidos ele dá; desejo que ainda não foi portado aparece dizendo isso, e "
					+ "não gasta pedido nenhum", false),
				("PEDIR", "db_desejar", "pede o desejo escrito ao lado", true),

				// ============================ ESTES DOIS SO PASSARAM A IMPORTAR AGORA ============================
				// `db_refazer` e `db_derrubar` existem desde a Fase 1 e **nunca tiveram botao**. Enquanto o
				// dragao nao concedia nada, "quantos pedidos por invocacao" era um numero sem consequencia
				// e ninguem sentia falta. Com a tabela ligada ele e a diferenca entre um pedido e tres --
				// e e o gate que faz "Ressuscitar TODOS" e "Matar" existirem ou nao no set.
				//
				// Sem este botao, um Namekuseijin do Cla do Dragao ficaria preso em UM pedido pra sempre:
				// a estatua nasce com `Desejos = 1` e so o `Redo` do original a muda.
				// ============================================================================================
				("Pedidos por invocação", "db_refazer",
					"escreva 1, 2 ou 3 ao lado. MAIS pedidos deixa cada um mais fraco -- e com 3 o set "
					+ "perde \"Ressuscitar TODOS\" e \"Matar\", que é o que separa Shenron de Porunga", true),
				("Derrubar a estátua", "db_derrubar",
					"apaga as sete deste mundo, para sempre. Escreva 'sim' ao lado pra confirmar", true),
			})
		{
			string c = cmd;
			bool pede = precisaTexto;
			var b = new Button
			{
				Text = rotulo, TooltipText = dica,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			b.AddThemeFontSizeOverride("font_size", 12);
			b.Pressed += () =>
			{
				if (pede && _textoDoDesejo.Trim().Length == 0)
				{ Chat.Sistema("escreva o pedido (ou o nome) ao lado antes."); return; }
				cli.SendVerbo(c, pede ? _textoDoDesejo.Trim() : "");
			};
			grade.AddChild(b);
		}

		Nota(noEspaco
			? "Só quem fala a LÍNGUA DOS DEUSES desperta o Super Shenron. Quem não fala empresta as sete "
			+ "a quem fale: o desejo continua sendo de quem emprestou."
			: "Aperte \"Ver os desejos\" com o dragão de pé, e escreva o id do desejo ao lado pra pedir.",
			corpo);
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): tudo que ESTA aba
	/// desenha e que a assinatura basica (em MenuJogo.cs) nao cobre entra aqui, nos mesmos
	/// arredondamentos em que e desenhado.
	///
	/// A BASICA COBRE a zona (que decide os blocos e o "pode cair"), o placar das Super, o sinal dourado
	/// e o ceu/clima (`AssinaturaDoCeu`). O que faltava e o REFUGIO: o cartao de destaque nasce e some
	/// pelo `RefugioPrecisa` do servidor, e o nome do planeta destruido vai na nota -- sem estes dois a
	/// porta de volta so apareceria quando outra coisa qualquer mudasse.
	/// </summary>
	private string ExtraDaAssinaturaDeMundo(SheetState f) =>
		GameClient.Instance is { } c ? $"{c.RefugioPrecisa}|{c.RefugioNatal}" : "";
}
