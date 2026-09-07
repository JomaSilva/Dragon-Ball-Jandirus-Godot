using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// A GEOMETRIA DO FEIXE -- onde a cabeca para quando encosta em alguem, o que e TRONCO, e o que
/// acontece quando um feixe cruza o caminho do outro. Puro, sem servidor e sem Godot.
///
/// ============================ O PEDIDO DO DONO (2026-09-07), COM A FOTO ============================
/// *"ao colidirem a cabeca deles sempre devem ficar se empurrando na colisao, e isso tambem
/// acontece pra quando se chocar com alguem (atualmente a cabeca do beam fica SOBRE a pessoa e nao
/// NA FRENTE dela a empurrando e dando dano)"*; *"caso um jogador encoste no TRONCO de um beam e nao
/// necessariamente na cabeca, o beam vai ser CORTADO e a cabeca nova vai colidir com essa pessoa, e a
/// outra parte continua normalmente"*; *"no caso de 2 beams se CRUZAREM eles nao vao entrar em clash,
/// eles tem que vir de direcoes opostas, e o beam que bater no tronco do outro vai ficar PARADO ate o
/// outro sair do caminho"*.
///
/// Tres perguntas geometricas, e todas moram aqui porque as tres sao a mesma conta com o mesmo
/// numero: a cabeca tem um raio (<see cref="Projetil.RaioDeImpacto"/>) e o corpo tem uma meia
/// largura (<see cref="MeioCorpo"/>). "Encostar" e a soma das duas, em qualquer dos tres casos.
/// ===============================================================================================
///
/// ============================ O QUE E DO DM E O QUE E DO DONO ============================
///   * O CORTE do tronco e do DM: `Crossed(mob/M)` de `objects.dm:156-172` -- o segmento pisado some,
///     o de tras vira cabeca (`B.icon_state = "head"; B.density = 1; walk(B, ...)`) e vai dar o `Bump`
///     no mob; os segmentos da frente seguem viagem porque cada um anda sozinho. Aqui o feixe e um
///     objeto so, entao cortar e PARTIR o objeto em dois (ver `GameServer.Feixe.cs`).
///   * O CRUZAMENTO e do dono, e DIVERGE do DM de proposito: la `Crossed(obj/attack)` compara o poder
///     dos dois e apaga o mais fraco depois de um `sleep` (`objects.dm:173-185`). O dono pediu outra
///     coisa -- o que bate no tronco alheio ESPERA o tronco sair --, e e a que vale.
///   * A CABECA NA FRENTE e do dono; o DM nao tem a pergunta porque la o beam ocupa TILES, e "estar em
///     cima" e "estar na frente" sao o mesmo tile.
/// ==========================================================================================
/// </summary>
public static class Feixe
{
	/// <summary>
	/// A META LARGURA DE UM CORPO, em pixels: a caixa dos pes do <c>MoveRules</c> tem 16 px de largura,
	/// e a cabeca de um feixe encosta na BEIRADA dela, nao no centro.
	/// </summary>
	public const float MeioCorpo = 8f;

	/// <summary>
	/// A QUE DISTANCIA DO CENTRO DO CORPO A CABECA PARA: o raio dela mais a meia largura dele. E a
	/// unica conta de "encostar" deste arquivo, e as tres perguntas a usam.
	/// </summary>
	public const float DistanciaDeContato = Projetil.RaioDeImpacto + MeioCorpo;

	// =====================================================================
	// OS DEFEITOS INJETAVEIS DAS BANCADAS
	// =====================================================================
	/// <summary>DEFEITO INJETADO: a cabeca fica onde encostou, em cima do corpo -- a foto do dono.</summary>
	public static bool CabecaEmCimaDeTeste;

	/// <summary>DEFEITO INJETADO: a cabeca atravessa o tronco alheio em vez de esperar.</summary>
	public static bool AtravessaTroncoDeTeste;

