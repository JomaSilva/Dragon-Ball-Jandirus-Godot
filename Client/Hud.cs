using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O PAINEL DE STATUS. Substitui o statpanel do BYOND (que morreu no meio do caminho e virou
/// aba HTML la) por nodes do Godot.
///
/// TRES CANTOS, e a divisao nao e estetica:
///
///   ESQUERDA  -- quem eu sou e como estou: nome, classe, poder, vida, Ki. E o que se olha
///                entre uma luta e outra.
///   DIREITA   -- o corpo em partes e onde estou mirando. E o que se olha DURANTE a luta, e
///                fica do lado oposto pra os dois nao disputarem o mesmo canto de olho.
///   BAIXO     -- o relato do ultimo golpe, que aparece e some.
///
/// A AJUDA DE TECLAS ficava aberta ocupando quatro linhas. Virou um lembrete de uma linha, e
/// o painel inteiro abre no TAB. Interface de jogo nao e manual: quem ja sabe as teclas nao
/// deveria continuar lendo elas a partida toda.
///
/// O PODER DE LUTA E SEGREDO, e este painel e o primeiro lugar onde isso aparece: sem scouter
/// o BP le "???". Nao e a interface sendo modesta -- e a regra do jogo (HUD.dm:28 do BYOND faz
/// exatamente isto, `if(!usr.scouteron) bptext = "???"`), e ela vale pro proprio poder tambem.
/// Quando o servidor tambem para de MANDAR o numero (ver Server/GameServer.Sigilo.cs), ele
/// chega como ausencia de dado e nem ha o que esconder aqui.
///
/// A CLASSE NAO APARECE, e por isso nao ha linha de classe neste painel. No BYOND inteiro a
/// `Class` so e lida por codigo, nunca impressa: a unica pista que o jogador recebe e uma
/// frase no chat, uma vez, na criacao do personagem.
/// </summary>
public partial class Hud : CanvasLayer
{
	/// <summary>O painel vivo. O mundo chama pra narrar golpe sem precisar procurar o node.</summary>
	public static Hud? Instancia { get; private set; }

	private Label _nome = null!, _bp = null!, _atividade = null!, _relato = null!;
	private Label _mira = null!, _letal = null!, _hora = null!, _efetivo = null!;
	private LuaNoCeu _lua = null!;

	/// <summary>O mostrador da lua. A bancada confere que ele so aparece com a lua no ceu.</summary>
	public LuaNoCeu Lua => _lua;
	private Barra _hp = null!, _ki = null!, _vigor = null!, _nutricao = null!;

	/// <summary>
	/// A BARRA DE KI DESENHADA. Pra bancada: ela media `Sheet.Ki / Sheet.MaxKi` e afirmava que
	/// aquilo era "o que a barra do HUD desenha" -- e o corte em 100% morava DEPOIS disso, dentro
	/// do widget. Com esta porta a checagem passa a ler o que saiu na tela. Ver <see cref="Barra.TextoDeTeste"/>.
	/// </summary>
	public Barra BarraDeKi => _ki;

	/// <summary>
	/// AS OUTRAS TRES BARRAS DESENHADAS, pela mesma razao da <see cref="BarraDeKi"/>.
	///
	/// A DE NUTRICAO E A QUE PRECISA DISTO DE VERDADE: ela nasce cheia e uma barra congelada em
	/// 100% desenha o MESMO pixel que uma barra correta. Sem uma porta que devolva o que ela
	/// ESCREVEU, a unica prova possivel seria "o campo existe na ficha" -- que era exatamente o
	/// que a bancada media quando o corte de Ki atravessou quatro mil checagens verdes.
	///
	/// VIDA E VIGOR entram junto porque elas sao o CONTRA-EXEMPLO do teto: as tres tem que
	/// continuar com o trilho acabando em 1,0 enquanto so o Ki ganha folga.
	/// </summary>
	public Barra BarraDeNutricao => _nutricao;
	public Barra BarraDeVida => _hp;
	public Barra BarraDeVigor => _vigor;

	/// <summary>
	/// AS DUAS LABELS DA LINHA DO BP, como elas sairam na tela. Pra bancada, e so pra ela.
	///
	/// O BP e o campo do SIGILO ("???" sem scouter) e a % efetivo e a que o dono pediu -- e as
	/// duas so valem alguma coisa comparadas com o que a MESMA informacao virou no menu P. Ler a
	/// ficha nos dois lados provaria que a ficha e uma so, que nunca esteve em duvida: a queixa
	/// do dono foi sobre duas TELAS discordando.
	/// </summary>
	public string TextoDeBpDeTeste => _bp.Text;
	public string TextoDeEfetivoDeTeste => _efetivo.Text;

	private PanelContainer _ajuda = null!;
	private GridContainer _gradeAjuda = null!;
	private double _relatoAte;

	/// <summary>
	/// TENHO SCOUTER? So ele destrava a leitura de BP em numero, e quem diz e o SERVIDOR: o bit
	/// vem na ficha lenta (<see cref="Protocol.Poder.Scouter"/>), nunca de uma decisao daqui.
	/// Fosse do cliente, bastaria mexer nele pra passar a ler poder de luta.
	/// </summary>
	private bool _temScouter;

	/// <summary>A ultima ficha recebida -- o painel se redesenha quando o scouter vai ou vem.</summary>
	private SheetState _ficha;

	public override void _Ready()
	{
		Instancia = this;
		var raiz = new Control
		{
			AnchorRight = 1, AnchorBottom = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		Tema.Aplicar(raiz);
		AddChild(raiz);

		MontarEsquerda(raiz);
		MontarDireita(raiz);
		MontarRodape(raiz);

		if (GameClient.Instance is { } cli)
		{
			cli.SheetUpdated += Mostrar;
			cli.AtributosRecebidos += MostrarPoderes;
			cli.ActivityChanged += MostrarAtividade;
			cli.MiraMudou += AoMudarMira;
			cli.LetalidadeMudou += AoMudarLetalidade;
			MostrarPoderes(cli.Atributos);
			Mostrar(cli.Sheet);
		}
		MostrarCombate();
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		if (GameClient.Instance is not { } cli) return;
		cli.SheetUpdated -= Mostrar;
		cli.AtributosRecebidos -= MostrarPoderes;
		cli.ActivityChanged -= MostrarAtividade;
		// ESTES DOIS FALTAVAM, e faltavam por serem lambdas: `-=` precisa do mesmo delegate, e
		// lambda nova nunca e igual a anterior. Viraram metodos nomeados por isso.
		cli.MiraMudou -= AoMudarMira;
		cli.LetalidadeMudou -= AoMudarLetalidade;
	}

	private void AoMudarMira(byte _) => MostrarCombate();
	private void AoMudarLetalidade(bool _) => MostrarCombate();

	// =====================================================================
	// CANTO ESQUERDO: identidade e condicao
	// =====================================================================
	private void MontarEsquerda(Control raiz)
	{
		PanelContainer painel = Tema.Painel1();
		painel.OffsetLeft = 14; painel.OffsetTop = 14;
		painel.CustomMinimumSize = new Vector2(268, 0);
		painel.MouseFilter = Control.MouseFilterEnum.Ignore;
		raiz.AddChild(painel);

		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 6);
		painel.AddChild(v);

		_nome = new Label { Text = "..." };
		_nome.AddThemeFontSizeOverride("font_size", 18);
		v.AddChild(_nome);

		v.AddChild(new HSeparator());

		_hp = new Barra("VIDA", Tema.Vida);
		v.AddChild(_hp);
		_ki = new Barra("KI", Tema.Ki);
		v.AddChild(_ki);

		// VIGOR embaixo do Ki de proposito: e ele que LIMITA o Ki agora (carregar gasta folego e
		// para sozinho quando acaba), entao ler de cima pra baixo conta a historia na ordem certa.
		_vigor = new Barra("VIGOR", Tema.Vigor);
		v.AddChild(_vigor);

		// ============================ A NUTRICAO FECHA A CADEIA ============================
		// E a quarta e ultima da pilha, e ela continua a mesma leitura de cima pra baixo: o Ki e
		// limitado pelo VIGOR, e o vigor so volta as custas DESTE tanque. Sem ela o jogador ve o
		// folego minguar e nao tem como saber que a causa e fome -- uma barra caindo sem causa na
		// tela e o jogo deixando de responder sem dizer por que.
		//
		// O dado ja viajava: `SheetState.Nutricao` esta no pacote, o servidor preenche, o `TickFichas`
		// o compara e o menu P ja o desenhava. So faltava a barra.
		_nutricao = new Barra("NUTRIÇÃO", Tema.Nutricao);
		v.AddChild(_nutricao);

		// ============================ O BP GANHOU COMPANHIA ============================
		// A % de poder EFETIVO fica na MESMA linha do BP e alinhada a direita, porque as duas
		// respondem a mesma pergunta em dois tempos: quanto eu valho, e quanto disso eu estou
		// conseguindo colocar pra fora agora. Duas Labels e nao uma string so pra a % poder mudar de
		// cor sozinha -- um corpo a 40% precisa gritar sem pintar a palavra "BP" de vermelho.
		var linhaBp = new HBoxContainer();
		linhaBp.AddThemeConstantOverride("separation", 8);
		v.AddChild(linhaBp);

		_bp = new Label { Text = "" };
		_bp.AddThemeFontSizeOverride("font_size", 13);
		linhaBp.AddChild(_bp);

		_efetivo = new Label
		{
			Text = "",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		_efetivo.AddThemeFontSizeOverride("font_size", 13);
		linhaBp.AddChild(_efetivo);

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 10);
		v.AddChild(linha);

		_atividade = new Label { Text = "" };
		_atividade.AddThemeFontSizeOverride("font_size", 12);
		_atividade.AddThemeColorOverride("font_color", Tema.TextoFraco);
		linha.AddChild(_atividade);

		_hora = new Label { Text = "", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
							HorizontalAlignment = HorizontalAlignment.Right };
		_hora.AddThemeFontSizeOverride("font_size", 12);
		_hora.AddThemeColorOverride("font_color", Tema.TextoFraco);
		linha.AddChild(_hora);
	}

	// =====================================================================
	// CANTO DIREITO: o corpo e a mira
	// =====================================================================
	private void MontarDireita(Control raiz)
	{
		PanelContainer painel = Tema.Painel1(8);

		// ============================ O PAINEL CRESCE PRA ESQUERDA ============================
		// Ele tinha LARGURA FIXA, calculada a mao a partir do que havia dentro. Bastou a lua
		// entrar na linha pra o conteudo passar do tamanho e o boneco ser empurrado pra fora da
		// tela -- e nao era um valor errado, era o metodo: largura cravada nao sobrevive a nada
		// que se acrescente.
		//
		// Ancorado nas duas pontas a direita e crescendo pra `Begin`, o painel passa a ter o
		// tamanho do que cabe dentro dele e se estica pra esquerda. E o que tambem resolve a lua
		// APARECER E SUMIR: de dia ela nao existe, o painel encolhe sozinho e nao fica buraco.
		// ======================================================================================
		painel.AnchorLeft = 1; painel.AnchorRight = 1;
		painel.OffsetLeft = -14; painel.OffsetRight = -14;
		painel.GrowHorizontal = Control.GrowDirection.Begin;
		painel.OffsetTop = 14;
		painel.MouseFilter = Control.MouseFilterEnum.Pass;   // o seletor precisa do clique
		raiz.AddChild(painel);

		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 4);
		painel.AddChild(v);

		var linha = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
		linha.AddThemeConstantOverride("separation", 8);
		v.AddChild(linha);

		// A LUA VEM PRIMEIRO NA LINHA, à esquerda da mira e do boneco. Ela só aparece de noite
		// (ver `LuaNoCeu`), e o painel encolhe sozinho quando ela some -- por isso ela pode
		// dividir a linha sem empurrar nada de lugar durante o dia.
		_lua = new LuaNoCeu { Name = "Lua" };
		linha.AddChild(_lua);

		linha.AddChild(new ZonePicker { Name = "Mira" });
		linha.AddChild(new BodyDoll { Name = "Corpo" });

		_mira = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		_mira.AddThemeFontSizeOverride("font_size", 12);
		v.AddChild(_mira);

		_letal = new Label { Text = "", HorizontalAlignment = HorizontalAlignment.Center };
		_letal.AddThemeFontSizeOverride("font_size", 12);
		v.AddChild(_letal);
	}

	// =====================================================================
	// RODAPE: relato do golpe e a ajuda
	// =====================================================================
	private void MontarRodape(Control raiz)
	{
		_relato = Tema.Legenda("", Tema.Destaque, 16);
		_relato.AnchorLeft = 0; _relato.AnchorRight = 1;
		_relato.AnchorTop = 1; _relato.AnchorBottom = 1;
		_relato.OffsetTop = -108; _relato.OffsetBottom = -80;
		_relato.HorizontalAlignment = HorizontalAlignment.Center;
		_relato.MouseFilter = Control.MouseFilterEnum.Ignore;
		raiz.AddChild(_relato);

		// O PAINEL DE TECLAS, fechado. Abre no TAB.
		_ajuda = Tema.Painel1();
		_ajuda.AnchorLeft = 0.5f; _ajuda.AnchorRight = 0.5f;
		_ajuda.AnchorTop = 1; _ajuda.AnchorBottom = 1;
		_ajuda.OffsetLeft = -230; _ajuda.OffsetRight = 230;
		_ajuda.OffsetTop = -268; _ajuda.OffsetBottom = -56;
		_ajuda.Visible = false;
		_ajuda.MouseFilter = Control.MouseFilterEnum.Ignore;
		raiz.AddChild(_ajuda);

		_gradeAjuda = new GridContainer { Columns = 2 };
		_gradeAjuda.AddThemeConstantOverride("h_separation", 18);
		_gradeAjuda.AddThemeConstantOverride("v_separation", 5);
		_ajuda.AddChild(_gradeAjuda);
		MontarAjuda();

		Label dica = Tema.Legenda($"{Teclas.NomeDaAcao("ui_ajuda")}  teclas", Tema.TextoFraco, 12);
		dica.AnchorLeft = 0.5f; dica.AnchorRight = 0.5f;
		dica.AnchorTop = 1; dica.AnchorBottom = 1;
		dica.OffsetLeft = -60; dica.OffsetRight = 60;
		dica.OffsetTop = -34; dica.OffsetBottom = -14;
		dica.HorizontalAlignment = HorizontalAlignment.Center;
		dica.MouseFilter = Control.MouseFilterEnum.Ignore;
		raiz.AddChild(dica);
	}

	/// <summary>
	/// A LISTA DO TAB. Ela LE o registro de teclas em vez de escrever "C" e "ALT" na mao: quem
	/// religou o carregar de Ki pro G tem que ver G aqui, senao a ajuda passa a ensinar errado
	/// justamente o jogador que a mudou. Ver `Teclas.Ajuda`.
	/// </summary>
	private void MontarAjuda()
	{
		foreach (Node n in _gradeAjuda.GetChildren()) n.QueueFree();

		foreach ((string tecla, string oque) in Teclas.Ajuda())
		{
			var k = new Label { Text = tecla };
			k.AddThemeColorOverride("font_color", Tema.Destaque);
			k.AddThemeFontSizeOverride("font_size", 12);
			_gradeAjuda.AddChild(k);

			var d = new Label { Text = oque };
			d.AddThemeFontSizeOverride("font_size", 12);
			_gradeAjuda.AddChild(d);
		}
	}

	public override void _Input(InputEvent e)
	{
		// TAB no meio de uma frase e navegacao de campo, nao atalho -- e no meio de um embate ele e
		// mais uma tela abrindo por cima do quick time event. Ver `Foco.AtalhosMudos`.
		if (Foco.AtalhosMudos) return;
		if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
		if (!Teclas.Bate("ui_ajuda", k)) return;

		// REMONTA AO ABRIR. A lista e feita uma vez no boot; sem isto, quem trocasse uma tecla na
		// pausa e abrisse o TAB leria a tecla ANTIGA -- uma tela dizendo o contrario da tela que
		// acabou de mudar.
		if (!_ajuda.Visible) MontarAjuda();
		_ajuda.Visible = !_ajuda.Visible;
		GetViewport().SetInputAsHandled();
	}

	// =====================================================================
	// ATUALIZACAO
	// =====================================================================
	private void MostrarAtividade(Protocol.Activity a)
	{
		_atividade.Text = a switch
		{
			Protocol.Activity.Treinando => "TREINANDO",
			Protocol.Activity.Meditando => "MEDITANDO",
			_ => "",
		};
		_atividade.AddThemeColorOverride("font_color",
			a == Protocol.Activity.Parado ? Tema.TextoFraco : Tema.Bom);
	}

	private void MostrarCombate()
	{
		if (GameClient.Instance is not { } cli) return;
		string zona = cli.ZonaMirada == 0 ? "livre" : Protocol.ZonaDe(cli.ZonaMirada);
		_mira.Text = $"mira: {zona}";
		_mira.AddThemeColorOverride("font_color", cli.ZonaMirada == 0 ? Tema.TextoFraco : Tema.Texto);

		_letal.Text = cli.Letal ? "GOLPE LETAL" : "nao-letal";
		_letal.AddThemeColorOverride("font_color", cli.Letal ? Tema.Perigo : Tema.TextoFraco);
	}

	/// <summary>
	/// O scouter chegou (ou foi embora). Redesenha a leitura de BP na hora: o BYOND tratava
	/// esse mesmo caso -- tirar o scouter com o painel aberto tinha que apagar o numero.
	/// </summary>
	private void MostrarPoderes(Protocol.AtributosState a)
	{
		bool antes = _temScouter;
		_temScouter = a.Tem(Protocol.Poder.Scouter);
		if (antes != _temScouter) Mostrar(_ficha);
	}

	private void Mostrar(SheetState f)
	{
		_ficha = f;
		_nome.Text = GameClient.Instance?.LocalName ?? "";
		_bp.Text = $"BP {Leitura(f.ExpressedBP)}";

		// ============================ A % DE PODER EFETIVO ============================
		// Ela APARECE MESMO SEM SCOUTER, e nao e furo no sigilo: e RAZAO, e o DM e explicito em nao
		// esconder multiplicador ("x2,5 de forma", "netBuff 80%"). Sem aparelho o BP ao lado le
		// "???" e esta linha e a unica leitura de condicao que sobra -- que e exatamente o papel
		// que o original dava aos numeros relativos.
		//
		// QUEM CALCULA E O SERVIDOR (`Fighter.Inteireza`, pelo `peakexBP`). Aqui nao ha conta
		// nenhuma pra copiar: sem scouter o cliente nem recebe os dois lados da fracao.
		_efetivo.Text = $"{f.Inteireza * 100:0}% efetivo";
		_efetivo.AddThemeColorOverride("font_color",
			f.Inteireza >= 0.9 ? Tema.Bom : f.Inteireza <= 0.5 ? Tema.Perigo : Tema.Texto);

		_hp.Valor = f.HP / 100.0;

		// O TRILHO ANTES DO VALOR: e ele que decide se o excedente cabe na barra. Ver `Barra.Teto`.
		_ki.Teto = f.TrilhoDeKi;
		_ki.Valor = f.RazaoDeKi;

		_vigor.Valor = f.RazaoDeVigor;
		_nutricao.Valor = f.RazaoDeNutricao;
	}

	/// <summary>
	/// O BP COMO ELE PODE SER LIDO. Duas portas, e as duas fecham do mesmo jeito:
	///
	///   1. O NUMERO NEM VEIO. O servidor manda ausencia de dado (NaN) pra quem nao tem como
	///      ler poder -- ver <c>GameServer.Sigilo.SemLeitura</c>. Nao ha o que esconder porque
	///      nao ha o que mostrar, e nem um cliente modificado tira numero de onde nao tem.
	///   2. O NUMERO VEIO MAS EU NAO TENHO SCOUTER. Enquanto o corte do servidor nao estiver
	///      ligado nos dois envios de ficha, a tela nao pode contradizer a regra.
	///
	/// So o BP EXPRESSO e mostrado, e e o que o HUD do BYOND fazia (HUD.dm:24-28: `beepee =
	/// usr.expressedBP`). O BP base e a conta interna do treino; quem le poder le o que o corpo
	/// esta expressando AGORA.
	/// </summary>
	private string Leitura(double bp) =>
		double.IsNaN(bp) || !_temScouter ? "???" : Numero(bp);

	/// <summary>
	/// Narra o golpe pra quem esta nele. Diz ONDE acertou, nao so quanto: o corpo e por
	/// partes, e saber que o braco esta indo embora e o que faz mudar de mira ou recuar.
	/// </summary>
	public void Narrar(Protocol.HitEvent h, int localId)
	{
		bool bati = h.Atacante == localId;
		string quem = bati ? "acertou" : "levou";
		string txt = (Jandirus.Core.Combat.Desfecho)h.Desfecho switch
		{
			Jandirus.Core.Combat.Desfecho.Critico => $"CRITICO!  {quem} {h.Dano:0.#} em {h.Membro}",
			Jandirus.Core.Combat.Desfecho.Aparou => $"aparado com {h.Membro}  ({h.Dano:0.#})",
			Jandirus.Core.Combat.Desfecho.Contra => bati ? "CONTRA-ATAQUE recebido" : "CONTRA-ATAQUE!",
			Jandirus.Core.Combat.Desfecho.Esquivou => bati ? "esquivou do seu golpe" : "voce esquivou",
			Jandirus.Core.Combat.Desfecho.Errou => bati ? "" : "",
			_ => $"{quem} {h.Dano:0.#} em {h.Membro}",
		};
		if (h.Rabo) txt += "   -- RABO ARRANCADO";
		else if (h.Decepou) txt += "   -- MEMBRO ARRANCADO";
		else if (h.Quebrou) txt += "   -- QUEBROU";
		if (h.Morreu) txt += "   -- MORTE";
		else if (h.Nocauteou) txt += "   -- NOCAUTE";

		if (txt.Length == 0) return;

		// O RELATO TAMBEM VAI PRO CHAT. O aviso do meio da tela some em tres segundos, e no
		// meio de uma troca de golpes e facil perder o que acabou de acontecer -- ali fica o
		// historico, e da pra rolar depois pra entender por que o braco parou de responder.
		Chat.Sistema(txt);

		_relato.Text = txt;
		// A ESQUIVA SAI BRANCA, e nao no laranja de "aconteceu dano" -- e branca e o DM literal:
		// `attackcolor = "white"` (`AttackFlavor.dm:54`), contra o vermelho dos acertos e o amarelo
		// do contra-ataque.
		//
		// ELA JA FOI AZUL-GELO AQUI, justificada por "e a mesma leitura do anel e do borrao". Os dois
		// morreram: o anel saiu da esquiva a pedido do dono e o borrao tingido virou a TROCA de sprite
		// do `flick`, que nao tem cor nenhuma (a arte e preta -- ver `EsquivaZanzoken`). Sem eles a
		// justificativa do azul evaporou, e o que sobra e o que o DM sempre mandou.
		_relato.AddThemeColorOverride("font_color",
			h.Morreu || h.Decepou || h.Rabo ? Tema.Perigo
			: (Jandirus.Core.Combat.Desfecho)h.Desfecho == Jandirus.Core.Combat.Desfecho.Esquivou ? Tema.Texto
			: Tema.Destaque);
		_relatoAte = 3;
	}

	public override void _Process(double delta)
	{
		// A HORA E O CLIMA. A FASE DA LUA NAO ENTRA AQUI: ela tem mostrador proprio no canto
		// direito (ver `LuaNoCeu`), com o disco desenhado -- repetir o nome dela nesta linha seria
		// dizer duas vezes a mesma coisa em dois cantos da tela.
		if (World.Instancia?.Ceu is { } ceu)
		{
			string txt = Jandirus.Core.World.Ceu.NomeDaHora(ceu.Hora);
			if (World.Instancia.TempoQueFaz is { Ativo: true } tq)
				txt += " · " + Jandirus.Core.World.Clima.Nome(tq.Tipo);
			_hora.Text = txt;

			_lua.Aplicar(ceu, World.Instancia.TempoQueFaz?.Encobre ?? 0);
		}

		if (_relatoAte <= 0) return;
		_relatoAte -= delta;
		if (_relatoAte > 0.6) return;
		// some por fade, nao por corte: um texto que pisca fora do nada distrai no meio da luta
		_relato.Modulate = new Color(1, 1, 1, (float)Math.Max(_relatoAte / 0.6, 0));
		if (_relatoAte <= 0) { _relato.Text = ""; _relato.Modulate = Colors.White; }
	}

	/// <summary>BP fica em bilhoes rapido: notacao curta em vez de 14 digitos na tela.</summary>
	private static string Numero(double v) => v switch
	{
		>= 1e12 => $"{v / 1e12:0.##} T",
		>= 1e9 => $"{v / 1e9:0.##} B",
		>= 1e6 => $"{v / 1e6:0.##} M",
		>= 1e3 => $"{v / 1e3:0.##} k",
		_ => v.ToString("0.#"),
	};
}

