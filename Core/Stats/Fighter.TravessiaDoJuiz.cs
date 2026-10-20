namespace Jandirus.Core.Stats;

/// <summary>
/// (2026-10-19) A VOLTA DO KING YEMMA: o lugar EXATO do mundo dos vivos de onde ele atravessou pro Outro Mundo
/// (`GameServer.TravessiaDoJuiz.cs`). Campo publico da ficha vai pro save sozinho -- o juiz que sai do jogo do lado de la volta
/// pro mesmo lugar quando entrar de novo e apertar a travessia. Zona vazia = nenhuma volta guardada (e e assim que a ficha antiga
/// le).
///
/// TRES PARTES DA ZONA, e nao o nome: o nome e o mesmo pra toda nave e pra todo mundo sorteado homonimo -- a mesma razao da
/// `CharacterSave.ZonaTipo`.
/// </summary>
public partial class Fighter
{
	/// <summary>O nome da zona da volta ("" = nenhuma volta guardada).</summary>
	public string VoltaDoJuizZona = "";

	/// <summary>O tipo da zona da volta (`ZoneKey.Kind`).</summary>
	public byte VoltaDoJuizTipo;

	/// <summary>A semente da zona da volta (`ZoneKey.Seed`: o mundo sorteado, ou o id do interior).</summary>
	public ulong VoltaDoJuizSeed;

	/// <summary>O ponto da volta, em pixels da zona.</summary>
	public float VoltaDoJuizX, VoltaDoJuizY;
}
