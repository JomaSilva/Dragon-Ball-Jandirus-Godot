shader_type canvas_item;
render_mode blend_premul_alpha, unshaded;

// ============================ A AURA DAS FORMAS DIVINAS, POR SHADER (v0.3.0, adendo 13 do dono) ============================
// O pedido comecou como *"pararmos de usar aura de sprite e comecar a usar aura de shaders do godot"*, pra todas as formas,
// e depois de ver as fotos o dono o recortou: *"acho q e melhor manter a aura de sprite pras formas mortais, e manter essa
// aura shaders pras formas divinas, e no blue kaioken seria a aura shaders do blue + aura sprite do kaioken por cima"*.
// Entao este arquivo desenha SO as tres chamas divinas -- as tres folhas que o `SpriteDeAura` deixou de ter --, e o
// Kaio-ken por cima do Blue e o sprite dele, desenhado depois deste quad no mesmo node (ver `Aura` e `CargaVisual`).
//
// AS REFERENCIAS DO DONO: o SSJ Deus, FOGO laranja e vermelho, arredondado; o Blue, um BRILHO ciano em volta do corpo; o
// Rose, violeta colado no corpo e labaredas de carmim fundo com a beirada acesa em vermelho. E as correcoes dele em cima
// das primeiras fotos: *"os pontinhos brancos subindo so seriam nas formas divinas"*, *"o rose e o god poderiam ser
// menores, estao mt grandes"*, *"o roxo do rose seguindo mais a silhueta do corpo"*, *"a aura deveria comecar do pe"* e
// *"veja como no blue kaioken o azul contorna a silhueta"*.
//
// TODA CHAMA CONTORNA O CORPO: o desenho mede a distancia ate a silhueta (`au_corpo`) e nao ate um oval. E toda chama
// COMECA NO PE: abaixo da linha dos pes ela some em ~2 px.
//
// TRES DESENHOS, pelo `estilo`:
//   0  O FOGO (o Deus): fogo de desenho animado em FAIXAS (vermelho, laranja, amarelo, quase branco), revirando e
//      subindo, ~6 px dos lados do corpo e ~12 acima da cabeca.
//   1  O BRILHO (o Blue e o Blue Evolution): macio, quase branco encostado no corpo, a cor da forma no meio, a beirada
//      ondulando em fiapos que sobem, listras de luz por dentro.
//   2  O ROSE: o violeta colado na silhueta, labaredas de carmim fundo em volta e a beirada delas acesa em vermelho.
//   3  (2026-11-02, o pedido 223) A LUZ DO BLUE: uma nuvem de luz em volta do corpo -- o clarao quase branco no peito e na
//      cabeca, o azul em volta, a beirada macia em lobulos acesa num contorno fino amarelado, e estrelinhas piscando.
//   4  (o pedido 223) O BLUE KAIO-KEN: a luz do Blue por dentro, e por fora uma chama VERMELHA grande de labaredas pontudas,
//      o miolo rosado e a beirada vermelha acesa, com faiscas vermelhas subindo. O sprite do Kaio-ken por cima saiu.
//   (2026-11-02, o pedido 223f) E A 3 VIROU A LUZ DE TODAS AS DIVINAS (o dono: *"Só as divinas"*): o SSG, a Golden Perfect e o Rose
//   a vestem, cada uma na sua paleta (`ChamaDeAura.PaletaDe`); a 0, a 1 e a 2 ficam pras fotos do antes.
// E nas tres primeiras, os PONTINHOS brancos subindo e piscando (no Rose, um ceu deles); nas duas da luz, as estrelinhas.
//
// O DONO SOBRE A 3 E A 4, com duas fotos de um Blue de outro jogo: *"Faça a aura do blue ter esse efeito, e no blue kaioken o
// efeito da segunda imagem, tire prints pra revisao, se ficar bom aplicamos em outras auras"*. A 1 (a chama do Blue de antes)
// fica no arquivo: e a comparacao das fotos, e a volta se ele preferir a de antes.
//
// AS MEDIDAS SAO EM PIXEL DE MUNDO do corpo (o quadro de 32 px; os pes na linha `pes_px` do quad). O quad de cada
// estilo e medido no `ChamaDeAura.cs` pra caber o desenho; o corpo grande (o gigante, o Oozaru) e escalado no NODE.
//
// O BRILHO EM VOLTA E CURTO de proposito: o halo largo que o `Personagem.gdshader` ja teve foi recusado pelo dono
// ("amarronzado por cima do verde do chao") -- cor de aura rala em cima da grama nao e nem uma nem outra.
// =====================================================================================================================

