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

// ============================ O CONTORNO DA FORMA ============================
// Um segundo contorno, PERMANENTE enquanto a transformacao dura, na cor dela.
//
// E separado do `contorno` acima porque os dois querem coisas diferentes: aquele e o do IMPACTO,
// um lampejo de 0,15 s; este e o da forma, que fica horas ligado. Compartilhar o uniform faria cada
// soco levado apagar a marca do Super Saiyajin por um sexto de segundo -- e o impacto ficaria
// invisivel em quem esta transformado.
//
// ============================ ELE E UMA LINHA, E NAO UM BRILHO ============================
// Palavras do dono: *"oq brilha nao e o contorno e sim a aura (isso vale pra TODAS AS FORMAS)"*.
//
// Isto derrubou as duas metades do efeito anterior:
//
//   * o HALO POR FORA morreu. Ele escrevia alfa onde nao havia desenho, e era ele que o dono via
//     "amarronzado por cima do verde do chao": um halo aditivo sobre grama e cor de aura mais cor
//     de chao, e o resultado nao e nem uma nem outra. Quem tem que brilhar por fora do corpo e a
//     aura, que ja e um sprite com luz propria (`SpriteDeAura` + a `PointLight2D` do node `Aura`).
//   * a SOMA DE LUZ virou MISTURA. Somar clareia o pixel e vira brilho; misturar PINTA. O contorno
//     agora e um traco da cor da forma na borda da silhueta -- desenho, nao iluminacao.
//
// PERDA CONHECIDA E ACEITA: dentro da silhueta o traco depende de contraste, e o corpo padrao e
// claro. Um amarelo em cima de pele clara le fraco -- foi por isso que o halo existiu. A resposta
// do dono e que essa leitura e trabalho da aura, e nao deste canal.
//
// O `aura_pulso` FOI DELETADO junto: um uniform que ninguem escreve e campo morto.
//
// O CONTORNO VOLTOU A PULSAR (pedido do dono: "um pouco mais fraco e ... pulsando lentamente"), e
// mesmo assim o uniform NAO voltou -- quem oscila e o `aura` aqui embaixo, escrito quadro a quadro
// pelo `CharacterVisual.ForcaNaFaseDoPulso`. Duas razoes: um relogio de shader nao para quando o
// contorno apaga (e a regra do dono e que ele so acende acima de 100% de Ki, sem gastar quadro
// abaixo disso), e a fase tem que ser a MESMA em todas as camadas do boneco -- coisa que so quem ve
// a pilha inteira consegue garantir.
//
// ============================ E ELE NAO VALE EM TODA CAMADA ============================
// `borda = c.a * (1 - viz)` quer dizer "pixel opaco encostado em vazio", e isso so descreve um fio
// de contorno em desenho GRANDE. Num sprite de detalhe -- os OLHOS -- quase todo pixel tem vizinho
// transparente, entao `borda` da ~1 no desenho inteiro e a mistura pinta o olho de ponta a ponta
// (defeito visto pelo dono). NAO HA CONSERTO AQUI: quem decide e a lista de quem recebe o uniform,
// e ela mora no `CharacterVisual.EhSilhueta`. Este shader continua fazendo a conta certa.
// ==========================================================================================
uniform float aura : hint_range(0.0, 2.0) = 0.0;
uniform vec4  aura_cor : source_color = vec4(1.0, 0.85, 0.35, 1.0);

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

/// O MODULATE DO NODE, PEGO ONDE ELE AINDA E SO ELE. Ver o bloco do `COLOR` no fim do `fragment`:
/// la em baixo o `COLOR` ja vem multiplicado pela textura, e usa-lo desenhava a folha ao quadrado.
///
/// E ELE E MESMO SO O NODE, medido e nao suposto: o `CanvasModulate` do mundo NAO entra aqui -- o
/// Godot o aplica DEPOIS do fragment. Ver o fim do `fragment`, no bloco "O QUE O AMBIENTE FAZ".
varying vec4 cor_do_node;

