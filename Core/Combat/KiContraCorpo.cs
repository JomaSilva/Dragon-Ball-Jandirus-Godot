using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>Onde o corpo fica diante de um tiro de ki, pela razao entre o poder dele e o do tiro.</summary>
public enum PostoContraKi : byte
{
	/// <summary>Mais fraco que o tiro: e levado por ele.</summary>
	MaisFraco = 0,

	/// <summary>De igual a quatro vezes mais forte: apanha, mas nao e levado.</summary>
	MaisForte = 1,

	/// <summary>Quatro vezes mais forte ou mais: o tiro para nele e nao o fere.</summary>
	Parede = 2,
}

/// <summary>
/// **O KI CONTRA O CORPO: A ESCADA POR DIFERENCA DE PODER.**
///
/// ============================ NAO E PORTE: E REGRA DO DONO (2026-10-12) ============================
/// O playtest: *"os viloes q spawnam no mapa (...) o ataque de ki deles (beam) n esta causando dano em mim, mesmo
/// eles tendo o mesmo poder q eu, e eu to conseguindo dar deflect mesmo n sendo muito mais forte, pois lembre q
/// deflect vc tem q ser pelomenos 2x a força do inimigo pra funcionar. deflect deveria fazer com q o ataque de ki do
/// inimigo seja cancelado"*. E a escada que ele descreveu em seguida:
///
///   * **PAREDE** (4 vezes mais forte, ou mais): *"nem precisa defender o ataque de ki, ao bater nele vai ter o
///     efeito de colisao, e o defensor vai poder andar livremente como se ele fosse uma parede imparavel"*;
///   * **MAIS FORTE** (de igual a 4 vezes): defendendo, segura o raio e sai andando quando quiser; sem defender,
///     leva o dano e *"ainda pode tentar sair do ataque mas ele anda bem mais devagar enquanto toma o dano"*;
///     (segurar e a disputa de guarda, que planta os dois; sair andando e o passo DE LADO, com a guarda erguida --
///     <see cref="PassoAtravessado"/>, pedido no mesmo dia)
///   * **MAIS FRACO**: e ARRASTADO pelo raio ate o fim do alcance, onde ele estoura. Sem defender fica atordoado;
///     defendendo, o dano vai nos membros da guarda e o raio o empurra mais devagar.
///   * **DEFLETIR** (a guarda erguida no tempo certo, ou a sorte da deflexao) pede o DOBRO do poder do tiro, e
///     CANCELA o ataque na hora.
///   * **O ESTOURO FERE EM AREA** (retorno do dono do mesmo dia: *"A explosao de ki deveria dar dano em area sim
///     mesmo quem n foi arrastado"*): quem esta dentro do raio da explosao leva uma fracao dela pela distancia --
///     menos quem e parede. Ver <see cref="TilesDaExplosao"/> e <see cref="FracaoDaExplosao"/>.
///
/// O QUE ISTO SUBSTITUI: o "corte dos fracos" do DM (`objects.dm:355-357`: o impacto de dano final abaixo de 10
/// nao e golpe). La o corte e por DANO, e entre dois corpos de poder igual quase todo raio cai abaixo dele -- o
/// vilao de mesmo poder atirava e nada acontecia. Aqui quem nao e ferido e quem e PAREDE; todo o resto apanha.
/// O corte antigo ficou como o defeito injetado (<see cref="RegraDeAntesDeTeste"/>).
/// ====================================================================================================
///
/// A RAZAO E `poder expresso do corpo / poder do tiro` (`Projetil.Bp`, o poder de quem atirou no disparo). Corpo
/// caido expressa 10% (`Fighter.PowerLevel`): quem esta no chao e sempre o mais fraco.
///
/// ELA E RELIDA A CADA BATIDA, E O DEGRAU DE AGORA VALE TAMBEM PRA QUEM O TIRO JA VINHA LEVANDO: o corpo que deixa
/// de ser mais fraco no meio do caminho (uma forma, uma explosao de raiva) e SOLTO na batida seguinte -- fica onde
/// esta e apanha como qualquer um do degrau dele --, e o que passa a ser mais fraco (caiu, baixou o poder) comeca a
/// ser levado. Quem solta e o `GameServer.Acertar`; ate 2026-10-12 a tarde so virar parede soltava
/// (<see cref="ArrastoSoSoltaAParedeDeTeste"/>).
/// </summary>
public static class KiContraCorpo
{
	/// <summary>A partir de quantas vezes o poder do tiro o corpo e parede. *"4 a 5x ou mais"*.</summary>
	public const double VezesDaParede = 4;

