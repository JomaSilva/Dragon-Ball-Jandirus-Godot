using Godot;
using Jandirus.Core.Items;

namespace Jandirus.Client;

/// <summary>
/// A TECLA I: a mochila.
///
/// ============================ UMA GRADE, E NAO UMA LISTA ============================
/// O original mostra o inventario como linhas de texto numa aba HTML ("Maca x3", com botoes ao
/// lado). Funciona e e feio -- e pior, e lento de ler: achar uma coisa entre trinta linhas de texto
/// exige LER trinta linhas.
///
/// O dono pediu grade com icone: "vai ter slots e cada item vai ficar em um slot (o icone de cada
/// um la) se tiver mais de um item igual vai ficar a foto do item com um numero logo embaixo
/// mostrando a quantidade". Reconhecer um desenho e instantaneo; ler nao e.
/// ====================================================================================
///
/// ============================ ELA NAO DECIDE NADA ============================
/// As acoes de cada item vem do <see cref="CatalogoDeItens"/>, que o servidor le tambem, e cada
/// botao manda um verbo `item_<acao>`. Quem confere se voce TEM o item e se a acao PERTENCE a ele
/// e o servidor -- esconder um botao e conveniencia, nao permissao.
/// =============================================================================
/// </summary>
public partial class TelaDeInventario : CanvasLayer
{
	/// <summary>Quantas colunas a grade tem. Seis por cinco fecham os 30 slots do original.</summary>
	private const int Colunas = 6;

	/// <summary>O lado de cada slot, em pixels. O sprite tem 32; o dobro se le sem esforco.</summary>
	private const int LadoDoSlot = 64;

	/// <summary>A tela viva, ou nula antes do mundo. SO PRA BANCADA.</summary>
	public static TelaDeInventario? Instancia { get; private set; }

	private Control _raiz = null!;
	private PanelContainer _painel = null!;
	private GridContainer _grade = null!;
	private Label _contagem = null!;
	private Label _titulo = null!, _rotuloDoCorpo = null!;

	/// <summary>
	/// A FILEIRA DA CARTEIRA, embaixo da grade (dono, 2026-10-13). Na MINHA mochila sao os dois botoes de largar zeni
	/// e recursos no chao; na mochila de um caido, os dois de levar o que ele carrega. Ver <see cref="Redesenhar"/>.
	/// </summary>
	private HBoxContainer _carteira = null!;

	/// <summary>
	/// ============================ A MOCHILA DE QUEM ESTA CAIDO (dono, 2026-10-13) ============================
	/// *"ao analisar uma pessoa ou npc nocauteado pra pegar os itens dela, deveria abrir o inventario dela e nao um
	/// menu onde vc seleciona por botoes oq vc quer, deveria ter o mesmo GUI do inventario (botao i) porem com oq tem
	/// no inventario da pessoa nocauteada, e tb ter como roubar recursos e zeni de nocauteados (npc ou player)"*.
	///
	/// E ESTA MESMA TELA, COM OUTRA LISTA: a fileira de cima mostra o que ele veste (as linhas marcadas do saque,
	/// `Saque.NoCorpo`), a grade mostra o que ele carrega, e a fileira da carteira oferece o zeni e os recursos dele.
	/// Clicar LEVA -- um por clique, como o `Steal()` do original --, e o servidor responde com a lista nova depois de
	/// cada coisa levada (`S2C.Saque`), entao a tela se redesenha sem guardar foto nenhuma da mochila alheia.
	///
	/// NULO = a tela mostra a MINHA mochila. Quem a abre na de outro e so o pacote (<see cref="AbrirSaque"/>): a
	/// tecla I e o botao de fechar voltam sempre pra minha.
	/// ============================================================================================================
	/// </summary>
	private MochilaAlheia? _saque;

	private readonly record struct MochilaAlheia(int Caido, string Nome, List<Pilha> Leva, double Zeni, double Recursos)
	{
		/// <summary>(v0.2.1) O id NEGATIVO e o de um DEPOSITO (bau, geladeira, base) -- ver `GameServer.MandarODeposito`.</summary>
		public bool EhDeposito => Caido < 0;
	}

	/// <summary>
	/// O DEPOSITO ABERTO (v0.2.1) -- o nome do bau, da geladeira ou da base cuja tela esta (ou esteve agora ha pouco)
	/// aberta. Enquanto houver um, cada item da MINHA mochila ganha o botao "Guardar no baú", e a fileira de baixo o
	/// botao de voltar pra ele. Nulo = nenhum.
	/// </summary>
	private string? _deposito;

	/// <summary>
	/// A FILEIRA "VESTINDO": as pecas que estao no corpo agora, uma por vaga (dono, 2026-10-08: *"a roupa q vc
	/// escolhe ao criar deveria aparecer no inventario pra ter a opcao de tirar ela e trocar por outro"*).
	///
	/// ELA NAO E PARTE DA GRADE DE 30, e de proposito: a peca vestida nao ocupa espaco da mochila -- ela mora
	/// na aparencia (ver `RoupaGuardada`). Clicar numa oferece TIRAR, e a peca tirada cai na grade de baixo
	/// como um item comum, com VESTIR. As vagas sao sempre as quatro (`Appearance.MaxRoupa`), cheias ou nao,
	/// pelo mesmo motivo dos 30 slots: a tela nao pode dancar a cada troca.
	/// </summary>
	private HBoxContainer _vestindo = null!;

	/// <summary>O painel de acoes do item clicado. Nulo = nenhum item aberto.</summary>
	private PanelContainer? _acoes;

	public override void _Ready()
	{
		Layer = 4;   // a mesma do menu de interacao: sao as duas telas "do mundo"
		Instancia = this;
		Montar();

		if (GameClient.Instance is { } cli)
		{
			cli.MochilaMudou += AoMudarMochila;
			cli.CarteiraMudou += AoMudarMochila;   // a fileira da carteira mostra os dois saldos
			cli.SaqueAberto += AbrirSaque;
			cli.AtributosRecebidos += AoMudarAtributos;   // (2026-10-18) qual peso esta vestido, a % guardada e os degraus do corpo chegam pela ficha lenta
		}
	}

