using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// A TELA DE TECLAS -- o pedido do dono: *"uma aba q permite eu colocar HOTKEYS de transformacoes e
/// verbs em QUALQUER TECLA do teclado, assim o usuario pode colocar teclas pra cada transformacao ou
/// verb sem precisar ter q abrir o menu P"*.
///
/// ============================ ELA MORA NA PAUSA, E NAO NO MENU P ============================
/// Tecla e preferencia de MAQUINA, como resolucao e volume -- o dedo e da pessoa, nao do Saiyajin.
/// O menu P e a ficha do PERSONAGEM (stats, formas, skills, cargos), e uma aba de teclas la dentro
/// prometeria que a ligacao troca junto com o personagem. Ela nao troca: mora no `config.json` (ver
/// o cabecalho do `Settings`), ao lado do zoom e do volume, que e onde a pausa ja vive.
/// ======================================================================================
///
/// ============================ TRES COISAS QUE ELA NAO FAZ ============================
///   * **nao decide se pode**. Um verb apagado continua ligavel: a ficha do personagem muda o tempo
///     todo (Ki, cooldown, alvo marcado) e ninguem liga uma tecla no instante exato em que a acao
///     esta pronta. Quem recusa e o disparo, com a frase do botao (ver `Atalhos.Disparar`);
///   * **nao guarda o nome** como identidade. Ela guarda a <see cref="Verbo.ChaveOuNome"/>, que e o
///     id estavel -- ver o cabecalho de `Verbo.Chave` pros quatro nomes que mudam sozinhos em jogo;
///   * **nao inventa padrao**. "Restaurar" e reler a tabela do <see cref="Teclas.Todas"/>. Uma
///     segunda copia dos padroes aqui divergiria no primeiro ajuste.
/// ================================================================================
/// </summary>
public partial class TelaDeTeclas : CanvasLayer
{
	public static TelaDeTeclas? Instancia { get; private set; }

	/// <summary>
	/// O TECLADO ESTA OCUPADO POR ESTA TELA? Lido pelo <see cref="Foco"/>.
	///
	/// SAO DUAS SITUACOES e nao uma: a busca com foco (como em toda tela) **e a captura**. A captura
	/// e a mais importante das duas e a mais facil de esquecer -- enquanto a tela espera "aperte a
	/// tecla", o jogo inteiro tem que parar de ouvir teclado, senao ligar uma forma ao C transforma
	/// o personagem no gesto de ligar.
	/// </summary>
	public static bool Digitando =>
		Instancia is { _raiz.Visible: true } t && (t._capturando != null || t._busca.HasFocus());

	private Control _raiz = null!;
	private LineEdit _busca = null!;
	private VBoxContainer _lista = null!;
	private Label _recado = null!;

	/// <summary>O que esta esperando tecla agora. Nulo = ninguem.</summary>
	private Alvo? _capturando;

	/// <summary>A troca que espera confirmacao: a tecla ja e de alguem.</summary>
	private (Alvo Alvo, Key Tecla, string Dono)? _confirmar;

	/// <summary>
	/// UMA LINHA DA TELA. Ou e uma acao do jogo (<see cref="Acao"/> preenchido), ou e um alvo do
	/// jogador (verb/forma). Os dois vao pra mesma tabela e disputam as mesmas teclas -- por isso
	/// eles compartilham o tipo em vez de terem cada um a sua tela.
	/// </summary>
	private sealed record Alvo(string Acao, string Tipo, string Chave, string Rotulo);

	public override void _Ready()
	{
		Instancia = this;
		Layer = 21;   // acima da pausa (20), que e de onde ela abre
		Montar();
		_raiz.Visible = false;
		Teclas.Mudou += AoMudarTeclas;
	}

	// ASSINATURA COM METODO NOMEADO E `-=` NO FIM. Lambda nao da pra cancelar, e uma tela que
	// assina e nunca solta deixa um orfao por ciclo de relog -- defeito ja pago neste projeto.
	public override void _ExitTree()
	{
		Teclas.Mudou -= AoMudarTeclas;
		if (Instancia == this) Instancia = null;
	}

	private void AoMudarTeclas() { if (_raiz.Visible) Redesenhar(); }

	public void Abrir()
	{
		_raiz.Visible = true;
		_capturando = null;
		_confirmar = null;
		Redesenhar();
	}

	public void Fechar()
	{
		_capturando = null;
		_confirmar = null;
		_raiz.Visible = false;
	}

