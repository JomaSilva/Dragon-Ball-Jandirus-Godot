using Jandirus.Core.World;

namespace Jandirus.Core.Forms;

/// <summary>
/// O LIMIAR DE CADA FORMA E PROPRIO DE CADA PERSONAGEM.
///
/// No BYOND os `*at` ("ascends at") sao `mob/var` com um valor de fabrica -- e o
/// `special_info()` do proto racial reescreve TODOS eles no nascimento, multiplicando o valor
/// de fabrica por um `rand()` (`statsaiyan.dm:45-77`) ou trocando por uma faixa absoluta
/// (`statheran.dm:26-42`). Duas coisas se perdem quando isso vira constante:
///
/// 1. **A FAIXA E CARACTERIZACAO, NAO RUIDO.** O Elite acende o SSJ1 MAIS TARDE (x1,1..1,4) e o
///    SSJ2 MAIS CEDO (x0,9..1,2); o Low-Class faz o contrario (statsaiyan.dm:58-64). Nao da pra
///    tirar a media disso sem apagar a diferenca entre as duas classes: o Elite e quem custa a
///    virar Super Saiyajin e depois dispara, o Low-Class e quem vira cedo e empaca. O Legendary
///    nem aparece no meio -- ele nao mexe em ssjat/ssj2at porque a escada dele e outra
///    (restssjat/unrestssjat/lssjat, statsaiyan.dm:65-70).
///
/// 2. **DUAS PESSOAS DA MESMA CLASSE NAO SAO A MESMA PESSOA.** Dentro do Elite ainda ha 4
///    resultados possiveis (rand(11,14) e DISCRETO: 1,1 / 1,2 / 1,3 / 1,4). E o que faz "quando
///    eu viro" ser uma pergunta sobre o SEU personagem e nao sobre a tabela do jogo.
///
/// TRES INVARIANTES, e cada uma existe porque quebra-la quebra o jogo:
///
///  * **SORTEIO UNICO, NO NASCIMENTO.** Um limiar re-sorteado no login e um jogador que virou
///    SSJ ontem e hoje esta abaixo da porta -- destransformado pra sempre, sem nada na tela
///    dizendo por que. Por isso <see cref="Rolado"/>: quem ja rolou nunca rola de novo.
///  * **PERSISTE.** Vai pro save junto do personagem (ver o cabecalho de <see cref="Semente"/>).
///  * **DETERMINISTICO A PARTIR DA SEMENTE.** Nada aqui le um `Random` global. Cada limiar sai
///    de um gerador proprio derivado de (semente do personagem + nome do campo), entao o mesmo
///    personagem produz os mesmos numeros em qualquer maquina, e ACRESCENTAR um limiar novo
///    depois nao desloca os que ja existiam.
/// </summary>
public sealed class LimiaresPessoais
{
	// =====================================================================
	// OS "INITIAL" QUE ESTA CLASSE NAO POSSUI
	//
	// Os da escada Saiyajin moram no Catalogo (Formas.cs) porque mexer neles reequilibra o jogo
	// inteiro. Os de baixo ficam aqui, com o dm:linha, porque sao limiares de linhas cujo motor
	// nasceu depois -- e o comentario antigo dizia "ladeiras que o port ainda nao tem motor". Tem
	// agora: a linha Legendary usa restssjat/unrestssjat/lssjat, pelo ChaveDoLimiar do catalogo.
	// =====================================================================

	/// <summary>`restssjat` -- Restrained SSJ do Legendary. lssjbuff.dm:139.</summary>
	public const double RestSsjatInicial = 1_000_000;

	/// <summary>`unrestssjat` -- Unrestrained SSJ. lssjbuff.dm:140.</summary>
	public const double UnrestSsjatInicial = 3_000_000;

	/// <summary>`lssjat` -- Legendary Super Saiyajin. lssjbuff.dm:141.</summary>
	public const double LssjatInicial = 50_000_000;

	/// <summary>
	/// `snamekat` DE FABRICA NO DM -- `Super_Namek.dm:4`, dois milhoes.
	///
	/// **ELE DEIXOU DE SER A PORTA, e continua aqui como MEDIDA.** Ver
	/// <see cref="SNamekatInicial"/> logo abaixo pro numero que vale hoje e pelo pedido do dono que o
	/// trocou. Guardar o valor do original nao e nostalgia: e o que deixa a divergencia CONFERIVEL --
	/// a bancada compara os dois e a distancia entre eles e a propria decisao, escrita em numero.
	/// </summary>
	public const double SNamekatDoDm = 2_000_000;

