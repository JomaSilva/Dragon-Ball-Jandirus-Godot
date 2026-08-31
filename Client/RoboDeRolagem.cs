using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A ROLAGEM DO CENARIO (`--diagrolagem`) ============================
/// O dono, palavra por palavra: *"a movimentacao pros lados (esquerda e direita) esta estranha ...
/// quando ando pros lados parece q o personagem fica borrado/tremendo, mas andando pra cima e pra
/// baixo fica liso sem problemas"*.
///
/// A medicao anterior ja tinha respondido DUAS coisas, e as duas mudam o que esta bancada mede:
///
///   1. o corpo NAO anda na tela -- a camera e filha dele, entao ele fica cravado no meio do
///      viewport e QUEM ROLA E O CENARIO. Medir a posicao do boneco nao mede nada;
///   2. `_pos` (o float cru) e liso e ISOTROPICO -- mesma velocidade nos dois eixos, zero passos
///      pra tras, zero correcao de servidor. O movimento esta bom; o defeito e de DESENHO.
///
/// ============================ ENTAO O QUE ELA MEDE E O ELO DO MEIO ============================
/// Entre `_pos` (liso) e o pixel (aos trancos) ha uma linha so:
///
///     LocalPlayer.NoPontoDaGrade()  -&gt;  Position  -&gt;  Camera2D filha  -&gt;  transformacao de canvas
///
/// A pergunta e **de quanto em quanto o cenario pode se mover**. O erro de quantizacao e
/// `zoom * (exata - desenhada)`, em pixels de TELA, e ele e o tremor inteiro: enquanto ele passeia
/// de 0 a 2 px o cenario anda 2,2,2,4 quando devia andar 2,3 -- nenhum quadro certo.
///
/// A bancada nao acredita na propria conta: ela pergunta ao MOTOR onde o mundo esta desenhado
/// (`GetGlobalTransformWithCanvas`) e cobra que as duas respostas batam. Se a formula aqui e a
/// transformacao de canvas discordarem, a prova reprova antes de qualquer conclusao.
///
/// E ela cobra DUAS coisas ao mesmo tempo, porque uma sozinha aceitaria o remedio errado:
///   * erro de quantizacao abaixo de 1 px de tela (senao ha tremor);
///   * o cenario assentando SEMPRE na mesma fracao de pixel (senao ha cintilacao).
/// Deslizar em subpixel zeraria a primeira e estouraria a segunda.
/// ==========================================================================================
///
/// ============================ E A PROVA CENTRAL E A REGULARIDADE ============================
/// As cobrancas acima medem o passo contra um IDEAL. A que responde a queixa mede o passo contra
/// ELE MESMO: a sequencia de posicoes em pixel de TELA, quadro a quadro, e o quanto os
/// deslocamentos consecutivos discordam entre si -- que e o que o olho enxerga. Ela vem em duas
/// coincidencias que tem que bater: uma lida da transformacao (prova 6 do `Julgar`) e outra
/// correlacionada da FOTO (`JulgarAFoto`).
///
/// O CHAO DELA NAO E ZERO E ISSO ESTA DITO: o passo real e ~2,3 px de tela e a tela e de pixel
/// inteiro, entao o cenario anda 2,2,3,2,3 -- um pixel de hesitacao e o piso da aritmetica. O
/// criterio e `vao <= 1 px`; com a grade antiga o vao era DOIS (2,2,4,2,4), a mesma velocidade com
/// o dobro do tranco.
///
/// E O VEREDITO TEM QUE SER O MESMO NOS DOIS EIXOS (`OsDoisEixos`), porque a queixa do dono NAO e
/// "treme" -- e a diferenca entre os eixos. Uma bancada de um eixo so ficaria verde num conserto
/// que consertasse metade, que e a forma mais provavel de errar aqui.
/// ==========================================================================================
///
/// ============================ E ELA TERMINA NA FOTO, PORQUE JA FOMOS ENGANADOS ============================
/// A memoria desta casa tem um verbete inteiro ("a bancada mede INTENCAO") sobre os quatro defeitos
/// visuais que passaram por 4000 checagens verdes: transformacao escrita != pixel desenhado. Entao a
/// ultima fase FOTOGRAFA quadros consecutivos e mede, por correlacao cruzada, quantos pixels o
/// cenario andou DE VERDADE entre um e o proximo -- e cobra que isso bata com o que a transformacao
/// prometeu. So depois disso o numero das fases anteriores vale como prova.
/// ==========================================================================================================
///
/// ============================ DOIS BERCOS, E ELA DIZ EM QUAL NASCEU ============================
///   `--diagrolagem`      NO MUNDO. Nasce depois de entrar, anda com o piloto automatico do jogo e
///                        mede o corpo do JOGADOR -- o `LocalPlayer` de verdade, com o `MoveRules`,
///                        a camera que o `World` pendurou e o servidor conferindo cada passo.
///   `--diagrolagemlab`   NO LABORATORIO. Nasce ANTES do lobby, monta um palco com cenario texturado
///                        e um corpo movido pelo MESMO `MoveRules.Advance` e desenhado pela MESMA
///                        `LocalPlayer.NoPontoDaGrade`, com a MESMA `World.NovaCamera`.
///
/// O laboratorio e o juiz MAIS FRACO e o relatorio diz isso com todas as letras: ele nao atravessa o
/// `LocalPlayer._Process`, entao nao prova que o jogo CHAMA a grade certa -- prova que a grade e a
/// camera, juntas, produzem o pixel certo. Ele existe porque a pergunta e de DESENHO e nao de rede,
/// e porque uma medicao que so roda com o mundo de pe morre junto com ele.
/// ==========================================================================================
///
/// ============================ O QUE E ASSIMETRICO NAO E MEDIDO AQUI ============================
/// O tremor e SIMETRICO (a grade age igual em X e em Y). O que e assimetrico e a SILHUETA: o sprite
/// de perfil tem ~10 px de largura contra ~26 px de altura, entao o MESMO erro de 1 px vale 10% do
/// corpo andando de lado e 3,8% andando pra cima. Isso e arte, nao codigo -- por isso a bancada
/// cobra o erro nos DOIS eixos por igual: consertar so o horizontal seria tratar o sintoma.
/// ==========================================================================================
///
/// ============================ E ELA TERMINA NUMA IMAGEM PRA OLHO HUMANO ============================
/// `--rolagemtira CAMINHO` grava a tira: kimografo (uma linha de pixel do cenario por quadro,
/// empilhadas -- listras retas = rolagem parelha) por cima do tranco (o erro contra uma rolagem
/// perfeita, ampliado 20x -- linha reta = liso, zigue-zague = tremor). `--rolagemtiraantes CAMINHO`
/// poe a tira de uma rodada anterior AO LADO, e e assim que o par "antes x depois" existe: sao duas
/// rodadas do jogo, e nenhum processo fotografa as duas. Ver `Revelar` e `GravarATira`.
/// ==========================================================================================
///
/// COMO RODAR
///     (no mundo)         Godot --path . --host --rede 7931 --diagrolagem --position 1920,0
///                              --raca Human --conta bancada_rolagem --nome Andarilho
///     (no laboratorio)   Godot --path . --diagrolagemlab --position 1920,0
///
/// Em janela sempre: no headless a fase da FOTO se declara nao-medida em vez de passar de graca.
/// `--rolagemsaida CAMINHO` escreve o relatorio em disco (o console e um cano que morre junto com o
/// lancador).
/// </summary>
public partial class RoboDeRolagem : Node
{
	/// <summary>Nasci antes do lobby e monto o meu proprio palco -- ver "dois bercos".</summary>
	[Export] public bool Laboratorio;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _fase;

	/// <summary>Por onde cada corpo remoto passou, em PONTOS DA GRADE de desenho. Ver `Colher`.</summary>
	private readonly Dictionary<int, (List<double> X, List<double> Y)> _trilhaRemota = [];
	private double _t;
	private int _correcoes;

	/// <summary>Um quadro medido. Tudo lido no MESMO instante -- ver <see cref="Colher"/>.</summary>
	private readonly record struct Quadro(
		Vector2 Exata,
		Vector2 Desenhada,
		Vector2 TelaDoMundo);

	private readonly List<Quadro> _colhidos = [];

	/// <summary>
	/// O QUE CADA RUMO DEU NA PROVA DA REGULARIDADE, guardado ate o fim porque a pergunta do dono nao
	/// e sobre um rumo: e sobre a DIFERENCA entre os dois eixos. Ver <see cref="OsDoisEixos"/>.
	/// </summary>
	private readonly Dictionary<string, double> _regularidade = [];

	/// <summary>O mesmo, medido no PIXEL fotografado em vez de na transformacao. Ver a foto.</summary>
	private readonly Dictionary<string, double> _regularidadeNoPixel = [];

	/// <summary>De quanto em quanto o cenario parou, por rumo. Nulo = nao parou em pixel inteiro.</summary>
	private readonly Dictionary<string, int?> _quantum = [];

	/// <summary>Os paineis ja revelados, um por rumo fotografado -- ver <see cref="Revelar"/>.</summary>
	private readonly List<(string Nome, Image Painel)> _paineis = [];

	/// <summary>
	/// AS FOTOS DA FASE FINAL, ja recortadas. DUAS tiras por quadro, e as duas sao necessarias:
	///   * a do CENARIO diz quanto o mundo andou (tem que bater com a transformacao);
	///   * a do CORPO diz quanto o boneco andou (tem que ser ZERO).
	/// A segunda e o unico juiz possivel da regra que o cabecalho do `LocalPlayer` protege -- ver o
	/// comentario no <see cref="Julgar"/> sobre por que o numero nao consegue prova-la.
	/// </summary>
	private readonly List<(byte[] Cenario, byte[] Corpo)?> _tiras = [];
	private readonly List<Vector2> _telaDaFoto = [];

	/// <summary>
	/// O RECORTE DO CENARIO E QUADRADO, e isso e uma exigencia e nao um enfeite.
	///
	/// Ele era 192x64 quando so o passo LATERAL era fotografado. A prova de pixel agora anda tambem
	/// pra CIMA (ver <see cref="Fotos"/>), e uma tira baixa nao tem margem pra procurar deslocamento
	/// em Y: a correlacao precisa de `alcance` pixels de folga no eixo em que ela desliza, e com 64
	/// de altura menos 2x10 de folga sobrava fita, nao imagem. Quadrado, os dois eixos sao medidos
	/// com exatamente a mesma quantidade de textura -- e um veredito assimetrico passa a ser do JOGO
	/// e nao da regua.
	/// </summary>
	private const int LadoDaTira = 160;

	/// <summary>
	/// LADO DA TIRA DO CORPO, em pixel de tela -- quadrada e centrada no meio do viewport.
	///
	/// PROPORCIONAL AO ZOOM porque o que importa e quantos TEXEIS ela ve, e nao quantos pixels: um
	/// recorte fixo de 40 px enxerga 20 texeis no zoom 2 e so 10 no zoom 4, e com 10 a correlacao
	/// perde o pe. Em pixel de mundo ela e sempre a mesma janela sobre o boneco.
	/// </summary>
	/// (E VEZES A ESCALA DA TELA: quando a base e menor que a janela, a foto sai maior que o viewport
	/// e um recorte em pixel de BASE enxergaria menos boneco do que a conta acima pediu.)
	private int LadoDoCorpo() => Math.Max(24, (int)(20f * ZoomAgora * _escalaDaFoto));

	/// <summary>
	/// QUANTOS PIXELS DE MONITOR CABEM NUM PIXEL DA BASE DE DESENHO, medido da propria foto.
	/// Vale 1 ate a primeira foto sair -- ver <see cref="Fotografar"/>, que e quem o descobre.
	/// </summary>
	private float _escalaDaFoto = 1f;

	/// <summary>Quadros de aquecimento antes de cada medicao -- o passo so estabiliza depois deles.</summary>
	private const int Aquecimento = 40;
	private const int Medidos = 140;

