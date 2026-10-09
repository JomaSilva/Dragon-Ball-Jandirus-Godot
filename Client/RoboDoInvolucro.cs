using System.Diagnostics;
using System.Runtime.CompilerServices;
using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O OUVIDO DA `--diaginvolucro`: conta os erros que o MOTOR loga com a assinatura da bancada.
///
/// E um `Logger` do Godot (`OS.AddLogger`) e nao uma leitura do proprio log: o erro medido nao e uma excecao
/// que chega ao C# do jogo -- o GodotSharp a apanha la dentro e so a IMPRIME. Sem este ouvido a bancada teria
/// de ler a propria saida pra saber se o defeito aconteceu.
///
/// O MOTOR CHAMA ISTO DE QUALQUER THREAD (o erro pode sair da thread de finalizacao ou de uma de carga), entao
/// as contas sao atomicas e nada aqui toca em node.
/// </summary>
public partial class EscutaDoInvolucro : Logger
{
	private int _total, _outros, _avisosDaRegua;

	/// <summary>Quantos erros com a assinatura da bancada o motor ja logou neste processo.</summary>
	public int Total => Volatile.Read(ref _total);

	/// <summary>Quantos ERROS de outra natureza -- a bancada so os relata.</summary>
	public int Outros => Volatile.Read(ref _outros);

	/// <summary>Quantas vezes o aviso de teste da regua chegou -- prova que o motor esta mesmo chamando este ouvido.</summary>
	public int AvisosDaRegua => Volatile.Read(ref _avisosDaRegua);

	/// <summary>O primeiro erro de outra natureza, pra o relatorio dizer qual foi.</summary>
	public string PrimeiroOutro = "";

