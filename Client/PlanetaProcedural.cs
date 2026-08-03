using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// UM PLANETA GERADO -- a terceira filha de <see cref="Planeta"/>, ao lado do
/// <see cref="PlanetaPreFeito"/>.
///
/// ============================ O QUE ELA FAZ, EM UMA FRASE ============================
/// Recebe a seed do servidor, pede o terreno ao Core, e PINTA o resultado em TileMapLayer --
/// sem baixar um unico byte de mapa.
/// =====================================================================================
///
/// POR QUE NAO HA .tscn: um pre-feito e um arquivo (o `.dmm` convertido); um gerado nao tem
/// arquivo nenhum. Toda a cena dele nasce aqui, em codigo, a partir de um numero -- e por isso o
/// carregador de zona INSTANCIA esta classe em vez de carregar um `PackedScene` (ver o gancho do
/// `World.CarregarZona` no relatorio).
///
/// A ORDEM IMPORTA, e ela e o motivo de o desenho e a geometria estarem separados aqui:
///   1. GERAR e sincrono e acontece dentro do `Entrar()`. A COLISAO nao pode esperar: no quadro
///      seguinte o corpo ja vai andar, e andar sem mapa e atravessar montanha e brigar com o
///      servidor (aquele "tremer na parede" que ja custou caro neste projeto);
///   2. PINTAR e fatiado ao longo de alguns quadros. Ver <see cref="OrcamentoDePinturaUs"/>.
///
/// CUSTO MEDIDO (Godot 4.7.1 headless, com o `tileset.tres` e o `tiles.json` de verdade -- a
/// bancada gera, monta as camadas e pinta exatamente como este arquivo):
///
///     lado      celulas    gerar    pintar (SetCell)   camadas
///     256x256    65.536    14 ms        16 ms           3 a 5
///     352x352   123.904   ~27 ms       ~31 ms           3 a 6
///     448x448   200.704    43 ms        49 ms           6
///
/// ou seja ~0,25 us por celula pintada, estavel em qualquer tamanho e em qualquer bioma. A bancada
/// tambem conferiu que NADA se perde no caminho: as celulas com tile batem exatamente com
/// chao + cobertura em todos os seis biomas (72.019 de 72.019 no Jardim, 206.338 de 206.338 no
/// Deserto de 448). Um `SetCell` que cai num tile inexistente nao desenha e nao reclama -- por isso
/// a contagem importa mais que a foto.
///
/// GERAR NAO DA PRA FATIAR (a colisao precisa existir inteira antes do primeiro passo), PINTAR DA.
/// E e por isso que o teto de tamanho foi escolhido pela geracao e nao pela pintura -- ver
/// `MundoProcedural.DaSeed`. Com o mundo maior de hoje (352), pousar custa ~27 ms de engasgo e mais
/// ~31 ms diluidos em ~7 quadros, em vez dos 58 ms de uma vez so.
/// </summary>
[GlobalClass]
public partial class PlanetaProcedural : Planeta
{
	/// <summary>
	/// O TileSet compartilhado, o MESMO das cenas pre-feitas (`MapConverter.EscreverTileSet`).
	/// Um planeta gerado nao tem paleta propria: ele desenha com os mesmos turfs do jogo.
	/// </summary>
	private const string CaminhoDoTileSet = "res://Assets/Maps/tileset.tres";

	/// <summary>
	/// QUANTO TEMPO A PINTURA PODE ROUBAR DE UM QUADRO, em microssegundos.
	///
	/// 5 ms num quadro de 16,6 ms (60 fps) deixa folga pro resto do jogo. Com ~0,25 us por celula
	/// isso da ~20 mil celulas por quadro: um mundo de 256 termina em 4 quadros (~70 ms) e o maior
	/// de hoje (352) em 7 (~120 ms). O jogador ve o chao "chegando", e nao a tela travando -- e um
	/// planeta que congela o jogo ao entrar e pior que um planeta feio.
	///
	/// O ORCAMENTO E MEDIDO, NAO ESTIMADO: o laco de pintura olha o relogio a cada linha e para.
	/// Um numero fixo de celulas por quadro seria um chute que envelhece mal (maquina mais lenta,
	/// bioma com mais cobertura, o dia em que o TileSet crescer).
	/// </summary>
	private const ulong OrcamentoDePinturaUs = 5000;

