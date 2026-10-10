using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Runtime;
using System.Runtime.InteropServices;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// O COLETOR DO .NET, MEDIDO DE DENTRO DO PROCESSO (`--diagcoletor [s]`).
///
/// ============================ O DEFEITO QUE ELA MEDE ============================
/// Com o corpo parado num campo aberto e nada acontecendo, o processo `--host` parava 38 a 44 ms a cada
/// ~0,37 s (2026-10-08; a `--diagestouro` ja contava essas pausas de manha, a 30-35 ms): uma coleta de
/// geracao 1 quase tres vezes por segundo, mais de 10% do tempo com o processo parado, quatro a seis quadros
/// perdidos de cada vez. A `--diagestouro` dizia QUANTO; esta diz DE QUEM e POR QUE.
///
/// DE QUEM ERA (41 corpos na tela, 149 no mundo, .NET 10.0.8, coletor workstation concorrente):
///
///     o servidor dedicado sozinho ....... 1 pausa de 5 ms em 20 s      1,8 MB/s    nao era dele
///     o cliente puro .................... 2,4 pausas/s de 41 ms        40 MB/s
///     o host (os dois no mesmo processo)  2,75 pausas/s de 38 ms       43,5 MB/s   10,4% do tempo parado
///
/// POR QUE CADA COLETA CUSTAVA 38 ms: nao era tamanho de heap (59 MB) nem numero de handles. O relogio de
/// animacao do `CharacterVisual` perguntava a animacao e os tempos dela ao Godot em todo quadro, por camada,
/// e cada pergunta fabrica um `StringName` -- 332 mil por segundo. Um `StringName` criado em C# sao TRES
/// objetos (ele, que tem finalizador, mais um `WeakReference` e um no de dicionario na tabela de descartaveis
/// do GodotSharp), e os tres SOBREVIVEM a primeira coleta, porque so morrem depois de a thread de finalizacao
/// rodar. Resultado: 13 dos 16 MB da geracao 0 subiam de geracao a CADA coleta, o orcamento da geracao 1
/// estourava sempre (toda coleta era de geracao 1), e os 38 ms eram 17 marcando 138 mil finalizaveis
/// recem-mortos, 4 varrendo o que a geracao velha apontava e 17 compactando os sobreviventes.
///
/// QUEM ALOCAVA, e o que foi feito:
///
///     `CharacterVisual._Process` ................ 40 MB/s (97%)   -> o compasso de cada camada (ver la)
///     `World.AoReceberSnapshot`/`CargaVisual` ... 0,69 MB/s       -> `NodePath` guardado (8 mil por segundo)
///     as 24 leituras de tecla do `LocalPlayer` .. 0,29 MB/s       -> `Teclas.NomeNoMotor`
///     os 11 uniformes do `ClimaNaTela` .......... 0,11 MB/s       -> `StringName` guardado
///     `Interacoes.Interativo` (a dica do "[E]") . 0,13 MB/s       -> resposta guardada por tipo
///     o servidor: 64,6 KB por tique ............. 1,9 MB/s        -> 8,7 KB por tique: as perguntas de nave e
///         o `Body.Achar` do snapshot sem fecho, o snapshot so pras zonas com gente, o fecho por corpo do
///         `TickDasMaquinasDeCura` (hoje `TickDoCampoBio`), a copia de `_players` das passagens e da nuvem
///
/// DEPOIS (o mesmo cenario, a mesma regua):
///
///     o host ............ 0 pausas em 20 s; em 120 s, 2 (4,5 e 7,9 ms): 0,01% do tempo parado    0,33 MB/s
///     o cliente puro .... 0 pausas em 20 s                                                         0,07 MB/s
///     o servidor sozinho  1 pausa de 4 ms em 20 s                                                  0,10 MB/s
///     o script da cena .. de 4,8 pra 1,4 ms por quadro; o quadro p99 de 46 pra 9 ms
///
/// O QUE SOBROU, e esta bancada mede sem cobrar: ~900 finalizaveis por segundo no host (385 `Variant+Disposer`
/// e 56 `Godot.Collections.Dictionary` que so aparecem com o servidor no mesmo processo -- origem nao achada --,
/// 370 `StringName`, 80 `NodePath`), e no servidor os `byte[]` dos pacotes de snapshot (73 KB/s) e as strings.
///
/// ============================ O QUE SE MEDE, E DE ONDE VEM CADA NUMERO ============================
/// TRES FONTES, e nenhuma delas e ferramenta de fora (nao ha `dotnet-trace` nem `dotnet-counters` na
/// maquina do dono):
///
///   * AS MARCAS DE QUADRO. Um node antes de CADA filho da raiz (os tres autoloads e a cena) mais um em
///     cada ponta do quadro: em cada marca, o relogio e `GC.GetAllocatedBytesForCurrentThread()`. Dai
///     saem os bytes e os milissegundos de cada FASE do quadro na thread principal -- o `_Process` do
///     `GameClient` (a rede do cliente), o do `GameServer` (rede + tiques de 30 Hz), a cena (o `World` e
///     tudo que pende dele) e o resto (desenho, sinais, temporizadores). A ordem de processo do Godot,
///     com a mesma prioridade, e a ordem da arvore: a marca que e irma de cima roda antes do irmao e de
///     todos os filhos dele.
///   * A REGUA DAS PAUSAS e `GC.GetTotalPauseDuration()` lido nas duas pontas do quadro -- a mesma da
///     `--diagestouro`, de proposito: os numeros das duas se comparam, e ela nao depende do ouvinte.
///   * O OUVINTE (`EventListener` no provedor do runtime, palavra-chave GC). Informa, por coleta: quem a
///     pediu, o motivo, a geracao, quanto o processo ficou suspenso, e os bytes promovidos POR TIPO DE
///     RAIZ (pilha, handles, geracao mais velha...) com a hora de cada etapa da marcacao -- e isso que
///     responde "por que 30 ms". No nivel verboso ele tambem recebe o `GCAllocationTick` (uma amostra a
///     cada ~100 KB alocados, com o TIPO do objeto e a thread) e o `FinalizeObject`.
///
/// O TIPO DE CADA AMOSTRA CAI NUMA FASE pelo relogio: o evento traz a hora em que a alocacao aconteceu.
/// Essa hora vem de um relogio que so difere do `Stopwatch` por uma constante, e a constante e medida:
/// a bancada aloca uns blocos de tamanho unico (`TiroDeCalibracao`) e casa cada um com o proprio eco.
///
/// O OUVINTE NAO E DE GRACA, e por isso ha um controle: `--coletornivel 0` roda sem ele (so as marcas e a
/// regua), `4` so com os eventos de coleta e `5` (o padrao) com as amostras de alocacao. MEDIDO: com 340 mil
/// objetos finalizados por segundo o nivel 5 recebia um evento por objeto e o despachante dele alocava
/// 110 MB/s -- as coletas passavam de 2,7 pra 10 por segundo. Os TIPOS e os FINALIZADOS do nivel 5 valem; a
/// frequencia e o tamanho das pausas so se leem no nivel 0 ou no 4 (o relatorio avisa quando e o caso).
///
/// ============================ A REGUA, E O CONTRA-EXEMPLO ============================
/// Quatro tetos (ver `TetoDaCenaPorCorpo` e os vizinhos): bytes por corpo na tela por quadro na cena, bytes
/// por corpo do mundo por tique no servidor, a fracao do tempo parado e a pausa mediana. Sao cobrados sobre
/// os numeros que nao dependem do ouvinte.
///
///     --coletordefeito relogio   liga `CharacterVisual.CompassoVencidoDeTeste`: o relogio de animacao volta a
///                                perguntar ao Godot em todo quadro. A regua tem que REPROVAR a cena (605 B por
///                                corpo por quadro contra o teto de 100).
///     --coletordefeito piloto    liga `GameServer.PilotoPorFechoDeTeste`: as perguntas de nave do snapshot
///                                voltam a montar um fecho por corpo, por tique. Reprova o servidor (108 B
///                                contra 85).
///
/// ============================ AS CHAVES DO COLETOR ============================
/// Rodando pelo editor do Godot o .NET sobe pelo `GodotPlugins.runtimeconfig.json` DO GODOT
/// (`rollForward: LatestMajor`, e por isso o runtime destas rodadas e o 10 e nao o 8 do projeto): o que se
/// escreve no `.csproj` so vale no jogo EXPORTADO. Pra medir uma chave aqui ela entra por variavel de
/// ambiente (`DOTNET_gcConcurrent=0`, `DOTNET_GCgen0MaxBudget=0x400000`...), e o relatorio imprime a
/// configuracao que o processo pegou de verdade.
///
/// ============================ COMO RODAR ============================
/// Em qualquer dos tres tipos de processo -- a pergunta "de quem e a pausa" e respondida comparando-os:
///
///     ... --host --rede 7964 --semfoco --campoteste 8 --horateste 0.5 --diagcoletor 20   cliente + servidor
///     ... --connect 127.0.0.1 --rede 7965 --semfoco --diagcoletor 20                     cliente puro
///     ... --headless --server --port 7965 --campoteste 8 --horateste 0.5 --diagcoletor 20   servidor dedicado
///
///     --coletorespera &lt;s&gt;   segundos gravados ANTES da janela medida (padrao 5): a entrada no mundo
///     --coletornivel 0|4|5   o controle do ouvinte (ver acima)
///     --coletornocaute       depois da janela, desliga o `_Process` de UMA classe de node por vez (1,5 s cada)
///                            e mede quanto a thread principal deixa de alocar -- ver `TickDoNocaute`
///     --coletordefeito &lt;qual&gt;   a rodada de injecao (ver acima)
///     --coletorfica          nao fecha o processo no fim (ao lado de outro robo, que manda na cena) e nao
///                            cobra a regua: os tetos sao os do corpo parado no campo
///
/// As cinco rodadas de um processo so estao no `testar-o-coletor.bat`. A do cliente puro sao dois processos:
/// o servidor dedicado primeiro (com `--coletorfica`, pra ele nao sair antes do cliente), depois o cliente.
/// </summary>
public partial class RoboDoColetor : Node
{
	// =====================================================================
	// A LINHA DE COMANDO
	// =====================================================================
	private static bool Tem(string flag) => Array.IndexOf(OS.GetCmdlineArgs(), flag) >= 0;

