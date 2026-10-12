using Jandirus.Core.World;

namespace Jandirus.Core.Npc;

/// <summary>
/// EM QUE PE ESTA A HISTORIA DO BILLS. Vai pro disco (`visita-de-bills.json`).
///
/// So anda pra frente: quem a move e o calendario (<see cref="BillsEWhis.Passo"/>) e a derrota dele na Terra
/// (o servidor). A LIMPEZA TOTAL a devolve pra <see cref="Dormindo"/>.
/// </summary>
public enum FaseDaVisita : byte
{
	/// <summary>A saga de Majin Boo ainda nao foi vencida. Ninguem no mundo.</summary>
	Dormindo = 0,

	/// <summary>Boo caiu; o mes in-game ate a visita esta correndo.</summary>
	ACaminho = 1,

	/// <summary>Ele esta na Terra, com o Whis ao lado, esperando quem o enfrente.</summary>
	NaTerra = 2,

	/// <summary>Foi derrubado na Terra (ou a visita acabou): os dois estao em casa, e as coordenadas sao publicas.</summary>
	EmCasa = 3,
}

/// <summary>O estado da visita que atravessa reinicio. Os CORPOS nao estao aqui: eles sao deduzidos (ver o servidor).</summary>
public sealed class EstadoDaVisita
{
	public FaseDaVisita Fase = FaseDaVisita.Dormindo;

	/// <summary>
	/// O dia in-game (o indice do `GameServer.DiaDoMundo`) em que ele pousa. So vale em
	/// <see cref="FaseDaVisita.ACaminho"/>; -1 fora dela.
	/// </summary>
	public long DiaDaChegada = -1;

	/// <summary>Quem o derrubou na Terra (o nome que o mundo ouviu). Vazio se a visita acabou sem luta.</summary>
	public string DerrubadoPor = "";
}

/// <summary>O que o calendario fez neste passo. Ver <see cref="BillsEWhis.Passo"/>.</summary>
public enum PassoDaVisita : byte { Nada = 0, Despertou = 1, Pousou = 2 }

/// <summary>Quem fala uma linha do roteiro.</summary>
public enum QuemFala : byte { Bills = 0, Whis = 1 }

/// <summary>Uma linha de roteiro: quem diz e o que diz. `{coordenadas}`, `{mundo}` e `{nome}` sao trocados na hora.</summary>
public readonly record struct Fala(QuemFala Quem, string Texto);

/// <summary>O endereco de um mundo gerado na carta: a celula do sistema, a orbita e o corpo.</summary>
public readonly record struct CasaDeBills(PlanetaNoEspaco Corpo, int Sx, int Sy, int K, double Gravidade);

/// <summary>
/// BILLS E WHIS -- pedido do dono em 2026-10-11. NAO EXISTE NO DM: e regra dele, do comeco ao fim.
///
///   *"eles sao npcs q vao existir ate um player pegar um cargo de Deus da Destruicao ou anjo (...) o npc bills
///   sempre anda com o npc whis perto, apos os jogadores terem derrotado o vilao de saga majin boo, 1 mes in game
///   dps o bills ira pra terra ao qual os jogadores podem enfrentalo, o poder dele em 100% e 1 trilhao mas ele
///   luta usando apenas 15% do poder dele (150 bilhoes) e caso os players derrotem ele, ele vai falar q eles podem
///   visitar o planeta onde ele mora pra treinarem (vai ser um planeta procedural aleatorio de gravidade
///   altissima, ele fala as coordenadas galaticas ...) no planeta do bills ele e o whis vao estar parados como um
///   objeto q da pra interagir, ao interagir vai abrir um menu de opcoes (...) o whis sempre tem o triplo do bp
///   do player q o chamou pra lutar e sempre esta usando o instinto superior com 100% de maestria (...) lutar com
///   o whis acelera a maestria dela. falar com o bills tem outras opcoes, 1: e a mesma coisa do whis, porem (...)
///   caso vc ja tenha a skill do ultra ego, ajuda a dominar mais rapido, e a segunda opcao a desafia-lo ao qual
///   ele vai usar 100% do poder dele e usar a forma fury (...) e se o player derrotar o bills, ele vira o novo
///   deus da destruicao."*
///
/// AQUI FICAM OS NUMEROS (todos com nome), as contas puras, a escolha do planeta e as FALAS. Quem poe os corpos
/// no mundo, guia as lutas e troca o titulo e o servidor (`GameServer.Bills.cs` e `GameServer.BillsTreino.cs`).
///
/// ============================ A REGRA PRINCIPAL DO BP VALE AQUI DENTRO ============================
/// O trilhao, os 15% e "o triplo" sao regra de SERVIDOR. Nenhuma fala, rotulo de menu ou pacote deste sistema
/// escreve numero de poder -- o que o jogador sabe do Bills e o que o scouter dele ja mostrava de qualquer corpo.
/// ==================================================================================================
/// </summary>
public static class BillsEWhis
{
	// =====================================================================
	// 1. QUEM ELES SAO
	// =====================================================================

	/// <summary>Os ids dos moldes no `npcs.json`.</summary>
	public const string MoldeDoBills = "bills", MoldeDoWhis = "whis";

