using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O CHAT, no canto de baixo a esquerda.
///
/// Os canais sao os do BYOND, com os mesmos numeros e os mesmos alcances (ver
/// <see cref="Protocol.Fala"/> e GameServer.Chat.cs). O painel HTML do original separava as
/// falas em ABAS por categoria -- "say", "rp", "ooc", "looc" -- e essa divisao esta aqui como
/// FILTRO: tudo chega na mesma janela e os botoes de cima escolhem o que fica visivel. Numa
/// janela pequena, trocar de aba pra descobrir que ninguem falou nada e pior que rolar.
///
/// TRANSPARENTE, e mais ainda quando ninguem esta escrevendo: o chat fica em cima do mundo, e
/// no meio de uma luta o que importa e o que esta atras dele. Ao abrir a linha de digitacao a
/// opacidade sobe, porque ai o assunto passa a ser o texto.
///
/// A LINHA DE DIGITACAO SO EXISTE QUANDO SE VAI FALAR. ENTER abre, ENTER manda, ESC desiste.
/// Enquanto ela esta aberta o personagem NAO se mexe -- <see cref="Digitando"/> e lido pelo
/// <see cref="LocalPlayer"/>. Sem isso, escrever "sai da frente" faria o personagem andar pra
/// direita, treinar e meditar no meio da frase.
/// </summary>
public partial class Chat : CanvasLayer
{
	/// <summary>O chat vivo. Quem quiser escrever uma linha de sistema passa por aqui.</summary>
	public static Chat? Instancia { get; private set; }

	/// <summary>
	/// TEM ALGUEM ESCREVENDO? O teclado do jogo e o mesmo teclado do chat, e
	/// <c>Input.IsActionPressed</c> nao sabe que ha um campo de texto com foco -- ele le a
	/// tecla fisica. Quem anda, soca, treina e mira consulta isto antes de agir.
	/// </summary>
	public static bool Digitando { get; private set; }

	private const int MaxLinhas = 300;

	/// <summary>Uma fala guardada. Sao guardadas TODAS; o filtro decide o que se desenha.</summary>
	private readonly record struct Linha(Protocol.Fala Canal, string Autor, string Texto, string Hora);

	private readonly List<Linha> _linhas = [];

	/// <summary>Os filtros de cima. Cada um e um conjunto de canais.</summary>
	private static readonly (string Nome, Protocol.Fala[] Canais)[] Filtros =
	[
		("Tudo", []),
		("Fala", [Protocol.Fala.Diz, Protocol.Fala.Sussurro]),
		("RP", [Protocol.Fala.Emote, Protocol.Fala.Pensa]),
		("OOC", [Protocol.Fala.Ooc, Protocol.Fala.Looc]),
		("Sistema", [Protocol.Fala.Sistema]),
	];

	private int _filtro;

	/// <summary>
	/// COMANDOS DE CANAL. E o jeito do BYOND: o jogador nao troca de modo, ele escreve o modo.
	/// Quem preferir o botao tem o seletor ao lado da caixa -- os dois mexem na mesma coisa.
	/// </summary>
	private static readonly (string Cmd, Protocol.Fala Canal)[] Comandos =
	[
		("/ooc", Protocol.Fala.Ooc),
		("/looc", Protocol.Fala.Looc),
		("/w", Protocol.Fala.Sussurro),
		("/sussurro", Protocol.Fala.Sussurro),
		("/me", Protocol.Fala.Emote),
		("/pensa", Protocol.Fala.Pensa),
		("/diz", Protocol.Fala.Diz),
		("/s", Protocol.Fala.Diz),
	];

	private PanelContainer _painel = null!;
	private RichTextLabel _texto = null!;
	private LineEdit _entrada = null!;
	private OptionButton _canal = null!;
	private HBoxContainer _rodape = null!;
	private readonly List<Button> _abas = [];

	public override void _Ready()
	{
		Instancia = this;
		Layer = 2;   // acima do HUD (camada 1), abaixo do menu de pause (20)
		Montar();

		if (GameClient.Instance is { } cli) cli.Falou += Receber;

		Sistema("ENTER fala  ·  /ooc /looc /w /me /pensa trocam de canal");
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		Digitando = false;
		if (GameClient.Instance is { } cli) cli.Falou -= Receber;
	}