/// <summary>
/// Uma barra de recurso com rotulo e numero.
///
/// A BARRA SOZINHA NAO BASTA: "quase cheia" e "cheia" sao o mesmo desenho a dois pixels de
/// distancia, e a diferenca entre 100% e 91% de Ki decide se da pra correr. O numero por cima
/// resolve, e o rotulo diz o que e sem precisar decorar a cor.
///
/// ============================ O CORTE EM 100% MORAVA AQUI ============================
/// O dono relatou que a barra de Ki do jogo trava em 100% enquanto o menu P mostra 118%. As duas
/// telas liam a MESMA ficha -- o corte era uma linha SO, dentro deste widget: um
/// `Mathf.Clamp(value, 0, 1)` no setter, que cortava a barra E o numero escrito ao lado. O menu P
/// imprime o valor cru e por isso acertava.
///
/// O clamp nao podia simplesmente sair: VIDA e VIGOR tambem sao desenhadas por esta classe, e
/// pra elas "acima de 100%" nao existe. Virou <see cref="Teto"/>, que e opt-in: quem nao mexe
/// nele continua com o trilho acabando em 1,0 exatamente como antes.
///
/// COM TETO, O TRILHO INTEIRO PASSA A SER O TETO DE CARGA do personagem (1,4 de fabrica, mais de
/// 3,8 com as skills de power-up), com um TRACO fixo no ponto dos 100%. Assim a barra nunca
/// estoura, o excedente tem pra onde crescer, o traco mostra onde acaba o tanque e comeca o buff,
/// e o segmento acima dele ganha cor de sobrecarga. O numero ao lado continua sendo a razao CRUA,
/// que e o que o dono quer ver.
/// ====================================================================================
/// </summary>
public partial class Barra : VBoxContainer
{
	private readonly ProgressBar _b;
	private readonly Label _txt;
	private readonly Panel _excesso;
	private readonly ColorRect _marca;
	private double _valor = 1, _teto = 1;

