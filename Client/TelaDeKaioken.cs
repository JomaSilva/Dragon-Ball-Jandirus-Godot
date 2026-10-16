using Godot;
using KaiokenCore = Jandirus.Core.Skills.Kaioken;

namespace Jandirus.Client;

/// <summary>
/// **KAIO-KEN VEZES QUANTO?** -- a escolha do multiplicador antes de acender.
///
/// ============================ O PEDIDO DO DONO ============================
/// *"o kaioken ta sem maestria, e tb n ta dando pra escolher o multiplicador"* (2026-10-15). O botao do Kaio-ken mandava o id
/// nu e o servidor acendia o x2 nativo, sempre; os sete atalhos (`Kaioken_2` ... `Kaioken_100`) existiam como descritor e
/// nunca viraram botao. E a maestria nunca chegou a tela.
///
/// No DM o verb PERGUNTA (`kaioken.dm:102`: *"Kaioken multiple. (You have Kaioken x[KaiokenMastery] mastered)"*) -- aqui a
/// pergunta e esta tela: a maestria escrita em cima, o multiplicador num deslizador de 2 a 100 (com os degraus de sempre a um
/// clique), o PODER de verdade daquele multiplicador e o RISCO dele contra a maestria (`Core.Skills.Kaioken`, a mesma tabela
/// que o servidor cobra). Ela parte do ultimo multiplicador aceso (`Fighter.KaiokenVezes`, que vai pro disco).
/// ==========================================================================
///
/// ============================ APERTAR O KAIO-KEN E O MESMO GESTO DO DM ============================
///   * apagado: abre esta tela (a pergunta do `input()`);
///   * com ela aberta, apertar o Kaio-ken DE NOVO acende no multiplicador escolhido -- a tecla ligada a ele vira um "duplo
///     toque", e quem ja escolheu o de sempre acende sem tirar a mao do teclado;
///   * aceso: apaga (`if(usr.KaioPcnt != 1) usr.KaiokenRevert()`, a primeira linha dos cinco verbs do DM). O X tambem apaga
///     (`GameServer.ReverterOuLiberar`).
/// ================================================================================================
///
/// O MOLDE E O DA `TelaDeRefugio` (camada 4, ancora + `CenterContainer`, fundo que come o clique) e ela NAO TRAVA TECLA:
/// os controles nao pegam foco, e o jogo continua por baixo. Ela nao decide nada -- manda o verbo e o servidor responde.
/// </summary>
public partial class TelaDeKaioken : CanvasLayer
{
	public static TelaDeKaioken? Instancia { get; private set; }

	/// <summary>Os degraus a um clique: os de sempre do anime, e os do DM.</summary>
	private static readonly int[] Degraus = [2, 3, 4, 10, 20, 50, 100];

	private Control _raiz = null!;
	private Label _maestria = null!, _vezes = null!, _poder = null!, _risco = null!, _nota = null!;
	private HSlider _deslizador = null!;
	private Button _acender = null!;

	/// <summary>O multiplicador marcado AGORA -- escolha do jogador, e nao estado do servidor (o pacote que chega nao mexe nele).</summary>
	private int _escolhido = KaiokenCore.VezesMinimas;

