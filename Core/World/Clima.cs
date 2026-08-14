namespace Jandirus.Core.World;

/// <summary>
/// OS TIPOS DE CLIMA.
///
/// Os dez primeiros são o `possibleWeatherTypes` do original
/// (`Modules/Turfs/Weather.dm:25`), na mesma semântica: lá são STRINGS numa lista por área, aqui
/// são um enum porque o que viaja no fio e o que o desenho consulta não pode ser texto solto.
///
/// <see cref="Nublado"/> NÃO VEM DO DM -- lá o céu ou está limpo ou tem um clima com sprite
/// próprio, e não existe um estado intermediário de "só ficou cinza". Foi pedido do dono, e é o
/// que dá ao clima um degrau leve: nem sol, nem chuva.
/// </summary>
public enum TipoDeClima : byte
{
	Limpo = 0,
	Nublado = 1,
	Chuva = 2,
	Tempestade = 3,
	Neve = 4,
	Nevasca = 5,
	Neblina = 6,
	Fumaca = 7,
	Areia = 8,
	ChuvaDeSangue = 9,
	ChuvaDeNamek = 10,

	/// <summary>
	/// O CÉU DE UM PLANETA MORRENDO -- o `currentWeather = "Destruction"` do original
	/// (`Area_Death.dm:95`), que lá vira `icon_state = "Rising Rocks"` (`Weather.dm:115-116`).
	///
	/// Ele **não é sorteável**: nenhum planeta o tem em `Permitidos`, e nenhum bioma o oferece. Ele
	/// só existe forçado, e quem força é a destruição de planeta (`GameServer.Destruicao.cs`).
	/// Entrou por último no enum de propósito -- o byte viaja no fio e mudar a numeração dos dez
	/// primeiros quebraria o cliente antigo em silêncio.
	/// </summary>
	Destruicao = 11,
}

/// <summary>O que este planeta pode ter no céu. É o `allowedWeatherTypes` de cada área do DM.</summary>
public readonly struct ClimaDoPlaneta
{
	/// <summary>Os tipos sorteáveis aqui. Vazio = céu sempre limpo.</summary>
	public TipoDeClima[] Permitidos { get; init; }

	/// <summary>O `HasWeather` da área. Falso = nada acontece no céu deste lugar.</summary>
	public bool Existe { get; init; }

	/// <summary>Quanto o clima daqui é ativo, de 0 a 1. Sai da seed nos mundos gerados.</summary>
	public double Frequencia { get; init; }

	public static readonly ClimaDoPlaneta Nenhum = new() { Permitidos = [], Existe = false };

	/// <summary>
	/// O clima de um mundo SORTEADO, tirado do bioma.
	///
	/// NÃO VEM DO DM: lá o espaço procedural inteiro é `HasWeather = 0`
	/// (`Modules/Tech/ProceduralSpace.dm:264`), ou seja, nenhum mundo gerado jamais teve clima. É
	/// escolha nossa, e o bioma é o que decide -- um mundo gelado nevar e um deserto ter tempestade
	/// de areia é a mesma leitura que já governa o terreno dele.
	/// </summary>
	public static ClimaDoPlaneta DoBioma(BiomaDeTerreno bioma, ulong seed)
	{
		TipoDeClima[] tipos = bioma switch
		{
			BiomaDeTerreno.Jardim => [TipoDeClima.Chuva, TipoDeClima.Tempestade, TipoDeClima.Neblina, TipoDeClima.Nublado],
			BiomaDeTerreno.Gelado => [TipoDeClima.Neve, TipoDeClima.Nevasca, TipoDeClima.Neblina, TipoDeClima.Nublado],
			BiomaDeTerreno.Deserto => [TipoDeClima.Areia, TipoDeClima.Fumaca, TipoDeClima.Nublado],
			BiomaDeTerreno.Vulcanico => [TipoDeClima.Fumaca, TipoDeClima.Tempestade, TipoDeClima.ChuvaDeSangue],
			BiomaDeTerreno.Morto => [TipoDeClima.Fumaca, TipoDeClima.Neblina],
			_ => [TipoDeClima.Chuva, TipoDeClima.Neblina, TipoDeClima.Nublado],
		};

		ulong h = Espaco.Misturar(seed, Espaco.Hash64("clima"), 0);
		return new ClimaDoPlaneta
		{
			Permitidos = tipos,
			Existe = h % 100 < 80,
			// de um mundo quase sempre limpo a um que vive fechado
			Frequencia = 0.25 + (h >> 12) % 100 / 100.0 * 0.5,
		};
	}
}

