using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O FUNDO DO ESPACO -- e ele e o TILE do jogo antigo, nao um desenho meu.
///
/// ============================ POR QUE ISTO FOI REESCRITO ============================
/// A primeira versao inventava um campo de estrelas por codigo: pontos sorteados em tres camadas
/// de paralaxe. Ficava com cara de protetor de tela, e -- pior -- NAO ERA O JOGO. O BYOND ja tem
/// `spacebck.dmi`, com 26 variantes de 32x32 desenhadas a mao, e o espaco inteiro do original e
/// esse tile repetido. Inventar arte quando a arte existe troca uma coisa reconhecivel por uma
/// generica, e joga fora o trabalho de quem desenhou.
/// ===================================================================================
///
/// SO PINTA O QUE A CAMERA VE, que era o pedido explicito. O espaco nao tem fim: pintar "o mapa"
/// e impossivel por definicao. A cada quadro isto olha o retangulo visivel, converte em celulas e
/// pinta so essa faixa, apagando o que saiu. Num monitor comum sao ~1.400 celulas vivas de cada
/// vez, e o custo NAO CRESCE nunca -- da pra voar por milhoes de pixels e continuam 1.400.
///
/// QUAL VARIANTE CAI EM CADA CELULA E FUNCAO PURA de (seed do universo, x, y). Nao ha sorteio em
/// runtime e nao ha memoria: sair voando e voltar mostra o mesmo pedaco de ceu. Sorteado na hora
/// de pintar, o espaco se reembaralharia a cada volta de camera -- e a sensacao de LUGAR, que e o
/// unico ponto de referencia que existe no vazio, iria junto.
/// </summary>
public partial class CeuDoEspaco : Node2D
{
	/// <summary>A folha do original: 26 variantes numeradas de 32x32.</summary>
	private const string Folha = "res://Assets/Sprites/Turfs/spacebck.tres";

	private const int Lado = 32;

	/// <summary>
	/// Quantas variantes usar. A folha tem 26 numeradas mais `speedspace_*`, `damaged` e
	/// `bluespace` -- essas sao de CENARIO (corredor de nave, dano), nao de fundo, e entrariam
	/// como remendo no meio do ceu.
	/// </summary>
	private const int Variantes = 26;

	/// <summary>Folga em celulas: a borda chega pintada em vez de aparecer entrando na tela.</summary>
	private const int Folga = 2;

	public ulong Seed;

	private TileMapLayer _camada = null!;
	private int _colunas = 1;
	private Rect2I _pintado;
	private ulong _seedPintada;

	public override void _Ready()
	{
		// ATRAS DE TUDO: o ceu e fundo, e ate os planetas passam por cima dele.
		ZIndex = -100;
		_camada = new TileMapLayer { Name = "Fundo", TileSet = MontarTileSet(out _colunas) };
		AddChild(_camada);
	}

