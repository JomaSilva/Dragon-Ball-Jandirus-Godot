using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DA ENTRADA NO MUNDO (`--diagcarga`) ============================
/// O PEDIDO DO DONO, literal: *"quando terminar de criar o personagem aparecer uma tela de loading
/// ate o personagem spawnar etc, pq fica uns 1 a 2 segundos na tela azul do byond ate carregar pela
/// primeira vez"*.
///
/// ============================ POR QUE ISTO NAO PODE SER MEDIDO POR DENTRO ============================
/// Porque a pergunta e sobre PIXEL, e as duas metades do erro sao invisiveis em qualquer numero que
/// nao venha da tela:
///
///   * **SAIR CEDO DEMAIS** -- a cobertura cai antes de o mundo ter sido desenhado, e o jogador ve
///     o fundo chapado de novo, so que por um quadro. Um `Visible = false` no lugar errado nao
///     falha, nao avisa e nao aparece em log nenhum.
///   * **SAIR TARDE DEMAIS** -- a cobertura fica no ar com o jogo pronto atras. Tambem nao falha,
///     tambem nao avisa, e do lado de dentro tudo parece perfeito.
///
/// Este projeto tem registro de quatro defeitos visuais que atravessaram quatro mil checagens
/// verdes porque a bancada media INTENCAO. Entao aqui nao se pergunta "o `Levantar` foi chamado?":
/// fotografa-se **todo quadro** entre o clique e o corpo na tela, e cada quadro e classificado em
/// tres baldes pela cor que ele realmente tem.
/// ======================================================================================================
///
/// ============================ OS TRES BALDES ============================
///   CHAPADO   -- a maioria dos pixels e `Tema.Fundo` (#12141c). **E O DEFEITO DO DONO**: e o
///                `ColorRect` do lobby aparecendo sozinho depois que a criacao se escondeu.
///   COBERTURA -- a maioria dos pixels e o fundo da `TelaDeCarregamento` (0,04 0,05 0,08). E o
///                conserto funcionando.
///   MUNDO     -- nem um nem outro: ha cenario desenhado.
///
/// As duas cores sao PROXIMAS de proposito no jogo (as duas sao azul-marinho escuro), entao a
/// bancada nao as separa no olho: ela compara canal a canal com tolerancia apertada, e a
/// familia 0 prova que o separador funciona antes de qualquer veredito depender dele.
/// =========================================================================================
///
/// ============================ ELA ANDA PELOS BOTOES DE VERDADE ============================
/// Nada aqui e reconstruido e nada aqui e um atalho. A bancada nasce DEPOIS do `MontarLogin` (como
/// a `--diagopcoes`, e pelo mesmo motivo): o lobby na tela e o do jogador. Ela digita no `LineEdit`
/// da conta, aperta "Entrar", aperta "Criar personagem" no slot vazio, atravessa as oito paginas do
/// assistente apertando "Avançar" e aperta **"Entrar no mundo"** -- e e nesse `Pressed`, e nao
/// antes, que o cronometro zera.
///
/// Depois ela aperta ESC, aperta "Desconectar" no menu de pausa, entra de novo e aperta "Jogar" no
/// personagem que acabou de criar -- porque o login de personagem existente mede o MESMO tempo e
/// sofria a MESMA espera (a diferenca era so o que ficava congelado: la a lista de personagens,
/// aqui o fundo chapado).
/// ==========================================================================================
///
/// ============================ ELA PRECISA DE UM SERVIDOR DEDICADO ============================
/// De proposito: hospedar poria o servidor DENTRO deste processo, e ai o "Desconectar" do menu de
/// pausa derrubaria a partida junto (`SalvarEParar`) -- nao haveria pra onde relogar, e a metade do
/// login nao seria medida. Alem disso o servidor dedicado tira as centenas de NPC do boot de dentro
/// do processo que esta sendo CRONOMETRADO.
/// ==============================================================================================
///
/// COMO RODAR (a receita inteira esta em `testar-a-tela-de-carregamento.bat`):
///     Godot --headless --path . --server --port 7801     (noutro processo)
///     Godot --path . --diagcarga --rede 7801 --position 1920,0 --resolution 1280x720
///
/// A PORTA PROPRIA (`--rede`) NAO E CAPRICHO: com a constante nos dois lados, esta bancada e
/// qualquer outro servidor da mesma maquina se misturam em SILENCIO -- o cliente daqui entra no
/// mundo do outro e mede um jogo que nao e o que ele acabou de compilar.
///
/// E as rodadas de INJECAO, que sao o que impede esta bancada de ser decoracao:
///     ... --diagcarga --semcobertura        -> a conta do fundo chapado TEM que ficar vermelha
///     ... --diagcarga --semaquecimento      -> o ANTES do cronometro, no mesmo binario
///     ... --diagcarga --quedanomeio --sinal &lt;arquivo&gt;
///                                           -> entra com o servidor MORTO e prova que a cobertura
///                                              nao vira prisao (ver `AndarNaQueda`)
///     ... --marca &lt;txt&gt;                     -> sufixo nas fotos, pra que a rodada injetada nao
///                                              sobrescreva as provas da rodada boa
///
/// Sem janela o `GetImage` volta vazio: a familia 0 e a 1 continuam saindo, e as de PIXEL dizem
/// que nao mediram em vez de passar de graca.
/// </summary>
public partial class RoboDeCarga : Node
{
	private const string Conta = "diagcarga";
	private const string Senha = "diagcarga";
	private const string Nome = "Cronometrado";

	private static GameClient? C => GameClient.Instance;

	private int _ok, _falha;
	private readonly List<string> _reprovadas = [];
	private readonly List<string> _fotos = [];

