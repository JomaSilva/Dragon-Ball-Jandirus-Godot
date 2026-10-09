using Godot;

namespace Jandirus.Client;

/// <summary>
/// OS SCRIPTS DE NODE, CONTADOS E COBRADOS -- a rodada `--diagestouro --scripts`, e a conta deles no gravador sozinho
/// (`--diagestouro --passivo &lt;s&gt; --scripts`, ao lado do robo de outra cena).
///
/// ============================ O DEFEITO ============================
/// Pro Godot, toda classe C# de node e um RECURSO de script (`res://Client/X.cs`), e o recurso so vive enquanto alguem
/// o segura. Quem o segura sao as INSTANCIAS da classe, e mais ninguem. Quando a ultima morre o motor destroi o script
/// (`CSharpScript::~CSharpScript` -> `ScriptManagerBridge.RemoveScriptBridge`), e o `new` seguinte, sem achar script
/// pra classe, pede o arquivo ao `ResourceLoader` ali mesmo -- dentro do construtor, pela thread principal
/// (`GetOrLoadOrCreateScriptForType` -> `godotsharp_internal_script_load`; o comentario do motor diz "so na primeira
/// instancia do tipo", e vale pra TODA primeira instancia depois de um zero). Lido no fonte do Godot 4.7.1
/// (`modules/mono/csharp_script_resource_format.cpp`, `csharp_script.cpp` e `ScriptManagerBridge.cs`), cada carga:
///
///   * passa pela maquina do `ResourceLoader` (a tarefa de carga, o remapeamento do caminho, os carregadores);
///   * LE O FONTE INTEIRO do `.cs` (`load_source_code`) -- so nos binarios de depuracao: o do editor, que e o de toda
///     bancada e de quem roda o jogo pelo `.bat`, e o template de depuracao da exportacao;
///   * refaz a ficha da classe (`CSharpScript::reload` -> `UpdateScriptClassInfo`): um dicionario do Godot por METODO
///     da lista da classe, e uma passada de reflexao em todos os metodos dela atras de `[Rpc]` -- em todo binario.
///
/// NAO DEPENDE DO COLETOR DO .NET, e e a diferenca pros sons de efeito (`SonsPresos`): o script morre no quadro em que
/// a ultima instancia e liberada, sempre. Um efeito que nasce, dura um segundo e some -- o tiro, a carga dele, a
/// estrela de um embate, a poeira -- carrega o script de novo toda vez que aparece sozinho.
///
/// ============================ O QUE SE MEDE ============================
/// O `new` de cada classe, com um cronometro em volta e um FATO ao lado -- o script estava no cache do Godot antes?
/// (`ResourceLoader.HasCached`) --, em dois estados alternados na mesma corrida:
///
///   * COMO O PROCESSO ESTA: sem dono, o `new` carrega o script e o `Free` o mata;
///   * COM A BANCADA SEGURANDO o script (`ResourceLoader.Load`, guardado numa variavel): o `new` so faz o node.
///
/// A diferenca e o custo da carga. Depois, o mesmo pela PORTA DE PRODUCAO, cada nascimento num quadro so dele: a
/// chamada inteira e a fase de script do quadro, pra a conta nao ficar so no construtor.
///
/// E UMA SONDA conta as cargas em jogo sem `--verbose`: no comeco e no fim de cada quadro (fora da fase de script, pra
/// nao entrar no trabalho que a bancada imprime) ela pergunta ao Godot por cada classe de script de producao, e a
/// passagem de "fora do cache" pra "no cache" e uma carga. Com `--verbose` na linha sai ao lado a palavra do motor
/// (`Completed load ... at thread -1`), que serve pra CONFERIR a contagem -- e nunca pra dizer quanto custou: com ele o
/// motor escreve no log a cada carga, e a carga custa mais.
///
/// A REGUA (`RelatarOsScripts`) tem quatro pernas, cobradas nas classes que os gestos de uma luta nascem e matam: sem
/// nenhuma instancia viva o script continua no cache (tem DONO); nascida sozinha, a classe nao o carrega de novo; o
/// `new` dela custa o de um node; e, do lobby ao fim da corrida, a sonda nao viu o script dela morrer. O CONTROLE e um
/// node que nenhuma lista cita (`RoboDeScriptSolto`): ele TEM de continuar sendo solto -- e a prova, em toda corrida,
/// de que o mecanismo ainda existe no motor.
///
/// ============================ O QUE ELA MEDIU (2026-10-09) ============================
/// CADA CARGA, sem `--verbose`, no binario do editor -- o de toda bancada. Cinco corridas: a do binario sem o conserto
/// e, no de hoje, quatro com `--semsegurarosscripts`. O `new` da classe sozinha (60 voltas de cada; com o script na mao
/// de alguem ele custa 0,002 a 0,006 ms) e a chamada da porta de producao (12 a 24 nascimentos de cada):
///
///                               fonte   metodos    a carga, no `new`    a carga, na chamada da porta
///     ProjetilDesenhado ......  52 KB     42       0,79 a 0,86 ms       1,0 a 1,1 ms
///     CargaDeRaioVisual ......  10 KB     11       0,53 a 0,64          0,7 a 0,8
///     ChoqueDeKi .............  19 KB     19       0,62 a 0,70          0,8
///     PoeiraDeEstrago ........  21 KB     11       0,66 a 0,76          0,8 a 0,9
///     EstouroDeKi ............   4 KB      7       0,52 a 0,62          0,65 a 0,73
///     LuzDeKi ................  15 KB      9       0,61 a 0,71          (so nasce com o mundo escuro)
///     Transformacao .......... 188 KB     84       1,18 a 1,31          1,4 a 1,5
///     Zanzoken, EsquivaZanzoken, MarcaDeAlvo ...   0,50 a 0,61          (nascem em volta de um corpo)
///     o controle (1 KB, 7 metodos) ............    0,47 a 0,55
///
/// (Pela porta a carga sai 0,1 a 0,2 ms mais cara que no `new` pelado, nas cinco corridas: o que mais o motor faz com
/// um script recem-carregado quando o node entra na arvore, ninguem isolou. E A FASE DE SCRIPT DO QUADRO do nascimento
/// custa a mais o mesmo que a chamada: 0,6 a 1,1 ms sem dono, e 1,3 a 1,6 a cinematica -- nas duas corridas com a
/// espera antes de cada nascimento sorteada. Nas tres de antes disso ela saia de 0,5 a 1,4: ver o `AndarNaPorta`.)
///
/// ONDE O TEMPO VAI, separado num projeto minimo, no mesmo binario (classes sinteticas, com o tamanho do fonte e o
/// numero de metodos escolhidos): 0,41 a 0,45 ms pra uma classe de 1 KB e 9 metodos -- o piso, a maquina do
/// `ResourceLoader`; 9 microssegundos por METODO a mais (256 metodos: 2,5 a 3,1 ms); e 0,6 microssegundo por KB de
/// fonte (190 KB: 0,1 ms). OU SEJA: LER O FONTE E A PARTE PEQUENA. (Uma classe SEM caminho de script -- a que mora
/// num arquivo de outro nome -- nao passa pelo `ResourceLoader`: refaz so a ficha, 0,04 ms.)
///
/// COM `--verbose` CADA CARGA CUSTA 0,85 ms A MAIS (o mesmo projeto minimo: 0,52 vira 1,39 e 2,89 vira 3,78) -- o motor
/// escreve duas linhas no log por carga. Os "4 a 12 ms de trabalho" dos quadros com releitura, no log em que isto foi
/// visto pela primeira vez (`sons_a_duelo_v.log`), tinham isso dentro.
///
/// NUMA LUTA -- o duelo de dois NPCs do diretor do trailer, 91 a 93 s do primeiro tiro ao ultimo, com o gravador ao
/// lado (`--trailer duelo ... --diagestouro --passivo 100 --scripts`):
///
///                                                cargas de script na luta        por minuto de luta
///     ANTES   o binario sem o conserto ........  26 (25 delas recargas)          17,1
///             `--semsegurarosscripts` .........  22; e 16 com `--verbose`        14,2; 10,6
///     DEPOIS  (tres corridas) .................  0                               0
///
///     quem era recarregado, por duelo: a poeira 6 a 10 vezes, a carga do raio 5 a 9, o tiro 5 a 7, a estrela do embate
///     3 a 5, o vulto do Zanzoken ate 3, o estouro 1 ou 2 -- 17 a 18 ms de thread principal ao todo (cada carga pelo
///     preco da classe dela, medido no fim da mesma corrida)
///     E VEM AOS MONTES: o quadro em que dois raios se encontram nasce a estrela, a poeira e o segundo tiro, tres cargas
///     de uma vez (7,8 e 9,2 ms de trabalho nos dois em que isso caiu; o quadro inteiro, 12 e 16)
///     o quadro em que um raio nasce: 4,9 a 9,2 ms de trabalho antes, 2,3 a 5,3 depois (fora o primeiro do processo)
///
/// A CONTAGEM DA SONDA BATE COM A DO MOTOR nas duas corridas com `--verbose`: 42 e 42, 63 e 63 arquivos `.cs`. E NAS
/// CENAS DE TRANSFORMACAO (`--trailer formas`, duas cinematicas): o `Transformacao.cs` carregado de novo nas duas, 1,1
/// ms cada; depois, nenhuma vez.
///
/// O QUE O CONSERTO CUSTA: 20 a 24 ms uma vez por processo, no quadro em que o lobby nasce -- as dez cargas (6,4 a 6,7
/// ms somadas, em regime) e o codigo delas rodando pela primeira vez, que sem o conserto caia no primeiro uso de cada
/// classe: no ensaio, ou no meio do jogo pras cinco que o ensaio nao nasce (a carga do raio, a luz, o vulto, a esquiva,
/// a marca do alvo).
///
/// ============================ E NO JOGO EXPORTADO ============================
/// FAZ O MESMO, em todo binario. O projeto minimo exportado de quatro jeitos -- os dois templates do Godot 4.7.1, com o
/// fonte no pacote e sem ele; .NET 8 proprio, contra o .NET 10 em que o binario do editor roda o jogo: em todos, morta a
/// ultima instancia o script sai do cache e o `new` seguinte o carrega, 120 vezes em 120. O que muda e o preco:
///
///                                         o editor     template de depuracao      template de lancamento
///     classe de 1 KB e 9 metodos ......   0,45 ms      0,24 (0,25 sem o fonte)    0,12 (0,13)
///     20 KB e 36 metodos ..............   0,78         0,47                       0,32
///     50 KB e 76 metodos ..............   1,20         0,81 (0,79)                0,61 (0,60)
///     190 KB e 256 metodos ............   3,09         2,08                       1,85
///     190 KB e 9 metodos ..............   0,52         0,33 (0,23 sem o fonte)    0,11
///
/// Cai o PISO (0,45 no editor, 0,24 na depuracao, 0,12 no lancamento) e some a leitura do fonte, que so a depuracao faz
/// e so se ele estiver no pacote (0,1 ms por 190 KB). O custo por METODO fica: 6,5 a 9 microssegundos em todos.
///
/// O JOGO DE VERDADE, exportado de uma COPIA do repo com o preset do dono, rodando esta mesma rodada:
///
///                                         a carga, no `new`              na chamada da porta
///     template de DEPURACAO ...........   0,25 a 0,51 ms; a cinematica   0,4 a 0,7 ms; a cinematica 1,1
///                                         0,84; as dez somadas 3,9
///     template de LANCAMENTO ..........   0,13 a 0,35 ms; a cinematica   0,24 a 0,44 ms; a cinematica 0,7
///                                         0,62; as dez somadas 2,3
///
///     nos dois a regua passa (o script das dez tem dono desde o lobby, que paga por isso 17 ms na depuracao e 13 no
///     lancamento) e, com `--semsegurarosscripts`, reprova nas quatro pernas
///
/// O `DBJ.exe` QUE O DONO EXPORTOU em 2026-08-16 (`../Games da silva`) e template de DEPURACAO com os 542 `.cs` inteiros
/// no pacote -- conferido nos bytes: o codigo dele e o do `windows_debug_x86_64.exe`, e o preset tem
/// `dotnet/include_scripts_content=true`. E a linha da depuracao.
///
/// ============================ O QUE FICOU DE FORA ============================
///   * AS CLASSES QUE NAO SAO DE LUTA. A sonda, que vigia todas, so viu mais uma recarga em duelo e em cinematica: o
///     `RemotePlayer.cs`, uma vez, na chegada na zona (os corpos da zona velha morrem todos antes de os da nova
///     nascerem) -- num quadro que ja custa 130 a 210 ms. As coisas do mapa que entram e saem de vista (a esfera, a
///     nave, a obra, a estrela, a porta) e as telas que nascem num clique de menu tem o mesmo mecanismo, e ninguem as
///     mediu em uso.
///   * AS CLASSES SEM CAMINHO DE SCRIPT (`Fogo`, `PlanetaDesenhado`, `AnelDoRaioLetal`, `DiscoDeEstrela`, `Barra`,
///     `DesenhoDaChave`: moram no arquivo de outra classe). Nao passam pelo `ResourceLoader`, mas refazem a ficha a cada
///     nascimento sozinhas (0,04 ms com 8 metodos, no projeto minimo), e nao da pra segura-las pelo caminho.
///   * O PROCESSO SEM AQUECIMENTO (`--semaquecimento`): ninguem segura nada.
///   * O LIXO: cada carga aloca um punhado de involucros com finalizador por metodo da classe. Na rodada de injecao
///     (1.300 cargas em 13 s, nada parecido com um jogo) o coletor do .NET parou o processo 4 vezes por 20 ms, contra
///     nenhuma na rodada verde; quanto disso pesava numa luta de verdade, ninguem mediu.
///   * A COMPLETUDE DA LISTA: a regua cobra as dez classes da lista DESTA bancada. Uma classe de efeito nova, que
///     ninguem ponha em lista nenhuma, so aparece na conta do gravador (`--passivo` com `--scripts`).
/// </summary>
public partial class RoboDoPrimeiroEstouro
{
	/// <summary>`--scripts`: a rodada dos scripts de node (<see cref="AndarNosScripts"/>) e, com `--passivo`, a conta deles na cena de outro robo.</summary>
	private readonly bool _scripts = Tem("--scripts");

