namespace Jandirus.Core.Social;

/// <summary>Uma onda de defensores: quantos corpos, e a que fracao do poder do invasor.</summary>
public readonly record struct OndaDeDefesa(int Quantos, double Fator);

/// <summary>
/// **CONQUISTA DE PLANETAS** -- os numeros e as contas. Porte de `Code/Modules/Globals/PlanetConquest.dm`.
///
/// ============================ O QUE ESTE ARQUIVO E ============================
/// So a REGRA: os limiares, a tabela de ondas, o tributo por hora, a lealdade que decai e as
/// travessias de faixa. Nada aqui sabe o que e um `ServerPlayer`, uma zona ou um pacote -- e o
/// mesmo recorte do <see cref="Reputacao"/>, e existe pelo mesmo motivo: a bancada mede a regra sem
/// subir servidor, e o servidor nao tem numero solto no meio do laco.
/// ============================================================================
///
/// ============================ E A CHAVE E O ENDERECO, NAO O NOME ============================
/// O DM guarda `conq_data["Planeta"]` -- indexado por NOME -- e `conq_premade()` e literalmente
/// `pl == "Earth" || pl == "Vegeta" || pl == "Namek"`. As duas coisas morreram com os sistemas
/// solares: ha planetas gerados em qualquer lugar e os nomes deles COLIDEM (ver
/// <see cref="World.EnderecoDePlaneta"/>). Um dominio aqui e chaveado pela trinca (Sx, Sy, K), e o
/// nome so aparece em texto de tela.
/// ======================================================================================
///
/// ============================ A DIVISAO QUE SOBREVIVEU NAO E "PRE-FEITO" ============================
/// O DM separa pre-feito de procedural porque so os tres pre-feitos dele tinham povo. Aqui a
/// pergunta certa e **"tem cidadao?"**: os seis planetas com linha de povoamento tem gente pra
/// defender e valem tributo cheio; Arconia (pre-feita, sem povo) e todo mundo gerado nao tem
/// habitante nenhum, e caem no mesmo caso. Ver <see cref="TributoPorHora"/>.
/// ================================================================================================
/// </summary>
public static class Conquista
{
	// =====================================================================
	// AS PORTAS -- `#define` de PlanetConquest.dm:38-47, literais
	// =====================================================================
	/// <summary>
	/// `CONQ_MIN_BP_POP` (:38). BP **EXPRESSO**, e nao base -- e o que o verb do DM compara
	/// (`usr.expressedBP &lt; minbp`, :395).
	///
	/// ARMADILHA 6 DA PARTE 3 CONFERIDA, e o resultado e que ela NAO morde aqui: `expressedBP` so
	/// e muito menor que `BP` em corpo recem-nascido (BP de duas casas); num corpo em repouso e
	/// adulto os fatores de estado valem 1 e o expresso acompanha o base. O numero fica literal, e
	/// e uma DECISAO DO DONO -- cinco milhoes e a barreira que o DM poe entre "sei lutar" e "posso
	/// tomar um mundo com povo".
	/// </summary>
	public const double BpMinimoComPovo = 5_000_000;

	/// <summary>`CONQ_MIN_BP_PROC` (:39). Mundo sem habitante custa vinte vezes menos.</summary>
	public const double BpMinimoSemPovo = 250_000;

	/// <summary>`CONQ_MAX_DOMAINS` (:40). Tres, e e a primeira metade do "manter custa".</summary>
	public const int MaximoDeDominios = 3;

	/// <summary>`CONQ_PLAYER_CD` (:41): 18.000 decimos = 30 min entre campanhas do MESMO jogador.</summary>
	public const double SegundosEntreCampanhas = 1800;

	/// <summary>`CONQ_TIMEOUT` (:42): 6.000 decimos = 10 min pra invasao inteira.</summary>
	public const double SegundosDeInvasao = 600;

