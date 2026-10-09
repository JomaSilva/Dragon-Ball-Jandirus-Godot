using Godot;

namespace Jandirus.Client;

/// <summary>
/// OS PRIMEIROS DESENHOS QUE AINDA NASCIAM EM JOGO (`--diagestouro`): os quadros que o inventario desta bancada
/// (`ListarAsPipelinesNascidasEmJogo`) continuava mostrando com pipeline 2D montada na hora depois de os raios da forma
/// sairem dele, e os que a leitura do motor acrescentou. A regua e a dos raios (o arquivo
/// `RoboDoPrimeiroEstouro.Raios.cs`: nenhuma pipeline nasce no quadro, e o desenho dele custa um desenho normal); aqui
/// fica quem ACHA cada quadro, e o defeito injetado de cada um.
///
/// ============================ OS QUADROS ============================
///   * o PRIMEIRO RELAMPAGO -- o primeiro quadro em que o risco do raio do `ClimaNaTela` e desenhado DENTRO da tela: um
///     `Sprite2D` com o `Raio.gdshader`, que nasce invisivel e so aparece quando um raio cai. Na rodada dos temas e a
///     tempestade da estreia do `ssj1` (`Transformacao.TocarTempestade`); na do ceu, um raio que a propria rodada faz
///     cair; em jogo, tambem o primeiro de uma tempestade natural;
///   * a PRIMEIRA CHUVA -- o primeiro quadro em que ha pingo no ar: o emissor do `ClimaNaTela` nasce desligado, e o
///     motor nao desenha particula inativa. So a rodada do ceu (`--temas --ceu`) faz uma chuva COMECAR;
///   * o PRIMEIRO CORPO ILUMINADO -- o primeiro quadro em que uma luz alcanca um boneco. Na bola de producao de NOITE e
///     a luz de ki da primeira bola, que nasce na mao de quem atira; na rodada `--temas --raioscomluz`, a luz da propria
///     bancada, acesa em cima do corpo no meio da base.
///
/// ============================ E O BALAO DE FALA, QUE FOI MEDIDO E FICOU COMO ESTAVA ============================
/// O "beat zero da estreia do `blue`" do inventario (duas pipelines em toda corrida da rodada dos temas) e a FALA da
/// cena: o `BalaoDeFala._Draw` desenha o contorno da moldura (um `DrawRect` vazado: polilinha, que pro motor e uma tira
/// de triangulos) e o rabicho (`DrawColoredPolygon`), os dois por MALHA com o shader padrao do motor -- duas pipelines
/// que so diferem das que a interface ja usa no feitio do vertice e na primitiva. O anel de mira (`MarcaDeAlvo`,
/// polilinha) e o circulo da sombra de quem voa (`SombraDeVoo`) sao os mesmos dois desenhos: quem chega primeiro numa
/// sessao -- a fala, a mira ou o voo -- monta a pipeline.
///
/// NAO CUSTA, e por isso nao ganhou ato de ensaio. MEDIDO em 2026-10-09 com o cache de shader do driver de video NOVO de
/// verdade (o quarto Godot com janela ao mesmo tempo estreia um arquivo de cache vazio -- a mesma corrida pagou 25 ms
/// no relampago sem ensaio e 4,4 s no ensaio inteiro, contra 2,0): as duas pipelines nascem no quadro da fala e o
/// desenho dele custa 0,7 ms, o mesmo de quando elas ja existem; o quadro inteiro, 10,4 ms contra os 8,3 de um quadro
/// qualquer -- 2 ms, e o teto da regua sao 12. O shader padrao o driver ja compilou pra outra malha qualquer, e uma
/// pipeline a mais dele sai quase de graca. (O sal do `--driverfrio` nao serve aqui: o codigo do shader padrao nao e
/// do projeto.) O quadro continua saindo no inventario com o rotulo dele; nao e defeito.
///
/// ============================ E A ESTRELA DO EMBATE, QUE FOI DESTRINCHADA E TAMBEM FICOU ============================
/// A primeira estrela do embate da rodada `--pecas` trazia DUAS pipelines no quadro em que nasce, com o ensaio e sem
/// ele, e so se sabia de quem era uma. A medicao `--pecas --estrelapartida` (<see cref="AndarNaEstrelaPartida"/>) poe
/// na tela em tres quadros o que o node desenha -- as pedras, as faiscas, o quad pintado -- e conta as pipelines de
/// cada um. MEDIDO em 2026-10-09 (tres corridas): nenhuma nas pedras, UMA nas faiscas, UMA no quad.
///
///   * a das FAISCAS e material gerado pelo motor (`CanvasItemMaterial`), que morre com o ultimo dono e volta: nao e
///     defeito, e a conta esta no `ListarAsPipelinesNascidasEmJogo`;
///   * a do QUAD e a pipeline do `ChoqueDeKi.gdshader` pro item SEM luz em cima -- a que o jogo usa de dia. O ensaio
///     desenha duas estrelas, uma de cada lado do palco, e as duas saem COM luz: o motor decide que um item "tem luz em
///     cima" pelo RETANGULO dele contra o da luz (Godot 4.7, `renderer_canvas_render_rd.cpp`, `_record_item_commands`:
///     `global_rect_cache.intersects(light->rect_cache)`), o quad da estrela tem 199 px de meio lado, e o do lado
///     escuro chega a x = 327,3 -- nove decimos de pixel dentro do retangulo da luz do lado iluminado, que comeca em
///     326,4. A PROVA: com o palco do ensaio alargado de 512 pra 1024 px (um rascunho que nao ficou), o ato da estrela
///     monta quatro pipelines em vez de tres e o quadro do quad sai sem nenhuma, em tres corridas de tres.
///
/// NAO CUSTA, e por isso o palco continuou com os 512 px. Com o palco como esta e o `ChoqueDeKi.gdshader` salgado
/// (`--driverfrio ChoqueDeKi`), o quadro em que a pipeline do quad nasce custou 0,5 e 0,6 ms de desenho (duas
/// corridas); e a estrela inteira da rodada `--pecas`, com as duas pipelines no mesmo quadro, 1,0 ms num arquivo de
/// cache do driver estreado no mesmo dia por rodadas de temas, que nao desenham estrela em jogo (o quarto Godot com
/// janela ao mesmo tempo). A gemea SEM luz, montada depois da iluminada, sai de graca -- pelo jeito, porque e o mesmo
/// codigo com o laco das luzes desligado. A ordem contraria e a cara: a do boneco COM luz, montada depois da sem luz,
/// custa mais de 100 ms.
/// =====================================================================================================================
/// </summary>
public partial class RoboDoPrimeiroEstouro
{
	/// <summary>
	/// `--semensaiodorelampago`: A RODADA DE INJECAO do relampago -- o defeito `Aquecimento.SemEnsaioDoRelampagoDeTeste`
	/// (ligado pelo `Boot` na linha em que o aquecimento nasce): o jogo de antes, em que ninguem desenhava o risco do
	/// raio antes da hora. A mesma regua tem que REPROVAR o quadro do primeiro relampago.
	/// </summary>
	private readonly bool _semEnsaioDoRelampago = Tem("--semensaiodorelampago");

