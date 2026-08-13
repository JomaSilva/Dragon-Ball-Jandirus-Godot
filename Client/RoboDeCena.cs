using Godot;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DE OLHAR A CINEMATICA (`--diagcena`). Ela roda a cena de VERDADE, no relogio da engine,
/// e TIRA FOTO ao longo dela -- e conta, quadro a quadro, o que esta na tela.
///
/// ============================ POR QUE ELA EXISTE, SEPARADA DO `--diagforma` ============================
/// O `--diagforma` ja tranca o ROTEIRO: uma cratera por cena, no beat que assume, nenhuma poeira
/// antes dela, zero pedido a `PoeiraDeEstrago`, chao solto do inicio ao fim. Cinco mil linhas
/// verdes. Mas as quatro perguntas desta rodada sao do dono e sao sobre a TELA:
///
///   1. *"tem transformacao q estao criando a cratera no meio da cinematica"* -- a cratera
///      APARECEU no fim? Nao "o beat esta no lugar certo": apareceu.
///   2. *"o ssj1 na cinematica da primeira vez deveria fazer raios cairem durante TODA a
///      cinematica"* -- caiu raio do comeco ao fim, e na REGIAO?
///   3. *"aumente a area ... pq ta mt perto do personagem e dura mt pouco"* -- quantas pedras ha
///      na tela, e a que distancia?
///   4. *"uns quadrados marrons caindo ... TIRE esse efeito"* -- e o unico pedido que e uma
///      AUSENCIA, e ausencia e o que passa despercebido numa bancada de numero verde.
///
/// A casa ja pagou por essa diferenca duas vezes, e esta escrito nos dois lugares: *"uniform
/// escrito nao e pixel desenhado"* (`RoboDeForma.Fotografar`) e a tira da colada, que saiu verde
/// enquanto o dono via brilho cinza em camera lenta. Entao aqui nada e perguntado ao roteiro: o
/// que conta e o que esta PLANTADO na arvore e o que esta no PNG.
/// ====================================================================================================
///
/// ============================ O QUE ELA RODA, E POR QUE ESTAS TRES ============================
///   * `ssj1` -- a estreia. E a unica cena do jogo com <see cref="Cinematica.OCeuDescarrega"/>, ou
///     seja e onde as perguntas 1, 2 e 3 se respondem juntas.
///   * `legendary` -- a de RAIVA. `Catalogo.NasceDaRaiva` e verdadeiro nela, entao a cratera tem
///     que sair na folha GRANDE (`big crater.tres`) e nao na pequena. E a unica maneira de
///     distinguir "a cratera caiu na hora" de "a cratera caiu na hora e virou a outra".
///   * `ssg` -- uma DIVINA, e de proposito uma das tres que NAO tinha cratera nenhuma antes deste
///     passe (o ritual). Se o funil da cratera nao alcancasse as cenas que nunca a pediram, e aqui
///     que apareceria.
/// ==============================================================================================
///
/// COMO RODAR (precisa de JANELA -- no headless o `GetImage` volta vazio e nao ha foto nenhuma):
///     Godot --path . --host --rede 7937 --kiteste --bpteste 3000000 --diagcena \
///           --raca Saiyan --nome Zx --conta &lt;NOVA&gt;
///
/// As fotos saem em `user://cena-*.png`, uma por instante agendado, TELA INTEIRA -- e a tela
/// inteira e o enquadramento certo aqui, ao contrario das outras bancadas de foto: pedra a seis
/// tiles e raio caindo no ceu nao cabem num recorte de cabeca.
/// </summary>
public partial class RoboDeCena : Node
{
	private static GameClient? C => GameClient.Instance;

	private Node2D? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as Node2D;

	private World? Mundo => GetTree().Root.FindChild("World", true, false) as World;

	private static void Nota(string linha) => GD.Print("[cena] " + linha);

	private readonly List<string> _falhas = [];