	// =====================================================================
	// MONTAGEM
	// =====================================================================
	private void Montar()
	{
		var raiz = new Control
		{
			AnchorRight = 1, AnchorBottom = 1,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		Tema.Aplicar(raiz);
		AddChild(raiz);

		_painel = new PanelContainer
		{
			AnchorTop = 1, AnchorBottom = 1,
			OffsetLeft = 12, OffsetRight = 12 + 470,
			OffsetTop = -236, OffsetBottom = -12,
			MouseFilter = Control.MouseFilterEnum.Pass,
		};
		_painel.AddThemeStyleboxOverride("panel", CaixaDeVidro());
		raiz.AddChild(_painel);

		var coluna = new VBoxContainer();
		coluna.AddThemeConstantOverride("separation", 4);
		_painel.AddChild(coluna);

		// ---- filtros ----
		var linhaAbas = new HBoxContainer();
		linhaAbas.AddThemeConstantOverride("separation", 2);
		coluna.AddChild(linhaAbas);
		for (int i = 0; i < Filtros.Length; i++)
		{
			int qual = i;
			var b = new Button
			{
				Text = Filtros[i].Nome,
				ToggleMode = true,
				ButtonPressed = i == 0,
				FocusMode = Control.FocusModeEnum.None,   // senao o botao rouba o foco da caixa
			};
			b.AddThemeFontSizeOverride("font_size", 11);
			b.Pressed += () => TrocarFiltro(qual);
			linhaAbas.AddChild(b);
			_abas.Add(b);
		}

		// ---- o texto ----
		_texto = new RichTextLabel
		{
			BbcodeEnabled = true,
			ScrollFollowing = true,
			SelectionEnabled = true,
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 150),
		};
		_texto.AddThemeFontSizeOverride("normal_font_size", 12);
		coluna.AddChild(_texto);

		// ---- a linha de digitacao (escondida ate alguem falar) ----
		_rodape = new HBoxContainer { Visible = false };
		_rodape.AddThemeConstantOverride("separation", 4);
		coluna.AddChild(_rodape);

		_canal = new OptionButton { FocusMode = Control.FocusModeEnum.None };
		_canal.AddThemeFontSizeOverride("font_size", 11);
		foreach (Protocol.Fala f in new[]
				 {
					 Protocol.Fala.Diz, Protocol.Fala.Sussurro, Protocol.Fala.Emote,
					 Protocol.Fala.Pensa, Protocol.Fala.Ooc, Protocol.Fala.Looc,
				 })
			_canal.AddItem(NomeDoCanal(f), (int)f);
		_canal.Selected = 0;
		_rodape.AddChild(_canal);

		_entrada = new LineEdit
		{
			MaxLength = Protocol.MaxFala,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			PlaceholderText = "falar...",
		};
		_entrada.TextSubmitted += Enviar;
		_rodape.AddChild(_entrada);

		Opacidade(false);
	}

