shader_type canvas_item;

uniform float t : hint_range(0.0, 1.0) = 0.0;   // 0 = nasceu, 1 = morreu
uniform vec4  cor : source_color = vec4(1.0, 0.95, 0.72, 1.0);
uniform float espessura : hint_range(0.005, 0.4) = 0.09;

void fragment() {
    vec2  d = UV - vec2(0.5);
    float r = length(d) * 2.0;                  // 0 no centro, 1 na borda

    float esp  = espessura * (1.0 - t * 0.7);   // o anel afina enquanto abre
    float anel = smoothstep(esp, 0.0, abs(r - t));
    float some = (1.0 - t) * (1.0 - t);

    COLOR = vec4(cor.rgb, anel * some * cor.a);
    COLOR.a *= step(r, 1.0);                    // nao vaza pra fora do circulo
}
