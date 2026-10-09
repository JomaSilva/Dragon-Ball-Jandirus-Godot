using Godot;

namespace Jandirus.Client;

/// <summary>
/// OS SONS DE EFEITO, CONTADOS E COBRADOS -- a rodada `--diagestouro --sons`, e a conta deles no gravador sozinho
/// (`--diagestouro --passivo &lt;s&gt;`, ao lado do robo de outra cena).
///
/// ============================ O QUE SE MEDE ============================
/// Todo som que nao e musica passa pela porta `SonsPresos`, e a porta tem um cronometro e uma espia: cada vez que
/// ela vai ao `ResourceLoader` buscar um arquivo, a espia recebe o arquivo, quanto a thread principal ficou la dentro
/// e -- perguntado ao proprio Godot ANTES da busca (`ResourceLoader.HasCached`) -- se o arquivo estava no cache dele
/// ou teve de ser LIDO DO DISCO ali, no meio do quadro. Com `--verbose` na linha, o `EscutaDeCarga` da a segunda
/// palavra, a do motor: `Completed load ... at thread -1`.
///
///   * NO GRAVADOR SOZINHO (`--trailer duelo --diagestouro --passivo 100`) sai a conta de uma cena de verdade: quantos
///     toques, quantas idas ao `ResourceLoader`, quantas leituras de disco, o custo de cada uma e o quadro em que caiu;
///   * NA RODADA `--sons` sai a regua, de tres pernas -- ver <see cref="AndarNosSons"/>.
///
/// ============================ O QUE ELA MEDIU (2026-10-09) ============================
/// O DEFEITO: os tres funis de som (`AudioDirector.Efeito`, `EfeitoNoLugar`, `Ambiente`) pediam o arquivo ao
/// `ResourceLoader` a cada toque e ninguem o segurava -- so estavam presos, pelo `Aquecimento`, a pasta dos golpes, os
/// sons de beat, os estalos e os trovoes. O resto saia do cache quando o tocador morria e o coletor do .NET passava,
/// e voltava do disco, pela thread principal, no toque seguinte. A conta inteira esta no cabecalho do `SonsPresos`.
///
/// NO JOGO, sem coleta forcada (o gravador ao lado de duas cenas do diretor do trailer; "na cena" = fora a chegada
/// na zona, que le o ambiente dela):
///
///                                               toques    leituras de disco na cena     o coletor passou
///     ANTES   duelo, anfitriao (4 corridas) ..  183-249   4 a 8  (2-4 delas RELEITURAS)  6 a 8 vezes
///             duelo, cliente puro ............  206       3      (1 releitura)           3 vezes
///             passeio, 8 voos (2 corridas) ...  50-55     12 a 13 (9-10 releituras)      5 vezes
///     DEPOIS  duelo, anfitriao (2 corridas) ..  203-234   0                              7 a 8 vezes
///             duelo, cliente puro ............  238       0                              1 vez
///             passeio ........................  72        0                              5 vezes
///
///     quem era relido: o Zanzoken (`teleport.ogg`), o rasgo da investida e da corrida (`chainswoop.ogg`), a decolagem
///     (`buku.ogg`, em 6 de 8 voos) e o pouso (`buku_land.ogg`, em 5 de 8)
///     cada leitura: 1,0 a 1,7 ms dentro da porta (cem leituras; uma delas, 6,3 -- o quadro dela gastou 12,5 ms de
///     trabalho, contra os 3 a 7 dos outros quadros com leitura)
///
/// (As corridas de "antes" sao as do binario sem o conserto e, no de hoje, as com `--semsegurarossons` na linha.)
///
/// COM `--verbose` CADA LEITURA CUSTA O DOBRO: 2,5 a 4,4 ms na mesma cena -- o motor escreve no log a cada carga. A
/// conta do motor serve pra CONFERIR quem foi lido (os seis `.ogg` que ele listou sao as seis leituras que a porta
/// contou), e nao pra dizer quanto custou. Conferido tambem nos sons de BEAT (`--trailer formas`, as estreias do
/// `ssj1` e do `ssj2`, com `--semadiantaracena --semsegurarossons`): 18 leituras de 1,0 a 1,4 ms sem `--verbose`, 20
/// de 2,4 a 4,2 com ele -- os mesmos arquivos.
///
/// NA REGUA (`--sons`), com uma coleta forcada entre os dois toques: os oito sons de gesto lidos do disco no primeiro
/// toque (9,8 a 10,7 ms pelos oito), os dez FORA do cache depois da coleta, os dez lidos de novo no segundo toque (12,4
/// a 14,1 ms) -- no binario de antes e na rodada `--semsegurarossons`. Com o conserto: 0,12 ms pelos oito no primeiro
/// toque, os dez na memoria, 0,00 no segundo.
///
/// O QUE A CONTA DO MOTOR MOSTROU E NAO E SOM (o duelo com `--verbose`): o Godot rele do disco o SCRIPT de um node C#
/// quando nasce um e nao ha outro vivo -- `ProjetilDesenhado.cs`, `CargaDeRaioVisual.cs`, `ChoqueDeKi.cs` e
/// `PoeiraDeEstrago.cs`, dezoito vezes em 75 s de luta, pela thread principal -- e le o `Borrao.gdshader` no primeiro
/// borrao do processo, a primeira investida: o quadro dela custou 33 a 42 ms de trabalho em quatro corridas. Nao
/// foram mexidos aqui.
///
/// (OS SCRIPTS FORAM FECHADOS DEPOIS: o aquecimento segura o de cada classe de efeito. A conta deles -- 0,5 a 1,3 ms
/// por carga sem `--verbose`, 16 a 26 cargas por duelo -- e a regua estao no arquivo `RoboDoPrimeiroEstouro.Scripts.cs`.)
///
/// (E O BORRAO TAMBEM: o shader dele vem da fila de carga do lobby e e ensaiado la. A conta -- 31 a 40 ms de trabalho no
/// quadro da primeira investida, 6 depois -- e a regua estao no bloco "E O PRIMEIRO BORRAO" do cabecalho do
/// `RoboDoPrimeiroEstouro` e no arquivo `RoboDoPrimeiroEstouro.Borrao.cs`.)
/// </summary>
public partial class RoboDoPrimeiroEstouro
{
	/// <summary>`--sons`: a rodada dos sons de efeito. Ver <see cref="AndarNosSons"/>.</summary>
	private readonly bool _sons = Tem("--sons");