	/// <summary>`CONQ_WAVE_BREATHER` (:43): 80 decimos = 8 s de respiro entre ondas.</summary>
	public const double SegundosDeRespiro = 8;

	/// <summary>`CONQ_RIP_SECS` (:44): 8 s de canal pra ARRANCAR uma bandeira de invasao.</summary>
	public const double SegundosDeArranque = 8;

	/// <summary>A tensao antes da primeira onda -- o `sleep(30)` de :499, que sao 3 s.</summary>
	public const double SegundosDeTensao = 3;

	/// <summary>`CONQ_LEADER_FACTOR` (:45): o LIDER entra junto na ultima onda, a 0,95x do invasor.</summary>
	public const double FatorDoLider = 0.95;

	/// <summary>`CONQ_DEF_BP_FLOOR` (:47): piso de BP de qualquer defensor.</summary>
	public const double PisoDeBpDeDefensor = 5000;

	/// <summary>`CONQ_TRIB_POP` (:48) e `CONQ_TRIB_PROC` (:49): zeni por hora REAL de dominio.</summary>
	public const double TributoComPovoPorHora = 1000, TributoSemPovoPorHora = 250;

	/// <summary>`CONQ_TRIB_CAP_H` (:50): o tributo para de acumular depois de 24 h sem coleta.</summary>
	public const double HorasDeTetoDoTributo = 24;

	// --- os deltas de reputacao (:51-55) ---
	public const double RepPorMatarDefensor = -6;
	public const double RepPorMatarLider = -15;
	public const double RepPorReivindicarEmPaz = 10;
	public const double RepPorLibertar = 40;
	public const double RepPorArrancarBandeira = 15;

	/// <summary>
	/// `REP_KILL_CITIZEN` (PlanetReputation.dm:20). Nao e da conquista -- e o gancho generico de
	/// "matar habitante", que era o item 1 do <see cref="Reputacao.OqueFalta"/> e que a conquista
	/// obrigou a existir: o defensor tem delta PROPRIO (-6/-15) justamente porque ele PULA este.
	/// </summary>
	public const double RepPorMatarHabitante = -10;

	// =====================================================================
	// AS ONDAS -- `conq_waves_pop` / `conq_waves_proc` (:58-59)
	// =====================================================================
	/// <summary>
	/// 4x0,30 -> 4x0,50 -> 2x0,45, e o LIDER entra junto na ultima. Literal do DM.
	///
	/// O fator e sobre o BP **expresso** do invasor relido A CADA ONDA, com piso no valor do inicio
	/// (`max(M.expressedBP, I.base_bp)`, :509) -- e esse `max` e um anti-trapaca: sem ele, destransformar
	/// entre ondas faria a onda seguinte nascer fraca.
	/// </summary>
	public static readonly OndaDeDefesa[] OndasComPovo =
		[new(4, 0.30), new(4, 0.50), new(2, 0.45)];

	/// <summary>
	/// ============================ O MUNDO SEM POVO NAO TEM GUARNICAO, E ISSO NAO E ESQUECIMENTO ============================
	/// O DM enche as ondas do planeta procedural com a FAUNA do bioma: `conq_make_beast` instancia
	/// `pspace_biomes[D.biome][4]`, que e um **tipo de inimigo**, e pra isso ele precisa LIGAR o
	/// `npcspawnson` na marra por duas linhas (:671-674).
	///
	/// Neste port o inimigo comum esta **DESLIGADO por pedido do dono**
	/// (`Npc.Povoamento.InimigoComumLigado`), e a guarda dele mora no funil de nascimento justamente
	/// pra que ninguem a contorne por uma porta lateral. Escrever aqui um molde de fera seria fazer
	/// exatamente o que aquele funil existe pra impedir -- e seria a pior forma de fazer, porque
	/// pareceria uma feature e nao uma exceção.
	///
	/// Entao mundo sem povo se conquista pela BANDEIRA: ela leva <see cref="SegundosDePlantio"/> pra
	/// firmar e qualquer um pode arranca-la nesse tempo. E a contestacao sozinha -- que e o que sobra
	/// quando nao ha povo, e que e justamente o que da PvP sobre os mundos gerados.
	///
	/// No dia em que o dono religar o inimigo comum, a fauna entra por uma LINHA aqui e mais nada
	/// muda: o motor de ondas ja e o mesmo dos dois lados.
	/// ================================================================================================================
	/// </summary>
	public static readonly OndaDeDefesa[] OndasSemPovo = [];

