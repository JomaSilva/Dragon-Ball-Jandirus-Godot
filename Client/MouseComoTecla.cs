using Godot;

namespace Jandirus.Client;

/// <summary>
/// O BOTAO DO MEIO E OS LATERAIS DO MOUSE VALEM TECLA (dono, 2026-10-23: *"adicione a opcao de m3 e m4 dos mouses gamers
/// como botao de setar alguma coisa, pq atualmente o jogo n entende esses botoes"*). O M5, a outra lateral, veio junto
/// (decisao do orquestrador: a mesma categoria). Na tela de teclas eles se ligam a qualquer linha -- acao do corpo, verbo,
/// forma, interface -- e em jogo fazem exatamente o que a tecla faria.
///
/// ============================ O MOUSE VIRA TECLA NA PORTA, E O RESTO DO JOGO NAO MUDA ============================
/// O registro (<see cref="Teclas"/>) e `Key` de ponta a ponta: a tabela, os atalhos, o `config.json`, a projecao no
/// `InputMap`, as telas que perguntam `Teclas.Bate` e a leitura crua do `LocalPlayer` (`IsPhysicalKeyPressed`). Ensinar
/// o mouse a cada uma dessas portas seria o mesmo `if` em nove arquivos -- e o decimo, que nascesse depois, esqueceria.
///
/// Entao cada botao ganha UMA tecla reservada (<see cref="Mouse3"/>, <see cref="Mouse4"/>, <see cref="Mouse5"/>), e o
/// aperto dele vira o aperto DESSA tecla, entregue ao motor pelo mesmo caminho do teclado (<see cref="Traduzir"/>). Quem
/// esta do outro lado recebe uma tecla e nao sabe de onde ela veio.
///
/// E POR ISSO ELE VALE ONDE QUER QUE O CURSOR ESTEJA, como a tecla vale: o CLIQUE e engolido pelo painel que estiver
/// debaixo do cursor (o `MouseFilter.Stop` dos paineis do HUD e das telas) antes de chegar ao `_UnhandledInput` dos
/// atalhos -- e a tecla nao e de painel nenhum. Um M4 lido como clique so funcionaria com o cursor no chao do mundo.
/// ==================================================================================================================
///
/// O ESQUERDO, O DIREITO E A RODA NUNCA VIRAM TECLA: sao o clique da interface, o alvo (duplo clique), a obra (por e
/// tirar) e o zoom. A tabela de <see cref="DoBotao"/> so conhece os tres de cima; o resto cai no `_` e fica sem tecla.
///
/// ============================ POR QUE ESSES NUMEROS ============================
/// A faixa "especial" do Godot (bit 22, 0x400000) e a das teclas sem letra (ESC = 0x400001; a ultima hoje e o
/// `Key.JisKana`, 0x400081), e ela acaba na mascara de codigo (0x7FFFFF, o `Key.Unknown`). 0x4D00nn -- 'M' e o numero do
/// botao -- fica longe das duas pontas, e nenhum teclado produz um codigo ali: a letra de qualquer layout e Unicode (ate
/// 0x10FFFF), e a tecla fisica sai sempre da tabela do proprio Godot. O motor nao conhece o nome desses codigos -- por
/// isso o nome na tela e o do arquivo moram aqui (<see cref="NomeNaTela"/>, <see cref="NomeGravado"/>).
/// ===============================================================================
/// </summary>
public static class MouseComoTecla
{
	/// <summary>A faixa das teclas reservadas pro mouse (ver "POR QUE ESSES NUMEROS").</summary>
	private const long Faixa = 0x4D0000;

	public const Key Mouse3 = (Key)(Faixa + 3);
	public const Key Mouse4 = (Key)(Faixa + 4);
	public const Key Mouse5 = (Key)(Faixa + 5);

	/// <summary>
	/// A LINHA DA TELA DE TECLAS que conta que o mouse vale tecla -- sem ela ninguem descobriria que a captura aceita o botao
	/// do meio e os laterais. Constante aqui, e nao escrita la, porque a bancada (`--diagtecla`, familia FM) confere o
	/// ingles dela pelo tradutor de verdade.
	/// </summary>
	public const string Aviso = "O botão do meio e os laterais do mouse (Mouse 3, 4 e 5) também valem como tecla.";

