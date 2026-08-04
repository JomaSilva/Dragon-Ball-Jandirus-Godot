shader_type canvas_item;

uniform sampler2D tela : hint_screen_texture, repeat_disable, filter_linear;
uniform vec2 centro = vec2(0.5);
uniform float raio = 0.0;          // 0..1 do menor lado da tela
uniform float espessura = 0.06;
uniform float forca = 0.03;        // deslocamento maximo, em UV
uniform vec4 cor : source_color = vec4(1.0, 0.85, 0.35, 1.0);
uniform float brilho = 1.0;
uniform float proporcao = 1.777;   // largura/altura, pra o anel sair REDONDO

void fragment() {
	// corrige a proporcao: sem isto o anel vira uma elipse deitada em tela wide
	vec2 d = UV - centro;
	d.x *= proporcao;
	float dist = length(d);

	// o quanto este pixel esta DENTRO do anel (1 na crista, 0 fora)
	float faixa = 1.0 - smoothstep(0.0, espessura, abs(dist - raio));

	// EMPURRA PRA FORA na direcao radial. O sinal usa a crista: dentro do anel os
	// pixels vao pra fora, e e isso que da a sensacao de ar sendo deslocado.
	vec2 dir = dist > 0.0001 ? normalize(UV - centro) : vec2(0.0);
	vec2 uv = SCREEN_UV + dir * faixa * forca;

	vec3 c = texture(tela, uv).rgb;

	// a CRISTA acende. Elevar ao cubo deixa o brilho concentrado na linha em vez de
	// espalhado num borrao.
	float crista = faixa * faixa * faixa;
	c += cor.rgb * crista * brilho;

	COLOR = vec4(c, 1.0);
}