	/// <summary>
	/// `--scripts --semsegurarosscripts`: A RODADA DE INJECAO dos scripts -- o processo entra no mundo com o defeito
	/// `Aquecimento.SemSegurarOsScriptsDeTeste` (ligado pelo `Boot`, antes de o aquecimento nascer). As quatro pernas da
	/// regua tem que REPROVAR, e e desta rodada que sai o numero de antes, no mesmo binario.
	/// </summary>
	private readonly bool _semSegurarOsScripts = Tem("--semsegurarosscripts");

	private const string DefeitoDosScripts = "os scripts de node sem dono: carregados de novo a cada nascimento sem outra instancia viva";

	private static double MsDeScript(long de, long ate) => (ate - de) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

	/// <summary>
	/// O caminho de recurso que o MOTOR usa pro script desta classe, ou nulo se ela nao tem um. Sai do `ScriptPath` que
	/// o gerador do Godot poe em cada classe de script -- o mesmo atributo que o GodotSharp le pra montar o mapa dele.
	/// (So ganha caminho a classe que tem o nome do arquivo em que mora: `Fogo`, dentro do `Iluminacao.cs`, nao tem.)
	/// </summary>
	private static string? CaminhoDoScript(Type tipo)
	{
		Attribute[] achados = Attribute.GetCustomAttributes(tipo, typeof(ScriptPathAttribute), false);
		return achados.Length > 0 ? ((ScriptPathAttribute)achados[0]).Path : null;
	}

