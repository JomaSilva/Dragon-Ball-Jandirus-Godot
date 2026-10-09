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
// ============================ O DESENHO GERAL, E DE ONDE ELE VEIO ============================
// Depois de ver os raios no jogo, o dono mandou UMA imagem (um feixe azul de desenho animado) e
// descreveu o que queria de TODO raio: *"ele ser maior quando sai da mao do personagem, ai ele afina
// ate chega na cabeca do beam onde cresce dnv, e esses efeitos em volta e q parece uma onda/fogo e
// bem legal tb, e faca elas se moverem em volta do beam"*. O que aquela imagem tem, peca por peca:
//
//   * DUAS EXPLOSOES, uma na mao e uma na cabeca: um miolo branco grande com um LEQUE DE LABAREDAS
//     pontudas saindo do centro dele. O da mao abre pra frente (a energia saindo); o da cabeca abre pra
//     tras (o rastro de quem voa). Ver `labareda` e o `leque`;
//   * o TRONCO entre elas e mais fino, e desce de cada explosao numa gota lisa;
//   * CORES CHAPADAS, de borda dura -- branco no miolo, a cor clara, e uma cor FORTE por fora --, e nao
//     um degrade. Ver `cor_fundo`;
//   * FITAS largas que dao a volta no tronco, como uma onda enrolada nele, e que ANDAM. Ver `onda`.
//
// E desenho de celula: por isso as labaredas sao DENTES (triangulos de altura sorteada) e nao ruido,
// e o leque anda em degraus, doze por segundo, como a estrela do choque.
// ==============================================================================================
//
// NUMA DISPUTA O FEIXE NAO MUDA. Este shader ja teve um `embate` que abria o tronco em cone ate o
// encontro, inchava a ponta e punha a mao em chamas. O dono viu no jogo e dispensou: *"N precisa fazer
// a ponta do beam ficar maior na colisao, so a estrela no centro do choque basta, ja fica legal"*.
// Quem desenha o choque e a estrela (`ChoqueDeKi.gdshader`), por cima das duas pontas.
//
// AS MEDIDAS VEM DO CORE E SAO REGRA: `raio` e `ArteDeProjetil.MeiaEspessuraDoTronco` e a ponta da
// cabeca cai em `ArteDeProjetil.FrenteDaCabeca` a frente da posicao do tiro. O servidor corta,
// cruza e encosta por esses dois numeros -- o que se ve e o que acerta. As labaredas e as fitas sao
// FOGO EM VOLTA, como o halo: nao encostam em ninguem, nao passam da ponta e nao passam pra tras da mao.
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
uniform float labareda : hint_range(0.0, 1.0) = 1.0;    // a coroa de fogo da mao e a da cabeca
uniform float onda : hint_range(0.0, 1.0) = 1.0;        // as fitas que giram em volta do tronco
uniform float ponta : hint_range(0.0, 1.0) = 0.0;       // cabeca em seta/broca em vez de bola
uniform float coroa : hint_range(0.0, 1.0) = 0.0;       // cabeca em estouro de espinhos
uniform float serrilha : hint_range(0.0, 1.0) = 0.0;    // dentes na casca do tronco
uniform float granulado : hint_range(0.0, 1.0) = 0.0;   // faixas e grao por dentro
uniform float fiapos : hint_range(0.0, 1.0) = 0.0;      // linguas esparsas saindo do tronco
uniform float aneis : hint_range(0.0, 1.0) = 0.0;       // aneis correndo em volta
uniform float eletrico : hint_range(0.0, 1.0) = 0.0;    // descargas rastejando
uniform float pulsar : hint_range(0.0, 1.0) = 0.0;      // o tronco engrossa e afina
uniform float brancura : hint_range(0.0, 1.0) = 0.5;    // quanto do miolo e nucleo

/// A COR FORTE, a de fora: as labaredas compridas, as fitas e o fio que contorna o feixe. Nao e um dos
/// tres tons da folha -- e o MATIZ que a soma deles com o ki teria se nao houvesse teto (ver
/// `ArteDeKiNoCliente.Rampa.Fundo`, que e quem faz a conta e explica por que ela nao da pra fazer aqui).
uniform vec3 cor_fundo : source_color = vec3(0.07, 0.35, 1.0);
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

/// Quantos desenhos por segundo as labaredas tem. Doze e "animar em dois" a 24 quadros -- a cadencia de
/// desenho animado, a mesma da estrela do choque.
const float DESENHOS_POR_SEGUNDO = 12.0;

