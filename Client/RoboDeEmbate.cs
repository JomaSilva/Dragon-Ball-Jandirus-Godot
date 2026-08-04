using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO ZANZO CLASH (`--diagclash`).
///
/// ============================ O QUE SO O TESTE RESPONDE ============================
/// O embate acontece com os dois corpos INVISIVEIS, dura menos de sete segundos e depende de dois
/// jogadores se acertarem na mesma fracao de segundo. Olhando, ve-se o chao rachar e uma letra
/// piscar -- e nada disso diz:
///   * o gatilho e mesmo a troca SIMULTANEA (e nao "socou duas vezes")?
///   * a letra errada custa a vez, ou da pra martelar o teclado?
///   * a vantagem de poder chega aos pontos, e no lado certo?
///   * o corpo fica preso durante e SOLTO depois?
///   * quem venceu aparece atras do outro e o golpe de saida sai?
/// ==================================================================================
///
/// COMO RODAR -- sao DOIS processos, porque o embate precisa de dois lutadores:
///
///   servidor + bancada (host):
///     Godot --path . --host --clashteste --skillteste /datum/skill/ki/Afterimage \
///           --diagclash --nome Ame --conta ame
///   o adversario:
///     Godot --path . --socar --nome Bue --conta bue
///
/// O `--clashteste` tira o `prob(50)` (uma bancada que so acontece metade das vezes nao mede) e
/// da um empurrao de BP ao HOST, pra a vantagem de poder ter o que multiplicar.
///
/// UM AVISO ESPERADO no console: com os dois processos na mesma maquina, o servidor ve DUAS contas
/// chegando por endereco local e DESLIGA o admin por endereco (a guarda contra tunel/proxy do lote
/// de administracao). E o sistema funcionando -- os verbs de admin param de valer no meio do teste,
/// e nenhuma conferencia daqui depende deles.
/// </summary>
public partial class RoboDeEmbate : Node
{
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou, _viuComeco, _viuFim;
	private int _letras, _acertos, _baques;
	private char _pedida;
	private double _pontosMeus, _pontosDele;
	private bool _erreiDeProposito;
	private double _espera;
	private Vector2 _ondeEuEstava;
	private float _maiorPasso;
	private double _fotoEm;
	private readonly List<Vector2> _ondeCruzou = [];
	private readonly HashSet<(int, int)> _celulasCaidas = [];
	private int _vislumbres, _sumicos;
	private bool _aVista;
	private float _meuMult = 1;
	private int _duracaoMs;
	private bool _prepareiABriga;

	private static GameClient? C => GameClient.Instance;

