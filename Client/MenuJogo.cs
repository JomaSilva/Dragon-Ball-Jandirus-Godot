using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O MENU DO JOGO (tecla P). E o painel de abas do BYOND, portado.
///
/// A LISTA DE ABAS NAO E INVENTADA: sai literal de `BuildStatsHTML()` em
/// Code/Modules/User Interface/HtmlUI.dm:137 --
///
///     Stats, Items, Equip, Body, Forms, Ki, People, World, Skills, Other, Learning, Tech
///
/// mais tres que APARECEM E SOMEM conforme o personagem, e essa parte e a que importa:
///
///   * "Sense" so existe depois de aprender a skill (`register_html_tab("Sense")`);
///   * com o SCOUTER ligado ela nao ganha uma aba nova -- ela VIRA "Scan", e o BP passa a ser
///     lido em numero exato em vez de "???";
///   * "Nav" so existe com nav system a bordo;
///   * "Admin" so pra quem e.
///
/// Quem decide isso e o SERVIDOR, no campo <see cref="Protocol.Poder"/> da ficha lenta. O
/// cliente nunca decide sozinho que sabe uma habilidade -- fosse assim, bastaria mexer no
/// cliente pra ganhar a aba de Scan e ler o BP alheio.
///
/// A BUSCA vale pro jogo inteiro, nao pra aba: digitar filtra os verbs de TODAS as categorias.
/// Ninguem lembra em que aba mora a tecnica que quer usar.
/// </summary>
public partial class MenuJogo : CanvasLayer
{
	public static MenuJogo? Instancia { get; private set; }

	/// <summary>O menu esta aberto? O jogo pergunta antes de andar, como faz com o chat.</summary>
	public static bool Aberto { get; private set; }

	/// <summary>
	/// A BUSCA esta com o foco? So ela engole o teclado -- com o menu aberto e o campo solto,
	/// da pra continuar andando e lutando enquanto se olha a ficha, que e como o painel do
	/// BYOND funcionava.
	/// </summary>
	public static bool Digitando => Instancia is { Visible: true } m && m._busca.HasFocus();

	/// <summary>
	/// As abas fixas, na ordem do original.
	///
	/// "ITEMS" SAIU. Ela era um aviso de "vem com o sistema de itens" -- e o sistema chegou: a
	/// mochila e a tecla I, com grade, pilha e as acoes de cada item. Deixar a aba seria oferecer
	/// duas portas pra mesma coisa, e uma delas nao abre.
	/// </summary>
	private static readonly string[] Fixas =
	[
		"Stats", "Equip", "Body", "Forms", "Ki",
		"People", "World", "Cargos", "Skills", "Other", "Learning", "Tech",
	];

	private Control _raiz = null!;
	private PanelContainer _painel = null!;
	private HBoxContainer _barraAbas = null!;
	private LineEdit _busca = null!;
	private VBoxContainer _conteudo = null!;
	private Label _titulo = null!;

	private string _aba = "Stats";
	private Protocol.AtributosState _atributos;

	/// <summary>
	/// QUE ARVORE ESTA ABERTA no balcao de aprendizado. Vazio = a lista de arvores.
	///
	/// E o `mob/var/CurrentTree` do original (SkillTreesWindow.dm:53), com a mesma vida: nasce
	/// nulo, o clique num card de arvore o preenche, e o botao de voltar o zera de novo
	/// (`backbutton()`, SkillTreesWindow.dm:114).
	/// </summary>
	private string _arvoreAberta = "";

	/// <summary>A gaveta das trancadas da arvore aberta esta escancarada? Ver <see cref="SkillsDaArvore"/>.</summary>
	private bool _verTrancadas;

	/// <summary>
	/// O CATALOGO, lido do mesmo arquivo que o servidor le.
	///
	/// O cliente precisa dele pra MOSTRAR (nome, custo, o que falta) e pra nao oferecer o que
	/// vai ser recusado. Quem DECIDE continua sendo o servidor -- e a mesma funcao do Core
	/// roda nos dois lados, entao nao ha duas regras pra divergir.
	/// </summary>
	private static SkillCatalog? _catalogo;
	private readonly SkillBook _livro = new();

	public override void _Ready()
	{
		Instancia = this;
		Layer = 3;              // acima do chat (2), abaixo do menu de pause (20)
		Montar();
		Visible = false;

		// ============================ METODOS NOMEADOS, E NAO LAMBDAS ============================
		// Estas nove assinaturas eram lambdas anonimas, e por isso NAO TINHAM COMO ser canceladas --
		// `-=` precisa do mesmo delegate, e uma lambda nova nunca e igual a anterior.
		//
		// O `GameClient` SOBREVIVE ao logout (o `VoltarAoLogin` derruba os filhos do Boot, e ele nao
		// e um deles), entao cada volta ao menu deixava nove assinantes apontando pra um node ja
		// liberado. No login seguinte o pacote de ficha -- que chega a 5 Hz -- acordava os mortos, e
		// cada um estourava `ObjectDisposedException` dentro do `Handle`, virando um
		// "[client] pacote invalido" a cada 200 ms. Duas idas ao menu, dezoito erros por segundo.
		//
		// O `World` sempre fez certo (metodos nomeados + `-=` no `_ExitTree`); esta classe e as
		// outras telas e que nasceram fora do padrao.
		// =========================================================================================
		if (GameClient.Instance is { } cli)
		{
			cli.SheetUpdated += AoFicha;
			cli.AtributosRecebidos += AoAtributos;
			cli.CorpoAtualizado += AoCorpo;
			cli.SkillsMudaram += AoSkills;
			cli.CargosMudaram += AoCargos;
			cli.ConhecidosMudaram += AoConhecidos;
			cli.ContasMudaram += AoContas;
			cli.TechMudou += AoTech;
			cli.EstilosMudaram += AoEstilos;
			cli.ObrasMudaram += AoObras;
			_atributos = cli.Atributos;
			SincronizarLivro();
		}

		// O `Verbos.Mudou` E ESTATICO -- ele vive o processo inteiro. Um assinante morto aqui nao
		// some nem quando o mundo inteiro e derrubado.
		Verbos.Mudou += AoMudarVerbos;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli)
		{
			cli.SheetUpdated -= AoFicha;
			cli.AtributosRecebidos -= AoAtributos;
			cli.CorpoAtualizado -= AoCorpo;
			cli.SkillsMudaram -= AoSkills;
			cli.CargosMudaram -= AoCargos;
			cli.ConhecidosMudaram -= AoConhecidos;
			cli.ContasMudaram -= AoContas;
			cli.TechMudou -= AoTech;
			cli.EstilosMudaram -= AoEstilos;
			cli.ObrasMudaram -= AoObras;
		}
		Verbos.Mudou -= AoMudarVerbos;

