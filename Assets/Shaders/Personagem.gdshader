shader_type canvas_item;

// --- o ICON_ADD do BYOND: a APARENCIA depende disto, nao mexer ---
uniform vec3 tinta = vec3(0.0);

/// 0 = SOMA (o ICON_ADD do BYOND, e o padrao), 1 = MATIZ com o sombreado do desenho preservado.
/// Ver o bloco no `fragment` -- e `CharacterVisual.Tingir`, que escreve os dois SEMPRE juntos.
uniform int tinta_modo = 0;

// --- impacto ---
uniform float flash : hint_range(0.0, 1.0) = 0.0;
uniform vec4  flash_cor : source_color = vec4(1.0, 0.95, 0.9, 1.0);
uniform float contorno : hint_range(0.0, 1.0) = 0.0;
uniform vec4  contorno_cor : source_color = vec4(1.0, 0.85, 0.35, 1.0);

// --- o corpo CEDE na direcao do golpe ---
uniform vec2  empurrao = vec2(0.0);
uniform float achatar : hint_range(-0.5, 0.5) = 0.0;

// ============================ FERIDAS ============================
// DUAS COISAS DIFERENTES, e o `modo` decide qual:
//
//   1 = PELE   -> hematoma e sangue pintados por cima. As duas camadas NAO sobem juntas:
//                 primeiro o roxo, e o sangue so quando o membro esta muito ferido.
//   2 = PANO   -> a roupa RASGA: o pixel fica transparente, com a borda desfiada.
//   0 = nada   -> cabelo, olhos e rabo passam batido e nao pagam nada.
//
// O RASGO REVELA A PELE FERIDA, e isso nao e coincidencia: a roupa desenha POR CIMA do
// corpo, entao o buraco na camisa mostra o hematoma que a camada de baixo ja pintou. As
// duas metades do efeito se encaixam sozinhas.
//
// A ordem dos valores e a de `ZonaDeFerida`: cabeca, torso, abdomen, bracos, pernas.
uniform int   ferida_modo = 0;
uniform float f_hema[5];
uniform float f_sang[5];

// A CAIXA DESTE QUADRO dentro da folha. Sem ela nada aqui funciona -- ver o bloco no
// `fragment`, e o comentario de `BorraoDirecional`, que ja explicou o tombo duas vezes.
uniform vec2 quadro_min = vec2(0.0);
uniform vec2 quadro_max = vec2(1.0);

uniform float ferida_semente = 0.0;

// O CORPO ESTA DEITADO DENTRO DO QUADRO?
//
// O sprite de NOCAUTE nao e o boneco em pe girado -- e um desenho PROPRIO, ja deitado, com os pes
// em x=0 e a cabeca em x=0.78. Girar o node resolve a pose NA TELA e nao muda uma linha do que
// esta desenhado no quadro, entao as faixas (cabeca em cima, pernas embaixo) continuavam medindo o
// eixo errado: o sangue da perna aparecia na cabeca.
//
// Medido nos 48 quadros do corpo: SO o `ko` deita. Voo, treino e meditacao sao desenhos em pe --
// neles o node gira, o UV gira junto, e nada aqui precisa mudar.
uniform float ferida_deitado : hint_range(0.0, 1.0) = 0.0;

// MEMBRO ARRANCADO SOME. Em coordenada de TELA, e nao do corpo: (x = metade esquerda da imagem,
// y = metade direita). Quem traduz "o braco esquerdo DELE" pra "o lado esquerdo da IMAGEM" e o
// cliente, que sabe pra onde o boneco esta virado -- de frente os lados trocam, como num espelho.
//
// So braco e perna. Arrancar cabeca, torso ou abdomen MATA na hora: nao existe corpo andando sem
// eles pra desenhar, e por isso nao ha bit pra eles -- ficam so com o sangue no maximo.
uniform vec2 amp_braco = vec2(0.0);
uniform vec2 amp_perna = vec2(0.0);

// AS CORES sao as da referencia do jogo anterior, DESSATURADAS pra 32x32: num sprite deste
// tamanho um roxo saturado nao le como hematoma, le como PERSONAGEM ROXO. Hematoma de verdade e
// acinzentado, entre o vinho e o mofo.
uniform vec3 cor_sangue   : source_color = vec3(0.55, 0.06, 0.06);
uniform vec3 cor_hematoma : source_color = vec3(0.38, 0.19, 0.26);
uniform vec3 cor_hematoma2: source_color = vec3(0.29, 0.23, 0.31);
uniform float sombreado : hint_range(0.0, 1.0) = 0.845;
uniform float angulo_arranhao = 132.845;