// ============================ O LEQUE DE LABAREDAS, EM NUMEROS ============================
// As labaredas de cada explosao sao ESPINHOS SAINDO DO CENTRO DELA, e nao dentes em fila ao longo da
// casca. A primeira versao era a fila (dentes deitados todos no mesmo angulo): saiu uma franja
// penteada. Na imagem do dono os espinhos de tras estao quase de pe e os da frente quase deitados no
// tronco -- todos apontam pra fora do MESMO ponto, que e o miolo branco. E um leque.

/// Quanto o leque e ESTICADO pro lado do tronco. Um leque redondo seria uma estrela; esticado, os
/// espinhos que apontam pro meio do feixe saem mais compridos e mais deitados que os de pe. Na imagem do
/// dono o espinho a 27 graus do eixo tem 2,2 vezes o comprimento do que esta de pe.
///
/// E O TETO: num raio curto cada leque estica so ate perto do meio do vao (ver `estica_de`).
const float ESTICA = 2.3;

/// A largura de um espinho, em radianos (medidos no espaco esticado). Sao DUAS fileiras: os grandes,
/// uns seis de cada lado do feixe, e os miudos que enchem os vaos entre eles.
///
/// JA FORAM 0,21 E 0,10, e na foto do jogo o leque saiu PELUDO -- uma franja de pelos em volta da bola.
/// Os da imagem do dono sao cacos largos (uns 17 graus cada), poucos, e e isso que le como explosao.
const float ESPINHO_GRANDE = 0.30;
const float ESPINHO_MIUDO = 0.14;

/// Os espinhos DE PE sao mais curtos que os que apontam pro tronco (este e o quanto), e os que estao
/// deitados EM CIMA dele encolhem ate zero -- senao correriam por meio feixe, colados na casca.
const float ESPINHO_DE_PE = 0.70;

/// De que angulo a que angulo o leque FECHA (95 a 135 graus do rumo do tronco). E o que garante que
/// nenhum espinho passa pra tras da mao nem alem da ponta: a 110 graus um espinho de 1,9 raio ainda
/// fica 0,35 raio a frente do plano da mao (o centro da explosao esta um raio a frente dele).
const float FECHA_DE = 1.66;
const float FECHA_ATE = 2.36;

/// Quantos espinhos grandes passam por um ponto por segundo. O leque VARRE: os espinhos nascem atras, de
/// pe, e deitam pro lado do tronco ate sumir nele -- na mao e a energia sendo soprada pra frente, na
/// cabeca e a juba de quem voa. Anda em degraus (uns 0,3 espinho por desenho).
const float VARRE = 3.5;

// AS QUATRO FAIXAS DO LEQUE, de fora pra dentro (forte, casca, manto, nucleo), em raios da explosao.
//
// CADA FAIXA E UMA MASSA CHEIA COM A BEIRADA EM ESPINHOS, e nao um punhado de agulhas saindo do centro.
// A versao das agulhas foi feita e fotografada: como a largura de um espinho e o ANGULO dele vezes a
// distancia ao centro, perto do miolo cada um tinha 1 px -- saiu um ourico. Na imagem do dono cada cor
// e uma mancha inteira, maior que a de dentro, e o que e pontudo e so o CONTORNO dela.
//
// `MASSA_DO_LEQUE` e ate onde a mancha de cada cor vai (a de fora passa da bola: e a explosao sendo
// MAIOR que o feixe, o "maior quando sai da mao" do pedido) e `ESPINHOS_DO_LEQUE` e quanto o espinho
// mais comprido sobe alem dela. As de dentro sao menores E de espinho mais curto: o espinho comprido e
// da cor forte, o meio dele e claro e so o pe e branco -- o mesmo truque da estrela do choque.
//
// `MASSA_SEM_FOGO` cabe DENTRO da bola: com `labareda` zero (e nas direcoes em que o leque esta
// fechado) o leque some atras do corpo do feixe e o desenho e so o corpo.
const vec4 MASSA_SEM_FOGO = vec4(0.80, 0.74, 0.66, 0.50);
const vec4 MASSA_DO_LEQUE = vec4(1.35, 1.10, 0.92, 0.62);
const vec4 ESPINHOS_DO_LEQUE = vec4(1.45, 0.95, 0.66, 0.52);
// ==========================================================================================

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