	/// <summary>
	/// Raio, em tiles, do pedaco pintado NA HORA em volta do ponto de pouso.
	///
	/// Existe porque o corpo aparece no primeiro quadro: sem isto, o jogador passa a fracao de
	/// segundo inicial em pe sobre o vazio (o `ColorRect` de reserva do World), que e exatamente o
	/// sintoma que o fatiamento deveria estar escondendo. 24 tiles cobrem a tela em zoom 2 e
	/// custam ~2.300 celulas: meio milissegundo.
	/// </summary>
	private const int RaioDaJanelaInicial = 24;

	/// <summary>O terreno pronto. Nulo antes do <see cref="Planeta.Entrar"/>.</summary>
	public TerrenoGerado? Terreno { get; private set; }

	/// <summary>
	/// A colisao -- a MESMA que o servidor gerou, a partir da mesma seed. E o que o
	/// <see cref="LocalPlayer"/> usa pra colidir e o que impede a briga cliente-servidor.
	/// </summary>
	public ZoneCollision? Colisao => Terreno?.Colisao;

	/// <summary>
	/// O QUE ESCONDE, que nao e o mesmo que o que BLOQUEIA -- a mesma separacao que o
	/// `MapConverter` faz entre o `.col` e o `.vis` dos planetas pre-feitos: la, cenario solto
	/// (arvore, pedra, poste) PARA o corpo mas nao corta a linha de visao. Aqui vale igual: so a
	/// montanha cega. Sem este segundo mapa, ou a arvore viraria muro visual (uma floresta deixaria
	/// o jogador cego) ou nada esconderia nada.
	/// </summary>
	public ZoneCollision? Sombra { get; private set; }

	/// <summary>A pintura terminou. So interessa a quem for medir ou testar.</summary>
	public bool Pintado { get; private set; }

	/// <summary>Um pincel pronto: em que camada, com que fonte do TileSet e que quadro do atlas.</summary>
	private struct Pincel
	{
		public TileMapLayer? Camada;
		public int Fonte;
		public Vector2I Quadro;
		public readonly bool Vale => Camada != null;
	}

	// indexados pelo valor de ClasseDeTerreno (0-4) e CoberturaDeTerreno (0-5)
	private readonly Pincel[] _chao = new Pincel[5];
	private readonly Pincel[] _cobertura = new Pincel[6];

	// as camadas ja criadas, por (cobertura?, tinta). Ver MontarCamadas pra o porque da chave.
	private readonly Dictionary<string, TileMapLayer> _camadas = [];

	private int _proximaLinha;
	private ulong _usDeGeracao, _usDePintura;

	public override void _Ready()
	{
		Procedural = true;   // e o que ele E; nao da pra desmarcar no inspetor e virar outra coisa
		base._Ready();
	}

