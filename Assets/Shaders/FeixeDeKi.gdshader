shader_type canvas_item;

// `unshaded`: o feixe TEM LUZ PROPRIA, como todo efeito de energia deste projeto (ver o cabecalho do
// `NebulosaDaForma.gdshader`). De noite o `CanvasModulate` escurece o mundo e nao o raio -- quem
// clareia o chao em volta dele continua sendo a `LuzDeKi`.
//
// `blend_premul_alpha`: o MESMO quad pinta corpo opaco e halo aditivo. Com alfa pre-multiplicado um
// pixel de alfa 0 e RGB > 0 SOMA na tela, e um de alfa 1 COBRE -- entao o corpo le em fundo claro
// (onde somar branco nao muda pixel nenhum, a licao do "branco some em fundo claro") e o halo
// clareia sem sujar.
render_mode blend_premul_alpha, unshaded;

// ============================================================================
// O RAIO DE KI, SEM FOLHA.
//
// Pedido do dono (2026-10-07), vendo o trailer: *"uma solucao melhor pros beams seria ao inves de
// usar sprite pra cada beam como era no byond, fazer eles por efeitos, noise e shaders do proprio
// godot, assim em colisao eles podem ter o efeito... q o beam se deforma ao se chocar... alem de
// beam na diagonal tb n ficar aquela coisa picotada e poder ser continuo em todas as direcoes e
// poder ate dar curva"*.
//
// O desenho antigo era o TREM do BYOND: tres folhas (`origin`, `tail`, `head`) carimbadas sem girar
// ao longo do eixo, escolhidas entre 8 (ou 4) direcoes. Fora dos eixos da folha cada carimbo
// escorregava de lado em relacao ao anterior -- a escada que o dono viu.
//
// AQUI NAO HA CARIMBO. A geometria e UMA FITA ao longo do caminho (`ProjetilDesenhado` a monta), e
// cada pixel sabe duas coisas so: quanto ja andou desde a mao (`UV.x`, em px) e quao longe esta do
// eixo (`UV.y`, em px). Angulo nenhum entra na conta, entao nao ha angulo ruim; e como a fita segue
// um CAMINHO, ele pode ser curvo.
//
// A FORMA E UM CAMPO DE DISTANCIA: tronco, cabeca e boca sao tres distancias unidas por um minimo
// suave -- a silhueta sai inteira, sem emenda entre as tres pecas.
//
// NUMA DISPUTA O FEIXE NAO MUDA. Este shader ja teve um `embate` que abria o tronco em cone ate o
// encontro, inchava a ponta e punha a mao em chamas. O dono viu no jogo e dispensou: *"N precisa fazer
// a ponta do beam ficar maior na colisao, so a estrela no centro do choque basta, ja fica legal"*.
// Quem desenha o choque e a estrela (`ChoqueDeKi.gdshader`), por cima das duas pontas.
//
// AS MEDIDAS VEM DO CORE E SAO REGRA: `raio` e `ArteDeProjetil.MeiaEspessuraDoTronco` e a ponta da
// cabeca cai em `ArteDeProjetil.FrenteDaCabeca` a frente da posicao do tiro. O servidor corta,
// cruza e encosta por esses dois numeros -- o que se ve e o que acerta.
// ============================================================================

// ---- a geometria, em px, ja na escala do tiro ----
uniform float comprimento = 200.0;   // da mao ate a PONTA da cabeca
uniform float raio = 5.0;            // meia espessura SOLIDA do tronco
uniform float raio_cabeca = 7.0;
uniform float raio_boca = 7.0;
/// Ate onde chegam os enfeites do tronco (arcos, fiapos, aneis): e a meia espessura da REGRA.
uniform float alcance = 5.0;
/// O alcance do halo, em px alem da casca. FINITO de proposito: um halo exponencial nunca chega a
/// zero, e o que sobra na borda da fita desenha o RETANGULO da geometria. Quem escreve e o
/// `PintorDeKi`, que e tambem quem dimensiona a fita -- um numero so, dos dois lados.
uniform float alcance_do_halo = 12.0;

