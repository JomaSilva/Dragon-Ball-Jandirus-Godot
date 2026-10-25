shader_type canvas_item;
render_mode blend_mix, unshaded;

// ============================================================================
// O FOGO EM VOLTA DO ULTRA EGO (2026-10-25) -- a quarta imagem do dono: um paredao de fogo magenta subindo ("uns efeitos q
// parecem fogo em volta roxo"). Um anel de chamas no chao em volta do corpo -- uma elipse: o chao visto de cima e de lado --,
// e cada ponto do anel solta uma chama pra cima. Atras do corpo. A cor e a da forma. Quem anda os tempos e o tocador
// (`Transformacao.EstreiaDivina.cs`):
//   forca -- o tamanho das chamas (0 apagado, 1 cheio, um pouco acima de 1 no estouro da virada);
//   alfa  -- o fogo todo.
// ============================================================================

uniform vec2 lado = vec2(148.0, 160.0);
/// Os pes no quadro (px).
uniform vec2 pe = vec2(74.0, 130.0);
/// Os raios da elipse no chao (px).
uniform vec2 anel = vec2(54.0, 19.0);
/// A chama cheia (px).
uniform float altura = 72.0;
uniform float forca = 0.0;
uniform float alfa = 1.0;
uniform vec4 cor : source_color = vec4(0.73, 0.42, 1.0, 1.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float ruido(vec2 p)
{
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash(i);
	float b = hash(i + vec2(1.0, 0.0));
	float c = hash(i + vec2(0.0, 1.0));
	float d = hash(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// A CHAMA DE UM PONTO DO ANEL: cheia embaixo, rasgada em cima, tremendo pra cima. `subida` e a altura do pixel acima do ponto.
float chama(float x, float subida, float semente)
{
	if (subida < -3.0) return 0.0;
	float n = ruido(vec2(x * 0.09 + semente, subida * 0.05 - TIME * 2.4));
	float n2 = ruido(vec2(x * 0.21 - semente, subida * 0.11 - TIME * 3.7));
	float lim = altura * forca * (0.35 + 0.75 * n);
	float v = (1.0 - smoothstep(lim * 0.45, lim, subida)) * smoothstep(-3.0, 3.0, subida);
	return clamp(v * (0.75 + 0.5 * n2), 0.0, 1.0);
}

void fragment()
{
	vec2 p = UV * lado - pe;                  // px, a partir dos pes; o y cresce pra baixo
	float xn = p.x / anel.x;
	float s = sqrt(max(0.0, 1.0 - xn * xn));
	float lados = 1.0 - smoothstep(0.92, 1.08, abs(xn));
	// as duas metades do anel: a de tras (acima dos pes, na tela) e a da frente (abaixo)
	float atras = chama(p.x, -anel.y * s - p.y, 1.7);
	float frente = chama(p.x, anel.y * s - p.y, 6.3);
	float f = max(atras, frente) * lados * step(0.001, forca);

	vec3 escuro = cor.rgb * 0.38 + vec3(0.18, 0.0, 0.12);
	vec3 claro = mix(cor.rgb, vec3(1.0, 0.92, 1.0), 0.65);
	vec3 c = mix(escuro, cor.rgb, smoothstep(0.15, 0.55, f));
	c = mix(c, claro, smoothstep(0.72, 1.0, f));
	COLOR = vec4(c, clamp(f * 1.25, 0.0, 1.0) * alfa);
}