	/// <summary>Este id de molde e de um dos dois?</summary>
	public static bool EhMolde(string? moldeId) =>
		string.Equals(moldeId, MoldeDoBills, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(moldeId, MoldeDoWhis, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// AS "RACAS" QUE SO ELES CARREGAM. Fora do `races.json` de proposito, como a do bicho (`Animais.Raca`): nao ha
	/// proto, classe nem arvore racial -- a ficha sai NEUTRA (todo multiplicador em 1) e o poder deles e o BP que o
	/// servidor crava. E por elas que as duas pontas sabem que o corpo e UMA folha de sprite (ver
	/// <see cref="FolhaDoCorpo"/>): o Whis e, nas palavras do dono, "um ser da raca anjo".
	/// </summary>
	public const string RacaDoBills = "Hakaishin", RacaDoWhis = "Anjo";

	public static bool EhRacaDivina(string? raca) =>
		string.Equals(raca, RacaDoBills, StringComparison.Ordinal)
		|| string.Equals(raca, RacaDoWhis, StringComparison.Ordinal);

	/// <summary>
	/// A FOLHA DO CORPO de quem carrega uma destas racas (nulo pra todo o resto do jogo). Sao as duas que o dono
	/// apontou em `Assets/Sprites/Character Icons/`. A do Bills vem SEM ROUPA: quem o veste e a peca
	/// <see cref="Traje"/>, pela camada de roupa de todo corpo.
	/// </summary>
	public static string? FolhaDoCorpo(string? raca) =>
		string.Equals(raca, RacaDoBills, StringComparison.Ordinal)
			? "res://Assets/Sprites/Character Icons/Beerus (God of Destruction).tres"
		: string.Equals(raca, RacaDoWhis, StringComparison.Ordinal)
			? "res://Assets/Sprites/Character Icons/Whis.tres"
		: null;

	/// <summary>
	/// O TRAJE -- o nome do arquivo da peca (`Assets/Sprites/Clothes/Beerus Inspired Suit (DU).png`). O Bills o
	/// veste pelo molde, e todo jogador que GANHA o titulo de Deus da Destruicao recebe um na mochila
	/// (`GameServer.ReconciliarOTrajeDoDeus`). Ele mora na lista `armaduras` do `visual.json`: vestivel, e fora da
	/// grade de roupas da criacao de personagem -- e por morar la, na mochila ele e uma peca de ARMADURA
	/// (`RoupaGuardada.PrefixoDeArmadura`), como toda folha daquela pasta.
	/// </summary>
	public const string Traje = "Beerus Inspired Suit (DU)";

	/// <summary>Os cargos (as chaves do `Ranks.cs`) que cada um guarda enquanto nenhum jogador os toma.</summary>
	public const string CargoDoBills = Ranks.KitDoDeus.Cargo, CargoDoWhis = "angel";

	/// <summary>
	/// ELE EXISTE? -- **funcao pura do trono**: existe enquanto o cargo DELE estiver vago. Sem estado a mais: o
	/// titulo tomado (por duelo, por admin ou vencendo o proprio Bills) tira o corpo do mundo, e o titulo que
	/// vagar de novo o devolve.
	/// </summary>
	public static bool Existe(bool cargoVago) => !ExisteSempreDeTeste ? cargoVago : true;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--billsteste`): o corpo deixa de olhar o trono -- o Bills continua no mundo com um
	/// jogador carregando o titulo dele. Lido por <see cref="Existe"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool ExisteSempreDeTeste;

	// =====================================================================
	// 2. O PODER
	// =====================================================================

	/// <summary>O poder do Bills a 100%: um trilhao.</summary>
	public const double PoderTotal = 1_000_000_000_000;

	/// <summary>Na Terra ele luta com esta fracao do poder: 15%, ou cento e cinquenta bilhoes.</summary>
	public const double FracaoNaTerra = 0.15;

	/// <summary>O BP BASE cravado no corpo dele na Terra (e o que o scouter de quem o tem mede com ele descansado).</summary>
	public static double PoderNaTerra => PoderTotal * (FracaoDeTeste > 0 ? FracaoDeTeste : FracaoNaTerra);

	/// <summary>
	/// DEFEITO INJETADO (bancada): a fracao com que ele luta na Terra (1 = o poder inteiro). Lido por
	/// <see cref="PoderNaTerra"/>. Zero em jogo, sempre.
	/// </summary>
	public static double FracaoDeTeste;

	/// <summary>
	/// O TREINO: o adversario tem este tanto do poder de quem o chamou. O dono: "o whis sempre tem o triplo do bp do
	/// player q o chamou pra lutar" -- e o treino com o Bills "e a mesma coisa".
	///
	/// DECISAO (o dono nao disse de QUAL poder): e o EXPRESSO de quem treina, medido A CADA TIQUE. O base nao
	/// serviria -- um Super Saiyajin (50x) atropelaria um adversario de 3x o base dele --, e medir so na hora do
	/// pedido deixaria quem se transforma no meio da luta passar na frente. "Sempre" e sempre: carregou ki,
	/// transformou, cansou -- o adversario acompanha.
	/// </summary>
	public const double VezesOPoderDoParceiro = 3;

	/// <summary>O poder expresso que o adversario de treino tem de ter agora.</summary>
	public static double PoderDoTreino(double expressoDoParceiro) =>
		Math.Max(expressoDoParceiro, 1) * (TreinoSemMultiploDeTeste ? 1 : VezesOPoderDoParceiro);

	/// <summary>DEFEITO INJETADO (bancada): o adversario de treino fica com o MESMO poder do parceiro. Lido por <see cref="PoderDoTreino"/>.</summary>
	public static bool TreinoSemMultiploDeTeste;

	/// <summary>
	/// O BP BASE que da ao corpo o poder EXPRESSO pedido. O expresso de um corpo e o base vezes o estado dele
	/// (forma, ki, vida, raiva): divide-se por essa razao, medida no proprio corpo, pra o numero que VALE na luta
	/// ser o pedido -- e nao um base que a primeira ferida derruba. Razao fora de [1/8, 8] (nocauteado, por
	/// exemplo, o expresso cai a um decimo) e ignorada: o corpo fica com o base pedido.
	/// </summary>
	public static double BaseParaOExpresso(double expressoPedido, double baseDeAgora, double expressoDeAgora)
	{
		if (baseDeAgora <= 0 || expressoDeAgora <= 0) return expressoPedido;
		double razao = expressoDeAgora / baseDeAgora;
		return razao is < 0.125 or > 8 ? expressoPedido : expressoPedido / razao;
	}

	/// <summary>
	/// O DESAFIO PELO TITULO: "ele vai usar 100% do poder dele e usar a forma fury". Leitura adotada: o trilhao e o
	/// BASE, e a Fury multiplica POR CIMA (72x no catalogo) -- as duas coisas somadas sao o que faz do titulo o
	/// topo do jogo. Com `false`, o trilhao passa a ser o poder JA NA FURY (o base e dividido pelo multiplicador).
	/// </summary>
	public const bool AFuryMultiplicaOTotal = true;

	/// <summary>
	/// O id da forma Fury no catalogo de formas: o do kit do Deus da Destruicao (<see cref="Ranks.KitDoDeus.IdDaFury"/>).
	/// Este sistema so a usa numa funcao (`GameServer.BillsVesteAFury`), pela porta do kit.
	/// </summary>
	public const string IdDaFury = Ranks.KitDoDeus.IdDaFury;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--billsteste`): o Bills aceita o desafio SEM entrar na Fury -- o titulo passaria a
	/// valer um trilhao seco. Lido pela propria linha que o poe na forma (`GameServer.BillsVesteAFury`).
	/// </summary>
	public static bool DesafioSemFuryDeTeste;

	/// <summary>O BP base do Bills no desafio, dado o multiplicador que a Fury tem no catalogo.</summary>
	public static double PoderNoDesafio(double multDaFury) =>
		AFuryMultiplicaOTotal || multDaFury <= 0 ? PoderTotal : PoderTotal / multDaFury;

	// ---- o kit do Deus da Destruicao nas lutas dele (dono, 2026-10-11) ----

	/// <summary>
	/// O HAKAI VALE NA LUTA DA TERRA? O dono: "faca o bills usar o kit completo na primeira luta (15% do poder
	/// total) menos o fury" -- e o kit completo tem o Hakai e a Destroyer Sphere, que APAGAM (a morte de verdade, e o
	/// Enma cobra em dobro) quem estiver com o torso em 15% ou menos e for ao menos 25% mais fraco que ele.
	///
	/// ============================ UMA LINHA PRA DESLIGAR ============================
	/// Com `true`, a luta da Terra PODE MATAR: quem cai no ponto do Hakai e executado em vez de so ficar no chao.
	/// Com `false`, o Bills da Terra usa o resto do kit (o Fury de longe, o peteleco, a anulacao e o surto) e NAO
	/// executa ninguem: o verb do Hakai recusa, e a Destroyer Sphere -- que e o Hakai a distancia -- nem e largada.
	/// ================================================================================
	/// </summary>
	public const bool HakaiNaTerra = true;

	/// <summary>
	/// O HAKAI VALE NO DESAFIO PELO TITULO? A mesma chave, pra a outra luta em que ele carrega o kit (la, com a Fury
	/// por cima). Com `true`, o desafiante que cai no ponto do Hakai e apagado em vez de so perder o duelo.
	/// </summary>
	public const bool HakaiNoDesafio = true;

	/// <summary>
	/// A BANCADA VIRA AS DUAS CHAVES DE CIMA pra `false` (elas sao constantes: e esta flag que prova que cada uma
	/// desliga o que diz). Lida pelo <see cref="OBillsExecuta"/>. Falsa em jogo, sempre.
	/// </summary>
	public static bool HakaiDesligadoDeTeste;

	/// <summary>O Hakai do Bills vale na luta em que ele esta entrando? -- a Terra ou o desafio, cada uma com a sua chave.</summary>
	public static bool OBillsExecuta(bool noDesafio) => (noDesafio ? HakaiNoDesafio : HakaiNaTerra) && !HakaiDesligadoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): o Bills acorda na Terra SEM o kit -- so o soco e o que ele sabe de livro, como era antes. Falso em jogo, sempre.</summary>
	public static bool SemKitNaTerraDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): na Terra ele TAMBEM entra na Fury -- o "menos o fury" do dono ignorado. Falso em jogo, sempre.</summary>
	public static bool FuryNaTerraDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): o TREINO com o Bills tambem arma o kit -- e treino deixaria de ser "ninguem morre aqui". Falso em jogo, sempre.</summary>
	public static bool KitNoTreinoDeTeste;

	/// <summary>
	/// NO DESAFIO, quanto tempo o juiz espera o Bills executar o desafiante que caiu NO PONTO DO HAKAI antes de dar o
	/// duelo por encerrado do jeito de sempre (os dois inteiros). Sem esta espera o Hakai do desafio nao existiria: o
	/// juiz encerra no nocaute, e e no chao que o criterio se cumpre.
	/// </summary>
	public const double SegundosPraExecutar = 6;

	// =====================================================================
	// 3. A LINHA DO TEMPO
	// =====================================================================

	/// <summary>O elo da cadeia de sagas (`npcs.json`) cuja vitoria acorda o Bills.</summary>
	public const string SagaQueOAcorda = "boo";

	/// <summary>
	/// UM MES IN-GAME EM DIAS. O calendario do original tem meses de 28 dias (`if(Days>=29)`, `WorldClock.dm:19`),
	/// e este port ja conta o `Year` do DM por essa regua (`Magic.Esferas.DiasPorAnoDoDm` = 10 meses = 280 dias).
	/// Com o dia de 24 minutos (`Ceu.SegundosPorDia`), o mes sao 11,2 horas reais.
	/// </summary>
	public const int DiasPorMes = 28;

	/// <summary>"1 mes in game dps o bills ira pra terra": quantos dias in-game entre Boo cair e ele pousar.</summary>
	public const int DiasAteAVisita = 1 * DiasPorMes;

	/// <summary>
	/// O PASSO DO CALENDARIO -- puro: recebe o estado e o que o servidor sabe do mundo, escreve a fase e devolve o
	/// que aconteceu. So cobre as duas viradas que sao do RELOGIO; a derrota e o fim da visita sao do servidor.
	///
	///   Dormindo -> ACaminho   quando a saga de Boo esta VENCIDA (so vencida: vilao que ganha ou saga cancelada
	///                          nao e "os jogadores terem derrotado"). A chegada fica marcada pra daqui a um mes.
	///   ACaminho -> NaTerra    quando o dia chegou E a Terra existe. Com a Terra destruida ele espera: o prazo
	///                          vencido nao se perde, e ele pousa no dia em que ela voltar.
	///
	/// O RELOGIO PODE RECUAR (a manivela das bancadas): o dia marcado e absoluto, entao recuar so o empurra de
	/// volta pro futuro -- nada do que ja aconteceu se desfaz.
	/// </summary>
	public static PassoDaVisita Passo(EstadoDaVisita e, bool booVencido, long hoje, bool terraDePe)
	{
		switch (e.Fase)
		{
			case FaseDaVisita.Dormindo:
				if (!booVencido) return PassoDaVisita.Nada;
				e.Fase = FaseDaVisita.ACaminho;
				e.DiaDaChegada = hoje + (EsperaDeTeste >= 0 ? EsperaDeTeste : DiasAteAVisita);
				return PassoDaVisita.Despertou;

			case FaseDaVisita.ACaminho:
				if (hoje < e.DiaDaChegada || !terraDePe) return PassoDaVisita.Nada;
				e.Fase = FaseDaVisita.NaTerra;
				e.DiaDaChegada = -1;
				return PassoDaVisita.Pousou;

			default:
				return PassoDaVisita.Nada;
		}
	}

	/// <summary>
	/// DEFEITO INJETADO / MANIVELA (bancada e a flag `--billsdias N` do servidor): quantos dias in-game de espera no
	/// lugar do mes. Negativo = a regra de verdade. Lido por <see cref="Passo"/>.
	/// </summary>
	public static int EsperaDeTeste = -1;

	// =====================================================================
	// 4. A LUTA NA TERRA
	// =====================================================================

	/// <summary>
	/// O "lugar" de habitante em que ele pousa na Terra (o terceiro argumento da semente de todo NPC, e o indice da
	/// vaga do `Habitat`): um ponto de terra firme do mapa, o mesmo em todo reinicio do mesmo universo -- pela
	/// mesma funcao que poe os viloes de saga no mundo.
	/// </summary>
	public const ulong LugarNaTerra = 7_101_100;

	/// <summary>O Whis fica a este tanto (em tiles) do lado do Bills, onde quer que os dois estejam.</summary>
	public const float TilesEntreOsDois = 2;

	/// <summary>
	/// O TERRITORIO da luta na Terra, em tiles a partir de onde ele cochila: quem o ataca e sai deste raio deixa de
	/// ser perseguido. E o que impede arrasta-lo pelo mapa batendo de longe -- e o que o mantem perto do Whis.
	/// </summary>
	public const float TerritorioTiles = 40;

	/// <summary>
	/// Sem ninguem de pe pra enfrentar dentro do territorio por estes segundos, ele volta a cochilar INTEIRO (vida,
	/// ki e folego cheios). A visita nao tem prazo: ele fica na Terra ate alguem o derrubar.
	/// </summary>
	public const double SegundosAteVoltarACochilar = 6;

	/// <summary>Quanto tempo ele fica no chao antes de se levantar e falar, depois de derrubado.</summary>
	public const double SegundosNoChao = 3;

	/// <summary>O intervalo entre duas linhas de um roteiro falado, em segundos (da tempo de ler).</summary>
	public const double SegundosEntreFalas = 3.6;

	// ---- o Whis: ao lado dele fora da luta, de longe na luta (dono, 2026-10-11) ----

	/// <summary>
	/// "o whis so segue o bills fora de combate, ao comecar uma luta o whis fica vendo de longe": a quantos tiles do
	/// MEIO DA LUTA (entre o Bills e quem ele enfrenta) ele vai se por pra assistir. Sem chao a essa distancia do lado
	/// em que ele esta, ele assiste de um pouco mais perto -- nunca de menos que <see cref="TilesPertoDemais"/> mais um.
	/// </summary>
	public const float TilesDoMirante = 9;

	/// <summary>A luta chegou a menos disto (em tiles) de onde ele assiste -- o Bills ou quem o enfrenta: ele escolhe outro lugar.</summary>
	public const float TilesPertoDemais = 6;

	/// <summary>A luta foi pra mais longe do que isto (em tiles) de onde ele assiste: ele se reaproxima, pra continuar vendo.</summary>
	public const float TilesLongeDemais = 15;

	/// <summary>
	/// Quanto tempo de luta antes do primeiro comentario dele, e de quanto em quanto tempo vem o seguinte (mais uma
	/// folga sorteada): espacado pra nao entupir o chat de quem luta.
	/// </summary>
	public const double SegundosAteOPrimeiroPalpite = 5, SegundosEntrePalpites = 18, FolgaEntrePalpites = 10;

	/// <summary>Acima desta distancia (em tiles) do lugar pra onde vai, um dos dois corre; abaixo, anda.</summary>
	public const float TilesPraCorrer = 6;

	/// <summary>
	/// Um dos dois ANDANDO pra um lugar e sem chegar mais perto por este tempo esta empacado (o passo de quem o
	/// servidor dirige nao contorna obstaculo): ele aparece no lugar. Deus preso atras de um pinheiro e pior do que
	/// deus que pisca.
	/// </summary>
	public const double SegundosEmpacado = 1.5;

	/// <summary>...e sem chegar mais perto por este tempo (bem menor) ele primeiro tenta sair de lado: e o que contorna um corpo no caminho.</summary>
	public const double SegundosAteDesviar = 0.2;

	/// <summary>DEFEITO INJETADO (bancada): com o Bills lutando, o Whis continua colado nele (a dois tiles), como era antes. Falso em jogo, sempre.</summary>
	public static bool WhisColadoNaLutaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): acabada a luta, o Whis fica onde estava assistindo -- nao volta pro lado do Bills. Falso em jogo, sempre.</summary>
	public static bool WhisNaoVoltaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): assistindo, o Whis pode ser atingido. Falso em jogo, sempre.</summary>
	public static bool WhisTocavelNaLutaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): o Whis assiste calado. Falso em jogo, sempre.</summary>
	public static bool WhisCaladoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): o Whis repete o MESMO comentario toda vez. Falso em jogo, sempre.</summary>
	public static bool WhisRepeteDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): desistindo da briga, o Bills aparece no posto num passo so (o teleporte de antes)
	/// -- e nao ha caminhada pro Whis acompanhar. Falso em jogo, sempre.
	/// </summary>
	public static bool BillsVoltaNumPassoDeTeste;

	/// <summary>
	/// A ORDEM EM QUE OS COMENTARIOS SAEM: todos os indices de 0 a `quantos - 1` embaralhados -- cada comentario sai
	/// uma vez antes de qualquer um repetir --, com o que sai PRIMEIRO (o ultimo da lista: quem usa tira do fim)
	/// diferente do `ultimo` dito, pra a emenda de uma rodada na outra tambem nao repetir.
	/// </summary>
	public static int[] SacolaDePalpites(int quantos, int ultimo, Random rng)
	{
		int[] ordem = new int[Math.Max(quantos, 0)];
		for (int i = 0; i < ordem.Length; i++) ordem[i] = i;
		for (int i = ordem.Length - 1; i > 0; i--)
		{
			int j = rng.Next(i + 1);
			(ordem[i], ordem[j]) = (ordem[j], ordem[i]);
		}
		if (ordem.Length > 1 && ordem[^1] == ultimo) (ordem[0], ordem[^1]) = (ordem[^1], ordem[0]);
		return ordem;
	}

	// =====================================================================
	// 5. A CASA -- "um planeta procedural aleatorio de gravidade altissima"
	// =====================================================================

	/// <summary>
	/// A regiao da busca, em celulas de sistema pra cada lado da celula da Terra: 17x17 celulas, a mesma regiao em
	/// que o jogo ja garante os mundos de atmosfera divina (`AtmosferaDivina.RaioGarantidoEmCelulas`). Sao perto de
	/// mil mundos gerados; o canto mais longe fica a ~11 celulas da Terra.
	/// </summary>
	public const int RaioDaBuscaEmCelulas = 8;

	/// <summary>Quantos mundos a lista guarda, do mais pesado pra baixo. Os de reserva sao pra quando o primeiro for destruido.</summary>
	public const int CasasGuardadas = 6;

	private const ulong SalDaCasa = 0xB111_5CA5_A000_0001UL;

	private sealed record Lista(ulong Universo, CasaDeBills[] Casas);

	/// <summary>A lista do ultimo universo perguntado. Uma entrada basta: servidor e cliente do host dividem o processo e a semente.</summary>
	private static volatile Lista? _ultima;

	/// <summary>
	/// AS CASAS POSSIVEIS, da mais pesada pra baixo -- **funcao pura da semente do universo**. "Aleatorio" e isto:
	/// cada universo sorteado tem a sua, a LIMPEZA TOTAL (que troca a semente) a muda de lugar sozinha, e nenhum
	/// arquivo guarda nada. A primeira da lista e o mundo gerado de MAIOR GRAVIDADE da regiao (a gravidade de mundo
	/// gerado vai de 1 a 80: em quase todo universo a casa dele passa de 75x); o empate se decide por um peso
	/// proprio, e nao pela ordem da varredura.
	/// </summary>
	public static IReadOnlyList<CasaDeBills> Casas(ulong seedDoUniverso)
	{
		Lista? l = _ultima;
		if (l != null && l.Universo == seedDoUniverso) return l.Casas;

		var todas = new List<(double G, ulong Peso, CasaDeBills Casa)>();
		for (int sy = -RaioDaBuscaEmCelulas; sy <= RaioDaBuscaEmCelulas; sy++)
			for (int sx = -RaioDaBuscaEmCelulas; sx <= RaioDaBuscaEmCelulas; sx++)
			{
				if (Sistemas.Do(seedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					if (k == s.OrbitaPreFeita) continue;
					PlanetaNoEspaco p = s.Planeta(k);
					double g = MundoProcedural.GravidadeDaSeed(p.Seed);
					todas.Add((g, Espaco.Misturar(p.Seed ^ SalDaCasa, 1, 2), new CasaDeBills(p, sx, sy, k, g)));
				}
			}

		todas.Sort((a, b) =>
			a.G != b.G ? b.G.CompareTo(a.G)
			: a.Peso != b.Peso ? a.Peso.CompareTo(b.Peso)
			: a.Casa.Corpo.Seed.CompareTo(b.Casa.Corpo.Seed));

		var casas = new CasaDeBills[Math.Min(CasasGuardadas, todas.Count)];
		for (int i = 0; i < casas.Length; i++) casas[i] = todas[i].Casa;

		_ultima = new Lista(seedDoUniverso, casas);
		return casas;
	}

	/// <summary>
	/// A CASA DE AGORA: a primeira da lista que ainda existe. Quem sabe o que foi destruido e o servidor -- por isso
	/// a pergunta vem de fora. Nulo so num universo sem mundo gerado nenhum na regiao (ou com os seis destruidos).
	/// </summary>
	public static CasaDeBills? Casa(ulong seedDoUniverso, Func<PlanetaNoEspaco, bool>? destruido = null)
	{
		foreach (CasaDeBills c in Casas(seedDoUniverso))
			if (destruido == null || !destruido(c.Corpo)) return c;
		return null;
	}

	/// <summary>
	/// AS COORDENADAS GALACTICAS, do jeito que a carta estelar ja escreve um endereco: o sistema pela celula
	/// (`SistemaSolar.NomeDaEstrela` = "Sistema Sx:Sy") e a orbita pelo numero que a tela do sistema mostra.
	/// </summary>
	public static string Coordenadas(CasaDeBills c) => $"Sistema {c.Sx}:{c.Sy}, órbita #{c.K}";

	/// <summary>
	/// Onde os dois ficam PARADOS na casa, em tiles a partir do ponto de pouso do planeta (todo mundo pousa no mesmo
	/// tile, no centro do mapa): alguns passos pro norte, pra ninguem cair em cima deles.
	/// </summary>
	public const float TilesDoPouso = 6;

	/// <summary>Nenhum dos dois fica a menos que isto (em tiles) do tile de pouso, por mais apertado que o terreno seja.</summary>
	public const float FolgaDoPousoTiles = 3;

	/// <summary>
	/// Ate onde (em tiles, pra cada lado do ponto pedido) se procura chao firme pros dois ficarem LADO A LADO. Alem
	/// disso vale o primeiro ponto livre que houver -- mundo todo de agua tambem tem de ter deus.
	/// </summary>
	public const int BuscaDoPostoTiles = 24;

	/// <summary>
	/// A CLAREIRA: em volta dos dois postos nao pode haver nada que BARRE nesta janela de tiles -- tantas colunas pra
	/// cada lado, e tantas fileiras pra BAIXO (o sul da tela). E a medida da copa de uma arvore grande, que sobe uns
	/// seis tiles acima do tronco e sobra quase tres pra cada lado: parado atras dela, o deus some da tela.
	/// </summary>
	public const int ClareiraColunas = 3, ClareiraFileiras = 6;

	// =====================================================================
	// 6. O MENU DA TECLA E -- os tipos e os verbos (o servidor valida tudo)
	// =====================================================================

	/// <summary>
	/// Os "tipos" com que o corpo parado aparece pra tecla E -- as chaves do `Tech.Interacoes.De`. O servidor aponta
	/// o tipo certo pra cada jogador: quem esta no meio de um treino ve a saida, e nao o convite.
	/// </summary>
	public const string TipoWhis = "Divino_Whis", TipoBills = "Divino_Bills",
						TipoEmTreino = "Divino_Em_Treino", TipoEmDesafio = "Divino_Em_Desafio";

	/// <summary>Os verbos do menu (no maximo 24 letras: o canal de verbs devolve vazio acima disso).</summary>
	public const string VerboTreinarComWhis = "whis_treinar", VerboTreinarComBills = "bills_treinar",
						VerboDesafiar = "bills_desafiar", VerboParar = "divino_parar";

	public static bool EhVerbo(string cmd) =>
		cmd is VerboTreinarComWhis or VerboTreinarComBills or VerboDesafiar or VerboParar;

	// =====================================================================
	// 7. O TREINO E O DESAFIO
	// =====================================================================

	/// <summary>
	/// "caso o jogador ja tenha a skill do ultra instinto liberada, lutar com o whis acelera a maestria dela" (e o
	/// Bills, a do Poder da Destruicao). QUANTO: a maestria REAL da disciplina certa sobe este tanto mais rapido
	/// enquanto durar o treino -- pelas mesmas regras de sempre (so lutando, e so com a disciplina em uso). O
	/// numero e do dono; tres foi escolhido pra rimar com o triplo do poder.
	/// </summary>
	public const double AceleracaoDaMaestria = 3;

	/// <summary>DEFEITO INJETADO (bancada): treinar com eles deixa de acelerar a maestria. Lido por `GameServer.AceleracaoDoTreinoDivino`.</summary>
	public static bool TreinoNaoAceleraDeTeste;

	/// <summary>O treino acaba sozinho depois disto (em segundos), pra a vez passar a quem espera.</summary>
	public const double SegundosDeTreino = 5 * 60;

	/// <summary>Quem treina e se afasta mais que isto (em tiles) do lugar do mestre larga o treino.</summary>
	public const float TilesDoTreino = 40;

	/// <summary>
	/// O FOLEGO DE QUEM SAI DE UMA LUTA DA CASA, em segundos: nesse tempo ninguem o atinge. O mestre para na hora em
	/// que a luta acaba, mas o que ele JA TINHA LANCADO continua no ar -- medido na `--diagbills` (2026-10-11): a
	/// aula acabou com o aluno no chao, ele foi posto de pe, e a rajada que vinha atras o nocauteou de novo, ja
	/// fora do treino (e ai era derrota de verdade). E a carencia de quem acabou de se levantar
	/// (`CombatState.Carencia`), que cai sozinha se ele golpear.
	/// </summary>
	public const double SegundosDeFolegoAoFim = 2.5;

	/// <summary>DEFEITO INJETADO (bancada): quem sai de uma luta da casa nao ganha folego nenhum. Lido pela `GameServer.EncerrarASessao`.</summary>
	public static bool FimSemFolegoDeTeste;

	/// <summary>
	/// Depois de perder um desafio pelo titulo, o mesmo personagem so desafia de novo passados estes dias in-game.
	/// (O duelo entre JOGADORES fecha o trono inteiro por dois dias reais; contra o Bills isso deixaria um unico
	/// teimoso trancar o titulo pra todo mundo -- aqui a espera e de quem perdeu, e so dele.)
	/// </summary>
	public const int DiasEntreDesafiosDoMesmo = 1;

	/// <summary>
	/// DEFEITO INJETADO (bancada): no treino a precisao do Instinto do Whis volta a CAIR com o uso, como a de
	/// qualquer jogador. Lido pelo pino do `GameServer.BillsTreino.cs`. Falso em jogo, sempre.
	/// </summary>
	public static bool InstintoQueCansaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o Whis ENTRA NA FORMA do Instinto ao treinar (cabelo prateado, aura) -- o efeito
	/// visual que o dono disse que um Anjo nao tem. Lido pelo `GameServer.VestirADisciplina`. Falso em jogo, sempre.
	/// </summary>
	public static bool InstintoComFormaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): cair num treino volta a ser DERROTA (Zenkai de quem apanhou, luto de quem viu).
	/// Lido pelo `GameServer.TreinoDivinoEntre`, que o `AoPerderALuta` consulta. Falso em jogo, sempre.
	/// </summary>
	public static bool TreinoEhDerrotaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): o servidor aceita os pedidos do menu sem conferir lugar, distancia nem estado. Falso em jogo, sempre.</summary>
	public static bool MenuSemValidacaoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada): a casa do Bills vai no pacote ANTES de ele a revelar. Falso em jogo, sempre.</summary>
	public static bool CasaVazaDeTeste;

	// =====================================================================
	// 8. AS FALAS
	// =====================================================================
	// Escritas pra este jogo, no tom dos dois: o Bills e preguicoso, guloso, de pavio curto, orgulhoso e justo do
	// jeito dele; o Whis e educado, calmo, ironico -- e tambem guloso. NENHUMA delas diz numero de poder.

	/// <summary>O mundo inteiro ouve, no dia em que Boo cai (linha de sistema, sem autor).</summary>
	public const string AvisoDoDespertar =
		"Muito longe daqui, alguma coisa que dormia há décadas abriu um olho. O poder que derrubou Majin Boo foi "
		+ "sentido longe demais.";

	/// <summary>O mundo inteiro ouve, no dia em que ele pousa.</summary>
	public const string AvisoDaChegada =
		"Uma presença que não é deste mundo pousou na Terra. BILLS, o Deus da Destruição, veio ver de perto quem "
		+ "derrubou Majin Boo -- e trouxe o Whis com ele.";

	/// <summary>Quem esta por perto ouve os dois, quando pousam.</summary>
	public static readonly Fala[] FalasDaChegada =
	[
		new(QuemFala.Bills, "Então foi neste planetinha que derrubaram o chiclete cor-de-rosa. Hm. Me tiraram de um cochilo de décadas por isto."),
		new(QuemFala.Whis, "Bom dia a todos. Não reparem no humor: o senhor Bills acorda assim desde que o universo é universo."),
		new(QuemFala.Bills, "Quem quiser medir forças comigo, a fila é aqui. Vou usar só uma lasca do meu poder -- não quero rachar o planeta antes de provar a comida."),
		new(QuemFala.Bills, "Enquanto ninguém se decide, eu cochilo. Whis, só me acorde se for comida. Ou se alguém me bater."),
	];

	/// <summary>Uma delas, quando o primeiro golpe o acorda.</summary>
	public static readonly string[] FalasDeAcordar =
	[
		"...Quem foi o infeliz que me acordou?",
		"Hm? Ah. Um voluntário. Tente durar mais que o meu bocejo.",
		"Eu estava sonhando com um banquete. Você vai pagar por cada prato.",
	];

	/// <summary>Uma delas, quando ele derruba alguem na Terra.</summary>
	public static readonly string[] FalasDeDerrubar =
	[
		"Próximo.",
		"Era só isso? Eu nem acordei direito.",
		"Levante quando puder. Ou não levante -- pra mim dá na mesma.",
	];

	/// <summary>O Whis, de vez em quando, depois de o Bills derrubar alguem.</summary>
	public const string WhisTranquiliza =
		"Não se preocupem: ele só derruba. Destruir daria trabalho, e trabalho é a única coisa que ele evita mais do que salada.";

	/// <summary>Quando nao sobra ninguem de pe e ele volta a dormir.</summary>
	public const string FalaDeVoltarACochilar = "Acabou? Então boa noite.";

	/// <summary>Depois de apagar alguem com o Hakai na Terra (uma das duas, sorteada).</summary>
	public static readonly string[] FalasDeApagar =
	[
		"Eu avisei que acordo de mau humor.",
		"Menos um barulho. Onde eu estava? Ah, sim: dormindo.",
	];

	/// <summary>
	/// O QUE O WHIS COMENTA ENQUANTO O BILLS LUTA (dono: "fica vendo de longe e comentando sobre comidas"). Um por
	/// vez, na ordem da <see cref="SacolaDePalpites"/>. Nenhum fala de poder nem da luta: so de comida.
	/// </summary>
	public static readonly string[] PalpitesDeComida =
	[
		"Ohoho... não demore, senhor Bills: o lámen esfria, e macarrão frio é um pecado sem perdão.",
		"Pudim. É nisso que eu estou pensando agora. Pudim com bastante calda.",
		"Se terminar logo, ainda pegamos o sushi fresco. Peixe não espera por ninguém.",
		"Lutar de estômago vazio é falta de educação. Depois disso, um bom churrasco.",
		"Morango com creme... ou uma torta inteira? Decisões difíceis, essas.",
		"Não se apresse por minha causa. Estou só contando quantos pratos de curry cabem numa tarde.",
		"Dizem que existe uma pizza com queijo que estica. Que conceito fascinante.",
		"Um chá quentinho e uns biscoitos cairiam muito bem assistindo a isso.",
		"Cuidado com a poeira, senhor Bills: poeira estraga o tempero de qualquer coisa.",
		"Quando isso acabar, eu quero um parfait. Daqueles altos, com fruta até a borda.",
		"Sorvete... já provaram sorvete? Gelado e doce ao mesmo tempo. Uma invenção notável.",
		"Hum, takoyaki. Bolinhos quentes, recheados... só de lembrar já vale a espera.",
	];

	/// <summary>A DERROTA NA TERRA: e aqui que ele diz as coordenadas (o dono: "ele fala as coordenadas galaticas no momento").</summary>
	public static readonly Fala[] FalasDaDerrota =
	[
		new(QuemFala.Bills, "...Hunf. Me derrubaram. A mim."),
		new(QuemFala.Bills, "Não se animem. Isso foi uma lasca do meu poder, e eu ainda estava com sono."),
		new(QuemFala.Bills, "Mas eu sou justo: quem me põe no chão merece treino de verdade, e este planeta não tem mais nada pra ensinar a vocês."),
		new(QuemFala.Bills, "Apareçam na minha casa. Coordenadas galácticas: {coordenadas}. Anotem, que eu não repito."),
		new(QuemFala.Whis, "Ele repete, sim. {coordenadas} -- nas cartas de vocês o mundo aparece como {mundo}. E levem um lanche. Pra mim, de preferência."),
		new(QuemFala.Bills, "A gravidade de lá esmaga quem for fraco. Se não aguentarem ficar de pé, nem batam na porta."),
		new(QuemFala.Bills, "Whis, vamos embora. E embrulhe aquela coisa frita que eu comi ontem."),
	];

	/// <summary>O mundo inteiro ouve, quando os dois vao embora da Terra. `{nome}` = quem o derrubou.</summary>
	public const string AvisoDaDerrota =
		"BILLS FOI DERRUBADO NA TERRA por {nome}! O Deus da Destruição abriu a própria casa a quem quiser treinar: "
		+ "{coordenadas} ({mundo}). A carta estelar de todos passou a marcar o lugar.";

	/// <summary>A visita que acaba SEM luta (o titulo dele foi tomado enquanto ele estava na Terra): quem fala e o Whis, se estiver la.</summary>
	public const string WhisEncerraAVisita =
		"O senhor Bills não carrega mais o título, então a visita acaba aqui. Eu continuo recebendo quem quiser treinar: {coordenadas}.";

	public const string AvisoDaVisitaEncerrada =
		"A visita de Bills à Terra terminou. A casa dele fica em {coordenadas} ({mundo}), e a carta estelar de todos passou a marcar o lugar.";

	/// <summary>Uma vez por personagem, no primeiro login depois de a casa ser revelada: e como quem nao estava la fica sabendo.</summary>
	public const string NoticiaDaCasa =
		"Corre a notícia: Bills foi derrubado na Terra e recebe na casa dele quem quiser treinar -- {coordenadas} "
		+ "({mundo}), marcado em roxo na carta estelar. Dizem que a gravidade de lá esmaga os fracos.";

	/// <summary>O mundo ouve quando a casa muda de lugar (o planeta em que eles moravam foi destruido).</summary>
	public const string AvisoDaMudanca =
		"Bills se mudou: a casa dele agora fica em {coordenadas} ({mundo}). A carta estelar de todos passou a marcar o lugar novo.";

	/// <summary>
	/// O RUMO, pra quem chega no mundo em que eles estao (uma vez por chegada): o mapa da Terra tem 500 tiles de
	/// lado e o da casa pode ter mil, e "eles estao aqui" sem dizer onde mandaria procurar no escuro. `{rumo}` e a
	/// frase de rumo de todo aviso do jogo ("a uns 14 passos a nordeste de voce").
	/// </summary>
	public const string RumoNaTerra = "Bills está {rumo}. Dizem que ele cochila, e que só acorda apanhando.";

	/// <summary>Em casa: os dois, ou so o que existir (o cargo do outro tem dono).</summary>
	public const string RumoDosDois = "Bills e Whis estão {rumo}.", RumoDoBills = "Bills está {rumo}.", RumoDoWhis = "Whis está {rumo}.";

	// ---- em casa, parados ----

	/// <summary>O que o Whis diz quando alguem chega perto dos dois (de vez em quando).</summary>
	public static readonly string[] WhisRecebe =
	[
		"Ah, visitas. Trouxeram alguma coisa pra comer?",
		"Falem baixo: o senhor Bills dorme leve quando o assunto é barulho, e pesado quando o assunto é trabalho.",
		"Se vieram treinar, é comigo ou com ele. Comigo dói menos. Um pouco.",
	];

	/// <summary>O Bills, parado, e um EMOTE (o cliente escreve o nome dele na frente).</summary>
	public const string BillsDorme = "ronca baixinho, de barriga pra cima.";

	/// <summary>De quanto em quanto tempo (segundos) cada um deles fala sozinho quando ha visita por perto.</summary>
	public const double SegundosEntreRecepcoes = 75;

	/// <summary>A que distancia (em tiles) uma visita conta como "perto" pra recepcao.</summary>
	public const float TilesDaRecepcao = 8;

	// ---- o treino com o Whis ----

	public const string WhisComecaOTreino =
		"Muito bem. Eu acompanho o seu ritmo -- alguns passos à frente, como convém a um professor. Comece quando quiser.";

	/// <summary>No lugar da de cima, pra quem ja tem o Ultra Instinto: e o aviso (sem numero) de que o treino o apura.</summary>
	public const string WhisComecaComInstinto =
		"O seu corpo já aprendeu a se mexer sozinho. A aula de hoje é ensinar a cabeça a não atrapalhar -- lute com o instinto ligado e ele cresce mais depressa aqui.";

	/// <summary>Uma delas quando ele desvia de um golpe (de vez em quando).</summary>
	public static readonly string[] WhisDesvia =
	[
		"Você pensa antes de bater. É por isso que não acerta.",
		"Bonito golpe. Pena que eu já não estava aí.",
		"De novo. Com menos ombro e mais vontade.",
	];

	public const string WhisDerrubouOAluno = "Por hoje chega. Você durou mais que a sobremesa de ontem -- e isso é um elogio.";
	public const string WhisFoiDerrubado = "Oh. Fazia eras que ninguém me tirava do lugar. Meus parabéns -- de verdade.";
	public const string WhisAcabouOTempo = "Acabou o tempo da aula. Volte quando as pernas pararem de tremer.";
	public const string WhisAlunoParou = "Como quiser. Descansar também é treino -- o senhor Bills que o diga.";
	public const string WhisAlunoFugiu = "Fugir também é uma técnica. Não das melhores.";
	public const string WhisIntrometido = "Plateia demais. Aula é de um aluno só -- combinem a vez entre vocês.";
	public const string WhisOcupado = "Um de cada vez, por favor. Estou no meio de uma aula.";

	// ---- o treino com o Bills ----

	public const string BillsComecaOTreino = "Treinar? Tá. Mas rápido, que a soneca não se tira sozinha. E não me faça suar.";

	/// <summary>Pra quem ja carrega o Poder da Destruicao (o Ultra Ego): o aviso de que o treino o apura.</summary>
	public const string BillsComecaComDestruicao =
		"Hm. Você carrega a Destruição. Então apanhe direito: aqui, com a aura ligada, ela cresce mais depressa.";

	/// <summary>Uma delas durante o treino (de vez em quando).</summary>
	public static readonly string[] BillsProvoca =
	[
		"Bata como quem quer destruir alguma coisa, não como quem pede desculpa.",
		"Isso foi um soco ou você tropeçou?",
		"Mais forte. Eu quase senti.",
	];

	public const string BillsDerrubouOAluno = "Chega. Você ainda apanha feio... mas apanha melhor do que quando chegou.";
	public const string BillsFoiDerrubadoNoTreino = "...Nada mal. Não conte pra ninguém.";
	public const string BillsAcabouOTempo = "Cansei. Não de você -- de ficar de pé.";
	public const string BillsAlunoParou = "Já vai? Ótimo. Eu ia sugerir a mesma coisa.";
	public const string BillsAlunoFugiu = "Correu. Sábio, até.";
	public const string BillsIntrometido = "Dois contra um é falta de educação. Sumam, os dois.";
	public const string BillsOcupado = "Estou ocupado. Entre na fila -- ou vá incomodar o Whis.";

	// ---- o desafio pelo titulo ----

	public static readonly Fala[] FalasDoDesafio =
	[
		new(QuemFala.Bills, "Você quer o MEU título."),
		new(QuemFala.Whis, "Vou afastar a louça, senhor Bills."),
		new(QuemFala.Bills, "Então nada de lasca desta vez. Tudo. E com raiva."),
	];

	public const string AvisoDoDesafio =
		"DUELO PELO TÍTULO DE GOD OF DESTRUCTION: Bills (Deus) VS {nome} (desafiante)! Um contra um; nocaute decide.";

	public const string BillsVenceuODesafio = "Volte quando doer menos.";
	public const string BillsAcabouOPrazo = "O tempo acabou e eu continuo de pé. O título fica onde está.";
	public const string BillsDesafianteSumiu = "Sumiu. Nem pra perder direito.";
	public const string BillsApagouODesafiante = "Quis o meu título e apostou a própria existência. Perdeu os dois.";
	public const string BillsDesafioAnulado = "Trapaça no meu duelo? Fora daqui, os dois.";
	public const string AvisoDoDesafioAnulado = "O duelo de {nome} contra Bills foi ANULADO: alguém se meteu no meio.";
	public const string AvisoDoTituloDefendido = "O GOD OF DESTRUCTION defendeu seu título contra {nome}.";
	public const string BillsSemKiDivino = "Um mortal sem ki divino querendo o MEU título? Volte quando o seu poder tiver gosto de deus.";
	public const string BillsDesafioCedo = "Você já tentou. Deixe o dia virar -- e o orgulho sarar.";
	public const string BillsFundido = "Um de cada vez, e inteiro. Desfaça essa fusão e volte.";

	/// <summary>A despedida: o titulo ja mudou de mao quando eles falam.</summary>
	public static readonly Fala[] FalasDaCoroacao =
	[
		new(QuemFala.Bills, "...Então é isso. Um mortal."),
		new(QuemFala.Bills, "O título é seu. As obrigações também -- são muitas, e são todas chatas."),
		new(QuemFala.Whis, "Meus parabéns, novo senhor da Destruição. O traje já está na sua mochila; fica melhor em você do que eu esperava."),
		new(QuemFala.Bills, "Vou tirar umas férias. Longas. Não me procurem."),
	];

	/// <summary>O que o mundo ouve ANTES da coroacao (o `via` do `Entronizar`).</summary>
	public const string ViaDoTitulo = "{nome} DERROTOU Bills, o Deus da Destruição, em duelo formal!!";

	/// <summary>Troca os marcadores de uma fala. Ver <see cref="Fala"/>.</summary>
	public static string Preencher(string texto, string coordenadas = "", string mundo = "", string nome = "") =>
		texto.Replace("{coordenadas}", coordenadas).Replace("{mundo}", mundo).Replace("{nome}", nome);
}
