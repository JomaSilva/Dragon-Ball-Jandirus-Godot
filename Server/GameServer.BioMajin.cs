using Jandirus.Core.Races;

namespace Jandirus.Server;

/// <summary>
/// ============================ O BIO-ANDROIDE COM SANGUE MAJIN TEM AS DUAS ABSORCOES (2026-10-24) ============================
/// O relato do dono: *"tem um bug q bio androide com dna majin so consegue absorver igual um majin e nao absorver fo bio androide, ai n
/// consegue evoluir"*. Desde a v0.3.0 o `CorpoDeMajin` inclui o bio com sangue Majin (`SangueDoBio`), e o `absorver` perguntava a ele
/// quem sela: o bio de DNA Majin SELAVA sempre -- e, sem Buu Absorb, so ouvia a recusa -- e nunca bebia, entao a escada de marcos
/// (`BioAndroids.Andar`) nao andava. A decisao do dono: *"Os dois, em botoes separados"* -- o "Absorver" do bio-androide e SEMPRE a
/// absorcao dele (bebe e evolui), e o bio com sangue Majin ganha o "Absorver como Majin", que sela pelas MESMAS portas do Majin
/// (`AbsorverMajin`: a skill Buu Absorb, nocauteado e vivo ao lado, a recarga, a mente, a copia do Split Form).
///
/// O `CorpoDeMajin` continua decidindo o resto (o se desfazer em vez de morrer, `SeDesfazAoMorrer`; o nao sangrar): so o VERB mudou.
///
/// QUEM JA EXISTE sai consertado sem passo de login (*"n esqueca de consertar quem ja crio bio"*): o roteamento le a raca, gravada
/// desde sempre, e o defeito nao deixou nada errado no save -- o poder emprestado do selo (`majin_absorb_bp`) e zerado na saida e no
/// login (`DesfazerAAbsorcaoAoSair`, `AcordarSemAbsorcao`); a roupa e as tecnicas emprestadas (`LookDoMajin`, `VerbosAbsorvidos`) so
/// vivem na memoria; o `AbsorbDeterminesBP` que o selo ligava nao pesa com o `AbsorbBP` em zero (`Fighter.Power`), e a primeira
/// absorcao de bio o liga de qualquer jeito; e a escada (`bio_stage`, `bio_marco`, `bio_passos`) o selo nunca tocou -- ela so estava
/// parada, e anda na primeira absorcao. A familia 4c da `--bioteste` passa um bio assim pelo save e pelo login.
/// =================================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--bioteste`, familia 4c): o "Absorver" do bio com sangue Majin volta a SELAR, como da v0.3.0 a
	/// v0.4.4. Lido pelo <see cref="SelaPeloAbsorver"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool BioMajinSelaPeloAbsorverDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--bioteste`, familia 4c): o "Absorver como Majin" deixa de conferir o sangue -- o Majin de verdade sela
	/// por ele tambem. Lido pelo <see cref="AbsorverComoMajin"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool AbsorverComoMajinSemSangueDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--bioteste`, familia 4c): a larva de sangue Majin sela pelo "Absorver como Majin" (como selava pelo
	/// "Absorver" ate a v0.4.4). Lido pelo <see cref="AbsorverComoMajin"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool LarvaSelaComoMajinDeTeste;

	/// <summary>A recusa de quem aperta o "Absorver como Majin" sem o sangue -- a `--verbsteste` a procura (a recusa da linha na tabela).</summary>
	private const string RecusaDoAbsorverComoMajin = "só o bio-androide com sangue Majin absorve como Majin.";

	/// <summary>
	/// O "ABSORVER" DESTE CORPO SELA? So o do Majin (e o do meio-Majin, `AbsorcaoMajin.EhMajin`): o bio-androide BEBE, com o sangue que
	/// tiver -- o selo dele e o <see cref="AbsorverComoMajin"/>.
	/// </summary>
	private static bool SelaPeloAbsorver(ServerPlayer pl) =>
		CorpoDeMajin(pl) && (BioMajinSelaPeloAbsorverDeTeste || !SangueDoBio.EhBio(pl.Ficha));

	/// <summary>
	/// "ABSORVER COMO MAJIN" (`absorver_majin`): so o bio com sangue Majin, e por dentro e o verb do Majin, com todas as portas dele. O
	/// Majin de verdade sela pelo "Absorver" e nao tem este botao (`RequisitosDosVerbos`).
	/// </summary>
	private void AbsorverComoMajin(ServerPlayer pl)
	{
		if (!AbsorverComoMajinSemSangueDeTeste && !SangueDoBio.Carrega(pl.Ficha, "Majin"))
		{ Avisar(pl, RecusaDoAbsorverComoMajin); return; }

		// A LARVA NAO ABSORVE, nem selando: a regra do dono vale pras duas absorcoes (*"A forma de barata do bio androide n consegue
		// absorver"*) -- a mesma frase do `AbsorverBio`.
		if (!LarvaSelaComoMajinDeTeste && BioAndroids.EmLarva(pl.Ficha))
		{ Avisar(pl, "a carapaça larval não tem os órgãos de absorção: amadureça primeiro (enterre-se)."); return; }

		AbsorverMajin(pl);
	}
}