	private const string DefeitoDoRelampago = "o aquecimento sem o ensaio do relampago";

	/// <summary>
	/// `--semensaiodocorpo`: A RODADA DE INJECAO do corpo -- o defeito `Aquecimento.SemEnsaioDoCorpoDeTeste` (ligado pelo
	/// `Boot`): o jogo de antes. A mesma regua tem que REPROVAR o quadro do primeiro corpo iluminado.
	/// </summary>
	private readonly bool _semEnsaioDoCorpo = Tem("--semensaiodocorpo");

	private const string DefeitoDoCorpo = "o aquecimento sem o ensaio do corpo";

	/// <summary>
	/// `--temas --ceu --semensaiodachuva`: A RODADA DE INJECAO da chuva -- o defeito `Aquecimento.SemEnsaioDaChuvaDeTeste`
	/// (ligado pelo `Boot`): o jogo de antes. A mesma regua tem que REPROVAR o quadro da primeira chuva.
	/// </summary>
	private readonly bool _semEnsaioDaChuva = Tem("--semensaiodachuva");

	private const string DefeitoDaChuva = "o aquecimento sem o ensaio da chuva";

	/// <summary>
	/// O `ClimaNaTela` do mundo, o risco do raio dele e o balao do corpo local: os nodes que este arquivo olha, achados
	/// uma vez, quando o corpo entra no mundo -- e procurados uma vez por segundo ate la. (Pedir um node pelo caminho em
	/// todo quadro fabrica lixo que entraria na conta do coletor que a propria bancada imprime.)
	/// </summary>
	private ClimaNaTela? _clima;
	private Sprite2D? _risco;
	private BalaoDeFala? _balao;
	private bool _nodesProcurados;

