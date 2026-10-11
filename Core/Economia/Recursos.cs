using Jandirus.Core.Items;
using Jandirus.Core.World;

namespace Jandirus.Core.Economia;

/// <summary>
/// COM O QUE SE PAGA uma tecnologia ou uma peca de base: o dinheiro de sempre, ou a materia-prima que se tira
/// do chao dos planetas. Pedido do dono de 2026-10-10: *"a capacidade de construir tecnologias sem precisar de
/// zenni com um material chamado resource"*.
/// </summary>
public enum Moeda : byte
{
	Zeni = 0,
	Recursos = 1,
}

/// <summary>
/// COM O QUE SE MINERA. A ordem do enum E a ordem do pedido: *"shovel sendo mais rapido q sem nada, mas mais
/// lento q hand drill"*. Ver <see cref="Recurso.PorSegundo"/>.
/// </summary>
public enum Ferramenta : byte
{
	Mao = 0,
	Pa = 1,
	Furadeira = 2,
}

/// <summary>
/// O RECURSO -- a materia-prima dos planetas, e as contas dela.
///
/// NAO EXISTE NO DM como coisa separada: la o que a pa, a furadeira e o `Power_Drill` rendiam era dinheiro (os
/// "resources" do texto de `Tier 1.dm:256` caem no bolso como zeni). Aqui e um SEGUNDO saldo, que so compra obra
/// -- e por isso vale metade: tudo o que custa N zeni pode ser feito com 2N recursos.
///
/// ESTE ARQUIVO E O CONTRATO entre os sistemas que mexem com recurso (mineracao, Drill, rochas de minerio,
/// arvore derrubada, custo de tecnologia e de base): as assinaturas daqui e as de `GameServer.Recursos.cs` nao
/// mudam sem manter a antiga funcionando.
///
/// ============================ A ESCALA, E DE ONDE ELA SAI ============================
/// Tres numeros se seguram, e mexer num pede olhar os outros dois:
///
///   * o PRECO: o catalogo vai de 15 zeni (a pa) a 100-200 mil (nave, Drill, regenerador), com o topo em
///     milhoes. Em recursos e o dobro: 30, 200-400 mil, e dezenas de milhoes.
///   * a MAO DE QUEM MINERA (<see cref="PorSegundo"/>): 3, 6 e 12 por segundo. Uma hora de furadeira sao
///     43 mil recursos -- 21,6 mil zeni, mais ou menos o triplo do que o "cavar" do item rendia e bem acima
///     do tributo de um planeta dominado (1.000 zeni/h). A nave de 100 mil zeni sai em ~4,6 h de furadeira.
///   * a RESERVA do planeta (<see cref="MaximoDoPlaneta"/>): a Terra (500 x 500) guarda 2,5 milhoes e devolve
///     28,9 por segundo -- uma furadeira (12/s) nem a faz descer; tres juntas (36/s) a secam em uns quatro
///     dias, cinco em 22 h. Um mundo pequeno (lado 192) guarda 369 mil, devolve 4,3 por segundo, e uma
///     furadeira sozinha o seca em umas 13 h.
///
/// Ou seja: minerar paga o comeco e o meio do catalogo, um planeta grande so SENTE quando muita gente (ou muita
/// Drill) tira dele ao mesmo tempo, e um pequeno sente uma pessoa so.
///
/// ESTES NUMEROS SAO OS DE 2026-10-11, quando o dono quintuplicou a reserva (*"troque o lado^2 x 2 por
/// lado^2 x 10"*). Com o teto de antes a Terra guardava 500 mil, devolvia 5,8 por segundo e UMA furadeira a
/// secava em 22 h. O tempo de encher nao mudou (<see cref="HorasPraEncher"/>), entao o que o chao devolve por
/// segundo quintuplicou junto com o teto.
/// ====================================================================================
/// </summary>
public static class Recurso
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): pagar com recursos custa o MESMO numero que em zeni -- o
	/// dobro some. Lido pela propria conta, em <see cref="CustoEmRecursos"/>.
	/// </summary>
	public static bool SemDobroDeTeste;

	/// <summary>*"se algo custo 1000 zennis pra fazer vc pode fazer com 2000 resources"*.</summary>
	public const double RecursosPorZeni = 2.0;

	/// <summary>O preco em recursos do que custa <paramref name="custoEmZeni"/> zeni.</summary>
	public static double CustoEmRecursos(double custoEmZeni) =>
		Math.Max(0, custoEmZeni) * (SemDobroDeTeste ? 1.0 : RecursosPorZeni);

	/// <summary>
	/// Quanto de recurso cabe em cada celula do mapa de um planeta -- ver <see cref="MaximoDoPlaneta"/>. O dono
	/// (2026-10-11): *"Reserva do planeta troque o lado^2 x 2 por lado^2 x 10"*.
	/// </summary>
	public const double RecursosPorCelula = 10.0;

	/// <summary>O que cabia por celula ate 2026-10-11. So o defeito injetado o le.</summary>
	private const double RecursosPorCelulaDeAntes = 2.0;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): a celula guarda o de antes -- a reserva volta a ser
	/// lado^2 x 2. Lido pela propria conta, em <see cref="MaximoDoPlaneta"/>.
	/// </summary>
	public static bool CelulaGuardaODeAntesDeTeste;

	/// <summary>
	/// A RESERVA MAXIMA PELO TAMANHO: *"cada planeta tem um resource maximo ao qual e baseado em seu tamanho"*.
	/// Pela AREA (lado ao quadrado, vezes <see cref="RecursosPorCelula"/>): um planeta de lado 192 guarda 368.640,
	/// um de 500 guarda 2,5 milhoes, um de 1000 guarda 10 milhoes.
	/// </summary>
	public static double MaximoDoPlaneta(int ladoEmTiles)
	{
		double lado = Math.Max(0, ladoEmTiles);
		return lado * lado * (CelulaGuardaODeAntesDeTeste ? RecursosPorCelulaDeAntes : RecursosPorCelula);
	}

	/// <summary>
	/// *"ele regenera lentamente com o tempo"*: horas de relogio pra uma reserva ZERADA voltar a cheia.
	///
	/// E UM TEMPO, E NAO UMA TAXA: o que o chao devolve por segundo e o teto dividido por estas horas. Quando o
	/// teto quintuplicou (2026-10-11) este numero ficou como estava -- e a Terra passou de 5,8 pra 28,9 por
	/// segundo. E o que sai de "trocar o x2 pelo x10" sem mexer aqui; quem quiser a taxa de antes com o teto novo
	/// poe 120 horas.
	/// </summary>
	public const double HorasPraEncher = 24.0;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): a reserva nao volta com o tempo -- o estado do nucleo antes
	/// de o tique existir (ela nascia cheia e so descia). Lido pela propria <see cref="ReservaDePlaneta.Regenerar"/>.
	/// </summary>
	public static bool ReservaNaoRegeneraDeTeste;

	// =====================================================================
	// ONDE SE TIRA RECURSO -- a pergunta de lugar, uma copia so pras duas pontas
	// =====================================================================
	/// <summary>
	/// DE QUE PLANETA SAI O RECURSO QUE SE TIRA NESTA ZONA -- nulo = de nenhum: aqui nao ha o que tirar.
	///
	/// DUAS RESPOSTAS QUE NAO SAO NULO, e so elas:
	///   * a SUPERFICIE de um planeta (<see cref="Espaco.EhPlaneta"/>): o proprio;
	///   * a CAVERNA de um planeta (<see cref="Cavernas.PlanetaMae"/>): o planeta-mae. A caverna nao tem reserva
	///     dela -- quem tira la dentro e quem tira la fora dividem o MESMO numero. E a unica excecao de interior
	///     (dono, 2026-10-11).
	/// O Mirante, o alem, a Sala do Tempo, uma estacao, o interior de uma nave ou de um Majin e o espaco: nulo.
	///
	/// QUEM PERGUNTA: o servidor, pra achar a reserva (`GameServer.ReservaDe`) e pra contar juntas as Drills que
	/// tiram do mesmo chao; e o cliente, pra nao acender "MINERANDO" de um pedido que vai ser negado. Planeta
	/// DESTRUIDO e conta do servidor (a zona continua sendo planeta; a reserva e que some).
	/// </summary>
	public static ZoneKey? PlanetaDaReserva(ZoneKey zona) =>
		Espaco.EhPlaneta(zona) ? zona : Cavernas.PlanetaMae(zona);

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--recursosteste` e `--diagminerar`): debaixo de teto tambem se minera -- o jogo
	/// de antes de 2026-10-11, em que so a ZONA contava. Lido pela propria pergunta, em <see cref="OTetoBarra"/>.
	/// </summary>
	public static bool MineraSobTetoDeTeste;

	/// <summary>
	/// O TETO BARRA O VERB MINERAR AQUI? No mapa do planeta, sim: so se minera a ceu aberto -- dentro de predio
	/// ou de base erguida (a area interna, `CelulaInterna.SobTeto`) nao. NA CAVERNA NAO BARRA: ela e interior
	/// inteira, e e justamente a excecao.
	///
	/// E a resposta do dono a "minerar dentro de predio vale?": *"essa e a unica excessao de mapa de interior q
	/// da pra tirar recurso de um planeta"* (2026-10-11) -- interior nao da recurso, menos a caverna.
	///
	/// SO O VERB: a Drill assentada trabalha em qualquer celula do mapa do planeta, com teto ou sem (e uma
	/// maquina do chao do planeta, e e dentro de base que se guarda maquina); a rocha de minerio ja nao nascia
	/// sob teto (`RochaDeMinerio.ChaoServe`).
	/// </summary>
	/// <param name="sobTeto">O corpo esta com os pes numa celula sob teto? Cada ponta responde com o mapa dela.</param>
	public static bool OTetoBarra(ZoneKey zona, bool sobTeto) =>
		sobTeto && !MineraSobTetoDeTeste && !Cavernas.EhCaverna(zona);

	// =====================================================================
	// MINERAR A MAO -- o verb "Minerar"
	// =====================================================================
	/// <summary>
	/// QUANTO SE TIRA POR SEGUNDO, por ferramenta: *"vc comeca a pegar resource por segundo [...] caso vc tenha
	/// uma hand drill ou shovel vc pega esses recursos mais rapidos"*. Cada degrau dobra o anterior.
	///
	/// NAO E O NUMERO DO DM, que nao tinha recurso: la a pa e a furadeira rendiam ZENI, um punhado a cada 5 e a
	/// cada 2,5 s com 20% de chance (`Tier 1.dm:263-265` e `:316-318`). Esse "cavar" chegou a ser portado como
	/// acao do item e SAIU (dono, 2026-10-10: *"tire a opcao de cavar da pa e furadeira pq e redundante"*): as
	/// duas ferramentas so fazem isto aqui. A razao entre elas ficou a de la (2x).
	/// </summary>
	public const double PorSegundoNaMao = 3, PorSegundoComPa = 6, PorSegundoComFuradeira = 12;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): a ferramenta nao conta -- todo mundo minera na mao. Lido
	/// pela propria escolha, em <see cref="MelhorFerramenta"/>.
	/// </summary>
	public static bool FerramentaNaoContaDeTeste;

	public static double PorSegundo(Ferramenta f) => f switch
	{
		Ferramenta.Furadeira => PorSegundoComFuradeira,
		Ferramenta.Pa => PorSegundoComPa,
		_ => PorSegundoNaMao,
	};

	/// <summary>
	/// A MELHOR FERRAMENTA QUE ESTA NA MOCHILA. Basta carrega-la: nao ha "equipar" pra pa nem pra furadeira
	/// neste port (o `Equip`/`Power_Switch` do DM, `Tier 1.dm:241` e `:285`, ligava um `equipped` que so o
	/// proprio `Dig` lia), e pedir um gesto a mais pra minerar seria o tipo de passo que ninguem descobre.
	/// </summary>
	public static Ferramenta MelhorFerramenta(Inventario mochila) =>
		FerramentaNaoContaDeTeste ? Ferramenta.Mao
		: mochila.Quantos(CatalogoDeItens.Furadeira) > 0 ? Ferramenta.Furadeira
		: mochila.Quantos(CatalogoDeItens.Pa) > 0 ? Ferramenta.Pa
		: Ferramenta.Mao;

	/// <summary>Como a ferramenta entra numa frase que o jogador le ("voce minera com a furadeira").</summary>
	public static string ComQue(Ferramenta f) => f switch
	{
		Ferramenta.Furadeira => "com a furadeira",
		Ferramenta.Pa => "com a pá",
		_ => "com as mãos",
	};

	// =====================================================================
	// PAGAR EM ZENI OU EM RECURSOS -- a moeda no pedido, e o preco escrito
	//
	// O dono (2026-10-10), sobre o que ainda so aceitava zeni: *"as melhorias da maquina de gravidade, do
	// regenerador e da nave; a conversao em androide; a estatua SUPREMA. mude pra aceitarem resource, o enma
	// mantenha so zeni"*. O verbo de cada um continua o mesmo; a moeda vai no ARGUMENTO, como a Drill ja fazia
	// (`drill_up zeni|recursos`). Aqui mora a palavra, pros dois lados do fio lerem a mesma.
	// =====================================================================
	/// <summary>
	/// A MOEDA, COMO ELA VIAJA NUM VERBO. As mesmas duas palavras da Drill (`Drill.PagarComZeni`) e do modo de
	/// construir (`PrecoDeBloco.Chave`).
	/// </summary>
	public const string PalavraDeZeni = "zeni", PalavraDeRecursos = "recursos";

	/// <summary>
	/// O ARGUMENTO DE UM PEDIDO PAGO: o que se pede e, em recursos, a palavra da moeda NO FIM ("forca recursos";
	/// so "recursos" quando o verbo nao tem o que escolher). EM ZENI O ARGUMENTO E O DE SEMPRE -- e o que os
	/// clientes de antes desta mudanca mandam, e por isso eles continuam pagando em zeni.
	/// </summary>
	public static string PedidoPago(string oQue, Moeda moeda) =>
		moeda != Moeda.Recursos ? oQue : oQue.Length == 0 ? PalavraDeRecursos : $"{oQue} {PalavraDeRecursos}";

	/// <summary>
	/// LE UM PEDIDO PAGO: a ultima palavra, se for a de uma moeda, e a moeda -- e o resto e o que se pede. Sem
	/// palavra de moeda, zeni.
	/// </summary>
	public static (string OQue, Moeda Moeda) LerPedidoPago(string? argumento)
	{
		string pedido = (argumento ?? "").Trim();
		int corte = pedido.LastIndexOf(' ');
		string ultima = corte < 0 ? pedido : pedido[(corte + 1)..];

		Moeda? moeda = ultima.ToLowerInvariant() switch
		{
			PalavraDeRecursos => Moeda.Recursos,
			PalavraDeZeni => Moeda.Zeni,
			_ => null,
		};
		return moeda is { } m ? (corte < 0 ? "" : pedido[..corte].TrimEnd(), m) : (pedido, Moeda.Zeni);
	}

	/// <summary>O preco NESTA moeda, escrito: "1.000 zeni" ou "2.000 recursos".</summary>
	public static string PrecoEscrito(double custoEmZeni, Moeda moeda) =>
		moeda == Moeda.Recursos ? $"{CustoEmRecursos(custoEmZeni):N0} recursos" : $"{custoEmZeni:N0} zeni";

	/// <summary>O preco NAS DUAS moedas, escrito: "1.000 zeni ou 2.000 recursos".</summary>
	public static string DoisPrecos(double custoEmZeni) =>
		$"{PrecoEscrito(custoEmZeni, Moeda.Zeni)} ou {PrecoEscrito(custoEmZeni, Moeda.Recursos)}";

	// =====================================================================
	// O QUE A PA E A FURADEIRA FAZEM -- o texto que o jogador le
	// =====================================================================
	/// <summary>
	/// A DESCRICAO DE UMA FERRAMENTA DE MINERAR, pelo id do item -- nulo pra qualquer outra coisa.
	///
	/// No DM elas cavavam (`Tier 1.dm:254-331`), e o `desc` de la diz isso: "Allows you to dig. Slowly...". Aqui
	/// elas nao tem acao nenhuma -- o "cavar" saiu, ver <see cref="PorSegundoNaMao"/> -- e o que fazem e acelerar
	/// o verb Minerar por estarem na mochila. E isso que a frase diz, com os numeros que o servidor paga.
	///
	/// UMA FRASE PRA TRES LUGARES: o item da mochila (`CatalogoDeItens`, que e tambem a dica do card da aba
	/// Tech), o `construcoes.json` e o extrator que o regenera (`DmTechScanner`). A `instalar-prova` cobra que os
	/// tres digam a mesma coisa -- mexer numa taxa pede reescrever as duas linhas do json.
	/// </summary>
	public static string? DescricaoDeFerramenta(string idDoItem) => idDoItem switch
	{
		CatalogoDeItens.Pa =>
			$"Na mochila, faz você minerar mais rápido: +{PorSegundoComPa:0} recursos por segundo, "
			+ $"em vez de +{PorSegundoNaMao:0} com as mãos (menu P > Other > Minerar).",
		CatalogoDeItens.Furadeira =>
			$"Na mochila, faz você minerar mais rápido que a pá: +{PorSegundoComFuradeira:0} recursos por segundo "
			+ "(menu P > Other > Minerar).",
		_ => null,
	};

	// =====================================================================
	// AS FRASES DA RECUSA -- uma copia so, pras duas pontas
	//
	// Quem decide e o servidor (`GameServer.PorQueNaoMinera`); o cliente adianta as que ele ja sabe responder
	// (`LocalPlayer.PedirMineracao`), pra a tela nao dizer "MINERANDO" de um pedido que vai ser negado. As duas
	// pontas dizem a MESMA frase porque leem a mesma constante.
	// =====================================================================
	/// <summary>
	/// Zona de onde nao se tira recurso de planeta nenhum (o espaco, um interior que nao e caverna, uma mente), ou
	/// planeta destruido -- e a caverna dele junto: nao ha reserva. Ver <see cref="PlanetaDaReserva"/>.
	/// </summary>
	public const string NaoHaOQueMinerar = "não há o que minerar aqui.";

	/// <summary>Debaixo de teto, no mapa de um planeta. Ver <see cref="OTetoBarra"/>.</summary>
	public const string NaoSeMineraSobTeto = "não dá pra minerar debaixo de teto: só a céu aberto, ou dentro de uma caverna.";

	/// <summary>Nocauteado ou morto.</summary>
	public const string SemCondicoesDeMinerar = "você não está em condições de minerar.";

	/// <summary>Virado em Oozaru (dirigindo ou nao).</summary>
	public const string AFeraNaoMinera = "a fera não minera.";

	/// <summary>Numa cena, na area de espera do torneio ou pilotando.</summary>
	public const string NaoDaPraMinerarAgora = "não dá pra minerar agora.";

	/// <summary>So o cliente a diz: no servidor, quem anda simplesmente para de minerar.</summary>
	public const string NaoSeMineraAndando = "pare antes: não se minera andando.";
}

/// <summary>
/// O QUE VAI PRO DISCO de uma reserva: quanto havia, e QUANDO. O que cresceu de la pra ca se deriva da hora
/// (<see cref="ReservaDePlaneta.DoDisco"/>), entao o arquivo so precisa ser regravado quando alguem TIRA.
///
/// SO AS MEXIDAS VAO PRO ARQUIVO: reserva cheia e a resposta padrao, e ausencia e a resposta pra ela -- a mesma
/// disciplina do livro dos planetas mortos (`FaseDaMorte.Vivo`).
/// </summary>
public sealed class ReservaGravada
{
	/// <summary>A chave do planeta em texto -- ver <see cref="ChaveDePlaneta.Texto"/>.</summary>
	public string Chave = "";

	/// <summary>O nome da ZONA. Nao identifica (a identidade e a chave), mas faz parte do endereco de um mundo gerado.</summary>
	public string Nome = "";

	public double Atual;

	/// <summary>O instante em que <see cref="Atual"/> valia: Unix ms, relogio de parede.</summary>
	public long Em;
}

/// <summary>
/// A RESERVA DE UM PLANETA: tem teto (pelo tamanho), cai quando tiram, e volta devagar com o tempo.
///
/// <see cref="Tirar"/> e a UNICA porta de saida -- a mineracao a mao, o Drill e a rocha de minerio passam todos
/// por ela, e e por isso que um Drill que seca o planeta faz as rochas pararem de nascer.
///
/// A CAVERNA DO PLANETA TIRA DESTA MESMA (<see cref="Recurso.PlanetaDaReserva"/>): nao ha uma segunda reserva
/// pra ela, nem no disco. Quem minera la dentro e quem minera la fora descem o mesmo numero.
///
/// NAO GUARDE O OBJETO: pergunte de novo ao servidor (`ReservaDe(zona)`) a cada uso. A limpeza total, a morte e
/// a volta de um planeta TROCAM o objeto, e quem tivesse guardado o antigo tiraria de uma reserva que ninguem
/// mais le.
/// </summary>
public sealed class ReservaDePlaneta
{
	public double Maximo { get; }
	public double Atual { get; private set; }

	/// <summary>De 0 (seca) a 1 (cheia). Zero num planeta sem reserva nenhuma.</summary>
	public double Fracao => Maximo <= 0 ? 0 : Atual / Maximo;

	/// <summary>Esta no teto? E o que a deixa de fora do arquivo -- ver <see cref="ReservaGravada"/>.</summary>
	public bool Cheia => Atual >= Maximo;

	/// <summary>
	/// DE QUE PLANETA ELA E: a chave em texto e o nome da zona, como o disco os guarda. Quem preenche e o
	/// servidor, ao cria-la; vazios numa reserva avulsa (as de bancada).
	/// </summary>
	public string Chave { get; init; } = "";

	/// <inheritdoc cref="Chave"/>
	public string Nome { get; init; } = "";

	/// <summary>A zona deste planeta, remontada do registro -- o caminho de volta do disco.</summary>
	public ZoneKey Zona() => ChaveDePlaneta.ZonaDe(Chave, Nome);

	/// <summary>
	/// QUANTAS VEZES ALGUEM TIROU ALGUMA COISA DAQUI. Sobe a cada <see cref="Tirar"/> que saiu com algo na mao,
	/// e e por ele que o servidor sabe que ha o que gravar: quem tira e qualquer um dos tres sistemas, direto
	/// neste objeto, e nenhum deles precisa lembrar de avisar ninguem.
	/// </summary>
	public long Mexidas { get; private set; }

	/// <param name="atual">Quanto ha agora; nulo = nasce cheia.</param>
	public ReservaDePlaneta(double maximo, double? atual = null)
	{
		Maximo = Math.Max(0, maximo);
		Atual = Math.Clamp(atual ?? Maximo, 0, Maximo);
	}

	/// <summary>Tira ATE <paramref name="quanto"/> e devolve o que SAIU de fato -- zero com a reserva seca.</summary>
	public double Tirar(double quanto)
	{
		if (!(quanto > 0)) return 0;
		double saiu = Math.Min(quanto, Atual);
		if (saiu <= 0) return 0;
		Atual -= saiu;
		Mexidas++;
		return saiu;
	}

	/// <summary>O tempo que passou devolve um pedaco do teto -- ver <see cref="Recurso.HorasPraEncher"/>.</summary>
	public void Regenerar(double segundos)
	{
		if (Recurso.ReservaNaoRegeneraDeTeste) return;
		if (!(segundos > 0) || Maximo <= 0) return;
		Atual = Math.Min(Maximo, Atual + Maximo * segundos / (Recurso.HorasPraEncher * 3600.0));
	}

	/// <summary>O registro que vai pro disco, carimbado com a hora de agora.</summary>
	public ReservaGravada Gravar(long agoraMs) => new() { Chave = Chave, Nome = Nome, Atual = Atual, Em = agoraMs };

	/// <summary>
	/// A RESERVA DE VOLTA DO DISCO, ja com o que cresceu enquanto ninguem olhava: o tempo entre o carimbo e
	/// agora regenera como qualquer outro -- inclusive o tempo com o servidor DESLIGADO. E de proposito, e o
	/// avesso do pavio do planeta que morre (`EstadoDaMorte.Faltam`): la uma noite de servidor fora nao pode
	/// explodir um mundo; aqui ela so devolve minerio ao chao, e isso nao tira nada de ninguem.
	///
	/// Carimbo no futuro (relogio do sistema acertado pra tras) conta como zero, nunca como tempo negativo.
	/// </summary>
	public static ReservaDePlaneta DoDisco(ReservaGravada g, double maximo, long agoraMs)
	{
		var r = new ReservaDePlaneta(maximo, g.Atual) { Chave = g.Chave, Nome = g.Nome };
		r.Regenerar(Math.Max(0, agoraMs - g.Em) / 1000.0);
		return r;
	}
}
