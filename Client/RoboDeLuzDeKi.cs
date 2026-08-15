using Godot;
using Jandirus.Core.Combat;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA LUZ DOS ATAQUES DE KI ============================
/// O pedido do dono foi *"beams e ataque de ki deveriam ter LUZ PROPRIA"*, e ele so esta atendido se
/// CINCO coisas forem verdade ao mesmo tempo:
///
///   1. a luz existe e e a mesma fonte que o cenario ja usa (nao um segundo sistema de iluminacao);
///   2. a cor dela e a cor do ki de quem atirou;
///   3. ela MORRE com o tiro -- zero luz orfa, que e o defeito mais provavel aqui;
///   4. ha teto, e ele e respeitado mesmo com a zona cheia (256 tiros);
///   5. o PIXEL do chao em volta clareia de verdade, e o custo disso e um numero.
///
/// E as familias 7 e 8 respondem as quatro perguntas que so a FOTO DE NOITE responde -- o clarao
/// anda junto do tiro, ele tem a cor do ki DAQUELE jogador, ele some quando o tiro some, e o quadro
/// aguenta a tela cheia de raio --, cada uma com o DEFEITO INJETADO no mesmo quadro, ao lado.
///
/// ============================ ELA MEDE PIXEL E MILISSEGUNDO, E ISSO E O PONTO ============================
/// Este projeto tem registro de quatro defeitos visuais que passaram por quatro mil checagens verdes
/// porque a bancada media INTENCAO: `Enabled = true` escrito nao e chao aceso, e "as duas telas
/// concordam" fica verde com as duas erradas igual. Entao a familia 5 nao pergunta se a
/// `PointLight2D` esta ligada: ela fotografa o chao com a luz ligada e com a luz apagada e exige que
/// o quadro tenha CLAREADO.
///
/// E a familia 4 nao estima custo: ela roda 90 quadros com as luzes apagadas e 90 com elas acesas,
/// no mesmo cenario e com os MESMOS nodes -- so a passada de luz muda --, e devolve o milissegundo
/// por quadro que cada teto custa. Era o numero que faltava pra escolher o teto, e ele nao podia
/// sair de palpite: nao existia bancada de custo de luz neste repo.
/// ====================================================================================================
///
///     &lt;godot&gt; --headless --path . --diagluzdeki                (familias 1 a 3)
///     &lt;godot&gt; --path . --diagluzdeki --position 1920,0 --resolution 1280x720   (as cinco)
///
/// SEM REDE E SEM MUNDO, como a `--diagartedeki`: luz de tiro nao depende de zona, de servidor nem
/// de login. O que ela toca e um node de tiro, um contador e o quadro desenhado.
///
/// ============================ POR QUE UM PASSO POR QUADRO ============================
/// `QueueFree` nao apaga na hora: ele apaga no FIM do quadro. Uma familia que criasse tiros logo
/// depois de outra ter pedido a morte dos dela contaria as luzes das duas -- e a familia seguinte
/// veria o contador descer sozinho quando os mortos finalmente saissem. As familias entao andam uma
/// por quadro (<see cref="_passos"/>), com um <see cref="Limpar"/> entre elas.
/// ==================================================================================
/// </summary>
public partial class RoboDeLuzDeKi : Node2D
{
	private readonly List<string> _linhas = [];
	private int _falhas;

	/// <summary>
	/// O CHAO DA BANCADA -- um `Polygon2D` cinza escuro, e nao um `ColorRect`.
	///
	/// Luz 2D acende CanvasItem, e um `Polygon2D` e um canvas item do mundo sem discussao nenhuma. Um
	/// `Control` mora na arvore de interface, e a duvida sobre se ele entra na passada de luz seria
	/// duvida sobre o VEREDITO -- a familia 5 mede exatamente o brilho deste chao.
	/// </summary>
	private static readonly Color CorDoChao = new(0.10f, 0.10f, 0.12f);

	/// <summary>
	/// O CHAO DA FAMILIA 7 -- claro, porque a noite dela nao e um chao pintado de escuro: e este chao
	/// multiplicado pela cor da meia-noite, que e como a noite do jogo se faz.
	/// </summary>
	private static readonly Color CorDoChaoDeDia = new(0.44f, 0.46f, 0.40f);

	/// <summary>O azul da meia-noite do jogo, letra por letra: `Iluminacao.Curva[0]`.</summary>
	private static readonly Color CorDaMeiaNoite = new("1b2242");

	private CanvasModulate _noiteDeVerdade = null!;

	/// <summary>A cor de ki da bancada: um magenta que nao existe em folha de ki nenhuma.</summary>
	private static readonly Color CorDeKi = new(1f, 0.15f, 0.85f);

	private void Ok(string oque, bool passou)
	{
		_linhas.Add((passou ? "  OK   " : "  FALHA") + "  " + oque);
		if (!passou) _falhas++;
	}

	private void Nota(string t) => _linhas.Add("   --    " + t);

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private readonly List<Action> _passos = [];
	private int _passo;

	/// <summary>
	/// O CHAO EM UM PEDACO SO -- um unico canvas item cobrindo a tela inteira. Ver
	/// <see cref="Familia6"/>: e ele que revela quantas luzes o motor aceita por item.
	/// </summary>
	private Polygon2D _chaoInteiro = null!;

	/// <summary>
	/// O CHAO EM PEDACOS, que e o que o jogo de verdade tem: o cenario e um `TileMapLayer`, e o
	/// Godot o desenha em BLOCOS. Cada bloco e um canvas item proprio -- e por isso o limite de
	/// luzes por item nao vale pra tela toda, e sim por bloco.
	/// </summary>
	private Node2D _chaoLadrilhado = null!;

	/// <summary>
	/// O LADO DO BLOCO, em pixel. Da ordem do que o `TileMapLayer` usa (blocos de 16x16 tiles de 32
	/// px). O numero exato importa menos que o fato de haver blocos: e o que faz a 17a luz da TELA
	/// aparecer, desde que ela caia noutro bloco.
	/// </summary>
	private const int LadoDoBloco = 256;

	public override void _Ready()
	{
		_chaoInteiro = new Polygon2D
		{
			Name = "ChaoInteiro",
			Color = CorDoChao,
			Polygon = [new Vector2(-2000, -2000), new Vector2(4000, -2000),
					   new Vector2(4000, 4000), new Vector2(-2000, 4000)],
			ZIndex = -100,
		};
		AddChild(_chaoInteiro);

		_chaoLadrilhado = new Node2D { Name = "ChaoLadrilhado", Visible = false, ZIndex = -100 };
		AddChild(_chaoLadrilhado);
		for (int y = -1; y < 8; y++)
			for (int x = -1; x < 12; x++)
				_chaoLadrilhado.AddChild(new Polygon2D
				{
					Color = CorDoChao,
					Position = new Vector2(x * LadoDoBloco, y * LadoDoBloco),
					Polygon = [Vector2.Zero, new Vector2(LadoDoBloco, 0),
							   new Vector2(LadoDoBloco, LadoDoBloco), new Vector2(0, LadoDoBloco)],
				});

		// ============================ A NOITE DE VERDADE, APAGADA POR ENQUANTO ============================
		// As familias 5 e 6 medem sobre um chao PINTADO de escuro, e isso basta pra elas: o que elas
		// perguntam e se a luz compoe no quadro. A familia 7 pergunta outra coisa -- se o efeito
		// aparece PRA O JOGADOR, na cena que ele ve --, e a noite do jogo nao e um chao escuro: e um
		// chao normal multiplicado pelo `CanvasModulate` da meia-noite. Sao coisas diferentes, e a
		// segunda pode falhar com a primeira verde (o `Add` da luz entra ANTES do modulate).
		// ==============================================================================================
		_noiteDeVerdade = new CanvasModulate { Name = "MeiaNoite", Color = CorDaMeiaNoite, Visible = false };
		AddChild(_noiteDeVerdade);

		_passos.AddRange([
			Familia1,       Limpar,
			Familia2Montar, Familia2Conferir, Familia2Zona, Familia2ZonaConferir, Limpar,
			Familia3Montar, Familia3Conferir, Familia3Devolve, Limpar,
			MontarACena,
		]);
	}

