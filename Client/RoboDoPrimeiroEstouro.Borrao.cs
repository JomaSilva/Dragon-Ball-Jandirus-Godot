using Godot;

namespace Jandirus.Client;

/// <summary>
/// O PRIMEIRO BORRAO DO PROCESSO -- a peca do rastro na rodada dos "outros primeiros usos" (`--diagestouro --pecas`), a
/// regua dela, a medicao em pecas (`--borraopartido`), o ensaio do lobby ato a ato e o rotulo do borrao no gravador
/// sozinho (`--passivo`). A historia e os numeros estao no cabecalho do `RoboDoPrimeiroEstouro`, no bloco "E O PRIMEIRO
/// BORRAO"; aqui fica o que e so desta regua.
///
/// ============================ O DEFEITO ============================
/// O `BorraoDirecional` carregava o shader dele na hora do primeiro uso, e ninguem o trazia antes: o primeiro borrao do
/// processo -- a primeira investida de uma luta, ou o primeiro passo de uma corrida -- lia o `Borrao.gdshader` do disco
/// pela thread principal, criava o primeiro material dele e o desenhava no MESMO quadro.
///
/// ============================ O QUE SE MEDE ============================
/// O QUADRO EM QUE AS COPIAS DO RASTRO NASCEM, partido em tres: o SCRIPT, o PREPARO do desenho (os recursos sujos do
/// quadro: e ali que a thread principal espera um shader que ainda esta compilando) e a TELA (o desenho da janela: e
/// ali que a pipeline e montada). As duas ultimas chegam atrasadas, e cada uma com o atraso dela -- ver
/// <see cref="PreparoNoBorrao"/>.
///
/// ============================ A REGUA TEM TRES PERNAS ============================
///   1. O ARQUIVO JA ESTAVA NA MEMORIA antes do primeiro gesto -- um fato perguntado ao proprio Godot
///      (`ResourceLoader.HasCached`), e nao um tempo: ler este shader custa menos de um milissegundo, abaixo de
///      qualquer teto de quadro;
///   2. NENHUMA PIPELINE 2D NASCE no quadro do primeiro borrao, nem no seguinte -- contagem do proprio motor, que nao
///      depende do cache de ninguem;
///   3. O QUADRO CUSTA UM QUADRO NORMAL -- o teto de trabalho de todo efeito desta bancada. E a perna que o jogador
///      sente, e a unica que depende de cache: so morde com o cache de shader do Godot vazio (a thread principal espera
///      a compilacao) ou com o driver de video frio (a pipeline e montada do zero).
///
/// O DEFEITO QUE ELA GUARDA e o `Aquecimento.SemEnsaioDoBorraoDeTeste` (`--pecas --semensaiodoborrao`): o aquecimento
/// sem o shader na fila e sem o ato do ensaio, o jogo de antes. As tres pernas reprovam.
/// </summary>
public partial class RoboDoPrimeiroEstouro
{
	/// <summary>
	/// `--pecas --semensaiodoborrao`: A RODADA DE INJECAO do borrao -- o processo entra no mundo com o defeito
	/// `Aquecimento.SemEnsaioDoBorraoDeTeste` (ligado pelo `Boot` na linha em que o aquecimento nasce): o jogo de antes,
	/// em que o shader do borrao nao vinha do lobby nem era ensaiado. As tres pernas da regua tem que REPROVAR.
	/// </summary>
	private readonly bool _semEnsaioDoBorrao = Tem("--semensaiodoborrao");

	private const string DefeitoDoBorrao = "o aquecimento sem o shader e sem o ensaio do borrao";

	/// <summary>`--pecas --borraopartido`: A MEDICAO EM PECAS, sem regua. Ver <see cref="AndarNoBorrao"/>.</summary>
	private readonly bool _borraoPartido = Tem("--borraopartido");

	/// <summary>
	/// `--driverfrio Borrao`: o driver de video nunca viu o shader do borrao desta corrida. Quem salga, e quem diz no
	/// relatorio se o sal entrou, e o arquivo `RoboDoPrimeiroEstouro.Raios.cs` (<see cref="SalgarOsShaders"/>); aqui so
	/// se pergunta se o borrao esta na lista dele.
	/// </summary>
	private bool BorraoNoDriverFrio => Array.IndexOf(_shadersFrios, BorraoDirecional.CaminhoDoShader) >= 0;