/// <summary>
/// UM CLIMA FORÇADO -- alguém mandou o céu mudar, e ele muda.
///
/// ============================ POR QUE ISTO EXISTE ============================
/// O clima natural é função pura do tempo (ver <see cref="Clima.Natural"/>), e é justamente por
/// isso que ele não pode ser a história inteira: função pura não obedece a ninguém. Uma
/// transformação que escurece o céu, um Deus da Destruição chegando, o ritual de magia que o DM
/// já tem (`e_change_weather`, `Rituals_Manipulation.dm:271-279`) -- todos precisam de um jeito de
/// dizer "agora chove aqui", e isso é ESTADO, não conta.
///
/// É o único pedaço do céu que viaja pelo fio (`S2C.Clima`). Tudo o mais se deriva.
/// ==============================================================================
/// </summary>
public readonly struct ClimaForcado
{
	public TipoDeClima Tipo { get; init; }

	/// <summary>Em que instante do mundo isto acaba (segundos). Zero = não há nada forçado.</summary>
	public double Ate { get; init; }

	/// <summary>Quanto tempo dura no total -- serve pra calcular a entrada e a saída suaves.</summary>
	public double Duracao { get; init; }

	/// <summary>Força máxima, de 0 a 1. Um SSJ2 não fecha o céu como um SSJ3.</summary>
	public double Forca { get; init; }

	/// <summary>Quem mandou (pro chat e pro log). Não viaja no fio.</summary>
	public string Motivo { get; init; }

	public bool Vivo(double agora) => Ate > agora;
}

/// <summary>O céu meteorológico de um lugar num instante.</summary>
public readonly struct EstadoDoClima
{
	public TipoDeClima Tipo { get; init; }

	/// <summary>De 0 (acabando/começando) a 1 (no auge). É o que dá entrada e saída suaves.</summary>
	public double Forca { get; init; }

	/// <summary>Veio de <see cref="ClimaForcado"/> e não do sorteio natural.</summary>
	public bool Forcado { get; init; }

	public bool Ativo => Tipo != TipoDeClima.Limpo && Forca > 0.001;

	/// <summary>
	/// O QUANTO ESTE CÉU ESTÁ FECHADO, de 0 a 1 -- já com a força aplicada.
	///
	/// É o número que apaga a lua e rouba o luar: nuvem carregada não deixa ver lua cheia. Fica
	/// disponível pro servidor (e não só pro desenho) porque é a pergunta que o Oozaru vai fazer.
	/// </summary>
	public double Encobre => Clima.EncobreNoAuge(Tipo) * Forca;

	/// <summary>O quanto este clima mexe na luz, de 0 a 1 -- já com a força aplicada. Só pra mostrar.</summary>
	public double Cinza => Clima.CinzaNoAuge(Tipo) * Forca;

	/// <summary>
	/// A COR DO AR AGORA: o multiplicador que vai no ambiente, já interpolado pela força.
	///
	/// Força 0 devolve branco (ar limpo), força 1 devolve a cor cheia do tipo. É o que faz o
	/// clima entrar e sair da iluminação junto com a rampa, sem um segundo relógio.
	/// </summary>
	public (double R, double G, double B) Ar
	{
		get
		{
			(double r, double g, double b) = Clima.CorDoAr(Tipo);
			double f = Math.Clamp(Forca, 0, 1);
			return (1 + (r - 1) * f, 1 + (g - 1) * f, 1 + (b - 1) * f);
		}
	}
}