	/// <summary>Um quadro de folga pra o que morreu de fato sair, e a conta recomeca do zero.</summary>
	private void Limpar()
	{
		foreach (Node n in GetChildren())
			if (n is ProjetilDesenhado or CargaDeRaioVisual || n.Name.ToString().StartsWith("Zona"))
				n.QueueFree();
		LuzDeKi.ZerarContaDeTeste();
	}

	// =====================================================================
	// FAMILIA 1 -- O MECANISMO
	// =====================================================================
	/// <summary>
	/// A LUZ E FILHA DO TIRO, E ELA E A FONTE QUE O CENARIO JA USA.
	///
	/// A checagem do TIPO nao e formalidade: o aviso que abriu este trabalho foi que um sistema de
	/// iluminacao inteiro ja tinha sido construido neste port e o dono mandou REVERTER. Exigir
	/// `PointLight2D` -- a mesma classe do <see cref="Fogo"/> e da <see cref="Aura"/> -- e o teste de
	/// que ninguem inventou um segundo caminho de luz por baixo.
	/// </summary>
	private void Familia1()
	{
		_linhas.Add("=== FAMILIA 1: o tiro e mais uma fonte, pelo mecanismo que ja existe ===");

		Settings.LuzesDeKiDeTeste(16);

		// ---- DE DIA NAO NASCE NODE NENHUM
		Iluminacao.EscuridaoDeTeste(0f);
		LuzDeKi.ZerarContaDeTeste();
		ProjetilDesenhado dia = PorTiro(Longe);
		Ok("AO MEIO-DIA o tiro nao pendura luz nenhuma (zero node, zero custo)",
		   Luz(dia) == null && LuzDeKi.Acesas == 0);
		dia.QueueFree();

		// ---- DE NOITE NASCE
		Iluminacao.EscuridaoDeTeste(1f);
		LuzDeKi.ZerarContaDeTeste();
		ProjetilDesenhado noite = PorTiro(Longe);
		PointLight2D? l = Luz(noite);

		Ok("A NOITE o tiro tem luz, e ela e uma PointLight2D -- a mesma fonte do cenario", l != null);
		Ok("ela e FILHA do tiro (anda junto de graca, morre junto por construcao)",
		   l != null && l.GetParent() == noite);
		Ok("ela fica na CABECA (posicao local zero -- `Position` do node E a cabeca)",
		   l != null && l.Position == Vector2.Zero);

		// ---- A COR E A DO KI DE QUEM ATIROU
		Ok($"a COR da luz e a cor do ki do dono ({CorDeKi.ToHtml(false)}), a mesma que tinge o sprite",
		   l != null && l.Color.IsEqualApprox(CorDeKi));
		if (l != null)
			Nota($"luz: cor {l.Color.ToHtml(false)}, energia {l.Energy:0.00}, escala {l.TextureScale:0.00}");

		// ---- SEM SOMBRA: e a linha que decide o custo de uma luz que ANDA
		Ok("a sombra esta DESLIGADA (uma luz que anda refaria os oclusores todo quadro)",
		   l != null && !l.ShadowEnabled);
		Ok("a luz esta LIGADA e com energia (pegou vaga no orcamento)",
		   l is { Enabled: true } && l.Energy > 0.01f);

		// ---- A TEXTURA E COMPARTILHADA: uma so na vida do processo
		ProjetilDesenhado outro = PorTiro(Longe);
		Ok("dois tiros dividem a MESMA textura radial (o cache do `Fogo`; sem ele, 2,15 ms por luz)",
		   l != null && Luz(outro) is { } l2 && ReferenceEquals(l.Texture, l2.Texture));

		// ---- O TAMANHO SEGUE O `wavemult`, como a arte ja segue
		ProjetilDesenhado muro = PorTiro(Longe, escala: 4f);
		Ok("o Final Flash (wavemult 4) ilumina MAIS longe que o Ki Wave (wavemult 1)",
		   Luz(muro) is { } lm && l != null && lm.TextureScale > l.TextureScale);

		// ---- A CARGA NA MAO TAMBEM ACENDE, e ela e um ataque de ki nascendo
		var carga = new CargaDeRaioVisual { Name = "CargaDeTeste", Cor = CorDeKi };
		AddChild(carga);
		Ok("a CARGA na mao (o brilho antes do raio sair) tambem lanca luz, e da mesma cor",
		   Luz(carga) is { } lc && lc.Color.IsEqualApprox(CorDeKi));

		noite.QueueFree();
		outro.QueueFree();
		muro.QueueFree();
		carga.QueueFree();
	}

	// =====================================================================
	// FAMILIA 2 -- MORRE JUNTO
	// =====================================================================
	/// <summary>
	/// ZERO LUZ ORFA -- e esta e a familia que existe porque o projeto ja teve tres defeitos desse
	/// feitio nesta semana (aureola presa, relogio que nao rearmava, pedido de musica eterno).
	///
	/// Ela nao confere so o node: confere o CONTADOR. Um contador que sobe e nao desce nao da defeito
	/// visivel no dia -- ele vai somando ate o teto e, dali em diante, nenhum tiro do jogo volta a
	/// brilhar. E o tipo de falha que so aparece depois de uma hora de partida.
	/// </summary>
	private Node2D _caixa = null!;

	private void Familia2Montar()
	{
		_linhas.Add("=== FAMILIA 2: a luz morre com o tiro (zero orfa) ===");

		Iluminacao.EscuridaoDeTeste(1f);
		Settings.LuzesDeKiDeTeste(16);

		_caixa = new Node2D { Name = "ZonaDeTeste" };
		AddChild(_caixa);

		for (int i = 0; i < 10; i++) PorTiro(Longe, pai: _caixa);

		Ok("10 tiros acesos = 10 luzes contadas", LuzDeKi.Acesas == 10);
		Ok("10 luzes na arvore", ContarLuzes(_caixa) == 10);

		foreach (Node n in _caixa.GetChildren()) n.QueueFree();
	}

	private void Familia2Conferir()
	{
		Ok("depois que os 10 tiros morreram, sobrou ZERO luz na arvore", ContarLuzes(_caixa) == 0);
		Ok("e o contador voltou a ZERO (sem isso o teto entope e ninguem mais brilha)",
		   LuzDeKi.Acesas == 0);
	}

	private Node2D _zona2 = null!;
	private bool _seisAcesas;

	private void Familia2Zona()
	{
		// A ZONA INTEIRA SUMINDO leva as luzes junto, sem ninguem varrer nada. E o caso de trocar de
		// planeta com tiro no ar -- o `World` solta a zona e nao vai atras de luz nenhuma.
		LuzDeKi.ZerarContaDeTeste();
		_zona2 = new Node2D { Name = "ZonaQueSome" };
		AddChild(_zona2);
		for (int i = 0; i < 6; i++) PorTiro(Longe, pai: _zona2);
		_seisAcesas = LuzDeKi.Acesas == 6;
		_zona2.QueueFree();
	}

	private void Familia2ZonaConferir() =>
		Ok("trocar de planeta (a zona inteira sumindo) leva as 6 luzes junto",
		   _seisAcesas && LuzDeKi.Acesas == 0);