	/// <summary>
	/// NASCER: pedir o chao ao Core e comecar a pintar.
	///
	/// Tudo que entra na geracao sai da SEED (ver <see cref="Jandirus.Core.World.MundoProcedural"/>) --
	/// nome, bioma, tamanho e gravidade. E o que garante que este planeta e IDENTICO ao que o
	/// servidor gerou: se um so desses numeros divergisse, o jogador veria montanha onde o servidor
	/// ve campo.
	/// </summary>
	protected override void Nascer()
	{
		if (NomeDoPlaneta.Length == 0) NomeDoPlaneta = Name;

		// RENASCER TEM QUE APAGAR O MUNDO ANTERIOR. Hoje o carregador de zona joga o node fora e
		// cria outro, entao isto nunca roda -- mas `Entrar()` e publico e aceita uma seed nova, e
		// sem esta limpeza o planeta novo herdaria as camadas do velho (com as celulas do velho
		// dentro delas, porque `_camadas` devolveria a camada ja existente pela mesma tinta).
		foreach (TileMapLayer velha in _camadas.Values) velha.QueueFree();
		_camadas.Clear();
		Array.Clear(_chao);
		Array.Clear(_cobertura);
		_usDePintura = 0;

		// ORDENAR POR Y a partir daqui. Sem isto as camadas de cobertura viram um bloco fechado e
		// o personagem passa SEMPRE na frente (ou sempre atras) das arvores -- o Godot so funde a
		// ordenacao quando ela e continua de pai pra filho, e o World ja ordena acima.
		YSortEnabled = true;

		// QUALIFICADO DE PROPOSITO: a ficha de um mundo gerado e regra de SERVIDOR (ele e quem
		// decide o que uma zona e), e o cliente aqui esta perguntando, nao decidindo. O caminho
		// Client -> Server ja existe no projeto (Boot.cs, PauseMenu.cs). O lugar definitivo dela e
		// o Core, em `PlanetaNoEspaco` -- ver o relatorio.
		Jandirus.Core.World.MundoProcedural ficha = Jandirus.Core.World.MundoProcedural.DaSeed(Seed, NomeDoPlaneta);
		Gravidade = ficha.Gravidade;
		Tipo = TipoDoBioma(ficha.Bioma);
		Largura = ficha.Lado;
		Altura = ficha.Lado;

		ulong t0 = Time.GetTicksUsec();
		Terreno = GeradorDeTerreno.Gerar(ficha.Parametros());
		_usDeGeracao = Time.GetTicksUsec() - t0;

		PontoDeChegada = new Vector2(Terreno.Spawn.X, Terreno.Spawn.Y);
		Sombra = MapaDoQueEsconde(Terreno);

		MontarCamadas(Terreno);

		// a vizinhanca do pouso PRIMEIRO, no mesmo quadro: o corpo ja vai estar la
		PintarBloco(Terreno.SpawnCelX - RaioDaJanelaInicial, Terreno.SpawnCelY - RaioDaJanelaInicial,
					Terreno.SpawnCelX + RaioDaJanelaInicial, Terreno.SpawnCelY + RaioDaJanelaInicial);

		_proximaLinha = 0;
		Pintado = false;
		SetProcess(true);

		// A ASSINATURA NO LOG existe pra uma coisa so: conferir contra a que o servidor imprimiu.
		// Dois numeros iguais provam que as duas pontas geraram o mesmo mundo; e o unico teste de
		// determinismo que da pra fazer com o jogo rodando.
		GD.Print($"[planeta] {NomeDoPlaneta}: {ficha.Descricao()} gerado em {_usDeGeracao / 1000.0:0.0} ms "
				 + $"| assinatura {Terreno.Assinatura():X16}"
				 + (Terreno.ClareiraEscavada ? " | CLAREIRA ESCAVADA" : ""));
	}

	/// <summary>
	/// A PINTURA, FATIADA. Pinta linhas inteiras ate estourar o orcamento do quadro.
	///
	/// LINHA E A UNIDADE certa porque o terreno esta em ordem de linha (`i = y * Largura + x`):
	/// varrer por coluna pularia a memoria de um lado inteiro a cada celula e jogaria fora o cache.
	/// Uma linha de 352 custa ~0,09 ms, entao o orcamento nunca estoura por muito.
	/// </summary>
	public override void _Process(double delta)
	{
		if (Pintado || Terreno == null) return;

		ulong inicio = Time.GetTicksUsec();
		while (_proximaLinha < Terreno.Altura)
		{
			PintarFatia(Terreno, _proximaLinha++, 0, Terreno.Largura - 1);
			if (Time.GetTicksUsec() - inicio >= OrcamentoDePinturaUs) break;
		}
		_usDePintura += Time.GetTicksUsec() - inicio;

		if (_proximaLinha < Terreno.Altura) return;

		Pintado = true;
		SetProcess(false);
		GD.Print($"[planeta] {NomeDoPlaneta} pintado: {Terreno.Largura}x{Terreno.Altura} "
				 + $"em {_usDePintura / 1000.0:0.0} ms de trabalho "
				 + $"({_usDePintura / (double)(Terreno.Largura * Terreno.Altura):0.00} us/celula)");
	}