	/// <summary>
	/// QUANTOS QUADROS A FOTO COLHE. Eram 36, e subiu porque a foto deixou de ser so uma conta: ela
	/// vira a TIRA que o dono olha (ver <see cref="Revelar"/>), e cada quadro fotografado e UMA LINHA
	/// dessa tira. Com 36 linhas o tranco cabe na imagem mas nao se enxerga; com 60 ele salta.
	/// </summary>
	private const int Fotografados = 60;

	// ---- o palco do laboratorio (nulo no berco do mundo) ----
	private Node2D? _palcoLab;
	private Node2D? _corpoLab;
	private Vec2 _posLab;
	private int _zoomLab = 1;

	/// <summary>
	/// A GRADE QUE O LABORATORIO USA -- por padrao a do jogo (o zoom), trocavel por
	/// `--rolagemgrade N`.
	///
	/// Existe porque as duas perguntas desta bancada BRIGAM ENTRE SI e so a comparacao mostra isso:
	/// com `--rolagemgrade 1` o palco desenha na grade ANTIGA (pixel de mundo) e o relatorio sai com
	/// o corpo cravado e o cenario tremendo; com a grade do zoom sai o contrario, se o motor estiver
	/// arredondando o corpo por conta propria. Sem o par, cada rodada sozinha parece um veredito.
	///
	/// E LABORATORIO SO: o jogo nao tem este interruptor -- ver `LocalPlayer.GradeDoDesenho`.
	/// </summary>
	private float _gradeLab = 1f;

	private GameClient? C => GameClient.Instance;

	public override void _Ready()
	{
		if (C is { } cli) cli.Corrected += AoCorrigir;
		if (Laboratorio) MontarLaboratorio();
		Anotar(Laboratorio
			? "berco: LABORATORIO (juiz mais fraco -- nao atravessa o LocalPlayer._Process)"
			: "berco: MUNDO (corpo do jogador, MoveRules, camera do World, servidor conferindo)");
	}

	// METODO NOMEADO E NAO LAMBDA -- ver a memoria "assinaturas vazadas": lambda nao se cancela.
	public override void _ExitTree()
	{
		if (C is { } cli) cli.Corrected -= AoCorrigir;
	}

	private void AoCorrigir(Vec2 _) => _correcoes++;

	/// <summary>
	/// OS QUATRO RUMOS, E NAO SO O HORIZONTAL: a queixa e de um eixo so, mas a causa medida age nos
	/// dois. Uma prova que so olhasse pro leste ficaria verde com o vertical quebrado do mesmo jeito.
	/// </summary>
	private static readonly (string Nome, Vector2 Rumo)[] Rumos =
	[
		("leste",  new Vector2(1, 0)),
		("sul",    new Vector2(0, 1)),
		("oeste",  new Vector2(-1, 0)),
		("norte",  new Vector2(0, -1)),
	];

	/// <summary>
	/// OS DOIS RUMOS QUE SAO FOTOGRAFADOS -- um de cada eixo, e e por isso que sao dois.
	///
	/// O relato compara os eixos ("pros lados treme, pra cima nao"), entao a prova de PIXEL tem que
	/// existir nos dois pra que a comparacao seja uma medida e nao uma impressao. Nao sao os quatro
	/// porque cada quadro fotografado custa uma leitura de GPU (`GetImage`), e leste/norte ja bastam:
	/// os quatro rumos passam pela prova de transformacao, que e mais barata.
	/// </summary>
	private static readonly (string Nome, Vector2 Rumo)[] Fotos =
	[
		("leste", new Vector2(1, 0)),
		("norte", new Vector2(0, -1)),
	];

	public override void _Process(double delta)
	{
		if (_acabou) return;
		_t += delta;
		if (!Pronto()) return;

		// ESPERA ASSENTAR. Entrar na zona carrega cenario, e o primeiro segundo tem quadro de 200 ms
		// -- medir passo ali seria medir a carga, nao a rolagem.
		if (_fase == 0)
		{
			if (_t < (Laboratorio ? 1.0 : 3.0)) return;
			_passos.Add($"zoom da camera: {ZoomAgora:0.##}x   viewport: {GetViewport().GetVisibleRect().Size}   "
						+ $"grade de desenho: 1/{(Laboratorio ? _gradeLab : ZoomAgora):0.##} px de mundo"
						+ $"   snap do motor: {ProjectSettings.GetSetting("rendering/2d/snap/snap_2d_transforms_to_pixel")}");
			DizerEmQueTelaRodou();
			Avancar();
			return;
		}

		int rumo = _fase - 1;
		if (rumo < Rumos.Length)
		{
			Andar(Rumos[rumo].Rumo, delta);
			Colher(foto: false);
			if (_colhidos.Count >= Aquecimento + Medidos)
			{
				Julgar(Rumos[rumo].Nome, Rumos[rumo].Rumo, ZoomAgora);
				Avancar();
			}
			return;
		}

		// ============================ DUAS FASES DE FOTO, E NAO UMA ============================
		// A frase do dono e uma ASSIMETRIA: *"pros lados ... borrado/tremendo, mas andando pra cima e
		// pra baixo fica liso"*. Uma foto so do passo lateral responderia METADE dela -- e a metade
		// que responderia seria justamente a que ele ja sabe. O eixo que ele diz estar BOM precisa ser
		// fotografado tambem, porque e ele que transforma "esta liso" em "esta liso IGUAL".
		// ==========================================================================================
		int qualFoto = rumo - Rumos.Length;
		if (qualFoto < Fotos.Length)
		{
			Andar(Fotos[qualFoto].Rumo, delta);
			Colher(foto: true);
			if (_colhidos.Count >= Aquecimento + Fotografados)
			{
				JulgarAFoto(Fotos[qualFoto].Nome, Fotos[qualFoto].Rumo);
				Avancar();
			}
			return;
		}

		Parar();
		Fechar();
	}

	/// <summary>
	/// ============================ EM QUE TELA ESTA RODADA ACONTECEU, E EM QUE UNIDADE ELA MEDE ============================
	/// A resolucao escolhida virou a BASE DE DESENHO (`Settings.Aplicar` escreve `ContentScaleSize`),
	/// e sao DOIS estagios ate o vidro. As duas familias de prova desta bancada olham estagios
	/// diferentes, e confundi-los ja custou uma acusacao errada aqui:
	///
	///   AS CONTAS (provas 1 a 7)   leem a transformacao de canvas, que esta em pixel de BASE.
	///   AS FOTOS                   leem a textura do viewport, que com `stretch/mode = canvas_items`
	///                              sai no tamanho da JANELA -- 1920x1080 mesmo com base 1280x720. O
	///                              motor nao desenha pequeno e amplia: ele desenha grande e poe a
	///                              escala na transformacao.
	///
	/// A primeira versao deste comentario afirmava o contrario ("a foto sai NA BASE, antes da
	/// esticada"). A bancada entao comparava deslocamento em pixel de MONITOR com passo em pixel de
	/// BASE e acusou 0,848 px de tremor numa rodada em que a conta media 0,471 -- a diferenca era o
	/// 1,5x que ninguem tinha convertido. A `Fotografar` agora MEDE a escala da propria imagem.
	///
	/// COM A ESCALA MEDIDA, A FOTO RESPONDE AS DUAS PERGUNTAS: em pixel de base (que e o que julga,
	/// com o mesmo teto em qualquer resolucao) e em pixel de monitor (que e o que o dono ve). A
	/// segunda nao reprova -- ver o porque em `JulgarAFoto`, no bloco "NO VIDRO".
	/// ==========================================================================================
	/// </summary>
	private void DizerEmQueTelaRodou()
	{
		Vector2I janela = DisplayServer.WindowGetSize();
		Vector2 baseDesenho = GetViewport().GetVisibleRect().Size;
		bool cheia = DisplayServer.WindowGetMode() is DisplayServer.WindowMode.Fullscreen
													or DisplayServer.WindowMode.ExclusiveFullscreen;
		float escala = baseDesenho.X > 0 ? janela.X / baseDesenho.X : 0f;
		bool inteira = Math.Abs(escala - MathF.Round(escala)) < 0.02f;

		_passos.Add($"TELA: {(cheia ? "tela cheia" : "janela")} {janela.X}x{janela.Y} | "
					+ $"base de desenho {baseDesenho.X:0}x{baseDesenho.Y:0} | "
					+ $"estica {escala:0.###}x ({(inteira ? "inteira" : "QUEBRADA")})");
		if (!inteira)
			_passos.Add("TELA: a esticada NAO e inteira -- as contas abaixo julgam em pixel de BASE (mesmo teto "
						+ "em qualquer resolucao) e as FOTOS medem tambem em pixel de MONITOR, na linha "
						+ "\"NO VIDRO\". E la que o preco desta escolha aparece em numero.");
	}

	// ==================== DE ONDE SAEM OS NUMEROS, NOS DOIS BERCOS ====================

	private bool Pronto()
		=> Laboratorio ? _corpoLab != null : World.Instancia?.PassoLocalDeTeste != null;

	private float ZoomAgora
		=> Laboratorio ? _zoomLab : World.Instancia?.ZoomDaCameraDeTeste ?? 0f;

	/// <summary>
	/// ANDA UM QUADRO. No mundo quem anda e o piloto automatico do jogo (o mesmo `Destino` do nav
	/// system, que so preenche a direcao e deixa o passo passar pelo `MoveRules`); no laboratorio a
	/// chamada ao `MoveRules.Advance` e feita aqui -- mesma funcao, sem mapa e sem vizinhos.
	/// </summary>
	private void Andar(Vector2 rumo, double delta)
	{
		if (!Laboratorio) { World.Instancia?.AndarDeTeste(rumo); return; }
		_posLab = MoveRules.Advance(_posLab, new Vec2(rumo.X, rumo.Y), (float)delta, 1f, null, out _);
	}

	private void Parar()
	{
		if (!Laboratorio) World.Instancia?.PararDeTeste();
	}

	/// <summary>
	/// UM QUADRO. As quatro leituras saem do mesmo instante de proposito: pedir a posicao exata num
	/// quadro e a desenhada no seguinte mediria o PASSO junto com o erro de arredondamento.
	/// </summary>
	private void Colher(bool foto)
	{
		Vector2 exata, desenhada;
		Node2D palco;

		if (Laboratorio)
		{
			if (_corpoLab is not { } corpo || _palcoLab is not { } p) return;
			// A MESMA LINHA DO JOGO: a grade e a de producao (`NoPontoDaGrade`), nao uma copia daqui.
			exata = new Vector2(_posLab.X, _posLab.Y);
			desenhada = LocalPlayer.NoPontoDaGrade(exata, _gradeLab);
			corpo.Position = desenhada;
			palco = p;
		}
		else
		{
			if (World.Instancia is not { } mundo || mundo.PassoLocalDeTeste is not { } passo) return;
			if (mundo.CorpoDeTeste(C?.LocalId ?? 0) is not { } corpo) return;
			if (corpo.GetParent() is not Node2D p) return;
			exata = passo.Exata;
			desenhada = passo.Desenhada;
			palco = p;
		}

		// OS OUTROS BONECOS TAMBEM. Nao basta o meu ficar na grade: com o arredondamento do motor
		// desligado, um NPC na grade errada sai tremendo do mesmo jeito -- e ninguem olharia pra isso.
		// A TRILHA e guardada, e nao so um sim/nao por quadro: e preciso ver o corpo ANDAR pra saber
		// de quanto em quanto ele para (ver `Fechar`).
		if (!Laboratorio && World.Instancia is { } m2)
			foreach ((int id, Vector2 naGrade) in m2.RemotosNaGradeDeTeste())
			{
				if (!_trilhaRemota.TryGetValue(id, out var t))
					_trilhaRemota[id] = t = ([], []);
				t.X.Add(naGrade.X);
				t.Y.Add(naGrade.Y);
			}

		Transform2D xf = palco.GetGlobalTransformWithCanvas();
		var q = new Quadro(exata, desenhada, xf.Origin);
		_colhidos.Add(q);

		if (!foto) return;
		_telaDaFoto.Add(q.TelaDoMundo);
		_tiras.Add(Fotografar());
	}

