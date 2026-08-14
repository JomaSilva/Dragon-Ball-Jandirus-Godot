namespace Jandirus.Core.World;

/// <summary>
/// AS REGRAS DO VOO. Mora aqui, e so aqui -- o cliente preve com elas, o servidor valida com elas.
///
/// ============================ DUAS METADES DE PROCEDENCIA DIFERENTE ============================
/// A metade de BAIXO deste arquivo (custo de Ki, escada da skill, portoes do espaco) e PORTE
/// LITERAL do original: `Code/Modules/Skills/Misc/flying.dm`, `Stats/Stats.dm:411-427` e
/// `Skills/Skill Trees/Ki Trees/old/Mindo.dm:160-194`. As formulas estao copiadas com os numeros
/// que la estao, e cada uma cita a linha.
///
/// A metade de CIMA (altitude) **nao existe no original** -- o BYOND nao tem altura: voar la e um
/// punhado de flags no MESMO z, e o que `isflying` faz e sair da conta de colisao. O `Space_Flight`
/// tambem nao e altitude acumulada: e um verbo com portoes fixos que teleporta depois de 5 s.
///
/// Entao a altura e desenho novo, e esta anotada como tal. O que ela NAO faz e mudar as regras
/// portadas: quem paga o Ki, quanto paga e quando cai continuam sendo o que o DM dizia.
/// ==============================================================================================
/// </summary>
public static class Voo
{
	// ==================================================================================
	// PARTE 1 -- PORTE LITERAL DO DM
	// ==================================================================================

	/// <summary>
	/// `flightability` = 1 e o valor de QUEM NAO SABE VOAR (`flying.dm:64`, no esquecer da skill).
	///
	/// Nao e "1% de proficiencia": e a sentinela. O portao do `Space_Flight` testa
	/// `flightability != 1` (`Mindo.dm:162`) exatamente por isso -- ele nao quer saber se a
	/// proficiencia e alta, quer saber se ela FOI ATRIBUIDA alguma vez.
	/// </summary>
	public const double HabilidadeSemVoo = 1;

	/// <summary>Nivel 1 da skill: `savant.flightability = 25` (`flying.dm:73`).</summary>
	public const double HabilidadeNivel1 = 25;

	/// <summary>Nivel 2: `savant.flightability = 75` (`flying.dm:80`).</summary>
	public const double HabilidadeNivel2 = 75;

	/// <summary>O teto do treino em voo (`flying.dm:40`: sobe ate 100).</summary>
	public const double HabilidadeTeto = 100;

	/// <summary>
	/// O piso que o nivel da skill garante. A proficiencia REAL pode estar acima disso (ela sobe
	/// voando), entao isto e o valor de partida e nao o valor corrente.
	/// </summary>
	public static double HabilidadeDoNivel(int nivel) => nivel switch
	{
		>= 2 => HabilidadeNivel2,
		1 => HabilidadeNivel1,
		_ => HabilidadeSemVoo,
	};

	/// <summary>
	/// QUANTAS VOLTAS DO LACO DO DM CABEM NUM SEGUNDO.
	///
	/// O custo por tique do original nao serve de nada sem isto: o dreno de voo mora no `Stats()`
	/// (`Stats.dm:411-427`), que dorme `sleep(2)` (`:126` declara, `:511` dorme). `sleep(2)` sao
	/// **0,2 s** -- ver <see cref="TempoDoDm"/> --, logo o laco acorda **5** vezes por segundo.
	///
	/// AQUI ESTAVA 6,0, e o defeito era de unidade: eu dividia o `sleep` pelo `world.fps = 12` em
	/// vez de por 10. Com 6 o voo drenava 20% de Ki a mais do que o original cobra.
	///
	/// Todo custo "por tique" vira custo por segundo multiplicando aqui -- e e o que permite o tique
	/// do Godot rodar noutra cadencia sem mudar o quanto se gasta.
	/// </summary>
	public const double TiquesDoDmPorSegundo = TempoDoDm.TiquesPorSegundo / 2;

	/// <summary>
	/// O QUE CUSTA LEVANTAR VOO. `flying.dm:86`:
	/// <code>kiReq = 80/flightability + (450*flightspeed)/flightability</code>
	/// e logo abaixo `if(kiReq &lt; 1) kiReq = 0` -- quem tem proficiencia alta decola de graca.
	/// </summary>
	public static double CustoParaLigar(double habilidade, bool rapido)
	{
		double h = habilidade <= 0 ? HabilidadeSemVoo : habilidade;
		double k = 80 / h + 450 * (rapido ? 1 : 0) / h;
		return k < 1 ? 0 : k;
	}