	/// <summary>
	/// `--sons --semsegurarossons`: A RODADA DE INJECAO dos sons -- o processo entra no mundo com o defeito
	/// `SonsPresos.SemSegurarDeTeste` (ligado pelo `Boot`, antes de o aquecimento nascer). As tres pernas da regua tem
	/// que REPROVAR, e e desta rodada que sai o numero de antes, no mesmo binario.
	/// </summary>
	private readonly bool _semSegurarOsSons = Tem("--semsegurarossons");

	// =====================================================================
	// AS DUAS ESPIAS -- o que tocou, e o que a porta foi buscar
	// =====================================================================
	/// <summary>Cada vez que a porta dos sons foi ao `ResourceLoader`: o quadro, o arquivo, os ms la dentro, e se ele veio do disco.</summary>
	private readonly List<(int Quadro, string Arquivo, double Ms, bool DoDisco)> _buscasDeSom = [];

	/// <summary>Cada efeito POSICIONADO que tocou (`AudioDirector.Espiao`): o quadro e o arquivo.</summary>
	private readonly List<(int Quadro, string Arquivo)> _toquesDeSom = [];

	private bool _ouvindoOsSons;

	/// <summary>Liga as duas espias -- so no gravador sozinho e na rodada dos sons. Chamado no `_Ready`, no lobby.</summary>
	private void OuvirOsSons()
	{
		if (_ouvindoOsSons || (_passivo <= 0 && !_sons)) return;
		_ouvindoOsSons = true;
		SonsPresos.EspiaoDeBusca += AoBuscarSom;
		AudioDirector.Espiao += AoTocarSom;
		// (com `--verbose` na linha, cada arquivo que a thread principal le ganha o quadro dele -- e nao so os de som)
		LigarAEscutaDeCarga();
	}

	private void SoltarOsSons()
	{
		if (!_ouvindoOsSons) return;
		_ouvindoOsSons = false;
		SonsPresos.EspiaoDeBusca -= AoBuscarSom;
		AudioDirector.Espiao -= AoTocarSom;
	}

	private void AoBuscarSom(string arquivo, double ms, bool doDisco)
	{
		_buscasDeSom.Add((_quadros.Count, arquivo, ms, doDisco));
		// NO GRAVADOR SOZINHO a leitura vira rotulo do quadro: e por ele que o quadro entra na lista, com o custo.
		if (_passivo > 0 && doDisco) Marcar($"SOM LIDO DO DISCO pela thread principal: `{arquivo.GetFile()}`, {ms:0.0} ms");
	}