	// =====================================================================
	// FAMILIA 3 -- O TETO
	// =====================================================================
	/// <summary>
	/// O TETO SEGURA UMA ZONA CHEIA. 256 e o teto de tiros de uma zona no servidor
	/// (`GameServer.Projeteis.MaxProjeteisPorZona`) e o cliente desenha todos -- ele nao tem teto
	/// proprio de desenho. Sem um teto de LUZ, uma briga cheia decidiria o quadro na maquina do
	/// jogador.
	///
	/// Ela confere as DUAS metades: o teto segura (nao passa de 8) e ele DEVOLVE (um tiro que morre
	/// libera a vaga pro proximo). So a primeira metade ficaria verde com um sistema que acende oito
	/// luzes no comeco da partida e nunca mais nenhuma.
	/// </summary>
	private Node2D _cheia = null!;
	private readonly List<ProjetilDesenhado> _muitos = [];

	private void Familia3Montar()
	{
		_linhas.Add("=== FAMILIA 3: o teto, com a zona cheia (256 tiros) ===");

		Iluminacao.EscuridaoDeTeste(1f);
		Settings.LuzesDeKiDeTeste(8);

		_cheia = new Node2D { Name = "ZonaCheia" };
		AddChild(_cheia);
		_muitos.Clear();
		for (int i = 0; i < 256; i++) _muitos.Add(PorTiro(Longe, pai: _cheia));

		Ok("com 256 tiros no ar e teto 8, exatamente 8 luzes ACESAS", LuzDeKi.Acesas == 8);
		Ok("as outras 248 ficam DESLIGADAS -- luz desligada nao entra na passada do quadro",
		   ContarLuzesLigadas(_cheia) == 8);
		Nota($"nodes de luz na arvore: {ContarLuzes(_cheia)} (o node e barato; a PASSADA e que nao e)");

		// ---- O TIRO QUE PASSOU DO TETO CONTINUA UM TIRO
		Ok("o tiro que nao pegou vaga continua desenhado (o efeito degrada, o tiro nao some)",
		   _muitos[^1].Cor.IsEqualApprox(CorDeKi) && _muitos[^1].IsInsideTree());

		for (int i = 0; i < 8; i++) _muitos[i].QueueFree();
	}

	private void Familia3Conferir() =>
		Ok("os 8 que morreram devolveram as 8 vagas", LuzDeKi.Acesas == 0);

	private void Familia3Devolve()
	{
		ProjetilDesenhado novo = PorTiro(Longe, pai: _cheia);
		Ok("e o proximo tiro ja acende com a vaga liberada (o teto DEVOLVE, nao so segura)",
		   Luz(novo) is { Enabled: true } && LuzDeKi.Acesas == 1);
		_cheia.QueueFree();
	}

	// =====================================================================
	// A CENA DAS FAMILIAS 4 E 5
	// =====================================================================
	/// <summary>Quantos tiros a medida de custo poe na tela. Ver <see cref="Familia4"/>.</summary>
	private const int TirosNaTela = 64;

	/// <summary>O teto da qualidade ALTA, lido de onde o jogo o le. Ver `Settings.OrcamentoDeLuzDeKi`.</summary>
	private static int TetoAlto => new Settings { Grafico = Settings.GraficoAlto }.OrcamentoDeLuzDeKi;

	private readonly List<PointLight2D> _luzesNaTela = [];
	private readonly List<Vector2I> _ondeOlhar = [];

	/// <summary>
	/// ONDE SE OLHA O CHAO pra saber se aquela luz acendeu: 24 px a ESQUERDA da cabeca do raio.
	///
	/// Nao em cima dela: ali esta o sprite do proprio tiro, que e claro com luz e sem luz. O ponto
	/// tem que ser CHAO -- so assim "clareou" quer dizer "a luz chegou no cenario", que e o pedido
	/// do dono, e nao "o tiro continua desenhado". E fora do halo da primitiva, que tem 13,5 px de
	/// raio na cabeca: 24 px e chao limpo, perto o bastante pra o degrade ainda estar forte.
	/// </summary>
	private static readonly Vector2I OlhoNoChao = new(-24, 0);

	/// <summary>
	/// ESPALHA OS TIROS PELA TELA. Uma luz FORA da tela nao custa quase nada (o motor a descarta), e
	/// medir 64 luzes empilhadas fora do quadro devolveria um custo lindo e mentiroso.
	/// </summary>
	private void MontarACena()
	{
		Iluminacao.EscuridaoDeTeste(1f);
		Settings.LuzesDeKiDeTeste(TirosNaTela);
		LuzDeKi.ZerarContaDeTeste();

		Vector2 tela = GetViewportRect().Size;
		if (tela.X < 100) tela = new Vector2(1280, 720);

		var cena = new Node2D { Name = "ZonaDaMedida" };
		AddChild(cena);

		for (int i = 0; i < TirosNaTela; i++)
		{
			int col = i % 8, lin = i / 8;
			var onde = new Vector2(tela.X * (col + 0.5f) / 8f, tela.Y * (lin + 0.5f) / 8f);
			ProjetilDesenhado t = PorTiro(onde, pai: cena, tipo: TipoDeProjetil.Beam);
			t.Mirar(onde, onde + new Vector2(0, 48));
			if (Luz(t) is not { } l) continue;
			_luzesNaTela.Add(l);
			_ondeOlhar.Add((Vector2I)onde + OlhoNoChao);
		}
		Nota($"cena da medida: {TirosNaTela} raios espalhados pela tela, {_luzesNaTela.Count} com luz");
	}

	/// <summary>Deixa acesas as <paramref name="n"/> primeiras e apaga o resto.</summary>
	private void AcenderApenas(int n)
	{
		for (int i = 0; i < _luzesNaTela.Count; i++) _luzesNaTela[i].Enabled = i < n;
	}

	// =====================================================================
	// FAMILIA 4 -- O CUSTO, MEDIDO
	// =====================================================================
	/// <summary>
	/// QUANTOS MILISSEGUNDOS POR QUADRO CADA TETO CUSTA.
	///
	/// ============================ POR QUE ELA ISOLA A PASSADA DE LUZ, E NADA MAIS ============================
	/// Os MESMOS 64 nodes ficam na arvore o tempo inteiro; entre uma fase e outra so muda o `Enabled`
	/// das luzes. Ou seja: mesma contagem de node, mesmo `_Process`, mesmo desenho de sprite, mesma
	/// interpolacao. A unica diferenca entre as fases e quantas luzes o motor tem que compor no
	/// quadro -- que e exatamente a pergunta.
	///
	/// Uma medida que criasse e destruisse tiros entre as fases mediria criacao de node junto, e o
	/// numero nao serviria pra escolher teto nenhum.
	///
	/// O VSYNC SAI, senao toda fase mede 16,7 ms e as tres empatam. Os primeiros quadros de cada fase
	/// sao jogados fora: o primeiro quadro com luz nova paga alocacao que nao se repete.
	/// ====================================================================================================
	/// </summary>
	private const int QuadrosDeAquecimento = 15, QuadrosMedidos = 90;

	private double _relogioDaFase;
	private int _quadrosDaFase;
	private readonly List<(string Nome, double Ms)> _medidas = [];

	/// <summary>Junta um quadro na fase atual. Verdadeiro quando a fase acabou.</summary>
	private bool Medir(double delta)
	{
		_quadrosDaFase++;
		if (_quadrosDaFase <= QuadrosDeAquecimento) return false;
		_relogioDaFase += delta;
		return _quadrosDaFase >= QuadrosDeAquecimento + QuadrosMedidos;
	}

	private void FecharFase(string nome)
	{
		_medidas.Add((nome, _relogioDaFase * 1000.0 / QuadrosMedidos));
		_relogioDaFase = 0;
		_quadrosDaFase = 0;
	}