	private bool _riscoVisto, _riscoAntesDaBase, _chuvaVista, _chuvaAntesDaBase, _balaoVisto, _corpoIluminadoVisto;

	/// <summary>Quantos raios acenderam o risco inteiro FORA da tela antes do primeiro que riscou dentro dela. Ver <see cref="RiscoNaTela"/>.</summary>
	private int _riscosForaDaTela;
	private bool _riscoAcesoFora;

	/// <summary>A que altura da vida do robo o primeiro risco apareceu, em segundos.</summary>
	private double _riscoAos;

	/// <summary>A estreia em curso que tem tempestade, enquanto a rodada espera o primeiro relampago dela.</summary>
	private Transformacao? _cenaDaTempestade;

	/// <summary>A cena tinha tempestade no roteiro e nao descarregou: nao ha ceu aqui (o espaco, uma nave, a Sala do Tempo).</summary>
	private bool _semCeu;

	/// <summary>
	/// Os prazos da espera pelo primeiro relampago, em segundos desde o gesto que estreou a cena. O primeiro raio da
	/// tempestade cai entre 1,0 e 2,0 s de cena (`Cinematicas.DescargaMinima`/`DescargaMaxima`): passado
	/// <see cref="SemTempestadeAos"/> sem nenhum, a cena nao descarrega aqui. Caido um, ele pode ter caido fora da tela (o
	/// sorteio alcanca 6 tiles em volta), e o seguinte vem em 1 a 2 s: a rodada espera ate o <see cref="PrazoDoRelampago"/>.
	/// E, riscada a tela, ela ainda da <see cref="DepoisDoRelampago"/> a cena antes de corta-la, pra o corte nao dividir
	/// com o relampago os quadros que a regua olha.
	/// </summary>
	private const double SemTempestadeAos = 2.6, PrazoDoRelampago = 8.0, DepoisDoRelampago = 0.35;

	/// <summary>
	/// `--temas --ceu`: A RODADA DO CEU -- uma tempestade que comeca no MEIO do jogo, como a natural comeca: a primeira
	/// chuva do processo e, dois segundos depois, o primeiro raio dentro da tela. Ver <see cref="AndarNoCeu"/>.
	/// </summary>
	private readonly bool _rodadaDoCeu = Tem("--ceu");

	private int _passoDoCeu;

	/// <summary>
	/// `--pecas --estrelapartida`: A PRIMEIRA ESTRELA DO EMBATE EM PEDACOS, uma MEDICAO sem regua. A rodada das pecas
	/// separa em tres quadros as tres coisas que o node poe na tela -- as pedras que sobem, as faiscas e o quad pintado
	/// -- e conta as pipelines 2D que nascem em cada um. Foi ela que disse de quem sao as duas da primeira estrela: ver
	/// o cabecalho e o <see cref="AndarNaEstrelaPartida"/>.
	/// </summary>
	private readonly bool _estrelaPartida = Tem("--estrelapartida");

	private ChoqueDeKi? _estrelaDaBancada;
	private double _estrelaAos;
	private int _passoDaEstrela;

	/// <summary>O quadro em que cada pedaco da estrela partida entrou na tela: as pedras, as faiscas, o quad.</summary>
	private int _quadroDasPedras = -1, _quadroDasFaiscas = -1, _quadroDoQuad = -1;

	private static void SoltarOsDefeitosDosPrimeirosDesenhos()
	{
		ChoqueDeKi.SemPinturaDeTeste = false;
		Aquecimento.SemEnsaioDoRelampagoDeTeste = false;
		Aquecimento.SemEnsaioDoCorpoDeTeste = false;
		Aquecimento.SemEnsaioDaChuvaDeTeste = false;
	}

	/// <summary>O que esta rodada tem destes quadros -- os defeitos injetados e as rodadas proprias --, no feitio da nota "rodada:" do relatorio.</summary>
	private string NotaDosPrimeirosDesenhos() =>
		(_semEnsaioDoRelampago ? $" | DEFEITO INJETADO: {DefeitoDoRelampago} (`--semensaiodorelampago`)" : "")
		+ (_semEnsaioDoCorpo ? $" | DEFEITO INJETADO: {DefeitoDoCorpo} (`--semensaiodocorpo`)" : "")
		+ (_semEnsaioDaChuva ? $" | DEFEITO INJETADO: {DefeitoDaChuva} (`--semensaiodachuva`)" : "")
		+ (_rodadaDoCeu && _temas ? " | a rodada do CEU: uma tempestade que comeca no meio do jogo (`--ceu`)" : "")
		+ (_estrelaPartida && _pecas ? " | a primeira estrela do embate em pedacos, sem regua (`--estrelapartida`)" : "");

