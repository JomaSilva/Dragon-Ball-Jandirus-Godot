namespace Jandirus.Core.World;

/// <summary>
/// AS OITO FASES DA LUA, com os nomes e a ordem do original.
///
/// O DM guarda isto como um inteiro `mooncycle` por área (`Modules/Turfs/Weather.dm:6-7`) e o
/// comentário dele fixa os dois extremos: "mooncycle increments by 1, max is 5, 8 is the last
/// phase before it loops back. 1 is new moon". Ou seja: 1 é lua nova, 5 é lua CHEIA, e a volta
/// acontece depois da 8.
///
/// POR QUE OITO E NÃO UM NÚMERO REDONDO: porque é o que o jogo antigo usava, e é a fase 5 que
/// dispara o Oozaru. Trocar a contagem sem trocar o 5 moveria a lua cheia pro lugar errado do
/// ciclo, e nada acusaria -- o jogo continuaria tendo lua cheia, só que na noite errada.
/// </summary>
public enum FaseDaLua : byte
{
	Nova = 1,
	CrescenteFina = 2,
	QuartoCrescente = 3,
	CrescenteGibosa = 4,
	Cheia = 5,
	MinguanteGibosa = 6,
	QuartoMinguante = 7,
	MinguanteFina = 8,
}

/// <summary>
/// A LUA DE UM PLANETA -- se existe, e qual é a dela.
///
/// A DEFASAGEM É O CAMPO QUE IMPORTA. Sem ela, todo planeta do universo teria lua cheia na mesma
/// noite: um Saiyajin descobriria o ciclo uma vez e saberia o de todos os mundos pra sempre.
/// Com ela, cada planeta corre o próprio calendário, e "quando é a cheia aqui?" volta a ser uma
/// pergunta sobre ESTE lugar.
/// </summary>
public readonly struct LuaDoPlaneta
{
	/// <summary>Este mundo tem lua? (o `HasMoon` das áreas do DM).</summary>
	public bool Existe { get; init; }

	/// <summary>Quantas noites o ciclo daqui está adiantado em relação ao calendário do planeta.</summary>
	public long Defasagem { get; init; }

	/// <summary>Tamanho do disco no céu, 1 = a lua da Terra. Enfeite dos mundos gerados.</summary>
	public float Tamanho { get; init; }

	/// <summary>Sem lua: Namek, o espaço, o Outro Mundo, e todo interior.</summary>
	public static readonly LuaDoPlaneta Nenhuma = default;

	/// <summary>
	/// A lua de um planeta que tem MAPA PRÓPRIO. Quem responde "tem lua?" é o `planetas.json`,
	/// extraído do `HasMoon` das áreas do DM; a defasagem sai do nome, pra ser a mesma em toda
	/// máquina sem viajar no fio.
	/// </summary>
	public static LuaDoPlaneta DoNome(string nome, bool existe) => new()
	{
		Existe = existe,
		Defasagem = (long)(Espaco.Hash64(nome + "|lua") % (ulong)Ceu.Fases),
		Tamanho = 1f,
	};

	/// <summary>
	/// A LUA DE UM MUNDO SORTEADO. Função pura da seed: cliente e servidor chegam à mesma lua
	/// sem trocar um byte, que é a mesma regra do terreno e do céu de estrelas.
	///
	/// NÃO VEM DO DM -- lá o espaço procedural inteiro é `HasMoon = 0`
	/// (`Modules/Tech/ProceduralSpace.dm:260-274`), ou seja, nenhum mundo gerado jamais teve lua.
	/// Foi pedido do dono que os gerados tivessem, e está escrito aqui como escolha nossa e não
	/// como extração.
	/// </summary>
	public static LuaDoPlaneta DaSeed(ulong seed)
	{
		ulong h = Espaco.Misturar(seed, Espaco.Hash64("lua"), 0);
		return new LuaDoPlaneta
		{
			// pouco mais da metade dos mundos tem lua: a lua cheia só é um acontecimento
			// enquanto houver planeta onde ela não nasce
			Existe = h % 100 < 55,
			Defasagem = (long)((h >> 8) % (ulong)Ceu.Fases),
			// de meia lua a lua e meia -- um mundo alienígena não precisa ter o céu da Terra
			Tamanho = 0.6f + (h >> 20) % 100 / 100f,
		};
	}
}