	private void Familia4()
	{
		_linhas.Add("=== FAMILIA 4: o custo por quadro, medido (nao estimado) ===");

		foreach ((string nome, double ms) in _medidas) Nota($"{nome,-30} {ms,7:0.000} ms/quadro");

		double zero = _medidas[0].Ms, alto = _medidas[1].Ms, cheio = _medidas[2].Ms;
		Nota($"custo da luz: {alto - zero:+0.000;-0.000} ms com {TetoAlto}, {cheio - zero:+0.000;-0.000} ms com {TirosNaTela}");

		// ---- O TETO ALTO TEM QUE CABER NO QUADRO. 16,7 ms e um quadro a 60 Hz; 1 ms e 6% dele.
		Ok($"o teto ALTO ({TetoAlto} luzes) custa menos de 1 ms por quadro", alto - zero < 1.0);
		Ok($"e o teto CHEIO ({TirosNaTela} luzes) tambem cabe no quadro", cheio - zero < 4.0);

		// ============================ O QUE ESTA MEDIDA NAO PROVA, E VALE DIZER ============================
		// Nao ha aqui um "e por isso que o teto existe": nesta maquina a diferenca entre 8 e 64 fica
		// DENTRO DO RUIDO -- luz 2D sem sombra e barata, e quem escolheu isso foi o `ShadowEnabled =
		// false`. Escrever a conclusao oposta seria inventar um numero pra justificar uma decisao.
		//
		// Quem justifica o teto e a FAMILIA 6: acima de um certo numero por bloco de cenario o motor
		// simplesmente NAO DESENHA a luz. Gastar orcamento acima disso nao compra imagem nenhuma.
		// ============================================================================================
		Nota($"a diferenca entre {TetoAlto} e {TirosNaTela} esta no ruido: luz 2D SEM SOMBRA e barata (ver familia 6)");
	}

	// =====================================================================
	// FAMILIA 6 -- O LIMITE DO MOTOR
	// =====================================================================
	/// <summary>
	/// QUANTAS LUZES O GODOT ACEITA NUM MESMO PEDACO DE CENARIO -- e esta e a familia que decide o
	/// teto, e nao a do milissegundo.
	///
	/// ============================ A FOTO ACHOU ISTO, E NENHUM `if` ACHARIA ============================
	/// A primeira rodada desta bancada acendeu 64 luzes sobre um chao de UM pedaco so e a foto mostrou
	/// SEIS FILEIRAS SEM HALO NENHUM: 16 luzes desenhadas, 48 descartadas caladas. Nao ha erro, aviso
	/// nem excecao -- o renderizador de canvas do Godot compoe ate um numero fixo de luzes por canvas
	/// item e ignora o resto.
	///
	/// A checagem de codigo teria passado verde nas 64 (`Enabled == true` nas 64, energia nas 64,
	/// vaga nas 64). E o proprio custo medido teria mentido junto: 64 luzes "custaram" o mesmo que 8
	/// porque 48 delas nunca foram desenhadas.
	/// ==============================================================================================
	///
	/// ============================ E POR QUE ISSO NAO CONDENA O EFEITO ============================
	/// O limite e POR CANVAS ITEM, e o chao do jogo nao e um item so: o cenario e um `TileMapLayer`
	/// e o Godot o desenha em BLOCOS. Esta familia mede as duas situacoes lado a lado -- as mesmas
	/// 64 luzes sobre um chao inteiro e sobre um chao em blocos -- e a diferenca entre os dois
	/// numeros e a prova de que o limite e por pedaco.
	///
	/// O QUE ELE DECIDE: o teto ALTO nao passa do que o motor desenha num pedaco. Orcamento acima
	/// disso e orcamento que compra descarte.
	/// =========================================================================================
	/// </summary>
	private int _acesasNoInteiro, _acesasEmBlocos;

	private void Familia6()
	{
		_linhas.Add("=== FAMILIA 6: quantas luzes o MOTOR desenha, e nao quantas foram ligadas ===");

		Nota($"chao em UM pedaco  : {_acesasNoInteiro} de {_luzesNaTela.Count} luzes chegaram ao chao");
		Nota($"chao em BLOCOS ({LadoDoBloco}px): {_acesasEmBlocos} de {_luzesNaTela.Count} chegaram ao chao");

		Ok("num pedaco so de cenario o motor DESCARTA luz calada acima de um limite",
		   _acesasNoInteiro < _luzesNaTela.Count);
		Ok("e o limite e POR PEDACO: o mesmo cenario em blocos desenha MAIS luzes",
		   _acesasEmBlocos > _acesasNoInteiro);

		// ============================ O TETO TEM QUE FICAR ABAIXO DO MURO, E COM FOLGA ============================
		// Nao basta caber: as vagas do pedaco sao DISPUTADAS. A fogueira do cenario (ate 25 numa
		// Vegeta) e a aura de cada corpo aceso entram na mesma conta. Um teto colado no muro faria a
		// luz do tiro apagar a da fogueira ao lado -- um defeito que aparece so as vezes, so em certos
		// mapas, e que ninguem ia conseguir reproduzir.
		// ====================================================================================================
		Ok($"o teto ALTO ({TetoAlto}) fica ABAIXO do muro do motor, com folga pras luzes do cenario",
		   _acesasNoInteiro >= TetoAlto + 2);
	}

	/// <summary>Quantas das luzes desta cena de fato clarearam o chao. Ver <see cref="OlhoNoChao"/>.</summary>
	private int ContarQueChegaramAoChao(Image apagado, Image aceso, string rotulo)
	{
		int n = 0;
		float maior = 0;
		foreach (Vector2I p in _ondeOlhar)
		{
			if (p.X < 0 || p.Y < 0 || p.X >= apagado.GetWidth() || p.Y >= apagado.GetHeight()) continue;
			float d = aceso.GetPixel(p.X, p.Y).Luminance - apagado.GetPixel(p.X, p.Y).Luminance;
			maior = MathF.Max(maior, d);
			if (d > 0.02f) n++;
		}
		// O MAIOR SALTO VAI NO RELATORIO: "quantas acenderam" e um contador, e um contador nao diz se
		// o que acendeu da pra VER. Este numero e o que responde "a luz esta forte o bastante".
		Nota($"{rotulo}: maior salto de luminancia no chao {maior:0.000}");
		return n;
	}

	// =====================================================================
	// FAMILIA 5 -- O PIXEL
	// =====================================================================
	/// <summary>
	/// O CHAO EM VOLTA DO TIRO CLAREIA DE VERDADE.
	///
	/// Duas fotos do MESMO cenario, so com o `Enabled` das luzes trocado. Nao ha como isso ficar
	/// verde com a luz apagada: se a `PointLight2D` nao estivesse compondo no quadro, as duas fotos
	/// seriam byte a byte iguais.
	/// </summary>
	private double _brilhoApagado, _brilhoAceso;

	private void Familia5()
	{
		_linhas.Add("=== FAMILIA 5: o PIXEL do chao, e nao o `Enabled` escrito ===");
		Nota($"brilho medio do quadro -- apagado {_brilhoApagado:0.0000}, aceso {_brilhoAceso:0.0000}");
		Ok("com a luz acesa o CHAO esta mais claro do que com ela apagada",
		   _brilhoAceso > _brilhoApagado * 1.02);
	}

	/// <summary>O quadro de agora, gravado em disco. Nulo = headless (nao ha quadro).</summary>
	private Image? Foto(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;

		string caminho = $"user://luzdeki-{nome}.png";
		img.SavePng(caminho);
		Nota($"foto: {ProjectSettings.GlobalizePath(caminho)}");
		return img;
	}