/// <summary>
/// O CLIMA, COMO REGRA PURA -- e um único ponto de estado pra quem quiser mandar no céu.
///
/// ============================ COMO ISTO NÃO CUSTA REDE ============================
/// O clima natural é função pura de (ficha do planeta, tempo do mundo). O tempo do mundo já é
/// sincronizado pelo <see cref="Ceu"/> desde o login, então servidor e cliente chegam à MESMA
/// chuva sem trocar um byte -- é a mesma jogada da lua, do terreno e do céu de estrelas.
///
/// O que viaja é só o clima FORÇADO (<see cref="ClimaForcado"/>), porque esse não se deriva de
/// nada: alguém decidiu.
/// ===================================================================================
///
/// O CICLO NÃO É O DO DM, e vale dizer por quê. Lá o clima liga e desliga por `prob(25)` a cada
/// 500-700 s (`Weather.dm:43-50`), o que dá cerca de 40 MINUTOS em média em cada estado -- num
/// jogo cujo dia inteiro dura 24 minutos, isso é meio dia de chuva ininterrupta. Aqui o tempo é
/// cortado em blocos fixos e cada bloco sorteia o próprio céu, o que dá a mesma variedade num
/// ritmo que cabe numa sessão. A LISTA de climas de cada planeta, essa sim, é a do DM.
/// </summary>
public static class Clima
{
	/// <summary>
	/// Quanto dura um bloco de clima, em segundos reais.
	///
	/// Seis minutos: quatro blocos por dia terrestre. Curto o bastante pra alguém ver o céu
	/// mudar numa sessão, longo o bastante pra a chuva não virar um piscar. É tempo REAL e não
	/// fração do dia local de propósito -- num planeta de dia curto (a Big Gete Star gira em 12
	/// min) a fração daria blocos de três minutos, e o céu ficaria estroboscópico.
	/// </summary>
	public const double SegundosPorBloco = 6 * 60;

	/// <summary>Quanto tempo o clima leva pra entrar e pra sair, em segundos.</summary>
	public const double Transicao = 45;

	/// <summary>Fatia do bloco em que o céu fica limpo, quando o planeta tem clima normal.</summary>
	public const double ChanceDeLimpo = 0.5;

	// =====================================================================
	// O QUE CADA CLIMA É
	// =====================================================================
	/// <summary>O quanto este clima fecha o céu no auge (apaga a lua, rouba o luar).</summary>
	public static double EncobreNoAuge(TipoDeClima t) => t switch
	{
		TipoDeClima.Nublado => 0.70,
		TipoDeClima.Chuva or TipoDeClima.ChuvaDeSangue or TipoDeClima.ChuvaDeNamek => 0.80,
		TipoDeClima.Tempestade => 0.95,
		TipoDeClima.Neve => 0.65,
		TipoDeClima.Nevasca => 0.95,
		TipoDeClima.Neblina => 0.55,
		TipoDeClima.Fumaca => 0.75,
		TipoDeClima.Areia => 0.85,

		// O CÉU DA DESTRUIÇÃO FECHA MAIS QUE A TEMPESTADE. É poeira de planeta subindo, e o
		// original o usa junto com a noite permanente (`Weather.dm:166-167`): quase nada de luz
		// chega ao chão.
		TipoDeClima.Destruicao => 0.98,

		_ => 0,
	};