	/// <summary>
	/// ATE ONDE O TRILHO VAI, em razao. 1 = a barra de sempre (o comportamento de VIDA e VIGOR).
	///
	/// Acima de 1 a barra ganha o traco dos 100% e o segmento de sobrecarga. Nunca abaixo de 1:
	/// um trilho menor que o cheio nao quer dizer nada.
	/// </summary>
	public double Teto
	{
		get => _teto;
		set { _teto = Math.Max(value, 1); Reaplicar(); }
	}

	/// <summary>
	/// O valor, em razao. NAO E MAIS CORTADO EM 1 -- ver o cabecalho da classe. O piso em zero
	/// fica: razao negativa nao existe e viraria desenho invertido.
	/// </summary>
	public double Valor
	{
		get => _valor;
		set { _valor = Math.Max(value, 0); Reaplicar(); }
	}

	/// <summary>
	/// ============================ O QUE A BARRA REALMENTE ESCREVEU ============================
	/// Pra bancada, e so pra ela. A bancada afirmava cobrir este bug lendo `Sheet.Ki / Sheet.MaxKi`
	/// e chamando isso de "a mesma que a barra do HUD desenha" -- e nao era: a razao passava inteira
	/// pela ficha e morria no clamp DAQUI. Quatro mil checagens verdes com o defeito na tela.
	///
	/// Medir intencao (o valor que se MANDOU) nunca ia pegar isso. Estes dois expoem o que saiu do
	/// outro lado: o texto desenhado e o quanto do trilho o desenho preencheu.
	/// ========================================================================================
	/// </summary>
	public string TextoDeTeste => _txt.Text;

