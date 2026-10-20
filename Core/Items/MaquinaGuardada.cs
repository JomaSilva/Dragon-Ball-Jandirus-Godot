namespace Jandirus.Core.Items;

/// <summary>
/// ============================ A MAQUINA RECOLHIDA LEVA AS MELHORIAS (dono, 2026-10-19) ============================
/// *"tem um bug no jogo q ao pegar um item q vc construiu e deu upgrade, tipo a maquina de gravidade etc (a drill isso n
/// acontece) ao vc re-instala-la em outro local, todos os upgrades sao removidos e vc tem q pagar todo o upgrade dnv"*.
///
/// A CAUSA: o "Pegar" (`GameServer.PegarObra`, e o `PegarNave` das naves) tirava a obra do chao e guardava na mochila o
/// id CRU do catalogo ("Gravity") -- um item sem estado, igual ao recem-fabricado. O estado morava na obra
/// (`Obra.Gravidade`, `Obra.Regenerador`, `Obra.Tech`, a `Nave.Velocidade`) e sumia com ela. A Drill escapava porque
/// guarda o nivel na FICHA de quem a recolhe (`Fighter.DrillsRecolhidas`), e continua assim.
///
/// AGORA A MAQUINA RECOLHIDA E UM ITEM COM ESTADO, como o livro e o recipiente de DNA (`TechDeBolso`): o id leva uma
/// GAVETA do cofre do servidor (`Gravity|c12|500x`), e a gaveta guarda o estado inteiro dela (`GameServer.MaquinaGuardada.cs`).
/// Assentada de novo, ela volta como era e a gaveta se esvazia. Trocar de mao, cair no chao, ir pro bau: o id vai
/// inteiro, e a maquina e a mesma -- a regra que o dono deu pros pesos em 2026-10-18 (*"o jogo acha q os dois sao o
/// mesmo item"*). O terceiro campo e so o ROTULO que a mochila mostra ("Gravity (500x)"): e ele que separa duas
/// maquinas do mesmo tipo na grade.
///
/// A MAQUINA SEM MELHORIA NENHUMA volta ao id cru: ela e igual a uma de fabrica.
/// ===================================================================================================================
/// </summary>
public static class MaquinaGuardada
{
	/// <summary>O teto do rotulo: o id inteiro tem de caber em <see cref="TechDeBolso.MaxDoId"/>.</summary>
	public const int MaxDoRotulo = 16;

	/// <summary>O rotulo da maquina que nao tem um numero que a separe das outras (e o do id que chegou sem rotulo).</summary>
	public const string RotuloComum = "com melhorias";

	/// <summary>O id da maquina guardada na gaveta <paramref name="gaveta"/>, com o rotulo que a mochila mostra.</summary>
	public static string Id(string tipo, int gaveta, string rotulo) =>
		TechDeBolso.Com(tipo, TechDeBolso.EscreverGaveta(gaveta), TechDeBolso.Limpo(rotulo, MaxDoRotulo));

	/// <summary>O tipo do catalogo de uma maquina guardada ("Gravity").</summary>
	public static string Tipo(string id) => TechDeBolso.Base(id);

	/// <summary>A gaveta do cofre que este id aponta; zero = nenhuma (o id cru).</summary>
	public static int Gaveta(string id) => TechDeBolso.Gaveta(TechDeBolso.Campo(id, 0));

	/// <summary>Este id e de uma maquina guardada com o estado dela?</summary>
	public static bool Eh(string id) => Ficha(id) != null;

	/// <summary>
	/// A FICHA: a da maquina de fabrica, com o id inteiro e o rotulo no nome -- nula quando o id nao e de uma (a porta do
	/// <see cref="CatalogoDeItens.Get"/>). O tipo tem de ser uma obra que se INSTALA: a capsula de companheiro tambem leva
	/// gaveta no primeiro campo (`Bola|c5|3`), e nao e maquina nenhuma.
	/// </summary>
	public static ItemDef? Ficha(string id)
	{
		if (id.Length > TechDeBolso.MaxDoId || Gaveta(id) <= 0) return null;
		if (CatalogoDeItens.Get(Tipo(id)) is not { } deFabrica
			|| Array.IndexOf(deFabrica.AcoesDoItem, CatalogoDeItens.AcaoPosicionar) < 0) return null;
		string rotulo = TechDeBolso.Campo(id, 1);
		return deFabrica with { Id = id, Nome = $"{deFabrica.Nome} ({(rotulo.Length > 0 ? rotulo : RotuloComum)})" };
	}
}
