using Godot;
using Jandirus.Core.Forms;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DAS TECLAS (`--diagtecla`) ============================
/// Uma afirmacao central e um contra-exemplo. A afirmacao: **a tecla faz exatamente o que o botao
/// faz**. O contra-exemplo: **unificar o registro nao pode ter quebrado o C, o ALT e o E** -- as
/// teclas que ja existiam e que passaram a sair de uma tabela em vez de um `if`.
///
/// ============================ POR QUE ELA NAO MEDE `Verbo.Acionar` ============================
/// Ler o codigo mostra que o botao e a tecla chamam a MESMA `Verbo.Acionar`. Isso prova que HOJE e
/// a mesma linha; nao prova que o que **saiu no fio** foi igual, que e a unica coisa que o servidor
/// obedece. E o defeito que essa afirmacao existe pra impedir -- uma segunda escrita do pacote,
/// envelhecendo calada -- e invisivel pra qualquer checagem que compare intencao: as duas escritas
/// leem o mesmo verb, tem o mesmo nome na tela, e mandam pacotes diferentes.
///
/// Entao esta bancada compara **os bytes que chegaram no servidor** (`GameServer.EspiaoDeEntrada`),
/// que sao copiados antes da primeira leitura do pacote. E a mesma regra que esta casa aprendeu com
/// o corte de Ki em 100%: so se afirma o que foi DESENHADO -- aqui, o que foi RECEBIDO.
/// =========================================================================================
///
/// ============================ AS SEIS FAMILIAS, E COMO CADA UMA REPROVA ============================
///   F1 -- MESMO PACOTE. O botao de verdade (o `Button` do menu, apertado pelo sinal `pressed`) e a
///         tecla ligada ao mesmo verb tem que por bytes IGUAIS no fio, e o servidor tem que
///         responder as duas. **Reprova** quando a tecla monta o proprio pacote: um byte diferente,
///         um comando pelo NOME em vez do id, um canal diferente. Injetado.
///   F2 -- MESMA RECUSA. Verb indisponivel: o botao recusa ESTANDO APAGADO (`Button.Disabled`) e
///         nao mandando nada; a tecla tem que nao mandar nada TAMBEM -- e falar, porque o botao
///         apagado se explica por estar na tela e a tecla nao esta. E a recusa de FORMA tem que ser
///         a MESMA FRASE pelos dois gestos (a tecla de forma e o duplo toque no C, com o corpo
///         caido). **Reprova** quando a tecla manda pacote pra ouvir nao, ou quando cala.
///   F3 -- NAO FURA GATE. Uma forma que este corpo nao desperta e recusada PELOS DOIS CAMINHOS: o
///         portao do cliente (nao vira pacote) e o `Avaliar` do servidor (quando o pacote e forjado
///         no fio, como faria um cliente mexido). **Reprova** quando a forma entra.
///   F4 -- CONFLITO E RESTAURO. A tecla tem UM dono, a pergunta e uma so, e o dono anterior fica SEM
///         tecla. **Reprova** quando religar SOMA em vez de trocar (o defeito do `ActionAddEvent`
///         sem `ActionEraseEvents`) -- injetado no `InputMap` de verdade.
///   F5 -- DIGITANDO NAO DISPARA. Com o chat aberto, ou com qualquer campo do menu em foco, a mesma
///         tecla nao pode mandar nada. **Reprova** quando o pacote sai no meio da palavra.
///   F6 -- SOBREVIVE A FECHAR O JOGO. A ligacao esta no disco e volta pelo MESMO caminho do boot
///         (`Settings.Carregar` + `Teclas.Aplicar`). **Reprova** quando some no relog -- injetado
///         com um config vazio.
///   F7 -- AS FIXAS CONTINUAM. C carrega, C-C sobe, ALT guarda, E/P/I/TAB continuam abrindo o que
///         abriam. **Reprova** quando o registro deixa de projetar a acao -- injetado apagando os
///         eventos da acao no `InputMap`.
/// ============================================================================================
///
/// ============================ ELA MEXE NO `config.json` DA MAQUINA ============================
/// A ligacao de tecla e preferencia de MAQUINA (ver o cabecalho do `Settings`), entao nao ha config
/// de bancada pra usar: o arquivo que ela grava e o mesmo do dono. Por isso o conteudo dele e
/// copiado no `_Ready` e devolvido no fim E no `_ExitTree` -- inclusive quando a bancada e fechada
/// no meio. Se este arquivo for interrompido de um jeito que nem o `_ExitTree` rode, o config volta
/// ao padrao de fabrica e nao ao do dono: e a unica pegada que ela deixa, e esta escrita aqui.
/// ======================================================================================
///
/// COMO RODAR (porta propria, conta NOVA):
///     Godot --headless --path . --host --rede 7993 --conta bancadatecla --nome BancadaTecla
///           --raca Saiyan --bpteste 3000000 --diagtecla
/// </summary>
public partial class RoboDeTecla : Node
{
	private static GameClient? C => GameClient.Instance;

	// =====================================================================
	// PLACAR
	// =====================================================================
	private int _ok, _falha;
	private readonly List<string> _reprovadas = [];

