shader_type canvas_item;

// `unshaded` e `blend_premul_alpha` pelos mesmos dois motivos do `FeixeDeKi.gdshader`: luz propria, e
// corpo que COBRE com halo que SOMA no mesmo quad.
render_mode blend_premul_alpha, unshaded;

// ============================================================================
// TODA BOLA DE KI, SEM FOLHA -- a rajada, o disco, a Death Ball, a Genkidama, a carga na mao.
//
// E a outra metade do pedido do dono (2026-10-07): *"Pra outros ataques de ki isso tb seria
// interessante trocar sprite por puro efeito, noise e shaders em esferas como genkidama, super nova,
// blast etc"*.
//
// As folhas do original tinham OITO desenhos de fundo diferentes (bola, anel, disco, lamina, concha,
// losango, redemoinho, faisca) e um punhado de enfeites por cima (rastro, pontos em volta, aro,
// descargas). Aqui cada um virou um campo de distancia -- `forma` escolhe qual -- e os enfeites sao
// numeros de 0 a 1 que qualquer forma aceita. Quem escreve tudo isto e o `Client/ArteDeKiNoCliente.cs`.
//
// O quad e desenhado pelo `ProjetilDesenhado`, com UV em PIXELS a partir do centro e `+x` no rumo
// do voo (o disco e a excecao: ele nao gira com o rumo, ver `EstiloDeBola.SemGiro`).
// ============================================================================

const int ORBE = 0;         // a bola, com ou sem rastro de cometa
const int ANEL = 1;         // um aro oco (o Kill Driver)
const int DISCO = 2;        // a serra deitada (o Kienzan)
const int LAMINA = 3;       // a meia-lua (o sopro do Kiai)
const int CONCHA = 4;       // frente redonda, costas em espinho (o Kikoho)
const int LOSANGO = 5;      // a ponta de flecha (o tiro do Android)
const int VORTICE = 6;      // miolo com bracos em espiral (a Spirit Gun)
const int FAISCA = 7;       // um punhado de brilhos (o tiro do Genie, a paralisia)
const int ALVO = 8;         // aneis dentro de aneis (o alvo de treino)

uniform int forma = 0;
uniform float raio = 8.0;
/// O comprimento do rastro, em px (0 = parada). Ja vem multiplicado pela velocidade do tiro.
uniform float rastro = 0.0;

uniform float chama : hint_range(0.0, 1.0) = 0.0;       // costas e rastro em labareda
uniform float escuro : hint_range(0.0, 1.0) = 0.0;      // miolo escuro, casca acesa (a Death Ball)
uniform float fervura : hint_range(0.0, 1.0) = 0.0;     // superficie de sol (a Supernova)
uniform float calma : hint_range(0.0, 1.0) = 0.0;       // grande, clara e mansa (a Genkidama)
uniform float eletrico : hint_range(0.0, 1.0) = 0.0;    // descargas pela casca
uniform float pontilhado : hint_range(0.0, 1.0) = 0.0;  // pontos girando em volta
uniform float pontos = 8.0;                             // quantos (1 = um satelite)
uniform float aro : hint_range(0.0, 1.0) = 0.0;         // um aro de pe em volta do meio
uniform float pulsar : hint_range(0.0, 1.0) = 0.0;      // incha e murcha
uniform float brancura : hint_range(0.0, 1.0) = 0.5;
/// O alcance do halo, em px alem da casca. Finito, e escrito pelo `PintorDeKi` -- ver o `FeixeDeKi.gdshader`.
uniform float alcance_do_halo = 10.0;

uniform vec3 cor_borda : source_color = vec3(0.27, 0.55, 1.0);
uniform vec3 cor_manto : source_color = vec3(0.58, 0.85, 1.0);
uniform vec3 cor_nucleo : source_color = vec3(1.0);

uniform float tempo = 0.0;
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

float crista(vec2 p) {
	return 1.0 - abs(ruido(p) * 2.0 - 1.0);
}

float smin(float a, float b, float k) {
	float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
	return mix(b, a, h) - k * h * (1.0 - h);
}

/// Distancia EXATA a uma meia-lua: o circulo de raio `ra` na origem menos o de raio `rb` em (`d`, 0).
/// Exata e nao "o maior de dois campos": entre os dois circulos o maior dos dois e PEQUENO sem haver
/// lamina nenhuma por perto, e o halo desenhava um eclipse -- um aro claro fechando a volta toda.
float meia_lua(vec2 p, float d, float ra, float rb) {
	p.y = abs(p.y);
	float a = (ra * ra - rb * rb + d * d) / (2.0 * d);
	float b = sqrt(max(ra * ra - a * a, 0.0));
	if (d * (p.x * b - p.y * a) > d * d * max(b - p.y, 0.0))
		return length(p - vec2(a, b));
	return max(length(p) - ra, -(length(p - vec2(d, 0.0)) - rb));
}

