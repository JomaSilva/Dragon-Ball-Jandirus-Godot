using Godot;
using Jandirus.Core.Skills;
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

	/// <summary>As abas fixas, na ordem do original.</summary>
	private static readonly string[] Fixas =
	[
		"Stats", "Items", "Equip", "Body", "Forms", "Ki",
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

		if (GameClient.Instance is { } cli)
		{
			cli.SheetUpdated += _ => { if (Visible) Redesenhar(); };
			cli.AtributosRecebidos += a =>
			{
				bool trocouRaca = _atributos.Raca != a.Raca;
				_atributos = a;
				// a RACA so chega na ficha lenta, e e ela que diz quais habilidades existem
				if (trocouRaca) Habilidades.Montar(a.Raca ?? "");
				if (Visible) Redesenhar();
			};
			cli.CorpoAtualizado += _ => { if (Visible && _aba == "Body") Redesenhar(); };
			cli.SkillsMudaram += () =>
			{
				SincronizarLivro();
				// APRENDER UMA SKILL PODE CRIAR UM BOTAO. Refaz a lista inteira em vez de
				// acrescentar so o novo: e a mesma funcao do login, entao nao ha um segundo
				// caminho que possa divergir do primeiro.
				Habilidades.Montar(_atributos.Raca ?? "");
				if (Visible) Redesenhar();
			};
			cli.CargosMudaram += () => { if (Visible) Redesenhar(); };
			cli.TechMudou += () => { if (Visible) Redesenhar(); };
			cli.EstilosMudaram += () =>
			{
				Habilidades.Montar(_atributos.Raca ?? "");
				if (Visible) Redesenhar();
			};
			cli.ObrasMudaram += () => { if (Visible && _aba == "Tech") Redesenhar(); };
			_atributos = cli.Atributos;
			SincronizarLivro();
		}
		Verbos.Mudou += () => { if (Visible) Redesenhar(); };
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		Aberto = false;
	}

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
		_conteudo = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_conteudo.AddThemeConstantOverride("separation", 3);
		rolagem.AddChild(_conteudo);

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

		foreach (Node n in _conteudo.GetChildren()) n.QueueFree();

		SheetState f = GameClient.Instance?.Sheet ?? default;
		_titulo.Text = GameClient.Instance?.LocalName ?? "";

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
			case Verbos.Outros:
			case Verbos.Admin: ListaDeVerbos(_aba); break;
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
	private void Gente()
	{
		Secao("Quem esta por perto");
		List<string> nomes = World.Instancia?.NomesVisiveis() ?? [];
		if (nomes.Count == 0) { Aviso("Ninguem no seu campo de visao."); return; }
		foreach (string n in nomes) Linha(n, "");
	}

	private void Mundo()
	{
		Secao("Onde voce esta");
		Linha("Zona", GameClient.Instance?.Zone.Name ?? "");
		if (World.Instancia?.Hora is { } h)
		{
			Linha("Hora", Iluminacao.NomeDaFase(h));
			Linha("Ciclo", $"{h * 24:00.0}h");
		}
		Aviso("\nGravidade, clima e conquista do planeta entram aqui com os sistemas deles.");
	}

	// =====================================================================
	// NAV -- o mapa do espaco
	// =====================================================================
	/// <summary>
	/// O MAPA ESTELAR: quem esta por perto, a que distancia, e quanto tempo leva pra chegar.
	///
	/// O TEMPO E O QUE IMPORTA na tela, nao a distancia. "1.608.035 px" nao diz nada a ninguem;
	/// "7 dias" diz tudo -- e a mesma unidade em que o anime mede a viagem pra Namek. A conta sai
	/// do Core (velocidade de voo x ciclo do dia), entao ela nao pode divergir do que a viagem
	/// vai custar de verdade.
	/// </summary>
	private void AbaNav()
	{
		if (GameClient.Instance is not { } cli) return;

		if (!Jandirus.Core.World.Espaco.EhEspaco(cli.Zone))
		{
			Aviso("o mapa estelar so serve no espaco. Use 'Decolar' (aba Other) pra subir.");
			return;
		}

		Vector2? eu = World.Instancia?.PosicaoLocal;
		if (eu == null) { Aviso("posicao ainda desconhecida."); return; }

		var chunk = Jandirus.Core.World.ChunkId.De(new Jandirus.Core.World.Vec2(eu.Value.X, eu.Value.Y));
		Secao($"Voce esta na chunk {chunk}");

		if (cli.Planetas.Count == 0) { Aviso("nenhum corpo celeste por perto."); return; }

		Secao("Corpos celestes por perto");
		foreach (GameClient.PlanetaInfo p in cli.Planetas.OrderBy(p => (p.Pos - eu.Value).Length()))
		{
			float dist = (p.Pos - eu.Value).Length();
			double dias = Jandirus.Core.World.Espaco.DiasInGame(dist);
			double min = Jandirus.Core.World.Espaco.SegundosDeViagem(dist) / 60;

			var b = new Button
			{
				Text = $"{p.Nome}   ·   {Tempo(dias, min)}"
					 + (p.Premade ? "   ·   da pra pousar" : "   ·   sem superficie ainda"),
				Alignment = HorizontalAlignment.Left,
				TooltipText = $"{dist:N0} px daqui",
			};
			Vector2 destino = p.Pos;
			b.Pressed += () =>
			{
				World.Instancia?.Pilotar(destino);
				Chat.Sistema($"rumo a {p.Nome}. Qualquer tecla de movimento desliga o piloto.");
				Fechar();
			};
			_conteudo.AddChild(b);
		}

		Aviso("\nA viagem leva o tempo que diz: o piloto anda no passo normal, nao teleporta. "
			+ "Terra a Namek sao 7 dias in-game, como no anime.");
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
		var atual = (Jandirus.Core.Forms.Forma)_atributos.FormaAtual;
		Jandirus.Core.Forms.FormaDef? defAtual = Jandirus.Core.Forms.EscadaSaiyajin.Def(atual);

		Secao("Agora");
		Linha("Forma", defAtual?.Nome ?? "normal", defAtual != null ? Tema.Destaque : Tema.Texto);
		if (defAtual != null)
		{
			Linha("Maestria desta forma", $"{Maestria(atual):0.#}%");
			Linha("Dreno de Ki", $"{Jandirus.Core.Forms.EscadaSaiyajin.DrenoPorSegundo(atual, Livro()) * 100:0.##}% do Ki por segundo");
		}
		Aviso("\nSegure C pra reunir energia  ·  toque C duas vezes pra tentar subir  ·  X volta ao normal.\n"
			+ "Maestria SÓ cresce dentro da forma, gastando Ki -- é o único eixo do jogo que não se compra.");

		// SO O QUE JA DESPERTOU. Maestria > 0 quer dizer que este corpo ja esteve nessa forma
		// alguma vez -- e o unico registro honesto de "eu sei fazer isto".
		var minhas = new List<Jandirus.Core.Forms.FormaDef>();
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.EscadaSaiyajin.Degraus)
			if (d.Id == atual || Maestria(d.Id) > 0) minhas.Add(d);

		Secao("Formas que você desperta");

		if (minhas.Count == 0)
		{
			Aviso("Nenhuma, ainda. Nada garante que exista alguma -- e se existir, ela não vem por "
				+ "treino marcado: vem na hora em que vier.");
			return;
		}

		foreach (Jandirus.Core.Forms.FormaDef d in minhas)
		{
			double m = Maestria(d.Id);
			Linha(d.Nome,
				d.Id == atual ? $"EM USO  ·  maestria {m:0.#}%" : $"maestria {m:0.#}%",
				d.Id == atual ? Tema.Destaque : Tema.Bom);
		}

		Aviso("\nO que vem depois -- se vier -- você descobre tentando.");
	}

	private double Maestria(Jandirus.Core.Forms.Forma f)
	{
		foreach ((ushort id, float pct) in _atributos.Maestrias ?? [])
			if (id == (ushort)f) return pct;
		return 0;
	}

	/// <summary>As maestrias num formato que o Core entende, pra calcular dreno e multiplicador.</summary>
	private Jandirus.Core.Forms.Maestrias Livro()
	{
		var m = new Jandirus.Core.Forms.Maestrias();
		foreach ((ushort id, float pct) in _atributos.Maestrias ?? [])
			m.Por((Jandirus.Core.Forms.Forma)id, pct);
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
			"Items" => "Inventario, zenni e itens carregados. Vem com o sistema de itens.",
			"Equip" => "O que esta vestido e equipado. Vem com o sistema de itens.",
			"Tech" => "Nivel tecnologico, construcoes e androides. Vem com o sistema de tecnologia.",
			"Sense" => "Leitura de Ki: quem esta por perto e quao forte. Vem com a skill de Sense.",
			"Scan" => "Leitura EXATA de poder de luta pelo scouter. Vem com o scouter.",
			"Nav" => "Mapa do espaco e viagem entre planetas. Vem com o nav system.",
			_ => "Ainda nao implementado.",
		});
	}
}
