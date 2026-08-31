using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ O CHAO DE UM PLANETA MORRENDO, FOTOGRAFADO (`--diagchao`) ============================
/// A irma da `--diagagonia`. Aquela mede o planeta visto **DO ESPACO** (a crosta de magma, a
/// rachadura, o estouro) num laboratorio sem rede; esta mede o que o dono pediu **DE DENTRO**:
/// *"o planeta vai comecar a tremer, e varios efeitos climaticos como raios etc irao comecar,
/// explosoes e crateras aparecendo, pedras levitando pelo mapa todo... quanto mais perto ta de
/// explodir, mais intenso esses efeitos ficam"*.
///
/// E ela responde a pergunta que nenhuma bancada deste sistema respondia: **quanto isso custa**.
///
/// ============================ POR QUE ELA PRECISA DE SERVIDOR DE VERDADE ============================
/// Porque quatro dos cinco efeitos do chao **nao sao decisao do cliente**: o ceu vem do
/// `ForcarClima`/`ApertarClima` por pacote, o tremor vem do `MandarEfeito`, a cratera vem do
/// `MandarDecalque` e o chao que cai vem do `MandarCelulaCaida`. Um laboratorio que os desenhasse
/// sozinho provaria o DESENHO e deixaria justamente o encanamento -- que e onde este projeto ja
/// perdeu quatro efeitos calados -- sem uma medida. Sobe com `--host --agoniaviva`, que e o palco do
/// outro lado (`GameServer.AgoniaViva.cs`).
///
/// ============================ O QUE ELA MEDE NO PIXEL, E POR QUE ISSO E "INTENSIDADE" ============================
/// Tres numeros, e nenhum deles e um campo do jogo:
///
///   1. **O AR** -- `media(R) / media((G+B)/2)` do quadro inteiro.
///      RAZAO e nao diferenca: brilho de cena move diferenca e nao move razao (um raio caindo clareia
///      a tela e mudaria uma medida por diferenca sem que o ceu tivesse piorado). E ela representa
///      intensidade porque o ceu de `Destruicao` e MULTIPLICATIVO no ambiente -- `(0.62, 0.30, 0.24)`
///      em `Clima.cs` -- e a forca dele e a rampa: `0,45 + 0,55 x agonia`. Quanto mais perto do fim,
///      mais alaranjada fica a pele de todo mundo. E o unico efeito que o jogador nao consegue nao ver.
///
///   2. **O CHAO** -- a fracao do recorte central que **deixou de ser o chao calmo**.
///      Duas correcoes fazem esse numero valer alguma coisa, e as duas foram escolhas medidas:
///        * **normalizacao pelo ar**: cada canal e dividido pela media do proprio recorte antes de
///          comparar. Sem isso o veu alaranjado sozinho marcaria a tela inteira como "mudou", e o
///          numero seria o do item 1 outra vez, com outro nome;
///        * **alinhamento por deslocamento**: o tremor MOVE a camera, entao o quadro inteiro sai
///          deslocado do calmo. Sem alinhar, um tremor de 19 px marcaria toda a borda de todo objeto.
///      O que sobra depois das duas e o que APARECEU: cratera, fumaca, buraco de chao caido, pedra
///      levitando e particula de clima. Ou seja: **quanto do que voce ve deixou de ser planeta e
///      passou a ser destrocos**.
///
///   3. **O TREMOR** -- o deslocamento em PIXELS que o alinhamento do item 2 teve que desfazer, no
///      pior instante de cada patamar. Ele e o tremor medido na imagem, e nao o `_tremor` lido do
///      node: a camera pode estar sacudindo no campo e a tela pode nao estar se mexendo (foi assim
///      que este projeto descobriu que `Modulate` nao e tela).
///
/// ============================ E O CUSTO ============================
/// Quadros por segundo com o jogador DENTRO do planeta, medidos duas vezes: no mundo calmo e no pico
/// da agonia, no mesmo lugar, com a mesma camera, em janelas do mesmo tamanho. Uma medida so nao diz
/// nada -- "58 quadros por segundo" pode ser o custo da agonia ou pode ser esta maquina.
/// **A janela do pico nao mede mais nada enquanto corre**: comparar imagem custa milissegundos, e
/// uma bancada que medisse o custo do jogo somando o custo dela mesma mentiria pra cima.
///
///     &lt;godot&gt; --path . --host --rede 7962 --agoniaviva --diagchao \
///              --conta bancada_chao --nome QuemVeOMundoAcabar --position 1920,0
/// ==================================================================================================================
/// </summary>
public partial class RoboDoChaoQueMorre : Node
{
	private readonly List<string> _linhas = [];
	private int _falhas;

