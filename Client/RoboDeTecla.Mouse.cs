using System.Text.RegularExpressions;
using Godot;
using Jandirus.Core.Forms;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ FM -- O BOTAO DO MEIO E OS LATERAIS DO MOUSE VALEM TECLA (2026-10-23) ============================
/// O pedido do dono: *"adicione a opcao de m3 e m4 dos mouses gamers como botao de setar alguma coisa, pq atualmente o jogo n
/// entende esses botoes"* (o M5 veio junto, decisao do orquestrador). Ver `MouseComoTecla`. Tudo aqui e apertado como um
/// mouse de verdade aperta: `InputEventMouseButton` entregue ao motor, e o que se mede e o que saiu no FIO (as regras da F1).
///
///   FMa. o nome na tela e "Mouse 3/4/5";                      FMb. a tela conta que o mouse vale, e o ingles do aviso;
///   FMc. o ESQUERDO, o DIREITO e a RODA nao capturam;          FMd. o M4 entra na linha de um verb pela tela de teclas;
///   FMe. o M4 dispara o verb (com o cursor em cima de um painel, onde o CLIQUE seria engolido);
///   FMf. o M5 numa acao do `InputMap` (a guarda) -- e o ALT para;  FMg. o M3 no carregar Ki (segurar, dois toques, a leitura crua);
///   FMh. o M5 ja tem dono: a tela pergunta; trocado, ele abre a mochila (acao de INTERFACE);
///   FMj. o `config.json` guarda o NOME ("Mouse4");             FMk. e le de volta pelo caminho do boot (e o config de antes carrega);
///   FMl. reaberta, a tela mostra o "Mouse 4" que veio do disco; FMz. restaurar tudo devolve o C, o ALT e o I.
///
/// CADA REGRA NOVA TEM O SEU DEFEITO INJETADO (os botoes `...DeTeste` do `MouseComoTecla`, lidos pela propria linha da regra),
/// ligado num `try/finally` e desligado tambem no `_ExitTree`: "(defeito injetado: ...) a mesma regua REPROVA".
///
/// FOTOS (`user://tecla-mouse-NN-*.png`) so com janela: a linha esperando, o M4 no verb, o M4 disparado (o chat), o M5 na
/// guarda, o M3 no carregar, a pergunta do dono, a mochila aberta pelo M5 e a tela reaberta.
///     Godot --position 1920,0 --resolution 1280x720 --semfoco --host --rede 10940 --conta bancada_tecla_mouse
///           --nome BancadaTecla --raca Saiyan --bpteste 3000000 --diagtecla
/// ===============================================================================================================================
/// </summary>
public partial class RoboDeTecla
{
	/// <summary>Um ponto da tela fora do painel da tela de teclas (ela e um quadro de 760x620 no meio): la o clique nao aperta nada.</summary>
	private static readonly Vector2 CantoVazio = new(6, 6);

	/// <summary>O texto do botao de uma linha enquanto a tela espera a tecla (o `TelaDeTeclas.Linha`).</summary>
	private const string Esperando = "aperte uma tecla...";

	private int _fotosDoMouse;

	/// <summary>Os botoes de defeito desta familia, todos desligados -- no fim de cada medida e no `_ExitTree`.</summary>
	private static void DesligarOsDefeitosDoMouse()
	{
		MouseComoTecla.MouseSemTraducaoDeTeste = false;
		MouseComoTecla.TodoBotaoViraTeclaDeTeste = false;
		MouseComoTecla.NomeCruDeTeste = false;
		MouseComoTecla.GravadoComoNumeroDeTeste = false;
		MouseComoTecla.NaoLeOGravadoDeTeste = false;
	}

	// =====================================================================
	// O MOUSE DE MENTIRA -- eventos de verdade, injetados no motor (como o `Tecla` da familia de cima)
	// =====================================================================
	private static void BotaoDoMouse(MouseButton b, bool apertado, Vector2 onde) =>
		Input.ParseInputEvent(new InputEventMouseButton
		{
			ButtonIndex = b,
			Pressed = apertado,
			Position = onde,
			GlobalPosition = onde,
		});

	private static void CliqueDoMouse(MouseButton b, Vector2 onde)
	{
		BotaoDoMouse(b, true, onde);
		BotaoDoMouse(b, false, onde);
	}