	private static void Nota(string linha) => GD.Print("[carga] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	// =====================================================================
	// AS CORES QUE A BANCADA SEPARA
	// =====================================================================
	/// <summary>O fundo do lobby -- ver `Boot.MontarLogin`. E ele que o dono chamou de "tela azul do byond".</summary>
	private static readonly Color Chapado = Tema.Fundo;

	/// <summary>O fundo da tela de carregamento -- ver `TelaDeCarregamento.Montar`.</summary>
	private static readonly Color Cobertura = new(0.04f, 0.05f, 0.08f);

	/// <summary>
	/// TOLERANCIA POR CANAL. Apertada porque as duas cores que precisam ser separadas distam ~0,03
	/// em cada canal: uma folga generosa aqui faria a bancada dizer "cobertura" olhando pro defeito.
	/// </summary>
	private const float Folga = 0.012f;

	/// <summary>
	/// ============================ POR QUE HA UM BALDE "OUTRO" ============================
	/// A primeira versao classificava por exclusao -- "nem chapado nem cobertura, logo MUNDO" -- e a
	/// bancada fechou a corrida em 20 ms com um veredito verde. O quadro que ela chamou de mundo era
	/// a **tela de criacao**: `GetImage` no `_Process` devolve o quadro ANTERIOR, e o anterior ao
	/// clique ainda tinha o assistente na tela.
	///
	/// Vale registrar porque e o erro que esta tarefa existe pra nao repetir: um cronometro que
	/// para cedo demais nao da erro, da um numero bonito. "Mundo" agora exige um FATO fora da
	/// imagem -- o corpo montado na arvore -- alem da cor.
	/// =====================================================================================
	/// </summary>
	private enum Balde { Chapado, Cobertura, Mundo, Outro, SemFoto }

	/// <summary>
	/// A QUE BALDE O ULTIMO QUADRO DESENHADO PERTENCE.
	///
	/// ATENCAO AO DECALAGEM DE UM QUADRO: `GetTexture().GetImage()` chamado no `_Process` devolve o
	/// que foi desenhado no quadro ANTERIOR, e nao no que esta comecando. Isso e exatamente o que
	/// interessa (o que o jogador VIU), mas confundir os dois faz a contagem sair deslocada.
	///
	/// A AMOSTRA E UMA GRADE e nao a imagem inteira: 41 x 23 pontos respondem a mesma pergunta que
	/// 920 mil e nao poem um laco de milhao dentro do tique -- e o custo importa aqui, porque este
	/// laco roda dentro do intervalo que a propria bancada esta cronometrando.
	/// </summary>
	private Balde Classificar(Image? img)
	{
		if (img == null || img.IsEmpty()) return Balde.SemFoto;

		int w = img.GetWidth(), h = img.GetHeight();
		if (w < 8 || h < 8) return Balde.SemFoto;

		int chapado = 0, cobertura = 0, total = 0;
		for (int y = 2; y < h - 2; y += Math.Max(1, h / 23))
			for (int x = 2; x < w - 2; x += Math.Max(1, w / 41))
			{
				Color c = img.GetPixel(x, y);
				total++;
				if (Perto(c, Chapado)) chapado++;
				else if (Perto(c, Cobertura)) cobertura++;
			}

		if (total == 0) return Balde.SemFoto;
		// 70% e nao 90%: a janela tem barra de titulo e moldura do Windows na foto, e a tela de
		// carregamento tem o nome do personagem escrito no meio dela. Nenhuma das duas telas
		// chapadas chega perto de 70% da outra cor -- ver a familia 0, que mede a distancia.
		if (chapado * 100 / total >= 70) return Balde.Chapado;
		if (cobertura * 100 / total >= 70) return Balde.Cobertura;

		// O CORPO NA ARVORE E A SEGUNDA METADE DA DEFINICAO DE "MUNDO" -- ver o cabecalho do enum.
		return World.Instancia?.PosicaoLocal != null ? Balde.Mundo : Balde.Outro;
	}

	private static bool Perto(Color a, Color b) =>
		Math.Abs(a.R - b.R) <= Folga && Math.Abs(a.G - b.G) <= Folga && Math.Abs(a.B - b.B) <= Folga;

	// =====================================================================
	// O QUE A CORRIDA REGISTRA
	// =====================================================================
	/// <summary>
	/// UM QUADRO QUE O JOGADOR VIU -- o numero dele, quando foi, quanto durou, de que cor era e a
	/// imagem inteira.
	///
	/// A imagem fica na MEMORIA e o PNG so e escrito depois que a corrida acaba (ver
	/// <see cref="Revelar"/>): codificar ~3,7 MB de PNG dentro do laco engordaria o proprio
	/// intervalo que esta sendo cronometrado, e a bancada passaria a medir a bancada.
	/// </summary>
	private sealed class Amostra
	{
		public ulong Quadro;        // Engine.GetFramesDrawn() na leitura -- ver o cabecalho de Fotograma
		public double MsDoClique;
		public double MsDeQuadro;   // quanto durou o quadro que esta amostra mostra
		public Balde Balde;
		public Image? Foto;
	}

	/// <summary>
	/// ============================ QUANTAS FOTOS INTEIRAS CABEM NA MEMORIA ============================
	/// A 1280x720 RGBA cada quadro pesa ~3,7 MB, entao guardar todos nao e uma opcao: uma rodada em
	/// que o servidor demorou a responder deu **186 quadros**, que seriam 686 MB.
	///
	/// SAO GUARDADOS OS 20 PRIMEIROS E OS 20 ULTIMOS, e o "ultimos" nao e enfeite -- foi assim que a
	/// primeira versao se denunciou. Ela guardava so os 32 primeiros, e naquela rodada de 186 quadros
	/// a prova do corpo saiu com "faltou uma das tres fotos": o quadro que interessava era o 186o,
	/// justamente o unico que nao havia sido guardado.
	///
	/// A CLASSIFICACAO NAO TEM TETO: todos os 186 continuaram sendo lidos e julgados pela cor. O que
	/// o teto limita e quantos vao pro disco, e nao quantos entram no veredito.
	/// ==================================================================================================
	/// </summary>
	private const int FotosNoComeco = 20, FotosNoFim = 20;

	private sealed class Corrida
	{
		public string Nome = "";
		public ulong Comecou;                 // o instante do clique
		public ulong QuadroDoClique;          // Engine.GetFramesDrawn() no instante do clique
		public int Quadros, Chapados, Cobertos, DeMundo, Outros, SemFoto;
		public double MsAteOMundo = -1;       // do clique ao primeiro quadro com cenario
		public double MsAteMontado = -1;      // do clique ate o mundo MONTADO (ver AoEntrar)
		public int QuadrosDeSobra = -1;       // do corpo montado ate a cobertura sair
		public bool BuracoDepoisDaCobertura;  // caiu a cobertura e voltou pro chapado?
		public bool ViuCobertura;
		public readonly List<Amostra> Amostras = [];

		/// <summary>O que a PROPRIA bancada gastou lendo a tela dentro do intervalo -- ver JulgarCorrida.</summary>
		public double MsLendoATela;
	}

	private Corrida? _corrida;
	private readonly List<Corrida> _feitas = [];
	private int _quadrosComCorpo;

	// =====================================================================
	// ROTEIRO
	// =====================================================================
	private int _passo;
	private double _t;
	private bool _acabou;
	private List<Jandirus.Net.SlotInfo> _slots = [];
	private bool _naSelecao;

	/// <summary>
	/// O NO DO LOBBY. Guardado porque esta bancada SAI de dentro dele -- ver <see cref="Emancipar"/>.
	/// </summary>
	private Node? _lobby;

	private Node Lobby => _lobby ?? GetParent();

	/// <summary>
	/// ============================ ASSINAR NO `_EnterTree`, E NAO NO `_Ready` ============================
	/// Porque esta bancada TROCA DE PAI no meio da execucao, e reparentar dispara `_ExitTree` (que
	/// cancela as assinaturas) seguido de `_EnterTree` (mas NAO de outro `_Ready`). Assinando aqui,
	/// a mudanca de pai se paga sozinha; assinando no `_Ready`, a bancada ficaria surda depois dela --
	/// e surda em silencio, que e o pior jeito.
	/// ====================================================================================================
	/// </summary>
	public override void _EnterTree()
	{
		if (C is not { } cli) return;
		cli.SlotsRecebidos += AoReceberSlots;
		cli.Joined += AoEntrar;
	}

	public override void _ExitTree()
	{
		if (C is not { } cli) return;
		cli.SlotsRecebidos -= AoReceberSlots;
		cli.Joined -= AoEntrar;
	}

	public override void _Ready()
	{
		if (C == null) { Nota("sem GameClient -- nada a medir"); return; }
		CallDeferred(nameof(Emancipar));
		_modoQueda = Array.IndexOf(OS.GetCmdlineArgs(), "--quedanomeio") >= 0;
		Familia0();
	}

	/// <summary>
	/// ============================ SAIR DE DENTRO DO `Boot` PRA SOBREVIVER AO RELOG ============================
	/// O `Boot.VoltarAoLogin` derruba TODOS os filhos dele menos quatro telas de maquina -- e a
	/// bancada era um deles. Sintoma exato: a metade da criacao media certo, o robo apertava
	/// "Desconectar", e o log terminava ali. Nao havia erro nenhum; simplesmente nao havia mais robo.
	///
	/// O conserto e do lado da BANCADA e nao do `Boot`: por uma quarta excecao naquela lista, o
	/// codigo de producao passaria a conhecer o nome de um robo de teste. Aqui ela se muda pra raiz
	/// da arvore e guarda o endereco do lobby pra continuar apertando os botoes de la -- o `Boot`
	/// sobrevive ao relog, sao os filhos dele que nao.
	/// ==========================================================================================
	/// </summary>
	private void Emancipar()
	{
		if (GetParent() is not { } pai || GetTree() is not { } arv || pai == arv.Root) return;

		// A RAIZ TEM QUE SER PEGA ANTES DO `RemoveChild`. Fora da arvore, `GetTree()` devolve nulo --
		// a primeira versao chamava `GetTree().Root` DEPOIS de se desligar e estourou ali mesmo, com
		// a bancada inteira morrendo em silencio depois da familia 0.
		Node raiz = arv.Root;
		_lobby = pai;
		pai.RemoveChild(this);
		raiz.AddChild(this);
	}

	private void AoReceberSlots(List<Jandirus.Net.SlotInfo> slots) { _slots = slots; _naSelecao = true; }

	/// <summary>
	/// O SERVIDOR DISSE QUE O PERSONAGEM EXISTE -- e isto NAO e o fim da espera.
	///
	/// A distancia entre este instante e o pixel e justamente o que esta bancada mede: o `Boot`
	/// monta o mundo inteiro dentro deste callback, e o custo aparece no DESENHO do quadro, depois
	/// que ele retorna. Uma sessao anterior deste projeto mediu a aparencia do servidor chegando
	/// ate 6 s antes do pixel -- por isso "sair por relogio" e "sair quando o servidor falou" sao a
	/// mesma armadilha com nomes diferentes.
	/// </summary>
	private void AoEntrar(int id, Jandirus.Core.World.ZoneKey z, Jandirus.Core.World.Vec2 spawn, string nome)
	{
		_naSelecao = false;
		if (_corrida is { } c && c.MsAteMontado < 0) c.MsAteMontado = (Time.GetTicksUsec() - c.Comecou) / 1000.0;
	}

	// =====================================================================
	// FAMILIA 0 -- O SEPARADOR DE COR FUNCIONA?
	// =====================================================================
	/// <summary>
	/// Antes de qualquer veredito depender do balde, prova que o balde SABE SEPARAR. As duas cores
	/// do jogo distam pouco; se a tolerancia estivesse folgada, todo veredito de pixel desta
	/// bancada ficaria verde olhando pro defeito.
	/// </summary>
	private void Familia0()
	{
		Nota("===== FAMILIA 0: o separador de cor =====");
		Checa("as duas cores NAO se confundem no separador", !Perto(Chapado, Cobertura),
			  $"fundo do lobby {Chapado.ToHtml(false)} x cobertura {Cobertura.ToHtml(false)}");
		Checa("cada cor se reconhece", Perto(Chapado, Chapado) && Perto(Cobertura, Cobertura));
		float d = Math.Max(Math.Max(Math.Abs(Chapado.R - Cobertura.R), Math.Abs(Chapado.G - Cobertura.G)),
						   Math.Abs(Chapado.B - Cobertura.B));
		Checa("a distancia entre elas e maior que a folga", d > Folga, $"distancia {d:0.000} > folga {Folga:0.000}");
	}

	// =====================================================================
	// FAMILIA 1 -- O AQUECIMENTO
	// =====================================================================
	private bool _familia1Feita;

	private void Familia1()
	{
		_familia1Feita = true;
		Nota("");
		Nota("===== FAMILIA 1: o aquecimento (o conserto, nao a maquiagem) =====");

		Checa("a fila de aquecimento terminou antes de o jogador entrar", Aquecimento.Terminou);
		Checa($"todos os {Aquecimento.Total} recursos ficaram quentes", Aquecimento.Prontos == Aquecimento.Total,
			  $"{Aquecimento.Prontos} de {Aquecimento.Total} em {Aquecimento.Ms:0} ms");

		// O FATO, e nao a intencao: o tileset esta NO CACHE do Godot agora, antes de existir World.
		const string tileset = "res://Assets/Maps/tileset.tres";
		Checa("o tileset ja esta no cache ANTES de o mundo montar", ResourceLoader.HasCached(tileset));

		// ============================ O CONTROLE NEGATIVO ============================
		// Sem ele, `HasCached` podendo responder "sim" pra tudo deixaria a linha de cima verde num
		// mundo em que o aquecimento nunca rodou. Este arquivo existe, e do mesmo tipo (.gdshader) e
		// esta DE FORA da lista de propositio -- ele so e usado no mergulho mental.
		const string frio = "res://Assets/Shaders/Zanzoken.gdshader";
		Checa("e o cache sabe dizer NAO (controle negativo)", !ResourceLoader.HasCached(frio), frio);

		// QUANTO CUSTA AGORA o que custava ~630 ms na primeira entrada. Nao e o ganho (pra medir o
		// ganho seria preciso um processo frio), e sim a prova de que o `World._Ready` vai encontrar
		// pronto: se este numero fosse alto, o aquecimento nao teria servido pra nada.
		ulong t0 = Time.GetTicksUsec();
		var ts = ResourceLoader.Load<TileSet>(tileset);
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;
		Checa("pegar o tileset agora e de graca (< 5 ms)", ts != null && ms < 5, $"{ms:0.00} ms");
	}

	// =====================================================================
	// O LACO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;

		// A CAMERA ANDA SEMPRE QUE HA CORRIDA -- todo quadro, sem pular nenhum. Pular um quadro
		// aqui seria abrir exatamente o buraco que a bancada existe pra fechar.
		if (_corrida is { } c) Fotograma(c, delta);

		// E A PROVA DO CORPO ANDA LOGO DEPOIS, no mesmo quadro em que a corrida fechou: ela precisa
		// dos tres quadros SEGUIDOS ao ultimo da corrida. Ver `AndarNaProvaDoCorpo`.
		if (_prova is not (Prova.Nao or Prova.Feita)) AndarNaProvaDoCorpo();

		// A QUEDA DO SERVIDOR TEM ROTEIRO PROPRIO -- ver `AndarNaQueda`.
		if (_modoQueda) { AndarNaQueda(delta); return; }

		_t += delta;
		if (_t < 0.45) return;
		_t = 0;

		switch (_passo)
		{
			case 0: Logar(); break;
			case 1:
				if (!_naSelecao) return;
				if (!_familia1Feita) Familia1();
				if (!AbrirCriacao()) return;          // limpou a conta: espera a lista nova
				break;

			case 2: AndarNoAssistente(); return;      // ele mesmo avanca o passo quando termina
			case 3: if (_corrida != null) return; AbrirPausa(); break;

			// ============================ A VOLTA PRO LOBBY E PELO BOTAO, E ISSO IMPORTOU ============================
			// A primeira versao chamava `GameClient.Desconectar()` direto e reconectava. O log ficou
			// assim: a lista de slots CHEGOU e o robo nunca soube dela. O motivo e que `Desconectar`
			// cru nao passa pelo `Boot.VoltarAoLogin` -- a tela de login continuou LIBERADA (o
			// `MontarMundo` a derruba), e o `Boot.AoReceberSlots` estourou em `_painel.Visible` antes
			// de o evento chegar ao proximo assinante, que era este robo.
			//
			// Isso nao e um defeito de producao: nenhum caminho do jogo desconecta sem passar pelo
			// `VoltarAoLogin`. Era o ATALHO da bancada que nao existia no jogo -- e uma bancada que
			// anda por onde o jogador nao anda mede um jogo que nao e o dele.
			// ==========================================================================================
			// ============================ UM ESC SO NEM SEMPRE ABRE O MENU, E ISSO E DO JOGO ============================
			// Ao entrar no mundo o chat fica com o foco, e enquanto ha campo de texto com foco o
			// `PauseMenu` recusa a tecla de proposito (`Foco.AtalhosMudos`). O primeiro ESC e
			// consumido pelo chat, que solta o foco; o SEGUNDO e que abre o menu -- exatamente o que
			// o dedo do jogador faz.
			//
			// A bancada insiste na TECLA em vez de chamar `PauseMenu.Abrir()` por dentro: chamar o
			// metodo pularia justamente o portao que decide se a tecla vale, e ai a bancada estaria
			// andando por um caminho que o jogador nao tem. (Sintoma de quando ela apertava uma vez
			// so: "botao Desconectar nao esta na tela", e a metade do LOGIN nao era medida.)
			// ==============================================================================================================
			case 4:
				if (PauseMenu.Instancia is not { Aberto: true })
				{
					if (++_esc > 8) { Nota("  --     o ESC nunca abriu o menu de pausa -- login nao medido"); Terminar(); return; }
					AbrirPausa();
					return;
				}
				Nota("");
				Nota("===== agora o LOGIN de personagem que JA EXISTE (a mesma doenca) =====");
				Apertar("Desconectar");
				break;

			case 5:
				if (_naSelecao) { _passo = 6; return; }
				if (++_tentativas > 25) { Nota("  --     nao consegui voltar pra selecao -- login nao medido"); Terminar(); return; }
				if (_tentativas % 4 == 1) Logar();     // o lobby voltou vazio: preenche e entra de novo
				return;

			case 6: JogarNoSlot(); break;
			case 7: if (_corrida != null) return; Terminar(); return;
		}
		_passo++;
	}

