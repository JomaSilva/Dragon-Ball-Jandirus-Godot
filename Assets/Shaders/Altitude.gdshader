shader_type canvas_item;

// A NEVOA DE ALTITUDE. Um retangulo que cobre a tela e desfoca/clareia o que esta atras dele
// conforme se sobe. Nada disso existe no original -- o voo do BYOND e no mesmo z, e a tela nunca
// muda. E desenho novo, e o unico jeito de a altura ser SENTIDA num jogo visto de cima.
//
// POR QUE UM PASSE DE TELA E NAO UM BORRAO NO SPRITE: quem sobe nao fica borrado -- o MUNDO
// EMBAIXO e que fica. Aplicar o `Borrao.gdshader` (que e por sprite, no rumo do movimento)
// desfocaria justamente a unica coisa que tem que continuar nitida: o proprio personagem.

uniform sampler2D tela : hint_screen_texture, filter_linear_mipmap;

// 0 no chao, 1 no teto. Quem escreve e o C# a cada quadro.
uniform float altura : hint_range(0.0, 1.0) = 0.0;

// A cor pra onde o mundo puxa la em cima: o azul da distancia.
uniform vec4 ceu : source_color = vec4(0.62, 0.76, 0.95, 1.0);

void fragment() {
	if (altura <= 0.001) {
		// NO CHAO O SHADER NAO FAZ NADA -- e nem paga as amostras. Sem esta saida o passe custaria
		// nove leituras de tela por pixel a vida inteira pra devolver a imagem igual.
		COLOR = vec4(0.0);
	} else {
		// A curva e quadratica de proposito: os primeiros tiles quase nao turvam (pairar tem que
		// continuar sendo uma cena de chao) e o efeito cresce depressa perto do teto, que e onde o
		// jogo quer dizer "voce esta saindo do planeta".
		float f = altura * altura;

		// NOVE AMOSTRAS NUM ANEL. Blur de caixa e mais barato que gaussiano e, com desfoque forte,
		// visualmente indistinguivel -- a diferenca so aparece em raio pequeno, que aqui nao existe.
		vec2 r = SCREEN_PIXEL_SIZE * (14.0 * f);
		vec4 soma = texture(tela, SCREEN_UV);
		soma += texture(tela, SCREEN_UV + vec2( r.x, 0.0));
		soma += texture(tela, SCREEN_UV + vec2(-r.x, 0.0));
		soma += texture(tela, SCREEN_UV + vec2( 0.0,  r.y));
		soma += texture(tela, SCREEN_UV + vec2( 0.0, -r.y));
		soma += texture(tela, SCREEN_UV + r * 0.7);
		soma += texture(tela, SCREEN_UV - r * 0.7);
		soma += texture(tela, SCREEN_UV + vec2(r.x, -r.y) * 0.7);
		soma += texture(tela, SCREEN_UV + vec2(-r.x, r.y) * 0.7);
		vec3 borrada = (soma / 9.0).rgb;

		// A DISTANCIA LAVA A COR. E o mesmo motivo pelo qual montanha longe parece azul: mais ar
		// entre o olho e a coisa.
		vec3 c = mix(borrada, ceu.rgb, f * 0.45);

		// ============================ O MIOLO FICA LIMPO ============================
		// A camera segue o corpo, entao o centro da tela E o personagem. A primeira versao punha
		// 35% de nevoa ali mesmo no teto, e na foto o boneco virou um borrao de tres pixels: quem
		// voava alto perdia de vista a propria posicao.
		//
		// E o corte tambem e o certo pela leitura da cena: o ar turvo esta entre o olho e o CHAO,
		// nao entre o olho e quem esta olhando. Um buraco limpo em volta do corpo e o que faz a
		// altura parecer altura, em vez de tela suja.
		// ============================================================================
		float d = distance(SCREEN_UV, vec2(0.5)) * 1.7;
		float peso = f * (0.05 + 0.95 * smoothstep(0.06, 0.62, d));

		COLOR = vec4(c, peso);
	}
}
