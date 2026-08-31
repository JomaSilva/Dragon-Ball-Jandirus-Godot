shader_type canvas_item;

// ============================================================================================
// A CROSTA DE UM MUNDO QUE ESTA ACABANDO -- vista do espaco.
//
// ============================ O PEDIDO DO DONO, LITERAL ============================
// *"quem ta vendo do espaco o planeta deveria ficar com uns efeitos (pode ser via shaders) na
// mesma ideia dos ferimentos procedurais nos personagens, so q seria um efeito meio avermelhado
// a lembra magma, e rachaduras no planeta, q vai se intensificando durante esses 5 minutos"*.
//
// "NA MESMA IDEIA DOS FERIMENTOS" nao e uma comparacao solta: e a instrucao de seguir aquele
// molde, e ele esta seguido linha a linha. As cinco regras que vieram de `Personagem.gdshader`:
//
//  1. **A MASCARA E O ALFA DO PROPRIO DESENHO** (`c.a > 0.01`), e nao arte de mascara nem um
//     circulo calculado. Isso nao e economia -- e correcao medida: os 16 estados de `Planets.tres`
//     tem QUATRO familias de tamanho (o disco de Vegeta ocupa os 128 px inteiros, o da Terra
//     ocupa 96, o do Ceu ocupa 80) e duas entradas nem sequer sao discos (as estacoes espaciais).
//     Um shader que pintasse "um circulo de raio 0,5 do quadro" acertaria em 4 e erraria em 12.
//     O alfa acerta nos 16 de graca.
//
//  2. **O QUADRO E RECORTADO** (`quadro_min`/`quadro_max`). Todo sprite aqui e `AtlasTexture`, e
//     amostrar fora do retangulo NAO devolve transparente: devolve o QUADRO VIZINHO DA FOLHA. Sem
//     o recorte, uma rachadura na Terra amostraria o desenho de Namek ao lado. O projeto ja levou
//     esse tombo duas vezes (ver `Client/BorraoDirecional`), e a caixa vem de la, pronta.
//
//  3. **O RUIDO MORA NA GRADE DE TEXELS DO SPRITE** (`q = floor(quv * res) / res`). O disco e
//     desenhado com 128 px de arte esticados pra 220..440 px de tela em filtro `Nearest`, ou seja
//     cada pixel de arte vira 1,7 a 3,4 pixels na tela. Ruido amostrado em UV continuo sairia LISO
//     por cima de um desenho visivelmente quadriculado -- o "colado por cima" que o comentario do
//     boneco nomeia. Travado na grade, a rachadura tem a mesma resolucao que o planeta.
//
//  4. **A INTENSIDADE MOVE UM LIMIAR SOBRE UM RUIDO FIXO**, e o limiar NAO DESCE ATE O CHAO. Foi
//     a licao mais cara do boneco: com o limiar indo perto de zero o corpo ficava *repintado* de
//     roxo em vez de machucado. Aqui vale igual -- um planeta inteiramente laranja nao le como
//     planeta rachando, le como planeta trocado por outro sprite.
//
//  5. **A SEMENTE E POR CORPO.** Dois mundos morrendo lado a lado (o dono pode derrubar uma
//     galaxia) com a mesma rachadura denunciariam o efeito como desenho gerado.
//
// ============================ O QUE **NAO** VEIO DO MOLDE, E POR QUE ============================
//   * o `ferida_deitado`: nao ha pose. Um planeta nao e desenhado deitado.
//   * as faixas por REGIAO (cabeca/tronco/pernas): um disco nao tem anatomia. O que substitui e a
//     BORDA da silhueta (ver `borda`, la embaixo), que e a unica divisao que um planeta tem.
//   * o gradiente radial "mais quente no meio": recusado de proposito. Ele exigiria saber o raio
//     REAL do disco dentro do quadro, que e justamente o numero que varia em quatro familias --
//     seria reintroduzir a armadilha 1 pela porta dos fundos.
// ============================================================================================

/// A AGONIA, de 0 a 1. **Ela nao e calculada aqui**: e a MESMA fracao que o servidor usa pra
/// apertar o ceu, encurtar o tremor e derrubar mais chao (`MortePlanetaria.Intensidade`, no Core,
/// chamada pelos dois lados). Um shader que derivasse a propria intensidade do tempo divergiria do
/// planeta que treme embaixo dos pes de quem esta la, e nada apontaria pra isso.
uniform float agonia : hint_range(0.0, 1.0) = 0.0;

/// A SEMENTE DESTE CORPO. Sai da seed do planeta -- ver `PlanetaDesenhado`.
uniform float semente = 0.0;

/// O RECORTE DO QUADRO DENTRO DA FOLHA. Ver a regra 2 la em cima; vem de `BorraoDirecional.Caixa`.
uniform vec2 quadro_min = vec2(0.0);
uniform vec2 quadro_max = vec2(1.0);

