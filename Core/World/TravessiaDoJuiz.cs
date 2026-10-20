namespace Jandirus.Core.World;

/// <summary>
/// ============================ O KING YEMMA VAI E VOLTA DO OUTRO MUNDO -- pedido do dono, 2026-10-19 ============================
/// *"faca com q king yemma possa ter uma skill q permite ir e voltar do outro mundo pra poder reviver as pessoas la, ao voltar do
/// outro mundo ele volta pro exato local onde estava no mundo dos vivos"*.
///
/// No DM o kit do cargo (`OtherworldRanks.dm:125-129`) da o `Judge` -- cujo verb (`Go_To_Heaven_Or_Hell`) leva o juiz ao Ceu ou
/// ao Inferno e nunca de volta -- e o `Revive`, que pede um morto COLADO. Um juiz vivo nao tinha como chegar aos mortos (eles
/// chegam na mesa do Enma, no Outro Mundo) nem como voltar de la. Esta e a travessia que faltava: a ida guarda o lugar EXATO de
/// onde ele saiu, e a volta o devolve la. A regra (quem pode, de onde, e o que acontece se o lugar sumiu) mora no
/// `GameServer.TravessiaDoJuiz.cs`; aqui ficam so os nomes que as duas pontas dividem.
/// ==========================================================================================================================
/// </summary>
public static class TravessiaDoJuiz
{
	/// <summary>A chave do cargo que atravessa (`Core/Ranks/Ranks.cs`): o King Yemma.</summary>
	public const string Cargo = "yemma";

	/// <summary>O id que viaja pelo canal de habilidade (`C2S.Habilidade`); a chave do botao na tabela de requisitos e `hab:` + ele.</summary>
	public const string Verbo = "juiz_outro_mundo";

	/// <summary>O nome do botao (aba Skills) -- o mesmo na tabela de requisitos e no cliente.</summary>
	public const string Rotulo = "Outro Mundo (ir e voltar)";
}
