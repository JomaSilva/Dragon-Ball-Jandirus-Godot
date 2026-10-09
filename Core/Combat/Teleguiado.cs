using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// A CURVA DE UM TIRO TELEGUIADO -- quanto ele vira atras do alvo, e ate onde.
///
/// ============================ O PEDIDO DO DONO (2026-10-09) ============================
/// *"...a nao ser q na criacao de tecnica o usuario coloque q o ataque e teleguiado ai beams etc feitos
/// teleguiados, caso o target se mova o beam se torce levemente pra direcao nova do target, claro q se o
/// target se mover muito o beam ou ki n vai fazer uma curva de 90 graus, fazendo assim o ataque errar"*;
/// *"(e nao so beams, blasts etc tb)"*.
///
/// Duas travas, e as duas sao do pedido:
///   * LEVEMENTE -- o tiro vira ate <see cref="GrausPorTile"/> a cada tile que anda. E CURVATURA, e nao graus
///     por segundo: a curva de uma tecnica e a mesma seja qual for a velocidade dela (o raio de curva e de
///     uns 11 tiles), e quem corre de lado perto demais sai da frente antes de o tiro conseguir dobrar.
///   * NUNCA 90 GRAUS -- ele nunca aponta a mais de <see cref="DesvioMaximoEmGraus"/> do rumo em que SAIU. Quem
///     passa pro lado dele (ou pras costas) foi perdido: o tiro segue na beirada do leque, e erra.
///
/// ============================ DIVERGENCIA DECLARADA DO DM ============================
/// La ha tres perseguicoes, e nenhuma e esta:
///   * a bola customizada teleguiada anda por `walk_towards(A, T, lag)` (`customattacks.dm:534`): corrige o
///     rumo a CADA passo, sem limite, e da a volta em quem ela persegue ate o `Burnout`. Era a regra deste
///     port ate 2026-10-09 (<see cref="SemLimiteDeTeste"/> e ela).
///   * a bola comum com `blasthoming` (`objects.dm:592-601`) da UM passo de lado na direcao do alvo a cada
///     0,8 s -- se ele estiver a 5 tiles, e se o sorteio da pericia deixar (`prob(homingchance)`).
///   * o raio com `homeTarget` (`beams.dm:162-165`) mira o TURF em que o alvo estava no disparo, e so aceita
///     virar 45 graus pra cada lado (`objects.dm:104-107`) -- e de la o 45 daqui.
/// O dono descreveu UMA regra pros tres, e e ela que vale.
/// ====================================================================================
///
/// ============================ O RAIO TELEGUIADO NAO ENTORTA: ELE GIRA ============================
/// O feixe deste port e UM objeto reto, da mao ate a cabeca (ver o cabecalho do <see cref="Projetil"/>), e
/// tudo que encosta nele conta com isso -- o plantio da cabeca na frente do corpo, o corte do tronco, o
/// cruzamento, a disputa (<see cref="Feixe"/>). Entao o raio teleguiado continua reto e GIRA em torno da
/// cauda, como quem acompanha o alvo com as maos; a ponta escorrega de lado ate <see cref="LadoPorFrente"/>
/// do que ela anda pra frente. Um raio que entortasse no meio pediria de volta a lista de segmentos do DM.
/// =================================================================================================
/// </summary>
public static class Teleguiado
{
	/// <summary>Quantos graus o tiro vira a cada tile que anda. Com 5, o raio de curva e de ~11,5 tiles.</summary>
	public const float GrausPorTile = 5f;

	/// <summary>O LEQUE: o tiro nunca aponta a mais do que isto do rumo em que saiu.</summary>
	public const float DesvioMaximoEmGraus = 45f;