	/// <summary>
	/// Monta um TileSet de uma fonte so com as 26 variantes.
	///
	/// FEITO POR CODIGO e nao lido de um `.tres` de TileSet porque o `spacebck.tres` do projeto e
	/// um <see cref="SpriteFrames"/> -- o pipeline converte todo .dmi assim, pensando em animacao
	/// de personagem. O que interessa dele e a TEXTURA; a estrutura de tile e outra coisa, e sai
	/// daqui.
	/// </summary>
	private static TileSet MontarTileSet(out int colunas)
	{
		colunas = 1;
		var ts = new TileSet { TileSize = new Vector2I(Lado, Lado) };

		var frames = FolhasPresas.Carregar(Folha);
		Texture2D? tex = frames != null && frames.HasAnimation("0") && frames.GetFrameCount("0") > 0
						 && frames.GetFrameTexture("0", 0) is AtlasTexture at
			? at.Atlas
			: null;

		if (tex == null)
		{
			GD.PushWarning("[ceu] nao achei a textura do spacebck -- o espaco fica preto");
			return ts;
		}

		var fonte = new TileSetAtlasSource { Texture = tex, TextureRegionSize = new Vector2I(Lado, Lado) };
		colunas = Mathf.Max(1, tex.GetWidth() / Lado);
		for (int i = 0; i < Variantes; i++)
		{
			var c = new Vector2I(i % colunas, i / colunas);
			if (!fonte.HasTile(c)) fonte.CreateTile(c);
		}
		ts.AddSource(fonte, 0);
		return ts;
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;

		Rect2I quer = CelulasVisiveis();
		if (Seed != _seedPintada)
		{
			_camada.Clear();
			_pintado = new Rect2I();
			_seedPintada = Seed;
		}
		else if (quer == _pintado) return;

		// SO O QUE MUDOU. Repintar a tela inteira a cada passo seria refazer 1.400 celulas por
		// quadro pra trocar uma coluna -- e o custo apareceria exatamente quando o jogador esta
		// se movendo, que e quando ele menos pode engasgar.
		for (int y = _pintado.Position.Y; y < _pintado.End.Y; y++)
			for (int x = _pintado.Position.X; x < _pintado.End.X; x++)
				if (!Dentro(quer, x, y)) _camada.EraseCell(new Vector2I(x, y));

		for (int y = quer.Position.Y; y < quer.End.Y; y++)
			for (int x = quer.Position.X; x < quer.End.X; x++)
			{
				if (Dentro(_pintado, x, y)) continue;
				// CAST EXPLICITO e nao acidental: `x` e `y` sao coordenadas de celula e podem ser
				// NEGATIVAS (o espaco tem origem no meio). O cast de `int` negativo pra `ulong`
				// preserva os bits, que e o que o hash quer -- e o mesmo valor nas duas pontas.
				int v = (int)(Espaco.Misturar(Seed, (ulong)(long)x, (ulong)(long)y) % Variantes);
				_camada.SetCell(new Vector2I(x, y), 0, new Vector2I(v % _colunas, v / _colunas));
			}

		_pintado = quer;
	}

	private static bool Dentro(Rect2I r, int x, int y) =>
		x >= r.Position.X && x < r.End.X && y >= r.Position.Y && y < r.End.Y;

	/// <summary>O retangulo de celulas que a camera alcanca, com folga.</summary>
	private Rect2I CelulasVisiveis()
	{
		Camera2D? cam = GetViewport()?.GetCamera2D();
		Vector2 tam = GetViewportRect().Size;
		if (cam != null) tam /= cam.Zoom;
		Vector2 centro = cam?.GetScreenCenterPosition() ?? GlobalPosition;

		var canto = new Vector2I(
			Mathf.FloorToInt((centro.X - tam.X * 0.5f) / Lado) - Folga,
			Mathf.FloorToInt((centro.Y - tam.Y * 0.5f) / Lado) - Folga);
		var lados = new Vector2I(
			Mathf.CeilToInt(tam.X / Lado) + Folga * 2,
			Mathf.CeilToInt(tam.Y / Lado) + Folga * 2);
		return new Rect2I(canto, lados);
	}

	/// <summary>Quantas celulas estao pintadas -- pro diagnostico provar que nao cresce.</summary>
	public int CelulasVivas => Mathf.Max(0, _pintado.Size.X * _pintado.Size.Y);
}

/// <summary>
/// UM PLANETA VISTO DO ESPACO -- com o icone do jogo antigo.
///
/// ============================ POR QUE ISTO FOI REESCRITO ============================
/// A primeira versao desenhava um circulo colorido com halo e crescente de sombra, e o comentario
/// dela justificava a escolha por causa do raio variavel (110 a 200 px). O argumento era fraco:
/// um sprite escala. E `Misc/Planets.dmi` existe desde sempre, com DEZOITO planetas desenhados de
/// 128x128 -- `earth`, `namek`, `vegeta`, `icer_planet`, `arlia`, `hell`, `heaven`,
/// `big_gete_star` e outros. Escolher o icone pelo nome e o que faz a Terra parecer a Terra em
/// vez de um disco verde-agua indistinguivel de qualquer outro mundo verde.
/// ===================================================================================
///
/// PLANETA GERADO CAI NO ICONE DO BIOMA: `jungle`, `desert`, `icer_planet`. A folha nao tem um
/// icone por seed -- nem poderia -- entao o que da pra fazer e escolher o parente mais proximo
/// pelo tipo. Um Jardim sorteado se parece com selva porque ele E uma selva.
/// </summary>
public partial class PlanetaDesenhado : Node2D
{
	private const string Folha = "res://Assets/Sprites/Misc/Planets.tres";