	/// <summary>
	/// O PAPEL DO QUADRO DA PRIMEIRA BOLA: regua, como sempre -- menos na rodada de injecao do corpo feita de NOITE
	/// (`--estouronoite --semensaiodocorpo`). La esse e o quadro em que a pipeline do boneco com luz e montada, e com o
	/// driver de video frio ele custa mais de 100 ms: a regua da bola reprovaria por uma conta que nao e a dela (e so
	/// com o driver frio, o que nem contra-exemplo seria). Quem cobra esse quadro la e a regua do corpo iluminado.
	/// </summary>
	private Papel PapelDaPrimeiraBola() => _semEnsaioDoCorpo && Tem("--estouronoite") ? Papel.Nota : PrimeiroUso();

	/// <summary>
	/// UMA ESTREIA ACABA DE NASCER, e o roteiro dela tem tempestade (hoje so a cheia do `ssj1`): a rodada passa a esperar
	/// o primeiro relampago dela antes de corta-la -- ver <see cref="EsperandoORelampago"/>. Se um raio ja riscou a tela
	/// neste processo (uma tempestade natural, na rodada de noite), nao ha primeiro relampago pra esperar.
	/// </summary>
	private void SeguirATempestadeDaEstreia(Transformacao? cena)
	{
		if (!_riscoVisto && cena is { CenaDeTeste.OCeuDescarrega: true }) _cenaDaTempestade = cena;
	}

	/// <summary>
	/// A RODADA DOS TEMAS ESPERA O PRIMEIRO RELAMPAGO DA ESTREIA QUE TEM TEMPESTADE. Ele cai entre 1,0 e 2,0 s de cena, e a
	/// rodada cortava cada estreia com um segundo e meio: em 50 corridas de 2026-10-09 ele so apareceu em 18, e a
	/// pipeline dele so saia no inventario nessas. Em jogo a cena dura 26 s, e ele cai sempre.
	///
	/// Verdadeiro enquanto a rodada tem que esperar. Chamado a cada quadro pelo <see cref="AndarNosTemas"/>, antes do
	/// gesto seguinte (o corte da cena).
	/// </summary>
	private bool EsperandoORelampago()
	{
		if (_cenaDaTempestade is not { } cena) return false;
		if (!IsInstanceValid(cena) || cena.IsQueuedForDeletion() || !cena.Rodando) { _cenaDaTempestade = null; return false; }

		if (_riscoVisto)
		{
			if (_vida - _riscoAos < DepoisDoRelampago) return true;
			_cenaDaTempestade = null;
			return false;
		}

		if (cena.RaiosDaEstreiaDeTeste == 0 && _t > SemTempestadeAos)
		{
			_semCeu = true;
			_cenaDaTempestade = null;
			return false;
		}
		// (passado o prazo a rodada segue, e quem reprova e o fecho: "a rodada chegou no quadro do primeiro relampago")
		if (_t > PrazoDoRelampago) { _cenaDaTempestade = null; return false; }
		return true;
	}