	public override void _LogError(string function, string file, int line, string code, string rationale,
		bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
	{
		if (code.Contains(RoboDoInvolucro.Assinatura) || rationale.Contains(RoboDoInvolucro.Assinatura))
			Interlocked.Increment(ref _total);
		else if (code.Contains(RoboDoInvolucro.AvisoDaRegua) || rationale.Contains(RoboDoInvolucro.AvisoDaRegua))
			Interlocked.Increment(ref _avisosDaRegua);
		else if (errorType != 1 && Interlocked.Increment(ref _outros) == 1)   // 1 = aviso
			PrimeiroOutro = (code + " " + rationale).Trim();
	}
}

/// <summary>
/// ============================ `--diaginvolucro`: O "Handle is not initialized" DA ENTRADA NO MUNDO ============================
/// O QUE FOI VISTO (2026-10-08): em UMA de 33 rodadas da `--diagcarga`, o cliente logou tres vezes
///
///     ERROR: System.InvalidOperationException: Handle is not initialized.
///        at Godot.Bridge.ScriptManagerBridge.SwapGCHandleForType(...)   ScriptManagerBridge.cs:line 1284
///
/// enquanto vestia um corpo remoto: duas dentro do `ResourceLoader.Load` do `CharacterVisual.Trocar` (a folha
/// do cabelo) e uma no `GetFrameTexture` do `AtualizarCaixa`.
///
/// ============================ A CAUSA, QUE E DO MOTOR E QUE O JOGO ARMAVA ============================
/// 1. O cache de recursos do Godot guarda tambem os SUB-recursos de um `.tres`: cada quadro de uma folha e um
///    `AtlasTexture` com caminho proprio ("folha.tres::AtlasTexture_7").
/// 2. Quando o C# pede um quadro (`GetFrameTexture`), nasce um INVOLUCRO C# dele, e o involucro conta como uma
///    referencia. Enquanto alguem do motor tambem segura o quadro, a alca do involucro e FORTE; quando so sobra
///    o involucro, ela vira FRACA.
/// 3. A `CreationScreen.Miniatura` carrega cada folha, pega o quadro 0 e poe num botao. ANTES DA `FolhasPresas`
///    ela largava a folha: a folha morria na coleta seguinte, e o quadro ficava vivo pelo botao. Na entrada no
///    mundo o `Boot.MontarMundo` solta a tela de criacao -- e o quadro passava a existir SO pelo involucro, que
///    e lixo do C#.
/// 4. O coletor leva o involucro. Ate a thread de FINALIZACAO rodar, o quadro continua vivo no cache com uma
///    referencia que ja nao tem dono: um zumbi. Se nessa janela alguem recarrega a folha (o `Trocar` de um corpo
///    que entra na visao), o carregador de `.tres` ACHA o quadro no cache e o reusa.
/// 5. Ao reusar, a contagem do quadro vai de 1 pra 2 e o motor tenta trocar a alca fraca por uma forte. O alvo
///    ja foi coletado: a troca falha e o vinculo fica com "alca nula, tipo fraco"
///    (`CSharpLanguage::_instance_binding_reference_callback`, modules/mono/csharp_script.cpp do 4.7.1 -- o ramo
///    do incremento nao confere `is_released()`).
/// 6. Dali em diante, toda vez que a contagem daquele quadro for de 1 pra 2 o motor chama
///    `SwapGCHandleForType(0)`, e `GCHandle.FromIntPtr(0)` estoura. O GodotSharp apanha, loga e segue. Isso para
///    quando o C# pede o quadro de novo: nasce um involucro novo e o vinculo se refaz.
///
/// CADA PASSO FOI MEDIDO FORA DO JOGO (2026-10-08, um projeto minimo com o motor e tres folhas de cabelo; ele nao
/// mora no repo): com o finalizador preso, recarregar a folha nao loga nada; solto, o PRIMEIRO pedido do quadro
/// loga um erro e o segundo nenhum; o quadro zumbi so DESENHADO (sem ninguem do C# pedi-lo) loga a cada redesenho,
/// 10 de 10, e sai pixel a pixel igual ao PNG; e em 242 corridas com o finalizador solto por outra thread no meio
/// do `Load`, 54 logaram dentro do `Load` e de novo no pedido, 31 so no pedido e 157 nenhuma. Os dois remedios
/// testados la zeram tudo: SEGURAR a folha (ela nunca morre, entao o quadro nunca fica so com o involucro) e
/// descartar o involucro logo depois do uso (`Dispose`). ESTA bancada prova a outra metade: que o caminho de
/// PRODUCAO arma o zumbi, e o que acontece com o corpo vestido nessa hora.
///
/// ============================ O QUE ELA FAZ, E COM QUE CODIGO ============================
/// So codigo de producao no caminho medido: a `CreationScreen` de verdade (montada como o `Boot.PrepararCriacao`
/// a monta e solta como o `Boot.MontarMundo` a solta) e `CharacterVisual.Vestir` / `SetMotion` em corpos novos
/// (o que o `World.VestirCorpoInteiro` e o `RemotePlayer.Receive` chamam). O que a bancada acrescenta e a HORA:
///
///   * A TRAVA: um objeto cujo finalizador para a thread de finalizacao do .NET ate a bancada soltar. E o que
///     segura aberta, pelo tempo que for preciso, a janela que em jogo dura milissegundos.
///   * O AJUDANTE: outra thread, que solta a trava um tanto depois de o vestir comecar -- a corrida de verdade,
///     com a thread de finalizacao chegando no MEIO do `Vestir`.
///
/// CENAS:
///   0. a regua: o ouvido escuta o motor, e a conta de pixel sabe dizer NAO (um corpo sem cabelo reprova);
///   1. o controle: os corpos vestidos num processo que nunca montou a tela de criacao;
///   2. a ficha que chega depois do corpo (o `PeerLook` atrasado, corpo virado pro norte): deterministica --
///      o erro sai quando o corpo vira pro sul;
///   3. a corrida: o finalizador solto no meio do vestir, varrendo o atraso;
///   4. a contraprova: a mesma sequencia com o finalizador LIVRE;
///   5. o login de GENTE: as cenas 2 e 3 de novo, depois de o `Aquecimento` de producao completar as duas filas;
///   6. a porta e a unica: os fontes do cliente lidos atras de uma carga de folha por fora da `FolhasPresas`.
///
/// E A FOTO: cada cena fotografa os corpos com a arvore pausada (o relogio de animacao parado no quadro 0) e
/// compara com os do controle, pixel a pixel. "Ficou sem camada?" e respondido ali, e nao por contagem de node.
///
/// ============================ O QUE ELA MEDIU (2026-10-08) ============================
/// ANTES DO CONSERTO a bancada fechava com tres falhas, que eram o defeito -- e sao hoje o que a rodada de
/// injecao (`--involucrodefeito`) tem que reproduzir:
///
///   * a tela de criacao tem 227 miniaturas, e depois de ela sair e de UMA coleta 225 quadros (de 221 folhas)
///     estavam no cache com a folha ja fora dele;
///   * cena 2: nenhum erro no vestir e 10 na virada pro sul (um ou dois por corpo);
///   * cena 3: 7 ou 8 de 16 corridas logavam, sempre no corpo que estava sendo vestido quando o finalizador
///     chegava -- uma delas com as pilhas do log de origem (dentro do `ResourceLoader.Load` do `Trocar` do
///     cabelo e, em seguida, no `GetFrameTexture` que o `SetSpriteFrames` do mesmo `Trocar` dispara);
///   * cena 5: mesmo com o aquecimento completo sobravam 6 quadros orfaos, de folhas fora do catalogo de
///     aparencia (`cat.tres`, `Planets.tres`) -- sem erro nenhum em 7 rodadas, porque ninguem as vestia.
///
/// O QUE NUNCA FALHOU: em TODAS as fotos (a cena 2, as 16 corridas, a contraprova) os oito corpos saem iguais
/// aos do controle, pixel a pixel. O erro e ruido de log: o quadro reusado e valido e o vinculo se refaz
/// sozinho. E com o finalizador livre (cena 4) nunca sobrou zumbi nem erro.
///
/// O CONSERTO e a `FolhasPresas`: toda folha do cliente e carregada por ela e fica presa pelo processo inteiro.
/// DEPOIS dele: 0 de 227 quadros orfaos e nenhum erro em nenhuma cena (17 conferencias), a rodada de injecao
/// reprova as quatro contas acima, e de carona o segundo vestir dos oito corpos cai de 61 pra 2,2 ms -- a folha
/// ja esta na mao, sem volta ao `ResourceLoader`.
///
/// ============================ COMO RODAR ============================
/// Um processo so, sem rede e sem servidor, COM JANELA (sem ela os erros sao contados e as fotos nao saem):
///
///     Godot --path . --position 1920,0 --audio-driver Dummy --semfoco --resolution 1280x720 --diaginvolucro
///
///     --involucrocorridas &lt;n&gt;   quantas corridas a cena 3 faz (padrao 16; cada uma remonta a tela de criacao)
///     --involucrodefeito          A RODADA DE INJECAO, um processo so dela: liga `FolhasPresas.SemSegurarDeTeste`
///                                 antes de qualquer carga. As conferencias do conserto TEM que reprovar, e a
///                                 rodada termina sem falha justamente porque reprovaram: procure
///                                 "(defeito injetado: ...) a mesma regua REPROVA".
///
/// As fotos saem em `user://` (com o APPDATA desviado, como toda bancada).
/// ==========================================================================================================
/// </summary>
public partial class RoboDoInvolucro : Node2D
{
	/// <summary>O texto que identifica o erro medido, como o runtime do .NET o escreve.</summary>
	public const string Assinatura = "Handle is not initialized";

	/// <summary>O aviso que a cena 0 manda de proposito, pra provar que o motor chama o ouvido.</summary>
	public const string AvisoDaRegua = "[involucro] aviso de teste do ouvido";

	private const int Corpos = 8;
	private const float Escala = 3f;
	private const int MeiaCelula = 70;

	/// <summary>Um verde-acinzentado chapado: so precisa ser um fundo liso e igual em todas as fotos.</summary>
	private static readonly Color Fundo = new(0.30f, 0.34f, 0.30f);

	private readonly EscutaDoInvolucro _escuta = new();
	private readonly List<string> _fotos = [];
	private int _ok, _falha;

	private VisualCatalog _cat = null!;
	private (Appearance Ap, string Raca, string Genero)[] _fichas = [];
	private Label _titulo = null!;