	/// <summary>O lado do quadro na folha. Serve pra converter Raio (px) em escala.</summary>
	private const float LadoDoIcone = 128f;

	public string Nome = "";
	public float Raio = 120;
	public ulong Seed;
	public bool Premade;

	/// <summary>O tipo, quando gerado ("Jardim", "Deserto", "Gelado"...). Vazio nos pre-feitos.</summary>
	public string Tipo = "";

	private Sprite2D _icone = null!;

	public override void _Ready()
	{
		ZIndex = -60;   // atras dos corpos, na frente do ceu

		(_icone, _agonia) = NovoIcone(Quadro(EstadoDoIcone()), Raio, Seed);
		AddChild(_icone);

		// O NOME NAO E ESCRITO EM CIMA DO DISCO. Havia um rotulo aqui ("Earth", "Namek"...), e o dono
		// mandou tirar: *"n precisa ter 'earth', 'namek' em cima dos planetas, o jogador ja vai saber
		// qual e pelo nav system"*. Quem diz o nome e a carta estelar (`MapaEstelar`, `TelaDoSistema`);
		// o `Nome` continua neste node porque escolhe o icone e identifica o mundo pros destrocos.
	}

	// =====================================================================
	// A AGONIA -- a crosta de magma, as rachaduras e o estouro
	// =====================================================================
	/// <summary>
	/// ============================ O QUE O DONO PEDIU, E ONDE ELE MORA ============================
	/// *"quem ta vendo do espaco o planeta deveria ficar com uns efeitos... um efeito meio
	/// avermelhado a lembra magma, e rachaduras no planeta, q vai se intensificando durante esses 5
	/// minutos, ate acontecer uma mega explosao... e assim o planeta some"*.
	///
	/// O DESENHO NAO MUDOU: continua **um** `Sprite2D` com um quadro de `Planets.tres`. O que entrou
	/// foi um `ShaderMaterial` nele -- e era literalmente uma linha, porque este node nunca teve
	/// material nenhum. Ver `Assets/Shaders/PlanetaMorrendo.gdshader`, onde as cinco regras copiadas
	/// dos ferimentos procedurais estao escritas uma a uma.
	///
	/// ============================ O RECORTE DO QUADRO E OBRIGATORIO ============================
	/// `Planets.tres` e uma folha de 640x512 com 20 quadros de 128x128, e o `Sprite2D` recebe um
	/// `AtlasTexture`. Amostrar UV fora do retangulo devolve **o quadro vizinho**, nao transparente.
	/// A caixa sai de `BorraoDirecional.Caixa`, que ja existe e ja faz o recuo de meio texel -- o
	/// mesmo helper que o borrao de corrida e a miragem do Zanzoken usam depois de o projeto ter
	/// levado esse tombo duas vezes.
	/// ========================================================================================
	///
	/// O DISCO E O MATERIAL DELE SAEM JUNTOS DAQUI, e a receita e UMA pra duas casas: a do espaco (<see cref="_Ready"/>)
	/// e a do palco do ensaio (<see cref="Ensaiar"/>). E isso que faz a pipeline montada no ensaio ser a que o primeiro
	/// planeta acha pronta.
	/// </summary>
	private static (Sprite2D Icone, ShaderMaterial Agonia) NovoIcone(Texture2D? quadro, float raio, ulong seed)
	{
		var icone = new Sprite2D
		{
			Name = "Icone",
			Texture = quadro,
			// ESCALA PELO DIAMETRO. O raio vem do servidor em pixels de mundo e e ele que decide a
			// que distancia o pouso acontece; se o desenho nao casar com esse raio, o jogador
			// pousa "no vazio" ao lado de um planeta que parecia estar longe.
			Scale = Vector2.One * (raio * 2f / LadoDoIcone),
			TextureFilter = TextureFilterEnum.Nearest,
		};

		var mat = new ShaderMaterial { Shader = ShaderDaAgonia };
		(Vector2 min, Vector2 max) = BorraoDirecional.Caixa(quadro);
		mat.SetShaderParameter("quadro_min", min);
		mat.SetShaderParameter("quadro_max", max);

		// A SEMENTE E A DO PROPRIO PLANETA -- pre-feito tem seed derivada do nome (`Espaco.Fixo`),
		// gerado tem a dele. O `% 997` e o mesmo empacotamento da semente das feridas: um `ulong`
		// grande vira `float` com perda, e o que se quer aqui e so um numero pequeno e estavel.
		mat.SetShaderParameter("semente", (seed % 997) * 0.37f);
		mat.SetShaderParameter("agonia", 0f);

		icone.Material = mat;
		return (icone, mat);
	}

