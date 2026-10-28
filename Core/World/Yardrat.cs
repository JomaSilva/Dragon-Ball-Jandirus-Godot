namespace Jandirus.Core.World;

/// <summary>
/// ============================ O PLANETA YARDRAT, O SECRETO (dono, 2026-10-27, o pedido 109) ============================
/// O pedido dos jogadores: *"IT needs a npc in a yardrat planet as soon as possible"*. E a escolha do dono: *"escolhe um planeta em um
/// sistema proximo de namek pra ser yadrat, vai ter npcs dessa raça andando por la so q é um planeta "secreto" n aparece marcando no nav
/// system, vc tem q achar procurando pelos nomes dos planetas"*.
///
/// QUAL PLANETA: o do sistema de Namek -- o sistema ANCORADO no pre-feito (`SistemaSolar.Ancorado`, quatro orbitas) --, na ORBITA VIZINHA
/// a de Namek (a de fora; sendo Namek a ultima, a de dentro). Funcao pura da semente do mundo, como todo o resto do universo: o servidor e
/// o cliente chegam ao mesmo planeta sem pacote nenhum, e cada universo (cada wipe) tem o seu Yardrat num lugar.
///
/// O QUE MUDA NELE: o NOME (o disco no espaco e a zona da superficie se chamam "Yardrat" -- e e por ele que se acha o planeta, voando pelo
/// sistema de Namek e lendo os nomes); a CARTA ESTELAR e a TELA DO SISTEMA nao o desenham (<see cref="SistemaSolar.NaCarta"/>); e o
/// servidor o POVOA quando o chao nasce (`GameServer.Yardrat.cs`): habitantes Yardrat andando e o mestre que ensina o Instant Transmission
/// (tecla E). O chao e o do planeta gerado de sempre (a mesma semente).
/// ================================================================================================================
/// </summary>
public static class Yardrat
{
	/// <summary>O nome do planeta e da zona.</summary>
	public const string Nome = "Yardrat";

	/// <summary>O pre-feito em cujo sistema o Yardrat fica.</summary>
	public const string Vizinho = "Namek";

	/// <summary>Os moldes de NPC (`Assets/Data/npcs.json`): o povo e o mestre.</summary>
	public const string MoldeDoPovo = "habitante_yardrat", MoldeDoMestre = "mestre_yardrat";

	/// <summary>Quantos habitantes nascem com o chao.</summary>
	public const int Habitantes = 10;

	/// <summary>A chave do menu da tecla E do mestre, e o verbo de aprender.</summary>
	public const string Tipo = "Mestre_Yardrat", VerboAprender = "yardrat_aprender";

	/// <summary>A skill que o mestre ensina: o Instant Transmission (`shunkanido`).</summary>
	public const string SkillDoMestre = "/datum/skill/shunkanido";

	/// <summary>A orbita do Yardrat NESTE sistema, ou -1: so no sistema ancorado em Namek, a vizinha (a de fora; a de dentro se Namek e a ultima).</summary>
	public static int OrbitaEm(in SistemaSolar s)
	{
		if (!s.Ancorado || s.OrbitaPreFeita < 0 || s.Orbitas < 2 || s.PreFeito.Nome != Vizinho) return -1;
		return s.OrbitaPreFeita + 1 < s.Orbitas ? s.OrbitaPreFeita + 1 : s.OrbitaPreFeita - 1;
	}
}
