using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-31, o pedido 198i) A MISSAO DA PATRULHA GALACTICA como o cliente a sabe (`S2C.MissaoDaPatrulha`): se este personagem e da
/// Patrulha, a missao de agora e o endereco do planeta dela na carta. Quem desenha e o botao "missão da Patrulha" da carta estelar
/// (`MenuJogo.Patrulha.cs`). Ver `GameServer.CargoDaPatrulha.cs`.
/// </summary>
public partial class GameClient
{
	/// <summary>O que o cliente sabe da missao: membro, a frase, e o endereco do planeta na carta (quando ele esta nela).</summary>
	public readonly record struct MissaoDaPatrulhaVista(bool Membro, bool TemMissao, string Texto, bool NaCarta, int Sx, int Sy, int Orbita, ulong Semente);

	public MissaoDaPatrulhaVista MissaoDaPatrulha { get; private set; }

	/// <summary>A missao mudou (o botao do Nav se refaz quando a aba e aberta).</summary>
	public event Action? MissaoDaPatrulhaMudou;

	private void LerMissaoDaPatrulha(NetDataReader reader)
	{
		bool membro = reader.GetBool();
		if (!membro) { PorAMissao(default); return; }
		if (!reader.GetBool()) { PorAMissao(new MissaoDaPatrulhaVista(true, false, "", false, 0, 0, 0, 0)); return; }
		string texto = reader.GetString();
		if (!reader.GetBool()) { PorAMissao(new MissaoDaPatrulhaVista(true, true, texto, false, 0, 0, 0, 0)); return; }
		int sx = reader.GetInt();
		int sy = reader.GetInt();
		int orbita = reader.GetByte();
		ulong semente = reader.GetULong();
		PorAMissao(new MissaoDaPatrulhaVista(true, true, texto, true, sx, sy, orbita, semente));
	}

	private void PorAMissao(MissaoDaPatrulhaVista m)
	{
		MissaoDaPatrulha = m;
		MissaoDaPatrulhaMudou?.Invoke();
	}
}