	/// <summary>
	/// Quantas coletas do .NET o processo ja tinha feito no primeiro e no ultimo toque posicionado: de qualquer
	/// geracao, de geracao 1 ou mais, e de geracao 2. A diferenca e o que o coletor passou DURANTE os sons -- e cada
	/// passada solta o som que so um involucro C# segurava.
	/// </summary>
	private (int Todas, int Gen1, int Gen2) _coletasNoPrimeiroToque, _coletasNoUltimoToque;

	private void AoTocarSom(string arquivo, float volume)
	{
		(int, int, int) coletas = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
		if (_toquesDeSom.Count == 0) _coletasNoPrimeiroToque = coletas;
		_coletasNoUltimoToque = coletas;
		_toquesDeSom.Add((_quadros.Count, arquivo));
	}

	/// <summary>O instante de um quadro, em segundos desde o quadro `de`. (O quadro corrente ainda nao esta na lista: vale o ultimo.)</summary>
	private double SegundosDesde(int de, int quadro) =>
		_quadros.Count == 0 ? 0 : ((double)_quadros[Math.Min(quadro, _quadros.Count - 1)].Comeco - _quadros[Math.Min(de, _quadros.Count - 1)].Comeco) / 1e6;

	/// <summary>
	/// A CONTA DOS SONS do quadro `de` em diante: os toques, as idas da porta ao `ResourceLoader`, quantas delas leram o
	/// arquivo do disco e quanto custaram -- no total, por arquivo, e uma a uma com o quadro em que cairam.
	///
	/// A TAXA E POR MINUTO DE SOM, e nao de gravacao: do primeiro toque posicionado ao ultimo. No gravador sozinho a
	/// cena do outro robo gasta os primeiros segundos viajando e armando o palco, calada.
	/// </summary>
	private List<string> ContaDosSons(int de)
	{
		var linhas = new List<string>();
		var toques = _toquesDeSom.FindAll(t => t.Quadro >= de);
		var buscas = _buscasDeSom.FindAll(b => b.Quadro >= de);
		var doDisco = buscas.FindAll(b => b.DoDisco);
		var daMemoria = buscas.FindAll(b => !b.DoDisco);

		// PRIMEIRA LEITURA OU RELEITURA? Releitura e a do arquivo que a porta JA tinha buscado neste processo: ele tocou,
		// saiu da memoria e voltou do disco. Primeira leitura e a do som que ninguem trouxe antes da hora.
		var releitura = new List<bool>();   // alinhada com `doDisco`
		var relidas = new Dictionary<string, int>(StringComparer.Ordinal);
		var vistos = new HashSet<string>(StringComparer.Ordinal);
		foreach ((int quadro, string arquivo, _, bool disco) in _buscasDeSom)
		{
			if (disco && quadro >= de)
			{
				releitura.Add(vistos.Contains(arquivo));
				if (releitura[^1]) relidas[arquivo] = relidas.GetValueOrDefault(arquivo) + 1;
			}
			vistos.Add(arquivo);
		}
		int releituras = releitura.Count(r => r);

		if (toques.Count == 0 && buscas.Count == 0)
		{
			linhas.Add("OS SONS DE EFEITO: nenhum tocou.");
			return linhas;
		}

		double janela = toques.Count > 1 ? SegundosDesde(toques[0].Quadro, toques[^1].Quadro) : 0;
		string taxa = janela > 1 ? $"{doDisco.Count * 60.0 / janela:0.0} por minuto" : "sem janela pra taxa";
		linhas.Add($"OS SONS DE EFEITO: {toques.Count} toque(s) posicionado(s) de {toques.Select(t => t.Arquivo).Distinct().Count()} arquivo(s)"
				   + $" em {janela:0.0} s (do primeiro ao ultimo) | a porta foi {buscas.Count} vez(es) ao `ResourceLoader`"
				   + $" ({SonsPresos.EsperaDeTeste:0.0} ms la dentro desde que o processo abriu) | a porta segura {SonsPresos.Quantos} som(ns)");
		linhas.Add($"   -- achou o arquivo NA MEMORIA {daMemoria.Count} vez(es): {daMemoria.Sum(b => b.Ms):0.00} ms ao todo"
				   + (daMemoria.Count > 0 ? $", {daMemoria.Average(b => b.Ms):0.000} cada, pior {daMemoria.Max(b => b.Ms):0.00}" : ""));
		linhas.Add($"   -- LEU DO DISCO, na thread principal, {doDisco.Count} vez(es) ({taxa}) -- {doDisco.Count - releituras} PRIMEIRA(S) leitura(s) do processo"
				   + $" e {releituras} RELEITURA(S) de som que ja tinha tocado: {doDisco.Sum(b => b.Ms):0.0} ms ao todo"
				   + (doDisco.Count > 0
					   ? $", {doDisco.Average(b => b.Ms):0.0} cada, de {doDisco.Min(b => b.Ms):0.0} a {doDisco.Max(b => b.Ms):0.0}"
						 + $" (a pior: `{doDisco.MaxBy(b => b.Ms).Arquivo.GetFile()}`)"
					   : ""));

		linhas.Add($"   -- o coletor do .NET passou {_coletasNoUltimoToque.Todas - _coletasNoPrimeiroToque.Todas} vez(es) entre o primeiro toque e o ultimo"
				   + $" ({_coletasNoUltimoToque.Gen1 - _coletasNoPrimeiroToque.Gen1} de geracao 1 ou mais, {_coletasNoUltimoToque.Gen2 - _coletasNoPrimeiroToque.Gen2} de geracao 2)");

		linhas.Add("   POR ARQUIVO -- toques posicionados, idas ao `ResourceLoader`, leituras de disco (e quanto custaram):");
		foreach (string arquivo in buscas.Select(b => b.Arquivo).Concat(toques.Select(t => t.Arquivo)).Distinct().OrderBy(a => a, StringComparer.Ordinal))
		{
			var lidas = doDisco.FindAll(b => b.Arquivo == arquivo);
			linhas.Add($"      {arquivo.GetFile(),-34} {toques.Count(t => t.Arquivo == arquivo),4} toque(s) {buscas.Count(b => b.Arquivo == arquivo),4} ida(s) {lidas.Count,3} do disco"
					   + (lidas.Count > 0 ? $"  ({lidas.Min(b => b.Ms):0.0} a {lidas.Max(b => b.Ms):0.0} ms; {relidas.GetValueOrDefault(arquivo)} releitura(s))" : ""));
		}

		if (doDisco.Count > 0)
		{
			linhas.Add("   CADA LEITURA DE DISCO, no quadro em que caiu -- o que a porta esperou, e o trabalho do quadro inteiro:");
			for (int i = 0; i < doDisco.Count; i++)
			{
				(int quadro, string arquivo, double ms, _) = doDisco[i];
				linhas.Add($"      t={SegundosDesde(de, quadro),7:0.000}s  {arquivo.GetFile(),-34} {ms,5:0.0} ms na porta  {(releitura[i] ? "RELEITURA " : "1a leitura")}"
						   + (quadro + 1 < _quadros.Count
							   ? $" | o quadro: trabalho {Trabalho(quadro):0.0} ms (script {ScriptMs(quadro) - ColetorNoScript(quadro):0.0}), inteiro {TotalMs(quadro):0.0}"
							   : ""));
			}
		}
		return linhas;
	}