	// =====================================================================
	// A SONDA -- que script o motor carregou, e em que quadro, sem `--verbose`
	// =====================================================================
	private sealed class ScriptVigiado
	{
		public string Nome = "", Caminho = "";
		public bool Vivo, JaViveu;
		public int Mortes;
	}

	private readonly List<ScriptVigiado> _vigiados = [];

	/// <summary>Cada carga de script que a sonda viu: o quadro, a classe, se ela ja tinha vivido neste processo (RECARGA) e se caiu fora da fase de script.</summary>
	private readonly List<(int Quadro, ScriptVigiado Qual, bool Recarga, bool ForaDoScript)> _cargasDeScript = [];

	private bool _sondaLigada, _sondaAssentada;
	private double _msDaSonda;
	private int _olhadasDaSonda;

	/// <summary>
	/// Liga a sonda -- so com `--scripts` na linha. Chamado no `_Ready`, no lobby: o ensaio do aquecimento ainda nao
	/// nasceu classe nenhuma, e a primeira carga de cada uma entra na conta como PRIMEIRA.
	///
	/// VIGIA TODA CLASSE DE SCRIPT DE PRODUCAO, e nao uma lista escolhida: e ela que diz o que ficou de fora de qualquer
	/// lista. As bancadas (`Robo*`) nao entram -- o que se conta e o jogo.
	/// </summary>
	private void OuvirOsScripts()
	{
		if (!_scripts || _sondaLigada) return;
		_sondaLigada = true;

		Type[] tipos;
		try { tipos = typeof(RoboDoPrimeiroEstouro).Assembly.GetTypes(); }
		catch (System.Reflection.ReflectionTypeLoadException e) { tipos = [.. e.Types.Where(t => t != null).Select(t => t!)]; }
		foreach (Type tipo in tipos)
		{
			if (tipo.Name.StartsWith("Robo", StringComparison.Ordinal)) continue;
			if (CaminhoDoScript(tipo) is not { Length: > 0 } caminho) continue;
			_vigiados.Add(new ScriptVigiado { Nome = tipo.Name, Caminho = caminho });
		}
		_vigiados.Sort((a, b) => string.CompareOrdinal(a.Nome, b.Nome));

		// DUAS OLHADAS POR QUADRO, as duas FORA da fase de script que a bancada cronometra: uma antes do gancho que abre
		// o quadro e outra depois do que o fecha. Uma so nao bastaria: o node que morre no fim de um quadro e renasce no
		// seguinte esta "no cache" nas duas pontas de qualquer olhada unica.
		AddChild(new MarcaDeQuadro { Name = "SondaDeScriptsNoComeco", ProcessPriority = -100_001, Agora = () => SondarOsScripts(noFim: false) });
		AddChild(new MarcaDeQuadro { Name = "SondaDeScriptsNoFim", ProcessPriority = 100_001, Agora = () => SondarOsScripts(noFim: true) });
		// (com `--verbose` na linha, a palavra do motor sai ao lado da contagem da sonda)
		LigarAEscutaDeCarga();
	}

	private void SondarOsScripts(bool noFim)
	{
		if (_acabou) return;
		long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

		// O QUADRO DESTA OLHADA: a do comeco roda antes de o robo abrir o quadro (e o que vai entrar na lista); a do fim,
		// depois de ele o fechar (e o ultimo da lista).
		int quadro = Math.Max(0, noFim ? _quadros.Count - 1 : _quadros.Count);
		foreach (ScriptVigiado v in _vigiados)
		{
			bool vivo = ResourceLoader.HasCached(v.Caminho);
			if (vivo && !v.Vivo && _sondaAssentada) _cargasDeScript.Add((quadro, v, v.JaViveu, !noFim));
			if (!vivo && v.Vivo) v.Mortes++;
			v.Vivo = vivo;
			v.JaViveu |= vivo;
		}
		_sondaAssentada = true;

		_msDaSonda += MsDeScript(t0, System.Diagnostics.Stopwatch.GetTimestamp());
		_olhadasDaSonda++;
	}

	/// <summary>
	/// A LUTA, pelos rotulos que o gravador sozinho poe nos quadros: do primeiro tiro ou golpe ao ultimo. Devolve os
	/// dois quadros, ou (-1, -1) se nao houve luta.
	/// </summary>
	private (int De, int Ate) JanelaDaLuta(int de)
	{
		int primeiro = -1, ultimo = -1;
		for (int i = Math.Max(0, de); i < _quadros.Count; i++)
		{
			string marcas = _quadros[i].Marcas;
			if (marcas.Length == 0) continue;
			if (!marcas.Contains("GOLPE em ", StringComparison.Ordinal) && !marcas.Contains("tiro ", StringComparison.Ordinal)) continue;
			if (primeiro < 0) primeiro = i;
			ultimo = i;
		}
		return (primeiro, ultimo);
	}

