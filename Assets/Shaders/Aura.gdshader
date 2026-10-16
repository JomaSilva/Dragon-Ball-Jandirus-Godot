shader_type canvas_item;
uniform vec3 cor : source_color = vec3(1.0);
uniform float forca : hint_range(0.0, 2.0) = 1.0;

// ============================ FOLHA JA COLORIDA NAO PASSA PELA TINTA ============================
// A cor de uma folha que se TINGE vem de fora (`cor`); a de uma folha que JA vem colorida, como a `AuraSSjBig`, e a do
// proprio arquivo -- trocar o dourado dela pela cor de fora seria jogar a arte fora.
//
// Foi o que o dono viu -- "no jogo a aura ta branca, mas nos arquivos ela e dourada". Eu tinha
// tentado resolver mandando BRANCO como cor, e branco aqui nao e "nao tingir": e apagar a arte.
// ================================================================================================
uniform bool tingir = true;

// ============================ O DESENHO MORA NO ALFA, EM TODA FOLHA ============================
// Medido arquivo por arquivo, as quatro folhas vivas tem o RGB CHAPADO -- uma cor so nos pixels opacos -- e o desenho
// inteiro no alfa: `Aura, Big` (PRETO puro), `AuraSSjBig` (`ffff80`), `AuraLSSjBig` (`d9ff00`) e a do Kaio-ken (`ff0000`).
// Entao a forma da chama e o ALFA, e a cor e uma so: a de fora (`tingir`) ou a do arquivo.
//
// ESTE SHADER JA LEU DESENHO NO RGB: o `i = max(r,g,b)`, normalizado pelo pico de 200/255 da `colorablebigaura` e depois
// usado pelas tres folhas divinas, com um `forma_no_alfa` dizendo qual folha era qual. A `colorablebigaura` saiu da base
// por ordem do dono, e as divinas sairam pra `ChamaDeAura` no adendo 13 (shader proprio). Sem consumidor, o ramo morreu.
//
// O PERIGO QUE SOBROU E A TINTA: a `Aura, Big` e PRETA no arquivo, e sem `tingir` ela sai como uma silhueta preta em
// volta de 19 formas. A bancada da chama (`--diagchama`) injeta exatamente isso (`SpriteDeAura.ForcarTintaDeTeste`).
// ==============================================================================================

void fragment() {
	vec4 c = texture(TEXTURE, UV);
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
	COLOR = tingir ? vec4(cor * max(forca, 1.0), c.a * clamp(forca, 0.0, 1.0))
				   : vec4(c.rgb * max(forca, 1.0), c.a * clamp(forca, 0.0, 1.0));
}
