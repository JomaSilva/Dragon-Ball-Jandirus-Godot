shader_type canvas_item;

// ============================ A TELA DO ZANZO CLASH ============================
// O embate acontece com os dois corpos INVISIVEIS. Sem isto a tela ficaria vazia por quatro
// segundos, e o jogador estaria apertando letras num quick time event que nao parece ligado a
// nada. Este shader e o "esta acontecendo": raias de velocidade correndo do centro pra fora,
// vinheta escura fechando as bordas, e um pulso que bate a cada cruzamento.
//
// PINTA O ECRA INTEIRO (e um ColorRect que cobre a tela num CanvasLayer), entao aqui `UV` e
// espaco de TELA e nao de atlas -- ao contrario do `Personagem.gdshader`, onde todo sprite e
// AtlasTexture e o UV cru mente. Sem textura, nada a corrigir.
// ==============================================================================

uniform float tempo = 0.0;

/// Sobe pra 1 no instante de cada cruzamento e desce sozinho. E o unico sinal de que os corpos
/// se encontraram -- eles nao aparecem.
uniform float baque : hint_range(0.0, 1.0) = 0.0;

/// A forca geral: 0 apaga tudo. Serve pro fade de entrada e de saida.
uniform float forca : hint_range(0.0, 1.0) = 1.0;

/// O centro das raias, em espaco de tela (0..1). E onde os dois estao brigando.
uniform vec2 foco = vec2(0.5, 0.5);

uniform vec3 cor_raia = vec3(0.62, 0.86, 1.0);
uniform vec3 cor_vinheta = vec3(0.02, 0.03, 0.06);

/// A proporcao da tela, pra as raias sairem em circulo e nao em elipse.
uniform float razao = 1.777;

float ruido(float x) { return fract(sin(x * 127.1) * 43758.5453); }

void fragment()
{
	vec2 d = (UV - foco) * vec2(razao, 1.0);
	float raio = length(d);
	float ang = atan(d.y, d.x);

	// ---- RAIAS DE VELOCIDADE ----
	// Uma faixa por setor angular, cada uma com fase e largura proprias (do `ruido`), correndo pra
	// fora. Sao 48 setores: menos que isso vira ventilador, mais vira chuvisco.
	float setor = floor((ang + 3.14159265) / 6.2831853 * 48.0);
	float fase = ruido(setor);
	float corrida = fract(tempo * (1.4 + fase * 1.6) + fase);

	// a raia so existe entre o miolo e a borda, e some nas duas pontas
	float faixa = smoothstep(0.10, 0.30, raio) * (1.0 - smoothstep(0.55, 0.95, raio));
	float pos = abs(fract((ang + 3.14159265) / 6.2831853 * 48.0) - 0.5);
	float fina = 1.0 - smoothstep(0.06, 0.26, pos);
	float viva = smoothstep(0.0, 0.25, corrida) * (1.0 - smoothstep(0.35, 1.0, corrida));

	float raias = faixa * fina * viva * (0.35 + baque * 0.9);

	// ---- O PULSO DO CRUZAMENTO ----
	// Um anel branco que sai do foco quando os dois se encontram. Ele e o baque: e a unica coisa
	// na tela que diz "aconteceu agora".
	float onda = 1.0 - smoothstep(0.0, 0.09, abs(raio - baque * 0.85));
	onda *= baque;

	// ---- VINHETA ----
	// Fecha as bordas pra que o centro (onde estao a letra e a barra) domine a atencao.
	float vinheta = smoothstep(0.35, 1.05, raio) * 0.85;

	vec3 cor = cor_vinheta * vinheta + cor_raia * (raias + onda * 1.6);
	float alfa = (vinheta + raias * 0.9 + onda) * forca;
	COLOR = vec4(cor, clamp(alfa, 0.0, 1.0));
}