	/// <summary>Quantas vezes o poder do tiro e preciso ter pra defleti-lo. *"pelomenos 2x a força do inimigo"*.</summary>
	public const double VezesPraDefletir = 2;

	/// <summary>Quanto do passo sobra pra quem e mais forte e leva o raio sem defender. *"anda bem mais devagar"*.</summary>
	public const double AndarSobORaio = 0.35;

	/// <summary>Por quanto tempo o passo lento dura depois de cada batida do raio (uma batida a cada 0,2 s o renova).</summary>
	public const long MsSobORaio = 500;

	/// <summary>A velocidade com que o raio leva um corpo de poder IGUAL, em fracao da do proprio raio. *"devagar"*.</summary>
	public const double ArrastoEntreIguais = 0.2;

	/// <summary>Defendendo, o corpo levado anda a esta fracao do que andaria sem a guarda.</summary>
	public const double ArrastoDeQuemDefende = 0.5;

	/// <summary>
	/// Quanto de uma batida o raio cobra enquanto LEVA o corpo. *"causando dano por segundo (nao muito alto) e ao
	/// atingir o limite do ataque, acontece uma explosao ao qual ai sim toma bastante dano"*.
	/// </summary>
	public const double FracaoDaBatidaNoArrasto = 0.35;

	/// <summary>A explosao do fim do alcance, em batidas cheias do raio.</summary>
	public const double BatidasDaExplosao = 6;

	/// <summary>
	/// O RAIO DO DANO EM AREA DA EXPLOSAO de um tiro de escala 1, em tiles: o tamanho da primeira onda que a tela
	/// desenha no estouro (`World.cs`, 130 px).
	/// </summary>
	public const double TilesDaExplosaoDeEscala1 = 4;

	/// <summary>O teto do raio da explosao, em tiles: a esfera gigante nao varre a zona inteira.</summary>
	public const double TetoDeTilesDaExplosao = 8;

	/// <summary>
	/// (v0.3.0) O RAIO DA EXPLOSAO COLOSSAL (a Genkidama, `Projetil.Colossal`) com escala 1, em tiles -- o dono: *"explodir no
	/// final e ter uma cratera ENORME"*. Duas vezes e meia o de uma esfera grande; cresce com a escala ate o teto.
	/// </summary>
	public const double TilesDaExplosaoColossalDeEscala1 = 10;

	/// <summary>(v0.3.0) O teto da explosao colossal, em tiles.</summary>
	public const double TetoDeTilesDaExplosaoColossal = 16;

