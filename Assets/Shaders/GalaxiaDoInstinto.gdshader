shader_type canvas_item;
render_mode blend_mix, unshaded;

// ============================================================================
// A GALAXIA DO PERFECTED (2026-10-25) -- a terceira imagem do dono: uma espiral branca e azul-lilas no escuro, com pedras
// escuras flutuando ("aparece essa galaxia em volta do personagem e no final ela comprime nele causando um clarao"). Um
// disco atras do corpo, girando. Quem o abre, gira e comprime e o tocador (`Transformacao.EstreiaDivina.cs`):
//   raio  -- o raio do disco agora (px do mundo): abre, fica, e encolhe ate zero na compressao;
//   giro  -- o angulo da espiral (rad), que acelera na compressao;
//   miolo -- de 0 a 1, o brilho do centro, que sobe enquanto ela comprime;
//   alfa  -- o disco todo.
// ============================================================================

uniform float lado = 308.0;
uniform float raio = 0.0;
uniform float giro = 0.0;
uniform float miolo = 0.0;
uniform float alfa = 1.0;

float hash1(float n) { return fract(sin(n) * 43758.5453); }
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

float fbm(vec2 p)
{
	float v = 0.0;
	float a = 0.5;
	for (int k = 0; k < 4; k++)
	{
		v += a * ruido(p);
		p = p * 2.03 + vec2(5.2, 1.3);
		a *= 0.5;
	}
	return v;
}

mat2 girar(float a)
{
	float s = sin(a);
	float c = cos(a);
	return mat2(vec2(c, s), vec2(-s, c));
}

// AS PEDRAS ESCURAS da imagem: onze, cada uma no seu raio e no seu passo, de borda irregular.
float pedras(vec2 p, float r)
{
	float m = 0.0;
	for (int k = 0; k < 11; k++)
	{
		float fk = float(k);
		float rr = r * (0.30 + 0.60 * hash1(fk * 7.13 + 1.0));
		float aa = hash1(fk * 3.71 + 2.0) * 6.2832 + giro * (0.35 + 0.5 * hash1(fk * 1.9 + 3.0));
		vec2 centro = vec2(cos(aa), sin(aa)) * rr;
		float tam = (1.6 + 3.2 * hash1(fk * 5.3 + 4.0)) * clamp(r / 60.0, 0.0, 1.0);
		vec2 d = p - centro;
		float borda = tam * (0.7 + 0.45 * ruido(d * 0.8 + fk * 3.0));
		m = max(m, 1.0 - smoothstep(borda - 0.8, borda + 0.4, length(d)));
	}
	return m;
}

void fragment()
{
	vec2 p = (UV - 0.5) * lado;
	float rn = length(p) / max(raio, 0.001);
	float ang = atan(p.y, p.x);

	// DOIS BRACOS em espiral logaritmica, girando, e o gas que os rasga
	float braco = pow(0.5 + 0.5 * cos(2.0 * (ang - giro) + log(rn + 0.04) * 5.0), 2.2);
	vec2 q = girar(-giro * 0.7) * (p / max(raio, 0.001));
	float gas = fbm(q * 3.5 + 2.0);
	float luz = braco * exp(-rn * 2.1) * (0.55 + 0.9 * gas);
	vec3 espaco = vec3(0.02, 0.02, 0.07);
	vec3 azul = vec3(0.56, 0.64, 1.0);
	vec3 lilas = vec3(0.80, 0.70, 1.0);
	vec3 c = mix(espaco, mix(azul, lilas, smoothstep(0.35, 0.7, gas)), clamp(luz * 1.7, 0.0, 1.0));

	// o miolo branco, que incha enquanto ela comprime
	float centro = exp(-rn * rn * (26.0 - 18.0 * miolo));
	c = mix(c, vec3(1.0), clamp(centro * (0.85 + miolo), 0.0, 1.0));

	// estrelas finas
	c += vec3(0.9, 0.92, 1.0) * step(0.992, hash(floor(q * 90.0))) * max(1.0 - rn, 0.0);

	// e as pedras por cima
	float pd = pedras(p, raio);
	c = mix(c, vec3(0.03, 0.03, 0.04), pd * 0.92);

	float borda = 1.0 - smoothstep(0.78, 1.0, rn);
	float a = clamp(0.58 + luz * 0.6 + centro + pd, 0.0, 1.0) * borda * alfa * step(0.5, raio);
	COLOR = vec4(min(c, vec3(1.0)), a);
}