	/// <summary>Os arquivos dos dois shaders do planeta. Publicos pra o `Aquecimento` po-los na fila de carga do lobby -- ver <see cref="Ensaiar"/>.</summary>
	public const string CaminhoDaAgonia = "res://Assets/Shaders/PlanetaMorrendo.gdshader",
						CaminhoDoEstouro = "res://Assets/Shaders/EstouroDePlaneta.gdshader";

	private static Shader? _shAgonia;
	private static Shader ShaderDaAgonia => _shAgonia ??= ResourceLoader.Load<Shader>(CaminhoDaAgonia);

	private static Shader? _shEstouro;
	private static Shader ShaderDoEstouro => _shEstouro ??= ResourceLoader.Load<Shader>(CaminhoDoEstouro);

	/// <summary>O raio do planeta do ensaio e o lado do estouro dele, em pixels do palco: a pipeline e do shader e do jeito de desenhar, e nao do tamanho.</summary>
	private const float RaioDoEnsaio = 16f, EstouroDoEnsaio = 48f;

	/// <summary>
	/// O ENSAIO DO LOBBY (ver `Aquecimento.AtosDoPlaneta`): o disco de um planeta, pela receita de producao
	/// (<see cref="NovoIcone"/>), num palco fora da tela -- pra o `PlanetaMorrendo.gdshader` ser compilado e a pipeline
	/// dele montada ALI, e nao no quadro em que o primeiro planeta do processo e desenhado.
	///
	/// TODO DISCO VISTO DO ESPACO CARREGA ESTE MATERIAL, vivo ou morrendo: o primeiro uso e a primeira chegada ao espaco,
	/// e nao a primeira agonia. O shader nem na fila de carga do aquecimento estava: era lido do disco, compilado e
	/// desenhado quando o primeiro disco nascia.
	///
	/// MEDIDO em 2026-10-09 pela `--diagestouro --avulsos`, que leva o corpo pro espaco em cima da Terra. O quadro em que o
	/// primeiro disco aparece e o da chegada na zona, e custa 54 a 59 ms com tudo quente (a zona montando, e 15 a 20
	/// esperando o shader); com o driver de video FRIO, 76 a 82 -- a pipeline, 30 a 32 ms de tela. Em onze corridas de
	/// dezoito a cobertura de carregamento ainda estava no ar nesse quadro; em sete, nao. E A GEMEA ILUMINADA, quando uma
	/// luz alcanca o disco, 73 a 79 ms de tela com o driver frio, no meio do voo: no espaco ha luz de efeito (a zona fica
	/// no crepusculo parado: forca da noite 0,66), e a aura de uma forma ou um tiro de ki perto de um planeta bastam.
	/// DEPOIS: nenhuma pipeline nasce em nenhum dos dois quadros; o da chegada custa 12 a 30 ms (a zona), e o da luz 8.
	///
	/// UM QUADRO RECORTADO DE UMA TEXTURA GERADA (a radial das luzes, que o palco do ensaio ja usa), e nao a folha dos
	/// planetas: a pipeline e do shader e do jeito de desenhar, e nao da textura -- e a folha so interessa a quem vai ao
	/// espaco. A MEIA AGONIA, e sem relogio: quem anda a agonia e o <see cref="_Process"/> de um planeta vivo, e aqui nao
	/// ha um. Morre com o palco do aquecimento.
	/// </summary>
	public static void Ensaiar(Node2D pai, Vector2 onde)
	{
		var quadro = new AtlasTexture { Atlas = Fogo.Radial(LuzDeKi.RaioDaTextura), Region = new Rect2(64, 64, 64, 64) };
		(Sprite2D icone, ShaderMaterial agonia) = NovoIcone(quadro, RaioDoEnsaio, 0);
		icone.Name = "PlanetaDoEnsaio";
		icone.Position = onde;
		agonia.SetShaderParameter("agonia", 0.5f);
		pai.AddChild(icone);
	}