	/// <summary>
	/// A CONTA DOS SCRIPTS do quadro `de` em diante: as cargas que a sonda viu, quantas eram RECARGAS (a classe ja tinha
	/// vivido neste processo, a ultima instancia morreu e o motor soltou o script), a taxa por minuto de luta, por classe,
	/// e -- no gravador sozinho, `cadaUma` -- cada recarga com o quadro em que caiu. (Na rodada `--scripts` as recargas
	/// sao as da propria bancada, nascimento a nascimento: so a contagem.)
	/// </summary>
	private List<string> ContaDosScripts(int de, bool cadaUma)
	{
		var linhas = new List<string>();
		if (!_sondaLigada) return linhas;

		var cargas = _cargasDeScript.FindAll(c => c.Quadro >= de);
		int recargas = cargas.Count(c => c.Recarga);
		double porQuadro = _olhadasDaSonda > 0 ? _msDaSonda / _olhadasDaSonda * 2 : 0;
		linhas.Add($"OS SCRIPTS DE NODE: a sonda vigia {_vigiados.Count} classe(s) de producao com caminho de script, duas olhadas por quadro"
				   + $" ({porQuadro:0.000} ms por quadro, fora da fase de script)");
		linhas.Add($"   -- o motor CARREGOU script {cargas.Count} vez(es), pela thread principal: {cargas.Count - recargas} PRIMEIRA(S) carga(s) do processo"
				   + $" e {recargas} RECARGA(S) -- a classe ja tinha vivido, a ultima instancia morreu e o script foi solto");
		if (SaidaVerbosa)
		{
			int doMotor = 0;
			foreach ((int quadro, string[] lidos) in _lidos)
				if (quadro >= de)
					doMotor += lidos.Count(l => l.EndsWith(".cs", StringComparison.Ordinal) && !l.GetFile().StartsWith("Robo", StringComparison.Ordinal));
			linhas.Add($"   -- a palavra do motor (`--verbose`, `Completed load ... at thread -1`) no mesmo trecho: {doMotor} arquivo(s) `.cs` de producao");
		}

		(int lutaDe, int lutaAte) = JanelaDaLuta(de);
		if (lutaAte > lutaDe)
		{
			double segundos = SegundosDesde(lutaDe, lutaAte);
			var naLuta = cargas.FindAll(c => c.Quadro >= lutaDe && c.Quadro <= lutaAte);
			linhas.Add($"   -- NA LUTA (do primeiro tiro ou golpe ao ultimo, {segundos:0.0} s): {naLuta.Count} carga(s), {naLuta.Count(c => c.Recarga)} dela(s) recarga(s)"
					   + (segundos > 1 ? $" = {naLuta.Count * 60.0 / segundos:0.0} por minuto de luta" : ""));
		}

		if (cargas.Count == 0) return linhas;

		// (a classe com UMA carga, a primeira, e a que nasceu com o mundo e ficou: sai contada, nao listada)
		linhas.Add("   POR CLASSE -- cargas (recargas), e quantas vezes o script dela morreu desde o lobby:");
		int deUmaSo = 0;
		foreach (var grupo in cargas.GroupBy(c => c.Qual).OrderByDescending(g => g.Count(c => c.Recarga)).ThenBy(g => g.Key.Nome, StringComparer.Ordinal))
		{
			if (grupo.Count() == 1 && !grupo.First().Recarga && grupo.Key.Mortes == 0) { deUmaSo++; continue; }
			linhas.Add($"      {grupo.Key.Nome + ".cs",-26} {grupo.Count(),3} carga(s) ({grupo.Count(c => c.Recarga)} recarga(s))   {grupo.Key.Mortes} morte(s) do script");
		}
		if (deUmaSo > 0) linhas.Add($"      ... e {deUmaSo} classe(s) com uma carga so, a primeira, e o script vivo ate agora");

		var asRecargas = cadaUma ? cargas.FindAll(c => c.Recarga) : [];
		if (asRecargas.Count > 0)
		{
			linhas.Add("   CADA RECARGA, no quadro em que caiu -- e o trabalho do quadro inteiro:");
			foreach ((int quadro, ScriptVigiado qual, _, bool fora) in asRecargas.Take(160))
				linhas.Add($"      t={SegundosDesde(de, quadro),7:0.000}s  {qual.Nome + ".cs",-26}{(fora ? " (fora da fase de script)" : "")}"
						   + (quadro + 1 < _quadros.Count
							   ? $" | o quadro: trabalho {Trabalho(quadro):0.0} ms (script {ScriptMs(quadro) - ColetorNoScript(quadro):0.0}), inteiro {TotalMs(quadro):0.0}"
							   : ""));
			if (asRecargas.Count > 160) linhas.Add($"      ... e mais {asRecargas.Count - 160}");
		}
		return linhas;
	}

	/// <summary>
	/// O PRECO DAS CARGAS QUE A CENA FEZ: cada classe da lista da bancada que a sonda viu ser carregada do quadro `de`
	/// em diante e medida ali mesmo, pelada (<see cref="MedirPelado"/>), e a conta sai multiplicada. So da pra medir a
	/// classe que nao tem instancia viva nem dono naquele instante -- as outras saem ditas.
	/// </summary>
	private List<string> PrecoDasCargas(int de)
	{
		var linhas = new List<string>();
		var cargas = _cargasDeScript.FindAll(c => c.Quadro >= de);
		if (cargas.Count == 0) return linhas;

		(int lutaDe, int lutaAte) = JanelaDaLuta(de);
		double total = 0, naLuta = 0;
		int semPreco = 0;
		linhas.Add("   O PRECO, medido agora no mesmo processo -- o `new` da classe sozinha sem dono, menos o mesmo `new` com a bancada segurando o script:");
		foreach (var grupo in cargas.GroupBy(c => c.Qual).OrderBy(g => g.Key.Nome, StringComparer.Ordinal))
		{
			ScriptDaRodada? s = _scriptsDaRodada.Find(x => x.Tipo.Name == grupo.Key.Nome);
			if (s == null) { semPreco += grupo.Count(); continue; }
			PrepararAClasse(s);
			if (ResourceLoader.HasCached(s.Caminho))
			{
				linhas.Add($"      {grupo.Key.Nome + ".cs",-26} {grupo.Count(),3} carga(s): NAO MEDIDA aqui (ha instancia viva ou dono do script neste instante)");
				semPreco += grupo.Count();
				continue;
			}
			MedirPelado(s, 4, guardar: false);
			MedirPelado(s, 24, guardar: true);
			double carga = Math.Max(0, MedianaDe(s.NovoNatural) - MedianaDe(s.NovoSeguro));
			total += carga * grupo.Count();
			if (lutaAte > lutaDe) naLuta += carga * grupo.Count(c => c.Quadro >= lutaDe && c.Quadro <= lutaAte);
			linhas.Add($"      {grupo.Key.Nome + ".cs",-26} {grupo.Count(),3} carga(s) x {carga:0.000} ms = {carga * grupo.Count():0.0} ms"
					   + $"   (fonte de {s.Bytes / 1024.0:0} KB, {s.Metodos} metodos, {s.MetodosDoGodot} na lista do Godot; `new` sem dono {MedianaDe(s.NovoNatural):0.000}, com dono {MedianaDe(s.NovoSeguro):0.000})");
		}
		linhas.Add($"   -- AO TODO: {total:0.0} ms de thread principal carregando script" + (lutaAte > lutaDe ? $", {naLuta:0.0} deles na luta" : "")
				   + (semPreco > 0 ? $" ({semPreco} carga(s) sem preco: classe fora da lista da bancada, ou que nao dava pra medir)" : ""));
		return linhas;
	}

	/// <summary>O fecho do gravador sozinho: a conta dos scripts da cena que ele assistiu, desde o corpo no mundo.</summary>
	private void ImprimirOsScriptsDoPassivo()
	{
		if (!_sondaLigada) return;
		int de = Math.Max(1, _entrouNoMundo);
		if (Aquecimento.SemSegurarOsScriptsDeTeste) GD.Print("[estouro] DEFEITO INJETADO a gravacao inteira: os scripts de node sem dono (`--semsegurarosscripts`)");
		foreach (string linha in ContaDosScripts(de, cadaUma: true)) GD.Print("[estouro] " + linha);
		foreach (string linha in PrecoDasCargas(de)) GD.Print("[estouro] " + linha);
	}