	/// <summary>
	/// UM PONTO EM CIMA DE UM PAINEL DO HUD (`MouseFilter.Stop`): ali o CLIQUE e do painel e nunca chega ao `_UnhandledInput` dos
	/// atalhos. E onde um M4 lido como clique falharia, e onde a tecla nao pode falhar.
	/// </summary>
	private static (Vector2 Onde, string Sobre) PontoSobreUmPainel()
	{
		if (Hud.Instancia is { } h
			&& Todos<PanelContainer>(h).FirstOrDefault(p => p.IsVisibleInTree() && p.MouseFilter == Control.MouseFilterEnum.Stop
															&& p.GetGlobalRect().Size.X > 20) is { } painel)
			return (painel.GetGlobalRect().GetCenter(), $"um painel do HUD ({painel.Name})");
		return (CantoVazio, "o canto da tela (nenhum painel do HUD achado)");
	}

	// =====================================================================
	// LER A TELA DE TECLAS
	// =====================================================================
	/// <summary>
	/// VIVO = nem ele nem um pai esperando o `QueueFree`: o `Redesenhar` da tela apaga as linhas no FIM do quadro, e quem varre a
	/// arvore logo depois ve a tela velha junto da nova (achado da `--diagmesa`).
	/// </summary>
	private static bool Vivo(Node n)
	{
		for (Node? p = n; p != null; p = p.GetParent())
			if (p.IsQueuedForDeletion()) return false;
		return true;
	}

	private static IEnumerable<T> NaTelaDeTeclas<T>() where T : Node =>
		TelaDeTeclas.Instancia is { } t ? Todos<T>(t).Where(Vivo) : [];

	/// <summary>O botao da tecla de uma linha (`TelaDeTeclas.Linha`: o rotulo, o botao da tecla e o "x", numa fileira).</summary>
	private static Button? BotaoDaLinha(string rotulo) =>
		NaTelaDeTeclas<Label>()
			.Where(l => l.Text == rotulo && l.GetParent() is HBoxContainer)
			.Select(l => l.GetParent().GetChildren().OfType<Button>().FirstOrDefault())
			.FirstOrDefault(b => b != null);

	private static string TextoDaLinha(string rotulo) => BotaoDaLinha(rotulo)?.Text ?? "(linha nao achada)";

	/// <summary>O recado do rodape depois de uma ligacao ("Toggle Knockback  ->  Mouse 4").</summary>
	private static string Recado() =>
		NaTelaDeTeclas<Label>().Select(l => l.Text).FirstOrDefault(t => t.Contains("  ->  ", StringComparison.Ordinal)) ?? "";

	private static bool TemNaTelaDeTeclas(string trecho) =>
		NaTelaDeTeclas<Label>().Any(l => l.IsVisibleInTree() && l.Text.Contains(trecho, StringComparison.Ordinal));

	/// <summary>Escreve na busca da tela (sem foco nela: o foco e o que faria o jogo se achar digitando).</summary>
	private static void Buscar(string texto)
	{
		if (NaTelaDeTeclas<LineEdit>().FirstOrDefault() is not { } busca) return;
		busca.Text = texto;
		busca.EmitSignal(LineEdit.SignalName.TextChanged, texto);
	}

	/// <summary>Aperta (pelo sinal) o botao da tecla desta linha: a tela passa a esperar a tecla dela.</summary>
	private static void EsperarNaLinha(string rotulo) => BotaoDaLinha(rotulo)?.EmitSignal(BaseButton.SignalName.Pressed);

	private static string Disco() =>
		Godot.FileAccess.FileExists(ArquivoDeConfig) ? Godot.FileAccess.GetFileAsString(ArquivoDeConfig) : "";

	/// <summary>As teclas gravadas no arquivo, em fila ("Mouse4,Mouse3") -- e o detalhe das reguas do disco.</summary>
	private static string TeclasDoDisco(string json) =>
		string.Join(",", Regex.Matches(json, "\"Tecla\":\\s*\"([^\"]*)\"").Select(m => m.Groups[1].Value));