	/// <summary>
	/// O PAI DOS CORPOS, e ele e PAUSAVEL de proposito. A bancada roda com `ProcessMode.Always` (ela tem que andar
	/// com a arvore pausada), e filho HERDA o modo do pai: pendurados direto nela, os corpos continuavam com o
	/// relogio de animacao andando durante a "pausa". MEDIDO na primeira rodada desta bancada: tres dos oito
	/// corpos saiam 36 pixels diferentes do controle em TODA rodada, com erro ou sem -- eram os olhos, na piscada
	/// do corpo parado (tres quadros de 0,1 s), fotografada em fases diferentes. Nada a ver com o defeito medido.
	/// </summary>
	private Node2D _palcoDosCorpos = null!;
	private Image? _controle;
	private long _vestirDoControleUs;

	/// <summary>`--involucrodefeito`: a rodada de injecao -- ver <see cref="ChecaDoConserto"/>.</summary>
	private static readonly bool ComDefeito = Array.IndexOf(OS.GetCmdlineArgs(), "--involucrodefeito") >= 0;

	private static void Nota(string linha) => GD.Print("[involucro] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else _falha++;
	}

	/// <summary>
	/// UMA CONFERENCIA QUE O CONSERTO DEIXA VERDE -- e que a rodada de injecao TEM que deixar vermelha.
	///
	/// Na rodada normal ela e uma <see cref="Checa"/> comum. Com `--involucrodefeito` a porta volta a nao segurar
	/// a folha (`FolhasPresas.SemSegurarDeTeste`, o jogo de antes), e a MESMA conta tem que reprovar: ai a linha
	/// passa justamente por ter reprovado. Sem essa rodada a bancada so saberia ficar verde -- e depois do
	/// conserto nao ha mais zumbi nenhum pra ela contar.
	/// </summary>
	private void ChecaDoConserto(string oque, bool sadio, string detalhe)
	{
		if (ComDefeito) Checa($"(defeito injetado: a porta nao segura a folha) a mesma regua REPROVA -- {oque}", !sadio, detalhe);
		else Checa(oque, sadio, detalhe);
	}

	// =====================================================================
	// A TRAVA E O AJUDANTE -- o que a bancada acrescenta ao caminho de producao
	// =====================================================================
	/// <summary>
	/// UM OBJETO CUJO FINALIZADOR PARA A THREAD DE FINALIZACAO. O .NET finaliza numa thread so, em fila: enquanto
	/// este finalizador espera, nenhum involucro coletado depois dele devolve a referencia que segurava.
	/// </summary>
	private sealed class Trava
	{
		public static readonly ManualResetEventSlim Solta = new(false);
		public static volatile bool Dentro;

		~Trava()
		{
			Dentro = true;
			Solta.Wait();
			Dentro = false;
		}
	}

	/// <summary>A thread de finalizacao esta parada na trava agora? Enquanto estiver, esperar por ela trava a bancada.</summary>
	private bool _presa;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void NovaTrava() => _ = new Trava();

	/// <summary>
	/// PRENDE a thread de finalizacao. Tem que ser chamado ANTES de os involucros que interessam virarem lixo: os
	/// que forem coletados na mesma coleta da trava podem entrar na fila na frente dela.
	/// </summary>
	private bool Travar()
	{
		Trava.Solta.Reset();
		NovaTrava();
		GC.Collect();
		var sw = Stopwatch.StartNew();
		while (!Trava.Dentro && sw.ElapsedMilliseconds < 3000) Thread.Sleep(1);
		_presa = Trava.Dentro;
		return _presa;
	}

	/// <summary>Solta a thread de finalizacao e espera a fila dela esvaziar.</summary>
	private void Soltar()
	{
		Trava.Solta.Set();
		GC.WaitForPendingFinalizers();
		_presa = false;
	}

	/// <summary>
	/// Coleta ate nao sobrar nada: uma folha morre em cascata (ela, depois os quadros, depois o PNG), e cada
	/// degrau precisa de uma coleta E de a fila de finalizacao rodar.
	/// </summary>
	private void ColetarTudo()
	{
		if (_presa) throw new InvalidOperationException("ColetarTudo com a thread de finalizacao presa: travaria a bancada");
		for (int i = 0; i < 4; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
	}

	private readonly ManualResetEventSlim _vai = new(false);
	private long _alvo;
	private volatile bool _fimDoAjudante;

	/// <summary>
	/// O AJUDANTE: espera o sinal, gira ate o instante combinado e solta a trava. Girar (em vez de dormir) e o que
	/// da precisao de dezenas de microssegundos -- a janela medida tem um ou dois milissegundos.
	/// </summary>
	private void LigarOAjudante()
	{
		var t = new Thread(() =>
		{
			while (true)
			{
				_vai.Wait();
				if (_fimDoAjudante) return;
				_vai.Reset();
				while (Stopwatch.GetTimestamp() < Volatile.Read(ref _alvo)) { }
				Trava.Solta.Set();
			}
		}) { IsBackground = true, Name = "SoltaOFinalizador" };
		t.Start();
	}

	// =====================================================================
	// O PALCO
	// =====================================================================
	private static Vector2 LugarDe(int i) => new(160 + (i % 4) * 320, 230 + (i / 4) * 300);

	private static Rect2I CelulaDe(int i)
	{
		Vector2 c = LugarDe(i);
		return new Rect2I((int)c.X - MeiaCelula, (int)c.Y - MeiaCelula, MeiaCelula * 2, MeiaCelula * 2);
	}

	private void MontarPalco()
	{
		AddChild(new ColorRect { Name = "Fundo", Color = Fundo, Size = new Vector2(4000, 4000), Position = new Vector2(-500, -500), ZIndex = -100 });
		_titulo = new Label { Name = "Titulo", Position = new Vector2(24, 16), Text = "" };
		_titulo.AddThemeFontSizeOverride("font_size", 22);
		AddChild(_titulo);
		_palcoDosCorpos = new Node2D { Name = "Corpos", ProcessMode = ProcessModeEnum.Pausable };
		AddChild(_palcoDosCorpos);
		for (int i = 0; i < Corpos; i++)
		{
			// O ROTULO FICA FORA DA CELULA COMPARADA: ele e igual em todas as fotos, mas texto nao entra em conta de corpo.
			var rotulo = new Label
			{
				Name = $"Rotulo{i}",
				Position = LugarDe(i) + new Vector2(-MeiaCelula, MeiaCelula + 6),
				Text = $"corpo {i}: {_fichas[i].Ap.Cabelo} / {_fichas[i].Genero}",
			};
			rotulo.AddThemeFontSizeOverride("font_size", 13);
			AddChild(rotulo);
		}
	}

	/// <summary>Espera <paramref name="n"/> quadros DESENHADOS (o sinal sai com a arvore pausada tambem).</summary>
	private async Task Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
	}