	/// <summary>
	/// Quanto tempo a bandeira leva pra firmar num mundo SEM POVO. E o lugar da contestacao ali:
	/// o `CONQ_TIMEOUT` de 10 min so faz sentido com onda pra vencer, e um plantio instantaneo nao
	/// daria a ninguem a chance de aparecer. Um minuto e o dobro do canal de arranque somado a um
	/// tempo de voo curto -- longo o bastante pra ser interrompivel, curto pra nao ser tedio.
	/// </summary>
	public const double SegundosDePlantio = 60;

	// =====================================================================
	// MANTER CUSTA -- **NAO EXISTE NO DM.** Ver o cabecalho abaixo.
	// =====================================================================
	/// <summary>
	/// ============================ O DM NAO COBRA NADA POR MANTER, E ISSO E UM BURACO ============================
	/// Vasculhado o `PlanetConquest.dm` inteiro: um dominio conquistado e do jogador **para sempre**.
	/// Nao ha decaimento, nao ha presenca exigida, nao ha custo. O unico jeito de perder e alguem
	/// invadir (:578), o planeta ser destruido (:171) ou voce abandonar (:269). Ou seja: quem
	/// conquistou tres mundos numa noite os tem tres meses depois sem nunca mais voltar a nenhum.
	///
	/// Isso transforma conquista numa COLECAO DE TROFEUS. A regra abaixo e proposta deste port, e ela
	/// e escrita a partir do unico numero que o proprio DM ja usa como medida de negligencia: o teto
	/// de acumulo do tributo, `CONQ_TRIB_CAP_H` = 24 h. O DM ja diz, com esse teto, "vinte e quatro
	/// horas sem aparecer e o limite do que o sistema tolera" -- so nao tirava consequencia disso.
	/// ========================================================================================================
	///
	/// A LEALDADE E UM NUMERO DO POVO, e nao do jogador: ela mede o quanto aquele mundo ainda aceita
	/// aquele soberano. Ela faz tres coisas, e a graduacao e o que impede a regra de ser uma
	/// guilhotina:
	///
	///   1. **corta o tributo** proporcionalmente -- negligencia custa DINHEIRO antes de custar o
	///      planeta, e o jogador ve o numero cair antes de perder qualquer coisa;
	///   2. **enfraquece a guarnicao** (<see cref="FatorDaGuarnicao"/>) -- um dominio abandonado se
	///      defende mal de quem vier toma-lo, o que amarra "manter" a "contestacao";
	///   3. **zerada, o dominio se perde**: o povo derruba a bandeira sozinho.
	/// </summary>
	public const double LealdadeMaxima = 100;

	/// <summary>
	/// A CARENCIA, em horas reais. Igual ao teto do tributo do DM, e de proposito: e o mesmo numero
	/// dizendo a mesma coisa duas vezes ("ate aqui, ausencia nao e abandono"), em vez de duas
	/// constantes que alguem ajusta separadas.
	/// </summary>
	public const double HorasDeCarencia = HorasDeTetoDoTributo;

	/// <summary>
	/// QUANTO A LEALDADE CAI POR HORA depois da carencia. 2 pontos: de 100 a zero sao 50 h de
	/// abandono **alem** das 24 de carencia -- pouco mais de tres dias reais sem pisar la.
	///
	/// O numero e escolhido pra que o teto DISPARE (corolario 0.7): um dominio de fim de semana
	/// sobrevive a semana de trabalho de quem visita uma vez; um dominio esquecido cai. Um valor
	/// que levasse um mes seria indistinguivel de decaimento nenhum.
	/// </summary>
	public const double QuedaDeLealdadePorHora = 2;

