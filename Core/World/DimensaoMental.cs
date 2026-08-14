namespace Jandirus.Core.World;

/// <summary>
/// ============================ A DIMENSAO MENTAL, COMO LUGAR ============================
/// *"a mecanica da MEDITACAO, onde vc podia meditar profundamente e enfrentar um CLONE seu na sua
/// mente, e se um player meditasse AO SEU LADO ele entraria na sua mente e poderia lutar com vc."*
///
/// Este arquivo e so a resposta a **"que lugar e este?"**. As regras de dentro (o clone, o
/// visitante, os chefes, a ferida que nao volta) moram em `GameServer.Mente.cs`; o que mora aqui e
/// o que as duas pontas precisam concordar sem se falar.
/// =====================================================================================
///
/// ============================ A CHAVE DA ZONA JA CARREGA O ANFITRIAO ============================
/// A mente e `ZoneKey.Interior("Interdimension", id)`: o NOME resolve a cena no catalogo (o mapa
/// Interdimension existe desde o clone) e a SEED **e o id do dono da mente**.
///
/// E e por isso que nao ha campo nenhum de "estou na mente de fulano". Havia uma pergunta parecida
/// no servidor -- `pl.CloneId != 0` -- e ela e uma **segunda verdade** que quebra em dois casos que
/// a Camada 2 acabou de criar:
///
///   * o VISITANTE nao tem clone (quando alguem entra na mente do outro, o reflexo se desfaz -- e
///     literalmente o `if(clone) del(C)` do `add_member`, `MindMeditate.dm:201-204`). Pelo campo
///     antigo ele "nao estava na mente", e o golpe que acordasse o corpo dele la fora o levaria pelo
///     caminho errado;
///   * o ANFITRIAO tambem fica sem clone assim que o visitante chega.
///
/// A zona nao tem esse problema: quem esta la dentro **esta la dentro**, com clone ou sem.
/// ==========================================================================================
/// </summary>
public static class DimensaoMental
{
	/// <summary>
	/// O nome da cena. E o mesmo mapa branco do original (`turf/MindFloor`, `'White.dmi'`), que
	/// neste port ja existia no catalogo desde o primeiro clone.
	/// </summary>
	public const string Zona = "Interdimension";

	/// <summary>Esta zona e uma mente? So `Interior` com este nome pode ser.</summary>
	public static bool EhAMente(ZoneKey z) =>
		z.Kind == ZoneKey.KindInterior && string.Equals(z.Name, Zona, StringComparison.Ordinal);

	/// <summary>
	/// A MENTE DE QUEM. Derivado da seed da chave -- ver o cabecalho.
	///
	/// Zero pra qualquer zona que nao seja uma mente, e o zero e util: ele e a resposta a
	/// "este lugar tem dono?" sem exigir que o chamador pergunte duas coisas.
	/// </summary>
	public static int Anfitriao(ZoneKey z) => EhAMente(z) ? (int)z.Seed : 0;

	/// <summary>A mente DESTA pessoa. UM bolso por dono, e o mesmo bolso em toda entrada.</summary>
	public static ZoneKey De(int anfitriaoId) => ZoneKey.Interior(Zona, (ulong)anfitriaoId);

	/// <summary>
	/// A MESMA MENTE? Compara o bolso e nao o dono -- e a pergunta que o visitante faz, e a resposta
	/// tem que ser verdadeira pra ele mesmo estando na mente de outra pessoa.
	/// </summary>
	public static bool MesmaMente(ZoneKey a, ZoneKey b) => EhAMente(a) && a.Hash == b.Hash;
}
