using Jandirus.Core.World;

namespace Jandirus.Core.Magic;

/// <summary>
/// UM SINAL DO RADAR: onde uma esfera esta, em pixels da zona, e se alguem a carrega.
///
/// E tudo o que o servidor conta a quem tem o radar. O NUMERO de estrelas nao viaja de proposito: o
/// radar do anime mostra pontos, e quem quer saber qual e qual vai ate la.
/// </summary>
/// <param name="Carregada">Esta na mao de alguem (o ponto anda com a pessoa).</param>
/// <param name="Comigo">Esta na MINHA mao -- desenhada no centro do disco, e nao pela posicao.</param>
public readonly record struct SinalDeEsfera(float X, float Y, bool Carregada, bool Comigo);

/// <summary>
/// **O DISCO DO DRAGON RADAR** -- a geometria e as constantes, sem tela e sem servidor.
///
/// O dono (2026-10-10): *"quando tiver radar do dragao no canto inferior direito vai aparecer um circulo
/// igual um radar com as bolinhas amarelas a onde as esferas estao em relacao a voce"*.
///
/// ============================ QUEM SABE ONDE AS ESFERAS ESTAO E O SERVIDOR ============================
/// O disco so desenha o que chega no pacote `S2C.RadarDeEsferas`, e esse pacote so sai pra quem tem o
/// Dragon Radar na mochila (`GameServer.RadarDeEsferas.cs`) -- a mesma regra do verb de radar: so esfera
/// ACORDADA, e so deste mundo. Aqui mora o que as duas pontas precisam concordar e o que da pra medir
/// sem abrir o jogo: onde um sinal cai no disco.
/// ====================================================================================================
/// </summary>
public static class RadarDeEsferas
{
	/// <summary>
	/// OS ALCANCES DO DISCO, em tiles do centro a borda -- o zoom alternavel (um clique no disco troca).
	/// O do meio e o de fabrica: 150 tiles cobrem um quarto de um planeta desenhado (500 de lado) e quase
	/// um planeta gerado pequeno inteiro (192); o curto serve pra chegar na esfera, o longo pra saber de
	/// que lado do mundo ela esta.
	/// </summary>
	public static readonly int[] AlcancesEmTiles = [50, 150, 500];

	/// <summary>O indice de <see cref="AlcancesEmTiles"/> com que o disco nasce.</summary>
	public const int AlcanceDeFabrica = 1;

	/// <summary>O alcance de um degrau do zoom, em pixels de mundo. Indice fora da tabela da a volta.</summary>
	public static float AlcanceEmPx(int degrau) =>
		AlcancesEmTiles[((degrau % AlcancesEmTiles.Length) + AlcancesEmTiles.Length) % AlcancesEmTiles.Length]
		* (float)ZoneCollision.TileSize;

	/// <summary>
	/// DE QUANTO EM QUANTO a posicao de uma esfera CARREGADA muda no pacote, em pixels: um tile. E o que
	/// faz quem esta parado com uma esfera na mao nao gerar pacote nenhum -- o servidor so manda quando o
	/// que o disco mostraria mudou (ver `GameServer.TickDoRadarDeEsferas`).
	/// </summary>
	public const float PassoDaCarregada = ZoneCollision.TileSize;

	/// <summary>Teto de sinais num pacote. Um mundo tem um set (sete esferas); a folga e pra esfera de passagem.</summary>
	public const int TetoDeSinais = 32;

	/// <summary>Quanto tempo a varredura leva pra dar uma volta, em segundos. So desenho.</summary>
	public const double SegundosPorVolta = 2.6;

	/// <summary>DEFEITO INJETADO (bancadas `--radarteste` e `--diagmapa`): o sinal alem do alcance nao e preso a borda -- ele cai fora do disco.</summary>
	public static bool SemPrenderNaBordaDeTeste;

	/// <summary>DEFEITO INJETADO (bancadas `--radarteste` e `--diagmapa`): o eixo Y do disco invertido -- a esfera ao norte aparece embaixo.</summary>
	public static bool NorteParaBaixoDeTeste;

	/// <summary>
	/// ONDE UM SINAL CAI NO DISCO, em fracoes do raio: (0, 0) e o centro (voce), X cresce pro leste e Y
	/// pro SUL -- os mesmos eixos do mundo, entao o norte fica em cima.
	///
	/// ALEM DO ALCANCE O SINAL FICA PRESO NA BORDA, na direcao certa: um radar que so mostrasse o que
	/// cabe no disco ficaria vazio no planeta inteiro menos em volta de cada esfera, que e justamente
	/// onde ninguem precisa dele.
	/// </summary>
	/// <param name="dx">Esfera menos eu, em pixels (leste positivo).</param>
	/// <param name="dy">Esfera menos eu, em pixels (sul positivo).</param>
	public static (float X, float Y, bool NaBorda) NoDisco(float dx, float dy, float alcancePx)
	{
		if (NorteParaBaixoDeTeste) dy = -dy;
		alcancePx = Math.Max(alcancePx, 1f);
		float d = MathF.Sqrt(dx * dx + dy * dy);
		if (d <= alcancePx) return (dx / alcancePx, dy / alcancePx, false);
		if (SemPrenderNaBordaDeTeste) return (dx / alcancePx, dy / alcancePx, true);
		return (dx / d, dy / d, true);
	}

	/// <summary>A posicao que viaja de uma esfera CARREGADA: a de quem a carrega, no centro do tile dele.</summary>
	public static Vec2 PosicaoDaCarregada(Vec2 portador) => new(
		(MathF.Floor(portador.X / PassoDaCarregada) + 0.5f) * PassoDaCarregada,
		(MathF.Floor(portador.Y / PassoDaCarregada) + 0.5f) * PassoDaCarregada);
}