float smin(float a, float b, float k) {
	float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
	return mix(b, a, h) - k * h * (1.0 - h);
}

/// Cristas pontudas a partir do ruido liso: e a diferenca entre "nuvem" e "lingua de fogo".
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

/// UMA FILEIRA DE DENTES: um triangulo por casa (`k` anda uma unidade por casa), com a altura e o lugar
/// da ponta sorteados. Devolve a altura neste ponto, de 0 a 1. E a labareda de desenho animado: ponta
/// dura, cada uma de um tamanho.
float dentes(float k, float sorteio) {
	float casa = floor(k);
	float f = k - casa;
	float alt = 0.30 + 0.70 * h21(vec2(casa, sorteio));
	float pico = 0.30 + 0.40 * h21(vec2(casa, sorteio + 7.0));
	float tri = f < pico ? f / pico : (1.0 - f) / (1.0 - pico);
	// lados levemente concavos: a ponta afina mais que uma reta
	return pow(tri, 1.3) * alt;
}

/// O LEQUE DE LABAREDAS de uma das pontas, visto do centro da explosao dela. `p.x` cresce PRO MEIO DO
/// FEIXE (pra frente na mao, pra tras na cabeca) e `p.y` e a distancia ao eixo.
///
/// Devolve: x = a distancia ao centro, em px, no espaco esticado; y = o tamanho do espinho que passa por
/// este pixel, de 0 a 1; z = quanto o leque esta ABERTO nesta direcao.
vec3 leque(vec2 p, float estica, float gira, float sorteio) {
	vec2 q = vec2(p.x > 0.0 ? p.x / estica : p.x, p.y);
	float ang = atan(q.y, q.x);                         // 0 = deitado no tronco, PI/2 = de pe, PI = pra fora do feixe
	float aberto = 1.0 - smoothstep(FECHA_DE, FECHA_ATE, ang);
	float vies = smoothstep(0.0, 0.40, ang) * mix(1.0, ESPINHO_DE_PE, smoothstep(0.9, 1.7, ang));
	float espinho = max(dentes(ang / ESPINHO_GRANDE + gira, sorteio),
						0.45 * dentes(ang / ESPINHO_MIUDO + gira * 1.3, sorteio + 11.0));
	return vec3(length(q), espinho * vies * aberto, aberto);
}

/// As quatro faixas de um leque (ver `MASSA_DO_LEQUE`): negativo = dentro. `bola` e o raio da explosao.
vec4 faixas_do_leque(vec3 lq, float bola, float quanto) {
	vec4 massa = mix(MASSA_SEM_FOGO, MASSA_DO_LEQUE, quanto * lq.z);
	return vec4(lq.x) - bola * (massa + quanto * lq.y * ESPINHOS_DO_LEQUE);
}

/// QUANTO O LEQUE DE UMA EXPLOSAO ESTICA NESTE FEIXE: o `ESTICA` cheio quando ha tronco de sobra, e menos
/// num raio curto -- o bastante pra a massa dele chegar so ate perto do meio do vao entre as duas bolas.
/// Sem isto as massas das duas explosoes se encontravam e o raio curto virava uma pilula sem cintura.
float estica_de(float bola, float vao) {
	return clamp((0.42 * vao + 0.5 * bola) / (MASSA_DO_LEQUE.x * bola), 1.0, ESTICA);
}

/// So a MASSA de fora do leque, sem os espinhos. E dela que o halo sai: medido da ponta de cada espinho
/// ele desenhava um RAIO DE SOL borrado adiante de cada um -- e borrao e o contrario de desenho de celula.
float massa_do_leque(vec3 lq, float bola, float quanto) {
	return lq.x - bola * mix(MASSA_SEM_FOGO.x, MASSA_DO_LEQUE.x, quanto * lq.z);
}

/// Quanto deste pixel esta DENTRO de uma forma cuja fronteira e `x = 0` (negativo = dentro), com a
/// borda de um pixel. E a borda dura do desenho de celula, sem serrilhado.
float dentro_de(float x) {
	float aa = max(fwidth(x), 0.0001);
	return smoothstep(aa, -aa, x);
}

/// O mesmo, pra quatro formas de uma vez (as quatro faixas).
vec4 dentro_das(vec4 x) {
	vec4 aa = max(fwidth(x), vec4(0.0001));
	return smoothstep(aa, -aa, x);
}

