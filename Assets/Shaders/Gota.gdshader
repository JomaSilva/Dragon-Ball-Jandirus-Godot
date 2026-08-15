shader_type canvas_item;

// ============================ A GOTA NA AGUA ============================
// *"faca um SHADER legal q deixa a TELA ONDULANDO IGUAL UMA GOTA quando cai na agua"*.
//
// Ele nao desenha nada: le o que ja foi desenhado (`hint_screen_texture`) e devolve DESLOCADO --
// o mesmo mecanismo da nevoa de altitude (`Altitude.gdshader` + `Client/NevoaDeAltitude.cs`), na
// mesma camada 0, que e a do MUNDO. A interface fica de fora e continua legivel.
//
// A MATEMATICA VEIO DO `Transformacao.gdshader`, que ja tinha o anel radial em espaco de tela com
// a correcao de proporcao. O que muda aqui e o que separa "uma explosao" de "uma gota":
//   * la e UM anel com crista dourada; aqui sao VARIOS, e sem cor -- agua nao brilha de amarelo;
//   * la o raio e escrito de fora quadro a quadro; aqui a frente anda sozinha com `fase`, e os
//     aneis nascem ATRAS dela, que e como a agua se comporta (adiante da frente o lago esta calmo).
// ========================================================================
uniform sampler2D tela : hint_screen_texture, repeat_disable, filter_linear;

// 0 no instante do impacto, 1 quando a onda morreu. Quem escreve e o `GotaNaTela`, por relogio.
uniform float fase = 0.0;

// Deslocamento maximo, em UV. 0,03 = ~3% da tela na crista mais forte -- o bastante pra o cenario
// "nadar" sem virar borrao ilegivel.
uniform float forca = 0.030;

// largura/altura da janela, pra o anel sair REDONDO e nao uma elipse deitada.
uniform float proporcao = 1.777;

uniform vec2 centro = vec2(0.5);

// Ate onde a frente da onda chega. 1,3 passa da quina (o canto mais distante do centro, ja com a
// proporcao aplicada, fica em ~1,0): a onda SAI da tela em vez de sumir dentro dela.
const float ALCANCE = 1.3;

// Comprimento de onda em UV. 0,145 poe ~2,5 aneis visiveis dentro da cauda que o DECAI deixa.
const float FREQUENCIA = 43.0;

// Quao rapido o anel morre atras da frente. Sem isto a tela inteira ondularia de uma vez, que e
// interferencia de piscina e nao gota.
const float DECAI = 5.0;

void fragment() {
	// A DISTANCIA E CORRIGIDA, o deslocamento NAO. Medir em espaco redondo e o que faz o anel ser
	// um circulo; empurrar o pixel na direcao crua e o que faz ele voltar pro lugar certo da tela.
	vec2 d = UV - centro;
	d.x *= proporcao;
	float dist = length(d);

	// A FRENTE DESACELERA (`pow(fase, 0.65)`): a gota bate forte e a energia se espalha cada vez
	// mais devagar. Linear parece um circulo desenhado por computador; este parece agua.
	float frente = ALCANCE * pow(fase, 0.65);

	// ATRAS DA FRENTE HA ONDA, ADIANTE DELA HA CALMARIA. `dif > 0` = a frente ja passou por aqui.
	float dif = frente - dist;
	float cauda = step(0.0, dif) * exp(-max(dif, 0.0) * DECAI);

	// OS ANEIS. Como `dif` cresce junto com a frente, as cristas (valores fixos de `dif`) caminham
	// pra FORA sozinhas -- nao ha um segundo termo de tempo aqui, e nao pode haver: dois relogios
	// somados dariam batimento, e o anel andaria em soquinhos.
	float onda = sin(dif * FREQUENCIA);

	// E A ONDA INTEIRA MORRE NO FIM. Ao quadrado pra ela segurar a forca no comeco e apagar de vez
	// no ultimo terco -- linear deixaria um resto visivel bem na hora da troca de zona.
	float vida = (1.0 - fase) * (1.0 - fase);
	float amp = onda * cauda * vida;

	vec2 dir = dist > 0.0001 ? normalize(UV - centro) : vec2(0.0);
	vec3 c = texture(tela, SCREEN_UV + dir * amp * forca).rgb;

	// A REFRACAO. A crista da onda concentra luz e o vale a espalha -- e o que faz o olho ler
	// "agua" em vez de "a tela esta tremendo". Sutil de proposito: 0,22 sobre uma amplitude que ja
	// e menor que 1 nunca estoura o branco da dimensao mental.
	c *= 1.0 + amp * 0.22;

	COLOR = vec4(c, 1.0);
}