	private int _tentativas, _esc;

	/// <summary>ESC de verdade: o menu de pausa so mostra "Desconectar" depois de se ajustar ao contexto.</summary>
	private static void AbrirPausa()
	{
		Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
		Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
	}

	/// <summary>
	/// UM QUADRO DA CORRIDA. Classifica o que o jogador acabou de ver e fecha a corrida no primeiro
	/// quadro com MUNDO -- que e, por definicao, o instante que o dono descreveu como "ate o
	/// personagem spawnar".
	/// </summary>
	private void Fotograma(Corrida c, double delta)
	{
		// ============================ UMA AMOSTRA POR QUADRO DESENHADO, E ISSO E O TETO ============================
		// A pergunta certa -- "e se o buraco estiver ENTRE duas fotos?" -- tem aqui uma resposta
		// exata: nao ha nada entre duas fotos. A tela so muda quando um quadro e desenhado, e o
		// monitor segura aquele quadro ate o proximo chegar. Amostrar uma vez por quadro entao nao e
		// "amostrar bastante": e amostrar TUDO que existe pra ser visto. Amostrar mais depressa
		// devolveria a mesma imagem duas vezes.
		//
		// O que isso EXIGE e prova de que nenhum quadro escapou -- senao a frase acima e so uma
		// intencao. Por isso o numero do quadro vem junto (`Engine.GetFramesDrawn()`): duas amostras
		// seguidas tem que distar exatamente 1. Distando 2, houve um quadro que foi pra tela sem
		// ninguem olhar, e ai sim a pergunta do buraco volta a valer. Ver a conta "nenhum quadro
		// DESENHADO escapou da amostragem", em JulgarCorrida.
		//
		// O DESLOCAMENTO DE UM QUADRO CONTINUA VALENDO: lendo no `_Process` da iteracao N, a imagem
		// que volta e a do quadro N-1 -- o ultimo que o jogador viu de fato.
		// ==========================================================================================
		ulong quadro = (ulong)Engine.GetFramesDrawn();

		ulong t0 = Time.GetTicksUsec();
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		c.MsLendoATela += (Time.GetTicksUsec() - t0) / 1000.0;

		Balde b = Classificar(img);
		var amostra = new Amostra
		{
			Quadro = quadro,
			MsDoClique = (Time.GetTicksUsec() - c.Comecou) / 1000.0,
			// O `delta` da iteracao N e a duracao da iteracao N-1 -- justamente o quadro que esta
			// amostra mostra. Por isso ele e passado pra ca em vez de medido aqui dentro.
			MsDeQuadro = delta * 1000.0,
			Balde = b,
			Foto = img,
		};
		c.Amostras.Add(amostra);

		// SOLTA O QUE SAIU DA JANELA DO FIM -- ver `FotosNoComeco`/`FotosNoFim`. A conta e feita
		// depois de somar: o indice que acabou de cair fora da janela dos ultimos e o unico que
		// precisa ser esquecido, e os primeiros ficam intocados.
		int velho = c.Amostras.Count - 1 - FotosNoFim;
		if (velho >= FotosNoComeco) c.Amostras[velho].Foto = null;

		c.Quadros++;
		switch (b)
		{
			case Balde.Chapado: c.Chapados++; if (c.ViuCobertura) c.BuracoDepoisDaCobertura = true; break;
			case Balde.Cobertura: c.Cobertos++; c.ViuCobertura = true; break;
			case Balde.SemFoto: c.SemFoto++; break;
			case Balde.Outro: c.Outros++; break;
			case Balde.Mundo: c.DeMundo++; break;
		}

		// ============================ O PRIMEIRO QUADRO QUE O JOGADOR VE SEM COBERTURA ============================
		// E nele que a prova do corpo vai procurar pixel de corpo (ver `JulgarOCorpo`): se a
		// cobertura saiu na hora certa, este quadro ja tem o corpo desenhado.
		//
		// SEM EXIGIR QUE TENHA HAVIDO COBERTURA, e isso importou. A primeira versao so olhava depois
		// de ter visto a tela no ar, e na rodada `--semcobertura` -- onde nenhum quadro tem cobertura
		// -- ela caia num remendo que pegava o ULTIMO quadro. Resultado: a conta ficava VERDE na
		// rodada do defeito, olhando pro unico quadro que tinha corpo. Assim como esta, "o primeiro
		// quadro sem cobertura" na rodada injetada e o primeiro quadro depois do clique, que e o
		// fundo chapado -- e a conta fica vermelha, que e o certo.
		// ==========================================================================================
		if (_primeiroSemCobertura == null && b != Balde.Cobertura) _primeiroSemCobertura = amostra;

		// QUANTOS QUADROS A COBERTURA SOBROU. O corpo ja existe na arvore; se a cobertura ficasse
		// muitos quadros depois disso, ela estaria TAPANDO o jogo -- a segunda metade do erro.
		bool temCorpo = World.Instancia?.PosicaoLocal != null;
		if (temCorpo)
		{
			_quadrosComCorpo++;
			if (c.QuadrosDeSobra < 0 && TelaDeCarregamento.Instancia is { NoAr: false }) c.QuadrosDeSobra = _quadrosComCorpo;
		}

		if (b != Balde.Mundo) return;

		c.MsAteOMundo = (Time.GetTicksUsec() - c.Comecou) / 1000.0;
		_feitas.Add(c);
		_corrida = null;

		// A PROVA DO CORPO COMECA AGORA, NESTA MESMA ITERACAO. Ela precisa de tres quadros SEGUIDOS
		// (ver `AndarNaProvaDoCorpo`), e adiar por um so ja deixaria o cenario andar entre eles.
		_provaDe = c.Nome;
		_prova = Prova.Esconder;

		JulgarCorrida(c);
	}

