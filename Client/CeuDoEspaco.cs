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

		var frames = ResourceLoader.Load<SpriteFrames>(Folha);
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

	private Label _rotulo = null!;
	private Sprite2D _icone = null!;

	public override void _Ready()
	{
		ZIndex = -60;   // atras dos corpos, na frente do ceu

		Texture2D? quadro = Quadro(EstadoDoIcone());

		_icone = new Sprite2D
		{
			Name = "Icone",
			Texture = quadro,
			// ESCALA PELO DIAMETRO. O raio vem do servidor em pixels de mundo e e ele que decide a
			// que distancia o pouso acontece; se o desenho nao casar com esse raio, o jogador
			// pousa "no vazio" ao lado de um planeta que parecia estar longe.
			Scale = Vector2.One * (Raio * 2f / LadoDoIcone),
			TextureFilter = TextureFilterEnum.Nearest,
		};
		AddChild(_icone);

		MontarAgonia(quadro);

		_rotulo = Tema.Legenda(Nome, Premade ? Tema.Destaque : Tema.TextoFraco, 13);
		_rotulo.Position = new Vector2(-90, -Raio - 34);
		_rotulo.Size = new Vector2(180, 20);
		_rotulo.HorizontalAlignment = HorizontalAlignment.Center;
		AddChild(_rotulo);
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
	/// </summary>
	private void MontarAgonia(Texture2D? quadro)
	{
		var mat = new ShaderMaterial { Shader = ShaderDaAgonia };
		(Vector2 min, Vector2 max) = BorraoDirecional.Caixa(quadro);
		mat.SetShaderParameter("quadro_min", min);
		mat.SetShaderParameter("quadro_max", max);

		// A SEMENTE E A DO PROPRIO PLANETA -- pre-feito tem seed derivada do nome (`Espaco.Fixo`),
		// gerado tem a dele. O `% 997` e o mesmo empacotamento da semente das feridas: um `ulong`
		// grande vira `float` com perda, e o que se quer aqui e so um numero pequeno e estavel.
		mat.SetShaderParameter("semente", (Seed % 997) * 0.37f);
		mat.SetShaderParameter("agonia", 0f);

		_icone.Material = mat;
		_agonia = mat;
	}

	private static Shader? _shAgonia;
	private static Shader ShaderDaAgonia =>
		_shAgonia ??= ResourceLoader.Load<Shader>("res://Assets/Shaders/PlanetaMorrendo.gdshader");

	private static Shader? _shEstouro;
	private static Shader ShaderDoEstouro =>
		_shEstouro ??= ResourceLoader.Load<Shader>("res://Assets/Shaders/EstouroDePlaneta.gdshader");

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

		var chave = ChaveDePlaneta.De(new PlanetaNoEspaco
		{
			Nome = Nome, Seed = Seed, Premade = Premade,
		});

		AplicarAgonia(cli.IntensidadeDaAgonia(chave), cli.SegundosAteOEstouro(chave));
	}

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
		float lado = Raio * 5.2f;

		var mat = new ShaderMaterial { Shader = ShaderDoEstouro };
		mat.SetShaderParameter("t", 0f);
		mat.SetShaderParameter("semente", (Seed % 997) * 0.37f);

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
		AddChild(quad);

		// O DISCO SOME POR BAIXO DO CLARAO: ele desaparece durante o estouro, e nao depois dele.
		// `Tween` no proprio node e nao lambda solta -- ele morre junto com o node (ver a nota das 19
		// assinaturas orfas em `Transformacao.TocarPedras`).
		Tween t = CreateTween();
		t.SetParallel();
		t.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("t", v)), 0f, 1f,
					  MortePlanetaria.SegundosDoEstouro);
		t.TweenProperty(_icone, "modulate:a", 0f, MortePlanetaria.SegundosDoEstouro * 0.55);
		t.TweenProperty(_rotulo, "modulate:a", 0f, MortePlanetaria.SegundosDoEstouro * 0.35);

		// O ALCANCE E DERIVADO DO PLANETA e nao o padrao de 480 px: um mundo estourando se ouve de
		// muito mais longe que um soco, e o raio dele e a unica medida de "muito mais longe" que o
		// espaco tem.
		AudioDirector.EfeitoNoLugar(this, Trilha.Explosao, 1f, Raio * 8f);
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
		var frames = ResourceLoader.Load<SpriteFrames>(Folha);
		if (frames == null) { GD.PushWarning("[planeta] sem Planets.tres"); return null; }
		if (frames.HasAnimation(estado) && frames.GetFrameCount(estado) > 0)
			return frames.GetFrameTexture(estado, 0);

		GD.PushWarning($"[planeta] a folha nao tem o estado '{estado}'");
		return frames.HasAnimation("jungle") && frames.GetFrameCount("jungle") > 0
			? frames.GetFrameTexture("jungle", 0) : null;
	}
}
