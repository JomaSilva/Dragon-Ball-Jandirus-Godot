shader_type canvas_item;
render_mode blend_add, unshaded;

// ============================================================================
// UM RAIOZINHO DE FORMA -- o `electrictyeffects` do BYOND, desenhado em vez de
// animado quadro a quadro.
//
// ============================ POR QUE PROCEDURAL E NAO SPRITE ============================
// O original tem uma folha .dmi com N quadros de eletricidade, e o overlay cicla por eles. Isso
// tem dois custos que so aparecem no jogo: TODO mundo transformado pisca os MESMOS quadros na
// mesma ordem (dez Super Saiyajin numa luta viram um estroboscopio sincronizado), e a cor e a do
// desenho -- um raio azul do Blue exigiria outra folha.
//
// Aqui cada particula desenha o proprio raio a partir da POSICAO dela: dois raios nunca saem
// iguais, e a cor e um uniform. O custo e um punhado de senos por pixel de um quad minusculo.
// =====================================================================================
//
// O QUAD E VERTICAL: o raio corre de UV.y = 0 (topo) a 1 (base), e o desvio horizontal e a soma
// de tres dobras de frequencia diferente -- que e como se desenha um relampago a mao.
// ============================================================================

uniform vec4 cor : source_color = vec4(1.0, 0.96, 0.62, 1.0);

/// Meia-largura do nucleo, em fracao do quad. Fino: raio grosso vira borrao.
uniform float grossura : hint_range(0.005, 0.2) = 0.045;

/// Quanto o raio serpenteia. 0 = risco reto.
uniform float zigue : hint_range(0.0, 1.0) = 0.34;

/// Brilho do halo em volta do nucleo.
uniform float halo : hint_range(0.0, 3.0) = 1.1;


// A POSICAO DA PARTICULA E A SEMENTE. Vem do vertex porque no fragment nao ha como saber onde o
// quad esta no mundo -- e sem semente todas as particulas desenhariam o MESMO raio.
varying vec2 semente;
varying float fase;

