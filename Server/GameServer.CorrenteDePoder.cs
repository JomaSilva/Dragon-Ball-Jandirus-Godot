using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ A CORRENTE DE PODER DO DU -- pedido do dono, 2026-10-19 ============================
/// *"coloque um efeito q tinha no E:\Users\Joao\Desktop\DU-SOURCE-master quando da poder pro amigo"*.
///
/// O DU desenha a doacao como um FLUXO (`Code/Combat/Skills.dm:704-802`, o `Give_Power`): enquanto `Giving_Power` esta de pe, um
/// laco solta a cada `sleep(1)` -- 0,1 s -- um `missile()` com o `Give Power.dmi` (uma espiral verde translucida de 32 px, cinco
/// quadros a 0,1 s) do doador ate o alvo (`Missile(O,src,M)`, `Code/Misc/Unsorted.dm:42-43`), na camada logo acima dos corpos
/// (`O.layer = layer+1`). Nao ha overlay, aura nem som: e so a corrente de bolinhas voando de um pro outro.
///
/// O port ja tinha a doacao inteira (`GameServer.Tecnicas.G11.cs`, o `givepower.dm` do Finale: 1% do Ki por dose, a cada 0,2 s) e
/// nao desenhava nada -- o `MandarEfeito("dando_poder")` ia so pra tela de quem doa, e nenhum `case` do cliente o lia. (O Finale
/// tinha o seu proprio `missile('Spirit.dmi',src,M)`, `givepower.dm:46`, que tambem nunca foi portado: o dono escolheu o do DU.)
///
/// O FIO: a cada dose o servidor diz a quem esta perto "fulano da poder a beltrano" (`S2C.DandoPoder`), e cada pacote e um FOLEGO
/// da corrente -- o cliente a mantem viva um instante depois do ultimo (`Client/CorrenteDePoder.cs`). Quem chega no meio da doacao
/// ve a corrente no folego seguinte, e quando a doacao acaba ela para sozinha: sem pacote de fim e sem estado pra lembrar.
/// ======================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (robo `--diagcorrente`): o folego vai so pra tela de quem DA, como o `MandarEfeito` de antes -- quem esta em
	/// volta nao ve nada. Lido pela <see cref="AnunciarACorrenteDePoder"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool CorrenteSoProDoadorDeTeste;

	/// <summary>
	/// UM FOLEGO DA CORRENTE, a cada dose (`DoseDaDoacaoG11`): pra quem esta na zona e a uma tela de uma das duas pontas -- a corrente
	/// cruza a tela de quem esta perto de qualquer uma delas.
	/// </summary>
	private void AnunciarACorrenteDePoder(ServerPlayer doador, ServerPlayer alvo)
	{
		var w = Protocol.Begin(Protocol.S2C.DandoPoder);
		w.Put(doador.Id);
		w.Put(alvo.Id);

		float raio2 = RaioDaVista * RaioDaVista;
		foreach (ServerPlayer o in ZoneList(doador.Zone.Hash))
		{
			if (o.Peer == null || (CorrenteSoProDoadorDeTeste && o != doador)) continue;
			if ((o.Pos - doador.Pos).LengthSquared > raio2 && (o.Pos - alvo.Pos).LengthSquared > raio2) continue;
			o.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}
}