	// =====================================================================
	// A CAPTURA
	// =====================================================================
	/// <summary>
	/// `_Input` e nao `_UnhandledInput`: capturando, esta tela tem que ver a tecla ANTES de
	/// qualquer outra -- inclusive antes do menu (P) e da mochila (I), que sao teclas perfeitamente
	/// ligaveis e que abririam a propria tela por cima da captura.
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		if (!_raiz.Visible) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		if (_capturando is not { } alvo)
		{
			if (k.Keycode != Key.Escape) return;

			// ESC DESFAZ UMA CAMADA POR VEZ, como no menu do jogo: primeiro solta a busca, e so
			// entao fecha. Sem a primeira metade quem clicou no campo de busca ficava com o foco
			// preso nele -- o `LineEdit` do Godot nao solta o foco sozinho.
			//
			// E e por ISTO que o ESC nao se liga a nada (ver `AcaoDeTecla.Fixa`): quem tivesse
			// ligado uma forma ao ESC ficaria sem como fechar a tela onde desfaria isso.
			if (_busca.HasFocus()) _busca.ReleaseFocus();
			else Fechar();
			GetViewport().SetInputAsHandled();
			return;
		}

		GetViewport().SetInputAsHandled();
		Key t = Teclas.Fisica(k);

		if (k.Keycode == Key.Escape) { _capturando = null; Redesenhar(); return; }

		if (!Teclas.Elegivel(t))
		{
			// A recusa NOMEIA o dono fixo -- "essa tecla nao pode" sem dizer por que mandaria o
			// jogador tentar de novo achando que foi engano.
			_recado.Text = $"{Teclas.Nome(t)} e do jogo e nao se troca ({Teclas.DonoDe(t)?.Rotulo}).";
			_capturando = null;
			Redesenhar();
			return;
		}

		// TECLA LIVRE: liga na hora. TECLA COM DONO: pergunta antes -- roubar calado deixaria o
		// jogador sem entender por que o soco parou de sair.
		if (Teclas.DonoDe(t) is { } dono && dono.Rotulo != alvo.Rotulo)
		{
			_confirmar = (alvo, t, dono.Rotulo);
			_capturando = null;
			Redesenhar();
			return;
		}