	public override void _Ready()
	{
		Layer = 4;   // a mesma do menu da tecla E, da telinha de meditar e do refugio
		Instancia = this;

		_raiz = new Control { AnchorRight = 1, AnchorBottom = 1, Visible = false };
		Tema.Aplicar(_raiz);
		AddChild(_raiz);

		var fundo = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.55f),
			AnchorRight = 1, AnchorBottom = 1,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
		};
		_raiz.AddChild(fundo);

		var centro = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
		_raiz.AddChild(centro);

		PanelContainer painel = Tema.Painel1(18);
		centro.AddChild(painel);

		var coluna = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
		coluna.AddThemeConstantOverride("separation", 8);
		painel.AddChild(coluna);

		var titulo = new Label { Text = "KAIO-KEN", HorizontalAlignment = HorizontalAlignment.Center };
		titulo.AddThemeFontSizeOverride("font_size", 22);
		titulo.AddThemeColorOverride("font_color", Aura.CorDoKaioken);
		coluna.AddChild(titulo);

		_maestria = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_maestria.AddThemeColorOverride("font_color", Tema.Destaque);
		coluna.AddChild(_maestria);
		coluna.AddChild(new HSeparator());

		// ---- o multiplicador ----
		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 10);
		coluna.AddChild(linha);
		// SEM FOCO DE TECLADO: o corpo continua andando com a tela aberta, e um deslizador com foco comeria as setas.
		_deslizador = new HSlider
		{
			MinValue = KaiokenCore.VezesMinimas, MaxValue = KaiokenCore.VezesMaximas, Step = 1,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(240, 0),
		};
		_deslizador.ValueChanged += AoArrastar;
		linha.AddChild(_deslizador);
		_vezes = new Label { CustomMinimumSize = new Vector2(64, 0), HorizontalAlignment = HorizontalAlignment.Right };
		_vezes.AddThemeFontSizeOverride("font_size", 20);
		linha.AddChild(_vezes);

		var degraus = new HBoxContainer();
		degraus.AddThemeConstantOverride("separation", 4);
		coluna.AddChild(degraus);
		foreach (int d in Degraus)
		{
			int v = d;
			var b = new Button { Text = $"x{v}", FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			b.Pressed += () => Escolher(v);
			degraus.AddChild(b);
		}

		_poder = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		coluna.AddChild(_poder);
		_risco = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_risco.AddThemeFontSizeOverride("font_size", 13);
		coluna.AddChild(_risco);
		coluna.AddChild(new HSeparator());

		var botoes = new HBoxContainer();
		botoes.AddThemeConstantOverride("separation", 8);
		coluna.AddChild(botoes);
		_acender = new Button { FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		_acender.Pressed += Acender;
		botoes.AddChild(_acender);
		var fechar = new Button { Text = "Fechar (Esc)", FocusMode = Control.FocusModeEnum.None };
		fechar.Pressed += Fechar;
		botoes.AddChild(fechar);

		_nota = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
		_nota.AddThemeFontSizeOverride("font_size", 11);
		_nota.AddThemeColorOverride("font_color", Tema.TextoFraco);
		coluna.AddChild(_nota);

		// METODO NOMEADO E NAO LAMBDA: assinatura com lambda nao se cancela (ver `_ExitTree`).
		if (GameClient.Instance is { } cli) cli.AtributosRecebidos += AoAtributos;
		Atualizar();
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.AtributosRecebidos -= AoAtributos;
		if (Instancia == this) Instancia = null;
	}

	private void AoAtributos(Jandirus.Net.Protocol.AtributosState _)
	{
		if (NaTela) Atualizar();
	}

	// =====================================================================
	// O GESTO
	// =====================================================================
	/// <summary>
	/// O BOTAO (OU A TECLA) DO KAIO-KEN FOI APERTADO -- ver o cabecalho. Devolve falso pra qualquer outro id, e ai quem chamou
	/// manda o verbo como sempre (`MenuJogo.AbrirOControleDe`). O `Kaioken_Settings` (o menu de atalhos do DM, do nivel 2 da
	/// skill) abre a mesma tela: e ela que configura o Kaio-ken agora.
	/// </summary>
	public bool Pedir(string id)
	{
		bool nu = string.Equals(id, VerboDoKaioken, StringComparison.OrdinalIgnoreCase);
		if (!nu && !string.Equals(id, "Kaioken_Settings", StringComparison.OrdinalIgnoreCase)) return false;
		if (GameClient.Instance is not { } cli) return false;

		if (nu && cli.Atributos.KaiokenAceso) { cli.SendHabilidade(VerboDoKaioken); Fechar(); return true; }
		if (nu && NaTela) { Acender(); return true; }
		Abrir();
		return true;
	}

	/// <summary>O id nu do Kaio-ken -- o verb que o catalogo concede e a tecla guarda.</summary>
	public const string VerboDoKaioken = "Kaioken";

	/// <summary>ABRE NO ULTIMO MULTIPLICADOR ACESO (`Fighter.KaiokenVezes`; quem nunca escolheu, no x2 nativo).</summary>
	public void Abrir()
	{
		int ultimo = GameClient.Instance?.Atributos.KaiokenVezes ?? 0;
		Escolher(ultimo >= KaiokenCore.VezesMinimas ? ultimo : KaiokenCore.VezesMinimas);
		_raiz.Visible = true;
	}

	public void Fechar() => _raiz.Visible = false;

	/// <summary>A tela esta na tela? Le o NODE, e nao um `bool` proprio -- ver `TelaDeMeditacao.NaTela`.</summary>
	public bool NaTela => IsInstanceValid(_raiz) && _raiz.Visible;

	/// <summary>ACENDE no multiplicador marcado (`Kaioken_N`, que o servidor atende pela familia `VivoPorPrefixo`) -- ou, aceso, APAGA.</summary>
	private void Acender()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.SendHabilidade(cli.Atributos.KaiokenAceso ? VerboDoKaioken : $"{VerboDoKaioken}_{_escolhido}");
		Fechar();
	}

	/// <summary>ESC FECHA, e fechar nao acende nada. `_Input` e nao `_UnhandledInput`, pelo motivo da `TelaDeRefugio`.</summary>
	public override void _Input(InputEvent evento)
	{
		if (!NaTela || Foco.Digitando) return;
		if (evento is not InputEventKey { Pressed: true, Echo: false } k || k.Keycode != Key.Escape) return;
		Fechar();
		GetViewport().SetInputAsHandled();
	}

	private void Escolher(int vezes)
	{
		_escolhido = KaiokenCore.Valido(vezes);
		_deslizador.SetValueNoSignal(_escolhido);
		Atualizar();
	}

	private void AoArrastar(double valor)
	{
		_escolhido = KaiokenCore.Valido(valor);
		Atualizar();
	}

	// =====================================================================
	// O QUE ESTA NA TELA
	// =====================================================================
	/// <summary>Escreve nos controles que ja existem -- nada nasce nem morre aqui, e por isso o deslizador aguenta o arrasto.</summary>
	private void Atualizar()
	{
		Jandirus.Net.Protocol.AtributosState a = GameClient.Instance?.Atributos ?? default;
		double maestria = a.KaiokenMaestria > 0 ? a.KaiokenMaestria : KaiokenCore.MaestriaInicial;

		// "Mastered up to x[round(KaiokenMastery)]" (`Statistics.dm:294`) -- e o `round()` de um argumento e PISO no DM.
		_maestria.Text = $"Você domina o Kaio-ken até x{Math.Floor(maestria):0}  (maestria x{maestria:0.0})";
		_vezes.Text = $"x{_escolhido}";
		_poder.Text = $"Kaio-ken x{_escolhido} = {KaiokenCore.Multiplicador(_escolhido):0.##}x de poder.";

		KaiokenCore.Risco risco = KaiokenCore.RiscoDe(_escolhido, maestria);
		_risco.Text = KaiokenCore.FraseDoRisco(_escolhido, maestria);
		_risco.AddThemeColorOverride("font_color", risco switch
		{
			KaiokenCore.Risco.NaMaestria => Tema.Bom,
			KaiokenCore.Risco.AcimaDaMaestria => Tema.Destaque,
			_ => Tema.Perigo,
		});

		_acender.Text = a.KaiokenAceso ? $"Apagar o Kaio-ken (aceso em x{a.KaiokenVezes})" : $"Acender o Kaio-ken x{_escolhido}";
		_acender.AddThemeColorOverride("font_color", a.KaiokenAceso ? Tema.Texto : Aura.CorDoKaioken);
		_nota.Text = a.KaiokenAceso
			? "Pra trocar de multiplicador, apague primeiro (como no original)."
			: "Apertar o Kaio-ken de novo com esta tela aberta também acende. Aceso, o Kaio-ken apaga no mesmo botão -- ou no X. "
			  + "A maestria só cresce com ele aceso: devagar até x20, e muito devagar depois (até x50).";
	}

	// =====================================================================
	// AS PORTAS DE BANCADA (`--diagv030`, familia KK)
	// =====================================================================
	/// <summary>O que a tela mostra agora. So pra bancada.</summary>
	public (int Escolhido, string Maestria, string Poder, string Risco, string Botao) TextosDeTeste() =>
		(_escolhido, _maestria.Text, _poder.Text, _risco.Text, _acender.Text);

	/// <summary>Arrasta o deslizador (o mesmo `ValueChanged` do mouse). So pra bancada.</summary>
	public void ArrastarDeTeste(int vezes) => _deslizador.Value = vezes;

	/// <summary>Aperta o botao grande (o mesmo `Pressed` do mouse). So pra bancada.</summary>
	public void ApertarDeTeste() => _acender.EmitSignal(BaseButton.SignalName.Pressed);
}