/// <summary>
/// O RELÓGIO DE UM PLANETA: quanto dura o dia dele, onde ele está nesse dia, e que céu ele tem.
///
/// ============================ POR QUE CADA MUNDO TEM O SEU ============================
/// No BYOND havia UM relógio (`Modules/Globals/WorldClock.dm`) e todo planeta obedecia a ele: era
/// meio-dia na Terra e meio-dia em Vegeta, sempre, porque o `Hours` era global. Isso é o que se
/// faz quando o mundo é uma pilha de andares do mesmo servidor -- e é o que o dono pediu pra
/// mudar aqui: cada planeta com o próprio horário.
///
/// O QUE CONTINUA GLOBAL É O INSTANTE. O servidor manda UM número (segundos desde a origem do
/// tempo) e cada planeta traduz esse instante pro próprio dia, com a própria duração e a própria
/// defasagem. Nada de um relógio por zona pra dessincronizar: são funções puras do mesmo número.
/// Isso também é o que deixa a carta estelar dizer "em Namek é dia" sem perguntar nada ao
/// servidor.
/// ======================================================================================
///
/// TER DIA E TER NOITE SÃO DUAS PERGUNTAS SEPARADAS, e vêm do DM. As áreas de lá carregam
/// `HasDay`/`HasNight` (`Modules/Turfs/Areas.dm`) com o comentário explicando a intenção: "if
/// HasDay is ticked off, night will loop back into night. If HasNight is ticked off, day will
/// loop back into day. Both ticked off will result in dusk only." NAMEK É `HasNight=0` -- o
/// planeta dos três sóis não anoitece, e isso é do anime, não invenção nossa.
/// </summary>
public readonly struct RelogioDoPlaneta
{
	/// <summary>
	/// Quanto dura uma rotação, em segundos REAIS.
	///
	/// NÃO VEM DO DM (lá só havia o relógio global). A duração de cada mundo é escolha nossa,
	/// declarada no extrator -- ver `DmPlanetScanner.DiaPorNome`.
	/// </summary>
	public double SegundosPorDia { get; init; }

	/// <summary>Onde este mundo está no próprio dia quando o relógio global marca zero (0 a 1).</summary>
	public double Defasagem { get; init; }

	public bool TemDia { get; init; }
	public bool TemNoite { get; init; }

	/// <summary>
	/// ESTE LUGAR TEM LUZ PRÓPRIA: nada de ambiente, nada de hora, nada de clima na cor da cena.
	///
	/// ============================ POR QUE UM CAMPO, E NÃO "MEIO-DIA PARADO" ============================
	/// Porque meio-dia parado **não é branco**. O tom do meio-dia deste jogo é `dcdcd2` (ver
	/// `Iluminacao.AmbienteDia`), um branco levemente quente que multiplica a cena inteira -- e ele é
	/// a resposta certa pra um planeta ao meio-dia. A dimensão mental não é um planeta ao meio-dia:
	/// no DM ela é um z-level construído em tempo de execução (`build_mind_z`), **sem sol, sem área
	/// de clima e sem nada que module cor**. O `White.dmi` é desenhado como ele é.
	///
	/// ISTO FOI ACHADO POR UMA FOTO, e é o tipo de coisa que só a foto acha. A bancada de regras já
	/// estava verde: a zona resolvia pra planta de 100x100, o nascimento caía na célula do DM, o
	/// pincel do chão era o mesmo da parede. Só que interior neste port cai em <see cref="Ceu.SemCeu"/>
	/// -- *"crepúsculo parado"* --, e crepúsculo sobre branco dá **malva**: a foto mediu `R0,38 G0,26
	/// B0,30` e ZERO pixel quase-branco, com o HUD escrito "poente". O quarto estava certo e a luz
	/// estava errada, e o dono continuaria vendo *"um lugar nada a ver"*.
	///
	/// O CAMPO É DO RELÓGIO e não do desenho porque quem responde "que céu tem aqui?" é o Core, e as
	/// duas pontas leem a mesma resposta. Ver <see cref="Ceu.DimensaoBranca"/>.
	/// ==============================================================================================
	/// </summary>
	public bool LuzPropria { get; init; }

	public LuaDoPlaneta Lua { get; init; }

	/// <summary>O relógio da Terra -- e o padrão de todo mundo que não disser o contrário.</summary>
	public static RelogioDoPlaneta Padrao => new()
	{
		SegundosPorDia = Ceu.SegundosPorDia,
		Defasagem = 0,
		TemDia = true,
		TemNoite = true,
		Lua = LuaDoPlaneta.Nenhuma,
	};

	/// <summary>
	/// O DIA LOCAL deste planeta no instante global dado. A parte inteira é qual dia; a fração é
	/// a hora (0 = meia-noite).
	/// </summary>
	public double DiaLocal(double segundosDoMundo) =>
		segundosDoMundo / Math.Max(SegundosPorDia, 1) + Defasagem;

	/// <summary>Quantos minutos reais dura um dia aqui. Só pro texto da ficha.</summary>
	public double MinutosPorDia => SegundosPorDia / 60;
}