	/// <summary>
	/// ============================ A RODADA DO CEU ============================
	/// O clima natural e sorteado em blocos de seis minutos, e as outras rodadas de dia o cravam aberto: nenhuma delas ve
	/// uma chuva COMECAR. E o comeco e o que custa -- o emissor da chuva nasce com o mundo e fica desligado, o motor nao
	/// desenha particula inativa, e a pipeline do `Queda.gdshader` so e montada quando o primeiro pingo e desenhado: no
	/// meio do jogo, na primeira vez que chove (ou neva, ou venta areia) desde que o processo abriu.
	///
	/// O ROTEIRO, com a base ja medida de ceu aberto:
	///   1. a TEMPESTADE e forcada no servidor (`GameServer.CeuDoTrailer`, a manivela que o diretor do trailer usa: o
	///      pacote de clima vai pra zona e o `ClimaNaTela` liga o emissor). O quadro em que ha pingo no ar pela
	///      primeira vez e da regua;
	///   2. dois segundos depois, UM RAIO CAI A 3 TILES DO CORPO, pela mesma chamada que o pacote `S2C.Raio` do servidor
	///      faz (`Iluminacao.Raio`). O quadro em que o risco aparece e da regua -- e o do relampago, sem depender do
	///      sorteio da cinematica (os raios naturais caem de 6 a 46 tiles de alguem, quase sempre fora da tela);
	///   3. o ceu abre de novo, e a rodada fecha.
	///
	/// ELA PRECISA NASCER SEM CHUVA, E O MUNDO NAO PROMETE ISSO: o clima natural e funcao do relogio de parede, e quando o
	/// bloco da hora e de chuva o mundo ja nasce chovendo -- o primeiro pingo do processo cai na entrada, tres segundos
	/// antes de a bancada cravar o ceu, e nao ha "primeira chuva" pra medir depois. (A primeira corrida desta rodada
	/// caiu num bloco desses, as 13:13 de 2026-10-09.) Por isso a linha dela leva `--climateste Nublado`: o servidor
	/// segura um ceu SEM precipitacao em quem entra, ate a bancada assumir. Sem a flag, num dia de chuva, a rodada
	/// reprova dizendo isso.
	/// ==========================================================================
	/// </summary>
	private void AndarNoCeu(World mundo, GameClient cli)
	{
		if (S is not { } srv) return;
		switch (_passoDoCeu)
		{
			case 0:
				if (_t < 1.0) return;
				srv.CeuDoTrailer(cli.LocalId, -1, Jandirus.Core.World.TipoDeClima.Tempestade);
				Marcar("a TEMPESTADE e forcada no servidor");
				break;

			case 1:
				if (!_chuvaVista)
				{
					if (_t > 10) { Conferir(false, "a chuva da tempestade forcada comecou a cair em 10 s"); Fechar(); }
					return;
				}
				break;

			case 2:
				if (_t < 2.0) return;
				if (!_riscoVisto && mundo.GetNodeOrNull<Iluminacao>("Iluminacao") is { } luz)
				{
					luz.Raio((mundo.PosicaoLocal ?? Vector2.Zero) + new Vector2(3 * Jandirus.Core.World.ZoneCollision.TileSize, 16), 123.4f);
					Marcar("um raio cai a 3 tiles do corpo (`Iluminacao.Raio`, a chamada do pacote do servidor)");
				}
				break;

			case 3:
				if (_t < 1.5) return;
				srv.CeuDoTrailer(cli.LocalId, -1, Jandirus.Core.World.TipoDeClima.Limpo, 0.02);
				Marcar("o ceu abre de novo");
				break;

			default:
				if (_t >= 1.0) Fechar();
				return;
		}
		_passoDoCeu++;
		_t = 0;
	}

	/// <summary>
	/// A ESTRELA PARTIDA, um passo por chamada (no fim de cada quadro). O node de producao tem os dois interruptores que
	/// isto precisa: o `ChoqueDeKi.SemPinturaDeTeste`, que o deixa na arvore sem pintar o quad, e as faiscas, que sao um
	/// filho com nome. A ordem:
	///
	///   1. a estrela NASCE sem quad e com as faiscas escondidas -- no quadro dela so as pedras entram na tela;
	///   2. 0,3 s depois as FAISCAS aparecem (`CpuParticles2D`, com o material gerado `Unshaded` + `Add`);
	///   3. 0,6 s depois do nascimento o QUAD e pintado (`PintorDeKi.Quadro`, com o shader `ChoqueDeKi`).
	/// O que nasce em cada um sai no relatorio (<see cref="RelatarAEstrelaPartida"/>), sem cobrar nada.
	///
	/// O INTERRUPTOR DA PINTURA SO LIGA DEPOIS DO ENSAIO: ele e do processo, e ligado antes calaria tambem as estrelas do
	/// palco -- e a medicao deixaria de ser a do jogo.
	/// </summary>
	private void AndarNaEstrelaPartida()
	{
		switch (_passoDaEstrela)
		{
			case 0:
				if (!Aquecimento.Ensaiou && _semEnsaio == null) return;
				ChoqueDeKi.SemPinturaDeTeste = true;
				_passoDaEstrela = 1;
				return;

			case 1:
				// (a estrela e a terceira peca: antes dela nao ha o que procurar)
				if (_peca < 3 || _palco?.GetNodeOrNull<ChoqueDeKi>("ChoqueDaBancada") is not { } estrela) return;
				_estrelaDaBancada = estrela;
				_estrelaAos = _vida;
				if (estrela.GetNodeOrNull<CanvasItem>("Faiscas") is { } escondidas) escondidas.Visible = false;
				Marcar("a estrela PARTIDA nasce: sem o quad e com as faiscas escondidas (so as pedras entram na tela)");
				_quadroDasPedras = _quadros.Count;
				_passoDaEstrela = 2;
				return;

			case 2:
				if (_vida - _estrelaAos < 0.3) return;
				if (_estrelaDaBancada is { } comFaiscas && IsInstanceValid(comFaiscas)
					&& comFaiscas.GetNodeOrNull<CanvasItem>("Faiscas") is { } faiscas) faiscas.Visible = true;
				Marcar("a estrela PARTIDA: as FAISCAS aparecem (`CpuParticles2D`, material gerado `Unshaded` + `Add`)");
				_quadroDasFaiscas = _quadros.Count;
				_passoDaEstrela = 3;
				return;

			case 3:
				if (_vida - _estrelaAos < 0.6) return;
				ChoqueDeKi.SemPinturaDeTeste = false;
				if (_estrelaDaBancada is { } pintada && IsInstanceValid(pintada)) pintada.QueueRedraw();
				Marcar("a estrela PARTIDA: o QUAD e pintado (`PintorDeKi.Quadro`, shader `ChoqueDeKi`)");
				_quadroDoQuad = _quadros.Count;
				_passoDaEstrela = 4;
				return;
		}
	}