	// =====================================================================
	// A RODADA `--scripts`
	// =====================================================================
	/// <summary>
	/// Quanto o `new` de uma classe de efeito, sozinha, pode custar, em ms. Com o script na mao de alguem ele so faz o
	/// node -- centesimos de ms; carregando o script, e a carga inteira (os numeros de cada corrida saem no relatorio,
	/// classe a classe). O cronometro nao e a unica palavra: as outras duas pernas da regua sao FATOS.
	/// </summary>
	private const double TetoDoNascimento = 0.1;

	/// <summary>Quantas voltas de medida cada classe da, pelada e pela porta, e quantas por quadro (duas cargas de um script grande ja sao meio quadro).</summary>
	private const int VoltasDoPelado = 60, VoltasPorQuadro = 2, AquecerOPelado = 4, VoltasDaPorta = 48;

	private static readonly Color CorDosScripts = new(0.3f, 0.6f, 1f);

	private sealed class ScriptDaRodada
	{
		public string Rotulo = "";
		public Type Tipo = typeof(Node);

		/// <summary>O `new` da classe, e mais nada: o node nao entra na arvore.</summary>
		public Func<Node> Novo = static () => new Node();

		/// <summary>A PORTA DE PRODUCAO da classe, num palco da bancada -- nula pra classe que so nasce de um pacote do servidor.</summary>
		public Action<Node2D, Vector2>? Porta;

		/// <summary>O controle: um node que nenhuma lista do jogo cita.</summary>
		public bool DeFora;

		public string Caminho = "";
		public long Bytes;
		public int Metodos, MetodosDoGodot;

		/// <summary>Sem nenhuma instancia viva e antes de a bancada tocar em nada: o script estava no cache do Godot?</summary>
		public bool TinhaDono;

		/// <summary>A contagem de referencias do script com a bancada olhando: 1 = so um involucro C# o segura, e nenhuma instancia.</summary>
		public int Refs = -1;

		/// <summary>Quantas vezes a classe nasceu sozinha na rodada (pelada e pela porta), e em quantas o script ja estava no cache antes do `new`.</summary>
		public int Nascimentos, ComOScriptVivo;

		/// <summary>Os tempos, em ms: como o processo esta (Natural) e com a bancada segurando o script (Seguro).</summary>
		public readonly List<double> NovoNatural = [], FreeNatural = [], NovoSeguro = [], FreeSeguro = [], Carga = [], Soltar = [];

		/// <summary>Cada nascimento pela porta de producao: o quadro, a chamada inteira em ms, e se o script tinha dono (o do processo, ou a bancada).</summary>
		public readonly List<(int Quadro, double Ms, bool ComDono)> NaPorta = [];
	}

	/// <summary>
	/// AS CLASSES DA RODADA: os nodes que os gestos de uma luta, de uma esquiva e de uma transformacao NASCEM E MATAM. A
	/// lista e DESTA bancada, escrita gesto a gesto: a pergunta e "o que o jogo nasce no meio da luta tem dono?", e a
	/// resposta nao pode sair da lista de quem escolheu o que segurar. A ultima e o controle.
	/// </summary>
	private readonly List<ScriptDaRodada> _scriptsDaRodada =
	[
		new() { Rotulo = "todo tiro de ki, bola ou raio", Tipo = typeof(ProjetilDesenhado), Novo = static () => new ProjetilDesenhado(), Porta = PortaDoTiro },
		new() { Rotulo = "o brilho na mao de quem carrega um raio", Tipo = typeof(CargaDeRaioVisual), Novo = static () => new CargaDeRaioVisual(), Porta = PortaDaCarga },
		new() { Rotulo = "a estrela do embate", Tipo = typeof(ChoqueDeKi), Novo = static () => new ChoqueDeKi(), Porta = PortaDaEstrela },
		new() { Rotulo = "a poeira de todo estrago", Tipo = typeof(PoeiraDeEstrago), Novo = static () => new PoeiraDeEstrago(), Porta = PoeiraDeEstrago.Ensaiar },
		new() { Rotulo = "o estouro de todo tiro que acerta", Tipo = typeof(EstouroDeKi), Novo = static () => new EstouroDeKi(), Porta = static (pai, onde) => EstouroDeKi.Soltar(pai, onde, 14f, CorDosScripts) },
		new() { Rotulo = "a luz de cada efeito de ki num mundo escuro", Tipo = typeof(LuzDeKi), Novo = static () => new LuzDeKi() },
		new() { Rotulo = "a cinematica de transformacao", Tipo = typeof(Transformacao), Novo = static () => new Transformacao(), Porta = Transformacao.Ensaiar },
		new() { Rotulo = "o vulto que o Zanzoken deixa pra tras", Tipo = typeof(Zanzoken), Novo = static () => new Zanzoken() },
		new() { Rotulo = "o corpo trocado da esquiva por Zanzoken", Tipo = typeof(EsquivaZanzoken), Novo = static () => new EsquivaZanzoken() },
		new() { Rotulo = "o anel que marca o alvo", Tipo = typeof(MarcaDeAlvo), Novo = static () => new MarcaDeAlvo() },
		new() { Rotulo = "um node que nenhuma lista do jogo cita", Tipo = typeof(RoboDeScriptSolto), Novo = static () => new RoboDeScriptSolto(), DeFora = true },
	];

	/// <summary>Um tiro parado, pela receita do `World.AoNascerTiro` (tipo e cor ANTES do `Vestir`) e do ensaio do aquecimento: sem luz propria.</summary>
	private static void PortaDoTiro(Node2D pai, Vector2 onde)
	{
		var tiro = new ProjetilDesenhado { Tipo = Jandirus.Core.Combat.TipoDeProjetil.Blast, Cor = CorDosScripts, SemLuz = true, SempreVoando = true, Position = onde };
		tiro.Vestir(Jandirus.Core.Combat.ArteDeKi.Nenhuma, 1f);
		tiro.Mirar(onde, onde - new Vector2(48, 0));
		pai.AddChild(tiro);
	}

	/// <summary>A carga de um raio, como o `World.MarcarCargaDeRaio` a pendura num corpo.</summary>
	private static void PortaDaCarga(Node2D pai, Vector2 onde) =>
		pai.AddChild(new CargaDeRaioVisual { Estado = 1, Direcao = Jandirus.Core.World.Facing.South, Cor = CorDosScripts, Position = onde });

	/// <summary>A estrela do embate, definida antes de entrar na arvore como o `World.TickDosChoquesDeKi` faz -- sem pedras e sem luz, como a do ensaio.</summary>
	private static void PortaDaEstrela(Node2D pai, Vector2 onde)
	{
		var estrela = new ChoqueDeKi { NoChao = false, SemLuz = true };
		estrela.Definir(onde, Vector2.Right, 30f, 18f, CorDosScripts, new Color(1f, 0.3f, 0.8f));
		pai.AddChild(estrela);
	}

	private static double MedianaDe(List<double> v) => Mediana([.. v]);

	/// <summary>O caminho, o peso do fonte e a conta de metodos da classe -- uma vez.</summary>
	private static void PrepararAClasse(ScriptDaRodada s)
	{
		if (s.Caminho.Length > 0) return;
		s.Caminho = CaminhoDoScript(s.Tipo) ?? "";
		if (s.Caminho.Length > 0 && Godot.FileAccess.FileExists(s.Caminho))
		{
			using Godot.FileAccess? f = Godot.FileAccess.Open(s.Caminho, Godot.FileAccess.ModeFlags.Read);
			if (f != null) s.Bytes = (long)f.GetLength();
		}
		const System.Reflection.BindingFlags daClasse = System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
		s.Metodos = s.Tipo.GetMethods(daClasse | System.Reflection.BindingFlags.Instance).Length;
		// (a lista que o gerador do Godot escreve pra classe: e por METODO dela que a ficha do script e refeita)
		s.MetodosDoGodot = (s.Tipo.GetMethod("GetGodotMethodList", daClasse | System.Reflection.BindingFlags.Static)?.Invoke(null, null) as System.Collections.ICollection)?.Count ?? -1;
	}

