using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA ADMIN -- os verbs de administracao, mais o que eles nao dao conta de fazer sozinhos:
/// aviso ao servidor, clima, forcar formas, contas, o alvo marcado, e a zona de perigo.
///
/// ============================ CADA SECAO E UM CARTAO, E A ULTIMA E VERMELHA ============================
/// Redesenhada em 2026-09-03 na mesma lingua da Learning (ver MenuJogo.Pecas.cs): cada secao virou
/// um <see cref="Cartao"/>, os avisos longos viraram notas curtas, os verbs vieram em cartoes por
/// tema (a mesma tabela-por-nome da aba Other), a fileira de botoes do clima passou a QUEBRAR LINHA
/// em vez de alargar o painel (era ela que empurrava o menu de 760 pra 880 px so nesta aba), e a
/// zona de perigo ganhou a borda em <see cref="Tema.Perigo"/>.
///
/// O QUE NAO MUDOU, e nao pode mudar: os textos de todo botao, os placeholders e o `MaxLength` de
/// cada campo, os verbs que cada clique manda e os campos guardados fora da pagina. A `--diagadmin`,
/// a `--diagmudez` e a `--diagtecla` acham esses controles por texto.
/// ==================================================================================================
/// </summary>
public partial class MenuJogo
{
	// =====================================================================
	// ADMIN -- os verbs, mais o que eles nao dao conta de fazer
	// =====================================================================
	/// <summary>
	/// O QUE O ADMIN DIGITOU, guardado FORA da pagina.
	///
	/// A pagina inteira e destruida e remontada quando a assinatura muda (ver
	/// <see cref="Assinatura"/>), e uma <c>LineEdit</c> destruida leva o texto junto. Guardando
	/// aqui, a remontagem devolve o que estava escrito -- que e o que qualquer um espera de um
	/// campo de texto, e o que faz a diferenca entre "digitei e sumiu" e um painel usavel.
	/// </summary>
	private string _avisoDigitado = "", _contaDigitada = "", _textoDoAlvo = "";

	/// <summary>
	/// O CODIGO DA LIMPEZA TOTAL, digitado. Fora da pagina pelo mesmo motivo dos tres acima -- e com
	/// um agravante proprio: aqui o campo tem PRAZO de um minuto, e perder o que foi digitado a cada
	/// remontagem transformaria a confirmacao numa corrida contra o relogio do menu.
	///
	/// Zerado quando chega uma previa nova (ver <see cref="AoLimpeza"/>): codigo de outra lista nao
	/// pode ficar de bobeira num campo que confirma o apagamento do servidor.
	/// </summary>
	private string _codigoDaLimpeza = "";

	/// <summary>Ja pedi a lista de contas nesta sessao? Ver o comentario em <see cref="PainelDeContas"/>.</summary>
	private bool _pediContas;

	/// <summary>O clima escolhido no painel de admin, e a forca dele. Sobrevivem ao redesenho.</summary>
	// Nasce em `Limpo`: quem abre esta aba quase sempre quer TIRAR o clima (ver o Oozaru, que so
	// aparece com a lua livre), nao por chuva. Por chuva e a excecao, e a excecao pode dar um clique.
	private Jandirus.Core.World.TipoDeClima _climaEscolhido = Jandirus.Core.World.TipoDeClima.Limpo;
	private float _forcaEscolhida = 1f;

	// =====================================================================
	// A TABELA DE TEMAS DOS VERBS DE ADMIN
	// =====================================================================
	/// <summary>
	/// A ORDEM DOS CARTOES de verbs de admin. Segue as secoes em que `VerbosDoJogo.Registrar` os
	/// declara ("corpo do alvo", "ir e trazer", "enxergar", "punir", "dar", "mundo e servidor", a
	/// destruicao de planeta) -- e a ordem em que eles fazem sentido de ler, e a que o chat do
	/// servidor tambem usa. "Diversos" por ultimo, pra verb novo que ninguem pendurou.
	/// </summary>
	private static readonly string[] OrdemDosGruposDeAdmin =
	[
		"Corpo do alvo", "Ir e trazer", "Enxergar", "Punir", "Dar", "Mundo e servidor", "Planeta", Diversos,
	];

	/// <summary>VERB DE ADMIN -> TEMA, pelo nome (os literais de `VerbosDoJogo.Registrar`).</summary>
	private static readonly Dictionary<string, string> GrupoDeAdminPorNome = new(StringComparer.OrdinalIgnoreCase)
	{
		["Heal"] = "Corpo do alvo",
		["Revive Target"] = "Corpo do alvo",
		["Knock Out Target"] = "Corpo do alvo",
		["Kill Target"] = "Corpo do alvo",
		["Heal Everyone"] = "Corpo do alvo",
		["Knock Out Everyone Here"] = "Corpo do alvo",

		["Go To Target"] = "Ir e trazer",
		["Bring Target"] = "Ir e trazer",
		["Bring Everyone"] = "Ir e trazer",
		["Send Target To Spawn"] = "Ir e trazer",
		["Goto Spawn"] = "Ir e trazer",
		["Release From Time Chamber"] = "Ir e trazer",
		["Ir Para A Estrela"] = "Ir e trazer",

		["Assess Target"] = "Enxergar",
		["Assess All"] = "Enxergar",
		["Who (admin)"] = "Enxergar",
		["Races"] = "Enxergar",
		["IPs"] = "Enxergar",
		["Galaxy Status"] = "Enxergar",

		["Boot Target"] = "Punir",
		["Mute Target"] = "Punir",
		["Unmute All"] = "Punir",

		["Give Milestone"] = "Dar",
		["Give Milestone to Everyone"] = "Dar",
		["Master Current Form"] = "Dar",

		["Toggle Invisible"] = "Mundo e servidor",
		["Restore Scenery"] = "Mundo e servidor",
		["Save All"] = "Mundo e servidor",
		["Weather Report"] = "Mundo e servidor",
		["Release Weather"] = "Mundo e servidor",

		["Villain (target)"] = "Planeta",
		["Kill This Planet (slow)"] = "Planeta",
		["Destroy This Planet"] = "Planeta",
		["Abort Planet Death"] = "Planeta",
		["Restore Planet"] = "Planeta",
	};