	private void FotoDoMouse(string nome)
	{
		if (DisplayServer.GetName() == "headless") { Nota($"  --     sem foto pra {nome} (headless nao desenha)"); return; }
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { Nota($"  --     sem foto pra {nome}"); return; }
			string caminho = ProjectSettings.GlobalizePath($"user://tecla-mouse-{++_fotosDoMouse:00}-{nome}.png");
			img.SavePng(caminho);
			Nota("  foto   " + caminho);
		}
		catch (Exception e) { Nota($"  --     sem foto pra {nome}: {e.Message}"); }
	}

	// =====================================================================
	// FM -- O ROTEIRO
	// =====================================================================
	private IEnumerable<double> FM_OMouseQueValeTecla()
	{
		Nota("--- FM: o botao do meio e os laterais do mouse valem TECLA (M3, M4, M5) ---");

		// A FAMILIA PARTE DO PADRAO DE FABRICA, com a tela fechada -- a F6 acabou de restaurar, e isto nao depende dela.
		Teclas.RestaurarTudo();
		TelaDeTeclas.Instancia?.Fechar();
		yield return 0.3;

		Verbo? achado = Verbos.PorChave("Toggle Knockback");
		Checa("FM0. a tela de teclas existe, e o verb de referencia (o da F1) tambem",
			  TelaDeTeclas.Instancia != null && achado != null,
			  $"tela={TelaDeTeclas.Instancia != null} verb={achado?.Nome ?? "nao achei"}");
		if (TelaDeTeclas.Instancia is not { } tela || achado is not { } v) yield break;

		const MouseButton M3 = MouseButton.Middle, M4 = MouseButton.Xbutton1, M5 = MouseButton.Xbutton2;
		const string Guarda = "guarda", Carga = "carregar Ki (segurar) e subir a escada (dois toques)", Mochila = "mochila";

		// ---------------------------------------------------------------- FMa. o nome
		bool NomesCertos() =>
			Teclas.Nome(MouseComoTecla.Mouse3) == "Mouse 3" && Teclas.Nome(MouseComoTecla.Mouse4) == "Mouse 4"
			&& Teclas.Nome(MouseComoTecla.Mouse5) == "Mouse 5";
		string Nomes() =>
			$"{Teclas.Nome(MouseComoTecla.Mouse3)} | {Teclas.Nome(MouseComoTecla.Mouse4)} | {Teclas.Nome(MouseComoTecla.Mouse5)}";

		Checa("FMa. o nome na tela e 'Mouse 3', 'Mouse 4' e 'Mouse 5' (e nao o numero da tecla reservada)", NomesCertos(), Nomes());
		MouseComoTecla.NomeCruDeTeste = true;
		try
		{
			Injeta("FMa. (defeito injetado: a tela escreve o codigo cru) a mesma regua REPROVA", !NomesCertos(), Nomes());
		}
		finally { MouseComoTecla.NomeCruDeTeste = false; }

		// ---------------------------------------------------------------- FMb. a tela conta, e o ingles
		tela.Abrir();
		yield return 0.3;
		Checa("FMb. a tela de teclas diz que o botao do meio e os laterais valem tecla", TemNaTelaDeTeclas(MouseComoTecla.Aviso),
			  MouseComoTecla.Aviso);
		string avisoEn = Idioma.Tradutor.Traduzir(MouseComoTecla.Aviso);
		Checa("FMb. ...e em ingles o aviso sai traduzido, e o nome 'Mouse 4' passa igual",
			  avisoEn != MouseComoTecla.Aviso && avisoEn.Contains("Mouse 3", StringComparison.Ordinal)
			  && Idioma.Tradutor.Traduzir("Mouse 4") == "Mouse 4",
			  $"{avisoEn} | {Idioma.Tradutor.Traduzir("Mouse 4")}");

		// ---------------------------------------------------------------- FMc. o esquerdo, o direito e a roda NAO capturam
		Buscar("Knockback");
		yield return 0.3;
		Checa("FMc. a linha do verb esta na tela (a busca achou)", BotaoDaLinha(v.Nome) != null, TextoDaLinha(v.Nome));
		EsperarNaLinha(v.Nome);
		yield return 0.3;
		Checa("FMc. clicada a linha, a tela ESPERA a tecla", TelaDeTeclas.Digitando && TextoDaLinha(v.Nome) == Esperando,
			  TextoDaLinha(v.Nome));
		FotoDoMouse("esperando");

		bool AindaEsperando() => TelaDeTeclas.Digitando && TextoDaLinha(v.Nome) == Esperando && !Teclas.Ligados.Any();
		string Estado() => $"linha='{TextoDaLinha(v.Nome)}' esperando={TelaDeTeclas.Digitando} ligados={Teclas.Ligados.Count()}";

		foreach (MouseButton b in new[] { MouseButton.Left, MouseButton.Right, MouseButton.WheelUp, MouseButton.WheelDown })
		{
			CliqueDoMouse(b, CantoVazio);
			yield return 0.15;
		}
		yield return 0.2;
		Checa("FMc. o ESQUERDO, o DIREITO e a RODA nao viram tecla: a linha continua esperando e nada foi ligado",
			  AindaEsperando(), Estado());

		MouseComoTecla.TodoBotaoViraTeclaDeTeste = true;
		try
		{
			CliqueDoMouse(MouseButton.Left, CantoVazio);
			yield return 0.3;
			Injeta("FMc. (defeito injetado: o esquerdo vira tecla tambem) a mesma regua REPROVA", !AindaEsperando(), Estado());
		}
		finally { MouseComoTecla.TodoBotaoViraTeclaDeTeste = false; }

		// a volta: o que o defeito ligou sai, e a linha volta a esperar
		Teclas.Desligar("verbo", v.ChaveOuNome);
		yield return 0.2;
		if (!TelaDeTeclas.Digitando)
		{
			EsperarNaLinha(v.Nome);
			yield return 0.3;
		}
		Checa("FMc. (volta) a linha esperando de novo, sem nada ligado", AindaEsperando(), Estado());

		// ---------------------------------------------------------------- FMd. o M4 captura
		bool M4NoVerb() => Teclas.AtalhoDe(MouseComoTecla.Mouse4)?.Chave == v.ChaveOuNome && !TelaDeTeclas.Digitando;

		MouseComoTecla.MouseSemTraducaoDeTeste = true;
		try
		{
			CliqueDoMouse(M4, CantoVazio);
			yield return 0.3;
			Injeta("FMd. (defeito injetado: o mouse nao vira tecla -- o jogo de antes) a mesma regua REPROVA: a linha continua esperando",
				   !M4NoVerb(), Estado());
		}
		finally { MouseComoTecla.MouseSemTraducaoDeTeste = false; }

		CliqueDoMouse(M4, CantoVazio);
		yield return 0.35;
		Checa("FMd. o M4 (o lateral de tras) entrou na linha do verb, e a tela parou de esperar", M4NoVerb(),
			  Teclas.AtalhoDe(MouseComoTecla.Mouse4)?.Rotulo ?? "nada no M4");
		Checa("FMd. ...e a linha diz 'Mouse 4'", TextoDaLinha(v.Nome) == "Mouse 4", TextoDaLinha(v.Nome));
		Checa("FMd. ...e o recado diz pra onde o verb foi ('-> Mouse 4')", Recado().EndsWith("->  Mouse 4", StringComparison.Ordinal),
			  Recado());
		FotoDoMouse("m4-no-verb");

		// ---------------------------------------------------------------- FMe. o M4 dispara
		tela.Fechar();
		yield return 0.3;
		(Vector2 onde, string sobre) = PontoSobreUmPainel();
		Nota($"  nota   o cursor fica em cima de {sobre}, em ({onde.X:0}, {onde.Y:0}): o CLIQUE ali seria do painel; a tecla nao e");

		Marcar();
		CliqueDoMouse(M4, onde);
		yield return 0.45;
		Gesto m4 = Colher("M4 ligado ao verb");
		Checa("FMe. apertar o M4 dispara o verb: exatamente um comando no fio", RegraUmComando(m4), Hex(m4));
		Checa("FMe. ...e e o MESMO pacote que o botao do menu manda (o `knockback` da F1)",
			  Comandos(m4).Any(p => p.AsSpan().SequenceEqual(Referencia("knockback", ""))), Hex(m4));
		Checa("FMe. ...e o servidor respondeu", LinhaCom(m4, "arremess").Length > 0, PrimeiraLinha(m4));
		yield return 0.2;
		FotoDoMouse("m4-disparou");

		MouseComoTecla.MouseSemTraducaoDeTeste = true;
		try
		{
			Marcar();
			CliqueDoMouse(M4, onde);
			yield return 0.45;
			Gesto semTraducao = Colher("M4 sem a traducao");
			Injeta("FMe. (defeito injetado: o mouse nao vira tecla) a mesma regua REPROVA: o M4 nao manda nada",
				   !RegraUmComando(semTraducao), Hex(semTraducao));
		}
		finally { MouseComoTecla.MouseSemTraducaoDeTeste = false; }

		// ---------------------------------------------------------------- FMf. o M5 numa acao do `InputMap` (a guarda)
		tela.Abrir();
		Buscar("");
		yield return 0.3;
		EsperarNaLinha(Guarda);
		yield return 0.3;
		bool esperavaAGuarda = TelaDeTeclas.Digitando;
		CliqueDoMouse(M5, CantoVazio);
		yield return 0.35;
		Checa("FMf. o M5 (o lateral da frente) entrou na acao 'guarda' -- e o ALT, de fabrica, saiu dela",
			  esperavaAGuarda && Teclas.Teclado("guard") is [MouseComoTecla.Mouse5], $"esperava={esperavaAGuarda} {Teclas.NomeDaAcao("guard")}");
		Checa("FMf. ...e a projecao no `InputMap` e a tecla do M5 (o motor, e nao so a tabela)",
			  InputMap.ActionGetEvents("guard").Count == 1
			  && InputMap.ActionGetEvents("guard")[0] is InputEventKey { PhysicalKeycode: MouseComoTecla.Mouse5 },
			  $"{InputMap.ActionGetEvents("guard").Count} eventos");
		FotoDoMouse("m5-guarda");
		tela.Fechar();
		yield return 0.3;

		Marcar();
		BotaoDoMouse(M5, true, onde);
		yield return 0.5;
		BotaoDoMouse(M5, false, onde);
		yield return 0.4;
		Gesto guardaM5 = Colher("segurar o M5");
		Checa("FMf. segurar o M5 ergue a GUARDA (uma acao do `InputMap`, lida pelo corpo)", RegraTem(guardaM5, Protocol.C2S.Guard),
			  $"{Do(guardaM5, Protocol.C2S.Guard).Length} pacotes");

		Marcar();
		Tecla(Key.Alt, true);
		yield return 0.4;
		Tecla(Key.Alt, false);
		yield return 0.3;
		Gesto alt = Colher("ALT depois da troca");
		Checa("FMf. ...e o ALT parou de erguer (a troca tira a tecla velha, nao soma)", !RegraTem(alt, Protocol.C2S.Guard),
			  $"{Do(alt, Protocol.C2S.Guard).Length} pacotes");

		// ---------------------------------------------------------------- FMg. o M3 no carregar Ki
		tela.Abrir();
		Buscar("carregar Ki");
		yield return 0.3;
		EsperarNaLinha(Carga);
		yield return 0.3;
		CliqueDoMouse(M3, CantoVazio);
		yield return 0.35;
		Checa("FMg. o M3 (o botao do meio) entrou no carregar Ki", Teclas.Teclado("transformar") is [MouseComoTecla.Mouse3],
			  Teclas.NomeDaAcao("transformar"));
		FotoDoMouse("m3-carga");
		Buscar("");
		tela.Fechar();
		yield return 0.3;

		Marcar();
		BotaoDoMouse(M3, true, onde);
		yield return 0.25;
		bool cruApertado = Input.IsPhysicalKeyPressed(MouseComoTecla.Mouse3);
		yield return 0.3;
		BotaoDoMouse(M3, false, onde);
		yield return 0.4;
		bool cruSolto = !Input.IsPhysicalKeyPressed(MouseComoTecla.Mouse3);
		Gesto cargaM3 = Colher("segurar o M3");
		Checa("FMg. segurar o M3 CARREGA KI, e soltar tambem viaja (o par liga/desliga)",
			  Do(cargaM3, Protocol.C2S.Carregar).Length >= 2, $"{Do(cargaM3, Protocol.C2S.Carregar).Length} pacotes");
		Checa("FMg. ...e a leitura crua de tecla (`IsPhysicalKeyPressed`, a do embate de raio) ve o botao apertado e depois solto",
			  cruApertado && cruSolto, $"apertado={cruApertado} solto={cruSolto}");

		Marcar();
		CliqueDoMouse(M3, onde);
		yield return 0.12;
		CliqueDoMouse(M3, onde);
		yield return 0.6;
		Gesto duploM3 = Colher("dois toques no M3");
		Checa("FMg. dois toques no M3 pedem a SUBIDA (o C duplo, no mouse)", RegraTem(duploM3, Protocol.C2S.Transformar),
			  $"{Do(duploM3, Protocol.C2S.Transformar).Length} pacotes");
		C?.SendVerbo("forma", Catalogo.IdBase);
		yield return 0.7;

		// ---------------------------------------------------------------- FMh. o M5 ja tem dono; trocado, abre a mochila
		tela.Abrir();
		Buscar(Mochila);
		yield return 0.3;
		EsperarNaLinha(Mochila);
		yield return 0.3;
		CliqueDoMouse(M5, CantoVazio);
		yield return 0.35;
		bool perguntou = TemNaTelaDeTeclas("ESSA TECLA JA TEM DONO");
		Checa("FMh. o M5 ja e da guarda: a tela PERGUNTA antes de trocar, como pergunta por uma tecla",
			  perguntou && Teclas.Teclado("guard") is [MouseComoTecla.Mouse5], $"perguntou={perguntou} guarda={Teclas.NomeDaAcao("guard")}");
		FotoDoMouse("m5-tem-dono");
		NaTelaDeTeclas<Button>().FirstOrDefault(b => b.Text == "Trocar")?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.35;
		Checa("FMh. ...trocado: o M5 e da mochila, e a guarda ficou SEM tecla (nao ha duas coisas no mesmo botao)",
			  Teclas.Teclado("ui_mochila") is [MouseComoTecla.Mouse5] && Teclas.Teclado("guard").Length == 0,
			  $"mochila={Teclas.NomeDaAcao("ui_mochila")} guarda={Teclas.NomeDaAcao("guard")}");
		Buscar("");
		tela.Fechar();
		yield return 0.3;

		CliqueDoMouse(M5, onde);
		yield return 0.4;
		bool abriu = MochilaNaTela();
		FotoDoMouse("m5-mochila");
		CliqueDoMouse(M5, onde);
		yield return 0.4;
		bool fechou = !MochilaNaTela();
		Checa("FMh. o M5 abre e fecha a mochila (uma acao de INTERFACE, que a tela dela le por conta propria)", abriu && fechou,
			  $"abriu={abriu} fechou={fechou}");

		// ---------------------------------------------------------------- FMj. o arquivo guarda o NOME
		bool PelosNomes(string json) =>
			json.Contains("\"Mouse4\"", StringComparison.Ordinal) && json.Contains("\"Mouse3\"", StringComparison.Ordinal)
			&& json.Contains("\"Mouse5\"", StringComparison.Ordinal)
			&& !json.Contains(((long)MouseComoTecla.Mouse4).ToString(), StringComparison.Ordinal);

		string disco = Disco();
		Checa("FMj. o config.json guarda os botoes pelo NOME ('Mouse3', 'Mouse4', 'Mouse5'), e nao pelo numero da tecla reservada",
			  PelosNomes(disco), TeclasDoDisco(disco));
		MouseComoTecla.GravadoComoNumeroDeTeste = true;
		try
		{
			Teclas.Religar("transformar", MouseComoTecla.Mouse3);   // a mesma ligacao: so regrava o arquivo, com o defeito ligado
			string comDefeito = Disco();
			Injeta("FMj. (defeito injetado: o arquivo guarda o numero) a mesma regua REPROVA", !PelosNomes(comDefeito),
				   TeclasDoDisco(comDefeito));
		}
		finally { MouseComoTecla.GravadoComoNumeroDeTeste = false; }
		Teclas.Religar("transformar", MouseComoTecla.Mouse3);       // regrava pelos nomes
		yield return 0.2;

		// ---------------------------------------------------------------- FMk. e le de volta (o caminho do boot)
		bool Voltaram() =>
			Teclas.AtalhoDe(MouseComoTecla.Mouse4)?.Chave == v.ChaveOuNome
			&& Teclas.Teclado("transformar") is [MouseComoTecla.Mouse3] && Teclas.Teclado("ui_mochila") is [MouseComoTecla.Mouse5];
		string Ligacoes() =>
			$"M4={Teclas.AtalhoDe(MouseComoTecla.Mouse4)?.Rotulo ?? "nada"} carga={Teclas.NomeDaAcao("transformar")} "
			+ $"mochila={Teclas.NomeDaAcao("ui_mochila")}";

		// `Settings.Carregar()` + `Teclas.Aplicar(...)` sao as duas linhas do `Boot._Ready` (ver a F6)
		Teclas.Aplicar(Settings.Carregar());
		yield return 0.3;
		Checa("FMk. depois de reabrir, os tres voltaram do disco: M4 no verb, M3 no carregar Ki, M5 na mochila", Voltaram(), Ligacoes());
		Marcar();
		CliqueDoMouse(M4, onde);
		yield return 0.45;
		Gesto reaberto = Colher("M4 depois do relog");
		Checa("FMk. ...e o M4 reaberto DISPARA o verb", RegraUmComando(reaberto), Hex(reaberto));

		MouseComoTecla.NaoLeOGravadoDeTeste = true;
		try
		{
			Teclas.Aplicar(Settings.Carregar());
			Injeta("FMk. (defeito injetado: o 'Mouse4' do disco nao e lido) a mesma regua REPROVA", !Voltaram(), Ligacoes());
		}
		finally { MouseComoTecla.NaoLeOGravadoDeTeste = false; }
		Teclas.Aplicar(Settings.Carregar());
		yield return 0.2;
		Checa("FMk. (volta) relido sem o defeito, os tres estao la de novo", Voltaram(), Ligacoes());

		// O CONFIG DE ANTES (so teclado) CONTINUA CARREGANDO: a leitura nova so se mete com "Mouse3/4/5"
		var velho = new Settings();
		velho.LigacoesDeTecla.Add(new Settings.LigacaoDeTecla { Acao = "attack", Tecla = "N" });
		velho.AtalhosDeTecla.Add(new Settings.AtalhoGravado { Tecla = "J", Tipo = "verbo", Chave = v.ChaveOuNome, Rotulo = v.Nome });
		Teclas.Aplicar(velho);
		Checa("FMk. um config.json de antes (so teclado) carrega igual",
			  Teclas.Teclado("attack") is [Key.N] && Teclas.AtalhoDe(Key.J)?.Chave == v.ChaveOuNome,
			  $"socar={Teclas.NomeDaAcao("attack")} J={Teclas.AtalhoDe(Key.J)?.Rotulo ?? "nada"}");
		Teclas.Aplicar(Settings.Carregar());
		yield return 0.2;

		// ---------------------------------------------------------------- FMl. reaberta, a tela mostra o que veio do disco
		tela.Abrir();
		Buscar("Knockback");
		yield return 0.35;
		Checa("FMl. reaberta, a tela mostra 'Mouse 4' na linha do verb -- o que voltou do disco", TextoDaLinha(v.Nome) == "Mouse 4",
			  TextoDaLinha(v.Nome));
		FotoDoMouse("reaberta");
		Buscar("");
		tela.Fechar();
		yield return 0.2;

		// ---------------------------------------------------------------- FMz. tudo de volta
		Teclas.RestaurarTudo();
		DesligarOsDefeitosDoMouse();
		yield return 0.3;
		Checa("FMz. restaurar tudo devolve o C ao carregar Ki, o ALT a guarda e o I a mochila (e tira o M4 do verb)",
			  Teclas.Teclado("transformar") is [Key.C] && Teclas.Teclado("guard") is [Key.Alt]
			  && Teclas.Teclado("ui_mochila") is [Key.I] && Teclas.AtalhoDe(MouseComoTecla.Mouse4) == null,
			  $"carga={Teclas.NomeDaAcao("transformar")} guarda={Teclas.NomeDaAcao("guard")} mochila={Teclas.NomeDaAcao("ui_mochila")}");
	}
}