	/// <summary>
	/// O DONO DO SCRIPT, sem cronometro. Com nenhuma instancia viva: o script esta no cache do Godot? E quem o segura?
	///
	/// A SEGUNDA PERGUNTA E O QUE SEPARA "tem dono" DE "tem instancia viva": a bancada pede o script e le a contagem de
	/// referencias dele. O involucro C# de um script e UM so por script -- o que o `Load` devolve aqui e o mesmo objeto
	/// que um dono ja segurasse --, e cada instancia viva soma uma referencia: 1 quer dizer "so um involucro C#, e
	/// nenhuma instancia". (Pelo mesmo motivo a bancada so SOLTA o que ela mesma carregou: soltar o involucro de outro
	/// dono soltaria o script dele.)
	/// </summary>
	private static void OlharODono(ScriptDaRodada s)
	{
		PrepararAClasse(s);
		if (s.Caminho.Length == 0) return;
		s.TinhaDono = ResourceLoader.HasCached(s.Caminho);
		Script? olho = ResourceLoader.Load<Script>(s.Caminho);
		s.Refs = olho?.GetReferenceCount() ?? -1;
		if (!s.TinhaDono) olho?.Dispose();
	}

	/// <summary>
	/// A CLASSE PELADA: `new` e `Free`, sem arvore, com um cronometro em volta de cada um. Cada volta mede os dois
	/// estados, um atras do outro:
	///
	///   1. COMO O PROCESSO ESTA -- e o fato ao lado: o script estava no cache antes do `new`?
	///   2. COM A BANCADA SEGURANDO o script -- so quando ninguem mais o segura (ver <see cref="OlharODono"/>). E o que
	///      um dono custa pra carregar (a "carga pela porta") e o que o `new` custa com ele.
	///
	/// A volta em que o coletor do .NET passou nao vale: a pausa dele cairia dentro de um dos cronometros.
	/// </summary>
	private static void MedirPelado(ScriptDaRodada s, int voltas, bool guardar)
	{
		if (s.Caminho.Length == 0) return;
		for (int i = 0; i < voltas; i++)
		{
			int coletas = GC.CollectionCount(0);

			bool vivoAntes = ResourceLoader.HasCached(s.Caminho);
			long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
			Node a = s.Novo();
			long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
			a.Free();
			long t2 = System.Diagnostics.Stopwatch.GetTimestamp();

			long t3 = 0, t4 = 0, t5 = 0, t6 = 0, t7 = 0;
			if (!vivoAntes)
			{
				t3 = System.Diagnostics.Stopwatch.GetTimestamp();
				Script? dono = ResourceLoader.Load<Script>(s.Caminho);
				t4 = System.Diagnostics.Stopwatch.GetTimestamp();
				Node b = s.Novo();
				t5 = System.Diagnostics.Stopwatch.GetTimestamp();
				b.Free();
				t6 = System.Diagnostics.Stopwatch.GetTimestamp();
				dono?.Dispose();
				t7 = System.Diagnostics.Stopwatch.GetTimestamp();
			}

			if (!guardar) continue;
			s.Nascimentos++;
			if (vivoAntes) s.ComOScriptVivo++;
			if (GC.CollectionCount(0) != coletas) continue;
			s.NovoNatural.Add(MsDeScript(t0, t1));
			s.FreeNatural.Add(MsDeScript(t1, t2));
			if (vivoAntes) continue;
			s.Carga.Add(MsDeScript(t3, t4));
			s.NovoSeguro.Add(MsDeScript(t4, t5));
			s.FreeSeguro.Add(MsDeScript(t5, t6));
			s.Soltar.Add(MsDeScript(t6, t7));
		}
	}

	private enum FaseDosScripts { Donos, Pelado, Porta, Fim }

	private FaseDosScripts _faseDosScripts = FaseDosScripts.Donos;
	private int _scriptDaVez, _voltaDosScripts, _passoDaPorta, _quadrosNaPorta, _esperaDaPorta = 2;
	private Node2D? _palcoDosScripts;
	private Script? _donoDaBancada;

	/// <summary>
	/// A RODADA, em tres tempos:
	///
	///   1. OS DONOS, com o mundo parado e nenhuma instancia viva de classe nenhuma da lista (<see cref="OlharODono"/>);
	///   2. CADA CLASSE PELADA (<see cref="MedirPelado"/>), duas voltas por quadro -- duas cargas de um script grande ja
	///      sao meio quadro, e o quadro da medida nao pode virar ele mesmo um engasgo que suje a vizinha;
	///   3. CADA CLASSE PELA PORTA DE PRODUCAO (<see cref="AndarNaPorta"/>), um nascimento por quadro calmo.
	/// </summary>
	private void AndarNosScripts(World mundo)
	{
		switch (_faseDosScripts)
		{
			case FaseDosScripts.Donos:
				if (_t < 1.0) return;
				foreach (ScriptDaRodada s in _scriptsDaRodada) OlharODono(s);
				Marcar("SCRIPTS: os donos conferidos");
				_scriptDaVez = 0;
				_voltaDosScripts = 0;
				_faseDosScripts = FaseDosScripts.Pelado;
				return;

			case FaseDosScripts.Pelado:
				if (_scriptDaVez >= _scriptsDaRodada.Count)
				{
					_scriptDaVez = 0;
					_voltaDosScripts = 0;
					_faseDosScripts = FaseDosScripts.Porta;
					return;
				}
				ScriptDaRodada daVez = _scriptsDaRodada[_scriptDaVez];
				// (as primeiras voltas de cada classe nao valem: e o codigo do construtor rodando pela primeira vez)
				if (_voltaDosScripts == 0) MedirPelado(daVez, AquecerOPelado, guardar: false);
				MedirPelado(daVez, VoltasPorQuadro, guardar: true);
				Marcar($"SCRIPTS: `{daVez.Tipo.Name}` pelada");
				if ((_voltaDosScripts += VoltasPorQuadro) >= VoltasDoPelado) { _scriptDaVez++; _voltaDosScripts = 0; }
				return;

			case FaseDosScripts.Porta:
				AndarNaPorta(mundo);
				return;

			default:
				if (_t >= 0.5) Fechar();
				return;
		}
	}

