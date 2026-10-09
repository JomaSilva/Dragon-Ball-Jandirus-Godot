using Godot;

namespace Jandirus.Client;

/// <summary>
/// A PORTA DE TODO SOM DE EFEITO E DE AMBIENTE DO CLIENTE -- e ela SEGURA o que carrega, pelo processo inteiro. Quem
/// toca um arquivo de som que nao e musica pede o fluxo aqui: `AudioDirector.Efeito`, `EfeitoNoLugar` e `Ambiente`, e
/// o laco de carga do `CargaVisual`.
///
/// (A musica tem a maquina dela, no `AudioDirector`: faixa de megabytes, lida numa thread de carga e guardada la.)
///
/// ============================ O DEFEITO QUE ELA FECHA ============================
/// Os tres pediam o arquivo ao `ResourceLoader` A CADA TOQUE, e ninguem segurava o que vinha. O cache do Godot e por
/// contagem de referencia: enquanto o som toca quem segura o fluxo e o tocador; o tocador acaba e se libera
/// (`Finished` -> `QueueFree`), e dai em diante so sobra o involucro C# que o `Load` devolveu -- lixo pro coletor do
/// .NET. Na coleta seguinte o involucro vai embora, o Godot tira o arquivo do cache, e o proximo toque do MESMO som
/// o le do disco de novo, pela thread principal, no meio do quadro.
///
/// MEDIDO em 2026-10-09 pelo gravador da `--diagestouro` (`--passivo`; a conta esta no `RoboDoPrimeiroEstouro.Sons.cs`)
/// ao lado de duas cenas do diretor do trailer, com o coletor andando sozinho -- nenhuma coleta forcada:
///
///     um DUELO de dois NPCs, 90 a 93 s de som .... 183 a 249 toques de 21 a 24 arquivos, e 4 a 8 leituras de disco no
///                                                  meio da luta (quatro corridas, num processo que hospeda o servidor):
///                                                  metade primeiras leituras, metade RELEITURAS -- o Zanzoken, o
///                                                  rasgo da investida, o pouso. Num cliente PURO olhando a mesma
///                                                  luta, 3: o coletor dele passou 3 vezes, contra 8 no anfitriao
///     um PASSEIO, oito voos em 65 s .............. 50 a 55 toques de 3 arquivos, e 12 a 13 leituras (duas corridas):
///                                                  a decolagem em 6 dos 8 voos e o pouso em 5 -- o coletor passa 5
///                                                  vezes, e o som que esta calado na hora sai da memoria
///     cada leitura ............................... 1,0 a 1,7 ms com o arquivo no cache do Windows (sons de 5 a 34 KB);
///                                                  uma em cem custou 6,3, e levou o quadro dela a 12,5 ms de
///                                                  trabalho -- mais que uma volta de um monitor de 120 Hz
///
/// E POUCO, e por isso ninguem via: um milissegundo e meio num quadro que gasta quatro. O que a porta tira e a ida ao
/// DISCO do meio da luta -- um custo que nao e do jogo, e que so e pequeno enquanto o disco responde depressa.
///
/// (A mesma leitura medida com `--verbose` na linha custa 2,4 a 4,4 ms: o motor escreve no log a cada carga. Numero
/// de custo se tira sem ele. Os "2,5 a 5,6 ms por som de beat" escritos no `Transformacao.SonsDasCenas`, no
/// `Aquecimento.ArquivosDaCena` e no cabecalho do `RoboDoPrimeiroEstouro` batem com a medida COM ele, e nao com a
/// de sem: os mesmos sons de beat, estalos e trovoes, lidos na hora em duas cinematicas de estreia, custaram 1,0 a
/// 1,4 ms cada sem `--verbose` e 2,4 a 4,2 com ele.)
///
/// DEPOIS, nas mesmas cenas e com o mesmo coletor: 203 a 238 toques no duelo e 72 no passeio, e NENHUMA leitura de som
/// no meio de nenhum dos dois. A porta vai ao `ResourceLoader` uma vez por ARQUIVO (24 e 4 vezes), 0,01 ms em cada
/// -- menos o ambiente da zona, que ela le na chegada (ver abaixo).
///
/// ============================ O REMEDIO: O SOM NAO MORRE ============================
/// Todo fluxo carregado por aqui entra num dicionario estatico e fica ate o processo acabar: o segundo toque de um
/// som nao volta ao `ResourceLoader`, com coletor ou sem ele. ESTATICO como a `FolhasPresas` e os recursos do
/// `Aquecimento`: o custo e do PROCESSO, e relogar nao deve repaga-lo.
///
/// O CUSTO e memoria que nao volta: o fluxo de cada som que o jogo chegou a tocar. MEDIDO nos arquivos importados
/// (2026-10-09): tudo o que o codigo cita hoje -- a pasta dos golpes, os sons de beat, os estalos, os trovoes, os de
/// gesto e o ambiente das sete zonas que tem um -- soma 1,8 MB, e 1,05 MB disso o `Aquecimento` ja prendia desde o
/// lobby. A porta acrescenta os 146 KB dos sons de gesto e ate 602 KB de ambiente, um por zona visitada.
///
/// A PORTA NAO ADIANTA NADA: som que ninguem trouxe antes da hora ainda e lido do disco no PRIMEIRO toque, uma vez
/// por processo. Quem traz antes, numa thread, e a fila do lobby -- os golpes, os sons de beat e os de gesto (ver
/// `Aquecimento.SonsDeGesto`). O que sobra pra primeira leitura e o AMBIENTE de cada zona, na chegada nela: 1,3 a 1,5
/// ms dentro do quadro que monta a zona, que custa de 120 a 780.
///
/// ============================ POR ISSO ELA TEM QUE SER A UNICA PORTA ============================
/// Um som carregado por fora (um `ResourceLoader.Load` solto, num quarto funil) nao tem dono e volta a ser lido do
/// disco a cada coleta. A `--diagestouro --sons` le os fontes do cliente e reprova a chamada solta.
///
/// SO A THREAD PRINCIPAL: o dicionario nao tem trava, e todo chamador e codigo de cena. E O `Load` DAQUI E CRU: um
/// caminho que ainda estivesse numa thread de carga travaria o jogo (ver o cabecalho do `Aquecimento`). Nao ha esse
/// caso hoje -- os sons que o aquecimento pede em thread estao todos na PRIMEIRA fila dele, e nada toca antes de ela
/// ser recolhida: o ensaio so comeca depois dela, e o mundo depois do `Concluir`. Som que um dia toque no lobby com
/// a fila no ar tem que ser recolhido pelo `LoadThreadedGet`, como a `FolhasPresas.Carregar` faz com o que ela mesma
/// adianta.
/// ================================================================================================
/// </summary>
public static class SonsPresos
{
	private static readonly Dictionary<string, AudioStream> _presos = new(StringComparer.Ordinal);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagestouro --sons --semsegurarossons`, ligado pelo `Boot`): a porta carrega e NAO
	/// segura, e o aquecimento nao traz os sons de gesto -- o jogo de antes, em que cada toque pedia o arquivo ao
	/// `ResourceLoader` e o som que o coletor do .NET soltava voltava do disco, pela thread principal, no toque
	/// seguinte. Sempre falso em jogo.
	///
	/// QUEM O LE: esta porta e o `Aquecimento` (a fila do `SonsDeGesto`).
	/// </summary>
	public static bool SemSegurarDeTeste;

	/// <summary>Quantos sons estao presos agora -- pra bancada e pro log.</summary>
	public static int Quantos => _presos.Count;

	/// <summary>
	/// QUANTO A THREAD PRINCIPAL JA ESPEROU POR SOM NESTA PORTA, em ms, desde que o processo abriu: a soma do tempo
	/// passado dentro do `ResourceLoader` por cada som que nao estava preso. Pra bancada (`--diagestouro --sons`): a
	/// diferenca entre dois instantes diz quanto um toque parou a tela buscando o arquivo, que e o que um teto de
	/// quadro nao separa do resto do quadro.
	/// </summary>
	public static double EsperaDeTeste { get; private set; }

	/// <summary>
	/// ESPIA DE BANCADA -- nula em jogo. Recebe (arquivo, ms dentro do `ResourceLoader`, veio do disco?) de cada som
	/// que a porta foi buscar no `ResourceLoader` -- o que nao estava preso.
	///
	/// O TERCEIRO E UM FATO, E NAO UM TEMPO: antes de buscar, a porta pergunta ao proprio Godot se o arquivo esta no
	/// cache dele (`ResourceLoader.HasCached`: outro dono o segura, e a busca e uma entrega). Fora do cache, quem o le
	/// e a thread principal, ali. So com a espia ligada: a pergunta e da bancada, e o jogo nao a paga.
	/// </summary>
	public static Action<string, double, bool>? EspiaoDeBusca;

	/// <summary>
	/// O fluxo deste caminho. A primeira chamada carrega pelo `ResourceLoader`; as seguintes devolvem o mesmo fluxo
	/// sem voltar ao motor.
	/// </summary>
	/// <returns>
	/// Nulo quando o `ResourceLoader` devolve nulo (o arquivo nao existe ou nao foi importado). A falha nao e
	/// guardada: a chamada seguinte tenta de novo, como sempre foi.
	/// </returns>
	public static AudioStream? Carregar(string caminho)
	{
		// (o jogo de antes, pra bancada: ver `SemSegurarDeTeste`)
		if (!SemSegurarDeTeste && _presos.TryGetValue(caminho, out AudioStream? preso)) return preso;

		bool doDisco = EspiaoDeBusca != null && !ResourceLoader.HasCached(caminho);
		ulong t0 = Time.GetTicksUsec();
		AudioStream? fluxo = ResourceLoader.Load<AudioStream>(caminho);
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;
		EsperaDeTeste += ms;
		EspiaoDeBusca?.Invoke(caminho, ms, doDisco);

		if (fluxo != null && !SemSegurarDeTeste) _presos[caminho] = fluxo;
		return fluxo;
	}
}
