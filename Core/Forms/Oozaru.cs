namespace Jandirus.Core.Forms;

/// <summary>Em que Oozaru o corpo esta. Nao e degrau da escada SSJ -- ver o cabecalho de <see cref="Oozaru"/>.</summary>
public enum FormaOozaru : byte
{
	Nao = 0,
	Regular = 1,
	Dourado = 2,
}

/// <summary>Por que o Oozaru nao rolou. <see cref="Pode"/> = rolou.</summary>
public enum RecusaOozaru
{
	Pode = 0,
	SangueDeMais,     // menos de 50% de genoma Saiyajin
	SemRabo,
	Caido,            // KO ou morto
	JaEsta,
	SemLinhagemPrimal,   // Dourado exige Primal Saiyan
	SemMaestriaSsj,      // Dourado exige o SSJ1 DOMINADO (100%), nao so despertado
	SemPoder,            // Dourado exige o BP BASE minimo DESTE personagem (ver PortaOozaruDourado)
}

/// <summary>
/// O OOZARU, portado de `Code/Modules/Skills/Buffs/racial/Oozaru.dm`.
///
/// ============================ POR QUE NAO E UM DEGRAU DA ESCADA ============================
/// A tentacao e por `Oozaru` no enum <see cref="Forma"/> junto do SSJ1..SSJ4. Seria errado por tres
/// motivos, e todos aparecem no proprio DM:
///
///   1. **Nao se sobe pra ele.** Ele acontece POR OLHAR A LUA (`Apeshit()`), sem o jogador pedir --
///      e o `Osetting` existe justamente pra deixar de olhar. A escada e uma decisao; isto e um
///      acidente que te pega.
///   2. **O multiplicador vem de outro campo.** O regular usa `giantFormbuff` (1,5x) e o Dourado
///      usa `ssjBuff` (18x) -- e o Dourado ZERA o `giantFormbuff` de proposito pra o total ficar
///      exatamente 18 e nao 27. Enfiar isso no `EscadaSaiyajin.Multiplicador` misturaria dois
///      sistemas que o original mantem separados.
///   3. **Ele CONVIVE com o SSJ.** O Dourado so existe pra quem ja esta em SSJ -- as duas coisas
///      sao lidas ao mesmo tempo, e um enum unico so guarda uma.
///
/// Entao o Oozaru e um estado PARALELO, como no DM (`Apeshit`, `golden`, `bigform` sao vars
/// separadas do `ssj`).
/// ==========================================================================================
/// </summary>
public static class Oozaru
{
	// ==================================================================================
	// OS NUMEROS -- todos de `Oozaru.dm`, e todos ja com o rework que o dono pediu la
	// ==================================================================================

	/// <summary>
	/// OOZARU REGULAR: **1,5x**, e so isso.
	///
	/// `container.giantFormbuff = 1.5` com o comentario do proprio DM: "Oozaru regular = APENAS o
	/// buff de 1.5x de BP". O `OozaruBuff` fica em **1** -- ele MULTIPLICAVA kicapacity e powerupcap
	/// por 6 e enchia o tanque, e isso foi tirado num rework anterior deste projeto.
	/// </summary>
	public const double MultRegular = 1.5;

	/// <summary>OOZARU DOURADO: **18x** (`ssjBuff = 18`).</summary>
	public const double MultDourado = 18;

	/// <summary>
	/// LEGENDARY PRIMAL: **20x**. `if(container.Class == "Legendary Primal Saiyan") ssjBuff = 20`.
	/// </summary>
	public const double MultDouradoLegendary = 20;

	/// <summary>A classe que troca o 18 pelo 20.</summary>
	public const string ClasseLegendaryPrimal = "Legendary Primal Saiyan";

	/// <summary>A linhagem que abre o Dourado (e o caminho do SSJ4). `statsaiyan.dm:4`.</summary>
	public const string LinhagemPrimal = "Primal Saiyan";

	/// <summary>Os ids no <see cref="Catalogo"/>. Sao a ponte entre o estado paralelo e o livro de maestrias.</summary>
	public const string IdRegular = "oozaru";
	public const string IdDourado = "oozaru_dourado";

	/// <summary>A forma do catalogo que corresponde a este estado. Vazio quando nao ha fera.</summary>
	public static string Id(FormaOozaru f) => f switch
	{
		FormaOozaru.Regular => IdRegular,
		FormaOozaru.Dourado => IdDourado,
		_ => "",
	};

	/// <summary>Genoma Saiyajin minimo: `genome.race_percent("Saiyan") >= 50`.</summary>
	public const double GenomaMinimo = 50;

