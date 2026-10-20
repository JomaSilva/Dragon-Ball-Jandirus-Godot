using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) O DESCONTROLE DO FROST DEMON MUTANTE NA TELA -- a aura ameacadora vermelha colada no corpo (`fd_menacing_red`,
/// `IcerTransform.dm:166-169`) e o `chargeaura.wav` do instante em que o ki trava (`icer.dm:62-63`). O servidor diz o estado pelo
/// byte de marcas do `S2C.Forma` (`Protocol.MarcaDescontrolado`, lido pelo `GameClient.FrostDescontrolado`); aqui ele vira camada e
/// som. A cena vermelha da forma que nao se segura e outro bit do mesmo byte, e quem a escolhe e o `AoMudarForma`.
/// </summary>
public partial class World
{
	/// <summary>
	/// MARCA O DESCONTROLE NO BONECO deste corpo e, quando o pacote traz a VIRADA (`Protocol.MarcaViradaDoDescontrole`) com o ki
	/// travado, toca o `chargeaura` no lugar dele. So na virada: o pacote de estado de quem chega na zona tambem traz o descontrole, e
	/// o recem-chegado ouviria o estalo de uma coisa que aconteceu antes de ele chegar -- a mesma regra do `de == para` do
	/// <see cref="AoMudarForma"/>.
	/// </summary>
	private void MarcarODescontrole(int id, Node2D corpo)
	{
		if (corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis || GameClient.Instance is not { } cli) return;
		bool travado = cli.FrostDescontrolado(id);
		vis.MarcarDescontrole(travado);
		if (travado && cli.FrostDescontroleVirou(id) && Transformacao.CaminhoDoSom("chargeaura") is { } som)
			AudioDirector.EfeitoNoLugar(corpo, som, 0.9f);
	}

	/// <summary>
	/// A FICHA VISUAL DE QUEM ESTA JOGANDO -- a raca, o genero e a aparencia que o ultimo `PeerLook` dele trouxe (o `_looks`, que o
	/// servidor reescreve a cada `ReapresentarAparencia`). Pra a `TelaDeFormasDoFrost` abrir com os corpos de agora. Nulo antes do
	/// primeiro pacote.
	/// </summary>
	internal (string Raca, string Genero, Jandirus.Core.Appearance.Appearance Ap)? MeuLook =>
		GameClient.Instance is { } cli && _looks.TryGetValue(cli.LocalId, out var l) ? l : null;
}
