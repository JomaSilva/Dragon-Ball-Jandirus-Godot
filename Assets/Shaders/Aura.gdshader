shader_type canvas_item;
uniform vec3 cor : source_color = vec3(1.0);
uniform float forca : hint_range(0.0, 2.0) = 1.0;

// ============================ FOLHA JA COLORIDA NAO PASSA PELA TINTA ============================
// Este shader descarta o RGB da textura de proposito (`i = max(r,g,b)`) pra que UM desenho cinza
// sirva de dourado, vermelho e azul. Mas isso quebra pra uma folha que JA vem colorida, como a
// `AuraSSjBig`: o dourado do arquivo e jogado fora e trocado pela cor de fora.
//
// Foi o que o dono viu -- "no jogo a aura ta branca, mas nos arquivos ela e dourada". Eu tinha
// tentado resolver mandando BRANCO como cor, e branco aqui nao e "nao tingir": e apagar a arte.
// ================================================================================================
uniform bool tingir = true;

// ============================ E ONDE MORA O DESENHO? SAO DUAS PERGUNTAS, NAO UMA ============================
// O `tingir` acima responde *"a cor vem de fora?"*. Esta responde outra, e elas sao INDEPENDENTES:
// *"em que canal esta o desenho desta folha?"*. Medido arquivo por arquivo:
//
//   colorablebigaura  RGB cinza (pico 200) + alfa   -> o desenho esta no RGB   -> falso
//   Aura, Big         RGB **PRETO PURO** + alfa      -> o desenho esta no ALFA  -> verdadeiro
//   AuraSSjBig        RGB `ffff80` CHAPADO + alfa    -> o desenho esta no ALFA  -> verdadeiro
//   AuraLSSjBig       RGB `d9ff00` CHAPADO + alfa    -> o desenho esta no ALFA  -> verdadeiro
//   FieryGod          RGB variado + alfa CONSTANTE   -> o desenho esta no RGB   -> falso
//
// ============================ SEM ISTO, A `Aura, Big` SAI PRETA ============================
// `Aura, Big.png` tem `rgb(0,0,0)` nos 27.248 pixels opacos dela -- 100%, medido. O `i` de baixo
// tira a intensidade de `max(r,g,b)`, entao pra ela `i = 0`, e `cor * 0` e PRETO. Trocar a folha
// base sem trocar esta conta pinta uma silhueta preta em volta de 19 formas -- e a bancada passa
// VERDE, porque ela confere o uniform escrito e nao o pixel desenhado.
//
// ============================ E ESTE MODO NAO E NOVO: DUAS FOLHAS JA VIVEM NELE ============================
// A `AuraSSjBig` e a `AuraLSSjBig` sao CHAPADAS -- uma unica cor RGB nos 27.248 e nos 37.902 pixels
// opacos, respectivamente. Ou seja o desenho delas ja esta inteiro no alfa, e ja sai certo:
//
//   * a SSj pelo ramo `!tingir`, que copia `c.rgb` e nunca calcula `i` -- de PROPOSITO;
//   * a LSSj por ACIDENTE feliz: `max(r,g,b) = 255/255 = 1,0`, dividido por 0,784 da 1,276, que o
//     `clamp` corta em 1,0 -- entao `i` ja vale exatamente 1,0 em todo pixel dela.
//
// Marcar a LSSj aqui e, por isso, um NAO-OPERACAO comprovavel: o valor de `i` nao muda em nenhum
// pixel. Ela entra porque a regra e sobre a FOLHA, e deixar de fora uma folha que satisfaz a regra
// so pra "nao mexer" deixaria a proxima pessoa achando que ha duas classes onde ha uma.
// ==========================================================================================================
uniform bool forma_no_alfa = false;

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	// a aura e monocromatica na folha: o canal mais forte vira a INTENSIDADE, e a cor
	// vem de fora. Assim o mesmo desenho serve pra dourado, vermelho e azul.
	//
	// ============================ NORMALIZAR, SENAO TUDO ESCURECE ============================
	// O dono: "a aura do ssj ta mt marrom, deveria ser mais dourada". Nao era a cor -- ela ja era
	// `ffd24a`. Era a CONTA: a folha `colorablebigaura` e cinza com o maximo em 200/255 = 0,784,
	// entao `cor * i` fazia ate o pixel mais claro sair a 78% da cor, e os meios-tons bem abaixo
	// disso. Dourado multiplicado por 0,3 E marrom -- e nao ha cor que conserte isso, porque o
	// escurecimento e do multiplicador.
	//
	// Dividir pelo pico devolve a faixa cheia; a gama de 0,75 levanta os meios-tons, que e o que
	// o `ICON_ADD` do BYOND fazia de graca (la a cor era SOMADA, nao multiplicada).
	// ================================================================================================
	//
	// ============================ E QUANDO O DESENHO ESTA NO ALFA, NAO HA O QUE NORMALIZAR ============================
	// `i` vale 1 e a forma vem toda do `c.a` la embaixo. Nao e um atalho: e a MESMA conta que o ramo
	// `!tingir` ja faz pra a `AuraSSjBig`, so que com a cor vindo de fora em vez de vir chapada no
	// arquivo. Ver o cabecalho do `forma_no_alfa`.
	// ==============================================================================================================
	const float PicoDaFolha = 0.784;
	float i = forma_no_alfa
		? 1.0
		: pow(clamp(max(max(c.r, c.g), c.b) / PicoDaFolha, 0.0, 1.0), 0.75);
	// Folha colorida: os pixels valem como estao, so o alfa e a forca entram.
	// ============================ FORCA E PRESENCA, NAO ESCURIDAO ============================
	// Isto era `cor * i * forca` no RGB E `c.a * forca` no alfa -- a mesma `forca` decidindo o
	// BRILHO e a OPACIDADE. Na carga comum ela vale 0,45..0,75, entao a chama saia com 45% de
	// brilho: escura, transparente, e -- pior -- com TODAS as cores escurecidas na mesma proporcao,
	// convergindo pro mesmo cinza. O dono viu exatamente isso: "fica tao fraca q n consigo ver
	// diferença entre os sprites de carga na base... parece q tudo e escuro/cinza escuro".
	//
	// Agora sao dois canais separados:
	//   COR   -- sempre cheia. So passa de 1 pra ESTOURAR no excesso (`max(forca, 1)`), que e o
	//            branco-quente de quem ultrapassou o teto.
	//   ALFA  -- e quem a `forca` governa, saturando em 1. E ele que faz o pulso: a chama respira
	//            aparecendo e sumindo, nao clareando e escurecendo.
	// ================================================================================================
	COLOR = tingir ? vec4(cor * i * max(forca, 1.0), c.a * clamp(forca, 0.0, 1.0))
				   : vec4(c.rgb * max(forca, 1.0), c.a * clamp(forca, 0.0, 1.0));
}