		if (Instancia == this) Instancia = null;
		Aberto = false;
	}

	// =====================================================================
	// O QUE O SERVIDOR MANDA
	// =====================================================================
	private void AoFicha(SheetState _) { if (Visible) Redesenhar(); }

	private void AoAtributos(Protocol.AtributosState a)
	{
		bool trocouRaca = _atributos.Raca != a.Raca;
		_atributos = a;
		// A BUSCA TAMBEM PRECISA SABER. Ela varre todas as categorias de proposito, entao
		// esconder a aba nao bastava pra esconder os verbs de admin de quem nao e admin.
		Verbos.DefinirAdmin(a.Tem(Protocol.Poder.Admin));
		// a RACA so chega na ficha lenta, e e ela que diz quais habilidades existem
		if (trocouRaca) Habilidades.Montar(a.Raca ?? "");
		if (Visible) Redesenhar();
	}

	private void AoCorpo(List<Protocol.ParteState> _) { if (Visible && _aba == "Body") Redesenhar(); }

	private void AoSkills()
	{
		SincronizarLivro();
		// APRENDER UMA SKILL PODE CRIAR UM BOTAO. Refaz a lista inteira em vez de
		// acrescentar so o novo: e a mesma funcao do login, entao nao ha um segundo
		// caminho que possa divergir do primeiro.
		Habilidades.Montar(_atributos.Raca ?? "");
		if (Visible) Redesenhar();
	}

	private void AoCargos() { if (Visible) Redesenhar(); }

	/// <summary>
	/// A LISTA DE CONHECIDOS MUDOU. So redesenha na aba People, como o `AoContas` faz com a de
	/// admin: um pedido de amizade chegando no meio de uma luta nao pode remontar a aba de Stats
	/// que o jogador esta lendo.
	/// </summary>
	private void AoConhecidos() { if (Visible && _aba == "People") Redesenhar(); }
	private void AoContas() { if (Visible && _aba == Verbos.Admin) Redesenhar(); }
	private void AoTech() { if (Visible) Redesenhar(); }

	private void AoEstilos()
	{
		Habilidades.Montar(_atributos.Raca ?? "");
		if (Visible) Redesenhar();
	}

	private void AoObras() { if (Visible && _aba == "Tech") Redesenhar(); }
	private void AoMudarVerbos() { if (Visible) Redesenhar(); }

	// =====================================================================
	// ABRIR E FECHAR
	// =====================================================================
	public override void _Input(InputEvent e)
	{
		if (e is not InputEventKey { Pressed: true, Echo: false } k) return;

		// ESCREVENDO NO CHAT, "p" e a letra p. A regra que o dono deu, e a mesma que ja vale
		// pra andar e socar.
		if (Chat.Digitando) return;

		if (k.Keycode == Key.P)
		{
			Alternar();
			GetViewport().SetInputAsHandled();
			return;
		}

		// ESC fecha o menu ANTES de o menu de pause ouvir a tecla (ele escuta em
		// _UnhandledInput, que roda depois daqui)
		if (Visible && k.Keycode == Key.Escape)
		{
			// ESC DESFAZ UMA CAMADA POR VEZ, da mais interna pra mais externa: primeiro a busca,
			// depois a arvore aberta, e so entao o menu. Fechar tudo de uma vez faria quem entrou
			// numa arvore por engano perder o painel inteiro pra corrigir o clique.
			if (_busca.HasFocus() && _busca.Text.Length > 0) { _busca.Text = ""; Redesenhar(); }
			else if (_aba == "Learning" && _arvoreAberta.Length > 0) FecharArvore();
			else Fechar();
			GetViewport().SetInputAsHandled();
		}
	}

	public void Alternar()
	{
		if (Visible) Fechar(); else Abrir();
	}

	/// <summary>
	/// As abas que a bancada percorre (`--diagmenu`).
	///
	/// AS VIVAS, e nao as fixas. Antes esta propriedade devolvia `Fixas`, e por isso a bancada
	/// nunca abriu Sense, Nav nem Admin -- justo as tres que so existem em certas condicoes, que
	/// sao as mais faceis de quebrar sem ninguem ver. A aba de admin, em particular, so aparece
	/// pro host: sem isto, ela so seria exercitada por alguem jogando.
	/// </summary>
	public string[] AbasDeTeste => [.. Abas()];

	/// <summary>Troca de aba pelo MESMO caminho que o botao usa. So pra bancada.</summary>
	public void IrPara(string aba)
	{
		if (aba == _aba) return;
		FecharArvore();
		_aba = aba;
		Redesenhar();
	}

	public void Abrir()
	{
		Visible = true;
		Aberto = true;
		Redesenhar();
	}

	public void Fechar()
	{
		Visible = false;
		Aberto = false;
		_busca.ReleaseFocus();
	}

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		// SEMITRANSPARENTE como o chat: o dono pediu, e faz sentido -- da pra conferir a ficha
		// sem perder de vista o que esta acontecendo em volta.
		_painel = new PanelContainer
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -380, OffsetRight = 380, OffsetTop = -290, OffsetBottom = 290,
			GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Both,
		};
		var vidro = Tema.Caixa(new Color(0.06f, 0.07f, 0.10f, 0.88f), Tema.Borda, 12);
		_painel.AddThemeStyleboxOverride("panel", vidro);
		_raiz.AddChild(_painel);

		var coluna = new VBoxContainer();
		coluna.AddThemeConstantOverride("separation", 8);
		_painel.AddChild(coluna);

		// --- cabecalho: nome + busca ---
		var topo = new HBoxContainer();
		topo.AddThemeConstantOverride("separation", 10);
		coluna.AddChild(topo);

		_titulo = new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_titulo.AddThemeFontSizeOverride("font_size", 18);
		_titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		topo.AddChild(_titulo);

		_busca = new LineEdit
		{
			PlaceholderText = "procurar acao...",
			CustomMinimumSize = new Vector2(240, 0),
			ClearButtonEnabled = true,
		};
		_busca.TextChanged += _ => Redesenhar();
		topo.AddChild(_busca);

		// --- barra de abas ---
		var rolagemAbas = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, 34),
			VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		coluna.AddChild(rolagemAbas);
		_barraAbas = new HBoxContainer();
		_barraAbas.AddThemeConstantOverride("separation", 2);
		rolagemAbas.AddChild(_barraAbas);

		coluna.AddChild(new HSeparator());

		// --- conteudo ---
		var rolagem = new ScrollContainer
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		coluna.AddChild(rolagem);

		// A PILHA DE PAGINAS. Uma VBox por aba, todas vivas na arvore e so UMA visivel -- ver
		// `PaginaDe`. O `_conteudo` aponta pra pagina da vez, entao os construtores de aba
		// continuam escrevendo em `_conteudo` sem saber que existe pilha.
		_pilha = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		rolagem.AddChild(_pilha);
		_conteudo = PaginaDe("Stats");

		var rodape = Tema.Rotulo("P fecha  ·  ESC fecha  ·  a busca vale pra todas as abas");
		rodape.HorizontalAlignment = HorizontalAlignment.Center;
		coluna.AddChild(rodape);
	}

	// =====================================================================
	// AS ABAS QUE EXISTEM AGORA
	// =====================================================================
	/// <summary>
	/// A lista viva de abas. Reproduz a logica do `BuildStatsHTML()`: fixas, mais as que o
	/// personagem destravou, com o Sense virando Scan quando o scouter esta ligado.
	/// </summary>
	private List<string> Abas()
	{
		var abas = new List<string>(Fixas);

		if (_atributos.Tem(Protocol.Poder.Sense))
			abas.Add(_atributos.Tem(Protocol.Poder.Scouter) ? "Scan" : "Sense");
		else if (_atributos.Tem(Protocol.Poder.Scouter))
			abas.Add("Scan");   // scouter sem a skill le BP igual: e aparelho, nao dom

		if (_atributos.Tem(Protocol.Poder.Nav)) abas.Add("Nav");
		if (_atributos.Tem(Protocol.Poder.Admin)) abas.Add(Verbos.Admin);

		return abas;
	}

	/// <summary>As abas que estao na tela agora, pra saber se a barra precisa ser refeita.</summary>
	private string _abasNaTela = "";
	private readonly Dictionary<string, Button> _botoes = [];

	// =====================================================================
	// A PILHA DE PAGINAS -- uma por aba, construida UMA vez
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ISTO EXISTE ============================
	/// O pedido do dono foi "deixar os menus ja instanciados so que invisiveis, e quando apertar a
	/// tecla so deixar visivel -- e mais rapido pra engine". Os DOIS menus ja nasciam com o mundo e
	/// so trocavam `Visible` (Boot.MontarMundo); o que custava era outra coisa, e era pior: o
	/// CONTEUDO era destruido e reconstruido do zero a cada `Redesenhar()`.
	///
	/// E `Redesenhar()` nao roda so ao abrir -- roda a cada pacote de ficha, 5x por segundo com o
	/// menu aberto, e uma vez inteira a cada abertura. A aba de skills sozinha monta centenas de
	/// linhas. Era ai que estava o custo que o `Visible` nao ia resolver.
	///
	/// Agora cada aba tem a PROPRIA pagina, viva na arvore e escondida. Trocar de aba e trocar
	/// `Visible`, e reconstruir so acontece quando o que a aba MOSTRA muda (ver `_assinaturas`) --
	/// e a mesma regra que ja valia pra barra de abas desde o defeito do "botao piscando".
	/// =============================================================================
	/// </summary>
	private VBoxContainer _pilha = null!;
	private readonly Dictionary<string, VBoxContainer> _paginas = [];

	/// <summary>O que cada pagina mostrava da ultima vez. Igual = nao ha o que refazer.</summary>
	private readonly Dictionary<string, string> _assinaturas = [];

	private VBoxContainer PaginaDe(string aba)
	{
		if (_paginas.TryGetValue(aba, out VBoxContainer? p) && IsInstanceValid(p)) return p;
		p = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false };
		p.AddThemeConstantOverride("separation", 3);
		_pilha.AddChild(p);
		_paginas[aba] = p;
		return p;
	}

	/// <summary>
	/// O QUE ESTA ABA MOSTRA, resumido numa string. Se ela nao mudou, a pagina que ja esta montada
	/// continua correta e nao ha nada a refazer.
	///
	/// E DE PROPOSITO GROSSEIRA: numeros arredondados, contagens em vez de listas. Uma assinatura
	/// exata custaria quase o mesmo que remontar; o objetivo e cortar as 5 remontagens por segundo
	/// em que NADA visivel mudou, nao perseguir o ultimo quadro.
	///
	/// A BUSCA ENTRA NA ASSINATURA porque ela troca o conteudo inteiro da pagina (ver `Achados`).
	/// </summary>
	private string Assinatura(string aba, SheetState f)
	{
		GameClient? c = GameClient.Instance;
		string comum = $"{_busca.Text.Trim()}|{_arvoreAberta}|{f.Estado}";
		return aba switch
		{
			"Stats" => $"{comum}|{f.ExpressedBP:0}|{f.Ki:0}|{f.MaxKi:0}|{f.HP:0}|{f.Vigor:0}|{f.Nutricao:0}"
					 + $"|{_atributos.PhysOff:0.##}|{_atributos.PhysDef:0.##}|{_atributos.KiOff:0.##}"
					 + $"|{_atributos.Speed:0.##}|{_atributos.Idade}|{f.Class}",
			"Body" => $"{comum}|{string.Join(',', (c?.Corpo ?? []).Select(p => p.Nome + p.Vida + (p.Decepado ? "x" : "")))}",
			"Learning" => $"{comum}|{c?.SkillsAprendidas.Count}|{c?.MarcosTotais}|{c?.MarcosLivres}",
			"Skills" => $"{comum}|{c?.SkillsAprendidas.Count}",
			// A PROFICIENCIA DA DISCIPLINA ENTRA AQUI porque a aba passou a DESENHA-LA: as quatro
			// formas divinas relatam a proficiencia da skill no lugar da maestria, e sem este pedaco
			// a barra delas ficaria congelada na tela enquanto sobe de verdade no servidor.
			"Forms" => $"{comum}|{_atributos.FormaAtual}|{string.Join(',', (_atributos.Maestrias ?? []).Select(m => $"{m.Forma}:{m.Pct:0.#}"))}"
					 + $"|{_atributos.Disciplina}:{_atributos.DiscReal:0.#}",
			"Tech" => $"{comum}|{c?.TechNivel:0.#}|{c?.Zeni:0}|{c?.TechXp:0}|{c?.Obras.Count}|{c?.Catalogo.Count}",
			"Cargos" => $"{comum}|{string.Join(',', (c?.Cargos ?? []).Select(r => r.Chave + r.Dono + r.Falta))}",
			// A ABA ADMIN TEM CAIXAS DE TEXTO, e uma remontagem as recria vazias. Por isso a
			// assinatura dela e so o que o painel DESENHA (a lista de contas e as marcas de cada
			// uma) -- nada que mude a cada tick. O que o admin digitou fica fora da pagina, em
			// `_avisoDigitado`/`_contaDigitada`, e volta pra caixa na remontagem.
			// A ABA NAV TEM UM WIDGET COM ESTADO (zoom, arrasto, selecao), e por isso a assinatura
			// dela NAO passa pelo `comum`.
			//
			// Uma assinatura vazia mandaria remontar a pagina a cada pacote de ficha e o mapa
			// nasceria de novo, enquadrado no padrao, no meio do arrasto. Mas o `comum` tambem nao
			// serve: ele carrega `f.Estado`, cujos dois bits altos sao a DIRECAO DO CORPO -- e ela
			// e reescrita a cada pacote de input enquanto se anda. Com o menu aberto o personagem
			// continua andando (o `Foco.Digitando` so vale se a BUSCA tiver foco), entao virar pra
			// esquerda remontava a aba e jogava fora o zoom que o jogador acabou de ajustar.
			//
			// O que a aba mostra que muda de verdade: a zona (espaco ou nao, que liga o botao de
			// viajar), a seed do universo, e a busca -- que troca o conteudo da pagina inteira.
			"Nav" => $"{_busca.Text.Trim()}|{c?.Zone.Hash}|{c?.SeedDoUniverso}",
			// O CLIMA ENTRA AQUI SO PELO QUE E DISCRETO -- o tipo e "e forcado?". A FORCA fica de
			// fora de proposito: ela sobe e desce continuamente durante os 45 s de transicao, e
			// remontar a pagina a cada quadro do fade recriaria as caixas de texto vazias na mao
			// de quem esta digitando. E a mesma armadilha que o comentario acima descreve.
			// `FormaAtual` ENTRA porque o painel de formas marca com ● o degrau em uso. Sem ela a
			// marca ficaria no botao velho depois de forcar uma forma -- o painel diria que nada
			// aconteceu justamente no clique em que tudo aconteceu.
			Verbos.Admin => $"{comum}|{c?.AlvoId}|{_atributos.FormaAtual}"
						  + $"|{World.Instancia?.TempoQueFaz?.Tipo}|{World.Instancia?.TempoQueFaz?.Forcado}|"
						  + string.Join(',', (c?.Contas ?? []).Select(a => $"{a.Conta}{a.Admin}{a.Banida}{a.Online}")),
			"Ki" => $"{comum}|{f.Ki:0}|{f.MaxKi:0}|{c?.SkillsAprendidas.Count}",
			// PEOPLE PRECISA DE ASSINATURA DESDE QUE ELA TEM BOTAO. Ela caia no `_ => ""` (remonta
			// sempre), que era inofensivo enquanto a aba era so texto -- agora ela tem os botoes de
			// relacao e o aceitar/recusar do pedido de amizade, e uma pagina refeita 5x por segundo
			// destroi o botao debaixo do dedo de quem clica. E a MESMA armadilha que o bloco do
			// Admin acima descreve, e ela mordeu este projeto uma vez.
			//
			// Entram: os vinculos (que so mudam a cada 3 s, e devagar), o pedido pendente, e os
			// NOMES visiveis -- que mudam quando alguem entra ou sai do campo de visao, e nao a
			// cada quadro. Os pontos vao arredondados de proposito: 0,1 por passo remontaria a
			// pagina toda vez sem nada visivel mudar.
			"People" => $"{comum}|{c?.PedidoDeAmizade}|"
					  + string.Join(',', (c?.Conhecidos ?? []).Select(
							k => $"{k.Assinatura}{k.Amizade:0}{k.Familiaridade}{k.Relacao}{k.Rival}{k.Inimizade:0}"))
					  + "|" + string.Join(',', World.Instancia?.NomesVisiveis() ?? []),
			// As abas fixas sem dado proprio (Equip, Other) sao texto parado: uma assinatura
			// so ja basta pra elas nunca mais serem remontadas.
			"Equip" or Verbos.Outros => comum,
			// AS QUE DEPENDEM DE QUEM ESTA NA TELA refazem sempre: People e World listam corpos que
			// entram e saem a cada snapshot, e uma assinatura que os cobrisse custaria o mesmo que
			// remontar. Devolver vazio e dizer "nao ha cache pra esta".
			_ => "",
		};
	}

	/// <summary>
	/// `--diagmenu`: mede o menu em vez de afirmar que ele ficou rapido.
	///
	/// Sao dois numeros que importam e que nao dava pra ver antes: quantas vezes o conteudo foi
	/// REMONTADO (contra quantas o pedido foi atendido pela pagina que ja estava pronta) e quanto
	/// custa uma remontagem. O primeiro e o que diz se o cache funciona; o segundo e o que o dono
	/// sente ao apertar P.
	/// </summary>
	private static readonly bool Diag = Array.IndexOf(OS.GetCmdlineArgs(), "--diagmenu") >= 0;
	private int _remontagens, _reaproveitadas;
	private double _msRemontando;

	/// <summary>O relatorio do `--diagmenu`. Publico porque quem fecha o menu e quem o imprime.</summary>
	public string Relatorio() =>
		$"[menu] {_remontagens} remontagens ({_msRemontando:0.0} ms no total, "
		+ $"{(_remontagens > 0 ? _msRemontando / _remontagens : 0):0.00} ms cada) | "
		+ $"{_reaproveitadas} pedidos atendidos pela pagina ja montada";

	private void Redesenhar()
	{
		List<string> abas = Abas();

		// a aba aberta pode ter DEIXADO de existir (tirou o scouter com o Scan aberto -- o
		// original tratava exatamente este caso)
		if (!abas.Contains(_aba)) _aba = abas.Contains("Sense") ? "Sense" : "Stats";

		// ============================ A BARRA DE ABAS NAO SE RECONSTROI ============================
		// Ela era destruida e recriada a CADA `Redesenhar()`. E `Redesenhar` roda a cada pacote de
		// ficha -- varias vezes por segundo, ainda mais desde que vigor e estado entraram na
		// deteccao de mudanca. Um botao recem-criado nao esta sob o mouse ate o proximo quadro,
		// entao o realce acendia e apagava sem parar e clicar virava questao de sorte. Foi o que o
		// dono descreveu: "boto o mouse em cima da aba e ela fica piscando... ai pra clicar fica
		// ruim".
		//
		// O CONTEUDO continua sendo refeito (ele E o que muda). A barra so muda quando a LISTA de
		// abas muda -- tirar o scouter tira a aba Scan --, e isso e raro e detectavel.
		// =========================================================================================
		string assinatura = string.Join('', abas);
		if (assinatura != _abasNaTela)
		{
			_abasNaTela = assinatura;
			foreach (Node n in _barraAbas.GetChildren()) n.QueueFree();
			_botoes.Clear();

			foreach (string a in abas)
			{
				string qual = a;
				var b = new Button
				{
					Text = a,
					ToggleMode = true,
					FocusMode = Control.FocusModeEnum.None,
				};
				b.AddThemeFontSizeOverride("font_size", 12);
				// TROCAR DE ABA FECHA A ARVORE. O original tambem nao guardava a arvore aberta entre
				// visitas: `TreeWindowClose()` zera o CurrentTree (SkillTreesWindow.dm:287). Voltar pra
				// Learning e cair na lista de arvores, que e a casa da aba.
				b.Pressed += () => { if (qual != _aba) FecharArvore(); _aba = qual; Redesenhar(); };
				_barraAbas.AddChild(b);
				_botoes[qual] = b;
			}
		}

		// qual esta marcada e propriedade, nao node novo: da pra atualizar sem recriar nada
		foreach ((string nome, Button botao) in _botoes) botao.ButtonPressed = nome == _aba;

		SheetState f = GameClient.Instance?.Sheet ?? default;
		_titulo.Text = GameClient.Instance?.LocalName ?? "";

		// SO A PAGINA DA VEZ APARECE. As outras continuam montadas, escondidas -- voltar pra elas
		// e uma troca de `Visible`, e nao uma remontagem.
		_conteudo = PaginaDe(_aba);
		foreach ((string nome, VBoxContainer pg) in _paginas)
			if (IsInstanceValid(pg)) pg.Visible = nome == _aba;

		// NADA MUDOU? Entao a pagina que ja esta ai continua certa. E o que corta as cinco
		// remontagens por segundo que o pacote de ficha provocava com o menu aberto.
		string sig = Assinatura(_aba, f);
		if (sig.Length > 0 && _assinaturas.TryGetValue(_aba, out string? antiga)
			&& antiga == sig && _conteudo.GetChildCount() > 0)
		{
			_reaproveitadas++;
			return;
		}
		_assinaturas[_aba] = sig;

		ulong t0 = Time.GetTicksUsec();
		_remontagens++;

		foreach (Node n in _conteudo.GetChildren()) { _conteudo.RemoveChild(n); n.QueueFree(); }
		Montar(f);
		_msRemontando += (Time.GetTicksUsec() - t0) / 1000.0;
	}

	/// <summary>Enche a pagina da aba da vez. Separado do <see cref="Redesenhar"/> so pra o
	/// cronometro ter onde fechar -- os `return` no meio de um metodo unico o perderiam.</summary>
	private void Montar(SheetState f)
	{
		// BUSCA VENCE A ABA. Ver o comentario da classe.
		string termo = _busca.Text.Trim();
		if (termo.Length > 0) { Achados(termo); return; }

		switch (_aba)
		{
			case "Stats": Stats(f); break;
			case "Body": Corpo(); break;
			case "Ki": Ki(f); break;
			case "People": Gente(); break;
			case "World": Mundo(); break;
			case "Learning": Aprendizado(); break;
			case "Forms": AbaFormas(); break;
			case "Cargos": AbaCargos(); break;
			case "Nav": AbaNav(); break;
			case "Skills": Sabidas(); break;
			case Verbos.Outros: ListaDeVerbos(_aba); break;
			case Verbos.Admin: AbaAdmin(); break;
			default: AindaNao(_aba); break;
		}
	}

	// =====================================================================
	// PECAS
	// =====================================================================
	private void Secao(string texto)
	{
		if (_conteudo.GetChildCount() > 0) _conteudo.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
		var l = Tema.Rotulo(texto);
		l.AddThemeColorOverride("font_color", Tema.Destaque);
		_conteudo.AddChild(l);
		_conteudo.AddChild(new HSeparator());
	}

	/// <summary>Uma linha "rotulo .... valor", com a cor de qualidade do original (hi/av/lo).</summary>
	private void Linha(string rotulo, string valor, Color? cor = null)
	{
		var h = new HBoxContainer();
		var a = new Label { Text = rotulo, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		a.AddThemeColorOverride("font_color", Tema.TextoFraco);
		a.AddThemeFontSizeOverride("font_size", 13);
		h.AddChild(a);
		var b = new Label { Text = valor, HorizontalAlignment = HorizontalAlignment.Right };
		b.AddThemeColorOverride("font_color", cor ?? Tema.Texto);
		b.AddThemeFontSizeOverride("font_size", 13);
		h.AddChild(b);
		_conteudo.AddChild(h);
	}

	private void Aviso(string texto)
	{
		var l = new Label { Text = texto, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		l.AddThemeColorOverride("font_color", Tema.TextoFraco);
		l.AddThemeFontSizeOverride("font_size", 12);
		_conteudo.AddChild(l);
	}

	// =====================================================================
	// STATS -- o ui_tab_stats do original
	// =====================================================================
	private void Stats(SheetState f)
	{
		Secao("Poder");

		// "???" SEM SCOUTER. E a regra do original e ela e do JOGO, nao da interface: ninguem
		// le o proprio poder de luta em numero sem um aparelho que meca.
		//
		// COM scouter sai o expresso E o base, e as duas metades sao literais do DM
		// (HtmlUI.dm:178-181: `[FullNum(round(expressedBP))] (base [FullNum(round(BP))])` no ramo
		// `if(scouteron)`, `??? (no scouter)` no outro). Esta e a UNICA linha do painel inteiro que
		// tem permissao de imprimir BP -- a aba Forms nao imprime nem o limiar (ver AbaFormas).
		Linha("Battle Power", _atributos.Tem(Protocol.Poder.Scouter)
			? $"{f.ExpressedBP:N0}   (base {f.BP:N0})"
			: "???   (sem scouter)");
		Linha("Vida", $"{f.HP:0}%", f.HP >= 66 ? Tema.Bom : f.HP <= 33 ? Tema.Perigo : Tema.Texto);
		Linha("Ki", $"{f.Ki:N0} / {f.MaxKi:N0}   ({(f.MaxKi > 0 ? f.Ki / f.MaxKi * 100 : 0):0}%)");
		Linha("Vigor", $"{_atributos.Stamina * 100:0}%");

		// ============================ A NUTRICAO FALTAVA, E ELA EXPLICA O VIGOR ============================
		// O vigor cai sozinho e so sobe as custas do tanque de comida. Sem este numero na tela, um
		// jogador com o folego minguando nao tem como saber que o problema e FOME -- ele ve uma
		// barra caindo e nenhuma causa. Ver `Core.Stats.Nutricao`.
		//
		// A COR AVISA ANTES DE DOER: o aviso de fome do servidor bate em 25% de vigor, mas quem fica
		// sem tanque para de recuperar MUITO antes disso.
		double pct = f.NutricaoMax > 0 ? f.Nutricao / f.NutricaoMax * 100 : 0;
		Linha("Nutrição", $"{f.Nutricao:0} / {f.NutricaoMax:0}   ({pct:0}%)",
			pct >= 50 ? Tema.Bom : pct <= 15 ? Tema.Perigo : Tema.Texto);

		Secao("Atributos");
		(string Nome, float Valor)[] atts =
		[
			("Ofensiva Fisica", _atributos.PhysOff), ("Defesa Fisica", _atributos.PhysDef),
			("Ofensiva de Ki", _atributos.KiOff),    ("Defesa de Ki", _atributos.KiDef),
			("Tecnica", _atributos.Technique),       ("Pericia de Ki", _atributos.KiSkill),
			("Velocidade", _atributos.Speed),        ("Esoterico", _atributos.Esoteric),
		];

		// A COR SAI DA COMPARACAO COM A PROPRIA MEDIA, como no `ui_qual()`: um atributo nao e
		// alto ou baixo em absoluto, e alto ou baixo PRA ESTE personagem. E o que deixa a
		// vocacao de cada um visivel de relance.
		float media = 0;
		foreach ((string _, float v) in atts) media += v;
		media /= Math.Max(atts.Length, 1);

		foreach ((string nome, float v) in atts)
		{
			Color cor = v >= media * 1.2f ? Tema.Bom : v <= media * 0.8f ? Tema.Perigo : Tema.Texto;
			string rotulo = v >= media * 1.2f ? "alto" : v <= media * 0.8f ? "baixo" : "medio";
			Linha(nome, $"{v * 10:0}   ({rotulo})", cor);
		}
		Linha("Forca de Vontade", $"{_atributos.Willpower:0.##}");

		Secao("Estado");

		// A CLASSE NAO APARECE, NUNCA. Ela e sorteio cego na criacao (por isso a tela de criacao
		// so da uma dica indireta, CreationScreen.cs:500) e o painel do original tambem nunca a
		// imprimiu: `ui_tab_stats()` lista poder, atributos, emocao e estilo -- classe nao esta la
		// (HtmlUI.dm:175-229). Escrever "Legendary" numa linha entrega de graca o que o jogo inteiro
		// trata como descoberta, e ainda vaza pra quem olha a tela de outro.
		Linha("Raca", _atributos.Raca ?? "");
		Linha("Idade", $"{_atributos.Idade}");
		Linha("Condicao", f.Morto ? "MORTO" : f.KO ? "NOCAUTEADO" : "de pe",
			f.Morto || f.KO ? Tema.Perigo : Tema.Bom);
		Linha("Golpe", f.Letal ? "LETAL" : "nao-letal", f.Letal ? Tema.Perigo : Tema.Texto);
		Linha("Cadencia do soco", $"{f.SocoMs} ms");
	}

	// =====================================================================
	// BODY -- os membros, os mesmos que o boneco do HUD desenha
	// =====================================================================
	private void Corpo()
	{
		List<Protocol.ParteState> partes = GameClient.Instance?.Corpo ?? [];
		if (partes.Count == 0) { Aviso("O corpo ainda nao chegou do servidor."); return; }

		Secao("Membros");
		foreach (Protocol.ParteState p in partes)
		{
			if (p.Decepado) { Linha(p.Nome, "DECEPADO", Tema.Perigo); continue; }
			Color cor = p.Vida >= 66 ? Tema.Bom : p.Vida <= 33 ? Tema.Perigo : Tema.Texto;
			Linha(p.Nome, $"{p.Vida}%", cor);
		}
	}

	private void Ki(SheetState f)
	{
		Secao("Energia");
		Linha("Ki atual", $"{f.Ki:N0}");
		Linha("Ki maximo", $"{f.MaxKi:N0}");
		Linha("Percentual", $"{(f.MaxKi > 0 ? f.Ki / f.MaxKi * 100 : 0):0.#}%");
		Aviso("\nCarregar Ki, tecnicas e formas entram aqui quando as skills forem portadas.");
	}

	// =====================================================================
	// PEOPLE -- o "Known People" do original
	// =====================================================================
	/// <summary>
	/// ============================ A ABA PEOPLE -- O KNOWN-PEOPLE DO ORIGINAL ============================
	/// Ela tinha SO a lista de quem estava na tela, que e o que o `NomesVisiveis` sabia responder.
	/// Agora ela e o que o `ui_tab_people()` do DM (`HtmlUI.dm:545-560`) mostrava: a lista de quem
	/// voce CONHECE, com a ficha "como visto da ultima vez" e o degrau de proximidade.
	///
	/// AS DUAS SECOES CONVIVEM porque respondem coisas diferentes: "quem esta aqui agora" (que e o
	/// que voce usa pra marcar alguem) e "quem eu conheco" (que e o que sobrevive ao logout). No
	/// original as duas tambem existiam, em telas separadas.
	///
	/// O RETRATO NAO VEIO. La ele e o icone achatado do corpo (`contact_portrait`), servido por
	/// `browse_rsc`; aqui a aparencia e montada em camadas pelo cliente e "a foto de como ele
	/// estava" seria um SEGUNDO caminho de vestir alguem. Ficaram os campos de texto, que sao o que
	/// a aba de la realmente le.
	/// ======================================================================================================
	/// </summary>
	private void Gente()
	{
		if (GameClient.Instance is not { } cli) return;

		// ---- o pedido de amizade pendente vem PRIMEIRO: e o unico item aqui que expira ----
		if (cli.PedidoDeAmizade.Length > 0)
		{
			Secao($"{cli.PedidoDeAmizade} quer ser seu amigo");
			var linha = new HBoxContainer();
			var sim = new Button { Text = "Aceitar" };
			sim.Pressed += () => cli.SendVerbo("amizade_aceitar");
			var nao = new Button { Text = "Recusar" };
			nao.Pressed += () => cli.SendVerbo("amizade_recusar");
			linha.AddChild(sim);
			linha.AddChild(nao);
			_conteudo.AddChild(linha);
		}

		Secao("Quem voce conhece");
		if (cli.Conhecidos.Count == 0)
			Aviso("Voce ainda nao conviveu com ninguem de quem valha a pena lembrar.");

		foreach (GameClient.ConhecidoInfo c in cli.Conhecidos)
		{
			// SEM FICHA = so vinculo, sem rosto. `??? (assinatura)` e a forma do original.
			string nome = c.Nome.Length > 0 ? c.Nome : $"??? ({c.Assinatura})";
			string grau = Convivio.RotuloDeProximidade(c.Amizade);
			string odio = Convivio.RotuloDeInimizade(c.Inimizade);
			var rel = (Relacao)c.Relacao;

			Linha(nome, $"{grau} ({c.Amizade:0})");
			Aviso($"      {c.Raca} / {c.Classe}  ·  como visto da ultima vez"
				  + $"  ·  convivio {c.Familiaridade}"
				  + (rel != Relacao.Nenhuma ? $"  ·  {Convivio.NomeDaRelacao(rel)}" : "")
				  + (c.Rival ? "  ·  RIVAL" : "")
				  + (odio.Length > 0 ? $"  ·  {odio} ({c.Inimizade:0})" : ""));

			BotoesDeRelacao(cli, c);
		}

		Secao("Quem esta por perto");
		List<string> nomes = World.Instancia?.NomesVisiveis() ?? [];
		if (nomes.Count == 0) { Aviso("Ninguem no seu campo de visao."); return; }
		foreach (string n in nomes) Linha(n, "");
	}

	/// <summary>
	/// AS DECLARACOES QUE ESTA FAMILIARIDADE JA PAGA -- o verb `Relation()` do original, que la era
	/// um `input()` com a lista inteira e a explicacao dos custos no titulo.
	///
	/// O BOTAO APARECE APAGADO em vez de sumir, que e a regra do menu deste port: saber que "Amor"
	/// existe e que custa 200 de convivio e informacao; esconder e esconder o jogo do jogador.
	///
	/// **QUEM DECIDE E O SERVIDOR.** Isto aqui e conveniencia -- o `VerboRelacao` confere a
	/// familiaridade de novo, porque apagar botao nunca foi permissao.
	/// </summary>
	private void BotoesDeRelacao(GameClient cli, GameClient.ConhecidoInfo c)
	{
		if (c.Nome.Length == 0) return;   // sem ficha nao ha o que declarar

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 2);
		foreach (Relacao r in Convivio.Declaraveis)
		{
			int pede = Convivio.FamiliaridadeExigida(r);
			bool pode = c.Familiaridade >= pede;
			var b = new Button
			{
				Text = Convivio.NomeDaRelacao(r),
				Disabled = !pode || (Relacao)c.Relacao == r,
				TooltipText = pode ? "declarar" : $"exige {pede} de convivio (voce tem {c.Familiaridade})",
			};
			b.AddThemeFontSizeOverride("font_size", 11);
			string sig = c.Assinatura, nome = Convivio.NomeDaRelacao(r);
			b.Pressed += () => cli.SendVerbo("relacao", $"{sig}|{nome}");
			linha.AddChild(b);
		}
		_conteudo.AddChild(linha);
	}

	private void Mundo()
	{
		Secao("Onde voce esta");
		Linha("Zona", GameClient.Instance?.Zone.Name ?? "");

		Jandirus.Core.World.RelogioDoPlaneta r = World.Instancia?.RelogioDoLugar
											 ?? Jandirus.Core.World.RelogioDoPlaneta.Padrao;
		Linha("Ciclo", Jandirus.Core.World.Ceu.NomeDoCiclo(r));

		if (World.Instancia?.Ceu is { } c)
		{
			Linha("Hora", $"{Jandirus.Core.World.Ceu.NomeDaHora(c.Hora)} ({c.Hora * 24:00.0}h)");

			if (!r.Lua.Existe) Linha("Lua", "este mundo nao tem lua");
			else
			{
				Linha("Lua", Jandirus.Core.World.Ceu.NomeDaFase(c.Fase)
							 + (c.LuaNoCeu ? " (no ceu)" : " (abaixo do horizonte)"));

				// QUANTO FALTA PRA CHEIA. E a informacao com que se PLANEJA -- sem ela o ciclo
				// da lua e uma surpresa que cai na cabeca de quem tem rabo.
				double faltam = Jandirus.Core.World.Ceu.SegundosAteACheia(r, World.Instancia.TempoDoMundo);
				Linha("Lua cheia", faltam < 0 ? "nunca (a lua nao chega a nascer aqui)"
							 : faltam < 1 ? "AGORA"
							 : faltam < 60 ? $"em {faltam:0} s"
							 : $"em {faltam / 60:0} min");
			}
		}

		Jandirus.Core.World.ClimaDoPlaneta cl = World.Instancia?.ClimaDoLugar
											?? Jandirus.Core.World.ClimaDoPlaneta.Nenhum;
		if (World.Instancia?.TempoQueFaz is { } tq)
		{
			Linha("Clima", tq.Ativo
				? $"{Jandirus.Core.World.Clima.Nome(tq.Tipo)} ({tq.Forca:P0})"
					+ (tq.Forcado ? " -- forcado" : "")
				: "ceu limpo");

			// O QUE PODE CAIR AQUI vem do `allowedWeatherTypes` do DM, e e informacao de LUGAR:
			// saber que em Vegeta chove sangue e nao agua e parte de conhecer Vegeta.
			if (cl.Existe && cl.Permitidos.Length > 0)
				Linha("Pode cair", string.Join(", ",
					cl.Permitidos.Select(Jandirus.Core.World.Clima.Nome)));
			else Linha("Pode cair", "nada -- o ceu daqui nao muda");
		}

		Aviso("\nCada planeta corre o proprio dia e o proprio tempo: a hora e o ceu daqui");
		Aviso("nao sao os da Terra. A conquista do planeta entra aqui com o sistema dela.");
	}

	// =====================================================================
	// NAV -- o mapa do espaco
	// =====================================================================
	/// <summary>
	/// O MAPA ESTELAR: a galaxia desenhada, com zoom, arrasto e clique.
	///
	/// ============================ ERA UMA LISTA, E LISTA NAO E MAPA ============================
	/// Esta aba mostrava os corpos celestes das tres chunks em volta, em texto, ordenados por
	/// distancia. Isso responde "o que ha por perto" -- e nao responde a pergunta que faz um mapa
	/// estelar existir, que e "PRA ONDE EU VOU". Sem posicao relativa nao da pra escolher rota, nao
	/// da pra saber que Namek fica do lado oposto de Vegeta, e o universo inteiro cabe em cinco
	/// linhas de texto que mudam quando voce anda.
	///
	/// O pedido do dono: "faca um mapa do espaco com todos os planetas que o servidor sabe onde
	/// estao... voce clica nos planetas e seleciona viajar... e voce pode dar zoom e zoom out".
	/// O desenho, o zoom e o clique moram no <see cref="MapaEstelar"/>; esta aba e a moldura.
	/// ===========================================================================================
	///
	/// O TEMPO CONTINUA SENDO O QUE IMPORTA no texto: "1.608.035 px" nao diz nada a ninguem, "7
	/// dias" diz tudo -- e a mesma unidade em que o anime mede a viagem pra Namek. A conta sai do
	/// Core (velocidade de voo x ciclo do dia), entao ela nao pode divergir do que a viagem custa.
	/// </summary>
	private void AbaNav()
	{
		if (GameClient.Instance is not { } cli) return;

		bool noEspaco = Jandirus.Core.World.Espaco.EhEspaco(cli.Zone);

		// O MAPA APARECE EM TERRA FIRME TAMBEM -- so o botao de viajar e que nao.
		//
		// Ele e uma CARTA: consultar antes de decolar e metade do uso de uma carta estelar, e
		// esconder o mapa de quem esta no chao obrigaria a subir pra descobrir aonde subir.
		Secao(noEspaco ? "Carta estelar" : "Carta estelar (em terra: so leitura)");

		_mapa = new MapaEstelar { Name = "MapaEstelar" };

		// A BARRA VEM ANTES DO MAPA na arvore, e isso e proposital.
		//
		// O mapa e alto (a carta so serve grande) e a aba mora num ScrollContainer: com a barra
		// EMBAIXO ela caia fora da area visivel e so aparecia depois de rolar -- os controles de
		// zoom ficavam escondidos justamente na tela que existe pra dar zoom. Em cima, eles sao a
		// primeira coisa que se ve.
		var barra = new HBoxContainer();
		foreach ((string rotulo, Action acao, string dica) in new (string, Action, string)[]
		{
			("+", () => _mapa.Zoom(1.6f), "aproximar (roda do mouse pra cima)"),
			("-", () => _mapa.Zoom(1f / 1.6f), "afastar (roda do mouse pra baixo)"),
			("centralizar em mim", () => _mapa.VerMim(), "poe voce no meio, num zoom de vizinhanca"),
			("ver tudo", () => _mapa.VerTudo(), "enquadra os mundos com mapa proprio"),
		})
		{
			var b = new Button { Text = rotulo, TooltipText = dica };
			Action fazer = acao;
			b.Pressed += () => fazer();
			barra.AddChild(b);
		}
		_conteudo.AddChild(barra);
		_conteudo.AddChild(_mapa);

		// O PAINEL DO DESTINO fica FORA da remontagem da pagina: ele muda a cada clique no mapa, e
		// remontar a aba inteira a cada clique jogaria fora o zoom e o arrasto que o jogador acabou
		// de ajustar. Por isso o mapa avisa por evento e so este pedaco se refaz.
		var painel = new VBoxContainer();
		_conteudo.AddChild(painel);
		_mapa.SelecaoMudou += () => DesenharDestino(painel, noEspaco);
		_mapa.PediuViagem += p => Viajar(p, noEspaco);
		DesenharDestino(painel, noEspaco);

		Aviso("\nClique num planeta pra selecionar, duplo clique pra viajar. Arraste pra mover o mapa, "
			+ "roda pra zoom. O laranja tem superficie pra pousar; o azul-acinzentado e mundo gerado -- "
			+ "os menores so aparecem quando voce aproxima.");
		Aviso("A viagem leva o tempo que diz: o piloto anda no passo normal, nao teleporta. "
			+ "Terra a Namek sao 7 dias in-game, como no anime.");
	}

	/// <summary>O mapa da aba Nav. Guardado pra os botoes da barra alcancarem a camera dele.</summary>
	private MapaEstelar _mapa = null!;

	/// <summary>O mapa vivo, ou nulo se a aba Nav ainda nao foi montada. SO PRA BANCADA (`--diagnav`).</summary>
	public MapaEstelar? MapaDeTeste => IsInstanceValid(_mapa) ? _mapa : null;

	/// <summary>
	/// O QUE ESTA SELECIONADO: nome, distancia, tempo de viagem, e o botao que liga o piloto.
	///
	/// Refeito sozinho a cada clique no mapa -- e so ele, nao a aba.
	/// </summary>
	private void DesenharDestino(VBoxContainer painel, bool noEspaco)
	{
		foreach (Node n in painel.GetChildren()) { painel.RemoveChild(n); n.QueueFree(); }

		if (_mapa.Selecionado is not { } p)
		{
			var vazio = new Label
			{
				Text = _mapa.VendoProcedurais
					? "nenhum destino selecionado."
					: "nenhum destino selecionado. Aproxime pra os mundos gerados aparecerem.",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
			};
			vazio.AddThemeColorOverride("font_color", Tema.TextoFraco);
			vazio.AddThemeFontSizeOverride("font_size", 12);
			painel.AddChild(vazio);
			return;
		}

		// A POSICAO DE GALAXIA, e nao a do corpo -- ver `MapaEstelar.MinhaPosicaoNaGalaxia`.
		// Pousado, a coordenada do corpo e de superficie e o "X dias daqui" sairia de um ponto
		// que nao existe no mapa.
		Vector2? eu = MapaEstelar.MinhaPosicaoNaGalaxia();
		var titulo = new Label { Text = p.Nome };
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		painel.AddChild(titulo);

		var info = new Label
		{
			Text = FichaDoPlaneta(p)
				 + (eu is { } meu
					? $"   \u00b7   {Jandirus.Core.World.Espaco.DiasInGame((new Vector2(p.Pos.X, p.Pos.Y) - meu).Length()):0.0} dias in-game"
					+ $" ({Jandirus.Core.World.Espaco.SegundosDeViagem((new Vector2(p.Pos.X, p.Pos.Y) - meu).Length()) / 60:0} min reais)"
					: ""),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		info.AddThemeColorOverride("font_color", Tema.TextoFraco);
		info.AddThemeFontSizeOverride("font_size", 12);
		painel.AddChild(info);

		var linha = new HBoxContainer();
		var viajar = new Button
		{
			Text = $"Viajar para {p.Nome}",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			// SO NO ESPACO. Viajar e voar entre mundos: em terra firme o piloto automatico andaria
			// contra a parede do planeta. Continua APARECENDO, apagado, porque saber que o destino
			// existe e que falta decolar e informacao -- sumir com o botao seria esconder o jogo.
			Disabled = !noEspaco,
			TooltipText = noEspaco
				? "liga o piloto automatico. Qualquer tecla de movimento desliga."
				: "so no espaco. Use 'Decolar' (aba Other) pra subir.",
		};
		Jandirus.Core.World.PlanetaNoEspaco alvo = p;
		viajar.Pressed += () => Viajar(alvo, noEspaco);
		linha.AddChild(viajar);

		if (World.Instancia?.DestinoDoPiloto != null)
		{
			var parar = new Button { Text = "Parar", TooltipText = "desliga o piloto automatico" };
			parar.Pressed += () =>
			{
				World.Instancia?.SoltarPiloto();
				Chat.Sistema("piloto automatico desligado.");
			};
			linha.AddChild(parar);
		}
		painel.AddChild(linha);
	}

	/// <summary>
	/// O QUE SE SABE DO MUNDO ANTES DE IR: superficie, bioma e GRAVIDADE.
	///
	/// ============================ A GRAVIDADE NAO E ENFEITE ============================
	/// O `Chronology.dm` e explicito: "O Nav System mostra a gravidade dos planetas antes de
	/// pousar" -- e o motivo e que acima da sua maestria de gravidade o planeta te ESMAGA (anda
	/// devagar, o corpo todo toma dano, muito acima desmaia, absurdamente acima explode). Um
	/// planeta gerado pode ter ate 80x.
	///
	/// Sem este numero na tela, escolher destino no mapa e apostar. Com ele, a carta estelar faz o
	/// que uma carta faz: avisa onde nao se atracar.
	/// ==================================================================================
	///
	/// Os dois lados saem de funcao PURA -- a tabela dos pre-feitos (`planetas.json`, o mesmo
	/// arquivo que o servidor le) e a seed do gerado. Nenhum pacote de rede.
	/// </summary>
	private static string FichaDoPlaneta(Jandirus.Core.World.PlanetaNoEspaco p)
	{
		var zona = p.Premade
			? Jandirus.Core.World.ZoneKey.Premade(p.Nome)
			: Jandirus.Core.World.ZoneKey.Procedural(p.Nome, p.Seed);

		// O CEU DE LA, DAQUI. A hora de cada planeta e funcao pura do relogio do mundo mais a
		// ficha dele, entao a carta consegue dizer "em Namek e dia" sem pedir nada ao servidor --
		// e saber que o destino esta em plena noite de lua cheia e informacao de viagem.
		Jandirus.Core.World.RelogioDoPlaneta r = Planetas.Relogio(zona);
		string ceu = Ceu(r);

		if (!p.Premade)
		{
			Jandirus.Core.World.MundoProcedural m = Jandirus.Core.World.MundoProcedural.DaSeed(p.Seed, p.Nome);
			return $"mundo gerado · {m.Bioma} · gravidade {m.Gravidade:0.##}x · {ceu}";
		}
		double g = Planetas.Catalogo?.De(p.Nome).Gravidade ?? 1;
		return $"mundo com superficie -- da pra pousar · gravidade {g:0.##}x · {ceu}";
	}

	/// <summary>Que horas sao naquele mundo agora, e em que fase a lua dele esta.</summary>
	private static string Ceu(Jandirus.Core.World.RelogioDoPlaneta r)
	{
		if (GameClient.Instance is not { TempoChegou: true } cli)
			return Jandirus.Core.World.Ceu.NomeDoCiclo(r);

		Jandirus.Core.World.EstadoDoCeu e = Jandirus.Core.World.Ceu.De(r, cli.TempoDoMundo);
		string hora = Jandirus.Core.World.Ceu.NomeDaHora(e.Hora);
		return e.LuaNoCeu ? $"{hora}, {Jandirus.Core.World.Ceu.NomeDaFase(e.Fase)}" : hora;
	}

	private void Viajar(Jandirus.Core.World.PlanetaNoEspaco p, bool noEspaco)
	{
		if (!noEspaco) { Chat.Sistema("voce precisa estar no espaco pra viajar."); return; }
		World.Instancia?.Pilotar(new Vector2(p.Pos.X, p.Pos.Y));
		Chat.Sistema($"rumo a {p.Nome}. Qualquer tecla de movimento desliga o piloto.");
		Fechar();
	}

	/// <summary>Distancia em TEMPO, que e como o anime mede: dias in-game e minutos reais.</summary>
	private static string Tempo(double dias, double min) =>
		dias >= 1 ? $"{dias:0.0} dias in-game ({min:0} min reais)"
		: $"{dias * 24:0.0} h in-game ({min:0.0} min reais)";

	// =====================================================================
	// CARGOS -- quem manda no mundo
	// =====================================================================
	/// <summary>
	/// A lista de cargos do MUNDO, com quem ocupa cada um e o que falta pra voce.
	///
	/// Mostra os OCUPADOS tambem: saber quem e o Guardiao da Terra e metade do valor de um
	/// sistema de cargos -- a outra metade e poder disputar quando vagar.
	/// </summary>
	private void AbaCargos()
	{
		if (GameClient.Instance is not { } cli) return;

		if (cli.Cargos.Count == 0)
		{
			Aviso("pedindo a lista ao servidor...");
			cli.SendCargo();   // a lista chega e o painel se redesenha sozinho
			return;
		}

		Secao("Cargos do mundo");
		foreach (GameClient.CargoInfo c in cli.Cargos)
		{
			bool vago = c.Dono.Length == 0;
			bool apto = c.Falta.Length == 0;

			if (!vago) { Linha(NomeDoCargo(c.Chave), c.Dono, Tema.Texto); continue; }

			var b = new Button
			{
				Text = $"{NomeDoCargo(c.Chave)}   ·   VAGO" + (apto ? "   ·   reivindicar" : ""),
				Alignment = HorizontalAlignment.Left,
				Disabled = !apto,
				TooltipText = apto ? "voce cumpre os requisitos" : c.Falta,
			};
			string chave = c.Chave;
			b.Pressed += () => cli.SendCargo(chave);
			_conteudo.AddChild(b);
			if (!apto) Aviso("      exige: " + c.Falta);
		}

		Aviso("\nUm cargo tem UM dono no mundo, e uma alma carrega UM cargo. "
			+ "A escada dos Kaios e a excecao: subir larga o cargo anterior.");
	}

	private static string NomeDoCargo(string chave) =>
		Jandirus.Core.Ranks.Cargos.Get(chave)?.Nome ?? chave;

	// =====================================================================
	// FORMS -- a escada de transformacao
	// =====================================================================
	/// <summary>
	/// O QUE VOCE TEM: a forma de agora e as que ja despertaram. Mais nada.
	///
	/// ============================ A ESCADA FOI EMBORA ============================
	/// Esta aba listava TODOS os degraus, inclusive os travados, com uma faixa de distancia
	/// ("muito longe", "perto", "quase la", "no limiar"). O dono mandou tirar, e as duas razoes
	/// batem com o corte de sigilo que ele pediu na mensagem anterior:
	///
	///   1. A FAIXA ERA O BP DE VOLTA. Ela nascia de `BP / PortaBp`. Cinco faixas contra uma
	///      escada de degraus conhecidos deixam qualquer um binarizar o proprio poder em poucas
	///      sessoes de treino -- e o jogo inteiro acabou de ser arrumado pra que BP so vire
	///      numero com scouter. Esconder o digito e publicar a razao dele e esconder pela metade.
	///   2. LISTA DE DEGRAUS FUTUROS E TABELA DE PROGRESSAO. Saber de antemao que existem sete
	///      degraus acima transforma despertar -- que no anime e um acontecimento -- em barra de
	///      carregamento. O que o personagem sabe das proprias formas e o que ele ja viveu.
	/// =============================================================================
	///
	/// Quem quiser subir aperta C: a tentativa falhando E a informacao, como no original.
	/// </summary>
	private void AbaFormas()
	{
		Jandirus.Core.Forms.FormaDef? defAtual = Jandirus.Core.Forms.Catalogo.PorRede(_atributos.FormaAtual);
		if (defAtual is { Id: "base" }) defAtual = null;
		string atual = defAtual?.Id ?? Jandirus.Core.Forms.Catalogo.IdBase;

		// O LIVRO UMA VEZ SO, e nao um por linha: o `Livro()` remonta o dicionario inteiro a cada
		// chamada, e esta aba redesenha a cada quadro em que algo muda. Ele serve as duas leituras de
		// nome daqui de baixo.
		Jandirus.Core.Forms.Maestrias livro = Livro();

		Secao("Agora");
		// ============================ O NOME SAI DO CATALOGO E NAO DA ENTRADA ============================
		// `Catalogo.NomeDe` e nao `defAtual.Nome`: o Super Saiyajin a 100% de maestria se chama
		// "Super Saiyajin Grade 4" e continua sendo a MESMA forma (ver `Catalogo.DominouOSuperSaiyajin`).
		// Aqui o cliente tem o livro de maestrias do proprio dono da ficha, entao a pergunta se responde
		// sem pedir nada ao servidor.
		// ============================================================================================
		Linha("Forma", defAtual != null ? Jandirus.Core.Forms.Catalogo.NomeDe(defAtual, livro) : "normal",
			  defAtual != null ? Tema.Destaque : Tema.Texto);
		if (defAtual != null)
		{
			// A FORMA DE DISCIPLINA MOSTRA A PROFICIENCIA DA SKILL, e diz o nome dela. Sem isto esta
			// linha marcaria 0,0% pra sempre em quem esta vivendo dentro do Ultra Instinto -- ver
			// `ProficienciaDaForma`.
			if (ProficienciaDaForma(atual) is { } prof)
				Linha($"Proficiência em {prof.Nome}", $"{prof.Pct:0.#}%");
			else
				Linha("Maestria desta forma", $"{Maestria(atual):0.#}%");
			Linha("Dreno de Ki", $"{Jandirus.Core.Forms.Catalogo.DrenoPorSegundo(atual, Livro()) * 100:0.##}% do Ki por segundo");
		}
		Aviso("\nSegure C pra reunir energia  ·  toque C duas vezes pra tentar subir  ·  X volta ao normal.\n"
			+ "Maestria SÓ cresce dentro da forma, gastando Ki -- é o único eixo do jogo que não se compra.\n"
			+ "As formas de uma disciplina divina são exceção: elas não têm maestria própria -- usá-las "
			+ "sobe a proficiência da SKILL, e essa só cresce LUTANDO.");

		// SO O QUE JA DESPERTOU. Maestria > 0 quer dizer que este corpo ja esteve nessa forma
		// alguma vez -- e o unico registro honesto de "eu sei fazer isto".
		//
		// NAS FORMAS DE DISCIPLINA O REGISTRO HONESTO E OUTRO: elas nao guardam maestria nenhuma, e
		// o que prova que o corpo as conhece e a FAIXA de proficiencia que as concedeu ter sido
		// cruzada (`Degrau.Pct`). Sem esta metade elas sumiriam da aba no instante em que o jogador
		// voltasse pra base -- a regra existiria e ninguem veria.
		var minhas = new List<Jandirus.Core.Forms.FormaDef>();
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
			if (d.Id != Jandirus.Core.Forms.Catalogo.IdBase
				&& (d.Id == atual || Maestria(d.Id) > 0 || DespertouPelaDisciplina(d.Id)))
				minhas.Add(d);

		Secao("Formas que você desperta");

		if (minhas.Count == 0)
		{
			Aviso("Nenhuma, ainda. Nada garante que exista alguma -- e se existir, ela não vem por "
				+ "treino marcado: vem na hora em que vier.");
			return;
		}

		foreach (Jandirus.Core.Forms.FormaDef d in minhas)
		{
			// A MESMA TROCA DA LINHA DE CIMA: forma de disciplina relata a proficiencia da SKILL, e
			// diz que e da skill -- "maestria 0,0%" ao lado de uma forma que o jogador acabou de usar
			// leria como progresso perdido.
			string ficha = ProficienciaDaForma(d.Id) is { } p
				? $"proficiência em {p.Nome} {p.Pct:0.#}%"
				: $"maestria {Maestria(d.Id):0.#}%";

			// MESMO FUNIL DA LINHA "Forma" LA DE CIMA. Sao os dois lugares desta tela que escrevem o
			// nome de uma forma, e escrever `d.Nome` num e `NomeDe` no outro faria a aba dizer
			// "Grade 4" em cima e "Super Saiyajin" tres linhas abaixo, sobre a mesma forma.
			Linha(Jandirus.Core.Forms.Catalogo.NomeDe(d, livro),
				d.Id == atual ? $"EM USO  ·  {ficha}" : ficha,
				d.Id == atual ? Tema.Destaque : Tema.Bom);
		}

		Aviso("\nO que vem depois -- se vier -- você descobre tentando.");
	}

	/// <summary>
	/// A PROFICIENCIA QUE ESTA FORMA RELATA NO LUGAR DA MAESTRIA. Nulo = ela tem maestria propria.
	///
	/// Quem responde "esta forma e de uma disciplina?" e o <see cref="Jandirus.Core.Forms.Disciplinas.DaForma"/>,
	/// o mesmo funil que o servidor usa -- ver o cabecalho dele. Aqui so falta cruzar com a disciplina
	/// que ESTE corpo trilhou, que chega em byte na ficha lenta; os dois caminhos se excluem, entao
	/// uma forma da outra escola nunca casa e mostra 0 (que e a verdade: ela nao e alcancavel).
	/// </summary>
	private (string Nome, double Pct)? ProficienciaDaForma(string forma)
	{
		if (Jandirus.Core.Forms.Disciplinas.DaForma(forma) is not { } par) return null;
		bool minha = _atributos.Disciplina == Jandirus.Core.Forms.Disciplinas.Rede(par.Def.Tipo);
		return (par.Def.Nome, minha ? _atributos.DiscReal : 0);
	}

	/// <summary>
	/// ESTE CORPO JA DESPERTOU ESTA FORMA DE DISCIPLINA? A faixa que a concede foi cruzada.
	///
	/// E o substituto do "maestria > 0" pras quatro formas divinas: elas nao acumulam maestria, entao
	/// o registro de "eu sei fazer isto" e a proficiencia REAL ter passado do <see cref="Jandirus.Core.Forms.Degrau.Pct"/>
	/// da faixa que anuncia a forma (20% pro Sign/Destroyer, 60% pro Perfected/Ultra Ego).
	/// </summary>
	private bool DespertouPelaDisciplina(string forma) =>
		Jandirus.Core.Forms.Disciplinas.DaForma(forma) is { } par
		&& _atributos.Disciplina == Jandirus.Core.Forms.Disciplinas.Rede(par.Def.Tipo)
		&& _atributos.DiscReal >= par.Faixa.Pct;

	private double Maestria(string forma)
	{
		ushort alvo = Jandirus.Core.Forms.Catalogo.Rede(forma);
		foreach ((ushort id, float pct) in _atributos.Maestrias ?? [])
			if (id == alvo) return pct;
		return 0;
	}

	/// <summary>As maestrias num formato que o Core entende, pra calcular dreno e multiplicador.</summary>
	private Jandirus.Core.Forms.Maestrias Livro()
	{
		var m = new Jandirus.Core.Forms.Maestrias();
		foreach ((ushort id, float pct) in _atributos.Maestrias ?? [])
			if (Jandirus.Core.Forms.Catalogo.PorRede(id) is { } d) m.Por(d.Id, pct);
		return m;
	}

	// =====================================================================
	// SKILLS
	// =====================================================================
	/// <summary>O mesmo catalogo, pra quem mais precisar dele no cliente (o robo de teste).</summary>
	public static SkillCatalog? CatalogoPublico() => Catalogo();

	private static SkillCatalog? Catalogo()
	{
		if (_catalogo != null) return _catalogo;
		const string a = "res://Assets/Data/skills.json", b = "res://Assets/Data/skilltrees.json";
		if (!Godot.FileAccess.FileExists(a) || !Godot.FileAccess.FileExists(b)) return null;
		_catalogo = SkillCatalog.Parse(Godot.FileAccess.GetFileAsString(a), Godot.FileAccess.GetFileAsString(b));
		return _catalogo;
	}

	/// <summary>Copia pro livro local o que o servidor mandou. O servidor e a verdade.</summary>
	private void SincronizarLivro()
	{
		if (GameClient.Instance is not { } cli) return;
		_livro.Carregar(cli.SkillsAprendidas);
		_livro.MarcosTotais = cli.MarcosTotais;
		_livro.MarcosLivres = cli.MarcosLivres;
	}

	/// <summary>
	/// O QUE EU JA SEI, agrupado pela arvore de onde veio.
	///
	/// A arvore e o endereco da habilidade na cabeca de quem joga: ninguem lembra "eu comprei
	/// Backstab", lembra "eu fui pro lado do assassino". Uma lista alfabetica de trinta nomes
	/// perde justamente isso -- e era o que esta aba fazia, apesar de o comentario dela prometer
	/// agrupamento desde sempre.
	///
	/// A ULTIMA SECAO E A INTERESSANTE: o que NAO pende de nenhuma arvore sua so pode ter chegado
	/// por ensino. E a mesma leitura que o Core faz em <see cref="SkillBook.PenduraEmArvoreDe"/>
	/// ("skill solta e ensinada, nao comprada"): Kaio-ken e Genkidama vem do Senhor Kaioh, nao de
	/// um balcao.
	/// </summary>
	/// <summary>
	/// A FICHA DA SKILL, com Comprar e Cancelar.
	///
	/// POR QUE UM PASSO A MAIS: a compra e IRREVERSIVEL -- marco gasto nao volta -- e no balcao so
	/// cabem nome e preco. Sem esta tela o jogador clica num nome que nao conhece e descobre o que
	/// comprou depois de pago. A dica de mouse nao resolvia: ninguem passa o mouse antes de
	/// clicar, e em tela sensivel ao toque ela nem existe.
	///
	/// E ELA DIZ O QUE A SKILL FAZ, nao so o que ela e. O texto do DM descreve a fantasia ("a arte
	/// da assassinacao deixa sua marca"); os EFEITOS extraidos dizem o numero. Os dois juntos sao
	/// a unica resposta honesta pra "vale a pena?".
	/// </summary>
	private void AbrirFichaDaSkill(Skill s, int custo)
	{
		if (_fichaAberta != null && IsInstanceValid(_fichaAberta)) _fichaAberta.QueueFree();

		var janela = new AcceptDialog
		{
			Title = s.Nome,
			MinSize = new Vector2I(440, 0),
			OkButtonText = $"Comprar  ·  {custo} marco{(custo > 1 ? "s" : "")}",
		};
		janela.AddCancelButton("Cancelar");

		var caixa = new VBoxContainer();
		janela.AddChild(caixa);

		if (s.Desc.Length > 0)
			caixa.AddChild(new Label
			{
				Text = s.Desc,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				CustomMinimumSize = new Vector2(420, 0),
			});

		var efeitos = EfeitosEmTexto(s).ToList();
		if (efeitos.Count > 0)
		{
			caixa.AddChild(new HSeparator());
			foreach (string linha in efeitos)
			{
				var l = new Label { Text = "• " + linha };
				l.AddThemeColorOverride("font_color", Tema.Destaque);
				caixa.AddChild(l);
			}
		}
		else
		{
			// HONESTIDADE NO BALCAO: 68 folhas ainda nao tem efeito portado. Vender em silencio
			// seria cobrar por nada sem dizer.
			var l = new Label { Text = "O efeito mecânico desta habilidade ainda não foi portado." };
			l.AddThemeColorOverride("font_color", Tema.TextoFraco);
			caixa.AddChild(l);
		}

		string caminho = s.Path;
		janela.Confirmed += () => { GameClient.Instance?.SendAprender(caminho); _fichaAberta = null; };
		janela.Canceled += () => _fichaAberta = null;

		AddChild(janela);
		_fichaAberta = janela;
		janela.PopupCentered();
	}

	private AcceptDialog? _fichaAberta;

	/// <summary>
	/// O QUE A SKILL FAZ, em portugues. Sai dos efeitos EXTRAIDOS do DM -- por isso a lista fica
	/// vazia justamente nas que ainda nao tem efeito portado, o que e a verdade e nao um descuido.
	/// </summary>
	private static IEnumerable<string> EfeitosEmTexto(Skill s)
	{
		foreach ((string campo, double v) in s.Buffs)
			yield return $"{Jandirus.Core.Skills.NomesLegiveis.Campo(campo)} {v:+0.##;-0.##}";
		foreach ((string campo, double v) in s.Mults)
			yield return $"{Jandirus.Core.Skills.NomesLegiveis.Campo(campo)} x{v:0.##}";
		foreach (string verbo in s.Verbos)
		{
			var t = Jandirus.Core.Skills.Tecnicas.Get(verbo);
			yield return t is { Modo: not Jandirus.Core.Skills.Modo.NaoPortada }
				? $"habilidade nova: {t.Nome}"
				: $"habilidade nova: {Jandirus.Core.Skills.NomesLegiveis.Habilidade(verbo)} (efeito ainda não portado)";
		}
		if (s.Estilo.Length > 0) yield return $"estilo de luta: {s.Estilo}";
	}

	private void Sabidas()
	{
		SkillCatalog? cat = Catalogo();
		if (cat == null) { Aviso("catálogo de skills não encontrado (rode o AssetPipeline: comando 'skills')."); return; }

		if (_livro.Aprendidas.Count == 0)
		{
			Secao("Aprendidas (0)");
			Aviso("Você ainda não aprendeu nada. Abra a aba Learning.");
		}
		else
		{
			string raca = _atributos.Raca ?? "";
			string classe = GameClient.Instance?.Sheet.Class ?? "";

			// Vai esvaziando conforme cada arvore reclama as suas. O que sobrar no fim veio de fora.
			var sobrou = new HashSet<string>(_livro.Aprendidas, StringComparer.OrdinalIgnoreCase);

			foreach (Skill arv in ArvoresDoPersonagem(cat, raca, classe))
			{
				var minhas = arv.Galhos.Where(sobrou.Contains).ToList();
				if (minhas.Count == 0) continue;

				Secao($"{arv.Nome}  ({minhas.Count})");
				foreach (string p in minhas.OrderBy(x => cat.Get(x)?.Tier ?? 0))
				{
					sobrou.Remove(p);
					Skill? s = cat.Get(p);
					Linha(s?.Nome ?? p, s?.Tipo ?? "", Tema.Bom);
				}
			}

			if (sobrou.Count > 0)
			{
				Secao($"Avulsas  ({sobrou.Count})");
				foreach (string p in sobrou.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
				{
					Skill? s = cat.Get(p);
					Linha(s?.Nome ?? p, s?.Tipo ?? "", Tema.Destaque);
				}
				Aviso("      Não pendem de nenhuma árvore que este painel liste: vieram de um mestre, "
					+ "ou de um caminho que só se abre jogando.");
			}
		}

		// os verbs registrados por skills que ja tem EFEITO implementado
		var acoes = Verbos.Da(Verbos.Skills).ToList();
		if (acoes.Count == 0) return;
		Secao("Ações");
		foreach (Verbo v in acoes) Botao(v);
	}

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
	/// fora exatamente a informacao que organiza a escolha, e ainda enterra as trinta skills que
	/// interessam debaixo de trezentas que nao.
	///
	/// A BUSCA CONTINUA VENCENDO A ABA (ver o comentario da classe): quem sabe o nome nao precisa
	/// saber a arvore.
	/// </summary>
	private void Aprendizado()
	{
		SkillCatalog? cat = Catalogo();
		if (cat == null) { Aviso("catálogo de skills não encontrado (rode o AssetPipeline: comando 'skills')."); return; }
		if (GameClient.Instance is not { } cli) return;

		string raca = _atributos.Raca ?? "";
		string classe = cli.Sheet.Class ?? "";

		Secao($"Marcos: {_livro.MarcosLivres} livres de {_livro.MarcosTotais}");

		List<Skill> arvores = ArvoresDoPersonagem(cat, raca, classe);

		// A ARVORE ABERTA PODE TER SUMIDO no meio do caminho (a ficha lenta trouxe outra raca, um
		// cargo caiu). Cair de volta na lista e melhor que uma pagina vazia sem explicacao -- e o
		// mesmo cuidado que o `Redesenhar()` toma com a aba Scan quando o scouter sai.
		Skill? aberta = _arvoreAberta.Length > 0 ? cat.Get(_arvoreAberta) : null;
		if (aberta != null && !arvores.Contains(aberta)) aberta = null;

		if (aberta == null) { _arvoreAberta = ""; ListaDeArvores(cat, arvores, raca, classe); }
		else SkillsDaArvore(cat, cli, aberta, raca, classe);
	}

	/// <summary>Volta pro primeiro nivel. O `backbutton()` do original (SkillTreesWindow.dm:111).</summary>
	private void FecharArvore()
	{
		_arvoreAberta = "";
		_verTrancadas = false;
	}

	/// <summary>
	/// AS ARVORES QUE ESTE PERSONAGEM TEM: as da raca e da classe (`generatetrees`) mais as que o
	/// PROGRESSO abriu (`enabletree`). E a mesma uniao de <see cref="SkillBook.Ofertas"/>.
	///
	/// POR QUE NAO CHAMO Ofertas() DIRETO, ja que ela existe: ela DEDUPLICA entre arvores e devolve
	/// a lista achatada. Uma navegacao em dois niveis precisa justamente do que ela descarta -- de
	/// QUE arvore cada skill veio -- e uma skill pendurada em duas arvores tem que aparecer nas
	/// duas, senao ela some da segunda sem motivo visivel. As regras de recusa, essas sim, saem
	/// inteiras do Core (<see cref="SkillBook.PodeAprender"/>): nao ha uma segunda copia aqui.
	/// </summary>
	private List<Skill> ArvoresDoPersonagem(SkillCatalog cat, string raca, string classe)
	{
		List<Skill> l = cat.ArvoresDe(raca, classe);
		foreach (string p in _livro.Destravadas)
			if (cat.Get(p) is { } a && !l.Contains(a)) l.Add(a);
		return l;
	}

	/// <summary>
	/// A RECUSA DO CORE, com um unico ajuste -- e uma porta so pras duas telas do balcao, pra que
	/// a contagem da lista de arvores nunca discorde do botao que ela promete.
	///
	/// O AJUSTE: a classe nao chega mais ao cliente, e nao deve mesmo chegar -- o sigilo zera o
	/// campo em TODA ficha, com scouter ou sem (GameServer.Sigilo.cs:105). So que a classe e um
	/// dos gates de skill do DM (`compatible_classes`, skill.dm:13), entao daqui pra frente o
	/// cliente passa a errar pra MENOS: uma skill que a classe permite volta como RacaOuClasse.
	///
	/// Errar pra menos e o pior dos dois erros. Um botao apagado por engano esconde conteudo PRA
	/// SEMPRE, e o jogador nao tem como desconfiar que a recusa era do cliente; errar pra mais
	/// custa uma frase de recusa vinda do servidor, que e onde a decisao sempre morou. Entao:
	/// quando a skill pede CLASSE e eu nao sei a minha, eu nao decido -- deixo passar e quem sabe
	/// responde.
	/// </summary>
	private Recusa Estado(SkillCatalog cat, Skill s, string raca, string classe)
	{
		Recusa r = _livro.PodeAprender(cat, s.Path, raca, classe, vilao: false);
		if (r == Recusa.RacaOuClasse && classe.Length == 0 && s.Classes.Length > 0) return Recusa.Pode;
		return r;
	}

	/// <summary>
	/// NIVEL 1: as arvores, com quanto de cada uma ja e seu e quanto da pra comprar agora.
	///
	/// O CONTADOR "pra aprender agora" e o que faz a lista valer: sem ele, escolher arvore vira
	/// tentativa e erro em quatorze cards. Com ele, a aba responde de relance a unica pergunta que
	/// alguem com marcos na mao tem -- onde e que eu gasto isto.
	/// </summary>
	private void ListaDeArvores(SkillCatalog cat, List<Skill> arvores, string raca, string classe)
	{
		Secao("Suas árvores");

		bool alguma = false;
		foreach (Skill arv in arvores)
		{
			int total = 0, sabidas = 0, agora = 0, trancadas = 0;
			foreach (string p in arv.Galhos)
			{
				Skill? s = cat.Get(p);
				if (s == null || s.Nome.Length == 0 || s.Arvore) continue;
				total++;
				if (_livro.Sabe(p)) { sabidas++; continue; }
				if (!s.Ligada) { trancadas++; continue; }
				if (Estado(cat, s, raca, classe) == Recusa.Pode) agora++;
			}

			// "Tree Mastery" nasce sem galho nenhum no DM e continua assim. Card vazio so ocupa
			// espaco e faz o jogador clicar duas vezes pra descobrir que nao tem nada.
			if (total == 0) continue;
			alguma = true;

			var b = new Button
			{
				Text = $"{arv.Nome}   ·   {sabidas}/{total} suas"
					 + (agora > 0 ? $"   ·   {agora} pra aprender agora" : ""),
				// AS TRANCADAS FICAM NO TOOLTIP, nao numa linha embaixo do card: uma linha por
				// arvore devolveria pro indice exatamente o ruido que esta reforma tirou da lista.
				// No indice a pergunta e "onde eu gasto marco"; o resto e assunto de dentro.
				TooltipText = (arv.Desc.Length > 0 ? arv.Desc : arv.Path)
							+ (trancadas > 0 ? $"\n\n+ {trancadas} que não estão à venda -- entre pra ver por quê" : ""),
				Alignment = HorizontalAlignment.Left,
			};
			if (agora > 0) b.AddThemeColorOverride("font_color", Tema.Bom);
			string caminho = arv.Path;
			b.Pressed += () => { _arvoreAberta = caminho; _verTrancadas = false; Redesenhar(); };
			_conteudo.AddChild(b);
		}

		if (!alguma)
			Aviso("Nenhuma árvore ainda. Elas vêm da raça, da classe e do que você treina.");
	}

	/// <summary>
	/// NIVEL 2: as skills DESTA arvore, na escada de tiers do original.
	///
	/// A ORDEM E POR TIER porque a arvore e uma escada: o tier 1 e o tronco e o tier 5 e a ponta.
	/// O original desenhava uma grade por tier (`SkillListTier[N]Grid`, SkillTreesWindow.dm:184-198)
	/// pelo mesmo motivo; a unica diferenca e que ele listava de cima pra baixo (tier 6 primeiro,
	/// HtmlUI.dm:827) porque era grade de janela, e aqui a leitura e de rolagem -- comecar pela
	/// base e ler na ordem em que se compra.
	///
	/// AS DESATIVADAS SAIRAM DO BALCAO. Ver <see cref="Trancadas"/>.
	/// </summary>
	private void SkillsDaArvore(SkillCatalog cat, GameClient cli, Skill arv, string raca, string classe)
	{
		// VOLTAR SEMPRE VISIVEL, no topo, antes de qualquer coisa que role pra fora da tela. ESC
		// faz o mesmo (ver _Input), mas quem entrou clicando espera sair clicando.
		var voltar = new Button { Text = "‹  todas as árvores", Alignment = HorizontalAlignment.Left };
		voltar.Pressed += () => { FecharArvore(); Redesenhar(); };
		_conteudo.AddChild(voltar);

		Secao(arv.Nome);
		if (arv.Desc.Length > 0) Aviso(arv.Desc);

		var balcao = new List<Skill>();      // da pra comprar (ou faltam marcos/pre-requisito)
		var jaSao = new List<Skill>();       // ja sao suas
		var trancadas = new List<Skill>();   // enabled = 0: nao se compram de jeito nenhum

		foreach (string p in arv.Galhos)
		{
			Skill? s = cat.Get(p);
			if (s == null || s.Nome.Length == 0 || s.Arvore) continue;
			if (_livro.Sabe(p)) jaSao.Add(s);
			else if (!s.Ligada) trancadas.Add(s);
			else balcao.Add(s);
		}

		// ---- o balcao, por tier ----
		if (balcao.Count == 0) Aviso("Nada à venda nesta árvore agora.");

		int tier = int.MinValue;
		foreach (Skill s in balcao.OrderBy(x => x.Tier).ThenBy(x => x.Nome, StringComparer.OrdinalIgnoreCase))
		{
			if (s.Tier != tier) { tier = s.Tier; Secao($"Tier {tier}"); }

			Recusa r = Estado(cat, s, raca, classe);
			int custo = SkillCatalog.CustoDe(s);

			var b = new Button
			{
				Text = $"{s.Nome}   ·   {custo} marco{(custo > 1 ? "s" : "")}",
				TooltipText = s.Desc.Length > 0 ? s.Desc : s.Path,
				Alignment = HorizontalAlignment.Left,
				Disabled = r != Recusa.Pode,
			};
			Skill escolhida = s;
			// CLICAR ABRE A FICHA, NAO COMPRA. Marco gasto nao volta, e a lista mostra so nome e
			// preco -- comprar no clique faz o jogador pagar por uma coisa que ele ainda nao leu.
			// Um passo a mais aqui vale mais que um desfazer que nao existe.
			b.Pressed += () => AbrirFichaDaSkill(escolhida, custo);
			_conteudo.AddChild(b);

			if (r == Recusa.Pode) continue;
			Aviso("      " + r switch
			{
				Recusa.SemMarcos => $"faltam {custo - _livro.MarcosLivres} marco(s)",
				Recusa.FaltaPreRequisito => "falta pré-requisito: "
					+ string.Join(", ", s.PreReqs.Select(p => cat.Get(p)?.Nome ?? p)),
				Recusa.RacaOuClasse => "sua raça ou classe não aprende esta",
				Recusa.SoVilao => "só pra vilão",
				// as duas abaixo nao deviam chegar aqui (a filtragem acima ja tirou), mas se
				// chegarem eu prefiro uma frase certa a um nome de enum na cara do jogador
				Recusa.Desligada => "não está à venda",
				Recusa.SemArvore => "não pende desta árvore",
				_ => "indisponível",
			});
		}

		// ---- o que ja e seu ----
		if (jaSao.Count > 0)
		{
			Secao($"Já são suas  ({jaSao.Count})");
			foreach (Skill s in jaSao.OrderBy(x => x.Tier).ThenBy(x => x.Nome, StringComparer.OrdinalIgnoreCase))
				Linha($"{s.Nome}  (tier {s.Tier})", "aprendida", Tema.Bom);
		}

		Trancadas(cat, trancadas);
	}

	/// <summary>
	/// AS QUE NAO ESTAO A VENDA -- fora do balcao, numa gaveta fechada que diz por que.
	///
	/// O PROBLEMA: 152 das 317 folhas nascem com `enabled = 0`. Elas estavam na lista como botao
	/// apagado com a legenda "desativada neste servidor" -- que e ruido e ainda por cima mentira,
	/// porque nao ha servidor nenhum desativando nada.
	///
	/// O QUE O ORIGINAL FAZ: some com elas. `enabled == 0` e pulado ANTES de virar card, tanto na
	/// janela antiga (SkillTreesWindow.dm:168) quanto na de HTML (HtmlUI.dm:820). A pessoa nunca
	/// via a skill; ela APARECIA sozinha quando destravava, com um `to_chat` avisando ("You can now
	/// learn [nome]!", trees.dm:175).
	///
	/// POR QUE EU NAO SUMI DE VEZ, ENTAO: porque `enabled = 0` no DM nao quer dizer UMA coisa so.
	/// O proprio comentario do campo diz que ele e mecanismo de pre-requisito, nao de desligar
	/// ("set to 0 and modify with other skills to establish prereqs", skill.dm:26). As 152 se
	/// partem em tres grupos que pedem acoes OPOSTAS de quem joga, e por isso a gaveta separa:
	///
	///   * 35 sao `teacher`. NUNCA acendem sozinhas: so chegam por outra pessoa, porque o `Study()`
	///     do DM pula a checagem inteira quando a skill e de ensino (`canLearnSkill(S) ||
	///     S.teacher == TRUE`, teachable.dm:46). Quem junta marco esperando o balcao abrir espera
	///     pra sempre -- estas pedem que voce ACHE ALGUEM.
	///   * 34 tem pre-requisito e nao sao de ensino. Nascem apagadas e o `testskillprereqs()` as
	///     acende sozinho quando os pre-requisitos entram (trees.dm:29-36). Estas sao o mapa da
	///     arvore: e o "compre isto pra abrir aquilo" que da direcao a compra.
	///   * as 83 restantes abrem POR FORA da arvore -- um cargo, um ritual, outra skill chamando
	///     `enableskill()` (207 chamadas dessas no DM). Estas pedem so que a vida aconteca.
	///
	/// ENTAO: botao nenhum (ninguem compra nada disto, e botao apagado convida clique), mas o nome
	/// e o motivo ficam acessiveis atras de UM clique, fechados por padrao. Aberta, a gaveta e o
	/// mapa; fechada, ela e uma linha. O que nao da e a pessoa nao ter como descobrir que a skill
	/// existe e que o caminho ate ela nao passa por marcos.
	/// </summary>
	private void Trancadas(SkillCatalog cat, List<Skill> trancadas)
	{
		if (trancadas.Count == 0) return;

		var gaveta = new Button
		{
			Text = (_verTrancadas ? "▾  " : "▸  ") + $"{trancadas.Count} não estão à venda nesta árvore",
			Alignment = HorizontalAlignment.Left,
			TooltipText = "estas não se compram com marcos: ou abrem sozinhas, ou vêm de um mestre",
		};
		gaveta.AddThemeColorOverride("font_color", Tema.TextoFraco);
		gaveta.Pressed += () => { _verTrancadas = !_verTrancadas; Redesenhar(); };
		_conteudo.AddChild(gaveta);
		if (!_verTrancadas) return;

		// ENSINO PRIMEIRO porque e o unico grupo que pede uma ACAO do jogador (achar quem saiba).
		// Os outros dois pedem so paciencia, e por isso vem depois.
		var ensino = trancadas.Where(s => s.Ensinavel).ToList();
		var porPreReq = trancadas.Where(s => !s.Ensinavel && s.PreReqs.Length > 0).ToList();
		var deFora = trancadas.Where(s => !s.Ensinavel && s.PreReqs.Length == 0).ToList();

		if (ensino.Count > 0)
		{
			Secao($"Isto é ensinado, não comprado  ({ensino.Count})");
			Aviso("Marco nenhum abre estas. Você precisa de alguém que já as saiba, por perto, "
				+ "disposto a ensinar.");
			foreach (Skill s in ensino.OrderBy(x => x.Tier).ThenBy(x => x.Nome, StringComparer.OrdinalIgnoreCase))
				Linha($"{s.Nome}  (tier {s.Tier})", "só com um mestre", Tema.Destaque);
		}

		if (porPreReq.Count > 0)
		{
			Secao($"Abrem quando você aprender o que vem antes  ({porPreReq.Count})");
			foreach (Skill s in porPreReq.OrderBy(x => x.Tier).ThenBy(x => x.Nome, StringComparer.OrdinalIgnoreCase))
				Linha($"{s.Nome}  (tier {s.Tier})",
					"depois de " + string.Join(", ", s.PreReqs.Select(p => cat.Get(p)?.Nome ?? p)),
					Tema.TextoFraco);
		}

		if (deFora.Count > 0)
		{
			Secao($"Abrem por fora da árvore  ({deFora.Count})");
			Aviso("Um cargo, um ritual ou outra habilidade destrava estas. Elas aparecem no balcão "
				+ "sozinhas no dia em que isso acontecer.");
			foreach (Skill s in deFora.OrderBy(x => x.Tier).ThenBy(x => x.Nome, StringComparer.OrdinalIgnoreCase))
				Linha($"{s.Nome}  (tier {s.Tier})", "trancada", Tema.TextoFraco);
		}
	}

	// =====================================================================
	// VERBS
	// =====================================================================
	private void ListaDeVerbos(string categoria)
	{
		Secao(categoria);
		var lista = Verbos.Da(categoria).ToList();
		if (lista.Count == 0)
		{
			// a mesma frase do original quando a categoria estava vazia
			Aviso("Nenhuma acao aqui ainda.");
			return;
		}
		foreach (Verbo v in lista) Botao(v);
	}

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

	/// <summary>Ja pedi a lista de contas nesta sessao? Ver o comentario em <see cref="AbaAdmin"/>.</summary>
	private bool _pediContas;

	/// <summary>O clima escolhido no painel de admin, e a forca dele. Sobrevivem ao redesenho.</summary>
	// Nasce em `Limpo`: quem abre esta aba quase sempre quer TIRAR o clima (ver o Oozaru, que so
	// aparece com a lua livre), nao por chuva. Por chuva e a excecao, e a excecao pode dar um clique.
	private Jandirus.Core.World.TipoDeClima _climaEscolhido = Jandirus.Core.World.TipoDeClima.Limpo;
	private float _forcaEscolhida = 1f;

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
		Secao("Clima deste planeta");

		Jandirus.Core.World.ClimaDoPlaneta daqui = World.Instancia?.ClimaDoLugar
											   ?? Jandirus.Core.World.ClimaDoPlaneta.Nenhum;
		Jandirus.Core.World.EstadoDoClima agora = World.Instancia?.TempoQueFaz ?? default;

		// SEM A PORCENTAGEM AQUI. Ela muda a cada quadro durante a transicao e esta pagina so se
		// remonta quando o TIPO muda -- o numero ficaria congelado mentindo. Quem quer o valor
		// vivo usa o verb "Weather Report", que imprime no chat na hora em que se pede.
		Linha("Agora", agora.Ativo
			? Jandirus.Core.World.Clima.Nome(agora.Tipo) + (agora.Forcado ? " (forcado)" : " (natural)")
			: "ceu limpo");

		if (!daqui.Existe)
			Aviso("Este lugar nao tem clima proprio (o `HasWeather=0` do DM) -- mas forcar funciona.");

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

		var linha = new HBoxContainer();
		linha.AddChild(escolha);

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
		linha.AddChild(forca);
		linha.AddChild(rotuloForca);

		var bForcar = new Button
		{
			Text = "Forcar",
			TooltipText = "poe este clima nesta zona por 20 min. Vale pra todo mundo que esta aqui.",
		};
		// A FORCA VAI NO MESMO ARGUMENTO, depois de uma barra -- o canal de verb leva UMA string, e
		// e o mesmo formato que `admin_marco` ja usa ("id|quantos").
		bForcar.Pressed += () => cli.SendVerbo("admin_clima",
			$"{_climaEscolhido}|{_forcaEscolhida.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
		linha.AddChild(bForcar);

		var bSoltar = new Button
		{
			Text = "Voltar ao natural",
			Disabled = !agora.Forcado,
			TooltipText = "solta o ceu: o clima volta a ser sorteado pelo relogio do mundo",
		};
		bSoltar.Pressed += () => cli.SendVerbo("admin_clima_natural");
		linha.AddChild(bSoltar);

		// ============================ O CEU DO OOZARU ============================
		// Fica NESTA linha, ao lado do clima, porque as duas coisas sao a mesma pergunta pro
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
		linha.AddChild(bLua);

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
		linha.AddChild(bDia);

		_conteudo.AddChild(linha);

		Aviso(daqui.Existe && daqui.Permitidos.Length > 0
			? "Cai aqui naturalmente: " + string.Join(", ",
				daqui.Permitidos.Select(Jandirus.Core.World.Clima.Nome))
			: "");
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
		Secao("Forcar transformacao");

		// ---------------------------------------------------------- em QUEM cai
		// "alvoId|forma", o mesmo formato do `admin_skill_dar` -- e o `PorNome` do servidor devolve
		// nulo pro id 0, entao SEM alvo marcado o verb cai em quem clicou. Nao ha estado novo aqui:
		// a caixa de "aplicar no alvo" seria um campo a manter sincronizado com uma verdade que o
		// `cli.AlvoId` ja carrega. O rotulo abaixo so LE essa verdade -- e como `AlvoId` esta na
		// assinatura da aba (ver `Assinatura`), marcar alguem remonta a pagina e o rotulo acompanha.
		bool noAlvo = cli.AlvoId != 0;
		Aviso(noAlvo
			? "Cai no CORPO MARCADO (o do duplo clique), e nao em voce."
			: "Cai em VOCE. Marque alguem com duplo clique pra empurrar outro corpo.");

		Jandirus.Core.Forms.FormaDef? defAtual =
			Jandirus.Core.Forms.Catalogo.PorRede(_atributos.FormaAtual);
		// A MARCA SO VALE PRA MIM: `FormaAtual` e a MINHA ficha. Com alvo marcado, o "em uso" seria
		// uma mentira sobre o corpo errado -- entao ela some.
		string atual = noAlvo ? "" : defAtual?.Id ?? Jandirus.Core.Forms.Catalogo.IdBase;

		// ---------------------------------------------------------- a linha de cima
		var topo = new HBoxContainer();

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

		_conteudo.AddChild(topo);

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
			_conteudo.AddChild(faixa);
		}

		Aviso("Ignora BP, maestria, linhagem, classe, degrau anterior, raca e Ki -- as formas que "
			+ "interessa testar SAO as trancadas. O Ki entra cheio (senao o tique derrubaria a forma "
			+ "antes de voce olhar) e a cinematica toca pela regra normal: cheia na estreia deste "
			+ "corpo, encurtada ate 50% de maestria, instantanea depois.\n"
			+ "A fera nao aparece marcada mesmo quando esta ativa: o estado dela e paralelo a escada "
			+ "e nao viaja no campo de forma. Ela precisa de RABO INTEIRO pra durar mais que um tique.");
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
	/// Por isso o painel: um campo de aviso, a lista de contas do servidor com o que cada uma e, e
	/// so entao os verbs.
	/// ==============================================================================================
	/// </summary>
	private void AbaAdmin()
	{
		if (GameClient.Instance is not { } cli) { ListaDeVerbos(Verbos.Admin); return; }

		// ------------------------------------------------- anunciar
		Secao("Aviso ao servidor");
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
		linhaAviso.AddChild(aviso);
		var bAviso = new Button { Text = "Anunciar", TooltipText = "o `Announce` do original: uma linha pra todo mundo" };
		bAviso.Pressed += Anunciar;
		linhaAviso.AddChild(bAviso);
		_conteudo.AddChild(linhaAviso);

		// ------------------------------------------------- clima
		PainelDoClima(cli);

		// ------------------------------------------------- formas e Ki
		// LOGO DEPOIS DO CLIMA de proposito: as tres coisas respondem a mesma pergunta de testador
		// ("por que nao consigo ver isto?"). Lua cheia e ceu limpo destravam o Oozaru; forcar forma
		// destrava o resto; liberar Ki destrava a aura. Juntas, elas sao o kit de olhar o jogo.
		PainelDeFormas(cli);

		// ------------------------------------------------- contas
		Secao("Contas deste servidor");
		if (cli.Contas.Count == 0)
		{
			Aviso(_pediContas ? "sem contas pra mostrar." : "pedindo a lista ao servidor...");
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

				var nome = new Label
				{
					Text = (a.Online ? "● " : "○ ") + a.Conta
						 + (a.Admin ? "   [admin]" : "") + (a.Banida ? "   [banida]" : ""),
					SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
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

				_conteudo.AddChild(h);
				if (a.Personagens.Length > 0) Aviso("      " + a.Personagens);
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
		_conteudo.AddChild(linhaNome);

		var atualizar = new Button { Text = "atualizar lista", Alignment = HorizontalAlignment.Left };
		atualizar.Pressed += () => cli.SendVerbo("admin_contas");
		_conteudo.AddChild(atualizar);

		// ------------------------------------------------- o que precisa de alvo E de texto
		// Sao os quatro verbs do original que abriam um `input()` DEPOIS de escolher o mob:
		// `Give (Skill)`, `Take_Skill`, `Give (Rank)` e `cmd_admin_pm`. O alvo vem do duplo clique;
		// o texto, daqui.
		Secao("Sobre o alvo marcado");
		bool temAlvo = cli.AlvoId != 0;
		Aviso(temAlvo
			? "o alvo esta marcado. Escreva ao lado o nome da skill, a chave do cargo, ou a mensagem."
			: "marque alguem com DUPLO CLIQUE pra usar os botoes abaixo.");

		var noAlvo = new LineEdit
		{
			PlaceholderText = "nome da skill / chave do cargo / mensagem",
			Text = _textoDoAlvo,
			// menos o "<id>|" que vai na frente, com folga pra um id de cinco digitos
			MaxLength = Protocol.MaxArgDeVerbo - 8,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		noAlvo.TextChanged += t => _textoDoAlvo = t;

		var linhaAlvo = new HBoxContainer();
		linhaAlvo.AddChild(noAlvo);
		foreach ((string rotulo, string cmd, string dica) in new[]
		{
			("dar skill", "admin_skill_dar", "o `Give (Skill)` do original: por nome ou typepath"),
			("tirar skill", "admin_skill_tirar", "o `Take_Skill`: desfaz um presente errado"),
			("dar cargo", "admin_cargo_dar", "o `Give (Rank)`: poe no trono ignorando os requisitos"),
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
			linhaAlvo.AddChild(b);
		}
		_conteudo.AddChild(linhaAlvo);

		// ------------------------------------------------- os verbs
		ListaDeVerbos(Verbos.Admin);
		Aviso("\nOs verbs marcados com \"Target\" agem sobre quem voce marcou com DUPLO CLIQUE. "
			+ "A permissao e conferida de novo no servidor -- esconder botao nunca foi permissao.");
	}

	private void Achados(string termo)
	{
		var lista = Verbos.Buscar(termo).ToList();
		Secao($"Busca: \"{termo}\"");
		if (lista.Count == 0) { Aviso("Nenhuma acao com esse nome."); return; }
		foreach (Verbo v in lista) Botao(v, mostrarCategoria: true);
	}

	private void Botao(Verbo v, bool mostrarCategoria = false)
	{
		var b = new Button
		{
			Text = mostrarCategoria ? $"{v.Nome}   [{v.Categoria}]" : v.Nome,
			TooltipText = v.Descricao,
			Alignment = HorizontalAlignment.Left,
			Disabled = !v.PodeAgora,
		};
		b.Pressed += () => { v.Acionar(); Redesenhar(); };
		_conteudo.AddChild(b);
	}

	/// <summary>
	/// A aba existe, o sistema por tras dela ainda nao. Dizer isso e melhor que esconder a aba:
	/// o original tambem tinha painel sem conteudo ("this panel has no detailed view yet"), e
	/// ver a aba e saber que aquilo faz parte do jogo.
	/// </summary>
	private void AindaNao(string aba)
	{
		Secao(aba);
		Aviso(aba switch
		{
			"Equip" => "O que esta vestido e equipado. Vem com o sistema de itens.",
			"Tech" => "Nivel tecnologico, construcoes e androides. Vem com o sistema de tecnologia.",
			"Sense" => "Leitura de Ki: quem esta por perto e quao forte. Vem com a skill de Sense.",
			"Scan" => "Leitura EXATA de poder de luta pelo scouter. Vem com o scouter.",
			"Nav" => "Mapa do espaco e viagem entre planetas. Vem com o nav system.",
			_ => "Ainda nao implementado.",
		});
	}
}
