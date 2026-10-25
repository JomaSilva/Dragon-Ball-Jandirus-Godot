namespace Jandirus.Core.Stats;

/// <summary>
/// A MAESTRIA DO INSTANT TRANSMISSION (dono, 2026-10-23) -- as contas moram em
/// <see cref="Jandirus.Core.Skills.TransmissaoEmNiveis"/>, e quem a sobe e o `GameServer.MaestriaDaTransmissao.cs`.
/// </summary>
public partial class Fighter
{
	/// <summary>
	/// DE 0 A 100 (%). SO SOBE USANDO: +2 a cada Instant Transmission que chega (o Yardrat +3), e nunca cai. Ela nao e o
	/// `teleskill` do DM (que cresce com a DISTANCIA e mexe no custo de antes): e o quanto o corpo ja pegou o jeito do gesto.
	/// Campo publico da ficha vai pro save sozinho (`IncludeFields`), e ficha de antes desta regra le 0 -- o jogo de antes.
	/// </summary>
	public double MaestriaDaTransmissao;
}