uniform float largura_px = 48.0;
uniform float altura_px = 62.0;
uniform float pes_px = 56.0;
uniform float semente = 0.0;

uniform int estilo = 0;
uniform vec4 cor_miolo : source_color = vec4(1.0, 0.96, 0.78, 1.0);
uniform vec4 cor_meio : source_color = vec4(1.0, 0.68, 0.18, 1.0);
uniform vec4 cor_borda : source_color = vec4(0.91, 0.20, 0.10, 1.0);
// o ARO: a beirada acesa das labaredas do Rose (as outras chamas nao usam)
uniform vec4 cor_aro : source_color = vec4(0.86, 0.06, 0.27, 1.0);
// (o pedido 223) o VERMELHO da chama do Blue Kaio-ken (o desenho 4): o do Kaio-ken do jogo (`Aura.CorDoKaioken`)
uniform vec4 cor_kaioken : source_color = vec4(1.0, 0.16, 0.10, 1.0);
// (o pedido 223g) o TAMANHO da luz (o desenho 3): 1 no Blue, no tamanho da foto do dono; as outras divinas menores (o dono:
// *"Diminuir"*). A luz inteira em escala a partir dos pes -- o vaso, as bolhas, a faixa e as estrelinhas.
uniform float tamanho_luz = 1.0;

// A PRESENCA: 1 e a aura comum; a carga pulsa abaixo e acima disso. Acima de 1 so o brilho cresce -- e pela METADE do
// que passa de 1: a cena de transformacao manda ate ~2,2, e a conta inteira lavaria as cores divinas ate o branco.
uniform float forca : hint_range(0.0, 2.0) = 1.0;