	/// <summary>
	/// O brilho medio do quadro. De 4 em 4 pixels: a media de 1/16 do quadro e a mesma media, e ler
	/// 1920x1080 pixel a pixel pela fronteira do motor custa mais que o quadro que ela esta medindo.
	/// </summary>
	private static double BrilhoMedio(Image img)
	{
		double soma = 0;
		int w = img.GetWidth(), h = img.GetHeight(), n = 0;
		for (int y = 0; y < h; y += 4)
			for (int x = 0; x < w; x += 4) { soma += img.GetPixel(x, y).Luminance; n++; }
		return n > 0 ? soma / n : 0;
	}

	// =====================================================================
	// O RELOGIO
	// =====================================================================
	public override void _Process(double delta)
	{
		// AS FAMILIAS 1 A 3 ANDAM UMA POR QUADRO -- ver o cabecalho sobre o `QueueFree`.
		if (_passo < _passos.Count) { _passos[_passo++](); return; }

		switch (_fase)
		{
			// ============================ AS FOTOS VEM ANTES DA MEDIDA DE TEMPO ============================
			// Porque foi a FOTO que descobriu que 48 das 64 luzes nunca eram desenhadas -- e sem isso a
			// medida de tempo teria dito, com toda a confianca, que 64 luzes custam o mesmo que 8.
			// Medir depois de saber o que esta na tela e a ordem certa.
			// ==========================================================================================

			// ---- O CHAO EM UM PEDACO SO: apagado, depois aceso
			case 0:
				AcenderApenas(0);
				_fase = 1;
				break;

			case 1:
				_apagadoInteiro = Foto("1-um-pedaco-apagado");
				if (_apagadoInteiro == null) { SemQuadro(); break; }
				AcenderApenas(TirosNaTela);
				_fase = 2;
				break;

			case 2:
			{
				Image? aceso = Foto("2-um-pedaco-aceso");
				if (aceso == null) { SemQuadro(); break; }
				_acesasNoInteiro = ContarQueChegaramAoChao(_apagadoInteiro!, aceso, "chao em UM pedaco");

				// ---- TROCA O CHAO: o mesmo cenario, agora em blocos, como o `TileMapLayer` do jogo
				_chaoInteiro.Visible = false;
				_chaoLadrilhado.Visible = true;
				AcenderApenas(0);
				_fase = 3;
				break;
			}

			// ---- O CHAO EM BLOCOS: apagado, depois aceso
			case 3:
				_apagadoEmBlocos = Foto("3-em-blocos-apagado");
				if (_apagadoEmBlocos == null) { SemQuadro(); break; }
				_brilhoApagado = BrilhoMedio(_apagadoEmBlocos);
				AcenderApenas(TirosNaTela);
				_fase = 4;
				break;

			case 4:
			{
				Image? aceso = Foto("4-em-blocos-aceso");
				if (aceso == null) { SemQuadro(); break; }
				_acesasEmBlocos = ContarQueChegaramAoChao(_apagadoEmBlocos!, aceso, "chao em BLOCOS");
				_brilhoAceso = BrilhoMedio(aceso);

				// SEM VSYNC: com ele toda fase mede 16,7 ms e as tres empatam.
				DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
				Engine.MaxFps = 0;
				AcenderApenas(0);
				_fase = 5;
				break;
			}

			// ---- E SO AGORA O RELOGIO
			case 5:
				if (!Medir(delta)) break;
				FecharFase($"0 luzes (so os {TirosNaTela} raios)");
				AcenderApenas(TetoAlto);
				_fase = 6;
				break;

			case 6:
				if (!Medir(delta)) break;
				FecharFase($"{TetoAlto} luzes (o teto ALTO)");
				AcenderApenas(TirosNaTela);
				_fase = 7;
				break;

			case 7:
				if (!Medir(delta)) break;
				FecharFase($"{TirosNaTela} luzes (sem teto)");
				Familia4();
				Familia5();
				Familia6();
				_fase = 10;
				break;

			// ============================ A FAMILIA 7 COMECA POR APAGAR A CENA ANTERIOR ============================
			// 64 raios desenhados na tela sao 64 sprites claros por cima do chao que ela vai medir. E a
			// noite dela nao e a mesma: chao de DIA multiplicado pelo modulate da meia-noite.
			// ==================================================================================================
			case 10:
				Limpar();
				_luzesNaTela.Clear();
				_ondeOlhar.Clear();
				foreach (Node b in _chaoLadrilhado.GetChildren())
					if (b is Polygon2D bloco) bloco.Color = CorDoChaoDeDia;
				_noiteDeVerdade.Visible = true;
				Settings.LuzesDeKiDeTeste(16);
				LuzDeKi.ZerarContaDeTeste();
				_fase = 11;
				break;

			// ---- A NOITE VAZIA: e contra ESTA foto que todo salto de brilho e medido
			case 11:
			{
				_basalDaNoite = Foto("5-noite-sem-tiro");
				if (_basalDaNoite == null) { SemQuadro(); break; }

				_raio = PorRaio(P1);

				// ---- DEFEITO DA 7a: a luz que a analogia com a aura teria deixado
				_tiroFraco = PorRaio(Fraco);
				if (Luz(_tiroFraco) is { } lf) lf.Energy = EnergiaDoDefeito;

				// ---- DEFEITO DA 7b E DA 7d: a luz DESGRUDADA do tiro.
				// Nao e um caso inventado: e exatamente o que se obtem guardando a luz num lugar e a
				// vida dela noutro, que e o feitio dos tres defeitos recentes deste projeto. Uma vez
				// solta, ela nao segue o tiro (7b) e nao morre com ele (7d) -- os dois de graca.
				_tiroSolto = PorRaio(Q1);
				if (Luz(_tiroSolto) is { } ls)
				{
					ls.GetParent().RemoveChild(ls);
					AddChild(ls);
					ls.Position = Q1;
					_luzSolta = ls;
				}
				_fase = 12;
				break;
			}

			// ---- O RAIO NASCEU: mede o chao e manda os dois andarem
			case 12:
			{
				Image? f = Foto("6-raio-nasce");
				if (f == null) { SemQuadro(); break; }
				_7aHonesto = Salto(f, P1);
				_7aFraco = Salto(f, Fraco);
				_ondeEstavaAoNascer = _raio.Position.Round();

				_raio.Mirar(P2, P2 - Rastro);
				_tiroSolto.Mirar(Q2, Q2 - Rastro);
				_esperandoAndar = 0;
				_fase = 13;
				break;
			}

			// ---- ANDANDO. O node interpola (`Suavizacao`), entao espera-se ele CHEGAR e nao um relogio
			case 13:
				if (_raio.Position.DistanceTo(P2) > 2f && ++_esperandoAndar < 90) break;
				_fase = 14;
				break;

			case 14:
			{
				Image? f = Foto("7-raio-andou");
				if (f == null) { SemQuadro(); break; }
				_ondeEstavaDepois = _raio.Position.Round();
				_7bNoP2 = Salto(f, P2);
				_7bNoP1 = Salto(f, P1);
				_7bSolta = Salto(f, Q1);   // o tiro estragado esta em Q2; se Q1 acendeu, a luz ficou pra tras

				_raio.QueueFree();
				_tiroFraco.QueueFree();
				_tiroSolto.QueueFree();   // a luz SOLTA nao e filha dele: ela sobrevive, que e o defeito
				_fase = 15;
				break;
			}

			// ---- OS TIROS SUMIRAM: sobrou clarao?
			case 15:
			{
				Image? f = Foto("8-raio-sumiu");
				if (f == null) { SemQuadro(); break; }
				_7dHonesto = Salto(f, P2);
				_7dSolta = Salto(f, Q1);

				_luzSolta.QueueFree();

				// ---- 7c: DUAS CORES DE KI, e ao lado os dois mesmos tiros com a luz BRANCA
				PorRaio(CorVermelha, KiVermelho);
				PorRaio(CorAzul, KiAzul);
				if (Luz(PorRaio(MutVermelha, KiVermelho)) is { } m1) m1.Color = Colors.White;
				if (Luz(PorRaio(MutAzul, KiAzul)) is { } m2) m2.Color = Colors.White;
				_fase = 16;
				break;
			}

			case 16:
			{
				Image? f = Foto("9-duas-cores");
				if (f == null) { SemQuadro(); break; }
				_razaoVermelha = RazaoVermelha(f, CorVermelha);
				_razaoAzul = RazaoVermelha(f, CorAzul);
				_razaoMutVermelha = RazaoVermelha(f, MutVermelha);
				_razaoMutAzul = RazaoVermelha(f, MutAzul);
				Familia7();

				// ---- E AGORA OS FPS: a tela cheia de raio outra vez, com oclusores no caminho
				Limpar();
				MontarACena();
				MontarOclusores();
				AcenderApenas(TetoAlto);
				_fase = 17;
				break;
			}

			// ---- O TETO DO JOGO, COM A TELA CHEIA DE TIRO
			case 17:
				if (!Medir(delta)) break;
				FecharFase($"{TirosNaTela} raios, {TetoAlto} luzes (o teto do jogo)");
				_msTeto = _medidas[^1].Ms;
				AcenderApenas(TirosNaTela);
				_fase = 18;
				break;

			// ---- E SEM TETO NENHUM
			case 18:
				if (!Medir(delta)) break;
				FecharFase($"{TirosNaTela} raios, {TirosNaTela} luzes (sem teto)");
				_msSemTeto = _medidas[^1].Ms;
				_fase = 19;
				break;

			case 19:
			{
				Image? f = Foto("10-sem-sombra");
				if (f == null) { SemQuadro(); break; }
				_brilhoSemSombra = BrilhoMedio(f);
				foreach (PointLight2D l in _luzesNaTela) l.ShadowEnabled = true;
				_fase = 20;
				break;
			}

			case 20:
			{
				Image? f = Foto("11-com-sombra");
				if (f == null) { SemQuadro(); break; }
				_brilhoComSombra = BrilhoMedio(f);
				_fase = 21;
				break;
			}

			case 21:
				if (!Medir(delta)) break;
				FecharFase($"{TirosNaTela} luzes COM sombra ({Oclusores} oclusores)");
				_msComSombra = _medidas[^1].Ms;

				// ---- O NASCIMENTO: some tudo, e no quadro seguinte 64 luzes nascem de uma vez
				Limpar();
				foreach (Node n in GetChildren())
					if (n is PointLight2D or LightOccluder2D || n.Name.ToString().StartsWith("Zona")) n.QueueFree();
				_fase = 22;
				break;

			// ============================ O QUADRO DO NASCIMENTO, E NAO O DEPOIS ============================
			// O `delta` que chega no quadro SEGUINTE e a duracao do quadro em que as luzes nasceram --
			// e ali que mora a alocacao e a subida da textura pra GPU. Medir a media dos 90 quadros
			// seguintes diluiria o engasgo em nada, que e exatamente como este defeito passa batido.
			// ==========================================================================================
			case 22:
				PorSessentaEQuatroLuzes(umaTexturaPorTiro: false);
				_fase = 23;
				break;

			case 23:
				_msNasceCompartilhada = delta * 1000.0;
				foreach (Node n in GetChildren())
					if (n.Name.ToString() == "ZonaDeNascimento") n.QueueFree();
				_fase = 24;
				break;

			case 24:
				PorSessentaEQuatroLuzes(umaTexturaPorTiro: true);
				_fase = 25;
				break;

			case 25:
				_msNasceUmaPorTiro = delta * 1000.0;
				Familia8();
				_fase = 9;
				break;

			default: Terminar(); break;
		}
	}