void fragment() {
	float u = UV.x;
	float v = UV.y;
	float y = abs(v);
	float L = comprimento;
	float corre = tempo * FLUXO;
	float grao = max(raio, 2.5);                        // a escala dos detalhes
	float lado = v < 0.0 ? -1.0 : 1.0;
	// o relogio dos desenhos: anda em DEGRAU
	float desenho = floor(tempo * DESENHOS_POR_SEGUNDO) / DESENHOS_POR_SEGUNDO;

	// as duas bolas respiram; a da boca nasce inchada (o clarao do disparo) e assenta
	float nasce = exp(-tempo * 9.0);
	float rc = raio_cabeca * (1.0 + 0.04 * sin(tempo * 37.0 + semente * 3.0));
	float rb = raio_boca * (1.0 - solto)
			 * (1.0 + 0.06 * sin(tempo * 43.0 + semente) + 0.05 * sin(tempo * 71.0) + 0.40 * nasce);

	// A BOLA DA MAO, ASSENTADA, CABE NO RAIO. Ela encosta na mao (o centro fica um raio a frente dela), entao a
	// frente dela cai a DOIS raios da mao -- e num feixe mais curto que isso ela atravessava o plano da PONTA, que
	// e regra do servidor: alem dele esta quem o raio empurra, ou a cabeca do raio que vem de frente. E o caso do
	// feixe-parede, e nele nao e raro: o Final Flash a 4x tem 108 px de bola, e numa disputa a menos de quinze
	// tiles cada feixe tem menos que os 216 que ela pede. A `--diagembateki` (cena 3: dois deles a 14 tiles) mediu
	// na FOTO a tinta de cada cabeca de 7 a 20 px alem da ponta que o node anuncia -- uma cabeca de 22 a 38 px
	// dentro da outra, com as duas pontas anunciadas no mesmo ponto.
	//
	// ENCOLHE, e nao e cortada no plano: o corte deixaria uma parede reta na frente do feixe.
	// O CLARAO DO DISPARO FICA DE FORA (o `nasce`): ele e maior que o feixe de proposito e dura um decimo de
	// segundo -- a bola passa a caber conforme ele apaga.
	rb = mix(min(rb, L * 0.5), rb, nasce);

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
		float x = (u - corre * 0.30 + clamp(y - raio, 0.0, raio * 1.5) * 1.3) / (grao * 0.95);
		float dente = crista(vec2(x, lado * 9.0 + semente));
		float miudo = crista(vec2(x * 2.7, lado * 4.0 + semente + 6.0));
		r += raio * serrilha * (0.50 * dente * dente + 0.20 * miudo);
	}

	// fiapos: linguas esparsas e compridas (o Kamehameha 5)
	if (fiapos > 0.001) {
		float x = (u - corre * 0.5 + clamp(y - raio, 0.0, alcance) * 1.6) / (grao * 1.2);
		float n = ruido(vec2(x, 40.0 + lado * 5.0 + semente));
		r += (alcance - raio + raio * 0.5) * fiapos * pow(clamp((n - 0.58) / 0.42, 0.0, 1.0), 1.5);
	}

	// solto: sem mao atras, a cauda afina em rabo de cometa
	r *= mix(1.0, sqrt(smoothstep(0.0, raio * 6.0 + 8.0, u)), solto);

	// A GOTA: da bola de cada ponta o perfil DESCE ate o tronco, liso -- e o "afina" do pedido. A da mao e
	// mais comprida que a da cabeca: o feixe sai GROSSO e vai fechando, e so reabre em cima da ponta.
	//
	// AS DUAS CABEM NO VAO ENTRE AS BOLAS (45% e 30% dele, no maximo). Com o comprimento fixo em raios, um
	// raio curto -- o alvo a tres tiles -- era so gota em cima de gota: uma pilula branca, sem tronco
	// nenhum pra afinar. Foi a cena dos doze rumos do laboratorio (feixes de 86 px) que mostrou.
	float vao = max(xc - xb, 0.001);
	float w = r;
	{
		float tc = clamp((xc - u) / min(rc * 3.0, vao * 0.30), 0.0, 1.0);
		w = max(w, r + max(rc - r, 0.0) * pow(1.0 - tc, 1.6));
	}
	if (rb > 0.01) {
		float tb = clamp((u - xb) / min(rb * 4.5, vao * 0.45), 0.0, 1.0);
		w = max(w, r + max(rb - r, 0.0) * pow(1.0 - tb, 1.6));
	}

	// o tronco so existe ENTRE os dois centros; pra fora deles quem desenha e a bola de cada ponta
	float ini = rb > 0.01 ? xb : 0.0;
	float d = max(y - w, max(u - xc, ini - u));
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
		// E A EMENDA DELA NAO ESTUFA A PONTA. O minimo suave engorda a silhueta ate um quarto de `liga` onde as
		// duas formas quase coincidem, e num feixe curto a bola da mao chega junto da frente da cabeca: com os
		// 54 px de `liga` do Final Flash a 4x eram ate 13 px a mais, ALEM da ponta (a conta fecha com a foto em
		// 0,1 px). A emenda nunca e maior que o que falta pra bola da mao chegar ao plano da ponta -- e ali quem
		// desenha a frente e so a cabeca.
		float liga_da_boca = min(liga, max(L - 2.0 * rb, 0.001));
		d = smin(d, max(length(vec2(u - xb, v)) - rb, -u), liga_da_boca);
		d_eixo = smin(d_eixo, max(abs(u - xb) - rb, -u), liga_da_boca);
	}

	// ================= a COR do corpo: faixas CHAPADAS =================
	// `fundo_do_eixo` = a profundidade do eixo neste ponto do feixe. As faixas sao fracoes DELA, e por isso
	// acompanham a silhueta: o branco engorda dentro das bolas e afina no tronco sem emenda.
	// O piso guarda a ponta: la a profundidade vai a zero e o nucleo viraria um espeto.
	float piso = max(grao * 0.5, 0.55 * rc * smoothstep(xc - rc, xc, u));
	float fundo_do_eixo = -min(d_eixo, -piso);

	// nas bolas o nucleo e maior: sao o ponto mais quente do feixe
	float nas_bolas = max(smoothstep(xc - rc * 2.0, xc - rc * 0.3, u), 1.0 - smoothstep(xb + rb * 0.3, xb + rb * 2.0, u));
	float bojo = clamp((max(rc, rb) - raio) / max(max(rc, rb), 0.001), 0.0, 1.0);
	float lim = mix(0.88, 0.26, brancura) - 0.20 * nas_bolas * bojo * brancura;

	// o corpo, faixa por faixa: o fio forte por fora, a casca, o manto e o nucleo
	vec4 d_faixas = vec4(d) + vec4(0.0, 0.10, 0.24, lim) * fundo_do_eixo;

	// ================= as LABAREDAS: um LEQUE em cada ponta =================
	// O leque de cima e o de baixo sao sorteados separados (o `lado`), senao o feixe sairia espelhado no
	// proprio eixo.
	//
	// NENHUM DOS DOIS PASSA DE NENHUM DOS DOIS PLANOS -- o da mao (atras dele esta o boneco de quem atira) e
	// o da ponta (a frente da cabeca e regra do servidor, e alem dela esta quem o raio empurra). E CADA
	// LEQUE PRECISA OUVIR OS DOIS: o da cabeca abre PRA TRAS e num raio curto alcancava o boneco (o
	// laboratorio fotografou um espinho atravessando quem atirava); o da mao abre PRA FRENTE e num raio
	// curto passava da ponta (a `--diagartedeki`, familia 7, mediu 65 px de labareda alem dela num raio
	// de 40 px). O corte nos dois planos e a garantia; e pra o corte nao aparecer como uma parede reta, o
	// leque inteiro MURCHA ao chegar perto de cada um (`entre_os_planos`).
	float d_do_halo = d;
	if (labareda > 0.001) {
		float sorteio = floor(semente * 29.0) + (lado > 0.0 ? 0.0 : 53.0);
		float gira = desenho * VARRE;
		float fora_dos_planos = max(u - L, -u);

		// o da CABECA abre PRA TRAS
		float entre_os_planos = smoothstep(0.0, rc * 0.7, u) * smoothstep(0.0, rc * 0.7, L - u);
		vec3 lc = leque(vec2(xc - u, y), estica_de(rc, vao), gira, sorteio);
		d_faixas = min(d_faixas, max(faixas_do_leque(lc, rc, labareda * entre_os_planos), vec4(fora_dos_planos)));
		d_do_halo = min(d_do_halo, max(massa_do_leque(lc, rc, labareda * entre_os_planos), fora_dos_planos));

		// o da MAO abre PRA FRENTE
		if (rb > 0.01) {
			entre_os_planos = smoothstep(0.0, rb * 0.7, u) * smoothstep(0.0, rb * 0.7, L - u);
			vec3 lb = leque(vec2(u - xb, y), estica_de(rb, vao), gira, sorteio + 23.0);
			d_faixas = min(d_faixas, max(faixas_do_leque(lb, rb, labareda * entre_os_planos), vec4(fora_dos_planos)));
			d_do_halo = min(d_do_halo, max(massa_do_leque(lb, rb, labareda * entre_os_planos), fora_dos_planos));
		}
	}

	float corpo = dentro_de(d);
	vec4 faixas = dentro_das(d_faixas);
	float dentro = faixas.x;
	float nucleo = faixas.w;

	// LASCAS: uns poucos riscos compridos, de borda dura, da cor da casca entrando no branco. E o que le
	// como FLUXO num desenho que nao tem degrade.
	float risco = ruido(vec2((u - corre * 1.25) / (grao * 9.0), v / (grao * 0.55) + semente * 5.0));
	float lasca = smoothstep(0.74, 0.78, risco);

	vec3 rgb = mix(cor_fundo, cor_borda, faixas.y);
	rgb = mix(rgb, cor_manto, faixas.z);
	if (granulado > 0.001) {
		// faixas ao comprido, de duas grossuras (o Kamehameha 6, o Galick Ho)
		float faixa = ruido(vec2(v / (grao * 0.36) + semente, (u - corre * 0.5) / (grao * 8.0)));
		float fina = ruido(vec2(v / (grao * 0.15) + semente * 2.0, (u - corre * 0.9) / (grao * 3.0)));
		rgb *= 1.0 + granulado * (0.50 * (faixa - 0.5) + 0.30 * (fina - 0.5));
		nucleo *= 1.0 - granulado * 0.35 * smoothstep(0.45, 0.75, fina);
	}
	rgb = mix(rgb, mix(cor_nucleo, cor_borda, 0.8 * lasca), nucleo);

	// ================= o HALO =================
	float alc_halo = alcance_do_halo;
	float h = clamp(1.0 - max(d_do_halo, 0.0) / alc_halo, 0.0, 1.0);
	float halo = h * h * h * 0.85;
	// O CLARAO COMECA NA MAO, E NAO ATRAS DELA. A mao fica na frente do corpo de quem atira; um halo que
	// vazasse pra tras pintaria luz POR CIMA do boneco -- e "nenhum pixel de feixe entra no quadro do
	// boneco" e regra medida (`--diagboca`), nascida de uma foto do dono com o raio saindo da cabeca.
	halo *= mix(smoothstep(0.0, alc_halo * 0.5, u), 1.0, solto);
	vec3 rgb_halo = mix(cor_fundo, cor_manto, 0.45) * halo;
	float a_halo = halo * 0.30;

	vec3 saida = rgb * dentro + rgb_halo * (1.0 - dentro);
	float alfa = dentro + a_halo * (1.0 - dentro);

	// ================= os ENFEITES do tronco =================
	float janela = smoothstep(xb + rb * 0.4, xb + rb * 0.4 + grao * 3.0, u)
				 * (1.0 - smoothstep(xc - rc * 1.2, xc + rc * 0.2, u));

	// AS FITAS: duas faixas largas enroladas no tronco, como uma onda dando a volta nele -- e ANDANDO (o
	// dono: *"faca elas se moverem em volta do beam"*). Uma helice vista de lado e um seno; fazer o seno
	// correr pelo feixe e o mesmo que fazer a helice girar.
	//
	// A LARGURA E O QUE AS FAZ SEREM ONDA E NAO ARAME: cheia quando a fita passa pela FRENTE do tronco e
	// zero nas duas viradas, la em cima e la embaixo -- cada volta sai como um crescente de ponta fina. A
	// metade de tras e mais magra e some atras do feixe.
	//
	// E AS DUAS SAO DESIGUAIS de proposito (a segunda e mais baixa, mais magra, de passo mais curto e anda
	// noutra pressa): duas fitas iguais e defasadas de meia volta desenham uma escada de DNA.
	//
	// O PORTE DELAS NAO E PROPORCIONAL AO TRONCO. Proporcional foi a primeira conta, e o catalogo das
	// vinte artes mostrou os dois extremos: num raio de 13 px de meia espessura as fitas viravam lencois
	// de 30 px cobrindo o feixe, e num fio de 2 px viravam um poste de barbeiro mais grosso que ele.
	// Entao a fita acompanha o tronco ate um calibre medio e dai em diante cresce devagar; o quanto ela
	// se AFASTA da casca e o passo da helice tem piso e teto do mesmo jeito.
	if (onda > 0.001) {
		float fio = min(max(raio, 1.3), 5.0 + raio * 0.25);          // a meia largura de uma fita
		float afasta = clamp(raio * 2.0, 5.0, 11.0 + raio * 0.3);    // quanto ela passa da casca nas viradas
		float passo = max(fio, 4.0) * 3.6;                           // px por radiano de helice
		for (int k = 0; k < 2; k++) {
			float fk = float(k);
			float amp = max(alcance, raio + afasta) * (1.0 - 0.28 * fk);
			float fase = (u - corre * (0.30 + 0.09 * fk)) / (passo * (1.0 - 0.30 * fk)) + fk * 2.4 + semente;
			float frente = cos(fase);
			// EM GOLFADAS: a fita nao cobre o feixe inteiro, ela vem em trechos que correm com o fluxo. A
			// golfada (e a janela, junto das duas explosoes) entra na LARGURA e nao na transparencia: cada
			// trecho comeca e acaba em PONTA. Desbotando, sobrava um fantasma da fita por cima da explosao.
			float golfada = smoothstep(0.30, 0.48, ruido(vec2((u - corre * 0.55) / (passo * 5.5), fk * 7.0 + semente)));
			float grossa = fio * (1.15 - 0.45 * fk) * pow(abs(frente), 0.75) * mix(0.40, 1.0, step(0.0, frente))
						 * golfada * janela * onda;
			float f = dentro_de(abs(v - sin(fase) * amp) - grossa) * smoothstep(0.15, 0.8, grossa);
			float visivel = mix(1.0 - dentro, 1.0, step(0.0, frente));
			saida = mix(saida, cor_fundo * mix(0.78, 1.0, step(0.0, frente)), f * visivel);
			alfa = max(alfa, f * visivel);
		}

		// E AS FAISCAS: pontinhos quadrados soltos em volta, correndo com o fluxo (os que a imagem tem em
		// torno do tronco). Uma por casa do reticulado, e so numa casa em cada oito.
		float casa_px = max(grao * 1.4, 5.0);
		vec2 cel = vec2((u - corre * 0.42) / casa_px, v / casa_px);
		vec2 id = floor(cel);
		float sorte = h21(id + vec2(floor(semente * 17.0), 3.0));
		vec2 onde = vec2(h21(id + vec2(5.0, 11.0)), h21(id + vec2(13.0, 7.0))) * 0.6 + 0.2;
		vec2 q = abs(fract(cel) - onde) * casa_px;
		float tam = 0.5 + 0.6 * h21(id + vec2(2.0, 19.0));
		float ponto = dentro_de(max(q.x, q.y) - tam);
		float faixa_das_faiscas = smoothstep(raio * 1.2, raio * 1.7, y) * (1.0 - smoothstep(raio * 3.0, raio * 3.6, y));
		float faisca = ponto * step(0.875, sorte) * faixa_das_faiscas * janela * onda;
		saida = mix(saida, mix(cor_manto, cor_nucleo, 0.5), faisca);
		alfa = max(alfa, faisca);
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
		float visivel = mix(1.0 - corpo, 1.0, step(0.0, x));
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
			float yl = (ruido(vec2(u / (grao * 1.7), sk)) - 0.5) * amp * 2.2
					 + (ruido(vec2(u / (grao * 0.55), sk + 9.0)) - 0.5) * amp * 0.6;
			float onde = h21(vec2(sk, 1.0)) * L;
			float trecho = smoothstep(0.0, grao * 2.0, u - onde) * (1.0 - smoothstep(grao * 9.0, grao * 13.0, u - onde));
			float dl = abs(v - yl);
			luz += trecho * ((1.0 - smoothstep(grao * 0.05, grao * 0.26, dl)) + 0.30 * (1.0 - smoothstep(0.0, grao * 1.1, dl)));
		}
		luz = clamp(luz, 0.0, 1.0) * eletrico * smoothstep(0.0, grao * 2.0, u) * (1.0 - smoothstep(L - rc, L + rc, u));
		saida = mix(saida, mix(cor_manto, cor_nucleo, 0.7), luz);
		alfa = max(alfa, luz);
	}

	COLOR = vec4(saida, clamp(alfa, 0.0, 1.0));
}
