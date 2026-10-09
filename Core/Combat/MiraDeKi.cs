using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>
/// A MIRA DE UM ATAQUE DE KI -- pra onde o tiro sai quando quem atira tem alguem MARCADO.
///
/// ============================ O PEDIDO DO DONO (2026-10-09) ============================
/// *"ao ter um target e vc usar um ataque de ki o seu personagem ai virar pra direcao do seu target e usar
/// o ataque de ki na direcao do target, mas se o target se mover o seu beam ou ataque de ki nao vao se
/// curvar pra nova direcao do target"*; *"se o inimigo estiver na sua diagonal, vc vai soltar o beam nessa
/// diagonal, mas se ele se mover o beam continua indo na mesma direcao"*.
///
/// DIVERGENCIA DECLARADA DO DM: la todo tiro sai pra onde o corpo OLHA -- `A.dir=src.dir` (`beams.dm:136`),
/// `A.dir=usr.dir` (`blasts.dm:71`), `A.dir = dir` (`customattacks.dm:510`) -- e o corpo so olha pra oito
/// lados (neste port, quatro: <see cref="Facing"/>). Marcar alguem nao mudava o tiro em nada: quem estava na
/// diagonal do inimigo tinha que ANDAR ate uma fileira dele pra poder acerta-lo. Aqui o alvo marcado e a
/// mira -- o corpo vira pro lado dele (o mais perto dos quatro) e o tiro sai no rumo EXATO, em qualquer
/// angulo.
///
/// O QUE NAO MUDOU: sem ninguem marcado o tiro sai pra onde o corpo olha, como sempre. E a mira e so do
/// NASCIMENTO: depois de solto o tiro nao acompanha ninguem -- a nao ser o teleguiado, e ele so ate onde a
/// curva dele deixa (<see cref="Teleguiado"/>).
///
/// O QUE SE PERDEU: atirar pra um lado com outro alguem marcado. Quem quer o tiro "pra onde eu olho" solta
/// a marca antes.
/// =======================================================================================
/// </summary>
public static class MiraDeKi
{
	/// <summary>
	/// DEFEITO INJETADO (bancada): o tiro sai pra onde o corpo olha mesmo com alguem marcado -- o alvo na
	/// diagonal ve o raio passar reto pela fileira ao lado. E o jogo de antes de 2026-10-09, e o DM. Falso em
	/// jogo, sempre.
	/// </summary>
	public static bool TiroSoPraFrenteDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o olhar que o tiro cravou nao segura o pacote de movimento que ja estava a
	/// caminho -- o corpo vira pro alvo e desvira no pacote seguinte, com o olhar de antes do tiro. Falso em
	/// jogo, sempre.
	/// </summary>
	public static bool OlharSoltoDeTeste;

	/// <summary>
	/// POR QUANTO TEMPO O CORPO FICA ENCARANDO O TIRO, em milissegundos: e o `usr.icon_state="Blast"` com
	/// `spawn(3) usr.icon_state=""` do disparo de bola (`blasts.dm:82-83`) -- tres decimos de pose. Nesse
	/// prazo o olhar e de quem atirou e nao do teclado: sem ele, o pacote de movimento que ja estava a caminho
	/// (com o olhar de ANTES do tiro) desviraria o corpo um quadro depois de o servidor vira-lo.
	/// </summary>
	public const int OlharDoTiroMs = 300;

	/// <summary>
	/// O TIRO SAI NO MARCADO? Verdadeiro com o rumo exato e o lado pra onde o corpo vira. Falso = nao ha mira
	/// (ele esta em cima de mim), e quem chama cai no olhar de sempre.
	/// </summary>
	public static bool NoAlvo(Vec2 atirador, Vec2 alvo, Facing olhar, out Vec2 rumo, out Facing novoOlhar)
	{
		rumo = default;
		novoOlhar = olhar;
		if (TiroSoPraFrenteDeTeste) return false;

		Vec2 d = alvo - atirador;
		// EM CIMA DELE (menos de um pixel): nao ha rumo pra tirar da diferenca.
		if (d.LengthSquared < 1f) return false;

		rumo = d.Normalized();
		novoOlhar = MoveRules.FacingFrom(d, olhar);
		return true;
	}
}