	/// <summary>O que esta rodada tem de proprio do borrao, pra nota "rodada:" do relatorio.</summary>
	private string RotuloDoBorrao() =>
		(_semEnsaioDoBorrao ? $" | DEFEITO INJETADO: {DefeitoDoBorrao} (`--semensaiodoborrao`)" : "")
		+ (_borraoPartido ? " | o primeiro borrao em pecas (`--borraopartido`)" : "");

	// =====================================================================
	// O ROTEIRO DA PECA -- um gesto por segundo, depois das pecas de ki
	// =====================================================================
	/// <summary>
	/// O tamanho do salto que a peca manda o rastro reconstruir, em pixels: oito tiles. O rastro larga uma copia a cada
	/// 26 px (`RastroDeCorrida.VaoDoArranque`), entao sao dez copias do corpo -- um arranque comprido (o teto sao doze).
	/// </summary>
	private const float SaltoDoBorrao = 256f;

	/// <summary>O grupo das copias do rastro, ja como `StringName`: a pergunta e feita todo quadro, e converter o texto a cada vez seria lixo pro coletor.</summary>
	private static readonly StringName GrupoDoRastroDoBorrao = RastroDeCorrida.GrupoDoRastro;

	private List<Action<World, GameClient>>? _gestosDoBorrao;
	private int _gestoDoBorrao, _borroesPedidos, _borroesNascidos, _copiasDeRastro;
	private bool _esperandoOBorrao;

	/// <summary>
	/// O FATO DE PARTIDA: o Godot tinha o shader do borrao no cache ANTES de a bancada encostar nele? Perguntado uma vez
	/// so (`ResourceLoader.HasCached`): no primeiro gesto da peca e, no gravador sozinho, quando o corpo entra no mundo.
	/// </summary>
	private bool _borraoNaMemoria, _olheiAMemoriaDoBorrao;

	/// <summary>A bancada carregou o shader ela mesma, pra o sal do driver frio ter o que salgar. Ver <see cref="PrepararOBorrao"/>.</summary>
	private bool _carregueiOBorraoPraOSal;

	/// <summary>Os eventos desta peca, na ordem: o indice em <see cref="_eventos"/> e um nome curto pra tabela.</summary>
	private readonly List<(int Evento, string Curto)> _eventosDoBorrao = [];
	private int _eventoDoPrimeiroBorrao = -1;

	private Sprite2D? _spriteDoBorraoPartido;

	/// <summary>
	/// O PRIMEIRO BORRAO DO PROCESSO, SOZINHO NUM QUADRO -- e o segundo, um segundo depois, que e o controle (nada nele e
	/// novo). Chamado pelo <see cref="AndarNasPecas"/> uma vez por segundo, depois das pecas de ki; devolve falso quando o
	/// roteiro acabou.
	///
	/// PELA PORTA DE PRODUCAO: `RastroDeCorrida.Arranque` no node `Rastro` do corpo local -- a chamada que o
	/// `World.AoPiscar` faz quando chega o anuncio de um salto. O rastro reconstroi o trajeto (dez copias do corpo, cada
	/// camada de cada copia com um material do `Borrao.gdshader`) no primeiro quadro em que acha o corpo longe do ponto
	/// de partida; aqui a partida e um ponto oito tiles a oeste, e o corpo nao sai do lugar. NAO PASSA PELO SERVIDOR de
	/// proposito: o salto de verdade traz golpe, faisca e som no mesmo quadro, e a peca quer o custo do borrao sozinho. O
	/// jogo inteiro se confere no palco do duelo, com o gravador ao lado (`--trailer duelo --diagestouro --passivo 100`).
	///
	/// COM `--borraopartido` VEM ANTES, CADA UMA NUM QUADRO SO DELA, AS TRES PECAS DO PRIMEIRO USO -- o arquivo, o primeiro
	/// material e o primeiro desenho --, e o que sobra pro primeiro borrao e o resto: codigo rodando pela primeira vez.
	/// Nessa rodada nada e cobrado: os tres custos ja foram pagos em jogo, pela propria bancada.
	/// </summary>
	private bool AndarNoBorrao(World mundo, GameClient cli)
	{
		if (_gestosDoBorrao == null)
		{
			_gestosDoBorrao = [];
			if (_borraoPartido)
			{
				_gestosDoBorrao.Add(PecaDoArquivoDoBorrao);
				_gestosDoBorrao.Add(PecaDoMaterialDoBorrao);
				_gestosDoBorrao.Add(PecaDoSpriteDoBorrao);
			}
			_gestosDoBorrao.Add(PedirUmBorrao);
			_gestosDoBorrao.Add(PedirUmBorrao);
			_gestosDoBorrao.Add(static (_, _) => { });   // os ultimos quadros do segundo borrao
			PrepararOBorrao();
			return true;
		}

		if (_esperandoOBorrao)
		{
			Conferir(false, $"o borrao {_borroesPedidos} nasceu em um segundo (as copias do rastro entram no grupo `{RastroDeCorrida.GrupoDoRastro}`)");
			return false;
		}
		if (_gestoDoBorrao >= _gestosDoBorrao.Count) return false;
		_gestosDoBorrao[_gestoDoBorrao++](mundo, cli);
		return true;
	}