	/// <summary>
	/// QUANTO A PRESENCA DEVOLVE POR HORA no planeta. 60 = **um ponto por minuto**: um dominio
	/// caido a 50 volta ao cheio com cinquenta minutos de presenca.
	///
	/// Recuperar mais devagar do que se perde (60/h contra 2/h) parece generoso e nao e: o dono
	/// PRECISA estar la, e uma hora de presenca e cara. O que se compra com ela sao 24 h de
	/// carencia + 50 h de decaimento -- ou seja, a conta fecha a favor de quem aparece.
	/// </summary>
	public const double RecuperacaoDeLealdadePorHora = 60;

	/// <summary>
	/// A LEALDADE DEPOIS DE <paramref name="horas"/> HORAS. Uma casa so: quem esta presente sobe,
	/// quem esta ausente ha mais que a carencia desce, e quem esta ausente dentro dela nao mexe.
	///
	/// A CARENCIA E CONTADA A PARTIR DA ULTIMA VISITA e nao do intervalo deste tique -- por isso o
	/// argumento e `horasDesdeAVisita` e nao "quanto tempo passou". Sem isso, um servidor que
	/// tiquesse de hora em hora nunca sairia da carencia.
	/// </summary>
	public static double Lealdade(double atual, bool presente, double horasDesdeAVisita, double horasDoPasso)
	{
		if (presente)
			return Math.Clamp(atual + RecuperacaoDeLealdadePorHora * horasDoPasso, 0, LealdadeMaxima);

		if (horasDesdeAVisita <= HorasDeCarencia) return Math.Clamp(atual, 0, LealdadeMaxima);

		// So a parte do passo que caiu FORA da carencia cobra. Sem isto, o tique que atravessa a
		// fronteira das 24 h cobraria o passo inteiro e a carencia seria menor do que diz.
		double cobravel = Math.Min(horasDoPasso, horasDesdeAVisita - HorasDeCarencia);
		return Math.Clamp(atual - QuedaDeLealdadePorHora * cobravel, 0, LealdadeMaxima);
	}

	/// <summary>
	/// O QUANTO A GUARNICAO DE UM DOMINIO NEGLIGENCIADO AINDA VALE. Meio a um.
	///
	/// Nao vai a zero: mesmo um mundo que odeia o soberano tem gente com medo dele. O que ele perde
	/// e a metade que era lealdade, e isso e o bastante pra um invasor de poder parecido notar a
	/// diferenca entre tomar um dominio cuidado e um esquecido.
	/// </summary>
	public static double FatorDaGuarnicao(double lealdade) =>
		0.5 + 0.5 * Math.Clamp(lealdade, 0, LealdadeMaxima) / LealdadeMaxima;

	/// <summary>O nome da faixa. Existe porque lealdade e um numero que ninguem ve -- ver `Reputacao.Faixa`.</summary>
	public static string FaixaDeLealdade(double l) =>
		l >= 90 ? "leal"
		: l >= 60 ? "inquieto"
		: l >= 30 ? "descontente"
		: l > 0 ? "em revolta"
		: "perdido";

	/// <summary>
	/// A FRASE DA TRAVESSIA DE FAIXA, ou vazio. Mesmo desenho -- e mesmo motivo -- do
	/// <see cref="Reputacao.Travessia"/>: perder um planeta sem nunca ter lido um aviso de que ele
	/// estava escorregando seria a pior forma possivel de descobrir esta regra.
	/// </summary>
	public static string TravessiaDeLealdade(double antes, double depois, string planeta) =>
		antes > 60 && depois <= 60
			? $"O povo de {planeta} comenta a sua ausência."
		: antes > 30 && depois <= 30
			? $"{planeta} está descontente: sem você por perto, o tributo mingua e a guarnição afrouxa."
		: antes > 10 && depois <= 10
			? $"REVOLTA em {planeta}! Se você não aparecer, perde o domínio."
		: "";