	/// <summary>
	/// O fundo do painel. Mais transparente que o dos outros paineis de proposito: este fica
	/// sobre o MUNDO o tempo todo, e o que esta atras dele e o jogo.
	/// </summary>
	private static StyleBoxFlat CaixaDeVidro()
	{
		var s = new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.06f, 0.09f, 0.45f),
			BorderColor = new Color(Tema.Borda, 0.55f),
			ContentMarginLeft = 8, ContentMarginRight = 8,
			ContentMarginTop = 6, ContentMarginBottom = 6,
		};
		s.SetBorderWidthAll(1);
		s.SetCornerRadiusAll(Tema.RaioCanto);
		return s;
	}

	private void Opacidade(bool escrevendo) =>
		_painel.Modulate = new Color(1, 1, 1, escrevendo ? 1f : 0.72f);

	// =====================================================================
	// ABRIR, FECHAR, ENVIAR
	// =====================================================================
	public override void _Input(InputEvent e)
	{
		if (e is not InputEventKey { Pressed: true, Echo: false } k) return;

		if (Digitando)
		{
			// ESC desiste da frase. Precisa ser consumido AQUI: o menu de pause escuta a mesma
			// tecla em _UnhandledInput, e sair do chat nao pode abrir o menu junto.
			if (k.Keycode == Key.Escape)
			{
				Fechar();
				GetViewport().SetInputAsHandled();
			}
			return;   // o resto e do campo de texto
		}

		// ============================ O CHAT TAMBEM E UM ATALHO ============================
		// Este era o unico dos seis caminhos que nao perguntava NADA: com um embate correndo, ENTER
		// abria a caixa de texto por cima do quick time event -- e a partir dali o `Foco.Digitando`
		// calava o proprio embate, entao a caixa aberta por acidente custava o resto das letras.
		//
		// O PORTAO FICA AQUI E NAO NO TOPO de proposito: acima dele esta o ramo de quem JA esta
		// escrevendo, e la o ESC precisa continuar fechando a caixa. Barrar tudo no topo trancaria o
		// jogador dentro do campo que ele quis fechar -- o mesmo erro que o `MenuJogo` ja documenta.
		// ==================================================================================
		if (Foco.AtalhosMudos) return;

		switch (k.Keycode)
		{
			case Key.Enter or Key.KpEnter:
				Abrir("");
				GetViewport().SetInputAsHandled();
				break;
			case Key.Slash:
				// abre JA com a barra: quem digita "/" quer um comando, nao uma fala
				Abrir("/");
				GetViewport().SetInputAsHandled();
				break;
		}
	}

	private void Abrir(string comeco)
	{
		Digitando = true;
		_rodape.Visible = true;
		Opacidade(true);
		_entrada.Text = comeco;
		_entrada.GrabFocus();
		_entrada.CaretColumn = comeco.Length;
	}

	private void Fechar()
	{
		Digitando = false;
		_rodape.Visible = false;
		_entrada.Text = "";
		_entrada.ReleaseFocus();
		Opacidade(false);
	}

	private void Enviar(string texto)
	{
		texto = texto.Trim();
		Fechar();
		if (texto.Length == 0) return;

		Protocol.Fala canal = (Protocol.Fala)_canal.GetItemId(_canal.Selected);

		// "/ooc oi" -> canal OOC, texto "oi". O comando tambem MUDA o seletor: quem falou em
		// OOC uma vez costuma falar de novo, e obrigar a repetir a barra e ruido.
		if (texto.StartsWith('/'))
		{
			int esp = texto.IndexOf(' ');
			string cmd = (esp < 0 ? texto : texto[..esp]).ToLowerInvariant();

			// ============================ O CANAL DOS CARGOS (`RankChat`) ============================
			// Ele NAO e um `Protocol.Fala`: quem ouve nao e decidido por distancia nem por filtro, e
			// sim por CARREGAR UM CARGO -- e isso e uma pergunta do servidor, que e quem tem a lista
			// de tronos. Entao ele viaja pelo canal de verbs (`cargo_falar`), como o resto do sistema
			// de cargos, e nao como uma fala.
			//
			// A porta e a barra e nao um botao porque falar exige TEXTO, e o painel de verbs so tem
			// botoes -- a caixa de chat ja e o lugar onde se escreve. E o `RankChat` do DM
			// (`RankTree.dm:14`), a unica skill que os trinta cargos concedem.
			if (cmd is "/cargos" or "/rank")
			{
				string msg = esp < 0 ? "" : texto[(esp + 1)..].Trim();
				if (msg.Length == 0) { Sistema("escreva a mensagem depois de /cargos."); return; }
				GameClient.Instance?.SendVerbo("cargo_falar", msg);
				return;
			}
			foreach ((string c, Protocol.Fala f) in Comandos)
			{
				if (c != cmd) continue;
				canal = f;
				texto = esp < 0 ? "" : texto[(esp + 1)..].Trim();
				SelecionarCanal(f);
				break;
			}
			if (texto.StartsWith('/'))
			{
				Sistema($"comando desconhecido: {texto.Split(' ')[0]}");
				return;
			}
			if (texto.Length == 0) return;
		}

		GameClient.Instance?.SendChat(canal, texto);
	}

	private void SelecionarCanal(Protocol.Fala f)
	{
		for (int i = 0; i < _canal.ItemCount; i++)
			if (_canal.GetItemId(i) == (int)f) { _canal.Selected = i; return; }
	}

	// =====================================================================
	// RECEBER E DESENHAR
	// =====================================================================
	private void Receber(Protocol.Fala canal, string autor, string texto) =>
		Somar(new Linha(canal, autor, texto, Hora()));

	/// <summary>
	/// UMA LINHA SO PRA MIM. E por onde passam os avisos que antes so existiam no
	/// <c>GD.Print</c>: "zona carregada", "sem Ki pro dash", "voce acertou o braco". O console
	/// e do desenvolvedor; o jogador precisa ler isso na tela.
	/// </summary>
	public static void Sistema(string texto) =>
		Instancia?.Somar(new Linha(Protocol.Fala.Sistema, "", texto, Instancia.Hora()));

	private void Somar(Linha l)
	{
		_linhas.Add(l);
		if (_linhas.Count > MaxLinhas) _linhas.RemoveRange(0, _linhas.Count - MaxLinhas);
		if (Passa(l.Canal)) _texto.AppendText(Formatar(l) + "\n");
	}

	private bool Passa(Protocol.Fala canal) =>
		Filtros[_filtro].Canais.Length == 0 || Array.IndexOf(Filtros[_filtro].Canais, canal) >= 0;

	private void TrocarFiltro(int qual)
	{
		_filtro = qual;
		for (int i = 0; i < _abas.Count; i++) _abas[i].ButtonPressed = i == qual;

		_texto.Clear();
		foreach (Linha l in _linhas)
			if (Passa(l.Canal)) _texto.AppendText(Formatar(l) + "\n");
	}

	private string Hora() => Time.GetTimeStringFromSystem()[..5];

	/// <summary>
	/// A LINHA COMO ELA APARECE. As formas de frase sao as do `sayType()`: "Fulano diz, '...'",
	/// "*Fulano faz alguma coisa*", "Fulano pensa consigo mesmo, '...'".
	///
	/// O TEXTO DO JOGADOR E ESCAPADO. O painel le BBCode -- sem escapar, um "[color=red]" no
	/// meio de uma frase pintaria o chat alheio, e um "[img]" faria coisa pior.
	/// </summary>
	private static string Formatar(Linha l)
	{
		string hora = $"[color=#4d566e]{l.Hora}[/color] ";
		string quem = Esc(l.Autor);
		string oque = Esc(l.Texto);

		return l.Canal switch
		{
			Protocol.Fala.Sistema =>
				$"{hora}[color=#{Tema.Destaque.ToHtml(false)}]»[/color] [color=#8f9ab5]{oque}[/color]",

			Protocol.Fala.Ooc =>
				$"{hora}[color=#6fb6e8](OOC) {quem}:[/color] [color=#dfe4f0]{oque}[/color]",

			Protocol.Fala.Looc =>
				$"{hora}[color=#5f93b0](LOOC) {quem}:[/color] [color=#c8cede]{oque}[/color]",

			// sem texto = o teaser: alguem sussurrou e eu estava perto o bastante pra notar
			Protocol.Fala.Sussurro when l.Texto.Length == 0 =>
				$"{hora}[color=#77809a][i]- {quem} sussurra alguma coisa...[/i][/color]",

			Protocol.Fala.Sussurro =>
				$"{hora}[color=#9aa4bd][i]* {quem} sussurra: {oque}[/i][/color]",

			Protocol.Fala.Emote =>
				$"{hora}[color=#e8d06f][i]* {quem} {oque}[/i][/color]",

			Protocol.Fala.Pensa =>
				$"{hora}[color=#9d8fc4][i]{quem} pensa consigo mesmo, '{oque}'[/i][/color]",

			// "!" no texto e grito -- a MESMA regra que o servidor usa pra esticar o alcance
			_ when l.Texto.Contains('!') =>
				$"{hora}[color=#f0a041]{quem} grita, '{oque}'[/color]",

			_ => $"{hora}[color=#e6e9f2]{quem} diz, '[/color][color=#ffffff]{oque}[/color][color=#e6e9f2]'[/color]",
		};
	}

	private static string Esc(string s) => s.Replace("[", "[lb]");

	private static string NomeDoCanal(Protocol.Fala f) => f switch
	{
		Protocol.Fala.Diz => "Diz",
		Protocol.Fala.Sussurro => "Sussurra",
		Protocol.Fala.Emote => "Emote",
		Protocol.Fala.Pensa => "Pensa",
		Protocol.Fala.Ooc => "OOC",
		Protocol.Fala.Looc => "LOOC",
		_ => f.ToString(),
	};
}