	/// <summary>
	/// O PRIMEIRO GESTO E SO O PREPARO, um segundo antes de qualquer coisa do borrao: o fato de partida (o shader esta na
	/// memoria?) e, com `--verbose` na linha, o ouvido das cargas.
	///
	/// E O CASO QUE SO A BANCADA TEM: `--driverfrio Borrao` numa rodada em que o shader NAO veio do lobby (a de injecao).
	/// O sal entra num shader ja carregado, e ai nao ha nenhum -- o jogo de antes so o carrega no quadro do primeiro
	/// borrao, e o sal chegaria depois do primeiro material. A bancada o carrega aqui, pela porta de producao: a leitura
	/// do arquivo sai do quadro medido (menos de um milissegundo; quem a cobra e a perna do fato, que ja foi anotada), e
	/// o que fica nele e o que a rodada veio medir -- o shader compilando e a pipeline montada do zero.
	/// </summary>
	private void PrepararOBorrao()
	{
		LigarAEscutaDeCarga();

		_borraoNaMemoria = ResourceLoader.HasCached(BorraoDirecional.CaminhoDoShader);
		_olheiAMemoriaDoBorrao = true;

		if (BorraoNoDriverFrio && !_borraoNaMemoria && !_borraoPartido)
		{
			_ = BorraoDirecional.Sh;
			_carregueiOBorraoPraOSal = true;
			Marcar("`--driverfrio Borrao` sem o shader na memoria: a bancada o carrega aqui, pra o sal entrar antes do primeiro borrao");
		}
	}

	private void PedirUmBorrao(World mundo, GameClient cli)
	{
		if (mundo.CorpoDeTeste(cli.LocalId) is not { } corpo || corpo.GetNodeOrNull<RastroDeCorrida>("Rastro") is not { } rastro)
		{
			Conferir(false, "o corpo local tem o node do rastro (`Rastro`) pra a peca do borrao");
			_gestoDoBorrao = int.MaxValue;
			return;
		}

		_borroesPedidos++;
		_esperandoOBorrao = true;
		rastro.Arranque(corpo.GlobalPosition - new Vector2(SaltoDoBorrao, 0));
		Marcar($"o borrao {_borroesPedidos} PEDIDO (`RastroDeCorrida.Arranque`, um salto de {SaltoDoBorrao:0} px)");
	}

