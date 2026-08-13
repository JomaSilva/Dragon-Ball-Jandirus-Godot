namespace Jandirus.Core.World;

/// <summary>Onde um corpo esta em relacao a uma estrela. Ver <see cref="CalorDaEstrela.Onde"/>.</summary>
public enum ProximidadeDaEstrela
{
	/// <summary>Fora ate da coroa: a estrela nao faz nada.</summary>
	Longe,

	/// <summary>Na COROA: nao queima, mas o jogador e AVISADO. Ver <see cref="CalorDaEstrela.RazaoDaCoroa"/>.</summary>
	Coroa,

	/// <summary>DENTRO do raio: o corpo queima, por segundo, enquanto estiver ali.</summary>
	Dentro,
}

/// <summary>
/// O SOL QUEIMA -- e o item 10.4 da especificacao, com a correcao que o dono deu depois.
///
/// ============================ NAO E MORTE INSTANTANEA, E DANO POR SEGUNDO ============================
/// A spec dizia *"entrar no raio mata"*. O dono corrigiu: *"se vc cair la comeca a tomar MUITO dano"*.
/// O dono vence, e a diferenca nao e de implementacao, e de desenho:
///
///   * dano por segundo deixa o corpo TENTAR SAIR -- e a saida vira uma corrida entre o quanto o
///     corpo aguenta e a distancia ate a superficie, que e a unica coisa que faz "cair no sol" ser
///     um acidente com historia em vez de uma tela de morte;
///   * o forte dura mais que o fraco. Numa morte instantanea o Super Saiyajin e o recem-nascido
///     morrem igual, e o jogo inteiro e sobre essa diferenca.
///
/// ESTA CLASSE NAO MATA NINGUEM. Ela devolve NUMEROS. Quem fere o corpo e quem chama `Morrer()` e o
/// `GameServer.Sol.cs`, pela porta unica de <see cref="Jandirus.Core.Combat.CombatState.NegarMorte"/>
/// -- **nao existe uma segunda maneira de morrer**, e o seguro da Aura of Destruction continua
/// valendo contra o sol como vale contra soco.
/// =================================================================================================
///
/// ============================ POR QUE O NUMERO E COMPARADO COM O BP, E NAO COM O HP ============================
/// O corpo do jogo tem 100 de vida por membro e SO ISSO -- `Body.Novo()` da 100 pro recem-nascido e
/// 100 pro mais forte do universo. Dano plano por segundo daria EXATAMENTE o mesmo tempo de vida pra
/// todo mundo, que e a morte instantanea de novo, so que lenta.
///
/// Entao o que escala e o DANO, contra a moeda que este jogo usa pra dizer quem e mais forte: o
/// `expressedBP`. A estrela tem um PODER proprio (<see cref="PoderDe"/>), e o que decide o estrago e
/// a razao entre os dois. E a mesma leitura do `BpModulus` do combate -- e nao o `BpModulus` em si,
/// porque ele nao tem teto (bater em alguem 40.000x mais fraco multiplica o dano por 40.000) e um
/// sol sem teto vaporiza o novato dentro de um unico tique de 33 ms, que e morte instantanea com
/// outro nome.
/// ==========================================================================================================
///
/// ============================ OS TRES NUMEROS, E DE ONDE ELES SAEM ============================
/// A regeneracao passiva do corpo cura <b>1,67 de vida por segundo</b> fora de combate
/// (<see cref="Jandirus.Core.Combat.CombatKnobs.RegenPorSegundo"/>), e ela NAO e desligada aqui de proposito -- desligar a
/// cura por causa do sol seria escrever uma segunda regra de combate escondida num sistema de mundo.
/// A consequencia e dura e e o motivo do piso: <b>um dano por segundo abaixo de 1,67 nao mata
/// ninguem -- ele CURA</b>, e o sol viraria um teto que nunca dispara (regra 0.7 da spec).
///
/// Na menor estrela o piso poe o dano em 3,0 contra 1,67 de cura: sobram 1,33 por segundo, ou seja
/// <b>~75 s</b> ate o primeiro nucleo zerar mesmo pro corpo mais forte do jogo. Nas maiores o piso
/// SOBE com o raio (<see cref="PisoDe"/>) -- numa gigante azul o mesmo corpo dura 6,5 s. Ninguem e
/// imune, ninguem morre de surpresa, e a classe da estrela continua significando alguma coisa depois
/// que o jogador fica forte.
/// ==========================================================================================
/// </summary>
public static class CalorDaEstrela
{
	// =====================================================================
	// A GEOMETRIA
	// =====================================================================
	/// <summary>
	/// ATE ONDE VAI A COROA, em multiplos do raio -- e ela e o AVISO, nao o dano.
	///
	/// 1,75x nao e gosto: e a distancia que da tempo de frear. Numa ana vermelha (raio 360) sao
	/// 630 px, ou ~4 s de voo base; numa gigante (2.048) sao 3.584 px, ~22 s. Ou seja: quanto maior
	/// a estrela, mais cedo o aviso -- que e exatamente a ordem certa, porque tambem e dela que se
	/// leva mais tempo pra sair.
	///
	/// A coroa NAO machuca. Um aviso que ja custa vida nao e aviso, e o comeco da punicao.
	/// </summary>
	public const double RazaoDaCoroa = 1.75;