	/// <summary>
	/// A TECLA DE CADA BOTAO -- e a regra do "nunca": o esquerdo, o direito e a roda caem no `_` e ficam sem tecla.
	/// A numeracao e a dos mouses de jogo, e nao a do Godot: o M4 e o `Xbutton1` (o lateral de tras, o "voltar").
	/// </summary>
	public static Key DoBotao(MouseButton b) => b switch
	{
		MouseButton.Middle => Mouse3,
		MouseButton.Xbutton1 => Mouse4,
		MouseButton.Xbutton2 => Mouse5,
		_ => TodoBotaoViraTeclaDeTeste ? (Key)(Faixa + 0x10 + (long)b) : Key.None,
	};

	/// <summary>O NOME NA TELA ("Mouse 4"). Nulo = nao e do mouse (quem pergunta segue pro nome do teclado).</summary>
	public static string? NomeNaTela(Key k) => NomeCruDeTeste ? null : k switch
	{
		Mouse3 => "Mouse 3",
		Mouse4 => "Mouse 4",
		Mouse5 => "Mouse 5",
		_ => null,
	};

	/// <summary>
	/// O NOME NO `config.json` ("Mouse4"), e nao o numero da tecla reservada: o arquivo guarda o nome em texto justamente pra
	/// nao depender de numero nenhum (ver o cabecalho das listas do `Settings`). Nulo = nao e do mouse.
	/// </summary>
	public static string? NomeGravado(Key k) => GravadoComoNumeroDeTeste ? null : k switch
	{
		Mouse3 => "Mouse3",
		Mouse4 => "Mouse4",
		Mouse5 => "Mouse5",
		_ => null,
	};

	/// <summary>O CAMINHO DE VOLTA do <see cref="NomeGravado"/>. Nulo = nao e do mouse (o resto e nome de `Godot.Key`).</summary>
	public static Key? LerGravado(string s) => NaoLeOGravadoDeTeste ? null : s switch
	{
		"Mouse3" => Mouse3,
		"Mouse4" => Mouse4,
		"Mouse5" => Mouse5,
		_ => null,
	};

	/// <summary>
	/// O PORTEIRO: o aperto (e o soltar) de um dos tres botoes vira o aperto (e o soltar) da tecla dele. Chamado no `_Input`
	/// da tela de teclas, que vive do lobby ao mundo -- ver la por que ali.
	///
	/// A TECLA SAI PELO `Input.ParseInputEvent`, e nao pelo `PushInput` de uma tela: e o caminho do teclado de verdade, e so
	/// por ele que o `InputMap` (andar, voar, carregar...) e o `IsPhysicalKeyPressed` ficam sabendo. Ela entra na fila de
	/// eventos do motor e e entregue logo depois do clique, na mesma rodada da fila.
	///
	/// O CLIQUE SEGUE O CAMINHO DELE: nenhuma tela deste jogo le o meio nem os laterais (a obra, o mapa e o mundo leem o
	/// esquerdo, o direito e a roda), e engoli-lo aqui mudaria o que os paineis fazem com ele.
	/// </summary>
	public static void Traduzir(InputEvent e)
	{
		if (MouseSemTraducaoDeTeste || e is not InputEventMouseButton m) return;
		Key k = DoBotao(m.ButtonIndex);
		if (k == Key.None) return;

		// AS DUAS METADES, como um teclado de verdade manda: `PhysicalKeycode` e o que o registro e o `InputMap` casam (ver
		// `Teclas.Fisica`), `Keycode` e o que algumas telas leem.
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = m.Pressed });
	}

	// =====================================================================
	// DEFEITOS INJETADOS (bancada `--diagtecla`, familia FM -- `RoboDeTecla.Mouse.cs`). Falsos em jogo, sempre.
	// =====================================================================
	/// <summary>DEFEITO INJETADO: o jogo de antes -- o botao do mouse nao vira tecla nenhuma. Lido pela linha do <see cref="Traduzir"/>.</summary>
	public static bool MouseSemTraducaoDeTeste;

	/// <summary>DEFEITO INJETADO: o esquerdo, o direito e a roda viram tecla tambem. Lido pelo `_` do <see cref="DoBotao"/>.</summary>
	public static bool TodoBotaoViraTeclaDeTeste;

	/// <summary>DEFEITO INJETADO: a tela escreve o numero cru da tecla reservada. Lido pelo <see cref="NomeNaTela"/>.</summary>
	public static bool NomeCruDeTeste;

	/// <summary>DEFEITO INJETADO: o `config.json` guarda o numero da tecla reservada, e nao o nome. Lido pelo <see cref="NomeGravado"/>.</summary>
	public static bool GravadoComoNumeroDeTeste;

	/// <summary>DEFEITO INJETADO: o "Mouse4" do `config.json` nao e lido de volta. Lido pelo <see cref="LerGravado"/>.</summary>
	public static bool NaoLeOGravadoDeTeste;
}