	// =====================================================================
	// AS PECAS DO PRIMEIRO USO, UMA POR QUADRO (`--borraopartido`)
	// =====================================================================
	private void PecaDoArquivoDoBorrao(World mundo, GameClient cli)
	{
		ulong t0 = Time.GetTicksUsec();
		_ = BorraoDirecional.Sh;
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;
		Evento($"PECA DO BORRAO 1 de 3 -- ARQUIVO: o shader pedido pela porta de producao (`BorraoDirecional.Sh`; a chamada: {ms:0.00} ms;"
			   + $" {(_borraoNaMemoria ? "ja estava na memoria" : "LIDO DO DISCO aqui")})", Papel.Nota);
		_eventosDoBorrao.Add((_eventos.Count - 1, "PECA 1, o arquivo"));
	}

	private void PecaDoMaterialDoBorrao(World mundo, GameClient cli)
	{
		_spriteDoBorraoPartido = new Sprite2D
		{
			Name = "SpriteDoBorraoPartido",
			Texture = Fogo.Radial(LuzDeKi.RaioDaTextura),
			Position = (mundo.PosicaoLocal ?? Vector2.Zero) + new Vector2(-96, -64),
		};
		ulong t0 = Time.GetTicksUsec();
		BorraoDirecional.Aplicar(_spriteDoBorraoPartido, Vector2.Right, 1f);
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;
		Evento("PECA DO BORRAO 2 de 3 -- MATERIAL: um `ShaderMaterial` do shader num sprite FORA da arvore (`BorraoDirecional.Aplicar`: nada e"
			   + $" desenhado; sem o ensaio do lobby e o PRIMEIRO do processo, e o Godot comeca a compilar; a chamada: {ms:0.00} ms)", Papel.Nota);
		_eventosDoBorrao.Add((_eventos.Count - 1, "PECA 2, o material"));
	}

	private void PecaDoSpriteDoBorrao(World mundo, GameClient cli)
	{
		if (_spriteDoBorraoPartido == null || _palco == null) return;
		_palco.AddChild(_spriteDoBorraoPartido);
		Evento("PECA DO BORRAO 3 de 3 -- O SPRITE NA TELA: o mesmo sprite entra na arvore um segundo depois de o material nascer (o DESENHO)", Papel.Nota);
		_eventosDoBorrao.Add((_eventos.Count - 1, "PECA 3, o desenho"));
	}

	// =====================================================================
	// O FIM DE CADA QUADRO: o desenho partido, e o quadro em que as copias nascem
	// =====================================================================
	/// <summary>Quantos nascimentos de rastro o gravador sozinho rotula e parte: o primeiro que ele ve e tres de controle.</summary>
	private const int BorroesDoPassivo = 4;

	private readonly List<(int Quadro, int Copias)> _borroesDoPassivo = [];

	/// <summary>Os atos do ensaio do `Aquecimento`, como a espia os entregou: o quadro, o nome e os ms da montagem.</summary>
	private readonly List<(int Quadro, string Nome, double Ms)> _atosDoEnsaioDoBorrao = [];
	private bool _ouvindoOEnsaioDoBorrao;

	/// <summary>
	/// No fim de cada quadro, depois de todos os scripts (quem chama e o <see cref="NoFimDoQuadro"/>) -- so nas rodadas
	/// das pecas e no gravador sozinho:
	///
	///   * guarda o PREPARO e a TELA que o Godot acabou de entregar (a mesma gaveta da rodada das cenas, que nessas duas
	///     rodadas ninguem mais enche);
	///   * ve se as copias do rastro NASCERAM neste quadro: havia zero no anterior e ha alguma agora. E aqui, e nao no
	///     gesto, porque o rastro so resolve o salto no `_Process` dele -- o quadro do custo e o das copias.
	/// </summary>
	private void NoFimDoQuadroDoBorrao()
	{
		if (!_pecas && _passivo <= 0) return;

		// A ESPIA DO ENSAIO, ligada no primeiro quadro do lobby: o primeiro ato so vem depois da fila de carga.
		if (!_ouvindoOEnsaioDoBorrao)
		{
			_ouvindoOEnsaioDoBorrao = true;
			Aquecimento.EspiaoDeAto += AoEnsaiarParaOBorrao;
		}

		if (_primeiroQuadroPartido < 0) _primeiroQuadroPartido = _quadros.Count;
		_desenhoPartido.Add((RenderingServer.GetFrameSetupTimeCpu(), RenderingServer.ViewportGetMeasuredRenderTimeCpu(_tela)));

		if (_passivo > 0 && !_olheiAMemoriaDoBorrao && World.Instancia?.PosicaoLocal != null)
		{
			_olheiAMemoriaDoBorrao = true;
			_borraoNaMemoria = ResourceLoader.HasCached(BorraoDirecional.CaminhoDoShader);
		}

		int copias = GetTree().GetNodeCountInGroup(GrupoDoRastroDoBorrao);
		bool nasceram = copias > 0 && _copiasDeRastro == 0;
		_copiasDeRastro = copias;
		if (!nasceram) return;

		_borroesNascidos++;
		if (_passivo > 0)
		{
			if (_borroesNascidos > BorroesDoPassivo) return;
			_borroesDoPassivo.Add((_quadros.Count, copias));
			Marcar($"BORRAO {_borroesNascidos}: {copias} copia(s) do rastro nascem");
			return;
		}

		// (copias que a bancada nao pediu -- alguem correu ou saltou por conta propria -- nao sao a peca)
		if (!_esperandoOBorrao) return;
		_esperandoOBorrao = false;
		AnotarOBorraoDaPeca(copias);
	}