	// =====================================================================
	// TRIBUTO
	// =====================================================================
	/// <summary>`conq_tribute_rate` (:118) -- com a divisao que sobreviveu (ver o cabecalho).</summary>
	public static double TributoPorHora(bool temPovo) =>
		temPovo ? TributoComPovoPorHora : TributoSemPovoPorHora;

	/// <summary>
	/// O TRIBUTO ACUMULADO, em zeni inteiros. `conq_tribute_pending` (:121) mais a lealdade.
	///
	/// O TETO DE 24 H E DO DM e nao se mexe: ele e o que impede o dominio de virar renda passiva de
	/// quem some. A lealdade multiplica DEPOIS do teto -- as duas cobram a mesma negligencia por
	/// caminhos diferentes (uma limita o acumulo, a outra corta a taxa), e compor as duas e o
	/// desenho, nao um engano.
	/// </summary>
	public static double TributoDevido(double horasDesdeAColeta, bool temPovo, double lealdade)
	{
		double h = Math.Clamp(horasDesdeAColeta, 0, HorasDeTetoDoTributo);
		double bruto = h * TributoPorHora(temPovo);
		return Math.Floor(bruto * Math.Clamp(lealdade, 0, LealdadeMaxima) / LealdadeMaxima);
	}

	// =====================================================================
	// A LUTA
	// =====================================================================
	/// <summary>
	/// O BP DE UM DEFENSOR: a fracao da onda sobre o poder de referencia, com o piso do DM.
	///
	/// A LEALDADE ENTRA SO QUANDO A DEFESA E DE UM DONO (a guarnicao). O povo defendendo a si mesmo
	/// nao tem lealdade a ninguem -- ele luta inteiro.
	/// </summary>
	public static double BpDoDefensor(double bpDeReferencia, double fatorDaOnda, double fatorDaGuarnicao) =>
		Math.Max(bpDeReferencia * fatorDaOnda * fatorDaGuarnicao, PisoDeBpDeDefensor);

	/// <summary>
	/// O PODER DE REFERENCIA DE UMA ONDA -- `max(M.expressedBP, I.base_bp)` (:509).
	///
	/// Relido a cada onda **e com piso no instante do plantio**: sem o piso, destransformar entre
	/// ondas encomendaria defensores fracos; sem a releitura, transformar depois do plantio daria
	/// uma luta grande de graca.
	/// </summary>
	public static double ReferenciaDaOnda(double expressoAgora, double expressoNoPlantio) =>
		Math.Max(Math.Max(expressoAgora, expressoNoPlantio), 1);

	// =====================================================================
	// A DIVIDA
	// =====================================================================
	/// <summary>
	/// O QUE DO `PlanetConquest.dm` **NAO** ESTA AQUI. Lista e nao comentario, pelo mesmo motivo do
	/// <see cref="Reputacao.OqueFalta"/> e do `RankDef.Pendencias`: sistema pela metade que nao diz
	/// onde acaba e sistema que ninguem termina.
	/// </summary>
	public static readonly string[] OqueFalta =
	[
		"a bandeira DESENHADA no chao (a arte é 'Images/ConquestFlag.dmi' e ainda não foi convertida; "
		+ "hoje ela existe como posição no livro e o alcance de uso é medido contra ela)",
		"a guarnição da FAUNA em mundo gerado (conq_make_beast, :668) -- inimigo comum está desligado "
		+ "por pedido do dono; ver OndasSemPovo",
		"o traje e o cabelo do defensor por raça (conq_make_defender, :646-664) -- aqui ele nasce com "
		+ "a aparência sorteada do cidadão",
	];
}