	private void Ok(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  OK   " : "  FALHA") + "  " + oque
					+ (passou || detalhe.Length == 0 ? "" : "   " + detalhe));
		if (!passou) _falhas++;
	}

	private void Nota(string t) => _linhas.Add("   --    " + t);

	// =====================================================================
	// O RECORTE, E POR QUE E ESTE
	// =====================================================================
	/// <summary>
	/// O RETANGULO CENTRAL DA TELA -- 640x360 no meio de 1280x720.
	///
	/// **A ESCOLHA E MEDIDA E NAO ESTETICA.** Este projeto ja errou a faixa de amostragem duas vezes:
	/// uma caiu no CEU (e mediu a cor do ceu achando que media o corpo) e outra pegou uma chuva de
	/// sangue no palco. Aqui o problema e outro e igualmente concreto: o HUD e `CanvasLayer` e desenha
	/// POR CIMA de tudo, nas bordas -- barra de vida, barra de ki, chat, minimapa. Um recorte de tela
	/// cheia mediria a interface, que nao muda nunca, e diluiria o chao em ~30%.
	///
	/// O meio da tela e o unico lugar garantidamente livre de HUD, e e onde o corpo do jogador esta --
	/// ou seja, e exatamente o que o jogador esta olhando.
	/// </summary>
	private static readonly Rect2I Recorte = new(320, 180, 640, 360);

	/// <summary>
	/// O RECORTE DESCE PRA 160x90 antes de qualquer comparacao (fator 4).
	///
	/// Nao e economia gratuita: a busca de alinhamento e `(2k+1)^2` comparacoes de imagem inteira, e
	/// em resolucao cheia isso seria 60 milhoes de leituras por amostra. Em 160x90, com os bytes
	/// crus do `GetData()`, sao 4 milhoes -- que cabem entre dois quadros sem aparecer na medida de
	/// custo (e por isso ela roda FORA da janela de custo mesmo assim).
	///
	/// O preco: o deslocamento medido tem resolucao de 4 px. O tremor deste jogo vai de ~6 px (700 ms)
	/// a ~19 px (2400 ms), entao 4 px de passo separam os dois extremos com folga.
	/// </summary>
	private const int Fator = 4;
	private const int LargPeq = 640 / Fator, AltPeq = 360 / Fator;

	/// <summary>Quantos passos de <see cref="Fator"/> px a busca de alinhamento cobre pra cada lado.</summary>
	private const int BuscaDeAlinhamento = 8;

	/// <summary>
	/// Acima disto o pixel "deixou de ser o chao calmo" -- e a unidade e DESVIO PADRAO do proprio
	/// recorte, somado nos tres canais (ver <see cref="Reduzir"/>).
	///
	/// **E ELE NAO E CHUTADO, E CONFERIDO**: a fase calma mede o recorte contra ELE MESMO num instante
	/// diferente (<see cref="_ruidoDoCalmo"/>), e esse numero e o piso de ruido do mundo parado --
	/// grama balancando, nuvem andando, o proprio corpo respirando. Uma bancada que so escolhesse o
	/// limiar no olho estaria afirmando o que ela mesma calibrou; com o piso medido ao lado, o leitor
	/// ve a distancia entre "o mundo normal se mexendo" e "o mundo virando destroco".
	/// </summary>
	private const double LimiarDeMudanca = 1.2;

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private enum Fase { Esperando, Calmo, Ruido, Agonia, Custo, Espaco, Fim }

	private Fase _fase = Fase.Esperando;
	private double _relogio;
	private bool _acabou;

	/// <summary>O quadro do mundo CALMO, ja reduzido e normalizado. E a regua de tudo.</summary>
	private float[]? _calmo;

	private ZoneKey _zonaDoPlaneta;
	private string _nomeDoPlaneta = "";
	private double _arNoCalmo;
	private double _fpsCalmo;

	/// <summary>O quanto o mundo PARADO difere de si mesmo. Ver <see cref="TickDoRuido"/>.</summary>
	private double _ruidoDoCalmo;


	private readonly List<Instante> _instantes = [];
	private readonly List<TiraDeFotos.Quadro> _tira = [];

	/// <summary>Um patamar medido. `Agonia` vem do caminho de producao, o resto sai do pixel.</summary>
	private readonly record struct Instante(
		double Agonia, double Ar, double Chao, double Tremor, double ForcaDoCeu,
		int Decalques, int Pedras);

	/// <summary>A agonia do ultimo patamar ja fotografado. Ver <see cref="TickDaAgonia"/>.</summary>
	private double _ultimaAgonia = -1;

	/// <summary>Ha quanto tempo a agonia esta parada no mesmo valor.</summary>
	private double _paradaHa;

	/// <summary>
	/// QUANTO O PATAMAR TEM QUE SEGURAR ANTES DE VALER A FOTO.
	///
	/// **Ela espera FATO e nao relogio**: o fato e "a agonia parou de mudar", e a espera existe porque
	/// os efeitos do chao precisam ACUMULAR na intensidade nova -- a cratera dura 18 s, a cratera
	/// grande 40 s, o chao cai de uma celula por vez. Fotografar no instante em que o numero mudou
	/// mostraria o acumulo do patamar ANTERIOR com o rotulo do novo.
	/// </summary>
	private const double EstabilidadeParaFotografar = 9;

	/// <summary>
	/// QUANTO A AGONIA PODE OSCILAR SEM ISSO CONTAR COMO "MUDOU DE PATAMAR".
	///
	/// ============================ ELA NAO E FOLGA GRATUITA, E UM DENTE DE SERRA MEDIDO ============================
	/// O cliente converte `Faltam` num PRAZO ABSOLUTO no instante em que recebe e integra sozinho dali
	/// em diante -- entao, entre dois pacotes, a agonia dele SOBE. O palco fixa o patamar e reenvia uma
	/// vez por segundo, e o resultado e um dente de serra de ~0,004 no ultimo patamar (`faltam` 4 -> 3
	/// e um passo de 0,0043 na curva `t^1,5`).
	///
	/// Com a tolerancia em 0,005 -- que foi o primeiro palpite -- esse dente ficava **no limite**, e um
	/// quadro perdido bastaria pra o robo achar que o patamar mudou, zerar a espera e nunca fotografar.
	/// Uma bancada que nunca dispara nao fica vermelha: ela fica rodando.
	///
	/// 0,02 e mais que quatro vezes o dente e menos que um sexto do menor degrau de verdade (0,12 pra
	/// 0,24, entre o primeiro e o segundo patamar). A margem esta dos dois lados.
	/// ==========================================================================================================
	/// </summary>
	private const double FolgaDoPatamar = 0.02;

	/// <summary>
	/// O PRAZO DE SANIDADE da fase de agonia. **Nao e um prazo de efeito** -- e a garantia de que a
	/// bancada nunca PENDURA. A irma dela (`--diagagonia`) travou pra sempre na primeira rodada com o
	/// defeito injetado, esperando uma explosao que nunca ia abrir, e uma janela parada nao diz a
	/// ninguem se o jogo travou ou se o efeito nao existe.
	/// </summary>
	private const double PacienciaDaAgonia = 420;

	/// <summary>
	/// QUANTO DURA A JANELA DE AMOSTRA de cada patamar.
	///
	/// **DOZE SEGUNDOS, E O NUMERO VEIO DE UMA MEDIDA COM BURACO.** Com cinco a coluna do tremor saiu
	/// `25,6 / 0,0 / 26,8 / 42,5 / 45,3 px`: um ZERO no meio de uma escada que sobe. Nao era o tremor
	/// que faltava -- e que no segundo patamar ele vem a cada ~9 s (`TremorMax` apertado pela agonia
	/// 0,23, com jitter de +-25%), e uma janela de 5 s tem chance real de cair inteira no silencio
	/// entre duas sacudidas.
	///
	/// A regra que sobrou: **a janela tem que ser maior que o maior intervalo do que ela mede**, e o
	/// maior intervalo desta rampa e ~11 s (`MortePlanetaria.TremorMax`). Doze cobre com folga, e o
	/// patamar do palco (26 s) foi feito pra caber os 9 s de espera de estabilidade mais estes 12.
	/// </summary>
	private const double JanelaDeAmostra = 12;

	/// <summary>As amostras da janela do patamar em curso. Ver <see cref="TickDaAgonia"/>.</summary>
	private readonly List<(double Chao, double Tremor, double Ar)> _amostras = [];

	private double _amostrandoHa;
	private int _saltoDeAmostra;

	private static GameClient? C => GameClient.Instance;

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ESTE MUNDO E MEU? Mesma recusa das outras bancadas de `--host`, e aqui ela e mais grave que
		// em qualquer outra: com a porta tomada o cliente entra no mundo DA OUTRA SESSAO, e esta
		// bancada **destroi o planeta em que a pessoa estiver jogando**.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[chao] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo "
					  + "e de outra sessao, e eu ia MATAR O PLANETA dela. Nada foi forcado.");
			GetTree().Quit(1);
			return;
		}

		if (C is not { Connected: true } cli || World.Instancia == null) return;

		_relogio += delta;
		MedirQuadro(delta);

		switch (_fase)
		{
			case Fase.Esperando: Esperar(cli); break;
			case Fase.Calmo: TickCalmo(cli); break;
			case Fase.Ruido: TickDoRuido(cli); break;
			case Fase.Agonia: TickDaAgonia(cli, delta); break;
			case Fase.Custo: TickDoCusto(cli); break;
			case Fase.Espaco: TickDoEspaco(cli, delta); break;
		}
	}

	// =====================================================================
	// O RELOGIO DE QUADROS -- a unica coisa que corre o tempo todo
	// =====================================================================
	/// <summary>
	/// TODO QUADRO DA JANELA, e nao um acumulador.
	///
	/// A media sozinha e uma resposta ruim pra *"o beta trava?"*: 118 quadros por segundo de media
	/// convive com uma travadinha de 90 ms, e e a travadinha que o jogador sente. Guardar os `delta`
	/// custa uma lista de ~950 `double` por janela e devolve a distribuicao inteira -- mediana (o que
	/// se ve o tempo todo), 95% (o que se ve de vez em quando) e o PIOR (a travadinha).
	/// </summary>
	private readonly List<double> _deltas = [];

	private void MedirQuadro(double delta) => _deltas.Add(delta);

	private void ZerarOContador() => _deltas.Clear();

	/// <summary>
	/// Um quadro que passa disto e uma TRAVADINHA -- 33 ms e um quadro perdido a 30 Hz, que e o piso
	/// abaixo do qual o olho reclama. Contar quantos ha responde *"trava?"*, que a media nao responde.
	/// </summary>
	private const double MsDeTravadinha = 33;

	/// <summary>Devolve (quadros/s pela mediana, ms no percentil 95, pior ms, quantos quadros, quantas travadinhas).</summary>
	private (double Fps, double P95Ms, double PiorMs, int Quadros, int Travadas) Fechar()
	{
		if (_deltas.Count == 0) return (0, 0, 0, 0, 0);
		List<double> v = [.. _deltas];
		v.Sort();
		double mediana = v[v.Count / 2];
		return (mediana <= 0 ? 0 : 1 / mediana,
				v[Math.Min(v.Count - 1, (int)(v.Count * 0.95))] * 1000,
				v[^1] * 1000,
				v.Count,
				v.Count(d => d * 1000 > MsDeTravadinha));
	}

	// =====================================================================
	// AS FASES
	// =====================================================================
	private void Esperar(GameClient cli)
	{
		if (ChaveDePlaneta.Da(cli.Zone) == null) return;   // ainda no lobby / em zona que nao e planeta
		if (_relogio < 6) return;                          // o mundo assentando: mapa, corpo, camera

		_zonaDoPlaneta = cli.Zone;
		_nomeDoPlaneta = cli.Zone.Name;
		_fase = Fase.Calmo;
		_relogio = 0;
		ZerarOContador();
		GD.Print($"[chao] entrei em '{_nomeDoPlaneta}'. Medindo o mundo CALMO.");
	}

	/// <summary>Quanto dura cada janela de custo. Oito segundos: ~500 quadros a 60 Hz.</summary>
	private const double JanelaDeCusto = 8;

	private void TickCalmo(GameClient cli)
	{
		if (_relogio < JanelaDeCusto) return;

		(double fps, double p95, double pior, int quadros, int travadas) = Fechar();
		_fpsCalmo = fps;

		// ---- A REGUA: o quadro calmo, reduzido e normalizado ----
		Image? img = Quadro();
		if (img == null)
		{
			Nota("SEM QUADRO (headless nao renderiza): esta bancada e inteira sobre PIXEL.");
			Nota("Rode com janela: --host --agoniaviva --diagchao --position 1920,0");
			Terminar();
			return;
		}

		_calmo = Reduzir(img);
		_arNoCalmo = Ar(img);

		Ok("0. o mundo CALMO foi medido (e a regua de tudo que vem depois)", _calmo != null);
		Ok("0. o planeta esta VIVO no comeco -- a bancada nao nasce dentro do estado que ela testa",
		   Math.Abs(AgoniaAgora(cli)) < 1e-9, $"agonia {AgoniaAgora(cli):0.000}");
		Ok("0. ...e o ceu esta LIMPO (nenhum clima de destruicao forcado antes da hora)",
		   cli.ClimaForcado.Tipo != TipoDeClima.Destruicao);

		Nota($"CUSTO no mundo calmo: {fps:0.0} quadros/s pela mediana | 95% dos quadros abaixo de "
		   + $"{p95:0.0} ms | pior quadro {pior:0.0} ms | {travadas} travadinha(s) acima de "
		   + $"{MsDeTravadinha:0} ms | {quadros} quadros em {JanelaDeCusto:0} s");
		_travadasNoCalmo = travadas;

		if (Cortar(img) is { } corte) _tira.Add(new TiraDeFotos.Quadro(corte, 0));
		Foto("chao-0-calmo", img);

		_fase = Fase.Ruido;
		_relogio = 0;
		GD.Print($"[chao] regua pronta ({fps:0.0} quadros/s no calmo). Medindo o PISO DE RUIDO.");
	}

	/// <summary>
	/// ============================ O PISO DE RUIDO: O MUNDO CALMO CONTRA ELE MESMO ============================
	/// Dois segundos e meio depois da regua, a mesma medida e feita de novo -- **no mesmo mundo, no
	/// mesmo lugar, sem nada acontecendo**. O que ela devolver e o quanto um mundo PARADO difere de si
	/// mesmo: grama balancando, nuvem andando, o corpo respirando, um cidadao passando.
	///
	/// Sem este numero, "93% do chao virou destroco" nao teria com o que ser comparado, e o limiar
	/// seria um palpite que a propria bancada calibrou pra ficar bonito. Com ele, o leitor ve a
	/// distancia entre as duas coisas -- e, se um dia o piso subir, sabe que a medida deixou de
	/// separa-las antes de a linha ficar vermelha.
	///
	/// **E ele e a outra metade do TREMOR tambem**: num mundo calmo o alinhamento tem que dar ZERO
	/// deslocamento. Um piso de tremor diferente de zero num mundo parado diria que a medida esta
	/// achando movimento onde nao ha.
	/// ======================================================================================================
	/// </summary>
	private void TickDoRuido(GameClient cli)
	{
		// ============================ O PISO E MEDIDO COMO OS PATAMARES SAO, E ISSO FOI CONSERTO ============================
		// A primeira versao tirava UMA amostra 2,5 s depois da regua, e o numero pulou de **0,2% pra
		// 16,6% entre duas rodadas** -- com o mundo igualmente parado nas duas. A causa e obvia depois de
		// vista: a Terra tem quarenta cidadaos de povoamento andando, e um deles atravessar o recorte no
		// instante da amostra move o numero inteiro.
		//
		// E a inconsistencia era MINHA: cada patamar da agonia e a MEDIANA de ~90 amostras, e o piso
		// contra o qual eles sao comparados era um quadro sorteado. Comparar mediana com sorteio nao e
		// comparar. Agora os dois lados usam a mesma janela e a mesma estatistica.
		// ==============================================================================================================
		if (++_saltoDeAmostra % 4 == 0 && Quadro() is { } q && _calmo != null)
		{
			(double ch, double tr) = ChaoETremor(q);
			_amostras.Add((ch, tr, 0));
		}

		if (_relogio < JanelaDeAmostra || _amostras.Count < 4) return;

		double ruido = Mediana([.. _amostras.Select(a => a.Chao)]);
		double tremor = _amostras.Max(a => a.Tremor);
		int quantas = _amostras.Count;
		_amostras.Clear();

		_ruidoDoCalmo = ruido;

		Nota($"PISO DE RUIDO (o mundo calmo contra ele mesmo, mediana de {quantas} amostras em "
		   + $"{JanelaDeAmostra:0} s): chao {ruido * 100:0.0}%, tremor {tremor:0.0} px");

		Ok("0. o PISO DE RUIDO do mundo calmo e baixo -- a medida separa 'o mundo se mexendo' de "
		 + "'o mundo virando destroco'",
		   ruido < 0.20, $"{ruido * 100:0.0}% ja no mundo parado");

		Ok("0. ...e num mundo PARADO o alinhamento nao acha tremor nenhum",
		   tremor <= 4.0, $"{tremor:0.0} px de deslocamento sem tremor nenhum acontecendo");

		_fase = Fase.Agonia;
		_relogio = 0;
		GD.Print($"[chao] piso de ruido: {ruido * 100:0.1}%. Esperando o mundo acabar.");
	}

	private void TickDaAgonia(GameClient cli, double delta)
	{
		if (_relogio > PacienciaDaAgonia)
		{
			Ok("A. o planeta comecou a morrer e a agonia andou ate o pico",
			   false, $"{PacienciaDaAgonia:0} s sem chegar ao pico -- o palco (`--agoniaviva`) subiu?");
			Relatar();
			return;
		}

		double agonia = AgoniaAgora(cli);

		// ============================ ELA ESPERA O FATO, E O FATO E "PAROU DE MUDAR" ============================
		// Nada aqui conta segundos desde o comeco: o palco do servidor pode ser mais rapido ou mais
		// lento, e um roteiro por relogio fotografaria o patamar errado no dia em que alguem mexesse
		// num dos dois numeros. O que o robo observa e a AGONIA que o `S2C.Mortos` entrega -- o mesmo
		// dado que o shader do planeta no espaco le.
		// ====================================================================================================
		if (Math.Abs(agonia - _ultimaAgonia) > FolgaDoPatamar)
			{ _ultimaAgonia = agonia; _paradaHa = 0; return; }

		_paradaHa += delta;
		if (agonia <= 0 || _paradaHa < EstabilidadeParaFotografar) return;

		// ============================ A AMOSTRA E UMA JANELA, E NAO UM QUADRO ============================
		// **ISTO E CONSERTO DE UMA MEDIDA MINHA QUE ESTAVA ERRADA, e o numero denunciou.** A primeira
		// versao media o deslocamento num quadro so, e a coluna do tremor saiu `0,0 / 0,0 / 0,0 / 8,0 /
		// 5,7 px`: tres zeros e um passo pra tras. Nao era o tremor que faltava -- e que o tremor e
		// INTERMITENTE (uma sacudida a cada 3 a 11 s, que decai em ~1 s), e um quadro sorteado cai quase
		// sempre no silencio entre duas.
		//
		// A linha "a tela sacode mais no fim" ficou VERDE assim mesmo, por `5,7 >= 0,0`. Uma prova que
		// passa por acidente e do mesmo tipo daquelas que este projeto ja pagou caro: ela nao mede o que
		// diz medir, e ninguem descobre isso ate o dia em que o efeito quebrar e ela continuar verde.
		//
		// Entao a amostra e uma janela de <see cref="JanelaDeAmostra"/>, e dela saem DOIS numeros com
		// estatisticas diferentes de proposito: o tremor e o **MAIOR** deslocamento (amplitude e um pico,
		// nao uma media) e o chao e a **MEDIANA** (a mediana ignora o quadro que pegou um relampago).
		// ==============================================================================================
		if (_amostras.Count == 0) _amostrandoHa = 0;
		_amostrandoHa += delta;

		// UM A CADA QUATRO QUADROS: a busca de alinhamento custa ~7 ms, e faze-la em todo quadro
		// atrapalharia justamente a coisa que a proxima fase vai medir.
		if (++_saltoDeAmostra % 4 == 0 && Quadro() is { } q && _calmo != null)
		{
			(double ch, double tr) = ChaoETremor(q);
			_amostras.Add((ch, tr, Ar(q)));
		}

		if (_amostrandoHa < JanelaDeAmostra || _amostras.Count < 4) return;

		_paradaHa = 0;

		Image? img = Quadro();
		if (img == null || _calmo == null) { _amostras.Clear(); return; }

		double tremor = _amostras.Max(a => a.Tremor);
		double chao = Mediana([.. _amostras.Select(a => a.Chao)]);

		// ============================ O AR TAMBEM E MEDIANA, E ISSO FOI CONSERTO ============================
		// Na rodada anterior o ar era lido de UM quadro, e a coluna dele subiu 1,36 / 1,43 / 1,58 / 1,77
		// e entao **CAIU pra 1,44 no auge** -- justamente no instante mais vermelho dos cinco minutos.
		// A causa nao era o ceu: e que no auge o RELAMPAGO cai quase de dez em dez segundos (a cadencia
		// dele ja escala com a forca do clima) e um relampago acende a tela de BRANCO. O quadro sorteado
		// caiu num deles.
		//
		// E o mesmo defeito do tremor, do outro lado: **um quadro nao e uma medida de um estado que
		// pisca**. A mediana da janela ignora o relampago do mesmo jeito que ignora o quadro escuro.
		// ================================================================================================
		double ar = Mediana([.. _amostras.Select(a => a.Ar)]);
		int quantas = _amostras.Count;
		_amostras.Clear();

		_instantes.Add(new Instante(
			agonia, ar, chao, tremor,
			cli.ClimaForcado.Tipo == TipoDeClima.Destruicao ? cli.ClimaForcado.Forca : 0,
			Decalques.VivosDeTeste, World.Instancia?.PedrasVivasDeTeste ?? 0));

		if (Cortar(img) is { } corte) _tira.Add(new TiraDeFotos.Quadro(corte, _tira.Count));
		Foto($"chao-{_instantes.Count}-agonia-{agonia:0.00}".Replace(",", "."), img);

		GD.Print($"[chao] instante {_instantes.Count}: agonia {agonia:0.000}, ar {ar:0.000}, "
			   + $"chao {chao:0.000} (mediana de {quantas}), tremor {tremor:0.0} px (maior de {quantas}), "
			   + $"{Decalques.VivosDeTeste} decalques, {World.Instancia?.PedrasVivasDeTeste} pedras");

		// O PICO E O ULTIMO PATAMAR, e quem diz que ele chegou e a agonia -- nao a contagem.
		if (agonia >= 0.90)
		{
			_fase = Fase.Custo;
			_relogio = 0;
			ZerarOContador();
			GD.Print("[chao] PICO. Medindo o custo, sem medir mais nada junto.");
		}
	}

	private int _decalquesNoPico, _pedrasNoPico;
	private double _fpsPico, _piorMsPico;
	private int _travadasNoCalmo;

	private void TickDoCusto(GameClient cli)
	{
		// NADA DE COMPARAR IMAGEM AQUI DENTRO -- ver o cabecalho.
		if (_relogio < JanelaDeCusto) return;

		(_fpsPico, double p95, _piorMsPico, int quadros, int travadas) = Fechar();
		_decalquesNoPico = Decalques.VivosDeTeste;
		_pedrasNoPico = World.Instancia?.PedrasVivasDeTeste ?? 0;

		Nota($"CUSTO no PICO da agonia: {_fpsPico:0.0} quadros/s pela mediana | 95% dos quadros abaixo "
		   + $"de {p95:0.0} ms | pior quadro {_piorMsPico:0.0} ms | {travadas} travadinha(s) acima de "
		   + $"{MsDeTravadinha:0} ms (no calmo eram {_travadasNoCalmo}) | {quadros} quadros em "
		   + $"{JanelaDeCusto:0} s, com {_decalquesNoPico} decalques e {_pedrasNoPico} pedras na tela, "
		   + $"ceu em {cli.ClimaForcado.Forca:0.00}");
		Nota($"CUSTO: calmo {_fpsCalmo:0.0} -> pico {_fpsPico:0.0} quadros/s "
		   + $"({(_fpsCalmo > 0 ? 100 * (_fpsPico / _fpsCalmo - 1) : 0):+0.0;-0.0;0}%)");

		// ============================ O VEREDITO DE CUSTO E UM PISO, E ELE E EXPLICITO ============================
		// 45 quadros por segundo e o piso, e nao 60: a folga existe porque este numero e medido numa
		// maquina so, com a janela no segundo monitor, e o que ele tem que responder e *"o beta trava?"*.
		// Um piso em 60 reprovaria por causa do compositor da area de trabalho.
		// ====================================================================================================
		Ok("C. **O PICO DA AGONIA E JOGAVEL** (o dono precisa deste numero antes do beta)",
		   _fpsPico >= 45, $"{_fpsPico:0.0} quadros/s");
		// ============================ O PERCENTIL 95, E NAO O PIOR, E O QUE VIRA REGRA ============================
		// O pior quadro de uma janela de 950 e um evento unico -- pode ser o coletor de lixo, pode ser o
		// compositor da area de trabalho, pode ser a propria foto que a bancada acabou de tirar. Medido:
		// **34,6 ms no mundo CALMO**, onde nao ha agonia nenhuma pra culpar. Cobrar dele seria cobrar da
		// maquina, e a linha ficaria vermelha por motivo errado.
		//
		// O percentil 95 e o que o jogador sente como fluidez, e ele e cobrado. O pior fica ANOTADO --
		// o dono precisa saber que a travadinha existe, mesmo que ela nao reprove.
		// ======================================================================================================
		Ok("C. ...e 95% dos quadros ficam abaixo de 25 ms (o jogo continua fluido, nao so rapido em media)",
		   p95 < 25, $"95% abaixo de {p95:0.0} ms");

		// ============================ AS TRAVADINHAS SAO CONTADAS, E O TETO E FROUXO DE PROPOSITO ============================
		// Medido em quatro rodadas: no pico ha SEMPRE um punhado de quadros isolados entre 75 e 120 ms,
		// enquanto a mediana e o percentil 95 nao se mexem um decimo. Ou seja: nao e carga continua, e
		// evento -- alguma coisa acontece uma vez e custa caro. Nao isolei a causa; os dois candidatos
		// com precedente escrito neste projeto sao a CELULA DE CHAO que cai (ela toca o `TileMapLayer`,
		// e montar estrutura de desenho custa 2,7 us por celula) e o primeiro `CrateraGrande` da rodada
		// (carregar `DU/Map/big crater.tres` na linha principal).
		//
		// O teto e DEZ e nao zero porque o mundo calmo tambem tem algumas -- reprovar em zero seria
		// reprovar a maquina. O que este numero tem que pegar e a diferenca virar dezenas, que e quando
		// ela deixa de ser evento e vira carga.
		// ==============================================================================================================
		Ok($"C. ...e as travadinhas continuam sendo EVENTO e nao carga (poucos quadros acima de {MsDeTravadinha:0} ms)",
		   travadas <= 10, $"{travadas} travadinha(s) no pico contra {_travadasNoCalmo} no calmo");
		Ok("C. ...e a conta de pedras NAO acompanha o mapa (a Terra tem 266 mil celulas)",
		   _pedrasNoPico is > 0 and <= 64, $"{_pedrasNoPico} pedras");
		Ok("C. ...e a de decalques fica dentro do teto que ja existia",
		   _decalquesNoPico <= Decalques.MaxVivos, $"{_decalquesNoPico} de {Decalques.MaxVivos}");

		_fase = Fase.Espaco;
		_relogio = 0;
	}

	/// <summary>Quanto esperar o commit + a evacuacao antes de desistir.</summary>
	private const double PacienciaDoDesfecho = 60;

	/// <summary>Ha quanto tempo o corpo esta no espaco. Ver <see cref="TickDoEspaco"/>.</summary>
	private double _noEspacoHa;

	private void TickDoEspaco(GameClient cli, double delta)
	{
		bool noEspaco = Espaco.EhEspaco(cli.Zone);

		if (!noEspaco)
		{
			if (_relogio < PacienciaDoDesfecho) return;
			Ok("X. **O JOGADOR FOI JOGADO NO ESPACO** quando o mundo acabou",
			   false, $"ainda em '{cli.Zone.Name}' depois de {PacienciaDoDesfecho:0} s");
			Relatar();
			return;
		}

		// ============================ O RELOGIO DAQUI COMECA AO CHEGAR, E NAO ANTES ============================
		// **ISTO FOI UMA LINHA VERMELHA INTERMITENTE, e a culpa era da bancada.** O `_relogio` desta
		// fase e zerado quando a medida de custo acaba -- uns quarenta segundos ANTES do mundo explodir.
		// Quando o corpo finalmente chegava no espaco, o "espere tres segundos" ja tinha vencido havia
		// muito, e a checagem do registro corria no PRIMEIRO quadro em que a zona mudou: uma corrida
		// contra o `S2C.Mortos`, que chega logo depois do pacote de zona. Numa rodada passou, na outra
		// nao -- que e o pior tipo de prova que existe.
		//
		// E ela ESPERA O FATO, e nao o relogio: o fato e o registro do cliente marcando o planeta como
		// morto. O prazo so existe pra a bancada nunca pendurar -- vencido ele, a linha sai VERMELHA com
		// o motivo, que e o que se quer se o pacote de verdade nunca chegar.
		// ==================================================================================================
		_noEspacoHa += delta;

		bool morto = ChaveDePlaneta.Da(_zonaDoPlaneta) is { } chave && cli.Mortos.Morto(chave);
		if (!morto && _noEspacoHa < 10) return;

		// E um respiro pra a vizinhanca do espaco chegar e os discos serem desenhados, pra a foto.
		if (_noEspacoHa < 3) return;

		Image? img = Quadro();
		Ok("X. **O JOGADOR FOI JOGADO NO ESPACO** quando o mundo acabou (X4 do pedido)", true);
		Ok("X. ...e o planeta em que ele estava esta MORTO no registro do cliente",
		   morto, $"'{_nomeDoPlaneta}' nao constava como morto {_noEspacoHa:0.0} s depois de eu chegar aqui");

		if (img != null) Foto("chao-9-no-espaco", img);
		Relatar();
	}

	// =====================================================================
	// AS MEDIDAS
	// =====================================================================
	private static double AgoniaAgora(GameClient cli) =>
		ChaveDePlaneta.Da(cli.Zone) is { } ch ? cli.IntensidadeDaAgonia(ch) : 0;

	private Image? Quadro()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	/// <summary>
	/// A RAZAO ENTRE CANAIS DO QUADRO INTEIRO: `media(R) / media((G+B)/2)`. Ver o cabecalho.
	/// </summary>
	private static double Ar(Image img)
	{
		double r = 0, gb = 0;
		for (int y = 0; y < img.GetHeight(); y += 3)
			for (int x = 0; x < img.GetWidth(); x += 3)
			{
				Color c = img.GetPixel(x, y);
				r += c.R;
				gb += (c.G + c.B) / 2f;
			}
		return gb <= 1e-6 ? 0 : r / gb;
	}

	/// <summary>
	/// REDUZ O RECORTE CENTRAL A 160x90 **E TIRA DELE A MEDIA E O DESVIO DE CADA CANAL**.
	///
	/// ============================ POR QUE DUAS CORRECOES, E NAO UMA ============================
	/// A primeira versao so DIVIDIA pela media, e o numero que ela produziu denunciou o erro: no
	/// primeiro patamar da agonia (0,12, quando ha duas crateras na tela inteira) ela ja dizia que
	/// **54,7% do chao tinha virado destroco**, e no patamar seguinte esse numero CAIU pra 38,1%. Uma
	/// medida que satura no primeiro degrau e depois anda pra tras nao esta medindo o que diz medir.
	///
	/// A causa: o veu do clima nao MULTIPLICA a cena, ele MISTURA com ela -- `c' = c(1-a) + neblina*a`.
	/// Isso e uma reta com COEFICIENTE e DESLOCAMENTO, e dividir pela media so desfaz o coeficiente. O
	/// deslocamento fica, comprime o contraste e empurra todo pixel um pouco pra longe do original.
	///
	/// Tirar a media E dividir pelo desvio (o "z" de cada canal) desfaz os dois de uma vez, e o que
	/// sobra e ESTRUTURA pura: o que apareceu no lugar do chao. E a mesma disciplina do resto desta
	/// bancada -- razao em vez de diferenca, forma em vez de brilho.
	/// ======================================================================================
	/// </summary>
	private static float[]? Reduzir(Image cheia)
	{
		if (cheia.GetWidth() < Recorte.End.X || cheia.GetHeight() < Recorte.End.Y) return null;

		Image corte = cheia.GetRegion(Recorte);
		corte.Resize(LargPeq, AltPeq, Image.Interpolation.Bilinear);
		corte.Convert(Image.Format.Rgb8);

		byte[] cru = corte.GetData();
		int n = LargPeq * AltPeq;
		var saida = new float[n * 3];
		double[] media = [0, 0, 0], quad = [0, 0, 0];

		for (int i = 0; i < n; i++)
			for (int c = 0; c < 3; c++)
			{
				double v = cru[i * 3 + c];
				media[c] += v;
				quad[c] += v * v;
			}

		for (int c = 0; c < 3; c++)
		{
			media[c] /= n;
			// PISO NO DESVIO: uma tela de cor chapada tem desvio zero, e dividir por ele daria
			// infinito. Um desvio de 1 (de 255) e "sem estrutura nenhuma", que e a resposta certa.
			quad[c] = Math.Max(Math.Sqrt(Math.Max(quad[c] / n - media[c] * media[c], 0)), 1);
		}

		for (int i = 0; i < n; i++)
			for (int c = 0; c < 3; c++)
				saida[i * 3 + c] = (float)((cru[i * 3 + c] - media[c]) / quad[c]);

		return saida;
	}

	/// <summary>
	/// QUANTO DO CHAO DEIXOU DE SER CHAO, e de quanto foi o TREMOR -- num varrimento so.
	///
	/// Procura o deslocamento que MELHOR alinha o quadro de agora com o calmo (o tremor), e devolve a
	/// fracao de pixels que continuam diferentes DEPOIS de alinhado (o estrago). Sem a busca, o
	/// tremor sozinho marcaria toda borda de todo objeto e o numero saturaria no primeiro patamar.
	/// </summary>
	private (double Chao, double Tremor) ChaoETremor(Image img)
	{
		float[]? agora = Reduzir(img);
		if (agora == null || _calmo == null) return (0, 0);

		double melhor = double.MaxValue;
		int melhorDx = 0, melhorDy = 0;

		for (int dy = -BuscaDeAlinhamento; dy <= BuscaDeAlinhamento; dy++)
			for (int dx = -BuscaDeAlinhamento; dx <= BuscaDeAlinhamento; dx++)
			{
				double soma = 0;
				int n = 0;
				// A BORDA FICA DE FORA da soma: com deslocamento, uma faixa do quadro nao tem par no
				// outro, e contar essa faixa como "mudou" premiaria o deslocamento zero.
				for (int y = BuscaDeAlinhamento; y < AltPeq - BuscaDeAlinhamento; y += 2)
					for (int x = BuscaDeAlinhamento; x < LargPeq - BuscaDeAlinhamento; x += 2)
					{
						int a = (y * LargPeq + x) * 3;
						int b = ((y + dy) * LargPeq + (x + dx)) * 3;
						soma += Math.Abs(agora[a] - _calmo[b])
							  + Math.Abs(agora[a + 1] - _calmo[b + 1])
							  + Math.Abs(agora[a + 2] - _calmo[b + 2]);
						n++;
					}
				soma = n == 0 ? double.MaxValue : soma / n;
				if (soma < melhor) { melhor = soma; melhorDx = dx; melhorDy = dy; }
			}

		// SEGUNDA PASSADA no melhor alinhamento: agora contando PIXEL A PIXEL quantos passaram do
		// limiar. A primeira passada escolhe o alinhamento pela soma (que e estavel); esta responde a
		// pergunta que interessa, que e "quanta AREA da tela virou destroco".
		int mudou = 0, total = 0;
		for (int y = BuscaDeAlinhamento; y < AltPeq - BuscaDeAlinhamento; y++)
			for (int x = BuscaDeAlinhamento; x < LargPeq - BuscaDeAlinhamento; x++)
			{
				int a = (y * LargPeq + x) * 3;
				int b = ((y + melhorDy) * LargPeq + (x + melhorDx)) * 3;
				double d = Math.Abs(agora[a] - _calmo[b])
						 + Math.Abs(agora[a + 1] - _calmo[b + 1])
						 + Math.Abs(agora[a + 2] - _calmo[b + 2]);
				total++;
				if (d > LimiarDeMudanca) mudou++;
			}

		double tremorEmPixels = Math.Sqrt(melhorDx * melhorDx + melhorDy * melhorDy) * Fator;
		return (total == 0 ? 0 : (double)mudou / total, tremorEmPixels);
	}

	/// <summary>A mediana de uma lista. Mediana e nao media: um relampago e um quadro, nao um estado.</summary>
	private static double Mediana(List<double> v)
	{
		if (v.Count == 0) return 0;
		v.Sort();
		return v[v.Count / 2];
	}

	private static Image? Cortar(Image img) =>
		img.GetWidth() < Recorte.End.X || img.GetHeight() < Recorte.End.Y
			? null : img.GetRegion(Recorte);

	/// <summary>
	/// GRAVA A FOTO **E CONFERE QUE ELA NAO SAIU PRETA**.
	///
	/// A guarda existe porque este projeto ja teve uma rodada inteira de fotos pretas com todas as
	/// checagens verdes. E aqui ela e mais dificil que na bancada do espaco: o ceu de `Destruicao`
	/// encobre 0,98 e escurece a cena de proposito, entao um piso alto reprovaria justamente a foto
	/// certa. O piso e sobre a fracao de pixels ACIMA DE QUASE-PRETO, e o que ele mata e o buffer
	/// vazio -- que sai com luminancia exatamente zero em 100% do quadro.
	/// </summary>
	private void Foto(string nome, Image img)
	{
		int acesos = 0, total = 0;
		for (int y = 0; y < img.GetHeight(); y += 4)
			for (int x = 0; x < img.GetWidth(); x += 4)
			{
				total++;
				if (img.GetPixel(x, y).Luminance > 0.02f) acesos++;
			}

		double fracao = total == 0 ? 0 : (double)acesos / total;
		Ok($"foto `{nome}` NAO saiu preta ({fracao * 100:0.0}% do quadro tem luz)",
		   fracao > 0.50, $"{fracao * 100:0.00}%");

		string caminho = $"user://{nome}.png";
		img.SavePng(caminho);
		Nota($"foto: {ProjectSettings.GlobalizePath(caminho)}");
	}

	// =====================================================================
	// O VEREDITO
	// =====================================================================
	private void Relatar()
	{
		if (_instantes.Count < 3)
		{
			Ok("A. a tira do chao tem pelo menos tres instantes pra comparar",
			   false, $"so {_instantes.Count}");
			Terminar();
			return;
		}

		Nota("os instantes, na ordem (agonia | ar R/((G+B)/2) | chao que virou destroco | tremor em "
		   + "px | forca do ceu | decalques | pedras):");
		foreach (Instante i in _instantes)
			Nota($"     agonia {i.Agonia:0.000}   ar {i.Ar:0.000}   chao {i.Chao * 100:00.0}%   "
			   + $"tremor {i.Tremor:00.0} px   ceu {i.ForcaDoCeu:0.00}   "
			   + $"{i.Decalques} decalques   {i.Pedras} pedras");

		Instante primeiro = _instantes[0], ultimo = _instantes[^1];

		// ---- 1. O AR ----
		Ok("A. **O AR AVERMELHA** do primeiro instante ao ultimo (o ceu de fim de mundo apertando)",
		   ultimo.Ar > primeiro.Ar + 0.01,
		   $"{primeiro.Ar:0.000} -> {ultimo.Ar:0.000} (no calmo era {_arNoCalmo:0.000})");

		Ok("A. ...e ja no PRIMEIRO instante ele nao e o mundo calmo (a agonia comeca no piso, nao em zero)",
		   primeiro.Ar > _arNoCalmo + 0.005,
		   $"calmo {_arNoCalmo:0.000} -> primeiro instante {primeiro.Ar:0.000}");

		// ---- 2. O CHAO ----
		Ok("A. **O CHAO VIRA DESTROCO**: a fracao da tela que deixou de ser o chao calmo cresce",
		   ultimo.Chao > primeiro.Chao + 0.03,
		   $"{primeiro.Chao * 100:0.0}% -> {ultimo.Chao * 100:0.0}%");

		// E A METADE QUE DA SENTIDO AO NUMERO: ele tem que estar MUITO acima do que um mundo parado
		// mede contra si mesmo. Sem esta linha, "93% do chao virou destroco" poderia ser 93% de grama
		// balancando -- e a bancada estaria medindo a propria tolerancia.
		Ok("A. ...e no auge ele esta MUITO acima do piso de ruido do mundo parado",
		   ultimo.Chao > _ruidoDoCalmo * 2 + 0.05,
		   $"auge {ultimo.Chao * 100:0.0}% contra piso {_ruidoDoCalmo * 100:0.0}%");

		// ---- 3. O TREMOR ----
		Ok("A. **A TELA SACODE MAIS** no fim que no comeco (medido na imagem, nao no node)",
		   ultimo.Tremor > primeiro.Tremor,
		   $"{primeiro.Tremor:0.0} px -> {ultimo.Tremor:0.0} px");

		Ok("A. ...e ela SACODE, ponto: no auge o alinhamento tem deslocamento pra desfazer",
		   ultimo.Tremor > 0, $"{ultimo.Tremor:0.0} px no auge");

		// ---- 4. A RAMPA CHEGA LONGE (o crivo que a `--agoniachata` derruba do outro lado) ----
		// "Nao desce" fica verde numa rampa chata. Esta linha exige movimento de verdade nos dois
		// numeros que representam a intensidade, e ela e a que reprova um sistema que so LIGA os
		// efeitos em vez de INTENSIFICA-LOS -- que e o pedido literal do dono.
		Ok("A. **A INTENSIDADE E RAMPA, E NAO INTERRUPTOR**: ar e chao andam nos dois sentidos da escada",
		   ultimo.Ar - primeiro.Ar > 0.01 && ultimo.Chao - primeiro.Chao > 0.03
		   && _instantes[_instantes.Count / 2].Chao > primeiro.Chao,
		   $"meio {_instantes[_instantes.Count / 2].Chao * 100:0.0}%");

		// ---- 5. AS DUAS METADES DOS EFEITOS DO SERVIDOR ----
		Ok("A. o CEU vem do servidor e aperta junto (o `ApertarClima`, e nao um clima local)",
		   ultimo.ForcaDoCeu > primeiro.ForcaDoCeu,
		   $"{primeiro.ForcaDoCeu:0.00} -> {ultimo.ForcaDoCeu:0.00}");

		Ok("A. as CRATERAS chegam pelo fio e existem na tela (o ramo `if(4)` do DM, que nunca rodou la)",
		   _instantes.Max(i => i.Decalques) > 0,
		   $"maximo de {_instantes.Max(i => i.Decalques)} decalques vivos");

		Ok("A. as PEDRAS levitam, e mais no fim que no comeco",
		   ultimo.Pedras > 0 && ultimo.Pedras >= primeiro.Pedras,
		   $"{primeiro.Pedras} -> {ultimo.Pedras}");

		// ---- A TIRA ----
		string caminho = ProjectSettings.GlobalizePath("user://agonia-tira-do-chao.png");
		double pintada = TiraDeFotos.Montar(_tira, caminho);
		Ok($"A. **A TIRA DO CHAO** saiu com {_tira.Count} quadros numerados (0 = mundo calmo, "
		 + $"1 a {_tira.Count - 1} = a agonia subindo) e ela NAO esta vazia",
		   pintada > 0.5, $"{pintada * 100:0}% dos pixels sao imagem");
		Nota($"tira: {caminho}");

		Terminar();
	}

	private void Terminar()
	{
		_acabou = true;
		GD.Print("");
		GD.Print("========== BANCADA DO CHAO QUE MORRE ==========");
		foreach (string l in _linhas) GD.Print(l);
		int ok = _linhas.Count(x => x.StartsWith("  OK", StringComparison.Ordinal));
		GD.Print($"===== FIM: {ok} OK, {_falhas} FALHA(S) =====");
		GetTree().Quit(_falhas == 0 ? 0 : 1);
	}
}