/// <summary>O céu de um lugar num instante: a hora local, a lua, e o quanto ela ilumina.</summary>
public readonly struct EstadoDoCeu
{
	/// <summary>A hora LOCAL do planeta, de 0 (meia-noite) a 1 (meia-noite de novo).</summary>
	public double Hora { get; init; }

	/// <summary>
	/// A hora crua, antes do travamento de `HasDay`/`HasNight`. Num mundo sem noite ela continua
	/// andando enquanto a <see cref="Hora"/> fica presa no meio-dia -- é ela que faz o calendário
	/// (e portanto o ciclo da lua) seguir contando em planeta de sol eterno.
	/// </summary>
	public double HoraCrua { get; init; }

	public bool Noite { get; init; }

	/// <summary>1 a 8 na ordem do DM; 0 quando o planeta não tem lua.</summary>
	public int Fase { get; init; }

	public bool TemLua => Fase > 0;
	public bool Cheia => Fase == Ceu.Cheia;

	/// <summary>
	/// A ALTURA DA LUA no céu: 0 no horizonte, 1 no ponto mais alto, negativo quando está
	/// abaixo. É isto que separa "a lua cheia está NASCENDO" de "a lua cheia se PÕE" -- as duas
	/// falas que o DM escreve em vermelho no chat (`Weather.dm:197-199`).
	/// </summary>
	public double Altura { get; init; }

	/// <summary>Quanto do disco está aceso, de 0 (nova) a 1 (cheia).</summary>
	public double Aceso { get; init; }

	/// <summary>Tamanho do disco, 1 = a lua da Terra.</summary>
	public float Tamanho { get; init; }

	/// <summary>
	/// O QUANTO ESTA LUA CLAREIA A NOITE, de 0 a 1. Já leva a altura em conta: lua cheia no
	/// horizonte ilumina pouco, lua cheia a pino ilumina tudo.
	/// </summary>
	public double Luar => Fase == 0 || Altura <= 0 ? 0 : Aceso * Altura;

	/// <summary>A lua está no céu AGORA e dá pra encarar? É a pergunta que o Oozaru faz.</summary>
	public bool LuaNoCeu => Fase > 0 && Altura > 0;
}

