shader_type canvas_item;
render_mode unshaded;

// ============================================================================
// O FUNDO DA ESTREIA DO SSJ2 (2026-10-24) -- o pedido do dono, com tres prints do Gohan: o jogo some
// atras do corpo, "uma linha vermelha passaria atras e se expande cobrindo todo o fundo preto com esse
// efeito vermelho/amarelo".
//
// UMA CORTINA SO, EM COORDENADA DE TELA (SCREEN_UV): o retangulo e grande e anda com o corpo, mas a
// linha e o fogo sao da TELA -- a diagonal cruza o quadro inteiro onde quer que o corpo esteja. Quem
// anda os tempos e o `FundoDaEstreia.cs`, pelos tres uniforms:
//   alfa    -- a cortina toda (o mundo some e volta);
//   linha   -- de 0 a 1, quanto a linha fina ja atravessou a tela (da esquerda pra direita);
//   largura -- a meia-largura da faixa em volta da diagonal: fina, depois a tela inteira.
// ============================================================================

uniform float alfa : hint_range(0.0, 1.0) = 0.0;
uniform float linha : hint_range(0.0, 1.0) = 0.0;
uniform float largura = 0.0;
/// A diagonal desce pra direita, como nos prints (o y da tela cresce pra baixo). Quem escreve e o C#.
uniform float inclinacao = 0.25;

const vec3 VERMELHO = vec3(0.55, 0.04, 0.0);
const vec3 LARANJA = vec3(1.0, 0.40, 0.04);
const vec3 AMARELO = vec3(1.0, 0.80, 0.38);
const vec3 BRASA = vec3(1.0, 0.22, 0.05);
const vec3 NUCLEO = vec3(1.0, 0.86, 0.62);

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float ruido(vec2 p)
{
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash(i);
	float b = hash(i + vec2(1.0, 0.0));
	float c = hash(i + vec2(0.0, 1.0));
	float d = hash(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// O FOGO DOS PRINTS: nuvens grossas de laranja com veios de amarelo, e o vermelho no fundo delas.
float fogo(vec2 p)
{
	float v = 0.0;
	float a = 0.5;
	for (int k = 0; k < 5; k++)
	{
		v += a * ruido(p);
		p = p * 2.03 + vec2(7.1, 3.7);
		a *= 0.5;
	}
	return v;
}

void fragment()
{
	vec2 uv = SCREEN_UV;
	float d = abs((uv.y - 0.5) - inclinacao * (uv.x - 0.5));

	// A FAIXA EM FOGO (o segundo e o terceiro print). Sem largura ela nao existe -- a linha fina vem sozinha.
	float n = fogo(uv * vec2(7.0, 5.0) + vec2(TIME * 0.45, -TIME * 0.25));
	vec3 chama = mix(VERMELHO, LARANJA, smoothstep(0.22, 0.58, n));
	chama = mix(chama, AMARELO, smoothstep(0.60, 0.85, n));
	float dentro = step(0.0005, largura) * (1.0 - smoothstep(largura * 0.85, largura + 0.004, d));
	float borda = smoothstep(largura * 0.55, largura, d) * dentro;   // a borda da faixa escurece pro vermelho
	vec3 cor = mix(vec3(0.0), mix(chama, VERMELHO * 0.6, borda * 0.7), dentro);

	// A LINHA FINA (o primeiro print): nucleo claro e halo vermelho, entrando pela esquerda.
	float vista = 1.0 - smoothstep(linha * 1.25 - 0.12, linha * 1.25 - 0.08, uv.x);
	float nucleo = exp(-pow(d / 0.0035, 2.0));
	float halo = exp(-pow(d / 0.018, 2.0));
	cor += (BRASA * halo * 0.9 + NUCLEO * nucleo) * vista * (1.0 - dentro * 0.6);

	COLOR = vec4(min(cor, vec3(1.0)), alfa);
}
