using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// QUAIS TECNICAS SAO ATAQUE A DISTANCIA -- e, pra cada uma, as cinco perguntas que a IA precisa
/// fazer antes de decidir atirar.
///
/// ============================ ELA FICOU VAZIA POR TRES CAMADAS, E AGORA TEM AS TRES QUE VOAM ============================
/// O cabecalho antigo dizia, com razao: *"a tabela nao pode ser preenchida por enquanto, so pra
/// testar -- uma linha aqui liga a IA a um efeito que nao existe, ela pararia de socar pra conjurar
/// um nada"*. A condicao pra preenche-la era **existir um ataque que viaja**, e ela foi cumprida:
/// a camada 1 pos no jogo a primeira entidade que sai de um corpo, anda entre tiques e acerta outro
/// (`Core.Combat.Projetil`), com tres verbs de producao -- um por tipo.
///
/// Sao esses tres que entram aqui, e nada mais. As ~33 tecnicas instantaneas continuam FORA: elas
/// nao viajam (resolvem por raio, no funil do soco), e o `Light_Buster` na mao da IA e um golpe de
/// aproximacao, nao um tiro. A distincao que este arquivo sempre defendeu -- *a tabela e de ATAQUE A
/// DISTANCIA* -- continua sendo o criterio.
///
/// O QUE ISSO LIGA, sem uma linha de codigo novo na IA: `GameServer.ArsenalDeLonge` para de devolver
/// vazio, `Cerebro.EscolherTiro` passa a ter o que escolher, `Plano.Atirar` deixa de ser inalcancavel
/// e a linha de visao volta a ser tracada (a 1 Hz, e so pra quem tem arsenal). O gesto -- recuar,
/// plantar o pe, conjurar, soltar -- ja estava escrito e nunca tinha rodado.
/// =====================================================================================================================
///
/// ============================ O RAIO E UM CANAL, E O GANCHO NAO PRECISOU MUDAR ============================
/// O `Cerebro.Disparo` solta UM PULSO e o `Ki_Wave` e um estado sustentado -- a camada 1 anotou isso
/// como pergunta em aberto ("ou o beam da IA vira tiro unico, ou o `Comando` ganha um estado de
/// canal"). O DM ja tinha respondido: `npc_beam_loop` (`BeamClash.dm:424`) canaliza e o laco morre
/// por TEMPO (`BCL_NPC_BEAM_TIME`, 12 s), e o prazo NAO corre durante uma disputa de feixes.
///
/// Entao o pulso ABRE o canal e um relogio o FECHA (`GameServer.TickDoPrazoDeRaioDaIa`). Nada mudou
/// no `Comando`, nada mudou no `Cerebro`: o gancho estava certo.
/// ======================================================================================================
///
/// ============================ POR QUE O CUSTO E UMA FUNCAO E NAO UM NUMERO ============================
/// A tentacao obvia e escrever `custoKi: 250` na linha. Isso cria a SEGUNDA COPIA DO PRECO: a
/// tecnica cobra o dela la no efeito (`Tecnicas.SolarCustoKi(f)`, `GameServer.Tecnicas.cs:58`) e a
/// IA acredita neste; no dia em que alguem reequilibrar um dos dois, a IA passa a decidir com um
/// preco que o jogo nao pratica -- e ninguem liga o defeito a esta linha.
///
/// Entao a linha aponta pra a MESMA funcao que o efeito chama. Quem registrar uma tecnica de longe
/// e nao tiver essa funcao esta dizendo, pela forma do dado, que o preco dela ainda mora dentro do
/// efeito e precisa sair de la primeiro.
///
/// E, mesmo assim, a IA trata o custo como PISO e nunca como permissao: quem recusa por falta de Ki
/// e o efeito. Uma divergencia entre os dois pode deixar a IA timida (ela nao atira quando podia);
/// nunca pode deixa-la trapacear (atirar sem pagar). Errar pro lado seguro e uma escolha.
/// ================================================================================================
/// </summary>
public static class TecnicasDeLonge
{
	/// <summary>
	/// UMA LINHA DA TABELA -- a descricao ESTATICA de uma tecnica de longe.
	///
	/// Nao confundir com <c>Jandirus.Core.Ai.Tiro</c>: aquele e o que sobra depois de resolver esta
	/// linha PARA UM CORPO (o custo vira numero, os tiles viram pixels). Esta aqui nao conhece
	/// ninguem, e por isso e dado compartilhado; aquele e resposta do jogo pra um corpo so, e por
	/// isso mora nas `Capacidades`.
	/// </summary>
	public readonly record struct Linha(
		/// <summary>O id do verb do DM -- o MESMO que viaja no `C2S.Habilidade` ("Kamehameha").</summary>
		string Id,
		/// <summary>Perto demais nao vale: quem esta colado leva soco, e nao raio. Em TILES.</summary>
		float AlcanceMinTiles,
		/// <summary>Alem disto o golpe nao chega. Em TILES.</summary>
		float AlcanceMaxTiles,
		/// <summary>Quanto tempo de pe, parado, ate o golpe sair. Segundos.</summary>
		double TempoDeConjuracao,
		/// <summary>O preco, pela MESMA funcao que o efeito cobra. Ver o cabecalho.</summary>
		Func<Fighter, double> CustoDeKi,
		/// <summary>Parede no caminho impede? Um raio sim; uma bomba em arco, nao.</summary>
		bool PrecisaDeLinhaLivre,
		/// <summary>0..1 -- o quanto ela perdoa alvo longe e alvo em movimento. 1 = nunca erra.</summary>
		double Precisao);