/// <summary>
/// O CÉU, COMO REGRA PURA: que horas são NAQUELE planeta, em que fase a lua dele está, e onde ela
/// está no céu.
///
/// ============================ POR QUE ISTO É UMA FUNÇÃO E NÃO UM ESTADO ============================
/// Tudo aqui sai de UM número -- os segundos do mundo -- mais a ficha do planeta. Não há contador
/// andando, não há nada pra salvar e não há nada que possa dessincronizar: dado o mesmo instante e
/// o mesmo planeta, servidor e cliente chegam à mesma lua sozinhos.
///
/// É o que conserta o defeito que estava aqui: o ciclo do dia era um contador LOCAL do cliente
/// (`Iluminacao._Process` somando `delta`), começando em 0,42 a cada `_Ready`. Dois jogadores no
/// mesmo planeta viam horas diferentes, e quem entrasse mais tarde nunca alcançava. Enquanto o
/// céu fosse só enfeite isso passava; com o Oozaru pendurado nele, viraria um jogador virando
/// macaco gigante numa lua cheia que o outro não está vendo.
/// ==================================================================================================
///
/// A LUA MUDA DE FASE AO ANOITECER, nunca à meia-noite. No DM o `mooncycle` avança na transição
/// dia->noite (`Weather.dm:168-171`), e é a única escolha que funciona: a noite atravessa a
/// meia-noite, então contar o ciclo por DIA de calendário trocaria a lua no meio da noite, na
/// cara de quem está olhando pra ela.
/// </summary>
public static class Ceu
{
	/// <summary>
	/// QUANTO DURA O DIA PADRÃO (o da Terra), em segundos reais. 24 minutos = um minuto por hora
	/// do relógio.
	///
	/// ESTE É O ÚNICO LUGAR onde esse número existe. Ele estava escrito em dois (o ciclo do dia
	/// no cliente e a escala do universo no <see cref="Espaco"/>), com um comentário pedindo pra
	/// quem mudasse um lembrar do outro -- e pedido de lembrança em comentário é um erro marcado
	/// pra acontecer. Agora a distância Terra->Namek e a hora do pôr do sol saem da mesma linha.
	/// </summary>
	public const double SegundosPorDia = 24 * 60;

	/// <summary>Quantas fases o ciclo tem. Oito, como o `mooncycle` do DM.</summary>
	public const int Fases = 8;

	/// <summary>A fase da LUA CHEIA. É o `if(currentMoonlight==5)` do `Weather.dm:196`.</summary>
	public const int Cheia = (int)FaseDaLua.Cheia;

	/// <summary>
	/// A hora em que a noite começa e a hora em que acaba, na mesma escala da curva de luz.
	///
	/// Saíram dos degraus que o cliente já usava pra nomear a hora ("noite" a partir de 0,80,
	/// "madrugada" até 0,26) -- se a lua nascesse fora deles, ela apareceria com o céu ainda
	/// claro. Uma noite dura 0,46 do dia; num dia de 24 minutos, pouco mais de onze reais.
	/// </summary>
	public const double Anoitecer = 0.80;
	public const double Amanhecer = 0.26;

	/// <summary>A hora em que um mundo de crepúsculo eterno fica parado (`HasDay` e `HasNight` off).</summary>
	public const double Crepusculo = 0.78;

	/// <summary>A hora em que um mundo sem noite fica parado. É o "pleno dia" do DM.</summary>
	public const double MeioDia = 0.50;

	/// <summary>A hora em que um mundo sem dia fica parado.</summary>
	public const double MeiaNoite = 0.00;

	public static double DuracaoDaNoite => 1 - Anoitecer + Amanhecer;

	public static double HoraDe(double dias) => dias - Math.Floor(dias);

	public static bool EhNoite(double hora) => hora >= Anoitecer || hora < Amanhecer;

	/// <summary>
	/// O TRAVAMENTO DE `HasDay`/`HasNight`, na mesma forma do DM (`Weather.dm:159-166`): lá o
	/// `daylightcycle` é GRAMPEADO num degrau válido, não remapeado. Um mundo sem noite vive o dia
	/// normalmente e, na hora em que anoiteceria, o céu simplesmente para em pleno dia.
	///
	/// Grampear e não acelerar é o que mantém o CALENDÁRIO andando: o dia local continua virando,
	/// então a lua de um mundo sem noite ainda muda de fase -- ela só nunca aparece.
	/// </summary>
	public static double HoraVisivel(RelogioDoPlaneta r, double horaCrua)
	{
		if (!r.TemDia && !r.TemNoite) return Crepusculo;
		if (!r.TemNoite) return EhNoite(horaCrua) ? MeioDia : horaCrua;
		if (!r.TemDia) return EhNoite(horaCrua) ? horaCrua : MeiaNoite;
		return horaCrua;
	}