	/// <summary>
	/// O QUE CUSTA CONTINUAR NO AR, por tique do DM. `Stats.dm:415`:
	/// <code>Ki -= max(35/flightability + (450*flightspeed)/flightability, 1)</code>
	/// O `max(...,1)` e o que impede o voo de virar gratuito na proficiencia 100.
	/// </summary>
	public static double CustoPorTiqueDoDm(double habilidade, bool rapido)
	{
		double h = habilidade <= 0 ? HabilidadeSemVoo : habilidade;
		return Math.Max(35 / h + 450 * (rapido ? 1 : 0) / h, 1);
	}

	/// <summary>O mesmo custo, na unidade que o tique do Godot usa.</summary>
	public static double CustoPorSegundo(double habilidade, bool rapido)
		=> CustoPorTiqueDoDm(habilidade, rapido) * TiquesDoDmPorSegundo;

	/// <summary>
	/// ABAIXO DISTO O CORPO CAI. `Stats.dm:413` -- o gasto so acontece `if(Ki>5)`; no `else` o
	/// original desliga o voo, toca o som de pouso e diz "You're exhausted.".
	/// </summary>
	public const double KiQueDerruba = 5;

	/// <summary>
	/// A PROFICIENCIA SOBE VOANDO, e o teto depende do nivel da skill.
	///
	/// `flying.dm:34` -- no nivel 1, so enquanto `flight` e ate 50.
	/// `flying.dm:40` -- no nivel 2+, so com `flight` E `flightspeed`, e ate 100.
	///
	/// O passo do original e `+= 0.1` por tique do `effector`, que roda na mesma cadencia do Stats.
	/// </summary>
	public const double GanhoPorTiqueDoDm = 0.1;

	/// <summary>Teto do treino em voo por nivel: 50 no nivel 1, 100 do 2 em diante.</summary>
	public static double TetoDoTreino(int nivel) => nivel >= 2 ? HabilidadeTeto : 50;

	/// <summary>
	/// Quanto a proficiencia sobe em <paramref name="segundos"/> de voo. Devolve o valor NOVO.
	///
	/// A condicao de cada nivel e a do original: no 1 basta estar voando; do 2 em diante so conta
	/// com o Superflight ligado -- treinar voo lento parou de render, e e o que a mensagem de
	/// subida de nivel avisa ("Seems like you wont get any more out of training normal flight").
	/// </summary>
	public static double SubirHabilidade(double habilidade, int nivel, bool rapido, double segundos)
	{
		if (nivel < 1) return habilidade;
		if (nivel >= 2 && !rapido) return habilidade;

		double teto = TetoDoTreino(nivel);
		if (habilidade >= teto) return habilidade;
		return Math.Min(teto, habilidade + GanhoPorTiqueDoDm * TiquesDoDmPorSegundo * segundos);
	}

	/// <summary>Portao de Ki do `Space_Flight` (`Mindo.dm:162`).</summary>
	public const double KiParaEspaco = 600;

	/// <summary>Portao de poder EXPRESSO do `Space_Flight` (`Mindo.dm:162`).</summary>
	public const double BpParaEspaco = 2000;

	/// <summary>
	/// Os tres portoes do `Space_Flight`, juntos: `Ki>=600 &amp;&amp; expressedBP>=2000 &amp;&amp; flightability!=1`.
	///
	/// O terceiro nao e redundante com os outros dois: e ele que exige ter APRENDIDO a voar. Sem
	/// ele, poder bruto sozinho levaria ao espaco.
	/// </summary>
	public static bool PodeRomperAAtmosfera(double ki, double bpExpresso, double habilidade)
		=> ki >= KiParaEspaco && bpExpresso >= BpParaEspaco && habilidade != HabilidadeSemVoo;

	// ==================================================================================
	// PARTE 2 -- ALTITUDE (camada NOVA; nao ha original pra copiar)
	// ==================================================================================

