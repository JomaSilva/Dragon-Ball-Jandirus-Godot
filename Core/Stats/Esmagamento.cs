namespace Jandirus.Core.Stats;

/// <summary>
/// O ESMAGAMENTO -- porte de `Code/Modules/Stats/BP/Gravity.dm:41-72` (`Grav_Handler`) mais o
/// atraso de passo do `Code/Modules/Movement Improvement/movement handler.dm:56-63`.
///
/// ============================ POR QUE ISTO PRECISAVA EXISTIR ============================
/// Ate aqui o port tinha o PREMIO da gravidade alta e nao tinha o PRECO. `GravGain` pagava BP por
/// tique proporcional a gravidade absoluta, e a unica coisa que a gravidade cobrava era o
/// `gravFelt` (uma reducao no poder EXPRESSO, que so aparece no scouter). Ou seja: um planeta de
/// gravidade 80 era ganho de graca, e a decisao "onde eu treino?" tinha uma resposta so.
///
/// O mesmo vale pro PESO. Vestir peso rendia BP (`weight` multiplica o ganho ate 8x) e nao custava
/// nada -- nem um passo mais lento. Um custo que nao existe faz do maximo a escolha obvia, e uma
/// escolha obvia deixa de ser escolha.
/// ========================================================================================
///
/// ============================ A RAZAO E A PIOR DAS DUAS ============================
/// O DM: `var/r = max(Gravity / max(GravMastered, 1), weight_ratio)`. Peso e gravidade esmagam pela
/// MESMA regra e a pior das duas manda -- e o peso ja embute a gravidade local (o `weight_ratio` e
/// `Weighted x grav / weight_cap_hw`, escrito no <see cref="Fighter.WeightTick"/>). Por isso nao ha
/// duas contas aqui: ha uma razao, e uma pergunta separada (<see cref="PorPeso"/>) que so serve pra
/// a mensagem dizer o que afrouxar.
/// ==================================================================================
///
/// TUDO AQUI E FUNCAO PURA DA FICHA. Quem aplica dano, quem trava o passo e quem desenha o aviso
/// sao tres lugares diferentes do servidor e do cliente -- e os tres precisam concordar sobre
/// quando o corpo esta sendo esmagado. Duas copias da formula divergem no dia em que alguem mexe
/// numa (PARTE 3, armadilha 4).
/// </summary>
public static class Esmagamento
{
	// ============================ OS QUATRO NUMEROS DO `1A Defines.dm` ============================
	/// <summary>`GRAVCRUSH_DMG_BASE = 0.5`: dano/segundo espalhado em TODOS os membros, na base.</summary>
	public const double DanoBase = 0.5;

	/// <summary>
	/// `GRAVCRUSH_DMG_CAP = 3`: teto do dano/segundo. O comentario do DM diz o porque, e ele e de
	/// desenho e nao de balanceamento: *"esmagado desmaia rapido mas morre DEVAGAR (da tempo de
	/// alguem resgatar)"*.
	/// </summary>
	public const double DanoTeto = 3;

	/// <summary>
	/// `GRAVCRUSH_EXPLODE_R = 4`: a razao a partir da qual o corpo fica PRESO no chao -- e, se ja
	/// estiver em farrapos, se desfaz.
	/// </summary>
	public const double RazaoQuePrende = 4;

	/// <summary>
	/// `GRAVCRUSH_SLOW = 1`: o peso da lentidao MULTIPLICATIVA. Com 1, o dobro da maestria e metade
	/// da velocidade e o quadruplo e um quarto -- e a razao de ser multiplicativo esta escrita no
	/// proprio DM: *"o -2 flat antigo sumia no ganho dos personagens rapidos"*.
	/// </summary>
	public const double PesoDaLentidao = 1;
	// ==========================================================================================

	/// <summary>A gravidade que este corpo sente agora (planeta + maquina de gravidade).</summary>
	public static double Gravidade(Fighter f) => f.Planetgrav + f.gravmult;