	private void ComecarCorrida(string nome)
	{
		_quadrosComCorpo = 0;
		_primeiroSemCobertura = null;
		_semCorpo = _comCorpo = null;
		_prova = Prova.Nao;
		_corrida = new Corrida
		{
			Nome = nome,
			Comecou = Time.GetTicksUsec(),
			// LIDO ANTES DO CLIQUE e no mesmo `_Process` dele: o quadro desenhado no fim desta
			// iteracao e o PRIMEIRO que o jogador ve depois de apertar o botao. Ver a conta da borda
			// de entrada, em JulgarCorrida.
			QuadroDoClique = (ulong)Engine.GetFramesDrawn(),
		};
	}

	private void JulgarCorrida(Corrida c)
	{
		Nota("");
		Nota($"===== {c.Nome.ToUpperInvariant()}: do clique ao corpo na tela =====");
		Nota($"   --    {c.MsAteOMundo:0} ms no total   ({c.Quadros} quadros: "
			 + $"{c.Cobertos} de cobertura, {c.Chapados} chapados, {c.Outros} de outra coisa, {c.SemFoto} sem foto)");
		if (c.MsAteMontado >= 0)
			Nota($"   --    o mundo estava MONTADO em {c.MsAteMontado:0} ms; os outros {c.MsAteOMundo - c.MsAteMontado:0} ms sao o QUADRO que o desenhou");

		// ---------- O TEMPO DE QUADRO, E NAO SO O TOTAL ----------
		// Um total encolhendo pode ser trabalho REMANEJADO em vez de trabalho evitado -- e neste
		// projeto ja houve o contrario, um total que caiu de 1049 pra 52 ms com a travada intacta,
		// porque o custo verdadeiro morava num quadro que nenhuma medicao olhava. Entao a serie
		// inteira sai impressa: o quadro caro tem que aparecer como UM numero grande, e o conserto
		// tem que encolher ESSE numero, nao a soma.
		//
		// DUAS SERIES, E ELAS DISCORDAM DE PROPOSITO:
		//   * o `delta` do Godot e o tempo de CPU do quadro -- ele entrega os comandos e segue em frente;
		//   * o relogio de parede entre duas amostras inclui o `GetImage`, que ESPERA a GPU terminar de
		//     desenhar -- e e a GPU terminando que poe o quadro na tela.
		// O que o jogador espera esta entre os dois: o `delta` e piso e o relogio de parede e teto (o
		// teto ainda carrega o custo da propria bancada). Imprimir so um dos dois seria escolher o
		// numero mais bonito e chamar de medicao.
		var espacos = new List<double>();
		for (int i = 1; i < c.Amostras.Count; i++) espacos.Add(c.Amostras[i].MsDoClique - c.Amostras[i - 1].MsDoClique);
		Nota("   --    tempo de CPU de cada quadro (delta, ms): " + string.Join("  ", c.Amostras.Select(x => $"{x.MsDeQuadro:0}")));
		Nota("   --    relogio de parede entre amostras (ms):    " + string.Join("  ", espacos.Select(x => $"{x:0}")));
		if (espacos.Count > 0)
			Nota($"   --    o quadro mais caro: {c.Amostras.Max(x => x.MsDeQuadro):0} ms de CPU e {espacos.Max():0} ms de parede"
				 + $"   |   a bancada gastou {c.MsLendoATela:0} ms lendo a tela dentro deste intervalo");

		if (c.SemFoto == c.Quadros)
		{
			Nota("  --     SEM JANELA: os vereditos de pixel nao foram medidos (rode com --position 1920,0)");
			return;
		}

		// ---------- A AMOSTRAGEM COBRE O INTERVALO INTEIRO? ----------
		// Esta conta vem ANTES de todas as outras porque todas as outras dependem dela: "nunca vi
		// tela vazia" nao vale nada se um quadro tiver passado sem ser olhado. Ver o cabecalho de
		// Fotograma -- entre dois quadros desenhados nao existe nada pra ver, entao amostra por
		// quadro com salto zero e cobertura TOTAL do intervalo, e nao uma amostragem por cima.
		int saltos = 0;
		for (int i = 1; i < c.Amostras.Count; i++)
			if (c.Amostras[i].Quadro != c.Amostras[i - 1].Quadro + 1) saltos++;
		double menor = espacos.Count > 0 ? espacos.Min() : 0, maior = espacos.Count > 0 ? espacos.Max() : 0;
		Checa($"[{c.Nome}] nenhum quadro DESENHADO escapou da amostragem", saltos == 0 && c.Amostras.Count > 1,
			  $"{c.Amostras.Count} amostras, quadros {(c.Amostras.Count > 0 ? c.Amostras[0].Quadro : 0)}"
			  + $"..{(c.Amostras.Count > 0 ? c.Amostras[^1].Quadro : 0)}, {saltos} salto(s);"
			  + $" espacamento {menor:0} a {maior:0} ms");

		// ---------- A BORDA DE ENTRADA: NO PRIMEIRO QUADRO, E NAO NO SEGUNDO ----------
		// O `Levantar` e sincrono de proposito (ver `TelaDeCarregamento`), e e AQUI que isso se
		// paga: o quadro desenhado no fim da iteracao do clique ja tem que estar coberto. Um
		// `CallDeferred` ou um `await` no caminho custaria exatamente um quadro de fundo chapado --
		// pequeno, invisivel num log e visivel na tela.
		Amostra? primeira = c.Amostras.Count > 0 ? c.Amostras[0] : null;
		Checa($"[{c.Nome}] a cobertura ja estava no PRIMEIRO quadro depois do clique",
			  primeira is { Balde: Balde.Cobertura } && primeira.Quadro == c.QuadroDoClique + 1,
			  primeira == null ? "sem amostra nenhuma"
			  : $"clique no quadro {c.QuadroDoClique}; a primeira amostra e o quadro {primeira.Quadro}"
				+ $" e mostra {primeira.Balde}");

		// ---------- O PEDIDO DO DONO ----------
		Checa($"[{c.Nome}] nenhum quadro de fundo chapado entre o clique e o corpo", c.Chapados == 0,
			  $"{c.Chapados} quadro(s) de #{Chapado.ToHtml(false)}");

		// ---------- A COBERTURA APARECE, E APARECE JA ----------
		Checa($"[{c.Nome}] a tela de carregamento esteve no ar", c.Cobertos > 0, $"{c.Cobertos} quadro(s)");

		// ---------- NAO SAIU CEDO DEMAIS ----------
		// Cair a cobertura e VOLTAR pro chapado e a assinatura exata do "saiu antes do mundo".
		Checa($"[{c.Nome}] a cobertura nao caiu antes do mundo", !c.BuracoDepoisDaCobertura);

		// ---------- NAO SAIU TARDE DEMAIS ----------
		// O `Soltar` espera UM `frame_post_draw`. Do quadro em que o corpo entra na arvore ate a
		// cobertura sair tem que ser 1 quadro; 2 ainda e o arredondamento do laco. Mais que isso e
		// cobertura tapando jogo pronto.
		Checa($"[{c.Nome}] a cobertura saiu no quadro seguinte ao corpo, e nao depois",
			  c.QuadrosDeSobra >= 0 && c.QuadrosDeSobra <= 2,
			  c.QuadrosDeSobra < 0 ? "a cobertura nunca saiu" : $"{c.QuadrosDeSobra} quadro(s) com o corpo ja montado");
	}