	/// <summary>
	/// A HISTERESE DA SAIDA: so se "sai" da coroa depois de 2,0x o raio.
	///
	/// Sem ela, um corpo parado bem em cima de 1,75x entraria e sairia da coroa a cada tique com o
	/// ruido de ponto flutuante da posicao, e o aviso viraria uma metralhadora no chat. E a mesma
	/// disciplina do `Hungry` da ficha, que existe pra o barrigao roncar UMA vez.
	/// </summary>
	public const double RazaoDeSaida = 2.0;

	/// <summary>
	/// ONDE ESTE PONTO ESTA em relacao a estrela. <paramref name="distancia"/> e ate o CENTRO.
	///
	/// O <paramref name="jaAvisado"/> e o que aplica a histerese: quem ja foi avisado so deixa de
	/// estar na coroa em <see cref="RazaoDeSaida"/>, quem nao foi so entra nela em
	/// <see cref="RazaoDaCoroa"/>.
	/// </summary>
	public static ProximidadeDaEstrela Onde(Estrela e, double distancia, bool jaAvisado = false)
	{
		if (distancia <= e.Raio) return ProximidadeDaEstrela.Dentro;
		double limite = (jaAvisado ? RazaoDeSaida : RazaoDaCoroa) * e.Raio;
		return distancia <= limite ? ProximidadeDaEstrela.Coroa : ProximidadeDaEstrela.Longe;
	}

	// =====================================================================
	// O PODER DE UMA ESTRELA
	// =====================================================================
	/// <summary>
	/// Raio da menor estrela do catalogo (a ana vermelha). E o divisor da curva de poder abaixo --
	/// escrito a partir da tabela e nao copiado dela, pra nao existirem dois "360" no repo.
	/// </summary>
	public static float RaioDaMenorEstrela => Sistemas.RaioDaClasse(ClasseDeEstrela.AnaVermelha);

	/// <summary>
	/// O PODER DA MENOR ESTRELA -- e ele nao e um numero inventado: e o limiar em que este jogo
	/// considera alguem "um Super Saiyajin" (`LimiaresPessoais.RestSsjatInicial`, 1e6 de BP).
	///
	/// Ou seja: a ana vermelha, que e 30% das estrelas do universo, machuca um Super Saiyajin novo
	/// tanto quanto ele machucaria a si mesmo. Abaixo disso o corpo derrete; acima, ele nada.
	/// </summary>
	public const double PoderDaMenorEstrela = Jandirus.Core.Forms.LimiaresPessoais.RestSsjatInicial;

	/// <summary>
	/// O PODER DE UMA ESTRELA, derivado do RAIO e nao de uma segunda tabela.
	///
	/// `poder = 1e6 * (raio/360)^4`. A quarta potencia esta escrita como tres multiplicacoes de
	/// proposito -- `Math.Pow` e da libm da plataforma, e este numero aparece na TELA do jogador
	/// (a tela do sistema estima quanto tempo ele aguenta), entao as duas pontas tem que chegar ao
	/// mesmo digito. Ver a mesma regra em `Sistemas.DirecaoDaOrbita`.
	///
	/// A curva foi escolhida pelas duas PONTAS, nao pelo expoente: a menor estrela vale o limiar de
	/// Super Saiyajin (1e6) e a maior vale o teto de treino do jogo (`Fighter.BpGainSoftcap`, 1e9).
	/// O 4 e o expoente que liga os dois -- e derivar dele em vez de cravar oito numeros e o que
	/// impede a tabela de classes e a de poder de divergirem no dia em que um raio mudar.
	///
	///     ana vermelha  360 ->  1,0e6      branca         980 ->  5,5e7
	///     laranja       520 ->  4,4e6      azul         1.350 ->  2,0e8
	///     amarela       700 ->  1,4e7      gigante br.  1.750 ->  5,6e8
	///                                      gigantes     2.048 ->  1,0e9
	/// </summary>
	public static double PoderDe(ClasseDeEstrela c) => PoderDoRaio(Sistemas.RaioDaClasse(c));

