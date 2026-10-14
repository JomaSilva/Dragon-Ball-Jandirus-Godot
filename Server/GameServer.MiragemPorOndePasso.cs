namespace Jandirus.Server;

/// <summary>
/// A JANELA DA BANCADA DA MIRAGEM POR ONDE PASSO (`--diagmiragem`). Quem mede e o `Client/RoboDaMiragemPorOndePasso.cs`.
///
/// UMA PORTA SO, e ela nao anuncia nada por conta propria: liga a Zanzoken Afterimage pela funcao de producao
/// (<see cref="MiragensG14"/>), e quem anuncia as miragens e o relogio do lote (`TickG14` -> `AnunciarZanzo`), como em
/// jogo. O palco e a marca sao os da `--diagborrao` (<see cref="PalcoDoBorrao"/>, <see cref="PorNoPontoNaFotoDoBorrao"/>).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A ZANZOKEN AFTERIMAGE, sem o despacho do verb -- que e o mesmo de toda tecnica e nao e o que se mede. O Ki cheio
	/// e a recarga dos punhos zerada sao o que a bancada repete entre as rodadas (a mesma preparacao do
	/// <see cref="SaltarNaFotoDoBorrao"/>). Devolve se o efeito ficou ligado.
	/// </summary>
	internal bool LigarAsMiragensNaBancada(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		_prontoG3.Remove(id);
		MiragensG14(pl);
		return _miragensG14.ContainsKey(id);
	}
}
