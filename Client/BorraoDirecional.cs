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
	private const string Codigo = """
		shader_type canvas_item;

		uniform vec2  rumo = vec2(1.0, 0.0);          // direcao do movimento, normalizada
		uniform float forca : hint_range(0.0, 1.0) = 1.0;
		uniform vec2  quadro_min = vec2(0.0);          // a caixa DESTE quadro dentro do atlas
		uniform vec2  quadro_max = vec2(1.0);

		void fragment() {
			// SETE AMOSTRAS AO LONGO DO RUMO, centradas: e o que transforma uma copia nitida num
			// borrao. Menos que isso deixa "degraus" visiveis; mais nao muda nada e custa.
			vec2 passo = rumo * TEXTURE_PIXEL_SIZE * 3.2 * forca;
			vec4 soma = vec4(0.0);
			float peso = 0.0;

			for (int i = -3; i <= 3; i++) {
				float w = 1.0 - abs(float(i)) * 0.22;
				vec2 uv = clamp(UV + passo * float(i), quadro_min, quadro_max);
				soma += texture(TEXTURE, uv) * w;
				peso += w;
			}
			COLOR = soma / peso;
		}
		""";

	private static Shader? _sh;
	public static Shader Sh => _sh ??= new Shader { Code = Codigo };

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

	/// <summary>Poe o borrao num sprite ja pronto, com o recorte do quadro dele.</summary>
	public static void Aplicar(Sprite2D s, Vector2 rumo, float forca)
	{
		var m = new ShaderMaterial { Shader = Sh };
		(Vector2 min, Vector2 max) = Caixa(s.Texture);
		m.SetShaderParameter("rumo", rumo);
		m.SetShaderParameter("forca", forca);
		m.SetShaderParameter("quadro_min", min);
		m.SetShaderParameter("quadro_max", max);
		s.Material = m;
	}
}