	private Image? Foto() => DisplayServer.GetName() == "headless" ? null : GetViewport().GetTexture().GetImage();

	private string Gravar(Image img, string nome)
	{
		string caminho = ProjectSettings.GlobalizePath($"user://involucro-{nome}.png");
		img.SavePng(caminho);
		_fotos.Add(caminho);
		return caminho;
	}

	/// <summary>Quantos pixels da celula diferem entre as duas fotos. Comparacao EXATA: mesma placa, mesmo quadro parado.</summary>
	private static int Diferentes(Image a, Image b, Rect2I r)
	{
		int n = 0;
		for (int y = r.Position.Y; y < r.End.Y; y++)
			for (int x = r.Position.X; x < r.End.X; x++)
				if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
		return n;
	}

	/// <summary>Quantos pixels da celula NAO sao o fundo -- o corpo. Zero quer dizer que a conta acima mediria nada.</summary>
	private static int DeCorpo(Image a, Rect2I r)
	{
		Color fundo = a.GetPixel(r.Position.X, r.Position.Y);
		int n = 0;
		for (int y = r.Position.Y; y < r.End.Y; y++)
			for (int x = r.Position.X; x < r.End.X; x++)
				if (a.GetPixel(x, y) != fundo) n++;
		return n;
	}

	private static void Moldura(Image img, Rect2I r, Color cor)
	{
		const int g = 3;
		img.FillRect(new Rect2I(r.Position.X - g, r.Position.Y - g, r.Size.X + 2 * g, g), cor);
		img.FillRect(new Rect2I(r.Position.X - g, r.End.Y, r.Size.X + 2 * g, g), cor);
		img.FillRect(new Rect2I(r.Position.X - g, r.Position.Y, g, r.Size.Y), cor);
		img.FillRect(new Rect2I(r.End.X, r.Position.Y, g, r.Size.Y), cor);
	}

	/// <summary>O controle em cima, a rodada embaixo, e uma moldura vermelha nos corpos cujo vestir logou o erro.</summary>
	private Image LadoALado(Image controle, Image rodada, int[] errosPorCorpo)
	{
		int w = controle.GetWidth(), h = controle.GetHeight();
		Image saida = Image.CreateEmpty(w, h * 2, false, controle.GetFormat());
		saida.BlitRect(controle, new Rect2I(0, 0, w, h), Vector2I.Zero);
		saida.BlitRect(rodada, new Rect2I(0, 0, w, h), new Vector2I(0, h));
		for (int i = 0; i < Corpos; i++)
		{
			if (errosPorCorpo[i] == 0) continue;
			Rect2I c = CelulaDe(i);
			Moldura(saida, c, Colors.Red);
			Moldura(saida, new Rect2I(c.Position + new Vector2I(0, h), c.Size), Colors.Red);
		}
		return saida;
	}

	// =====================================================================
	// AS FICHAS
	// =====================================================================
	/// <summary>O nome da PRIMEIRA animacao gravada no `.tres` -- a que o `AnimatedSprite2D` assume ao receber a folha.</summary>
	private static string PrimeiraAnimacao(string texto)
	{
		const string chave = "\"name\": &\"";
		int i = texto.IndexOf(chave, StringComparison.Ordinal);
		if (i < 0) return "";
		i += chave.Length;
		return texto[i..texto.IndexOf('"', i)];
	}

	/// <summary>
	/// OITO FICHAS, tiradas do `visual.json` como as de qualquer habitante: um penteado e uma peca de roupa cada.
	///
	/// A ROUPA E ESCOLHIDA LENDO O TEXTO DO `.tres`, e nao carregando a folha (carregar aqui poria involucros da
	/// bancada no caminho medido): so entra peca que TEM `default_south` e cuja primeira animacao e OUTRA. E o
	/// caso em que a miniatura da tela de criacao tira o quadro de `default_south` e o `Trocar` nao o pede na
	/// hora -- o quadro so e pedido quando o corpo fica de frente, parado. O cabelo nao tem `default_south`: a
	/// miniatura dele e o quadro 0 de `walk_south`, que e tambem o primeiro que o `Trocar` pede.
	/// </summary>
	private (Appearance, string, string)[] MontarFichas()
	{
		var cabelos = _cat.Cabelos.Where(c => c.Sprite is { Length: > 0 } s && ResourceLoader.Exists(s)).ToList();
		var roupas = new List<string>();
		foreach (string peca in _cat.Roupas)
		{
			if (!Godot.FileAccess.FileExists(peca)) continue;
			string texto = Godot.FileAccess.GetFileAsString(peca);
			if (texto.Contains("&\"default_south\"") && PrimeiraAnimacao(texto) != "default_south") roupas.Add(peca);
		}
		Nota($"   --    catalogo: {cabelos.Count} penteados com folha, {_cat.Roupas.Count} pecas de roupa ({roupas.Count} com `default_south` que nao e a primeira animacao)");

		var fichas = new (Appearance, string, string)[Corpos];
		for (int i = 0; i < Corpos; i++)
		{
			var ap = new Appearance { Cabelo = cabelos[i * cabelos.Count / Corpos].Nome };
			if (roupas.Count > 0) ap.Roupa.Add(new PecaDeRoupa(roupas[i * roupas.Count / Corpos]));
			fichas[i] = (ap, "Human", i % 2 == 0 ? "Male" : "Female");
		}
		return fichas;
	}

