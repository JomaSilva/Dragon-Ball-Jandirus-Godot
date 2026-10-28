using Jandirus.Core.Items;
using Jandirus.Core.Tech;

namespace Jandirus.Core.Npc;

/// <summary>
/// UM LACO DE AMIZADE: o que UM personagem ja fez por UM habitante. Mora na memoria do servidor, pelo id do corpo do habitante
/// e pela assinatura do personagem (`GameServer.Amizade.cs`) -- e e por isso que ele so dura enquanto o habitante vive: NPC nao tem
/// identidade entre sessoes (o id e `_nextId++`, a semente e do lugar), entao o habitante que morre, e reciclado pelo povoamento ou
/// renasce num reinicio do servidor e OUTRA pessoa, e o progresso com ele se perde.
/// </summary>
public sealed class LacoDeAmizade
{
	/// <summary>O medidor, de zero a <see cref="Amizade.Cheia"/>. Cheio, o menu E dele ganha o "Recrutar".</summary>
	public double Pontos;

	/// <summary>O dia do mundo (`DiaDoMundo`) da ultima contagem: e nele que o retorno decrescente zera. -1 = nunca contou.</summary>
	public long Dia = -1;

	/// <summary>Quantos presentes e quantos treinos ja renderam neste dia -- cada um rende a metade do anterior.</summary>
	public int PresentesHoje, TreinosHoje;
}

/// <summary>
/// ============================ A AMIZADE COM OS HABITANTES (2026-10-21) ============================
/// Pedido dos jogadores, repassado pelo dono: *"Poder fazer amizade e recrutar npcs dando presentes e sparring, eles iriam pra uma
/// aba nova chamada allies pra n confundir com people."* O DM nao tem nada parecido (la o habitante nao lembra de ninguem, e o
/// companheiro so vem da capsula): a regra e do dono e do orquestrador desta leva, e o que nenhum dos dois disse esta marcado
/// como decisao no relatorio.
///
///   * O MEDIDOR vai de 0 a 100, por personagem e por habitante (<see cref="LacoDeAmizade"/>).
///   * O PRESENTE (tecla E: "Dar presente"): um item da mochila vale pontos pelo VALOR -- a comida pela nutricao, a Senzu de
///     proposito acima dela, e o resto pelo preco de fabrica (escala de dez em dez, ver <see cref="ValorDoPresente"/>). O item sai
///     da mochila. O mesmo habitante no mesmo dia rende cada vez MENOS (a metade do anterior, <see cref="RetornoNoMesmoDia"/>).
///   * O TREINO (tecla E: "Treinar"): um contra um, nao letal, no molde do treino com o Bills e o Whis -- sem Zenkai, karma ou luto
///     (`AoPerderALuta`), ate um dos dois chegar a metade da vida ou um minuto passar. O habitante RECUSA o poder desigual demais, a
///     mesma "inteligencia minima" do spar entre habitantes (`RotinaConfig.RazaoDePoderQueAssusta`). Rende amizade (com o mesmo
///     retorno decrescente) e o ganho de BP de sempre por golpe em NPC (1x).
///   * BATER DE VERDADE (ou matar) ZERA o laco, e o rancor e o de sempre (`MarcarAgressao`).
///   * COM 100, "Recrutar": o habitante VIVO sai do povoamento (a manutencao repoe outro) e vira um ALIADO (<see cref="Aliado"/>).
///
/// SO O HABITANTE COMUM entra (<see cref="MoldesRecrutaveis"/>): o Rei e o Principe de Vegeta, o Guru, os Supremos Kaiohs e as
/// almas do Outro Mundo ficam de fora -- os quatro primeiros por serem unicos, e as almas por estarem mortas (recrutar uma seria
/// ressuscita-la, e isso e outro sistema). Chefe, lutador, bicho e divino nem sao habitantes.
/// ==================================================================================================
/// </summary>
public static class Amizade
{
	/// <summary>O medidor cheio: com ele, o "Recrutar" aparece.</summary>
	public const double Cheia = 100;

	// ---------------------------------------------------------------- o presente
	/// <summary>Pontos por ponto de nutricao da comida (a maca, 6, da 3; o salmao, 20, da 10). Decisao minha: o dono disse "pelo valor".</summary>
	public const double PontosPorNutricao = 0.5;