	// =====================================================================
	// AS CAMADAS
	// =====================================================================
	/// <summary>
	/// Cria as camadas e resolve os pinceis -- UMA vez, no nascimento.
	///
	/// POR QUE MAIS DE DUAS CAMADAS. O pedido natural e "uma pro chao, uma pra cobertura", como o
	/// `MapConverter` faz. So que as paletas do Core trazem TINTA por classe de terreno (a agua do
	/// Deserto e turquesa, a colina e ocre, a montanha e areia queimada -- sao os `tint` do DM), e
	/// o TileMapLayer nao tem cor POR CELULA: `Modulate` e da camada inteira. Entao o agrupamento e
	/// por TINTA, nao por classe -- e a bancada mostrou que isso custa pouco: no pior bioma
	/// (Deserto) sao 5 tintas de chao, e a cobertura nunca tem nenhuma, ou seja 6 camadas no maximo
	/// e 2 no melhor (Jardim/Vulcanico tem 2 tintas de chao).
	///
	/// A alternativa seria criar tiles ALTERNATIVOS coloridos no TileSet em runtime
	/// (`TileSetAtlasSource.CreateAlternativeTile` + `TileData.Modulate`). Isso MUTA um recurso
	/// compartilhado com as cenas pre-feitas -- o preco de uma camada extra e muito menor que o de
	/// um tileset que muda de conteudo dependendo de onde o jogador esteve.
	/// </summary>
	private void MontarCamadas(TerrenoGerado t)
	{
		var conjunto = ResourceLoader.Exists(CaminhoDoTileSet)
			? ResourceLoader.Load<TileSet>(CaminhoDoTileSet)
			: null;

		if (conjunto == null)
		{
			// SEM TILESET NAO HA DESENHO, mas o planeta continua existindo: colisao, sombra e
			// ponto de chegada ja estao prontos. Andar num mundo invisivel e ruim; cair fora da
			// zona porque o cenario nao carregou e pior.
			GD.PushWarning($"[planeta] sem '{CaminhoDoTileSet}' -- rode o Tools/AssetPipeline. "
						   + "O chao gerado NAO vai aparecer.");
			return;
		}

		PaletaDeBioma p = t.Paleta;
		Armar(ref _chao[(int)ClasseDeTerreno.Agua], conjunto, p.Agua, cobre: false);
		Armar(ref _chao[(int)ClasseDeTerreno.Praia], conjunto, p.Praia, cobre: false);
		Armar(ref _chao[(int)ClasseDeTerreno.Planicie], conjunto, p.Planicie, cobre: false);
		Armar(ref _chao[(int)ClasseDeTerreno.Colina], conjunto, p.Colina, cobre: false);
		Armar(ref _chao[(int)ClasseDeTerreno.Montanha], conjunto, p.Montanha, cobre: false);

		Armar(ref _cobertura[(int)CoberturaDeTerreno.Arvore], conjunto, p.Arvore, cobre: true);
		Armar(ref _cobertura[(int)CoberturaDeTerreno.ArvoreAcento], conjunto, p.ArvoreAcento, cobre: true);
		Armar(ref _cobertura[(int)CoberturaDeTerreno.Planta], conjunto, p.Planta, cobre: true);
		Armar(ref _cobertura[(int)CoberturaDeTerreno.Minerio], conjunto, p.Minerio, cobre: true);
		Armar(ref _cobertura[(int)CoberturaDeTerreno.Gema], conjunto, p.Gema, cobre: true);
	}

	/// <summary>Resolve UM visual do Core em pincel, criando a camada da tinta dele se preciso.</summary>
	private void Armar(ref Pincel pincel, TileSet conjunto, TileVisual visual, bool cobre)
	{
		if (!Resolver(visual, out int fonte, out Vector2I quadro))
		{
			// TILE QUE NAO EXISTE NA PALETA. Nao e fatal: a celula fica vazia e o resto do planeta
			// desenha. Fatal seria o quadro 0 silencioso -- a armadilha que ja fez as casas dos
			// mapas pre-feitos aparecerem com parede de Namekuseijin (ver MapConverter).
			GD.PushWarning($"[planeta] o TileSet nao tem '{visual.Atlas}' estado '{visual.Estado}' "
						   + "-- estas celulas ficarao vazias");
			return;
		}

		pincel.Fonte = fonte;
		pincel.Quadro = quadro;
		pincel.Camada = Camada(conjunto, cobre, visual.Tinta);
	}