	/// <summary>
	/// ============================ A PORTA DO SUPER NAMEKUSEIJIN HOJE: A MESMA DO SSJ ============================
	/// Pedido do dono, literal: *"namekuseijins ganham super namek aprox no mesmo requisito do SSJ
	/// (mantendo a ideia de cada um ter um requisito pessoal, mas em torno de um valor)"*.
	///
	/// "Aprox no mesmo requisito do SSJ" **tem um numero**, e ele nao e uma escolha minha: e o
	/// <see cref="Catalogo.SsjatInicial"/>, o 1.500.000 do `supersaiyanbuff.dm:6`. Escrever
	/// `1_500_000` aqui seria a segunda copia do mesmo numero -- a que fica pra tras no dia em que o
	/// dono reequilibrar o SSJ. Entao a base do Namekuseijin **e** a do Saiyajin, por referencia.
	///
	/// ============================ ISTO E DIVERGENCIA DECLARADA, E ELA CUSTA 25% ============================
	/// O DM cobra 2.000.000 (<see cref="SNamekatDoDm"/>). Baixar pra 1,5 M e mexer no original de olho
	/// aberto, e o motivo e a frase do dono acima -- a regra da casa e que quando ele pede uma coisa
	/// que o original nao faz, a palavra dele vence o DM.
	///
	/// **E o que muda de verdade nao e a base: e a DISPERSAO.** Ver <see cref="RolarNamek"/>.
	/// ====================================================================================================
	/// </summary>
	public const double SNamekatInicial = Catalogo.SsjatInicial;

	// =====================================================================
	// A SEMENTE
	// =====================================================================

	/// <summary>
	/// A SEMENTE DESTE PERSONAGEM. Decidida uma vez, no nascimento, e gravada.
	///
	/// Guardar a semente ALEM dos valores parece redundante e nao e: quando um limiar NOVO
	/// entrar no jogo (o port ainda nao tem a escada Legendary nem a do Heran), o personagem
	/// antigo precisa poder rolar SO ele, com o mesmo resultado em qualquer execucao, sem
	/// tocar nos que ja estao valendo. Foi exatamente o problema que o BYOND teve quando o
	/// SSJ4 foi reescrito e precisou de um conserto de login (supersaiyanbuff.dm:851-855).
	/// </summary>
	public ulong Semente;

	/// <summary>
	/// Ja sorteou? Falso = personagem de save antigo (ou nunca nascido por aqui) e os campos
	/// abaixo ainda sao os valores de fabrica. E o unico portao contra re-sortear.
	/// </summary>
	public bool Rolado;

	// =====================================================================
	// OS LIMIARES
	//
	// Os nomes sao os do DM de proposito, igual ao Fighter: `ssjat`, `ssj2at`, `rawssj4at`. Com
	// os nomes iguais da pra por o statsaiyan.dm do lado e conferir linha a linha.
	//
	// O DEFAULT E O VALOR DE FABRICA. Assim um save sem este bloco (ou uma raca que nao rola
	// nada) se comporta exatamente como o jogo se comportava antes desta classe existir.
	// =====================================================================

	/// <summary>SSJ1. BP BASE direto -- nao passa por divisor.</summary>
	public double ssjat = Catalogo.SsjatInicial;

	/// <summary>SSJ2. E BP EXPRESSO: o gate divide por 6 (Transformation Controls.dm:19).</summary>
	public double ssj2at = Catalogo.Ssj2atInicial;

	/// <summary>SSJ3. E BP EXPRESSO: o gate divide por 10 (Transformation Controls.dm:45).</summary>
	public double ssj3at = Catalogo.Ssj3atInicial;

	/// <summary>SSJ4. Ja e BP BASE no original (rework 2026-07-10, supersaiyanbuff.dm:55).</summary>
	public double rawssj4at = Catalogo.RawSsj4atInicial;