	/// <summary>
	/// O QUE O CORPO GANHA E PERDE. Somas TEMPORARIAS (os `T*` do DM), desfeitas no DeBuff.
	///
	/// Repare que velocidade e tecnica CAEM: o Oozaru e um tanque lento e desajeitado, nao uma
	/// forma melhor em tudo. Sem isto virar macaco seria decisao obvia e deixaria de ser decisao.
	/// </summary>
	public const double SomaPhysoff = 1.2;
	public const double SomaSpeed = -1.5;
	public const double SomaTecnica = -1.5;

	/// <summary>
	/// QUANTO A FORMA DURA SOZINHA, em segundos.
	///
	/// `spawn(3000)` (`Oozaru.dm:46`, `:56`) e `spawn(1000)` (`:36`) no DM -- e eles sao DECISSEGUNDOS,
	/// nao quadros: 300 s e 100 s. Ver <see cref="TempoDoDm"/>, que e onde a unidade esta explicada e
	/// provada; aqui estava escrito `/12` (250 s e 83 s), 20% curto.
	///
	/// O 300 tem confirmacao independente no proprio DM: `PlanetPopulation.dm:471` comenta o MESMO
	/// `sleep(3000)` como *"every ~5 min"*.
	/// </summary>
	public const double SegundosRegular = 3000 / TempoDoDm.TiquesPorSegundo;
	public const double SegundosDourado = 1000 / TempoDoDm.TiquesPorSegundo;

	// ==================================================================================
	// A MAESTRIA DA FERA
	//
	// ============================ O `Apeshitskill` FOI DELETADO ============================
	// O DM guarda o dominio sobre o macaco num campo proprio de 0 a 10 (`Apeshitskill`), separado
	// do resto do jogo, e o porte tinha copiado isso literalmente (`Fighter.Apeshitskill`, lido so
	// pelo `ctrlParalysis`). Ele saiu, e o Oozaru passou a usar o MESMO livro de maestria de todas
	// as outras formas (<see cref="Maestrias"/>, 0 a 100 por id de forma). Tres coisas vieram de
	// graca com a troca, e nenhuma delas existiria com um campo separado:
	//
	//   1. **Regular e Dourado tem maestrias SEPARADAS.** O DM tem uma so, entao quem domasse o
	//      macaco comum ja nasceria dono do Dourado -- e o dono descreveu os dois como duas
	//      escolas ("ao virar golden oozaru ... o mesmo processo acontece"). Duas entradas de
	//      catalogo, duas maestrias, sem uma linha de codigo a mais.
	//   2. **A aba Formas ja as mostra.** Ela lista o que tem maestria > 0 (`MenuJogo.cs:1104`) e
	//      o pacote de atributos ja carrega o livro inteiro. Um campo proprio precisaria de
	//      entrada no protocolo, na tela e no save.
	//   3. **O `PedeMaestria`/`PedeMaestriaDe` do catalogo passou a alcancar a fera** -- e e assim
	//      que o SSJ4 cobra "Dourado a 100%" sem nenhum `if` novo.
	//
	// O QUE SE PERDEU: o `Apeshitskill` de saves antigos. E aceitavel porque ele nunca chegou a
	// valer nada -- o Oozaru so ficou alcancavel na camada anterior (o botao embaixo da lua), e
	// ninguem acumulou tempo de forma antes disso.
	// ====================================================================================
	// ==================================================================================

	/// <summary>
	/// QUANTO TEMPO DE FORMA CUSTA DOMINAR A FERA, em segundos acumulados.
	///
	/// Meia hora -- um SEXTO das ~3h que uma forma da escada pede (`Catalogo.MaestriaPorSegundo`),
	/// e mesmo assim a maestria mais cara do jogo em tempo de RELOGIO. Um SSJ se sustenta a hora
	/// que se quiser; o Oozaru so existe sob lua cheia, e cada aparicao dela paga no maximo
	/// <see cref="SegundosRegular"/> (300 s) de maestria -- sao 6 noites cheias pro regular e,
	/// como o Dourado dura um terco disso, ~18 pro Dourado.
	///
	/// Igualar as 3h da escada seria pedir 36 luas cheias pro regular. O numero nao mede esforco:
	/// mede quantas vezes o mundo precisa girar.
	/// </summary>
	public const double SegundosParaDominar = 30 * 60;

	/// <summary>Maestria ganha por segundo TRANSFORMADO. Ver <see cref="SegundosParaDominar"/>.</summary>
	public const double MaestriaPorSegundo = 100.0 / SegundosParaDominar;

