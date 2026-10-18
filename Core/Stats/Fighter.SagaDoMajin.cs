namespace Jandirus.Core.Stats;

/// <summary>
/// (2026-10-16) A SAGA DO CORRUPTED MAJIN, LADO DA FICHA -- os `mob/var` de `Magic/MajinSaga.dm:15-25` que *"persist across
/// logout"*, e a sanidade do sistema do amigo do dono. As regras moram em `Races.SagaDoMajin`; quem escreve e o
/// `GameServer.SagaDoMajin.cs`. Todos vao pro disco com a ficha (o save serializa os campos publicos).
/// </summary>
public partial class Fighter
{
	/// <summary>`majin_saga_form`: 0 base, 1 o Kai absorvido, 2 a furia, 3 a outra metade devorada, 4 os tres jogadores.</summary>
	public int majin_saga_form;

	/// <summary>`majin_kai_absorbed`: o primeiro passo ja foi dado (um Kai so conta uma vez).</summary>
	public bool majin_kai_absorbed;

	/// <summary>`majin_form3_players`: quantos JOGADORES a forma 3 ja absorveu, rumo a forma 4.</summary>
	public int majin_form3_players;

	/// <summary>`majin_pure_unlocked`: a Forma Pura destravou -- e com ela o sistema inteiro da sanidade.</summary>
	public bool majin_pure_unlocked;

	/// <summary>
	/// A SANIDADE, de 0 a 100 -- do sistema do amigo do dono, sem nada no DM. Nasce cheia; so anda depois da Forma Pura.
	/// Vai pro disco: sair do jogo nao cura a loucura de ninguem.
	/// </summary>
	public double majin_sanidade = Races.SagaDoMajin.SanidadeCheia;
}