	/// <summary>
	/// O QUE NASCEU EM CADA PEDACO DA ESTRELA PARTIDA, no fecho da rodada: as pipelines 2D que o motor montou no quadro
	/// do pedaco e no seguinte, e o desenho dele. So numero -- o que cada um quer dizer esta no cabecalho.
	/// </summary>
	private void RelatarAEstrelaPartida()
	{
		if (_passoDaEstrela < 4) { Conferir(false, "`--estrelapartida`: a rodada chegou no quadro em que o QUAD da estrela e pintado"); return; }

		Nota("a primeira estrela do embate EM PEDACOS (`--estrelapartida`), sem regua -- as pipelines 2D que nascem em cada pedaco (no quadro dele e no seguinte) e o desenho do quadro:");
		foreach ((string pedaco, int q) in new[] { ("as pedras", _quadroDasPedras), ("as faiscas (material gerado pelo motor)", _quadroDasFaiscas), ("o quad pintado (shader `ChoqueDeKi`)", _quadroDoQuad) })
		{
			if (q < 0 || q + 3 >= _quadros.Count) { Conferir(false, $"`--estrelapartida`, {pedaco}: a corrida gravou os 3 quadros seguintes"); continue; }
			Nota($"    {pedaco}: +{_quadros[q + 2].Canvas - _quadros[q].Canvas} pipeline(s) 2D, desenho {DesenhoMs(q + 1):0.0} ms, quadro inteiro {TotalLiquido(q):0.0} ms");
		}
	}

