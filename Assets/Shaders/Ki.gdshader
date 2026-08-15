shader_type canvas_item;

// ============================ O `icon += rgb(...)` DO BYOND, E SO ELE ============================
// `beams.dm:130-132` e `blasts.dm:56`: a folha do ataque de ki e CINZA e o jogo SOMA a cor do ki de
// quem atirou por cima dela (`ICON_ADD`). E a mesma receita da aura -- e por isso o Kamehameha de um
// e azul e o do outro e vermelho sem existirem duas folhas.
//
// MULTIPLICAR (o reflexo natural em Godot, `Modulate`) NAO SERVE, e a razao e a mesma que o
// `Personagem.gdshader` ja documenta pro cabelo: onde a folha e escura, multiplicar zera; um raio
// com nucleo branco e bordas escuras viraria um traco fino e sem halo. Somar clareia mantendo o
// desenho -- que e o que a arte foi feita pra receber.
//
// ============================ POR QUE UM SHADER PROPRIO E NAO O DO PERSONAGEM ============================
// O `Personagem.gdshader` faz exatamente esta soma no `tinta_modo = 0`, e a tentacao era reusa-lo.
// Ele carrega junto ferida, amputacao, contorno de forma, empurrao e achatamento -- doze uniforms
// que um tiro nunca escreve, e um leitor futuro teria que provar que nenhum deles vale aqui. Este
// arquivo tem uma conta e nao tem o que provar.
// ====================================================================================================

uniform vec3 tinta = vec3(0.0);

void fragment() {
	vec4 c = texture(TEXTURE, UV);

	// A SOMA SO VALE ONDE HA DESENHO. Sem o `c.a` no fator, o pixel transparente ganharia cor (e
	// alfa zero, entao ele nao aparece) -- mas o retangulo inteiro passaria a somar em qualquer
	// mistura aditiva que alguem ligue depois. Prender a tinta ao alfa deixa o efeito igual ao do
	// BYOND, onde a soma acontece DENTRO do icone.
	c.rgb = clamp(c.rgb + tinta * c.a, 0.0, 1.0);

	// `COLOR = c` E NAO `c * COLOR`, e aqui isso e CERTO -- ao contrario do `Personagem.gdshader`,
	// onde a mesma linha descartava o `modulate` de tres coisas escritas e documentadas. A diferenca
	// e que la o node TEM modulate a preservar (a colada da forma, o tom do Namekuseijin, o vulto do
	// Zanzoken); aqui o node do tiro e sempre branco e a unica cor que existe e a `tinta`. Ler o
	// `COLOR` que chega seria pior que inutil: no canvas_item ele ja vem multiplicado pela textura,
	// entao `c * COLOR` desenharia a folha AO QUADRADO -- o tombo que aquele arquivo mediu na GPU.
	//
	// O CICLO DO DIA CONTINUA VALENDO: o `CanvasModulate` do mundo e aplicado pelo Godot DEPOIS do
	// fragment, e nenhuma linha daqui escapa dele -- tambem medido, no mesmo arquivo.
	COLOR = c;
}