	/// <summary>
	/// A RAZAO DE ESMAGAMENTO: `max(gravidade / max(maestria, 1), weight_ratio)`. 1 = no limite,
	/// 2 = o dobro do que o corpo aguenta.
	/// </summary>
	public static double Razao(Fighter f) =>
		Math.Max(Gravidade(f) / Math.Max(f.GravMastered, 1), f.weight_ratio);

	/// <summary>Acima do limite do corpo? (o `if(r > 1)` do `Grav_Handler`)</summary>
	public static bool Esmaga(Fighter f) => Razao(f) > 1;

	/// <summary>
	/// PRESO NO CHAO -- o `gravParalysis = 1` do DM (`Gravity.dm:66-67`), que la vira `mobTime = 0`
	/// no `movement handler.dm:132`.
	///
	/// Nao ha campo guardado pra isto de proposito: e uma pergunta sobre o estado ATUAL, e um bit
	/// guardado seria mais um "ligou e esqueceu de desligar" -- exatamente o que o proprio DM tem que
	/// desfazer a mao em `Grav_Gain` (`if(testgrav != 3) gravParalysis = 0`) e no `MajinSaga.dm:196`.
	/// </summary>
	public static bool Prende(Fighter f) => Razao(f) >= RazaoQuePrende;

	/// <summary>
	/// QUEM MANDA NO ESMAGAMENTO: o PESO ou a GRAVIDADE? (`var/porpeso` do DM.)
	///
	/// So serve pra mensagem, e a mensagem e o sistema: "seu corpo range" sem dizer se a saida e
	/// tirar os pesos ou sair do planeta e um aviso que nao ensina nada.
	/// </summary>
	public static bool PorPeso(Fighter f) => f.weight_ratio > Gravidade(f) / Math.Max(f.GravMastered, 1);

	/// <summary>
	/// DANO POR SEGUNDO, quadratico no excesso e com teto:
	/// `min(GRAVCRUSH_DMG_BASE * excess * max(excess, 0.2), GRAVCRUSH_DMG_CAP)`.
	///
	/// **A defesa NAO divide isto**, e o DM registra por que: a formula antiga era
	/// `/(1 + Ephysdef*Ekidef)` e pra qualquer personagem forte o dano virava ~0 -- ou seja
	/// "gravidade nao fazia nada" justamente pra quem podia encarar gravidade alta.
	///
	/// O `max(excess, 0.2)` e um PISO no segundo fator, nao no primeiro: logo acima do limite o dano
	/// e linear e pequeno (razao 1,1 da 0,01/s), e so vira castigo de verdade acima do dobro.
	/// </summary>
	public static double DanoPorSegundo(Fighter f)
	{
		double excesso = Razao(f) - 1;
		if (excesso <= 0) return 0;
		return Math.Min(DanoBase * excesso * Math.Max(excesso, 0.2), DanoTeto);
	}

	/// <summary>Dreno de folego por segundo: `stamina -= maxstamina * 0.002 * r`.</summary>
	public static double DrenoDeVigorPorSegundo(Fighter f) =>
		Esmaga(f) ? f.maxstamina * 0.002 * Razao(f) : 0;