	/// <summary>
	/// NO FIM DE CADA QUADRO, depois de o mundo e a cena rodarem: o risco do raio apareceu? ha pingo no ar? a primeira
	/// luz de ki? O quadro em que cada um aparece pela primeira vez e um quadro da regua. (O balao do corpo local so
	/// ganha rotulo: ver o cabecalho.)
	///
	/// SO VALE COMO QUADRO DA REGUA DEPOIS DE A BASE COMECAR: com o login de robo o ensaio do lobby roda nos primeiros
	/// quadros do mundo, e um raio de tempestade natural que caisse ali cairia antes de o ato dele rodar. O que
	/// aparece antes da base fica so anotado.
	/// </summary>
	private void OlharOsPrimeirosDesenhos()
	{
		if (_estrelaPartida && _pecas) AndarNaEstrelaPartida();

		if (_entrouNoMundo < 0 || World.Instancia is not { } mundo) return;
		// (uma procura por segundo enquanto faltar algum, e nao uma por quadro: ver os campos)
		if (!_nodesProcurados && _quadros.Count % 60 == 0)
		{
			if (C is not { } cli || mundo.CorpoDeTeste(cli.LocalId) is not { } corpo) return;
			_clima ??= mundo.GetNodeOrNull<ClimaNaTela>("Iluminacao/Clima");
			_risco ??= _clima?.GetNodeOrNull<Sprite2D>("Raio");
			_balao ??= corpo.GetNodeOrNull<BalaoDeFala>("Balao");
			_nodesProcurados = _risco != null && _balao != null;
		}

		// (so conta o risco que tem pedaco DENTRO da tela: ver `RiscoNaTela`. O que acende inteiro fora dela ganha rotulo,
		// uma vez por raio -- o risco fica aceso um terco de segundo)
		bool aceso = !_riscoVisto && _risco != null && IsInstanceValid(_risco) && _risco.Visible;
		bool fora = aceso && !RiscoNaTela(_risco!);
		if (fora && !_riscoAcesoFora)
		{
			_riscosForaDaTela++;
			Marcar("um raio caiu com o risco inteiro FORA da tela: o motor nao o desenha, e a rodada espera o seguinte");
		}
		_riscoAcesoFora = fora;
		if (aceso && !fora)
		{
			_riscoVisto = true;
			_riscoAos = _vida;
			if (_baseDe < 0)
			{
				_riscoAntesDaBase = true;
				Marcar("um relampago riscou a tela ANTES de a base comecar");
			}
			else
			{
				Marcar("o primeiro relampago do processo risca a tela");
				_primeirosDesenhos.Add((_quadros.Count, "o PRIMEIRO RELAMPAGO do processo (`Iluminacao.Raio` -> `ClimaNaTela.Estourar`: o risco, um sprite com o `Raio.gdshader`)", AtoDoEnsaio.Relampago));
			}
		}

		// (o emissor da chuva liga no `ClimaNaTela.Aplicar` deste quadro; os pingos sao processados no desenho dele e
		// desenhados a partir do seguinte -- por isso a regua da chuva olha dois quadros a mais: ver `QuadrosAMais`)
		if (!_chuvaVista && _clima is { } clima && IsInstanceValid(clima) && clima.PingosVivos > 0)
		{
			_chuvaVista = true;
			if (_baseDe < 0) _chuvaAntesDaBase = true;
			else
			{
				Marcar("a primeira chuva do processo comeca a cair");
				_primeirosDesenhos.Add((_quadros.Count, "a PRIMEIRA CHUVA do processo (`ClimaNaTela.Aplicar` liga o emissor: os pingos sao particula, com o `Queda.gdshader`)", AtoDoEnsaio.Chuva));
			}
		}

		// (so o rotulo, pra o inventario dizer de quem sao as duas pipelines desse quadro: ver o cabecalho)
		if (!_balaoVisto && _balao is { } balao && IsInstanceValid(balao) && balao.Visible)
		{
			_balaoVisto = true;
			Marcar("o primeiro balao de fala do processo (duas pipelines do shader padrao do motor, por malha: nao e defeito)");
		}

		// (a luz de ki do palco do ensaio -- que so existe com o defeito `--estrelacomluz` -- nao e de corpo nenhum, e
		// some com o palco antes de a base comecar)
		if (!_corpoIluminadoVisto && _baseDe >= 0 && LuzDeKi.Acesas > 0)
			AnotarOPrimeiroCorpoIluminado("a primeira luz de ki do processo, que nasce na mao de quem atira e so com o mundo escuro");
	}

	/// <summary>
	/// O RISCO TEM PEDACO DENTRO DA TELA? O `ClimaNaTela.Estourar` acende o risco de todo raio que cai PERTO da tela -- com
	/// folga de lado, e ate nove decimos de tela acima e abaixo do centro --, e o risco sobe do ponto em que o raio cai: o
	/// raio que cai acima da borda de cima acende um risco inteiro fora dela. O motor nao desenha item fora da tela, e sem
	/// desenho nao ha pipeline: esse quadro passaria pela regua sem provar nada, e na rodada de injecao a reprovacao
	/// faltaria. (Achado em 2026-10-09: numa corrida de tres, ainda sem o conserto, o "primeiro relampago" saiu com
	/// zero pipeline. A tempestade da estreia sorteia os raios ate 6 tiles em volta do corpo, e a janela da bancada,
	/// no zoom padrao, so tem 3,75 acima dele.)
	///
	/// A conta e a do proprio `Estourar`: o pedaco de mundo que a camera cobre, contra o retangulo do sprite (a textura
	/// dele tem 1 px, entao a escala E o tamanho, e ele nao e centrado).
	/// </summary>
	private static bool RiscoNaTela(Sprite2D risco)
	{
		if (risco.GetViewport() is not { } tela) return false;
		Camera2D? camera = tela.GetCamera2D();
		Vector2 mundo = camera != null ? tela.GetVisibleRect().Size / camera.Zoom : tela.GetVisibleRect().Size;
		Vector2 centro = camera?.GetScreenCenterPosition() ?? mundo * 0.5f;
		return new Rect2(centro - mundo * 0.5f, mundo).Intersects(new Rect2(risco.GlobalPosition, risco.Scale));
	}

