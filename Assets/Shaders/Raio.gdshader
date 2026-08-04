shader_type canvas_item;

// ============================ O RAIO DA TEMPESTADE ============================
// Um risco que desce do topo da tela em ZIGUE-ZAGUE, com miolo branco e halo azul, brilhando por
// um terco de segundo. Pintado num ColorRect estreito e alto, posicionado onde o raio cai.
//
// ============================ POR QUE SEGMENTOS RETOS ============================
// A primeira versao deslocava uma linha com ruido suave. Dava uma serpente: o traco ondulava
// como corda balancando, e descarga eletrica nao faz isso. Ela quebra -- vai reto, VIRA num
// canto vivo, vai reto de novo. O olho reconhece o canto, nao a curva.
//
// Aqui o risco e uma POLILINHA: uma pilha de vertices alternando esquerda e direita, e cada pixel
// mede a distancia ate o segmento mais proximo. O canto sai de graca, porque ele existe de
// verdade na geometria em vez de ser imitado por uma frequencia alta de ruido.
// ================================================================================
//
// POR QUE SHADER E NAO SPRITE: um raio desenhado e SEMPRE O MESMO raio. O olho decora o formato
// na terceira vez e a tempestade vira um GIF em loop. Aqui a forma sai de `semente`, que muda a
// cada estouro -- nao ha dois iguais, e nao ha arte pra manter.
//
// POR QUE SHADER E NAO PARTICULA: um raio nao e um punhado de coisas voando, e um CAMINHO
// continuo com espessura variavel. Montar isso com particulas exigiria emitir ao longo de uma
// curva que teria de ser calculada na CPU de qualquer forma -- e ainda ficaria pontilhado.
//
// A PARTE DE PARTICULA DA TEMPESTADE E A CHUVA, que roda junto (ver `ClimaNaTela.cs`), e o
// clarao de tela inteira mora no `Clima.gdshader`: sao tres pecas do mesmo instante.
// ==============================================================================

/// 0 = acabou de estourar, 1 = apagou. E o unico relogio do efeito.
uniform float idade : hint_range(0.0, 1.0) = 1.0;

/// Muda a cada raio. E o que faz o risco nunca se repetir.
uniform float semente = 0.0;

/// Quanto o risco se afasta do centro, em fracao da meia-largura do quad.
uniform float desvio : hint_range(0.0, 1.0) = 0.8;

/// A cor do HALO. O miolo estoura pra branco por conta propria.
uniform vec3 cor : source_color = vec3(0.55, 0.80, 1.0);

/// Ate onde o risco desce, de 0 (so o topo) a 1 (chao). Ramo curto nao chega embaixo.
uniform float alcance : hint_range(0.1, 1.0) = 1.0;

/// altura/largura do quad. Sem isto a espessura sai achatada, porque UV nao e quadrado.
uniform float razao = 6.0;

/// Espessura do miolo, em fracao da largura do quad.
///
/// ============================ ISTO ERA GROSSO DEMAIS ============================
/// Estava em 0,05 a 0,08, com um manto de 2,6x e um halo de ONZE VEZES em volta. Num quad que
/// ocupa um terco da tela, o halo sozinho cobria mais de meia largura -- e como ele so desaparece
/// no limite do quad, o retangulo do proprio ColorRect ficava visivel como uma mancha clara na
/// tela. O raio parecia um tubo de neon com brilho vazando.
///
/// Descarga eletrica e FINA e MUITO clara: o que se ve de longe e quase todo halo, mas o halo tem
/// que ser apertado o bastante pra ter forma. Agora o miolo e ~4x mais fino e o halo para em 4x.
/// ================================================================================
uniform float grossura : hint_range(0.002, 0.08) = 0.014;

/// Quantos cantos o risco tem. Poucos = manso; muitos = eletrico.
const int VERTICES = 10;

float hash(float x) { return fract(sin(x * 127.1 + semente * 73.7) * 43758.5453123); }