	/// <summary>
	/// O QUE A THREAD PRINCIPAL LEU DO DISCO do quadro `de` em diante, segundo o proprio Godot -- so com `--verbose`
	/// (ver `EscutaDeCarga`). E a conta que nao depende da porta: arquivo lido por fora dela tambem aparece aqui.
	/// </summary>
	private List<string> LidosPelaThreadPrincipal(int de)
	{
		var linhas = new List<string>();
		if (!SaidaVerbosa)
		{
			linhas.Add("(sem `--verbose` na linha: a conta do motor -- todo arquivo que a thread principal leu -- nao foi feita)");
			return linhas;
		}

		int arquivos = 0;
		var porTipo = new SortedDictionary<string, int>(StringComparer.Ordinal);
		var quadros = new List<string>();
		foreach (int quadro in _lidos.Keys.Where(q => q >= de).OrderBy(q => q))
		{
			string[] lidos = _lidos[quadro];
			arquivos += lidos.Length;
			foreach (string l in lidos)
			{
				string tipo = l.GetExtension();
				porTipo[tipo] = porTipo.GetValueOrDefault(tipo) + 1;
			}
			quadros.Add($"      t={SegundosDesde(de, quadro),7:0.000}s  ({lidos.Length}) {string.Join(", ", lidos.Take(6).Select(l => l.GetFile()))}{(lidos.Length > 6 ? ", ..." : "")}"
						+ (quadro + 1 < _quadros.Count ? $" | o quadro: trabalho {Trabalho(quadro):0.0} ms" : ""));
		}
		linhas.Add($"O QUE A THREAD PRINCIPAL LEU DO DISCO (a palavra do motor, `Completed load ... at thread -1`): {arquivos} arquivo(s) em {quadros.Count} quadro(s)"
				   + (porTipo.Count > 0 ? " -- " + string.Join(", ", porTipo.Select(p => $"{p.Value} .{p.Key}")) : ""));
		linhas.AddRange(quadros);
		return linhas;
	}