	private Image? _apagadoInteiro, _apagadoEmBlocos;

	private int _fase;

	private void SemQuadro()
	{
		Nota("SEM QUADRO (headless nao renderiza): as familias 4 e 5 nao rodam.");
		Nota("Pra medir custo e ver o pixel: --path . --diagluzdeki --position 1920,0 --resolution 1280x720");
		_fase = 9;
	}

	private void Terminar()
	{
		GD.Print("");
		GD.Print("========== BANCADA DA LUZ DOS ATAQUES DE KI ==========");
		foreach (string l in _linhas) GD.Print(l);
		int ok = _linhas.Count(x => x.StartsWith("  OK", StringComparison.Ordinal));
		GD.Print($"===== FIM: {ok} OK, {_falhas} FALHA(S) =====");
		GetTree().Quit(_falhas == 0 ? 0 : 1);
	}

	// =====================================================================
	// FAMILIA 7 -- UM RAIO SO, DE NOITE, FOTOGRAFADO
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ELA EXISTE SE JA HA A FAMILIA 5 ============================
	/// A familia 5 fotografa 64 luzes de uma vez e compara a MEDIA do quadro. Isso responde "a
	/// `PointLight2D` compoe no quadro?" -- e nao responde nenhuma das quatro perguntas que o dono de
	/// fato faz de um efeito: o clarao anda junto? ele tem a cor do ki DAQUELE jogador? ele some
	/// quando o tiro some? da pra VER?
	///
	/// Uma media de quadro fica verde com a luz presa no lugar do disparo, fica verde com todas as
	/// luzes brancas e fica verde com uma luz orfa acesa pra sempre. As quatro medidas daqui sao no
	/// PIXEL DO CHAO em volta da cabeca do raio, ponto a ponto, e cada uma vem com o DEFEITO INJETADO
	/// ao lado -- o mesmo quadro carrega o tiro certo e o tiro estragado, e a linha so vale se ela
	/// separar os dois.
	/// ============================================================================================
	///
	/// ============================ E A NOITE E A DO JOGO, NAO UM CHAO PRETO ============================
	/// `CanvasModulate` da meia-noite (`1b2242`) sobre um chao de dia. E a unica montagem em que a
	/// pergunta "o chao em volta acende?" quer dizer o que o dono quis dizer -- e ela e mais dura que
	/// a das familias 5 e 6, porque o modulate da noite tambem PISA no que a luz somou.
	/// ==============================================================================================
	/// </summary>
	private const float DaPraVer = 0.05f;

	/// <summary>
	/// ONDE SE OLHA O CHAO em volta da cabeca: meio anel de 26 px, so do lado da FRENTE.
	///
	/// Meio, e nao inteiro: o rastro do raio sai da cabeca pra tras com 23 px de largura, entao os
	/// pontos de tras cairiam em cima do proprio desenho do tiro -- e ai "clareou" quereria dizer "o
	/// tiro continua desenhado", que e verdade com luz e sem luz. 26 px tambem passa fora do halo da
	/// ponta (13,5 px), e ainda esta bem dentro do raio da luz (79 px num tiro comum).
	/// </summary>
	private static readonly Vector2I[] MeioAnel =
		[new(26, 0), new(18, 18), new(0, 26), new(0, -26), new(18, -18)];

	/// <summary>A media da COR do chao no meio anel. Cor, e nao luminancia: a familia 7c le canal.</summary>
	private static Color ChaoEmVolta(Image img, Vector2 centro)
	{
		float r = 0, g = 0, b = 0;
		int n = 0;
		foreach (Vector2I d in MeioAnel)
		{
			Vector2I p = (Vector2I)centro + d;
			if (p.X < 0 || p.Y < 0 || p.X >= img.GetWidth() || p.Y >= img.GetHeight()) continue;
			Color c = img.GetPixel(p.X, p.Y);
			r += c.R; g += c.G; b += c.B; n++;
		}
		return n == 0 ? new Color(0, 0, 0) : new Color(r / n, g / n, b / n);
	}