	// =====================================================================
	// A QUEDA DO SERVIDOR NO MEIO DA ESPERA (`--quedanomeio`)
	// =====================================================================
	/// <summary>
	/// ============================ O RISCO QUE A PROPRIA COBERTURA CRIOU ============================
	/// A cobertura sai por FATO: ela cai quando um quadro com o mundo foi desenhado. Isso e o certo
	/// -- e cria um caso novo que antes nao existia: se esse mundo NUNCA vier, a espera nao tem fim
	/// proprio. Servidor morto no meio, e o jogador fica trancado em "carregando..." pra sempre, sem
	/// ver o mundo, sem ver a lista e sem ver erro nenhum. Isso e PIOR que os 1 a 2 s de tela vazia
	/// que a cobertura veio consertar, e por isso tem bancada propria.
	///
	/// COMO SE MATA UM SERVIDOR NA HORA CERTA: quem mata e quem rodou a bancada, de fora, e avisa
	/// por um arquivo (`--sinal <caminho>`). A bancada espera esse arquivo parada na selecao e SO
	/// ENTAO aperta "Jogar". Do lado do cliente nada mudou: o `peer` ainda se acha conectado (o
	/// prazo de silencio do LiteNetLib ainda nao estourou), o pedido sai pro vazio e a cobertura
	/// sobe exatamente como sobe num dia normal.
	///
	/// Um relogio na bancada ("espere 12 s e clique") pareceria mais simples e mediria outra coisa:
	/// bastaria o servidor demorar a morrer pra o clique cair com ele vivo e a rodada ficar verde
	/// sem ter testado nada.
	/// ================================================================================================
	/// </summary>
	private bool _modoQueda;
	private int _passoQueda;
	private double _tQueda;
	private ulong _comecouAQueda, _quadroDaQueda;
	private int _esperaJulgar;
	private Image? _fotoA, _fotoB;
	private bool _viCoberturaNaQueda;
	private double _msAteSoltar = -1;

	private static string Sinal
	{
		get
		{
			string[] a = OS.GetCmdlineArgs();
			int i = Array.IndexOf(a, "--sinal");
			return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
		}
	}