	/// <summary>A camada de uma (cobertura?, tinta). Cria na primeira vez que alguem pede.</summary>
	private TileMapLayer Camada(TileSet conjunto, bool cobre, string? tinta)
	{
		string chave = (cobre ? "cob:" : "chao:") + (tinta ?? "");
		if (_camadas.TryGetValue(chave, out TileMapLayer? achada)) return achada;

		var camada = new TileMapLayer
		{
			Name = cobre ? $"Cobertura{_camadas.Count}" : $"Chao{_camadas.Count}",
			TileSet = conjunto,
		};

		if (cobre)
		{
			// COBERTURA ORDENA POR Y: e o que poe o personagem ATRAS da arvore quando ele esta
			// acima dela e NA FRENTE quando esta abaixo. `z_index` fixo mataria isso -- dentro de
			// um mesmo z quem decide e o Y; entre z diferentes quem decide e sempre o z.
			camada.YSortEnabled = true;
		}
		else
		{
			// O CHAO NUNCA ORDENA e fica sempre embaixo: e chao. Ordenar 200 mil tiles de grama
			// contra os personagens seria caro e nao mudaria nada -- ninguem passa atras da grama.
			camada.ZIndex = -1;
		}

		// A TINTA E DO DM. `Modulate` multiplica a textura, que e exatamente o que o `color` de um
		// turf faz no BYOND -- a areia do Deserto e a MESMA arte do chao da Terra, pintada.
		if (tinta is { Length: > 0 }) camada.Modulate = new Color(tinta);

		AddChild(camada);
		_camadas[chave] = camada;
		return camada;
	}

	/// <summary>
	/// NOME DE ATLAS -> ID DE FONTE NO TILESET.
	///
	/// O Core devolve `("Grass", "Grass1")` de proposito e NAO um numero: o id da fonte e atribuido
	/// por ordem de descoberta na conversao dos mapas (`MapConverter.Garantir`, `proxId++`), entao
	/// ele muda quando alguem reconverte. Quem traduz e a cena, uma vez, no carregamento -- e a
	/// tabela que faz a traducao e o <see cref="CatalogoDeTiles"/>, publicado pelo mesmo passo do
	/// pipeline que escreve o `tileset.tres` (`Tools/AssetPipeline/TileIndex`).
	/// </summary>
	private static bool Resolver(TileVisual visual, out int fonte, out Vector2I quadro)
	{
		if (Catalogo?.Achar(visual) is { } achado)
		{
			fonte = achado.Fonte;
			quadro = new Vector2I(achado.X, achado.Y);
			return true;
		}

		fonte = -1;
		quadro = Vector2I.Zero;
		return false;
	}

	/// <summary>
	/// O indice de tiles, lido UMA vez pro processo inteiro.
	///
	/// Estatico porque ele nao depende do planeta: sao ~125 folhas e alguns milhares de estados,
	/// os mesmos pra qualquer mundo. Reler o json a cada pouso seria I/O e parse repetidos por
	/// nada -- e o cache de terreno do servidor existe pela mesma razao.
	///
	/// O Core nao le `res://` (ele nao conhece o Godot): quem abre o arquivo e o cliente, e o Core
	/// so recebe o texto. Arquivo ausente devolve catalogo VAZIO, nao explosao -- o planeta nasce
	/// sem desenho, com colisao e ponto de chegada intactos, e o aviso diz o que rodar.
	/// </summary>
	private static CatalogoDeTiles? Catalogo => _catalogo ??= LerCatalogo();

	private static CatalogoDeTiles? _catalogo;

	private const string CaminhoDoIndice = "res://Assets/Data/tiles.json";