	/// <summary>Quanto o chao em volta de <paramref name="onde"/> clareou em relacao a noite vazia.</summary>
	private float Salto(Image agora, Vector2 onde) =>
		ChaoEmVolta(agora, onde).Luminance - ChaoEmVolta(_basalDaNoite!, onde).Luminance;

	// ---- ONDE CADA COISA ACONTECE, na tela de 1280x720 ----
	private static readonly Vector2 P1 = new(250, 150), P2 = new(760, 150);   // o raio honesto ANDA
	private static readonly Vector2 Fraco = new(1090, 150);                   // defeito da 7a: luz fraca
	private static readonly Vector2 Q1 = new(250, 420), Q2 = new(760, 420);   // defeito da 7b/7d: luz solta
	private static readonly Vector2 CorVermelha = new(220, 180), CorAzul = new(680, 180);
	private static readonly Vector2 MutVermelha = new(220, 560), MutAzul = new(680, 560);

	/// <summary>Duas fichas de ki que ninguem confunde: um vermelho e um azul.</summary>
	private static readonly Color KiVermelho = new(1f, 0.16f, 0.08f), KiAzul = new(0.12f, 0.42f, 1f);

	/// <summary>
	/// A ENERGIA DO DEFEITO DA 7a. E o numero que a primeira versao desta luz teve de verdade: a
	/// faixa da <see cref="Aura"/> copiada por analogia, que fotografada subiu 0,02 de luminancia no
	/// chao -- um roxo que so aparece pra quem sabe onde olhar. O defeito injetado aqui e o defeito
	/// que ja aconteceu.
	/// </summary>
	private const float EnergiaDoDefeito = 0.14f;

	private Image? _basalDaNoite;
	private ProjetilDesenhado _raio = null!, _tiroFraco = null!, _tiroSolto = null!;
	private PointLight2D _luzSolta = null!;
	private Vector2 _ondeEstavaAoNascer, _ondeEstavaDepois;
	private float _7aHonesto, _7aFraco, _7bNoP2, _7bNoP1, _7bSolta, _7dHonesto, _7dSolta;
	private float _razaoVermelha, _razaoAzul, _razaoMutVermelha, _razaoMutAzul;
	private int _esperandoAndar;

	/// <summary>
	/// QUANTO DO CLARAO E VERMELHO: `dR / (dR + dB)`, sobre o salto de cada canal.
	///
	/// Razao e nao canal cru porque a noite ja e AZUL (`1b2242` tem 0,11 de vermelho contra 0,26 de
	/// azul): comparar `dR` com `dB` em numero absoluto acusaria de azul ate uma luz branca. A razao
	/// tira o tom da noite da conta e sobra a cor de quem atirou.
	/// </summary>
	private float RazaoVermelha(Image agora, Vector2 onde)
	{
		Color basal = ChaoEmVolta(_basalDaNoite!, onde), luz = ChaoEmVolta(agora, onde);
		float dr = MathF.Max(0, luz.R - basal.R), db = MathF.Max(0, luz.B - basal.B);
		return dr + db < 0.005f ? 0.5f : dr / (dr + db);
	}

	private void Familia7()
	{
		_linhas.Add("=== FAMILIA 7: um raio so, na noite do jogo, com o defeito injetado ao lado ===");
		Nota($"noite: CanvasModulate {CorDaMeiaNoite.ToHtml(false)} sobre chao de dia -- escuridao {Iluminacao.Escuridao:0.00}, forca da noite {Iluminacao.ForcaDaNoite():0.00}");

		// ---- 7a: O CHAO EM VOLTA ACENDE, E DA PRA VER
		Nota($"7a  salto de luminancia no chao: raio {_7aHonesto:+0.000;-0.000}  |  defeito (energia {EnergiaDoDefeito:0.00}) {_7aFraco:+0.000;-0.000}");
		Ok($"7a o chao em volta do raio CLAREIA o bastante pra se ver ({_7aHonesto:0.000} > {DaPraVer:0.00}), e a luz fraca de antes NAO clareava",
		   _7aHonesto > DaPraVer && _7aFraco < DaPraVer);

		// ---- 7b: O CLARAO ANDA COM O TIRO
		Nota($"7b  a cabeca do raio saiu de {_ondeEstavaAoNascer} e chegou em {_ondeEstavaDepois}");
		Nota($"7b  chao no destino {_7bNoP2:+0.000;-0.000}  |  chao na origem, depois de o tiro sair de la {_7bNoP1:+0.000;-0.000}");
		Nota($"7b  defeito (luz desgrudada do tiro): o tiro andou pra {Q2} e o chao de {Q1} ficou aceso em {_7bSolta:+0.000;-0.000}");
		Ok("7b o clarao ANDOU: acendeu no destino e a origem voltou ao escuro -- e a luz desgrudada foi pega ficando pra tras",
		   _7bNoP2 > DaPraVer && _7bNoP1 < 0.02f && _7bSolta > DaPraVer);

		// ---- 7c: DUAS CORES DE KI, DOIS CLAROES
		Nota($"7c  quanto do clarao e vermelho -- ki vermelho {_razaoVermelha:0.00}, ki azul {_razaoAzul:0.00}");
		Nota($"7c  defeito (luz branca nos dois): vermelho {_razaoMutVermelha:0.00}, azul {_razaoMutAzul:0.00} -- os dois claroes iguais");
		Ok("7c dois jogadores com ki diferente pintam o chao de cores diferentes, e com a luz branca os dois ficariam IGUAIS",
		   _razaoVermelha - _razaoAzul > 0.25f && MathF.Abs(_razaoMutVermelha - _razaoMutAzul) < 0.08f);

		// ---- 7d: DEPOIS QUE O TIRO SOME, ZERO CLARAO
		Nota($"7d  depois que o tiro sumiu: chao onde ele estava {_7dHonesto:+0.000;-0.000}  |  defeito (luz orfa) {_7dSolta:+0.000;-0.000}");
		Ok("7d o tiro morreu e o chao voltou ao escuro (ZERO clarao) -- e a luz orfa ficou acesa sem dono nenhum, como esta linha exige que apareca",
		   MathF.Abs(_7dHonesto) < 0.015f && _7dSolta > DaPraVer);
	}

	// =====================================================================
	// FAMILIA 8 -- OS FPS COM A TELA CHEIA DE RAIO
	// =====================================================================
	/// <summary>
	/// QUANTOS QUADROS POR SEGUNDO COM A TELA CHEIA DE RAIO, e os dois defeitos que fazem o quadro
	/// afundar de verdade.
	///
	/// ============================ O DEFEITO QUE EU QUERIA INJETAR NAO AFUNDOU NADA ============================
	/// A primeira versao desta familia ligou a SOMBRA nas 64 luzes com 40 oclusores no caminho,
	/// esperando o quadro despencar -- `ShadowEnabled = false` e a linha que a `LuzDeKi` diz que
	/// decide o custo. A foto mostrou a sombra funcionando (os claroes saem picotados em quina reta) e
	/// o relogio mostrou 204 FPS contra 187 SEM sombra: o "defeito" saiu mais RAPIDO que o certo, ou
	/// seja a diferenca inteira estava dentro do ruido.
	///
	/// Entao a linha da sombra deixou de ser sobre TEMPO e passou a ser sobre IMAGEM, que e o que ela
	/// de fato muda: com a sombra ligada o cenario atras de cada oclusor apaga. Afirmar "a sombra
	/// custa caro" com este numero na mao seria inventar medida pra justificar decisao -- e este
	/// projeto tem registro escrito de quatro defeitos que passaram por bancada assim.
	/// ======================================================================================================
	///
	/// ============================ E O QUE AFUNDA MESMO E O NASCIMENTO ============================
	/// O que trava uma briga cheia nao e o quadro parado com 64 tiros no ar: e o INSTANTE em que eles
	/// nascem. `Fogo.Radial` guarda a textura por raio justamente por isso -- sem o cache, cada tiro
	/// montaria um `GradientTexture2D` de 121 KB (a medida antiga: 2,15 ms por luz). Esta familia
	/// injeta esse defeito -- uma textura por tiro em vez da compartilhada -- e mede o quadro do
	/// nascimento nos dois. E um defeito que o olho ve como engasgo e que a media de FPS esconde.
	/// ==========================================================================================
	/// </summary>
	private const int Oclusores = 40;
	private double _msTeto, _msSemTeto, _msComSombra;
	private double _brilhoSemSombra, _brilhoComSombra;
	private double _msNasceCompartilhada, _msNasceUmaPorTiro;