	// =====================================================================
	// UMA RODADA -- a tela de criacao, a hora do finalizador, o vestir e a foto
	// =====================================================================
	private sealed class Rodada
	{
		public Image? Foto;
		public readonly int[] ErrosPorCorpo = new int[Corpos];
		public readonly int[] ViradaPorCorpo = new int[Corpos];
		public readonly long[] FimUs = new long[Corpos];
		public int ErrosNaTela, ErrosNoDesenho, ErrosNaVirada, Miniaturas, Zumbis;
		public bool Prendeu;

		/// <summary>As folhas (sem repetir) cujo quadro de miniatura ficou no cache sem dono.</summary>
		public readonly SortedSet<string> FolhasDosZumbis = new(StringComparer.Ordinal);

		public int ErrosNoVestir => ErrosPorCorpo.Sum();
		public int Erros => ErrosNaTela + ErrosNoVestir + ErrosNoDesenho + ErrosNaVirada;
		public long TotalUs => FimUs[^1];
	}

	/// <summary>Os botoes com miniatura da tela: de cada um, a folha e o caminho do quadro no cache.</summary>
	private static void JuntarMiniaturas(Node raiz, List<(string Folha, string Quadro)> saida)
	{
		foreach (Node n in raiz.GetChildren())
		{
			if (n is Button { Icon: AtlasTexture at })
			{
				string caminho = at.ResourcePath;
				int corte = caminho.IndexOf("::", StringComparison.Ordinal);
				if (corte > 0) saida.Add((caminho[..corte], caminho));
			}
			JuntarMiniaturas(n, saida);
		}
	}

	/// <param name="comTela">Monta e solta a `CreationScreen` antes de vestir -- o caminho do jogo.</param>
	/// <param name="prender">Prende a thread de finalizacao antes de soltar a tela (so com <paramref name="comTela"/>).</param>
	/// <param name="atrasoUs">Com a thread presa: quantos microssegundos depois de o vestir comecar o ajudante a solta. Negativo = so depois do vestir inteiro.</param>
	/// <param name="fichaTardia">Os corpos ja existem virados pro NORTE quando a ficha chega, e viram pro sul depois.</param>
	private async Task<Rodada> UmaRodada(string titulo, (Appearance Ap, string Raca, string Genero)[] fichas,
		bool comTela, bool prender = false, int atrasoUs = -1, bool fichaTardia = false)
	{
		var r = new Rodada();
		_titulo.Text = titulo;

		if (comTela)
		{
			int antesDaTela = _escuta.Total;
			// A TELA DE CRIACAO DE VERDADE, escondida, como o `Boot.PrepararCriacao` a monta no lobby.
			var tela = new CreationScreen { Name = "CriacaoDaBancada", Visible = false };
			AddChild(tela);
			await Quadros(2);

			var miniaturas = new List<(string Folha, string Quadro)>();
			JuntarMiniaturas(tela, miniaturas);
			r.Miniaturas = miniaturas.Count;

			// O TEMPO DE LOBBY, comprimido: as folhas que so a `Miniatura` segurava morrem; os quadros ficam, pelos botoes.
			ColetarTudo();

			if (prender) r.Prendeu = Travar();

			// E A TELA SAI COMO NO `Boot.MontarMundo`: `QueueFree`, e ela morre no fim do quadro.
			tela.QueueFree();
			await Quadros(2);

			// UMA coleta leva os involucros dos quadros. Com a thread presa os finalizadores ficam na fila: o zumbi.
			if (_presa) GC.Collect();
			else ColetarTudo();

			foreach ((string folha, string quadro) in miniaturas)
			{
				if (!ResourceLoader.HasCached(quadro) || ResourceLoader.HasCached(folha)) continue;
				r.Zumbis++;
				r.FolhasDosZumbis.Add(folha);
			}
			r.ErrosNaTela = _escuta.Total - antesDaTela;
		}

		// O RELOGIO DE ANIMACAO PARA: a foto de toda rodada mostra o quadro 0 de cada camada.
		GetTree().Paused = true;
		var corpos = new CharacterVisual[Corpos];
		for (int i = 0; i < Corpos; i++)
		{
			corpos[i] = new CharacterVisual { Name = $"Corpo{i}", Position = LugarDe(i), Scale = new Vector2(Escala, Escala) };
			_palcoDosCorpos.AddChild(corpos[i]);
			if (fichaTardia) corpos[i].SetMotion(Facing.North, false);
		}

		long t0 = Stopwatch.GetTimestamp();
		if (_presa && atrasoUs >= 0)
		{
			Volatile.Write(ref _alvo, t0 + atrasoUs * Stopwatch.Frequency / 1_000_000);
			_vai.Set();
		}
		for (int i = 0; i < Corpos; i++)
		{
			int antes = _escuta.Total;
			corpos[i].Vestir(_cat, fichas[i].Ap, fichas[i].Raca, fichas[i].Genero);
			r.ErrosPorCorpo[i] = _escuta.Total - antes;
			r.FimUs[i] = (Stopwatch.GetTimestamp() - t0) * 1_000_000 / Stopwatch.Frequency;
		}

		// A FILA DO FINALIZADOR ESVAZIA ANTES DA FOTO, em todos os modos.
		if (_presa)
		{
			if (atrasoUs >= 0)
				while (!Trava.Solta.IsSet) Thread.SpinWait(64);
			Soltar();
		}

		int antesDoDesenho = _escuta.Total;
		await Quadros(3);
		r.ErrosNoDesenho = _escuta.Total - antesDoDesenho;

		if (fichaTardia)
		{
			for (int i = 0; i < Corpos; i++)
			{
				int antes = _escuta.Total;
				corpos[i].SetMotion(Facing.South, false);
				r.ViradaPorCorpo[i] = _escuta.Total - antes;
			}
			r.ErrosNaVirada = r.ViradaPorCorpo.Sum();

			antesDoDesenho = _escuta.Total;
			await Quadros(3);
			r.ErrosNoDesenho += _escuta.Total - antesDoDesenho;
		}

		if (r.Erros > 0)
		{
			_titulo.Text = titulo + $"  --  {r.Erros} ERRO(S) logado(s)";
			await Quadros(2);
		}
		r.Foto = Foto();

		foreach (CharacterVisual c in corpos) c.Free();
		GetTree().Paused = false;
		ColetarTudo();
		return r;
	}