	/// <summary>O fecho do gravador sozinho: a conta dos sons da cena que ele assistiu, desde o corpo no mundo.</summary>
	private void ImprimirOsSonsDoPassivo()
	{
		int de = Math.Max(1, _entrouNoMundo);
		if (SonsPresos.SemSegurarDeTeste) GD.Print("[estouro] DEFEITO INJETADO a gravacao inteira: os sons de efeito sem dono (`--semsegurarossons`)");
		foreach (string linha in ContaDosSons(de)) GD.Print("[estouro] " + linha);
		foreach (string linha in LidosPelaThreadPrincipal(de)) GD.Print("[estouro] " + linha);
	}

	// =====================================================================
	// A RODADA `--sons`
	// =====================================================================
	/// <summary>
	/// Quanto a porta pode esperar pelo `ResourceLoader` num toque, em ms. Com o arquivo na memoria ela gasta
	/// centesimos de ms; lido do disco na hora, um som destes custa 1,2 a 1,7 (medido em 2026-10-09; os numeros de cada
	/// corrida saem no relatorio dela, som a som).
	///
	/// E PERTO DEMAIS DO TETO pra o cronometro ser a unica palavra: as duas pernas de toque cobram TAMBEM o fato -- o
	/// arquivo estava no cache do Godot antes da busca? (`SonsPresos.EspiaoDeBusca`)
	/// </summary>
	private const double EsperaPorSom = 1.0;

	private const string DefeitoDosSons = "os sons de efeito sem dono: lidos a cada toque, e sem vir do lobby";

	/// <summary>
	/// UM ARQUIVO QUE NENHUMA LINHA DO JOGO CITA -- o som novo de amanha, que ninguem pos em lista nenhuma. E ele que
	/// separa a porta da fila do lobby: o que a fila traz o aquecimento tambem segura, e sem um som de fora dela a
	/// rodada passaria igual com a porta soltando tudo.
	/// </summary>
	private const string SomDeFora = "res://Assets/Sounds/Effects/landshort.ogg";

	/// <summary>O ambiente da zona do berco desta bancada (`--raca Human`: a Terra), e o de outro planeta.</summary>
	private static readonly string AmbienteDaZona = Trilha.AmbienteDe("Earth") ?? "", AmbienteDeFora = Trilha.AmbienteDe("Namek") ?? "";

	private enum PapelDoSom { Gesto, DeFora, Ambiente }

	private sealed class SomDaRodada
	{
		public string Rotulo = "", Caminho = "";
		public PapelDoSom Papel;

		/// <summary>O que a porta esperou no toque de antes e no de depois da coleta, em ms (negativo = nao tocou).</summary>
		public double Primeiro = -1, Segundo = -1;
		public bool PrimeiroDoDisco, SegundoDoDisco;

		/// <summary>Depois da coleta forcada, o Godot ainda tem o arquivo no cache?</summary>
		public bool NaMemoria;
	}

	/// <summary>
	/// OS SONS DA RODADA. Os oito primeiros sao os que os GESTOS de uma luta e de um passeio tocam e que nao sao a pasta
	/// dos golpes nem som de beat de cinematica -- a lista e DESTA bancada, escrita gesto a gesto: a pergunta e "o que o
	/// jogo toca esta na memoria?", e a resposta nao pode sair da lista de quem escolheu o que por la.
	/// </summary>
	private readonly List<SomDaRodada> _sonsDaRodada =
	[
		new() { Rotulo = "o rasgo de todo comeco de corrida e de toda investida", Caminho = Trilha.Dash, Papel = PapelDoSom.Gesto },
		new() { Rotulo = "o Zanzoken", Caminho = Trilha.Teleporte, Papel = PapelDoSom.Gesto },
		new() { Rotulo = "a decolagem", Caminho = Trilha.Decolagem, Papel = PapelDoSom.Gesto },
		new() { Rotulo = "o pouso", Caminho = Trilha.Pouso, Papel = PapelDoSom.Gesto },
		new() { Rotulo = "o Kiai", Caminho = Trilha.Kiai, Papel = PapelDoSom.Gesto },
		new() { Rotulo = "a lamina de ar do Kiai", Caminho = Trilha.LaminaDeAr, Papel = PapelDoSom.Gesto },
		new() { Rotulo = "o Kaio-ken acendendo", Caminho = Trilha.Kaioken, Papel = PapelDoSom.Gesto },
		new() { Rotulo = "o membro arrancado", Caminho = Trilha.Decepou, Papel = PapelDoSom.Gesto },
		new() { Rotulo = "um som que nenhuma linha do jogo cita", Caminho = SomDeFora, Papel = PapelDoSom.DeFora },
		new() { Rotulo = "o ambiente de outro planeta", Caminho = AmbienteDeFora, Papel = PapelDoSom.Ambiente },
	];