	private void MontarOclusores()
	{
		var caixa = new Node2D { Name = "ZonaDeSombra" };
		AddChild(caixa);
		var forma = new OccluderPolygon2D
		{
			Polygon = [Vector2.Zero, new Vector2(64, 0), new Vector2(64, 64), new Vector2(0, 64)],
		};
		for (int i = 0; i < Oclusores; i++)
			caixa.AddChild(new LightOccluder2D
			{
				Occluder = forma,
				Position = new Vector2(80 + i % 8 * 150, 60 + i / 8 * 130),
			});
	}

	/// <summary>
	/// 64 LUZES NASCENDO DE UMA VEZ. Iguais em tudo menos na textura -- que e o defeito injetado.
	/// </summary>
	private void PorSessentaEQuatroLuzes(bool umaTexturaPorTiro)
	{
		var caixa = new Node2D { Name = "ZonaDeNascimento" };
		AddChild(caixa);
		Vector2 tela = GetViewportRect().Size;
		for (int i = 0; i < TirosNaTela; i++)
			caixa.AddChild(new PointLight2D
			{
				// O DEFEITO E SO ESTA LINHA: raio distinto por tiro nunca acha o cache e monta uma
				// textura nova -- alocacao mais subida pra GPU, por tiro, no quadro do disparo.
				Texture = Fogo.Radial(umaTexturaPorTiro ? 90 + i : 96),
				Color = CorDeKi,
				Energy = 1.8f,
				ShadowEnabled = false,
				BlendMode = Light2D.BlendModeEnum.Add,
				Position = new Vector2(tela.X * (i % 8 + 0.5f) / 8f, tela.Y * (i / 8 + 0.5f) / 8f),
			});
	}

	private void Familia8()
	{
		_linhas.Add("=== FAMILIA 8: os FPS com a tela cheia de raio ===");

		double comTeto = 1000.0 / _msTeto, semTeto = 1000.0 / _msSemTeto, comSombra = 1000.0 / _msComSombra;
		Nota($"{TirosNaTela} raios no ar, {TetoAlto} luzes (o teto do jogo) : {_msTeto:0.000} ms/quadro = {comTeto:0} FPS");
		Nota($"{TirosNaTela} raios no ar, {TirosNaTela} luzes (sem teto)     : {_msSemTeto:0.000} ms/quadro = {semTeto:0} FPS");

		Ok($"com {TirosNaTela} raios no ar e o teto do jogo o quadro NAO afunda ({comTeto:0} FPS)", comTeto > 60);
		Ok($"e mesmo estourando o teto ({TirosNaTela} luzes) ele continua acima de 60 ({semTeto:0} FPS)", semTeto > 60);

		// ---- O DEFEITO QUE AFUNDA: 64 TIROS NASCENDO SEM O CACHE DE TEXTURA
		Nota($"o quadro em que {TirosNaTela} luzes NASCEM -- textura compartilhada {_msNasceCompartilhada:0.0} ms, uma por tiro {_msNasceUmaPorTiro:0.0} ms");
		Ok($"defeito injetado: sem o cache de textura, o quadro do disparo custa {_msNasceUmaPorTiro / MathF.Max(0.01f, (float)_msNasceCompartilhada):0.0}x mais -- o engasgo que a media de FPS esconde",
		   _msNasceUmaPorTiro > _msNasceCompartilhada * 3);

		// ---- A SOMBRA: O DEFEITO QUE A FOTO PEGA E O RELOGIO NAO
		Nota($"sombra ligada em {TirosNaTela} luzes com {Oclusores} oclusores: {_msComSombra:0.000} ms/quadro = {comSombra:0} FPS");
		Nota($"brilho medio do quadro -- sem sombra {_brilhoSemSombra:0.0000}, com sombra {_brilhoComSombra:0.0000}");
		Nota($"o TEMPO da sombra ficou dentro do ruido ({_msComSombra - _msSemTeto:+0.000;-0.000} ms): nao ha numero aqui que justifique `ShadowEnabled = false`");
		Ok("o que a sombra muda e a IMAGEM: com ela ligada o cenario atras dos oclusores APAGA (e o clarao sai picotado em quina reta)",
		   _brilhoComSombra < _brilhoSemSombra * 0.98);
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	/// <summary>Fora da tela: o que so precisa EXISTIR nao precisa ser desenhado.</summary>
	private static readonly Vector2 Longe = new(-9000, -9000);

	/// <summary>
	/// UM TIRO COMO O `World.AoNascerTiro` o faz: cor, tipo e escala ANTES do `AddChild`, porque e o
	/// `_Ready` que pendura a luz e ele le os tres.
	///
	/// SEM `Vestir`: a folha de arte nao muda nada da luz, e sem ela o tiro cai na primitiva -- que
	/// desenha com a `Cor` crua e da a esta bancada um alvo colorido de graca.
	/// </summary>
	/// <summary>O rastro de um raio da bancada: 60 px pra TRAS da cabeca. Ver <see cref="MeioAnel"/>.</summary>
	private static readonly Vector2 Rastro = new(60, 0);

	/// <summary>
	/// UM RAIO APONTADO PRA DIREITA, que e o rumo em que ele vai andar na familia 7. Rumo e rastro
	/// combinados nao sao enfeite: e o que garante que o meio anel de medida cai em CHAO e nao em
	/// cima do desenho do proprio tiro.
	/// </summary>
	private ProjetilDesenhado PorRaio(Vector2 onde, Color? cor = null)
	{
		var t = new ProjetilDesenhado { Tipo = TipoDeProjetil.Beam, Cor = cor ?? CorDeKi, Position = onde };
		AddChild(t);
		t.Mirar(onde, onde - Rastro);
		return t;
	}

	private ProjetilDesenhado PorTiro(Vector2 onde, float escala = 1f, Node? pai = null,
									  TipoDeProjetil tipo = TipoDeProjetil.Blast)
	{
		var t = new ProjetilDesenhado { Tipo = tipo, Cor = CorDeKi, Escala = escala, Position = onde };
		(pai ?? this).AddChild(t);
		t.Mirar(onde, onde);
		return t;
	}

	private static PointLight2D? Luz(Node dono) => dono.GetNodeOrNull<PointLight2D>(LuzDeKi.NomeDoNode);

	private static int ContarLuzes(Node raiz)
	{
		int n = raiz is PointLight2D ? 1 : 0;
		foreach (Node f in raiz.GetChildren()) n += ContarLuzes(f);
		return n;
	}

	private static int ContarLuzesLigadas(Node raiz)
	{
		int n = raiz is PointLight2D { Enabled: true } ? 1 : 0;
		foreach (Node f in raiz.GetChildren()) n += ContarLuzesLigadas(f);
		return n;
	}
}
