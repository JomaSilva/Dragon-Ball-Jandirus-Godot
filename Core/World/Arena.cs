namespace Jandirus.Core.World;

/// <summary>
/// O RETANGULO DE UMA ARENA, em CELULAS e com as quatro bordas DENTRO -- o `in_arena` e o
/// `edge_margin` do `Tournament.dm` (`TRN_ARENA_X1..Y2`, e a arena celeste `TRNH_*`).
///
/// ============================ POR QUE MORA NO CORE, E NAO NO TORNEIO ============================
/// Duas coisas fazem a mesma pergunta e nao podem discordar: o JUIZ (pisou fora sem voar = derrota)
/// e o CEREBRO (perto da linha, recua ou decola). No original as duas leem o mesmo `edge_margin`
/// (`Tournament.dm:275-277`); aqui as duas leem este struct. Uma copia da conta no cerebro seria
/// o dia em que o NPC "sabe" uma linha e o juiz cobra outra.
/// ============================================================================================
/// </summary>
/// <param name="X1">coluna esquerda, inclusive</param>
/// <param name="Y1">linha de cima, inclusive (o eixo Y do port cresce pra BAIXO; o do BYOND, pra cima)</param>
/// <param name="X2">coluna direita, inclusive</param>
/// <param name="Y2">linha de baixo, inclusive</param>
public readonly record struct Arena(int X1, int Y1, int X2, int Y2)
{
	/// <summary>A celula esta dentro? Bordas contam como dentro -- `M.x >= arena_x1 && M.x <= arena_x2`.</summary>
	public bool Contem(int cx, int cy) => cx >= X1 && cx <= X2 && cy >= Y1 && cy <= Y2;

	/// <summary>A mesma pergunta pra um ponto em pixels (o centro dos pes do corpo).</summary>
	public bool ContemEm(Vec2 pos) => Contem(Celula(pos.X), Celula(pos.Y));

	/// <summary>
	/// QUANTAS CELULAS ATE A LINHA MAIS PROXIMA -- o `edge_margin` (`Tournament.dm:275`), inteiro e
	/// em celulas de proposito: `TRN_AI_EDGE_MARGIN 2` foi escrito nessa unidade, e a IA reage quando
	/// `margem &lt;= 2`. Zero = em cima da linha; negativo = ja fora.
	/// </summary>
	public int MargemEmCelulas(Vec2 pos)
	{
		int cx = Celula(pos.X), cy = Celula(pos.Y);
		return Math.Min(Math.Min(cx - X1, X2 - cx), Math.Min(cy - Y1, Y2 - cy));
	}

	/// <summary>O centro da arena em pixels -- pra onde a IA recua (`locate(round((x1+x2)/2), ...)`).</summary>
	public Vec2 Centro => new((X1 + X2 + 1) * ZoneCollision.TileSize / 2f,
							  (Y1 + Y2 + 1) * ZoneCollision.TileSize / 2f);

	public int Largura => X2 - X1 + 1;
	public int Altura => Y2 - Y1 + 1;

	private static int Celula(float v) => (int)MathF.Floor(v / ZoneCollision.TileSize);

	public override string ToString() => $"arena ({X1},{Y1})-({X2},{Y2})";
}