void vertex() {
	// Centered = true: o quad ja esta centrado em (0,0) em pixels locais
	VERTEX.x *= (1.0 + achatar);
	VERTEX.y *= (1.0 - achatar);
	VERTEX   += empurrao;

	// AQUI o `COLOR` e o `modulate` x `self_modulate` x cor do vertice, SEM textura nenhuma.
	cor_do_node = COLOR;
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
	if (contorno > 0.0 || aura > 0.0) {
		vec2 p = TEXTURE_PIXEL_SIZE;
		float viz = texture(TEXTURE, clamp(UV + vec2( p.x, 0.0), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(-p.x, 0.0), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(0.0,  p.y), quadro_min, quadro_max)).a
				  * texture(TEXTURE, clamp(UV + vec2(0.0, -p.y), quadro_min, quadro_max)).a;
		float borda = c.a * (1.0 - viz);

		// A FORMA VEM PRIMEIRO e o IMPACTO por cima -- assim um soco levado continua se lendo em
		// quem esta transformado, que e o caso em que ele mais precisa aparecer.
		//
		// UMA LINHA, DENTRO DA SILHUETA, e nada alem disso: nenhum alfa novo, nenhum pulso, nenhuma
		// soma. `c.a` nao e tocado, entao o corpo continua ocupando exatamente os pixels que a arte
		// desenhou -- e todo o problema de amostra fora da caixa do quadro deixou de existir junto
		// com o halo.
		//
		// E o MESMO idioma da linha do impacto logo abaixo, de proposito: sao dois tracos na mesma
		// borda, um permanente e um de 0,15 s, e eles se sobrepoem na ordem certa.
		if (aura > 0.0) c.rgb = mix(c.rgb, aura_cor.rgb, clamp(borda * aura, 0.0, 1.0));

		if (contorno > 0.0) c.rgb = mix(c.rgb, contorno_cor.rgb, borda * contorno);
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

	// ============================ O `COLOR` QUE CHEGOU E A TERCEIRA OPERACAO ============================
	// `COLOR = c` DESCARTAVA o `modulate`/`self_modulate` do node -- calado. No Godot esse e o canal
	// pelo qual o `CanvasItem` entrega a cor (o produto do `modulate` de todos os pais, do
	// `self_modulate` proprio e da cor do vertice); quem escreve `COLOR` sem le-lo joga tudo fora. Como
	// nada aqui reclama, o efeito era so "a cor nao aparece", e TRES coisas escritas e documentadas
	// estavam mortas por isso:
	//
	//   1. A TINTA DA COLADA (`CharacterVisual.ColadasDaForma`, `s.Modulate`) -- o defeito que o dono
	//      relatou: *"no wrathfull e nas formas de lssj o `god - grey.png` ta CINZA ainda e n verde"*.
	//      O Rose sofria igual e ninguem tinha visto, porque a linha e mais dificil de alcancar.
	//   2. O TOM DO NAMEKUSEIJIN (`CharacterVisual.Vestir`, `_corpo.SelfModulate`) -- o `Brilho`
	//      1,18/0,82 do `VisualCatalog.TintaDoCorpo`, cujo comentario diz "sem isso os quatro tons de
	//      Namekuseijin sairiam todos iguais". E era exatamente o que acontecia.
	//   3. O VULTO SIMPLES do `Zanzoken` (estilo `Simples`, que fica com o material desta classe): ele
	//      some pelo `Modulate` do proprio node (`Zanzoken.cs:175`) e nascia com o azul frio de
	//      `:94` -- nao sumia e nao era azul.
	//
	// ============================ E MULTIPLICAR E A OPERACAO CERTA, NAO UM ATALHO ============================
	// `COLOR` multiplica, e multiplicar E o `color = rgb(110,255,140)` do `EffectLayer.dm:32` -- na
	// linguagem do BYOND o `color` de um atom multiplica, e a folha `god - grey.dmi` foi desenhada pra
	// isso. Medido nos 4 tons que sao 95,4% dos pixels opacos da folha (192, 190, 187, 184 -- cinza
	// puro), com o verde `6eff8c`:
	//
	//     SOMA  (tinta_modo 0)  ->  (255,255,255) nos QUATRO -- branco chapado, saturacao 0
	//     MATIZ (tinta_modo 1)  ->  (166,255,211) -- verde lavado, e o canal G satura em 255 nos
	//                               quatro, ou seja o sombreado do desenho SOME no canal dominante
	//     MULTIPLICA            ->  ( 83,192,105) -- saturacao 0,568, a mesma da tinta (0,569)
	//
	// E o `192 x 110/255 = 83` que o `Catalogo.VerdeDaAmeaca` ja documentava: as duas constantes
	// (`6eff8c` e `ff7ac6`) foram DIMENSIONADAS pra multiplicar, e nenhum dos dois modos do uniform
	// entrega isso. Por isso o conserto e ligar o canal, e nao criar um `tinta_modo = 2`.
	//
	// NAO ATRAPALHA QUEM NAO USA: `modulate` e `self_modulate` nascem brancos, e branco e o neutro da
	// multiplicacao -- toda camada que nao escreve nada continua pixel por pixel como era.
	//
	// ============================ MAS O `COLOR` DO FRAGMENT NAO E O MODULATE (TOMBO 2) ============================
	// A frase acima ("branco e o neutro, quem nao escreve continua como era") era FALSA, e foi ela que
	// pintou o jogo inteiro de preto. No canvas_item do Godot o `COLOR` que CHEGA no fragment ja vem
	// **multiplicado pela textura** ("Color from vertex function multiplied by the TEXTURE color"), e
	// nao e o modulate sozinho. Entao `c * COLOR` nao era "aplicar o modulate": era
	// `textura x textura x modulate` -- a folha ELEVADA AO QUADRADO.
	//
	// MEDIDO NA GPU, e nao na conta (Godot 4.7, sprite real, `get_viewport().get_texture().get_image()`):
	//
	//   * shader vazio (`COLOR = texture(...)`) sobre `Hair_GokuSSj.png` -> `#948c08 #c6b508 #f7de29
	//     #f7ef94`, a folha intacta;
	//   * a MESMA folha com `c * COLOR` e tinta zero -> `#564d00 #9a8000 #efc107 #efe056`. Cada canal e
	//     o quadrado do original (148/255 ao quadrado = 86 = `0x56`, e assim nos doze).
	//
	// E O ESTRAGO NAO ERA "um pouco mais escuro": onde a folha e PRETA, o quadrado zera tudo.
	//
	//   * o penteado base do jogo e preto (`Hair_Goku.png`, 40% em `#000000`), e e nele que caem o
	//     vermelho do SSG e o roxo do Ultra Ego. Medido com `e2331c`: **`#000000` em 40% dos pixels**
	//     (e `#290f0b`, `#421e18` no resto) -- cabelo PRETO, nao vermelho. Com o conserto: `#e2331c`,
	//     `#ff5c45`, `#ff755e`;
	//   * a camada de olho e `Eyes_Black.png`, preta em 100% dos pixels opacos. As NOVE cores de
	//     `Catalogo.CorDoOlho` (o verde do SSJ, o branco sem iris das duas linhas lendarias, a prata do
	//     Ultra Instinto) mediram **`#000000` em 100%** -- o eixo inteiro estava invisivel. Com o
	//     conserto cada uma mede exatamente o seu hexa (`40a060`, `fcfdfd`, `e0409a`).
	//
	// Isto e a metade de baixo do *"o cabelo do ssj blue ta MUITO escuro"* do dono: quem tem penteado
	// sem arte de Super Saiyajin (41 dos 59) nao via um azul escuro, via cabelo preto. A outra metade
	// -- os hexas em si -- esta em `Catalogo.AzulDoCabeloDivino`.
	//
	// O conserto foi o VARYING: no **vertex** a textura ainda nao entrou, entao guardar o `COLOR` la e
	// multiplicar aqui aplica o modulate UMA vez. A colada do `god - grey` passou a medir `#b85c95` sobre
	// o cinza 192 com o `ff7ac6`, que e o `192 x rosa/255` que a constante ja documentava.
	//
	// ============================ O QUE O AMBIENTE FAZ, E O QUE ELE NUNCA DEPENDEU DAQUI ============================
	// Houve uma TERCEIRA suspeita, e ela era sobre esta linha: a de que ligar o `COLOR` teria trazido
	// junto o `CanvasModulate` do mundo (a luz do dia da `Iluminacao`), e que era por isso que o boneco
	// tinha comecado a escurecer com o ceu -- o relato do dono de que *"o cabelo e o rabo n eram tao
	// escuros assim antes"*. ELA E FALSA, e foi medida na GPU dos dois lados:
	//
	//   * troque este fim por uma cor CONSTANTE (`COLOR.rgb = vec3(1,0,0) * c.a`, que nao le `COLOR`
	//     nenhum) e o sprite AINDA sai multiplicado pelo ambiente: `#ff0000` vira `#dc0000` ao meio-dia
	//     (`dc` = o R do `dcdcd2`) e `#1b0000` a meia-noite. O `CanvasModulate` e aplicado pelo Godot
	//     DEPOIS do fragment -- nenhuma linha escrita aqui escapa dele, e a antiga (`COLOR = c`) tambem
	//     nao escapava;
	//   * e o `cor_do_node` NAO o carrega: com ambiente de noite, a razao de brilho do personagem medida
	//     em jogo (`--diagtintamundo`) deu 0,187 contra 0,182 do chao. Se o ambiente entrasse tambem por
	//     aqui, o boneco seria multiplicado DUAS vezes e a razao dele seria o QUADRADO (0,03).
	//
	// Ou seja: o personagem sempre obedeceu a luz do dia -- antes e depois desta linha -- e obedece UMA
	// vez so, como o cenario. Tirar o boneco do ciclo do dia nao e assunto deste arquivo: e topologia
	// (em que canvas ele desenha), porque o ambiente nao passa por aqui pra ser recusado.
	// ======================================================================================================
	COLOR = c * cor_do_node;
}