	/// <summary>
	/// USSJ. No port os grades abrem por MAESTRIA no SSJ1 e nao por BP, entao este numero nao
	/// gateia nada hoje -- mas e sorteado no nascimento igual aos outros (statsaiyan.dm:53) e o
	/// re-sorteio no dia em que ele voltar a valer seria justamente o bug que esta classe evita.
	/// </summary>
	public double ultrassjat = Catalogo.UltrassjatInicial;

	/// <summary>Restrained SSJ (escada Legendary). statsaiyan.dm:66.</summary>
	public double restssjat = RestSsjatInicial;

	/// <summary>Unrestrained SSJ (escada Legendary). statsaiyan.dm:55.</summary>
	public double unrestssjat = UnrestSsjatInicial;

	/// <summary>Legendary SSJ. statsaiyan.dm:56.</summary>
	public double lssjat = LssjatInicial;

	/// <summary>Super Namekuseijin. statnamek.dm:13-14.</summary>
	public double snamekat = SNamekatInicial;

	/// <summary>
	/// O BP em que este personagem APRENDE o SSJ3 sozinho (Friendship.dm:181). Diferente dos
	/// outros: nao e a porta da forma, e o ponto em que a forma passa a existir pra ele.
	/// ZERO = esta raca/classe nunca aprende assim (o DM recusa Legendary e nao-Saiyajin,
	/// Friendship.dm:178-180) -- e o zero e significativo, o Zenkai le ele
	/// (`Fighter.ZenkaiCeiling`) e cai pro teto medio quando nao ha requisito pessoal.
	/// </summary>
	public double ssj3LearnReq;

	/// <summary>
	/// O BP BASE que abre esta forma PARA ESTE PERSONAGEM. E o numero que a checagem de
	/// transformacao tem que comparar -- o <see cref="FormaDef.PortaBp"/> e so o valor de fabrica.
	///
	/// ============================ ISTO ERA UM SWITCH E VIROU DADO ============================
	/// Antes do rework de catalogo havia aqui um `switch` sobre o enum de formas, e ele era o
	/// QUINTO lugar que uma forma nova precisava tocar -- o que dava uma forma que transformava,
	/// tinha cabelo, drenava Ki, e mesmo assim ignorava o limiar sorteado do personagem. Agora a
	/// entrada do catalogo diz qual limiar manda nela (<see cref="FormaDef.ChaveDoLimiar"/>), e
	/// esquecer o campo da porta ZERO, que e obvio na bancada, em vez de a porta media, que nao e.
	/// ========================================================================================
	///
	/// Os divisores estao aqui e nao no catalogo porque sao propriedade do LIMIAR e nao da forma:
	/// `ssj2at` e `ssj3at` sao BP EXPRESSO no original (Transformation Controls.dm:19 e :45),
	/// enquanto `ssjat` e `rawssj4at` ja sao base.
	/// </summary>
	public double Porta(FormaDef d) => d.ChaveDoLimiar switch
	{
		// os grades saem do SSJ1 e custam o mesmo BP dele; quem separa e a maestria
		"ssjat" => ssjat,
		"ssj2at" => ssj2at / Catalogo.Ssj1GateMult,

		// ============================ O MESMO `ssj2at`, OUTRO DIVISOR -- e o Heran ============================
		// `heran.dm:38` cobra `ssj2at/50` onde o `Transformation Controls.dm:19` cobra `ssj2at/6`. A
		// var e A MESMA (o DM nao criou `heran2at`), entao o que muda e o gate e nao o limiar -- e por
		// isso e uma CHAVE nova aqui e nao um campo novo no personagem.
		//
		// E O NUMERO GRANDE NAO E ENGANO DO ORIGINAL: o `RolarHeran` nao toca `ssj2at`, ele fica nos
		// 3,5 bilhoes de fabrica. Com o divisor 6 a segunda forma do Heran pediria 583 milhoes de BP
		// BASE -- mais que o Super Saiyajin 3 -- e ninguem a alcancaria. Com 50, pede 70 milhoes.
		// ==================================================================================================
		"ssj2at_heran" => ssj2at / Catalogo.HeranGateMult2,

		"ssj3at" => ssj3at / Catalogo.Ssj2GateMult,
		"rawssj4at" => rawssj4at,
		"ultrassjat" => ultrassjat,
		"restssjat" => restssjat,
		"unrestssjat" => unrestssjat,
		"lssjat" => lssjat,
		"snamekat" => snamekat,
		_ => 0,
	};

