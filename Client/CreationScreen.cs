using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Races;

namespace Jandirus.Client;

/// <summary>
/// CRIACAO DE PERSONAGEM com PREVIA AO VIVO.
///
/// O boneco fica na tela e muda NA HORA a cada escolha -- cabelo, cor, pele, roupa. Tudo
/// acontece ANTES de conectar: so no fim o cliente abre a conexao e manda a ficha inteira,
/// e o servidor confere. Isso significa que esta tela nao depende de rede nenhuma, e que o
/// jogador nunca ocupa uma vaga no servidor enquanto decide o penteado.
///
/// A tela e so COLETA e DESENHO. Quem decide stat, BP e classe e o servidor, com o mesmo
/// RaceCatalog -- a classe continua sendo sorteio cego, por isso ela nao aparece aqui.
/// </summary>
public partial class CreationScreen : CanvasLayer
{
	public event Action<CharacterDraft, Appearance>? Pronto;
	public event Action? Cancelado;

	/// <summary>Nome ja digitado na tela de login -- nao faz o jogador digitar duas vezes.</summary>
	public string NomeInicial = "";

	private readonly CharacterDraft _ficha = new();
	private readonly Appearance _visual = new();
	private RaceCatalog? _racas;
	private VisualCatalog? _cat;

	private OptionButton _corpo = null!;
	private GridContainer _gradeCabelo = null!, _gradeRoupa = null!;
	private GridContainer _gradeRaca = null!, _gradeFormas = null!;
	private VBoxContainer _caixaLinhagem = null!;
	private LineEdit _nome = null!;
	private TextEdit _historia = null!;
	private SpinBox _idade = null!;
	private Label _descricao = null!, _erro = null!, _notaDaIdade = null!, _contaLetras = null!;
	private HBoxContainer _linhaCorpo = null!;
	private VBoxContainer _blocoCabelo = null!;

	/// <summary>O sexo escolhido na etapa propria. "Male" pras racas que nao tem o passo.</summary>
	private string _generoEscolhido = "Male";

	/// <summary>Qual dos corpos do Frost Demon a grade esta editando (0 = base).</summary>
	private int _slotDeForma;
	private HBoxContainer _linhaCorCabelo = null!, _linhaCorPele = null!;
	private ColorPickerButton _corCabelo = null!, _corOlho = null!, _corPele = null!;
	private CheckBox _tingirCabelo = null!, _tingirOlho = null!, _tingirPele = null!;
	private CharacterVisual _boneco = null!;
	private VBoxContainer _listaRoupa = null!;

	public override void _Ready()
	{
		// acima da tela de login (que fica na camada 1). O Boot ainda esconde a de login,
		// mas duas telas empilhadas na MESMA camada dependem da ordem de criacao pra decidir
		// quem fica na frente -- e isso e o tipo de coisa que volta a quebrar sozinha.
		Layer = 2;

		_racas = Carregar("res://Assets/Data/races.json", RaceCatalog.Parse);
		_cat = Carregar("res://Assets/Data/visual.json", VisualCatalog.Parse);
		if (_racas == null) GD.PushWarning("[criacao] sem races.json -- rode o AssetPipeline (comando 'races')");
		if (_cat == null) GD.PushWarning("[criacao] sem visual.json -- rode o AssetPipeline (comando 'visual')");

		Montar();
		MontarGrades();
		EscolherPlaneta(CharacterDraft.Planetas[0]);
		MostrarEtapa();
	}

	private static T? Carregar<T>(string caminho, Func<string, T> parse) where T : class =>
		Godot.FileAccess.FileExists(caminho) ? parse(Godot.FileAccess.GetFileAsString(caminho)) : null;

	// =====================================================================
	// LAYOUT
	// =====================================================================
	private void Montar()
	{
		var fundo = new ColorRect { Color = Tema.Fundo, AnchorRight = 1, AnchorBottom = 1 };
		AddChild(fundo);

		var centro = new CenterContainer
		{
			AnchorRight = 1, AnchorBottom = 1,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
		};
		AddChild(centro);

		Tema.Aplicar(centro);

		var colunas = new HBoxContainer();
		colunas.AddThemeConstantOverride("separation", 24);
		centro.AddChild(colunas);

		_palco = MontarPalco();
		colunas.AddChild(_palco);
		colunas.AddChild(MontarFormulario());
	}

	/// <summary>A coluna do boneco. Escondida na etapa do planeta -- ver `MostrarEtapa`.</summary>
	private Control? _palco;