	private void AoEnsaiarParaOBorrao(string nome, double ms)
	{
		_atosDoEnsaioDoBorrao.Add((_quadros.Count, nome, ms));
		Marcar($"ENSAIO: {nome} ({ms:0.0} ms montando)");
	}

	/// <summary>A espia do ensaio sai: o evento e ESTATICO e de producao, e este node nao pode ficar pendurado nele.</summary>
	private void SoltarOBorrao()
	{
		if (!_ouvindoOEnsaioDoBorrao) return;
		_ouvindoOEnsaioDoBorrao = false;
		Aquecimento.EspiaoDeAto -= AoEnsaiarParaOBorrao;
	}

	/// <summary>
	/// AS COPIAS DE UM BORRAO PEDIDO PELA PECA NASCERAM NESTE QUADRO: e ele o evento.
	///
	/// O PAPEL: o primeiro e a regua, e o segundo o controle dela. Na rodada de injecao o primeiro tem que REPROVAR -- mas
	/// a perna do TEMPO so reprova quando ha o que esperar: com o cache de shader do Godot vazio (o de toda bancada) a
	/// thread principal espera a compilacao, e com o driver frio a pipeline e montada do zero. Com os dois quentes o
	/// defeito inteiro sao poucos milissegundos, abaixo do teto, e o quadro sai como numero; quem reprova ai sao as duas
	/// pernas de fato (<see cref="ConferirOPrimeiroBorrao"/>). Nas rodadas sem ensaio nenhum (`--semensaio`,
	/// `--semaquecimento`) e na medicao em pecas, tudo sai como numero.
	/// </summary>
	private void AnotarOBorraoDaPeca(int copias)
	{
		int camadas = GetTree().GetFirstNodeInGroup(GrupoDoRastroDoBorrao)?.GetChildCount() ?? 0;
		bool primeiro = _borroesPedidos == 1;

		Papel papel;
		string defeito = "";
		if (_borraoPartido || _semEnsaio != null) papel = Papel.Nota;
		else if (!primeiro || !_semEnsaioDoBorrao) papel = Papel.Regua;
		else if (_shadersEmDiscoNoComeco == 0 || BorraoNoDriverFrio) { papel = Papel.Injetado; defeito = DefeitoDoBorrao; }
		else papel = Papel.Nota;

		Evento(primeiro
				   ? $"o PRIMEIRO BORRAO do processo (`RastroDeCorrida.Arranque` -> `Largar`: {copias} copias do corpo, {camadas} camada(s) cada, e cada camada com um material do `Borrao.gdshader`)"
				   : $"o borrao {_borroesPedidos} (o controle: nada e novo; {copias} copias de {camadas} camada(s))",
			   papel, defeito);
		_eventosDoBorrao.Add((_eventos.Count - 1, primeiro ? "o PRIMEIRO borrao" : $"o borrao {_borroesPedidos} (o controle)"));
		if (primeiro) _eventoDoPrimeiroBorrao = _eventos.Count - 1;
	}

