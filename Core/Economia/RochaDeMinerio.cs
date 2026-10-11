using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Core.Economia;

/// <summary>
/// A ROCHA DE MINERIO -- a pedra escura de cristais vermelhos que nasce pelos planetas e rende recursos a quem
/// a colhe. Pedido do dono de 2026-10-10:
///
///   *"essas rochas de minerios deveriam spawnar de forma aleatoria nos planetas (terra, namek, vegeta etc,
///   menos em locais como lookout) e ao interagir com eles vc pega uma quantidade aleatoria de recursos. [...]
///   adicione colisao nessas rochas grandes e nas rochas de recursos. essas rochas de recursos spawnam em
///   quantidades proporcionais a quantidade de recursos do planeta, se alguem usar uma drill e zerar o planeta,
///   essas rochas param de spawnar, se o planeta estiver cheio, elas spawnam mais."*
///
/// ============================ DE ONDE ELA VEM ============================
/// A ARTE ja estava no jogo: e a `Red Rock.dmi` do original (la, o `/obj/Raw_Material/Source_Stone` de
/// `Enchanting.dm:375-378`), que o gerador de planetas pintava nos veios das colinas como um tile PARADO --
/// sem fisica, sem menu e sem fim. Era o que o dono via na foto. Esse tile saiu do gerador
/// (`CoberturaDeTerreno`): a rocha passou a ser uma coisa do SERVIDOR, que nasce e some.
///
/// NO DM o no de minerio (`/obj/Raw_Material`, `Crafting Master.dm:338-374`) e denso, se quebra no SOCO por
/// quem tem a maestria de mineracao (`attack_proc.dm:26-45`) e larga de 1 a 4 materiais em volta; quem os
/// repoe sao os `matspawners` do mapa (`:376-407`), ate seis por ponto. A maestria e os materiais nao foram
/// portados, e o desenho daqui e o do dono: colhe-se pela tecla E, o que sai e o RECURSO, e a quantidade de
/// rochas segue a reserva do planeta. Do original ficam a densidade e a ideia de reposicao.
/// =========================================================================
///
/// ESTE ARQUIVO SO TEM A REGRA E OS NUMEROS. Quem guarda as rochas de pe, sorteia o lugar e paga e o
/// servidor (`GameServer.Rochas.cs`); quem as pinta e o cliente (`RochasNaTela`). O que sai da rocha sai da
/// reserva do planeta por <see cref="ReservaDePlaneta.Tirar"/>, a porta unica.
/// </summary>
public static class RochaDeMinerio
{
	/// <summary>O "tipo" que a tecla E enxerga: a linha desta rocha no <see cref="Interacoes.De"/>.</summary>
	public const string Tipo = "Ore_Rock";

	/// <summary>O nome que o menu escreve.</summary>
	public const string Nome = "Rocha de minério";

	/// <summary>O verbo do botao de colher.</summary>
	public const string Verbo = "rocha_coletar";

	/// <summary>
	/// A arte: a pedra escura de cristais vermelhos, 32x32. A mesma folha que o tileset dos mapas ja usa --
	/// nao ha arte nova pra importar.
	/// </summary>
	public const string Arte = "res://Assets/Sprites/Misc/Objects/Materials/Ores/Red Rock.png";

	// =====================================================================
	// QUANTAS
	// =====================================================================

	/// <summary>
	/// QUANTO CHAO CADA ROCHA TEM PRA SI num planeta de reserva cheia: uma a cada 20x20 tiles de terra em que
	/// se anda. Numa tela comum cabem uns 100 tiles -- com o planeta cheio ve-se uma rocha a cada tres ou
	/// quatro telas de caminhada.
	/// </summary>
	public const double CelulasPorRocha = 400;