	private static double Numero(string flag, double padrao)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, flag);
		return i >= 0 && i + 1 < a.Length
			   && double.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
			? v : padrao;
	}

	private readonly double _janela = Math.Max(1, Numero("--diagcoletor", 20));
	private readonly double _espera = Math.Max(0, Numero("--coletorespera", 5));
	private readonly int _nivel = (int)Numero("--coletornivel", 5);
	private readonly bool _fica = Tem("--coletorfica");
	private readonly bool _dedicado = Tem("--server");
	private readonly bool _nocaute = Tem("--coletornocaute") && !Tem("--server");

	/// <summary>
	/// `--coletordefeito &lt;qual&gt;`: A RODADA DE INJECAO. Liga um defeito de bancada de producao, e a mesma regua
	/// tem que REPROVAR o que ele estraga:
	///
	///     relogio   `CharacterVisual.CompassoVencidoDeTeste` -- o relogio de animacao volta a perguntar ao Godot
	///               em todo quadro, por camada. Reprova a CENA.
	///     piloto    `GameServer.PilotoPorFechoDeTeste` -- as duas perguntas de nave do snapshot voltam a montar
	///               um fecho por corpo, por tique. Reprova o SERVIDOR.
	/// </summary>
	private readonly string _defeito = Palavra("--coletordefeito");

	private static string Palavra(string flag)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, flag);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	private enum Estado { Instalando, Esperando, Gravando, Fechando, Relatado }

	private Estado _estado = Estado.Instalando;
	private bool _noPonto;

	// =====================================================================
	// OS QUADROS
	// =====================================================================
	/// <summary>O nome de cada FASE do quadro: a fase `i` vai da marca `i` ate a marca seguinte.</summary>
	private string[] _fases = [];

	private int _marcas, _capacidade, _n;
	private long[] _t = [], _b = [];                           // por quadro e por marca: relogio e bytes da thread principal
	private long[] _doProcesso = [];                           // por quadro: bytes alocados pelo processo inteiro
	private double[] _pausaNoComeco = [], _pausaNoFim = [];    // por quadro: ms que o coletor ja parou o processo
	private int[] _coletas0 = [], _coletas1 = [], _coletas2 = [];
	private long[] _tiques = [];                               // por quadro: tiques que o servidor ja deu inteiros
	private long _comeco, _fim;
	private int _coletasVistas, _coletasSemFoto;

	private readonly List<string> _falhas = [];

	// =====================================================================
	// A REGUA
	// =====================================================================
	// O CENARIO E UM SO: o corpo parado no campo da Terra (`--campoteste 8`), com os quarenta habitantes dela
	// na tela, vinte segundos depois de cinco de entrada. Os tetos sao POR CORPO, que e a unidade em que o custo
	// cresce -- o mesmo defeito com oitenta corpos custa o dobro, e um teto em bytes soltos passaria a mentir.

	/// <summary>
	/// Quanto a CENA pode alocar por quadro, por corpo na tela: tudo o que a thread principal aloca fora do
	/// `_Process` do servidor (a rede do cliente, a cena, o desenho). Medido em 2026-10-08 com 41 corpos: 9358 B
	/// por corpo por quadro antes dos consertos (375 KB por quadro), 15 B depois (612 B por quadro), e 605 B com o
	/// defeito `relogio` injetado. O teto e o tamanho de UM `StringName` novo por corpo por quadro: 96 B com a
	/// entrada dele na tabela de descartaveis.
	/// </summary>
	private const double TetoDaCenaPorCorpo = 100;

	/// <summary>
	/// Quanto o `_Process` do servidor pode alocar por tique, por corpo do mundo. Medido em 2026-10-08 com 149
	/// corpos: 444 B antes (64,6 KB por tique), 60 B depois (8,7 KB por tique, o mesmo numero em toda rodada -- o
	/// tique aloca sempre igual) e 108 B com o defeito `piloto` injetado, que hoje so alcanca os corpos da zona
	/// em que ha alguem conectado.
	/// </summary>
	private const double TetoDoServidorPorCorpo = 85;

	/// <summary>Quanto do tempo o processo pode passar parado no coletor, em %. Antes: 11,9% no host, 0,0% no servidor sozinho.</summary>
	private const double TetoDeTempoParado = 0.5;

	/// <summary>A pausa mediana, em ms. Antes: 44 ms (uma coleta de geracao 1 com 13 MB de sobreviventes).</summary>
	private const double TetoDaPausa = 10;

	private void Conferir(bool ok, string oque)
	{
		Linha((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	/// <summary>Quantos quadros havia quando a JANELA fechou: o relatorio principal so le ate aqui (o nocaute grava depois).</summary>
	private int _quadrosDaJanela;

	// ---- o nocaute ----
	private bool _emNocaute;
	private List<(string Tipo, List<Node> Nodes)>? _alvos;
	private int _alvoAtual, _trechoDe;
	private long _trechoDesde;
	private readonly List<(string Nome, int Nodes, int De, int Ate)> _trechos = [];
	private const double SegundosPorTrecho = 1.5, DescarteDoTrecho = 0.3;

	/// <summary>A ULTIMA COLETA, como o runtime a descreve (`GC.GetGCMemoryInfo`) -- a fonte que nao depende do ouvinte.</summary>
	private struct Foto
	{
		public int Quadro, Geracao;
		public long Indice;
		public bool Compactou, Concorrente;
		public double PausaMs;
		public long Promovidos, Heap, Fragmentado, Comprometido, FinalizacaoPendente, Pinados;
		public long G0Antes, G0Depois, G1Antes, G1Depois, G2Antes, G2Depois, LohAntes, LohDepois, PohAntes, PohDepois;
	}

	private readonly List<Foto> _fotos = new(2048);

	// =====================================================================
	// O OUVINTE
	// =====================================================================
	private Ouvinte? _ouvinte;
	private readonly object _trava = new();
	private volatile bool _ouvindo;
	private uint _threadPrincipal, _threadDoOuvinte;

	/// <summary>UMA SUSPENSAO DO PROCESSO, do pedido ate a retomada, com a coleta que rodou dentro dela.</summary>
	private sealed class Pausa
	{
		public long TPedido, TSuspenso, TComeco, TFim, TRetomar, TRetomou;
		public int MotivoDaSuspensao = -1, Thread;
		public long Indice = -1;
		public int Geracao = -1, Motivo = -1, Tipo = -1;
		public readonly List<(int Raiz, long Bytes, long T)> Marcas = [];
		public long Orcamento0 = -1, Mecanismos = -1, Razoes0 = -1, Razoes1 = -1;
		public int Condenada = -1, PressaoDeMemoria = -1;
		public long[]? Stats;
	}

	private Pausa? _aberta;
	private readonly List<Pausa> _pausas = new(2048);

	/// <summary>UMA AMOSTRA DE ALOCACAO: a cada ~100 KB alocados o runtime diz o tipo do objeto que cruzou a marca.</summary>
	private struct Amostra
	{
		public long T, Bytes, Tamanho;
		public int Thread, Tipo;
		public byte Classe;   // 0 = heap pequeno, 1 = objetos grandes (LOH), 2 = pinados
	}

	private readonly List<Amostra> _amostras = new(65536);
	private readonly Dictionary<string, int> _indiceDoTipo = new(1024);
	private readonly List<string> _tipos = new(1024);
	private readonly Dictionary<long, int> _finalizados = new(256);
	private readonly Dictionary<string, (int Vezes, string Campos)> _eventos = new(64);
	private readonly Dictionary<int, string> _nomeDoThread = new(64);

	// ---- a calibracao do relogio dos eventos ----
	private const int TamanhoDoTiro = 131_771;
	private readonly List<(int Tamanho, long Antes, long Depois)> _tiros = new(16);
	private readonly List<(int Tamanho, long T)> _ecos = new(16);
	private int _quadrosDoRobo, _tirosDoFim;

	private readonly List<string> _saida = [];

	private void Linha(string l) => _saida.Add(l);

	// =====================================================================
	// A MONTAGEM
	// =====================================================================
	public override void _Ready()
	{
		_threadPrincipal = EsteThread();
		if (_defeito == "relogio") CharacterVisual.CompassoVencidoDeTeste = true;
		if (_defeito == "piloto") Jandirus.Server.GameServer.PilotoPorFechoDeTeste = true;
		if (_defeito.Length > 0) GD.Print($"[coletor] DEFEITO INJETADO nesta rodada: {_defeito}");
		// DEPOIS do `_Ready` de todo mundo: a raiz ainda esta montando os filhos, e `AddChild` nela falharia agora.
		Callable.From(Instalar).CallDeferred();
	}

	/// <summary>
	/// OS DEFEITOS INJETADOS NAO SOBREVIVEM A BANCADA: sao campos ESTATICOS de producao, e se este node sair da
	/// arvore no meio da corrida (janela fechada na mao) o jogo seguinte neste processo rodaria com eles.
	/// </summary>
	public override void _ExitTree() => DesligarOsDefeitos();

	private static void DesligarOsDefeitos()
	{
		CharacterVisual.CompassoVencidoDeTeste = false;
		Jandirus.Server.GameServer.PilotoPorFechoDeTeste = false;
	}

	/// <summary>
	/// AS MARCAS: uma em cada ponta do quadro (prioridade extrema) e uma ANTES de cada filho da raiz. A lista
	/// de filhos e lida aqui e nao escrita a mao -- se um autoload entrar ou mudar de ordem, as fases mudam
	/// de nome sozinhas em vez de medir o vizinho errado caladas.
	/// </summary>
	private void Instalar()
	{
		Window raiz = GetTree().Root;
		var filhos = new List<Node>();
		foreach (Node f in raiz.GetChildren()) filhos.Add(f);

		var fases = new List<string> { "antes de todos (prioridade negativa)" };
		AddChild(new MarcaDeQuadro { Name = "ComecoDoQuadro", ProcessPriority = -100_000, Agora = () => Marca(0) });
		for (int i = 0; i < filhos.Count; i++)
		{
			int marca = i + 1;
			var m = new MarcaDeQuadro { Name = $"MarcaDoColetor{marca}", Agora = () => Marca(marca) };
			raiz.AddChild(m);
			raiz.MoveChild(m, filhos[i].GetIndex());
			fases.Add(filhos[i].Name.ToString());
		}
		int ultima = filhos.Count + 1;
		AddChild(new MarcaDeQuadro { Name = "FimDoQuadro", ProcessPriority = 100_000, Agora = () => Marca(ultima) });
		fases.Add("o resto do quadro (desenho, sinais, temporizadores, entrada)");

		_fases = [.. fases];
		_marcas = _fases.Length;
		Reservar(16384);

		var ordem = new List<string>();
		foreach (Node f in raiz.GetChildren()) ordem.Add(f.Name.ToString());
		GD.Print($"[coletor] marcas instaladas; a raiz ficou: {string.Join(" > ", ordem)}");

		if (_nivel > 0)
		{
			_ouvinte = new Ouvinte { Chegou = AoChegar };
			_ouvinte.Ligar(_nivel >= 5 ? EventLevel.Verbose : EventLevel.Informational);
		}
		_estado = Estado.Esperando;
	}

	private void Reservar(int quadros)
	{
		_capacidade = quadros;
		Array.Resize(ref _t, quadros * _marcas);
		Array.Resize(ref _b, quadros * _marcas);
		Array.Resize(ref _doProcesso, quadros);
		Array.Resize(ref _pausaNoComeco, quadros);
		Array.Resize(ref _pausaNoFim, quadros);
		Array.Resize(ref _coletas0, quadros);
		Array.Resize(ref _coletas1, quadros);
		Array.Resize(ref _coletas2, quadros);
		Array.Resize(ref _tiques, quadros);
	}

	/// <summary>O servidor dedicado esta no ponto quando escuta; os outros, quando o corpo esta no mundo.</summary>
	private bool NoPonto() =>
		_dedicado ? Jandirus.Server.GameServer.Instance is { Running: true }
				  : World.Instancia?.PosicaoLocal != null;

	// =====================================================================
	// AS MARCAS
	// =====================================================================
	private void Marca(int i)
	{
		// OS DOIS NUMEROS PRIMEIRO: o que a propria marca gasta depois disto cai na fase seguinte, e e nada.
		long t = Stopwatch.GetTimestamp();
		long b = GC.GetAllocatedBytesForCurrentThread();

		if (i == 0)
		{
			if (_estado == Estado.Esperando && _noPonto)
			{
				_estado = Estado.Gravando;
				_comeco = t;
				_coletasVistas = GC.CollectionCount(0);
				_ouvindo = true;
			}
			if (_estado != Estado.Gravando) return;
			if (_n >= _capacidade) Reservar(_capacidade * 2);
			_doProcesso[_n] = GC.GetTotalAllocatedBytes(false);
			_pausaNoComeco[_n] = GC.GetTotalPauseDuration().TotalMilliseconds;
			_coletas0[_n] = GC.CollectionCount(0);
			_coletas1[_n] = GC.CollectionCount(1);
			_coletas2[_n] = GC.CollectionCount(2);
			_tiques[_n] = Jandirus.Server.GameServer.Instance is { Running: true } srv ? srv.ContagemDoColetorDeTeste.Tiques : 0;
		}
		else if (_estado != Estado.Gravando) return;

		int k = _n * _marcas + i;
		_t[k] = t;
		_b[k] = b;
		if (i < _marcas - 1) return;

		// ---- a ultima marca fecha o quadro ----
		_pausaNoFim[_n] = GC.GetTotalPauseDuration().TotalMilliseconds;
		int coletas = GC.CollectionCount(0);
		if (coletas != _coletasVistas)
		{
			_coletasSemFoto += Math.Max(0, coletas - _coletasVistas - 1);
			_coletasVistas = coletas;
			Fotografar();
		}
		_n++;
		if (_quadrosDaJanela == 0 && Stopwatch.GetElapsedTime(_comeco, t).TotalSeconds >= _espera + _janela)
		{
			_quadrosDaJanela = _n;
			// COM NOCAUTE a gravacao segue: quem a fecha e o ultimo trecho dele (`TickDoNocaute`).
			if (_nocaute) _emNocaute = true;
			else { _estado = Estado.Fechando; _fim = t; }
		}
	}

	private void Fotografar()
	{
		GCMemoryInfo m = GC.GetGCMemoryInfo(GCKind.Any);
		ReadOnlySpan<GCGenerationInfo> g = m.GenerationInfo;
		ReadOnlySpan<TimeSpan> p = m.PauseDurations;
		double pausa = 0;
		foreach (TimeSpan x in p) pausa += x.TotalMilliseconds;
		_fotos.Add(new Foto
		{
			Quadro = _n, Geracao = m.Generation, Indice = m.Index, Compactou = m.Compacted, Concorrente = m.Concurrent,
			PausaMs = pausa, Promovidos = m.PromotedBytes, Heap = m.HeapSizeBytes, Fragmentado = m.FragmentedBytes,
			Comprometido = m.TotalCommittedBytes, FinalizacaoPendente = m.FinalizationPendingCount, Pinados = m.PinnedObjectsCount,
			G0Antes = g[0].SizeBeforeBytes, G0Depois = g[0].SizeAfterBytes, G1Antes = g[1].SizeBeforeBytes, G1Depois = g[1].SizeAfterBytes,
			G2Antes = g[2].SizeBeforeBytes, G2Depois = g[2].SizeAfterBytes, LohAntes = g[3].SizeBeforeBytes, LohDepois = g[3].SizeAfterBytes,
			PohAntes = g[4].SizeBeforeBytes, PohDepois = g[4].SizeAfterBytes,
		});
	}

	// =====================================================================
	// O ROTEIRO (nenhum: ela so espera, grava e relata)
	// =====================================================================
	public override void _Process(double delta)
	{
		switch (_estado)
		{
			case Estado.Esperando:
				if (!_noPonto && NoPonto())
				{
					_noPonto = true;
					GD.Print($"[coletor] no ponto ({(_dedicado ? "o servidor escuta" : "o corpo esta no mundo")}): gravando {_espera:0} s de entrada + {_janela:0} s de janela"
							 + $" | ouvinte no nivel {_nivel}");
				}
				break;

			case Estado.Gravando when _emNocaute:
				TickDoNocaute();
				break;

			case Estado.Gravando:
				// TRES TIROS NA ENTRADA (fora da janela medida, quando ha espera) -- ver `TiroDeCalibracao`.
				_quadrosDoRobo++;
				if (_ouvinte != null && _nivel >= 5 && _tiros.Count < 3 && _quadrosDoRobo % 10 == 5) TiroDeCalibracao();
				break;

			case Estado.Fechando:
				// E TRES NA SAIDA, depois de a janela fechar; mais meio segundo pro despachante entregar os ultimos ecos.
				_quadrosDoRobo++;
				if (_ouvinte != null && _nivel >= 5 && _tirosDoFim < 3 && _quadrosDoRobo % 4 == 0) { _tirosDoFim++; TiroDeCalibracao(); }
				if (Stopwatch.GetElapsedTime(_fim).TotalSeconds >= 0.6) Relatar();
				break;
		}
	}

	/// <summary>
	/// UM BLOCO DE TAMANHO UNICO, com o `Stopwatch` lido antes e depois. Ele passa do limite dos objetos grandes,
	/// entao o runtime solta um `GCAllocationTick` NA HORA, com o tamanho exato -- e e esse eco que diz quanto o
	/// relogio dos eventos esta adiantado ou atrasado em relacao ao `Stopwatch` das marcas.
	/// </summary>
	private void TiroDeCalibracao()
	{
		int tamanho = TamanhoDoTiro + 1024 * _tiros.Count;
		long antes = Stopwatch.GetTimestamp();
		byte[] bloco = new byte[tamanho];
		long depois = Stopwatch.GetTimestamp();
		GC.KeepAlive(bloco);
		_tiros.Add((tamanho, antes, depois));
	}

	// =====================================================================
	// O NOCAUTE: DE QUE CLASSE DE NODE E O LIXO DA CENA
	// =====================================================================
	/// <summary>
	/// A AMOSTRA DE ALOCACAO DIZ O TIPO DO OBJETO, E NAO QUEM O CRIOU -- e na cena quase tudo e o mesmo punhado
	/// de tipos do Godot (`StringName`, `Variant`...), criados por dezenas de classes diferentes. Em vez de
	/// cercar o `_Process` de cada classe com marcas (mexer em producao pra medir), a bancada DESLIGA o
	/// `_Process` de uma classe por vez, por um segundo e meio, e le quanto a thread principal deixou de alocar
	/// por quadro. O primeiro e o ultimo trecho sao o controle, com nada desligado.
	///
	/// So mede o que roda no `_Process`: o que uma classe aloca ao tratar um pacote ou ao desenhar (`_Draw`)
	/// continua la com ela desligada. E a tela fica errada enquanto dura (corpo parado no quadro, remoto sem
	/// interpolar): e bancada, ninguem esta olhando.
	/// </summary>
	private void TickDoNocaute()
	{
		long agora = Stopwatch.GetTimestamp();
		if (_alvos == null)
		{
			_alvos = OsQueProcessam();
			_alvoAtual = -1;
			ComecarOTrecho(agora);
			return;
		}

		double s = Stopwatch.GetElapsedTime(_trechoDesde, agora).TotalSeconds;
		if (_trechoDe < 0 && s >= DescarteDoTrecho) _trechoDe = _n + 1;   // os primeiros quadros sao da troca
		if (s < SegundosPorTrecho) return;

		// ---- fecha o trecho: religa quem ele desligou ----
		bool controle = _alvoAtual < 0 || _alvoAtual >= _alvos.Count;
		if (!controle)
			foreach (Node n in _alvos[_alvoAtual].Nodes)
				if (IsInstanceValid(n)) n.SetProcess(true);
		if (_trechoDe >= 0 && _n - 1 > _trechoDe)
			_trechos.Add((controle ? "CONTROLE (nada desligado)" : _alvos[_alvoAtual].Tipo, controle ? 0 : _alvos[_alvoAtual].Nodes.Count, _trechoDe, _n - 1));

		_alvoAtual++;
		if (_alvoAtual > _alvos.Count)
		{
			_emNocaute = false;
			_estado = Estado.Fechando;
			_fim = agora;
			return;
		}
		ComecarOTrecho(agora);
	}

	private void ComecarOTrecho(long agora)
	{
		_trechoDesde = agora;
		_trechoDe = -1;
		if (_alvos == null || _alvoAtual < 0 || _alvoAtual >= _alvos.Count) return;
		foreach (Node n in _alvos[_alvoAtual].Nodes)
			if (IsInstanceValid(n)) n.SetProcess(false);
	}

	/// <summary>
	/// Todo node da arvore com `_Process` ligado, agrupado pela classe -- menos os tres autoloads (o custo deles
	/// ja sai nas fases, e desligar o `GameServer` pararia o mundo) e as pecas desta bancada.
	/// </summary>
	private List<(string Tipo, List<Node> Nodes)> OsQueProcessam()
	{
		var porTipo = new Dictionary<string, List<Node>>();
		var pilha = new Stack<Node>();
		foreach (Node f in GetTree().Root.GetChildren()) pilha.Push(f);
		while (pilha.Count > 0)
		{
			Node n = pilha.Pop();
			if (n == this || n is MarcaDeQuadro or GameClient or Jandirus.Server.GameServer or AudioDirector) continue;
			foreach (Node f in n.GetChildren()) pilha.Push(f);
			if (!n.IsProcessing()) continue;
			string tipo = n.GetType().Name;
			if (!porTipo.TryGetValue(tipo, out List<Node>? l)) porTipo[tipo] = l = [];
			l.Add(n);
		}
		var lista = new List<(string Tipo, List<Node> Nodes)>();
		foreach ((string tipo, List<Node> nodes) in porTipo) lista.Add((tipo, nodes));
		lista.Sort((a, b) => b.Nodes.Count.CompareTo(a.Nodes.Count));
		return lista;
	}

	// =====================================================================
	// O QUE CHEGA DO RUNTIME (roda na thread do despachante de eventos, nunca na principal)
	// =====================================================================
	private sealed class Ouvinte : EventListener
	{
		private EventSource? _runtime;
		private EventLevel? _nivel;
		public Action<EventWrittenEventArgs>? Chegou;

		// CHAMADO DE DENTRO DO CONSTRUTOR DA BASE pra toda fonte que ja existe -- antes de o corpo deste
		// construtor rodar. Por isso ele so GUARDA a fonte; quem liga e o `Ligar`, chamado depois.
		protected override void OnEventSourceCreated(EventSource fonte)
		{
			if (fonte.Name != "Microsoft-Windows-DotNETRuntime") return;
			_runtime = fonte;
			if (_nivel is { } n) EnableEvents(fonte, n, (EventKeywords)0x1);
		}

		/// <summary>Palavra-chave 0x1 = GC. No nivel verboso entram o `GCAllocationTick` e o `FinalizeObject`.</summary>
		public void Ligar(EventLevel nivel)
		{
			_nivel = nivel;
			if (_runtime != null) EnableEvents(_runtime, nivel, (EventKeywords)0x1);
		}

		protected override void OnEventWritten(EventWrittenEventArgs e) => Chegou?.Invoke(e);
	}

	private static long Campo(EventWrittenEventArgs e, string nome, long padrao = -1)
	{
		if (e.PayloadNames is not { } nomes || e.Payload is not { } valores) return padrao;
		int i = nomes.IndexOf(nome);
		if (i < 0 || i >= valores.Count) return padrao;
		return valores[i] switch
		{
			null => padrao,
			IntPtr p => p.ToInt64(),
			UIntPtr u => unchecked((long)u.ToUInt64()),
			ulong u => unchecked((long)u),
			IConvertible c => c.ToInt64(System.Globalization.CultureInfo.InvariantCulture),
			_ => padrao,
		};
	}

	private static string Texto(EventWrittenEventArgs e, string nome)
	{
		if (e.PayloadNames is not { } nomes || e.Payload is not { } valores) return "";
		int i = nomes.IndexOf(nome);
		return i >= 0 && i < valores.Count ? valores[i] as string ?? "" : "";
	}

	private static bool Comeca(string nome, string prefixo) => nome.StartsWith(prefixo, StringComparison.Ordinal);

	private void AoChegar(EventWrittenEventArgs e)
	{
		if (!_ouvindo || e.EventName is not { } nome) return;
		long t = e.TimeStamp.Ticks;

		lock (_trava)
		{
			if (!_ouvindo) return;
			if (_threadDoOuvinte == 0) _threadDoOuvinte = EsteThread();

			if (_eventos.TryGetValue(nome, out (int Vezes, string Campos) visto)) _eventos[nome] = (visto.Vezes + 1, visto.Campos);
			else _eventos[nome] = (1, e.PayloadNames is { } pn ? string.Join(",", pn) : "");

			if (Comeca(nome, "GCAllocationTick"))
			{
				long tamanho = Campo(e, "ObjectSize", 0);
				byte classe = (byte)Campo(e, "AllocationKind", 0);
				long bytes = Campo(e, "AllocationAmount64", 0);
				if (bytes <= 0) bytes = Campo(e, "AllocationAmount", 0);

				// O ECO DE UM TIRO DE CALIBRACAO nao e amostra: e o relogio.
				if (classe == 1)
				{
					long sobra = tamanho - 24 - TamanhoDoTiro;
					long qual = (sobra + 512) / 1024;
					if (sobra >= 0 && qual < 16 && Math.Abs(sobra - qual * 1024) <= 16)
					{
						_ecos.Add((TamanhoDoTiro + (int)qual * 1024, t));
						return;
					}
				}

				string tipo = Texto(e, "TypeName");
				if (tipo.Length == 0) tipo = "(sem nome)";
				if (!_indiceDoTipo.TryGetValue(tipo, out int it)) { it = _tipos.Count; _tipos.Add(tipo); _indiceDoTipo[tipo] = it; }
				int thread = (int)e.OSThreadId;
				if (!_nomeDoThread.ContainsKey(thread)) _nomeDoThread[thread] = NomeDoThread(thread);
				_amostras.Add(new Amostra { T = t, Bytes = bytes, Tamanho = tamanho, Thread = thread, Tipo = it, Classe = classe });
				return;
			}

			if (Comeca(nome, "FinalizeObject"))
			{
				long tipo = Campo(e, "TypeID", 0);
				_finalizados[tipo] = _finalizados.GetValueOrDefault(tipo) + 1;
				return;
			}

			if (Comeca(nome, "GCSuspendEEBegin"))
			{
				_aberta = new Pausa { TPedido = t, MotivoDaSuspensao = (int)Campo(e, "Reason"), Thread = (int)e.OSThreadId };
				return;
			}
			if (_aberta is not { } p) return;   // o que chega fora de uma suspensao e de coleta de fundo: nao para ninguem

			if (Comeca(nome, "GCSuspendEEEnd")) p.TSuspenso = t;
			else if (Comeca(nome, "GCStart"))
			{
				if (p.Indice < 0)
				{
					p.Indice = Campo(e, "Count");
					p.Geracao = (int)Campo(e, "Depth");
					p.Motivo = (int)Campo(e, "Reason");
					p.Tipo = (int)Campo(e, "Type");
					p.TComeco = t;
				}
			}
			else if (Comeca(nome, "GCMarkWithType")) p.Marcas.Add(((int)Campo(e, "Type"), Campo(e, "Bytes", 0), t));
			else if (Comeca(nome, "GCEnd")) p.TFim = t;
			else if (Comeca(nome, "GCGlobalHeapHistory"))
			{
				p.Orcamento0 = Campo(e, "FinalYoungestDesired");
				p.Condenada = (int)Campo(e, "CondemnedGeneration");
				p.Mecanismos = Campo(e, "GlobalMechanisms");
				p.PressaoDeMemoria = (int)Campo(e, "MemoryPressure");
			}
			else if (Comeca(nome, "GCPerHeapHistory"))
			{
				p.Razoes0 = Campo(e, "CondemnReasons0");
				p.Razoes1 = Campo(e, "CondemnReasons1");
			}
			else if (Comeca(nome, "GCHeapStats"))
			{
				p.Stats =
				[
					Campo(e, "GenerationSize0", 0), Campo(e, "TotalPromotedSize0", 0), Campo(e, "GenerationSize1", 0), Campo(e, "TotalPromotedSize1", 0),
					Campo(e, "GenerationSize2", 0), Campo(e, "TotalPromotedSize2", 0), Campo(e, "GenerationSize3", 0), Campo(e, "TotalPromotedSize3", 0),
					Campo(e, "GenerationSize4", 0), Campo(e, "TotalPromotedSize4", 0), Campo(e, "FinalizationPromotedSize", 0),
					Campo(e, "FinalizationPromotedCount", 0), Campo(e, "PinnedObjectCount", 0), Campo(e, "SinkBlockCount", 0), Campo(e, "GCHandleCount", 0),
				];
			}
			else if (Comeca(nome, "GCRestartEEBegin")) p.TRetomar = t;
			else if (Comeca(nome, "GCRestartEEEnd"))
			{
				p.TRetomou = t;
				_pausas.Add(p);
				_aberta = null;
			}
		}
	}

	// =====================================================================
	// AS THREADS (so pra dar NOME a quem aloca fora da principal)
	// =====================================================================
	[DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
	[DllImport("kernel32.dll")] private static extern IntPtr OpenThread(uint acesso, bool herdar, uint id);
	[DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
	[DllImport("kernel32.dll")] private static extern int GetThreadDescription(IntPtr h, out IntPtr texto);
	[DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);

	private static uint EsteThread() => OperatingSystem.IsWindows() ? GetCurrentThreadId() : 0;

	/// <summary>O nome que o .NET deu a thread (`Thread.Name` vira a descricao da thread no Windows).</summary>
	private static string NomeDoThread(int id)
	{
		if (!OperatingSystem.IsWindows() || id <= 0) return "?";
		IntPtr h = OpenThread(0x0800, false, (uint)id);
		if (h == IntPtr.Zero) return "(ja morreu)";
		try
		{
			if (GetThreadDescription(h, out IntPtr p) < 0 || p == IntPtr.Zero) return "(sem nome)";
			string s = Marshal.PtrToStringUni(p) ?? "";
			LocalFree(p);
			return s.Length > 0 ? s : "(sem nome)";
		}
		catch (EntryPointNotFoundException) { return "?"; }
		finally { CloseHandle(h); }
	}

	// =====================================================================
	// AS CONTAS
	// =====================================================================
	private static readonly double MsPorTique = 1000.0 / Stopwatch.Frequency;

	private double Ms(long tiques) => tiques * MsPorTique;

	/// <summary>Os tiques da fase `i` do quadro `q`; a ultima fase vai ate a primeira marca do quadro seguinte.</summary>
	private long TempoDaFase(int q, int i) =>
		i < _marcas - 1 ? _t[q * _marcas + i + 1] - _t[q * _marcas + i] : _t[(q + 1) * _marcas] - _t[q * _marcas + i];

	private long BytesDaFase(int q, int i) =>
		i < _marcas - 1 ? _b[q * _marcas + i + 1] - _b[q * _marcas + i] : _b[(q + 1) * _marcas] - _b[q * _marcas + i];

	private double ColetorNoQuadro(int q) => _pausaNoComeco[q + 1] - _pausaNoComeco[q];

	private static double Mediana(List<double> v) => Percentil(v, 0.5);

	/// <summary>ORDENA a lista que recebe.</summary>
	private static double Percentil(List<double> v, double p)
	{
		if (v.Count == 0) return 0;
		v.Sort();
		return v[Math.Min(v.Count - 1, (int)(v.Count * p))];
	}

	private static double Media(List<double> v) { double s = 0; foreach (double x in v) s += x; return v.Count > 0 ? s / v.Count : 0; }

	private static string Tam(double bytes) =>
		Math.Abs(bytes) >= 1024 * 1024 ? $"{bytes / (1024 * 1024):0.00} MB"
		: Math.Abs(bytes) >= 1024 ? $"{bytes / 1024:0.0} KB" : $"{bytes:0} B";

	private static readonly string[] Raizes =
	[
		"pilha", "fila de finalizacao", "handles", "geracao mais velha (cartoes)", "sized ref", "overflow",
		"handles dependentes", "finalizaveis recem-mortos", "roubo", "coleta de fundo",
	];

	private static string NomeDaRaiz(int r) => r >= 0 && r < Raizes.Length ? Raizes[r] : $"raiz {r}";

	private static readonly string[] Motivos =
	[
		"alocacao (heap pequeno)", "pedida (GC.Collect)", "pouca memoria", "vazia", "alocacao (objeto grande)", "sem espaco (pequeno)",
		"sem espaco (grande)", "pedida sem forcar", "stress", "pedida por pouca memoria", "pedida compactando", "pouca memoria (host)",
		"PM full", "pouca memoria (host, bloqueante)", "ajuste de fundo (pequeno)", "ajuste de fundo (grande)", "passo de fundo", "pedida agressiva",
	];

	private static string NomeDoMotivo(int m) => m >= 0 && m < Motivos.Length ? Motivos[m] : $"motivo {m}";

	private readonly Dictionary<string, string> _donoDaGerada = [];

	/// <summary>
	/// DE QUE METODO SAIU UMA CLASSE GERADA PELO COMPILADOR. A amostra traz so o nome curto
	/// (`&lt;&gt;c__DisplayClass2675_0`), e o numero nao diz nada a ninguem: a classe e a sacola das variaveis que um
	/// lambda capturou, e os metodos DELA carregam o nome de quem a criou (`&lt;EstaPilotando&gt;b__0`). Devolve
	/// " &lt;= Classe.Metodo", ou vazio quando o tipo nao e um desses.
	/// </summary>
	private string DonoDaGerada(string tipo)
	{
		if (!tipo.StartsWith("<>c__DisplayClass", StringComparison.Ordinal)) return "";
		if (_donoDaGerada.TryGetValue(tipo, out string? pronto)) return pronto;

		int corte = tipo.IndexOf('[');
		string curto = corte > 0 ? tipo[..corte] : tipo;
		var achados = new List<string>();
		foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies())
		{
			string nome = a.GetName().Name ?? "";
			if (!nome.StartsWith("Jandirus", StringComparison.Ordinal) && !nome.StartsWith("Dragon", StringComparison.Ordinal)) continue;
			var tipos = new List<Type>();
			try { tipos.AddRange(a.GetTypes()); }
			catch (System.Reflection.ReflectionTypeLoadException e) { foreach (Type? t in e.Types) if (t != null) tipos.Add(t); }
			foreach (Type t in tipos)
			{
				if (t.Name != curto || t.DeclaringType == null) continue;
				var metodos = new List<string>();
				foreach (System.Reflection.MethodInfo m in t.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
																	   | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
				{
					int fim = m.Name.IndexOf('>');
					if (!m.Name.StartsWith('<') || fim <= 1) continue;
					string de = m.Name[1..fim];
					if (!metodos.Contains(de)) metodos.Add(de);
				}
				achados.Add($"{t.DeclaringType.Name}.{string.Join("/", metodos)}");
			}
		}
		return _donoDaGerada[tipo] = achados.Count > 0 ? "   <= " + string.Join(" | ", achados) : "";
	}

	// =====================================================================
	// O RELATORIO
	// =====================================================================
	private void Relatar()
	{
		_estado = Estado.Relatado;
		_ouvindo = false;
		_ouvinte?.Dispose();
		_ouvinte = null;

		lock (_trava)
		{
			try { Escrever(); }
			catch (Exception ex) { Conferir(false, $"o relatorio saiu inteiro (estourou: {ex})"); }
			finally { DesligarOsDefeitos(); }   // com conferencia ou sem ela
		}

		GD.Print("\n[coletor] ===== O COLETOR DO .NET, MEDIDO DE DENTRO =====");
		foreach (string l in _saida) GD.Print("[coletor] " + l);
		if (!_fica)
			GD.Print(_falhas.Count == 0
				? "[coletor] ===== TUDO OK ====="
				: $"[coletor] ===== {_falhas.Count} FALHA(S) =====\n[coletor]   " + string.Join("\n[coletor]   ", _falhas));
		GD.Print("[coletor] ===== FIM =====");
		if (!_fica) GetTree().Quit();
	}

	/// <summary>Quantos `CharacterVisual` estao com o `_Process` ligado agora: os corpos que a cena anima a cada quadro.</summary>
	private int CorposNaTela()
	{
		int n = 0;
		var pilha = new Stack<Node>();
		pilha.Push(GetTree().Root);
		while (pilha.Count > 0)
		{
			Node no = pilha.Pop();
			if (no is CharacterVisual && no.IsProcessing()) n++;
			foreach (Node f in no.GetChildren()) pilha.Push(f);
		}
		return n;
	}

	/// <summary>
	/// A REGUA DA BANCADA -- ver os quatro tetos la em cima. Ela e cobrada sobre os numeros que NAO dependem do
	/// ouvinte: os bytes das marcas de quadro e `GC.GetTotalPauseDuration`.
	/// </summary>
	private void Medir(int de, int ultimo, double segundos, int quadros, double bytesDaCena, double bytesDoServidor,
					   List<double> pausas, double somaDasPausas)
	{
		if (_fica) { Linha("A REGUA: nao cobrada (`--coletorfica`: a cena e de outro robo, e os tetos sao os do corpo parado no campo)"); return; }
		Linha("A REGUA:");

		if (!_dedicado)
		{
			int corpos = Math.Max(1, CorposNaTela());
			double porCorpo = bytesDaCena / quadros / corpos;
			string medida = $"{porCorpo:0} B por corpo na tela por quadro, contra o teto de {TetoDaCenaPorCorpo:0}"
							+ $" ({Tam(bytesDaCena / quadros)} por quadro com {corpos} corpos; tudo na thread principal fora o `_Process` do servidor)";
			if (_defeito == "relogio")
				Conferir(porCorpo > TetoDaCenaPorCorpo, $"(defeito injetado: o relogio de animacao perguntando ao Godot em todo quadro) a mesma regua REPROVA a cena: {medida}");
			else
				Conferir(porCorpo <= TetoDaCenaPorCorpo, $"a cena aloca {medida}");
		}

		if (Jandirus.Server.GameServer.Instance is { Running: true } srv)
		{
			int corposDoMundo = Math.Max(1, srv.ContagemDoColetorDeTeste.Corpos);
			long tiques = _tiques[ultimo + 1] - _tiques[de];
			double porCorpo = tiques > 0 ? bytesDoServidor / tiques / corposDoMundo : 0;
			string medida = $"{porCorpo:0} B por corpo do mundo por tique, contra o teto de {TetoDoServidorPorCorpo:0}"
							+ $" ({Tam(tiques > 0 ? bytesDoServidor / tiques : 0)} por tique com {corposDoMundo} corpos; {tiques} tiques na janela)";
			if (_defeito == "piloto")
				Conferir(tiques > 0 && porCorpo > TetoDoServidorPorCorpo, $"(defeito injetado: as perguntas de nave do snapshot por LINQ, com fecho) a mesma regua REPROVA o servidor: {medida}");
			else
				Conferir(tiques > 0 && porCorpo <= TetoDoServidorPorCorpo, $"o servidor aloca {medida}");
		}

		double parado = somaDasPausas / (segundos * 10);
		double mediana = Mediana(pausas);
		string doTempo = $"o processo passa {parado:0.00}% do tempo parado no coletor, contra o teto de {TetoDeTempoParado:0.0}% ({pausas.Count} pausa(s) em {segundos:0} s)";
		string daPausa = $"a pausa mediana do coletor e de {mediana:0.0} ms, contra o teto de {TetoDaPausa:0} ms";
		if (_defeito.Length > 0)
		{
			// NA RODADA DE INJECAO as pausas saem como NUMERO: o defeito de bancada e uma versao pequena do de
			// verdade (ele enche a geracao 0 em segundos, e nao em tercos de segundo), e o que ele tem que
			// reprovar com folga e o teto de BYTES de quem ele estraga -- a linha de cima.
			Linha("  --     " + doTempo);
			Linha("  --     " + daPausa);
			return;
		}
		Conferir(parado <= TetoDeTempoParado, doTempo);
		Conferir(mediana <= TetoDaPausa, daPausa);
	}

	private void Escrever()
	{
		// ---- O AMBIENTE: sem isto os numeros de duas rodadas nao se comparam ----
		bool semOtimizar = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<DebuggableAttribute>(typeof(RoboDoColetor).Assembly) is { IsJITOptimizerDisabled: true };
		string tipo = _dedicado ? "SERVIDOR DEDICADO" : Jandirus.Server.GameServer.Instance is { Running: true } ? "HOST (cliente + servidor)" : "CLIENTE PURO";
		Linha($"processo: {tipo} | tela: {DisplayServer.GetName()} | runtime: {RuntimeInformation.FrameworkDescription}"
			  + $" | binario do jogo: {(semOtimizar ? "Debug (JIT sem otimizar)" : "otimizado")}");
		Linha($"coletor: {(GCSettings.IsServerGC ? "SERVER" : "workstation")}, latencia {GCSettings.LatencyMode}, {System.Environment.ProcessorCount} nucleos"
			  + $" | ouvinte no nivel {_nivel}");
		var chaves = new List<string>();
		foreach (KeyValuePair<string, object> par in GC.GetConfigurationVariables()) chaves.Add($"{par.Key}={par.Value}");
		Linha("configuracao do coletor: " + string.Join(" ", chaves));
		var ambiente = new List<string>();
		foreach (System.Collections.DictionaryEntry v in System.Environment.GetEnvironmentVariables())
			if (v.Key is string k && (k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase)))
				ambiente.Add($"{k}={v.Value}");
		Linha("variaveis de ambiente do runtime: " + (ambiente.Count > 0 ? string.Join(" ", ambiente) : "(nenhuma)"));
		Linha($"Godot: {Performance.GetMonitor(Performance.Monitor.ObjectCount):0} objetos, {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0} nodes,"
			  + $" {Performance.GetMonitor(Performance.Monitor.ObjectResourceCount):0} recursos");

		// ---- A JANELA ----
		int gravados = _quadrosDaJanela > 0 ? _quadrosDaJanela : _n;
		if (gravados < 20) { Conferir(false, $"ha quadros pra relatar (so {gravados} gravados)"); return; }
		int ultimo = gravados - 2;   // o ultimo quadro com o seguinte gravado (a fase do resto precisa dele)
		int de = 0;
		long limiar = _comeco + (long)(_espera * Stopwatch.Frequency);
		while (de < ultimo && _t[de * _marcas] < limiar) de++;
		double segundos = Ms(_t[(ultimo + 1) * _marcas] - _t[de * _marcas]) / 1000.0;
		int quadros = ultimo - de + 1;
		Linha($"JANELA: {segundos:0.0} s, {quadros} quadros ({quadros / segundos:0} por segundo), depois de {_espera:0} s de entrada");

		// ---- AS PAUSAS, pela regua das duas pontas do quadro ----
		var totais = new List<double>();
		var comPausa = new List<double>();
		var pausas = new List<double>();
		int[] porGeracao = new int[3];
		double[] msPorGeracao = new double[3];
		int noScript = 0;
		for (int q = de; q <= ultimo; q++)
		{
			double total = Ms(_t[(q + 1) * _marcas] - _t[q * _marcas]);
			totais.Add(total);
			double pausa = ColetorNoQuadro(q);
			if (pausa <= 1) continue;
			pausas.Add(pausa);
			comPausa.Add(total);
			if (_pausaNoFim[q] - _pausaNoComeco[q] > 1) noScript++;
			int g = _coletas2[q + 1] > _coletas2[q] ? 2 : _coletas1[q + 1] > _coletas1[q] ? 1 : 0;
			porGeracao[g]++;
			msPorGeracao[g] += pausa;
		}
		double somaDasPausas = 0;
		foreach (double x in pausas) somaDasPausas += x;
		double quadroMediano = Mediana(totais);
		Linha($"PAUSAS (regua: GC.GetTotalPauseDuration nas pontas do quadro): {pausas.Count} em {segundos:0.0} s = {pausas.Count / segundos:0.00} por segundo,"
			  + $" {(pausas.Count > 0 ? somaDasPausas / pausas.Count : 0):0.0} ms cada (mediana {Mediana(pausas):0.0}, pior {(pausas.Count > 0 ? pausas[^1] : 0):0.0})"
			  + $" = {somaDasPausas / (segundos * 10):0.0}% do tempo com o processo parado");
		Linha($"   por geracao: g0 {porGeracao[0]} ({(porGeracao[0] > 0 ? msPorGeracao[0] / porGeracao[0] : 0):0.0} ms cada),"
			  + $" g1 {porGeracao[1]} ({(porGeracao[1] > 0 ? msPorGeracao[1] / porGeracao[1] : 0):0.0} ms),"
			  + $" g2 {porGeracao[2]} ({(porGeracao[2] > 0 ? msPorGeracao[2] / porGeracao[2] : 0):0.0} ms)"
			  + $" | {noScript} cairam na fase de script, {pausas.Count - noScript} no resto do quadro");
		Linha($"   quadro mediano {quadroMediano:0.0} ms (p99 {Percentil(totais, 0.99):0.0}, pior {totais[^1]:0.0});"
			  + $" o quadro com pausa dura {Mediana(comPausa):0.0} ms na mediana = {(quadroMediano > 0 ? Mediana(comPausa) / quadroMediano : 0):0.0} quadros");

		// ---- A ALOCACAO ----
		double doProcesso = (_doProcesso[ultimo + 1] - _doProcesso[de]) / segundos;
		double daPrincipal = (_b[(ultimo + 1) * _marcas] - _b[de * _marcas]) / segundos;
		Linha($"ALOCACAO: o processo aloca {Tam(doProcesso)}/s; a thread principal {Tam(daPrincipal)}/s ({(doProcesso > 0 ? 100 * daPrincipal / doProcesso : 0):0}%),"
			  + $" as outras threads {Tam(doProcesso - daPrincipal)}/s"
			  + (pausas.Count > 0 ? $" | entre duas pausas o processo aloca {Tam(doProcesso * segundos / pausas.Count)}" : ""));

		Linha("POR FASE DO QUADRO, na thread principal (bytes e tempo; a pausa do coletor esta dentro do tempo da fase em que caiu):");
		double bytesDaCena = 0, bytesDoServidor = 0;
		long tiquesNaJanela = _tiques[ultimo + 1] - _tiques[de];
		for (int i = 0; i < _marcas; i++)
		{
			var bytes = new List<double>(quadros);
			var tempos = new List<double>(quadros);
			double somaB = 0, somaT = 0;
			for (int q = de; q <= ultimo; q++)
			{
				double b = BytesDaFase(q, i), t = Ms(TempoDaFase(q, i));
				bytes.Add(b); tempos.Add(t); somaB += b; somaT += t;
			}
			bool doServidor = _fases[i] == "GameServer";
			if (doServidor) bytesDoServidor = somaB; else bytesDaCena += somaB;
			Linha($"   {_fases[i],-28} {Tam(somaB / segundos),12}/s ({(daPrincipal > 0 ? 100 * somaB / segundos / daPrincipal : 0),3:0}%)"
				  + $"  por quadro: mediana {Tam(Mediana(bytes)),10}, p95 {Tam(Percentil(bytes, 0.95)),10}, pior {Tam(bytes[^1]),10}"
				  + $"  |  {somaT / quadros:0.00} ms por quadro (mediana {Mediana(tempos):0.00}, p95 {Percentil(tempos, 0.95):0.00})"
				  + (doServidor && tiquesNaJanela > 0 ? $"  |  {Tam(somaB / tiquesNaJanela)} por tique ({tiquesNaJanela} tiques)" : ""));
		}

		// ---- A SERIE NO TEMPO: de 5 em 5 s desde o ponto, pra ver a entrada e o regime ----
		Linha("DE 5 EM 5 s DESDE O PONTO (a entrada no mundo esta nas primeiras linhas):");
		long passo = 5 * Stopwatch.Frequency;
		for (int q0 = 0; q0 < ultimo;)
		{
			long ate = _t[q0 * _marcas] + passo;
			int q1 = q0;
			int n = 0;
			double ms = 0;
			while (q1 <= ultimo && _t[q1 * _marcas] < ate) { double p = ColetorNoQuadro(q1); if (p > 1) { n++; ms += p; } q1++; }
			double s = Ms(_t[q1 * _marcas] - _t[q0 * _marcas]) / 1000.0;
			if (s > 0.5)
				Linha($"   t={Ms(_t[q0 * _marcas] - _comeco) / 1000.0,5:0.0}s  {n / s,5:0.00} pausas/s  {(n > 0 ? ms / n : 0),5:0.0} ms cada  {ms / (s * 10),4:0.0}% parado"
					  + $"  processo {Tam((_doProcesso[q1] - _doProcesso[q0]) / s),10}/s  principal {Tam((_b[q1 * _marcas] - _b[q0 * _marcas]) / s),10}/s"
					  + $"  {(q1 - q0) / s,4:0} quadros/s");
			q0 = q1;
		}

		EscreverAsFotos(de, ultimo);
		if (_nivel > 0) EscreverOOuvinte(de, ultimo, segundos, doProcesso);
		if (_trechos.Count > 0) EscreverONocaute();
		Medir(de, ultimo, segundos, quadros, bytesDaCena, bytesDoServidor, pausas, somaDasPausas);
	}

	private void EscreverONocaute()
	{
		Linha($"NOCAUTE: o `_Process` de UMA classe de node desligado por {SegundosPorTrecho:0.0} s de cada vez"
			  + $" (thread principal, o quadro inteiro; os primeiros {DescarteDoTrecho:0.0} s de cada trecho ficam fora):");
		var medidas = new List<(string Nome, int Nodes, double BytesPorS, double Script, double PorQuadro)>();
		double controle = 0, scriptDoControle = 0;
		int controles = 0;
		foreach ((string nome, int nodes, int de, int ate) in _trechos)
		{
			double s = Ms(_t[ate * _marcas] - _t[de * _marcas]) / 1000.0;
			if (s <= 0) continue;
			var script = new List<double>(ate - de);
			for (int q = de; q < ate; q++) script.Add(Ms(_t[q * _marcas + _marcas - 1] - _t[q * _marcas]));
			double bytes = _b[ate * _marcas] - _b[de * _marcas];
			medidas.Add((nome, nodes, bytes / s, Mediana(script), bytes / (ate - de)));
			if (nodes == 0) { controle += bytes / s; scriptDoControle += Mediana(script); controles++; }
		}
		if (controles > 0) { controle /= controles; scriptDoControle /= controles; }
		foreach ((string nome, int nodes, double porS, double script, double porQuadro) in medidas)
			Linha(nodes == 0
				? $"   {nome,-34} {Tam(porS),12}/s  {Tam(porQuadro),10} por quadro  script {script:0.00} ms"
				: $"   sem {nome + " x" + nodes,-30} {Tam(porS),12}/s  {Tam(porQuadro),10} por quadro  script {script:0.00} ms"
				  + $"   => ela aloca {Tam(controle - porS)}/s ({(controle > 0 ? 100 * (controle - porS) / controle : 0):0}% da thread principal)"
				  + $" e gasta {scriptDoControle - script:0.00} ms do quadro");
	}

	/// <summary>AS COLETAS COMO O RUNTIME AS DESCREVE (`GC.GetGCMemoryInfo`): o que havia, o que sobrou, o que subiu de geracao.</summary>
	private void EscreverAsFotos(int de, int ultimo)
	{
		var daJanela = _fotos.FindAll(f => f.Quadro >= de && f.Quadro <= ultimo + 1);
		if (daJanela.Count == 0) { Linha("FOTOS DAS COLETAS: nenhuma na janela"); return; }
		Linha($"FOTOS DAS COLETAS (GC.GetGCMemoryInfo, uma por quadro em que houve coleta; {_coletasSemFoto} coleta(s) sem foto por cairem duas no mesmo quadro):");
		for (int g = 0; g <= 2; g++)
		{
			var v = daJanela.FindAll(f => f.Geracao == g);
			if (v.Count == 0) continue;
			double Med(Func<Foto, double> campo) { var l = new List<double>(v.Count); foreach (Foto f in v) l.Add(campo(f)); return Mediana(l); }
			Linha($"   geracao {g}: {v.Count} coleta(s), pausa mediana {Med(f => f.PausaMs):0.0} ms | promovidos {Tam(Med(f => f.Promovidos))}"
				  + $" | g0 {Tam(Med(f => f.G0Antes))} -> {Tam(Med(f => f.G0Depois))} | g1 {Tam(Med(f => f.G1Antes))} -> {Tam(Med(f => f.G1Depois))}"
				  + $" | g2 {Tam(Med(f => f.G2Antes))} -> {Tam(Med(f => f.G2Depois))} | LOH {Tam(Med(f => f.LohDepois))} | POH {Tam(Med(f => f.PohDepois))}");
			Linha($"              heap {Tam(Med(f => f.Heap))} (comprometido {Tam(Med(f => f.Comprometido))}, fragmentado {Tam(Med(f => f.Fragmentado))})"
				  + $" | finalizacao pendente {Med(f => f.FinalizacaoPendente):0} objeto(s) | pinados {Med(f => f.Pinados):0}"
				  + $" | compactou em {v.FindAll(f => f.Compactou).Count} de {v.Count}, concorrente em {v.FindAll(f => f.Concorrente).Count}");
		}
		Foto a = daJanela[0], z = daJanela[^1];
		Linha($"   da primeira pra ultima foto da janela: g2 {Tam(a.G2Depois)} -> {Tam(z.G2Depois)}, LOH {Tam(a.LohDepois)} -> {Tam(z.LohDepois)}, heap {Tam(a.Heap)} -> {Tam(z.Heap)}");
	}

	private void EscreverOOuvinte(int de, int ultimo, double segundos, double doProcesso)
	{
		// ---- O RELOGIO DOS EVENTOS ----
		// `evento.Ticks - K` da a hora do evento na escala do Stopwatch (em tiques de 100 ns).
		double tiquesPorSw = 10_000_000.0 / Stopwatch.Frequency;
		long k = 0;
		long melhor = long.MaxValue;
		var ks = new List<double>();
		foreach ((int tamanho, long antes, long depois) in _tiros)
			foreach ((int eco, long t) in _ecos)
			{
				if (eco != tamanho) continue;
				long este = t - (long)((antes + depois) / 2.0 * tiquesPorSw);
				ks.Add(este / 10.0);
				if (depois - antes < melhor) { melhor = depois - antes; k = este; }
			}
		bool calibrado = ks.Count > 0;
		if (_nivel >= 5)
			Linha(calibrado
				? $"RELOGIO DOS EVENTOS: {ks.Count} de {_tiros.Count} tiros com eco; o melhor cercou a alocacao em {Ms(melhor) * 1000:0} us;"
				  + $" os deslocamentos medidos diferem em {(Percentil(ks, 1) - Percentil(ks, 0)):0} us entre si"
				: $"RELOGIO DOS EVENTOS: NENHUM eco dos {_tiros.Count} tiros -- as amostras ficam sem fase");
		long EmSw(long tiquesDoEvento) => (long)((tiquesDoEvento - k) / tiquesPorSw);

		long janelaDe = _t[de * _marcas], janelaAte = _t[(ultimo + 1) * _marcas];
		var comecos = new long[_n];
		for (int q = 0; q < _n; q++) comecos[q] = _t[q * _marcas];

		// ---- OS EVENTOS QUE CHEGARAM (pra o relatorio se explicar sozinho num runtime de outra versao) ----
		var nomes = new List<string>(_eventos.Keys);
		nomes.Sort(StringComparer.Ordinal);
		Linha("EVENTOS DO RUNTIME QUE CHEGARAM (nome x vezes): " + string.Join(", ", nomes.ConvertAll(n => $"{n} x{_eventos[n].Vezes}")));

		// ---- AS SUSPENSOES ----
		var daJanela = calibrado ? _pausas.FindAll(p => EmSw(p.TPedido) >= janelaDe && EmSw(p.TPedido) < janelaAte) : _pausas;
		Linha($"SUSPENSOES DO PROCESSO VISTAS PELO OUVINTE: {daJanela.Count} na janela ({_pausas.Count} desde o ponto)"
			  + (calibrado ? "" : " -- sem relogio calibrado, a lista e a de toda a gravacao"));
		var grupos = new SortedDictionary<string, List<Pausa>>(StringComparer.Ordinal);
		foreach (Pausa p in daJanela)
		{
			string chave = p.Indice < 0
				? $"sem coleta dentro (motivo da suspensao {p.MotivoDaSuspensao})"
				: $"geracao {p.Geracao}{(p.Tipo == 1 ? " de FUNDO" : p.Tipo == 2 ? " durante uma de fundo" : "")}, {NomeDoMotivo(p.Motivo)}";
			if (!grupos.TryGetValue(chave, out List<Pausa>? l)) grupos[chave] = l = [];
			l.Add(p);
		}
		foreach ((string chave, List<Pausa> lista) in grupos)
		{
			double D(Func<Pausa, long> campo) { var l = new List<double>(lista.Count); foreach (Pausa p in lista) l.Add(campo(p) / 10_000.0); return Media(l); }
			Linha($"   {lista.Count,4} x {chave}: {D(p => p.TRetomou - p.TPedido):0.0} ms cada (suspender as threads {D(p => p.TSuspenso - p.TPedido):0.00},"
				  + $" ate a coleta comecar {D(p => p.TComeco > 0 ? p.TComeco - p.TSuspenso : 0):0.00}, a coleta {D(p => p.TFim > 0 && p.TComeco > 0 ? p.TFim - p.TComeco : 0):0.0},"
				  + $" retomar {D(p => p.TFim > 0 ? p.TRetomou - p.TFim : 0):0.00})");

			var quem = new Dictionary<int, int>();
			foreach (Pausa p in lista) quem[p.Thread] = quem.GetValueOrDefault(p.Thread) + 1;
			var pedidas = new List<string>();
			foreach ((int thread, int vezes) in quem)
				pedidas.Add($"{vezes} pela {(thread == _threadPrincipal ? "PRINCIPAL" : $"thread {thread} \"{NomeDoThread(thread)}\"")}");
			Linha("          pedidas: " + string.Join(", ", pedidas));

			// A MARCACAO, etapa por etapa: cada `GCMarkWithType` sai quando a etapa ACABA, entao a diferenca de
			// hora entre dois seguidos e o tempo da segunda; o que sobra ate o `GCEnd` e plano + realocacao + compactacao.
			var tempo = new SortedDictionary<int, double>();
			var bytes = new SortedDictionary<int, double>();
			double restoDaColeta = 0;
			int comColeta = 0;
			foreach (Pausa p in lista)
			{
				if (p.TComeco == 0 || p.TFim == 0) continue;
				comColeta++;
				long antes = p.TComeco;
				foreach ((int raiz, long b, long t) in p.Marcas)
				{
					tempo[raiz] = tempo.GetValueOrDefault(raiz) + (t - antes) / 10_000.0;
					bytes[raiz] = bytes.GetValueOrDefault(raiz) + b;
					antes = t;
				}
				restoDaColeta += (p.TFim - antes) / 10_000.0;
			}
			if (comColeta > 0)
			{
				var partes = new List<string>();
				foreach ((int raiz, double ms) in tempo) partes.Add($"{NomeDaRaiz(raiz)} {ms / comColeta:0.00} ms / {Tam(bytes[raiz] / comColeta)}");
				Linha("          a marcacao, por raiz (tempo / bytes promovidos, media por coleta): " + string.Join(" | ", partes));
				Linha($"          depois da marcacao (plano, realocacao, compactacao ou varredura): {restoDaColeta / comColeta:0.00} ms");
			}

			var comStats = lista.FindAll(p => p.Stats != null);
			if (comStats.Count > 0)
			{
				double S(int i) { var l = new List<double>(comStats.Count); foreach (Pausa p in comStats) l.Add(p.Stats![i]); return Media(l); }
				Linha($"          depois da coleta: g0 {Tam(S(0))} (promovido {Tam(S(1))}) | g1 {Tam(S(2))} (promovido {Tam(S(3))}) | g2 {Tam(S(4))} (promovido {Tam(S(5))})"
					  + $" | LOH {Tam(S(6))} | POH {Tam(S(8))}");
				Linha($"          finalizaveis recem-mortos por coleta: {S(11):0} objeto(s), {Tam(S(10))} | pinados {S(12):0} | sync blocks {S(13):0} | HANDLES {S(14):0}");
			}
			var comOrcamento = lista.FindAll(p => p.Orcamento0 >= 0);
			if (comOrcamento.Count > 0)
			{
				double O(Func<Pausa, double> campo) { var l = new List<double>(comOrcamento.Count); foreach (Pausa p in comOrcamento) l.Add(campo(p)); return Media(l); }
				var mecanismos = new SortedDictionary<long, int>();
				var razoes = new SortedDictionary<string, int>(StringComparer.Ordinal);
				foreach (Pausa p in comOrcamento)
				{
					mecanismos[p.Mecanismos] = mecanismos.GetValueOrDefault(p.Mecanismos) + 1;
					string r = $"{p.Razoes0:X}/{p.Razoes1:X}";
					razoes[r] = razoes.GetValueOrDefault(r) + 1;
				}
				Linha($"          orcamento da geracao 0 depois da coleta: {Tam(O(p => p.Orcamento0))} | pressao de memoria {O(p => p.PressaoDeMemoria):0}%"
					  + $" | mecanismos (1 concorrente, 2 compactou, 4 promoveu, 8 rebaixou, 16 cartoes em pacote): {string.Join(", ", new List<long>(mecanismos.Keys).ConvertAll(m => $"{m} x{mecanismos[m]}"))}"
					  + $" | razoes de condenacao (hexa, geracao/condicao): {string.Join(", ", new List<string>(razoes.Keys).ConvertAll(r => $"{r} x{razoes[r]}"))}");
			}
		}

		if (_nivel < 5) return;

		// ---- AS AMOSTRAS DE ALOCACAO ----
		int nTipos = _tipos.Count;
		var porThread = new Dictionary<int, double>();
		var porTipo = new double[nTipos];
		var tamanhoPorTipo = new double[nTipos];
		var vezesPorTipo = new int[nTipos];
		var porFaseETipo = new Dictionary<(int Fase, int Tipo), double>();
		var porFase = new double[_marcas];
		var porThreadETipo = new Dictionary<(int Thread, int Tipo), double>();
		double total = 0, grandes = 0, semFase = 0;
		int usadas = 0;
		foreach (Amostra a in _amostras)
		{
			long sw = calibrado ? EmSw(a.T) : 0;
			if (calibrado && (sw < janelaDe || sw >= janelaAte)) continue;
			usadas++;
			total += a.Bytes;
			if (a.Classe == 1) grandes += a.Bytes;
			porThread[a.Thread] = porThread.GetValueOrDefault(a.Thread) + a.Bytes;
			porTipo[a.Tipo] += a.Bytes;
			tamanhoPorTipo[a.Tipo] += a.Tamanho;
			vezesPorTipo[a.Tipo]++;
			porThreadETipo[(a.Thread, a.Tipo)] = porThreadETipo.GetValueOrDefault((a.Thread, a.Tipo)) + a.Bytes;
			if (a.Thread != _threadPrincipal) continue;
			if (!calibrado) { semFase += a.Bytes; continue; }

			// em que quadro e em que fase a amostra caiu
			int q = Array.BinarySearch(comecos, sw);
			if (q < 0) q = ~q - 1;
			if (q < 0 || q >= _n - 1) { semFase += a.Bytes; continue; }
			int fase = _marcas - 1;
			for (int i = 1; i < _marcas; i++)
				if (sw < _t[q * _marcas + i]) { fase = i - 1; break; }
			porFase[fase] += a.Bytes;
			porFaseETipo[(fase, a.Tipo)] = porFaseETipo.GetValueOrDefault((fase, a.Tipo)) + a.Bytes;
		}

		Linha($"AMOSTRAS DE ALOCACAO (uma a cada ~100 KB; o peso de cada uma sao os bytes alocados desde a anterior): {usadas} na janela somam {Tam(total / segundos)}/s"
			  + $" = {(doProcesso > 0 ? 100 * total / segundos / doProcesso : 0):0}% do que o processo alocou pela conta do runtime | objetos grandes (LOH): {Tam(grandes / segundos)}/s");

		Linha("POR THREAD:");
		var threads = new List<int>(porThread.Keys);
		threads.Sort((x, y) => porThread[y].CompareTo(porThread[x]));
		// O OUVINTE VERBOSO PESA NA CONTA QUE ELE MESMO MEDE: cada objeto finalizado e um evento, e cada evento e lixo
		// do despachante. Com centenas de milhares de finalizados por segundo ele enche a geracao 0 mais depressa
		// que o jogo -- os TIPOS e os FINALIZADOS desta rodada valem, a FREQUENCIA das pausas nao (use o nivel 0 ou 4).
		if (porThread.TryGetValue((int)_threadDoOuvinte, out double doOuvinte) && total > 0 && doOuvinte > 0.1 * total)
			Linha($"   ATENCAO: o despachante deste ouvinte alocou {100 * doOuvinte / total:0}% do total -- a frequencia das pausas DESTA rodada e a dele, nao a do jogo");
		foreach (int th in threads)
		{
			string nome = th == _threadPrincipal ? "a PRINCIPAL (o jogo)" : th == _threadDoOuvinte ? "o despachante de eventos (o custo DESTE ouvinte)" : $"\"{_nomeDoThread.GetValueOrDefault(th, "?")}\"";
			Linha($"   thread {th,6} {nome}: {Tam(porThread[th] / segundos)}/s ({(total > 0 ? 100 * porThread[th] / total : 0):0}%)");
			if (th == _threadPrincipal) continue;
			var doThread = new List<(int Tipo, double Bytes)>();
			foreach (((int Thread, int Tipo) chave, double bts) in porThreadETipo) if (chave.Thread == th) doThread.Add((chave.Tipo, bts));
			doThread.Sort((x, y) => y.Bytes.CompareTo(x.Bytes));
			for (int i = 0; i < Math.Min(6, doThread.Count); i++)
				Linha($"            {Tam(doThread[i].Bytes / segundos),12}/s  {_tipos[doThread[i].Tipo]}");
		}

		Linha("OS TIPOS QUE MAIS PESAM NO PROCESSO INTEIRO (bytes/s, % do total, tamanho medio do objeto amostrado):");
		var ordem = new List<int>();
		for (int i = 0; i < nTipos; i++) if (porTipo[i] > 0) ordem.Add(i);
		ordem.Sort((x, y) => porTipo[y].CompareTo(porTipo[x]));
		for (int i = 0; i < Math.Min(40, ordem.Count); i++)
		{
			int tp = ordem[i];
			Linha($"   {Tam(porTipo[tp] / segundos),12}/s {(total > 0 ? 100 * porTipo[tp] / total : 0),5:0.0}%  {Tam(tamanhoPorTipo[tp] / vezesPorTipo[tp]),10}  {_tipos[tp]}{DonoDaGerada(_tipos[tp])}");
		}

		Linha($"NA THREAD PRINCIPAL, POR FASE DO QUADRO (pela hora de cada amostra; {Tam(semFase / segundos)}/s ficaram sem fase):");
		for (int f = 0; f < _marcas; f++)
		{
			if (porFase[f] <= 0) continue;
			Linha($"   --- {_fases[f]}: {Tam(porFase[f] / segundos)}/s");
			var daFase = new List<(int Tipo, double Bytes)>();
			foreach (((int Fase, int Tipo) chave, double bts) in porFaseETipo) if (chave.Fase == f) daFase.Add((chave.Tipo, bts));
			daFase.Sort((x, y) => y.Bytes.CompareTo(x.Bytes));
			// (objetos por segundo = bytes / tamanho medio do objeto amostrado: exato pra tipo de tamanho fixo, estimativa pros arrays)
			for (int i = 0; i < Math.Min(25, daFase.Count); i++)
			{
				int tp = daFase[i].Tipo;
				double tamanho = tamanhoPorTipo[tp] / vezesPorTipo[tp];
				Linha($"       {Tam(daFase[i].Bytes / segundos),12}/s {100 * daFase[i].Bytes / porFase[f],5:0.0}%  {(tamanho > 0 ? daFase[i].Bytes / segundos / tamanho : 0),9:0}/s  {_tipos[tp]}{DonoDaGerada(_tipos[tp])}");
			}
		}

		// ---- OS FINALIZADOS ----
		if (_finalizados.Count > 0)
		{
			double desdeOPonto = Ms(_fim - _comeco) / 1000.0;
			long soma = 0;
			var lista = new List<(string Tipo, int Vezes)>();
			foreach ((long id, int vezes) in _finalizados)
			{
				soma += vezes;
				string nome;
				try { nome = Type.GetTypeFromHandle(RuntimeTypeHandle.FromIntPtr((IntPtr)id))?.FullName ?? $"tipo 0x{id:X}"; }
				catch (Exception) { nome = $"tipo 0x{id:X}"; }
				lista.Add((nome, vezes));
			}
			lista.Sort((x, y) => y.Vezes.CompareTo(x.Vezes));
			Linha($"OBJETOS FINALIZADOS desde o ponto ({desdeOPonto:0.0} s): {soma} = {soma / desdeOPonto:0} por segundo. Os tipos:");
			for (int i = 0; i < Math.Min(20, lista.Count); i++)
				Linha($"   {lista[i].Vezes / desdeOPonto,9:0.0}/s  {lista[i].Tipo}");
		}
	}
}
