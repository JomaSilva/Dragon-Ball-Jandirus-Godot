using Godot;

namespace Jandirus.Client;

/// <summary>
/// MOTION BLUR DIRECIONAL, em shader -- e com o cuidado que faltou nas duas vezes anteriores.
///
/// ============================ O RECORTE DO QUADRO NAO E OPCIONAL ============================
/// Todo sprite deste jogo e um <see cref="AtlasTexture"/>: um retangulo dentro de uma folha grande
/// com dezenas de outras poses. Amostrar UV fora desse retangulo NAO devolve transparente -- devolve
/// o QUADRO VIZINHO da folha.
///
/// Isso ja mordeu duas vezes aqui:
///   * no borrao de corrida, onde eu tinha o recorte e o removi junto com o efeito;
///   * na ondulacao da miragem do Zanzoken, onde eu simplesmente esqueci -- e o dono viu o
///     resultado exato que a falta do recorte produz: "da uma leve bugadinha quando to virado pra
///     direita e pra baixo, pra esquerda e pra cima funciona perfeitamente". Qual pose vaza depende
///     de QUEM esta ao lado na folha, e isso muda com a direcao.
///
/// Entao o recorte mora aqui, num lugar so, junto do calculo de onde o quadro comeca e acaba.
/// ==========================================================================================
///
/// POR QUE ISTO NAO ESTROBOSCOPA COMO A PRIMEIRA VERSAO. Aquela pintava o borrao no CORPO VIVO, cujo
/// quadro de animacao troca 2,2x mais rapido correndo -- a cada troca, todo o conteudo amostrado
/// mudava de uma vez. Aqui o borrao vai nas COPIAS CONGELADAS do rastro: cada uma guarda o quadro
/// que tinha quando nasceu e nunca mais muda. O borrao suaviza a copia; o rastro da o comprimento.
/// </summary>
public static class BorraoDirecional
{
	/// <summary>
	/// O CODIGO DESTE EFEITO mora num `.gdshader` de verdade -- ver o comentario de
	/// <see cref="CharacterVisual"/>: efeito procedural nao se acerta lendo codigo, se acerta
	/// arrastando o valor e OLHANDO, e pra isso ele precisa abrir no editor do Godot.
	///
	/// PUBLICO porque quem o traz pra memoria e o `Aquecimento`, no lobby, numa thread de carga -- e porque a bancada
	/// pergunta por ele ao cache do Godot. Ver <see cref="Ensaiar"/>.
	/// </summary>
	public const string CaminhoDoShader = "res://Assets/Shaders/Borrao.gdshader";

	private static Shader? _sh;

	/// <summary>
	/// PREGUICOSO, E QUASE NUNCA O PRIMEIRO A PEDIR: o arquivo ja veio do lobby, e este `Load` so o acha no cache. Quem
	/// ainda le o disco aqui e o processo que nao aquece (`--semaquecimento`).
	/// </summary>
	public static Shader Sh => _sh ??= ResourceLoader.Load<Shader>(CaminhoDoShader);

	/// <summary>
	/// A caixa do quadro deste sprite dentro da folha, em UV.
	///
	/// MEIO TEXEL PRA DENTRO em cada lado: em UV a borda exata cai EM CIMA da fronteira, e a
	/// interpolacao da textura ja pesca metade do pixel do quadro vizinho. Meio texel de folga e o
	/// que separa "recortado" de "quase recortado".
	/// </summary>
	public static (Vector2 Min, Vector2 Max) Caixa(Texture2D? tex)
	{
		if (tex is not AtlasTexture at || at.Atlas == null) return (Vector2.Zero, Vector2.One);

		var folha = new Vector2(Mathf.Max(at.Atlas.GetWidth(), 1), Mathf.Max(at.Atlas.GetHeight(), 1));
		Rect2 r = at.Region;
		Vector2 meio = new Vector2(0.5f, 0.5f) / folha;
		return (r.Position / folha + meio, r.End / folha - meio);
	}

	/// <summary>
	/// Poe o borrao num sprite ja pronto, com o recorte do quadro dele.
	///
	/// REUSA o material que ja estiver no sprite quando ele for deste mesmo shader. Nao e
	/// microtuning: este metodo e chamado uma vez por CAMADA por FOTO do rastro de corrida, umas
	/// 120 vezes por segundo por corpo correndo, e cada `new ShaderMaterial` e um recurso de
	/// verdade com o custo de alocacao e de coleta que isso tem.
	/// </summary>
	/// <param name="tinta">
	/// A COR DA CAMADA que esta copia retrata -- os dois uniforms de tinta do `Personagem.gdshader`
	/// (<paramref name="tinta"/> e <paramref name="tintaModo"/>) e o modulate dela (<paramref name="brilho"/>).
	/// O material de borrao ENTRA NO LUGAR do que pinta o corpo, entao quem nao passa a cor adiante desenha
	/// a folha crua: o rastro na "cor base" do item 6 do playtest de 2026-10-10. Os padroes sao a arte crua,
	/// que e o que um sprite sem corpo por tras (o do ensaio do lobby) tem mesmo.
	/// </param>
	public static void Aplicar(Sprite2D s, Vector2 rumo, float forca,
							   Vector3 tinta = default, int tintaModo = 0, Vector3? brilho = null)
	{
		ShaderMaterial m = s.Material is ShaderMaterial velho && velho.Shader == Sh
			? velho
			: new ShaderMaterial { Shader = Sh };
		(Vector2 min, Vector2 max) = Caixa(s.Texture);
		m.SetShaderParameter("rumo", rumo);
		m.SetShaderParameter("forca", forca);
		m.SetShaderParameter("quadro_min", min);
		m.SetShaderParameter("quadro_max", max);
		// SO QUANDO HA COR PRA LEVAR: a camada sem tinta nenhuma (a maioria) fica com os valores de fabrica do
		// shader, que sao a arte crua -- tres chamadas a menos por camada por foto, e sao ~120 fotos de camada
		// por segundo por corpo correndo.
		bool comCor = !SemACorDoCorpoDeTeste && (tinta != Vector3.Zero || tintaModo != 0 || brilho is { } br && br != Vector3.One);
		if (comCor)
		{
			m.SetShaderParameter(NomeDaTinta, tinta);
			m.SetShaderParameter(NomeDoModo, tintaModo);
			m.SetShaderParameter(NomeDoBrilho, brilho ?? Vector3.One);
		}
		s.Material = m;
	}