		Aplicar(alvo, t);
	}

	private void Aplicar(Alvo alvo, Key t)
	{
		_capturando = null;
		_confirmar = null;

		if (alvo.Acao.Length > 0) Teclas.Religar(alvo.Acao, t);
		else Teclas.Ligar(t, new Atalho(alvo.Tipo, alvo.Chave, alvo.Rotulo));

		_recado.Text = $"{alvo.Rotulo}  ->  {Teclas.Nome(t)}";
		Redesenhar();
	}

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	private void Montar()
	{
		var fundo = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.82f),
			AnchorRight = 1, AnchorBottom = 1,
		};
		AddChild(fundo);
		_raiz = fundo;
		Tema.Aplicar(fundo);

		var centro = new CenterContainer
		{
			AnchorRight = 1, AnchorBottom = 1,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
		};
		fundo.AddChild(centro);

		PanelContainer painel = Tema.Painel1(14);
		painel.CustomMinimumSize = new Vector2(760, 620);
		centro.AddChild(painel);

		var caixa = new VBoxContainer();
		painel.AddChild(caixa);

		var titulo = new Label { Text = "TECLAS", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 24);
		caixa.AddChild(titulo);

		caixa.AddChild(Tema.Legenda(
			"Clique na tecla de qualquer linha e aperte a tecla que voce quer. ESC cancela.\n"
			+ "A ligacao e desta MAQUINA: ela vale pra todos os seus personagens e sobrevive ao "
			+ "fechar o jogo.", Tema.TextoFraco, 12));
		caixa.AddChild(new HSeparator());

		_busca = new LineEdit { PlaceholderText = "procurar acao, tecnica ou forma..." };
		_busca.TextChanged += _ => Redesenhar();
		caixa.AddChild(_busca);

		var rolagem = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, 430),
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		caixa.AddChild(rolagem);

		_lista = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		rolagem.AddChild(_lista);

		caixa.AddChild(new HSeparator());
		_recado = Tema.Legenda("", Tema.Destaque, 13);
		caixa.AddChild(_recado);

		var rodape = new HBoxContainer();
		caixa.AddChild(rodape);

		// DUAS ETAPAS, porque ele apaga os ATALHOS junto -- e um atalho custou uma escolha e uma
		// busca, enquanto uma tecla de jogo so volta ao que ja era. Um clique unico aqui apagaria
		// trinta ligacoes de quem so queria devolver o WASD.
		var restaurar = new Button { Text = "Restaurar tudo (teclas E atalhos)" };
		bool armado = false;
		restaurar.Pressed += () =>
		{
			if (!armado)
			{
				armado = true;
				restaurar.Text = "Clique de novo pra confirmar";
				_recado.Text = "isso apaga TAMBEM as teclas que voce ligou a verbos e formas.";
				return;
			}
			armado = false;
			restaurar.Text = "Restaurar tudo (teclas E atalhos)";
			Teclas.RestaurarTudo();
			_recado.Text = "tudo de volta ao padrao de fabrica.";
			Redesenhar();
		};
		rodape.AddChild(restaurar);

		var fechar = new Button { Text = "Fechar (ESC)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		fechar.Pressed += Fechar;
		rodape.AddChild(fechar);
	}

	// =====================================================================
	// DESENHO
	// =====================================================================
	private void Redesenhar()
	{
		foreach (Node n in _lista.GetChildren()) n.QueueFree();

		if (_confirmar is { } c)
		{
			Confirmacao(c);
			return;
		}

		string busca = _busca.Text.Trim();

		// ---------------------------------------------------------- controles do jogo
		foreach (GrupoDeTecla g in Enum.GetValues<GrupoDeTecla>())
		{
			AcaoDeTecla[] doGrupo = [.. Teclas.Todas.Where(a => a.Grupo == g && Casa(a.Rotulo, busca))];
			if (doGrupo.Length == 0) continue;

			Secao(g switch
			{
				GrupoDeTecla.Movimento => "Movimento",
				GrupoDeTecla.Combate => "Combate",
				GrupoDeTecla.Voo => "Voo",
				GrupoDeTecla.Acao => "Acoes do corpo",
				_ => "Interface",
			});

			foreach (AcaoDeTecla a in doGrupo)
				Linha(new Alvo(a.Id, "acao", a.Id, a.Rotulo), Teclas.NomeDaAcao(a.Id), a.Fixa);
		}

		// ---------------------------------------------------------- transformacoes
		FormaDef[] formas = [.. FormasDespertas.Minhas()];
		var visiveis = formas.Where(d => Casa(Catalogo.NomeDe(d, new Maestrias()), busca)).ToArray();

		Secao("Transformacoes");
		if (formas.Length == 0)
		{
			_lista.AddChild(Tema.Legenda(
				"Nenhuma ainda. As formas aparecem aqui conforme voce as desperta -- e uma tecla de "
				+ "forma nao pula degrau: ela PEDE aquela forma, e a subida e conferida igualzinho "
				+ "ao toque duplo no C.", Tema.TextoFraco, 12));
		}
		else
		{
			foreach (FormaDef d in visiveis)
			{
				string nome = Catalogo.NomeDe(d, new Maestrias());
				Key tem = Teclas.TeclaDo("forma", d.Id);
				Linha(new Alvo("", "forma", d.Id, nome), tem == Key.None ? "-- sem tecla --" : Teclas.Nome(tem), false);
			}
		}

		// ---------------------------------------------------------- verbos
		foreach (string cat in new[] { Verbos.Skills, Verbos.Aprendizado, Verbos.Outros, Verbos.Admin })
		{
			Verbo[] doGrupo =
			[
				.. Verbos.Da(cat)
					.Where(Verbos.Visivel)
					// PASSIVA NAO SE LIGA: sem `Acionar` nao ha o que disparar. Ela continua no menu
					// (o jogador precisa saber que a tem), mas uma tecla pra ela nao teria efeito --
					// e tecla sem efeito e o que esta tela existe pra nao criar.
					.Where(v => v.Acionar != null && Casa(v.Nome, busca)),
			];
			if (doGrupo.Length == 0) continue;

			Secao(cat switch
			{
				"Skills" => "Tecnicas e habilidades",
				"Learning" => "Aprendizado",
				"Admin" => "Administracao",
				_ => "Outros",
			});

			foreach (Verbo v in doGrupo)
			{
				Key tem = Teclas.TeclaDo("verbo", v.ChaveOuNome);
				Linha(new Alvo("", "verbo", v.ChaveOuNome, v.Nome),
					  tem == Key.None ? "-- sem tecla --" : Teclas.Nome(tem), false);
			}
		}

		// ---------------------------------------------------------- as orfas
		//
		// AS LIGACOES CUJO ALVO NAO EXISTE NESTE PERSONAGEM. Elas aparecem APAGADAS, e nao somem:
		// sumir esconderia do jogador por que a tecla dele parou de funcionar, e faria a linha
		// "reaparecer sozinha" no dia em que ele voltasse ao outro personagem. E a mesma regra que
		// o menu ja aplica a verb indisponivel (ver `Verbo.Disponivel`).
		var orfas = Teclas.Ligados
			.Where(p => p.Value.Tipo == "forma"
				? !FormasDespertas.Sei(p.Value.Chave)
				: Verbos.PorChave(p.Value.Chave) == null)
			.ToArray();

		if (orfas.Length > 0)
		{
			Secao("Ligadas a coisas que este personagem nao tem");
			foreach ((Key k, Atalho a) in orfas)
			{
				var h = new HBoxContainer();
				Label rot = Tema.Legenda($"{a.Rotulo}   ({Teclas.Nome(k)})", Tema.TextoFraco, 13);
				rot.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				h.AddChild(rot);

				var tirar = new Button { Text = "esquecer" };
				string tipo = a.Tipo, chave = a.Chave;
				tirar.Pressed += () => { Teclas.Desligar(tipo, chave); Redesenhar(); };
				h.AddChild(tirar);
				_lista.AddChild(h);
			}
			_lista.AddChild(Tema.Legenda(
				"Elas continuam gravadas e nao mandam nada. Se voce voltar ao personagem que tem "
				+ "essas acoes, a tecla volta a funcionar sozinha.", Tema.TextoFraco, 11));
		}
	}

	private static bool Casa(string texto, string busca) =>
		busca.Length == 0 || texto.Contains(busca, StringComparison.OrdinalIgnoreCase);

	private void Secao(string t)
	{
		_lista.AddChild(new HSeparator());
		_lista.AddChild(Tema.Rotulo(t));
	}

	private void Linha(Alvo alvo, string tecla, bool fixa)
	{
		var h = new HBoxContainer();

		bool esperando = _capturando == alvo;
		Label rot = Tema.Legenda(alvo.Rotulo, fixa ? Tema.TextoFraco : Tema.Texto, 13);
		rot.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		h.AddChild(rot);

		var botao = new Button
		{
			Text = esperando ? "aperte uma tecla..." : tecla,
			CustomMinimumSize = new Vector2(190, 0),
			Disabled = fixa,
		};
		botao.Pressed += () => { _capturando = alvo; _confirmar = null; _recado.Text = ""; Redesenhar(); };
		h.AddChild(botao);

		// LIMPAR: a acao volta ao PADRAO (ela precisa de tecla pra existir), o atalho do jogador
		// some. Sao dois verbos diferentes na mesma coluna porque a coluna diz a mesma coisa nos
		// dois casos -- "tire o que eu botei aqui".
		var limpar = new Button { Text = "x", Disabled = fixa, CustomMinimumSize = new Vector2(34, 0) };
		limpar.Pressed += () =>
		{
			if (alvo.Acao.Length > 0) Teclas.Restaurar(alvo.Acao);
			else Teclas.Desligar(alvo.Tipo, alvo.Chave);
			Redesenhar();
		};
		h.AddChild(limpar);

		_lista.AddChild(h);
	}

	private void Confirmacao((Alvo Alvo, Key Tecla, string Dono) c)
	{
		_lista.AddChild(Tema.Rotulo("essa tecla ja tem dono"));
		_lista.AddChild(Tema.Legenda(
			$"{Teclas.Nome(c.Tecla)} e de \"{c.Dono}\".\n\n"
			+ $"Se voce confirmar, ela passa a ser de \"{c.Alvo.Rotulo}\" e \"{c.Dono}\" fica SEM "
			+ "tecla -- nao ha duas coisas na mesma tecla, e voce vai ver a linha vazia dele na "
			+ "lista.", Tema.Texto, 13));

		var h = new HBoxContainer();
		var sim = new Button { Text = "Trocar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		sim.Pressed += () => Aplicar(c.Alvo, c.Tecla);
		h.AddChild(sim);

		var nao = new Button { Text = "Cancelar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		nao.Pressed += () => { _confirmar = null; Redesenhar(); };
		h.AddChild(nao);
		_lista.AddChild(h);
	}
}
