namespace Jandirus.Core.Races;

/// <summary>
/// ============================ A GOSMA DO MAJIN (dono, 2026-11-01, o pedido 219) ============================
/// *"vamos implementar essa gosma q vc coloca no chao pros majins (todos) mas ela dps q absorve uma pessoa ela vai voando rapidamente na
/// direçao do majin q colocou essa gosma no chao (ou seja sair da zona desfaz as gosmas) e ao encostar no corpo do majin q colocou a gosma
/// no chao, ele absorve a pessoa"*.
///
/// E a `Goo Trap` do DU (`Code/Races/Majins/Goo Trap.dm`), que todo Majin ganha no login (`Main.dm:186`): o Majin larga uma gosma onde esta
/// (apertar de novo a recolhe; uma por Majin); ela espreita INVISIVEL e intocavel; de tempos em tempos olha em volta e, achando um JOGADOR
/// nocauteado ou mais fraco que ela, aparece (e ai qualquer golpe a desfaz), vai atras dele, GRUDA e da nove segundos pra ele superar o
/// poder dela -- quem supera se solta, e ela volta pro lugar e se esconde de novo.
///
/// O QUE O DONO MUDOU: no DU o Majin absorvia de longe no fim dos nove segundos. Aqui a gosma ENGOLE a pessoa e VOA RAPIDO ate o Majin --
/// o corpo dela some e a camera dela vai junto com a gosma --, e e ao ENCOSTAR nele que ele a absorve, pela absorcao de sempre (o interior e
/// a imagem). O Majin que sai da zona desfaz as gosmas dele (e quem ia dentro cai onde a gosma estava).
///
/// O QUE FICOU DE FORA DO DU, e por que: a `determination` da vitima (o `prob(M.determination)` do `Absorb`, que soltava todo mundo com
/// 100) -- o port nao tem esse numero, e eu propus deixar de fora; e o `cant_logout_until_time` de 20 s, que o port nao tem como cobrar.
/// ==================================================================================================================
/// </summary>
public static class GosmaDoMajin
{
	/// <summary>O id do verbo (`C2S.Habilidade`) e a chave do botao (`hab:gosma`).</summary>
	public const string Verbo = "gosma";

	/// <summary>O poder dela: 70% do poder que o Majin esta usando ao po-la (`GetGooBP`: `BP * 0.7`).</summary>
	public const double FracaoDoPoder = 0.7;

	/// <summary>O preco: enquanto ela esta no mapa, o Majin fica com 73% do poder (`goo_trap_bp_mult`, `Stat Loop.dm:192`).</summary>
	public const double PoderDoMajinComAGosmaNoMapa = 0.73;

	/// <summary>A primeira olhada (`sleep(40)` no `GooWaitForTarget`) e as seguintes (`sleep(80)`), em segundos.</summary>
	public const double SegundosAtePrimeiraOlhada = 4, SegundosEntreOlhadas = 8;

	/// <summary>Ate onde ela olha (`player_view(15)`) e ate onde persegue antes de desistir (`viewable(src, m, 30)`), em tiles.</summary>
	public const double TilesDeFaro = 15, TilesDePerseguicao = 30;

	/// <summary>O passo dela atras da presa (`step_towards` a cada 2 decimos: cinco tiles por segundo) e o VOO de volta pro Majin.</summary>
	public const double TilesPorSegundoCacando = 5, TilesPorSegundoVoando = 18;

	/// <summary>Grudada: nove conferidas, uma por segundo (`for(var/v in 1 to 9)` com `sleep(10)`).</summary>
	public const int Conferidas = 9;
	public const double SegundosPorConferida = 1;

	/// <summary>Quao perto e "chegou": na presa (o mesmo tile) e no Majin (encostou).</summary>
	public const double TilesDoBote = 0.6, TilesDoToque = 1.0;

	/// <summary>
	/// (o pedido 219h) O SUPER BOO DA SAGA tambem larga gosma -- *"faça a parte do super buu da saga buu poder usar essa mecanica (os outros
	/// buus como fat e kid nao podem usar)"*: sem gosma no mapa, ele larga uma onde esta a cada tantos segundos. ESCOLHA MINHA: ele nao paga
	/// os 27% (o poder dele e o da saga, pinado pelo roteiro).
	/// </summary>
	public const double SegundosAteAGosmaDoSuperBoo = 20;

	/// <summary>
	/// QUEM ELA PEGA: nocauteado, ou mais fraco que ela (`if(m.KO) return 1` e `if(m.BP < goo_bp) return 1`, `IsViableGooTarget`).
	/// </summary>
	public static bool Presa(bool nocauteado, double poderDoAlvo, double poderDaGosma) => nocauteado || poderDoAlvo < poderDaGosma;

	/// <summary>
	/// GRUDADA, QUEM SE SOLTA: de pe e com o poder ja no dela ou acima -- o `else` do `GooAbsorbTarget` (`GooStrongerThanTarget(m) || m.KO`).
	/// </summary>
	public static bool Escapa(bool nocauteado, double poderDoAlvo, double poderDaGosma) => !nocauteado && poderDoAlvo >= poderDaGosma;

	// ============================ AS FRASES ============================
	public const string AoPor = "você larga um pedaço de você no chão: a gosma espreita invisível até alguém fraco o bastante chegar perto, "
							  + "e aí ataca. Apertar de novo a recolhe.";
	public const string AoRecolher = "sua gosma volta pra você.";
	public const string AoGrudar = "a gosma de {0} grudou em você! Aumente o seu poder pra se soltar!";
	public const string AoSoltar = "você se livrou da gosma de {0}!";
	public const string AoEngolir = "a gosma de {0} te engoliu -- e voa com você até ele!";
	public const string AoDonoQuandoEngole = "sua gosma engoliu {0} e está trazendo pra você.";
	public const string AoDonoQuandoDestroem = "sua gosma foi destruída. Você pode fazer outra agora.";
	public const string AoDonoQuandoSai = "você saiu daqui, e a sua gosma se desfez.";
	public const string AoSoltoNoCaminho = "a gosma que te levava se desfez -- você está livre.";
	public const string RecusaNaoMajin = "só os Majins põem gosma.";
	public const string RecusaCaido = "caído, você não consegue largar a gosma.";
	public const string RecusaAqui = "aqui não dá pra largar a gosma.";
}