	/// <summary>O teto de uma comida comum (a carne de caca, 30 de nutricao, para aqui).</summary>
	public const double TetoDaComida = 15;

	/// <summary>A SEMENTE SENZU vale de proposito mais que a comida: e rara (so o Assistente do Guardiao cultiva) e cura o corpo inteiro.</summary>
	public const double PontosDaSenzu = 20;

	/// <summary>
	/// O ITEM COM PRECO DE FABRICA vale <see cref="PontosPorDecadaDeZeni"/> por "zero a mais" acima de <see cref="ZeniDoPisoDoPresente"/>:
	/// 1.000 zeni dao 5, o Scouter (8.000) da 9,5, 100 mil dao 15, um milhao da 20 -- e nada passa de <see cref="TetoDoPresente"/>.
	/// </summary>
	public const double PontosPorDecadaDeZeni = 5;
	public const double ZeniDoPisoDoPresente = 100;

	/// <summary>O menos e o mais que um presente vale (antes do retorno decrescente).</summary>
	public const double PisoDoPresente = 1, TetoDoPresente = 25;

	/// <summary>
	/// O RETORNO DECRESCENTE: cada presente (e cada treino) ao mesmo habitante no mesmo dia do jogo rende esta fracao do anterior. Sem
	/// isto, uma pilha de macas compraria qualquer amizade numa tarde. O "dia" e o do mundo (24 min de relogio), e nao o do calendario.
	/// </summary>
	public const double RetornoNoMesmoDia = 0.5;

	// ---------------------------------------------------------------- o treino
	/// <summary>O que um treino que chegou ao fim (a metade da vida de um dos dois, ou o minuto) rende, antes do retorno decrescente.</summary>
	public const double PontosDoTreino = 15;

	/// <summary>O treino acaba quando um dos dois chega a esta fracao da vida (decisao do orquestrador: metade).</summary>
	public const double VidaQueEncerraOTreino = 0.5;

	/// <summary>...ou quando passa este tempo.</summary>
	public const double SegundosDoTreino = 60;

	/// <summary>
	/// Quem fica mais longe que isto do parceiro, por <see cref="SegundosLongeQueEncerram"/> seguidos, larga o treino (sem pontos).
	/// (2026-10-27, o pedido 124b) Eram 15, e na hora: um soco com recuo jogava o parceiro pra fora e encerrava o treino.
	/// </summary>
	public const int TilesDoTreino = 30;

	/// <summary>(2026-10-27, o pedido 124b) ...o tempo SEGUIDO alem de <see cref="TilesDoTreino"/> que encerra; voltar perto zera a conta.</summary>
	public const double SegundosLongeQueEncerram = 15;

	/// <summary>
	/// (2026-10-27, o pedido 124b) O QUE O TREINO QUE CHEGA AO FIM PAGA ALEM DA AMIZADE -- a escolha do dono, "BP + Inspirado": o BP de
	/// tantos segundos treinando ali (a tecla T: os mesmos passes do `Treinar`), e o estado INSPIRADO, em que o treino rende
	/// <see cref="MultiplicadorDoInspirado"/> vezes por <see cref="MinutosDoInspirado"/> minutos.
	/// </summary>
	public const double SegundosDeTreinoDaRecompensa = 180;
	public const double MultiplicadorDoInspirado = 1.5;
	public const double MinutosDoInspirado = 10;

	/// <summary>
	/// FOLGA DO MENU JA ABERTO, em pixels (dois tiles). A tecla E aponta quem esta a dois tiles, mas o clique chega
	/// um instante depois e o habitante da um passo. Sem isto, "Dar presente" e "Recrutar" recusam com o menu ainda
	/// na tela. So vale pra o corpo que o apontador mostrou, e nao pra outro que passou perto. Decisao minha, 2026-10-21.
	/// </summary>
	public const float FolgaDoMenuAberto = 64;