	/// <summary>
	/// ============================ A COR DO AR ============================
	/// O que este clima faz com a LUZ, como um multiplicador (r, g, b) aplicado ao ambiente.
	/// Branco = ar limpo, não muda nada.
	///
	/// POR QUE MULTIPLICAR E NÃO MISTURAR: misturar a cena com uma cor comprime tudo em direção a
	/// ela -- o preto sobe, o branco desce, e o personagem perde contraste contra o chão. É a
	/// aparência de "meio transparente" que reapareceu três vezes aqui. Multiplicar ESCALA: tira
	/// (ou põe) luz preservando as relações, que é o que ar carregado de fato faz.
	///
	/// ABAIXO DE 1 TIRA LUZ, ACIMA DE 1 PÕE. As duas famílias saem naturalmente disso, sem precisar
	/// de dois caminhos no código:
	///   * nuvem, chuva e tempestade TAPAM o sol -- valores baixos, mundo escuro;
	///   * nevasca e neblina ESPALHAM a luz -- valores acima de 1, mundo claro e chapado, que é o
	///     que um whiteout é de verdade;
	///   * a areia tinge de ocre, e a chuva de sangue de vermelho, porque o ar tem cor.
	///
	/// É AQUI QUE O CLIMA APARECE, e não no véu. O véu (`ClimaNaTela`) ficou só com a textura de
	/// nuvem se mexendo, que é o que só ele sabe fazer; o peso do tempo mora nesta tabela.
	/// =====================================================================
	/// </summary>
	public static (double R, double G, double B) CorDoAr(TipoDeClima t) => t switch
	{
		TipoDeClima.Nublado => (0.60, 0.62, 0.68),
		TipoDeClima.Chuva => (0.50, 0.54, 0.64),
		TipoDeClima.Tempestade => (0.32, 0.35, 0.44),
		TipoDeClima.Neve => (0.86, 0.89, 0.96),

		// ============================ NEVASCA É TEMPESTADE, NÃO É NEVE FORTE ============================
		// Ela chegou a clarear (1,30), pelo argumento de que neve no ar espalha luz. O argumento
		// vale pra uma NEVOA de neve, não pra uma nevasca: nevasca é um temporal, e temporal tem a
		// mesma nuvem carregada em cima que a tempestade de chuva. Deixá-la mais clara que a NEVE
		// -- que é o tempo mais fraco da mesma família -- invertia a escala inteira: o pior tempo
		// do planeta era o mais alegre da tela.
		//
		// Fica na escuridão da tempestade, só que puxada pro frio. É o que separa as duas sem
		// desfazer a ordem: nevasca é tão pesada quanto o temporal, e mais azul.
		// ================================================================================================
		TipoDeClima.Nevasca => (0.33, 0.38, 0.48),

		// A NEBLINA CONTINUA CLAREANDO, e aqui o argumento vale: névoa fina é justamente a que
		// espalha o sol em vez de tapá-lo. É o único tempo que deixa o mundo mais claro.
		TipoDeClima.Neblina => (1.12, 1.15, 1.20),

		TipoDeClima.Fumaca => (0.52, 0.49, 0.44),
		TipoDeClima.Areia => (1.10, 0.88, 0.55),
		TipoDeClima.ChuvaDeSangue => (0.60, 0.38, 0.38),
		TipoDeClima.ChuvaDeNamek => (0.52, 0.68, 0.56),

		// ============================ A COR DE UM MUNDO ACABANDO ============================
		// Escura como a tempestade e PUXADA PRO VERMELHO -- é a poeira incandescente do
		// `"Rising Rocks"`, não uma nuvem de chuva. O vermelho fica acima dos outros dois canais
		// (0,62 contra 0,30/0,24) porque o que se quer é que a pele e a roupa de todo mundo
		// fiquem alaranjadas, e não que a cena vire cinza: o jogador tem que reconhecer o céu do
		// fim do mundo sem precisar ler o chat.
		// ================================================================================
		TipoDeClima.Destruicao => (0.62, 0.30, 0.24),

		_ => (1, 1, 1),
	};

	/// <summary>
	/// O QUANTO ESTE CLIMA MEXE NA LUZ, de 0 a 1 -- o desvio da cor do ar em relação ao branco.
	///
	/// Serve pra MOSTRAR (o painel de admin, a bancada); quem faz a conta de verdade é a
	/// <see cref="CorDoAr"/>. Um número só não daria conta: tempestade e nevasca mexem
	/// igualmente na luz, uma tirando e a outra pondo.
	/// </summary>
	public static double CinzaNoAuge(TipoDeClima t)
	{
		(double r, double g, double b) = CorDoAr(t);
		return Math.Clamp(Math.Max(Math.Abs(1 - r), Math.Max(Math.Abs(1 - g), Math.Abs(1 - b))), 0, 1);
	}

	/// <summary>
	/// Este clima solta raio? A tempestade -- é o `if("Storm")` do `Weather.dm:245` -- e a
	/// DESTRUIÇÃO, que no original acende relâmpagos pelo chão a cada volta do laço
	/// (`createLightningmisc`, `Area_Death.dm:124-126`). O port não tem esse efeito de chão; o raio
	/// do céu é o que sobra dele, e é de graça porque o desenho já existe.
	/// </summary>
	public static bool TemRaio(TipoDeClima t) =>
		t is TipoDeClima.Tempestade or TipoDeClima.Destruicao;

