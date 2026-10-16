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
// E nas tres, os PONTINHOS brancos subindo e piscando (no Rose, um ceu deles).
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
	} else {
		c = au_rosa(p, t);
	}

	// OS PONTINHOS BRANCOS, so dentro da chama.
	float fa = au_faiscas(p, t) * clamp(c.a * 2.5, 0.0, 1.0);
	c = c * (1.0 - fa) + vec4(cor_miolo.rgb, 1.0) * fa;

	// A BEIRADA DO QUAD: o desenho foi medido pra caber, e isto so garante que nada corta reto se passar.
	float quad = smoothstep(0.0, 0.04, UV.x) * smoothstep(1.0, 0.96, UV.x) * smoothstep(0.0, 0.04, UV.y);
	float presenca = clamp(forca, 0.0, 1.0);
	c.rgb *= 1.0 + max(forca - 1.0, 0.0) * 0.5;
	COLOR = c * presenca * quad;
}