	/// <summary>
	/// O PISO DO PRAZO DE CONTROLE, em segundos -- o que ate quem tem 0% de maestria dirige.
	///
	/// Sem ele a curva daria ZERO no primeiro Oozaru: a cinematica terminaria e o corpo ja seria do
	/// servidor, sem um unico quadro em que o jogador tenha mandado nele. Isso e indistinguivel de
	/// defeito -- e um jogador que acha que achou um bug nao aprende a regra, ele reporta. Estes
	/// seis segundos existem pra que a perda de controle seja uma COISA QUE ACONTECE e nao um
	/// estado em que a forma nasce.
	/// </summary>
	public const double SegundosDeGraca = 6;

	/// <summary>
	/// MEDITAR E O FREIO. `angertick = 1000` (`Oozaru.dm:103`) e `if(container.med) angertick--`
	/// (`:165-166`) -- meditando, a forma cai sozinha.
	///
	/// ============================ ISTO NAO E UM `sleep`, E UM CONTADOR ============================
	/// Aqui estava `1000 / 12.0` (83 s), e o erro nao era so o doze: `angertick` nao e prazo nenhum,
	/// e um CONTADOR decrementado uma vez por `Loop()` do buff -- e todo `Loop()` roda dentro do
	/// `BuffLoop()` do `GlobalStats()`, que dorme `sleep(3)`. Logo o passo e 0,3 s e nao um tique:
	/// 1000 x 0,3 = **300 s**. Ver <see cref="TempoDoDm.SegundosDoLacoGlobalStats"/>.
	///
	/// E ISSO DEIXA O FREIO SEM FREAR: 300 s e exatamente o <see cref="SegundosRegular"/>. No DM,
	/// meditar nao encurta o Oozaru comum -- ele so antecipa a queda do Dourado (100 s). Nao mexo
	/// nisso: e o que o original faz, e mudar seria decisao do dono e nao conserto de unidade.
	/// ==========================================================================================
	///
	/// E a unica saida de quem esta descontrolado, e por isso ela existe: sem ela, `ctrlParalysis`
	/// seria uma punicao sem resposta.
	/// </summary>
	public const double SegundosMeditandoAteCair =
		1000 * TempoDoDm.TiquesDoLacoGlobalStats / TempoDoDm.TiquesPorSegundo;

	// ==================================================================================
	// AS PORTAS
	// ==================================================================================

	/// <summary>
	/// PODE VIRAR OOZARU AO OLHAR A LUA?
	///
	/// `Apeshit()`: `genome.race_percent("Saiyan") >= 50`, `!Apeshit && Tail && !KO`.
	/// O rabo nao e detalhe -- e o orgao que capta a luz da lua no cânone, e o DM derruba a forma
	/// no instante em que ele some (`Loop(): if(!container.Tail) DeBuff()`).
	/// </summary>
	public static RecusaOozaru PodeVirar(double genomaSaiyajin, bool temRabo, bool caido, FormaOozaru agora)
	{
		if (agora != FormaOozaru.Nao) return RecusaOozaru.JaEsta;
		if (genomaSaiyajin < GenomaMinimo) return RecusaOozaru.SangueDeMais;
		if (!temRabo) return RecusaOozaru.SemRabo;
		if (caido) return RecusaOozaru.Caido;
		return RecusaOozaru.Pode;
	}

	/// <summary>
	/// QUAL DOS DOIS SAI. `Apeshit()` decide por uma coisa so: se ja esta em SSJ, vira Dourado.
	///
	/// E o Dourado tem porta propria (`GoldenApeshit`): linhagem Primal e ja ter despertado o SSJ.
	/// Quem esta em SSJ mas nao e Primal **nao vira nada** -- o `GoldenApeshit` volta sem fazer
	/// nada, e o regular nao roda porque o `else` do original ja escolheu o Dourado. E fiel: no DM,
	/// um Saiyajin comum em SSJ que olha a lua fica igual.
	/// </summary>
	public static FormaOozaru QualSai(bool estaEmSsj, string linhagem, Maestrias maestrias,
									  double bpBase, LimiaresPessoais? limiares)
	{
		if (!estaEmSsj) return FormaOozaru.Regular;
		return PodeDourado(linhagem, maestrias, bpBase, limiares) == RecusaOozaru.Pode
			? FormaOozaru.Dourado : FormaOozaru.Nao;
	}

