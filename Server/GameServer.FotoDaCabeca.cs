using Jandirus.Core.Combat;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-27) AS PORTAS DO ROBO DE FOTOS DA CABECA (`--diagcabeca`, o pedido 119): ferir um membro, derrubar, levantar e falar pelo corpo
/// do jogador, no processo do host. O corpo do pacote (`S2C.Corpo`) sai na hora; o nocaute chega ao cliente pela ficha, como em jogo. So
/// o robo as chama.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Poe este membro do corpo do jogador nesta fracao de vida, e manda o corpo ao dono.</summary>
	internal void FerirNaFoto(int id, string membro, double fracao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate?.Corpo.Achar(membro) is not { } m) return;
		m.Vida = m.VidaMax * fracao;
		pl.Combate.SincronizarVida();
		MandarCorpo(pl);
	}

	/// <summary>O corpo do jogador inteiro de novo, e de pe.</summary>
	internal void InteiroNaFoto(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return;
		if (pl.Ficha.KO) pl.Combate.Levantar();
		foreach (BodyPart m in pl.Combate.Corpo.Partes) m.Vida = m.VidaMax;
		pl.Combate.SincronizarVida();
		MandarCorpo(pl);
	}

	/// <summary>Derruba o corpo do jogador por este tanto (o nocaute cronometrado: a cura nao o levanta no meio da foto).</summary>
	internal void NocautearNaFoto(int id, double segundos)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Combate?.Nocautear(segundos);
	}

	/// <summary>O que o servidor diz do desmaio deste corpo agora.</summary>
	internal (bool Ko, bool PelaCabeca) DesmaioNaFoto(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null ? (pl.Ficha.KO, pl.Combate.DesmaioPelaCabeca) : (false, false);

	/// <summary>O corpo do jogador fala isto no say, como se o jogador tivesse digitado.</summary>
	internal void FalarNaFoto(int id, string texto)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		_ultimaFala.Remove(id);
		Falar(pl, Protocol.Fala.Diz, texto, digitada: true);
	}

	/// <summary>
	/// (2026-10-27, o pedido 127) A VERDADE DA BARRA VIDA, 0..1: a pior parte que derruba deste corpo AGORA, no servidor (`Body.VidaQueDerruba`)
	/// -- a regua do robo, que a regeneracao natural move uns decimos por segundo.
	/// </summary>
	internal double PiorNucleoNaFoto(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null
			? (Body.VidaQueDerruba(pl.Combate.Corpo.Partes.Select(p => (p.Nome, p.Fracao * 100, p.Decepado))) ?? 100) / 100
			: -1;
}