	private void AndarNaQueda(double delta)
	{
		_tQueda += delta;

		switch (_passoQueda)
		{
			case 0:
				if (_tQueda < 0.6) return;
				_tQueda = 0;
				Logar();
				_passoQueda = 1;
				return;

			case 1:
				if (!_naSelecao)
				{
					if (_tQueda > 25) { Nota("  --     nao cheguei na selecao -- a queda nao foi medida"); Terminar(); }
					return;
				}
				Nota("");
				Nota("===== A QUEDA DO SERVIDOR NO MEIO DA ESPERA =====");
				if (Sinal.Length == 0) { Nota("  --     falta `--sinal <arquivo>`: a queda nao foi medida"); Terminar(); return; }
				Nota("   na selecao. Esperando o aviso de que o servidor foi morto: " + Sinal);
				_tQueda = 0;
				_passoQueda = 2;
				return;

			case 2:
				if (!System.IO.File.Exists(Sinal))
				{
					if (_tQueda > 90) { Nota("  --     o aviso nunca chegou -- a queda nao foi medida"); Terminar(); }
					return;
				}
				Nota("   o servidor esta morto. Apertando \"Jogar\" assim mesmo -- e o que o jogador faria.");
				_comecouAQueda = Time.GetTicksUsec();
				_quadroDaQueda = (ulong)Engine.GetFramesDrawn();
				if (!Apertar("Jogar")) { Nota("  --     sem botao Jogar -- a queda nao foi medida"); Terminar(); return; }
				_tQueda = 0;
				_passoQueda = 3;
				return;

			case 3:
			{
				bool noAr = TelaDeCarregamento.Instancia is { NoAr: true };
				if (noAr) _viCoberturaNaQueda = true;
				if (_viCoberturaNaQueda && !noAr && _msAteSoltar < 0)
					_msAteSoltar = (Time.GetTicksUsec() - _comecouAQueda) / 1000.0;

				// UM SEGUNDO DEPOIS DE SOLTAR, pra que a foto do fim mostre a tela ja assentada; e
				// 30 s de teto, que e o unico jeito de "presa pra sempre" virar um veredito e nao
				// uma bancada pendurada.
				if ((_msAteSoltar >= 0 && _tQueda > 1.0) || _tQueda > 30)
				{
					// ============================ DEZ QUADROS ANTES DE OLHAR, E POR QUE ============================
					// A primeira versao fotografava no MESMO quadro em que decidia julgar, e acusou o
					// conserto de nao funcionar: a arvore dizia "cobertura escondida" e a foto mostrava a
					// cobertura, com 99,7% dos pixels na cor dela. Era o deslocamento de um quadro do
					// `GetImage` (o mesmo que o `Fotograma` documenta): a foto era do quadro ANTERIOR a
					// esconder a tela. Aqui isso doi mais que no laco, porque aqui ha UMA foto so.
					//
					// Dez quadros resolvem, e as DUAS fotos provam que resolveram: identicas byte a byte
					// com o jogo desenhando querem dizer que quem congelou foi a LEITURA e nao a tela --
					// e nesse caso o veredito de pixel abaixo perde o direito de acusar a producao por um
					// defeito que e da bancada.
					// ================================================================================================
					_fotoA = GetViewport()?.GetTexture()?.GetImage();
					_passoQueda = 4;
					_esperaJulgar = 10;
				}
				return;
			}

			case 4:
				if (--_esperaJulgar > 0) return;
				_fotoB = GetViewport()?.GetTexture()?.GetImage();
				JulgarQueda();
				Terminar();
				return;
		}
	}

	private void JulgarQueda()
	{
		// ============================ O CAMPO E O PIXEL PRECISAM SER LIDOS SEPARADO ============================
		// `NoAr` responde false por DOIS motivos diferentes -- "a tela desceu" e "o node sumiu" --, e
		// os dois sao indistinguiveis de fora. Se eles se juntassem numa conta so, um node liberado
		// com a cobertura ainda desenhada na tela passaria por conserto. Por isso as tres linhas
		// abaixo saem antes de qualquer veredito, e por isso o veredito que vale e o do PIXEL.
		// ========================================================================================
		List<TelaDeCarregamento> telas = [.. Todos<TelaDeCarregamento>(GetTree()?.Root)];
		Nota($"   --    o node da cobertura {(TelaDeCarregamento.Instancia == null ? "SUMIU da arvore" : "continua na arvore")}"
			 + $"; NoAr={(TelaDeCarregamento.Instancia?.NoAr.ToString() ?? "n/a")}"
			 + $"; quadros desenhados desde o clique: {(ulong)Engine.GetFramesDrawn() - _quadroDaQueda}");
		Nota($"   --    ha {telas.Count} TelaDeCarregamento na arvore: "
			 + string.Join(" | ", telas.Select(t => $"{t.GetPath()} NoAr={t.NoAr} eh-a-Instancia={t == TelaDeCarregamento.Instancia}")));

		Checa("[queda] a cobertura subiu no clique, mesmo com o servidor ja morto", _viCoberturaNaQueda);
		Checa("[queda] a cobertura NAO ficou presa: ela saiu sozinha", _msAteSoltar >= 0,
			  _msAteSoltar >= 0 ? $"caiu {_msAteSoltar:0} ms depois do clique"
								: $"ainda no ar depois de {_tQueda:0} s -- jogador TRANCADO");

		// ============================ A CAMERA PRECISA PROVAR QUE ESTA VIVA ============================
		// Duas fotos separadas por dez quadros. Iguais byte a byte com o jogo desenhando quer dizer que
		// a LEITURA congelou -- e ai o veredito de pixel abaixo nao mede a tela do jogador, mede um
		// retrato velho. Sem esta conta, a bancada acusaria a producao por um defeito que e dela.
		// ================================================================================================
		bool camaraViva = _fotoA != null && _fotoB != null
					   && !_fotoA.GetData().AsSpan().SequenceEqual(_fotoB.GetData());
		Nota($"   --    a camera da bancada {(camaraViva ? "esta VIVA" : "esta CONGELADA")}"
			 + " (duas fotos separadas por dez quadros: "
			 + (camaraViva ? "diferentes" : "identicas byte a byte") + ")");

		Checa("[queda] o lobby voltou -- da pra tentar de novo sem fechar o jogo", Botao("Entrar") != null);

		if (camaraViva)
			Checa("[queda] e o que ficou na tela nao e a cobertura", Classificar(_fotoB) != Balde.Cobertura);
		else
			Nota("  --     o que ficou na tela NAO foi medido: a leitura da tela congelou nesta rodada");

		Guardar(_fotoB, $"queda{Marca}-o-que-sobrou.png");
	}

	// =====================================================================
	// A BORDA DE SAIDA, MEDIDA NO PIXEL DO CORPO
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ISTO NAO PODE SER `PosicaoLocal != null` ============================
	/// Porque isso e CAMPO, e campo ja mentiu neste projeto: uma bancada assinou "o corpo esta
	/// branco" lendo um uniform, e a foto mostrou 0,0% de branco na tela. O corpo existir na arvore
	/// nao e o corpo estar desenhado -- entre uma coisa e outra ha um quadro inteiro, e e nesse
	/// quadro que mora a pergunta do dono.
	///
	/// ============================ COMO SE MEDE PIXEL DE CORPO SEM SABER COMO O CORPO E ============================
	/// Por DIFERENCA, e em tres quadros seguidos:
	///
	///     quadro M-1   o primeiro quadro sem cobertura -- a foto que esta sendo julgada  (A)
	///     quadro M     o mesmo lugar com o corpo ESCONDIDO                               (B)
	///     quadro M+1   o mesmo lugar com o corpo de volta                                (C)
	///
	/// Os pixels que mudam entre B e C SAO o corpo -- e uma mascara medida, nao um retangulo
	/// chutado, e ela nao depende de saber a cor da pele, o tamanho do sprite nem o desenho da
	/// roupa. Dentro dessa mascara a pergunta fica simples: a foto A parece mais com C (corpo
	/// desenhado) ou com B (corpo ausente)?
	///
	/// TRES QUADROS SEGUIDOS e nao "alguns quadros depois" de proposito: entre M-1 e M+1 o cenario
	/// quase nao anda, e o que anda aparece na conta "fora da janela", que sai impressa junto.
	/// Esconder o corpo por um quadro nao e invencao da bancada -- e o que o `World` ja faz no
	/// zanzoken, com o mesmo `Visible`.
	/// ==============================================================================================================
	/// </summary>
	private enum Prova { Nao, Esconder, LerSemCorpo, LerComCorpo, Feita }

	private Prova _prova = Prova.Nao;
	private Amostra? _primeiroSemCobertura;
	private Image? _semCorpo, _comCorpo;
	private Vector2 _ondeOCorpoEsta;
	private string _provaDe = "";

	/// <summary>O corpo do jogador, achado pela ARVORE -- a bancada nao precisa de porta nova no World.</summary>
	private LocalPlayer? Corpo => Todos<LocalPlayer>(GetTree()?.Root).FirstOrDefault();

