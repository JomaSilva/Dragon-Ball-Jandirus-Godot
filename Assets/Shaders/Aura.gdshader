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
	const float PicoDaFolha = 0.784;
	float i = pow(clamp(max(max(c.r, c.g), c.b) / PicoDaFolha, 0.0, 1.0), 0.75);
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
