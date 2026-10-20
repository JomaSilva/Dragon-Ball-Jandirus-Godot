namespace Jandirus.Core.Combat;

/// <summary>
/// ============================ O GOLPE NAO-LETAL NAO MATA (2026-10-19) ============================
/// O dono: *"modo nao letal ainda ta matando as vezes"*. A regra do `murderToggle` desligado tem DUAS metades, e as duas moram aqui
/// pra que os funis que as aplicam leiam o mesmo numero e o mesmo botao:
///
///   * O PISO (<see cref="Piso"/>): o golpe nao-letal nunca leva um membro abaixo dele -- logo abaixo da linha do nocaute, entao ele
///     derruba e nao mata. Quem o aplica: o `Body.Ferir` (todo golpe que passa pelo funil) e o `Espalhar` do servidor (o baque do
///     arremesso, o arremesso do agarrao, a cambalhota e a fome, que escrevem a vida direto).
///   * A PORTA DA MORTE: so o golpe LETAL mata. As duas portas da morte por golpe (`MeleeResolver.AplicarNoMembro` e o
///     `GameServer.ConsequenciaDoDano`) perguntam o corpo INTEIRO (`Body.DeveMorrer`), e o piso so segura o membro que o golpe pegou:
///     um nucleo que outra coisa ja tinha zerado fazia do soco nao-letal no braco o golpe que matava. O nao-letal que acha um corpo
///     assim so o derruba.
///
/// ARQUIVO PROPRIO de proposito: a bancada `--naoletalteste` le o piso e liga o botao, e ela compila sozinha contra o repo de antes do
/// conserto -- um campo novo num arquivo velho do Core a deixaria sem volta.
/// ==================================================================================================
/// </summary>
public static class NaoLetal
{
	/// <summary>
	/// O PISO DO GOLPE NAO-LETAL num membro de <paramref name="vidaMax"/>: 99% da linha de quebra (<see cref="Regras.LimiarQuebra"/>), e na
	/// ordem de conta que o `Body.Ferir` sempre fez -- a mesma vida da o mesmo piso, ate o ultimo bit.
	/// </summary>
	public static double Piso(double vidaMax) => vidaMax * Regras.LimiarQuebra * 0.99;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--naoletalteste`): o golpe nao-letal volta a matar o corpo que ja tinha um nucleo zerado -- o jogo de
	/// antes de 2026-10-19. Lido pelas duas portas da morte por golpe. Falso em jogo, sempre.
	/// </summary>
	public static bool MataPeloEstadoDeTeste;
}
