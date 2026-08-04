shader_type canvas_item;
uniform vec3 cor : source_color = vec3(1.0);
uniform float forca : hint_range(0.0, 2.0) = 1.0;

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	// a aura e monocromatica na folha: o canal mais forte vira a INTENSIDADE, e a cor
	// vem de fora. Assim o mesmo desenho serve pra dourado, vermelho e azul.
	float i = max(max(c.r, c.g), c.b);
	COLOR = vec4(cor * i * forca, c.a * forca);
}
