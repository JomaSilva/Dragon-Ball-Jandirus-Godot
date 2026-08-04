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