	/// <summary>
	/// O FOLEGO AO FIM: os dois ficam estes segundos sem poder ser atingidos -- o que um deles ja tinha lancado ainda esta no ar, e o
	/// tiro que chegasse depois do fim seria um golpe de verdade (o rancor de 90 s e o laco zerado). O mesmo argumento do treino com o
	/// Bills (`BillsEWhis.SegundosDeFolegoAoFim`).
	/// </summary>
	public const double SegundosDeFolegoAoFim = 3;

	// ---------------------------------------------------------------- quem entra
	/// <summary>Os moldes do `npcs.json` com quem se faz amizade: so o habitante comum. Ver o cabecalho.</summary>
	public static readonly string[] MoldesRecrutaveis = ["cidadao"];

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): qualquer molde de habitante entra -- o Rei de Vegeta ganharia o menu e o recrutamento.</summary>
	public static bool QualquerHabitanteDeTeste;

	/// <summary>Este molde entra na amizade?</summary>
	public static bool MoldeRecrutavel(string moldeId) =>
		QualquerHabitanteDeTeste || MoldesRecrutaveis.Any(m => string.Equals(m, moldeId, StringComparison.OrdinalIgnoreCase));

	// ---------------------------------------------------------------- as contas
	/// <summary>
	/// QUANTO ESTE ITEM VALE COMO PRESENTE (antes do retorno decrescente) -- zero = nao se da de presente. `custoDaObra` e o preco de
	/// fabrica do item no catalogo de construcoes (`Construcao.Custo`; negativo ou zero = nao se fabrica).
	/// </summary>
	public static double ValorDoPresente(string id, ItemDef def, double custoDaObra)
	{
		if (string.Equals(id, CatalogoDeItens.Senzu, StringComparison.OrdinalIgnoreCase)) return PontosDaSenzu;
		if (def.Nutricao > 0) return Math.Clamp(def.Nutricao * PontosPorNutricao, PisoDoPresente, TetoDaComida);
		if (custoDaObra <= 0) return 0;
		return Math.Clamp(PontosPorDecadaDeZeni * Math.Log10(custoDaObra / ZeniDoPisoDoPresente), PisoDoPresente, TetoDoPresente);
	}

	/// <summary>
	/// ESTE ITEM DA MOCHILA PODE SER DADO? So o que tem valor (<see cref="ValorDoPresente"/>) e NAO tem estado no id: a capsula com a
	/// gaveta da criatura, a maquina recolhida com as melhorias, o peso com o maximo comprado, a roupa tingida e o livro de ensinamentos
	/// carregam um pedaco do personagem no `|`, e a Esfera do Dragao e os brincos Potara tem dono e regra proprios.
	/// </summary>
	public static bool DaPraDar(string id, ItemDef? def, double custoDaObra) =>
		def != null && !id.Contains('|')
		&& !string.Equals(id, CatalogoDeItens.BrincosPotara, StringComparison.OrdinalIgnoreCase)
		&& Magic.EsferaNaMochila.Ler(id) == null
		&& ValorDoPresente(id, def, custoDaObra) > 0;

	/// <summary>Virou o dia do mundo desde a ultima contagem: o retorno decrescente recomeca.</summary>
	public static void VirarODia(LacoDeAmizade l, long dia)
	{
		if (l.Dia == dia) return;
		l.Dia = dia;
		l.PresentesHoje = 0;
		l.TreinosHoje = 0;
	}

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): sem o retorno decrescente -- o decimo presente do dia vale o primeiro.</summary>
	public static bool SemRetornoDecrescenteDeTeste;

	/// <summary>O que um gesto de valor `valor` rende sendo o `quantosHoje`+1-esimo do dia (0 = o primeiro, que rende inteiro).</summary>
	public static double Ganho(double valor, int quantosHoje) =>
		SemRetornoDecrescenteDeTeste ? valor : valor * Math.Pow(RetornoNoMesmoDia, Math.Max(0, quantosHoje));

	/// <summary>Soma ao laco, sem passar do cheio; devolve o que entrou de verdade.</summary>
	public static double Somar(LacoDeAmizade l, double pontos)
	{
		double antes = l.Pontos;
		l.Pontos = Math.Clamp(l.Pontos + pontos, 0, Cheia);
		return l.Pontos - antes;
	}

	/// <summary>O laco esta cheio: da pra recrutar.</summary>
	public static bool Cheio(LacoDeAmizade? l) => l != null && l.Pontos >= Cheia - 1e-9;

	/// <summary>
	/// O PODER E DESIGUAL DEMAIS PRA UM TREINO? `razao` e o poder expresso de quem pede sobre o do habitante -- a mesma conta e o mesmo
	/// limiar da recusa do spar entre habitantes (`Rotina.Convite`, `RotinaConfig.RazaoDePoderQueAssusta`).
	/// </summary>
	public static bool PoderDesigual(double razao, double queAssusta) => razao > queAssusta || razao < 1 / queAssusta;

	// ---------------------------------------------------------------- o menu da tecla E
	/// <summary>As chaves do menu E do habitante (`Interacoes.De`): o comum, o que ja confia (com o "Recrutar") e o do treino em curso.</summary>
	public const string TipoDoHabitante = "Habitante_Amizade", TipoDoAmigo = "Habitante_Amigo", TipoEmTreino = "Habitante_EmTreino";

	public const string VerboPresente = "amizade_presente", VerboTreinar = "amizade_treinar", VerboParar = "amizade_parar",
						VerboRecrutar = "amizade_recrutar";

	/// <summary>Os rotulos dos botoes -- os robos os acham por aqui.</summary>
	public const string RotuloPresente = "Dar presente", RotuloTreinar = "Treinar", RotuloRecrutar = "Recrutar", RotuloParar = "Parar o treino";

	private static readonly Interacoes.Acao Presente = new(RotuloPresente, VerboPresente, "",
		"escolha um item da mochila: comida, Senzu e o que tem preço valem amizade pelo valor -- o mesmo dia rende cada vez menos",
		Interacoes.Forma.Lista);

	private static readonly Interacoes.Acao Treino = new(RotuloTreinar, VerboTreinar, "",
		"um treino de um contra um: ninguém se machuca de verdade, e acaba na metade da vida de um dos dois ou em um minuto");

	/// <summary>O menu do habitante comum: o presente e o treino.</summary>
	public static readonly Interacoes.Acao[] AcoesDoHabitante = [Presente, Treino];

	/// <summary>O menu de quem ja confia em voce (o laco cheio): o mesmo, e o "Recrutar".</summary>
	public static readonly Interacoes.Acao[] AcoesDoAmigo =
	[
		Presente, Treino,
		new(RotuloRecrutar, VerboRecrutar, "", "ele deixa a vida dele e passa a seguir você -- as ordens ficam na aba Allies (tecla P)"),
	];

	/// <summary>No meio do treino o menu e a saida, a qualquer distancia (o molde do treino com o Bills).</summary>
	public static readonly Interacoes.Acao[] AcoesNoTreino =
	[
		new(RotuloParar, VerboParar, "", "encerra o treino -- sem amizade pelo que ficou pela metade"),
	];

	// ---------------------------------------------------------------- o que o habitante diz
	public static readonly string[] FalasDoPresente = ["Pra mim? Obrigado!", "Que gentileza...", "Valeu, de verdade.", "Olha só... obrigado."];
	public static readonly string[] FalasDoPresenteGrande = ["Isso é demais! Nem sei o que dizer.", "Uau... vou guardar com carinho.", "Você é generoso demais!"];
	public static readonly string[] FalasDoTreinoAceito = ["Vamos lá! Sem pegar pesado.", "Um treino? Pode vir!", "Certo, mostre o que sabe."];
	public static readonly string[] FalasDeForteDemais = ["Você me esmagaria! Treine com alguém do seu tamanho.", "Nem pensar, eu não duraria um segundo."];
	public static readonly string[] FalasDeFracoDemais = ["Hum... volte quando estiver mais forte.", "Eu acabaria te machucando. Melhor não."];
	public static readonly string[] FalasDoFimDoTreino = ["Bom treino!", "Ufa... foi divertido.", "Você luta bem. De novo outro dia?"];
	public static readonly string[] FalasDoRecrutamento = ["Conte comigo. Aonde você for, eu vou.", "Tá decidido: eu vou com você!", "Pode contar comigo, amigo."];

	/// <summary>O presente "grande" (a fala mais calorosa) -- a partir deste valor.</summary>
	public const double PresenteGrande = 10;
}
