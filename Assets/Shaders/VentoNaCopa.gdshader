shader_type canvas_item;

// ============================ O VENTO NA COPA (o pedido 226, 2026-11-02) ============================
// O dono: *"vegetaçao dos planetas poderiam balançar (baseando-se no clima) pra simular vento, mas seria a parte superior deles pq se
// balancar a arvore toda o tronco vai balancar e ficar restranho, entao mire mais na parte das folhas mesma ideia q o terraria usa"*.
//
// Cada quadro de vegetacao do tileset ganha este material (`Client/VentoNaVegetacao.cs`) com o PE DA COPA medido na arte dele
// (`Vento.MedirCopa`, no Core). Do pe pra baixo -- o tronco -- nada mexe. Do pe pro alto, cada fileira e lida um pouco de lado: zero no
// pe, o maximo no topo. E isso que VERGA a copa sem tirar o tronco do lugar (deslocar os cantos do quadro, no vertex, entortaria a arvore
// inteira -- o que o dono pediu pra nao fazer).
//
// A ONDA E POR ARVORE: a fase sai do x de mundo do quadro (nao do pixel), entao cada arvore e rigida dentro da propria copa e a rajada
// passa pela mata uma arvore depois da outra. A forca vem do clima (`Vento.DoClima`), posta pelo `ClimaNaTela` a cada mudanca.
// ===================================================================================================

uniform vec2 quadros = vec2(1.0, 1.0);   // colunas e linhas da folha do atlas: o quadro e esta fracao da textura
uniform float topo = 0.0;                 // a fileira mais alta da arte, de 0 (o alto do quadro) a 1 (o pe do quadro)
uniform float pivo = 1.0;                 // o PE DA COPA, na mesma conta: daqui pra baixo (o tronco) nada mexe
uniform float porte = 1.0;                // a copa alta balanca mais pixels que a baixa (`Vento.Porte`)
uniform float amplitude = 1.28;           // px de balanco no alto de uma copa de porte 1 (`Vento.AmplitudePx`)
uniform float empurrao = 0.0;             // px que o vento deita a copa pra direita, no alto (`Vento.EmpurraoPx`)

varying vec2 mundo;
varying vec4 cor;

void vertex() {
	mundo = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;
	cor = COLOR;
}

void fragment() {
	vec2 celula = floor(UV * quadros);
	vec2 local = UV * quadros - celula;

	// O PESO DA FILEIRA: 0 do pe da copa pra baixo, 1 no topo -- e curvo, a ponta mexe mais que o meio
	float w = clamp((pivo - local.y) / max(pivo - topo, 0.001), 0.0, 1.0);
	w = pow(w, 1.5);

	// A FASE DA ARVORE: o x de mundo da beirada do quadro (o mesmo pra todo pixel dele)
	float largura = 1.0 / (TEXTURE_PIXEL_SIZE.x * quadros.x);
	float fase = (mundo.x - local.x * largura) * 0.012;
	float onda = sin(TIME * 1.7 - fase) * 0.65 + sin(TIME * 3.1 - fase * 1.6 + 1.3) * 0.35;
	float rajada = 0.7 + 0.3 * sin(TIME * 0.41 - fase * 0.4);
	float dx = (amplitude * onda * rajada + empurrao) * porte * w;

	// A FILEIRA E LIDA DE LADO (a copa vai pra direita quando dx > 0). Fora do quadro e vazio: nunca o quadro vizinho da folha.
	float lx = local.x - dx / largura;
	if (lx < 0.0 || lx > 1.0) {
		COLOR = vec4(0.0);
	} else {
		COLOR = texture(TEXTURE, (celula + vec2(lx, local.y)) / quadros) * cor;
	}
}