	/// <summary>
	/// A PONTA DE UM RAIO anda de lado, no maximo, esta fracao do que anda pra frente. E o que impede o raio
	/// comprido de varrer a tela: girar um feixe de 20 tiles um grau desloca a ponta dele 11 px, e sem esta
	/// trava a ponta correria de lado tanto mais depressa quanto mais longe estivesse.
	/// </summary>
	public const float LadoPorFrente = 0.25f;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o teleguiado aponta direto pro alvo a cada tique, sem curva minima e sem
	/// leque -- o `walk_towards` do DM, e este port ate 2026-10-09. Ele da meia-volta em cima de quem passou
	/// por ele. Falso em jogo, sempre.
	/// </summary>
	public static bool SemLimiteDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o raio teleguiado gira sem conferir o cenario -- o tronco varre por dentro
	/// de um muro pra alcancar quem se escondeu atras dele (a cabeca so confere parede ANDANDO pra frente).
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool VarreParedeDeTeste;

	/// <summary>Quantos graus uma BOLA pode virar depois de andar isto.</summary>
	public static float GiroDaBola(float andadoPx) => GrausPorTile * andadoPx / ZoneCollision.TileSize;

	/// <summary>
	/// Quantos graus um RAIO deste comprimento pode girar enquanto a cabeca dele anda isto: a curva da bola,
	/// ou o que a ponta pode escorrer de lado -- o que for MENOR. Recem-nascido (curto) ele vira como a bola;
	/// comprido, manda a ponta.
	/// </summary>
	public static float GiroDoRaio(float andadoPx, float comprimentoPx)
	{
		float daCurva = GiroDaBola(andadoPx);
		if (comprimentoPx < 1f) return daCurva;
		float daPonta = LadoPorFrente * andadoPx / comprimentoPx * (180f / MathF.PI);
		return MathF.Min(daCurva, daPonta);
	}

	/// <summary>
	/// O RUMO NOVO: <paramref name="rumo"/> virado na direcao de <paramref name="desejado"/> em no maximo
	/// <paramref name="maxGraus"/>, e preso ao leque em volta de <paramref name="saida"/>.
	/// </summary>
	/// <param name="saida">O rumo em que o tiro saiu; nulo = ele ainda nao saiu, e nao ha leque.</param>
	public static Vec2 Virar(Vec2 rumo, Vec2 desejado, Vec2 saida, float maxGraus)
	{
		if (desejado.LengthSquared < 1e-6f) return rumo;
		// SEM RUMO AINDA (a bola que nasce parada em volta do alvo e so agora comeca a cacar): aponta direto.
		if (rumo.LengthSquared < 1e-6f || SemLimiteDeTeste) return desejado.Normalized();

		float atual = MathF.Atan2(rumo.Y, rumo.X);
		float max = maxGraus * (MathF.PI / 180f);
		float novo = atual + Math.Clamp(Diferenca(MathF.Atan2(desejado.Y, desejado.X), atual), -max, max);

		if (saida.LengthSquared >= 1e-6f)
		{
			float daSaida = MathF.Atan2(saida.Y, saida.X);
			float leque = DesvioMaximoEmGraus * (MathF.PI / 180f);
			novo = daSaida + Math.Clamp(Diferenca(novo, daSaida), -leque, leque);
		}
		return new Vec2(MathF.Cos(novo), MathF.Sin(novo));
	}

	/// <summary>Quantos graus <paramref name="rumo"/> esta fora de <paramref name="saida"/>, de 0 a 180.</summary>
	public static float Desvio(Vec2 rumo, Vec2 saida)
	{
		if (rumo.LengthSquared < 1e-6f || saida.LengthSquared < 1e-6f) return 0f;
		return MathF.Abs(Diferenca(MathF.Atan2(rumo.Y, rumo.X), MathF.Atan2(saida.Y, saida.X))) * (180f / MathF.PI);
	}

	/// <summary>`a - b` pelo caminho curto, em radianos: de -pi (fora) a pi (dentro).</summary>
	private static float Diferenca(float a, float b)
	{
		float d = (a - b) % (2f * MathF.PI);
		if (d > MathF.PI) d -= 2f * MathF.PI;
		else if (d <= -MathF.PI) d += 2f * MathF.PI;
		return d;
	}
}