	public static string Nome(TipoDeClima t) => t switch
	{
		TipoDeClima.Limpo => "ceu limpo",
		TipoDeClima.Nublado => "nublado",
		TipoDeClima.Chuva => "chuva",
		TipoDeClima.Tempestade => "tempestade",
		TipoDeClima.Neve => "neve",
		TipoDeClima.Nevasca => "nevasca",
		TipoDeClima.Neblina => "neblina",
		TipoDeClima.Fumaca => "fumaca",
		TipoDeClima.Areia => "tempestade de areia",
		TipoDeClima.ChuvaDeSangue => "chuva de sangue",
		TipoDeClima.ChuvaDeNamek => "chuva de Namek",
		TipoDeClima.Destruicao => "o ceu se despedacando",
		_ => "?",
	};

	/// <summary>
	/// A ponte com as STRINGS do DM. É assim que o `allowedWeatherTypes` de cada área vira enum.
	///
	/// "Rain", "Storm", "Blood Rain"... os nomes vêm literais do original; um que não caia na
	/// tabela vira <see cref="TipoDeClima.Limpo"/> e some -- o que é melhor do que virar chuva
	/// por engano num planeta que nunca teve chuva.
	/// </summary>
	public static TipoDeClima DoNomeDoDm(string s) => s.Trim().ToLowerInvariant() switch
	{
		"rain" => TipoDeClima.Chuva,
		"storm" => TipoDeClima.Tempestade,
		"snow" => TipoDeClima.Neve,
		"blizzard" => TipoDeClima.Nevasca,
		"fog" => TipoDeClima.Neblina,
		"smog" => TipoDeClima.Fumaca,
		"sandstorm" => TipoDeClima.Areia,
		"blood rain" => TipoDeClima.ChuvaDeSangue,
		"namek rain" => TipoDeClima.ChuvaDeNamek,
		"nublado" or "overcast" => TipoDeClima.Nublado,

		// O CLIMA DA MORTE DE PLANETA. Ele nunca aparece num `allowedWeatherTypes` -- o DM o
		// ESCREVE em `currentWeather` de dentro do `DestroyPlanet` (`Area_Death.dm:95`). Está aqui
		// mesmo assim porque esta tabela é a ponte com as strings do original, e o dia em que
		// alguém extrair aquela linha e ela cair no `_ => Limpo`, o céu do fim do mundo sumiria
		// calado -- a armadilha 5 da PARTE 3.
		"destruction" => TipoDeClima.Destruicao,

		_ => TipoDeClima.Limpo,
	};

	// =====================================================================
	// O CLIMA NATURAL
	// =====================================================================
	/// <summary>
	/// QUE CLIMA ESTE BLOCO DE TEMPO SORTEOU. Função pura -- não há sorteio em runtime e não há
	/// nada guardado, então as duas pontas chegam ao mesmo céu.
	/// </summary>
	public static TipoDeClima TipoDoBloco(ClimaDoPlaneta c, long bloco, ulong sal)
	{
		if (!c.Existe || c.Permitidos.Length == 0) return TipoDeClima.Limpo;

		ulong h = Espaco.Misturar(sal, (ulong)bloco, Espaco.Hash64("bloco"));
		double limpo = 1 - Math.Clamp(c.Frequencia, 0, 1);
		if (h % 1000 / 1000.0 < limpo) return TipoDeClima.Limpo;

		return c.Permitidos[(int)((h >> 20) % (ulong)c.Permitidos.Length)];
	}