	/// <summary>Em quantos dos corpos a celula da rodada difere da do controle (e o pior deles, em pixels).</summary>
	private (int Corpos, int PiorPixels) ContraOControle(Rodada r)
	{
		if (_controle == null || r.Foto == null) return (-1, 0);
		int corpos = 0, pior = 0;
		for (int i = 0; i < Corpos; i++)
		{
			int d = Diferentes(_controle, r.Foto, CelulaDe(i));
			if (d > 0) corpos++;
			pior = Math.Max(pior, d);
		}
		return (corpos, pior);
	}

	private static string Lista(int[] v) =>
		string.Join(" ", v.Select((n, i) => n > 0 ? $"corpo{i}={n}" : "").Where(s => s.Length > 0));

	// =====================================================================
	// AS CENAS
	// =====================================================================
	public override void _Ready()
	{
		// A BANCADA PAUSA A ARVORE pra fotografar, e tem que continuar andando com ela pausada.
		ProcessMode = ProcessModeEnum.Always;
		OS.AddLogger(_escuta);
		// ANTES DE QUALQUER CARGA: uma folha que a porta ja prendeu nao se solta mais neste processo.
		if (ComDefeito)
		{
			FolhasPresas.SemSegurarDeTeste = true;
			Nota("DEFEITO INJETADO por --involucrodefeito: a porta das folhas carrega e NAO segura (o jogo de antes)");
		}
		_ = RodarERelatar();
	}

	private async Task RodarERelatar()
	{
		try { await Rodar(); }
		catch (Exception e)
		{
			Nota("  FALHA a bancada estourou: " + e);
			_falha++;
		}
		finally
		{
			// NUNCA SAIR COM A THREAD DE FINALIZACAO PRESA.
			Trava.Solta.Set();
			FolhasPresas.SemSegurarDeTeste = false;
			_fimDoAjudante = true;
			_vai.Set();
			GetTree().Paused = false;

			Nota("");
			Nota("===== BANCADA DO INVOLUCRO =====");
			foreach (string f in _fotos) Nota("   foto  " + f);
			if (_escuta.Outros > 0) Nota($"   --    {_escuta.Outros} erro(s) de OUTRA natureza no caminho; o primeiro: {_escuta.PrimeiroOutro}");
			Nota($"   --    '{Assinatura}' logado {_escuta.Total} vez(es) neste processo; {FolhasPresas.Quantas} folha(s) presa(s) pela porta no fim");
			Nota(_falha == 0 ? $"===== {_ok} OK, NENHUMA FALHA =====" : $"===== {_ok} OK, {_falha} FALHA(S) =====");
			OS.RemoveLogger(_escuta);
			GetTree().Quit(_falha == 0 ? 0 : 1);
		}
	}

	private async Task Rodar()
	{
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados)) { Checa("achei o visual.json", false); return; }
		_cat = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

		Nota($".NET {System.Environment.Version} | {Engine.GetVersionInfo()["string"]} | desenho: {DisplayServer.GetName()}");
		bool comJanela = DisplayServer.GetName() != "headless";
		if (!comJanela) Nota("SEM JANELA: os erros sao contados, e as conferencias de PIXEL dizem que nao mediram");

		_fichas = MontarFichas();
		MontarPalco();
		LigarOAjudante();
		await Quadros(2);

		// ---------------------------------------------------------------- CENA 0
		Nota("");
		Nota("===== CENA 0: a regua =====");
		GD.PushWarning(AvisoDaRegua);
		Checa("o ouvido escuta o que o motor loga", _escuta.AvisosDaRegua == 1, $"{_escuta.AvisosDaRegua} aviso(s) de teste ouvidos");

		// ---------------------------------------------------------------- CENA 1
		Nota("");
		Nota("===== CENA 1: o controle -- os corpos vestidos num processo que nunca montou a tela de criacao =====");
		Rodada controle = await UmaRodada("CONTROLE: processo limpo", _fichas, comTela: false);
		_controle = controle.Foto;
		_vestirDoControleUs = controle.TotalUs;
		Nota($"   --    vestir os {Corpos} corpos levou {controle.TotalUs / 1000.0:0.0} ms");
		Checa("vestir os corpos num processo limpo nao loga o erro", controle.Erros == 0, $"{controle.Erros} erro(s)");
		if (_controle != null)
		{
			Gravar(_controle, "1-controle");
			int menor = Enumerable.Range(0, Corpos).Min(i => DeCorpo(_controle, CelulaDe(i)));
			Checa("cada celula da foto tem um corpo desenhado", menor > 300, $"o menor corpo tem {menor} pixels");

			// A REGUA SABE DIZER NAO: o corpo 0 sem o cabelo tem que sair diferente do controle.
			var carecas = ((Appearance Ap, string Raca, string Genero)[])_fichas.Clone();
			Appearance semCabelo = carecas[0].Ap.Copiar();
			semCabelo.Cabelo = "Bald";
			carecas[0] = (semCabelo, carecas[0].Raca, carecas[0].Genero);
			Rodada careca = await UmaRodada("REGUA: o corpo 0 SEM a camada do cabelo", carecas, comTela: false);
			// A VARREDURA DA CENA 3 USA ESTE TEMPO, e nao o do controle: o primeiro vestir do processo paga a
			// compilacao do C# e a carga do shader, e um atraso sorteado nesse tempo a mais cairia depois do ultimo corpo.
			Nota($"   --    o segundo vestir do processo levou {careca.TotalUs / 1000.0:0.0} ms");
			_vestirDoControleUs = Math.Min(_vestirDoControleUs, careca.TotalUs);
			if (careca.Foto != null)
			{
				Gravar(careca.Foto, "0-regua-corpo-0-sem-cabelo");
				int d0 = Diferentes(_controle, careca.Foto, CelulaDe(0));
				int resto = Enumerable.Range(1, Corpos - 1).Sum(i => Diferentes(_controle, careca.Foto, CelulaDe(i)));
				Checa("(a regua sabe dizer NAO) um corpo sem a camada do cabelo sai DIFERENTE do controle", d0 > 20, $"{d0} pixels diferentes no corpo 0");
				Checa("e os outros sete, vestidos iguais, saem IGUAIS (a foto repete de uma rodada pra outra)", resto == 0, $"{resto} pixels diferentes");
			}
		}