	// =====================================================================
	// O SORTEIO
	// =====================================================================

	/// <summary>
	/// O NASCIMENTO DOS LIMIARES. Chamado UMA VEZ, na criacao do personagem.
	///
	/// `raca` e `classe` sao os mesmos que o <c>Birth</c> acabou de decidir. Half-Saiyan entra
	/// como "Halfbreed" e recebe o bloco Saiyajin: no DM ele nao tem proto proprio, o genoma
	/// dele CARREGA o proto Saiyajin (stathalfbreed.dm:75-77) e por isso o `special_info()` do
	/// Saiyajin roda nele tambem -- caindo no ramo `else` do switch, junto com o Normal.
	/// </summary>
	public static LimiaresPessoais Rolar(string raca, string classe, ulong semente)
	{
		var l = new LimiaresPessoais { Semente = semente, Rolado = true };

		// Quem carrega o proto Saiyajin: Saiyajin puro e Half-Saiyan (Birth.cs mapeia
		// "Halfbreed" -> proto "Saiyan"). O Quarter Saiyan do DM nao existe no port.
		if (raca is "Saiyan" or "Halfbreed") l.RolarSaiyajin(classe);
		else if (raca == "Heran") l.RolarHeran(classe);
		// O CLA ENTRA AQUI, e ele nao entrava: `RolarNamek()` nao recebia argumento nenhum enquanto a
		// margem era um +-5% plano. Ver `RolarNamek(classe)`.
		else if (raca == "Namekian") l.RolarNamek(classe);

		return l;
	}

	/// <summary>
	/// statsaiyan.dm:45-77.
	///
	/// Primeiro as formas que TODA classe sorteia igual (linhas 52-56), depois o switch por
	/// classe (57-77). A ordem nao muda resultado nenhum aqui -- cada campo tem gerador proprio
	/// -- mas a estrutura e a mesma do DM de proposito, pra conferencia.
	/// </summary>
	private void RolarSaiyajin(string classe)
	{
		// --- iguais pra todas as classes (statsaiyan.dm:52-56) ---------------
		ssj3at = Catalogo.Ssj3atInicial * Faixa("ssj3at", 9, 13);          // :52
		ultrassjat = Catalogo.UltrassjatInicial * Faixa("ultrassjat", 9, 13); // :53
		rawssj4at = Catalogo.RawSsj4atInicial * Faixa("rawssj4at", 9, 13); // :54
		unrestssjat = UnrestSsjatInicial * Faixa("unrestssjat", 9, 13);          // :55
		lssjat = LssjatInicial * Faixa("lssjat", 9, 13);                         // :56

		// --- por classe (statsaiyan.dm:57-77) --------------------------------
		switch (classe)
		{
			// ELITE: SSJ1 mais TARDE (1,1-1,4x), SSJ2 mais CEDO (0,9-1,2x). statsaiyan.dm:58-61
			case "Elite":
				ssjat = Catalogo.SsjatInicial * Faixa("ssjat", 11, 14);
				ssj2at = Catalogo.Ssj2atInicial * Faixa("ssj2at", 9, 12);
				break;

			// LOW-CLASS: o espelho do Elite -- SSJ1 cedo, SSJ2 tarde. statsaiyan.dm:62-64
			case "Low-Class":
				ssjat = Catalogo.SsjatInicial * Faixa("ssjat", 9, 12);
				ssj2at = Catalogo.Ssj2atInicial * Faixa("ssj2at", 11, 14);
				break;

			// LEGENDARY: NAO mexe em ssjat/ssj2at (statsaiyan.dm:65-70). Nao e esquecimento do
			// original: a escada dele e Restrained -> Unrestrained -> Legendary, e o ssjat dele
			// nunca e lido. Mexer aqui inventaria numero.
			case "Legendary":
				restssjat = RestSsjatInicial * Faixa("restssjat", 9, 12);
				break;

			// LEGENDARY PRIMAL: a unica classe que so ENCURTA o SSJ1 (0,9-1,1x). statsaiyan.dm:72-73
			case "Legendary Primal Saiyan":
				ssjat = Catalogo.SsjatInicial * Faixa("ssjat", 9, 11);
				ssj2at = Catalogo.Ssj2atInicial * Faixa("ssj2at", 9, 12);
				break;

			// O RESTO (statsaiyan.dm:75-77): Normal, Normal Primal, Kaio e as tres linhagens
			// Half-Saiyan (New Generation / Future Lineage / Prodigial). O `else` do DM.
			default:
				ssjat = Catalogo.SsjatInicial * Faixa("ssjat", 10, 12);
				ssj2at = Catalogo.Ssj2atInicial * Faixa("ssj2at", 9, 12);
				break;
		}

		// O REQUISITO PESSOAL DO SSJ3 (Friendship.dm:181): 2,0x-3,0x de ssj3at/10 -- e ele parte
		// do ssj3at JA SORTEADO acima, entao duas aleatoriedades se compoem, como no original.
		// O DM rola isso preguicosamente na primeira checagem; rolar no nascimento da a mesma
		// distribuicao e ainda cabe no save. O gate e o do DM (Friendship.dm:178-180): Legendary
		// fica de fora porque segue o caminho LSSJ.
		if (classe != "Legendary")
			ssj3LearnReq = ssj3at / 10 * (Sorteador("ssj3LearnReq").Next(200, 301) / 100.0);
	}

