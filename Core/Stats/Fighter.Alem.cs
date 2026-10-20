namespace Jandirus.Core.Stats;

/// <summary>
/// (2026-10-19) OS SERVICOS DO OUTRO MUNDO NA FICHA -- o servico em curso, as recargas e a rede de quem ja te conhece. As regras estao em
/// <see cref="Jandirus.Core.World.ServicosDoAlem"/>.
/// </summary>
public partial class Fighter
{
	/// <summary>
	/// A FICHA DE SERVICOS deste personagem. Campo publico da ficha, entao vai pro save sozinho (`CharacterSave.Ficha`, `IncludeFields`):
	/// o servico em curso sobrevive ao relog (o prazo continua correndo, como todo prazo deste port), e a recarga de cada um tambem.
	/// Nulo ate o primeiro servico -- save antigo e quem nunca morreu nao carregam nada.
	/// </summary>
	public Jandirus.Core.World.FichaDoAlem? ServicosNoAlem;
}