// O HASH COM A ENTRADA DOBRADA EM 1024: com coordenada grande o `fract` de um produto de milhoes perde a fracao, o
// hash vira degrau e o ruido sai em BLOCOS retos. O ruido repete a cada 1024 celulas, o que ninguem ve.
float au_hash(vec2 p) {
	p = mod(p, 1024.0);
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float au_ruido(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(au_hash(i), au_hash(i + vec2(1.0, 0.0)), f.x),
			   mix(au_hash(i + vec2(0.0, 1.0)), au_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float au_fbm(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 5; i++) {
		s += a * au_ruido(p);
		p *= 2.03;
		a *= 0.5;
	}
	return s;
}

// O RUIDO DE TRES OITAVAS: so as formas grandes -- a beirada de uma labareda arredondada (com cinco oitavas ela sai
// rasgada como papel queimado). Normalizado pra 0..1 como o de cinco.
float au_fbm3(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 3; i++) {
		s += a * au_ruido(p);
		p *= 2.03;
		a *= 0.5;
	}
	return s / 0.875;
}

vec3 au_rampa(float i) {
	return i < 0.5 ? mix(cor_borda.rgb, cor_meio.rgb, i * 2.0) : mix(cor_meio.rgb, cor_miolo.rgb, (i - 0.5) * 2.0);
}

// ============================ O CORPO PADRAO, EM DISTANCIA ============================
// A silhueta do quadro de 32 em pe (medida na folha `BaseTanMale`: cabeca de 10 px de largura dos 16 aos 26 px acima
// dos pes, ombros de 16 dos 6 aos 16, pernas de 11 ate o chao), com folga pro cabelo no alto. Devolve a distancia em
// px ate ela -- negativa dentro. E o que faz o fogo, o brilho e o violeta CONTORNAREM o corpo em vez de um oval.
float au_sd_elipse(vec2 p, vec2 r) {
	return (length(p / r) - 1.0) * min(r.x, r.y);
}

float au_sd_caixa(vec2 p, vec2 b, float raio) {
	vec2 q = abs(p) - b + raio;
	return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - raio;
}

float au_smin(float a, float b, float k) {
	float h = clamp(0.5 + 0.5 * (b - a) / k, 0.0, 1.0);
	return mix(b, a, h) - k * h * (1.0 - h);
}

float au_corpo(vec2 p) {
	float cabeca = au_sd_elipse(p - vec2(0.0, 22.0), vec2(6.5, 7.0));
	float tronco = au_sd_caixa(p - vec2(0.0, 11.5), vec2(8.0, 5.0), 3.5);
	float pernas = au_sd_caixa(p - vec2(0.0, 3.5), vec2(5.5, 4.0), 2.0);
	return au_smin(au_smin(cabeca, tronco, 4.0), pernas, 3.0);
}

// O ALCANCE CRESCE ACIMA DOS OMBROS: a chama sobe mais do que abre. `lado` vale nos lados e embaixo, `topo` acima da
// cabeca.
float au_alcance(float y, float lado, float topo) {
	return mix(lado, topo, smoothstep(14.0, 32.0, y));
}

// ============================ 0) O FOGO DO DEUS ============================
// Fogo de desenho animado contornando o corpo. A turbulencia e ESTICADA na vertical (os blocos do ruido viram linguas
// que sobem); a beirada sai do ruido de tres oitavas (labareda redonda) e o de cinco so desenha as FAIXAS de dentro --
// vermelho, laranja, amarelo, quase branco.
vec4 au_fogo(vec2 p, float t) {
	float dc = au_corpo(p);
	vec2 uv = vec2(p.x * 0.14, p.y * 0.060 - t * 1.40);
	float w = au_fbm3(uv * 1.5 + vec2(t * 0.25, semente));
	float n = au_fbm(uv + vec2(w * 1.3, w * 0.5));
	float nb = au_fbm3(uv + vec2(w * 1.3, w * 0.5));
	float alcance = au_alcance(p.y, 6.0, 12.0);
	// longe do corpo o ruido perde a forca: sem isso nascem ilhas de fogo soltas no ar
	float perto = 1.0 - smoothstep(alcance, alcance + 6.0, dc);
	float fb = 1.0 - dc / alcance + (nb - 0.5) * 1.00 * perto;
	float f = fb + (n - nb) * 0.60;
	float g = clamp(f, 0.0, 1.0) * 4.0;
	float faixa = (floor(g) + smoothstep(0.70, 1.0, fract(g))) / 4.0;
	float cobre = smoothstep(0.0, 0.06, fb);
	vec3 cor = au_rampa(clamp(0.18 + faixa * 0.95, 0.0, 1.0));
	// COMECA NO PE: abaixo da linha dos pes o fogo some em ~2 px, desfiado no ruido
	cobre *= smoothstep(-2.5, 0.5, p.y + (nb - 0.5) * 3.0);
	// LUZ: a cor entra cheia e o alfa (o quanto o fundo apaga) e menor -- a chama clareia o fundo em vez de virar chapa
	return vec4(cor * cobre, cobre * mix(0.70, 0.92, faixa));
}

// ============================ 1) A CHAMA DO BLUE ============================
// Contornando a silhueta, e NAO redonda (o dono: *"a aura do blue ta mt redondinha"*, com a foto do Evolution: *"ela tem
// o formato igual a do blue, a diferenca ta na cor"*): a beirada e de LABAREDAS irregulares, com chanfros, e acesa num
// ARO fino e claro (`cor_aro`); por dentro, quase branco encostado no corpo, a cor da forma no meio e o tom fundo perto
// da beirada; listras de luz correndo pra cima.
vec4 au_brilho(vec2 p, float t) {
	float dc = au_corpo(p);
	vec2 uv = vec2(p.x * 0.12, p.y * 0.055 - t * 1.10);
	float w = au_fbm3(uv * 1.4 + vec2(t * 0.20, semente));
	float n = au_fbm(uv + vec2(w * 1.2, w * 0.6));
	float alcance = au_alcance(p.y, 7.0, 12.0);
	// longe do corpo o ruido perde a forca: sem isso nascem ilhas soltas no ar
	float perto = 1.0 - smoothstep(alcance, alcance + 6.0, dc);
	// AS LABAREDAS: o ruido de cinco oitavas deixa a beirada chanfrada como a da foto, e nao um brilho redondo
	float fb = 1.0 - dc / alcance + (n - 0.5) * 1.50 * perto;
	float cobre = smoothstep(0.0, 0.04, fb);
	// O ARO: uma faixa fina e clara logo dentro da beirada
	float aro = 1.0 - smoothstep(0.02, 0.10, fb);
	float listra = au_fbm(vec2(p.x * 0.30 + semente, p.y * 0.035 - t * 0.7));
	float encostado = 1.0 - smoothstep(0.0, 3.0, dc);
	float i = clamp(fb * 1.05 * mix(0.85, 1.15, listra) + encostado * 0.35, 0.0, 1.0);
	vec3 cor = mix(au_rampa(i), cor_aro.rgb, aro);
	// o brilho claro em volta da beirada: curto
	float halo = exp(-max(-fb, 0.0) / 0.06) * (1.0 - cobre) * 0.40;
	// COMECA NO PE
	float chao = smoothstep(-2.5, 0.5, p.y + (n - 0.5) * 3.0);
	cobre *= chao;
	vec3 rgb = cor * cobre + cor_aro.rgb * halo * 0.60 * chao;
	return vec4(rgb, clamp(cobre * mix(0.62, 0.90, i) + halo * 0.20 * chao, 0.0, 1.0));
}

// ============================ 2) A CHAMA ROSE ============================
// O violeta colado na silhueta, com veios mais fundos; em volta, labaredas arredondadas e fumacentas de CARMIM fundo; e
// a beirada delas acesa num VERMELHO vivo (`cor_aro`).
vec4 au_rosa(vec2 p, float t) {
	float dc = au_corpo(p);
	vec2 uv = vec2(p.x * 0.10, p.y * 0.050 - t * 0.85);
	float w = au_fbm3(uv * 1.3 + vec2(t * 0.12, semente));
	float n = au_fbm(uv + vec2(w * 1.2, w * 0.6));
	float nb = au_fbm3(uv + vec2(w * 1.2, w * 0.6));
	float alcance = au_alcance(p.y, 7.0, 12.0);
	float perto = 1.0 - smoothstep(alcance, alcance + 6.0, dc);
	float fb = 1.0 - dc / alcance + (nb - 0.5) * 1.70 * perto;
	// a beirada fumacenta: um ruido fino so na borda
	float fino = au_ruido(p * 0.8 + vec2(semente, -t * 2.5)) - 0.5;
	float fe = fb + fino * 0.08;
	float cobre = smoothstep(0.0, 0.04, fe);
	// o aro vermelho vivo: uma faixa fina logo dentro da beirada
	float aro = 1.0 - smoothstep(0.03, 0.14, fe);
	// O VIOLETA: a distancia ate a SILHUETA, nao um oval -- ~4 px colado no corpo, desfiando no ruido
	float violeta = 1.0 - smoothstep(0.5, 4.5, dc + (n - 0.5) * 4.0);
	float veio = au_fbm(vec2(p.x * 0.16 + semente, p.y * 0.09 - t * 0.6));
	vec3 roxo = mix(cor_meio.rgb * 0.70, cor_meio.rgb, smoothstep(0.30, 0.70, veio));
	vec3 cor = mix(cor_borda.rgb, roxo, violeta);
	cor = mix(cor, cor_miolo.rgb, (1.0 - smoothstep(0.0, 2.5, dc)) * 0.45);
	cor = mix(cor, cor_aro.rgb, aro * (1.0 - violeta));
	// o brilho vermelho em volta da beirada: curto
	float halo = exp(-max(-fe, 0.0) / 0.06) * (1.0 - cobre) * 0.45;
	// COMECA NO PE
	float chao = smoothstep(-2.5, 0.5, p.y + (nb - 0.5) * 3.0);
	cobre *= chao;
	vec3 rgb = cor * cobre + cor_aro.rgb * halo * 0.60 * chao;
	return vec4(rgb, clamp(cobre * 0.75 + halo * 0.25 * chao, 0.0, 1.0));
}

// ============================ 3 E 4) A LUZ DO BLUE E O BLUE KAIO-KEN, 1:1 COM AS FOTOS DO DONO (o pedido 223) ============================
// O dono, sobre a primeira passada: *"As auras ainda n estao no ponto das imagens q mandei, quero elas 1:1"*. As duas sao medidas nas
// fotos dele, pixel a pixel, na escala do corpo (o boneco de ~30 px da foto): as CORES sao as da foto, e a cor vem da DISTANCIA ATE A
// BEIRADA, pra dentro -- um brilho interno, e nao a distancia ate o corpo. Na luz do Blue: o contorno amarelado (`cor_aro`) com o brilho
// oliva por fora (`cor_borda`), ~2 px da faixa na cor da forma (`cor_meio`), e dai clareando devagar ate o miolo quase branco
// (`cor_miolo`). Na chama do Blue Kaio-ken: a beirada vermelha escura, o vermelho, e o rosa clareando ate encostar na luz azul.
// O FORMATO tambem e o da foto: a luz e um VASO (estreito nos pes, +-19 na altura da cabeca, ~48 de altura) em BOLHAS de nuvem; a chama
// vermelha e um OVO de ponta pra cima (+-30 no meio, a base redonda nos pes) com LINGUAS que sobem -- altas no meio, curtas nos cantos.
// O prototipo que mediu tudo isso contra as fotos usou estas mesmas funcoes de ruido.

// A RAMPA DA LUZ, pela distancia `d` pra dentro da beirada (px): o contorno (`aro`) vira a faixa da cor da forma em ~1 px, ela segura
// ~2 px, e dai clareia ate o miolo em ~11 px (a foto: 64cdee, 86c3e6, 9ccde8, acd8ea, bcdfe8, cde2e2, dbe1de, e0e0de -- o miolo da foto e
// quase todo branco, e o ciano so na beirada).
vec3 au_rampa_luz(float d, vec3 aro) {
	vec3 c = mix(aro, cor_meio.rgb, smoothstep(0.0, 0.9, d));
	float k = pow(clamp((d - 2.4) / 9.0, 0.0, 1.0), 0.7);
	return mix(c, cor_miolo.rgb, k);
}

// O VASO DA LUZ, em distancia ate a beirada (negativa dentro): duas elipses unidas -- a de cima larga (a cabeca e os ombros), a de baixo
// estreita (as pernas) --, as BOLHAS de nuvem (o ruido "billow", o modulo: o vinco fundo entre uma bolha e outra), bolhas menores e um
// serrilhado fino. `esc` o escala (o Blue Kaio-ken o usa um pouco menor, por dentro do vermelho). `n1` volta pra desfiar o pe.
float au_vaso(vec2 p, float t, float esc, out float n1) {
	vec2 e = p / esc;
	float cima = au_sd_elipse(e - vec2(0.0, 27.5), vec2(18.5, 18.5));
	float baixo = au_sd_elipse(e - vec2(0.0, 13.0), vec2(10.0, 15.0));
	float base = au_smin(cima, baixo, 5.0) * esc;
	n1 = au_fbm3(vec2(p.x * 0.075 + semente, p.y * 0.07 - t * 0.30));
	float bolha = 1.0 - abs(n1 - 0.5) * 2.0;
	float bolha2 = 1.0 - abs(au_ruido(vec2(p.x * 0.26 + 4.0, p.y * 0.22 - t * 0.6)) - 0.5) * 2.0;
	float fino = au_ruido(vec2(p.x * 0.6 + 7.0, p.y * 0.6 - t * 1.2)) - 0.5;
	return base - (bolha - 0.45) * 11.0 - (bolha2 - 0.5) * 2.4 + fino * 0.8;
}

// A LUZ: o vaso na rampa, o brilho de fora (`fora_cor`, ~4 px sumindo) e o pe desfiado. Opaca por dentro, como na foto.
vec4 au_luz(vec2 p, float t, float esc, vec3 aro, vec3 fora_cor, float fora_forca) {
	float n1;
	float d = -au_vaso(p, t, esc, n1);
	vec3 cor = au_rampa_luz(d, aro);
	float dentro = smoothstep(-0.35, 0.35, d);
	float fora = exp(min(d, 0.0) / 1.1) * (1.0 - dentro) * fora_forca;
	// COMECA NO PE
	float chao = smoothstep(-2.5, 0.5, p.y + (n1 - 0.5) * 3.0);
	return vec4((cor * dentro + fora_cor * fora) * chao, clamp(dentro + fora, 0.0, 1.0) * chao);
}

// A RAMPA DA CHAMA VERMELHA, pela distancia pra dentro da beirada (a foto: c34141, da5a5a, e77e7e, ... fcbaba, fac4c4, eacfcf).
vec3 au_rampa_vermelha(float d) {
	vec3 c = mix(vec3(0.765, 0.255, 0.255), vec3(0.855, 0.353, 0.353), smoothstep(0.0, 0.6, d));
	c = mix(c, vec3(0.906, 0.494, 0.494), smoothstep(0.6, 1.2, d));
	c = mix(c, vec3(0.988, 0.729, 0.729), smoothstep(1.2, 7.0, d));
	c = mix(c, vec3(0.980, 0.769, 0.769), smoothstep(7.0, 10.5, d));
	return mix(c, vec3(0.918, 0.812, 0.812), smoothstep(10.5, 14.0, d));
}

// A CHAMA VERMELHA, em distancia ate a beirada: o OVO de ponta pra cima (duas elipses unidas) e as LINGUAS -- o ruido "ridged" (a
// crista) esticado na vertical, subindo rapido, EMPURRANDO A BEIRADA PRA CIMA (o y, e nao pra fora: as pontas sobem sem alargar a
// chama), altas no meio e curtas nos cantos. `n` volta pra desfiar o pe.
float au_vela(vec2 p, float t, out float n) {
	n = au_fbm3(vec2(p.x * 0.14 + semente + 3.0, p.y * 0.038 - t * 1.6));
	float crista = pow(1.0 - abs(n - 0.5) * 2.0, 1.8);
	float alto = smoothstep(4.0, 50.0, p.y);
	float lingua = crista * (1.0 + 20.0 * alto) * (1.0 - 0.30 * smoothstep(6.0, 24.0, abs(p.x)));
	vec2 q = vec2(p.x, p.y - lingua);
	float base = au_smin(au_sd_elipse(q - vec2(0.0, 16.0), vec2(26.5, 19.0)), au_sd_elipse(q - vec2(0.0, 33.0), vec2(30.0, 23.0)), 6.0);
	return base + (au_ruido(vec2(p.x * 0.5, p.y * 0.3 - t * 3.0)) - 0.5) * 1.0;
}

vec4 au_fogo_kaioken(vec2 p, float t) {
	float n;
	float d = -au_vela(p, t, n);
	vec3 cor = au_rampa_vermelha(d);
	float dentro = smoothstep(-0.3, 0.3, d);
	// o brilho vermelho de fora, como o da foto: a beirada brilha uns 2 px pra fora
	float fora = exp(min(d, 0.0) / 1.8) * (1.0 - dentro) * 0.60;
	// COMECA NO PE (a base redonda passa um fio abaixo dele, como na foto)
	float chao = smoothstep(-4.0, -1.0, p.y + (n - 0.5) * 2.0);
	return vec4((cor * dentro + vec3(0.804, 0.212, 0.216) * fora) * chao, clamp(dentro + fora, 0.0, 1.0) * chao);
}

// AS ESTRELINHAS da luz: pontos brancos em celulas sorteadas, piscando (a celula e as vizinhas). Devolve a intensidade.
float au_estrelas(vec2 p, float t) {
	float celula = 7.0;
	vec2 base = floor(p / celula);
	float s = 0.0;
	for (int ix = -1; ix <= 1; ix++) {
		for (int iy = -1; iy <= 1; iy++) {
			vec2 cc = base + vec2(float(ix), float(iy));
			float h = au_hash(cc + vec2(semente, 17.0));
			if (h > 0.30) { continue; }
			float h2 = au_hash(cc + vec2(31.0, semente));
			float h3 = au_hash(cc + vec2(semente, 53.0));
			vec2 centro = (cc + vec2(0.25 + 0.5 * h2, 0.25 + 0.5 * h3)) * celula;
			float ponto = 1.0 - smoothstep(0.25, 0.75, length(p - centro));
			s = max(s, ponto * (0.55 + 0.45 * sin(t * (2.5 + 2.5 * h3) + h2 * 6.28)));
		}
	}
	return s;
}

// OS FLOCOS VERMELHOS do Blue Kaio-ken, dentro da luz azul: losangos macios subindo em colunas sorteadas. Devolve a intensidade.
float au_faiscas_vermelhas(vec2 p, float t) {
	float col = floor(p.x / 4.0);
	float h1 = au_hash(vec2(col, 5.0 + semente));
	if (h1 > 0.55) { return 0.0; }
	float h2 = au_hash(vec2(col, 9.0 + semente));
	float h3 = au_hash(vec2(col, 13.0 + semente));
	float y = mod(h3 * 131.0 + t * (10.0 + 14.0 * h2), 70.0) - 4.0;
	float dx = (fract(p.x / 4.0) - (0.3 + 0.4 * h3)) * 4.0;
	float dy = p.y - y;
	return pow(clamp(1.0 - abs(dx) / 1.3 - abs(dy) / 3.2, 0.0, 1.0), 0.7);
}

// OS PONTINHOS: subindo em colunas sorteadas, piscando. Devolve a intensidade.
float au_faiscas(vec2 p, float t) {
	// o Rose e um ceu de pontinhos (a referencia dele); nas outras eles sao poucos
	float coluna_px = estilo == 2 ? 3.0 : 4.0;
	float cx = p.x / coluna_px;
	float col = floor(cx);
	float h1 = au_hash(vec2(col, 3.0 + semente));
	if (h1 > (estilo == 2 ? 0.80 : 0.50)) { return 0.0; }
	float h2 = au_hash(vec2(col, 7.0 + semente));
	float h3 = au_hash(vec2(col, 11.0 + semente));
	float vel = mix(10.0, 22.0, h2);
	float y = mod(h3 * 97.0 + t * vel, 64.0) - 6.0;
	float dx = (fract(cx) - (0.3 + 0.4 * h3)) * coluna_px;
	float dy = p.y - y;
	float r = length(vec2(dx, dy * 0.8));
	float pisca = 0.55 + 0.45 * sin(t * mix(7.0, 13.0, h2) + h3 * 6.28);
	return (1.0 - smoothstep(0.20, 0.65, r)) * pisca;
}

void fragment() {
	// O pixel no espaco do corpo, em pixel de mundo: x a partir do eixo, y pra cima a partir dos pes.
	vec2 p = vec2((UV.x - 0.5) * largura_px, pes_px - UV.y * altura_px);
	// O RELOGIO DOBRADO NUMA HORA: o tempo do motor so cresce, e com ele a coordenada do ruido. Uma vez por hora a chama
	// da um salto -- o preco de ela nunca virar bloco.
	float t = mod(TIME, 3600.0) + semente * 0.37;

	vec4 c;
	if (estilo == 0) {
		c = au_fogo(p, t);
	} else if (estilo == 1) {
		c = au_brilho(p, t);
	} else if (estilo == 2) {
		c = au_rosa(p, t);
	} else if (estilo == 3) {
		// A LUZ DO BLUE: o contorno amarelado e o brilho oliva por fora (a foto), e as estrelinhas por dentro. (o pedido 223g) No tamanho
		// da forma: o ponto lido em `p / tamanho_luz` encolhe o desenho inteiro em torno dos pes.
		vec2 q = p / tamanho_luz;
		c = au_luz(q, t, 1.0, cor_aro.rgb, cor_borda.rgb, 0.35);
		float fe = au_estrelas(q, t) * c.a;
		c = c * (1.0 - fe) + vec4(1.0) * fe;
	} else {
		// O BLUE KAIO-KEN: a chama vermelha por fora e a luz do Blue (um pouco maior, com o contorno e o brilho de fora branco-ciano, como
		// na foto) por cima dela -- o "por cima" do alfa premultiplicado --, com as estrelinhas e os flocos vermelhos dentro da luz
		vec4 fora = au_fogo_kaioken(p, t);
		vec4 dentro = au_luz(p, t, 1.05, vec3(0.706, 0.851, 0.851), vec3(0.847, 0.933, 0.941), 0.55);
		float fe = au_estrelas(p, t) * dentro.a;
		dentro = dentro * (1.0 - fe) + vec4(1.0) * fe;
		float fl = au_faiscas_vermelhas(p, t) * dentro.a;
		dentro = dentro * (1.0 - fl) + vec4(0.878, 0.392, 0.392, 1.0) * fl;
		c = dentro + fora * (1.0 - dentro.a);
	}

	if (estilo < 3) {
		// OS PONTINHOS BRANCOS, so dentro da chama.
		float fa = au_faiscas(p, t) * clamp(c.a * 2.5, 0.0, 1.0);
		c = c * (1.0 - fa) + vec4(cor_miolo.rgb, 1.0) * fa;
	}

	// A BEIRADA DO QUAD: o desenho foi medido pra caber, e isto so garante que nada corta reto se passar.
	float quad = smoothstep(0.0, 0.04, UV.x) * smoothstep(1.0, 0.96, UV.x) * smoothstep(0.0, 0.04, UV.y);
	float presenca = clamp(forca, 0.0, 1.0);
	c.rgb *= 1.0 + max(forca - 1.0, 0.0) * 0.5;
	COLOR = c * presenca * quad;
}