/// Onde esta o vertice `i`, no espaco corrigido pela razao.
vec2 vertice(int i)
{
	float t = float(i) / float(VERTICES - 1);
	float f = float(i);

	// ALTERNA OS LADOS. Um deslocamento sorteado livremente vira passeio aleatorio -- o risco
	// deriva pra um lado e nunca volta. O sinal alternado e o que produz o zigue-zague de
	// verdade; o sorteio so decide QUANTO cada perna avanca.
	float lado = (mod(f, 2.0) < 1.0) ? 1.0 : -1.0;
	float quanto = 0.22 + hash(f) * 0.78;

	// as pontas fecham no meio: raio nao comeca nem termina torto pra fora
	float afunila = sin(t * 3.14159265);

	// altura irregular: cantos igualmente espacados leem como zigue-zague de desenho animado
	float y = t + (hash(f + 51.0) - 0.5) * 0.06;

	return vec2(0.5 + lado * quanto * desvio * 0.5 * afunila, clamp(y, 0.0, 1.0) * razao);
}

/// Distancia de um ponto ao segmento a-b.
float dist_seg(vec2 p, vec2 a, vec2 b)
{
	vec2 pa = p - a;
	vec2 ba = b - a;
	float h = clamp(dot(pa, ba) / max(dot(ba, ba), 1e-6), 0.0, 1.0);
	return length(pa - ba * h);
}

void fragment()
{
	// O risco DESCE: so existe acima da frente, que corre no primeiro quarto da vida.
	float frente = clamp(idade / 0.25, 0.0, 1.0) * alcance;
	if (UV.y > frente)
	{
		COLOR = vec4(0.0);
	}
	else
	{
		vec2 p = vec2(UV.x, UV.y * razao);

		// ---- o tronco ----
		float d = 1e9;
		for (int i = 0; i < VERTICES - 1; i++)
			d = min(d, dist_seg(p, vertice(i), vertice(i + 1)));

		// ---- dois ramos ----
		// Saem de vertices sorteados e morrem depressa. Sao o que tira a leitura de "uma linha
		// so": descarga real se divide, e a divisao e mais visivel que o proprio zigue-zague.
		float dr = 1e9;
		for (int r = 0; r < 2; r++)
		{
			float s = float(r) * 17.0;
			int i = 2 + int(hash(90.0 + s) * float(VERTICES - 4));
			vec2 a = vertice(i);
			vec2 b = a + vec2((hash(91.0 + s) - 0.5) * 1.4, 0.25 + hash(92.0 + s) * 0.5);
			dr = min(dr, dist_seg(p, a, b));
		}

		// ---- espessura ----
		// AFINA PRA BAIXO: descarga e mais grossa perto da origem. Sem isso o risco vira uma fita
		// de largura constante, que e a cara de um retangulo torto.
		float esp = mix(grossura, grossura * 0.35, UV.y);

		float nucleo = 1.0 - smoothstep(esp * 0.4, esp, d);
		float manto = 1.0 - smoothstep(esp, esp * 2.2, d);
		float halo = 1.0 - smoothstep(esp, esp * 4.0, d);
		float ramo = 1.0 - smoothstep(esp * 0.25, esp * 0.7, dr);
		float haloRamo = 1.0 - smoothstep(esp * 0.6, esp * 2.6, dr);

		// APAGA COM UM REPIQUE. Descarga real pisca duas vezes -- o segundo lampejo e o que faz o
		// efeito parecer eletrico em vez de um fade de opacidade.
		float brilho = (1.0 - smoothstep(0.55, 1.0, idade))
					 * (1.0 + 0.7 * exp(-pow((idade - 0.40) * 11.0, 2.0)));

		float aceso = clamp(nucleo + ramo, 0.0, 1.0);
		float corpo = clamp(aceso + manto * 0.45, 0.0, 1.0);

		// O HALO ENTRA FRACO. Ele existe pra dar a impressao de luz demais pro sensor, nao pra
		// pintar area: subir isso e o caminho mais curto de volta pro tubo de neon.
		float brilhoLateral = clamp(halo * 0.16 + haloRamo * 0.10, 0.0, 1.0);

		float alfa = clamp((corpo + brilhoLateral) * brilho, 0.0, 1.0);

		// O MIOLO ESTOURA PRA BRANCO e so a franja fica com a cor. E o que da a leitura de "isto
		// e mais claro do que a tela consegue mostrar", que e como se ve um raio.
		vec3 rgb = mix(cor, vec3(1.0), aceso);
		COLOR = vec4(rgb, alfa);
	}
}
