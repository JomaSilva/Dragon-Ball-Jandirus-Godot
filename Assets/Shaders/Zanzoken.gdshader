shader_type canvas_item;

// 0 = inteiro, 1 = sumiu. E o relogio da miragem.
uniform float desfeito : hint_range(0.0, 1.0) = 0.0;
uniform vec4  cor_da_borda : source_color = vec4(0.55, 0.85, 1.0, 1.0);

// A CAIXA DO QUADRO dentro do atlas. So serve pra AMOSTRAR: sem ela a ondulacao le fora do
// quadro e puxa a pose vizinha da folha.
uniform vec2 quadro_min = vec2(0.0);
uniform vec2 quadro_max = vec2(1.0);

// ============================ O CORPO E UM SO, E ESTE E O ESPACO DELE ============================
// O personagem nao e um sprite: e uma PILHA (corpo, roupa, cabelo, rabo), cada camada com a
// propria folha e o proprio quadro dentro dela. O `UV` que chega aqui e coordenada de ATLAS, e
// portanto DIFERENTE em cada camada e em cada direcao -- o quadro do cabelo virado pra leste cai
// numa linha da folha; o do corpo, noutra.
//
// A dissolucao lia `UV.y` direto. Com isso o limiar de cada camada saia de um lugar diferente da
// FOLHA em vez do mesmo lugar do CORPO: virado pra direita e pra baixo o cabelo cruzava o limiar
// antes do resto e o vulto ficava careca por alguns quadros; pra esquerda e pra cima os quadros
// caiam em linhas parecidas e o defeito nao aparecia. Foi por isso que a tentativa anterior --
// grampear a UV no quadro -- consertou o VAZAMENTO da pose vizinha e nao consertou a careca:
// eram dois defeitos com a mesma origem, e so um deles era de amostragem.
//
// `corpo_origem`/`corpo_tamanho` chegam prontos POR CAMADA, ja descontada a posicao dela dentro
// da pilha (ver `Zanzoken.Deixar`), entao `p` abaixo e o MESMO ponto do corpo pra todas elas.
// A dissolucao e o ruido passam a correr num espaco so, que e o que faz a pilha se desfazer como
// uma pessoa em vez de como quatro adesivos empilhados.
// =================================================================================================
uniform vec2 corpo_origem = vec2(0.0);
uniform vec2 corpo_tamanho = vec2(32.0, 32.0);

varying vec2 local;

// RUIDO BARATO E SEM TEXTURA. Nao vale a pena carregar um NoiseTexture2D pra um efeito de
// meio segundo: esta soma de senos ja da um padrao irregular o bastante pro olho ler como
// "desfazendo em pedacos" em vez de "cortando em listras".
float granulado(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void vertex() {
	// A POSICAO NO PROPRIO SPRITE, em pixels. E o unico jeito de sair da coordenada de atlas sem
	// depender de onde a miragem esta no mundo -- ela SOBE enquanto se desfaz, e um espaco preso
	// ao mundo faria o padrao escorregar por baixo dela.
	local = VERTEX;
}

void fragment() {
	// ONDE ESTE PIXEL ESTA NO CORPO, de 0 (topo) a 1 (pes). Igual em todas as camadas.
	vec2 p = clamp((local - corpo_origem) / max(corpo_tamanho, vec2(1.0)), vec2(0.0), vec2(1.0));

	// ONDULACAO: cresce junto com o desfazimento -- comeca parado (e o corpo que acabou de
	// sair) e treme mais conforme vira ar.
	//
	// A AMPLITUDE E FRACAO DO QUADRO, e nao um numero fixo em UV de folha: uma folha larga fazia
	// 0,006 valer muitos pixels e outra estreita fazia valer quase nada, entao camadas vindas de
	// folhas de tamanhos diferentes tremiam com forcas diferentes.
	vec2 uv = UV;
	uv.x += sin(p.y * 40.0 + TIME * 18.0) * (quadro_max.x - quadro_min.x) * 0.2 * desfeito;
	uv = clamp(uv, quadro_min, quadro_max);

	vec4 c = texture(TEXTURE, uv);

	// ============================ DE BAIXO PRA CIMA, E AGORA DE VERDADE ============================
	// Os pes se desfazem primeiro e a cabeca por ultimo. Some tudo junto le como fade; em ordem le
	// como dissipar.
	//
	// O SINAL ESTAVA TROCADO. Em coordenada de tela o Y cresce PRA BAIXO, entao `p.y` vale 0 na
	// cabeca e 1 nos pes -- subtrair `p.y` do limiar fazia o TOPO cruzar antes, ou seja, a cabeca
	// (e o cabelo, que e a camada mais alta de todas) se desfazendo primeiro. Era metade do
	// defeito de "vulto careca": mesmo com todas as camadas no mesmo espaco, a ordem estava
	// invertida e o cabelo continuaria saindo na frente.
	//
	// Com `1.0 - p.y`, o cabelo passa a ser a ULTIMA coisa a sumir -- que e a ordem certa e, de
	// quebra, torna a careca impossivel por construcao.
	// ================================================================================================
	float g = granulado(floor(p * 48.0));
	float limiar = desfeito * 1.35 - (1.0 - p.y) * 0.28;

	if (g < limiar - 0.16) { COLOR = vec4(0.0); }
	else {
		// A BORDA DA DISSOLUCAO ACENDE. E o unico lugar em que o vulto tem cor propria, e e
		// o que faz ele parecer feito de ki e nao um fantasma cinza.
		float fio = 1.0 - smoothstep(0.0, 0.16, g - (limiar - 0.16));
		c.rgb = mix(c.rgb, cor_da_borda.rgb, fio * 0.85);
		c.a  *= (1.0 - desfeito * 0.85);
		COLOR = c;
	}
}
