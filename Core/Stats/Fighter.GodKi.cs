namespace Jandirus.Core.Stats;

/// <summary>
/// O PEDACO DA FICHA QUE O DESPERTAR DO KI DIVINO ESCREVE. As regras moram em
/// `Core/Forms/DespertarDivino.cs`; aqui so mora o que tem de ir pro save (a ficha e gravada inteira).
/// </summary>
public sealed partial class Fighter
{
	/// <summary>
	/// A EXPOSICAO AO KI DIVINO, de 0 a 100 -- o medidor do DESPERTAR. Enche ficando num mundo de
	/// atmosfera divina e trocando golpes com quem ja tem ki divino; aos 100 o ki divino desperta
	/// (`DespertarDivino.Despertar`). Nao zera ao sair do mundo, e atravessa o logout.
	///
	/// ============================ NO DM ELA E A `godki.energy` -- AQUI NAO PODE SER ============================
	/// O original usa o mesmo campo pras duas vidas do datum: antes do despertar a `godki.energy` e o que
	/// se junta ate 100 (`godkiattain.dm:23` e `:33`), e depois dele vira o TANQUE de energia divina que o
	/// uso gasta (`godki.dm:96-108`). Neste port o tanque nasce CHEIO (`GodKiState.energy = 100`), e meia
	/// duzia de bancadas monta um `new GodKiState { ... }` na mao: ler o medidor de la daria um corpo que
	/// "ja se expos por inteiro" no instante em que o objeto existe. Entao o medidor tem campo proprio,
	/// fora do `GodKiState` -- que alem disso so passa a existir no despertar.
	/// ========================================================================================================
	/// </summary>
	public double ExposicaoDivina;
}