	/// <summary>
	/// A TABELA -- as tres tecnicas que VOAM, uma por tipo de projetil.
	///
	/// ============================ CADA NUMERO SAI DO VERB, E NENHUM E OPINIAO ============================
	/// * ALCANCE MAXIMO: os tres verbs nascem com `maxdistance = 30` tiles, e e esse o teto fisico do
	///   tiro. A IA usa MENOS que isso de proposito -- 18, 14 e 16 --, e o motivo nao e timidez: o
	///   `RiscoDeErrar` cresce ate 1 na ponta da janela, entao registrar 30 faria o cerebro rejeitar
	///   quase todo tiro por risco em vez de nunca tentar. A janela e onde ele ACERTA, nao onde o
	///   tiro chega.
	/// * ALCANCE MINIMO: 3 tiles pras bolas e 4 pro raio. Colado se soca -- e o raio pede um pouco
	///   mais porque ele PLANTA o corpo (`canmove = 0`), e plantar-se a dois tiles de alguem e um
	///   presente.
	/// * CUSTO: a MESMA expressao que o verb cobra, nunca uma copia. `10*BaseDrain` pros dois
	///   primeiros (`beams.dm:281`, `blasts.dm:44`), `600*BaseDrain` pro teleguiado
	///   (`GuidedBall.dm:30` -- o valor COBRADO, ver o defeito consertado na camada 1).
	/// * CONJURACAO: o tempo que o corpo fica de pe antes de o golpe sair. A bola sai na hora (0) e o
	///   raio pede a carga do DM. O `Guided_Ball` pede meio segundo por ser um gesto grande, e e o
	///   unico numero aqui que e escolha -- esta anotado como tal.
	/// * LINHA LIVRE: os tres precisam. Sao tiros retos que morrem no cenario (`BlockedAt`), e um
	///   NPC que atira num muro e o defeito que qualquer jogador nota em dez segundos.
	/// * PRECISAO: o teleguiado nao erra quem esta marcado (`walk_towards` corrige o rumo TODO tique,
	///   sem limite de angulo -- 0,95); o raio e rapido e comprido (0,78); a bola e o tiro lento do
	///   jogo, 107 px/s, mais devagar que alguem correndo (0,55).
	/// ================================================================================================
	/// </summary>
	private static readonly Dictionary<string, Linha> Tudo = new(StringComparer.OrdinalIgnoreCase)
	{
		["Ki_Wave"] = new("Ki_Wave", 4f, 18f, 0.7, CustoDeRaio, true, 0.78),
		["Basic_Blast"] = new("Basic_Blast", 3f, 14f, 0, CustoDeBola, true, 0.55),
		["Guided_Ball"] = new("Guided_Ball", 3f, 16f, 0.5, CustoDeTeleguiado, true, 0.95),

		// ============================ A QUARTA, E ELA E A PRIMEIRA QUE NAO PRECISA DE LINHA LIVRE ============================
		// A BALA DISPERSA (`blasts.dm:530`, lote G7) nasce ESPALHADA em volta de quem atira e converge
		// no alvo marcado de todos os angulos (`walk_towards`, `:601`). E a unica tecnica do jogo em
		// que a parede entre os dois nao decide nada: as bolas contornam porque nunca estiveram na
		// linha reta. Registrar `PrecisaDeLinhaLivre = true` aqui seria copiar o dos outros tres e
		// apagar a unica coisa que distingue esta -- a IA deixaria de usa-la exatamente na situacao
		// pra qual ela serve.
		//
		// Alcance 4..20: o piso e maior que o das bolas porque a nuvem nasce em VOLTA de quem atira
		// (a dois tiles, metade dela nasce em cima do alvo e ele nem precisa desviar); o teto e o do
		// verb, que exige alvo a ate 30 tiles, encurtado pela mesma razao dos outros tres -- a janela
		// e onde ele ACERTA, nao onde o tiro chega. Conjuracao ZERO: o corpo nao e plantado, a espera
		// de 2 s acontece com as bolas ja no ar. Precisao 0,9: elas perseguem como o teleguiado, mas
		// sao muitas e cada uma sai de um lugar, entao a nuvem perdoa mais do que uma bola so.
		// =====================================================================================================================
		["Scattering_Bullet"] = new("Scattering_Bullet", 4f, 20f, 0, CustoDeBalaDispersa, false, 0.90),
	};

	/// <summary>`10*BaseDrain` -- o mesmo que o `Ki_Wave` cobra (`beams.dm:281`).</summary>
	private static double CustoDeRaio(Fighter f) => 10 * f.BaseDrain();