	/// <summary>
	/// statheran.dm:26-42.
	///
	/// O Heran NAO multiplica o valor de fabrica: ele TROCA por uma faixa absoluta, e as faixas
	/// nem se encostam (Low-Class 0,8-2 mi; Epsilon 2,5-5 mi; Omega 5,5-8 mi). Aqui a classe nao
	/// e um tempero no mesmo numero, e o numero inteiro -- o Omega acende quase 7x depois do
	/// Low-Class e paga isso com stats muito melhores (statheran.dm:100-120).
	/// </summary>
	private void RolarHeran(string classe)
	{
		Random r = Sorteador("ssjat");
		ssjat = classe switch
		{
			"Omega" => r.Next(5_500_000, 8_000_001),      // statheran.dm:28
			"Low-Class" => r.Next(800_000, 2_000_001),    // statheran.dm:33
			_ => r.Next(2_500_000, 5_000_001),            // Epsilon -- statheran.dm:39
		};
		// ssj2at do Heran NAO e tocado no DM: fica no valor de fabrica e o gate dele divide por
		// 50, nao por 6 (heran.dm:38). O divisor e da escada Heran, que o port ainda nao tem.
	}

	/// <summary>
	/// ============================ O REQUISITO PESSOAL DO SUPER NAMEKUSEIJIN ============================
	/// Pedido do dono: *"namekuseijins ganham super namek aprox no mesmo requisito do SSJ (mantendo a
	/// ideia de cada um ter um requisito pessoal, mas em torno de um valor)"*.
	///
	/// ============================ O QUE O DM FAZ, E POR QUE NAO BASTAVA ============================
	/// `statnamek.dm:13-14` -- `snamekat/=100` seguido de `snamekat*=rand(95,105)`, que e uma forma
	/// torta de escrever "x0,95 a x1,05". Ou seja o original JA da um limiar pessoal, e este port ja o
	/// portava: era esta funcao, com <see cref="SNamekatDoDm"/> de base.
	///
	/// So que a faixa que sai dali e **1.900.000 a 2.100.000** -- onze valores discretos, +-5%, a mais
	/// estreita do jogo. Comparada com a do SSJ (`statsaiyan.dm:57-77`), que espalha de **1.350.000 a
	/// 2.100.000** conforme a CLASSE, ela e outra coisa: os Namekuseijin sao praticamente todos iguais
	/// entre si, e o centro deles fica 33% acima do centro dos Saiyajin. "Aprox no mesmo requisito" e
	/// "cada um tem o seu" nao se sustentam nessa faixa.
	/// ==========================================================================================
	///
	/// ============================ O QUE VALE HOJE, E OS DOIS INGREDIENTES SAO DO DM ============================
	/// A base e a do SSJ (<see cref="SNamekatInicial"/>) e a dispersao e **por CLA**, com o idioma que
	/// o proprio original usa pras formas altas do Saiyajin (`initial(X) * rand(9,13)/10`,
	/// `statsaiyan.dm:52-56`) e com o mesmo DESENHO do `switch` por classe do SSJ1
	/// (`statsaiyan.dm:57-77`): **quem nasce forte transforma mais TARDE**.
	///
	/// A ordem dos tres clas nao e opiniao, ela sai do `class_stats` do `statnamek.dm`:
	///
	///   * **Warrior clan** -- `Starting BP` 50, `Battle Power` 1,6. E o Elite de Namek, e leva a faixa
	///     do Elite: **1,1x a 1,4x**. Nasce o mais forte e paga isso na porta;
	///   * **Demon clan** -- `Starting BP` 35, `Battle Power` 1,4. O meio, e leva o `else` do DM:
	///     **1,0x a 1,2x**;
	///   * **Dragon clan** -- `Starting BP` 25, `Battle Power` 1,2. E o Low-Class de Namek (o cla de
	///     suporte, o mais fraco de briga) e leva a faixa dele: **0,9x a 1,2x**. Transforma cedo,
	///     porque e o unico consolo que ele tem.
	///
	/// A FAIXA RESULTANTE E **1.350.000 a 2.100.000** -- a mesma do SSJ, numero por numero. Que e o
	/// pedido do dono lido ao pe da letra.
	/// ======================================================================================================
	/// </summary>
	/// <param name="cla">O `Class` do Namekuseijin: "Warrior clan", "Demon clan" ou "Dragon clan".</param>
	private void RolarNamek(string cla) =>
		snamekat = SNamekatInicial * (cla switch
		{
			"Warrior clan" => Faixa("snamekat", 11, 14),
			"Dragon clan" => Faixa("snamekat", 9, 12),
			_ => Faixa("snamekat", 10, 12),
		});