float ruido(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void vertex() {
	semente = MODEL_MATRIX[3].xy;

	// `INSTANCE_CUSTOM.y` e a FASE DE VIDA da particula (0 = nasceu, 1 = morreu), preenchida pelo
	// ParticleProcessMaterial. E o que faz o raio piscar e sumir em vez de so aparecer e apagar.
	fase = INSTANCE_CUSTOM.y;
}

void fragment() {
	float s = ruido(floor(semente));
	float s2 = ruido(floor(semente) + vec2(37.0, 17.0));

	// ============================ O RAIO SE CONTORCE ENQUANTO VIVE ============================
	// As tres dobras andam com a FASE. Sem isso o raio nasce numa forma e morre nela -- um risco
	// congelado que so muda de brilho. Com a fase somada, o corpo do raio serpenteia sozinho, que e
	// o que o dono descreveu: "vao se contorcendo ate sumirem".
	//
	// As velocidades sao diferentes entre as dobras (1x, 1,7x, 2,3x): iguais, as tres andariam em
	// bloco e o raio pareceria deslizar de lado em vez de se retorcer.
	// =====================================================================================
	float t = UV.y;
	float w = fase * 7.0;
	float desvio =
		  sin(t * 11.0 + s * 62.0 + w) * 0.50
		+ sin(t * 27.0 + s2 * 91.0 + w * 1.7) * 0.30
		+ sin(t * 53.0 + s * 143.0 + w * 2.3) * 0.20;
	desvio *= zigue * 0.5;

	// ============================ ALGUNS RAIOS SAO ARCOS, EM "C" ============================
	// Pedido do dono: que alguns pareçam CONTORNAR o personagem. Um arco se faz com meio seno ao
	// longo do raio -- zero nas duas pontas, barriga no meio -- e o resultado e um "C" ou um ")".
	//
	// O sorteio e por PARTICULA (a mesma semente da posicao), entao numa rajada de tres saem uns
	// retos e uns curvos. Fossem todos curvos, a leitura viraria "anel"; fossem todos retos,
	// perde-se a sensacao de que a eletricidade abraca o corpo.
	//
	// A AMPLITUDE E LIMITADA A 0,3 do quad de proposito: o raio ja gasta ate ~0,1 com o
	// zigue-zague, e a soma dos dois nao pode passar de 0,5 -- que e a metade do quad. Passando,
	// o arco encosta na borda e o "C" sai CORTADO, que e pior que nao ter arco.
	// =====================================================================================
	float sorteioC = ruido(floor(semente) + vec2(11.0, 23.0));
	float curvo = step(0.55, sorteioC);              // ~45% dos raios saem curvos
	float lado = sorteioC < 0.775 ? 1.0 : -1.0;      // pra qual lado o C abre
	float arco = sin(t * 3.14159265) * 0.3 * curvo * lado;

	float d = abs(UV.x - 0.5 - desvio - arco);

	float nucleo = 1.0 - smoothstep(0.0, grossura, d);
	float aura = 1.0 - smoothstep(0.0, grossura * 4.0, d);

	// AS PONTAS AFINAM. Sem isso o raio comeca e termina em corte reto -- fica um traco, nao uma
	// descarga.
	float ponta = smoothstep(0.0, 0.18, t) * smoothstep(1.0, 0.80, t);

	// ============================ PISCA EM DEGRAUS, NAO EM RAMPA ============================
	// Raio de verdade nao desvanece: ele ESTROBA. A versao anterior era um seno vezes uma queda
	// linear -- o que da um brilho que respira, bonito e nada eletrico.
	//
	// Aqui a vida e fatiada em ~16 lampejos e cada fatia sorteia o proprio brilho pela semente.
	// Alguns saem APAGADOS (o `step`), e e o apagado que faz a leitura: o olho le a ausencia entre
	// lampejos como faisca.
	// ===================================================================================
	float passo = floor(fase * 16.0);
	float br = ruido(vec2(passo, s * 91.0 + 7.0));
	float pisca = step(0.22, br) * (0.5 + 0.5 * br);

	// ============================ E SE DESFAZ EM PEDACOS ============================
	// O raio nao some inteiro: ele se PARTE, e os pedacos apagam em ordens diferentes -- e a
	// segunda imagem da referencia, com os cacos subindo depois que o corpo do raio ja foi.
	//
	// Cada uma das 8 fatias ao longo do raio tem um PRAZO proprio, sorteado. Quando a fase passa do
	// prazo daquela fatia, ela some e as vizinhas continuam. Perto do fim sobram uns poucos cacos.
	// ============================================================================
	float fatia = floor(t * 8.0);
	float prazo = 0.35 + 0.65 * ruido(vec2(fatia, s * 53.0 + 3.0));
	float inteiro = step(fase, prazo);

	// E O APAGAR GERAL no ultimo quarto, pra nenhum caco sumir com corte seco.
	float apagar = 1.0 - smoothstep(0.72, 1.0, fase);

	float i = (nucleo + aura * halo * 0.22) * ponta * pisca * inteiro * apagar;

	// ============================ DUAS CORRECOES, EM DIRECOES OPOSTAS ============================
	// 1a: o nucleo estourava pra branco puro (`nucleo * 0.75`) e o raio SUMIA -- a luz da aura ja
	//     deixa o chao claro, e branco sobre claro nao existe. Clarear menos resolveu.
	// 2a: ao corrigir aquilo eu passei do ponto -- ganho de 1,6x e saturacao de 1,4x deixaram tudo
	//     lavado. O ponto entre os dois erros e este: clarear pouco, saturar quase nada, ganho 1.
	//
	// Fica anotado porque a tentacao, das duas vezes, foi mexer no MESMO numero -- e o remedio de
	// um dos lados e a doenca do outro.
	// =========================================================================================
	// O NUCLEO E BRANCO-QUENTE e o halo e da cor -- e o desenho da referencia: um fio branco por
	// dentro de um brilho azul. Como o nucleo e fino (ver `grossura`), o branco nao lava a cena;
	// quem ocupa area e o halo colorido.
	vec3 rgb = mix(cor.rgb, vec3(1.0), nucleo * 0.55);

	// UM EMPURRAOZINHO de cor nas bordas, so pra o raio nao virar risco cinza contra fundo claro.
	rgb = mix(rgb, cor.rgb, aura * 0.35);

	COLOR = vec4(rgb * i, i * cor.a * 0.85);
}