	/// <summary>O mesmo, a partir do raio -- e por onde a bancada varre a tabela inteira.</summary>
	public static double PoderDoRaio(double raio)
	{
		double t = raio / RaioDaMenorEstrela;
		return PoderDaMenorEstrela * t * t * t * t;
	}

	// =====================================================================
	// O DANO
	// =====================================================================
	/// <summary>
	/// DANO POR SEGUNDO A CADA MEMBRO quando o poder do corpo IGUALA o da estrela.
	///
	/// Dez por segundo contra 1,67 de regeneracao dao 8,33 liquidos: um corpo inteiro (100 por
	/// membro) leva <b>~12 s</b> pra ter o primeiro nucleo zerado. E o numero central da mecanica --
	/// tempo de sobra pra virar e nadar pra fora de uma ana vermelha, tempo curto demais pra
	/// atravessar uma gigante.
	/// </summary>
	public const double DanoDeParidade = 10.0;

	/// <summary>
	/// PISO DO FATOR **NA MENOR ESTRELA** -- e ele existe porque a regeneracao existe (ver o cabecalho).
	///
	/// 0,30 x 10 = 3,0 por segundo contra 1,67 de cura: <b>~75 s</b> pro corpo mais forte do jogo
	/// dentro de uma ana vermelha. Qualquer valor abaixo de 0,167 faria o sol CURAR o jogador, e o
	/// sistema inteiro viraria um teto que nunca dispara.
	///
	/// Ver <see cref="PisoDe"/> pro que acontece nas estrelas maiores -- ele NAO e o mesmo numero.
	/// </summary>
	public const double FatorMinimo = 0.30;

	/// <summary>
	/// O PISO CRESCE COM O RAIO -- e esta linha foi escrita porque a tabela da bancada mostrou o
	/// contrario do que o desenho queria.
	///
	/// ============================ O QUE A TABELA MOSTROU ============================
	/// Com um piso CONSTANTE (0,30 pra toda estrela), a coluna do corpo de 1e13 de BP dava **75 s em
	/// todas as oito classes**: da ana vermelha de 360 px a gigante azul de 2.048. Ou seja, passado
	/// certo poder, a classe da estrela deixava de significar qualquer coisa -- e a escada de oito
	/// classes, a arte das 32 folhas e a tabela de pesos viravam enfeite pro fim de jogo inteiro.
	///
	/// O conserto sai da propria curva de poder e nao de uma constante nova: como
	/// `poder = (raio/360)^4`, a RAIZ QUARTA do poder e exatamente `raio/360`. Entao o piso escala
	/// com o raio, que e a mesma grandeza que o jogador VE na tela.
	///
	///     ana vermelha  piso 0,30 -> dano  3,0/s -> 75 s      azul          piso 1,13 -> 11,3/s -> 10,4 s
	///     laranja       piso 0,43 ->  4,3/s -> 38 s           gigante br.   piso 1,46 -> 14,6/s ->  7,7 s
	///     amarela       piso 0,58 ->  5,8/s -> 24 s           gigantes      piso 1,71 -> 17,1/s ->  6,5 s
	///     branca        piso 0,82 ->  8,2/s -> 15 s
	///
	/// E o numero que isso compra e o melhor do sistema: **o corpo mais forte do jogo leva 6,5 s numa
	/// gigante azul, e atravessa-la leva 25,6 s**. Ninguem sai do meio de uma gigante, ninguem morre
	/// se so encostou na borda, e o quanto se afundou passa a ser a pergunta.
	/// ==============================================================================
	/// </summary>
	public static double PisoDe(double raioDaEstrela) =>
		FatorMinimo * (raioDaEstrela / RaioDaMenorEstrela);

	/// <summary>
	/// TETO SUAVE DO FATOR. Nao e um corte: e a saturacao de `f = r / (1 + r/Teto)`, que vale ~r
	/// enquanto r e pequeno e encosta em 8 quando r cresce.
	///
	/// Uma hiperbole e nao um `Math.Min` porque o corte duro criaria um patamar visivel -- todo
	/// mundo abaixo de um oitavo do poder da estrela morrendo no MESMO instante --, e o patamar
	/// e justamente a leitura de "morte instantanea" que este desenho recusa. Com a saturacao o
	/// tempo continua andando ate o fim, so que cada vez mais devagar.
	///
	/// 8 x 10 = 80 por segundo: o corpo mais fraco que existe dura <b>~1,3 s</b> dentro de uma
	/// estrela. E pouco de proposito -- um recem-nascido nao sobrevive a um sol --, mas ainda e
	/// tempo de ver o aviso e de a coroa ter avisado antes.
	/// </summary>
	public const double TetoSuaveDoFator = 8.0;

