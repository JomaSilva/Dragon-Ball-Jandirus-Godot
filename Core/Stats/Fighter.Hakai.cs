namespace Jandirus.Core.Stats;

public partial class Fighter
{
	/// <summary>
	/// **APAGADO POR UM DEUS DA DESTRUICAO** -- `mob/var/hakai_mark` (`Ranks/GodOfDestruction.dm:58-59`).
	///
	/// Nao e "esta morto" (isso e o <see cref="dead"/>): e COMO se morreu, e serve a uma regra so -- o Enma cobra
	/// `GOD_HAKAI_REVIVE_MULT` vezes o preco pra reconstruir a alma (`NPCs/SkyNPCs.dm:189`; o numero e o
	/// <see cref="Ranks.KitDoDeus.HakaiPrecoDoRevive"/>).
	///
	/// QUEM A ESCREVE e o Hakai que MATOU (`GameServer.KitDoDeus.cs`); QUEM A APAGA e a volta a vida, por qualquer
	/// porta (`hakai_mark = 0` no `ReviveMe`, `Death.dm:148` -- aqui o `CombatState.Reviver`).
	///
	/// PERSISTE de graca (a ficha inteira vai pro disco, ver `CharacterSave.Ficha`), e tem que persistir pelo mesmo
	/// motivo do <see cref="aged_out"/>: se morresse no logout, deslogar no Outro Mundo seria o jeito barato de
	/// pagar metade.
	/// </summary>
	public bool hakai_mark;
}
