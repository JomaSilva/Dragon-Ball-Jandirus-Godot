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
	/// </summary>
	private const string CaminhoDoShader = "res://Assets/Shaders/Borrao.gdshader";

	private static Shader? _sh;
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
	public static void Aplicar(Sprite2D s, Vector2 rumo, float forca)
	{
		ShaderMaterial m = s.Material is ShaderMaterial velho && velho.Shader == Sh
			? velho
			: new ShaderMaterial { Shader = Sh };
		(Vector2 min, Vector2 max) = Caixa(s.Texture);
		m.SetShaderParameter("rumo", rumo);
		m.SetShaderParameter("forca", forca);
		m.SetShaderParameter("quadro_min", min);
		m.SetShaderParameter("quadro_max", max);
		s.Material = m;
	}
}