	/// <summary>
	/// O TETO, em pixels. 20 tiles.
	///
	/// Ele existe por dois motivos, e nenhum e estetico: e onde os portoes do `Space_Flight` sao
	/// consultados (chegar no teto E o gesto de tentar sair do planeta), e e o denominador de todo
	/// efeito por faixa -- borrao, zoom e alcance de vista sao fracoes DELE.
	/// </summary>
	public const float AlturaMaxima = 20f * ZoneCollision.TileSize;

	/// <summary>
	/// A ALTURA DE PAIRAR: onde o corpo para sozinho quando se liga o voo sem pedir mais nada.
	///
	/// E o `flight` do DM e nada alem dele -- alto o bastante pra passar por cima do cenario,
	/// baixo o bastante pra continuar sendo uma cena de chao. Quem quiser mais alto pede.
	/// </summary>
	public const float AlturaDePairar = 1.5f * ZoneCollision.TileSize;

	/// <summary>
	/// A PARTIR DAQUI O CORPO ATRAVESSA O CENARIO -- e o `isflying` do original.
	///
	/// ============================ POR QUE UMA FAIXA SO, E NAO DUAS ============================
	/// A especificacao pedia duas: acima de N para de colidir com cenario, acima de M para de
	/// colidir com tudo. No DM ha mesmo essa diferenca -- `barrier.dm:16` deixa passar so o obj de
	/// `tall == 0`, entao barreira ALTA continua barrando quem voa.
	///
	/// So que aqui nao ha esse dado. A colisao da zona e UM bitset de 1 bit por celula
	/// (<see cref="ZoneCollision"/>): nao ha altura por tile, nao ha `tall`, e as construcoes de
	/// jogador tambem deixam passar quem voa (`buildturfs.dm:880`). Duas faixas seriam o MESMO
	/// numero com dois nomes -- e um limite que nunca separa nada e indistinguivel de limite nenhum.
	///
	/// Fica uma faixa, honesta. Se um dia o conversor exportar altura por tile, e aqui que a
	/// segunda entra.
	/// ========================================================================================
	/// </summary>
	public const float AlturaQueAtravessa = 1f * ZoneCollision.TileSize;

	/// <summary>Subir custa tempo: 6 tiles por segundo.</summary>
	public const float SubidaPorSegundo = 6f * ZoneCollision.TileSize;

	/// <summary>Descer e mais rapido que subir -- a gravidade ajuda.</summary>
	public const float DescidaPorSegundo = 9f * ZoneCollision.TileSize;

	/// <summary>
	/// A QUEDA de quem perde o voo no ar (Ki no fim, nocaute, morte). Mais rapida que descer de
	/// proposito: nao e uma manobra, e um tombo.
	/// </summary>
	public const float QuedaPorSegundo = 16f * ZoneCollision.TileSize;

	/// <summary>Este corpo passa por cima de parede agora?</summary>
	public static bool AtravessaCenario(float altura) => altura >= AlturaQueAtravessa;

	/// <summary>De 0 (chao) a 1 (teto). E o parametro de todo efeito visual de altura.</summary>
	public static float Fracao(float altura)
		=> Math.Clamp(altura / AlturaMaxima, 0f, 1f);

	/// <summary>
	/// QUANTO O SPRITE SOBE NA TELA por pixel de altitude.
	///
	/// Nao e 1:1 de proposito. Num jogo visto de cima, desenhar o corpo 640 px acima da posicao
	/// dele o jogaria pra fora da tela e desgrudaria o personagem do lugar onde ele de fato esta --
	/// e e ESSA posicao que a luta, a colisao e a sombra usam. Um quarto da altura da leitura de
	/// "estou alto" sem mentir sobre onde o corpo esta.
	/// </summary>
	public const float EscalaNaTela = 0.25f;

