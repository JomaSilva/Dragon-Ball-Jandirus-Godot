shader_type canvas_item;
render_mode blend_mix, unshaded;

// ============================================================================
// A COLUNA DE LUZ DAS ESTREIAS DIVINAS (2026-10-25) -- o pedido do dono, com uma imagem: uma coluna branca larga, de borda
// azulada, subindo do chao ao ceu e acendendo o chao em volta ("esse clarao subindo do personagem"). A do -Sign- e
// prata-azulada; a do Ultra Ego, roxa ("um clarao parecido com do UI so q roxo"). A cor e a da forma; o miolo, branco.
//
// UM QUADRO ALTO QUE ANDA COM O CORPO, com os pes perto do fundo dele, atras do corpo. Quem anda os tempos e o tocador
// (`Transformacao.EstreiaDivina.cs`), pelos uniforms:
//   altura  -- ate onde a coluna ja subiu, a partir dos pes (px do mundo);
//   largura -- a meia-largura do miolo (px): fina ao nascer, grossa na virada, larga quando engole o corpo;
//   base    -- de onde ela comeca, a partir dos pes (px): sobe quando ela se solta do chao, na cauda;
//   alfa    -- a coluna toda;
//   branco  -- quanto do miolo vira branco: 1 na coluna; menos no CONE do fogo do Ultra Ego (2026-10-25), que fica roxo;
//   chao_aceso -- quanto do chao em volta dos pes acende: 1 na coluna; pouco no cone, que mora dentro do anel de fogo.
// ============================================================================

uniform vec2 lado = vec2(560.0, 1500.0);
/// A linha dos pes no quadro, em px a partir do alto dele.
uniform float pe = 1476.0;
uniform float altura = 0.0;
uniform float largura = 4.0;
uniform float base = 0.0;
uniform float alfa = 0.0;
uniform float branco = 1.0;
uniform float chao_aceso = 1.0;
uniform vec4 cor : source_color = vec4(0.79, 0.85, 1.0, 1.0);

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

void fragment()
{
	vec2 p = UV * lado;
	float x = p.x - lado.x * 0.5;
	float y = pe - p.y;                       // a altura acima dos pes, em px (negativo: abaixo deles)
	float ax = abs(x);

	// O MIOLO chapado e o HALO em volta dele; os veios correm pra cima e fazem o halo respirar.
	float miolo = 1.0 - smoothstep(largura * 0.7, largura, ax);
	float halo = exp(-pow(ax / (largura * 2.4 + 6.0), 2.0));
	float veio = ruido(vec2(x * 0.16, y * 0.02 - TIME * 2.6));
	halo *= 0.6 + 0.4 * veio;

	// O ALTO (a ponta que sobe) e o PE (o chao, ou a base que se solta na cauda).
	float topo = 1.0 - smoothstep(altura - 80.0, altura, y);
	float chao = smoothstep(base - 3.0, base + 6.0, y);
	float coluna = max(miolo, halo * 0.9) * topo * chao;

	// O CHAO ACESO em volta dos pes (a elipse da imagem), so enquanto ela esta no chao.
	float aceso = exp(-(pow(x / (largura * 4.5 + 22.0), 2.0) + pow(y / (largura * 1.1 + 7.0), 2.0)))
				* (1.0 - smoothstep(0.0, 40.0, base)) * step(1.0, altura);

	vec3 c = mix(cor.rgb, vec3(1.0), clamp(miolo + 0.35 * halo, 0.0, 1.0) * branco);
	COLOR = vec4(c, clamp(coluna + aceso * 0.85 * chao_aceso, 0.0, 1.0) * alfa);
}