		// ---------------------------------------------------------------- CENA 2
		Nota("");
		Nota("===== CENA 2: a ficha chega DEPOIS do corpo (finalizador preso durante o vestir; depois os corpos viram pro sul) =====");
		Rodada tardia = await UmaRodada("CENA 2: ficha tardia, corpos viram pro sul", _fichas, comTela: true, prender: true, fichaTardia: true);
		Nota($"   --    a tela de criacao tinha {tardia.Miniaturas} miniaturas; a thread de finalizacao ficou presa: {tardia.Prendeu}");
		ChecaDoConserto("depois de a tela de criacao sair e de UMA coleta, nenhum quadro de miniatura fica no cache sem dono", tardia.Zumbis == 0,
			  $"{tardia.Zumbis} de {tardia.Miniaturas} quadros estao no cache com a folha deles ja fora dele ({tardia.FolhasDosZumbis.Count} folhas)");
		ChecaDoConserto("vestir a ficha num corpo que ja existia e vira-lo pro sul nao loga o erro", tardia.Erros == 0,
			  $"{tardia.ErrosNaTela} soltando a tela, {tardia.ErrosNoVestir} no vestir, {tardia.ErrosNaVirada} na virada ({Lista(tardia.ViradaPorCorpo)}), {tardia.ErrosNoDesenho} no desenho");
		if (tardia.Foto != null && _controle != null)
		{
			(int dif, int pior) = ContraOControle(tardia);
			Gravar(tardia.Foto, "2-ficha-tardia");
			Checa("os corpos da cena 2 saem IGUAIS aos do controle, pixel a pixel (nenhuma camada a menos)", dif == 0, $"{dif} de {Corpos} corpos diferentes; o pior, {pior} pixels");
		}

		// ---------------------------------------------------------------- CENA 3
		Nota("");
		int corridas = 16;
		string[] args = OS.GetCmdlineArgs();
		int ia = Array.IndexOf(args, "--involucrocorridas");
		if (ia >= 0 && ia + 1 < args.Length && int.TryParse(args[ia + 1], out int pedido) && pedido > 0) corridas = pedido;
		Nota($"===== CENA 3: a corrida -- o finalizador solto no MEIO do vestir ({corridas} corridas, atraso varrendo os {_vestirDoControleUs / 1000.0:0.0} ms do vestir) =====");
		int comErro = 0, errosAoTodo = 0, corposDiferentes = 0, fotosComparadas = 0, semZumbi = 0, semTrava = 0, gravadas = 0;
		for (int j = 0; j < corridas; j++)
		{
			int atraso = (int)(_vestirDoControleUs * (j + 0.5) / corridas);
			Rodada c = await UmaRodada($"CENA 3, corrida {j:00}: finalizador solto aos {atraso} us", _fichas, comTela: true, prender: true, atrasoUs: atraso);
			if (c.Zumbis == 0) semZumbi++;
			if (!c.Prendeu) semTrava++;
			int noCorpo = Array.FindIndex(c.FimUs, f => f >= atraso);
			(int dif, int pior) = ContraOControle(c);
			if (dif >= 0) { fotosComparadas++; corposDiferentes += dif; }
			if (c.Erros > 0) { comErro++; errosAoTodo += c.Erros; }
			Nota($"   --    corrida {j:00}: solto aos {atraso,6} us ({(noCorpo < 0 ? "depois do ultimo corpo" : $"durante o corpo {noCorpo}")}), vestir {c.TotalUs / 1000.0:0.0} ms, {c.Zumbis} zumbis | "
				 + (c.Erros == 0 ? "nenhum erro" : $"ERROS: no vestir [{Lista(c.ErrosPorCorpo)}], no desenho {c.ErrosNoDesenho}")
				 + (dif < 0 ? "" : $" | foto: {Corpos - dif} de {Corpos} corpos iguais ao controle{(dif > 0 ? $" (pior {pior} px)" : "")}"));
			if (c.Foto != null && _controle != null && (c.ErrosNoVestir > 0 || dif > 0) && gravadas < 4)
			{
				gravadas++;
				Gravar(LadoALado(_controle, c.Foto, c.ErrosPorCorpo), $"3-corrida-{j:00}-controle-em-cima-rodada-embaixo");
			}
		}
		ChecaDoConserto("vestir os corpos logo depois de a tela de criacao sair nao loga o erro, chegue o finalizador quando chegar", comErro == 0,
			  $"{comErro} de {corridas} corridas logaram, {errosAoTodo} erro(s) ao todo; {semZumbi} corrida(s) sem zumbi, {semTrava} sem a trava presa");
		if (fotosComparadas > 0)
			Checa("em TODAS as corridas os corpos saem IGUAIS aos do controle, pixel a pixel (nenhuma camada a menos)", corposDiferentes == 0,
				  $"{corposDiferentes} corpo(s) diferente(s) em {fotosComparadas} fotos de {Corpos} corpos");
		else Nota("   --    sem janela: a conferencia de pixel das corridas NAO foi feita");

		// ---------------------------------------------------------------- CENA 4
		Nota("");
		Nota("===== CENA 4: a contraprova -- a mesma sequencia com o finalizador LIVRE =====");
		Rodada livre = await UmaRodada("CENA 4: finalizador livre", _fichas, comTela: true);
		Checa("com a thread de finalizacao livre nao sobra zumbi", livre.Zumbis == 0, $"{livre.Zumbis} de {livre.Miniaturas}");
		Checa("e o mesmo vestir nao loga erro nenhum (o que a trava acrescenta e so a HORA)", livre.Erros == 0, $"{livre.Erros} erro(s)");
		if (livre.Foto != null && _controle != null)
		{
			(int dif, int pior) = ContraOControle(livre);
			Checa("e os corpos saem iguais aos do controle", dif == 0, $"{dif} de {Corpos} diferentes; o pior, {pior} pixels");
		}