	/// <summary>
	/// AS TRES CONDICOES EXTRAS DO DOURADO (`GoldenApeshit`), **lidas do catalogo**.
	///
	/// Elas moram na entrada `oozaru_dourado` (`PedeLinhagem`, `PedeMaestriaDe`, `PedeMaestria`,
	/// `PortaBp`/`ChaveDoLimiar`) e nao em constantes daqui, de proposito: o `Avaliar` recusa a
	/// linha inteira do Oozaru (ela nao se sobe), entao sem este metodo aqueles campos seriam dado
	/// escrito e nunca lido -- o defeito que este port ja pagou varias vezes. Editar o catalogo muda
	/// o gate; nao ha segunda verdade sobre quem vira Dourado.
	///
	/// A MAESTRIA E A MUDANCA: o DM pede so `hasssj`, e o dono pediu "precisa ter pelomenos o ss1
	/// masterizado". Ver o comentario da entrada no catalogo.
	///
	/// ============================ E A PORTA DE PODER, QUE FALTAVA ============================
	/// O dono pediu o Dourado como "dominar o ssj1 MAIS um poder minimo, tambem requisito pessoal".
	/// So a maestria estava implementada. `bpBase` e `limiares` entraram na assinatura por isso -- e
	/// entraram como parametros e nao como leitura de algum estado global porque este metodo e do
	/// Core, que nao conhece jogador nenhum.
	///
	/// A CONTA E A MESMA DO `EstadoDeForma.Avaliar` (passo 8), copiada de proposito e nao
	/// compartilhada: `Limiares?.Porta(d)` quando ha limiar sorteado, `d.PortaBp` de fabrica quando
	/// nao ha (save antigo, NPC). Duas leituras diferentes do mesmo campo dariam duas portas
	/// diferentes pro mesmo personagem conforme o caminho -- a lua ou a tecla C.
	///
	/// A ORDEM E A DA MENSAGEM, igual a do `Avaliar`: linhagem, depois maestria, depois poder. Quem
	/// nao e Primal nunca vai ouvir "falta BP" sobre uma forma que nao e dele.
	/// ========================================================================================
	/// </summary>
	public static RecusaOozaru PodeDourado(string linhagem, Maestrias maestrias,
										   double bpBase, LimiaresPessoais? limiares)
	{
		FormaDef? d = Catalogo.Def(IdDourado);
		if (d == null) return RecusaOozaru.SemLinhagemPrimal;   // catalogo mutilado: nega, nao concede

		if (d.PedeLinhagem.Length > 0
			&& !string.Equals(linhagem, d.PedeLinhagem, StringComparison.OrdinalIgnoreCase))
			return RecusaOozaru.SemLinhagemPrimal;

		if (d.PedeMaestria > 0 && maestrias.De(d.PedeMaestriaDe) < d.PedeMaestria)
			return RecusaOozaru.SemMaestriaSsj;

		if (d.PortaBp > 0)
		{
			double porta = limiares?.Porta(d) is > 0 and var p ? p : d.PortaBp;
			if (bpBase < porta) return RecusaOozaru.SemPoder;
		}

		return RecusaOozaru.Pode;
	}

	/// <summary>O multiplicador de BP desta forma, ja com a excecao da classe Legendary.</summary>
	public static double Multiplicador(FormaOozaru f, string classe) => f switch
	{
		FormaOozaru.Regular => MultRegular,
		FormaOozaru.Dourado => string.Equals(classe, ClasseLegendaryPrimal, StringComparison.OrdinalIgnoreCase)
			? MultDouradoLegendary : MultDourado,
		_ => 1,
	};

	/// <summary>Quanto tempo esta forma dura sozinha.</summary>
	public static double Duracao(FormaOozaru f) => f switch
	{
		FormaOozaru.Regular => SegundosRegular,
		FormaOozaru.Dourado => SegundosDourado,
		_ => 0,
	};

	/// <summary>A fera esta DOMADA de vez? So aos 100% -- e a promessa do dono: "nunca mais perde o controle".</summary>
	public static bool DominouAFera(double maestria) => maestria >= 100;