	private static string GrupoDeAdmin(Verbo v) =>
		GrupoDeAdminPorNome.TryGetValue(v.Nome, out string? grupo) ? grupo : Diversos;

	/// <summary>
	/// O PAINEL DE CLIMA DA ABA ADMIN -- forcar o ceu pra poder OLHAR o efeito.
	///
	/// ============================ POR QUE ISTO NAO E UM VERB DA LISTA ============================
	/// Os verbs da lista sao botoes sem argumento. Clima pede DOIS (qual e quao forte), e a
	/// alternativa seria um verb por tipo -- onze botoes chamados "Force Rain", "Force Snow"... que
	/// e o tipo de lista que ninguem le. Um seletor com um botao diz a mesma coisa em uma linha, e
	/// e o mesmo caminho que o painel de contas ja usa pra promover por nome.
	/// =============================================================================================
	///
	/// A LISTA MOSTRA O QUE CAI AQUI PRIMEIRO. Todo tipo continua escolhivel (forcar neve em Vampa
	/// e exatamente o tipo de coisa que se quer poder fazer pra conferir o desenho da neve), mas o
	/// que pertence ao planeta vem em cima e sem marca -- e o resto vem marcado com um ponto.
	/// </summary>
	private void PainelDoClima(GameClient cli)
	{
		VBoxContainer corpo = Cartao("Clima deste planeta");

		Jandirus.Core.World.ClimaDoPlaneta daqui = World.Instancia?.ClimaDoLugar
											   ?? Jandirus.Core.World.ClimaDoPlaneta.Nenhum;
		Jandirus.Core.World.EstadoDoClima agora = World.Instancia?.TempoQueFaz ?? default;

		// SEM A PORCENTAGEM AQUI. Ela muda a cada quadro durante a transicao e esta pagina so se
		// remonta quando o TIPO muda -- o numero ficaria congelado mentindo. Quem quer o valor
		// vivo usa o verb "Weather Report", que imprime no chat na hora em que se pede.
		Linha("Agora", agora.Ativo
			? Jandirus.Core.World.Clima.Nome(agora.Tipo) + (agora.Forcado ? " (forcado)" : " (natural)")
			: "ceu limpo", null, corpo);

		// ---- o seletor ----
		var escolha = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var ordem = new List<Jandirus.Core.World.TipoDeClima>();
		foreach (Jandirus.Core.World.TipoDeClima t in Enum.GetValues<Jandirus.Core.World.TipoDeClima>())
		{
			// ============================ "LIMPO" E UM CLIMA, NAO O BOTAO DE SOLTAR ============================
			// Isto pulava o `Limpo` com o argumento de que "limpo e o botao de soltar". Nao e: soltar
			// devolve o ceu ao SORTEIO -- ele pode cair em chuva no quadro seguinte. Forcar limpo e
			// outra coisa: e cravar que nao ha clima nenhum e ele FICA assim.
			//
			// O dono precisou disso e nao tinha: "kd a opçao q pedi de clima limpo, limpar qualquer
			// clima e deixar sem efeito de clima pra deixar a lua livre". Sem forcar limpo, testar o
			// Oozaru dependia de o sorteio colaborar -- e o sorteio nao colabora sob demanda.
			// ================================================================================================
			ordem.Add(t);
		}
		ordem.Sort((a, b) =>
		{
			bool na = Array.IndexOf(daqui.Permitidos, a) >= 0, nb = Array.IndexOf(daqui.Permitidos, b) >= 0;
			return na == nb ? string.CompareOrdinal(a.ToString(), b.ToString()) : na ? -1 : 1;
		});

		for (int i = 0; i < ordem.Count; i++)
		{
			bool nativo = Array.IndexOf(daqui.Permitidos, ordem[i]) >= 0;
			escolha.AddItem(Jandirus.Core.World.Clima.Nome(ordem[i]) + (nativo ? "" : "  ·"), i);
			if (ordem[i] == _climaEscolhido) escolha.Selected = i;
		}
		escolha.TooltipText = "os marcados com · nao caem aqui naturalmente -- forcar continua valendo";
		escolha.ItemSelected += i => _climaEscolhido = ordem[(int)i];

		var seletor = new HBoxContainer();
		seletor.AddThemeConstantOverride("separation", 6);
		seletor.AddChild(escolha);

		// ---- a forca ----
		var forca = new HSlider
		{
			MinValue = 10, MaxValue = 100, Step = 5, Value = _forcaEscolhida * 100,
			CustomMinimumSize = new Vector2(120, 0),
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			TooltipText = "quao forte: garoa a temporal",
		};
		var rotuloForca = new Label { Text = $"{_forcaEscolhida:P0}", CustomMinimumSize = new Vector2(46, 0) };
		forca.ValueChanged += v =>
		{
			_forcaEscolhida = (float)v / 100f;
			rotuloForca.Text = $"{_forcaEscolhida:P0}";
		};
		seletor.AddChild(forca);
		seletor.AddChild(rotuloForca);
		corpo.AddChild(seletor);

		// ---- os botoes, numa fileira que QUEBRA LINHA ----
		// HFlow E NAO HBox: com os quatro botoes atras do seletor, a HBox de antes tinha ~880 px de
		// largura MINIMA e alargava o painel inteiro so nesta aba (o menu e 760 px). A fileira que
		// quebra cabe em qualquer largura, e o painel para de mudar de tamanho conforme a aba.
		var botoes = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		botoes.AddThemeConstantOverride("h_separation", 6);
		botoes.AddThemeConstantOverride("v_separation", 4);

		var bForcar = new Button
		{
			Text = "Forcar",
			TooltipText = "poe este clima nesta zona por 20 min. Vale pra todo mundo que esta aqui.",
		};
		// A FORCA VAI NO MESMO ARGUMENTO, depois de uma barra -- o canal de verb leva UMA string, e
		// e o mesmo formato que `admin_marco` ja usa ("id|quantos").
		bForcar.Pressed += () => cli.SendVerbo("admin_clima",
			$"{_climaEscolhido}|{_forcaEscolhida.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
		botoes.AddChild(bForcar);

		var bSoltar = new Button
		{
			Text = "Voltar ao natural",
			Disabled = !agora.Forcado,
			TooltipText = "solta o ceu: o clima volta a ser sorteado pelo relogio do mundo",
		};
		bSoltar.Pressed += () => cli.SendVerbo("admin_clima_natural");
		botoes.AddChild(bSoltar);

		// ============================ O CEU DO OOZARU ============================
		// Fica NESTA fileira, ao lado do clima, porque as duas coisas sao a mesma pergunta pro
		// testador: "por que o botao de olhar pra lua nao aparece?". A resposta e sempre uma das
		// duas -- ou nao e lua cheia, ou o ceu esta encoberto -- e ter os dois botoes juntos
		// responde as duas sem o testador precisar saber qual era.
		// ====================================================================
		var bLua = new Button
		{
			Text = "Lua cheia agora",
			TooltipText = "adianta o relogio do mundo ate a proxima noite de lua cheia NESTA zona "
						+ "(o clima continua como esta -- se estiver encoberta, limpe ao lado)",
		};
		bLua.Pressed += () => cli.SendVerbo("admin_lua_cheia");
		botoes.AddChild(bLua);

		// O IRMAO DELE, e ele fica aqui pelo mesmo argumento do bloco acima: a pergunta do testador
		// tambem e sobre o ceu, so que a mais banal ("por que nao da pra ver nada?"). Um dia inteiro
		// dura 24 minutos, entao metade das rodadas cai no escuro -- e julgar cor de cabelo no escuro
		// foi exatamente o que aconteceu na bancada `--diagolhada`. Ver `GameServer.AdminMeioDia`.
		var bDia = new Button
		{
			Text = "Meio-dia agora",
			TooltipText = "adianta o relogio do mundo ate o proximo meio-dia NESTA zona -- pra julgar "
						+ "cor (o clima continua como esta: meio-dia sob tempestade tambem e escuro)",
		};
		bDia.Pressed += () => cli.SendVerbo("admin_meio_dia");
		botoes.AddChild(bDia);

		corpo.AddChild(botoes);

		// UMA NOTA SO, e as duas sao excludentes: ou o lugar tem clima proprio (e a nota diz qual
		// cai aqui), ou nao tem (e a nota diz que forcar funciona mesmo assim).
		if (!daqui.Existe)
			Nota("Este lugar não tem clima próprio (o `HasWeather=0` do DM) -- mas forçar funciona.", corpo);
		else if (daqui.Permitidos.Length > 0)
			Nota("Cai aqui naturalmente: " + string.Join(", ", daqui.Permitidos.Select(Jandirus.Core.World.Clima.Nome)), corpo);
	}

	/// <summary>
	/// O NOME DA ESCADA em portugues, pros cabecalhos do painel de formas.
	///
	/// ============================ POR QUE O `_ =>` DEVOLVE O NOME DO ENUM ============================
	/// Porque linha nova tem que APARECER no painel no mesmo dia em que entra no Core, nem que seja
	/// com o nome cru ("LegendaryPrimal"). Um `_ => ""` -- ou pior, um dicionario que so contivesse as
	/// dez de hoje -- daria um cabecalho vazio (ou uma excecao) e as formas da linha nova ficariam
	/// penduradas em lugar nenhum. Feio e visivel vence bonito e ausente numa ferramenta de teste.
	/// ==============================================================================================
	/// </summary>
	private static string NomeDaLinha(Jandirus.Core.Forms.LinhaDeForma l) => l switch
	{
		Jandirus.Core.Forms.LinhaDeForma.Saiyajin => "Saiyajin",
		Jandirus.Core.Forms.LinhaDeForma.Futuro => "Futuro",
		Jandirus.Core.Forms.LinhaDeForma.Legendary => "Legendary",
		Jandirus.Core.Forms.LinhaDeForma.LegendaryPrimal => "Legendary Primal",
		Jandirus.Core.Forms.LinhaDeForma.GodKi => "Ki divino",
		Jandirus.Core.Forms.LinhaDeForma.GodKiRose => "Ki divino · Rose",
		Jandirus.Core.Forms.LinhaDeForma.Mistico => "Mistico",
		Jandirus.Core.Forms.LinhaDeForma.UltraInstinct => "Ultra Instinto",
		Jandirus.Core.Forms.LinhaDeForma.UltraEgo => "Ultra Ego",
		Jandirus.Core.Forms.LinhaDeForma.Oozaru => "Oozaru (fera)",
		// AS QUATRO LINHAS RACIAIS. O `_ =>` ja as mostrava com o nome cru do enum ("FrostDemon"),
		// que e o que este metodo promete no cabecalho -- mas "cru" era pra ser o estado do PRIMEIRO
		// dia da linha, e nao o definitivo.
		Jandirus.Core.Forms.LinhaDeForma.FrostDemon => "Frost Demon",
		Jandirus.Core.Forms.LinhaDeForma.Namekuseijin => "Namekuseijin",
		Jandirus.Core.Forms.LinhaDeForma.Heran => "Heran",
		Jandirus.Core.Forms.LinhaDeForma.Alien => "Alien",
		_ => l.ToString(),
	};

	/// <summary>
	/// FORCAR QUALQUER FORMA, E SOLTAR O KI -- as duas ferramentas de teste que o dono pediu
	/// ("forçar me transformar em qualquer transformaçao do jogo pra eu testar, assim como uma opçao
	/// de liberar as skills de ki pra eu ja poder usar aura etc").
	///
	/// ============================ A LISTA SAI DO CATALOGO, NUNCA DA MAO ============================
	/// Um botao por entrada de <see cref="Jandirus.Core.Forms.Catalogo.Todas"/>. E a mesma razao de o
	/// catalogo existir: "uma forma nova e uma entrada nova aqui e mais nada". Uma lista escrita a mao
	/// aqui seria a 37a coisa a lembrar de atualizar, e a que ninguem lembraria -- a forma nova
	/// simplesmente nao teria como ser testada, calada. O rotulo e o `Nome` e a dica de mouse e o
	/// `Desc`; os dois ja estao no dado.
	///
	/// ============================ POR QUE BOTAO E NAO UM SELETOR ============================
	/// O painel de clima ao lado usa <c>OptionButton</c> porque clima pede DOIS argumentos (qual e quao
	/// forte) -- escolher e so metade do gesto. Forma pede UM, e um seletor transformaria "quero ver o
	/// Blue" em abrir-rolar-escolher-fechar-clicar. Com botao e um clique. Sao 35 deles, e e por isso
	/// que vem AGRUPADOS: 35 numa fila e uma lista que ninguem le.
	///
	/// ============================ O AGRUPAMENTO E O MESMO DO SERVIDOR ============================
	/// Por <see cref="Jandirus.Core.Forms.LinhaDeForma"/>, e dentro dela por `Ordem` -- identico ao
	/// `ListarFormas` que responde ao `admin_forma` sem argumento. A pergunta que o admin faz e sempre
	/// "quais sao os degraus da escada X", e as duas respostas do jogo (a do chat e a da aba) dizerem a
	/// mesma coisa na mesma ordem e o que impede uma de parecer errada.
	///
	/// O `GroupBy` respeita a ordem de DECLARACAO do catalogo, que ja e a ordem em que as linhas fazem
	/// sentido de ler (Saiyajin primeiro, Oozaru por ultimo). Nao ha sort de linha aqui de proposito:
	/// alfabetico jogaria "Futuro" antes de "Saiyajin", e o catalogo e quem sabe a ordem boa.
	/// =============================================================================================
	/// </summary>
	private void PainelDeFormas(GameClient cli)
	{
		VBoxContainer corpo = Cartao("Forçar transformação");

		// ---------------------------------------------------------- em QUEM cai
		// "alvoId|forma", o mesmo formato do `admin_skill_dar` -- e o `PorNome` do servidor devolve
		// nulo pro id 0, entao SEM alvo marcado o verb cai em quem clicou. Nao ha estado novo aqui:
		// a caixa de "aplicar no alvo" seria um campo a manter sincronizado com uma verdade que o
		// `cli.AlvoId` ja carrega. A nota abaixo so LE essa verdade -- e como `AlvoId` esta na
		// assinatura da aba (ver `Assinatura`), marcar alguem remonta a pagina e a nota acompanha.
		bool noAlvo = cli.AlvoId != 0;
		Nota(noAlvo
			? "Cai no CORPO MARCADO (o do duplo clique), e não em você. "
			: "Cai em VOCÊ (marque alguém com duplo clique pra empurrar outro corpo). ", corpo)
			.Text += "Ignora BP, maestria, linhagem, classe, raça e Ki: as formas que interessa testar SÃO as "
				   + "trancadas. O Ki entra cheio e a cinemática toca pela regra normal; a fera (Oozaru) não "
				   + "aparece marcada e precisa de rabo inteiro pra durar mais que um tique.";

		Jandirus.Core.Forms.FormaDef? defAtual =
			Jandirus.Core.Forms.Catalogo.PorRede(_atributos.FormaAtual);
		// A MARCA SO VALE PRA MIM: `FormaAtual` e a MINHA ficha. Com alvo marcado, o "em uso" seria
		// uma mentira sobre o corpo errado -- entao ela some.
		string atual = noAlvo ? "" : defAtual?.Id ?? Jandirus.Core.Forms.Catalogo.IdBase;

		// ---------------------------------------------------------- a linha de cima
		var topo = new HBoxContainer();
		topo.AddThemeConstantOverride("separation", 6);

		var bBase = new Button
		{
			Text = "Voltar ao normal",
			TooltipText = "forca a forma base -- desfaz TAMBEM o Oozaru, que e um estado paralelo "
						+ "a escada e nao sai com o X",
		};
		bBase.Pressed += () => cli.SendVerbo("admin_forma",
			$"{cli.AlvoId}|{Jandirus.Core.Forms.Catalogo.IdBase}");
		topo.AddChild(bBase);

		var bKi = new Button
		{
			Text = "Liberar skills de Ki",
			TooltipText = "da o Basic Ki Control e o degrau que acende o canPower: segurar C passa a "
						+ "carregar (aura), e os verbs Power Control e Conceal Power aparecem",
		};
		bKi.Pressed += () => cli.SendVerbo("admin_liberar_ki", cli.AlvoId.ToString());
		topo.AddChild(bKi);

		corpo.AddChild(topo);

		// ---------------------------------------------------------- uma faixa por escada
		foreach (var linha in Jandirus.Core.Forms.Catalogo.Todas
			.Where(d => d.Id != Jandirus.Core.Forms.Catalogo.IdBase)
			.GroupBy(d => d.Linha))
		{
			var faixa = new HBoxContainer();

			var rotulo = new Label
			{
				Text = NomeDaLinha(linha.Key),
				CustomMinimumSize = new Vector2(140, 0),
				VerticalAlignment = VerticalAlignment.Center,
			};
			rotulo.AddThemeColorOverride("font_color", Tema.TextoFraco);
			rotulo.AddThemeFontSizeOverride("font_size", 12);
			faixa.AddChild(rotulo);

			// HFlow E NAO HBox: a linha Futuro tem dez degraus de nome comprido ("Future Super
			// Saiyajin 10") e o painel nao rola na horizontal (`HorizontalScrollMode.Disabled`) --
			// numa HBox os ultimos botoes ficariam espremidos a zero e sem como clicar.
			var botoes = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

			foreach (Jandirus.Core.Forms.FormaDef d in linha.OrderBy(d => d.Ordem))
			{
				bool emUso = d.Id == atual;
				var b = new Button
				{
					// ============================ AQUI E `d.Nome` CRU, E DE PROPOSITO ============================
					// A aba Formas passa pelo `Catalogo.NomeDe` porque ela fala da ficha de quem esta
					// olhando. Este painel nao: cada botao age sobre `cli.AlvoId`, que pode ser OUTRO
					// jogador -- e a maestria dele nao chega neste cliente (ver `Catalogo.NomeDe`, e o
					// motivo de o `S2C.Forma` carregar um bit e nao um numero). Nomear o botao pela
					// maestria do ADMIN seria escrever na tela um fato sobre a pessoa errada.
					//
					// Entao o que este painel mostra e a ENTRADA do catalogo, que e o que ele manipula:
					// "Super Saiyajin" e a forma, e o "Grade 4" e um estado dela que so o dono da barra
					// tem. Foi por isto que a linha Legendary foi resolvida por RENOME de entrada -- ver
					// o cabecalho da forma `legendary` em `Formas.cs`.
					// ========================================================================================
					Text = (emUso ? "● " : "") + d.Nome,
					// A DICA E O `Desc` DO CATALOGO, palavra por palavra. Escrever outra aqui seria uma
					// segunda descricao da mesma forma pra envelhecer sozinha.
					TooltipText = d.Desc,
				};
				b.AddThemeFontSizeOverride("font_size", 12);
				if (emUso) b.AddThemeColorOverride("font_color", Tema.Destaque);

				// COPIA LOCAL do id: o que a lambda leva pro clique tem que ser o id DESTE botao, e nao
				// o que a variavel do laco calhar de valer depois. (Em C# moderno a variavel de
				// `foreach` ja e por iteracao -- a copia e pra deixar visivel que a captura e de uma
				// STRING e do `cli`, e de mais nada: nada aqui prende o menu vivo depois do relog.)
				string id = d.Id;
				b.Pressed += () => cli.SendVerbo("admin_forma", $"{cli.AlvoId}|{id}");
				botoes.AddChild(b);
			}

			faixa.AddChild(botoes);
			corpo.AddChild(faixa);
		}
	}

	/// <summary>
	/// A ABA DE ADMIN.
	///
	/// ============================ POR QUE ELA NAO E SO A LISTA DE VERBS ============================
	/// A maioria dos verbs de admin do original age sobre alguem que esta na tela, e pra esses o
	/// alvo marcado por duplo clique resolve (e melhor que o `input()` bloqueante do BYOND: nome
	/// se repete e nome se digita errado).
	///
	/// Dois nao cabem nesse molde:
	///   * ANUNCIAR pede um TEXTO, que nenhum botao carrega.
	///   * PROMOVER pede uma CONTA, e a conta que o dono quer promover costuma estar OFFLINE --
	///     nao ha o que marcar com duplo clique. Foi exatamente o pedido: "permita o adm dar
	///     administrador a um dos perfis criados no server".
	///
	/// Por isso os paineis: um cartao de aviso, o clima e as formas (o kit de olhar o jogo), as
	/// contas do servidor com o que cada uma e, o alvo marcado, so entao os verbs -- e a zona de
	/// perigo POR ULTIMO.
	/// ==============================================================================================
	/// </summary>
	private void AbaAdmin()
	{
		if (GameClient.Instance is not { } cli) { ListaDeVerbos(Verbos.Admin); return; }

		// ------------------------------------------------- anunciar
		VBoxContainer anunciar = Cartao("Aviso ao servidor");
		var aviso = new LineEdit
		{
			PlaceholderText = "escreva e aperte Enter (ou o botao)",
			Text = _avisoDigitado,
			// O TETO E O DO FIO, e nao um gosto de interface: o argumento de um verb cabe em
			// `Protocol.MaxArgDeVerbo` bytes, e acima disso o leitor do servidor devolve string
			// VAZIA em vez de truncar. Sem este limite, o aviso longo sumia e o servidor
			// respondia "escreva o aviso antes" -- acusando quem tinha escrito.
			MaxLength = Protocol.MaxArgDeVerbo,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		aviso.TextChanged += t => _avisoDigitado = t;
		void Anunciar()
		{
			if (_avisoDigitado.Trim().Length == 0) return;
			cli.SendVerbo("admin_anunciar", _avisoDigitado.Trim());
			_avisoDigitado = "";
			aviso.Text = "";
		}
		aviso.TextSubmitted += _ => Anunciar();

		var linhaAviso = new HBoxContainer();
		linhaAviso.AddThemeConstantOverride("separation", 6);
		linhaAviso.AddChild(aviso);
		var bAviso = new Button { Text = "Anunciar", TooltipText = "o `Announce` do original: uma linha pra todo mundo" };
		bAviso.Pressed += Anunciar;
		linhaAviso.AddChild(bAviso);
		anunciar.AddChild(linhaAviso);

		// ------------------------------------------------- clima
		PainelDoClima(cli);

		// ------------------------------------------------- formas e Ki
		// LOGO DEPOIS DO CLIMA de proposito: as tres coisas respondem a mesma pergunta de testador
		// ("por que nao consigo ver isto?"). Lua cheia e ceu limpo destravam o Oozaru; forcar forma
		// destrava o resto; liberar Ki destrava a aura. Juntas, elas sao o kit de olhar o jogo.
		PainelDeFormas(cli);

		// ------------------------------------------------- contas
		PainelDeContas(cli);

		// ------------------------------------------------- o que precisa de alvo E de texto
		PainelDoAlvo(cli);

		// ------------------------------------------------- os verbs, em cartoes por tema
		ListaDeVerbos(Verbos.Admin);

		// ------------------------------------------------- a zona de perigo
		// POR ULTIMO, e isso e a unica coisa que a posicao dela pode fazer de util: e preciso
		// ROLAR ATE O FIM da aba mais longa do jogo pra ver o botao que apaga o servidor. Nao e
		// seguranca (a seguranca sao os dois passos e o codigo), e afastar a mao.
		PainelDePerigo(cli);
	}

	/// <summary>
	/// AS CONTAS DO SERVIDOR, com o que cada uma e, e os botoes de promover/rebaixar e banir/perdoar --
	/// na lista, e por nome pra quem nao esta nela.
	/// </summary>
	private void PainelDeContas(GameClient cli)
	{
		VBoxContainer corpo = Cartao("Contas deste servidor");
		if (cli.Contas.Count == 0)
		{
			Nota(_pediContas ? "sem contas pra mostrar." : "pedindo a lista ao servidor...", corpo);
			// UMA VEZ SO, e nao a cada remontagem. Se o servidor devolvesse uma lista VAZIA (um
			// servidor sem armazenamento em disco, por exemplo), pedir dentro do desenho viraria
			// um pedido a cada pacote de ficha -- cinco varreduras da pasta de contas por segundo,
			// pra sempre. O botao "atualizar lista" continua sendo a saida manual.
			if (!_pediContas) { _pediContas = true; cli.SendVerbo("admin_contas"); }
		}
		else
		{
			foreach (GameClient.ContaInfo a in cli.Contas)
			{
				var h = new HBoxContainer();
				h.AddThemeConstantOverride("separation", 6);

				var nome = new Label
				{
					Text = (a.Online ? "● " : "○ ") + a.Conta
						 + (a.Admin ? "   [admin]" : "") + (a.Banida ? "   [banida]" : ""),
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
					VerticalAlignment = VerticalAlignment.Center,
					TooltipText = a.Personagens.Length > 0 ? a.Personagens : "sem personagens",
				};
				nome.AddThemeColorOverride("font_color",
					a.Banida ? Tema.Perigo : a.Admin ? Tema.Destaque : Tema.Texto);
				nome.AddThemeFontSizeOverride("font_size", 13);
				h.AddChild(nome);

				string conta = a.Conta;
				bool eraAdmin = a.Admin, eraBanida = a.Banida;

				var bAdm = new Button
				{
					Text = eraAdmin ? "rebaixar" : "promover",
					TooltipText = eraAdmin
						? "tira o admin desta conta (o `AdminDemote` do original)"
						: "da admin a esta conta -- vale pros tres personagens dela",
				};
				bAdm.Pressed += () => cli.SendVerbo(eraAdmin ? "admin_rebaixar" : "admin_promover", conta);
				h.AddChild(bAdm);

				var bBan = new Button
				{
					Text = eraBanida ? "perdoar" : "banir",
					Disabled = eraAdmin,   // rebaixe antes: o servidor recusaria de qualquer jeito
					TooltipText = eraAdmin ? "rebaixe antes de banir um administrador" : "o `Ban` do original",
				};
				bBan.Pressed += () => cli.SendVerbo(eraBanida ? "admin_perdoar" : "admin_banir", conta);
				h.AddChild(bBan);

				corpo.AddChild(h);
				// OS PERSONAGENS DA CONTA, recuados e menores: sao dado da lista, nao explicacao.
				if (a.Personagens.Length > 0) Nota("      " + a.Personagens, corpo).AddThemeFontSizeOverride("font_size", 11);
			}
		}

		// PELO NOME, pra quem nao esta na lista (conta recem-criada, ou o nome de um PERSONAGEM,
		// que e o que o admin ve andando na tela e nem sempre e igual ao da conta).
		var porNome = new LineEdit
		{
			PlaceholderText = "conta ou nome de personagem",
			Text = _contaDigitada,
			MaxLength = 32,   // o mesmo teto que o `Login` do servidor aplica ao nome da conta
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		porNome.TextChanged += t => _contaDigitada = t;

		var linhaNome = new HBoxContainer();
		linhaNome.AddThemeConstantOverride("separation", 6);
		linhaNome.AddChild(porNome);
		foreach ((string rotulo, string cmd) in new[]
		{
			("promover", "admin_promover"), ("rebaixar", "admin_rebaixar"),
			("banir", "admin_banir"), ("perdoar", "admin_perdoar"),
		})
		{
			var b = new Button { Text = rotulo };
			string comando = cmd;
			b.Pressed += () =>
			{
				if (_contaDigitada.Trim().Length == 0) { Chat.Sistema("escreva a conta ou o personagem."); return; }
				cli.SendVerbo(comando, _contaDigitada.Trim());
			};
			linhaNome.AddChild(b);
		}
		corpo.AddChild(linhaNome);

		var atualizar = new Button { Text = "atualizar lista", Alignment = HorizontalAlignment.Left };
		atualizar.Pressed += () => cli.SendVerbo("admin_contas");
		corpo.AddChild(atualizar);
	}

	/// <summary>
	/// O QUE PRECISA DE ALVO E DE TEXTO. Sao os quatro verbs do original que abriam um `input()`
	/// DEPOIS de escolher o mob: `Give (Skill)`, `Take_Skill`, `Give (Rank)` e `cmd_admin_pm`. O alvo
	/// vem do duplo clique; o texto, daqui. O cartao ACENDE (borda laranja) quando ha alguem marcado:
	/// e o que muda de estado nesta aba, e e o que se quer achar de relance.
	/// </summary>
	private void PainelDoAlvo(GameClient cli)
	{
		bool temAlvo = cli.AlvoId != 0;
		VBoxContainer corpo = Cartao("Sobre o alvo marcado", destaque: temAlvo);
		// A NOTA JUNTA O QUE ERAM DOIS AVISOS (o deste painel e o do fim da lista de verbs): os verbs
		// com "Target" e os botoes daqui agem sobre a MESMA pessoa, entao a explicacao e uma so.
		Nota(temAlvo
			? "O alvo está marcado: os verbs com \"Target\" e os botões abaixo agem sobre ele. Escreva "
			  + "o nome da skill, a chave do cargo, ou a mensagem."
			: "Marque alguém com DUPLO CLIQUE: os verbs com \"Target\" e os botões abaixo agem sobre ele. "
			  + "A permissão é conferida de novo no servidor -- esconder botão nunca foi permissão.", corpo);

		var noAlvo = new LineEdit
		{
			PlaceholderText = "nome da skill / chave do cargo / mensagem",
			Text = _textoDoAlvo,
			// menos o "<id>|" que vai na frente, com folga pra um id de cinco digitos
			MaxLength = Protocol.MaxArgDeVerbo - 8,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		noAlvo.TextChanged += t => _textoDoAlvo = t;
		corpo.AddChild(noAlvo);

		// OS BOTOES EMBAIXO DO CAMPO, numa fileira que quebra: ao lado dele, cinco botoes deixavam o
		// campo com um terco da largura e o placeholder cortado no meio.
		var botoes = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		botoes.AddThemeConstantOverride("h_separation", 6);
		botoes.AddThemeConstantOverride("v_separation", 4);
		foreach ((string rotulo, string cmd, string dica) in new[]
		{
			("dar skill", "admin_skill_dar", "o `Give (Skill)` do original: por nome ou typepath"),
			("tirar skill", "admin_skill_tirar", "o `Take_Skill`: desfaz um presente errado"),
			("dar cargo", "admin_cargo_dar", "o `Give (Rank)`: poe no trono ignorando os requisitos"),
			// DESTITUIR NAO OLHA O ALVO: destitui-se um TRONO pela chave escrita ao lado, e o dono
			// dele costuma estar offline -- que e o motivo mais comum de precisar disto. O botao mora
			// nesta fileira porque e aqui que existe o campo de texto.
			("tirar cargo", "admin_cargo_tirar", "esvazia o trono da chave escrita acima (o alvo marcado e ignorado)"),
			("PM", "admin_pm", "mensagem particular. Os outros admins recebem copia"),
		})
		{
			var b = new Button { Text = rotulo, Disabled = !temAlvo, TooltipText = dica };
			string comando = cmd;
			b.Pressed += () =>
			{
				if (_textoDoAlvo.Trim().Length == 0) { Chat.Sistema("escreva o texto antes."); return; }
				// "alvoId|texto" -- o canal de verbs carrega UM argumento so, e este e o formato que
				// o resto do painel ja usa (ver `admin_marco`).
				cli.SendVerbo(comando, $"{cli.AlvoId}|{_textoDoAlvo.Trim()}");
			};
			botoes.AddChild(b);
		}
		corpo.AddChild(botoes);
	}

	/// <summary>
	/// A LIMPEZA TOTAL DO SERVIDOR -- o pedido do dono: *"um verb pra adm no menu P q LIMPA O SERVER
	/// TODO... dando uma limpa total como se tivesse acabado de rodar pela primeira vez"*.
	///
	/// ============================ UM CLIQUE NAO PODE BASTAR, E AQUI SAO DOIS PASSOS ============================
	/// Isto apaga contas, personagens, construcoes, veiculos, dominios, tronos, discipulados e sagas --
	/// e nao volta. A confirmacao tem que ser dificil de dar POR ACIDENTE (e nao dificil de dar):
	///
	///   1. "Preparar limpeza" nao apaga nada. Ele pede ao servidor o INVENTARIO -- quantas contas,
	///      quantas obras, quantos planetas dominados, quantos jogadores online -- e um codigo de
	///      quatro caracteres sorteado na hora, que vale por um minuto.
	///   2. So depois de LER a lista e DIGITAR esse codigo o segundo botao acende.
	///
	/// O codigo e sorteado no servidor e nunca existiu antes desta tela: nao da pra decorar, nao da
	/// pra colar de um comando de ontem, e um painel esquecido aberto vence sozinho. E como a lista
	/// e recontada a cada previa, quem confirma esta confirmando o numero que esta vendo.
	///
	/// A PERMISSAO NAO ESTA AQUI. Este painel so aparece pra quem tem a aba, e a aba e escondida de
	/// quem nao e admin -- mas os dois verbs sao conferidos de novo no servidor, pelo mesmo funil de
	/// admin de todos os outros. Esconder botao nunca foi permissao.
	/// ========================================================================================================
	/// </summary>
	private void PainelDePerigo(GameClient cli)
	{
		VBoxContainer corpo = CartaoDaZonaDePerigo("ZONA DE PERIGO");

		GameClient.PreviaDeLimpeza p = cli.Limpeza;

		if (p.Codigo.Length == 0)
		{
			Nota("Limpar o servidor apaga TUDO o que foi jogado aqui -- contas e personagens, construções "
				+ "e veículos, planetas dominados, cargos, discipulados e o andamento das sagas -- e não há "
				+ "como desfazer. O botão abaixo NÃO apaga nada: ele só mostra a lista do que sumiria, com "
				+ "contagem.", corpo);

			var preparar = new Button
			{
				Text = "Preparar limpeza total do servidor...",
				Alignment = HorizontalAlignment.Left,
				TooltipText = "passo 1 de 2: pede ao servidor o inventario do que existe hoje. "
							+ "Nada e apagado por este botao",
			};
			preparar.AddThemeColorOverride("font_color", Tema.Perigo);
			preparar.Pressed += () => cli.SendVerbo("admin_limpar");
			corpo.AddChild(preparar);
			return;
		}

		// ---------------------------------------------------------- a previa chegou
		corpo.AddChild(Titulo("ISTO VAI SUMIR PARA SEMPRE:", Tema.Perigo));

		// UMA LINHA POR SISTEMA, com o numero na frente. E o que o dono pediu em voz alta: "quem
		// confirma tem que saber o tamanho do que esta fazendo" -- e tamanho e numero, nao adjetivo.
		foreach (string l in p.Linhas)
		{
			var item = new Label { Text = "   " + l, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			item.AddThemeColorOverride("font_color", Tema.Texto);
			item.AddThemeFontSizeOverride("font_size", 13);
			corpo.AddChild(item);
		}

		Nota($"Para confirmar, digite o código abaixo. Ele vale por {p.Segundos} segundos e não "
			+ "serve pra mais nada depois disso.", corpo);

		var codigo = new Label
		{
			Text = p.Codigo,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		codigo.AddThemeColorOverride("font_color", Tema.Perigo);
		codigo.AddThemeFontSizeOverride("font_size", 28);
		corpo.AddChild(codigo);

		var campo = new LineEdit
		{
			PlaceholderText = "digite o codigo acima",
			Text = _codigoDaLimpeza,
			MaxLength = 16,
			Alignment = HorizontalAlignment.Center,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		var confirmar = new Button
		{
			Text = "APAGAR O SERVIDOR",
			// COMECA DESLIGADO e so acende com o codigo certo digitado. Nao e a guarda de verdade
			// (o servidor confere de novo, e e la que a decisao vale) -- e o que impede o clique
			// reflexo de quem nem leu a lista.
			Disabled = !string.Equals(_codigoDaLimpeza.Trim(), p.Codigo, StringComparison.OrdinalIgnoreCase),
			TooltipText = "passo 2 de 2: derruba todo mundo SEM salvar e apaga a pasta de saves",
		};
		confirmar.AddThemeColorOverride("font_color", Tema.Perigo);

		// O BOTAO SO LIGA/DESLIGA, e a pagina NAO se remonta a cada tecla: remontar recriaria a
		// `LineEdit` (e o foco, e o cursor) no meio da digitacao. Por isso o `TextChanged` mexe no
		// `Disabled` do botao na mao em vez de chamar `Redesenhar`.
		campo.TextChanged += t =>
		{
			_codigoDaLimpeza = t;
			confirmar.Disabled = !string.Equals(t.Trim(), p.Codigo, StringComparison.OrdinalIgnoreCase);
		};

		void Apagar()
		{
			if (confirmar.Disabled) return;
			cli.SendVerbo("admin_limpar_ja", _codigoDaLimpeza.Trim());
			_codigoDaLimpeza = "";
		}
		campo.TextSubmitted += _ => Apagar();
		confirmar.Pressed += Apagar;

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 6);
		linha.AddChild(campo);
		linha.AddChild(confirmar);
		corpo.AddChild(linha);

		var desistir = new Button { Text = "cancelar", Alignment = HorizontalAlignment.Left };
		// CANCELAR E PEDIR A PREVIA DE NOVO? Nao: e so esquecer o codigo daqui. O do servidor vence
		// sozinho em um minuto, e um verb novo pra "cancelar" seria um caminho a mais pra manter --
		// com o unico efeito de encurtar um prazo que ja e curto.
		desistir.Pressed += () => { _codigoDaLimpeza = ""; cli.EsquecerLimpeza(); };
		corpo.AddChild(desistir);
	}

	/// <summary>
	/// O CARTAO DA ZONA DE PERIGO: a peca <see cref="Cartao"/> com a borda e o titulo em
	/// <see cref="Tema.Perigo"/>. A peca comum so sabe a borda laranja de destaque -- e laranja e a
	/// cor do que se olha PRIMEIRO, o oposto do que este cartao pede. O metadado `perigo` e o que a
	/// bancada le pra saber que o ultimo cartao da aba e este.
	///
	/// Mora aqui como helper privado por regra da frente (MenuJogo.Pecas.cs e compartilhado);
	/// deveria subir pra la como um `Cartao(..., cor:)`.
	/// </summary>
	private VBoxContainer CartaoDaZonaDePerigo(string titulo)
	{
		var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		card.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.Perigo, 10));
		card.SetMeta("cartao", "secao");
		card.SetMeta("titulo", titulo);
		card.SetMeta("perigo", true);

		var corpo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		corpo.AddThemeConstantOverride("separation", 4);
		Label t = Tema.Rotulo(titulo);
		t.AddThemeColorOverride("font_color", Tema.Perigo);
		corpo.AddChild(t);
		card.AddChild(corpo);
		_conteudo.AddChild(card);
		return corpo;
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): tudo que ESTA aba
	/// desenha e que a assinatura basica (em MenuJogo.cs) nao cobre entra aqui, nos mesmos
	/// arredondamentos em que e desenhado. Vazio = a basica basta: ela ja leva o alvo (que acende o
	/// cartao e os botoes), a forma atual, o clima, o codigo da limpeza e as contas.
	/// </summary>
	private string ExtraDaAssinaturaDeAdmin(SheetState f) => "";
}