	/// <summary>
	/// QUAL NOITE É ESTA, contada desde a origem do tempo DAQUELE planeta. O corte fica no
	/// ANOITECER: das 20h de hoje às 6h de amanhã é a mesma noite e, portanto, a mesma lua.
	/// </summary>
	public static long NoiteDe(double diaLocal) => (long)Math.Floor(diaLocal + (1 - Anoitecer));

	/// <summary>
	/// O quanto a noite já andou, de 0 (anoitecendo) a 1 (amanhecendo). Fora da noite passa de 1
	/// ou fica negativo -- quem quer saber se é noite pergunta a <see cref="EhNoite"/>.
	/// </summary>
	public static double ProgressoDaNoite(double hora)
	{
		double desde = hora >= Anoitecer ? hora - Anoitecer : hora + (1 - Anoitecer);
		return desde / DuracaoDaNoite;
	}

	/// <summary>A fase (1 a 8) da lua deste planeta nesta noite dele. 0 quando não há lua.</summary>
	public static int FaseEm(RelogioDoPlaneta r, double diaLocal)
	{
		if (!r.Lua.Existe) return 0;
		long n = NoiteDe(diaLocal) + r.Lua.Defasagem;
		return (int)(((n % Fases) + Fases) % Fases) + 1;
	}

	/// <summary>
	/// QUANTO DO DISCO ESTÁ ACESO, de 0 (nova) a 1 (cheia).
	///
	/// A fase 1 é a nova e a 5 é a cheia, então a fase `f` corresponde a `(f-1)/8` de volta do
	/// ciclo -- e a fração acesa é a meia-onda do cosseno, que é a mesma conta que dá o desenho
	/// do disco no céu. Uma tabela de oito números cravados daria os mesmos valores e divergiria
	/// do desenho no dia em que alguém mexesse num dos dois.
	/// </summary>
	public static double AcesoNaFase(int fase) =>
		fase <= 0 ? 0 : (1 - Math.Cos(2 * Math.PI * (fase - 1) / Fases)) / 2;

	/// <summary>
	/// A ALTURA DA LUA: ela nasce ao anoitecer, cruza o céu e se põe ao amanhecer.
	///
	/// Meia onda de seno em vez de linha reta porque é o que faz a lua PARAR no alto em vez de
	/// atravessar a tela em velocidade constante -- e é no alto que ela fica a maior parte da
	/// noite, que é o que se vê olhando pro céu de verdade.
	/// </summary>
	public static double AlturaEm(double hora)
	{
		if (!EhNoite(hora)) return -1;
		return Math.Sin(Math.PI * Math.Clamp(ProgressoDaNoite(hora), 0, 1));
	}

	/// <summary>
	/// O CÉU INTEIRO num instante, pro planeta dado. É a porta única: todo o resto do jogo lê
	/// daqui, e ninguém mais precisa saber como o dia local se calcula.
	/// </summary>
	public static EstadoDoCeu De(RelogioDoPlaneta r, double segundosDoMundo)
	{
		double dia = r.DiaLocal(segundosDoMundo);
		double crua = HoraDe(dia);
		double hora = HoraVisivel(r, crua);
		int fase = FaseEm(r, dia);
		return new EstadoDoCeu
		{
			Hora = hora,
			HoraCrua = crua,
			Noite = EhNoite(hora),
			Fase = fase,
			Altura = fase == 0 ? -1 : AlturaEm(hora),
			Aceso = AcesoNaFase(fase),
			Tamanho = r.Lua.Tamanho,
		};
	}