	private void AndarNaProvaDoCorpo()
	{
		switch (_prova)
		{
			case Prova.Esconder:
				if (Corpo is not { } corpo)
				{
					Nota("  --     sem LocalPlayer na arvore: a prova do corpo NAO foi medida");
					_prova = Prova.Feita;
					return;
				}
				// A POSICAO SO SERVE PRA APONTAR A JANELA DE BUSCA. Quem diz se ha corpo desenhado e
				// a diferenca entre os pixels, e nao esta coordenada -- ela so evita varrer a tela
				// inteira e evita que uma nuvem do outro canto entre na mascara.
				_ondeOCorpoEsta = corpo.GetGlobalTransformWithCanvas().Origin;
				corpo.Visible = false;
				_prova = Prova.LerSemCorpo;
				return;

			case Prova.LerSemCorpo:
				_semCorpo = GetViewport()?.GetTexture()?.GetImage();   // o quadro que acabou de ser desenhado: sem corpo
				if (Corpo is { } devolta) devolta.Visible = true;
				_prova = Prova.LerComCorpo;
				return;

			case Prova.LerComCorpo:
				_comCorpo = GetViewport()?.GetTexture()?.GetImage();   // e este ja tem o corpo de volta
				_prova = Prova.Feita;
				JulgarOCorpo();
				return;
		}
	}

	/// <summary>Distancia de cor entre dois pixels: soma dos tres canais, 0 a 3.</summary>
	private static double Dist(Color a, Color b) =>
		Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

	/// <summary>
	/// O que conta como "este pixel mudou". 0,09 somando tres canais e ~8 niveis de 255 em cada um --
	/// acima do ruido de compressao do alvo de render e MUITO abaixo da diferenca entre um pedaco de
	/// corpo e o chao atras dele.
	/// </summary>
	private const double LimiarDeMudanca = 0.09;

	private void JulgarOCorpo()
	{
		Nota("");
		Nota($"===== {_provaDe.ToUpperInvariant()}: o corpo no PIXEL (a borda de saida) =====");

		if (_primeiroSemCobertura?.Foto is not { } a || _semCorpo is not { } semC || _comCorpo is not { } comC)
		{
			Nota("  --     faltou uma das tres fotos: a prova do corpo NAO foi medida");
			return;
		}

		int w = a.GetWidth(), h = a.GetHeight();
		if (semC.GetWidth() != w || semC.GetHeight() != h || comC.GetWidth() != w || comC.GetHeight() != h)
		{
			Nota("  --     as tres fotos nao tem o mesmo tamanho: a prova do corpo NAO foi medida");
			return;
		}

		// A JANELA: generosa o bastante pra caber o corpo inteiro e a aura, apertada o bastante pra
		// deixar o resto da tela de fora.
		const int Raio = 140;
		int cx = Math.Clamp((int)_ondeOCorpoEsta.X, 0, w - 1);
		int cy = Math.Clamp((int)_ondeOCorpoEsta.Y, 0, h - 1);
		int x0 = Math.Max(0, cx - Raio), x1 = Math.Min(w, cx + Raio);
		int y0 = Math.Max(0, cy - Raio), y1 = Math.Min(h, cy + Raio);

		int mascara = 0, minX = w, minY = h, maxX = -1, maxY = -1;
		double somaSem = 0, somaCom = 0;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				Color pSem = semC.GetPixel(x, y), pCom = comC.GetPixel(x, y);
				if (Dist(pSem, pCom) <= LimiarDeMudanca) continue;   // aqui nao ha corpo

				mascara++;
				minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
				minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);