	/// <summary>`10*BaseDrain` -- o mesmo que o `Basic_Blast` cobra (`blasts.dm:44`).</summary>
	private static double CustoDeBola(Fighter f) => 10 * f.BaseDrain();

	/// <summary>
	/// `600*BaseDrain` -- o que o `Guided_Ball` COBRA. O DM confere 50 e cobra 600 no mesmo verb; a
	/// camada 1 consertou isso pro valor cobrado, e a IA tem que decidir com o preco de verdade,
	/// senao ela escolheria o golpe caro achando que ele e barato e ouviria "nao" do proprio jogo.
	/// </summary>
	private static double CustoDeTeleguiado(Fighter f) => 600 * f.BaseDrain();

	/// <summary>
	/// `60*BaseDrain` -- o que a Bala Dispersa COBRA (`blasts.dm:534`).
	///
	/// O verb do DM escreve `kireq = 60*BaseDrain` e cobra `kireq*BaseDrain` (BaseDrain ao QUADRADO);
	/// o lote G7 cobra o valor conferido, e a IA decide com o preco de verdade -- senao ela julgaria
	/// o tiro caro e nunca o escolheria.
	/// </summary>
	private static double CustoDeBalaDispersa(Fighter f) => 60 * f.BaseDrain();

	// ============================ O QUE ESTA TABELA AINDA NAO SABE, E E DIVIDA MEDIDA ============================
	// Ela tem QUATRO linhas e o jogo tem VINTE E UM verbos que atiram: os tres originais, os catorze
	// do lote G5 (Masenko, Makankosappo, Raio Colossal, Final Flash, Tiro Carregado, Kill Driver,
	// Buster Shell, Tiro Disperso, Barragem de Energia, Campo Minado, Hellzone Grenade, Kienzan,
	// Paralysis, Stunlock), os seis raios do lote G6 (Kamehameha, Galick Ho, Death Beam, Dodon Ray,
	// Enkumei, Boom Wave) mais o Kikoho, e as duas do lote G7.
	//
	// Ou seja: o arsenal da IA NAO cresce sozinho quando uma tecnica de tiro e portada. Esta tabela e
	// escrita a mao, e de proposito -- cada linha declara `AlcanceMin`, `AlcanceMax` e `Precisao`,
	// que sao julgamentos sobre a JANELA em que a IA acerta, e nenhum dos tres esta escrito no DM
	// (o verb so diz `maxdistance`). Deriva-los automaticamente do `maxdistance` produziria o
	// defeito que o cabecalho da tabela ja descreve: registrar 30 faz o `RiscoDeErrar` chegar a 1 na
	// ponta e o cerebro rejeitar quase todo tiro.
	//
	// O SPIRIT GUN NAO CABE AQUI POR OUTRO MOTIVO, e ele e estrutural: o campo se chama
	// `Linha.CustoDeKi` e a IA compara com o Ki dela. O Spirit Gun cobra FOLEGO
	// (`Spirit.dm:352`). Registra-lo com o custo em Ki faria a IA achar que pode atirar quando esta
	// sem folego, e o efeito recusaria -- ou o contrario, deixando-a timida com o tanque cheio.
	// Ligar o Spirit Gun a IA pede um segundo campo de moeda nesta linha, e isso e uma decisao de
	// dado, nao um remendo de uma linha.
	// ==========================================================================================================


	/// <summary>Quantas tecnicas de longe o jogo conhece.</summary>
	public static int Quantas => Tudo.Count;

	/// <summary>
	/// AS LINHAS, pra quem quer perguntar "quais destas eu sei?" em vez de "quais verbs eu tenho?".
	///
	/// A diferenca e de CUSTO e ela apareceu numa medicao: montar a lista de TODOS os verbs de um
	/// corpo (o `TecnicasDe`) pra depois filtrar tres aloca uma lista proporcional ao livro inteiro --
	/// num personagem com o catalogo todo aprendido sao centenas de strings, uma vez por segundo, por
	/// corpo. Perguntando pelas tres, a varredura continua sendo a mesma mas nao sobra lixo.
	/// </summary>
	public static IEnumerable<Linha> Todas => Tudo.Values;

	/// <summary>
	/// EXISTE ALGUM ATAQUE DE LONGE NESTE JOGO? A poda mais barata do sistema: enquanto for falso,
	/// nenhum livro de skill e varrido e nenhuma linha de visao e tracada.
	/// </summary>
	public static bool Alguma => Tudo.Count > 0;

	/// <summary>
	/// Esta tecnica e de longe? Nulo = nao -- e e a resposta pra todas menos as tres que voam.
	///
	/// O SOLAR FLARE NAO ENTRA NA TABELA, e vale dizer por que: ele TEM alcance
	/// (`Tecnicas.SolarAlcanceTiles`) mas nao e um ataque -- cega quem esta OLHANDO, nao causa dano
	/// e nao viaja. Registra-lo faria a IA "atirar" flashes no meio do soco, comportamento que
	/// ninguem pediu. A tabela e de ATAQUE A DISTANCIA, e a distincao e essa.
	/// </summary>
	public static Linha? Get(string id) =>
		id.Length > 0 && Tudo.TryGetValue(id, out Linha l) ? l : null;
}
