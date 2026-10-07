shader_type canvas_item;

// `unshaded` e `blend_premul_alpha` pelos mesmos dois motivos do `FeixeDeKi.gdshader`: luz propria, e
// miolo que COBRE com franja que SOMA no mesmo quad.
render_mode blend_premul_alpha, unshaded;

// ============================================================================
// O ESTOURO DE UM ATAQUE DE KI -- o que sobra na tela quando o tiro acerta.
//
// O tiro morria com a faisca de soco (`attackspark`, uma folha) num tamanho fixo. Enquanto a bola era
// uma folha de 32 px isso passava; com a bola desenhada por shader no tamanho de verdade -- uma
// Death Ball de 36 px de raio, uma Genkidama de 96 -- a maior coisa do jogo sumia num estalinho do
// tamanho de um punho. O estouro e do TAMANHO do que estourou, e da cor dele.
//
// Uma vida so, de `t` = 0 (acertou) a 1 (sumiu), em quatro pecas que se revezam:
//   * o CLARAO  -- um disco branco que abre e some no primeiro quinto da vida;
//   * a BOLA    -- a massa de fogo: nasce cheia, e o ruido vai abrindo BURACOS nela ate so sobrar fiapo;
//   * as LASCAS -- riscos pra fora, so no comeco;
//   * o ANEL    -- a onda de choque, que continua abrindo depois que a bola ja se desfez.
//
// O quad e desenhado pelo `EstouroDeKi.cs`, com UV em PIXELS a partir do centro.
// ============================================================================

/// O raio da bola de fogo no auge, em px. O anel vai ate ~2,2x isto.
uniform float raio = 20.0;
uniform vec3 cor : source_color = vec3(0.4, 0.7, 1.0);

/// 0 = acabou de acertar, 1 = sumiu. O unico relogio do efeito.
uniform float t : hint_range(0.0, 1.0) = 0.0;
uniform float semente = 0.0;

// O sorteio em inteiros -- ver a nota no `FeixeDeKi.gdshader`: o de ponto flutuante rasga o ruido.
float h21(vec2 p) {
	uvec2 q = uvec2(ivec2(floor(p)));
	uint n = q.x * 1597334673u ^ q.y * 3812015801u;
	n = (n ^ (n >> 15u)) * 2246822519u;
	n = (n ^ (n >> 13u)) * 3266489917u;
	n ^= n >> 16u;
	return float(n >> 8u) * (1.0 / 16777216.0);
}

float ruido(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	return mix(mix(h21(i), h21(i + vec2(1.0, 0.0)), f.x),
			   mix(h21(i + vec2(0.0, 1.0)), h21(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
	float a = 0.5;
	float s = 0.0;
	for (int i = 0; i < 4; i++) {
		s += a * ruido(p);
		p = p * 2.03 + vec2(17.1, 9.3);
		a *= 0.5;
	}
	return s / 0.9375;
}

void fragment() {
	vec2 p = UV;
	float r = length(p);
	vec2 dir = p / max(r, 0.0001);
	float ang = atan(p.y, p.x);

	// ---- a bola: abre depressa (a raiz do tempo), e o ruido a come por dentro ----
	float abre = sqrt(clamp(t * 2.2, 0.0, 1.0));
	float R = raio * (0.35 + 0.65 * abre) * (1.0 + 0.10 * t);
	float casca = fbm(dir * 2.6 + vec2(r / raio * 0.9 - t * 2.0) + semente);
	float d = r - R * (0.86 + 0.34 * casca);

	// os buracos: o corte sobe com o tempo, e onde o ruido fica abaixo dele a bola ja se desfez
	float trama = fbm(p / raio * 2.3 + dir * (t * 1.4) + semente * 3.0);
	float corte = smoothstep(0.25, 1.0, t) * 1.15;
	float cheio = smoothstep(corte - 0.10, corte + 0.10, trama + (1.0 - r / max(R, 0.001)) * 0.35);

	float fundo = clamp(-d / max(R, 0.001), 0.0, 1.0);
	float quente = 1.0 - smoothstep(0.0, 0.55, t);                 // o miolo esfria: de branco pra cor
	vec3 rgb = mix(cor * 0.9, mix(cor, vec3(1.0), 0.5), smoothstep(0.02, 0.30, fundo));
	rgb = mix(rgb, vec3(1.0), smoothstep(0.30, 0.62, fundo) * (0.25 + 0.75 * quente));

	float aa = max(fwidth(d), 0.0001);
	float dentro = smoothstep(aa, -aa, d) * cheio * (1.0 - smoothstep(0.70, 1.0, t));

	// ---- o clarao: so no comeco ----
	float clarao = (1.0 - smoothstep(0.0, 0.22, t)) * (1.0 - smoothstep(0.0, raio * (0.7 + 2.0 * t), r));

	// ---- as lascas: riscos pra fora, que somem antes da bola ----
	float lascas = 0.0;
	{
		float n = 18.0;
		float k = (ang + 3.14159265) / 6.2831853 * n + h21(vec2(floor(semente * 31.0), 5.0)) * n;
		float casa = mod(floor(k), n);
		float sorte = h21(vec2(casa, floor(semente * 17.0)));
		float comp = raio * (1.2 + 2.0 * sorte) * (0.3 + 1.4 * abre);
		float fina = 1.0 - smoothstep(0.0, 0.5, abs(fract(k) - 0.5) * 2.0 / (0.16 + 0.22 * sorte) * (1.0 + r / raio * 0.5));
		lascas = step(0.35, sorte) * fina * smoothstep(R * 0.5, R * 0.95, r) * (1.0 - smoothstep(comp * 0.5, comp, r))
			   * (1.0 - smoothstep(0.10, 0.50, t));
	}

	// ---- o anel: a onda de choque, que continua abrindo ----
	float ra = raio * (0.5 + 1.7 * (1.0 - (1.0 - t) * (1.0 - t)));
	float anel = (1.0 - smoothstep(0.0, raio * 0.14 * (1.0 - 0.6 * t), abs(r - ra))) * (1.0 - t) * (1.0 - t);

	// ---- o halo, de alcance finito (ver o `FeixeDeKi.gdshader`) ----
	float hh = clamp(1.0 - max(d, 0.0) / (raio * 0.9), 0.0, 1.0);
	float halo = hh * hh * hh * (1.0 - smoothstep(0.35, 0.9, t));

	vec3 claro = mix(cor, vec3(1.0), 0.7);
	vec3 soma = cor * halo * 0.8 + claro * lascas + mix(cor, vec3(1.0), 0.35) * anel * 0.7 + vec3(clarao);
	float cobre = max(max(lascas * 0.5, halo * 0.30), clarao * 0.85);

	vec3 saida = rgb * dentro + soma * (1.0 - dentro);
	float alfa = dentro + cobre * (1.0 - dentro);

	COLOR = vec4(saida, clamp(alfa, 0.0, 1.0));
}