				Color pA = a.GetPixel(x, y);
				somaSem += Dist(pA, pSem);
				somaCom += Dist(pA, pCom);
			}

		// O CONTROLE: fora da janela, esconder e mostrar o corpo nao pode mudar quase nada. Se este
		// numero fosse grande, a "mascara" acima seria cenario andando e nao corpo -- e a prova toda
		// estaria medindo vento. Amostrado em grade porque e so um contexto, nao um veredito.
		int foraMudou = 0, foraOlhados = 0;
		for (int y = 2; y < h - 2; y += 4)
			for (int x = 2; x < w - 2; x += 4)
			{
				if (x >= x0 && x < x1 && y >= y0 && y < y1) continue;
				foraOlhados++;
				if (Dist(semC.GetPixel(x, y), comC.GetPixel(x, y)) > LimiarDeMudanca) foraMudou++;
			}

		Nota($"   --    o corpo ocupa {mascara} pixels, na caixa ({minX},{minY})-({maxX},{maxY}),"
			 + $" achada por diferenca e nao por chute");
		Nota($"   --    fora da janela, esconder o corpo mudou {foraMudou} de {foraOlhados} pontos"
			 + " (o cenario estava parado entre os tres quadros)");

		// ---------- O CORPO TEM PIXEL? ----------
		// Sem esta conta, tudo abaixo ficaria verde num mundo onde o corpo nunca e desenhado: duas
		// fotos identicas dao mascara vazia, e mascara vazia da diferenca zero dos dois lados.
		Checa($"[{_provaDe}] esconder e mostrar o corpo MUDA a tela (existe pixel de corpo)", mascara >= 150,
			  $"{mascara} pixels mudaram");
		if (mascara < 150) return;

		double mediaSem = somaSem / mascara, mediaCom = somaCom / mascara;

		// ---------- A BORDA DE SAIDA ----------
		// A cobertura saiu no instante certo se, no PRIMEIRO quadro sem ela, os pixels do corpo ja
		// estao la. Saindo cedo demais (por relogio, por exemplo) este quadro se pareceria com o
		// SEM-corpo -- que e o fundo que o dono passou 1 a 2 s olhando.
		Checa($"[{_provaDe}] no primeiro quadro SEM cobertura o corpo ja esta desenhado",
			  mediaCom < mediaSem * 0.5,
			  $"aquele quadro esta a {mediaCom:0.000} do COM-corpo e a {mediaSem:0.000} do SEM-corpo"
			  + $" (quanto menor, mais parecido)");

		// AS TRES FOTOS DA PROVA vao pro disco com a caixa do corpo no nome, pra quem quiser conferir
		// no olho o que a conta afirmou.
		Guardar(a, $"corpo{Marca}-{_provaDe}-A-primeiro-sem-cobertura.png");
		Guardar(semC, $"corpo{Marca}-{_provaDe}-B-corpo-escondido.png");
		Guardar(comC, $"corpo{Marca}-{_provaDe}-C-corpo-de-volta.png");
	}

	// =====================================================================
	// OS BOTOES DE VERDADE
	// =====================================================================
	private static IEnumerable<T> Todos<T>(Node? raiz) where T : Node
	{
		if (raiz == null) yield break;
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t) yield return t;
			foreach (T neto in Todos<T>(f)) yield return neto;
		}
	}

	private Button? Botao(string texto) =>
		Todos<Button>(Lobby).FirstOrDefault(b => b.Text == texto && b.IsVisibleInTree());

	private bool Apertar(string texto)
	{
		if (Botao(texto) is not { } b) { Nota($"  --     botao \"{texto}\" nao esta na tela"); return false; }
		b.EmitSignal(BaseButton.SignalName.Pressed);
		return true;
	}

	private void Logar()
	{
		// os dois campos do formulario de verdade: conta e senha
		List<LineEdit> campos = [.. Todos<LineEdit>(Lobby).Where(l => l.IsVisibleInTree())];
		foreach (LineEdit l in campos)
			if (l.Secret) l.Text = Senha;
			else if (l.MaxLength == 24) l.Text = Conta;

		// "Entrar", e nao "Hospedar partida": esta bancada disca num servidor DEDICADO (ver o
		// cabecalho). Hospedar poria o servidor dentro deste processo, e ai o "Desconectar" do menu
		// de pausa derrubaria a partida junto -- nao haveria pra onde relogar.
		Apertar("Entrar");
	}

	/// <summary>Falso quer dizer "ainda nao da: a conta estava suja e eu acabei de limpar".</summary>
	private bool AbrirCriacao()
	{
		int vazio = _slots.FindIndex(s => !s.Ocupado);
		if (vazio < 0)
		{
			// TERRENO LIMPO: a conta e reusada entre rodadas, e sem isto a segunda execucao acharia
			// os tres slots cheios e nao teria o que criar -- o teste falharia por sujeira.
			_naSelecao = false;
			for (int i = 0; i < _slots.Count; i++) if (_slots[i].Ocupado) C?.SendDeleteChar(i, _slots[i].Nome);
			return false;
		}
		return Apertar("Criar personagem");
	}

	/// <summary>
	/// ATRAVESSA O ASSISTENTE PELO BOTAO "Avançar", uma pagina por passada.
	///
	/// ADAPTATIVO E NAO ROTEIRIZADO: so duas paginas das oito recusam avancar (a raca e a
	/// identidade), e quais sao elas depende da raca escolhida. Entao a bancada aperta "Avançar",
	/// olha se apareceu texto no rotulo de ERRO e so entao mexe em alguma coisa -- que e o que o
	/// jogador faz. Um roteiro de "clique nesta ordem" quebraria na primeira pagina nova.
	/// </summary>
	private void AndarNoAssistente()
	{
		if (Todos<CreationScreen>(Lobby).FirstOrDefault() is not { Visible: true } tela) return;

		Label? erro = Todos<Label>(tela).FirstOrDefault(l => l.Text.Length > 0
														&& l.GetThemeColor("font_color") == Tema.Perigo);

		if (erro != null && erro.Text.Length > 0)
		{
			if (erro.Text.Contains("raça") || erro.Text.Contains("raca")) EscolherAlgo(tela);
			else PreencherIdentidade(tela);
			return;
		}

		if (Botao("Entrar no mundo") is { } fim)
		{
			// A ULTIMA PAGINA E A DA IDENTIDADE, e ela e a unica que ainda pode recusar. Preencher
			// ANTES de apertar e o que o jogador faz -- a primeira versao apertava primeiro, levava
			// a recusa e zerava o cronometro num clique que nao abriu porta nenhuma.
			PreencherIdentidade(tela);

			// ============================ O CRONOMETRO ZERA AQUI ============================
			// No `Pressed` do botao que o dedo do dono aperta, e nao no pacote que sai depois dele.
			// O que o jogador cronometra e o clique, e nao a rede.
			// ================================================================================
			ComecarCorrida("criacao");
			fim.EmitSignal(BaseButton.SignalName.Pressed);

			// O CLIQUE PEGOU? O `Confirmar` roda inteiro dentro do `EmitSignal`, entao a resposta ja
			// esta aqui: se a tela continua visivel, ele recusou e nao ha corrida nenhuma pra medir.
			if (tela.Visible) { _corrida = null; return; }
			_passo = 3;
			return;
		}

		Apertar("Avançar");
	}

	/// <summary>Aperta o primeiro botao da PAGINA que nao seja da navegacao -- na pagina da raca, isso e uma raca.</summary>
	private static void EscolherAlgo(CreationScreen tela)
	{
		string[] navegacao = ["Avançar", "Voltar", "Cancelar", "Entrar no mundo", "Opções", "Sair do jogo", "cor"];
		foreach (Button b in Todos<Button>(tela))
			if (b.IsVisibleInTree() && !b.Disabled && Array.IndexOf(navegacao, b.Text) < 0 && b.Text.Length > 1)
			{
				b.EmitSignal(BaseButton.SignalName.Pressed);
				return;
			}
	}

	private static void PreencherIdentidade(CreationScreen tela)
	{
		if (Todos<LineEdit>(tela).FirstOrDefault(l => l.IsVisibleInTree()) is { } nome) nome.Text = Nome;
		if (Todos<TextEdit>(tela).FirstOrDefault(t => t.IsVisibleInTree()) is { } historia)
			historia.Text = "nasceu pra ser cronometrado do clique ate o primeiro pixel do mundo, "
						  + "e pra provar que ninguem mais olha fundo chapado nesse caminho.";
	}

	private void JogarNoSlot()
	{
		if (Botao("Jogar") is not { } b) { Nota("  --     sem botao Jogar -- o personagem nao ficou gravado?"); Terminar(); return; }
		ComecarCorrida("login");
		b.EmitSignal(BaseButton.SignalName.Pressed);
	}

	/// <summary>
	/// O SUFIXO DA RODADA. Sem ele a rodada de injecao sobrescreveria as fotos da rodada boa, e a
	/// prova e a contraprova ficariam com o mesmo nome de arquivo -- que e o jeito mais silencioso
	/// de perder as duas.
	/// </summary>
	private static string Marca
	{
		get
		{
			string[] a = OS.GetCmdlineArgs();
			int i = Array.IndexOf(a, "--marca");
			if (i >= 0 && i + 1 < a.Length) return "-" + a[i + 1];
			if (Array.IndexOf(a, "--semcobertura") >= 0) return "-semcobertura";
			if (Array.IndexOf(a, "--semaquecimento") >= 0) return "-semaquecimento";
			return "";
		}
	}

	private void Guardar(Image? img, string nome)
	{
		try
		{
			if (img == null || img.IsEmpty()) return;
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_fotos.Add(caminho);
		}
		catch (Exception e) { Nota("  --     sem foto: " + e.Message); }
	}

	/// <summary>
	/// ESCREVE A TIRA INTEIRA -- todo quadro do intervalo, na ordem, com o balde no nome.
	///
	/// SO DEPOIS QUE TUDO ACABOU, e essa e a razao de as fotos terem ficado na memoria: codificar
	/// PNG dentro do laco somaria dezenas de ms a cada quadro do intervalo que a propria bancada
	/// esta cronometrando. O numero do quadro entra no nome pra que a sequencia no disco seja a
	/// sequencia que o jogador viu.
	/// </summary>
	private void Revelar()
	{
		foreach (Corrida c in _feitas)
			for (int i = 0; i < c.Amostras.Count; i++)
			{
				Amostra a = c.Amostras[i];
				Guardar(a.Foto, $"tira{Marca}-{c.Nome}-q{i:00}-{a.Balde.ToString().ToLowerInvariant()}.png");
			}
	}

	private void Terminar()
	{
		if (_acabou) return;
		_acabou = true;

		Revelar();

		Nota("");
		Nota("===== BANCADA DA ENTRADA NO MUNDO =====");
		foreach (Corrida c in _feitas)
			Nota($"   {c.Nome,-8} {c.MsAteOMundo:0} ms   (rede + montagem {c.MsAteMontado:0} ms + desenho {c.MsAteOMundo - c.MsAteMontado:0} ms)"
				 + $"   quadro mais caro {(c.Amostras.Count > 0 ? c.Amostras.Max(x => x.MsDeQuadro) : 0):0} ms");
		foreach (string f in _fotos) Nota("   foto  " + f);

		// ============================ A RODADA DE INJECAO DIZ O QUE ESPERAVA ============================
		// Sem esta linha, uma rodada vermelha e ambigua: pode ser o defeito injetado aparecendo (o
		// certo) ou a bancada quebrada (o errado). Aqui ela declara a expectativa ANTES de o leitor
		// interpretar o placar -- e uma bancada que ficasse VERDE com o defeito na frente se
		// denuncia sozinha nesta mesma linha.
		// ================================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--semcobertura") >= 0)
		{
			int chapados = _feitas.Sum(c => c.Chapados);
			Nota("");
			Nota("--semcobertura: o esperado nesta rodada e VERMELHO -- sem a tela, as MESMAS amostras");
			Nota("   tem que achar tela vazia nas mesmas contas que ficam verdes na rodada boa.");
			Nota(chapados > 0 && _falha > 0
				 ? $"   -> achou {chapados} quadro(s) de fundo chapado e ficou vermelha, como tinha que ficar."
				 : "   -> NAO ACHOU TELA VAZIA COM O DEFEITO NA FRENTE: a bancada nao esta medindo nada.");
		}

		Nota(_falha == 0
			? $"===== {_ok} OK, NENHUMA FALHA ====="
			: $"===== {_ok} OK, {_falha} FALHA(S) =====\n[carga]   " + string.Join("\n[carga]   ", _reprovadas));

		// ============================ ELA SAI SOZINHA ============================
		// A bancada dava o veredito e ficava de pe. Numa rodada so isso nao aparece; num LOTE (a
		// rodada boa, a `--semcobertura`, a `--semaquecimento` e as de injecao, uma atras da outra)
		// a bateria inteira parava na primeira, e do lado de fora isso parece uma bancada lenta e
		// nao uma bancada travada. O segundo de folga e pro disco fechar as fotos que o `Revelar`
		// acabou de escrever e pro log sair inteiro. Mesmo desfecho do `RoboDeRolagem`.
		// =========================================================================
		if (GetTree() is { } arv) arv.CreateTimer(1.0).Timeout += () => GetTree()?.Quit();
	}
}