	/// <summary>O palco da previa: o boneco grande, girando com as setas.</summary>
	private Control MontarPalco()
	{
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) };

		var moldura = new PanelContainer { CustomMinimumSize = new Vector2(230, 260) };
		// a previa fica num painel de borda VIVA: e o assunto da tela, nao um acessorio
		moldura.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.BordaViva, 10));
		caixa.AddChild(moldura);

		// RECORTA: o boneco e um Node2D e ignora o layout do container -- sem isto ele escorre
		// por cima dos botoes de girar quando o sprite e alto
		var palco = new Control { ClipContents = true };
		moldura.AddChild(palco);

		// escala 5x: o sprite tem 32 px e a ideia e ver o penteado, nao adivinhar
		_boneco = new CharacterVisual { Name = "Previa", Scale = new Vector2(5, 5) };
		palco.AddChild(_boneco);

		// A POSICAO SEGUE O PAINEL, e nao um par de numeros cravados.
		//
		// Estava `Position = (115, 170)` -- metade de 230 e um pouco abaixo de metade de 260, os
		// valores do `CustomMinimumSize`. Mas `CustomMinimumSize` e um MINIMO: quem decide o
		// tamanho real do painel e o layout, e ele cresce com a janela e com o conteudo ao lado.
		// Assim que o painel deixa de ter exatamente 230x260, o boneco sai do centro -- desce,
		// escorrega pro lado, e em painel mais baixo chega a ser cortado pelo `ClipContents`.
		// Numero cravado contra caixa elastica so acerta na resolucao em que foi medido.
		//
		// Os pes ficam num terco a partir de baixo em vez do centro exato: o personagem tem
		// cabelo pra cima e nada pra baixo, entao centrar o QUADRO deixa a figura visualmente
		// baixa. Centrar onde o olho ve o corpo e o que parece centrado.
		void Centralizar()
		{
			Vector2 tam = palco.Size;
			if (tam.X <= 0 || tam.Y <= 0) return;
			_boneco.Position = new Vector2(tam.X * 0.5f, tam.Y * 0.62f);
		}
		palco.Resized += Centralizar;
		palco.Ready += Centralizar;

		var giro = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		foreach ((string txt, Jandirus.Core.World.Facing dir) in new[]
				 {
					 ("◀", Jandirus.Core.World.Facing.West), ("▼", Jandirus.Core.World.Facing.South),
					 ("▲", Jandirus.Core.World.Facing.North), ("▶", Jandirus.Core.World.Facing.East),
				 })
		{
			var b = new Button { Text = txt, CustomMinimumSize = new Vector2(46, 0) };
			Jandirus.Core.World.Facing d = dir;
			b.Pressed += () => _boneco.SetMotion(d, false);
			giro.AddChild(b);
		}
		caixa.AddChild(giro);

		var andar = new CheckBox { Text = "andando", ButtonPressed = false };
		andar.Toggled += on => _boneco.SetMotion(_facingAtual, on);
		caixa.AddChild(andar);

		return caixa;
	}

	private Jandirus.Core.World.Facing _facingAtual = Jandirus.Core.World.Facing.South;

	// =====================================================================
	// O ASSISTENTE
	// =====================================================================
	/// <summary>
	/// UMA ETAPA DA CRIACAO: o titulo, a pagina, e se ela vale pra este personagem.
	///
	/// O `Cabe` e o que faz o assistente pular sozinho o que nao se aplica -- linhagem so existe
	/// em tres racas, genero nao existe em cinco, e cabelo nao existe em seis. Uma etapa vazia com
	/// "nada a escolher aqui" seria um passo a mais pra atravessar sem motivo.
	/// </summary>
	private sealed record Etapa(string Titulo, string Dica, Control Pagina, Func<bool> Cabe);

	private readonly List<Etapa> _etapas = [];
	private int _passo;
	private Label _tituloEtapa = null!, _dicaEtapa = null!, _trilha = null!;
	private Button _btVoltar = null!, _btAvancar = null!;
	private VBoxContainer _pilha = null!;

	/// <summary>
	/// A CRIACAO EM ETAPAS, e nao um formulario so.
	///
	/// ============================ POR QUE MUDOU ============================
	/// Era uma coluna unica com nome, dois dropdowns, idade, corpo, tres seletores de cor e duas
	/// grades roláveis -- tudo visivel ao mesmo tempo. O dono resumiu em quatro palavras: "a criacao
	/// de personagem ta mt simples". O problema nao era falta de opcao, era que TUDO tinha o mesmo
	/// peso: escolher o planeta onde a sua historia comeca parecia tao importante quanto marcar uma
	/// caixinha de tingir olho.
	///
	/// O original faz uma pergunta por tela (`ui_choose` do `CreationUI.dm`), com ICONE em cada
	/// resposta, e e isso que da peso a escolha. Aqui cada etapa e uma pagina; o boneco da previa
	/// fica ao lado o tempo todo, entao ninguem perde o que ja montou.
	/// =======================================================================
	/// </summary>
	private Control MontarFormulario()
	{
		PanelContainer painel = Tema.Painel1(18);
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(470, 470) };
		caixa.AddThemeConstantOverride("separation", 7);
		painel.AddChild(caixa);

		_tituloEtapa = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_tituloEtapa.AddThemeFontSizeOverride("font_size", 24);
		caixa.AddChild(_tituloEtapa);

		_dicaEtapa = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.Word,
		};
		_dicaEtapa.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_dicaEtapa.AddThemeFontSizeOverride("font_size", 13);
		caixa.AddChild(_dicaEtapa);
		caixa.AddChild(new HSeparator());

		// A PILHA: todas as paginas vivem aqui, e so a da vez fica visivel. Nao se destroi nada --
		// voltar uma etapa tem que devolver exatamente o que o jogador tinha escolhido.
		_pilha = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		caixa.AddChild(_pilha);

		MontarEtapas();

		_descricao = new Label { AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(0, 46) };
		_descricao.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_descricao.AddThemeFontSizeOverride("font_size", 13);
		caixa.AddChild(_descricao);

		_erro = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.Word };
		_erro.AddThemeColorOverride("font_color", Tema.Perigo);
		caixa.AddChild(_erro);

		_trilha = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_trilha.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_trilha.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(_trilha);

		var botoes = new HBoxContainer();
		_btVoltar = new Button { Text = "Voltar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_btVoltar.Pressed += Retroceder;
		botoes.AddChild(_btVoltar);

		_btAvancar = new Button { Text = "Avançar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_btAvancar.Pressed += Avancar;
		botoes.AddChild(_btAvancar);
		caixa.AddChild(botoes);

		return painel;
	}

	private void MontarEtapas()
	{
		// ============================ ESTA ETAPA NAO E MAIS "ONDE VOCE NASCE" ============================
		// Ela nunca foi, na verdade: `pl.Planeta` so filtra QUAIS RACAS aparecem e nunca decidiu lugar
		// nenhum de jogo. Enquanto todo mundo nascia na Terra cravada, o titulo era so impreciso;
		// agora que existe a etapa "O BERCO", que decide de verdade, dois passos prometendo a mesma
		// coisa e uma tela contradizendo a si mesma -- um Frost Demon escolhido nesta pagina sob
		// "Vegeta" nasce em Icer, e nada explicaria isso ao jogador.
		// ==========================================================================================
		_etapas.Add(new Etapa("DE ONDE VEM O SEU POVO?",
			"O mundo que você escolhe aqui decide quais povos podem te gerar -- onde você "
			+ "vai acordar quem diz é o seu povo, mais adiante.", PaginaDePlaneta(), () => true));

		_etapas.Add(new Etapa("ESCOLHA SUA RAÇA",
			"O que você é importa mais que tudo o que vier depois.", PaginaDeRaca(), () => true));

		_etapas.Add(new Etapa("LINHAGEM",
			"O sangue de onde você vem, dentro do seu próprio povo.", PaginaDeLinhagem(),
			() => CharacterDraft.EscolhasDeClasse(_ficha.Race).Length > 0));

		// O BERCO VEM DEPOIS DA LINHAGEM, e a ordem e a da regra: o planeta e funcao da RACA (e, nas
		// duas excecoes, da classe que ainda vai ser sorteada). Perguntar antes de a raca existir
		// seria mostrar o berco de outro corpo -- o mesmo erro que a etapa do planeta ja teve com a
		// descricao da raca (ver `DescreverRacasDoPlaneta`).
		_etapas.Add(new Etapa("O BERÇO",
			"Onde o seu povo te põe no mundo -- e se você prefere acordar longe dele.",
			PaginaDeBerco(), () => true));

		_etapas.Add(new Etapa("GÊNERO", "", PaginaDeGenero(), () => _ficha.TemGenero));

		_etapas.Add(new Etapa("APARÊNCIA",
			"O corpo, a pele e o cabelo.", PaginaDeAparencia(),
			() => !Jandirus.Core.Races.FormasDeFrost.EhFrost(_ficha.Race)));

		// A ETAPA QUE SO O FROST DEMON VE, e ela SUBSTITUI a de aparencia.
		_etapas.Add(new Etapa("AS SUAS FORMAS",
			"Um Frost Demon não tem um corpo: tem vários. Escolha o de cada degrau.",
			PaginaDeFormas(), () => Jandirus.Core.Races.FormasDeFrost.EhFrost(_ficha.Race)));

		_etapas.Add(new Etapa("ROUPA",
			$"Até {Appearance.MaxRoupa} peças, cada uma com a sua cor.", PaginaDeRoupa(), () => true));

		_etapas.Add(new Etapa("PORTE DO CORPO",
			"A ÚNICA escolha desta tela que mexe em atributo -- e ela é permanente.",
			PaginaDePorte(), () => true));

		_etapas.Add(new Etapa("QUEM É VOCÊ?",
			"Nome, idade e a história que te trouxe até aqui.", PaginaDeIdentidade(), () => true));
	}

	/// <summary>A pagina da etapa atual, escondendo as outras.</summary>
	private void MostrarEtapa()
	{
		_passo = Math.Clamp(_passo, 0, _etapas.Count - 1);
		Etapa e = _etapas[_passo];

		for (int i = 0; i < _etapas.Count; i++) _etapas[i].Pagina.Visible = i == _passo;

		_tituloEtapa.Text = e.Titulo;
		_dicaEtapa.Text = e.Dica;
		_dicaEtapa.Visible = e.Dica.Length > 0;
		_erro.Text = "";

		// ============================ O BONECO SO APARECE QUANDO HA BONECO ============================
		// Na etapa do PLANETA nao ha personagem nenhum ainda -- o corpo ao lado era o padrao de uma
		// raça que o jogador nao escolheu, e ele ficava ali sugerindo que aquela ja era a escolha.
		// A tela de "onde voce nasce" e sobre o mundo, e nao sobre um humano palido de sunga.
		// ==============================================================================================
		bool temCorpo = _passo > 0;
		if (_palco != null) _palco.Visible = temCorpo;

		// O RODAPE FALA DO QUE A TELA PERGUNTA: no planeta, dos povos dele; no resto, da raça.
		_descricao.Text = _passo == 0 ? DescreverRacasDoPlaneta(_ficha.Planet) : Descrever(_ficha.Race);

		// A TRILHA CONTA SO AS ETAPAS QUE ESTE PERSONAGEM TEM. Mostrar "4 de 9" pra um Namekuseijin
		// que nunca vera genero nem aparencia de frost seria prometer telas que nao existem.
		int total = _etapas.Count(x => x.Cabe());
		int aqui = _etapas.Take(_passo + 1).Count(x => x.Cabe());
		_trilha.Text = $"etapa {aqui} de {total}";

		_btVoltar.Text = _passo == 0 ? "Cancelar" : "Voltar";
		_btAvancar.Text = UltimaEtapa ? "Entrar no mundo" : "Avançar";
	}

	private bool UltimaEtapa
	{
		get
		{
			for (int i = _passo + 1; i < _etapas.Count; i++)
				if (_etapas[i].Cabe()) return false;
			return true;
		}
	}

	private void Avancar()
	{
		string erro = ConferirEtapa();
		if (erro.Length > 0) { _erro.Text = erro; return; }

		if (UltimaEtapa) { Confirmar(); return; }

		do { _passo++; } while (_passo < _etapas.Count - 1 && !_etapas[_passo].Cabe());
		MostrarEtapa();
	}

	private void Retroceder()
	{
		if (_passo == 0) { Visible = false; Cancelado?.Invoke(); return; }

		do { _passo--; } while (_passo > 0 && !_etapas[_passo].Cabe());
		MostrarEtapa();
	}

	/// <summary>
	/// O QUE FALTA NESTA ETAPA -- vazio quer dizer "pode seguir".
	///
	/// A conferencia e por etapa, e nao so no fim, pelo motivo obvio: descobrir na ultima tela que
	/// o nome era curto demais e ter que voltar sete paginas e o jeito de fazer alguem desistir.
	/// </summary>
	private string ConferirEtapa() => _etapas[_passo].Titulo switch
	{
		"ESCOLHA SUA RAÇA" => _ficha.Race.Length == 0 ? "escolha uma raça" : "",
		"QUEM É VOCÊ?" => ConferirIdentidade(),
		_ => "",
	};

	private string ConferirIdentidade()
	{
		_ficha.Name = _nome.Text.Trim();
		_ficha.Age = (int)_idade.Value;
		_ficha.Backstory = _historia.Text;
		return _ficha.Validar();
	}

	/// <summary>
	/// A LISTA DAS PECAS VESTIDAS, uma linha por peca: nome, cor e "tirar".
	///
	/// ============================ A COR E POR PECA, E MORA NA PECA ============================
	/// Pedido do dono: "cada roupa q vc selecionar pode ter a cor alterada". A cor nao e um estado
	/// desta tela -- ela e um campo de `PecaDeRoupa`, dentro da `Appearance`. E por isso que ela
	/// sobrevive de graca ao save, a rede, a troca de zona e ao olhar dos outros: tudo isso ja
	/// carrega a aparencia inteira.
	///
	/// A LINHA E LIGADA POR CAMINHO, e nao por indice. A lista e reconstruida do zero a cada
	/// mexida, e o `Sanear` pode reordenar ou remover: um widget preso ao indice 0 acabaria
	/// pintando a peca errada.
	/// =========================================================================================
	/// </summary>
	private void RedesenharRoupa()
	{
		foreach (Node n in _listaRoupa.GetChildren()) n.QueueFree();
		foreach (PecaDeRoupa peca in _visual.Roupa.ToArray())
		{
			string alvo = peca.Caminho;
			var h = new HBoxContainer();
			h.AddChild(new Label
			{
				Text = "  " + NomeDeArquivo(alvo),
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			});

			// O MARCADOR DE "TINGIR" e o mesmo par de `LinhaDeCor`: sem ele nao ha como VOLTAR
			// pra cor natural do sprite, que e o padrao e o que a maioria das pecas quer.
			var tingir = new CheckBox { Text = "cor", ButtonPressed = peca.Cor != null };
			var cor = new ColorPickerButton
			{
				Color = peca.Cor is { } c ? new Color(c.R / 255f, c.G / 255f, c.B / 255f) : new Color("ffffff"),
				CustomMinimumSize = new Vector2(70, 24),
				Disabled = peca.Cor == null,
				EditAlpha = false,
			};

			void Pintar()
			{
				cor.Disabled = !tingir.ButtonPressed;
				int i = _visual.Roupa.FindIndex(p => p.Caminho == alvo);
				if (i < 0) return;
				_visual.Roupa[i] = new PecaDeRoupa(alvo, tingir.ButtonPressed ? DeCor(cor.Color) : null);
				// SO REPINTA A PREVIA. `Repintar()` reconstroi esta lista inteira, e reconstruir a
				// linha embaixo do seletor que o jogador esta arrastando fecha o seletor a cada
				// pixel de mudanca -- escolher cor viraria impossivel.
				Previa();
			}

			tingir.Toggled += _ => Pintar();
			cor.ColorChanged += _ => Pintar();
			h.AddChild(tingir);
			h.AddChild(cor);

			var x = new Button { Text = "tirar" };
			x.Pressed += () => { _visual.Roupa.RemoveAll(p => p.Caminho == alvo); Repintar(); };
			h.AddChild(x);
			_listaRoupa.AddChild(h);
		}
	}

	// =====================================================================
	// AS PAGINAS
	// =====================================================================
	private VBoxContainer NovaPagina()
	{
		var p = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		p.AddThemeConstantOverride("separation", 6);
		_pilha.AddChild(p);
		return p;
	}

	/// <summary>
	/// OS CINCO MUNDOS, com o icone de cada um.
	///
	/// Os icones ja existiam e nao eram usados aqui: `Assets/Sprites/Misc/Planets.tres` e o mesmo
	/// atlas que desenha a galaxia no espaco (ver `CeuDoEspaco`), com um estado por planeta. A
	/// criacao mostrava os cinco como texto num `OptionButton` -- e o original mostra o disco de
	/// cada mundo desde sempre (`CreationUI.dm:339-343` monta os icones a partir dos proprios
	/// objetos de planeta).
	/// </summary>
	private Control PaginaDePlaneta()
	{
		VBoxContainer p = NovaPagina();
		var grade = new GridContainer { Columns = 3 };
		grade.AddThemeConstantOverride("h_separation", 10);
		grade.AddThemeConstantOverride("v_separation", 10);
		p.AddChild(grade);

		for (int i = 0; i < CharacterDraft.Planetas.Length; i++)
		{
			string planeta = CharacterDraft.Planetas[i];
			Button b = CartaoGrande(IconeDePlaneta(planeta), NomeBonito(planeta), DescreverPlaneta(planeta));
			b.Pressed += () => { MarcarSelecionado(grade, b); EscolherPlaneta(planeta); };
			grade.AddChild(b);
			if (i == 0) b.ButtonPressed = true;
		}
		return p;
	}

	/// <summary>
	/// O QUE ESTE MUNDO OFERECE -- os povos que nascem nele.
	///
	/// A ETAPA DO PLANETA FALAVA DA RACA ERRADA. O rodape mostra a descricao da raça SELECIONADA, e
	/// na primeira tela ainda nao ha raça escolhida -- so a primeira da lista, que o jogador nem
	/// viu. Ele lia "Comeca forte. Linhagens possiveis: Legendary, Elite..." embaixo de uma tela que
	/// pergunta onde ele nasce, falando de um Saiyajin que ele nao escolheu.
	///
	/// O que a tela do planeta tem a dizer e QUEM nasce ali.
	/// </summary>
	private static string DescreverRacasDoPlaneta(string planeta)
	{
		string[] racas = CharacterDraft.RacasDoPlaneta(planeta);
		if (racas.Length == 0) return "";
		return $"Povos deste mundo: {string.Join(", ", racas.Select(NomeBonito))}.";
	}

	/// <summary>As racas do planeta escolhido, cada uma com o proprio corpo como retrato.</summary>
	private Control PaginaDeRaca()
	{
		VBoxContainer p = NovaPagina();
		_gradeRaca = new GridContainer { Columns = 4 };
		_gradeRaca.AddThemeConstantOverride("h_separation", 8);
		_gradeRaca.AddThemeConstantOverride("v_separation", 8);
		p.AddChild(Rolagem(_gradeRaca, 300));
		return p;
	}

	private Control PaginaDeLinhagem()
	{
		VBoxContainer p = NovaPagina();
		_caixaLinhagem = new VBoxContainer();
		_caixaLinhagem.AddThemeConstantOverride("separation", 6);
		p.AddChild(_caixaLinhagem);
		return p;
	}

	// =====================================================================
	// O BERCO
	// =====================================================================
	private VBoxContainer _caixaBerco = null!;

	/// <summary>
	/// ONDE ESTE CORPO VAI NASCER -- e a unica escolha desta tela que muda o MUNDO em vez do corpo.
	///
	/// ============================ O CLIENTE CALCULA ISTO SOZINHO ============================
	/// Nada aqui vem do servidor, e nao e economia de pacote: e a regra 0.2. O planeta natal e uma
	/// tabela pura (<see cref="Bercos.PlanetaNatal"/>) e as irmas de um pre-feito saem so do nome
	/// dele (`Sistemas.Ancorar` deriva tudo de `Hash64(nome)`), entao esta tela chega no MESMO
	/// resultado que o servidor sem trocar um byte -- inclusive antes de existir conexao, que e
	/// quando esta tela roda.
	///
	/// O QUE ELA NAO SABE E QUAL DAS IRMAS SAI, e isso e de proposito: a gravidade do berco vira
	/// `GravMastered` de graca no nascimento (`race.dm:130-131`), entao um cliente que escolhesse o
	/// mundo escolheria um atributo permanente. Ele mostra o SORTEIO inteiro e manda um BIT
	/// (<see cref="CharacterDraft.PertoDeCasa"/>); quem sorteia e o servidor.
	/// ====================================================================================
	/// </summary>
	private Control PaginaDeBerco()
	{
		VBoxContainer p = NovaPagina();
		_caixaBerco = new VBoxContainer();
		_caixaBerco.AddThemeConstantOverride("separation", 8);
		p.AddChild(Rolagem(_caixaBerco, 320));
		return p;
	}

	/// <summary>
	/// Refaz a pagina do berco pra raca escolhida. Chamada do <see cref="EscolherRaca"/> porque e
	/// a raca que decide TUDO aqui -- o natal, as irmas e se ha escolha.
	/// </summary>
	private void AtualizarBerco()
	{
		if (_caixaBerco == null) return;
		foreach (Node n in _caixaBerco.GetChildren()) n.QueueFree();

		// O PADRAO E CASA, e ele e reafirmado a cada troca de raca. Sem esta linha, marcar "vizinho"
		// como Saiyajin e depois voltar pra escolher Kai deixaria o pedido ligado numa raca que nem
		// mostra a opcao -- o servidor o ignoraria, mas o save carregaria uma escolha fantasma que o
		// renascimento leria pra sempre.
		_ficha.PertoDeCasa = false;

		string natal = Bercos.PlanetaNatal(_ficha.Race);
		List<Jandirus.Core.World.PlanetaNoEspaco> irmas = Bercos.IrmasDoNatal(natal);

		var titulo = new Label
		{
			Text = $"VOCÊ NASCE EM {NomeDeMundo(natal).ToUpperInvariant()}",
			AutowrapMode = TextServer.AutowrapMode.Word,
		};
		titulo.AddThemeColorOverride("font_color", Tema.Destaque);
		titulo.AddThemeFontSizeOverride("font_size", 16);
		_caixaBerco.AddChild(titulo);

		var lore = new Label { Text = LoreDoBerco(_ficha.Race), AutowrapMode = TextServer.AutowrapMode.Word };
		lore.AddThemeColorOverride("font_color", Tema.TextoFraco);
		lore.AddThemeFontSizeOverride("font_size", 13);
		_caixaBerco.AddChild(lore);

		// ============================ A RACA QUE NAO TEM VIZINHANCA ============================
		// Paraiso e Inferno existem como ZONA e nao como CORPO -- no DM sao `area/afterlifeareas` e
		// nao planetas --, entao nao ha orbita irma a oferecer. O `Bercos.Vizinho` devolveria o
		// proprio natal; esconder a opcao e dizer a mesma coisa antes, em vez de aceitar um pedido
		// e nao cumpri-lo.
		// ==================================================================================
		if (irmas.Count == 0)
		{
			var so = new Label
			{
				Text = $"Não há para onde ser mandado: {NomeDeMundo(natal)} não é um corpo no mapa do "
					 + "universo, é um plano. Daqui não se decola -- daqui se renasce.",
				AutowrapMode = TextServer.AutowrapMode.Word,
			};
			so.AddThemeFontSizeOverride("font_size", 13);
			_caixaBerco.AddChild(so);
			return;
		}

		string nomes = string.Join(", ", irmas.Select(i => i.Nome));
		var opcoes = new VBoxContainer();
		opcoes.AddThemeConstantOverride("separation", 6);
		_caixaBerco.AddChild(opcoes);

		Button casa = CartaoLargo($"Em casa — {NomeDeMundo(natal)}",
			"O mundo do seu povo, com o chão que alguém desenhou à mão.");
		casa.Pressed += () => { MarcarSelecionado(opcoes, casa); _ficha.PertoDeCasa = false; };
		opcoes.AddChild(casa);

		// A LISTA DAS CANDIDATAS E O QUE FAZ A OPCAO SIGNIFICAR ALGUMA COISA. "Um planeta aleatorio"
		// sem dizer quais e um botao que promete o desconhecido; com os nomes, o jogador sabe que sao
		// dois ou tres mundos concretos da estrela de casa -- e o numero MUDA por raca (Icer tem uma
		// irma so, medido), o que e informacao de jogo e nao enfeite.
		Button perto = CartaoLargo(
			$"Num mundo vizinho — {irmas.Count} possível{(irmas.Count > 1 ? "eis" : "")}: {nomes}",
			"Mesma estrela, outro chão: um mundo gerado, sem cidade e sem ninguém. Qual deles "
			+ "é sorteio do berço, e você só descobre ao abrir os olhos.");
		perto.Pressed += () => { MarcarSelecionado(opcoes, perto); _ficha.PertoDeCasa = true; };
		opcoes.AddChild(perto);

		casa.ButtonPressed = true;

		// ============================ O AVISO DO SANGUE SAIYAJIN ============================
		// As duas excecoes do dono dependem da CLASSE, e a classe e sorteada pelo servidor e nunca
		// mostrada (e por isso que existe a "dica de classe" no chat). Entao esta tela nao pode
		// prometer Vegeta a um Saiyajin -- ela mentiria pra 46% deles.
		//
		// O TEXTO DIZ AS DUAS REGRAS E NAO DIZ QUAL E A SUA. Isso e sabor e nao limitacao: o
		// classe-baixa despachado e o Lendario exilado sao as duas historias mais conhecidas da
		// raca, e le-las aqui faz o jogador ENTENDER o berco estranho em vez de achar que o jogo
		// quebrou. O que ele nao recebe e a confirmacao -- o berco vira mais uma pista, do mesmo
		// tamanho da dica de classe, e nao uma ficha aberta.
		// ================================================================================
		if (Bercos.PlanetaNatal(_ficha.Race) == "Vegeta" && _ficha.Race == "Saiyan")
		{
			var sangue = new Label
			{
				Text = "MAS NEM TODO SAIYAJIN NASCE EM VEGETA. O de classe baixa é despachado ainda "
					 + "bebê para um mundo fraco de se conquistar -- a Terra --, e o Lendário não é "
					 + "criado em lugar nenhum que se possa procurar: puseram o berço numa cápsula e "
					 + "a cápsula num rumo que ninguém anotou, porque tinham medo dele.\n"
					 + "Nada disso se escolhe aqui. O seu sangue decide, e você só saberá ao acordar.",
				AutowrapMode = TextServer.AutowrapMode.Word,
			};
			sangue.AddThemeColorOverride("font_color", Tema.Perigo);
			sangue.AddThemeFontSizeOverride("font_size", 12);
			_caixaBerco.AddChild(sangue);
		}
	}

	/// <summary>De onde cada povo vem, na voz do mundo. O planeta em si sai do <see cref="Bercos"/>.</summary>
	private static string LoreDoBerco(string raca) => raca switch
	{
		"Saiyan" or "Tsujin" or "Saibaman" =>
			"O mundo vermelho dos guerreiros, e o chão mais pesado onde alguém escolhe morar.",
		"Namekian" or "Gray" or "Kanassa" or "Yardrat" =>
			"O planeta verde, de três sóis e nenhuma noite.",
		"Icer" => "O mundo gelado da estirpe que cobrou tributo do universo inteiro.",
		"Arlian" => "Arlia, o mundo dos insetos -- pequeno, duro e esquecido.",
		"Makyo" => "A Estrela Makyo, que só se aproxima da Terra a cada cinco mil anos.",
		"Kai" => "O Paraíso. Não se chega aqui de nave.",
		"Demon" => "O Inferno. Não se chega aqui de nave.",
		"Alien" =>
			"O seu povo não tem lar: os mundos que ele conhece são todos de passagem. Você começa "
			+ "no porto mais movimentado que existe.",
		"Android" or "BioAndroid" or "SpiritDoll" =>
			"Você não nasce: alguém te monta. Sem um criador, o mundo te deixa onde deixa todo mundo.",
		"Halfbreed" =>
			"Sangue misturado não tem mundo próprio. Nascido de gravidez, você herdaria o planeta de "
			+ "quem te gerou; nascido aqui, começa na Terra.",
		_ => "O lar do seu povo.",
	};

	/// <summary>O nome de um mundo como gente escreve: `Makyo_Star` vira "Makyo Star".</summary>
	private static string NomeDeMundo(string zona) => zona switch
	{
		"Earth" => "Terra",
		"Heaven" => "Paraíso",
		"Hell" => "Inferno",
		_ => zona.Replace('_', ' '),
	};

	private Control PaginaDeGenero()
	{
		VBoxContainer p = NovaPagina();
		var h = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		h.AddThemeConstantOverride("separation", 16);
		p.AddChild(h);

		foreach ((string rotulo, string valor) in new[] { ("Masculino", "Male"), ("Feminino", "Female") })
		{
			string g = valor;
			Button b = CartaoGrande(null, rotulo, "");
			b.Pressed += () => { MarcarSelecionado(h, b); _generoEscolhido = g; AoTrocarGenero(); };
			h.AddChild(b);
			if (g == "Male") b.ButtonPressed = true;
		}
		return p;
	}

	private Control PaginaDeAparencia()
	{
		VBoxContainer p = NovaPagina();

		_corpo = new OptionButton();
		_corpo.ItemSelected += i => { _visual.Corpo = (int)i; _visual.Tom = (int)i; Repintar(); };
		_linhaCorpo = Linha("Corpo", _corpo);
		p.AddChild(_linhaCorpo);

		// COR OPCIONAL. O padrao e NAO tingir: os sprites ja vem coloridos, com realce e
		// sombra desenhados. Quando o jogador liga a cor, ela e SOMADA por cima (o ICON_ADD
		// do jogo) -- multiplicar apagaria o desenho e o cabelo viraria uma silhueta.
		(_linhaCorPele, _tingirPele, _corPele) = LinhaDeCor("Cor do corpo", new Color("ff9ab5"),
			() => { _visual.CorPele = _tingirPele.ButtonPressed ? DeCor(_corPele.Color) : null; Previa(); });
		p.AddChild(_linhaCorPele);

		// CABELO: grade de icones. Ver o penteado antes de escolher e o ponto todo -- um
		// dropdown com 59 nomes ("Kylin 2"?) nao diz nada.
		_blocoCabelo = new VBoxContainer();
		_blocoCabelo.AddChild(Tema.Rotulo("Cabelo"));
		_gradeCabelo = new GridContainer { Columns = 8 };
		_blocoCabelo.AddChild(Rolagem(_gradeCabelo, 170));
		p.AddChild(_blocoCabelo);

		(_linhaCorCabelo, _tingirCabelo, _corCabelo) = LinhaDeCor("Colorir o cabelo", new Color("8a4b2a"),
			() => { _visual.CorCabelo = _tingirCabelo.ButtonPressed ? DeCor(_corCabelo.Color) : null; Previa(); });
		p.AddChild(_linhaCorCabelo);

		(HBoxContainer linhaOlho, _tingirOlho, _corOlho) = LinhaDeCor("Colorir os olhos", new Color("2857c8"),
			() => { _visual.CorOlho = _tingirOlho.ButtonPressed ? DeCor(_corOlho.Color) : null; Previa(); });
		p.AddChild(linhaOlho);

		return p;
	}

	/// <summary>
	/// A APARENCIA DO FROST DEMON: um corpo por degrau da escada dele.
	///
	/// ============================ ELE NAO TEM "UM CORPO" ============================
	/// Todas as outras racas escolhem um sprite e pronto; a transformacao delas troca cabelo e
	/// aura. A do Frost Demon troca o CORPO INTEIRO -- e por isso o original pula o passo de corpo
	/// pra ele e abre o `formchoose` (`body_custom.dm:197-203`). O dono confirmou com todas as
	/// letras: "a aparencia do frost demon e onde ele escolhe as formas dele".
	///
	/// SAO TRES SLOTS, e nao sete. As quatro supressoes so existem pro Mutante, e ninguem sabe
	/// AINDA se sera um: a classe e sorteada no servidor (1%). Pedir sete corpos a todo mundo seria
	/// pedir quatro escolhas que 99 em cada 100 jogadores nunca veriam -- entao quem sair Mutante
	/// recebe os quatro de baixo nos padroes do original, e pode re-escolher em jogo depois.
	/// ================================================================================
	/// </summary>
	private Control PaginaDeFormas()
	{
		VBoxContainer p = NovaPagina();
		int[] degraus = Jandirus.Core.Races.FormasDeFrost.DegrausDe(
			Jandirus.Core.Races.FormasDeFrost.ClasseNormal);

		var abas = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		abas.AddThemeConstantOverride("separation", 6);
		p.AddChild(abas);

		_gradeFormas = new GridContainer { Columns = 6 };
		_gradeFormas.AddThemeConstantOverride("h_separation", 6);
		_gradeFormas.AddThemeConstantOverride("v_separation", 6);
		p.AddChild(Rolagem(_gradeFormas, 260));

		for (int i = 0; i < degraus.Length; i++)
		{
			int slot = i;
			int degrau = degraus[i];
			var b = new Button
			{
				Text = Jandirus.Core.Races.FormasDeFrost.Nome(degrau),
				ToggleMode = true,
				TooltipText = degrau == Jandirus.Core.Races.FormasDeFrost.Base
					? "onde você começa"
					: $"multiplica o seu poder por {Jandirus.Core.Races.FormasDeFrost.Multiplicador(degrau):0}",
			};
			b.AddThemeFontSizeOverride("font_size", 12);
			b.Pressed += () => { MarcarSelecionado(abas, b); _slotDeForma = slot; DesenharGradeDeFormas(); };
			abas.AddChild(b);
			if (i == 0) b.ButtonPressed = true;
		}

		return p;
	}

	/// <summary>Os botoes do catalogo, por corpo. Montados UMA vez -- ver `DesenharGradeDeFormas`.</summary>
	private readonly Dictionary<string, Button> _botoesDeForma = [];

	/// <summary>
	/// A GRADE DE CORPOS. Montada uma vez; trocar de aba so remarca qual esta aceso.
	///
	/// ============================ ERA UM ENGASGO POR CLIQUE ============================
	/// A versao anterior destruia e reconstruia os 54 botoes a cada troca de aba, e cada botao
	/// carrega um `SpriteFrames` do disco pra tirar a miniatura. Sao 54 leituras de recurso no meio
	/// de um clique -- o dono sentiu exatamente isso ("ao clicar em frost demon o jogo da uma
	/// travadinha"), e ele acontecia DE NOVO a cada vez que ele trocava de degrau.
	///
	/// Montando uma vez, o custo acontece uma vez. E depois disso trocar de aba e mudar um booleano
	/// em 54 botoes, que e trabalho de um quadro.
	/// ===================================================================================
	/// </summary>
	private void DesenharGradeDeFormas()
	{
		if (_slotDeForma >= _visual.FormasDeFrost.Count) return;

		if (_botoesDeForma.Count == 0)
			foreach (string corpo in Jandirus.Core.Races.FormasDeFrost.Catalogo)
			{
				string alvo = corpo;
				Button b = BotaoIcone(Miniatura(Jandirus.Core.Races.FormasDeFrost.Caminho(corpo)), corpo);
				b.Pressed += () =>
				{
					_visual.FormasDeFrost[_slotDeForma] = alvo;
					MarcarSelecionado(_gradeFormas, b);
					Previa();
				};
				_gradeFormas.AddChild(b);
				_botoesDeForma[corpo] = b;
			}

		string atual = _visual.FormasDeFrost[_slotDeForma];
		foreach ((string corpo, Button b) in _botoesDeForma) b.ButtonPressed = corpo == atual;
	}

	private Control PaginaDeRoupa()
	{
		VBoxContainer p = NovaPagina();
		_gradeRoupa = new GridContainer { Columns = 8 };
		p.AddChild(Rolagem(_gradeRoupa, 200));
		_listaRoupa = new VBoxContainer();
		p.AddChild(_listaRoupa);
		return p;
	}

	/// <summary>
	/// O PORTE -- Medium, Small ou Large. E o passo "TIPO DE CORPO" do original, que nunca foi
	/// portado, e a unica escolha desta tela que muda numero.
	///
	/// O TEXTO DIZ O PRECO, e nao so o beneficio. Um "agil: mais rapido!" faria todo mundo escolher
	/// Small; o original avisa em maiuscula que a mudanca e permanente e lista as duas metades da
	/// troca, porque a escolha so e interessante se doer.
	/// </summary>
	private Control PaginaDePorte()
	{
		VBoxContainer p = NovaPagina();
		var caixa = new VBoxContainer();
		caixa.AddThemeConstantOverride("separation", 8);
		p.AddChild(caixa);

		(string, string, string)[] opcoes =
		[
			("Medium", "Padrão", "Nenhum ajuste de atributos. O corpo que a sua raça te deu."),
			("Small", "Ágil", "Acerta, esquiva, corre e recupera fôlego com mais facilidade. Em troca, "
							+ "corte severo em força e resistência física."),
			("Large", "Gigante", "Resistência e força enormes. Em troca, mais lento, recupera devagar "
							   + "e é mais fácil de acertar."),
		];

		foreach ((string valor, string titulo, string texto) in opcoes)
		{
			string v = valor;
			Button b = CartaoLargo(titulo, texto);
			b.Pressed += () => { MarcarSelecionado(caixa, b); _ficha.Porte = v; };
			caixa.AddChild(b);
			if (v == "Medium") b.ButtonPressed = true;
		}
		return p;
	}

	/// <summary>
	/// NOME, IDADE E HISTORIA -- o formulario "QUEM E VOCE?" do original (CreationUI.dm:148-157).
	///
	/// A HISTORIA E TEXTO LIVRE de 10 a 500 caracteres, e nao um menu de origens com bonus. Ver
	/// `CharacterDraft.Backstory`: transformar isso em build mudaria o que a caixa significa.
	///
	/// A IDADE TEM TETO POR RACA, e o teto e o AUGE dela: um Saibaman para em 10, um Kai vai a
	/// 75 mil. Era um 1-200 igual pra todo mundo. Ver `Envelhecimento`.
	/// </summary>
	private Control PaginaDeIdentidade()
	{
		VBoxContainer p = NovaPagina();

		_nome = new LineEdit { PlaceholderText = "nome do personagem", MaxLength = 24, Text = NomeInicial };
		p.AddChild(Linha("Nome", _nome));

		_idade = new SpinBox { MinValue = 1, MaxValue = 200, Value = 18 };
		p.AddChild(Linha("Idade", _idade));

		_notaDaIdade = new Label { AutowrapMode = TextServer.AutowrapMode.Word };
		_notaDaIdade.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_notaDaIdade.AddThemeFontSizeOverride("font_size", 12);
		p.AddChild(_notaDaIdade);

		p.AddChild(Tema.Rotulo("História"));
		_historia = new TextEdit
		{
			PlaceholderText = "quem é você, de onde veio, o que quer",
			CustomMinimumSize = new Vector2(0, 150),
			WrapMode = TextEdit.LineWrappingMode.Boundary,
		};
		p.AddChild(_historia);

		_contaLetras = new Label { HorizontalAlignment = HorizontalAlignment.Right };
		_contaLetras.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_contaLetras.AddThemeFontSizeOverride("font_size", 12);
		p.AddChild(_contaLetras);

		_historia.TextChanged += AtualizarContagem;
		AtualizarContagem();
		return p;
	}

	private void AtualizarContagem()
	{
		int n = _historia.Text.Trim().Length;
		_contaLetras.Text = n < CharacterDraft.BackstoryMin
			? $"{n} de {CharacterDraft.BackstoryMin} caracteres no mínimo"
			: $"{n} / {CharacterDraft.BackstoryMax}";
		_contaLetras.AddThemeColorOverride("font_color",
			n > CharacterDraft.BackstoryMax ? Tema.Perigo : Tema.TextoFraco);
	}

	// =====================================================================
	// PECAS DAS PAGINAS
	// =====================================================================
	/// <summary>Um cartao alto: icone grande em cima, nome embaixo, descricao no tooltip.</summary>
	private static Button CartaoGrande(Texture2D? icone, string titulo, string dica)
	{
		var b = new Button
		{
			CustomMinimumSize = new Vector2(132, 116),
			Text = titulo,
			TooltipText = dica,
			ToggleMode = true,
			IconAlignment = HorizontalAlignment.Center,
			VerticalIconAlignment = VerticalAlignment.Top,
			ExpandIcon = true,
		};
		if (icone != null) b.Icon = icone;
		return b;
	}

	/// <summary>Um cartao largo de uma linha de titulo e um paragrafo -- o porte usa tres deles.</summary>
	private static Button CartaoLargo(string titulo, string texto)
	{
		var b = new Button
		{
			Text = $"{titulo}\n{texto}",
			ToggleMode = true,
			AutowrapMode = TextServer.AutowrapMode.Word,
			CustomMinimumSize = new Vector2(0, 74),
			Alignment = HorizontalAlignment.Left,
		};
		b.AddThemeFontSizeOverride("font_size", 13);
		return b;
	}

	/// <summary>
	/// O DISCO DO PLANETA, tirado do mesmo atlas que desenha a galaxia. Os nomes dos estados sao
	/// minusculos com underscore (ver `CeuDoEspaco`), e nao os nomes bonitos.
	/// </summary>
	private static Texture2D? IconeDePlaneta(string planeta)
	{
		var f = ResourceLoader.Load<SpriteFrames>("res://Assets/Sprites/Misc/Planets.tres");
		if (f == null) return null;
		string estado = planeta.ToLowerInvariant();
		return f.HasAnimation(estado) && f.GetFrameCount(estado) > 0 ? f.GetFrameTexture(estado, 0) : null;
	}

	private static string DescreverPlaneta(string p) => p switch
	{
		"Earth" => "O lar dos Humanos.",
		"Namek" => "O planeta verde dos Namekuseijins.",
		"Vegeta" => "O mundo-berço dos Saiyajins.",
		"Heaven" => "O plano celestial.",
		"Hell" => "O submundo.",
		_ => "",
	};

	// =====================================================================
	// GRADES DE ICONE
	// =====================================================================
	/// <summary>Uma area rolavel com altura fixa -- 59 cabelos nao cabem numa tela.</summary>
	private static ScrollContainer Rolagem(Control dentro, int altura)
	{
		var sc = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, altura),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		dentro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		sc.AddChild(dentro);
		return sc;
	}

	/// <summary>
	/// O primeiro quadro de um SpriteFrames, virado textura -- e o icone do botao.
	/// Barato: o AtlasTexture aponta pro mesmo PNG ja carregado, nao copia pixel nenhum.
	/// </summary>
	private static Texture2D? Miniatura(string caminho)
	{
		var f = ResourceLoader.Load<SpriteFrames>(caminho);
		if (f == null) return null;

		// A ordem importa: a pose PARADA de frente e o melhor retrato. Mas nem toda peca tem
		// uma -- depois que a caminhada virou um estado separado (`walk_*`), varias so tem
		// essa. Procurar so por `default_south` deixava a grade quase toda sem icone.
		foreach (string anim in new[] { "default_south", "walk_south", "default_east", "walk_east" })
			if (f.HasAnimation(anim) && f.GetFrameCount(anim) > 0)
				return f.GetFrameTexture(anim, 0);

		// ultimo recurso: o primeiro quadro de qualquer animacao que exista
		foreach (StringName anim in f.GetAnimationNames())
			if (f.GetFrameCount(anim) > 0)
				return f.GetFrameTexture(anim, 0);

		return null;
	}

	private static Button BotaoIcone(Texture2D? icone, string dica)
	{
		var b = new Button
		{
			CustomMinimumSize = new Vector2(48, 48),
			TooltipText = dica,
			IconAlignment = HorizontalAlignment.Center,
			ExpandIcon = true,
			ToggleMode = true,
		};
		if (icone != null) b.Icon = icone;
		else b.Text = dica.Length > 4 ? dica[..4] : dica;
		return b;
	}

	/// <summary>Monta as duas grades UMA VEZ. Trocar de raca so muda quais ficam visiveis.</summary>
	private void MontarGrades()
	{
		if (_cat == null) return;

		foreach ((string nome, string? sprite) in _cat.Cabelos)
		{
			Button b = BotaoIcone(sprite == null ? null : Miniatura(sprite), nome);
			string estilo = nome;
			b.Pressed += () =>
			{
				_visual.Cabelo = estilo;
				MarcarSelecionado(_gradeCabelo, b);
				Repintar();
			};
			_gradeCabelo.AddChild(b);
		}

		foreach (string peca in _cat.Roupas)
		{
			Button b = BotaoIcone(Miniatura(peca), NomeDeArquivo(peca));
			string alvo = peca;
			b.Pressed += () =>
			{
				if (_visual.Roupa.RemoveAll(p => p.Caminho == alvo) > 0) { b.ButtonPressed = false; Repintar(); return; }
				if (_visual.Roupa.Count >= Appearance.MaxRoupa)
				{
					b.ButtonPressed = false;
					_erro.Text = $"o guarda-roupa leva no maximo {Appearance.MaxRoupa} pecas";
					return;
				}
				_visual.Roupa.Add(new PecaDeRoupa(alvo));
				b.ButtonPressed = true;
				_erro.Text = "";
				Repintar();
			};
			_gradeRoupa.AddChild(b);
		}
	}

	/// <summary>Grade de escolha unica: so um botao fica apertado.</summary>
	private static void MarcarSelecionado(Node grade, Button escolhido)
	{
		foreach (Node n in grade.GetChildren())
			if (n is Button b) b.ButtonPressed = ReferenceEquals(b, escolhido);
	}

	/// <summary>
	/// Uma linha de "colorir isto?": a caixa liga a cor, o seletor escolhe qual. Desligada
	/// (o padrao) o sprite fica com a cor que o artista desenhou.
	/// </summary>
	private static (HBoxContainer, CheckBox, ColorPickerButton) LinhaDeCor(string rotulo, Color inicial, Action aoMudar)
	{
		var h = new HBoxContainer();
		var caixa = new CheckBox { Text = rotulo, CustomMinimumSize = new Vector2(190, 0) };
		var cor = new ColorPickerButton
		{
			Color = inicial,
			CustomMinimumSize = new Vector2(0, 26),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			Disabled = true,
			EditAlpha = false,
		};
		caixa.Toggled += on => { cor.Disabled = !on; aoMudar(); };
		cor.ColorChanged += _ => aoMudar();
		h.AddChild(caixa);
		h.AddChild(cor);
		return (h, caixa, cor);
	}

	private static HBoxContainer Linha(string rotulo, Control campo)
	{
		var h = new HBoxContainer();
		Label r = Tema.Rotulo(rotulo);
		r.CustomMinimumSize = new Vector2(130, 0);
		r.VerticalAlignment = VerticalAlignment.Center;
		h.AddChild(r);
		campo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		h.AddChild(campo);
		return h;
	}

	private static string NomeDeArquivo(string res) =>
		res.Substring(res.LastIndexOf('/') + 1).Replace(".tres", "").Replace('_', ' ');

	private static string NomeBonito(string s) => s switch
	{
		"Earth" => "Terra", "Vegeta" => "Vegeta", "Namek" => "Namek",
		"Heaven" => "Outro Mundo", "Hell" => "Inferno",
		"Icer" => "Frost Demon", "Saibaman" => "Saibamen",
		"Kanassa" => "Kanassa-Jin", "BioAndroid" => "Bio-Android",
		"SpiritDoll" => "Spirit Doll", "Halfbreed" => "Half-Saiyan",
		_ => s,
	};

	private static Rgb DeCor(Color c) => new((byte)(c.R * 255), (byte)(c.G * 255), (byte)(c.B * 255));

	// =====================================================================
	// REACAO AS ESCOLHAS
	// =====================================================================
	/// <summary>Trocar de mundo troca QUEM pode te gerar: a grade de racas e refeita.</summary>
	private void EscolherPlaneta(string planeta)
	{
		_ficha.Planet = planeta;

		foreach (Node n in _gradeRaca.GetChildren()) n.QueueFree();
		string[] racas = CharacterDraft.RacasDoPlaneta(planeta);

		for (int i = 0; i < racas.Length; i++)
		{
			string raca = racas[i];
			Button b = CartaoGrande(IconeDeRaca(raca), NomeBonito(raca), Descrever(raca));
			b.Pressed += () => { MarcarSelecionado(_gradeRaca, b); EscolherRaca(raca); };
			_gradeRaca.AddChild(b);
			if (i == 0) b.ButtonPressed = true;
		}

		EscolherRaca(racas.Length > 0 ? racas[0] : "");
	}

	private void EscolherRaca(string raca)
	{
		_ficha.Race = raca;

		// LINHAGEM: so nas tres racas em que o jogador escolhe de verdade. A pagina inteira e
		// pulada quando nao ha o que escolher (ver `Etapa.Cabe`), entao aqui so se monta a lista.
		foreach (Node n in _caixaLinhagem.GetChildren()) n.QueueFree();
		string[] opcoes = CharacterDraft.EscolhasDeClasse(raca);
		_ficha.ChosenClass = opcoes.Length > 0 ? opcoes[0] : "";

		for (int i = 0; i < opcoes.Length; i++)
		{
			string o = opcoes[i];
			Button b = CartaoLargo(o, DescreverLinhagem(o));
			b.Pressed += () => { MarcarSelecionado(_caixaLinhagem, b); _ficha.ChosenClass = o; };
			_caixaLinhagem.AddChild(b);
			if (i == 0) b.ButtonPressed = true;
		}

		// AS RACAS SEM GENERO CRAVAM MASCULINO -- a etapa some, e o valor tem que ir junto.
		if (!_ficha.TemGenero) _generoEscolhido = "Male";

		// O BERCO E FUNCAO DA RACA, entao ele se refaz junto com ela -- inclusive pra ZERAR o pedido
		// de vizinho quando a raca nova nao tem vizinhanca. Ver `AtualizarBerco`.
		AtualizarBerco();

		// A FAIXA DE IDADE E DA RACA. Trocar de raca reaperta o teto, e um valor que ficou acima
		// dele desce sozinho: deixar 200 numa raca que vive 15 anos so adiaria o erro pro fim.
		int teto = Envelhecimento.IdadeMaximaNaCriacao(raca);
		_idade.MaxValue = teto;
		if (_idade.Value > teto) _idade.Value = teto;
		double vida = Envelhecimento.TempoDeVida(raca);
		_notaDaIdade.Text = Envelhecimento.NaoEnvelhece(raca)
			? "esta raça não envelhece: o corpo nunca declina."
			: $"o auge desta raça é aos {teto} anos, e ela vive cerca de {vida:0}. "
			  + "Antes do auge o corpo ainda está crescendo, e depois dele começa a declinar.";

		// O FROST DEMON NAO PASSA PELA APARENCIA COMUM: os corpos dele sao as formas.
		//
		// A LISTA E REFEITA AQUI, e nao uma vez na montagem da pagina. Sair do Frost Demon pra
		// outra raca LIMPA a lista (senao um Humano viajaria carregando tres corpos de Icer), e
		// voltar pra ele encontraria a grade vazia -- ela le `FormasDeFrost[slot]` pra saber o que
		// marcar. Preencher na troca de raca cobre a ida e a volta.
		if (Jandirus.Core.Races.FormasDeFrost.EhFrost(raca))
		{
			_visual.FormasDeFrost.Clear();
			foreach (int degrau in Jandirus.Core.Races.FormasDeFrost.DegrausDe(
						 Jandirus.Core.Races.FormasDeFrost.ClasseNormal))
				_visual.FormasDeFrost.Add(Jandirus.Core.Races.FormasDeFrost.PadraoDoNormal(degrau));

			_slotDeForma = 0;
			DesenharGradeDeFormas();
		}
		else _visual.FormasDeFrost.Clear();

		AoTrocarGenero();
		_descricao.Text = Descrever(raca);
	}

	private static string DescreverLinhagem(string l) => l switch
	{
		"Saiyan" => "O sangue comum de Vegeta.",
		"Primal Saiyan" => "O caminho do Oozaru -- e o único que chega ao Super Saiyajin 4.",
		"Warrior clan" => "Os melhores lutadores de Namek.",
		"Demon clan" => "Conjuradores agressivos, na linhagem do Rei Piccolo.",
		"Dragon clan" => "Curandeiros e criadores, com regeneração incomparável.",
		"New Generation" => "Nascido depois da guerra, sem as cicatrizes dela.",
		"Future Lineage" => "Veio de um tempo que ainda não aconteceu.",
		"Prodigial" => "Nasceu pronto -- e isso assusta os mais velhos.",
		_ => "",
	};

	/// <summary>
	/// O RETRATO DE CADA RACA -- o corpo dela, e nao um icone abstrato. E o que o original faz
	/// (`CreationUI.dm:328-338` monta o card com o proprio `icon` do `/obj/race`).
	///
	/// Pergunta ao catalogo o corpo masculino da raca; quem nao tem entrada propria cai no corpo
	/// padrao, que e exatamente o que o jogo desenharia mesmo. O Frost Demon e a excecao util: ele
	/// nao tem corpo no catalogo, e o retrato dele e a primeira forma.
	/// </summary>
	private Texture2D? IconeDeRaca(string raca)
	{
		// O RETRATO TEM TABELA PROPRIA, e nao sai do catalogo de corpos. O catalogo so conhece as
		// racas que TEM passo de escolha de corpo, e seis das sete de Vegeta nao tem -- por isso
		// Saiyan, Tsujin, Saibamen, Heran, Meta e Alien apareciam como o mesmo humano palido. Ver
		// `IconesDeRaca`.
		if (IconesDeRaca.De(raca) is { } proprio) return Miniatura(proprio);

		string? corpo = _cat?.CorposDe(raca).Para("Male").FirstOrDefault();
		return corpo == null ? null : Miniatura(corpo);
	}

	/// <summary>Trocar de raca ou de sexo troca a lista de corpos -- e o que a tela oferece.</summary>
	private void AoTrocarGenero()
	{
		string genero = Genero();
		BodyOptions b = _cat?.CorposDe(_ficha.Race) ?? new BodyOptions();

		// quando a raca tem TONS (pele clara/morena/negra, verdes do Namekuseijin) o rotulo e
		// o tom; senao e o proprio corpo, que ai e escolha de silhueta
		List<string> rotulos = b.Tons.Count > 0
			? b.Tons
			: [.. b.Para(genero).Select(NomeDeArquivo)];

		_corpo.Clear();
		foreach (string r in rotulos) _corpo.AddItem(r);
		if (_corpo.ItemCount > 0) _corpo.Selected = 0;
		_visual.Corpo = 0;
		_visual.Tom = 0;
		_linhaCorpo.Visible = _corpo.ItemCount > 1;

		_linhaCorPele.Visible = b.CorLivre;
		if (!b.CorLivre) { _tingirPele.ButtonPressed = false; _visual.CorPele = null; }

		// cabelo: seis racas nao tem, e o Saiyajin nao colore (o sprite dele JA e preto,
		// com realce -- tingir de preto apagaria o desenho)
		bool temCabelo = VisualCatalog.TemCabelo(_ficha.Race);
		_blocoCabelo.Visible = temCabelo;
		_linhaCorCabelo.Visible = temCabelo && !VisualCatalog.CabeloNatural(_ficha.Race);
		if (!temCabelo) _visual.Cabelo = "Bald";
		if (VisualCatalog.CabeloNatural(_ficha.Race))
		{
			_tingirCabelo.ButtonPressed = false;
			_visual.CorCabelo = null;
		}

		Repintar();
	}

	private string Genero() => !_ficha.TemGenero ? "Male" : _generoEscolhido;

	/// <summary>A PREVIA AO VIVO: remonta as camadas do boneco com a ficha atual.</summary>
	private void Repintar()
	{
		RedesenharRoupa();
		Previa();
	}

	/// <summary>
	/// So o BONECO, sem remontar a lista de pecas.
	///
	/// Existe pro seletor de cor: `Repintar` reconstroi a lista inteira, e reconstruir a linha
	/// embaixo do seletor que o jogador esta arrastando fecharia o seletor a cada pixel.
	/// </summary>
	private void Previa()
	{
		if (_cat == null) return;

		// ============================ O FROST DEMON MOSTRA A FORMA QUE ESTA SENDO EDITADA ============================
		// `VisualCatalog.CorpoSprite` devolve SEMPRE a forma base -- e certo pro jogo (e nela que o
		// personagem anda), e errado pra esta tela: quem abre a aba "2ª Evolução" e clica num corpo
		// via o boneco continuar na base, como se o clique nao tivesse funcionado.
		//
		// A COPIA E LOCAL, e nao um estado novo: troca-se a base pelo slot aberto SO pra desenhar, e
		// a escolha de verdade continua em `_visual`. Mexer no `_visual` faria a previa reordenar a
		// lista que vai pro servidor.
		Appearance mostrar = _visual;
		if (Jandirus.Core.Races.FormasDeFrost.EhFrost(_ficha.Race)
			&& _slotDeForma > 0 && _slotDeForma < _visual.FormasDeFrost.Count)
		{
			mostrar = _visual.Copiar();
			mostrar.FormasDeFrost[0] = _visual.FormasDeFrost[_slotDeForma];
		}

		_boneco.Vestir(_cat, mostrar, _ficha.Race, Genero());
		_boneco.SetMotion(_facingAtual, false);
	}

	/// <summary>O que da pra dizer sem entregar a classe (que e sorteio cego).</summary>
	private string Descrever(string raca)
	{
		RaceProto? p = _racas?.Get(raca);
		if (p == null) return "";

		double bp = p.MiscStat("Starting BP");
		string classes = p.ClassSpread.Count > 0
			? $"Linhagens possiveis: {string.Join(", ", p.ClassSpread.Select(c => c.Classe))}."
			: "Sem variacao de linhagem.";
		string escolhe = CharacterDraft.EscolhasDeClasse(raca).Length > 0
			? " Voce escolhe a sua."
			: " A sua e revelada quando o combate mostrar do que voce e feito.";

		// SEM O NUMERO. Eu tinha decidido manter ("e ficha de RACA, nao BP de ninguem"), mas o
		// dono pediu TODA referencia de BP escondida, e ele tem razao pelo lado que importa: o
		// jogador nao distingue "poder inicial da especie" de "meu poder" -- ve um numero de
		// poder de luta na tela e aprende que numero de poder e coisa que se le.
		//
		// A FAIXA continua servindo pra comparar raca com raca, que era o unico uso legitimo.
		string inicio = bp >= 400 ? "Comeca MUITO forte."
					   : bp >= 100 ? "Comeca forte."
					   : bp >= 30 ? "Comeca mediano."
					   : "Comeca fraco.";
		return $"{inicio} {classes}{escolhe}";
	}

	private void Confirmar()
	{
		_ficha.Name = _nome.Text.Trim();
		_ficha.Age = (int)_idade.Value;
		_ficha.Gender = Genero();
		_ficha.Backstory = _historia.Text.Trim();

		// A LINHAGEM SO VALE NAS RACAS QUE ESCOLHEM. Ela e escrita quando a raca muda, mas trocar
		// de raca DEPOIS de ter passado pela etapa deixaria a escolha antiga colada -- e o servidor
		// recusa linhagem em raca que nao escolhe ("essa raca nao escolhe linhagem").
		if (CharacterDraft.EscolhasDeClasse(_ficha.Race).Length == 0) _ficha.ChosenClass = "";

		string erro = _ficha.Validar();
		if (erro.Length > 0) { _erro.Text = erro; return; }

		// sanear ANTES de mandar: o servidor vai rodar a mesma conferencia, e e melhor o
		// jogador ver o ajuste aqui do que descobrir depois que a roupa nao veio
		_cat?.Sanear(_visual, _ficha.Race, _ficha.Gender);

		Visible = false;
		Pronto?.Invoke(_ficha, _visual);
	}

	/// <summary>
	/// Volta a aparecer. A TELA NAO E DESTRUIDA ao sair -- e o que tira a travadinha de abrir.
	///
	/// Montar esta tela custa alguns milissegundos: dois catalogos em JSON, uns quarenta
	/// Controls e o boneco da previa, que puxa os SpriteFrames de corpo, cabelo e roupa do
	/// disco. Feito no clique, isso e um engasgo bem no quadro em que o jogador esta olhando.
	/// A tela e montada ANTES, escondida, enquanto ele le a lista de slots -- e abrir vira
	/// trocar um booleano.
	/// </summary>
	public void Reabrir()
	{
		// ZERA O GUARDA-ROUPA. Esta tela nao e destruida (e o que tira a travadinha de abrir) e a
		// `Appearance` dela e um campo unico: sem isto, criar um segundo personagem comecava com a
		// roupa, as cores e os botoes marcados do primeiro.
		_visual.Roupa.Clear();
		foreach (Node n in _gradeRoupa.GetChildren()) if (n is Button b) b.ButtonPressed = false;
		RedesenharRoupa();

		// VOLTA PRA PRIMEIRA ETAPA. A tela nao e destruida entre um personagem e outro, entao sem
		// isto o segundo comecaria na pagina em que o primeiro terminou -- "QUEM E VOCE?" com tudo
		// ja preenchido, e as escolhas de mundo e raca do anterior valendo em silencio.
		_passo = 0;
		MostrarEtapa();

		_erro.Text = "";
		Visible = true;
	}
}