	// =====================================================================
	// O PASSO
	// =====================================================================
	/// <summary>
	/// QUANTO DO PASSO SOBRA: o fator que multiplica o `SpeedStat`. 1 = passo cheio.
	///
	/// ============================ A TRADUCAO DO `mobTime`, E O QUE ELA CUSTA ============================
	/// O BYOND nao tem velocidade: ele tem um ACUMULADOR de tempo por tique (`mobTime`) e da um passo
	/// quando ele enche. As duas regras que este metodo porta sao escritas nesse acumulador:
	///
	///     mobTime += 0.3 + max(log(3.6, Epspeed), 0.1)          // o passo normal (:48-49)
	///     if(weight > 1) mobTime -= weight * (1 / Espeed)       // o atraso do PESO (:56)
	///     if(mvcrush > 1) mobTime /= 1 + (mvcrush-1)*GRAVCRUSH_SLOW  // o esmagamento (:63)
	///     if(mobTime < 0.1) mobTime = 0.1                       // o piso (:70)
	///
	/// Aqui o movimento e px/s (`MoveRules.SpeedPx`), entao a traducao honesta e a RAZAO entre o
	/// acumulador com as penalidades e o acumulador sem elas -- e nao um numero novo inventado. A
	/// divisao do esmagamento atravessa a razao intacta (ela ja e multiplicativa); a subtracao do
	/// peso vira `(base - peso/Espeed) / base`, que e a MESMA fracao de passo que o BYOND perde.
	///
	/// O que se GANHA: um so numero (`SpeedStat`) carrega tudo, entao as duas pontas obedecem a mesma
	/// velocidade pelo mesmo campo -- sem cliente andando rapido e servidor corrigindo (o corpo
	/// tremendo, o defeito classico deste port).
	/// O que se PERDE: o `min(mobTime, MAXIMUM_TIME + OMEGA_RATE)` do DM, que corta o acumulador antes
	/// de dividir. Ele existe porque o `dash` do BYOND soma `0.5*Epspeed` (~55!) e a divisao sumiria
	/// no excedente descartado; numa razao normalizada nao ha excedente pra descartar, entao a
	/// divisao ja morde sempre -- que e exatamente o efeito que aquela linha existia pra garantir.
	/// ==============================================================================================
	///
	/// O PISO NAO E ZERO. `mobTime < 0.1 -> 0.1` e o "proxy nerf to fastboys" do DM, e aqui ele
	/// impede que o peso sozinho pare alguem: quem esta apenas carregado se ARRASTA. Parar de vez e
	/// outra regra, com outro gatilho e outra porta -- ver <see cref="Prende"/>.
	/// </summary>
	public static double FatorDePasso(Fighter f) => AtrasoDoPeso(f) / Freio(f);

	/// <summary>
	/// A fracao do passo que sobra depois do PESO -- `(base - weight/Espeed) / base`, com o piso
	/// `0.1/base` do DM.
	///
	/// `weight` so passa de 1 com peso vestido (ver <see cref="Fighter.WeightTick"/>), e o teto dele
	/// e 8. Note que quem tem `Espeed` alto perde MENOS: no DM o atraso e dividido pela velocidade,
	/// entao treinar velocidade e o que devolve o passo a quem quer carregar muito -- e isso e o
	/// original, nao uma suavizada.
	/// </summary>
	public static double AtrasoDoPeso(Fighter f)
	{
		if (f.weight <= 1) return 1;

		double baseDoPasso = 0.3 + Math.Max(DmMath.Log(3.6, Math.Max(f.Epspeed, 1e-9)), 0.1);
		double sobra = baseDoPasso - f.weight * (1 / Math.Max(f.Espeed, 0.1));
		double piso = 0.1;
		return Math.Max(sobra, piso) / baseDoPasso;
	}

	/// <summary>O divisor do esmagamento: `1 + (r-1) * GRAVCRUSH_SLOW`, e 1 (sem freio) abaixo do limite.</summary>
	public static double Freio(Fighter f)
	{
		double r = Razao(f);
		return r > 1 ? 1 + (r - 1) * PesoDaLentidao : 1;
	}

	// =====================================================================
	// O QUE A TELA MOSTRA
	// =====================================================================
	/// <summary>
	/// COMO O JOGADOR LE A RAZAO. Tres faixas, e elas sao as mesmas do painel do DM
	/// (`HtmlUI.dm:648-651`): dentro do limite, esmagando, esmagamento fatal.
	/// </summary>
	public static string Rotulo(Fighter f)
	{
		double r = Razao(f);
		if (r >= RazaoQuePrende) return "ESMAGAMENTO FATAL";
		if (r > 1) return "esmagando";
		return "dentro do limite";
	}
}
