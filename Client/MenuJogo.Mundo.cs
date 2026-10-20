using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Core.Verbos;
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
		Faixa("Lugar", cli?.NomeDoLugar ?? "",
			  ceu is { } c
				  ? $"{Jandirus.Core.World.Ceu.NomeDaHora(c.Hora)} ({c.Hora * 24:00.0}h)  ·  {Jandirus.Core.World.Ceu.NomeDoCiclo(r)}"
				  : Jandirus.Core.World.Ceu.NomeDoCiclo(r));

		// (2026-10-19) OS SERVICOS DO OUTRO MUNDO, so pro morto: o servico em curso e a rede. Ver `MenuJogo.ServicoDoAlem.cs`.
		CartaoDoServicoDoAlem();

		// O MAPA DO LUGAR E AS COORDENADAS, logo abaixo do nome: e a resposta de "onde eu estou". Ver `MenuJogo.Mapa.cs`.
		if (cli != null) BlocoDoMapa(cli);

		// CEU E CLIMA LADO A LADO: sao listas curtas de rotulo/valor, que e o caso das duas colunas.
		GridContainer duas = Colunas();
		CartaoDoCeu(duas, r, ceu);
		CartaoDoClima(duas);
		// (2026-10-17) O CALENDARIO, na mesma grade: a data e do mundo, e nao do lugar, mas e a mesma pergunta -- "que tempo e este?".
		CartaoDoCalendario(duas);

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
			// O KI DIVINO: o medidor do despertar e a maestria. So nasce pra quem tem o que ler nele -- ver `MenuJogo.KiDivino.cs`.
			BlocoDoKiDivino(cli);
			BlocoDeDominio(cli);
			BlocoDoNome(cli);
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
	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagabas`): a assinatura da aba World volta a levar o "quanto falta pra lua cheia"
	/// em SEGUNDOS inteiros -- o estado de antes de 2026-10-11, em que a pagina se refazia a cada segundo (e levava
	/// junto o que o jogador estivesse digitando no batismo do planeta). Lido pelo <see cref="CheiaNaAssinatura"/>.
	/// </summary>
	internal static bool CheiaEmSegundosNaAssinaturaDeTeste;

	/// <summary>
	/// A LINHA "Lua cheia", em texto. UMA funcao pros dois que precisam dela: o cartao que a DESENHA e a assinatura
	/// de cache que decide quando a pagina se refaz (<see cref="CheiaNaAssinatura"/>). Enquanto os dois lerem
	/// daqui, a pagina se refaz quando -- e so quando -- esta linha muda na tela: de minuto em minuto, e de
	/// segundo em segundo so no ultimo minuto antes da lua cheia.
	/// </summary>
	internal static string TextoDaLuaCheia(double faltam) =>
		faltam < 0 ? "nunca (a lua não chega a nascer aqui)"
		: faltam < 1 ? "AGORA"
		: faltam < 60 ? $"em {faltam:0} s"
		: $"em {faltam / 60:0} min";

	/// <summary>
	/// O PEDACO DA LUA CHEIA NA ASSINATURA DA ABA WORLD: o TEXTO que a tela mostra, e nao a contagem em segundos.
	///
	/// A assinatura levava `SegundosAteACheia` com `:0` -- um numero que muda a cada segundo, sempre -- enquanto a
	/// linha desenhada so muda de minuto em minuto. A pagina inteira se refazia uma vez por segundo: os botoes
	/// nasciam de novo debaixo do mouse, a caixa do batismo se esvaziava na mao de quem digitava, e a bancada das
	/// abas estourava de vez em quando por segurar um cartao que ja tinha morrido.
	/// </summary>
	internal static string CheiaNaAssinatura(double faltam) =>
		CheiaEmSegundosNaAssinaturaDeTeste ? $"{faltam:0}" : TextoDaLuaCheia(faltam);

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
			Linha("Lua cheia", TextoDaLuaCheia(faltam), faltam >= 0 && faltam < 1 ? Tema.Destaque : null, corpo);
		}

		// A ATMOSFERA DIVINA e do LUGAR, como a lua: a linha so nasce no mundo que a tem. Ver `MenuJogo.KiDivino.cs`.
		if (GameClient.Instance is { } daqui) LinhaDaAtmosfera(daqui, corpo);
		// ...E A SALA DO TEMPO de um mundo sorteado tambem e do lugar: a pista so nasce no mundo que guarda uma. Ver `MenuJogo.SalaDoTempo.cs`.
		if (GameClient.Instance is { } nesteMundo) LinhaDaSalaDoTempo(nesteMundo, corpo);

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

	/// <summary>
	/// O CARTAO DO CALENDARIO (dono, 2026-10-17: *"tinhas os meses e o ano"*): a data de hoje, quando o mes vira, e quanto falta pro proximo
	/// aniversario de quem esta jogando. No DM a data so saia no chat (`WorldClock.dm:34`, `:112`) e no verb `Sense_Time`; aqui ela mora
	/// tambem nesta aba, que e a do lugar e do tempo. A data e a conta do Core com o dia da Terra de agora e a origem que veio no
	/// `S2C.Ceu`; os anos e os meses da idade chegam na ficha lenta.
	/// </summary>
	private void CartaoDoCalendario(Control pai)
	{
		VBoxContainer corpo = Cartao("Calendário", pai);
		if (GameClient.Instance is not { TempoChegou: true } cli || World.Instancia is not { } mundo)
		{
			Nota("o calendário ainda não chegou.", corpo);
			return;
		}

		Jandirus.Core.World.DataDoCalendario hoje = HojeNoCalendario(cli, mundo);
		Linha("Hoje", hoje.Extenso, null, corpo);
		Jandirus.Core.World.DataDoCalendario vira = hoje.ProximoMes;
		Linha("O mês vira", (hoje.DiasAteOMesVirar == 1 ? "amanhã" : $"em {hoje.DiasAteOMesVirar} dias") + $" ({vira.Dia} de {vira.NomeDoMes})",
			  null, corpo);

		int anos = cli.Atributos.Idade, meses = cli.Atributos.MesesDaIdade;
		int faltam = Jandirus.Core.World.Calendario.MesesPorAno - meses;
		Linha("Sua idade", $"{anos} anos e {meses} {(meses == 1 ? "mês" : "meses")} -- faz {anos + 1} em {faltam} {(faltam == 1 ? "mês" : "meses")}",
			  null, corpo);
		Nota($"Um dia do jogo dura {Jandirus.Core.World.Ceu.SegundosPorDia / 60:0} minutos; o mês tem {Jandirus.Core.World.Calendario.DiasPorMes} "
			  + $"dias e o ano, {Jandirus.Core.World.Calendario.MesesPorAno} meses. A idade anda com o calendário -- e mais depressa na Sala do Tempo.",
			  corpo);
	}

	/// <summary>A DATA DE HOJE na tela: a mesma conta do servidor (`GameServer.Hoje`), com o dia da Terra deste cliente.</summary>
	internal static Jandirus.Core.World.DataDoCalendario HojeNoCalendario(GameClient cli, World mundo) =>
		Jandirus.Core.World.Calendario.De(Jandirus.Core.World.Calendario.DiaDaTerra(
			Planetas.Relogio(Jandirus.Core.World.ZoneKey.Premade("Earth")), mundo.TempoDoMundo) - cli.OrigemDoCalendario);

	/// <summary>O CALENDARIO NA ASSINATURA DA ABA: o dia, e os anos e os meses da idade -- a pagina se refaz quando um deles muda.</summary>
	internal static string AssinaturaDoCalendario() =>
		GameClient.Instance is { TempoChegou: true } cli && World.Instancia is { } mundo
			? $"{HojeNoCalendario(cli, mundo).Dias}|{cli.Atributos.Idade}|{cli.Atributos.MesesDaIdade}"
			: "-";

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
	///
	/// ============================ O BOTAO SO NASCE PRA QUEM TEM O REQUISITO ============================
	/// O crivo e o da tabela unica (`RequisitosDosVerbos`), pelo comando e pelo argumento que o botao
	/// manda -- o mesmo que os verbs das outras abas passam no `Verbos.Visivel`. Mora AQUI, e nao em cada
	/// bloco, porque todo botao de verb desta aba nasce por este metodo: bloco novo que o usar ja nasce
	/// filtrado, e verb sem linha na tabela nao tem requisito e aparece. Devolve nulo quando nao desenhou.
	/// ==================================================================================================
	/// </summary>
	private static Button? BotaoDeVerbo(GameClient cli, string rotulo, string cmd, string arg, string dica, Control pai)
	{
		if (!RequisitosDosVerbos.DoVerbo(cmd, arg).Atende(PosseDoCliente.Atual)) return null;

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
	/// QUEM AUTORIZA E O SERVIDOR: esconder botao nunca foi permissao neste projeto. Ver, conquistar e
	/// arrancar a bandeira de um invasor aparecem sempre que se esta num planeta -- a recusa ("você é
	/// fraco demais", "chegue mais perto da bandeira") e a resposta do verbo, com o motivo.
	///
	/// OS QUATRO DE QUEM JA TEM DOMINIO (meus dominios, tributo, renascer aqui, abandonar) so nascem pra
	/// quem o tem -- o `BotaoDeVerbo` pergunta a tabela de requisitos, e a posse chega nos fatos
	/// (`Fatos.DominaAlgum`, `Fatos.DominaEstePlaneta`). Pra quem nunca fincou bandeira eram quatro botoes
	/// que so sabiam dizer "você não tem domínio neste planeta".
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
	/// O que o jogador escreveu na caixa do batismo. Fora da pagina: ela e destruida e remontada quando a
	/// assinatura muda, e a caixa nasceria vazia na mao de quem esta digitando.
	/// </summary>
	private string _nomeDigitadoDoPlaneta = "";

	/// <summary>
	/// **O NOME DESTE PLANETA** -- o batismo. O dono (2026-10-10): *"adicione tb a opçao de nomear o planeta (a
	/// primeira pessoa q entrou nele ou o dono do planeta q dominou)"*.
	///
	/// ============================ ESTE CARTAO DESENHA ESTADO, E O VIZINHO NAO ============================
	/// O cartao do dominio logo acima e so botoes porque um painel de dominios pediria um opcode novo. Este
	/// TEM o opcode (`S2C.NomesDePlanetas`), e por um motivo que nao e dele so: o nome batizado precisa
	/// aparecer na carta estelar e na faixa do lugar de TODO MUNDO, entao a lista ja viaja de qualquer jeito.
	/// O cabecalho pessoal do mesmo pacote diz o MEU direito sobre este chao -- e e ele que faz a caixa de
	/// texto existir so pra quem pode usa-la, em vez de um botao que so sabe recusar.
	///
	/// QUEM AUTORIZA CONTINUA SENDO O SERVIDOR: o direito desenhado aqui e o que ele mandou, e o verbo e
	/// conferido de novo quando chega.
	/// ====================================================================================================
	/// </summary>
	private void BlocoDoNome(GameClient cli)
	{
		if (Jandirus.Core.World.ChaveDePlaneta.Da(cli.Zone) is not { } chave) return;

		Jandirus.Core.World.NomeDePlaneta? dado = cli.NomesDosPlanetas.De(chave.Texto);
		bool batizado = dado is { Nome.Length: > 0 };
		Jandirus.Core.World.Batismo.Direito direito = cli.DireitoDeBatismo;
		bool pode = direito != Jandirus.Core.World.Batismo.Direito.Nenhum;

		VBoxContainer corpo = Cartao("Nome deste planeta", destaque: pode);
		Linha("Nome", cli.NomeDoLugar, null, corpo);
		if (batizado) Nota($"Batizado por {dado!.PorQuem}. Antes: {cli.Zone.Name}.", corpo);
		if (cli.DescobridorDaqui.Length > 0) Nota($"Primeiro a pisar aqui: {cli.DescobridorDaqui}.", corpo);

		if (!pode)
		{
			Nota("Dá o nome quem pisou primeiro num mundo gerado (uma vez, e enquanto ele não tem dono) "
			   + "ou quem domina o planeta.", corpo);
			return;
		}

		var campo = new LineEdit
		{
			PlaceholderText = $"novo nome ({Jandirus.Core.World.Batismo.MinLetras} a {Jandirus.Core.World.Batismo.MaxLetras} caracteres)",
			Text = _nomeDigitadoDoPlaneta,
			MaxLength = Jandirus.Core.World.Batismo.MaxLetras,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		campo.TextChanged += t => _nomeDigitadoDoPlaneta = t;
		void Batizar()
		{
			if (_nomeDigitadoDoPlaneta.Trim().Length == 0) { Chat.Sistema("escreva o nome antes."); return; }
			cli.SendVerbo("planeta_nomear", _nomeDigitadoDoPlaneta.Trim());
		}
		campo.TextSubmitted += _ => Batizar();

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 6);
		linha.AddChild(campo);
		var batizar = new Button { Text = "Batizar", TooltipText = "o servidor inteiro fica sabendo do nome novo" };
		batizar.Pressed += Batizar;
		linha.AddChild(batizar);

		// TIRAR O NOME e desfazer um batismo: so quem domina (e o admin, que modera).
		if (batizado && direito is Jandirus.Core.World.Batismo.Direito.Dono or Jandirus.Core.World.Batismo.Direito.Admin)
		{
			var tirar = new Button { Text = "Tirar o nome", TooltipText = $"o planeta volta a se chamar {cli.Zone.Name}" };
			tirar.Pressed += () => cli.SendVerbo("planeta_desnomear");
			linha.AddChild(tirar);
		}
		corpo.AddChild(linha);

		Nota(direito switch
		{
			Jandirus.Core.World.Batismo.Direito.Descobridor =>
				"Você foi o primeiro a pisar aqui: o batismo é seu -- e é um só.",
			Jandirus.Core.World.Batismo.Direito.Dono =>
				"Você domina este planeta: o nome é seu pra dar (uma troca a cada 5 minutos).",
			_ => "Admin: você pode trocar ou tirar o nome de qualquer planeta.",
		}, corpo);
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
	/// A tela e EMPURRADA uma vez so (v0.3.0: era uma por sessao -- ver `GameServer.Refugio.OferecerORefugio`),
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
			TooltipText = "o mundo vivo mais perto de onde era casa, a Terra (o ponto de encontro) ou um planeta que você conquistou",
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
	/// ============================ CADA BOTAO E DE QUEM O TEM (dono, 2026-10-10) ============================
	/// "Erguer estátua" aparecia pra todo mundo, com a recusa ("apenas Namekuseijins do Clã do Dragão...")
	/// como resposta -- e o dono, com um Saiyajin, viu um cartao de oito botoes dos quais nao podia usar
	/// nenhum, encimado por uma leitura de RADAR sem ter radar. Agora o `BotaoDeVerbo` pergunta a tabela de
	/// requisitos (`Core/Verbos/RequisitosDosVerbos.cs`):
	///
	///   * os do CLA DO DRAGAO (erguer, erguer + supremo) so pra Namekuseijin dessa classe, e so onde ainda
	///     nao ha estatua; "Despertar as esferas" so pra quem ergueu a deste mundo;
	///   * "Radar" -- e a linha do sinal dourado -- so com o Dragon Radar na mochila;
	///   * ver, pegar, largar e invocar so num mundo que TEM estatua: as sete nascem dela e nunca saem do
	///     mundo dela, entao sem estatua nao ha o que pegar. E o recorte de LUGAR que este bloco ja fazia
	///     (Super so no espaco, estatua so em terra), um passo adiante.
	///
	/// QUEM AUTORIZA CONTINUA SENDO O SERVIDOR: o botao some da tela, a validacao nao sai de la.
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
		//
		// SO COM O DRAGON RADAR. O servidor nem calcula a frase pra quem nao o tem (`SinalDouradoDe`); o
		// crivo daqui e o mesmo requisito, lido da mochila, e existe pra a linha sumir no instante em que
		// o aparelho sai da mao -- o pacote que a esvazia chega ate um segundo depois.
		if (cli.SinalDourado.Length > 0
			&& RequisitosDosVerbos.De(RequisitosDosVerbos.LinhaDoRadar).Atende(PosseDoCliente.Atual))
		{
			var l = new Label { Text = cli.SinalDourado, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			l.AddThemeColorOverride("font_color", Tema.Nutricao);
			l.AddThemeFontSizeOverride("font_size", 13);
			corpo.AddChild(l);
		}

		Linha("Super Esferas suas", $"{cli.MinhasSupers}/{Jandirus.Core.Magic.SuperEsferas.Total}",
			  cli.MinhasSupers > 0 ? Tema.Destaque : null, corpo);

		GridContainer grade = GradeDeBotoes(corpo);
		foreach ((string rotulo, string cmd, string arg, string dica) in
			new (string, string, string, string)[]
			{
				("Ver as esferas daqui", "db_ver", "",
					"que estátua manda neste mundo, quantos pedidos ela dá e se as sete estão acordadas"),
				("Radar", "db_radar", "",
					"o Dragon Radar da sua mochila. Só acha esfera ACORDADA, e só deste mundo"),
				("Pegar esfera", "db_pegar", "", "a que estiver ao seu alcance"),
				("Largar tudo", "db_largar", "", "põe no chão as que você carrega"),
				("INVOCAR", "db_invocar", "",
					"com as sete reunidas: no chão ao seu redor ou com você"),

				("Erguer Estátua do Dragão", "db_estatua", "",
					"só num mundo que você domine (ou na Terra, se for o Guardião)"),

				// ============================ ELE E A UNICA PORTA DO DESEJO SUPREMO ============================
				// `namekian.dm:101-109`: o "Strongest in the Universe" nao sai da escada de poder -- ele e
				// COMPRADO por 2.000.000 de zeni **na hora de erguer a estatua**, e nunca depois. No
				// original isso e um `alert()` que aparece sozinho; aqui virou argumento, e um argumento
				// sem botao seria um desejo que so existe pra quem digita verbo. Segundo botao, e nao um
				// campo de texto, porque a escolha e binaria.
				// ========================================================================================
				("Erguer + gravar o SUPREMO (2.000.000 zeni)", "db_estatua", "supremo",
					"grava nestas esferas o desejo O MAIS FORTE DO UNIVERSO -- a única forma de ele "
					+ "existir num set de jogador. Cobra na hora"),
				// O MESMO GESTO, PAGO EM RECURSOS (dono, 2026-10-10): o dobro, e a moeda vai no fim do argumento.
				("Erguer + gravar o SUPREMO (4.000.000 recursos)", "db_estatua",
					Jandirus.Core.Economia.Recurso.PedidoPago("supremo", Jandirus.Core.Economia.Moeda.Recursos),
					"o mesmo desejo O MAIS FORTE DO UNIVERSO, pago em recursos (o dobro). Cobra na hora"),
				("Despertar as esferas", "db_reviver", "",
					"só vivo -- é a única volta de um set inerte"),

				("Super Esferas: placar", "sdb_status", "",
					"quem tem quantas, e quantas ainda estão livres (sem dizer ONDE)"),
				("Reivindicar Super Esfera", "sdb_reivindicar", "",
					"chegue perto dela no espaço. 10s se estiver livre, 5 MINUTOS se for de alguém -- "
					+ "e o dono é avisado"),
				("Chamar o Super Shenron", "sdb_invocar", "",
					"com as sete Super Esferas. A Língua dos Deuses é a Fase 2"),
			})
			// O RECORTE (de LUGAR e de POSSE) e o da tabela de requisitos, e quem o aplica e o
			// `BotaoDeVerbo`: Super so no espaco, estatua e esferas so em terra, e cada botao so pra quem
			// tem o que ele pede. Botao que nunca poderia funcionar dali -- ou com este corpo -- e ruido.
			BotaoDeVerbo(cli, rotulo, cmd, arg, dica, grade);

		// NENHUM BOTAO NASCEU (um mundo sem estatua, visto por quem nao tem radar nem pode erguer uma): a
		// grade vazia sai, senao ela deixa um vao no cartao.
		if (grade.GetChildCount() == 0)
		{
			corpo.RemoveChild(grade);
			grade.QueueFree();
		}

		// SEM ESTATUA NAO HA ESFERA, e isso se diz em vez de se mostrar quatro botoes que nao tem o que
		// pegar: e a frase que "Ver as esferas daqui" respondia ao ser apertado num mundo assim.
		if (emPlaneta && (PosseDoCliente.Atual.Lugar & Lugar.ComEstatua) == 0)
			Nota("Este mundo não tem Estátua do Dragão -- e sem estátua não há esferas aqui. Só um "
				 + "Namekuseijin do Clã do Dragão ergue uma, num mundo que ele domine (ou na Terra, sendo o "
				 + "Guardião).", corpo);

		Nota(noEspaco
			? "As Super Esferas têm o tamanho de um planetoide: não se carregam. Quem defende não "
			+ "precisa vencer o ladrão -- basta derrubá-lo ou afastá-lo da esfera."
			: "As sete esferas de um mundo nunca saem dele: largue uma em outro planeta e ela volta "
			+ "sozinha. Depois de um pedido, elas apagam e se espalham de novo.", corpo);

		BlocoDoPedido(cli, noEspaco);
	}

	/// <summary>
	/// **O PEDIDO** -- a porta de volta pra janela de desejos (`MenuDeDesejos`), no cartao proprio.
	///
	/// ============================ A CAIXA DE TEXTO SAIU, E OS BOTOES QUE A USAVAM TAMBEM ============================
	/// Aqui morava uma linha de texto -- "id do desejo (e o alvo, se pedir)" -- e os botoes que a mandavam: PEDIR,
	/// PRONUNCIAR, Emprestar as sete, Pedidos por invocacao, ACEITO O PRECO. O dono (2026-10-10): *"ao invocar o
	/// dragao das esferas, deveria aparecer um menu com varios desejos q vc pode escolher ... e nao ter q abrir um
	/// menu e colocar um id de pedido"*.
	///
	/// O QUE A CAIXA DEFENDIA CONTINUA DE PE: a lista de desejos nao e fixa (depende do poder do set, de quantos
	/// pedidos ele da, do genoma e do cargo de quem pede), e desenha-la aqui seria uma SEGUNDA casa pra escada do
	/// `WishTable.dm`. Quem manda a lista continua sendo o servidor -- so que num pacote (`S2C.MenuDoDragao`), com
	/// o motivo de cada desejo apagado e quem cada um alcanca, e nao em vinte linhas de chat. A janela abre sozinha
	/// quando o dragao sobe; este cartao e pra quem a fechou.
	///
	/// TUDO O QUE ERA BOTAO DAQUI VIROU LINHA DELA, e so aparece pra quem pode usar: "Pedidos por invocacao" (quem
	/// ergueu a estatua), "Emprestar as sete", "PRONUNCIAR", "Retomar a guarda", "ACEITO O PRECO". Ficam dois
	/// botoes: o que abre a janela, e "Derrubar a estatua", que a abre ja na confirmacao -- derrubar e de qualquer
	/// um que esteja ao lado dela, e quem quer derrubar nao procura por "desejos".
	///
	/// NENHUM DOS DOIS MANDA DESEJO: mandam `db_menu`, que PEDE a janela, e ela abre quando o servidor responde
	/// (com a lista de agora, nunca com uma guardada). Nascem pelo `BotaoDeVerbo`, como todo botao desta aba.
	/// ================================================================================================================
	/// </summary>
	private void BlocoDoPedido(GameClient cli, bool noEspaco)
	{
		VBoxContainer corpo = Cartao(noEspaco ? "O pedido ao Super Shenron" : "O pedido ao dragão");
		GridContainer grade = GradeDeBotoes(corpo);

		// O ROTULO "Ver os desejos" E CONTRATO: a `--diagnav` e a `--diagabas` acham este botao pelo texto, em
		// terra e no espaco. O que mudou e o que ele faz: pede o MENU ao servidor (`db_menu`), que responde
		// abrindo a janela -- e nao mais uma lista no chat. Ele aparece em todo lugar, mesmo num mundo sem
		// estatua: e por ele que quem tem assunto com o Super Shenron (as sete, uma guarda oferecida, o
		// preco esperando) chega a janela dele estando num planeta.
		BotaoDeVerbo(cli, "Ver os desejos", "db_menu", "",
			noEspaco
				? "abre a janela do Super Shenron: o que ele atende, o que falta pra você pedir, e os gestos da "
				+ "guarda das sete (emprestar, retomar, aceitar, pronunciar)"
				: "abre a janela de desejos: o que o dragão deste mundo atende, quantos pedidos restam e, com ele "
				+ "de pé, o pedido. Ela abre sozinha quando você invoca",
			grade);

		// "DERRUBAR A ESTATUA" SO NUM MUNDO QUE TEM UMA (dono, 2026-10-10: botao que nunca poderia funcionar
		// dali e ruido). O recorte nao esta escrito aqui: e a linha deste botao na tabela de requisitos
		// (`RequisitosDosVerbos`, "mundo com Estátua do Dragão"), e quem o aplica e o `BotaoDeVerbo` -- como em
		// todo botao do cartao de cima. Estar AO LADO da estatua e circunstancia, e quem a confere e o
		// servidor: longe, a janela abre sem a linha e a frase diz por que.
		BotaoDeVerbo(cli, "Derrubar a estátua", "db_menu", "foco " + Jandirus.Core.Magic.AcoesDoDragao.Derrubar,
			"apaga as sete deste mundo, para sempre. Precisa estar ao lado dela; a janela pede a confirmação",
			grade);

		Nota(noEspaco
			? "Só quem fala a LÍNGUA DOS DEUSES desperta o Super Shenron. Quem não fala empresta as sete a quem "
			+ "fale -- pela janela -- e o desejo continua sendo de quem emprestou."
			: "A janela de desejos abre sozinha quando o dragão sobe. Quem ergueu a estátua muda nela os pedidos "
			+ "por invocação.",
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
	//
	// E O BATISMO: o nome do lugar (a faixa e o cartao), quem batizou, quem descobriu e o MEU direito -- os
	// quatro chegam pelo `S2C.NomesDePlanetas` sem mexer em mais nada desta assinatura.
	//
	// E A POSSE: que botoes de dominio e de esferas NASCEM depende do que este personagem tem (raca, radar,
	// dominio, estatua neste mundo -- ver `BotaoDeVerbo`). Ela entra pelo que a tabela de requisitos
	// responde, e nao campo a campo: a pagina se refaz quando um botao passa a existir, e so entao.
	private string ExtraDaAssinaturaDeMundo(SheetState f) =>
		GameClient.Instance is { } c
			? $"{c.RefugioPrecisa}|{c.RefugioNatal}|{c.NomeDoLugar}|{c.DireitoDeBatismo}|{c.DescobridorDaqui}"
			+ $"|{(Jandirus.Core.World.ChaveDePlaneta.Da(c.Zone) is { } chave ? c.NomesDosPlanetas.De(chave.Texto)?.PorQuem : "")}"
			+ $"|{PosseDoCliente.Assinatura()}"
			+ $"|{AssinaturaDoKiDivino(c)}"
			+ $"|{AssinaturaDoServicoDoAlem(c)}"
			: "";
}
