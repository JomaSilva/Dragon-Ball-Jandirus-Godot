shader_type canvas_item;

uniform vec2  rumo = vec2(1.0, 0.0);          // direcao do movimento, normalizada
uniform float forca : hint_range(0.0, 1.0) = 1.0;
uniform vec2  quadro_min = vec2(0.0);          // a caixa DESTE quadro dentro do atlas
uniform vec2  quadro_max = vec2(1.0);

// ============================ A COR QUE O CORPO TEM AGORA ============================
// A copia do rastro e um `Sprite2D` com a textura da camada e ESTE material no lugar do
// `Personagem.gdshader` -- e a cor de um corpo nao mora na textura: mora nos dois uniforms de
// tinta daquele shader (a pele do Majin e do Kai, o cabelo e o rabo de uma forma, a cor de
// cada peca de roupa) e no modulate do node (o tom do Namekuseijin, a colada pintada). Sem
// eles a copia saia com a folha CRUA: o rastro de um Majin azul era o rosa do arquivo, o de
// um Super Saiyajin God tinha cabelo preto, e toda roupa tingida voltava a cor de fabrica.
// Foi o item 6 do playtest de 2026-10-10: *"o borrao do dash usa a cor base do personagem"*.
//
// AS DUAS FORMULAS SAO AS DO `Personagem.gdshader`, transcritas -- la esta o porque de cada
// modo. A bancada `--diagcorrastro` fotografa a copia e o corpo e compara pixel a pixel: se
// uma das duas mudar sem a outra, e ela que avisa.
//
// Com os valores de fabrica (tinta zero, modo soma, brilho um) nada muda: `cor + 0` e `x 1`.
// =====================================================================================
uniform vec3 tinta = vec3(0.0);
uniform int  tinta_modo = 0;                   // 0 = soma (o ICON_ADD do BYOND), 1 = matiz
uniform vec3 brilho = vec3(1.0);               // o modulate x self_modulate da camada

vec3 tingir(vec4 px) {
	if (tinta_modo == 1) {
		float luz = dot(px.rgb, vec3(0.299, 0.587, 0.114));
		return clamp(tinta * (luz * 2.0), 0.0, 1.0);
	}
	// A SOMA SO ENTRA ONDE HA DESENHO. O texel transparente nao aparece no corpo vivo, mas aqui
	// ele entra na MEDIA do borrao: somada nele, a tinta vazaria pra fora da silhueta como um
	// halo da cor do corpo, que o rastro de quem nao tem tinta nenhuma nao tem.
	return clamp(px.rgb + tinta * step(0.004, px.a), 0.0, 1.0);
}

void fragment() {
	// SETE AMOSTRAS AO LONGO DO RUMO, centradas: e o que transforma uma copia nitida num
	// borrao. Menos que isso deixa "degraus" visiveis; mais nao muda nada e custa.
	vec2 passo = rumo * TEXTURE_PIXEL_SIZE * 3.2 * forca;
	vec4 soma = vec4(0.0);
	float peso = 0.0;

	for (int i = -3; i <= 3; i++) {
		float w = 1.0 - abs(float(i)) * 0.22;
		vec2 uv = clamp(UV + passo * float(i), quadro_min, quadro_max);
		vec4 px = texture(TEXTURE, uv);
		px.rgb = tingir(px) * brilho;
		soma += px * w;
		peso += w;
	}
	COLOR = soma / peso;
}
