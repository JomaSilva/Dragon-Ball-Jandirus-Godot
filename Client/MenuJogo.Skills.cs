using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// ============================ A ABA DE SKILLS DO MENU P ============================
/// O PEDIDO DO DONO, literal: *"na parte das skills onde vc escolhe a tree e compra a skills ta mt
/// ruim, mt simples e pouco intuitivo, deveria ser mais bonito e entendivel"*. Ele jogou anos no
/// BYOND, e "entendivel" pra ele e "parecido com o que eu conhecia". O molde e a `SkillTreeWindow`
/// do original (`skin.dmf:2262-2539`, `HtmlUI.dm:768-844`): LINHAS POR TIER, cada linha uma GRADE
/// de cards clicaveis, "Milestones: N" fixo na janela, e o clique na skill abrindo a pergunta
/// "custa N, voce tem M, aprender?".
///
/// O QUE ESTA AQUI, e por que:
///
///   * NIVEL 1 -- as arvores, em CARDS (nome, tier atual de maximo, X/Y suas, quantas da pra
///     comprar agora, e a DESCRICAO VISIVEL -- nao em tooltip, porque ninguem passa o mouse antes
///     de clicar). As arvores que o progresso ainda vai abrir aparecem APAGADAS com a condicao
///     escrita: uma arvore invisivel e uma arvore que o jogador nunca vai perseguir.
///   * NIVEL 2 -- a arvore, em LINHAS POR TIER com uma grade de cards por linha. A linha do tier
///     existe mesmo trancada (apagada, com "invista N marcos") e mesmo com tudo comprado -- era o
///     defeito G: o rotulo "TIER 1" sumia depois da ultima compra e a arvore parecia comecar no 2.
///     A gaveta "N nao estao a venda" MORREU: a skill trancada fica no LUGAR dela, apagada, com o
///     motivo do veredito por extenso.
///   * A FICHA -- um painel NO TEMA por cima da aba (o molde do `TelaDeConstrucao.Confirmar`), e
///     nunca um `AcceptDialog`: aquele vinha no cinza de fabrica do Godot no meio de uma interface
///     inteira pintada pela paleta da casa.
///   * MARCOS -- o saldo e o dado mais importante da aba e era o menos visivel (um rotulo de 11 px).
///     Agora e uma faixa FIXA, fora da rolagem, com o numero grande.
///   * OS VERBS DE APRENDIZADO (mestre e aluno, fusao, ensino, mente, inventar tecnicas) tem lugar:
///     uma secao no fim do nivel 1. Eles nao tinham aba nenhuma -- so apareciam pela busca.
///   * A BUSCA ACHA SKILL pelo nome e leva ate ela (abre a arvore certa e a ficha).
///
/// ============================ A ORDEM VERTICAL DOS TIERS ============================
/// O BYOND punha o tier ALTO EM CIMA (`HtmlUI.dm:837`: `for(var/t in list(6,5,4,3,2,1,0))`) -- uma
/// escada, com a ponta no topo. Aqui o tier BAIXO fica em cima, e a escolha e deliberada: isto e
/// uma pagina que ROLA, e a primeira tela tem que ser acionavel. Com o alto em cima, quem abre a
/// arvore ve duas linhas apagadas de coisa que nao pode comprar e o balcao de verdade fica abaixo
/// da dobra. A escada do original so funcionava porque a grade CABIA numa janela fixa. A metafora
/// nao se perde: as linhas de tier continuam sendo degraus, so que lidos na ordem em que se sobe.
///
/// ============================ LIGACOES DE PRE-REQUISITO ============================
/// 62 das 317 folhas tem pre-requisito, quase sempre dentro da mesma arvore. Linhas desenhadas
/// entre cards numa grade que QUEBRA LINHA viram macarrao no primeiro tier com onze skills (e o
/// Body tem onze no tier 1). Decisao: nenhuma linha. O card trancado diz "depois de X" por
/// extenso, a ficha nomeia os pre-requisitos com o tique de quais ja sao seus, e passar o mouse
/// num card pendura uma ETIQUETA "requisito" em quem vem antes -- a ligacao aparece quando alguem
/// pergunta por ela, e some quando nao.
///
/// A etiqueta substituiu uma borda branca no pre-requisito. A borda era da MESMA natureza que o
/// realce do card apontado -- que nem existia (ver CartaoRico) -- e o dono leu o vizinho aceso
/// como o card sob o mouse. A hierarquia agora: o card SOB O MOUSE e o mais forte da tela (chapa
/// e borda de hover do tema, 2 px, brilho por fora); o pre-requisito ganha uma pilula branca com a
/// PALAVRA -- outra natureza, e ninguem precisa de comentario pra saber qual dos dois esta
/// apontando. O inverso nao existe de proposito: apontar um pre-requisito nao marca quem depende
/// dele -- seria a mesma confusao ao contrario, varios cards marcados sem ninguem ter perguntado.
///
/// ============================ O QUE CONTINUA SENDO DIVIDA ============================
/// As descricoes vem em INGLES do catalogo (317 textos do DM). Traduzir e trabalho de outra rodada;
/// aqui elas sao mostradas como estao, porque esconder e pior que mostrar em ingles. E o NIVEL da
/// skill (`skill.level` do DM) nao existe no pacote: o card nao inventa um.
/// ====================================================================================
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// QUE ARVORE ESTA ABERTA no balcao de aprendizado. Vazio = a lista de arvores.
	///
	/// E o `mob/var/CurrentTree` do original (SkillTreesWindow.dm:53), com a mesma vida: nasce
	/// nulo, o clique num card de arvore o preenche, e o botao de voltar o zera de novo
	/// (`backbutton()`, SkillTreesWindow.dm:114).
	/// </summary>
	private string _arvoreAberta = "";

	/// <summary>
	/// O CATALOGO, lido do mesmo arquivo que o servidor le.
	///
	/// O cliente precisa dele pra MOSTRAR (nome, custo, o que falta) e pra nao oferecer o que
	/// vai ser recusado. Quem DECIDE continua sendo o servidor -- e a mesma funcao do Core
	/// roda nos dois lados, entao nao ha duas regras pra divergir.
	/// </summary>
	private static SkillCatalog? _catalogo;
	private readonly SkillBook _livro = new();

	/// <summary>A largura de um card de skill. Seis cabem numa linha da pagina de 760 px, com folga pra rolagem.</summary>
	private const int LarguraDoCartao = 104;

	/// <summary>O lado do icone do card. 40 e o dobro do sprite de 32 encolhido por 0,8: le-se sem virar mancha.</summary>
	private const int LadoDoIcone = 40;

	/// <summary>O mesmo catalogo, pra quem mais precisar dele no cliente (o robo de teste).</summary>
	public static SkillCatalog? CatalogoPublico() => Catalogo();

	/// <summary>SO PRA BANCADA: quanto custou ler o `skills.json` neste processo, em ms (0 = ainda nao leu).</summary>
	public static double MsDoCatalogoDeTeste { get; private set; }

	/// <summary>SO PRA BANCADA: quanto custou ler o `niveis.json` neste processo, em ms, e quantas regras entraram.</summary>
	public static double MsDoNiveisDeTeste { get; private set; }
	public static int NiveisNoClienteDeTeste { get; private set; }

	private static SkillCatalog? Catalogo()
	{
		if (_catalogo != null) return _catalogo;
		const string a = "res://Assets/Data/skills.json", b = "res://Assets/Data/skilltrees.json";
		if (!Godot.FileAccess.FileExists(a) || !Godot.FileAccess.FileExists(b)) return null;
		ulong t0 = Time.GetTicksUsec();
		_catalogo = SkillCatalog.Parse(Godot.FileAccess.GetFileAsString(a), Godot.FileAccess.GetFileAsString(b));
		MsDoCatalogoDeTeste = (Time.GetTicksUsec() - t0) / 1000.0;
		// O REGISTRO DE NIVEIS VEM JUNTO, do mesmo lugar e na mesma chamada: sem ele o veredito das
		// skills acesas por DEGRAU sai "sem acendedor" -- ver `CarregarNiveisNoCliente`.
		CarregarNiveisNoCliente();
		GD.Print($"[skills] catalogo no cliente: {_catalogo.Total} entradas em {MsDoCatalogoDeTeste:0.0} ms"
				 + $" | niveis.json: {NiveisNoClienteDeTeste} regras em {MsDoNiveisDeTeste:0.0} ms");
		return _catalogo;
	}

	/// <summary>
	/// O `niveis.json` NO CLIENTE -- o mesmo arquivo, lido pelo MESMO `RegrasDoDisco.Carregar` que
	/// o servidor chama no boot (`GameServer.CarregarNiveis`).
	///
	/// ============================ POR QUE O CLIENTE PRECISA DISTO ============================
	/// A DECISAO "acesa ou nao" chega pronta no pacote `S2C.Skills` (`Acesas`) e o cliente nao
	/// recalcula nada. Mas o TEXTO do veredito de uma skill que so um DEGRAU de outra acende
	/// (Advanced Ki Awareness <- Basic Ki Awareness no nivel 100; sao 32 assim, as Advanced_* e
	/// Perfect_* da Mente e das maestrias de Ki) e montado AQUI, pelo `SkillBook.AcendedorDe`, que le
	/// o registro de niveis (`RegrasDeNivel.DestravadaPor`). Com o registro vazio o veredito caia em
	/// `Desligada` e o card dizia "sem acendedor" -- a resposta certa com o motivo errado, e o jogador
	/// nao tinha como saber o que treinar. O servidor tinha o registro e o cliente nao; o `--host`
	/// escondia isso porque ali os dois moram no mesmo processo e o registro e estatico.
	///
	/// O QUE O CLIENTE NAO TEM, E NAO INVENTA: o NIVEL ATUAL das skills nao viaja no pacote (decisao
	/// de 2026-09-01). Ele sabe QUAL degrau acende ("chega ao nivel 100") e nao QUANTO falta -- e
	/// mostra so o que sabe. O contexto do servidor (`ctx.DestravadasPorDegrau = pl.Niveis.Destravadas()`)
	/// precisa desses niveis, e por isso nao se monta aqui: o cliente recebe o RESULTADO dele.
	///
	/// NO `--host` O SERVIDOR DESTE PROCESSO JA CARREGOU (`RegrasDoDisco.Carregado`): o registro e um
	/// so, e ler de novo seria trabalho repetido. Quem disca (`--connect`) paga a leitura aqui, na
	/// mesma chamada que ja le o `skills.json` -- medido pela `--diagskills` (F4c), dentro da
	/// entrada no mundo e nao num quadro proprio.
	/// ==========================================================================================
	/// </summary>
	private static void CarregarNiveisNoCliente()
	{
		if (RegrasDoDisco.Carregado)
		{
			NiveisNoClienteDeTeste = RegrasDeNivel.Total;
			GD.Print("[skills] niveis.json ja carregado neste processo (o servidor mora aqui)");
			return;
		}
		const string cj = "res://Assets/Data/niveis.json";
		if (!Godot.FileAccess.FileExists(cj))
		{
			GD.PushWarning("[skills] sem niveis.json -- rode o AssetPipeline (comando 'effector'); "
						   + "as skills acesas por degrau vao sair como 'sem acendedor'");
			return;
		}
		ulong t0 = Time.GetTicksUsec();
		NiveisNoClienteDeTeste = RegrasDoDisco.Carregar(Godot.FileAccess.GetFileAsString(cj));
		MsDoNiveisDeTeste = (Time.GetTicksUsec() - t0) / 1000.0;
	}

	/// <summary>Copia pro livro local o que o servidor mandou. O servidor e a verdade.</summary>
	private void SincronizarLivro()
	{
		if (GameClient.Instance is not { } cli) return;
		_livro.Carregar(cli.SkillsAprendidas);
		_livro.MarcosTotais = cli.MarcosTotais;
		_livro.MarcosLivres = cli.MarcosLivres;
		// o estado das arvores (tier, abertas, acesas) vem do servidor -- sem isto `Destravadas`
		// era sempre vazio e o `PodeAprender` daqui discordava do de la
		_livro.CarregarEstado(cli.SkillsDestravadas, cli.SkillsArvores);
	}

	// =====================================================================
	// A FAIXA DE MARCOS -- fixa, fora da rolagem
	// =====================================================================
	private PanelContainer _faixaDeMarcos = null!;
	private Label _marcosGrande = null!;
	private Label _marcosLegenda = null!;

	/// <summary>
	/// A FAIXA DE MARCOS: o numero grande, entre a barra de abas e a rolagem.
	///
	/// FORA DA ROLAGEM de proposito -- e o "Milestones: N" fixo da janela do BYOND
	/// (`SkillTreesWindow.dm:79`). Dentro da pagina ele rolaria pra fora da tela no primeiro tier, e o
	/// saldo e justamente o numero que se consulta antes de cada clique.
	/// </summary>
	private void MontarFaixaDeMarcos(VBoxContainer coluna)
	{
		_faixaDeMarcos = new PanelContainer { Visible = false, Name = "FaixaDeMarcos" };
		_faixaDeMarcos.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.Borda, 8));
		coluna.AddChild(_faixaDeMarcos);

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 12);
		_faixaDeMarcos.AddChild(linha);

		Label rotulo = Tema.Rotulo("Marcos");
		rotulo.VerticalAlignment = VerticalAlignment.Center;
		linha.AddChild(rotulo);

		_marcosGrande = new Label { Text = "0", VerticalAlignment = VerticalAlignment.Center };
		_marcosGrande.AddThemeFontSizeOverride("font_size", 26);
		_marcosGrande.AddThemeColorOverride("font_color", Tema.Destaque);
		linha.AddChild(_marcosGrande);

		_marcosLegenda = new Label { VerticalAlignment = VerticalAlignment.Center, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_marcosLegenda.AddThemeFontSizeOverride("font_size", 12);
		_marcosLegenda.AddThemeColorOverride("font_color", Tema.TextoFraco);
		linha.AddChild(_marcosLegenda);
	}

	/// <summary>
	/// Escreve o saldo na faixa. Chamado na montagem da pagina de aprendizado -- e os marcos entram
	/// na assinatura da pagina, entao toda mudanca de saldo passa por aqui.
	/// </summary>
	private void AtualizarFaixaDeMarcos()
	{
		_marcosGrande.Text = _livro.MarcosLivres.ToString(System.Globalization.CultureInfo.InvariantCulture);
		_marcosLegenda.Text = $"livre{(_livro.MarcosLivres == 1 ? "" : "s")} pra gastar  ·  {_livro.MarcosTotais} ganho{(_livro.MarcosTotais == 1 ? "" : "s")} na vida"
						   + "  ·  marco é a moeda das habilidades";
	}

	// =====================================================================
	// A ASSINATURA DAS DUAS ABAS -- por PECAS NOMEADAS
	// =====================================================================
	/// <summary>
	/// SO PRA BANCADA: o nome de uma peca da assinatura que deve ser DEIXADA DE FORA. Reproduz o
	/// defeito de origem desta aba (a gaveta que nao abria porque a flag dela nao assinava a pagina) sob
	/// medida: com a peca "marcos" fora, um marco que chega do servidor NAO remonta a pagina, e a faixa
	/// fica com o numero velho. Vazio no jogo.
	/// </summary>
	internal static string PedacoIgnoradoDeTeste = "";

	/// <summary>
	/// TUDO QUE MUDA O DESENHO DAS ABAS Learning E Skills, cada coisa com nome.
	///
	/// A regra da casa e "toda variavel que muda a tela entra na assinatura". Nomear cada peca e o que
	/// deixa a bancada provar a regra peca a peca -- mexer numa variavel e cobrar que a pagina remontou --
	/// em vez de afirmar que ela vale.
	/// </summary>
	private List<(string Nome, string Valor)> PecasDaAssinatura(string aba)
	{
		GameClient? c = GameClient.Instance;
		var pecas = new List<(string, string)>
		{
			("busca", _busca.Text.Trim()),
			("arvore", _arvoreAberta),
			// o CONJUNTO das aprendidas, e nao a contagem: esquecer uma e comprar outra entre dois
			// pacotes deixaria a contagem igual com dois cards errados na tela
			("aprendidas", AssinaturaDasAprendidas(c)),
			("marcos", $"{c?.MarcosLivres}/{c?.MarcosTotais}"),
			// o bit de vilao decide se o Planet Destroy e "so pra vilao" ou "10 marcos"
			("vilao", c?.SouVilao == true ? "v" : "-"),
			// o tier de vitrine, o investido e as acesas/apagadas de cada arvore, e as destravadas
			("arvores", _livro.AssinaturaDasArvores()),
			// a raca chega na ficha lenta e e ela que diz quais arvores existem; a classe idem
			("ficha", $"{_atributos.Raca}|{c?.Sheet.Class}"),
		};
		if (aba == "Learning")
			pecas.Add(("verbos", string.Join(',', Verbos.Da(Verbos.Aprendizado).Select(v => v.Nome + (v.PodeAgora ? '+' : '-')))));
		return pecas;
	}

	private string AssinaturaDeSkills(string aba) =>
		string.Join('|', PecasDaAssinatura(aba).Where(p => p.Nome != PedacoIgnoradoDeTeste).Select(p => p.Valor));

	/// <summary>O conjunto das aprendidas numa string curta, independente da ordem em que chegaram.</summary>
	private static string AssinaturaDasAprendidas(GameClient? c)
	{
		if (c == null) return "-";
		int soma = 0;
		foreach (string p in c.SkillsAprendidas) soma = unchecked(soma + StringComparer.OrdinalIgnoreCase.GetHashCode(p));
		return $"{c.SkillsAprendidas.Count}:{soma:x8}";
	}

	/// <summary>SO PRA BANCADA: as pecas da assinatura da aba, com nome, pra cobrar uma de cada vez.</summary>
	public IReadOnlyList<(string Nome, string Valor)> PecasDaAssinaturaDeTeste(string aba) => PecasDaAssinatura(aba);

	/// <summary>SO PRA BANCADA: quantas vezes o conteudo de uma aba foi remontado ate agora.</summary>
	public int RemontagensDeTeste => _remontagens;

	// =====================================================================
	// O BALCAO -- dois niveis
	// =====================================================================
	/// <summary>
	/// O BALCAO, EM DOIS NIVEIS: escolhe a ARVORE, depois as skills dela.
	///
	/// E o caminho de duas janelas do original. A `SkillTreeWindow` lista as arvores em cards; o
	/// clique num card guarda a arvore e abre a `SkillsListWindow` com as skills DAQUELA arvore
	/// (`CurrentTree = A; SkillWindowOpen()` -- SkillTreesWindow.dm:18-23 e HtmlUI.dm:988-993). O
	/// caminho de volta e o `backbutton()`, que zera o CurrentTree e reabre a lista de arvores
	/// (SkillTreesWindow.dm:111-122).
	///
	/// POR QUE NAO UMA LISTA SO: sao 317 folhas em 47 arvores. Ninguem procura "uma skill", procura
	/// "o que a minha arvore de corpo tem" -- a arvore E a pergunta. Achatar tudo numa lista joga
	/// fora exatamente a informacao que organiza a escolha.
	/// </summary>
	private void Aprendizado()
	{
		// a pagina foi remontada: uma ficha aberta sobre ela falaria de um estado que ja nao existe
		FecharFicha();
		_etiquetas.Clear();

		SkillCatalog? cat = Catalogo();
		if (cat == null) { Aviso("catálogo de skills não encontrado (rode o AssetPipeline: comando 'skills')."); return; }
		if (GameClient.Instance is not { } cli) return;

		AtualizarFaixaDeMarcos();

		string raca = _atributos.Raca ?? "";
		string classe = cli.Sheet.Class ?? "";
		List<Skill> arvores = ArvoresDoPersonagem(cat, raca, classe);

		// A ARVORE ABERTA PODE TER SUMIDO no meio do caminho (a ficha lenta trouxe outra raca, um
		// cargo caiu). Cair de volta na lista e melhor que uma pagina vazia sem explicacao -- e o
		// mesmo cuidado que o `Redesenhar()` toma com a aba Scan quando o scouter sai.
		Skill? aberta = _arvoreAberta.Length > 0 ? cat.Get(_arvoreAberta) : null;
		if (aberta != null && !arvores.Contains(aberta)) aberta = null;

		if (aberta == null) { _arvoreAberta = ""; ListaDeArvores(cat, arvores, raca, classe); }
		else SkillsDaArvore(cat, aberta, raca, classe);
	}

	/// <summary>Volta pro primeiro nivel. O `backbutton()` do original (SkillTreesWindow.dm:111). Nao redesenha: quem chama decide.</summary>
	private void FecharArvore()
	{
		FecharFicha();
		_arvoreAberta = "";
	}

	/// <summary>
	/// AS ARVORES QUE ESTE PERSONAGEM TEM: as da raca e da classe (`generatetrees`) mais as que o
	/// PROGRESSO abriu (`enabletree`). E a mesma uniao de <see cref="SkillBook.Ofertas"/>.
	///
	/// POR QUE NAO CHAMO Ofertas() DIRETO, ja que ela existe: ela DEDUPLICA entre arvores e devolve
	/// a lista achatada. Uma navegacao em dois niveis precisa justamente do que ela descarta -- de
	/// QUE arvore cada skill veio -- e uma skill pendurada em duas arvores tem que aparecer nas
	/// duas, senao ela some da segunda sem motivo visivel. As regras de recusa, essas sim, saem
	/// inteiras do Core (<see cref="SkillBook.Avaliar"/>): nao ha uma segunda copia aqui.
	/// </summary>
	private List<Skill> ArvoresDoPersonagem(SkillCatalog cat, string raca, string classe)
	{
		List<Skill> l = cat.ArvoresDe(raca, classe);
		foreach (string p in _livro.Destravadas)
			if (cat.Get(p) is { } a && !l.Contains(a)) l.Add(a);
		return l;
	}

	/// <summary>
	/// O VEREDITO DO CORE, com um unico ajuste -- e uma porta so pras duas telas do balcao, pra que
	/// a contagem da lista de arvores nunca discorde do card que ela promete.
	///
	/// O AJUSTE: a classe nao chega mais ao cliente, e nao deve mesmo chegar -- o sigilo zera o
	/// campo em TODA ficha, com scouter ou sem (GameServer.Sigilo.cs:105). So que a classe e um
	/// dos gates de skill do DM (`compatible_classes`, skill.dm:13), entao daqui pra frente o
	/// cliente passa a errar pra MENOS: uma skill que a classe permite volta como RacaOuClasse.
	///
	/// Errar pra menos e o pior dos dois erros. Um card apagado por engano esconde conteudo PRA
	/// SEMPRE, e o jogador nao tem como desconfiar que a recusa era do cliente; errar pra mais
	/// custa uma frase de recusa vinda do servidor, que e onde a decisao sempre morou. Entao:
	/// quando a skill pede CLASSE e eu nao sei a minha, eu nao decido -- deixo passar e quem sabe
	/// responde. O que da pra conferir sem a classe (pre-requisito e saldo) continua conferido.
	/// </summary>
	private Veredito Avaliar(SkillCatalog cat, Skill s, string raca, string classe)
	{
		// O `vilao:` era `false` cravado, e por isso a unica skill de vilao do catalogo (Planet
		// Destroy) aparecia trancada ate pra quem o admin tinha designado vilao. O bit chega junto
		// das skills (`S2C.Skills`); sem cliente na mao a resposta e "nao", que e o erro pro lado seguro.
		Veredito v = _livro.Avaliar(cat, s.Path, raca, classe, vilao: GameClient.Instance?.SouVilao ?? false);
		if (v.Motivo == Recusa.RacaOuClasse && classe.Length == 0 && s.Classes.Length > 0)
		{
			v.Motivo = Recusa.Pode;
			if (!SkillCatalog.PreReqsOk(s, (ICollection<string>)_livro.Aprendidas))
			{
				v.PreReqsFaltando = [.. s.PreReqs.Where(p => !_livro.Sabe(p))];
				v.Motivo = Recusa.FaltaPreRequisito;
			}
			else if (_livro.MarcosLivres < v.Custo)
			{
				v.FaltamMarcos = v.Custo - _livro.MarcosLivres;
				v.Motivo = Recusa.SemMarcos;
			}
		}
		return v;
	}

	/// <summary>As folhas com nome de uma arvore, NA ORDEM DO CATALOGO. E a ordem que todo desenho daqui usa.</summary>
	private static IEnumerable<Skill> Folhas(SkillCatalog cat, Skill arv)
	{
		foreach (string p in arv.Galhos)
			if (cat.Get(p) is { Arvore: false } s && s.Nome.Length > 0) yield return s;
	}

	/// <summary>O tier mais alto que a arvore TEM. `TierMax` do DM e o teto declarado (o Body diz 10 e vai ate o 3).</summary>
	private static int TierMaisAlto(SkillCatalog cat, Skill arv)
	{
		int t = 0;
		foreach (Skill s in Folhas(cat, arv)) if (s.Tier > t) t = s.Tier;
		return t;
	}

	/// <summary>O estado desta arvore como o servidor calculou -- ou o de nascenca, se ainda nao chegou nada.</summary>
	private EstadoDeArvore EstadoDa(SkillCatalog cat, Skill arv) =>
		_livro.Arvore(arv.Path) ?? new EstadoDeArvore { Path = arv.Path, Tier = arv.TierInicial, Investido = _livro.Investido(cat, arv) };

	// =====================================================================
	// NIVEL 1 -- AS ARVORES, EM CARDS
	// =====================================================================
	/// <summary>
	/// NIVEL 1: as arvores, com quanto de cada uma ja e seu e quanto da pra comprar agora.
	///
	/// O CONTADOR "pra comprar agora" e o que faz a lista valer: sem ele, escolher arvore vira
	/// tentativa e erro em nove cards. Com ele, a aba responde de relance a unica pergunta que
	/// alguem com marcos na mao tem -- onde e que eu gasto isto.
	///
	/// DEPOIS DELAS, AS QUE AINDA VAO ABRIR: cada arvore sua declara (`growbranches()`) que outras
	/// abre e sob que condicao. Elas entram apagadas, com a condicao escrita em portugues. So o
	/// PROXIMO elo da cadeia aparece (Wrestling so aparece depois de Bodybuilding abrir), porque
	/// e o proximo elo que se persegue.
	///
	/// E NO FIM, OS VERBS DE APRENDIZADO -- mestre e aluno, fusao, ensino, mente, inventar tecnicas.
	/// Eles moravam em categoria nenhuma: o `Montar` mandava a aba Learning pra ca e a lista de verbs
	/// so rodava pra Other e Admin, entao dezenove acoes so existiam pela busca.
	/// </summary>
	private void ListaDeArvores(SkillCatalog cat, List<Skill> arvores, string raca, string classe)
	{
		Secao("Suas árvores");

		var grade = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		grade.AddThemeConstantOverride("h_separation", 8);
		grade.AddThemeConstantOverride("v_separation", 8);
		_conteudo.AddChild(grade);

		bool alguma = false;
		foreach (Skill arv in arvores)
		{
			int total = 0, sabidas = 0, agora = 0;
			foreach (Skill s in Folhas(cat, arv))
			{
				total++;
				if (_livro.Sabe(s.Path)) { sabidas++; continue; }
				if (Avaliar(cat, s, raca, classe).Motivo == Recusa.Pode) agora++;
			}

			// "Tree Mastery" nasce sem galho nenhum no DM e continua assim. Card vazio so ocupa
			// espaco e faz o jogador clicar duas vezes pra descobrir que nao tem nada.
			if (total == 0) continue;
			alguma = true;

			EstadoDeArvore e = EstadoDa(cat, arv);
			// O TIER MOSTRADO E O MENOR ENTRE A VITRINE E O QUE A ARVORE TEM: o Mind nasce com
			// `allowedtier = 10` no DM (tudo aberto) e so tem skill ate o tier 5 -- "tier 10 de 5" e
			// verdade no dado e mentira na leitura.
			int maisAlto = TierMaisAlto(cat, arv);
			string linha2 = $"tier {Math.Min(e.Tier, maisAlto)} de {maisAlto}   ·   {sabidas}/{total} suas"
						  + (agora > 0 ? $"   ·   {agora} pra comprar agora" : "");
			string caminho = arv.Path;
			grade.AddChild(CartaoDeArvore(arv.Nome, linha2, arv.Desc, agora > 0, apagado: false,
				() => { _arvoreAberta = caminho; Redesenhar(); }));
		}

		foreach ((Skill alvo, string condicao, string origem) in ArvoresAAbrir(cat, arvores))
		{
			int n = Folhas(cat, alvo).Count();
			grade.AddChild(CartaoDeArvore(alvo.Nome,
				$"abre com: {NomesLegiveis.Condicao(condicao)}   ·   regra de {origem}   ·   {n} habilidade{(n == 1 ? "" : "s")}",
				alvo.Desc, destaque: false, apagado: true, aoClicar: null));
		}

		if (!alguma)
			Aviso("Nenhuma árvore ainda. Elas vêm da raça, da classe e do que você treina.");

		VerbosDeAprendizado();
	}

	/// <summary>
	/// AS ARVORES QUE AS SUAS AINDA VAO ABRIR: cada regra `enabletree(X)` de uma arvore sua cujo X
	/// ainda nao e seu. A condicao vai junto, crua, pra quem desenha traduzir.
	/// </summary>
	private static IEnumerable<(Skill Alvo, string Condicao, string Origem)> ArvoresAAbrir(SkillCatalog cat, List<Skill> minhas)
	{
		var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Skill arv in minhas)
			foreach (RegraDeArvore r in arv.RegrasDeGalho)
			{
				if (r.Tipo != TipoDeRegra.AbreArvore) continue;
				if (cat.Get(r.Alvo) is not { Arvore: true } alvo) continue;
				if (minhas.Contains(alvo) || !vistas.Add(alvo.Path)) continue;
				yield return (alvo, r.Condicao, arv.Nome);
			}
	}

	/// <summary>
	/// UM CARD DE ARVORE: nome, a linha de contadores, e a descricao VISIVEL.
	///
	/// A descricao saiu do tooltip: ninguem passa o mouse antes de clicar, e em tela de toque o
	/// tooltip nem existe. A linha de contadores e o que a lista antiga escrevia no botao, so que
	/// agora ela tem o tier (o "tier 1 de 3" e o que diz quanto da arvore ainda falta abrir).
	/// </summary>
	private Control CartaoDeArvore(string nome, string linha2, string desc, bool destaque, bool apagado, Action? aoClicar)
	{
		var corpo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		corpo.AddThemeConstantOverride("separation", 2);

		var titulo = new Label { Text = nome };
		titulo.AddThemeFontSizeOverride("font_size", 15);
		titulo.AddThemeColorOverride("font_color", apagado ? Tema.TextoFraco : destaque ? Tema.Destaque : Tema.Texto);
		corpo.AddChild(titulo);

		var contadores = new Label { Text = linha2, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		contadores.AddThemeFontSizeOverride("font_size", 11);
		contadores.AddThemeColorOverride("font_color", apagado ? Tema.TextoFraco : destaque ? Tema.Bom : Tema.Texto);
		corpo.AddChild(contadores);

		if (desc.Length > 0)
		{
			var d = new Label { Text = desc, AutowrapMode = TextServer.AutowrapMode.WordSmart };
			d.AddThemeFontSizeOverride("font_size", 11);
			d.AddThemeColorOverride("font_color", Tema.TextoFraco);
			corpo.AddChild(d);
		}

		PanelContainer card = CartaoRico(corpo, apagado ? Tema.Caixa(Tema.PainelApagado, Tema.BordaApagada, 8) : Tema.Caixa(Tema.PainelClaro, Tema.Borda, 8), aoClicar);
		// A IDENTIDADE VAI EM METADADO, e nao no `Name`: o Godot renomeia irmaos homonimos pra
		// "@PanelContainer@736" (a CLASSE, nao o nome dado), entao so o primeiro card guardaria o nome.
		card.SetMeta("cartao", "arvore");
		card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return card;
	}

	/// <summary>
	/// UM CARD RICO: um painel no tema com o conteudo por cima de um botao invisivel.
	///
	/// ============================ POR QUE NAO UM `Button` COM TEXTO ============================
	/// O texto de um botao e UMA cor e UM tamanho. Um card tem tres linhas de pesos diferentes (o
	/// nome, o contador colorido, a descricao apagada) -- e o `PanelContainer` empilha os filhos no
	/// mesmo retangulo, entao o botao (primeiro filho, embaixo) recebe o clique e o mouse, e o
	/// conteudo (segundo filho, em cima) desenha o texto e IGNORA o mouse.
	///
	/// ============================ O HOVER E DO PAINEL, NAO DO BOTAO ============================
	/// O botao e `Flat`, e um botao Flat NAO DESENHA STYLEBOX DE ESTADO NENHUM -- nem normal, nem
	/// hover, nem pressed; so o de foco, que aqui esta desligado. Foi o defeito que o dono viu: o
	/// card sob o mouse ficava MUDO, e a unica coisa que acendia na tela era a borda do
	/// pre-requisito -- ele leu o vizinho como o apontado. Tirar o Flat nao resolve: o botao mora
	/// DENTRO das margens do painel, e o hover do tema aparece como uma segunda moldura 6 px por
	/// dentro da do card (a rodada de antes da bancada `--diagskills` mediu isso em runtime,
	/// 2026-09-02: #645144 na aresta de cima do botao com Flat=false, card ainda apagado). Entao o
	/// botao so AVISA (MouseEntered/MouseExited) e quem acende e a moldura do PAINEL, trocada pela
	/// de <see cref="Tema.CaixaSobOMouse"/>: a mesma chapa e a mesma borda laranja que todo botao
	/// do jogo ganha, com 2 px e brilho por fora -- o realce mais forte da tela, sem precisar de
	/// explicacao. A moldura de repouso volta quando o mouse sai.
	/// =============================================================================================
	/// </summary>
	private static PanelContainer CartaoRico(Control conteudo, StyleBoxFlat moldura, Action? aoClicar)
	{
		var card = new PanelContainer();
		card.AddThemeStyleboxOverride("panel", moldura);

		if (aoClicar != null)
		{
			var b = new Button { Flat = true, FocusMode = Control.FocusModeEnum.None };
			b.Pressed += aoClicar;
			StyleBoxFlat sobOMouse = Tema.CaixaSobOMouse(moldura);
			b.MouseEntered += () => card.AddThemeStyleboxOverride("panel", sobOMouse);
			b.MouseExited += () => card.AddThemeStyleboxOverride("panel", moldura);
			card.AddChild(b);
		}

		IgnorarMouse(conteudo);
		card.AddChild(conteudo);
		return card;
	}

	/// <summary>O conteudo de um card nao pode engolir o clique que e do botao embaixo dele.</summary>
	private static void IgnorarMouse(Control c)
	{
		c.MouseFilter = Control.MouseFilterEnum.Ignore;
		foreach (Node n in c.GetChildren()) if (n is Control f) IgnorarMouse(f);
	}

	/// <summary>
	/// A SECAO DOS VERBS DE APRENDIZADO, no fim do nivel 1, em duas colunas.
	///
	/// Duas colunas e nao a lista de largura total das outras abas: sao dezenove botoes (mais um por
	/// skill ensinavel que voce souber), e empilhados eles empurrariam a metade util da aba pra fora
	/// da tela. A busca continua achando todos, como antes.
	/// </summary>
	private void VerbosDeAprendizado()
	{
		Secao("Ações de aprendizado");
		var lista = Verbos.Da(Verbos.Aprendizado).ToList();
		if (lista.Count == 0) { Aviso("Nenhuma acao aqui ainda."); return; }

		var grade = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		grade.AddThemeConstantOverride("h_separation", 6);
		grade.AddThemeConstantOverride("v_separation", 4);
		_conteudo.AddChild(grade);
		foreach (Verbo v in lista)
		{
			Button b = BotaoDe(v);
			b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			b.AddThemeFontSizeOverride("font_size", 13);
			grade.AddChild(b);
		}
	}

	// =====================================================================
	// NIVEL 2 -- A ARVORE, EM LINHAS POR TIER
	// =====================================================================
	/// <summary>A etiqueta "requisito" de cada card da arvore aberta, por typepath -- pra quem depende dele achar qual mostrar.</summary>
	private readonly Dictionary<string, Label> _etiquetas = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// NIVEL 2: as skills DESTA arvore, uma LINHA POR TIER, cada linha uma grade de cards.
	///
	/// A LINHA DO TIER EXISTE SEMPRE que a arvore tem skill naquele tier -- trancada (apagada, com
	/// "invista N marcos"), aberta, ou inteira comprada. Era o defeito G: a secao "Tier 1" so nascia
	/// se sobrasse skill a venda nela, e depois da ultima compra a arvore parecia comecar no tier 2.
	/// </summary>
	private void SkillsDaArvore(SkillCatalog cat, Skill arv, string raca, string classe)
	{
		// VOLTAR SEMPRE VISIVEL, no topo, antes de qualquer coisa que role pra fora da tela. ESC
		// faz o mesmo (ver _Input), mas quem entrou clicando espera sair clicando.
		var topo = new HBoxContainer();
		topo.AddThemeConstantOverride("separation", 12);
		var voltar = new Button { Text = "‹  todas as árvores" };
		voltar.Pressed += () => { FecharArvore(); Redesenhar(); };
		topo.AddChild(voltar);
		var titulo = new Label { Text = arv.Nome, VerticalAlignment = VerticalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 18);
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		topo.AddChild(titulo);
		_conteudo.AddChild(topo);

		if (arv.Desc.Length > 0) Aviso(arv.Desc);

		EstadoDeArvore e = EstadoDa(cat, arv);
		int maisAlto = TierMaisAlto(cat, arv);
		string vitrine = $"vitrine no tier {Math.Min(e.Tier, maisAlto)} de {maisAlto}   ·   {e.Investido} marco{(e.Investido == 1 ? "" : "s")} investido{(e.Investido == 1 ? "" : "s")} nesta árvore";
		if (e.ProximoInvestir > 0) vitrine += $"   ·   próximo degrau: tier {e.ProximoTier} com {e.ProximoInvestir}";
		Aviso(vitrine);

		// AS FOLHAS POR TIER, NA ORDEM DO CATALOGO. Um dicionario ordenado por tier; dentro de cada
		// tier a lista guarda a ordem em que os galhos vem no json -- a mesma arvore no mesmo estado
		// desenha igual, sempre.
		var porTier = new SortedDictionary<int, List<Skill>>();
		foreach (Skill s in Folhas(cat, arv))
		{
			if (!porTier.TryGetValue(s.Tier, out List<Skill>? l)) porTier[s.Tier] = l = [];
			l.Add(s);
		}

		foreach ((int tier, List<Skill> folhas) in porTier)
		{
			bool aberto = tier <= e.Tier;
			int suas = folhas.Count(s => _livro.Sabe(s.Path));
			LinhaDeTier(cat, e, tier, aberto, suas, folhas.Count);

			var grade = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			grade.AddThemeConstantOverride("h_separation", 6);
			grade.AddThemeConstantOverride("v_separation", 6);
			_conteudo.AddChild(grade);

			foreach (Skill s in folhas)
				grade.AddChild(CartaoDeSkill(cat, s, Avaliar(cat, s, raca, classe)));
		}
	}

	/// <summary>
	/// O CABECALHO DE UM TIER: "TIER 2  ·  3 de 8 suas" aberto, ou "TIER 2  ·  TRANCADO  ·  invista 4
	/// marcos nesta árvore" apagado. O numero vem do mesmo `FaltaInvestir` que o veredito usa.
	/// </summary>
	private void LinhaDeTier(SkillCatalog cat, EstadoDeArvore e, int tier, bool aberto, int suas, int total)
	{
		if (_conteudo.GetChildCount() > 0) _conteudo.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

		var h = new HBoxContainer();
		h.AddThemeConstantOverride("separation", 10);

		Label rotulo = Tema.Rotulo($"Tier {tier}");
		rotulo.AddThemeFontSizeOverride("font_size", 12);
		rotulo.AddThemeColorOverride("font_color", aberto ? Tema.Destaque : Tema.TextoFraco);
		h.AddChild(rotulo);

		string detalhe;
		if (aberto) detalhe = suas == total ? $"todas as {total} suas" : $"{suas} de {total} suas";
		else
		{
			int falta = SkillBook.FaltaInvestir(cat, e, tier);
			detalhe = falta > 0
				? $"TRANCADO  ·  invista {falta} marco{(falta == 1 ? "" : "s")} nesta árvore pra abrir (você investiu {e.Investido})"
				: "TRANCADO  ·  abre por outro caminho, não por marcos";
		}
		Label d = Tema.Rotulo(detalhe);
		d.Text = detalhe;   // sem o ToUpper do Rotulo: e frase, nao titulo
		d.AddThemeColorOverride("font_color", aberto ? Tema.TextoFraco : Tema.Perigo);
		h.AddChild(d);

		_conteudo.AddChild(h);
		_conteudo.AddChild(new HSeparator());
	}

	/// <summary>As tres caras de um card. Cada uma tem chapa e borda proprias, pra ler de relance.</summary>
	private enum Cara { Sua, Compravel, Trancada }

	private static StyleBoxFlat MolduraDe(Cara cara)
	{
		switch (cara)
		{
			case Cara.Sua:
			{
				StyleBoxFlat sb = Tema.Caixa(Tema.PainelClaro, Tema.Bom, 6);
				sb.SetBorderWidthAll(2);
				return sb;
			}
			case Cara.Compravel: return Tema.Caixa(Tema.PainelClaro, Tema.BordaViva, 6);
			default: return Tema.Caixa(Tema.PainelApagado, Tema.BordaApagada, 6);
		}
	}

	/// <summary>
	/// UM CARD DE SKILL: icone, nome, e o ESTADO por extenso -- sua / o preco / o motivo.
	///
	/// TRES CARAS, e nao duas. O botao antigo tinha "ligado" e "apagado", e apagado juntava "ja e
	/// sua" com "nao pode": o jogador nao distinguia o que tinha comprado do que nao podia comprar.
	/// Borda verde e SUA; borda laranja com o preco e COMPRAVEL; chapa apagada com o motivo e
	/// TRANCADA -- e o motivo diz o que fazer (comprar X antes, investir na arvore, esperar Y).
	///
	/// TRANCADA CONTINUA CLICAVEL, de proposito: abre a ficha em modo leitura. Quem planeja a arvore
	/// precisa ler o que a skill de cima faz antes de comprar as de baixo.
	/// </summary>
	private Control CartaoDeSkill(SkillCatalog cat, Skill s, Veredito v)
	{
		bool sua = v.Motivo == Recusa.JaSabe;
		Cara cara = sua ? Cara.Sua : v.Motivo == Recusa.Pode ? Cara.Compravel : Cara.Trancada;
		(string estado, Color corDoEstado) = EstadoDoCartao(cat, s, v);

		var corpo = new VBoxContainer { CustomMinimumSize = new Vector2(LarguraDoCartao - 12, 0) };
		corpo.AddThemeConstantOverride("separation", 3);

		var icone = new TextureRect
		{
			Texture = ArteDaSkill(s),
			CustomMinimumSize = new Vector2(LadoDoIcone, LadoDoIcone),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			Name = "Icone",
		};
		// APAGADO NO PIXEL, nao so na borda: o icone trancado sai a 35% -- e a bancada mede a
		// luminancia dele contra a de um card compravel na mesma foto
		if (cara == Cara.Trancada) icone.SelfModulate = new Color(1, 1, 1, 0.35f);
		corpo.AddChild(icone);

		var nome = new Label
		{
			Text = NomeCurto(s.Nome),
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Name = "Nome",
		};
		nome.AddThemeFontSizeOverride("font_size", 11);
		nome.AddThemeColorOverride("font_color", cara == Cara.Trancada ? Tema.TextoFraco : Tema.Texto);
		corpo.AddChild(nome);

		var rodape = new Label
		{
			Text = estado,
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			Name = "Estado",
		};
		rodape.AddThemeFontSizeOverride("font_size", 10);
		rodape.AddThemeColorOverride("font_color", corDoEstado);
		corpo.AddChild(rodape);

		StyleBoxFlat moldura = MolduraDe(cara);
		Skill escolhida = s;
		PanelContainer card = CartaoRico(corpo, moldura, () => AbrirFicha(escolhida));
		card.SetMeta("cartao", "skill");   // metadado, e nao `Name` -- ver `CartaoDeArvore`
		card.CustomMinimumSize = new Vector2(LarguraDoCartao, 0);
		// O TOOLTIP DIZ O QUE ELA FAZ, e nao so o que ela e: a mesma linha da aba Skills.
		string resumo = FichaDeSkill.Montar(cat, s, RegrasDeNivel.Get(s.Path)).Resumo;
		card.TooltipText = s.Nome + (s.Desc.Length > 0 ? "\n" + s.Desc : "") + (resumo.Length > 0 ? "\n\n" + resumo : "");
		card.SetMeta("path", s.Path);
		card.SetMeta("estado", estado);

		// A ETIQUETA "requisito" DESTE card, escondida: quem a mostra e o mouse em cima de uma skill
		// que depende dele. Filha do ICONE, e nao do corpo nem do painel: o PanelContainer encaixa
		// cada filho no retangulo inteiro e o VBox empilha -- uma etiqueta no corpo mudaria a altura
		// do card ao aparecer. O icone nao e container: a filha fica onde as ancoras mandam
		// (pendurada na aresta de cima, centrada) e e desenhada POR CIMA do icone. `SelfModulate`
		// do icone trancado (35%) nao desce pra ela: a pilula sai inteira em qualquer card.
		Label etiqueta = Etiqueta();
		icone.AddChild(etiqueta);
		_etiquetas[s.Path] = etiqueta;

		// A LIGACAO DE PRE-REQUISITO, sob demanda: o mouse em cima pendura a etiqueta em quem vem antes.
		if (s.PreReqs.Length > 0 && card.GetChild(0) is Button b)
		{
			string[] antes = s.PreReqs;
			b.MouseEntered += () => MarcarRequisitos(antes, true);
			b.MouseExited += () => MarcarRequisitos(antes, false);
		}
		return card;
	}

	/// <summary>
	/// A ETIQUETA "requisito": uma pilula BRANCA com a palavra em letra escura, pendurada na aresta
	/// de cima do card. Nasce escondida; ver <see cref="MarcarRequisitos"/>.
	///
	/// Branca de proposito: nao e cor de estado nenhum (verde = sua, laranja = compravel e hover,
	/// cinza = trancada). E uma pilula com uma PALAVRA, e nao uma borda, porque a marca de
	/// pre-requisito tem que ser de OUTRA natureza que o realce do card apontado: quando as duas
	/// eram borda, o dono leu o vizinho aceso como o card sob o mouse. E um Label com stylebox
	/// proprio, desenhado sempre que visivel -- o Flat do botao nao entra nesta historia.
	/// </summary>
	private static Label Etiqueta()
	{
		var e = new Label
		{
			Name = "Requisito",
			Text = "REQUISITO",
			Visible = false,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		e.AddThemeFontSizeOverride("font_size", 9);
		e.AddThemeColorOverride("font_color", Tema.Fundo);
		var pilula = new StyleBoxFlat { BgColor = Tema.Texto, ContentMarginLeft = 5, ContentMarginRight = 5, ContentMarginTop = 1, ContentMarginBottom = 1 };
		pilula.SetCornerRadiusAll(6);
		e.AddThemeStyleboxOverride("normal", pilula);
		// Ancorada no meio da aresta de CIMA do icone (= a de cima do conteudo do card, 6 px por
		// dentro da borda): 12 px de altura centrados 5 px acima dela, entao a pilula pisa na borda
		// do card e entra 6 px na folga entre as linhas. Cresce ate o minimo do texto pros DOIS
		// lados (Grow = Both): continua centrada seja qual for a largura da palavra.
		e.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		e.OffsetLeft = 0; e.OffsetRight = 0; e.OffsetTop = -11; e.OffsetBottom = 1;
		e.GrowHorizontal = Control.GrowDirection.Both;
		e.GrowVertical = Control.GrowDirection.Both;
		return e;
	}

	/// <summary>Mostra (ou esconde) a etiqueta "requisito" dos cards dados. So mexe em quem esta na arvore aberta.</summary>
	private void MarcarRequisitos(string[] paths, bool mostrar)
	{
		foreach (string p in paths)
			if (_etiquetas.TryGetValue(p, out Label? e) && IsInstanceValid(e)) e.Visible = mostrar;
	}

	/// <summary>
	/// Nomes de skill do DM chegam a 52 caracteres ("Turtle School - Kamehameha | Turtle Destruction
	/// Wave"). Num card de 104 px isso e cinco linhas. O que vem depois do `|` e o subtitulo, e sai;
	/// o que passar de 34 letras ganha reticencias. O nome inteiro continua no tooltip e na ficha.
	/// </summary>
	private static string NomeCurto(string nome)
	{
		int barra = nome.IndexOf('|');
		if (barra > 0) nome = nome[..barra].Trim();
		return nome.Length > 34 ? nome[..33].TrimEnd() + "…" : nome;
	}

	/// <summary>O que o card diz embaixo do nome, e em que cor. E o veredito do Core traduzido.</summary>
	private (string Texto, Color Cor) EstadoDoCartao(SkillCatalog cat, Skill s, Veredito v)
	{
		int custo = v.Custo > 0 ? v.Custo : SkillCatalog.CustoDe(s);
		string marcos = $"{custo} marco{(custo == 1 ? "" : "s")}";
		return v.Motivo switch
		{
			Recusa.JaSabe => ("SUA", Tema.Bom),
			Recusa.Pode => (marcos, Tema.Destaque),
			Recusa.SemMarcos => ($"{marcos}  ·  faltam {v.FaltamMarcos}", Tema.Perigo),
			Recusa.TierTrancado => ("tier trancado", Tema.TextoFraco),
			Recusa.FaltaPreRequisito => ("depois de " + string.Join(", ", v.PreReqsFaltando.Select(p => cat.Get(p)?.Nome ?? p)), Tema.TextoFraco),
			Recusa.AguardaAcendedor => ("abre quando " + NomesLegiveis.Condicao(v.Acendedor), Tema.TextoFraco),
			Recusa.Apagada => ("apagada: " + NomesLegiveis.Condicao(v.Acendedor), Tema.TextoFraco),
			Recusa.Desligada => (s.Ensinavel ? "só com um mestre" : "sem acendedor", Tema.TextoFraco),
			Recusa.SoVilao => ("só pra vilão", Tema.TextoFraco),
			Recusa.RacaOuClasse => ("não é da sua raça ou classe", Tema.TextoFraco),
			Recusa.SemArvore => ("não pende desta árvore", Tema.TextoFraco),
			_ => ("indisponível", Tema.TextoFraco),
		};
	}

	/// <summary>
	/// O MOTIVO POR EXTENSO, pra ficha. O card diz "tier trancado"; a ficha diz quanto falta investir e
	/// onde. E a mesma informacao do veredito, com espaco pra frase inteira.
	/// </summary>
	private string MotivoPorExtenso(SkillCatalog cat, Skill s, Veredito v)
	{
		string arvore = cat.Get(v.Arvore)?.Nome ?? v.Arvore;
		return v.Motivo switch
		{
			Recusa.JaSabe => "Esta habilidade já é sua.",
			Recusa.Pode => "Você pode aprender isto agora.",
			Recusa.SemMarcos => $"Faltam {v.FaltamMarcos} marco{(v.FaltamMarcos == 1 ? "" : "s")}: custa {v.Custo} e você tem {_livro.MarcosLivres}.",
			Recusa.TierTrancado => v.FaltaInvestir > 0
				? $"A vitrine de {arvore} está no tier {v.TierDaArvore} e esta habilidade é do tier {v.TierDaSkill}: invista mais {v.FaltaInvestir} marco{(v.FaltaInvestir == 1 ? "" : "s")} nessa árvore (você investiu {v.Investido})."
				: $"A vitrine de {arvore} está no tier {v.TierDaArvore} e esta habilidade é do tier {v.TierDaSkill}: o tier sobe por outro caminho, não por marcos.",
			Recusa.FaltaPreRequisito => "Antes você precisa de: " + string.Join(", ", v.PreReqsFaltando.Select(p => cat.Get(p)?.Nome ?? p)) + ".",
			Recusa.AguardaAcendedor => $"Abre sozinha quando: {NomesLegiveis.Condicao(v.Acendedor)}.",
			Recusa.Apagada => $"Uma regra de {arvore} a apagou: {NomesLegiveis.Condicao(v.Acendedor)}.",
			Recusa.Desligada => s.Ensinavel
				? "Marco nenhum abre esta: alguém que a saiba precisa te ensinar, de perto."
				: "Nenhuma regra deste port a acende. No jogo antigo ela abria por um caminho que ainda não foi trazido.",
			Recusa.SoVilao => "Só quem foi designado vilão aprende isto.",
			Recusa.RacaOuClasse => "Sua raça ou classe não aprende esta.",
			Recusa.SemArvore => "Não pende de nenhuma árvore sua.",
			_ => "Indisponível.",
		};
	}

	// =====================================================================
	// A ARTE DO CARD
	// =====================================================================
	/// <summary>
	/// ARTE DA TECNICA POR VERB -- um vinculo NOVO, que o catalogo nao tem.
	///
	/// A `Tecnica` do Core nao sabe de sprite (e nem deve: o Core nao abre arquivo de imagem). O que
	/// existe sao as folhas em `Assets/Sprites/Techniques`, com o nome que o DM deu ao `.dmi`. A
	/// tabela liga o verb que a skill concede a folha e ao estado cujo primeiro quadro E a tecnica
	/// (o "origin"/"head" de um raio, e nao o rastro) -- conferido quadro a quadro: so entram as que
	/// tem mais de 40% de pixel pintado no quadro escolhido. As outras caem no icone da categoria,
	/// que e o que o BYOND fazia com TODAS (um `ability.jpg` unico).
	/// </summary>
	private static readonly Dictionary<string, (string Folha, string Estado)> ArtePorVerbo = new(StringComparer.OrdinalIgnoreCase)
	{
		["Kamehameha"] = ("Beam - Kamehameha", "origin_south"),
		["SpiritBomb"] = ("Ball - Spirit Bomb", "default"),
		["Final_Flash"] = ("Final Flash", "head_east"),
		["GalicGun"] = ("galacticgun", "origin_south"),
		["Death_Ball"] = ("deathball", "default"),
		["Death_Beam"] = ("Beam Laser", "head_east"),
		["Kikoho"] = ("Kikoho", "default_south"),
		["Energy_Shield"] = ("Electro Shield", "default"),
	};

	private static readonly Dictionary<string, Texture2D?> _artes = new(StringComparer.OrdinalIgnoreCase);

	private static Texture2D? ArteDaSkill(Skill s)
	{
		foreach (string verbo in s.Verbos)
			if (ArtePorVerbo.TryGetValue(verbo, out (string Folha, string Estado) arte))
				return Arte("tec:" + verbo, () => Miniaturas.De($"res://Assets/Sprites/Techniques/{arte.Folha}.tres", arte.Estado));

		string categoria = NomesLegiveis.Categoria(s.Tipo);
		string arquivo = categoria switch
		{
			"físico" => "melee",
			"buff" => "buff",
			"ki" => "beam",
			"forma" => "transformation",
			_ => "ability",
		};
		return Arte("cat:" + arquivo, () =>
		{
			string caminho = $"res://Assets/Sprites/DU/UI/{arquivo}.jpg";
			return ResourceLoader.Exists(caminho) ? ResourceLoader.Load<Texture2D>(caminho) : null;
		});
	}

	private static Texture2D? Arte(string chave, Func<Texture2D?> carregar)
	{
		if (_artes.TryGetValue(chave, out Texture2D? t)) return t;
		t = carregar();
		_artes[chave] = t;
		return t;
	}

	// =====================================================================
	// A FICHA -- um painel no tema, por cima da aba
	// =====================================================================
	private Control? _ficha;
	private PanelContainer? _fichaPainel;

	/// <summary>SO PRA BANCADA: o painel da ficha aberta, ou nulo.</summary>
	public PanelContainer? FichaDeTeste => _fichaPainel != null && IsInstanceValid(_fichaPainel) ? _fichaPainel : null;

	/// <summary>Fecha a ficha (ou a escolha de linhagem). Devolve se havia o que fechar.</summary>
	private bool FecharFicha()
	{
		if (_ficha == null) return false;
		if (IsInstanceValid(_ficha)) _ficha.QueueFree();
		_ficha = null;
		_fichaPainel = null;
		return true;
	}

	/// <summary>
	/// A MOLDURA DA FICHA: um veu escuro por cima da aba (clicar nele fecha) e o painel no centro.
	/// Pendurada em `_raiz`, e nao na pagina: a pagina e remontada a cada mudanca, e a ficha nao pode
	/// morrer no meio de um clique porque um pacote de ficha chegou.
	/// </summary>
	private PanelContainer AbrirMoldura(string nome)
	{
		FecharFicha();

		_ficha = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore, Name = nome };
		var veu = new ColorRect
		{
			AnchorRight = 1, AnchorBottom = 1,
			Color = new Color(0, 0, 0, 0.55f),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		veu.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true }) FecharFicha(); };
		_ficha.AddChild(veu);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
		_ficha.AddChild(centro);

		_fichaPainel = Tema.Painel1(14);
		_fichaPainel.Name = "FichaDaSkill";
		centro.AddChild(_fichaPainel);

		_raiz.AddChild(_ficha);
		return _fichaPainel;
	}

	/// <summary>
	/// A FICHA DA SKILL: nome, descricao, efeitos, pre-requisitos nomeados, custo, e o botao certo.
	///
	/// POR QUE UM PASSO A MAIS: a compra e IRREVERSIVEL na pratica (o esquecimento devolve os marcos,
	/// mas derruba o que dependia dela) e no card so cabem icone, nome e preco. Sem esta tela o
	/// jogador clica num nome que nao conhece e descobre o que comprou depois de pago.
	///
	/// E ELA DIZ O QUE A SKILL FAZ, nao so o que ela e. O texto do DM descreve a fantasia ("a arte
	/// da assassinacao deixa sua marca"); os EFEITOS extraidos dizem o numero. Os dois juntos sao
	/// a unica resposta honesta pra "vale a pena?".
	///
	/// OS EFEITOS SAO OS DOIS CANAIS: o que a COMPRA da e o que cada NIVEL da (`FichaDeSkill`, no
	/// Core). Ate 2026-09-03 so o primeiro entrava, e a ficha de dezesseis skills da Mente -- todas
	/// portadas por degrau -- dizia "efeito ainda nao portado". O dono leu isso e pediu o port de
	/// algo que ja estava portado.
	///
	/// O BOTAO DEPENDE DO ESTADO: comprar (com o preco e o saldo escritos), esquecer (em dois cliques,
	/// o molde da `TelaDeTecnicas`) quando e sua e esquecivel, escolher linhagem quando e sua e de
	/// escolha unica, e nenhum -- so o motivo -- quando esta trancada.
	/// </summary>
	private void AbrirFicha(Skill s)
	{
		SkillCatalog? cat = Catalogo();
		if (cat == null || GameClient.Instance is not { } cli) return;
		string raca = _atributos.Raca ?? "";
		string classe = cli.Sheet.Class ?? "";
		Veredito v = Avaliar(cat, s, raca, classe);

		PanelContainer painel = AbrirMoldura("Ficha");
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
		caixa.AddThemeConstantOverride("separation", 8);
		painel.AddChild(caixa);

		// ---- cabecalho: icone, nome, onde pende ----
		var cabeca = new HBoxContainer();
		cabeca.AddThemeConstantOverride("separation", 10);
		cabeca.AddChild(new TextureRect
		{
			Texture = ArteDaSkill(s),
			CustomMinimumSize = new Vector2(LadoDoIcone, LadoDoIcone),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		});
		var nomes = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var titulo = new Label { Text = s.Nome, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		titulo.AddThemeFontSizeOverride("font_size", 18);
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		nomes.AddChild(titulo);
		string arvore = cat.Get(v.Arvore)?.Nome ?? cat.Arvores.FirstOrDefault(a => Array.IndexOf(a.Galhos, s.Path) >= 0)?.Nome ?? "";
		var onde = new Label { Text = $"{(arvore.Length > 0 ? arvore + "  ·  " : "")}tier {s.Tier}  ·  {NomesLegiveis.Categoria(s.Tipo)}" };
		onde.AddThemeFontSizeOverride("font_size", 12);
		onde.AddThemeColorOverride("font_color", Tema.TextoFraco);
		nomes.AddChild(onde);
		cabeca.AddChild(nomes);
		caixa.AddChild(cabeca);

		// ---- a descricao, como esta no catalogo (em ingles -- divida registrada no cabecalho) ----
		if (s.Desc.Length > 0)
			caixa.AddChild(new Label { Text = s.Desc, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) });

		caixa.AddChild(new HSeparator());

		// ---- o que ela faz: a COMPRA e o NIVEL, montados no Core (`FichaDeSkill`) ----
		// A ficha lia so `Skill.Buffs/Mults/Verbos/Estilo` (a compra) e chamava de "efeito ainda nao
		// portado" toda skill cujo efeito mora nos DEGRAUS -- as dezesseis da Mente que nao sao a
		// raiz, o Green Dean, o Hokuto. O texto agora vem de uma funcao pura do Core, a mesma que a
		// `--menteskills` (secao 8) cobra frase a frase.
		FichaDeSkill.Texto ficha = FichaDeSkill.Montar(cat, s, RegrasDeNivel.Get(s.Path));
		if (!ficha.TemEfeito)
		{
			// HONESTIDADE NO BALCAO: as folhas que ainda nao tem efeito portado (nem na compra nem
			// por nivel) continuam dizendo isso. Vender em silencio seria cobrar por nada sem dizer.
			var l = new Label { Text = FichaDeSkill.SemEfeitoAinda };
			l.AddThemeColorOverride("font_color", Tema.TextoFraco);
			caixa.AddChild(l);
		}
		else
		{
			foreach (string linha in ficha.NaCompra) caixa.AddChild(LinhaDeEfeito(linha));
			if (ficha.Progressao.Length > 0) caixa.AddChild(LinhaDeNota(ficha.Progressao));
			// MUITOS DEGRAUS ROLAM: a Ki Unlocked tem treze marcos, e uma ficha que nao coubesse na
			// tela perderia os botoes de baixo. Ate oito linhas ficam soltas; acima disso entram numa
			// caixa de altura fixa com rolagem propria.
			if (ficha.PorNivel.Count <= 8)
				foreach (string linha in ficha.PorNivel) caixa.AddChild(LinhaDeEfeito(linha));
			else
			{
				var rol = new ScrollContainer
				{
					CustomMinimumSize = new Vector2(440, 200),
					HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
				};
				var lista = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
				foreach (string linha in ficha.PorNivel) lista.AddChild(LinhaDeEfeito(linha));
				rol.AddChild(lista);
				caixa.AddChild(rol);
			}
			if (ficha.NoTopo.Length > 0) caixa.AddChild(LinhaDeNota(ficha.NoTopo));
		}

		// ---- pre-requisitos NOMEADOS, com o tique de quais ja sao seus ----
		if (s.PreReqs.Length > 0)
		{
			string lista = string.Join("   ", s.PreReqs.Select(p => (_livro.Sabe(p) ? "✓ " : "✗ ") + (cat.Get(p)?.Nome ?? p)));
			string folga = s.Folga > 0 ? $"   (basta {s.PreReqs.Length - s.Folga} de {s.PreReqs.Length})" : "";
			var pre = new Label { Text = "pede:  " + lista + folga, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) };
			pre.AddThemeFontSizeOverride("font_size", 12);
			pre.AddThemeColorOverride("font_color", Tema.TextoFraco);
			caixa.AddChild(pre);
		}

		// ---- o estado, por extenso ----
		var motivo = new Label { Text = MotivoPorExtenso(cat, s, v), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) };
		motivo.AddThemeFontSizeOverride("font_size", 12);
		motivo.AddThemeColorOverride("font_color", v.Motivo switch
		{
			Recusa.JaSabe => Tema.Bom,
			Recusa.Pode => Tema.Texto,
			Recusa.SemMarcos => Tema.Perigo,
			_ => Tema.TextoFraco,
		});
		caixa.AddChild(motivo);

		// ---- os botoes ----
		var botoes = new HBoxContainer();
		botoes.AddThemeConstantOverride("separation", 8);
		var fechar = new Button { Text = v.Motivo is Recusa.Pode or Recusa.JaSabe ? "Cancelar" : "Fechar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		fechar.Pressed += () => FecharFicha();
		botoes.AddChild(fechar);

		string caminho = s.Path;
		int custo = v.Custo > 0 ? v.Custo : SkillCatalog.CustoDe(s);
		if (v.Motivo == Recusa.Pode)
		{
			var comprar = new Button
			{
				Text = $"Comprar  ·  {custo} marco{(custo == 1 ? "" : "s")}  (você tem {_livro.MarcosLivres})",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			comprar.AddThemeColorOverride("font_color", Tema.Destaque);
			// QUEM DECIDE E O SERVIDOR: daqui so sai o pedido, e a resposta dele remonta a aba.
			comprar.Pressed += () => { GameClient.Instance?.SendAprender(caminho); FecharFicha(); };
			botoes.AddChild(comprar);
		}
		else if (v.Motivo == Recusa.JaSabe)
		{
			if (s.Escolhas.Length > 0)
			{
				// A ESCOLHA UNICA sobrevive ao relog: quem comprou e saiu antes de responder acha a
				// pergunta de novo aqui, na ficha da skill que ja e sua.
				var escolher = new Button { Text = "Escolher linhagem…", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
				Skill comEscolha = s;
				escolher.Pressed += () => AbrirEscolha(comEscolha);
				botoes.AddChild(escolher);
			}

			if (s.Esquecivel)
			{
				// ESQUECER PEDE UM SEGUNDO CLIQUE, e o alerta do DM diz por que: *"This decision is
				// irreversable!"*. O botao troca de texto em vez de abrir outra caixa -- a confirmacao
				// mora onde o dedo ja esta, e um "tem certeza?" que aparece embaixo do cursor e um
				// "tem certeza?" que se clica sem ler. E o molde da `TelaDeTecnicas`.
				var esquecer = new Button { Text = $"Esquecer  ·  reembolsa {custo}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
				bool armado = false;
				esquecer.Pressed += () =>
				{
					if (!armado)
					{
						armado = true;
						esquecer.Text = "Esquecer — tem certeza? O que depende dela cai junto.";
						esquecer.AddThemeColorOverride("font_color", Tema.Perigo);
						return;
					}
					GameClient.Instance?.SendVerbo("skill_esquecer", caminho);
					FecharFicha();
				};
				botoes.AddChild(esquecer);
			}
			else
			{
				var fixa = new Label { Text = "esta não se esquece", VerticalAlignment = VerticalAlignment.Center };
				fixa.AddThemeFontSizeOverride("font_size", 12);
				fixa.AddThemeColorOverride("font_color", Tema.TextoFraco);
				botoes.AddChild(fixa);
			}
		}
		caixa.AddChild(botoes);
	}

	/// <summary>
	/// A ESCOLHA UNICA de uma skill: as casas lado a lado, com o que cada uma da.
	///
	/// QUEM DECIDE E O SERVIDOR, como sempre. Daqui so sai `skill_escolha &lt;path&gt; &lt;n&gt;`; se a pessoa
	/// ja escolheu (a escolha e definitiva, igual ao `chosen` do DM, que so morre esquecendo a
	/// skill), quem responde "voce ja escolheu" e ele. O cliente nao guarda a resposta porque nao
	/// e dele -- e o mesmo motivo de o botao de compra nao descontar marco sozinho.
	/// </summary>
	private void AbrirEscolha(Skill s)
	{
		PanelContainer painel = AbrirMoldura("Escolha");
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
		caixa.AddThemeConstantOverride("separation", 8);
		painel.AddChild(caixa);

		var titulo = new Label { Text = s.Nome };
		titulo.AddThemeFontSizeOverride("font_size", 18);
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		caixa.AddChild(titulo);

		var aviso = new Label
		{
			Text = "Escolha uma linhagem. A escolha é definitiva e, até você escolher, esta habilidade não faz nada.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(440, 0),
		};
		aviso.AddThemeColorOverride("font_color", Tema.TextoFraco);
		caixa.AddChild(aviso);
		caixa.AddChild(new HSeparator());

		for (int i = 0; i < s.Escolhas.Length; i++)
		{
			Escolha e = s.Escolhas[i];
			int casa = i + 1;
			string path = s.Path;
			var b = new Button { Text = e.Rotulo, Alignment = HorizontalAlignment.Left };
			b.Pressed += () => { GameClient.Instance?.SendVerbo("skill_escolha", $"{path} {casa}"); FecharFicha(); };
			caixa.AddChild(b);

			// O QUE A CASA DA, pelo mesmo canal de sempre: os efeitos EXTRAIDOS. Sem isto a
			// escolha definitiva seria feita por nome de linhagem, que nao diz nada.
			var l = new Label { Text = "      " + FichaDeSkill.TextoDaCasa(e), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) };
			l.AddThemeColorOverride("font_color", Tema.Destaque);
			caixa.AddChild(l);
		}

		var fechar = new Button { Text = "Depois" };
		fechar.Pressed += () => FecharFicha();
		caixa.AddChild(fechar);
	}

	// O TEXTO DO QUE UMA SKILL (ou uma casa) FAZ mora no Core: `FichaDeSkill.Montar` e
	// `FichaDeSkill.TextoDaCasa`. Aqui so se pinta. As duas versoes locais que existiam liam so a
	// COMPRA, e foi por isso que a Mente inteira saia como "efeito ainda nao portado".

	/// <summary>Uma linha de efeito da ficha: com marcador, na cor de destaque. As casas da escolha
	/// unica chegam recuadas (quatro espacos) e saem sem o marcador.</summary>
	private static Label LinhaDeEfeito(string linha)
	{
		bool recuada = linha.StartsWith("    ", StringComparison.Ordinal);
		var l = new Label
		{
			Text = recuada ? "      " + linha.TrimStart() : "• " + linha,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(440, 0),
		};
		l.AddThemeColorOverride("font_color", Tema.Destaque);
		return l;
	}

	/// <summary>Uma nota da ficha (como sobe, a soma no topo): menor e em cor fraca.</summary>
	private static Label LinhaDeNota(string texto)
	{
		var l = new Label { Text = texto, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) };
		l.AddThemeFontSizeOverride("font_size", 12);
		l.AddThemeColorOverride("font_color", Tema.TextoFraco);
		return l;
	}

	// =====================================================================
	// A ABA SKILLS -- o que eu ja sei
	// =====================================================================
	/// <summary>
	/// O QUE EU JA SEI, agrupado pela arvore de onde veio, com o tier e O QUE CADA UMA FAZ.
	///
	/// A arvore e o endereco da habilidade na cabeca de quem joga: ninguem lembra "eu comprei
	/// Backstab", lembra "eu fui pro lado do assassino".
	///
	/// A COLUNA DA DIREITA DIZIA O `type` DO DM, EM VERDE ("Body Buff", "Sprit Buff", "misc") -- na
	/// posicao em que toda outra aba escreve um STATUS, e na cor de "bom". Lia como "esta ativa", e
	/// nao dizia nada. Agora ela diz o tier e a categoria limpa, e a linha de baixo diz o efeito --
	/// que e o que se quer saber de uma habilidade que ja e sua.
	///
	/// A ULTIMA SECAO E A INTERESSANTE: o que NAO pende de nenhuma arvore sua so pode ter chegado
	/// por ensino. E a mesma leitura que o Core faz em <see cref="SkillBook.PenduraEmArvoreDe"/>
	/// ("skill solta e ensinada, nao comprada"): Kaio-ken e Genkidama vem do Senhor Kaioh.
	/// </summary>
	private void Sabidas()
	{
		SkillCatalog? cat = Catalogo();
		if (cat == null) { Aviso("catálogo de skills não encontrado (rode o AssetPipeline: comando 'skills')."); return; }

		if (_livro.Aprendidas.Count == 0)
		{
			Nota("Você ainda não aprendeu nada. Abra a aba Learning.", Cartao("Aprendidas  (0)"));
		}
		else
		{
			string raca = _atributos.Raca ?? "";
			string classe = GameClient.Instance?.Sheet.Class ?? "";

			// Vai esvaziando conforme cada arvore reclama as suas. O que sobrar no fim veio de fora.
			var sobrou = new HashSet<string>(_livro.Aprendidas, StringComparer.OrdinalIgnoreCase);

			// UM CARTAO POR ARVORE, na mesma lingua da Learning (ver MenuJogo.Pecas.cs). O titulo
			// continua "{arvore}  ({n})": a `--diagskills` F8 procura um rotulo que COMECA com
			// "STRENGTH OF BODY" e traz "(10)", e o `Cartao` passa o titulo pelo `Tema.Rotulo`, que e
			// quem o poe em caixa alta.
			foreach (Skill arv in ArvoresDoPersonagem(cat, raca, classe))
			{
				var minhas = Folhas(cat, arv).Where(s => sobrou.Contains(s.Path)).ToList();
				if (minhas.Count == 0) continue;

				VBoxContainer corpo = Cartao($"{arv.Nome}  ({minhas.Count})");
				foreach (Skill s in minhas.OrderBy(x => x.Tier))
				{
					sobrou.Remove(s.Path);
					LinhaDeSabida(cat, s, corpo);
				}
			}

			if (sobrou.Count > 0)
			{
				VBoxContainer corpo = Cartao($"Avulsas  ({sobrou.Count})");
				foreach (string p in sobrou.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
				{
					if (cat.Get(p) is { } s) LinhaDeSabida(cat, s, corpo);
					else Linha(p, "", Tema.TextoFraco, corpo);
				}
				Nota("Não pendem de nenhuma árvore que este painel liste: vieram de um mestre, "
					+ "ou de um caminho que só se abre jogando.", corpo);
			}
		}

		// os verbs registrados por skills que ja tem EFEITO implementado
		var acoes = Verbos.Da(Verbos.Skills).ToList();
		if (acoes.Count == 0) return;
		VBoxContainer cartaoDeAcoes = Cartao($"Ações  ({acoes.Count})");
		// EM DUAS COLUNAS quando ha mais de um, como os verbs da aba Other: cada botao continua um
		// `Button` com o nome do verb (contrato das bancadas), com a frase do que faz embaixo.
		Control pai = acoes.Count >= 2 ? Colunas(cartaoDeAcoes) : cartaoDeAcoes;
		foreach (Verbo v in acoes) pai.AddChild(BotaoComDescricao(BotaoDe(v), v.Descricao));
	}

	/// <summary>
	/// Nome a esquerda, "tier N · categoria" a direita, e o efeito embaixo -- a compra E o nivel, numa
	/// linha, DENTRO do cartao da arvore. O valor "tier 1  ·  buff" e contrato: e o que a `--diagskills`
	/// F8 le por `ValorDesenhado("Skills", "Basic Training")`.
	///
	/// O NOME SAI EM DESTAQUE, e nao apagado como o rotulo de toda outra linha: nas outras abas o rotulo
	/// e a pergunta ("Ki") e o valor e a resposta; aqui e o contrario -- o nome da habilidade e o que se
	/// procura na lista, e "tier 1 · buff" e o detalhe.
	/// </summary>
	private static void LinhaDeSabida(SkillCatalog cat, Skill s, Control pai)
	{
		HBoxContainer linha = Linha(s.Nome, $"tier {s.Tier}  ·  {NomesLegiveis.Categoria(s.Tipo)}", Tema.TextoFraco, pai);
		if (linha.GetChild(0) is Label nome)
		{
			nome.AddThemeColorOverride("font_color", Tema.Texto);
			nome.AddThemeFontSizeOverride("font_size", 14);
		}
		FichaDeSkill.Texto ficha = FichaDeSkill.Montar(cat, s, RegrasDeNivel.Get(s.Path));
		var l = new Label
		{
			Text = "      " + (ficha.TemEfeito ? ficha.Resumo : "efeito ainda não portado"),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		l.AddThemeFontSizeOverride("font_size", 12);
		l.AddThemeColorOverride("font_color", ficha.TemEfeito ? Tema.Destaque : Tema.TextoFraco);
		pai.AddChild(l);
	}

	// =====================================================================
	// A BUSCA ACHA SKILL
	// =====================================================================
	/// <summary>
	/// AS SKILLS DAS SUAS ARVORES que casam com o termo, como botoes que LEVAM ate elas: o clique
	/// limpa a busca, abre a arvore certa e abre a ficha da skill.
	///
	/// So as das suas arvores: uma skill que nao pende de arvore nenhuma sua nao tem "ate ela" pra
	/// levar. Devolve quantas achou, pra quem chama saber se a busca ficou vazia.
	/// </summary>
	private int AchadosDeSkills(string termo)
	{
		SkillCatalog? cat = Catalogo();
		if (cat == null || GameClient.Instance is not { } cli) return 0;
		string raca = _atributos.Raca ?? "";
		string classe = cli.Sheet.Class ?? "";

		var vistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var achados = new List<(Skill Skill, Skill Arvore)>();
		foreach (Skill arv in ArvoresDoPersonagem(cat, raca, classe))
			foreach (Skill s in Folhas(cat, arv))
			{
				if (!s.Nome.Contains(termo, StringComparison.OrdinalIgnoreCase) || !vistas.Add(s.Path)) continue;
				achados.Add((s, arv));
			}
		if (achados.Count == 0) return 0;

		Secao($"Habilidades  ({achados.Count})");
		foreach ((Skill s, Skill arv) in achados)
		{
			Veredito v = Avaliar(cat, s, raca, classe);
			(string estado, Color cor) = EstadoDoCartao(cat, s, v);
			var b = new Button
			{
				Text = $"{s.Nome}   [{arv.Nome} · tier {s.Tier}]   ·   {estado}",
				TooltipText = s.Desc.Length > 0 ? s.Desc : s.Path,
				Alignment = HorizontalAlignment.Left,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			b.AddThemeColorOverride("font_color", cor);
			Skill alvo = s;
			string caminho = arv.Path;
			b.Pressed += () =>
			{
				_busca.Text = "";   // sem sinal: o `Redesenhar` abaixo ja remonta
				_aba = "Learning";
				_arvoreAberta = caminho;
				Redesenhar();
				AbrirFicha(alvo);
			};
			_conteudo.AddChild(b);
		}
		return achados.Count;
	}
}
