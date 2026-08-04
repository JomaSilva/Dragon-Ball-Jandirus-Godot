shader_type canvas_item;

// 0 = inteiro, 1 = sumiu. E o relogio da miragem.
uniform float desfeito : hint_range(0.0, 1.0) = 0.0;
uniform vec4  cor_da_borda : source_color = vec4(0.55, 0.85, 1.0, 1.0);

// A CAIXA DO QUADRO dentro do atlas. Sem ela a ondulacao abaixo amostra FORA do quadro e
// puxa a pose vizinha da folha -- foi o defeito que o dono viu como "uma leve bugadinha
// virado pra direita e pra baixo". Qual pose vaza depende de quem esta ao lado na folha, e
// isso muda com a direcao; por isso funcionava pra esquerda e pra cima.
uniform vec2 quadro_min = vec2(0.0);
uniform vec2 quadro_max = vec2(1.0);

// RUIDO BARATO E SEM TEXTURA. Nao vale a pena carregar um NoiseTexture2D pra um efeito de
// meio segundo: esta soma de senos ja da um padrao irregular o bastante pro olho ler como
// "desfazendo em pedacos" em vez de "cortando em listras".
float granulado(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	// ONDULACAO: cresce junto com o desfazimento -- comeca parado (e o corpo que acabou de
	// sair) e treme mais conforme vira ar.
	vec2 uv = UV;
	uv.x += sin(uv.y * 40.0 + TIME * 18.0) * 0.006 * desfeito;
	uv = clamp(uv, quadro_min, quadro_max);

	vec4 c = texture(TEXTURE, uv);

	// DE BAIXO PRA CIMA: o `UV.y` entra no limiar, entao os pes se desfazem primeiro e a
	// cabeca por ultimo. Some tudo junto le como fade; em ordem le como dissipar.
	float g = granulado(floor(UV * 48.0));
	float limiar = desfeito * 1.35 - UV.y * 0.28;

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