	/// <summary>Salva a tela, se houver renderizador. No headless o `GetImage` volta vazio.</summary>
	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add("  ok     foto salva em " + caminho);
		}
		catch (Exception e) { _passos.Add("  --     sem foto: " + e.Message); }
	}

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready()
	{
		if (C is not { } cli) return;
		cli.ClashComecou += Comecou;
		cli.ClashTeclaPedida += Pediu;
		cli.ClashPlacar += Placar;
		cli.ClashBaque += onde => { _baques++; _ondeCruzou.Add(new Vector2(onde.X, onde.Y)); };

		// O ESTRAGO NO CHAO, celula por celula. E a unica leitura de "o cenario caiu MESMO" que o
		// cliente tem, e ela vem por um pacote proprio (`S2C.Cenario`), nao pelo baque.
		cli.CenarioCaiu += (cx, cy) => _celulasCaidas.Add((cx, cy));

		cli.ClashVislumbre += aparece =>
		{
			if (aparece) _vislumbres++; else _sumicos++;
			_aVista = aparece;
		};
		cli.ClashAcabou += Acabou;
		GD.Print("[embate] esperando um adversario que saiba sumir...");
	}

	public override void _ExitTree()
	{
		if (C is not { } cli) return;
		cli.ClashComecou -= Comecou;
		cli.ClashTeclaPedida -= Pediu;
		cli.ClashPlacar -= Placar;
		cli.ClashAcabou -= Acabou;
	}

	// =====================================================================
	// OS MOMENTOS
	// =====================================================================
	private void Comecou(int a, int b, int ms, float meu, float dele)
	{
		if (C is not { } cli) return;
		if (cli.LocalId != a && cli.LocalId != b) return;   // embate dos outros nao me diz respeito

		_viuComeco = true;
		_meuMult = meu;
		_duracaoMs = ms;

		// A VANTAGEM CHEGA, e chega COERENTE: um dos dois vale 1 e o outro vale a razao dos BPs.
		// Nao da pra fixar o numero aqui (as contas da bancada acumulam BP entre rodadas), mas da
		// pra exigir a FORMA da regra -- que e exatamente o que o dono pediu.
		Conferir(meu >= 1 && dele >= 1 && (Math.Abs(meu - 1) < 0.001 || Math.Abs(dele - 1) < 0.001),
			$"a vantagem tem a forma da regra: eu x{meu:0.##}, ele x{dele:0.##} (um dos dois vale 1)");
		_ondeEuEstava = World.Instancia?.PosicaoLocal ?? default;

		Conferir(true, $"o embate COMECOU (contra {(cli.LocalId == a ? b : a)})");

		// A DURACAO E SORTEADA: 6 a 9 cruzamentos de 500 a 700 ms cada (`blinks = rand(6,9)` e
		// `sleep(rand(5,7))` do DM). Fora dessa faixa, alguma das duas contas se perdeu.
		Conferir(ms >= 6 * 500 && ms <= 9 * 700,
			$"a duracao cai na faixa do DM: {ms} ms (6 a 9 cruzamentos de 500-700)");

		Conferir(cli.EmClash, "o cliente sabe que esta preso no embate (EmClash)");
	}

	private void Pediu(char c, int ms)
	{
		_letras++;
		_pedida = c;

		// UMA FOTO NO MEIO DO EMBATE. O quick time event, a barra de cabo de guerra, o anel do prazo
		// e as raias de velocidade so existem na TELA -- nenhuma conferencia acima olha pra eles.
		// Na terceira letra a cena ja esta montada e o placar ja tem numero.
		//
		// COM ATRASO, e nao aqui: neste instante o `_Process` da tela ainda nao rodou com a letra
		// nova, e a foto sairia no unico quadro em que a letra anterior ja sumiu e a nova ainda nao
		// apareceu. Foi o que aconteceu na primeira tentativa.
		if (_letras == 3) _fotoEm = 0.25;
		if (_letras == 1)
			Conferir(c >= 'A' && c <= 'Z' && "WASD".IndexOf(c) < 0,
				$"a letra e uma LETRA e nao e de andar: '{c}'");

		// A PRIMEIRA VEZ EU ERRO DE PROPOSITO. E a unica maneira de provar que martelar o teclado
		// nao vence o quick time event: se a letra errada nao custasse a vez, bastaria varrer o
		// alfabeto que uma hora sai a certa.
		_erreiDeProposito = _letras == 1;
		_espera = 0.08;
	}

	private void Placar(float meus, float dele)
	{
		double ganho = meus - _pontosMeus;
		_pontosMeus = meus;
		_pontosDele = dele;
		_acertos++;

		// A VANTAGEM SAI DO ANUNCIO E CHEGA AO PONTO. O servidor disse quanto vale cada acerto meu
		// no `Comecou`; aqui se confere que o placar paga exatamente isso -- que e a diferenca entre
		// a regra estar ESCRITA e a regra estar LIGADA.
		if (_acertos == 1)
			Conferir(Math.Abs(ganho - _meuMult) < 0.01,
				$"cada acerto meu paga o que foi anunciado: {ganho:0.##} (anunciado x{_meuMult:0.##})");
	}

	private void Acabou(int venc, int perd)
	{
		if (C is not { } cli) return;
		if (cli.LocalId != venc && cli.LocalId != perd) return;

		_viuFim = true;
		Conferir(!cli.EmClash, "o embate acabou e o corpo foi SOLTO (EmClash caiu)");
		Conferir(venc == cli.LocalId,
			$"venceu quem acertou mais teclas -- eu ({_pontosMeus:0.#} a {_pontosDele:0.#})");
		Conferir(_baques >= 5, $"os cruzamentos foram anunciados a zona: {_baques} baque(s)");

		// ============================ O ENCONTRO ANDA ============================
		// O dono pegou isto olhando: "o chao so ta quebrando e as particulas de destruiçao so estao
		// no local onde o clash começou". A causa era o ponto de encontro FIXO -- os dois trocavam
		// de lado em volta do mesmo centro, entao os nove baques caiam no mesmo tile e o `RacharChao`
		// nao tinha mais o que rachar depois do primeiro.
		//
		// Duas medidas, porque sao duas coisas: os BAQUES espalhados (o efeito na tela) e as CELULAS
		// distintas derrubadas (o estrago no mapa).
		// =========================================================================
		float maiorVao = 0;
		for (int i = 1; i < _ondeCruzou.Count; i++)
			maiorVao = Math.Max(maiorVao, _ondeCruzou[i].DistanceTo(_ondeCruzou[0]));
		Conferir(maiorVao > 64,
			$"os cruzamentos acontecem em lugares DIFERENTES: {maiorVao:0} px entre o 1o e o mais longe");

		Conferir(_celulasCaidas.Count >= 3,
			$"o estrago deixa TRILHA: {_celulasCaidas.Count} celula(s) distintas derrubadas");

		// ============================ O VISLUMBRE ============================
		// "As vezes" quer dizer duas coisas, e as duas se medem: aconteceu ALGUMA vez, e NAO
		// aconteceu em todas. Com 40% por cruzamento e 6 a 9 cruzamentos, os dois extremos sao raros
		// o bastante (~5% e ~0,2%) pra a bancada valer -- e se cair, cai mostrando o numero.
		Conferir(_vislumbres >= 1,
			$"os dois APARECERAM em algum cruzamento: {_vislumbres} de {_baques}");
		Conferir(_vislumbres < _baques,
			$"e continuaram invisiveis nos outros: {_baques - _vislumbres} cruzamento(s) sem aparecer");

		// E NINGUEM FICA A VISTA POR ENGANO. Cada aparecimento tem o seu sumico -- senao o embate
		// terminaria com um corpo visivel que o servidor acha que esta escondido.
		Conferir(_vislumbres == _sumicos && !_aVista,
			$"todo vislumbre fechou: {_vislumbres} apareceu / {_sumicos} sumiu");
		Conferir(_maiorPasso > 32,
			$"quem moveu o corpo foi o SERVIDOR: {_maiorPasso:0} px sem um unico pedido de passo");
		Conferir(_letras >= 3, $"o quick time event pediu {_letras} letra(s)");

		// ERRAR CUSTA A VEZ: pedi `_letras` e acertei `_acertos`, e a PRIMEIRA eu errei de proposito.
		// Se martelar valesse, os dois numeros seriam iguais. A folga de dois e a ultima letra, que
		// pode nascer com o embate ja acabando e nunca chegar a ser respondida.
		Conferir(_acertos < _letras && _acertos >= _letras - 2,
			$"a letra ERRADA custou a vez: {_letras} pedidas, {_acertos} pontuadas");

		// A CADENCIA. Com janela fixa o placar tem TETO -- e o teto e o que impede um cliente
		// automatico de ganhar por velocidade de digitacao. Uma letra por 900 ms, com folga.
		Conferir(_letras <= _duracaoMs / 900.0 + 2,
			$"o quick time event tem CADENCIA: {_letras} letra(s) em {_duracaoMs} ms (uma por 900 ms)");
	}

	// =====================================================================
	// O QUADRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou || C is not { Connected: true } cli) return;

		// SEM LETALIDADE. O embate exige que NENHUM dos dois esteja morto, e dois robos socando pra
		// valer resolvem isso em vinte segundos -- a primeira rodada desta bancada terminou com o
		// adversario morto no chao e zero embates. A briga aqui e so o meio; o que se mede e o
		// encontro.
		if (!_prepareiABriga) { _prepareiABriga = true; cli.SendLethal(false); }

		if (_fotoEm > 0 && (_fotoEm -= delta) <= 0) Fotografar("user://embate.png");

		// A RESPOSTA DO QUICK TIME EVENT, com um respiro pra nao parecer robo instantaneo (e pra
		// dar tempo do `Placar` do acerto anterior chegar antes do proximo).
		if (_espera > 0)
		{
			_espera -= delta;
			if (_espera <= 0 && _pedida != '\0')
			{
				// ERRA DE PROPOSITO NA PRIMEIRA: manda uma letra que garantidamente nao e a pedida.
				// Dali em diante a vez esta PERDIDA -- o servidor manda uma letra nova, e e nela que
				// se responde. Reusar a letra velha seria responder a pergunta anterior.
				char manda = _erreiDeProposito ? (_pedida == 'Z' ? 'Y' : 'Z') : _pedida;
				cli.SendClashTecla(manda);
				_erreiDeProposito = false;
				_pedida = '\0';
			}
		}

		// O CORPO ESTA PRESO? Guarda o MAIOR deslocamento do embate inteiro, e nao a distancia num
		// instante: os cruzamentos sao sorteados em volta do ponto de encontro, e um deles pode cair
		// perto de onde o corpo comecou. A primeira versao media a 1,2 s e reprovava por azar.
		//
		// O que se prova e que o corpo SAIU DO LUGAR sem que o cliente tenha pedido passo nenhum --
		// durante o embate quem escreve a posicao e o servidor.
		if (cli.EmClash && _viuComeco && World.Instancia?.PosicaoLocal is { } agora)
			_maiorPasso = Math.Max(_maiorPasso, agora.DistanceTo(_ondeEuEstava));

		if (!_viuFim) return;

		// deixa o golpe de saida e o arremesso acontecerem antes de fechar o relatorio
		_espera -= delta;
		if (_espera > -1.5) return;

		_acabou = true;
		Conferir(_viuComeco && _viuFim, "a cena inteira chegou: comeco, letras, baques e fim");
		GD.Print("\n[embate] ===== BANCADA DO ZANZO CLASH =====");
		foreach (string l in _passos) GD.Print("[embate] " + l);
		GD.Print(_falhas.Count == 0
			? "[embate] ===== TUDO OK ====="
			: $"[embate] ===== {_falhas.Count} FALHA(S) =====\n[embate]   " + string.Join("\n[embate]   ", _falhas));
	}
}
