using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-18) AS NAVES NO CLIENTE -- o tanque da nave que eu vejo e a nave que eu observo de fora, como o servidor os contou por ultimo.
/// Quem desenha a barra e o `PainelDeCombustivel`; quem esconde o corpo e segue a nave e o `LocalPlayer` com o `World`. A regra mora no
/// servidor (`GameServer.Combustivel.cs`, `GameServer.NaveObservar.cs`).
/// </summary>
public partial class GameClient
{
	/// <summary>
	/// O TANQUE QUE EU VEJO: <c>Modo</c> 0 = nenhum (a barra some), 1 = estou ao leme, 2 = estou observando; o nome da nave; o nivel, a
	/// capacidade e o que a ultima volta de um segundo gastou. Ver `Protocol.S2C.Combustivel`.
	/// </summary>
	public readonly record struct TanqueVisto(byte Modo, string Nave, float Nivel, float Capacidade, float PorSegundo);

	public TanqueVisto Tanque { get; private set; }

	/// <summary>`S2C.Combustivel`: guarda. A barra le por quadro (sem assinatura pra vazar), como a faixa da mineracao.</summary>
	private void LerCombustivel(NetDataReader reader)
	{
		byte modo = reader.GetByte();
		string nave = reader.GetString(64);
		float nivel = reader.GetFloat();
		float capacidade = reader.GetFloat();
		float porSegundo = reader.GetFloat();
		Tanque = new TanqueVisto(modo, nave, nivel, capacidade, porSegundo);
	}

	/// <summary>
	/// A NAVE QUE EU OBSERVO DE FORA: o id dela (0 = nenhuma), o tipo (a arte do casco) e quem esta ao leme (0 = ninguem). Ver
	/// `Protocol.S2C.Observando`.
	/// </summary>
	public readonly record struct NaveObservada(int Nave, string Tipo, int Piloto);

	public NaveObservada Observando { get; private set; }

	/// <summary>Estou observando uma nave de fora agora?</summary>
	public bool Observador => Observando.Nave != 0;

	/// <summary>A observacao comecou, acabou, ou o leme da nave observada trocou de mao.</summary>
	public event Action? ObservacaoMudou;

	/// <summary>`S2C.Observando`: guarda e avisa.</summary>
	private void LerObservacao(NetDataReader reader)
	{
		int nave = reader.GetInt();
		string tipo = reader.GetString(48);
		int piloto = reader.GetInt();
		Observando = new NaveObservada(nave, tipo, piloto);
		ObservacaoMudou?.Invoke();
	}
}