	// GUARDADOS: a `string` entregue ao `SetShaderParameter` fabrica um `StringName` por chamada, lixo com
	// finalizador -- e este caminho roda por camada por foto (ver o compasso do `CharacterVisual`).
	private static readonly StringName NomeDaTinta = "tinta", NomeDoModo = "tinta_modo", NomeDoBrilho = "brilho";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagcorrastro`): a copia do rastro volta a sair com a folha crua -- o estado
	/// de antes de 2026-10-10, em que o borrao de um Majin de outra cor era o rosa do arquivo. Lido pela propria
	/// <see cref="Aplicar"/>.
	/// </summary>
	public static bool SemACorDoCorpoDeTeste;

	/// <summary>
	/// O ENSAIO DO LOBBY (ver `Aquecimento.AtosDoBorrao`): um sprite parado no palco, borrado pela MESMA chamada que o
	/// rastro faz em cada camada de cada foto (<see cref="Aplicar"/>) -- pra o shader ser compilado e as pipelines dele
	/// montadas ALI, e nao no quadro da primeira investida do processo.
	///
	/// ============================ O QUE ISTO TIRA DO MEIO DA LUTA ============================
	/// O shader deste efeito era carregado na hora do primeiro uso (o <see cref="Sh"/> e preguicoso, e ninguem o trazia
	/// antes), e o primeiro uso e o pior instante que ha: a primeira investida de uma luta, ou o primeiro passo de uma
	/// corrida. MEDIDO em 2026-10-09 pela `--diagestouro --pecas`, com o cache de shader do Godot vazio (o de quem abre o
	/// jogo pela primeira vez, e o de toda bancada) e o do driver de video quente:
	///
	///     o quadro do primeiro borrao do processo ........ 31 a 40 ms de trabalho   (o de um borrao qualquer: 3 a 5)
	///       -- esperando o shader compilar ............... 22 a 32 ms   a thread principal parada no PREPARO do desenho
	///       -- o primeiro material ....................... 1,7 ms
	///       -- o arquivo, lido do disco .................. 0,7 ms       (3,9 com `--verbose`: o motor escreve no log)
	///       -- a pipeline, no primeiro desenho ........... 1 ms         com o driver de video quente; 24 a 28 com ele frio
	///       -- codigo rodando pela primeira vez .......... 2 a 3 ms     o .NET compilando o caminho do rastro
	///
	/// (Com o cache de shader do Godot CHEIO -- o de quem ja jogou uma vez -- a espera some sozinha: o defeito eram 8,5 a
	/// 10,9 ms de trabalho, num quadro de 10 a 12 ms de relogio. Com o driver FRIO, o de quem acabou de instalar, 59 a 61
	/// ms de relogio: os 25 da espera e mais 24 a 27 da pipeline montada do zero.)
	///
	/// DEPOIS: 5,5 a 8,4 ms de trabalho, numa volta do monitor, em qualquer um dos tres casos; nenhuma pipeline nasce no
	/// quadro, e a thread principal nao le o arquivo. A conta inteira, o preco no lobby e a regua estao no cabecalho do
	/// `RoboDoPrimeiroEstouro`, no bloco "E O PRIMEIRO BORRAO".
	///
	/// A ESPERA E DO MATERIAL, E NAO DO DESENHO (`--pecas --borraopartido`, que nasce cada peca sozinha num quadro): o
	/// quadro em que o primeiro material deste shader nasce para 24 ms no preparo MESMO com o sprite fora da arvore, sem
	/// nada desenhado. Um segundo depois, o primeiro desenho custa 1 ms e traz so a pipeline. (Que o motor precise do
	/// shader compilado pra montar os valores do material e leitura minha desse numero, e nao do fonte dele.)
	///
	/// O QUE O ATO NAO PAGA: o codigo do rastro rodando pela primeira vez (`RastroDeCorrida`,
	/// `CharacterVisual.Fotografar`). O palco tem um sprite, e nao um corpo correndo: esses 2 a 3 ms continuam no
	/// primeiro borrao do processo.
	/// ========================================================================================
	///
	/// UM QUADRO RECORTADO DE UMA FOLHA, como todo sprite do jogo (ver o cabecalho da classe) -- mas de uma textura
	/// GERADA, a radial das luzes, que o palco do ensaio ja usa: nenhuma folha de aparencia pode ser pedida daqui, porque
	/// quando o ensaio comeca elas ainda estao na thread de carga do `Aquecimento`.
	/// </summary>
	public static void Ensaiar(Node2D pai, Vector2 onde)
	{
		var s = new Sprite2D
		{
			Name = "BorraoDoEnsaio",
			Texture = new AtlasTexture { Atlas = Fogo.Radial(LuzDeKi.RaioDaTextura), Region = new Rect2(64, 64, 64, 64) },
			Position = onde,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
		};
		Aplicar(s, Vector2.Right, 1f);
		pai.AddChild(s);
	}
}