	private void Avancar()
	{
		_fase++;
		_colhidos.Clear();
		_tiras.Clear();
		_telaDaFoto.Clear();
		Parar();
	}

	// ==================== O PALCO DO LABORATORIO ====================

	/// <summary>
	/// O PALCO: cenario texturado em coordenada de mundo, um corpo, e a camera DO JOGO pendurada
	/// nele. As tres pecas que o defeito precisa -- e nenhuma a mais.
	///
	/// O CENARIO PRECISA DE TEXTURA, e nao de cor chapada: a fase da foto mede o deslocamento por
	/// correlacao cruzada, e um fundo liso da empate em todo deslocamento. O ruido aqui e
	/// DETERMINISTICO (semente fixa) pra que duas rodadas sejam comparaveis.
	/// </summary>
	private void MontarLaboratorio()
	{
		_zoomLab = Math.Max(1, Boot.Config.Zoom);
		_gradeLab = _zoomLab;
		string[] args = OS.GetCmdlineArgs();
		int ig = Array.IndexOf(args, "--rolagemgrade");
		if (ig >= 0 && ig + 1 < args.Length && float.TryParse(args[ig + 1],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out float g) && g >= 1f)
			_gradeLab = g;

		_palcoLab = new Node2D { Name = "PalcoDaRolagem" };
		AddChild(_palcoLab);

		var chao = new Sprite2D
		{
			Name = "ChaoDoLaboratorio",
			Texture = TexturaDeRuido(1024),
			Centered = false,
			Position = Vector2.Zero,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		_palcoLab.AddChild(chao);

		_corpoLab = new Node2D { Name = "CorpoDaRolagem" };
		_palcoLab.AddChild(_corpoLab);

		// O BONECO DO LABORATORIO. Ele nao e enfeite: e o que a `JulgarAFoto` recorta pra provar que
		// o corpo fica CRAVADO na tela enquanto o cenario anda -- a unica prova possivel dessa regra
		// (ver a prova 2 no `Julgar`). Texturado, e nao chapado, porque um retangulo de cor unica da
		// empate na correlacao e a bancada se declararia incapaz de medir. E MAIOR que o sprite do
		// jogo (48 px contra ~16) so pra caber inteiro no recorte de 40 px de tela.
		_corpoLab.AddChild(new Sprite2D
		{
			Name = "BonecoDoLaboratorio",
			Texture = TexturaDeRuido(48),
			Centered = true,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Modulate = new Color(1f, 0.55f, 0.2f),
		});
		// A CAMERA DO JOGO, e nao uma parecida -- ver `World.NovaCamera`.
		_corpoLab.AddChild(World.NovaCamera(_zoomLab));

		_posLab = new Vec2(512, 512);
		_corpoLab.Position = LocalPlayer.NoPontoDaGrade(new Vector2(_posLab.X, _posLab.Y), _gradeLab);
	}

	/// <summary>Ruido cinza deterministico, um pixel de mundo por texel.</summary>
	private static ImageTexture TexturaDeRuido(int lado)
	{
		var img = Image.CreateEmpty(lado, lado, false, Image.Format.Rgb8);
		uint s = 0x9E3779B9;
		for (int y = 0; y < lado; y++)
			for (int x = 0; x < lado; x++)
			{
				s ^= s << 13; s ^= s >> 17; s ^= s << 5;
				float v = 0.25f + (s & 0xFF) / 255f * 0.55f;
				// UM XADREZ POR BAIXO DO RUIDO: da a correlacao uma estrutura de baixa frequencia,
				// que e o que sobrevive num recorte pequeno.
				if ((((x >> 4) + (y >> 4)) & 1) == 0) v *= 0.72f;
				img.SetPixel(x, y, new Color(v, v * 0.96f, v * 0.88f));
			}
		return ImageTexture.CreateFromImage(img);
	}

	// ==================== O JULGAMENTO ====================

	/// <summary>
	/// O JULGAMENTO DE UM RUMO. Cinco cobrancas, e a ordem importa: a primeira e a que valida as
	/// outras quatro (se a minha formula nao for a do motor, o resto nao vale nada).
	/// </summary>
	private void Julgar(string nome, Vector2 rumo, float zoom)
	{
		var m = _colhidos.GetRange(Aquecimento, _colhidos.Count - Aquecimento);
		if (m.Count < 20) { _falhas.Add($"[{nome}] quadros de menos ({m.Count})"); return; }
		if (zoom < 1f) { _falhas.Add($"[{nome}] camera sem zoom -- a bancada nao mediu nada"); return; }

		Vector2 centro = GetViewport().GetVisibleRect().Size * 0.5f;

		// ============================ 1. A FORMULA E A DO MOTOR? ============================
		// `TelaDoMundo` (lido do canvas) tem que ser `centro - zoom * desenhada`. Se nao for, quem
		// manda no desenho nao e quem eu penso, e o resto desta fase nao vale nada.
		//
		// O ATRASO E MEDIDO, E NAO SUPOSTO -- e a primeira rodada desta bancada ensinou por que. Ela
		// cravava atraso ZERO e reprovou o jogo por "3 px de desacordo" nos quatro rumos: a
		// `Camera2D` so aplica a transformacao de canvas DEPOIS do `_Process`, entao o valor que se
		// le aqui e o do quadro ANTERIOR. Tres pixels e exatamente um passo de caminhada -- a
		// bancada tinha medido o proprio atraso e chamado de defeito. Agora ela testa os dois
		// alinhamentos e diz qual bateu.
		// UM TELEPORTE NAO E UM PASSO. Andando o bastante o corpo cruza uma passagem de zona e o
		// servidor o poe noutro lugar -- a primeira rodada desta bancada leu esse salto de 365 px
		// como "um quadro errou o passo em 366 px" e reprovou a rolagem por causa de uma porta.
		// Quadros com salto acima de um tile sao DESCARTADOS e CONTADOS; muitos deles reprovam,
		// porque ai nao foi teleporte, foi tranco.
		float saltoDemais = Jandirus.Core.World.ZoneCollision.TileSize;
		int saltos = 0;
		var salto = new bool[m.Count];
		for (int i = 1; i < m.Count; i++)
			if ((m[i].Exata - m[i - 1].Exata).Length() > saltoDemais) { salto[i] = true; saltos++; }
		if (saltos > 2)
			_falhas.Add($"[{nome}] {saltos} salto(s) de mais de {saltoDemais:0} px de mundo num rumo so "
						+ "-- isso nao e passagem de zona, e tranco");

		int atraso = 0;
		double maiorDesacordo = double.MaxValue;
		for (int L = 0; L <= 1; L++)
		{
			double pior = 0;
			for (int i = L; i < m.Count; i++)
			{
				if (Perto(salto, i, L)) continue;
				pior = Math.Max(pior, (m[i].TelaDoMundo - (centro - zoom * m[i - L].Desenhada)).Length());
			}
			if (pior < maiorDesacordo) { maiorDesacordo = pior; atraso = L; }
		}
		if (maiorDesacordo > 0.51)
			_falhas.Add($"[{nome}] a transformacao de canvas NAO e `centro - zoom*desenhada` em atraso "
						+ $"nenhum (melhor desacordo {maiorDesacordo:0.###} px) -- o resto desta fase nao vale");

		// ============================ 2. E ONDE ESTA A PROVA DE QUE O CORPO NAO TREME? ============================
		// **NA FOTO, e nao aqui** -- e isso e uma decisao, nao um esquecimento. A camera e FILHA do
		// corpo: `corpo_na_tela = TelaDoMundo + zoom * desenhada`, e a prova 1 acabou de cobrar que
		// `TelaDoMundo = centro - zoom * desenhada`. Substituindo, o corpo da `centro` por
		// construcao, sempre, com qualquer grade -- inclusive com uma grade errada. Uma checagem
		// aqui seria verde por algebra, e nao por medida.
		//
		// Quem responde e a `JulgarAFoto`: ela recorta o BONECO da tela em quadros consecutivos e
		// exige deslocamento ZERO enquanto o cenario anda. Ver la.
		// ==========================================================================================

		// QUANTO O CORPO ANDA POR QUADRO, em pixel de tela -- medido da posicao EXATA, que e a unica
		// que nao depende de arredondamento. Serve so pras mensagens: um relatorio que diz "o cenario
		// so para de 2 em 2" sem dizer o tamanho do passo nao deixa ninguem julgar se 2 e muito.
		double passoDoQuadro = 0;
		for (int i = 1; i < m.Count; i++)
			passoDoQuadro += (zoom * (m[i].Exata - m[i - 1].Exata)).Length();
		passoDoQuadro /= Math.Max(1, m.Count - 1);

		// 3. O ERRO DE QUANTIZACAO -- o tremor inteiro, em pixel de TELA.
		double erroMax = 0, erroAntigoMax = 0;
		foreach (Quadro q in m)
		{
			Vector2 e = zoom * (q.Exata - q.Desenhada);
			erroMax = Math.Max(erroMax, Math.Max(Math.Abs(e.X), Math.Abs(e.Y)));

			// O CODIGO ANTIGO, RECALCULADO SOBRE OS MESMOS QUADROS. Nao e uma segunda medicao: e o
			// mesmo `_pos`, so que na grade que o `Desenhar` usava antes (pixel de MUNDO). Vale como
			// o "antes" desta prova sem precisar de um interruptor no codigo de producao.
			Vector2 antigo = new(MathF.Floor(q.Exata.X), MathF.Floor(q.Exata.Y));
			Vector2 ea = zoom * (q.Exata - antigo);
			erroAntigoMax = Math.Max(erroAntigoMax, Math.Max(Math.Abs(ea.X), Math.Abs(ea.Y)));
		}
		if (erroMax >= 1.0)
			// O PASSO REAL SAI DA MEDICAO e nao de um numero escrito na mao: a frase trazia "~2,3 px",
			// que era o passo do LABORATORIO, e no berco do mundo o corpo anda 1,6. A mensagem
			// explicava o defeito com o numero de outra bancada.
			_falhas.Add($"[{nome}] o cenario so pode parar de {erroMax:0.##} em {erroMax:0.##} px de tela "
						+ $"-- com passo real de {passoDoQuadro:0.0} px isso da tranco em quadro sim quadro nao, "
						+ "e e o tremor do relato");

		// 4. O PASSO DESENHADO NAO PODE ERRAR MAIS QUE UM PIXEL DE TELA. O ideal e continuo
		//    (`zoom * d(exata)`); o desenhado e a diferenca do que o canvas mostrou.
		double piorPasso = 0, prosTras = 0;
		double sentido = rumo.X != 0 ? -Math.Sign(rumo.X) : -Math.Sign(rumo.Y);
		for (int i = atraso + 1; i < m.Count; i++)
		{
			if (Perto(salto, i, atraso)) continue;
			// O IDEAL VEM DO MESMO INSTANTE QUE O DESENHO -- por isso o `atraso` (ver a prova 1).
			Vector2 ideal = -zoom * (m[i - atraso].Exata - m[i - atraso - 1].Exata);
			Vector2 real = m[i].TelaDoMundo - m[i - 1].TelaDoMundo;
			piorPasso = Math.Max(piorPasso, (real - ideal).Length());
			double aoLongo = rumo.X != 0 ? real.X : real.Y;
			if (aoLongo * sentido < -0.001) prosTras++;
		}
		if (piorPasso >= 1.001)
			_falhas.Add($"[{nome}] um quadro errou o passo em {piorPasso:0.##} px de tela (teto 1 px)");
		if (prosTras > 0)
			_falhas.Add($"[{nome}] {prosTras} quadro(s) rolaram PRA TRAS -- isso e tranco, nao passo");

		// 5. NITIDEZ: o cenario tem que assentar SEMPRE na mesma fracao de pixel de tela. Trocar o
		//    tremor por meio pixel de deslize borraria a arte de pixel -- o remedio pior que a
		//    doenca --, e sem esta cobranca a prova 3 sozinha aceitaria exatamente isso.
		double fr0X = Frac(m[0].TelaDoMundo.X), fr0Y = Frac(m[0].TelaDoMundo.Y);
		// (a nitidez nao se importa com teleporte: a fracao de pixel e a mesma antes e depois)
		double piorFrac = 0;
		foreach (Quadro q in m)
			piorFrac = Math.Max(piorFrac,
				Math.Max(Dist(Frac(q.TelaDoMundo.X), fr0X), Dist(Frac(q.TelaDoMundo.Y), fr0Y)));
		if (piorFrac > 0.01)
			_falhas.Add($"[{nome}] o cenario assenta em fracoes de pixel diferentes (ate {piorFrac:0.###}) "
						+ "-- isso e cintilacao, e a arte e de pixel");

		// ============================ 6. A REGULARIDADE DO PASSO -- A PROVA CENTRAL ============================
		// As provas 3, 4 e 5 medem o passo contra um IDEAL. Esta mede o passo contra ELE MESMO: a
		// sequencia de posicoes em pixel de TELA, quadro a quadro, e o quanto os deslocamentos
		// consecutivos discordam entre si. E o que o olho ve -- ninguem enxerga "erro de
		// quantizacao", enxerga o cenario andando ora um tanto ora outro.
		//
		// ============================ O CHAO DESTA CONTA NAO E ZERO, E DIZE-LO E METADE DA PROVA ============================
		// O passo real e ~2,3 px de tela por quadro e a tela e feita de pixels inteiros. Nao existe
		// desenho em que o cenario ande 2,3 px: ele anda 2, 2, 3, 2, 3 -- e essa alternancia de UM
		// pixel e o piso fisico, nao um defeito. Cravar "vao = 0" reprovaria qualquer jogo que
		// existisse, e a bancada estaria medindo a aritmetica em vez do codigo.
		//
		// Com a grade ANTIGA (pixel de MUNDO) no zoom 2, o cenario so podia parar de 2 em 2 px de
		// tela: os passos viravam 2, 2, 4, 2, 4 -- vao DOIS. E a mesma caminhada, a mesma velocidade,
		// com o dobro de hesitacao em cada quadro. E isso que a coluna "na grade ANTIGA" abaixo
		// mostra, recalculada sobre ESTES quadros, sem precisar de uma segunda rodada.
		//
		// ENTAO O CRITERIO E: o cenario pode hesitar UM pixel de tela, que e o minimo que a
		// quantizacao permite, e nao mais. Acima disso a culpa e do codigo, e nao da grade de pixel.
		// ==========================================================================================
		// ============================ E O RELOGIO DO QUADRO E DESCONTADO, SENAO A CONTA MENTE ============================
		// A primeira versao desta prova olhava o passo DESENHADO cru, e reprovou o jogo CONSERTADO --
		// com o vertical pior que o horizontal, que e o oposto da queixa. O motivo nao era desenho: o
		// corpo anda `velocidade * delta`, e o `delta` de um quadro nao e constante. Uma pausa do
		// coletor de lixo, uma leitura de GPU, e o quadro dura 30 ms em vez de 16 -- o cenario andou
		// mesmo o dobro naquele quadro, e andou CERTO, porque o corpo estava mesmo la. A bancada
		// tinha medido o RELOGIO e chamado de tremor. E como a fase da foto le a GPU e a de medicao
		// nao, o rumo fotografado saia sempre pior: uma assimetria inteiramente fabricada por ela.
		//
		// Entao o que se mede e o RESIDUO: passo desenhado MENOS o passo que a posicao exata pedia
		// naquele mesmo quadro. O que sobra e so o que o arredondamento acrescentou -- e e isso que o
		// olho ve como tranco, porque o movimento de verdade o olho aceita.
		//
		// Os tres numeros vao pro relatorio, e o do meio explica os outros dois: o vao do passo
		// DESENHADO (o que a tela fez), o vao do passo IDEAL (o quanto o relogio do quadro oscilou
		// sozinho) e o vao do RESIDUO (o que o desenho acrescentou). So o terceiro reprova.
		// ==========================================================================================
		var passosDaTela = new List<double>();
		var passosIdeais = new List<double>();
		var residuo = new List<double>();
		var residuoAntigo = new List<double>();
		for (int i = atraso + 1; i < m.Count; i++)
		{
			if (Perto(salto, i, atraso)) continue;
			Vector2 d = m[i].TelaDoMundo - m[i - 1].TelaDoMundo;
			// O IDEAL VEM DO MESMO INSTANTE QUE O DESENHO -- por isso o `atraso` (ver a prova 1).
			Vector2 ideal = -zoom * (m[i - atraso].Exata - m[i - atraso - 1].Exata);
			double desenhado = (rumo.X != 0 ? d.X : d.Y) * sentido;
			double devido = (rumo.X != 0 ? ideal.X : ideal.Y) * sentido;
			passosDaTela.Add(desenhado);
			passosIdeais.Add(devido);
			residuo.Add(desenhado - devido);

			// O MESMO `_pos`, na grade que o `Desenhar` usava ANTES. Ver a prova 3: o "antes" desta
			// bancada e recalculado sobre ESTES quadros, e nao lembrado de outra rodada.
			Vector2 a0 = new(MathF.Floor(m[i - atraso - 1].Exata.X), MathF.Floor(m[i - atraso - 1].Exata.Y));
			Vector2 a1 = new(MathF.Floor(m[i - atraso].Exata.X), MathF.Floor(m[i - atraso].Exata.Y));
			Vector2 da = -zoom * (a1 - a0);
			residuoAntigo.Add((rumo.X != 0 ? da.X : da.Y) * sentido - devido);
		}

		(double desvio, double vao) = Esparramo(residuo);
		(double _, double vaoDesenhado) = Esparramo(passosDaTela);
		(double __, double vaoDoRelogio) = Esparramo(passosIdeais);
		(double desvioAntigo, double vaoAntigo) = Esparramo(residuoAntigo);
		_regularidade[nome] = desvio;

		// ============================ E QUEM REPROVA E O DESVIO, NAO O VAO ============================
		// O VAO e o maior menos o menor de 140 amostras, e um extremo nao e uma estatistica: basta UM
		// quadro atipico -- e o relogio do quadro produz um por rodada -- pra ele saltar. Medido no
		// jogo consertado, o vao deu 1,00 em tres rumos e 1,56 no quarto, com o desvio identico nos
		// quatro (0,470 / 0,476 / 0,471 / 0,471). O quarto rumo nao estava pior; ele tinha um soluco.
		// Reprovar pelo vao seria reprovar o soluco -- e, pior, seria FABRICAR uma assimetria entre os
		// eixos, que e exatamente a conclusao que esta bancada existe pra nao chutar.
		//
		// O DESVIO NAO DILUI O DEFEITO DE VERDADE porque este defeito nao e um quadro raro: e o
		// arredondamento de TODO quadro. Na grade antiga ele dobra em todos eles, e o desvio dobra
		// junto -- 0,47 vira 0,94, com a mesma limpeza nos quatro rumos. O vao continua no relatorio
		// porque e ele que diz o tamanho do pior tranco; so nao e ele que julga.
		//
		// O TETO 0,60 fica entre os dois patamares medidos e nao no meio deles de proposito: o piso
		// teorico (residuo espalhado por igual num pixel) e 1/raiz(6) = 0,41, entao 0,60 da 45% de
		// folga pra cima do que o codigo certo produz e ainda para a 36% do que o errado produz.
		// ==========================================================================================
		if (desvio > 0.60)
			_falhas.Add($"[{nome}] o desenho acrescenta {desvio:0.###} px de tela de desvio ao passo "
						+ $"(vao {vao:0.##} px, de {residuo.Min():0.##} a {residuo.Max():0.##}) -- descontado o "
						+ "relogio do quadro, essa hesitacao e o tremor do relato");

		// ============================ 7. DE QUANTO EM QUANTO O CENARIO PODE PARAR ============================
		// A pergunta do cabecalho desta bancada, respondida com um inteiro exato em vez de uma media:
		// o maior divisor comum de todos os deslocamentos que o cenario teve em relacao ao primeiro
		// quadro. Com a grade certa o cenario pode parar em QUALQUER pixel de tela e o divisor da 1;
		// com a grade antiga no zoom 2 ele so parava em pixels pares e o divisor da 2 -- e "so parar
		// de dois em dois" e a definicao do defeito, dita sem estatistica nenhuma.
		//
		// Ela e IMUNE ao relogio do quadro (nao olha o tempo, so o conjunto de posicoes) e imune a
		// outliers (um divisor comum nao tem cauda). E a companheira exata do desvio: uma diz quanto,
		// a outra diz de quanto em quanto.
		// ==========================================================================================
		var aoLongoDaTela = new List<double>();
		foreach (Quadro q in m) aoLongoDaTela.Add(rumo.X != 0 ? q.TelaDoMundo.X : q.TelaDoMundo.Y);
		int? quantum = Quantum(aoLongoDaTela);
		_quantum[nome] = quantum;
		if (quantum is { } qt && qt > 1)
			_falhas.Add($"[{nome}] o cenario so para de {qt} em {qt} px de tela -- com passo real de "
						+ $"{Math.Abs(passosIdeais.Average()):0.0} px isso da o tranco do relato");
		else if (quantum == null)
			_falhas.Add($"[{nome}] o cenario nao para em pixel inteiro de tela -- desliza em subpixel, "
						+ "e arte de pixel deslizando em subpixel BORRA (era o remedio pior que a doenca)");

		double passoMedio = 0;
		for (int i = 1; i < m.Count; i++) passoMedio += (m[i].TelaDoMundo - m[i - 1].TelaDoMundo).Length();
		passoMedio /= m.Count - 1;

		_passos.Add($"{nome,-6} REGULARIDADE: desvio {desvio:0.###} px de tela (teto 0,60) | "
					+ $"na grade ANTIGA daria {desvioAntigo:0.###} | para de {quantum?.ToString() ?? "?"} "
					+ $"em {quantum?.ToString() ?? "?"} px | vao {vao:0.##} (o cru foi {vaoDesenhado:0.##}, "
					+ $"do qual {vaoDoRelogio:0.##} e o relogio do quadro)");
		_passos.Add($"{nome,-6} {m.Count} quadros | passo {passoMedio:0.00} px de tela/quadro | "
					+ $"erro de quantizacao max {erroMax:0.##} px (na grade ANTIGA daria {erroAntigoMax:0.##}) | "
					+ $"pior passo {piorPasso:0.##} px | pra tras {prosTras} | "
					+ $"atraso de leitura {atraso} quadro (desacordo {maiorDesacordo:0.###} px)"
					+ (saltos > 0 ? $" | {saltos} salto(s) de zona descartado(s)" : ""));
	}

	/// <summary>
	/// DE QUANTOS QUADROS A TRANSFORMACAO DE CANVAS ESTA ATRASADA em relacao a posicao exata.
	///
	/// A `Camera2D` so aplica a matriz depois do `_Process`, entao o que se le ali dentro pode ser a
	/// do quadro anterior. Isso e MEDIDO e nao suposto -- a primeira rodada desta bancada cravou zero
	/// e reprovou o jogo por "3 px de desacordo" nos quatro rumos, que era um passo de caminhada: ela
	/// tinha medido o proprio atraso de leitura e chamado de defeito.
	///
	/// A prova 1 do <see cref="Julgar"/> faz esta mesma busca com o descarte de salto de zona junto,
	/// porque la ela tambem serve de veredito ("a formula e a do motor?"). Aqui ela e so alinhamento,
	/// e por isso a versao curta: os quadros de salto entram na conta do pior caso e, no maximo,
	/// empatam os dois candidatos -- nao invertem a escolha.
	/// </summary>
	private int AtrasoDaTransformacao(List<Quadro> m, float zoom)
	{
		Vector2 centro = GetViewport().GetVisibleRect().Size * 0.5f;
		int atraso = 0;
		double melhor = double.MaxValue;
		for (int L = 0; L <= 1; L++)
		{
			double pior = 0;
			for (int i = L; i < m.Count; i++)
				pior = Math.Max(pior, (m[i].TelaDoMundo - (centro - zoom * m[i - L].Desenhada)).Length());
			if (pior < melhor) { melhor = pior; atraso = L; }
		}
		return atraso;
	}

	/// <summary>O quadro `i`, ou o par `i-L`/`i-L-1` que ele usa, caiu num salto?</summary>
	private static bool Perto(bool[] salto, int i, int atraso)
	{
		for (int k = i - atraso - 1; k <= i; k++)
			if (k >= 0 && k < salto.Length && salto[k]) return true;
		return false;
	}

	/// <summary>
	/// O ESPARRAMO DE UMA SERIE DE PASSOS: o desvio padrao e o VAO (maior menos menor).
	///
	/// Os dois, e nao um. O desvio e a medida honesta do tremor medio, mas ele DILUI o tranco raro --
	/// um unico quadro que anda o dobro fica escondido em 140 quadros bem-comportados, e um unico
	/// tranco por segundo e exatamente o que o olho pega. O vao pega o pior caso e nao dilui nada.
	/// Um sozinho aceitaria um defeito que o outro reprova.
	/// </summary>
	private static (double Desvio, double Vao) Esparramo(List<double> v)
	{
		if (v.Count < 2) return (0, 0);
		double media = v.Average();
		double s = Math.Sqrt(v.Sum(x => (x - media) * (x - media)) / v.Count);
		return (s, v.Max() - v.Min());
	}

	/// <summary>
	/// DE QUANTO EM QUANTO ESTA SERIE DE POSICOES PODE PARAR: o maior divisor comum dos
	/// deslocamentos em relacao a primeira. Nulo quando alguma posicao nao cai em pixel inteiro
	/// (deslize em subpixel) ou quando o corpo nao saiu do lugar.
	///
	/// A PRIMEIRA POSICAO E A REFERENCIA e nao o zero absoluto, porque o meio do viewport pode cair
	/// em meio pixel (uma janela de altura IMPAR -- 993 px, medido) e ai TODAS as posicoes sao
	/// meio-inteiras. A diferenca entre elas continua inteira, que e o que esta pergunta quer saber.
	/// </summary>
	private static int? Quantum(List<double> posicoes)
	{
		if (posicoes.Count < 2) return null;
		int g = 0;
		foreach (double p in posicoes)
		{
			double d = p - posicoes[0];
			if (Math.Abs(d - Math.Round(d)) > 0.02) return null;
			g = Mdc(g, (int)Math.Abs(Math.Round(d)));
		}
		return g == 0 ? null : g;
	}

	private static int Mdc(int a, int b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }

	/// <summary>
	/// ============================ O VEREDITO TEM QUE SER O MESMO NOS DOIS EIXOS ============================
	/// Esta e a prova que responde a FRASE do dono, e nao um dos seus sintomas. Ele nao disse "treme";
	/// disse *"pros lados ... borrado/tremendo, mas andando pra cima e pra baixo fica liso"*. A queixa
	/// E a diferenca entre os eixos, e uma bancada que medisse so o horizontal ficaria verde num jogo
	/// em que o vertical tivesse quebrado do mesmo jeito -- ou, pior, num jogo em que o conserto
	/// tivesse consertado um eixo so, que e a forma mais provavel de errar aqui.
	///
	/// SAO DUAS COBRANCAS, e a segunda existe porque a primeira e grossa demais sozinha:
	///   * o VEREDITO (passou/nao passou) tem que ser igual nos dois eixos;
	///   * e o NUMERO tem que ser parecido -- dois eixos aprovados, um com vao 0,2 e outro com 1,0,
	///     passariam os dois e ainda assim um estaria cinco vezes mais tremido que o outro.
	///
	/// O horizontal e o pior entre leste e oeste; o vertical, o pior entre norte e sul. Pior, e nao
	/// media: a media de um rumo bom com um ruim da um numero que nao descreve nenhum dos dois.
	/// ==========================================================================================
	/// </summary>
	private void OsDoisEixos()
	{
		double Pior(params string[] rumos)
		{
			double p = -1;
			foreach (string r in rumos)
				if (_regularidade.TryGetValue(r, out double reg)) p = Math.Max(p, reg);
			return p;
		}

		int PiorQuantum(params string[] rumos)
		{
			int p = 0;
			foreach (string r in rumos)
				if (_quantum.TryGetValue(r, out int? q)) p = Math.Max(p, q ?? 99);
			return p;
		}

		double h = Pior("leste", "oeste"), v = Pior("norte", "sul");
		if (h < 0 || v < 0)
		{
			_falhas.Add("a comparacao entre os eixos nao rodou -- faltou rumo medido");
			return;
		}

		int qh = PiorQuantum("leste", "oeste"), qv = PiorQuantum("norte", "sul");
		bool hOk = h <= 0.60, vOk = v <= 0.60;
		_passos.Add($"OS DOIS EIXOS: horizontal desvio {h:0.###} px, para de {qh} em {qh} ({(hOk ? "liso" : "TREMENDO")}) | "
					+ $"vertical desvio {v:0.###} px, para de {qv} em {qv} ({(vOk ? "liso" : "TREMENDO")}) | "
					+ $"diferenca {Math.Abs(h - v):0.###} px");

		if (hOk != vOk)
			_falhas.Add($"OS EIXOS DISCORDAM: o {(hOk ? "vertical" : "horizontal")} treme (desvio {Math.Max(h, v):0.###} px) "
						+ $"e o {(hOk ? "horizontal" : "vertical")} nao (desvio {Math.Min(h, v):0.###} px) -- "
						+ "e literalmente a frase do dono, e nenhum conserto de um eixo so vale");
		else if (Math.Abs(h - v) > 0.10)
			_falhas.Add($"OS EIXOS DAO O MESMO VEREDITO mas nao o mesmo numero (horizontal {h:0.###} px contra "
						+ $"vertical {v:0.###} px) -- um anda mais duro que o outro, e e disso que a queixa fala");
		if (qh != qv)
			_falhas.Add($"OS EIXOS PARAM EM GRADES DIFERENTES: horizontal de {qh} em {qh} px e vertical de "
						+ $"{qv} em {qv} px -- a assimetria e do desenho, e nao da impressao de quem joga");
	}

	/// <summary>
	/// ============================ E OS OUTROS BONECOS PARAM NA MESMA GRADE? ============================
	/// O corpo local se poe na grade no `Desenhar`; os remotos passam pela MESMA `NoPontoDaGrade`
	/// (ver `RemotePlayer`), e esta e a prova de que passam mesmo. Sem ela, um NPC ao lado do jogador
	/// tremeria enquanto o cenario esta liso -- e ninguem olharia pra isso, porque a queixa fala do
	/// personagem e do cenario.
	///
	/// A CONTA E A MESMA DO CORPO LOCAL, de proposito: <see cref="Quantum"/> sobre a trilha do corpo,
	/// ja em pontos da grade. Ela responde as duas perguntas de uma vez -- devolve nulo se o corpo nao
	/// para em ponto de grade (subpixel) e devolve 2 se ele so para de dois em dois (a grade grossa).
	/// A versao anterior desta prova so sabia perguntar a primeira, e por isso imprimia o mesmo
	/// "36800/36800" na build certa e na errada; ver `World.RemotosNaGradeDeTeste`.
	///
	/// SO CORPOS QUE ANDARAM VALEM. Um NPC parado tem trilha de um ponto so, e um ponto so tem
	/// divisor comum nenhum -- contá-lo como aprovado seria aprovar por imobilidade.
	/// ==========================================================================================
	/// </summary>
	private void OsOutrosBonecos()
	{
		const double andouOBastante = 8;   // pontos da grade; abaixo disso a trilha nao diz nada
		int andaram = 0, fora = 0, grossos = 0;
		foreach ((List<double> X, List<double> Y) t in _trilhaRemota.Values)
		{
			if (t.X.Count < 20) continue;
			bool mexeu = t.X.Max() - t.X.Min() >= andouOBastante || t.Y.Max() - t.Y.Min() >= andouOBastante;
			if (!mexeu) continue;
			andaram++;
			foreach (List<double> eixo in (List<double>[])[t.X, t.Y])
			{
				if (eixo.Max() - eixo.Min() < andouOBastante) continue;
				int? q = Quantum(eixo);
				if (q == null) fora++;
				else if (q > 1) grossos++;
			}
		}

		_passos.Add($"CORPOS REMOTOS: {_trilhaRemota.Count} vistos, {andaram} andaram o bastante pra medir | "
					+ $"{fora} eixo(s) em subpixel | {grossos} eixo(s) parando de mais de 1 em 1 px");
		if (_trilhaRemota.Count == 0)
			_falhas.Add("nenhum corpo remoto apareceu -- a grade dos OUTROS bonecos ficou sem prova");
		else if (andaram == 0)
			_falhas.Add($"os {_trilhaRemota.Count} corpos remotos ficaram parados -- a grade deles nao foi provada "
						+ "(corpo parado passa em qualquer grade)");
		if (fora > 0)
			_falhas.Add($"{fora} eixo(s) de corpo remoto nao param em ponto da grade -- desliza em subpixel, "
						+ "e arte de pixel em subpixel sai com texel de largura irregular");
		if (grossos > 0)
			_falhas.Add($"{grossos} eixo(s) de corpo remoto param de mais de 1 em 1 px de tela -- o boneco do "
						+ "lado treme mesmo com o cenario liso");
	}

	/// <summary>
	/// A MESMA COMPARACAO ENTRE OS EIXOS, so que sobre o que a placa DESENHOU.
	///
	/// Separada da <see cref="OsDoisEixos"/> de proposito: uma le a transformacao (140 quadros por
	/// rumo, barato) e a outra le foto (60 quadros, caro). Juntar as duas numa conta so faria a
	/// prova cara herdar a confianca da barata -- e e exatamente essa mistura que a memoria desta
	/// casa manda evitar.
	/// </summary>
	private void OsDoisEixosNoPixel()
	{
		if (!_regularidadeNoPixel.TryGetValue("leste", out double h) ||
			!_regularidadeNoPixel.TryGetValue("norte", out double v))
		{
			_passos.Add("OS DOIS EIXOS NO PIXEL: nao medi -- faltou foto de um dos eixos");
			return;
		}

		bool hOk = h <= 0.60, vOk = v <= 0.60;
		_passos.Add($"OS DOIS EIXOS NO PIXEL: leste desvio {h:0.###} px ({(hOk ? "liso" : "TREMENDO")}) | "
					+ $"norte desvio {v:0.###} px ({(vOk ? "liso" : "TREMENDO")}) | "
					+ $"diferenca {Math.Abs(h - v):0.###} px");
		if (hOk != vOk)
			_falhas.Add($"OS EIXOS DISCORDAM NA FOTO: {(hOk ? "norte" : "leste")} treme e "
						+ $"{(hOk ? "leste" : "norte")} nao -- e a frase do dono, medida no pixel");
	}

	private static double Frac(double v) => v - Math.Floor(v);
	private static double Dist(double a, double b) { double d = Math.Abs(a - b); return Math.Min(d, 1 - d); }

	// ==================== A FOTO ====================

	/// <summary>
	/// A TIRA DE CENARIO, em cinza. Recortada LONGE do meio da tela: o corpo fica cravado ali e nunca
	/// se move, entao inclui-lo na tira so acrescentaria pixels parados que puxam a correlacao pro
	/// zero -- exatamente o resultado que a bancada quer ser incapaz de fabricar.
	/// </summary>
	private (byte[] Cenario, byte[] Corpo)? Fotografar()
	{
		Image? img = GetViewport().GetTexture()?.GetImage();
		if (img == null || img.GetWidth() < LadoDaTira + 8) return null;

		// ============================ A FOTO E MAIOR QUE O VIEWPORT, E ISSO MUDA TUDO ============================
		// `GetVisibleRect()` devolve a BASE DE DESENHO (a resolucao escolhida); a imagem devolvida
		// pela textura tem o tamanho da JANELA. Com `stretch/mode = canvas_items` o motor nao desenha
		// pequeno e amplia depois -- ele desenha no tamanho da janela e aplica a escala na
		// transformacao de canvas. Em tela cheia a 1280x720 num monitor 1080p a base e 1280x720 e a
		// foto e 1920x1080.
		//
		// Enquanto esta funcao recortava pelo tamanho do viewport, o recorte caia no lugar errado, o
		// deslocamento saia em pixels DE MONITOR e era comparado com um passo em pixels DE BASE. A
		// bancada acusou "0,848 px de tremor" numa rodada em que a transformacao mediu 0,471 -- e a
		// diferenca era exatamente o 1,5x que ninguem tinha convertido.
		//
		// A ESCALA E MEDIDA AQUI, da propria imagem, e nao lida da configuracao: o que interessa e o
		// tamanho do pixel que foi de fato desenhado.
		// ==========================================================================================
		Vector2 tela = GetViewport().GetVisibleRect().Size;
		_escalaDaFoto = tela.X > 0 ? img.GetWidth() / tela.X : 1f;
		int cx = img.GetWidth() / 2, cy = img.GetHeight() / 2;

		// A TIRA DO CENARIO fica LONGE do meio: o corpo esta cravado ali, e pixels parados dentro
		// dela puxariam a correlacao pro zero -- exatamente o resultado que a bancada quer ser
		// incapaz de fabricar.
		//
		// O AFASTAMENTO E EM FRACAO DA TELA, e nao os 400x260 px cravados que estavam aqui. A prova
		// roda em varias resolucoes de proposito (a base de desenho e a resolucao escolhida, ver
		// `Settings.Aplicar`), e num viewport de 1280x720 um recorte a 260 px acima do centro cai em
		// y=100 -- mas numa base menor ele cairia em y negativo e o `Recortar` devolveria nulo. A
		// bancada se declararia cega justamente na resolucao que ela foi feita pra testar.
		byte[]? cenario = Recortar(img, cx - (int)(img.GetWidth() * 0.31f), cy - (int)(img.GetHeight() * 0.36f),
								   LadoDaTira, LadoDaTira);
		// A TIRA DO CORPO e o contrario: centrada no boneco, e so nele.
		int lado = LadoDoCorpo();
		byte[]? corpo = Recortar(img, cx - lado / 2, cy - lado / 2, lado, lado);
		return cenario == null || corpo == null ? null : (cenario, corpo);
	}

	/// <summary>
	/// ============================ A TIRA QUE O DONO OLHA ============================
	/// Tudo acima desta linha e numero. O relato dele nao e um numero -- e *"parece q o personagem
	/// fica borrado/tremendo"* --, e a memoria desta casa e explicita sobre o preco de responder um
	/// relato visual so com campo lido: este projeto ja afirmou "o corpo esta branco" lendo um
	/// uniform, com a foto mostrando 0,0%% de branco. Entao a prova fecha numa IMAGEM.
	///
	/// SAO DUAS METADES, e a segunda so existe porque a primeira nao basta:
	///
	///   O KIMOGRAFO (em cima)  Uma LINHA de pixels do cenario por quadro fotografado, empilhadas.
	///                          Nao ha interpretacao nenhuma aqui: e o pixel que a placa desenhou.
	///                          Um cenario que rola parelho vira listras diagonais RETAS; um que
	///                          hesita vira uma diagonal com degraus. E o pixel cru dizendo tudo --
	///                          mas dizendo baixinho, porque um pixel de erro num passo de dois e
	///                          uma inclinacao de nada.
	///
	///   O TRANCO (embaixo)     A mesma caminhada, so que so o ERRO: quanto o cenario esta adiantado
	///                          ou atrasado em relacao a uma rolagem perfeitamente parelha, AMPLIADO
	///                          20 vezes e desenhado como uma linha. Reta vertical = liso. Zigue-zague
	///                          = tremor, e a LARGURA do zigue-zague e o tamanho do tremor.
	///
	/// A AMPLIACAO E HONESTA E ESTA DITA: sem ela o defeito e invisivel a olho nu numa imagem
	/// estatica (e por isso ele so aparece EM MOVIMENTO, que e a razao de o dono ter levado meses
	/// pra descrever isso). Ampliar o erro nao inventa erro nenhum -- uma rolagem sem tranco da uma
	/// linha reta por mais que se amplie.
	/// ==========================================================================================
	/// </summary>
	private void Revelar(string nome, bool eixoY, List<double> serie)
	{
		if (CaminhoDaTira == null || serie.Count < 8) return;

		// UMA LINHA DE PIXEL POR QUADRO. Andando pros lados, uma LINHA do recorte; andando pra cima,
		// uma COLUNA -- deitada, pra que as duas imagens se leiam do mesmo jeito e a comparacao entre
		// os eixos seja entre as mesmas coisas.
		var linhas = new List<byte[]>();
		foreach ((byte[] Cenario, byte[] Corpo)? t in _tiras)
		{
			if (t is not { } tira) continue;
			var linha = new byte[LadoDaTira];
			for (int k = 0; k < LadoDaTira; k++)
				linha[k] = eixoY
					? tira.Cenario[k * LadoDaTira + LadoDaTira / 2]
					: tira.Cenario[LadoDaTira / 2 * LadoDaTira + k];
			linhas.Add(linha);
		}
		if (linhas.Count < 8) return;

		const int faixa = 8, vao = 6;
		int alturaTranco = serie.Count;
		int alt = faixa + linhas.Count + vao + alturaTranco + vao;
		var painel = Image.CreateEmpty(LadoDaTira, alt, false, Image.Format.Rgb8);
		var fundo = new Color(0.10f, 0.10f, 0.12f);
		painel.Fill(fundo);
		int meio = LadoDaTira / 2;

		// ============================ A FAIXA DE CIMA E O VEREDITO, EM COR ============================
		// Verde passou, vermelho nao -- quem abre a imagem sabe o resultado antes de entender o
		// desenho. E o CRITERIO TEM QUE SER O MESMO DO RELATORIO: a primeira versao pintava pelo vao
		// e o relatorio julgava pelo desvio, entao a tira saia com faixa VERMELHA numa rodada que
		// terminou "TUDO VERDE". Uma imagem que contradiz o texto que ela ilustra e pior que nenhuma.
		//
		// E dentro da faixa vai um risco dizendo QUAL EIXO este painel mediu -- deitado pro passo
		// lateral, em pe pro passo pra cima. Sem ele os quatro paineis de um par "antes x depois" sao
		// quatro retangulos parecidos, e quem olha tem que confiar na ordem em que foram colados.
		// ==========================================================================================
		(double desvioPx, double _) = Esparramo(serie);
		Color veredito = desvioPx <= 0.60 ? new Color(0.20f, 0.75f, 0.35f) : new Color(0.85f, 0.20f, 0.18f);
		for (int y = 0; y < faixa; y++)
			for (int x = 0; x < LadoDaTira; x++)
				painel.SetPixel(x, y, veredito);

		var risco = new Color(0.08f, 0.08f, 0.10f);
		if (eixoY)
			for (int y = 1; y < faixa - 1; y++)
				for (int k = -1; k <= 1; k++) painel.SetPixel(meio + k, y, risco);
		else
			for (int x = meio - 14; x <= meio + 14; x++)
				for (int k = -1; k <= 1; k++) painel.SetPixel(x, faixa / 2 + k, risco);

		for (int i = 0; i < linhas.Count; i++)
			for (int x = 0; x < LadoDaTira; x++)
			{
				float v = linhas[i][x] / 255f;
				painel.SetPixel(x, faixa + i, new Color(v, v, v));
			}

		// ============================ O TRANCO: O ATRASO ACUMULADO DO DESENHO, AMPLIADO ============================
		// A serie e o RESIDUO (passo desenhado menos passo devido), entao a soma dela e exatamente o
		// quanto o cenario esta adiantado ou atrasado em relacao a onde o corpo esta de verdade -- ou
		// seja, o erro de quantizacao, o tremor inteiro. Ele e LIMITADO PELA GRADE por construcao: um
		// pixel de tela de largura com a grade certa, dois com a antiga. E por isso que a largura do
		// zigue-zague nesta imagem E o tamanho do defeito, e nao uma ilustracao dele.
		//
		// Somar a serie e nao plotar quadro a quadro porque um grafico de barras de "2,3,2,3" nao se
		// distingue de "2,4,2,4" de relance; ja a faixa que a soma varre dobra de largura, e dobrar
		// de largura o olho pega.
		// ==========================================================================================
		int y0 = faixa + linhas.Count + vao;
		var guia = new Color(0.28f, 0.28f, 0.34f);
		for (int i = 0; i < alturaTranco; i++) painel.SetPixel(meio, y0 + i, guia);

		var atrasos = new List<double>(serie.Count);
		double acumulado = 0;
		foreach (double r in serie) { acumulado += r; atrasos.Add(acumulado); }
		double centro = atrasos.Average();

		var traco = new Color(1f, 0.72f, 0.25f);
		for (int i = 0; i < atrasos.Count && i < alturaTranco; i++)
		{
			int x = Mathf.Clamp(meio + (int)Math.Round((atrasos[i] - centro) * 20.0), 1, LadoDaTira - 2);
			// UM TRACO E NAO UM PONTO: com um pixel so, a linha some no meio do fundo escuro.
			for (int k = -1; k <= 1; k++) painel.SetPixel(Mathf.Clamp(x + k, 0, LadoDaTira - 1), y0 + i, traco);
		}

		_paineis.Add((nome, painel));
	}

	/// <summary>Um retangulo da tela, em cinza. Nulo quando nao cabe.</summary>
	private static byte[]? Recortar(Image img, int x0, int y0, int larg, int alt)
	{
		if (x0 < 0 || y0 < 0 || x0 + larg >= img.GetWidth() || y0 + alt >= img.GetHeight()) return null;
		var saida = new byte[larg * alt];
		for (int y = 0; y < alt; y++)
			for (int x = 0; x < larg; x++)
			{
				Color c = img.GetPixel(x0 + x, y0 + y);
				saida[y * larg + x] =
					(byte)Mathf.Clamp((int)((c.R * 0.30f + c.G * 0.59f + c.B * 0.11f) * 255f), 0, 255);
			}
		return saida;
	}

	/// <summary>
	/// A FOTO -- e ela julga DUAS coisas, uma em cada tira.
	///
	///   CENARIO  correlaciona quadros CONSECUTIVOS, mede quantos pixels o mundo andou de verdade, e
	///            cobra que seja o que a transformacao de canvas prometeu. E o que faz os numeros das
	///            fases anteriores valerem como prova do que esta na tela.
	///   CORPO    a MESMA correlacao no recorte do boneco, e o resultado tem que ser ZERO. Esta e a
	///            unica prova possivel da regra que o cabecalho do `LocalPlayer` protege -- que o
	///            corpo e a camera param no MESMO ponto da grade --, porque por algebra ela e sempre
	///            verdadeira (ver a prova 2 no `Julgar`). Um conserto que trocasse o cenario tremendo
	///            pelo boneco tremendo passaria por tudo, menos por aqui.
	///
	/// O ATRASO DE UM QUADRO E MEDIDO, NAO SUPOSTO: `GetImage` dentro do `_Process` devolve o quadro
	/// ANTERIOR (o atual ainda nao foi desenhado). Em vez de cravar o atraso, a bancada testa os dois
	/// alinhamentos e diz qual bateu -- se nenhum bater, e porque o pixel nao segue a transformacao,
	/// que e justamente o que esta fase existe pra descobrir.
	/// </summary>
	private void JulgarAFoto(string nome, Vector2 rumo)
	{
		bool eixoY = rumo.X == 0;

		// ============================ O ALCANCE DA BUSCA SAI DO PASSO QUE REALMENTE ACONTECEU ============================
		// Ele era `zoom * 5` -- quatro passos de folga sobre um passo NOMINAL de 1,15 px de mundo por
		// quadro. E o passo desta fase nao e o nominal: cada quadro fotografado paga um `GetImage`,
		// que e leitura de GPU, e o quadro dura duas ou tres vezes mais. O corpo anda
		// `velocidade * delta` e andou tres vezes mais tambem -- 12 px de tela num alcance de 10.
		//
		// Uma busca que nao alcanca o deslocamento verdadeiro nao devolve "nao sei": ela devolve o
		// melhor candidato DENTRO da janela, com confianca. Foi assim que esta prova acusou "o pixel
		// hesitou 8,28 px por quadro" numa rolagem que a prova 6, nos mesmos quadros, mediu em 0,47:
		// os quadros lentos batiam na parede da busca e voltavam com um numero errado.
		//
		// Agora o alcance sai do maior passo que a posicao EXATA de fato deu nesta fase, mais folga.
		// A medida deixa de depender de uma suposicao sobre o relogio da maquina que a roda.
		// ==========================================================================================
		double maiorPasso = 0;
		for (int i = 1; i < _colhidos.Count; i++)
			maiorPasso = Math.Max(maiorPasso, (ZoomAgora * (_colhidos[i].Exata - _colhidos[i - 1].Exata)).Length());
		// EM PIXEL DE MONITOR, que e a unidade da foto -- ver `Fotografar`. Com a base menor que a
		// janela o passo na foto e maior que o passo na base, e uma busca dimensionada em pixel de
		// base ficaria curta exatamente na resolucao que o dono pediu pra poder usar.
		maiorPasso *= _escalaDaFoto;
		int alcance = Math.Max(8, (int)Math.Ceiling(maiorPasso) + 3);
		int tetoDoAlcance = (LadoDaTira - 20) / 2;
		if (alcance > tetoDoAlcance)
		{
			_passos.Add($"FOTO {nome}: o passo chegou a {maiorPasso:0.#} px de tela e a tira so comporta busca "
						+ $"de {tetoDoAlcance} px -- nao medi (maquina engasgada? recorte pequeno demais?)");
			_falhas.Add($"FOTO {nome}: o passo passou do que a tira consegue procurar -- a prova de pixel nao rodou");
			return;
		}

		int uteis = _tiras.Count(t => t != null);
		if (uteis < 8)
		{
			_passos.Add($"FOTO {nome}: nao medida ({uteis} tiras) -- rodou sem janela? headless nao renderiza");
			_falhas.Add($"FOTO {nome}: a fase de pixel nao mediu nada -- rode COM janela");
			return;
		}

		// QUANTO O PIXEL ANDOU DE VERDADE, quadro a quadro. Medido UMA vez e usado por duas provas
		// diferentes -- a que compara com a transformacao e a que olha so pra propria serie.
		var andados = new List<int?>(_tiras.Count);
		andados.Add(null);
		for (int i = 1; i < _tiras.Count; i++)
			andados.Add(_tiras[i] is { } a && _tiras[i - 1] is { } b
				? Deslocamento(b.Cenario, a.Cenario, LadoDaTira, LadoDaTira, alcance, eixoY)
				: null);

		(int acertos, int total, int lag) melhor = (0, 0, 0);
		for (int lag = 0; lag <= 1; lag++)
		{
			int acertos = 0, total = 0;
			for (int i = 1; i < andados.Count; i++)
			{
				if (andados[i] is not { } dx) continue;
				int j = i - lag;
				if (j < 1 || j >= _telaDaFoto.Count) continue;
				total++;
				// A COMPARACAO E EM PIXEL DE MONITOR, que e a unidade em que `dx` foi medido. A
				// transformacao promete em pixel de BASE, e por isso ela e esticada aqui: sem isso a
				// prova acusava "o pixel nao segue a transformacao (29%)" em tela cheia a 1280x720
				// num monitor 1080p -- ela estava comparando 4 com 2,67 e chamando de desacordo.
				Vector2 prometido = (_telaDaFoto[j] - _telaDaFoto[j - 1]) * _escalaDaFoto;
				if (Math.Abs(dx - (eixoY ? prometido.Y : prometido.X)) <= 1.001) acertos++;
			}
			if (total > 0 && acertos > melhor.acertos) melhor = (acertos, total, lag);
		}

		if (melhor.total < 8)
		{
			_passos.Add($"FOTO {nome} cenario: {melhor.total} pares legiveis -- liso demais pra correlacionar");
			_falhas.Add($"FOTO {nome}: nao houve par de quadros de cenario legivel");
		}
		else
		{
			double taxa = melhor.acertos / (double)melhor.total;
			_passos.Add($"FOTO {nome} cenario: o pixel andou o que a transformacao prometeu em "
						+ $"{melhor.acertos}/{melhor.total} pares ({taxa:P0}), atraso de {melhor.lag} quadro");
			if (taxa < 0.90)
				_falhas.Add($"FOTO {nome}: o pixel do CENARIO nao segue a transformacao ({taxa:P0}) -- "
							+ "os numeros das fases anteriores nao provam o que esta na tela");
		}

		// ============================ A REGULARIDADE, AGORA MEDIDA NO PIXEL ============================
		// A prova 6 do `Julgar` faz esta mesma conta sobre a TRANSFORMACAO. Esta faz sobre o que a
		// placa de video realmente desenhou, e por isso ela nao e uma repeticao: a memoria desta casa
		// tem um verbete inteiro ("a bancada mede INTENCAO") sobre quatro defeitos visuais que
		// passaram por milhares de checagens verdes porque a checagem lia o valor escrito e nao o
		// pixel aceso. Aqui a serie de deslocamentos sai de correlacionar FOTOS -- se as duas provas
		// discordarem, quem manda no desenho nao e quem este arquivo pensa.
		//
		// E ELA E A QUE ACUSA O DEFEITO DE ORIGEM. Com a grade de pixel de MUNDO no zoom 2, a
		// transformacao ainda era coerente com o pixel (o cenario andava exatamente o que ela
		// prometia -- a prova acima ficava VERDE), mas o que ela prometia era 2, 2, 4, 2, 4. So esta
		// conta pega isso na foto.
		// ==========================================================================================
		// O RELOGIO DO QUADRO E DESCONTADO AQUI TAMBEM, e aqui ele e PIOR: cada quadro fotografado
		// paga uma leitura de GPU (`GetImage`), que faz o quadro durar o dobro sem aviso. O corpo
		// anda `velocidade * delta` e andou mesmo o dobro -- medir o deslocamento cru mediria o custo
		// da propria camera. Ver o bloco correspondente na prova 6 do `Julgar`.
		// SAO DOIS ATRASOS EMPILHADOS, E OS DOIS SAO MEDIDOS. O `melhor.lag` acima e o atraso entre a
		// FOTO e a transformacao (o `GetImage` devolve o quadro anterior); o `atraso` aqui e o atraso
		// entre a transformacao e a posicao EXATA (a `Camera2D` so aplica a matriz depois do
		// `_Process`). Somar so um dos dois desalinha a serie em um quadro, e um quadro de
		// desalinhamento vira ~2,7 px de "residuo" que nao existe -- foi o que fez esta prova acusar
		// tremor de 5 px num jogo que a prova 6, com o alinhamento certo, mediu em 0,47.
		int atraso = AtrasoDaTransformacao(_colhidos, ZoomAgora);
		var serie = new List<double>();        // em pixel de BASE -- e a serie que julga
		var noVidro = new List<double>();      // em pixel de MONITOR -- e a serie que so informa
		double sentido = eixoY ? -Math.Sign(rumo.Y) : -Math.Sign(rumo.X);
		for (int i = 1; i < andados.Count; i++)
		{
			if (andados[i] is not { } px) continue;
			int j = i - melhor.lag - atraso;
			if (j < 1 || j >= _colhidos.Count) continue;
			Vector2 ideal = -ZoomAgora * (_colhidos[j].Exata - _colhidos[j - 1].Exata);
			double devido = (eixoY ? ideal.Y : ideal.X) * sentido;
			serie.Add(px * sentido / _escalaDaFoto - devido);
			noVidro.Add(px * sentido - devido * _escalaDaFoto);
		}

		if (serie.Count < 8)
		{
			_passos.Add($"FOTO {nome} regularidade: {serie.Count} deslocamentos legiveis -- nao medi");
			_falhas.Add($"FOTO {nome}: nao deu pra medir a regularidade no pixel");
		}
		else
		{
			// O JUIZ E O DESVIO, PELO MESMO MOTIVO DA PROVA 6 (ver la): o vao e um extremo de ~100
			// amostras e um soluco da maquina o dobra sozinho. O vao continua no relatorio porque e
			// ele que diz o tamanho do pior tranco -- so nao e ele que julga.
			(double desvioPx, double vaoPx) = Esparramo(serie);
			_regularidadeNoPixel[nome] = desvioPx;
			_passos.Add($"FOTO {nome} regularidade NO PIXEL: desvio {desvioPx:0.###} px de base (teto 0,60), "
						+ $"de {serie.Min():0.##} a {serie.Max():0.##} px por quadro (vao {vaoPx:0.##}) "
						+ $"em {serie.Count} quadros, busca de +-{alcance} px de monitor");
			if (desvioPx > 0.60)
				_falhas.Add($"FOTO {nome}: o pixel hesitou {desvioPx:0.###} px de base de desvio por quadro "
							+ $"(de {serie.Min():0.##} a {serie.Max():0.##}) -- isto e o tremor, visto na tela "
							+ "e nao na conta");

			// ============================ E QUANTO ISSO VALE NO VIDRO, QUE E ONDE O OLHO ESTA ============================
			// A prova acima e em pixel de BASE, e o teto dela vale igual em qualquer resolucao. Mas o
			// olho do dono conta pixel de MONITOR: a mesma hesitacao de meio pixel de base custa meio
			// pixel de monitor a 1x e tres quartos a 1,5x. O numero e o mesmo defeito, na unidade em
			// que ele incomoda.
			//
			// **E ELE E EXATAMENTE `base * escala`, POR CONSTRUCAO.** Isso esta dito aqui porque a
			// primeira versao deste bloco apresentava a diferenca entre os dois como se fosse uma
			// medida ("a esticada acrescenta X px") -- e ela dava ZERO sempre, porque as duas series
			// saem da MESMA correlacao, uma dividida pela escala. Era algebra vestida de medicao, e
			// esta casa ja tem verbete sobre isso.
			//
			// O QUE SE APRENDE COM ELE, ENTAO: a resolucao menor **nao acrescenta desordem nova a
			// rolagem, ela AMPLIA a que ja existe**. E por isso que 1,5x incomoda mais que 1x sem que
			// haja nada errado no codigo -- e por isso que a resposta certa e rotular a opcao (como a
			// tela de opcoes ja faz) em vez de tirar a resolucao da lista, que o dono pediu pra ter.
			//
			// O QUE ELE **NAO** ALCANCA, dito em voz alta: a largura do TEXEL dentro dos sprites. A
			// 1,5x um texel vira ora um ora dois pixels de monitor, e isso e CINTILACAO (a arte
			// fervendo parada), nao TRANCO (o cenario hesitando ao andar). A correlacao mede o quanto
			// a imagem se deslocou, e um deslocamento e o que ela acha mesmo com a textura fervendo.
			// Quem quiser provar aquilo precisa de outra bancada.
			// ==========================================================================================
			(double desvioVidro, _) = Esparramo(noVidro);
			_passos.Add($"FOTO {nome} NO VIDRO: desvio {desvioVidro:0.###} px de MONITOR "
						+ $"(= {desvioPx:0.###} de base x {_escalaDaFoto:0.##} de esticada, por construcao) -- "
						+ (_escalaDaFoto > 1.02f
							? "a resolucao menor nao acrescenta desordem nova, AMPLIA a que ha; a cintilacao "
							  + "de texel que ela tambem causa NAO e medida aqui"
							: "escala inteira: o vidro conta o mesmo que a base"));
		}

		// ---- e o boneco, no MESMO par de quadros, tem que ficar parado ----
		int parado = 0, lidos = 0, maior = 0;
		for (int i = 1; i < _tiras.Count; i++)
		{
			if (_tiras[i] is not { } a || _tiras[i - 1] is not { } b) continue;
			if (Deslocamento(b.Corpo, a.Corpo, LadoDoCorpo(), LadoDoCorpo(), alcance, eixoY) is not { } d) continue;
			lidos++;
			if (d == 0) parado++;
			maior = Math.Max(maior, Math.Abs(d));
		}
		if (lidos < 8)
		{
			_passos.Add($"FOTO {nome} corpo: {lidos} pares legiveis -- o recorte do boneco nao tem textura "
						+ "suficiente pra correlacionar (sprite parado e chapado?)");
			_falhas.Add($"FOTO {nome}: nao deu pra medir se o CORPO fica parado na tela");
		}
		else
		{
			double taxaCorpo = parado / (double)lidos;
			_passos.Add($"FOTO {nome} corpo: cravado na tela em {parado}/{lidos} pares ({taxaCorpo:P0}), "
						+ $"maior deslocamento visto {maior} px");
			if (taxaCorpo < 0.90)
				_falhas.Add($"FOTO {nome}: o CORPO andou na tela ({taxaCorpo:P0} parados, ate {maior} px) -- "
							+ "o cenario deixou de tremer as custas do boneco, que e trocar de defeito");
		}

		Revelar(nome, eixoY, serie);
	}

	/// <summary>
	/// QUANTOS PIXELS A TIRA ANDOU EM X, por soma de diferencas absolutas. Nulo quando o melhor
	/// candidato nao se destaca do segundo: recorte sem textura da empate, e um empate lido como
	/// medida seria a bancada inventando um numero.
	/// </summary>
	private static int? Deslocamento(byte[] antes, byte[] agora, int larg, int alt, int alcance, bool eixoY)
	{
		// A FOLGA VAI NO EIXO EM QUE A TIRA DESLIZA. Ao procurar deslocamento em X e preciso sobrar
		// `alcance` colunas de cada lado (senao a comparacao le fora da tira); em Y a folga vai nas
		// linhas. Trocar isso de lugar mediria o vizinho do pixel em vez do pixel.
		int margemX = eixoY ? 8 : alcance;
		int margemY = eixoY ? alcance : 8;
		if (larg <= margemX * 2 + 2 || alt <= margemY * 2 + 2) return null;

		// ============================ O PULO DE LINHA VAI NO EIXO QUE **NAO** DESLIZA ============================
		// Ele existe pra baratear a conta (metade das amostras), e no eixo errado ele CEGA a medida.
		// A arte e desenhada com zoom inteiro: no zoom 2 cada texel ocupa 2x2 pixels de tela, entao
		// amostrar so as linhas pares faz o deslocamento de UM pixel devolver uma imagem identica --
		// d=0 e d=1 empatam com diferenca zero.
		//
		// Foi assim que a bancada reprovou o corpo do laboratorio: *"FOTO norte: o CORPO andou na
		// tela (0%% parados, ate 1 px)"*, num boneco que esta cravado no meio da tela por construcao.
		// Ela nao viu o boneco andar; ela viu um empate e escolheu um dos empatados.
		// ==========================================================================================
		int puloX = eixoY ? 2 : 1;
		int puloY = eixoY ? 1 : 2;
		long melhor = long.MaxValue, segundo = long.MaxValue;
		int qual = 0;
		for (int d = -alcance; d <= alcance; d++)
		{
			long soma = 0;
			int n = 0;
			for (int y = margemY; y < alt - margemY; y += puloY)
				for (int x = margemX; x < larg - margemX; x += puloX)
				{
					int onde = eixoY ? (y + d) * larg + x : y * larg + x + d;
					soma += Math.Abs(antes[y * larg + x] - agora[onde]);
					n++;
				}
			if (n == 0) return null;
			soma = soma * 1000 / n;
			if (soma < melhor) { segundo = melhor; melhor = soma; qual = d; }
			else if (soma < segundo) segundo = soma;
		}
		// ============================ EMPATE NAO E MEDIDA ============================
		// O vencedor tem que ser ao menos 30% melhor que o vice. A margem era 15% e ISSO NAO BASTOU:
		// no zoom 4 cada texel do cenario ocupa 4 px de tela, o padrao fica quase periodico e a
		// correlacao elegia com confianca um deslocamento ERRADO -- a bancada reprovou uma rolagem
		// que os numeros mostravam perfeita (erro 1 px, pior passo 0,67). Com a margem larga ela
		// devolve "nao medi" em vez de medir errado, que e a unica das duas respostas que nao mente.
		// ==========================================================================================
		//
		// E O SINAL E `>=`, NAO `>`: com um recorte que casa PERFEITO -- o corpo cravado no meio da
		// tela, que e o caso que esta funcao mais precisa acertar -- o melhor e o segundo dao ZERO os
		// dois, e `0 > 0` e falso. A comparacao deixava passar o empate mais perfeito que existe e
		// devolvia o primeiro `d` varrido como se fosse uma medida.
		if (segundo == long.MaxValue || melhor * 100 >= segundo * 70) return null;
		return qual;
	}

	// ==================== O RELATORIO ====================

	private void Fechar()
	{
		_acabou = true;
		if (_correcoes > 0)
			_falhas.Add($"{_correcoes} correcao(oes) de servidor durante a caminhada -- "
						+ "as duas pontas discordaram do passo");

		// TUDO O QUE ESCREVE EM `_passos` RODA AQUI, ANTES DO RELATORIO. A conta dos corpos remotos
		// ficava depois do laco que imprime `_passos`, e a linha dela sumia do relatorio calada --
		// as falhas apareciam (elas sao impressas no fim), mas o numero que as explica, nao.
		OsDoisEixos();
		OsDoisEixosNoPixel();
		if (!Laboratorio) OsOutrosBonecos();
		GravarATira();

		Anotar("===== A ROLAGEM DO CENARIO =====");
		foreach (string l in _passos) Anotar(l);
		Anotar($"correcoes de servidor: {_correcoes}");
		if (Laboratorio)
			Anotar("corpos remotos: nao ha no laboratorio -- so o berco do MUNDO responde por eles");
		Anotar(_falhas.Count == 0 ? "===== TUDO VERDE =====" : $"===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) Anotar("   " + f);
		GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit();
	}

	/// <summary>
	/// O RELATORIO VAI PRO DISCO, e nao so pro console.
	///
	/// O console desta bancada e um cano: quem lanca o jogo e o `..._console.exe`, e matar o lancador
	/// nao mata o jogo -- o texto que o `GD.Print` ja tinha escrito morre no cano junto com ele. Um
	/// arquivo aberto e fechado a cada linha sobrevive a isso, e e o que deixa a medicao existir
	/// mesmo quando a rodada e interrompida no meio.
	/// </summary>
	private void Anotar(string linha)
	{
		GD.Print("[rolagem] " + linha);
		try { System.IO.File.AppendAllText(CaminhoDoRelatorio, linha + System.Environment.NewLine); }
		catch { /* sem disco a bancada continua no console */ }
	}

	/// <summary>
	/// A TIRA VAI PRO DISCO, e quando ha um "ANTES" ela sai LADO A LADO com ele.
	///
	/// ============================ POR QUE A COMPARACAO E FEITA AQUI DENTRO ============================
	/// O "antes" e o "depois" sao duas RODADAS do jogo -- nao ha como um processo fotografar os dois.
	/// Entao a segunda rodada carrega a imagem da primeira (`--rolagemtiraantes`) e monta o par. A
	/// alternativa era juntar as duas metades num programa de fora, e isso poria a prova visual em
	/// cima de uma ferramenta que nao e deste repositorio e que ninguem revisa.
	///
	/// A moldura entre as duas metades e VERMELHA do lado do antes e VERDE do lado do depois, e as
	/// faixas de cima de cada painel dizem o veredito daquele rumo. Uma imagem que precisa de legenda
	/// pra ser lida nao serve pra alguem conferir de relance.
	/// ==========================================================================================
	/// </summary>
	private void GravarATira()
	{
		if (CaminhoDaTira is not { } destino) return;
		if (_paineis.Count == 0)
		{
			_passos.Add("TIRA: nao houve painel pra gravar (rodou sem janela?)");
			return;
		}

		Image agora = LadoALado(_paineis, new Color(0.16f, 0.16f, 0.20f));

		// ============================ A DOBRA VEM ANTES DO PAR, E NAO DEPOIS ============================
		// A tira e feita de pixels de um por um e sai dobrada com vizinho mais proximo -- qualquer
		// interpolacao aqui borraria exatamente o que a imagem existe pra mostrar.
		//
		// Dobrar so no fim parecia igual e nao era: o "antes" que se carrega do disco JA esta dobrado
		// (ele foi salvo por esta mesma funcao), e comparar a altura dele com a do painel cru
		// reprovava o par por "rodadas diferentes" -- 452 px contra 226. Dobrando o de agora primeiro,
		// os dois lados chegam na mesma escala e a conferencia de altura volta a significar o que ela
		// diz: se as alturas ainda diferirem, as rodadas de fato foram diferentes.
		// ==========================================================================================
		agora.Resize(agora.GetWidth() * 2, agora.GetHeight() * 2, Image.Interpolation.Nearest);

		Image? antes = null;
		if (CaminhoDaTiraAntes is { } origem && System.IO.File.Exists(origem))
		{
			antes = Image.LoadFromFile(origem);
			if (antes != null && antes.GetHeight() != agora.GetHeight())
			{
				// ALTURAS DIFERENTES = RODADAS DIFERENTES. Emparelhar imagens de tamanhos distintos
				// esticaria uma delas, e uma tira esticada mente sobre a inclinacao das listras --
				// que e justamente o que se esta olhando. Melhor recusar o par.
				_passos.Add($"TIRA: o 'antes' tem {antes.GetHeight()} px de altura e o de agora "
							+ $"{agora.GetHeight()} -- rodadas diferentes, nao emparelhei");
				antes = null;
			}
		}

		// A MOLDURA DO PAR E NEUTRA, e isso e uma correcao: ela era vermelha (a cor do "antes") e
		// acabava emoldurando de vermelho a metade VERDE tambem. Quem carrega a cor do veredito e a
		// faixa de cada painel -- a moldura so separa as duas rodadas.
		Image saida = antes == null
			? agora
			: LadoALado([("antes", antes), ("depois", agora)], new Color(0.16f, 0.16f, 0.20f));

		Error e = saida.SavePng(destino);
		_passos.Add(e == Error.Ok
			? $"TIRA: {destino} ({saida.GetWidth()}x{saida.GetHeight()}, "
			  + $"{_paineis.Count} painel(eis){(antes == null ? "" : ", com o ANTES do lado")})"
			: $"TIRA: falhei ao gravar {destino} ({e})");
	}

	/// <summary>Junta paineis da mesma altura numa fila, com uma coluna de separacao entre eles.</summary>
	private static Image LadoALado(List<(string Nome, Image Painel)> partes, Color separador)
	{
		const int folga = 4;
		int alt = partes.Max(p => p.Painel.GetHeight());
		int larg = partes.Sum(p => p.Painel.GetWidth()) + folga * (partes.Count + 1);
		var saida = Image.CreateEmpty(larg, alt + folga * 2, false, Image.Format.Rgb8);
		saida.Fill(separador);
		int x = folga;
		foreach ((string _, Image p) in partes)
		{
			saida.BlitRect(p, new Rect2I(Vector2I.Zero, p.GetSize()), new Vector2I(x, folga));
			x += p.GetWidth() + folga;
		}
		return saida;
	}

	/// <summary>Onde a tira e gravada. Sem `--rolagemtira CAMINHO` ela nem e montada.</summary>
	private static string? CaminhoDaTira => Argumento("--rolagemtira");

	/// <summary>A tira de uma rodada anterior, pra sair ao lado desta. Ver <see cref="GravarATira"/>.</summary>
	private static string? CaminhoDaTiraAntes => Argumento("--rolagemtiraantes");

	private static string? Argumento(string chave)
	{
		string[] args = OS.GetCmdlineArgs();
		int i = Array.IndexOf(args, chave);
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}

	/// <summary>Onde o relatorio e escrito. `--rolagemsaida CAMINHO` troca.</summary>
	private static string CaminhoDoRelatorio
	{
		get
		{
			string[] args = OS.GetCmdlineArgs();
			int i = Array.IndexOf(args, "--rolagemsaida");
			return i >= 0 && i + 1 < args.Length ? args[i + 1] : "rolagem.txt";
		}
	}
}
