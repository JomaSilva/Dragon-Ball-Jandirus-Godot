namespace Jandirus.Core.Combat;

/// <summary>
/// O IRRITADO -- o degrau de raiva ABAIXO da raiva comum (dono, 2026-10-21).
///
/// A palavra dele: *"vida abaixo de 50% ou cabeca ou tronco abaixo de 40% ou 2 ou mais membros
/// gravemente feridos (perder um membro e instantanea a raiva)"*. Aqui mora so a pergunta pura
/// ("este corpo, depois deste golpe, acende?"). Quem decide se o golpe ERA de inimigo numa luta
/// de verdade e o servidor (<c>GameServer.TalvezIrritar</c>): treino, spar, mente, Bills/Whis,
/// bicho e autodano ficam de fora la, e essa exclusao esta anotada pro dono confirmar.
///
/// "Gravemente ferido" nao tinha outro nome no jogo. O limiar que ja existe e o
/// <see cref="BodyPart.Quebrado"/> (<see cref="Regras.LimiarQuebra"/>, 20% da vida da parte).
/// Cabeca e tronco nao entram nessa conta: eles tem a porta dos 40%, separada.
/// Membro de fora = <see cref="Vitalidade.Membro"/> nao aninhado (braco, perna, rabo, reprodutor).
/// Mao e pe vao de carona no membro que os contem e nao contam duas vezes.
/// </summary>
public static class GatilhoDoIrritado
{
	/// <summary>Vida do corpo (<see cref="Stats.Fighter.HP"/>, media das partes, 0..100) abaixo disto acende.</summary>
	public const double VidaAbaixoDe = 50;

	/// <summary>Cabeca ou tronco (fracao da parte, 0..1) abaixo disto acende. "Abaixo" e estrito.</summary>
	public const double NucleoAbaixoDe = 0.40;

	/// <summary>Quantos membros de fora quebrados acendem, sem precisar perder nenhum.</summary>
	public const int MembrosGraves = 2;

	/// <summary>
	/// DEFEITO INJETADO (bancada <c>--irritadoteste</c>): apanhar nunca irrita -- o jogo de antes, sem este degrau.
	/// Lido por <see cref="Acende"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool NuncaAcendeDeTeste;

	/// <summary>Membro que o jogador perde ou quebra de verdade: nao aninhado (a mao nao conta alem do braco).</summary>
	public static bool EhMembroDeFora(BodyPart p) => p.Papel == Vitalidade.Membro && !p.Aninhado;

	/// <summary>
	/// ESTE CORPO ACENDE O IRRITADO por causa do golpe que acabou de cair?
	/// <paramref name="perdeuMembroAgora"/> e o instante: perder um membro acende sozinho, mesmo com a vida alta.
	/// Um membro que JA estava decepado nao reacende -- so o golpe que o arrancou.
	/// </summary>
	public static bool Acende(Body corpo, double hp, bool perdeuMembroAgora)
	{
		if (NuncaAcendeDeTeste) return false;
		if (perdeuMembroAgora) return true;
		if (hp < VidaAbaixoDe) return true;

		BodyPart? cabeca = corpo.Achar("Cabeca");
		BodyPart? tronco = corpo.Achar(Body.NomeDoTronco);
		if (Abaixo(cabeca) || Abaixo(tronco)) return true;

		int graves = 0;
		foreach (BodyPart p in corpo.Partes)
			if (EhMembroDeFora(p) && p.Quebrado) graves++;
		return graves >= MembrosGraves;

		static bool Abaixo(BodyPart? p) => p != null && !p.Decepado && p.Fracao < NucleoAbaixoDe;
	}
}
