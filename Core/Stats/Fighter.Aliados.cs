namespace Jandirus.Core.Stats;

/// <summary>
/// (2026-10-21) OS ALIADOS NA FICHA -- os habitantes que este personagem recrutou pela amizade (ver `Npc.Amizade` e `Npc.Aliados`).
/// Campo publico da ficha, entao vai pro save sozinho (`CharacterSave.Ficha`, `IncludeFields`): o aliado guardado volta no login, com o
/// nivel, a relacao e as melhorias -- e vai embora junto com o personagem quando ele e apagado, reencarnado ou consumido (a vaga
/// reciclada nasce com uma ficha nova, sem ninguem).
/// </summary>
public partial class Fighter
{
	/// <summary>Os aliados deste personagem, soltos ou guardados. Nulo ate o primeiro recrutamento -- save antigo nao carrega nada.</summary>
	public List<Jandirus.Core.Npc.Aliado>? Aliados;

	/// <summary>O numero do proximo aliado recrutado. Nunca volta atras: o aliado que morre nao empresta o numero ao seguinte.</summary>
	public int ProximoAliado = 1;
}