	private enum FaseDosSons { Primeiro, Calar, Segundo, Fim }

	private FaseDosSons _faseDosSons = FaseDosSons.Primeiro;
	private Node2D? _palcoDosSons;
	private int _somDaVez;
	private double _semTocador;

	/// <summary>
	/// CADA SOM TOCA DUAS VEZES pela porta de producao (`AudioDirector.EfeitoNoLugar`; o ambiente, `Ambiente`), com uma
	/// coleta forcada do .NET no meio -- um toque por gesto, cada um num quadro so dele:
	///
	///   1. O PRIMEIRO TOQUE de cada som. Regua, nos oito de gesto: a porta nao esperou pelo arquivo -- eles ja vieram
	///      do lobby, numa thread. (O som de fora e o ambiente do outro planeta ninguem trouxe: o primeiro toque deles
	///      LE o disco, e sai como numero -- e o que custa uma primeira leitura.)
	///   2. O ambiente volta ao da zona, os tocadores morrem, e o COLETOR DO .NET PASSA. Regua, sem cronometro: os dez
	///      arquivos continuam no cache do Godot (`ResourceLoader.HasCached`). E a perna do DONO -- som que ninguem
	///      segura sai do cache quando o tocador morre e o coletor leva o involucro C# dele.
	///   3. O SEGUNDO TOQUE de cada som. Regua, nos dez: a porta nao esperou -- nenhum voltou do disco.
	///
	/// O PALCO E UM NODE SO DA BANCADA, no lugar do corpo: os tocadores que a porta pendura nele sao os unicos filhos, e
	/// contar filhos diz quando o ultimo som acabou sem a bancada saber a duracao de arquivo nenhum.
	/// </summary>
	private void AndarNosSons(World mundo)
	{
		if (AudioDirector.Instance is not { } audio)
		{
			Conferir(false, "ha maquina de som pra rodada dos sons");
			Fechar();
			return;
		}
		if (_palcoDosSons == null)
		{
			_palcoDosSons = new Node2D { Name = "PalcoDosSons", Position = mundo.PosicaoLocal ?? Vector2.Zero };
			mundo.AddChild(_palcoDosSons);
		}

		switch (_faseDosSons)
		{
			case FaseDosSons.Primeiro:
				if (_t < 1.2) return;
				_t = 0;
				if (_somDaVez < _sonsDaRodada.Count) { TocarNaRodada(audio, _sonsDaRodada[_somDaVez++], primeiro: true); return; }

				// O AMBIENTE VOLTA AO DA ZONA: sai do tocador o do outro planeta, e dali em diante so um dono o segura.
				audio.Ambiente(AmbienteDaZona);
				Marcar("o ambiente volta ao da zona");
				_somDaVez = 0;
				_semTocador = 0;
				_faseDosSons = FaseDosSons.Calar;
				return;

			case FaseDosSons.Calar:
				// OS TOCADORES TEM QUE MORRER ANTES DA COLETA: enquanto um toca, e ELE quem segura o arquivo.
				if (_palcoDosSons.GetChildCount() > 0)
				{
					_semTocador = 0;
					if (_t > 20) { Conferir(false, $"os tocadores dos sons acabaram em 20 s (sobram {_palcoDosSons.GetChildCount()})"); Fechar(); }
					return;
				}
				if ((_semTocador += GetProcessDeltaTime()) < 1.0) return;

				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
				Marcar("coleta forcada do .NET (os donos dos sons)");
				foreach (SomDaRodada som in _sonsDaRodada) som.NaMemoria = ResourceLoader.HasCached(som.Caminho);
				_t = 0;
				_faseDosSons = FaseDosSons.Segundo;
				return;

			case FaseDosSons.Segundo:
				if (_t < 0.8) return;
				_t = 0;
				if (_somDaVez < _sonsDaRodada.Count) { TocarNaRodada(audio, _sonsDaRodada[_somDaVez++], primeiro: false); return; }
				audio.Ambiente(AmbienteDaZona);
				_faseDosSons = FaseDosSons.Fim;
				return;

			default:
				if (_t >= 1.0) Fechar();
				return;
		}
	}