	/// <summary>
	/// O FATOR: quanto o dano de paridade e multiplicado, dado o raio da estrela e o BP do corpo.
	///
	/// ENTRA O RAIO E NAO O PODER, e nao e detalhe de assinatura: as DUAS pontas da curva saem do
	/// raio (o poder por <see cref="PoderDoRaio"/>, o piso por <see cref="PisoDe"/>), e passar o
	/// poder obrigaria quem chama a lembrar de passar o piso junto -- que e o tipo de parametro que
	/// um dia alguem esquece, e o esquecimento nao aparece em teste: o dano so fica errado no fim
	/// do jogo, na estrela grande.
	///
	/// So `+`, `*` e `/` -- ver <see cref="PoderDoRaio"/> pra o porque.
	/// </summary>
	public static double Fator(double raioDaEstrela, double bpExpresso)
	{
		double r = PoderDoRaio(raioDaEstrela) / Math.Max(bpExpresso, 1);
		double f = r / (1 + r / TetoSuaveDoFator);
		double piso = PisoDe(raioDaEstrela);
		return f < piso ? piso : f;
	}

	/// <summary>
	/// QUANTO ESTA ESTRELA TIRA DE CADA MEMBRO POR SEGUNDO deste corpo.
	///
	/// NAO DEPENDE DA PROFUNDIDADE, e isso e escolha: a superficie queima tanto quanto o nucleo.
	/// Um gradiente por profundidade tornaria a beirada quase inofensiva e a estrela viraria uma
	/// rosquinha -- e o gradiente que importa ja existe e e o certo: quanto mais fundo se entra,
	/// mais longe fica a saida. A corrida e essa.
	/// </summary>
	public static double DanoPorSegundo(Estrela e, double bpExpresso) =>
		DanoDeParidade * Fator(e.Raio, bpExpresso);

	/// <summary>
	/// ESTIMATIVA DE QUANTO TEMPO ESTE CORPO AGUENTA, em segundos, a partir da vida do membro mais
	/// ferido e ja descontando a regeneracao passiva.
	///
	/// E ESTIMATIVA e o nome esta certo: ela supoe o corpo parado, inteiro e ACORDADO dentro da
	/// estrela. Serve pra a TELA e pra a bancada ter contra o que comparar o tempo medido.
	///
	/// ============================ O QUE ELA NAO MODELA: DESMAIAR ACELERA A MORTE ============================
	/// Nao e uma regra escrita aqui -- e uma consequencia de duas que ja existiam, e a bancada ao vivo
	/// e que a revelou. Quando um nucleo cai abaixo de 20% o corpo NOCAUTEIA, e ai:
	///
	///   * `expressedBP` despenca pra **um decimo do BP** (`Fighter.Power.cs:125`), e o dano da estrela
	///     escala com ele -- um veterano passa de 5,8 pra 21,1 de dano por segundo no instante em que
	///     apaga;
	///   * a regeneracao passiva PARA (ela recusa corpo nocauteado).
	///
	/// Medido: o veterano de 5e7 dura 20,2 s e o corpo de 1e13 dura 22,6 s num sol amarelo, contra os
	/// ~24 s que esta funcao preve pros dois. Ou seja a estimativa e um LIMITE SUPERIOR e erra pra
	/// menos de 10% -- e o jogo mata um pouco antes do que a tela promete, que e o lado seguro do erro.
	///
	/// E a leitura de jogo e boa demais pra ser corrigida: **voce apaga, e ai voce cozinha.**
	/// ==================================================================================================
	///
	/// Devolve `double.PositiveInfinity` se a cura empatar com o dano -- que e o caso que o
	/// <see cref="FatorMinimo"/> existe pra impedir, e por isso a bancada afirma que ele nunca sai.
	/// </summary>
	public static double SegundosAteCeder(Estrela e, double bpExpresso, double vidaDoMembro,
										  double regenPorSegundo)
	{
		double liquido = DanoPorSegundo(e, bpExpresso) - Math.Max(regenPorSegundo, 0);
		return liquido <= 0 ? double.PositiveInfinity : vidaDoMembro / liquido;
	}
}