	// =====================================================================
	// AS CONTAS
	// =====================================================================
	/// <summary>
	/// O PREPARO e a TELA do desenho do quadro `i`, cada um com o atraso dele: o preparo (`GetFrameSetupTimeCpu`) esta
	/// na leitura do quadro SEGUINTE; a tela (`ViewportGetMeasuredRenderTimeCpu`), na de DOIS quadros depois -- ela vem
	/// junto com as marcas de tempo da placa de video, que o motor so recolhe uma volta da fila de quadros mais tarde.
	///
	/// (Conferido nas corridas de 2026-10-09: o quadro em que um primeiro uso cai so fecha a conta do relogio somando o
	/// script dele, o preparo da leitura seguinte e a tela da outra -- 10,4 + 27,6 + 1,1 num quadro de 40,2 ms.)
	/// </summary>
	private double PreparoNoBorrao(int i) => Partido(i + 1).Preparo;
	private double TelaNoBorrao(int i) => Partido(i + 2).Tela;

	private string LinhaPartidaDoBorrao(int q) =>
		$"script {ScriptMs(q) - ColetorNoScript(q),5:0.0} | preparo {PreparoNoBorrao(q),5:0.0} | tela {TelaNoBorrao(q),5:0.0} | quadro inteiro {TotalMs(q),5:0.0} ms"
		+ $" | pipelines 2D +{_quadros[q + 1].Canvas - _quadros[q].Canvas}"
		+ (ColetorNoQuadro(q) > 0.05 ? $" | coletor {ColetorNoQuadro(q):0.0}" : "");

	/// <summary>
	/// O FECHO DA PECA, no relatorio da rodada `--pecas` (quem chama e o <see cref="Relatar"/>): o ensaio ato a ato, as
	/// duas pernas de fato da regua -- a terceira, a do tempo, ja saiu com os outros eventos -- e a tabela dos quadros.
	/// </summary>
	private void ConferirOPrimeiroBorrao()
	{
		ImprimirOEnsaioDoBorrao();

		if (_eventoDoPrimeiroBorrao < 0)
		{
			Conferir(false, "a rodada chegou no PRIMEIRO BORRAO do processo (as copias do rastro nasceram)");
			return;
		}
		int q = _eventos[_eventoDoPrimeiroBorrao].Quadro;
		if (q + 3 >= _quadros.Count) { Conferir(false, "o primeiro borrao: a corrida gravou os tres quadros seguintes"); return; }

		ulong nascidas = _quadros[q + 2].Canvas - _quadros[q].Canvas;
		const string naMemoria = "o shader do PRIMEIRO BORRAO ja estava na memoria antes dele -- veio do lobby, numa thread, e a thread principal nao le o arquivo no meio da luta";
		const string semPipeline = "nenhuma pipeline 2D nasce no quadro do PRIMEIRO BORRAO, nem no seguinte -- o ensaio do lobby ja o desenhou";
		string achadoNaMemoria = (_borraoNaMemoria
									 ? "`ResourceLoader.HasCached` disse SIM antes do primeiro gesto"
									 : "`ResourceLoader.HasCached` disse NAO antes do primeiro gesto: quem le o arquivo e o primeiro borrao")
								 + (_carregueiOBorraoPraOSal ? "; depois disso a bancada o carregou, pra o sal do driver frio entrar -- a leitura nao esta no quadro medido" : "");
		string achadoDaPipeline = $"{nascidas} nascida(s); o preparo do desenho dele custou {PreparoNoBorrao(q):0.0} ms e a tela {TelaNoBorrao(q):0.0}";

		if (_borraoPartido || _semEnsaio != null)
		{
			string porque = _borraoPartido ? "nesta rodada a propria bancada paga antes, em pecas, o primeiro uso" : "nesta rodada o aquecimento nao e o do jogo";
			Nota($"{naMemoria}: {achadoNaMemoria} ({porque})");
			Nota($"{semPipeline}: {achadoDaPipeline} ({porque})");
		}
		else if (_semEnsaioDoBorrao)
		{
			Conferir(!_borraoNaMemoria, $"(defeito injetado: {DefeitoDoBorrao}) a mesma regua REPROVA: {naMemoria} ({achadoNaMemoria})");
			Conferir(nascidas > 0, $"(defeito injetado: {DefeitoDoBorrao}) a mesma regua REPROVA: {semPipeline} ({achadoDaPipeline})");
		}
		else
		{
			Conferir(_borraoNaMemoria, $"{naMemoria} ({achadoNaMemoria})");
			Conferir(nascidas == 0, $"{semPipeline} ({achadoDaPipeline})");
		}

		Nota("O BORRAO, QUADRO A QUADRO -- o SCRIPT, e o desenho partido em PREPARO (os recursos sujos do quadro: e onde a thread principal espera um shader"
			 + " que ainda esta compilando) e TELA (o desenho da janela: e onde a pipeline e montada):");
		foreach ((int evento, string curto) in _eventosDoBorrao)
		{
			int qe = _eventos[evento].Quadro;
			if (qe + 3 >= _quadros.Count) continue;
			_linhas.Add($"           {curto,-28} " + LinhaPartidaDoBorrao(qe));
		}
	}