	/// <summary>
	/// UM NASCIMENTO PELA PORTA DE PRODUCAO, num quadro so dele: o palco vazio ha dois quadros, a classe nasce, vive tres
	/// quadros, morre, e o palco esvazia. O cronometro e o da chamada inteira (o `new`, o material, a entrada na arvore);
	/// a fase de script do quadro sai ao lado, no relatorio.
	///
	/// NAS VOLTAS IMPARES A BANCADA SEGURA O SCRIPT -- so quando ninguem mais o segura: sem dono, as voltas pares mostram
	/// a carga e as impares o mesmo nascimento sem ela; com dono, as quarenta e oito sao iguais. ELA O SEGURA UM QUADRO
	/// ANTES do nascimento: segurar E uma carga de script, e no mesmo quadro ela entraria na fase de script que se quer
	/// comparar (a primeira corrida desta rodada fazia isso, e a diferenca entre os dois quadros saia um terco da real).
	///
	/// A ESPERA ANTES DE CADA NASCIMENTO E SORTEADA, de dois a cinco quadros. Com a volta de tamanho fixo das primeiras
	/// corridas (seis quadros, doze nascimentos de cada) a diferenca entre os dois estados, na fase de script do QUADRO,
	/// saia a corrida inteira pro mesmo lado: de 0,25 ms a menos a 0,45 a mais que a do cronometro da chamada, em quatro
	/// corridas. A SUSPEITA, que ninguem mediu: as voltas pares e as impares caiam sempre nos mesmos quadros do compasso
	/// do servidor que este processo hospeda (um tique a cada quatro quadros, num monitor de 120 Hz). SORTEADA, as duas
	/// diferencas batem -- 0,13 ms pra um lado ou pro outro no pior caso, nas seis classes, em duas corridas.
	/// </summary>
	private void AndarNaPorta(World mundo)
	{
		while (_scriptDaVez < _scriptsDaRodada.Count && _scriptsDaRodada[_scriptDaVez].Porta == null) { _scriptDaVez++; _voltaDosScripts = 0; }
		if (_scriptDaVez >= _scriptsDaRodada.Count)
		{
			_palcoDosScripts?.QueueFree();
			_palcoDosScripts = null;
			_t = 0;
			_faseDosScripts = FaseDosScripts.Fim;
			return;
		}
		ScriptDaRodada s = _scriptsDaRodada[_scriptDaVez];
		if (_palcoDosScripts == null)
		{
			_palcoDosScripts = new Node2D { Name = "PalcoDosScripts", Position = (mundo.PosicaoLocal ?? Vector2.Zero) + new Vector2(0, -96) };
			mundo.AddChild(_palcoDosScripts);
		}

		switch (_passoDaPorta)
		{
			case 0:
				if (_palcoDosScripts.GetChildCount() > 0) { _quadrosNaPorta = 0; return; }
				if (++_quadrosNaPorta < _esperaDaPorta)
				{
					if (_quadrosNaPorta == _esperaDaPorta - 1 && _voltaDosScripts % 2 == 1 && !ResourceLoader.HasCached(s.Caminho))
						_donoDaBancada = ResourceLoader.Load<Script>(s.Caminho);
					return;
				}

				bool aBancadaSegura = _donoDaBancada != null;
				bool tinhaDono = !aBancadaSegura && ResourceLoader.HasCached(s.Caminho);
				long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
				s.Porta!(_palcoDosScripts, Vector2.Zero);
				double ms = MsDeScript(t0, System.Diagnostics.Stopwatch.GetTimestamp());
				s.NaPorta.Add((_quadros.Count, ms, tinhaDono || aBancadaSegura));
				// (a volta em que a bancada segura nao e um nascimento "como o processo esta": nao entra no fato)
				if (!aBancadaSegura)
				{
					s.Nascimentos++;
					if (tinhaDono) s.ComOScriptVivo++;
				}
				Marcar($"SCRIPTS: `{s.Tipo.Name}` nasce pela porta de producao ({(tinhaDono ? "o script tem dono" : aBancadaSegura ? "a bancada segura o script" : "SEM dono: o motor carrega o script")})");
				_passoDaPorta = 1;
				_quadrosNaPorta = 0;
				return;

			case 1:
				if (++_quadrosNaPorta < 3) return;
				foreach (Node filho in _palcoDosScripts.GetChildren()) filho.QueueFree();
				_passoDaPorta = 2;
				return;

			default:
				if (_palcoDosScripts.GetChildCount() > 0) return;
				_donoDaBancada?.Dispose();
				_donoDaBancada = null;
				_passoDaPorta = 0;
				_quadrosNaPorta = 0;
				// (a espera da proxima volta e SORTEADA: ver o cabecalho do metodo)
				_esperaDaPorta = 2 + (int)(GD.Randi() % 4);
				if (++_voltaDosScripts >= VoltasDaPorta) { _scriptDaVez++; _voltaDosScripts = 0; }
				return;
		}
	}

	/// <summary>Uma perna da regua dos scripts: cobrada no jogo, e REPROVADA na rodada de injecao.</summary>
	private void CobrarOsScripts(bool ok, string oque, string medida)
	{
		if (_semSegurarOsScripts) Conferir(!ok, $"(defeito injetado: {DefeitoDosScripts}) a mesma regua REPROVA: {oque} ({medida})");
		else Conferir(ok, $"{oque} ({medida})");
	}

