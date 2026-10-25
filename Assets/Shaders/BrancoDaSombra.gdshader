shader_type canvas_item;
render_mode blend_mix, unshaded;

// ============================================================================
// O BRANCO QUE ENGOLE TUDO (2026-10-25) -- a segunda imagem do dono: tudo branco, o corpo uma silhueta preta e borrada, e
// pontinhos escuros em volta ("e aparece so a silhueta do corpo como uma sombra"). So na tela de quem vira.
//
// O corpo de verdade desenha POR CIMA deste quadro, preto (o tocador o lava de preto e o sobe de plano). Aqui sao o branco,
// o BORRAO da sombra em volta dele (a silhueta da imagem nao tem borda dura) e os PONTINHOS. O quadro e enorme e anda com o
// corpo: o centro dele (UV 0,5) e o meio do corpo. Quem anda o `alfa` (o branco entrando e saindo) e o tocador
// (`Transformacao.EstreiaDivina.cs`).
// ============================================================================

uniform float lado = 8000.0;
uniform float alfa = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

void fragment()
{
	vec2 p = (UV - 0.5) * lado;              // px do mundo, a partir do meio do corpo

	// o branco, com um azul de nada longe do corpo (a luz vem dele)
	vec3 c = mix(vec3(1.0), vec3(0.94, 0.96, 1.0), smoothstep(80.0, 260.0, length(p)));

	// O BORRAO: o corpo (uns 16x32 px) vaza escuro em volta -- a silhueta borrada
	vec2 q = p / vec2(13.0, 22.0);
	c = mix(c, vec3(0.0), exp(-dot(q, q) * 1.1) * 0.55);

	// OS PONTINHOS ESCUROS: sobem devagar em volta do corpo, mais densos perto dele
	vec2 grade = vec2(p.x, p.y + TIME * 14.0) / 7.0;
	vec2 cel = floor(grade);
	vec2 dentro = fract(grade) - vec2(hash(cel + 3.1), hash(cel + 7.7));
	float ponto = (1.0 - smoothstep(0.10, 0.20, length(dentro))) * step(0.86, hash(cel));
	c = mix(c, vec3(0.05), ponto * exp(-length(p) / 70.0) * 0.8);

	COLOR = vec4(c, alfa);
}