	private static void Nota(string linha) => GD.Print("[tecla] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	/// <summary>
	/// O PLACAR DA INJECAO -- ele e separado do outro de proposito.
	///
	/// Uma checagem verde na rodada real e uma checagem que **nao viu defeito**; uma checagem que
	/// tambem fica vermelha quando o defeito e posto na frente dela e uma checagem que **sabe olhar**.
	/// Somar os dois no mesmo numero esconderia justamente a diferenca entre as duas coisas.
	/// </summary>
	private int _injOk, _injFalha;
	private readonly List<string> _injPassouBatido = [];

	/// <summary>Uma regra posta na frente do defeito que ela existe pra pegar: tem que ficar VERMELHA.</summary>
	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou " : "  PASSOU") + "  (injecao) " + oque
			 + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _injPassouBatido.Add(oque); }
	}

	// =====================================================================
	// O FIO -- os bytes que chegaram no servidor
	// =====================================================================
	private readonly List<byte[]> _fio = [];
	private readonly object _trava = new();

	/// <summary>
	/// UM GESTO MEDIDO: o que ele pos no fio e o que apareceu no chat depois dele.
	///
	/// E um `record` e nao dois campos soltos porque a rodada de injecao precisa poder FORJAR um
	/// gesto defeituoso e passa-lo pelas MESMAS regras -- se as regras lessem campos da bancada em
	/// vez de receberem uma amostra, nao haveria como pos-las na frente de um defeito.
	/// </summary>
	private sealed record Gesto(string Rotulo, byte[][] Fio, string Falas);

	/// <summary>Zera o fio e marca onde o chat estava. Todo gesto comeca por aqui.</summary>
	private int _marcaDoChat;
	private void Marcar()
	{
		lock (_trava) _fio.Clear();
		_marcaDoChat = TextoDoChat().Length;
	}

	private Gesto Colher(string rotulo)
	{
		byte[][] copia;
		lock (_trava) copia = [.. _fio];
		string tudo = TextoDoChat();
		string novo = tudo.Length > _marcaDoChat ? tudo[_marcaDoChat..] : "";
		return new Gesto(rotulo, copia, novo);
	}

	// =====================================================================
	// AS REGRAS -- funcoes puras sobre uma amostra, pra poderem ser injetadas
	// =====================================================================
	/// <summary>Os pacotes deste gesto que sao de um opcode.</summary>
	private static byte[][] Do(Gesto g, Protocol.C2S op) =>
		[.. g.Fio.Where(p => p.Length > 0 && p[0] == (byte)op)];

	/// <summary>Exatamente UM pacote de comando (verbo ou habilidade) saiu.</summary>
	private static bool RegraUmComando(Gesto g) =>
		Do(g, Protocol.C2S.Verbo).Length + Do(g, Protocol.C2S.Habilidade).Length == 1;

	/// <summary>NENHUM pacote de comando saiu -- a recusa que o botao apagado faz.</summary>
	private static bool RegraSilencio(Gesto g) =>
		Do(g, Protocol.C2S.Verbo).Length + Do(g, Protocol.C2S.Habilidade).Length == 0;

	/// <summary>Os dois gestos poseram os MESMOS BYTES no fio.</summary>
	private static bool RegraMesmoPacote(Gesto a, Gesto b)
	{
		byte[][] pa = [.. Comandos(a)], pb = [.. Comandos(b)];
		return pa.Length == 1 && pb.Length == 1 && pa[0].AsSpan().SequenceEqual(pb[0]);
	}

	private static IEnumerable<byte[]> Comandos(Gesto g) =>
		g.Fio.Where(p => p.Length > 0
					  && (p[0] == (byte)Protocol.C2S.Verbo || p[0] == (byte)Protocol.C2S.Habilidade));

	/// <summary>O gesto arrancou uma linha de chat que contem isto.</summary>
	private static bool RegraFalou(Gesto g, string termo) =>
		g.Falas.Contains(termo, StringComparison.OrdinalIgnoreCase);

	/// <summary>Algum pacote deste opcode saiu (as teclas fixas: carga, guarda, subida).</summary>
	private static bool RegraTem(Gesto g, Protocol.C2S op) => Do(g, op).Length > 0;

	/// <summary>
	/// O PACOTE QUE UM `SendVerbo(cmd, arg)` PRODUZ, montado aqui pela mesma receita do `Protocol`.
	///
	/// Serve pra afirmar o que esta DENTRO do pacote sem escrever um decodificador: a comparacao e
	/// byte a byte com um pacote de referencia. E como esta bancada diz "o fio carrega `forma ssj1`,
	/// e nao `admin_forma ssj1` e nao um opcode novo".
	/// </summary>
	private static byte[] Referencia(string cmd, string arg)
	{
		NetDataWriter w = Protocol.Begin(Protocol.C2S.Verbo);
		w.Put(cmd);
		w.Put(arg);
		return w.CopyData();
	}

	// =====================================================================
	// TECLADO DE MENTIRA -- eventos de verdade, injetados no motor
	// =====================================================================
	/// <summary>
	/// AS DUAS METADES SAO PREENCHIDAS. `PhysicalKeycode` e o que o registro le (`Teclas.Fisica`) e
	/// o que o `InputMap` casa; `Keycode` e o que o chat e o menu leem pro ESC e pro ENTER. Um
	/// teclado de verdade manda os dois -- e um evento com so metade passaria pela metade do jogo,
	/// que e um jeito de a bancada medir menos do que parece.
	/// </summary>
	private static void Tecla(Key k, bool apertada) =>
		Input.ParseInputEvent(new InputEventKey
		{
			Keycode = k,
			PhysicalKeycode = k,
			Pressed = apertada,
		});

	private static void Toque(Key k) { Tecla(k, true); Tecla(k, false); }

	private static InputEventKey Evento(Key k) => new() { Keycode = k, PhysicalKeycode = k, Pressed = true };

	// =====================================================================
	// LER A TELA
	// =====================================================================
	private static T? Procurar<T>(Node raiz, Func<T, bool> quer) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t && quer(t)) return t;
			if (Procurar(f, quer) is { } achado) return achado;
		}
		return null;
	}

	private static IEnumerable<T> Todos<T>(Node raiz) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t) yield return t;
			foreach (T n in Todos<T>(f)) yield return n;
		}
	}

	/// <summary>O que o painel de chat DESENHOU -- e nao a lista interna dele.</summary>
	private static string TextoDoChat() =>
		Chat.Instancia is { } c && Procurar<RichTextLabel>(c, _ => true) is { } r ? r.GetParsedText() : "";

	private static Button? BotaoDoVerb(string nome) =>
		MenuJogo.Instancia is { } m
			? Procurar<Button>(m, b => b.Text == nome || b.Text.StartsWith(nome + "   [", StringComparison.Ordinal))
			: null;

	private static Node? Achar(string nome) =>
		Engine.GetMainLoop() is SceneTree t ? t.Root.FindChild(nome, true, false) : null;

	/// <summary>A mochila esta na tela? Ela nao tem `Instancia`; o que se pergunta e o node dela.</summary>
	private static bool MochilaNaTela() =>
		Achar("Inventario") is { } n && n.GetChildren().OfType<Control>().Any(c => c.Visible);

	/// <summary>Quantos paineis do HUD estao acesos. O painel do TAB e o unico que liga e desliga.</summary>
	private static int PaineisDoHud() =>
		Hud.Instancia is { } h ? Todos<PanelContainer>(h).Count(p => p.Visible) : 0;

	// =====================================================================
	// ESTADO
	// =====================================================================
	private IEnumerator<double>? _roteiro;
	private double _espera = 2.5;
	private string _configOriginal = "";
	private bool _tinhaConfig;
	private bool _restaurado;

	private const string ArquivoDeConfig = "user://config.json";

	/// <summary>
	/// ============================ A COPIA DE SEGURANCA EM DISCO -- ACHADO RODANDO ============================
	/// Guardar o config do dono numa VARIAVEL nao bastava, e o defeito apareceu na terceira rodada: a
	/// segunda foi interrompida no meio (fim de prazo do processo), morreu com uma ligacao de teste
	/// gravada, e a terceira copiou ESSA como se fosse a do dono -- e devolveu ela no fim, fielmente.
	/// A copia em memoria so sobrevive ao que o proprio processo faz.
	///
	/// Entao a copia vai pro DISCO, e ela e o sinal de "havia uma rodada em andamento": se este
	/// arquivo existe no comeco, a rodada anterior nao chegou a devolver nada, e o que esta AQUI e o
	/// original de verdade -- o `config.json` de agora e lixo de bancada. Ele e apagado no fim.
	/// ====================================================================================================
	/// </summary>
	private const string CopiaDeSeguranca = "user://config.json.bancada-tecla";

	/// <summary>Marca gravada quando NAO havia config nenhum -- pra distinguir de "config vazio".</summary>
	private const string NaoExistia = "(nao existia)";

	public override void _Ready()
	{
		// O CONFIG DO DONO, COPIADO ANTES DE QUALQUER COISA. Ver o cabecalho da classe.
		if (Godot.FileAccess.FileExists(CopiaDeSeguranca))
		{
			Nota("AVISO: a rodada anterior nao devolveu o config. Usando a copia de seguranca dela,"
				 + " e nao o config de agora (que e lixo de bancada).");
			_configOriginal = Godot.FileAccess.GetFileAsString(CopiaDeSeguranca);
		}
		else
		{
			_configOriginal = Godot.FileAccess.FileExists(ArquivoDeConfig)
				? Godot.FileAccess.GetFileAsString(ArquivoDeConfig) : NaoExistia;
			using Godot.FileAccess? f = Godot.FileAccess.Open(CopiaDeSeguranca, Godot.FileAccess.ModeFlags.Write);
			f?.StoreString(_configOriginal);
		}
		_tinhaConfig = _configOriginal != NaoExistia;

		Jandirus.Server.GameServer.EspiaoDeEntrada = bytes => { lock (_trava) _fio.Add(bytes); };
		_roteiro = Roteiro().GetEnumerator();
	}

	public override void _ExitTree()
	{
		Jandirus.Server.GameServer.EspiaoDeEntrada = null;
		Devolver();
	}

	/// <summary>Devolve o `config.json` do dono. Idempotente -- roda no fim do roteiro e na saida.</summary>
	private void Devolver()
	{
		if (_restaurado) return;
		_restaurado = true;

		if (_tinhaConfig)
		{
			using Godot.FileAccess? f = Godot.FileAccess.Open(ArquivoDeConfig, Godot.FileAccess.ModeFlags.Write);
			f?.StoreString(_configOriginal);
		}
		else if (Godot.FileAccess.FileExists(ArquivoDeConfig))
		{
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ArquivoDeConfig));
		}

		// E O REGISTRO VIVO TAMBEM: o processo continua rodando depois da bancada, e deixar o
		// `InputMap` com as teclas do teste faria o jogo desta janela responder ao que ela ligou.
		Teclas.Aplicar(Settings.Carregar());

		// A COPIA SO SE APAGA DEPOIS DE O ORIGINAL ESTAR DE VOLTA -- ela e o sinal de "havia uma
		// rodada em andamento", e apagar antes reabriria a janela em que uma interrupcao perde tudo.
		if (Godot.FileAccess.FileExists(CopiaDeSeguranca))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(CopiaDeSeguranca));
	}

	public override void _Process(double delta)
	{
		if (_roteiro == null) return;
		_espera -= delta;
		if (_espera > 0) return;
		if (!_roteiro.MoveNext()) { _roteiro = null; return; }
		_espera = _roteiro.Current;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable<double> Roteiro()
	{
		Nota("=================== BANCADA DAS TECLAS ===================");

		// ---------------------------------------------------------------- 0. o mundo de pe
		for (int i = 0; i < 400 && (C is not { Connected: true } || Verbos.Todos.Count == 0); i++)
			yield return 0.05;

		Checa("o mundo subiu e o catalogo de verbs chegou",
			  C is { Connected: true } && Verbos.Todos.Count > 0,
			  $"{Verbos.Todos.Count} verbs");
		Checa("o chat esta desenhando (e por ele que as recusas se leem)",
			  TextoDoChat().Length > 0, $"{TextoDoChat().Length} letras");

		// PARTE DE FABRICA. O `config.json` da maquina pode ja ter ligacao de tecla; sem esta linha
		// a bancada mediria o registro do dono e nao o padrao, e as checagens de conflito (que
		// afirmam quem e o dono de cada tecla) dariam vermelho por motivo errado.
		Teclas.RestaurarTudo();
		yield return 0.2;
		Checa("a bancada parte do padrao de fabrica (nenhum atalho de jogador)",
			  !Teclas.Ligados.Any(), $"{Teclas.Ligados.Count()} ligados");

		foreach (double d in DespertarUmaForma()) yield return d;
		foreach (double d in F1_MesmoPacote()) yield return d;
		foreach (double d in F2_MesmaRecusa()) yield return d;
		foreach (double d in F3_NaoFuraGate()) yield return d;
		foreach (double d in F4_ConflitoERestauro()) yield return d;
		foreach (double d in F5_Digitando()) yield return d;
		foreach (double d in F6_SobreviveAoRelog()) yield return d;
		foreach (double d in F7_AsFixas()) yield return d;

		// ---------------------------------------------------------------- fim
		//
		// O PLACAR SAI ANTES DA ARRUMACAO, e a ordem era a inversa: uma rodada morreu calada no
		// intervalo entre `Devolver()` e a impressao, e o resultado inteiro se perdeu depois de tudo
		// ter sido medido. O relatorio e o produto; a arrumacao tem `_ExitTree` como segunda chance,
		// e o relatorio nao tem nenhuma.
		Nota("==========================================================");
		Nota($"PLACAR: {_ok} OK, {_falha} FALHA");
		if (_falha > 0) foreach (string f in _reprovadas) Nota($"   falhou: {f}");
		Nota($"INJECAO: {_injOk} pegou, {_injFalha} PASSOU BATIDO");
		if (_injFalha > 0) foreach (string f in _injPassouBatido) Nota($"   passou batido: {f}");
		Nota("==========================================================");

		Devolver();
		yield return 0.2;
		Nota($"config do dono devolvido: {(_tinhaConfig ? "sim" : "nao havia nenhum")}");
	}

	// =====================================================================
	// PREPARO -- um corpo que desperta uma forma
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE A FORMA E FORCADA E DEPOIS DESFEITA ============================
	/// A tecla de forma so pode ser ligada a uma forma que este corpo DESPERTA (`FormasDespertas`), e
	/// "desperta" quer dizer maestria acima de zero -- ou seja, ja ter estado nela. Um personagem
	/// recem-criado nao desperta nenhuma, e a bancada nao teria o que ligar.
	///
	/// Entao ela usa o `admin_forma` (o corpo desta bancada e o host, logo admin) pra ENTRAR uma vez,
	/// `admin_dominar` pra assentar a maestria, e volta pra base pelo mesmo verb. O que se mede
	/// depois disso nao passa mais por admin nenhum: a tecla manda `forma`, que e comando de jogador
	/// e cai no `Avaliar`. O admin aqui e o equivalente a "este personagem ja tinha jogado".
	///
	/// A MAESTRIA E ASSENTADA E NAO ESPERADA -- medido: ela sobe a `100/(3*3600)` por segundo em
	/// forma, entao os seis segundos que a primeira rodada desta bancada esperou davam 0,015% e o
	/// pacote de ficha chegava com o campo ainda zerado. Esperar mais seria uma bancada de tres
	/// horas; o `admin_dominar` poe o numero de uma vez e a pergunta medida continua sendo a mesma
	/// ("este corpo desperta esta forma?").
	/// ================================================================================================
	/// </summary>
	private IEnumerable<double> DespertarUmaForma()
	{
		Nota("--- preparo: dar a este corpo uma forma despertada ---");

		C?.SendVerbo("admin_forma", "ssj1");
		yield return 1.5;

		C?.SendVerbo("admin_dominar", "");
		yield return 1.5;

		C?.SendVerbo("admin_forma", Catalogo.IdBase);
		yield return 1.5;

		Checa("o corpo voltou pra base antes de medir",
			  Catalogo.PorRede(C?.Atributos.FormaAtual ?? 0)?.Id == Catalogo.IdBase,
			  Catalogo.PorRede(C?.Atributos.FormaAtual ?? 0)?.Id ?? "?");
		Checa("...e passou a DESPERTAR o Super Saiyajin (e o que a tela de teclas oferece)",
			  FormasDespertas.Sei("ssj1"),
			  string.Join(",", FormasDespertas.Minhas().Select(d => d.Id)));
		Checa("...e continua NAO despertando o Blue (o alvo do teste de gate)",
			  !FormasDespertas.Sei("blue"));
	}

	// =====================================================================
	// F1 -- A TECLA MANDA O MESMO PACOTE QUE O BOTAO
	// =====================================================================
	private IEnumerable<double> F1_MesmoPacote()
	{
		Nota("--- F1: a tecla manda o MESMO pacote que o botao ---");

		const string alvo = "Toggle Knockback";
		Verbo? v = Verbos.PorChave(alvo);
		Checa("F1 o verb de referencia existe no catalogo deste personagem", v != null, alvo);
		if (v == null) yield break;

		// O BOTAO DE VERDADE. Abrir o menu, ir na aba e achar o `Button` -- e apertar o SINAL, e nao
		// chamar `v.Acionar()` por dentro. Se a ligacao `b.Pressed += ...` estivesse faltando, chamar
		// a acao por dentro passaria verde e o dedo do jogador nao faria nada.
		MenuJogo.Instancia?.Abrir();
		MenuJogo.Instancia?.IrPara(Verbos.Outros);
		yield return 0.3;

		Button? b = BotaoDoVerb(alvo);
		Checa("F1 o botao existe na tela, e nao esta apagado", b is { Disabled: false }, b?.Text ?? "nao achei");
		if (b == null) yield break;

		Marcar();
		b.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.45;
		Gesto botao = Colher("botao");

		MenuJogo.Instancia?.Fechar();
		yield return 0.2;

		// A TECLA. `Teclas.Ligar` e o mesmo caminho que a tela de teclas usa ao confirmar a captura.
		bool ligou = Teclas.Ligar(Key.J, new Atalho("verbo", v.ChaveOuNome, v.Nome));
		Checa("F1 o J aceitou a ligacao (tecla livre)", ligou && Teclas.AtalhoDe(Key.J)?.Chave == v.ChaveOuNome,
			  Teclas.AtalhoDe(Key.J)?.Rotulo ?? "nada");
		yield return 0.2;

		Marcar();
		Toque(Key.J);
		yield return 0.45;
		Gesto tecla = Colher("tecla J");

		Checa("F1 o botao pos exatamente um comando no fio", RegraUmComando(botao),
			  $"{Do(botao, Protocol.C2S.Verbo).Length} verbos");
		Checa("F1 a tecla pos exatamente um comando no fio", RegraUmComando(tecla),
			  $"{Do(tecla, Protocol.C2S.Verbo).Length} verbos");
		Checa("F1 OS BYTES SAO OS MESMOS (o que chegou no servidor, nao o que o cliente quis)",
			  RegraMesmoPacote(botao, tecla), Hex(botao) + " x " + Hex(tecla));
		Checa("F1 ...e o pacote e o `knockback` do canal de verbos, e nao outro comando",
			  Comandos(tecla).Any(p => p.AsSpan().SequenceEqual(Referencia("knockback", ""))),
			  Hex(tecla));

		// ============================ A RESPOSTA E UMA LINHA, E NAO "O QUE APARECEU NO CHAT" ============================
		// O chat e VIVO: o clima do planeta e os NPCs escrevem nele sozinhos, e uma bancada que
		// compare o trecho INTEIRO de duas janelas de tempo compara o clima junto. Isso reprovou uma
		// checagem certa numa rodada e passou na seguinte -- que e o pior dos dois mundos, porque uma
		// checagem que oscila deixa de ser lida. Entao toda comparacao de fala daqui pra baixo ancora
		// numa LINHA identificada pelo teor.
		// ============================================================================================================
		string rBotao = LinhaCom(botao, "arremess");
		string rTecla = LinhaCom(tecla, "arremess");
		Checa("F1 o servidor respondeu ao botao", rBotao.Length > 0, PrimeiraLinha(botao));
		Checa("F1 o servidor respondeu a tecla", rTecla.Length > 0, PrimeiraLinha(tecla));

		// O EFEITO, E NAO SO O PACOTE. O `knockback` alterna: se as duas respostas forem as DUAS
		// metades do par, os dois gestos foram processados de verdade -- uma tecla que mandasse o
		// pacote certo e fosse descartada no caminho daria pacotes iguais e uma resposta so.
		Checa("F1 ...e as duas respostas sao OPOSTAS (o estado alternou: os dois gestos AGIRAM)",
			  rBotao.Length > 0 && rTecla.Length > 0 && rBotao != rTecla, $"{rBotao} | {rTecla}");

		// ---------------------------------------------------------------- um verb de resposta fixa
		// O `knockback` prova o EFEITO por alternar; este prova a IGUALDADE da resposta, que o outro
		// nao pode provar justamente por alternar. Sao as duas metades da mesma afirmacao.
		const string quem = "Who";
		if (Verbos.PorChave(quem) is { } vq)
		{
			MenuJogo.Instancia?.Abrir();
			MenuJogo.Instancia?.IrPara(Verbos.Outros);
			yield return 0.3;

			Button? bq = BotaoDoVerb(quem);
			if (bq != null)
			{
				Marcar();
				bq.EmitSignal(BaseButton.SignalName.Pressed);
				yield return 0.45;
				Gesto bqG = Colher("botao quem");

				MenuJogo.Instancia?.Fechar();
				// TECLA LIVRE DE PROPOSITO (o G virou o `descer` quando o voo foi pro F): `Ligar`
				// EXPULSA o dono anterior e o `Desligar` do fim nao o devolve -- a bancada sairia
				// deixando o descer sem tecla no config da maquina.
				Teclas.Ligar(Key.Y, new Atalho("verbo", vq.ChaveOuNome, vq.Nome));
				yield return 0.25;

				Marcar();
				Toque(Key.Y);
				yield return 0.45;
				Gesto tqG = Colher("tecla Y");

				Checa("F1 (verb de resposta fixa) os bytes sao os mesmos", RegraMesmoPacote(bqG, tqG),
					  Hex(bqG) + " x " + Hex(tqG));
				// AS DUAS LINHAS DO RELATORIO, e nao o trecho de chat inteiro (ver a nota do `LinhaCom`):
				// o cabecalho ("-- N no mundo --") e a linha do jogador ("Nome (Raca) em Zona"). Duas
				// e melhor que uma aqui -- a primeira sozinha e um numero, e um numero igual e o tipo
				// de igualdade que acontece por acaso.
				string cabB = LinhaCom(bqG, "no mundo"), cabT = LinhaCom(tqG, "no mundo");
				string linB = LinhaCom(bqG, "(Saiyan)"), linT = LinhaCom(tqG, "(Saiyan)");
				Checa("F1 (verb de resposta fixa) a RESPOSTA do servidor e identica, letra por letra",
					  cabB.Length > 0 && cabB == cabT && linB.Length > 0 && linB == linT,
					  $"{cabB} / {linB}  |  {cabT} / {linT}");
				Teclas.Desligar("verbo", vq.ChaveOuNome);
			}
		}

		// ---------------------------------------------------------------- INJECAO
		//
		// O DEFEITO E REAL E TEM NOME: a tecla montar o proprio pacote. A forma mais provavel dele --
		// e a que o desenho evitou -- e a tecla guardar o verb pelo NOME MOSTRADO e mandar esse nome
		// como comando, porque o nome e o que a tela ja tinha na mao. O pacote sai, o servidor nem
		// reclama (cai no `default` do switch), e a tecla "nao faz nada" sem nenhuma pista.
		Marcar();
		C?.SendVerbo(alvo, "");        // <- o pacote que a segunda escrita mandaria
		yield return 0.45;
		Gesto forjado = Colher("injecao: comando pelo NOME");

		Injeta("F1 bytes iguais pega a tecla que manda o NOME em vez do comando",
			   !RegraMesmoPacote(botao, forjado), Hex(forjado));
		Injeta("F1 ...e a igualdade de resposta pega o mesmo defeito (o servidor nao respondeu)",
			   LinhaCom(forjado, "arremess").Length == 0, PrimeiraLinha(forjado));

		// E O CONTRA-EXEMPLO DA REGRA DE SILENCIO: ela nao pode ser verdade num gesto que MANDOU.
		Injeta("F1 a regra de silencio (usada na F2) pega um gesto que mandou pacote",
			   !RegraSilencio(tecla), $"{Comandos(tecla).Count()} comandos");

		Teclas.Desligar("verbo", v.ChaveOuNome);
		yield return 0.2;
	}

	private static string Hex(Gesto g)
	{
		byte[][] p = [.. Comandos(g)];
		return p.Length == 0 ? "(nada no fio)" : Convert.ToHexString(p[0]);
	}

	// =====================================================================
	// F2 -- A RECUSA E A MESMA
	// =====================================================================
	private IEnumerable<double> F2_MesmaRecusa()
	{
		Nota("--- F2: a recusa da tecla e a recusa do botao ---");

		// ---------------------------------------------------------------- 2a. VERB INDISPONIVEL
		//
		// "Declare Rival" pede alvo marcado (`Disponivel = TemAlvo`). Sem alvo, o botao existe e esta
		// APAGADO -- que e a recusa do botao: ele nao manda nada. A tecla nao pode mandar tambem.
		const string alvo = "Declare Rival";
		Verbo? v = Verbos.PorChave(alvo);
		Checa("F2 o verb indisponivel existe no catalogo", v != null, alvo);
		if (v == null) yield break;

		Checa("F2 ...e ele esta MESMO indisponivel agora (sem alvo marcado)", !v.PodeAgora,
			  $"alvo={C?.AlvoId ?? 0}");

		MenuJogo.Instancia?.Abrir();
		MenuJogo.Instancia?.IrPara(Verbos.Outros);
		yield return 0.3;

		Button? b = BotaoDoVerb(alvo);
		Checa("F2 o botao esta na tela e APAGADO -- e assim que o botao recusa",
			  b is { Disabled: true }, b == null ? "nao achei" : $"Disabled={b.Disabled}");
		MenuJogo.Instancia?.Fechar();
		yield return 0.2;

		Teclas.Ligar(Key.U, new Atalho("verbo", v.ChaveOuNome, v.Nome));
		yield return 0.2;

		Marcar();
		Toque(Key.U);
		yield return 0.45;
		Gesto tecla = Colher("tecla U (verb indisponivel)");

		Checa("F2 a tecla NAO pos pacote no fio (a mesma decisao do botao apagado)",
			  RegraSilencio(tecla), $"{Comandos(tecla).Count()} comandos");
		Checa("F2 ...mas a tecla FALA, e nomeia o verb (o botao apagado se explica por estar na tela)",
			  LinhaCom(tecla, alvo).Length > 0, PrimeiraLinha(tecla));

		Injeta("F2 a regra de silencio pega um gesto que mandou (contra-exemplo dela mesma)",
			   !RegraSilencio(new Gesto("forjado", [Referencia("rival", "0")], "")), "");
		Injeta("F2 a regra do 'a tecla fala' pega a tecla muda",
			   !RegraFalou(new Gesto("mudo", [], ""), alvo), "");

		Teclas.Desligar("verbo", v.ChaveOuNome);
		yield return 0.2;

		// ---------------------------------------------------------------- 2b. A MESMA FRASE, DOIS GESTOS
		//
		// ============================ AQUI NAO HA BOTAO PRA IMITAR ============================
		// A escada nao tem botao: ela sobe pelo duplo toque no C, que pede DIRECAO e nao forma. Entao
		// o "mesmo que o botao" da transformacao vira **mesmo que o C**, e a prova e uma condicao em
		// que os dois gestos tem que recusar pelo mesmo motivo: o corpo CAIDO.
		//
		// A frase e literal nos dois lados do servidor ("nao da, caido.", `GameServer.Formas.cs` nas
		// linhas do `Transformar` e do `TransformarPara`), e por isso ela e comparavel letra por
		// letra. Uma segunda escrita da guarda -- que e o erro que este caminho novo mais convidava --
		// apareceria aqui como duas frases parecidas e diferentes.
		// ==============================================================================
		//
		// ============================ E O C NAO CHEGA NO SERVIDOR CAIDO -- ACHADO RODANDO ============================
		// O duplo toque no C NAO manda pacote com o corpo no chao, e isso nao esta no `LerTeclaC`:
		// esta acima dele, no `LocalPlayer.LerAcoes`, que faz `if (_caido) { ...; return; }` antes de
		// chegar la. Ou seja o C tem DOIS portoes (o do cliente e o do servidor) e so o segundo e
		// alcancavel por teclado.
		//
		// Duas consequencias, e as duas viraram checagem aqui embaixo:
		//
		//   * a comparacao de FRASE tem que ser feita com os dois pedidos indo pelo FIO -- o do C e
		//     forjado (`SendTransformar(true)`, que e literalmente o que `LerTeclaC` mandaria), senao
		//     nao haveria segunda frase pra comparar;
		//   * e a tecla de forma nao tem o portao do cliente que o C tem: caido, ela manda um pacote
		//     por aperto pra ouvir nao. E exatamente o que o cabecalho do `Atalhos` diz que evitou no
		//     caso da forma nao despertada ("mandar pra ouvir nao a cada aperto e trafego por nada"),
		//     e o caso do corpo caido escapou. A checagem que cobra isso esta marcada, e ela reprova
		//     hoje -- e o unico achado desta bancada que nao e uma confirmacao.
		// ==========================================================================================================
		Teclas.Ligar(Key.Y, new Atalho("forma", "ssj1", "Super Saiyajin"));
		yield return 0.2;

		// ============================ O CORPO TEM QUE ESTAR CAIDO DE VERDADE ============================
		// A primeira rodada desta secao passou com o corpo DE PE: o `admin_kb` foi mandado com o NOME
		// e o verb quer o ID (o `PorNome` do servidor le id, apesar do nome dele). Sem alvo ele
		// responde "marque alguem antes", e a bancada seguiu medindo -- a tecla de forma transformou,
		// o C-C subiu um degrau, e as tres checagens desta secao reprovaram por um motivo que nao
		// tinha nada a ver com o que elas medem.
		//
		// Por isso o estado e CONFERIDO e nao suposto: `Sheet.Imobilizado` e o que o proprio jogo usa
		// pra saber que o corpo esta no chao (`LocalPlayer._caido`). Toda condicao que uma bancada
		// MONTA precisa ser lida de volta antes de valer como condicao.
		// ==========================================================================================
		C?.SendVerbo("admin_kb", (C?.LocalId ?? 0).ToString());
		for (int i = 0; i < 40 && C is { Sheet.Imobilizado: false }; i++) yield return 0.05;
		Checa("F2 o corpo esta MESMO caido antes de medir a recusa",
			  C is { Sheet.Imobilizado: true });

		// A TECLA DE FORMA, DE VERDADE (teclado).
		Marcar();
		Toque(Key.Y);
		yield return 0.7;
		Gesto porTecla = Colher("tecla de forma, caido");

		// O DUPLO TOQUE NO C, DE VERDADE (teclado) -- pra medir se ele chega no fio.
		Marcar();
		Toque(Key.C);
		yield return 0.15;
		Toque(Key.C);
		yield return 0.7;
		Gesto porCNoTeclado = Colher("duplo toque no C, caido");

		// E O PEDIDO DE SUBIDA PELO FIO -- o mesmo `SendTransformar(true)` que o `LerTeclaC` manda
		// quando o corpo esta de pe. E o unico jeito de ter a segunda frase pra comparar.
		Marcar();
		C?.SendTransformar(true);
		yield return 0.7;
		Gesto porCNoFio = Colher("pedido de subida pelo fio, caido");

		C?.SendVerbo("admin_reviver", (C?.LocalId ?? 0).ToString());
		for (int i = 0; i < 40 && C is { Sheet.Imobilizado: true }; i++) yield return 0.05;
		Checa("F2 ...e o corpo voltou de pe depois da medicao", C is { Sheet.Imobilizado: false });

		string fTecla = LinhaCom(porTecla, "caido");
		string fC = LinhaCom(porCNoFio, "caido");

		Checa("F2 caido: a tecla de forma foi recusada, e a recusa diz por que",
			  fTecla.Length > 0, PrimeiraLinha(porTecla));
		Checa("F2 caido: o pedido de subida foi recusado, e a recusa diz por que",
			  fC.Length > 0, PrimeiraLinha(porCNoFio));
		Checa("F2 A MESMA FRASE nos dois gestos -- a guarda e uma so, e nao duas parecidas",
			  fTecla.Length > 0 && fTecla == fC, $"{fTecla} | {fC}");
		Checa("F2 ...e a tecla de forma passou pelo canal de verbos, nao por um opcode novo",
			  Do(porTecla, Protocol.C2S.Verbo).Length == 1
			  && Do(porTecla, Protocol.C2S.Verbo)[0].AsSpan().SequenceEqual(Referencia("forma", "ssj1")),
			  Hex(porTecla));

		// ---------------------------------------------------------------- o portao que falta
		//
		// O C, CAIDO, NAO GASTA PACOTE (portao do `LocalPlayer.LerAcoes`). A tecla de forma gasta.
		// As duas linhas medem o mesmo assunto dos dois lados, e por isso ficam juntas: uma diz o que
		// o jogo ja faz, a outra diz o que a tecla nova deixou de fazer.
		Checa("F2 o C caido nao gasta pacote nenhum (o portao do cliente que ele ja tinha)",
			  !RegraTem(porCNoTeclado, Protocol.C2S.Transformar),
			  $"{Do(porCNoTeclado, Protocol.C2S.Transformar).Length} pacotes de subida");
		Checa("F2 [ACHADO] a tecla de forma caida TAMBEM devia calar -- hoje ela manda pra ouvir nao",
			  RegraSilencio(porTecla),
			  $"{Comandos(porTecla).Count()} comandos no fio com o corpo no chao");

		Injeta("F2 a comparacao de frase pega duas guardas parecidas e diferentes",
			   "nao da, caido." != "nao da: voce esta caido.", "");
	}

	/// <summary>
	/// A PRIMEIRA linha nova que contem isto -- e nao a ultima linha do trecho.
	///
	/// Era `l[^1]` e isso reprovou uma checagem certa: o clima do planeta escreve no chat sozinho, e
	/// a frase "o ceu se parte sobre a regiao" caiu depois da recusa e roubou o lugar dela. Num chat
	/// vivo a ultima linha nao e a resposta ao que voce acabou de fazer.
	/// </summary>
	private static string LinhaCom(Gesto g, string termo) =>
		Linhas(g.Falas).FirstOrDefault(l => l.Contains(termo, StringComparison.OrdinalIgnoreCase)) ?? "";

	private static string PrimeiraLinha(Gesto g) =>
		Linhas(g.Falas).FirstOrDefault() ?? "(nada no chat)";

	private static IEnumerable<string> Linhas(string bruto) =>
		bruto.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0);

	// =====================================================================
	// F3 -- A TECLA NAO FURA GATE
	// =====================================================================
	private IEnumerable<double> F3_NaoFuraGate()
	{
		Nota("--- F3: a tecla nao fura gate (os dois caminhos recusam) ---");

		// A CONDICAO DE PARTIDA E POSTA E CONFERIDA, e nao herdada da secao anterior. Na primeira
		// rodada ela foi herdada, a secao de cima deixou o corpo transformado por engano, e as tres
		// checagens de gate daqui reprovaram sem terem nada a ver com gate nenhum.
		C?.SendVerbo("forma", Catalogo.IdBase);
		yield return 0.8;

		string antes = Catalogo.PorRede(C?.Atributos.FormaAtual ?? 0)?.Id ?? "?";
		Checa("F3 o corpo esta na base antes de tentar", antes == Catalogo.IdBase, antes);

		// ---------------------------------------------------------------- 3a. O PORTAO DO CLIENTE
		//
		// A ligacao e FORJADA de proposito: a tela de teclas nunca ofereceria o Blue a este corpo. E
		// exatamente o caso que existe em jogo -- a ligacao e de MAQUINA e o catalogo e do
		// PERSONAGEM, entao quem tinha Blue ligado noutro personagem entra com o Blue ligado aqui.
		Teclas.Ligar(Key.B, new Atalho("forma", "blue", "Super Saiyajin Blue"));
		yield return 0.2;

		Marcar();
		Toque(Key.B);
		yield return 0.5;
		Gesto peloCliente = Colher("tecla de forma nao despertada");

		Checa("F3 (cliente) a tecla NAO virou pacote -- nao se manda pra ouvir nao",
			  RegraSilencio(peloCliente), $"{Comandos(peloCliente).Count()} comandos");
		Checa("F3 (cliente) ...e ela diz que o problema e o PERSONAGEM, e nao a tecla",
			  RegraFalou(peloCliente, "nao desperta"), PrimeiraLinha(peloCliente));

		// ---------------------------------------------------------------- 3b. O PORTAO DO SERVIDOR
		//
		// O MESMO PEDIDO, FORJADO NO FIO. E o que um cliente mexido faz, e e a metade que importa: o
		// portao do cliente e conforto (nao gastar pacote), o portao de verdade e o `Avaliar`. Uma
		// bancada que so medisse o primeiro estaria medindo a educacao do cliente.
		Marcar();
		C?.SendVerbo("forma", "blue");
		yield return 0.6;
		Gesto peloServidor = Colher("pedido de forma forjado no fio");

		string depois = Catalogo.PorRede(C?.Atributos.FormaAtual ?? 0)?.Id ?? "?";
		Checa("F3 (servidor) o pacote SAIU (a bancada mediu o portao, e nao a boa vontade do cliente)",
			  Do(peloServidor, Protocol.C2S.Verbo).Length == 1, Hex(peloServidor));
		Checa("F3 (servidor) e a forma NAO entrou", depois == Catalogo.IdBase, depois);
		// A RECUSA E PROCURADA PELO TEOR, e nao "veio alguma linha": o clima do planeta escreve no
		// chat sozinho, e "veio alguma linha" fica verde com o servidor calado e uma chuva caindo.
		Checa("F3 (servidor) ...e a recusa nomeia a forma que foi negada",
			  LinhaCom(peloServidor, "Blue").Length > 0, PrimeiraLinha(peloServidor));

		// ---------------------------------------------------------------- 3c. NAO PULA DEGRAU
		//
		// "Tecla 3 = SSJ3" e o que um jogador espera ao ligar a tecla, e e o que NAO pode acontecer.
		// Este corpo desperta o SSJ1; pedir o SSJ3 da base tem que bater no `Avaliar` igual ao C.
		Marcar();
		C?.SendVerbo("forma", "ssj3");
		yield return 0.6;
		Gesto salto = Colher("pedido de SSJ3 da base");

		string aposSalto = Catalogo.PorRede(C?.Atributos.FormaAtual ?? 0)?.Id ?? "?";
		Checa("F3 pedir SSJ3 da base NAO transforma (o atalho e escolha, nao salto)",
			  aposSalto == Catalogo.IdBase, aposSalto);
		Checa("F3 ...e a recusa explica que ele vem DEPOIS de outra forma",
			  LinhaCom(salto, "vem depois").Length > 0, PrimeiraLinha(salto));

		// ---------------------------------------------------------------- 3d. E A QUE ELE PODE, VAI
		//
		// SEM ESTA, TUDO ACIMA E VACUO. Uma tecla que nunca transforma passa em todos os testes de
		// gate desta secao -- inclusive num jogo em que a tecla de forma esteja simplesmente quebrada.
		Marcar();
		Toque(Key.Y);
		yield return 0.8;
		Gesto valida = Colher("tecla de forma despertada");

		string virou = Catalogo.PorRede(C?.Atributos.FormaAtual ?? 0)?.Id ?? "?";
		Checa("F3 a forma que o corpo DESPERTA entra pela tecla (senao o resto seria vacuo)",
			  virou == "ssj1", virou);
		Checa("F3 ...e o fio carregou `forma ssj1` -- sem opcode novo e fora do prefixo admin_",
			  Do(valida, Protocol.C2S.Verbo).Length == 1
			  && Do(valida, Protocol.C2S.Verbo)[0].AsSpan().SequenceEqual(Referencia("forma", "ssj1")),
			  Hex(valida));

		Injeta("F3 o portao do cliente pega a forma nao despertada (e deixa passar a despertada)",
			   !FormasDespertas.Sei("blue") && FormasDespertas.Sei("ssj1"), "");

		// volta pra base pelo caminho do jogador
		C?.SendVerbo("forma", Catalogo.IdBase);
		yield return 0.8;
		Checa("F3 pedir a base recua pelo mesmo caminho da tecla X",
			  Catalogo.PorRede(C?.Atributos.FormaAtual ?? 0)?.Id == Catalogo.IdBase,
			  Catalogo.PorRede(C?.Atributos.FormaAtual ?? 0)?.Id ?? "?");

		Teclas.Desligar("forma", "blue");
		yield return 0.2;
	}

	// =====================================================================
	// F4 -- CONFLITO E RESTAURO
	// =====================================================================
	private IEnumerable<double> F4_ConflitoERestauro()
	{
		Nota("--- F4: conflito, troca de dono e restaurar tudo ---");

		// A PERGUNTA E UMA SO, E ELA ENXERGA AS TRES FAMILIAS DE TECLA. Antes do registro, uma
		// varredura do `InputMap` responderia "o P esta livre" -- e o P abre o menu por um `if`.
		Checa("F4 o C tem dono, e o registro sabe quem e",
			  Teclas.DonoDe(Key.C) is { Fixa: false } d && d.Rotulo.Contains("Ki"),
			  Teclas.DonoDe(Key.C)?.Rotulo ?? "ninguem");
		Checa("F4 o P tem dono (a tecla que o `InputMap` sozinho diria estar livre)",
			  Teclas.DonoDe(Key.P) != null, Teclas.DonoDe(Key.P)?.Rotulo ?? "ninguem");
		Checa("F4 o ESC tem dono e e INELEGIVEL (e a saida de sete telas)",
			  Teclas.DonoDe(Key.Escape) is { Fixa: true } && !Teclas.Elegivel(Key.Escape),
			  Teclas.DonoDe(Key.Escape)?.Rotulo ?? "ninguem");
		Checa("F4 o ENTER e a barra tambem sao inelegiveis",
			  !Teclas.Elegivel(Key.Enter) && !Teclas.Elegivel(Key.Slash));
		Checa("F4 uma tecla livre nao tem dono nenhum", Teclas.DonoDe(Key.Z) == null);

		Checa("F4 ligar numa tecla FIXA e recusado",
			  !Teclas.Ligar(Key.Escape, new Atalho("verbo", "knockback", "x"))
			  && Teclas.AtalhoDe(Key.Escape) == null);

		// ---------------------------------------------------------------- a troca
		Verbo? v = Verbos.PorChave("Toggle Knockback");
		if (v == null) yield break;

		Checa("F4 antes da troca, o C esta projetado no `InputMap` da acao de carga",
			  InputMap.HasAction("transformar") && InputMap.ActionGetEvents("transformar").Count > 0,
			  $"{InputMap.ActionGetEvents("transformar").Count} eventos");

		Teclas.Ligar(Key.C, new Atalho("verbo", v.ChaveOuNome, v.Nome));
		yield return 0.3;

		Checa("F4 depois da troca, o C e do verb", Teclas.AtalhoDe(Key.C)?.Chave == v.ChaveOuNome,
			  Teclas.AtalhoDe(Key.C)?.Rotulo ?? "ninguem");
		Checa("F4 ...e o dono anterior ficou SEM TECLA (nao ha duas coisas na mesma tecla)",
			  Teclas.Teclado("transformar").Length == 0, Teclas.NomeDaAcao("transformar"));
		Checa("F4 ...e o `InputMap` acompanhou: a acao de carga nao tem mais evento",
			  InputMap.ActionGetEvents("transformar").Count == 0,
			  $"{InputMap.ActionGetEvents("transformar").Count} eventos");

		// O TESTE QUE VALE: apertar o C AGORA. O registro pode estar certo e a projecao errada.
		Marcar();
		Tecla(Key.C, true);
		yield return 0.35;
		Tecla(Key.C, false);
		yield return 0.35;
		Gesto cTrocado = Colher("C depois da troca");

		Checa("F4 apertar o C agora dispara o VERB", RegraUmComando(cTrocado), Hex(cTrocado));
		Checa("F4 ...e NAO carrega mais Ki (a tecla velha parou de verdade)",
			  !RegraTem(cTrocado, Protocol.C2S.Carregar),
			  $"{Do(cTrocado, Protocol.C2S.Carregar).Length} pacotes de carga");

		// ---------------------------------------------------------------- INJECAO: religar que SOMA
		//
		// O DEFEITO E O DO `ActionAddEvent` SEM `ActionEraseEvents`: religar acrescenta e o antigo
		// continua valendo. Ele e injetado no `InputMap` DE VERDADE, e nao numa amostra -- e o unico
		// jeito de saber que a checagem acima olha pro motor e nao pro dicionario da tabela.
		InputMap.ActionAddEvent("transformar", new InputEventKey { PhysicalKeycode = Key.C });
		yield return 0.2;

		Marcar();
		Tecla(Key.C, true);
		yield return 0.35;
		Tecla(Key.C, false);
		yield return 0.35;
		Gesto cSomado = Colher("C com a projecao somada");

		Injeta("F4 'a tecla velha parou' pega o religar que SOMA em vez de trocar",
			   RegraTem(cSomado, Protocol.C2S.Carregar),
			   $"{Do(cSomado, Protocol.C2S.Carregar).Length} pacotes de carga");
		Injeta("F4 ...e a leitura do `InputMap` tambem pega (a acao voltou a ter evento)",
			   InputMap.ActionGetEvents("transformar").Count > 0);

		// ---------------------------------------------------------------- restaurar tudo
		Teclas.RestaurarTudo();
		yield return 0.3;

		Checa("F4 restaurar devolve o C ao carregar de Ki",
			  Teclas.Teclado("transformar") is [Key.C], Teclas.NomeDaAcao("transformar"));
		Checa("F4 ...e apaga TODOS os atalhos do jogador", !Teclas.Ligados.Any(),
			  $"{Teclas.Ligados.Count()} ligados");
		Checa("F4 ...e devolve o apelido de nascenca junto (o A e a seta)",
			  Teclas.Teclado("move_left").Length == 2, Teclas.NomeDaAcao("move_left"));
		Checa("F4 ...e limpa o disco: o config nao guarda sobra",
			  Godot.FileAccess.FileExists(ArquivoDeConfig)
			  && !Godot.FileAccess.GetFileAsString(ArquivoDeConfig).Contains("Toggle Knockback"),
			  "");
		Checa("F4 ...e a projecao voltou junto (a acao de carga tem evento de novo)",
			  InputMap.ActionGetEvents("transformar").Count == 1,
			  $"{InputMap.ActionGetEvents("transformar").Count} eventos");

		Marcar();
		Tecla(Key.C, true);
		yield return 0.35;
		Tecla(Key.C, false);
		yield return 0.35;
		Gesto cDeVolta = Colher("C depois de restaurar");

		Checa("F4 e o C volta a CARREGAR KI, sem disparar verb nenhum",
			  RegraTem(cDeVolta, Protocol.C2S.Carregar) && RegraSilencio(cDeVolta),
			  $"carga={Do(cDeVolta, Protocol.C2S.Carregar).Length} verbos={Comandos(cDeVolta).Count()}");
	}

	// =====================================================================
	// F5 -- DIGITANDO NAO DISPARA
	// =====================================================================
	private IEnumerable<double> F5_Digitando()
	{
		Nota("--- F5: digitando no chat (e no menu) nao dispara ---");

		Verbo? v = Verbos.PorChave("Toggle Knockback");
		if (v == null) yield break;
		Teclas.Ligar(Key.J, new Atalho("verbo", v.ChaveOuNome, v.Nome));
		yield return 0.2;

		// O CHAT ABRE PELA TECLA DE VERDADE (ENTER), e nao por um `Abrir()` chamado por dentro: o
		// que se mede e o portao do `Foco`, e ele so vale se o estado tiver sido criado pelo caminho
		// que o jogador usa.
		Toque(Key.Enter);
		yield return 0.3;
		Checa("F5 o ENTER abriu a caixa de fala", Chat.Digitando && Foco.Digitando);

		Marcar();
		Toque(Key.J);
		yield return 0.45;
		Gesto digitando = Colher("J com o chat aberto");

		Checa("F5 escrevendo, o J NAO manda nada (cada letra viraria uma tecnica)",
			  RegraSilencio(digitando), $"{Comandos(digitando).Count()} comandos");

		Toque(Key.Escape);
		yield return 0.3;
		Checa("F5 o ESC fechou a caixa", !Chat.Digitando && !Foco.Digitando);

		Marcar();
		Toque(Key.J);
		yield return 0.45;
		Gesto solto = Colher("J com o chat fechado");

		// A METADE QUE TIRA O VACUO. "Nao mandou nada" e verdade tambem quando a tecla esta quebrada.
		Checa("F5 fechada a caixa, o MESMO J volta a mandar (a checagem acima nao e vacua)",
			  RegraUmComando(solto), Hex(solto));

		// ---------------------------------------------------------------- o menu, e nao so a busca
		//
		// `MenuJogo.Digitando` olhava so a BUSCA, e o menu tem outros cinco `LineEdit` (o aviso do
		// servidor, a conta de promover e banir, o alvo, o painel de admin). Digitando o nome de quem
		// ia ser banido o personagem andava e treinava -- ja era assim; com tecla ligavel em qualquer
		// tecla, cada letra do nome viraria um golpe. Aqui a pergunta e feita num campo QUE NAO E A
		// BUSCA, que e o unico jeito de a checagem enxergar a diferenca.
		MenuJogo.Instancia?.Abrir();
		MenuJogo.Instancia?.IrPara("Admin");
		yield return 0.4;

		LineEdit? busca = MenuJogo.Instancia is { } m0 ? Procurar<LineEdit>(m0, _ => true) : null;
		LineEdit[] campos = MenuJogo.Instancia is { } m1 ? [.. Todos<LineEdit>(m1)] : [];
		LineEdit? outro = campos.FirstOrDefault(c => c != busca);

		Checa("F5 o menu tem mais de um campo de texto (a busca nao e o unico)",
			  campos.Length > 1 && outro != null, $"{campos.Length} campos");

		if (outro != null)
		{
			outro.GrabFocus();
			yield return 0.25;
			Checa("F5 com o foco num campo QUE NAO E A BUSCA, o jogo se sabe digitando",
				  MenuJogo.Digitando && Foco.Digitando);

			Marcar();
			Toque(Key.J);
			yield return 0.45;
			Gesto noMenu = Colher("J escrevendo no painel de admin");
			Checa("F5 ...e o J nao manda nada por ali tambem", RegraSilencio(noMenu),
				  $"{Comandos(noMenu).Count()} comandos");

			outro.ReleaseFocus();
			yield return 0.2;
		}

		MenuJogo.Instancia?.Fechar();
		yield return 0.3;

		Injeta("F5 a regra de silencio pega o gesto que disparou no meio da palavra",
			   !RegraSilencio(new Gesto("forjado", [Referencia("knockback", "")], "")), "");
		Injeta("F5 ...e a metade 'volta a mandar' pega a tecla quebrada",
			   !RegraUmComando(new Gesto("quebrada", [], "")), "");

		Teclas.Desligar("verbo", v.ChaveOuNome);
		yield return 0.2;
	}

	// =====================================================================
	// F6 -- A LIGACAO SOBREVIVE A FECHAR O JOGO
	// =====================================================================
	private IEnumerable<double> F6_SobreviveAoRelog()
	{
		Nota("--- F6: a ligacao sobrevive a fechar o jogo ---");

		Verbo? v = Verbos.PorChave("Toggle Knockback");
		if (v == null) yield break;

		Teclas.Ligar(Key.J, new Atalho("verbo", v.ChaveOuNome, v.Nome));
		Teclas.Ligar(Key.Y, new Atalho("forma", "ssj1", "Super Saiyajin"));
		Teclas.Religar("attack", Key.N);
		yield return 0.3;

		string disco = Godot.FileAccess.FileExists(ArquivoDeConfig)
			? Godot.FileAccess.GetFileAsString(ArquivoDeConfig) : "";

		Checa("F6 o disco guardou o atalho do verb", disco.Contains(v.ChaveOuNome), "");
		Checa("F6 o disco guardou o atalho da forma", disco.Contains("ssj1"), "");
		Checa("F6 o disco guardou a acao religada", disco.Contains("attack") && disco.Contains("\"N\""), "");
		Checa("F6 ...e NAO guardou as acoes que ficaram no padrao (o config nao congela a fabrica)",
			  !disco.Contains("move_left"), "");

		// ============================ O RELOG E O DO BOOT, E NAO UM ATALHO ============================
		// `Settings.Carregar()` + `Teclas.Aplicar(...)` sao as DUAS LINHAS que o `Boot._Ready` roda,
		// nesta ordem. Chamar um `Recarregar()` escrito pra bancada mediria a funcao da bancada; isto
		// mede o caminho que o jogo faz ao abrir.
		// ==========================================================================================
		Settings novo = Settings.Carregar();
		Teclas.Aplicar(novo);
		yield return 0.3;

		Checa("F6 depois de reabrir, o atalho do verb esta la",
			  Teclas.AtalhoDe(Key.J)?.Chave == v.ChaveOuNome, Teclas.AtalhoDe(Key.J)?.Rotulo ?? "nada");
		Checa("F6 depois de reabrir, o atalho da forma esta la",
			  Teclas.AtalhoDe(Key.Y) is { Tipo: "forma", Chave: "ssj1" });
		Checa("F6 depois de reabrir, a acao religada esta la",
			  Teclas.Teclado("attack") is [Key.N], Teclas.NomeDaAcao("attack"));
		Checa("F6 ...e o `InputMap` foi reprojetado com a tecla nova (o motor, e nao so a tabela)",
			  InputMap.ActionGetEvents("attack").Count == 1
			  && InputMap.ActionGetEvents("attack")[0] is InputEventKey { PhysicalKeycode: Key.N },
			  $"{InputMap.ActionGetEvents("attack").Count} eventos");
		Checa("F6 ...e o rotulo gravado voltou junto (e o que a tela mostra quando o alvo nao existe)",
			  Teclas.AtalhoDe(Key.J)?.Rotulo == v.Nome, Teclas.AtalhoDe(Key.J)?.Rotulo ?? "");

		// E A TECLA REABERTA DISPARA. Sobreviver na tabela e metade: se a projecao nao voltasse, a
		// ligacao existiria na tela de teclas e nao faria nada no jogo.
		Marcar();
		Toque(Key.J);
		yield return 0.45;
		Gesto depoisDoRelog = Colher("J depois do relog");
		Checa("F6 e a tecla reaberta DISPARA (sobreviver na tabela nao basta)",
			  RegraUmComando(depoisDoRelog), Hex(depoisDoRelog));

		// ---------------------------------------------------------------- INJECAO: o config vazio
		//
		// E o defeito real de "gravar e nao ler de volta": a tabela nasce do padrao, a ligacao some, e
		// nada quebra -- o jogo abre normal e a tecla do jogador simplesmente nao existe mais.
		Teclas.Aplicar(new Settings());
		yield return 0.25;

		Injeta("F6 'o atalho voltou' pega o config que nao foi lido",
			   Teclas.AtalhoDe(Key.J) == null);
		Injeta("F6 'a acao religada voltou' pega o mesmo defeito",
			   Teclas.Teclado("attack") is not [Key.N], Teclas.NomeDaAcao("attack"));

		Marcar();
		Toque(Key.J);
		yield return 0.45;
		Injeta("F6 ...e 'a tecla reaberta dispara' fica vermelha junto",
			   RegraSilencio(Colher("J com o config perdido")));

		// devolve o estado gravado e segue
		Teclas.Aplicar(Settings.Carregar());
		yield return 0.25;
		Checa("F6 (volta) o estado gravado foi reposto depois da injecao",
			  Teclas.AtalhoDe(Key.J) != null && Teclas.Teclado("attack") is [Key.N]);

		Teclas.RestaurarTudo();
		yield return 0.3;
	}

	// =====================================================================
	// F7 -- AS TECLAS FIXAS CONTINUAM FUNCIONANDO
	// =====================================================================
	/// <summary>
	/// ============================ O CONTRA-EXEMPLO ============================
	/// Tudo acima mede o que a tecla configuravel GANHOU. Esta secao mede o que ela nao podia ter
	/// custado: as teclas que ja existiam e que sairam de `if (k.Keycode == Key.X)` espalhados por
	/// sete arquivos pra uma tabela so. Uma tabela errada nao quebra a compilacao, nao gera runtime,
	/// e faz o C parar de carregar Ki.
	///
	/// AS TRES DO CORPO SAO MEDIDAS NO FIO (carga, subida, guarda), porque delas ha pacote. As de
	/// INTERFACE nao mandam nada -- delas o que se mede e a TELA que abriu.
	/// ==========================================================================
	/// </summary>
	private IEnumerable<double> F7_AsFixas()
	{
		Nota("--- F7: as fixas continuam (C, ALT, E, P, I, TAB) ---");

		// ---------------------------------------------------------------- C: segurar carrega
		Marcar();
		Tecla(Key.C, true);
		yield return 0.5;
		Tecla(Key.C, false);
		yield return 0.4;
		Gesto carga = Colher("segurar o C");

		Checa("F7 o C segurado ainda CARREGA KI",
			  RegraTem(carga, Protocol.C2S.Carregar),
			  $"{Do(carga, Protocol.C2S.Carregar).Length} pacotes");
		Checa("F7 ...e soltar tambem viaja (o par liga/desliga)",
			  Do(carga, Protocol.C2S.Carregar).Length >= 2,
			  $"{Do(carga, Protocol.C2S.Carregar).Length} pacotes");

		// ---------------------------------------------------------------- C-C: sobe a escada
		Marcar();
		Toque(Key.C);
		yield return 0.12;
		Toque(Key.C);
		yield return 0.6;
		Gesto duplo = Colher("duplo toque no C");

		Checa("F7 o duplo toque no C ainda pede a SUBIDA",
			  RegraTem(duplo, Protocol.C2S.Transformar),
			  $"{Do(duplo, Protocol.C2S.Transformar).Length} pacotes");

		// volta pra base se subiu
		C?.SendVerbo("forma", Catalogo.IdBase);
		yield return 0.7;

		// ---------------------------------------------------------------- ALT: guarda
		Marcar();
		Tecla(Key.Alt, true);
		yield return 0.5;
		Tecla(Key.Alt, false);
		yield return 0.4;
		Gesto guarda = Colher("segurar o ALT");

		Checa("F7 o ALT ainda ergue a GUARDA",
			  RegraTem(guarda, Protocol.C2S.Guard),
			  $"{Do(guarda, Protocol.C2S.Guard).Length} pacotes");

		// ---------------------------------------------------------------- INJECAO nas tres do corpo
		//
		// A PROJECAO E APAGADA DE VERDADE. E o defeito que uma tabela errada produz: a acao existe,
		// o codigo que a le existe, e nenhuma tecla cai nela.
		InputMap.ActionEraseEvents("guard");
		yield return 0.2;

		Marcar();
		Tecla(Key.Alt, true);
		yield return 0.4;
		Tecla(Key.Alt, false);
		yield return 0.3;
		Injeta("F7 'o ALT ergue a guarda' pega a acao sem tecla projetada",
			   !RegraTem(Colher("ALT sem projecao"), Protocol.C2S.Guard));

		Teclas.Aplicar(Settings.Carregar());
		yield return 0.3;

		Marcar();
		Tecla(Key.Alt, true);
		yield return 0.4;
		Tecla(Key.Alt, false);
		yield return 0.3;
		Checa("F7 (volta) reaplicar o registro devolve a guarda",
			  RegraTem(Colher("ALT de volta"), Protocol.C2S.Guard));

		// ---------------------------------------------------------------- E: o registro responde
		//
		// ============================ O QUE DELA SE MEDE, E O QUE NAO ============================
		// O `MenuDeInteracao` so ABRE quando ha coisa ao alcance, e o berco desta bancada pode nao ter
		// nenhuma -- entao "a tela abriu" nao e uma afirmacao que ela possa fazer sempre. O que ela
		// afirma e a linha que MUDOU no port: o handler pergunta `Teclas.Bate("ui_interagir", k)`, e
		// e essa pergunta que uma tabela errada faria responder nao.
		//
		// A metade de TELA da mesma familia (uma tecla de interface que abre alguma coisa) esta
		// coberta logo abaixo pelo P, pelo I e pelo TAB -- que sairam do mesmo `if` no mesmo dia.
		// ====================================================================================
		Checa("F7 o E continua sendo do interagir, no registro",
			  Teclas.Bate("ui_interagir", Evento(Key.E))
			  && Teclas.DonoDe(Key.E) is { } dE && dE.Rotulo.Contains("interagir"),
			  Teclas.DonoDe(Key.E)?.Rotulo ?? "ninguem");
		Checa("F7 ...e nenhum atalho de jogador reivindica o E", Teclas.AtalhoDe(Key.E) == null);
		if (MenuDeInteracao.Instancia is { } mi)
			Nota($"  nota   o menu de interacao tem alvo por perto? dica=\"{mi.DicaNaTela}\"");

		// ---------------------------------------------------------------- P, I, TAB: a tela abriu
		Toque(Key.P);
		yield return 0.35;
		bool abriuMenu = MenuJogo.Instancia is { Visible: true };
		Toque(Key.P);
		yield return 0.35;
		bool fechouMenu = MenuJogo.Instancia is { Visible: false };
		Checa("F7 o P ainda abre e fecha o menu do jogo", abriuMenu && fechouMenu,
			  $"abriu={abriuMenu} fechou={fechouMenu}");

		Toque(Key.I);
		yield return 0.35;
		bool abriuMochila = MochilaNaTela();
		Toque(Key.I);
		yield return 0.35;
		Checa("F7 o I ainda abre e fecha a mochila", abriuMochila && !MochilaNaTela(),
			  $"abriu={abriuMochila}");

		int antesTab = PaineisDoHud();
		Toque(Key.Tab);
		yield return 0.35;
		int comTab = PaineisDoHud();
		Toque(Key.Tab);
		yield return 0.35;
		int depoisTab = PaineisDoHud();
		Checa("F7 o TAB ainda acende e apaga a lista de teclas",
			  comTab > antesTab && depoisTab == antesTab, $"{antesTab} -> {comTab} -> {depoisTab}");

		// ---------------------------------------------------------------- a ajuda LE o registro
		//
		// O painel do TAB escrevia "C" na mao. Quem religasse a carga pro G leria "C" ali -- a ajuda
		// ensinando errado justamente o jogador que mudou a tecla.
		Teclas.Religar("transformar", Key.H);
		yield return 0.25;
		bool ensinaH = Teclas.Ajuda().Any(a => a.Tecla.Contains("H", StringComparison.Ordinal));
		Checa("F7 religado, o painel do TAB passa a ensinar a tecla NOVA", ensinaH,
			  string.Join(" ", Teclas.Ajuda().Select(a => a.Tecla)));

		Marcar();
		Tecla(Key.H, true);
		yield return 0.4;
		Tecla(Key.H, false);
		yield return 0.3;
		Checa("F7 ...e a carga de Ki mudou de tecla de verdade",
			  RegraTem(Colher("segurar o H"), Protocol.C2S.Carregar));

		Teclas.RestaurarTudo();
		yield return 0.3;
		Checa("F7 (volta) restaurar devolve a carga ao C",
			  Teclas.Teclado("transformar") is [Key.C], Teclas.NomeDaAcao("transformar"));

		Injeta("F7 'a ajuda ensina a tecla nova' pega a lista escrita na mao",
			   !Teclas.Ajuda().Any(a => a.Tecla.Contains("H", StringComparison.Ordinal)), "");
	}
}