	/// <summary>
	/// O ENSAIO DO LOBBY (ver `Aquecimento.AtosDoPlaneta`): o quad do estouro, pela receita de producao
	/// (<see cref="NovoEstouro"/>), num palco fora da tela -- pra o `EstouroDePlaneta.gdshader` ser compilado e a pipeline
	/// dele montada ALI, e nao no quadro do primeiro estouro do processo.
	///
	/// O PRIMEIRO ESTOURO QUASE NUNCA E O DE UM PLANETA: o `World.EstouroNoMundo` desenha o mesmo shader, num quad do
	/// mesmo feitio, pra Final Explosion, pra Aura da Destruicao e pro tremor de um mundo que morre -- no meio de uma
	/// luta. (Que as duas casas usam as mesmas pipelines e conferido pela rodada dos avulsos da `--diagestouro`: com o
	/// estouro no mundo ja desenhado, nenhuma nasce no do espaco.) E o shader nem na fila de carga do aquecimento estava:
	/// era lido do disco, compilado e desenhado no quadro do estouro.
	///
	/// MEDIDO em 2026-10-09 pela `--diagestouro --avulsos`, o quadro do primeiro estouro no mundo, em relogio:
	///
	///     com o driver de video FRIO .......... 54 a 57 ms   script 7 a 8, a espera pelo shader 24 a 26, a pipeline 21 a 23
	///     com o driver quente ................. 35 a 39 ms   so a espera: e o cache de shader do Godot vazio, o de quem abre
	///                                                        o jogo pela primeira vez
	///     a gemea ILUMINADA, driver frio ...... 68 a 75 ms   63 a 67 deles a pipeline do item COM luz, montada depois da sem luz
	///
	/// (A gemea e a do estouro de noite, com uma aura, um tiro de ki ou uma fogueira ao alcance do quad -- e ele tem 660 px
	/// de lado na Final Explosion.) DEPOIS: nenhuma pipeline nasce no quadro do estouro, com luz ou sem ela, e ele custa
	/// 8 a 9 ms -- uma volta do monitor --, com o driver quente ou frio.
	///
	/// NO COMECO DA ONDA (`t` 0,3) e sem relogio: quem anda o `t` e o tween de um estouro de verdade. Morre com o palco.
	/// </summary>
	public static void EnsaiarOEstouro(Node2D pai, Vector2 onde)
	{
		(ColorRect quad, ShaderMaterial tinta) = NovoEstouro(EstouroDoEnsaio, 0);
		quad.Name = "EstouroDoEnsaio";
		quad.Position += onde;   // (a receita o centra na origem do pai: no espaco, o planeta)
		tinta.SetShaderParameter("t", 0.3f);
		pai.AddChild(quad);
	}