/// A rocha derretida no fundo da fenda -- o mais claro e o mais quente.
uniform vec4 cor_magma : source_color = vec4(1.00, 0.52, 0.10, 1.0);

/// A brasa espalhada pela superficie, um degrau abaixo do magma.
uniform vec4 cor_brasa : source_color = vec4(0.80, 0.13, 0.03, 1.0);

/// A crosta queimada: onde nao ha fogo, o mundo ESCURECE. Sem isto o planeta so ficaria mais
/// claro, e "mais claro" le como iluminado, nao como agonizante.
uniform vec4 cor_crosta : source_color = vec4(0.16, 0.09, 0.08, 1.0);

/// O quanto a lava PULSA. Zero = fogo parado, que le como textura pintada e nao como fogo.
uniform float pulso = 0.35;

float pm_hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float pm_ruido(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(pm_hash(i),                  pm_hash(i + vec2(1.0, 0.0)), f.x),
			   mix(pm_hash(i + vec2(0.0, 1.0)), pm_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float pm_fbm(vec2 p) {
	return pm_ruido(p) * 0.60 + pm_ruido(p * 2.3) * 0.30 + pm_ruido(p * 5.1) * 0.10;
}

/// ============================ O `COLOR` DO FRAGMENT **NAO** E O MODULATE ============================
/// No canvas_item do Godot o `COLOR` que chega no fragment ja vem multiplicado pela TEXTURA. Escrever
/// `c * COLOR` no fim seria `textura x textura x modulate` -- a folha ELEVADA AO QUADRADO, que e o
/// tombo medido e documentado em `Personagem.gdshader` (onde ele pintou o cabelo preto do jogo
/// inteiro). No **vertex** a textura ainda nao entrou: guardar o `COLOR` la e o unico jeito de
/// aplicar o modulate do node uma vez so.
///
/// Aqui isso importa de verdade e nao por simetria: o `PlanetaDesenhado` pode ser escurecido pelo
/// `Modulate` (o disco de um mundo morto, a carta acinzentando), e sem este varying a agonia
/// desligaria esse controle calada.
/// ==================================================================================================
varying vec4 cor_do_node;

void vertex() {
	cor_do_node = COLOR;
}

void fragment() {
	vec4 c = texture(TEXTURE, UV);

	if (agonia > 0.001 && c.a > 0.01) {
		vec2 caixa = max(quadro_max - quadro_min, vec2(1e-5));
		vec2 quv = (UV - quadro_min) / caixa;

		// A GRADE DE TEXELS DO PROPRIO QUADRO -- regra 3.
		vec2 res = caixa / TEXTURE_PIXEL_SIZE;
		vec2 q = floor(quv * res) / max(res, vec2(1.0));
		vec2 sem = vec2(semente, semente * 1.7);

		// ============================ A RACHADURA E UMA CRISTA, NAO UMA MANCHA ============================
		// `1 - |n*2 - 1|` transforma o fbm em ruido de CRISTA (*ridged*): o valor vai a 1 exatamente
		// na linha em que o ruido cruza 0,5, e cai dos dois lados. Um limiar alto sobre isso da uma
		// REDE DE LINHAS FINAS -- que e o que uma rachadura e --, enquanto o mesmo limiar sobre o fbm
		// cru daria bolhas. Foi a unica troca de funcao de ruido em relacao ao molde do boneco, e ela
		// e a diferenca entre "planeta rachado" e "planeta com manchas".
		// ==============================================================================================
		float n = pm_fbm(q * 5.0 + sem);
		float crista = 1.0 - abs(n * 2.0 - 1.0);

		// ============================ O LIMIAR PARA EM 0,80, E A FOTO E QUEM DECIDIU ISSO ============================
		// Ele parava em **0,58**, e a checagem numerica adorou: "o disco mudou", "ele avermelhou", a
		// rampa subindo bonitinho. A FOTO mostrou outra coisa -- no auge da agonia o planeta virava um
		// disco de RUIDO AMARELO, sem uma rachadura reconhecivel. Repintado, exatamente o defeito que
		// o `Personagem.gdshader` documenta ter cometido com o roxo do hematoma ("um boneco REPINTADO
		// de roxo, nao um boneco machucado -- vi isso na primeira foto").
		//
		// A causa e a natureza do ruido de CRISTA: `crista = 1 - |2n-1|` fica ALTA onde o fbm passa
		// perto de 0,5 -- e um fbm passa perto de 0,5 quase em todo lugar. Pra ele render LINHA, o
		// limiar tem que ficar perto de 1: em 0,80 so passa onde `|n - 0,5| < 0,10`, que e uma faixa
		// estreita e portanto uma REDE. Em 0,58 passava quase o disco inteiro.
		//
		// (O quadro do meio da agonia ja estava certo pela mesma conta: la o `mix` dava 0,87.)
		// ========================================================================================================
		float tr = mix(1.04, 0.80, agonia);

		// A FENDA e o miolo dela: a fenda e a linha inteira (borda mole, porque rocha nao corta
		// reto); o miolo e a parte mais funda, e so ele acende.
		float fenda = smoothstep(tr, tr + 0.07, crista);
		float miolo = smoothstep(tr + 0.05, tr + 0.22, crista);

		// ============================ A LAVA SO APARECE DEPOIS DO RACHADO ============================
		// A folga e proposital e e o mesmo truque da FASE do hematoma pro sangue no boneco: sem ela o
		// vermelho e a rachadura nasceriam juntos e a rachadura nunca seria uma etapa, seria so a
		// silhueta do vermelho. Aqui o planeta primeiro RACHA (escuro), e so depois a fenda acende.
		// =========================================================================================
		float acende = smoothstep(0.18, 0.62, agonia);

		// O PULSO: a lava respira. `TIME` e local a cada maquina e isso nao e problema -- o brilho de
		// uma fenda nao e estado de jogo, e ninguem consegue comparar o brilho de duas telas.
		float respiro = 1.0 + pulso * sin(TIME * 2.1 + pm_hash(q + sem) * 6.28) * acende;

		// ============================ A BRASA: O CALOR VAZANDO PELA CROSTA ============================
		// Manchas MOLES (borda `smoothstep`), como o hematoma do boneco -- calor atravessando rocha
		// nao tem contorno. Elas entram cedo e dao o "meio avermelhado" que o dono pediu antes de
		// haver rachadura nenhuma; a fenda vem por cima delas.
		// =========================================================================================
		//
		// O LIMIAR DELA PARA EM 0,72 pelo mesmo motivo do da fenda, e medido na mesma foto: em 0,52 a
		// brasa sozinha ja cobria metade do disco, e metade do disco aceso nao e brasa vazando -- e o
		// planeta trocado por outro sprite.
		float nb = pm_fbm(q * 2.6 + sem + 41.0);
		float tb = mix(1.06, 0.72, agonia);
		float brasa = smoothstep(tb, tb + 0.16, nb);

		// ============================ A BORDA DA SILHUETA -- A UNICA "REGIAO" QUE UM DISCO TEM ============================
		// Mesmo idioma do contorno do boneco: um pixel e de borda quando ele e opaco e um vizinho nao
		// e. As amostras sao PRESAS NA CAIXA DO QUADRO pelo mesmo motivo de la -- fora dela o vizinho
		// nao e transparente, e o quadro ao lado.
		//
		// Serve pra o mundo pegar fogo pelas beiradas no fim: e onde a atmosfera queimaria, e e o
		// unico lugar do disco que da pra nomear sem saber o raio dele (ver a recusa do gradiente
		// radial, no cabecalho).
		// ==========================================================================================================
		vec2 p = TEXTURE_PIXEL_SIZE * 2.0;
		float viz = texture(TEXTURE, clamp(UV + vec2( p.x, 0.0), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(-p.x, 0.0), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(0.0,  p.y), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(0.0, -p.y), quadro_min, quadro_max)).a;
		float borda = c.a * (1.0 - viz) * smoothstep(0.45, 1.0, agonia);

		// ---- a pintura, em camadas, da mais fria pra mais quente ----

		// 1. A CROSTA QUEIMA PRIMEIRO. O disco inteiro escurece, e escurece MAIS onde a fenda vai
		//    abrir -- assim a rede de rachaduras ja se le antes de acender.
		//
		//    **O ESCURECIMENTO E METADE DO EFEITO**, e a foto mostrou por que: com 0,35 o planeta
		//    continuava azul e verde com uns riscos dourados por cima, que le como "planeta com
		//    enfeite". Em 0,55 a Terra vira uma bola de carvao com fogo dentro das fendas -- e o
		//    contraste com o carvao e o que faz a fenda parecer QUENTE. Sem ele, so o mais claro
		//    aparece, e "mais claro" le como iluminado.
		c.rgb = mix(c.rgb, cor_crosta.rgb, agonia * 0.55 + fenda * 0.40);

		// 2. A BRASA sob a crosta.
		c.rgb = mix(c.rgb, cor_brasa.rgb * respiro, brasa * acende * 0.75);

		// 3. O MAGMA no fundo da fenda -- o unico que chega a estourar pro claro.
		c.rgb = mix(c.rgb, cor_magma.rgb * respiro, miolo * acende);

		// 4. A ATMOSFERA PEGANDO FOGO na borda, so no fim.
		c.rgb = mix(c.rgb, cor_magma.rgb, clamp(borda * 0.9, 0.0, 1.0));
	}

	COLOR = c * cor_do_node;
}