	/// <summary>
	/// O ENSAIO DO `Aquecimento`, ATO A ATO: a montagem de cada um (as duas chamadas, o lado escuro e o iluminado do
	/// palco) e o quadro dele partido. E como se le quanto um ato pesa onde ele e pago -- no lobby, com o login de
	/// gente (`--lobbyteste`); com o de robo os atos caem nos primeiros quadros do mundo, e o quadro de cada um traz
	/// junto a montagem do mundo.
	/// </summary>
	private void ImprimirOEnsaioDoBorrao()
	{
		if (_atosDoEnsaioDoBorrao.Count == 0)
		{
			Nota("O ENSAIO, ATO A ATO: a espia nao viu ato nenhum (nesta rodada o ensaio nao roda)");
			return;
		}

		bool noLobby = _entrouNoMundo < 0 || _atosDoEnsaioDoBorrao[^1].Quadro < _entrouNoMundo;
		Nota($"O ENSAIO, ATO A ATO ({_atosDoEnsaioDoBorrao.Count} atos, {(noLobby ? "todos no LOBBY" : "nos primeiros quadros do MUNDO, junto com a montagem dele: o login foi mais rapido que o ensaio")})"
			 + " -- a montagem de cada um (as duas chamadas, o lado escuro e o iluminado), e o quadro dele partido:");
		foreach ((int q, string nome, double ms) in _atosDoEnsaioDoBorrao)
		{
			if (q + 3 >= _quadros.Count) continue;
			_linhas.Add($"           {nome,-26} montagem {ms,6:0.0} ms | o quadro: " + LinhaPartidaDoBorrao(q));
		}
	}

	/// <summary>O fecho do gravador sozinho: os primeiros borroes da cena que ele assistiu, cada um com o quadro partido.</summary>
	private void ImprimirOBorraoDoPassivo()
	{
		GD.Print($"[estouro] O BORRAO DO RASTRO: as copias nasceram do nada {_borroesNascidos} vez(es) (cada arranque, e cada comeco de corrida)"
				 + $" | o shader dele estava na memoria quando o corpo entrou no mundo? {(!_olheiAMemoriaDoBorrao ? "(o corpo nao entrou)" : _borraoNaMemoria ? "SIM" : "NAO")}");
		if (_semEnsaioDoBorrao) GD.Print($"[estouro] DEFEITO INJETADO a gravacao inteira: {DefeitoDoBorrao} (`--semensaiodoborrao`)");

		int de = Math.Max(1, _entrouNoMundo);
		for (int n = 0; n < _borroesDoPassivo.Count; n++)
		{
			(int q, int copias) = _borroesDoPassivo[n];
			if (q + 3 >= _quadros.Count) continue;
			GD.Print($"[estouro]    {(n == 0 ? "o PRIMEIRO" : $"o {n + 1}o        ")}  t={SegundosDesde(de, q),7:0.000}s  {copias,2} copia(s)  " + LinhaPartidaDoBorrao(q));
		}
	}
}