	/// <summary>Solta as assinaturas -- ver a nota no `MenuJogo._Ready`: o `GameClient` sobrevive ao logout.</summary>
	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		if (GameClient.Instance is not { } cli) return;
		cli.MochilaMudou -= AoMudarMochila;
		cli.CarteiraMudou -= AoMudarMochila;
		cli.SaqueAberto -= AbrirSaque;
		cli.AtributosRecebidos -= AoMudarAtributos;
	}

	private void AoMudarMochila() { if (_raiz.Visible) Redesenhar(); }

	/// <summary>O que a ficha lenta dizia dos pesos quando a grade foi desenhada: qual esta vestido, a % guardada e os degraus do corpo.</summary>
	private string _pesosDesenhados = "";

	/// <summary>(2026-10-18) O que a ficha lenta diz dos pesos: com a mochila aberta e um dado novo (os rotulos "Equipar (N%)" e "Ate o limite"), redesenha.</summary>
	private static string PesosDaFicha(Jandirus.Net.Protocol.AtributosState a) => $"{a.PesoVestido}|{a.PesoPct}|{a.PesoDegrausDoCorpo}";

	private void AoMudarAtributos(Jandirus.Net.Protocol.AtributosState a)
	{
		if (_raiz.Visible && PesosDaFicha(a) != _pesosDesenhados) Redesenhar();
	}

	private void Montar()
	{
		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(16);
		centro.AddChild(painel);
		_painel = painel;

		var caixa = new VBoxContainer();
		caixa.AddThemeConstantOverride("separation", 8);
		painel.AddChild(caixa);

		// (o texto do titulo e o da fileira do corpo sao escritos no `Redesenhar`: a tela serve a minha mochila e a de um caido)
		_titulo = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_titulo.AddThemeFontSizeOverride("font_size", 22);
		caixa.AddChild(_titulo);

		_contagem = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_contagem.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_contagem.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(_contagem);
		caixa.AddChild(new HSeparator());

		_rotuloDoCorpo = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_rotuloDoCorpo.AddThemeColorOverride("font_color", Tema.TextoFraco);
		_rotuloDoCorpo.AddThemeFontSizeOverride("font_size", 12);
		caixa.AddChild(_rotuloDoCorpo);

		_vestindo = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_vestindo.AddThemeConstantOverride("separation", 6);
		caixa.AddChild(_vestindo);
		caixa.AddChild(new HSeparator());

		_grade = new GridContainer { Columns = Colunas };
		_grade.AddThemeConstantOverride("h_separation", 6);
		_grade.AddThemeConstantOverride("v_separation", 6);
		caixa.AddChild(_grade);

		_carteira = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_carteira.AddThemeConstantOverride("separation", 8);
		caixa.AddChild(_carteira);

		// O TEXTO LE O REGISTRO: quem religou a mochila pra outra tecla tem que ler a tecla DELE
		// aqui, senao o botao ensina errado justamente quem mudou.
		var fechar = new Button { Text = $"Fechar ({Teclas.NomeDaAcao("ui_mochila")} ou Esc)" };
		fechar.Pressed += Fechar;
		caixa.AddChild(fechar);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv020`): o Esc da mochila aberta e ouvido no `_UnhandledInput` -- onde o menu de
	/// pausa o recebe ANTES (ele e o ultimo da arvore) e abre por cima, com a mochila aberta atras. Era onde esta tela
	/// o ouvia ate 2026-10-12, com o botao dizendo "Fechar (I ou Esc)". Lido pelas duas portas de entrada. Falso em
	/// jogo, sempre.
	/// </summary>
	public static bool EscSoDepoisDaPausaDeTeste;

	/// <summary>
	/// ============================ O ESC COM A MOCHILA ABERTA E DA MOCHILA ============================
	/// Ele e ouvido AQUI, no `_Input`, e nao no `_UnhandledInput` junto da tecla I -- pelo motivo escrito no
	/// `MenuDeInteracao._Input`: o menu de pausa escuta a mesma tecla no `_UnhandledInput` e e o ultimo node da arvore
	/// (`MoveChild(pm, -1)` no `Boot`), entao la ele a recebia ANTES deste node. Com a mochila aberta o Esc abria a
	/// PAUSA por cima e a mochila continuava aberta atras -- e o botao daqui diz "Fechar (I ou Esc)". O `_Input` roda
	/// antes: a coisa mais na frente da tela responde primeiro, e a pausa so abre quando nao ha mais nada pra fechar.
	///
	/// UMA CAMADA POR VEZ: com o teclado numerico por cima (o ajuste dos pesos), o Esc fecha o TECLADO e a mochila
	/// fica. E com a grade da armadura por cima quem responde e a grade: o node dela vem DEPOIS deste na arvore (ver o
	/// `Boot`), entao o `_Input` dela roda antes e consome a tecla.
	///
	/// QUEM ESTA DIGITANDO E DONO DO ESC (`Foco.Digitando`, a mesma porta do `MenuDeInteracao`): a frase do chat
	/// desiste por ele. E a guarda e obrigatoria, e nao cortesia: o chat e o primeiro node do mundo, entao o `_Input`
	/// dele roda DEPOIS deste -- sem ela a mochila fecharia, consumiria a tecla e a frase ficaria aberta.
	///
	/// A PAUSA ABERTA E DELA: se ela ja esta na tela, e ela que esta na frente -- o Esc segue e a fecha.
	///
	/// SEM O PORTAO DO EMBATE (`Foco.AtalhosMudos`), que continua valendo pra tecla I logo abaixo: aquele portao cala
	/// o que ABRE coisa por cima da disputa. Fechar nunca e bloqueado (a mesma regra escrita no `PauseMenu`).
	/// =================================================================================================
	/// </summary>
	public override void _Input(InputEvent evento)
	{
		if (!EscSoDepoisDaPausaDeTeste) FecharComOEsc(evento);
	}

	public override void _UnhandledInput(InputEvent evento)
	{
		if (EscSoDepoisDaPausaDeTeste && FecharComOEsc(evento)) return;

		// O "I" E UMA DAS LETRAS QUE O EMBATE SORTEIA -- ver `Foco.AtalhosMudos`. Sem esta troca,
		// responder ao quick time event abria a mochila por cima dele.
		if (Foco.AtalhosMudos) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k) return;

		// (O ESC da mochila aberta nao e daqui: ele e ouvido no `_Input`, antes do menu de pausa. Ver acima.)

		// PELO REGISTRO -- ver a mesma linha em `MenuDeInteracao` e o cabecalho do `Teclas`.
		if (!Teclas.Bate("ui_mochila", k)) return;

		if (_raiz.Visible) Fechar(); else Abrir();
		GetViewport().SetInputAsHandled();
	}

	/// <summary>O Esc da mochila aberta: fecha a camada da frente e consome a tecla. Falso se a tecla nao era dela.</summary>
	private bool FecharComOEsc(InputEvent evento)
	{
		if (!_raiz.Visible || Foco.Digitando) return false;
		if (evento is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return false;
		if (PauseMenu.Instancia is { Aberto: true }) return false;

		if (_teclado != null) FecharTeclado();
		else Fechar();
		GetViewport().SetInputAsHandled();
		return true;
	}

	/// <summary>Abre a mochila. Publico pro robo diretor (`RoboDoTrailer`), que nao aperta tecla.</summary>
	public void Abrir() { _saque = null; _deposito = null; _raiz.Visible = true; Redesenhar(); }

	public void Fechar()
	{
		FecharAcoes();
		FecharTeclado();
		_raiz.Visible = false;
		_saque = null;   // a proxima vez que a tecla I abrir, e a MINHA mochila
		_deposito = null;
	}

	/// <summary>
	/// O `S2C.Saque` CHEGOU: a tela abre (ou se redesenha) na mochila do caido. O id ZERO manda fechar -- ele acordou,
	/// ou eu me afastei --, e so fecha se a tela estava mesmo na mochila de alguem: a minha, aberta pela tecla I, fica.
	/// </summary>
	private void AbrirSaque(int caido, string nome, List<Pilha> leva, double zeni, double recursos)
	{
		if (caido == 0)
		{
			if (_saque != null) Fechar();
			return;
		}

		FecharTeclado();
		_saque = new MochilaAlheia(caido, nome, leva, zeni, recursos);
		_deposito = _saque.Value.EhDeposito ? nome : null;
		_raiz.Visible = true;
		Redesenhar();
	}

	private void Redesenhar()
	{
		_pesosDesenhados = GameClient.Instance is { } cli ? PesosDaFicha(cli.Atributos) : "";
		FecharAcoes();
		foreach (Node n in _grade.GetChildren()) n.QueueFree();
		foreach (Node n in _vestindo.GetChildren()) n.QueueFree();
		foreach (Node n in _carteira.GetChildren()) n.QueueFree();

		if (_saque is { } alheia) { DesenharAMochilaAlheia(alheia); return; }

		_titulo.Text = _deposito != null ? $"MOCHILA -- guardar em {_deposito.ToUpperInvariant()}" : "MOCHILA";
		_rotuloDoCorpo.Text = "VESTINDO";
		Inventario inv = GameClient.Instance?.Mochila ?? new Inventario();
		_contagem.Text = $"{inv.Ocupados} de {Inventario.Slots} espaços";

		// A FILEIRA DO QUE ESTA NO CORPO -- ver `_vestindo`. A lista vem no MESMO pacote da mochila.
		IReadOnlyList<Jandirus.Core.Appearance.PecaDeRoupa> noCorpo = GameClient.Instance?.Vestindo ?? [];
		for (int i = 0; i < Jandirus.Core.Appearance.Appearance.MaxRoupa; i++)
		{
			if (i < noCorpo.Count) _vestindo.AddChild(SlotVestido(RoupaGuardada.De(noCorpo[i])));
			else _vestindo.AddChild(SlotVazio());
		}

		// A GRADE TEM SEMPRE OS 30 SLOTS, cheios ou nao. Desenhar so o que existe faria a caixa
		// mudar de tamanho a cada maca colhida, e a tela inteira dancaria embaixo do mouse.
		for (int i = 0; i < Inventario.Slots; i++)
		{
			if (i < inv.Pilhas.Count) _grade.AddChild(SlotCheio(inv.Pilhas[i]));
			else _grade.AddChild(SlotVazio());
		}

		// A CARTEIRA: largar zeni e recursos no chao (dono, 2026-10-13: *"ter a opçao de dropar zeni e recursos"*). O
		// botao abre o teclado numerico e o verbo leva a quantia -- o `Drop()` do `obj/Zenni` (`Inventory.dm:124-142`),
		// que tambem pergunta quanto. Sem saldo o botao fica apagado, com o saldo escrito.
		double zeni = Math.Floor(GameClient.Instance?.Zeni ?? 0), recursos = Math.Floor(GameClient.Instance?.Recursos ?? 0);
		_carteira.AddChild(BotaoDaCarteira($"Largar zeni ({zeni:N0})", zeni >= 1, () => AbrirTecladoDeLargar(recursos: false)));
		_carteira.AddChild(BotaoDaCarteira($"Largar recursos ({recursos:N0})", recursos >= 1, () => AbrirTecladoDeLargar(recursos: true)));
		// (v0.2.1) COM UM DEPOSITO ABERTO, o caminho de volta pra tela dele.
		if (_deposito != null)
			_carteira.AddChild(BotaoDaCarteira($"Ver {_deposito}", true,
											   () => GameClient.Instance?.SendVerbo(Jandirus.Core.Tech.Deposito.VerboAbrir)));
	}

	/// <summary>
	/// A MESMA TELA, NA MOCHILA DE UM CAIDO -- ver <see cref="_saque"/>. A fileira de cima e o que ele VESTE (sai do
	/// corpo dele), a grade e o que ele CARREGA, e a carteira e o zeni e os recursos. As vagas sao sempre as mesmas
	/// quatro e trinta, cheias ou nao: a tela nao danca.
	/// </summary>
	private void DesenharAMochilaAlheia(MochilaAlheia s)
	{
		_titulo.Text = s.EhDeposito ? s.Nome.ToUpperInvariant() : $"MOCHILA DE {s.Nome.ToUpperInvariant()}";
		_rotuloDoCorpo.Text = s.EhDeposito ? "" : "NO CORPO";

		List<Pilha> doCorpo = [.. s.Leva.Where(p => Saque.EhDoCorpo(p.Id, out _))];
		List<Pilha> daMochila = [.. s.Leva.Where(p => !Saque.EhDoCorpo(p.Id, out _))];
		bool nada = s.Leva.Count == 0 && s.Zeni < 1 && s.Recursos < 1;
		_contagem.Text = s.EhDeposito ? (s.Leva.Count == 0 ? "vazio" : "clique no que quer pegar")
			: nada ? "nada que dê pra levar" : "clique no que quer levar -- quem estiver por perto vê";

		for (int i = 0; i < Jandirus.Core.Appearance.Appearance.MaxRoupa; i++)
			_vestindo.AddChild(i < doCorpo.Count ? SlotDeSaque(doCorpo[i]) : SlotVazio());
		for (int i = 0; i < Inventario.Slots; i++)
			_grade.AddChild(i < daMochila.Count ? SlotDeSaque(daMochila[i]) : SlotVazio());

		// (v0.2.1) O DEPOSITO NAO TEM CARTEIRA: no lugar dela, o botao que leva a MINHA mochila -- e la que se guarda.
		if (s.EhDeposito)
		{
			_carteira.AddChild(BotaoDaCarteira("Guardar algo da minha mochila", true,
											   () => { _saque = null; Redesenhar(); }));
			return;
		}

		// A CARTEIRA DELE: tudo de uma vez, como o `Steal()` do original faria (`Grabbing.dm:25-28`). Ver `Saque`.
		_carteira.AddChild(BotaoDaCarteira($"Levar o zeni ({s.Zeni:N0})", s.Zeni >= 1,
										   () => GameClient.Instance?.SendVerbo(Saque.Verbo, Saque.Zeni)));
		_carteira.AddChild(BotaoDaCarteira($"Levar os recursos ({s.Recursos:N0})", s.Recursos >= 1,
										   () => GameClient.Instance?.SendVerbo(Saque.Verbo, Saque.Recursos)));
	}

	/// <summary>Um botao da fileira da carteira. Apagado quando nao ha o que largar ou levar.</summary>
	private static Button BotaoDaCarteira(string texto, bool ligado, Action aoApertar)
	{
		var b = new Button { Text = texto, Disabled = !ligado };
		b.Pressed += aoApertar;
		return b;
	}

	/// <summary>
	/// UM SLOT DA MOCHILA ALHEIA: o mesmo desenho do meu (icone, e a quantidade no canto), e o clique LEVA -- manda o
	/// `saquear` com o id da linha, inteiro (a marca do corpo vai junto: e por ela que o servidor sabe de onde tirar).
	/// A peca que esta no corpo dele leva a borda de destaque, como a minha fileira "vestindo".
	/// </summary>
	private Control SlotDeSaque(Pilha linha)
	{
		bool doCorpo = Saque.EhDoCorpo(linha.Id, out string peca);
		ItemDef? def = CatalogoDeItens.Get(peca);
		string nome = def?.Nome ?? peca;

		var botao = new Button
		{
			CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot),
			TooltipText = doCorpo ? $"{nome}\nNo corpo. Clique para despir e levar pra sua mochila."
								  : $"{nome}\nClique para levar UM pra sua mochila.",
			ExpandIcon = true,
			IconAlignment = HorizontalAlignment.Center,
		};
		if (doCorpo) botao.AddThemeStyleboxOverride("normal", Tema.Caixa(Tema.PainelClaro, Tema.Destaque, 4));
		PorIcone(botao, def, RoupaGuardada.Ler(peca)?.Cor);
		// O ROTULO VAI EM METADADO: e por ele que a bancada acha o slot ("Tirar do corpo: X", "Pegar X").
		botao.SetMeta("saque", doCorpo ? $"Tirar do corpo: {nome}" : $"Pegar {nome}");
		botao.Pressed += () => GameClient.Instance?.SendVerbo(Saque.Verbo, linha.Id);
		return ComAQuantidade(botao, linha.Quantidade);
	}

	private static Control SlotVazio()
	{
		var p = new PanelContainer { CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot) };
		p.AddThemeStyleboxOverride("panel", Tema.Caixa(Tema.PainelClaro, Tema.Borda, 4));
		return p;
	}

	/// <summary>
	/// UM SLOT COM COISA: o icone grande, e a quantidade no canto de baixo.
	///
	/// O NUMERO SO APARECE COM MAIS DE UM. Um "1" em cada slot seria ruido em toda a grade, e a
	/// pergunta que ele responde ("quantos?") so existe quando ha mais de um.
	/// </summary>
	private Control SlotCheio(Pilha pilha)
	{
		ItemDef? def = CatalogoDeItens.Get(pilha.Id);

		var botao = new Button
		{
			CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot),
			TooltipText = def == null ? pilha.Id : $"{def.Nome}\n{def.Descricao}",
			ExpandIcon = true,
			IconAlignment = HorizontalAlignment.Center,
		};

		PorIcone(botao, def, RoupaGuardada.Ler(pilha.Id)?.Cor);

		botao.SetMeta("item", pilha.Id);   // o id do slot, pra bancada achar o icone (`IconeDoSlotDeTeste`)
		botao.Pressed += () => AbrirAcoes(pilha, botao);
		return ComAQuantidade(botao, pilha.Quantidade);
	}

	/// <summary>O slot com o numero da pilha no canto -- ou o proprio botao, quando e uma coisa so.</summary>
	private static Control ComAQuantidade(Button botao, int quantidade)
	{
		if (quantidade <= 1) return botao;

		// O NUMERO POR CIMA do botao, e nao ao lado: ele pertence ao slot, e um `Label` irmao
		// empurraria a grade inteira.
		var pilhaVisual = new Control { CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot) };
		pilhaVisual.AddChild(botao);
		botao.AnchorRight = 1;
		botao.AnchorBottom = 1;

		var n = new Label
		{
			Text = quantidade.ToString(),
			AnchorLeft = 0, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1,
			OffsetTop = -18, OffsetRight = -4,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		n.AddThemeFontSizeOverride("font_size", 14);
		n.AddThemeColorOverride("font_color", Tema.Destaque);
		// CONTORNO PRETO: o numero cai em cima do sprite, e sprite claro comeria um texto claro.
		n.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
		n.AddThemeConstantOverride("outline_size", 4);
		pilhaVisual.AddChild(n);

		return pilhaVisual;
	}

	/// <summary>O icone do item: o carregador unico (<see cref="Miniaturas"/>), o mesmo das abas Equip e Tech.</summary>
	private static Texture2D? Miniatura(ItemDef def) => Miniaturas.DoItemNaMochila(def);

	/// <summary>
	/// POE O DESENHO DO ITEM NO BOTAO DO SLOT -- e, se ele for uma peca de roupa TINGIDA, na cor dela.
	///
	/// A peca com cor nao pode usar o `Icon` do botao: o material que a tinge (`CharacterVisual.MaterialDeRoupa`,
	/// o mesmo shader do boneco) pintaria o botao INTEIRO, fundo e borda juntos. Ela vai num retangulo proprio
	/// por cima, que so desenha a folha. Sem desenho nenhum, sobram as quatro primeiras letras do nome.
	/// </summary>
	private static void PorIcone(Button botao, ItemDef? def, Jandirus.Core.Appearance.Rgb? corDaPeca)
	{
		Texture2D? icone = def == null ? null : Miniatura(def);
		if (icone == null) { botao.Text = def?.Nome[..Math.Min(4, def.Nome.Length)] ?? "?"; return; }
		if (CharacterVisual.MaterialDeRoupa(corDaPeca) is not { } tinta) { botao.Icon = icone; return; }

		botao.AddChild(new TextureRect
		{
			Texture = icone,
			Material = tinta,
			AnchorRight = 1, AnchorBottom = 1,
			OffsetLeft = 4, OffsetTop = 4, OffsetRight = -4, OffsetBottom = -4,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});
	}

	/// <summary>
	/// UMA VAGA DA FILEIRA "VESTINDO" COM PECA: o mesmo slot da grade, com a borda de destaque de quem esta em
	/// uso. Clicar abre o painel com a unica coisa que se faz com roupa no corpo -- tirar.
	/// </summary>
	private Control SlotVestido(RoupaGuardada roupa)
	{
		ItemDef? def = roupa.Ficha(CatalogoDeItens.Visual);
		var botao = new Button
		{
			CustomMinimumSize = new Vector2(LadoDoSlot, LadoDoSlot),
			TooltipText = $"{roupa.Nome}\nNo corpo. Clique para tirar e guardar na mochila.",
			ExpandIcon = true,
			IconAlignment = HorizontalAlignment.Center,
		};
		botao.AddThemeStyleboxOverride("normal", Tema.Caixa(Tema.PainelClaro, Tema.Destaque, 4));
		PorIcone(botao, def, roupa.Cor);
		// A IDENTIDADE VAI EM METADADO (o id da peca), como nas linhas da aba Equip: e por ele que a bancada
		// acha a vaga e confere o que a tela diz que o corpo veste.
		botao.SetMeta("vestida", roupa.Id);
		botao.Pressed += () => AbrirAcoesDeVestida(roupa, botao);
		return botao;
	}

	/// <summary>O painel de uma peca VESTIDA: o nome e o botao de tirar. O verbo leva o id da peca (folha + cor).</summary>
	private void AbrirAcoesDeVestida(RoupaGuardada roupa, Control perto)
	{
		FecharAcoes();

		_acoes = Tema.Painel1(8);
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0) };
		caixa.AddThemeConstantOverride("separation", 4);
		_acoes.AddChild(caixa);

		caixa.AddChild(new Label { Text = roupa.Nome, HorizontalAlignment = HorizontalAlignment.Center });
		caixa.AddChild(new HSeparator());

		var b = new Button { Text = Rotulo(RoupaGuardada.AcaoTirar) };
		b.Pressed += () =>
		{
			GameClient.Instance?.SendVerbo($"item_{RoupaGuardada.AcaoTirar}", roupa.Id);
			FecharAcoes();
		};
		caixa.AddChild(b);

		_raiz.AddChild(_acoes);
		_acoes.GlobalPosition = perto.GlobalPosition + new Vector2(LadoDoSlot + 6, 0);
	}

	/// <summary>
	/// AS OPCOES DO ITEM, num painel flutuante ao lado do slot.
	///
	/// FLUTUANTE e nao uma coluna fixa: ele nasce onde o dedo esta, que e onde o olho ja esta
	/// olhando. Um painel no canto da tela obrigaria a atravessar a grade inteira com a vista
	/// depois de cada clique.
	/// </summary>
	private void AbrirAcoes(Pilha pilha, Control perto)
	{
		FecharAcoes();
		ItemDef? def = CatalogoDeItens.Get(pilha.Id);
		if (def == null) return;

		_acoes = Tema.Painel1(8);
		var caixa = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0) };
		caixa.AddThemeConstantOverride("separation", 4);
		_acoes.AddChild(caixa);

		var nome = new Label { Text = def.Nome, HorizontalAlignment = HorizontalAlignment.Center };
		caixa.AddChild(nome);
		caixa.AddChild(new HSeparator());

		// "LARGAR" ENTRA AQUI e nao no catalogo: ela vale pra todo item, e repeti-la em cada linha
		// seria uma chance por item de esquecer. Ver `ItemDef.AcoesDoItem`.
		// (v0.2.1) COM UM DEPOSITO ABERTO, "guardar" vem primeiro: vale pra todo item, como "largar".
		IEnumerable<string> acoesDoMenu = def.AcoesDoItem.Append("largar");
		if (_deposito != null) acoesDoMenu = acoesDoMenu.Prepend(TechDeBolso.AcaoGuardar);
		foreach (string acao in acoesDoMenu)
		{
			string a = acao;
			var b = new Button { Text = Rotulo(a, def.Id) };
			b.Pressed += () =>
			{
				// (v0.2.1) A TECH DE BOLSO: o botao que PERGUNTA abre o teclado ou a caixa de texto, e a resposta viaja
				// colada no id (`Key/senha`), como o numero do ajuste dos pesos. O fabricador abre a aba Tech.
				if (TechDeBolso.BotaoDa(a, def.Id) is { } pergunta)
				{
					if (pergunta.Forma != Jandirus.Core.Tech.Interacoes.Forma.Direta) { AbrirPerguntaDaTech(def, a, pergunta); return; }
					if (a == TechDeBolso.AcaoFabricar)
					{
						Fechar();
						MenuJogo.Instancia?.Abrir();
						MenuJogo.Instancia?.IrPara("Tech");
					}
				}

				// AS ACOES QUE PEDEM UM NUMERO abrem o mesmo teclado do menu de interacao. O valor
				// viaja colado no id ("Weights/40") porque o canal de verbos tem um argumento so --
				// ver `GameServer.ComandoDeItem`.
				if (a == "ajustar") { AbrirTeclado(def); return; }

				// (v0.3.0) O "EQUIPAR" DOS PESOS veste a porcentagem guardada -- e, sem nenhuma escolhida ainda, pede o numero pelo
				// mesmo teclado do ajuste (que e quem a guarda). Ver `GameServer.VestirOsPesos`.
				if (a == "equipar" && PesoDeTreino.EhPeso(def.Id) && (GameClient.Instance?.Atributos.PesoPct ?? 0) == 0)
				{ AbrirTeclado(def); return; }

				// A ARTE DOS PESOS ABRE A MESMA GRADE DA ARMADURA, e o verbo sai de la -- ver `TelaDeArmadura.AbrirPraPesos`.
				if (a == PesoDeTreino.AcaoArte)
				{
					FecharAcoes();
					TelaDeArmadura.Instancia?.AbrirPraPesos(def.Id);
					return;
				}

				// A ARMADURA CRUA NAO MANDA VERBO AQUI: ela abre a grade de artes, e e o clique LA que manda
				// (`item_moldar Armor/<arte>`). Ver `TelaDeArmadura`.
				if (a == ArmaduraDeTech.AcaoMoldar)
				{
					FecharAcoes();
					TelaDeArmadura.Instancia?.Abrir();
					return;
				}

				// (v0.2.1) O PENTE E A TINTA DE OLHOS da bancada de criacao abrem a mesma grade: o penteado (e a cor) se
				// escolhem la, e e de la que o verbo sai. Ver `TelaDeArmadura.AbrirPraPentear` e `AbrirPraOsOlhos`.
				if (a == Jandirus.Core.Tech.BancadaDeCriacao.AcaoPentear)
				{
					FecharAcoes();
					TelaDeArmadura.Instancia?.AbrirPraPentear();
					return;
				}
				if (a == Jandirus.Core.Tech.BancadaDeCriacao.AcaoTingirOsOlhos)
				{
					FecharAcoes();
					TelaDeArmadura.Instancia?.AbrirPraOsOlhos();
					return;
				}

				// POSICIONAR NAO MANDA VERBO: ela poe a construcao na MAO e fecha o inventario. O
				// verbo sai depois, quando o jogador clicar no chao -- ver `TelaDeConstrucao`.
				// PLANTAR E A MESMA COISA com outra palavra: a semente vai pra mao e o clique no chao
				// escolhe onde a arvore nasce.
				if (a == Jandirus.Core.Items.CatalogoDeItens.AcaoPosicionar
					|| a == Jandirus.Core.Items.CatalogoDeItens.AcaoPlantar)
				{
					Fechar();
					TelaDeConstrucao.Instancia?.Segurar(def.Id);
					return;
				}

				GameClient.Instance?.SendVerbo($"item_{a}", def.Id);
				FecharAcoes();
			};
			caixa.AddChild(b);

			// ============================ A MELHORIA DOS PESOS TEM AS DUAS MOEDAS (dono, 2026-10-13) ============================
			// *"ao tentar melhorar o peso pelo inventario, so tem a opçao de zenni e nao de recurso, adicione essa opçao"*.
			// O servidor ja aceitava as duas (`GameServer.MelhorarOsPesos`) e a aba Equip ja oferecia as duas; aqui so
			// havia o botao do zeni. A moeda viaja colada no id, como o numero do ajuste ("Weights/recursos").
			// ==================================================================================================================
			// A PILHA SE LARGA INTEIRA TAMBEM -- o `Drop_All()` do original (`Grabbing.dm:236-239`). O botao de cima
			// larga UM; este, todos. A palavra viaja colada no id, como o numero do ajuste dos pesos ("Apple/tudo").
			if (a == "largar" && pilha.Quantidade > 1)
			{
				var tudo = new Button { Text = $"Largar tudo ({pilha.Quantidade})" };
				tudo.Pressed += () =>
				{
					GameClient.Instance?.SendVerbo("item_largar", $"{def.Id}/{Chao.PalavraDeTudo}");
					FecharAcoes();
				};
				caixa.AddChild(tudo);
			}

			// (v0.2.1) TODA MELHORIA PAGA DA TECH DE BOLSO TEM AS DUAS MOEDAS: o botao de cima paga em zeni, este em
			// recursos -- a palavra da moeda vai colada no id, como a dos pesos logo abaixo. Ver `TechDeBolso.Botao.Paga`.
			if (TechDeBolso.BotaoDa(a, def.Id) is { Paga: true } pago && !MelhoriaSoComZeniDeTeste)
			{
				var emRecursos = new Button { Text = pago.RotuloEm(Jandirus.Core.Economia.Moeda.Recursos) };
				emRecursos.SetMeta("moeda", Jandirus.Core.Economia.Recurso.PalavraDeRecursos);
				emRecursos.Pressed += () =>
				{
					GameClient.Instance?.SendVerbo($"item_{a}", $"{def.Id}/{Jandirus.Core.Economia.Recurso.PalavraDeRecursos}");
					FecharAcoes();
				};
				caixa.AddChild(emRecursos);
			}

			if (a == "melhorar" && !MelhoriaSoComZeniDeTeste)
			{
				var comRecursos = new Button { Text = RotuloDaMelhoriaDosPesos(def.Id, Jandirus.Core.Economia.Moeda.Recursos) };
				comRecursos.SetMeta("moeda", Jandirus.Core.Economia.Recurso.PalavraDeRecursos);
				comRecursos.Pressed += () =>
				{
					GameClient.Instance?.SendVerbo("item_melhorar", $"{def.Id}/{Jandirus.Core.Economia.Recurso.PalavraDeRecursos}");
					FecharAcoes();
				};
				caixa.AddChild(comRecursos);
			}

			// (2026-10-15) O LIMITE DO CORPO TAMBEM NAS DUAS MOEDAS, como a melhoria (o botao de cima paga em zeni).
			if (a == PesoDeTreino.AcaoAteOLimite && !MelhoriaSoComZeniDeTeste && MelhoriasAteOLimiteDe(def.Id) > 0)
			{
				var limiteEmRecursos = new Button { Text = RotuloDoLimiteDosPesos(def.Id, Jandirus.Core.Economia.Moeda.Recursos) };
				limiteEmRecursos.SetMeta("moeda", Jandirus.Core.Economia.Recurso.PalavraDeRecursos);
				limiteEmRecursos.Pressed += () =>
				{
					GameClient.Instance?.SendVerbo($"item_{PesoDeTreino.AcaoAteOLimite}", $"{def.Id}/{Jandirus.Core.Economia.Recurso.PalavraDeRecursos}");
					FecharAcoes();
				};
				caixa.AddChild(limiteEmRecursos);
			}
		}

		_raiz.AddChild(_acoes);
		_acoes.GlobalPosition = perto.GlobalPosition + new Vector2(LadoDoSlot + 6, 0);
	}

	/// <summary>
	/// O TECLADO PRA QUEM PEDE UM NUMERO -- hoje so os pesos, que se ajustam em porcentagem do
	/// proprio corpo. Ver `TecladoNumerico`, que e o mesmo widget do menu de interacao.
	/// </summary>
	private CenterContainer? _teclado;

	private void AbrirTeclado(ItemDef def)
	{
		FecharAcoes();
		FecharTeclado();

		// A PORCENTAGEM E DO PESO MAXIMO DESTE PESO (o que ja foi pago por ele, no id dele), e o titulo diz de quantos kg -- ver `PesoDeTreino`.
		double maximo = PesoDeTreino.MaximoDe(def.Id);
		var t = new TecladoNumerico($"{def.Nome} — % do peso máximo ({maximo:N0} kg)", 0, 100,
			v =>
			{
				GameClient.Instance?.SendVerbo("item_ajustar", $"{def.Id}/{v:0}");
				FecharTeclado();
			},
			FecharTeclado);

		_teclado = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_teclado.AddChild(t);
		_raiz.AddChild(_teclado);
	}

	private void FecharTeclado()
	{
		_teclado?.QueueFree();
		_teclado = null;
	}

	/// <summary>
	/// (v0.2.1) A PERGUNTA DE UM BOTAO DA TECH DE BOLSO: o teclado numerico ou a caixa de texto, pelo que a tabela
	/// diz (`TechDeBolso.BotaoDa`). A resposta vai colada no id do item, depois da barra.
	/// </summary>
	private void AbrirPerguntaDaTech(ItemDef def, string acao, TechDeBolso.Botao botao)
	{
		FecharAcoes();
		FecharTeclado();
		string titulo = botao.Pergunta.Length > 0 ? $"{def.Nome} — {botao.Pergunta}" : $"{def.Nome} — {botao.Rotulo}";
		Node pergunta = botao.Forma == Jandirus.Core.Tech.Interacoes.Forma.Numero
			? new TecladoNumerico(titulo, botao.Min, botao.Max,
				v => { GameClient.Instance?.SendVerbo($"item_{acao}", $"{def.Id}/{v:0}"); FecharTeclado(); }, FecharTeclado)
			: new CaixaDeTexto(titulo, "", (int)Math.Max(1, botao.Max),
				texto => { GameClient.Instance?.SendVerbo($"item_{acao}", $"{def.Id}/{texto}"); FecharTeclado(); }, FecharTeclado);

		_teclado = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_teclado.AddChild(pergunta);
		_raiz.AddChild(_teclado);
	}

	/// <summary>
	/// QUANTO LARGAR DA CARTEIRA: o teclado numerico, de 1 ate o que ha. O verbo leva a moeda e a quantia juntas
	/// (`chao_largar zeni/500`) -- ver `GameServer.LargarDaCarteira`.
	/// </summary>
	private void AbrirTecladoDeLargar(bool recursos)
	{
		FecharAcoes();
		FecharTeclado();
		if (GameClient.Instance is not { } cli) return;
		double tem = Math.Floor(recursos ? cli.Recursos : cli.Zeni);
		if (tem < 1) return;

		string moeda = recursos ? Jandirus.Core.Economia.Recurso.PalavraDeRecursos : Jandirus.Core.Economia.Recurso.PalavraDeZeni;
		var t = new TecladoNumerico($"Largar no chão — quanto de {moeda}? (você tem {tem:N0})", 1, tem,
			v =>
			{
				GameClient.Instance?.SendVerbo(Chao.VerboLargar, $"{moeda}/{v:0}");
				FecharTeclado();
			},
			FecharTeclado);

		_teclado = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_teclado.AddChild(t);
		_raiz.AddChild(_teclado);
	}

	// =====================================================================
	// AS PORTAS DE BANCADA (`--diagv020`, `--diagv021`, `--diaganimais`)
	// =====================================================================
	/// <summary>A tela esta aberta na mochila de um caido, com quantas linhas, e o titulo. SO PRA BANCADA.</summary>
	public (bool Aberto, int Itens, string Titulo) SaqueDeTeste =>
		(IsInstanceValid(_raiz) && _raiz.Visible && _saque != null, _saque?.Leva.Count ?? 0, _titulo.Text);

	/// <summary>A tela esta aberta na MINHA mochila? SO PRA BANCADA.</summary>
	public bool MinhaAbertaDeTeste => IsInstanceValid(_raiz) && _raiz.Visible && _saque == null;

	/// <summary>O retangulo do PAINEL da mochila na tela (a raiz dela e do tamanho da tela inteira, e invisivel). SO PRA BANCADA.</summary>
	public Rect2 RetanguloDeTeste => _painel.GetGlobalRect();

	private static IEnumerable<Button> BotoesDe(Node raiz)
	{
		foreach (Node n in raiz.GetChildren())
		{
			if (n.IsQueuedForDeletion()) continue;
			if (n is Button b) yield return b;
			foreach (Button dentro in BotoesDe(n)) yield return dentro;
		}
	}

	/// <summary>Os rotulos dos slots da mochila alheia aberta, o corpo primeiro ("Tirar do corpo: X", "Pegar X"). SO PRA BANCADA.</summary>
	public List<string> RotulosDoSaqueDeTeste()
	{
		var saida = new List<string>();
		if (!SaqueDeTeste.Aberto) return saida;
		foreach (Button b in BotoesDe(_vestindo).Concat(BotoesDe(_grade)))
			if (b.HasMeta("saque")) saida.Add(b.GetMeta("saque").AsString());
		return saida;
	}

	/// <summary>Clica no slot da mochila alheia que tem este rotulo -- o mesmo sinal do dedo. SO PRA BANCADA.</summary>
	public bool LevarDoSaqueDeTeste(string rotulo)
	{
		if (!SaqueDeTeste.Aberto) return false;
		foreach (Button b in BotoesDe(_vestindo).Concat(BotoesDe(_grade)))
		{
			if (!b.HasMeta("saque") || b.GetMeta("saque").AsString() != rotulo) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}

	/// <summary>Os botoes da fileira da carteira, na ordem: (texto, ligado). SO PRA BANCADA.</summary>
	public List<(string Texto, bool Ligado)> CarteiraDeTeste()
	{
		var saida = new List<(string, bool)>();
		if (!IsInstanceValid(_carteira)) return saida;
		foreach (Button b in BotoesDe(_carteira)) saida.Add((b.Text, !b.Disabled));
		return saida;
	}

	/// <summary>Aperta o botao da carteira cujo texto comeca assim. Falso se ele nao esta la ou esta apagado. SO PRA BANCADA.</summary>
	public bool ApertarNaCarteiraDeTeste(string comecaCom)
	{
		foreach (Button b in BotoesDe(_carteira))
		{
			if (b.Disabled || !b.Text.StartsWith(comecaCom, StringComparison.Ordinal)) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv021`): a mochila volta a oferecer a melhoria dos pesos so em zeni -- a v0.2.0.
	/// Lido pelo proprio painel de acoes, em <see cref="AbrirAcoes"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool MelhoriaSoComZeniDeTeste;

	/// <summary>
	/// (2026-10-15) "Até o limite do corpo: 50 → 1.600 kg (7.500 zeni)" -- as melhorias que faltam, de uma vez, lidas da
	/// ficha lenta. Sem nada faltando, diz isso. Ver `PesoDeTreino.AcaoAteOLimite`.
	/// </summary>
	internal static string RotuloDoLimiteDosPesos(string pesos, Jandirus.Core.Economia.Moeda moeda = Jandirus.Core.Economia.Moeda.Zeni)
	{
		double max = PesoDeTreino.MaximoDe(pesos);
		int n = MelhoriasAteOLimiteDe(pesos);
		if (n <= 0) return "Até o limite do corpo: já chegou";
		return $"Até o limite do corpo: {PesoDeTreino.Maximo(max):N0} → {PesoDeTreino.MaximoDepoisDe(max, n):N0} kg "
			+ $"({Jandirus.Core.Economia.Recurso.PrecoEscrito(PesoDeTreino.PrecoDeVariasEmZeni(max, n), moeda)})";
	}

	/// <summary>
	/// "Melhorar: 50 → 100 kg (500 zeni)" -- o proximo degrau do peso maximo e o preco dele, na moeda pedida (em
	/// recursos e o dobro, como toda melhoria). Ver `PesoDeTreino`.
	/// </summary>
	/// <summary>
	/// (v0.3.0) "Equipar (40% do máximo)" -- a porcentagem que o jogador escolheu da ultima vez, e que o "equipar" veste (dono:
	/// *"dps de setar a % do peso maximo ele salvaria aquele valor e vc so precisaria clicar na opcao 'equipar'"*). Sem nenhuma
	/// guardada, o botao pede o numero.
	/// </summary>
	internal static string RotuloDoEquiparDosPesos()
	{
		int pct = GameClient.Instance?.Atributos.PesoPct ?? 0;
		return pct > 0 ? $"Equipar ({pct}% do máximo)" : "Equipar (escolha a %)";
	}

	/// <summary>
	/// (2026-10-18) As melhorias que faltam pra ESTE peso passar do limite do corpo: os degraus do corpo desde a fabrica (a ficha lenta) menos
	/// o nivel dele (o id). Ver `PesoDeTreino.MelhoriasAteOLimitePorDegraus`.
	/// </summary>
	internal static int MelhoriasAteOLimiteDe(string pesos) =>
		PesoDeTreino.MelhoriasAteOLimitePorDegraus(PesoDeTreino.MaximoDe(pesos), GameClient.Instance?.Atributos.PesoDegrausDoCorpo ?? 0);

	internal static string RotuloDaMelhoriaDosPesos(string pesos, Jandirus.Core.Economia.Moeda moeda = Jandirus.Core.Economia.Moeda.Zeni)
	{
		double max = PesoDeTreino.MaximoDe(pesos);
		return $"Melhorar: {Jandirus.Core.Items.PesoDeTreino.Maximo(max):N0} → {Jandirus.Core.Items.PesoDeTreino.ProximoMaximo(max):N0} kg "
			 + $"({Jandirus.Core.Economia.Recurso.PrecoEscrito(Jandirus.Core.Items.PesoDeTreino.PrecoEmZeni(max), moeda)})";
	}

	/// <summary>
	/// Os botoes do painel de acoes aberto agora, na ordem: (texto, moeda marcada no botao -- vazia na maioria). SO PRA
	/// BANCADA (`--diagv021`).
	/// </summary>
	/// <summary>O icone que o slot deste item esta mostrando (nulo sem slot). SO PRA BANCADA (`--diagv030`).</summary>
	public Texture2D? IconeDoSlotDeTeste(string itemId)
	{
		if (!IsInstanceValid(_grade)) return null;
		foreach (Node n in _grade.FindChildren("*", "Button", true, false))
			if (n is Button b && b.HasMeta("item") && b.GetMeta("item").AsString() == itemId)
				return b.Icon ?? b.GetChildren().OfType<TextureRect>().FirstOrDefault()?.Texture;
		return null;
	}

	public List<(string Texto, string Moeda)> AcoesAbertasDeTeste()
	{
		var saida = new List<(string, string)>();
		if (_acoes == null || !IsInstanceValid(_acoes)) return saida;
		foreach (Node n in _acoes.GetChild(0).GetChildren())
			if (n is Button b) saida.Add((b.Text, b.HasMeta("moeda") ? b.GetMeta("moeda").AsString() : ""));
		return saida;
	}

	/// <summary>Abre o painel de acoes do primeiro slot que guarda este item. Falso se ele nao esta na mochila. SO PRA BANCADA.</summary>
	public bool AbrirAcoesDeTeste(string idDoItem)
	{
		Inventario inv = GameClient.Instance?.Mochila ?? new Inventario();
		int i = inv.Pilhas.FindIndex(p => p.Id == idDoItem);
		if (i < 0 || i >= _grade.GetChildCount() || _grade.GetChild(i) is not Control slot) return false;
		AbrirAcoes(inv.Pilhas[i], slot);
		return true;
	}

	/// <summary>
	/// Aperta o botao do painel de acoes aberto cujo texto comeca assim -- o mesmo sinal do dedo. Falso se ele nao esta
	/// la, ou esta apagado. SO PRA BANCADA (`--diagv021`).
	/// </summary>
	public bool ApertarAcaoDeTeste(string comecaCom)
	{
		if (_acoes == null || !IsInstanceValid(_acoes)) return false;
		foreach (Node n in _acoes.GetChild(0).GetChildren())
		{
			if (n is not Button b || b.Disabled || !b.Text.StartsWith(comecaCom, StringComparison.Ordinal)) continue;
			b.EmitSignal(BaseButton.SignalName.Pressed);
			return true;
		}
		return false;
	}

	private static string Rotulo(string acao, string id = "") =>
		TechDeBolso.BotaoDa(acao, id) is { } daTech ? daTech.RotuloEm(Jandirus.Core.Economia.Moeda.Zeni) : acao switch
	{
		"comer" => "Comer",
		"equipar" when PesoDeTreino.EhPeso(id) => RotuloDoEquiparDosPesos(),
		"equipar" => "Equipar / tirar",
		"ajustar" => "Ajustar o peso",
		// O PESO MAXIMO SE COMPRA (dono, 2026-10-12): o botao diz o degrau e o preco, lidos da ficha lenta.
		"melhorar" => RotuloDaMelhoriaDosPesos(id),
		// (2026-10-15) as melhorias que faltam pro limite do corpo, de uma vez. Ver `PesoDeTreino.AcaoAteOLimite`.
		PesoDeTreino.AcaoAteOLimite => RotuloDoLimiteDosPesos(id),
		// A FOLHA QUE OS PESOS USAM NO CORPO (dono, 2026-10-13) -- a mesma grade da armadura. Ver `PesoDeTreino.AcaoArte`.
		PesoDeTreino.AcaoArte => "Escolher a aparência",
		"tirar" => "Tirar os pesos",
		ArmaduraDeTech.AcaoMoldar => "Escolher a arte",
		// (v0.2.1) o pente e a tinta de olhos da bancada de criacao
		Jandirus.Core.Tech.BancadaDeCriacao.AcaoPentear => "Mudar o penteado",
		Jandirus.Core.Tech.BancadaDeCriacao.AcaoTingirOsOlhos => "Tingir os olhos",
		"usar" => "Usar",
		// O LIVRO DE ENSINAMENTOS (lote G13). "Ler" sozinho serviria -- o `_` la embaixo ja
		// capitaliza --, mas o botao precisa dizer que o livro SE GASTA, e isso nao cabe no verbo.
		"ler" => "Ler (gasta o livro)",
		"jogar" => "Jogar no alvo marcado",
		// A ROUPA: a guardada VESTE, a que esta no corpo SAI e vai pra mochila. Ver `RoupaGuardada`.
		RoupaGuardada.AcaoVestir => "Vestir",
		RoupaGuardada.AcaoTirar => "Tirar e guardar",
		// A PALAVRA E A DO DONO ("clicar em instalar"), e ela e a MESMA na frase que o servidor manda
		// depois de fabricar -- ver `GameServer.Construir`. Duas palavras pro mesmo botao fariam o
		// jogador procurar um segundo botao que nao existe.
		Jandirus.Core.Items.CatalogoDeItens.AcaoPosicionar => "Instalar no chão",
		Jandirus.Core.Items.CatalogoDeItens.AcaoPlantar => "Plantar",
		// LARGAR POE NO CHAO (dono, 2026-10-13): ate a v0.2.0 o botao era "Jogar fora", e apagava o item.
		"largar" => "Largar no chão",
		_ => char.ToUpperInvariant(acao[0]) + acao[1..],
	};

	private void FecharAcoes()
	{
		if (_acoes == null) return;
		_acoes.QueueFree();
		_acoes = null;
	}
}