	/// <summary>
	/// O QUAD DO ESTOURO e o material dele, centrado na origem de quem o recebe. UMA receita pra duas casas, a do planeta
	/// que estoura (<see cref="Estourar"/>) e a do palco do ensaio (<see cref="EnsaiarOEstouro"/>).
	/// </summary>
	private static (ColorRect Quad, ShaderMaterial Tinta) NovoEstouro(float lado, ulong seed)
	{
		var mat = new ShaderMaterial { Shader = ShaderDoEstouro };
		mat.SetShaderParameter("t", 0f);
		mat.SetShaderParameter("semente", (seed % 997) * 0.37f);

		var quad = new ColorRect
		{
			Name = "Estouro",
			Size = new Vector2(lado, lado),
			Position = new Vector2(-lado / 2, -lado / 2),
			Color = Colors.White,
			Material = mat,
			// ============================ O `ZIndex` AQUI E **RELATIVO**, E ISSO CUSTOU UMA FOTO ============================
			// `ZAsRelative` nasce VERDADEIRO no Godot, entao este numero soma ao do pai -- e o pai
			// (`PlanetaDesenhado`) e `ZIndex = -60`. A primeira versao escreveu -55 aqui querendo dizer
			// "logo acima do disco", e o que saiu foi **-115**: abaixo do proprio planeta e abaixo do
			// fundo. A bancada ficou verde nas tres checagens de codigo (o node existe, o material
			// existe, o `t` do tween anda) e a FOTO mostrou um planeta apagando sozinho, sem explosao
			// nenhuma. E o cego que este projeto chama de "uniform escrito nao e pixel desenhado".
			//
			// +5 RELATIVO diz o que se quis dizer: cinco degraus acima do disco, e o conjunto inteiro
			// continua atras dos corpos e na frente do ceu de estrelas.
			// ==========================================================================================================
			ZIndex = 5,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		return (quad, mat);
	}

	private ShaderMaterial? _agonia;

	/// <summary>O ultimo valor ESCRITO no uniform. Ver a guarda no <see cref="_Process"/>.</summary>
	private float _agoniaEscrita = -1f;

	private bool _estourou;

	/// <summary>
	/// ============================ A RAMPA E LIDA POR QUADRO, E ESSA E A EXCECAO ============================
	/// A disciplina do projeto (e a dos ferimentos, que este efeito copia) e traduzir estado em
	/// uniform **uma vez por mudanca**, nunca por quadro. Aqui a "mudanca" e continua: a agonia e uma
	/// funcao do relogio que anda sozinho, e nao ha evento nenhum pra assinar -- o `S2C.Mortos` so
	/// chega quando algo muda de FASE, e os cinco minutos sao uma fase so.
	///
	/// O que sobrou da disciplina e a GUARDA DE IDEMPOTENCIA: o uniform so e escrito quando o valor
	/// se move mais de 0,002 (uns 150 degraus na agonia inteira). Num quadro em que nada muda -- que e
	/// a esmagadora maioria, inclusive todos os quadros de todo planeta VIVO -- este metodo e uma
	/// comparacao de `float` e nada mais.
	/// ====================================================================================================
	/// </summary>
	public override void _Process(double delta)
	{
		if (GameClient.Instance is not { } cli) return;

		ChaveDePlaneta chave = Chave;
		AplicarAgonia(cli.IntensidadeDaAgonia(chave), cli.SegundosAteOEstouro(chave));
	}

	/// <summary>
	/// A CHAVE DESTE PLANETA -- a identidade honesta, e nao o nome (ver <see cref="ChaveDePlaneta"/>).
	///
	/// Virou propriedade quando o <see cref="Estourar"/> passou a precisar dela pra entregar a chave ao
	/// campo de destrocos: duas montagens da mesma chave em dois metodos do mesmo arquivo e o comeco
	/// classico de duas nocoes de identidade discordando.
	/// </summary>
	private ChaveDePlaneta Chave => ChaveDePlaneta.De(new PlanetaNoEspaco
	{
		Nome = Nome, Seed = Seed, Premade = Premade,
	});

	/// <summary>
	/// ESTADO -> UNIFORM -> PIXEL, num metodo so.
	///
	/// Separado do <see cref="_Process"/> pelo mesmo motivo do `GameClient.AplicarMortos`: e o unico
	/// jeito de uma bancada exercitar a traducao de verdade -- o material, o shader e o quadro
	/// desenhado -- sem precisar de servidor, de rede e de cinco minutos de relogio. O `_Process`
	/// pergunta ao cliente e chama isto; a bancada roteiriza a rampa e chama isto. **A escrita do
	/// uniform e o disparo do estouro acontecem aqui, e so aqui.**
	/// </summary>
	/// <param name="faltaParaOEstouro">
	/// Segundos ate o planeta estourar, ou nulo quando ele nao esta em contagem. **NEGATIVO e a
	/// janela do efeito**, e nao um erro: conta o tempo DESDE o estouro.
	/// </param>
	internal void AplicarAgonia(double agonia, double? faltaParaOEstouro)
	{
		var a = (float)agonia;
		if (_agonia != null && Mathf.Abs(a - _agoniaEscrita) > 0.002f)
		{
			_agonia.SetShaderParameter("agonia", a);
			_agoniaEscrita = a;
		}

		// ============================ O ESTOURO, E POR QUE ELE NAO VEM POR PACOTE ============================
		// O cliente ja tem o prazo (`S2C.Mortos` carrega o `faltam`), entao "quando o planeta estoura"
		// e **funcao pura do relogio** -- a mesma disciplina do ceu, da lua, do terreno e das estrelas.
		// Um pacote de "estourou agora" seria uma segunda fonte pra um instante que as duas pontas ja
		// derivam, e a primeira a divergir seria a de la, calada.
		// ==================================================================================================
		if (faltaParaOEstouro is not { } falta) return;

		if (!_estourou && falta <= 0)
		{
			_estourou = true;
			Estourar();
		}

		// O MUNDO SOME. Nao ha `QueueFree` do disco antes disto: quem fizesse o planeta sumir no
		// instante do prazo apagaria justamente o quadro em que a explosao comeca. O
		// `DesenharPlanetas` usa o MESMO prazo pra nao redesenhar o cadaver -- as duas metades.
		if (falta < -MortePlanetaria.SegundosDoEstouro) QueueFree();
	}

	/// <summary>
	/// A MEGA EXPLOSAO, ancorada NO LUGAR e nao na tela.
	///
	/// Um `Clarao` de tela cheia (o da cinematica de transformacao) seria errado aqui pelo motivo que
	/// o proprio `Transformacao.Clarao` documenta: *"o modo de falha seria a tela de alguem no espaco
	/// ficando branca por causa de um SSJ3 num planeta qualquer"*. Quem esta em orbita vendo um mundo
	/// morrer a 400 px de distancia precisa do efeito NO PONTO -- assim ele fica pequeno se voce
	/// estiver longe e toma a tela se voce estiver em cima, sem nenhuma regra de distancia escrita.
	///
	/// O QUAD E 2,6x O DIAMETRO porque a frente da onda sai do raio do planeta (`alcance = 1.25` no
	/// shader, e ela ainda precisa de folga pra o anel afinar em vez de ser cortado na quina).
	/// </summary>
	private void Estourar()
	{
		(ColorRect quad, ShaderMaterial mat) = NovoEstouro(Raio * 5.2f, Seed);
		AddChild(quad);

		// O DISCO SOME POR BAIXO DO CLARAO: ele desaparece durante o estouro, e nao depois dele.
		// `Tween` no proprio node e nao lambda solta -- ele morre junto com o node (ver a nota das 19
		// assinaturas orfas em `Transformacao.TocarPedras`).
		Tween t = CreateTween();
		t.SetParallel();
		t.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("t", v)), 0f, 1f,
					  MortePlanetaria.SegundosDoEstouro);
		t.TweenProperty(_icone, "modulate:a", 0f, MortePlanetaria.SegundosDoEstouro * 0.55);

