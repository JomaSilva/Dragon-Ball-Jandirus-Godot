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
			if (_busca.HasFocus() && _busca.Text.Length > 0) { _busca.Text = ""; Redesenhar(); }
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

	private void Redesenhar()
	{
		List<string> abas = Abas();

		// a aba aberta pode ter DEIXADO de existir (tirou o scouter com o Scan aberto -- o
		// original tratava exatamente este caso)
		if (!abas.Contains(_aba)) _aba = abas.Contains("Sense") ? "Sense" : "Stats";

		foreach (Node n in _barraAbas.GetChildren()) n.QueueFree();
		foreach (string a in abas)
		{
			string qual = a;
			var b = new Button
			{
				Text = a,
				ToggleMode = true,
				ButtonPressed = a == _aba,
				FocusMode = Control.FocusModeEnum.None,
			};
			b.AddThemeFontSizeOverride("font_size", 12);
			b.Pressed += () => { _aba = qual; Redesenhar(); };
			_barraAbas.AddChild(b);
		}

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
		Linha("Classe", f.Class);
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
	/// A ESCADA, com o que falta em cada degrau e quanto voce domina dele.
	///
	/// Mostra os degraus BLOQUEADOS de proposito: saber que existe um SSJ3 e que ele pede tanto
	/// de BP e o que da direcao ao treino. Esconder o que falta transforma progressao em
	/// adivinhacao.
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
		Aviso("\nC sobe a escada  ·  X volta ao normal.\n"
			+ "Maestria SO cresce dentro da forma, gastando Ki -- e o unico eixo do jogo que nao se compra.");

		Secao("A escada");
		double bp = GameClient.Instance?.Sheet.BP ?? 0;
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.EscadaSaiyajin.Degraus)
		{
			double m = Maestria(d.Id);
			bool aberta = bp >= d.PortaBp
					   && (d.PedeMaestria <= 0 || Maestria(d.Vem) >= d.PedeMaestria);

			Color cor = d.Id == atual ? Tema.Destaque : aberta ? Tema.Bom : Tema.TextoFraco;
			Linha(d.Nome, m > 0 ? $"maestria {m:0.#}%" : aberta ? "disponivel" : "bloqueada", cor);

			if (aberta && m <= 0) continue;
			if (!aberta)
			{
				Aviso(bp < d.PortaBp
					? $"      pede {d.PortaBp:N0} de BP base (voce tem {bp:N0})"
					: $"      pede {d.PedeMaestria:0}% de maestria em {Jandirus.Core.Forms.EscadaSaiyajin.Def(d.Vem)?.Nome ?? "a forma anterior"}");
			}
		}
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

	/// <summary>O que EU JA SEI. Agrupado por arvore, que e como o jogador pensa nisso.</summary>
	private void Sabidas()
	{
		SkillCatalog? cat = Catalogo();
		if (cat == null) { Aviso("catalogo de skills nao encontrado (rode o AssetPipeline: comando 'skills')."); return; }

		Secao($"Aprendidas ({_livro.Aprendidas.Count})");
		if (_livro.Aprendidas.Count == 0)
		{
			Aviso("Voce ainda nao aprendeu nada. Abra a aba Learning.");
			return;
		}
		foreach (string p in _livro.Aprendidas.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
		{
			Skill? s = cat.Get(p);
			Linha(s?.Nome ?? p, s?.Tipo ?? "", Tema.Bom);
		}

		// os verbs registrados por skills que ja tem EFEITO implementado
		var acoes = Verbos.Da(Verbos.Skills).ToList();
		if (acoes.Count == 0) return;
		Secao("Acoes");
		foreach (Verbo v in acoes) Botao(v);
	}

	/// <summary>
	/// O QUE DA PRA APRENDER. Uma secao por arvore, e cada linha diz o que falta -- nao some com
	/// o que esta bloqueado: ver a skill e o requisito dela E o mapa da progressao.
	/// </summary>
	private void Aprendizado()
	{
		SkillCatalog? cat = Catalogo();
		if (cat == null) { Aviso("catalogo de skills nao encontrado (rode o AssetPipeline: comando 'skills')."); return; }
		if (GameClient.Instance is not { } cli) return;

		string raca = _atributos.Raca ?? "";
		string classe = cli.Sheet.Class ?? "";

		Secao($"Marcos: {_livro.MarcosLivres} livres de {_livro.MarcosTotais}");

		foreach (Skill arv in cat.ArvoresDe(raca, classe))
		{
			var galhos = arv.Galhos.Select(cat.Get).Where(s => s is { Nome.Length: > 0 }).ToList();
			if (galhos.Count == 0) continue;

			Secao(arv.Nome);
			foreach (Skill? s in galhos.OrderBy(g => g!.Tier).ThenBy(g => g!.Nome, StringComparer.OrdinalIgnoreCase))
			{
				Recusa r = _livro.PodeAprender(cat, s!.Path, raca, classe, vilao: false);
				int custo = SkillCatalog.CustoDe(s);

				if (r == Recusa.JaSabe) { Linha($"{s.Nome}  (t{s.Tier})", "aprendida", Tema.Bom); continue; }

				var b = new Button
				{
					Text = $"{s.Nome}   ·   t{s.Tier}   ·   {custo} marco{(custo > 1 ? "s" : "")}",
					TooltipText = s.Desc.Length > 0 ? s.Desc : s.Path,
					Alignment = HorizontalAlignment.Left,
					Disabled = r != Recusa.Pode,
				};
				string caminho = s.Path;
				b.Pressed += () => cli.SendAprender(caminho);
				_conteudo.AddChild(b);

				if (r is Recusa.Pode or Recusa.JaSabe) continue;
				Aviso("      " + r switch
				{
					Recusa.SemMarcos => $"faltam {custo - _livro.MarcosLivres} marco(s)",
					Recusa.FaltaPreRequisito => "falta pre-requisito: "
						+ string.Join(", ", s.PreReqs.Select(p => cat.Get(p)?.Nome ?? p)),
					Recusa.RacaOuClasse => "sua raca ou classe nao aprende",
					Recusa.Desligada => "desativada neste servidor",
					Recusa.SoVilao => "so pra vilao",
					_ => r.ToString(),
				});
			}
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
