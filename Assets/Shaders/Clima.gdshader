shader_type canvas_item;

// ============================ O VEU DO CLIMA ============================
// Pinta o ECRA INTEIRO (um ColorRect que cobre a tela num CanvasLayer), entao aqui `UV` e espaco
// de TELA -- ao contrario do `Personagem.gdshader`, onde todo sprite e AtlasTexture e o UV cru
// mente. Sem textura de entrada, nada a corrigir.
//
// O QUE ISTO NAO FAZ: a chuva, a neve e a areia. Aquilo e PARTICULA (ver `ClimaNaTela.cs`) --
// pingo e uma coisa que cai, e uma coisa que cai tem posicao propria, velocidade propria e um fim.
// Fazer chuva em shader de tela cheia obriga a fingir tudo isso com repeticao, e o olho pega a
// repeticao em dois segundos.
//
// O QUE ISTO FAZ e o que particula faz MAL: a MASSA. Nuvem, neblina, fumaca e a poeira suspensa da
// tempestade de areia nao tem contorno nem contagem -- sao campos continuos. Um so quad e uma
// soma de ruido dao isso por um custo fixo, que nao cresce com a "quantidade" de nuvem.
// ========================================================================

/// O tempo, em segundos. Anda sozinho -- e o unico jeito de a nuvem se mover.
uniform float tempo = 0.0;

/// Forca geral, de 0 a 1. E o fade de entrada e de saida do clima.
uniform float forca : hint_range(0.0, 1.0) = 0.0;

/// A cor da massa (cinza da nuvem, branco da nevasca, ocre da areia).
uniform vec3 cor : source_color = vec3(0.55, 0.57, 0.62);

/// Quao OPACO o veu chega a ficar no auge.
uniform float densidade : hint_range(0.0, 1.0) = 0.5;

/// Tamanho de uma "celula" da massa, em PIXELS DE MUNDO. Grande = nuvem larga; pequeno = poeira.
uniform float escala = 1200.0;

// ============================ A MASSA FICA NO MUNDO, NAO NA TELA ============================
// O quad e de tela (cobre a janela inteira, que e o certo -- neblina fica ENTRE voce e tudo),
// mas o CAMPO e amostrado em coordenada de MUNDO. Sem isto a nuvem viajava colada na camera: o
// jogador andava dez tiles e a mesma mancha continuava no mesmo canto do ecra, o que le como
// sujeira na lente e nao como tempo fechado.
//
// `origem` e o canto superior esquerdo da tela em pixels de mundo; `tamanho` e o quanto de mundo
// a tela cobre. Com os dois, cada pixel sabe em que lugar do planeta ele esta.
//
// A PARALAXE E POR TIPO. Nuvem esta LA EM CIMA e quase nao se desloca quando se anda (valor
// baixo); neblina esta na altura do corpo e acompanha o chao (valor perto de 1). E a mesma
// leitura de profundidade que qualquer fundo em camadas usa -- sem ela, ou a nuvem parece pintada
// no chao, ou a neblina parece um decalque no ceu.
// ============================================================================================
uniform vec2 origem = vec2(0.0);
uniform vec2 tamanho = vec2(1920.0, 1080.0);
uniform float paralaxe : hint_range(0.0, 1.0) = 1.0;

/// Pra onde a massa corre. A areia corre de lado; a nuvem quase nao anda.
uniform vec2 deriva = vec2(0.02, 0.006);

/// Quanto o campo e "manchado" (nuvem, com buracos) contra "chapado" (neblina uniforme).
uniform float manchado : hint_range(0.0, 1.0) = 1.0;

/// O CLARAO DO RAIO. Sobe pra 1 no estouro e cai sozinho -- ver `Raio.gdshader`, que desenha o
/// risco em si. Aqui e so a luz que ele joga na cena inteira.
uniform float clarao : hint_range(0.0, 1.0) = 0.0;

// ---- ruido de valor + fbm ----
// Feito a mao em vez de textura de ruido importada: sao quatro linhas, roda em qualquer maquina, e
// nao adiciona um asset que teria de ser reimportado a cada `--import`.
float hash(vec2 p)
{
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

float ruido(vec2 p)
{
	vec2 i = floor(p);
	vec2 f = fract(p);
	// suavizacao cubica: sem ela as celulas do ruido aparecem como um xadrez
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
			   mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p)
{
	// QUATRO OITAVAS, e nao mais: da terceira em diante cada uma vale metade da anterior, e a
	// quinta mexe em menos de 3% do resultado por um quinto a mais de custo por pixel de tela.
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++)
	{
		v += a * ruido(p);
		p *= 2.03;   // nao 2.0: dobrar exato alinha as oitavas e o padrao vira grade
		a *= 0.5;
	}
	return v;
}

void fragment()
{
	if (forca <= 0.001 && clarao <= 0.001)
	{
		COLOR = vec4(0.0);
	}
	else
	{
		// ONDE ESTE PIXEL ESTA NO MUNDO, em celulas de ruido. A paralaxe encolhe o quanto o
		// campo anda com a camera: 1 gruda no chao, 0 gruda na tela.
		vec2 mundo = origem * paralaxe + UV * tamanho;
		vec2 p = mundo / max(escala, 1.0) + deriva * tempo;

		// DUAS CAMADAS em velocidades diferentes: uma so da um padrao que desliza rigido, e o olho
		// le como uma textura sendo arrastada em vez de nuvem se deformando.
		float n = fbm(p) * 0.65 + fbm(p * 1.9 - deriva * tempo * 0.6) * 0.35;

		// `manchado` = 0 achata o campo num veu parelho (neblina); = 1 deixa o contraste do
		// ruido inteiro (nuvem carregada, com clareira e miolo escuro).
		float massa = mix(0.5, n, manchado);

		float alfa = clamp(massa * densidade * forca, 0.0, 1.0);
		vec3 rgb = cor;

		// O CLARAO ENTRA POR CIMA DE TUDO e nao depende de `forca`: quando o raio estoura, a
		// tela lava de branco mesmo nas beiradas onde a nuvem esta fina.
		rgb = mix(rgb, vec3(1.0), clarao * 0.9);
		alfa = clamp(alfa + clarao * 0.55, 0.0, 1.0);

		COLOR = vec4(rgb, alfa);
	}
}