	/// <summary>
	/// A FICHA DE CÉU DE UMA ZONA: quem tem dia, quem tem noite, quanto dura o dia e quem tem lua.
	///
	/// Resolve o tipo da zona: mapa próprio pergunta ao catálogo, mundo gerado pergunta à seed, e
	/// o espaço e os interiores não têm céu nenhum.
	///
	/// O ESPAÇO ANTES DO RESTO: a zona do espaço também é do tipo procedural (ela carrega a seed
	/// do universo), então cair no ramo do gerado a faria sortear um amanhecer pro vácuo.
	/// </summary>
	public static RelogioDoPlaneta RelogioDaZona(ZoneKey zona, CatalogoDePlanetas? catalogo)
	{
		// NO ESPAÇO NÃO AMANHECE. É o `HasNight=0 HasDay=0 HasMoon=0` da área `Space` do DM: sem
		// atmosfera não há céu que mude de cor, e o fundo de estrelas já é a imagem do lugar.
		if (Espaco.EhEspaco(zona)) return SemCeu;

		// A MENTE VEM ANTES DO INTERIOR GENÉRICO, e a ordem é o conserto: ela **é** um interior, então
		// a linha de baixo já respondia por ela -- com crepúsculo parado, que sobre chão branco dá
		// malva. Ver `RelogioDoPlaneta.LuzPropria`.
		if (DimensaoMental.EhAMente(zona)) return DimensaoBranca;

		// INTERIOR NÃO VÊ LUA -- é a regra que o DM escreve duas vezes: as áreas `Inside` nascem
		// com `HasMoon = 0` (`Weather.dm:76-80`) e o gatilho do Oozaru ainda confere o nome da
		// área antes de deixar o Saiyajin olhar pra cima (`Weather.dm:202`). Teto é teto.
		if (zona.Kind == ZoneKey.KindInterior) return SemCeu;

		if (zona.Kind == ZoneKey.KindProcedural) return DaSeed(zona.Seed);

		FichaDePlaneta? f = catalogo?.De(zona.Name);
		return new RelogioDoPlaneta
		{
			SegundosPorDia = f?.SegundosPorDia > 0 ? f.SegundosPorDia : SegundosPorDia,
			Defasagem = DefasagemDoNome(zona.Name),
			TemDia = f?.TemDia ?? true,
			TemNoite = f?.TemNoite ?? true,
			Lua = LuaDoPlaneta.DoNome(zona.Name, f?.TemLua ?? true),
		};
	}

	/// <summary>
	/// A DIMENSÃO MENTAL: sem céu **e sem penumbra** -- ver <see cref="RelogioDoPlaneta.LuzPropria"/>,
	/// que é onde a decisão inteira está escrita.
	/// </summary>
	public static RelogioDoPlaneta DimensaoBranca => new()
	{
		SegundosPorDia = SegundosPorDia,
		TemDia = false,
		TemNoite = false,
		Lua = LuaDoPlaneta.Nenhuma,
		LuzPropria = true,
	};

	/// <summary>Lugar sem céu: espaço e interiores. Crepúsculo parado e nenhuma lua.</summary>
	public static RelogioDoPlaneta SemCeu => new()
	{
		SegundosPorDia = SegundosPorDia,
		TemDia = false,
		TemNoite = false,
		Lua = LuaDoPlaneta.Nenhuma,
	};

	/// <summary>
	/// O RELÓGIO DE UM MUNDO SORTEADO -- rotação, defasagem e lua, tudo da seed.
	///
	/// A rotação varia de meio dia terrestre ao dobro dele. Um mundo com dia de 12 minutos tem
	/// duas noites no tempo em que a Terra tem uma, e é isso que faz "voltar pra Terra" ser uma
	/// mudança de ritmo e não só de cenário.
	/// </summary>
	public static RelogioDoPlaneta DaSeed(ulong seed)
	{
		ulong h = Espaco.Misturar(seed, Espaco.Hash64("dia"), 0);
		return new RelogioDoPlaneta
		{
			SegundosPorDia = SegundosPorDia * (0.5 + h % 150 / 100.0),
			Defasagem = (h >> 16) % 1000 / 1000.0,
			// SOL ETERNO É RARO E NOITE ETERNA É MAIS RARA AINDA. Os dois existem no DM (Namek
			// não anoitece; o Outro Mundo é `AlwaysDay`), então um mundo sorteado poder nascer
			// assim é coerente -- mas um planeta sem noite é um planeta onde o Oozaru nunca
			// acontece, e isso tem que ser exceção.
			TemDia = (h >> 32) % 100 >= 4,
			TemNoite = (h >> 40) % 100 >= 10,
			Lua = LuaDoPlaneta.DaSeed(seed),
		};
	}