float fer_hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float fer_ruido(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(fer_hash(i),                 fer_hash(i + vec2(1.0, 0.0)), f.x),
			   mix(fer_hash(i + vec2(0.0, 1.0)), fer_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fer_fbm(vec2 p) {
	return fer_ruido(p) * 0.60 + fer_ruido(p * 2.3) * 0.30 + fer_ruido(p * 5.1) * 0.10;
}

void vertex() {
	// Centered = true: o quad ja esta centrado em (0,0) em pixels locais
	VERTEX.x *= (1.0 + achatar);
	VERTEX.y *= (1.0 - achatar);
	VERTEX   += empurrao;
}

void fragment() {
	vec4 c = texture(TEXTURE, UV);

	// ============================ DOIS MODOS DE TINGIR ============================
	// MODO 0 (SOMA) e o padrao e o de sempre -- o `ICON_ADD` do BYOND. Ele clareia sem apagar o
	// desenho, que e o que corpo, cabelo e olho precisam (base escura, realce desenhado). Como e o
	// valor padrao do uniform, camada que nunca escreve o modo continua exatamente como era.
	//
	// MODO 1 (MATIZ) e o da ROUPA. Somar nao serve pra um catalogo que ja vem pintado: gi branco
	// mais azul continua branco, gi vermelho mais azul vira magenta. Aqui a LUMINANCIA do pixel
	// (o sombreado que o artista desenhou: dobras, vincos, sombra) e mantida como intensidade, e a
	// COR e substituida. Cinza medio devolve a cor pedida; a dobra escura devolve a cor escura; o
	// realce estoura pro claro. A peca troca de cor de verdade e continua parecendo pano.
	// ==============================================================================
	if (tinta_modo == 1) {
		float luz = dot(c.rgb, vec3(0.299, 0.587, 0.114));
		c.rgb = clamp(tinta * (luz * 2.0), 0.0, 1.0);
	} else {
		c.rgb = clamp(c.rgb + tinta, 0.0, 1.0);
	}

	// CONTORNO: acende onde o pixel e opaco e um vizinho e transparente
	//
	// AS AMOSTRAS SAO PRESAS NA CAIXA DO QUADRO. Sem isso, o vizinho de um pixel na borda do
	// quadro nao e "transparente" -- e o QUADRO AO LADO na folha, e o contorno some justamente
	// onde o desenho encosta na borda. E o mesmo tombo que o `BorraoDirecional` documenta ter
	// levado duas vezes; este bloco tinha ficado pra tras porque a caixa nao existia aqui.
	if (contorno > 0.0) {
		vec2 p = TEXTURE_PIXEL_SIZE;
		float viz = texture(TEXTURE, clamp(UV + vec2( p.x, 0.0), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(-p.x, 0.0), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(0.0,  p.y), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(0.0, -p.y), quadro_min, quadro_max)).a;
		c.rgb = mix(c.rgb, contorno_cor.rgb, c.a * (1.0 - viz) * contorno);
	}

	// ============================ AS FERIDAS ============================
	if (ferida_modo > 0 && c.a > 0.01) {
		// UV DO QUADRO, e nao do atlas.
		//
		// Todo sprite deste jogo e um AtlasTexture: `UV` cobre a FOLHA inteira, nao o
		// desenho. Usar `UV` cru aqui poria a "cabeca" na altura de qualquer coisa,
		// dependendo de onde a pose caiu na folha -- e mudaria a cada quadro de animacao.
		vec2 caixa = max(quadro_max - quadro_min, vec2(1e-6));
		vec2 quv = (UV - quadro_min) / caixa;

		// ---- do quadro pro CORPO ----
		// Em pe os dois eixos coincidem. Deitado, o corpo corre no X com a cabeca no x ALTO: girar
		// 90 graus e inverter poe cabeca em cima e pes embaixo de novo, e todo o resto deste bloco
		// continua valendo sem precisar saber que o boneco caiu.
		vec2 fuv = mix(quv, vec2(quv.y, 1.0 - quv.x), ferida_deitado);

		// ---- que regiao do corpo e este pixel ----
		//
		// ESTES NUMEROS FORAM MEDIDOS, nao estimados. Varri o alfa dos quadros de `default_south`
		// e `walk_south` do corpo jogavel (32x32): a silhueta e a mesma em todos e simetrica em
		// x = 0.5.
		//
		//   linhas  6..15  -> CABECA          y 0.19 .. 0.50, largura x 0.34 .. 0.66
		//   linhas 16..25  -> TRONCO+BRACOS   y 0.50 .. 0.81, largura x 0.25 .. 0.75
		//   linhas 26..31  -> PERNAS          y 0.81 .. 1.00, largura x 0.34 .. 0.66
		//
		// Ou seja: o que TRANSBORDA da coluna central (|x-0.5| > 0.16) na faixa do meio sao os
		// BRACOS -- cabeca e pernas ocupam exatamente a mesma coluna do tronco. Meu primeiro palpite
		// punha a cabeca ate 0.32 e as pernas a partir de 0.70; a cabeca real vai ate a METADE do
		// sprite, e chutar isso teria posto ferida de cabeca no peito de todo mundo.
		//
		// As bordas sao SUAVES pra duas regioes vizinhas nao criarem uma linha reta na divisa.
		float meio = abs(fuv.x - 0.5);
		float braco = smoothstep(0.145, 0.175, meio);

		float w_cab = 1.0 - smoothstep(0.47, 0.53, fuv.y);
		float w_per = smoothstep(0.78, 0.84, fuv.y);
		float tronco = (1.0 - w_cab) * (1.0 - w_per);
		float baixo = smoothstep(0.66, 0.72, fuv.y);     // abdomen: o terco de baixo do tronco
		float w_abd = tronco * baixo * (1.0 - braco);
		float w_tor = tronco * (1.0 - baixo) * (1.0 - braco);
		float w_bra = tronco * braco;

		float hema = f_hema[0]*w_cab + f_hema[1]*w_tor + f_hema[2]*w_abd
				   + f_hema[3]*w_bra + f_hema[4]*w_per;
		float sang = f_sang[0]*w_cab + f_sang[1]*w_tor + f_sang[2]*w_abd
				   + f_sang[3]*w_bra + f_sang[4]*w_per;

		// ---- O QUE FOI ARRANCADO NAO E DESENHADO ----
		// Antes de qualquer tinta: um membro que nao existe nao fica roxo, ele FALTA. Vale pra pele
		// e pro pano na mesma linha -- a manga vai junto com o braco.
		//
		// ============================ MASCARA DURA, E NAO O PESO DA FAIXA ============================
		// A primeira versao multiplicava pelos pesos `w_bra`/`w_per`, que sao SUAVIZADOS (smoothstep)
		// pra a pintura nao criar uma linha reta na divisa entre duas regioes. Suavizar e certo pra
		// TINTA e errado pra AUSENCIA: onde o peso ficava abaixo de 0.5 -- o ombro, o punho, a coluna
		// mais externa -- o pixel sobrevivia, e o dono viu exatamente isso: "no braco q vc arrancou a
		// pontinha dele ainda ta ali, acho q e so 1 pixel".
		//
		// Aqui a divisao e por `step`, sem meio-termo, e as bordas sao GENEROSAS: o membro arrancado
		// nao deixa sobra. A coluna central do sprite (|x-0.5| <= 0.165) e tronco/cabeca/pernas; o que
		// passa disso na faixa do meio e braco.
		// ============================================================================================
		float lado = step(0.5, fuv.x);          // 0 = metade esquerda da imagem, 1 = direita
		float fora = step(0.165, meio);         // fora da coluna central = braco
		// O BRACO e a coluna de FORA na faixa do meio; a PERNA e tudo que esta embaixo.
		//
		// A perna NAO se restringe a coluna central, e essa foi a segunda sobra que o dono viu: os
		// PES sao mais largos que as pernas (as duas ultimas linhas do sprite chegam a |x-0.5| = 0.19,
		// contra 0.16 da canela), entao filtrar por coluna deixava a ponta do pe pra tras. Abaixo de
		// 0.795 nao existe outra coisa no boneco alem de perna e pe -- o lado ja basta pra separar as
		// duas, e elas se encontram exatamente no meio.
		float so_braco = step(0.46, fuv.y) * (1.0 - step(0.80, fuv.y)) * fora;
		float so_perna = step(0.795, fuv.y);
		float perdeu = so_braco * mix(amp_braco.x, amp_braco.y, lado)
					 + so_perna * mix(amp_perna.x, amp_perna.y, lado);
		c.a *= 1.0 - step(0.5, perdeu);

		if (c.a > 0.01 && (hema > 0.001 || sang > 0.001)) {
			// ---- o ruido MORA NO PIXEL DO SPRITE ----
			// Amostrar em UV continuo daria manchas borradas sobre um desenho de pixel
			// duro -- o efeito ficaria "colado por cima" em vez de fazer parte do boneco.
			// Travando a coordenada na grade de texels do proprio quadro, cada mancha tem
			// a mesma resolucao que o resto do desenho.
			vec2 res0 = caixa / TEXTURE_PIXEL_SIZE;
			vec2 res = mix(res0, res0.yx, ferida_deitado);
			vec2 q = floor(fuv * res) / max(res, vec2(1.0));
			vec2 sem = vec2(ferida_semente, ferida_semente * 1.7);

			if (ferida_modo == 2) {
				// ================= PANO: A ROUPA RASGA =================
				// O estrago da roupa segue o mesmo apanhado do membro debaixo dela -- e
				// quem manda e o PIOR dos dois, porque tecido rasga desde a pancada que so
				// deixa roxo ate a que abre a pele.
				float dano = max(hema, sang);

				float nr = fer_fbm(q * 7.0 + sem + 53.0);
				float tr = mix(1.05, 0.42, dano);

				// BURACO de borda dura: o furo e o pixel que passou do limiar.
				float furo = step(tr, nr);
				// DESFIADO: a franja logo antes do limiar nao some, escurece -- e o que
				// separa "rasgado" de "recortado com tesoura".
				float franja = smoothstep(tr - 0.06, tr, nr) * (1.0 - furo);

				c.a *= 1.0 - furo;
				c.rgb *= 1.0 - franja * 0.45;
			} else {
				// ================= PELE: HEMATOMA E SANGUE =================
				// ---- HEMATOMA: manchas MOLES, duas cores ----
				// O LIMIAR NAO DESCE ATE O CHAO, e isso importa: com `mix(..., 0.18, hema)` o
				// hematoma cheio passava em quase todo pixel e o resultado era um boneco
				// REPINTADO de roxo, nao um boneco machucado -- vi isso na primeira foto. Parando
				// em 0.42 sobra pele limpa entre as manchas, e e o contraste com a pele limpa que
				// faz uma mancha ler como mancha.
				float nh = fer_fbm(q * 6.0 + sem);
				float th = mix(1.02, 0.42, hema);
				float m_h = smoothstep(th, th + 0.12, nh);

				vec3 hcol = mix(cor_hematoma2, cor_hematoma, fer_ruido(q * 3.0 + sem + 9.0));

				// ---- ARRANHOES: listras numa direcao, quebradas por ruido ----
				float ang = radians(angulo_arranhao);
				vec2 dir = vec2(cos(ang), sin(ang));
				vec2 dirUV = vec2(dot(q, dir), dot(q, vec2(-dir.y, dir.x))) * 22.0;
				float listras = smoothstep(0.72, 0.92, sin(dirUV.y) * 0.5 + 0.5);
				listras *= fer_fbm(dirUV * 0.18 + sem);      // o `breakup` do original
				float m_arr = clamp(listras * 1.1, 0.0, 1.0) * m_h;
				vec3 arrcol = mix(hcol * 1.2, vec3(1.0, 0.7, 0.7), 0.3);
				hcol = mix(hcol, arrcol, m_arr);

				// ---- SANGUE: limiar DURO, borda de pixel ----
				// `step` e nao `smoothstep`: respingo de sangue tem borda, e borda macia
				// num sprite de pixel le como borrao de compressao.
				// Mesma razao do hematoma, e mais forte ainda: sangue e RESPINGO. Cobrir a perna
				// inteira de vermelho le como calca vermelha.
				float nb = fer_fbm(q * 8.0 + sem + 31.0);
				float tb = mix(1.02, 0.53, sang);
				float m_s = step(tb, nb);

				// A LUZ DO DESENHO ATRAVESSA. Sem isto a ferida vira adesivo chapado: o
				// sombreado do sprite (musculo, dobra, vinco) some debaixo dela.
				float luz = dot(c.rgb, vec3(0.299, 0.587, 0.114));
				float ganho = mix(1.0, clamp(luz * 2.0, 0.35, 1.45), sombreado);

				c.rgb = mix(c.rgb, hcol * ganho, m_h * 0.88);
				c.rgb = mix(c.rgb, cor_sangue * ganho, m_s);
			}
		}
	}

	// FLASH por MISTURA, nao por multiplicacao: preserva a silhueta e o desenho.
	// O `modulate` que fazia isto antes ESCURECIA o boneco -- levar um soco deixava
	// o personagem mais apagado, que e o oposto do que um impacto deve parecer.
	c.rgb = mix(c.rgb, flash_cor.rgb, flash * step(0.01, c.a));

	COLOR = c;
}