	/// <summary>Quanto do trilho o desenho encheu, de 0 a 1. Ver <see cref="TextoDeTeste"/>.</summary>
	public double PreenchimentoDeTeste => _b.MaxValue > 0 ? _b.Value / _b.MaxValue : 0;

	/// <summary>
	/// O SEGMENTO DE SOBRECARGA, como ele saiu: quanto do trilho ele ocupa, ou 0 quando ele nao
	/// esta na tela. Ver <see cref="TextoDeTeste"/> pra por que estas portas devolvem DESENHO.
	///
	/// Ele e a metade visual do conserto e a unica que o numero nao cobre: com o excedente
	/// invisivel, a barra de quem esta a 130% desenha exatamente a barra de quem esta a 100% --
	/// numero certo, desenho errado, que e a forma deste bug que ja passou por aqui uma vez.
	/// </summary>
	public double SobrecargaDeTeste =>
		_excesso.Visible ? Math.Max(_excesso.AnchorRight - _excesso.AnchorLeft, 0) : 0;

	/// <summary>Onde o traco dos 100% ficou no trilho, ou 0 quando ele nao existe (trilho sem folga).</summary>
	public double MarcaDeTeste => _marca.Visible ? _marca.AnchorLeft : 0;

	/// <summary>O trilho que a barra realmente montou -- e nao o que se mandou pra ela.</summary>
	public double TetoDeTeste => _b.MaxValue;