	private void Conferir(bool ok, string oque)
	{
		Nota((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	/// <summary>Ver o cabecalho pra por que sao estas tres.</summary>
	private static readonly string[] Roteiro = ["ssj1", "legendary", "ssg"];

	private int _cenaAtual;

	/// <summary>0 preparo, 1 comeca, 2 rodando, 3 entre cenas.</summary>
	private int _estado;

	private double _espera = 2.5;
	private double _t;
	private bool _acabou;

	// ---- o que se mede DENTRO de uma cena ----
	private Transformacao? _cena;
	private double _dur, _assume;
	private readonly List<double> _agenda = [];
	private int _agendaProx;
	private int _foto;

	private int _poeiraAntes;
	private double _primeiraCratera = -1, _primeiraFumaca = -1, _ultimaPedra = -1, _primeiraPedra = -1;
	private int _pedrasPico, _craterasPico, _fumacaPico, _particulasPico;
	private float _pedraPerto = float.MaxValue, _pedraLonge;
	private int _quadros, _quadrosComPedra;
	private bool _crateraGrande;

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ESTE MUNDO E MEU? Copiado do `RoboDeOlhada:104` e pelo mesmo motivo escrito la: com a porta
		// tomada o `--host` nao vira servidor nenhum e o cliente entra no mundo DA OUTRA SESSAO -- e
		// esta bancada transforma o corpo tres vezes, com cratera e tremor de camera, na tela de quem
		// estiver jogando ali. Ha outra sessao editando este repo agora.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[cena] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo "
					  + "e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
			return;
		}

		if (C is not { Connected: true } || Corpo is null) return;

		// A AMOSTRAGEM CORRE ANTES DA MAQUINA DE PASSOS, e todo quadro: as perguntas do dono sao sobre
		// o que dura ("do inicio ao fim") e sobre o que e raro (o raio a cada 1..2 s). Uma medida so
		// nos instantes agendados nao distingue "sumiu" de "nao calhou".
		if (_estado == 2) Amostrar();

		_t += delta;
		if (_t < _espera) return;
		_t = 0;

		switch (_estado)
		{
			case 0: Preparar(); break;
			case 1: Comecar(); break;
			case 2: Fotografar(); break;
			default: Entre(); break;
		}
	}

	// =====================================================================
	// 1. PREPARAR
	// =====================================================================
	private void Preparar()
	{
		// ============================ MEIO-DIA, E ELE E O CONSERTO E NAO O ZELO ============================
		// Um dia dura 24 minutos (`Ceu.SegundosPorDia`), entao "vai anoitecer no meio da rodada" e uma
		// rodada em cada duas -- e ja produziu relatorio errado nesta casa: o `RoboDeOlhada.Preparar`
		// conta a vez em que o mesmo dourado mediu quase preto e a `--diagforma` conta a vez em que a
		// neve da nevasca foi lida como faisca. As duas coisas que estas fotos vem julgar -- raio no
		// ceu e pedra no chao -- sao pontos claros num fundo, exatamente o que a noite e a neve
		// falsificam.
		// ==============================================================================================
		C?.SendVerbo("admin_meio_dia");
		C?.SendVerbo("admin_clima", "Neblina|0.05");

		OQueOOlhoVaiJulgar();

		Nota($"  --     zoom={World.Instancia?.ZoomDeTeste}  janela={GetViewport().GetVisibleRect().Size}");
		Nota($"  --     PoeiraDeEstrago.PedidosDeTeste na largada = {PoeiraDeEstrago.PedidosDeTeste}");

		_estado = 1;
		_espera = 1.5;
	}

	/// <summary>
	/// O ENUNCIADO, ESCRITO ANTES DE QUALQUER FOTO SAIR.
	///
	/// Nao e checagem: foto nao tem legenda, e escrever a expectativa depois do resultado deixa
	/// qualquer imagem ser lida como confirmacao do que quer que ela mostre. Foi assim que a poeira
	/// de cinematica foi lida como rampa de cor por duas rodadas (`RoboDeColada.OQueOOlhoVaiJulgar`).
	/// </summary>
	private static void OQueOOlhoVaiJulgar()
	{
		Nota("  --     1. CRATERA: nas fotos de ANTES do instante de troca o chao tem que estar");
		Nota("  --        INTEIRO -- sem buraco, sem nuvem. O buraco aparece na foto do instante da");
		Nota("  --        troca e nao antes. Cratera no meio = o pedido 1 do dono continua de pe.");
		Nota("  --     2. RAIOS (so no `ssj1`): risco branco caindo do ceu, LONGE do boneco, em");
		Nota("  --        fotos do comeco, do meio E do fim. Se so houver no fim, virou pulso.");
		Nota("  --     3. PEDRAS: pedaco de rocha subindo, ESPALHADO pela tela inteira e presente em");
		Nota("  --        TODAS as fotos, inclusive na primeira. Poucas e coladas no boneco = o");
		Nota("  --        pedido 3c continua de pe.");
		Nota("  --     4. QUADRADO MARROM: NAO pode haver nenhum. Ele e escombro quadrado caindo com");
		Nota("  --        fumacinha, como se uma parede tivesse quebrado. Este e o unico pedido que e");
		Nota("  --        uma AUSENCIA -- procure por ele de proposito.");
		Nota("");
	}

	// =====================================================================
	// 2. COMECAR UMA CENA -- pelo caminho do jogo
	// =====================================================================
	/// <summary>
	/// `World.AoMudarForma` com <see cref="Jandirus.Core.Forms.DegrauDeCena.Estreia"/> e o MESMO
	/// caminho por onde o pacote do servidor entra -- e nao uma chamada a `Transformacao.Rodar` na
	/// mao. E a mesma escolha do `RoboDeForma.AIdaEAVolta` e pelo mesmo motivo: uma cena montada a
	/// mao prova que o tocador toca, nao que o jogo a toca.
	///
	/// PASSA PELA BASE ANTES, sempre: sem isso a segunda e a terceira cena receberiam `de == para`
	/// (o corpo ficou na forma anterior) e o `AoMudarForma` poderia tratar como "nada mudou".
	/// </summary>
	private void Comecar()
	{
		if (Mundo is not { } mundo || Corpo is not { } corpo) return;
		int eu = C?.LocalId ?? 0;
		if (eu == 0) { Conferir(false, "a bancada esta conectada"); return; }

		string id = Roteiro[_cenaAtual];
		if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } def
			|| Jandirus.Core.Forms.Cinematicas.Para(def) is not { } roteiro)
		{
			Conferir(false, $"a forma `{id}` tem cena");
			_estado = 3;
			return;
		}

		ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);
		mundo.AoMudarForma(eu, b, b, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.AoMudarForma(eu, b, def.IdRede, Jandirus.Core.Forms.DegrauDeCena.Estreia);

		// A CENA E ACHADA NA ARVORE e nao guardada de quem a criou: quem a cria e o `World`, la dentro.
		_cena = null;
		foreach (Node n in corpo.GetParent().GetChildren()) if (n is Transformacao tr) _cena = tr;
		if (_cena is null) { Conferir(false, $"`{id}`: nasceu cena viva no corpo"); _estado = 3; return; }

		_dur = roteiro.Segundos;

		// ============================ O INSTANTE DA TROCA E LIDO DO ROTEIRO JA FUNILADO ============================
		// `Cinematica.Beats` passa pelo funil no `init`, entao o beat que carrega `Efeito.Cratera` E o
		// que assume a forma -- por construcao, e nao por convencao. Ler daqui e o unico jeito de a
		// agenda das fotos acompanhar sozinha o dia em que um prazo do DM for recontado; um numero
		// cravado aqui fotografaria o instante errado e ninguem saberia.
		// ======================================================================================================
		_assume = _dur;
		foreach (Jandirus.Core.Forms.Beat bt in roteiro.Beats)
			if (bt.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Cratera)) { _assume = bt.Em; break; }

		MontarAgenda();

		_poeiraAntes = PoeiraDeEstrago.PedidosDeTeste;
		_primeiraCratera = _primeiraFumaca = _primeiraPedra = _ultimaPedra = -1;
		_pedrasPico = _craterasPico = _fumacaPico = _particulasPico = 0;
		_pedraPerto = float.MaxValue;
		_pedraLonge = 0;
		_quadros = _quadrosComPedra = 0;
		_crateraGrande = false;
		_raiosVistos = _fotosDeRaio = _quadrosComRaio = 0;
		_fotoDeRaioEm = 0;
		_agendaProx = 0;
		_foto = 0;

		Nota("");
		Nota($"===== `{id}` -- {_dur:0.0}s de cena, a troca aos {_assume:0.0}s, "
		   + $"ceu descarrega={roteiro.OCeuDescarrega}, chao solto={roteiro.OChaoSeSolta}, "
		   + $"cratera {(Jandirus.Core.Forms.Catalogo.NasceDaRaiva(def) ? "GRANDE (raiva)" : "pequena")}");

		_estado = 2;
		_espera = 0;
	}

	/// <summary>
	/// OS INSTANTES FOTOGRAFADOS, e eles sao DERIVADOS da cena e nao uma lista de segundos.
	///
	/// Quatro deles cercam a troca (dois antes, dois depois) porque a pergunta 1 do dono e sobre um
	/// LIMIAR: "antes tinha buraco?" so se responde com uma foto colada no instante anterior. Os
	/// outros varrem a cena pra as perguntas 2 e 3, que sao sobre DURACAO -- comeco, um terco, dois
	/// tercos, e o ultimo suspiro.
	/// </summary>
	private void MontarAgenda()
	{
		_agenda.Clear();
		double[] pedidos =
		[
			0.35,
			_assume * 0.20, _assume * 0.45, _assume * 0.70,
			_assume - 0.45, _assume - 0.15,
			_assume + 0.20, _assume + 1.10,
			_dur - 0.20,
		];
		foreach (double p in pedidos)
			if (p > 0 && p < _dur && !_agenda.Exists(x => Mathf.Abs(x - p) < 0.12)) _agenda.Add(p);
		_agenda.Sort();
	}

	// =====================================================================
	// 3. AMOSTRAR -- todo quadro, e sem perguntar nada ao roteiro
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTA PLANTADO, E NAO O QUE FOI PEDIDO ============================
	/// A cratera, a fumaca e os quadrados marrons sao contados na ARVORE (os filhos vivos do
	/// <see cref="Decalques"/>) e nao num contador de chamadas. A diferenca importa: um decalque
	/// pedido cujo `.tres` nao carregou some do `Plantar` num `return` calado, e o contador de
	/// pedidos diria "caiu a cratera" com o chao intacto na foto.
	///
	/// O TIPO SE RECUPERA PELA ARTE, que e a unica marca que o node carrega: `big crater.tres` e a
	/// grande (a de raiva), `Craters.tres` e a pequena, e a fumaca e a UNICA textura solta plantada
	/// pelo `Decalques` (`dust cloud 2018.png`). Ver `Decalques.Arte`.
	/// ================================================================================================
	/// </summary>
	private void Amostrar()
	{
		if (_cena is null || !IsInstanceValid(_cena)) return;
		double t = _cena.TempoDeTeste;
		_quadros++;

		(int crateras, int grandes, int fumaca) = ContarDecalques();
		if (crateras > 0 && _primeiraCratera < 0) _primeiraCratera = t;
		if (fumaca > 0 && _primeiraFumaca < 0) _primeiraFumaca = t;
		if (grandes > 0) _crateraGrande = true;
		_craterasPico = Math.Max(_craterasPico, crateras);
		_fumacaPico = Math.Max(_fumacaPico, fumaca);

		int pedras = _cena.PedrasVivasDeTeste;
		_pedrasPico = Math.Max(_pedrasPico, pedras);
		if (pedras > 0)
		{
			_quadrosComPedra++;
			if (_primeiraPedra < 0) _primeiraPedra = t;
			_ultimaPedra = t;
			if (Corpo is { } corpo)
				foreach (Vector2 p in _cena.PedrasDeTeste)
				{
					float d = p.DistanceTo(corpo.GlobalPosition);
					_pedraPerto = Mathf.Min(_pedraPerto, d);
					_pedraLonge = Mathf.Max(_pedraLonge, d);
				}
		}

		_particulasPico = Math.Max(_particulasPico, ContarParticulas(_cena));

		// ============================ A FOTO QUE PERSEGUE O RAIO ============================
		// A agenda de instantes NAO consegue fotografar uma descarga, e a conta diz por que: sao 17
		// numa cena de 27,4 s e cada risco fica na tela 0,333 s (`ClimaNaTela`), ou seja o ceu tem
		// raio em ~20% do tempo. Nove fotos agendadas pegariam uma ou duas por SORTE -- e a rodada
		// em que nao pegassem nenhuma nao distinguiria "o raio e raro" de "o raio nao desenha", que
		// e exatamente o cego que a rajada do `--diagforma` ja pagou uma vez.
		//
		// UM QUADRO DE ATRASO, e ele e obrigatorio: `GetTexture().GetImage()` devolve o quadro JA
		// DESENHADO, ou seja o ANTERIOR a este `_Process`. Fotografar no mesmo quadro em que o
		// contador sobe salvaria o ceu limpo de um instante antes do risco existir -- e a foto sairia
		// perfeita, provando o contrario do que se quer.
		// ==================================================================================
		// ============================ O RISCO FOI DESENHADO, OU SO O CEU ACENDEU? ============================
		// `RaiosDaEstreiaDeTeste` conta o PEDIDO. O `ClimaNaTela.Estourar` responde a esse pedido por
		// DOIS canais independentes: o clarao (que nao depende de enquadramento) e o risco em
		// zigue-zague (que so sai se o ponto passar no teste `naTela`). Uma tempestade que so
		// acendesse o ceu contaria 17 e nao teria um raio na tela -- e o log inteiro sairia verde.
		//
		// Entao o node do risco e lido da ARVORE, pelo nome, todo quadro. E o mesmo principio do resto
		// desta bancada (a cratera tambem e contada na arvore e nao no contador de plantios).
		if (Raio() is { } risco && risco.Visible) _quadrosComRaio++;

		if (_fotoDeRaioEm > 0 && t >= _fotoDeRaioEm)
		{
			_fotoDeRaioEm = 0;
			string onde = Raio() is { } n
				? $"quad em tela {n.GetGlobalTransformWithCanvas().Origin}, "
				+ $"{n.Scale * (World.Instancia?.ZoomDeTeste ?? 1)} px, visivel={n.Visible}"
				: "sem node de raio na arvore";
			Nota($"  --     t={t:00.0}s  RAIO #{_raiosVistos}: {onde}");
			Salvar(NomeDeRaio(t));
		}
		if (_cena.RaiosDaEstreiaDeTeste > _raiosVistos)
		{
			_raiosVistos = _cena.RaiosDaEstreiaDeTeste;
			// UMA A CADA CINCO, E NAO AS QUATRO PRIMEIRAS. As quatro primeiras cairiam todas nos seis
			// segundos iniciais, e a pergunta do dono e sobre "durante TODA a cinematica" -- quatro
			// fotos do comeco responderiam "comecou", que ja se sabia. Com 17 descargas na cena, uma
			// a cada cinco da comeco, meio, dois tercos e fim.
			if (_raiosVistos % 5 == 1 && _fotosDeRaio < 4) _fotoDeRaioEm = t + AtrasoDaFotoDeRaio;
		}
	}

	/// <summary>
	/// O NODE DO RISCO, achado pelo nome na arvore. Nulo enquanto o clima nao subiu.
	///
	/// Ele e filho do proprio <see cref="ClimaNaTela"/> e nao da camada de tela -- e a propriedade
	/// `RaioNoMundo` de la existe justamente pra trancar isso -- entao procura-lo pela raiz acha o
	/// node certo e um so.
	/// </summary>
	private Sprite2D? Raio() => GetTree().Root.FindChild("Raio", true, false) as Sprite2D;

	/// <summary>
	/// QUANTO A FOTO ESPERA DEPOIS DE A DESCARGA SER PEDIDA, em segundos de cena.
	///
	/// ============================ UM QUADRO DE ATRASO NAO BASTA, E A PRIMEIRA RODADA PROVOU ============================
	/// Eu fotografava no quadro seguinte ao contador subir, pelo argumento (correto) de que
	/// `GetImage` devolve o quadro JA DESENHADO. Quatro fotos sairam, as quatro com `visivel=True` e
	/// o quad bem posto na tela -- e ZERO pixel de raio dentro do proprio retangulo dele (medido:
	/// nenhum pixel com azul acima do vermelho na regiao inteira do quad). Eu quase reportei "a
	/// tempestade conta 17 e nao desenha nada".
	///
	/// O RISCO DESCE. `Raio.gdshader` tem `frente = idade/0.25` e descarta todo pixel abaixo dela:
	/// aos 0 % de vida o raio nao existe, e ele so termina de descer a 25 % da vida. Como a vida
	/// inteira e 0,333 s (`ClimaNaTela.Raio(delta)`, `idade` a 3,0/s) e o jogo roda a ~116 fps, dois
	/// quadros sao 2 % da vida -- eu estava fotografando o raio antes de ele ter descido.
	///
	/// 0,10 s = 30 % da vida: o risco ja desceu inteiro e o `brilho` ainda esta cheio (ele so comeca
	/// a cair a 55 %). E o unico ponto da vida em que a foto responde a pergunta do dono.
	/// ==============================================================================================================
	/// </summary>
	private const double AtrasoDaFotoDeRaio = 0.10;

	/// <inheritdoc cref="Amostrar"/>
	private int _raiosVistos, _fotosDeRaio, _quadrosComRaio;

	/// <summary>Tempo de cena em que a foto do raio sai. Zero = nao ha foto pendente.</summary>
	private double _fotoDeRaioEm;

	private string NomeDeRaio(double t) =>
		$"user://cena-{_cenaAtual + 1}-{Roteiro[_cenaAtual]}-RAIO{++_fotosDeRaio}-t{t:00.0}.png";

	/// <summary>Crateras (pequenas+grandes), so as grandes, e nuvens de poeira vivas na arvore.</summary>
	private static (int Crateras, int Grandes, int Fumaca) ContarDecalques()
	{
		if (Decalques.Instancia is not { } d) return (0, 0, 0);
		int cr = 0, gr = 0, fu = 0;
		foreach (Node n in d.GetChildren())
		{
			switch (n)
			{
				case AnimatedSprite2D a when a.SpriteFrames is { } f:
					string arte = f.ResourcePath;
					// `craterseries.tres` E O SULCO DO SOCO e nao a cratera -- ver `Decalques.Arte`.
					// Contar por "contem crater" pegaria o rastro de combate junto e a medida ficaria
					// alta por um motivo que nao e este.
					if (arte.EndsWith("big crater.tres", StringComparison.OrdinalIgnoreCase)) { cr++; gr++; }
					else if (arte.EndsWith("Craters.tres", StringComparison.OrdinalIgnoreCase)) cr++;
					break;
				case Sprite2D s when s.Texture is { } tex
								  && tex.ResourcePath.Contains("dust cloud", StringComparison.OrdinalIgnoreCase):
					fu++;
					break;
			}
		}
		return (cr, gr, fu);
	}

	/// <summary>
	/// EMISSOR DE PARTICULA VIVO NA ARVORE DA CINEMATICA -- deste node pra baixo, e so dele.
	///
	/// O dono ja rejeitou particula pra as pedras uma vez -- *"nas cinematicas e pra tirar o efeito
	/// de pedras levitando em particulas, ficou mt feio, prefiro usar o proprio rising rocks .png"* --
	/// e o quadrado marrom (`PoeiraDeEstrago`) tambem e emissor. Contar aqui e o que separa "o bit
	/// foi aposentado" de "nada mais emite": aposentar o bit fecha o caminho ANTIGO, este contador
	/// fecha o caminho NOVO, que e literalmente como o efeito entrou da primeira vez.
	///
	/// ============================ O ESCOPO E A CENA, E A PRIMEIRA RODADA E QUEM DIZ POR QUE ============================
	/// Isto estava varrendo `GetTree().Root` -- a arvore INTEIRA -- e reprovou as tres cenas com
	/// "2 emissores" enquanto o efeito estava perfeito. Os dois sao do jogo e nao da cinematica: o
	/// `ClimaNaTela._queda` (a neblina que esta propria bancada acabou de pedir no preparo, ou seja
	/// eu reprovava a minha propria linha) e o `RaiosDaForma._fogo`, a faisca que anda no corpo
	/// transformado -- que e um sistema separado e que o dono nunca pediu pra tirar.
	///
	/// A varredura certa e a do `RoboDeForma:10494`, e ela ja estava escrita: da cena pra baixo,
	/// recursiva. Pega o emissor exista onde existir DENTRO do tocador -- junto da aura grande, dos
	/// feixes, na raiz --, sem confundir o efeito da cena com o cenario em volta dela.
	/// ==============================================================================================================
	/// </summary>
	private static int ContarParticulas(Node raiz)
	{
		int n = raiz is GpuParticles2D ? 1 : 0;
		foreach (Node f in raiz.GetChildren()) n += ContarParticulas(f);
		return n;
	}

	// =====================================================================
	// 4. A FOTO, e o fim da cena
	// =====================================================================
	private void Fotografar()
	{
		if (_cena is null || !IsInstanceValid(_cena)) { Fechar_(); return; }
		double t = _cena.TempoDeTeste;

		if (_agendaProx < _agenda.Count && t >= _agenda[_agendaProx])
		{
			_agendaProx++;
			(int cr, int gr, int fu) = ContarDecalques();
			string nome = $"user://cena-{_cenaAtual + 1}-{Roteiro[_cenaAtual]}-{++_foto:00}-t{t:00.0}.png";
			Salvar(nome);
			Nota($"  --     t={t:00.0}s  pedras={_cena.PedrasVivasDeTeste,2}  cratera={cr}{(gr > 0 ? " (GRANDE)" : "")}"
			   + $"  poeira={fu,2}  raios={_cena.RaiosDaEstreiaDeTeste,2}"
			   + $"  quadradoMarrom={PoeiraDeEstrago.PedidosDeTeste - _poeiraAntes}"
			   + $"  -> {nome.GetFile()}");
		}

		if (t >= _dur) Fechar_();
	}

	private void Salvar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { Nota("  --     sem foto (headless nao renderiza)"); return; }
			img.SavePng(ProjectSettings.GlobalizePath(destino));
		}
		catch (Exception e) { Nota("  --     sem foto: " + e.Message); }
	}

	/// <summary>O veredito desta cena, dito em numero -- e depois a proxima.</summary>
	private void Fechar_()
	{
		string id = Roteiro[_cenaAtual];
		int quadrosMarrons = PoeiraDeEstrago.PedidosDeTeste - _poeiraAntes;

		Nota($"  --     RESUMO `{id}`: pedras pico={_pedrasPico} (alvo={_cena?.AlvoDePedrasDeTeste}), "
		   + $"tiles={_cena?.TilesDePedraDeTeste}, alcance={_cena?.AlcanceDePedraDeTeste} tiles, "
		   + $"distancia {_pedraPerto:0}..{_pedraLonge:0} px");

		// ---- 4 (a AUSENCIA) ----
		Conferir(quadrosMarrons == 0,
				 $"`{id}`: ZERO quadrado marrom -- nenhum pedido a `PoeiraDeEstrago` na cena inteira "
			   + $"({quadrosMarrons})");

		// ---- 1 (a cratera) ----
		// O LIMIAR E O INSTANTE DA TROCA COM MEIO SEGUNDO DE FOLGA, e a folga e do QUADRO e nao do
		// gosto: a amostragem roda uma vez por quadro, e o beat pode vencer entre duas amostras.
		Conferir(_primeiraCratera >= 0,
				 $"`{id}`: a cratera APARECEU na tela (t={_primeiraCratera:0.0}s)");
		Conferir(_primeiraCratera >= _assume - 0.5,
				 $"`{id}`: e ela nao apareceu ANTES da troca -- surgiu aos {_primeiraCratera:0.0}s, "
			   + $"a troca e aos {_assume:0.0}s");
		Conferir(_primeiraFumaca < 0 || _primeiraFumaca >= _assume - 0.5,
				 $"`{id}`: nenhuma nuvem de poeira antes da cratera (primeira aos {_primeiraFumaca:0.0}s)");

		bool deveSerGrande = Jandirus.Core.Forms.Catalogo.Def(id) is { } d
						  && Jandirus.Core.Forms.Catalogo.NasceDaRaiva(d);
		Conferir(_crateraGrande == deveSerGrande,
				 $"`{id}`: a folha da cratera e a {(deveSerGrande ? "GRANDE" : "pequena")} "
			   + $"({(_crateraGrande ? "big crater.tres" : "Craters.tres")})");

		// ---- 3 (as pedras) ----
		// "DO INICIO AO FIM" MEDIDO COMO COBERTURA e nao como "apareceu": uma leva unica no comeco
		// tambem daria `_primeiraPedra` baixo. O que responde o pedido do dono e a fracao de quadros
		// da cena inteira em que havia pedra na tela.
		double cobertura = _quadros == 0 ? 0 : 100.0 * _quadrosComPedra / _quadros;
		Conferir(cobertura > 95,
				 $"`{id}`: havia pedra na tela em {cobertura:0}% dos quadros da cena "
			   + $"({_primeiraPedra:0.0}s ate {_ultimaPedra:0.0}s de {_dur:0.0}s)");

		// ---- 2 (os raios) ----
		int raios = _cena?.RaiosDaEstreiaDeTeste ?? 0;
		bool deveDescarregar = Jandirus.Core.Forms.Catalogo.Def(id) is { } dd
							&& Jandirus.Core.Forms.Cinematicas.Para(dd) is { } cn && cn.OCeuDescarrega;
		Conferir(deveDescarregar ? raios > 0 : raios == 0,
				 $"`{id}`: {raios} descarga(s) do ceu -- {(deveDescarregar ? "esta cena e a estreia do SSJ1" : "esta cena nao e a estreia do SSJ1")}");

		// ============================ E A METADE QUE O CONTADOR NAO RESPONDE ============================
		// Cada risco fica na tela 0,333 s (`ClimaNaTela.Raio(delta)`, `idade` a 3,0/s). Numa cena de
		// 27,4 s com 17 descargas isso e ~5,7 s de risco, ou seja ~20% dos quadros. O piso e posto BEM
		// abaixo (5%) de proposito: ele nao existe pra medir a cadencia -- quem faz isso e o contador
		// de cima --, existe pra separar "o ceu acendeu" de "o ceu acendeu E o risco foi desenhado",
		// que sao dois canais independentes dentro do `Estourar` e so um deles se ve.
		// ==========================================================================================
		double comRisco = _quadros == 0 ? 0 : 100.0 * _quadrosComRaio / _quadros;
		if (deveDescarregar)
			Conferir(comRisco > 5,
					 $"`{id}`: o RISCO foi desenhado em {comRisco:0}% dos quadros -- nao so o clarao do ceu "
				   + $"({_quadrosComRaio} de {_quadros})");

		Conferir(_particulasPico == 0,
				 $"`{id}`: nenhum emissor de particula vivo em cena ({_particulasPico})");

		// --- proxima ---
		_cenaAtual++;
		_estado = 3;
		_espera = 1.0;
	}

	/// <summary>Volta pra base e engata a proxima -- ou fecha o log.</summary>
	private void Entre()
	{
		if (Mundo is { } mundo && C is { } c && _cenaAtual > 0)
		{
			ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);
			mundo.AoMudarForma(c.LocalId, Jandirus.Core.Forms.Catalogo.Rede(Roteiro[_cenaAtual - 1]), b,
							   Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		}

		if (_cenaAtual < Roteiro.Length)
		{
			// TRES SEGUNDOS ENTRE CENAS, e eles nao sao respiro: a cratera pequena vive 18 s e a
			// grande 40 (`Decalques.Prazo`). Emendar direto faria a cratera da cena ANTERIOR estar
			// viva na arvore quando a proxima comeca, e o `_primeiraCratera` da proxima marcaria
			// zero -- ou seja a checagem de "nao apareceu antes da troca" reprovaria por heranca.
			// Tres segundos nao apagam a cratera velha; quem a apaga e a linha abaixo.
			Decalques.Instancia?.Limpar();
			_espera = 3.0;
			_estado = 1;
			return;
		}

		_acabou = true;
		Nota("");
		Nota("===== BANCADA DE OLHAR A CINEMATICA =====");
		GD.Print(_falhas.Count == 0
			? "[cena] ===== TUDO OK ====="
			: $"[cena] ===== {_falhas.Count} FALHA(S) =====\n[cena]   " + string.Join("\n[cena]   ", _falhas));
		Nota("as fotos estao em " + ProjectSettings.GlobalizePath("user://"));
	}
}
