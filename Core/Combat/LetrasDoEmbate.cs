namespace Jandirus.Core.Combat;

/// <summary>
/// AS LETRAS DO QUICK TIME EVENT -- as dos quatro embates: o Zanzo Clash, a disputa de ki, a disputa de guarda e a danca
/// da fusao. O servidor sorteia daqui (`GameServer.SortearLetraDeEmbate`) e a tela do embate le daqui o que e resposta
/// (`ClashQte._UnhandledInput`): uma lista so, pras duas pontas nao discordarem sobre o que e letra.
///
/// ============================ SO W, A, S E D (dono, 2026-10-19) ============================
/// *"faça os quick time event do jogo usar apenas wasd e nao o teclado todo pq a galera ta reclamando muito"*. Ate esse
/// dia eram 22 letras ("BCEFGHIJKLMNOPQRTUVXYZ"), com o WASD de fora justamente por ser o andar: no meio de um teste de
/// reflexo o jogador tinha de achar a letra no teclado inteiro. Agora a mao fica onde ja esta.
///
/// O ANDAR NAO ATRAPALHA: em tres dos quatro embates o corpo esta plantado (`LocalPlayer.PorQueNaoAnda`, "num embate"),
/// entao a letra nao vira passo. O quarto -- a disputa de guarda, em que quem segura o raio com as maos pode sair de
/// lado -- sai de lado pelas SETAS (decisao do dono no mesmo dia, `LocalPlayer.TeclasDeAndarSemAsLetras`): ali o WASD e
/// so do quick time event.
/// ===========================================================================================
/// </summary>
public static class LetrasDoEmbate
{
	/// <summary>As letras que o embate pede, em maiuscula. Ver o cabecalho.</summary>
	public static string Letras => TecladoInteiroDeTeste ? TecladoInteiro : "WASD";

	/// <summary>Esta letra e resposta de embate? Maiuscula ou minuscula, tanto faz.</summary>
	public static bool EhLetra(char c) => Letras.IndexOf(char.ToUpperInvariant(c)) >= 0;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagmudez`, familia F1): o alfabeto de antes de 2026-10-19 -- as 22 letras do teclado
	/// inteiro, sem o WASD. Lido pela propria <see cref="Letras"/>, e por isso vale nas duas pontas: o servidor volta a
	/// sortear do teclado inteiro e a tela volta a mandar qualquer letra. Falso em jogo, sempre.
	/// </summary>
	public static bool TecladoInteiroDeTeste;

	/// <summary>O alfabeto de antes, so pro defeito injetado.</summary>
	private const string TecladoInteiro = "BCEFGHIJKLMNOPQRTUVXYZ";
}