	public Barra(string rotulo, Color cor)
	{
		AddThemeConstantOverride("separation", 1);
		MouseFilter = MouseFilterEnum.Ignore;

		var topo = new HBoxContainer();
		AddChild(topo);

		Label r = Tema.Rotulo(rotulo);
		topo.AddChild(r);

		_txt = new Label
		{
			Text = "100%",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		_txt.AddThemeFontSizeOverride("font_size", 11);
		topo.AddChild(_txt);

		_b = new ProgressBar
		{
			CustomMinimumSize = new Vector2(0, 12),
			ShowPercentage = false,
			MaxValue = 1,
			Value = 1,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		var cheio = new StyleBoxFlat { BgColor = cor };
		cheio.SetCornerRadiusAll(3);
		_b.AddThemeStyleboxOverride("fill", cheio);
		AddChild(_b);

		// ---- o pedaco acima dos 100%, por cima do preenchimento ----
		// Uma `ProgressBar` tem UMA cor de preenchimento so, e o excedente precisa se distinguir do
		// tanque: os dois sao Ki, mas um e o que cabe no corpo e o outro e o que esta transbordando.
		// Ancorado nas duas pontas, ele acompanha a largura do painel sem conta de pixel.
		//
		// PANEL E NAO ColorRect, e o motivo e a PONTA DIREITA: o preenchimento da barra tem canto
		// arredondado de 3, e um retangulo cru encostado nele deixaria um degrau quadrado saindo do
		// desenho justo no Ki no talo -- que e o instante que alguem fotografa. So a direita
		// arredonda: a esquerda encosta no traco dos 100% e um canto ali leria como falha no meio.
		var sobrecarga = new StyleBoxFlat { BgColor = Tema.KiExcesso };
		sobrecarga.CornerRadiusTopRight = sobrecarga.CornerRadiusBottomRight = 3;
		_excesso = new Panel
		{
			Visible = false,
			AnchorTop = 0, AnchorBottom = 1,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_excesso.AddThemeStyleboxOverride("panel", sobrecarga);
		_b.AddChild(_excesso);

		// ---- o traco dos 100%, DEPOIS do excesso pra ficar por cima dele ----
		_marca = new ColorRect
		{
			Color = new Color(1, 1, 1, 0.85f), Visible = false,
			AnchorTop = 0, AnchorBottom = 1,
			OffsetLeft = -1, OffsetRight = 1,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_b.AddChild(_marca);

		Reaplicar();
	}

	private void Reaplicar()
	{
		_b.MaxValue = _teto;
		_b.Value = Math.Clamp(_valor, 0, _teto);

		// O NUMERO SAI CRU. E o pedido do dono, e e o que o menu P ja escrevia.
		_txt.Text = $"{_valor * 100:0}%";

		// abaixo de um quinto o numero fica VERMELHO: e o aviso que se ve pelo canto do olho.
		// Acima do tanque ele fica LARANJA -- nao e alarme, e "voce esta carregado".
		_txt.AddThemeColorOverride("font_color",
			_valor > 1 ? Tema.Destaque : _valor < 0.2 ? Tema.Perigo : Tema.Texto);

		bool sobra = _teto > 1;
		float umCheio = (float)(1 / _teto);

		_marca.Visible = sobra;
		_marca.AnchorLeft = _marca.AnchorRight = umCheio;

		_excesso.Visible = sobra && _valor > 1;
		_excesso.AnchorLeft = umCheio;
		_excesso.AnchorRight = (float)(Math.Clamp(_valor, 1, _teto) / _teto);
	}
}
