using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O KI DIVINO NO CLIENTE -- como o servidor contou por ultimo o do MEU personagem (`S2C.GodKi`), e o
/// aviso de que o de alguem por perto acabou de despertar (`S2C.GodKiDespertou`).
///
/// O QUE NAO MORA AQUI: se o LUGAR e divino. Isso o cliente tira sozinho da seed
/// (`Core.World.AtmosferaDivina.Tem`), como tira a gravidade e o bioma de um mundo gerado -- e por isso a
/// carta estelar marca os mundos divinos sem pedir nada. A regra esta em `Core/Forms/DespertarDivino.cs`;
/// quem desenha e a aba World (`MenuJogo.KiDivino.cs`) e o `World.KiDivino.cs`.
/// </summary>
public partial class GameClient
{
	/// <summary>O meu ki divino, como veio no ultimo `S2C.GodKi`.</summary>
	/// <param name="Exposicao">O medidor do despertar, de 0 a 100.</param>
	/// <param name="Maestria">A maestria divina, de 0 a 100 (zero pra quem nao despertou).</param>
	public readonly record struct KiDivinoVisto(Protocol.BitsDoGodKi Bits, int Exposicao, double Maestria)
	{
		public bool Desperto => (Bits & Protocol.BitsDoGodKi.Desperto) != 0;
		public bool AlcancaORequisito => (Bits & Protocol.BitsDoGodKi.AlcancaORequisito) != 0;
		public bool NaAtmosfera => (Bits & Protocol.BitsDoGodKi.NaAtmosfera) != 0;
	}

	public KiDivinoVisto KiDivino { get; private set; }

	/// <summary>O retrato do meu ki divino mudou. Quem escuta e o menu (o cartao da aba World).</summary>
	public event Action? KiDivinoMudou;

	/// <summary>
	/// O KI DIVINO DESPERTOU NUM CORPO DAQUI (`S2C.GodKiDespertou`): o id dele. Quem escuta e o `World`,
	/// que toca o som e abre o anel de luz em volta do corpo.
	/// </summary>
	public event Action<int>? GodKiDespertou;

	/// <summary>`S2C.GodKi`: guarda o retrato e avisa pelo <see cref="KiDivinoMudou"/>.</summary>
	private void LerGodKi(NetDataReader reader)
	{
		var bits = (Protocol.BitsDoGodKi)reader.GetByte();
		int exposicao = reader.GetByte();
		double maestria = reader.GetUShort() / 10.0;
		KiDivino = new KiDivinoVisto(bits, exposicao, maestria);
		KiDivinoMudou?.Invoke();
	}

	/// <summary>`S2C.GodKiDespertou`: so repassa -- o pacote e um acontecimento, e nao estado.</summary>
	private void LerGodKiDespertou(NetDataReader reader) => GodKiDespertou?.Invoke(reader.GetInt());
}
