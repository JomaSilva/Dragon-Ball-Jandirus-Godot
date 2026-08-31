shader_type canvas_item;

// ============================================================================================
// A MEGA EXPLOSAO -- *"uma mega explosao (via shaders e bem bonita de se ver) e assim o planeta
// some"*, pedido literal do dono.
//
// ============================ POR QUE PROCEDURAL, E NAO UMA FOLHA ============================
// Nao foi escolha estetica: **nao ha arte de explosao nos assets**. Procurei por `*explos*`,
// `*boom*` e `*blast*` e so existem SONS (`explosion.ogg`, ja convertido e ja mapeado em
// `Trilha.Explosao`) e feixes. E, mesmo se houvesse, uma folha nao serviria: este efeito precisa
// cobrir de 220 a 440 px de diametro (o raio de um planeta no espaco e 110..220 px), e a mesma
// razao que fez o anel de choque ser shader e nao o `Shockwavecustom` convertido vale em dobro
// aqui -- arte ampliada quatro vezes vira um borrao de blocos.
//
// ============================ O ESQUELETO VEIO DE DOIS EFEITOS QUE JA EXISTEM ============================
// Nada aqui e matematica nova. O anel e o mesmo do `Transformacao.gdshader` (crista = potencia da
// faixa) e a FRENTE QUE DESACELERA e a do `Gota.gdshader` (`pow(fase, 0.65)`, com o `ALCANCE > 1`
// que faz a onda SAIR da area em vez de sumir dentro dela). O que este junta que nenhum dos dois
// tinha e o NUCLEO (o clarao branco que estoura no meio) e os ESTILHACOS.
// ========================================================================================================
//
// ============================ AS TRES REGRAS DO CLARAO, QUE JA FORAM APRENDIDAS ============================
// Estao escritas no `Transformacao.Clarao` e valem aqui:
//   1. **branco puro e opaco e um corte pra branco** -- perde-se o quadro inteiro e, com ele,
//      justamente o que se queria ver. Por isso o nucleo satura pra um branco QUENTE (amarelo) e
//      cede lugar ao anel;
//   2. **a forca cai com a distancia** -- aqui isso e de graca, porque o efeito e um node no MUNDO
//      ancorado no planeta, e nao um retangulo de tela cheia. Quem esta longe ve pequeno;
//   3. **reaproveitar em vez de empilhar** -- um planeta so estoura uma vez, e o node se mata no
//      fim (ver `PlanetaDesenhado`).
// ==========================================================================================================

/// O RELOGIO DO EFEITO: 0 = o instante do estouro, 1 = acabou. Quem o move e um `Tween` de fora,
/// pela mesma razao do `Impacto.gdshader`: o shader nao sabe (nem deve saber) quanto dura.
uniform float t : hint_range(0.0, 1.0) = 0.0;

/// A SEMENTE DESTE ESTOURO -- sai da seed do planeta. Dois mundos morrendo com os MESMOS estilhacos
/// nos mesmos angulos denunciariam o efeito como desenho gerado; e a mesma regra da semente por
/// corpo dos ferimentos procedurais.
uniform float semente = 0.0;

/// O centro branco-quente. Nao e branco puro -- ver a regra 1 no cabecalho.
uniform vec4 cor_nucleo : source_color = vec4(1.00, 0.96, 0.80, 1.0);

/// A crista da onda de choque e os estilhacos.
uniform vec4 cor_onda : source_color = vec4(1.00, 0.55, 0.12, 1.0);

/// ATE ONDE A FRENTE VAI, em raios. Acima de 1 ela SAI do quad -- e o `ALCANCE` do `Gota.gdshader`,
/// pelo mesmo motivo: uma onda que para de crescer dentro da propria area le como bolha, nao como
/// choque.
uniform float alcance = 1.25;

/// Quantos raios de luz saem do centro. Numero primo de proposito: com um valor par os raios
/// espelham e a explosao ganha um eixo de simetria que o olho pega na hora.
uniform float raios = 23.0;

float ep_hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float ep_ruido(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(ep_hash(i),                  ep_hash(i + vec2(1.0, 0.0)), f.x),
			   mix(ep_hash(i + vec2(0.0, 1.0)), ep_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	vec2 d = UV - vec2(0.5);
	float r = length(d) * 2.0;              // 0 no centro, 1 na borda do quad
	float ang = atan(d.y, d.x);

	// ============================ A FRENTE DESACELERA ============================
	// `pow(t, 0.65)` -- a onda arranca rapido e vai freando, que e como uma frente de choque de
	// verdade se comporta e o que separa isto de um circulo crescendo linearmente. Copiado do
	// `Gota.gdshader`, onde a mesma curva ja tinha sido acertada olhando.
	// =============================================================
	float frente = pow(t, 0.65) * alcance;

	// ---- 1. O NUCLEO: o clarao que estoura e cede ----
	// Ele CRESCE um pouco e some rapido (`1-t` ao cubo). Se ele durasse, a explosao inteira viraria
	// um disco branco e nenhuma das outras camadas seria vista.
	float raioNucleo = 0.12 + 0.55 * pow(t, 0.45);
	float nucleo = smoothstep(raioNucleo, raioNucleo * 0.35, r) * pow(1.0 - t, 3.0);

	// ---- 2. A ONDA DE CHOQUE: um anel que afina enquanto abre ----
	float esp = 0.20 * (1.0 - t * 0.75);
	float faixa = smoothstep(esp, 0.0, abs(r - frente));
	float onda = faixa * faixa * faixa;      // a `crista` do `Transformacao.gdshader`

	// ---- 3. OS RAIOS: luz vazando pelas fendas do mundo que se abriu ----
	// Modulados por ruido pra nao virarem um leque regular. Eles vivem ATRAS da frente (dentro do
	// raio ja alcancado), porque sao a luz que sai do buraco, e nao algo que voa.
	float leque = pow(max(sin(ang * raios + semente * 6.28), 0.0), 6.0);
	leque *= ep_ruido(vec2(ang * 4.0 + semente, t * 1.5));
	float raiosLuz = leque * smoothstep(frente + 0.05, 0.0, r) * pow(1.0 - t, 2.0);

	// ---- 4. OS ESTILHACOS: pedacos do planeta indo embora com a frente ----
	// Pontos duros (`step`) num anel logo atras da crista. Borda dura de proposito: escombro tem
	// silhueta, e borda macia leria como fumaca.
	float grao = ep_hash(vec2(floor(ang * 90.0), floor(semente * 13.0)));
	float faixaEst = smoothstep(0.30, 0.0, abs(r - frente * (0.80 + grao * 0.30)));
	float estilhaco = step(0.86, ep_hash(vec2(floor(ang * 140.0), 7.0))) * faixaEst * (1.0 - t);

	float brilho = clamp(nucleo + onda + raiosLuz * 0.55 + estilhaco * 0.9, 0.0, 1.6);

	// O NUCLEO PUXA PRO BRANCO-QUENTE e o resto fica na cor da onda -- e o degrau de temperatura
	// que faz uma explosao parecer quente em vez de laranja.
	vec3 cor = mix(cor_onda.rgb, cor_nucleo.rgb, clamp(nucleo * 1.4, 0.0, 1.0));

	// NAO VAZA DO QUAD: o mesmo `step(r, 1.0)` do `Impacto.gdshader` -- sem ele o efeito ganharia
	// as quinas do retangulo, e quina denuncia o quad.
	COLOR = vec4(cor * brilho, clamp(brilho, 0.0, 1.0) * step(r, 1.0));
}