	/// <summary>Quanto da explosao chega a quem esta na BEIRADA dela. No centro chega inteira; entre os dois, uma reta.</summary>
	public const double FracaoNaBeiraDaExplosao = 0.25;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): a explosao volta a ferir so quem vinha na ponta do tiro -- a primeira
	/// entrega de 2026-10-12, que o dono reprovou. Lido pela propria linha do dano em area (`GameServer.Estourar`).
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool ExplosaoSemAreaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): o estouro fere tambem quem DEFLETIU o tiro -- o deflect certo
	/// terminando em dano pra quem o fez. Lido pela propria linha da regra (`GameServer.Estourar`). Falso em jogo, sempre.
	/// </summary>
	public static bool EstouroPegaQuemDefletiuDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (as bancadas de ki: `--v020teste` 9, `--projetilteste` 3, 9, 15 e 16, `--embatekiteste` 6b e
	/// 13): o jogo de antes de 2026-10-12 -- o corte por dano (abaixo de 10 nao fere), a deflexao sem exigir o dobro,
	/// o empurrao de perto e o arrasto de dez tiles sem explosao. SO SOBE DENTRO DO CONTRA-EXEMPLO que o nomeia, entre
	/// `try/finally`: nenhuma bancada roda uma familia inteira com ele (a `--projetilteste` rodava, ate ser convertida
	/// pra escada). Falso em jogo, sempre.
	/// </summary>
	public static bool RegraDeAntesDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--projetilteste`, familia 9): o arrasto ja comecado so solta quem vira PAREDE -- a
	/// escada como saiu na manha de 2026-10-12. Quem vinha sendo levado e deixava de ser mais fraco que o tiro no
	/// meio do caminho continuava arrastado ate o fim do alcance, pagando a batida cheia a cada ciclo e a explosao
	/// inteira (defendendo, nunca chegava a segurar o raio nas maos); a esfera grande fazia o mesmo. Lido pela
	/// propria linha que solta o corpo (`GameServer.Acertar`, passo 5c). Falso em jogo, sempre.
	/// </summary>
	public static bool ArrastoSoSoltaAParedeDeTeste;

	public static double Razao(double poderDoCorpo, double poderDoTiro) =>
		poderDoTiro <= 0 ? double.PositiveInfinity : Math.Max(poderDoCorpo, 0) / poderDoTiro;

	public static PostoContraKi Posto(double razao) =>
		razao >= VezesDaParede ? PostoContraKi.Parede : razao >= 1 ? PostoContraKi.MaisForte : PostoContraKi.MaisFraco;

	public static bool EhParede(double razao) => Posto(razao) == PostoContraKi.Parede;

	/// <summary>Este corpo consegue DEFLETIR este tiro (cancela-lo)? So com o dobro do poder dele.</summary>
	public static bool PodeDefletir(double razao) => razao >= VezesPraDefletir;

	/// <summary>O raio LEVA este corpo? So o mais fraco -- defendendo ou nao.</summary>
	public static bool Arrasta(double razao) => Posto(razao) == PostoContraKi.MaisFraco;

	/// <summary>O impacto ATORDOA? O mais fraco que nao estava defendendo.</summary>
	public static bool Atordoa(double razao, bool defendendo) => Posto(razao) == PostoContraKi.MaisFraco && !defendendo;

	/// <summary>
	/// A VELOCIDADE DO ARRASTO, em fracao da do raio: *"empurar ele pra tras devagar (velocidade varia com relaçao
	/// a diferença de força)"*. No abismo de poder o corpo vai na velocidade do raio; perto do empate, a
	/// <see cref="ArrastoEntreIguais"/>; defendendo, a metade disso.
	/// </summary>
	public static double FatorDoArrasto(double razao, bool defendendo)
	{
		if (!Arrasta(razao)) return 0;
		double f = ArrastoEntreIguais + (1 - ArrastoEntreIguais) * (1 - Math.Clamp(razao, 0, 1));
		return defendendo ? f * ArrastoDeQuemDefende : f;
	}

	/// <summary>O raio do dano em area da explosao deste tiro, em tiles: cresce com a escala do desenho dele, ate o teto.</summary>
	public static double TilesDaExplosao(double escalaDoTiro) =>
		Math.Min(TilesDaExplosaoDeEscala1 * Math.Max(escalaDoTiro, 1), TetoDeTilesDaExplosao);

	/// <summary>(v0.3.0) O raio da explosao, em tiles -- a COLOSSAL (a Genkidama) tem a dela, ver <see cref="TilesDaExplosaoColossalDeEscala1"/>.</summary>
	public static double TilesDaExplosao(double escalaDoTiro, bool colossal) => colossal
		? Math.Min(TilesDaExplosaoColossalDeEscala1 * Math.Max(escalaDoTiro, 1), TetoDeTilesDaExplosaoColossal)
		: TilesDaExplosao(escalaDoTiro);

	/// <summary>
	/// Quanto da explosao chega a um corpo, pela distancia dele ao centro em FRACAO do raio (0 = no centro, 1 = na
	/// beirada): uma reta de 1 a <see cref="FracaoNaBeiraDaExplosao"/>. Fora do raio, nada.
	/// </summary>
	public static double FracaoDaExplosao(double distanciaSobreORaio) =>
		distanciaSobreORaio > 1 ? 0 : 1 - (1 - FracaoNaBeiraDaExplosao) * Math.Max(distanciaSobreORaio, 0);

	// =====================================================================
	// O KI CONTRA O CENARIO, E O TIRO QUE ACABA ESTOURANDO (dono, 2026-10-13)
	// =====================================================================
	// *"a explosao do beam ta sem efeito nenhum, nem mesmo a cratera ( a mesma q tem quando uma transformaçao de
	// raiva acontece), e quando uma esfera de ki se afasta muito ela so some e n explode (so explode se bater em
	// paredes ou pessoas e n cria a cratera)"* e *"esferas de ki, kiai etc deveriam quebrar paredes assim como soco
	// (explosoes de colisao com esferas grandes idem), explosoes de ataque de ki como esferas grandes e beams alem
	// da cratera devem dar dano em area na regiao da explosao, paredes e construçoes tb levam o dano, podendo
	// destruid varias paredes em volta da explosao e tiles de chao tb"*.
	//
	// O QUE MUDOU, EM TRES LINHAS:
	//   * TODO RAIO E TODA ESFERA GRANDE ACABAM NUM ESTOURO GRANDE -- no fim do alcance (ja era), e agora tambem na
	//     parede, quando o prazo vence e quando a mao solta o raio que estava em cima de alguem. A v0.2.0 so
	//     estourava no fim do alcance: o raio que LEVAVA alguem devagar morria de prazo no meio do caminho, sem
	//     clarao e sem cratera -- era a queixa.
	//   * TODA BOLA COMUM ACABA NUM ESTOURO PEQUENO, com a cratera pequena: no corpo, na parede, e no fim do alcance
	//     ou do prazo (o `Burnout` do DM termina em `explode()`, `objects.dm:652-661`).
	//   * O ESTOURO BATE NO CENARIO: a bola, na celula em que acabou, pela REGRA DO SOCO; o estouro grande, em
	//     tudo o que esta no raio dele (parede, construcao, nave, bloco erguido e o chao).
	//
	// NO DM a explosao e um objeto (`obj/explosions`, `explosions.dm:185-232`): `obj` leva `takeDamage(strength)`,
	// `turf` cai se `Resistance <= strength/dist` com `prob(80)` (`damage_turf`, `:178-183`) -- igual pra chao e
	// parede. (La o `return` da linha 225 esta DENTRO do `for`: so o primeiro atomo da lista era processado, e a
	// poeira e a cratera do fim nunca saiam. E defeito de la, e nao regra.)

	/// <summary>A chance de uma PAREDE (celula que barra) cair na explosao que a alcanca -- o `prob(80)` do `damage_turf`.</summary>
	public const double ChanceDeParedeNaExplosao = 0.80;

	/// <summary>
	/// A chance de o CHAO rachar bem no centro da explosao. DIVERGENCIA DECLARADA do DM, onde o chao caia com os
	/// mesmos 80% da parede: aqui cada Kamehameha deixaria um disco de terra batida de oito tiles de lado a lado.
	/// </summary>
	public const double ChanceDeChaoNoCentro = 0.55;

	/// <summary>A chance de o chao rachar na beirada da explosao; entre o centro e ela, uma reta.</summary>
	public const double ChanceDeChaoNaBeira = 0.06;

	/// <summary>A chance de uma celula de CHAO rachar na explosao, pela distancia ao centro em fracao do raio.</summary>
	public static double ChanceDeChaoNaExplosao(double distanciaSobreORaio) =>
		distanciaSobreORaio > 1
			? 0
			: ChanceDeChaoNoCentro - (ChanceDeChaoNoCentro - ChanceDeChaoNaBeira) * Math.Max(distanciaSobreORaio, 0);

	/// <summary>
	/// (v0.3.0) A CHANCE DE O CHAO RACHAR NA EXPLOSAO COLOSSAL: o miolo racha quase inteiro (ate 60% do raio) e dali a chance
	/// cai ate a beirada -- e o buraco ENORME do pedido, e nao o salpicado de um Kamehameha.
	/// </summary>
	public static double ChanceDeChaoNaExplosaoColossal(double distanciaSobreORaio) =>
		distanciaSobreORaio > 1 ? 0
		: distanciaSobreORaio <= MioloDaExplosaoColossal ? ChanceDeChaoNoMioloColossal
		: ChanceDeChaoNoMioloColossal - (ChanceDeChaoNoMioloColossal - ChanceDeChaoNaBeiraColossal)
		  * (distanciaSobreORaio - MioloDaExplosaoColossal) / (1 - MioloDaExplosaoColossal);

	public const double MioloDaExplosaoColossal = 0.6, ChanceDeChaoNoMioloColossal = 0.95, ChanceDeChaoNaBeiraColossal = 0.15;

	/// <summary>
	/// De quantos em quantos tiles uma CRATERA PEQUENA pode nascer: `if(!locate(/obj/Crater/destroyed) in range(3))
	/// createCrater(loc,1)` (`objects.dm:464`). Sem isto a rajada de bolas num muro empilharia uma por tiro.
	/// </summary>
	public const double TilesEntreCraterasPequenas = 3;

	/// <summary>Por quanto tempo uma cratera pequena barra a vizinha: o que ela dura na tela (`Decalques.Prazo`, 18 s).</summary>
	public const double SegundosDaCrateraPequena = 18;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o ki volta a nao arranhar o cenario -- a bola acaba na parede sem
	/// bater nela e a explosao nao toca em nada. Lido pelas duas portas do servidor (`BaterNoCenarioComKi`,
	/// `EstragarOCenarioNaExplosao`). Falso em jogo, sempre.
	/// </summary>
	public static bool KiNaoEstragaCenarioDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--v021teste` e `--diagv021`): a bola comum volta a se APAGAR no fim do alcance e do prazo, sem
	/// estouro nem cratera -- a v0.2.0. Lido pelo `Projetil.BolaQueEstouraAoAcabar`. Falso em jogo, sempre.
	/// </summary>
	public static bool BolaSomeAoAcabarDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): o raio e a esfera grande voltam a so estourar no fim do alcance --
	/// na parede lascam, e o raio que acaba em cima de alguem (o prazo, a mao que soltou) some calado. E a v0.2.0.
	/// Lido pelo `Projetil.EstouraSempreQueAcaba`. Falso em jogo, sempre.
	/// </summary>
	public static bool SoEstouraNoFimDoAlcanceDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v021teste`): a bola comum volta a nao deixar cratera nenhuma -- no corpo, na
	/// parede ou no fim do alcance. Lido pela propria porta da cratera pequena (`GameServer.AbrirACrateraPequena`).
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool BolaSemCrateraDeTeste;

	// =====================================================================
	// SAIR DE LADO DA DISPUTA DE GUARDA (dono, 2026-10-12)
	// =====================================================================
	/// <summary>
	/// **QUEM SEGURA O RAIO COM AS MAOS SAI DE LADO SEM BAIXAR A GUARDA.** O dono: *"se o defensor for mais forte ele
	/// pode tentar se mover pros lados e sair da frente do ataque de ki enquanto defende com a guarda e ele n é
	/// arrastado pelo ki"*.
	///
	/// Quem nao e mais fraco que o raio e esta de guarda abre a disputa de guarda (`GameServer.TentarEmbateDeGuarda`),
	/// e a disputa PLANTA os dois: ate este dia a unica saida de quem segurava era baixar a guarda -- e levar o raio.
	/// Agora o passo ATRAVESSADO ao raio fecha a disputa SEM VENCEDOR (`GameServer.SairDeLadoDaDisputa`): o raio segue
	/// viagem, e quem saiu tem um prazo pra sair de cima dele (<see cref="SegundosPraSairDeLado"/>). O passo AO LONGO
	/// do raio -- pra cima de quem atira, ou pra tras -- nao solta ninguem: recuar na frente de um raio nao e sair da
	/// frente dele.
	///
	/// ESTE NUMERO e quanto do passo tem de ser DE LADO: o seno do angulo entre o passo e o eixo do raio. Com 0,5 sai
	/// quem anda a mais de 30 graus do eixo -- contra um raio que vem reto, as duas teclas atravessadas e as quatro
	/// diagonais; contra um raio em qualquer angulo, pelo menos duas das quatro teclas de andar.
	/// </summary>
	public const double LadoDoPassoQueSai = 0.5;

	/// <summary>
	/// ESTE PASSO E ATRAVESSADO A ESTE RAIO? A parte dele que e de lado (o produto vetorial com o eixo) tem de ser pelo
	/// menos <see cref="LadoDoPassoQueSai"/> do tamanho dele. Passo nenhum nao e passo.
	///
	/// AS DUAS PONTAS PERGUNTAM AQUI, COM O MESMO EIXO: o servidor ao decidir se a disputa fecha e o cliente ao
	/// decidir se da o passo (`LocalPlayer`; o eixo viaja no comeco da disputa, `Protocol.ClashSub.SaidaDeLado`). Com
	/// duas contas, o passo que uma aceita e a outra recusa vira correcao de posicao por pacote -- o corpo tremendo.
	/// </summary>
	/// <param name="eixoDoRaio">O rumo do raio, unitario. O sentido nao importa.</param>
	public static bool PassoAtravessado(Vec2 passo, Vec2 eixoDoRaio)
	{
		if (passo.LengthSquared < 1e-6f) return false;
		float deLado = MathF.Abs(passo.X * eixoDoRaio.Y - passo.Y * eixoDoRaio.X);
		return deLado >= LadoDoPassoQueSai * passo.Length;
	}

	/// <summary>Quanto tempo ALEM do que o proprio passo pede quem sai de lado ganha pra sair de cima do raio: metade.</summary>
	public const double FolgaDaSaidaDeLado = 1.5;

	/// <summary>O piso do prazo de quem sai de lado, em segundos: o raio fino e o corpo ligeiro nao dao menos que isto.</summary>
	public const double PisoDaSaidaDeLado = 0.3;

	/// <summary>O teto do prazo, em segundos: o corpo lento demais contra o raio largo demais nao sai ileso.</summary>
	public const double TetoDaSaidaDeLado = 3;

	/// <summary>
	/// DE QUANTO E PRECISO ANDAR DE LADO PRA SAIR DE CIMA DESTE RAIO, a partir do eixo dele: a maior das duas faixas em
	/// que ele toca um corpo -- a da cabeca (`Feixe.EncostaNoCorpo`: a frente desenhada mais a meia largura do corpo) e
	/// a do tronco (`Feixe.EmCimaDoTiro`: a meia espessura desenhada mais a meia largura do corpo, com o piso do raio
	/// de impacto). 24 px na `Beam3` de escala 1; 136 num Final Flash.
	/// </summary>
	public static float LarguraPraSairDeCima(Projetil raio) =>
		MathF.Max(Feixe.ContatoNoCorpo(raio), MathF.Max(Projetil.RaioDeImpacto, Feixe.MeiaEspessuraDoTronco(raio) + Feixe.MeioCorpo));

	/// <summary>
	/// **POR QUANTO TEMPO O RAIO SOLTO NAO ENXERGA QUEM SAIU DE LADO.** Sem prazo nenhum a saida viraria apanhar: a
	/// disputa fecha com a cabeca do raio encostada nas maos dele, e no mesmo tique ela o acertaria (e, passada a
	/// cabeca, o corpo dele cortaria o tronco). Sem FIM, bastaria um passo de um pixel pra ficar parado dentro do raio
	/// sem ser tocado por ele.
	///
	/// O prazo e o tempo que o passo DELE leva pra cruzar a largura do raio, com a <see cref="FolgaDaSaidaDeLado"/>,
	/// entre o piso e o teto: quem continua andando sai ileso do raio fino e do largo; quem para no meio volta a ser
	/// alvo quando o prazo vence. E um prazo de RELOGIO, e nao a regua de "em cima do tiro" do raspao: o raio que a
	/// guarda vinha empurrando de volta ainda nem chegou nele quando a disputa fecha (`Projetil.SaiuDeLado`).
	/// </summary>
	/// <param name="larguraPx">De quanto ele precisa andar de lado (<see cref="LarguraPraSairDeCima"/>).</param>
	/// <param name="passoPxPorSegundo">A velocidade de andar dele (`MoveRules.SpeedPx`), sem a corrida.</param>
	public static double SegundosPraSairDeLado(double larguraPx, double passoPxPorSegundo) =>
		Math.Clamp(larguraPx / Math.Max(passoPxPorSegundo, 1e-6) * FolgaDaSaidaDeLado, PisoDaSaidaDeLado, TetoDaSaidaDeLado);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--embatekiteste`, familia 14): o jogo de antes de 2026-10-12 -- a disputa de guarda
	/// planta quem segura, e a unica saida dele e baixar a guarda. Lido pela propria porta da saida
	/// (`GameServer.SairDeLadoDaDisputa`). Falso em jogo, sempre.
	/// </summary>
	public static bool SemSaidaDeLadoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--embatekiteste`, familia 14): quem sai de lado nao ganha prazo nenhum -- o raio solto
	/// o enxerga no mesmo tique, e a saida vira apanhar. Lido pela propria linha que da o prazo
	/// (`GameServer.SairDeLadoDaDisputa`). Falso em jogo, sempre.
	/// </summary>
	public static bool SaidaDeLadoSemPrazoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--embatekiteste`, familia 14): quem saiu de lado e esquecido pela regua do raspao
	/// ("ja nao esta em cima do tiro"), e nao pelo prazo -- a cabeca que a guarda tinha empurrado de volta chega em
	/// quem ja voltou a ser alvo. Lido pela propria linha que o poupa da regua (`GameServer.ReverQuemSaiuDaLinha`).
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool SaidaDeLadoPelaReguaDeTeste;
}