	// =====================================================================
	// O GERADOR
	// =====================================================================

	/// <summary>
	/// `rand(lo,hi)/10` do DM, o idioma que aparece em todas as linhas do statsaiyan.
	///
	/// E DISCRETO e isso importa: `rand(9,13)/10` da CINCO valores (0,9 / 1,0 / 1,1 / 1,2 / 1,3),
	/// nao um numero continuo entre 0,9 e 1,3. Trocar por `NextDouble()` numa faixa mudaria a
	/// distribuicao e o resultado nunca bateria com o do BYOND.
	/// </summary>
	private double Faixa(string campo, int lo, int hi) => Sorteador(campo).Next(lo, hi + 1) / 10.0;

	/// <summary>
	/// UM GERADOR POR CAMPO, derivado de (semente do personagem + nome do campo).
	///
	/// INVENCAO MINHA -- nao ha equivalente no DM, que usa o `rand()` global do BYOND. Existe por
	/// duas razoes praticas:
	///
	///  * um `Random` compartilhado torna o resultado dependente da ORDEM das chamadas: acrescentar
	///    um limiar novo entre dois existentes mudaria os dois seguintes, e o personagem de ontem
	///    acordaria com outra porta de SSJ2.
	///  * `string.GetHashCode()` nao serve de semente: o .NET o randomiza por processo, entao o
	///    mesmo personagem daria numeros diferentes a cada reinicio do servidor. Por isso
	///    <see cref="Espaco.Hash64"/>, que ja existe no Core justamente por ser estavel entre
	///    maquinas e execucoes.
	/// </summary>
	private Random Sorteador(string campo)
	{
		ulong h = Espaco.Misturar(Semente, Espaco.Hash64(campo), 0x5347_4F4B_4C494D55UL);
		return new Random(unchecked((int)(h ^ (h >> 32))));
	}

	/// <summary>
	/// A SEMENTE DE UM PERSONAGEM NOVO, a partir do que ja o identifica no save.
	///
	/// Nome + instante de criacao: dois personagens de mesmo nome em contas diferentes nascem
	/// no mesmo milissegundo com probabilidade desprezivel, e a dupla ja esta gravada
	/// (`CharacterSave.Nome` / `CharacterSave.CriadoEm`), o que deixa a semente reconstruivel
	/// se o campo dela se perder num save antigo.
	/// </summary>
	public static ulong SementeDe(string nome, long criadoEm) =>
		Espaco.Misturar(Espaco.Hash64(nome), unchecked((ulong)criadoEm), 0x9E37_79B9_7F4A_7C15UL);
}