	private static CatalogoDeTiles LerCatalogo()
	{
		if (!Godot.FileAccess.FileExists(CaminhoDoIndice))
		{
			GD.PushWarning($"[planeta] sem '{CaminhoDoIndice}' -- rode o Tools/AssetPipeline. "
						   + "Nenhum planeta gerado vai aparecer.");
			return CatalogoDeTiles.Parse("");
		}

		CatalogoDeTiles c = CatalogoDeTiles.Parse(Godot.FileAccess.GetFileAsString(CaminhoDoIndice));
		GD.Print($"[planeta] indice de tiles: {c.Total} atlas, {c.TotalDeEstados} estados");
		return c;
	}

	// =====================================================================
	// PINTAR
	// =====================================================================
	/// <summary>Um pedaco de UMA linha. E o unico lugar do arquivo que toca `SetCell`.</summary>
	private void PintarFatia(TerrenoGerado t, int y, int x0, int x1)
	{
		int inicio = y * t.Largura;
		for (int x = x0; x <= x1; x++)
		{
			int i = inicio + x;
			var celula = new Vector2I(x, y);

			ref Pincel piso = ref _chao[t.Chao[i]];
			if (piso.Vale) piso.Camada!.SetCell(celula, piso.Fonte, piso.Quadro);

			byte cob = t.Cobertura[i];
			if (cob == 0) continue;
			ref Pincel acima = ref _cobertura[cob];
			if (acima.Vale) acima.Camada!.SetCell(celula, acima.Fonte, acima.Quadro);
		}
	}

	/// <summary>
	/// Pinta um retangulo agora. Repintar depois nao custa nada: `SetCell` sobrescreve com o mesmo
	/// valor, entao a fatia que passar por aqui de novo so gasta o tempo dela.
	/// </summary>
	private void PintarBloco(int x0, int y0, int x1, int y1)
	{
		if (Terreno == null) return;
		x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
		x1 = Math.Min(Terreno.Largura - 1, x1); y1 = Math.Min(Terreno.Altura - 1, y1);

		for (int y = Math.Max(0, y0); y <= Math.Min(Terreno.Altura - 1, y1); y++)
			PintarFatia(Terreno, y, x0, x1);
	}

	// =====================================================================
	// O QUE ESCONDE
	// =====================================================================
	/// <summary>
	/// O mapa de VISAO: so montanha. Sai no formato "JCOL", o mesmo do `.vis` dos pre-feitos, pra
	/// entrar direto no <see cref="Visao"/> sem um segundo tipo de mapa no jogo.
	///
	/// Nao da pra reusar o `TerrenoGerado.Colisao`: la a arvore tambem e parede (e ela PARA o
	/// corpo, com razao), e uma floresta cegando o jogador inteiro seria outro jogo.
	/// </summary>
	private static ZoneCollision? MapaDoQueEsconde(TerrenoGerado t)
	{
		int n = t.Largura * t.Altura;
		var blob = new byte[8 + (n + 7) / 8];
		blob[0] = (byte)'J'; blob[1] = (byte)'C'; blob[2] = (byte)'O'; blob[3] = (byte)'L';
		blob[4] = (byte)(t.Largura & 0xFF); blob[5] = (byte)(t.Largura >> 8);
		blob[6] = (byte)(t.Altura & 0xFF); blob[7] = (byte)(t.Altura >> 8);

		for (int i = 0; i < n; i++)
			if (t.Chao[i] == (byte)ClasseDeTerreno.Montanha)
				blob[8 + (i >> 3)] |= (byte)(1 << (i & 7));

		return ZoneCollision.Load(blob);
	}

	/// <summary>
	/// Bioma do Core -> tipo da cena. Os seis nomes sao os MESMOS dos dois lados (foi de proposito:
	/// ver o comentario do `BiomaDeTerreno`), entao isto e so a ponte entre dois enums.
	/// </summary>
	private static TipoDePlaneta TipoDoBioma(BiomaDeTerreno b) => b switch
	{
		BiomaDeTerreno.Jardim => TipoDePlaneta.Jardim,
		BiomaDeTerreno.Deserto => TipoDePlaneta.Deserto,
		BiomaDeTerreno.Gelado => TipoDePlaneta.Gelado,
		BiomaDeTerreno.Vulcanico => TipoDePlaneta.Vulcanico,
		BiomaDeTerreno.Morto => TipoDePlaneta.Morto,
		_ => TipoDePlaneta.Rochoso,
	};
}