	/// <summary>DEFEITO INJETADO: encostar no tronco nao corta nada.</summary>
	public static bool SemCorteDeTeste;

	// =====================================================================
	// A CABECA NA FRENTE
	// =====================================================================
	/// <summary>Onde a cabeca fica quando encosta neste corpo, vindo neste rumo: na frente dele.</summary>
	public static Vec2 CabecaNaFrenteDe(Vec2 corpo, Vec2 rumo) => corpo - rumo * DistanciaDeContato;

	// =====================================================================
	// DE FRENTE OU CRUZANDO
	// =====================================================================
	/// <summary>
	/// O OUTRO VEM CONTRA MIM? O rumo dele dentro de 45 graus do oposto do meu -- `R.dir != opp &amp;&amp;
	/// R.dir != turn(opp,45) &amp;&amp; R.dir != turn(opp,-45)` do gatilho por proximidade (`objects.dm:253`).
	/// `cos(135) = -0,707`. Fora disso os dois se CRUZAM, e cruzar nao e disputa.
	/// </summary>
	public static bool VemContra(Vec2 meuRumo, Vec2 rumoDele)
		=> meuRumo.X * rumoDele.X + meuRumo.Y * rumoDele.Y <= -0.7f;

	/// <summary>
	/// DUAS CABECAS QUE SE CRUZAM (sem vir de frente): quem espera e a mais fraca; empatadas, espera
	/// quem chegou por ultimo -- que e quem esta perguntando.
	/// </summary>
	public static bool EsperaNoCruzamento(double meuPoder, double poderDele) => meuPoder <= poderDele;

	// =====================================================================
	// O TRONCO
	// =====================================================================
	/// <summary>
	/// O TRONCO DE UM FEIXE: da cauda ate onde a cabeca deixa de ser cabeca. Devolve falso quando nao
	/// ha tronco (feixe curto demais, ou cauda ja em cima da cabeca).
	///
	/// A CABECA E MAIS COMPRIDA QUE O RAIO DELA, de proposito: quem esta a menos de
	/// <see cref="DistanciaDeContato"/> + <see cref="Projetil.RaioDeImpacto"/> da cabeca e assunto da
	/// CABECA (o corpo que ela ja esta empurrando fica exatamente a `DistanciaDeContato` do centro
	/// dela, e nao pode ser lido como "alguem encostou no tronco" e cortar o proprio feixe que o
	/// empurra). Um corpo nessa faixa que ainda nao foi acertado e alcancado pela cabeca no proximo
	/// sub-passo -- ela anda 16 px por vez.
	/// </summary>
	public static bool Tronco(Vec2 cauda, Vec2 cabeca, Vec2 rumo, out Vec2 fim)
	{
		fim = cabeca - rumo * (DistanciaDeContato + Projetil.RaioDeImpacto);
		Vec2 eixo = fim - cauda;
		return eixo.LengthSquared > 1f && eixo.X * rumo.X + eixo.Y * rumo.Y > 0;
	}

	/// <summary>
	/// ESTE PONTO ENCOSTA NO TRONCO? Distancia do ponto ao segmento do tronco menor ou igual ao raio.
	/// Devolve a projecao dele no eixo do feixe -- o lugar do corte.
	/// </summary>
	public static bool EncostaNoTronco(Vec2 cauda, Vec2 cabeca, Vec2 rumo, Vec2 ponto, float raio, out Vec2 projecao)
	{
		projecao = default;
		if (!Tronco(cauda, cabeca, rumo, out Vec2 fim)) return false;

		Vec2 eixo = fim - cauda;
		Vec2 ate = ponto - cauda;
		float t = (ate.X * eixo.X + ate.Y * eixo.Y) / eixo.LengthSquared;
		if (t < 0f || t > 1f) return false;

		projecao = cauda + eixo * t;
		return (ponto - projecao).LengthSquared <= raio * raio;
	}
}