	/// <summary>UMA LUZ ACABA DE ALCANCAR O CORPO LOCAL pela primeira vez no processo: o quadro e da regua.</summary>
	private void AnotarOPrimeiroCorpoIluminado(string quem)
	{
		if (_corpoIluminadoVisto) return;
		_corpoIluminadoVisto = true;
		Marcar("o primeiro corpo iluminado do processo: " + quem);
		if (_baseDe >= 0)
			_primeirosDesenhos.Add((_quadros.Count, $"o PRIMEIRO CORPO ILUMINADO do processo ({quem}: o motor desenha o item que tem luz em cima com outra pipeline do mesmo shader, e a do boneco e do `Personagem.gdshader`)", AtoDoEnsaio.Corpo));
	}

	/// <summary>
	/// A RODADA CHEGOU NOS QUADROS DELA? Sem isto, um quadro que a rodada nunca alcancasse passaria pela regua por nao
	/// estar na lista. Cada rodada deve os seus: a dos temas, o relampago; a do ceu, a chuva e o relampago; a da bola de
	/// noite e a `--raioscomluz`, o corpo iluminado; a `--estrelapartida`, o quad.
	/// </summary>
	private void ConferirQueARodadaChegouNosPrimeirosDesenhos()
	{
		if (_riscosForaDaTela > 0)
			Nota($"{_riscosForaDaTela} raio(s) cairam com o risco inteiro fora da tela antes do primeiro que riscou dentro dela: nao sao desenhados, e a rodada esperou");

		if (_temas && _rodadaDoCeu)
		{
			if (_chuvaAntesDaBase) Conferir(false, "a rodada do ceu comecou sem chuva (ja chovia antes de a base comecar: e o clima natural deste instante -- rode-a com `--climateste Nublado`, que segura o ceu sem chuva ate a base)");
			else if (!_chuvaVista) Conferir(false, "a rodada do ceu chegou no quadro da PRIMEIRA CHUVA (a tempestade forcada ligando o emissor)");
			if (_riscoAntesDaBase || !_riscoVisto) Conferir(false, "a rodada do ceu chegou no quadro do PRIMEIRO RELAMPAGO (o raio que ela mesma faz cair a 3 tiles do corpo)");
		}
		else if (_temas)
		{
			// (os dois jeitos de a rodada nao ter o quadro do relampago sem ser defeito dela. NA RODADA DE INJECAO a falta
			// reprova: sem o quadro ela fecharia em TUDO OK sem ter reprovado nada)
			string? semQuadro = _riscoAntesDaBase ? "um relampago de tempestade natural riscou a tela antes de a base comecar: o primeiro do processo nao foi o da cena, e nao ha quadro dele pra cobrar"
				: _semCeu ? "a estreia que tem tempestade no roteiro nao descarregou aqui (nao ha ceu neste lugar): nao ha quadro do primeiro relampago"
				: null;
			if (semQuadro != null && _semEnsaioDoRelampago) Conferir(false, $"a rodada de injecao do relampago chegou no quadro dele ({semQuadro} -- rode de novo)");
			else if (semQuadro != null) Nota(semQuadro);
			else if (!_riscoVisto) Conferir(false, "a rodada chegou no quadro do PRIMEIRO RELAMPAGO (a tempestade da estreia do `ssj1` riscando a tela)");
			if (_raiosComLuz && _quadroDaLuz < 0) Conferir(false, "`--raioscomluz`: a luz em cima do corpo acendeu");
		}
		else if (_pecas)
		{
			if (_estrelaPartida) RelatarAEstrelaPartida();
		}
		else if (!_sons && Tem("--estouronoite") && !_corpoIluminadoVisto)
			Conferir(false, "a rodada de NOITE chegou no quadro do PRIMEIRO CORPO ILUMINADO (a luz de ki da primeira bola)");
	}
}