	/// <summary>
	/// ONDE O MUNDO ESTÁ NO PRÓPRIO DIA quando o relógio global marca zero.
	///
	/// Sai do NOME porque precisa ser o mesmo número nas duas pontas sem viajar no fio, e porque
	/// um planeta não muda de nome. Sem isto todo mundo amanheceria junto, e "que horas são em
	/// Vegeta?" seria a mesma pergunta que "que horas são aqui?".
	/// </summary>
	public static double DefasagemDoNome(string nome) =>
		Espaco.Hash64(nome + "|dia") % 1000 / 1000.0;

	// =====================================================================
	// NOMES E CONTAS DE APOIO
	// =====================================================================
	/// <summary>Nome legível da hora, pro HUD ("amanhecer", "tarde", "noite").</summary>
	public static string NomeDaHora(double hora) => (hora - Math.Floor(hora)) switch
	{
		< Amanhecer => "madrugada",
		< 0.33 => "amanhecer",
		< 0.45 => "manha",
		< 0.62 => "meio-dia",
		< 0.72 => "tarde",
		< Anoitecer => "poente",
		_ => "noite",
	};

	public static string NomeDaFase(int fase) => fase switch
	{
		(int)FaseDaLua.Nova => "lua nova",
		(int)FaseDaLua.CrescenteFina => "crescente fina",
		(int)FaseDaLua.QuartoCrescente => "quarto crescente",
		(int)FaseDaLua.CrescenteGibosa => "crescente gibosa",
		(int)FaseDaLua.Cheia => "LUA CHEIA",
		(int)FaseDaLua.MinguanteGibosa => "minguante gibosa",
		(int)FaseDaLua.QuartoMinguante => "quarto minguante",
		(int)FaseDaLua.MinguanteFina => "minguante fina",
		_ => "sem lua",
	};

	/// <summary>Como este mundo trata o dia -- pro texto da ficha do planeta.</summary>
	public static string NomeDoCiclo(RelogioDoPlaneta r) =>
		!r.TemDia && !r.TemNoite ? "crepusculo eterno"
		: !r.TemNoite ? "sol eterno"
		: !r.TemDia ? "noite eterna"
		: $"dia de {r.MinutosPorDia:0.#} min";

	/// <summary>
	/// QUANTO FALTA PRA PRÓXIMA LUA CHEIA, em SEGUNDOS reais. Serve pro HUD e pra quem quiser
	/// planejar a noite -- saber que a cheia vem em três noites é o que transforma o ciclo em
	/// algo com que se joga, em vez de uma surpresa que cai na cabeça.
	///
	/// Devolve -1 quando não há lua ou quando o mundo não anoitece: num planeta de sol eterno a
	/// fase continua girando, mas a lua nunca sobe, e prometer uma cheia que não vai aparecer
	/// seria pior do que não dizer nada.
	/// </summary>
	public static double SegundosAteACheia(RelogioDoPlaneta r, double segundosDoMundo)
	{
		if (!r.Lua.Existe || !r.TemNoite) return -1;

		double dia = r.DiaLocal(segundosDoMundo);
		if (FaseEm(r, dia) == Cheia && EhNoite(HoraDe(dia))) return 0;

		// A NOITE DE ÍNDICE k COMEÇA EM `k - (1 - Anoitecer)`: é a mesma conta do `NoiteDe`, ao
		// contrário. Andar noite a noite em vez de contar fases resolve o caso de já ser dia
		// depois de uma cheia -- ali a fase AINDA é 5, mas a próxima cheia é daqui a oito noites.
		for (int n = 0; n <= Fases; n++)
		{
			double anoitece = NoiteDe(dia) + n - (1 - Anoitecer);
			if (anoitece < dia) continue;
			if (FaseEm(r, anoitece + 0.001) == Cheia) return (anoitece - dia) * r.SegundosPorDia;
		}
		return -1;
	}
}