	/// <summary>Um toque da rodada, pela porta de producao: o que a porta esperou por ele, e se o arquivo veio do disco.</summary>
	private void TocarNaRodada(AudioDirector audio, SomDaRodada som, bool primeiro)
	{
		int buscasAntes = _buscasDeSom.Count;
		double esperaAntes = SonsPresos.EsperaDeTeste;
		if (som.Papel == PapelDoSom.Ambiente) audio.Ambiente(som.Caminho);
		else AudioDirector.EfeitoNoLugar(_palcoDosSons!, som.Caminho, 0.5f);
		double espera = SonsPresos.EsperaDeTeste - esperaAntes;

		bool doDisco = false;
		for (int i = buscasAntes; i < _buscasDeSom.Count; i++) doDisco |= _buscasDeSom[i].DoDisco;

		if (primeiro) { som.Primeiro = espera; som.PrimeiroDoDisco = doDisco; }
		else { som.Segundo = espera; som.SegundoDoDisco = doDisco; }
		Evento($"SOM, {(primeiro ? "o PRIMEIRO toque" : "o toque DEPOIS da coleta")}: {som.Rotulo} (`{som.Caminho.GetFile()}`) -- a porta esperou {espera:0.00} ms"
			   + (doDisco ? ", com o arquivo LIDO DO DISCO" : ""), Papel.Nota);
	}

	/// <summary>Uma perna da regua dos sons: cobrada no jogo, e REPROVADA na rodada de injecao.</summary>
	private void CobrarOsSons(bool ok, string oque, string medida)
	{
		if (_semSegurarOsSons) Conferir(!ok, $"(defeito injetado: {DefeitoDosSons}) a mesma regua REPROVA: {oque} ({medida})");
		else Conferir(ok, $"{oque} ({medida})");
	}

	/// <summary>O FECHO DA RODADA DOS SONS: as tres pernas, a porta unica, e a tabela som a som.</summary>
	private void RelatarOsSons()
	{
		bool tocaram = _sonsDaRodada.TrueForAll(s => s.Primeiro >= 0 && s.Segundo >= 0);
		Conferir(tocaram, $"a rodada tocou os {_sonsDaRodada.Count} sons duas vezes, antes e depois da coleta");
		if (!tocaram) return;

		// ---- 1. A FILA DO LOBBY: o primeiro toque dos sons de gesto ----
		List<SomDaRodada> gestos = _sonsDaRodada.FindAll(s => s.Papel == PapelDoSom.Gesto);
		SomDaRodada pior = gestos.MaxBy(s => s.Primeiro)!;
		string oque = $"os {gestos.Count} sons de gesto tocam pela PRIMEIRA vez sem a thread principal esperar pelo arquivo (o rasgo da corrida, o Zanzoken, a decolagem e o pouso, o Kiai e a lamina dele, o Kaio-ken, o membro arrancado)";
		string medida = $"a porta esperou {gestos.Sum(s => s.Primeiro):0.00} ms pelos {gestos.Count}; o pior, `{pior.Caminho.GetFile()}`, {pior.Primeiro:0.00} ms contra {EsperaPorSom:0.0};"
						+ $" {gestos.Count(s => s.PrimeiroDoDisco)} lido(s) do disco";
		// (sem aquecimento nenhum nao ha fila: nessa rodada o primeiro toque sai como numero)
		if (Tem("--semaquecimento")) Nota($"{oque}: {medida} (nesta rodada nao ha aquecimento)");
		else CobrarOsSons(pior.Primeiro <= EsperaPorSom && !gestos.Exists(s => s.PrimeiroDoDisco), oque, medida);

		// ---- 2. OS DONOS: depois da coleta, sem cronometro ----
		List<string> fora = _sonsDaRodada.FindAll(s => !s.NaMemoria).ConvertAll(s => s.Caminho.GetFile());
		CobrarOsSons(fora.Count == 0,
					 $"com os tocadores mortos e o coletor do .NET passado, os {_sonsDaRodada.Count} sons que a rodada tocou continuam na memoria (os de gesto, o que nenhuma linha do jogo cita e o ambiente do outro planeta)",
					 fora.Count == 0 ? $"os {_sonsDaRodada.Count} la" : $"{fora.Count} de {_sonsDaRodada.Count} fora: {string.Join(", ", fora)}");

		// ---- 3. A PORTA: o segundo toque de todos ----
		pior = _sonsDaRodada.MaxBy(s => s.Segundo)!;
		CobrarOsSons(pior.Segundo <= EsperaPorSom && !_sonsDaRodada.Exists(s => s.SegundoDoDisco),
					 $"tocados de novo depois da coleta, nenhum dos {_sonsDaRodada.Count} sons volta do disco",
					 $"a porta esperou {_sonsDaRodada.Sum(s => s.Segundo):0.00} ms pelos {_sonsDaRodada.Count}; o pior, `{pior.Caminho.GetFile()}`, {pior.Segundo:0.00} ms contra {EsperaPorSom:0.0};"
					 + $" {_sonsDaRodada.Count(s => s.SegundoDoDisco)} lido(s) do disco");

		ConferirAPortaDosSons();

		Nota("OS SONS, UM A UM -- o que a porta esperou no primeiro toque, se o arquivo ficou na memoria depois da coleta, e o que ela esperou no segundo toque:");
		foreach (SomDaRodada som in _sonsDaRodada)
			_linhas.Add($"           {som.Caminho.GetFile(),-22} 1o toque {som.Primeiro,5:0.00} ms{(som.PrimeiroDoDisco ? " (DISCO)  " : " (memoria)")}"
						+ $" | depois da coleta: {(som.NaMemoria ? "na memoria" : "FORA      ")}"
						+ $" | 2o toque {som.Segundo,5:0.00} ms{(som.SegundoDoDisco ? " (DISCO)  " : " (memoria)")}   {som.Rotulo}");

		foreach (string linha in ContaDosSons(Math.Max(1, _baseDe))) Nota(linha);
		foreach (string linha in LidosPelaThreadPrincipal(Math.Max(1, _baseAte))) Nota(linha);
	}