vec2 gira(vec2 p, float a) {
	float c = cos(a);
	float s = sin(a);
	return vec2(c * p.x - s * p.y, s * p.x + c * p.y);
}

void fragment() {
	vec2 p = UV;
	float t = tempo * mix(1.0, 0.35, calma);
	float R = raio * (1.0 + 0.03 * sin(tempo * 31.0 + semente)
					- pulsar * 0.22 * (0.5 + 0.5 * sin(tempo * 17.0 + semente * 3.0)));
	float r = length(p);
	vec2 dir = p / max(r, 0.0001);
	float ang = atan(p.y, p.x);

	// a casca viva: o raio ondula em volta
	float onda = fbm(dir * 1.9 + vec2(t * 1.7, -t * 1.1) + semente) - 0.5;
	float onda2 = ruido(dir * 5.0 + vec2(-t * 4.0, t * 3.0) + semente * 2.0) - 0.5;
	// a Supernova e um SOL: redonda, com a borda em labareda miuda -- e nao uma pedra de casca torta
	float ondula = (0.10 - 0.07 * fervura) * onda * 2.0 + 0.04 * onda2 * (1.0 - calma)
				 + fervura * 0.07 * crista(dir * 7.0 + vec2(t * 3.0, -t * 2.0) + semente);

	// ================= a FORMA =================
	float d;                    // a distancia a casca
	float eixo = R;             // a profundidade do meio: o divisor de `fundo`
	if (forma == ANEL) {
		float grosso = R * 0.17;
		d = abs(r - R * (0.82 + ondula * 0.5)) - grosso;
		eixo = grosso;
	} else if (forma == DISCO) {
		// uma serra deitada: elipse larga, dentes correndo pela borda
		vec2 e = vec2(p.x, p.y / 0.44);
		float re = length(e);
		float fi = atan(e.y, e.x);
		float k = fi / 6.2831853 * 14.0 + tempo * 9.0;
		float dente = 1.0 - fract(k);
		d = (re - R * (0.90 + 0.13 * dente * dente)) * 0.44;
		eixo = R * 0.44;
	} else if (forma == LAMINA) {
		// meia-lua de costas pra tras: o circulo de fora menos um de dentro deslocado
		d = meia_lua(vec2(-p.x, p.y), R * 0.52, R, R * 1.02);
		eixo = R * 0.25;
	} else if (forma == CONCHA) {
		// frente redonda; as costas sao cortadas e rasgadas em espinho
		float espinho = crista(vec2(p.y / R * 3.2 + semente, tempo * 6.0));
		float corte = -(p.x + R * (0.10 + 0.55 * espinho * (1.0 - abs(p.y) / (R * 1.05))));
		d = max(r - R * (1.0 + ondula * 0.4), corte);
		eixo = R * 0.55;
	} else if (forma == LOSANGO) {
		// ponta de flecha: comprida pra frente, curta pra tras
		float a = p.x > 0.0 ? R * 1.25 : R * 0.80;
		d = (abs(p.x) / a + abs(p.y) / (R * 0.72) - 1.0) * R * 0.52;
		eixo = R * 0.52;
	} else if (forma == VORTICE) {
		// o miolo e uma bola; os bracos sao a casca empurrada pra fora em espiral
		float braco = 0.5 + 0.5 * sin(ang * 5.0 + log(max(r / R, 0.05)) * 6.0 - tempo * 8.0 + semente);
		float alcance_b = R * (0.50 + 0.58 * pow(braco, 2.2));
		d = r - alcance_b * (1.0 + ondula * 0.6);
		eixo = R * 0.55;
	} else if (forma == FAISCA) {
		// um punhado de brilhos de quatro pontas, sorteados de novo varias vezes por segundo
		float quadro = floor(tempo * 10.0);
		d = r - R * 0.30;
		for (int k = 0; k < 5; k++) {
			float sk = floor(semente * 9.0) + float(k) * 13.0 + quadro * 3.0;
			vec2 c = (vec2(h21(vec2(sk, 1.0)), h21(vec2(sk, 2.0))) - 0.5) * R * 1.7;
			vec2 q = abs(gira(p - c, 0.785 * step(0.5, h21(vec2(sk, 5.0)))));
			float tam = R * (0.18 + 0.42 * h21(vec2(sk, 3.0)));
			// estrela de quatro pontas: o produto das duas coordenadas e constante numa hiperbole
			float estrela = (q.x * q.y) / (tam * 0.16) + max(q.x, q.y) - tam;
			d = min(d, estrela);
		}
		eixo = R * 0.30;
	} else if (forma == ALVO) {
		// um disco; os aneis sao de COR (ver adiante), e nao de forma
		d = r - R;
	} else {
		d = r - R * (1.0 + ondula);
		// as costas em labareda: quanto mais pra tras, mais rasgada
		if (chama > 0.001) {
			float costas = smoothstep(0.25, -0.85, dir.x);
			float lingua = crista(vec2(p.y / R * 2.8 + semente, p.x / R * 1.1 + tempo * 7.0));
			d -= R * chama * costas * (0.10 + 0.42 * lingua);
		}
	}

	// ---- o rastro de cometa (toda forma que voa pode ter) ----
	if (rastro > 0.5) {
		float x = clamp(-p.x / rastro, 0.0, 1.0);
		float lado = p.y < 0.0 ? -1.0 : 1.0;
		float fiapo = mix(ruido(vec2(p.x / (R * 1.3) + tempo * 9.0, semente + lado * 2.0)),
						  crista(vec2(p.x / (R * 0.8) + tempo * 11.0, semente + lado * 5.0)), chama);
		float w = R * 0.90 * pow(1.0 - x, 1.35) * (0.78 + (0.45 + 0.5 * chama) * (fiapo - 0.3));
		float d_cauda = max(abs(p.y) - w, max(p.x, -p.x - rastro));
		d = smin(d, d_cauda, R * 0.5);
	}

	float fundo = clamp(-d / eixo, 0.0, 1.0);           // 0 na casca, 1 no meio

	// ================= a COR =================
	// veios girando por dentro, com a coordenada dobrada pelo proprio ruido (e o que da filamento)
	vec2 q = gira(p / raio, t * 0.9 + semente);
	vec2 dobra = vec2(fbm(q * 1.6 + vec2(0.0, t * 0.6)), fbm(q * 1.6 + vec2(5.2, -t * 0.5)));
	float veio = fbm(q * 1.9 + (dobra - 0.5) * 1.6 + semente);

	float lim = mix(0.84, 0.22, brancura) + 0.28 * (veio - 0.5);
	float nucleo = smoothstep(lim, lim + 0.24, fundo);
	float manto = smoothstep(0.03, 0.30, fundo);
	vec3 rgb = mix(cor_borda, cor_manto, manto) * (0.80 + 0.42 * veio);
	rgb = mix(rgb, cor_nucleo, nucleo);

	if (forma == DISCO) {
		// as faixas do disco: um anel claro por dentro e um brilho que da a volta
		vec2 e = vec2(p.x, p.y / 0.44) / R;
		float re = length(e);
		float faixa = (1.0 - smoothstep(0.0, 0.10, abs(re - 0.56))) * 0.55;
		float varre = pow(0.5 + 0.5 * cos(atan(e.y, e.x) - tempo * 13.0), 6.0) * smoothstep(0.25, 0.85, re);
		rgb = mix(rgb, cor_nucleo, clamp(faixa + varre * 0.7, 0.0, 1.0));
	}

	if (forma == ALVO) {
		// aneis que nascem no centro e andam pra fora: e o que a folha do alvo faz em sete quadros
		float anel = 0.5 + 0.5 * sin((r / R) * 15.0 - tempo * 6.0);
		rgb = mix(cor_borda, mix(cor_manto, cor_nucleo, anel), smoothstep(0.25, 0.55, anel));
	}

	// GENKIDAMA: quase toda luz, com veios mansos e so a borda da cor
	if (calma > 0.001) {
		vec3 g = mix(cor_manto, cor_nucleo, 0.35 + 0.55 * smoothstep(0.0, 0.50, fundo));
		g *= 0.90 + 0.16 * veio;
		g = mix(g, vec3(1.0), smoothstep(0.30, 0.90, fundo) * 0.75);
		rgb = mix(rgb, g, calma);
	}

	// SUPERNOVA: a superficie ferve -- granulos claros, veias escuras, e o limbo mais escuro que o meio
	if (fervura > 0.001) {
		float gran = fbm(q * 3.4 + (dobra - 0.5) * 2.2 + vec2(t * 0.8, 0.0));
		vec3 fundo_cor = cor_borda * vec3(0.72, 0.40, 0.34);
		vec3 quente = mix(cor_manto, vec3(1.0, 1.0, 0.82), 0.55);
		vec3 s = mix(fundo_cor, cor_borda, smoothstep(0.25, 0.50, gran));
		s = mix(s, quente, smoothstep(0.50, 0.74, gran));
		s = mix(s, vec3(1.0), smoothstep(0.74, 0.92, gran) * 0.9);
		s *= mix(0.62, 1.0, smoothstep(0.0, 0.42, fundo));
		rgb = mix(rgb, s, fervura);
	}

	// DEATH BALL: miolo escuro, casca acesa, veias de luz rachando o escuro
	if (escuro > 0.001) {
		float racha = fbm(q * 2.6 + (dobra - 0.5) * 2.4);
		float fio = (1.0 - smoothstep(0.0, 0.035, abs(racha - 0.5))) * (1.0 - smoothstep(0.35, 0.95, fundo));
		vec3 breu = cor_borda * 0.12 + vec3(0.02, 0.0, 0.03);
		vec3 e = mix(cor_manto, breu, smoothstep(0.05, 0.42, fundo));
		e = mix(e, mix(cor_manto, cor_nucleo, 0.5), fio * 0.9);
		// um brilho de vidro no alto: e o que faz a bola parecer BOLA e nao um disco preto
		float reflexo = 1.0 - smoothstep(0.0, 0.55, length(p / raio - vec2(-0.30, -0.36)));
		e += vec3(reflexo * reflexo) * 0.40;
		rgb = mix(rgb, e, escuro);
	}

	float aa = max(fwidth(d), 0.0001);
	float dentro = smoothstep(aa, -aa, d);

	// ================= o HALO =================
	float alc = alcance_do_halo;
	float h = clamp(1.0 - max(d, 0.0) / alc, 0.0, 1.0);
	float halo = h * h * h * (0.80 + 0.30 * onda);
	vec3 rgb_halo = mix(cor_borda, cor_manto, 0.35 + 0.4 * calma) * halo;
	float a_halo = halo * 0.32;

	vec3 saida = rgb * dentro + rgb_halo * (1.0 - dentro);
	float alfa = dentro + a_halo * (1.0 - dentro);

	// ================= os ENFEITES =================
	// pontos girando em volta (a Esfera Teleguiada, metade das cargas)
	if (pontilhado > 0.001) {
		float n = max(pontos, 1.0);
		float a = ang - tempo * (n < 1.5 ? 6.0 : 2.4) + semente;
		float k = a / 6.2831853 * n;
		float no_arco = (fract(k) - 0.5) / n * 6.2831853 * R * 1.55;
		float tam = R * (n < 1.5 ? 0.30 : 0.16);
		float dp = length(vec2(no_arco, r - R * 1.55)) - tam;
		float f = (1.0 - smoothstep(-1.0, 1.0, dp / max(fwidth(dp), 0.0001))) * pontilhado;
		saida = mix(saida, mix(cor_manto, cor_nucleo, 0.5), f);
		alfa = max(alfa, f);
	}

	// um aro de pe em volta do meio (o tiro do Ogre)
	if (aro > 0.001) {
		float ry = R * 1.6;
		float rx = ry * 0.34;
		float e = length(vec2(p.x / rx, p.y / ry)) - 1.0;
		float de = abs(e) * rx - R * 0.10;
		float f = (1.0 - smoothstep(-1.0, 1.0, de / max(fwidth(de), 0.0001))) * aro;
		// a metade de tras do aro passa por baixo da bola
		float visivel = mix(1.0 - dentro, 1.0, step(0.0, p.x));
		saida = mix(saida, mix(cor_borda, cor_nucleo, 0.4), f * visivel);
		alfa = max(alfa, f * visivel);
	}

	// descargas pela casca
	if (eletrico > 0.001) {
		float quadro = floor(tempo * 13.0);
		float luz = 0.0;
		for (int k = 0; k < 3; k++) {
			float sk = floor(semente * 11.0) + float(k) * 23.0 + quadro * 5.0;
			float a0 = h21(vec2(sk, 2.0)) * 6.2831853;
			float da = mod(ang - a0 + 3.14159265, 6.2831853) - 3.14159265;
			float abre = 0.9 + 0.9 * h21(vec2(sk, 3.0));
			float trecho = 1.0 - smoothstep(abre * 0.7, abre, abs(da));
			float rr = R * (1.10 + 0.36 * (ruido(vec2(da * 4.0, sk)) - 0.5) + 0.14 * (ruido(vec2(da * 13.0, sk + 4.0)) - 0.5));
			float dl = abs(r - rr);
			luz += trecho * ((1.0 - smoothstep(R * 0.015, R * 0.07, dl)) + 0.30 * (1.0 - smoothstep(0.0, R * 0.30, dl)));
		}
		luz = clamp(luz, 0.0, 1.0) * eletrico;
		saida = mix(saida, mix(cor_manto, cor_nucleo, 0.8), luz);
		alfa = max(alfa, luz);
	}

	COLOR = vec4(saida, clamp(alfa, 0.0, 1.0));
}