		// O ALCANCE E DERIVADO DO PLANETA e nao o padrao de 480 px: um mundo estourando se ouve de
		// muito mais longe que um soco, e o raio dele e a unica medida de "muito mais longe" que o
		// espaco tem.
		AudioDirector.EfeitoNoLugar(this, Trilha.Explosao, 1f, Raio * 8f);

		// ============================ E O QUE SOBRA DO MUNDO NASCE AQUI -- IRMAO, E NAO FILHO ============================
		// *"onde ficava o planeta vao ter uns asteroides/rochas"*, pedido do dono. O campo vai pro PAI
		// e nao pra este node porque este node **se mata** 2,2 s depois (o `QueueFree` do
		// `AplicarAgonia`, que e o *"e assim o planeta some"*) -- um campo pendurado nele morreria
		// junto, e o rescaldo dura 60 s.
		//
		// **O CLARAO QUE O DONO PEDIU JA E O `nucleo` DO SHADER ACIMA** (`EstouroDePlaneta.gdshader:83`),
		// que acende exatamente onde o planeta estava e cede em `(1-t)^3`. Nao ha efeito novo aqui: o
		// que se acrescenta e so o que sobra depois que ele cede.
		//
		// A montagem passa pelo funil unico (`Garantir`) porque o `World.DesenharPlanetas` monta pela
		// outra porta -- ver o cabecalho de la.
		if (GetParent() is { } pai)
			DestrocosNoEspaco.Garantir(pai, Nome, Position, Raio, Seed, Chave);
	}

	/// <summary>A agonia que o material do planeta esta desenhando AGORA. Pra bancada -- ver o robo.</summary>
	public float AgoniaNoMaterialDeTeste =>
		_icone?.Material is ShaderMaterial m && m.GetShaderParameter("agonia").VariantType != Variant.Type.Nil
			? (float)m.GetShaderParameter("agonia")
			: -1f;

	/// <summary>Ja estourou? Pra bancada.</summary>
	public bool EstourouDeTeste => _estourou;

	/// <summary>
	/// EM QUE PONTO DA EXPLOSAO O MATERIAL ESTA (0 a 1), lido do proprio `ShaderMaterial`.
	///
	/// Existe pra a bancada fotografar o AUGE do efeito em vez de contar quadros: "dois quadros" nao
	/// quer dizer a mesma coisa a 60 e a 144 Hz, e a primeira foto de estouro saiu em `t = 0,03` --
	/// o instante em que ainda nao ha o que ver. Devolve -1 quando nao ha estouro em curso.
	/// </summary>
	public float TDoEstouroDeTeste =>
		GetNodeOrNull<ColorRect>("Estouro")?.Material is ShaderMaterial m
		&& m.GetShaderParameter("t").VariantType != Variant.Type.Nil
			? (float)m.GetShaderParameter("t")
			: -1f;

	/// <summary>
	/// O estado da folha pra este planeta.
	///
	/// PRE-FEITO CASA PELO NOME (a Terra usa `earth`); GERADO cai no icone do BIOMA. Sem
	/// correspondencia sobra um mundo generico -- nunca um quadrado vazio.
	/// </summary>
	private string EstadoDoIcone()
	{
		if (Premade)
			return Nome.Trim().ToLowerInvariant().Replace(' ', '_') switch
			{
				"earth" => "earth",
				"namek" => "namek",
				"vegeta" => "vegeta",
				"icer_planet" or "icer" => "icer_planet",
				"arlia" => "arlia",
				"arconia" or "acronia" => "arconia",
				"hell" => "hell",
				"heaven" => "heaven",
				"big_gete_star" => "big_gete_star",
				"vampa" => "vampa",
				_ => "jungle",
			};

		return Tipo.Trim().ToLowerInvariant() switch
		{
			"jardim" => "jungle",
			"deserto" => "desert",
			"gelado" => "icer_planet",
			"vulcanico" => "hell",
			"morto" => "big_gete_star",
			_ => "vegeta",   // Rochoso: o mundo de pedra do original
		};
	}

	private static Texture2D? Quadro(string estado)
	{
		var frames = FolhasPresas.Carregar(Folha);
		if (frames == null) { GD.PushWarning("[planeta] sem Planets.tres"); return null; }
		if (frames.HasAnimation(estado) && frames.GetFrameCount(estado) > 0)
			return frames.GetFrameTexture(estado, 0);

		GD.PushWarning($"[planeta] a folha nao tem o estado '{estado}'");
		return frames.HasAnimation("jungle") && frames.GetFrameCount("jungle") > 0
			? frames.GetFrameTexture("jungle", 0) : null;
	}
}
