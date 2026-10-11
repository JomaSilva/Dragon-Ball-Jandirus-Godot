using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// OS MARCOS QUE O PODER DA -- o `processpoints()` do original (`Stats/Level/scaling.dm:1-13`).
///
/// ============================ O DEFEITO DO PLAYTEST, E A CAUSA ============================
/// *"Jogadores foram de 10 a 1000 de BP sem ganhar nenhum marco."* Nao havia checagem falhando: NAO HAVIA
/// CHECAGEM. O port deu tres marcos no nascimento (`GameServer.Skills.MarcosIniciais`) e nunca portou a
/// conversao de BP em marco -- os unicos caminhos que chamavam `SkillBook.Conceder` eram o nascimento, o
/// admin, o desejo ao dragao e o renascimento do bio-androide. O proprio port anotava isso em tres lugares
/// (`MoldeDeNpc.cs`: "o acumulo do jogador AINDA NAO EXISTE neste port").
///
/// ============================ A REGRA DO ORIGINAL ============================
/// ```
/// unAIDSpoints = totalskillpoints - (admingibbedpoints + wishedpoints)   // os NATURAIS
/// pointgain    = ((1+0.466)**unAIDSpoints) / skillpointMod
/// spcap        = round(40 + (10*skillpointMod)) + gifts
/// if(totalskillpoints &lt; spcap)  if(BP &gt;= round(pointgain,1))  totalskillpoints += 1
/// ```
/// O limiar do proximo marco cresce 46,6% a cada marco natural, a raca o divide (`skillpointMod`: Humano
/// 1,5; Saiyajin 1,2; Gray 0,5...), e quem compara e o BP BASE. La o personagem nasce com 1 e a funcao da
/// UM por chamada (ela roda a cada passo); com mod 1 sao 7 marcos aos 10 de BP e 19 aos 1000.
///
/// ============================ O QUE MUDA NA TRADUCAO, E POR QUE ============================
///   * O PORT NASCE COM 3, e nao com 1. Os tres sao o ponto inicial do DM mais os dois primeiros limiares
///     (BP 1 e BP 2), que todo personagem ja cruza ao nascer -- por isso a conta daqui comeca no TERCEIRO
///     marco natural (<see cref="JaNoNascimento"/>) e os totais batem com os do original: 7 aos 10, 19 aos 1000.
///   * TODOS OS LIMIARES CRUZADOS DE UMA VEZ (<see cref="Devidos"/>), e nao um por chamada: quem salta de BP
///     (um Zenkai, um desejo, um save antigo) recebe na hora o que cruzou. Foi o "padrao" pedido pelo dono:
///     *"BP &gt;= limiar e ainda nao concedido; se o BP pular varios limiares, todos sao concedidos --
///     inclusive pra quem ja passou deles"*.
///   * O CONTADOR E PROPRIO (`SkillBook.MarcosPorBp`: quantos o BP ja deu), e nao a subtracao do original. La
///     os naturais sao "o total menos os do admin e os do desejo", e o port nao guarda os do admin -- a
///     subtracao daria marco a menos pra todo personagem que um admin presenteou. O contador nasce zero em
///     save antigo, e e isso que faz a concessao ser retroativa.
///   * O TETO (`spcap`) vale pros naturais, e nao pro total: presente de admin e desejo nao comem a escada.
/// ===========================================================================================
/// </summary>
public static class MarcosDeBp
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--arvoreteste`): o BP nao rende marco nenhum -- o jogo de antes de
	/// 2026-10-10. Lido pela propria regra, em <see cref="Devidos"/>.
	/// </summary>
	public static bool BpNaoDaMarcoDeTeste;

	/// <summary>`(1+0.466)` -- quanto o limiar cresce a cada marco natural (`scaling.dm:3`).</summary>
	public const double Passo = 1.466;

	/// <summary>
	/// Os marcos naturais que o personagem ja tem ao nascer neste port: os tres do `MarcosIniciais`, que
	/// fazem as vezes do ponto inicial do DM e dos limiares de BP 1 e BP 2.
	/// </summary>
	public const int JaNoNascimento = 3;

	/// <summary>
	/// O BP que da o proximo marco a quem ja tem `naturais`. E o `round(pointgain,1)` de `scaling.dm:11`:
	/// 3, 5, 7, 10, 15, 21, 31, 46, 67, 99, 144... pra `mod` 1.
	/// </summary>
	public static double Limiar(int naturais, double mod) =>
		DmMath.Round(Math.Pow(Passo, naturais) / (mod > 0 ? mod : 1), 1);

	/// <summary>Quantos marcos naturais cabem numa vida: o `spcap` de `scaling.dm:4` (50 pra `mod` 1).</summary>
	public static int Teto(double mod) => (int)DmMath.Round(40 + 10 * (mod > 0 ? mod : 1), 1);

	/// <summary>
	/// QUANTOS MARCOS ESTE BP JA MERECE E AINDA NAO RECEBEU. `jaConcedidos` e quantos o BP ja deu a este
	/// personagem (`SkillBook.MarcosPorBp`); `mod` e o `skillpointMod` da raca.
	/// </summary>
	public static int Devidos(double bp, int jaConcedidos, double mod)
	{
		if (BpNaoDaMarcoDeTeste) return 0;

		int naturais = JaNoNascimento + Math.Max(jaConcedidos, 0), teto = Teto(mod), devidos = 0;
		while (naturais + devidos < teto && bp >= Limiar(naturais + devidos, mod)) devidos++;
		return devidos;
	}
}