	/// <summary>
	/// O CLIMA NATURAL AGORA.
	///
	/// A FORÇA VEM DAS BORDAS DO BLOCO, e é o que impede o corte seco: a chuva entra em 45 s e
	/// sai em 45 s. Quando o bloco seguinte sorteia o MESMO clima ela não chega a diminuir -- sem
	/// essa checagem, uma tempestade de doze minutos daria uma trégua no meio por motivo nenhum.
	/// </summary>
	public static EstadoDoClima Natural(ClimaDoPlaneta c, double segundos, ulong sal)
	{
		long bloco = (long)Math.Floor(segundos / SegundosPorBloco);
		TipoDeClima tipo = TipoDoBloco(c, bloco, sal);
		if (tipo == TipoDeClima.Limpo) return default;

		double dentro = segundos - bloco * SegundosPorBloco;
		double entrada = TipoDoBloco(c, bloco - 1, sal) == tipo ? 1 : Math.Clamp(dentro / Transicao, 0, 1);
		double saida = TipoDoBloco(c, bloco + 1, sal) == tipo
			? 1
			: Math.Clamp((SegundosPorBloco - dentro) / Transicao, 0, 1);

		return new EstadoDoClima { Tipo = tipo, Forca = Math.Min(entrada, saida) };
	}

	/// <summary>
	/// O CÉU METEOROLÓGICO DE VERDADE: o forçado vence o natural enquanto durar.
	///
	/// O forçado também entra e sai suave, mas ENTRA MUITO MAIS RÁPIDO -- um SSJ3 não escurece o
	/// céu em 45 segundos, ele escurece de uma vez; o que demora é o céu se recompor depois.
	/// </summary>
	public static EstadoDoClima De(ClimaDoPlaneta c, double segundos, ulong sal, ClimaForcado forcado)
	{
		if (!forcado.Vivo(segundos)) return Natural(c, segundos, sal);

		double falta = forcado.Ate - segundos;
		double decorrido = forcado.Duracao - falta;
		const double entradaRapida = 1.2;

		double f = Math.Min(
			Math.Clamp(decorrido / entradaRapida, 0, 1),
			Math.Clamp(falta / Transicao, 0, 1));

		return new EstadoDoClima
		{
			Tipo = forcado.Tipo,
			Forca = f * Math.Clamp(forcado.Forca, 0, 1),
			Forcado = true,
		};
	}

	/// <summary>
	/// A FICHA DE CLIMA DE UMA ZONA. Mesma resolução do <see cref="Ceu.RelogioDaZona"/>: mapa
	/// próprio pergunta ao catálogo, mundo gerado pergunta ao bioma, e espaço e interior não têm
	/// céu nenhum.
	/// </summary>
	public static ClimaDoPlaneta DaZona(ZoneKey zona, CatalogoDePlanetas? catalogo)
	{
		if (Espaco.EhEspaco(zona) || zona.Kind == ZoneKey.KindInterior) return ClimaDoPlaneta.Nenhum;

		if (zona.Kind == ZoneKey.KindProcedural)
			return ClimaDoPlaneta.DoBioma(MundoProcedural.DaSeed(zona.Seed, zona.Name).Bioma, zona.Seed);

		FichaDePlaneta? f = catalogo?.De(zona.Name);
		if (f == null) return ClimaDoPlaneta.Nenhum;

		var tipos = new List<TipoDeClima>();
		foreach (string s in f.Climas)
			if (DoNomeDoDm(s) is var t && t != TipoDeClima.Limpo && !tipos.Contains(t)) tipos.Add(t);

		// NUBLADO ENTRA EM TODO MUNDO QUE JÁ TEM CLIMA. É a adição do dono, e o degrau leve que o
		// DM não tinha: sem ele um planeta só conhece dois estados, sol aberto e temporal.
		if (tipos.Count > 0 && !tipos.Contains(TipoDeClima.Nublado)) tipos.Add(TipoDeClima.Nublado);

		return new ClimaDoPlaneta
		{
			Permitidos = [.. tipos],
			Existe = f.TemClima && tipos.Count > 0,
			Frequencia = 1 - ChanceDeLimpo,
		};
	}

	/// <summary>
	/// O SAL DO SORTEIO: o que faz o céu de Vegeta não ser o mesmo céu da Terra no mesmo minuto.
	///
	/// Sai do nome pelo mesmo motivo da defasagem da lua -- precisa ser igual nas duas pontas sem
	/// viajar no fio, e planeta não muda de nome.
	/// </summary>
	public static ulong SalDaZona(ZoneKey zona) => Espaco.Hash64(zona.Name + "|clima") ^ zona.Seed;
}