// ---- o estilo da arte, de 0 a 1 (ver `Client/ArteDeKiNoCliente.cs`, que e quem escreve) ----
uniform float ponta : hint_range(0.0, 1.0) = 0.0;       // cabeca em seta/broca em vez de bola
uniform float chama : hint_range(0.0, 1.0) = 0.0;       // linguas de fogo nas duas pontas
uniform float coroa : hint_range(0.0, 1.0) = 0.0;       // cabeca em estouro de espinhos
uniform float serrilha : hint_range(0.0, 1.0) = 0.0;    // dentes na casca do tronco
uniform float granulado : hint_range(0.0, 1.0) = 0.0;   // faixas e grao por dentro
uniform float fiapos : hint_range(0.0, 1.0) = 0.0;      // linguas esparsas saindo do tronco
uniform float aneis : hint_range(0.0, 1.0) = 0.0;       // aneis correndo em volta
uniform float espiral : hint_range(0.0, 1.0) = 0.0;     // fitas enroladas
uniform float eletrico : hint_range(0.0, 1.0) = 0.0;    // descargas rastejando
uniform float pulsar : hint_range(0.0, 1.0) = 0.0;      // o tronco engrossa e afina
uniform float brancura : hint_range(0.0, 1.0) = 0.5;    // quanto do miolo e nucleo

uniform vec3 cor_borda : source_color = vec3(0.27, 0.55, 1.0);
uniform vec3 cor_manto : source_color = vec3(0.58, 0.85, 1.0);
uniform vec3 cor_nucleo : source_color = vec3(1.0);

// ---- o instante ----
uniform float tempo = 0.0;
uniform float semente = 0.0;
/// 1 = ninguem alimenta mais: a ponta de tras nao tem mao, afina em rabo.
uniform float solto : hint_range(0.0, 1.0) = 0.0;

/// Com que pressa a textura corre da mao pra ponta, em px/s. E o que le como "isto esta saindo".
const float FLUXO = 280.0;

// O SORTEIO DO RETICULADO E EM INTEIROS, e nao o `fract(sin(...))` de sempre. Medido no laboratorio: o
// sorteio em ponto flutuante tem pontos em que o produto cai a um ulp de um inteiro, e ali DUAS
// celulas vizinhas leem o mesmo canto com arredondamentos diferentes (0,9999 numa, 0,0000 na outra).
// O ruido rasga numa linha reta -- saia um DEGRAU vertical no meio do feixe. Conta de inteiro nao
// arredonda: o canto vale o mesmo pra quem quer que pergunte.
float h21(vec2 p) {
	uvec2 q = uvec2(ivec2(floor(p)));
	uint n = q.x * 1597334673u ^ q.y * 3812015801u;
	n = (n ^ (n >> 15u)) * 2246822519u;
	n = (n ^ (n >> 13u)) * 3266489917u;
	n ^= n >> 16u;
	return float(n >> 8u) * (1.0 / 16777216.0);
}

float h11(float x) { return h21(vec2(x, 71.0)); }

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
	for (int i = 0; i < 3; i++) {
		s += a * ruido(p);
		p = p * 2.03 + vec2(17.1, 9.3);
		a *= 0.5;
	}
	return s / 0.875;
}

float smin(float a, float b, float k) {
	float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
	return mix(b, a, h) - k * h * (1.0 - h);
}

////// Cristas pontudas a partir do ruido liso: e a diferenca entre "nuvem" e "lingua de fogo".
float crista(vec2 p) {
	return 1.0 - abs(ruido(p) * 2.0 - 1.0);
}

/// Distancia EXATA (com sinal) a um triangulo isosceles deitado: apice em (+a, 0), base em x = -b com
/// meia altura h. Exata e nao "o maior de tres planos": com os planos a distancia fora do apice
/// continua crescendo em cunha, e o halo da seta saia em ESTRELA.
float seta(vec2 p, float a, float b, float h) {
	vec2 q = vec2(h, a + b);
	vec2 s = vec2(abs(p.y), a - p.x);                   // o apice na origem, a base em s.y = a + b
	vec2 e1 = s - q * clamp(dot(s, q) / dot(q, q), 0.0, 1.0);
	vec2 e2 = s - q * vec2(clamp(s.x / q.x, 0.0, 1.0), 1.0);
	vec2 d = min(vec2(dot(e1, e1), -(s.x * q.y - s.y * q.x)), vec2(dot(e2, e2), -(s.y - q.y)));
	return -sqrt(d.x) * sign(d.y);
}