	/// <summary>
	/// A PORTA E A UNICA? Le os fontes do cliente atras de uma carga de `AudioStream` por fora da `SonsPresos` -- a
	/// mesma conta que a `--diaginvolucro` faz pras folhas. Um quarto funil de som (o terceiro foi o laco de carga do
	/// `CargaVisual`, montado na mao) nasceria lendo o arquivo a cada toque, e nenhuma perna de cima o veria: elas so
	/// tocam pela porta.
	///
	/// A MAQUINA DE MUSICA FICA DE FORA, e so ela: o `AudioDirector.Cruzar` tem UMA carga de fluxo, a da faixa que a
	/// thread de carga nao trouxe. As bancadas (`Robo*.cs`) tambem: elas carregam pra MEDIR.
	/// </summary>
	private void ConferirAPortaDosSons()
	{
		const string pasta = "res://Client";
		const string carga = "Load<AudioStream>(";
		var soltas = new List<string>();
		int lidos = 0, daMusica = 0;
		foreach (string nome in DirAccess.GetFilesAt(pasta))
		{
			if (!nome.EndsWith(".cs", StringComparison.Ordinal)) continue;
			if (nome.StartsWith("Robo", StringComparison.Ordinal) || nome == "SonsPresos.cs") continue;
			lidos++;
			string[] linhas = Godot.FileAccess.GetFileAsString($"{pasta}/{nome}").Split('\n');
			for (int i = 0; i < linhas.Length; i++)
			{
				string l = linhas[i].TrimStart();
				if (l.StartsWith("//", StringComparison.Ordinal) || !l.Contains(carga, StringComparison.Ordinal)) continue;   // um comentario pode citar a chamada
				if (nome == "AudioDirector.cs") daMusica++;
				else soltas.Add($"{nome}:{i + 1}");
			}
		}

		if (lidos == 0)
		{
			Nota("os fontes do cliente nao estao aqui (jogo exportado): a conferencia da porta unica dos sons NAO foi feita");
			return;
		}
		Conferir(soltas.Count == 0 && daMusica == 1,
				 $"nenhum fonte de producao do cliente carrega um `AudioStream` fora da `SonsPresos` -- so a maquina de musica, uma vez ({lidos} arquivos lidos; {daMusica} carga(s) no `AudioDirector`"
				 + (soltas.Count > 0 ? $"; chamadas soltas: {string.Join(", ", soltas.Take(12))}" : "") + ")");
	}
}