	/// <summary>O FECHO DA RODADA DOS SCRIPTS: as quatro pernas, o controle, e as duas tabelas -- pelada e pela porta.</summary>
	private void RelatarOsScripts()
	{
		_donoDaBancada?.Dispose();
		_donoDaBancada = null;

		List<ScriptDaRodada> daLista = _scriptsDaRodada.FindAll(s => !s.DeFora);
		List<ScriptDaRodada> semCaminho = _scriptsDaRodada.FindAll(s => s.Caminho.Length == 0);
		Conferir(semCaminho.Count == 0,
				 $"as {_scriptsDaRodada.Count} classes da rodada tem caminho de script (`ScriptPath`: a classe tem o nome do arquivo em que mora)"
				 + (semCaminho.Count > 0 ? $" -- sem caminho: {string.Join(", ", semCaminho.Select(s => s.Tipo.Name))}" : ""));
		bool mediu = _scriptsDaRodada.TrueForAll(s => s.Caminho.Length == 0 || s.NovoNatural.Count >= VoltasDoPelado / 2);
		Conferir(mediu, $"a rodada mediu as {_scriptsDaRodada.Count} classes peladas ({VoltasDoPelado} voltas cada, menos as que o coletor do .NET atravessou)");
		if (semCaminho.Count > 0 || !mediu) return;

		// ---- 0. A PERGUNTA SO VALE SEM INSTANCIA VIVA ----
		List<ScriptDaRodada> comInstancia = _scriptsDaRodada.FindAll(s => s.Refs != 1);
		Conferir(comInstancia.Count == 0,
				 $"na hora da pergunta nenhuma das {_scriptsDaRodada.Count} classes tinha instancia viva: com a bancada olhando, o script de cada uma tem UMA referencia, a de um involucro C#"
				 + (comInstancia.Count > 0 ? $" -- com outra conta: {string.Join(", ", comInstancia.Select(s => $"{s.Tipo.Name} ({s.Refs})"))}" : ""));

		// ---- 1. O DONO: sem cronometro ----
		List<ScriptDaRodada> semDono = daLista.FindAll(s => !s.TinhaDono);
		CobrarOsScripts(semDono.Count == 0,
						$"sem nenhuma instancia viva, o script das {daLista.Count} classes de efeito continua no cache do Godot -- tem dono (o tiro, a carga do raio, a estrela do embate, a poeira, o estouro, a luz de ki, a cinematica, o vulto e a esquiva do Zanzoken, a marca do alvo)",
						semDono.Count == 0 ? $"os {daLista.Count} la" : $"{semDono.Count} de {daLista.Count} fora: {string.Join(", ", semDono.Select(s => s.Tipo.Name))}");

		// ---- 2. A CARGA: o fato, nascimento a nascimento ----
		int nascimentos = daLista.Sum(s => s.Nascimentos), cargas = daLista.Sum(s => s.Nascimentos - s.ComOScriptVivo);
		CobrarOsScripts(cargas == 0,
						$"nascida sozinha, pelada e pela porta de producao, nenhuma das {daLista.Count} faz o motor carregar o script dela de novo",
						$"{cargas} carga(s) em {nascimentos} nascimento(s)");

		// ---- 3. O CRONOMETRO ----
		ScriptDaRodada pior = daLista.MaxBy(s => MedianaDe(s.NovoNatural))!;
		double oPior = MedianaDe(pior.NovoNatural);
		CobrarOsScripts(oPior <= TetoDoNascimento,
						$"o `new` de cada uma, sozinha, custa o de um node e nao o de uma carga de script",
						$"o pior, `{pior.Tipo.Name}`, {oPior:0.000} ms de mediana contra {TetoDoNascimento:0.00}; os {daLista.Count} somados, {daLista.Sum(s => MedianaDe(s.NovoNatural)):0.000} ms");

		// ---- 4. DO LOBBY AO FIM: o script nunca morreu ----
		// (as tres de cima perguntam NUM instante; esta, a corrida inteira -- o ensaio do aquecimento nasce e mata metade
		// destas classes antes de a rodada comecar a perguntar)
		List<string> morreram = [];
		foreach (ScriptDaRodada daLinha in daLista)
		{
			ScriptVigiado? vigiado = _vigiados.Find(v => v.Caminho == daLinha.Caminho);
			if (vigiado == null) morreram.Add($"{daLinha.Tipo.Name} (a sonda nao a vigia)");
			else if (vigiado.Mortes > 0) morreram.Add($"{daLinha.Tipo.Name} ({vigiado.Mortes}x)");
		}
		CobrarOsScripts(morreram.Count == 0,
						$"do lobby ao fim da corrida a sonda nao viu morrer o script de nenhuma das {daLista.Count} -- nem no ensaio do aquecimento, que nasce e mata metade delas",
						morreram.Count == 0 ? "nenhuma morte" : $"{morreram.Count} de {daLista.Count} morreram: {string.Join(", ", morreram)}");

		// ---- O CONTROLE: o mecanismo ainda existe? ----
		foreach (ScriptDaRodada fora in _scriptsDaRodada.FindAll(s => s.DeFora))
			Conferir(!fora.TinhaDono && fora.ComOScriptVivo == 0 && fora.Nascimentos > 0,
					 $"o CONTROLE -- {fora.Rotulo} (`{fora.Tipo.Name}`) -- continua sem dono, e o motor continua carregando o script dele a cada nascimento sozinho:"
					 + $" {fora.Nascimentos - fora.ComOScriptVivo} carga(s) em {fora.Nascimentos} nascimento(s), {MedianaDe(fora.NovoNatural):0.000} ms cada"
					 + $" contra {MedianaDe(fora.NovoSeguro):0.000} com a bancada segurando o script (se isto reprovar, o MOTOR mudou: a lista do aquecimento pode ter perdido a razao de ser)");

		// ---- A TABELA: pelada ----
		Nota("OS SCRIPTS, UM A UM, PELADOS -- o fonte e os metodos da classe; o `new` e o `Free` dela sozinha como o processo esta, e com a bancada segurando o script; e a diferenca, que e a CARGA:");
		double somaDasCargas = 0, somaDaPorta = 0;
		foreach (ScriptDaRodada s in _scriptsDaRodada)
		{
			double novo = MedianaDe(s.NovoNatural), livre = MedianaDe(s.FreeNatural);
			string linha = $"           {s.Tipo.Name,-20} {s.Bytes / 1024.0,4:0} KB {s.Metodos,4} metodos ({s.MetodosDoGodot,3} do Godot)"
						   + $" | {(s.TinhaDono ? "COM dono" : "SEM dono")}: new {novo,6:0.000} ms, Free {livre:0.000}";
			if (s.NovoSeguro.Count > 0)
			{
				double carga = novo - MedianaDe(s.NovoSeguro);
				if (!s.DeFora) { somaDasCargas += carga; somaDaPorta += MedianaDe(s.Carga); }
				linha += $" | a bancada segura: new {MedianaDe(s.NovoSeguro):0.000}, Free {MedianaDe(s.FreeSeguro):0.000}"
						 + $" | A CARGA {carga:0.000} ms (de {s.NovoNatural.Min() - MedianaDe(s.NovoSeguro):0.000} a {Percentil(s.NovoNatural, 0.9) - MedianaDe(s.NovoSeguro):0.000}), e {livre - MedianaDe(s.FreeSeguro):0.000} na morte do script"
						 + $" | segurar custa uma carga: {MedianaDe(s.Carga):0.000}";
			}
			linha += $"   n={s.NovoNatural.Count}   {s.Rotulo}";
			_linhas.Add(linha);
		}
		if (somaDasCargas > 0)
			Nota($"as {daLista.Count} classes de efeito somadas: {somaDasCargas:0.00} ms de carga (uma de cada); segura-las todas de uma vez custa {somaDaPorta:0.00} ms, uma vez por processo");

		// ---- A TABELA: pela porta ----
		Nota("PELA PORTA DE PRODUCAO, cada nascimento num quadro so dele, com a espera antes dele sorteada -- a chamada inteira, e a fase de script do quadro (medianas):");
		foreach (ScriptDaRodada s in _scriptsDaRodada.FindAll(x => x.NaPorta.Count > 0))
		{
			var sem = s.NaPorta.FindAll(p => !p.ComDono);
			var com = s.NaPorta.FindAll(p => p.ComDono);
			string De(List<(int Quadro, double Ms, bool ComDono)> v) =>
				v.Count == 0 ? "--"
				: $"a chamada {MedianaDe(v.ConvertAll(p => p.Ms)):0.000} ms, o script do quadro {MedianaDe(v.FindAll(p => p.Quadro + 1 < _quadros.Count).ConvertAll(p => ScriptMs(p.Quadro) - ColetorNoScript(p.Quadro))):0.00} ms (n={v.Count})";
			List<double> Scripts(List<(int Quadro, double Ms, bool ComDono)> v) =>
				v.FindAll(p => p.Quadro + 1 < _quadros.Count).ConvertAll(p => ScriptMs(p.Quadro) - ColetorNoScript(p.Quadro));
			_linhas.Add($"           {s.Tipo.Name,-20} sem dono: {De(sem)} | com dono: {De(com)}"
						+ (sem.Count > 0 && com.Count > 0
							? $" | a diferenca: {MedianaDe(sem.ConvertAll(p => p.Ms)) - MedianaDe(com.ConvertAll(p => p.Ms)):0.000} ms na chamada, {MedianaDe(Scripts(sem)) - MedianaDe(Scripts(com)):0.00} no script do quadro"
							: ""));
		}

		foreach (string linha in ContaDosScripts(Math.Max(1, _baseDe), cadaUma: false)) Nota(linha);
	}

	private static double Percentil(List<double> v, double p)
	{
		if (v.Count == 0) return 0;
		List<double> ordenada = [.. v];
		ordenada.Sort();
		return ordenada[Math.Min(ordenada.Count - 1, (int)(ordenada.Count * p))];
	}
}
