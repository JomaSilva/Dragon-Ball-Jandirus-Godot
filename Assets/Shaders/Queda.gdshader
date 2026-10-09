shader_type canvas_item;

// ============================ O QUE CAI NAO CAI DENTRO DE CASA ============================
// O material das particulas do clima (`ClimaNaTela._queda`). Ele nao desenha nada novo: o floco e
// o pingo saem como o motor ja os faz (a textura vezes a cor da particula). A unica coisa que ele
// acrescenta e APAGAR o fragmento que cai sobre uma celula sob teto -- a area `Inside` do
// original, onde o icone do clima nunca e desenhado (ver `Core/World/CelulaInterna.cs`).
//
// POR PIXEL, E NAO POR PARTICULA: no DM o clima e um ladrilho desenhado por turf, entao o corte e
// na divisa da celula -- o floco que cruza a parede some no telhado e continua caindo do lado de
// fora. Matar a particula inteira pelo ponto em que ela esta daria uma franja sem neve em volta
// do predio e flocos inteiros por cima da beirada dele.
//
// ONDE O FRAGMENTO ESTA NO MUNDO sai da tela: `SCREEN_UV` vezes o pedaco de mundo que a camera
// cobre. Quem escreve `teto_origem` e `teto_tamanho` e o `ClimaNaTela`, no ultimo instante antes
// de o quadro ser desenhado (`FramePreDraw`): um quadro de atraso da camera aqui seria a mascara
// escorregando sobre o predio enquanto o jogador anda.
// ===========================================================================================

/// O plano do que esta sob teto: UM TEXEL POR CELULA, 1 = sob teto. Sem filtro, de proposito --
/// interpolar aqui borraria o corte por meia celula pra cada lado da parede.
uniform sampler2D teto : filter_nearest, repeat_disable;

/// O tamanho do mapa em celulas, que e o tamanho da textura.
uniform vec2 teto_celulas = vec2(1.0);

/// O canto superior esquerdo da tela em pixels de mundo, e o quanto de mundo ela cobre.
uniform vec2 teto_origem = vec2(0.0);
uniform vec2 teto_tamanho = vec2(1280.0, 720.0);

/// 1 = recorta. 0 = nao ha teto nesta zona, ou o clima de agora nao respeita teto (a destruicao
/// do planeta -- ver `CelulaInterna.TiraOClima`).
uniform float teto_recorta = 0.0;

/// O lado de uma celula em pixels de mundo -- o `ZoneCollision.TileSize`.
const float CELULA = 32.0;

void fragment()
{
	// `COLOR` JA CHEGA PRONTO (a textura vezes a cor da particula): so o alfa e mexido. Reescrever
	// a cor aqui jogaria fora a tinta de cada clima, que vem pelo `ParticleProcessMaterial`.
	vec2 mundo = teto_origem + SCREEN_UV * teto_tamanho;
	float sob = texture(teto, mundo / (teto_celulas * CELULA)).r;
	COLOR.a *= 1.0 - sob * teto_recorta;
}