	/// <summary>
	/// ESTE CORPO PROJETA SOMBRA NO CHAO? -- a pergunta tem UM dono, e por causa do nado.
	///
	/// ============================ POR QUE VIROU FUNCAO, E EM QUE MOMENTO ============================
	/// O limiar morava dentro do `Client/SombraDeVoo.Altura` (um `value > 0.5f` no setter), e ali ele
	/// era invisivel pra qualquer medicao que nao abrisse o Godot. Isso bastava enquanto so o VOO
	/// mexia em altura -- ai "tem sombra" e "esta voando" eram a mesma frase.
	///
	/// O nado desfez isso. O dono pediu, na letra, *"uma animacao PARECIDA COM A DO FLY, porem N CRIA
	/// A SOMBRA em baixo e NEM FICA MAIS ALTO"*: existe agora uma pose de voo com altura ZERO, e a
	/// unica coisa que separa o nado do voo rasante na tela e esta resposta. Uma regra que separa duas
	/// poses e regra de jogo, e regra de jogo mora no Core, onde da pra prova-la sem engine.
	///
	/// O MEIO PIXEL NAO E FOLGA ARBITRARIA: a altura chega ao cliente como BYTE (ver
	/// <see cref="ParaByte"/>, ~2,5 px por degrau) e volta interpolada, entao "zero" no fio pode
	/// chegar como um epsilon. Meio pixel esta abaixo do primeiro degrau que o fio consegue
	/// representar -- ou seja, nada que o servidor mande como zero acende a sombra.
	/// ============================================================================================
	/// </summary>
	public static bool TemSombra(float altura) => altura > 0.5f;

	/// <summary>
	/// A ALTITUDE NO FIO: um byte.
	///
	/// Quatro bytes de float por corpo por snapshot pra uma informacao que so alimenta desenho
	/// seria caro no lugar errado. Um byte da 20/255 de tile de resolucao (~2,5 px) -- mais fino
	/// que o olho ve num movimento de 6 tiles por segundo.
	/// </summary>
	public static byte ParaByte(float altura)
		=> (byte)Math.Clamp((int)MathF.Round(Fracao(altura) * 255f), 0, 255);

	/// <summary>O caminho de volta.</summary>
	public static float DeByte(byte b) => b / 255f * AlturaMaxima;

	// ==================================================================================
	// PARTE 3 -- NIVEIS DE ALTURA (quem alcanca quem, quem enxerga quem)
	// ==================================================================================

	/// <summary>
	/// QUANTAS FAIXAS DE VOO existem acima do chao.
	///
	/// Altura continua nao serve pra decidir soco: dois corpos a 210 e 214 px estariam "em alturas
	/// diferentes" por quatro pixels que ninguem ve, e a luta viraria loteria. Faixas dao ao jogador
	/// uma pergunta que ele consegue responder olhando -- "estamos no mesmo andar?".
	/// </summary>
	public const int Andares = 3;

	/// <summary>Em que andar este corpo esta. 0 = chao.</summary>
	public static int Andar(float altura)
	{
		if (altura <= 0f) return 0;
		int n = (int)MathF.Ceiling(altura / (AlturaMaxima / Andares));
		return Math.Clamp(n, 1, Andares);
	}

	/// <summary>
	/// <paramref name="deQuem"/> CONSEGUE ENCOSTAR EM <paramref name="emQuem"/>?
	///
	/// ============================ AS REGRAS, COMO O DONO AS DITOU ============================
	///   * mesmo andar  -> sim (inclusive os dois no chao)
	///   * andar 1 -> chao: SIM. Quem paira rasante ainda alcanca quem esta em pe embaixo.
	///   * chao -> andar 1: NAO. E o "porem nao o contrario": ficar rasante e vantagem, e essa
	///     vantagem e o que faz voar valer a pena numa briga.
	///   * andar 2+ -> chao: NAO. De alto demais nao se alcanca o chao.
	///   * dois voando: so no MESMO andar.
	/// ========================================================================================
	///
	/// A regra e ASSIMETRICA de proposito, e isso e raro o bastante pra merecer aviso: nao se pode
	/// escrever `PodeAcertar(a,b) == PodeAcertar(b,a)` em lugar nenhum. Quem paira no andar 1 bate
	/// em quem esta no chao sem poder levar de volta.
	/// </summary>
	public static bool PodeAcertar(int andarDeQuem, int andarDeQuemLeva)
	{
		if (andarDeQuem == andarDeQuemLeva) return true;
		return andarDeQuem == 1 && andarDeQuemLeva == 0;
	}

	/// <summary>
	/// UM ENXERGA O OUTRO? Um andar de diferenca, no maximo.
	///
	/// "Se a pessoa estiver voando muito alto, quem esta no chao nem consegue ver ela -- so se voar
	/// alto tambem." Um andar de folga e o que mantem coerente o soco de cima pra baixo: quem paira
	/// rasante PODE bater em quem esta no chao, e bater em alguem invisivel seria pior que injusto,
	/// seria incompreensivel.
	/// </summary>
	public static bool Enxerga(int andarA, int andarB) => Math.Abs(andarA - andarB) <= 1;
}