	/// <summary>
	/// ============================ QUANTO TEMPO VOCE DIRIGE A FERA ============================
	/// O `ctrlParalysis` do DM e um interruptor: abaixo de 10 de `Apeshitskill` voce NUNCA manda no
	/// corpo, a partir de 10 voce SEMPRE manda. O dono pediu uma rampa no lugar dele -- "conforme
	/// vc sobe de maestria mais tempo vc consegue controlar ele antes de perder o controle, ate
	/// chegar no 100% onde vc domina ele completamente e nunca mais perde o controle" -- entao a
	/// maestria deixou de ser um sim/nao e virou um PRAZO.
	///
	/// A CURVA E `duracao * f²`, com `f` = maestria/100. Por que quadratica e nao linear:
	///
	///   * A maestria e ganha LINEARMENTE no tempo de forma (<see cref="MaestriaPorSegundo"/>). Com
	///     um prazo tambem linear, cada noite de lua cheia compraria a mesma fatia de controle, e a
	///     progressao inteira teria a mesma textura do comeco ao fim -- o jogador na metade do
	///     caminho ja estaria dirigindo metade da forma, que e "resolvido o bastante" pra que os
	///     ultimos 50% pareçam so imposto.
	///   * Com a quadratica o prazo COMECA lento e ACELERA. 25% de maestria dao 6% da forma; 50%
	///     dao 25%; 75% dao 56%; 90% dao 81%. O trecho que mais muda o jogo e o ultimo -- e e
	///     exatamente onde o dono pos a recompensa (o 100% que domina de vez, e, no Dourado, o
	///     SSJ4). A curva faz a reta final ser a parte que se SENTE.
	///   * E ela continua sendo, em qualquer ponto, mais generosa que o original, que dava ZERO ate
	///     o fim. Nao ha como esta escolha ser mais dura que o DM.
	///
	/// AOS 100%, INFINITO -- e infinito de verdade (`PositiveInfinity`), nao um numero grande. Um
	/// numero grande e um prazo que um dia vence, e "nunca mais perde o controle" nao pode ter uma
	/// data escondida no fim.
	/// ====================================================================================
	/// </summary>
	public static double SegundosDeControle(double maestria, FormaOozaru f)
	{
		if (f == FormaOozaru.Nao) return 0;
		if (DominouAFera(maestria)) return double.PositiveInfinity;

		double frac = Math.Clamp(maestria, 0, 100) / 100.0;
		return Math.Max(SegundosDeGraca, Duracao(f) * frac * frac);
	}

	/// <summary>
	/// DA PRA VOLTAR POR VONTADE PROPRIA? O verb `Oozaru Revert` do DM: regular exige a fera
	/// DOMADA; Dourado so volta sozinho (ou virando SSJ4).
	///
	/// Continua pedindo os 100% e NAO "estar no prazo de controle", de proposito: com o prazo, quem
	/// tem 0% de maestria teria <see cref="SegundosDeGraca"/> pra desfazer a transformacao antes de
	/// ela cobrar qualquer coisa -- e a lua cheia deixaria de ser um acontecimento pra virar um
	/// botao de 1,5x de BP com seis segundos de arrependimento gratis.
	/// </summary>
	public static bool PodeReverterSozinho(FormaOozaru f, double maestria)
		=> f == FormaOozaru.Regular && DominouAFera(maestria);

	/// <summary>
	/// SAIR DO DOURADO VIRA SSJ4 -- e este e o pre-requisito que faltava pra o degrau que ja
	/// estava na escada esperando.
	///
	/// `ApeshitRevert`: `if(usr.golden &amp;&amp; usr.Apeshit &amp;&amp; usr.Race == "Saiyan")` e
	/// `if(usr.hasssj4)` -> "You try to revert your transformation, but end up being a Super Saiyan 4."
	///
	/// ============================ O CIRCULO QUE SE FECHOU ============================
	/// O DM (e o porte ate aqui) exige `hasssj4` -- ja TER o SSJ4 -- pra cair nele saindo do
	/// Dourado. Só que o unico jeito de ter o SSJ4 e sair do Dourado: a porta pedia a chave que ela
	/// mesma guardava. Ninguem notava porque `Apeshit` marcava o Dourado como despertado logo na
	/// primeira transformacao, e isso ja bastava pro gate `PedeFormaDespertada` do catalogo.
	///
	/// A maestria e o que abre o circulo, e e o que o dono descreveu: **dominar o Dourado** e o que
	/// LIBERA o SSJ4. Dai em diante o `ssj4Liberado` do DM assume e toda saida do Dourado cai em
	/// SSJ4, como la.
	/// ==============================================================================
	/// </summary>
	public static bool ViraSsj4AoSair(FormaOozaru f, string raca, bool ssj4Liberado, double maestriaDourado)
		=> f == FormaOozaru.Dourado
		   && string.Equals(raca, "Saiyan", StringComparison.OrdinalIgnoreCase)
		   && (ssj4Liberado || DominouAFera(maestriaDourado));
}