	/// <summary>
	/// O TETO DE ROCHAS de um planeta, pelo TAMANHO dele: a terra em que se anda (o servidor passa o chao
	/// navegavel do planeta, sem mar, montanha nem predio), uma rocha a cada <see cref="CelulasPorRocha"/>.
	/// </summary>
	public static int Teto(int celulasDeTerra) => (int)(Math.Max(0, celulasDeTerra) / CelulasPorRocha);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--rochasteste`): a reserva do planeta nao entra na conta -- o alvo e sempre
	/// o teto, e um planeta zerado continua parindo rocha. Falso em jogo, sempre.
	/// </summary>
	public static bool IgnoraAReservaDeTeste;

	/// <summary>
	/// QUANTAS ROCHAS O PLANETA SUSTENTA AGORA: o teto vezes a fracao da reserva
	/// (<see cref="ReservaDePlaneta.Fracao"/>). Reserva zerada, alvo zero -- as rochas param de nascer.
	/// Reserva cheia, o teto. Arredonda pro inteiro mais perto: o planeta a 99,9% continua com todas (uma
	/// coleta nao pode custar a rocha seguinte), e o quase seco nao segura meia rocha.
	/// </summary>
	public static int Alvo(int teto, double fracao)
	{
		if (IgnoraAReservaDeTeste) return Math.Max(0, teto);
		if (!(fracao > 0) || teto <= 0) return 0;
		return (int)Math.Round(teto * Math.Min(1.0, fracao), MidpointRounding.AwayFromZero);
	}

	// =====================================================================
	// QUANTO RENDE
	// =====================================================================

	/// <summary>A faixa do que UMA rocha rende, em recursos: o sorteio e uniforme entre os dois, inteiro.</summary>
	public const int RendimentoMinimo = 40, RendimentoMaximo = 160;

	/// <summary>
	/// O QUE ESTA ROCHA PEDE A RESERVA. <paramref name="sorteio"/> e um numero em [0,1). O que o jogador
	/// recebe e o que a reserva ENTREGAR (`Tirar`): num planeta quase seco, menos que o pedido.
	/// </summary>
	public static double Rendimento(double sorteio)
	{
		double s = Math.Clamp(sorteio, 0.0, 0.999999);
		return RendimentoMinimo + Math.Floor(s * (RendimentoMaximo - RendimentoMinimo + 1));
	}

	// =====================================================================
	// A REPOSICAO
	// =====================================================================

	/// <summary>
	/// EM QUANTOS MINUTOS um planeta sem rocha nenhuma volta a ter todas as que a reserva dele sustenta. O
	/// relogio e lento de proposito: a rocha colhida nao renasce no mesmo lugar nem na mesma hora.
	/// </summary>
	public const double MinutosPraReporTudo = 60;

	/// <summary>
	/// O intervalo entre uma rocha e a seguinte num planeta: o tempo de repor tudo, dividido pelo teto dele.
	/// Planeta grande repoe mais rochas por minuto -- e leva o mesmo tempo pra se refazer que um pequeno.
	/// </summary>
	public static double SegundosPorRocha(int teto) => MinutosPraReporTudo * 60.0 / Math.Max(1, teto);

	// =====================================================================
	// ONDE
	// =====================================================================

	/// <summary>A distancia minima entre duas rochas, em tiles (a maior das duas diferencas).</summary>
	public const int DistanciaEntreRochas = 6;

	/// <summary>
	/// A que distancia de um JOGADOR uma rocha pode nascer, em tiles: fora da tela dele. Rocha que brota na
	/// cara de quem esta olhando e um defeito de desenho; e uma que brota em cima dele e uma prisao.
	/// </summary>
	public const int DistanciaDeJogador = 12;

	/// <summary>A que distancia de qualquer outro corpo (habitante, chefe, cadaver), em tiles.</summary>
	public const int DistanciaDeCorpo = 2;

	/// <summary>Quantos lugares o servidor sorteia antes de desistir de por UMA rocha nesta volta.</summary>
	public const int TentativasPorRocha = 60;

	/// <summary>
	/// O CHAO DESTA CELULA ACEITA UMA ROCHA? A celula e as OITO em volta tem de ser chao em que se anda, a
	/// ceu aberto.
	///
	/// AS OITO EM VOLTA SAO A REGRA INTEIRA, e nao folga: a rocha barra, e uma coisa que barra e nasce ao
	/// acaso nao pode fechar passagem. Com o anel de fora livre, todo caminho que passaria pela celula da
	/// rocha da a volta por ele -- ela nunca tapa um corredor, uma ponte, um portao nem a frente de uma
	/// porta, e nunca nasce encostada em parede, agua, arvore, maquina ou outra rocha.
	///
	/// `ServeDeChao` ja recusa parede, borda do mundo, agua, nuvem, obra densa, bloco erguido e boca de
	/// passagem; o teto (predio do mapa, caverna, base de jogador) e conferido aqui.
	/// </summary>
	public static bool ChaoServe(ZoneCollision mapa, int cx, int cy)
	{
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
			{
				int x = cx + dx, y = cy + dy;
				if (!mapa.ServeDeChao(x, y)) return false;
				if (CelulaInterna.SobTeto(mapa, x, y, caiu: false)) return false;
			}
		return true;
	}

	// =====================================================================
	// O ALCANCE
	// =====================================================================

	/// <summary>
	/// O PONTO DA ROCHA pra medir distancia: onde estaria o centro de um corpo com os pes no meio da celula
	/// dela -- a mesma conta da mobilia que o mapa traz (`GameServer.PorMobiliaDoMapa`), pra a tecla E medir
	/// a rocha como mede um banco.
	/// </summary>
	public static Vec2 Ponto(int cx, int cy) => new(
		cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f,
		cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f - MoveRules.FeetOffsetY);

	/// <summary>
	/// ESTE CORPO ALCANCA A ROCHA DESTA CELULA? Por EIXO e com o numero de todo gesto da tecla E
	/// (<see cref="Interacoes.Alcance"/>): o cliente acende o "[E]" com esta conta e o servidor aceita o verbo
	/// com ela.
	/// </summary>
	public static bool AoAlcance(Vec2 corpo, int cx, int cy)
	{
		Vec2 p = Ponto(cx, cy);
		return Math.Abs(p.X - corpo.X) <= Interacoes.Alcance && Math.Abs(p.Y - corpo.Y) <= Interacoes.Alcance;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--rochasteste`): a rocha de minerio e so desenho -- nasce, aparece e nao
	/// barra ninguem. Lido pela linha que a assenta no mapa, nas duas pontas. Falso em jogo, sempre.
	/// </summary>
	public static bool SemColisaoDeTeste;

	/// <summary>
	/// ASSENTA UMA ROCHA NO MAPA DE COLISAO. Um funil pras duas pontas: o servidor ao parir a rocha e o
	/// cliente ao recebe-la passam por aqui, e o botao de defeito tambem.
	/// </summary>
	public static void Assentar(ZoneCollision mapa, int cx, int cy)
	{
		if (!SemColisaoDeTeste) mapa.PorRocha(cx, cy);
	}
}
