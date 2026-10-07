shader_type canvas_item;

// `unshaded` e `blend_premul_alpha` pelos mesmos dois motivos do `FeixeDeKi.gdshader`: luz propria, e
// miolo que COBRE com franja que SOMA no mesmo quad.
render_mode blend_premul_alpha, unshaded;

// ============================================================================
// O PONTO ONDE DOIS FEIXES SE ENCONTRAM -- a ESTRELA do embate.
//
// O original nao tem nada aqui: o `struggle` do `objects.dm:287` so troca o quadro da cabeca. O port
// desenhava menos ainda -- duas cabecas encostadas e um anel por segundo (o `Baque`).
//
// ============================ ESTA E A SEGUNDA VERSAO, E A PRIMEIRA O DONO REPROVOU ============================
// A primeira era uma bola de ruido: casca fervendo, agulhas finas, aneis e descargas em volta. O dono
// viu no jogo e disse: *"To achando a colisao mt feia sendo sincero, queria q vc refizesse"* -- e mandou
// o que queria: o choque do Kamehameha contra o Galick Ho, no anime e num desenho de fa.
//
// O que aquelas duas imagens tem, e a bola de ruido nao tinha:
//   * e uma ESTRELA DE PONTAS, de borda DURA -- desenho de celula, e nao fumaca;
//   * o miolo e BRANCO e GRANDE, e a cor aparece nas PONTAS;
//   * cada METADE tem a cor de um lutador (a de cima rosa, a de baixo azul);
//   * as pontas compridas saem pros LADOS do eixo dos feixes -- o choque e um disco visto de quina;
//   * e ela e ENORME perto dos feixes: e a maior coisa da tela.
//
// Entao aqui nao ha ruido nenhum. A forma e uma coroa de dentes triangulares em tres tamanhos, com a
// altura de cada dente SORTEADA -- e o sorteio troca DOZE vezes por segundo, em degrau, como animacao
// feita a mao (cada desenho fica dois quadros na tela). Tres copias da mesma estrela, uma dentro da
// outra, dao as tres faixas: cor, cor clara e branco.
// ====================================================================================================================
//
// O quad e desenhado pelo `ChoqueDeKi.cs`, com UV em PIXELS a partir do ponto de encontro e `+x`
// apontando do feixe A pro feixe B.
// ============================================================================

/// O CORPO E UMA LENTE: uma elipse deitada ATRAVESSADA no eixo dos feixes -- o disco do choque visto de
/// quina, que e como o anime o desenha. `raio` e o semi-eixo de TRAVES (pouco mais que o dobro da cabeca
/// do feixe mais largo, `ChoqueDeKi.CorpoPara`: a estrela tapa o lugar onde eles se encontram) e
/// `raio_no_eixo` o semi-eixo AO LONGO dos feixes (a cabeca deles e pouco mais). Em px.
uniform float raio = 30.0;
uniform float raio_no_eixo = 18.0;

/// A UNIDADE DAS PONTAS, em px: a mais comprida passa 5,55 delas do corpo, e o risco mais comprido 6.
/// Sao esses dois tetos (e o do halo, la embaixo) que dimensionam o quad -- `ChoqueDeKi.MeioQuadro`.
uniform float ponta = 15.0;

/// A cor de quem vem de `-x` e a de quem vem de `+x`. Numa guarda (um feixe so) as duas sao iguais.
uniform vec3 cor_a : source_color = vec3(0.3, 0.6, 1.0);
uniform vec3 cor_b : source_color = vec3(1.0, 0.4, 0.8);

uniform float tempo = 0.0;
uniform float semente = 0.0;

/// 0 = apagado, 1 = cheio. Sobe quando o embate comeca e desce quando acaba.
uniform float forca : hint_range(0.0, 1.0) = 1.0;

/// Sobe pra 1 a cada `Baque` do servidor e desce sozinho: a estrela da um tranco pra fora.
uniform float tranco : hint_range(0.0, 1.0) = 0.0;

/// Quantos desenhos por segundo. Doze e "animar em dois" a 24 quadros -- a cadencia do anime.
const float DESENHOS_POR_SEGUNDO = 12.0;

// O sorteio em inteiros -- ver a nota no `FeixeDeKi.gdshader`: o de ponto flutuante rasga o desenho.
float h21(vec2 p) {
	uvec2 q = uvec2(ivec2(floor(p)));
	uint n = q.x * 1597334673u ^ q.y * 3812015801u;
	n = (n ^ (n >> 15u)) * 2246822519u;
	n = (n ^ (n >> 13u)) * 3266489917u;
	n ^= n >> 16u;
	return float(n >> 8u) * (1.0 / 16777216.0);
}

/// UMA COROA DE DENTES: `n` casas em volta do centro, um dente triangular em cada, com a altura e a
/// posicao dentro da casa sorteadas por `sorteio`. Devolve a altura do dente neste angulo, de 0 a 1.
///
/// O dente nunca encosta na borda da casa (o centro fica entre 0,32 e 0,68 e a meia largura nao passa
/// de 0,30): um dente cortado pela borda sairia com um lado RETO pro centro -- uma lasca, e nao uma ponta.
float coroa(float ang, float n, float largura, float sorteio) {
	float k = (ang + PI) / TAU * n;
	float casa = mod(floor(k), n);
	float alt = h21(vec2(casa, sorteio));
	float centro = 0.32 + 0.36 * h21(vec2(casa, sorteio + 7.0));
	float dente = max(1.0 - abs(fract(k) - centro) / (largura * 0.5), 0.0);
	// a ponta afina mais que uma reta: lados CONCAVOS, que e como se desenha uma estrela de impacto
	return pow(dente, 1.6) * (0.30 + 0.70 * alt);
}

