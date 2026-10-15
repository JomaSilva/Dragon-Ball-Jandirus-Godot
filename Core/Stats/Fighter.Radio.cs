namespace Jandirus.Core.Stats;

/// <summary>
/// O RADIO DO SCOUTER NA FICHA -- so a frequencia sintonizada. As regras estao em
/// <see cref="Jandirus.Core.Social.RadioDoScouter"/>.
/// </summary>
public partial class Fighter
{
	/// <summary>
	/// EM QUE FREQUENCIA O SCOUTER DESTE PERSONAGEM ESTA. Campo publico da ficha, entao vai pro save sozinho
	/// (`CharacterSave.Ficha`, `IncludeFields`) e sobrevive ao relog -- e o liga/desliga do aparelho, que mora em
	/// `ServerPlayer.PoderesConcedidos`, tambem sobrevive desde a v0.3.0, pela copia `CharacterSave.ScouterLigado`.
	///
	/// NO PERSONAGEM E NAO NO ITEM: no DM o canal e `var/channel` do proprio `obj/items/Scouter`
	/// (`Tier 1.5.dm:328`), mas aqui a pilha da mochila e so id + quantidade (`Core/Items/Inventario.cs`) e
	/// nao tem onde guardar estado por aparelho. Quem perde o scouter e arruma outro continua na mesma
	/// frequencia, que e o que o jogador espera de uma preferencia.
	///
	/// Save antigo (sem o campo) nasce na de fabrica pelo inicializador; quem LE passa por
	/// <see cref="Jandirus.Core.Social.RadioDoScouter.Sanear"/>.
	/// </summary>
	public int FrequenciaDoRadio = Jandirus.Core.Social.RadioDoScouter.FrequenciaPadrao;
}
