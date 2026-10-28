namespace Jandirus.Core.World;

/// <summary>
/// (2026-10-27, o pedido 124d) A CORRIDA DA SNAKE WAY -- os numeros da escolha do dono ("Corrida + exigencia"), do documento de um
/// jogador: *"a dialog option for king yemma to let you run snake way, with a very small bp requirement before he allows you to, and king
/// kais training would require you to have completed the snake way quest, giving you a small flat bp increase just for clearing it"*. O
/// servidor dela: `GameServer.SnakeWay.cs`.
/// </summary>
public static class SnakeWay
{
	/// <summary>O BP minimo (o `Fighter.BP`, o poder real) pra o Enma deixar correr.</summary>
	public const double BpMinimo = 1000;

	/// <summary>O BP fixo de quem chega ao fim da estrada a pe, uma vez na vida.</summary>
	public const double BonusDeBp = 1000;

	/// <summary>O verbo do menu E do Enma.</summary>
	public const string Verbo = "enma_snakeway";

	/// <summary>
	/// ============================ A CHEGADA E O FIM DA ESTRADA (dono, 2026-10-27, o pedido 142) ============================
	/// *"como completo a missao de chegar no sr kaio pela snakeway se a unica forma de entrar na ilha dele é voando"*. A corrida fechava a dois
	/// tiles do Sr. Kaioh, A PE -- e a estrada termina na cabeca da serpente, a 11 tiles de NUVEM da ponta sul da ilha dele (a pe a nuvem joga
	/// no Inferno; voar cancela a corrida): ninguem fechava. O dono, perguntado: "Fim da estrada". A chegada e a cabeca da serpente, o fim do
	/// Caminho no mapa `z06_Afterlife` (celulas, y de cima pra baixo: a estrada tem cinco de largura e termina na fileira 50); dali o
	/// corredor VOA ate a ilha, e o Sr. Kaioh ensina a quem tem a marca.
	/// ======================================================================================================================
	/// </summary>
	public const int FimX = 176, FimY = 51;

	/// <summary>A que distancia da cabeca (em tiles, do centro da celula dos pes) a corrida fecha: as ultimas fileiras da estrada.</summary>
	public const double TilesDaChegada = 2.5;

	/// <summary>Os pes deste corpo estao na chegada, a cabeca da serpente? (So vale na zona do Outro Mundo -- quem chama confere.)</summary>
	public static bool NaChegada(Vec2 corpo)
	{
		(int cx, int cy) = Caminho.Celula(corpo);
		double dx = cx - FimX, dy = cy - FimY;
		return dx * dx + dy * dy <= TilesDaChegada * TilesDaChegada;
	}
}