		// ---------------------------------------------------------------- CENA 5
		// POR ULTIMO, E TEM QUE SER: o que o aquecimento prende fica preso pelo processo inteiro.
		Nota("");
		Nota("===== CENA 5: o login de GENTE -- o aquecimento do lobby COMPLETO antes da tela de criacao =====");
		// O `Aquecimento` DE PRODUCAO, pendurado como o `Boot` o pendura no lobby. Quem gasta um segundo ali pega as
		// duas filas dele, e a segunda PRENDE toda folha de aparencia do catalogo (`_presos`, estatica): a folha nunca
		// morre, entao o quadro dela nunca fica sozinho com o involucro. As bancadas entram em milissegundos e ficam
		// com a fila pela metade ("fecho antecipado ... 0 folhas de aparencia") -- foi nelas que o erro apareceu.
		AddChild(new Aquecimento { Name = "Aquecimento" });
		var espera = Stopwatch.StartNew();
		while (!Aquecimento.Terminou && espera.ElapsedMilliseconds < 60000) await Quadros(1);
		Nota($"   --    aquecimento: {Aquecimento.Prontos} de {Aquecimento.Total} recursos presos em {espera.ElapsedMilliseconds} ms (terminou: {Aquecimento.Terminou})");

		Rodada gente = await UmaRodada("CENA 5: aquecimento completo, ficha tardia", _fichas, comTela: true, prender: true, fichaTardia: true);
		int errosDeGente = gente.Erros, corposDeGente = 0, fotosDeGente = 0;
		(int difG, _) = ContraOControle(gente);
		if (difG >= 0) { fotosDeGente++; corposDeGente += difG; }
		const int CorridasDeGente = 6;
		for (int j = 0; j < CorridasDeGente; j++)
		{
			int atraso = (int)(_vestirDoControleUs * (j + 0.5) / CorridasDeGente);
			Rodada c = await UmaRodada($"CENA 5, corrida {j:00}: finalizador solto aos {atraso} us", _fichas, comTela: true, prender: true, atrasoUs: atraso);
			errosDeGente += c.Erros;
			(int dif, _) = ContraOControle(c);
			if (dif >= 0) { fotosDeGente++; corposDeGente += dif; }
		}
		// O AQUECIMENTO SO PRENDE O CATALOGO DE APARENCIA: as folhas de fora dele (os retratos de raca, os planetas)
		// so ficam presas pela porta. E a unica conferencia desta cena que o defeito injetado derruba.
		ChecaDoConserto("com o aquecimento completo nao sobra quadro orfao NENHUM, nem das folhas de fora do catalogo", gente.Zumbis == 0,
			  $"{gente.Zumbis} de {gente.Miniaturas} miniaturas"
			  + (gente.Zumbis > 0 ? $" -- {string.Join(", ", gente.FolhasDosZumbis.Take(8).Select(f => f[(f.LastIndexOf('/') + 1)..]))}" : ""));
		Checa("com as folhas de aparencia presas pelo aquecimento, a ficha tardia e as corridas nao logam o erro", errosDeGente == 0,
			  $"{errosDeGente} erro(s) em 1 + {CorridasDeGente} rodadas");
		if (fotosDeGente > 0)
			Checa("e os corpos saem iguais aos do controle", corposDeGente == 0, $"{corposDeGente} corpo(s) diferente(s) em {fotosDeGente} fotos");

		// ---------------------------------------------------------------- CENA 6
		Nota("");
		Nota("===== CENA 6: a porta e a UNICA -- nenhum fonte de producao carrega uma folha por fora dela =====");
		ConferirAPortaUnica();
	}

	/// <summary>
	/// LE OS FONTES DO CLIENTE atras de uma carga de `SpriteFrames` que nao passe pela `FolhasPresas`.
	///
	/// Nao e zelo: a porta so fecha o defeito se for a UNICA. Uma folha carregada por fora dela pode morrer, deixar
	/// um quadro orfao no cache e ser ressuscitada DEPOIS pela propria porta, na primeira vez em que for pedida la
	/// -- e as cenas acima nao veriam, porque so vestem o que a tela de criacao mostra. Esta conta e o que impede a
	/// vigesima oitava chamada de nascer solta.
	///
	/// ELA SO CONHECE A FORMA QUE AS 27 CHAMADAS TINHAM (a carga generica pelo tipo, do `ResourceLoader` ou do
	/// `GD`). As bancadas (`Robo*.cs`) ficam de fora: elas carregam folha pra MEDIR, e a regra e do jogo.
	/// </summary>
	private void ConferirAPortaUnica()
	{
		const string pasta = "res://Client";
		var soltas = new List<string>();
		int lidos = 0;
		foreach (string nome in DirAccess.GetFilesAt(pasta))
		{
			if (!nome.EndsWith(".cs", StringComparison.Ordinal)) continue;
			if (nome.StartsWith("Robo", StringComparison.Ordinal) || nome == "FolhasPresas.cs") continue;
			lidos++;
			string[] linhas = Godot.FileAccess.GetFileAsString($"{pasta}/{nome}").Split('\n');
			for (int i = 0; i < linhas.Length; i++)
			{
				string l = linhas[i].TrimStart();
				if (l.StartsWith("//", StringComparison.Ordinal)) continue;   // um comentario pode citar a chamada
				if (l.Contains("Load<SpriteFrames>(", StringComparison.Ordinal)) soltas.Add($"{nome}:{i + 1}");
			}
		}

		if (lidos == 0)
		{
			Nota("   --    os fontes do cliente nao estao aqui (jogo exportado): esta conferencia NAO foi feita");
			return;
		}
		Checa("nenhum fonte de producao do cliente carrega `SpriteFrames` fora da `FolhasPresas`", soltas.Count == 0,
			  $"{lidos} arquivos lidos" + (soltas.Count > 0 ? $"; chamadas soltas: {string.Join(", ", soltas.Take(12))}" : ""));
	}
}