void fragment() {
	float u = UV.x;
	float v = UV.y;
	float L = comprimento;
	float corre = tempo * FLUXO;
	float grao = max(raio, 2.5);                        // a escala dos detalhes
	float lado = v < 0.0 ? -1.0 : 1.0;

	// as duas bolas respiram; a da boca nasce inchada (o clarao do disparo) e assenta
	float nasce = exp(-tempo * 9.0);
	float rc = raio_cabeca * (1.0 + 0.04 * sin(tempo * 37.0 + semente * 3.0));
	float rb = raio_boca * (1.0 - solto)
			 * (1.0 + 0.06 * sin(tempo * 43.0 + semente) + 0.05 * sin(tempo * 71.0) + 0.40 * nasce);

	float xc = L - rc;                                  // o centro da cabeca
	float xb = rb;                                      // o centro da boca: a bola ENCOSTA na mao, nao a engole

	// ================= o PERFIL: a meia largura ao longo do feixe =================
	float n_borda = ruido(vec2((u - corre * 0.75) / (grao * 2.4), semente * 7.0 + lado * 3.7));
	// barrigas que viajam da mao pra ponta: e o que faz o tronco parecer um JATO e nao um cano
	float barriga = sin((u - corre * 0.9) / (grao * 2.3) + semente * 2.0);
	float r = raio * (1.0 + 0.10 * (n_borda - 0.5) + 0.04 * barriga);

	// pulsa: engrossa e afina (o Boom Wave do original troca de espessura a cada quadro)
	r *= 1.0 - pulsar * 0.55 * (0.5 + 0.5 * sin(tempo * 23.0 + semente * 5.0));

	// serrilha: labaredas curtas deitadas pra tras ao longo de toda a casca (o Masenko). O `abs(v)` na
	// coordenada e o que as DEITA: quanto mais longe do eixo, mais de tras vem a amostra.
	if (serrilha > 0.001) {
		float x = (u - corre * 0.30 + clamp(abs(v) - raio, 0.0, raio * 1.5) * 1.3) / (grao * 0.95);
		float dente = crista(vec2(x, lado * 9.0 + semente));
		float miudo = crista(vec2(x * 2.7, lado * 4.0 + semente + 6.0));
		r += raio * serrilha * (0.50 * dente * dente + 0.20 * miudo);
	}

	// fiapos: linguas esparsas e compridas (o Kamehameha 5)
	if (fiapos > 0.001) {
		float x = (u - corre * 0.5 + clamp(abs(v) - raio, 0.0, alcance) * 1.6) / (grao * 1.2);
		float n = ruido(vec2(x, 40.0 + lado * 5.0 + semente));
		r += (alcance - raio + raio * 0.5) * fiapos * pow(clamp((n - 0.58) / 0.42, 0.0, 1.0), 1.5);
	}

	// solto: sem mao atras, a cauda afina em rabo de cometa
	r *= mix(1.0, sqrt(smoothstep(0.0, raio * 6.0 + 8.0, u)), solto);

	// A GOTA: da bola de cada ponta o perfil DESCE ate o tronco. Sem `chama` a descida e curta (um
	// pescoco); com ela e comprida e rasgada -- a gota de fogo de quase toda folha do original.
	// O rasgo comeca em ZERO no centro da bola e so cresce pra longe dele: no centro a gota tem que
	// ter exatamente o raio da bola, ou as duas pecas se encontram num degrau.
	float queda = mix(1.0, 1.6, chama);
	float w = r;
	{
		float tc = clamp((xc - u) / (rc * mix(0.9, 3.0, chama)), 0.0, 1.0);
		float x = ((xc - u) + clamp(abs(v) - r, 0.0, rc) * 0.9) / (grao * 1.3) - tempo * 8.0;
		float rasgo = crista(vec2(x, lado * 3.0 + semente)) * smoothstep(0.0, 0.25, tc) * (1.0 - tc * tc);
		w = max(w, r + max(rc - r, 0.0) * pow(1.0 - tc, queda) * (1.0 + chama * 1.9 * (rasgo - 0.20)));
	}
	if (rb > 0.01) {
		float tb = clamp((u - xb) / (rb * mix(0.9, 3.0, chama)), 0.0, 1.0);
		float x = ((u - xb) - clamp(abs(v) - r, 0.0, rb) * 0.9) / (grao * 1.3) - tempo * 8.0;
		float rasgo = crista(vec2(x, lado * 5.0 + semente + 2.0)) * smoothstep(0.0, 0.25, tb) * (1.0 - tb * tb);
		w = max(w, r + max(rb - r, 0.0) * pow(1.0 - tb, queda) * (1.0 + chama * 1.9 * (rasgo - 0.20)));
	}

	// o tronco so existe ENTRE os dois centros; pra fora deles quem desenha e a bola de cada ponta
	float ini = rb > 0.01 ? xb : 0.0;
	float d = max(abs(v) - w, max(u - xc, ini - u));
	float d_eixo = max(-w, max(u - xc, ini - u));       // a MESMA forma, medida no eixo: a profundidade maxima daqui

	// ================= a CABECA =================
	vec2 pc = vec2(u - xc, v);
	float d_cab = length(pc) - rc;
	if (ponta > 0.001)
		d_cab = mix(d_cab, seta(pc, rc, rc * 0.55, rc), ponta);
	if (coroa > 0.001) {
		// estouro: espinhos abrindo pra frente, cada um com o seu comprimento, piscando
		float ang = atan(pc.y, pc.x);
		float quadro = floor(tempo * 11.0);
		float k = ang / 0.36;
		float espinho = pow(0.5 + 0.5 * cos(fract(k) * 6.2831853), 2.4);
		float alt = h11(floor(k) * 3.0 + quadro * 2.0 + floor(semente * 13.0));
		float cs = pc.x / max(length(pc), 0.0001);
		float pra_frente = smoothstep(-0.50, 0.40, cs);
		float R = rc * (0.40 + 0.60 * espinho * (0.45 + 0.55 * alt) * pra_frente + 0.22 * (1.0 - pra_frente));
		// E UM ESPINHO FIXO NO EIXO, do tamanho exato do raio da cabeca. Os sorteados nunca passam dele (o
		// teto acima e 1,0), e ele garante que a PONTA do estouro e a ponta do feixe: a frente da cabeca e
		// regra do servidor, e com espinhos de 0,8 a 1,35 a bancada mediu a ponta de -2 a +7 px em volta dela.
		R = max(R, rc * pow(max(cs, 0.0), 7.0));
		d_cab = mix(d_cab, length(pc) - R, coroa);
	}
	float liga = max(raio * 0.5, 1.0);
	d = smin(d, d_cab, liga);
	d_eixo = smin(d_eixo, abs(u - xc) - rc, liga);

	// ================= a BOCA =================
	if (rb > 0.01) {
		d = smin(d, max(length(vec2(u - xb, v)) - rb, -u), liga);
		d_eixo = smin(d_eixo, max(abs(u - xb) - rb, -u), liga);
	}

	// ================= a COR =================
	// `fundo` = quao fundo este pixel esta, como FRACAO da profundidade do eixo neste mesmo ponto do
	// feixe: 0 na casca, 1 no eixo. Dividir pela profundidade LOCAL e o que faz o nucleo engordar
	// dentro das bolas e afinar no tronco sem emenda -- as tres faixas acompanham a silhueta.
	// O piso do divisor guarda a ponta: la a profundidade vai a zero e o nucleo viraria um espeto.
	float piso = max(grao * 0.5, 0.55 * rc * smoothstep(xc - rc, xc, u));
	float fundo = clamp(d / min(d_eixo, -piso), 0.0, 1.0);

	vec2 pv = vec2((u - corre) / (grao * 6.0), v / (grao * 1.25));
	float veio = fbm(pv + vec2(semente * 3.1, semente));
	// riscos compridos e finos, que e o que le como FLUXO
	float risco = ruido(vec2((u - corre * 1.25) / (grao * 7.0), v / (grao * 0.32) + semente * 5.0));

	// nas bolas o nucleo e maior: sao o ponto mais quente do feixe
	float nas_bolas = max(smoothstep(xc - rc * 2.0, xc - rc * 0.3, u), 1.0 - smoothstep(xb + rb * 0.3, xb + rb * 2.0, u));
	float bojo = clamp((max(rc, rb) - raio) / max(max(rc, rb), 0.001), 0.0, 1.0);
	float lim = mix(0.88, 0.26, brancura) - 0.20 * nas_bolas * bojo * brancura + 0.16 * (veio - 0.5);
	float nucleo = smoothstep(lim, lim + 0.16, fundo);
	float manto = smoothstep(0.03, 0.30, fundo);

	vec3 rgb = mix(cor_borda, cor_manto, manto);
	rgb *= 0.80 + 0.42 * risco;
	if (granulado > 0.001) {
		// faixas ao comprido, de duas grossuras (o Kamehameha 6, o Galick Ho)
		float faixa = ruido(vec2(v / (grao * 0.36) + semente, (u - corre * 0.5) / (grao * 8.0)));
		float fina = ruido(vec2(v / (grao * 0.15) + semente * 2.0, (u - corre * 0.9) / (grao * 3.0)));
		rgb *= 1.0 + granulado * (0.50 * (faixa - 0.5) + 0.30 * (fina - 0.5));
		nucleo *= 1.0 - granulado * 0.35 * smoothstep(0.45, 0.75, fina);
	}
	rgb = mix(rgb, cor_nucleo, nucleo);

	float aa = max(fwidth(d), 0.0001);
	float dentro = smoothstep(aa, -aa, d);

	// ================= o HALO =================
	float alc_halo = alcance_do_halo;
	float h = clamp(1.0 - max(d, 0.0) / alc_halo, 0.0, 1.0);
	float halo = h * h * h * (0.70 + 0.35 * veio);
	// O CLARAO COMECA NA MAO, E NAO ATRAS DELA. A mao fica na frente do corpo de quem atira; um halo que
	// vazasse pra tras pintaria luz POR CIMA do boneco -- e "nenhum pixel de feixe entra no quadro do
	// boneco" e regra medida (`--diagboca`), nascida de uma foto do dono com o raio saindo da cabeca.
	halo *= mix(smoothstep(0.0, alc_halo * 0.5, u), 1.0, solto);
	vec3 rgb_halo = mix(cor_borda, cor_manto, 0.35) * halo;
	float a_halo = halo * 0.30;

	vec3 saida = rgb * dentro + rgb_halo * (1.0 - dentro);
	float alfa = dentro + a_halo * (1.0 - dentro);

	// ================= os ENFEITES do tronco =================
	float janela = smoothstep(xb + rb * 0.4, xb + rb * 0.4 + grao * 3.0, u)
				 * (1.0 - smoothstep(xc - rc * 1.2, xc + rc * 0.2, u));

	// fitas em espiral (dao a volta: metade passa pela frente, metade por tras)
	if (espiral > 0.001) {
		float amp = max(alcance, raio * 1.9);
		for (int k = 0; k < 2; k++) {
			float fase = (u - corre * 0.55) / (grao * 2.4) + float(k) * 3.14159265 + semente;
			float y = sin(fase) * amp;
			float frente = cos(fase);
			float esp = grao * (0.30 + 0.16 * frente);
			float df = abs(v - y) - esp;
			float f = smoothstep(1.0, -1.0, df / max(fwidth(df), 0.0001) * 0.5) * janela * espiral;
			float visivel = mix(1.0 - dentro, 1.0, step(0.0, frente));
			vec3 cf = mix(cor_borda, cor_nucleo, 0.25 + 0.55 * max(frente, 0.0));
			saida = mix(saida, cf, f * visivel);
			alfa = max(alfa, f * visivel);
		}
	}

	// aneis: elipses de pe em volta do tronco, correndo pra frente (o Makkankosappo)
	if (aneis > 0.001) {
		float passo = 34.0;
		float x = mod(u - corre * 0.35, passo) - passo * 0.5;
		float ry = max(alcance, raio * 2.0);
		float rx = ry * 0.36;
		float e = length(vec2(x / rx, v / ry)) - 1.0;
		float de = abs(e) * min(rx, ry) - grao * 0.22;
		float f = smoothstep(1.0, -1.0, de / max(fwidth(de), 0.0001) * 0.5) * janela * aneis;
		// a metade de tras do anel passa por baixo do tronco
		float visivel = mix(1.0 - dentro, 1.0, step(0.0, x));
		saida = mix(saida, mix(cor_borda, cor_nucleo, 0.35), f * visivel);
		alfa = max(alfa, f * visivel);
	}

	// descargas rastejando pela casca (o Static Beam)
	if (eletrico > 0.001) {
		float quadro = floor(tempo * 14.0);
		float amp = max(alcance, raio * 1.8);
		float luz = 0.0;
		for (int k = 0; k < 3; k++) {
			float sk = floor(semente * 13.0) + float(k) * 31.0 + quadro * 7.0;
			float y = (ruido(vec2(u / (grao * 1.7), sk)) - 0.5) * amp * 2.2
					+ (ruido(vec2(u / (grao * 0.55), sk + 9.0)) - 0.5) * amp * 0.6;
			float onde = h21(vec2(sk, 1.0)) * L;
			float trecho = smoothstep(0.0, grao * 2.0, u - onde) * (1.0 - smoothstep(grao * 9.0, grao * 13.0, u - onde));
			float dl = abs(v - y);
			luz += trecho * ((1.0 - smoothstep(grao * 0.05, grao * 0.26, dl)) + 0.30 * (1.0 - smoothstep(0.0, grao * 1.1, dl)));
		}
		luz = clamp(luz, 0.0, 1.0) * eletrico * smoothstep(0.0, grao * 2.0, u) * (1.0 - smoothstep(L - rc, L + rc, u));
		saida = mix(saida, mix(cor_manto, cor_nucleo, 0.7), luz);
		alfa = max(alfa, luz);
	}

	COLOR = vec4(saida, clamp(alfa, 0.0, 1.0));
}