void fragment() {
	vec2 p = UV;
	float r = length(p);
	float ang = atan(p.y, p.x);
	// quanto este rumo olha PRO LADO (1) e nao pro eixo dos feixes (0): e pra la que o choque escapa
	float de_lado = abs(sin(ang));

	// o desenho da vez: muda em DEGRAU, e gira um pouco a cada troca pra nenhum dente parar no lugar
	float desenho = floor(tempo * DESENHOS_POR_SEGUNDO) + floor(semente * 37.0);
	float giro = h21(vec2(desenho, 3.0)) * TAU;

	// ---- o CORPO: a lente, em coordenada polar ----
	float corpo = raio_no_eixo * raio / length(vec2(raio * cos(ang), raio_no_eixo * sin(ang)));

	// ---- as PONTAS: tres coroas somadas, as compridas so pros lados. De 0 a 5,55 (3,6 + 1,5 + 0,45). ----
	// As compridas sao as mais FINAS (um terco da casa delas): e a agulha comprida e magra que faz a
	// estrela ler como estouro, e nao como balao de quadrinho.
	float compridas = coroa(ang + giro, 13.0, 0.36, desenho);
	float medias = coroa(ang + giro * 1.7, 23.0, 0.52, desenho + 31.0);
	float curtas = coroa(ang + giro * 2.3, 41.0, 0.60, desenho + 67.0);
	float pros_lados = 0.20 + 0.80 * pow(de_lado, 1.3);
	float pontas = pros_lados * (3.60 * compridas + 1.50 * medias) + 0.45 * curtas;

	// o tamanho: entra crescendo, bate com o pulso e leva o tranco do servidor. O teto disto e 1,33.
	float vivo = (0.30 + 0.70 * forca) * (1.0 + 0.05 * sin(tempo * 41.0 + semente) + 0.28 * tranco);

	// ---- as tres faixas: a MESMA estrela tres vezes, uma dentro da outra ----
	// As de dentro tem o corpo um pouco menor e as pontas BEM mais curtas: a ponta comprida comeca branca
	// no pe, clareia no meio e so e cor pura no ultimo terco. E assim nas duas imagens -- o branco e uma
	// massa no centro e a cor mora nas pontas, e nao numa casquinha em volta de uma estrela toda branca.
	float R_fora = vivo * (corpo + ponta * pontas);
	float R_meio = vivo * (corpo * 0.84 + ponta * pontas * 0.62);
	float R_miolo = vivo * (corpo * 0.66 + ponta * pontas * 0.30);

	float aa = max(fwidth(r), 0.0001);
	float fora = 1.0 - smoothstep(R_fora - aa, R_fora + aa, r);
	float meio = 1.0 - smoothstep(R_meio - aa, R_meio + aa, r);
	float miolo = 1.0 - smoothstep(R_miolo - aa, R_miolo + aa, r);

	// ---- de quem e cada metade ----
	float lado_b = smoothstep(-raio_no_eixo * 0.25, raio_no_eixo * 0.25, p.x);
	vec3 cor = mix(cor_a, cor_b, lado_b);

	vec3 rgb = mix(cor, mix(cor, vec3(1.0), 0.60), meio);
	rgb = mix(rgb, vec3(1.0), miolo);

	// ---- os RISCOS: tracos finos e compridos, alem das pontas. O mais comprido passa 6 `ponta` do corpo. ----
	float riscos = 0.0;
	{
		float n = 34.0;
		float k = (ang + giro * 0.6 + PI) / TAU * n;
		float casa = mod(floor(k), n);
		float sorte = h21(vec2(casa, desenho + 113.0));
		float fino = max(1.0 - abs(fract(k) - 0.5) / 0.07, 0.0);
		float comp = vivo * (corpo + ponta * (2.0 + 4.0 * h21(vec2(casa, desenho + 151.0))) * (0.25 + 0.75 * de_lado));
		riscos = step(0.50, sorte) * fino * smoothstep(vivo * corpo * 0.8, vivo * corpo * 1.2, r)
			   * (1.0 - smoothstep(comp * 0.45, comp, r));
	}

	// ---- o clarao: um halo liso e REDONDO em volta, de alcance finito (1,5 do maior semi-eixo + 3 pontas) ----
	float alc = vivo * (max(raio, raio_no_eixo) * 1.5 + ponta * 3.0);
	float hh = clamp(1.0 - r / alc, 0.0, 1.0);
	float halo = hh * hh * hh;

	// ---- junta: a estrela cobre, o resto soma ----
	vec3 soma = cor * halo * 0.80 + mix(cor, vec3(1.0), 0.55) * riscos * 0.85;
	float cobre = max(halo * 0.30, riscos * 0.45);

	vec3 saida = rgb * fora + soma * (1.0 - fora);
	float alfa = fora + cobre * (1.0 - fora);

	COLOR = vec4(saida, clamp(alfa, 0.0, 1.0)) * forca;
}
